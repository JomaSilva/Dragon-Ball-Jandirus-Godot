using System.Threading.Tasks;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O LABORATORIO DA AURA (`--labaura`, v0.3.0, adendo 13): as chamas divinas por shader -- o Deus, o Blue, o Blue
/// Evolution, o Rose e o Rose 2 --, o Kaio-ken de SPRITE por cima do Blue (o pedido do dono: *"no blue kaioken seria a
/// aura shaders do blue + aura sprite do kaioken por cima"*) e, pra comparar, duas mortais que continuam de sprite (o
/// Super Saiyajin e o Kaio-ken na base). Num palco proprio, em fundo escuro e em fundo claro, de perto e de longe, com as
/// MESMAS classes do jogo (<see cref="ChamaDeAura"/> e <see cref="SpriteDeAura"/>), na mesma ordem de irmao do node
/// `Aura`. So FOTOGRAFA (`user://aura-lab-*.png`): e a mesa em que o desenho e comparado com as imagens do dono. Sem
/// rede, sem mundo; PRECISA de janela.
/// </summary>
public partial class RoboDoLaboratorioDaAura : Node
{
	private const int Largura = 1680, Altura = 900;
	private static readonly Vector2 Centro = new(400, 300);

	private SubViewport _tela = null!;
	private Node2D _palco = null!;
	private ColorRect _fundo = null!;
	private Camera2D _camera = null!;
	private VisualCatalog _cat = null!;

	private static void Nota(string s) => GD.Print("[labaura] " + s);

	/// <summary>Um corpo do palco: a forma (pela folha DO CATALOGO, e nao escrita aqui), o cabelo e se o Kaio-ken esta aceso.</summary>
	private readonly record struct Corpo(string Nome, string Forma, Rgb Cabelo, bool Kaioken);

	private static readonly Corpo[] Corpos =
	[
		new("Deus", "ssg", new Rgb(210, 40, 60), false),
		new("Blue", "blue", new Rgb(60, 200, 255), false),
		new("BlueEvolution", "blue_evolution", new Rgb(40, 90, 230), false),
		new("Rose", "rose", new Rgb(255, 110, 190), false),
		new("Kaioken+Blue", "blue", new Rgb(60, 200, 255), true),
		new("Rose2", "rose2", new Rgb(255, 80, 170), false),
		new("SSJ", "ssj1", new Rgb(255, 222, 90), false),
		new("Kaioken", Catalogo.IdBase, new Rgb(20, 20, 24), true),
	];

	public override async void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Nota("==== O LABORATORIO DA AURA ====");
		if (DisplayServer.GetName() == "headless" || VistosDeGente.Catalogo is not { } cat)
		{
			Nota("sem janela (ou sem visual.json) nao ha o que fotografar");
			Nota("===== FIM =====");
			GetTree().Quit(1);
			return;
		}
		_cat = cat;

		try
		{
			MontarOPalco();
			var lugares = new List<Vector2>();
			for (int i = 0; i < Corpos.Length; i++)
			{
				var onde = new Vector2(Centro.X + (i % 4 - 1.5f) * 140f, Centro.Y + (i / 4 - 0.5f) * 140f + 22f);
				lugares.Add(onde);
				PorUmCorpo(Corpos[i], onde);
			}

			await Quadros(20);
			foreach (float espera in (float[])[0.35f, 0.6f, 0.9f])
			{
				await Segundos(espera);
				await Foto($"aura-lab-escuro-{espera:0.00}.png");
			}

			_fundo.Color = new Color(0.36f, 0.42f, 0.27f);
			await Segundos(0.3f);
			await Foto("aura-lab-claro.png");

			_fundo.Color = new Color(0.06f, 0.07f, 0.10f);
			_camera.Zoom = new Vector2(6, 6);
			for (int i = 0; i < Corpos.Length; i++)
			{
				_camera.Position = lugares[i] + new Vector2(0, -30);
				await Segundos(0.3f);
				await Foto($"aura-lab-perto-{Corpos[i].Nome.Replace('+', '_')}.png");
			}
			// A MESMA CHAMA EM QUATRO INSTANTES: o desenho anda, e uma foto so nao diz isso.
			_camera.Position = lugares[1] + new Vector2(0, -30);
			for (int k = 0; k < 4; k++)
			{
				await Segundos(0.12f);
				await Foto($"aura-lab-perto-Blue-instante-{k}.png");
			}
		}
		catch (Exception e)
		{
			Nota("excecao: " + e);
		}

		Nota("===== FIM =====");
		GetTree().Quit(0);
	}

	private void MontarOPalco()
	{
		_tela = new SubViewport { Name = "PalcoDaAura", World2D = new World2D() };
		AddChild(_tela);
		_tela.Size = new Vector2I(Largura, Altura);
		_tela.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;
		_tela.TransparentBg = false;
		_tela.Disable3D = true;
		_tela.RenderTargetClearMode = SubViewport.ClearMode.Always;
		_tela.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		_palco = new Node2D { Name = "Palco" };
		_tela.AddChild(_palco);
		_fundo = new ColorRect
		{
			Name = "Fundo", Color = new Color(0.06f, 0.07f, 0.10f),
			Position = Centro - new Vector2(600, 400), Size = new Vector2(1200, 800), ZIndex = -100,
		};
		_palco.AddChild(_fundo);
		_camera = new Camera2D { Name = "Camera", Position = Centro, Zoom = new Vector2(3, 3) };
		_tela.AddChild(_camera);
	}

	/// <summary>
	/// O CORPO COMO O `World` O MONTA: um irmao `Aura` com a chama divina e o sprite (nessa ordem -- o sprite do Kaio-ken
	/// desenha por cima da chama), e o boneco depois. A folha sai do `Catalogo.Folha` e a cor do `Aura.CorDaChamaDe`, as
	/// mesmas contas do jogo; os dois desenhistas recebem o mesmo simbolo, e cada um desenha so o que e dele.
	/// </summary>
	private void PorUmCorpo(Corpo c, Vector2 onde)
	{
		var corpo = new Node2D { Name = "Corpo" + c.Nome.Replace('+', '_'), Position = onde };
		_palco.AddChild(corpo);

		FormaDef? d = Catalogo.Def(c.Forma);
		FolhaDeAura folha = Catalogo.Folha(d);
		Color cor = Aura.CorDaChamaDe(d, Aura.CorDoKiCru);

		var dono = new Node2D { Name = "Aura" };
		corpo.AddChild(dono);
		var chama = new ChamaDeAura { Name = "Chama" };
		dono.AddChild(chama);
		var desenho = new SpriteDeAura { Name = "Desenho" };
		dono.AddChild(desenho);

		chama.DefinirFolha(folha);
		chama.Definir(true, cor);
		desenho.DefinirFolha(c.Kaioken ? FolhaDeAura.Kaioken : folha);
		desenho.Definir(true, c.Kaioken ? Aura.CorDoKaioken : cor);

		var vis = new CharacterVisual { Name = "Visual" };
		corpo.AddChild(vis);
		vis.Vestir(_cat, new Appearance { Cabelo = "Goku", CorCabelo = c.Cabelo }, "Saiyan", "Male");
		vis.SetMotion(Facing.South, false);

		var rotulo = new Label { Text = c.Nome, Position = new Vector2(-40, 22), Size = new Vector2(80, 10), HorizontalAlignment = HorizontalAlignment.Center };
		rotulo.AddThemeFontSizeOverride("font_size", 7);
		corpo.AddChild(rotulo);
	}

	private async Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
	}

	private async Task Segundos(float s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async Task Foto(string nome)
	{
		await Quadros(2);
		Image img = _tela.GetTexture().GetImage();
		string caminho = ProjectSettings.GlobalizePath("user://" + nome);
		img.SavePng(caminho);
		Nota("foto " + caminho);
	}
}
