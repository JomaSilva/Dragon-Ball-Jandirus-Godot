using System.Globalization;
using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// .dmm -> uma CENA POR ANDAR (decisao do dono do projeto: cada planeta pre-feito e uma
/// cena instanciada na principal, o que casa com o corte de interesse por zona).
///
/// Gera:
///   Assets/Maps/tileset.tres   -- um TileSet com uma fonte por atlas de turf
///   Assets/Maps/&lt;nome&gt;.tscn -- Node2D + TileMapLayer preenchido
///
/// COLISAO: turf com `density = 1` ganha poligono de fisica; `opacity = 1` ganha oclusor de
/// luz. E o mesmo dado que o SERVIDOR vai carregar pra validar movimento de verdade (hoje
/// ele so confere velocidade).
/// </summary>
public static class MapConverter
{
	private const int Cell = 32;

	private sealed class Fonte
	{
		public int Id;
		public string ResPath = "";
		public int IconW, IconH, Cols;
		public Dictionary<string, int> StateIndex = new(StringComparer.OrdinalIgnoreCase); // icon_state -> indice do 1o quadro
		public HashSet<(int X, int Y)> Usadas = [];
		public HashSet<(int X, int Y)> Densas = [];
		public HashSet<(int X, int Y)> Opacas = [];
	}

	public static void Convert(string dmmDir, string spritesDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		// indice case-insensitive dos atlas ja convertidos (o DM escreve 'Turfs.dmi' e o arquivo e 'turfs.dmi')
		var atlasPorNome = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string png in Directory.GetFiles(spritesDir, "*.png", SearchOption.AllDirectories))
			atlasPorNome[Path.GetFileNameWithoutExtension(png)] = png;

		var fontes = new Dictionary<string, Fonte>(StringComparer.OrdinalIgnoreCase);
		var semAtlas = new HashSet<string>();
		int proxId = 0;

		// ---- passada 1: descobrir quais tiles cada mapa usa ----
		// CADA .dmm NUMERA O PROPRIO z A PARTIR DE 1. O z real do jogo vem da ORDEM em que o
		// .dme inclui os arquivos: o 1o mapa ocupa z1..zN, o 2o continua de zN+1, e assim por
		// diante. Sem este deslocamento, quatro mapas diferentes gerariam quatro "z01".
		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		foreach ((string _, DmmMap.Result dados, int _off) in mapas)
			foreach (string[] tipos in dados.Keys.Values)
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!bp.StartsWith("/turf", StringComparison.Ordinal)) continue;
					if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null) continue;

					Fonte? f = Garantir(td.Icon, fontes, atlasPorNome, semAtlas, ref proxId);
					if (f == null) continue;

					(int X, int Y) coord = Coord(f, td.IconState);
					f.Usadas.Add(coord);
					if (td.Density) f.Densas.Add(coord);
					if (td.Opacity) f.Opacas.Add(coord);
				}

		EscreverTileSet(Path.Combine(outDir, "tileset.tres"), fontes);

		// ---- passada 2: uma cena por andar + o mapa de colisao que o SERVIDOR le ----
		int cenas = 0, celulas = 0, bloqueadas = 0;
		var manifesto = new List<string>();
		foreach ((string arquivo, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string cena = Path.Combine(outDir, nome + ".tscn");
				celulas += EscreverCena(cena, nome, nivel, dados, turfs, fontes, atlasPorNome, semAtlas, ref proxId);
				bloqueadas += EscreverColisao(Path.Combine(outDir, nome + ".col"), nivel, dados, turfs);
				cenas++;

				string zona = nome[(nome.IndexOf('_') + 1)..];
				manifesto.Add($"  {{ \"zona\": \"{zona}\", \"z\": {nivel.Z + off}, \"cena\": \"res://Assets/Maps/{nome}.tscn\", " +
							  $"\"colisao\": \"res://Assets/Maps/{nome}.col\", \"w\": {nivel.Width}, \"h\": {nivel.Height} }}");
			}

		File.WriteAllText(Path.Combine(outDir, "manifest.json"),
			"[\n" + string.Join(",\n", manifesto) + "\n]\n", new UTF8Encoding(false));

		Console.WriteLine($"mapas lidos    : {mapas.Count}");
		Console.WriteLine($"cenas geradas  : {cenas}");
		Console.WriteLine($"celulas        : {celulas}");
		Console.WriteLine($"fontes no tileset: {fontes.Count}");
		if (semAtlas.Count > 0)
			Console.WriteLine($"SEM atlas ({semAtlas.Count}): {string.Join(", ", semAtlas.Take(8))}");
	}

	private static Fonte? Garantir(string icone, Dictionary<string, Fonte> fontes,
		Dictionary<string, string> atlasPorNome, HashSet<string> semAtlas, ref int proxId)
	{
		if (fontes.TryGetValue(icone, out Fonte? existente)) return existente;

		string chave = Path.GetFileNameWithoutExtension(icone);
		if (!atlasPorNome.TryGetValue(chave, out string? png))
		{
			semAtlas.Add(icone);
			return null;
		}

		// o .dmi original guarda os metadados; o .png convertido e copia crua dele
		string dmi = png; // mesmo conteudo
		DmiFile.Result? meta = DmiFile.Read(dmi);
		if (meta == null) { semAtlas.Add(icone); return null; }

		var f = new Fonte
		{
			Id = proxId++,
			ResPath = "res://" + Path.GetRelativePath(Directory.GetCurrentDirectory(), png).Replace('\\', '/'),
			IconW = meta.IconWidth,
			IconH = meta.IconHeight,
			Cols = Math.Max(1, meta.SheetWidth / Math.Max(1, meta.IconWidth)),
		};

		int idx = 0;
		foreach (DmiState st in meta.States)
		{
			f.StateIndex.TryAdd(st.Name, idx);
			idx += Math.Max(1, st.Dirs) * Math.Max(1, st.Frames);
		}

		fontes[icone] = f;
		return f;
	}

	private static (int X, int Y) Coord(Fonte f, string? state)
	{
		int idx = 0;
		if (state != null) f.StateIndex.TryGetValue(state, out idx);
		return (idx % f.Cols, idx / f.Cols);
	}

	/// <summary>Le a ordem dos .dmm no .dme: e ela que define o z real de cada mapa.</summary>
	private static List<string> OrdemDoDme(string dmmDir)
	{
		var ordem = new List<string>();
		string? raiz = Directory.GetParent(dmmDir)?.FullName;
		string? dme = raiz == null ? null : Directory.GetFiles(raiz, "*.dme").FirstOrDefault();

		if (dme != null)
			foreach (string linha in File.ReadAllLines(dme))
			{
				if (!linha.TrimStart().StartsWith("#include", StringComparison.Ordinal)) continue;
				if (!linha.Contains(".dmm", StringComparison.OrdinalIgnoreCase)) continue;
				string arq = Path.GetFileName(linha.Trim().Trim('"').Replace('\\', '/'));
				string cheio = Path.Combine(dmmDir, arq);
				if (File.Exists(cheio)) ordem.Add(cheio);
			}

		// .dme ausente/ilegivel: cai na ordem alfabetica e avisa (o z pode sair trocado)
		if (ordem.Count == 0)
		{
			Console.WriteLine("AVISO: nao achei a ordem dos mapas no .dme; usando ordem alfabetica");
			ordem.AddRange(Directory.GetFiles(dmmDir, "*.dmm").OrderBy(s => s));
		}
		return ordem;
	}

	private static string NomeDoAndar(DmmMap.Result dados, DmmLevel nivel, int offset)
	{
		// a AREA dominante nomeia o andar: e o nome que o jogo ja usa pro lugar
		var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int x = 0; x < nivel.Width; x++)
			for (int y = 0; y < nivel.Height; y++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!bp.StartsWith("/area", StringComparison.Ordinal)) continue;
					contagem[bp] = contagem.GetValueOrDefault(bp) + 1;
				}
			}

		string dominante = contagem.Count > 0
			? contagem.OrderByDescending(kv => kv.Value).First().Key
			: "/area/Unknown";

		string curto = dominante[(dominante.LastIndexOf('/') + 1)..];
		var sb = new StringBuilder();
		foreach (char c in curto) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		return $"z{nivel.Z + offset:00}_{(sb.Length > 0 ? sb.ToString() : "Area")}";
	}

	// ---------------------------------------------------------------------
	// .tres do TileSet
	// ---------------------------------------------------------------------
	private static void EscreverTileSet(string caminho, Dictionary<string, Fonte> fontes)
	{
		var ext = new StringBuilder();
		var sub = new StringBuilder();
		var res = new StringBuilder();
		int passos = 1;

		foreach (Fonte f in fontes.Values.OrderBy(v => v.Id))
		{
			string extId = $"{f.Id}_atlas";
			ext.Append($"[ext_resource type=\"Texture2D\" path=\"{f.ResPath}\" id=\"{extId}\"]\n");
			passos++;

			sub.Append($"[sub_resource type=\"TileSetAtlasSource\" id=\"Atlas_{f.Id}\"]\n");
			sub.Append($"texture = ExtResource(\"{extId}\")\n");
			sub.Append($"texture_region_size = Vector2i({f.IconW}, {f.IconH})\n");

			// ATENCAO A CULTURA: em pt-BR o float sai com VIRGULA decimal e um "72,5" vira
			// dois componentes no PackedVector2Array -> "Convex decomposing failed". Sempre invariante.
			string hw = Inv(f.IconW / 2f), hh = Inv(f.IconH / 2f);
			string nhw = Inv(-f.IconW / 2f), nhh = Inv(-f.IconH / 2f);
			foreach ((int X, int Y) c in f.Usadas.OrderBy(c => c.Y).ThenBy(c => c.X))
			{
				sub.Append($"{c.X}:{c.Y}/0 = 0\n");
				if (f.Densas.Contains(c))
					sub.Append($"{c.X}:{c.Y}/0/physics_layer_0/polygon_0/points = " +
							   $"PackedVector2Array({nhw}, {nhh}, {hw}, {nhh}, {hw}, {hh}, {nhw}, {hh})\n");
				if (f.Opacas.Contains(c))
					sub.Append($"{c.X}:{c.Y}/0/occlusion_layer_0/polygon = " +
							   $"PackedVector2Array({nhw}, {nhh}, {hw}, {nhh}, {hw}, {hh}, {nhw}, {hh})\n");
			}
			sub.Append('\n');
			passos++;

			res.Append($"sources/{f.Id} = SubResource(\"Atlas_{f.Id}\")\n");
		}

		var sb = new StringBuilder();
		sb.Append($"[gd_resource type=\"TileSet\" load_steps={passos} format=3]\n\n");
		sb.Append(ext).Append('\n');
		sb.Append(sub);
		sb.Append("[resource]\n");
		sb.Append($"tile_size = Vector2i({Cell}, {Cell})\n");
		sb.Append("physics_layer_0/collision_layer = 1\n");
		sb.Append("occlusion_layer_0/light_mask = 1\n");
		sb.Append(res);

		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}

	// ---------------------------------------------------------------------
	// .tscn do andar
	// ---------------------------------------------------------------------
	private static int EscreverCena(string caminho, string nome, DmmLevel nivel, DmmMap.Result dados,
		Dictionary<string, TurfDef> turfs, Dictionary<string, Fonte> fontes,
		Dictionary<string, string> atlasPorNome, HashSet<string> semAtlas, ref int proxId)
	{
		// TileMapLayer.tile_map_data: [uint16 formato][por celula: 12 bytes]
		//   int16 x | int16 y | uint16 fonte | uint16 atlasX | uint16 atlasY | uint16 alternativa
		var bytes = new List<byte>();
		// O TileMapLayer (node novo) tem numeracao PROPRIA de formato e hoje so aceita 0.
		// Mandar 3 (o formato do TileMap ANTIGO) faz o Godot recusar o blob inteiro em silencio:
		// a cena carrega, o layer fica VAZIO e nada avisa alem de um erro no log.
		AddU16(bytes, 0);

		int usadas = 0;
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				// a celula desenha o TURF (obj vira entidade depois, nao tile)
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!bp.StartsWith("/turf", StringComparison.Ordinal)) continue;
					if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null) break;
					if (!fontes.TryGetValue(td.Icon, out Fonte? f)) break;

					(int X, int Y) c = Coord(f, td.IconState);
					AddI16(bytes, (short)x);
					AddI16(bytes, (short)y);
					AddU16(bytes, (ushort)f.Id);
					AddU16(bytes, (ushort)c.X);
					AddU16(bytes, (ushort)c.Y);
					AddU16(bytes, 0);
					usadas++;
					break;
				}
			}

		var sb = new StringBuilder();
		sb.Append("[gd_scene load_steps=2 format=3]\n\n");
		sb.Append("[ext_resource type=\"TileSet\" path=\"res://Assets/Maps/tileset.tres\" id=\"1_ts\"]\n\n");
		sb.Append($"[node name=\"{nome}\" type=\"Node2D\"]\n\n");
		sb.Append("[node name=\"Chao\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("tile_set = ExtResource(\"1_ts\")\n");
		sb.Append($"tile_map_data = PackedByteArray({string.Join(", ", bytes)})\n");

		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
		return usadas;
	}

	private static string Inv(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

	/// <summary>
	/// Mapa de colisao compacto: 1 BIT por celula. Um andar de 500x500 cabe em ~31 KB, entao
	/// o servidor carrega a geometria de todas as zonas sem instanciar cena nenhuma (uma cena
	/// de 250 mil tiles no headless seria absurdo). E o mesmo dado que o cliente usa via
	/// TileMap, so que numa forma que o Core consegue ler sem tocar no Godot.
	/// Cabecalho: "JCOL" + uint16 largura + uint16 altura, depois o bitset em ordem de linha.
	/// </summary>
	private static int EscreverColisao(string caminho, DmmLevel nivel, DmmMap.Result dados,
		Dictionary<string, TurfDef> turfs)
	{
		int w = nivel.Width, h = nivel.Height;
		var bits = new byte[(w * h + 7) / 8];
		int bloqueadas = 0;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;
				// QUALQUER coisa densa na celula bloqueia -- turf OU objeto, em qualquer posicao
				// da lista. Antes so o PRIMEIRO turf era olhado, entao uma celula como
				// "(/turf/Grass, /turf/decor/LargeRock2)" passava batido porque a grama vem
				// primeiro -- e objeto (arvore, cerca, barreira) nem era considerado.
				bool densa = false;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (turfs.TryGetValue(bp, out TurfDef? td) && td.Density) { densa = true; break; }
				}

				if (densa)
				{
					int i = y * w + x;
					bits[i >> 3] |= (byte)(1 << (i & 7));
					bloqueadas++;
				}
			}

		using var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write);
		fs.Write("JCOL"u8);
		fs.WriteByte((byte)(w & 0xFF)); fs.WriteByte((byte)(w >> 8));
		fs.WriteByte((byte)(h & 0xFF)); fs.WriteByte((byte)(h >> 8));
		fs.Write(bits);
		return bloqueadas;
	}

	private static void AddU16(List<byte> b, ushort v) { b.Add((byte)(v & 0xFF)); b.Add((byte)(v >> 8)); }
	private static void AddI16(List<byte> b, short v) => AddU16(b, unchecked((ushort)v));
}
