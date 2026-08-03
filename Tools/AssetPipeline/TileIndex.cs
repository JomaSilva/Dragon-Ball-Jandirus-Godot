using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// O QUE O INDICE PRECISA SABER DE UMA FONTE DO TILESET -- e so isso.
///
/// E um recorte do `MapConverter.Fonte`, que e privado de proposito (ele carrega os estados
/// crus do .dmi, os conjuntos de celulas densas/opacas, o total de quadros... nada disso e
/// assunto de quem so quer desenhar). Declarar a interface minima aqui em vez de abrir a
/// classe de la mantem o conversor livre pra mudar por dentro sem quebrar este arquivo.
/// </summary>
/// <param name="Id">O `sources/N` do tileset.tres -- o `Fonte.Id`.</param>
/// <param name="Chave">Caminho no DISCO do .png. E daqui que sai o NOME do atlas.</param>
/// <param name="ResPath">O `res://` da textura. So vai pro arquivo pra facilitar o log.</param>
/// <param name="IconW">Largura de um icone.</param>
/// <param name="IconH">Altura de um icone.</param>
/// <param name="Cols">Quantos icones cabem numa linha da folha.</param>
/// <param name="StateIndex">`icon_state` -> indice linear do PRIMEIRO quadro dele.</param>
public sealed record FonteDeAtlas(
	int Id, string Chave, string ResPath, int IconW, int IconH, int Cols,
	IReadOnlyDictionary<string, int> StateIndex);

/// <summary>
/// PUBLICA O INDICE DE TILES: `Assets/Data/tiles.json`.
///
/// POR QUE ISTO FALTAVA. O conversor de mapas ja calculava tudo -- o id da fonte, as colunas da
/// folha e a conta `(idx % cols, idx / cols)` que leva um `icon_state` ate a celula do atlas
/// (`MapConverter.Coord`). Ele so nunca escreveu isso em lugar nenhum: a conta acontecia dentro
/// da conversao, virava bytes de `tile_map_data` e morria ali. Resultado pratico: o gerador
/// procedural devolvia `TileVisual("Waters", "1")` e NADA no jogo sabia transformar esse par de
/// nomes num tile -- o terreno era gerado, era correto, e nao aparecia.
///
/// O ARQUIVO E IRMAO DO `tileset.tres` E TEM QUE SER GERADO JUNTO. Os ids sao atribuidos por
/// ordem de descoberta (`proxId++`), entao um `tiles.json` de uma conversao antiga ao lado de um
/// `tileset.tres` novo nao da erro: da o SPRITE ERRADO. E o mesmo modo de falha das casas de
/// Namek desenhadas com Namekuseijins, e ele nao acusa em teste nenhum.
///
/// FORMATO PLANO por contrato com o leitor (`Core/World/CatalogoDeTiles.cs`): ele fatia por
/// `{`..`}` de primeiro nivel, entao NENHUM bloco pode ter chave aninhada. O mapa de estados sai
/// como lista de texto `"estado=x,y"`.
/// </summary>
public static class TileIndex
{
	/// <summary>Quanto saiu, e o que colidiu de nome.</summary>
	/// <param name="Atlas">Quantas folhas entraram.</param>
	/// <param name="Estados">Quantos icon_state no total.</param>
	/// <param name="Colisoes">Um texto por nome disputado, ja dizendo quem ganhou.</param>
	public sealed record Resultado(int Atlas, int Estados, List<string> Colisoes);

	/// <summary>
	/// Monta e grava o indice.
	///
	/// O NOME DO ATLAS E O DO ARQUIVO, SEM PASTA E SEM EXTENSAO -- porque e assim que o DM fala
	/// (`icon = 'Waters.dmi'`), e assim que o `MapConverter` indexa os candidatos (`atlasPorNome`)
	/// e assim que as paletas do gerador escrevem ("Waters", "Red Rock", "Turfs1"). Casar por
	/// caminho inteiro seria pedir pro Core conhecer a arvore de pastas do BYOND.
	/// </summary>
	public static Resultado Escrever(string caminho, IEnumerable<FonteDeAtlas> fontes)
	{
		var porNome = new Dictionary<string, FonteDeAtlas>(StringComparer.OrdinalIgnoreCase);
		var colisoes = new List<string>();

		foreach (FonteDeAtlas f in fontes.OrderBy(f => f.Id))
		{
			string nome = NomeDoAtlas(f);
			if (nome.Length == 0) continue;

			if (!porNome.TryGetValue(nome, out FonteDeAtlas? ja)) { porNome[nome] = f; continue; }

			// DOIS .dmi COM O MESMO NOME EM PASTAS DIFERENTES. A arvore de icones tem 79 nomes
			// repetidos, e o `MapConverter` ja convive com isso resolvendo por icon_state -- mas
			// AQUI so ha o nome, porque o nome e tudo que o `TileVisual` carrega. Ganha a folha
			// com MAIS estados (a de cenario, quase sempre; a que colide costuma ser uma variante
			// de um estado so), e a perdedora vira linha de relatorio em vez de sumir calada.
			bool trocar = f.StateIndex.Count > ja.StateIndex.Count
						  || (f.StateIndex.Count == ja.StateIndex.Count
							  && string.CompareOrdinal(f.Chave, ja.Chave) < 0);
			FonteDeAtlas vencedor = trocar ? f : ja;
			FonteDeAtlas perdedor = trocar ? ja : f;
			porNome[nome] = vencedor;
			colisoes.Add($"{nome}: venceu {vencedor.Chave} ({vencedor.StateIndex.Count} estados), " +
						 $"fora {perdedor.Chave} ({perdedor.StateIndex.Count})");
		}

		int estados = 0;
		var sb = new StringBuilder("[\n");
		// ordem por NOME e nao por id: o arquivo e pra ser lido por gente, e um diff entre duas
		// conversoes so faz sentido se a ordem nao dancar quando um atlas novo entra no meio
		List<KeyValuePair<string, FonteDeAtlas>> ordenadas =
			[.. porNome.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)];

		for (int i = 0; i < ordenadas.Count; i++)
		{
			(string nome, FonteDeAtlas f) = (ordenadas[i].Key, ordenadas[i].Value);
			int cols = Math.Max(1, f.Cols);

			sb.Append("  { \"atlas\": \"").Append(Escapar(nome)).Append("\", ");
			sb.Append("\"fonte\": ").Append(f.Id).Append(", ");
			sb.Append("\"colunas\": ").Append(cols).Append(", ");
			sb.Append("\"iw\": ").Append(f.IconW).Append(", ");
			sb.Append("\"ih\": ").Append(f.IconH).Append(", ");
			sb.Append("\"res\": \"").Append(Escapar(f.ResPath)).Append("\", ");
			sb.Append("\"estados\": [");

			// em ordem de INDICE: o arquivo sai na mesma ordem em que a folha desenha, o que faz
			// um erro de grade (estado saltando linha) aparecer a olho nu
			bool primeiro = true;
			foreach ((string estado, int idx) in f.StateIndex.OrderBy(kv => kv.Value))
			{
				if (!primeiro) sb.Append(", ");
				primeiro = false;
				// A MESMA CONTA DO CONVERSOR (`MapConverter.Coord`). Ela e trivial e por isso mesmo
				// perigosa de reescrever: se as colunas daqui saissem de outro lugar que nao o
				// `SheetWidth / IconWidth` da fonte, o indice e o tileset apontariam pra celulas
				// diferentes do MESMO png -- e cada tile viria trocado por um vizinho.
				sb.Append('"').Append(Escapar(estado)).Append('=')
				  .Append(idx % cols).Append(',').Append(idx / cols).Append('"');
				estados++;
			}

			sb.Append("] }").Append(i + 1 < ordenadas.Count ? ",\n" : "\n");
		}
		sb.Append("]\n");

		string? pasta = Path.GetDirectoryName(Path.GetFullPath(caminho));
		if (pasta != null) Directory.CreateDirectory(pasta);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));

		return new Resultado(ordenadas.Count, estados, colisoes);
	}

	/// <summary>Nome do .png sem pasta e sem extensao; cai no `res://` se a chave vier vazia.</summary>
	private static string NomeDoAtlas(FonteDeAtlas f)
	{
		string n = Path.GetFileNameWithoutExtension(f.Chave);
		if (n.Length == 0) n = Path.GetFileNameWithoutExtension(f.ResPath);
		return n;
	}

	/// <summary>
	/// JSON nao aceita `"` nem `\` crus dentro de texto. Nome de arquivo do BYOND ja tem de tudo
	/// (`!!!  house furniture.dmi`), e um `icon_state` pode ter qualquer coisa -- escapar aqui e o
	/// que impede um nome exotico de partir o arquivo inteiro em duas metades ilegiveis.
	/// </summary>
	private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
