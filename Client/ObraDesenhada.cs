using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UMA CONSTRUCAO NO CHAO -- a bancada de pesquisa, o mainframe de androides.
///
/// ============================ AGORA ELA USA O SPRITE DO BYOND ============================
/// Ate aqui era um retangulo desenhado por codigo, e o comentario antigo dizia por que: "os icones
/// das 99 construcoes estao espalhados por dezenas de .dmi que o pipeline ainda nao converte".
/// Isso deixou de ser verdade -- o pipeline resolve 98 das 99 (`tech` no AssetPipeline), e ele
/// resolve o icone do que E ERGUIDO (`create_type`) e nao o do item de loja, que sao objetos
/// diferentes com propriedades diferentes.
///
/// A Research Bench, que o dono citou, tem CINCO QUADROS e 96 px de largura com `pixel_x = -32`.
/// O retangulo nao tinha como mostrar nem a animacao nem o tamanho.
/// =======================================================================================
///
/// O RETANGULO CONTINUA EXISTINDO como reserva: uma construcao sem .dmi convertido (hoje so a
/// Crafting_Bench) e melhor feia do que invisivel -- o jogador ergue uma coisa de meio milhao de
/// zeni e tem que ver onde ela caiu.
///
/// ENTRA NO Y-SORT como qualquer corpo, ancorada na BASE da celula que ela ocupa -- a mesma que o
/// servidor bloqueia (ver <see cref="CatalogoDeObras.Celula"/>). Ancorar no ponto solto onde o
/// jogador estava faria o desenho e a parede ficarem em lugares diferentes.
/// </summary>
public partial class ObraDesenhada : Node2D
{
	public string Tipo = "";
	public string Dono = "";
	public bool Aparafusada;
	public int Lab;

	/// <summary>res:// do SpriteFrames, vindo do servidor. Vazio = cai no retangulo.</summary>
	public string Arte = "";
	public string Estado = "";
	public Vector2 Pixel;

	/// <summary>Metade da largura do retangulo de reserva, em pixels.</summary>
	private const float Largura = 28f;
	private const float Altura = 34f;

	private AnimatedSprite2D? _sprite;

	public override void _Ready()
	{
		YSortEnabled = true;
		ZIndex = 0;
		MontarSprite();
	}

	/// <summary>
	/// O sprite do original, ancorado como o BYOND ancora: o canto INFERIOR ESQUERDO do icone
	/// encosta no canto inferior esquerdo do tile, mais o `pixel_x`/`pixel_y`.
	///
	/// O `pixel_y` inverte de sinal: no BYOND o Y cresce PRA CIMA e no Godot pra baixo. Copiar o
	/// numero cru poria a bancada meio tile enterrada no chao em vez de meio tile acima dele.
	/// </summary>
	private void MontarSprite()
	{
		if (Arte.Length == 0 || !ResourceLoader.Exists(Arte)) return;
		if (ResourceLoader.Load<SpriteFrames>(Arte) is not { } folha) return;

		string anim = Estado.Length > 0 ? Sanear(Estado) : "default";
		if (!folha.HasAnimation(anim))
		{
			// o .dmi pode nomear o estado de um jeito que o conversor saneou diferente; sem o
			// estado certo, o primeiro serve mais do que nada
			string[] nomes = [.. folha.GetAnimationNames()];
			if (nomes.Length == 0) return;
			anim = nomes[0];
		}

		if (folha.GetFrameTexture(anim, 0) is not { } quadro) return;
		Vector2 tam = quadro.GetSize();

		_sprite = new AnimatedSprite2D
		{
			SpriteFrames = folha,
			Animation = anim,
			Centered = false,
			// da ancora (a base da celula) pro canto superior esquerdo do desenho
			Position = new Vector2(Pixel.X, -tam.Y - Pixel.Y),
		};
		AddChild(_sprite);
		_sprite.Play();
	}

	/// <summary>Mesmo saneamento de nome que o `SpriteFramesWriter` aplicou ao converter.</summary>
	private static string Sanear(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		string r = sb.ToString().Trim('_');
		while (r.Contains("__")) r = r.Replace("__", "_");
		return r.Length == 0 ? "state" : r;
	}

	/// <summary>Cor por familia de construcao -- so a reserva usa.</summary>
	private Color Cor => Tipo switch
	{
		"Research_Station" => new Color(0.35f, 0.62f, 0.85f),
		"Fabricator" => new Color(0.55f, 0.75f, 0.45f),
		"Android_Creation_Mainframe" => Lab switch
		{
			1 => new Color(0.85f, 0.72f, 0.30f),   // Android Lab
			2 => new Color(0.55f, 0.80f, 0.45f),   // Bio-Android Lab
			_ => new Color(0.70f, 0.70f, 0.76f),
		},
		_ => new Color(0.62f, 0.60f, 0.68f),
	};

	public override void _Draw()
	{
		// SOLTA PISCA. Uma construcao nao aparafusada nao funciona, e descobrir isso so ao clicar
		// e a diferenca entre um jogo que ensina e um que esconde. Vale com sprite ou sem.
		Rect2 corpo = _sprite != null
			? new Rect2(_sprite.Position, TamanhoDoSprite())
			: new Rect2(-Largura, -Altura, Largura * 2, Altura);

		if (_sprite == null)
		{
			Color c = Cor;

			// SOMBRA NO CHAO. Sem ela a construcao parece flutuar -- e num jogo com Y-sort,
			// "parece flutuar" e indistinguivel de "esta na camada errada".
			DrawCircle(new Vector2(0, -2), Largura * 0.9f, new Color(0, 0, 0, 0.28f));

			DrawRect(corpo, c);
			DrawRect(corpo, c.Darkened(0.5f), filled: false, width: 2f);
			DrawRect(new Rect2(-Largura * 0.6f, -Altura * 0.85f, Largura * 1.2f, Altura * 0.4f),
					 c.Lightened(0.35f));
		}

		if (!Aparafusada)
		{
			float p = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 250f);
			DrawRect(corpo.Grow(3), new Color(1f, 0.85f, 0.35f, 0.25f + 0.35f * p), filled: false, width: 2f);
		}
	}

	private Vector2 TamanhoDoSprite()
	{
		if (_sprite?.SpriteFrames?.GetFrameTexture(_sprite.Animation, 0) is { } t) return t.GetSize();
		return new Vector2(ZoneCollision.TileSize, ZoneCollision.TileSize);
	}

	public override void _Process(double delta)
	{
		if (!Aparafusada) QueueRedraw();   // so a que pisca precisa de quadro novo
	}
}
