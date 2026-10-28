using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// ============================ A TELA DO ESTUDO -- OS MINIJOGOS DA BANCADA (dono, 2026-10-27, o pedido 120) ============================
/// Abre sozinha quando o servidor manda um minijogo (`S2C.Estudo`, a cada minuto de estudo na Research Station) e some quando ele fecha:
///   * a CONTA e o SIMBOLO: o enunciado, um campo e "Responder" (o Enter tambem responde);
///   * a MOLECULA: as bolinhas na mesa (uma por atomo, na cor de cada um) -- clicar em duas liga as duas; clicar de novo no mesmo par
///     faz a dupla e a tripla, e a quarta vez desfaz; arrastar uma bolinha a move. Cada bolinha diz quantas ligacoes ainda faltam. "Pronto"
///     manda as ligacoes, e quem decide se fechou e o servidor (a regra das ligacoes do `Minijogos.MoleculaFechou`).
/// No alto, o relogio dos 30 s e o bonus de estudo valendo. O resultado (certo, errado, tempo) fica na tela um instante e a janela some.
///
/// O MOLDE E O DA <see cref="TelaDeMeditacao"/>: `CanvasLayer` na camada 4, montada no boot e invisivel. O enunciado e escrito em
/// portugues (o dicionario da tela o traduz), com o nome do elemento e da molecula ja na lingua da tela (`Minijogos.Pergunta`).
/// ======================================================================================================================
/// </summary>
public partial class TelaDoEstudo : CanvasLayer
{
	/// <summary>A tela viva, ou nula antes do boot. So o robo de fotos (`--diagestudo`) a procura por aqui.</summary>
	public static TelaDoEstudo? Instancia { get; private set; }

	/// <summary>
	/// (2026-10-27, o pedido 123) A resposta do minijogo esta sendo digitada? Lido por `Foco.Digitando` -- sem isto as letras com atalho
	/// faziam a funcao da tecla em vez de irem pra resposta. So com a tela aberta, o campo a mostra (a molecula nao tem campo) e o foco
	/// nele: a tela que some solta o foco sozinha, e o jogo volta a ouvir as teclas.
	/// </summary>
	public static bool Digitando => Instancia is { } t && IsInstanceValid(t._campo) && t._raiz.Visible && t._campo.Visible && t._campo.HasFocus();

	private Control _raiz = null!;
	private Label _titulo = null!, _bonus = null!, _relogio = null!, _pergunta = null!, _resultado = null!;
	private LineEdit _campo = null!;
	private Button _responder = null!, _pronto = null!, _limpar = null!;
	private Label _dica = null!;
	private QuadroDaMolecula _quadro = null!;

	private TipoDeMinijogo _tipo;
	private double _restante, _sumirEm;

	/// <summary>O minijogo que a tela mostra agora (Nenhum = fechada). So o robo de fotos.</summary>
	public TipoDeMinijogo TipoNaTela => _raiz.Visible ? _tipo : TipoDeMinijogo.Nenhum;

	/// <summary>O quadro da molecula. So o robo de fotos (ele liga as bolinhas pelo mesmo caminho do clique).</summary>
	internal QuadroDaMolecula QuadroDeTeste => _quadro;

	/// <summary>O enunciado na tela. So o robo de fotos.</summary>
	internal string PerguntaDeTeste => _pergunta.Text;

	/// <summary>O texto no campo da resposta, e se ele tem o foco. So o robo de fotos (o pedido 123: a letra de atalho vai pra resposta).</summary>
	internal string RespostaNoCampoDeTeste => _campo.Text;
	internal bool CampoComFocoDeTeste => _campo.HasFocus();

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu da tecla E e da tela de meditacao
		Instancia = this;
		Montar();
		if (GameClient.Instance is { } cli) cli.EstudoMudou += AoMudar;
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		if (GameClient.Instance is { } cli) cli.EstudoMudou -= AoMudar;
	}

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(14);
		centro.AddChild(painel);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		var topo = new HBoxContainer();
		_titulo = new Label { Text = "Estudo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_titulo.AddThemeFontSizeOverride("font_size", 20);
		_relogio = new Label { HorizontalAlignment = HorizontalAlignment.Right };
		_relogio.AddThemeFontSizeOverride("font_size", 18);
		topo.AddChild(_titulo);
		topo.AddChild(_relogio);
		caixa.AddChild(topo);

		_bonus = new Label();
		_bonus.AddThemeFontSizeOverride("font_size", 13);
		caixa.AddChild(_bonus);
		caixa.AddChild(new HSeparator());

		_pergunta = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
		_pergunta.AddThemeFontSizeOverride("font_size", 18);
		caixa.AddChild(_pergunta);

		_campo = new LineEdit { PlaceholderText = "sua resposta", Alignment = HorizontalAlignment.Center };
		_campo.AddThemeFontSizeOverride("font_size", 18);
		_campo.TextSubmitted += _ => Responder();
		caixa.AddChild(_campo);
		_responder = new Button { Text = "Responder" };
		_responder.Pressed += Responder;
		caixa.AddChild(_responder);

		_quadro = new QuadroDaMolecula { CustomMinimumSize = new Vector2(460, 300) };
		caixa.AddChild(_quadro);
		_dica = new Label
		{
			Text = "Clique em duas bolinhas pra ligá-las; no mesmo par de novo, a ligação vira dupla e tripla (a quarta desfaz). Arraste pra mover.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		_dica.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_dica);
		var botoes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_limpar = new Button { Text = "Limpar ligações" };
		_limpar.Pressed += () => _quadro.Limpar();
		_pronto = new Button { Text = "Pronto" };
		_pronto.Pressed += Responder;
		botoes.AddChild(_limpar);
		botoes.AddChild(_pronto);
		caixa.AddChild(botoes);

		_resultado = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_resultado.AddThemeFontSizeOverride("font_size", 20);
		caixa.AddChild(_resultado);
	}

	/// <summary>O PACOTE CHEGOU: um minijogo novo abre a tela; o fechamento mostra o resultado e a tela some num instante.</summary>
	private void AoMudar()
	{
		if (GameClient.Instance is not { } cli) return;
		var e = cli.Estudo;
		int pct = (int)Math.Round(Minijogos.BonusPorAcerto * e.Bonus * 100);
		_bonus.Text = e.Bonus > 0 ? $"Bônus de estudo valendo: +{pct}% de tecnologia" : "Sem bônus de estudo valendo.";

		if (e.Tipo == TipoDeMinijogo.Nenhum)
		{
			_resultado.Text = e.Resultado switch { 1 => "Certo!", 2 => "Errado.", 3 => "Tempo esgotado.", _ => "" };
			_resultado.Modulate = e.Resultado == 1 ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.55f, 0.5f);
			_campo.Editable = false;
			_responder.Disabled = _pronto.Disabled = _limpar.Disabled = true;
			_quadro.Travado = true;
			_sumirEm = 1.6;
			return;
		}

		_tipo = e.Tipo;
		_restante = e.Segundos;
		_sumirEm = 0;
		_resultado.Text = "";
		_pergunta.Text = Minijogos.Pergunta(e.Tipo, e.Indice, e.Expressao, e.Forma, Idioma.EmIngles);
		bool molecula = e.Tipo == TipoDeMinijogo.Molecula;
		_campo.Visible = _responder.Visible = !molecula;
		_quadro.Visible = _dica.Visible = _limpar.Visible = _pronto.Visible = molecula;
		_campo.Editable = true;
		_campo.Text = "";
		_responder.Disabled = _pronto.Disabled = _limpar.Disabled = false;
		_quadro.Travado = false;
		if (molecula) _quadro.Montar(Minijogos.Moleculas[Math.Clamp(e.Indice, 0, Minijogos.Moleculas.Length - 1)].Atomos);
		_raiz.Visible = true;
		if (!molecula) _campo.CallDeferred(Control.MethodName.GrabFocus);
	}

	/// <summary>Manda a resposta: o texto do campo, ou as ligacoes do quadro.</summary>
	private void Responder()
	{
		if (!_raiz.Visible || _quadro.Travado && _tipo == TipoDeMinijogo.Molecula || !_campo.Editable && _tipo != TipoDeMinijogo.Molecula) return;
		string resposta = _tipo == TipoDeMinijogo.Molecula ? Minijogos.EscreverLigacoes(_quadro.Ligacoes()) : _campo.Text;
		GameClient.Instance?.SendTech("estudo_resposta", resposta);
		_responder.Disabled = _pronto.Disabled = true;
	}

	/// <summary>A resposta que o robo de fotos manda pelo mesmo caminho do botao.</summary>
	internal void ResponderDeTeste(string texto)
	{
		_campo.Text = texto;
		Responder();
	}

	/// <summary>O "Pronto" da molecula, pelo robo de fotos.</summary>
	internal void ProntoDeTeste() => Responder();

	public override void _Process(double delta)
	{
		if (!_raiz.Visible) return;
		if (_sumirEm > 0)
		{
			_sumirEm -= delta;
			if (_sumirEm <= 0) _raiz.Visible = false;
			return;
		}
		_restante = Math.Max(0, _restante - delta);
		_relogio.Text = $"{Math.Ceiling(_restante):0} s";
	}
}

/// <summary>
/// O QUADRO DA MOLECULA: as bolinhas (uma por atomo) e as ligacoes entre elas, desenhadas a mao (`_Draw`). Clique numa, depois noutra:
/// a ligacao entre as duas sobe um grau (simples, dupla, tripla, desfaz). Arrastar move a bolinha. Cada uma mostra o simbolo e quantas
/// ligacoes ainda faltam pra valencia dela.
/// </summary>
public partial class QuadroDaMolecula : Control
{
	private const float Raio = 20f;

	private string[] _atomos = [];
	private Vector2[] _onde = [];
	private readonly Dictionary<(int, int), int> _ligacoes = [];
	private int _escolhida = -1, _arrastando = -1;
	private Vector2 _apertouEm;
	private bool _moveu;

	/// <summary>Travado = o minijogo fechou; nada mais muda.</summary>
	public bool Travado;

	/// <summary>As bolinhas na mesa, em roda (o carbono e o nitrogenio mais pro meio, o hidrogenio por fora).</summary>
	public void Montar(string[] atomos)
	{
		_atomos = atomos;
		_ligacoes.Clear();
		_escolhida = _arrastando = -1;
		_onde = new Vector2[atomos.Length];
		Vector2 meio = CustomMinimumSize / 2;
		int pesados = atomos.Count(a => a is not ("H" or "F" or "Cl")), leves = atomos.Length - pesados;
		int ip = 0, il = 0;
		for (int i = 0; i < atomos.Length; i++)
		{
			bool leve = atomos[i] is "H" or "F" or "Cl";
			int k = leve ? il++ : ip++, n = leve ? leves : pesados;
			float raio = leve ? 120f : (pesados <= 1 ? 0f : 60f);
			float ang = Mathf.Tau * k / Math.Max(n, 1) + (leve ? 0.3f : 0f);
			_onde[i] = meio + new Vector2(Mathf.Cos(ang) * raio * 1.4f, Mathf.Sin(ang) * raio);
		}
		QueueRedraw();
	}

	/// <summary>Todas as ligacoes desfeitas.</summary>
	public void Limpar()
	{
		if (Travado) return;
		_ligacoes.Clear();
		_escolhida = -1;
		QueueRedraw();
	}

	/// <summary>As ligacoes de agora (atomo A, atomo B, grau).</summary>
	public List<(int A, int B, int Ordem)> Ligacoes() => [.. _ligacoes.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value))];

	/// <summary>
	/// O CLIQUE NUM PAR: a ligacao entre as duas sobe um grau (a quarta vez desfaz). O caminho do mouse e o do robo de fotos.
	/// </summary>
	public void Ligar(int a, int b)
	{
		if (Travado || a == b || a < 0 || b < 0 || a >= _atomos.Length || b >= _atomos.Length) return;
		var par = (Math.Min(a, b), Math.Max(a, b));
		int grau = (_ligacoes.GetValueOrDefault(par) + 1) % 4;
		if (grau == 0) _ligacoes.Remove(par);
		else _ligacoes[par] = grau;
		QueueRedraw();
	}

	private int Bolinha(Vector2 p)
	{
		for (int i = _onde.Length - 1; i >= 0; i--)
			if (_onde[i].DistanceTo(p) <= Raio) return i;
		return -1;
	}

	public override void _GuiInput(InputEvent e)
	{
		if (Travado) return;
		if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
		{
			if (mb.Pressed)
			{
				_arrastando = Bolinha(mb.Position);
				_apertouEm = mb.Position;
				_moveu = false;
			}
			else
			{
				int solta = _arrastando;
				_arrastando = -1;
				if (solta >= 0 && !_moveu)
				{
					if (_escolhida < 0) _escolhida = solta;
					else if (_escolhida == solta) _escolhida = -1;
					else { Ligar(_escolhida, solta); _escolhida = -1; }
				}
				QueueRedraw();
			}
			AcceptEvent();
		}
		else if (e is InputEventMouseMotion mm && _arrastando >= 0)
		{
			if (mm.Position.DistanceTo(_apertouEm) > 4) _moveu = true;
			if (_moveu)
			{
				_onde[_arrastando] = new Vector2(Mathf.Clamp(mm.Position.X, Raio, Size.X - Raio), Mathf.Clamp(mm.Position.Y, Raio, Size.Y - Raio));
				QueueRedraw();
			}
			AcceptEvent();
		}
	}

	private static Color Cor(string atomo) => atomo switch
	{
		"H" => new Color(0.93f, 0.93f, 0.93f),
		"C" => new Color(0.25f, 0.25f, 0.27f),
		"N" => new Color(0.25f, 0.4f, 0.95f),
		"O" => new Color(0.9f, 0.2f, 0.2f),
		"F" => new Color(0.6f, 0.9f, 0.3f),
		"Cl" => new Color(0.2f, 0.8f, 0.3f),
		_ => new Color(0.8f, 0.5f, 0.8f),
	};

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.06f, 0.07f, 0.1f, 0.85f));
		var soma = new int[_atomos.Length];
		foreach (((int a, int b), int grau) in _ligacoes)
		{
			soma[a] += grau;
			soma[b] += grau;
			Vector2 de = _onde[a], ate = _onde[b], lado = (ate - de).Orthogonal().Normalized() * 5f;
			for (int k = 0; k < grau; k++)
			{
				Vector2 d = lado * (k - (grau - 1) / 2f);
				DrawLine(de + d, ate + d, new Color(0.85f, 0.85f, 0.9f), 2.5f, true);
			}
		}
		Font fonte = GetThemeDefaultFont();
		for (int i = 0; i < _atomos.Length; i++)
		{
			Color c = Cor(_atomos[i]);
			if (i == _escolhida) DrawCircle(_onde[i], Raio + 4, new Color(1f, 0.85f, 0.3f));
			DrawCircle(_onde[i], Raio, c);
			bool claro = c.Luminance > 0.55f;
			string s = _atomos[i];
			Vector2 t = fonte.GetStringSize(s, HorizontalAlignment.Left, -1, 16);
			DrawString(fonte, _onde[i] + new Vector2(-t.X / 2, 6), s, HorizontalAlignment.Left, -1, 16, claro ? Colors.Black : Colors.White);
			int falta = Minijogos.Valencia.GetValueOrDefault(_atomos[i]) - soma[i];
			string f = falta == 0 ? "ok" : falta.ToString();
			DrawString(fonte, _onde[i] + new Vector2(Raio - 4, -Raio + 2), f, HorizontalAlignment.Left, -1, 12,
					   falta == 0 ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.8f, 0.4f));
		}
	}
}
