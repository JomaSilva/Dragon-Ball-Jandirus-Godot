using Godot;
using Jandirus.Core.Appearance;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CHEGADA DE UM CHEFE DE SAGA, NA TELA DE TODOS (v0.3.0, item 35 do pedido 15) ============================
/// *"Seria bom uma cutscene q aparece pra todos quando um boss de saga aparece"*. O servidor manda `S2C.ChegadaDoChefe` pra
/// todo jogador do mundo (`GameServer.AnunciarChegadaDoChefe`), e esta tela a desenha: as faixas de cinema entram, o RETRATO
/// do chefe -- o mesmo boneco em camadas do mundo (`CharacterVisual.Vestir`), e nao uma figura a parte -- cresce no meio, o
/// nome, o planeta e a frase da chegada aparecem, o rugido toca, e tudo some.
///
/// ELA NAO PRENDE NADA: nenhum clique e engolido (`MouseFilter.Ignore` em tudo) e o teclado continua do jogador. Quem esta
/// lutando do outro lado da galaxia continua lutando -- a cena e o aviso grande, e nao uma pausa do mundo inteiro por causa
/// de um chefe que nao esta la.
/// ==========================================================================================================
/// </summary>
public partial class CenaDaChegada : CanvasLayer
{
	/// <summary>Quanto a cena fica na tela, em segundos -- entrada, leitura e saida.</summary>
	public const double Duracao = 6.5;

	private const double Entrada = 0.6, Saida = 0.9;
	private const float AlturaDaFaixa = 92f;

	/// <summary>Quem chegou -- o pacote inteiro. Escrito por quem cria a cena (`World.AoChegarOChefe`).</summary>
	public GameClient.ChegadaDeChefe Chegada;

	/// <summary>O catalogo de aparencia do mundo, pra vestir o retrato. Nulo = a cena sai sem retrato.</summary>
	public VisualCatalog? Catalogo;

	private double _t;
	private ColorRect _faixaDeCima = null!, _faixaDeBaixo = null!;
	private Control _miolo = null!;
	private CharacterVisual? _retrato;
	private Label _nome = null!, _onde = null!, _frase = null!;

	/// <summary>Quantas cenas de chegada nasceram neste processo, e a que esta na tela agora. So bancada.</summary>
	public static int CenasDeTeste { get; private set; }
	public static CenaDaChegada? NaTelaDeTeste { get; private set; }

	internal string NomeDeTeste => _nome.Text;
	internal string OndeDeTeste => _onde.Text;
	internal string FraseDeTeste => _frase.Text;
	internal bool RetratoDeTeste => _retrato != null && IsInstanceValid(_retrato) && _retrato.IsVisibleInTree();
	internal bool PrendeOMouseDeTeste => FindChildren("*", nameof(Control), true, false).OfType<Control>()
		.Any(c => c.MouseFilter != Control.MouseFilterEnum.Ignore);

	public override void _Ready()
	{
		Layer = 6;   // por cima do HUD, do chat (2) e do chaveamento (3)
		CenasDeTeste++;
		NaTelaDeTeste = this;

		var raiz = Ignorar(new Control { AnchorRight = 1, AnchorBottom = 1 });
		AddChild(raiz);

		_faixaDeCima = Ignorar(new ColorRect { Color = Colors.Black, AnchorRight = 1 });
		_faixaDeBaixo = Ignorar(new ColorRect { Color = Colors.Black, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1 });
		raiz.AddChild(_faixaDeCima);
		raiz.AddChild(_faixaDeBaixo);

		var centro = Ignorar(new CenterContainer { AnchorRight = 1, AnchorBottom = 1 });
		raiz.AddChild(centro);
		var linha = Ignorar(new HBoxContainer());
		linha.AddThemeConstantOverride("separation", 28);
		centro.AddChild(linha);
		_miolo = linha;
		_miolo.Modulate = new Color(1, 1, 1, 0);

		// O RETRATO: o boneco do mundo, grande. Num Control que recorta -- o Node2D nao respeita o layout do container.
		var moldura = Ignorar(new Control { CustomMinimumSize = new Vector2(200, 250), ClipContents = true });
		linha.AddChild(moldura);
		if (Catalogo != null)
		{
			_retrato = new CharacterVisual { Name = "Retrato", Position = new Vector2(100, 150), Scale = new Vector2(6, 6) };
			moldura.AddChild(_retrato);
			_retrato.Vestir(Catalogo, Chegada.Visual, Chegada.Raca, Chegada.Genero);
		}

		var textos = Ignorar(new VBoxContainer { CustomMinimumSize = new Vector2(540, 0), Alignment = BoxContainer.AlignmentMode.Center });
		textos.AddThemeConstantOverride("separation", 6);
		linha.AddChild(textos);
		textos.AddChild(Ignorar(Tema.Legenda("UM INIMIGO PODEROSO CHEGOU", Tema.TextoFraco, 15)));
		_nome = Ignorar(Tema.Legenda(Chegada.Nome.ToUpperInvariant(), Tema.Vida, 46));
		textos.AddChild(_nome);
		_onde = Ignorar(Tema.Legenda($"em {Chegada.Planeta}", Tema.Destaque, 20));
		textos.AddChild(_onde);
		_frase = Ignorar(Tema.Legenda(Chegada.Frase, Tema.Texto, 16));
		_frase.AutowrapMode = TextServer.AutowrapMode.Word;
		_frase.CustomMinimumSize = new Vector2(540, 0);
		textos.AddChild(_frase);

		AudioDirector.Instance?.Efeito(Trilha.Rugido, 0.9f);
		Desenhar();
	}

	private static T Ignorar<T>(T c) where T : Control
	{
		c.MouseFilter = Control.MouseFilterEnum.Ignore;
		return c;
	}

	public override void _Process(double delta)
	{
		_t += delta;
		Desenhar();
		if (_t < Duracao) return;
		if (NaTelaDeTeste == this) NaTelaDeTeste = null;
		QueueFree();
	}

	/// <summary>
	/// O QUADRO DA CENA, pelo relogio dela: as faixas e o miolo entram juntos (0,6 s), ficam, e saem juntos (0,9 s). O retrato
	/// cresce devagar a cena inteira -- o chefe se aproximando de quem esta olhando.
	/// </summary>
	private void Desenhar()
	{
		float entra = (float)Math.Clamp(_t / Entrada, 0, 1);
		float sai = (float)Math.Clamp((Duracao - _t) / Saida, 0, 1);
		float a = Math.Min(entra, sai);
		_faixaDeCima.OffsetBottom = AlturaDaFaixa * a;
		_faixaDeBaixo.OffsetTop = -AlturaDaFaixa * a;
		_miolo.Modulate = new Color(1, 1, 1, a);
		if (_retrato != null && IsInstanceValid(_retrato))
			_retrato.Scale = Vector2.One * (5.4f + 0.9f * (float)Math.Clamp(_t / Duracao, 0, 1));
	}
}
