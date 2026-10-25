using Godot;

namespace Jandirus.Client;

/// <summary>
/// O FUNDO DA ESTREIA DO SSJ2 (2026-10-24) -- o que so a tela de QUEM VIRA mostra, nos primeiros segundos da primeira vez dele no SSJ2.
///
/// O dono, com tres prints do Gohan: *"faça um efeito q so aparece pro personagem q virou ssj2 pela primeira vez (um efeito local) como pode ver na imagem, ficaria tudo escuro atras do personagem, tudo do jogo sumiria, e uma linha vermelha passaria atras e se expande cobrindo todo o fundo preto com esse efeito vermelho/amarelo, e ai começa a cinematica normal do ssj2 n deve durar mais do q 4 segundos isso tudo, e a musica do ssj2 ja deve começar desde o começo desse efeito, dps dele faz a cinematica padrao atual"*.
///
/// ============================ TRES PECAS, E NENHUMA E UMA TELA POR CIMA ============================
///   1. A CORTINA E DO MUNDO, e nao uma camada de tela: um retangulo grande que anda com o corpo, no plano <see cref="CamadaDaCortina"/> --
///      acima de tudo o que o mundo desenha (os planos dele nao passam de ~100) e ABAIXO do corpo, que sobe pro <see cref="CamadaDoCorpo"/>
///      enquanto o fundo dura. Uma camada de tela tamparia o corpo junto; desenhar o corpo de novo numa tela por cima seria um segundo
///      boneco, que a aura, o cabelo e a cena teriam de achar.
///   2. A LINHA E O FOGO SAO DA TELA: o shader (`FundoDaEstreia.gdshader`) desenha pela coordenada de tela, e a diagonal cruza o quadro
///      inteiro onde quer que o corpo esteja.
///   3. "TUDO DO JOGO SUMIRIA": as camadas de tela (o painel, o chat, o clima) e a tinta do ceu saem enquanto o fundo dura, e voltam como
///      estavam -- inclusive se o mundo cair por baixo (`_ExitTree`).
/// ==================================================================================================
///
/// O TEMPO: o mundo escurece em <see cref="Escurece"/>, a linha entra pela esquerda (<see cref="LinhaComeca"/>, em <see cref="LinhaCruza"/>),
/// a faixa se abre em fogo (<see cref="AbreComeca"/>, em <see cref="AbreDura"/>), o fogo fica, e o mundo volta nos ultimos <see cref="Volta"/>.
/// Quem decide QUANDO e a cena (o beat `Efeito.FundoDaEstreia` no segundo zero da estreia do SSJ2, `Cinematicas.EstreiaDoSsj2`) e por QUANTO
/// tempo e o Core (`Cinematicas.SegundosDoPreludio`); a musica e a da propria cena, e toca do segundo zero.
/// </summary>
public partial class FundoDaEstreia : Node2D
{
	/// <summary>O nome do node no mundo.</summary>
	public const string NomeDoNode = "FundoDaEstreia";

	/// <summary>O plano da cortina: acima do mundo inteiro (os planos dele nao passam de ~100).</summary>
	public const int CamadaDaCortina = 1000;

	/// <summary>O plano do corpo enquanto o fundo dura: acima da cortina, com folga pras camadas dele (a aura atras, o balao na frente).</summary>
	public const int CamadaDoCorpo = 1100;

	/// <summary>A inclinacao da diagonal (desce pra direita, como nos prints). A bancada mede os pixels na mesma reta.</summary>
	public const float Inclinacao = 0.25f;

	/// <summary>Os tempos do fundo, em segundos desde que ele abre. Ver o cabecalho.</summary>
	public const double Escurece = 0.25, LinhaComeca = 0.6, LinhaCruza = 0.6, AbreComeca = 1.4, AbreDura = 1.2, Volta = 0.4;

	/// <summary>DEFEITO INJETADO (bancada `--diagpreludio`): o fundo abre tambem na tela de quem ASSISTE. Falso em jogo, sempre.</summary>
	internal static bool PraQuemAssisteDeTeste;

	/// <summary>O fundo aberto agora (um so de cada vez), ou nulo.</summary>
	public static FundoDaEstreia? Ativo { get; private set; }

	private Node2D _corpo = null!;
	private double _t, _duracao;
	private int _zDoCorpo;
	private bool _zRelativoDoCorpo, _fechado;
	private readonly List<CanvasLayer> _camadasEscondidas = [];
	private readonly List<CanvasModulate> _tintasEscondidas = [];
	private ShaderMaterial _mat = null!;

	/// <summary>Abre o fundo atras deste corpo, por tantos segundos. Fecha o que estiver aberto antes.</summary>
	public static FundoDaEstreia? Abrir(Node2D corpo, double segundos)
	{
		if (!IsInstanceValid(corpo) || corpo.GetParent() is not { } pai) return null;
		Ativo?.Fechar();
		var f = new FundoDaEstreia { Name = NomeDoNode, _corpo = corpo, _duracao = segundos };
		pai.AddChild(f);
		return f;
	}

	public override void _Ready()
	{
		Ativo = this;
		ZIndex = CamadaDaCortina;
		ZAsRelative = false;
		LightMask = 0;   // a luz das tochas e dos tiros nao clareia a cortina
		_mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Assets/Shaders/FundoDaEstreia.gdshader") };
		_mat.SetShaderParameter("inclinacao", Inclinacao);
		const float lado = 8000f;   // a tela inteira em qualquer zoom: e o corpo que anda, e a cortina anda com ele
		AddChild(new ColorRect
		{
			Color = Colors.Black,
			Size = new Vector2(lado, lado),
			Position = new Vector2(-lado / 2, -lado / 2),
			Material = _mat,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			LightMask = 0,
		});
		GlobalPosition = _corpo.GlobalPosition;
		_zDoCorpo = _corpo.ZIndex;
		_zRelativoDoCorpo = _corpo.ZAsRelative;
		_corpo.ZIndex = CamadaDoCorpo;
		_corpo.ZAsRelative = false;
		EsconderOResto();
		Aplicar(0);
	}

	public override void _Process(double delta)
	{
		_t += delta;
		if (!IsInstanceValid(_corpo) || _t >= _duracao) { Fechar(); return; }
		GlobalPosition = _corpo.GlobalPosition;
		Aplicar(_t);
	}

	/// <summary>Os tres uniforms no segundo <paramref name="t"/> do fundo.</summary>
	private void Aplicar(double t)
	{
		double alfa = t < Escurece ? t / Escurece : t > _duracao - Volta ? Math.Max(0, (_duracao - t) / Volta) : 1;
		double linha = Math.Clamp((t - LinhaComeca) / LinhaCruza, 0, 1);
		double p = Math.Clamp((t - AbreComeca) / AbreDura, 0, 1);
		// a faixa nasce da linha e acelera ate tampar a tela: 0,75 de meia-largura cobre a quina mais longe da diagonal, que fica a ~0,56
		double largura = p <= 0 ? 0 : 0.006 + 0.75 * p * p * p;
		_mat.SetShaderParameter("alfa", (float)alfa);
		_mat.SetShaderParameter("linha", (float)linha);
		_mat.SetShaderParameter("largura", (float)largura);
	}

	/// <summary>"tudo do jogo sumiria": as camadas de tela e a tinta do ceu saem, e a lista guarda o que volta.</summary>
	private void EsconderOResto()
	{
		foreach (Node n in GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false))
			if (n is CanvasLayer c && c.Visible) { c.Visible = false; _camadasEscondidas.Add(c); }
		foreach (Node n in GetTree().Root.FindChildren("*", nameof(CanvasModulate), true, false))
			if (n is CanvasModulate m && m.Visible) { m.Visible = false; _tintasEscondidas.Add(m); }
	}

	private void Restaurar()
	{
		if (IsInstanceValid(_corpo)) { _corpo.ZIndex = _zDoCorpo; _corpo.ZAsRelative = _zRelativoDoCorpo; }
		foreach (CanvasLayer c in _camadasEscondidas) if (IsInstanceValid(c)) c.Visible = true;
		foreach (CanvasModulate m in _tintasEscondidas) if (IsInstanceValid(m)) m.Visible = true;
		_camadasEscondidas.Clear();
		_tintasEscondidas.Clear();
	}

	/// <summary>Fecha o fundo: o corpo volta ao plano dele, e o que sumiu volta.</summary>
	public void Fechar()
	{
		if (_fechado) return;
		_fechado = true;
		Restaurar();
		if (Ativo == this) Ativo = null;
		QueueFree();
	}

	/// <summary>O MUNDO PODE CAIR POR BAIXO (troca de zona, saida do jogo): o que sumiu volta do mesmo jeito.</summary>
	public override void _ExitTree()
	{
		if (_fechado) return;
		_fechado = true;
		Restaurar();
		if (Ativo == this) Ativo = null;
	}

	/// <summary>Ha quanto tempo o fundo esta aberto (s). Pra bancada.</summary>
	public double SegundosDeTeste => _t;

	/// <summary>Quantas camadas de tela este fundo escondeu. Pra bancada.</summary>
	public int CamadasEscondidasDeTeste => _camadasEscondidas.Count;
}
