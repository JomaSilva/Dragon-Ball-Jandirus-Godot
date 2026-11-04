using System.Collections;
using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO VENTO NAS COPAS (`--diagvento`, o pedido 226, 2026-11-02). O dono: *"vegetaçao dos planetas poderiam balançar
/// (baseando-se no clima) pra simular vento, mas seria a parte superior deles pq se balancar a arvore toda o tronco vai balancar e ficar
/// restranho, entao mire mais na parte das folhas mesma ideia q o terraria usa"*.
///
/// No mundo das fotos do procedural (o primeiro Jardim leve da seed de fabrica: o mesmo em toda rodada), o corpo para ao lado de uma
/// arvore SOZINHA e o robo fotografa a tela em quadros seguidos, comparando cada um com o primeiro. O desenho exato da arvore na tela sai
/// de apagar a celula dela por dois quadros (o que muda e ela), e a altura do pe da copa, da mesma medida da arte que o jogo usa:
/// As coberturas vizinhas da arvore saem da TELA durante a medida (so o desenho: elas balancariam junto e sujariam a faixa do tronco).
///   V1. a arvore esta pintada com o material do vento (`VentoNaVegetacao`), e o pe da copa medido na arte deixa um TRONCO embaixo;
///   V2. NA BRISA (ceu limpo): a COPA muda de um quadro pro outro e o TRONCO nao muda nada -- fotos `vento-1-brisa-NN`;
///   V3. CONTRA-EXEMPLO (`VentoNaVegetacao.ParadoDeTeste`, o jogo de antes): a copa para -- a regua de V2 reprovaria;
///   V4. NA NEVASCA: o vento das copas sobe com o clima, o alto da copa vai MAIS longe que na brisa (o deslocamento contra a tela em
///       repouso no mesmo clima: a nevasca lava as cores, e a contagem de pixels mudados nao serve) e o tronco segue parado. A medida e com o desenho
///       do clima escondido (a neve e o veu mudariam todo pixel), e a nevasca e a escolhida por nao ter raio (o clarao clareia a tela);
///   V5. (so foto) a TEMPESTADE com a chuva na tela -- fotos `vento-2-tempestade-NN`.
///
///     Godot --path . --janela --resolution 1280x720 --host --rede 12971 --horateste 0.5 --semfauna --diagvento --raca Human --conta bancada_vento --nome Ventania
/// </summary>
public partial class RoboDoVento : Node
{
	private const int T = ZoneCollision.TileSize;

	/// <summary>Quantos quadros seguidos se comparam, e de quanto em quanto tempo.</summary>
	private const int Quadros = 16;
	private const double Intervalo = 0.15;

	/// <summary>A diferenca (no canal que mais mudou) a partir da qual um pixel conta como mudado.</summary>
	private const float Limiar = 0.1f;

	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[vento]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[vento]   FALHA {nome}   [{detalhe}]"); }
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
			VentoNaVegetacao.ParadoDeTeste = false;
			GD.Print(_falhou == 0 ? $"\n[vento] ===== TUDO OK ===== ({_ok} OK)" : $"\n[vento] ===== {_falhou} FALHA(S) ===== ({_ok} OK)");
			GD.Print("[vento] ===== FIM =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	/// <summary>O mesmo mundo do `RoboDeFotoDoProcedural`: o primeiro Jardim de gravidade leve e lado pequeno da seed de fabrica.</summary>
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
	/// A ARVORE MAIS SOZINHA perto do pouso (a r1 pedia uma sem NENHUMA vizinha num raio de quatro tiles, e a mata do Jardim nao tem): a
	/// arvore com menos cobertura na janela em que o desenho de uma vizinha cairia por cima do dela (tres tiles pra cada lado, quatro pra
	/// cima, tres pra baixo), e com chao livre pro corpo parar fora do desenho. As vizinhas que sobram na janela sao APAGADAS DA TELA durante
	/// a medida (`ApagarVizinhas`): elas balancariam junto e sujariam a faixa do tronco. So o desenho -- o mapa e o servidor nao mudam.
	/// </summary>
	private static (int X, int Y, int CorpoX, int CorpoY, List<Vector2I> Vizinhas)? ArvoreMaisSozinha(TerrenoGerado t)
	{
		(int X, int Y, int CX, int CY, List<Vector2I> V)? melhor = null;
		for (int r = 3; r < 40; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
					int x = t.SpawnCelX + dx, y = t.SpawnCelY + dy;
					if (x < 10 || y < 10 || x >= t.Largura - 10 || y >= t.Altura - 10) continue;
					if (t.CoberturaEm(x, y) != CoberturaDeTerreno.Arvore) continue;
					// (r3) SEM AGUA NA JANELA: a agua e tile animado, e a r2 leu a tela inteira mudando (a pegada saiu do tamanho da tela)
					bool agua = false;
					for (int yy = y - 5; yy <= y + 4 && !agua; yy++)
						for (int xx = x - 4; xx <= x + 4 && !agua; xx++)
							agua = t.ClasseEm(xx, yy) == ClasseDeTerreno.Agua;
					if (agua) continue;
					(int X, int Y)? corpo = null;
					foreach ((int cx, int cy) in new[] { (x - 3, y + 1), (x + 3, y + 1), (x - 3, y), (x + 3, y), (x, y + 3) })
						if (corpo == null && Livre(t, cx, cy)) corpo = (cx, cy);
					if (corpo is not { } cc) continue;
					var vizinhas = new List<Vector2I>();
					for (int yy = y - 4; yy <= y + 3; yy++)
						for (int xx = x - 3; xx <= x + 3; xx++)
							if ((xx != x || yy != y) && t.CoberturaEm(xx, yy) != CoberturaDeTerreno.Nada) vizinhas.Add(new Vector2I(xx, yy));
					if (melhor == null || vizinhas.Count < melhor.Value.V.Count) melhor = (x, y, cc.X, cc.Y, vizinhas);
				}
		return melhor is { } m ? (m.X, m.Y, m.CX, m.CY, m.V) : null;
	}

	/// <summary>As vizinhas apagadas da tela pra medida, com o que tinha em cada uma (pra repor).</summary>
	private readonly List<(TileMapLayer Camada, Vector2I Celula, int Fonte, Vector2I Quadro)> _apagadas = [];

	/// <summary>Apaga da tela as coberturas destas celulas (so o desenho), guardando o que tinha.</summary>
	private void ApagarVizinhas(List<Vector2I> celulas)
	{
		foreach (Vector2I c in celulas)
			if (TileDaArvore(c) is { } t)
			{
				_apagadas.Add((t.Camada, c, t.Fonte, t.Quadro));
				t.Camada.EraseCell(c);
			}
	}

	/// <summary>Repoe as coberturas apagadas.</summary>
	private void ReporVizinhas()
	{
		foreach ((TileMapLayer camada, Vector2I c, int fonte, Vector2I quadro) in _apagadas)
			if (IsInstanceValid(camada)) camada.SetCell(c, fonte, quadro);
		_apagadas.Clear();
	}

	/// <summary>A camada de cobertura que tem tile pintado nesta celula, com a fonte e o quadro dele.</summary>
	private static (TileMapLayer Camada, int Fonte, Vector2I Quadro)? TileDaArvore(Vector2I c)
	{
		foreach (TileMapLayer l in World.Instancia!.CamadasDoCenarioDeTeste)
		{
			if (!IsInstanceValid(l) || !l.Name.ToString().StartsWith("Cobertura", StringComparison.Ordinal)) continue;
			int fonte = l.GetCellSourceId(c);
			if (fonte >= 0) return (l, fonte, l.GetCellAtlasCoords(c));
		}
		return null;
	}

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	/// <summary>Quantos pixels do retangulo mudaram de uma tela pra outra (no canal que mais mudou).</summary>
	private static int Diferentes(Image a, Image b, Rect2I r)
	{
		int n = 0;
		int x0 = Math.Max(0, r.Position.X), y0 = Math.Max(0, r.Position.Y);
		int x1 = Math.Min(Math.Min(a.GetWidth(), b.GetWidth()), r.End.X), y1 = Math.Min(Math.Min(a.GetHeight(), b.GetHeight()), r.End.Y);
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
				if (Math.Max(Math.Abs(ca.R - cb.R), Math.Max(Math.Abs(ca.G - cb.G), Math.Abs(ca.B - cb.B))) > Limiar) n++;
			}
		return n;
	}

	/// <summary>O retangulo (na tela) dos pixels que mudaram de uma tela pra outra; nulo se nada mudou.</summary>
	private static Rect2I? Pegada(Image a, Image b, Rect2I onde)
	{
		int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
		for (int y = Math.Max(0, onde.Position.Y); y < Math.Min(a.GetHeight(), onde.End.Y); y++)
			for (int x = Math.Max(0, onde.Position.X); x < Math.Min(a.GetWidth(), onde.End.X); x++)
			{
				Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
				if (Math.Max(Math.Abs(ca.R - cb.R), Math.Max(Math.Abs(ca.G - cb.G), Math.Abs(ca.B - cb.B))) <= Limiar) continue;
				x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
			}
		return x1 < 0 ? null : new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
	}

	/// <summary>O desenho do clima (o que cai e o veu) some ou volta -- so pra medir a copa sem a neve passando na frente.</summary>
	private void ClimaNaTelaVisivel(bool visivel)
	{
		if (GetTree().Root.FindChild("Clima", true, false) is not ClimaNaTela clima) return;
		clima.Visible = visivel;
		foreach (Node n in clima.GetChildren())
			if (n is CanvasLayer camada) camada.Visible = visivel;
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }
		if (MundoDasFotos() is not { } mundo) { Afirmar("(montagem) ha um Jardim leve e pequeno na seed de fabrica", false); yield break; }

		MundoProcedural ficha = MundoProcedural.DaSeed(mundo.Seed, mundo.Nome);
		ZoneKey zona = ZoneKey.Procedural(mundo.Nome, mundo.Seed);
		TerrenoGerado t = GeradorDeTerreno.Gerar(ficha.Parametros());

		// ---- POUSAR ----
		for (int i = 0; i < 60 && !srv.ProceduralDeFoto_Pousar(cli.LocalId, mundo.Nome, mundo.Seed); i++) yield return 0.25;
		for (int i = 0; i < 40 && !cli.Zone.Equals(zona); i++) yield return 0.25;
		yield return 4.0;
		Afirmar($"(montagem) o corpo pousou em {mundo.Nome} e o cliente montou o mundo", cli.Zone.Equals(zona), cli.Zone.ToString());
		if (!cli.Zone.Equals(zona)) yield break;
		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		for (int i = 0; i < 20; i++)
		{
			double daqui = World.Instancia?.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
			if (daqui is >= 0.42 and <= 0.6) break;
			srv.CeuDaTerraDeTeste(i / 20.0);
			yield return 0.35;
		}

		// ---- A ARVORE MAIS SOZINHA, E O CORPO AO LADO DELA (as vizinhas dela saem da tela durante a medida) ----
		if (ArvoreMaisSozinha(t) is not { } arv) { Afirmar("(montagem) ha uma arvore com chao livre do lado perto do pouso", false); yield break; }
		var celula = new Vector2I(arv.X, arv.Y);
		srv.ProceduralDeFoto_Levar(cli.LocalId, arv.CorpoX, arv.CorpoY);
		yield return 3.0;
		ApagarVizinhas(arv.Vizinhas);
		GD.Print($"[vento] a arvore em ({arv.X},{arv.Y}), o corpo em ({arv.CorpoX},{arv.CorpoY}), {arv.Vizinhas.Count} vizinha(s) apagada(s) da tela");
		yield return 0.5;

		// ---- V1: O MATERIAL E O TRONCO ----
		if (TileDaArvore(celula) is not { } tile) { Afirmar("(montagem) a celula da arvore esta pintada numa camada de cobertura", false); yield break; }
		var atlas = tile.Camada.TileSet.GetSource(tile.Fonte) as TileSetAtlasSource;
		TileData? dado = atlas?.GetTileData(tile.Quadro, 0);
		bool doVento = dado?.Material is ShaderMaterial sm && sm.Shader?.ResourcePath == VentoNaVegetacao.CaminhoDoShader;
		(int Topo, int Pivo, int Base)? copa = null;
		if (atlas?.Texture?.GetImage() is { } arte)
		{
			if (arte.IsCompressed()) arte.Decompress();
			Vector2I reg = atlas.TextureRegionSize;
			int ox = tile.Quadro.X * reg.X, oy = tile.Quadro.Y * reg.Y;
			copa = Vento.MedirCopa((x, y) => arte.GetPixel(ox + x, oy + y).A8 > Vento.AlfaOpaco, reg.X, reg.Y);
		}
		Afirmar("V1. a arvore esta pintada com o MATERIAL DO VENTO, e o pe da copa medido na arte deixa um TRONCO embaixo",
				doVento && copa is { } c1 && c1.Base - c1.Pivo > Vento.SemTroncoPx && VentoNaVegetacao.QuadrosComVento > 0,
				$"{atlas?.Texture?.ResourcePath} quadro {tile.Quadro}; material do vento {doVento}; copa {copa}; "
				+ $"{VentoNaVegetacao.QuadrosComVento} quadro(s) com vento");
		if (copa is not { } cp) yield break;

		// ---- O DESENHO DA ARVORE NA TELA: parada, com e sem a celula ----
		VentoNaVegetacao.ParadoDeTeste = true;
		yield return 0.5;
		Image? comArvore = Tela();
		tile.Camada.EraseCell(celula);
		yield return 0.4;
		Image? semArvore = Tela();
		tile.Camada.SetCell(celula, tile.Fonte, tile.Quadro);
		VentoNaVegetacao.ParadoDeTeste = false;
		yield return 0.5;
		if (comArvore == null || semArvore == null) { Afirmar("(montagem) a tela fotografa (rode com janela)", false); yield break; }
		Rect2I tela = new(0, 0, comArvore.GetWidth(), comArvore.GetHeight());
		// (r3) A PEGADA SO E PROCURADA EM VOLTA DE ONDE A ARVORE E DESENHADA (a celula na tela, pelo `GetGlobalTransformWithCanvas` da
		// camada, com folga de um tile de arte): a r2 procurou na tela inteira e achou tudo o que se mexe nela (a agua animada).
		Rect2I perto = tela;
		if (atlas != null && dado != null)
		{
			Vector2 tam = atlas.TextureRegionSize;
			var r0 = new Rect2(tile.Camada.MapToLocal(celula) - tam / 2 - (Vector2)dado.TextureOrigin, tam);
			Transform2D xf = tile.Camada.GetGlobalTransformWithCanvas();
			Vector2 a0 = xf * r0.Position, a1 = xf * r0.End;
			float um = (a1.X - a0.X) / tam.X * ZoneCollision.TileSize;
			perto = new Rect2I((int)(a0.X - um), (int)(a0.Y - um), (int)(a1.X - a0.X + 2 * um), (int)(a1.Y - a0.Y + 2 * um)).Intersection(tela);
		}
		if (Pegada(comArvore, semArvore, perto) is not { } pegada) { Afirmar("(montagem) apagar a celula muda a tela (a arvore esta na tela)", false); yield break; }

		// A ESCALA (px de tela por px de arte) e as faixas: a copa do topo ate 2 px de arte acima do pe; o tronco de 2 px abaixo do pe
		// ate a base. Nos lados, folga de 8 px de arte -- a copa pode sair da pegada parada quando verga.
		float s = pegada.Size.Y / (float)(cp.Base - cp.Topo + 1);
		int folga = (int)(8 * s);
		int yPe = pegada.Position.Y + (int)((cp.Pivo - cp.Topo) * s);
		var faixaCopa = new Rect2I(pegada.Position.X - folga, pegada.Position.Y, pegada.Size.X + 2 * folga, Math.Max(1, yPe - (int)(2 * s) - pegada.Position.Y));
		var faixaTronco = new Rect2I(pegada.Position.X - folga, yPe + (int)(2 * s), pegada.Size.X + 2 * folga,
									 Math.Max(1, pegada.End.Y - yPe - (int)(2 * s)));
		// (r4) O ALTO DA COPA: o terco de cima dela, onde o peso da fileira e grande; e o maior deslocamento procurado (dez px de arte)
		var altoDaCopa = new Rect2I(pegada.Position.X, pegada.Position.Y, pegada.Size.X, Math.Max(2, (int)((cp.Pivo - cp.Topo) * s / 3)));
		int maximo = (int)(10 * s);
		var recorte = new Rect2I(pegada.Position.X - 6 * folga, pegada.Position.Y - 3 * folga, pegada.Size.X + 12 * folga, pegada.Size.Y + 5 * folga)
			.Intersection(tela);
		GD.Print($"[vento] a arvore na tela: procurada em {perto}, pegada {pegada}, escala {s:0.##}, copa {faixaCopa}, tronco {faixaTronco}");
		comArvore.GetRegion(tela).SavePng(ProjectSettings.GlobalizePath("user://vento-0-arvore.png"));

		// ---- V2: NA BRISA ----
		var brisa = new List<Image>();
		for (int i = 0; i < Quadros; i++) { if (Tela() is { } img) brisa.Add(img); yield return Intervalo; }
		(int copaBrisa, int troncoBrisa) = Somar(brisa, faixaCopa, faixaTronco);
		for (int i = 0; i < brisa.Count; i++) brisa[i].GetRegion(recorte).SavePng(ProjectSettings.GlobalizePath($"user://vento-1-brisa-{i:00}.png"));
		double ventoBrisa = VentoNaVegetacao.VentoAgora;
		int balancoBrisa = Balanco(comArvore, brisa, altoDaCopa, maximo);   // o repouso da brisa: a tela com o vento parado, de ceu limpo
		Afirmar("V2. **NA BRISA (ceu limpo) A COPA BALANCA E O TRONCO NAO**: entre os quadros a copa muda e o tronco nao muda nada",
				copaBrisa > 20 && troncoBrisa == 0 && Math.Abs(ventoBrisa - Vento.Brisa) < 0.02,
				$"copa {copaBrisa} px mudados, tronco {troncoBrisa}; vento {ventoBrisa:0.###} ({brisa.Count} quadros)");

		// ---- V3: O CONTRA-EXEMPLO ----
		VentoNaVegetacao.ParadoDeTeste = true;
		yield return 0.4;
		var parada = new List<Image>();
		for (int i = 0; i < Quadros / 2; i++) { if (Tela() is { } img) parada.Add(img); yield return Intervalo; }
		VentoNaVegetacao.ParadoDeTeste = false;
		(int copaParada, int troncoParado) = Somar(parada, faixaCopa, faixaTronco);
		Afirmar("V3. CONTRA-EXEMPLO (defeito injetado: a vegetacao parada, o jogo de antes): a copa NAO muda -- a regua de V2 reprovaria",
				copaParada <= 2 && troncoParado == 0, $"copa {copaParada}, tronco {troncoParado}");

		// ---- V4: NA NEVASCA, COM O DESENHO DO CLIMA ESCONDIDO ----
		srv.ForcarClima(zona, TipoDeClima.Limpo, 0);
		srv.ForcarClima(zona, TipoDeClima.Nevasca, 3600, 1.0, "bancada");
		for (int i = 0; i < 180 && VentoNaVegetacao.VentoAgora < Vento.DoTipo(TipoDeClima.Nevasca) - 0.005; i++) yield return 0.5;
		double ventoNevasca = VentoNaVegetacao.VentoAgora;
		ClimaNaTelaVisivel(false);
		VentoNaVegetacao.ParadoDeTeste = true;
		yield return 1.0;
		Image? repousoDaNevasca = Tela();
		VentoNaVegetacao.ParadoDeTeste = false;
		yield return 0.5;
		var nevasca = new List<Image>();
		for (int i = 0; i < Quadros; i++) { if (Tela() is { } img) nevasca.Add(img); yield return Intervalo; }
		ClimaNaTelaVisivel(true);
		(int copaNevasca, int troncoNevasca) = Somar(nevasca, faixaCopa, faixaTronco);
		int balancoNevasca = repousoDaNevasca == null ? 0 : Balanco(repousoDaNevasca, nevasca, altoDaCopa, maximo);
		Afirmar("V4. **O VENTO SEGUE O CLIMA**: na nevasca o vento das copas sobe, o alto da copa vai MAIS longe que na brisa, e o tronco segue parado",
				ventoNevasca > Vento.Brisa + 0.5 && balancoNevasca >= balancoBrisa + 2 && balancoNevasca * 2 >= balancoBrisa * 3 && troncoNevasca == 0,
				$"vento {ventoNevasca:0.###} (brisa {ventoBrisa:0.###}); o alto da copa ate {balancoNevasca} px de lado (brisa {balancoBrisa}); "
				+ $"copa {copaNevasca} px mudados (brisa {copaBrisa}), tronco {troncoNevasca}");

		// ---- V5: A TEMPESTADE, SO FOTO (com a chuva) ----
		srv.ForcarClima(zona, TipoDeClima.Limpo, 0);
		srv.ForcarClima(zona, TipoDeClima.Tempestade, 3600, 1.0, "bancada");
		for (int i = 0; i < 180 && VentoNaVegetacao.VentoAgora < 0.95; i++) yield return 0.5;
		for (int i = 0; i < Quadros; i++)
		{
			Tela()?.GetRegion(recorte).SavePng(ProjectSettings.GlobalizePath($"user://vento-2-tempestade-{i:00}.png"));
			yield return Intervalo;
		}
		GD.Print($"[vento] (foto) a tempestade: vento {VentoNaVegetacao.VentoAgora:0.###}");
		srv.ForcarClima(zona, TipoDeClima.Limpo, 0);
		ReporVizinhas();
	}

	/// <summary>
	/// (r4) O QUANTO A FAIXA DO ALTO DA COPA ESTA DE LADO neste quadro, contra o quadro em repouso (o vento parado, NO MESMO CLIMA): o
	/// deslocamento horizontal, em px de tela, que deixa a menor diferenca entre os dois. A r3 media o vento pela CONTAGEM de pixels mudados,
	/// e a nevasca lava as cores (o ar claro dela): a copa balancava mais e mudava menos pixels acima do limiar.
	/// </summary>
	private static int Deslocamento(Image repouso, Image quadro, Rect2I faixa, int maximo)
	{
		int melhor = 0;
		double menor = double.MaxValue;
		for (int d = -maximo; d <= maximo; d++)
		{
			double soma = 0;
			int n = 0;
			for (int y = Math.Max(0, faixa.Position.Y); y < Math.Min(quadro.GetHeight(), faixa.End.Y); y++)
				for (int x = Math.Max(maximo, faixa.Position.X); x < Math.Min(quadro.GetWidth() - maximo, faixa.End.X); x++)
				{
					Color a = quadro.GetPixel(x, y), b = repouso.GetPixel(x - d, y);
					soma += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
					n++;
				}
			double media = n == 0 ? double.MaxValue : soma / n;
			if (media < menor - 1e-9 || (Math.Abs(media - menor) <= 1e-9 && Math.Abs(d) < Math.Abs(melhor))) { menor = media; melhor = d; }
		}
		return melhor;
	}

	/// <summary>O MAIOR deslocamento da faixa do alto da copa nos quadros, contra o repouso.</summary>
	private static int Balanco(Image repouso, List<Image> quadros, Rect2I faixa, int maximo) =>
		quadros.Count == 0 ? 0 : quadros.Max(q => Math.Abs(Deslocamento(repouso, q, faixa, maximo)));

	/// <summary>Os pixels mudados de cada quadro contra o primeiro, somados: na faixa da copa e na do tronco.</summary>
	private static (int Copa, int Tronco) Somar(List<Image> quadros, Rect2I copa, Rect2I tronco)
	{
		int nc = 0, nt = 0;
		for (int i = 1; i < quadros.Count; i++)
		{
			nc += Diferentes(quadros[0], quadros[i], copa);
			nt += Diferentes(quadros[0], quadros[i], tronco);
		}
		return (nc, nt);
	}
}
