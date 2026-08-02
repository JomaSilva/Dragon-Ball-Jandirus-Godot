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

		/// <summary>Caminho no DISCO -- e a chave do dicionario de fontes, e o que o TurfDef guarda.</summary>
		public string Chave = "";
		public int IconW, IconH, Cols;
		public Dictionary<string, int> StateIndex = new(StringComparer.OrdinalIgnoreCase); // icon_state -> indice do 1o quadro

		/// <summary>Os estados como o .dmi os declara -- e daqui que saem as animacoes.</summary>
		public List<DmiState> States = [];

		/// <summary>Quantos quadros a folha tem de verdade (o resto da grade e sobra vazia).</summary>
		public int TotalQuadros;
		public HashSet<(int X, int Y)> Usadas = [];
		public HashSet<(int X, int Y)> Densas = [];
		public HashSet<(int X, int Y)> Opacas = [];
	}

	public static void Convert(string dmmDir, string spritesDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		// A RAIZ DO PROJETO GODOT, achada pelo `project.godot` -- NAO o diretorio de trabalho.
		//
		// Os caminhos `res://` do tileset saiam do cwd, e o resultado era que rodar o pipeline
		// de DENTRO de Tools/AssetPipeline escrevia `res://../../Assets/Sprites/...`: nem o
		// jogo nem o editor acham a textura, o TileSet carrega com ZERO tiles e o editor cospe
		// "TileSetAtlasSource has no tile at (0,0)" pra cada celula do mapa. De qual pasta o
		// comando foi chamado nao pode decidir o conteudo do arquivo gerado.
		string? raizAchada = AcharRaiz(outDir);
		string raiz = raizAchada ?? Directory.GetCurrentDirectory();
		if (raizAchada == null)
			Console.WriteLine("AVISO: nao achei o project.godot subindo de " + outDir +
							  " -- os caminhos res:// vao sair relativos ao diretorio atual");

		// INDICE POR NOME -> TODOS OS CANDIDATOS.
		//
		// A arvore de icones tem 79 nomes REPETIDOS em pastas diferentes, e o DM so escreve o
		// nome (`icon = 'Namek.dmi'`) -- quem resolve o caminho e o FILE_DIR do DreamMaker.
		// Guardar um caminho por nome fazia o ultimo varrido ganhar, e o resultado apareceu na
		// tela: `Character Icons/Namekians/Namek.dmi` (o PERSONAGEM) venceu `Turfs/Namek.dmi`
		// (as CASAS), e as casas de Namek foram desenhadas com Namekuseijins empilhados.
		// `Trees.dmi` tinha o mesmo problema.
		//
		// A desambiguacao e por DADO, nao por palpite: ganha o arquivo que REALMENTE tem o
		// `icon_state` que o typepath pediu.
		var atlasPorNome = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string png in Directory.GetFiles(spritesDir, "*.png", SearchOption.AllDirectories))
		{
			string chave = Path.GetFileNameWithoutExtension(png);
			if (!atlasPorNome.TryGetValue(chave, out List<string>? l)) atlasPorNome[chave] = l = [];
			l.Add(png);
		}
		int repetidos = atlasPorNome.Count(kv => kv.Value.Count > 1);
		if (repetidos > 0)
			Console.WriteLine($"nomes de atlas repetidos: {repetidos} (resolvidos pelo icon_state)");

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

		// TURF E OBJ, os dois. So o turf era registrado, e essa era a causa de duas queixas que
		// pareciam separadas: "falta coisa no mapa" e "tem parede invisivel". Sao a MESMA coisa
		// -- 41% dos prefabs da Terra tem um /obj (arvore, minerio, cerca, cadeira), o desenho
		// ignorava todos e a colisao NAO: uma AppleTree densa virava um muro que ninguem via.
		foreach ((string _, DmmMap.Result dados, int _off) in mapas)
			foreach (string[] tipos in dados.Keys.Values)
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!Desenhavel(bp)) continue;
					if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null) continue;

					Fonte? f = Garantir(td.Icon, td.IconState, raiz, fontes, atlasPorNome, semAtlas, ref proxId);
					if (f == null) continue;
					td.Atlas = f.Chave;   // guarda QUAL arquivo venceu: o resto do pipeline usa este

					(int X, int Y) coord = Coord(f, td.IconState);
					f.Usadas.Add(coord);
					if (td.Density) f.Densas.Add(coord);
					if (td.Opacity) f.Opacas.Add(coord);
				}

		// ---- passada 1b: FISICA EM TODO TILE QUE E PAREDE ----
		//
		// Ate aqui so ganhava colisao a celula que algum .dmm usou. Como o tileset agora traz a
		// FOLHA INTEIRA (pra dar pra pintar), a maioria dos tiles de parede ficava sem fisica: o
		// dono pintava um muro no editor e o muro nao parava ninguem.
		//
		// Aqui a densidade vem do TIPO, nao do uso: todo estado de um typepath denso ganha
		// fisica em TODOS os seus quadros e direcoes -- uma parede virada pro norte e parede
		// igual, e a mesma parede no quadro 2 da animacao tambem.
		int marcados = MarcarSolidos(turfs, fontes, atlasPorNome);
		int opacos = fontes.Values.Sum(f => f.Opacas.Count);
		Console.WriteLine($"tiles com fisica : {marcados} | com oclusao: {opacos}");

		EscreverTileSet(Path.Combine(outDir, "tileset.tres"), fontes);

		// ---- passada 2: uma cena por andar + o mapa de colisao que o SERVIDOR le ----
		int cenas = 0, celulas = 0, bloqueadas = 0;
		var manifesto = new List<string>();
		foreach ((string arquivo, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string cena = Path.Combine(outDir, nome + ".tscn");

				// A CENA MANDA. O que ela DESENHOU e o que bloqueia -- a colisao sai da mesma
				// passada, nao de uma segunda leitura do .dmm com suas proprias regras. Duas
				// funcoes calculando "o que e parede" por caminhos diferentes divergiam em ~2%
				// das celulas, e divergencia entre o que se ve e o que se atravessa e
				// exatamente a queixa que estamos consertando.
				celulas += EscreverCena(cena, nome, nivel, dados, turfs, fontes, atlasPorNome,
										semAtlas, ref proxId, out HashSet<(int, int)> paredes,
										out HashSet<(int, int)> cegos);
				bloqueadas += EscreverColisao(Path.Combine(outDir, nome + ".col"),
											  nivel.Width, nivel.Height, paredes);
				// mesmo formato, outro proposito: este e o que o CAMPO DE VISAO consulta
				EscreverColisao(Path.Combine(outDir, nome + ".vis"), nivel.Width, nivel.Height, cegos);
				cenas++;

				string zona = nome[(nome.IndexOf('_') + 1)..];
				manifesto.Add($"  {{ \"zona\": \"{zona}\", \"z\": {nivel.Z + off}, \"cena\": \"res://Assets/Maps/{nome}.tscn\", " +
							  $"\"colisao\": \"res://Assets/Maps/{nome}.col\", \"visao\": \"res://Assets/Maps/{nome}.vis\", " +
							  $"\"luzes\": \"res://Assets/Maps/{nome}.luz\", " +
							  $"\"w\": {nivel.Width}, \"h\": {nivel.Height} }}");
			}

		File.WriteAllText(Path.Combine(outDir, "manifest.json"),
			"[\n" + string.Join(",\n", manifesto) + "\n]\n", new UTF8Encoding(false));

		Conferir(outDir, fontes.Count);

		Console.WriteLine($"mapas lidos    : {mapas.Count}");
		Console.WriteLine($"cenas geradas  : {cenas}");
		Console.WriteLine($"celulas        : {celulas}");
		Console.WriteLine($"fontes no tileset: {fontes.Count}");
		if (semAtlas.Count > 0)
			Console.WriteLine($"SEM atlas ({semAtlas.Count}): {string.Join(", ", semAtlas.Take(8))}");
	}

	/// <summary>
	/// CONFERE O QUE ACABOU DE SAIR. Le o tileset gerado e checa que toda textura referenciada
	/// EXISTE no disco.
	///
	/// Existe por causa de um defeito que passou batido: os caminhos `res://` eram montados a
	/// partir do diretorio de trabalho, entao rodar o comando de dentro da pasta da ferramenta
	/// escrevia `res://../../Assets/...`. O jogo abria (o TileMapLayer so nao desenha o que nao
	/// acha) e o EDITOR e que quebrava, com uma enxurrada de "TileSetAtlasSource has no tile at
	/// (0,0)" -- o pior tipo de defeito, o que so aparece pra quem vai mexer no arquivo.
	/// </summary>
	private static void Conferir(string outDir, int esperadas)
	{
		string tileset = Path.Combine(outDir, "tileset.tres");
		if (!File.Exists(tileset)) return;

		string? raiz = AcharRaiz(outDir);
		int ok = 0;
		var quebradas = new List<string>();

		foreach (string linha in File.ReadAllLines(tileset))
		{
			int i = linha.IndexOf("path=\"res://", StringComparison.Ordinal);
			if (i < 0) continue;
			int ini = i + "path=\"res://".Length;
			int fim = linha.IndexOf('"', ini);
			if (fim < 0) continue;

			string rel = linha[ini..fim];
			string cheio = raiz != null ? Path.Combine(raiz, rel) : rel;
			if (File.Exists(cheio)) ok++;
			else quebradas.Add(rel);
		}

		if (quebradas.Count == 0)
		{
			Console.WriteLine($"tileset conferido: {ok}/{esperadas} texturas existem");
			return;
		}

		Console.WriteLine($"ERRO: {quebradas.Count} textura(s) do tileset NAO existem -- o editor");
		Console.WriteLine("      nao vai conseguir abrir as cenas dos planetas:");
		foreach (string q in quebradas.Take(5)) Console.WriteLine("        res://" + q);
	}

	/// <summary>
	/// Marca fisica e oclusao em todo quadro de todo typepath solido do jogo -- nao so nos que
	/// aparecem nos mapas.
	///
	/// So mexe em atlas que JA estao no tileset: registrar tambem os .dmi que nenhum mapa usa
	/// encheria o arquivo de centenas de fontes que ninguem pediu. Quem quiser um deles, o
	/// caminho e usar o sprite em algum mapa (ou pedir pra incluir a pasta inteira).
	/// </summary>
	private static int MarcarSolidos(Dictionary<string, TurfDef> turfs, Dictionary<string, Fonte> fontes,
		Dictionary<string, List<string>> atlasPorNome)
	{
		int n = 0;
		foreach (TurfDef td in turfs.Values)
		{
			if (td.Icon == null || (!td.Density && !td.Opacity)) continue;
			if (EhPorta(td.Path)) continue;                       // porta e passagem: ver EhPorta

			// O typepath pode nunca ter aparecido num .dmm (e ai nao tem `Atlas`), mas o atlas
			// DELE pode ja estar no tileset por causa de outro typepath. Resolve sem REGISTRAR
			// nada novo: o objetivo e marcar parede no que ja existe, nao inchar o arquivo com
			// folhas que ninguem pediu.
			Fonte? f = td.Atlas != null && fontes.TryGetValue(td.Atlas, out Fonte? direta)
				? direta
				: ResolverExistente(td.Icon, td.IconState, atlasPorNome, fontes);
			if (f == null) continue;

			// acha o estado pelo nome pra saber quantos quadros e direcoes ele ocupa
			if (!f.StateIndex.TryGetValue(td.IconState ?? "", out int idx)) continue;

			DmiState? st = null;
			int passo = 0;
			foreach (DmiState cand in f.States)
			{
				int tam = Math.Max(1, cand.Dirs) * Math.Max(1, cand.Frames);
				if (passo == idx) { st = cand; break; }
				passo += tam;
			}
			if (st == null) continue;

			int total = Math.Max(1, st.Dirs) * Math.Max(1, st.Frames);
			for (int i = 0; i < total; i++)
			{
				(int X, int Y) c = Indice(f, idx + i);
				if (td.Density && f.Densas.Add(c)) n++;
				if (td.Opacity) f.Opacas.Add(c);
			}
		}
		return n;
	}

	/// <summary>Acha uma fonte JA REGISTRADA por nome + estado, sem criar nenhuma nova.</summary>
	private static Fonte? ResolverExistente(string icone, string? estado,
		Dictionary<string, List<string>> atlasPorNome, Dictionary<string, Fonte> fontes)
	{
		string nome = Path.GetFileNameWithoutExtension(icone);
		if (!atlasPorNome.TryGetValue(nome, out List<string>? candidatos)) return null;

		Fonte? primeira = null;
		foreach (string cand in candidatos)
		{
			if (!fontes.TryGetValue(cand, out Fonte? f)) continue;
			primeira ??= f;
			if (f.StateIndex.ContainsKey(estado ?? "")) return f;
		}
		return primeira;
	}

	/// <summary>
	/// PORTA. No BYOND ela e um turf DENSO que abre no `Enter()` -- e denso no papel, mas
	/// atravessavel na pratica. O port ainda nao tem sistema de portas, entao copiar a
	/// densidade crua deixa toda casa do mapa lacrada: parede com desenho de porta e nada por
	/// tras. Ate existir porta de verdade (abrir, fechar, trancar), a porta e passagem.
	/// </summary>
	private static bool EhPorta(string bp) =>
		bp.StartsWith("/turf/Door/", StringComparison.Ordinal) ||
		bp.StartsWith("/turf/build/Door/", StringComparison.Ordinal);

	/// <summary>
	/// Typepath que vira TILE na cena. Turf e o chao/parede; obj e tudo que fica em cima dele.
	/// `/mob` fica de fora: NPC nao e cenario, e entidade -- entra pelo servidor, nao pelo mapa.
	/// </summary>
	private static bool Desenhavel(string bp) =>
		bp.StartsWith("/turf", StringComparison.Ordinal) ||
		bp.StartsWith("/obj", StringComparison.Ordinal);

	/// <summary>
	/// Sobe a partir de <paramref name="daqui"/> ate achar a pasta com `project.godot`.
	/// Nulo se nao houver nenhuma.
	/// </summary>
	private static string? AcharRaiz(string daqui)
	{
		var d = new DirectoryInfo(Path.GetFullPath(daqui));
		while (d != null)
		{
			if (File.Exists(Path.Combine(d.FullName, "project.godot"))) return d.FullName;
			d = d.Parent;
		}
		return null;
	}

	/// <summary>
	/// Acha (ou cria) a fonte de atlas de um `icon` + `icon_state`.
	///
	/// QUANDO HA MAIS DE UM ARQUIVO COM O MESMO NOME, ganha o que TEM O ESTADO PEDIDO. Se
	/// nenhum tiver, ganha a folha MAIOR -- folha grande costuma ser a de cenario, e a pequena
	/// e a variante que colidiu de nome.
	/// </summary>
	private static Fonte? Garantir(string icone, string? estado, string raiz,
		Dictionary<string, Fonte> fontes, Dictionary<string, List<string>> atlasPorNome,
		HashSet<string> semAtlas, ref int proxId)
	{
		string chave = Path.GetFileNameWithoutExtension(icone);
		if (!atlasPorNome.TryGetValue(chave, out List<string>? candidatos) || candidatos.Count == 0)
		{
			semAtlas.Add(icone);
			return null;
		}

		string png = candidatos[0];
		if (candidatos.Count > 1)
		{
			string? escolhido = null;
			foreach (string cand in candidatos)
			{
				DmiFile.Result? m = DmiFile.Read(cand);
				if (m == null) continue;
				foreach (DmiState st in m.States)
					if (string.Equals(st.Name, estado ?? "", StringComparison.OrdinalIgnoreCase))
					{ escolhido = cand; break; }
				if (escolhido != null) break;
			}
			png = escolhido ?? candidatos.OrderByDescending(c => new FileInfo(c).Length).First();
		}

		// A CHAVE DO CACHE E O CAMINHO, nao o nome do icone: dois `Namek.dmi` diferentes sao
		// duas fontes diferentes, e guardar por nome faria um sobrescrever o outro de novo.
		if (fontes.TryGetValue(png, out Fonte? existente)) return existente;

		// o .dmi original guarda os metadados; o .png convertido e copia crua dele
		DmiFile.Result? meta = DmiFile.Read(png);
		if (meta == null) { semAtlas.Add(icone); return null; }

		var f = new Fonte
		{
			Id = proxId++,
			Chave = png,
			ResPath = "res://" + Path.GetRelativePath(raiz, png).Replace('\\', '/'),
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
		f.States = meta.States;
		f.TotalQuadros = idx;

		fontes[png] = f;
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
	/// <summary>
	/// O TileSet.
	///
	/// DECLARA TODO QUADRO DE TODO ATLAS, nao so os que o .dmm usou. A versao anterior so
	/// declarava as celulas que apareciam nos mapas -- 464 tiles de uns 20 mil -- e o efeito
	/// pratico era que no editor a esmagadora maioria dos sprites simplesmente NAO EXISTIA pra
	/// pintar. O tileset e a PALETA; ele tem que ter tudo que o .dmi tem.
	///
	/// E TRAZ AS ANIMACOES. Um estado de .dmi com varios quadros e um `delay` vira um tile
	/// ANIMADO do Godot -- agua correndo, porta abrindo, fogo. Antes cada quadro virava um
	/// tile parado e a agua ficava congelada no primeiro.
	/// </summary>
	private static void EscreverTileSet(string caminho, Dictionary<string, Fonte> fontes)
	{
		var ext = new StringBuilder();
		var sub = new StringBuilder();
		var res = new StringBuilder();
		int passos = 1;
		int totalTiles = 0, totalAnimados = 0;

		foreach (Fonte f in fontes.Values.OrderBy(v => v.Id))
		{
			string extId = $"{f.Id}_atlas";
			ext.Append($"[ext_resource type=\"Texture2D\" path=\"{f.ResPath}\" id=\"{extId}\"]\n");
			passos++;

			sub.Append($"[sub_resource type=\"TileSetAtlasSource\" id=\"Atlas_{f.Id}\"]\n");
			sub.Append($"texture = ExtResource(\"{extId}\")\n");
			sub.Append($"texture_region_size = Vector2i({f.IconW}, {f.IconH})\n");

			(int Tiles, int Animados) conta = DeclararTiles(sub, f);
			totalTiles += conta.Tiles;
			totalAnimados += conta.Animados;

			sub.Append('\n');
			passos++;

			res.Append($"sources/{f.Id} = SubResource(\"Atlas_{f.Id}\")\n");
		}

		Console.WriteLine($"tiles no tileset : {totalTiles} ({totalAnimados} animados)");

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
		Dictionary<string, List<string>> atlasPorNome, HashSet<string> semAtlas, ref int proxId,
		out HashSet<(int, int)> paredes, out HashSet<(int, int)> cegos)
	{
		// local de verdade: um `out` nao pode ser capturado por funcao local
		var muros = new HashSet<(int, int)>();

		// O QUE CEGA e diferente do que BLOQUEIA, e por isso sao dois mapas.
		//
		// O `opacity` do BYOND nao serve aqui: este jogo praticamente nao usa (93 ocorrencias no
		// codigo inteiro, quase todas `mouse_opacity`), ou seja, no original dava pra ver o
		// planeta todo atraves das casas. A regra que o dono pediu -- "nao ver atras de parede e
		// porta fechada" -- vira: TURF DENSO CEGA, OBJ DENSO NAO. Casa, muro e porta sao turf;
		// arvore, cerca e pedra sao obj. Sem essa separacao uma floresta viraria um labirinto de
		// pontos cegos, e nao e disso que se trata.
		var vendados = new HashSet<(int, int)>();
		// TileMapLayer.tile_map_data: [uint16 formato][por celula: 12 bytes]
		//   int16 x | int16 y | uint16 fonte | uint16 atlasX | uint16 atlasY | uint16 alternativa
		var bytes = new List<byte>();
		// O TileMapLayer (node novo) tem numeracao PROPRIA de formato e hoje so aceita 0.
		// Mandar 3 (o formato do TileMap ANTIGO) faz o Godot recusar o blob inteiro em silencio:
		// a cena carrega, o layer fica VAZIO e nada avisa alem de um erro no log.
		AddU16(bytes, 0);

		var decoracao = new List<byte>();
		AddU16(decoracao, 0);

		var objetos = new List<byte>();
		AddU16(objetos, 0);

		int usadas = 0, comObj = 0, objSemArte = 0;
		var semEstado = new Dictionary<string, int>(StringComparer.Ordinal);
		var luzes = new List<LuzDeTile>();

		// Poe UM typepath numa camada. Devolve se conseguiu desenhar.
		bool Por(string? bp, List<byte> destino, int x, int y)
		{
			if (bp == null) return false;
			if (!turfs.TryGetValue(bp, out TurfDef? td)) return false;

			// BORDA DO MUNDO. `/turf/Other/Blank` e denso, opaco e NAO TEM ICONE -- e o limite
			// do mapa, invisivel de proposito. A regra "so bloqueia o que da pra ver" (certa
			// pra arvore sem sprite) tirava a parede do vazio: quase 2 MILHOES de celulas, e
			// andares inteiros ficaram sem borda. Denso E SEM ICONE e geometria deliberada, nao
			// arte que faltou.
			if (td.Icon == null)
			{
				if (td.Density) { muros.Add((x, y)); vendados.Add((x, y)); }
				return false;
			}
			if (td.Atlas == null || !fontes.TryGetValue(td.Atlas, out Fonte? f)) return false;

			string? estado = EstadoDaCelula(td, x, y, nivel.Height);
			if (estado != null && !f.StateIndex.ContainsKey(estado))
			{
				string chave = $"{bp} -> {td.Icon} estado \"{estado}\"";
				semEstado[chave] = semEstado.GetValueOrDefault(chave) + 1;
			}

			(int X, int Y) c = Coord(f, estado);
			AddI16(destino, (short)x);
			AddI16(destino, (short)y);
			AddU16(destino, (ushort)f.Id);
			AddU16(destino, (ushort)c.X);
			AddU16(destino, (ushort)c.Y);
			AddU16(destino, 0);
			usadas++;

			// SO BLOQUEIA O QUE FOI DESENHADO -- e a porta e a excecao declarada, senao a casa
			// fica lacrada com um desenho de porta na frente.
			if (td.Density && !EhPorta(bp)) muros.Add((x, y));

			// ...e a porta CEGA mesmo sem bloquear: ela esta fechada no desenho. Ver o comentario
			// de `vendados` la em cima pra saber por que so turf entra aqui.
			if (td.Density && bp.StartsWith("/turf", StringComparison.Ordinal)) vendados.Add((x, y));

			// FONTE DE LUZ. Fogueira, tocha, lampada e lava acendem o cenario -- ver LightCatalog.
			if (LightCatalog.Da(bp) is { } luz)
				luzes.Add(new LuzDeTile(x, y, luz.Raio, luz.Cor, luz.Forca, luz.Tremula));

			return true;
		}
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				// O ULTIMO TURF VENCE. No DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o
				// anterior -- quem se ve e o do FIM da lista, nao o do comeco. Pegar o primeiro
				// jogava fora tudo que estava POR CIMA do chao: a porta da casa, o litoral
				// curvo, as plantas, as cadeiras, as pedras, as mesas. So na Terra sao 575
				// turfs em 572 celulas, e e metade da queixa "falta coisa no mapa".
				string? fundo = null, topo = null, objeto = null;
				bool tinhaObj = false;

				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal))
					{
						fundo ??= bp;
						topo = bp;                        // sempre o ultimo visto
					}
					else if (bp.StartsWith("/obj", StringComparison.Ordinal))
					{
						tinhaObj = true;
						objeto ??= bp;
					}
				}

				// uma celula com um turf so nao precisa de decoracao; com dois ou mais, o
				// primeiro e o chao e o ULTIMO vai por cima
				bool empilhado = topo != null && !ReferenceEquals(fundo, topo) && fundo != topo;
				Por(fundo, bytes, x, y);
				if (empilhado) Por(topo, decoracao, x, y);

				bool posObj = Por(objeto, objetos, x, y);
				if (posObj) comObj++;
				if (tinhaObj && !posObj) objSemArte++;
			}

		var sb = new StringBuilder();
		sb.Append("[gd_scene load_steps=2 format=3]\n\n");
		sb.Append("[ext_resource type=\"TileSet\" path=\"res://Assets/Maps/tileset.tres\" id=\"1_ts\"]\n\n");
		// A RAIZ ORDENA: sem `y_sort_enabled` aqui as camadas nao se misturam com os
		// personagens -- o Godot so funde a ordenacao quando ela e continua de pai pra filho.
		sb.Append($"[node name=\"{nome}\" type=\"Node2D\"]\n");
		sb.Append("y_sort_enabled = true\n\n");

		// O CHAO NUNCA ORDENA e fica sempre embaixo: e chao. Ordenar 250 mil tiles de grama
		// contra os personagens seria caro e nao mudaria nada -- ninguem passa atras da grama.
		sb.Append("[node name=\"Chao\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("z_index = -1\n");
		sb.Append("tile_set = ExtResource(\"1_ts\")\n");
		sb.Append($"tile_map_data = PackedByteArray({string.Join(", ", bytes)})\n\n");

		// DECORACAO: o turf que estava POR CIMA do chao no prefab. E onde moram a porta, o
		// litoral curvo, as plantas e as cadeiras -- tudo que o "primeiro turf vence" comia.
		sb.Append("[node name=\"Decor\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("y_sort_enabled = true\n");
		sb.Append("tile_set = ExtResource(\"1_ts\")\n");
		sb.Append($"tile_map_data = PackedByteArray({string.Join(", ", decoracao)})\n\n");

		// SEGUNDA CAMADA: o que fica EM CIMA do chao. Precisa ser um layer proprio porque o
		// TileMapLayer guarda UM tile por celula -- arvore e grama na mesma celula sao duas
		// camadas, nao duas entradas na mesma.
		// OBJETOS ORDENAM POR Y. E o que poe o personagem ATRAS da arvore quando ele esta
		// acima dela e NA FRENTE quando esta abaixo. `z_index` fixo mataria isso: dentro de um
		// mesmo z quem decide e o Y, entre z diferentes quem decide e sempre o z.
		sb.Append("[node name=\"Objetos\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("y_sort_enabled = true\n");
		sb.Append("tile_set = ExtResource(\"1_ts\")\n");
		sb.Append($"tile_map_data = PackedByteArray({string.Join(", ", objetos)})\n");

		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
		LightCatalog.Escrever(Path.ChangeExtension(caminho, ".luz"), luzes);
		if (luzes.Count > 0) Console.WriteLine($"  {nome}: {luzes.Count} fontes de luz");
		paredes = muros;
		cegos = vendados;
		if (comObj > 0 || objSemArte > 0)
			Console.WriteLine($"  {nome}: {comObj} objetos desenhados"
							  + (objSemArte > 0 ? $" | {objSemArte} celulas com objeto SEM arte" : ""));

		// QUADRO 0 SILENCIOSO. Quando o `icon_state` do typepath nao existe no atlas, o Coord
		// devolve o quadro 0 sem dizer nada -- e o quadro 0 de uma folha qualquer pode ser um
		// Namekuseijin. Foi exatamente assim que as casas apareceram com parede de
		// Namekuseijin. Agora isto e um relatorio, nao uma surpresa na tela.
		if (semEstado.Count > 0)
		{
			int total = semEstado.Values.Sum();
			Console.WriteLine($"     ATENCAO: {total} celulas cairam no quadro 0 " +
							  $"({semEstado.Count} estados que o atlas nao tem)");
			foreach ((string q, int n) in semEstado.OrderByDescending(kv => kv.Value).Take(6))
				Console.WriteLine($"        {n,7}x  {q}");
		}
		return usadas;
	}

	/// <summary>
	/// Declara os tiles de UM atlas: um por quadro, com os estados de varios quadros virando
	/// tile ANIMADO.
	///
	/// COMO O .dmi GUARDA OS QUADROS -- e a armadilha central daqui: a ordem e
	/// POR QUADRO, POR DIRECAO. Um estado com 4 direcoes e 2 quadros ocupa oito celulas assim:
	///
	///     q1d1 q1d2 q1d3 q1d4 q2d1 q2d2 q2d3 q2d4
	///
	/// ou seja, os quadros de UMA MESMA direcao ficam a `dirs` celulas de distancia, nao
	/// coladas. E por isso que a animacao usa `animation_separation = (dirs-1, 0)`: sem ela, a
	/// agua virada pro norte tocaria os quadros do leste, do oeste e do sul.
	///
	/// O Godot exige que os quadros de uma animacao caibam na MESMA LINHA do atlas (com
	/// `animation_columns = 0`). Quando o estado atravessa a quebra de linha da folha, o tile
	/// vira estatico, quadro a quadro -- melhor um sprite parado que se pode pintar do que uma
	/// animacao que o editor recusa e leva a fonte inteira junto.
	/// </summary>
	private static (int Tiles, int Animados) DeclararTiles(StringBuilder sub, Fonte f)
	{
		var ocupadas = new HashSet<(int X, int Y)>();
		var linhas = new List<(int X, int Y, string Texto)>();
		int animados = 0;

		// SPRITE MAIOR QUE O TILE: onde ele encosta o chao.
		//
		// O Godot desenha a regiao do atlas CENTRADA na celula. O BYOND desenha a partir do
		// canto INFERIOR ESQUERDO -- uma arvore de 96x96 cresce pra cima e pra direita, e a
		// celula dela e o TRONCO. Centrada, a arvore fica meia altura abaixo do lugar certo:
		// o desenho sai deslocado e a colisao parece estar "no meio da arvore".
		//
		// `texture_origin` e SUBTRAIDO da posicao de desenho, entao positivo move pra CIMA e
		// pra ESQUERDA. Levar a base ao pe da celula: y = +(altura-32)/2. Alinhar a esquerda
		// como o BYOND: x = -(largura-32)/2.
		int desY = (f.IconH - Cell) / 2;
		int desX = -(f.IconW - Cell) / 2;
		bool grande = f.IconW != Cell || f.IconH != Cell;

		void Ancora((int X, int Y) c, StringBuilder onde)
		{
			if (!grande) return;
			onde.Append($"{c.X}:{c.Y}/0/texture_origin = Vector2i({desX}, {desY})\n");
			// ORDENA PELA BASE, nao pelo centro: e o que faz o personagem passar ATRAS da copa
			// e NA FRENTE do tronco. Meio tile abaixo do centro = o pe da celula.
			onde.Append($"{c.X}:{c.Y}/0/y_sort_origin = {Cell / 2}\n");
		}

		// A CAIXA E DE UMA CELULA, nao do tamanho do desenho. No BYOND densidade e propriedade do
		// TURF, e um turf e um tile: a arvore de 96x96 ocupa UM tile (o do tronco) e o resto e
		// copa que se atravessa. Usando a extensao do icone, a arvore virava um bloco de 3x3
		// centrado no MEIO dela -- e essa e a queixa "a hitbox esta no meio e nao na base".
		string m = Inv(-Cell / 2f), p = Inv(Cell / 2f);
		string umaCelula = $"PackedVector2Array({m}, {m}, {p}, {m}, {p}, {p}, {m}, {p})";

		void Fisica((int X, int Y) c, StringBuilder onde)
		{
			if (f.Densas.Contains(c))
				onde.Append($"{c.X}:{c.Y}/0/physics_layer_0/polygon_0/points = {umaCelula}\n");
			if (f.Opacas.Contains(c))
				onde.Append($"{c.X}:{c.Y}/0/occlusion_layer_0/polygon = {umaCelula}\n");
		}

		bool Estatico(int indice)
		{
			(int X, int Y) c = Indice(f, indice);
			if (!ocupadas.Add(c)) return false;
			var b = new StringBuilder();
			b.Append($"{c.X}:{c.Y}/0 = 0\n");
			Ancora(c, b);
			Fisica(c, b);
			linhas.Add((c.X, c.Y, b.ToString()));
			return true;
		}

		int idx = 0;
		foreach (DmiState st in f.States)
		{
			int dirs = Math.Max(1, st.Dirs);
			int quadros = Math.Max(1, st.Frames);

			for (int d = 0; d < dirs; d++)
			{
				(int X, int Y) baseC = Indice(f, idx + d);

				// cabe animar? todos os quadros desta direcao tem que ficar na MESMA linha
				bool cabe = quadros > 1 && baseC.X + (quadros - 1) * dirs < f.Cols
							&& idx + (quadros - 1) * dirs + d < f.TotalQuadros;

				if (!cabe)
				{
					for (int q = 0; q < quadros; q++) Estatico(idx + q * dirs + d);
					continue;
				}

				var b = new StringBuilder();
				b.Append($"{baseC.X}:{baseC.Y}/0 = 0\n");
				// 0 = todos os quadros numa linha so
				b.Append($"{baseC.X}:{baseC.Y}/animation_columns = 0\n");
				if (dirs > 1)
					b.Append($"{baseC.X}:{baseC.Y}/animation_separation = Vector2i({dirs - 1}, 0)\n");
				b.Append($"{baseC.X}:{baseC.Y}/animation_speed = 1.0\n");

				for (int q = 0; q < quadros; q++)
				{
					// o `delay` do BYOND e em DECIMOS de segundo; o Godot quer segundos
					double d10 = q < st.Delays.Length ? st.Delays[q] : 1;
					double seg = Math.Max(d10, 0.1) / 10.0;
					b.Append($"{baseC.X}:{baseC.Y}/animation_frame_{q}/duration = " +
							 seg.ToString("0.####", CultureInfo.InvariantCulture) + "\n");
					ocupadas.Add((baseC.X + q * dirs, baseC.Y));
				}
				Ancora(baseC, b);
				Fisica(baseC, b);
				linhas.Add((baseC.X, baseC.Y, b.ToString()));
				animados++;
			}

			idx += dirs * quadros;
		}

		// sobras: quadros que nenhum estado reivindicou (folha com celulas soltas). Entram
		// como tile parado -- se esta desenhado na imagem, o editor tem que deixar pintar.
		for (int i = 0; i < f.TotalQuadros; i++) Estatico(i);

		foreach ((int _, int _y, string texto) in linhas.OrderBy(l => l.Y).ThenBy(l => l.X))
			sub.Append(texto);

		return (linhas.Count, animados);
	}

	/// <summary>
	/// O ESTADO que esta celula usa. Quase sempre e o do typepath -- menos nos TURFS HD, onde
	/// o desenho e um MOSAICO montado por coordenada.
	///
	/// O `autofill()` do original (`Turfs.dm:50-57`) roda no `New()` de cada turf e escreve
	/// `icon_state = "[x % (getWidth/32)],[y % (getHeight/32)]"`, ou `"[x&1],[y&1]"` quando o
	/// tipo nao declara tamanho. Como isso mora num corpo de proc, o scanner nunca viu: o
	/// estado ficava vazio e TODA celula HD desenhava o mesmo quadro. Sao 55 mil celulas na
	/// Terra -- 22% do planeta com a mesma grama errada.
	///
	/// O EIXO Y: o `autofill` usa o y do BYOND, que conta DE BAIXO PRA CIMA, e o DmmMap guarda
	/// em ordem de arquivo (linha 0 no topo). Sem converter, o mosaico sai espelhado na
	/// vertical -- funciona, e fica sutilmente errado, que e pior que quebrar.
	/// </summary>
	private static string? EstadoDaCelula(TurfDef td, int x, int y, int altura)
	{
		if (!td.IsHD) return td.IconState;
		// tipo HD que ja declara o estado na mao (WaterHD1) manda mais que o autofill
		if (!string.IsNullOrEmpty(td.IconState)) return td.IconState;

		int bx = x + 1;              // o BYOND indexa a partir de 1
		int by = altura - y;         // e conta de baixo pra cima

		if (td.GetWidth > 0 && td.GetHeight > 0)
			return $"{bx % (td.GetWidth / Cell)},{by % (td.GetHeight / Cell)}";

		return $"{bx & 1},{by & 1}";
	}

	/// <summary>Indice linear de quadro -> coordenada na grade do atlas.</summary>
	private static (int X, int Y) Indice(Fonte f, int i) => (i % f.Cols, i / f.Cols);

	private static string Inv(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

	/// <summary>
	/// Mapa de colisao compacto: 1 BIT por celula. Um andar de 500x500 cabe em ~31 KB, entao
	/// o servidor carrega a geometria de todas as zonas sem instanciar cena nenhuma (uma cena
	/// de 250 mil tiles no headless seria absurdo). E o mesmo dado que o cliente usa via
	/// TileMap, so que numa forma que o Core consegue ler sem tocar no Godot.
	/// Cabecalho: "JCOL" + uint16 largura + uint16 altura, depois o bitset em ordem de linha.
	/// </summary>
	/// <summary>
	/// Mapa de colisao compacto: 1 BIT por celula. Um andar de 500x500 cabe em ~31 KB, entao
	/// o servidor carrega a geometria de todas as zonas sem instanciar cena nenhuma.
	/// Cabecalho: "JCOL" + uint16 largura + uint16 altura, depois o bitset em ordem de linha.
	///
	/// As paredes chegam PRONTAS de quem desenhou a cena -- ver EscreverCena.
	/// </summary>
	private static int EscreverColisao(string caminho, int w, int h, HashSet<(int, int)> paredes)
	{
		var bits = new byte[(w * h + 7) / 8];
		int bloqueadas = 0;

		foreach ((int x, int y) in paredes)
		{
			if (x < 0 || y < 0 || x >= w || y >= h) continue;
			int i = y * w + x;
			bits[i >> 3] |= (byte)(1 << (i & 7));
			bloqueadas++;
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
