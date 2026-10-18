using Godot;

namespace Jandirus.Client;

/// <summary>
/// (v0.4.0) A ESFERA VERDE DO DEMONIC SAIYAN -- os 15 s parado em que o Lendario Primal carrega (a lista do amigo do dono:
/// *"Activating this skill creates a green orb visual effect around the player, signaling the start of a charge-up"*).
///
/// ============================ A BARREIRA DE ENERGIA, ESVERDEADA -- e a camera de quem esta perto treme (dono, 2026-10-17) ============================
/// O dono, vendo a primeira versao (um circulo desenhado a mao): *"n seria melhor usar essas barreiras de energia no demonic saiyan so
/// q esverdeadas e fazer a camera de todos perto tremer?"*. A arte e a `Blasts/ElectroShield` (quatro quadros de 128 px de um disco
/// eletrico), e as duas decisoes dela sao estas:
///   * a MISTURA ADITIVA e o que a esverdeia sem tapar o boneco: o disco e cinza escuro e quase opaco, e somado com a tinta verde ele
///     vira um brilho verde por cima do corpo, com os raios do desenho em verde vivo. Em mistura normal ele seria uma tampa;
///   * o TREMOR e o ronco da carga alta de Ki (`World.TremerPelaCarga`): cada tela sacode a PROPRIA camera pela distancia ate quem
///     carrega, entao todo mundo perto sente -- quem carrega, mais. A esfera o reacende a cada quadro enquanto esta acesa, e ao apagar
///     ele cai sozinho em menos de meio segundo.
/// ======================================================================================================================================
///
/// ============================ E DE NOITE ELA BRILHA (dono, 2026-10-17) ============================
/// *"faz essa barreira brilhar pra ser visivel a noite"*. A noite e um `CanvasModulate` que multiplica a cena inteira -- a soma aditiva
/// da barreira tambem --, e ela ficava um veu escuro. O jeito do jogo de uma coisa brilhar no escuro e o das auras de transformacao: uma
/// `PointLight2D` da cor dela, ATRAS do corpo, acesa so com a noite (`Iluminacao.ForcaDaNoite` -- o pedido do dono de que efeito de dia
/// nao estoure). Ela acende a barreira e o chao em volta, e o desenho ganha um brilho a mais junto (<see cref="BrilhoDaNoite"/>).
/// ================================================================================================
///
/// Quem a liga e desliga e o `World.MarcarEsferaDemoniaca`, pela `Ocupacao.CargaDemoniaca` do snapshot -- o mesmo molde do Kaio-ken:
/// estado do corpo, entao o dono e quem esta em volta a veem, e quem chega no meio da carga tambem. Desligada (a carga acendeu ou o
/// corpo andou), ela some em 0,25 s e sai da arvore sozinha.
/// </summary>
public partial class EsferaDemoniaca : Node2D
{
	public const string NomeDoNode = "EsferaDemoniaca";

	/// <summary>A barreira de energia do jogo -- o disco eletrico de quatro quadros.</summary>
	public const string FolhaDaBarreira = "res://Assets/Sprites/Blasts/ElectroShield.tres";

	/// <summary>O verde da linha Lendaria -- a aura do Full Power (a entrada `legendary` do catalogo).</summary>
	public static readonly Color Verde = new("2bff3a");

	/// <summary>
	/// QUANTO DOS 128 PX DA FOLHA: o corpo e um quadro de 32 centrado no node, e o disco o envolve com folga (~70 px, dois tiles e
	/// pouco).
	/// </summary>
	public const float Escala = 0.55f;

	/// <summary>O brilho que a noite soma no desenho (ate 2,6x no escuro) -- de dia ele fica no 1x, um veu verde que deixa ler o boneco.</summary>
	public const float BrilhoDaNoite = 1.6f;

	/// <summary>
	/// A LUZ DA NOITE: o raio da poca (em px de mundo -- uns quatro tiles e meio de diametro, a barreira e o chao em volta) e a energia no
	/// escuro. A energia fica no teto da luz das auras (1,3): a barreira e um efeito de 15 s que PRECISA ser lido de longe.
	/// </summary>
	public const int RaioDaLuz = 72;
	public const float EnergiaDaLuz = 1.3f;

	private AnimatedSprite2D? _barreira;
	private PointLight2D? _luz;
	private double _t, _alfa;

	/// <summary>Desligada: sumindo, e depois fora da arvore.</summary>
	public bool Apagando { get; private set; }

	/// <summary>
	/// (2026-10-17) A ESFERA DO GRITO DO DESABAFO (`Ocupacao.GritoDoDesabafo`): a mesma barreira verde -- o dono: *"uma esfera de energia
	/// verde parecendo com a do demonic em volta do personagem"* --, pulsando FURIOSA (maior e quase tres vezes mais rapida), que e o ki
	/// estourando enquanto ele grita. Quem a escreve e o `World.MarcarEsferaDemoniaca`.
	/// </summary>
	public bool Furiosa;

	/// <summary>Quanto dela esta na tela (0 a 1) -- a bancada `--diagesfera` le.</summary>
	internal double AlfaDeTeste => _alfa;

	/// <summary>A barreira desenhada -- a bancada `--diagesfera` confere a folha e a tinta.</summary>
	internal AnimatedSprite2D? BarreiraDeTeste => _barreira;

	/// <summary>A luz da noite -- a bancada `--diagesfera` confere que ela acende de noite e fica apagada de dia.</summary>
	internal PointLight2D? LuzDeTeste => _luz;

	public override void _Ready()
	{
		// ATRAS do corpo, como a luz da aura: uma luz por cima lava o sprite e apaga o desenho da roupa.
		_luz = new PointLight2D
		{
			Name = "Luz",
			Texture = Aura.Radial(RaioDaLuz),
			Color = Verde,
			Enabled = false,
			ShadowEnabled = true,   // parede segura a luz, como a da aura
			Energy = 0,
			ZIndex = -5,
		};
		AddChild(_luz);

		if (ResourceLoader.Load<SpriteFrames>(FolhaDaBarreira) is not { } folha)
		{
			GD.PushError($"[esfera] a folha da barreira nao carregou ({FolhaDaBarreira})");
			return;
		}
		_barreira = new AnimatedSprite2D
		{
			Name = "Barreira",
			SpriteFrames = folha,
			Scale = new Vector2(Escala, Escala),
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
			Modulate = new Color(Verde, 0),
		};
		AddChild(_barreira);
		_barreira.Play("default");
	}

	public override void _Process(double delta)
	{
		_t += delta;
		_alfa = Apagando ? Math.Max(0, _alfa - delta * 4) : Math.Min(1, _alfa + delta * 3);
		if (Apagando && _alfa <= 0)
		{
			QueueFree();
			return;
		}
		float noite = Iluminacao.ForcaDaNoite();
		if (_barreira != null)
		{
			float brilho = 1f + BrilhoDaNoite * noite;
			_barreira.Modulate = new Color(Verde.R * brilho, Verde.G * brilho, Verde.B * brilho, (float)_alfa);
			float pulso = Escala * (Furiosa ? 1.05f + 0.10f * Mathf.Sin((float)_t * 17f) : 1 + 0.04f * Mathf.Sin((float)_t * 6f));
			_barreira.Scale = new Vector2(pulso, pulso);
		}
		if (_luz != null)
		{
			// ZERO E ZERO (a regra da luz da aura): uma luz de energia quase nula nao aparece e custa uma passada por quadro.
			_luz.Enabled = noite > 0.01f;
			_luz.Energy = EnergiaDaLuz * noite * (float)_alfa;
		}
		// O RONCO: reaceso a cada quadro enquanto ela esta acesa (o `LevantarTremor` so sobe, entao varias esferas nao se somam).
		if (!Apagando) World.Instancia?.TremerPelaCarga(GlobalPosition);
	}

	/// <summary>A carga acabou (acendeu ou se desfez): a esfera some.</summary>
	public void Apagar() => Apagando = true;
}
