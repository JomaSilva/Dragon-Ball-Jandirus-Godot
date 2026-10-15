using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UMA COISA LARGADA NO CHAO, desenhada no mundo: um item, um monte de zeni ou uma bolsa de recursos (dono,
/// 2026-10-13: *"poder dropar itens e dinheiro ou recurso"*). A regra e as artes estao em `Core/Items/Chao.cs`; quem
/// planta e tira estes nodes e o `World.DesenharOChao`, a cada `S2C.Chao`.
///
/// Irma da <see cref="EsferaDesenhada"/>, e ancorada do mesmo jeito: o node fica no canto de baixo da celula (e por
/// ele que o Y-sort a intercala com os corpos) e o desenho sobe a partir dali, centrado na largura do tile.
///
/// O ZENI GIRA (a folha dele e uma moeda animada, como no original); o resto e um quadro parado -- e o mesmo retrato
/// que a mochila mostra (`Miniaturas`), porque a folha de uma peca de roupa e uma animacao de ANDAR, e uma camisa
/// largada nao anda.
/// </summary>
public partial class LargadoDesenhado : Node2D
{
	public int Id;
	public TipoDeLargado Tipo;
	public string Item = "";
	public double Quantidade;

	/// <summary>(2026-10-15) A folha no lugar da do item -- os pesos largados com a arte do dono deles. Vazia = a do catalogo.</summary>
	public string Arte = "";

	/// <summary>A folha que este node de fato desenhou. Pra bancada (`--diagv030`).</summary>
	public string ArteUsadaDeTeste { get; private set; } = "";

	/// <summary>A coisa achou o desenho dela? Falso = o quadradinho de reserva. Pra bancada.</summary>
	public bool ComArte { get; private set; }

	public override void _Ready()
	{
		YSortEnabled = true;
		const int t = ZoneCollision.TileSize;
		(string arte, string estado) = Arte.Length > 0 ? (Arte, PesoDeTreino.PoseDoIcone) : Chao.Arte(Tipo, Item);
		ArteUsadaDeTeste = arte;

		if (Tipo == TipoDeLargado.Zeni && arte.Length > 0 && ResourceLoader.Exists(arte) && FolhasPresas.Carregar(arte) is { } folha)
		{
			string anim = Miniaturas.NomeDaAnimacao(estado);
			if (folha.HasAnimation(anim) && folha.GetFrameCount(anim) > 0 && folha.GetFrameTexture(anim, 0) is { } quadro)
			{
				Vector2 tam = quadro.GetSize();
				var moeda = new AnimatedSprite2D
				{
					SpriteFrames = folha,
					Animation = anim,
					Centered = false,
					Position = new Vector2((t - tam.X) / 2f, -tam.Y),
				};
				AddChild(moeda);
				moeda.Play();
				ComArte = true;
				return;
			}
		}

		if (Miniaturas.De(arte, estado) is { } retrato)
		{
			Vector2 tam = retrato.GetSize();
			AddChild(new Sprite2D
			{
				Texture = retrato,
				Centered = false,
				Position = new Vector2((t - tam.X) / 2f, -tam.Y),
				TextureFilter = TextureFilterEnum.Nearest,
			});
			ComArte = true;
		}
	}

	/// <summary>SEM ARTE, UM QUADRADINHO: a coisa esta no chao e da pra pegar -- ela nao pode ser invisivel so porque a folha faltou.</summary>
	public override void _Draw()
	{
		if (ComArte) return;
		const int t = ZoneCollision.TileSize;
		DrawRect(new Rect2(t / 2f - 6, -t / 2f - 6, 12, 12), new Color(0.95f, 0.8f, 0.25f));
		DrawRect(new Rect2(t / 2f - 6, -t / 2f - 6, 12, 12), new Color(0.15f, 0.1f, 0f), filled: false, width: 1);
	}
}
