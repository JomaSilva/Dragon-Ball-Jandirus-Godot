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
///   C. (2026-10-20) O CELL CACA OS ANDROIDES: o Cell Imperfeito, com o jogador mais perto, vai no 17 (prioridade maxima), derruba um
///      androide e o absorve -- o Semi-Perfeito; e na cena da transformacao ele fica parado, sem raio na mao e intocavel;
///   A. OS ANDROIDES: o Scan e o Sense com um humano e tres androides (os dois tipos do jogador e o 17 NPC) -- so o humano aparece; e o
///      contra-exemplo, o Scan de antes, com os tres;
///   N. NAMEK: o Freeza de Namek caido quando o prazo vence -- sem Death Ball; e o contra-exemplo, o jogo de antes: a esfera sobre o corpo caido.
///   F. (2026-10-20) O FREEZA NA CENA DA PROPRIA TRANSFORMACAO: na troca de corpo e na forma final ele fica parado, sem raio na mao e intocavel.
///   D. (2026-10-20) O DOBRO DO JOGADOR MAIS FORTE: com 1,9 bilhao, doze segundos de Kamehameha na cara do Super Perfeito de 5 -- ele de pe; e o
///      contra-exemplo, o Super Perfeito de antes (a ficha de recem-criado), que o mesmo raio derruba.
///   P. (2026-10-20) O BOTAO PULAR do cartao Sagas da aba Admin: o Freeza de Namek chega agora, e o Pular da saga dele a fecha vencida, com ele
///      fora do mundo -- em DOIS cliques (o primeiro so pede a confirmacao); o Resetar tambem pede, e desarma sozinho; e o contra-exemplo, o
///      botao de um clique so (`MenuJogo.SagaDeUmCliqueDeTeste`).
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
		foreach (object passo in ACaca(mundo, srv, eu)) yield return passo;
		foreach (object passo in OsAndroides(cli, srv, eu)) yield return passo;
		foreach (object passo in Namek(mundo, srv, eu)) yield return passo;
		foreach (object passo in OFreezaNaCena(mundo, srv, eu)) yield return passo;
		foreach (object passo in ODobro(mundo, srv, eu)) yield return passo;
		foreach (object passo in PularASaga(cli, srv)) yield return passo;
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
		// (2026-10-20) a linha do tiro fica livre a cada espera (`PalcoTC_LinhaLivreDeNovo`): o bicho que o povoamento poe perto de mim levava o raio
		for (int i = 0; i < 80 && (raio = srv.PalcoTC_RaioDoKamehameha().Id) == 0; i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.1; }
		ProjetilDesenhado? desenho = null;
		for (int i = 0; i < 20 && raio != 0 && (desenho = mundo.TiroDesenhadoDeTeste(raio)) == null; i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.05; }
		// o raio chegando: a foto sai com a cabeca desenhada a menos de quatro tiles de mim (a camera me segue)
		for (int i = 0; i < 80 && desenho != null && IsInstanceValid(desenho) && mundo.PosicaoLocal is { } aqui
						&& desenho.Position.DistanceTo(aqui) > 4 * 32; i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.02; }
		Fotografar("tc-7-kamehameha-o-feixe-2x");
		double escala = srv.PalcoTC_RaioDoKamehameha().Escala;
		float desenhada = desenho != null && IsInstanceValid(desenho) ? desenho.Escala : 0;
		Afirmar("**...e o raio sai: o feixe do Kamehameha da tecnica, 2x o tamanho normal** -- e desenhado assim na minha tela",
				raio != 0 && Math.Abs(escala - 2) < 1e-6 && Math.Abs(desenhada - 2f) < 0.06f,
				$"raio {raio}; escala no servidor {escala:0.##}; desenhada {desenhada:0.##}");
		bool explodiu = false;
		for (int i = 0; i < 100 && !(explodiu = srv.PalcoTC_TerraExplodindo()); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.05; }
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
		for (int i = 0; i < 120 && montou && !srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.1; }
		bool abriu = srv.PalcoTC_EmEmbate();
		yield return 0.3;
		int raios = mundo.TirosDesenhados().Count(t => t.Tipo == Jandirus.Core.Combat.TipoDeProjetil.Beam);
		Fotografar("tc-9-o-embate-contra-o-kamehameha");
		Afirmar("**O EMBATE: o meu Kamehameha contra o dele, 2x** -- a disputa de ki abre, com os dois raios na minha tela",
				abriu && raios >= 2, $"montou {montou}; abriu {abriu}; raios desenhados {raios}");
		// a disputa: eu aperto cada letra -- o servidor nao aperta por quem tem teclado
		for (int i = 0; i < 900 && srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_ApertarNoEmbate(eu); srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.02; }
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
		for (int i = 0; i < 40 && cell != 0 && !srv.PalcoTC_CarregandoOKamehameha(); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.1; }
		yield return 1.5;
		(bool carregando, int degrau, bool torneio, double tronco, bool caido) = srv.PalcoTC_OSuperPerfeito();
		Fotografar("tc-11-super-perfeito-perdendo-carrega-o-kamehameha");
		Afirmar("**O SUPER PERFEITO PERDENDO SOLTA O KAMEHAMEHA** -- o tronco abaixo de 25%, na luta livre (sem torneio): ele junta as maos, na minha tela",
				cell != 0 && carregando && degrau == 3 && !torneio && !caido && tronco < 0.25 && mundo.CorpoDeTeste(cell) != null,
				$"cell {cell}; carregando {carregando}; degrau {degrau + 1}; torneio {torneio}; caido {caido}; tronco {tronco:P0}");

		// ---- eu entro na disputa com um milesimo do BP dele e nao aperto nada: PERCO -- o raio dele segue (eu vou pro fim dele, pra camera), e a
		// Terra explode
		bool montou = cell != 0 && srv.PalcoTC_DisputarOKamehameha(eu, vezesOBp: 0.001);
		for (int i = 0; i < 120 && montou && !srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.1; }
		bool abriu = srv.PalcoTC_EmEmbate();
		for (int i = 0; i < 600 && srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.05; }
		srv.PalcoTC_IrProFimDoKamehameha(eu);
		bool explodiu = false;
		for (int i = 0; i < 300 && !(explodiu = srv.PalcoTC_TerraExplodindo()); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.05; }
		yield return 0.25;
		Fotografar("tc-12-perdi-a-disputa-a-terra-explode");
		Afirmar("**EU PERCO A DISPUTA CONTRA O SUPER PERFEITO: o Kamehameha dele segue, e a Terra explode** (*\"Se o jogador perder o clash obviamente a "
				+ "terra tb explode\"*)", abriu && explodiu, $"montou {montou}; abriu {abriu}; explodiu {explodiu}");
		srv.PalcoTC_DevolverOBp();
		srv.PalcoTC_DevolverATerra(eu);
		yield return 1.5;

		// ---- de novo, NOCAUTEADO por um golpe alto de uma vez (o tronco a 10%, no chao): o vigia o levanta, e ele carrega do mesmo jeito
		cell = srv.PalcoTC_SuperPerfeitoPerdendo(eu, tilesAOeste: 6, nocauteado: true);
		for (int i = 0; i < 40 && cell != 0 && !srv.PalcoTC_CarregandoOKamehameha(); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.1; }
		yield return 1.5;
		(carregando, degrau, torneio, _, caido) = srv.PalcoTC_OSuperPerfeito();
		Fotografar("tc-13-nocauteado-ele-levanta-e-carrega-o-kamehameha");
		Afirmar("**NOCAUTEADO POR UM GOLPE ALTO, ELE LEVANTA E CARREGA O KAMEHAMEHA** -- de pe e intocavel na carga, na minha tela",
				cell != 0 && carregando && degrau == 3 && !torneio && !caido && mundo.CorpoDeTeste(cell) != null,
				$"cell {cell}; carregando {carregando}; degrau {degrau + 1}; torneio {torneio}; caido {caido}");

		// ---- ...e agora eu VENCO a disputa (cem vezes o BP dele, apertando cada letra): o Super Perfeito e desintegrado, e a Terra fica inteira
		montou = cell != 0 && srv.PalcoTC_DisputarOKamehameha(eu, vezesOBp: 100);
		for (int i = 0; i < 120 && montou && !srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.1; }
		abriu = srv.PalcoTC_EmEmbate();
		for (int i = 0; i < 900 && srv.PalcoTC_EmEmbate(); i++) { srv.PalcoTC_ApertarNoEmbate(eu); srv.PalcoTC_LinhaLivreDeNovo(); yield return 0.02; }
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
	// C. O CELL CACA OS ANDROIDES (dono, 2026-10-20)
	// =====================================================================
	private IEnumerable ACaca(World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- C: o Cell Imperfeito caca os androides ---");
		int cell = srv.PalcoTC_CellCacaOsAndroides(eu);
		int a17 = srv.PalcoTC_O17();
		// o monitor da saga roda uma vez por segundo: em ate 4 s o alvo imposto e o 17 -- e eu estou mais perto dele
		for (int i = 0; i < 40 && cell != 0 && srv.PalcoTC_ACaca().Alvo != a17; i++) yield return 0.1;
		yield return 1.5;
		(int alvo, int degrau, int androides) = srv.PalcoTC_ACaca();
		Fotografar("tc-15-cell-imperfeito-caca-o-androide");
		Afirmar("**O CELL IMPERFEITO CACA O ANDROIDE, PRIORIDADE MAXIMA** -- eu estou mais perto, e o alvo dele e o 17, na minha tela",
				cell != 0 && alvo == a17 && degrau == 0 && androides == 2 && mundo.CorpoDeTeste(cell) != null && mundo.CorpoDeTeste(a17) != null,
				$"cell {cell}; alvo {alvo} (o 17 {a17}, eu {eu}); degrau {degrau + 1}; androides {androides}");
		// ...ate derrubar um androide e absorve-lo: o Semi-Perfeito. Os androides andam e a caca e refeita a cada segundo (o de pe mais perto):
		// o primeiro pode ser qualquer um dos dois. A camera vai ate o Cell antes da foto.
		for (int i = 0; i < 900 && cell != 0 && srv.PalcoTC_ACaca().Degrau == 0; i++) yield return 0.1;
		srv.PalcoTC_IrProCell(eu);
		var naCena = srv.PalcoTC_CellNaCena();
		yield return 2.0;
		var depois = srv.PalcoTC_CellNaCena();
		(alvo, degrau, androides) = srv.PalcoTC_ACaca();
		Fotografar("tc-16-cell-absorveu-o-androide-semi-perfeito");
		Afirmar("**...ELE DERRUBA UM ANDROIDE E O ABSORVE** -- o Cell vira Semi-Perfeito, e um dos dois sai do mundo", degrau == 1 && androides == 1,
				$"degrau {degrau + 1}; androides no mundo {androides}");
		Afirmar("**E NA CENA DA TRANSFORMACAO ELE NAO AGE** (*\"faça o cell nao atacar ou se mover enquanto se transforma\"*): dois segundos de cena -- "
				+ "parado, sem raio na mao, intocavel",
				naCena.EmCena && depois.EmCena && depois.Intocavel && !naCena.Canal && !depois.Canal
				&& Math.Abs(naCena.X - depois.X) < 1 && Math.Abs(naCena.Y - depois.Y) < 1,
				$"em cena {naCena.EmCena}/{depois.EmCena}; intocavel {depois.Intocavel}; raio na mao {naCena.Canal}/{depois.Canal}; "
				+ $"andou {Math.Abs(naCena.X - depois.X) + Math.Abs(naCena.Y - depois.Y):0.#} px");
		srv.PalcoTC_LimparACaca();
		yield return 1.0;
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

	// =====================================================================
	// F. O FREEZA NA CENA DA PROPRIA TRANSFORMACAO (dono, 2026-10-20)
	// =====================================================================
	private IEnumerable OFreezaNaCena(World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- F: o Freeza na cena da propria transformacao ---");
		// (a Death Ball do capitulo N cai na tela quando o Freeza de la sai do mundo com ela no ar: ela termina de cair antes, e este Freeza nasce
		// do outro lado, a oeste -- na foto da g8 ela o cobriu)
		yield return 3.0;
		// ---- a troca de corpo (a cena curta, 2,5 s): ele larga o Death Beam que carregava e fica intocavel
		int freeza = srv.PalcoFZ_FreezaNaCena(eu, degrau: 0);
		for (int i = 0; i < 15 && freeza != 0 && mundo.CorpoDeTeste(freeza) == null; i++) yield return 0.05;
		yield return 0.4;
		var troca = srv.PalcoFZ_ANaCena();
		Fotografar("fz-1-freeza-troca-de-corpo-larga-o-raio");
		Afirmar("**O FREEZA NA TROCA DE CORPO LARGA O RAIO E FICA INTOCAVEL** (*\"todo chefe de saga fica parado, sem atacar e intocável na cena da "
				+ "própria transformação (o Freeza também)\"*) -- na minha tela",
				freeza != 0 && mundo.CorpoDeTeste(freeza) != null && troca.EmCena && troca.Intocavel && !troca.Canal,
				$"freeza {freeza}; em cena {troca.EmCena}; intocavel {troca.Intocavel}; raio na mao {troca.Canal}");

		// ---- a forma final (a cena grande, 28 s): dois segundos de cena -- parado, sem raio na mao, intocavel
		freeza = srv.PalcoFZ_FreezaNaCena(eu, degrau: 3);
		for (int i = 0; i < 15 && freeza != 0 && mundo.CorpoDeTeste(freeza) == null; i++) yield return 0.05;
		var naCena = srv.PalcoFZ_ANaCena();
		yield return 2.0;
		var depois = srv.PalcoFZ_ANaCena();
		Fotografar("fz-2-freeza-forma-final-parado-na-cena");
		Afirmar("**E NA FORMA FINAL ELE NAO AGE**: dois segundos de cena -- parado, sem raio na mao, intocavel, na minha tela",
				freeza != 0 && mundo.CorpoDeTeste(freeza) != null && naCena.EmCena && depois.EmCena && depois.Intocavel && !naCena.Canal && !depois.Canal
				&& Math.Abs(naCena.X - depois.X) < 1 && Math.Abs(naCena.Y - depois.Y) < 1,
				$"freeza {freeza}; em cena {naCena.EmCena}/{depois.EmCena}; intocavel {depois.Intocavel}; raio na mao {naCena.Canal}/{depois.Canal}; "
				+ $"andou {Math.Abs(naCena.X - depois.X) + Math.Abs(naCena.Y - depois.Y):0.#} px");
		srv.PalcoNM_Limpar();
		yield return 1.0;
	}

	// =====================================================================
	// D. O DOBRO DO JOGADOR MAIS FORTE: O RAIO NA CARA DO SUPER PERFEITO (dono, 2026-10-20)
	// (2026-10-27, o pedido 138) O DOBRO VIROU 1,25x (*"25% acima do player mais forte e nao 2x, ta mt op 2x"*): o raio de quem tem 1,9 bilhao
	// pode derruba-lo agora. A regua e o TEMPO: ele aguenta mais que o dobro do que aguenta o Super Perfeito com a ficha de recem-criado (o
	// contra-exemplo, medido primeiro), ou fica de pe os doze segundos.
	// =====================================================================
	private IEnumerable ODobro(World mundo, GameServer srv, int eu)
	{
		GD.Print("[dtorneiocell] --- D: o Kamehameha na cara do Super Perfeito, com 1,25x o jogador mais forte (o pedido 138; era o dobro) ---");

		// o raio na cara dele, contado em decimos desde que sai da mao: em que decimo ele caiu (-1: de pe os doze segundos, ou o raio acabou)
		int caiuEm = -1;
		double menor = 1;
		IEnumerable ORaio(bool fotografar, string foto)
		{
			caiuEm = -1;
			menor = 1;
			for (int d = 0; d < 120; d++)
			{
				var agora = srv.PalcoTC_OCellDoRaio(eu);
				if (agora.Caido) { caiuEm = d; break; }
				if (!agora.Raio) break;
				menor = Math.Min(menor, agora.Tronco);
				if (fotografar && d == 20) Fotografar(foto);
				yield return 0.1;
			}
		}

		// ---- o contra-exemplo PRIMEIRO, pelo tempo dele: o Super Perfeito de antes, com a ficha de recem-criado -- o mesmo raio o derruba
		int cell = srv.PalcoTC_RaioNoSuperPerfeito(eu, comODefeito: true);
		for (int i = 0; i < 60 && cell != 0 && !srv.PalcoTC_OCellDoRaio(eu).Raio; i++) yield return 0.1;   // a carga
		foreach (object passo in ORaio(false, "")) yield return passo;
		int caiuAntes = caiuEm;
		yield return 0.5;
		Fotografar("tc-18-contra-exemplo-o-super-perfeito-de-antes-cai");
		Afirmar("   CONTRA-EXEMPLO (o Super Perfeito de antes, com a ficha de recem-criado): o mesmo raio o derruba -- a regua de baixo reprovaria",
				caiuAntes >= 0, $"caiu em ~{caiuAntes / 10.0:0.#} s; tronco {srv.PalcoTC_OCellDoRaio(eu).Tronco:P0}");
		srv.PalcoTC_LargarORaio(eu);
		srv.PalcoTC_LimparORaio();
		yield return 1.0;

		// ---- de verdade: o Super Perfeito com 1,25x os meus atributos e a pericia no maximo -- ate doze segundos de Kamehameha
		cell = srv.PalcoTC_RaioNoSuperPerfeito(eu, comODefeito: false);
		for (int i = 0; i < 60 && cell != 0 && !srv.PalcoTC_OCellDoRaio(eu).Raio; i++) yield return 0.1;   // a carga
		bool saiu = srv.PalcoTC_OCellDoRaio(eu).Raio;
		foreach (object passo in ORaio(true, "tc-17-kamehameha-na-cara-do-super-perfeito")) yield return passo;
		srv.PalcoTC_LargarORaio(eu);
		Afirmar("**O SUPER PERFEITO DE 1,25x AGUENTA O RAIO BEM MAIS QUE O DE ANTES** (o pedido 138; *\"eu tinha 1,9B no ssj grade 4 + buffs e o cell "
				+ "5bi e ainda venci tranquilamente\"*): com 1,9 bilhao, o Kamehameha na cara dele -- ele toma (o tronco desce) e aguenta mais que o dobro "
				+ "do tempo do de antes, ou fica de pe os doze segundos",
				cell != 0 && saiu && mundo.CorpoDeTeste(cell) != null && menor < 1 && caiuAntes >= 0 && (caiuEm < 0 || caiuEm > 2 * caiuAntes),
				$"cell {cell}; o raio saiu {saiu}; {(caiuEm < 0 ? "de pe" : $"caiu em ~{caiuEm / 10.0:0.#} s")} (o de antes, ~{caiuAntes / 10.0:0.#} s); "
				+ $"o menor tronco {menor:P0}");
		srv.PalcoTC_LimparORaio();
		yield return 1.0;
	}

	// =====================================================================
	// P. O BOTAO PULAR DO CARTAO SAGAS (dono, 2026-10-20)
	// =====================================================================
	/// <summary>O botao de uma saga no cartao Sagas da aba Admin, pelo metadado (`agora_saga`, `pular_saga`) e o id dela.</summary>
	private static Button? BotaoDaSaga(string meta, string id) =>
		MenuJogo.Instancia is { } menu ? BotaoComMeta(menu, meta, id) : null;

	private static Button? BotaoComMeta(Node n, string meta, string id)
	{
		foreach (Node f in n.GetChildren())
		{
			if (f is Button b && b.IsVisibleInTree() && b.HasMeta(meta) && b.GetMeta(meta).AsString() == id) return b;
			if (BotaoComMeta(f, meta, id) is { } achou) return achou;
		}
		return null;
	}

	private static int QuantosComMeta(Node n, string meta)
	{
		int q = 0;
		foreach (Node f in n.GetChildren())
		{
			if (f is Button b && b.IsVisibleInTree() && b.HasMeta(meta)) q++;
			q += QuantosComMeta(f, meta);
		}
		return q;
	}

	/// <summary>Rola a aba Admin ate o cartao Sagas ficar na tela (o cartao leva a meta `titulo`).</summary>
	private static void MostrarOCartaoDasSagas()
	{
		if (MenuJogo.Instancia is not { } menu || AcharCartao(menu, "Sagas") is not { } cartao) return;
		for (Node? p = cartao.GetParent(); p != null; p = p.GetParent())
			if (p is ScrollContainer rol) { rol.EnsureControlVisible(cartao); break; }
	}

	private static Control? AcharCartao(Node n, string titulo)
	{
		foreach (Node f in n.GetChildren())
		{
			if (f is PanelContainer c && c.IsVisibleInTree() && c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == titulo) return c;
			if (AcharCartao(f, titulo) is { } achou) return achou;
		}
		return null;
	}

	private IEnumerable PularASaga(GameClient cli, GameServer srv)
	{
		GD.Print("[dtorneiocell] --- P: o botao Pular do cartao Sagas (a aba Admin) ---");
		// ---- o Freeza de Namek chega agora (o botao de antes), pra o Pular ter quem matar
		AbrirAba("Admin");
		yield return 0.6;
		if (MenuJogo.Instancia is { } menu && Botao(menu, "Ver as sagas") is { } ver) ver.EmitSignal(BaseButton.SignalName.Pressed);
		for (int i = 0; i < 40 && BotaoDaSaga("agora_saga", "freeza_namek") == null; i++) yield return 0.2;
		BotaoDaSaga("agora_saga", "freeza_namek")?.EmitSignal(BaseButton.SignalName.Pressed);
		FecharOMenu();   // a cena da chegada se desenha sobre o mundo, e nao sobre a lista
		for (int i = 0; i < 40 && !srv.PalcoSP_ASaga("freeza_namek").ChefeNoMundo; i++) yield return 0.2;
		var antes = srv.PalcoSP_ASaga("freeza_namek");
		yield return CenaDaChegada.Duracao + 0.5;

		// ---- o Pular: a aba Admin de novo, "Ver as sagas" (a lista com o Freeza no mundo), e o Pular da saga dele
		AbrirAba("Admin");
		yield return 0.6;
		if (MenuJogo.Instancia is { } menu2 && Botao(menu2, "Ver as sagas") is { } ver2) ver2.EmitSignal(BaseButton.SignalName.Pressed);
		for (int i = 0; i < 40 && BotaoDaSaga("pular_saga", "freeza_namek") == null; i++) yield return 0.2;
		yield return 0.6;
		int comOBotao = MenuJogo.Instancia is { } m3 ? QuantosComMeta(m3, "pular_saga") : 0;
		MostrarOCartaoDasSagas();
		yield return 0.3;
		Fotografar("sp-1-o-botao-pular-em-cada-saga");

		// o PRIMEIRO clique so pede a confirmacao: o texto muda, e a saga segue em curso com ele no mundo
		BotaoDaSaga("pular_saga", "freeza_namek")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 1.0;
		string textoArmado = BotaoDaSaga("pular_saga", "freeza_namek")?.Text ?? "";
		var noMeio = srv.PalcoSP_ASaga("freeza_namek");
		MostrarOCartaoDasSagas();
		yield return 0.3;
		Fotografar("sp-2-o-primeiro-clique-pede-confirmacao");
		Afirmar("**O PULAR PEDE CONFIRMACAO** (*\"o primeiro clique pede confirmação, o segundo faz\"*): o primeiro clique so arma -- o botao vira "
				+ "\"Confirmar o pular?\" e a saga segue em curso, com o Freeza no mundo",
				textoArmado == "Confirmar o pular?" && noMeio.Fase == "EmCurso" && noMeio.ChefeNoMundo,
				$"texto \"{textoArmado}\"; saga {noMeio.Fase}; no mundo {noMeio.ChefeNoMundo}");

		// ...e o SEGUNDO faz
		BotaoDaSaga("pular_saga", "freeza_namek")?.EmitSignal(BaseButton.SignalName.Pressed);
		for (int i = 0; i < 40 && srv.PalcoSP_ASaga("freeza_namek").Fase != "Vencida"; i++) yield return 0.2;
		yield return 1.2;   // a lista volta com o estado novo
		MostrarOCartaoDasSagas();
		yield return 0.3;
		var depois = srv.PalcoSP_ASaga("freeza_namek");
		bool naLista = cli.Escolhas is { Verbo: "admin_sagas" } l
					   && l.Itens.Any(x => x.StartsWith("freeza_namek|", StringComparison.Ordinal) && x.Contains("vencida") && x.Contains("derrotado"));
		Fotografar("sp-3-o-segundo-clique-pula-a-saga-do-freeza");
		Afirmar("**O BOTAO PULAR** (*\"adicione um botao q pula uma saga ja matando o boss\"*): em cada saga do cartao Sagas, ao lado do Resetar e do "
				+ "Chegar agora -- e o da saga do Freeza de Namek, com ele no mundo, a fecha VENCIDA: ele derrotado e fora do mundo, no servidor e na lista",
				comOBotao >= 4 && antes.ChefeNoMundo && depois.Fase == "Vencida" && !depois.ChefeNoMundo && naLista,
				$"botoes {comOBotao}; antes: {antes.Fase}, no mundo {antes.ChefeNoMundo}; depois: {depois.Fase}, no mundo {depois.ChefeNoMundo}; "
				+ $"na lista {naLista}");

		// ---- o Resetar tambem pede confirmacao -- e o primeiro clique sozinho desarma em quatro segundos: a saga fica como estava
		string faseAntes = srv.PalcoSP_ASaga("freeza_vegeta").Fase;
		BotaoDaSaga("resetar_saga", "freeza_vegeta")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 1.0;
		string textoDoReset = BotaoDaSaga("resetar_saga", "freeza_vegeta")?.Text ?? "";
		yield return 4.5;
		string textoDepois = BotaoDaSaga("resetar_saga", "freeza_vegeta")?.Text ?? "";
		string faseDepois = srv.PalcoSP_ASaga("freeza_vegeta").Fase;
		Afirmar("**...E O RESETAR TAMBEM**: o primeiro clique vira \"Confirmar o reset?\", e sem o segundo o botao desarma sozinho em quatro segundos "
				+ "-- a saga fica como estava",
				textoDoReset == "Confirmar o reset?" && textoDepois == "Resetar" && faseDepois == faseAntes,
				$"no primeiro clique \"{textoDoReset}\"; depois \"{textoDepois}\"; saga {faseAntes} -> {faseDepois}");

		// ---- o contra-exemplo: o botao de antes, de um clique so -- o Pular da saga do Boo a fecha na hora
		bool pulouNumClique = false;
		MenuJogo.SagaDeUmCliqueDeTeste = true;
		try
		{
			if (MenuJogo.Instancia is { } m4 && Botao(m4, "Ver as sagas") is { } ver3) ver3.EmitSignal(BaseButton.SignalName.Pressed);
			yield return 1.5;   // a lista volta, e o cartao se refaz com os botoes de um clique
			BotaoDaSaga("pular_saga", "boo")?.EmitSignal(BaseButton.SignalName.Pressed);
			for (int i = 0; i < 10 && srv.PalcoSP_ASaga("boo").Fase != "Vencida"; i++) yield return 0.1;
			pulouNumClique = srv.PalcoSP_ASaga("boo").Fase == "Vencida";
		}
		finally { MenuJogo.SagaDeUmCliqueDeTeste = false; }
		Afirmar("   CONTRA-EXEMPLO (o botao de antes, de um clique so): o Pular da saga do Boo a fecharia no primeiro clique -- a regua de cima reprovaria",
				pulouNumClique, $"pulou num clique {pulouNumClique}");
		FecharOMenu();
		yield return 0.5;
	}
}
