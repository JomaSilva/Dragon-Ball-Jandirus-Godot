using System.Text;

namespace Jandirus.Core.World;

/// <summary>
/// Uma FOLHA de tiles (um .dmi virado .png) do jeito que o `tileset.tres` a registrou.
///
/// O <see cref="Fonte"/> e o `sources/N` do TileSet -- e o primeiro dos tres numeros que o
/// TileMapLayer pede pra desenhar uma celula (fonte, x do atlas, y do atlas).
/// </summary>
public sealed class AtlasDeTiles
{
	/// <summary>Nome do .dmi SEM pasta e SEM extensao: "Waters", "Red Rock", "Turfs1".</summary>
	public string Nome = "";

	/// <summary>O `sources/N` do tileset.tres. Muda a cada reconversao -- ver a nota da classe.</summary>
	public int Fonte;

	/// <summary>Quantos icones cabem numa linha da folha. E o divisor que vira (x, y).</summary>
	public int Colunas = 1;

	public int LarguraDoIcone = 32;
	public int AlturaDoIcone = 32;

	/// <summary>O `res://` da textura. Nao e usado pra desenhar (o TileSet ja a carrega); serve pra log.</summary>
	public string ResPath = "";

	/// <summary>`icon_state` -> canto do PRIMEIRO quadro dele na grade do atlas.</summary>
	public Dictionary<string, (int X, int Y)> Estados = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// O INDICE DE TILES: `(atlas, icon_state)` -> `(fonte, x, y)`.
///
/// POR QUE ISTO EXISTE. O gerador procedural devolve <see cref="TileVisual"/>, que e um par de
/// NOMES ("Waters", "1") -- e nomes sao o unico jeito honesto de o Core falar de arte, porque o id
/// numerico da fonte e atribuido por ORDEM DE DESCOBERTA na conversao dos mapas
/// (`MapConverter.Garantir`, `proxId++`) e muda toda vez que alguem reconverte. Gravar o numero
/// dentro das paletas seria plantar um defeito com data marcada.
///
/// Sem este catalogo, porem, o nome nao virava nada: ninguem no jogo sabia traduzir "Waters"/"1"
/// pro trio que o TileMapLayer exige, e o terreno procedural era gerado e nao desenhado. O indice
/// e a ponte, e ele e GERADO pelo mesmo passo que escreve o tileset (`Tools/AssetPipeline/TileIndex`),
/// entao os ids nascem sempre casados com o `tileset.tres` que esta ao lado.
///
/// NAO HA QUADRO 0 DE CONSOLO. Estado que nao existe devolve NULO, de proposito. O conversor de
/// mapas tem o comportamento oposto (`Coord` cai no quadro 0 em silencio) e o preco disso ja foi
/// pago: as casas de Namek foram desenhadas com Namekuseijins porque o quadro 0 da folha errada e
/// um Namekuseijin. Quem chama aqui tem que decidir o que fazer com o buraco -- e a
/// <see cref="Conferir"/> existe justamente pra achar os buracos ANTES da tela.
///
/// FORMATO PLANO, e isto e requisito e nao gosto: o leitor fatia o arquivo por `{`..`}` de
/// primeiro nivel, entao uma chave ANINHADA quebraria o corte e todos os blocos dali pra frente
/// sairiam tortos EM SILENCIO. Por isso o mapa de estados vai como lista de texto
/// `"estado=x,y"` e nao como objeto.
/// </summary>
public sealed class CatalogoDeTiles
{
	private readonly Dictionary<string, AtlasDeTiles> _porNome = new(StringComparer.OrdinalIgnoreCase);

	public int Total => _porNome.Count;
	public int TotalDeEstados => _porNome.Values.Sum(a => a.Estados.Count);
	public IEnumerable<AtlasDeTiles> Todos => _porNome.Values;

	/// <summary>A folha inteira, se ela existir. Nulo quando o atlas nao esta no tileset.</summary>
	public AtlasDeTiles? Atlas(string? nome) =>
		nome == null ? null : _porNome.GetValueOrDefault(Normalizar(nome));

	/// <summary>
	/// A RESPOSTA QUE O JOGO PEDE: onde desenhar `(atlas, estado)`.
	///
	/// Nulo quer dizer uma de duas coisas, e as duas sao problema de dado (falta a folha, ou a
	/// folha nao tem esse estado). Quem quiser saber QUAL das duas chama <see cref="Atlas"/>.
	/// </summary>
	public (int Fonte, int X, int Y)? Achar(string? atlas, string? estado)
	{
		AtlasDeTiles? a = Atlas(atlas);
		if (a == null) return null;
		return a.Estados.TryGetValue(estado ?? "", out (int X, int Y) c) ? (a.Fonte, c.X, c.Y) : null;
	}

	/// <summary>A mesma busca direto do que o gerador de terreno devolve.</summary>
	public (int Fonte, int X, int Y)? Achar(TileVisual v) => Achar(v.Atlas, v.Estado);

	/// <summary>
	/// Nome robusto: sem pasta, sem extensao, sem diferenca de caixa.
	///
	/// O `TileVisual.Atlas` do gerador guarda "Waters"; o DM escreve `icon = 'Waters.dmi'`; o
	/// pipeline guarda o caminho inteiro no disco. Os tres tem que cair no mesmo balde, senao a
	/// busca falha por um ponto e uma barra.
	///
	/// SO CORTA EXTENSAO CONHECIDA, e isto ja me mordeu: cortar "o que vem depois do ultimo ponto"
	/// parece equivalente e nao e -- ha um atlas chamado `Tiles 1.21.2011.dmi`, e a regra generica
	/// o registrava como "Tiles 1.21". O catalogo continuava com 125 entradas e a contagem batia;
	/// so a BUSCA por esse nome e que nunca mais acharia nada.
	/// </summary>
	private static string Normalizar(string nome)
	{
		int barra = nome.LastIndexOfAny(['/', '\\']);
		if (barra >= 0) nome = nome[(barra + 1)..];

		foreach (string ext in Extensoes)
			if (nome.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
				return nome[..^ext.Length];
		return nome;
	}

	/// <summary>As unicas extensoes que um nome de atlas pode trazer: o .dmi do DM e o .png do port.</summary>
	private static readonly string[] Extensoes = [".dmi", ".png"];

	// =====================================================================
	// CONFERENCIA
	// =====================================================================
	/// <summary>O veredito de UM tile pedido por uma paleta.</summary>
	/// <param name="Bioma">Qual das seis paletas pediu.</param>
	/// <param name="Papel">Agua, Praia, Arvore... o campo da paleta.</param>
	/// <param name="Pedido">O que a paleta escreveu.</param>
	/// <param name="Resolve">Virou (fonte, x, y)?</param>
	/// <param name="Usado">Este papel chega a aparecer neste bioma? (planta com PlantaPct 0 nao.)</param>
	/// <param name="Motivo">Vazio quando resolve; senao "atlas ausente" ou "estado ausente".</param>
	/// <param name="Sugestao">O nome mais parecido que EXISTE -- atlas ou estado, conforme o motivo.</param>
	public readonly record struct Veredito(
		BiomaDeTerreno Bioma, string Papel, TileVisual Pedido,
		bool Resolve, bool Usado, string Motivo, string Sugestao);

	/// <summary>
	/// CONFERE AS SEIS PALETAS DO GERADOR contra o que o tileset realmente tem.
	///
	/// E o que separa "gerei o mapa" de "gerei um mapa que da pra ver". Um planeta procedural com a
	/// montanha faltando nao quebra nada: ele so nasce com buracos no chao, e o unico sinal e o
	/// jogador achando o mundo esquisito. Aqui a falta vira lista, com o nome mais proximo do lado
	/// pra dizer se foi erro de digitacao ou arte que nunca entrou no tileset.
	///
	/// O <see cref="Veredito.Usado"/> separa os dois tamanhos do problema: a paleta do Gelado
	/// declara uma planta e logo abaixo diz `PlantaPct = 0`. Se essa nao resolver, ninguem ve --
	/// e continuar contando como falha esconde as que importam no meio do ruido.
	/// </summary>
	public List<Veredito> Conferir()
	{
		var saida = new List<Veredito>();
		foreach (BiomaDeTerreno b in Enum.GetValues<BiomaDeTerreno>())
		{
			PaletaDeBioma p = GeradorDeTerreno.Paleta(b);

			// A LISTA E ESCRITA A MAO, nao tirada por reflexao: reflexao pegaria qualquer campo
			// novo sem saber se ele e desenhavel, e a ordem dela mudaria com a ordem da classe.
			(string Papel, TileVisual V, bool Usado)[] papeis =
			[
				("Agua", p.Agua, true),
				("Praia", p.Praia, true),
				("Planicie", p.Planicie, true),
				("Colina", p.Colina, true),
				("Montanha", p.Montanha, true),
				("Arvore", p.Arvore, p.VegetacaoPct > 0),
				("ArvoreAcento", p.ArvoreAcento, p.VegetacaoPct > 0),
				("Planta", p.Planta, p.PlantaPct > 0),
				("Minerio", p.Minerio, p.MinerioPct > 0),
				("Gema", p.Gema, p.MinerioPct > 0),
			];

			foreach ((string papel, TileVisual v, bool usado) in papeis)
				saida.Add(Julgar(b, papel, v, usado));
		}
		return saida;
	}

	private Veredito Julgar(BiomaDeTerreno b, string papel, TileVisual v, bool usado)
	{
		AtlasDeTiles? a = Atlas(v.Atlas);
		if (a == null)
			return new Veredito(b, papel, v, false, usado, "atlas ausente",
				MaisParecido(_porNome.Keys, Normalizar(v.Atlas)));

		if (a.Estados.ContainsKey(v.Estado))
			return new Veredito(b, papel, v, true, usado, "", "");

		return new Veredito(b, papel, v, false, usado, "estado ausente",
			MaisParecido(a.Estados.Keys, v.Estado));
	}

	/// <summary>
	/// O candidato mais proximo por distancia de edicao. Serve pra dizer, no relatorio, se o
	/// pedido foi erro de digitacao ("Turfs1" x "Turfs 1") ou arte que simplesmente nao existe.
	/// Empate desempata por ordem ordinal, senao o relatorio mudaria de conteudo a cada execucao.
	/// </summary>
	private static string MaisParecido(IEnumerable<string> candidatos, string alvo)
	{
		string melhor = "";
		int menor = int.MaxValue;
		foreach (string c in candidatos)
		{
			int d = Distancia(c, alvo);
			if (d < menor || (d == menor && string.CompareOrdinal(c, melhor) < 0))
			{
				menor = d;
				melhor = c;
			}
		}
		return melhor;
	}

	/// <summary>Levenshtein em duas linhas, sem caixa. So relatorio -- nao entra em geracao.</summary>
	private static int Distancia(string a, string b)
	{
		a = a.ToLowerInvariant();
		b = b.ToLowerInvariant();
		var ante = new int[b.Length + 1];
		var atual = new int[b.Length + 1];
		for (int j = 0; j <= b.Length; j++) ante[j] = j;

		for (int i = 1; i <= a.Length; i++)
		{
			atual[0] = i;
			for (int j = 1; j <= b.Length; j++)
			{
				int custo = a[i - 1] == b[j - 1] ? 0 : 1;
				atual[j] = Math.Min(Math.Min(atual[j - 1] + 1, ante[j] + 1), ante[j - 1] + custo);
			}
			(ante, atual) = (atual, ante);
		}
		return ante[b.Length];
	}

	// =====================================================================
	// LEITURA
	// =====================================================================
	/// <summary>
	/// Le o `tiles.json`. Arquivo ausente/vazio devolve catalogo VAZIO em vez de estourar: sem
	/// indice o planeta procedural nao desenha, mas o jogo abre e a <see cref="Conferir"/> diz por que.
	/// </summary>
	public static CatalogoDeTiles Parse(string json)
	{
		var cat = new CatalogoDeTiles();
		foreach (string bloco in Blocos(json))
		{
			var a = new AtlasDeTiles
			{
				Nome = Normalizar(Str(bloco, "atlas")),
				Fonte = Num(bloco, "fonte", 0),
				Colunas = Math.Max(1, Num(bloco, "colunas", 1)),
				LarguraDoIcone = Num(bloco, "iw", 32),
				AlturaDoIcone = Num(bloco, "ih", 32),
				ResPath = Str(bloco, "res"),
			};
			if (a.Nome.Length == 0) continue;

			foreach (string item in Lista(bloco, "estados"))
			{
				// "estado=x,y" cortado no ULTIMO '=': o icon_state pode ter '=' dentro (raro), e o
				// par de coordenadas nunca tem. Cortar no primeiro '=' perderia esses nomes calado.
				int ig = item.LastIndexOf('=');
				if (ig < 0) continue;
				string estado = item[..ig];
				string coord = item[(ig + 1)..];
				int virg = coord.IndexOf(',');
				if (virg < 0) continue;
				if (!int.TryParse(coord[..virg], out int x)) continue;
				if (!int.TryParse(coord[(virg + 1)..], out int y)) continue;
				a.Estados[estado] = (x, y);
			}

			cat._porNome[a.Nome] = a;
		}
		return cat;
	}

	/// <summary>Cada `{ ... }` de primeiro nivel. O formato e PLANO -- ver a nota da classe.</summary>
	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	/// <summary>
	/// Acha a CHAVE, nao o texto solto: procura `"chave":` com os dois pontos colados.
	/// Sem isso, um atlas chamado "res" faria a busca da chave "res" cair no VALOR de "atlas".
	/// </summary>
	private static int Chave(string bloco, string chave) =>
		bloco.IndexOf($"\"{chave}\":", StringComparison.Ordinal);

	private static string Str(string bloco, string chave)
	{
		int i = Chave(bloco, chave);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', i + chave.Length + 3);
		return a < 0 ? "" : LerTexto(bloco, a + 1, out _);
	}

	private static int Num(string bloco, string chave, int padrao)
	{
		int i = Chave(bloco, chave);
		if (i < 0) return padrao;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is ' ' or '-')) b++;
		return int.TryParse(bloco[a..b].Trim(), out int v) ? v : padrao;
	}

	/// <summary>
	/// A lista de textos de uma chave.
	///
	/// Anda caractere a caractere em vez de procurar o `]` com IndexOf porque um `icon_state` pode
	/// ter colchete dentro (o BYOND aceita), e cortar no primeiro `]` truncaria a lista sem avisar.
	/// O fim e o `]` que aparece FORA das aspas.
	/// </summary>
	private static List<string> Lista(string bloco, string chave)
	{
		var l = new List<string>();
		int i = Chave(bloco, chave);
		if (i < 0) return l;
		int k = bloco.IndexOf('[', i);
		if (k < 0) return l;

		for (k++; k < bloco.Length; k++)
		{
			if (bloco[k] == ']') break;
			if (bloco[k] != '"') continue;
			l.Add(LerTexto(bloco, k + 1, out int fim));
			k = fim;
		}
		return l;
	}

	/// <summary>Le um texto entre aspas a partir de <paramref name="i"/>, respeitando `\`.</summary>
	private static string LerTexto(string s, int i, out int fim)
	{
		var sb = new StringBuilder();
		while (i < s.Length && s[i] != '"')
		{
			if (s[i] == '\\' && i + 1 < s.Length) i++;
			sb.Append(s[i++]);
		}
		fim = i;
		return sb.ToString();
	}
}
