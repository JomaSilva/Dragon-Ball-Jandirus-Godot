using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA DA VOZ LOCAL -- `--vozteste` ============================
/// O corte da voz e uma afirmacao sobre o que **nao** sai do servidor, e nao ha foto disso. Um sistema
/// de voz que vaza soa exatamente igual a um que nao vaza -- a diferenca so aparece pra quem le os
/// pacotes, e essa pessoa nunca e o dono do jogo. E o mesmo formato de defeito do sigilo do BP (a API
/// do corte ficou orfa por meses, com o numero vazando por sete lugares) e da barra de vida.
///
/// Entao a bancada pergunta ao <see cref="GameServer.QuemOuveAVoz"/> -- **o funil de verdade**, e nao
/// uma copia da regra -- com corpos de verdade, mapas de verdade e distancias de verdade.
///
/// ============================ AS SEIS SECOES ============================
///  1. O ALCANCE -- 22 tiles, o MESMO do chat local; e quem esta a 23 nao recebe byte nenhum.
///  2. A ZONA -- planeta, mente e interior de nave se separam sozinhos, sem `if`.
///  3. A PAREDE -- e ela sai do `.vis` (o que CEGA) e nao do `.col` (o que BLOQUEIA).
///  4. O TETO DE QUATRO -- e quem cai e sempre o mais longe.
///  5. A TORNEIRA -- 50 quadros por segundo, com rajada, e o quadro grande demais.
///  6. O `Calada` -- calar alguem cala a voz dela, pela mesma marca do texto.
/// =====================================================================
///
/// ============================ O QUE ESTA BANCADA **NAO** PROVA ============================
/// Que o pacote chegou no fio. Corpo forjado nao tem `Peer`, e pacote que saiu nao volta pra ser
/// conferido -- e a mesma limitacao que o `Avisar` ja declara. O que ela prova e o CORTE, que e onde
/// mora a regra; a entrega e um `Peer.Send` de uma linha, logo abaixo do corte, guardado so por
/// `Peer != null`.
///
/// E ela nao prova NADA sobre o som: abafada, volume e posicao sao do cliente. Quem mede aquilo e a
/// `--diagvoz` (`Client/RoboDeVoz.cs`), e ela mede em AMOSTRAS -- energia em 3 kHz contra energia em
/// 300 Hz --, porque som nao se fotografa e uma constante conferida nao prova nada.
///
/// E ela nao prova que ELA MESMA sabe olhar: uma checagem que conta zero fica verde tambem quando nada
/// estava sendo mandado. Quem poe o DEFEITO na frente de cada regra e exige a linha vermelha e a
/// `--vozdupla` (`GameServer.VozDuplaTeste.cs`), com dois corpos de verdade.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	private const int IdBaseDaVoz = 91_400;

	private int _vozOk, _vozFalhou;

	private void AfirmarVoz(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _vozOk++; GD.Print($"[voz]   OK    {oque}"); return; }
		_vozFalhou++;
		GD.PrintErr($"[voz]   FALHA {oque}   {detalhe}");
	}

	/// <summary>Um quadro de voz de mentira -- 40 bytes, o tamanho medido de um quadro Opus real.</summary>
	private static readonly byte[] QuadroFalso = new byte[40];

	public void RodarBancadaDaVoz()
	{
		_vozOk = _vozFalhou = 0;
		GD.Print("[voz] ============ VOZ LOCAL: o corte e do servidor ============");

		var forjados = new List<ServerPlayer>();

		ServerPlayer Forjar(int i, string nome, ZoneKey zona, Vec2 pos)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDaVoz + i,
				Peer = null,
				Name = nome,
				Race = "Human",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = pos,
				Conta = $"bancada_voz_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = 1000 },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			forjados.Add(novo);
			return novo;
		}

		try
		{
			OAlcanceEhODoChat(Forjar);
			AZonaSeparaSozinha(Forjar);
			AParedeSaiDoQueCega(Forjar);
			OTetoDeQuatroCortaOMaisLonge(Forjar);
			ATorneiraSeguraATorneira(Forjar);
			OCaladoNaoFala(Forjar);
		}
		catch (Exception e)
		{
			AfirmarVoz($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// O MUNDO VOLTA COMO ESTAVA, inclusive os dicionarios da voz: `EsquecerVoz` e o mesmo que
			// a saida de um jogador chama, entao limpar aqui e limpar pelo caminho de verdade.
			foreach (ServerPlayer p in forjados)
			{
				EsquecerVoz(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}
			_calados.Remove($"bancada_voz_90");
		}

		GD.Print($"[voz] ============ {_vozOk} OK, {_vozFalhou} FALHA(S) ============");
	}

	/// <summary>Quem ouviria este falante AGORA, pelo funil de verdade.</summary>
	private List<(ServerPlayer Quem, byte Dist, bool Parede)> Leque(ServerPlayer a)
	{
		var saida = new List<(ServerPlayer, byte, bool)>();
		QuemOuveAVoz(a, NowMs(), saida);
		return saida;
	}

	private static bool Ouviu(List<(ServerPlayer Quem, byte Dist, bool Parede)> leque, ServerPlayer o) =>
		leque.Any(l => l.Quem.Id == o.Id);

	// =====================================================================
	// 1. O ALCANCE
	// =====================================================================
	/// <summary>
	/// O ALCANCE DA VOZ E O `RaioDaVista` DO CHAT -- os 22 tiles do `sayType()` do BYOND.
	///
	/// Duas nocoes de "perto" divergem no dia em que alguem mexer numa, e o sintoma seria o jogador
	/// dizendo "eu te OUVI mas o texto nao chegou" com nada na tela explicando. A afirmacao e testada
	/// nos dois lados do limite, porque so o "dentro" prova metade: um alcance infinito passaria.
	/// </summary>
	private void OAlcanceEhODoChat(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		ZoneKey zona = ZonaLimpaParaVoz();
		const float t = ZoneCollision.TileSize;
		Vec2 centro = new(250 * t, 250 * t);

		ServerPlayer fala = forjar(1, "bancada: quem fala", zona, centro);
		ServerPlayer colado = forjar(2, "bancada: colado", zona, centro + new Vec2(t, 0));
		ServerPlayer naBorda = forjar(3, "bancada: a 21 tiles", zona, centro + new Vec2(21 * t, 0));
		ServerPlayer fora = forjar(4, "bancada: a 23 tiles", zona, centro + new Vec2(23 * t, 0));

		List<(ServerPlayer Quem, byte Dist, bool Parede)> leque = Leque(fala);

		AfirmarVoz("quem esta colado ouve", Ouviu(leque, colado));
		AfirmarVoz("quem esta a 21 tiles ouve (dentro dos 22)", Ouviu(leque, naBorda));
		AfirmarVoz("quem esta a 23 tiles NAO RECEBE BYTE NENHUM", !Ouviu(leque, fora),
			"o corte e do servidor, e nao do botao de volume");

		AfirmarVoz("o alcance da voz E o do chat local (22 tiles)",
			Mathf.IsEqualApprox(RaioDaVista, 22 * ZoneCollision.TileSize),
			$"RaioDaVista={RaioDaVista}");

		// A DISTANCIA QUE VIAJA E MONOTONICA. Um byte trocado (ou uma divisao invertida) daria uma
		// curva de volume que sobe com a distancia -- e ninguem notaria ate alguem se afastar e ficar
		// MAIS alto, que e o tipo de defeito que se atribui a "bug do audio".
		byte dColado = leque.First(l => l.Quem.Id == colado.Id).Dist;
		byte dBorda = leque.First(l => l.Quem.Id == naBorda.Id).Dist;
		AfirmarVoz("a distancia que viaja cresce com a distancia de verdade", dColado < dBorda,
			$"colado={dColado} borda={dBorda}");
		AfirmarVoz("e o volume derivado dela CAI",
			VozLocal.VolumePelaDistancia(VozLocal.FracaoDaDistancia(dColado))
			> VozLocal.VolumePelaDistancia(VozLocal.FracaoDaDistancia(dBorda)));

		// E ELA NAO CHEGA A ZERO NO LIMITE: ver `VozLocal.VolumePelaDistancia`. Um corte em silencio
		// absoluto faria a voz sumir um passo antes de a pessoa sair do alcance, e o jogador ouviria
		// isso como falha de rede.
		AfirmarVoz("no limite do alcance ainda ha volume (o corte nao e um fade a zero)",
			VozLocal.VolumePelaDistancia(1f) > 0.05f,
			$"{VozLocal.VolumePelaDistancia(1f):0.000}");
	}

	// =====================================================================
	// 2. A ZONA
	// =====================================================================
	/// <summary>
	/// A ZONA SEPARA SOZINHA, e essa e a parte que NAO tem codigo -- `ZoneList` e o corte de interesse
	/// que o servidor ja usava pra snapshot, chat e projetil.
	///
	/// **E E ELA QUE RESPONDE A PERGUNTA 5 DO PEDIDO**: quem esta dentro da propria mente tem o
	/// `ServerPlayer` na zona da mente, e o que fica pra tras e um boneco (`GameServer.CorpoLargado.cs`)
	/// que nao esta no `_players`. Entao a voz de quem medita fundo **nao chega** em quem esta em pe ao
	/// lado do corpo dele -- e isso nao precisou de uma linha de codigo na voz.
	/// </summary>
	private void AZonaSeparaSozinha(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		ZoneKey zona = ZonaLimpaParaVoz();
		Vec2 p = new(250 * ZoneCollision.TileSize, 250 * ZoneCollision.TileSize);

		// A MESMA POSICAO EM DUAS ZONAS. E o caso que um corte por distancia SEM zona deixaria passar
		// -- e a distancia entre eles e ZERO, ou seja o pior caso possivel.
		ServerPlayer noMundo = forjar(10, "bancada: no mundo", zona, p);
		ServerPlayer naMente = forjar(11, "bancada: dentro da mente", DimensaoMental.De(999), p);
		ServerPlayer naNave = forjar(12, "bancada: na ponte da nave",
									 Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(999), p);

		AfirmarVoz("quem esta no mundo nao ouve quem esta dentro da mente",
			!Ouviu(Leque(naMente), noMundo), "e vice-versa: sao duas ZoneKey diferentes");
		AfirmarVoz("quem esta dentro da mente nao ouve o mundo la fora",
			!Ouviu(Leque(noMundo), naMente));
		AfirmarVoz("quem esta no interior da nave nao ouve o mundo (nem o contrario)",
			!Ouviu(Leque(noMundo), naNave) && !Ouviu(Leque(naNave), noMundo));

		// E A MENTE DE UM NAO E A MENTE DO OUTRO: duas pessoas meditando fundo ao mesmo tempo tem
		// zonas diferentes, e o hash da chave ja e o corte -- sem alocador de celula, sem teto de oito.
		ServerPlayer outraMente = forjar(13, "bancada: outra mente", DimensaoMental.De(1000), p);
		AfirmarVoz("duas mentes diferentes nao se ouvem", !Ouviu(Leque(naMente), outraMente));
	}

	// =====================================================================
	// 3. A PAREDE
	// =====================================================================
	/// <summary>
	/// A PAREDE SAI DO `.vis` -- o mapa do que CEGA -- e nao do `.col`.
	///
	/// ============================ POR QUE A DIFERENCA E O SISTEMA INTEIRO ============================
	/// Os dois divergem DE PROPOSITO: porta CEGA e nao bloqueia; a beirada do mapa BLOQUEIA e nao cega.
	/// Perguntar ao `.col` daria a resposta errada nos dois casos mais comuns do jogo -- e faria a voz e
	/// a vista discordarem sobre o que e parede, com o jogador ouvindo abafado sem nada na tela.
	///
	/// A bancada acha uma parede DE VERDADE num mapa DE VERDADE, e nao monta uma de mentira: um bitset
	/// forjado provaria que o `PathBlocked` funciona (o que ja se sabe) sem provar que o servidor esta
	/// lendo o arquivo certo -- que era exatamente o buraco (o `.vis` nunca tinha sido carregado aqui).
	/// ==============================================================================================
	/// </summary>
	private void AParedeSaiDoQueCega(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		// O SERVIDOR CARREGOU MESMO OS `.vis`? E a primeira pergunta, e ela e sobre o buraco que este
		// trabalho fechou: o campo existia no manifesto e ninguem o abria.
		int comVista = _catalogo?.Todas.Count(e => e.Vista != null) ?? 0;
		AfirmarVoz("o servidor carregou os mapas do que CEGA (.vis)", comVista > 0,
			"sem eles a voz nunca abafa, e nada no jogo diz por que");
		if (_catalogo == null || comVista == 0) return;

		// UM PAR DE CELULAS COM PAREDE ENTRE ELAS, achado no mapa de verdade.
		ZoneEntry? entrada = null;
		(int X, int Y) livreA = default, livreB = default;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Vista is not { } v) continue;
			if (!AcharParedeEntreDoisLivres(v, out livreA, out livreB)) continue;
			entrada = e;
			break;
		}

		if (entrada?.Vista is not { } vista)
		{
			AfirmarVoz("achei um par de celulas com parede entre elas num mapa de verdade", false);
			return;
		}

		ZoneKey zona = ZoneKey.Premade(entrada.Zona);
		const float t = ZoneCollision.TileSize;
		ServerPlayer aqui = forjar(20, "bancada: de um lado", zona,
								   new Vec2((livreA.X + 0.5f) * t, (livreA.Y + 0.5f) * t));
		ServerPlayer laDentro = forjar(21, "bancada: do outro lado", zona,
									   new Vec2((livreB.X + 0.5f) * t, (livreB.Y + 0.5f) * t));

		List<(ServerPlayer Quem, byte Dist, bool Parede)> leque = Leque(aqui);
		AfirmarVoz($"com parede no meio ({entrada.Zona}) o quadro sai MARCADO",
			leque.Any(l => l.Quem.Id == laDentro.Id && l.Parede),
			$"({livreA.X},{livreA.Y}) -> ({livreB.X},{livreB.Y})");

		// E ESTA E A OUTRA METADE: sem parede, o quadro sai LIMPO. So o primeiro passaria com um
		// `return true` cravado, e o defeito seria voz abafada no campo aberto inteiro.
		laDentro.Pos = new Vec2((livreA.X + 1.5f) * t, (livreA.Y + 0.5f) * t);
		EsquecerVoz(aqui.Id);   // o cache de parede tem 100 ms de validade; o teste nao espera
		AfirmarVoz("colado e sem parede, o quadro sai LIMPO",
			Leque(aqui).Any(l => l.Quem.Id == laDentro.Id && !l.Parede));
	}

	/// <summary>
	/// Acha duas celulas LIVRES com pelo menos uma CEGA no segmento entre elas.
	///
	/// Varre a diagonal do mapa em passos largos -- nao e uma busca boa, e nem precisa ser: qualquer
	/// par serve, e o primeiro que aparecer encerra. O ponto e que o par saia do ARQUIVO.
	/// </summary>
	private static bool AcharParedeEntreDoisLivres(ZoneCollision v, out (int X, int Y) a, out (int X, int Y) b)
	{
		const int passo = 4;
		for (int y = 8; y < v.Height - 8; y += 2)
			for (int x = 8; x < v.Width - 8 - passo * 2; x += 2)
			{
				if (v.BlockedCell(x, y) || v.BlockedCell(x + passo * 2, y)) continue;
				bool cego = false;
				for (int k = 1; k < passo * 2 && !cego; k++) cego = v.BlockedCell(x + k, y);
				if (!cego) continue;
				a = (x, y);
				b = (x + passo * 2, y);
				return true;
			}
		a = b = default;
		return false;
	}

	// =====================================================================
	// 4. O TETO DE QUATRO
	// =====================================================================
	/// <summary>
	/// O TETO E DE BANDA, e ele muda a ORDEM da conta: sem ele o leque e M x N; com ele e 4 x N.
	///
	/// A afirmacao que importa nao e "no maximo quatro" -- e **"quem cai e o mais longe"**. Um teto que
	/// cortasse pela ordem da lista faria a quinta pessoa colada em voce ser silenciada em favor de
	/// alguem no fim do alcance, e o jogador leria isso como "a voz dele nao funciona".
	/// </summary>
	private void OTetoDeQuatroCortaOMaisLonge(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		ZoneKey zona = ZonaLimpaParaVoz();
		const float t = ZoneCollision.TileSize;
		Vec2 ouve = new(250 * t, 250 * t);

		ServerPlayer ouvinte = forjar(30, "bancada: o ouvinte", zona, ouve);

		// SEIS FALANTES em anel, do mais perto pro mais longe, todos dentro dos 22 tiles.
		var falantes = new List<ServerPlayer>();
		for (int i = 0; i < 6; i++)
			falantes.Add(forjar(31 + i, $"bancada: falante {i}", zona, ouve + new Vec2((2 + i * 3) * t, 0)));

		// TODOS PASSAM PELO FUNIL DE VERDADE, e nao por um `_vozes[id] = new()` na mao: e o `RecebiVoz`
		// que registra quem esta falando, e testar o teto sem passar por ele provaria o teto de uma
		// tabela que ninguem escreve em jogo.
		foreach (ServerPlayer f in falantes) RecebiVoz(f, QuadroFalso, QuadroFalso.Length, calado: false);

		var ouviu = new List<ServerPlayer>();
		foreach (ServerPlayer f in falantes)
			if (Ouviu(Leque(f), ouvinte)) ouviu.Add(f);

		AfirmarVoz($"o ouvinte recebe no maximo {VozLocal.MaxFalantesPorOuvinte} vozes",
			ouviu.Count <= VozLocal.MaxFalantesPorOuvinte, $"recebeu {ouviu.Count}");

		AfirmarVoz("e as que ele recebe sao as MAIS PROXIMAS",
			ouviu.Count > 0 && ouviu.All(f => falantes.IndexOf(f) < VozLocal.MaxFalantesPorOuvinte),
			string.Join(", ", ouviu.Select(f => falantes.IndexOf(f))));

		AfirmarVoz("o mais longe e o que cai",
			!ouviu.Contains(falantes[5]) && !ouviu.Contains(falantes[4]));

		// E O TETO SOLTA A VAGA quando alguem cala. Sem isto, a primeira briga barulhenta do servidor
		// deixaria quatro pessoas com o microfone travado pra sempre.
		foreach (ServerPlayer f in falantes.Take(5)) EsquecerVoz(f.Id);
		AfirmarVoz("calado o mais perto, o mais longe recupera a vaga",
			Ouviu(Leque(falantes[5]), ouvinte));
	}

	// =====================================================================
	// 5. A TORNEIRA
	// =====================================================================
	/// <summary>
	/// O TETO POR FALANTE. Sem ele, um cliente modificado vira torneira: cada quadro que ele manda e
	/// multiplicado pelo numero de ouvintes.
	///
	/// PURO E COM O TEMPO NA MAO: a torneira recebe o instante como parametro, entao mil quadros num
	/// milissegundo se testam sem esperar milissegundo nenhum. Um `DateTime.Now` la dentro teria feito
	/// esta secao virar um `Sleep`.
	/// </summary>
	private void ATorneiraSeguraATorneira(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		var t = new VozLocal.Torneira();

		// A RAJADA E LEGITIMA: a rede entrega em lote, e recusar isso picotaria voz honesta.
		int passaram = 0;
		for (int i = 0; i < 20; i++)
			if (t.Cabe(1000, 40)) passaram++;
		AfirmarVoz($"a rajada inicial deixa passar a folga ({VozLocal.RajadaDeQuadros}) e nao mais",
			passaram == (int)VozLocal.RajadaDeQuadros, $"passaram {passaram}");

		// UM SEGUNDO DEPOIS: o credito repos exatamente um segundo de fala, e nao mais.
		passaram = 0;
		for (int i = 0; i < 500; i++)
			if (t.Cabe(2000, 40)) passaram++;
		AfirmarVoz($"500 quadros no mesmo instante viram no maximo {VozLocal.RajadaDeQuadros}",
			passaram <= (int)VozLocal.RajadaDeQuadros, $"passaram {passaram}");

		// O RITMO HONESTO PASSA INTEIRO. Um teto apertado demais e pior que nenhum: ele corta a voz de
		// quem nao fez nada de errado, e o sintoma (voz picotada) e indistinguivel de rede ruim.
		var honesta = new VozLocal.Torneira();
		int aceitos = 0;
		for (int i = 0; i < 250; i++)   // 5 segundos a 50 quadros/s
			if (honesta.Cabe(1000 + i * VozLocal.MsPorQuadro, 40)) aceitos++;
		AfirmarVoz("5 s de fala no ritmo certo passam INTEIROS", aceitos == 250, $"aceitos {aceitos}");
		AfirmarVoz("e nada foi recusado no caminho", honesta.Recusados == 0);

		// O QUADRO GRANDE DEMAIS E DESCARTADO, e ele e o vetor mais barato de todos: nao adianta
		// limitar a 50 quadros por segundo se cada um pode ter 4 KB.
		var gorda = new VozLocal.Torneira();
		AfirmarVoz("quadro maior que o teto e recusado",
			!gorda.Cabe(1000, VozLocal.MaxBytesDeQuadro + 1));
		AfirmarVoz("quadro vazio tambem", !gorda.Cabe(1000, 0));
		AfirmarVoz("e o quadro normal passa", gorda.Cabe(1000, 40));

		// E O TETO VALE NO CAMINHO DE VERDADE, e nao so num objeto solto: `RecebiVoz` e a porta por
		// onde a rede entra, e o contador de recusas do servidor e o unico jeito de perguntar a ele
		// "quantos voce jogou fora?" com o jogo rodando.
		ZoneKey zona = ZonaLimpaParaVoz();
		ServerPlayer torneira = forjar(80, "bancada: a torneira", zona,
											  new Vec2(250 * ZoneCollision.TileSize, 250 * ZoneCollision.TileSize));
		for (int i = 0; i < 200; i++) RecebiVoz(torneira, QuadroFalso, QuadroFalso.Length, calado: false);
		int recusados = QuadrosDeVozRecusadosDeTeste(torneira.Id);
		AfirmarVoz("200 quadros de uma vez pelo caminho de verdade: a maioria e jogada fora",
			recusados >= 200 - (int)VozLocal.RajadaDeQuadros - 1, $"recusados {recusados}");

		// O "ESTA FALANDO" E DERIVADO DO ULTIMO QUADRO -- e ele e o que acende o sinal sobre a cabeca
		// e o que libera a vaga no teto de quatro. Um campo `bool Falando` a parte seria a mesma
		// verdade escrita duas vezes.
		AfirmarVoz("logo depois do quadro, esta falando", gorda.Falando(1000));
		AfirmarVoz($"passados {VozLocal.MsAteCalar} ms sem quadro, calou",
			!gorda.Falando(1000 + VozLocal.MsAteCalar + 1));
	}

	// =====================================================================
	// 6. O SILENCIO DE ADMIN
	// =====================================================================
	/// <summary>
	/// CALAR ALGUEM CALA A PESSOA, E NAO O CANAL DE TEXTO DELA.
	///
	/// A marca e a MESMA (`AccountSave.Calada`, por CONTA e no disco) e o funil e o mesmo
	/// (`EstaCalado`). Um mute que valesse so pro texto seria pior que nenhum: quem fosse calado
	/// continuaria falando, e o admin nao teria como saber que a punicao dele nao pegou.
	///
	/// E O DESCARTE E MUDO. Avisar por quadro sao 50 linhas de chat por segundo num canal CONFIAVEL --
	/// a punicao viraria uma inundacao pior do que a fala que ela veio parar.
	/// </summary>
	private void OCaladoNaoFala(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		ZoneKey zona = ZonaLimpaParaVoz();
		const float t = ZoneCollision.TileSize;
		Vec2 centro = new(250 * t, 250 * t);

		ServerPlayer mudo = forjar(90, "bancada: o calado", zona, centro);
		ServerPlayer perto = forjar(91, "bancada: quem estaria ouvindo", zona, centro + new Vec2(t, 0));

		_calados.Add(mudo.Conta);
		AfirmarVoz("o funil do mute enxerga o corpo calado", EstaCalado(mudo));

		// ============================ COMO SE MEDE UM PACOTE QUE NAO SAIU ============================
		// Corpo forjado nao tem `Peer`, entao "nao entregou" seria verdade de qualquer jeito e nao
		// provaria nada. O que prova e o EFEITO COLATERAL: o contador de sequencia so avanca DEPOIS do
		// portao do mute (`RecebiVoz`). Sequencia parada = o quadro morreu antes do leque.
		// ==========================================================================================
		int avisosAntes = EscutaDeAvisos?.Count ?? 0;
		bool tinhaSeq = TemSequenciaDeVozDeTeste(mudo.Id);
		RecebiVoz(mudo, QuadroFalso, QuadroFalso.Length, calado: true);
		AfirmarVoz("o quadro de quem esta calado morre ANTES do leque",
			!tinhaSeq && !TemSequenciaDeVozDeTeste(mudo.Id),
			"a sequencia so avanca depois do portao do mute");

		// E O DESCARTE E MUDO: um aviso por quadro sao 50 linhas de chat por segundo, num canal
		// confiavel. A punicao viraria uma inundacao pior que a fala que ela veio parar.
		AfirmarVoz("o descarte e MUDO (nenhuma linha de chat por quadro recusado)",
			(EscutaDeAvisos?.Count ?? 0) == avisosAntes);

		// A TORNEIRA CORREU MESMO ASSIM: ela e teto de TRAFEGO, nao punicao. Se o mute a pulasse,
		// calar alguem tiraria dele a unica coisa que o impedia de inundar o servidor.
		AfirmarVoz("a torneira correu mesmo com o mute (o teto de trafego nao e a punicao)",
			FalandoPorVozDeTeste(mudo.Id));

		// E SOLTO O MUTE, O MESMO QUADRO PASSA. Sem esta metade, um `return` no topo do `RecebiVoz`
		// passaria no teste acima e calaria o servidor inteiro.
		_calados.Remove(mudo.Conta);
		EsquecerVoz(mudo.Id);
		RecebiVoz(mudo, QuadroFalso, QuadroFalso.Length, calado: false);
		AfirmarVoz("solto o mute, o mesmo quadro atravessa e chega em quem esta perto",
			TemSequenciaDeVozDeTeste(mudo.Id) && Ouviu(Leque(mudo), perto));
	}

	// =====================================================================
	// AJUDANTES
	// =====================================================================
	/// <summary>
	/// UMA ZONA PRE-FEITA SEM NINGUEM DENTRO.
	///
	/// Sem isto a bancada mediria o leque com os jogadores de VERDADE do servidor dentro dele -- e o
	/// resultado dependeria de quem esta jogando na hora. E o mesmo cuidado que a `--cargoportas` toma
	/// ao escolher a zona dela.
	/// </summary>
	private ZoneKey ZonaLimpaParaVoz() =>
		ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");
}
