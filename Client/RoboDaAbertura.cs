using Godot;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ABERTURA DO JOGO (`--diagabertura`) -- a bancada do pedido do dono de 2026-10-11: o titulo cai na tela
/// como no trailer, sobe pro alto e o menu aparece; o titulo antigo sai do menu.
///
/// ELE NASCE DEPOIS DO LOBBY (`Boot.AbrirComOTitulo`), como a `--diagopcoes`: o menu que ele olha e o do jogador, e a
/// abertura que ele mede e a que o `Boot` decidiu comecar. SAO DUAS RODADAS, e quem escolhe e a linha de comando:
///
///   COM `--abertura` (e JANELA) -- a abertura roda, e ele cobra:
///     A. a CORRIDA DE VERDADE, quadro a quadro, sem tocar em nada: a ordem dos acontecimentos, os tempos da receita
///        (contra uma tabela tirada das contas do `titulo_anim.py`), um titulo so na tela do comeco ao fim, o som na
///        queda, o ensaio do `Aquecimento` esperando o titulo parar, e o PIOR QUADRO;
///     B. as FOTOS dos momentos (a abertura parada em cada instante), com o pixel medido: o branco do comeco, o
///        tamanho do logo caindo, o anel no raio da receita, as faiscas, o brilho laranja, a subida, e o titulo no
///        alto com o menu. Elas saem na pasta `--pasta` pra ficar lado a lado com os quadros do trailer;
///     C. PULAR: com uma tecla, com o ESC (que nao abre as opcoes) e com um clique em cima de um botao do menu (que
///        nao e apertado -- nem pelo segundo toque de um clique duplo); as teclas que so acompanham nao pulam;
///     D. o TAMANHO DA TELA: numa janela maior e em duas outras bases de desenho o titulo fica inteiro e centrado, na
///        queda e no alto;
///     E. VOLTAR AO MENU nao reprisa: saindo da selecao, caindo do servidor e pelo "Desconectar" de dentro do mundo.
///
///   SEM `--abertura` -- a corrida de toda bancada: ele cobra que a abertura NAO rodou e que o menu esta na tela, com
///   o titulo no alto, desde o primeiro quadro (e, com janela, fotografa).
///
/// E nas duas: a REGRA de quando ela roda (uma tabela de corridas, pela funcao pura do `Boot`), a RECEITA (a conta
/// pura, contra a tabela), o SORTEIO (as estrelas e as faiscas do Python) e a CAIXA DAS OPCOES que a desliga (um
/// clique de verdade nela, e o arquivo de opcoes lido de volta do disco).
///
/// Cada regra tem o seu defeito injetado -- a checagem tem de reprovar com ele ligado.
///
/// O QUE ELE NAO PROVA: o som de verdade (a janela de bancada roda com o audio mudo: ele ve o tocador nascer, no
/// barramento e no volume certos, e nao ouve nada) e a sensacao da animacao. Isso e roteiro de mao, no relatorio.
///
///   janela:    --abertura --diagabertura --rede 8291 --pasta &lt;fotos&gt;      (+ `--aberturasemcalma`: a rodada de injecao do ensaio;
///                                                                          + `--aberturacurta`: so a corrida de verdade)
///   sem flag:  --diagabertura --pasta &lt;fotos&gt;                             (com janela e com `--headless`)
/// </summary>
public partial class RoboDaAbertura : Node
{
	/// <summary>Ja ha um robo vivo? (A volta ao menu passa de novo pelo `Boot`, e ele nao pode nascer duas vezes.)</summary>
	public static bool Vivo { get; private set; }

	private Boot? _boot;
	private int _ok, _falhou, _naoMedidos;
	private readonly List<string> _vermelhas = [], _fotos = [];
	private IEnumerator<double>? _roteiro;
	private ulong _acordaEm;
	private bool _decidiu, _acabou;
	private string _pasta = "";

	private static bool TemTela => DisplayServer.GetName() != "headless";
	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// O GRAVADOR DA CORRIDA DE VERDADE
	// =====================================================================
	private readonly record struct Amostra(ulong Usec, double Relogio, AberturaDoJogo.Estado Estado, bool Viva, bool LogoDaAbertura,
										   int TitulosDoMenu, bool EmMovimento, ulong Pipelines, bool SomNoAr);

	private readonly List<Amostra> _amostras = [];
	private readonly List<(string Nome, double Ms, double Relogio, bool EmMovimento, ulong NoProcessoMs)> _atos = [];
	private bool _gravando;

	/// <summary>Em que ms do PROCESSO o relogio da abertura largou, e em que ms a fila do aquecimento fechou (0 = ainda nao).</summary>
	private ulong _largouAosMs, _filaFechouAosMs;

	/// <summary>O tocador do baque, como ele estava no quadro em que nasceu (o som dura 1 s: no fim da corrida ele ja saiu).</summary>
	private (string Barramento, float VolumeDb)? _tocadorVisto;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dabertura]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[dabertura]   FALHA {nome}   [{detalhe}]"); }
	}

	private void NaoMedido(string nome, string porque)
	{
		_naoMedidos++;
		GD.Print($"[dabertura]   --    NAO MEDIDO: {nome}   [{porque}]");
	}

	private static void Nota(string s) => GD.Print("[dabertura] " + s);

	private static string N(double v, string f = "0.###") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);

	public override void _EnterTree()
	{
		Vivo = true;
		if (GetParent() is Boot boot) _boot = boot;
	}

	public override void _Ready()
	{
		Aquecimento.EspiaoDeAto += AoEnsaiar;
		// ELE SE MUDA PRA RAIZ: o `Boot.VoltarAoLogin` derruba os filhos dele, e a familia E mede justamente essa volta.
		CallDeferred(nameof(Emancipar));

		_pasta = Arg("--pasta");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/') && !_pasta.EndsWith('\\')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);
	}

	public override void _ExitTree()
	{
		if (!IsQueuedForDeletion()) return;   // a mudanca pra raiz tambem passa por aqui
		Vivo = false;
		Aquecimento.EspiaoDeAto -= AoEnsaiar;
	}

	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;
		Node raiz = arv.Root;   // antes do `RemoveChild`: fora da arvore o `GetTree()` devolve nulo
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	/// <summary>Um ato do ensaio do `Aquecimento` acabou de rodar: guarda onde a abertura estava (o FATO, e nao o pedido de calma).</summary>
	private void AoEnsaiar(string nome, double ms) =>
		_atos.Add((nome, ms, AberturaDoJogo.Viva?.Relogio ?? -1, AberturaDoJogo.Viva is { EmMovimento: true }, Time.GetTicksMsec()));

	public override void _Process(double delta)
	{
		if (_acabou || _boot == null) return;

		// A PRIMEIRA PERGUNTA, no primeiro quadro: a abertura esta rodando neste processo?
		if (!_decidiu)
		{
			_decidiu = true;
			_gravando = AberturaDoJogo.Vezes > 0;
			_roteiro = (_gravando ? ComAbertura() : SemAbertura()).GetEnumerator();
		}
		if (_gravando) Gravar();
		if (_largouAosMs == 0 && AberturaDoJogo.Viva is { Relogio: > 0 }) _largouAosMs = Time.GetTicksMsec();
		if (_filaFechouAosMs == 0 && Aquecimento.Terminou) _filaFechouAosMs = Time.GetTicksMsec();

		ulong agora = Time.GetTicksUsec();
		if (agora < _acordaEm) return;
		try
		{
			if (!_roteiro!.MoveNext()) { Fim(); return; }
			_acordaEm = agora + (ulong)(Math.Max(0.0, _roteiro.Current) * 1_000_000.0);
		}
		catch (Exception e)
		{
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			Fim();
		}
	}

	private void Gravar()
	{
		AberturaDoJogo? a = AberturaDoJogo.Viva;
		AudioStreamPlayer? tocador = SomDoImpactoNoAr();
		if (tocador != null) _tocadorVisto ??= (tocador.Bus.ToString(), tocador.VolumeDb);
		_amostras.Add(new Amostra(Time.GetTicksUsec(), a?.Relogio ?? -1, a?.Agora ?? default, a != null, AberturaDoJogo.LogoNaTela,
								  TituloDoJogo.NaTela, a is { EmMovimento: true },
								  RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas), tocador != null));
	}

	private void Fim()
	{
		_acabou = true;
		_gravando = false;
		// TODO DEFEITO DESLIGADO, aconteca o que acontecer no roteiro.
		AberturaDoJogo.QuedaLentaDeTeste = AberturaDoJogo.PuloVazaDeTeste = AberturaDoJogo.SemApagarOTituloDoMenuDeTeste = false;
		AberturaDoJogo.ReprisaDeTeste = false;
		Boot.AberturaEmCorridaAutomaticaDeTeste = false;
		TituloDoJogo.PixelFixoDeTeste = false;
		if (_baseDaTela is { } b0 && GetTree()?.Root is { } raiz) raiz.ContentScaleSize = b0;
		if (PauseMenu.Instancia is { Aberto: true } p) p.Fechar("bancada");

		foreach (string f in _fotos) Nota("foto " + f);
		if (_naoMedidos > 0) Nota($"{_naoMedidos} checagem(ns) NAO MEDIDA(S) (ver as linhas \"--\")");
		if (_falhou > 0) GD.PrintErr("[dabertura] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print(_falhou == 0 ? $"\n[dabertura] ===== TUDO OK ({_ok} OK) =====" : $"\n[dabertura] ===== {_falhou} FALHA(S), {_ok} OK =====");
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

	private static Button? BotaoComTexto(Node raiz, string texto) =>
		Todos(raiz).OfType<Button>().FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	private Vector2 Tela => GetViewport().GetVisibleRect().Size;

	private void Mover(Vector2 p) =>
		GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);

	private void Botao(Vector2 p, bool desce) =>
		GetViewport().PushInput(new InputEventMouseButton
		{
			ButtonIndex = MouseButton.Left, Pressed = desce, Position = p, GlobalPosition = p,
			ButtonMask = desce ? MouseButtonMask.Left : 0,
		}, true);

	/// <summary>Um clique de verdade num ponto da tela: o mouse chega, aperta e solta -- passa por quem estiver em cima.</summary>
	private void Clicar(Vector2 p)
	{
		Mover(p);
		Botao(p, true);
		Botao(p, false);
	}

	private void Teclar(Key k)
	{
		GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
		GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
	}

	/// <summary>O tocador do baque da queda, se ele esta no ar: um filho do `AudioDirector` com o arquivo da abertura.</summary>
	private static AudioStreamPlayer? SomDoImpactoNoAr() =>
		AudioDirector.Instance?.GetChildren().OfType<AudioStreamPlayer>()
			.FirstOrDefault(p => p.Stream != null && p.Stream.ResourcePath == AberturaDoJogo.SomDoImpacto && !p.IsQueuedForDeletion());

	private static bool OpcoesAbertas => PauseMenu.Instancia is { Aberto: true };

	// =====================================================================
	// A TABELA DA RECEITA -- tirada das contas do `Trailer/bruto/t2/titulo_anim.py` (as expressoes dele, rodadas no
	// Python), e NAO da funcao que ela julga. Colunas: t, branco, ha logo?, escala, alfa do logo, tremor x e y (px
	// da receita), pulso do brilho, raio do anel, e se o anel e as faiscas estao na tela.
	//
	// O ANEL E AS FAISCAS SAO "ESTA OU NAO ESTA": a receita escreve um alfa que cai, mas desenha num quadro RGBA onde
	// o Pillow nao mistura, e no clipe do trailer os dois saem OPACOS enquanto duram (conferido no `titulo_clip.mp4`:
	// aos 0,73 s ha 5 mil px de branco puro no anel; as faiscas sao cheias aos 1,43 s e somem aos 1,47 s).
	// =====================================================================
	private static readonly (double T, float Branco, bool Logo, float Escala, float Alfa, int Dx, int Dy, float Pulso, float Raio, int Anel, int Faiscas)[] Receita =
	[
		(0.00, 1.00000f, false, 0f, 0f, 0, 0, 0f, 0f, 0, 0),
		(0.10, 0.60368f, false, 0f, 0f, 0, 0, 0f, 0f, 0, 0),
		(0.30, 0.05399f, false, 0f, 0f, 0, 0, 0f, 0f, 0, 0),
		(0.35, 0.00000f, true, 2.60000f, 0.25000f, 0, 0, 0f, 0f, 0, 0),
		(0.43, 0.00000f, true, 1.41232f, 0.61364f, 0, 0, 0f, 0f, 0, 0),
		(0.50, 0.00000f, true, 1.05154f, 0.93182f, 0, 0, 0f, 0f, 0, 0),
		(0.56, 0.00000f, true, 1.00015f, 1.00000f, 0, 0, 0f, 0f, 0, 0),
		(0.58, 0.79688f, true, 1.00004f, 1.00000f, 23, 1, 0.72672f, 173.90f, 1, 1),
		(0.60, 0.69062f, true, 1.00013f, 1.00000f, -11, -4, 0.74014f, 279.37f, 1, 1),
		(0.65, 0.42500f, true, 1.00035f, 1.00000f, 7, 12, 0.77343f, 529.47f, 1, 1),
		(0.73, 0.00000f, true, 1.00069f, 1.00000f, -5, 2, 0.82490f, 889.31f, 1, 1),
		(0.77, 0.00000f, true, 1.00086f, 1.00000f, 1, 0, 0.84930f, 1050.61f, 1, 1),
		(0.90, 0.00000f, true, 1.00142f, 1.00000f, 0, 0, 0.91929f, 1489.16f, 1, 1),
		(1.07, 0.00000f, true, 1.00216f, 1.00000f, 0, 0, 0.98097f, 1864.90f, 1, 1),
		(1.26, 0.00000f, true, 1.00298f, 1.00000f, 0, 0, 0.99898f, 2019.61f, 1, 1),
		(1.30, 0.00000f, true, 1.00315f, 1.00000f, 0, 0, 0.99542f, 0f, 0, 1),
		(1.50, 0.00000f, true, 1.00401f, 1.00000f, 0, 0, 0.94099f, 0f, 0, 0),
		(2.00, 0.00000f, true, 1.00617f, 1.00000f, 0, 0, 0.63982f, 0f, 0, 0),
	];

	/// <summary>O que o Python da maquina sorteia com `random.seed(7)`: as 4 primeiras estrelas e a primeira faisca.</summary>
	private const string SorteioDoPython = "(663,308,200) (1333,98,60) (1681,192,130) (1193,118,90) | 2.60051 1024.168 4.88433";

	/// <summary>Em quantas linhas da tabela a conta do jogo discorda da receita (e a primeira delas).</summary>
	private static (int Erradas, string Primeira) ContraAReceita()
	{
		int erradas = 0;
		string primeira = "";
		foreach (var r in Receita)
		{
			AberturaDoJogo.Estado e = AberturaDoJogo.Em(r.T);
			bool bate = Math.Abs(e.Clarao - r.Branco) <= 0.002f && e.TemLogo == r.Logo
				&& (!r.Logo || (Math.Abs(e.Escala - r.Escala) <= 0.0005f && Math.Abs(e.AlfaDoLogo - r.Alfa) <= 0.0005f
								&& Math.Abs(e.Tremor.X - r.Dx) <= 0.5f && Math.Abs(e.Tremor.Y - r.Dy) <= 0.5f
								&& Math.Abs(e.Brilho - r.Pulso) <= 0.0005f && Math.Abs(e.RaioDoAnel - r.Raio) <= 0.05f
								&& e.AlfaDoAnel == r.Anel && e.AlfaDasFaiscas == r.Faiscas))
				&& e.NaSubida == 0f && e.Fundo == 1f;
			if (bate) continue;
			if (erradas++ == 0)
				primeira = $"t={N(r.T, "0.00")}: jogo (branco {N(e.Clarao)}, logo {e.TemLogo}, escala {N(e.Escala, "0.0000")}, alfa {N(e.AlfaDoLogo)}, tremor {e.Tremor}, anel {N(e.RaioDoAnel, "0.0")}/{N(e.AlfaDoAnel)})"
						 + $" x receita (branco {N(r.Branco)}, logo {r.Logo}, escala {N(r.Escala, "0.0000")}, alfa {N(r.Alfa)}, tremor ({r.Dx}, {r.Dy}), anel {N(r.Raio, "0.0")}/{N(r.Anel)})";
		}
		return (erradas, primeira);
	}

	// =====================================================================
	// O QUE VALE NAS DUAS RODADAS: a receita, o sorteio e a regra de quando ela roda
	// =====================================================================
	private void ARegraEAReceita()
	{
		Nota("--- a receita (a conta pura contra a tabela do titulo_anim.py) ---");
		(int erradas, string primeira) = ContraAReceita();
		Afirmar($"RECEITA: nas {Receita.Length} linhas da tabela (de 0 a 2 s) a abertura mostra o que o `quadro(t)` do trailer mostra", erradas == 0, primeira);

		float cheias = AberturaDoJogo.Em(AberturaDoJogo.Impacto + 0.78).AlfaDasFaiscas, meio = AberturaDoJogo.Em(AberturaDoJogo.Impacto + 0.84).AlfaDasFaiscas,
			  apagadas = AberturaDoJogo.Em(AberturaDoJogo.Impacto + 0.9).AlfaDasFaiscas;
		Afirmar("RECEITA (a unica divergencia do clipe, declarada): as faiscas desmaiam nos ultimos 0,12 s em vez de sumirem de uma vez aos 0,9 s",
				Math.Abs(cheias - 1f) < 0.001f && Math.Abs(meio - 0.5f) < 0.01f && apagadas < 0.001f, $"aos 0,78 s {N(cheias)}, aos 0,84 s {N(meio)}, aos 0,90 s {N(apagadas)}");

		AberturaDoJogo.QuedaLentaDeTeste = true;
		try
		{
			(int comDefeito, string qual) = ContraAReceita();
			Afirmar("(defeito injetado: a queda dura o dobro) a tabela reprova", comDefeito > 0, $"{comDefeito} linha(s) erradas; a primeira: {qual}");
		}
		finally { AberturaDoJogo.QuedaLentaDeTeste = false; }

		Afirmar("RECEITA: o impacto cai aos 0,57 s, a subida comeca aos 2,12 s e o titulo pousa aos 2,87 s (menos de 3 s ate o menu)",
				Math.Abs(AberturaDoJogo.Impacto - 0.57) < 1e-9 && Math.Abs(AberturaDoJogo.ComecoDaSubida - 2.12) < 1e-9
				&& Math.Abs(AberturaDoJogo.Fim - 2.87) < 1e-9 && AberturaDoJogo.Fim < 3.0,
				$"impacto {N(AberturaDoJogo.Impacto)}, subida {N(AberturaDoJogo.ComecoDaSubida)}, fim {N(AberturaDoJogo.Fim)}");

		AberturaDoJogo.Estado naMetade = AberturaDoJogo.Em(AberturaDoJogo.ComecoDaSubida + AberturaDoJogo.Subida / 2), fim = AberturaDoJogo.Em(AberturaDoJogo.Fim);
		Afirmar("SUBIDA: no meio dela o titulo esta a meio caminho e o fundo ja se desfez mais da metade; no fim, pousado e sem fundo",
				Math.Abs(naMetade.NaSubida - 0.5f) < 0.001f && naMetade.Fundo < 0.5f && naMetade.Fundo > 0f && fim.NaSubida == 1f && fim.Fundo == 0f && fim.Acabou,
				$"meio: subida {N(naMetade.NaSubida)}, fundo {N(naMetade.Fundo)}; fim: subida {N(fim.NaSubida)}, fundo {N(fim.Fundo)}");

		string sorteio = AberturaDoJogo.SorteioDeTeste();
		Afirmar("SORTEIO: as estrelas e as faiscas sao as do trailer (o `random.seed(7)` do Python, numero a numero)", sorteio == SorteioDoPython, sorteio);

		Nota("--- a regra de quando ela roda (a funcao pura do Boot, com corridas que nao existem) ---");
		string[] nada = [];
		(string Corrida, string[] Args, bool Janela, string? Robo, bool Opcao, bool Roda)[] casos =
		[
			("o jogo aberto por gente, sem argumento nenhum", nada, true, null, true, true),
			("gente, com a porta trocada (--rede) e a resolucao do motor", ["--rede", "7777", "--resolution", "1280x720"], true, null, true, true),
			("sem janela (--headless)", nada, false, null, true, false),
			("a janela de bancada (--semfoco)", ["--semfoco"], true, null, true, false),
			("login automatico de quem hospeda (--host)", ["--host", "--conta", "x"], true, null, true, false),
			("login automatico de quem disca (--connect)", ["--connect", "127.0.0.1"], true, null, true, false),
			("o diretor do trailer (--trailer com --host)", ["--host", "--trailer", "ki"], true, null, true, false),
			("um robo nascido no lobby, sem mais nada", ["--diagopcoes"], true, "RoboDeOpcoes", true, false),
			("desligada na linha de comando (--semabertura)", ["--semabertura"], true, null, true, false),
			("desligada nas opcoes", nada, true, null, false, false),
			("FORCADA (--abertura) na janela de bancada, com robo e com login automatico", ["--abertura", "--semfoco", "--host"], true, "RoboDaAbertura", true, true),
			("FORCADA com a caixa das opcoes desligada e com --semabertura", ["--abertura", "--semabertura"], true, null, false, true),
			("FORCADA sem janela", ["--abertura"], false, null, true, false),
		];
		var erros = new List<string>();
		foreach (var c in casos)
			if ((Boot.PorQueNaoAbre(c.Args, c.Janela, c.Robo, c.Opcao) == null) != c.Roda) erros.Add(c.Corrida);
		Afirmar($"REGRA: nas {casos.Length} corridas da tabela a abertura so roda com gente na frente da tela (ou forcada, com janela)", erros.Count == 0, string.Join("; ", erros));

		Boot.AberturaEmCorridaAutomaticaDeTeste = true;
		try
		{
			int rodariam = casos.Count(c => !c.Roda && Boot.PorQueNaoAbre(c.Args, c.Janela, c.Robo, c.Opcao) == null);
			Afirmar("(defeito injetado: os fatos de corrida automatica nao sao perguntados) a tabela reprova: a abertura rodaria em bancada",
					rodariam >= 5, $"{rodariam} corrida(s) automatica(s) com abertura");
		}
		finally { Boot.AberturaEmCorridaAutomaticaDeTeste = false; }
	}

	// =====================================================================
	// A RODADA SEM ABERTURA -- a de toda bancada
	// =====================================================================
	private IEnumerable<double> SemAbertura()
	{
		Boot boot = _boot!;
		Nota("=========== SEM ABERTURA: a corrida de toda bancada ===========");
		Nota($"  tela: {DisplayServer.GetName()} | motivo: \"{Boot.MotivoDeNaoAbrir}\" | fotos em {_pasta}");

		// NO PRIMEIRO QUADRO do robo -- antes de qualquer espera.
		Afirmar("a abertura NAO rodou nesta corrida, e o `Boot` diz por que", AberturaDoJogo.Vezes == 0 && AberturaDoJogo.Viva == null && Boot.MotivoDeNaoAbrir.Length > 0,
				$"vezes {AberturaDoJogo.Vezes}, motivo \"{Boot.MotivoDeNaoAbrir}\"");
		Afirmar("o MENU PRINCIPAL esta na tela desde o primeiro quadro, com o titulo aceso no alto (e so um)",
				boot.MenuPrincipalNaTela && boot.TituloDoMenuDeTeste is { Apagado: false } && TituloDoJogo.NaTela == 1,
				$"menu {boot.MenuPrincipalNaTela}, titulos {TituloDoJogo.NaTela}");
		Afirmar("o aquecimento nao foi segurado por ninguem (a abertura nao pede calma)", !AberturaDoJogo.PedeCalma);
		Afirmar("o titulo ANTIGO saiu do menu: nenhum texto \"DRAGON BALL\" nem \"J A N D I R U S\" na tela de login",
				!Todos(boot).OfType<Label>().Any(l => l.Text.Contains("DRAGON BALL", StringComparison.OrdinalIgnoreCase) || l.Text.Contains("J A N D I R U S", StringComparison.Ordinal)));

		yield return 0.6;
		Afirmar("meio segundo depois nada mudou: sem abertura, sem escudo, um titulo", AberturaDoJogo.Viva == null && boot.GetNodeOrNull("Abertura") == null && TituloDoJogo.NaTela == 1);
		foreach (double d in OMenuEstaNaMao(boot, "sem abertura, o menu e do jogador desde o comeco")) yield return d;

		foreach (double d in OTituloNoAltoComOMenu(boot, "abertura-sem-flag-o-menu-como-antes")) yield return d;
		foreach (double d in OTamanhoDoTitulo(boot, comAbertura: false)) yield return d;
		foreach (double d in ACaixaDasOpcoes(boot)) yield return d;
		ARegraEAReceita();
	}

	/// <summary>O menu responde: um clique de verdade no botao "Opções" abre as opcoes. (Fecha em seguida.)</summary>
	private IEnumerable<double> OMenuEstaNaMao(Boot boot, string quando)
	{
		if (BotaoComTexto(boot, "Opções") is not { } opcoes) { Afirmar($"{quando}: achei o botao \"Opções\"", false); yield break; }
		Clicar(opcoes.GetGlobalRect().GetCenter());
		yield return 0.15;
		Afirmar($"{quando}: um clique em \"Opções\" abre as opcoes", OpcoesAbertas);
		PauseMenu.Instancia?.Fechar("bancada");
		yield return 0.1;
	}

	/// <summary>O rotulo da caixa das opcoes que liga e desliga a abertura. Procurada pelo TEXTO, como o dono procura.</summary>
	private const string CaixaDaAbertura = "abertura animada ao abrir o jogo";

	/// <summary>
	/// A CAIXA DAS OPCOES. Ela e do ARRANQUE: desmarcar nao muda nada na hora, e o que vale na proxima vez que o jogo
	/// abrir e o que ficou GRAVADO. Entao a prova nao pergunta a caixa: clica nela de verdade, le o arquivo de opcoes
	/// de volta do disco e entrega o que leu a regra do `Boot` (a corrida de uma pessoa, sem argumento nenhum).
	/// </summary>
	private IEnumerable<double> ACaixaDasOpcoes(Boot boot)
	{
		Nota("--- a caixa das opcoes ---");
		if (BotaoComTexto(boot, "Opções") is not { } opcoes) { Afirmar("OPCOES: achei o botao \"Opções\" do menu", false); yield break; }
		Clicar(opcoes.GetGlobalRect().GetCenter());
		yield return 0.2;

		CheckBox? caixa = PauseMenu.Instancia is { Aberto: true } menu
			? Todos(menu).OfType<CheckBox>().FirstOrDefault(c => c.Text == CaixaDaAbertura && c.IsVisibleInTree()) : null;
		Afirmar($"OPCOES: a caixa \"{CaixaDaAbertura}\" esta nas opcoes, e vem MARCADA de fabrica (no arquivo tambem)",
				caixa is { ButtonPressed: true } && Settings.Carregar().AberturaAnimada, caixa == null ? "nao achei a caixa" : $"marcada {caixa.ButtonPressed}");
		if (caixa == null) { PauseMenu.Instancia?.Fechar("bancada"); yield break; }

		string[] deGente = [];
		string? NaProximaVez() => Boot.PorQueNaoAbre(deGente, true, null, Settings.Carregar().AberturaAnimada);
		try
		{
			// (o contra-exemplo da regua: a caixa DESENHADA desmarcada, sem o clique -- nada foi gravado, e a regua,
			// que le o arquivo, tem de continuar dizendo "ligada")
			caixa.SetPressedNoSignal(false);
			Afirmar("(defeito injetado: a caixa desmarcada so no desenho, sem gravar) a regua le o ARQUIVO e nao a caixa: pra ela a abertura continua ligada",
					!caixa.ButtonPressed && NaProximaVez() == null);
			caixa.SetPressedNoSignal(true);

			Clicar(caixa.GetGlobalRect().GetCenter());
			yield return 0.2;
			string? porque = NaProximaVez();
			Afirmar("OPCOES: um clique na caixa a DESMARCA e grava -- e com o arquivo que ficou a abertura nao roda na proxima vez que o jogo abrir",
					!caixa.ButtonPressed && porque == "desligada nas opcoes", $"marcada {caixa.ButtonPressed}; a regra: \"{porque ?? "roda"}\"");

			Clicar(caixa.GetGlobalRect().GetCenter());
			yield return 0.2;
			porque = NaProximaVez();
			Afirmar("OPCOES: outro clique MARCA de volta e grava -- a abertura volta a rodar", caixa.ButtonPressed && porque == null,
					$"marcada {caixa.ButtonPressed}; a regra: \"{porque ?? "roda"}\"");
		}
		finally
		{
			// SEM RESIDUO: acabe como acabar, a caixa e o arquivo ficam ligados (o clique grava pela propria caixa).
			if (!caixa.ButtonPressed) caixa.ButtonPressed = true;
		}
		PauseMenu.Instancia?.Fechar("bancada");
		TirarOMouse();
		yield return 0.1;
	}

	// =====================================================================
	// A RODADA COM ABERTURA
	// =====================================================================
	private IEnumerable<double> ComAbertura()
	{
		Boot boot = _boot!;
		Nota("=========== COM ABERTURA ===========");
		Nota($"  tela: {DisplayServer.GetName()} {DisplayServer.WindowGetSize()} | base {Tela} | fotos em {_pasta}"
			 + (AberturaDoJogo.SemPedirCalmaDeTeste ? " | RODADA DE INJECAO: sem pedir calma ao aquecimento" : ""));

		// ---------------------------------------------------------------- A. a corrida de verdade
		Afirmar("A. a abertura COMECOU pela porta do jogo, uma vez, no primeiro quadro", AberturaDoJogo.Vezes == 1 && AberturaDoJogo.Viva != null && Boot.MotivoDeNaoAbrir.Length == 0,
				$"vezes {AberturaDoJogo.Vezes}, motivo \"{Boot.MotivoDeNaoAbrir}\"");
		Afirmar("A. o MENU JA ESTA MONTADO por baixo dela (ele paga o primeiro desenho no primeiro quadro, e nao na subida)", boot.MenuPrincipalNaTela);
		Afirmar("A. o titulo do menu esta APAGADO enquanto ela corre (nenhum titulo de menu desenhado)", boot.TituloDoMenuDeTeste is { Apagado: true } && TituloDoJogo.NaTela == 0);

		// (ninguem toca em nada: ela corre sozinha ate pousar, e o gravador anota cada quadro)
		ulong prazo = Time.GetTicksUsec() + 12_000_000;
		while (AberturaDoJogo.Viva != null && Time.GetTicksUsec() < prazo) yield return 0;
		yield return 0;
		yield return 0;
		_gravando = false;

		// A RODADA DE INJECAO DO ENSAIO (`--aberturasemcalma`) so julga o ensaio e o pior quadro: com os atos caindo em
		// cima do titulo os tempos de parede sao outros, e o resto ja e da rodada boa.
		if (!AberturaDoJogo.SemPedirCalmaDeTeste) JulgarACorrida(boot);
		JulgarOEnsaioEOPiorQuadro();
		if (AberturaDoJogo.SemPedirCalmaDeTeste) yield break;
		foreach (double d in OMenuEstaNaMao(boot, "A. no quadro seguinte ao pouso o menu e do jogador")) yield return d;

		// `--aberturacurta`: so a corrida de verdade. E a rodada do cache de shader QUENTE (`dabertura_quente`), que so
		// quer os tempos -- as fotos, os pulos e as voltas ao menu ja sao da rodada inteira.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--aberturacurta") >= 0)
		{
			// (o ensaio que a abertura segurou termina sozinho: espera pra a linha do `[aquece]` sair no log)
			for (int i = 0; i < 100 && !Aquecimento.Ensaiou; i++) yield return 0.1;
			yield break;
		}

		// ---------------------------------------------------------------- B. as fotos
		foreach (double d in AsFotos(boot)) yield return d;
		// ---------------------------------------------------------------- C. pular
		foreach (double d in Pular(boot)) yield return d;
		// ---------------------------------------------------------------- D. o tamanho da tela
		foreach (double d in OTamanhoDoTitulo(boot, comAbertura: true)) yield return d;
		foreach (double d in ACaixaDasOpcoes(boot)) yield return d;
		ARegraEAReceita();
		// ---------------------------------------------------------------- E. voltar ao menu
		foreach (double d in VoltarAoMenu(boot)) yield return d;
	}

	/// <summary>A familia A: o que o gravador viu na corrida de verdade -- a ordem, os tempos, um titulo so, o som.</summary>
	private void JulgarACorrida(Boot boot)
	{
		List<Amostra> vivas = _amostras.Where(a => a.Viva).ToList();
		AberturaDoJogo.Corrida? ultima = AberturaDoJogo.UltimaCorrida;
		if (vivas.Count < 20 || ultima is not { } corrida)
		{
			Afirmar("A. o gravador pegou a corrida inteira", false, $"{vivas.Count} quadro(s) com a abertura viva");
			return;
		}

		// ---- a ordem: cada acontecimento, no relogio da animacao em que foi visto pela primeira vez ----
		double Primeiro(Func<Amostra, bool> quando) => vivas.FirstOrDefault(quando) is { Viva: true } a ? a.Relogio : -1;
		double Ultimo(Func<Amostra, bool> quando) => vivas.LastOrDefault(quando) is { Viva: true } a ? a.Relogio : -1;
		double passo = AberturaDoJogo.PassoMaximo + 0.001;

		double logo = Primeiro(a => a.Estado.TemLogo), assentou = Primeiro(a => a.Estado.TemLogo && a.Estado.AlfaDoAnel > 0),
			   anelAcabou = Ultimo(a => a.Estado.AlfaDoAnel > 0), faiscasAcabaram = Ultimo(a => a.Estado.AlfaDasFaiscas > 0),
			   subiu = Primeiro(a => a.Estado.NaSubida > 0), fundoSumiu = Primeiro(a => a.Estado.Fundo <= 0f);
		Afirmar("A. ORDEM: a tela nasce BRANCA (e fica branca ate o relogio largar)", vivas[0].Estado.Clarao >= 0.999f && !vivas[0].Estado.TemLogo && vivas[0].Relogio == 0,
				$"primeiro quadro: branco {N(vivas[0].Estado.Clarao)}, relogio {N(vivas[0].Relogio)}");
		Afirmar("A. ORDEM E TEMPOS: o logo aparece aos 0,35 s, assenta aos 0,57 s, o anel dura ate 1,27 s, as faiscas ate 1,47 s, a subida comeca aos 2,12 s",
				logo >= 0.35 && logo < 0.35 + passo && assentou >= 0.57 && assentou < 0.57 + passo
				&& anelAcabou > 1.27 - passo && anelAcabou < 1.27 && faiscasAcabaram > 1.47 - passo && faiscasAcabaram < 1.47
				&& subiu > 2.12 && subiu < 2.12 + 2 * passo && fundoSumiu > subiu && fundoSumiu < AberturaDoJogo.Fim,
				$"logo {N(logo)}, assentou {N(assentou)}, anel ate {N(anelAcabou)}, faiscas ate {N(faiscasAcabaram)}, subida {N(subiu)}, fundo some {N(fundoSumiu)}");

		bool relogioSoAnda = true;
		for (int i = 1; i < vivas.Count; i++)
			if (vivas[i].Relogio < vivas[i - 1].Relogio || vivas[i].Relogio - vivas[i - 1].Relogio > passo) relogioSoAnda = false;
		Afirmar("A. o relogio da animacao so anda pra frente, e nunca mais que 0,05 s num quadro (um engasgo congela, nao pula)", relogioSoAnda);

		// ---- cada quadro mostrou o estado do proprio relogio ----
		int foraDaConta = vivas.Count(a => a.Estado != AberturaDoJogo.Em(a.Relogio));
		Afirmar("A. em TODO quadro a tela mostrou a conta do relogio daquele quadro", foraDaConta == 0, $"{foraDaConta} de {vivas.Count} quadro(s) fora");

		// ---- o tempo ----
		Afirmar("A. TEMPO: a abertura pousou aos 2,87 s do relogio dela, sem ninguem pular", !corrida.Pulada && corrida.Motivo == "pousou"
				&& corrida.NoRelogio >= AberturaDoJogo.Fim && corrida.NoRelogio < AberturaDoJogo.Fim + passo, $"{N(corrida.NoRelogio)} s, \"{corrida.Motivo}\"");
		Afirmar("A. TEMPO: do primeiro quadro que andou ate o menu na mao, menos de 3,5 s de relogio de parede (2,87 s mais os engasgos)",
				corrida.Segundos >= AberturaDoJogo.Fim - 0.02 && corrida.Segundos < 3.5, $"{N(corrida.Segundos, "0.00")} s em {corrida.Quadros} quadros");

		// ---- um titulo so ----
		int dois = vivas.Count(a => (a.LogoDaAbertura ? 1 : 0) + a.TitulosDoMenu > 1);
		int nenhum = vivas.Count(a => a.Estado.TemLogo && !a.LogoDaAbertura);
		Amostra depois = _amostras[^1];
		Afirmar("A. UM TITULO SO: enquanto ela corre, o da abertura e mais nenhum; no quadro em que ela pousa, o do menu e mais nenhum",
				dois == 0 && nenhum == 0 && !depois.Viva && depois.TitulosDoMenu == 1 && !depois.LogoDaAbertura
				&& boot.TituloDoMenuDeTeste is { Apagado: false } && boot.GetNodeOrNull("Abertura") == null,
				$"{dois} quadro(s) com dois, {nenhum} sem nenhum; depois: {depois.TitulosDoMenu} no menu");

		// ---- o som ----
		Amostra? primeiraComSom = _amostras.Any(a => a.SomNoAr) ? _amostras.First(a => a.SomNoAr) : null;
		Afirmar("A. SOM: o baque da queda toca no quadro em que o logo assenta (nem antes, nem depois), pelo barramento de efeitos",
				primeiraComSom is { } s && s.Relogio >= AberturaDoJogo.Impacto && s.Relogio < AberturaDoJogo.Impacto + passo
				&& !_amostras.Any(a => a.SomNoAr && a.Viva && a.Relogio < AberturaDoJogo.Impacto),
				primeiraComSom is { } s2 ? $"aos {N(s2.Relogio)} s" : "nunca tocou");
		Afirmar("A. SOM: o tocador nasce no barramento \"Efeitos\" (o volume das opcoes manda nele), a 80%",
				_tocadorVisto is { } tv && tv.Barramento == AudioDirector.BusEfeitos && Math.Abs(tv.VolumeDb - Mathf.LinearToDb(AberturaDoJogo.VolumeDoImpacto)) < 0.01f,
				_tocadorVisto is { } tv2 ? $"barramento {tv2.Barramento}, {N(tv2.VolumeDb, "0.0")} dB" : "o tocador nao foi visto");
	}

	/// <summary>
	/// A familia A, a metade do ARRANQUE: o ensaio do `Aquecimento` contra o titulo em movimento, o pior quadro e as
	/// pipelines. Na rodada `--aberturasemcalma` a mesma regua tem de REPROVAR.
	/// </summary>
	private void JulgarOEnsaioEOPiorQuadro()
	{
		List<Amostra> vivas = _amostras.Where(a => a.Viva).ToList();
		if (vivas.Count < 20 || AberturaDoJogo.UltimaCorrida is not { } corrida)
		{
			Afirmar("A. o gravador pegou a corrida inteira (o ensaio e o pior quadro)", false, $"{vivas.Count} quadro(s) com a abertura viva");
			return;
		}

		var emMovimento = _atos.Where(a => a.EmMovimento).ToList();
		Nota($"  ensaio do aquecimento: {_atos.Count} ato(s) enquanto o robo olhava; {_atos.Count(a => a.Relogio >= 0)} com a abertura viva, {emMovimento.Count} com o titulo em movimento"
			 + (_atos.Count > 0 ? $" (o mais caro: {_atos.MaxBy(a => a.Ms).Nome}, {N(_atos.Max(a => a.Ms), "0.0")} ms)" : ""));
		foreach (var ato in _atos.Where(a => a.Relogio >= 0))
			Nota($"    ato \"{ato.Nome}\" {N(ato.Ms, "0.0")} ms aos {N(ato.Relogio, "0.00")} s da abertura ({ato.NoProcessoMs} ms do processo){(ato.EmMovimento ? "  <-- COM O TITULO EM MOVIMENTO" : "")}");
		Nota($"  linha do tempo do processo: o relogio da abertura largou aos {_largouAosMs} ms; a fila do aquecimento ({Aquecimento.Prontos} de {Aquecimento.Total} recursos, numa thread) fechou aos "
			 + (_filaFechouAosMs > 0 ? $"{_filaFechouAosMs} ms ({N(Aquecimento.Ms, "0")} ms de fila)" : "(ainda aberta)")
			 + (_atos.Count > 0 ? $"; o ensaio comecou aos {_atos[0].NoProcessoMs} ms" : ""));

		// os cinco quadros mais compridos, pelo relogio do robo (uma segunda testemunha do numero que a abertura imprime)
		var quadros = new List<(double Ms, double Relogio, bool EmMovimento)>();
		for (int i = 1; i < _amostras.Count; i++)
			if (_amostras[i - 1].Viva && _amostras[i - 1].Relogio > 0)
				quadros.Add(((_amostras[i].Usec - _amostras[i - 1].Usec) / 1000.0, _amostras[i - 1].Relogio, _amostras[i - 1].EmMovimento));
		string piores = string.Join(", ", quadros.OrderByDescending(q => q.Ms).Take(5).Select(q => $"{N(q.Ms, "0.0")} ms aos {N(q.Relogio, "0.00")} s{(q.EmMovimento ? "" : " (respirando)")}"));
		double mediana = quadros.Count > 0 ? quadros.Select(q => q.Ms).OrderBy(m => m).ElementAt(quadros.Count / 2) : 0;
		string pioresEmMovimento = string.Join(", ", quadros.Where(q => q.EmMovimento).OrderByDescending(q => q.Ms).Take(5).Select(q => $"{N(q.Ms, "0.0")} ms aos {N(q.Relogio, "0.00")} s"));
		Nota($"  PIOR QUADRO: {N(corrida.PiorQuadroMs, "0.0")} ms aos {N(corrida.PiorQuadroEm, "0.00")} s; com o titulo em movimento, {N(corrida.PiorEmMovimentoMs, "0.0")} ms"
			 + $" | mediana {N(mediana, "0.0")} ms em {quadros.Count} quadros | os cinco mais compridos: {piores} | os cinco mais compridos EM MOVIMENTO: {pioresEmMovimento}");

		// AS PIPELINES 2D QUE NASCERAM com a abertura na tela, depois do 3o quadro (os primeiros sao o arranque de todo
		// processo): quantas, e em que instante do relogio. Em movimento nao pode nascer nenhuma.
		ulong pipelinesAntes = vivas[Math.Min(3, vivas.Count - 1)].Pipelines, pipelinesDepois = vivas[^1].Pipelines;
		ulong emMov = 0;
		var nascidas = new List<string>();
		for (int i = 4; i < vivas.Count; i++)
		{
			ulong n = vivas[i].Pipelines - vivas[i - 1].Pipelines;
			if (n == 0) continue;
			if (vivas[i - 1].EmMovimento) emMov += n;
			nascidas.Add($"+{n} aos {N(vivas[i - 1].Relogio, "0.00")} s{(vivas[i - 1].EmMovimento ? " (EM MOVIMENTO)" : "")}");
		}
		Nota($"  pipelines 2D montadas depois do 3o quadro: {pipelinesDepois - pipelinesAntes} ao todo, {emMov} com o titulo em movimento"
			 + (nascidas.Count > 0 ? $" -- {string.Join(", ", nascidas)}" : ""));

		string contaDoEnsaio = $"{emMovimento.Count} ato(s) em movimento; pior quadro em movimento {N(corrida.PiorEmMovimentoMs, "0.0")} ms";
		if (AberturaDoJogo.SemPedirCalmaDeTeste)
		{
			Afirmar("(defeito injetado: a abertura nao pede calma) o ensaio CAI em cima do titulo em movimento -- a regua dos atos sabe reprovar",
					emMovimento.Count > 0, contaDoEnsaio);
			Afirmar($"(defeito injetado: a abertura nao pede calma) o pior quadro com o titulo em movimento PASSA de {N(TetoDoQuadroEmMovimentoMs)} ms -- a regua do quadro sabe reprovar",
					corrida.PiorEmMovimentoMs > TetoDoQuadroEmMovimentoMs, contaDoEnsaio);
			return;
		}
		Afirmar("A. ENSAIO: nenhum ato do aquecimento rodou com o titulo em movimento (ele espera a janela em que o logo so respira)", emMovimento.Count == 0, contaDoEnsaio);
		Afirmar($"A. PIOR QUADRO com o titulo em movimento: no maximo {N(TetoDoQuadroEmMovimentoMs)} ms", corrida.PiorEmMovimentoMs <= TetoDoQuadroEmMovimentoMs, contaDoEnsaio);
		Afirmar("A. o titulo em movimento nao monta pipeline nova (tudo e desenhado com o que o lobby ja desenha)", emMov == 0, $"{emMov} pipeline(s)");
	}

	/// <summary>
	/// O TETO DO QUADRO COM O TITULO EM MOVIMENTO, em ms. MEDIDO (2026-10-11): o pior deles tem 10,6 a 16,9 ms com a
	/// abertura pedindo calma (o quadro do impacto, que cria o tocador do som), e 408 ms sem ela -- o ato da poeira do
	/// ensaio, com outros quatro quadros de 50 a 65 ms (a rodada `--aberturasemcalma`). O teto fica no meio, com folga
	/// pra maquina ocupada: ha outras sessoes compilando enquanto a bancada roda.
	/// </summary>
	private const double TetoDoQuadroEmMovimentoMs = 50.0;

	// =====================================================================
	// B. AS FOTOS
	// =====================================================================
	private Image? Fotografar(string nome)
	{
		if (!TemTela) return null;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto pra {nome}"); return null; }
		string caminho = _pasta + nome + ".png";
		img.SavePng(caminho);
		_fotos.Add(caminho);
		return img;
	}

	private static float Luz(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

	/// <summary>
	/// TINTA DE LOGO: o amarelo, o vermelho e o laranja das letras -- quente, saturado e FORTE. O contorno preto nao e,
	/// e o brilho de tras tambem nao (o vermelho dele nao passa de 0,5): por isso o piso de 0,6 no logo de corpo
	/// inteiro. Na queda e atraves do fundo que se desfaz o logo tem 6 decimos do corpo, e o piso desce junto.
	/// </summary>
	private static bool EhTinta(Color c, float piso = 0.6f) => c.R >= piso && c.R - c.B >= 0.25f;

	private static bool EhTintaFraca(Color c) => EhTinta(c, 0.45f);

	/// <summary>BRILHO: laranja escuro (vermelho > verde > azul), mais fraco que a tinta e mais forte que o fundo.</summary>
	private static bool EhBrilho(Color c) => c.R >= 0.14f && c.R < 0.6f && c.R >= 1.3f * c.G && c.G >= c.B;

	/// <summary>TINTA DE FAISCA: amarelo claro (o verde perto do vermelho). O brilho de tras e laranja, e nao passa.</summary>
	private static bool EhFaisca(Color c) => c.R >= 0.28f && c.G >= 0.78f * c.R && c.R - c.B >= 0.10f;

	/// <summary>A caixa (e a contagem) dos pixels de uma foto que passam num teste, dentro de um retangulo de pixels.</summary>
	private static (Rect2 Caixa, int Quantos) Medir(Image foto, Rect2 onde, Func<Color, bool> teste)
	{
		int x0 = Math.Max(0, (int)MathF.Floor(onde.Position.X)), y0 = Math.Max(0, (int)MathF.Floor(onde.Position.Y));
		int x1 = Math.Min(foto.GetWidth(), (int)MathF.Ceiling(onde.End.X)), y1 = Math.Min(foto.GetHeight(), (int)MathF.Ceiling(onde.End.Y));
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, n = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				if (!teste(foto.GetPixel(x, y))) continue;
				n++;
				if (x < minX) minX = x;
				if (x > maxX) maxX = x;
				if (y < minY) minY = y;
				if (y > maxY) maxY = y;
			}
		return (n == 0 ? default : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1), n);
	}

	private static float LuzMedia(Image foto, Rect2 onde)
	{
		int x0 = Math.Max(0, (int)onde.Position.X), y0 = Math.Max(0, (int)onde.Position.Y);
		int x1 = Math.Min(foto.GetWidth(), (int)onde.End.X), y1 = Math.Min(foto.GetHeight(), (int)onde.End.Y);
		double soma = 0;
		int n = 0;
		for (int y = y0; y < y1; y += 2)
			for (int x = x0; x < x1; x += 2) { soma += Luz(foto.GetPixel(x, y)); n++; }
		return n == 0 ? 0f : (float)(soma / n);
	}

	private static Color CorMedia(Image foto, IEnumerable<Rect2> onde)
	{
		double r = 0, g = 0, b = 0;
		int n = 0;
		foreach (Rect2 q in onde)
			for (int y = Math.Max(0, (int)q.Position.Y); y < Math.Min(foto.GetHeight(), (int)q.End.Y); y++)
				for (int x = Math.Max(0, (int)q.Position.X); x < Math.Min(foto.GetWidth(), (int)q.End.X); x++)
				{
					Color c = foto.GetPixel(x, y);
					r += c.R; g += c.G; b += c.B; n++;
				}
		return n == 0 ? Colors.Black : new Color((float)(r / n), (float)(g / n), (float)(b / n));
	}

	/// <summary>
	/// ONDE ESTA A TINTA DENTRO DA ARTE, em fracao dela (0 a 1): a caixa das letras coloridas, lida da propria textura.
	/// E com ela que a bancada sabe onde a tinta TEM de aparecer na foto -- sem numero medido a mao.
	/// </summary>
	private Rect2 _tintaNaArte;

	/// <summary>A caixa do CORPO da arte (tudo o que nao e transparente: a tinta e o contorno preto), em fracao dela.</summary>
	private Rect2 _corpoNaArte;

	/// <summary>Um ponto no meio de uma letra AMARELA (um quadrado de 21 px todo amarelo em volta), em fracao da arte.</summary>
	private Vector2 _pontoAmarelo;

	private bool LerATintaDaArte()
	{
		if (_tintaNaArte.Size.X > 0) return true;
		if (TituloDoJogo.Logo?.GetImage() is not { } arte || arte.IsEmpty()) return false;
		if (arte.IsCompressed()) arte.Decompress();
		var tudo = new Rect2(0, 0, arte.GetWidth(), arte.GetHeight());
		(Rect2 tinta, int n) = Medir(arte, tudo, c => c.A >= 0.5f && EhTinta(c));
		(Rect2 corpo, int m) = Medir(arte, tudo, c => c.A >= 0.5f);
		if (n == 0 || m == 0) return false;
		Rect2 EmFracao(Rect2 r) => new(r.Position / tudo.Size, r.Size / tudo.Size);
		_tintaNaArte = EmFracao(tinta);
		_corpoNaArte = EmFracao(corpo);

		static bool Amarelo(Color c) => c.A > 0.99f && c.R > 0.9f && c.G > 0.7f && c.B < 0.2f;
		for (int y = 10; y < arte.GetHeight() - 10 && _pontoAmarelo == default; y += 7)
			for (int x = 10; x < arte.GetWidth() / 3 && _pontoAmarelo == default; x += 7)
			{
				bool cheio = true;
				for (int dy = -10; dy <= 10 && cheio; dy += 5)
					for (int dx = -10; dx <= 10 && cheio; dx += 5) cheio = Amarelo(arte.GetPixel(x + dx, y + dy));
				if (cheio) _pontoAmarelo = new Vector2(x, y) / tudo.Size;
			}
		return _pontoAmarelo != default;
	}

	/// <summary>A caixa da tinta de um logo desenhado num retangulo de arte (px da base), em px da FOTO.</summary>
	private Rect2 TintaEsperada(Rect2 arte, float porPx) =>
		new((arte.Position + _tintaNaArte.Position * arte.Size) * porPx, _tintaNaArte.Size * arte.Size * porPx);

	/// <summary>A caixa do corpo (tinta e contorno) do mesmo logo, em px da FOTO.</summary>
	private Rect2 CorpoEsperado(Rect2 arte, float porPx) =>
		new((arte.Position + _corpoNaArte.Position * arte.Size) * porPx, _corpoNaArte.Size * arte.Size * porPx);

	/// <summary>
	/// QUANTO BRILHO HA EM VOLTA DO LOGO: os pixels laranja-escuros da moldura que vai de 2 a 16 px (da base) pra fora
	/// do corpo dele. Ali nao ha tinta nem contorno -- so o fundo, ou o brilho.
	/// </summary>
	private int BrilhoEmVolta(Image foto, Rect2 arte, float porPx)
	{
		Rect2 corpo = CorpoEsperado(arte, porPx), dentro = corpo.Grow(2f * porPx), fora = corpo.Grow(16f * porPx);
		int n = 0;
		for (int y = Math.Max(0, (int)fora.Position.Y); y < Math.Min(foto.GetHeight(), (int)fora.End.Y); y++)
			for (int x = Math.Max(0, (int)fora.Position.X); x < Math.Min(foto.GetWidth(), (int)fora.End.X); x++)
				if (!dentro.HasPoint(new Vector2(x, y)) && EhBrilho(foto.GetPixel(x, y))) n++;
		return n;
	}

	private static bool Perto(Rect2 a, Rect2 b, float folga) =>
		Math.Abs(a.Position.X - b.Position.X) <= folga && Math.Abs(a.Position.Y - b.Position.Y) <= folga
		&& Math.Abs(a.End.X - b.End.X) <= folga && Math.Abs(a.End.Y - b.End.Y) <= folga;

	private static string R(Rect2 r) => $"({N(r.Position.X, "0.#")}, {N(r.Position.Y, "0.#")}) {N(r.Size.X, "0.#")}x{N(r.Size.Y, "0.#")}";

	/// <summary>Poe uma abertura PARADA num instante, espera o desenho e fotografa. Devolve a foto e a abertura (pra soltar).</summary>
	private (Image? Foto, AberturaDoJogo? Abertura) _pose;

	private IEnumerable<double> Posar(Boot boot, double t, string nome)
	{
		AberturaDoJogo a = boot.AberturaDeTeste(posadaEm: t);
		yield return 0;
		yield return 0;
		yield return 0.05;
		_pose = (Fotografar(nome), a);
	}

	private IEnumerable<double> SoltarAPose()
	{
		if (_pose.Abertura is { } a && IsInstanceValid(a)) a.Soltar();
		yield return 0;
		yield return 0;
	}

	private IEnumerable<double> AsFotos(Boot boot)
	{
		Nota("--- B. as fotos dos momentos (a abertura parada em cada instante) ---");
		if (!TemTela) { NaoMedido("B. as fotos e as medidas de pixel", "sem janela"); yield break; }

		Afirmar("B. a arte do titulo chegou da thread de carga, e o logo tem MIPMAPS (reduzido no cabecalho ele nao serrilha)",
				TituloDoJogo.Logo != null && TituloDoJogo.Brilho != null && TituloDoJogo.Logo.GetImage() is { } img0 && img0.HasMipmaps(),
				$"logo {TituloDoJogo.Logo?.GetSize()}, brilho {TituloDoJogo.Brilho?.GetSize()}");
		Afirmar("B. a proporcao da arte e a que o lugar do titulo usa (1498 x 899)",
				TituloDoJogo.Logo is { } tx && Math.Abs(tx.GetSize().X / tx.GetSize().Y - TituloDoJogo.Proporcao) < 0.002f);
		if (!LerATintaDaArte()) { Afirmar("B. deu pra ler a tinta da arte", false); yield break; }

		Vector2 tela = Tela;
		Rect2 noMenu = boot.TituloDoMenuDeTeste?.ArteNaTela ?? default;
		TirarOMouse();

		// ---- 0,10 s: o branco do comeco ----
		foreach (double d in Posar(boot, 0.10, "abertura-1-o-branco-do-comeco")) yield return d;
		if (_pose.Foto is { } f1)
		{
			float porPx = f1.GetWidth() / tela.X;
			float luz = LuzMedia(f1, new Rect2(0, 0, f1.GetWidth(), f1.GetHeight()));
			Afirmar("B. 0,10 s: a tela e o BRANCO do comeco sumindo (60% de branco sobre o fundo escuro), sem logo ainda",
					Math.Abs(luz - 0.62f) < 0.04f && Medir(f1, new Rect2(0, 0, f1.GetWidth(), f1.GetHeight()), EhTintaFraca).Quantos < 200,
					$"luz media {N(luz)} (esperado ~0,62); foto {f1.GetWidth()}x{f1.GetHeight()}, {N(porPx)} px por px da base");
		}
		foreach (double d in SoltarAPose()) yield return d;

		// ---- 0,43 s: o logo caindo, grande e meio transparente ----
		foreach (double d in Posar(boot, 0.43, "abertura-2-o-logo-caindo")) yield return d;
		if (_pose.Foto is { } f2)
		{
			float porPx = f2.GetWidth() / tela.X;
			AberturaDoJogo.Estado e = AberturaDoJogo.Em(0.43);
			Rect2 caindo = AberturaDoJogo.LugarDoLogo(e, tela, noMenu);
			Rect2 esperada = TintaEsperada(caindo, porPx);
			(Rect2 medida, int _) = Medir(f2, new Rect2(0, 0, f2.GetWidth(), f2.GetHeight()), EhTintaFraca);
			Rect2 assentada = TintaEsperada(AberturaDoJogo.LogoAssentado(tela), porPx);
			Afirmar("B. 0,43 s: o logo esta CAINDO -- 1,41x o tamanho de assentado, centrado, e a tinta esta onde a receita poe",
					Perto(medida, esperada, 3f) && Math.Abs(medida.Size.X / assentada.Size.X - 1.412f) < 0.01f,
					$"tinta {R(medida)} | esperada {R(esperada)} | {N(medida.Size.X / assentada.Size.X)}x a de assentado");
			Color miolo = f2.GetPixelv((Vector2I)((caindo.Position + _pontoAmarelo * caindo.Size) * porPx));
			Afirmar("B. 0,43 s: ... e ainda meio transparente (o amarelo de uma letra com 61% do corpo)", miolo.R > 0.50f && miolo.R < 0.72f && miolo.G > 0.40f && miolo.B < 0.12f, $"cor {miolo}");
			int antes = BrilhoEmVolta(f2, caindo, porPx);
			Afirmar("B. 0,43 s: antes do impacto nao ha brilho em volta do logo", antes < 60, $"{antes} px de brilho");
		}
		foreach (double d in SoltarAPose()) yield return d;

		// ---- 0,59 s: o clarao do impacto ----
		foreach (double d in Posar(boot, 0.59, "abertura-3-o-clarao-do-impacto")) yield return d;
		if (_pose.Foto is { } f3)
		{
			float luz = LuzMedia(f3, new Rect2(0, 0, f3.GetWidth(), f3.GetHeight()));
			Afirmar("B. 0,59 s: o CLARAO do impacto cobre a tela (74% de branco)", luz > 0.68f && luz < 0.90f, $"luz media {N(luz)}");
		}
		foreach (double d in SoltarAPose()) yield return d;

		// ---- 0,73 s: o anel de choque, no raio da receita ----
		foreach (double d in Posar(boot, 0.73, "abertura-4-o-anel-de-choque")) yield return d;
		if (_pose.Foto is { } f4)
		{
			(float noTraco, float dentro) = LuzDoAnel(f4, tela, 889.31f);
			Afirmar("B. 0,73 s: o ANEL de choque esta no raio da receita (889 px do quadro de 1920): branco CHEIO no traco (como no clipe), escuro logo por dentro",
					noTraco > 0.80f && dentro < 0.12f, $"luz no traco {N(noTraco)}, por dentro {N(dentro)}");

		}
		foreach (double d in SoltarAPose()) yield return d;

		AberturaDoJogo.QuedaLentaDeTeste = true;   // (a pose refaz a conta a cada quadro: o defeito fica ligado ate a foto)
		try
		{
			foreach (double d in Posar(boot, 0.73, "abertura-4x-defeito-a-queda-lenta")) yield return d;
			if (_pose.Foto is { } fd)
			{
				(float t2, float d2) = LuzDoAnel(fd, tela, 889.31f);
				Afirmar("(defeito injetado: a queda dura o dobro) aos 0,73 s o logo ainda esta caindo e NAO ha anel -- a medida do pixel reprova",
						!(t2 > 0.80f && d2 < 0.12f), $"luz no traco {N(t2)}, por dentro {N(d2)}");
			}
		}
		finally { AberturaDoJogo.QuedaLentaDeTeste = false; }
		foreach (double d in SoltarAPose()) yield return d;

		// ---- 0,77 s e 1,07 s: os dois quadros de previa do trailer (o anel aberto, as faiscas caindo) ----
		foreach (double d in Posar(boot, 0.77, "abertura-5-o-anel-aberto")) yield return d;
		foreach (double d in SoltarAPose()) yield return d;

		foreach (double d in Posar(boot, 1.07, "abertura-6-as-faiscas")) yield return d;
		int faiscasAos107 = 0;
		if (_pose.Foto is { } f6)
		{
			float porPx = f6.GetWidth() / tela.X;
			Rect2 arte = AberturaDoJogo.LugarDoLogo(AberturaDoJogo.Em(1.07), tela, noMenu);
			faiscasAos107 = FaiscasEmVolta(f6, arte, porPx);
		}
		foreach (double d in SoltarAPose()) yield return d;

		// ---- 2,00 s: assentado, respirando, com o brilho ----
		foreach (double d in Posar(boot, 2.00, "abertura-7-assentado-com-o-brilho")) yield return d;
		if (_pose.Foto is { } f7)
		{
			float porPx = f7.GetWidth() / tela.X;
			Rect2 arte = AberturaDoJogo.LugarDoLogo(AberturaDoJogo.Em(2.00), tela, noMenu);
			Rect2 esperada = TintaEsperada(arte, porPx);
			(Rect2 medida, int _) = Medir(f7, new Rect2(0, 0, f7.GetWidth(), f7.GetHeight()), c => EhTinta(c));
			Afirmar("B. 2,00 s: o logo ASSENTADO esta inteiro, centrado e do tamanho da receita (61% da largura da tela em 16:9)",
					Perto(medida, esperada, 3f) && Math.Abs((medida.Position.X + medida.End.X) / 2f - (esperada.Position.X + esperada.End.X) / 2f) <= 2f,
					$"tinta {R(medida)} | esperada {R(esperada)} | arte {R(arte)} de uma tela {tela}");
			int brilho = BrilhoEmVolta(f7, arte, porPx);
			Afirmar("B. 2,00 s: atras dele ha o BRILHO LARANJA (laranja escuro em volta do contorno, onde antes do impacto nao havia nada)",
					brilho >= 400, $"{brilho} px de brilho na moldura em volta do corpo");
			int faiscasAos200 = FaiscasEmVolta(f7, arte, porPx);
			Afirmar("B. as FAISCAS estao na tela aos 1,07 s (amarelas cheias, em volta do logo) e ja acabaram aos 2,00 s", faiscasAos107 >= 150 && faiscasAos200 <= 5,
					$"{faiscasAos107} px de faisca aos 1,07 s, {faiscasAos200} aos 2,00 s");
			Color ceu = CorMedia(f7, [new Rect2(8, 8, 40, 40)]);
			Afirmar("B. o FUNDO e o preto azulado da receita (6, 5, 14), com estrelas", Math.Abs(ceu.R - 6 / 255f) < 0.02f && Math.Abs(ceu.B - 14 / 255f) < 0.03f
					&& Medir(f7, new Rect2(0, 0, f7.GetWidth(), f7.GetHeight() * 0.12f), c => Luz(c) > 0.2f && Math.Abs(c.R - c.B) < 0.05f).Quantos >= 8, $"canto {ceu}");
		}
		foreach (double d in SoltarAPose()) yield return d;

		// ---- a subida, aos 45% dela: o fundo ja deixa ver o menu, e so ha UM titulo ----
		double naSubida = AberturaDoJogo.ComecoDaSubida + 0.45 * AberturaDoJogo.Subida;
		foreach (double d in Posar(boot, naSubida, "abertura-8-subindo")) yield return d;
		if (_pose.Foto is { } f8)
		{
			float porPx = f8.GetWidth() / tela.X;
			AberturaDoJogo.Estado e = AberturaDoJogo.Em(naSubida);
			Rect2 subindo = AberturaDoJogo.LugarDoLogo(e, tela, noMenu), assentado = AberturaDoJogo.LogoAssentado(tela);
			Afirmar("B. SUBINDO: o titulo esta entre o lugar da queda e o alto do menu, menor que assentado e maior que o do menu, e o fundo ja se desfaz",
					subindo.GetCenter().Y < assentado.GetCenter().Y - 20 && subindo.GetCenter().Y > noMenu.GetCenter().Y + 20
					&& subindo.Size.X < assentado.Size.X && subindo.Size.X > noMenu.Size.X && e.Fundo < 0.6f && e.Fundo > 0.2f,
					$"arte {R(subindo)}; fundo {N(e.Fundo)}");
			(Rect2 medida, int _) = Medir(f8, new Rect2(0, 0, f8.GetWidth(), f8.GetHeight()), c => EhTinta(c));
			Afirmar("B. SUBINDO: a tinta na foto esta onde a conta poe o titulo", Perto(medida, TintaEsperada(subindo, porPx), 4f), $"tinta {R(medida)} | esperada {R(TintaEsperada(subindo, porPx))}");

			int doMenu = TintaDoTituloDoMenu(f8, noMenu, subindo, porPx);
			Afirmar("B. SUBINDO: UM TITULO SO no pixel -- no lugar do titulo do menu (acima do que sobe) nao ha tinta de logo", doMenu < 30, $"{doMenu} px de tinta");

			// o contra-exemplo: o titulo do menu aceso por baixo aparece atraves do fundo que se desfaz
			foreach (double d in SoltarAPose()) yield return d;
			AberturaDoJogo.SemApagarOTituloDoMenuDeTeste = true;
			AberturaDoJogo comDois;
			try { comDois = boot.AberturaDeTeste(posadaEm: naSubida); }
			finally { AberturaDoJogo.SemApagarOTituloDoMenuDeTeste = false; }
			yield return 0;
			yield return 0;
			yield return 0.05;
			bool doisNoNo = TituloDoJogo.NaTela + (AberturaDoJogo.LogoNaTela ? 1 : 0) == 2;
			Image? fx = Fotografar("abertura-8x-defeito-dois-titulos");
			int comDefeito = fx != null ? TintaDoTituloDoMenu(fx, noMenu, subindo, porPx) : 0;
			Afirmar("(defeito injetado: o titulo do menu nao e apagado) ha DOIS titulos -- a contagem e o pixel reprovam", doisNoNo && comDefeito >= 30, $"contagem de dois: {doisNoNo}; {comDefeito} px de tinta no lugar do titulo do menu");
			comDois.Soltar();
			yield return 0;
			yield return 0;
		}
		else foreach (double d in SoltarAPose()) yield return d;

		// ---- o estado final: o titulo no alto, com o menu ----
		foreach (double d in OTituloNoAltoComOMenu(boot, "abertura-9-no-alto-com-o-menu")) yield return d;

		// (as fotos levaram uns segundos: o que a abertura segurou do aquecimento ja teve tempo de acabar)
		Afirmar("B. o AQUECIMENTO terminou inteiro depois da abertura: a fila fechada e o ensaio feito (ela so o segura enquanto o titulo se mexe)",
				Aquecimento.Terminou && Aquecimento.Ensaiou, $"fila {Aquecimento.Prontos} de {Aquecimento.Total}, ensaio feito: {Aquecimento.Ensaiou}");
	}

	/// <summary>
	/// A LUZ DO ANEL na linha do meio da tela: no traco (a faixa de 16 px da receita, por dentro do raio) e num pedaco
	/// escuro logo por dentro dele, dos dois lados.
	/// </summary>
	private static (float NoTraco, float PorDentro) LuzDoAnel(Image foto, Vector2 tela, float raioDaReceita)
	{
		float k = AberturaDoJogo.Unidade(tela), porPx = foto.GetWidth() / tela.X;
		float cx = tela.X / 2f, cy = tela.Y / 2f, rx = raioDaReceita * k, g = AberturaDoJogo.GrossuraDoAnel * k;
		float traco = 0f, dentro = 0f;
		foreach (int lado in new[] { -1, 1 })
		{
			float a = cx + lado * (rx - g * 0.8f), b = cx + lado * (rx - g * 0.2f);
			traco += LuzMedia(foto, new Rect2(Math.Min(a, b) * porPx, (cy - 3) * porPx, Math.Abs(b - a) * porPx, 6 * porPx)) / 2f;
			float c = cx + lado * (rx - g - 40f * k), e = cx + lado * (rx - g - 100f * k);
			dentro += LuzMedia(foto, new Rect2(Math.Min(c, e) * porPx, (cy - 3) * porPx, Math.Abs(e - c) * porPx, 6 * porPx)) / 2f;
		}
		return (traco, dentro);
	}

	/// <summary>
	/// QUANTA FAISCA HA EM VOLTA DO LOGO: os pixels amarelo-claros da foto fora da caixa do CORPO dele (dentro dela o
	/// amarelo das letras passaria pelo mesmo teste).
	/// </summary>
	private int FaiscasEmVolta(Image foto, Rect2 arte, float porPx)
	{
		Rect2 corpo = CorpoEsperado(arte, porPx).Grow(4f * porPx);
		int n = 0;
		for (int y = 0; y < foto.GetHeight(); y++)
			for (int x = 0; x < foto.GetWidth(); x++)
				if (!corpo.HasPoint(new Vector2(x, y)) && EhFaisca(foto.GetPixel(x, y))) n++;
		return n;
	}

	/// <summary>
	/// O MOUSE SAI DE CIMA DO MENU antes de uma foto: o robo acabou de clicar num botao, e o botao debaixo do mouse
	/// acende de laranja -- tinta quente que a medida do titulo contaria como logo.
	/// </summary>
	private void TirarOMouse() => Mover(new Vector2(3f, Tela.Y * 0.62f));

	/// <summary>Quantos pixels da foto passam no teste FORA de um retangulo (px da base).</summary>
	private static int TintaForaDe(Image foto, Rect2 fora, float porPx, Func<Color, bool> teste)
	{
		var f = new Rect2(fora.Position * porPx, fora.Size * porPx);
		int n = 0;
		for (int y = 0; y < foto.GetHeight(); y++)
			for (int x = 0; x < foto.GetWidth(); x++)
				if (!f.HasPoint(new Vector2(x, y)) && teste(foto.GetPixel(x, y))) n++;
		return n;
	}

	/// <summary>
	/// A TINTA NO LUGAR DO TITULO DO MENU enquanto o da abertura sobe: as linhas da arte do menu que ficam ACIMA do logo
	/// que sobe (com folga pro brilho dele). Ali, tinta de logo so pode ser de um segundo titulo.
	/// </summary>
	private int TintaDoTituloDoMenu(Image foto, Rect2 noMenu, Rect2 subindo, float porPx)
	{
		Rect2 tintaDoMenu = TintaEsperada(noMenu, porPx);
		float topoDoQueSobe = TintaEsperada(subindo, porPx).Position.Y;
		float folgaDoBrilho = 2.3f * 38f / TituloDoJogo.LarguraDaReceita * subindo.Size.X * porPx;   // 2,3 desvios do borrao
		var faixa = new Rect2(tintaDoMenu.Position, new Vector2(tintaDoMenu.Size.X, Math.Max(0f, topoDoQueSobe - folgaDoBrilho - tintaDoMenu.Position.Y)));
		return Medir(foto, faixa, EhTintaFraca).Quantos;
	}

	/// <summary>O estado FINAL, fotografado: o titulo no alto, inteiro e centrado, um so, e o menu embaixo dele.</summary>
	private IEnumerable<double> OTituloNoAltoComOMenu(Boot boot, string nome)
	{
		TirarOMouse();
		yield return 0.15;
		if (boot.TituloDoMenuDeTeste is not { } titulo) { Afirmar("o titulo do menu esta de pe", false); yield break; }

		Vector2 tela = Tela;
		Rect2 arte = titulo.ArteNaTela;
		Control? formulario = Todos(titulo.GetParent()).OfType<PanelContainer>().FirstOrDefault(p => p.IsVisibleInTree());
		Afirmar("NO ALTO: o titulo ocupa a faixa de cima (25% da altura), centrado, e o menu fica inteiro embaixo dele, sem encostar",
				Math.Abs(arte.GetCenter().X - tela.X / 2f) <= 0.75f && arte.Position.Y >= 0 && arte.End.Y <= tela.Y * TituloDoJogo.FaixaDoTopo + 0.5f
				&& formulario != null && formulario.GetGlobalRect().Position.Y >= arte.End.Y && formulario.GetGlobalRect().End.Y <= tela.Y,
				$"arte {R(arte)} numa tela {tela}; formulario {(formulario != null ? R(formulario.GetGlobalRect()) : "?")}");

		if (!TemTela) { NaoMedido("NO ALTO: o pixel do titulo no alto", "sem janela"); yield break; }
		// (a arte vem da thread de carga: espera chegar)
		for (int i = 0; i < 40 && !TituloDoJogo.ArteResolvida; i++) yield return 0.05;
		yield return 0.1;
		if (!LerATintaDaArte()) { Afirmar("NO ALTO: deu pra ler a tinta da arte", false); yield break; }
		if (Fotografar(nome) is not { } foto) yield break;

		float porPx = foto.GetWidth() / tela.X;
		Rect2 esperada = TintaEsperada(arte, porPx);
		(Rect2 medida, int _) = Medir(foto, new Rect2(0, 0, foto.GetWidth(), foto.GetHeight() * (TituloDoJogo.FaixaDoTopo + 0.05f)), c => EhTinta(c));
		Afirmar("NO ALTO (pixel): a tinta do titulo esta inteira e centrada na faixa de cima", Perto(medida, esperada, 2.5f)
				&& Math.Abs((medida.Position.X + medida.End.X) / 2f - foto.GetWidth() / 2f) <= esperada.Size.X * 0.03f, $"tinta {R(medida)} | esperada {R(esperada)}");
		int fora = TintaForaDe(foto, TituloDoJogo.LugarDoBrilho(arte), porPx, c => EhTinta(c));
		Afirmar("NO ALTO (pixel): UM TITULO SO -- fora do lugar dele nao ha tinta de logo na tela", fora < 30, $"{fora} px");
		int brilho = BrilhoEmVolta(foto, arte, porPx);
		Afirmar("NO ALTO (pixel): o titulo parado tem o brilho laranja atras", brilho >= 150, $"{brilho} px de brilho na moldura em volta do corpo");
		Color fundo = foto.GetPixel(6, foto.GetHeight() - 6);
		Afirmar("NO ALTO (pixel): o fundo e o do menu de sempre (o azul escuro do tema), e nao o da abertura",
				Math.Abs(fundo.R - Tema.Fundo.R) < 0.012f && Math.Abs(fundo.G - Tema.Fundo.G) < 0.012f && Math.Abs(fundo.B - Tema.Fundo.B) < 0.012f, $"canto {fundo}");
	}

	// =====================================================================
	// C. PULAR
	// =====================================================================
	private IEnumerable<double> EsperarORelogio(AberturaDoJogo a, double ate)
	{
		ulong prazo = Time.GetTicksUsec() + 8_000_000;
		while (IsInstanceValid(a) && AberturaDoJogo.Viva == a && a.Relogio < ate && Time.GetTicksUsec() < prazo) yield return 0;
	}

	private IEnumerable<double> Pular(Boot boot)
	{
		Nota("--- C. pular ---");
		if (BotaoComTexto(boot, "Opções") is not { } opcoes) { Afirmar("C. achei o botao \"Opções\" do menu", false); yield break; }
		Vector2 noBotao = opcoes.GetGlobalRect().GetCenter();

		// ---- as teclas que so acompanham nao pulam; uma tecla de verdade pula ----
		AberturaDoJogo a = boot.AberturaDeTeste();
		foreach (double d in EsperarORelogio(a, 0.45)) yield return d;
		Teclar(Key.Shift);
		Teclar(Key.Ctrl);
		Teclar(Key.Alt);
		Afirmar("C. Shift, Ctrl e Alt sozinhos NAO pulam (sao teclas que acompanham outra)", AberturaDoJogo.Viva == a && a.Relogio < 1.0);
		Teclar(Key.Space);
		Afirmar("C. TECLA: o espaco PULA na hora -- a abertura some, o titulo do menu acende, um titulo so",
				AberturaDoJogo.Viva == null && boot.TituloDoMenuDeTeste is { Apagado: false } && TituloDoJogo.NaTela == 1 && !AberturaDoJogo.LogoNaTela
				&& AberturaDoJogo.UltimaCorrida is { Pulada: true, NoRelogio: < 1.0 }, $"pulada aos {N(AberturaDoJogo.UltimaCorrida?.NoRelogio ?? -1)} s");
		yield return 0.05;
		Fotografar("abertura-10-pulada-o-estado-final");
		yield return AberturaDoJogo.GuardaDoPulo + 0.15;
		Afirmar("C. passada a guarda do pulo, a abertura saiu da arvore (o escudo nao fica)", boot.GetNodeOrNull("Abertura") == null);

		// ---- o ESC pula e NAO abre as opcoes ----
		a = boot.AberturaDeTeste();
		foreach (double d in EsperarORelogio(a, 0.8)) yield return d;
		Teclar(Key.Escape);
		Afirmar("C. ESC: pula, e o gesto MORRE na abertura -- as opcoes nao abrem", AberturaDoJogo.Viva == null && !OpcoesAbertas);
		yield return AberturaDoJogo.GuardaDoPulo + 0.15;
		Teclar(Key.Escape);
		yield return 0.1;
		Afirmar("C. ESC: depois da guarda, o ESC volta a ser do menu (abre as opcoes)", OpcoesAbertas);
		PauseMenu.Instancia?.Fechar("bancada");
		yield return 0.1;

		// ---- o clique em cima de um botao do menu pula e NAO aperta o botao ----
		a = boot.AberturaDeTeste();
		foreach (double d in EsperarORelogio(a, 1.2)) yield return d;
		Clicar(noBotao);
		Afirmar("C. CLIQUE em cima do botao \"Opções\": pula, e o botao NAO e apertado", AberturaDoJogo.Viva == null && !OpcoesAbertas);
		yield return 0.08;
		Clicar(noBotao);   // o segundo toque de um clique duplo, dentro da guarda
		yield return 0.05;
		Afirmar("C. CLIQUE DUPLO: o segundo toque, 0,08 s depois, tambem nao aperta o botao (a guarda do pulo)", !OpcoesAbertas);
		yield return AberturaDoJogo.GuardaDoPulo + 0.15;
		Clicar(noBotao);
		yield return 0.1;
		Afirmar("C. CLIQUE: depois da guarda o menu e do jogador (o mesmo clique abre as opcoes)", OpcoesAbertas);
		PauseMenu.Instancia?.Fechar("bancada");
		yield return 0.1;

		// ---- o clique durante a SUBIDA, com o menu ja aparecendo por baixo ----
		a = boot.AberturaDeTeste();
		foreach (double d in EsperarORelogio(a, AberturaDoJogo.ComecoDaSubida + 0.6 * AberturaDoJogo.Subida)) yield return d;
		Clicar(noBotao);
		yield return 0.05;
		Afirmar("C. CLIQUE NA SUBIDA (o botao ja aparece atraves do fundo): pula e nao aperta", AberturaDoJogo.Viva == null && !OpcoesAbertas);
		yield return AberturaDoJogo.GuardaDoPulo + 0.15;

		// ---- o contra-exemplo: o gesto vaza ----
		AberturaDoJogo.PuloVazaDeTeste = true;
		try
		{
			a = boot.AberturaDeTeste();
			foreach (double d in EsperarORelogio(a, 1.2)) yield return d;
			Clicar(noBotao);
			yield return 0.1;
			bool vazouOClique = OpcoesAbertas;
			PauseMenu.Instancia?.Fechar("bancada");
			yield return AberturaDoJogo.GuardaDoPulo + 0.15;

			a = boot.AberturaDeTeste();
			foreach (double d in EsperarORelogio(a, 0.8)) yield return d;
			Teclar(Key.Escape);
			yield return 0.1;
			bool vazouOEsc = OpcoesAbertas;
			PauseMenu.Instancia?.Fechar("bancada");
			Afirmar("(defeito injetado: o gesto que pula nao morre na abertura) o clique APERTA o botao e o ESC ABRE as opcoes -- as duas checagens sabem reprovar",
					vazouOClique && vazouOEsc, $"clique vazou {vazouOClique}, ESC vazou {vazouOEsc}");
		}
		finally { AberturaDoJogo.PuloVazaDeTeste = false; }
		yield return AberturaDoJogo.GuardaDoPulo + 0.15;
		Afirmar("C. nenhuma das aberturas de bancada contou como a do jogo (continua uma vez)", AberturaDoJogo.Vezes == 1);
	}

	// =====================================================================
	// D. O TAMANHO DA TELA
	// =====================================================================
	private Vector2I? _baseDaTela;

	private IEnumerable<double> OTamanhoDoTitulo(Boot boot, bool comAbertura)
	{
		Nota("--- D. o tamanho da tela ---");
		Window raiz = GetTree().Root;
		_baseDaTela = raiz.ContentScaleSize;
		Vector2I janela = DisplayServer.WindowGetSize();

		// A REGUA DE UM TAMANHO: o titulo do menu inteiro e centrado na faixa de cima, o menu inteiro embaixo dele, e o
		// logo assentado da abertura inteiro e centrado -- pelos retangulos que cada um tem NA TELA de agora.
		void Cobrar(string nome)
		{
			Vector2 tela = Tela;
			if (boot.TituloDoMenuDeTeste is not { } titulo) { Afirmar($"D. {nome}: o titulo do menu esta de pe", false); return; }
			Rect2 arte = titulo.ArteNaTela, visivel = new(Vector2.Zero, tela);
			Control? formulario = Todos(titulo.GetParent()).OfType<PanelContainer>().FirstOrDefault(p => p.IsVisibleInTree());
			Afirmar($"D. {nome} (a tela vira {N(tela.X, "0")} x {N(tela.Y, "0")}): o titulo do menu fica inteiro, centrado, na faixa de cima, e o menu inteiro embaixo",
					visivel.Encloses(arte) && Math.Abs(arte.GetCenter().X - tela.X / 2f) <= 0.75f && arte.End.Y <= tela.Y * TituloDoJogo.FaixaDoTopo + 0.5f
					&& Math.Abs(arte.Size.Y - tela.Y * TituloDoJogo.FaixaDoTopo * TituloDoJogo.AlturaNaFaixa) <= 1f
					&& formulario != null && formulario.GetGlobalRect().Position.Y >= arte.End.Y && visivel.Encloses(formulario.GetGlobalRect()),
					$"arte {R(arte)}; formulario {(formulario != null ? R(formulario.GetGlobalRect()) : "?")}");

			Rect2 assentado = AberturaDoJogo.LogoAssentado(tela);
			Afirmar($"D. {nome}: o logo ASSENTADO da abertura cabe inteiro e centrado (o quadro da receita encaixa pelo eixo mais apertado)",
					visivel.Encloses(assentado) && Math.Abs(assentado.GetCenter().X - tela.X / 2f) <= 0.75f
					&& (Math.Abs(assentado.Size.X / tela.X - 1180f / 1920f) < 0.002f || Math.Abs(assentado.Size.Y / tela.Y - 1180f / TituloDoJogo.Proporcao / 1080f) < 0.002f),
					$"logo {R(assentado)}");
		}

		// ---- UMA BASE DE DESENHO MAIOR (a "resolucao" das opcoes): a tela passa a ter 1920 x 1080 px ----
		raiz.ContentScaleSize = new Vector2I(1920, 1080);
		yield return 0.25;
		Cobrar("base de desenho 1920x1080");
		if (boot.TituloDoMenuDeTeste is { } naBaseMaior)
		{
			TituloDoJogo.PixelFixoDeTeste = true;
			try
			{
				Rect2 cravada = naBaseMaior.ArteNaTela;
				Afirmar("(defeito injetado: o titulo posto por numero fixo de pixel) numa base maior ele SAI do centro -- a regua reprova",
						Math.Abs(cravada.GetCenter().X - Tela.X / 2f) > 100f, $"arte {R(cravada)} numa tela {Tela}");
			}
			finally { TituloDoJogo.PixelFixoDeTeste = false; }
		}
		if (comAbertura && TemTela)
		{
			foreach (double d in Posar(boot, 2.00, "abertura-11-base-1920x1080-assentado")) yield return d;
			foreach (double d in SoltarAPose()) yield return d;
			Fotografar("abertura-12-base-1920x1080-no-alto");
		}
		raiz.ContentScaleSize = _baseDaTela.Value;
		_baseDaTela = null;
		yield return 0.25;

		// ---- A JANELA DE OUTROS TAMANHOS, de verdade: maior, ultralarga (21:9) e "quadrada" (4:3) ----
		if (!TemTela) { NaoMedido("D. a janela de outros tamanhos (maior, 21:9, 4:3) e o pixel do titulo nela", "sem janela"); yield break; }
		foreach ((Vector2I tamanho, string nome) in new[] { (new Vector2I(1680, 720), "janela ultralarga de 1680x720"), (new Vector2I(960, 720), "janela 4:3 de 960x720") })
		{
			DisplayServer.WindowSetSize(tamanho);
			TirarOMouse();
			yield return 0.5;
			Cobrar($"{nome} (ficou {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y})");
			Fotografar("abertura-13-" + (tamanho.X > 1000 ? "janela-ultralarga-no-alto" : "janela-4por3-no-alto"));
		}

		DisplayServer.WindowSetSize(new Vector2I(1600, 900));
		yield return 0.5;
		Cobrar($"janela maior de 1600x900 (ficou {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y})");
		foreach (double d in OTituloNoAltoComOMenu(boot, "abertura-14-janela-1600x900-no-alto")) yield return d;
		if (comAbertura && LerATintaDaArte())
		{
			foreach (double d in Posar(boot, 2.00, "abertura-14-janela-1600x900-assentado")) yield return d;
			if (_pose.Foto is { } f)
			{
				Vector2 tela = Tela;
				float porPx = f.GetWidth() / tela.X;
				Rect2 esperada = TintaEsperada(AberturaDoJogo.LugarDoLogo(AberturaDoJogo.Em(2.00), tela, default), porPx);
				(Rect2 medida, int _) = Medir(f, new Rect2(0, 0, f.GetWidth(), f.GetHeight()), c => EhTinta(c));
				Afirmar("D. janela maior (pixel): o logo assentado esta inteiro, centrado e proporcionalmente do mesmo tamanho",
						f.GetWidth() > 1300 && Perto(medida, esperada, 3.5f) && Math.Abs((medida.Position.X + medida.End.X) / 2f - f.GetWidth() / 2f) <= esperada.Size.X * 0.03f,
						$"foto {f.GetWidth()}x{f.GetHeight()}; tinta {R(medida)} | esperada {R(esperada)}");
			}
			foreach (double d in SoltarAPose()) yield return d;
		}
		DisplayServer.WindowSetSize(janela);
		yield return 0.5;
	}

	// =====================================================================
	// E. VOLTAR AO MENU NAO REPRISA
	// =====================================================================
	private string NaoReprisou() =>
		$"vezes {AberturaDoJogo.Vezes}, viva {AberturaDoJogo.Viva != null}, node {_boot!.GetNodeOrNull("Abertura") != null}, titulos {TituloDoJogo.NaTela}";

	private bool SemReprise(Boot boot) =>
		AberturaDoJogo.Vezes == 1 && AberturaDoJogo.Viva == null && boot.GetNodeOrNull("Abertura") == null && TituloDoJogo.NaTela == 1 && !AberturaDoJogo.LogoNaTela;

	private IEnumerable<double> Hospedar(Boot boot, string conta)
	{
		LineEdit? campoDaConta = Todos(boot).OfType<LineEdit>().FirstOrDefault(l => l.PlaceholderText.Contains("perfil") && l.IsVisibleInTree());
		LineEdit? campoDaSenha = Todos(boot).OfType<LineEdit>().FirstOrDefault(l => l.Secret && l.IsVisibleInTree());
		Button? hospedar = BotaoComTexto(boot, "Hospedar partida (servidor local)");
		if (campoDaConta == null || campoDaSenha == null || hospedar == null) { Afirmar("E. achei os campos e o botao de hospedar do menu", false); yield break; }
		campoDaConta.Text = conta;
		campoDaSenha.Text = "bancada";
		hospedar.EmitSignal(BaseButton.SignalName.Pressed);
		// (v0.3.0) A TELA DE HOSPEDAR pergunta o modo das racas antes de subir o servidor (`Boot.ModoDeRacas.cs`): o padrao. Com o
		// servidor deste processo ja de pe ela nao aparece, e o botao nao existe.
		BotaoComTexto(boot, "Modo padrão")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.1;
	}

	private IEnumerable<double> VoltarAoMenu(Boot boot)
	{
		Nota("--- E. voltar ao menu nao reprisa ---");
		if (Jandirus.Server.GameServer.Instance is not { } srv || C is not { } cli) { Afirmar("E. ha servidor e cliente neste processo", false); yield break; }
		string conta = Arg("--conta") is { Length: > 0 } c0 ? c0 : "bancada_abertura";

		// ---- 1. a SELECAO: o titulo no alto dela tambem, e um so ----
		foreach (double d in Hospedar(boot, conta)) yield return d;
		for (int i = 0; i < 300 && boot.SelecaoDeTeste is not { Visible: true }; i++) yield return 0.1;
		yield return 0.3;
		CharacterSelect? selecao = boot.SelecaoDeTeste;
		Afirmar("E. hospedei pelo botao do menu e a SELECAO de personagem chegou", selecao is { Visible: true } && srv.Running && cli.Connected);
		if (selecao == null) yield break;
		TituloDoJogo? daSelecao = Todos(selecao).OfType<TituloDoJogo>().FirstOrDefault(t => !t.IsQueuedForDeletion());
		Afirmar("E. SELECAO: ela tem o titulo no alto, o menu principal (e o titulo dele) esta escondido -- um titulo so, e a abertura nao voltou",
				daSelecao is { Apagado: false } && daSelecao.IsVisibleInTree() && !boot.MenuPrincipalNaTela && SemReprise(boot), NaoReprisou());
		if (daSelecao != null)
		{
			Rect2 arte = daSelecao.ArteNaTela;
			VBoxContainer? coluna = Todos(selecao).OfType<VBoxContainer>().FirstOrDefault();
			Afirmar("E. SELECAO: o titulo fica na faixa de cima, centrado, e os personagens inteiros embaixo dele",
					Math.Abs(arte.GetCenter().X - Tela.X / 2f) <= 0.75f && coluna != null && coluna.GetGlobalRect().Position.Y >= arte.End.Y - 0.5f
					&& coluna.GetGlobalRect().End.Y <= Tela.Y + 0.5f, $"arte {R(arte)}; coluna {(coluna != null ? R(coluna.GetGlobalRect()) : "?")}");
			Afirmar("E. SELECAO: o titulo da tela continua la (\"SEUS PERSONAGENS\"), e nao ha outro nome de jogo escrito",
					Todos(selecao).OfType<Label>().Any(l => l.Text == "SEUS PERSONAGENS") && !Todos(selecao).OfType<Label>().Any(l => l.Text.Contains("DRAGON BALL", StringComparison.OrdinalIgnoreCase)));
		}
		yield return 0.2;
		Fotografar("abertura-15-a-selecao-com-o-titulo");

		// ---- 2. sair da selecao ("Trocar de servidor") ----
		if (BotaoComTexto(selecao, "Trocar de servidor") is { } trocar) trocar.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.4;
		Afirmar("E. SAIR DA SELECAO: o menu principal volta com o titulo ja no alto, e a abertura NAO reprisa",
				boot.MenuPrincipalNaTela && boot.TituloDoMenuDeTeste is { Apagado: false } && SemReprise(boot), NaoReprisou());

		// a mesma porta do `_Ready`, chamada de novo: a trava da propria abertura recusa
		bool reprisou = boot.TentarAbrirDeNovoDeTeste();
		Afirmar("E. chamar de novo a porta do `Boot` (o que uma volta ao menu faria se reprisasse) NAO comeca outra: uma vez por execucao", !reprisou && SemReprise(boot), NaoReprisou());
		AberturaDoJogo.ReprisaDeTeste = true;
		try
		{
			bool comDefeito = boot.TentarAbrirDeNovoDeTeste();
			Afirmar("(defeito injetado: a trava de uma vez por execucao nao vale) a abertura REPRISA -- a regua reprova", comDefeito && AberturaDoJogo.Viva != null && !SemReprise(boot), NaoReprisou());
		}
		finally { AberturaDoJogo.ReprisaDeTeste = false; }
		AberturaDoJogo.Viva?.Pular();
		yield return AberturaDoJogo.GuardaDoPulo + 0.15;
		int vezesDepoisDaInjecao = AberturaDoJogo.Vezes;   // a reprise injetada contou: daqui pra frente a regua e "nao subiu mais"

		// ---- 3. o MUNDO: nenhum titulo de menu la dentro ----
		boot.EntrarSozinhoDeTeste("Abridor", "Human", true);
		foreach (double d in Hospedar(boot, conta)) yield return d;
		foreach (double d in EsperarOMundo(boot, cli)) yield return d;
		Afirmar("E. entrei no MUNDO (o personagem de bancada, pelo caminho do --host)", boot.MundoMontado && cli.Connected && cli.LocalId != 0, $"estado do menu: \"{boot.EstadoDoMenuPrincipal}\"");
		Afirmar("E. dentro do mundo nao ha titulo de menu nenhum na tela", TituloDoJogo.NaTela == 0 && AberturaDoJogo.Viva == null, $"titulos {TituloDoJogo.NaTela}");

		// ---- 4. CAIR DO SERVIDOR dentro do mundo: o servidor derruba a minha conexao pelo fio (a despedida de um kick) ----
		Afirmar("E. (montagem) o servidor derrubou a minha conexao pelo fio", srv.DerrubarDeBancada(cli.LocalId));
		for (int i = 0; i < 80 && boot.AvisoDeQuedaNaTela() == null; i++) yield return 0.1;
		yield return 0.2;
		string aviso = boot.AvisoDeQuedaNaTela() ?? "";
		Afirmar("E. CAIR DO SERVIDOR: o mundo cai, o menu principal volta com o aviso da queda, o titulo no alto (um so), e a abertura NAO reprisa",
				aviso.Contains("DESCONECTADO", StringComparison.Ordinal) && !boot.MundoMontado && boot.MenuPrincipalNaTela && boot.TituloDoMenuDeTeste is { Apagado: false }
				&& AberturaDoJogo.Vezes == vezesDepoisDaInjecao && AberturaDoJogo.Viva == null && TituloDoJogo.NaTela == 1, $"aviso \"{aviso}\"; {NaoReprisou()}");
		Fotografar("abertura-16-a-queda-com-o-titulo");
		boot.ApertarOkDoAvisoDeQueda();
		yield return 0.3;

		// ---- 5. o "DESCONECTAR" de dentro do mundo ----
		foreach (double d in Hospedar(boot, conta)) yield return d;
		foreach (double d in EsperarOMundo(boot, cli)) yield return d;
		Afirmar("E. entrei de novo no mundo (o lobby remontado funciona)", boot.MundoMontado && cli.Connected && cli.LocalId != 0, $"estado do menu: \"{boot.EstadoDoMenuPrincipal}\"");
		if (boot.MundoMontado && PauseMenu.Instancia is { } pausa)
		{
			pausa.Abrir();
			yield return 0.3;
			for (int vez = 0; vez < 2 && boot.MundoMontado; vez++)   // quem hospeda aperta duas vezes: a primeira so avisa
			{
				if (BotaoComTexto(pausa, "Desconectar") is { } desconectar) desconectar.EmitSignal(BaseButton.SignalName.Pressed);
				yield return 0.4;
			}
		}
		for (int i = 0; i < 50 && !boot.MenuPrincipalNaTela; i++) yield return 0.1;
		yield return 0.5;
		Afirmar("E. DESCONECTAR: o mundo cai, o menu principal volta com o titulo no alto (um so), e a abertura NAO reprisa",
				!boot.MundoMontado && boot.MenuPrincipalNaTela && boot.TituloDoMenuDeTeste is { Apagado: false }
				&& AberturaDoJogo.Vezes == vezesDepoisDaInjecao && AberturaDoJogo.Viva == null && boot.GetNodeOrNull("Abertura") == null && TituloDoJogo.NaTela == 1, NaoReprisou());
		foreach (double d in OTituloNoAltoComOMenu(boot, "abertura-17-de-volta-do-mundo")) yield return d;

		// ---- 6. CAIR DO SERVIDOR com a SELECAO na tela (o servidor para) ----
		// POR ULTIMO, e isto e um ACHADO de fora do topico: o `GameServer.Stop` nao esvazia a lista de quem esta na
		// tela de selecao (`_logados`), e o mesmo cliente nao consegue mais entrar neste processo depois dela -- o
		// servidor ignora o login dele, calado ("conectando..." pra sempre). Vai no relatorio; a bancada nao entra de novo.
		boot.EntrarSozinhoDeTeste("Abridor", "Human", false);
		foreach (double d in Hospedar(boot, conta)) yield return d;
		for (int i = 0; i < 300 && boot.SelecaoDeTeste is not { Visible: true }; i++) yield return 0.1;
		yield return 0.3;
		Afirmar("E. (montagem) hospedei de novo e a selecao chegou, agora com o personagem de bancada num slot", boot.SelecaoDeTeste is { Visible: true } && srv.Running);
		srv.SalvarEParar();   // o servidor some: a despedida chega pelo fio
		for (int i = 0; i < 80 && boot.AvisoDeQuedaNaTela() == null; i++) yield return 0.1;
		yield return 0.2;
		aviso = boot.AvisoDeQuedaNaTela() ?? "";
		Afirmar("E. CAIR NA SELECAO (o servidor parou): a selecao sai, o menu principal volta com o aviso e o titulo no alto (um so), e a abertura NAO reprisa",
				aviso.Contains("DESCONECTADO", StringComparison.Ordinal) && boot.SelecaoDeTeste == null && boot.MenuPrincipalNaTela && boot.TituloDoMenuDeTeste is { Apagado: false }
				&& AberturaDoJogo.Vezes == vezesDepoisDaInjecao && AberturaDoJogo.Viva == null && TituloDoJogo.NaTela == 1, $"aviso \"{aviso}\"; {NaoReprisou()}");
		boot.ApertarOkDoAvisoDeQueda();
		yield return 0.2;
	}

	private IEnumerable<double> EsperarOMundo(Boot boot, GameClient cli)
	{
		for (int i = 0; i < 600 && !(boot.MundoMontado && cli.LocalId != 0 && cli.Connected); i++) yield return 0.1;
		yield return 1.5;
	}
}
