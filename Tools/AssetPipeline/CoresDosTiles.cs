using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// O COMANDO `cores`: publica a cor media de cada quadro de cada folha do tileset em
/// `Assets/Data/tiles-cores.json` -- o que o mapa da aba World usa pra pintar um pixel por tile.
///
/// POR QUE E UM PASSO DO PIPELINE: o cliente nao tem os pixels das folhas (elas vivem na placa de video,
/// e le-las de volta trava o quadro). Aqui os PNG estao no disco e a conta e uma passada.
///
/// O ARQUIVO E IRMAO DO `tiles.json` E SE REFAZ QUANDO ELE MUDA (conversao de mapas, folha nova, folha
/// redesenhada). Esquecer nao quebra o jogo: o tile sem cor cai na cor de reserva do mapa
/// (`MapaDoLugar.Reserva`) e o cliente avisa no log quais folhas ficaram pra tras. A bancada `mapa`
/// reprova enquanto houver celula de mapa pre-feito na reserva.
///
///     dotnet run --project Tools/AssetPipeline -- cores Assets/Data/tiles.json . Assets/Data/tiles-cores.json
/// </summary>
public static class CoresDosTiles
{
	/// <summary>Mede as folhas e grava o arquivo. Devolve quantas folhas ficaram de fora (PNG ilegivel ou ausente).</summary>
	public static int Escrever(string tilesJson, string raizDoProjeto, string saida)
	{
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(File.ReadAllText(tilesJson));
		var folhas = new List<FolhaDeCores>();
		var fora = new List<string>();
		int quadros = 0;

		foreach (AtlasDeTiles a in tiles.Todos.OrderBy(a => a.Nome, StringComparer.OrdinalIgnoreCase))
		{
			string png = Path.Combine(raizDoProjeto, a.ResPath.Replace("res://", "").Replace('/', Path.DirectorySeparatorChar));
			if (PngPixels.Ler(png) is not { } img || a.LarguraDoIcone <= 0 || a.AlturaDoIcone <= 0)
			{
				fora.Add($"{a.Nome} ({png})");
				continue;
			}

			// A GRADE E A DO CATALOGO (`colunas`), e nao uma deduzida aqui: e por ela que o `.pedacos`
			// enderecou cada quadro, e duas contas do mesmo numero sao duas chances de divergir.
			int colunas = a.Colunas, linhas = img.Altura / a.AlturaDoIcone;
			if (colunas * a.LarguraDoIcone > img.Largura || linhas <= 0)
			{
				fora.Add($"{a.Nome} (a grade {colunas} x {a.LarguraDoIcone} px nao cabe em {img.Largura} px)");
				continue;
			}

			var cores = new CorDeTile[colunas * linhas];
			for (int y = 0; y < linhas; y++)
				for (int x = 0; x < colunas; x++)
					cores[y * colunas + x] = CoresDeTiles.Media(img.Rgba, img.Largura, x * a.LarguraDoIcone,
																  y * a.AlturaDoIcone, a.LarguraDoIcone, a.AlturaDoIcone);

			folhas.Add(new FolhaDeCores { Atlas = a.Nome, Colunas = colunas, Linhas = linhas, Cores = cores });
			quadros += cores.Length;
		}

		string? pasta = Path.GetDirectoryName(Path.GetFullPath(saida));
		if (pasta != null) Directory.CreateDirectory(pasta);
		File.WriteAllText(saida, CoresDeTiles.ParaJson(folhas), new System.Text.UTF8Encoding(false));

		Console.WriteLine($"cores: {folhas.Count} folha(s), {quadros} quadro(s) -> {saida} ({new FileInfo(saida).Length / 1024} KB)");
		foreach (string f in fora) Console.WriteLine($"cores: FORA  {f}");
		return fora.Count;
	}
}
