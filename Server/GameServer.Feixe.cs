using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O FEIXE ENCOSTANDO NO MUNDO -- o lote do dono de 2026-09-07, com a foto do Kamehameha sentado em
/// cima da vitima em vez de na frente dela.
///
/// Quatro regras, e as quatro sao consequencia de UMA decisao da camada 1: o feixe e um objeto so
/// (cabeca + cauda), e nao o trem de segmentos do DM. Onde o DM resolve por ocupacao de tile, aqui
/// se resolve por geometria (<see cref="Feixe"/>):
///
///   1. A CABECA PARA NA FRENTE de quem ela encosta (<see cref="PlantarACabecaNaFrenteDe"/>) -- e
///      continua na frente enquanto empurra: o corpo e a cabeca andam o mesmo delta.
///   2. QUEM ENCOSTA NO TRONCO CORTA O FEIXE (<see cref="CortarOndeEncostaram"/>): a parte de ca
///      ganha cabeca nova na frente de quem encostou, a parte de la vira outro feixe e segue.
///   3. O QUE BATE NO TRONCO ALHEIO ESPERA (<see cref="TroncoAlheioNoCaminho"/>): cruzar nao e
///      disputa; a cabeca fica parada ate o tronco sair do caminho.
///   4. APANHAR COM O RAIO NA MAO (<see cref="AoLevarGolpeComRaioNaMao"/>): fora de disputa o raio
///      cai na hora; dentro dela o golpe pesa no medidor, e ninguem agarra quem esta disputando.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1) A CABECA NA FRENTE
	// =====================================================================
	/// <summary>
	/// ONDE A CABECA FICA quando encosta num corpo: <see cref="Feixe.DistanciaDeContato"/> antes do centro
	/// dele, no rumo do feixe. Chamado do `Acertar` de raio -- o mesmo instante em que a cabeca vira
	/// `Encostado` -- e do corte, que e um `Acertar` num lugar novo.
	///
	/// ============================ POR QUE NAO BASTAVA O `Colidiu` ============================
	/// O `Colidiu` diz "encostou" quando o centro da cabeca chega a 16 px do centro do corpo -- ou
	/// seja, quando a cabeca ja esta EM CIMA dele, que e o que a foto do dono mostra. O teste de
	/// contato continua sendo esse (ele e o que faz a cabeca que anda 16 px por sub-passo nao pular
	/// ninguem); o que muda e onde ela PARA depois de encostar. Recuar a cabeca aqui, em vez de mudar
	/// o raio do teste, mantem uma pergunta so pra "encostou?" e uma so pra "onde fica".
	///
	/// O ARRASTO NAO PRECISA DE NADA: o corpo anda o mesmo delta que a cabeca (`ArrastarComOFeixe`),
	/// entao a distancia entre os dois e a que foi plantada aqui, ate o ciclo seguinte replanta-la.
	/// ====================================================================================
	/// </summary>
	private static void PlantarACabecaNaFrenteDe(Projetil p, ServerPlayer alvo)
	{
		if (Feixe.CabecaEmCimaDeTeste) return;
		// COM O ALCANCE DESTE TIRO (dono, 2026-09-23): a `Beam3` para a 24 px do centro, o Final Flash a
		// 136 -- a frente desenhada na beirada do corpo, seja qual for a folha e a escala. Pelo eixo do proprio
		// feixe e nunca atras da mao -- ver `Feixe.CabecaNaFrenteDe`.
		p.Pos = Feixe.CabecaNaFrenteDe(alvo.Pos, alvo.Altitude, p);
	}

	// =====================================================================
	// 2) O CORTE
	// =====================================================================
	/// <summary>
	/// ALGUEM ENCOSTOU NO TRONCO DESTE FEIXE? Corta ali. Chamado do tique, depois do avanco, pra cada
	/// raio vivo -- uma varredura da lista da zona por feixe com tronco, que e a mesma ordem de custo
	/// do `Colidiu`.
	///
	/// ============================ QUEM CORTA E QUEM NAO CORTA ============================
	///   * o DONO nao corta o proprio feixe (`if(M != proprietor)` do `Bump`; um raio nasce da mao dele
	///     e o tronco passa por cima dos pes dele o tempo todo);
	///   * quem a cabeca ja esta LEVANDO nao corta (esta na frente dela, nao no tronco -- e o
	///     <see cref="Feixe.Tronco"/> ja deixa a faixa da cabeca de fora, ver la);
	///   * o intocavel nao corta (area de espera, cena);
	///   * um feixe EM DISPUTA nao se corta: a cabeca dele esta plantada no ponto de encontro e o
	///     tronco e o cabo de guerra. Partir esse feixe soltaria a cabeca do dono no meio do embate,
	///     e o embate e quem manda nele ate acabar. Fica anotado como decisao.
	/// UM CORTE POR FEIXE POR TIQUE: a parte de la ja e outro feixe, e ela e testada no tique
	/// seguinte como qualquer outro.
	/// =====================================================================================
	/// </summary>
	private void CortarOndeEncostaram(Projetil p, List<ServerPlayer> corpos, List<Projetil> lista, ulong zona)
	{
		if (Feixe.SemCorteDeTeste) return;
		if (!p.Vivo || p.Tipo != TipoDeProjetil.Beam || p.Inerte || p.EmEmbate) return;
		float alcance = Feixe.AlcanceDaCabeca(p);
		if (!Feixe.Tronco(p.Cauda, p.Pos, p.Rumo, alcance, out _)) return;

		foreach (ServerPlayer o in corpos)
		{
			if (o.Id == p.Dono || o.Combate == null || o.Combate.Intocavel) continue;
			if (o.Id == p.Arrastando) continue;
			// quem desviou nao corta o trecho que ele mesmo desviou (a dobra fica colada nele) -- `Projetil.Desviador`
			if (p.Desviador != 0 && o.Id == p.Desviador && !Feixe.RamoPegaQuemDesviouDeTeste) continue;
			// quem raspou neste feixe saiu da linha dele: o tronco passa sem ser cortado (no DM o corpo esta um tile
			// ao lado, e so corta se voltar a pisar nele) -- `Projetil.ForaDaLinha`
			if (p.EstaForaDaLinha(o.Id)) continue;
			if (!Voo.PodeAcertar(Voo.Andar(p.Altitude), Voo.Andar(o.Altitude))) continue;
			// A BEIRADA DO TRONCO, e nao o eixo (2026-09-23): o corpo encosta quando a meia largura dele alcanca a
			// meia espessura DESENHADA do feixe. Com o raio fixo de 16 px, um corpo 80 px ao lado do eixo de um
			// Final Flash (dentro dos 108 px do desenho dele) nao cortava nada. O piso e o raio de antes: os
			// feixes finos continuam cortando como cortavam.
			float beirada = MathF.Max(Projetil.RaioDeImpacto, Feixe.MeiaEspessuraDoTronco(p) + Feixe.MeioCorpo);
			if (!Feixe.EncostaNoTronco(p.Cauda, p.Pos, p.Rumo, alcance, o.Pos, beirada, out Vec2 projecao)) continue;

			Cortar(p, o, projecao, lista, zona);
			return;
		}
	}

	/// <summary>
	/// PARTE O FEIXE EM DOIS onde <paramref name="alvo"/> encostou.
	///
	/// E o `Crossed(mob)` do DM (`objects.dm:156-172`) traduzido pra um feixe que e um objeto so:
	///
	///   * A PARTE DE LA (do outro lado do corpo ate a cabeca velha) vira OUTRO feixe, solto -- ninguem
	///     a alimenta, ela voa com o comprimento que tem (o ramo "solto e voando" do rastro) e carrega
	///     tudo o que a cabeca velha ja era: quem ela estava moendo ou levando, a disputa que ja teve,
	///     o alcance que lhe sobrava. No DM os segmentos da frente simplesmente continuam andando.
	///   * A PARTE DE CA (a cauda ate o corte) fica sendo ESTE objeto, porque e nele que o canal do
	///     dono (`CanalDeKi.Raio`) e o rastro alimentado pela mao apontam: a cabeca nova nasce na
	///     frente de quem encostou, e o `Acertar` roda nela no mesmo instante -- e o `Bump` que a
	///     cabeca nova do DM da ao andar.
	///
	/// O ALCANCE DA PARTE DE CA VOLTA o que a cabeca recuou: `AndouTiles` e "a que distancia da mao a
	/// cabeca esta", e e ele que decide arremesso, arrasto e `mods` -- uma cabeca nova a 4 tiles da mao
	/// arremessa como uma cabeca a 4 tiles, e nao como a de 20 que ela era.
	///
	/// A PARTE DE LA NASCE PELO MESMO CAMINHO DE TODO TIRO (a lista da zona, o teto, o `Nasceu` no fio),
	/// e depois um `Cortou` diz ao cliente onde as duas cabecas e caudas estao AGORA -- sem ele, a
	/// interpolacao do cliente arrastaria a cabeca velha de volta ate o corte por um quarto de segundo.
	/// Se nao houver vaga na zona pra parte de la, ela simplesmente nao existe (o corte engole o que
	/// estava alem do corpo) -- e a unica saida em que o teto de tiros nao vira teto de corpos.
	/// </summary>
	private void Cortar(Projetil p, ServerPlayer alvo, Vec2 projecao, List<Projetil> lista, ulong zona)
	{
		Vec2 cabecaVelha = p.Pos;
		// A MAO DA PARTE DE LA NASCE FORA DO CORPO DE QUEM CORTOU (2026-09-23): a cauda e onde o cliente estampa
		// o `origin`/`end`, CENTRADO, e ele tem o tamanho da cabeca da folha. Com a cauda na beirada do corpo, a
		// arte da mao cobria quem cortou -- e num Final Flash, 128 px de mao em cima dele. E o DM tambem: o
		// segmento do tile pisado some e o proximo comeca UM TILE adiante (`objects.dm:156-172`).
		Vec2 caudaDeLa = projecao + p.Rumo * (Feixe.MeioCorpo + Feixe.AlcanceDaCabeca(p));

		Projetil? deLa = null;
		bool cabe = lista.Count < MaxProjeteisPorZona && _projeteisVivos < MaxProjeteisNoMundo;
		if (cabe && (cabecaVelha - caudaDeLa).Length > Projetil.RaioDeImpacto)
		{
			deLa = new Projetil
			{
				Id = _proximoProjetil++,
				Dono = p.Dono, Tipo = p.Tipo, Pos = cabecaVelha, Cauda = caudaDeLa, Rumo = p.Rumo,
				Distancia = p.Distancia, MaxDistancia = p.MaxDistancia, RangeMod = p.RangeMod,
				ModsBase = p.ModsBase, Bp = p.Bp, MultDeOnda = p.MultDeOnda, BaseDano = p.BaseDano, MaxDano = p.MaxDano,
				Letal = p.Letal, Deflectivel = p.Deflectivel, Piercer = p.Piercer, Fisico = p.Fisico,
				Paralisia = p.Paralisia, Empurra = p.Empurra, Altitude = p.Altitude, EstouraNoImpacto = p.EstouraNoImpacto,
				SegundosPorTile = p.SegundosPorTile, Pressa = p.Pressa, Acumulado = p.Acumulado, VidaRestante = p.VidaRestante,
				Canalizando = false,
				Encostado = p.Encostado, AteMoerDeNovo = p.AteMoerDeNovo, Arrastando = p.Arrastando,
				BatendoSemFerir = p.BatendoSemFerir,
				Esvaziando = p.Esvaziando, FimPendente = p.FimPendente, JaDisputou = p.JaDisputou,
				Nome = p.Nome, Arte = p.Arte, EscalaVisual = p.EscalaVisual, Invisivel = p.Invisivel,
				UltimoSulco = p.UltimoSulco,
				NascidoDoCorte = p.Id,
				// quem raspou no feixe inteiro continua fora da linha da parte de la: ela e o mesmo raio, e ele pode
				// estar parado justamente nela (copia, e nao a mesma lista: cada parte esquece por conta propria)
				ForaDaLinha = p.ForaDaLinha == null ? null : [.. p.ForaDaLinha],
			};
			lista.Add(deLa);
			_projeteisVivos++;
			AnunciarProjetil(zona, Protocol.ProjetilSub.Nasceu, deLa);
		}

		// A PARTE DE CA: a cabeca nova, na frente de quem encostou. O que a cabeca velha era (quem ela
		// moia, quem levava, o esvaziamento) foi embora com a parte de la; esta cabeca esta comecando.
		p.Pos = Feixe.CabecaNaFrenteDe(alvo.Pos, alvo.Altitude, p);
		p.Distancia += (cabecaVelha - p.Pos).Length / ZoneCollision.TileSize;
		p.Arrastando = 0;
		p.Encostado = false;
		p.BatendoSemFerir = 0;
		p.AteMoerDeNovo = 0;
		p.Esvaziando = false;
		p.FimPendente = FimDeProjetil.Nenhum;

		AnunciarCorte(zona, p, deLa);
		GD.Print($"[server] FEIXE CORTADO: {alvo.Name} encostou no tronco de {p.Nome} (#{p.Id}) -- "
				 + (deLa != null ? $"a parte de la segue como #{deLa.Id}" : "a parte de la nao coube na zona"));

		Acertar(p, alvo);
	}

	/// <summary>
	/// O `Cortou` no fio: onde a cabeca da parte de ca esta agora e onde a cauda da parte de la
	/// comecou -- os dois pontos que o cliente NAO deve interpolar. Ver <see cref="Protocol.ProjetilSub.Cortou"/>.
	/// </summary>
	private void AnunciarCorte(ulong zona, Projetil deCa, Projetil? deLa)
	{
		var w = Protocol.Begin(Protocol.S2C.Projetil);
		w.Put((byte)Protocol.ProjetilSub.Cortou);
		w.Put(deCa.Id);
		w.PutVec(deCa.Pos);
		w.Put(deLa?.Id ?? 0);
		w.PutVec(deLa?.Cauda ?? deCa.Pos);
		foreach (ServerPlayer o in ZoneList(zona))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// 3) O CRUZAMENTO
	// =====================================================================
	/// <summary>
	/// A CABECA DE `p`, indo pra <paramref name="nova"/>, ESBARRA NO TRONCO de um feixe de OUTRO dono?
	/// Entao ela espera -- `Projetil.Esperando` -- ate o tronco sair do caminho. Mesmo andar so: um
	/// raio que passa por cima do outro nao encosta nele.
	///
	/// O DM apagava o mais fraco (`Crossed(obj/attack)`, `objects.dm:173-185`); o dono pediu a espera,
	/// e a espera e mais simples: nada morre, nada muda de dono, e o tronco sai sozinho -- quando o
	/// outro feixe acaba (a cauda dele e engolida) ou quando o outro dono solta.
	/// </summary>
	private bool TroncoAlheioNoCaminho(Projetil p, Vec2 nova, List<Projetil> lista)
	{
		if (Feixe.AtravessaTroncoDeTeste) return false;
		float minhaFrente = Feixe.AlcanceDaCabeca(p);
		Vec2 euNaTela = nova + Feixe.Subida(p.Altitude);
		foreach (Projetil q in lista)
		{
			if (ReferenceEquals(q, p) || !q.Vivo || q.Tipo != TipoDeProjetil.Beam || q.Inerte || q.Dono == p.Dono) continue;
			if (!Feixe.PodemSeTocar(p.Altitude, q.Altitude)) continue;
			// A MINHA FRENTE encosta na BEIRADA do tronco dele (a meia espessura desenhada), no plano desenhado: o
			// raio do teste e o alcance da minha cabeca mais a meia espessura do tronco dele, e a faixa da cabeca
			// DELE (que fica de fora do tronco) e a do alcance dela. Com o tronco no eixo, uma cabeca de
			// Kamehameha entrava 92 px no tronco de um Final Flash antes de esperar.
			Vec2 subida = Feixe.Subida(q.Altitude);
			if (Feixe.EncostaNoTronco(q.Cauda + subida, q.Pos + subida, q.Rumo, Feixe.AlcanceDaCabeca(q), euNaTela,
									  minhaFrente + Feixe.MeiaEspessuraDoTronco(q), out _)) return true;
		}
		return false;
	}

	/// <summary>
	/// A CABECA DE `p`, indo pra <paramref name="nova"/>, ENCONTRA DE FRENTE a cabeca de um feixe de OUTRO
	/// dono, e a disputa nao comecou (o `TentarEmbateDeFeixes` ja foi tentado antes, no mesmo sub-passo)?
	/// Entao a regra de <see cref="Feixe.SemDisputa"/>: alimentado contra solto, o alimentado segue e
	/// EMPURRA; em qualquer outro caso, `p` espera encostado. Devolve verdadeiro quando `p` tem que esperar.
	///
	/// A cabeca que ja esta NUMA DISPUTA com um terceiro nao e empurrada: quem manda na posicao dela e o
	/// tique do embate, e `p` espera como esperaria um tronco.
	/// </summary>
	private bool CabecaDeFrenteNoCaminho(Projetil p, Vec2 nova, List<Projetil> lista)
	{
		if (Feixe.AtravessaDeFrenteDeTeste) return false;
		foreach (Projetil q in lista)
		{
			if (ReferenceEquals(q, p) || !q.Vivo || q.Tipo != TipoDeProjetil.Beam || q.Inerte || q.Dono == p.Dono) continue;
			// A MESMA PERGUNTA DO GATILHO DA DISPUTA (`Feixe.DeFrenteNoCaminho`, que ja confere se os dois andares
			// podem se tocar): o que ele nao pegou, cai aqui.
			if (!Feixe.DeFrenteNoCaminho(p, nova, q)) continue;

			if (q.EmEmbate || Feixe.SemDisputa(p.Canalizando, q.Canalizando) == Feixe.DeFrente.Espera) return true;
			EmpurrarCabecaSolta(q, nova + Feixe.Subida(p.Altitude), Feixe.Contato(p, q));
		}
		return false;
	}

	/// <summary>
	/// A CABECA SOLTA RECUA NA FRENTE DA ALIMENTADA: ela volta PELO PROPRIO EIXO ate a frente dela encostar na
	/// de <paramref name="minhaCabecaNaTela"/> (medido no plano desenhado). O rastro dela encolhe; se a cabeca
	/// passar da cauda, o feixe acabou -- engolido, como o do perdedor de uma disputa (`Defletido`).
	///
	/// PELO EIXO DELA, e nao "na frente da minha": com as duas linhas desencontradas de lado, a versao que punha
	/// a cabeca dela em `minha - rumo * contato` a teleportava pra MINHA linha, com a cauda na dela -- um feixe
	/// torto. So a distancia ao longo do rumo dela muda.
	///
	/// O QUE ELA CARREGAVA CAI: uma cabeca empurrada pra tras nao esta mais encostada em quem moia, nem
	/// levando ninguem -- o mesmo que o corte faz com a parte de ca.
	/// </summary>
	private void EmpurrarCabecaSolta(Projetil q, Vec2 minhaCabecaNaTela, float contato)
	{
		Vec2 ate = minhaCabecaNaTela - Feixe.NaTela(q);
		float anda = ate.X * q.Rumo.X + ate.Y * q.Rumo.Y - contato;
		if (anda >= 0) return;   // ja esta encostada (ou longe): nada a empurrar
		Vec2 nova = q.Pos + q.Rumo * anda;
		Vec2 corpo = nova - q.Cauda;
		if (corpo.X * q.Rumo.X + corpo.Y * q.Rumo.Y <= Projetil.RaioDeImpacto)
		{
			q.Pos = nova;
			Matar(q, FimDeProjetil.Defletido);
			return;
		}
		q.Distancia -= (q.Pos - nova).Length / ZoneCollision.TileSize;
		q.Pos = nova;
		q.Arrastando = 0;
		q.Encostado = false;
	}

	/// <summary>
	/// A CABECA DE `p` CRUZA COM A CABECA de outro feixe SEM vir de frente (de frente e disputa -- ver
	/// `TentarEmbateDeFeixes`)? A mais fraca espera (<see cref="Feixe.EsperaNoCruzamento"/>). Sem esta
	/// regra dois raios perpendiculares que se encontram cabeca com cabeca ficariam um por cima do outro.
	/// </summary>
	private bool CabecaAlheiaNoCaminho(Projetil p, Vec2 nova, List<Projetil> lista)
	{
		if (Feixe.AtravessaTroncoDeTeste) return false;
		Vec2 euNaTela = nova + Feixe.Subida(p.Altitude);
		foreach (Projetil q in lista)
		{
			if (ReferenceEquals(q, p) || !q.Vivo || q.Tipo != TipoDeProjetil.Beam || q.Inerte || q.Dono == p.Dono) continue;
			if (!Feixe.PodemSeTocar(p.Altitude, q.Altitude)) continue;
			float toque = Feixe.Toque(p, q);
			if ((Feixe.NaTela(q) - euNaTela).LengthSquared > toque * toque) continue;
			if (Feixe.VemContra(p.Rumo, q.Rumo)) continue;   // de frente e assunto da disputa, nao daqui
			// A CABECA PRESA NUMA DISPUTA NAO SAI DO CAMINHO: quem manda nela e o embate, e ela nao espera
			// ninguem -- entao quem chega de lado espera, seja qual for o poder. Sem isto um terceiro feixe
			// mais forte estacionava por cima da cabeca de um encontro.
			if (q.EmEmbate) return true;
			if (Feixe.EsperaNoCruzamento(p.PoderDeEmbate(), q.PoderDeEmbate())) return true;
		}
		return false;
	}

	// =====================================================================
	// 4) APANHAR COM O RAIO NA MAO
	// =====================================================================
	/// <summary>
	/// APANHOU (soco, tiro, arremesso, agarrao) COM UM RAIO NA MAO. O pedido do dono, nas duas metades:
	/// *"caso o personagem seja atacado, agarrado ou qualquer coisa desse tipo, ele cancela o beam dele
	/// na hora"* e *"atacar um personagem que ta usando beam em colisao faz ele sofrer DESVANTAGEM no
	/// clash dele"*.
	///
	/// E UM FUNIL SO, de proposito: soco (`Atacar`), tiro (`Acertar`), arremesso (`Arremessar`) e agarrao
	/// (`Prender`) chamam este metodo, e e ele quem decide entre DERRUBAR o raio e PESAR no medidor.
	/// Cada chamador decidindo por conta propria seria o dia em que um deles esquece a disputa e
	/// derruba o raio de quem esta no meio de um Kamehameha contra Kamehameha.
	///
	/// O `KB` do DM (`beams.dm:73-74`, `if(KB) stopbeaming()`) so derrubava por ARREMESSO; o dono
	/// estendeu pra qualquer golpe que encoste, e a disputa e a excecao que ele mesmo escreveu.
	/// </summary>
	private void AoLevarGolpeComRaioNaMao(ServerPlayer alvo, ServerPlayer? agressor)
	{
		if (_emEmbateDeKi.TryGetValue(alvo.Id, out DisputaDeKi? d)) { PenalizarNoEmbate(d, alvo, agressor); return; }
		DerrubarRaioPorGolpe(alvo.Id);
	}

	/// <summary>
	/// O GOLPE PESA NO MEDIDOR: o OUTRO lado ganha <see cref="EmbateDeKi.ApertosQueUmGolpeCusta"/> apertos
	/// de graca, com a vantagem dele. Nao e um segundo caminho de fisica -- e um aperto a mais no cabo de
	/// guerra que ja existe, e a fisica do tique e quem o consome.
	/// </summary>
	private void PenalizarNoEmbate(DisputaDeKi d, ServerPlayer golpeado, ServerPlayer? agressor)
	{
		LadoDeKi oOutro = d.A.Quem.Id == golpeado.Id ? d.B : d.A;
		oOutro.ApertosPendentes += EmbateDeKi.ApertosQueUmGolpeCusta;
		Avisar(golpeado, agressor != null
			? $"o golpe de {agressor.Name} abala sua firmeza na disputa!"
			: "o golpe abala sua firmeza na disputa!");
		Avisar(oOutro.Quem, $"{golpeado.Name} vacila -- o encontro pende pro seu lado!");
	}

	/// <summary>
	/// NAO DA PRA AGARRAR QUEM ESTA NUMA COLISAO DE KI -- *"(nao da pra agarrar quem esta em colisao,
	/// mas se nao estiver da pra agarrar o usuario do beam)"*. A metade do "se nao estiver" e o
	/// <see cref="AoLevarGolpeComRaioNaMao"/> chamado pelo `Prender`.
	/// </summary>
	private bool RecusarAgarrarQuemDisputa(ServerPlayer quemAgarra, ServerPlayer alvo)
	{
		if (!_emEmbateDeKi.ContainsKey(alvo.Id)) return false;
		Avisar(quemAgarra, $"{alvo.Name} esta numa colisao de ki -- nao da pra agarrar quem esta preso nela.");
		return true;
	}
}
