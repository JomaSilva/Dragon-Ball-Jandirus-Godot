using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO MOVEL SOBRE A CADEIRA (`--diagmobilia`).
///
/// A queixa do dono (2026-09-06): *"icones em posicao errada e ficando torto, como a cadeira em cima do
/// bank"*. No `.dmm` do castelo de Vegeta a cadeira e o banco moram na MESMA celula (122,214), e no
/// BYOND o banco -- criado depois -- cobre a cadeira. Aqui a cadeira e tile e o banco e um node
/// (`ObraDesenhada`), e o node ordenava pelo TOPO do sprite: ficava atras.
///
/// Nasce com `--mobiliateste` (o servidor poe o corpo dois tiles abaixo do banco), espera o chao
/// assentar, e mede: o node do banco existe na celula e ordena pela base; e, com janela, a FOTO -- o
/// pixel do centro da celula do banco tem a cor do banco, nao a da cadeira. A foto fica em
/// `user://mobilia-1-banco-e-cadeira.png` pra quem quiser olhar.
/// </summary>
public partial class RoboDeMobilia : Node
{
	private double _t;
	private bool _feito;
	private int _ok, _falhou;

	private void Conferir(bool cond, string nome, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[mobilia]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[mobilia]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Process(double delta)
	{
		_t += delta;
		if (_feito || World.Instancia is not { } w || w.PosicaoLocal == null || w.ZonaMontadaDeTeste == 0) return;
		if (_t < 3.0) return;   // o chao assentou e o pacote das obras chegou
		_feito = true;

		GD.Print("[mobilia] ================ O MOVEL SOBRE A CADEIRA (pedido do dono, 2026-09-06) ================");
		ObraDesenhada? banco = w.ObraDeTeste(122, 214);
		Conferir(banco != null, "o banco do castelo esta desenhado como construcao na celula (122,214)", banco?.Tipo ?? "nenhuma obra ali");
		Conferir(banco is { } b && !b.YSortEnabled, "o movel ordena pela BASE: o Y-sort NAO esta ligado no proprio node (era o que o punha atras da cadeira)");
		Conferir(banco is { } b2 && b2.ZIndex == 0, "...e sem z-index proprio: ele disputa a ordem com a cadeira e com quem passa, pela altura", $"{banco?.ZIndex}");

		TileMapLayer[] camadas = w.CamadasDoCenarioDeTeste;
		bool cadeiraNoTile = camadas.Length >= 3 && GodotObject.IsInstanceValid(camadas[2]) && camadas[2].GetCellSourceId(new Vector2I(122, 214)) >= 0;
		Conferir(cadeiraNoTile, "a cadeira continua la, como tile da camada `Objetos` na mesma celula (as duas coisas cabem na celula, como no original)");
		Conferir(w.Colisao is { } m && m.BlockedCell(122, 214) && !m.BlockedCell(119, 214),
			"a colisao: o banco (obj denso) bloqueia a celula; a cadeira sozinha (119,214) nao");
		Conferir(w.Colisao is { } m2 && Enumerable.Range(118, 6).All(x => !m2.BlockedCell(x, 215)),
			"a fileira de mesas por baixo do piso (118..123, 215) esta LIVRE no cliente (era a 'hitbox sem icone')");

		Fotografar(w);
		GD.Print($"[mobilia] ================ {_ok} OK, {_falhou} FALHA(S) ================");
	}

	private void Fotografar(World w)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			GD.Print("[mobilia]   --     sem foto (headless nao renderiza): a ordem de desenho fica pro olho do dono");
			return;
		}
		string caminho = ProjectSettings.GlobalizePath("user://mobilia-1-banco-e-cadeira.png");
		img.SavePng(caminho);
		GD.Print($"[mobilia]   foto   {caminho} ({img.GetWidth()}x{img.GetHeight()})");

		// O PIXEL DO CENTRO DA CELULA DO BANCO, contra a arte do banco e a da cadeira no mesmo ponto.
		const int t = ZoneCollision.TileSize;
		Vector2 centro = new(122 * t + t / 2f, 214 * t + t / 2f);
		Vector2 tela = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * centro;
		Color? visto = Media(img, tela, 3);
		Color? doBanco = CorDaArteDoBanco();
		Color? daCadeira = CorDoTileDaCadeira(w);
		if (visto is not { } v || doBanco is not { } cb || daCadeira is not { } cc)
		{
			GD.Print($"[mobilia]   --     sem cor pra comparar (visto={visto}, banco={doBanco}, cadeira={daCadeira})");
			return;
		}
		// A LUZ DO MUNDO ESCURECE TUDO por igual: comparar MATIZ e nao valor. O banco e cinza-azulado, a
		// cadeira e marrom -- a razao vermelho/azul separa os dois em qualquer claridade.
		float rv = Razao(v), rb = Razao(cb), rc = Razao(cc);
		Conferir(Math.Abs(rv - rb) < Math.Abs(rv - rc),
			"na FOTO, o centro da celula do banco tem a cor do banco, nao a da cadeira (o banco cobre a cadeira, como no BYOND)",
			$"visto R/B {rv:0.00} | banco {rb:0.00} | cadeira {rc:0.00}");
	}

	private static float Razao(Color c) => (c.R + 0.02f) / (c.B + 0.02f);

	private static Color? Media(Image img, Vector2 tela, int raio)
	{
		double r = 0, g = 0, b = 0; int n = 0;
		for (int dy = -raio; dy <= raio; dy++)
			for (int dx = -raio; dx <= raio; dx++)
			{
				int x = (int)tela.X + dx, y = (int)tela.Y + dy;
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				Color p = img.GetPixel(x, y);
				r += p.R; g += p.G; b += p.B; n++;
			}
		return n == 0 ? null : new Color((float)(r / n), (float)(g / n), (float)(b / n));
	}

	/// <summary>A cor media do miolo do quadro `compdown` do banco (a mesma arte do node).</summary>
	private static Color? CorDaArteDoBanco()
	{
		const string arte = "res://Assets/Sprites/Misc/Objects/Technology/tech.tres";
		if (!ResourceLoader.Exists(arte) || ResourceLoader.Load<SpriteFrames>(arte) is not { } folha) return null;
		if (!folha.HasAnimation("compdown") || folha.GetFrameTexture("compdown", 0) is not { } tex) return null;
		return MediaDoMiolo(tex.GetImage());
	}

	/// <summary>A cor media do miolo do tile da cadeira, lida do atlas da camada `Objetos` na celula.</summary>
	private static Color? CorDoTileDaCadeira(World w)
	{
		TileMapLayer[] camadas = w.CamadasDoCenarioDeTeste;
		if (camadas.Length < 3 || !GodotObject.IsInstanceValid(camadas[2]) || camadas[2].TileSet is not { } ts) return null;
		var cel = new Vector2I(122, 214);
		int fonte = camadas[2].GetCellSourceId(cel);
		if (fonte < 0 || ts.GetSource(fonte) is not TileSetAtlasSource atlas || atlas.Texture == null) return null;
		Rect2I r = atlas.GetTileTextureRegion(camadas[2].GetCellAtlasCoords(cel));
		Image? folha = atlas.Texture.GetImage();
		if (folha == null) return null;
		return MediaDoMiolo(folha.GetRegion(r));
	}

	/// <summary>A media dos pixels opacos do miolo (metade central) de um quadro.</summary>
	private static Color? MediaDoMiolo(Image? q)
	{
		if (q == null || q.IsEmpty()) return null;
		double r = 0, g = 0, b = 0; int n = 0;
		int w = q.GetWidth(), h = q.GetHeight();
		for (int y = h / 4; y < h * 3 / 4; y++)
			for (int x = w / 4; x < w * 3 / 4; x++)
			{
				Color p = q.GetPixel(x, y);
				if (p.A < 0.5f) continue;
				r += p.R; g += p.G; b += p.B; n++;
			}
		return n == 0 ? null : new Color((float)(r / n), (float)(g / n), (float)(b / n));
	}
}
