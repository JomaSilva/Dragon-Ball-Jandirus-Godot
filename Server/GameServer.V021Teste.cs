using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using Discipulado = Jandirus.Core.Skills.Discipulado;
using EnsinoDeSkill = Jandirus.Core.Skills.EnsinoDeSkill;
using Fatos = Jandirus.Core.Verbos.Fatos;
using PedidosPendentes = Jandirus.Core.Verbos.PedidosPendentes;
using PosseEscrita = Jandirus.Core.Verbos.PosseEscrita;
using Recusa = Jandirus.Core.Skills.Recusa;
using RequisitosDosVerbos = Jandirus.Core.Verbos.RequisitosDosVerbos;
using Skill = Jandirus.Core.Skills.Skill;
using TecnicaCustomizada = Jandirus.Core.Skills.TecnicaCustomizada;
using AcendedoresDoPort = Jandirus.Core.Skills.AcendedoresDoPort;
using CensoDeSkills = Jandirus.Core.Skills.CensoDeSkills;
using EfeitosEscritosAMao = Jandirus.Core.Skills.EfeitosEscritosAMao;
using RegrasDeNivel = Jandirus.Core.Skills.RegrasDeNivel;
using SkillCatalog = Jandirus.Core.Skills.SkillCatalog;
using SkillsAposentadas = Jandirus.Core.Skills.SkillsAposentadas;
using SkillsFechadas = Jandirus.Core.Skills.SkillsFechadas;
using Materializacao = Jandirus.Core.Items.Materializacao;
using Caminho = Jandirus.Core.World.Caminho;
using Cerebro = Jandirus.Core.Ai.Cerebro;
using Comando = Jandirus.Core.Ai.Comando;
using Percepcao = Jandirus.Core.Ai.Percepcao;
using Plano = Jandirus.Core.Ai.Plano;
using VerbosDoPort = Jandirus.Core.Skills.VerbosDoPort;
using Tecnicas = Jandirus.Core.Skills.Tecnicas;
using Arsenal = Jandirus.Core.Ai.Arsenal;
using Capacidades = Jandirus.Core.Ai.Capacidades;
using TecnicasDeLonge = Jandirus.Core.Skills.TecnicasDeLonge;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--v021teste` -- O PEDIDO DE 2026-10-13 (a versao 0.2.1), do lado do servidor e do Core.
///
///     Godot --headless --path . --server --port 8271 --v021teste
///
/// Cada familia e um item do pedido do dono, medido pela porta de PRODUCAO e com o defeito injetado do jogo de
/// antes (a v0.2.0):
///
///   K1. A BOLA COMUM ESTOURA AO ACABAR -- no fim do alcance e do prazo, com a cratera pequena (uma por lugar);
///       e a que acaba num corpo tambem marca o chao (item 8).
///   K2. O RAIO E A ESFERA GRANDE ESTOURAM ONDE QUER QUE ACABEM -- na parede, de prazo (o raio que leva alguem
///       devagar) e quando a mao solta o raio que estava em cima de alguem (item 7).
///   K3. O KI CONTRA O CENARIO -- a bola bate na parede pela regra do soco; a explosao derruba paredes, racha o
///       chao (mais no centro que na beira), fere a construcao e derruba o bloco erguido (item 27).
///   K4. A POSE E O SOM -- todo ataque de ki veste a pose de tiro sem prender o corpo, sai com o som do DM, e a
///       tecla X soa (itens 1, 11 e 34).
///   K5. KI CONTRA KI -- bola contra bola, raio contra bola, a bola no tronco de um raio, e a DISPUTA do raio
///       alimentado (ou de outra esfera) com a esfera grande (item 32).
///   V.  O VIGOR E O KI -- o treino cansa e para; o meditar rende menos; o soco e o voo gastam Ki; o Ki acima de
///       100% escorre, mais depressa quanto maior; e o teto do jogo e 200% (itens 6, 23, 29, 30, 33 e 35).
///   S.  O SCOUTER E O SENSE COM OS NPCs -- o scouter le o BP do NPC comum, e os dois dizem o nome dele (item 14).
///   A.  O AGARRAO -- estrangular nao e soco e planta quem aperta; o carregado vai colado e nao marcha; quem se
///       debate so vira (item 36).
///   M.  O MAJIN NAO SANGRA -- a mascara que a tela recebe dele so tem a amputacao, e o membro arrancado nao deixa
///       peca no chao (item 16).
///   C.  O CABELO SEM A ARTE DA FORMA -- o penteado que nao tem a folha de Super Saiyajin ganha o dourado (item 19).
///   O.  O ORANGE -- o buff do Beast, concedido pelo desejo "liberar o meu potencial" so ao Namekuseijin, sem cobrar
///       o Super Namekuseijin; e o potencial desperto viaja na aparencia (item 20).
///   L.  LARGAR E PEGAR -- o item, o zeni e o recurso largados caem no chao um passo a frente (ou aos pes, contra a
///       parede), juntam-se no mesmo monte, viajam na lista da zona e voltam pra mochila ou pra carteira de quem
///       pega; o que esta em uso nao sai, e o que ninguem pega some (item 5).
///   R.  A CARTEIRA DO CAIDO E A BOLSA DO NPC -- o zeni e os recursos de quem esta nocauteado se roubam, inteiros; o
///       NPC nasce carregando zeni (item 18; o premio do vilao e medido na `--vilaoteste`, item 12).
///   I.  OS ITENS -- todo item do catalogo aponta pra uma folha e um estado que existem (a furadeira, a bandagem e o
///       kit herdam a arte da construcao); o scouter ligado e os pesos vestidos viajam na aparencia, com a arte que
///       o jogador escolheu pros pesos (itens 2, 3 e 24).
///   P.  OS PEDIDOS NA TELA -- o convite de mestre, a licao e as outras ofertas viram um pedido com as duas respostas
///       (os mesmos verbs do menu), e somem respondidos ou vencidos (item 15).
///   T.  TREINAR COM O MESTRE E COM O COLEGA -- contra o mestre o ganho nunca e menor que 2x, contra outro aluno do
///       mesmo mestre 1,5x, e o bit chega aos golpes de producao (item 17).
///   E.  A LICAO DO MESTRE -- ao proprio aluno o mestre ensina o que nao se ensina a estranho (e essa licao gasta os
///       marcos), pelo cartao do aluno; e a tecnica de ki que ele inventou vira uma copia que nao se repassa (item 4).
///   N.  AS SKILLS -- nenhuma folha que pende de arvore fica sem acendedor nem muda; cada acendedor que o port
///       escreveu abre a compra pelo livro; as maestrias legadas sairam com reembolso; a estatua do dragao pede a
///       skill dela; e o aviso de compra diz o que a skill faz (item 31).
///   G.  OS VERBS MORTOS -- nenhuma skill que a loja vende so promete um verb sem corpo: tres ganharam corpo (a
///       Miragem que se guarda, as Miragens do Zanzoken e o Cuspe Acido), a fusao Namekuseijin pede a skill dela, o
///       aviso de compra so anuncia o verb que existe, e a skill que so prometia um verb cujo sistema falta fechou,
///       com os marcos de volta (item 9; o botao cinza que saiu do menu e medido na tela, pelo robo dos verbs).
///   H.  AS SKILLS QUE CRIAM ITEM -- a Materialize concede um verb com corpo: os pesos e a roupa que ela cria nascem
///       NO CHAO, aos pes de quem conjura, pelo Ki do ritual; a roupa se pega e se veste; e o que nao se materializa
///       e recusado dizendo por que (item 10; a Semente Senzu que brota no chao e medida na `--g12teste`).
///   D.  A IA CONTORNA A PAREDE E CARREGA O KI -- o A* acha a porta de um muro e nao corta quina; o corpo dirigido
///       sai de um bolso de paredes e chega no alvo; o passo que emperra vira pro lado livre (e o comando montado a
///       mao continua batendo na parede); e com o Ki baixo e vigor pra pagar o corpo se afasta, carrega e volta --
///       inclusive o corpo possuido, que nunca carregava (item 22, a primeira metade).
///   U.  A IA USA TODAS AS SKILLS -- o corpo POSSUIDO que sabe o catalogo inteiro, lutando pelo tique de producao,
///       aperta TODA tecnica que sabe: os trinta e dois tiros (com o rodizio), os golpes de perto no lugar do
///       soco, os agarroes, as investidas, os reforcos (que ligam na luta, saem com o Ki baixo e se revezam), os
///       socorros e a deflexao do tiro que vem; as tecnicas que se seguram ganham o segundo aperto do relogio; e o
///       dono do corpo nao ouve a conversa dos verbs que nao apertou (item 22, a segunda metade).
///
/// A metade de TELA (os sons que tocam, a pose desenhada, o clarao e a cratera) e da `--diagv021`.
/// </summary>
public sealed partial class GameServer
{
	private int _v21Ok, _v21Falhou;

	private void AfirmarV21(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _v21Ok++; GD.Print($"[v021]   OK    {oque}"); return; }
		_v21Falhou++;
		GD.PrintErr($"[v021]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaV021()
	{
		_v21Ok = _v21Falhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[v021] ================ A VERSAO 0.2.1: O PEDIDO DE 2026-10-13 ================");
		AfirmarV21("a zona da bancada tem colisao carregada", _pjMapa != null);
		if (_pjMapa == null) { GD.Print($"[v021] ================ {_v21Ok} passaram, {_v21Falhou} falharam ================"); return; }

		List<BlocoDePe> basesDeAntes = [.. _bases.Values.SelectMany(z => z.Celulas.Values)];
		try
		{
			ABolaQueEstouraAoAcabarV21();
			ORaioQueAcabaEstourandoV21();
			OKiContraOCenarioV21();
			APoseEOSomDoTiroV21();
			OKiContraOKiV21();
			OVigorEOKiV21();
			OsNpcsNosSentidosV21();
			OAgarraoV21();
			OMajinNaoSangraV21();
			OCabeloSemArteV21();
			OOrangeV21();
			OsItensV21();
			LargarEPegarV21();
			ACarteiraDoCaidoV21();
			OsPedidosNaTelaV21();
			OTreinoComMestreEColegaV21();
			ALicaoDoMestreV21();
			AsSkillsV21();
			OsVerbsMortosV21();
			OQueSeCriaNasceNoChaoV21();
			AIaContornaECarregaV21();
			AIaUsaTodasAsSkillsV21();
			ATechDeBolsoV21();
			AsArmasEAsBombasV21();
			AsMaquinasDeChaoV21();
			AsMaquinasGrandesV21();
			OTeleporteEAVigilanciaV21();
			OsCompanheirosV21();
			OSintetizadorV21();
			OsKitsDeAndroideV21();
			OSuperComputadorV21();
			ABancadaDeCriacaoV21();
			OsItensDeMagiaFechamV21();
		}
		finally
		{
			DesligarOsDefeitosV21();
			EscutaDeDecalques = null;
			EscutaDeGestos = null;
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			ZerarBases();
			foreach (BlocoDePe b in basesDeAntes) GuardarBloco(b);
		}

		GD.Print($"[v021] ================ {_v21Ok} passaram, {_v21Falhou} falharam ================");
	}

	/// <summary>Nenhum botao de defeito sobrevive a bancada.</summary>
	private void DesligarOsDefeitosV21()
	{
		KiContraCorpo.KiNaoEstragaCenarioDeTeste = false;
		KiContraCorpo.BolaSomeAoAcabarDeTeste = false;
		KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = false;
		KiContraCorpo.BolaSemCrateraDeTeste = false;
		KiContraCorpo.RegraDeAntesDeTeste = false;
		SonsDeKi.TiroSemSomDeTeste = false;
		TiroSemPoseDeTeste = false;
		XSemSomDeTeste = false;
		GestoPrendeNaTelaDeTeste = false;
		KiContraKi.SemChoqueDeTeste = KiContraKi.RaioCedeNoEmpateDeTeste = KiContraKi.EsferaNaoDisputaDeTeste = false;
		Cansaco.TreinoNaoCansaDeTeste = Cansaco.SocoDeGracaDeTeste = Cansaco.VooSemPisoDeTeste = false;
		Cerebro.SemRepertorioDeTeste = Cerebro.SemRodizioDeTeste = false;
		SemSegundoApertoDeTeste = IaFalaComODonoDeTeste = PrazoDeRaioSoDeNpcDeTeste = EstouroSemConferirAZonaDeTeste = false;
		CargaDeKi.TetoPelaFormulaDoDmDeTeste = CargaDeKi.ExcessoNaoEscorreDeTeste = CargaDeKi.SegurarNoTetoDeGracaDeTeste = false;
		GainKnobs.MeditarRende = _v21MeditarRendeDeFabrica;
		NpcSemNomeNemLeituraDeTeste = false;
		AgarradoNaoViraDeTeste = AgarradoMarchaNoColoDeTeste = EstranguladorAndaDeTeste = false;
		Feridas.MajinSangraDeTeste = false;
		Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = false;
		Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = false;
		Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = false;
		Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = false;
		Jandirus.Core.Forms.EstadoDeForma.ConcedidaCobraODegrauDeTeste = false;
		Jandirus.Core.Items.CatalogoDeItens.ItemNaoHerdaAArteDaObraDeTeste = false;
		Jandirus.Core.Items.EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = false;
		Jandirus.Core.Items.Chao.LargarApagaDeTeste = false;
		Jandirus.Core.Items.Saque.CarteiraFicaDeTeste = false;
		Jandirus.Core.Npc.BolsaDeNpc.NpcSemBolsaDeTeste = false;
		PedidosPendentes.SemNotificacaoDeTeste = false;
		GainKnobs.ColegaSemBonusDeTeste = GainKnobs.MestreSemPisoDeTeste = false;
		EnsinoDeSkill.MestreSoEnsinaOQueSeEnsinaDeTeste = false;
		AcendedoresDoPort.SemCorrecaoDeTeste = EfeitosEscritosAMao.TabelaVaziaDeTeste = false;
		AvisoSoOlhaACompraDeTeste = EstatuaSemSkillDeTeste = false;
		SkillsFechadas.TudoAbertoDeTeste = false;
		MiragemGuardadaAindaValeDeTeste = FusaoNamekSemSkillDeTeste = AvisoAnunciaVerbMortoDeTeste = false;
		VerbosDoPort.SemCorrecaoDeTeste = false;
		Caminho.SemCaminhoDeTeste = PassoNaoContornaDeTeste = Cerebro.SoCarregaNoCriticoDeTeste = false;
	}

	/// <summary>O valor de producao do `GainKnobs.MeditarRende`, lido antes de a bancada mexer nele.</summary>
	private readonly double _v21MeditarRendeDeFabrica = GainKnobs.MeditarRende;

	// =====================================================================
	// A INFRAESTRUTURA
	// =====================================================================
	private static int DecalquesV21(Protocol.Decal tipo)
	{
		int n = 0;
		foreach ((ulong _, Protocol.Decal t, byte[] _) in EscutaDeDecalques ?? [])
			if (t == tipo) n++;
		return n;
	}

	private bool CaiuV21(int cx, int cy) =>
		_cenarioCaido.TryGetValue(ChaveDeEstado(ZonaDaBancadaDeProjetil), out HashSet<(int X, int Y)>? caidas)
		&& caidas.Contains((cx, cy));

	/// <summary>
	/// PAREDE "SIMPLES": tile denso do mapa que o ki pode derrubar -- sem obra, nave, bloco ou rocha em cima, fora
	/// da beirada e do que nao se quebra. E a celula em que a regra do turf (e so ela) responde.
	/// </summary>
	private bool ParedeSimplesV21(ZoneCollision mapa, int cx, int cy) =>
		cx > 0 && cy > 0 && cx < mapa.Width && cy < mapa.Height
		&& mapa.BlockedCell(cx, cy) && mapa.BloqueadaNoArquivo(cx, cy) && !mapa.NaBorda(cx, cy) && !mapa.Indestrutivel(cx, cy)
		&& ObraNaCelula(ZonaDaBancadaDeProjetil, cx, cy) == null && NaveNaCelula(ZonaDaBancadaDeProjetil, cx, cy) == null
		&& BlocoEm(ZonaDaBancadaDeProjetil, cx, cy) == null && !RochaNaCelula(ZonaDaBancadaDeProjetil, cx, cy);

	private static Vec2 CentroV21(int cx, int cy) =>
		new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f, cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f);

	/// <summary>
	/// Uma parede simples com <paramref name="livres"/> celulas de terra firme a oeste (na fileira dela) e pelo menos
	/// <paramref name="vizinhas"/> outras paredes simples a ate dois tiles. Varre de cima pra baixo a partir de
	/// <paramref name="deY"/>, e devolve falso se o mapa nao tem mais nenhuma.
	/// </summary>
	private bool AcharMuroV21(ZoneCollision mapa, int livres, int vizinhas, ref int deY, out int cx, out int cy)
	{
		for (int y = Math.Max(deY, 8); y < mapa.Height - 8; y++)
			for (int x = livres + 6; x < mapa.Width - 8; x++)
			{
				if (!ParedeSimplesV21(mapa, x, y)) continue;
				bool livre = true;
				for (int d = 1; d <= livres && livre; d++)
					livre &= !mapa.BlockedCell(x - d, y) && !mapa.EhAgua(x - d, y) && !mapa.NaBorda(x - d, y);
				if (!livre) continue;
				int perto = 0;
				for (int dy = -2; dy <= 2; dy++)
					for (int dx = -2; dx <= 2; dx++)
						if ((dx != 0 || dy != 0) && ParedeSimplesV21(mapa, x + dx, y + dy)) perto++;
				if (perto < vizinhas) continue;
				cx = x; cy = y;
				deY = y + 6;   // a cena seguinte procura mais embaixo: esta vai mexer no cenario daqui
				return true;
			}
		cx = cy = 0;
		return false;
	}

	/// <summary>Um quadrado de <paramref name="lado"/> celulas de chao firme (sem parede, agua, nuvem, obra ou beirada). Devolve a celula do centro.</summary>
	private bool AcharPracaFirmeV21(ZoneCollision mapa, int lado, ref int deY, ref int deX, out int cx, out int cy)
	{
		for (int y = Math.Max(deY, 8); y + lado < mapa.Height - 8; y++)
		{
			for (int x = Math.Max(deX, 8); x + lado < mapa.Width - 8; x++)
			{
				bool ok = true;
				for (int dy = 0; dy < lado && ok; dy++)
					for (int dx = 0; dx < lado && ok; dx++)
						ok &= !mapa.BlockedCell(x + dx, y + dy) && !mapa.EhAgua(x + dx, y + dy) && !mapa.EhNuvem(x + dx, y + dy)
							  && !mapa.NaBorda(x + dx, y + dy) && !mapa.Indestrutivel(x + dx, y + dy) && !CaiuV21(x + dx, y + dy)
							  && BlocoEm(ZonaDaBancadaDeProjetil, x + dx, y + dy) == null;
				if (!ok) continue;
				cx = x + lado / 2; cy = y + lado / 2;
				deX = x + lado + 1;   // a proxima praca nasce ao lado, sem dividir celula com esta
				deY = y;
				return true;
			}
			deX = 8;
		}
		cx = cy = 0;
		return false;
	}

	// =====================================================================
	// K1) A BOLA COMUM ESTOURA AO ACABAR
	// =====================================================================
	private void ABolaQueEstouraAoAcabarV21()
	{
		GD.Print("[v021] -- K1) A BOLA COMUM ESTOURA AO ACABAR, com a cratera pequena (item 8)");
		float tile = ZoneCollision.TileSize;
		Vec2 mao = TerraFirmeG12(16);

		(Projetil Bola, int Crateras, int Estouradas) Bola(double alcance, Action<Projetil>? ajuste = null,
														 bool crateraLimpa = true, Vec2? deOnde = null, float altitude = 0,
														 Vec2? alvoEm = null)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			if (crateraLimpa) _craterasPequenas.Clear();
			ServerPlayer atirador = ForjarV2("AtiradorDeBola", deOnde ?? mao, 10_000);
			atirador.Facing = Facing.East;
			atirador.Altitude = altitude;
			// O ALVO NASCE SEM KI: sem energia ninguem raspa nem deflete (os dois sorteios do `Acertar` pedem `Ki >= 5`).
			// O que esta cena mede e a cratera de quando a bola ACERTA; com o tanque cheio o raspao e um sorteio de
			// verdade (a bola de dano 1 contra um corpo de mesmo poder), e a cena reprovava de vez em quando por ele.
			if (alvoEm is { } la) ForjarV2("AlvoDaBola", la, 10_000).Ficha.Ki = 0;
			EscutaDeDecalques = [];
			int antes = BolasEstouradasDeTeste;
			Projetil bola = Disparar(atirador, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = alcance, Nome = "Bola de Ki",
			}, verbo: "Basic_Blast");
			ajuste?.Invoke(bola);
			for (int i = 0; i < 30 * 20 && bola.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			int crateras = DecalquesV21(Protocol.Decal.Cratera);
			EscutaDeDecalques = null;
			return (bola, crateras, BolasEstouradasDeTeste - antes);
		}

		var a = Bola(6);
		double andou = (a.Bola.Pos.X - mao.X) / tile;
		AfirmarV21("**A BOLA QUE CHEGA AO FIM DO ALCANCE ESTOURA** (`FimDeProjetil.Estourou`), em vez de se apagar",
				   a.Bola is { Vivo: false, Fim: FimDeProjetil.Estourou } && a.Estouradas == 1, $"fim {a.Bola.Fim}; estouradas {a.Estouradas}");
		AfirmarV21("...no ponto em que o alcance acabou (seis tiles de voo, a partir da boca do cano)",
				   andou is > 6.4 and < 7.6, $"{andou:0.##} tiles da mao");
		AfirmarV21("...e deixa UMA cratera pequena no chao (`Decal.Cratera`)", a.Crateras == 1, $"{a.Crateras} cratera(s)");

		var prazo = Bola(30, b => b.VidaRestante = 0.2);
		AfirmarV21("a bola cujo PRAZO vence no ar tambem estoura (o `Burnout` do DM termina em `explode()`, `objects.dm:652-661`)",
				   prazo.Bola.Fim == FimDeProjetil.Estourou && prazo.Crateras == 1 && (prazo.Bola.Pos.X - mao.X) / tile < 12,
				   $"fim {prazo.Bola.Fim}; {prazo.Crateras} cratera(s); a {(prazo.Bola.Pos.X - mao.X) / tile:0.#} tiles");

		KiContraCorpo.BolaSomeAoAcabarDeTeste = true;
		try
		{
			var some = Bola(6);
			AfirmarV21("(defeito injetado: a bola some ao acabar) a mesma bola se APAGA no fim do alcance, sem estouro nem cratera -- a v0.2.0",
					   some.Bola.Fim == FimDeProjetil.Apagou && some.Crateras == 0 && some.Estouradas == 0, $"fim {some.Bola.Fim}; {some.Crateras} cratera(s)");
		}
		finally { KiContraCorpo.BolaSomeAoAcabarDeTeste = false; }

		// ---- UMA CRATERA POR LUGAR
		Bola(6);
		var repetida = Bola(6, crateraLimpa: false);
		var longe = Bola(6, crateraLimpa: false, deOnde: mao + new Vec2(5 * tile, 0));
		AfirmarV21("**UMA CRATERA POR LUGAR**: a segunda bola que estoura no mesmo ponto estoura, mas NAO abre outra (`range(3)`, `objects.dm:464`)",
				   repetida.Bola.Fim == FimDeProjetil.Estourou && repetida.Crateras == 0, $"fim {repetida.Bola.Fim}; {repetida.Crateras} cratera(s)");
		AfirmarV21("...e a que estoura cinco tiles adiante abre a dela", longe.Bola.Fim == FimDeProjetil.Estourou && longe.Crateras == 1, $"{longe.Crateras} cratera(s)");

		KiContraCorpo.BolaSemCrateraDeTeste = true;
		try
		{
			var semCratera = Bola(6);
			AfirmarV21("(defeito injetado: a bola sem cratera) ela estoura e o chao fica liso", semCratera.Bola.Fim == FimDeProjetil.Estourou && semCratera.Crateras == 0,
					   $"{semCratera.Crateras} cratera(s)");
		}
		finally { KiContraCorpo.BolaSemCrateraDeTeste = false; }

		// ---- QUEM NAO ESTOURA
		var invisivel = Bola(6, b => b.Invisivel = true);
		var inerte = Bola(30, b => { b.Inerte = true; b.VidaRestante = 0.2; });
		AfirmarV21("o que NINGUEM VE (a lamina de ar do Kiai) e o que ainda esta SENDO FORMADO (o alvo de treino) continuam so se apagando",
				   invisivel.Bola.Fim == FimDeProjetil.Apagou && invisivel.Crateras == 0 && inerte.Bola.Fim == FimDeProjetil.Apagou && inerte.Crateras == 0,
				   $"invisivel {invisivel.Bola.Fim}/{invisivel.Crateras}; inerte {inerte.Bola.Fim}/{inerte.Crateras}");

		var doAlto = Bola(6, altitude: Voo.AlturaQueAtravessa + 1);
		AfirmarV21("a bola disparada LA DO ALTO estoura no ar: sem cratera (so marca o chao o que estoura perto dele)",
				   doAlto.Bola.Fim == FimDeProjetil.Estourou && doAlto.Crateras == 0, $"fim {doAlto.Bola.Fim}; {doAlto.Crateras} cratera(s)");

		// ---- A BOLA QUE ACABA NUM CORPO
		var noCorpo = Bola(20, alvoEm: mao + new Vec2(4 * tile, 0));
		AfirmarV21("**A BOLA QUE ESTOURA NUM CORPO TAMBEM MARCA O CHAO**: o fim e `Acertou` e sai a cratera pequena debaixo dele",
				   noCorpo.Bola.Fim == FimDeProjetil.Acertou && noCorpo.Crateras == 1,
				   $"fim {noCorpo.Bola.Fim}; {noCorpo.Crateras} cratera(s); a bola parou a {(noCorpo.Bola.Pos.X - mao.X) / tile:0.#} tiles da mao (o alvo esta a 4), viva {noCorpo.Bola.Vivo}");
		KiContraCorpo.BolaSemCrateraDeTeste = true;
		try
		{
			var liso = Bola(20, alvoEm: mao + new Vec2(4 * tile, 0));
			AfirmarV21("(defeito injetado: a bola sem cratera) o acerto no corpo nao marca o chao -- *\"so explode se bater em paredes ou pessoas e n cria a cratera\"*",
					   liso.Bola.Fim == FimDeProjetil.Acertou && liso.Crateras == 0, $"fim {liso.Bola.Fim}; {liso.Crateras} cratera(s)");
		}
		finally { KiContraCorpo.BolaSemCrateraDeTeste = false; }

		_craterasPequenas.Clear();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// K2) O RAIO E A ESFERA GRANDE ESTOURAM ONDE QUER QUE ACABEM
	// =====================================================================
	private void ORaioQueAcabaEstourandoV21()
	{
		GD.Print("[v021] -- K2) O RAIO E A ESFERA GRANDE ESTOURAM ONDE ACABAM: parede, prazo e a mao que solta (item 7)");
		ZoneCollision mapa = _pjMapa!;
		float tile = ZoneCollision.TileSize;
		int deY = 8;
		static double Perda(ServerPlayer pl) => 100 - pl.Combate.Corpo.Vida();

		// ---- (a) NA PAREDE
		(Projetil? Raio, int CraterasGrandes, int Explosoes, bool Achou, Vec2 Muro) RaioNaParede()
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			if (!AcharMuroV21(mapa, livres: 8, vizinhas: 0, ref deY, out int cx, out int cy)) return (null, 0, 0, false, default);
			ServerPlayer atirador = ForjarV2("RaioNoMuro", CentroV21(cx - 7, cy), 10_000);
			EscutaDeDecalques = [];
			int antes = ExplosoesDoFimDoAlcanceDeTeste;
			Projetil? raio = RaioAteOFimV2(atirador, 30);
			int grandes = DecalquesV21(Protocol.Decal.CrateraGrande);
			EscutaDeDecalques = null;
			return (raio, grandes, ExplosoesDoFimDoAlcanceDeTeste - antes, true, CentroV21(cx, cy));
		}

		var muro = RaioNaParede();
		AfirmarV21("(montagem) achei uma parede com oito tiles de chao firme a oeste, e o raio nasceu", muro.Achou && muro.Raio != null);
		AfirmarV21("**O RAIO QUE BATE NA PAREDE ESTOURA ALI** (`FimDeProjetil.Explodiu`), em vez de lascar e sumir",
				   muro.Raio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && muro.Explosoes == 1, $"fim {muro.Raio?.Fim}; {muro.Explosoes} explosao(oes)");
		AfirmarV21("...encostado nela, e nao no fim do alcance (30 tiles)",
				   muro.Raio != null && Vec2.Distance(muro.Raio.Pos, muro.Muro) < 1.6f * tile, $"a {(muro.Raio == null ? -1 : Vec2.Distance(muro.Raio.Pos, muro.Muro) / tile):0.##} tiles do muro");
		AfirmarV21("...com a CRATERA GRANDE (a das transformacoes de raiva)", muro.CraterasGrandes == 1, $"{muro.CraterasGrandes} cratera(s) grande(s)");

		KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = true;
		try
		{
			var semEstouro = RaioNaParede();
			AfirmarV21("(defeito injetado: so estoura no fim do alcance) o raio na parede acaba em `Cenario`, sem explosao nem cratera -- a v0.2.0",
					   semEstouro.Raio is { Fim: FimDeProjetil.Cenario } && semEstouro.Explosoes == 0 && semEstouro.CraterasGrandes == 0,
					   $"fim {semEstouro.Raio?.Fim}; {semEstouro.Explosoes} explosao(oes); {semEstouro.CraterasGrandes} cratera(s)");
		}
		finally { KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = false; }

		// ---- (b) DE PRAZO, LEVANDO ALGUEM
		Vec2 mao = TerraFirmeG12(16);
		(Projetil? Raio, double Perda, int CraterasGrandes, bool Levou) RaioQueMorreDePrazo()
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			ServerPlayer atirador = ForjarV2("RaioDePrazo", mao, 10_000);
			ServerPlayer fraco = ForjarV2("LevadoDePrazo", mao + new Vec2(3 * tile, 0), 5_000);
			atirador.Facing = Facing.East;
			ReceitaDeProjetil receita = SemDeflexao();
			receita.AlcanceTiles = 30;
			EscutaDeDecalques = [];
			Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), receita);
			Projetil? raio = null;
			bool levou = false;
			for (int i = 0; i < 30 * 12; i++)
			{
				raio ??= ProjeteisDaZona(atirador.Zone.Hash).FirstOrDefault(q => q.Dono == atirador.Id);
				if (raio != null && !levou && raio.Arrastando == fraco.Id)
				{
					levou = true;
					raio.VidaRestante = 0.4;   // o prazo vence com o corpo a caminho, longe do fim do alcance
				}
				TiqueDoParry(atirador);
				if (raio is { Vivo: false }) break;
			}
			int grandes = DecalquesV21(Protocol.Decal.CrateraGrande);
			EscutaDeDecalques = null;
			return (raio, Perda(fraco), grandes, levou);
		}

		var dePrazo = RaioQueMorreDePrazo();
		AfirmarV21("(montagem) o raio pegou o mais fraco e o vinha levando quando o prazo foi encurtado", dePrazo.Levou && dePrazo.Raio != null);
		AfirmarV21("**O RAIO QUE MORRE DE PRAZO LEVANDO ALGUEM ESTOURA** -- era o caso da queixa: ele levava devagar e sumia calado antes do fim do alcance",
				   dePrazo.Raio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && dePrazo.CraterasGrandes == 1,
				   $"fim {dePrazo.Raio?.Fim}; {dePrazo.CraterasGrandes} cratera(s) grande(s)");
		KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = true;
		try
		{
			var calado = RaioQueMorreDePrazo();
			AfirmarV21("(defeito injetado: so estoura no fim do alcance) o mesmo raio se APAGA sem cratera, e quem vinha na ponta apanha menos (sem a explosao)",
					   calado.Levou && calado.Raio is { Fim: FimDeProjetil.Apagou } && calado.CraterasGrandes == 0 && calado.Perda < dePrazo.Perda - 1e-6,
					   $"fim {calado.Raio?.Fim}; {calado.CraterasGrandes} cratera(s); perdeu {calado.Perda:0.###} contra {dePrazo.Perda:0.###} com o estouro");
		}
		finally { KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = false; }

		// ---- (c) A MAO SOLTA O RAIO QUE ESTAVA EM CIMA DE UM IGUAL
		(Projetil? Raio, int CraterasGrandes, bool Encostou) RaioSoltoEmCimaDeAlguem(bool comAlvo)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			ServerPlayer atirador = ForjarV2("RaioSolto", mao, 10_000);
			ServerPlayer? igual = comAlvo ? ForjarV2("IgualNaPonta", mao + new Vec2(4 * tile, 0), 10_000) : null;
			atirador.Facing = Facing.East;
			ReceitaDeProjetil receita = SemDeflexao();
			receita.AlcanceTiles = 12;
			EscutaDeDecalques = [];
			Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), receita);
			Projetil? raio = null;
			bool encostou = false, soltou = false;
			for (int i = 0; i < 30 * 12; i++)
			{
				raio ??= ProjeteisDaZona(atirador.Zone.Hash).FirstOrDefault(q => q.Dono == atirador.Id);
				if (raio != null && !soltou && (igual == null ? raio.Comprimento > 2 * tile : raio.Encostado))
				{
					encostou = raio.Encostado;
					soltou = true;
					SoltarCanal(atirador, "Ki_Wave");
				}
				TiqueDoParry(atirador);
				if (raio is { Vivo: false }) break;
			}
			int grandes = DecalquesV21(Protocol.Decal.CrateraGrande);
			EscutaDeDecalques = null;
			return (raio, grandes, encostou);
		}

		var solto = RaioSoltoEmCimaDeAlguem(comAlvo: true);
		AfirmarV21("(montagem) o raio encostou num corpo de poder IGUAL (que ele fere e nao leva), e a mao soltou ali", solto.Encostou && solto.Raio != null);
		AfirmarV21("**A MAO SOLTA O RAIO QUE ESTAVA EM CIMA DE ALGUEM, E ELE ESTOURA NELE**: a cauda chega na cabeca plantada e o fim e `Explodiu`, com cratera",
				   solto.Raio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && solto.CraterasGrandes == 1, $"fim {solto.Raio?.Fim}; {solto.CraterasGrandes} cratera(s)");
		KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = true;
		try
		{
			var cessa = RaioSoltoEmCimaDeAlguem(comAlvo: true);
			AfirmarV21("(defeito injetado: so estoura no fim do alcance) o mesmo raio so CESSA em cima do corpo, sem clarao -- a v0.2.0",
					   cessa.Encostou && cessa.Raio is { Fim: FimDeProjetil.Cessou } && cessa.CraterasGrandes == 0, $"fim {cessa.Raio?.Fim}; {cessa.CraterasGrandes} cratera(s)");
		}
		finally { KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = false; }

		var noAr = RaioSoltoEmCimaDeAlguem(comAlvo: false);
		AfirmarV21("o raio solto NO AR, sem ninguem na ponta, voa ate o fim do alcance e estoura la (como ja era)",
				   noAr.Raio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && (noAr.Raio.Pos.X - mao.X) / tile > 11, $"fim {noAr.Raio?.Fim}; a {(noAr.Raio == null ? -1 : (noAr.Raio.Pos.X - mao.X) / tile):0.#} tiles");

		// ---- (d) A ESFERA GRANDE NA PAREDE
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		if (AcharMuroV21(mapa, livres: 8, vizinhas: 0, ref deY, out int ex, out int ey))
		{
			ServerPlayer gigante = ForjarV2("EsferaNoMuro", CentroV21(ex - 7, ey), 10_000);
			gigante.Facing = Facing.East;
			EscutaDeDecalques = [];
			Projetil esfera = Disparar(gigante, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30, EscalaVisual = 1.5, Nome = "Esfera Grande",
			});
			for (int i = 0; i < 30 * 20 && esfera.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			AfirmarV21("a ESFERA GRANDE que bate na parede estoura ali com o clarao grande (`Explodiu`, e nao mais `Cenario`) e a cratera grande",
					   esfera.EsferaGrande && esfera.Fim == FimDeProjetil.Explodiu && DecalquesV21(Protocol.Decal.CrateraGrande) == 1
					   && Vec2.Distance(esfera.Pos, CentroV21(ex, ey)) < 1.6f * tile,
					   $"fim {esfera.Fim}; {DecalquesV21(Protocol.Decal.CrateraGrande)} cratera(s)");
			EscutaDeDecalques = null;
		}
		else AfirmarV21("(montagem) achei uma segunda parede pra esfera grande", false);

		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// K3) O KI CONTRA O CENARIO
	// =====================================================================
	private void OKiContraOCenarioV21()
	{
		GD.Print("[v021] -- K3) O KI CONTRA O CENARIO: a batida (regra do soco) e a explosao (item 27)");
		ZoneCollision mapa = _pjMapa!;
		ZoneKey terra = ZonaDaBancadaDeProjetil;
		const int T = ZoneCollision.TileSize;
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();

		// ---- O ESTOURO NA ZONA SEM NINGUEM: o tiro de quem saiu do planeta antes de a bola acabar. A lista de tiros
		// so guarda o hash da zona, e sem corpo nenhum la a chave vem VAZIA (`MapaDaZonaDoHash`).
		{
			string estourou = "";
			int cederam = -1;
			try { cederam = EstragarOCenarioNaExplosao(default, new Vec2(40 * T, 40 * T), 3 * T, 1e9, null); }
			catch (Exception e) { estourou = e.GetType().Name; }
			AfirmarV21("**O ESTOURO NUMA ZONA SEM NINGUEM NAO DERRUBA O TIQUE**: sem chave de zona nao ha mapa a estragar -- a explosao devolve zero, sem excecao",
					   estourou.Length == 0 && cederam == 0, $"cederam {cederam}; {estourou}");
			EstouroSemConferirAZonaDeTeste = true;
			try
			{
				estourou = "";
				try { EstragarOCenarioNaExplosao(default, new Vec2(40 * T, 40 * T), 3 * T, 1e9, null); }
				catch (Exception e) { estourou = e.GetType().Name; }
				AfirmarV21("(defeito injetado: o estouro nao confere a zona) a mesma chamada ESTOURA -- o catalogo e perguntado por um nome nulo, e no jogo a excecao subia ate o tique",
						   estourou == nameof(ArgumentNullException), estourou.Length == 0 ? "nao estourou" : estourou);
			}
			finally { EstouroSemConferirAZonaDeTeste = false; }
		}

		// ---- as paredes simples do mapa, em ordem (cada medida gasta as suas)
		var paredes = new List<(int X, int Y)>();
		for (int y = 10; y < mapa.Height - 10 && paredes.Count < 700; y++)
			for (int x = 10; x < mapa.Width - 10 && paredes.Count < 700; x++)
				if (ParedeSimplesV21(mapa, x, y)) paredes.Add((x, y));
		AfirmarV21("(montagem) a Terra tem pelo menos 600 paredes simples pra medir a chance", paredes.Count >= 600, $"{paredes.Count}");
		if (paredes.Count < 600) return;
		int proxima = 0;
		List<(int X, int Y)> Tomar(int n) { List<(int, int)> l = paredes.GetRange(proxima, n); proxima += n; return l; }

		// ---- (a) A BATIDA: a chance do soco
		int caiu = 0;
		foreach ((int x, int y) in Tomar(300))
		{
			BaterNoCenarioComKi(terra, x, y, 1_000_000, null);
			if (!mapa.BlockedCell(x, y)) caiu++;
		}
		AfirmarV21("**A BATIDA DE KI DERRUBA A PAREDE PELA REGRA DO SOCO**: 300 paredes, uma batida em cada, cai perto de um terco (`prob(34)`, `attack_proc.dm:104`)",
				   caiu is >= 66 and <= 138, $"{caiu} de 300 ({caiu / 3.0:0.#}%)");

		int fraco = 0;
		foreach ((int x, int y) in Tomar(60))
		{
			BaterNoCenarioComKi(terra, x, y, Empurrao.ResistenciaPadrao - 1, null);
			if (!mapa.BlockedCell(x, y)) fraco++;
		}
		AfirmarV21("o tiro que NAO ALCANCA a resistencia do cenario (20) nao derruba nada, por mais que bata", fraco == 0, $"{fraco} de 60");

		KiContraCorpo.KiNaoEstragaCenarioDeTeste = true;
		try
		{
			int nada = 0;
			foreach ((int x, int y) in Tomar(60))
			{
				BaterNoCenarioComKi(terra, x, y, 1_000_000, null);
				if (!mapa.BlockedCell(x, y)) nada++;
			}
			AfirmarV21("(defeito injetado: o ki nao estraga o cenario) 60 batidas de um milhao e nenhuma parede cai -- a v0.2.0", nada == 0, $"{nada} de 60");
		}
		finally { KiContraCorpo.KiNaoEstragaCenarioDeTeste = false; }

		// a beirada do mundo
		int bx = 0, by = mapa.Height / 2;
		bool beiraBarra = mapa.BlockedCell(bx, by);
		for (int i = 0; i < 50; i++) BaterNoCenarioComKi(terra, bx, by, 1e12, null);
		AfirmarV21("a BEIRADA do mundo nao cede ao ki (a recusa e a de sempre: `NaBorda`)", mapa.BlockedCell(bx, by) == beiraBarra && !CaiuV21(bx, by));

		// ---- (b) A BOLA DE VERDADE, da mao ate o muro
		int deY = 8;
		if (AcharMuroV21(mapa, livres: 8, vizinhas: 0, ref deY, out int mx, out int my))
		{
			ServerPlayer atirador = ForjarV2("BolaNoMuro", CentroV21(mx - 6, my), 10_000);
			atirador.Facing = Facing.East;
			int tiros = 0, noMuro = 0;
			while (tiros < 120 && mapa.BlockedCell(mx, my))
			{
				Projetil bola = Disparar(atirador, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30 }, verbo: "Basic_Blast");
				for (int i = 0; i < 30 * 10 && bola.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
				tiros++;
				if (bola.Fim == FimDeProjetil.Cenario) noMuro++;
			}
			AfirmarV21("**A BOLA DE KI QUEBRA A PAREDE EM QUE BATE**: disparada da mao, cada uma acaba no muro (`Cenario`) ate ele ceder",
					   !mapa.BlockedCell(mx, my) && noMuro == tiros && tiros >= 1, $"{tiros} tiro(s), {noMuro} no muro; muro de pe: {mapa.BlockedCell(mx, my)}");
			Projetil passa = Disparar(atirador, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 8 }, verbo: "Basic_Blast");
			for (int i = 0; i < 30 * 10 && passa.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			AfirmarV21("...e a bola seguinte PASSA pelo buraco que ela abriu",
					   passa.Pos.X > CentroV21(mx, my).X + T / 2f || passa.Fim != FimDeProjetil.Cenario || Vec2.Distance(passa.Pos, CentroV21(mx, my)) > T,
					   $"fim {passa.Fim} em x={passa.Pos.X:0} (muro em {CentroV21(mx, my).X:0})");
			LimparTudoDaBancada();
		}
		else AfirmarV21("(montagem) achei um muro pra bola", false);
		_craterasPequenas.Clear();

		// ---- (c) A EXPLOSAO NAS PAREDES
		int total = 0, cairam = 0, explosoes = 0;
		for (int tentativa = 0; tentativa < 6 && AcharMuroV21(mapa, livres: 1, vizinhas: 8, ref deY, out int ex, out int ey); tentativa++)
		{
			Vec2 centro = CentroV21(ex, ey);
			var naArea = new List<(int X, int Y)>();
			for (int dy = -4; dy <= 4; dy++)
				for (int dx = -4; dx <= 4; dx++)
					if (ParedeSimplesV21(mapa, ex + dx, ey + dy) && Vec2.Distance(CentroV21(ex + dx, ey + dy), centro) <= 4 * T)
						naArea.Add((ex + dx, ey + dy));
			EstragarOCenarioNaExplosao(terra, centro, 4 * T, 1_000_000, null);
			total += naArea.Count;
			cairam += naArea.Count(c => !mapa.BlockedCell(c.X, c.Y));
			explosoes++;
		}
		AfirmarV21("(montagem) achei muros com oito ou mais paredes simples em volta, e havia pelo menos 30 paredes no raio das explosoes", explosoes >= 1 && total >= 30, $"{explosoes} explosao(oes), {total} parede(s)");
		AfirmarV21("**A EXPLOSAO DERRUBA VARIAS PAREDES EM VOLTA**: das que estavam no raio dela, a maioria cai (`prob(80)`, `explosions.dm:178-183`)",
				   total >= 30 && cairam >= total * 0.6 && cairam <= total, $"{cairam} de {total} ({(total == 0 ? 0 : cairam * 100.0 / total):0.#}%)");

		// ---- (d) A EXPLOSAO NO CHAO: racha mais no centro que na beira
		int dentro = 0, dentroCaiu = 0, fora = 0, foraCaiu = 0, pracas = 0, py0 = 8, px0 = 8;
		Vec2 ultimaPraca = default;
		for (int i = 0; i < 10 && AcharPracaFirmeV21(mapa, 9, ref py0, ref px0, out int cx, out int cy); i++)
		{
			Vec2 centro = CentroV21(cx, cy);
			ultimaPraca = centro;
			EstragarOCenarioNaExplosao(terra, centro, 4 * T, 1_000_000, null);
			pracas++;
			for (int dy = -4; dy <= 4; dy++)
				for (int dx = -4; dx <= 4; dx++)
				{
					double d = Vec2.Distance(CentroV21(cx + dx, cy + dy), centro);
					if (d > 4 * T) continue;
					bool caiuAqui = CaiuV21(cx + dx, cy + dy);
					if (d <= 2 * T) { dentro++; if (caiuAqui) dentroCaiu++; }
					else { fora++; if (caiuAqui) foraCaiu++; }
				}
		}
		double fDentro = dentro == 0 ? 0 : (double)dentroCaiu / dentro, fFora = fora == 0 ? 0 : (double)foraCaiu / fora;
		AfirmarV21("(montagem) achei pelo menos seis pracas de 9x9 de chao firme pra explodir", pracas >= 6, $"{pracas}");
		AfirmarV21("**A EXPLOSAO RACHA O CHAO, MAIS NO CENTRO QUE NA BEIRA**: a fracao de celulas rachadas na metade de dentro do raio e maior que na de fora",
				   pracas >= 6 && fDentro > fFora && fDentro is > 0.2 and < 0.6 && fFora is > 0.07 and < 0.3,
				   $"dentro {dentroCaiu}/{dentro} ({fDentro:P0}), fora {foraCaiu}/{fora} ({fFora:P0})");

		// a forca que nao alcanca: nada racha
		int py1 = py0, px1 = px0;
		if (AcharPracaFirmeV21(mapa, 9, ref py1, ref px1, out int fx, out int fy))
		{
			int cedeu = EstragarOCenarioNaExplosao(terra, CentroV21(fx, fy), 4 * T, Empurrao.ResistenciaPadrao - 1, null);
			AfirmarV21("a explosao de um tiro que NAO alcanca a resistencia do cenario nao racha nada", cedeu == 0, $"{cedeu} celula(s)");

			KiContraCorpo.KiNaoEstragaCenarioDeTeste = true;
			try
			{
				int nada = EstragarOCenarioNaExplosao(terra, CentroV21(fx, fy), 4 * T, 1_000_000, null);
				AfirmarV21("(defeito injetado: o ki nao estraga o cenario) a explosao de um milhao nao racha uma celula", nada == 0, $"{nada} celula(s)");
			}
			finally { KiContraCorpo.KiNaoEstragaCenarioDeTeste = false; }

			// ---- (e) A CONSTRUCAO E O BLOCO ERGUIDO
			var obra = new Obra { Id = 984_210, Tipo = "Android_Creation_Mainframe", Aparafusada = true, X = CentroV21(fx + 1, fy).X, Y = CentroV21(fx + 1, fy).Y, ArmaduraMax = 100, Armadura = 100 };
			obra.PorZona(terra);
			_noChao.Add(obra);
			try
			{
				EstragarOCenarioNaExplosao(terra, CentroV21(fx, fy), 4 * T, 60, null);
				AfirmarV21("a CONSTRUCAO no raio da explosao leva a pancada na armadura dela: a que nao passa do piso (75%) nem arranha", Math.Abs(obra.Armadura - 100) < 1e-9, $"armadura {obra.Armadura:0.##}");
				EstragarOCenarioNaExplosao(terra, CentroV21(fx, fy), 4 * T, 1_000, null);
				AfirmarV21("**...E A QUE PASSA A DERRUBA**: *\"paredes e construçoes tb levam o dano\"*", !_noChao.Contains(obra) || obra.Armadura <= 0, $"armadura {obra.Armadura:0.##}; no chao {_noChao.Contains(obra)}");
			}
			finally { _noChao.Remove(obra); AplicarColisaoDasObras(terra); }

			if (Bloco(ClasseDeBloco.Parede) is { } parede)
			{
				ZerarBases();
				ServerPlayer pedreiro = ForjarV2("Pedreiro", CentroV21(fx - 1, fy + 2), 1_000);
				pedreiro.Conta = "bancada_pedreiro";
				pedreiro.Ficha.Zeni = 1_000_000;
				ComandoDeBloco(pedreiro, Protocol.BlocoErguer, fx, fy + 3, parede.Numero, "");
				BlocoDePe? bloco = BlocoEm(terra, fx, fy + 3);
				AfirmarV21("(montagem) o pedreiro ergueu uma parede", bloco != null);
				if (bloco != null)
				{
					double r = bloco.Resistencia;
					EstragarOCenarioNaExplosao(terra, CentroV21(fx, fy + 3), 2 * T, r * 0.9, null);
					bool aguentou = BlocoEm(terra, fx, fy + 3) != null;
					EstragarOCenarioNaExplosao(terra, CentroV21(fx, fy + 3), 2 * T, r * 1.5, null);
					AfirmarV21("o BLOCO ERGUIDO aguenta a explosao que nao alcanca a resistencia dele, e cai na que alcanca (sem sorteio, como no arremesso)",
							   aguentou && BlocoEm(terra, fx, fy + 3) == null, $"resistencia {r:0.#}; aguentou a fraca: {aguentou}; de pe depois da forte: {BlocoEm(terra, fx, fy + 3) != null}");
				}
				ZerarBases();
			}
			else AfirmarV21("(montagem) o catalogo de blocos tem parede", false);
		}
		else AfirmarV21("(montagem) achei mais uma praca firme", false);

		// ---- (f) A AGUA NAO RACHA
		bool aguaRachou = false;
		if (_cenarioCaido.TryGetValue(ChaveDeEstado(terra), out HashSet<(int X, int Y)>? caidas))
			foreach ((int x, int y) in caidas) aguaRachou |= mapa.EhAgua(x, y);
		AfirmarV21("nenhuma celula de AGUA rachou em toda a bancada", !aguaRachou);

		// ---- (g) DE PONTA A PONTA: o raio de verdade no muro, e a explosao dele no cenario
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		int antesDoRaio = CelulasQuebradasPorKiDeTeste, paredesNoRaio = 0, tentativas = 0, cairamNoRaio = 0;
		while (tentativas < 4 && AcharMuroV21(mapa, livres: 8, vizinhas: 6, ref deY, out int rx, out int ry))
		{
			tentativas++;
			var antes = new List<(int X, int Y)>();
			for (int dy = -4; dy <= 4; dy++)
				for (int dx = -4; dx <= 4; dx++)
					if (ParedeSimplesV21(mapa, rx + dx, ry + dy) && Vec2.Distance(CentroV21(rx + dx, ry + dy), CentroV21(rx, ry)) <= 3 * T)
						antes.Add((rx + dx, ry + dy));
			ServerPlayer atirador = ForjarV2("RaioNoCenario", CentroV21(rx - 7, ry), 10_000);
			Projetil? raio = RaioAteOFimV2(atirador, 30);
			if (raio is not { Fim: FimDeProjetil.Explodiu }) continue;
			paredesNoRaio += antes.Count;
			cairamNoRaio += antes.Count(c => !mapa.BlockedCell(c.X, c.Y));
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
		}
		AfirmarV21("**DE PONTA A PONTA**: o Ki Wave disparado da mao estoura no muro, e a explosao dele derruba paredes em volta",
				   paredesNoRaio >= 6 && cairamNoRaio >= 2 && CelulasQuebradasPorKiDeTeste > antesDoRaio,
				   $"{cairamNoRaio} de {paredesNoRaio} parede(s) a tres tiles do impacto, em {tentativas} raio(s); contador +{CelulasQuebradasPorKiDeTeste - antesDoRaio}");

		// ---- (h) LA DO ALTO, a explosao nao toca o chao
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Vec2 mao = TerraFirmeG12(16);
		ServerPlayer voador = ForjarV2("RaioDoAlto", mao, 10_000);
		voador.Altitude = Voo.AlturaQueAtravessa + 1;
		int antesDoAlto = CelulasQuebradasPorKiDeTeste;
		Projetil? alto = RaioAteOFimV2(voador, 6);
		AfirmarV21("a explosao do raio disparado LA DO ALTO estoura no ar: nenhuma celula do chao racha",
				   alto is { Fim: FimDeProjetil.Explodiu } && CelulasQuebradasPorKiDeTeste == antesDoAlto, $"fim {alto?.Fim}; contador +{CelulasQuebradasPorKiDeTeste - antesDoAlto}");

		// ---- (i) O SOPRO: a onda de choque bate no cenario dos oito lados
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		if (AcharMuroV21(mapa, livres: 3, vizinhas: 0, ref deY, out int sx, out int sy))
		{
			// (o corpo fica na celula a oeste do muro: a posicao e a dos pes, como a do soco)
			ServerPlayer soprador = ForjarV2("Soprador", new Vec2(CentroV21(sx - 1, sy).X, CentroV21(sx - 1, sy).Y - MoveRules.FeetOffsetY), 10_000);
			soprador.Facing = Facing.East;
			int sopros = 0;
			while (sopros < 150 && mapa.BlockedCell(sx, sy)) { OndaDeChoqueG6(soprador); sopros++; }
			AfirmarV21("**A ONDA DE CHOQUE QUEBRA A PAREDE VIZINHA** -- *\"esferas de ki, kiai etc deveriam quebrar paredes assim como soco\"*",
					   !mapa.BlockedCell(sx, sy), $"{sopros} sopro(s); muro de pe: {mapa.BlockedCell(sx, sy)}");
		}
		else AfirmarV21("(montagem) achei um muro pro sopro", false);

		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// K5) KI CONTRA KI
	// =====================================================================
	private void OKiContraOKiV21()
	{
		GD.Print("[v021] -- K5) KI CONTRA KI: bola x bola, raio x bola, a bola no tronco, e a disputa com a esfera grande (item 32)");
		float tile = ZoneCollision.TileSize;
		Vec2 mao = TerraFirmeG12(24);
		Vec2 doOutroLado = mao + new Vec2(14 * tile, 0);
		static double Perda(ServerPlayer pl) => 100 - pl.Combate.Corpo.Vida();
		static ReceitaDeProjetil Bola(double escala = 0, double baseDano = 1) => new()
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = baseDano, Velocidade = 1, AlcanceTiles = 30, EscalaVisual = escala, Nome = "Bola",
		};

		// ---- DUAS BOLAS, uma contra a outra
		(Projetil A, Projetil B, ServerPlayer DonoA, ServerPlayer DonoB, int Choques, double Razao) Duelo(
			double bpA, double bpB, double escalaA = 0, double escalaB = 0, double baseA = 1, double baseB = 1,
			float alturaDeB = 0, bool mesmoDono = false, int tiques = 30 * 6)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			_craterasPequenas.Clear();
			ServerPlayer a = ForjarV2("DueloA", mao, bpA);
			ServerPlayer b = ForjarV2("DueloB", doOutroLado, bpB);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			b.Altitude = alturaDeB;
			int antes = ChoquesDeKiDeTeste;
			Projetil pa = Disparar(a, Bola(escalaA, baseA), verbo: "Basic_Blast");
			Projetil pb = mesmoDono
				? Disparar(a, Bola(escalaB, baseB), rumoDado: new Vec2(-1, 0), deOnde: doOutroLado + new Vec2(-tile, 0), verbo: "Basic_Blast")
				: Disparar(b, Bola(escalaB, baseB), verbo: "Basic_Blast");
			double razao = pa.PoderDeEmbate() / Math.Max(pb.PoderDeEmbate(), 1e-9);
			for (int i = 0; i < tiques && (pa.Vivo || pb.Vivo); i++) TickDosProjeteis(Protocol.TickSeconds);
			return (pa, pb, a, b, ChoquesDeKiDeTeste - antes, razao);
		}
		bool NoMeio(Projetil p) => Math.Abs(p.Pos.X - (mao.X + 7 * tile)) < 2.5f * tile;

		var iguais = Duelo(10_000, 10_000);
		AfirmarV21("(montagem) duas bolas iguais, de dois corpos iguais: a razao de forca e 1", Math.Abs(iguais.Razao - 1) < 0.05, $"razao {iguais.Razao:0.###}");
		AfirmarV21("**BOLA CONTRA BOLA, IGUAIS: AS DUAS ACABAM** no meio do caminho, estourando (`objects.dm:548-554`)",
				   iguais.A.Fim == FimDeProjetil.Estourou && iguais.B.Fim == FimDeProjetil.Estourou && NoMeio(iguais.A) && NoMeio(iguais.B) && iguais.Choques == 2,
				   $"A {iguais.A.Fim} em x={(iguais.A.Pos.X - mao.X) / tile:0.#}t, B {iguais.B.Fim} em x={(iguais.B.Pos.X - mao.X) / tile:0.#}t; {iguais.Choques} choque(s)");

		var forte = Duelo(200_000, 5_000);
		AfirmarV21("**A MAIS FORTE ATRAVESSA**: acima de 1,3 vezes a forca, a bola fraca estoura no meio e a forte segue ate acertar quem a atirou",
				   forte.Razao > KiContraKi.VezesPraVencer && forte.B.Fim == FimDeProjetil.Estourou && NoMeio(forte.B)
				   && forte.A.Fim == FimDeProjetil.Acertou && Perda(forte.DonoB) > 1e-6 && forte.Choques == 1,
				   $"razao {forte.Razao:0.##}; fraca {forte.B.Fim}; forte {forte.A.Fim}; o dono da fraca perdeu {Perda(forte.DonoB):0.###}; {forte.Choques} choque(s)");

		var minhas = Duelo(10_000, 10_000, mesmoDono: true);
		AfirmarV21("as bolas do MESMO DONO nao se chocam (a rajada nao se desfaz sozinha)", minhas.Choques == 0, $"{minhas.Choques} choque(s)");

		// (dois andares acima: o rasante ainda alcanca o chao -- `Feixe.PodemSeTocar`, a regra de todo tiro)
		var doAlto = Duelo(10_000, 10_000, alturaDeB: Voo.AlturaMaxima);
		AfirmarV21("a bola que passa LA NO ALTO nao encosta na do chao", doAlto.Choques == 0, $"{doAlto.Choques} choque(s)");

		KiContraKi.SemChoqueDeTeste = true;
		try
		{
			var semChoque = Duelo(10_000, 10_000);
			AfirmarV21("(defeito injetado: sem choque de ki) as duas bolas iguais se ATRAVESSAM e cada uma acerta o corpo do outro lado -- a v0.2.0",
					   semChoque.Choques == 0 && semChoque.A.Fim == FimDeProjetil.Acertou && semChoque.B.Fim == FimDeProjetil.Acertou,
					   $"{semChoque.Choques} choque(s); A {semChoque.A.Fim}, B {semChoque.B.Fim}");
		}
		finally { KiContraKi.SemChoqueDeTeste = false; }

		// ---- A ESFERA GRANDE CONTRA A BOLA COMUM (sem disputa: a bola comum nao disputa)
		var engole = Duelo(10_000, 10_000, escalaA: 1.5, baseA: 10);
		AfirmarV21("a ESFERA GRANDE (dez vezes a forca) engole a bola comum e segue: a bola estoura, a esfera nao para nem abre disputa",
				   engole.A.EsferaGrande && engole.B.Fim == FimDeProjetil.Estourou && NoMeio(engole.B) && engole.A.Fim != FimDeProjetil.Estourou && _disputas.Count == 0,
				   $"razao {engole.Razao:0.##}; bola {engole.B.Fim}; esfera {engole.A.Fim}");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		{
			ServerPlayer fraco = ForjarV2("EsferaFraca", mao, 5_000);
			ServerPlayer bruto = ForjarV2("BolaBruta", doOutroLado, 200_000);
			fraco.Facing = Facing.East;
			bruto.Facing = Facing.West;
			EscutaDeDecalques = [];
			Projetil esfera = Disparar(fraco, Bola(1.5), verbo: "Death_Ball");
			Projetil bola = Disparar(bruto, Bola(), verbo: "Basic_Blast");
			for (int i = 0; i < 30 * 3 && esfera.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			AfirmarV21("a esfera grande VENCIDA por uma bola mais forte faz o estouro grande dela ali (`Explodiu`, com a cratera grande), e a bola segue",
					   esfera.Fim == FimDeProjetil.Explodiu && NoMeio(esfera) && DecalquesV21(Protocol.Decal.CrateraGrande) == 1 && bola.Vivo,
					   $"esfera {esfera.Fim} em x={(esfera.Pos.X - mao.X) / tile:0.#}t; {DecalquesV21(Protocol.Decal.CrateraGrande)} cratera(s); bola viva {bola.Vivo}");
			EscutaDeDecalques = null;
		}

		// ---- RAIO CONTRA BOLA, de frente
		(Projetil? Raio, Projetil? Bola, ServerPlayer DonoDoRaio, ServerPlayer DonoDaBola, double Razao, bool RaioParou, bool BolaSeguiu) RaioContraBola(double bpDoRaio, double bpDaBola)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			_craterasPequenas.Clear();
			ServerPlayer a = ForjarV2("RaioDoDuelo", mao, bpDoRaio);
			ServerPlayer b = ForjarV2("BolaDoDuelo", doOutroLado, bpDaBola);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			ReceitaDeProjetil receita = SemDeflexao();
			receita.AlcanceTiles = 30;
			Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), receita);
			Projetil? raio = null, bola = null;
			double razao = 0;
			bool raioParou = false, bolaSeguiu = false;
			for (int i = 0; i < 30 * 8; i++)
			{
				raio ??= ProjeteisDaZona(a.Zone.Hash).FirstOrDefault(q => q.Dono == a.Id);
				if (raio != null && bola == null)
				{
					bola = Disparar(b, Bola(), verbo: "Basic_Blast");
					razao = raio.PoderDeEmbate() / Math.Max(bola.PoderDeEmbate(), 1e-9);
				}
				a.Ficha.Ki = a.Ficha.MaxKi;   // (o canal nao pode cair por falta de Ki no meio da medida)
				TiqueDoParry(a);
				if (raio != null && bola != null)
				{
					raioParou |= raio.Esvaziando || !raio.Vivo;
					bolaSeguiu |= bola.Vivo && bola.Pos.X < mao.X + 4 * tile;
					if (!bola.Vivo && (raioParou || raio.Pos.X > mao.X + 11 * tile)) break;
					if (bolaSeguiu) break;
				}
			}
			return (raio, bola, a, b, razao, raioParou, bolaSeguiu);
		}

		var sonda = RaioContraBola(10_000, 10_000);
		AfirmarV21("(montagem) o raio e a bola nasceram, e mediram forcas", sonda.Raio != null && sonda.Bola != null && sonda.Razao > 0, $"razao {sonda.Razao:0.###}");
		// o poder do dono da bola que deixa as duas forcas IGUAIS (a forca e linear no poder de quem atira)
		double bpDoEmpate = 10_000 * sonda.Razao;
		var empate = RaioContraBola(10_000, bpDoEmpate);
		AfirmarV21("(montagem) com o poder ajustado, o raio e a bola tem a MESMA forca (a faixa do empate, 0,7 a 1,3)",
				   empate.Razao is > 0.85 and < 1.15, $"razao {empate.Razao:0.###} com a bola de um corpo de {bpDoEmpate:0}");
		AfirmarV21("**RAIO CONTRA BOLA DE FORCA IGUAL: A BOLA ESTOURA E O RAIO CONTINUA** (no DM os dois sumiam: divergencia declarada em `KiContraKi`)",
				   empate.Bola is { Fim: FimDeProjetil.Estourou } && !empate.RaioParou && empate.Raio is { Vivo: true },
				   $"bola {empate.Bola?.Fim}; o raio parou: {empate.RaioParou}");
		KiContraKi.RaioCedeNoEmpateDeTeste = true;
		try
		{
			var cede = RaioContraBola(10_000, bpDoEmpate);
			AfirmarV21("(defeito injetado: o raio cede no empate) a mesma bola DESFAZ o raio junto com ela -- a regra do DM",
					   cede.Bola is { Fim: FimDeProjetil.Estourou } && cede.RaioParou, $"bola {cede.Bola?.Fim}; o raio parou: {cede.RaioParou}");
		}
		finally { KiContraKi.RaioCedeNoEmpateDeTeste = false; }

		var bolaForte = RaioContraBola(10_000, bpDoEmpate * 40);
		AfirmarV21("**A BOLA 1,3 VEZES MAIS FORTE PARA O RAIO E SEGUE**: a cabeca dele para (o rastro e engolido) e a bola continua ate quem o atirou",
				   bolaForte.Razao < KiContraKi.VezesPraPerder && bolaForte.RaioParou && bolaForte.BolaSeguiu,
				   $"razao {bolaForte.Razao:0.###}; o raio parou: {bolaForte.RaioParou}; a bola seguiu: {bolaForte.BolaSeguiu}");

		// ---- A BOLA NO TRONCO DE UM RAIO
		(Projetil? Raio, Projetil? Bola, bool RaioInteiro) BolaNoTronco(double bpDaBola)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			_craterasPequenas.Clear();
			ServerPlayer a = ForjarV2("TroncoDoDuelo", mao, 10_000);
			ServerPlayer c = ForjarV2("QuemCruza", mao + new Vec2(-tile, 0), bpDaBola);   // atras de quem atira: o raio nao o alcanca
			// (a cabeca para num corpo de poder igual, a treze tiles: o tronco fica parado e alimentado enquanto a bola o cruza)
			ForjarV2("EscoraDoTronco", mao + new Vec2(13 * tile, 0), 10_000);
			a.Facing = Facing.East;
			ReceitaDeProjetil receita = SemDeflexao();
			receita.AlcanceTiles = 40;
			Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), receita);
			Projetil? raio = null, bola = null;
			bool inteiro = true;
			for (int i = 0; i < 30 * 6; i++)
			{
				raio ??= ProjeteisDaZona(a.Zone.Hash).FirstOrDefault(q => q.Dono == a.Id);
				// a bola sai de meio tile ao sul da linha do raio, no meio do tronco, subindo
				if (raio != null && bola == null && raio.Encostado)
					bola = Disparar(c, Bola(), rumoDado: new Vec2(0, -1), deOnde: mao + new Vec2(6 * tile, 44), verbo: "Basic_Blast");
				a.Ficha.Ki = a.Ficha.MaxKi;
				TiqueDoParry(a);
				if (raio != null && bola != null)
				{
					inteiro &= raio.Vivo && !raio.Esvaziando;
					if (!bola.Vivo || bola.Pos.Y < mao.Y - 3 * tile) break;
				}
			}
			return (raio, bola, inteiro);
		}

		var naTrave = BolaNoTronco(bpDoEmpate);
		AfirmarV21("**A BOLA QUE CRUZA O TRONCO DE UM RAIO ESTOURA NELE** (a mais fraca ou igual), e o raio nem sente",
				   naTrave.Bola is { Fim: FimDeProjetil.Estourou } && Math.Abs(naTrave.Bola.Pos.Y - mao.Y) < 1.5f * tile && naTrave.RaioInteiro,
				   $"bola {naTrave.Bola?.Fim} a {(naTrave.Bola == null ? -1 : Math.Abs(naTrave.Bola.Pos.Y - mao.Y) / tile):0.##}t da linha; raio inteiro {naTrave.RaioInteiro}");
		var atravessa = BolaNoTronco(bpDoEmpate * 40);
		AfirmarV21("...e a 1,3 vezes mais forte ATRAVESSA o tronco e segue viagem (o tronco nao acaba por causa de uma bola)",
				   atravessa.Bola is { Vivo: true } && atravessa.Bola.Pos.Y < mao.Y - 2 * tile && atravessa.RaioInteiro,
				   $"bola viva {atravessa.Bola?.Vivo}, {(atravessa.Bola == null ? 0 : (mao.Y - atravessa.Bola.Pos.Y) / tile):0.#}t alem da linha; raio inteiro {atravessa.RaioInteiro}");

		// ---- A DISPUTA: RAIO ALIMENTADO CONTRA ESFERA GRANDE
		(Projetil? Raio, Projetil? Esfera, ServerPlayer DonoDoRaio, ServerPlayer DonoDaEsfera, bool Disputou, bool PlantouOsDois, int CraterasGrandes) RaioContraEsfera(
			double bpDoRaio, double bpDaEsfera)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			_craterasPequenas.Clear();
			ServerPlayer a = ForjarV2("RaioDaDisputa", mao, bpDoRaio);
			ServerPlayer b = ForjarV2("EsferaDaDisputa", doOutroLado, bpDaEsfera);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			ReceitaDeProjetil receita = SemDeflexao();
			receita.AlcanceTiles = 30;
			EscutaDeDecalques = [];
			Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), receita);
			Projetil? raio = null, esfera = null;
			bool disputou = false, plantou = false;
			for (int i = 0; i < 30 * 40; i++)
			{
				raio ??= ProjeteisDaZona(a.Zone.Hash).FirstOrDefault(q => q.Dono == a.Id);
				if (raio != null && esfera == null) esfera = Disparar(b, Bola(1.5), verbo: "Death_Ball");
				a.Ficha.Ki = a.Ficha.MaxKi;
				b.Ficha.Ki = b.Ficha.MaxKi;
				TiqueDoParry(a);
				if (!disputou && _disputas.Count == 1)
				{
					disputou = true;
					plantou = raio is { EmEmbate: true } && esfera is { EmEmbate: true } && !PodeMexerOCorpo(a) && !PodeMexerOCorpo(b)
							  && _emEmbateDeKi.ContainsKey(a.Id) && _emEmbateDeKi.ContainsKey(b.Id);
				}
				bool acabou = disputou ? _disputas.Count == 0 : esfera is { Vivo: false } || raio is { Vivo: false };
				if (esfera != null && acabou)
				{
					// (mais cinco segundos de jogo, pro ataque que venceu chegar em quem perdeu)
					for (int j = 0; j < 150; j++) { a.Ficha.Ki = a.Ficha.MaxKi; TiqueDoParry(a); }
					break;
				}
			}
			int grandes = DecalquesV21(Protocol.Decal.CrateraGrande);
			EscutaDeDecalques = null;
			return (raio, esfera, a, b, disputou, plantou, grandes);
		}

		var raioVence = RaioContraEsfera(500_000, 10_000);
		AfirmarV21("**RAIO ALIMENTADO CONTRA ESFERA GRANDE, DE FRENTE: COMECA A DISPUTA** -- as duas cabecas presas no embate e os dois donos plantados (quem jogou a esfera a empurra de onde esta)",
				   raioVence.Disputou && raioVence.PlantouOsDois, $"disputou {raioVence.Disputou}; plantou os dois {raioVence.PlantouOsDois}");
		AfirmarV21("**O RAIO MUITO MAIS FORTE VENCE: A ESFERA VOLTA PRA QUEM A JOGOU E ESTOURA NELE** -- muda de dono, faz o estouro grande com cratera, e ele apanha",
				   raioVence.Esfera is { Vivo: false, Fim: FimDeProjetil.Explodiu } && raioVence.Esfera.Dono == raioVence.DonoDoRaio.Id
				   && raioVence.CraterasGrandes >= 1 && Perda(raioVence.DonoDaEsfera) > 1e-6 && Perda(raioVence.DonoDoRaio) < 1e-9
				   && raioVence.Esfera.Pos.X > mao.X + 8 * tile,
				   $"esfera {raioVence.Esfera?.Fim}, dono {raioVence.Esfera?.Dono} (o do raio e {raioVence.DonoDoRaio.Id}), a {(raioVence.Esfera == null ? 0 : (raioVence.Esfera.Pos.X - mao.X) / tile):0.#}t; "
				   + $"{raioVence.CraterasGrandes} cratera(s); quem a jogou perdeu {Perda(raioVence.DonoDaEsfera):0.###}; o do raio {Perda(raioVence.DonoDoRaio):0.###}");

		var esferaVence = RaioContraEsfera(10_000, 500_000);
		AfirmarV21("**A ESFERA MUITO MAIS FORTE VENCE: ENGOLE O RAIO E SEGUE** ate quem o atirou, que apanha",
				   esferaVence.Disputou && esferaVence.Raio is { Vivo: false, Fim: FimDeProjetil.Defletido } && Perda(esferaVence.DonoDoRaio) > 1e-6
				   && Perda(esferaVence.DonoDaEsfera) < 1e-9 && esferaVence.Esfera?.Dono == esferaVence.DonoDaEsfera.Id,
				   $"disputou {esferaVence.Disputou}; raio {esferaVence.Raio?.Fim}; o do raio perdeu {Perda(esferaVence.DonoDoRaio):0.###}; a esfera e de {esferaVence.Esfera?.Dono}");

		KiContraKi.EsferaNaoDisputaDeTeste = true;
		try
		{
			var semDisputa = RaioContraEsfera(500_000, 10_000);
			AfirmarV21("(defeito injetado: a esfera nao disputa) o mesmo encontro nao abre disputa: a forca decide na hora e a esfera estoura no meio do caminho, de quem a jogou",
					   !semDisputa.Disputou && semDisputa.Esfera is { Fim: FimDeProjetil.Explodiu } && semDisputa.Esfera.Dono == semDisputa.DonoDaEsfera.Id,
					   $"disputou {semDisputa.Disputou}; esfera {semDisputa.Esfera?.Fim}, dono {semDisputa.Esfera?.Dono}");
		}
		finally { KiContraKi.EsferaNaoDisputaDeTeste = false; }

		// ---- ESFERA GRANDE CONTRA ESFERA GRANDE
		var gigantes = Duelo(500_000, 10_000, escalaA: 1.5, escalaB: 1.5, tiques: 30 * 3);
		bool disputaDeEsferas = _disputas.Count == 1 && gigantes.A.EmEmbate && gigantes.B.EmEmbate;
		for (int i = 0; i < 30 * 40 && _disputas.Count > 0; i++)
		{
			gigantes.DonoA.Ficha.Ki = gigantes.DonoA.Ficha.MaxKi;
			gigantes.DonoB.Ficha.Ki = gigantes.DonoB.Ficha.MaxKi;
			TiqueDoParry(gigantes.DonoA);
		}
		AfirmarV21("**DUAS ESFERAS GRANDES, DE FRENTE, TAMBEM DISPUTAM** -- e a vencida volta e estoura em quem a jogou",
				   disputaDeEsferas && gigantes.B is { Vivo: false, Fim: FimDeProjetil.Explodiu } && gigantes.B.Dono == gigantes.DonoA.Id && Perda(gigantes.DonoB) > 1e-6,
				   $"disputa {disputaDeEsferas}; a vencida {gigantes.B.Fim}, dono {gigantes.B.Dono}; quem a jogou perdeu {Perda(gigantes.DonoB):0.###}");

		_craterasPequenas.Clear();
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// V) O VIGOR E O KI
	// =====================================================================
	private void OVigorEOKiV21()
	{
		GD.Print("[v021] -- V) O VIGOR E O KI: treino, meditar, soco, voo, o excesso que escorre e o teto de 200% (itens 6, 23, 29, 30, 33, 35)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Vec2 chao = TerraFirmeG12(16);
		const int PorSegundo = 5;   // as passadas do `Treinar`: o tique de ficha

		// ---------------------------------------------------------------- V1) O TREINO CANSA
		(Fighter F, double Gastou, double Subiu) Treino(double segundos, double peso = 1)
		{
			LimparTudoDaBancada();
			ServerPlayer atleta = ForjarV2("Atleta", chao, 1_000);
			Fighter f = atleta.Ficha;
			f.stamina = f.maxstamina;
			f.weight = peso;
			f.train = true;
			double bp0 = f.BP, vigor0 = f.stamina;
			for (int i = 0; i < PorSegundo * segundos; i++) Treinar(atleta);
			return (f, (vigor0 - f.stamina) / f.maxstamina, f.BP - bp0);
		}

		var vinte = Treino(20);
		AfirmarV21("**O TREINO GASTA VIGOR**: vinte segundos de treino tiram 10% do tanque (meio por cento por segundo), e o poder sobe",
				   Math.Abs(vinte.Gastou - 0.10) < 0.006 && vinte.Subiu > 0 && vinte.F.train, $"gastou {vinte.Gastou:P2}; BP +{vinte.Subiu:0.####}; treinando {vinte.F.train}");
		var pesado = Treino(20, peso: 2);
		AfirmarV21("...e com o DOBRO do peso gasta o DOBRO (proporcional ao peso, dono, 2026-10-24 -- no DM era o quadruplo, `0.01*weight*weight`, `Train.dm:88`)",
				   Math.Abs(pesado.Gastou - 0.20) < 0.01, $"gastou {pesado.Gastou:P2}");

		LimparTudoDaBancada();
		{
			ServerPlayer ateOFim = ForjarV2("AteOFim", chao, 1_000);
			Fighter f = ateOFim.Ficha;
			f.stamina = f.maxstamina;
			f.train = true;
			int passadas = 0;
			List<string> falas = Ouvir(() => { while (f.train && passadas < PorSegundo * 600) { Treinar(ateOFim); passadas++; } });
			double segundos = passadas / (double)PorSegundo, sobrou = f.stamina / f.maxstamina;
			AfirmarV21("**...E PARA SOZINHO QUANDO O VIGOR ACABA**: uns tres minutos de treino por tanque, e a flag cai com o vigor nos 5%",
					   !f.train && segundos is > 170 and < 200 && sobrou is >= 0.049 and < 0.06, $"parou aos {segundos:0.#} s com {sobrou:P1} de vigor");
			AfirmarV21("...avisando por que parou", falas.Exists(l => l.Contains("exausto", StringComparison.OrdinalIgnoreCase)), string.Join(" | ", falas));
			double bpParado = f.BP;
			for (int i = 0; i < PorSegundo * 10; i++) Treinar(ateOFim);
			AfirmarV21("...e exausto ninguem volta a treinar sozinho (a flag fica caida e o poder nao sobe mais)", !f.train && f.BP - bpParado < 1e-9);
		}

		Cansaco.TreinoNaoCansaDeTeste = true;
		try
		{
			var deGraca = Treino(60);
			AfirmarV21("(defeito injetado: o treino nao cansa) um minuto de treino e o vigor nao se mexe -- a v0.2.0, em que dava pra treinar pra sempre",
					   Math.Abs(deGraca.Gastou) < 1e-9 && deGraca.F.train, $"gastou {deGraca.Gastou:P2}");
		}
		finally { Cansaco.TreinoNaoCansaDeTeste = false; }

		// ---------------------------------------------------------------- V2) O MEDITAR RENDE MENOS
		(double Treino, double Meditar) Ganhos(double potencial)
		{
			LimparTudoDaBancada();
			ServerPlayer suado = ForjarV2("Suado", chao, 1_000), sentado = ForjarV2("Sentado", chao + new Vec2(64, 0), 1_000);
			foreach (ServerPlayer p in new[] { suado, sentado })
			{
				p.Ficha.hiddenpotential = potencial;
				p.Ficha.stamina = p.Ficha.maxstamina;
			}
			suado.Ficha.train = true;
			sentado.Ficha.med = true;
			double a0 = suado.Ficha.BP, b0 = sentado.Ficha.BP;
			for (int i = 0; i < PorSegundo * 60; i++) { Treinar(suado); Treinar(sentado); }
			return (suado.Ficha.BP - a0, sentado.Ficha.BP - b0);
		}

		var comum = Ganhos(1);
		AfirmarV21("**MEDITAR RENDE MENOS BP QUE TREINAR**: um minuto de cada, no mesmo corpo -- o treino rende varias vezes mais",
				   comum.Meditar > 0 && comum.Treino > comum.Meditar * 3, $"treino +{comum.Treino:0.#####}, meditar +{comum.Meditar:0.#####} ({comum.Treino / Math.Max(comum.Meditar, 1e-12):0.#}x)");
		var comPotencial = Ganhos(50_000);
		AfirmarV21("**...TAMBEM EM QUEM TEM MUITO POTENCIAL ESCONDIDO** (era o caso da queixa: a parcela do potencial e quase igual nas duas atividades)",
				   comPotencial.Meditar > 0 && comPotencial.Treino > comPotencial.Meditar * 1.8,
				   $"treino +{comPotencial.Treino:0.####}, meditar +{comPotencial.Meditar:0.####} ({comPotencial.Treino / Math.Max(comPotencial.Meditar, 1e-12):0.##}x)");
		GainKnobs.MeditarRende = 1;
		try
		{
			var igual = Ganhos(50_000);
			AfirmarV21("(defeito injetado: o meditar rende inteiro) com potencial alto os dois rendem quase o MESMO -- *\"meditar e training tao dano o mesmo bp/s\"*",
					   igual.Treino < igual.Meditar * 1.25, $"treino +{igual.Treino:0.####}, meditar +{igual.Meditar:0.####} ({igual.Treino / Math.Max(igual.Meditar, 1e-12):0.##}x)");
		}
		finally { GainKnobs.MeditarRende = _v21MeditarRendeDeFabrica; }

		// ---------------------------------------------------------------- V3) O SOCO GASTA KI
		LimparTudoDaBancada();
		{
			ServerPlayer lutador = ForjarV2("Lutador", chao, 10_000);
			Fighter f = lutador.Ficha;
			double Soco(Protocol.Golpe golpe)
			{
				lutador.Combate.Tick(30);   // a recarga do golpe anterior passa
				f.Ki = f.MaxKi;
				Atacar(lutador, golpe);
				return (f.MaxKi - f.Ki) / f.MaxKi;
			}
			double leve = Soco(Protocol.Golpe.Leve), pesadoKi = Soco(Protocol.Golpe.Pesado);
			AfirmarV21("**O SOCO GASTA UM POUCO DE KI**: 0,4% do tanque no golpe leve e o triplo no pesado, acerte ou nao",
					   Math.Abs(leve - 0.004) < 1e-6 && Math.Abs(pesadoKi - 0.012) < 1e-6, $"leve {leve:P2}, pesado {pesadoKi:P2}");
			lutador.Combate.Tick(30);
			f.Ki = 0;
			lutador.AtaqueAte = 0;
			Atacar(lutador, Protocol.Golpe.Leve);
			AfirmarV21("...e SEM KI o soco sai do mesmo jeito (o tanque nao fica negativo)", lutador.AtaqueAte > 0 && f.Ki == 0, $"ki {f.Ki}; pose de soco ate {lutador.AtaqueAte}");
			Cansaco.SocoDeGracaDeTeste = true;
			try
			{
				AfirmarV21("(defeito injetado: o soco de graca) o golpe sai e o Ki nao se mexe -- a v0.2.0", Soco(Protocol.Golpe.Leve) == 0);
			}
			finally { Cansaco.SocoDeGracaDeTeste = false; }
		}

		// ---------------------------------------------------------------- V4) O VOO GASTA KI
		(double Fracao, double Absoluto, double Tanque) Voo1s(double baseKi, bool rapido = false, bool admin = false, double pericia = 100)
		{
			LimparTudoDaBancada();
			ServerPlayer voador = ForjarV2("Voador", chao, 10_000);
			Fighter f = voador.Ficha;
			f.baseKi = baseKi;
			f.flightability = pericia;   // (o veterano: no DM ele paga o minimo, um ponto de Ki por tique)
			f.Tick(agoraMs: NowMs());
			f.Ki = f.MaxKi;
			if (admin) voador.Poderes |= Protocol.Poder.Admin;
			voador.Voando = true;
			voador.VooRapido = rapido;
			voador.Altitude = Voo.AlturaQueAtravessa;
			for (int i = 0; i < 30; i++) TickDoVoo(voador, (float)Protocol.TickSeconds);
			return ((f.MaxKi - f.Ki) / f.MaxKi, f.MaxKi - f.Ki, f.MaxKi);
		}

		var tanqueGrande = Voo1s(20_000);
		AfirmarV21("**VOAR GASTA KI DE VERDADE**: num tanque grande (onde o custo absoluto do DM sumia) um segundo no ar custa 0,3% dele",
				   tanqueGrande.Tanque > 5_000 && Math.Abs(tanqueGrande.Fracao - Cansaco.PisoDoVooPorSegundo) < 0.0003,
				   $"tanque {tanqueGrande.Tanque:0}; gastou {tanqueGrande.Absoluto:0.##} ({tanqueGrande.Fracao:P3})");
		var veloz = Voo1s(20_000, rapido: true);
		AfirmarV21("...e o voo RAPIDO custa quatro vezes mais", Math.Abs(veloz.Fracao - Cansaco.PisoDoVooRapidoPorSegundo) < 0.0012, $"gastou {veloz.Fracao:P3}");
		var novato = Voo1s(100, pericia: 25);
		AfirmarV21("o NOVATO, de tanque pequeno, continua pagando o custo do DM (`Stats.dm:415`), que e MAIOR que o piso",
				   novato.Fracao > Cansaco.PisoDoVooPorSegundo * 2, $"tanque {novato.Tanque:0.#}; gastou {novato.Absoluto:0.##} ({novato.Fracao:P2})");
		var doAdmin = Voo1s(20_000, admin: true);
		AfirmarV21("**O ADMIN TAMBEM PAGA** (ele voava de graca -- era por isso que voar parecia nao gastar)",
				   Math.Abs(doAdmin.Fracao - Cansaco.PisoDoVooPorSegundo) < 0.0003, $"gastou {doAdmin.Fracao:P3}");
		bool vooDeTesteDeAntes = _vooDeTeste;
		Cansaco.VooSemPisoDeTeste = true;
		_vooDeTeste = false;
		try
		{
			var semPiso = Voo1s(20_000);
			var adminDeGraca = Voo1s(20_000, admin: true);
			AfirmarV21("(defeito injetado: o voo sem piso) no tanque grande o custo volta a ser uma migalha, e o admin volta a voar de graca -- a v0.2.0",
					   semPiso.Fracao < Cansaco.PisoDoVooPorSegundo / 3 && semPiso.Fracao > 0 && adminDeGraca.Absoluto < 1e-9,
					   $"comum {semPiso.Fracao:P4}; admin {adminDeGraca.Absoluto:0.###}");
		}
		finally { Cansaco.VooSemPisoDeTeste = false; _vooDeTeste = vooDeTesteDeAntes; }

		// ---------------------------------------------------------------- V5) O KI ACIMA DE 100% ESCORRE
		static Fighter Carregado(double razao)
		{
			var f = new Fighter { Race = "Human", BP = 10_000 };
			f.Tick(agoraMs: 0);
			f.Ki = f.MaxKi * razao;
			return f;
		}
		static double SegundosAteCem(Fighter f, bool reunindo = false, double teto = 300)
		{
			double t = 0;
			for (; t < teto && f.Ki > f.MaxKi * 1.0005; t += 1.0 / 30) CargaDeKi.PrecoDoExcesso(f, 1.0 / 30, reunindo);
			return t;
		}
		static double PerdaEmUmSegundo(double razao)
		{
			Fighter f = Carregado(razao);
			for (int i = 0; i < 30; i++) CargaDeKi.PrecoDoExcesso(f, 1.0 / 30);
			return razao - f.Ki / f.MaxKi;
		}

		double de140 = SegundosAteCem(Carregado(1.4)), de200 = SegundosAteCem(Carregado(2.0));
		AfirmarV21("**O KI ACIMA DE 100% ESCORRE**: de 140% volta-se a 100% em meio minuto, e de 200% em menos de um (era uma hora)",
				   de140 is > 22 and < 40 && de200 is > 28 and < 56 && de200 > de140, $"de 140%: {de140:0.#} s; de 200%: {de200:0.#} s");
		double p120 = PerdaEmUmSegundo(1.2), p160 = PerdaEmUmSegundo(1.6), p200 = PerdaEmUmSegundo(2.0);
		AfirmarV21("**QUANTO MAIOR O EXCESSO, MAIS RAPIDO ELE CAI**: em um segundo, quem esta em 200% perde mais que quem esta em 160%, que perde mais que quem esta em 120%",
				   p200 > p160 * 1.3 && p160 > p120 * 1.3 && p120 > 0.01, $"120%: -{p120:P2}; 160%: -{p160:P2}; 200%: -{p200:P2}");
		Fighter segurando = Carregado(1.4);
		for (int i = 0; i < 30 * 30; i++) CargaDeKi.PrecoDoExcesso(segurando, 1.0 / 30, reunindo: true);
		AfirmarV21("quem esta REUNINDO energia (o C segurado) segura o excesso: em trinta segundos quase nada escorre",
				   segurando.Ki / segurando.MaxKi > 1.38, $"{segurando.Ki / segurando.MaxKi:P1}");
		CargaDeKi.ExcessoNaoEscorreDeTeste = true;
		try
		{
			Fighter parado = Carregado(1.4);
			for (int i = 0; i < 30 * 120; i++) CargaDeKi.PrecoDoExcesso(parado, 1.0 / 30);
			AfirmarV21("(defeito injetado: o excesso nao escorre) dois minutos depois o Ki continua acima de 135% -- *\"tu consegue ficar MUITO tempo com ki acima de 100%\"*",
					   parado.Ki / parado.MaxKi > 1.35, $"{parado.Ki / parado.MaxKi:P1}");
		}
		finally { CargaDeKi.ExcessoNaoEscorreDeTeste = false; }

		// ...e pelo servidor: o tique da carga de quem NAO segura o C
		LimparTudoDaBancada();
		{
			ServerPlayer cheio = ForjarV2("Carregado", chao, 10_000);
			Fighter f = cheio.Ficha;
			f.MeditateGivesKiRegen = 1;
			f.canPower = 1;
			f.Ki = f.MaxKi * 1.4;
			for (int i = 0; i < 30 * 10; i++) TickDaCarga(cheio, Protocol.TickSeconds);
			double solto = f.Ki / f.MaxKi;
			AfirmarV21("no servidor, quem solta o C com 140% de Ki ve o tanque descer: dez segundos depois esta abaixo de 127%", solto is > 1.0 and < 1.27, $"{solto:P1}");

			// segurando o C, parado, com folego: o excesso se sustenta (e sobe ate o teto) -- e o VIGOR paga
			f.Ki = f.MaxKi * CargaDeKi.TetoEmRazao(f);
			f.stamina = f.maxstamina;
			Carregar(cheio, true);
			for (int i = 0; i < 30 * 10; i++) TickDaCarga(cheio, Protocol.TickSeconds);
			double seguro = f.Ki / f.MaxKi, vigorGasto = 1 - f.stamina / f.maxstamina;
			AfirmarV21("**SEGURAR O C SUSTENTA O EXCESSO, E QUEM PAGA E O VIGOR**: dez segundos no teto, o Ki nao desce e o vigor cai uns 17%",
					   cheio.Carregando && seguro > CargaDeKi.TetoEmRazao(f) - 0.01 && vigorGasto is > 0.12 and < 0.22, $"ki {seguro:P1}; vigor -{vigorGasto:P1}");
			// O RAMO "JA NO MAXIMO", ISOLADO: o Ki CRAVADO no teto antes de cada tique. Ate 2026-10-16 quem o devolvia ao teto a cada
			// tique era o troco da carga (que saiu -- ver a V6c); sem ele o vazamento do excesso poe o Ki um fio abaixo e a carga normal
			// cobra o mesmo vigor, entao o contra-exemplo precisa cravar o Ki pra cair so neste ramo.
			double VigorCravadoNoTeto(bool deGraca)
			{
				CargaDeKi.SegurarNoTetoDeGracaDeTeste = deGraca;
				try
				{
					f.stamina = f.maxstamina;
					for (int i = 0; i < 30 * 10; i++)
					{
						f.Ki = f.MaxKi * CargaDeKi.TetoEmRazao(f);
						TickDaCarga(cheio, Protocol.TickSeconds);
					}
					return 1 - f.stamina / f.maxstamina;
				}
				finally { CargaDeKi.SegurarNoTetoDeGracaDeTeste = false; }
			}
			double cobradoNoTeto = VigorCravadoNoTeto(false), barato = VigorCravadoNoTeto(true);
			AfirmarV21("(defeito injetado: segurar no teto de graca) com o Ki cravado no teto, os mesmos dez segundos gastam menos da metade do vigor -- o ramo \"ja no maximo\" e quem cobra",
						   cobradoNoTeto is > 0.12 and < 0.22 && barato < cobradoNoTeto * 0.5, $"vigor -{barato:P2} contra -{cobradoNoTeto:P2}");
			Carregar(cheio, false);
		}

		// ---------------------------------------------------------------- V6) O TETO DO JOGO: 200%
		AfirmarV21("**O TETO DA CARGA PELA PERICIA**: 140% de fabrica (ate a pericia 10), 200% com circulacao e controle em 100, e uma reta no meio",
				   Math.Abs(CargaDeKi.TetoPelaPericia(1, 1) - 1.4) < 1e-9 && Math.Abs(CargaDeKi.TetoPelaPericia(10, 10) - 1.4) < 1e-9
				   && Math.Abs(CargaDeKi.TetoPelaPericia(55, 55) - 1.7) < 1e-9 && Math.Abs(CargaDeKi.TetoPelaPericia(100, 100) - 2.0) < 1e-9
				   && Math.Abs(CargaDeKi.TetoPelaPericia(500, 500) - 2.0) < 1e-9 && CargaDeKi.TetoPelaPericia(100, 10) < 1.6,
				   $"1: {CargaDeKi.TetoPelaPericia(1, 1):0.###}; 55: {CargaDeKi.TetoPelaPericia(55, 55):0.###}; 100: {CargaDeKi.TetoPelaPericia(100, 100):0.###}; 100 e 10: {CargaDeKi.TetoPelaPericia(100, 10):0.###}");

		static double TetoDaFicha(double pericia)
		{
			var f = new Fighter { Race = "Human", BP = 10_000 };
			f.kicirculationskill = f.kicontrolskill = pericia;
			f.Tick(agoraMs: 0);
			return f.powerupcap;
		}
		AfirmarV21("**NA FICHA** (o `Statify`): pericia 30 da 153%, pericia 100 da 200% cravados -- ninguem passa do teto do jogo carregando",
				   Math.Abs(TetoDaFicha(30) - (1.4 + 0.6 * 20 / 90)) < 1e-6 && Math.Abs(TetoDaFicha(100) - 2.0) < 1e-9 && Math.Abs(TetoDaFicha(400) - 2.0) < 1e-9,
				   $"30: {TetoDaFicha(30):0.###}; 100: {TetoDaFicha(100):0.###}; 400: {TetoDaFicha(400):0.###}");
		CargaDeKi.TetoPelaFormulaDoDmDeTeste = true;
		try
		{
			AfirmarV21("(defeito injetado: o teto pela conta do DM) a pericia 100 leva a carga a mais de 300% -- *\"rapidinho vc consegue passar de 200% de ki\"*",
					   TetoDaFicha(100) > 3.0, $"100: {TetoDaFicha(100):0.###}");
		}
		finally { CargaDeKi.TetoPelaFormulaDoDmDeTeste = false; }

		LimparTudoDaBancada();
		{
			ServerPlayer alem = ForjarV2("AlemDoTeto", chao, 10_000);
			Fighter f = alem.Ficha;
			// (o teto DESTE corpo e o do jogo, como o de quem tem as duas pericias no maximo -- o de cada um e o V6b, logo
			// abaixo. Escrito direto: o `powerupcap` e do `Statify`, que nao roda no tique da carga.)
			f.powerupcap = CargaDeKi.TetoDoJogo;
			f.Ki = f.MaxKi * 3.5;
			TickDaCarga(alem, Protocol.TickSeconds);
			AfirmarV21("**O CORTE DOS 200%**: o Ki que chega acima do teto do jogo por qualquer caminho e aparado no tique seguinte -- mesmo em quem tem o teto de carga no maximo",
					   f.Ki <= f.MaxKi * CargaDeKi.TetoDoJogo + 1e-6 && f.Ki > f.MaxKi * 1.9, $"{f.Ki / f.MaxKi:P1}");
		}

		// ---------------------------------------------------------------- V6b) O TETO DE CARGA E DURO
		// (dono, 2026-10-13, na vespera do lancamento) *"Simplesmente so carrego meu ki com C e ele passa do teto do meu
		// personagem q é 140%"* -- a tela dele: 158,7% de Ki com "Teto de carga 140% do tanque". Quem furava era o TROCO DA
		// CARGA (`extracharge`), no gesto de SOLTAR O C E VOLTAR A SEGURAR: solto, o troco comeca a voltar como Ki pelo
		// `RegenDeKi.Passo`; segurando de novo com o Ki no teto, a carga cai no ramo "ja no maximo", que nao escorre nem
		// reenche o troco -- e o resto dele entra acima do teto. (Segurando sem parar ele fica cheio e nao paga nada.)
		// (2026-10-16) O TROCO SAIU INTEIRO (ver a V6c): o gesto continua aqui como regressao, e o contra-exemplo do teto passou a ser
		// o Ki que chega POR FORA -- um presente, uma tecnica, um tiro engolido --, o caminho que o teto duro continua barrando.
		LimparTudoDaBancada();
		{
			ServerPlayer firme = ForjarV2("Firme", chao, 10_000);
			Fighter f = firme.Ficha;
			f.MeditateGivesKiRegen = 1;
			f.canPower = 1;
			// A REGENERACAO DE UM PERSONAGEM TREINADO: 0,09% do tanque por chamada. E a que poe o furo nos 158% da tela do
			// dono (o troco inteiro vale 208 chamadas); a de um corpo de bancada e um quarto disso, e com ela o Ki solto
			// escorre mais depressa do que o troco paga. Escrita direto: o `KIregen` e do `Statify`, que nao roda no tique
			// da carga.
			f.KIregen = f.MaxKi * 0.0009;
			double teto = CargaDeKi.TetoEmRazao(f);
			double PicoSoltandoEVoltando()
			{
				f.Ki = f.MaxKi * 0.2;
				f.powerMod = 1;
				double pico = 0;
				void Tiques(int segundos)
				{
					for (int i = 0; i < 30 * segundos; i++)
					{
						f.stamina = f.maxstamina;   // (o folego nao e o assunto: sem ele a carga pararia antes do teto)
						TickDaCarga(firme, Protocol.TickSeconds);
						pico = Math.Max(pico, f.Ki / f.MaxKi);
					}
				}
				Carregar(firme, true);
				Tiques(45);                  // sobe ate o teto, e o troco enche
				Carregar(firme, false);
				Tiques(5);                   // solta: o troco comeca a voltar
				Carregar(firme, true);
				Tiques(25);                  // segura de novo: no ramo "ja no maximo" nada escorre
				Carregar(firme, false);
				return pico;
			}
			double pico = PicoSoltandoEVoltando();
			AfirmarV21("**O TETO DE CARGA E DURO**: carregar ate o teto, soltar o C por cinco segundos e voltar a segurar -- o Ki chega ao teto do personagem (140%) e NAO PASSA dele: o troco da carga nao fura",
					   Math.Abs(teto - CargaDeKi.TetoDeFabrica) < 1e-9 && pico > teto - 0.01 && pico <= teto + 1e-9, $"pico {pico:P2}; teto {teto:P0}");
			CargaDeKi.TetoDeCargaNaoEDuroDeTeste = true;
			try
			{
				f.Ki = f.MaxKi * 1.9;                          // um presente: o Ki que chega por fora
				TickDaCarga(firme, Protocol.TickSeconds);
				double furado = f.Ki / f.MaxKi;
				AfirmarV21("(defeito injetado: o teto de carga so vale pra tecla C) o Ki que chega por fora fica acima do teto do personagem (140%) -- *\"so carrego meu ki com C e ele passa do teto do meu personagem\"*, e a tela dele mostrava 158,7%",
						   furado > teto + 0.10 && furado <= CargaDeKi.TetoDoJogo, $"{furado:P2}; teto {teto:P0}");
			}
			finally { CargaDeKi.TetoDeCargaNaoEDuroDeTeste = false; }
			Carregar(firme, false);

			// ...e venha o Ki de onde vier: um presente, uma tecnica, um tiro engolido
			f.Ki = f.MaxKi * 1.9;
			TickDaCarga(firme, Protocol.TickSeconds);
			double aparado = f.Ki / f.MaxKi;
			f.powerupcap = 1.7;
			f.Ki = f.MaxKi * 1.9;
			TickDaCarga(firme, Protocol.TickSeconds);
			double noTetoDele = f.Ki / f.MaxKi;
			f.Ki = f.MaxKi * 1.6;
			TickDaCarga(firme, Protocol.TickSeconds);
			double dentro = f.Ki / f.MaxKi;
			AfirmarV21("...e VENHA O KI DE ONDE VIER: o que chega acima do teto do personagem e aparado no mesmo tique (190% vira 140%), o teto e o DELE (com o teto em 170%, os mesmos 190% viram 170%) e quem esta abaixo dele so perde o que escorre (160% continua em 160%)",
					   Math.Abs(aparado - 1.4) < 1e-9 && Math.Abs(noTetoDele - 1.7) < 1e-9 && dentro is > 1.595 and < 1.6, $"{aparado:P1}; {noTetoDele:P1}; {dentro:P2}");
		}

		// ---------------------------------------------------------------- V6c) SOLTAR O C PARA A SUBIDA (dono, 2026-10-16)
		// *"com o C mesmo se passar de 100% o ki continua subindo por uns segundos mesmo eu tendo largado o C, e ai ele vai caindo
		// normalmente dps"* -- e, abaixo dos 100%, *"o ki continua subindo sozinho"*. Era o troco da carga (`extracharge`), que saiu. A
		// regua: dois corpos IGUAIS, um que segurou o C e um que nunca segurou, soltos no mesmo Ki -- dali em diante os dois tanques
		// andam juntos (a regeneracao normal, que para nos 100%). E acima dos 100%, quem soltou nunca passa do Ki de quando soltou.
		LimparTudoDaBancada();
		{
			ServerPlayer carregou = ForjarV2("Carregou", chao, 10_000);
			ServerPlayer nunca = ForjarV2("NuncaCarregou", chao + new Vec2(3 * ZoneCollision.TileSize, 0), 10_000);
			foreach (ServerPlayer p in new[] { carregou, nunca })
			{
				p.Ficha.MeditateGivesKiRegen = 1;
				p.Ficha.canPower = 1;
				p.Ficha.KIregen = p.Ficha.MaxKi * 0.0009;   // a regeneracao de um personagem treinado (a da V6b)
			}
			Fighter fc = carregou.Ficha, fn = nunca.Ficha;
			void TiquesDosDois(double segundos, Action? aCadaTique = null)
			{
				for (int i = 0; i < (int)Math.Round(30 * segundos); i++)
				{
					fc.stamina = fc.maxstamina;   // (o folego nao e o assunto)
					fn.stamina = fn.maxstamina;
					TickDaCarga(carregou, Protocol.TickSeconds);
					TickDaCarga(nunca, Protocol.TickSeconds);
					aCadaTique?.Invoke();
				}
			}
			double Razao(Fighter f) => f.Ki / Math.Max(f.MaxKi, 1e-9);

			// ABAIXO DOS 100%: segura o C de 20% ate 60% e solta; o gemeo, parado, comeca no mesmo Ki
			fc.Ki = fc.MaxKi * 0.2;
			fc.powerMod = 1;
			Carregar(carregou, true);
			for (int g = 0; Razao(fc) < 0.6 && g < 30 * 30; g++) TiquesDosDois(1.0 / 30);
			Carregar(carregou, false);
			fn.Ki = fc.Ki;
			double soltouAbaixo = Razao(fc), maiorDiferenca = 0;
			TiquesDosDois(8, () => maiorDiferenca = Math.Max(maiorDiferenca, Razao(fc) - Razao(fn)));
			AfirmarV21("**SOLTAR O C PARA A SUBIDA**: abaixo dos 100%, quem segurou o C e soltou anda IGUAL a quem nunca segurou -- so a regeneracao normal, nos 8 s seguintes",
						   soltouAbaixo is > 0.55 and < 0.65 && maiorDiferenca < 0.0005,
						   $"soltou em {soltouAbaixo:P1}; maior diferenca {maiorDiferenca:P3}; agora {Razao(fc):P1} x {Razao(fn):P1}");

			// ACIMA DOS 100%: carrega ate 130% e solta -- dali o Ki so desce (o vazamento do excesso)
			fc.Ki = fc.MaxKi;
			fc.powerMod = 1;
			Carregar(carregou, true);
			for (int g = 0; Razao(fc) < 1.3 && g < 90 * 30; g++) TiquesDosDois(1.0 / 30);
			Carregar(carregou, false);
			double soltouAcima = Razao(fc), pico = soltouAcima;
			TiquesDosDois(8, () => pico = Math.Max(pico, Razao(fc)));
			AfirmarV21("**...E ACIMA DOS 100% ELE SO DESCE**: carregou ate 130% e soltou -- nos 8 s seguintes o Ki nunca passa do que tinha ao soltar",
						   soltouAcima >= 1.29 && pico <= soltouAcima + 1e-9 && Razao(fc) < soltouAcima,
						   $"soltou em {soltouAcima:P2}, pico {pico:P2}, agora {Razao(fc):P2}");

			// O CONTRA-EXEMPLO, montado aqui: a tecla "solta" e a carga continua (o servidor ignorando o soltar) -- o mesmo criterio ve a subida
			fc.Ki = fc.MaxKi * 1.1;
			fc.powerMod = 1;
			Carregar(carregou, true);
			double soltouDeMentira = Razao(fc), picoDeMentira = soltouDeMentira;
			TiquesDosDois(3, () => picoDeMentira = Math.Max(picoDeMentira, Razao(fc)));
			Carregar(carregou, false);
			AfirmarV21("(contra-exemplo: a carga que nao para ao soltar) a mesma regua ve o Ki subindo depois do \"soltar\" -- ela sabe reprovar",
						   picoDeMentira > soltouDeMentira + 0.01, $"{soltouDeMentira:P2} -> {picoDeMentira:P2}");
		}

		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// S) O SCOUTER E O SENSE COM OS NPCs
	// =====================================================================
	private void OsNpcsNosSentidosV21()
	{
		GD.Print("[v021] -- S) O SCOUTER LE O BP DOS NPCs, E O SENSE E O SCOUTER DIZEM O NOME DELES (item 14)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		float tile = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(16);
		Jandirus.Core.Npc.MoldeDeNpc? comum = _moldes?.Todos.FirstOrDefault(m => !m.EhChefe), chefe = _moldes?.Todos.FirstOrDefault(m => m.EhChefe);
		AfirmarV21("(montagem) o catalogo de NPCs tem um molde comum e um de chefe", comum != null && chefe != null);
		if (comum == null || chefe == null) return;

		ServerPlayer eu = ForjarV2("Leitor", c, 10_000);
		ServerPlayer Npc(string nome, float tiles, double bp, Jandirus.Core.Npc.MoldeDeNpc molde, int numero)
		{
			ServerPlayer n = ForjarV2(nome, c + new Vec2(tiles * tile, 0), bp);
			n.Papel = new Jandirus.Core.Npc.PapelDeNpc(molde, (ulong)numero);
			n.Conta = "";   // NPC do mundo: tem papel e nao tem conta
			return n;
		}
		ServerPlayer guarda = Npc("Guarda da Cidade", 6, 4_000, comum, 31);
		ServerPlayer longe = Npc("Lavrador Distante", 60, 4_000, comum, 32);
		ServerPlayer rei = Npc("Chefe Distante", 70, 90_000, chefe, 33);
		ServerPlayer estranho = ForjarV2("JogadorEstranho", c + new Vec2(8 * tile, 0), 7_000);
		Protocol.PresencaState? Acha(List<Protocol.PresencaState> l, ServerPlayer quem, ushort dist) =>
			l.Where(p => p.Distancia == dist).Select(p => (Protocol.PresencaState?)p).FirstOrDefault();

		// ---- o SENSE: o nome do NPC
		eu.Poderes |= Protocol.Poder.Sense;
		(bool scanDoSense, List<Protocol.PresencaState> sense) = SentidosDe(eu);
		Protocol.PresencaState? guardaNoSense = Acha(sense, guarda, 6), estranhoNoSense = Acha(sense, estranho, 8);
		AfirmarV21("**O SENSE DIZ O NOME DO NPC**: o guarda a seis tiles vem com o nome dele (e sem assinatura) -- antes ia so \"???\"",
				   !scanDoSense && guardaNoSense is { Nome: "Guarda da Cidade", Assinatura: "" }, $"nome '{guardaNoSense?.Nome}'");
		AfirmarV21("...sem o BP dele (no Sense o numero nunca viaja): so o poder relativo",
				   guardaNoSense is { } g && double.IsNaN(g.Bp) && Math.Abs(g.PoderRelativo - 40) < 1.5f, $"bp {guardaNoSense?.Bp}; relativo {guardaNoSense?.PoderRelativo}%");
		AfirmarV21("...e o JOGADOR desconhecido continua sem nome, com a assinatura (a identidade dele e que se esconde)",
				   estranhoNoSense is { Nome: "" } && estranhoNoSense.Value.Assinatura.Length > 0, $"nome '{estranhoNoSense?.Nome}', assinatura '{estranhoNoSense?.Assinatura}'");

		// ---- o SCOUTER: o BP do NPC comum
		eu.Poderes |= Protocol.Poder.Scouter;
		(bool scan, List<Protocol.PresencaState> lido) = SentidosDe(eu);
		Protocol.PresencaState? guardaNoScan = Acha(lido, guarda, 6);
		AfirmarV21("**O SCOUTER LE O BP DO NPC COMUM**, com o nome dele: o guarda aparece no scan com o poder exato",
				   scan && guardaNoScan is { Nome: "Guarda da Cidade", Chefe: false } && guardaNoScan.Value.Bp == Math.Round(guarda.Ficha.expressedBP, MidpointRounding.AwayFromZero),
				   $"nome '{guardaNoScan?.Nome}', bp {guardaNoScan?.Bp} (dele: {guarda.Ficha.expressedBP:0})");
		AfirmarV21("...ate quarenta tiles: o lavrador a sessenta fica fora, e o CHEFE a setenta continua dentro",
				   Acha(lido, longe, 60) == null && Acha(lido, rei, 70) is { Chefe: true, Nome: "Chefe Distante" },
				   string.Join(", ", lido.Select(p => $"{p.Nome}@{p.Distancia}")));

		NpcSemNomeNemLeituraDeTeste = true;
		try
		{
			(_, List<Protocol.PresencaState> scanDeAntes) = SentidosDe(eu);
			eu.Poderes &= ~Protocol.Poder.Scouter;
			(_, List<Protocol.PresencaState> senseDeAntes) = SentidosDe(eu);
			AfirmarV21("(defeito injetado: o NPC sem nome nem leitura) o guarda some do scan e volta a ser \"???\" no Sense -- a v0.2.0",
					   Acha(scanDeAntes, guarda, 6) == null && Acha(senseDeAntes, guarda, 6) is { Nome: "" },
					   $"scan: {string.Join(", ", scanDeAntes.Select(p => $"{p.Nome}@{p.Distancia}"))}; sense: '{Acha(senseDeAntes, guarda, 6)?.Nome}'");
		}
		finally { NpcSemNomeNemLeituraDeTeste = false; }

		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// A) O AGARRAO
	// =====================================================================
	private void OAgarraoV21()
	{
		GD.Print("[v021] -- A) O AGARRAO: estrangular plantado e sem pose de soco, o carregado colado, quem se debate so vira (item 36)");
		float tile = ZoneCollision.TileSize;

		(ServerPlayer A, ServerPlayer D) Par(bool noColo)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			(ServerPlayer a, ServerPlayer d) = ParV2(500_000, 5_000, porTras: false);
			AlternarAgarrao(a);
			if (noColo) AlternarAgarrao(a);
			return (a, d);
		}

		// ---- A1) ESTRANGULAR
		var seg = Par(noColo: false);
		AfirmarV21("(montagem) o forte segura o fraco (modo de segurar)", seg.A.AgarrandoId == seg.D.Id && seg.A.ModoDoAgarrao == ModoDeAgarrao.Segurando,
				   $"agarrando {seg.A.AgarrandoId}, modo {seg.A.ModoDoAgarrao}");
		bool andavaAntes = PodeMexerOCorpo(seg.A);
		seg.A.AtaqueAte = 0;
		seg.A.AfogamentoLivreEm = 0;
		Atacar(seg.A, Protocol.Golpe.Leve);
		AfirmarV21("**ESTRANGULAR NAO E SOCO**: com alguem na mao o golpe liga o aperto e a pose de soco nao sobe",
				   seg.A.Estrangulando && seg.A.AtaqueAte == 0 && seg.A.Pose(NowMs(), false) != Protocol.Pose.Atacando,
				   $"estrangulando {seg.A.Estrangulando}; pose {seg.A.Pose(NowMs(), false)}");
		AfirmarV21("**QUEM ESTRANGULA FICA PLANTADO**: o funil do passo recusa o corpo dele (antes do aperto ele andava), e a ficha diz por que",
				   andavaAntes && !PodeMexerOCorpo(seg.A) && seg.A.Sheet().Estrangulando && seg.A.Sheet().Segurando,
				   $"andava {andavaAntes}; anda agora {PodeMexerOCorpo(seg.A)}; ficha {seg.A.Sheet().Estrangulando}");
		EstranguladorAndaDeTeste = true;
		try { AfirmarV21("(defeito injetado: o estrangulador anda) com o aperto ligado o corpo continua podendo andar -- a v0.2.0", PodeMexerOCorpo(seg.A)); }
		finally { EstranguladorAndaDeTeste = false; }
		seg.A.AfogamentoLivreEm = 0;
		Atacar(seg.A, Protocol.Golpe.Leve);
		AfirmarV21("...e afrouxando o aperto ele volta a andar (e a ficha apaga o aviso)",
				   !seg.A.Estrangulando && PodeMexerOCorpo(seg.A) && !seg.A.Sheet().Estrangulando);
		DesfazerV2(seg.A, seg.D);

		// ---- A2) NO COLO: junto, e sem marchar
		var colo = Par(noColo: true);
		AfirmarV21("(montagem) o segundo aperto do agarrao levanta o preso no colo", colo.A.ModoDoAgarrao == ModoDeAgarrao.Carregando, colo.A.ModoDoAgarrao.ToString());
		colo.A.Pos += new Vec2(3 * tile, 0);
		colo.A.Moving = true;
		for (int i = 0; i < 3; i++) TickDoAgarrao(Protocol.TickSeconds);
		EntityState doPreso = EstadoDe(colo.D, NowMs()), doCarregador = EstadoDe(colo.A, NowMs());
		AfirmarV21("**O CARREGADO VAI JUNTO**: o corpo dele esta no mesmo ponto de quem o leva, e o snapshot diz quem o leva (`LevadoPor`)",
				   Vec2.Distance(colo.D.Pos, colo.A.Pos) < 0.01f && doPreso.LevadoPor == colo.A.Id && doCarregador.LevadoPor == 0,
				   $"distancia {Vec2.Distance(colo.D.Pos, colo.A.Pos):0.##} px; levado por {doPreso.LevadoPor} (o carregador e {colo.A.Id})");
		AfirmarV21("**...E NAO MARCHA**: quem carrega esta andando, e o carregado vai com o `Moving` falso (sem animacao de andar)",
				   colo.A.Moving && !colo.D.Moving && !doPreso.Moving, $"carregador {colo.A.Moving}, carregado {colo.D.Moving}");
		var fio = new LiteNetLib.Utils.NetDataWriter();
		doPreso.Write(fio);
		int comColo = fio.Length;
		var lido = EntityState.Read(new LiteNetLib.Utils.NetDataReader(fio.CopyData()));
		fio.Reset();
		doCarregador.Write(fio);
		AfirmarV21("o `LevadoPor` atravessa o fio inteiro, e so quem esta no colo paga os quatro bytes dele",
				   lido.LevadoPor == colo.A.Id && lido.Id == colo.D.Id && comColo == fio.Length + 4 + (doPreso.Voando ? 1 : 0) - (doCarregador.Voando ? 1 : 0),
				   $"lido {lido.LevadoPor}; {comColo} bytes no colo contra {fio.Length} de quem carrega");
		AgarradoMarchaNoColoDeTeste = true;
		try
		{
			TickDoAgarrao(Protocol.TickSeconds);
			AfirmarV21("(defeito injetado: o carregado marcha) ele volta a andar no colo com a animacao de quem o carrega -- a v0.2.0", colo.D.Moving);
		}
		finally { AgarradoMarchaNoColoDeTeste = false; }

		// ---- A3) QUEM SE DEBATE SO VIRA
		TickDoAgarrao(Protocol.TickSeconds);
		Vec2 antesDeDebater = colo.D.Pos;
		Facing deCostas = colo.D.Facing;
		Facing pedido = deCostas == Facing.North ? Facing.South : Facing.North;
		uint seq = 10;
		void Debater(ServerPlayer preso, Facing olhar) =>
			AplicarInput(preso, seq++, 0, preso.Pos + new Vec2(0, olhar == Facing.North ? -8 : 8), (byte)((byte)olhar | Protocol.InputAndando));

		Debater(colo.D, pedido);
		for (int i = 0; i < 2; i++) TickDoAgarrao(Protocol.TickSeconds);
		AfirmarV21("**QUEM SE DEBATE SO VIRA**: a tecla de andar do preso vira o corpo pra onde ele apertou -- sem sair do lugar e sem animacao de andar",
				   colo.D.Facing == pedido && Vec2.Distance(colo.D.Pos, colo.A.Pos) < 0.01f && !colo.D.Moving && Vec2.Distance(colo.D.Pos, antesDeDebater) < 0.01f,
				   $"olhar {colo.D.Facing} (pedido {pedido}, de costas era {deCostas}); andando {colo.D.Moving}");
		colo.D.ViradaDoDebateAte = 0;
		TickDoAgarrao(Protocol.TickSeconds);
		AfirmarV21("...e parando de se debater o corpo volta a ficar de costas pra quem o carrega", colo.D.Facing == DeCostasPara(colo.A.Facing), colo.D.Facing.ToString());
		AgarradoNaoViraDeTeste = true;
		try
		{
			Debater(colo.D, pedido);
			TickDoAgarrao(Protocol.TickSeconds);
			AfirmarV21("(defeito injetado: o agarrado nao vira) a tecla de andar nao mexe no olhar dele -- a v0.2.0", colo.D.Facing != pedido, colo.D.Facing.ToString());
		}
		finally { AgarradoNaoViraDeTeste = false; }
		DesfazerV2(colo.A, colo.D);

		// (no modo de SEGURAR, sem colo, a mesma coisa: vira e nao anda)
		var preso = Par(noColo: false);
		Vec2 ondeEstava = preso.D.Pos;
		Facing outro = preso.D.Facing == Facing.North ? Facing.South : Facing.North;
		Debater(preso.D, outro);
		AfirmarV21("segurado (sem colo), quem se debate tambem so vira: o olhar muda, o corpo nao sai do lugar e nao marcha",
				   preso.D.Facing == outro && Vec2.Distance(preso.D.Pos, ondeEstava) < 0.01f && !preso.D.Moving && preso.D.DebatendoSe,
				   $"olhar {preso.D.Facing}; andou {Vec2.Distance(preso.D.Pos, ondeEstava):0.##} px; andando {preso.D.Moving}");
		DesfazerV2(preso.A, preso.D);

		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// M) O MAJIN NAO SANGRA
	// =====================================================================
	private void OMajinNaoSangraV21()
	{
		GD.Print("[v021] -- M) O MAJIN NAO SANGRA: sem ferida no corpo e sem peca no chao; so o membro some (item 16)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		float tile = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer majin = ForjarV2("Buu", c, 50_000, "Majin");
		ServerPlayer humano = ForjarV2("Kuririn", c + new Vec2(3 * tile, 0), 50_000);

		// O MESMO ESTRAGO NOS DOIS: o tronco e as pernas quase no fim, e o braco esquerdo arrancado.
		List<BodyPart> Estragar(ServerPlayer p)
		{
			Body corpo = p.Combate.Corpo;
			foreach (BodyPart parte in corpo.Partes)
				if (!parte.Nome.StartsWith("Braco", StringComparison.Ordinal) && !parte.Nome.StartsWith("Mao", StringComparison.Ordinal))
					parte.Vida = parte.VidaMax * 0.05;
			return corpo.Decepar(corpo.Partes.First(x => x.Nome == "Braco esquerdo"));
		}
		static bool SemFerida(MascaraDeFeridas m)
		{
			for (int z = 0; z < MascaraDeFeridas.Zonas; z++) if (m.Bruto(z) != 0) return false;
			return true;
		}

		List<BodyPart> caiuDoMajin = Estragar(majin), caiuDoHumano = Estragar(humano);
		MascaraDeFeridas cruaDoMajin = Feridas.De(majin.Combate.Corpo), cruaDoHumano = Feridas.De(humano.Combate.Corpo);
		AfirmarV21("(montagem) os dois corpos estao feridos de verdade: zonas marcadas e um braco arrancado",
				   !SemFerida(cruaDoMajin) && !SemFerida(cruaDoHumano) && cruaDoMajin.AmputadosBruto != 0 && caiuDoMajin.Count > 0 && caiuDoHumano.Count > 0,
				   $"amputados {cruaDoMajin.AmputadosBruto}; caiu {caiuDoMajin.Count}");

		MascaraDeFeridas naTelaDoMajin = Feridas.NaTela(majin.Combate.Corpo, majin.Race);
		AfirmarV21("**O MAJIN NAO MOSTRA FERIDA**: a mascara que a tela recebe dele nao tem roxo nem sangue em zona nenhuma -- e o braco arrancado continua nela",
				   SemFerida(naTelaDoMajin) && naTelaDoMajin.AmputadosBruto == cruaDoMajin.AmputadosBruto,
				   $"amputados {naTelaDoMajin.AmputadosBruto} (crua {cruaDoMajin.AmputadosBruto})");
		AfirmarV21("...e so ele: o humano com o mesmo estrago recebe a mascara inteira",
				   Feridas.NaTela(humano.Combate.Corpo, humano.Race) == cruaDoHumano && !Feridas.NaoSangra("Human") && !Feridas.NaoSangra("Namekian") && Feridas.NaoSangra("Majin"));

		TickDasFeridas();
		AfirmarV21("o tique das feridas (o que manda a mascara a zona) guarda a do Majin sem ferida e a do humano inteira",
				   majin.EnvFeridas == Feridas.SoAmputacoes(cruaDoMajin) && humano.EnvFeridas == cruaDoHumano);

		List<PecaNoChao> pecas = PecasDaZona(majin.Zone.Hash);
		int antes = pecas.Count;
		SoltarPecas(majin, caiuDoMajin);
		int depoisDoMajin = pecas.Count;
		SoltarPecas(humano, caiuDoHumano);
		AfirmarV21("**O MEMBRO DO MAJIN NAO DEIXA PECA NO CHAO** (a folha das pecas e a ensanguentada); o do humano deixa",
				   depoisDoMajin == antes && pecas.Count > depoisDoMajin, $"antes {antes}, depois do Majin {depoisDoMajin}, depois do humano {pecas.Count}");

		Feridas.MajinSangraDeTeste = true;
		try
		{
			int antesDoDefeito = pecas.Count;
			SoltarPecas(majin, caiuDoMajin);
			AfirmarV21("(defeito injetado: o Majin sangra) a mascara dele volta a levar as feridas e o membro volta a deixar peca -- a v0.2.0",
					   Feridas.NaTela(majin.Combate.Corpo, majin.Race) == cruaDoMajin && pecas.Count > antesDoDefeito);
		}
		finally { Feridas.MajinSangraDeTeste = false; }

		pecas.Clear();
		DesfazerV2(majin, humano);
	}

	// =====================================================================
	// C) O CABELO SEM A ARTE DA FORMA
	// =====================================================================
	private void OCabeloSemArteV21()
	{
		GD.Print("[v021] -- C) O CABELO SEM A ARTE DA FORMA: o penteado sem a folha de Super Saiyajin ganha o dourado (item 19)");
		static string? Dourado(string id) => Jandirus.Core.Forms.Catalogo.CorDoCabeloSemArte(Jandirus.Core.Forms.Catalogo.Def(id));

		AfirmarV21("**O SUPER SAIYAJIN E O 2 PINTAM DE DOURADO o penteado que nao tem a arte deles** (a mesma cor que o DM soma no rabo)",
				   Dourado("ssj1") == "dada26" && Dourado("ssj2") == "dada26", $"ssj1 {Dourado("ssj1")}, ssj2 {Dourado("ssj2")}");

		var errados = new List<string>();
		int quantos = 0;
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			string? cor = Jandirus.Core.Forms.Catalogo.CorDoCabeloSemArte(d);
			if (cor == null) continue;
			quantos++;
			if (d.SufixoDoCabelo.Length == 0) errados.Add(d.Id + " (nao troca de penteado)");
			if (Jandirus.Core.Forms.Catalogo.CorDoCabelo(d) != null) errados.Add(d.Id + " (ja tem tinta propria)");
			if (d.Linha is Jandirus.Core.Forms.LinhaDeForma.UltraInstinct or Jandirus.Core.Forms.LinhaDeForma.Namekuseijin
				or Jandirus.Core.Forms.LinhaDeForma.Mistico or Jandirus.Core.Forms.LinhaDeForma.FrostDemon)
				errados.Add(d.Id + " (nao e escada de Super Saiyajin)");
		}
		AfirmarV21($"...e so quem pede a folha de Super Saiyajin e nao tinge por conta propria ({quantos} formas): o God, o Blue e o Ultra Instinct ficam de fora",
				   quantos >= 2 && errados.Count == 0 && Dourado("ssg") == null && Dourado("blue") == null
				   && Jandirus.Core.Forms.Catalogo.CorDoCabeloSemArte(null) == null,
				   string.Join("; ", errados));

		Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = true;
		try { AfirmarV21("(defeito injetado: o cabelo sem arte nao pinta) o Super Saiyajin deixa o penteado sem arte na cor de sempre -- a v0.2.0", Dourado("ssj1") == null && Dourado("ssj2") == null); }
		finally { Jandirus.Core.Forms.Catalogo.CabeloSemArteNaoPintaDeTeste = false; }
	}

	// =====================================================================
	// O) O ORANGE
	// =====================================================================
	private void OOrangeV21()
	{
		GD.Print("[v021] -- O) O ORANGE: o buff do Beast, concedido pelo dragao ao Namekuseijin; a pele e o porte (item 20)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		float tile = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(6);
		string orange = Jandirus.Core.Forms.Catalogo.IdOrange;

		// ---- O1) O CATALOGO
		Jandirus.Core.Forms.FormaDef? o = Jandirus.Core.Forms.Catalogo.Def(orange), b = Jandirus.Core.Forms.Catalogo.Def("beast");
		AfirmarV21("(montagem) o catalogo tem o Orange, na linha do Namekuseijin e so por concessao",
				   o is { Linha: Jandirus.Core.Forms.LinhaDeForma.Namekuseijin, SoPorConcessao: true } && b != null);
		if (o == null || b == null) return;

		ServerPlayer piccolo = ForjarV2("Piccolo", c, 5_000_000, "Namekian");
		ServerPlayer gohan = ForjarV2("Gohan", c + new Vec2(3 * tile, 0), 5_000_000);
		var semMaestria = new Jandirus.Core.Forms.Maestrias();
		AfirmarV21("**O MESMO BUFF DO BEAST**: o multiplicador (absoluto), o dreno de Ki e o tanque de Ki das duas entradas sao iguais",
				   o.Mult.SequenceEqual(b.Mult) && o.Absoluta == b.Absoluta && o.Dreno.SequenceEqual(b.Dreno)
				   && Math.Abs(Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(orange, semMaestria) - Jandirus.Core.Forms.Catalogo.DrenoPorSegundo("beast", semMaestria)) < 1e-9
				   && Jandirus.Core.Forms.Catalogo.TetoDeKi(o, piccolo.Ficha) == Jandirus.Core.Forms.Catalogo.TetoDeKi(b, piccolo.Ficha),
				   $"mult {o.Mult[0]} contra {b.Mult[0]}; dreno {o.Dreno[0]} contra {b.Dreno[0]}; tanque {Jandirus.Core.Forms.Catalogo.TetoDeKi(o, piccolo.Ficha)}");
		AfirmarV21("...e o Super Namekuseijin continua dobrando o tanque de Ki (o corte e pelo degrau da linha, e nao pela linha inteira)",
				   Jandirus.Core.Forms.Catalogo.TetoDeKi(Jandirus.Core.Forms.Catalogo.Def("snamek"), piccolo.Ficha) == Jandirus.Core.Forms.Catalogo.SuperNamekKi);

		// ---- O2) SEM O DESEJO, NAO EXISTE
		Jandirus.Core.Forms.RecusaForma antesDoDesejo = piccolo.Forma.Avaliar(orange, piccolo.Ficha.BP, 1, false, Perfil(piccolo));
		AfirmarV21("**NAO SE TREINA**: sem o desejo, o Namekuseijin de cinco milhoes ouve que a forma nao foi concedida, e o C nao a oferece",
				   antesDoDesejo == Jandirus.Core.Forms.RecusaForma.NaoConcedida && piccolo.Forma.Proxima(piccolo.Ficha.BP, Perfil(piccolo))?.Id != orange,
				   antesDoDesejo.ToString());

		// ---- O3) O DESEJO E DE TODA RACA (v0.3.0 -- ate 2026-10-15 ele era so do Namekuseijin)
		bool Lista(double sangue) =>
			Jandirus.Core.Magic.Desejos.Menu(1e12, 1, false, false, sangue).Any(d => d.Id == "potencial");
		AfirmarV21("**O DESEJO E DE TODA RACA** (v0.3.0): o menu do dragao o lista sem olhar a raca -- o Mistico e o Orange sao do Namekuseijin, e quem decide e o desejo (`PorQueNaoLiberaOPotencial`)",
				   Lista(0) && Lista(100));

		// ---- O4) O DESEJO
		var set = new SetDeEsferas();
		// (v0.3.0) O NAMEKUSEIJIN SO PEDE COM O PODER DO KI DIVINO DELE -- e a regra do dono de 2026-10-15; os cinco milhoes de
		// antes ja nao bastam. Ver `GameServer.Desejos.PorQueNaoLiberaOPotencial` e a familia D da `--v030teste`.
		piccolo.Ficha.BP = Math.Max(piccolo.Ficha.BP, RequisitoDeGodKi(piccolo));
		double bpAntes = piccolo.Ficha.BP;
		// O `PeerLook` LIDO COMO O CLIENTE O LE (`GameClient`, no `case S2C.PeerLook`): o byte do pacote, quem, o nome, a
		// raca, o genero, a aparencia, o tipo de fusao, as marcas do corpo, a folha dos pesos e o segundo byte de marcas (o bebe,
		// 2026-10-18). `Sobra` sao os bytes que ficaram sem leitor.
		(byte Fusao, byte Marcas, string Pesos, int Sobra) LookDe(ServerPlayer p)
		{
			var fio = new LiteNetLib.Utils.NetDataReader(PacoteDeAparencia(p).CopyData());
			fio.GetByte();
			fio.GetInt(); fio.GetString(Protocol.MaxNomeNoFio); fio.GetString(24); fio.GetString(8);
			fio.GetAppearance();
			(byte fusao, byte marcas, string pesos) = (fio.GetByte(), fio.GetByte(), fio.GetString(120));
			fio.GetByte();
			return (fusao, marcas, pesos, fio.AvailableBytes);
		}
		byte MarcaDe(ServerPlayer p) => (byte)(LookDe(p).Marcas & 1);
		byte marcaAntes = MarcaDe(piccolo);
		bool concedeu = DesejoDePotencial(piccolo, set);
		// (v0.3.0) SEM "O BP SOBE": no requisito do ki divino o ganho do potencial bate no teto (`CapCheck` -- medido 250 mi ->
		// 250 mi). O ganho do desejo continua medido na familia D da `--v030teste`, num Saiyajin de cinco milhoes.
		AfirmarV21("**PEDIR AO DRAGAO LIBERA O POTENCIAL E DA O ORANGE**: o potencial desperta (uma vez por vida) e a forma passa a ser do corpo",
				   concedeu && piccolo.Ficha.unlockPotential >= 1 && piccolo.Forma.Despertou(orange),
				   $"concedeu {concedeu}; potencial {piccolo.Ficha.unlockPotential}; BP {bpAntes:0} -> {piccolo.Ficha.BP:0}");
		double bpDepois = piccolo.Ficha.BP;
		AfirmarV21("...e pedir de novo e recusado sem gastar nada (o ganho de BP nao se repete)",
				   !DesejoDePotencial(piccolo, set) && Math.Abs(piccolo.Ficha.BP - bpDepois) < 1e-6);

		// ---- O5) SEM O DEGRAU DE BAIXO
		Jandirus.Core.Forms.RecusaForma daBase = piccolo.Forma.Avaliar(orange, piccolo.Ficha.BP, 1, false, Perfil(piccolo));
		AfirmarV21("**O ORANGE NAO COBRA O SUPER NAMEKUSEIJIN**: concedido, ele sai da base de quem nunca comprou a forma de baixo -- e e o que o C escolhe",
				   daBase == Jandirus.Core.Forms.RecusaForma.Pode && !piccolo.Forma.Despertou("snamek") && piccolo.Forma.NaBase
				   && piccolo.Forma.Proxima(piccolo.Ficha.BP, Perfil(piccolo))?.Id == orange,
				   $"{daBase}; proxima {piccolo.Forma.Proxima(piccolo.Ficha.BP, Perfil(piccolo))?.Id}");
		Jandirus.Core.Forms.EstadoDeForma.ConcedidaCobraODegrauDeTeste = true;
		try
		{
			AfirmarV21("(defeito injetado: a concedida cobra o degrau) o Orange so sairia de dentro do Super Namekuseijin",
					   piccolo.Forma.Avaliar(orange, piccolo.Ficha.BP, 1, false, Perfil(piccolo)) == Jandirus.Core.Forms.RecusaForma.ForaDeOrdem);
		}
		finally { Jandirus.Core.Forms.EstadoDeForma.ConcedidaCobraODegrauDeTeste = false; }

		// ---- O6) A TECLA C
		Transformar(piccolo, subir: true);
		AfirmarV21("a tecla C poe o corpo no Orange, valendo as mesmas 56 vezes do Beast",
				   piccolo.Forma.Atual == orange && Math.Abs(piccolo.Forma.Multiplicador(Perfil(piccolo)) - b.Mult[0]) < 1e-9,
				   $"forma {piccolo.Forma.Atual}; mult {piccolo.Forma.Multiplicador(Perfil(piccolo)):0.##}");
		Reverter(piccolo, "bancada");
		AfirmarV21("...e sair dele devolve a base (o degrau de baixo da linha e uma transformacao, e nao um repouso)", piccolo.Forma.NaBase, piccolo.Forma.Atual);

		// ---- O7) A PELE VIAJA NA APARENCIA
		ServerPlayer nail = ForjarV2("Nail", c + new Vec2(6 * tile, 0), 5_000_000, "Namekian");
		byte marcaDoNovato = MarcaDe(nail);
		gohan.Ficha.unlockPotential = 1;
		nail.Forma.Liberar(Jandirus.Core.Forms.Catalogo.IdMistico);
		AfirmarV21("**O POTENCIAL DESPERTO VIAJA NA APARENCIA DO NAMEKUSEIJIN**: a marca do `PeerLook` dele e 1 depois do desejo (era 0 antes), e a de quem so TEM o Mistico tambem",
				   marcaAntes == 0 && MarcaDe(piccolo) == 1 && marcaDoNovato == 0 && MarcaDe(nail) == 1,
				   $"antes {marcaAntes}, depois {MarcaDe(piccolo)}; novato {marcaDoNovato}, com o Mistico {MarcaDe(nail)}");
		AfirmarV21("...e so nele: o humano de potencial desperto nao leva a marca", MarcaDe(gohan) == 0);
		(byte fusao, byte marcas, string pesos, int sobra) = LookDe(piccolo);
		AfirmarV21("o pacote inteiro se le na ordem em que foi escrito: a aparencia, o tipo de fusao, as marcas do corpo e a folha dos pesos, sem sobrar byte",
				   fusao == 0 && marcas == 1 && pesos.Length == 0 && sobra == 0, $"fusao {fusao}, marcas {marcas}, pesos '{pesos}', sobra {sobra}");
		Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = true;
		try { AfirmarV21("(defeito injetado: o potencial nao marca a pele) o Namekuseijin de potencial desperto viaja sem a marca -- a v0.2.0", MarcaDe(piccolo) == 0); }
		finally { Jandirus.Core.Forms.Catalogo.PotencialNaoMarcaAPeleDeTeste = false; }

		// ---- O8) A PELE E O PORTE DA FORMA
		Jandirus.Core.Forms.FormaDef? snamek = Jandirus.Core.Forms.Catalogo.Def("snamek");
		AfirmarV21("**NO ORANGE O CORPO FICA LARANJA E MAIOR**: a forma declara a pele (em matiz) e o porte, e so ela -- o Super Namekuseijin e o Beast nao mexem em nenhum dos dois",
				   Jandirus.Core.Forms.Catalogo.PeleDaForma(o) == o.Pele && o.Pele.Length == 6 && Jandirus.Core.Forms.Catalogo.PorteDaForma(o) > 1.05
				   && Jandirus.Core.Forms.Catalogo.PeleDaForma(snamek) == null && Jandirus.Core.Forms.Catalogo.PorteDaForma(snamek) == 1
				   && Jandirus.Core.Forms.Catalogo.PeleDaForma(b) == null && Jandirus.Core.Forms.Catalogo.PorteDaForma(b) == 1
				   && Jandirus.Core.Forms.Catalogo.PeleDaForma(null) == null && Jandirus.Core.Forms.Catalogo.PorteDaForma(null) == 1,
				   $"pele {o.Pele}, porte {Jandirus.Core.Forms.Catalogo.PorteDaForma(o):0.##}");
		Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = true;
		try
		{
			AfirmarV21("(defeito injetado: a forma nao pinta a pele nem muda o porte) o Orange volta a ser um Namekuseijin verde do tamanho de sempre",
					   Jandirus.Core.Forms.Catalogo.PeleDaForma(o) == null && Jandirus.Core.Forms.Catalogo.PorteDaForma(o) == 1);
		}
		finally { Jandirus.Core.Forms.Catalogo.FormaNaoPintaAPeleDeTeste = Jandirus.Core.Forms.Catalogo.FormaNaoMudaOPorteDeTeste = false; }

		DesfazerV2(piccolo, gohan, nail);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// I) OS ITENS
	// =====================================================================
	/// <summary>
	/// O que ha de errado com a arte deste item, ou vazio: a folha tem de existir -- e, com
	/// <paramref name="cobrarOEstado"/>, o estado pedido tem de ser uma animacao dela. O nome do estado passa pelo
	/// saneamento do conversor de `.dmi` (minusculas, o que nao e letra ou digito vira `_`), que e como o desenho o
	/// procura (`Miniaturas.NomeDaAnimacao`).
	///
	/// O ESTADO SO SE COBRA DO ITEM ESCRITO A MAO. O que vem do `construcoes.json` traz o `icon_state` do DM como ele
	/// e, e la ha estado que a folha nao tem (a "Camera Computer" pede "Computer" ao `PDA.dmi`): o BYOND mostra o
	/// padrao da folha e o port faz igual -- e fallback FIEL, e quem o separa do defeito e a bancada da aba Tech.
	/// </summary>
	private static string DefeitoDaArteV21(Jandirus.Core.Items.ItemDef def, bool cobrarOEstado)
	{
		if (def.Arte.Length == 0) return "sem arte";
		if (!ResourceLoader.Exists(def.Arte)) return $"a folha nao existe ({def.Arte})";
		if (!cobrarOEstado || def.Estado.Length == 0) return "";
		if (GD.Load<SpriteFrames>(def.Arte) is not { } folha) return $"a folha nao abre ({def.Arte})";

		var saneado = new System.Text.StringBuilder(def.Estado.Length);
		foreach (char letra in def.Estado.ToLowerInvariant()) saneado.Append(char.IsLetterOrDigit(letra) ? letra : '_');
		string anim = saneado.ToString().Trim('_');
		while (anim.Contains("__")) anim = anim.Replace("__", "_");
		return folha.HasAnimation(anim) && folha.GetFrameCount(anim) > 0 ? "" : $"a folha nao tem o estado '{def.Estado}' ({def.Arte})";
	}

	private void OsItensV21()
	{
		GD.Print("[v021] -- I) OS ITENS: a arte que existe, e o scouter e os pesos no corpo (itens 2, 3 e 24)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();

		// ---- I1) A ARTE DE TODO ITEM EXISTE
		List<string> Varrer(IEnumerable<Jandirus.Core.Items.ItemDef> itens, bool cobrarOEstado)
		{
			var ruins = new List<string>();
			foreach (Jandirus.Core.Items.ItemDef def in itens)
				if (DefeitoDaArteV21(def, cobrarOEstado) is { Length: > 0 } defeito) ruins.Add($"{def.Id}: {defeito}");
			return ruins;
		}
		List<string> aMao = Varrer(Jandirus.Core.Items.CatalogoDeItens.Todos, cobrarOEstado: true);
		AfirmarV21($"**TODO ITEM ESCRITO A MAO APONTA PRA UMA FOLHA E UM ESTADO QUE EXISTEM** ({Jandirus.Core.Items.CatalogoDeItens.Todos.Count()} itens)",
				   aMao.Count == 0, string.Join("; ", aMao));

		Jandirus.Core.Items.ItemDef? furadeira = Jandirus.Core.Items.CatalogoDeItens.Get(Jandirus.Core.Items.CatalogoDeItens.Furadeira);
		Jandirus.Core.Tech.Construcao? obraDaFuradeira = _obras?.Get(Jandirus.Core.Items.CatalogoDeItens.Furadeira);
		AfirmarV21("**A FURADEIRA NA MOCHILA E A MESMA DA ABA TECH**: o item herda a arte da construcao de mesmo id (e a bandagem e o kit tambem)",
				   furadeira != null && obraDaFuradeira != null && furadeira.Arte == obraDaFuradeira.Arte && furadeira.Estado == obraDaFuradeira.Estado
				   && Jandirus.Core.Items.CatalogoDeItens.Get(Jandirus.Core.Items.CatalogoDeItens.Bandagem)?.Arte == _obras?.Get(Jandirus.Core.Items.CatalogoDeItens.Bandagem)?.Arte
				   && Jandirus.Core.Items.CatalogoDeItens.Get(Jandirus.Core.Items.CatalogoDeItens.KitMedico)?.Arte == _obras?.Get(Jandirus.Core.Items.CatalogoDeItens.KitMedico)?.Arte,
				   $"item '{furadeira?.Arte}' [{furadeira?.Estado}] contra obra '{obraDaFuradeira?.Arte}' [{obraDaFuradeira?.Estado}]");

		// (as construcoes que a extracao trouxe SEM arte ficam de fora: sao divida do extrator, e a tela as mostra pelo
		// nome -- o que se cobra aqui e que arte DECLARADA exista)
		HashSet<string> semDesenho = [.. (_obras?.Todas ?? []).Where(c => c.Arte.Length == 0).Select(c => c.Id)];
		List<string> dasObras = _obras == null ? ["sem catalogo de construcoes"]
			: Varrer(_obras.Todas.Where(c => !semDesenho.Contains(c.Id))
							.Select(c => Jandirus.Core.Items.CatalogoDeItens.Get(c.Id)).OfType<Jandirus.Core.Items.ItemDef>(), cobrarOEstado: false);
		AfirmarV21($"...e a folha de todo item que vem de uma CONSTRUCAO existe ({_obras?.Todas.Count() ?? 0} construcoes, {semDesenho.Count} sem desenho declarado)",
				   dasObras.Count == 0, string.Join("; ", dasObras));

		Jandirus.Core.Items.CatalogoDeItens.ItemNaoHerdaAArteDaObraDeTeste = true;
		try
		{
			List<string> semHeranca = Varrer(Jandirus.Core.Items.CatalogoDeItens.Todos, cobrarOEstado: true);
			AfirmarV21("(defeito injetado: o item nao herda a arte da construcao) a furadeira, a bandagem e o kit ficam sem desenho -- e a varredura pega os tres",
					   semHeranca.Count == 3 && semHeranca.Exists(x => x.StartsWith(Jandirus.Core.Items.CatalogoDeItens.Furadeira, StringComparison.Ordinal)),
					   string.Join("; ", semHeranca));
		}
		finally { Jandirus.Core.Items.CatalogoDeItens.ItemNaoHerdaAArteDaObraDeTeste = false; }

		// ---- I2) O QUE SE USA APARECE NO CORPO
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer goku = ForjarV2("Goku", c, 50_000);
		goku.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Scouter);
		goku.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Pesos);
		(bool Scouter, string Pesos, int Sobra) Usando(ServerPlayer p)
		{
			var fio = new LiteNetLib.Utils.NetDataReader(PacoteDeAparencia(p).CopyData());
			fio.GetByte();
			fio.GetInt(); fio.GetString(Protocol.MaxNomeNoFio); fio.GetString(24); fio.GetString(8);
			fio.GetAppearance();
			fio.GetByte();
			byte marcas = fio.GetByte();
			string pesos = fio.GetString(120);
			fio.GetByte();   // (2026-10-18) o segundo byte de marcas -- o bebe
			return ((marcas & 2) != 0, pesos, fio.AvailableBytes);
		}
		Jandirus.Core.Items.ItemDef scouter = Jandirus.Core.Items.CatalogoDeItens.Get(Jandirus.Core.Items.CatalogoDeItens.Scouter)!;
		string folhaDeFabrica = _visual?.Peca(Jandirus.Core.Items.PesoDeTreino.ArtePadrao) ?? "";

		var nu = Usando(goku);
		AfirmarV21("(montagem) com o scouter e os pesos GUARDADOS na mochila o corpo nao mostra nenhum dos dois, e a folha de fabrica dos pesos existe",
				   !nu.Scouter && nu.Pesos.Length == 0 && nu.Sobra == 0 && folhaDeFabrica.Length > 0, $"scouter {nu.Scouter}, pesos '{nu.Pesos}', folha '{folhaDeFabrica}'");

		Equipar(goku, scouter);
		AjustarPeso(goku, Jandirus.Core.Items.CatalogoDeItens.Pesos, "50");
		var vestido = Usando(goku);
		AfirmarV21("**O SCOUTER LIGADO E OS PESOS VESTIDOS VIAJAM NA APARENCIA**: a marca do scouter acende e a folha dos pesos (a de fabrica, a camisa do DM) vai junto",
				   vestido.Scouter && vestido.Pesos == folhaDeFabrica && vestido.Sobra == 0 && goku.Ficha.Weighted > 0,
				   $"scouter {vestido.Scouter}, pesos '{vestido.Pesos}'");

		// (a segunda folha do guarda-roupa que nao e a de fabrica)
		string outraArte = (_visual?.Roupas ?? []).Select(Jandirus.Core.Appearance.VisualCatalog.NomeDoArquivo)
			.FirstOrDefault(n => n != Jandirus.Core.Items.PesoDeTreino.ArtePadrao && _visual!.Peca(n) != null) ?? "";
		bool aceitou = ComandoDeItem(goku, $"item_{Jandirus.Core.Items.PesoDeTreino.AcaoArte}", $"{Jandirus.Core.Items.CatalogoDeItens.Pesos}/{outraArte}");
		AfirmarV21("**O JOGADOR ESCOLHE A ARTE DOS PESOS**: o verbo da mochila troca a folha que o corpo mostra, e a escolha fica no PESO -- no id dele (vai pro save com a mochila)",
				   aceitou && outraArte.Length > 0 && Usando(goku).Pesos == _visual!.Peca(outraArte)
				   && Jandirus.Core.Items.PesoDeTreino.ArteDe(goku.Ficha.pesoVestido) == outraArte && goku.Mochila.Quantos(goku.Ficha.pesoVestido) == 1
				   && Array.IndexOf(Jandirus.Core.Items.CatalogoDeItens.Get(Jandirus.Core.Items.CatalogoDeItens.Pesos)!.AcoesDoItem, Jandirus.Core.Items.PesoDeTreino.AcaoArte) >= 0,
				   $"arte '{outraArte}', no corpo '{Usando(goku).Pesos}', o peso vestido '{goku.Ficha.pesoVestido}'");
		ComandoDeItem(goku, $"item_{Jandirus.Core.Items.PesoDeTreino.AcaoArte}", $"{goku.Ficha.pesoVestido}/arte_que_nao_existe");
		AfirmarV21("...e uma arte que nao existe e recusada: a escolha de antes continua",
				   Jandirus.Core.Items.PesoDeTreino.ArteDe(goku.Ficha.pesoVestido) == outraArte);

		Jandirus.Core.Items.EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = true;
		try
		{
			var semOverlay = Usando(goku);
			AfirmarV21("(defeito injetado: o que se usa nao aparece) o corpo de quem esta de scouter e de pesos viaja sem nenhum dos dois -- a v0.2.0",
					   !semOverlay.Scouter && semOverlay.Pesos.Length == 0);
		}
		finally { Jandirus.Core.Items.EquipamentoNoCorpo.NaoApareceNoCorpoDeTeste = false; }

		TirarPeso(goku);
		Equipar(goku, scouter);
		var tirou = Usando(goku);
		AfirmarV21("tirar os pesos e desligar o scouter tira os dois do corpo", !tirou.Scouter && tirou.Pesos.Length == 0 && goku.Ficha.Weighted <= 0);

		DesfazerV2(goku);
		LimparTudoDaBancada();
	}

	/// <summary>
	/// O que este gesto fez os corpos DIZEREM a quem esta a vista -- o `Falar` (os emotes "larga", "pega", "rouba"), que
	/// nao passa pelo canal dos avisos que o `Ouvir` escuta.
	/// </summary>
	private static List<string> FalasV21(Action gesto)
	{
		List<(int Quem, Protocol.Fala Canal, string Texto)>? antes = EscutaDeFalas;
		var falas = new List<(int Quem, Protocol.Fala Canal, string Texto)>();
		EscutaDeFalas = falas;
		try { gesto(); }
		finally { EscutaDeFalas = antes; }
		return [.. falas.Select(f => f.Texto)];
	}

	// =====================================================================
	// L) LARGAR E PEGAR
	// =====================================================================
	private void LargarEPegarV21()
	{
		GD.Print("[v021] -- L) LARGAR E PEGAR: o item, o zeni e o recurso no chao (item 5)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const int t = ZoneCollision.TileSize;
		string maca = Jandirus.Core.Items.CatalogoDeItens.Maca, scouterId = Jandirus.Core.Items.CatalogoDeItens.Scouter;
		ulong zona = ZonaDaBancadaDeProjetil.Hash;
		List<Jandirus.Core.Items.Largado> chao = LargadosDe(zona);
		chao.Clear();

		Vec2 c = TerraFirmeG12(8);
		ServerPlayer ana = ForjarV2("Ana", c, 50_000);
		ana.Facing = Facing.East;
		ana.Mochila.Guardar(maca, 3);
		ana.Mochila.Guardar(scouterId);
		Vec2 aFrente = new(MathF.Floor((c.X + t) / t) * t + t / 2f, MathF.Floor(c.Y / t) * t + t / 2f);

		// ---- L1) O ITEM
		List<string> falas = FalasV21(() => Verbo(ana, "item_largar", maca));
		Jandirus.Core.Items.Largado? monte = chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Item && l.Item == maca);
		AfirmarV21("**LARGAR POE O ITEM NO CHAO**: uma maca sai da mochila e aparece um passo a frente de quem largou (no centro da celula), e quem esta a vista fica sabendo",
				   ana.Mochila.Quantos(maca) == 2 && monte is { Quantidade: 1 } && Math.Abs(monte.X - aFrente.X) < 0.01f && Math.Abs(monte.Y - aFrente.Y) < 0.01f
				   && Disse(falas, "larga"),
				   monte == null ? "nada no chao" : $"({monte.X:0}, {monte.Y:0}) x{monte.Quantidade:0}; esperado ({aFrente.X:0}, {aFrente.Y:0}); {Ultimos(falas)}");

		Verbo(ana, "item_largar", $"{maca}/{Jandirus.Core.Items.Chao.PalavraDeTudo}");
		AfirmarV21("...e \"tudo\" larga a pilha inteira, que se junta ao MESMO monte (o item que empilha na mochila empilha no chao)",
				   ana.Mochila.Quantos(maca) == 0 && chao.Count(l => l.Item == maca) == 1 && chao.Find(l => l.Item == maca) is { Quantidade: 3 },
				   $"{chao.Count(l => l.Item == maca)} monte(s), x{chao.Find(l => l.Item == maca)?.Quantidade:0}");

		Equipar(ana, Jandirus.Core.Items.CatalogoDeItens.Get(scouterId)!);
		falas = Ouvir(() => Verbo(ana, "item_largar", scouterId));
		AfirmarV21("**O QUE ESTA EM USO NAO SAI**: o scouter ligado nao se larga (e a frase manda tirar antes)",
				   ana.Mochila.Quantos(scouterId) == 1 && !chao.Exists(l => l.Item == scouterId) && Disse(falas, "tire antes"), Ultimos(falas));
		Equipar(ana, Jandirus.Core.Items.CatalogoDeItens.Get(scouterId)!);
		Verbo(ana, "item_largar", scouterId);
		AfirmarV21("...e desligado ele cai, num monte so dele (o que nao empilha nao se mistura com as macas)",
				   ana.Mochila.Quantos(scouterId) == 0 && chao.Count == 2 && chao.Exists(l => l.Item == scouterId && l.Quantidade == 1));

		Jandirus.Core.Items.Chao.LargarApagaDeTeste = true;
		try
		{
			ana.Mochila.Guardar(maca);
			int antes = chao.Count;
			double macasNoChao = chao.Find(l => l.Item == maca)?.Quantidade ?? 0;
			Verbo(ana, "item_largar", maca);
			AfirmarV21("(defeito injetado: largar apaga) a maca some da mochila e NAO aparece no chao -- o \"Jogar fora\" da v0.2.0",
					   ana.Mochila.Quantos(maca) == 0 && chao.Count == antes && (chao.Find(l => l.Item == maca)?.Quantidade ?? 0) == macasNoChao);
		}
		finally { Jandirus.Core.Items.Chao.LargarApagaDeTeste = false; }

		// ---- L2) A CARTEIRA
		ana.Ficha.Zeni = 1000;
		ana.Ficha.Recursos = 400;
		Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "zeni/300");
		Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "recursos/tudo");
		AfirmarV21("**O ZENI E O RECURSO TAMBEM SE LARGAM**: 300 de zeni saem da carteira (ficam 700) e os 400 de recursos saem todos -- cada um no seu monte",
				   Math.Abs(ana.Ficha.Zeni - 700) < 1e-6 && ana.Ficha.Recursos < 1e-6
				   && chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni) is { Quantidade: 300 }
				   && chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Recursos) is { Quantidade: 400 },
				   $"zeni {ana.Ficha.Zeni:0}, recursos {ana.Ficha.Recursos:0}; {chao.Count} coisa(s) no chao");
		Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "zeni/200");
		AfirmarV21("...e o segundo punhado de zeni cai no MESMO monte (500)",
				   chao.Count(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni) == 1 && chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni) is { Quantidade: 500 }
				   && Math.Abs(ana.Ficha.Zeni - 500) < 1e-6);
		falas = Ouvir(() =>
		{
			Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "zeni");
			Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "zeni/0");
			Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "zeni/-50");
			Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "pedras/10");
		});
		AfirmarV21("sem quantia, com zero, com numero negativo ou com outra coisa no lugar da moeda nada sai da carteira (o verbo pergunta)",
				   Math.Abs(ana.Ficha.Zeni - 500) < 1e-6 && Disse(falas, "quanto?") && Disse(falas, "largar o quê?"), Ultimos(falas));
		Verbo(ana, Jandirus.Core.Items.Chao.VerboLargar, "zeni/999999");
		AfirmarV21("...e pedir mais do que ha larga so o que ha (a carteira nao fica negativa)",
				   ana.Ficha.Zeni < 1e-6 && chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni) is { Quantidade: 1000 });

		// ---- L3) O FIO
		var fio = new LiteNetLib.Utils.NetDataReader(PacoteDoChao(zona).CopyData());
		fio.GetByte();
		int n = fio.GetUShort();
		var lidos = new List<(int Id, byte Tipo, string Item, double Quantidade, float X, float Y, string Arte)>();
		// (2026-10-15) e a ARTE no fim de cada coisa (os pesos largados com a arte escolhida; vazia no resto)
		for (int i = 0; i < n; i++) lidos.Add((fio.GetInt(), fio.GetByte(), fio.GetString(64), fio.GetDouble(), fio.GetFloat(), fio.GetFloat(), fio.GetString(160)));
		AfirmarV21("o `S2C.Chao` leva a lista INTEIRA da zona, coisa por coisa, e se le na ordem em que foi escrito sem sobrar byte",
				   n == chao.Count && fio.AvailableBytes == 0
				   && lidos.Exists(x => x.Tipo == (byte)Jandirus.Core.Items.TipoDeLargado.Zeni && x.Quantidade == 1000 && x.Item.Length == 0)
				   && lidos.Exists(x => x.Tipo == (byte)Jandirus.Core.Items.TipoDeLargado.Item && x.Item == maca && x.Quantidade == 3)
				   && lidos.Select(x => x.Id).Distinct().Count() == n,
				   $"{n} coisa(s), sobra {fio.AvailableBytes}");

		// ---- L4) PEGAR
		ServerPlayer bob = ForjarV2("Bob", aFrente + new Vec2(t, 0), 50_000);
		ServerPlayer longe = ForjarV2("Longe", aFrente + new Vec2(8 * t, 0), 50_000);
		Verbo(longe, Jandirus.Core.Items.Chao.VerboPegar, "");
		int idDoZeni = chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni)!.Id;
		Verbo(longe, Jandirus.Core.Items.Chao.VerboPegar, $"#{idDoZeni}");
		AfirmarV21("**SO SE PEGA O QUE ESTA AO ALCANCE**: a oito tiles o verbo nao leva nada -- nem pedindo a coisa pelo numero dela",
				   longe.Ficha.Zeni < 1e-6 && longe.Mochila.Ocupados == 0 && chao.Count == 4, $"{chao.Count} coisa(s) no chao");

		bob.Combate.Nocautear(60);
		Verbo(bob, Jandirus.Core.Items.Chao.VerboPegar, $"#{idDoZeni}");
		bool caidoNaoPega = bob.Ficha.Zeni < 1e-6 && chao.Count == 4;
		bob.Combate.Levantar();
		bob.Ficha.Zeni = 50;
		falas = FalasV21(() => Verbo(bob, Jandirus.Core.Items.Chao.VerboPegar, $"#{idDoZeni}"));
		AfirmarV21("**PEGAR O ZENI SOMA NA CARTEIRA** de quem pegou (de pe: nocauteado ninguem pega nada), o monte sai do chao e quem esta a vista fica sabendo",
				   caidoNaoPega && Math.Abs(bob.Ficha.Zeni - 1050) < 1e-6 && !chao.Exists(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni) && Disse(falas, "pega"),
				   $"caido nao pega {caidoNaoPega}; zeni {bob.Ficha.Zeni:0}; {Ultimos(falas)}");

		for (int i = 0; i < 3; i++) Verbo(bob, Jandirus.Core.Items.Chao.VerboPegar, "");
		AfirmarV21("...e sem dizer qual, cada gesto leva a coisa MAIS PERTO: tres gestos esvaziam o chao -- as macas (as tres num slot so), o scouter e os recursos",
				   chao.Count == 0 && bob.Mochila.Quantos(maca) == 3 && bob.Mochila.Quantos(scouterId) == 1 && Math.Abs(bob.Ficha.Recursos - 400) < 1e-6,
				   $"chao {chao.Count}; macas {bob.Mochila.Quantos(maca)}, scouter {bob.Mochila.Quantos(scouterId)}, recursos {bob.Ficha.Recursos:0}");

		// (a mochila cheia: o que nao cabe FICA)
		ServerPlayer cheio = ForjarV2("Cheio", aFrente + new Vec2(0, t), 50_000);
		for (int i = 0; i < Jandirus.Core.Items.Inventario.Slots - 1; i++) cheio.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Pa);
		PorNoChao(ZonaDaBancadaDeProjetil, aFrente, Jandirus.Core.Items.TipoDeLargado.Item, scouterId, 3);
		falas = Ouvir(() => Verbo(cheio, Jandirus.Core.Items.Chao.VerboPegar, ""));
		bool levouUm = cheio.Mochila.Quantos(scouterId) == 1 && chao.Find(l => l.Item == scouterId) is { Quantidade: 2 };
		falas = Ouvir(() => Verbo(cheio, Jandirus.Core.Items.Chao.VerboPegar, ""));
		AfirmarV21("**O QUE NAO CABE NA MOCHILA FICA NO CHAO**: de tres scouters, quem tem uma vaga leva um e deixa dois -- e com a mochila cheia o gesto recusa e avisa",
				   levouUm && cheio.Mochila.Quantos(scouterId) == 1 && chao.Find(l => l.Item == scouterId) is { Quantidade: 2 } && Disse(falas, "cheia"),
				   $"levou um {levouUm}; {Ultimos(falas)}");
		chao.Clear();

		// ---- L5) CONTRA A PAREDE, AOS PES
		int deY = 8;
		if (_pjMapa is { } mapa && AcharMuroV21(mapa, 3, 0, ref deY, out int mx, out int my))
		{
			ServerPlayer encostado = ForjarV2("Encostado", CentroV21(mx - 1, my), 50_000);
			encostado.Facing = Facing.East;
			encostado.Ficha.Zeni = 10;
			Verbo(encostado, Jandirus.Core.Items.Chao.VerboLargar, "zeni/10");
			Jandirus.Core.Items.Largado? aosPes = chao.Find(l => l.Tipo == Jandirus.Core.Items.TipoDeLargado.Zeni);
			AfirmarV21("**CONTRA A PAREDE, O QUE SE LARGA CAI AOS PES**: o passo a frente e parede, e o monte fica na celula de quem largou",
					   aosPes != null && Math.Abs(aosPes.X - CentroV21(mx - 1, my).X) < 0.01f && Math.Abs(aosPes.Y - CentroV21(mx - 1, my).Y) < 0.01f,
					   aosPes == null ? "nada no chao" : $"({aosPes.X:0}, {aosPes.Y:0}) contra ({CentroV21(mx - 1, my).X:0}, {CentroV21(mx - 1, my).Y:0})");
			DesfazerV2(encostado);
		}
		else AfirmarV21("(montagem) ha uma parede simples com chao livre a oeste, pra largar contra ela", false);
		chao.Clear();

		// ---- L6) O RELOGIO E O TETO
		Jandirus.Core.Items.Largado velho = PorNoChao(ZonaDaBancadaDeProjetil, aFrente, Jandirus.Core.Items.TipoDeLargado.Zeni, "", 5);
		Jandirus.Core.Items.Largado novoMonte = PorNoChao(ZonaDaBancadaDeProjetil, aFrente + new Vec2(t, 0), Jandirus.Core.Items.TipoDeLargado.Recursos, "", 5);
		bool prazoCerto = Math.Abs(velho.SomeMs - NowMs() - (long)(Jandirus.Core.Items.Chao.SegundosNoChao * 1000)) < 2000;
		velho.SomeMs = NowMs() - 1;
		TickDoChao();
		AfirmarV21($"**O QUE NINGUEM PEGA SOME**: a coisa nasce com {Jandirus.Core.Items.Chao.SegundosNoChao / 60:0} minutos de prazo, e vencido o prazo o tique a tira do chao (a outra fica)",
				   prazoCerto && !chao.Contains(velho) && chao.Contains(novoMonte), $"prazo certo {prazoCerto}; {chao.Count} coisa(s)");
		chao.Clear();
		for (int i = 0; i < Jandirus.Core.Items.Chao.MaximoPorZona + 5; i++)
			PorNoChao(ZonaDaBancadaDeProjetil, new Vec2((4 + i) * t + 1, 6 * t + 1), Jandirus.Core.Items.TipoDeLargado.Item, scouterId, 1);
		AfirmarV21($"...e a zona guarda no maximo {Jandirus.Core.Items.Chao.MaximoPorZona} coisas: passou disso, as mais velhas saem",
				   chao.Count == Jandirus.Core.Items.Chao.MaximoPorZona && Math.Abs(chao[0].X - ((4 + 5) * t + t / 2f)) < 0.01f, $"{chao.Count} coisa(s)");
		chao.Clear();

		DesfazerV2(ana, bob, longe, cheio);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// R) A CARTEIRA DO CAIDO E A BOLSA DO NPC
	// =====================================================================
	private void ACarteiraDoCaidoV21()
	{
		GD.Print("[v021] -- R) A CARTEIRA DO CAIDO E A BOLSA DO NPC: roubar zeni e recursos de quem esta nocauteado (item 18)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const int t = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer ladrao = ForjarV2("Ladrao", c, 500_000);
		ServerPlayer vitima = ForjarV2("Vitima", c + new Vec2(t, 0), 5_000);
		vitima.Ficha.Zeni = 520.7;
		vitima.Ficha.Recursos = 200;
		ladrao.Ficha.Zeni = 10;
		string zeni = Jandirus.Core.Items.Saque.Zeni, recursos = Jandirus.Core.Items.Saque.Recursos;

		List<string> falas = Ouvir(() => Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, zeni));
		AfirmarV21("DE PE ninguem e roubado: o verbo recusa e a carteira fica", Math.Abs(vitima.Ficha.Zeni - 520.7) < 1e-6 && Math.Abs(ladrao.Ficha.Zeni - 10) < 1e-6 && Disse(falas, "nocauteado"), Ultimos(falas));

		vitima.Combate.Nocautear(60);
		(double Zeni, double Recursos) aVista = CarteiraDoCaido(vitima);
		AfirmarV21("**NOCAUTEADO, A CARTEIRA ENTRA NO SAQUE**: o que da pra levar e tudo o que ele carrega, em quantia inteira (520 de zeni, 200 de recursos)",
				   aVista == (520, 200), $"{aVista}");

		falas = FalasV21(() => Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, zeni));
		AfirmarV21("**ROUBAR O ZENI LEVA TUDO DE UMA VEZ** (o ramo \"Zenni\" do `Steal()` do DM): 520 mudam de carteira, o troco de centavos fica, e quem esta a vista fica sabendo quanto",
				   Math.Abs(ladrao.Ficha.Zeni - 530) < 1e-6 && vitima.Ficha.Zeni < 1 && Disse(falas, "rouba") && Disse(falas, "520"), $"ladrao {ladrao.Ficha.Zeni:0.##}, vitima {vitima.Ficha.Zeni:0.##}; {Ultimos(falas)}");

		falas = Ouvir(() => Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, recursos));
		bool respiro = Math.Abs(vitima.Ficha.Recursos - 200) < 1e-6 && Disse(falas, "um de cada vez");
		ladrao.ProximoSaqueMs = 0;
		Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, recursos);
		AfirmarV21("...e os recursos tambem, no gesto seguinte (o respiro entre dois saques vale pra carteira)",
				   respiro && Math.Abs(ladrao.Ficha.Recursos - 200) < 1e-6 && vitima.Ficha.Recursos < 1e-6, $"respiro {respiro}; ladrao {ladrao.Ficha.Recursos:0}");

		ladrao.ProximoSaqueMs = 0;
		falas = Ouvir(() => Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, zeni));
		AfirmarV21("de carteira vazia nao sai nada (e a frase diz que ele nao carrega zeni)", Math.Abs(ladrao.Ficha.Zeni - 530) < 1e-6 && Disse(falas, "não carrega"), Ultimos(falas));

		vitima.Ficha.Zeni = 300;
		ladrao.ProximoSaqueMs = 0;
		Jandirus.Core.Items.Saque.CarteiraFicaDeTeste = true;
		try
		{
			Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, zeni);
			AfirmarV21("(defeito injetado: a carteira fica) o caido de 300 zeni nao tem carteira no saque, e o verbo nao leva nada -- a v0.2.0",
					   CarteiraDoCaido(vitima) == (0, 0) && Math.Abs(vitima.Ficha.Zeni - 300) < 1e-6 && Math.Abs(ladrao.Ficha.Zeni - 530) < 1e-6);
		}
		finally { Jandirus.Core.Items.Saque.CarteiraFicaDeTeste = false; }

		// ---- a bolsa do NPC
		ulong semente = 0xB01_5AUL;
		double doCidadao = Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Cidadao, semente);
		double doVilao = Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Vilao, semente);
		double doChefe = Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Chefe, semente);
		bool variam = Enumerable.Range(1, 40).Select(i => Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Cidadao, (ulong)i * 7919)).Distinct().Count() > 20;
		bool naFaixa = Enumerable.Range(1, 400).All(i =>
		{
			double z = Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Cidadao, (ulong)i * 104729);
			return z >= Jandirus.Core.Npc.BolsaDeNpc.ZeniMinDoCidadao && z <= Jandirus.Core.Npc.BolsaDeNpc.ZeniMaxDoCidadao && z == Math.Floor(z);
		});
		AfirmarV21("**O NPC NASCE CARREGANDO ZENI**: o cidadao dentro da faixa dele (inteiro, e variando de um pro outro), o vilao na dele e o chefe de saga com os numeros do DM -- a mesma semente da sempre a mesma bolsa",
				   naFaixa && variam && doCidadao == Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Cidadao, semente)
				   && doVilao >= Jandirus.Core.Npc.BolsaDeNpc.ZeniMinDoVilao && doVilao <= Jandirus.Core.Npc.BolsaDeNpc.ZeniMaxDoVilao
				   && doChefe >= 500_000 && doChefe <= 800_000,
				   $"cidadao {doCidadao:0}, vilao {doVilao:0}, chefe {doChefe:0}; na faixa {naFaixa}, variam {variam}");
		AfirmarV21("...e o bicho e o lutador de torneio nao carregam nada",
				   Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Animal, semente) == 0 && Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Lutador, semente) == 0);

		Jandirus.Core.Npc.MoldeDeNpc? cidadao = _moldes?.Todos.FirstOrDefault(m => m.Tipo == Jandirus.Core.Npc.TipoDeNpc.Cidadao);
		ServerPlayer? npc = cidadao == null ? null : NascerNpc(cidadao.Id, ZonaDaBancadaDeProjetil, c + new Vec2(0, 2 * t), 0x7121_0001UL);
		AfirmarV21("o cidadao que nasce pela fabrica de producao (`NascerNpc`) ja vem com a bolsa dele na ficha",
				   npc != null && npc.Ficha.Zeni >= Jandirus.Core.Npc.BolsaDeNpc.ZeniMinDoCidadao && npc.Ficha.Zeni <= Jandirus.Core.Npc.BolsaDeNpc.ZeniMaxDoCidadao,
				   npc == null ? "nao nasceu" : $"{npc.Ficha.Zeni:0} zeni");
		if (npc != null)
		{
			double bolsa = Math.Floor(npc.Ficha.Zeni);
			npc.Combate.Nocautear(60);
			ladrao.Pos = npc.Pos + new Vec2(t * 0.5f, 0);
			vitima.Pos = c + new Vec2(20 * t, 0);   // (pra o caido mais perto ser o NPC)
			ladrao.ProximoSaqueMs = 0;
			double antes = ladrao.Ficha.Zeni;
			Verbo(ladrao, Jandirus.Core.Items.Saque.Verbo, zeni);
			AfirmarV21("**...E NOCAUTEADO ELE E ROUBADO COMO QUALQUER UM**: a bolsa do NPC vai inteira pra carteira de quem revistou",
					   Math.Abs(ladrao.Ficha.Zeni - antes - bolsa) < 1e-6 && npc.Ficha.Zeni < 1, $"bolsa {bolsa:0}; ladrao {antes:0} -> {ladrao.Ficha.Zeni:0}");
			RemoverNpc(npc);
		}

		Jandirus.Core.Npc.BolsaDeNpc.NpcSemBolsaDeTeste = true;
		try { AfirmarV21("(defeito injetado: o NPC nasce de bolso vazio) cidadao, vilao e chefe carregam zero -- a v0.2.0", Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Cidadao, semente) == 0 && Jandirus.Core.Npc.BolsaDeNpc.Zeni(Jandirus.Core.Npc.TipoDeNpc.Chefe, semente) == 0); }
		finally { Jandirus.Core.Npc.BolsaDeNpc.NpcSemBolsaDeTeste = false; }

		DesfazerV2(ladrao, vitima);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// P) OS PEDIDOS NA TELA
	// =====================================================================
	/// <summary>
	/// Um corpo forjado que e PESSOA pro discipulado: o `ForjarV2` da a mesma conta a todos (a mesma assinatura), e
	/// mestre, aluno e colega tem de ser pessoas diferentes.
	/// </summary>
	private ServerPlayer PessoaV21(string nome, Vec2 onde, double bp)
	{
		ServerPlayer p = ForjarV2(nome, onde, bp);
		p.Conta = "bancada_v021_" + nome.ToLowerInvariant();
		p.Class = p.Ficha.Class;
		return p;
	}

	/// <summary>Responde pela CHAVE do registro, como o cartao da tela faz (`PainelDePedidos.Mandar`): a habilidade ou o verbo.</summary>
	private void ResponderPelaChaveV21(ServerPlayer pl, string chave)
	{
		if (chave.StartsWith(RequisitosDosVerbos.PrefixoDeHabilidade, StringComparison.Ordinal))
		{
			UsarHabilidade(pl, chave[RequisitosDosVerbos.PrefixoDeHabilidade.Length..]);
			return;
		}
		string resto = chave[RequisitosDosVerbos.PrefixoDeVerbo.Length..];
		int espaco = resto.IndexOf(' ');
		if (espaco < 0) Verbo(pl, resto, "");
		else Verbo(pl, resto[..espaco], resto[(espaco + 1)..]);
	}

	private void OsPedidosNaTelaV21()
	{
		GD.Print("[v021] -- P) OS PEDIDOS NA TELA: o convite, a licao e as ofertas viram um pedido com Aceitar e Recusar (item 15)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const int t = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(6);

		// ---- P1) A TABELA: todo pedido tem as duas respostas, e cada uma e um verb do registro trancado pelo fato dele
		var ruins = new List<string>();
		foreach (Fatos f in PedidosPendentes.Todos)
		{
			(string sim, string nao) = PedidosPendentes.Respostas(f);
			(string rotuloSim, string rotuloNao) = PedidosPendentes.Rotulos(f);
			if (sim.Length == 0 || nao.Length == 0 || sim == nao || rotuloSim.Length == 0 || rotuloNao.Length == 0)
			{
				ruins.Add($"{f}: sem as duas respostas");
				continue;
			}
			foreach (string chave in new[] { sim, nao })
			{
				if (RequisitosDosVerbos.Linha(chave) is not { } linha) { ruins.Add($"{f}: '{chave}' nao esta no registro"); continue; }
				if (!linha.Requisito.Atende(new PosseEscrita { Fatos = f })) ruins.Add($"{f}: '{chave}' nao abre com o fato dele");
				if (linha.Requisito.Atende(new PosseEscrita())) ruins.Add($"{f}: '{chave}' abre sem pedido nenhum");
			}
		}
		string[] nomesDePedido = ["Convite", "Oferta", "Pedido", "Licao", "Desafio"];
		List<Fatos> foraDaLista = [.. Enum.GetValues<Fatos>().Where(f => nomesDePedido.Any(n => f.ToString().StartsWith(n, StringComparison.Ordinal))
																		  && !PedidosPendentes.Todos.Contains(f))];
		AfirmarV21($"**TODO PEDIDO TEM AS DUAS RESPOSTAS, E ELAS SAO OS VERBS DO MENU**: os {PedidosPendentes.Todos.Length} tipos apontam pra chaves que existem no registro, abrem com o fato do pedido e nao abrem sem ele",
				   ruins.Count == 0 && PedidosPendentes.Todos.Length >= 9, string.Join(" | ", ruins));
		AfirmarV21("...e nenhum fato com nome de pedido (Convite, Oferta, Pedido, Licao, Desafio) ficou fora da lista -- o proximo que nascer cai aqui",
				   foraDaLista.Count == 0, string.Join(", ", foraDaLista));

		// ---- P2) O CONVITE DE MESTRE, pela porta de producao
		ServerPlayer mestre = PessoaV21("Mestre", c, 900_000), aluno = PessoaV21("Aluno", c + new Vec2(t, 0), 100_000);
		mestre.Facing = Facing.East;
		aluno.Facing = Facing.West;
		var lista = new List<PedidoNaTela>();
		try
		{
			PedidosPraTela(aluno, lista);
			bool vazioAntes = lista.Count == 0;
			UsarHabilidade(mestre, "mst_convidar");
			PedidosPraTela(aluno, lista);
			AfirmarV21("**O CONVITE DE MESTRE VIRA UM PEDIDO NA TELA DO CONVIDADO**: um so, com o nome de quem convida e o prazo do convite contando",
					   vazioAntes && lista.Count == 1 && lista[0].Fato == Fatos.ConviteDeMestre && lista[0].Texto.Contains("Mestre quer ser o seu MESTRE")
					   && lista[0].Segundos > 0 && lista[0].Segundos <= (int)Discipulado.PrazoDoConviteSegundos
					   && (FatosDe(aluno) & Fatos.ConviteDeMestre) != 0,
					   $"antes vazio {vazioAntes}; {lista.Count} pedido(s): {string.Join(" | ", lista.Select(p => $"{p.Fato} '{p.Texto}' {p.Segundos}s"))}");
			PedidosPraTela(mestre, lista);
			AfirmarV21("...e quem convidou nao recebe pedido nenhum", lista.Count == 0, $"{lista.Count}");

			ResponderPelaChaveV21(aluno, PedidosPendentes.Respostas(Fatos.ConviteDeMestre).Sim);
			PedidosPraTela(aluno, lista);
			AfirmarV21("respondido pelo \"sim\" do pedido, o vinculo nasce e o pedido sai da lista",
					   EhMeuMestre(aluno, mestre) && lista.Count == 0, $"mestre {EhMeuMestre(aluno, mestre)}, {lista.Count} pedido(s)");

			Desvincular(aluno.Assinatura, "bancada: medindo o \"nao\"");
			UsarHabilidade(mestre, "mst_convidar");
			bool pediuDeNovo = aluno.PedidoDoMestre != null;
			ResponderPelaChaveV21(aluno, PedidosPendentes.Respostas(Fatos.ConviteDeMestre).Nao);
			PedidosPraTela(aluno, lista);
			AfirmarV21("...e o \"nao\" tambem o tira, sem vinculo", pediuDeNovo && lista.Count == 0 && !EhMeuMestre(aluno, mestre), $"pediu {pediuDeNovo}, {lista.Count} pedido(s)");

			UsarHabilidade(mestre, "mst_convidar");
			if (aluno.PedidoDoMestre is { } vivo) aluno.PedidoDoMestre = vivo with { Ate = NowMs() - 1 };
			PedidosPraTela(aluno, lista);
			AfirmarV21("**PEDIDO VENCIDO NAO E PEDIDO**: passado o prazo ele sai da lista sozinho, e o fato com ele",
					   lista.Count == 0 && (FatosDe(aluno) & Fatos.ConviteDeMestre) == 0, $"{lista.Count} pedido(s)");
			aluno.PedidoDoMestre = null;

			// ---- P3) DOIS DE UMA VEZ: o convite e uma licao
			Skill? ensinavel = _skills?.Todas.Where(s => s.Ensinavel && !s.Arvore && s.Nome.Length > 0)
											  .OrderBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault();
			AfirmarV21("(palco) o catalogo tem uma skill que se ensina", ensinavel != null);
			if (ensinavel != null)
			{
				mestre.Livro.Dar(ensinavel.Path);
				aluno.Livro.Conceder(1_000);
				UsarHabilidade(mestre, "mst_convidar");
				UsarHabilidade(mestre, $"ens_ensinar:{ensinavel.Path}");
				PedidosPraTela(aluno, lista);
				AfirmarV21("**DOIS PEDIDOS AO MESMO TEMPO SAO DOIS NA TELA** (o convite e a licao), e a licao comum diz que nao gasta marco",
						   lista.Count == 2 && lista.Any(p => p.Fato == Fatos.ConviteDeMestre)
						   && lista.Any(p => p.Fato == Fatos.LicaoOferecida && p.Texto.Contains(ensinavel.Nome) && p.Texto.Contains("não gasta marco")
											 && p.Segundos > 0 && p.Segundos <= (int)EnsinoDeSkill.PrazoDaOfertaSegundos),
						   string.Join(" | ", lista.Select(p => $"{p.Fato} '{p.Texto}' {p.Segundos}s")));

				PedidosPendentes.SemNotificacaoDeTeste = true;
				try
				{
					PedidosPraTela(aluno, lista);
					Fatos osDois = Fatos.ConviteDeMestre | Fatos.LicaoOferecida;
					AfirmarV21("(defeito injetado: sem notificacao) nada vai pra tela, e os dois fatos continuam de pe -- a v0.2.0, em que o pedido so existia como botao no menu P",
							   lista.Count == 0 && (FatosDe(aluno) & osDois) == osDois, $"{lista.Count} pedido(s)");
				}
				finally { PedidosPendentes.SemNotificacaoDeTeste = false; }

				ResponderPelaChaveV21(aluno, PedidosPendentes.Respostas(Fatos.LicaoOferecida).Sim);
				PedidosPraTela(aluno, lista);
				AfirmarV21("o \"sim\" da licao ensina a skill e tira SO o pedido dela: o convite continua na tela",
						   aluno.Livro.Sabe(ensinavel.Path) && lista.Count == 1 && lista[0].Fato == Fatos.ConviteDeMestre,
						   $"sabe {aluno.Livro.Sabe(ensinavel.Path)}; {string.Join(" | ", lista.Select(p => p.Fato))}");
				aluno.PedidoDoMestre = null;
			}

			// ---- P4) AS OFERTAS GUARDADAS POR CONTA (potencial e juventude): o pedido aparece e o "nao" dele e o verb certo
			long daqui = NowMs() + 30_000;
			_ofertasDePotencialG11[aluno.Conta] = (mestre.Conta, daqui);
			_ofertasDeJuventudeG8[aluno.Conta] = (mestre.Conta, 20, daqui);
			PedidosPraTela(aluno, lista);
			bool osDoisNaTela = lista.Count == 2 && lista.Any(p => p.Fato == Fatos.OfertaDePotencial && p.Texto.Contains("potencial"))
								&& lista.Any(p => p.Fato == Fatos.OfertaDeJuventude && p.Texto.Contains("20 anos"));
			ResponderPelaChaveV21(aluno, PedidosPendentes.Respostas(Fatos.OfertaDePotencial).Nao);
			PedidosPraTela(aluno, lista);
			bool soAJuventude = lista.Count == 1 && lista[0].Fato == Fatos.OfertaDeJuventude;
			ResponderPelaChaveV21(aluno, PedidosPendentes.Respostas(Fatos.OfertaDeJuventude).Nao);
			PedidosPraTela(aluno, lista);
			AfirmarV21("a oferta de POTENCIAL e a de JUVENTUDE tambem sao pedidos na tela, e o \"nao\" de cada uma tira a dela (a chave do pedido e o verb que o servidor atende)",
					   osDoisNaTela && soAJuventude && lista.Count == 0, $"os dois {osDoisNaTela}, so a juventude {soAJuventude}, no fim {lista.Count}");
		}
		finally
		{
			_ofertasDePotencialG11.Remove(aluno.Conta);
			_ofertasDeJuventudeG8.Remove(aluno.Conta);
			Desvincular(aluno.Assinatura, "fim da bancada");
			DesfazerV2(mestre, aluno);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// T) TREINAR COM O MESTRE E COM O COLEGA
	// =====================================================================
	private void OTreinoComMestreEColegaV21()
	{
		GD.Print("[v021] -- T) TREINAR COM O MESTRE E COM O COLEGA: o bonus do mestre vale sempre, e o do colega e menor (item 17)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const int t = ZoneCollision.TileSize;
		const double bp = 1_000_000;
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer a = PessoaV21("AlunoA", c, bp), b = PessoaV21("AlunoB", c + new Vec2(30 * t, 0), bp);
		ServerPlayer mestre = PessoaV21("Mestre", c + new Vec2(40 * t, 0), bp * 3);
		ServerPlayer outroMestre = PessoaV21("OutroMestre", c + new Vec2(50 * t, 0), bp * 3), dele = PessoaV21("AlunoDeOutro", c + new Vec2(60 * t, 0), bp);
		ServerPlayer estranho = PessoaV21("Estranho", c + new Vec2(70 * t, 0), bp);
		ServerPlayer[] todos = [a, b, mestre, outroMestre, dele, estranho];
		try
		{
			Vincular(mestre, a);
			Vincular(mestre, b);
			Vincular(outroMestre, dele);

			AfirmarV21("**COLEGA E QUEM TEM O MESMO MESTRE**: os dois alunos se reconhecem, nos dois sentidos; o mestre nao e colega do aluno, nem o aluno de outro mestre, nem o estranho, nem ele mesmo",
					   EhColegaDeMestre(a, b) && EhColegaDeMestre(b, a) && !EhColegaDeMestre(a, mestre) && !EhColegaDeMestre(mestre, a)
					   && !EhColegaDeMestre(a, dele) && !EhColegaDeMestre(a, estranho) && !EhColegaDeMestre(estranho, a)
					   && !EhColegaDeMestre(a, a) && !EhColegaDeMestre(a, null));

			// ---- a conta, com todo mundo do MESMO poder (expresso e base): so o vinculo muda
			foreach (ServerPlayer p in todos) { p.Ficha.BP = bp; p.Ficha.expressedBP = 1_000; }
			double comEstranho = a.Ficha.FightGainMult(estranho.Ficha, EhMeuMestre(a, estranho), EhColegaDeMestre(a, estranho));
			double comColega = a.Ficha.FightGainMult(b.Ficha, EhMeuMestre(a, b), EhColegaDeMestre(a, b));
			double comMestre = a.Ficha.FightGainMult(mestre.Ficha, EhMeuMestre(a, mestre), EhColegaDeMestre(a, mestre));
			double doMestreNoAluno = mestre.Ficha.FightGainMult(a.Ficha, EhMeuMestre(mestre, a), EhColegaDeMestre(mestre, a));
			AfirmarV21($"**DO MESMO PODER: O ESTRANHO RENDE 1x, O COLEGA {GainKnobs.ColegaDeMestreGain:0.#}x E O MESTRE {GainKnobs.MasterGainMin:0.#}x** -- o do colega e menor que o do mestre, e o mestre nao ganha nada por bater no aluno",
					   Math.Abs(comEstranho - 1) < 1e-9 && Math.Abs(comColega - GainKnobs.ColegaDeMestreGain) < 1e-9
					   && Math.Abs(comMestre - GainKnobs.MasterGainMin) < 1e-9 && comColega > comEstranho && comColega < comMestre
					   && Math.Abs(doMestreNoAluno - 1) < 1e-9,
					   $"estranho {comEstranho:0.##}, colega {comColega:0.##}, mestre {comMestre:0.##}; mestre->aluno {doMestreNoAluno:0.##}");

			b.Ficha.expressedBP = 5_000;
			double colegaForte = a.Ficha.FightGainMult(b.Ficha, false, true);
			b.Ficha.expressedBP = 1_000;
			AfirmarV21($"contra o colega MUITO mais forte vale o maior dos dois -- o gap geral, ate {GainKnobs.FightGainCap:0.#}x: o bonus nunca piora o ganho",
					   Math.Abs(colegaForte - GainKnobs.FightGainCap) < 1e-9, $"{colegaForte:0.##}");
			AfirmarV21("...e o mestre nunca e medido como colega, mesmo se os dois bits chegarem juntos",
					   Math.Abs(a.Ficha.FightGainMult(mestre.Ficha, true, true) - comMestre) < 1e-9);

			// ---- o SOCO de producao: o BP que entra, com o colega e com o estranho
			Soco Medir(ServerPlayer alvo)
			{
				// (ninguem mais ao alcance: o soco acha o mais proximo)
				for (int i = 0; i < todos.Length; i++)
					if (todos[i] != a && todos[i] != alvo) todos[i].Pos = c + new Vec2((30 + 10 * i) * t, 0);
				alvo.Ficha.BP = bp;
				return GanhoDeUmSoco(a, alvo, bp);
			}
			Soco noEstranho = Medir(estranho), noColega = Medir(b), noMestre = Medir(mestre);
			double razaoColega = noEstranho.Ganho > 0 ? noColega.Ganho / noEstranho.Ganho : 0;
			double razaoMestre = noEstranho.Ganho > 0 ? noMestre.Ganho / noEstranho.Ganho : 0;
			AfirmarV21("(palco) os tres socos acertaram quem deviam, e renderam BP", noEstranho.Acertou && noColega.Acertou && noMestre.Acertou && noEstranho.Ganho > 0,
					   $"estranho {noEstranho.Acertou}/{noEstranho.Ganho:0.######}, colega {noColega.Acertou}, mestre {noMestre.Acertou}");
			AfirmarV21($"**O BONUS CHEGA NO BP**: um soco no colega rende {GainKnobs.ColegaDeMestreGain:0.#}x o de um soco no estranho, e um no mestre do mesmo poder {GainKnobs.MasterGainMin:0.#}x",
					   Math.Abs(razaoColega - GainKnobs.ColegaDeMestreGain) < 0.03 && Math.Abs(razaoMestre - GainKnobs.MasterGainMin) < 0.03,
					   $"colega {razaoColega:0.###}, mestre {razaoMestre:0.###}");

			GainKnobs.ColegaSemBonusDeTeste = true;
			try
			{
				Soco semBonus = Medir(b);
				AfirmarV21("(defeito injetado: colega sem bonus) o soco no colega rende o mesmo que no estranho -- a v0.2.0",
						   semBonus.Acertou && Math.Abs(semBonus.Ganho / noEstranho.Ganho - 1) < 0.03, $"razao {semBonus.Ganho / noEstranho.Ganho:0.###}");
			}
			finally { GainKnobs.ColegaSemBonusDeTeste = false; }

			GainKnobs.MestreSemPisoDeTeste = true;
			try
			{
				Soco semPiso = Medir(mestre);
				AfirmarV21("(defeito injetado: o bonus do mestre so pelo gap) o soco no mestre do MESMO poder rende o mesmo que no estranho -- o DM, e o colega renderia mais que o mestre",
						   semPiso.Acertou && Math.Abs(semPiso.Ganho / noEstranho.Ganho - 1) < 0.03, $"razao {semPiso.Ganho / noEstranho.Ganho:0.###}");
			}
			finally { GainKnobs.MestreSemPisoDeTeste = false; }

			// ---- a varredura: o bit do colega chega a TODO golpe que rende ganho
			string[] fontes = ["Server/GameServer.Combat.cs", "Server/GameServer.Projeteis.cs", "Server/GameServer.Tecnicas.G2.cs", "Server/GameServer.Tecnicas.G3.cs"];
			int comColegaNoFonte = 0;
			var semColega = new List<string>();
			foreach (string f in fontes)
				foreach (string bruta in Fonte(f))
				{
					string l = SemTextoNemComentario(bruta);
					if (!l.Contains("FightGainMult(", StringComparison.Ordinal)) continue;
					if (l.Contains("EhMeuMestre(", StringComparison.Ordinal) && l.Contains("EhColegaDeMestre(", StringComparison.Ordinal)) comColegaNoFonte++;
					else semColega.Add($"{f}: {l.Trim()}");
				}
			AfirmarV21("todo chamador de producao do `FightGainMult` (soco, tiro e as tecnicas de golpe) passa o bit do mestre E o do colega",
					   semColega.Count == 0 && comColegaNoFonte >= 7, $"{comColegaNoFonte} com os dois; sem: {string.Join(" | ", semColega)}");
		}
		finally
		{
			Desvincular(a.Assinatura, "fim da bancada");
			Desvincular(b.Assinatura, "fim da bancada");
			Desvincular(dele.Assinatura, "fim da bancada");
			DesfazerV2(todos);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// E) A LICAO DO MESTRE E A TECNICA PROPRIA
	// =====================================================================
	private void ALicaoDoMestreV21()
	{
		GD.Print("[v021] -- E) A LICAO DO MESTRE: o que nao se ensina a estranho, pelo cartao do aluno, e a tecnica de ki propria (item 4)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		AfirmarV21("(palco) o catalogo de skills carregou", _skills != null);
		if (_skills == null) return;

		const int t = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(6), longe = c + new Vec2(40 * t, 0);
		ServerPlayer mestre = PessoaV21("Mestre", c, 3_000_000), aluno = PessoaV21("Aluno", longe, 1_000_000);
		ServerPlayer estranho = PessoaV21("Estranho", longe + new Vec2(10 * t, 0), 1_000_000);
		mestre.Facing = Facing.East;

		// QUEM ESTA NA FRENTE DO MESTRE (um tile a leste); o outro vai pra longe.
		void Frente(ServerPlayer quem)
		{
			(quem == aluno ? estranho : aluno).Pos = longe;
			quem.Pos = c + new Vec2(t, 0);
			quem.Facing = Facing.West;
			mestre.RecargaDaLicao = 0;
			aluno.PedidoDeLicao = estranho.PedidoDeLicao = null;
		}
		var lista = new List<PedidoNaTela>();
		try
		{
			Vincular(mestre, aluno);
			aluno.Livro.Conceder(60);
			estranho.Livro.Conceder(60);

			// OS ALVOS, POR PREDICADO (nunca por id escrito a mao): uma skill que NAO se ensina e que o aluno, sozinho,
			// esbarraria numa porta que o mestre abre; outra que nao e do corpo dele; e uma das que sempre se ensinaram.
			Recusa Sozinho(Skill s) => aluno.Livro.PodeAprender(_skills, s.Path, aluno.Race, aluno.Class, vilao: false);
			List<Skill> fechadas = [.. _skills.Todas.Where(s => !s.Ensinavel && !s.Arvore && s.Nome.Length > 0 && EnsinoDeSkill.Custo(s) > 0)
													.OrderBy(s => s.Path, StringComparer.Ordinal)];
			Skill? trancada = fechadas.FirstOrDefault(s => Sozinho(s) is Recusa.TierTrancado or Recusa.FaltaPreRequisito or Recusa.AguardaAcendedor or Recusa.Desligada);
			Skill? outra = fechadas.FirstOrDefault(s => s != trancada && !EnsinoDeSkill.ForaDoCorpo(Sozinho(s)) && Sozinho(s) != Recusa.JaSabe);
			Skill? alheia = fechadas.FirstOrDefault(s => EnsinoDeSkill.ForaDoCorpo(Sozinho(s)));
			Skill? comum = _skills.Todas.Where(s => s.Ensinavel && !s.Arvore && s.Nome.Length > 0 && EnsinoDeSkill.Custo(s) > 0)
										 .OrderBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault();
			AfirmarV21("(palco) o catalogo tem os quatro alvos: a que o aluno nao compraria sozinho, outra do corpo dele, uma de corpo alheio e uma das que se ensinam",
					   trancada != null && outra != null && alheia != null && comum != null,
					   $"trancada {trancada?.Path}, outra {outra?.Path}, alheia {alheia?.Path}, comum {comum?.Path}");
			if (trancada == null || outra == null || alheia == null || comum == null) return;
			foreach (Skill s in new[] { trancada, outra, alheia, comum }) mestre.Livro.Dar(s.Path);
			Recusa portaDoAluno = Sozinho(trancada);
			int custo = EnsinoDeSkill.Custo(trancada);

			// ---- E1) a um estranho, nada muda
			Frente(estranho);
			List<string> falas = Ouvir(() => UsarHabilidade(mestre, $"ens_ensinar:{trancada.Path}"));
			AfirmarV21($"**A UM ESTRANHO, O QUE NAO SE ENSINA CONTINUA NAO SE ENSINANDO**: `{trancada.Nome}` e recusada, e a frase diz que so um mestre a mostra ao proprio aluno",
					   estranho.PedidoDeLicao == null && Disse(falas, "nao se ensina a um estranho"), Ultimos(falas));

			// ---- E2) ao proprio aluno: oferecida, paga e marcada
			Frente(aluno);
			int antes = aluno.Livro.MarcosLivres;
			falas = Ouvir(() => UsarHabilidade(mestre, $"ens_ensinar:{trancada.Path}"));
			PedidosPraTela(aluno, lista);
			AfirmarV21($"**AO PROPRIO ALUNO O MESTRE ENSINA O QUE SABE**: `{trancada.Nome}` -- que o aluno, sozinho, nao compraria ({portaDoAluno}) -- e oferecida, e o pedido na tela diz o preco em marcos",
					   aluno.PedidoDeLicao is { Tecnica: 0 } oferta && oferta.Path == trancada.Path && lista.Count == 1
					   && lista[0].Fato == Fatos.LicaoOferecida && lista[0].Texto.Contains($"custa {custo} marco") && Disse(falas, "GASTA"),
					   $"{string.Join(" | ", lista.Select(p => p.Texto))} // {Ultimos(falas)}");
			UsarHabilidade(aluno, "ens_licao_sim");
			AfirmarV21($"...aceita, ela entra no livro dele como ENSINADA e os {custo} marco(s) SAEM (a licao de mestre e uma compra com a porta aberta)",
					   aluno.Livro.Sabe(trancada.Path) && aluno.Livro.FoiEnsinada(trancada.Path) && aluno.Livro.MarcosLivres == antes - custo,
					   $"sabe {aluno.Livro.Sabe(trancada.Path)}, ensinada {aluno.Livro.FoiEnsinada(trancada.Path)}, marcos {antes} -> {aluno.Livro.MarcosLivres}");
			AfirmarV21("...e quem aprendeu assim NAO repassa: nem a um aluno dele",
					   EnsinoDeSkill.Avaliar(aluno.Livro, estranho.Livro, trancada, true, true, false, 0, false, eMeuAluno: true)
					   == Jandirus.Core.Skills.RecusaDeLicao.AprendeuDeAlguem);

			// ---- E3) sem os marcos, a licao de mestre nao fecha -- e nada sai
			Frente(aluno);
			int guardados = aluno.Livro.MarcosLivres;
			aluno.Livro.MarcosLivres = EnsinoDeSkill.Custo(outra) - 1;
			falas = Ouvir(() => UsarHabilidade(mestre, $"ens_ensinar:{outra.Path}"));
			AfirmarV21("sem os marcos a licao de mestre nao e nem oferecida, e a frase diz que ela os GASTA",
					   aluno.PedidoDeLicao == null && !aluno.Livro.Sabe(outra.Path) && Disse(falas, "GASTA os marcos"), Ultimos(falas));
			aluno.Livro.MarcosLivres = guardados;

			// ---- E4) o que nao e do corpo do aluno, nem o mestre ensina
			Frente(aluno);
			falas = Ouvir(() => UsarHabilidade(mestre, $"ens_ensinar:{alheia.Path}"));
			AfirmarV21($"**O MESTRE NAO MUDA O CORPO DO ALUNO**: `{alheia.Nome}` (que pro aluno e {Sozinho(alheia)}) e recusada",
					   aluno.PedidoDeLicao == null && Disse(falas, "nao e do corpo"), Ultimos(falas));

			// ---- E5) a que sempre se ensinou continua de graca, tambem pro aluno
			Frente(aluno);
			antes = aluno.Livro.MarcosLivres;
			UsarHabilidade(mestre, $"ens_ensinar:{comum.Path}");
			PedidosPraTela(aluno, lista);
			bool dizQueNaoGasta = lista.Count == 1 && lista[0].Texto.Contains("não gasta marco");
			UsarHabilidade(aluno, "ens_licao_sim");
			AfirmarV21($"a skill que SEMPRE se ensinou (`{comum.Nome}`) continua sem gastar marco, tambem do mestre pro aluno",
					   dizQueNaoGasta && aluno.Livro.Sabe(comum.Path) && aluno.Livro.MarcosLivres == antes, $"diz {dizQueNaoGasta}; marcos {antes} -> {aluno.Livro.MarcosLivres}");

			// ---- E6) o contra-exemplo
			EnsinoDeSkill.MestreSoEnsinaOQueSeEnsinaDeTeste = true;
			try
			{
				Frente(aluno);
				falas = Ouvir(() => UsarHabilidade(mestre, $"ens_ensinar:{outra.Path}"));
				AfirmarV21("(defeito injetado: o mestre so ensina o que se ensina) o proprio aluno ouve a recusa do estranho -- a v0.2.0",
						   aluno.PedidoDeLicao == null && Disse(falas, "nao se ensina a um estranho"), Ultimos(falas));
			}
			finally { EnsinoDeSkill.MestreSoEnsinaOQueSeEnsinaDeTeste = false; }

			// ---- E7) PELO CARTAO DO ALUNO: o gesto e mirado
			Frente(estranho);                       // o estranho na frente...
			aluno.Pos = c + new Vec2(0, t);         // ...e o aluno ao lado, a um tile
			falas = Ouvir(() => Verbo(mestre, "alunos_licao", $"{aluno.Assinatura}|{outra.Path}"));
			AfirmarV21("**PELO CARTAO DO ALUNO A LICAO VAI PRA ELE**, e nao pra quem estiver na frente: o aluno (ao lado, a um tile) recebe a oferta e o estranho da frente nao",
					   aluno.PedidoDeLicao is { } pelaAba && pelaAba.Path == outra.Path && estranho.PedidoDeLicao == null, Ultimos(falas));
			antes = aluno.Livro.MarcosLivres;
			UsarHabilidade(aluno, "ens_licao_sim");
			AfirmarV21("...e aceita, fecha como qualquer licao de mestre (a skill entra e os marcos saem)",
					   aluno.Livro.Sabe(outra.Path) && aluno.Livro.MarcosLivres == antes - EnsinoDeSkill.Custo(outra), $"marcos {antes} -> {aluno.Livro.MarcosLivres}");

			mestre.RecargaDaLicao = 0;
			falas = Ouvir(() => Verbo(mestre, "alunos_licao", $"{estranho.Assinatura}|{comum.Path}"));
			bool recusouOEstranho = estranho.PedidoDeLicao == null && Disse(falas, "aluno seu");
			falas = Ouvir(() => Verbo(aluno, "alunos_licao", $"{mestre.Assinatura}|{comum.Path}"));
			bool recusouOAluno = mestre.PedidoDeLicao == null && Disse(falas, "aluno seu");
			aluno.Livro.Esquecer(comum.Path);
			aluno.Pos = c + new Vec2(0, 5 * t);
			mestre.RecargaDaLicao = 0;
			falas = Ouvir(() => Verbo(mestre, "alunos_licao", $"{aluno.Assinatura}|{comum.Path}"));
			AfirmarV21("o gesto do cartao so vale pra um ALUNO SEU (nem pro estranho, nem do aluno sobre o mestre) e pede o aluno colado: a cinco tiles a licao e recusada",
					   recusouOEstranho && recusouOAluno && aluno.PedidoDeLicao == null && Disse(falas, "colado"),
					   $"estranho {recusouOEstranho}, aluno {recusouOAluno}; {Ultimos(falas)}");

			// ================================================================ A TECNICA DE KI DO MESTRE
			AbrirAMesaDeBancada(mestre);
			CriarTecnica(mestre);
			TextoDaMesa(mestre, "nome/Golpe do Mestre");
			SalvarTecnica(mestre);
			CriarTecnica(mestre);
			TextoDaMesa(mestre, "nome/Segundo Golpe");
			SalvarTecnica(mestre);
			TecnicaCustomizada? golpe = mestre.Customizadas.Find(x => x.Criada && x.Nome == "Golpe do Mestre");
			TecnicaCustomizada? segundo = mestre.Customizadas.Find(x => x.Criada && x.Nome == "Segundo Golpe");
			AfirmarV21("(palco) o mestre inventou duas tecnicas pela mesa de producao", golpe != null && segundo != null,
					   string.Join(", ", mestre.Customizadas.Select(x => $"{x.Id}:{x.Nome}")));
			if (golpe == null || segundo == null) return;

			aluno.Pos = c + new Vec2(t, 0);
			estranho.Pos = longe;
			mestre.RecargaDaLicao = 0;
			aluno.PedidoDeLicao = null;
			falas = Ouvir(() => Verbo(mestre, "alunos_tecnica", $"{aluno.Assinatura}|{golpe.Id}"));
			AfirmarV21("**A TECNICA SO PASSA PRA QUEM JA ABRIU O TIPO DELA**: o aluno que nao sabe a generica ouve qual falta, e nada e oferecido",
					   aluno.PedidoDeLicao == null && Disse(falas, TecnicaCustomizada.NomeDaQueAbre(golpe.Tipo)), Ultimos(falas));

			AbrirAMesaDeBancada(aluno);
			falas = Ouvir(() => Verbo(mestre, "alunos_tecnica", $"{aluno.Assinatura}|{golpe.Id}"));
			PedidosPraTela(aluno, lista);
			AfirmarV21("**O MESTRE OFERECE A TECNICA QUE INVENTOU**: o pedido chega ao aluno, e diz o nome dela",
					   aluno.PedidoDeLicao is { } daTecnica && daTecnica.Tecnica == golpe.Id && lista.Count == 1 && lista[0].Fato == Fatos.LicaoOferecida
					   && lista[0].Texto.Contains("Golpe do Mestre"), $"{string.Join(" | ", lista.Select(p => p.Texto))} // {Ultimos(falas)}");
			antes = aluno.Livro.MarcosLivres;
			ResponderPelaChaveV21(aluno, PedidosPendentes.Respostas(Fatos.LicaoOferecida).Sim);
			TecnicaCustomizada? copia = aluno.Customizadas.Find(x => x.Nome == "Golpe do Mestre");
			AfirmarV21("...aceita, o aluno ganha uma COPIA pronta e marcada como ensinada -- o mesmo tipo, os mesmos numeros e a mesma arte, no primeiro slot livre dele, sem gastar marco",
					   copia is { Criada: true, Ensinada: true, Id: 1 } && copia.Tipo == golpe.Tipo && copia.BaseDano == golpe.BaseDano && copia.CustoKi == golpe.CustoKi
					   && copia.Velocidade == golpe.Velocidade && copia.Alcance == golpe.Alcance && copia.Arte == golpe.Arte && copia.Gasto == golpe.Gasto
					   && !ReferenceEquals(copia, golpe) && !golpe.Ensinada && aluno.Livro.MarcosLivres == antes && mestre.Customizadas.Contains(golpe),
					   copia == null ? "nao chegou" : $"id {copia.Id}, criada {copia.Criada}, ensinada {copia.Ensinada}");
			if (copia == null) return;

			mestre.RecargaDaLicao = 0;
			falas = Ouvir(() => Verbo(mestre, "alunos_tecnica", $"{aluno.Assinatura}|{golpe.Id}"));
			AfirmarV21("a mesma tecnica nao se ensina duas vezes ao mesmo aluno", aluno.PedidoDeLicao == null && Disse(falas, "ja aprendeu"), Ultimos(falas));

			// O RASCUNHO ABERTO NA MESA DO ALUNO GUARDA O ID DELE: a copia que chega pula esse id.
			CriarTecnica(aluno);
			int idDoRascunho = aluno.Mesa?.Id ?? -1;
			mestre.RecargaDaLicao = 0;
			Verbo(mestre, "alunos_tecnica", $"{aluno.Assinatura}|{segundo.Id}");
			UsarHabilidade(aluno, "ens_licao_sim");
			TecnicaCustomizada? segundaCopia = aluno.Customizadas.Find(x => x.Nome == "Segundo Golpe");
			TextoDaMesa(aluno, "nome/Minha Propria");
			SalvarTecnica(aluno);
			TecnicaCustomizada? propria = aluno.Customizadas.Find(x => x.Nome == "Minha Propria");
			AfirmarV21("a copia que chega NAO toma o id do rascunho que o aluno tinha aberto na mesa: confirmado o rascunho, as tres tecnicas existem (as duas ensinadas e a dele)",
					   idDoRascunho == 2 && segundaCopia is { Ensinada: true, Id: 3 } && propria is { Ensinada: false, Id: 2 }
					   && aluno.Customizadas.Count == 3 && aluno.Customizadas.Exists(x => x.Nome == "Golpe do Mestre"),
					   $"rascunho {idDoRascunho}; {string.Join(", ", aluno.Customizadas.Select(x => $"{x.Id}:{x.Nome}{(x.Ensinada ? "*" : "")}"))}");

			// QUEM RECEBEU NAO REPASSA; a que ELE inventou, sim -- a um aluno dele.
			Vincular(aluno, estranho);
			AbrirAMesaDeBancada(estranho);
			estranho.Pos = aluno.Pos + new Vec2(t, 0);
			aluno.RecargaDaLicao = 0;
			falas = Ouvir(() => Verbo(aluno, "alunos_tecnica", $"{estranho.Assinatura}|{copia.Id}"));
			bool naoRepassa = estranho.PedidoDeLicao == null && Disse(falas, "ENSINADA");
			aluno.RecargaDaLicao = 0;
			Verbo(aluno, "alunos_tecnica", $"{estranho.Assinatura}|{propria?.Id ?? -1}");
			AfirmarV21("**QUEM RECEBEU A TECNICA NAO A REPASSA** (a recusa diz que ela foi ensinada) -- e a que ele mesmo inventou, ele ensina ao aluno dele",
					   naoRepassa && estranho.PedidoDeLicao is { } doAluno && doAluno.Tecnica == (propria?.Id ?? -1), Ultimos(falas));
			estranho.PedidoDeLicao = null;

			// SO AO PROPRIO ALUNO, e com vaga.
			mestre.RecargaDaLicao = 0;
			estranho.Pos = c + new Vec2(0, t);
			falas = Ouvir(() => Verbo(mestre, "alunos_tecnica", $"{estranho.Assinatura}|{golpe.Id}"));
			bool soAoAluno = estranho.PedidoDeLicao == null && Disse(falas, "aluno seu");
			aluno.Customizadas.Remove(copia);
			while (aluno.Customizadas.Count < TecnicaCustomizada.Maximo)
				aluno.Customizadas.Add(new TecnicaCustomizada { Id = 20 + aluno.Customizadas.Count, Criada = true, Nome = "Enchimento" });
			aluno.Pos = c + new Vec2(t, 0);
			aluno.PedidoDeLicao = null;
			mestre.RecargaDaLicao = 0;
			falas = Ouvir(() => Verbo(mestre, "alunos_tecnica", $"{aluno.Assinatura}|{golpe.Id}"));
			AfirmarV21($"a tecnica so se ensina a um ALUNO SEU, e so cabe se ele tiver vaga entre as {TecnicaCustomizada.Maximo}",
					   soAoAluno && aluno.PedidoDeLicao == null && Disse(falas, "cabem"), $"so ao aluno {soAoAluno}; {Ultimos(falas)}");

			// A MARCA VIAJA NO FIO E VAI PRO DISCO.
			var fio = new LiteNetLib.Utils.NetDataWriter();
			CustomWire.Escrever(fio, copia);
			CustomWire.Escrever(fio, golpe);
			var leitor = new LiteNetLib.Utils.NetDataReader(fio.Data, 0, fio.Length);
			TecnicaCustomizada copiaLida = CustomWire.Ler(leitor), golpeLido = CustomWire.Ler(leitor);
			var comCampos = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
			TecnicaCustomizada? doDisco = System.Text.Json.JsonSerializer.Deserialize<TecnicaCustomizada>(System.Text.Json.JsonSerializer.Serialize(copia, comCampos), comCampos);
			AfirmarV21("a marca de ensinada viaja no fio (a tela a mostra, e o cartao do aluno nao a oferece pra repassar) e atravessa o save, com a tecnica",
					   copiaLida.Ensinada && copiaLida.Criada && copiaLida.Nome == copia.Nome && !golpeLido.Ensinada && golpeLido.Criada
					   && doDisco is { Ensinada: true, Criada: true } && doDisco.Nome == copia.Nome,
					   $"fio: copia {copiaLida.Ensinada}, original {golpeLido.Ensinada}; disco {doDisco?.Ensinada}");
		}
		finally
		{
			EnsinoDeSkill.MestreSoEnsinaOQueSeEnsinaDeTeste = false;
			Desvincular(aluno.Assinatura, "fim da bancada");
			Desvincular(estranho.Assinatura, "fim da bancada");
			DesfazerV2(mestre, aluno, estranho);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// N) AS SKILLS: quem acende, o que saiu, e o que o jogo diz na compra
	// =====================================================================
	private void AsSkillsV21()
	{
		GD.Print("[v021] -- N) AS SKILLS: nenhuma folha da loja sem acendedor nem muda; as maestrias legadas sairam; o aviso de compra diz a verdade (item 31)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		AfirmarV21("(palco) o catalogo de skills e as regras de nivel carregaram", _skills != null && RegrasDeNivel.Total > 0);
		if (_skills == null) return;
		Vec2 c = TerraFirmeG12(6);

		// ---- N1) A REGUA: o censo do catalogo de producao
		CensoDeSkills.Relatorio censo = CensoDeSkills.Levantar(_skills, RegrasDeNivel.VerbosDeDegrau, RegrasDeNivel.DestravadasPorDegrau);
		AfirmarV21("**NENHUMA FOLHA QUE PENDE DE ARVORE FICA SEM ACENDEDOR**: toda skill que a loja mostra tem quem a abra (as que sobram na conta nao pendem de arvore nenhuma)",
				   censo.SemAcendedorEmArvore.Count == 0 && censo.SemAcendedor.Count > 0,
				   $"na loja: {string.Join(", ", censo.SemAcendedorEmArvore)} | total {censo.SemAcendedor.Count}");
		AfirmarV21("**...E NENHUMA E MUDA**: toda skill que a loja mostra da alguma coisa -- na compra, por nivel, ou com o efeito escrito a mao no servidor",
				   censo.MudasEmArvore.Count == 0 && censo.EscritasAMao == EfeitosEscritosAMao.Caminhos.Count(),
				   $"mudas na loja: {string.Join(", ", censo.MudasEmArvore)} | escritas a mao {censo.EscritasAMao}");

		string skillsJson = Godot.FileAccess.GetFileAsString("res://Assets/Data/skills.json"), arvoresJson = Godot.FileAccess.GetFileAsString("res://Assets/Data/skilltrees.json");
		AcendedoresDoPort.SemCorrecaoDeTeste = true;
		try
		{
			CensoDeSkills.Relatorio cru = CensoDeSkills.Levantar(SkillCatalog.Parse(skillsJson, arvoresJson), RegrasDeNivel.VerbosDeDegrau, RegrasDeNivel.DestravadasPorDegrau);
			string[] esperadas = ["Basic Buff Mastery", "Basic Debuff Mastery", "Basic Defense Mastery", "Basic Ki Effusion", "Brain Power", "Make Dragon Statue", "Rapid Movement"];
			AfirmarV21("(defeito injetado: o catalogo sem as correcoes do port) as sete folhas voltam a aparecer na loja sem acendedor -- a v0.2.0",
					   esperadas.All(cru.SemAcendedorEmArvore.Contains) && cru.SemAcendedorEmArvore.Count == esperadas.Length, string.Join(", ", cru.SemAcendedorEmArvore));
		}
		finally { AcendedoresDoPort.SemCorrecaoDeTeste = false; }

		EfeitosEscritosAMao.TabelaVaziaDeTeste = true;
		try
		{
			CensoDeSkills.Relatorio semTabela = CensoDeSkills.Levantar(_skills, RegrasDeNivel.VerbosDeDegrau, RegrasDeNivel.DestravadasPorDegrau);
			AfirmarV21("(defeito injetado: a tabela dos efeitos escritos a mao vazia) a Heran Power, o Sol, a Lua e a Precognicao voltam a ser mudas na loja -- a v0.2.0",
					   new[] { "Heran Power", "Sun", "Moon", "Precognition" }.All(semTabela.MudasEmArvore.Contains), string.Join(", ", semTabela.MudasEmArvore));
		}
		finally { EfeitosEscritosAMao.TabelaVaziaDeTeste = false; }

		// A TABELA NAO SOBREVIVE AO EFEITO: cada caminho dela continua citado no codigo de producao do servidor.
		var orfas = new List<string>();
		foreach (string path in EfeitosEscritosAMao.Caminhos)
		{
			bool citado = false;
			foreach (string f in new[] { "Server/GameServer.Tecnicas.G11.cs", "Server/GameServer.Tecnicas.G12.cs" })
				foreach (string linha in Fonte(f))
					if (!linha.TrimStart().StartsWith("//", StringComparison.Ordinal) && linha.Contains($"\"{path}\"", StringComparison.Ordinal)) citado = true;
			if (!citado) orfas.Add(path);
		}
		AfirmarV21("cada skill da tabela de efeitos escritos a mao continua citada no codigo de producao que a roda (a frase nao sobrevive ao efeito)",
				   orfas.Count == 0 && EfeitosEscritosAMao.Caminhos.Any(), string.Join(", ", orfas));

		// ---- N2) CADA ACENDEDOR, PELO LIVRO: a loja de um corpo de verdade, com o contexto de producao
		Recusa Loja(ServerPlayer p, string path)
		{
			AplicarEfeitos(p);
			return p.Livro.Avaliar(_skills, path, p.Race, p.Class, vilao: false).Motivo;
		}
		static bool Aberta(Recusa m) => m is Recusa.Pode or Recusa.SemMarcos;
		ServerPlayer Corpo(string nome, string raca, string classe)
		{
			ServerPlayer p = ForjarV2(nome, c, 10_000, raca);
			p.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			p.Class = p.Ficha.Class = classe;
			return p;
		}

		ServerPlayer humano = Corpo("Humano", "Human", "Normal"), hermano = Corpo("Hermano", "Gray", "Hermano"), gray = Corpo("Gray", "Gray", "Gray");
		ServerPlayer escultor = Corpo("Escultor", "Namekian", "Dragon clan"), guerreiro = Corpo("Guerreiro", "Namekian", "Warrior clan");
		string tronoDeAntes = _tronos.GetValueOrDefault("guardian", "");
		// (a zona da bancada e a Terra: o Guardiao que aprendeu a skill ERGUE a estatua de verdade -- os sets de antes voltam no fim)
		List<SetDeEsferas> setsDeAntes = [.. _sets];
		try
		{
			// A ENTRADA DA EFFUSIVE MASTERY
			Recusa semArvore = Loja(humano, AcendedoresDoPort.BasicKiEffusion);
			humano.Ficha.kieffusionskill = 1;
			Recusa comArvore = Loja(humano, AcendedoresDoPort.BasicKiEffusion);
			AfirmarV21("**BASIC KI EFFUSION ACENDE QUANDO A ARVORE DELA ABRE** (com `kieffusionskill` 1, que o nivel 35 do Ki Unlocked da): antes disso a arvore nem existe pro corpo",
					   semArvore == Recusa.SemArvore && Aberta(comArvore), $"sem a arvore {semArvore}, com ela {comArvore}");

			// AS TRES DE ENTRADA DA KI BUFF MASTERY
			Recusa[] fechadas = [.. AcendedoresDoPort.BasicasDeBuff.Select(p => Loja(humano, p))];
			humano.Ficha.kibuffskill = 1;
			Recusa[] abertas = [.. AcendedoresDoPort.BasicasDeBuff.Select(p => Loja(humano, p))];
			AfirmarV21("**AS TRES BASICAS DA KI BUFF MASTERY ACENDEM COM A ARVORE DELAS** (Buff, Debuff e Defense): o galho que o DM pendurou na arvore errada voltou pro lugar",
					   fechadas.All(m => m == Recusa.SemArvore) && abertas.All(Aberta), $"antes [{string.Join(",", fechadas)}], depois [{string.Join(",", abertas)}]");

			// RAPID MOVEMENT
			Recusa semTreino = Loja(humano, AcendedoresDoPort.RapidMovement);
			humano.Livro.Dar(AcendedoresDoPort.SpeedDrills);
			Recusa comTreino = Loja(humano, AcendedoresDoPort.RapidMovement);
			AfirmarV21("**RAPID MOVEMENT PEDE O SPEED DRILLS**: sem ele a loja diz que falta o pre-requisito, com ele ela abre",
					   semTreino == Recusa.FaltaPreRequisito && Aberta(comTreino), $"sem {semTreino}, com {comTreino}");

			// O GRAY
			Recusa doGray = Loja(gray, AcendedoresDoPort.BrainPower), doHermano = Loja(hermano, AcendedoresDoPort.BrainPower);
			string quando = gray.Livro.Avaliar(_skills, AcendedoresDoPort.BrainPower, gray.Race, gray.Class, vilao: false).Acendedor;
			AfirmarV21("**BRAIN POWER E DA CLASSE HERMANO** (`gray.dm:17-18`): o Hermano a compra, e o Gray comum le que ela abre com a classe",
					   doGray == Recusa.AguardaAcendedor && quando.Contains("Hermano", StringComparison.Ordinal) && Aberta(doHermano), $"gray {doGray} ('{quando}'), hermano {doHermano}");

			gray.Livro.Dar(AcendedoresDoPort.MeditatePower);
			gray.Niveis.Por(AcendedoresDoPort.MeditatePower, 2);
			Recusa noDois = Loja(gray, AcendedoresDoPort.FullPower);
			gray.Niveis.Por(AcendedoresDoPort.MeditatePower, 3);
			Recusa noTres = Loja(gray, AcendedoresDoPort.FullPower);
			AfirmarV21("**FULL POWER ACENDE NO NIVEL 3 DA MEDITATE POWER** (`gray.dm:13-16`): no nivel 2 ainda espera, no 3 abre",
					   noDois == Recusa.AguardaAcendedor && Aberta(noTres), $"nivel 2 {noDois}, nivel 3 {noTres}");

			// A ESTATUA DO DRAGAO
			Recusa semTerritorio = Loja(escultor, AcendedoresDoPort.EstatuaDoDragao);
			string oQueFalta = Jandirus.Core.Skills.NomesLegiveis.Condicao(escultor.Livro.Avaliar(_skills, AcendedoresDoPort.EstatuaDoDragao, escultor.Race, escultor.Class, vilao: false).Acendedor);
			_tronos["guardian"] = escultor.Conta;
			Recusa comTerritorio = Loja(escultor, AcendedoresDoPort.EstatuaDoDragao);
			Recusa doGuerreiro = Loja(guerreiro, AcendedoresDoPort.EstatuaDoDragao);
			_tronos["guardian"] = guerreiro.Conta;
			Recusa doGuerreiroNoTrono = Loja(guerreiro, AcendedoresDoPort.EstatuaDoDragao);
			_tronos["guardian"] = escultor.Conta;
			AfirmarV21("**MAKE DRAGON STATUE ACENDE PRA QUEM TEM ONDE ERGUE-LA** (`namekian.dm:24`): o Cla do Dragao sem territorio le o que falta, o Guardiao da Terra a compra, e o Namekuseijin guerreiro nunca -- nem sentado no trono",
					   semTerritorio == Recusa.AguardaAcendedor && oQueFalta.Contains("Guardião da Terra", StringComparison.Ordinal) && Aberta(comTerritorio)
					   && !Aberta(doGuerreiro) && !Aberta(doGuerreiroNoTrono),
					   $"sem territorio {semTerritorio} ('{oQueFalta}'), guardiao {comTerritorio}, guerreiro {doGuerreiro} / no trono {doGuerreiroNoTrono}");

			// ...E O VERB E O QUE A SKILL DA
			var posse = new PosseEscrita { Raca = "Namekian", Classe = "Dragon clan", Lugar = Jandirus.Core.Verbos.Lugar.Planeta };
			bool semASkill = RequisitosDosVerbos.DoVerbo("db_estatua").Atende(posse);
			posse.Skills.Add(AcendedoresDoPort.EstatuaDoDragao);
			bool comASkill = RequisitosDosVerbos.DoVerbo("db_estatua").Atende(posse);
			List<string> falas = Ouvir(() => Verbo(escultor, "db_estatua", ""));
			bool recusouSemSkill = Disse(falas, "ainda não aprendeu a moldar");
			escultor.Livro.Dar(AcendedoresDoPort.EstatuaDoDragao);
			List<string> depois = Ouvir(() => Verbo(escultor, "db_estatua", ""));
			AfirmarV21("**A ESTATUA PEDE A SKILL**: o botao so existe pra quem aprendeu Make Dragon Statue, e o servidor recusa o gesto de quem nao aprendeu dizendo onde ela esta",
					   !semASkill && comASkill && recusouSemSkill && !Disse(depois, "ainda não aprendeu a moldar"),
					   $"botao sem a skill {semASkill}, com {comASkill}; sem: {Ultimos(falas)} // com: {Ultimos(depois)}");
			_sets.Clear();
			_sets.AddRange(setsDeAntes);

			EstatuaSemSkillDeTeste = true;
			try
			{
				List<string> deAntes = Ouvir(() => Verbo(guerreiro, "db_estatua", ""));
				escultor.Livro.Esquecer(AcendedoresDoPort.EstatuaDoDragao);
				List<string> semPedir = Ouvir(() => Verbo(escultor, "db_estatua", ""));
				AfirmarV21("(defeito injetado: a estatua sem a skill) quem nao aprendeu nao ouve mais que falta a skill -- a v0.2.0, em que ela era uma compra que nao dava nada",
						   !Disse(semPedir, "ainda não aprendeu a moldar") && Disse(deAntes, "Clã do Dragão"), $"{Ultimos(semPedir)} // {Ultimos(deAntes)}");
			}
			finally { EstatuaSemSkillDeTeste = false; }
		}
		finally
		{
			_sets.Clear();
			_sets.AddRange(setsDeAntes);
			if (tronoDeAntes.Length > 0) _tronos["guardian"] = tronoDeAntes; else _tronos.Remove("guardian");
			DesfazerV2(humano, hermano, gray, escultor, guerreiro);
			LimparTudoDaBancada();
		}

		// ---- N3) AS CINCO MAESTRIAS LEGADAS SAIRAM
		string[] legadas = ["/datum/skill/forms/ssj", "/datum/skill/forms/mssj", "/datum/skill/forms/lssjm", "/datum/skill/heran/MstedMXPWR", "/datum/skill/heran/MstedTMXPWR"];
		AfirmarV21("**AS CINCO MAESTRIAS DE FORMA LEGADAS SAIRAM DO JOGO**: nao estao no catalogo, em arvore nenhuma, nem nas regras de nivel -- e a Direct Transformation, vizinha de nome, ficou",
				   legadas.All(p => SkillsAposentadas.Saiu(p) && _skills.Get(p) == null && RegrasDeNivel.Get(p) == null
									&& !_skills.Arvores.Any(a => a.Galhos.Contains(p, StringComparer.OrdinalIgnoreCase)))
				   && _skills.Get("/datum/skill/forms/ssj/DirectSSJ") != null,
				   string.Join(", ", legadas.Where(p => _skills.Get(p) != null || RegrasDeNivel.Get(p) != null)));
		var livro = new Jandirus.Core.Skills.SkillBook();
		livro.Carregar(["/datum/skill/heran/MstedMXPWR"]);
		livro.CarregarCompradas(null);
		int marcosAntes = livro.MarcosLivres;
		var limpas = SkillsAposentadas.Limpar(livro);
		AfirmarV21("...e quem tinha comprado uma delas a perde no login com os marcos de volta (a Mastered Max Power custava 2)",
				   limpas.Count == 1 && limpas[0].Devolvidos == 2 && !livro.Sabe("/datum/skill/heran/MstedMXPWR") && livro.MarcosLivres == marcosAntes + 2,
				   $"{limpas.Count} limpa(s), marcos {marcosAntes} -> {livro.MarcosLivres}");

		// ---- N4) O AVISO DE COMPRA E A FICHA
		Skill? gathering = _skills.Get("/datum/skill/mind/Basic_Ki_Gathering"), heran = _skills.Get("/datum/skill/heran/Heran_Power");
		AfirmarV21("(palco) a Basic Ki Gathering e a Heran Power estao no catalogo", gathering != null && heran != null);
		if (gathering == null || heran == null) return;
		string daGathering = EfeitoEmTexto(gathering), daHeran = EfeitoEmTexto(heran);
		AfirmarV21("**O AVISO DE COMPRA NAO CHAMA MAIS DE \"SEM EFEITO\" A SKILL QUE RENDE POR NIVEL**: a Basic Ki Gathering (a frase que o dono leu) diz que ela rende por nivel e manda ver a ficha",
				   daGathering.Contains("rende por NIVEL", StringComparison.Ordinal) && !daGathering.Contains("sem efeito", StringComparison.Ordinal), daGathering);
		AfirmarV21("...nem a que o servidor escreveu a mao: a Heran Power diz o que faz (os surtos de poder em luta)",
				   daHeran.Contains("surtos", StringComparison.Ordinal) && !daHeran.Contains("sem efeito", StringComparison.Ordinal), daHeran);

		// A LOJA DE VERDADE: o que pende de uma arvore que algum corpo TEM e nao esta fechado. As skills de kit de cargo
		// pendem de arvores que ninguem recebe, e as fechadas (`SkillsFechadas`) nao se vendem -- nenhuma das duas
		// chega a este aviso por compra, e desde o lote G14 o aviso delas diz a verdade ("sem efeito mecanico ainda")
		// em vez de anunciar um verb que nao existe.
		HashSet<string> lojas = ArvoresComPortaV21();
		List<string> aindaSemEfeito = [.. _skills.Todas.Where(s => !s.Arvore && SkillsFechadas.De(s.Path) == null
																  && _skills.Arvores.Any(a => lojas.Contains(a.Path) && a.Galhos.Contains(s.Path, StringComparer.OrdinalIgnoreCase))
																  && EfeitoEmTexto(s).Contains("sem efeito mecanico", StringComparison.Ordinal)).Select(s => s.Nome)];
		AfirmarV21("**NENHUMA SKILL DA LOJA E ANUNCIADA COMO \"SEM EFEITO MECANICO\"** ao ser comprada",
				   aindaSemEfeito.Count == 0, string.Join(", ", aindaSemEfeito));
		Skill? kame = _skills.Todas.FirstOrDefault(s => !s.Arvore && s.Estilo.Length > 0);
		AfirmarV21("...e a skill que so da um ESTILO DE LUTA diz qual (eram oito anunciadas como \"sem efeito\": Martial Arts, Kame Style...)",
				   kame != null && EfeitoEmTexto(kame).Contains("estilo de luta", StringComparison.Ordinal) && EfeitoEmTexto(kame).Contains(kame.Estilo, StringComparison.Ordinal),
				   kame == null ? "nenhuma skill de estilo no catalogo" : EfeitoEmTexto(kame));

		Jandirus.Core.Skills.FichaDeSkill.Texto ficha = Jandirus.Core.Skills.FichaDeSkill.Montar(_skills, heran, RegrasDeNivel.Get(heran.Path));
		AfirmarV21("a FICHA da Heran Power diz o mesmo que o aviso (ela nao e mais \"efeito ainda nao portado\")",
				   ficha.TemEfeito && ficha.NaCompra.Any(l => l.Contains("surtos", StringComparison.Ordinal)), string.Join(" | ", ficha.NaCompra));

		AvisoSoOlhaACompraDeTeste = true;
		try
		{
			AfirmarV21("(defeito injetado: o aviso so olha a compra) a Basic Ki Gathering volta a ser anunciada como \"(sem efeito mecanico ainda)\" -- a frase da v0.2.0",
					   EfeitoEmTexto(gathering).Contains("sem efeito mecanico ainda", StringComparison.Ordinal), EfeitoEmTexto(gathering));
		}
		finally { AvisoSoOlhaACompraDeTeste = false; }
		EfeitosEscritosAMao.TabelaVaziaDeTeste = true;
		try
		{
			AfirmarV21("(defeito injetado: a tabela vazia) a ficha e o aviso da Heran Power voltam a dizer que ela nao faz nada",
					   !Jandirus.Core.Skills.FichaDeSkill.Montar(_skills, heran, RegrasDeNivel.Get(heran.Path)).TemEfeito
					   && EfeitoEmTexto(heran).Contains("sem efeito mecanico ainda", StringComparison.Ordinal));
		}
		finally { EfeitosEscritosAMao.TabelaVaziaDeTeste = false; }
	}

	/// <summary>
	/// AS ARVORES QUE ALGUM CORPO TEM: as de todos, as de raca, as de classe, e as que outra arvore abre (a regra
	/// `arvore;...`). O resto do catalogo -- as de cargo, as de forma legadas -- pende de arvores que ninguem recebe:
	/// nao e loja.
	/// </summary>
	private HashSet<string> ArvoresComPortaV21()
	{
		var comPorta = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (_skills == null) return comPorta;
		comPorta.UnionWith(_skills.ArvoresDeTodos);
		foreach (List<string> l in _skills.ArvorePorRaca.Values) comPorta.UnionWith(l);
		foreach (List<string> l in _skills.ArvorePorClasse.Values) comPorta.UnionWith(l);
		foreach (Skill a in _skills.Arvores)
			foreach (string regra in a.Regras)
				if (regra.Split(';') is ["arvore", { } alvo, ..]) comPorta.Add(alvo);
		return comPorta;
	}

	// =====================================================================
	// G) OS VERBS QUE A LOJA PROMETIA: tres ganham corpo, o resto sai da vitrine
	// =====================================================================
	private void OsVerbsMortosV21()
	{
		GD.Print("[v021] -- G) OS VERBS MORTOS: tres ganham corpo, o que nao tem corpo nao se anuncia, e a skill que so prometia um deles fecha com reembolso (item 9)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		AfirmarV21("(palco) o catalogo de skills e as regras de nivel carregaram", _skills != null && RegrasDeNivel.Total > 0);
		if (_skills == null) return;
		float tile = ZoneCollision.TileSize;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		var corpos = new List<ServerPlayer>();
		ServerPlayer Corpo(string nome, Vec2 onde, string raca = "Human")
		{
			ServerPlayer p = ForjarV2(nome, onde, 10_000, raca);
			p.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			p.Class = p.Ficha.Class;
			corpos.Add(p);
			return p;
		}
		// O efetor de producao anda de 0,2 em 0,2 s: o relogio da bancada anda igual, e o tique do lote e o de producao.
		void Passar(int ms)
		{
			for (int t = 0; t < ms; t += 200)
			{
				AdiantoDoRelogioDeTeste += 200;
				TickG14();
			}
		}

		try
		{
			// ---- G1) A REGUA: o que a loja vende
			HashSet<string> comPorta = ArvoresComPortaV21();
			bool NaLoja(Skill s) => _skills.Arvores.Any(a => comPorta.Contains(a.Path) && a.Galhos.Contains(s.Path, StringComparer.OrdinalIgnoreCase));
			static bool DegrauVivo(Jandirus.Core.Skills.Degrau d) =>
				d.Buffs.Count > 0 || d.Mults.Count > 0 || d.Genes.Count > 0 || d.Flags.Count > 0 || d.VerbosPorCasa.Length > 0
				|| d.Destrava.Length > 0 || d.Concede.Length > 0 || d.Verbos.Any(Tecnicas.TemCorpo);
			// (a pergunta e a do censo -- `SistemaQueFalta`, a boca que ja fala com quem recebe um cargo --, mais o que
			// ela nao olha: o degrau de nivel e o efeito escrito a mao)
			bool SoPrometeVerbMorto(Skill s) =>
				CensoDeSkills.SistemaQueFalta(s) != null && EfeitosEscritosAMao.De(s.Path) == null
				&& !(RegrasDeNivel.Get(s.Path) is { } regraDeNivel && Array.Exists(regraDeNivel.Degraus, DegrauVivo));
			List<string> AVenda() => [.. _skills.Todas.Where(s => !s.Arvore && NaLoja(s) && SoPrometeVerbMorto(s) && SkillsFechadas.De(s.Path) == null)
													  .Select(s => s.Nome).Order(StringComparer.Ordinal)];

			List<string> aVenda = AVenda();
			AfirmarV21("**NENHUMA SKILL QUE A LOJA VENDE SO PROMETE UM VERB SEM CORPO**: a que so dava um botao morto ganhou corpo, e atendida por outro canal, ou fechou",
					   aVenda.Count == 0 && comPorta.Count > 20, $"{string.Join(", ", aVenda)} ({comPorta.Count} arvores com porta)");
			SkillsFechadas.TudoAbertoDeTeste = true;
			try
			{
				List<string> abertas = AVenda();
				AfirmarV21("(defeito injetado: nada fechado) a loja volta a vender tres compras que so davam um botao cinza -- Golden Form, Shapeshifter's Cure e Tree Mastery: a v0.2.0",
						   abertas.SequenceEqual(["Golden Form", "Shapeshifter's Cure", "Tree Mastery"]), string.Join(", ", abertas));
			}
			finally { SkillsFechadas.TudoAbertoDeTeste = false; }

			List<string> tortas = [.. SkillsFechadas.Todas.Where(f => _skills.Get(f.Path) is not { } s || s.Nome != f.Nome || SkillCatalog.CustoDe(s) != f.Custo
																		 || !NaLoja(s) || s.Verbos.Length == 0 || s.Verbos.Any(Tecnicas.TemCorpo)
																		 || (RegrasDeNivel.Get(f.Path)?.Degraus.Any(d => d.Verbos.Any(Tecnicas.TemCorpo)) ?? false))
																 .Select(f => $"{f.Nome} ({f.Custo})")];
			AfirmarV21("**CADA SKILL FECHADA ESTA NO CATALOGO COM O NOME E O PRECO DA LISTA, PENDE DE UMA ARVORE E CONTINUA SEM CORPO** (Time Stop, Tree Mastery, Shapeshifter's Cure e Golden Form) -- no dia em que o verb de uma ganhar corpo, a linha dela sai",
					   tortas.Count == 0 && SkillsFechadas.Todas.Length == 4, string.Join(", ", tortas));

			// ---- G2) A LOJA RECUSA, E QUEM TINHA COMPRADO RECEBE DE VOLTA
			const string cura = "/datum/skill/general/PermanentImitation", arvoreDeArma = "/datum/skill/Tree_Mastery";
			Vec2 c = TerraFirmeG12(12);
			ServerPlayer metamorfo = Corpo("Metamorfo", c, "Shapeshifter");
			// (no DM o Shapeshifter e uma CLASSE de Alien, e a arvore racial dele vem pela classe -- `ArvorePorClasse`)
			metamorfo.Class = metamorfo.Ficha.Class = "Shapeshifter";
			AplicarEfeitos(metamorfo);
			metamorfo.Livro.MarcosLivres = 5;
			Recusa daLoja = metamorfo.Livro.Avaliar(_skills, cura, metamorfo.Race, metamorfo.Class, vilao: false).Motivo;
			List<string> recusou = Ouvir(() => Aprender(metamorfo, cura));
			AfirmarV21("**A LOJA NAO VENDE A SKILL FECHADA**: a Shapeshifter's Cure responde `ForaDoPort` ao proprio Shapeshifter, a compra e recusada dizendo o que falta, e nenhum marco sai",
					   daLoja == Recusa.ForaDoPort && !metamorfo.Livro.Sabe(cura) && metamorfo.Livro.MarcosLivres == 5
					   && Disse(recusou, "ainda nao existe neste jogo") && Disse(recusou, "imitação permanente"),
					   $"{daLoja}; marcos {metamorfo.Livro.MarcosLivres}; {Ultimos(recusou)}");

			var doMestre = new Jandirus.Core.Skills.SkillBook();
			doMestre.Dar(cura);
			var licao = EnsinoDeSkill.Avaliar(doMestre, metamorfo.Livro, _skills.Get(cura), true, true, false, 0, false, eMeuAluno: true, doAlunoSozinho: daLoja);
			AfirmarV21("...e nem a LICAO DE MESTRE a entrega (ela cobraria os marcos do aluno por um verb que nao existe)",
					   licao == Jandirus.Core.Skills.RecusaDeLicao.NaoEDoCorpoDele, $"{licao}");

			SkillsFechadas.TudoAbertoDeTeste = true;
			try
			{
				List<string> comprou = Ouvir(() => Aprender(metamorfo, cura));
				AfirmarV21("(defeito injetado: nada fechado) a mesma compra passa: um marco gasto numa skill que o proprio aviso admite que nao faz nada -- a v0.2.0",
						   metamorfo.Livro.Sabe(cura) && metamorfo.Livro.MarcosLivres == 4 && Disse(comprou, "sem efeito mecanico"),
						   $"sabe {metamorfo.Livro.Sabe(cura)}; marcos {metamorfo.Livro.MarcosLivres}; {Ultimos(comprou)}");
			}
			finally { SkillsFechadas.TudoAbertoDeTeste = false; }

			var livro = new Jandirus.Core.Skills.SkillBook();
			livro.Carregar([cura, arvoreDeArma]);
			livro.CarregarCompradas(null);   // (o save de antes da lista de compradas: o que ninguem ensinou conta como comprado)
			int marcosAntes = livro.MarcosLivres;
			var fecharam = SkillsFechadas.Limpar(livro);
			var presente = new Jandirus.Core.Skills.SkillBook();
			presente.Carregar([cura]);
			presente.CarregarCompradas([]);   // (nao comprou nada: a skill chegou de presente)
			int limpasDoPresente = SkillsFechadas.Limpar(presente).Count;
			AfirmarV21("**QUEM TINHA COMPRADO UMA FECHADA A PERDE NO LOGIN COM OS MARCOS DE VOLTA** (a Shapeshifter's Cure 1 e a Tree Mastery 2: o preco do tier) -- e a que chegou de presente fica onde esta",
					   fecharam.Count == 2 && !livro.Sabe(cura) && !livro.Sabe(arvoreDeArma) && livro.MarcosLivres == marcosAntes + 3
					   && limpasDoPresente == 0 && presente.Sabe(cura),
					   $"{fecharam.Count} fechada(s), marcos {marcosAntes} -> {livro.MarcosLivres}; do presente {limpasDoPresente}");

			// ---- G3) A MIRAGEM QUE SE GUARDA (`Afterimage_Toggle`)
			ServerPlayer Piscador(string nome)
			{
				ServerPlayer p = Corpo(nome, TerraFirmeG12(12));
				p.Livro.Dar(PathDoZanzoken);
				p.Niveis.Por(PathDoZanzoken, 1);
				p.Ficha.kiskill = 3;
				p.Ficha.speed = 3;
				p.Ficha.Statify();
				p.Ficha.Ki = p.Ficha.MaxKi;
				return p;
			}
			ServerPlayer pisca = Piscador("Pisca");
			Vec2 casa = pisca.Pos, adiante = casa + new Vec2(3 * tile, 0);
			bool temOBotao = SabeTecnica(pisca, "Afterimage_Toggle") && Tecnicas.TemCorpo("Afterimage_Toggle");
			List<string> guardou = ApertarEOuvir(pisca, "Afterimage_Toggle");
			bool guardada = pisca.Ficha.zanzoDesligado && !UsaAMiragem(pisca);
			double kiDoPisca = pisca.Ficha.Ki;
			List<string> naoPiscou = Ouvir(() => Zanzoken(pisca, adiante));
			bool ficou = Vec2.Distance(pisca.Pos, casa) < 0.5f && Math.Abs(pisca.Ficha.Ki - kiDoPisca) < 1e-9;
			AfirmarV21("**A MIRAGEM SE GUARDA** (`Afterimage_Toggle`, `Ki/misc.dm:91-98`): quem aprendeu a Afterimage Technique tem o botao, e com ela guardada o duplo clique nao leva o corpo nem cobra Ki -- e o servidor diz por que",
					   temOBotao && guardada && ficou && Disse(guardou, "guarda a tecnica") && Disse(naoPiscou, "guardada"),
					   $"botao {temOBotao}, guardada {guardada}, ficou {ficou}; {Ultimos(guardou)} // {Ultimos(naoPiscou)}");

			var comCampos = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
			Fighter? relida = System.Text.Json.JsonSerializer.Deserialize<Fighter>(System.Text.Json.JsonSerializer.Serialize(pisca.Ficha, comCampos), comCampos);
			AfirmarV21("...e guardada ela atravessa o save (a ficha inteira e gravada, como o `haszanzo` do DM)", relida is { zanzoDesligado: true });

			List<string> soltou = ApertarEOuvir(pisca, "Afterimage_Toggle");
			Zanzoken(pisca, adiante);
			AfirmarV21("o mesmo botao a solta, e o corpo volta a piscar",
					   !pisca.Ficha.zanzoDesligado && UsaAMiragem(pisca) && Vec2.Distance(pisca.Pos, adiante) < 0.5f && Disse(soltou, "volta a usar"),
					   $"desligado {pisca.Ficha.zanzoDesligado}; a {Vec2.Distance(pisca.Pos, adiante) / tile:0.#} tiles do destino; {Ultimos(soltou)}");

			MiragemGuardadaAindaValeDeTeste = true;
			try
			{
				ServerPlayer teimoso = Piscador("Teimoso");
				Vec2 la = teimoso.Pos + new Vec2(3 * tile, 0);
				ApertarEOuvir(teimoso, "Afterimage_Toggle");
				Zanzoken(teimoso, la);
				AfirmarV21("(defeito injetado: o botao escreve o bit e ninguem o le) com a Miragem guardada o corpo pisca do mesmo jeito -- o corte escrito e nao aplicado",
						   teimoso.Ficha.zanzoDesligado && Vec2.Distance(teimoso.Pos, la) < 0.5f,
						   $"desligado {teimoso.Ficha.zanzoDesligado}; a {Vec2.Distance(teimoso.Pos, la) / tile:0.#} tiles do destino");
			}
			finally { MiragemGuardadaAindaValeDeTeste = false; }

			// TODO LEITOR DE PRODUCAO PASSA PELO BOTAO: a skill crua so e perguntada onde o botao nao entra -- no proprio
			// `UsaAMiragem`, na frase de quem nao a aprendeu (`Zanzoken`) e na copia do livro pro clone.
			string[] leitoresCrus = ["GameServer.Clone.cs", "GameServer.Tecnicas.G14.cs", "GameServer.Zanzoken.cs"];
			var crus = new List<string>();
			foreach (string arquivo in Godot.DirAccess.GetFilesAt("res://Server"))
			{
				if (!arquivo.EndsWith(".cs", StringComparison.Ordinal) || arquivo.Contains("Teste", StringComparison.Ordinal)) continue;
				foreach (string linha in Fonte("Server/" + arquivo))
					if (!linha.TrimStart().StartsWith("//", StringComparison.Ordinal) && linha.Contains("Sabe(PathDoZanzoken)", StringComparison.Ordinal))
						crus.Add(arquivo);
			}
			crus.Sort(StringComparer.Ordinal);
			AfirmarV21("**TODO LEITOR DE PRODUCAO PASSA PELO BOTAO**: o soco que investe, a esquiva, o embate de Zanzoken, a corrida de Ki e a piscada da IA perguntam `UsaAMiragem` -- a skill crua so e lida nos tres lugares onde o botao nao entra",
					   crus.SequenceEqual(leitoresCrus), string.Join(", ", crus));

			// ---- G4) ZANZOKEN: MIRAGENS (`Zanzoken_Afterimage`)
			ServerPlayer veloz = Corpo("Veloz", TerraFirmeG12(8));
			veloz.Livro.Dar(PathDoZanzoken);
			veloz.Niveis.Por(PathDoZanzoken, 3);
			bool noTres = SabeTecnica(veloz, "Zanzoken_Afterimage");
			List<string> cedo = ApertarEOuvir(veloz, "Zanzoken_Afterimage");
			veloz.Niveis.Por(PathDoZanzoken, 4);
			bool noQuatro = SabeTecnica(veloz, "Zanzoken_Afterimage");
			AfirmarV21("**ZANZOKEN: MIRAGENS E DO NIVEL 4 DA AFTERIMAGE TECHNIQUE** (o degrau do `niveis.json`): no 3 o servidor diz que ele nao sabe, no 4 o verb e dele",
					   !noTres && noQuatro && Disse(cedo, "voce nao sabe") && !_miragensG14.ContainsKey(veloz.Id), $"nivel 3 {noTres}, nivel 4 {noQuatro}; {Ultimos(cedo)}");

			veloz.Ficha.Ki = veloz.Ficha.MaxKi;
			veloz.Ficha.stamina = veloz.Ficha.maxstamina;
			double deflexaoDeAntes = veloz.Combate.Deflexao, kiDeAntes = veloz.Ficha.Ki;
			double preco = veloz.Ficha.Ephysoff * veloz.Ficha.BaseDrain() * 0.4;
			int saltos = _saltosAnunciados;
			List<string> ligou = ApertarEOuvir(veloz, "Zanzoken_Afterimage");
			AfirmarV21("**ZANZOKEN: MIRAGENS** (`Physical Skills.dm:37-54`): +20 de deflexao, `Ephysoff x BaseDrain x 0,4` de Ki, e o vulto da saida",
					   Math.Abs(veloz.Combate.Deflexao - deflexaoDeAntes - 20) < 1e-9 && Math.Abs(kiDeAntes - veloz.Ficha.Ki - preco) < 1e-6 && preco > 0
					   && _saltosAnunciados == saltos + 1 && _miragensG14.ContainsKey(veloz.Id),
					   $"deflexao {deflexaoDeAntes:0.#} -> {veloz.Combate.Deflexao:0.#}; ki -{kiDeAntes - veloz.Ficha.Ki:0.###} (preco {preco:0.###}); {_saltosAnunciados - saltos} vulto(s); {Ultimos(ligou)}");

			ServerPlayer agressor = Corpo("Agressor", veloz.Pos + new Vec2(tile, 0));
			double semMiragem = CombatMath.Pontaria(agressor.Ficha, veloz.Ficha, deflexaoDeAntes, 0);
			double comMiragem = CombatMath.Pontaria(agressor.Ficha, veloz.Ficha, veloz.Combate.Deflexao, 0);
			AfirmarV21("...e a pontaria de quem bate nele cai VINTE pontos (o campo que o `MeleeResolver` le)",
					   Math.Abs(semMiragem - comMiragem - 20) < 1e-9, $"{semMiragem:0.#}% -> {comMiragem:0.#}%");

			Passar(2000);
			int emDoisSegundos = _saltosAnunciados - saltos - 1;
			veloz.Ficha.Ki = veloz.Ficha.MaxKi;
			ApertarEOuvir(veloz, "Zanzoken_Afterimage");   // (a recarga dos punhos, 1,5 s, ja passou)
			long renovouEm = NowMs();
			AfirmarV21("uma miragem a cada meio segundo (quatro em dois segundos), e apertar de novo RENOVA o prazo sem empilhar a deflexao (`max_stacks = 1`)",
					   emDoisSegundos == 4 && Math.Abs(veloz.Combate.Deflexao - deflexaoDeAntes - 20) < 1e-9
					   && _miragensG14.TryGetValue(veloz.Id, out MiragensDePeG14? renovada) && Math.Abs(renovada.Ate - renovouEm - 5000) <= 50,
					   $"{emDoisSegundos} miragem(ns) em 2 s; deflexao {veloz.Combate.Deflexao:0.#}");

			int antesDoFim = _saltosAnunciados;
			Passar(4800);
			bool aindaDePe = _miragensG14.ContainsKey(veloz.Id);
			Passar(600);
			AfirmarV21("cinco segundos depois o efeito ACABA e a deflexao volta ao que era (`Removed`: `deflection -= 20`) -- com as DEZ miragens do prazo (a do ultimo tique nao se perde)",
					   aindaDePe && !_miragensG14.ContainsKey(veloz.Id) && Math.Abs(veloz.Combate.Deflexao - deflexaoDeAntes) < 1e-9 && _saltosAnunciados - antesDoFim == 10,
					   $"de pe aos 4,8 s {aindaDePe}; deflexao {veloz.Combate.Deflexao:0.#} (era {deflexaoDeAntes:0.#}); {_saltosAnunciados - antesDoFim} miragem(ns)");

			AdiantoDoRelogioDeTeste += 2000;
			veloz.Ficha.Ki = veloz.Ficha.MaxKi;
			veloz.Ficha.stamina = 10;
			List<string> cansado = ApertarEOuvir(veloz, "Zanzoken_Afterimage");
			veloz.Ficha.stamina = veloz.Ficha.maxstamina;
			veloz.Ficha.weaponeq = 2;
			List<string> armado = ApertarEOuvir(veloz, "Zanzoken_Afterimage");
			veloz.Ficha.weaponeq = 0;
			AfirmarV21("quem esta sem folego (menos de 15 de vigor) ou sem uma mao livre ouve a recusa, e nada e cobrado nem ligado",
					   Disse(cansado, "cansado demais") && Disse(armado, "mao livre") && !_miragensG14.ContainsKey(veloz.Id)
					   && Math.Abs(veloz.Ficha.Ki - veloz.Ficha.MaxKi) < 1e-9, $"{Ultimos(cansado)} // {Ultimos(armado)}");

			// ---- G5) O CUSPE ACIDO (`Acid_Spit`)
			const string cuspeDoSaiba = "/datum/skill/saiba/Acid_Spit";
			Vec2 c5 = TerraFirmeG12(12);
			ServerPlayer saiba = Corpo("Saiba", c5, "Saibamen");
			saiba.Facing = Facing.East;
			List<string> semSaber = ApertarEOuvir(saiba, "Acid_Spit");
			saiba.Livro.Dar(cuspeDoSaiba);
			ServerPlayer cuspido = Corpo("Cuspido", c5 + new Vec2(4 * tile, 0));
			saiba.Ficha.Ki = saiba.Ficha.MaxKi;
			double kiDoSaiba = saiba.Ficha.Ki, precoDoCuspe = 12 * saiba.Ficha.BaseDrain();
			ApertarEOuvir(saiba, "Acid_Spit");
			Projetil? cuspe = TirosDeG12(saiba).FirstOrDefault();
			// (2026-10-18) o `basedamage` e o do NERF do dono (*"acido do saibaman ta meio op, da uma nerfada"*): 20, e nao os 40 do DM -- e, no
			// segundo nerf (2026-10-22), 5 -- ver `DanoDoCuspeG14`. A regua do nerf mora na `--chefesteste`.
			AfirmarV21($"**O CUSPE ACIDO** (`arlian.dm:42-63`): quem aprendeu a skill solta uma bola de `basedamage` {DanoDoCuspeG14:0.#} (o nerf do dono; o DM: 40) que carrega o veneno, por `12 x BaseDrain` de Ki -- e quem nao aprendeu ouve que nao sabe",
					   Disse(semSaber, "voce nao sabe") && cuspe is { Dom: DomDeProjetil.Veneno, Tipo: TipoDeProjetil.Blast } && Math.Abs(cuspe.BaseDano - DanoDoCuspeG14) < 1e-6
					   && Math.Abs(kiDoSaiba - saiba.Ficha.Ki - precoDoCuspe) < 1e-6 && precoDoCuspe > 0,
					   $"{Ultimos(semSaber)}; bola {(cuspe == null ? "nenhuma" : $"dano base {cuspe.BaseDano:0.#}, dom {cuspe.Dom}")}; ki -{kiDoSaiba - saiba.Ficha.Ki:0.###} (preco {precoDoCuspe:0.###})");

			List<string> outroCuspe = ApertarEOuvir(saiba, "Acid_Spit");
			AfirmarV21("...e um segundo de folego entre um cuspe e outro (o `blasting` do verb, `sleep(10)`)",
					   Disse(outroCuspe, "folego") && TirosDeG12(saiba).Count == 1, Ultimos(outroCuspe));

			double vidaAntes = cuspido.Combate.Corpo.Vida();
			List<string> acertou = Ouvir(() => { for (int i = 0; i < 30 * 5 && cuspe is { Vivo: true }; i++) TickDosProjeteis(Protocol.TickSeconds); });
			AfirmarV21("a bola FERE e ENVENENA quem acerta (`OnSucCollWMob`, `arlian.dm:65-74`): \"voce esta envenenado!\"",
					   cuspe is { Fim: FimDeProjetil.Acertou } && _venenoG14.ContainsKey(cuspido.Id) && Disse(acertou, "envenenado") && cuspido.Combate.Corpo.Vida() < vidaAntes,
					   $"fim {cuspe?.Fim}; envenenado {_venenoG14.ContainsKey(cuspido.Id)}; vida {vidaAntes:0.##} -> {cuspido.Combate.Corpo.Vida():0.##}; {Ultimos(acertou)}");

			// O VENENO EM SI, num corpo inteiro e pela porta que o tiro chama: seis tiques de 2/30, um a cada meio segundo.
			ServerPlayer sadio = Corpo("Sadio", TerraFirmeG12(6));
			EnvenenarG14(saiba, sadio);
			double vidaDoSadio = sadio.Combate.Corpo.Vida(), anterior = vidaDoSadio;
			int tiques = 0;
			for (int t = 0; t < 3400; t += 200)
			{
				Passar(200);
				double agoraTem = sadio.Combate.Corpo.Vida();
				if (agoraTem < anterior - 1e-9) tiques++;
				anterior = agoraTem;
			}
			double perdeu = vidaDoSadio - sadio.Combate.Corpo.Vida();
			AfirmarV21("**O VENENO**: seis tiques em tres segundos (`duration = 30`, `tick_delay = 5`), cada um espalhando 2/30 de dano pelo corpo -- pouco, como no original --, e depois ele ACABA",
					   tiques == 6 && perdeu > 0.05 && perdeu < 0.45 && !_venenoG14.ContainsKey(sadio.Id),
					   $"{tiques} tique(s); perdeu {perdeu:0.###} de vida; ainda envenenado {_venenoG14.ContainsKey(sadio.Id)}");

			// ---- G6) A FUSAO NAMEKUSEIJIN PEDE A SKILL DELA
			Vec2 c6 = TerraFirmeG12(6);
			ServerPlayer pede = Corpo("NamekQuePede", c6, "Namekian"), ouve = Corpo("NamekQueOuve", c6 + new Vec2(tile * 0.9f, 0), "Namekian");
			pede.Facing = Facing.East;
			ouve.Facing = Facing.West;
			pede.AlvoId = ouve.Id;
			List<string> semASkill = Ouvir(() => UsarHabilidade(pede, "fus_namek"));
			bool convidouSem = _pedidosDeFusao.ContainsKey(ouve.Id);
			var posse = new PosseEscrita { Raca = "Namekian", Classe = "Warrior clan", Lugar = Jandirus.Core.Verbos.Lugar.Planeta };
			bool botaoSem = RequisitosDosVerbos.Linha("hab:fus_namek")!.Requisito.Atende(posse);
			posse.Skills.Add(RequisitosDosVerbos.SkillDaFusaoNamek);
			bool botaoCom = RequisitosDosVerbos.Linha("hab:fus_namek")!.Requisito.Atende(posse);
			pede.Livro.Dar(RequisitosDosVerbos.SkillDaFusaoNamek);
			List<string> comASkill = Ouvir(() => UsarHabilidade(pede, "fus_namek"));
			bool convidouCom = _pedidosDeFusao.ContainsKey(ouve.Id);
			AfirmarV21("**A FUSAO NAMEKUSEIJIN PEDE A SKILL DELA** (`Fusion- Namek Style`, `namekian.dm:111-124`): sem ela o botao nao existe e o servidor recusa o gesto dizendo onde ela esta; com ela o convite sai -- a skill deixou de ser uma compra que nao dava nada",
					   !botaoSem && botaoCom && !convidouSem && Disse(semASkill, "Fusion- Namek Style") && convidouCom,
					   $"botao sem {botaoSem}, com {botaoCom}; convite sem {convidouSem}, com {convidouCom}; {Ultimos(semASkill)} // {Ultimos(comASkill)}");
			AfirmarV21("...e o censo sabe que as duas fusoes TEM corpo (eram dadas como \"falta fusao de dois corpos num so\", e viravam botao cinza)",
					   Tecnicas.TemCorpo("Namekian_Fusion") && Tecnicas.TemCorpo("Fusion_dance") && !CensoDeSkills.Esperando.ContainsKey("Namekian_Fusion")
					   && !CensoDeSkills.Esperando.ContainsKey("Fusion_dance"));

			_pedidosDeFusao.Remove(ouve.Id);
			FusaoNamekSemSkillDeTeste = true;
			try
			{
				ServerPlayer cru = Corpo("NamekCru", c6 + new Vec2(0, 0), "Namekian");
				pede.Pos = c6 + new Vec2(4 * tile, 0);   // (sai da frente: o `AlvoNaFrente` do cru tem de achar o `ouve`)
				cru.Facing = Facing.East;
				cru.AlvoId = ouve.Id;
				Ouvir(() => UsarHabilidade(cru, "fus_namek"));
				AfirmarV21("(defeito injetado: a fusao sem a skill) o Namekuseijin que nao aprendeu nada convida do mesmo jeito -- a v0.2.0, em que a Fusion- Namek Style nao servia pra nada",
						   _pedidosDeFusao.ContainsKey(ouve.Id) && !cru.Livro.Sabe(RequisitosDosVerbos.SkillDaFusaoNamek));
			}
			finally { FusaoNamekSemSkillDeTeste = false; }

			// ---- G7) O AVISO DE COMPRA SO ANUNCIA O VERB QUE EXISTE
			static string DepoisDeNovaHabilidade(string aviso)
			{
				int i = aviso.IndexOf("nova habilidade:", StringComparison.Ordinal);
				return i < 0 ? "" : aviso[i..];
			}
			static string NomeDoVerbo(string v) => Tecnicas.Get(v)?.Nome ?? Jandirus.Core.Skills.NomesLegiveis.Habilidade(v);
			List<string> Anunciam() => [.. _skills.Todas.Where(s => !s.Arvore && s.Verbos.Any(v => !Tecnicas.TemCorpo(v)
																							&& DepoisDeNovaHabilidade(EfeitoEmTexto(s)).Contains(NomeDoVerbo(v), StringComparison.Ordinal)))
													 .Select(s => s.Nome).Order(StringComparer.Ordinal)];
			List<string> anunciam = Anunciam();
			Skill? daMiragem = _skills.Get(PathDoZanzoken), palavras = _skills.Todas.FirstOrDefault(s => s.Nome == "Magic Words");
			AfirmarV21("**O AVISO DE COMPRA SO ANUNCIA O VERB QUE EXISTE**: nenhuma skill do catalogo diz \"nova habilidade: X\" de um X sem corpo -- a Afterimage Technique anuncia a Miragem, e a Magic Words (cujo verb espera a magia de palavra) anuncia so o que ela soma",
					   anunciam.Count == 0 && daMiragem != null && EfeitoEmTexto(daMiragem).Contains("Miragem: ligar / desligar", StringComparison.Ordinal)
					   && palavras != null && !EfeitoEmTexto(palavras).Contains("nova habilidade", StringComparison.Ordinal) && !EfeitoEmTexto(palavras).Contains("sem efeito", StringComparison.Ordinal),
					   $"anunciam verb morto: {string.Join(", ", anunciam)} | {(daMiragem == null ? "sem Afterimage" : EfeitoEmTexto(daMiragem))} | {(palavras == null ? "sem Magic Words" : EfeitoEmTexto(palavras))}");
			AvisoAnunciaVerbMortoDeTeste = true;
			try
			{
				List<string> comOMorto = Anunciam();
				AfirmarV21("(defeito injetado: o aviso anuncia todo verb) as skills de verb sem corpo voltam a prometer no chat uma habilidade que nao existe -- a Magic Words entre elas",
						   comOMorto.Contains("Magic Words") && comOMorto.Count >= 5, string.Join(", ", comOMorto));
			}
			finally { AvisoAnunciaVerbMortoDeTeste = false; }
		}
		finally
		{
			SkillsFechadas.TudoAbertoDeTeste = false;
			MiragemGuardadaAindaValeDeTeste = FusaoNamekSemSkillDeTeste = AvisoAnunciaVerbMortoDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			foreach (ServerPlayer p in corpos) _pedidosDeFusao.Remove(p.Id);
			DesfazerV2([.. corpos]);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// H) AS SKILLS QUE CRIAM ITEM: o que a Materialize cria nasce no chao
	// =====================================================================
	private void OQueSeCriaNasceNoChaoV21()
	{
		GD.Print("[v021] -- H) AS SKILLS QUE CRIAM ITEM: a Materialize ganha corpo, e o que ela cria nasce no chao (item 10)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Jandirus.Core.Appearance.VisualCatalog? visual = Jandirus.Core.Items.CatalogoDeItens.Visual;
		AfirmarV21("(palco) o catalogo de skills e o de aparencia carregaram (com roupa e com armadura)",
				   _skills != null && visual is { Roupas.Count: > 1, Armaduras.Count: > 0 });
		if (_skills == null || visual is not { Roupas.Count: > 1, Armaduras.Count: > 0 }) return;
		float tile = ZoneCollision.TileSize;
		const string pesosDoCatalogo = Jandirus.Core.Items.CatalogoDeItens.Pesos, pegar = Jandirus.Core.Items.Chao.VerboPegar;
		List<Jandirus.Core.Items.Largado> chao = LargadosDe(ZonaDaBancadaDeProjetil.Hash);
		List<Jandirus.Core.Items.Largado> chaoDeAntes = [.. chao];
		chao.Clear();
		ServerPlayer? mago = null;
		try
		{
			// ---- H1) A SKILL CONCEDE O VERB
			Skill? skill = _skills.Get(Materializacao.Skill);
			string aviso = skill == null ? "" : EfeitoEmTexto(skill);
			AfirmarV21("**A MATERIALIZE GANHOU CORPO**: a skill concede o verb `Materialize` (no DM ela ensinava um ritual do sistema de magia, que o port nao tem), ele tem corpo, e o aviso de compra o anuncia pelo nome do botao",
					   skill != null && skill.Verbos.Contains(Materializacao.Verbo) && Tecnicas.TemCorpo(Materializacao.Verbo)
					   && aviso.Contains("nova habilidade: Materializar", StringComparison.Ordinal),
					   skill == null ? "a skill nao esta no catalogo" : aviso);

			VerbosDoPort.SemCorrecaoDeTeste = true;
			try
			{
				Skill? crua = SkillCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/skills.json"),
												 Godot.FileAccess.GetFileAsString("res://Assets/Data/skilltrees.json")).Get(Materializacao.Skill);
				AfirmarV21("(defeito injetado: o catalogo sem os verbs do port) a Materialize volta a nao conceder verb nenhum -- so o +1 de pericia esoterica: a v0.2.0",
						   crua is { Verbos.Length: 0 } && crua.Buffs.ContainsKey("magiBuff"), crua == null ? "sem a skill" : string.Join(",", crua.Verbos));
			}
			finally { VerbosDoPort.SemCorrecaoDeTeste = false; }

			// ---- H2) OS PESOS
			mago = ForjarV2("Mago", TerraFirmeG12(8), 10_000, "Namekian");
			mago.Conta = "bancada_v021_mago";
			List<string> semSaber = ApertarEOuvir(mago, $"{Materializacao.Verbo}:{Materializacao.DePesos}");
			bool nadaNasceu = chao.Count == 0;
			double fracaoDeAntes = Materializacao.Custo(mago.Ficha) / mago.Ficha.MaxKi;
			mago.Livro.Dar(Materializacao.Skill);
			AplicarEfeitos(mago);
			mago.Ficha.Ki = mago.Ficha.MaxKi;
			double custo = Materializacao.Custo(mago.Ficha), kiAntes = mago.Ficha.Ki;
			Verbo(mago, Materializacao.VerboDeCanal, Materializacao.DePesos);
			Jandirus.Core.Items.Largado? pesos = chao.Find(l => l.Item == pesosDoCatalogo);
			bool aosPes = pesos != null && MathF.Floor(pesos.X / tile) == MathF.Floor(mago.Pos.X / tile) && MathF.Floor(pesos.Y / tile) == MathF.Floor(mago.Pos.Y / tile);
			AfirmarV21("**MATERIALIZAR PESOS**: quem nao aprendeu a skill ouve que nao sabe e nada nasce; quem aprendeu ve os pesos tomarem forma NO CHAO, aos pes dele -- e nao na mochila --, pelo Ki do ritual",
					   Disse(semSaber, "voce nao sabe") && nadaNasceu && pesos is { Quantidade: 1, Tipo: Jandirus.Core.Items.TipoDeLargado.Item } && aosPes
					   && mago.Mochila.Quantos(pesosDoCatalogo) == 0 && Math.Abs(kiAntes - mago.Ficha.Ki - custo) < 1e-6,
					   $"{Ultimos(semSaber)}; no chao {chao.Count}; aos pes {aosPes}; ki -{kiAntes - mago.Ficha.Ki:0.##} (custo {custo:0.##})");
			AfirmarV21("...e o preco e a fracao que os 1.500 do ritual sao da reserva magica (`MagicCap`), aplicada ao Ki cheio: mais que nada, menos que tudo -- e a pericia esoterica que a propria skill da ja o barateia",
					   Math.Abs(custo - mago.Ficha.MaxKi * Math.Min(1, Materializacao.CustoDoRitual / mago.Ficha.MagicCap)) < 1e-6
					   && custo > 0 && custo < mago.Ficha.MaxKi && custo / mago.Ficha.MaxKi < fracaoDeAntes - 1e-6,
					   $"custo {custo:0.##} de {mago.Ficha.MaxKi:0.##} ({custo / mago.Ficha.MaxKi:P0}; sem a skill seriam {fracaoDeAntes:P0}); reserva magica {mago.Ficha.MagicCap:0.#}");

			Verbo(mago, pegar, "");
			AfirmarV21("...e o que nasceu no chao se pega como qualquer coisa largada", mago.Mochila.Quantos(pesosDoCatalogo) == 1 && chao.Count == 0,
					   $"na mochila {mago.Mochila.Quantos(pesosDoCatalogo)}; no chao {chao.Count}");

			// ---- H3) A ROUPA
			string folha = Jandirus.Core.Appearance.VisualCatalog.NomeDoArquivo(visual.Roupas[0]);
			string outraFolha = Jandirus.Core.Appearance.VisualCatalog.NomeDoArquivo(visual.Roupas[1]);
			string tingida = new Jandirus.Core.Items.RoupaGuardada(folha, new Jandirus.Core.Appearance.Rgb(0x2A, 0x2A, 0x33)).Id;
			string crua2 = new Jandirus.Core.Items.RoupaGuardada(outraFolha, null).Id;
			mago.Ficha.Ki = mago.Ficha.MaxKi;
			Verbo(mago, Materializacao.VerboDeCanal, $"{Materializacao.DeRoupa}/{folha}/2a2a33");
			bool nasceuTingida = chao.Exists(l => l.Item == tingida && l.Quantidade == 1);
			Verbo(mago, pegar, "");
			int vestia = mago.Visual.Roupa.Count;
			List<string> vestindo = Ouvir(() => Verbo(mago, "item_vestir", tingida));
			bool vestiu = mago.Visual.Roupa.Count == vestia + 1 && mago.Visual.Roupa[^1].Caminho == visual.Peca(folha)
						  && mago.Visual.Roupa[^1].Cor is { R: 0x2A, G: 0x2A, B: 0x33 };
			mago.Ficha.Ki = mago.Ficha.MaxKi;
			Verbo(mago, Materializacao.VerboDeCanal, $"{Materializacao.DeRoupa}/{outraFolha}");
			bool nasceuCrua = chao.Exists(l => l.Item == crua2);
			AfirmarV21("**MATERIALIZAR ROUPA**: a folha do guarda-roupa que se escolheu nasce no chao como item, na cor pedida (`Roupa|folha|RRGGBB`) ou na do desenho (sem cor), se pega e se VESTE -- a primeira fonte de roupa nova do jogo",
					   nasceuTingida && vestiu && nasceuCrua,
					   $"tingida no chao {nasceuTingida}; vestiu {vestiu} ({Ultimos(vestindo)}); sem cor no chao {nasceuCrua}");

			// ---- H4) O QUE NAO SE MATERIALIZA
			chao.Clear();
			mago.Ficha.Ki = mago.Ficha.MaxKi;
			string canal = Materializacao.VerboDeCanal;
			string? soDeArmadura = visual.Armaduras.Select(Jandirus.Core.Appearance.VisualCatalog.NomeDoArquivo).FirstOrDefault(n => visual.Peca(n) == null);
			List<string> deArmadura = soDeArmadura == null ? ["nao ha roupa chamada"] : Ouvir(() => Verbo(mago, canal, $"{Materializacao.DeRoupa}/{soDeArmadura}"));
			List<string> inventada = Ouvir(() => Verbo(mago, canal, $"{Materializacao.DeRoupa}/Folha_Que_Nao_Existe"));
			List<string> corTorta = Ouvir(() => Verbo(mago, canal, $"{Materializacao.DeRoupa}/{folha}/azul"));
			List<string> arma = Ouvir(() => Verbo(mago, canal, "arma"));
			List<string> semPedido = ApertarEOuvir(mago, Materializacao.Verbo);
			bool recusasDoPedido = Disse(deArmadura, "nao ha roupa chamada") && Disse(inventada, "nao ha roupa chamada") && Disse(corTorta, "essa cor nao existe")
								   && Disse(arma, "materializar o que") && Disse(semPedido, "materializar o que");
			mago.Ficha.KO = true;
			List<string> caido = Ouvir(() => Verbo(mago, canal, Materializacao.DePesos));
			mago.Ficha.KO = false;
			mago.Ficha.med = true;
			List<string> meditando = Ouvir(() => Verbo(mago, canal, Materializacao.DePesos));
			mago.Ficha.med = false;
			bool intacto = chao.Count == 0 && Math.Abs(mago.Ficha.Ki - mago.Ficha.MaxKi) < 1e-9;
			mago.Ficha.Ki = Materializacao.Custo(mago.Ficha) - 1;
			List<string> semKi = Ouvir(() => Verbo(mago, canal, Materializacao.DePesos));
			AfirmarV21("o que NAO se materializa, e o servidor diz por que: armadura (e de tecnologia), folha que nao existe, cor que nao e cor, arma (o port nao tem), pedido vazio, caido, meditando e sem Ki -- e nada nasce nem sai Ki",
					   recusasDoPedido && Disse(caido, "caido, voce nao materializa") && Disse(meditando, "pare o que esta fazendo") && Disse(semKi, "materializar pede")
					   && intacto && chao.Count == 0,
					   $"armadura '{soDeArmadura}': {Ultimos(deArmadura)} // {Ultimos(inventada)} // {Ultimos(corTorta)} // {Ultimos(arma)} // {Ultimos(semPedido)} // {Ultimos(caido)} // {Ultimos(meditando)} // {Ultimos(semKi)}; no chao {chao.Count}");
		}
		finally
		{
			VerbosDoPort.SemCorrecaoDeTeste = false;
			chao.Clear();
			chao.AddRange(chaoDeAntes);
			if (mago != null) DesfazerV2(mago);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// D) A IA: o caminho em volta da parede, o contorno e a carga de Ki
	// =====================================================================
	private void AIaContornaECarregaV21()
	{
		GD.Print("[v021] -- D) A IA CONTORNA A PAREDE E CARREGA O KI (item 22)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		ZoneCollision mapa = _pjMapa!;
		ZoneKey terra = ZonaDaBancadaDeProjetil;
		const float T = ZoneCollision.TileSize;
		const double dt = Protocol.TickSeconds;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		var corpos = new List<ServerPlayer>();
		var sorte = new Random(2021);   // (o cerebro sorteia: a bancada usa a propria semente, pra o placar nao depender do relogio)
		double restoDoRelogio = 0;

		ServerPlayer Corpo(string nome, Vec2 onde, bool comCerebro)
		{
			ServerPlayer p = ForjarV2(nome, onde, 10_000);
			p.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			if (comCerebro) p.Cerebro = new Cerebro();
			corpos.Add(p);
			return p;
		}
		// UM TIQUE DE UM CORPO DIRIGIDO: as linhas do `TicarUmCorpo` depois de escolhida a presa (capacidades a 1 Hz,
		// percepcao, decisao, atuador) e a carga. O relogio do servidor anda junto -- o desvio se rele a cada 200 ms.
		void Tique(ServerPlayer corpo, ServerPlayer? alvo, Vec2 destino)
		{
			restoDoRelogio += dt * 1000;
			long ms = (long)restoDoRelogio;
			restoDoRelogio -= ms;
			AdiantoDoRelogioDeTeste += ms;
			MontarAsGrades();   // (a grade de corpos e estado derivado, refeita a cada tique pelo `Tick()` de producao)
			Cerebro cerebro = corpo.Cerebro!;
			if (cerebro.PrecisaLerCapacidades(dt)) cerebro.Poderes = corpo.Perfil.Filtrar(LerCapacidades(corpo));
			Percepcao p = LerPercepcao(corpo, alvo, destino, cerebro.Poderes.DeLonge.TemAlguma);
			AplicarComando(corpo, cerebro.Pensar(p, dt, sorte), dt);
			TickDaCarga(corpo, dt);
		}

		try
		{
			// ---- D1) O A*, NUM MAPA DE BANCADA: um muro de cima a baixo com UMA porta
			const int lado = 40, porta = 16;
			var bits = new byte[(lado * lado + 7) / 8];
			void Muro(int x, int y) { int i = y * lado + x; bits[i >> 3] |= (byte)(1 << (i & 7)); }
			for (int y = 0; y < lado; y++) if (y != porta) Muro(20, y);
			ZoneCollision sintetico = ZoneCollision.Montar(lado, lado, bits);
			var rota = new Vec2[Caminho.MaximoDePontos];
			Vec2 daqui = Caminho.Centro(12, 10), ali = Caminho.Centro(26, 10);
			int buscas = Caminho.BuscasDeTeste;
			int n = Caminho.Achar(sintetico, daqui, ali, ModoDeTravessia.APe, rota, out bool chegou);
			bool pelaPorta = false, emChao = true, vizinhos = true, semCortarQuina = true;
			(int X, int Y) anterior = Caminho.Celula(daqui);
			for (int i = 0; i < n; i++)
			{
				(int X, int Y) c = Caminho.Celula(rota[i]);
				pelaPorta |= c == (20, porta);
				emChao &= !sintetico.BlockedCell(c.X, c.Y);
				int dx = c.X - anterior.X, dy = c.Y - anterior.Y;
				vizinhos &= Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1;
				if (dx != 0 && dy != 0) semCortarQuina &= !sintetico.BlockedCell(c.X, anterior.Y) && !sintetico.BlockedCell(anterior.X, c.Y);
				anterior = c;
			}
			AfirmarV21("**O A* ACHA A PORTA**: de um lado ao outro de um muro inteirico com uma abertura so, o caminho passa pela abertura, pisa so em chao, anda de celula vizinha em celula vizinha e termina no destino",
					   n > 14 && chegou && pelaPorta && emChao && vizinhos && n > 0 && Caminho.Celula(rota[n - 1]) == Caminho.Celula(ali)
					   && Caminho.BuscasDeTeste == buscas + 1,
					   $"{n} ponto(s), chegou {chegou}, pela porta {pelaPorta}, em chao {emChao}, vizinhos {vizinhos}");
			AfirmarV21("...e a diagonal NUNCA corta quina (as duas celulas do canto estao livres: a caixa dos pes encostaria nelas, e o passo emperraria num caminho que o A* prometeu)",
					   semCortarQuina && n > 0);

			Muro(20, porta);   // fecha a porta: o destino ficou do outro lado de um muro sem passagem
			int semPorta = Caminho.Achar(ZoneCollision.Montar(lado, lado, bits), daqui, ali, ModoDeTravessia.APe, rota, out bool chegouSemPorta);
			Vec2 colado = Caminho.Centro(19, 10);
			int jaEncostado = Caminho.Achar(ZoneCollision.Montar(lado, lado, bits), colado, ali, ModoDeTravessia.APe, rota, out _);
			AfirmarV21("sem passagem ele NAO inventa uma: o caminho vai ate o ponto alcancavel mais perto do destino (e diz que nao chegou) -- e de quem ja esta encostado no muro, nenhum",
					   semPorta > 0 && !chegouSemPorta && Caminho.Celula(rota[0]).X <= 19 && jaEncostado == 0,
					   $"{semPorta} ponto(s), chegou {chegouSemPorta}; do encostado {jaEncostado}");

			Vec2 naAgua = Caminho.Centro(26, 10);
			AfirmarV21("o destino na MESMA celula nao pede caminho, e o vetor vazio nao recebe nada",
					   Caminho.Achar(sintetico, daqui, daqui + new Vec2(3, 0), ModoDeTravessia.APe, rota, out bool jaLa) == 0 && jaLa
					   && Caminho.Achar(sintetico, daqui, naAgua, ModoDeTravessia.APe, [], out _) == 0);

			// ---- D2) O CORPO DIRIGIDO SAI DE UM BOLSO DE PAREDES (blocos erguidos pela porta de producao)
			int deY = 8, deX = 8;
			bool temPraca = AcharPracaFirmeV21(mapa, 13, ref deY, ref deX, out int cx, out int cy);
			Jandirus.Core.World.BlocoDef? parede = _blocos?.Todos.FirstOrDefault(b => b.Classe == ClasseDeBloco.Parede);
			AfirmarV21("(palco) ha uma praca firme de 13 celulas e um bloco de parede no catalogo", temPraca && parede != null);
			if (!temPraca || parede == null) return;

			ZerarBases();
			ServerPlayer pedreiro = Corpo("Pedreiro", CentroV21(cx, cy), comCerebro: false);
			pedreiro.Ficha.Zeni = 10_000_000;
			// O BOLSO: um "C" aberto pro oeste. O fundo e a coluna `cx` (cinco celulas) e os bracos sao as fileiras
			// `cy-2` e `cy+2`, tres celulas pra oeste. Quem esta DENTRO so sai andando pra LONGE do alvo.
			var doBolso = new List<(int X, int Y)>();
			for (int y = cy - 2; y <= cy + 2; y++) doBolso.Add((cx, y));
			for (int x = cx - 3; x <= cx - 1; x++) { doBolso.Add((x, cy - 2)); doBolso.Add((x, cy + 2)); }
			foreach ((int bx, int by) in doBolso)
			{
				pedreiro.Pos = CentroV21(cx + 1, by);   // (colado no fundo, a leste: a quatro celulas do braco mais distante, e fora do bolso)
				ComandoDeBloco(pedreiro, Protocol.BlocoErguer, bx, by, parede.Numero, "");
				// (A CENA E DE ANDAR: as paredes do bolso nao caem a soco -- senao o corpo sem caminho abriria um na marra)
				if (BlocoEm(terra, bx, by) is { } erguido) erguido.Resistencia = 1e30;
			}
			bool ergueu = doBolso.All(c => BlocoEm(terra, c.X, c.Y) != null && mapa.BlockedCell(c.X, c.Y));
			AfirmarV21("(palco) o pedreiro ergueu as onze paredes do bolso", ergueu, $"{doBolso.Count(c => BlocoEm(terra, c.X, c.Y) != null)} de {doBolso.Count}");
			DesfazerV2(pedreiro);
			corpos.Remove(pedreiro);
			if (!ergueu) return;

			Vec2 dentro = Caminho.Centro(cx - 1, cy), fora = Caminho.Centro(cx + 3, cy);
			(bool Chegou, double Segundos, float Perto) Cacar(string nome)
			{
				ServerPlayer alvo = Corpo(nome + "Alvo", fora, comCerebro: false);
				ServerPlayer cacador = Corpo(nome, dentro, comCerebro: true);
				float maisPerto = float.MaxValue;
				double t = 0;
				bool chegouNele = false;
				for (; t < 12 && !chegouNele; t += dt)
				{
					Tique(cacador, alvo, alvo.Pos);
					float d = Vec2.Distance(cacador.Pos, alvo.Pos);
					maisPerto = Math.Min(maisPerto, d);
					chegouNele = d <= 1.7f * T && !MoveRules.PathOccupied(mapa, cacador.Pos, alvo.Pos);
				}
				DesfazerV2(cacador, alvo);
				corpos.Remove(cacador);
				corpos.Remove(alvo);
				return (chegouNele, t, maisPerto);
			}

			int tracados = CaminhosTracadosDeTeste;
			var comCaminho = Cacar("Cacador");
			AfirmarV21("**O CORPO DIRIGIDO SAI DO BOLSO E CHEGA NO ALVO**: de dentro de um \"C\" de paredes, com o alvo do outro lado do fundo, ele sai pela boca (andando pra LONGE do alvo), da a volta e encosta -- em segundos",
					   comCaminho.Chegou && comCaminho.Segundos < 8 && CaminhosTracadosDeTeste > tracados,
					   $"chegou {comCaminho.Chegou} em {comCaminho.Segundos:0.0} s; mais perto {comCaminho.Perto / T:0.0} tiles; {CaminhosTracadosDeTeste - tracados} caminho(s) tracado(s)");

			Caminho.SemCaminhoDeTeste = PassoNaoContornaDeTeste = true;
			try
			{
				var semNada = Cacar("CacadorDeAntes");
				AfirmarV21("(defeito injetado: sem caminho nem contorno) o mesmo corpo passa os doze segundos empurrando o fundo do bolso -- *\"fica andando contra a parede\"*, a v0.2.0",
						   !semNada.Chegou && semNada.Perto > 2.5f * T, $"chegou {semNada.Chegou}; mais perto {semNada.Perto / T:0.0} tiles");
			}
			finally { Caminho.SemCaminhoDeTeste = PassoNaoContornaDeTeste = false; }

			// ---- D3) O CONTORNO: o passo que emperra vira pro lado livre (aqui sem caminho: so o atuador)
			// Um corpo a leste do fundo do bolso quer ir pra OESTE, de cara pro muro de cinco celulas.
			float Empurrar(string nome, bool doCerebro)
			{
				ServerPlayer andarilho = Corpo(nome, Caminho.Centro(cx + 1, cy), comCerebro: false);
				for (double t = 0; t < 5; t += dt)
				{
					AdiantoDoRelogioDeTeste += 33;
					MontarAsGrades();
					AplicarComando(andarilho, new Comando { Rumo = new Vec2(-1, 0), DesviaDeParede = doCerebro }, dt);
				}
				float x = andarilho.Pos.X;
				DesfazerV2(andarilho);
				corpos.Remove(andarilho);
				return x;
			}
			float muro = cx * T;
			float contornando = Empurrar("Contorna", doCerebro: true);
			float aMao = Empurrar("MontadoAMao", doCerebro: false);
			PassoNaoContornaDeTeste = true;
			float semContorno;
			try { semContorno = Empurrar("NaoContorna", doCerebro: true); }
			finally { PassoNaoContornaDeTeste = false; }
			AfirmarV21("**O PASSO QUE EMPERRA VIRA PRO LADO LIVRE**: de cara pra um muro de cinco celulas, o comando do cerebro o contorna e segue pro outro lado; o comando MONTADO A MAO (a rota de um chefe, o passo de uma bancada) continua batendo nele",
					   contornando < muro - T && aMao > muro + T * 0.5f, $"contornando parou em x={contornando / T:0.0} tiles; a mao em {aMao / T:0.0}; o muro e a coluna {cx}");
			AfirmarV21("(defeito injetado: o passo nao contorna) o comando do cerebro fica de cara pro muro -- o atuador que \"desliza no eixo livre e para\"",
					   semContorno > muro + T * 0.5f, $"parou em x={semContorno / T:0.0} tiles");

			ZerarBases();

			// ---- D4) A CARGA DE KI PELA REGRA DO DONO
			// O tempero e o da FERA do Oozaru (inteligencia zero, sem cautela, sem guarda): era ela que nunca carregava.
			// (a fera recua pra OESTE: ela nasce dez tiles adentro do corredor livre, com a presa a leste dela)
			Vec2 campo = TerraFirmeG12(16) + new Vec2(10 * T, 0);
			ServerPlayer Fera(string nome, Vec2 onde, double kiFrac, double folegoFrac)
			{
				ServerPlayer p = Corpo(nome, onde, comCerebro: false);
				// (as quatro linhas do verb de admin que libera o Ki -- `GameServer.Admin.cs`)
				LiberarOKi(p);
				p.Niveis.Aplicar(p.Ficha);
				AplicarPoderes(p);
				AplicarEfeitos(p);
				p.Cerebro = new Cerebro { VidaCautelosa = 0, ChanceDePesado = 0.85, IntervaloDeDecisao = 0.5, Disciplina = 0, Inteligencia = 0 };
				p.Ficha.Ki = p.Ficha.MaxKi * kiFrac;
				p.Ficha.stamina = p.Ficha.maxstamina * folegoFrac;
				return p;
			}
			// (O Ki que se mede e o MAIOR da luta: depois de encher ela volta a bater, e bater gasta.)
			(bool Carregou, double KiMaximo, float MaisLonge, double Folego, Plano NoFim, bool AindaCarrega) Lutar(ServerPlayer fera, ServerPlayer? presa, double segundos, bool presaPersegue = false)
			{
				bool carregou = false;
				double kiMaximo = 0;
				float maisLonge = 0;
				for (double t = 0; t < segundos; t += dt)
				{
					if (presaPersegue && presa != null) presa.Pos = fera.Pos + new Vec2(T * 0.9f, 0);
					Tique(fera, presa, presa?.Pos ?? fera.Pos + new Vec2(200, 0));
					carregou |= fera.Carregando;
					kiMaximo = Math.Max(kiMaximo, fera.Ficha.Ki / fera.Ficha.MaxKi);
					if (presa != null) maisLonge = Math.Max(maisLonge, Vec2.Distance(fera.Pos, presa.Pos));
				}
				return (carregou, kiMaximo, maisLonge, fera.Ficha.stamina / fera.Ficha.maxstamina, fera.Cerebro!.Atual, fera.Carregando);
			}

			ServerPlayer presa1 = Corpo("PresaParada", campo + new Vec2(T * 1.2f, 0), comCerebro: false);
			ServerPlayer fera1 = Fera("FeraComPoucoKi", campo, kiFrac: 0.2, folegoFrac: 1);
			bool sabeReunir = Jandirus.Core.Combat.CargaDeKi.SabeReunir(fera1.Ficha);
			var luta1 = Lutar(fera1, presa1, 14);
			AfirmarV21("**COM O KI BAIXO E VIGOR PRA PAGAR, O CORPO POSSUIDO SE AFASTA E CARREGA** (o tempero da fera do Oozaru, que nunca carregava): abre a distancia segura, planta o pe, enche ate 75% e volta pra luta -- e a carga custou vigor",
					   sabeReunir && luta1.Carregou && luta1.KiMaximo >= Cerebro.CarregarAte - 0.02 && luta1.MaisLonge >= Cerebro.DistanciaSegura - 4
					   && luta1.Folego < 0.995 && luta1.NoFim != Plano.Recuperar && !luta1.AindaCarrega,
					   $"sabe reunir {sabeReunir}; carregou {luta1.Carregou}; ki chegou a {luta1.KiMaximo:P0}; vigor {luta1.Folego:P1}; plano {luta1.NoFim}; chegou a {luta1.MaisLonge / T:0.0} tiles da presa");
			DesfazerV2(presa1, fera1);

			ServerPlayer presa2 = Corpo("PresaQueCansa", campo + new Vec2(T * 1.2f, 0), comCerebro: false);
			ServerPlayer fera2 = Fera("FeraSemVigor", campo, kiFrac: 0.2, folegoFrac: 0.10);
			var luta2 = Lutar(fera2, presa2, 4);
			AfirmarV21("...e SEM VIGOR ela nem tenta: luta com o que sobrou (*\"ate o vigor acabar\"*)",
					   !luta2.Carregou && luta2.NoFim != Plano.Recuperar, $"carregou {luta2.Carregou}; plano {luta2.NoFim}");
			DesfazerV2(presa2, fera2);

			ServerPlayer fera3 = Fera("FeraSozinha", campo, kiFrac: 0.2, folegoFrac: 1);
			var luta3 = Lutar(fera3, null, 12);
			AfirmarV21("sem ninguem por perto e com o Ki baixo, ela carrega ali mesmo antes de voltar a vagar",
					   luta3.Carregou && luta3.KiMaximo >= Cerebro.CarregarAte - 0.02 && luta3.NoFim != Plano.Recuperar,
					   $"carregou {luta3.Carregou}; ki chegou a {luta3.KiMaximo:P0}; plano {luta3.NoFim}");
			DesfazerV2(fera3);

			ServerPlayer presa4 = Corpo("PresaQuePersegue", campo + new Vec2(T * 0.9f, 0), comCerebro: false);
			ServerPlayer fera4 = Fera("FeraPerseguida", campo, kiFrac: 0.2, folegoFrac: 1);
			// (o cerebro nasce parado 1,2 s -- o compromisso -- e decide a cada 0,5 s: entra na carga por volta de 1,5 s,
			// e o prazo de abrir distancia vence 2,5 s depois)
			var cedo = Lutar(fera4, presa4, 2.6, presaPersegue: true);
			var depois = Lutar(fera4, presa4, 3.4, presaPersegue: true);
			var maisTarde = Lutar(fera4, presa4, 2.0, presaPersegue: true);
			AfirmarV21("com a presa COLADA nos calcanhares ela tenta abrir distancia e, sem conseguir, DESISTE em dois segundos e meio e volta a lutar -- nao recua pra sempre, e a carencia a segura de tentar de novo em seguida",
					   cedo.NoFim == Plano.Recuperar && depois.NoFim != Plano.Recuperar && maisTarde.NoFim != Plano.Recuperar
					   && !cedo.Carregou && !depois.Carregou && !maisTarde.Carregou,
					   $"a 2,6 s {cedo.NoFim}; a 6 s {depois.NoFim}; a 8 s {maisTarde.NoFim}; plantou o pe {cedo.Carregou || depois.Carregou || maisTarde.Carregou}");
			DesfazerV2(presa4, fera4);

			Cerebro.SoCarregaNoCriticoDeTeste = true;
			try
			{
				ServerPlayer presa5 = Corpo("PresaDeAntes", campo + new Vec2(T * 1.2f, 0), comCerebro: false);
				ServerPlayer fera5 = Fera("FeraDeAntes", campo, kiFrac: 0.2, folegoFrac: 1);
				var luta5 = Lutar(fera5, presa5, 14);
				AfirmarV21("(defeito injetado: so o portao do DM) a mesma fera, com o mesmo Ki baixo e o vigor cheio, nunca carrega -- *\"elas n carregam o ki nunca\"*, a v0.2.0",
						   !luta5.Carregou && luta5.KiMaximo < 0.3, $"carregou {luta5.Carregou}; ki chegou a {luta5.KiMaximo:P0}; plano {luta5.NoFim}");
				DesfazerV2(presa5, fera5);
			}
			finally { Cerebro.SoCarregaNoCriticoDeTeste = false; }
		}
		finally
		{
			Caminho.SemCaminhoDeTeste = PassoNaoContornaDeTeste = Cerebro.SoCarregaNoCriticoDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			ZerarBases();
			foreach (ServerPlayer p in corpos) if (_players.ContainsKey(p.Id)) DesfazerV2(p);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// U) A IA USA TODAS AS SKILLS
	// =====================================================================
	private void AIaUsaTodasAsSkillsV21()
	{
		GD.Print("[v021] -- U) A IA USA TODAS AS SKILLS: o corpo possuido solta o livro inteiro (item 22, a segunda metade)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		const double dt = Protocol.TickSeconds;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		List<(int Quem, string Tecnica)>? escutaDeAntes = EscutaDeTecnicasDaIa;
		var apertos = new List<(int Quem, string Tecnica)>();
		var feras = new List<ServerPlayer>();
		double restoDoRelogio = 0;
		int tique = 0;

		// O MUNDO ANDA UM TIQUE: a ordem do `Tick()` de producao, com o que uma luta usa -- e os corpos dirigidos sao
		// tiqueados pelo `TickDosCorposSemDono`, o caminho de producao (o corpo com cerebro e sem papel cai no ramo do
		// POSSUIDO: caca quem ve, a dez tiles).
		void Mundo()
		{
			restoDoRelogio += dt * 1000;
			long ms = (long)restoDoRelogio;
			restoDoRelogio -= ms;
			AdiantoDoRelogioDeTeste += ms;
			tique++;
			MontarAsGrades();
			TickCombate(dt);
			TickDosCorposSemDono(dt);
			TickDoEmpurrao();
			TickDoAgarrao(dt);
			TickDoArranque(dt);
			TickDosCanaisDeKi(dt);
			TickG12(dt);
			TickG14();
			TickDosProjeteis(dt);
			TickDosEmbatesDeKi(dt);
			TickDoPrazoDeRaioDaIa(dt);
			TickDoSegundoApertoDaIa();
			if (tique % 3 == 0) Pulsar();   // (o `spawn(1)` do DM: um decimo de segundo)
			if (tique % 30 == 0) { TickDasTecnicas(); TickDasTecnicasG6(); TickDosBuffs(); TickTecnicasG2(); }
		}

		// O CORPO POSSUIDO: sabe o catalogo inteiro, e quem o dirige e o cerebro da fera do Oozaru -- os cinco numeros
		// do `TomarAsRedeas` (`GameServer.Oozaru.cs`), pela porta da posse.
		ServerPlayer Fera(string nome, Vec2 onde)
		{
			ServerPlayer f = ForjarQueSabeTudo(nome, onde);
			f.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			TanqueDeVerdade(f);
			AssumirOCorpo(f, new Cerebro { VidaCautelosa = 0, ChanceDePesado = 0.85, IntervaloDeDecisao = 0.5, Disciplina = 0, Inteligencia = 0 });
			feras.Add(f);
			return f;
		}
		// O TANQUE TEM DE SOBREVIVER AO PRIMEIRO REFORCO. O forjador do catalogo escreve o `MaxKi` na mao (cinco bilhoes),
		// e todo reforco que liga refaz os atributos (`LigarBuff` -> `Statify`, que REESCREVE o `MaxKi` a partir do
		// `baseKi`): o tanque do corpo desabava pra cem no primeiro buff, e dali em diante nenhuma tecnica de trezentos
		// drenos pra cima cabia nele -- a bancada media a fera mais pobre do jogo. O `baseKi` e o que o `Statify` le.
		static void TanqueDeVerdade(ServerPlayer p)
		{
			p.Ficha.baseKi = 2_000_000;
			p.Ficha.Statify();
			p.Ficha.Ki = p.Ficha.MaxKi;
			p.Ficha.stamina = p.Ficha.maxstamina;
		}
		// O BONECO DA LUTA LONGA: sabe tudo (pra atirar de volta na fase do tiro vindo) e e VINTE vezes mais forte -- o
		// que se mede aqui e o que a fera APERTA, e um boneco que cai a cada tres golpes tira a presa dela (quem esta
		// no chao nao e presa) e a manda vagar um segundo e meio de cada vez.
		ServerPlayer BonecoDuro(string nome, Vec2 onde)
		{
			ServerPlayer b = ForjarQueSabeTudo(nome, onde);
			TanqueDeVerdade(b);
			b.Ficha.BP *= 20;
			b.Ficha.PowerLevel(agoraMs: NowMs());
			return b;
		}
		static void Inteiro(ServerPlayer p)
		{
			p.Ficha.KO = false;
			p.Ficha.dead = false;
			p.Combate.Corpo.Restaurar();
			p.Combate.SincronizarVida();
		}
		static void Cheio(ServerPlayer p) { p.Ficha.Ki = p.Ficha.MaxKi; p.Ficha.stamina = p.Ficha.maxstamina; }
		static void Ferido(ServerPlayer p)
		{
			foreach (BodyPart parte in p.Combate.Corpo.Partes) if (!parte.Decepado) parte.Vida = parte.VidaMax * 0.4;
			p.Combate.SincronizarVida();
		}

		// UMA FASE DA LUTA: o boneco preso a `tiles` da fera (os dois voltam pro lugar a cada tique -- a investida corre
		// e o arremesso joga, e a fase seguinte precisa da distancia dela), inteiro, e a fera com os tanques cheios.
		// Quem esta nos bracos de alguem nao e reposto: o agarrao e que diz onde ele fica.
		// O RETRATO DE CADA FASE vai pro log -- numa reprovacao e ele que diz ONDE a tecnica nao saiu, e por que: em
		// quantos tiques a fera esteve em cada plano, com as maos ocupadas por ki e com a presa nos bracos.
		var planosDaFase = new Dictionary<Plano, int>();
		int tiquesDaFase = 0, maosCheias = 0, nosBracos = 0;
		void Luta(ServerPlayer fera, ServerPlayer boneco, Vec2 ancora, float tiles, double segundos, Action<int>? aCadaTique = null)
		{
			int n = (int)(segundos / dt);
			for (int i = 0; i < n; i++)
			{
				Inteiro(boneco);
				Inteiro(fera);
				Cheio(fera);
				if (fera.AgarrandoId != boneco.Id && boneco.AgarradoPorId == 0)
				{
					fera.Pos = ancora;
					boneco.Pos = ancora + new Vec2(tiles * T, 0);
					boneco.TiquesDeVoo = 0;
				}
				aCadaTique?.Invoke(i);
				Mundo();
				tiquesDaFase++;
				if (fera.Cerebro is { } pensa) planosDaFase[pensa.Atual] = planosDaFase.GetValueOrDefault(pensa.Atual) + 1;
				if (MaosOcupadasDaIa(fera)) maosCheias++;
				if (fera.AgarrandoId != 0) nosBracos++;
			}
		}
		HashSet<string> Apertadas(ServerPlayer fera) =>
			new(apertos.Where(a => a.Quem == fera.Id).Select(a => a.Tecnica), StringComparer.OrdinalIgnoreCase);
		var jaVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void Fase(ServerPlayer fera, string nome)
		{
			HashSet<string> agora = Apertadas(fera);
			List<string> novas = [.. agora.Where(t => !jaVistas.Contains(t))];
			jaVistas.UnionWith(agora);
			int n = Math.Max(tiquesDaFase, 1);
			GD.Print($"[v021]      fase {nome}: +{novas.Count} tecnica(s) nova(s), {agora.Count} no total"
					 + $" | planos: {string.Join(", ", planosDaFase.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {100 * kv.Value / n}%"))}"
					 + $" | maos com ki {100 * maosCheias / n}%, presa nos bracos {100 * nosBracos / n}%"
					 + $" | novas: {string.Join(" ", novas)}");
			planosDaFase.Clear();
			tiquesDaFase = maosCheias = nosBracos = 0;
		}
		static List<string> Ids(Arsenal a)
		{
			var l = new List<string>();
			for (int i = 0; i < a.Quantas; i++) l.Add(a[i].Id);
			return l;
		}

		EscutaDeTecnicasDaIa = apertos;
		try
		{
			Vec2 praca = TerraFirmeG12(30) + new Vec2(12 * T, 0);

			// ---------------------------------------------------------------- U1) A LUTA LONGA: TODAS
			ServerPlayer fera = Fera("FeraQueSabeTudo", praca);
			ServerPlayer boneco = BonecoDuro("Boneco", praca + new Vec2(T, 0));
			AfirmarV21("(montagem) o tanque da fera e de verdade e sobrevive a um reforco ligado: mais de um milhao de Ki, e a tecnica mais cara da tabela (setecentos drenos) cabe nele",
					   fera.Ficha.MaxKi > 1_000_000 && 700 * fera.Ficha.BaseDrain() < fera.Ficha.MaxKi * 0.5,
					   $"MaxKi {fera.Ficha.MaxKi:N0}; 700 drenos = {700 * fera.Ficha.BaseDrain():N0}");
			Capacidades sabe = LerCapacidades(fera);
			List<string> tiros = Ids(sabe.DeLonge), luta = Ids(sabe.DeLuta);
			AfirmarV21("(montagem) o corpo possuido sabe os trinta e dois tiros e mais de sessenta tecnicas de luta, e tem cerebro sem inteligencia nenhuma (a fera do Oozaru)",
					   tiros.Count == TecnicasDeLonge.Quantas && luta.Count > 60 && fera.Cerebro is { Inteligencia: 0, Disciplina: 0 },
					   $"{tiros.Count} tiros, {luta.Count} de luta");

			// As distancias cobrem as janelas todas: colado (os golpes, os agarroes), dois tiles (as corridas curtas, os
			// tiros de perto), tres e meio e cinco e meio (o grosso dos tiros, as investidas longas), sete e meio e nove
			// (os raios compridos, a Genkidama). Os raios seguram o corpo ate o prazo: as fases deles sao as longas.
			// O AQUECIMENTO, E O TANQUE NA MEDIDA DO CORPO. Em quarenta e cinco segundos colada no alvo a fera liga os
			// reforcos -- e so entao os precos dela sao os da luta: o de todo punho nomeado e `Ephysoff * BaseDrain * N`
			// (a conta do verb), e este corpo (o livro inteiro no nivel maximo, a forma gigante, os reforcos que
			// quintuplicam o dreno) tem o ataque fisico de dezenas de lutadores. Com o tanque de dois milhoes o golpe mais
			// barato custava mais que o tanque INTEIRO, e a fera so dava os que nao cobram dreno (medido: quatro golpes em
			// quatro minutos). O preco cresce com a raiz do tanque: o tanque em que o mais caro e um quarto e
			// `16 * preco^2 / tanque`.
			Luta(fera, boneco, praca, 1.0f, 45); Fase(fera, "aquecimento (colado)");
			(double Preco, int Golpes) MaisCaro()
			{
				Arsenal comReforcos = LerCapacidades(fera).DeLuta;
				double preco = 0;
				int golpes = 0;
				for (int i = 0; i < comReforcos.Quantas; i++)
				{
					if (comReforcos[i].Uso != Jandirus.Core.Ai.Uso.Golpe) continue;
					golpes++;
					preco = Math.Max(preco, comReforcos[i].CustoDeKi);
				}
				return (preco, golpes);
			}
			var antesDoAjuste = MaisCaro();
			double tanqueDeAntes = fera.Ficha.MaxKi;
			if (antesDoAjuste.Preco > tanqueDeAntes * 0.25)
			{
				fera.Ficha.baseKi *= 16 * antesDoAjuste.Preco * antesDoAjuste.Preco / (tanqueDeAntes * tanqueDeAntes);
				fera.Ficha.Statify();
				fera.Ficha.Ki = fera.Ficha.MaxKi;
			}
			var noTanque = MaisCaro();
			AfirmarV21("(montagem, com os reforcos ligados) o tanque da fera fica na medida do corpo dela: o golpe de perto mais caro cabe em meio tanque",
					   noTanque.Golpes > 30 && noTanque.Preco <= fera.Ficha.MaxKi * 0.5,
					   $"{noTanque.Golpes} golpes; o mais caro custava {antesDoAjuste.Preco:N0} com o tanque em {tanqueDeAntes:N0}, e custa {noTanque.Preco:N0} com ele em {fera.Ficha.MaxKi:N0} (Ephysoff {fera.Ficha.Ephysoff:0.#}, BaseDrain {fera.Ficha.BaseDrain():N0})");

			Luta(fera, boneco, praca, 1.0f, 300); Fase(fera, "colado (1 tile)");
			Luta(fera, boneco, praca, 2.2f, 90); Fase(fera, "a 2,2 tiles");
			Luta(fera, boneco, praca, 3.5f, 110); Fase(fera, "a 3,5 tiles");
			Luta(fera, boneco, praca, 5.5f, 170); Fase(fera, "a 5,5 tiles");
			Luta(fera, boneco, praca, 7.5f, 60); Fase(fera, "a 7,5 tiles");
			Luta(fera, boneco, praca, 9.0f, 50); Fase(fera, "a 9 tiles");
			// FERIDA: a vida a 40% -- os socorros.
			Luta(fera, boneco, praca, 3.5f, 28, _ => Ferido(fera)); Fase(fera, "ferida");
			// O TANQUE NO FIM, QUATRO VEZES: os reforcos saem, o Estirao acode, e na volta ligam os IRMAOS de grupo.
			for (int volta = 0; volta < 4; volta++)
			{
				Luta(fera, boneco, praca, 1.0f, 5, _ => fera.Ficha.Ki = fera.Ficha.MaxKi * 0.2);
				Luta(fera, boneco, praca, 1.0f, 9);
			}
			Fase(fera, "o tanque no fim, quatro vezes");
			// HA TIRO VINDO: o boneco atira na fera a cada dois segundos.
			boneco.Facing = Facing.West;
			boneco.AlvoId = fera.Id;
			Luta(fera, boneco, praca, 6f, 24, i =>
			{
				Cheio(boneco);
				if (i % 60 == 0) UsarHabilidade(boneco, "Basic_Blast");
			});
			Fase(fera, "tiro vindo");

			HashSet<string> usadas = Apertadas(fera);
			List<string> tirosQueFaltaram = [.. tiros.Where(id => !usadas.Contains(id))];
			List<string> lutaQueFaltou = [.. luta.Where(id => !usadas.Contains(id))];
			AfirmarV21($"**O CORPO POSSUIDO SOLTA TODOS OS TIROS QUE SABE**: numa luta longa, pelo tique de producao, os {tiros.Count} do arsenal de longe sairam -- o raio, as bolas, as barragens, os cercos, as que prendem, a Genkidama",
					   tirosQueFaltaram.Count == 0, "faltaram: " + string.Join(", ", tirosQueFaltaram));
			AfirmarV21($"**...E TODAS AS TECNICAS DE LUTA**: as {luta.Count} do repertorio sairam -- os golpes no lugar do soco, os agarroes, as investidas, os reforcos (os quatro do mesmo slot se revezando), os socorros e a deflexao",
					   lutaQueFaltou.Count == 0, "faltaram: " + string.Join(", ", lutaQueFaltou));
			DestruirSplitformsG12(fera);
			DesfazerV2(fera, boneco);
			LimparTudoDaBancada();
			apertos.Clear();

			// ---------------------------------------------------------------- U2) O REPERTORIO DESLIGADO (a v0.2.0)
			Cerebro.SemRepertorioDeTeste = true;
			try
			{
				ServerPlayer deAntes = Fera("FeraDeAntes", praca);
				ServerPlayer alvoDeAntes = BonecoDuro("AlvoDeAntes", praca + new Vec2(T, 0));
				Luta(deAntes, alvoDeAntes, praca, 1.0f, 40);
				HashSet<string> so = Apertadas(deAntes);
				AfirmarV21("(defeito injetado: o repertorio de luta nao e consultado) em quarenta segundos colada no alvo a mesma fera nao aperta UMA tecnica de luta -- so soca (e solta os tiros de alcance zero): a v0.2.0",
						   !luta.Any(so.Contains), string.Join(", ", luta.Where(so.Contains)));
				DestruirSplitformsG12(deAntes);
				DesfazerV2(deAntes, alvoDeAntes);
			}
			finally { Cerebro.SemRepertorioDeTeste = false; }
			LimparTudoDaBancada();
			apertos.Clear();

			// ---------------------------------------------------------------- U3) O RODIZIO
			int Variedade(string nome)
			{
				ServerPlayer f = Fera(nome, praca);
				ServerPlayer alvo = BonecoDuro("AlvoDe" + nome, praca + new Vec2(3.5f * T, 0));
				Luta(f, alvo, praca, 3.5f, 45);
				int distintos = Apertadas(f).Count(id => TecnicasDeLonge.Get(id) != null);
				DestruirSplitformsG12(f);
				DesfazerV2(f, alvo);
				LimparTudoDaBancada();
				apertos.Clear();
				return distintos;
			}
			int comRodizio = Variedade("FeraQueVaria");
			Cerebro.SemRodizioDeTeste = true;
			int semRodizio;
			try { semRodizio = Variedade("FeraQueRepete"); }
			finally { Cerebro.SemRodizioDeTeste = false; }
			AfirmarV21("**O RODIZIO**: em 45 s a tres tiles e meio a fera solta pelo menos seis tiros DIFERENTES -- sai o que esta ha mais tempo sem ser usado, e a nota so desempata",
					   comRodizio >= 6, $"{comRodizio} tiros diferentes");
			AfirmarV21("(defeito injetado: o cerebro esquece o que ja usou) na mesma cena a escolha volta a eleger o mais caro que cabe -- no maximo tres tiros diferentes, e menos da metade",
					   semRodizio <= 3 && semRodizio * 2 <= comRodizio, $"{semRodizio} tiros diferentes (com o rodizio, {comRodizio})");

			// ---------------------------------------------------------------- U4) O SEGUNDO APERTO
			(bool Ligou, bool Armou, bool Parou) Barragem(string nome)
			{
				ServerPlayer f = Fera(nome, praca);
				ServerPlayer alvo = ForjarQueSabeTudo("AlvoDe" + nome, praca + new Vec2(9 * T, 0));
				alvo.Ficha.KO = true;   // (caido: a fera nao tem presa, e o cerebro nao pede mais nada -- so o relogio aperta)
				AplicarComando(f, new Comando { Habilidade = "BusterBarrage" }, dt);
				bool ligou = _busterG12.ContainsKey(f.Id), armou = _segundoApertoDaIa.ContainsKey(f.Id);
				for (int i = 0; i < (int)(7 / dt); i++) { Cheio(f); Mundo(); }
				bool parou = !_busterG12.ContainsKey(f.Id);
				if (!parou) UsarHabilidade(f, "BusterBarrage");
				DesfazerV2(f, alvo);
				LimparTudoDaBancada();
				apertos.Clear();
				return (ligou, armou, parou);
			}
			var barragem = Barragem("FeraDaBarragem");
			AfirmarV21("**A TECNICA QUE SE SEGURA GANHA O SEGUNDO APERTO**: o pulso do cerebro LIGA a Buster Barrage, o relogio do servidor e armado, e cinco segundos depois o mesmo verb e apertado de novo -- ela para",
					   barragem.Ligou && barragem.Armou && barragem.Parou, $"ligou {barragem.Ligou}; relogio armado {barragem.Armou}; parou {barragem.Parou}");
			SemSegundoApertoDeTeste = true;
			try
			{
				var semRelogio = Barragem("FeraSemRelogio");
				AfirmarV21("(defeito injetado: ninguem da o segundo aperto) a barragem que a IA ligou continua ligada sete segundos depois -- ela cuspiria ate o Ki acabar",
						   semRelogio.Ligou && !semRelogio.Armou && !semRelogio.Parou, $"ligou {semRelogio.Ligou}; parou {semRelogio.Parou}");
			}
			finally { SemSegundoApertoDeTeste = false; }

			// ---------------------------------------------------------------- U5) QUEM E DIRIGIDO NAO OUVE O VERB
			(int Caladas, int Ouvidas) Recusa(string nome)
			{
				ServerPlayer f = Fera(nome, praca);
				f.Ficha.Ki = 0;   // (sem Ki o verb RECUSA em voz alta: "isso pede N de energia")
				int caladas = _frasesCaladasDaIa;
				List<string> ouvidas = Ouvir(() => AplicarComando(f, new Comando { Habilidade = "Charged_Shot" }, dt));
				caladas = _frasesCaladasDaIa - caladas;
				DesfazerV2(f);
				apertos.Clear();
				return (caladas, ouvidas.Count);
			}
			var recusa = Recusa("FeraCalada");
			AfirmarV21("**O DONO DO CORPO NAO OUVE O VERB QUE NAO APERTOU**: o verb que a IA pediu e o jogo recusou responde (a bancada escuta), e a frase NAO vai pro corpo dirigido",
					   recusa.Ouvidas > 0 && recusa.Caladas == recusa.Ouvidas, $"{recusa.Ouvidas} frase(s) do verb; {recusa.Caladas} calada(s)");
			IaFalaComODonoDeTeste = true;
			try
			{
				var falando = Recusa("FeraQueFala");
				AfirmarV21("(defeito injetado: a frase vai pro dono do corpo) a mesma recusa nao e calada -- com o livro inteiro na mao da IA, o jogador possuido leria uma linha de sistema a cada segundo e meio",
						   falando.Ouvidas > 0 && falando.Caladas == 0, $"{falando.Ouvidas} frase(s); {falando.Caladas} calada(s)");
			}
			finally { IaFalaComODonoDeTeste = false; }

			// ---------------------------------------------------------------- U7) NO TREINO AMISTOSO NAO HA GOLPE ESPECIAL
			// So o cerebro, com as capacidades de um corpo que sabe UM punho nomeado e esta colado no parceiro: vinte
			// segundos de `Pensar`, contando os pulsos do repertorio. (Quem liga a chave no jogo e o spar da rotina dos
			// habitantes -- a `--rotinateste` mede esse fio.)
			int PulsosNoTreino(bool treino)
			{
				ServerPlayer quemSabe = ForjarV2("SabeUmPunho", praca, 80_000_000, "Saiyan");
				quemSabe.VerbosAbsorvidos = ["One_Two"];
				TanqueDeVerdade(quemSabe);
				var c = new Cerebro { VidaCautelosa = 0, ChanceDePesado = 0.85, IntervaloDeDecisao = 0.5, Disciplina = 0, Inteligencia = 0 };
				c.Poderes = LerCapacidades(quemSabe);
				c.LutaDeTreino = treino;
				var sorte = new Random(20261013);
				int pulsos = 0;
				for (int i = 0; i < (int)(20 / dt); i++)
				{
					var p = new Jandirus.Core.Ai.Percepcao
					{
						TemAlvo = true, IdDoAlvo = 4242, Minha = praca, DoAlvo = praca + new Vec2(T, 0), LinhaLivre = true,
						VidaFrac = 1, KiFrac = 1, Ki = quemSabe.Ficha.MaxKi, FolegoFrac = 1, Folego = quemSabe.Ficha.maxstamina,
						MeuPoder = 1000, PoderDoAlvo = 1000, VidaDoAlvo = 1,
					};
					if (c.Pensar(p, dt, sorte).Habilidade is not { Length: > 0 } id) continue;
					c.Apertou(id);
					if (Jandirus.Core.Skills.TecnicasDeLuta.Get(id) != null) pulsos++;
				}
				DesfazerV2(quemSabe);
				return pulsos;
			}
			int lutando = PulsosNoTreino(treino: false), treinando = PulsosNoTreino(treino: true);
			AfirmarV21("**NO TREINO AMISTOSO NAO HA GOLPE ESPECIAL**: numa luta de verdade o cerebro solta o punho nomeado que sabe; avisado de que a luta e o spar da vida diaria, vinte segundos colado no parceiro e nenhum pulso do repertorio",
					   lutando >= 2 && treinando == 0, $"{lutando} pulso(s) lutando, {treinando} treinando");
			Cerebro.TreinoComRepertorioDeTeste = true;
			try
			{
				int semARegra = PulsosNoTreino(treino: true);
				AfirmarV21("(defeito injetado: o cerebro ignora que e treino) o mesmo spar leva os punhos nomeados -- o que nocauteava o habitante com a vida em 84%",
						   semARegra >= 2, $"{semARegra} pulso(s)");
			}
			finally { Cerebro.TreinoComRepertorioDeTeste = false; }

			// ---------------------------------------------------------------- U6) COM A PRESA NOS BRACOS
			// Uma fera que so sabe o Aperto (o golpe de agarrao que deixa a presa na mao) e nenhum tiro: o plano dela e
			// pressionar, e colada no alvo o plano de pressionar NAO anda. O boneco nao tem cerebro: nao se debate.
			(bool Agarrou, double Segurou, bool Apertou, bool Voou, bool AindaNosBracos) Agarrao(string nome, string golpe = "Clench")
			{
				ServerPlayer f = ForjarV2(nome, praca, 80_000_000, "Saiyan");
				f.Conta = "bancada_v021_" + nome.ToLowerInvariant();
				f.VerbosAbsorvidos = [golpe];
				TanqueDeVerdade(f);
				AssumirOCorpo(f, new Cerebro { VidaCautelosa = 0, ChanceDePesado = 0.85, IntervaloDeDecisao = 0.5, Disciplina = 0, Inteligencia = 0 });
				feras.Add(f);
				ServerPlayer alvo = ForjarV2("AlvoDe" + nome, praca + new Vec2(T, 0), 80_000_000);
				bool agarrou = false, apertou = false, voou = false;
				double segurou = 0;
				for (int i = 0; i < (int)(40 / dt) && segurou < 8; i++)
				{
					Inteiro(alvo);
					Inteiro(f);
					Cheio(f);
					bool antes = f.AgarrandoId == alvo.Id;
					if (!antes && alvo.AgarradoPorId == 0 && alvo.TiquesDeVoo == 0 && !agarrou)
					{
						f.Pos = praca;
						alvo.Pos = praca + new Vec2(T, 0);
					}
					Mundo();
					bool agora = f.AgarrandoId == alvo.Id;
					if (agora) { agarrou = true; segurou += dt; apertou |= f.Estrangulando; }
					if (antes && !agora) { voou = alvo.TiquesDeVoo > 0 || (alvo.Pos - f.Pos).Length > 1.6f * T; break; }
				}
				bool aindaNosBracos = f.AgarrandoId == alvo.Id;
				if (aindaNosBracos) Soltar(f, MotivoDaSoltura.Tecla);
				DesfazerV2(f, alvo);
				LimparTudoDaBancada();
				apertos.Clear();
				return (agarrou, segurou, apertou, voou, aindaNosBracos);
			}
			var presa = Agarrao("FeraQueAgarra");
			AfirmarV21("**COM A PRESA NOS BRACOS A FERA APERTA E ARREMESSA**: o golpe de agarrao deixa o alvo na mao, ela aperta o pescoco (um pulso, e fica parada apertando) e, vencido o prazo de dois segundos e meio, afrouxa e da o passo que arremessa",
					   presa.Agarrou && presa.Apertou && presa.Voou && !presa.AindaNosBracos
					   && presa.Segurou >= Cerebro.PrazoComAPresaNaMao - 0.1 && presa.Segurou <= Cerebro.PrazoComAPresaNaMao + 2 * Cerebro.PausaDoPescoco + 0.3,
					   $"agarrou {presa.Agarrou}; apertou o pescoco {presa.Apertou}; segurou {presa.Segurou:0.00} s; a presa voou {presa.Voou}; ainda nos bracos {presa.AindaNosBracos}");
			// O COLO: o Espigao Gigante LEVANTA a presa (o modo de carregar), corre com ela e a esmaga -- e ela continua
			// no colo. Ali o passo nao arremessa (leva junto): quem solta e o terceiro toque da tecla de agarrar.
			var colo = Agarrao("FeraQueLevanta", "Gigantic_Spike");
			AfirmarV21("**...E A PRESA NO COLO E SOLTA**: o Espigao levanta o alvo (o modo de carregar, em que andar o LEVA junto em vez de arremessar); vencido o prazo a fera afrouxa o pescoco e da o toque na tecla de agarrar -- ele cai dos bracos dela",
					   colo.Agarrou && colo.Apertou && !colo.AindaNosBracos
					   && colo.Segurou >= Cerebro.PrazoComAPresaNaMao - 0.1 && colo.Segurou <= Cerebro.PrazoComAPresaNaMao + 3 * Cerebro.PausaDoPescoco + 0.3,
					   $"agarrou {colo.Agarrou}; apertou o pescoco {colo.Apertou}; segurou {colo.Segurou:0.00} s; ainda nos bracos {colo.AindaNosBracos}");
			Cerebro.PresaFicaNosBracosDeTeste = true;
			try
			{
				var coloParado = Agarrao("FeraQueNaoLarga", "Gigantic_Spike");
				AfirmarV21("(defeito injetado: vencido o prazo a receita devolve o corpo ao plano) com a presa no colo o plano anda e ela vai junto: oito segundos depois continua nos bracos",
						   coloParado.Agarrou && coloParado.AindaNosBracos && coloParado.Segurou >= 7.9,
						   $"agarrou {coloParado.Agarrou}; segurou {coloParado.Segurou:0.00} s; ainda nos bracos {coloParado.AindaNosBracos}");
				var presaParada = Agarrao("FeraQueNaoSolta");
				AfirmarV21("(defeito injetado: vencido o prazo a receita devolve o corpo ao plano) colada no alvo a fera nao da passo nenhum: oito segundos depois a presa continua nos bracos dela -- e com alguem nos bracos o repertorio inteiro fica calado",
						   presaParada.Agarrou && presaParada.AindaNosBracos && !presaParada.Voou && presaParada.Segurou >= 7.9,
						   $"agarrou {presaParada.Agarrou}; segurou {presaParada.Segurou:0.00} s; a presa voou {presaParada.Voou}; ainda nos bracos {presaParada.AindaNosBracos}");
			}
			finally { Cerebro.PresaFicaNosBracosDeTeste = false; }
		}
		finally
		{
			Cerebro.SemRepertorioDeTeste = Cerebro.SemRodizioDeTeste = Cerebro.PresaFicaNosBracosDeTeste = false;
			Cerebro.TreinoComRepertorioDeTeste = false;
			SemSegundoApertoDeTeste = IaFalaComODonoDeTeste = false;
			EscutaDeTecnicasDaIa = escutaDeAntes;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			foreach (ServerPlayer f in feras) if (_players.ContainsKey(f.Id)) { DestruirSplitformsG12(f); DesfazerV2(f); }
			_segundoApertoDaIa.Clear();
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// K4) A POSE, O SOM E A TECLA X
	// =====================================================================
	private void APoseEOSomDoTiroV21()
	{
		GD.Print("[v021] -- K4) A POSE DE TIRO EM TODO ATAQUE DE KI, O SOM DO DM E A TECLA X (itens 1, 11 e 34)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Vec2 mao = TerraFirmeG12(16);
		long agora = NowMs();

		ServerPlayer atirador = ForjarV2("PoseDeTiro", mao, 10_000);
		atirador.Facing = Facing.East;
		AfirmarV21("(montagem) antes do tiro o corpo esta na pose normal", atirador.Pose(agora, false) == Protocol.Pose.Normal, atirador.Pose(agora, false).ToString());

		Projetil bola = Disparar(atirador, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6 }, verbo: "Basic_Blast");
		// O INSTANTE DO TIRO, lido AQUI: a duracao da pose se mede a partir dele. Lida quatro afirmacoes depois (cada
		// uma escreve no console), a margem de 80 ms abaixo estourava com a maquina ocupada -- e a bancada reprovava a
		// regra certa.
		long noTiro = NowMs();
		EntityState e = EstadoDe(atirador, noTiro);
		AfirmarV21("**A BOLA DE KI VESTE A POSE DE TIRO** (a folha `blast`): o snapshot sai `Canalizando` + atirando",
				   e.Pose == Protocol.Pose.Canalizando && e.CanalAtirando, $"pose {e.Pose}, atirando {e.CanalAtirando}");
		AfirmarV21("...marcada como SO A POSE: o dono do corpo nao fica plantado (o gesto nao e canal)", e.CanalSoDePose);
		AfirmarV21("...e o corpo continua podendo andar no servidor (a pose nao entra no `PodeMexerOCorpo`)", PodeMexerOCorpo(atirador) && !EnraizadoPorKi(atirador.Id));
		AfirmarV21("...por tres decimos de segundo, como o `spawn(3) usr.icon_state=\"\"` (`blasts.dm:83`)",
				   atirador.Pose(noTiro + PoseDeTiroMs + 50, false) == Protocol.Pose.Normal && atirador.Pose(noTiro + PoseDeTiroMs - 80, false) == Protocol.Pose.Canalizando);
		AfirmarV21("a bola sai com o som do DM no pacote: `fire_kiblast` (`SomDeKi.Bola`)", bola.Som == SomDeKi.Bola, bola.Som.ToString());

		// o byte do canal aguenta os tres campos juntos
		var bits = new EntityState { Pose = Protocol.Pose.Canalizando };
		bits.CanalSoDePose = true;
		bits.CargaDoCanal = 9;
		bits.CanalAtirando = true;
		bool juntos = bits is { CanalSoDePose: true, CargaDoCanal: 9, CanalAtirando: true };
		bits.CargaDoCanal = 3;
		bits.CanalSoDePose = false;
		AfirmarV21("o byte do canal carrega os tres campos sem um apagar o outro (a carga, o `atirando` e o `so a pose`)",
				   juntos && bits is { CanalSoDePose: false, CargaDoCanal: 3, CanalAtirando: true }, $"canal 0x{bits.Canal:X2}");

		TiroSemPoseDeTeste = true;
		try
		{
			LimparTudoDaBancada();
			ServerPlayer semPose = ForjarV2("SemPose", mao, 10_000);
			Disparar(semPose, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6 }, verbo: "Basic_Blast");
			AfirmarV21("(defeito injetado: o tiro sem pose) a bola sai e o corpo fica na pose normal -- a v0.2.0", EstadoDe(semPose, NowMs()).Pose == Protocol.Pose.Normal);
		}
		finally { TiroSemPoseDeTeste = false; }

		// o raio canalizado: canal de verdade
		LimparTudoDaBancada();
		ServerPlayer deRaio = ForjarV2("ComRaioNaMao", mao, 10_000);
		Projetil? raio = null;
		{
			deRaio.Facing = Facing.East;
			ReceitaDeProjetil receita = SemDeflexao();
			receita.AlcanceTiles = 30;
			Canalizar(deRaio, "Kamehameha", 10 * deRaio.Ficha.BaseDrain(), receita);
			for (int i = 0; i < 90 && raio == null; i++)
			{
				TiqueDoParry(deRaio);
				raio = ProjeteisDaZona(deRaio.Zone.Hash).FirstOrDefault(q => q.Dono == deRaio.Id);
			}
		}
		EntityState doRaio = EstadoDe(deRaio, NowMs());
		AfirmarV21("o RAIO NA MAO e canal de verdade: `Canalizando`, atirando, e SEM o bit de so-a-pose (o corpo fica plantado)",
				   raio != null && doRaio.Pose == Protocol.Pose.Canalizando && doRaio.CanalAtirando && !doRaio.CanalSoDePose && EnraizadoPorKi(deRaio.Id),
				   $"raio {raio != null}; pose {doRaio.Pose}; so a pose {doRaio.CanalSoDePose}");
		AfirmarV21("...e sai com o som que ANDA com ele: `kamehameha_fire` (`SomDeKi.Raio`)", raio?.Som == SomDeKi.Raio && SonsDeKi.AndaComOAtaque(SomDeKi.Raio) && !SonsDeKi.AndaComOAtaque(SomDeKi.Bola), $"{raio?.Som}");

		// a tabela
		AfirmarV21("A TABELA DE SONS, pelo DM: rajadas `KIBLAST`, teleguiada `burning_fire`, discos `disc_fire`, Kikoho, Death Ball `Blast`, Genkidama, Final Flash `basicbeam_fire`",
				   SonsDeKi.DoDisparo("Energy_Barrage", TipoDeProjetil.Blast) == SomDeKi.Rajada
				   && SonsDeKi.DoDisparo("Hellzone_Grenade", TipoDeProjetil.Guided) == SomDeKi.Rajada
				   && SonsDeKi.DoDisparo("Guided_Ball", TipoDeProjetil.Guided) == SomDeKi.Queima
				   && SonsDeKi.DoDisparo("Kienzan", TipoDeProjetil.Blast) == SomDeKi.Disco
				   && SonsDeKi.DoDisparo("Kikoho", TipoDeProjetil.Blast) == SomDeKi.Kikoho
				   && SonsDeKi.DoDisparo("Death_Ball", TipoDeProjetil.Blast) == SomDeKi.EsferaDaMorte
				   && SonsDeKi.DoDisparo("SpiritBomb", TipoDeProjetil.Blast) == SomDeKi.Genkidama
				   && SonsDeKi.DoDisparo("Final_Flash", TipoDeProjetil.Beam) == SomDeKi.RaioBasico
				   && SonsDeKi.DoDisparo("Galic_Gun_que_nao_existe", TipoDeProjetil.Beam) == SomDeKi.Raio
				   && SonsDeKi.DoDisparo("", TipoDeProjetil.Guided) == SomDeKi.Queima
				   && SonsDeKi.DoDisparo("Kiai", TipoDeProjetil.Blast) == SomDeKi.Nenhum);

		SonsDeKi.TiroSemSomDeTeste = true;
		try
		{
			LimparTudoDaBancada();
			ServerPlayer mudo = ForjarV2("TiroMudo", mao, 10_000);
			Projetil calado = Disparar(mudo, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6 }, verbo: "Basic_Blast");
			AfirmarV21("(defeito injetado: o tiro sem som de ki) a bola nasce sem som nenhum no pacote -- a v0.2.0 (o cliente tocava o baque de soco)", calado.Som == SomDeKi.Nenhum, calado.Som.ToString());
		}
		finally { SonsDeKi.TiroSemSomDeTeste = false; }

		// ---- a tecla X
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		ServerPlayer deX = ForjarV2("TeclaX", mao, 10_000);
		int Descidas(Action gesto)
		{
			EscutaDeGestos = [];
			gesto();
			int n = EscutaDeGestos.Count(g => g.Quem == deX.Id && g.Gesto == (byte)Protocol.GestoDoCorpo.Descer);
			EscutaDeGestos = null;
			return n;
		}

		deX.Ficha.Ki = deX.Ficha.MaxKi;
		int naBase = Descidas(() => ReverterOuLiberar(deX));
		deX.Ficha.Ki = deX.Ficha.MaxKi * 1.5;
		int liberando = Descidas(() => ReverterOuLiberar(deX));
		AfirmarV21("**A TECLA X SOA** quando libera o Ki que excedia: o gesto `Descer` (`descend.wav`, `Power Control.dm:68`) sai pra zona, e o Ki volta a 100%",
				   liberando == 1 && Math.Abs(deX.Ficha.Ki - deX.Ficha.MaxKi) < 1e-6, $"{liberando} gesto(s); ki {deX.Ficha.Ki:0.#}/{deX.Ficha.MaxKi:0.#}");
		AfirmarV21("...e fica CALADA quando nao ha o que descer (na base, com o Ki em 100%)", naBase == 0, $"{naBase} gesto(s)");
		XSemSomDeTeste = true;
		try
		{
			deX.Ficha.Ki = deX.Ficha.MaxKi * 1.5;
			int muda = Descidas(() => ReverterOuLiberar(deX));
			AfirmarV21("(defeito injetado: o X sem som) o Ki desce e nenhum gesto sai -- a v0.2.0", muda == 0 && Math.Abs(deX.Ficha.Ki - deX.Ficha.MaxKi) < 1e-6, $"{muda} gesto(s)");
		}
		finally { XSemSomDeTeste = false; }

		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}
}
