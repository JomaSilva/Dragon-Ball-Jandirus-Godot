using System.Collections;
using Godot;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// ROBO `--diagtorneiocell` (2026-10-19) -- AS FOTOS DOS PEDIDOS DE HOJE, pro dono ver (*"tire print de todos os testes pra provar"*). So com
/// `--host`: o robo pede ao servidor do proprio processo as portas de palco (`GameServer.TorneioDoCellPalco.cs`) e fotografa cada passo:
///   T. O TORNEIO DO CELL: o Cell Perfeito esperando na arena; o x1 (pisando na arena o jogador e chamado); o Cell caindo da arena -- a regra
///      cai e os sete Cell Jr nascem em volta dele (2026-10-20: e um Cell Jr de perto, de frente, com a cabeca inteira); o Cell nocauteado
///      morre na luta e volta SUPER PERFEITO na arena; e o Kamehameha -- (2026-10-19)
///      o RAIO da tecnica, 2x: a carga na mao, o feixe chegando e o estouro no fim, quando a Terra comeca a explodir (o palco a devolve antes
///      do commit);
///   K. (2026-10-19) O EMBATE CONTRA O KAMEHAMEHA: o jogador de frente pro Cell, o Kamehameha dele contra o do Cell, a disputa de ki -- e o
///      jogador vencendo: o Cell DESINTEGRADO na hora, a Terra inteira;
///   S. (2026-10-20) O SUPER PERFEITO PERDENDO, na luta livre: o tronco abaixo de 25% e ele carrega o MESMO Kamehameha sozinho; o jogador que
///      PERDE a disputa contra ele -- o raio segue e a Terra explode --; ele NOCAUTEADO por um golpe alto, que levanta e carrega do mesmo jeito;
///      e o jogador que vence -- o Super Perfeito desintegrado, a Terra inteira;
///   A. OS ANDROIDES: o Scan e o Sense com um humano e tres androides (os dois tipos do jogador e o 17 NPC) -- so o humano aparece; e o
///      contra-exemplo, o Scan de antes, com os tres;
///   N. NAMEK: o Freeza de Namek caido quando o prazo vence -- sem Death Ball; e o contra-exemplo, o jogo de antes: a esfera sobre o corpo caido.
/// As regras sao das bancadas (`--torneiocellteste`, `--sentidosteste`, `--chefesteste`); aqui e o desenho.
/// </summary>
public partial class RoboDoTorneioDoCell : Node
{
	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dtorneiocell]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[dtorneiocell]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;
		Iluminacao.MeioDiaDeTeste = true;   // a luz do dia claro, como no robo da raiva: a foto e do desenho, nao da hora
		_espera -= delta;
		if (_espera > 0) return;
		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			GD.Print($"\n[dtorneiocell] ===== {_ok} OK, {_falhou} FALHA(S) =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[dtorneiocell] sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[dtorneiocell] foto {caminho}");
	}

	/// <summary>O menu P aberto na aba pedida -- pelo botao dela, o caminho do dedo (como o robo dos sentidos).</summary>
	private static void AbrirAba(string aba)
	{
		if (MenuJogo.Instancia is not { } menu) return;
		if (!menu.Visible) menu.Abrir();
		if (Botao(menu, aba) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed);
	}

	private static Button? Botao(Node n, string texto)
	{
		foreach (Node f in n.GetChildren())
		{
			if (f is Button b && b.IsVisibleInTree() && string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase)) return b;
			if (Botao(f, texto) is { } achou) return achou;
		}
		return null;
	}

	private static void FecharOMenu()
	{
		if (MenuJogo.Instancia is { Visible: true } menu) menu.Fechar();
	}

	private static bool NaLista(GameClient cli, string nome) => cli.Sentidos.Any(p => p.Nome == nome);

	/// <summary>A primeira linha do quadro com algum pixel desenhado (-1 = sem quadro, ou vazio).</summary>
	private static int PrimeiraLinhaComDesenho(Image? img)
	{
		if (img == null) return -1;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
				if (img.GetPixel(x, y).A > 0) return y;
		return -1;
	}

	private static string Lista(GameClient cli) => string.Join(", ", cli.Sentidos.Select(p => p.Nome.Length > 0 ? p.Nome : "???"));

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		World mundo = World.Instancia!;
		int eu = cli.LocalId;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }

		foreach (object passo in OTorneio(cli, mundo, srv, eu)) yield return passo;
		foreach (object passo in OEmbate(mundo, srv, eu)) yield return passo;
		foreach (object passo in OSuperPerfeito(mundo, srv, eu)) yield return passo;
		foreach (object passo in OsAndroides(cli, srv, eu)) yield return passo;
		foreach (object passo in Namek(mundo, srv, eu)) yield return passo;
	}

	// =====================================================================
	// T. O TORNEIO DO CELL
	// =====================================================================
	private IEnumerable OTorneio(GameClient cli, World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- T: o torneio do Cell ---");
		int cell = srv.PalcoTC_Abrir(eu);
		yield return 3.0;
		Afirmar("(montagem) o torneio abriu: o Cell Perfeito esta na arena da Terra e na minha tela, e eu na beirada",
				cell != 0 && mundo.CorpoDeTeste(cell) != null && srv.PalcoTC_Fase() == "Esperando", $"cell {cell}; fase {srv.PalcoTC_Fase()}");
		if (cell == 0) yield break;
		Fotografar("tc-1-o-cell-espera-na-arena");

		srv.PalcoTC_Desafiar(eu);
		for (int i = 0; i < 50 && srv.PalcoTC_Fase() != "Luta"; i++) yield return 0.2;
		Afirmar("pisando na arena eu sou chamado, e a contagem acaba na luta (o x1)", srv.PalcoTC_Fase() == "Luta", srv.PalcoTC_Fase());
		yield return 1.0;
		Fotografar("tc-2-o-x1");

		bool saiu = srv.PalcoTC_CellForaDaArena(eu);
		for (int i = 0; i < 30 && srv.PalcoTC_CellJr().CellJr < 7; i++) yield return 0.2;
		yield return 1.5;
		(int jrs, bool regraCaiu) = srv.PalcoTC_CellJr();
		Afirmar("**O CELL CAI DA ARENA**: a regra cai e os SETE CELL JR nascem em volta dele -- e a luta continua",
				saiu && jrs == 7 && regraCaiu && srv.PalcoTC_Fase() == "Luta", $"cell Jr {jrs}; regra caiu {regraCaiu}; fase {srv.PalcoTC_Fase()}");
		Fotografar("tc-3-cell-fora-da-arena-e-os-7-cell-jr");

		// (2026-10-20) A CABECA DO CELL JR, de perto e de frente -- o dono: *"os cell jr tao com o topo da cabeça cortados"*. Um Cell Jr parado, dois
		// tiles a leste de mim, olhando pro sul: o quadro que a minha tela desenha (a textura importada) comeca na linha 0, como o de costas.
		int jrDeFrente = srv.PalcoTC_CellJrDeFrente(eu);
		CharacterVisual? jrNaTela = null;
		for (int i = 0; i < 30 && jrDeFrente != 0 && (jrNaTela = mundo.CorpoDeTeste(jrDeFrente)?.GetNodeOrNull<CharacterVisual>("Visual")) == null; i++)
			yield return 0.1;
		yield return 0.8;
		bool naTela = jrNaTela != null && IsInstanceValid(jrNaTela);
		string pose = naTela ? jrNaTela!.AnimacaoDeTeste : "";
		int topo = PrimeiraLinhaComDesenho(naTela ? jrNaTela!.QuadroDoCorpoDeTeste(primeiro: true) : null);
		Fotografar("tc-3b-cell-jr-de-frente-cabeca-inteira");
		Afirmar("**O CELL JR DE FRENTE TEM A CABECA INTEIRA** (*\"os cell jr tao com o topo da cabeça cortados\"*): o quadro que a minha tela desenha "
				+ "comeca na linha 0, com as pontas da crista -- a arte do DM comecava na 3",
				naTela && pose.EndsWith("south", StringComparison.Ordinal) && topo == 0, $"cell Jr {jrDeFrente}; pose {pose}; primeira linha com desenho {topo}");
		srv.PalcoTC_LimparOCellJrDeFrente();

		srv.PalcoTC_NocautearOCell();
		for (int i = 0; i < 20 && srv.PalcoTC_OCell().Id != 0; i++) yield return 0.1;
		yield return 0.8;
		Fotografar("tc-4-cell-morto-na-luta");
		bool adiantou = false;
		for (int i = 0; i < 40 && !(adiantou = srv.PalcoTC_AdiantarAVolta()); i++) yield return 0.2;
		for (int i = 0; i < 60 && srv.PalcoTC_OCell().Degrau != 3; i++) yield return 0.2;
		yield return 1.5;
		(int volta, int degrau, bool torneio) = srv.PalcoTC_OCell();
		Afirmar("**NOCAUTEADO NO X1 ELE MORRE NA LUTA E VOLTA SUPER PERFEITO, NA ARENA** -- o torneio acabou: a luta e livre",
				adiantou && volta != 0 && degrau == 3 && !torneio && mundo.CorpoDeTeste(volta) != null, $"cell {volta}; degrau {degrau + 1}; torneio {torneio}");
		Fotografar("tc-5-cell-super-perfeito-na-arena");

		// ---- o Kamehameha, num torneio novo: (2026-10-19) o RAIO da tecnica, 2x ----
		cell = srv.PalcoTC_Abrir(eu);
		yield return 2.0;
		bool comecou = cell != 0 && srv.PalcoTC_Kamehameha(eu);
		yield return 2.5;
		Fotografar("tc-6-kamehameha-carregando");
		Afirmar("**A FILA VAZIA: O KAMEHAMEHA** -- ele o carrega na mao, pelo canal da tecnica (o desenho de carga de todo raio), na minha tela",
				comecou && srv.PalcoTC_CarregandoOKamehameha() && mundo.CorpoDeTeste(cell) != null, $"comecou {comecou}");
		srv.PalcoTC_IrProFimDoKamehameha(eu);
		int raio = 0;
		for (int i = 0; i < 80 && (raio = srv.PalcoTC_RaioDoKamehameha().Id) == 0; i++) yield return 0.1;
		ProjetilDesenhado? desenho = null;
		for (int i = 0; i < 20 && raio != 0 && (desenho = mundo.TiroDesenhadoDeTeste(raio)) == null; i++) yield return 0.05;
		// o raio chegando: a foto sai com a cabeca desenhada a menos de quatro tiles de mim (a camera me segue)
		for (int i = 0; i < 80 && desenho != null && IsInstanceValid(desenho) && mundo.PosicaoLocal is { } aqui
						&& desenho.Position.DistanceTo(aqui) > 4 * 32; i++) yield return 0.02;
		Fotografar("tc-7-kamehameha-o-feixe-2x");
		double escala = srv.PalcoTC_RaioDoKamehameha().Escala;
		float desenhada = desenho != null && IsInstanceValid(desenho) ? desenho.Escala : 0;
		Afirmar("**...e o raio sai: o feixe do Kamehameha da tecnica, 2x o tamanho normal** -- e desenhado assim na minha tela",
				raio != 0 && Math.Abs(escala - 2) < 1e-6 && Math.Abs(desenhada - 2f) < 0.06f,
				$"raio {raio}; escala no servidor {escala:0.##}; desenhada {desenhada:0.##}");
		bool explodiu = false;
		for (int i = 0; i < 100 && !(explodiu = srv.PalcoTC_TerraExplodindo()); i++) yield return 0.05;
		yield return 0.25;
		Fotografar("tc-8-a-terra-explode");
		Afirmar("**...o raio chega ao fim** -- o estouro na minha tela, e a destruicao da Terra comeca", explodiu);
		srv.PalcoTC_DevolverATerra(eu);
		yield return 1.5;
	}

	// =====================================================================
	// K. O EMBATE CONTRA O KAMEHAMEHA (dono, 2026-10-19)
	// =====================================================================
	private IEnumerable OEmbate(World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- K: o embate de ki contra o Kamehameha ---");
		int cell = srv.PalcoTC_Abrir(eu);
		yield return 2.0;
		bool montou = cell != 0 && srv.PalcoTC_EmbateDoKamehameha(eu, vezesOBp: 100);
		for (int i = 0; i < 120 && montou && !srv.PalcoTC_EmEmbate(); i++) yield return 0.1;
		bool abriu = srv.PalcoTC_EmEmbate();
		yield return 0.3;
		int raios = mundo.TirosDesenhados().Count(t => t.Tipo == Jandirus.Core.Combat.TipoDeProjetil.Beam);
		Fotografar("tc-9-o-embate-contra-o-kamehameha");
		Afirmar("**O EMBATE: o meu Kamehameha contra o dele, 2x** -- a disputa de ki abre, com os dois raios na minha tela",
				abriu && raios >= 2, $"montou {montou}; abriu {abriu}; raios desenhados {raios}");
		// a disputa: eu aperto cada letra -- o servidor nao aperta por quem tem teclado
		for (int i = 0; i < 900 && srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_ApertarNoEmbate(eu); yield return 0.02; }
		yield return 0.6;
		Fotografar("tc-10-o-cell-desintegrado");
		(bool morto, bool terraInteira) = srv.PalcoTC_DepoisDoEmbate();
		Afirmar("**EU VENCO A DISPUTA E O CELL MORRE NA HORA** -- desintegrado, e a Terra inteira", abriu && morto && terraInteira,
				$"morto {morto}; Terra inteira {terraInteira}");
		srv.PalcoTC_DevolverOBp();
		srv.PalcoTC_DevolverATerra(eu);
		yield return 1.5;
	}

	// =====================================================================
	// S. O SUPER PERFEITO PERDENDO (dono, 2026-10-20)
	// =====================================================================
	private IEnumerable OSuperPerfeito(World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- S: o Super Perfeito perdendo solta o Kamehameha que explode a Terra ---");
		// ---- ele perdendo, o tronco a 22%: o vigia do jogo o faz soltar o Kamehameha sozinho, na luta livre
		int cell = srv.PalcoTC_SuperPerfeitoPerdendo(eu, tilesAOeste: 6);
		for (int i = 0; i < 40 && cell != 0 && !srv.PalcoTC_CarregandoOKamehameha(); i++) yield return 0.1;
		yield return 1.5;
		(bool carregando, int degrau, bool torneio, double tronco, bool caido) = srv.PalcoTC_OSuperPerfeito();
		Fotografar("tc-11-super-perfeito-perdendo-carrega-o-kamehameha");
		Afirmar("**O SUPER PERFEITO PERDENDO SOLTA O KAMEHAMEHA** -- o tronco abaixo de 25%, na luta livre (sem torneio): ele junta as maos, na minha tela",
				cell != 0 && carregando && degrau == 3 && !torneio && !caido && tronco < 0.25 && mundo.CorpoDeTeste(cell) != null,
				$"cell {cell}; carregando {carregando}; degrau {degrau + 1}; torneio {torneio}; caido {caido}; tronco {tronco:P0}");

		// ---- eu entro na disputa com um milesimo do BP dele e nao aperto nada: PERCO -- o raio dele segue (eu vou pro fim dele, pra camera), e a
		// Terra explode
		bool montou = cell != 0 && srv.PalcoTC_DisputarOKamehameha(eu, vezesOBp: 0.001);
		for (int i = 0; i < 120 && montou && !srv.PalcoTC_EmEmbate(); i++) yield return 0.1;
		bool abriu = srv.PalcoTC_EmEmbate();
		for (int i = 0; i < 600 && srv.PalcoTC_EmEmbate(); i++) yield return 0.05;
		srv.PalcoTC_IrProFimDoKamehameha(eu);
		bool explodiu = false;
		for (int i = 0; i < 300 && !(explodiu = srv.PalcoTC_TerraExplodindo()); i++) yield return 0.05;
		yield return 0.25;
		Fotografar("tc-12-perdi-a-disputa-a-terra-explode");
		Afirmar("**EU PERCO A DISPUTA CONTRA O SUPER PERFEITO: o Kamehameha dele segue, e a Terra explode** (*\"Se o jogador perder o clash obviamente a "
				+ "terra tb explode\"*)", abriu && explodiu, $"montou {montou}; abriu {abriu}; explodiu {explodiu}");
		srv.PalcoTC_DevolverOBp();
		srv.PalcoTC_DevolverATerra(eu);
		yield return 1.5;

		// ---- de novo, NOCAUTEADO por um golpe alto de uma vez (o tronco a 10%, no chao): o vigia o levanta, e ele carrega do mesmo jeito
		cell = srv.PalcoTC_SuperPerfeitoPerdendo(eu, tilesAOeste: 6, nocauteado: true);
		for (int i = 0; i < 40 && cell != 0 && !srv.PalcoTC_CarregandoOKamehameha(); i++) yield return 0.1;
		yield return 1.5;
		(carregando, degrau, torneio, _, caido) = srv.PalcoTC_OSuperPerfeito();
		Fotografar("tc-13-nocauteado-ele-levanta-e-carrega-o-kamehameha");
		Afirmar("**NOCAUTEADO POR UM GOLPE ALTO, ELE LEVANTA E CARREGA O KAMEHAMEHA** -- de pe e intocavel na carga, na minha tela",
				cell != 0 && carregando && degrau == 3 && !torneio && !caido && mundo.CorpoDeTeste(cell) != null,
				$"cell {cell}; carregando {carregando}; degrau {degrau + 1}; torneio {torneio}; caido {caido}");

		// ---- ...e agora eu VENCO a disputa (cem vezes o BP dele, apertando cada letra): o Super Perfeito e desintegrado, e a Terra fica inteira
		montou = cell != 0 && srv.PalcoTC_DisputarOKamehameha(eu, vezesOBp: 100);
		for (int i = 0; i < 120 && montou && !srv.PalcoTC_EmEmbate(); i++) yield return 0.1;
		abriu = srv.PalcoTC_EmEmbate();
		for (int i = 0; i < 900 && srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_ApertarNoEmbate(eu); yield return 0.02; }
		yield return 0.6;
		Fotografar("tc-14-super-perfeito-desintegrado");
		(bool morto, bool terraInteira) = srv.PalcoTC_DepoisDoEmbate();
		Afirmar("**EU VENCO A DISPUTA E O SUPER PERFEITO MORRE NA HORA** -- desintegrado, e a Terra inteira", abriu && morto && terraInteira,
				$"montou {montou}; abriu {abriu}; morto {morto}; Terra inteira {terraInteira}");
		srv.PalcoTC_DevolverOBp();
		srv.PalcoTC_DevolverATerra(eu);
		yield return 1.5;
	}

	// =====================================================================
	// A. OS ANDROIDES FORA DO SCAN E DO SENSE
	// =====================================================================
	private IEnumerable OsAndroides(GameClient cli, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- A: os androides fora do Scan e do Sense ---");
		bool montou = srv.PalcoAN_Montar(eu);
		for (int i = 0; i < 40 && !(cli.SentidosSaoDoScouter && NaLista(cli, "Humano")); i++) yield return 0.2;
		AbrirAba("Scan");
		yield return 1.0;
		Afirmar("**O SCAN SEM OS ANDROIDES**: o Humano aparece; o androide de energia infinita, o de absorcao e o 17 NPC nao",
				montou && cli.SentidosSaoDoScouter && NaLista(cli, "Humano") && !NaLista(cli, "Numero 18") && !NaLista(cli, "Numero 19")
				&& !NaLista(cli, "Androide 17"), Lista(cli));
		Fotografar("an-1-scan-sem-androides");

		srv.PalcoAN_Defeito(true);
		for (int i = 0; i < 30 && !NaLista(cli, "Numero 18"); i++) yield return 0.2;
		yield return 0.6;
		bool apareceram = NaLista(cli, "Numero 18") && NaLista(cli, "Numero 19") && NaLista(cli, "Androide 17");
		Fotografar("an-2-contra-exemplo-scan-de-antes-com-androides");
		srv.PalcoAN_Defeito(false);
		Afirmar("   CONTRA-EXEMPLO (o Scan de antes): os tres androides aparecem -- a regua de cima reprovaria", apareceram, Lista(cli));

		srv.PalcoAN_Sense(eu);
		for (int i = 0; i < 40 && !(!cli.SentidosSaoDoScouter && NaLista(cli, "Humano")); i++) yield return 0.2;
		AbrirAba("Sense");
		yield return 1.0;
		Afirmar("**O SENSE SEM OS ANDROIDES**: o Humano e sentido; nenhum dos tres androides",
				!cli.SentidosSaoDoScouter && NaLista(cli, "Humano") && !NaLista(cli, "Numero 18") && !NaLista(cli, "Numero 19")
				&& !NaLista(cli, "Androide 17"), Lista(cli));
		Fotografar("an-3-sense-sem-androides");
		FecharOMenu();
		srv.PalcoAN_Limpar(eu);
		yield return 1.0;
	}

	// =====================================================================
	// N. O FREEZA CAIDO EM NAMEK
	// =====================================================================
	private IEnumerable Namek(World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- N: o Freeza caido quando o prazo vence ---");
		int freeza = srv.PalcoNM_FreezaCaido(eu, comODefeito: false);
		yield return 2.5;
		Afirmar("**CAIDO NO FIM DO PRAZO, O FREEZA NAO ERGUE A DEATH BALL**: ele fica no chao, sem esfera -- Namek pode ser salva",
				freeza != 0 && mundo.CorpoDeTeste(freeza) != null && !srv.PalcoNM_ComAEsfera() && mundo.SupernovaDeTeste(freeza) == null,
				$"freeza {freeza}; esfera {srv.PalcoNM_ComAEsfera()}");
		Fotografar("nm-1-freeza-caido-sem-death-ball");

		freeza = srv.PalcoNM_FreezaCaido(eu, comODefeito: true);
		for (int i = 0; i < 30 && mundo.SupernovaDeTeste(freeza) == null; i++) yield return 0.1;
		yield return 3.0;
		bool subiu = srv.PalcoNM_ComAEsfera() && mundo.SupernovaDeTeste(freeza) != null;
		Fotografar("nm-2-contra-exemplo-death-ball-no-freeza-caido");
		Afirmar("   CONTRA-EXEMPLO (o jogo de antes): a Death Ball sobe sobre o Freeza caido -- e esta no ar quando ele levanta (a tela desenha o "
				+ "nocaute antes da esfera); a regua de cima reprovaria", subiu, $"esfera {subiu}");
		srv.PalcoNM_Limpar();
		yield return 1.0;
	}
}
