using Godot;
using Jandirus.Core.Items;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;
using NivelDeRaiva = Jandirus.Core.Forms.NivelDeRaiva;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DE TELA DA VERSAO 0.3.0 (`--diagv030`) -- o pedido do dono de 2026-10-15, do lado de ca do fio.
///
/// A irma de servidor (`--v030teste`) prova as regras. Esta prova o que so existe na TELA e PELO FIO, com o defeito
/// do jogo de antes injetado em cada familia:
///
///   R  A RAIVA EMBAIXO DA VIDA: o painel do canto escreve "COM RAIVA" e "FURIOSO", e some calmo (item 4);
///   T  O RABO QUE NAO VOLTA: com a ferida aberta o boneco do HUD diz "rabo: ARRANCADO"; cicatrizada, a linha some
///      (itens 3 e 19);
///   C  OS AVISOS PINTADOS: o de raiva e o de zenkai chegam no tom proprio, moram na aba Sistema e saem com a cor deles
///      (item 20);
///   W  OS PESOS COM A ARTE ESCOLHIDA: o icone da mochila e o desenho no chao sao os da arte, e o painel oferece "Ate o
///      limite do corpo" (itens 9 e 10);
///   Q  OS PESOS QUE LEMBRAM: o "Equipar" da mochila diz a porcentagem guardada e veste com ela (item 28);
///   M  O MAJIN EM PEDACOS: o corpo some, os pedacos da `Majin1` ficam em volta, e na volta a reconstrucao toca a
///      `regenerate` da folha do corpo -- ou a da `Majin1`, se a dele nao tiver (item 2b);
///   X  O GIGANTE E A CHAMA: o Namekuseijin gigante dobra na tela de quem ve, e a aura acompanha o tamanho do corpo -- o
///      gigante e o Oozaru (itens 14 e 15);
///   A  O GOLPE AUTOMATICO: o golpe que a tecnica da por mim poe o MEU corpo na pose de soco, virado pro alvo (item 21);
///   J  O PASSE DO ENMA: o verbo do menu E me poe na Terra morto e de pe -- o meu corpo nao se deita (item 11);
///   P  O MORTO NA MEDITACAO PROFUNDA: pela tecla M e pela telinha, o fantasma entra na propria mente e fica, de pe (item 26);
///   G  O CHUTE GIRATORIO: o meu boneco passa pelas quatro direcoes e volta pra onde olhava (item 30);
///   RA O MODO DAS RACAS: o cartao da aba Admin muda o modo do servidor pelo fio, e a criacao abre com a raca sorteada --
///      no berco, sem mundo, raca nem linhagem (item 34); a VAGA nao conta o sorteio, so a criacao (item 45);
///   CS O CHEFE DE SAGA: a cena da chegada aparece na minha tela com o chefe noutro planeta (e nao me prende), e a troca de
///      corpo do Freeza ao meu lado tem cena -- o corpo novo so na virada (item 35);
///   MI AS MIRAGENS DA TECNICA: a da Zanzoken Afterimage vive na tela mais que a do duplo clique (item 33);
///   F  O CHEFE DE SAGA CACA O PLANETA: o Freeza da saga me escolhe a trinta tiles (item 5);
///   S  A SUPERNOVA: o ultimato do Freeza, pela porta de producao -- o sol nasce, cresce, cai e explode na minha tela, e
///      ele intocavel o tempo todo (item 22);
///   KK O KAIO-KEN: o botao abre a escolha com a maestria escrita, o multiplicador escolhido acende pelo fio, a tela reabre
///      no ultimo aceso, e o X apaga (item 37);
///   GK A GENKIDAMA QUE NAO TELEPORTA: lancada, o corpo fica preso na minha tela tambem -- a tecla de andar nao o tira do
///      lugar -- e, solto, anda de novo; com janela, as fotos dela em cima da cabeca e saindo na frente (itens 38 e 42);
///   TR O REFUGIO PELA TERRA: o meu natal acaba, a tela do refugio abre sozinha com o botao "Na Terra" ao lado da
///      vizinhanca, o botao escolhe pelo fio e a vizinhanca desfaz (adendo 11) -- e a pergunta aparece UMA vez: a oferta do
///      login seguinte nao a empurra, o cartao do menu P a abre, e o natal que volta apaga a marca (item 39);
///   L  A REENCARNACAO: o mundo cai, o cliente disca de novo e a CRIACAO abre na vaga que vagou, com a frase do Enma; o
///      novo nasce com 10% do poder (item 7). E a ULTIMA familia: ela troca o personagem.
///
/// O QUE ELA NAO PROVA: pixel nenhum. Sem janela o que se mede e a INTENCAO -- o texto do painel, o canal da linha, a
/// textura que o slot recebeu, a folha que o desenho do chao usou.
///
///     Godot --headless --path . --host --rede 8423 --campoteste 8 --horateste 0.5 --semfauna --diagv030 --raca Saiyan --conta bancada_dvtrinta --nome Trinta
/// </summary>
public partial class RoboDaV030 : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dv030] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>
	/// Ja ha um robo vivo? A familia L (a reencarnacao) derruba o mundo e o monta de novo, e o `MontarMundo` criaria um
	/// SEGUNDO robo rodando o roteiro do comeco por cima do primeiro -- achado na rodada do T24. O `RoboDeQueda` tem a mesma trava.
	/// </summary>
	public static bool Vivo { get; private set; }

	public override void _Ready()
	{
		Vivo = true;
		ProcessPriority = -100;
		_ = Rodar();
	}

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void Encerrar(int codigo)
	{
		// O MUNDO VOLTA: nenhum botao de defeito sobrevive ao roteiro.
		Hud.RaivaMudaDeTeste = false;
		Jandirus.Core.Combat.Feridas.RaboPerdidoSangraPraSempreDeTeste = false;
		Jandirus.Server.GameServer.AvisoSemTomDeTeste = false;
		PesoDeTreino.ArteSoNoCorpoDeTeste = false;
		PedacosDoMajin.CorpoInteiroNaTelaDeTeste = false;
		CharacterVisual.GiganteSoNoServidorDeTeste = false;
		SpriteDeAura.ChamaDeTamanhoFixoDeTeste = false;
		LocalPlayer.GolpeDoServidorSemPoseDeTeste = false;
		LocalPlayer.VisitaDeitaDeTeste = false;
		GameClient.DespedidaViraQuedaDeTeste = false;
		SupernovaDesenhada.SolIgnoradoDeTeste = false;
		Jandirus.Server.GameServer.MortoNaoMergulhaDeTeste = false;
		Jandirus.Core.World.Alem.MenteDeitaOMortoDeTeste = false;
		PesoDeTreino.PorcentagemEsquecidaDeTeste = false;
		CharacterVisual.GiroSoNoServidorDeTeste = false;
		Zanzoken.MiragemDaTecnicaCurtaDeTeste = false;
		CreationScreen.EtapasDoSorteioVisiveisDeTeste = false;
		CharacterSelect.SorteioNaVagaDeTeste = false;
		Jandirus.Server.GameServer.ChegadaSemCenaDeTeste = false;
		Jandirus.Server.GameServer.ChefeSemCenaDeTeste = false;
		Jandirus.Server.GameServer.TerraForaDoRefugioDeTeste = false;
		Jandirus.Server.GameServer.XNaoApagaOKaiokenDeTeste = false;
		LocalPlayer.TravaDaTecnicaSoNoServidorDeTeste = false;
		Jandirus.Server.GameServer.RefugioSempreAbreDeTeste = false;

		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" A VERSAO 0.3.0 NA TELA -- o pedido de 2026-10-15");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 } && c.LocalId != 0
												  && S != null && World.Instancia != null && Hud.Instancia != null && TelaDeInventario.Instancia != null
												  && Chat.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, HUD, mochila, chat) e o servidor e deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || Hud.Instancia is not { } hud || TelaDeInventario.Instancia is not { } mochila)
		{ Encerrar(2); return; }
		await Segundos(1.0);

		await R_ARaivaNoHud(cli, srv, hud);
		await T_ORaboQueCicatriza(cli, srv, hud);
		await C_OsAvisosPintados(cli, srv);
		await W_OsPesosComArte(cli, srv, mochila);
		await Q_OsPesosQueLembram(cli, srv, mochila);
		await G_OGiro(cli, srv);
		await MI_AsMiragensDuram(cli, srv);
		await RA_OModoDasRacas(cli, srv);
		await CS_OChefeDeSaga(cli, srv);
		await M_OMajinEmPedacos(cli, srv);
		await X_OGiganteEAChama(cli, srv);
		await A_OGolpeAutomatico(cli, srv);
		await J_OPasseDoEnma(cli, srv);
		await P_OMortoMergulha(cli, srv);
		await F_OChefeCaca(cli, srv);
		await S_ASupernova(cli, srv);
		await KK_OKaioken(cli, srv);
		await GK_AGenkidamaNaoTeleporta(cli, srv);
		await TR_ORefugioPelaTerra(cli, srv);
		await L_AReencarnacao(cli, srv);   // a ULTIMA: ela troca o personagem

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// R) A RAIVA EMBAIXO DA VIDA (item 4)
	// =====================================================================
	private async Tarefa R_ARaivaNoHud(GameClient cli, Jandirus.Server.GameServer srv, Hud hud)
	{
		Nota("--- R: a raiva embaixo da vida ---");
		try
		{
			srv.PalcoV30_Raiva(cli.LocalId, NivelDeRaiva.Comum);
			bool comum = await Ate(() => hud.TextoDaRaivaDeTeste == "COM RAIVA", 3);
			srv.PalcoV30_Raiva(cli.LocalId, NivelDeRaiva.Extrema);
			bool furioso = await Ate(() => hud.TextoDaRaivaDeTeste == "FURIOSO", 3);
			srv.PalcoV30_Raiva(cli.LocalId, NivelDeRaiva.Nenhuma);
			bool sumiu = await Ate(() => hud.TextoDaRaivaDeTeste == "", 3);
			Checa("**A RAIVA APARECE EMBAIXO DA VIDA**: \"COM RAIVA\" na raiva comum, \"FURIOSO\" na extrema -- e calmo a linha some",
				  comum && furioso && sumiu, $"comum {comum}; furioso {furioso}; sumiu {sumiu}; agora '{hud.TextoDaRaivaDeTeste}'");

			Hud.RaivaMudaDeTeste = true;
			srv.PalcoV30_Raiva(cli.LocalId, NivelDeRaiva.Extrema);
			await Segundos(0.8);
			hud.RedesenharOPoderEmUsoDeTeste();
			Injeta("(o painel mudo) furioso, e o canto da tela nao diz nada -- o jogo de antes, em que so o chat contava", hud.TextoDaRaivaDeTeste == "");
		}
		finally
		{
			Hud.RaivaMudaDeTeste = false;
			srv.PalcoV30_Raiva(cli.LocalId, NivelDeRaiva.Nenhuma);
		}
		await Ate(() => hud.TextoDaRaivaDeTeste == "", 3);
	}

	// =====================================================================
	// T) O RABO QUE NAO VOLTA (itens 3 e 19)
	// =====================================================================
	private async Tarefa T_ORaboQueCicatriza(GameClient cli, Jandirus.Server.GameServer srv, Hud hud)
	{
		Nota("--- T: o rabo que nao volta cicatriza, e o aviso sai do boneco ---");
		if (hud.FindChild("Corpo", true, false) is not BodyDoll boneco) { Checa("(montagem) o boneco do HUD existe", false); return; }
		try
		{
			bool arrancou = srv.PalcoV30_RaboPerdido(cli.LocalId, 600);
			bool aberta = await Ate(() => boneco.TextoDoRaboDeTeste == "rabo: ARRANCADO", 3);
			srv.PalcoV30_PassarOTempoDaFerida(601);
			bool sumiu = await Ate(() => boneco.TextoDoRaboDeTeste == "", 3);
			Checa("**O RABO QUE NAO VOLTA CICATRIZA**: com a ferida aberta o boneco diz \"rabo: ARRANCADO\"; passados os dez minutos, a linha sai -- o corpo de agora nao tem rabo",
				  arrancou && aberta && sumiu, $"arrancou {arrancou}; aberta {aberta}; sumiu {sumiu}; agora '{boneco.TextoDoRaboDeTeste}'");

			Jandirus.Core.Combat.Feridas.RaboPerdidoSangraPraSempreDeTeste = true;
			srv.PalcoV30_RaboPerdido(cli.LocalId, 600);
			srv.PalcoV30_PassarOTempoDaFerida(601);
			await Segundos(1.0);
			Injeta("(o rabo sangra pra sempre) dez minutos depois o boneco ainda diz \"rabo: ARRANCADO\" -- *\"esse aviso deveria sumir pq n tem necessidade\"*",
				   boneco.TextoDoRaboDeTeste == "rabo: ARRANCADO", $"agora '{boneco.TextoDoRaboDeTeste}'");
		}
		finally
		{
			Jandirus.Core.Combat.Feridas.RaboPerdidoSangraPraSempreDeTeste = false;
			srv.PalcoV30_DevolverORabo(cli.LocalId);
		}
		await Segundos(0.5);
	}

	// =====================================================================
	// C) OS AVISOS PINTADOS (item 20)
	// =====================================================================
	private async Tarefa C_OsAvisosPintados(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- C: os avisos de raiva e de zenkai chegam pintados ---");
		bool Chegou(string texto, Protocol.Fala canal) => Chat.LinhasDeTeste().Exists(l => l.Texto == texto && l.Canal == canal);
		try
		{
			srv.PalcoV30_Avisar(cli.LocalId, "dv030: a raiva da bancada", Protocol.Fala.AvisoDeRaiva);
			srv.PalcoV30_Avisar(cli.LocalId, "dv030: o zenkai da bancada", Protocol.Fala.AvisoDeZenkai);
			bool raiva = await Ate(() => Chegou("dv030: a raiva da bancada", Protocol.Fala.AvisoDeRaiva), 3);
			bool zenkai = await Ate(() => Chegou("dv030: o zenkai da bancada", Protocol.Fala.AvisoDeZenkai), 3);
			string linhaDaRaiva = Chat.FormatarDeTeste(Protocol.Fala.AvisoDeRaiva, "x"), linhaDoZenkai = Chat.FormatarDeTeste(Protocol.Fala.AvisoDeZenkai, "x");
			string linhaDeSistema = Chat.FormatarDeTeste(Protocol.Fala.Sistema, "x");
			bool cores = linhaDaRaiva.Contains(Chat.CorDaRaiva) && linhaDoZenkai.Contains(Chat.CorDoZenkai)
							   && !linhaDeSistema.Contains(Chat.CorDaRaiva) && !linhaDeSistema.Contains(Chat.CorDoZenkai);
			bool aba = Chat.AbaMostraDeTeste("Sistema", Protocol.Fala.AvisoDeRaiva) && Chat.AbaMostraDeTeste("Sistema", Protocol.Fala.AvisoDeZenkai)
							 && !Chat.AbaMostraDeTeste("Fala", Protocol.Fala.AvisoDeRaiva);
			Checa("**A RAIVA E O ZENKAI CHEGAM PINTADOS**: os dois avisos atravessam o fio no tom proprio, moram na aba Sistema e saem com a cor deles (o recado comum continua na cor de sempre)",
				  raiva && zenkai && cores && aba, $"raiva {raiva}; zenkai {zenkai}; cores {cores}; aba {aba}");
			// ...E COM A SETA DO RECADO DO SERVIDOR NA FRENTE. A primeira versao saiu com um TAB no lugar dela (o atalho de TAB
			// dos scripts de remendo comeu o caractere), e nenhuma linha daqui olhava pra seta.
			bool ComASeta(string linha) => linha.Contains("\u00bb x") && !linha.Contains('\t');
			Checa("...e os dois com a seta \"\u00bb\" na frente, como o recado comum -- sem o TAB que a primeira versao deixou no lugar dela",
				  ComASeta(linhaDaRaiva) && ComASeta(linhaDoZenkai) && linhaDeSistema.Contains("\u00bb"),
				  $"raiva '{linhaDaRaiva}'; zenkai '{linhaDoZenkai}'");
			Checa("   (contra-exemplo: a linha da primeira versao, com o TAB) a mesma regua a reprova",
				  !ComASeta($"[color=#{Chat.CorDaRaiva}]\t x[/color]"));

			Jandirus.Server.GameServer.AvisoSemTomDeTeste = true;
			srv.PalcoV30_Avisar(cli.LocalId, "dv030: a raiva sem tom", Protocol.Fala.AvisoDeRaiva);
			bool semTom = await Ate(() => Chegou("dv030: a raiva sem tom", Protocol.Fala.Sistema), 3);
			Injeta("(o aviso sem tom) a raiva volta a chegar como um recado qualquer, na cor de \"zona carregada\"", semTom);
		}
		finally { Jandirus.Server.GameServer.AvisoSemTomDeTeste = false; }
	}

	// =====================================================================
	// W) OS PESOS COM A ARTE ESCOLHIDA (itens 9 e 10)
	// =====================================================================
	private async Tarefa W_OsPesosComArte(GameClient cli, Jandirus.Server.GameServer srv, TelaDeInventario mochila)
	{
		Nota("--- W: os pesos com a arte escolhida na mochila e no chao, e o limite do corpo ---");
		const string pesos = CatalogoDeItens.Pesos;
		srv.PalcoV21_Limpar(cli.LocalId);
		srv.PalcoV21_Dar(cli.LocalId, pesos);
		bool temPesos = await Ate(() => cli.Mochila.Quantos(pesos) == 1, 4);
		(string nome, string caminho) = srv.PalcoV30_ArteDosPesos(cli.LocalId);
		bool chegouAArte = await Ate(() => cli.Atributos.PesoArte == nome, 4);
		Checa("(montagem) os pesos na mochila, e uma arte que nao e a de fabrica escolhida pela grade (chegou pela ficha lenta)",
			  temPesos && nome.Length > 0 && chegouAArte, $"pesos {temPesos}; arte '{nome}' ({caminho}); na ficha '{cli.Atributos.PesoArte}'");
		if (!temPesos || nome.Length == 0) return;

		ItemDef def = CatalogoDeItens.Get(pesos)!;
		Texture2D? daArte = Miniaturas.De(caminho, PesoDeTreino.PoseDoIcone), deFabrica = Miniaturas.DoItem(def);
		try
		{
			mochila.Abrir();
			await Quadros(3);
			Texture2D? noSlot = mochila.IconeDoSlotDeTeste(pesos);
			bool limite = mochila.AbrirAcoesDeTeste(pesos)
							  && mochila.AcoesAbertasDeTeste().Exists(a => a.Texto.StartsWith("Até o limite do corpo", StringComparison.Ordinal));
			mochila.Fechar();
			await Quadros(2);
			Checa("**O ICONE DOS PESOS NA MOCHILA E O DA ARTE ESCOLHIDA** (e nao a camisa de fabrica) -- e o painel deles oferece \"Até o limite do corpo\"",
				  daArte != null && noSlot != null && ReferenceEquals(noSlot, daArte) && !ReferenceEquals(noSlot, deFabrica) && limite,
				  $"slot {noSlot?.ResourcePath}; da arte {daArte?.ResourcePath}; de fabrica {deFabrica?.ResourcePath}; limite {limite}");

			PesoDeTreino.ArteSoNoCorpoDeTeste = true;
			mochila.Abrir();
			await Quadros(3);
			Texture2D? comODefeito = mochila.IconeDoSlotDeTeste(pesos);
			mochila.Fechar();
			PesoDeTreino.ArteSoNoCorpoDeTeste = false;
			await Quadros(2);
			Injeta("(a arte so no corpo) a mochila volta a mostrar a camisa de fabrica -- *\"o icone do peso no inventario (...) n muda quando vc muda a aparencia\"*",
				   comODefeito != null && ReferenceEquals(comODefeito, deFabrica));

			// O CHAO: largar pela mochila, como o jogador
			mochila.Abrir();
			await Quadros(3);
			bool largou = mochila.AbrirAcoesDeTeste(pesos) && mochila.ApertarAcaoDeTeste("Largar no chão");
			mochila.Fechar();
			bool caiu = await Ate(() => cli.Largados.Exists(l => l.Item == pesos), 4);
			GameClient.LargadoInfo noChao = cli.Largados.Find(l => l.Item == pesos);
			await Quadros(3);
			var desenho = World.Instancia?.FindChild("Largado" + noChao.Id, true, false) as LargadoDesenhado;
			Checa("**OS PESOS LARGADOS CAEM COM A ARTE ESCOLHIDA**: a folha viaja na lista do chao, e o desenho no mundo a usa",
				  largou && caiu && noChao.Arte == caminho && desenho?.ArteUsadaDeTeste == caminho,
				  $"largou {largou}; caiu {caiu}; no fio '{noChao.Arte}'; desenhada '{desenho?.ArteUsadaDeTeste}'");
		}
		finally
		{
			PesoDeTreino.ArteSoNoCorpoDeTeste = false;
			srv.PalcoV30_SemArteDosPesos(cli.LocalId);
			srv.PalcoV21_Limpar(cli.LocalId);
		}
	}

	// =====================================================================
	// Q) OS PESOS QUE LEMBRAM A PORCENTAGEM (item 28)
	// =====================================================================
	private async Tarefa Q_OsPesosQueLembram(GameClient cli, Jandirus.Server.GameServer srv, TelaDeInventario mochila)
	{
		Nota("--- Q: o \"Equipar\" dos pesos diz a porcentagem guardada e veste com ela (item 28) ---");
		const string pesos = CatalogoDeItens.Pesos;
		srv.PalcoV21_Limpar(cli.LocalId);
		srv.PalcoV30_EsquecerAPorcentagem(cli.LocalId);
		srv.PalcoV21_Dar(cli.LocalId, pesos);
		bool temPesos = await Ate(() => cli.Mochila.Quantos(pesos) == 1 && cli.Atributos.PesoPct == 0, 4);
		Checa("(montagem) os pesos na mochila, nada vestido e nenhuma porcentagem guardada", temPesos && cli.Atributos.PesoKg == 0,
			  $"pesos {temPesos}; vestindo {cli.Atributos.PesoKg}; guardada {cli.Atributos.PesoPct}");
		if (!temPesos) return;
		try
		{
			// o que o teclado do "Ajustar o peso" manda, e o "Tirar"
			cli.SendVerbo("item_ajustar", $"{pesos}/40");
			bool ajustou = await Ate(() => cli.Atributos.PesoKg > 0 && cli.Atributos.PesoPct == 40, 6);
			cli.SendVerbo("item_tirar", pesos);
			bool tirou = await Ate(() => cli.Atributos.PesoKg == 0, 6);
			Checa("(montagem) ajustados em 40% pelo verbo do teclado e tirados -- a porcentagem guardada chegou pela ficha lenta",
				  ajustou && tirou && cli.Atributos.PesoPct == 40, $"ajustou {ajustou}; tirou {tirou}; guardada {cli.Atributos.PesoPct}");

			mochila.Abrir();
			await Quadros(3);
			bool abriu = mochila.AbrirAcoesDeTeste(pesos);
			string? rotulo = mochila.AcoesAbertasDeTeste().Find(a => a.Texto.StartsWith("Equipar", StringComparison.Ordinal)).Texto;
			bool apertou = abriu && mochila.ApertarAcaoDeTeste("Equipar");
			mochila.Fechar();
			bool vestiu = await Ate(() => cli.Atributos.PesoKg > 0, 6);
			double esperado = 0.4 * PesoDeTreino.Maximo(cli.Atributos.PesoMaxKg);
			Checa("**O \"EQUIPAR\" DA MOCHILA DIZ A PORCENTAGEM E VESTE COM ELA** (*\"vc so precisaria clicar na opcao 'equipar' pra por o peso ao inves de colocar a % toda vez\"*): \"Equipar (40% do máximo)\", e um clique veste os mesmos 40%",
				  rotulo == "Equipar (40% do máximo)" && apertou && vestiu && Math.Abs(cli.Atributos.PesoKg - esperado) < 0.6,
				  $"rotulo '{rotulo}'; apertou {apertou}; vestindo {cli.Atributos.PesoKg:N1} de {esperado:N1} kg");

			// O JOGO DE ANTES: a % nao fica guardada -- o botao volta a pedir o numero
			cli.SendVerbo("item_tirar", pesos);
			await Ate(() => cli.Atributos.PesoKg == 0, 6);
			srv.PalcoV30_EsquecerAPorcentagem(cli.LocalId);
			await Ate(() => cli.Atributos.PesoPct == 0, 4);
			PesoDeTreino.PorcentagemEsquecidaDeTeste = true;
			cli.SendVerbo("item_ajustar", $"{pesos}/40");
			await Ate(() => cli.Atributos.PesoKg > 0, 6);
			cli.SendVerbo("item_tirar", pesos);
			await Ate(() => cli.Atributos.PesoKg == 0, 6);
			PesoDeTreino.PorcentagemEsquecidaDeTeste = false;
			mochila.Abrir();
			await Quadros(3);
			mochila.AbrirAcoesDeTeste(pesos);
			string? comDefeito = mochila.AcoesAbertasDeTeste().Find(a => a.Texto.StartsWith("Equipar", StringComparison.Ordinal)).Texto;
			mochila.Fechar();
			Injeta("(a % nao fica guardada) o botao volta a pedir a porcentagem -- \"Equipar (escolha a %)\", o jogo de antes", comDefeito == "Equipar (escolha a %)",
				   $"rotulo '{comDefeito}'");
		}
		finally
		{
			PesoDeTreino.PorcentagemEsquecidaDeTeste = false;
			if (cli.Atributos.PesoKg > 0) { cli.SendVerbo("item_tirar", pesos); await Ate(() => cli.Atributos.PesoKg == 0, 6); }
			srv.PalcoV21_Limpar(cli.LocalId);
			srv.PalcoV30_EsquecerAPorcentagem(cli.LocalId);
		}
	}

	// =====================================================================
	// M) O MAJIN DESFEITO EM PEDACOS (item 2b)
	// =====================================================================
	private async Tarefa M_OMajinEmPedacos(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- M: o Majin desfeito em pedacos, e o corpo que se refaz ---");
		int id = srv.PalcoV21_Figurante(cli.LocalId, "Majin", "", 3);
		RemotePlayer? Corpo() => World.Instancia?.FindChild($"Remoto{id}", true, false) as RemotePlayer;
		CharacterVisual? Boneco() => Corpo()?.GetNodeOrNull<CharacterVisual>("Visual");
		PedacosDoMajin? Pedacos() => Corpo()?.GetNodeOrNull<PedacosDoMajin>(PedacosDoMajin.NomeDoNode);

		bool chegou = id != 0 && await Ate(() => Boneco() is { Visible: true } b && b.CorpoParaOsPedacos() != null, 5);
		Checa("(montagem) o figurante Majin apareceu ao lado, de pe e desenhado", chegou, $"id {id}");
		if (!chegou)
		{
			if (id != 0) srv.PalcoV21_TirarOFigurante(id);
			return;
		}
		try
		{
			SpriteFrames? folhaDoCorpo = Boneco()?.CorpoParaOsPedacos()?.Folha;

			srv.PalcoV30_Desfazer(id);
			bool emPedacos = await Ate(() => Pedacos()?.PedacosDeTeste().Count == PedacosDoMajin.Quantos && Boneco() is { Visible: false }, 3);
			List<string> anims = Pedacos()?.PedacosDeTeste() ?? [];
			Checa("**O MAJIN SE DESFAZ NA TELA**: o corpo some e cinco pedacos da `Majin1` (os tres desenhos de pedaco) ficam espalhados em volta",
				  emPedacos && anims.All(a => a.StartsWith("chunk", StringComparison.Ordinal)),
				  $"pedacos [{string.Join(", ", anims)}]; boneco visivel {Boneco()?.Visible}");

			// A VOLTA: os pedacos andam pro corpo, a reconstrucao toca, e o corpo reaparece
			srv.PalcoV30_Refazer(id);
			(SpriteFrames? Folha, string Anim) refazendo = (null, "");
			bool tocou = await Ate(() =>
			{
				if (Pedacos() is { } p && p.RefazendoDeTeste.Anim.Length > 0) refazendo = p.RefazendoDeTeste;
				return refazendo.Anim.Length > 0;
			}, 3);
			bool voltou = await Ate(() => Pedacos() == null && Boneco() is { Visible: true }, 4);
			SpriteFrames? esperada = PedacosDoMajin.FolhaDaReconstrucao(folhaDoCorpo);
			Checa("**O CORPO SE REFAZ**: os pedacos voltam, a `regenerate` da folha do corpo toca uma vez no lugar dele, e ele reaparece",
				  tocou && refazendo.Anim == PedacosDoMajin.AnimDaReconstrucao && esperada != null && ReferenceEquals(refazendo.Folha, esperada) && voltou,
				  $"tocou {tocou} ({refazendo.Folha?.ResourcePath} '{refazendo.Anim}'); esperada {esperada?.ResourcePath}; voltou {voltou}");

			// A FOLHA SEM A ANIMACAO: a reconstrucao troca pra da `Majin1`
			SpriteFrames? semAnimacao = FolhasPresas.Carregar("res://Assets/Sprites/Character Icons/Majins/Majin - Form 3.tres");
			SpriteFrames? troca = PedacosDoMajin.FolhaDaReconstrucao(semAnimacao);
			Checa("...e a folha de Majin que NAO tem a reconstrucao (a \"Majin - Form 3\") toca a da `Majin1` no lugar -- *\"se nao tiverem é so trocar o sprite pra outro\"*",
				  semAnimacao != null && !semAnimacao.HasAnimation(PedacosDoMajin.AnimDaReconstrucao) && troca?.ResourcePath == PedacosDoMajin.FolhaDosPedacos,
				  $"form 3 {(semAnimacao == null ? "sem folha" : semAnimacao.HasAnimation(PedacosDoMajin.AnimDaReconstrucao) ? "TEM a regenerate" : "sem a regenerate")}; troca {troca?.ResourcePath}");

			PedacosDoMajin.CorpoInteiroNaTelaDeTeste = true;
			srv.PalcoV30_Desfazer(id);
			await Segundos(1.0);
			Injeta("(a tela ignora o desfeito) o Majin \"morto\" por soco fica desenhado inteiro, caido -- sem os pedacos e sem a volta",
				   Pedacos() == null && Boneco() is { Visible: true });
		}
		finally
		{
			PedacosDoMajin.CorpoInteiroNaTelaDeTeste = false;
			srv.PalcoV30_Refazer(id);
			srv.PalcoV21_TirarOFigurante(id);
		}
	}

	// =====================================================================
	// X) O GIGANTE NA TELA, E A CHAMA DO TAMANHO DO CORPO (itens 14 e 15)
	// =====================================================================
	private async Tarefa X_OGiganteEAChama(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- X: o Namekuseijin gigante na tela, e a aura do tamanho do corpo (itens 14 e 15) ---");
		int id = srv.PalcoV21_Figurante(cli.LocalId, "Namekian", "", 3);
		RemotePlayer? Corpo() => World.Instancia?.FindChild($"Remoto{id}", true, false) as RemotePlayer;
		CharacterVisual? Boneco() => Corpo()?.GetNodeOrNull<CharacterVisual>("Visual");
		bool chegou = id != 0 && await Ate(() => Boneco() is { Visible: true }, 5);
		Checa("(montagem) o figurante Namekuseijin apareceu ao lado", chegou, $"id {id}");
		if (!chegou)
		{
			if (id != 0) srv.PalcoV21_TirarOFigurante(id);
			return;
		}
		bool gigante = false;
		try
		{
			// A TECNICA DE PRODUCAO, e o desenho de quem VE (o figurante e de outro: o bit viaja no `PeerLook`)
			gigante = srv.PalcoV30_Gigante(id);
			bool cresceu = await Ate(() => Boneco() is { } b && Mathf.IsEqualApprox(b.PorteDeTeste, CharacterVisual.PorteDoGigante), 3);
			float tamanho = Boneco()?.TamanhoDoCorpo ?? 0;
			Checa("**O NAMEKUSEIJIN GIGANTE CRESCE NA TELA**: a Forma Gigante liga no servidor e o boneco dele dobra pra quem esta do lado",
				  gigante && cresceu && Mathf.IsEqualApprox(tamanho, 2f), $"gigante {gigante}; porte {Boneco()?.PorteDeTeste}; tamanho {tamanho}");

			// A CHAMA DA FORMA, acesa no boneco gigante: dobra junto, com a base nos pes
			SpriteDeAura? chama = Corpo()?.GetNodeOrNull<Aura>("Aura")?.DesenhoDeTeste;
			chama?.Definir(true, Colors.White);
			await Quadros(3);
			bool dobrou = chama != null && Mathf.IsEqualApprox(chama.TamanhoDeTeste, 2f)
							  && Mathf.IsEqualApprox(chama.Position.Y, SpriteDeAura.LinhaDosPes * (1f - 2f));
			Checa("**A AURA ACOMPANHA O TAMANHO DO CORPO**: a chama do gigante dobra, e a base dela continua nos pes",
				  dobrou, $"chama {chama?.TamanhoDeTeste}; y {chama?.Position.Y}");

			SpriteDeAura.ChamaDeTamanhoFixoDeTeste = true;
			// REACESA: a `Aura` apaga a chama de bancada no relogio dela (sem forma acesa), e a chama mede o corpo ao acender
			chama?.Definir(false, Colors.White);
			chama?.Definir(true, Colors.White);
			await Quadros(1);
			Injeta("(a chama de tamanho fixo) o gigante volta a ter a aura de um corpo comum -- *\"a aura n muda de tamanho pra ficar proporcional\"*",
				   chama != null && Mathf.IsEqualApprox(chama.TamanhoDeTeste, 1f), $"chama {chama?.TamanhoDeTeste}");
			SpriteDeAura.ChamaDeTamanhoFixoDeTeste = false;
			chama?.Definir(false, Colors.White);

			// O DEFEITO DO ITEM 14: o boneco ignora o bit -- desligado e religado, o servidor diz gigante e o desenho nao muda
			CharacterVisual.GiganteSoNoServidorDeTeste = true;
			srv.PalcoV30_Gigante(id);
			await Ate(() => Boneco() is { } b && Mathf.IsEqualApprox(b.PorteDeTeste, 1f), 3);
			gigante = srv.PalcoV30_Gigante(id);
			await Segundos(1.0);
			Injeta("(o gigante so no servidor) a Forma Gigante liga e o boneco continua do tamanho de sempre -- *\"namek gigante n ta deixando o sprite gigante\"*",
				   gigante && Boneco() is { } semCrescer && Mathf.IsEqualApprox(semCrescer.PorteDeTeste, 1f), $"gigante {gigante}; porte {Boneco()?.PorteDeTeste}");
		}
		finally
		{
			CharacterVisual.GiganteSoNoServidorDeTeste = false;
			SpriteDeAura.ChamaDeTamanhoFixoDeTeste = false;
			if (gigante) srv.PalcoV30_Gigante(id);
			srv.PalcoV21_TirarOFigurante(id);
		}

		// O OOZARU: a arte de 96 px faz o corpo do triplo -- e a chama acompanha (outra forma gigante, a do pedido)
		int macaco = srv.PalcoV21_Figurante(cli.LocalId, "Saiyan", "", 3);
		RemotePlayer? CorpoDoMacaco() => World.Instancia?.FindChild($"Remoto{macaco}", true, false) as RemotePlayer;
		bool veio = macaco != 0 && await Ate(() => CorpoDoMacaco()?.GetNodeOrNull<CharacterVisual>("Visual") is { Visible: true }, 5);
		try
		{
			string forma = veio ? srv.PalcoV21_Forma(macaco, Jandirus.Core.Forms.Oozaru.IdRegular) : "";
			bool triplo = await Ate(() => CorpoDoMacaco()?.GetNodeOrNull<CharacterVisual>("Visual") is { } b && Mathf.IsEqualApprox(b.TamanhoDoCorpo, 3f), 4);
			SpriteDeAura? chamaDoMacaco = CorpoDoMacaco()?.GetNodeOrNull<Aura>("Aura")?.DesenhoDeTeste;
			chamaDoMacaco?.Definir(true, Colors.White);
			await Quadros(3);
			Checa("...e o OOZARU tambem: a arte de 96 px faz o corpo do triplo, e a chama dele e do triplo",
				  forma == Jandirus.Core.Forms.Oozaru.IdRegular && triplo && chamaDoMacaco != null && Mathf.IsEqualApprox(chamaDoMacaco.TamanhoDeTeste, 3f),
				  $"forma '{forma}'; tamanho {CorpoDoMacaco()?.GetNodeOrNull<CharacterVisual>("Visual")?.TamanhoDoCorpo}; chama {chamaDoMacaco?.TamanhoDeTeste}");
			chamaDoMacaco?.Definir(false, Colors.White);
		}
		finally
		{
			if (macaco != 0)
			{
				srv.PalcoV21_Forma(macaco, "");
				srv.PalcoV21_TirarOFigurante(macaco);
			}
		}
	}

	// =====================================================================
	// A) O GOLPE AUTOMATICO NA MINHA TELA (item 21)
	// =====================================================================
	private async Tarefa A_OGolpeAutomatico(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- A: o golpe que a tecnica da por mim soca na minha tela e olha pro alvo (item 21) ---");
		LocalPlayer? eu = World.Instancia?.FindChild("LocalPlayer", true, false) as LocalPlayer;
		int alvo = srv.PalcoV21_Figurante(cli.LocalId, "Human", "", 3);
		bool chegou = eu != null && alvo != 0 && await Ate(() => World.Instancia?.FindChild($"Remoto{alvo}", true, false) is RemotePlayer, 5);
		Checa("(montagem) o corpo local e um alvo ao lado (a LESTE, tres tiles; duas fileiras abaixo)", chegou, $"alvo {alvo}");
		if (!chegou || eu == null)
		{
			if (alvo != 0) srv.PalcoV21_TirarOFigurante(alvo);
			return;
		}
		try
		{
			// o meu corpo olhando pro NORTE, fora de qualquer soco
			eu.PosarDeGolpe(0.01, Jandirus.Core.World.Facing.North);
			await Ate(() => !eu.PosandoGolpeDeTeste, 2);
			srv.PalcoV30_GolpeDeTecnica(cli.LocalId, alvo);
			bool posou = await Ate(() => eu.PosandoGolpeDeTeste, 2);
			Jandirus.Core.World.Facing olhar = eu.OlharDeTeste;
			Checa("**O GOLPE AUTOMATICO SOCA NA MINHA TELA**: o relato do golpe que a tecnica deu por mim poe o meu corpo na pose de soco, virado pro alvo (a LESTE)",
				  posou && olhar == Jandirus.Core.World.Facing.East, $"posou {posou}; olhar {olhar}");

			eu.PosarDeGolpe(0.01, Jandirus.Core.World.Facing.North);
			await Ate(() => !eu.PosandoGolpeDeTeste, 2);
			LocalPlayer.GolpeDoServidorSemPoseDeTeste = true;
			srv.PalcoV30_GolpeDeTecnica(cli.LocalId, alvo);
			await Segundos(0.5);
			Injeta("(o corpo local ignora o golpe do servidor) a tecnica bate e o meu boneco fica parado, olhando pro norte -- o jogo de antes",
				   !eu.PosandoGolpeDeTeste && eu.OlharDeTeste == Jandirus.Core.World.Facing.North, $"posando {eu.PosandoGolpeDeTeste}; olhar {eu.OlharDeTeste}");
		}
		finally
		{
			LocalPlayer.GolpeDoServidorSemPoseDeTeste = false;
			srv.PalcoV21_TirarOFigurante(alvo);
		}
	}

	// =====================================================================
	// J) O PASSE DO ENMA NA MINHA TELA (item 11)
	// =====================================================================
	private async Tarefa J_OPasseDoEnma(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- J: o morto com o passe do Enma desce na Terra de pe -- na minha tela ele nao e o cadaver deitado (item 11) ---");
		bool naMesa = srv.PalcoV30_MortoAoLadoDoEnma(cli.LocalId, Jandirus.Core.World.Alem.PrecoDoPasseDoEnma);
		bool morto = naMesa && await Ate(() => cli.Sheet.Morto && Jandirus.Core.World.Alem.EhOAlem(cli.Zone.Name), 15);
		Checa("(montagem) morto, ao lado do Enma, com os 100.000 zeni", morto, $"na mesa {naMesa}; zona {cli.Zone.Name}");
		if (!morto) { srv.PalcoV30_Reviver(cli.LocalId); return; }
		try
		{
			// o que o menu E manda: o verbo do passe com o nome escrito na caixa de texto
			cli.SendVerbo("enma_passe", "Terra");
			bool desceu = await Ate(() => cli.Zone.Name == "Earth" && cli.Sheet.Morto && cli.Sheet.EmVisita, 20);
			Checa("**O PASSE NA MINHA TELA**: o verbo do menu E (\"Terra\") me poe na Terra MORTO, com o bit do passe na ficha -- e o meu corpo nao se ve deitado (`LocalPlayer.Caido`)",
				  desceu && !LocalPlayer.Caido(cli.Sheet),
				  $"zona {cli.Zone.Name}; morto {cli.Sheet.Morto}; visita {cli.Sheet.EmVisita}; caido {LocalPlayer.Caido(cli.Sheet)}");

			LocalPlayer.VisitaDeitaDeTeste = true;
			bool caido = LocalPlayer.Caido(cli.Sheet);
			LocalPlayer.VisitaDeitaDeTeste = false;
			Injeta("(o cliente ignora o bit do passe) o morto que visita os vivos se veria deitado na propria tela, como o cadaver -- o jogo de antes", caido);
		}
		finally
		{
			LocalPlayer.VisitaDeitaDeTeste = false;
			srv.PalcoV30_Reviver(cli.LocalId);
			await Ate(() => !cli.Sheet.Morto, 10);
		}
	}

	// =====================================================================
	// G) O CHUTE GIRATORIO DA A VOLTA INTEIRA NA MINHA TELA (item 30)
	// =====================================================================
	private async Tarefa G_OGiro(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- G: o chute giratorio da a volta inteira na minha tela -- as quatro direcoes, e de volta pra onde eu olhava (item 30) ---");
		if (World.Instancia?.CorpoDeTeste(cli.LocalId)?.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis)
		{ Checa("(montagem) o meu boneco esta na tela", false); return; }

		// UMA VOLTA, olhada quadro a quadro por um segundo (ela e de 0,4 s): cada direcao NOVA desenhada entra na lista.
		async System.Threading.Tasks.Task<List<Jandirus.Core.World.Facing>> UmGiro()
		{
			await Segundos(0.3);
			var vistas = new List<Jandirus.Core.World.Facing> { vis.FacingDesenhadoDeTeste };
			srv.PalcoV30_Giro(cli.LocalId);
			ulong fim = Time.GetTicksMsec() + 1000;
			while (Time.GetTicksMsec() < fim)
			{
				await Quadros(1);
				Jandirus.Core.World.Facing agora = vis.FacingDesenhadoDeTeste;
				if (agora != vistas[^1]) vistas.Add(agora);
			}
			return vistas;
		}

		List<Jandirus.Core.World.Facing> giro = await UmGiro();
		bool volta = giro.Count == 5 && giro[0] == giro[4] && giro.Take(4).Distinct().Count() == 4;
		Checa("**A VOLTA INTEIRA NA MINHA TELA** (*\"faca o personagem realmente dar um 360 pra representar o giro\"*): o meu boneco passa pelas QUATRO direcoes e volta pra onde olhava",
			  volta, string.Join(" -> ", giro));

		CharacterVisual.GiroSoNoServidorDeTeste = true;
		List<Jandirus.Core.World.Facing> parado;
		try { parado = await UmGiro(); }
		finally { CharacterVisual.GiroSoNoServidorDeTeste = false; }
		Injeta("(o desenho nao gira) o Spin Attack bate parado na minha tela -- o jogo de antes", parado.Count == 1, string.Join(" -> ", parado));
	}

	// =====================================================================
	// CS) O CHEFE DE SAGA NA TELA: A CHEGADA PRA TODOS E A CENA DA TROCA DE CORPO (item 35)
	// =====================================================================
	private async Tarefa CS_OChefeDeSaga(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- CS: a chegada de um chefe de saga aparece na tela de todos, e a troca de corpo dele tem cena (item 35) ---");
		try
		{
			// ---- A CHEGADA, de um chefe que nem esta neste planeta ----
			int antes = CenaDaChegada.CenasDeTeste;
			(int chefe, string nome, string planeta) = srv.PalcoV30_ChegadaDoChefe(cli.LocalId);
			bool apareceu = chefe != 0 && await Ate(() => CenaDaChegada.NaTelaDeTeste is { } c && c.NomeDeTeste == nome.ToUpperInvariant(), 4);
			CenaDaChegada? cena = CenaDaChegada.NaTelaDeTeste;
			Checa("**A CHEGADA NA TELA DE TODOS** (*\"uma cutscene q aparece pra todos quando um boss de saga aparece\"*): o Freeza chegou em OUTRO planeta e a cena apareceu na minha tela -- o retrato dele, o nome, o planeta e a frase da chegada",
				  apareceu && cena != null && cena.RetratoDeTeste && cena.OndeDeTeste.Contains(planeta, StringComparison.Ordinal)
				  && cena.FraseDeTeste.Length > 0 && CenaDaChegada.CenasDeTeste == antes + 1,
				  $"chefe {chefe} '{nome}' em {planeta}; na tela: '{cena?.NomeDeTeste}' / '{cena?.OndeDeTeste}' / '{cena?.FraseDeTeste}', retrato {cena?.RetratoDeTeste}");
			Checa("...e ela NAO PRENDE: nenhum pedaco dela pega o mouse -- quem esta lutando continua lutando", cena != null && !cena.PrendeOMouseDeTeste);
			bool saiu = await Ate(() => CenaDaChegada.NaTelaDeTeste == null, CenaDaChegada.Duracao + 2);
			Checa($"...e sai sozinha (ela dura {CenaDaChegada.Duracao:0.#} s)", saiu);

			int antesDoDefeito = CenaDaChegada.CenasDeTeste;
			srv.PalcoV30_ChegadaDoChefe(cli.LocalId, semCena: true);
			await Segundos(1.5);
			Injeta("(a chegada so no chat) o chefe chega e ninguem ve cena nenhuma -- o jogo de antes",
				   CenaDaChegada.CenasDeTeste == antesDoDefeito, $"{CenaDaChegada.CenasDeTeste - antesDoDefeito} cena(s)");

			// ---- A CENA DA TROCA DE CORPO, de um Freeza aqui do lado ----
			int freeza = srv.PalcoV30_ChefeAqui(cli.LocalId);
			string Corpo() => World.Instancia?.LookDeTeste(freeza) is { } l && l.Ap.FormasDeFrost.Count > 0 ? l.Ap.FormasDeFrost[0] : "";
			bool naTela = freeza != 0 && await Ate(() => World.Instancia?.CorpoDeTeste(freeza) != null && Corpo().Length > 0, 6);
			Checa("(montagem) um Freeza de saga a tres tiles de mim, desenhado na primeira forma", naTela, $"chefe {freeza}, corpo '{Corpo()}'");
			if (!naTela) return;
			await Segundos(CenaDaChegada.Duracao);   // a cena da chegada dele sai da frente

			string antesDaCena = Corpo();
			bool intocavel = srv.PalcoV30_AvancarOChefe(freeza);
			bool comCena = await Ate(() => World.Instancia?.CenaEmCursoDeTeste(freeza) != null, 2);
			await Segundos(0.5);
			string noMeio = Corpo();
			bool acabou = await Ate(() => World.Instancia?.CenaEmCursoDeTeste(freeza) == null, 8);
			await Segundos(0.4);
			string depois = Corpo();
			Checa("**O CHEFE SE TRANSFORMA COM CENA** (*\"quando boss se transoforma ele tem cinematica igual um player\"*): a troca de corpo do Freeza toca a cena na minha tela e ele fica intocavel nela -- e o corpo novo so aparece na VIRADA: no meio da cena ainda e o de antes",
				  intocavel && comCena && acabou && noMeio == antesDaCena && depois != antesDaCena && depois.Length > 0,
				  $"intocavel {intocavel}; cena {comCena}; corpo antes '{antesDaCena}', no meio '{noMeio}', depois '{depois}'");

			string antesDoSemCena = Corpo();
			srv.PalcoV30_AvancarOChefe(freeza, semCena: true);
			await Segundos(0.6);
			Injeta("(a troca sem cena) o corpo do Freeza muda no meio da luta, sem cena nenhuma -- o jogo de antes",
				   World.Instancia?.CenaEmCursoDeTeste(freeza) == null && Corpo() != antesDoSemCena, $"'{antesDoSemCena}' -> '{Corpo()}'");
		}
		finally { srv.PalcoV30_TirarOsChefes(); }
	}

	// =====================================================================
	// RA) O MODO DAS RACAS: A ABA ADMIN E A CRIACAO COM A RACA SORTEADA (item 34)
	// =====================================================================
	private async Tarefa RA_OModoDasRacas(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- RA: o modo das racas -- o cartao da aba Admin muda o do servidor, e a criacao abre com a raca sorteada (item 34) ---");
		if (MenuJogo.Instancia is not { } menu) { Checa("(montagem) o menu P existe", false); return; }
		var aleatorio = Jandirus.Core.Races.ModoDeRacas.Aleatorio;
		var padrao = Jandirus.Core.Races.ModoDeRacas.Padrao;

		// PELA PAGINA DA ABA, e nao pela arvore do menu: destacado, o menu mora numa janela do sistema que nao e filha dele
		// (`MenuJogo.Janela.cs`) -- e a remontagem deixa o cartao velho na fila de apagar, com o modo de antes.
		Button? Botao(string texto)
		{
			if (menu.PaginaDeTeste(Verbos.Admin) is not { } pagina) return null;
			var fila = new Queue<Node>();
			fila.Enqueue(pagina);
			while (fila.Count > 0)
			{
				Node n = fila.Dequeue();
				if (n.IsQueuedForDeletion()) continue;
				if (n is Button b && b.Text == texto && b.IsVisibleInTree()) return b;
				foreach (Node f in n.GetChildren()) fila.Enqueue(f);
			}
			return null;
		}

		// ---- A ABA ADMIN ----
		menu.Abrir();
		menu.IrPara(Verbos.Admin);
		bool perguntou = await Ate(() => cli.ModoDeRacas == padrao && Botao("Raças aleatórias") is { Disabled: false }, 5);
		Checa("(montagem) a aba Admin tem o cartao das racas, e ele ja sabe o modo de agora (padrao) -- perguntado ao servidor pelo fio",
			  perguntou && srv.ModoDeRacas == padrao, $"cliente {cli.ModoDeRacas}, servidor {srv.ModoDeRacas}");
		Botao("Raças aleatórias")?.EmitSignal(BaseButton.SignalName.Pressed);
		bool virou = await Ate(() => srv.ModoDeRacas == aleatorio && cli.ModoDeRacas == aleatorio && Botao("Raças aleatórias") is { Disabled: true }, 5);
		Checa("**A ABA ADMIN MUDA O MODO** (*\"na aba de adm no meu P vai ter a opçao de mudar essa config\"*): o botao \"Raças aleatórias\" muda o servidor, grava o `servidor.json`, e o cartao volta mostrando o modo novo",
			  virou && Jandirus.Server.GameServer.ModoDeRacasGravado() == aleatorio, $"servidor {srv.ModoDeRacas}, cliente {cli.ModoDeRacas}, gravado {Jandirus.Server.GameServer.ModoDeRacasGravado()}");
		Botao("Modo padrão")?.EmitSignal(BaseButton.SignalName.Pressed);
		bool voltou = await Ate(() => srv.ModoDeRacas == padrao && cli.ModoDeRacas == padrao, 5);
		Checa("...e o \"Modo padrão\" devolve", voltou, $"servidor {srv.ModoDeRacas}");
		menu.Fechar();

		// ---- A CRIACAO COM A RACA SORTEADA ----
		async System.Threading.Tasks.Task<(string Titulo, string Trilha, (string Raca, string Linhagem) Ficha, string Anuncio, bool Cancelou)> AbrirComOSorteio()
		{
			var tela = new CreationScreen { Name = "CriacaoDaBancadaRA" };
			bool cancelou = false;
			tela.Cancelado += () => cancelou = true;
			AddChild(tela);
			await Quadros(2);
			tela.Reabrir("", "Saiyan", "Primal Saiyan");
			await Quadros(1);
			(string, string, (string, string), string) foto = (tela.TituloDaEtapaDeTeste, tela.TrilhaDeTeste, tela.FichaDeTeste, tela.AnuncioDeTeste);
			// A PRIMEIRA ETAPA DESTE PERSONAGEM: o botao da esquerda e o "Cancelar", e ele sai da criacao (e nao volta pro mundo).
			tela.FindChildren("*", nameof(Button), true, false).OfType<Button>()
				.FirstOrDefault(b => b.Text == "Cancelar" && b.IsVisibleInTree())?.EmitSignal(BaseButton.SignalName.Pressed);
			await Quadros(1);
			tela.QueueFree();
			return (foto.Item1, foto.Item2, foto.Item3, foto.Item4, cancelou);
		}

		var r = await AbrirComOSorteio();
		Checa("**A CRIACAO ABRE COM A RACA SORTEADA** (*\"raças aleatorias onde o jogador n pode escolher sua raça inicial\"*): comeca no BERCO -- sem mundo, raca nem linhagem --, a ficha ja e Saiyajin Primal, e o aviso em cima diz o sorteio",
			  r.Titulo == "O BERÇO" && r.Trilha.StartsWith("etapa 1 de ", StringComparison.Ordinal) && r.Ficha == ("Saiyan", "Primal Saiyan")
			  && r.Anuncio.Contains("sorteada", StringComparison.Ordinal) && r.Anuncio.Contains("Primal Saiyan", StringComparison.Ordinal),
			  $"etapa '{r.Titulo}', '{r.Trilha}', ficha {r.Ficha}, aviso '{r.Anuncio}'");
		Checa("...e na primeira etapa dele o botao da esquerda e o \"Cancelar\": sai da criacao, e nao volta pra uma etapa de raca que nao existe pra ele",
			  r.Cancelou);

		CreationScreen.EtapasDoSorteioVisiveisDeTeste = true;
		(string Titulo, string Trilha, (string Raca, string Linhagem) Ficha, string Anuncio, bool Cancelou) d;
		try { d = await AbrirComOSorteio(); }
		finally { CreationScreen.EtapasDoSorteioVisiveisDeTeste = false; }
		Injeta("(as etapas ignoram o sorteio) a criacao abre no mundo e oferece a raca, mesmo com a raca sorteada -- o jogo de antes",
			   d.Titulo != "O BERÇO", $"etapa '{d.Titulo}'");

		// ---- (adendo 14, item 45) A VAGA NAO CONTA O SORTEIO: a tela de vagas de producao, com uma vaga sorteada ----
		async System.Threading.Tasks.Task<string> TextosDaVaga()
		{
			var vagas = new CharacterSelect { Name = "VagasDaBancadaRA" };
			vagas.Mostrar([new SlotInfo { Ocupado = false, RacaSorteada = "Saiyan", LinhagemSorteada = "Primal Saiyan" }]);
			AddChild(vagas);
			await Quadros(2);
			string textos = string.Join(" | ", vagas.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text));
			vagas.QueueFree();
			await Quadros(1);
			return textos;
		}
		string vaga = await TextosDaVaga();
		Checa("**A VAGA NAO CONTA O SORTEIO** (*\"vc deveria clicar pra criar um personagem e ai q vc ia descobrir oq sorteou\"*): a vaga livre avisa que a raca e sorteada, sem dizer qual -- quem revela e a criacao",
			  vaga.Contains("descobre ao criar", StringComparison.Ordinal) && !vaga.Contains("Saiyan", StringComparison.Ordinal)
			  && !vaga.Contains("Primal", StringComparison.Ordinal), vaga);
		string vagaDeAntes;
		CharacterSelect.SorteioNaVagaDeTeste = true;
		try { vagaDeAntes = await TextosDaVaga(); }
		finally { CharacterSelect.SorteioNaVagaDeTeste = false; }
		Injeta("(a vaga que conta o sorteio, o jogo de antes) a vaga livre ja diz \"Saiyan (Primal Saiyan)\"",
			   vagaDeAntes.Contains("Primal Saiyan", StringComparison.Ordinal), vagaDeAntes);
	}

	// =====================================================================
	// MI) AS MIRAGENS DA TECNICA DURAM NA TELA (item 33)
	// =====================================================================
	private async Tarefa MI_AsMiragensDuram(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- MI: a miragem da Zanzoken Afterimage vive na minha tela mais que a do duplo clique (item 33) ---");
		srv.PalcoV30_Marcar(cli.LocalId, 0);   // sem alvo: a ronda e da `--v030teste`; aqui e a vida da miragem
		try
		{
			// A PRIMEIRA MIRAGEM e o `flick` da saida (a comum); a da tecnica e a seguinte, meio segundo depois.
			async System.Threading.Tasks.Task<(double Vida, bool Viva)> UmaMiragem()
			{
				int antes = Zanzoken.MiragensDeTeste;
				bool ligou = srv.LigarAsMiragensNaBancada(cli.LocalId);
				bool nasceu = ligou && await Ate(() => Zanzoken.MiragensDeTeste >= antes + 2, 4);
				Zanzoken? m = Zanzoken.UltimaDeTeste;
				double vida = m?.VidaDeTeste ?? 0;
				srv.PalcoV30_DesligarAsMiragens(cli.LocalId);
				await Segundos(1.0);
				return (vida, nasceu && m != null && IsInstanceValid(m));
			}

			(double vida, bool viva) = await UmaMiragem();
			Checa("**A MIRAGEM DA TECNICA DURA NA TELA** (*\"fica sem sentido a miragem durar milisegundos nesse caso\"*): feita pra viver 2 s, ela continua la um segundo depois -- a do duplo clique (0,55 s) ja teria sumido",
				  vida == Zanzoken.DuracaoDaTecnica && viva, $"vida {vida:0.##} s; viva depois de 1 s: {viva}");

			Zanzoken.MiragemDaTecnicaCurtaDeTeste = true;
			(double Vida, bool Viva) curta;
			try { curta = await UmaMiragem(); }
			finally { Zanzoken.MiragemDaTecnicaCurtaDeTeste = false; }
			Injeta("(a miragem curta) a da tecnica some como a do duplo clique -- o jogo de antes",
				   curta.Vida < Zanzoken.DuracaoDaTecnica && !curta.Viva, $"vida {curta.Vida:0.##} s; viva depois de 1 s: {curta.Viva}");
		}
		finally
		{
			Zanzoken.MiragemDaTecnicaCurtaDeTeste = false;
			srv.PalcoV30_DesligarAsMiragens(cli.LocalId);
		}
	}

	// =====================================================================
	// P) O MORTO NA MEDITACAO PROFUNDA (item 26)
	// =====================================================================
	private async Tarefa P_OMortoMergulha(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- P: o morto de pe mergulha na propria mente pela tecla M e pela telinha -- la dentro ele continua de pe (item 26) ---");
		bool naMesa = srv.PalcoV30_MortoAoLadoDoEnma(cli.LocalId, 0);
		bool morto = naMesa && await Ate(() => cli.Sheet.Morto && Jandirus.Core.World.Alem.EhOAlem(cli.Zone.Name), 15);
		Checa("(montagem) morto e de pe no Outro Mundo, ao lado do Enma", morto, $"na mesa {naMesa}; zona {cli.Zone.Name}");
		if (!morto) { srv.PalcoV30_Reviver(cli.LocalId); return; }
		try
		{
			await Segundos(0.5);
			bool abriu = await AbrirATelinhaDoMeditar();
			TelaDeMeditacao? tela = TelaDeMeditacao.Instancia;
			List<string> botoes = tela?.BotoesDesenhados() ?? [];
			Checa("**A TELINHA DO MEDITAR OFERECE A PROFUNDA AO FANTASMA**: os dois botoes acesos (a recusa sai da mesma regra do servidor -- so o cadaver e recusado)",
				  abriu && botoes.Count == 2 && !botoes.Exists(b => b.EndsWith("(apagado)", StringComparison.Ordinal)),
				  $"abriu {abriu}; botoes [{string.Join(" | ", botoes)}]; motivo '{tela?.MotivoNaTela}'");
			bool apertou = tela?.ApertarDesenhado(profunda: true) == true;
			bool entrou = apertou && await Ate(() => Jandirus.Core.World.DimensaoMental.EhAMente(cli.Zone), 15);
			await Segundos(1.5);
			bool ficou = Jandirus.Core.World.DimensaoMental.EhAMente(cli.Zone);
			Checa("**O FANTASMA ENTRA NA MENTE, E FICA** (*\"permita q morto possa fazer meditacao profunda\"*): a onda, a viagem, e um segundo e meio depois ele continua la -- morto e DE PE na minha tela (nao o cadaver)",
				  entrou && ficou && cli.Sheet.Morto && !LocalPlayer.Caido(cli.Sheet),
				  $"apertou {apertou}; entrou {entrou}; ficou {ficou}; morto {cli.Sheet.Morto}; caido {LocalPlayer.Caido(cli.Sheet)}; zona {cli.Zone.Name}");

			Jandirus.Core.World.Alem.MenteDeitaOMortoDeTeste = true;
			bool deitado = LocalPlayer.Caido(cli.Sheet);
			Jandirus.Core.World.Alem.MenteDeitaOMortoDeTeste = false;
			Injeta("(a mente nao e lugar de morto) o fantasma la dentro se veria deitado na propria tela, como o cadaver", deitado);

			// A SAIDA: a tecla M dentro da mente acorda (a mesma porta do botao do alto da tela)
			await ApertarM();
			bool voltou = await Ate(() => Jandirus.Core.World.Alem.EhOAlem(cli.Zone.Name), 15);
			await Segundos(0.5);
			Checa("...e a tecla M o devolve ao Outro Mundo morto, de pe e sem desmaio -- o `dead` da entrada nao vira uma 'morte de novo' na volta",
				  voltou && cli.Sheet.Morto && !cli.Sheet.KO && !LocalPlayer.Caido(cli.Sheet),
				  $"zona {cli.Zone.Name}; morto {cli.Sheet.Morto}; KO {cli.Sheet.KO}");

			// O JOGO DE ANTES: a porta do servidor recusa o morto
			Jandirus.Server.GameServer.MortoNaoMergulhaDeTeste = true;
			cli.SendActivity(Protocol.Activity.Meditando);
			cli.SendHabilidade("mente");
			bool entrouComDefeito = await Ate(() => Jandirus.Core.World.DimensaoMental.EhAMente(cli.Zone), 5);
			Jandirus.Server.GameServer.MortoNaoMergulhaDeTeste = false;
			Injeta("(a porta de antes: o `dead` do DM) o fantasma e recusado na porta da mente e fica no Outro Mundo", !entrouComDefeito);
			cli.SendActivity(Protocol.Activity.Parado);
		}
		finally
		{
			Jandirus.Server.GameServer.MortoNaoMergulhaDeTeste = false;
			Jandirus.Core.World.Alem.MenteDeitaOMortoDeTeste = false;
			TelaDeMeditacao.Instancia?.Fechar();
			if (Jandirus.Core.World.DimensaoMental.EhAMente(cli.Zone))
			{
				cli.SendHabilidade("sairdamente");
				await Ate(() => !Jandirus.Core.World.DimensaoMental.EhAMente(cli.Zone), 10);
			}
			srv.PalcoV30_Reviver(cli.LocalId);
			await Ate(() => !cli.Sheet.Morto, 10);
		}
	}

	/// <summary>
	/// ABRE A TELINHA DO MEDITAR pela tecla M -- e ela so abre pra quem NAO esta meditando: o primeiro M de quem medita levanta
	/// (a mesma regra que o `RoboDoMergulho` aprendeu, `AbrirATelinha`).
	/// </summary>
	private async System.Threading.Tasks.Task<bool> AbrirATelinhaDoMeditar()
	{
		if (World.Instancia?.CorpoDeTeste(C?.LocalId ?? 0) is LocalPlayer { AtividadeDeTeste: Protocol.Activity.Meditando })
		{
			await ApertarM();
			await Segundos(0.3);
		}
		await ApertarM();
		return TelaDeMeditacao.Instancia is { NaTela: true };
	}

	/// <summary>A tecla M, pelo motor -- a acao `meditate` do mapa de entrada, como o dedo.</summary>
	private async Tarefa ApertarM()
	{
		Input.ParseInputEvent(new InputEventAction { Action = "meditate", Pressed = false });
		await Quadros(1);
		Input.ParseInputEvent(new InputEventAction { Action = "meditate", Pressed = true });
		await Quadros(2);
		Input.ParseInputEvent(new InputEventAction { Action = "meditate", Pressed = false });
		await Quadros(1);
	}

	// =====================================================================
	// L) A REENCARNACAO: DE VOLTA A CRIACAO (item 7) -- a ULTIMA familia: ela troca o personagem
	// =====================================================================
	private async Tarefa L_AReencarnacao(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- L: reencarnar pelo Enma me devolve a CRIACAO, na vaga que vagou, e o novo nasce com 10% do poder (item 7) ---");
		if (GetParent() is not Boot boot) { Checa("(montagem) o robo nasceu debaixo do Boot", false); return; }
		(string conta, string nome) = srv.PalcoV30_QuemE(cli.LocalId);

		// O ROBO SAI DE BAIXO DO BOOT: a volta pra criacao derruba todos os filhos dele (`VoltarAoLogin`) -- o `RoboDeQueda`
		// faz igual (`Emancipar`)
		Node raiz = GetTree().Root;
		boot.RemoveChild(this);
		raiz.AddChild(this);
		await Quadros(1);

		srv.PalcoV30_BpBase(cli.LocalId, 20_000);
		bool naMesa = srv.PalcoV30_MortoAoLadoDoEnma(cli.LocalId, 0);
		bool morto = naMesa && await Ate(() => cli.Sheet.Morto, 15);
		Checa("(montagem) o robo saiu de baixo do Boot; morto, ao lado do Enma, com 20.000 de BP base", morto && GetParent() == raiz,
			  $"conta {conta}; nome {nome}");
		if (!morto) return;

		// O NOME ERRADO: nada
		cli.SendVerbo("enma_reencarnar", "nome errado");
		await Segundos(1.0);
		Checa("o NOME ERRADO nao apaga nada: o mundo continua e o personagem continua no slot",
			  boot.MundoMontado && srv.PalcoV30_Conta(conta, 0).Ocupado);

		// O NOME CERTO, sem o modo automatico: a criacao abre sozinha, como na tela do jogador
		boot.AutoDeTeste = false;
		try
		{
			cli.SendVerbo("enma_reencarnar", nome);
			bool naCriacao = await Ate(() => boot.CriacaoDeTeste.NaTela, 30);
			(_, int vaga, string anuncio) = boot.CriacaoDeTeste;
			(bool ocupado, double? eco) = srv.PalcoV30_Conta(conta, 0);
			Checa("**REENCARNAR ME DEVOLVE A CRIACAO**: o mundo cai, o cliente disca de novo sozinho e a CRIACAO abre na vaga que vagou, com a frase do Enma em cima -- sem o painel de \"DESCONECTADO\"",
				  naCriacao && !boot.MundoMontado && vaga == 0 && anuncio.Contains("recomeçar") && boot.AvisoDeQuedaNaTela() == null && cli.Connected,
				  $"criacao {naCriacao}; vaga {vaga}; anuncio '{anuncio}'; aviso '{boot.AvisoDeQuedaNaTela()}'");
			Checa("...e do lado de la o slot vagou e a conta guarda o eco (10% de 20.000 = 2.000)", !ocupado && eco == 2_000,
				  $"ocupado {ocupado}; eco {eco}");

			// O "CONFIRMAR" DA CRIACAO: a ficha da linha de comando, pela mesma porta do modo automatico
			boot.AutoDeTeste = true;
			boot.CriarComoOAutomaticoDeTeste();
			bool renasceu = await Ate(() => boot.MundoMontado && cli.LocalId != 0 && srv.PalcoV30_DaConta(conta, nome) is { BP: >= 2_000 }, 30);
			Checa("**O NOVO NASCE COM 10% DO PODER**: criado na vaga, ele entra no mundo com o BP base do eco (2.000) -- e a conta nao guarda mais eco nenhum",
				  renasceu && srv.PalcoV30_Conta(conta, 0) is { Ocupado: true, Eco: null }, $"{srv.PalcoV30_DaConta(conta, nome)}");
		}
		finally { boot.AutoDeTeste = true; }

		// O DEFEITO: a despedida vira queda comum -- o menu principal com "DESCONECTADO", e nada de criacao
		await Ate(() => World.Instancia != null && Hud.Instancia != null && cli.LocalId != 0, 15);
		GameClient.DespedidaViraQuedaDeTeste = true;
		try
		{
			bool deNovo = srv.PalcoV30_MortoAoLadoDoEnma(cli.LocalId, 0) && await Ate(() => cli.Sheet.Morto, 15);
			cli.SendVerbo("enma_reencarnar", nome);
			bool caiu = await Ate(() => boot.AvisoDeQuedaNaTela() != null, 20);
			await Segundos(1.0);
			Injeta("(a despedida vira queda comum) reencarnar jogaria o jogador no menu principal com \"DESCONECTADO DO SERVIDOR\", sem criacao -- o jogo de antes",
				   deNovo && caiu && !boot.CriacaoDeTeste.NaTela && boot.MenuPrincipalNaTela, $"morto {deNovo}; aviso '{boot.AvisoDeQuedaNaTela()}'");
		}
		finally
		{
			GameClient.DespedidaViraQuedaDeTeste = false;
			srv.PalcoV30_EsquecerOEco(conta);
		}
	}

	// =====================================================================
	// F) O CHEFE DE SAGA CACA O PLANETA INTEIRO (item 5)
	// =====================================================================
	private async Tarefa F_OChefeCaca(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F: o chefe de saga nota quem esta no planeta, e nao so quem passa a vinte tiles dele (item 5) ---");
		(bool daSaga, bool avulso, bool comDefeito, float tiles) = srv.PalcoV30_ChefeCacaOPlaneta(cli.LocalId);
		Checa("(montagem) um Freeza nasceu a mais de 25 tiles de mim (o raio de quem nota e 20)", tiles > 25, $"{tiles:0.#} tiles");
		Checa("**O CHEFE DE SAGA CACA O PLANETA INTEIRO**: o Freeza da saga me escolhe como presa do outro lado -- e nao fica parado esperando alguem passar a vinte tiles dele",
			  daSaga, $"{tiles:0.#} tiles");
		Checa("...e so o da saga: o mesmo molde FORA de uma saga (o chefe de admin, o Bills e o Whis na casa deles) continua com o raio de sempre",
			  !avulso);
		Injeta("(o chefe de saga com o raio comum) o Freeza ficaria parado do outro lado do planeta -- o 'perdido' do pedido", !comDefeito);
		await Quadros(1);
	}

	// =====================================================================
	// S) A SUPERNOVA DO FREEZA NA MINHA TELA (item 22)
	// =====================================================================
	private async Tarefa S_ASupernova(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- S: o ultimato do Freeza -- o sol nasce sobre ele, cresce, cai tres tiles e explode; ele imune o tempo todo (item 22) ---");
		int chefe = srv.PalcoV30_Ultimato(cli.LocalId);
		bool chegou = chefe != 0 && await Ate(() => World.Instancia?.SupernovaDeTeste(chefe) != null, 10);
		Checa("(montagem) um Freeza de saga perto de mim, no Outro Mundo (que nao e planeta: a explosao nao destroi nada), com o ultimato vencido -- e o sol apareceu na minha tela",
			  chegou, $"chefe {chefe}");
		if (!chegou) { srv.PalcoV30_DesfazerOUltimato(); return; }
		try
		{
			SupernovaDesenhada sol = World.Instancia!.SupernovaDeTeste(chefe)!;
			float d0 = sol.DiametroDeTeste;
			for (int i = 0; i < 6; i++) { await Segundos(0.5); srv.PalcoV30_TiqueDoUltimato(); }
			float d1 = sol.DiametroDeTeste;
			(bool supernova, bool intocavel, bool parado) = srv.PalcoV30_ComoEstaOChefe(chefe);
			Checa("**O SOL NASCE SOBRE O FREEZA E CRESCE**: brasa no comeco, bem maior tres segundos depois -- e ele concentra INTOCAVEL e PARADO (*\"nesse momento ele fica imune aos ataques\"*)",
				  d1 > d0 * 2 && !sol.SoltoDeTeste && supernova && intocavel && parado,
				  $"diametro {d0:0} -> {d1:0} px; supernova {supernova}; intocavel {intocavel}; parado {parado}");

			// O ULTIMATO ANDA PELO TIQUE DA SAGA: o sol sai da mao aos dez segundos e explode aos treze -- e o servidor tira o
			// chefe do mundo no mesmo instante, que e quando a destruicao comecaria
			int estourosAntes = SupernovaDesenhada.EstourosDeTeste;
			Vector2 antesDeCair = sol.CentroDeTeste;
			bool caiu = false;
			float desceu = 0;
			for (int i = 0; i < 40 && srv.PalcoV30_TiqueDoUltimato(); i++)
			{
				await Segundos(0.5);
				if (IsInstanceValid(sol) && sol.SoltoDeTeste)
				{
					caiu = true;
					desceu = Math.Max(desceu, sol.CentroDeTeste.Y - antesDeCair.Y);
				}
			}
			bool saiu = !srv.PalcoV30_TiqueDoUltimato();
			bool estourou = await Ate(() => SupernovaDesenhada.EstourosDeTeste > estourosAntes, 6);
			Checa("**ELE SOLTA O SOL, QUE CAI E EXPLODE**: o sol deixa a mao, desce na direcao do chao e explode no mundo -- e o Freeza sai do mundo, o instante em que a destruicao comeca",
				  caiu && desceu > 16 && saiu && estourou, $"caiu {caiu}; desceu {desceu:0} px; saiu {saiu}; estourou {estourou}");
			(bool intacto, double vidaAntes, double vidaAgora) = srv.PalcoV30_IntactoNoUltimato();
			Checa("**E ELE NAO ME TOCA ENQUANTO CONCENTRA**: a cinco tiles de mim, o Freeza com o sol erguido nao soca, nao investe e nao atira -- eu saio do ultimato inteiro (a primeira rodada me nocauteou)",
				  intacto, $"vida {vidaAntes:0.#} -> {vidaAgora:0.#}");
		}
		finally { srv.PalcoV30_DesfazerOUltimato(); }

		// O DEFEITO: a tela ignora a Supernova
		SupernovaDesenhada.SolIgnoradoDeTeste = true;
		try
		{
			int outro = srv.PalcoV30_Ultimato(cli.LocalId);
			// O CHEFE NA TELA PRIMEIRO: sem isso o "nao vi sol" mediria a troca de zona ainda carregando, e nao o defeito.
			bool corpoNaTela = outro != 0 && await Ate(() => World.Instancia?.CorpoDeTeste(outro) != null, 10);
			bool viu = await Ate(() => World.Instancia?.SupernovaDeTeste(outro) != null, 2);
			Injeta("(a tela ignora a Supernova) o Freeza congelaria imune sem sol nenhum, e o planeta explodiria do nada -- o jogo de antes",
				   corpoNaTela && !viu);
		}
		finally
		{
			SupernovaDesenhada.SolIgnoradoDeTeste = false;
			srv.PalcoV30_DesfazerOUltimato();
		}
		await Segundos(1.0);
	}

	// =====================================================================
	// O RELOGIO
	// =====================================================================
	// =====================================================================
	// TR) O REFUGIO PELA TERRA (adendo 11)
	// =====================================================================
	private async Tarefa TR_ORefugioPelaTerra(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- TR: o refugio pela Terra -- o meu natal acaba, a tela abre com o botao da Terra, e ele escolhe pelo fio (adendo 11) ---");
		if (TelaDeRefugio.Instancia is not { } tela) { Checa("(montagem) a tela do refugio existe", false); return; }

		Button? Botao(string trecho) => tela.BotoesDeTeste().FirstOrDefault(b => b.Text.Contains(trecho, StringComparison.Ordinal));
		string Botoes() => string.Join(" | ", tela.BotoesDeTeste().Select(b => b.Text));

		try
		{
			bool morto = srv.PalcoV30_SemNatal(cli.LocalId);
			bool abriu = await Ate(() => cli.RefugioPrecisa && tela.NaTela && Botao("Na Terra") != null, 5);
			Checa("**O NATAL ACABOU E A TELA ABRE SOZINHA COM A TERRA** (*\"ele sempre tem a opçao de reviver na terra se o planeta natal dele foi destruido\"*): sem territorio nenhum, a oferta pede a tela e ela traz o botao \"Na Terra\" ao lado da vizinhanca",
				  morto && abriu && Botao("O mundo vivo mais perto") != null,
				  $"palco={morto} precisa={cli.RefugioPrecisa} na tela={tela.NaTela} | {Botoes()}");
			Checa("...e o padrao continua a vizinhanca: ela vem marcada, e a Terra nao",
				  Botao("● O mundo vivo mais perto") != null && Botao("○ Na Terra") != null && !cli.RefugioTerraEscolhida, Botoes());
			await FotoSeHouverJanela("dv030-tr-1-refugio");

			Botao("Na Terra")?.EmitSignal(BaseButton.SignalName.Pressed);
			bool escolheu = await Ate(() => srv.PalcoV30_RefugioNaTerra(cli.LocalId) && cli.RefugioTerraEscolhida && Botao("● Na Terra") != null, 5);
			Checa("**O BOTAO DA TERRA ESCOLHE PELO FIO**: o servidor guarda a Terra (`RefugioNaTerra`) e a tela volta com ela marcada -- e a vizinhanca desmarcada",
				  escolheu && Botao("○ O mundo vivo mais perto") != null,
				  $"servidor={srv.PalcoV30_RefugioNaTerra(cli.LocalId)} cliente={cli.RefugioTerraEscolhida} | {Botoes()}");
			await FotoSeHouverJanela("dv030-tr-2-terra-escolhida");

			Botao("O mundo vivo mais perto")?.EmitSignal(BaseButton.SignalName.Pressed);
			bool desfez = await Ate(() => !srv.PalcoV30_RefugioNaTerra(cli.LocalId) && !cli.RefugioTerraEscolhida && Botao("● O mundo vivo mais perto") != null, 5);
			Checa("...e a vizinhanca desfaz: o servidor apaga a Terra e a marca volta pra ela", desfez, Botoes());

			// ---- (v0.3.0, item 39) A PERGUNTA APARECE UMA VEZ ----
			tela.Fechar();
			bool marcou = srv.PalcoV30_RefugioPerguntado(cli.LocalId);
			srv.PalcoV30_OfertaDoLogin(cli.LocalId);   // o login seguinte
			await Segundos(1.0);
			Checa("**A PERGUNTA APARECE UMA VEZ** (*\"sempre q entra no jogo é essa pergunta chata q ja respondi antes\"*): a marca ficou gravada na primeira, e a oferta do login seguinte NAO empurra a tela -- o cartao do menu P continua la",
				  marcou && !tela.NaTela && cli.RefugioPrecisa, $"marca={marcou} na tela={tela.NaTela} precisa={cli.RefugioPrecisa}");

			MenuJogo? menu = MenuJogo.Instancia;
			menu?.Abrir();
			menu?.IrPara("World");
			await Quadros(4);
			Button? cartao = menu?.PaginaDeTeste("World") is { } pg
				? Todos(pg).OfType<Button>().FirstOrDefault(b => b.Text.Contains("Escolher para onde eu volto", StringComparison.Ordinal))
				: null;
			cartao?.EmitSignal(BaseButton.SignalName.Pressed);
			bool peloMenu = cartao != null && await Ate(() => tela.NaTela, 3);
			await FotoSeHouverJanela("dv030-tr-2b-pelo-menu-p");
			menu?.Fechar();
			Checa("...e quem quiser mudar acha no menu P (*\"ele vai ter q ir no menu P procurar a opçao de mudar o local\"*): o cartao Refugio da aba World abre a tela",
				  peloMenu, cartao == null ? "o cartao nao esta na aba World" : $"na tela={tela.NaTela}");
			tela.Fechar();

			Jandirus.Server.GameServer.RefugioSempreAbreDeTeste = true;
			srv.PalcoV30_OfertaDoLogin(cli.LocalId);
			bool reabriu = await Ate(() => tela.NaTela, 3);
			Jandirus.Server.GameServer.RefugioSempreAbreDeTeste = false;
			Injeta("(a marca ignorada, o jogo de antes) a oferta do login empurra a tela de novo -- a pergunta chata", reabriu);
			tela.Fechar();

			// ---- O DEFEITO: o refugio sem a Terra (o jogo de antes) ----
			tela.Fechar();
			Jandirus.Server.GameServer.TerraForaDoRefugioDeTeste = true;
			srv.PalcoV30_OfertaDoRefugio(cli.LocalId);
			await Ate(() => !cli.RefugioTerra, 5);
			tela.Abrir();
			await Quadros(2);
			await FotoSeHouverJanela("dv030-tr-3-sem-terra");
			Injeta("(a Terra fora do refugio) a tela sai sem o botao da Terra -- o jogo de antes",
				   Botao("Na Terra") == null, Botoes());
		}
		finally
		{
			Jandirus.Server.GameServer.TerraForaDoRefugioDeTeste = false;
			tela.Fechar();
			srv.PalcoV30_DesfazerSemNatal(cli.LocalId);
		}
		bool limpou = await Ate(() => !cli.RefugioPrecisa, 5);
		Checa("(palco desfeito) Namek voltou e o cliente soube: com o berco de pe o refugio some", limpou);
		Checa("...e com o natal de pe a marca do \"ja perguntei\" se apaga: a proxima catastrofe pergunta de novo (item 39)",
			  !srv.PalcoV30_RefugioPerguntado(cli.LocalId));
	}

	// =====================================================================
	// KK) O KAIO-KEN (item 37): a tela da escolha com a maestria, o multiplicador pelo fio, e o X que apaga
	// =====================================================================
	private async Tarefa KK_OKaioken(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- KK: o Kaio-ken -- a tela da escolha com a maestria, o multiplicador pelo fio e o X que apaga (item 37) ---");
		if (TelaDeKaioken.Instancia is not { } tela) { Checa("(montagem) a tela do Kaio-ken existe", false); return; }
		try
		{
			bool sabe = srv.PalcoV30_AprenderOKaioken(cli.LocalId)
						&& await Ate(() => PosseDoCliente.Atual.SabeTecnica(TelaDeKaioken.VerboDoKaioken) && cli.Atributos.KaiokenVezes == 0, 5);
			Checa("(palco) o corpo aprendeu o Kaio-ken pela licao do Sr. Kaioh, e o cliente soube (sem multiplicador escolhido)", sabe,
				  $"sabe={PosseDoCliente.Atual.SabeTecnica(TelaDeKaioken.VerboDoKaioken)} ultimo=x{cli.Atributos.KaiokenVezes}");
			if (!sabe) return;

			// O BOTAO DO KAIO-KEN: o mesmo `Pedir` do botao da aba e da tecla de atalho (`MenuJogo.AbrirOControleDe`)
			tela.Pedir(TelaDeKaioken.VerboDoKaioken);
			await Quadros(2);
			var aberta = tela.TextosDeTeste();
			Checa("**O BOTAO ABRE A ESCOLHA, COM A MAESTRIA ESCRITA** (*\"o kaioken ta sem maestria\"*): a tela abre no x2 e diz ate onde o corpo domina -- x4, o piso de quem acabou de aprender",
				  tela.NaTela && aberta.Escolhido == 2 && aberta.Maestria.Contains("até x4", StringComparison.Ordinal)
				  && !srv.PalcoV30_Kaioken(cli.LocalId).Aceso, $"na tela={tela.NaTela} | {aberta}");

			tela.ArrastarDeTeste(7);
			await Quadros(2);
			var x7 = tela.TextosDeTeste();
			Checa("...arrastando pro x7 ela diz o poder do degrau e o RISCO contra a maestria x4: o nocaute com ele aceso despedaca o corpo (o DM, acima de 1,6x)",
				  x7.Escolhido == 7 && x7.Poder.Contains("x7", StringComparison.Ordinal) && x7.Risco.Contains("nocauteado", StringComparison.Ordinal)
				  && x7.Botao.Contains("x7", StringComparison.Ordinal), $"{x7}");
			await FotoSeHouverJanela("dv030-kk-1-escolha");

			tela.ApertarDeTeste();
			bool acendeu = await Ate(() => srv.PalcoV30_Kaioken(cli.LocalId) is { Aceso: true, Vezes: 7 }
															  && cli.Atributos is { KaiokenAceso: true, KaiokenVezes: 7 }, 5);
			Checa("**O MULTIPLICADOR ESCOLHIDO ACENDE PELO FIO** (*\"n ta dando pra escolher o multiplicador\"*): o botao manda `Kaioken_7`, o servidor acende o x7 e a ficha lenta volta dizendo aceso em x7",
				  acendeu && !tela.NaTela,
				  $"servidor={srv.PalcoV30_Kaioken(cli.LocalId)} cliente aceso={cli.Atributos.KaiokenAceso} x{cli.Atributos.KaiokenVezes} tela={tela.NaTela}");
			await FotoSeHouverJanela("dv030-kk-2-aceso");

			await ApertarX();
			bool apagou = await Ate(() => !srv.PalcoV30_Kaioken(cli.LocalId).Aceso && !cli.Atributos.KaiokenAceso, 5);
			Checa("**O X APAGA O KAIO-KEN** (*\"apertar X n ta saindo da forma\"*): a tecla do reverter desliga o Kaio-ken aceso, pelo fio",
				  apagou, $"servidor={srv.PalcoV30_Kaioken(cli.LocalId)} cliente aceso={cli.Atributos.KaiokenAceso}");

			// A TELA REABRE NO ULTIMO ACESO -- e o defeito: o X de antes
			tela.Pedir(TelaDeKaioken.VerboDoKaioken);
			await Quadros(2);
			int reaberta = tela.TextosDeTeste().Escolhido;
			Checa("...e a tela reabre no ULTIMO multiplicador aceso (x7), que o servidor guarda na ficha", reaberta == 7, $"x{reaberta}");
			tela.ApertarDeTeste();
			bool deNovo = await Ate(() => srv.PalcoV30_Kaioken(cli.LocalId).Aceso, 5);
			Jandirus.Server.GameServer.XNaoApagaOKaiokenDeTeste = true;
			await ApertarX();
			await Segundos(1.0);
			Injeta("(o X que ignora o Kaio-ken) o Kaio-ken continua aceso depois do X -- o jogo de antes",
				   deNovo && srv.PalcoV30_Kaioken(cli.LocalId).Aceso, $"aceso de novo={deNovo}");
		}
		finally
		{
			Jandirus.Server.GameServer.XNaoApagaOKaiokenDeTeste = false;
			tela.Fechar();
			srv.PalcoV30_ApagarOKaioken(cli.LocalId);
		}
	}

	// =====================================================================
	// GK) A GENKIDAMA QUE NAO TELEPORTA (itens 38 e 42)
	// =====================================================================
	private async Tarefa GK_AGenkidamaNaoTeleporta(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- GK: a Genkidama -- em cima da cabeca, saindo na frente, e lancada o corpo fica preso na minha tela tambem (itens 38 e 42) ---");
		if (World.Instancia?.FindChild("LocalPlayer", true, false) is not LocalPlayer eu) { Checa("(montagem) o meu boneco existe", false); return; }
		bool sabe = srv.PalcoV30_AprenderAGenkidama(cli.LocalId) && await Ate(() => PosseDoCliente.Atual.SabeTecnica("SpiritBomb"), 5);
		Checa("(palco) o corpo aprendeu a Genkidama pela licao do Sr. Kaioh, com o Ki cheio, e o cliente soube", sabe);
		if (!sabe) return;

		// QUANTO O MEU BONECO SAIU DO LUGAR com a tecla de andar apertada -- o MAIOR desvio, quadro a quadro: com a trava so no
		// servidor ele anda e e puxado de volta, e no fim da tecla ja voltou.
		async System.Threading.Tasks.Task<float> SegurarD(double segundos)
		{
			Vector2 de = World.Instancia?.PosicaoLocal ?? default;
			float maior = 0;
			Tecla(Key.D, true);
			double fim = Time.GetTicksMsec() / 1000.0 + segundos;
			while (Time.GetTicksMsec() / 1000.0 < fim)
			{
				maior = Math.Max(maior, (World.Instancia?.PosicaoLocal ?? de).DistanceTo(de));
				await Quadros(1);
			}
			Tecla(Key.D, false);
			await Quadros(2);
			return maior;
		}

		World.Instancia?.AplicarZoom(Settings.ZoomMin);   // so pras fotos: a bola de tres tiles de raio cabe melhor
		try
		{
			cli.SendHabilidade("SpiritBomb");
			bool formou = await Ate(() => srv.PalcoV30_FaseDaGenkidama(cli.LocalId) == 1, 8);
			Checa("(a Genkidama se formou pela porta do jogador: o botao manda `SpiritBomb`)", formou, $"fase {srv.PalcoV30_FaseDaGenkidama(cli.LocalId)}");
			if (!formou) return;
			await Segundos(1.0);
			await FotoSeHouverJanela("dv030-gk-1-em-cima-da-cabeca");

			cli.SendHabilidade("SpiritBomb");   // pede o disparo
			bool saiu = await Ate(() => srv.PalcoV30_FaseDaGenkidama(cli.LocalId) == 3, 6);
			await Quadros(3);
			await FotoSeHouverJanela("dv030-gk-2-saindo-na-frente");
			bool preso = saiu && await Ate(() => cli.EfeitosAtivos.Contains(Protocol.EfeitoDaTravaDeTecnica)
																		  && eu.PorQueNaoAnda.Contains("preso pela tecnica", StringComparison.Ordinal), 2);
			float andou = await SegurarD(0.5);
			Checa("**LANCADA, O CORPO FICA PRESO NA MINHA TELA TAMBEM** (*\"o certo era so desativar inputs ate liberar o movimento\"*): a trava chega pelo fio, o meu boneco sabe por que nao anda, e a tecla de andar NAO o tira do lugar -- nada de andar e ser puxado de volta",
				  preso && andou < 2f,
				  $"lancada={saiu} efeito={cli.EfeitosAtivos.Contains(Protocol.EfeitoDaTravaDeTecnica)} por que nao anda=\"{eu.PorQueNaoAnda}\" andou {andou:0.#} px");

			LocalPlayer.TravaDaTecnicaSoNoServidorDeTeste = true;
			float comDefeito = await SegurarD(0.5);
			LocalPlayer.TravaDaTecnicaSoNoServidorDeTeste = false;
			Injeta("(a trava so no servidor, o jogo de antes) o meu boneco anda com a tecla e e puxado de volta -- o \"fica te teleportando pra posiçao q vc tava\"",
				   comDefeito > 2f, $"andou {comDefeito:0.#} px");

			bool solto = await Ate(() => srv.PalcoV30_FaseDaGenkidama(cli.LocalId) == -1
																  && !cli.EfeitosAtivos.Contains(Protocol.EfeitoDaTravaDeTecnica), 6);
			float depois = await SegurarD(0.5);
			Checa("...e quando a trava acaba o efeito sai do fio e o corpo anda de novo", solto && depois > 8f, $"solto={solto} andou {depois:0.#} px");
		}
		finally
		{
			LocalPlayer.TravaDaTecnicaSoNoServidorDeTeste = false;
			Tecla(Key.D, false);
			World.Instancia?.AplicarZoom(Boot.Config.Zoom);
		}
	}

	/// <summary>As duas metades preenchidas, como um teclado de verdade manda (a nota da `--diagmudez`).</summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = apertada });

	/// <summary>O X de verdade: apertado por dois quadros (o `LocalPlayer` le o `IsActionJustPressed` no quadro dele) e solto.</summary>
	private async Tarefa ApertarX()
	{
		Tecla(Key.X, true);
		await Quadros(2);
		Tecla(Key.X, false);
		await Quadros(2);
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	/// <summary>
	/// COM JANELA, A TELA VIRA UM PNG na pasta do usuario (a bancada roda com o APPDATA desviado). Sem janela -- o caso de
	/// sempre desta bancada, `--headless` -- nao ha o que fotografar e nada acontece. So pra OLHAR o desenho: nenhuma prova
	/// daqui mede pixel (ver o cabecalho).
	/// </summary>
	private async Tarefa FotoSeHouverJanela(string nome)
	{
		if (DisplayServer.GetName() == "headless") return;
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		if (GetViewport()?.GetTexture()?.GetImage() is not { } img || img.IsEmpty()) return;
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota("  foto   " + caminho);
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
