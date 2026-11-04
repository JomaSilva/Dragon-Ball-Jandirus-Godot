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
///
/// (2026-11-02, o pedido 223) O BLUE E O BLUE KAIO-KEN NOVOS, cada um ao lado do DE ANTES (a mesma classe com o desenho de antes,
/// `ChamaDeAura.DesenhoDeAntesDoBlueDeTeste`): as fotos de revisao do dono -- *"tire prints pra revisao, se ficar bom aplicamos em
/// outras auras"* --, de longe, de perto (`aura-lab-perto-*`) e em instantes (`aura-lab-perto-Blue-instante-*`,
/// `aura-lab-perto-Kaioken_Blue-instante-*`).
///
/// (2026-11-02, o pedido 223f) E AS OUTRAS DIVINAS NA LUZ, cada uma ao lado da DE ANTES -- o SSG, a Golden Perfect e o Rose (o Rose 2 e
/// o mesmo simbolo) --, o dono: *"Só as divinas"*. Com as reguas do desenho (a chama de cada corpo veste a luz, e a de antes o desenho
/// de antes) e de pixel (acima da cabeca, fora do corpo, a luz e CLARA e na cor da forma), e as fotos em instantes
/// (`aura-lab-perto-Deus-instante-*`, `-Golden-instante-*`, `-Rose-instante-*`).
///
/// (o pedido 223g) E MENORES QUE A DO BLUE -- o dono, vistas as fotos: *"Diminuir"* --, com a regua da area acesa (L5).
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

	/// <summary>(o pedido 223) Quantas reguas de pixel reprovaram -- o ciclo le as linhas `FALHA`.</summary>
	private int _falhas;

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas++;
	}

	/// <summary>Um corpo do palco: a forma (pela folha DO CATALOGO, e nao escrita aqui), o cabelo e se o Kaio-ken esta aceso.</summary>
	private readonly record struct Corpo(string Nome, string Forma, Rgb Cabelo, bool Kaioken, bool Antes = false);

	private static readonly Corpo[] Corpos =
	[
		// (o pedido 223) os dois novos e os dois de antes, na primeira fila
		new("Blue", "blue", new Rgb(60, 200, 255), false),
		new("Blue-antes", "blue", new Rgb(60, 200, 255), false, Antes: true),
		new("Kaioken+Blue", "blue", new Rgb(60, 200, 255), true),
		new("Kaioken+Blue-antes", "blue", new Rgb(60, 200, 255), true, Antes: true),
		new("BlueEvolution", "blue_evolution", new Rgb(40, 90, 230), false),
		// (o pedido 223f) as outras divinas na luz, cada uma ao lado da de antes, na segunda e na terceira fila
		new("Deus", "ssg", new Rgb(210, 40, 60), false),
		new("Deus-antes", "ssg", new Rgb(210, 40, 60), false, Antes: true),
		new("Golden", "golden_perfect", new Rgb(255, 200, 60), false),
		new("Golden-antes", "golden_perfect", new Rgb(255, 200, 60), false, Antes: true),
		new("SSJ", "ssj1", new Rgb(255, 222, 90), false),
		new("Rose", "rose", new Rgb(255, 110, 190), false),
		new("Rose-antes", "rose", new Rgb(255, 110, 190), false, Antes: true),
		new("Rose2", "rose2", new Rgb(255, 80, 170), false),
		new("Kaioken", Catalogo.IdBase, new Rgb(20, 20, 24), true),
	];

	/// <summary>(o pedido 223f) Os corpos na luz nova que ganham a regua de pixel (o Blue e o Blue Kaio-ken tem a deles, do 223).</summary>
	private static readonly string[] LuzesNovas = ["Deus", "Golden", "Rose", "Rose2"];

	/// <summary>(o pedido 223f) A chama de cada corpo, pelo nome: a regua do desenho le o que cada uma vestiu.</summary>
	private readonly Dictionary<string, ChamaDeAura> _chamas = new();

	/// <summary>(o pedido 223g) A area acesa da luz do Blue (<see cref="ContarAcesos"/>), a regua das outras: elas tem de ser menores.</summary>
	private int _acesosDoBlue;

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
				// (o pedido 223) CINCO POR FILA: dez corpos em duas filas, todos no quadro (com quatro, a terceira fila caia fora)
				// (o pedido 223f) e agora catorze em tres filas, a 140 uma da outra (a foto de perto nao pega a fila de cima), com a camera
				// de longe mais aberta (o `MontarOPalco`)
				var onde = new Vector2(Centro.X + (i % 5 - 2f) * 110f, Centro.Y + (i / 5 - 1f) * 140f + 20f);
				lugares.Add(onde);
				PorUmCorpo(Corpos[i], onde);
			}

			await Quadros(20);
			// (o pedido 223f) O KAIO-KEN POR FORA CONTINUA SO DO BLUE: as outras divinas vestem a luz, mas o desenho 4 e o do Blue Kaio-ken
			Conferir(ChamaDeAura.LevaOKaioken(FolhaDeAura.DeusFrio) && !ChamaDeAura.LevaOKaioken(FolhaDeAura.DeusQuente)
					 && !ChamaDeAura.LevaOKaioken(FolhaDeAura.DeusRosa) && !ChamaDeAura.LevaOKaioken(FolhaDeAura.DeusDourado),
					 "L4. (223f) o Kaio-ken por fora (o desenho 4) continua so do Blue: a luz das outras divinas nao o leva");
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
				// (o pedido 223) o Blue Kaio-ken novo e alto: a camera sobe mais pra ele caber
				_camera.Position = lugares[i] + new Vector2(0, Corpos[i].Kaioken && !Corpos[i].Antes && Corpos[i].Forma == "blue" ? -42 : -30);
				await Segundos(0.3f);
				await Foto($"aura-lab-perto-{Corpos[i].Nome.Replace('+', '_')}.png");
				MedirALuz(Corpos[i], lugares[i]);
			}
			// A MESMA CHAMA EM QUATRO INSTANTES: o desenho anda, e uma foto so nao diz isso. (o pedido 223) O Blue e o Blue Kaio-ken novos;
			// (o 223f) e as outras divinas na luz.
			foreach (string nome in (string[])["Blue", "Kaioken+Blue", "Deus", "Golden", "Rose"])
			{
				int qual = Array.FindIndex(Corpos, c => c.Nome == nome);
				_camera.Position = lugares[qual] + new Vector2(0, Corpos[qual].Kaioken ? -42 : -30);
				for (int k = 0; k < 4; k++)
				{
					await Segundos(0.12f);
					await Foto($"aura-lab-perto-{Corpos[qual].Nome.Replace('+', '_')}-instante-{k}.png");
				}
			}
		}
		catch (Exception e)
		{
			Nota("excecao: " + e);
		}

		// (r2) O VEREDITO QUE O CICLO LE (`===== TUDO OK` / `===== N FALHA`), e o FIM que a bancada espera
		Nota(_falhas == 0 ? "===== TUDO OK =====" : $"===== {_falhas} FALHA(S) =====");
		Nota("===== FIM =====");
		GetTree().Quit(0);
	}

	/// <summary>
	/// (o pedido 223) A REGUA DE PIXEL DA LUZ, na foto de perto que acabou de sair: um shader que nao compila deixa a chama invisivel
	/// e o laboratorio terminaria igual. No Blue, ao lado da cabeca (a 10 px do eixo, fora do corpo e dentro da luz), o pixel e CLARO e
	/// AZUL; no Blue Kaio-ken, ao lado do tronco a 18 px (fora da luz azul, que e menor, e dentro da chama vermelha), CLARO e
	/// VERMELHO de um dos dois lados (a beirada da chama ondula; a 23 px desde a r3). O fundo das fotos de perto e o escuro.
	///
	/// (o pedido 223f) E AS OUTRAS DIVINAS NA LUZ: o DESENHO de cada corpo (a chama veste a luz, e a de antes o desenho de antes) e os
	/// PIXELS acima da cabeca, fora do quadro de 32 do corpo (nem cabelo nem corpo): o miolo CLARO, e o pixel mais saturado da luz na cor
	/// da forma -- o SSG e a Golden QUENTES (o vermelho acima do azul), a Golden tambem com o verde acima do azul (o ouro), o Rose VIOLETA
	/// (o azul acima do verde). (r2) Uma coluna, e nao um ponto: o ponto da r1 caia no miolo, que e quase branco em todas.
	/// (o pedido 223g) E A LUZ NOVA MENOR QUE A DO BLUE: a area acesa em volta do corpo (<see cref="ContarAcesos"/>) abaixo de 85% da dele.
	/// </summary>
	private void MedirALuz(Corpo quem, Vector2 onde)
	{
		Image img = _tela.GetTexture().GetImage();
		Color Pixel(float x, float alturaNoCorpo) =>
			PixelNoMundo(img, onde + new Vector2(x, SpriteDeAura.LinhaDosPes - alturaNoCorpo));
		static string Cor(Color c) => $"#{c.ToHtml(false)}";
		if (quem.Nome != "Blue" && quem.Nome != "Kaioken+Blue")
		{
			MedirAsOutrasDivinas(quem, Pixel, Cor);
			if (!quem.Antes && Array.IndexOf(LuzesNovas, quem.Nome) >= 0)
			{
				int acesos = ContarAcesos(img, onde);
				Conferir(_acesosDoBlue > 0 && acesos < _acesosDoBlue * 0.85f,
						 $"L5. (223g) {quem.Nome}: a luz e MENOR que a do Blue (o dono: \"Diminuir\") -- {acesos} px de mundo acesos contra "
						 + $"{_acesosDoBlue} do Blue");
			}
			return;
		}
		if (quem.Nome == "Blue")
		{
			_acesosDoBlue = ContarAcesos(img, onde);
			Color e = Pixel(-10f, 22f), d = Pixel(10f, 22f);
			// (r2) CLARO E SEM PUXAR PRO VERMELHO: perto do corpo o clarao e quase branco (a r1 leu #edfcff de um lado e #61e1ff do outro)
			bool azul(Color c) => c.Luminance > 0.30f && c.B >= c.R;
			Conferir(azul(e) && azul(d),
					 $"(223) A LUZ DO BLUE esta DESENHADA: ao lado da cabeca, dos dois lados, o pixel e claro e azul [{Cor(e)} | {Cor(d)}]");
			return;
		}
		// (r3) a 23 px: a chama 1:1 tem +-26 nessa altura, e a luz azul por dentro chega a +-16 com as bolhas
		Color fe = Pixel(-23f, 12f), fd = Pixel(23f, 12f);
		bool vermelho(Color c) => c.Luminance > 0.25f && c.R > c.B + 0.10f;
		Conferir(vermelho(fe) || vermelho(fd),
				 $"(223) A CHAMA VERMELHA DO BLUE KAIO-KEN esta DESENHADA por fora da luz: ao lado do tronco, o pixel e claro e vermelho "
				 + $"[{Cor(fe)} | {Cor(fd)}]");
	}

	/// <summary>
	/// (o pedido 223g) QUANTOS PX DE MUNDO A LUZ ACENDE em volta do corpo, na foto de perto que acabou de sair: de 40 px de cada lado do
	/// eixo e de 2 abaixo dos pes a 60 acima, um ponto por px, aceso o que passa de 0,2 de luminancia (o fundo escuro fica em ~0,07; o
	/// rotulo do nome, abaixo dos pes, fica de fora). O corpo entra igual na conta de todos.
	/// </summary>
	private int ContarAcesos(Image img, Vector2 onde)
	{
		int n = 0;
		for (float x = -40f; x <= 40f; x += 1f)
			for (float y = -2f; y <= 60f; y += 1f)
				if (PixelNoMundo(img, onde + new Vector2(x, SpriteDeAura.LinhaDosPes - y)).Luminance > 0.2f) n++;
		return n;
	}

	/// <summary>(o pedido 223f) As reguas das outras divinas -- ver o <see cref="MedirALuz"/>.</summary>
	private void MedirAsOutrasDivinas(Corpo quem, Func<float, float, Color> pixel, Func<Color, string> cor)
	{
		ChamaDeAura chama = _chamas[quem.Nome];
		FolhaDeAura folha = Catalogo.Folha(Catalogo.Def(quem.Forma));
		string vestiu = chama.EstiloDeTeste?.ToString() ?? "nenhum";
		if (quem.Antes)
		{
			if (ChamaDeAura.EstiloDeAntes(folha) is not { } antes) return;
			Conferir(chama.EstiloDeTeste == antes, $"L3. (223f) {quem.Nome}: a chama veste o desenho de ANTES (desenho {vestiu}, o de antes {antes})");
			return;
		}
		if (Array.IndexOf(LuzesNovas, quem.Nome) < 0) return;
		Conferir(chama.EstiloDeTeste == ChamaDeAura.EstiloDaLuz,
				 $"L1. (223f) {quem.Nome}: a chama veste a LUZ do Blue (desenho {vestiu}; a luz e o {ChamaDeAura.EstiloDaLuz})");
		// A COLUNA ACIMA DA CABECA, dos dois lados do eixo, de 33 a 58 px acima dos pes: a luz tem de estar la -- o miolo CLARO --, e o
		// pixel mais SATURADO dela (a faixa ou o contorno, perto da beirada de cima, que ondula entre ~41 e ~52) na cor da forma. O miolo
		// sozinho nao diz a cor: e quase branco em todas (a r1 leu #ece6da no SSG). O fundo escuro e o brilho de fora ficam abaixo de 0,2.
		var coluna = new List<Color>();
		foreach (float x in (float[])[-3f, 3f])
			for (float y = 33f; y <= 58f; y += 0.5f)
				coluna.Add(pixel(x, y));
		bool acesa = coluna.Exists(p => p.Luminance > 0.6f);
		Color forte = coluna.Where(p => p.Luminance > 0.2f).DefaultIfEmpty(Colors.Black).MaxBy(p => p.S);
		bool rosa = folha == FolhaDeAura.DeusRosa, ouro = folha == FolhaDeAura.DeusDourado;
		bool naCor = rosa ? forte.B > forte.G + 0.10f && forte.R > forte.G
			: forte.R > forte.B + 0.15f && (!ouro || forte.G > forte.B + 0.15f);
		Conferir(acesa && naCor,
				 $"L2. (223f) {quem.Nome}: a luz esta DESENHADA na cor da forma -- acima da cabeca o miolo e claro ({acesa}) e o pixel mais "
				 + "saturado dela e " + (rosa ? "violeta" : ouro ? "dourado" : "quente") + $" ({cor(forte)})");
	}

	/// <summary>(o pedido 223) A cor do pixel da foto no ponto do MUNDO dado, pela camera de agora.</summary>
	private Color PixelNoMundo(Image img, Vector2 mundo)
	{
		Vector2 tela = (mundo - _camera.Position) * _camera.Zoom + new Vector2(Largura, Altura) / 2f;
		int x = Mathf.Clamp((int)tela.X, 0, img.GetWidth() - 1), y = Mathf.Clamp((int)tela.Y, 0, img.GetHeight() - 1);
		return img.GetPixel(x, y);
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
		// (o pedido 223f) 2,3 e nao 3: as tres filas de corpos cabem no quadro de longe
		_camera = new Camera2D { Name = "Camera", Position = Centro, Zoom = new Vector2(2.3f, 2.3f) };
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

		// (o pedido 223) a chama leva o Kaio-ken quando o desenho dela o leva (a luz do Blue), e ai o sprite vermelho fica mudo -- a
		// mesma conta do `Aura.Kaioken`; o DE ANTES e a chama do Blue de antes com o sprite por cima
		chama.DesenhoDeAntesDeTeste = c.Antes;
		_chamas[c.Nome] = chama;
		chama.DefinirFolha(folha, c.Kaioken && !c.Antes);
		chama.Definir(true, cor);
		bool spriteDoKaioken = c.Kaioken && (c.Antes || !ChamaDeAura.LevaOKaioken(folha));
		desenho.DefinirFolha(spriteDoKaioken ? FolhaDeAura.Kaioken : folha);
		desenho.Definir(true, spriteDoKaioken ? Aura.CorDoKaioken : cor);

		var vis = new CharacterVisual { Name = "Visual" };
		corpo.AddChild(vis);
		vis.Vestir(_cat, new Appearance { Cabelo = "Goku", CorCabelo = c.Cabelo }, "Saiyan", "Male");
		vis.SetMotion(Facing.South, false);

		var rotulo = new Label { Text = c.Nome, Position = new Vector2(-50, 22), Size = new Vector2(100, 10), HorizontalAlignment = HorizontalAlignment.Center };
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
