using System.Collections;
using Godot;
using Jandirus.Core.Social;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO ARRANJO DA TELA (`--diagjanelas`) -- a bancada do pedido do dono de 2026-10-10: o menu P como janela
/// (arrastar, redimensionar, destacar numa janela do sistema), o chat e os dois paineis do topo redimensionaveis, a
/// secao "Interface" das opcoes e o "Restaurar a interface padrao".
///
/// ELE MEXE COMO O DEDO MEXERIA: os arrastos sao eventos de mouse de verdade empurrados na tela (apertar na
/// borda, andar, soltar) -- passam pelo teste de "o mouse esta em cima de que", pela alca e pelo que ha embaixo
/// dela. As teclas da janela destacada sao empurradas NA TELA DELA. O que ele le e o retangulo de cada peca NA
/// TELA, e nao o numero que mandou.
///
/// O QUE ELE COBRA, por familia:
///   1. o PADRAO e o de sempre (menu centrado 760 x 580, chat 540 x 280, paineis a 100%, nada desenhado a mais),
///      e ABRIR AS OPCOES nao mexe nele;
///   2. ARRASTAR o menu pela barra do titulo; ele nao sai da tela; o clique numa borda nao vaza pro mundo;
///   3. REDIMENSIONAR pequeno (as quatro bordas e o canto; todas as abas continuam dentro do painel, com rolagem)
///      e grande (a tela inteira; a pagina se estica) -- com um cartao da aba People aberto nos dois;
///   4. DESTACAR: a janela do sistema existe, na tela da janela do jogo, sem foco, com o conteudo VIVO; o jogo
///      continua recebendo teclado; P, TAB, ENTER, ESC e o X; a tecla que cai na janela e repassada; o campo de
///      busca nao deixa o personagem mudo; a janela lembra o lugar; monitor que sumiu = monitor principal;
///      ENCAIXAR; e, com a tela do jogo ESTICADA, o menu destacado sai na mesma escala;
///   5. o CHAT menor e maior, e a faixa da mineracao e o canto de assistir o torneio acompanhando;
///   6. os PAINEIS DO TOPO a 70% e a 160% (com a letra redesenhada, e nao esticada), e a faixa do radio, o chat e
///      a janela de desejos acompanhando;
///   7. as OPCOES (os deslizadores e o "Restaurar a interface padrao", inclusive com o menu destacado);
///   8. o `config.json`: gravado, relido pela porta de producao, e com valores absurdos travados.
/// Cada regra tem o seu defeito injetado -- a checagem tem de reprovar com ele ligado.
///
/// O QUE ELE NAO PROVA: arrastar de verdade uma janela do sistema de um monitor pro outro, o alt-tab e o foco
/// real do teclado (a janela de bancada nasce SEM foco de proposito: o dono esta usando o PC). Isso e roteiro de
/// mao, no relatorio.
///
/// Rode com janela (no segundo monitor), no modo host:
///   --host --rede 8241 --horateste 0.5 --diagjanelas --raca Human --conta bancada_janelas_ag --nome Fulano --pasta &lt;fotos&gt;
/// </summary>
public partial class RoboDeJanelas : Node
{
	private const AlcaDeJanela.Lado E = AlcaDeJanela.Lado.Esquerda, D = AlcaDeJanela.Lado.Direita,
									 Ci = AlcaDeJanela.Lado.Cima, B = AlcaDeJanela.Lado.Baixo;

	private int _ok, _falhou, _naoMedidos;
	private readonly List<string> _vermelhas = [], _fotos = [];
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;
	private string _pasta = "";

	/// <summary>Com isto ligado o robo conta (e engole) todo clique de mouse que SOBROU pro mundo.</summary>
	private bool _vigiando;
	private int _vazados;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[djanelas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[djanelas]   FALHA {nome}   [{detalhe}]"); }
	}

	private void NaoMedido(string nome, string porque)
	{
		_naoMedidos++;
		GD.Print($"[djanelas]   --    NAO MEDIDO: {nome}   [{porque}]");
	}

	private static void Nota(string s) => GD.Print("[djanelas] " + s);

	/// <summary>
	/// O ROBO E O ULTIMO FILHO DO BOOT, entao ouve o `_UnhandledInput` ANTES do mundo: o que chega aqui e
	/// exatamente o clique que a interface nao parou -- o que viraria alvo marcado ou Zanzoken.
	/// </summary>
	public override void _UnhandledInput(InputEvent e)
	{
		if (!_vigiando || e is not InputEventMouseButton { Pressed: true }) return;
		_vazados++;
		GetViewport().SetInputAsHandled();
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null || MenuJogo.Instancia == null
			|| Chat.Instancia == null || Hud.Instancia == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		try
		{
			_roteiro ??= Roteiro().GetEnumerator();
			if (!_roteiro.MoveNext()) { Fim(); return; }
			_espera = _roteiro.Current is double d ? d : 0;
		}
		catch (Exception e)
		{
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			Fim();
		}
	}

	private void Fim()
	{
		_acabou = true;
		_vigiando = false;
		// TODO DEFEITO DESLIGADO, aconteca o que acontecer no roteiro.
		Janelas.SemTravaDeTeste = Janelas.SemConferirOMonitorDeTeste = Janelas.NaoGravaDeTeste = false;
		AlcaDeJanela.VazaOCliqueDeTeste = false;
		MenuJogo.SemRepasseDeTeclasDeTeste = MenuJogo.FocoPresoNaJanelaDeTeste = false;
		MenuJogo.JanelaComOTecladoDeTeste = null;
		MenuDeInteracao.DicaPorCimaDoMenuDeTeste = false;
		PainelDeMineracao.NumerosFixosDoChatDeTeste = false;
		PainelDeAssistir.AlturaFixaDoChatDeTeste = false;
		RadioOuvido.SemAEscalaDoPainelDeTeste = false;
		ModoDeConstruir.AlcaNaoEInterfaceDeTeste = false;
		PauseMenu.AbrirAsOpcoesMexeDeTeste = false;
		Hud.LetraEsticadaDeTeste = Hud.SemReligarAFonteDeTeste = false;
		if (_baseDaTela is { } b0 && GetTree()?.Root is { } raizDoJogo) raizDoJogo.ContentScaleSize = b0;
		Soltar("move_right"); Soltar("move_left"); Soltar("move_up"); Soltar("move_down");
		if (PauseMenu.Instancia is { Aberto: true } p) p.Fechar("bancada");

		// A TELA DE VOLTA AO DE FABRICA, mesmo se o roteiro parou no meio (o menu destacado se encaixa aqui).
		try { if (!Janelas.Arranjo.EDeFabrica()) Janelas.Restaurar(); }
		catch (Exception e) { GD.PrintErr("[djanelas] nao deu pra restaurar o arranjo no fim: " + e.Message); }

		foreach (string f in _fotos) Nota("foto " + f);
		if (_naoMedidos > 0) Nota($"{_naoMedidos} checagem(ns) NAO MEDIDA(S) (ver as linhas \"--\")");
		if (_falhou > 0) GD.PrintErr("[djanelas] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print(_falhou == 0 ? $"\n[djanelas] ===== TUDO OK ({_ok} OK) =====" : $"\n[djanelas] ===== {_falhou} FALHA(S), {_ok} OK =====");
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	private Image? Fotografar(string nome, Viewport? de = null)
	{
		// SEM JANELA NAO HA FOTO, e nem se pergunta: no `--headless` o `GetImage` imprime um erro do motor por chamada.
		if (DisplayServer.GetName() == "headless") return null;
		Image? img = (de ?? GetViewport())?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto pra {nome} (headless nao renderiza): rode com janela"); return null; }
		string caminho = _pasta + nome + ".png";
		img.SavePng(caminho);
		_fotos.Add(caminho);
		return img;
	}

	private static bool Igual(Rect2 a, Rect2 b, float tol = 0.75f) =>
		Math.Abs(a.Position.X - b.Position.X) <= tol && Math.Abs(a.Position.Y - b.Position.Y) <= tol
		&& Math.Abs(a.Size.X - b.Size.X) <= tol && Math.Abs(a.Size.Y - b.Size.Y) <= tol;

	private static bool Igual(Vector2 a, Vector2 b, float tol = 0.75f) => Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;

	/// <summary>Um retangulo esta dentro de outro, com meio pixel de folga (o arredondamento da tela).</summary>
	private static bool Dentro(Rect2 fora, Rect2 dentro) => fora.Grow(0.75f).Encloses(dentro);

	private void Mover(Vector2 p, bool apertado = false, Viewport? tela = null) =>
		(tela ?? GetViewport()).PushInput(new InputEventMouseMotion
		{
			Position = p, GlobalPosition = p, ButtonMask = apertado ? MouseButtonMask.Left : 0,
		}, true);

	private void Botao(Vector2 p, bool desce, bool duplo = false, Viewport? tela = null) =>
		(tela ?? GetViewport()).PushInput(new InputEventMouseButton
		{
			ButtonIndex = MouseButton.Left, Pressed = desce, DoubleClick = duplo, Position = p, GlobalPosition = p,
			ButtonMask = desce ? MouseButtonMask.Left : 0,
		}, true);

	/// <summary>O ARRASTO DO DEDO: o mouse chega, aperta, anda em passos e solta. Eventos de verdade, na tela.</summary>
	private void Arrastar(Vector2 de, Vector2 ate, int passos = 6)
	{
		Mover(de);
		Botao(de, true);
		for (int i = 1; i <= passos; i++) Mover(de.Lerp(ate, i / (float)passos), apertado: true);
		Botao(ate, false);
	}

	/// <summary>Um clique (ou um duplo clique: dois apertos, o segundo marcado) num ponto da tela.</summary>
	private void Clicar(Vector2 p, bool duplo = false, Viewport? tela = null)
	{
		Mover(p, tela: tela);
		Botao(p, true, tela: tela);
		Botao(p, false, tela: tela);
		if (!duplo) return;
		Botao(p, true, duplo: true, tela: tela);
		Botao(p, false, tela: tela);
	}

	private static InputEventKey Tecla(Key k, bool desce = true) =>
		new() { Keycode = k, PhysicalKeycode = k, Unicode = desce && k >= Key.A && k <= Key.Z ? (long)char.ToLowerInvariant((char)k) : 0, Pressed = desce };

	private static void Segurar(string acao) => Godot.Input.ActionPress(Teclas.NomeNoMotor(acao));
	private static void Soltar(string acao) => Godot.Input.ActionRelease(Teclas.NomeNoMotor(acao));

	private static int PaineisDoHud() =>
		Hud.Instancia is { } h ? Todos(h).OfType<PanelContainer>().Count(p => p.Visible) : 0;

	private static Button? BotaoComTexto(Node raiz, string texto) =>
		Todos(raiz).OfType<Button>().FirstOrDefault(b => b.Text == texto);

	private static string Dica => MenuDeInteracao.Instancia?.DicaNaTela ?? "";

	/// <summary>A diferenca media (0 a 1, por canal) entre duas fotos num retangulo. 0 = os mesmos pixels.</summary>
	private static double Diferenca(Image a, Image b, Rect2I r)
	{
		int x0 = Math.Max(r.Position.X, 0), y0 = Math.Max(r.Position.Y, 0);
		int x1 = Math.Min(r.End.X, Math.Min(a.GetWidth(), b.GetWidth())), y1 = Math.Min(r.End.Y, Math.Min(a.GetHeight(), b.GetHeight()));
		double soma = 0;
		int n = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
				soma += (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B)) / 3.0;
				n++;
			}
		return n == 0 ? 0 : soma / n;
	}

	/// <summary>Grava um recorte de uma foto, ampliado 3x sem filtro -- pra olhar a letra de perto.</summary>
	private void GravarRecorte(Image foto, Rect2I r, string nome)
	{
		Image recorte = foto.GetRegion(r);
		recorte.Resize(r.Size.X * 3, r.Size.Y * 3, Image.Interpolation.Nearest);
		string caminho = _pasta + nome + ".png";
		recorte.SavePng(caminho);
		_fotos.Add(caminho);
	}

	/// <summary>
	/// Poe os seis deslizadores da secao Interface numa faixa qualquer, CALADOS: a proxima abertura das opcoes tem
	/// de acertar a faixa de todos -- que e o instante em que um deslizador avisa "mudei" sem ninguem ter mexido.
	/// </summary>
	private static void DesacertarOsDeslizadores(PauseMenu pausa)
	{
		foreach (string qual in new[] { "menu-l", "menu-a", "chat-l", "chat-a", "status", "corpo" })
		{
			if (pausa.DeslizadorDeInterfaceDeTeste(qual) is not { } s) continue;
			s.SetBlockSignals(true);
			s.MinValue = 0;
			s.MaxValue = 1;
			s.Value = 0;
			s.SetBlockSignals(false);
		}
	}

	/// <summary>O no desta aba que carrega este metadado com este valor (o cartao aberto, o botao de detalhes), ou nulo.</summary>
	private static Node? ComMeta(Node raiz, string meta, string valor) =>
		Todos(raiz).FirstOrDefault(n => n.HasMeta(meta) && n.GetMeta(meta).AsString() == valor && !n.IsQueuedForDeletion());

	/// <summary>A base de desenho da tela do jogo de antes de a bancada mexer nela (5b). Nula = nao mexeu.</summary>
	private Vector2I? _baseDaTela;

	private const string AssinaturaDoKuririn = "9000000102";

	/// <summary>
	/// TRES CONHECIDOS plantados na lista deste jogador (a porta de bancada do servidor, a mesma da `--diagconhecidos`):
	/// a aba People deixa de ser "ninguem ainda" e passa a ter cartoes -- um deles com historia, pra abrir no lugar.
	/// </summary>
	private bool PlantarConhecidos(GameClient cli)
	{
		if (S is not { } srv) return false;
		long agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		return srv.PlantarConhecidosDeTeste(cli.LocalId,
		[
			new Conhecido
			{
				Assinatura = "9000000101", Nome = "Gohan", Raca = "Halfbreed", Genero = "Male", Idade = 11,
				Familiaridade = 214, Relacao = Relacao.Amor, DesdeMs = agora - 400 * 86_400_000L,
				Historia = "Filho do Goku. Eu só queria estudar, mas a Terra continua precisando de mim.", DetalhesEm = agora,
			},
			new Conhecido
			{
				Assinatura = AssinaturaDoKuririn, Nome = "Kuririn", Raca = "Human", Genero = "Male", Idade = 31,
				Familiaridade = 62, Relacao = Relacao.Nenhuma, DesdeMs = agora - 3 * 86_400_000L, Titulo = "Turtle Hermit",
				Historia = "Treinei no templo Orin até fugir de lá, e bati na porta do Mestre Kame com uma revista debaixo do braço. "
						 + "Foi ali que conheci o Goku: primeiro rival, depois o melhor amigo que eu podia ter.\n\n"
						 + "Morri mais vezes do que gosto de contar. Continuo voltando, e continuo sendo o humano mais forte da Terra.",
				DetalhesEm = agora,
			},
			new Conhecido
			{
				Assinatura = "9000000103", Nome = "Bulma", Raca = "Human", Genero = "Female", Idade = 28,
				Familiaridade = 18, Relacao = Relacao.Nenhuma, DesdeMs = agora - 86_400_000L,
			},
		]);
	}

	/// <summary>
	/// Na aba People ABERTA, aperta "Ver detalhes" no cartao do Kuririn e espera o cartao abrir no lugar. Devolve o
	/// cartao aberto (o no com os fatos), ou nulo se ele nao abriu.
	/// </summary>
	private Control? _cartaoAberto;

	private IEnumerable AbrirOCartaoDoKuririn(MenuJogo menu)
	{
		_cartaoAberto = null;
		if (menu.PaginaDeTeste("People") is not { } pg) yield break;
		if (ComMeta(pg, "detalhes", "abertos") == null)
		{
			if (ComMeta(pg, "detalhes", AssinaturaDoKuririn) is not Button b || b.Disabled) yield break;
			b.EmitSignal(BaseButton.SignalName.Pressed);
		}
		for (int i = 0; i < 30; i++)
		{
			yield return 0.1;
			if (menu.PaginaDeTeste("People") is { } agora && ComMeta(agora, "detalhes", "abertos") is Control aberto)
			{
				_cartaoAberto = aberto;
				yield return 0.2;
				yield break;
			}
		}
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		_pasta = Arg("--pasta");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/') && !_pasta.EndsWith('\\')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);

		MenuJogo menu = MenuJogo.Instancia!;
		Chat chat = Chat.Instancia!;
		Hud hud = Hud.Instancia!;
		GameClient cli = C!;
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		var telaInteira = new Rect2(Vector2.Zero, tela);

		Nota("==================================================================================");
		Nota(" O ARRANJO DA TELA -- o menu P como janela, o chat e os paineis do topo redimensionaveis");
		Nota("==================================================================================");
		Nota($"  fotos em   : {_pasta}");
		Nota($"  tela       : {tela}  |  janela do jogo {DisplayServer.WindowGetSize()} em {DisplayServer.WindowGetPosition()}, "
		   + $"monitor {DisplayServer.WindowGetCurrentScreen()} de {DisplayServer.GetScreenCount()}  |  sem foco: {Janelas.SemFoco}");

		foreach (object o in F1_OPadrao(menu, chat, hud, tela)) yield return o;
		foreach (object o in F2_Arrastar(menu, cli, tela, telaInteira)) yield return o;
		foreach (object o in F3_Pequeno(menu, tela)) yield return o;
		foreach (object o in F4_Grande(menu, tela)) yield return o;
		foreach (object o in F5_Destacado(menu, cli, tela)) yield return o;
		foreach (object o in F5b_AEscalaDaJanela(menu, tela)) yield return o;
		foreach (object o in F6_OChat(menu, chat, hud, cli, tela, telaInteira)) yield return o;
		foreach (object o in F7_OsPaineisDoTopo(menu, chat, hud, cli, tela)) yield return o;
		foreach (object o in F8_AsOpcoes(menu, chat, hud, tela)) yield return o;
		foreach (object o in F9_OArquivo(menu, chat, hud, tela, telaInteira)) yield return o;
	}

	// ---------------------------------------------------------------------- 1. o padrao
	private Rect2 _menuDeFabrica, _chatDeFabrica;
	private string _dicaDoLugar = "";

	private IEnumerable F1_OPadrao(MenuJogo menu, Chat chat, Hud hud, Vector2 tela)
	{
		Nota("--- 1: o PADRAO e o de sempre ---");
		ArranjoDaInterface a = Janelas.Arranjo;
		Afirmar("(montagem) o arranjo desta sessao e o de fabrica (config.json novo)", a.EDeFabrica());

		// A DICA "[E] ..." DO LUGAR, com o menu fechado: e ela que o menu embutido vai ter de esconder.
		_dicaDoLugar = Dica;
		Rect2 dica = MenuDeInteracao.Instancia?.RetanguloDaDicaDeTeste ?? default;

		menu.Abrir();
		menu.IrPara("Stats");
		yield return 0.5;

		_menuDeFabrica = new Rect2(((tela - Janelas.MenuDeFabrica) / 2f).Round(), Janelas.MenuDeFabrica);
		_chatDeFabrica = new Rect2(12, tela.Y - 292, 540, 280);
		Afirmar("PADRAO: o menu P abre centrado, 760 x 580 -- o retangulo de sempre", Igual(menu.RetanguloDeTeste, _menuDeFabrica),
				$"{menu.RetanguloDeTeste} | esperado {_menuDeFabrica}");
		Afirmar("PADRAO: o chat fica onde sempre ficou (a 12 px das bordas, 540 x 280)",
				Chat.RetanguloNaTela is { } c0 && Igual(c0, _chatDeFabrica), $"{Chat.RetanguloNaTela} | esperado {_chatDeFabrica}");

		Rect2 status = Hud.RetanguloDoStatus ?? default, corpo = Hud.RetanguloDoCorpo ?? default;
		Afirmar("PADRAO: o painel de status a 100%, no canto de cima a esquerda (14, 14)",
				Igual(status.Position, new Vector2(14, 14)) && hud.EscalaDoStatusDeTeste == 1f && Igual(status.Size, hud.BaseDoStatusDeTeste)
				&& !hud.FonteAcompanhaDeTeste(true), $"{status} escala {hud.EscalaDoStatusDeTeste}");
		Afirmar("PADRAO: o painel do corpo a 100%, encostado a 14 px da direita e do alto",
				Math.Abs(corpo.End.X - (tela.X - 14)) <= 0.75f && Math.Abs(corpo.Position.Y - 14) <= 0.75f && hud.EscalaDoCorpoDeTeste == 1f
				&& !hud.FonteAcompanhaDeTeste(false), $"{corpo} escala {hud.EscalaDoCorpoDeTeste}");
		Afirmar("PADRAO: a faixa do radio comeca a 12 px da beirada do painel de status",
				Math.Abs(RadioOuvido.EsquerdaDaFaixaDeTeste - (status.End.X + 12)) <= 0.75f, $"{RadioOuvido.EsquerdaDaFaixaDeTeste} | status ate {status.End.X}");
		Afirmar("PADRAO: nenhuma alca desenha nada parada, e a rolagem horizontal da pagina continua desligada",
				!menu.MolduraDeTeste.AcesaDeTeste && !chat.AlcaDeTeste.AcesaDeTeste && !hud.AlcaDoStatusDeTeste.AcesaDeTeste
				&& !hud.AlcaDoCorpoDeTeste.AcesaDeTeste && menu.RolagemDeTeste.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled);
		Afirmar("o cabecalho do menu ganhou o botao \"Destacar\" (so onde ha janela do sistema pra abrir)",
				menu.BotaoDeDestacarDeTeste.Visible == Janelas.PodeDestacar && menu.BotaoDeDestacarDeTeste.Text == "Destacar",
				$"visivel {menu.BotaoDeDestacarDeTeste.Visible}, pode destacar {Janelas.PodeDestacar}");

		foreach (string aba in menu.AbasDeTeste)
		{
			menu.IrPara(aba);
			yield return 0.12;
			if (!Igual(menu.RetanguloDeTeste, _menuDeFabrica))
				Nota($"  (de fabrica a aba '{aba}' estica o painel: {menu.RetanguloDeTeste} -- e assim desde antes: a rolagem horizontal e desligada)");
		}
		menu.IrPara("Stats");
		yield return 0.25;
		Fotografar("janelas-01-padrao");

		// ---- a dica "[E] ..." nao e desenhada por cima do menu embutido ----
		if (_dicaDoLugar.Length == 0)
			NaoMedido("a dica \"[E] ...\" some embaixo do menu P embutido", "nao ha nada interagivel perto do berco desta rodada");
		else if (!_menuDeFabrica.Intersects(dica))
			NaoMedido("a dica \"[E] ...\" some embaixo do menu P embutido", $"nesta tela ({tela}) o menu de fabrica nao fica em cima da dica");
		else
		{
			Afirmar($"a dica \"{_dicaDoLugar}\" some embaixo do menu P embutido (ela caia em cima do rodape dele)",
					Dica == "" && menu.RetanguloDeTeste.Intersects(dica), $"dica '{Dica}' em {dica} | menu {menu.RetanguloDeTeste}");
			MenuDeInteracao.DicaPorCimaDoMenuDeTeste = true;
			try
			{
				yield return 0.2;
				Afirmar("(defeito injetado: a dica por cima do menu) ...com o defeito ela volta, em cima do menu -- a checagem sabe reprovar",
						Dica == _dicaDoLugar && menu.RetanguloDeTeste.Intersects(dica), $"dica '{Dica}'");
			}
			finally { MenuDeInteracao.DicaPorCimaDoMenuDeTeste = false; }
			yield return 0.2;
		}

		// ---- ABRIR AS OPCOES NAO MEXE EM NADA ----
		// Na primeira rodada desta bancada abrir a pausa encolhia o menu, o chat e os dois paineis pro minimo: os
		// deslizadores da secao Interface avisam "mudei" quando a FAIXA deles e acertada, e a tela tomava isso por
		// um gesto do jogador.
		if (PauseMenu.Instancia is not { } pausa) { NaoMedido("abrir as opcoes nao mexe no arranjo", "nao ha menu de pausa nesta sessao"); yield break; }
		DesacertarOsDeslizadores(pausa);
		pausa.Abrir();
		yield return 0.3;
		Afirmar("ABRIR AS OPCOES nao mexe em nada: o arranjo continua o de fabrica, e a secao Interface mostra 760 x 580, 540 x 280, 100% e 100%",
				Janelas.Arranjo.EDeFabrica() && pausa.ValorDeInterfaceDeTeste("menu-l") == "760 px" && pausa.ValorDeInterfaceDeTeste("menu-a") == "580 px"
				&& pausa.ValorDeInterfaceDeTeste("chat-l") == "540 px" && pausa.ValorDeInterfaceDeTeste("chat-a") == "280 px"
				&& pausa.ValorDeInterfaceDeTeste("status") == "100%" && pausa.ValorDeInterfaceDeTeste("corpo") == "100%"
				&& Igual(menu.RetanguloDeTeste, _menuDeFabrica) && hud.EscalaDoStatusDeTeste == 1f,
				$"de fabrica {Janelas.Arranjo.EDeFabrica()} | menu {pausa.ValorDeInterfaceDeTeste("menu-l")} x {pausa.ValorDeInterfaceDeTeste("menu-a")} | "
				+ $"chat {pausa.ValorDeInterfaceDeTeste("chat-l")} x {pausa.ValorDeInterfaceDeTeste("chat-a")} | {pausa.ValorDeInterfaceDeTeste("status")} {pausa.ValorDeInterfaceDeTeste("corpo")}");
		pausa.Fechar("bancada");
		yield return 0.2;
		PauseMenu.AbrirAsOpcoesMexeDeTeste = true;
		try
		{
			DesacertarOsDeslizadores(pausa);
			pausa.Abrir();
			yield return 0.3;
			Afirmar("(defeito injetado: abrir as opcoes mexe) ...com o defeito so ABRIR a pausa tira o arranjo do de fabrica -- a checagem sabe reprovar",
					!Janelas.Arranjo.EDeFabrica(), $"status {hud.EscalaDoStatusDeTeste} | chat {Chat.RetanguloNaTela}");
		}
		finally { PauseMenu.AbrirAsOpcoesMexeDeTeste = false; }
		pausa.Fechar("bancada");
		Janelas.Restaurar();
		yield return 0.3;
		Afirmar("(arrumacao) desfeito o defeito, tudo de fabrica de novo", Janelas.Arranjo.EDeFabrica() && Igual(menu.RetanguloDeTeste, _menuDeFabrica)
				&& Chat.RetanguloNaTela is { } c1 && Igual(c1, _chatDeFabrica) && hud.EscalaDoStatusDeTeste == 1f && hud.EscalaDoCorpoDeTeste == 1f);
	}

	// ---------------------------------------------------------------------- 2. arrastar
	private IEnumerable F2_Arrastar(MenuJogo menu, GameClient cli, Vector2 tela, Rect2 telaInteira)
	{
		Nota("--- 2: ARRASTAR o menu pela barra do titulo ---");
		ArranjoDaInterface a = Janelas.Arranjo;
		Rect2 antes = menu.RetanguloDeTeste;
		int alvoAntes = cli.AlvoId, zanzosAntes = M?.ZanzokensPedidosDeTeste ?? 0;
		var andar = new Vector2(150, -40);

		_vigiando = true;
		_vazados = 0;
		Vector2 pega = menu.PuxadorDeTeste.GetGlobalRect().GetCenter();
		Arrastar(pega, pega + andar);
		yield return 0.25;
		Rect2 depois = menu.RetanguloDeTeste;
		Afirmar("ARRASTAR pela barra do titulo muda o menu de lugar: 150 px pra direita e 40 pra cima, com o mesmo tamanho",
				Igual(depois, new Rect2(antes.Position + andar, antes.Size)), $"{antes} -> {depois}");
		Afirmar("...o arranjo guarda o lugar novo como FRACAO da tela",
				a.MenuMexido && Math.Abs(a.MenuX * tela.X - depois.Position.X) <= 0.75f && Math.Abs(a.MenuLargura * tela.X - depois.Size.X) <= 0.75f,
				$"x {a.MenuX:0.####} largura {a.MenuLargura:0.####}");
		Afirmar("...e o arrasto nao vaza pro mundo (nenhum clique sobrou, o alvo e o Zanzoken ficaram como estavam)",
				_vazados == 0 && cli.AlvoId == alvoAntes && (M?.ZanzokensPedidosDeTeste ?? 0) == zanzosAntes, $"{_vazados} clique(s) vazado(s)");

		// ---- um duplo clique numa BORDA: e da alca, nao do mundo ----
		Vector2 naBorda = menu.MolduraDeTeste.PontoDeTeste(D);
		_vazados = 0;
		Clicar(naBorda, duplo: true);
		yield return 0.15;
		Afirmar("um duplo clique numa BORDA do menu nao chega ao mundo (nem marca alvo, nem da o Zanzoken) e nao muda o tamanho",
				_vazados == 0 && Igual(menu.RetanguloDeTeste, depois), $"{_vazados} vazado(s)");
		Mover(naBorda);
		yield return 0.1;
		Afirmar("...e a borda ACENDE com o mouse em cima (a alca so se desenha ali)", menu.MolduraDeTeste.AcesaDeTeste);
		Fotografar("janelas-02-arrastado-borda-acesa");
		AlcaDeJanela.VazaOCliqueDeTeste = true;
		try
		{
			_vazados = 0;
			Clicar(naBorda, duplo: true);
			yield return 0.15;
			Afirmar("(defeito injetado: a alca deixa o clique passar) ...com o defeito o mesmo duplo clique SOBRA pro mundo -- a checagem sabe reprovar",
					_vazados > 0, $"{_vazados} vazado(s)");
		}
		finally { AlcaDeJanela.VazaOCliqueDeTeste = false; }
		Mover(new Vector2(tela.X / 2f, tela.Y - 4f));   // tira o mouse da borda (e devolve o filtro da alca)
		yield return 0.1;

		// ---- arrastado pra FORA, ele para na borda da tela ----
		pega = menu.PuxadorDeTeste.GetGlobalRect().GetCenter();
		Arrastar(pega, pega + new Vector2(5000, 5000));
		yield return 0.2;
		Rect2 noCanto = menu.RetanguloDeTeste;
		Afirmar("arrastado pra FORA da tela, o menu para na borda: nenhum pedaco dele sai",
				Dentro(telaInteira, noCanto) && Igual(noCanto.End, tela) && Igual(noCanto.Size, antes.Size), $"{noCanto} | tela {tela}");
		Janelas.SemTravaDeTeste = true;
		try
		{
			pega = menu.PuxadorDeTeste.GetGlobalRect().GetCenter();
			Arrastar(pega, pega + new Vector2(300, 200));
			yield return 0.2;
			Afirmar("(defeito injetado: sem trava) ...com o defeito o menu SAI da tela -- a checagem sabe reprovar",
					!Dentro(telaInteira, menu.RetanguloDeTeste), $"{menu.RetanguloDeTeste}");
		}
		finally { Janelas.SemTravaDeTeste = false; }
		Janelas.Avisar();
		yield return 0.2;
		Afirmar("...e desfeito o defeito a trava o devolve pra dentro (ela roda a cada aplicacao, e nao so no arrasto)",
				Dentro(telaInteira, menu.RetanguloDeTeste), $"{menu.RetanguloDeTeste}");
		_vigiando = false;
	}

	// ---------------------------------------------------------------------- 3. pequeno
	private IEnumerable F3_Pequeno(MenuJogo menu, Vector2 tela)
	{
		Nota("--- 3: REDIMENSIONAR pequeno ---");
		// a aba People com gente dentro: tres conhecidos plantados no servidor (e um cartao pra abrir no lugar)
		bool comGente = C is { } cliente && PlantarConhecidos(cliente);
		for (int i = 0; comGente && i < 40 && C!.Conhecidos.Count < 3; i++) yield return 0.1;
		comGente = comGente && C!.Conhecidos.Count >= 3;
		if (!comGente) NaoMedido("a aba People com cartoes (e um aberto) no menu pequeno e no grande", "sem servidor neste processo pra plantar os conhecidos");

		// pro canto de cima a esquerda, pra ter pra onde crescer depois
		Vector2 pega = menu.PuxadorDeTeste.GetGlobalRect().GetCenter();
		Arrastar(pega, pega - new Vector2(5000, 5000));
		yield return 0.2;

		Vector2 canto = menu.MolduraDeTeste.PontoDeTeste(D | B);
		Arrastar(canto, canto - new Vector2(3000, 3000));
		yield return 0.3;
		Rect2 pequeno = menu.RetanguloDeTeste;
		Afirmar("REDIMENSIONAR pelo canto: puxado pra alem do minimo, o menu para em 520 x 320 -- e o canto oposto nao sai do lugar",
				Igual(pequeno.Size, Janelas.MenuMinimo) && Igual(pequeno.Position, Vector2.Zero), $"{pequeno}");
		Afirmar("...com o menu mexido a pagina ganha rolagem horizontal (e ela que deixa o menu ficar mais estreito que a aba)",
				menu.RolagemDeTeste.HorizontalScrollMode == ScrollContainer.ScrollMode.Auto);

		// ---- cada aba, no menu pequeno: o painel nao cresce, a pagina fica dentro dele ----
		int abas = 0, comRolagemH = 0, comRolagemV = 0;
		var ruins = new List<string>();
		foreach (string aba in menu.AbasDeTeste)
		{
			menu.IrPara(aba);
			yield return 0.2;
			abas++;
			Rect2 painel = menu.RetanguloDeTeste, rolagem = menu.RolagemDeTeste.GetGlobalRect();
			Control? pg = menu.PaginaDeTeste(aba);
			bool temTexto = pg != null && pg.IsVisibleInTree()
							&& Todos(pg).Any(n => n is Label { Text.Length: > 0 } l && l.IsVisibleInTree() || n is Button b && b.IsVisibleInTree());
			if (!Igual(painel, pequeno)) ruins.Add($"{aba}: o painel cresceu pra {painel.Size}");
			if (!Dentro(painel, rolagem)) ruins.Add($"{aba}: a pagina sai do painel ({rolagem})");
			if (!temTexto) ruins.Add($"{aba}: a pagina ficou vazia");
			if (menu.RolagemDeTeste.GetHScrollBar().Visible) comRolagemH++;
			if (menu.RolagemDeTeste.GetVScrollBar().Visible) comRolagemV++;
			if (aba is "Stats" or "People" or "Learning" or "World") Fotografar($"janelas-03-pequeno-{aba.ToLowerInvariant()}");

			// A ABA PEOPLE TEM CARTOES QUE ABREM NO LUGAR (os detalhes do conhecido): aberto, o cartao continua
			// dentro do painel -- quem o alcanca e a rolagem.
			if (aba == "People" && comGente)
			{
				foreach (object o in AbrirOCartaoDoKuririn(menu)) yield return o;
				bool abriu = _cartaoAberto != null && IsInstanceValid(_cartaoAberto);
				if (abriu) menu.RolagemDeTeste.EnsureControlVisible(_cartaoAberto!);
				yield return 0.25;
				Afirmar("PEOPLE no menu MINIMO: o cartao de um conhecido ABRE no lugar, o painel nao cresce e a pagina continua dentro dele (rola)",
						abriu && Igual(menu.RetanguloDeTeste, pequeno) && Dentro(menu.RetanguloDeTeste, menu.RolagemDeTeste.GetGlobalRect())
						&& menu.RolagemDeTeste.GetVScrollBar().Visible,
						$"abriu {abriu} | painel {menu.RetanguloDeTeste}");
				Fotografar("janelas-03-pequeno-people-cartao-aberto");
			}
		}
		Afirmar($"no menu MINIMO as {abas} abas abrem, tem o que ler, e nenhuma estica o painel nem sai dele",
				ruins.Count == 0, string.Join(" | ", ruins));
		Nota($"  no menu minimo: {comRolagemV} aba(s) com rolagem vertical, {comRolagemH} com rolagem horizontal");
		Afirmar("...e o que nao cabe ROLA (ha aba com barra de rolagem no menu minimo)", comRolagemV + comRolagemH > 0);

		// ---- fora do caminho, a dica "[E] ..." volta ----
		if (_dicaDoLugar.Length > 0)
		{
			menu.IrPara("Stats");
			yield return 0.2;
			Rect2 dica = MenuDeInteracao.Instancia?.RetanguloDaDicaDeTeste ?? default;
			Afirmar("com o menu FORA do caminho (encolhido no canto de cima), a dica \"[E] ...\" volta a aparecer",
					Dica == _dicaDoLugar && !menu.RetanguloDeTeste.Intersects(dica), $"dica '{Dica}' em {dica} | menu {menu.RetanguloDeTeste}");
		}

		// ---- as quatro bordas, uma de cada vez ----
		menu.IrPara("Stats");
		Arrastar(menu.MolduraDeTeste.PontoDeTeste(D), menu.MolduraDeTeste.PontoDeTeste(D) + new Vector2(80, 37));
		yield return 0.15;
		Rect2 r1 = menu.RetanguloDeTeste;
		Arrastar(menu.MolduraDeTeste.PontoDeTeste(B), menu.MolduraDeTeste.PontoDeTeste(B) + new Vector2(-23, 60));
		yield return 0.15;
		Rect2 r2 = menu.RetanguloDeTeste;
		Arrastar(menu.MolduraDeTeste.PontoDeTeste(E), menu.MolduraDeTeste.PontoDeTeste(E) + new Vector2(50, 11));
		yield return 0.15;
		Rect2 r3 = menu.RetanguloDeTeste;
		Arrastar(menu.MolduraDeTeste.PontoDeTeste(Ci), menu.MolduraDeTeste.PontoDeTeste(Ci) + new Vector2(9, 30));
		yield return 0.15;
		Rect2 r4 = menu.RetanguloDeTeste;
		Afirmar("as QUATRO BORDAS: a direita so muda a largura, a de baixo so a altura, a esquerda e a de cima andam sozinhas (a oposta fica)",
				Igual(r1, new Rect2(0, 0, 600, 320)) && Igual(r2, new Rect2(0, 0, 600, 380))
				&& Igual(r3, new Rect2(50, 0, 550, 380)) && Igual(r4, new Rect2(50, 30, 550, 350)), $"{r1} | {r2} | {r3} | {r4}");
	}

	// ---------------------------------------------------------------------- 4. grande
	private IEnumerable F4_Grande(MenuJogo menu, Vector2 tela)
	{
		Nota("--- 4: REDIMENSIONAR grande ---");
		Vector2 canto = menu.MolduraDeTeste.PontoDeTeste(D | B);
		Arrastar(canto, canto + new Vector2(5000, 5000));
		yield return 0.15;
		canto = menu.MolduraDeTeste.PontoDeTeste(E | Ci);
		Arrastar(canto, canto - new Vector2(5000, 5000));
		yield return 0.3;
		Afirmar("puxado pra alem da tela pelos dois cantos, o menu para na TELA INTEIRA (o maximo) -- e nao passa dela",
				Igual(menu.RetanguloDeTeste, new Rect2(Vector2.Zero, tela)), $"{menu.RetanguloDeTeste}");

		var ruins = new List<string>();
		foreach (string aba in new[] { "Stats", "People", "Learning", "Tech" })
		{
			menu.IrPara(aba);
			yield return 0.3;
			Control? pg = menu.PaginaDeTeste(aba);
			float util = menu.RolagemDeTeste.Size.X;
			if (pg == null || pg.Size.X < util - 24f) ruins.Add($"{aba}: a pagina tem {pg?.Size.X:0} px num vao de {util:0}");
			if (menu.RolagemDeTeste.GetHScrollBar().Visible) ruins.Add($"{aba}: sobrou rolagem horizontal no menu grande");
			Fotografar($"janelas-04-grande-{aba.ToLowerInvariant()}");
		}
		Afirmar("no menu GRANDE a pagina de cada aba se estica pra largura nova, sem rolagem horizontal", ruins.Count == 0, string.Join(" | ", ruins));

		if (C is { Conhecidos.Count: >= 3 })
		{
			menu.IrPara("People");
			yield return 0.3;
			foreach (object o in AbrirOCartaoDoKuririn(menu)) yield return o;
			bool abriu = _cartaoAberto != null && IsInstanceValid(_cartaoAberto);
			if (abriu) menu.RolagemDeTeste.EnsureControlVisible(_cartaoAberto!);
			yield return 0.25;
			Rect2 cartao = abriu ? _cartaoAberto!.GetGlobalRect() : default;
			Afirmar("PEOPLE no menu GRANDE: o cartao aberto se estica com a pagina (os detalhes ocupam a largura nova), sem rolagem horizontal",
					abriu && cartao.Size.X > Janelas.MenuDeFabrica.X && cartao.End.X <= menu.RolagemDeTeste.GetGlobalRect().End.X + 0.75f
					&& !menu.RolagemDeTeste.GetHScrollBar().Visible, $"abriu {abriu} | cartao {cartao} | pagina {menu.RolagemDeTeste.GetGlobalRect()}");
			Fotografar("janelas-04-grande-people-cartao-aberto");
		}

		// um tamanho de gente pro resto do roteiro: 700 x 520, perto do canto de cima a esquerda
		Janelas.GuardarMenu(Janelas.Arranjo, new Rect2(60, 40, 700, 520), tela);
		Janelas.Avisar();
		menu.IrPara("Stats");
		yield return 0.25;
		Afirmar("(montagem) o menu fica em 700 x 520 pro resto do roteiro", Igual(menu.RetanguloDeTeste, new Rect2(60, 40, 700, 520)), $"{menu.RetanguloDeTeste}");
	}

	// ---------------------------------------------------------------------- 5. destacado
	private IEnumerable F5_Destacado(MenuJogo menu, GameClient cli, Vector2 tela)
	{
		Nota("--- 5: DESTACAR numa janela do sistema ---");
		if (!Janelas.PodeDestacar)
		{
			NaoMedido("o menu P destacado numa janela do sistema", "este modo nao abre janela (headless): rode com --janela");
			yield break;
		}

		ArranjoDaInterface a = Janelas.Arranjo;
		var embutido = new Rect2(60, 40, 700, 520);
		Image? jogoAntes = Fotografar("janelas-05a-antes-de-destacar");

		menu.BotaoDeDestacarDeTeste.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.8;
		Window? janela = menu.JanelaDeTeste;
		Afirmar("o botao \"Destacar\" cria uma JANELA DO SISTEMA de verdade (nativa, nao embutida na tela do jogo) e grava a escolha",
				janela is { Visible: true } && !janela.IsEmbedded() && janela.GetWindowId() > 0 && a.MenuDestacado && menu.Destacado,
				$"janela {(janela == null ? "nula" : $"id {janela.GetWindowId()}, embutida {janela.IsEmbedded()}, visivel {janela.Visible}")}");
		if (janela == null) yield break;

		int id = janela.GetWindowId();
		int telaDaJanela = DisplayServer.WindowGetCurrentScreen(id), telaDoJogo = DisplayServer.WindowGetCurrentScreen();
		Rect2I util = DisplayServer.ScreenGetUsableRect(telaDaJanela);
		var lugar = new Rect2I(DisplayServer.WindowGetPosition(id), DisplayServer.WindowGetSize(id));
		Nota($"  a janela destacada: {lugar} no monitor {telaDaJanela} (area util {util}); a do jogo esta no monitor {telaDoJogo}");
		Afirmar("...ela nasce NO MONITOR DA JANELA DO JOGO (na bancada: o segundo), inteira dentro da area util dele",
				telaDaJanela == telaDoJogo && util.Encloses(lugar), $"{lugar} | util {util}");
		Rect2I sobre = new((Vector2I)(DisplayServer.WindowGetPosition() + embutido.Position), (Vector2I)embutido.Size);
		Afirmar("...em cima de onde o menu embutido estava, com o mesmo tamanho (o menu \"sai\" do lugar dele)",
				Math.Abs(lugar.Position.X - sobre.Position.X) <= 2 && Math.Abs(lugar.Position.Y - sobre.Position.Y) <= 40
				&& Math.Abs(lugar.Size.X - sobre.Size.X) <= 2 && Math.Abs(lugar.Size.Y - sobre.Size.Y) <= 2, $"{lugar} | painel embutido no desktop {sobre}");
		if (Janelas.SemFoco)
			Afirmar("...e SEM FOCO: a janela de bancada nao pega o teclado de quem esta usando o PC", janela.Unfocusable && !janela.HasFocus(),
					$"sem foco {janela.Unfocusable}, com o teclado {janela.HasFocus()}");
		else Nota($"  (fora da bancada a janela aceita o teclado: sem foco {janela.Unfocusable}, com o teclado {janela.HasFocus()})");

		Vector2 conteudo = janela.GetVisibleRect().Size;
		Afirmar("o painel do menu ENCHE a janela, e o menu nao esta mais na tela do jogo",
				Igual(menu.RetanguloDeTeste, new Rect2(Vector2.Zero, conteudo)) && MenuJogo.RetanguloNaTela == null && !MenuJogo.NaFrenteDoJogo
				&& menu.Visible && MenuJogo.Aberto && menu.BotaoDeDestacarDeTeste.Text == "Encaixar",
				$"painel {menu.RetanguloDeTeste} | conteudo da janela {conteudo} | botao '{menu.BotaoDeDestacarDeTeste.Text}'");
		Afirmar("a escala do conteudo e a da tela do jogo (aqui 1:1 -- letra nitida, sem esticar)",
				Math.Abs(janela.ContentScaleFactor - GetViewport().GetStretchTransform().Scale.X) < 0.03f,
				$"janela {janela.ContentScaleFactor:0.##}x, jogo {GetViewport().GetStretchTransform().Scale.X:0.##}x");
		// a dica "[E] ..." do lugar: destacado, o menu nao tapa nada (antes de o corpo andar pra longe dela)
		if (_dicaDoLugar.Length > 0)
			Afirmar("com o menu DESTACADO a dica \"[E] ...\" continua na tela do jogo (ele mora noutra janela, e nao a cobre)", Dica == _dicaDoLugar, $"dica '{Dica}'");

		yield return 0.3;
		Image? naJanela = Fotografar("janelas-05b-destacado-stats", janela);
		Image? jogoDepois = Fotografar("janelas-05c-o-jogo-sem-o-menu");
		if (naJanela != null && jogoAntes != null && jogoDepois != null)
		{
			// a foto da JANELA tem o menu (a chapa escura dele no meio); a do JOGO deixou de ter
			Color meioDaJanela = naJanela.GetPixel(naJanela.GetWidth() / 2, naJanela.GetHeight() - 6);
			var noJogo = new Vector2I(60 + 350, 40 + 514);
			Color antes = jogoAntes.GetPixel(noJogo.X, noJogo.Y), depois = jogoDepois.GetPixel(noJogo.X, noJogo.Y);
			Afirmar("NA FOTO: a janela destacada desenha o menu (a chapa dele), e na tela do jogo o lugar onde ele estava voltou a ser mundo",
					meioDaJanela.R < 0.12f && meioDaJanela.G < 0.12f && meioDaJanela.B < 0.16f
					&& (Math.Abs(antes.R - depois.R) + Math.Abs(antes.G - depois.G) + Math.Abs(antes.B - depois.B)) > 0.02f,
					$"janela {meioDaJanela} | jogo antes {antes} depois {depois}");
		}
		else NaoMedido("as fotos da janela destacada", "sem imagem");

		// ---- O CONTEUDO ESTA VIVO ----
		// (a) o clique de MOUSE na tela da janela troca de aba
		Button? abaKi = Todos(menu).OfType<Button>().FirstOrDefault(b => b.Text == "Ki" && b.ToggleMode);
		if (abaKi != null) Clicar(abaKi.GetGlobalRect().GetCenter(), tela: janela);
		yield return 0.3;
		Afirmar("VIVO: um clique de mouse na tela da janela troca de aba (a aba \"Ki\" abre la dentro)",
				menu.AbaDeTeste == "Ki" && menu.PaginaDeTeste("Ki") is { } pgKi && pgKi.IsVisibleInTree() && pgKi.GetViewport() == janela,
				$"aba {menu.AbaDeTeste}");
		Fotografar("janelas-05d-destacado-ki", janela);

		// (b) o que o SERVIDOR muda chega na janela: o zeni escrito por fora reescreve a linha "Zeni"
		menu.IrPara("Stats");
		yield return 0.3;
		if (S is { } srv && Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0)
		{
			string zeniAntes = menu.ValorDesenhado("Stats", "Zeni") ?? "(nulo)";
			double tinha = cli.Zeni;
			srv.ZeniPorForaDeTeste(cli.LocalId, 777);
			for (int i = 0; i < 40 && menu.ValorDesenhado("Stats", "Zeni") == zeniAntes; i++) yield return 0.1;
			string zeniDepois = menu.ValorDesenhado("Stats", "Zeni") ?? "(nulo)";
			Afirmar("VIVO: o que o servidor muda chega a janela destacada (o zeni escrito na ficha reescreve a linha \"Zeni\" da aba Stats)",
					zeniDepois == $"{tinha + 777:N0}" && zeniDepois != zeniAntes, $"'{zeniAntes}' -> '{zeniDepois}'");
			srv.ZeniPorForaDeTeste(cli.LocalId, -777);
			yield return 0.6;
		}
		else NaoMedido("o que o servidor muda chega a janela destacada", "sem servidor neste processo: rode com --host");

		// ---- O JOGO CONTINUA RECEBENDO O TECLADO ----
		int paineis = PaineisDoHud();
		Godot.Input.ParseInputEvent(Tecla(Key.Tab));
		Godot.Input.ParseInputEvent(Tecla(Key.Tab, false));
		yield return 0.3;
		int comAjuda = PaineisDoHud();
		Godot.Input.ParseInputEvent(Tecla(Key.Tab));
		Godot.Input.ParseInputEvent(Tecla(Key.Tab, false));
		yield return 0.3;
		Afirmar("com o menu destacado O JOGO CONTINUA OUVINDO O TECLADO: TAB abre e fecha a lista de teclas na tela dele",
				comAjuda == paineis + 1 && PaineisDoHud() == paineis, $"paineis do HUD {paineis} -> {comAjuda} -> {PaineisDoHud()}");

		Vector2 saiDe = M!.PosicaoLocal!.Value;
		float andou = 0;
		foreach (string acao in new[] { "move_right", "move_left", "move_down", "move_up" })
		{
			Segurar(acao);
			yield return 0.6;
			Soltar(acao);
			yield return 0.15;
			andou = (M.PosicaoLocal!.Value - saiDe).Length();
			if (andou > 6) break;
		}
		Afirmar("...e o corpo ANDA (o que se segura e lido do estado das teclas, que e um so pras duas janelas)", andou > 6, $"{andou:0.#} px");

		// ESC na tela do JOGO: nao e do menu destacado -- a pausa abre, o menu fica
		GetViewport().PushInput(Tecla(Key.Escape));
		yield return 0.3;
		bool pausou = PauseMenu.Instancia is { Aberto: true };
		Afirmar("ESC na janela do JOGO nao fecha o menu destacado: vai pra pausa, e o menu continua aberto na janela dele",
				pausou && menu.Visible && janela.Visible, $"pausa {pausou}, menu {menu.Visible}");
		if (pausou) PauseMenu.Instancia!.Fechar("bancada");
		yield return 0.2;

		// ---- A TECLA QUE CAI NA JANELA DO MENU E REPASSADA ----
		MenuJogo.SemRepasseDeTeclasDeTeste = true;
		try
		{
			janela.PushInput(Tecla(Key.P));
			janela.PushInput(Tecla(Key.P, false));
			yield return 0.3;
			Afirmar("(defeito injetado: a tecla morre na janela) ...sem o repasse o P apertado na janela do menu nao faz nada -- a checagem sabe reprovar",
					menu.Visible && janela.Visible, $"menu {menu.Visible}");
		}
		finally { MenuJogo.SemRepasseDeTeclasDeTeste = false; }

		Vector2I lugarAntes = janela.Position, tamanhoAntes = janela.Size;
		janela.PushInput(Tecla(Key.P));
		janela.PushInput(Tecla(Key.P, false));
		yield return 0.4;
		Afirmar("TECLA P apertada NA JANELA DO MENU e repassada pro jogo: o menu fecha (a janela some), e continua destacado",
				!menu.Visible && !MenuJogo.Aberto && !janela.Visible && menu.Destacado, $"menu {menu.Visible}, janela {janela.Visible}");

		GetViewport().PushInput(Tecla(Key.P));
		yield return 0.6;
		Afirmar("TECLA P na janela do JOGO reabre o menu NA JANELA DELE, no mesmo lugar e do mesmo tamanho",
				menu.Visible && janela.Visible && janela.Position == lugarAntes && janela.Size == tamanhoAntes,
				$"janela {janela.Visible} em {janela.Position} {janela.Size} | antes {lugarAntes} {tamanhoAntes}");

		// TAB apertado na janela do menu tambem chega ao jogo (qualquer atalho que se APERTA)
		paineis = PaineisDoHud();
		janela.PushInput(Tecla(Key.Tab));
		janela.PushInput(Tecla(Key.Tab, false));
		yield return 0.3;
		comAjuda = PaineisDoHud();
		janela.PushInput(Tecla(Key.Tab));
		janela.PushInput(Tecla(Key.Tab, false));
		yield return 0.3;
		Afirmar("...e nao so o P: o TAB apertado na janela do menu abre a lista de teclas NA TELA DO JOGO",
				comAjuda == paineis + 1 && PaineisDoHud() == paineis, $"paineis do HUD {paineis} -> {comAjuda} -> {PaineisDoHud()}");

		janela.PushInput(Tecla(Key.Enter));
		janela.PushInput(Tecla(Key.Enter, false));
		yield return 0.25;
		bool abriuOChat = Chat.Digitando;
		GetViewport().PushInput(Tecla(Key.Escape));   // o ESC do JOGO desiste da frase
		yield return 0.25;
		Afirmar("...o ENTER apertado na janela do menu abre a caixa do chat na tela do jogo (e o ESC de la a fecha, sem fechar o menu)",
				abriuOChat && !Chat.Digitando && menu.Visible && janela.Visible, $"abriu {abriuOChat}, depois do ESC {Chat.Digitando}");

		// ---- ESC NA JANELA DO MENU: desfaz uma camada, e por fim fecha ----
		LineEdit busca = menu.BuscaDeTeste;
		busca.GrabFocus();
		busca.Text = "ki";
		busca.EmitSignal(LineEdit.SignalName.TextChanged, "ki");
		yield return 0.25;
		janela.PushInput(Tecla(Key.Escape));
		yield return 0.25;
		bool limpou = busca.Text.Length == 0 && menu.Visible;
		janela.PushInput(Tecla(Key.Escape));
		yield return 0.4;
		Afirmar("ESC NA JANELA DO MENU e o ESC de sempre: o primeiro limpa a busca, o segundo fecha o menu",
				limpou && !menu.Visible && !janela.Visible, $"busca limpa {limpou}, menu {menu.Visible}");
		menu.Abrir();
		yield return 0.6;

		// ---- O CAMPO DE BUSCA NAO DEIXA O PERSONAGEM MUDO ----
		busca.GrabFocus();
		yield return 0.1;
		MenuJogo.JanelaComOTecladoDeTeste = true;
		bool escrevendo = MenuJogo.Digitando && Foco.Digitando;
		MenuJogo.JanelaComOTecladoDeTeste = false;
		bool calado = !MenuJogo.Digitando && !Foco.Digitando;
		Afirmar("escrevendo na busca da janela destacada as teclas sao DELA -- mas so enquanto a janela tem o teclado (com o foco no jogo, nao)",
				escrevendo && calado, $"com o teclado: {escrevendo} | sem: {calado}");
		MenuJogo.FocoPresoNaJanelaDeTeste = true;
		try
		{
			Afirmar("(defeito injetado: o foco preso na janela) ...com o defeito a busca esquecida aberta cala o jogo -- a checagem sabe reprovar",
					MenuJogo.Digitando && Foco.Digitando);
		}
		finally { MenuJogo.FocoPresoNaJanelaDeTeste = false; }
		// escrevendo na busca da janela, "p" e a letra p: nao e oferecida ao jogo, e o menu nao fecha
		MenuJogo.JanelaComOTecladoDeTeste = true;
		busca.GrabFocus();
		janela.PushInput(Tecla(Key.P));
		janela.PushInput(Tecla(Key.P, false));
		yield return 0.25;
		Afirmar("...e a letra digitada na busca da janela e SO da busca: \"p\" escreve p, e nao fecha o menu",
				menu.Visible && janela.Visible && busca.Text == "p", $"busca '{busca.Text}', menu {menu.Visible}");
		busca.Text = "";
		busca.EmitSignal(LineEdit.SignalName.TextChanged, "");
		yield return 0.2;

		MenuJogo.JanelaComOTecladoDeTeste = null;
		busca.GrabFocus();
		janela.EmitSignal(Window.SignalName.FocusExited);
		yield return 0.1;
		Afirmar("...e quando a janela do menu PERDE o teclado (um clique no jogo), o campo da busca solta o foco", !busca.HasFocus());

		// ---- O X DA JANELA ----
		janela.EmitSignal(Window.SignalName.CloseRequested);
		yield return 0.4;
		Afirmar("O X DA JANELA fecha o menu (como o P), e ele continua destacado: o proximo P reabre a janela",
				!menu.Visible && !janela.Visible && menu.Destacado && a.MenuDestacado, $"menu {menu.Visible}");
		menu.Abrir();
		yield return 0.6;

		// ---- ARRASTADA PELO SISTEMA: o retangulo muda POR FORA do jogo, como quando o jogador a puxa pela barra do Windows ----
		Vector2I puxada = janela.Position + new Vector2I(-30, 22), cantoDoMonitor = DisplayServer.ScreenGetPosition(telaDaJanela);
		DisplayServer.WindowSetPosition(puxada, janela.GetWindowId());
		yield return 0.5;
		Afirmar("ARRASTADA PELO SISTEMA (o Windows muda o retangulo por fora do jogo, como no arrasto pela barra de titulo), a janela lembra o lugar NA HORA, sem precisar fechar",
				janela.Position == puxada && a.JanelaLembrada && a.JanelaX == puxada.X - cantoDoMonitor.X && a.JanelaY == puxada.Y - cantoDoMonitor.Y,
				$"janela em {janela.Position} (puxada pra {puxada}) | lembrado {a.JanelaX},{a.JanelaY} no monitor {a.JanelaTela}");

		// ---- A JANELA LEMBRA O LUGAR ----
		Vector2I novoCanto = janela.Position + new Vector2I(46, 28), novoTamanho = janela.Size + new Vector2I(60, 40);
		janela.Position = novoCanto;
		janela.Size = novoTamanho;
		yield return 0.4;
		menu.Fechar();
		yield return 0.4;
		Vector2I origem = DisplayServer.ScreenGetPosition(telaDaJanela);
		Afirmar("movida e redimensionada, a janela LEMBRA o lugar ao fechar: monitor, canto (a partir do canto do monitor) e tamanho",
				a.JanelaLembrada && a.JanelaTela == telaDaJanela && a.JanelaX == novoCanto.X - origem.X && a.JanelaY == novoCanto.Y - origem.Y
				&& a.JanelaLargura == novoTamanho.X && a.JanelaAltura == novoTamanho.Y,
				$"gravado: monitor {a.JanelaTela}, canto {a.JanelaX},{a.JanelaY}, {a.JanelaLargura}x{a.JanelaAltura} | pedido {novoCanto - origem} {novoTamanho}");
		ArranjoDaInterface noDisco = ArranjoDaInterface.DoDisco();
		Afirmar("...e isso esta no config.json (uma sessao nova abre o menu destacado, no mesmo lugar)",
				noDisco.MenuDestacado && noDisco.JanelaLembrada && noDisco.JanelaTela == a.JanelaTela && noDisco.JanelaX == a.JanelaX
				&& noDisco.JanelaY == a.JanelaY && noDisco.JanelaLargura == a.JanelaLargura && noDisco.JanelaAltura == a.JanelaAltura);
		menu.Abrir();
		yield return 0.6;
		Afirmar("...e ao reabrir ela volta pro lugar lembrado", janela.Visible && janela.Position == novoCanto && janela.Size == novoTamanho,
				$"{janela.Position} {janela.Size}");
		Fotografar("janelas-05e-destacado-maior", janela);

		// ---- O MONITOR GRAVADO SUMIU (conta pura: a bancada nao abre janela no monitor principal do dono) ----
		List<Janelas.Monitor> monitores = Janelas.Monitores();
		int principal = DisplayServer.GetPrimaryScreen();
		var fantasma = new ArranjoDaInterface { MenuDestacado = true, JanelaLembrada = true, JanelaTela = monitores.Count + 3, JanelaX = 400, JanelaY = 300, JanelaLargura = 760, JanelaAltura = 580 };
		Vector2I moldura = Janelas.MolduraDeJanela(), minimo = (Vector2I)Janelas.JanelaMinima;
		(int onde, Rect2I la) = Janelas.LugarDaJanela(fantasma, monitores, principal, default, moldura, minimo);
		Afirmar("se o MONITOR gravado nao existe mais, a janela nasce no monitor PRINCIPAL, inteira na area util dele",
				onde == principal && monitores[principal].Util.Encloses(la) && la.Size == new Vector2I(760, 580), $"monitor {onde} (principal {principal}), {la}");
		Janelas.SemConferirOMonitorDeTeste = true;
		try
		{
			(int errado, Rect2I _) = Janelas.LugarDaJanela(fantasma, monitores, principal, default, moldura, minimo);
			Afirmar("(defeito injetado: o monitor gravado nao e conferido) ...com o defeito ela e mandada pro monitor que sumiu -- a checagem sabe reprovar",
					errado != principal && errado >= monitores.Count, $"monitor {errado} de {monitores.Count}");
		}
		finally { Janelas.SemConferirOMonitorDeTeste = false; }

		var foraDaTela = new ArranjoDaInterface { MenuDestacado = true, JanelaLembrada = true, JanelaTela = telaDaJanela, JanelaX = -5000, JanelaY = 90000, JanelaLargura = 20, JanelaAltura = 99999 };
		(int mesma, Rect2I presa) = Janelas.LugarDaJanela(foraDaTela, monitores, principal, default, moldura, minimo);
		Afirmar("um lugar gravado ABSURDO (fora do monitor, 20 px de largura, 99999 de altura) e preso: dentro da area util, do minimo ao tamanho do monitor",
				mesma == telaDaJanela && monitores[mesma].Util.Encloses(presa) && presa.Size.X >= minimo.X && presa.Size.Y <= monitores[mesma].Util.Size.Y - moldura.Y,
				$"{presa} | util {monitores[mesma].Util}");

		// ---- ENCAIXAR DE VOLTA ----
		menu.BotaoDeDestacarDeTeste.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.6;
		Afirmar("o botao \"Encaixar\" devolve o menu pra tela do jogo: a janela do sistema morre, e ele volta pro lugar embutido que tinha (700 x 520)",
				menu.JanelaDeTeste == null && !menu.Destacado && !a.MenuDestacado && menu.Visible && Igual(menu.RetanguloDeTeste, embutido)
				&& MenuJogo.NaFrenteDoJogo && menu.BotaoDeDestacarDeTeste.Text == "Destacar" && (!IsInstanceValid(janela) || janela.IsQueuedForDeletion()),
				$"{menu.RetanguloDeTeste} | destacado {menu.Destacado}");
		Button? abaBody = Todos(menu).OfType<Button>().FirstOrDefault(b => b.Text == "Body" && b.ToggleMode);
		if (abaBody != null) Clicar(abaBody.GetGlobalRect().GetCenter());
		yield return 0.3;
		Afirmar("...e continua vivo la: um clique de mouse na tela do JOGO troca de aba", menu.AbaDeTeste == "Body" && menu.PaginaDeTeste("Body") is { } pgB && pgB.IsVisibleInTree(),
				$"aba {menu.AbaDeTeste}");
		menu.IrPara("Stats");
		yield return 0.25;
		Fotografar("janelas-05f-encaixado-de-volta");
		Afirmar("...a moldura e a barra do titulo voltam a pegar o mouse (redimensionar e arrastar funcionam de novo)",
				menu.MolduraDeTeste.IsVisibleInTree() && menu.PuxadorDeTeste.IsVisibleInTree());
	}

	// ---------------------------------------------------------------------- 5b. a escala da janela
	/// <summary>
	/// O PROJETO ESTICA A TELA (`canvas_items`): em tela cheia de 1920 com base de 1280 tudo sai 1,5x maior. A
	/// bancada nao entra em tela cheia; ela encolhe a BASE de desenho (960 x 540 no mesmo vidro de 1280 x 720), que
	/// estica a tela do jogo 1,33x pela mesma conta -- e cobra que o menu destacado saia na mesma escala.
	/// </summary>
	private IEnumerable F5b_AEscalaDaJanela(MenuJogo menu, Vector2 tela)
	{
		Nota("--- 5b: a ESCALA do menu destacado com a tela do jogo esticada ---");
		if (!Janelas.PodeDestacar) { NaoMedido("a escala do menu destacado com a tela esticada", "este modo nao abre janela (headless)"); yield break; }

		Window raiz = GetTree().Root;
		_baseDaTela = raiz.ContentScaleSize;
		raiz.ContentScaleSize = new Vector2I(960, 540);
		yield return 0.4;
		float esticado = GetViewport().GetStretchTransform().Scale.X;
		Vector2 telaPequena = GetViewport().GetVisibleRect().Size;
		var embutido = new Rect2(40, 30, 640, 480);
		Janelas.Arranjo.JanelaLembrada = false;   // sem lugar gravado: a janela nasce em cima do painel embutido
		Janelas.GuardarMenu(Janelas.Arranjo, embutido, telaPequena);
		Janelas.Avisar();
		menu.Abrir();
		menu.IrPara("Stats");
		yield return 0.4;
		Afirmar("(montagem) a tela do jogo esta esticada 1,33x (base de 960 x 540 num vidro de 1280 x 720), com o menu embutido em 640 x 480",
				Math.Abs(esticado - 4f / 3f) < 0.02f && Igual(menu.RetanguloDeTeste, embutido), $"{esticado:0.###}x | tela {telaPequena} | menu {menu.RetanguloDeTeste}");
		Fotografar("janelas-05g-esticado-embutido");

		menu.BotaoDeDestacarDeTeste.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.8;
		if (menu.JanelaDeTeste is { } janela)
		{
			Vector2 conteudo = janela.GetVisibleRect().Size;
			Afirmar("com a tela ESTICADA o menu destacado sai na MESMA escala (1,33x): a janela tem o tamanho que o menu tinha no vidro, e la dentro cabe o mesmo conteudo",
					Math.Abs(janela.ContentScaleFactor - esticado) < 0.02f && Igual(conteudo, embutido.Size, 1.5f)
					&& Math.Abs(janela.Size.X - embutido.Size.X * esticado) <= 2f && Math.Abs(janela.Size.Y - embutido.Size.Y * esticado) <= 2f
					&& Igual(menu.RetanguloDeTeste, new Rect2(Vector2.Zero, conteudo)),
					$"escala {janela.ContentScaleFactor:0.###} | janela {janela.Size} | conteudo {conteudo}");
			yield return 0.3;
			Fotografar("janelas-05h-esticado-destacado", janela);
			menu.BotaoDeDestacarDeTeste.EmitSignal(BaseButton.SignalName.Pressed);
			yield return 0.6;
		}
		else Afirmar("com a tela esticada o botao \"Destacar\" abre a janela", false, "a janela nao nasceu");

		raiz.ContentScaleSize = _baseDaTela.Value;
		_baseDaTela = null;
		yield return 0.4;
		Janelas.Arranjo.JanelaLembrada = false;
		Janelas.GuardarMenu(Janelas.Arranjo, new Rect2(60, 40, 700, 520), tela);
		Janelas.Avisar();
		yield return 0.3;
		Afirmar("(arrumacao) a base de desenho de volta, e o menu embutido em 700 x 520",
				Igual(GetViewport().GetVisibleRect().Size, tela) && !menu.Destacado && Igual(menu.RetanguloDeTeste, new Rect2(60, 40, 700, 520)),
				$"tela {GetViewport().GetVisibleRect().Size} | menu {menu.RetanguloDeTeste}");
	}

	// ---------------------------------------------------------------------- 6. o chat
	private IEnumerable F6_OChat(MenuJogo menu, Chat chat, Hud hud, GameClient cli, Vector2 tela, Rect2 telaInteira)
	{
		Nota("--- 6: o CHAT menor e maior ---");
		menu.Fechar();
		yield return 0.3;
		ArranjoDaInterface a = Janelas.Arranjo;
		PainelDeMineracao? faixa = PainelDeMineracao.Instancia;
		PainelDeAssistir? assistir = hud.FindChild("Assistir", true, false) as PainelDeAssistir;
		float peDoStatus = Hud.RetanguloDoStatus?.End.Y ?? 0;

		// o canto de ASSISTIR o torneio, sozinho: em cima do chat
		Rect2 chatDeAgora = Chat.RetanguloNaTela ?? default;
		if (assistir != null)
		{
			assistir.MostrarDeTeste(true);
			yield return 0.2;
			Rect2 so = assistir.GetGlobalRect();
			Afirmar("o canto de ASSISTIR o torneio fica logo EM CIMA do chat, alinhado com ele, sem encostar (ele ficava 46 px por DENTRO do chat)",
					!so.Intersects(chatDeAgora) && Math.Abs(so.End.Y - (chatDeAgora.Position.Y - 10)) <= 0.75f && Math.Abs(so.Position.X - chatDeAgora.Position.X) <= 0.75f
					&& Hud.RetanguloDoStatus is { } st0 && !so.Intersects(st0), $"assistir {so} | chat {chatDeAgora}");
			PainelDeAssistir.AlturaFixaDoChatDeTeste = true;
			try
			{
				Rect2 velho = assistir.MostrarDeTeste(true);
				Afirmar("(defeito injetado: a altura do chat copiada em numero) ...com o defeito o canto de assistir entra no chat -- a checagem sabe reprovar",
						velho.Intersects(chatDeAgora), $"assistir {velho} | chat {chatDeAgora}");
			}
			finally { PainelDeAssistir.AlturaFixaDoChatDeTeste = false; }
			assistir.MostrarDeTeste(true);
		}

		// a faixa da mineracao na tela: o verb de verdade
		Verbos.PorChave("minerar")?.Acionar?.Invoke();
		for (int i = 0; i < 30 && faixa is not { NaTela: true }; i++) yield return 0.1;
		yield return 0.3;
		bool minerando = faixa is { NaTela: true };
		if (!minerando) NaoMedido("a faixa da mineracao acompanha o chat", "o corpo nao comecou a minerar neste berco");

		Rect2 c0 = Chat.RetanguloNaTela ?? default;
		if (minerando)
			Afirmar("PADRAO: a faixa da mineracao fica 8 px acima do chat, alinhada com ele pela esquerda (os numeros de sempre: 12 e 300)",
					Math.Abs(faixa!.RetanguloNaTela.Position.X - c0.Position.X) <= 0.75f && Math.Abs(faixa.RetanguloNaTela.End.Y - (c0.Position.Y - 8)) <= 0.75f
					&& Math.Abs(faixa.RetanguloNaTela.End.Y - (tela.Y - 300)) <= 0.75f, $"faixa {faixa.RetanguloNaTela} | chat {c0}");
		if (assistir != null && minerando)
		{
			yield return 0.2;
			Rect2 ra = assistir.GetGlobalRect(), rf = faixa!.RetanguloNaTela;
			Afirmar("...e com a faixa da mineracao tambem na tela os dois se arrumam sem se cobrir, sem cobrir o chat nem o painel de status, dentro da tela",
					!ra.Intersects(c0) && !ra.Intersects(rf) && Hud.RetanguloDoStatus is { } st1 && !ra.Intersects(st1) && !rf.Intersects(st1)
					&& Dentro(new Rect2(Vector2.Zero, tela), ra), $"assistir {ra} | faixa {rf} | chat {c0}");
		}

		// ---- MENOR: o canto de cima a direita puxado pra dentro, alem do minimo ----
		AlcaDeJanela alca = chat.AlcaDeTeste;
		_vigiando = true;
		_vazados = 0;
		Vector2 canto = alca.PontoDeTeste(Ci | D);
		Arrastar(canto, canto + new Vector2(-3000, 3000));
		yield return 0.4;
		Rect2 menor = Chat.RetanguloNaTela ?? default;
		Afirmar("CHAT MENOR: puxado pra alem do minimo ele para em 300 x 180, e o canto de baixo a esquerda nao sai do lugar",
				Igual(menor, new Rect2(12, tela.Y - 12 - Janelas.ChatMinimo.Y, Janelas.ChatMinimo.X, Janelas.ChatMinimo.Y)) && a.ChatMexido, $"{menor}");
		Afirmar("...as falas continuam DENTRO do painel (o texto encolhe junto), e o arrasto nao vazou pro mundo",
				Dentro(menor, chat.TextoDeTeste) && chat.TextoDeTeste.Size.Y >= 60 && _vazados == 0, $"texto {chat.TextoDeTeste} | {_vazados} vazado(s)");
		_vigiando = false;
		if (minerando)
		{
			Rect2 f = faixa!.RetanguloNaTela;
			Afirmar("...e a faixa da mineracao DESCE junto: continua 8 px acima do chat de verdade",
					Math.Abs(f.Position.X - menor.Position.X) <= 0.75f && Math.Abs(f.End.Y - (menor.Position.Y - 8)) <= 0.75f, $"faixa {f} | chat {menor}");
			PainelDeMineracao.NumerosFixosDoChatDeTeste = true;
			try
			{
				yield return 0.2;
				Rect2 solta = faixa.RetanguloNaTela;
				Afirmar("(defeito injetado: os numeros fixos do chat) ...com o defeito a faixa fica solta la em cima, longe do chat -- a checagem sabe reprovar",
						Math.Abs(solta.End.Y - (menor.Position.Y - 8)) > 20, $"faixa {solta} | chat {menor}");
			}
			finally { PainelDeMineracao.NumerosFixosDoChatDeTeste = false; }
			yield return 0.2;
		}
		if (assistir != null)
		{
			Rect2 ra = assistir.GetGlobalRect();
			Afirmar("...e o canto de assistir desce junto (com o chat pequeno ha vao: ele fica em cima da faixa, que fica em cima do chat)",
					!ra.Intersects(menor) && ra.End.Y <= menor.Position.Y && ra.End.Y > menor.Position.Y - 120 && (!minerando || !ra.Intersects(faixa!.RetanguloNaTela)),
					$"assistir {ra} | chat {menor}");
		}
		Fotografar("janelas-06a-chat-menor");

		// ---- MAIOR: puxado pra alem do teto ----
		canto = alca.PontoDeTeste(Ci | D);
		Arrastar(canto, canto + new Vector2(3000, -3000));
		yield return 0.4;
		Rect2 maior = Chat.RetanguloNaTela ?? default;
		Vector2 teto = Janelas.TetoDoChat(tela, Hud.PeDoStatus);
		var conviteComRadar = new Rect2(tela.X - 14 - (RadarNaTela.Diametro + RadarNaTela.Margem + 8) - 300, tela.Y - 152, 300, 112);
		Afirmar("CHAT MAIOR: puxado pra alem do teto ele para no MAIOR que a tela aceita -- a largura ate 58% da tela (sem entrar na reserva do canto direito), a altura ate 8 px do painel de status",
				Igual(maior.Size, teto) && maior.Size.X <= tela.X * Janelas.ChatLarguraMaximaNaTela + 0.75f && maior.Size.X > Janelas.ChatDeFabrica.X
				&& Math.Abs(maior.Position.Y - (peDoStatus + 8)) <= 1f && Igual(maior.Position + new Vector2(0, maior.Size.Y), new Vector2(12, tela.Y - 12)),
				$"{maior} | teto {teto} | status ate {peDoStatus}");
		Afirmar("...sem tapar o painel de status nem entrar no canto inferior direito (o disco do radar e o convite do torneio encostado nele)",
				Hud.RetanguloDoStatus is { } st && !maior.Intersects(st) && !maior.Intersects(conviteComRadar)
				&& RadarNaTela.Instancia is { } radar && !maior.Intersects(radar.GetGlobalRect()), $"chat {maior} | convite {conviteComRadar}");
		if (minerando)
		{
			yield return 0.2;
			Rect2 f = faixa!.RetanguloNaTela;
			Afirmar("...e a faixa da mineracao, sem vao em cima do chat, vai pra DIREITA dele (no pe) -- sem cobrir o chat nem o painel de status",
					!f.Intersects(maior) && Hud.RetanguloDoStatus is { } st2 && !f.Intersects(st2) && f.Position.X >= maior.End.X && Dentro(telaInteira, f),
					$"faixa {f} | chat {maior}");
		}
		Fotografar("janelas-06b-chat-maior");

		// ---- uma borda de cada vez, e um tamanho do meio pra foto com a faixa em cima ----
		Arrastar(alca.PontoDeTeste(D), alca.PontoDeTeste(D) + new Vector2(-200, 33));
		yield return 0.15;
		Rect2 so1 = Chat.RetanguloNaTela ?? default;
		Arrastar(alca.PontoDeTeste(Ci), alca.PontoDeTeste(Ci) + new Vector2(17, 150));
		yield return 0.3;
		Rect2 so2 = Chat.RetanguloNaTela ?? default;
		Afirmar("as duas bordas do chat: a da direita so muda a largura, a de cima so a altura",
				Igual(so1.Size, maior.Size - new Vector2(200, 0)) && Igual(so2.Size, maior.Size - new Vector2(200, 150)), $"{so1.Size} | {so2.Size}");
		if (minerando)
		{
			Rect2 f = faixa!.RetanguloNaTela;
			Afirmar("...com vao de novo, a faixa da mineracao volta pra cima do chat",
					Math.Abs(f.Position.X - so2.Position.X) <= 0.75f && Math.Abs(f.End.Y - (so2.Position.Y - 8)) <= 0.75f, $"faixa {f} | chat {so2}");
		}
		Fotografar("janelas-06c-chat-do-meio-com-a-faixa");

		if (assistir != null) assistir.MostrarDeTeste(false);
		if (minerando) Verbos.PorChave("minerar")?.Acionar?.Invoke();
		yield return 0.3;
	}

	// ---------------------------------------------------------------------- 7. os paineis do topo
	private IEnumerable F7_OsPaineisDoTopo(MenuJogo menu, Chat chat, Hud hud, GameClient cli, Vector2 tela)
	{
		Nota("--- 7: os PAINEIS DO TOPO a 70% e a 160% ---");
		ArranjoDaInterface a = Janelas.Arranjo;
		// o chat de fabrica de volta: e contra ele que os 160% se medem
		a.ChatMexido = false;
		Janelas.Avisar();
		yield return 0.3;
		Rect2 chatR = Chat.RetanguloNaTela ?? default;
		Vector2 baseS = hud.BaseDoStatusDeTeste, baseC = hud.BaseDoCorpoDeTeste;
		Nota($"  a 100%: status {baseS}, corpo {baseC}; chat {chatR}");

		// ---- 160%: os dois cantos puxados pra fora, alem do maximo ----
		_vigiando = true;
		_vazados = 0;
		Vector2 p = hud.AlcaDoStatusDeTeste.GetGlobalRect().GetCenter();
		Arrastar(p, p + baseS);   // uma diagonal inteira = +100%: pediria 200%
		p = hud.AlcaDoCorpoDeTeste.GetGlobalRect().GetCenter();
		Arrastar(p, p + new Vector2(-baseC.X, baseC.Y));
		yield return 0.4;
		_vigiando = false;

		float cabeS = MathF.Floor(Janelas.EscalaQueCabe(2f, baseS, tela, chatR.Position.Y - Janelas.FolgaEntrePecas) * 100f + 0.001f) / 100f;
		Rect2 status = Hud.RetanguloDoStatus ?? default, corpo = Hud.RetanguloDoCorpo ?? default;
		Afirmar($"PAINEL DE STATUS puxado pelo canto pra alem do maximo: para em {cabeS * 100:0}% (o teto e 160%, ou o chat), crescendo a partir do canto de cima a esquerda",
				Math.Abs(hud.EscalaDoStatusDeTeste - cabeS) < 0.011f && cabeS >= 1.5f && Igual(status.Position, new Vector2(14, 14)) && Igual(status.Size, baseS * cabeS, 1.5f)
				&& a.EscalaDoStatus == hud.EscalaDoStatusDeTeste, $"escala {hud.EscalaDoStatusDeTeste} | {status}");
		Afirmar("PAINEL DO CORPO puxado pelo canto pra alem do maximo: para em 160%, crescendo a partir do canto de cima a DIREITA",
				Math.Abs(hud.EscalaDoCorpoDeTeste - Janelas.EscalaMaxima) < 0.011f && Math.Abs(corpo.End.X - (tela.X - 14)) <= 1f
				&& Math.Abs(corpo.Position.Y - 14) <= 1f && Igual(corpo.Size, baseC * Janelas.EscalaMaxima, 1.5f), $"escala {hud.EscalaDoCorpoDeTeste} | {corpo}");
		Afirmar("...a letra acompanha a escala (a fonte e desenhada de novo no tamanho ampliado, e nao esticada), e o arrasto nao vazou pro mundo",
				hud.FonteAcompanhaDeTeste(true) && hud.FonteAcompanhaDeTeste(false) && _vazados == 0, $"{_vazados} vazado(s)");

		// ---- A LETRA A 160% E REDESENHADA, E NAO A DE 100% ESTICADA ----
		yield return 0.3;
		var daLetra = new Rect2I((int)status.Position.X + 8, (int)status.Position.Y + 8, (int)status.Size.X - 16, (int)(70 * cabeS));
		Image? nitida = Fotografar("janelas-07a0-status-a-160");
		Image? esticada = null;
		Hud.LetraEsticadaDeTeste = true;
		try
		{
			Janelas.Avisar();
			yield return 0.4;
			esticada = Fotografar("janelas-07a1-status-a-160-letra-esticada");
		}
		finally { Hud.LetraEsticadaDeTeste = false; }
		Janelas.Avisar();
		yield return 0.4;
		if (nitida != null && esticada != null)
		{
			double dif = Diferenca(nitida, esticada, daLetra);
			GravarRecorte(nitida, daLetra, "janelas-07a0-recorte-letra-redesenhada");
			GravarRecorte(esticada, daLetra, "janelas-07a1-recorte-letra-esticada-defeito");
			Afirmar("A LETRA A 160% E REDESENHADA no tamanho novo: a foto difere da foto com a letra de 100% esticada (o defeito injetado) -- a checagem sabe reprovar",
					dif > 0.004, $"diferenca media {dif:0.#####} no retangulo {daLetra}");
			if (dif <= 0.004 && Hud.Instancia?.GetChildOrNull<Control>(0)?.GetChildOrNull<Control>(0) is { } painel)
			{
				// SO SE A REGRA FALHOU: tres tentativas, cada uma com o seu recorte, pra saber o que o motor pede.
				foreach (Node n in Todos(painel)) if (n is CanvasItem ci) { ci.OversamplingWithScale = CanvasItem.OversamplingWithScaleEnum.Enabled; ci.QueueRedraw(); }
				yield return 0.4;
				if (Fotografar("janelas-07x1-cada-filho-ligado") is { } x1) { Nota($"  tentativa 1 (cada filho com a escala ligada): diferenca {Diferenca(x1, esticada, daLetra):0.#####}"); GravarRecorte(x1, daLetra, "janelas-07x1-recorte"); }
				GetViewport().OversamplingOverride = GetViewport().GetStretchTransform().Scale.X;
				foreach (Node n in Todos(painel)) if (n is CanvasItem ci) ci.QueueRedraw();
				yield return 0.4;
				if (Fotografar("janelas-07x2-com-a-tela-forcada") is { } x2) { Nota($"  tentativa 2 (mais o oversampling da tela escrito a mao): diferenca {Diferenca(x2, esticada, daLetra):0.#####}"); GravarRecorte(x2, daLetra, "janelas-07x2-recorte"); }
				GetViewport().OversamplingOverride = 1.6f * GetViewport().GetStretchTransform().Scale.X;
				foreach (Node n in Todos(painel)) if (n is CanvasItem ci) ci.QueueRedraw();
				yield return 0.4;
				if (Fotografar("janelas-07x3-a-tela-inteira-a-160") is { } x3) { Nota($"  tentativa 3 (a TELA inteira com oversampling 1,6): diferenca {Diferenca(x3, esticada, daLetra):0.#####}"); GravarRecorte(x3, daLetra, "janelas-07x3-recorte"); }
				GetViewport().OversamplingOverride = 0f;
				foreach (Node n in Todos(painel)) if (n is CanvasItem ci) { ci.OversamplingWithScale = CanvasItem.OversamplingWithScaleEnum.ParentNode; ci.QueueRedraw(); }
				Janelas.Avisar();
				yield return 0.3;
			}
		}
		else NaoMedido("a letra a 160% e redesenhada", "sem foto");

		// o que mora em volta, a 160%
		Afirmar("EM VOLTA (160%): a faixa do radio comeca 12 px depois da beirada DE VERDADE do painel de status",
				Math.Abs(RadioOuvido.EsquerdaDaFaixaDeTeste - (status.End.X + 12)) <= 0.75f, $"{RadioOuvido.EsquerdaDaFaixaDeTeste} | status ate {status.End.X}");
		RadioOuvido.SemAEscalaDoPainelDeTeste = true;
		try
		{
			Afirmar("(defeito injetado: a faixa do radio sem a escala do painel) ...com o defeito ela nasce POR CIMA do painel aumentado -- a checagem sabe reprovar",
					RadioOuvido.EsquerdaDaFaixaDeTeste < status.End.X, $"{RadioOuvido.EsquerdaDaFaixaDeTeste} | status ate {status.End.X}");
		}
		finally { RadioOuvido.SemAEscalaDoPainelDeTeste = false; }
		Afirmar("EM VOLTA (160%): o painel de status nao encosta no chat, e o teto do chat desce com ele",
				!status.Intersects(chatR) && status.End.Y <= chatR.Position.Y - Janelas.FolgaEntrePecas + 0.75f
				&& Math.Abs(Janelas.TetoDoChat(tela, Hud.PeDoStatus).Y - (tela.Y - 12 - status.End.Y - 8)) <= 1f,
				$"status ate {status.End.Y} | chat de {chatR.Position.Y} | teto do chat {Janelas.TetoDoChat(tela, Hud.PeDoStatus).Y}");
		// a janela de desejos mede o bloco do corpo NA TELA (`MenuDeDesejos.Assentar`): 24 de margem, 8 de folga, 420 de minimo
		Afirmar("EM VOLTA (160%): a janela de desejos ainda cabe EMBAIXO do painel do corpo (sobram os 420 px que ela pede)",
				tela.Y - 24 - (corpo.End.Y + 8) >= 420, $"sobram {tela.Y - 24 - (corpo.End.Y + 8):0} px");
		Rect2 radar = RadarNaTela.Instancia?.GetGlobalRect() ?? default;
		PainelDoTorneio? convite = hud.FindChild("Torneio", true, false) as PainelDoTorneio;
		convite?.Convidar(1, 30, "");
		yield return 0.2;
		Rect2 rc = convite?.GetGlobalRect() ?? default;
		Afirmar("EM VOLTA (160%): o canto inferior direito (o disco do radar, o convite do torneio) nao e alcancado pelo painel do corpo nem pelo de status",
				radar.HasArea() && rc.HasArea() && !corpo.Intersects(radar) && !corpo.Intersects(rc) && !status.Intersects(rc), $"corpo {corpo} | radar {radar} | convite {rc}");
		Fotografar("janelas-07a-paineis-a-160");
		convite?.Fechar();

		// a faixa da mineracao a 160%: sem vao em cima do chat, vai pro lado
		Verbos.PorChave("minerar")?.Acionar?.Invoke();
		PainelDeMineracao? faixa = PainelDeMineracao.Instancia;
		for (int i = 0; i < 30 && faixa is not { NaTela: true }; i++) yield return 0.1;
		yield return 0.3;
		if (faixa is { NaTela: true })
		{
			Rect2 f = faixa.RetanguloNaTela;
			Afirmar("EM VOLTA (160%): a faixa da mineracao nao cobre o painel de status aumentado nem o chat",
					!f.Intersects(status) && !f.Intersects(chatR), $"faixa {f} | status {status} | chat {chatR}");
			Fotografar("janelas-07b-paineis-a-160-minerando");
			Verbos.PorChave("minerar")?.Acionar?.Invoke();
			yield return 0.3;
		}

		// ---- o modo de construir nao ergue bloco no clique de uma alca ----
		GetViewport().PushInput(Tecla(Key.B));
		yield return 0.4;
		if (ModoDeConstruir.Ligado && ModoDeConstruir.Instancia is { } obra)
		{
			Vector2 naAlca = hud.AlcaDoStatusDeTeste.GetGlobalRect().GetCenter();
			Mover(new Vector2(tela.X * 0.62f, tela.Y * 0.4f));   // um ponto de chao, longe de qualquer painel
			yield return 0.25;
			bool fantasmaNoChao = obra.FantasmaDeTeste is { Visible: true };
			Mover(naAlca);
			yield return 0.25;
			bool fantasmaNaAlca = obra.FantasmaDeTeste is { Visible: true };
			if (!fantasmaNoChao) NaoMedido("o modo de construir nao ergue bloco no clique de uma alca", "o fantasma do bloco nao apareceu neste lugar");
			else
			{
				Afirmar("MODO DE CONSTRUIR: com o mouse numa ALCA o fantasma do bloco some (o clique e da alca, e nao ergue nada embaixo dela)", !fantasmaNaAlca);
				ModoDeConstruir.AlcaNaoEInterfaceDeTeste = true;
				try
				{
					yield return 0.25;
					Afirmar("(defeito injetado: a alca nao conta como interface) ...com o defeito o fantasma continua embaixo da alca -- a checagem sabe reprovar",
							obra.FantasmaDeTeste is { Visible: true });
				}
				finally { ModoDeConstruir.AlcaNaoEInterfaceDeTeste = false; }
			}
			Mover(new Vector2(tela.X / 2f, tela.Y / 2f));
			GetViewport().PushInput(Tecla(Key.B));
			yield return 0.3;
		}
		else NaoMedido("o modo de construir nao ergue bloco no clique de uma alca", "o modo de construir nao ligou neste lugar");

		// ---- 70%: os dois cantos puxados pra dentro, alem do minimo ----
		p = hud.AlcaDoStatusDeTeste.GetGlobalRect().GetCenter();
		Arrastar(p, p - baseS * 1.5f);
		p = hud.AlcaDoCorpoDeTeste.GetGlobalRect().GetCenter();
		Arrastar(p, p - new Vector2(-baseC.X, baseC.Y) * 1.5f);
		yield return 0.4;
		status = Hud.RetanguloDoStatus ?? default;
		corpo = Hud.RetanguloDoCorpo ?? default;
		Afirmar("puxados pra dentro alem do minimo, os dois paineis param em 70% -- cada um preso no seu canto",
				Math.Abs(hud.EscalaDoStatusDeTeste - Janelas.EscalaMinima) < 0.011f && Math.Abs(hud.EscalaDoCorpoDeTeste - Janelas.EscalaMinima) < 0.011f
				&& Igual(status.Position, new Vector2(14, 14)) && Igual(status.Size, baseS * Janelas.EscalaMinima, 1.5f)
				&& Math.Abs(corpo.End.X - (tela.X - 14)) <= 1f && Igual(corpo.Size, baseC * Janelas.EscalaMinima, 1.5f),
				$"status {hud.EscalaDoStatusDeTeste} {status} | corpo {hud.EscalaDoCorpoDeTeste} {corpo}");
		Afirmar("EM VOLTA (70%): a faixa do radio encosta mais perto (a beirada de verdade do painel encolhido), e o teto do chat sobe",
				Math.Abs(RadioOuvido.EsquerdaDaFaixaDeTeste - (status.End.X + 12)) <= 0.75f
				&& Math.Abs(Janelas.TetoDoChat(tela, Hud.PeDoStatus).Y - (tela.Y - 12 - status.End.Y - 8)) <= 1f,
				$"radio {RadioOuvido.EsquerdaDaFaixaDeTeste} | status ate {status.End} | teto do chat {Janelas.TetoDoChat(tela, Hud.PeDoStatus).Y}");
		Afirmar("...as alcas acompanham os paineis (cada uma no canto de baixo do painel dela, onde ele termina DE VERDADE)",
				Igual(hud.AlcaDoStatusDeTeste.GetGlobalRect().End, status.End) && Igual(hud.AlcaDoCorpoDeTeste.GetGlobalRect().Position + new Vector2(0, 18), new Vector2(corpo.Position.X, corpo.End.Y)),
				$"alca do status {hud.AlcaDoStatusDeTeste.GetGlobalRect()} | alca do corpo {hud.AlcaDoCorpoDeTeste.GetGlobalRect()}");
		Image? a70 = Fotografar("janelas-07c-paineis-a-70");

		// ---- DE 160% PRA 70%: A LETRA QUE O ARRASTO DEIXOU E A DE 70%, E NAO A DE 160% ESPREMIDA ----
		var daLetra70 = new Rect2I((int)status.Position.X + 6, (int)status.Position.Y + 6, (int)status.Size.X - 12, (int)(100 * Janelas.EscalaMinima));
		hud.RefazerALetraDeTeste();
		yield return 0.4;
		Image? refeita = Fotografar("janelas-07c1-paineis-a-70-letra-refeita");
		if (a70 != null && refeita != null)
		{
			double dif = Diferenca(a70, refeita, daLetra70);
			GravarRecorte(a70, daLetra70, "janelas-07c-recorte-letra-a-70");
			Afirmar("de 160% pra 70% a letra que o ARRASTO deixa na tela ja e a de 70%: a foto e igual a de depois de refazer a letra do zero",
					dif < 0.002, $"diferenca media {dif:0.#####} no retangulo {daLetra70}");
		}
		else NaoMedido("a letra a 70% e a certa", "sem foto");

		// o contra-exemplo: de volta a 160% (pelo caminho certo) e dali pra 70% SEM religar a fonte -- a letra de
		// 160% fica na tela, espremida. E a foto da segunda rodada desta bancada.
		Hud.PedirEscala(doStatus: true, 1.6f);
		yield return 0.4;
		Hud.SemReligarAFonteDeTeste = true;
		Image? velha = null;
		try
		{
			Hud.PedirEscala(doStatus: true, Janelas.EscalaMinima);
			yield return 0.4;
			velha = Fotografar("janelas-07c2-paineis-a-70-letra-de-160-defeito");
		}
		finally { Hud.SemReligarAFonteDeTeste = false; }
		hud.RefazerALetraDeTeste();
		yield return 0.4;
		if (velha != null && refeita != null)
		{
			double dif = Diferenca(velha, refeita, daLetra70);
			GravarRecorte(velha, daLetra70, "janelas-07c2-recorte-letra-de-160-defeito");
			Afirmar("(defeito injetado: a escala muda sem religar a fonte) ...com o defeito a letra de 160% fica na tela, espremida -- a checagem sabe reprovar",
					dif > 0.004, $"diferenca media {dif:0.#####}");
		}

		// ---- as bordas travadas nao respondem ----
		a.Travado = true;
		Janelas.Avisar();
		p = hud.AlcaDoStatusDeTeste.GetGlobalRect().GetCenter();
		Arrastar(p, p + baseS * 0.4f);
		yield return 0.2;
		Afirmar("com as BORDAS TRAVADAS (a opcao da secao Interface) puxar um canto nao muda nada", Math.Abs(hud.EscalaDoStatusDeTeste - Janelas.EscalaMinima) < 0.011f,
				$"escala {hud.EscalaDoStatusDeTeste}");
		a.Travado = false;
		Janelas.Avisar();
	}

	// ---------------------------------------------------------------------- 8. as opcoes
	private IEnumerable F8_AsOpcoes(MenuJogo menu, Chat chat, Hud hud, Vector2 tela)
	{
		Nota("--- 8: a secao \"Interface\" das opcoes, e o \"Restaurar a interface padrao\" ---");
		ArranjoDaInterface a = Janelas.Arranjo;
		if (PauseMenu.Instancia is not { } pausa) { NaoMedido("a secao Interface das opcoes", "nao ha menu de pausa nesta sessao"); yield break; }

		pausa.Abrir();
		yield return 0.4;
		Afirmar("as opcoes abrem MOSTRANDO o arranjo de agora (70% nos dois paineis), sem marcar nada como mexido so por abrir",
				pausa.ValorDeInterfaceDeTeste("status") == "70%" && pausa.ValorDeInterfaceDeTeste("corpo") == "70%" && !a.ChatMexido
				&& pausa.ValorDeInterfaceDeTeste("chat-l") == "540 px" && pausa.ValorDeInterfaceDeTeste("chat-a") == "280 px",
				$"status {pausa.ValorDeInterfaceDeTeste("status")}, corpo {pausa.ValorDeInterfaceDeTeste("corpo")}, chat {pausa.ValorDeInterfaceDeTeste("chat-l")} x {pausa.ValorDeInterfaceDeTeste("chat-a")}");

		pausa.DeslizadorDeInterfaceDeTeste("status")!.Value = 130;
		pausa.DeslizadorDeInterfaceDeTeste("corpo")!.Value = 120;
		pausa.DeslizadorDeInterfaceDeTeste("chat-l")!.Value = 420;
		pausa.DeslizadorDeInterfaceDeTeste("chat-a")!.Value = 220;
		pausa.DeslizadorDeInterfaceDeTeste("menu-l")!.Value = 620;
		pausa.DeslizadorDeInterfaceDeTeste("menu-a")!.Value = 420;
		yield return 0.4;
		Rect2 chatR = Chat.RetanguloNaTela ?? default, menuR = Janelas.MenuNaTela(a, tela);
		Afirmar("OS DESLIZADORES mexem nas pecas de verdade: status 130%, corpo 120%, chat 420 x 220, menu 620 x 420",
				Math.Abs(hud.EscalaDoStatusDeTeste - 1.3f) < 0.011f && Math.Abs(hud.EscalaDoCorpoDeTeste - 1.2f) < 0.011f
				&& Igual(chatR.Size, new Vector2(420, 220)) && Igual(menuR.Size, new Vector2(620, 420)),
				$"status {hud.EscalaDoStatusDeTeste}, corpo {hud.EscalaDoCorpoDeTeste}, chat {chatR.Size}, menu {menuR.Size}");
		Afirmar("...e cada um escreve ao lado o valor que FICOU",
				pausa.ValorDeInterfaceDeTeste("status") == "130%" && pausa.ValorDeInterfaceDeTeste("chat-l") == "420 px" && pausa.ValorDeInterfaceDeTeste("menu-a") == "420 px",
				$"{pausa.ValorDeInterfaceDeTeste("status")} | {pausa.ValorDeInterfaceDeTeste("chat-l")} | {pausa.ValorDeInterfaceDeTeste("menu-a")}");

		// o deslizador obedece a MESMA trava do arrasto: um chat pedido mais alto que o vao para no teto
		pausa.DeslizadorDeInterfaceDeTeste("chat-a")!.MaxValue = 5000;
		pausa.DeslizadorDeInterfaceDeTeste("chat-a")!.Value = 5000;
		yield return 0.3;
		chatR = Chat.RetanguloNaTela ?? default;
		Afirmar("...e obedece a mesma trava do arrasto: um chat pedido com 5000 px de altura para 8 px abaixo do painel de status",
				Hud.RetanguloDoStatus is { } st && Math.Abs(chatR.Position.Y - (st.End.Y + 8)) <= 1f, $"chat de {chatR.Position.Y} | status ate {Hud.RetanguloDoStatus?.End.Y}");

		// a tela de opcoes cobre o jogo: a foto e so da secao
		Fotografar("janelas-08a-opcoes-interface");

		// a caixa "menu (P) numa janela separada" faz o mesmo que o botao do cabecalho -- e o "Restaurar" encaixa de volta
		if (Janelas.PodeDestacar)
		{
			pausa.Fechar("bancada");
			menu.Abrir();
			yield return 0.3;
			pausa.Abrir();
			yield return 0.3;
			pausa.DestacarDeTeste.ButtonPressed = true;
			yield return 0.8;
			Afirmar("a caixa \"menu (P) numa janela separada\" das opcoes destaca o menu aberto (o mesmo que o botao do cabecalho dele)",
					menu.Destacado && menu.JanelaDeTeste is { Visible: true } && Janelas.Arranjo.MenuDestacado, $"destacado {menu.Destacado}");
		}

		Button? restaurar = BotaoComTexto(pausa, "Restaurar a interface padrão");
		Afirmar("a secao tem o botao \"Restaurar a interface padrão\"", restaurar != null);
		restaurar?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.4;
		a = Janelas.Arranjo;
		pausa.Fechar("bancada");
		menu.Abrir();
		menu.IrPara("Stats");
		yield return 0.5;
		Rect2 status = Hud.RetanguloDoStatus ?? default, corpo = Hud.RetanguloDoCorpo ?? default;
		Afirmar("\"RESTAURAR A INTERFACE PADRAO\" devolve TUDO ao de fabrica: o menu ENCAIXADO e centrado 760 x 580, o chat 540 x 280, os paineis a 100%",
				a.EDeFabrica() && Igual(menu.RetanguloDeTeste, _menuDeFabrica) && Chat.RetanguloNaTela is { } c && Igual(c, _chatDeFabrica)
				&& hud.EscalaDoStatusDeTeste == 1f && hud.EscalaDoCorpoDeTeste == 1f && Igual(status.Position, new Vector2(14, 14)) && Igual(status.Size, hud.BaseDoStatusDeTeste)
				&& Math.Abs(corpo.End.X - (tela.X - 14)) <= 0.75f && !hud.FonteAcompanhaDeTeste(true) && !menu.Destacado && menu.JanelaDeTeste == null
				&& menu.RolagemDeTeste.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled,
				$"menu {menu.RetanguloDeTeste} | chat {Chat.RetanguloNaTela} | status {hud.EscalaDoStatusDeTeste} | corpo {hud.EscalaDoCorpoDeTeste}");
		Afirmar("...e o config.json guarda o de fabrica", ArranjoDaInterface.DoDisco().EDeFabrica());
		Fotografar("janelas-08b-restaurado");
		menu.Fechar();
		yield return 0.2;
	}

	// ---------------------------------------------------------------------- 9. o arquivo
	private IEnumerable F9_OArquivo(MenuJogo menu, Chat chat, Hud hud, Vector2 tela, Rect2 telaInteira)
	{
		Nota("--- 9: o config.json -- gravado, relido, e travado ---");
		ArranjoDaInterface a = Janelas.Arranjo;

		// ---- um arranjo conhecido, pelas portas de producao (o arrasto guarda; soltar grava) ----
		// O CHAT E O PAINEL PRIMEIRO, com o menu fechado: aberto no meio da tela, ele fica por cima do canto do chat.
		Arrastar(chat.AlcaDeTeste.PontoDeTeste(Ci | D), chat.AlcaDeTeste.PontoDeTeste(Ci | D) + new Vector2(-90, 40));
		Vector2 p = hud.AlcaDoCorpoDeTeste.GetGlobalRect().GetCenter();
		Arrastar(p, p + new Vector2(-hud.BaseDoCorpoDeTeste.X, hud.BaseDoCorpoDeTeste.Y) * 0.25f);
		menu.Abrir();
		yield return 0.3;
		Vector2 pega = menu.PuxadorDeTeste.GetGlobalRect().GetCenter();
		Arrastar(pega, pega + new Vector2(-120, 30));
		yield return 0.4;
		Rect2 menuSalvo = menu.RetanguloDeTeste, chatSalvo = Chat.RetanguloNaTela ?? default;
		float corpoSalvo = hud.EscalaDoCorpoDeTeste;
		ArranjoDaInterface d = ArranjoDaInterface.DoDisco();
		Afirmar("soltar o mouse GRAVA: o config.json tem o menu arrastado, o chat encolhido e o painel do corpo aumentado",
				d.MenuMexido && d.ChatMexido && Math.Abs(d.MenuX - a.MenuX) < 1e-5f && Math.Abs(d.ChatLargura - a.ChatLargura) < 1e-5f
				&& Math.Abs(d.EscalaDoCorpo - corpoSalvo) < 0.001f && corpoSalvo > 1.1f,
				$"menu x {d.MenuX:0.####}, chat largura {d.ChatLargura:0.####}, corpo {d.EscalaDoCorpo}");

		Janelas.NaoGravaDeTeste = true;
		try
		{
			pega = menu.PuxadorDeTeste.GetGlobalRect().GetCenter();
			Arrastar(pega, pega + new Vector2(77, 0));
			yield return 0.2;
			Afirmar("(defeito injetado: o arranjo nao grava) ...com o defeito o arquivo fica com o lugar velho -- a checagem sabe reprovar",
					Math.Abs(ArranjoDaInterface.DoDisco().MenuX - a.MenuX) > 1e-4f);
			Arrastar(pega + new Vector2(77, 0), pega);
			yield return 0.2;
		}
		finally { Janelas.NaoGravaDeTeste = false; }

		// ---- "fechar e abrir o jogo": tudo de fabrica na memoria, e o arranjo relido DO DISCO pela porta de producao ----
		Boot.Config.Arranjo = new ArranjoDaInterface();
		Janelas.Avisar();
		yield return 0.3;
		Afirmar("(montagem) com o arranjo esvaziado na memoria, a tela volta ao de fabrica", Igual(menu.RetanguloDeTeste, _menuDeFabrica) && hud.EscalaDoCorpoDeTeste == 1f);
		Boot.Config.Arranjo = ArranjoDaInterface.DoDisco();
		Janelas.Avisar();
		yield return 0.3;
		Afirmar("RELIDO DO DISCO pela porta de producao, o jogo nasce no arranjo salvo: o mesmo menu, o mesmo chat, a mesma escala",
				Igual(menu.RetanguloDeTeste, menuSalvo) && Chat.RetanguloNaTela is { } c1 && Igual(c1, chatSalvo) && Math.Abs(hud.EscalaDoCorpoDeTeste - corpoSalvo) < 0.011f,
				$"menu {menu.RetanguloDeTeste} (salvo {menuSalvo}) | chat {Chat.RetanguloNaTela} (salvo {chatSalvo}) | corpo {hud.EscalaDoCorpoDeTeste}");

		// ---- TROCAR A RESOLUCAO: o arranjo e FRACAO da tela, e acompanha a base de desenho nova ----
		Window raiz = GetTree().Root;
		_baseDaTela = raiz.ContentScaleSize;
		raiz.ContentScaleSize = new Vector2I(1600, 900);
		yield return 0.4;
		Vector2 telaGrande = GetViewport().GetVisibleRect().Size, vezes = telaGrande / tela;
		var telaGrandeInteira = new Rect2(Vector2.Zero, telaGrande);
		Rect2 mg = menu.RetanguloDeTeste, cg = Chat.RetanguloNaTela ?? default;
		Afirmar("TROCANDO A RESOLUCAO (a base de desenho vai de 1280 x 720 pra 1600 x 900) o arranjo acompanha: o menu e o chat ocupam a MESMA fracao da tela nova, e nada sai dela",
				Math.Abs(vezes.X - 1.25f) < 0.01f && Igual(mg, new Rect2(menuSalvo.Position * vezes, menuSalvo.Size * vezes), 1.5f)
				&& Igual(cg.Size, chatSalvo.Size * vezes, 1.5f) && Dentro(telaGrandeInteira, mg) && Dentro(telaGrandeInteira, cg)
				&& Math.Abs(hud.EscalaDoCorpoDeTeste - corpoSalvo) < 0.011f,
				$"tela {telaGrande} ({vezes.X:0.###}x) | menu {mg} (era {menuSalvo}) | chat {cg} (era {chatSalvo})");
		Fotografar("janelas-09a-resolucao-maior");
		raiz.ContentScaleSize = _baseDaTela.Value;
		_baseDaTela = null;
		yield return 0.4;
		Afirmar("...e de volta a 1280 x 720 tudo volta pro lugar salvo",
				Igual(menu.RetanguloDeTeste, menuSalvo) && Chat.RetanguloNaTela is { } c2 && Igual(c2, chatSalvo), $"menu {menu.RetanguloDeTeste} | chat {Chat.RetanguloNaTela}");

		// ---- VALORES ABSURDOS NO ARQUIVO ----
		Settings sujo = Settings.Carregar();
		sujo.Arranjo = new ArranjoDaInterface
		{
			MenuMexido = true, MenuX = -3f, MenuY = 9f, MenuLargura = 40f, MenuAltura = 0.0001f,
			ChatMexido = true, ChatLargura = 55f, ChatAltura = 0.00001f,
			EscalaDoStatus = 9f, EscalaDoCorpo = 0.01f,
		};
		sujo.Gravar();
		Boot.Config.Arranjo = ArranjoDaInterface.DoDisco();
		Janelas.Avisar();
		yield return 0.4;
		Rect2 m = menu.RetanguloDeTeste, ch = Chat.RetanguloNaTela ?? default, st = Hud.RetanguloDoStatus ?? default;
		Vector2 teto = Janelas.TetoDoChat(tela, Hud.PeDoStatus);
		Afirmar("VALORES ABSURDOS no config.json sao TRAVADOS: o menu nasce dentro da tela e nao menor que o minimo",
				Dentro(telaInteira, m) && m.Size.X >= Janelas.MenuMinimo.X - 0.75f && m.Size.Y >= Janelas.MenuMinimo.Y - 0.75f, $"menu {m}");
		Afirmar("...o chat entre o minimo e o teto desta tela (pediram 55 telas de largura e quase nada de altura)",
				Igual(ch.Size, new Vector2(teto.X, Janelas.ChatMinimo.Y)) && Dentro(telaInteira, ch), $"chat {ch} | teto {teto}");
		Afirmar("...e os paineis na faixa de 70% a 160% (pediram 900% e 1%), com o de status sem passar da tela",
				hud.EscalaDoStatusDeTeste <= Janelas.EscalaMaxima + 0.001f && hud.EscalaDoStatusDeTeste >= Janelas.EscalaMinima
				&& Math.Abs(hud.EscalaDoCorpoDeTeste - Janelas.EscalaMinima) < 0.011f && Dentro(telaInteira, st),
				$"status {hud.EscalaDoStatusDeTeste} {st} | corpo {hud.EscalaDoCorpoDeTeste}");
		Fotografar("janelas-09-absurdo-travado");

		Janelas.SemTravaDeTeste = true;
		try
		{
			Boot.Config.Arranjo = ArranjoDaInterface.DoDisco();
			Janelas.Avisar();
			yield return 0.3;
			Rect2 solto = menu.RetanguloDeTeste;
			Afirmar("(defeito injetado: sem trava) ...com o defeito o mesmo arquivo poe o menu pra fora da tela e os paineis fora da faixa -- a checagem sabe reprovar",
					!Dentro(telaInteira, solto) && hud.EscalaDoStatusDeTeste > Janelas.EscalaMaxima + 1f, $"menu {solto} | status {hud.EscalaDoStatusDeTeste}");
		}
		finally { Janelas.SemTravaDeTeste = false; }

		// a bancada devolve a tela (e o arquivo) ao de fabrica
		Janelas.Restaurar();
		yield return 0.3;
		menu.Fechar();
		yield return 0.2;
		Afirmar("(arrumacao) a bancada devolve a tela e o config.json ao de fabrica", Janelas.Arranjo.EDeFabrica() && ArranjoDaInterface.DoDisco().EDeFabrica());
	}
}
