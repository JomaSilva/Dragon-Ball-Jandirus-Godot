using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jandirus.Core.World;

/// <summary>
/// A COR DE UM TILE: a media dos pixels OPACOS do quadro, e quanto do quadro eles ocupam.
///
/// A `Cobertura` (0..255) existe por causa do que fica EM CIMA do chao: uma arvore ocupa um terco do
/// quadro dela, e pinta-la como um tile cheio transformaria toda floresta num borrao solido. No chao
/// ela so separa "tem desenho" de "quadro vazio".
/// </summary>
public readonly record struct CorDeTile(byte R, byte G, byte B, byte Cobertura);

/// <summary>As cores de UMA folha do tileset, quadro a quadro, em ordem de linha.</summary>
public sealed class FolhaDeCores
{
	/// <summary>O nome do atlas, do jeito que o <see cref="CatalogoDeTiles"/> o escreve ("Waters", "Turfs 1").</summary>
	public required string Atlas { get; init; }

	/// <summary>
	/// Quantos quadros por linha TINHA a folha quando as cores foram medidas. E a guarda de validade:
	/// folha redesenhada com outra grade muda este numero, e ai as cores guardadas sao de outros
	/// quadros (ver <see cref="CoresPorFonte.Montar"/>).
	/// </summary>
	public required int Colunas { get; init; }

	public required int Linhas { get; init; }

	/// <summary>`Colunas x Linhas` cores, em ordem de linha (`i = y * Colunas + x`).</summary>
	public required CorDeTile[] Cores { get; init; }

	public bool Tem(int x, int y) => x >= 0 && y >= 0 && x < Colunas && y < Linhas && y * Colunas + x < Cores.Length;
}

/// <summary>
/// **A COR DE CADA TILE DO JOGO** -- o `Assets/Data/tiles-cores.json`, irmao do `tiles.json`.
///
/// ============================ POR QUE ISTO EXISTE (o mapa da aba World) ============================
/// O dono (2026-10-10): *"adicione na aba world o mapa do mundo atual q vc ta pra vc saber onde ta no
/// mundo"*. Um mapa do planeta inteiro e um pixel por tile -- e pra pintar esse pixel e preciso saber de
/// que COR e o tile. O cliente nao sabe: o que ele tem e a folha ja na placa de video, e ler pixel de
/// volta da placa trava a thread principal (um `texture_get_data` por folha, com a fila de quadros
/// parada). A cor sai entao do PIPELINE, que le os PNG no disco e publica a media de cada quadro (comando
/// `cores` do `Tools/AssetPipeline`).
///
/// A CHAVE E O NOME DO ATLAS E O QUADRO, e nao o id de fonte: o id e atribuido por ordem de descoberta na
/// conversao dos mapas e muda a cada reconversao (ver <see cref="CatalogoDeTiles"/>); o desenho da folha
/// nao.
///
/// FORMATO PLANO, pelo contrato de todo json deste projeto:
///     [ { "atlas": "Waters", "colunas": 8, "linhas": 14, "cores": "3a6fb5ff..." }, ... ]
/// `cores` sao 8 digitos hexa por quadro (RRGGBB + cobertura), em ordem de linha.
/// ================================================================================================
/// </summary>
public sealed class CoresDeTiles
{
	private readonly Dictionary<string, FolhaDeCores> _porAtlas = new(StringComparer.OrdinalIgnoreCase);

	public int Total => _porAtlas.Count;
	public int TotalDeQuadros => _porAtlas.Values.Sum(f => f.Cores.Length);
	public IEnumerable<FolhaDeCores> Todas => _porAtlas.Values;

	public FolhaDeCores? Da(string? atlas) => atlas == null ? null : _porAtlas.GetValueOrDefault(atlas);

	/// <summary>A cor de um quadro, ou nulo quando a folha nao esta aqui (ou o quadro cai fora dela).</summary>
	public CorDeTile? De(string? atlas, int x, int y) =>
		Da(atlas) is { } f && f.Tem(x, y) ? f.Cores[y * f.Colunas + x] : null;

	/// <summary>
	/// A COR MEDIA DE UM QUADRO de uma folha RGBA de 8 bits -- a UNICA casa da regra "de que cor e este
	/// tile". Media ponderada pelo alfa (pixel meio transparente pesa meio), e a cobertura e a soma dos
	/// alfas sobre a area do quadro.
	/// </summary>
	public static CorDeTile Media(ReadOnlySpan<byte> rgba, int larguraDaFolha, int x0, int y0, int w, int h)
	{
		long r = 0, g = 0, b = 0, a = 0;
		for (int y = y0; y < y0 + h; y++)
		{
			int i = (y * larguraDaFolha + x0) * 4;
			for (int x = 0; x < w; x++, i += 4)
			{
				int alfa = rgba[i + 3];
				if (alfa == 0) continue;
				r += rgba[i] * alfa;
				g += rgba[i + 1] * alfa;
				b += rgba[i + 2] * alfa;
				a += alfa;
			}
		}
		if (a == 0) return default;
		long area = (long)w * h;
		return new CorDeTile((byte)((r + a / 2) / a), (byte)((g + a / 2) / a), (byte)((b + a / 2) / a),
							 (byte)Math.Clamp((a + area / 2) / area, 1, 255));
	}

	/// <summary>Le o `tiles-cores.json`. Vazio ou ilegivel devolve catalogo VAZIO: o mapa cai nas cores de reserva.</summary>
	public static CoresDeTiles Parse(string json)
	{
		var c = new CoresDeTiles();
		if (string.IsNullOrWhiteSpace(json)) return c;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			foreach (JsonElement e in doc.RootElement.EnumerateArray())
			{
				string atlas = e.GetProperty("atlas").GetString() ?? "";
				int colunas = e.GetProperty("colunas").GetInt32();
				int linhas = e.GetProperty("linhas").GetInt32();
				string hexa = e.GetProperty("cores").GetString() ?? "";
				if (atlas.Length == 0 || colunas <= 0 || linhas <= 0 || hexa.Length != colunas * linhas * 8) continue;

				var cores = new CorDeTile[colunas * linhas];
				for (int i = 0; i < cores.Length; i++)
				{
					uint v = uint.Parse(hexa.AsSpan(i * 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
					cores[i] = new CorDeTile((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
				}
				c._porAtlas[atlas] = new FolhaDeCores { Atlas = atlas, Colunas = colunas, Linhas = linhas, Cores = cores };
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
		{
			return new CoresDeTiles();
		}
		return c;
	}

	/// <summary>Escreve o arquivo. Mora aqui, ao lado do leitor, pra as duas pontas do formato nao divergirem.</summary>
	public static string ParaJson(IEnumerable<FolhaDeCores> folhas)
	{
		var sb = new StringBuilder("[\n");
		bool primeira = true;
		foreach (FolhaDeCores f in folhas.OrderBy(f => f.Atlas, StringComparer.OrdinalIgnoreCase))
		{
			if (!primeira) sb.Append(",\n");
			primeira = false;
			sb.Append("  { \"atlas\": ").Append(JsonSerializer.Serialize(f.Atlas))
			  .Append(", \"colunas\": ").Append(f.Colunas.ToString(CultureInfo.InvariantCulture))
			  .Append(", \"linhas\": ").Append(f.Linhas.ToString(CultureInfo.InvariantCulture))
			  .Append(", \"cores\": \"");
			foreach (CorDeTile c in f.Cores)
				sb.Append(c.R.ToString("x2", CultureInfo.InvariantCulture)).Append(c.G.ToString("x2", CultureInfo.InvariantCulture))
				  .Append(c.B.ToString("x2", CultureInfo.InvariantCulture)).Append(c.Cobertura.ToString("x2", CultureInfo.InvariantCulture));
			sb.Append("\" }");
		}
		return sb.Append("\n]\n").ToString();
	}
}

/// <summary>
/// AS CORES POR ID DE FONTE -- a ponte entre o `.pedacos` (que fala em fonte + quadro) e as
/// <see cref="CoresDeTiles"/> (que falam em nome de atlas). Montada uma vez; consulta sem dicionario,
/// porque quem pergunta e um laco de 250 mil a 1 milhao de celulas.
/// </summary>
public sealed class CoresPorFonte
{
	private readonly FolhaDeCores?[] _porFonte;

	/// <summary>Os atlas do tileset que ficaram SEM cor (folha ausente ou com outra grade). Vai pro log.</summary>
	public IReadOnlyList<string> SemCor { get; }

	private CoresPorFonte(FolhaDeCores?[] porFonte, List<string> semCor)
	{
		_porFonte = porFonte;
		SemCor = semCor;
	}

	public static CoresPorFonte Montar(CatalogoDeTiles tiles, CoresDeTiles cores)
	{
		int maior = -1;
		foreach (AtlasDeTiles a in tiles.Todos) maior = Math.Max(maior, a.Fonte);
		var porFonte = new FolhaDeCores?[maior + 1];
		var semCor = new List<string>();

		foreach (AtlasDeTiles a in tiles.Todos)
		{
			if (a.Fonte < 0) continue;

			// A GRADE TEM QUE SER A MESMA: se a folha foi redesenhada com outro numero de colunas, o
			// quadro (x, y) de hoje nao e o que foi medido. Cor errada e pior que cor de reserva.
			if (cores.Da(a.Nome) is { } f && f.Colunas == a.Colunas) porFonte[a.Fonte] = f;
			else semCor.Add(a.Nome);
		}
		return new CoresPorFonte(porFonte, semCor);
	}

	public bool Achar(int fonte, int ax, int ay, out CorDeTile cor)
	{
		if ((uint)fonte < (uint)_porFonte.Length && _porFonte[fonte] is { } f && f.Tem(ax, ay))
		{
			cor = f.Cores[ay * f.Colunas + ax];
			return true;
		}
		cor = default;
		return false;
	}
}

/// <summary>A imagem pronta do mapa de um lugar: RGBA de 8 bits, linha a linha, e a que escala ela esta.</summary>
public sealed class ImagemDoLugar
{
	/// <summary>O tamanho da IMAGEM, em pixels.</summary>
	public required int Largura { get; init; }
	public required int Altura { get; init; }

	/// <summary>O tamanho do LUGAR, em tiles.</summary>
	public required int LarguraEmTiles { get; init; }
	public required int AlturaEmTiles { get; init; }

	/// <summary>Quantos tiles de lado cabem num pixel (1 = um pixel por tile).</summary>
	public required int Bloco { get; init; }

	/// <summary>`Largura x Altura x 4` bytes. Alfa 0 = nao ha chao ali (o vazio em volta do Mirante).</summary>
	public required byte[] Rgba { get; init; }

	/// <summary>Quantas celulas cairam na cor de RESERVA (tile sem cor publicada). 0 no jogo em dia.</summary>
	public required int NaReserva { get; init; }

	/// <summary>Quantas celulas do retangulo nao tem chao nenhum.</summary>
	public required int Vazias { get; init; }

	public (byte R, byte G, byte B, byte A) Em(int px, int py)
	{
		int i = (py * Largura + px) * 4;
		return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
	}
}

/// <summary>
/// **O MAPA DE UM LUGAR** -- as regras, sem tela: de que cor e cada celula, como o lugar inteiro vira uma
/// imagem pequena, e onde nela o corpo esta. Quem desenha e o cliente (`Client/MapaDoLugarNaTela`).
///
/// ============================ A REGRA DA COR, QUE E UMA SO ============================
/// **A celula tem a cor do tile que o jogo desenha nela** (a media dos pixels do quadro, vezes a tinta
/// quando o planeta gerado pinta o tile), com o que esta EM CIMA do chao -- decoracao, arvore, pedra --
/// misturado por cima na medida do quanto cobre. Vale igual pro mapa desenhado a mao (as celulas do
/// `.pedacos`) e pro planeta gerado (a classe de terreno, que a paleta do bioma traduz em tile).
///
/// Nao e uma paleta de meia duzia de cores por "tipo de chao", e isso foi escolha: com a cor da propria
/// arte o mapa sai com a cara do planeta -- o mar de Namek e verde e a grama e azul, Vegeta e vermelho, a
/// agua de um mundo vulcanico e lava. Uma tabela "agua = azul" pintaria Namek igual a Terra.
///
/// O "TIPO DE CHAO" SO DECIDE A COR ONDE A ARTE NAO DIZ: tile de uma folha sem cor publicada (o
/// `tiles-cores.json` ficou pra tras de uma conversao) cai na <see cref="Reserva"/> -- agua, parede ou
/// chao, pelos planos que a colisao ja tem. O mapa continua legivel, e a contagem
/// (<see cref="ImagemDoLugar.NaReserva"/>) diz que ha o que regerar.
/// ====================================================================================
///
/// TUDO AQUI E PURO E NAO TOCA EM ESTADO QUE MUDA: as entradas sao o `.pedacos` lido, o terreno gerado e
/// os bitsets do ARQUIVO da colisao (nunca as camadas que abrem e fecham em jogo). E o que deixa o
/// cliente montar a imagem numa thread de trabalho, fora do quadro.
/// </summary>
public static class MapaDoLugar
{
	/// <summary>
	/// O MAIOR LADO DA IMAGEM, em pixels. Um planeta gerado chega a 1000x1000 tiles e o painel da aba tem
	/// uns 300 px: acima disto a placa so jogaria pixel fora. Lugar maior que isto vira BLOCOS (2x2
	/// tiles por pixel num mundo de 1000), com a cor media do bloco.
	/// </summary>
	public const int LadoMaximoDaImagem = 512;

	/// <summary>
	/// QUANTO O QUE ESTA EM CIMA DO CHAO PESA NA COR DA CELULA, por unidade de cobertura. Com 1,6 um
	/// objeto que cobre 62% do quadro ja pinta a celula inteira, e uma arvore de copa rala (30%) vira
	/// metade arvore, metade grama -- a floresta aparece sem virar tinta solida.
	/// </summary>
	public const double PesoDoQueEstaEmCima = 1.6;

	/// <summary>Abaixo desta cobertura (2% do quadro) o tile de chao e tratado como vazio.</summary>
	public const byte CoberturaMinimaDoChao = 5;

	// AS CORES DE RESERVA -- a regra "tipo de chao -> cor" de quando a arte nao responde.
	public static readonly CorDeTile ReservaDaAgua = new(0x3a, 0x6f, 0xb5, 255);
	public static readonly CorDeTile ReservaDaParede = new(0x5a, 0x5d, 0x66, 255);
	public static readonly CorDeTile ReservaDoChao = new(0x7d, 0x6e, 0x52, 255);

	/// <summary>DEFEITO INJETADO (bancada `mapa`): a celula sem cor publicada fica VAZIA em vez de cair na reserva -- um buraco no mapa.</summary>
	public static bool SemReservaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `mapa`): o que esta em cima do chao nao entra na conta -- floresta e cidade somem do mapa.</summary>
	public static bool SoOChaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancadas `mapa` e `--diagmapa`): o marcador nao e preso ao retangulo -- fora da borda ele sai do mapa.</summary>
	public static bool MarcadorSoltoDeTeste;

	/// <summary>
	/// O LADO DA VISTA DE PERTO, em tiles: o quadrado em volta do corpo que o mapa mostra ampliado quando se
	/// clica nele. Num mundo gerado de 1000 tiles o relevo tem graos de 6 a 16 tiles e o mundo inteiro,
	/// encolhido pro quadro, vira um chuvisco -- de perto (2 px de tela por tile) da pra ler lago, colina e
	/// floresta em volta de onde se esta.
	/// </summary>
	public const int TilesDaVistaDePerto = 128;

	/// <summary>DEFEITO INJETADO (bancada `mapa`): a janela da vista de perto nao e presa dentro do mapa -- perto da borda ela mostra o vazio de fora.</summary>
	public static bool JanelaSoltaDeTeste;

	/// <summary>A cor de uma celula cujo tile nao tem cor: agua vence parede, parede vence chao.</summary>
	public static CorDeTile Reserva(bool agua, bool parede) =>
		agua ? ReservaDaAgua : parede ? ReservaDaParede : ReservaDoChao;

	/// <summary>Quantos tiles por pixel um lugar deste tamanho pede pra caber em <see cref="LadoMaximoDaImagem"/>.</summary>
	public static int BlocoPara(int larguraEmTiles, int alturaEmTiles) =>
		Math.Max(1, (Math.Max(larguraEmTiles, alturaEmTiles) + LadoMaximoDaImagem - 1) / LadoMaximoDaImagem);

	// =====================================================================
	// O MAPA DESENHADO A MAO
	// =====================================================================
	/// <summary>
	/// O mapa de uma zona PRE-FEITA, das celulas do `.pedacos`. A camada "Chao" da a cor de base; as
	/// outras (decoracao e objetos) entram por cima, na ordem do arquivo.
	/// </summary>
	/// <param name="planos">A colisao da zona, so pela agua e pela parede DO ARQUIVO (a cor de reserva). Pode ser nula.</param>
	public static ImagemDoLugar DoPreFeito(PedacosDoMapa dados, int largura, int altura, CoresPorFonte cores,
										   ZoneCollision? planos)
	{
		largura = Math.Max(1, largura);
		altura = Math.Max(1, altura);
		var rgba = new byte[largura * altura * 4];
		int naReserva = 0;

		int chao = Math.Max(0, Array.IndexOf(dados.Camadas, "Chao"));
		for (int passo = 0; passo < dados.Camadas.Length; passo++)
		{
			// O CHAO PRIMEIRO, o resto na ordem do arquivo: quem escreveu o `.pedacos` gravou as camadas de
			// baixo pra cima (`MapConverter`), e e essa a ordem em que uma cobre a outra.
			int camada = passo == 0 ? chao : passo <= chao ? passo - 1 : passo;
			bool ehChao = camada == chao;
			if (!ehChao && SoOChaoDeTeste) continue;

			for (int cy = dados.Cy0; cy < dados.Cy1; cy++)
				for (int cx = dados.Cx0; cx < dados.Cx1; cx++)
				{
					if (!dados.Achar(cx, cy, camada, out int inicio, out int quantas)) continue;
					for (int n = 0; n < quantas; n++)
					{
						CelulaDePedaco cel = dados.Celula(inicio, n);
						if (cel.X < 0 || cel.Y < 0 || cel.X >= largura || cel.Y >= altura) continue;
						int i = (cel.Y * largura + cel.X) * 4;

						if (!cores.Achar(cel.Fonte, cel.Ax, cel.Ay, out CorDeTile cor))
						{
							// SEM COR PUBLICADA. No chao a celula cai na reserva; em cima do chao ela so
							// nao pinta (um enfeite cinza-reserva em cima da grama seria pior que nenhum).
							if (!ehChao || SemReservaDeTeste) continue;
							cor = Reserva(planos?.EhAgua(cel.X, cel.Y) == true, planos?.BloqueadaNoArquivo(cel.X, cel.Y) == true);
							naReserva++;
						}

						if (ehChao) PorChao(rgba, i, cor);
						else PorEmCima(rgba, i, cor);
					}
				}
		}

		return Fechar(rgba, largura, altura, naReserva);
	}

	// =====================================================================
	// O PLANETA GERADO (e as plantas fixas: nave, mente, interior do Majin)
	// =====================================================================
	/// <summary>
	/// O mapa de um terreno GERADO: a classe de cada celula vira o tile que a paleta do bioma manda
	/// desenhar, e o tile vira cor -- meia duzia de respostas pro mundo inteiro, resolvidas uma vez cada.
	/// </summary>
	public static ImagemDoLugar DoTerreno(TerrenoGerado t, CatalogoDeTiles tiles, CoresDeTiles cores)
	{
		int naReserva = 0;

		// AS CINCO CLASSES DE CHAO, perguntadas ao PROPRIO terreno (`VisualDaClasse`): e a mesma resposta que
		// o cliente usa pra pintar o mundo. A reserva de cada uma sai do que a classe E: agua e agua, montanha
		// e parede, o resto e chao.
		Span<CorDeTile> chao = stackalloc CorDeTile[5];
		Span<bool> chaoSemCor = stackalloc bool[5];
		for (int c = 0; c < 5; c++)
		{
			if (CorDoVisual(t.VisualDaClasse((ClasseDeTerreno)c), tiles, cores) is { } achada) chao[c] = achada;
			else
			{
				chaoSemCor[c] = true;
				chao[c] = SemReservaDeTeste ? default
					: Reserva(c == (int)ClasseDeTerreno.Agua, c == (int)ClasseDeTerreno.Montanha);
			}
		}

		// O QUE ESTA EM CIMA, resolvido na PRIMEIRA celula em que cada cobertura aparece e guardado pelo valor
		// dela -- tambem pelo terreno (`VisualDaCobertura`), pra uma cobertura que nasca amanha ja sair pintada
		// sem este arquivo saber o nome dela. Sem cor, a cobertura nao pinta.
		Span<CorDeTile> emCima = stackalloc CorDeTile[256];
		Span<bool> resolvida = stackalloc bool[256];
		resolvida.Clear();

		int w = t.Largura, h = t.Altura;
		var rgba = new byte[w * h * 4];
		for (int n = 0; n < w * h; n++)
		{
			byte classe = t.Chao[n];
			if (classe >= 5) continue;
			if (chaoSemCor[classe]) naReserva++;
			PorChao(rgba, n * 4, chao[classe]);

			byte cob = t.Cobertura[n];
			if (cob == 0 || SoOChaoDeTeste) continue;
			if (!resolvida[cob])
			{
				resolvida[cob] = true;
				emCima[cob] = t.VisualDaCobertura(n % w, n / w) is { } visual ? CorDoVisual(visual, tiles, cores) ?? default : default;
			}
			PorEmCima(rgba, n * 4, emCima[cob]);
		}

		return Fechar(rgba, w, h, naReserva);
	}

	/// <summary>
	/// A cor de um tile do gerador: o quadro que o nome resolve, vezes a TINTA -- o `Modulate` da camada
	/// no jogo (`PlanetaProcedural.Camada`), que multiplica a textura canal a canal.
	/// </summary>
	public static CorDeTile? CorDoVisual(TileVisual v, CatalogoDeTiles tiles, CoresDeTiles cores)
	{
		if (v.Atlas is not { Length: > 0 } || tiles.Achar(v) is not { } q) return null;
		if (cores.De(tiles.Atlas(v.Atlas)?.Nome, q.X, q.Y) is not { Cobertura: > 0 } c) return null;
		if (v.Tinta is not { Length: 7 } t || t[0] != '#') return c;

		if (!int.TryParse(t.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int tinta)) return c;
		return new CorDeTile((byte)(c.R * ((tinta >> 16) & 255) / 255), (byte)(c.G * ((tinta >> 8) & 255) / 255),
							 (byte)(c.B * (tinta & 255) / 255), c.Cobertura);
	}

	// =====================================================================
	// AS DUAS PINCELADAS
	// =====================================================================
	private static void PorChao(byte[] rgba, int i, CorDeTile cor)
	{
		if (cor.Cobertura < CoberturaMinimaDoChao) return;
		rgba[i] = cor.R;
		rgba[i + 1] = cor.G;
		rgba[i + 2] = cor.B;
		rgba[i + 3] = 255;
	}

	private static void PorEmCima(byte[] rgba, int i, CorDeTile cor)
	{
		if (cor.Cobertura == 0) return;

		// SEM CHAO EMBAIXO (uma ponte sobre o vazio, uma plataforma): o que esta em cima E o chao dali.
		if (rgba[i + 3] == 0)
		{
			rgba[i] = cor.R;
			rgba[i + 1] = cor.G;
			rgba[i + 2] = cor.B;
			rgba[i + 3] = 255;
			return;
		}

		double k = Math.Min(1.0, cor.Cobertura / 255.0 * PesoDoQueEstaEmCima);
		rgba[i] = (byte)(rgba[i] + (cor.R - rgba[i]) * k);
		rgba[i + 1] = (byte)(rgba[i + 1] + (cor.G - rgba[i + 1]) * k);
		rgba[i + 2] = (byte)(rgba[i + 2] + (cor.B - rgba[i + 2]) * k);
	}

	/// <summary>Conta o vazio e reduz a imagem a <see cref="LadoMaximoDaImagem"/> quando o lugar e maior que ela.</summary>
	private static ImagemDoLugar Fechar(byte[] porTile, int w, int h, int naReserva)
	{
		int vazias = 0;
		for (int i = 3; i < porTile.Length; i += 4)
			if (porTile[i] == 0) vazias++;

		int bloco = BlocoPara(w, h);
		if (bloco == 1)
			return new ImagemDoLugar
			{
				Largura = w, Altura = h, LarguraEmTiles = w, AlturaEmTiles = h, Bloco = 1,
				Rgba = porTile, NaReserva = naReserva, Vazias = vazias,
			};

		// A MEDIA DO BLOCO, so das celulas que TEM chao: um bloco meio vazio fica com a cor do chao que
		// ha nele, e so some quando nao ha nenhum.
		int pw = (w + bloco - 1) / bloco, ph = (h + bloco - 1) / bloco;
		var rgba = new byte[pw * ph * 4];
		for (int py = 0; py < ph; py++)
			for (int px = 0; px < pw; px++)
			{
				int r = 0, g = 0, b = 0, n = 0;
				int y1 = Math.Min(h, (py + 1) * bloco), x1 = Math.Min(w, (px + 1) * bloco);
				for (int y = py * bloco; y < y1; y++)
					for (int x = px * bloco; x < x1; x++)
					{
						int i = (y * w + x) * 4;
						if (porTile[i + 3] == 0) continue;
						r += porTile[i];
						g += porTile[i + 1];
						b += porTile[i + 2];
						n++;
					}
				if (n == 0) continue;
				int o = (py * pw + px) * 4;
				rgba[o] = (byte)(r / n);
				rgba[o + 1] = (byte)(g / n);
				rgba[o + 2] = (byte)(b / n);
				rgba[o + 3] = 255;
			}

		return new ImagemDoLugar
		{
			Largura = pw, Altura = ph, LarguraEmTiles = w, AlturaEmTiles = h, Bloco = bloco,
			Rgba = rgba, NaReserva = naReserva, Vazias = vazias,
		};
	}

	// =====================================================================
	// ONDE EU ESTOU
	// =====================================================================
	/// <summary>
	/// A CELULA EM QUE O CORPO ESTA -- as coordenadas que a aba escreve. E a dos PES
	/// (<see cref="CelulaInterna.CelulaDoCorpo"/>), a mesma que o servidor usa pra dizer em que celula um
	/// corpo esta. X cresce pro leste e Y pro SUL (a linha 0 e a de cima do mapa).
	/// </summary>
	public static (int X, int Y) Celula(Vec2 centroDoCorpo) => CelulaInterna.CelulaDoCorpo(centroDoCorpo);

	/// <summary>O texto das coordenadas, do jeito que a aba e as bancadas leem.</summary>
	public static string Coordenadas(Vec2 centroDoCorpo)
	{
		(int x, int y) = Celula(centroDoCorpo);
		return $"X {x.ToString(CultureInfo.InvariantCulture)} · Y {y.ToString(CultureInfo.InvariantCulture)}";
	}

	/// <summary>
	/// A JANELA DA VISTA DE PERTO: o quadrado de <see cref="TilesDaVistaDePerto"/> tiles com o corpo no meio,
	/// PRESO dentro do mapa -- perto de uma borda a janela para nela, e e o marcador que sai do centro. (Num
	/// lugar menor que a janela, ela e o lugar inteiro.) `X0`, `Y0` e `Lado` em tiles; `U` e `V` sao a posicao
	/// do marcador dentro da janela, de 0 a 1.
	/// </summary>
	public static (float X0, float Y0, float Lado, float U, float V) VistaDePerto(Vec2 centroDoCorpo, int larguraEmTiles, int alturaEmTiles)
	{
		float lado = Math.Max(1, Math.Min(TilesDaVistaDePerto, Math.Min(larguraEmTiles, alturaEmTiles)));
		Vec2 pes = ClasseDeCorpo.Pes(centroDoCorpo);
		float px = pes.X / ZoneCollision.TileSize, py = pes.Y / ZoneCollision.TileSize;
		float x0 = px - lado / 2f, y0 = py - lado / 2f;
		if (!JanelaSoltaDeTeste)
		{
			x0 = Math.Clamp(x0, 0f, Math.Max(0f, larguraEmTiles - lado));
			y0 = Math.Clamp(y0, 0f, Math.Max(0f, alturaEmTiles - lado));
		}
		return (x0, y0, lado, Math.Clamp((px - x0) / lado, 0f, 1f), Math.Clamp((py - y0) / lado, 0f, 1f));
	}

	/// <summary>
	/// ONDE O MARCADOR FICA NO MAPA, em fracao do retangulo (0 = borda esquerda/de cima, 1 = a oposta).
	///
	/// PRESO AO RETANGULO: ha lugares sem beirada (a Sala do Tempo, a mente) em que o corpo anda pra fora
	/// do que o mapa desenha. O marcador para na borda, do lado certo, e `Fora` diz que ele esta alem
	/// dela -- as coordenadas continuam sendo as de verdade.
	/// </summary>
	public static (float U, float V, bool Fora) Marcador(Vec2 centroDoCorpo, int larguraEmTiles, int alturaEmTiles)
	{
		Vec2 pes = ClasseDeCorpo.Pes(centroDoCorpo);
		float u = pes.X / (Math.Max(1, larguraEmTiles) * (float)ZoneCollision.TileSize);
		float v = pes.Y / (Math.Max(1, alturaEmTiles) * (float)ZoneCollision.TileSize);
		bool fora = u < 0f || v < 0f || u > 1f || v > 1f;
		if (MarcadorSoltoDeTeste) return (u, v, fora);
		return (Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f), fora);
	}
}
