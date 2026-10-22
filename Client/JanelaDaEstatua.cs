using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Magic;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-22) **O MENU DA ESTATUA DO DRAGAO** -- o que abre no "Erguer Estátua do Dragão" da aba World e no "Configurar o dragão" da
/// janela de desejos (so pra quem ergueu a estatua daquele mundo). O dono: *"e ao criar um set de esferas crie um menu bem intuitivo e bem
/// feito pro jogador q criou o set escolher os desejos, a quantidade, a força etc"*. As regras moram em `Core/Magic/ConfigDaEstatua.cs`.
///
/// QUATRO BLOCOS, de cima pra baixo, cada um dizendo o que muda quando se mexe nele:
///   1. O NOME do dragao -- um campo de texto; na Terra e em Namek o nome e fixo (o Shenlong e o Porunga), e o bloco diz por que;
///   2. Os PEDIDOS por invocacao -- tres botoes, e embaixo a forca de cada pedido (100%, 50%, 33%);
///   3. Os DESEJOS -- uma caixa por desejo, e embaixo de cada uma o que ele da COM AQUELA FORCA (a mesma frase da janela de desejos, pela
///      mesma funcao: `Desejos.DescricaoNoDragao`). Os que nao cabem nos pedidos escolhidos ficam apagados, com o porque;
///   4. Ao erguer, o DESEJO SUPREMO -- gravar ou nao, e em que moeda (com o saldo do jogador ao lado). Ao refazer, so diz se ele esta la:
///      ele so se grava ao erguer, como no DM.
/// O pe tem o resumo, o motivo de nao poder confirmar (quando ha) e os dois botoes.
///
/// ELA NAO DECIDE NADA: monta a configuracao e a manda no verbo de producao -- `db_estatua` ao erguer, `db_refazer` ao refazer --, e quem
/// confere tudo de novo e o servidor (`GameServer.Esferas.cs`). A resposta dele vem no chat.
///
/// NO MEIO DA TELA, POR CIMA DO JOGO: e um gesto que se faz parado, ao lado da estatua (ou de onde ela vai subir) -- ao contrario da janela
/// de desejos, que mora na direita porque o dragao esta no meio. Nenhum botao dela pega o foco do teclado (ENTER e a tecla de falar); o campo
/// do nome sim, e enquanto ele o tem o resto do jogo sabe que alguem esta digitando (<see cref="Digitando"/>).
/// </summary>
public partial class JanelaDaEstatua : CanvasLayer
{
	/// <summary>A janela viva, ou nula antes do mundo. E por aqui que a aba World, a janela de desejos e o robo a alcancam.</summary>
	public static JanelaDaEstatua? Instancia { get; private set; }

	/// <summary>O nome do dragao esta sendo digitado? Lido por `Foco.Digitando` -- sem isto o "E" do nome abriria o menu da tecla E.</summary>
	public static bool Digitando => Instancia is { } j && j.NaTela && IsInstanceValid(j._nome) && j._nome.HasFocus();

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagestatua`): o menu mostra cada desejo com a forca INTEIRA do set, quantos pedidos houver -- o numero
	/// que mente sobre a forca dividida. Lido pela linha do fator em <see cref="Atualizar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ForcaInteiraNaTelaDeTeste;

	// (2026-10-22, r2) DUAS COLUNAS: a da esquerda com o nome, os pedidos, o supremo e o resumo; a da direita com a LISTA dos desejos na altura
	// inteira. Numa coluna so (a r1) a lista ficava com duas linhas a vista numa tela de 720 -- e ela e a parte que o criador mais olha.
	private const int Largura = 900;
	private const int LarguraDaEsquerda = 320;
	private const int Margem = 24;

	private enum Modo { Erguer, Refazer }

	private Control _raiz = null!;
	private PanelContainer _painel = null!;
	private Label _titulo = null!, _subtitulo = null!, _nomeFixo = null!, _avisoDoNome = null!, _forca = null!, _notaDosPedidos = null!;
	private Label _resumo = null!, _motivo = null!, _avisoDoRefazer = null!, _saldo = null!, _supremoNoSet = null!, _contagem = null!;
	private LineEdit _nome = null!;
	private readonly Button[] _pedidos = new Button[3];
	private VBoxContainer _desejos = null!, _blocoDoSupremo = null!;
	private CheckBox _supremo = null!;
	private Button _emZeni = null!, _emRecursos = null!, _confirmar = null!;

	private Modo _modo;
	private string _planeta = "";
	private ConfigDaEstatua _cfg = new();
	private bool _supremoGravado;

	/// <summary>Uma linha por desejo ajustavel: a caixa, a frase do que ele da e a nota de quem pode pedir.</summary>
	private readonly Dictionary<string, (CheckBox Caixa, Label Desc, Label Nota, PanelContainer Cartao)> _linhas = new(StringComparer.Ordinal);

	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>O que o pe da janela diz agora (o resumo e o motivo) -- o robo `--diagestatua` le por aqui.</summary>
	public string ResumoDesenhado => IsInstanceValid(_resumo) ? _resumo.Text + " | " + _motivo.Text : "";

	/// <summary>A frase de um desejo como esta desenhada agora (vazio = nao ha a linha).</summary>
	public string DescricaoDesenhada(string id) => _linhas.TryGetValue(id, out var l) ? l.Desc.Text : "";

	/// <summary>A configuracao que a janela mandaria se o botao fosse apertado agora.</summary>
	public string ConfigDesenhada => _cfg.Codificar();

	public override void _Ready()
	{
		Layer = 5;   // por cima da janela de desejos (4): o "Configurar o dragão" abre a partir dela
		Instancia = this;
		Montar();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	/// <summary>O "Erguer Estátua do Dragão" da aba World: a configuracao de nascimento (1 pedido, todos os desejos, sem o supremo).</summary>
	public void AbrirParaErguer(string planeta, string nomeDoLugar)
	{
		_modo = Modo.Erguer;
		_planeta = planeta;
		_cfg = new ConfigDaEstatua { Pedidos = 1 };
		_supremoGravado = false;
		Abrir(nomeDoLugar);
	}

	/// <summary>O "Configurar o dragão" da janela de desejos: a configuracao de agora, que o servidor mandou na linha.</summary>
	public void AbrirParaRefazer(string planeta, string nomeDoLugar, ConfigDaEstatua atual)
	{
		_modo = Modo.Refazer;
		_planeta = planeta;
		_cfg = new ConfigDaEstatua
		{
			Nome = ConfigDaEstatua.NomeFixoEm(planeta) ? "" : atual.Nome,
			Pedidos = Math.Clamp(atual.Pedidos, 1, 3),
			Desligados = new HashSet<string>(atual.Desligados, StringComparer.Ordinal),
		};
		_supremoGravado = atual.SupremoGravado;
		Abrir(nomeDoLugar);
	}

	private void Abrir(string nomeDoLugar)
	{
		// O MENU DO P SAI DA FRENTE: os dois moram no meio da tela, e quem apertou "Erguer" la dentro ja fez o que tinha pra fazer nele.
		if (MenuJogo.NaFrenteDoJogo) MenuJogo.Instancia?.Fechar();

		bool erguer = _modo == Modo.Erguer;
		_titulo.Text = "ESTÁTUA DO DRAGÃO";
		_subtitulo.Text = erguer ? $"Erguer em {nomeDoLugar}" : $"Configurar o dragão de {nomeDoLugar}";
		_confirmar.Text = erguer ? "Erguer a estátua" : "Refazer o set";
		_confirmar.TooltipText = erguer
			? "a estátua sobe aqui e as sete esferas se espalham pelo mundo"
			: "as sete se espalham de novo e voltam a dormir até acordar; a força passa a ser o seu BP de agora";

		bool fixo = ConfigDaEstatua.NomeFixoEm(_planeta);
		_nome.Visible = !fixo;
		_nomeFixo.Visible = fixo;
		if (fixo)
			_nomeFixo.Text = string.Equals(_planeta, "Earth", StringComparison.OrdinalIgnoreCase)
				? $"{Esferas.NomeDoShenlong} — o dragão da Terra é sempre o Shenlong."
				: "PORUNGA — o dragão de Namek é sempre o Porunga.";
		else _nome.Text = _cfg.Nome;

		_blocoDoSupremo.Visible = erguer;
		_supremoNoSet.Visible = !erguer;
		_supremoNoSet.Text = _supremoGravado
			? "Desejo supremo: GRAVADO nestas esferas (O Mais Forte do Universo)."
			: "Desejo supremo: não gravado. Ele só se grava ao erguer a estátua.";
		_supremo.SetPressedNoSignal(false);
		_emZeni.SetPressedNoSignal(true);
		_emRecursos.SetPressedNoSignal(false);
		_avisoDoRefazer.Visible = !erguer;

		foreach ((string id, var l) in _linhas) l.Caixa.SetPressedNoSignal(!_cfg.Desligados.Contains(id));
		for (int i = 0; i < 3; i++) _pedidos[i].SetPressedNoSignal(i + 1 == _cfg.Pedidos);

		_raiz.Visible = true;
		Atualizar();
	}

	public void Fechar()
	{
		if (!NaTela) return;
		if (_nome.HasFocus()) _nome.ReleaseFocus();
		_raiz.Visible = false;
	}

	/// <summary>ESC fecha -- e, com o nome em foco, primeiro solta o campo (o jogador so queria parar de digitar).</summary>
	public override void _Input(InputEvent evento)
	{
		if (!NaTela) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;
		if (_nome.HasFocus()) _nome.ReleaseFocus();
		else Fechar();
		GetViewport().SetInputAsHandled();
	}

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	/// <summary>Um botao desta janela: so o mouse o aperta (ver o cabecalho).</summary>
	private static Button Botao(string texto, string nome = "") =>
		new() { Text = texto, Name = nome.Length > 0 ? nome : texto, FocusMode = Control.FocusModeEnum.None };

	private static Label Texto(string texto, int tamanho, Color cor, string nome = "")
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		if (nome.Length > 0) l.Name = nome;
		l.AddThemeFontSizeOverride("font_size", tamanho);
		l.AddThemeColorOverride("font_color", cor);
		return l;
	}

	private void Montar()
	{
		// A RAIZ NAO PEGA O MOUSE: fora do painel, a tela e do jogo.
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// NO MEIO, com a altura da tela: ancoras, e nao pixels -- ela se refaz sozinha num resize. O FUNDO E OPACO: a janela fica por cima
		// do chat, e o fundo com alfa dos paineis do HUD deixava as linhas dele vazarem por tras do texto dela.
		_painel = new PanelContainer { Name = "Painel" };
		_painel.AddThemeStyleboxOverride("panel", Tema.Caixa(new Color(Tema.Painel, 1f), Tema.Borda, 14));
		_painel.AnchorLeft = 0.5f;
		_painel.AnchorRight = 0.5f;
		_painel.AnchorTop = 0;
		_painel.AnchorBottom = 1;
		_painel.OffsetLeft = -Largura / 2f;
		_painel.OffsetRight = Largura / 2f;
		_painel.OffsetTop = Margem;
		_painel.OffsetBottom = -Margem;
		_painel.MouseFilter = Control.MouseFilterEnum.Stop;
		_raiz.AddChild(_painel);

		var tudo = new VBoxContainer();
		tudo.AddThemeConstantOverride("separation", 6);
		_painel.AddChild(tudo);

		_titulo = Texto("", 22, Tema.Destaque);
		_titulo.HorizontalAlignment = HorizontalAlignment.Center;
		tudo.AddChild(_titulo);
		_subtitulo = Texto("", 13, Tema.TextoFraco);
		_subtitulo.HorizontalAlignment = HorizontalAlignment.Center;
		tudo.AddChild(_subtitulo);
		tudo.AddChild(new HSeparator());

		// AS DUAS COLUNAS ocupam a altura que sobra; o pe (o motivo e os dois botoes) fica embaixo, fora delas.
		var lados = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		lados.AddThemeConstantOverride("separation", 12);
		tudo.AddChild(lados);
		var coluna = new VBoxContainer { Name = "Esquerda", CustomMinimumSize = new Vector2(LarguraDaEsquerda, 0) };
		coluna.AddThemeConstantOverride("separation", 6);
		lados.AddChild(coluna);
		lados.AddChild(new VSeparator());
		var direita = new VBoxContainer { Name = "Direita", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		direita.AddThemeConstantOverride("separation", 6);
		lados.AddChild(direita);

		// ---------------------------------------------------------- 1. o nome
		coluna.AddChild(Tema.Rotulo("o nome do dragão"));
		_nome = new LineEdit
		{
			Name = "Nome",
			MaxLength = ConfigDaEstatua.NomeMaximo,
			PlaceholderText = $"{ConfigDaEstatua.NomePadrao} (deixe vazio pro nome de sempre)",
			CustomMinimumSize = new Vector2(0, 34),
		};
		_nome.TextChanged += t => { _cfg.Nome = t; Atualizar(); };
		coluna.AddChild(_nome);
		_nomeFixo = Texto("", 14, Tema.Texto, "NomeFixo");
		coluna.AddChild(_nomeFixo);
		_avisoDoNome = Texto("", 12, Tema.Perigo, "AvisoDoNome");
		coluna.AddChild(_avisoDoNome);

		// ---------------------------------------------------------- 2. os pedidos
		coluna.AddChild(Tema.Rotulo("pedidos por invocação"));
		var linhaDosPedidos = new HBoxContainer();
		linhaDosPedidos.AddThemeConstantOverride("separation", 6);
		var grupo = new ButtonGroup();
		for (int i = 0; i < 3; i++)
		{
			int n = i + 1;
			Button b = Botao(n == 1 ? "1 pedido" : $"{n} pedidos", $"Pedidos{n}");
			b.ToggleMode = true;
			b.ButtonGroup = grupo;
			b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			b.Pressed += () => { _cfg.Pedidos = n; Atualizar(); };
			_pedidos[i] = b;
			linhaDosPedidos.AddChild(b);
		}
		coluna.AddChild(linhaDosPedidos);
		_forca = Texto("", 13, Tema.Nutricao, "Forca");
		coluna.AddChild(_forca);
		_notaDosPedidos = Texto("", 12, Tema.TextoFraco, "NotaDosPedidos");
		coluna.AddChild(_notaDosPedidos);

		// ---------------------------------------------------------- 3. os desejos
		var cabecaDosDesejos = new HBoxContainer();
		Label rotuloDosDesejos = Tema.Rotulo("os desejos que o seu dragão atende");
		rotuloDosDesejos.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		cabecaDosDesejos.AddChild(rotuloDosDesejos);
		_contagem = Texto("", 11, Tema.TextoFraco, "Contagem");
		_contagem.AutowrapMode = TextServer.AutowrapMode.Off;
		cabecaDosDesejos.AddChild(_contagem);
		Button todos = Botao("ligar todos", "LigarTodos");
		todos.AddThemeFontSizeOverride("font_size", 11);
		todos.Pressed += () =>
		{
			_cfg.Desligados.Clear();
			foreach (var l in _linhas.Values) l.Caixa.SetPressedNoSignal(true);
			Atualizar();
		};
		cabecaDosDesejos.AddChild(todos);
		direita.AddChild(cabecaDosDesejos);

		var rolagem = new ScrollContainer
		{
			Name = "Rolagem",
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		direita.AddChild(rolagem);
		_desejos = new VBoxContainer { Name = "Desejos", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_desejos.AddThemeConstantOverride("separation", 4);
		rolagem.AddChild(_desejos);
		foreach (DesejoDef d in ConfigDaEstatua.Ajustaveis) _desejos.AddChild(LinhaDoDesejo(d));

		// ---------------------------------------------------------- 4. o desejo supremo
		_blocoDoSupremo = new VBoxContainer { Name = "Supremo" };
		_blocoDoSupremo.AddThemeConstantOverride("separation", 3);
		_blocoDoSupremo.AddChild(Tema.Rotulo("o desejo supremo"));
		_supremo = new CheckBox
		{
			Name = "GravarSupremo",
			Text = "Gravar O MAIS FORTE DO UNIVERSO nestas esferas",
			FocusMode = Control.FocusModeEnum.None,
			TooltipText = "o dobro do maior poder do jogo -- e UM ANO de vida a quem pedir. Só se grava ao erguer.",
		};
		_supremo.Toggled += _ => Atualizar();
		_blocoDoSupremo.AddChild(_supremo);
		var moedas = new HBoxContainer();
		moedas.AddThemeConstantOverride("separation", 6);
		var grupoDaMoeda = new ButtonGroup();
		_emZeni = Botao(Recurso.PrecoEscrito(Desejos.PrecoDoSupremo, Moeda.Zeni), "SupremoEmZeni");
		_emRecursos = Botao(Recurso.PrecoEscrito(Desejos.PrecoDoSupremo, Moeda.Recursos), "SupremoEmRecursos");
		foreach (Button b in new[] { _emZeni, _emRecursos })
		{
			b.ToggleMode = true;
			b.ButtonGroup = grupoDaMoeda;
			b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			b.Pressed += Atualizar;
			moedas.AddChild(b);
		}
		_blocoDoSupremo.AddChild(moedas);
		_saldo = Texto("", 11, Tema.TextoFraco, "Saldo");
		_blocoDoSupremo.AddChild(_saldo);
		coluna.AddChild(_blocoDoSupremo);
		_supremoNoSet = Texto("", 12, Tema.TextoFraco, "SupremoNoSet");
		coluna.AddChild(_supremoNoSet);

		// ---------------------------------------------------------- o resumo, no pe da coluna da esquerda
		coluna.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
		coluna.AddChild(Tema.Rotulo("o seu dragão"));
		_resumo = Texto("", 14, Tema.Texto, "Resumo");
		coluna.AddChild(_resumo);
		_avisoDoRefazer = Texto("Refazer espalha as sete esferas de novo, e elas dormem até acordar. A força do set passa a ser o seu BP de agora.",
								12, Tema.Nutricao, "AvisoDoRefazer");
		coluna.AddChild(_avisoDoRefazer);

		// ---------------------------------------------------------- o pe, embaixo das duas colunas
		tudo.AddChild(new HSeparator());
		_motivo = Texto("", 12, Tema.Perigo, "Motivo");
		_motivo.HorizontalAlignment = HorizontalAlignment.Center;
		tudo.AddChild(_motivo);
		var pe = new HBoxContainer();
		pe.AddThemeConstantOverride("separation", 6);
		_confirmar = Botao("Erguer a estátua", "Confirmar");
		_confirmar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_confirmar.Pressed += Confirmar;
		pe.AddChild(_confirmar);
		Button cancelar = Botao("Cancelar (Esc)", "Cancelar");
		cancelar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		cancelar.Pressed += Fechar;
		pe.AddChild(cancelar);
		tudo.AddChild(pe);
	}

	/// <summary>UMA LINHA DE DESEJO: a caixa com o nome, o que ele da com a forca escolhida e, quando ha, quem pode pedir.</summary>
	private PanelContainer LinhaDoDesejo(DesejoDef d)
	{
		var conteudo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		conteudo.AddThemeConstantOverride("separation", 0);
		// (r2) A CAIXA SEM MOLDURA: o cartao ja e a moldura da linha, e a do botao em volta dela dobrava a altura de cada desejo.
		var caixa = new CheckBox
		{
			Name = "Desejo_" + d.Id, Text = d.Nome, FocusMode = Control.FocusModeEnum.None, ButtonPressed = true, Flat = true,
		};
		caixa.AddThemeFontSizeOverride("font_size", 14);
		string id = d.Id;
		caixa.Toggled += ligado =>
		{
			if (ligado) _cfg.Desligados.Remove(id);
			else _cfg.Desligados.Add(id);
			Atualizar();
		};
		conteudo.AddChild(caixa);
		Label desc = Texto("", 12, Tema.TextoFraco, "Desc_" + d.Id);
		conteudo.AddChild(desc);
		Label nota = Texto(NotaDeQuemPede(d), 11, Tema.Nutricao, "Nota_" + d.Id);
		nota.Visible = nota.Text.Length > 0;
		conteudo.AddChild(nota);

		var cartao = new PanelContainer { Name = "Linha_" + d.Id, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		cartao.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 6));
		cartao.AddChild(conteudo);
		_linhas[d.Id] = (caixa, desc, nota, cartao);
		return cartao;
	}

	/// <summary>Os portoes de QUEM PEDE (o set nao decide estes): o Kaioshin, o sangue Saiyajin. Vazio = qualquer um.</summary>
	private static string NotaDeQuemPede(DesejoDef d) =>
		d.PedeKaioshin ? "só o Kaioshin (da classe Golden Apple) consegue pedir este."
		: d.PedeSangueSaiyajin ? $"só quem tem {Desejos.SangueSaiyajinMinimo:0}% ou mais de sangue Saiyajin consegue pedir este."
		: "";

	// =====================================================================
	// O QUE MUDA A CADA CLIQUE
	// =====================================================================
	/// <summary>
	/// REFAZ OS NUMEROS: a forca de cada pedido, a frase de cada desejo com ela, os apagados pelos pedidos, o resumo e se da pra confirmar.
	/// Mexe so em texto e em estado de caixa -- a lista e montada uma vez, e uma lista que se remonta pisca embaixo do mouse.
	/// </summary>
	private void Atualizar()
	{
		if (!IsInstanceValid(_painel)) return;
		int pedidos = Math.Clamp(_cfg.Pedidos, 1, 3);
		double fator = ForcaInteiraNaTelaDeTeste ? 1 : ConfigDaEstatua.ForcaDeCadaPedido(pedidos);
		int marcos = Desejos.MarcosDoFator(fator);

		_forca.Text = pedidos == 1
			? "Um pedido com 100% da força do set -- a força é o seu BP na hora de erguer."
			: $"{pedidos} pedidos, cada um com {ConfigDaEstatua.PorcentagemDaForca(pedidos)} da força do set: a força é dividida entre eles.";
		_notaDosPedidos.Text = pedidos > 2
			? "Com 3 pedidos o dragão não atende Ressuscitar TODOS nem Matar alguém (só em dragões de até 2 pedidos)."
			: "Mais pedidos deixam cada um mais fraco: a lista ao lado já mostra quanto cada desejo dá.";

		int ligados = 0, total = 0;
		foreach (DesejoDef d in ConfigDaEstatua.Ajustaveis)
		{
			if (!_linhas.TryGetValue(d.Id, out var l)) continue;
			bool cabe = ConfigDaEstatua.CabeNosPedidos(d, pedidos);
			bool ligado = cabe && !_cfg.Desligados.Contains(d.Id);
			l.Caixa.Disabled = !cabe;
			l.Desc.Text = cabe ? Desejos.DescricaoNoDragao(d, fator, marcos, temCriador: true)
				: "Só em dragões de até 2 pedidos: com 3, ele sai da lista.";
			l.Caixa.AddThemeColorOverride("font_color", ligado ? Tema.Texto : Tema.TextoFraco);
			l.Cartao.AddThemeStyleboxOverride("panel", ligado
				? Tema.Caixa(Tema.PainelClaro, Tema.Borda, 6)
				: Tema.Caixa(Tema.PainelApagado, Tema.BordaApagada, 6));
			if (cabe) total++;
			if (ligado) ligados++;
		}
		_contagem.Text = $"{ligados} de {total} ligados  ";

		// O NOME: o aviso so aparece com algo digitado que nao serve (vazio e o nome de sempre)
		bool nomeLivre = !ConfigDaEstatua.NomeFixoEm(_planeta);
		string? doNome = nomeLivre && ConfigDaEstatua.Limpo(_cfg.Nome).Length > 0 ? ConfigDaEstatua.PorQueNaoServeONome(_cfg.Nome) : null;
		_avisoDoNome.Text = doNome ?? "";
		_avisoDoNome.Visible = doNome != null;

		// O SUPREMO: gravar, e em que moeda -- com o saldo de quem ergue
		_cfg.Supremo = _modo == Modo.Erguer && _supremo.ButtonPressed
			? (_emRecursos.ButtonPressed ? ConfigDaEstatua.SupremoEmRecursos : ConfigDaEstatua.SupremoEmZeni) : "";
		_emZeni.Disabled = _emRecursos.Disabled = !_supremo.ButtonPressed;
		double zeni = GameClient.Instance?.Zeni ?? 0, recursos = GameClient.Instance?.Recursos ?? 0;
		_saldo.Text = $"Você tem {zeni:N0} zeni e {recursos:N0} recursos.";
		string? doBolso = _cfg.Supremo switch
		{
			ConfigDaEstatua.SupremoEmZeni when zeni < Desejos.PrecoDoSupremo =>
				$"Falta zeni pra gravar o supremo: ele custa {Recurso.PrecoEscrito(Desejos.PrecoDoSupremo, Moeda.Zeni)}.",
			ConfigDaEstatua.SupremoEmRecursos when recursos < Recurso.CustoEmRecursos(Desejos.PrecoDoSupremo) =>
				$"Faltam recursos pra gravar o supremo: ele custa {Recurso.PrecoEscrito(Desejos.PrecoDoSupremo, Moeda.Recursos)}.",
			_ => null,
		};

		string nome = ConfigDaEstatua.NomeNoPlaneta(_planeta, doNome == null ? _cfg.Nome : "");
		_resumo.Text = $"{nome}  ·  {(pedidos == 1 ? "1 pedido" : $"{pedidos} pedidos")} de {ConfigDaEstatua.PorcentagemDaForca(pedidos)}  ·  "
					 + $"{ligados} desejo(s) ligado(s)"
					 + (_modo == Modo.Erguer ? (_cfg.Supremo.Length > 0 ? $"  ·  supremo em {_cfg.Supremo}" : "  ·  sem o supremo")
						: _supremoGravado ? "  ·  supremo gravado" : "");

		// O TETO DO FIO: acima dele o servidor leria o argumento VAZIO e ergueria a estatua de sempre (ver `ConfigDaEstatua.Codificar`, que
		// nunca chega perto dele -- esta linha e a rede de baixo)
		string? doFio = _cfg.Codificar().Length > Jandirus.Net.Protocol.MaxArgDeVerbo
			? "a configuração passou do tamanho que o servidor lê." : null;
		string? recusa = doNome ?? _cfg.PorQueNaoServe(nomeLivre) ?? doBolso ?? doFio;
		_motivo.Text = recusa ?? "";
		_motivo.Visible = recusa != null;
		_confirmar.Disabled = recusa != null;
	}

	/// <summary>
	/// O PEDIDO SAI: a configuracao no verbo de producao, e a janela fecha. A resposta do servidor e o chat -- e a recusa dele (outra
	/// estatua no mundo, o dominio que mudou de mao, o saldo que acabou) tambem.
	/// </summary>
	private void Confirmar()
	{
		if (_confirmar.Disabled || GameClient.Instance is not { } cli) return;
		if (ConfigDaEstatua.NomeFixoEm(_planeta)) _cfg.Nome = "";
		cli.SendVerbo(_modo == Modo.Erguer ? "db_estatua" : "db_refazer", _cfg.Codificar());
		Fechar();
	}
}
