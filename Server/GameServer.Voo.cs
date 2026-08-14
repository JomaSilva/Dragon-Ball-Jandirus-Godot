using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O VOO, LADO DO SERVIDOR.
///
/// As formulas vivem no <see cref="Voo"/> (Core), porque o cliente preve com as mesmas. Aqui mora
/// o que e DECISAO: quem pode levantar voo, quanto ja pagou, quando cai, e o que acontece ao
/// encostar no teto.
///
/// ============================ O QUE MUDA DE VERDADE COM A ALTURA ============================
/// Uma coisa so, e e a que importa: acima de <see cref="Voo.AlturaQueAtravessa"/> o passo deixa de
/// consultar o mapa. Nao ha "colisao de voo" paralela -- o `MoveRules` ja aceita `mapa` nulo, e
/// passar nulo E o `isflying` do original. As duas pontas fazem a mesma escolha pelo mesmo numero,
/// entao nao ha divergencia possivel entre o que o cliente andou e o que o servidor aceita.
/// ===========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// UM DECALQUE PRA ZONA INTEIRA -- o unico caminho de servidor pro <c>Client/Decalques.cs</c>.
	///
	/// Vai pra todo mundo da zona, e nao so pro dono: uma marca no chao e cenario, e cenario que so
	/// uma pessoa ve nao e cenario. Canal confiavel, porque um decalque perdido nao volta -- ao
	/// contrario da posicao, que o proximo snapshot conserta sozinho.
	/// </summary>
	/// <param name="peca">
	/// SO PRO <see cref="Protocol.Decal.Membro"/>: qual recorte da folha `Body Parts Bloody` cai no
	/// chao. Vai no fio SO quando o tipo e esse -- os outros sete decalques nao pagariam o byte pra
	/// escrever zero, e este e um pacote que numa briga sai varias vezes por segundo.
	///
	/// O CAMPO E PUBLICO NO SENTIDO DO JOGO e isso e deliberado: amputacao ja e informacao de zona
	/// inteira (os quatro bits de membro arrancado do `S2C.Feridas`), e a peca no chao e a MESMA
	/// informacao com outro desenho. O que ela NAO leva e numero nenhum de ficha -- nem vida, nem
	/// dano, nem de quem era o corpo. Quem assiste ve um braco no chao, que e o que ele veria.
	/// </param>
	private void MandarDecalque(ZoneKey zona, Protocol.Decal tipo, Vec2 onde, Facing dir,
								PecaDeCorpo peca = PecaDeCorpo.Nenhuma)
	{
		// A BANCADA ESCUTA AQUI, pela mesma razao das escutas de `GameServer.FormasTeste.cs`: um
		// decalque termina num `Peer.Send` e pacote que saiu no fio nao volta. A pergunta que so
		// daqui se responde e a do vacuo -- "o arremesso no espaco carimbou cratera no nada?" --, e
		// ela e sobre um pacote que NAO devia sair. Ausencia nao deixa rastro em lugar nenhum.
		// Nula em jogo: uma comparacao contra null por decalque.
		EscutaDeDecalques?.Add((zona.Hash, tipo));

		var w = Protocol.Begin(Protocol.S2C.Decalque);
		w.Put((byte)tipo);
		w.PutVec(onde);
		w.Put((byte)dir);
		// O BYTE CONDICIONAL. Ver o parametro `peca`: o leitor do cliente le pela MESMA condicao
		// (`GameClient`), entao as duas pontas concordam sem precisar de tamanho no pacote.
		if (tipo == Protocol.Decal.Membro) w.Put((byte)peca);
		foreach (ServerPlayer o in ZoneList(zona.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}

	/// <summary>A skill que destrava tudo isto.</summary>
	private const string SkillDoVoo = "/datum/skill/flying";

	/// <summary>`--vooteste`: quem entrar ja sabe voar. Ver a leitura dos argumentos em GameServer.cs.</summary>
	private bool _vooDeTeste;

	/// <summary>
	/// ESTE CORPO PAGA O KI DO VOO?
	///
	/// ============================ A BANCADA NAO PODE SER ADMIN ============================
	/// Admin nao paga -- e o `freeflight` do original. So que o host E SEMPRE ADMIN neste port (por
	/// IP, ver `GameServer.Admin.cs`), e a bancada roda com `--host` numa janela so. Sem esta
	/// excecao, o teste do custo mediria um jogador que nao paga NADA: as tres conferencias mais
	/// importantes (custo de decolagem, dreno por segundo e queda por exaustao) passariam a
	/// verificar zero contra zero, e uma formula errada nunca seria pega.
	///
	/// Entao `--vooteste` tambem tira o freeflight. E o mesmo espirito das outras flags: elas
	/// removem o que ATRAPALHA MEDIR, nunca o que esta sendo medido.
	/// =====================================================================================
	/// </summary>
	private bool PagaPeloVoo(ServerPlayer pl) => _vooDeTeste || !EhAdmin(pl);

	/// <summary>
	/// A PROFICIENCIA DE AGORA, com o piso do nivel ja aplicado.
	///
	/// ============================ POR QUE O PISO E APLICADO AQUI ============================
	/// No original quem escreve `flightability = 25` e o `after_learn()`/`effector()` no instante
	/// em que a skill sobe de nivel (`flying.dm:73,80`). Copiar isso exigiria um gancho de
	/// "subiu de nivel" que o port nao tem -- e ganchos de evento sao onde nasce o defeito de
	/// "aconteceu enquanto ninguem escutava" (personagem que subiu de nivel offline, save antigo,
	/// nivel concedido por admin).
	///
	/// Ler o piso a cada consulta da o MESMO resultado sem depender de ter escutado o momento
	/// certo, e e barato: uma comparacao. O valor sobe e e gravado de volta, entao a proficiencia
	/// ganha voando (que pode passar do piso) nao e perdida.
	/// ======================================================================================
	/// </summary>
	private static double HabilidadeDeVoo(ServerPlayer pl)
	{
		double piso = Voo.HabilidadeDoNivel(NivelDeVoo(pl));
		// QUEM DESTRAVOU PELA MAESTRIA DE KI entra com o piso do nivel 1 do DM. Sem isto ele voaria
		// com `flightability = 1`, e 80/1 = 80 de Ki so pra sair do chao -- um portao que se abre
		// mas nao deixa passar.
		if (piso < Voo.HabilidadeNivel1 && DestravouPelaMaestria(pl)) piso = Voo.HabilidadeNivel1;
		if (pl.Ficha.flightability < piso) pl.Ficha.flightability = piso;
		return pl.Ficha.flightability;
	}

	/// <summary>O nivel da skill de voo -- 0 pra quem nem aprendeu.</summary>
	private static int NivelDeVoo(ServerPlayer pl)
		=> pl.Livro.Sabe(SkillDoVoo) ? pl.Niveis.Nivel(SkillDoVoo) : 0;

	/// <summary>A raiz da arvore de Ki. Sobe meditando (2 por tique) e voando (2) -- Mind.dm:81.</summary>
	private const string SkillDoKi = "/datum/skill/mind/Ki_Unlocked";

	/// <summary>
	/// METADE DA MAESTRIA DO KI: onde o voo aparece sozinho.
	///
	/// `Ki_Unlocked` tem `maxlevel = 100`, entao "50% de maestria" e o nivel 50, literal.
	/// </summary>
	public const int MaestriaQueDestravaVoo = 50;

	/// <summary>
	/// ESTE CORPO PODE VOAR?
	///
	/// ============================ POR QUE NAO E MAIS UM VERB DE SKILL ============================
	/// A primeira versao pos "Voar" e "Velocidade de voo" como botoes na aba de skills, atras da
	/// skill `/datum/skill/flying`. O dono cortou os dois: voar tem que LIBERAR SOZINHO ao chegar em
	/// metade da maestria do Ki, e antes disso nem aparecer.
	///
	/// E melhor assim, e por um motivo que vale anotar: voar deixa de ser uma compra e passa a ser
	/// uma CONSEQUENCIA de treinar Ki -- que e como o jogo conta que voar e controle de energia, e
	/// nao um item de lista. Quem medita chega la sem precisar saber que existia uma skill pra
	/// comprar.
	///
	/// A skill `flying` do DM continua valendo (quem a tiver voa pelo caminho antigo, e ela e que da
	/// os pisos de 25/75 de proficiencia): as duas portas levam ao mesmo lugar.
	/// ============================================================================================
	/// </summary>
	private static bool PodeVoar(ServerPlayer pl)
		=> NivelDeVoo(pl) >= 1 || DestravouPelaMaestria(pl);

	private static bool DestravouPelaMaestria(ServerPlayer pl)
		=> pl.Niveis.Nivel(SkillDoKi) >= MaestriaQueDestravaVoo;

	/// <summary>
	/// O NIVEL QUE VALE pra tudo que o DM decidia pelo nivel da skill `flying` -- inclusive o
	/// treino da proficiencia.
	///
	/// ============================ SEM ISTO, QUEM DESTRAVA POR MAESTRIA NUNCA MELHORA ============================
	/// `Voo.SubirHabilidade` recusa nivel 0 (no DM, quem nao tem a skill nao treina voo). Como quem
	/// abre o voo pela maestria de Ki tem nivel 0 na skill `flying`, ele voaria a vida inteira com
	/// `flightability` travado em 25 -- pagaria sempre o custo mais caro e nunca melhoraria, sem nada
	/// na tela explicando por que.
	///
	/// Destravar por maestria vale como nivel 1: o mesmo piso, e o mesmo direito de treinar.
	/// =========================================================================================================
	/// </summary>
	private static int NivelEfetivoDeVoo(ServerPlayer pl)
		=> Math.Max(NivelDeVoo(pl), DestravouPelaMaestria(pl) ? 1 : 0);

	/// <summary>
	/// LIGAR E DESLIGAR O VOO -- o verb `Fly` (`flying.dm:84-96`).
	///
	/// O original cobra o Ki de decolagem NA HORA de ligar e nao devolve nada ao pousar. Pousar e
	/// de graca e nunca falha, entao aqui tambem: quem quer descer, desce.
	/// </summary>
	private void AlternarVoo(ServerPlayer pl)
	{
		if (!PodeVoar(pl))
		{
			Avisar(pl, $"voce ainda nao sente o proprio Ki o bastante pra voar "
				+ $"(maestria de Ki {pl.Niveis.Nivel(SkillDoKi)} de {MaestriaQueDestravaVoo} -- medite pra treinar).");
			return;
		}

		if (pl.Voando)
		{
			// DESLIGAR NAO TELEPORTA PRO CHAO. O corpo passa a descer sozinho pelo tique -- e o que
			// separa "pousar" de "cair", e o unico jeito de o pouso ter duracao na tela.
			pl.Voando = false;
			MandarEfeito(pl, "voo", 0);
			Avisar(pl, "voce comeca a descer.");
			return;
		}

		// ============================ O MORTO DO OUTRO MUNDO VOA ============================
		// No DM **nao ha gate nenhum de `dead` no voo**: o morto e `Un_KO()`'d antes de subir pro alem
		// (`Death.dm:86-89`) e `move = 1` e escrito quatro vezes no proprio `Death()`. E preciso: o
		// planeta do Sr. Kaioh fica no mesmo z6, do outro lado da Snake Way.
		//
		// O CORTE FICA NO CADAVER, e nao na morte: durante os 15 s em que o corpo esta caido no chao
		// do mundo dos vivos (`Alem.MsNoChao`) ele nao decola -- seria um boneco desenhado DEITADO
		// subindo. `MortoDePe` responde as duas coisas com uma pergunta so. Ver `Core/World/Alem.cs`.
		if (pl.Ficha.KO || (pl.Ficha.dead && !pl.MortoDePe))
		{ Avisar(pl, "nao da pra voar assim."); return; }

		double custo = Voo.CustoParaLigar(HabilidadeDeVoo(pl), pl.VooRapido);
		if (pl.Ficha.Ki < custo) { Avisar(pl, "voce esta cansado demais pra voar."); return; }

		pl.Ficha.Ki -= custo;
		pl.Voando = true;
		// LEVANTAR VOO DESLIGA O NADO -- `savant.swim = 0` (`flying.dm:112`, dentro do
		// `start_flying`). E a outra metade do par: `AlternarNado` desliga o voo. Sem as duas, um
		// corpo poderia estar nos dois estados e pagar os dois Kis ao mesmo tempo.
		//
		// SEM AVISO: quem apertou "voar" em cima da agua nao precisa ler que parou de nadar -- ele
		// esta subindo, que e o que ele pediu.
		PararDeNadar(pl, null);
		MandarEfeito(pl, "voo", -1);
		Avisar(pl, "voce comeca a pairar.");
	}

	/// <summary>
	/// O SUPERFLIGHT VIROU O SHIFT -- nao ha mais verb pra ele.
	///
	/// ============================ POR QUE O VERB SAIU ============================
	/// No DM `Superflight` e um verb que ALTERNA uma marcha (`flying.dm:98-105`), e eu portei assim:
	/// um botao chamado "Velocidade de voo" na aba de skills. O dono cortou -- "seria so apertar
	/// shift enquanto voa pra ter o mesmo efeito de blur de quando corre, porem voando".
	///
	/// Ele esta certo e a razao e de CONTROLE, nao de gosto: no chao, correr ja e segurar shift. Ter
	/// uma marcha de voo que se liga por menu e se esquece ligada obriga o jogador a lembrar de um
	/// estado invisivel que drena o Ki dele — e `450/flightability` por tique e o dreno mais caro do
	/// jogo. Preso ao shift, a marcha existe exatamente enquanto o dedo esta la.
	///
	/// O `flightspeed` do original continua sendo o mesmo campo e a mesma formula: so mudou quem o
	/// escreve. Ver a chamada em `GameServer.Input`.
	/// =============================================================================
	/// </summary>
	private static void MarchaDeVoo(ServerPlayer pl, bool shift)
		=> pl.VooRapido = shift && pl.Voando;

	/// <summary>
	/// O TIQUE DO VOO. Roda no tique CHEIO, junto da forma e da carga, e pelo mesmo motivo: os tres
	/// mexem no MESMO Ki. Rodar em cadencias diferentes faria o saldo depender da ordem em que os
	/// relogios se cruzam.
	/// </summary>
	private void TickDoVoo(ServerPlayer pl, float dt)
	{
		// QUEM CAIU, CAI. Nocaute e morte tiram o voo na hora (`KO.dm:71` faz `isflying=0`), mas a
		// altura sobra -- e o corpo desce sozinho pelo trecho de queda logo abaixo.
		//
		// MORRER VOANDO DERRUBA -- e isso continua valendo, e e o certo: quem morre no ar cai. O que
		// mudou e que o morto que ja esta DE PE no Outro Mundo nao e derrubado a cada tique (senao o
		// voo de la seria ligado e desligado 30 vezes por segundo). Mesma pergunta do gate de cima.
		if (pl.Voando && (pl.Ficha.KO || (pl.Ficha.dead && !pl.MortoDePe)))
		{
			pl.Voando = false;
			MandarEfeito(pl, "voo", 0);
		}

		if (!pl.Voando)
		{
			if (pl.Altitude <= 0f) { pl.Altitude = 0f; return; }
			DescerAte(pl, 0f, Voo.QuedaPorSegundo * dt);
			return;
		}

		// ============================ EM CENA O VOO NEM COBRA NEM TREINA ============================
		// Ver `GameServer.Formas.EmCena`. Quem se transforma no ar continua no ar -- e o terceiro dos
		// relogios que mexem no MESMO Ki (o comentario do `Tick()` ja os chama de irmaos por isso).
		// Sem esta guarda, congelar o dreno da forma nao resolveria nada pra quem estivesse voando:
		// o custo do voo esvaziaria o tanque durante os 140 s da cena do SSJ3 do mesmo jeito.
		//
		// A PROFICIENCIA PARA JUNTO pelo mesmo motivo da maestria da forma: ela e paga POR ESTE custo
		// (`flying.dm:34,40` -- voar e o unico jeito de ela subir), e manter o ganho com o preco
		// congelado seria treino de graca por assistir a uma cinematica.
		//
		// A ALTURA, LOGO ABAIXO, CONTINUA SENDO CUIDADA: corpo preso nao pode cair do ceu, e descer
		// nao custa Ki nenhum.
		// =======================================================================================
		if (!EmCena(pl))
		{
			// ============================ O CUSTO, LITERAL ============================
			// `Stats.dm:411-427`. O gasto so acontece com `Ki > 5`; abaixo disso o original desliga o
			// voo e diz "You're exhausted.". Admin nao paga -- e o `freeflight` de la.
			// ==========================================================================
			if (PagaPeloVoo(pl))
			{
				if (pl.Ficha.Ki > Voo.KiQueDerruba)
				{
					pl.Ficha.Ki -= Voo.CustoPorSegundo(HabilidadeDeVoo(pl), pl.VooRapido) * dt;
				}
				else
				{
					pl.Voando = false;
					MandarEfeito(pl, "voo", 0);
					Avisar(pl, "voce esta exausto.");
					return;
				}
			}

			// A PROFICIENCIA SOBE VOANDO (`flying.dm:34,40`) -- e o unico jeito de ela subir.
			pl.Ficha.flightability = Voo.SubirHabilidade(
				HabilidadeDeVoo(pl), NivelEfetivoDeVoo(pl), pl.VooRapido, dt);
		}

		// ---------------------------- a altura ----------------------------
		if (pl.QuerDescer)
		{
			DescerAte(pl, 0f, Voo.DescidaPorSegundo * dt);
		}
		else if (pl.QuerSubir)
		{
			pl.Altitude = MathF.Min(Voo.AlturaMaxima, pl.Altitude + Voo.SubidaPorSegundo * dt);
			if (pl.Altitude >= Voo.AlturaMaxima) TentarRomperAAtmosfera(pl);
		}
		else if (pl.Altitude < Voo.AlturaDePairar)
		{
			// SEM PEDIR NADA, O CORPO PAIRA. Ligar o voo e um gesto so; a altura de pairar e a
			// altura em que voar JA FAZ o que o original fazia (passar por cima do cenario).
			pl.Altitude = MathF.Min(Voo.AlturaDePairar, pl.Altitude + Voo.SubidaPorSegundo * dt);
		}
	}

	/// <summary>
	/// DESCE, E SE CHEGOU NO CHAO, POUSA.
	///
	/// ============================ O POUSO DENTRO DA PEDRA ============================
	/// Este e um problema que o original NAO tinha e que a altura cria: la voar nunca tirou o corpo
	/// da colisao do turf denso de verdade -- `isflying` deixava ENTRAR, mas o mob continuava no
	/// mesmo z, num mapa onde nao havia "por cima". Aqui da pra sobrevoar uma montanha e soltar a
	/// tecla em cima dela.
	///
	/// Sem tratamento, o corpo pousa dentro de parede e fica preso: o `MoveRules.Advance` tem um
	/// escape ("ja preso dentro de parede: deixa sair") que devolve o passo cheio, mas ele so
	/// funciona se houver pra onde ir -- no meio de um macico de rocha, todo passo cai em parede
	/// de novo. O jogador ficaria imovel num lugar de onde nao da pra sair andando.
	///
	/// A saida e procurar chao livre em espiral a partir do ponto e desviar o pouso pra la.
	/// ================================================================================
	/// </summary>
	private void DescerAte(ServerPlayer pl, float piso, float passo)
	{
		pl.Altitude = MathF.Max(piso, pl.Altitude - passo);
		if (pl.Altitude > 0f) return;

		pl.Altitude = 0f;
		pl.Voando = false;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);

		// ============================ A PERGUNTA VAI COM O **MODO** ============================
		// `Occupied` sem modo e a pergunta A PE, e a pe a agua para -- entao pousar em cima do lago
		// caia no desvio do "pousou dentro da pedra": o corpo era teleportado pra margem e o tique
		// seguinte apagava o nado por falta de agua embaixo. Quem larga o voo em cima da agua
		// NADANDO tem que continuar nadando; quem larga a pe continua sendo desviado, porque agua
		// nao serve de chao (`ClasseDeAgua.ServeDeChao`).
		//
		// O modo e lido DEPOIS de zerar a altura e o voo, logo acima: aqui `noAr` ja e falso e a
		// resposta e a do corpo POUSADO, que e sobre quem esta se perguntando.
		//
		// (Era a quinta chamada esquecida do `ClasseDeAgua.ModoDe` -- as outras quatro sao o passo do
		// cliente, a validacao do servidor, o passo da IA e o arremesso.)
		ModoDeTravessia modo = ModoDeTravessiaDe(pl);
		if (mapa != null && MoveRules.Occupied(mapa, pl.Pos, modo) && ChaoLivrePerto(mapa, pl.Pos) is { } saida)
		{
			pl.Pos = saida;
			// O IDIOMA DO DESLOCAMENTO PELO SERVIDOR, igual ao do dash e do Zanzoken: carimba a
			// sequencia (os pacotes que o cliente ja mandou sao da posicao velha e nao opinam),
			// abre a janela de correcao esperada (senao isto conta como violacao) e manda a
			// correcao com sequencia + posicao.
			pl.LastInputMs = NowMs();
			pl.CorrecaoEsperadaAte = NowMs() + 500;
			pl.SeqDoTeleporte = pl.SeqInput;
			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);
			w.PutVec(pl.Pos);
			pl.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
			// A MENSAGEM DEIXOU DE CITAR ROCHA porque o desvio deixou de ser so por rocha: agua
			// tambem nao serve de chao (`ClasseDeAgua.ServeDeChao`), e o `MoveRules.Occupied` aqui
			// pergunta A PE -- entao pousar em cima do lago cai neste mesmo desvio, de graca. Dizer
			// "rocha" pra quem pousou num rio seria a tela contando outra historia.
			Avisar(pl, "nao dava pra pousar ai -- voce desce ao lado.");
		}

		MandarEfeito(pl, "voo", 0);
		MandarEfeito(pl, "pouso", -1);
	}

	/// <summary>
	/// O chao livre mais proximo, em espiral por anel de celulas. Nulo se nao houver nenhum ate
	/// <see cref="RaioDoPouso"/> -- e ai nao ha o que fazer de melhor do que pousar onde esta.
	/// </summary>
	private const int RaioDoPouso = 12;

	private static Vec2? ChaoLivrePerto(ZoneCollision mapa, Vec2 centro)
	{
		int cx = (int)MathF.Floor(centro.X / ZoneCollision.TileSize);
		int cy = (int)MathF.Floor(centro.Y / ZoneCollision.TileSize);

		for (int r = 1; r <= RaioDoPouso; r++)
		{
			for (int dx = -r; dx <= r; dx++)
			for (int dy = -r; dy <= r; dy++)
			{
				// so a CASCA do quadrado: o miolo ja foi visto nos aneis anteriores
				if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;

				var p = new Vec2((cx + dx + 0.5f) * ZoneCollision.TileSize,
								 (cy + dy + 0.5f) * ZoneCollision.TileSize);
				// A BEIRADA NAO SERVE DE POUSO: cair nela dispararia a volta do planeta no quadro
				// seguinte, e o jogador seria cuspido no outro lado do mundo sem ter pedido nada.
				if (mapa.NaBordaEm(p)) continue;
				if (!MoveRules.Occupied(mapa, p)) return p;
			}
		}
		return null;
	}

	/// <summary>
	/// NO TETO, COM OS PORTOES DO `Space_Flight` -- sai do planeta. Sem eles, para e diz por que.
	///
	/// O original nao tem teto de altitude: la o `Space_Flight` e um verb com portoes fixos
	/// (`Mindo.dm:162`) que espera 5 s e teleporta. O que muda aqui e so o GESTO -- em vez de
	/// escolher um verb num menu, sobe-se ate nao dar mais. Os portoes sao os mesmos, e o
	/// <see cref="Decolar"/> que leva pro espaco tambem.
	/// </summary>
	private void TentarRomperAAtmosfera(ServerPlayer pl)
	{
		if (Espaco.EhEspaco(pl.Zone)) return;

		if (Voo.PodeRomperAAtmosfera(pl.Ficha.Ki, pl.Ficha.expressedBP, HabilidadeDeVoo(pl)))
		{
			pl.Voando = false;
			pl.Altitude = 0f;
			pl.VooRapido = false;
			MandarEfeito(pl, "voo", 0);
			Decolar(pl);
			return;
		}

		long agora = NowMs();
		if (agora < pl.AvisoDeTetoAte) return;
		pl.AvisoDeTetoAte = agora + 4000;

		// O AVISO DIZ QUAL PORTAO FALTA. "Voce nao consegue" nao ensina nada -- e o jogador que nao
		// sabe o que falta tenta a mesma coisa de novo.
		string falta = pl.Ficha.Ki < Voo.KiParaEspaco
			? $"Ki (precisa de {Voo.KiParaEspaco:0}, voce tem {pl.Ficha.Ki:0})"
			: pl.Ficha.expressedBP < Voo.BpParaEspaco
				? $"poder (precisa de {Voo.BpParaEspaco:0} de BP expresso)"
				: "treino de voo";
		Avisar(pl, $"voce bate no limite da atmosfera e nao rompe: falta {falta}.");
	}

	/// <summary>
	/// O corpo PASSA por cima do cenario agora? E o unico lugar que responde isso, e as duas pontas
	/// perguntam a ele (o cliente pela copia em <c>LocalPlayer</c>).
	/// </summary>
	private static bool AtravessandoCenario(ServerPlayer pl)
		=> Voo.AtravessaCenario(pl.Altitude);

	/// <summary>
	/// <paramref name="a"/> ALCANCA <paramref name="o"/> pela ALTURA? Ver <see cref="Voo.PodeAcertar"/>.
	///
	/// CUIDADO: e assimetrico. `AlcancaPelaAltura(a, o)` e `AlcancaPelaAltura(o, a)` dao respostas
	/// diferentes quando um esta pairando rasante e o outro no chao -- e essa diferenca E a regra.
	/// </summary>
	private static bool AlcancaPelaAltura(ServerPlayer a, ServerPlayer o)
		=> Voo.PodeAcertar(Voo.Andar(a.Altitude), Voo.Andar(o.Altitude));
}
