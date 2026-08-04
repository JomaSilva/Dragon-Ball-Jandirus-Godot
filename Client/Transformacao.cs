using Godot;

namespace Jandirus.Client;

/// <summary>
/// A CINEMATICA DO DESPERTAR. Roda UMA vez por forma, na primeira vez que ela sai.
///
/// Por que so uma vez: o impacto de uma transformacao e a surpresa. Repetir a mesma explosao
/// toda vez que alguem aperta C transforma o momento mais importante do jogo em ruido -- e no
/// segundo dia o jogador ja esta apertando C pra passar por cima dela. O servidor guarda quais
/// formas ja despertaram (`EstadoDeForma.JaDespertou`, que vai no save), entao a cena e
/// realmente irrepetivel naquele personagem.
///
/// TRES TEMPOS, e eles nao sao decorativos -- sao a estrutura da cena no anime:
///
///   CARGA (0 -> 0,9 s)   o chao treme, a poeira sobe, a luz pulsa e cresce. Nada explode
///                        ainda: e a pressao juntando.
///   ESTOURO (0,9 -> 1,2) o clarao, a onda de choque que ENTORTA a tela, o tranco da camera.
///   ASSENTAR (1,2 -> 3)  a coluna de luz baixa ate virar a aura permanente da forma.
///
/// A ONDA DE CHOQUE E UM SHADER DE TELA CHEIA que le o proprio quadro ja desenhado e desloca os
/// pixels num anel -- e por isso ela deforma o CENARIO, e nao so pinta por cima dele. Pra isso
/// existir e preciso um <see cref="BackBufferCopy"/> antes: sem ele o `hint_screen_texture` le
/// um quadro vazio e o efeito simplesmente nao aparece.
/// </summary>
public partial class Transformacao : Node2D
{
	/// <summary>
	/// O SHADER DA ONDA. Um anel que se afasta do centro empurrando os pixels pra fora, com a
	/// borda acesa na cor da forma.
	///
	/// `deslocamento` cai com o tempo E com a espessura do anel: uma onda que empurra o mesmo
	/// tanto do inicio ao fim parece um zoom, nao uma explosao. O que da a leitura de energia e
	/// a borda ficar FINA e FORTE e ir se abrindo.
	/// </summary>
	/// <summary>
	/// O CODIGO DESTE EFEITO mora num `.gdshader` de verdade -- ver o comentario de
	/// <see cref="CharacterVisual"/>: efeito procedural nao se acerta lendo codigo, se acerta
	/// arrastando o valor e OLHANDO, e pra isso ele precisa abrir no editor do Godot.
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Transformacao.gdshader";

	/// <summary>
	/// O PILAR DE LUZ. Um feixe vertical que sobe do personagem, com ruido pra nao parecer um
	/// retangulo pintado.
	/// </summary>
	private const string CodigoPilar = """
		shader_type canvas_item;

		uniform vec4 cor : source_color = vec4(1.0, 0.85, 0.35, 1.0);
		uniform float tempo = 0.0;
		uniform float intensidade = 1.0;

		// ruido barato e sem textura: duas senoides que nao fecham periodo juntas
		float ondinha(vec2 p) {
			return sin(p.y * 14.0 + tempo * 9.0) * 0.5
				 + sin(p.y * 27.0 - tempo * 13.0) * 0.3;
		}

		void fragment() {
			vec2 uv = UV;

			// a coluna AFINA em cima: energia subindo, nao um poste
			float afunila = mix(1.0, 0.25, uv.y);
			float meio = 0.5 + ondinha(uv) * 0.02 * (1.0 - uv.y);
			float d = abs(uv.x - meio) / (0.5 * afunila);

			float corpo = 1.0 - smoothstep(0.35, 1.0, d);
			float nucleo = 1.0 - smoothstep(0.0, 0.35, d);

			// esvanece em cima; a base fica cheia porque e de onde a luz sai
			float alt = 1.0 - smoothstep(0.55, 1.0, uv.y);

			vec3 c = mix(cor.rgb, vec3(1.0), nucleo * 0.75);
			float a = (corpo * 0.55 + nucleo * 0.9) * alt * intensidade;
			COLOR = vec4(c, a);
		}
		""";

	private static Shader? _shOnda, _shPilar;
	private static Shader ShOnda => _shOnda ??= ResourceLoader.Load<Shader>(CaminhoDoShader);
	private static Shader ShPilar => _shPilar ??= new Shader { Code = CodigoPilar };

	private const double Carga = 0.9, Estouro = 1.2, Fim = 3.0;

	private Node2D _alvo = null!;
	private Color _cor;
	private int _tier;
	private double _t;

	private ColorRect _onda = null!;
	private ShaderMaterial _matOnda = null!;
	private ColorRect _pilar = null!;
	private ShaderMaterial _matPilar = null!;
	private PointLight2D _luz = null!;
	private CanvasLayer _camada = null!;
	private ColorRect _clarao = null!;
	private Raios? _raios;
	private bool _estourou;

	/// <summary>
	/// Dispara a cena. <paramref name="tier"/> escala o exagero: SSJ1 e um estouro, SSJ3 e um
	/// terremoto.
	/// </summary>
	public static void Rodar(Node pai, Node2D alvo, Color cor, int tier)
	{
		var t = new Transformacao { _alvo = alvo, _cor = cor, _tier = Mathf.Clamp(tier, 1, 4) };
		pai.AddChild(t);
	}

	public override void _Ready()
	{
		TopLevel = true;
		ZIndex = 80;   // abaixo do veu de sombra (90): a explosao nao apaga a escuridao

		// SEM O BackBufferCopy A ONDA NAO EXISTE. Ele e quem copia o quadro ja desenhado pro
		// `hint_screen_texture`; sem ele o shader le preto e o efeito some sem erro nenhum.
		AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });

		// --- onda de choque + clarao: em CanvasLayer, porque sao efeitos de TELA ---
		_camada = new CanvasLayer { Layer = 4 };
		AddChild(_camada);

		_matOnda = new ShaderMaterial { Shader = ShOnda };
		_matOnda.SetShaderParameter("cor", _cor);
		_onda = new ColorRect
		{
			AnchorRight = 1, AnchorBottom = 1,
			Material = _matOnda,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_camada.AddChild(_onda);

		_clarao = new ColorRect
		{
			AnchorRight = 1, AnchorBottom = 1,
			Color = new Color(1, 1, 1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_camada.AddChild(_clarao);

		// --- pilar de luz: no MUNDO, preso ao personagem ---
		_matPilar = new ShaderMaterial { Shader = ShPilar };
		_matPilar.SetShaderParameter("cor", _cor);
		_pilar = new ColorRect
		{
			Material = _matPilar,
			Size = new Vector2(120, 320),
			Position = new Vector2(-60, -300),
			Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(_pilar);

		_luz = new PointLight2D
		{
			Texture = Fogo.Radial(220),
			Color = _cor,
			Energy = 0,
			BlendMode = Light2D.BlendModeEnum.Add,
			ZIndex = -4,
		};
		AddChild(_luz);

		// FAISCAS so do SSJ2 pra cima: e a assinatura visual daquela forma no anime, e usa-las
		// no SSJ1 tiraria o que diferencia as duas na tela.
		if (_tier >= 2)
		{
			_raios = new Raios { Cor = _cor, Alcance = 90 + _tier * 20 };
			AddChild(_raios);
		}

		AudioDirector.EfeitoNoLugar(_alvo, Trilha.Dash, 1.0f);
	}

	public override void _Process(double delta)
	{
		if (!IsInstanceValid(_alvo)) { QueueFree(); return; }

		_t += delta;
		GlobalPosition = _alvo.GlobalPosition;

		Vector2 tela = GetViewportRect().Size;
		_matOnda.SetShaderParameter("proporcao", tela.Y > 0 ? tela.X / tela.Y : 1.777f);
		_matOnda.SetShaderParameter("centro", CentroNaTela());

		if (_t < Carga) Carregar();
		else if (_t < Estouro) Explodir();
		else if (_t < Fim) Assentar();
		else QueueFree();
	}

	/// <summary>A pressao juntando: luz crescente, tremor curto, poeira.</summary>
	private void Carregar()
	{
		float p = (float)(_t / Carga);

		// cresce em CURVA, nao em rampa: quase nada nos primeiros terços e tudo no fim. E o que
		// faz o estouro parecer inevitavel em vez de repentino.
		float e = p * p * p;
		_luz.Energy = e * (1.2f + _tier * 0.4f);
		_matPilar.SetShaderParameter("intensidade", e * 0.8f);
		_matPilar.SetShaderParameter("tempo", (float)_t);
		_pilar.Size = new Vector2(120, 120 + 260 * e);
		_pilar.Position = new Vector2(-60, -_pilar.Size.Y + 20);

		_matOnda.SetShaderParameter("raio", 0f);
		_matOnda.SetShaderParameter("brilho", 0f);
		_matOnda.SetShaderParameter("forca", 0f);

		// tremor CURTO e rapido -- ainda nao e o tranco, e o chao rachando
		World.Instancia?.Sacudir(1.5f + _tier * 0.8f, e);
	}

	/// <summary>O estouro: clarao, onda que entorta a tela, tranco da camera.</summary>
	private void Explodir()
	{
		if (!_estourou)
		{
			_estourou = true;
			World.Instancia?.Sacudir(9f + _tier * 3f, 1f);
			AudioDirector.EfeitoNoLugar(_alvo, Trilha.Acerto(3), 1.0f);
		}

		float p = (float)((_t - Carga) / (Estouro - Carga));

		_clarao.Color = new Color(1, 1, 1, (1f - p) * 0.75f);
		_luz.Energy = (3f + _tier) * (1f - p * 0.5f);

		_matOnda.SetShaderParameter("raio", p * 0.75f);
		_matOnda.SetShaderParameter("espessura", 0.10f - p * 0.05f);
		_matOnda.SetShaderParameter("forca", (1f - p) * (0.02f + _tier * 0.006f));
		_matOnda.SetShaderParameter("brilho", (1f - p) * 1.6f);

		_matPilar.SetShaderParameter("intensidade", 1.4f);
		_matPilar.SetShaderParameter("tempo", (float)_t);
	}

	/// <summary>A luz baixa ate o nivel em que a aura permanente assume.</summary>
	private void Assentar()
	{
		float p = (float)((_t - Estouro) / (Fim - Estouro));
		float q = 1f - p;

		_clarao.Color = new Color(1, 1, 1, 0);
		_matOnda.SetShaderParameter("raio", 0.75f + p * 0.6f);
		_matOnda.SetShaderParameter("forca", q * 0.004f);
		_matOnda.SetShaderParameter("brilho", q * 0.3f);

		_luz.Energy = q * (2f + _tier * 0.5f);
		_matPilar.SetShaderParameter("intensidade", q * 1.2f);
		_matPilar.SetShaderParameter("tempo", (float)_t);
		_pilar.Size = new Vector2(120, 380 * q + 60);
		_pilar.Position = new Vector2(-60, -_pilar.Size.Y + 20);

		if (_raios != null) _raios.Modulate = new Color(1, 1, 1, q);
	}

	private Vector2 CentroNaTela()
	{
		Viewport? vp = GetViewport();
		if (vp == null) return new Vector2(0.5f, 0.5f);
		Vector2 t = vp.GetScreenTransform() * (GetGlobalTransformWithCanvas().Origin);
		Vector2 tam = GetViewportRect().Size;
		return new Vector2(tam.X > 0 ? t.X / tam.X : 0.5f, tam.Y > 0 ? t.Y / tam.Y : 0.5f);
	}
}

/// <summary>
/// AS FAISCAS do SSJ2 pra cima. Segmentos quebrados que se refazem varias vezes por segundo.
///
/// Desenhadas em codigo, e nao com sprite, por um motivo pratico: o raio precisa nascer no
/// personagem e terminar em qualquer direcao, com comprimento variavel. Um sprite fixo daria
/// sempre o mesmo desenho, e raio que se repete deixa de ser raio.
/// </summary>
public partial class Raios : Node2D
{
	public Color Cor = Colors.White;
	public int Alcance = 110;

	private double _t;
	private readonly List<Vector2[]> _linhas = [];

	public override void _Ready() => ZIndex = -2;

	public override void _Process(double delta)
	{
		_t += delta;
		// 14 Hz: rapido o bastante pra ler como eletricidade, devagar o bastante pra o olho
		// pegar o desenho de cada raio
		if (_t < 1.0 / 14) return;
		_t = 0;
		Sortear();
		QueueRedraw();
	}

	private void Sortear()
	{
		_linhas.Clear();
		int quantos = GD.RandRange(2, 4);
		for (int i = 0; i < quantos; i++)
		{
			int nos = GD.RandRange(3, 5);
			var p = new Vector2[nos];
			float ang = GD.Randf() * Mathf.Tau;
			var atual = new Vector2(GD.Randf() * 10 - 5, GD.Randf() * 20 - 10);
			p[0] = atual;
			for (int k = 1; k < nos; k++)
			{
				// o angulo passeia pouco a cada no: raio anda quebrado, nao em ziguezague regular
				ang += GD.Randf() * 1.4f - 0.7f;
				float passo = Alcance / (float)nos * (0.6f + GD.Randf() * 0.8f);
				atual += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * passo;
				p[k] = atual;
			}
			_linhas.Add(p);
		}
	}

	public override void _Draw()
	{
		foreach (Vector2[] l in _linhas)
		{
			// duas passadas: um traco grosso e apagado por baixo (o halo) e um fino e branco por
			// cima (o nucleo). E o que faz a faisca parecer quente em vez de riscada a caneta.
			DrawPolyline(l, new Color(Cor, 0.35f), 5f, true);
			DrawPolyline(l, new Color(1, 1, 1, 0.9f), 1.6f, true);
		}
	}
}
