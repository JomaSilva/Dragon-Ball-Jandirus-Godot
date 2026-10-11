using System.Collections;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DAS FOTOS DE UM MUNDO SORTEADO (`--fotoprocedural`) -- a ponta de TELA das duas queixas do dono
/// de 2026-10-10 sobre os planetas procedurais:
///
///   * *"estao com esses blocos estranhos de madeira no mapa"* -- a montanha pintada com a janela das casas;
///   * *"sempre devem ter uma arvore frutifera (maça)"* -- a macieira perto de onde se chega.
///
/// Quem cobra a regra em milhares de mundos e a bancada de console (`procedural`, no AssetPipeline) e quem
/// cobra o funil do servidor e a `--pomarteste`. Este robo confere o que so a tela tem: o tile que o
/// `PlanetaProcedural` de fato PINTOU na celula de montanha, a macieira chegando pelo pacote de construcoes
/// e a maca caindo na mochila do CLIENTE pelo verbo da tecla E -- e fotografa.
///
/// O MUNDO E SEMPRE O MESMO, de proposito: o primeiro Jardim leve da seed de fabrica do universo
/// (`GameServer.SementeLegada`), e nao um do universo deste servidor (que a pasta de saves vazia da bancada
/// sorteia a cada rodada). As fotos do "antes" e do "depois" saem do mesmo lugar, celula por celula.
///
/// `--montanhadejanela` liga o DEFEITO INJETADO (`GeradorDeTerreno.MontanhaDeJanelaDeTeste`): a mesma cena
/// com a montanha de antes do conserto. As fotos saem com `-ANTES` no nome.
///
/// Rode com janela (no segundo monitor), no modo host:
///   --host --rede 8141 --horateste 0.5 --fotoprocedural --raca Human --conta bancada_procedural --nome Viajante
/// </summary>
public partial class RoboDeFotoDoProcedural : Node
{
	private const int T = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou, _antes;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[fotoprocedural]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[fotoprocedural]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Ready()
	{
		// ANTES de qualquer mundo sorteado ser pintado: o corpo ainda esta na Terra.
		_antes = Array.IndexOf(OS.GetCmdlineArgs(), "--montanhadejanela") >= 0;
		GeradorDeTerreno.MontanhaDeJanelaDeTeste = _antes;
		if (_antes) GD.Print("[fotoprocedural] DEFEITO INJETADO: a montanha vai ser pintada com a janela das casas (o jogo de antes)");
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			GD.Print($"\n[fotoprocedural] ===== {_ok} OK, {_falhou} FALHA(S) =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private bool Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[fotoprocedural] sem foto (headless nao renderiza): rode com janela"); return false; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}{(_antes ? "-ANTES" : "")}.png");
		img.SavePng(caminho);
		GD.Print($"[fotoprocedural] foto {caminho}");
		return true;
	}

	/// <summary>
	/// O MUNDO DAS FOTOS: o primeiro Jardim de gravidade leve (ate 2x: o corpo de bancada nao pode chegar
	/// esmagado) e de lado pequeno da seed de fabrica. Funcao pura -- as duas rodadas acham o mesmo.
	/// </summary>
	private static PlanetaNoEspaco? MundoDasFotos()
	{
		for (int i = 2; i <= 400; i++)
		{
			if (Sistemas.Do(GameServer.SementeLegada, i * 2, i * 3 + 1) is not { Ancorado: false } sistema) continue;
			foreach (PlanetaNoEspaco p in sistema.Planetas())
			{
				if (p.Premade) continue;
				MundoProcedural m = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (m.Bioma == BiomaDeTerreno.Jardim && m.Gravidade <= 2 && m.Lado <= 256) return p;
			}
		}
		return null;
	}

	/// <summary>Terra em que se anda e sem nada em cima.</summary>
	private static bool Livre(TerrenoGerado t, int x, int y) =>
		t.ClasseEm(x, y) is ClasseDeTerreno.Praia or ClasseDeTerreno.Planicie or ClasseDeTerreno.Colina
		&& t.CoberturaEm(x, y) == CoberturaDeTerreno.Nada && !t.Colisao.BlockedCell(x, y);

	/// <summary>
	/// ONDE POR O CORPO PRA FOTOGRAFAR A MONTANHA: o chao livre mais perto do pouso que tem a beirada de uma
	/// cordilheira tres tiles a DIREITA -- o enquadramento da foto do dono. Devolve tambem a celula de montanha.
	/// </summary>
	private static ((int X, int Y) Chao, (int X, int Y) Montanha)? MiranteDaMontanha(TerrenoGerado t)
	{
		for (int r = 4; r < Math.Max(t.Largura, t.Altura); r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
					int x = t.SpawnCelX + dx, y = t.SpawnCelY + dy;
					if (x < 8 || y < 8 || x >= t.Largura - 12 || y >= t.Altura - 8) continue;
					if (!Livre(t, x, y) || !Livre(t, x + 1, y) || !Livre(t, x + 2, y)) continue;

					// uma parede de montanha de verdade: a coluna x+3 e a x+4, cinco linhas de altura
					bool parede = true;
					for (int yy = y - 2; yy <= y + 2 && parede; yy++)
						parede = t.ClasseEm(x + 3, yy) == ClasseDeTerreno.Montanha && t.ClasseEm(x + 4, yy) == ClasseDeTerreno.Montanha;
					if (parede) return ((x, y), (x + 3, y));
				}
		return null;
	}

	/// <summary>O tile pintado nesta celula, em qualquer camada de CHAO do mundo montado. Nulo = nada pintado.</summary>
	private static (int Fonte, Vector2I Quadro, Color Tinta)? TilePintado(int cx, int cy)
	{
		foreach (TileMapLayer l in World.Instancia!.CamadasDoCenarioDeTeste)
		{
			if (!l.Name.ToString().StartsWith("Chao", StringComparison.Ordinal)) continue;
			var c = new Vector2I(cx, cy);
			int fonte = l.GetCellSourceId(c);
			if (fonte >= 0) return (fonte, l.GetCellAtlasCoords(c), l.Modulate);
		}
		return null;
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }

		if (MundoDasFotos() is not { } mundo) { Afirmar("(montagem) ha um Jardim leve e pequeno na seed de fabrica", false); yield break; }
		MundoProcedural ficha = MundoProcedural.DaSeed(mundo.Seed, mundo.Nome);
		ZoneKey zona = ZoneKey.Procedural(mundo.Nome, mundo.Seed);
		TerrenoGerado t = GeradorDeTerreno.Gerar(ficha.Parametros());
		GD.Print($"[fotoprocedural] o mundo das fotos: {mundo.Nome} (seed {mundo.Seed}) -- {ficha.Descricao()}; "
				 + $"pouso ({t.SpawnCelX},{t.SpawnCelY}), {t.Macieiras.Count} macieira(s) na seed");

		// ---- 1. POUSAR (o mundo nasce fora do tique: pede-se ate ele existir) ----
		for (int i = 0; i < 60 && !srv.ProceduralDeFoto_Pousar(cli.LocalId, mundo.Nome, mundo.Seed); i++) yield return 0.25;
		for (int i = 0; i < 40 && !cli.Zone.Equals(zona); i++) yield return 0.25;
		yield return 5.0;
		Afirmar($"o corpo pousou em {mundo.Nome} e o cliente montou o mundo", cli.Zone.Equals(zona), cli.Zone.ToString());
		if (!cli.Zone.Equals(zona)) yield break;
		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);

		// DIA NESTE PLANETA: a manivela e a da Terra, e cada mundo tem a defasagem dele (o mesmo passo do robo
		// das fotos dos pre-feitos).
		for (int i = 0; i < 20; i++)
		{
			double daqui = World.Instancia?.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
			if (daqui is >= 0.42 and <= 0.6) break;
			srv.CeuDaTerraDeTeste(i / 20.0);
			yield return 0.35;
		}
		yield return 1.0;

		// ---- 2. A CHEGADA: a macieira veio no pacote de construcoes, e esta perto ----
		Vector2 corpo = World.Instancia!.PosicaoLocal ?? Vector2.Zero;
		(int X, int Y) pouso = Jandirus.Core.Tech.CatalogoDeObras.Celula(corpo.X, corpo.Y);
		List<GameClient.ObraInfo> macieiras = [.. cli.Obras.Where(o => o.Tipo == CatalogoDeItens.ArvoreDeMaca)];
		int longe = macieiras.Count == 0 ? -1 : macieiras.Min(o =>
		{
			(int x, int y) = Jandirus.Core.Tech.CatalogoDeObras.Celula(o.Pos.X, o.Pos.Y);
			return Math.Max(Math.Abs(x - pouso.X), Math.Abs(y - pouso.Y));
		});
		Afirmar($"NO CLIENTE: as macieiras do mundo chegaram pelo pacote de construcoes, e uma esta a ate {Pomar.RaioDaChegada} tiles de onde o corpo pousou",
				macieiras.Count == t.Macieiras.Count && longe >= 1 && longe <= Pomar.RaioDaChegada
				&& pouso == (t.SpawnCelX, t.SpawnCelY),
				$"{macieiras.Count} no cliente, {t.Macieiras.Count} na seed; a mais perto a {longe} tile(s); pouso ({pouso.X},{pouso.Y})");
		if (Fotografar("procedural-01-chegada")) yield return 0.3;

		// ---- 3. COLHER PELO FIO: o corpo vai pro lado da arvore e o verbo da tecla E sai do cliente ----
		if (t.Macieiras.Count > 0)
		{
			(int ax, int ay) = t.Macieiras[0];
			(int X, int Y)? aoLado = null;
			foreach ((int dx, int dy) in new[] { (0, 1), (-1, 0), (1, 0), (0, -1) })
				if (aoLado == null && Livre(t, ax + dx, ay + dy) && !t.Macieiras.Contains((ax + dx, ay + dy))) aoLado = (ax + dx, ay + dy);

			if (aoLado is { } chao)
			{
				srv.ProceduralDeFoto_Levar(cli.LocalId, chao.X, chao.Y);
				yield return 1.5;
				int antes = cli.Mochila.Quantos(CatalogoDeItens.Maca);
				cli.SendVerbo("colher");
				yield return 1.5;
				Afirmar("COLHER PELO FIO: ao lado da macieira, o verbo da tecla E poe uma maca na mochila do cliente",
						cli.Mochila.Quantos(CatalogoDeItens.Maca) == antes + 1,
						$"macas {antes} -> {cli.Mochila.Quantos(CatalogoDeItens.Maca)}; corpo em ({chao.X},{chao.Y}), macieira em ({ax},{ay})");
				if (Fotografar("procedural-02-macieira")) yield return 0.3;
			}
			else Afirmar("(montagem) ha chao livre encostado na macieira da chegada", false);
		}
		else if (!_antes) Afirmar("(montagem) o mundo das fotos tem macieira", false);

		// ---- 4. A MONTANHA: o tile que a tela pintou, e a foto ----
		if (MiranteDaMontanha(t) is not { } mirante) { Afirmar("(montagem) ha uma parede de montanha com chao livre ao lado", false); yield break; }
		srv.ProceduralDeFoto_Levar(cli.LocalId, mirante.Chao.X, mirante.Chao.Y);
		yield return 4.0;

		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/tiles.json"));
		(int Fonte, int X, int Y)? rocha = tiles.Achar("Walls", "Wall13"), janela = tiles.Achar("Walls", "Wall1");
		(int Fonte, Vector2I Quadro, Color Tinta)? pintado = TilePintado(mirante.Montanha.X, mirante.Montanha.Y);
		string oque = pintado is { } p ? $"fonte {p.Fonte} quadro ({p.Quadro.X},{p.Quadro.Y}) tinta {p.Tinta.ToHtml(false)}" : "nada pintado";

		if (_antes)
			Afirmar("(defeito injetado: a montanha e a janela das casas) a celula de montanha esta pintada com a `Walls`/`Wall1`",
					pintado is { } a && janela is { } j && a.Fonte == j.Fonte && a.Quadro == new Vector2I(j.X, j.Y), oque);
		else
			Afirmar("**A MONTANHA NA TELA E A ROCHA**: a celula de montanha esta pintada com a `Walls`/`Wall13` (o desenho do penhasco do DM), e nao com a janela",
					pintado is { } a && rocha is { } r && a.Fonte == r.Fonte && a.Quadro == new Vector2I(r.X, r.Y), oque);
		Fotografar("procedural-03-montanha");
	}
}
