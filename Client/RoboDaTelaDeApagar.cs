using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA TELA DE APAGAR (`--diagapagar`) ============================
/// O PEDIDO DO DONO, literal: *"a tela de DELETAR PERSONAGEM ta TODA TORTA, e se eu coloco em FULL
/// SCREEN o jogo e dps em JANELA, ela MUDA DE POSICAO e fica todo torto. coloque ele pra ficar SEMPRE
/// FIXO NO CENTRO DA TELA e MELHORE O LAYOUT dele pq ta todo bagunçado"*.
///
/// ============================ POR QUE ISTO NAO PODE SER SO UMA FOTO ============================
/// O conserto foi trocar o `ConfirmationDialog` pelo molde ancorado do resto do jogo. Uma foto prova
/// o dia de hoje; ela nao prova NADA sobre o dia em que alguem puser um `PopupCentered` de volta --
/// e o defeito de origem era invisivel em foto parada: ele so aparece **depois de trocar de
/// resolucao com a caixa ABERTA**, que e exatamente o caminho que o dono descreveu.
///
/// Entao a bancada mede DOIS numeros, e os dois em pixels de tela:
///   * **a distancia do centro** -- `|centro do painel - centro do viewport|`, que tem que ser ZERO
///     em toda resolucao E depois de mudar de resolucao com a caixa aberta;
///   * **a area de sobreposicao** -- todo par de elementos da coluna, `Rect2.Intersection().Area`,
///     que tem que ser ZERO. Era isto que a foto do dono mostrava: o nome digitado por cima do
///     texto de aviso (o `AcceptDialog` da a TODO filho Control o retangulo inteiro da area de
///     conteudo, entao o campo nascia em cima do Label do `DialogText`).
/// ==========================================================================================
///
/// ============================ ELA MEDE A TELA DE VERDADE ============================
/// Nada aqui e reconstruido: a bancada sobe o servidor, LOGA, cria dois personagens pelo fio, monta
/// o `CharacterSelect` de producao com a lista de slots que o servidor mandou, e **aperta o botao
/// "Excluir personagem" do slot** -- o mesmo `Pressed` que o dedo do dono dispara. O que ela mede
/// depois sao os nodes que aquele clique criou.
///
/// A unica coisa que ela nao atravessa sao as 12 linhas do `Boot.AoReceberSlots` que penduram os
/// eventos da tela (Jogar/Criar/Sair) -- ela as repete, porque o caminho normal do `Boot` monta a
/// tela de login e conecta com a conta do jogador.
/// ==============================================================================
///
/// ============================ A TRAVA DO NOME TEM DUAS CAMADAS, E ELAS SAO MEDIDAS SEPARADAS ============================
/// A tela so acende o "Excluir" quando o texto BATE, e o servidor confere de novo
/// (`GameServer.DeleteChar:3286`). Sao coisas diferentes e a bancada nao as confunde: ela poe um
/// nome errado, confere que o botao continua APAGADO e que nada saiu no fio; depois **acende o
/// botao na marra** e aperta -- e o personagem tem que continuar vivo, porque a trava nao e a cor do
/// botao. E o nome CERTO apaga de verdade, que e o que impede as duas familias de passarem verdes
/// num caminho morto.
/// ================================================================================================
///
/// COMO RODAR (janela no SEGUNDO monitor, que e de onde saem as fotos):
///     Godot --path . --diagapagar --position 1920,0 --resolution 1280x720 --rede 7902
/// Sem janela nao ha foto (o `GetImage` volta vazio no headless), mas as medidas continuam saindo.
/// </summary>
public partial class RoboDaTelaDeApagar : Node
{
	private const string Conta = "diagapagar";
	private const string Senha = "diagapagar";
	private const string NomeA = "Sacrificavel";
	private const string NomeB = "Testemunha";

	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// PLACAR -- a mesma disciplina da `--diagmudez`: dois placares separados
	// =====================================================================
	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];

	/// <summary>Verde na rodada real e "nao viu defeito"; vermelho com o defeito na frente e "sabe olhar".</summary>
	private int _injOk, _injFalha;
	private readonly List<string> _injPassouBatido = [];

	private readonly List<string> _fotos = [];

	private static void Nota(string linha) => GD.Print("[apagar] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque
			 + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	// =====================================================================
	// O FIO -- o que o servidor RECEBEU, e nao o que o cliente pretendia
	// =====================================================================
	private readonly List<byte[]> _fio = [];
	private readonly object _trava = new();

	private void Marcar() { lock (_trava) _fio.Clear(); }

	private bool Saiu(Protocol.C2S op)
	{
		lock (_trava) return _fio.Any(p => p.Length > 0 && p[0] == (byte)op);
	}

	// =====================================================================
	// ESTADO DA CONEXAO
	// =====================================================================
	private List<SlotInfo> _slots = [];
	private bool _naSelecao;
	private readonly List<string> _recusas = [];
	private CharacterSelect? _tela;

	public override void _Ready()
	{
		if (Jandirus.Server.GameServer.Instance is { } srv && !srv.Running && !srv.Start(Porta()))
		{
			GD.PushError("[apagar] nao consegui abrir a porta -- ha outra bancada viva?");
			return;
		}
		Jandirus.Server.GameServer.EspiaoDeEntrada = bytes => { lock (_trava) _fio.Add(bytes); };

		if (C is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Rejected += motivo => _recusas.Add(motivo);

		_roteiro = Roteiro().GetEnumerator();
	}

	public override void _ExitTree()
	{
		Jandirus.Server.GameServer.EspiaoDeEntrada = null;
		if (C is { } cli) cli.SlotsRecebidos -= AoReceberSlots;
	}

	private static int Porta()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--rede");
		return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int p) && p > 0
			? p : Protocol.DefaultPort;
	}

	private void AoReceberSlots(List<SlotInfo> slots)
	{
		_slots = slots;
		_naSelecao = true;
		// A TELA SE REFAZ COM A LISTA NOVA, como o `Boot` faz: apagar um personagem manda a lista de
		// volta (`MandarSlots`, no fim do `DeleteChar`), e uma tela que nao se refaz mostraria um
		// slot que ja nao existe.
		_tela?.Mostrar(slots);
	}

	private bool Tem(string nome) => _slots.Any(s => s.Ocupado && s.Nome == nome);
	private int Ocupados() => _slots.Count(s => s.Ocupado);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerator<double>? _roteiro;
	private double _espera = 0.5;

	public override void _Process(double delta)
	{
		if (_roteiro == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; return; }
		_espera = _roteiro.Current;
	}

	private IEnumerable<double> Roteiro()
	{
		Nota("=================== A TELA DE EXCLUIR PERSONAGEM ===================");

		foreach (double d in Conectar()) yield return d;
		if (!_naSelecao) { foreach (double d in Fechamento()) yield return d; yield break; }

		foreach (double d in PreparaAConta()) yield return d;
		foreach (double d in MontarATela()) yield return d;

		foreach (double d in F1_SempreNoCentro()) yield return d;
		foreach (double d in F2_NadaSeSobrepoe()) yield return d;
		foreach (double d in F3_ATravaDoNome()) yield return d;
		foreach (double d in F4_AsInjecoesDeLayout()) yield return d;

		foreach (double d in Fechamento()) yield return d;
	}

	private IEnumerable<double> Conectar()
	{
		_naSelecao = false;
		C?.Conectar("127.0.0.1", Porta(), Conta, Senha);
		for (int i = 0; i < 300 && !_naSelecao; i++) yield return 0.05;
		Checa("a conta abriu e a lista de slots chegou", _naSelecao);
	}

	/// <summary>
	/// DOIS PERSONAGENS, e nao um. Um laco errado no servidor limparia a conta inteira e uma conta de
	/// um personagem so nunca notaria -- e a mesma razao da `--diagslot`. Aqui ha um segundo motivo:
	/// a tela precisa de um slot que NAO se mexe enquanto o outro e apagado.
	///
	/// CRIAR ENTRA NO MUNDO (achado da `--diagslot`): o `CreateChar` cria E entra na mesma chamada.
	/// Por isso cada criacao termina em desconectar e voltar -- e de quebra a volta prova que o que
	/// se apagou depois foi pro DISCO.
	/// </summary>
	private IEnumerable<double> PreparaAConta()
	{
		// TERRENO LIMPO: a conta e reusada entre rodadas.
		for (int i = 0; i < _slots.Count; i++)
			if (_slots[i].Ocupado) C?.SendDeleteChar(i, _slots[i].Nome);
		yield return 0.8;
		Checa("a conta comeca vazia", Ocupados() == 0, $"{Ocupados()} ocupado(s)");

		foreach (double d in CriarEVoltar(0, NomeA)) yield return d;
		foreach (double d in CriarEVoltar(1, NomeB)) yield return d;

		Checa($"os dois personagens existem ('{NomeA}' e '{NomeB}')", Tem(NomeA) && Tem(NomeB),
			  $"{Ocupados()} ocupado(s)");
	}

	private IEnumerable<double> CriarEVoltar(int slot, string nome)
	{
		var ficha = new Jandirus.Core.Races.CharacterDraft
		{
			Name = nome, Race = "Human", Planet = "Earth", Gender = "Male",
			Backstory = "personagem de bancada, criado pra ser apagado.",
			Porte = "Medium",
		};
		C?.CriarPersonagem(slot, ficha, new Jandirus.Core.Appearance.Appearance());
		yield return 1.2;   // criar ENTRA no mundo -- nao se desconecta no mesmo quadro

		_naSelecao = false;
		C?.Desconectar();
		yield return 0.8;
		foreach (double d in Conectar()) yield return d;
	}

	/// <summary>
	/// A TELA DE PRODUCAO, montada como o `Boot` a monta. O `CharacterSelect` e um `CanvasLayer` e
	/// vive na raiz -- e onde o jogo o poe.
	/// </summary>
	private IEnumerable<double> MontarATela()
	{
		_tela = new CharacterSelect { Name = "SelecaoDaBancada" };
		GetTree().Root.AddChild(_tela);
		_tela.Mostrar(_slots);
		yield return 0.5;

		Checa("a tela de selecao montou com os tres slots",
			  BotaoDeApagar(0) != null, $"{_slots.Count} slot(s)");
	}

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	/// <summary>O botao "Excluir personagem" do slot pedido -- o que o dedo do dono aperta.</summary>
	private Button? BotaoDeApagar(int slot)
	{
		if (_tela == null) return null;
		List<Button> todos = [.. Todos<Button>(_tela).Where(b => b.Text == "Excluir personagem")];
		return slot < todos.Count ? todos[slot] : null;
	}

	/// <summary>
	/// A CAIXA DE PERGUNTA que o clique criou. Ela e o `ColorRect` escuro (o modal) que tem um
	/// `CenterContainer` dentro -- procurada pela FORMA e nao por nome, pra bancada nao virar refem
	/// de um `Name` que a tela nao usa.
	/// </summary>
	private Control? Pergunta() =>
		_tela == null ? null
		: _tela.GetChildren().OfType<ColorRect>()
			   .FirstOrDefault(c => c.GetChildren().OfType<CenterContainer>().Any());

	private PanelContainer? Painel() =>
		Pergunta()?.GetChildren().OfType<CenterContainer>().FirstOrDefault()
				 ?.GetChildren().OfType<PanelContainer>().FirstOrDefault();

	private VBoxContainer? Coluna() => Painel() is { } p ? Todos<VBoxContainer>(p).FirstOrDefault() : null;

	private LineEdit? Campo() => Painel() is { } p ? Todos<LineEdit>(p).FirstOrDefault() : null;

	private Button? BotaoExcluir() =>
		Painel() is { } p ? Todos<Button>(p).FirstOrDefault(b => b.Text == "Excluir") : null;

	private Button? BotaoCancelar() =>
		Painel() is { } p ? Todos<Button>(p).FirstOrDefault(b => b.Text == "Cancelar") : null;

	private Vector2 Viewport() => GetViewport().GetVisibleRect().Size;

	/// <summary>
	/// A MEDIDA DO PEDIDO: quanto o centro da caixa esta longe do centro da tela. O dono pediu
	/// *"SEMPRE FIXO NO CENTRO"*, e "sempre" e o que esta medida cobra em cada resolucao.
	/// </summary>
	private float DesvioDoCentro()
	{
		if (Painel() is not { } p) return float.NaN;
		return p.GetGlobalRect().GetCenter().DistanceTo(Viewport() / 2f);
	}

	/// <summary>
	/// OS ELEMENTOS QUE O OLHO VE, um por um: os filhos da coluna, com a linha de botoes ABERTA nos
	/// dois botoes. Sao estes retangulos que a foto do dono mostrava empilhados uns nos outros.
	/// </summary>
	private List<(string Nome, Rect2 Caixa)> Pecas()
	{
		List<(string, Rect2)> saida = [];
		if (Coluna() is not { } col) return saida;

		foreach (Node filho in col.GetChildren())
		{
			if (filho is not Control c || !c.Visible) continue;
			if (filho is HBoxContainer linha)
			{
				foreach (Node b in linha.GetChildren())
					if (b is Control cb && cb.Visible) saida.Add((Rotulo(cb), cb.GetGlobalRect()));
				continue;
			}
			saida.Add((Rotulo(c), c.GetGlobalRect()));
		}
		return saida;
	}

	private static string Rotulo(Control c) => c switch
	{
		Button b => $"botao \"{b.Text}\"",
		Label l => $"texto \"{Curto(l.Text)}\"",
		LineEdit => "campo de digitar",
		HSeparator => "risco",
		_ => c.GetType().Name,
	};

	private static string Curto(string t) => t.Length <= 22 ? t : t[..22] + "...";

	/// <summary>A pior sobreposicao entre dois elementos, em px². Tem que ser ZERO.</summary>
	private (float Area, string Quem) PiorSobreposicao()
	{
		List<(string Nome, Rect2 Caixa)> p = Pecas();
		float pior = 0;
		string quem = "";
		for (int i = 0; i < p.Count; i++)
			for (int j = i + 1; j < p.Count; j++)
			{
				Rect2 cruz = p[i].Caixa.Intersection(p[j].Caixa);
				float area = cruz.Size.X * cruz.Size.Y;
				if (area <= pior) continue;
				pior = area;
				quem = $"{p[i].Nome} x {p[j].Nome}";
			}
		return (pior, quem);
	}

	// =====================================================================
	// ABRIR E FECHAR PELA TELA, e nunca por dentro
	// =====================================================================
	private IEnumerable<double> Abrir(int slot)
	{
		BotaoDeApagar(slot)?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.3;
	}

	private IEnumerable<double> Fechar()
	{
		BotaoCancelar()?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.3;
	}

	/// <summary>
	/// UM TOQUE DE TECLA DE VERDADE, injetado no motor. As duas metades preenchidas
	/// (`Keycode` e `PhysicalKeycode`) porque um teclado de verdade manda as duas -- a mesma nota da
	/// `--diagmudez`.
	/// </summary>
	private static void Toque(Key k)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
	}

	// =====================================================================
	// TROCAR DE RESOLUCAO -- o caminho que o dono descreveu
	// =====================================================================
	private IEnumerable<double> Janela(int l, int a)
	{
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		DisplayServer.WindowSetSize(new Vector2I(l, a));
		GetTree().Root.Size = new Vector2I(l, a);
		yield return 0.6;
	}

	private IEnumerable<double> TelaCheia()
	{
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		yield return 0.8;
	}

	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { Nota("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_fotos.Add(caminho);
			Nota("  foto   " + caminho);
		}
		catch (Exception e) { Nota("  --     sem foto: " + e.Message); }
	}

	// =====================================================================
	// F1 -- SEMPRE NO CENTRO
	// =====================================================================
	/// <summary>
	/// AS QUATRO PARADAS, e a terceira e o pedido inteiro: a caixa e aberta em JANELA, a tela vai pra
	/// CHEIA **com ela aberta**, e volta pra janela **sem fechar**. Era ai que o
	/// `PopupCentered` deixava a caixa 320 x 180 px fora do centro -- ele centra UMA VEZ, na abertura,
	/// e guarda pixels absolutos.
	///
	/// A quarta parada (800x600) nao e capricho: numa tela pequena o que estoura nao e a posicao, e o
	/// TAMANHO -- por isso ali tambem se cobra que a caixa caiba INTEIRA no viewport.
	/// </summary>
	private IEnumerable<double> F1_SempreNoCentro()
	{
		Nota("--- F1: a caixa fica no centro em qualquer resolucao, e ao TROCAR de resolucao ---");

		// ---- 1) janela 1280x720, aberta ali ----
		foreach (double d in Janela(1280, 720)) yield return d;
		foreach (double d in Abrir(0)) yield return d;
		Checa("o clique em \"Excluir personagem\" abriu a caixa", Pergunta() != null);
		if (Pergunta() == null) yield break;
		MedirCentro($"JANELA {Vp()}");
		Fotografar("user://apagar-1-janela.png");

		// ---- 2) TELA CHEIA com a caixa ABERTA ----
		foreach (double d in TelaCheia()) yield return d;
		MedirCentro($"TELA CHEIA {Vp()} (a caixa foi aberta em janela e NAO foi fechada)");
		Fotografar("user://apagar-2-tela-cheia.png");

		// ---- 3) DE VOLTA PRA JANELA, ainda aberta: o caminho do dono ----
		foreach (double d in Janela(1280, 720)) yield return d;
		MedirCentro($"CHEIA -> JANELA {Vp()} (o caminho que o dono descreveu)");
		Fotografar("user://apagar-3-cheia-para-janela.png");

		// ---- 4) janela pequena ----
		foreach (double d in Janela(800, 600)) yield return d;
		MedirCentro($"JANELA PEQUENA {Vp()}");
		Fotografar("user://apagar-4-pequena.png");

		if (Painel() is { } p)
			Checa($"...e a caixa cabe INTEIRA na tela pequena {Vp()}",
				  new Rect2(Vector2.Zero, Viewport()).Encloses(p.GetGlobalRect()),
				  $"caixa {p.GetGlobalRect().Size}");

		foreach (double d in Janela(1280, 720)) yield return d;
	}

	private string Vp() => $"{Viewport().X:0}x{Viewport().Y:0}";

	private void MedirCentro(string onde)
	{
		float desvio = DesvioDoCentro();
		// UM PIXEL DE FOLGA e o arredondamento de um painel de largura impar num viewport par --
		// nao e "quase centrado": e o mesmo pixel que o `CenterContainer` arredonda.
		Checa($"{onde}: a caixa esta no centro", desvio <= 1.0f, $"desvio {desvio:0.##} px");
	}

	// =====================================================================
	// F2 -- NADA SE SOBREPOE
	// =====================================================================
	/// <summary>
	/// A OUTRA METADE DA FOTO DO DONO: *"todo bagunçado"*, com o titulo, o aviso e o campo de digitar
	/// um em cima do outro e o nome do personagem escrito por cima do texto.
	///
	/// A medida e crua de proposito -- area de interseccao entre TODO par de elementos da coluna, em
	/// px². Ela nao sabe o que e titulo e o que e botao, e por isso nao envelhece quando a caixa
	/// ganhar mais uma linha.
	/// </summary>
	private IEnumerable<double> F2_NadaSeSobrepoe()
	{
		Nota("--- F2: nada se sobrepoe, em nenhuma das resolucoes ---");

		foreach ((int l, int a) in new[] { (1280, 720), (1920, 1080), (800, 600) })
		{
			foreach (double d in Janela(l, a)) yield return d;
			(float area, string quem) = PiorSobreposicao();
			Checa($"{Vp()}: nenhum elemento cruza outro ({Pecas().Count} elementos medidos)",
				  area <= 0.5f, area > 0.5f ? $"{area:0} px² em {quem}" : "0 px²");
		}

		foreach (double d in Janela(1280, 720)) yield return d;

		// A ORDEM DE CIMA PRA BAIXO tambem e o pedido ("melhore o layout"): titulo, nome, aviso,
		// rotulo, campo, botoes. Se dois elementos trocarem de lugar a caixa continua sem
		// sobreposicao e mesmo assim volta a ficar bagunçada.
		List<(string Nome, Rect2 Caixa)> p = Pecas();
		bool emOrdem = true;
		for (int i = 1; i < p.Count; i++)
			if (p[i].Caixa.Position.Y + 0.5f < p[i - 1].Caixa.Position.Y) emOrdem = false;
		Checa("a coluna esta empilhada de cima pra baixo, na ordem em que foi escrita", emOrdem);

		Checa("o NOME do personagem tem linha propria (era ele que saia por cima do aviso)",
			  p.Any(x => x.Nome.Contains(NomeA)), string.Join(" | ", p.Select(x => x.Nome)));
	}

	// =====================================================================
	// F3 -- A TRAVA DO NOME
	// =====================================================================
	/// <summary>
	/// A FAMILIA QUE JUSTIFICA A BANCADA numa tela que destroi: se a conferencia do nome deixar
	/// passar qualquer texto, ninguem descobre pelo caminho normal -- descobre no dia em que alguem
	/// perder meses de treino.
	///
	/// Sao TRES perguntas, e elas medem coisas diferentes:
	///   1. o botao continua APAGADO com o nome errado (a tela);
	///   2. o botao ACESO NA MARRA nao apaga nada (a tela de novo, mas por dentro: o `Apagar()`
	///      reconfere) -- e mesmo que passasse, o servidor recusa;
	///   3. o nome CERTO apaga de verdade, e SO o slot pedido.
	/// A terceira e o que impede as duas primeiras de ficarem verdes num caminho morto.
	/// </summary>
	private IEnumerable<double> F3_ATravaDoNome()
	{
		Nota("--- F3: excluir exige o nome certo ---");

		// ============================ AS DUAS SAIDAS DA CAIXA, ANTES DE MEDIR A TRAVA ============================
		// Elas eram DE GRACA no `ConfirmationDialog` do Godot e passaram a ser nossas quando ele saiu --
		// e uma caixa modal sem saida numa tela que destroi e pior do que a caixa torta.
		//   * o FUNDO ESCURO tem que COMER O CLIQUE, senao da pra apertar "Jogar" num slot atras da
		//     pergunta de apagar OUTRO personagem (e o que substituiu a subjanela);
		//   * o ESC tem que desistir (`_UnhandledKeyInput`, que roda antes de qualquer
		//     `_UnhandledInput` -- por isso ele nao briga com o menu de pause).
		// =========================================================================================================
		Checa("o fundo escuro COME o clique (a caixa e modal sem subjanela)",
			  Pergunta()?.MouseFilter == Control.MouseFilterEnum.Stop,
			  Pergunta()?.MouseFilter.ToString() ?? "sem caixa");

		// ============================ O ESC E MEDIDO **COM O CAMPO EM FOCO** ============================
		// E o estado em que esta caixa SEMPRE esta -- ela abre com `campo.GrabFocus()`. E foi
		// exatamente aqui que a bancada achou um defeito de verdade: com o `_UnhandledKeyInput`, o ESC
		// fechava a caixa **sem** o campo em foco e **nao** fechava com ele (`[com o campo em
		// foco=False, sem foco=True]`), porque um `LineEdit` com foco recebe a tecla pelo `gui_input` e
		// o evento nunca chega ao `_unhandled_*`. Medir com o foco solto teria passado VERDE numa tela
		// em que o ESC nao funcionava. Ver `CharacterSelect._Input`.
		// ==================================================================================================
		Checa("PRECONDICAO do ESC: o campo esta em foco, como ele fica sempre que a caixa abre",
			  Campo()?.HasFocus() == true);

		Toque(Key.Escape);
		yield return 0.3;
		Checa("ESC desiste de apagar e fecha a caixa (com o campo em foco)", Pergunta() == null);

		foreach (double d in Abrir(0)) yield return d;
		Checa("...e ela reabre depois (o ESC nao deixou node solto na arvore)", Pergunta() != null);

		if (Campo() is not { } campo || BotaoExcluir() is not { } excluir)
		{
			Checa("a caixa tem campo de digitar e botao de excluir", false);
			yield break;
		}

		Checa("o botao \"Excluir\" NASCE apagado (campo vazio)", excluir.Disabled);

		// ---- nome errado ----
		Digitar(campo, "Sacrificave");   // falta uma letra
		yield return 0.2;
		Checa("com o nome quase certo o botao continua APAGADO", excluir.Disabled, campo.Text);

		Marcar();
		_recusas.Clear();
		campo.EmitSignal(LineEdit.SignalName.TextSubmitted, campo.Text);   // ENTER
		yield return 0.4;
		Checa("...e o ENTER com o nome errado nao manda nada no fio", !Saiu(Protocol.C2S.DeleteChar));
		Checa("...e o personagem continua na lista", Tem(NomeA));

		// ---- o botao aceso na marra: a trava nao e a cor do botao ----
		Marcar();
		excluir.Disabled = false;
		excluir.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.4;
		Checa("com o botao ACESO NA MARRA e o nome errado, nada sai no fio (a trava nao e a cor "
			  + "do botao)", !Saiu(Protocol.C2S.DeleteChar));
		Checa("...e o personagem continua na lista", Tem(NomeA));

		// ---- e mesmo mandando o pacote na mao, o servidor recusa ----
		Marcar();
		_recusas.Clear();
		C?.SendDeleteChar(0, "nome que nao e o dele");
		yield return 0.5;
		Checa("mandando o pacote NA MAO com o nome errado, o servidor recusa e explica",
			  Tem(NomeA) && _recusas.Any(r => r.Contains("nao confere")),
			  _recusas.Count > 0 ? _recusas[0] : "nenhuma recusa");

		// ---- caixa alta e espaco em volta batem (a regra e `Trim` + `OrdinalIgnoreCase`) ----
		Digitar(campo, "  sacrificavel  ");
		yield return 0.2;
		Checa("o nome com espacos em volta e em minusculas ACENDE o botao (Trim + ignora caixa)",
			  !excluir.Disabled, $"\"{campo.Text}\"");

		// ---- o nome certo apaga ----
		Marcar();
		Digitar(campo, NomeA);
		yield return 0.2;
		Checa("o nome exato acende o botao", !excluir.Disabled);

		excluir.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.8;
		Checa($"...e apertar apaga: '{NomeA}' saiu da lista", !Tem(NomeA));

		// ============================ A INJECAO DESTA FAMILIA ============================
		// "O personagem continua na lista" e uma frase que fica verde sozinha num caminho morto -- um
		// botao que nao chama nada, um pacote que nao sai, uma conta que nunca muda. O defeito
		// injetado e o unico que faz a trava desaparecer sem mexer no codigo: **dar a ela o texto que
		// ela aceita**. O mesmo botao, o mesmo Enter, o mesmo pacote -- e o personagem morre.
		// ==============================================================================
		Injeta($"com o texto CERTO o MESMO caminho apaga -- entao \"'{NomeA}' continua na lista\" nao "
			   + "e uma frase que ficaria verde sozinha", !Tem(NomeA),
			   $"{Ocupados()} slot(s) ocupado(s) agora");
		Checa($"...e SO ele: '{NomeB}' continua intacto no outro slot", Tem(NomeB));
		Checa("...e a caixa se fechou junto com a resposta", Pergunta() == null);

		// ---- foi pro disco? ----
		_naSelecao = false;
		C?.Desconectar();
		yield return 0.8;
		foreach (double d in Conectar()) yield return d;
		Checa($"depois de RELOGAR, '{NomeA}' continua apagado (foi pro disco)", !Tem(NomeA));
		Checa($"...e '{NomeB}' voltou inteiro", Tem(NomeB));

		_tela?.Mostrar(_slots);
		yield return 0.3;
	}

	/// <summary>Digita como um teclado digita: o `TextChanged` e quem acende o botao.</summary>
	private static void Digitar(LineEdit campo, string texto)
	{
		campo.Text = texto;
		campo.EmitSignal(LineEdit.SignalName.TextChanged, texto);
	}

	// =====================================================================
	// F4 -- AS INJECOES
	// =====================================================================
	/// <summary>
	/// OS DOIS DEFEITOS DE ORIGEM, REPOSTOS NOS NODES DE VERDADE -- e nao numa copia deles. A bancada
	/// mede depois exatamente com as mesmas duas contas de antes; se alguma delas continuar verde com
	/// o defeito na frente, ela nao sabe olhar e as familias F1 e F2 nao valem nada.
	///
	///   * INJECAO 1 -- **o `PopupCentered`**: a caixa deixa de ser ancorada e passa a ter o tamanho
	///     da abertura congelado em pixels. Trocar de resolucao depois disso e o defeito do dono,
	///     literal: `(viewport_da_abertura - viewport_atual) / 2`.
	///   * INJECAO 2 -- **o retangulo inteiro pra todo filho** (o que o `AcceptDialog` fazia): o campo
	///     de digitar e reparentado pra fora da coluna e posto EXATAMENTE em cima do texto de aviso.
	///     E a foto do dono, reconstruida.
	/// </summary>
	private IEnumerable<double> F4_AsInjecoesDeLayout()
	{
		Nota("--- F4: os defeitos de origem, repostos nos nodes de verdade ---");

		foreach (double d in Janela(1920, 1080)) yield return d;
		foreach (double d in Abrir(0)) yield return d;
		if (Pergunta() is not { } fundo || Painel() is not { } painel)
		{
			Checa("havia uma caixa aberta pra injetar o defeito", false);
			yield break;
		}

		float limpo = DesvioDoCentro();

		// ---- INJECAO 1: a caixa deixa de ser ancorada ----
		if (fundo.GetChildren().OfType<CenterContainer>().FirstOrDefault() is { } centro)
		{
			Vector2 naAbertura = Viewport();
			centro.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			centro.Size = naAbertura;
			yield return 0.3;
			float aindaCentrado = DesvioDoCentro();

			foreach (double d in Janela(1280, 720)) yield return d;
			float depois = DesvioDoCentro();

			Injeta("com a caixa DESANCORADA (o `PopupCentered` de volta), trocar de resolucao a "
				   + "tira do centro", depois > 1.0f,
				   $"limpo {limpo:0.#} px -> na abertura {aindaCentrado:0.#} px -> "
				   + $"depois de voltar pra janela {depois:0.#} px");
			Fotografar("user://apagar-5-INJETADO-fora-do-centro.png");
		}

		// ---- INJECAO 2: o campo por cima do aviso ----
		if (Coluna() is { } col && Campo() is { } campo)
		{
			Label? aviso = col.GetChildren().OfType<Label>()
							  .FirstOrDefault(l => l.AutowrapMode != TextServer.AutowrapMode.Off);
			if (aviso != null)
			{
				(float antes, _) = PiorSobreposicao();
				Rect2 alvo = aviso.GetGlobalRect();
				campo.GetParent().RemoveChild(campo);
				fundo.AddChild(campo);       // um ColorRect nao e container: ele nao re-arruma o filho
				campo.GlobalPosition = alvo.Position;
				campo.Size = alvo.Size;
				yield return 0.3;

				Rect2 cruz = campo.GetGlobalRect().Intersection(aviso.GetGlobalRect());
				float area = cruz.Size.X * cruz.Size.Y;
				Injeta("com o campo de digitar POR CIMA do aviso (o que o `AcceptDialog` fazia), a "
					   + "medida de sobreposicao acusa", area > 0.5f,
					   $"antes {antes:0} px² -> depois {area:0} px²");
				Fotografar("user://apagar-6-INJETADO-sobreposto.png");
			}
		}

		// A CAIXA INJETADA MORRE AQUI -- ela esta com os nodes fora do lugar de proposito.
		fundo.QueueFree();
		yield return 0.3;
		foreach (double d in Janela(1280, 720)) yield return d;
	}

	// =====================================================================
	// FECHAMENTO
	// =====================================================================
	private IEnumerable<double> Fechamento()
	{
		Nota("==================================================================");
		Nota($"PLACAR: {_ok} OK, {_falha} FALHA");
		if (_falha > 0) foreach (string f in _reprovadas) Nota($"   falhou: {f}");
		Nota($"INJECAO: {_injOk} pegou, {_injFalha} PASSOU BATIDO");
		if (_injFalha > 0) foreach (string f in _injPassouBatido) Nota($"   passou batido: {f}");
		if (_fotos.Count > 0) { Nota("FOTOS:"); foreach (string f in _fotos) Nota("   " + f); }
		Nota("==================================================================");
		yield return 0.5;

		GetTree()?.Quit();
	}
}
