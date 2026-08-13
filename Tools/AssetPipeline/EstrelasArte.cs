using System.IO.Compression;
using System.Text;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// A ARTE DAS ESTRELAS, MEDIDA E ENCOLHIDA (`estrelas`).
///
/// ============================ POR QUE ISTO E UMA FERRAMENTA E NAO UMA TABELA ============================
/// A arte veio em 32 folhas de 4096x4096 RGBA. Duas coisas precisam sair delas, e nenhuma das duas
/// pode ser digitada a mao:
///
///   1. O RAIO DO NUCLEO. `Sistemas.RaioDaClasse` diz o raio LETAL em pixels de mundo, e o desenho
///      tem que se ajustar a esse numero -- nao o contrario. Mas o nucleo opaco de cada folha nao
///      ocupa a folha inteira: sobra brilho, coroa e halo em volta. Copiar o `Scale = Raio*2/lado`
///      que o `PlanetaDesenhado` usa (onde o disco ENCOSTA na borda do quadro de 128 px) desenharia
///      uma estrela cujo nucleo aparente e MENOR que o raio que mata -- e o jogador morre "no vazio"
///      ao lado de uma estrela que parecia estar longe. A razao nucleo/meio-lado e diferente por
///      familia, entao ela e medida aqui, folha por folha.
///
///   2. UMA COPIA QUE CABE NUMA TELA. 4096x4096 RGBA sao 64 MB de textura descompactada POR FOLHA.
///      Abrir a tela do sistema carregando isso e um engasgo de segundos e memoria de video que
///      nenhuma interface precisa: a estrela e desenhada com no maximo ~400 px de lado. Esta
///      ferramenta escreve a copia de 512 px que o jogo carrega, e as folhas gigantes ficam no
///      repositorio como FONTE, sem nunca entrar no jogo.
/// ==================================================================================================
///
/// ============================ E O IMPORT, QUE E ONDE ISTO JA MORREU UMA VEZ ============================
/// PNG escrito nao e textura. O Godot so o carrega depois de importar (o `.png.import` ao lado + o
/// `.godot/imported`), e o modo de falha e o pior possivel: o arquivo esta la, o codigo aponta pra
/// ele, e o jogo responde "No loader found for resource" so quando alguem abre a tela. O
/// `AtlasAnimado` ja pagou por isso -- 35 atlas escritos e ZERO importados, 178 animacoes mortas.
/// Por isso esta ferramenta chama o import e depois CONFERE que cada `.import` existe.
/// ====================================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- estrelas [raizDoProjeto]
/// </summary>
public static class EstrelasArte
{
	/// <summary>
	/// O NOME DE ARQUIVO DE CADA CLASSE, na ordem do <see cref="ClasseDeEstrela"/>.
	///
	/// ESTA TABELA E A AUTORIDADE, E NAO A ARVORE DE PASTAS. `Stars/Giant/` guarda `star_blue` e
	/// `star_orange`, que sao de sequencia principal, e `Stars/Main sequence/` guarda
	/// `star_white_giant`, que e gigante -- as pastas mentem, exatamente como o
	/// <see cref="ClasseDeEstrela"/> ja documenta. Por isso a busca aqui e por NOME DE ARQUIVO em
	/// toda a arvore, e nao por pasta.
	/// </summary>
	private static readonly string[] Familias =
	[
		"star_red",          // AnaVermelha
		"star_orange",       // Laranja
		"star_yellow",       // Amarela
		"star_white",        // Branca
		"star_blue",         // Azul
		"star_white_giant",  // GiganteBranca
		"star_red_giant",    // GiganteVermelha
		"star_blue_giant",   // GiganteAzul
	];

	/// <summary>Quantas folhas numeradas cada familia tem (`star_red01`..`04`).</summary>
	private const int Variantes = 4;

	/// <summary>
	/// O lado da copia que o jogo carrega.
	///
	/// 512 e nao 256: a estrela e o centro da tela do sistema e chega a ~400 px de lado num
	/// enquadramento fechado, e uma copia de 256 apareceria borrada justamente onde ela e o assunto.
	/// 512x512 RGBA sao 1 MB de textura -- 64 vezes menos que a folha original.
	/// </summary>
	private const int LadoDaCopia = 512;

	/// <summary>Onde as copias vao. Pasta PLANA de proposito: as pastas de origem mentem.</summary>
	private const string PastaDaCopia = "Assets/Sprites/Stars/Carta";

	private const string Json = "Assets/Data/estrelas.json";

	/// <summary>
	/// ALFA A PARTIR DO QUAL O PIXEL E "NUCLEO".
	///
	/// 250 e nao 255: as folhas sao arte pintada, e o miolo tem pixels de 252-254 por ruido do
	/// anti-serrilhado. Exigir 255 devolveria um nucleo esburacado e a medida cairia pra onde o
	/// primeiro furo aparece, que e aleatorio. O que se quer e "aqui a estrela e solida".
	/// </summary>
	private const byte AlfaDeNucleo = 250;

	/// <summary>Alfa a partir do qual o pixel ainda APARECE. Abaixo disto o brilho ja e invisivel.</summary>
	private const byte AlfaVisivel = 8;

	public static int Run(string raizDoProjeto)
	{
		raizDoProjeto = Path.GetFullPath(raizDoProjeto);
		string pastaStars = Path.Combine(raizDoProjeto, "Assets/Sprites/Stars");
		if (!Directory.Exists(pastaStars))
		{
			Console.WriteLine($"ERRO: nao achei {pastaStars}");
			return 1;
		}

		// A BUSCA E RECURSIVA E POR NOME. Ver o comentario de `Familias`: a pasta nao classifica.
		var porNome = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string f in Directory.GetFiles(pastaStars, "*.png", SearchOption.AllDirectories))
		{
			if (f.Replace('\\', '/').Contains("/" + Path.GetFileName(PastaDaCopia) + "/", StringComparison.OrdinalIgnoreCase))
				continue;   // as copias que esta ferramenta ja escreveu nao sao fonte
			porNome[Path.GetFileNameWithoutExtension(f)] = f;
		}

		string destino = Path.Combine(raizDoProjeto, PastaDaCopia);
		Directory.CreateDirectory(destino);

		Console.WriteLine("=== ARTE DAS ESTRELAS: MEDIDA E COPIA ===");
		Console.WriteLine($"{"FOLHA",-20} {"LADO",6} {"NUCLEO",8} {"BRILHO",8} {"NUCLEO(cor)",12} {"HALO(cor)",10} {"DESLIZE",8}");
		Console.WriteLine(new string('-', 80));

		var linhas = new List<string>();
		int faltando = 0, escritas = 0;
		double piorDeslize = 0;

		for (int c = 0; c < Familias.Length; c++)
		{
			for (int v = 0; v < Variantes; v++)
			{
				string nome = $"{Familias[c]}{v + 1:00}";
				if (!porNome.TryGetValue(nome, out string? caminho))
				{
					// FALHA ALTA (regra 0.5): uma familia sem arte faria a classe sortear um caminho
					// que nao existe, e o Godot devolveria textura nula -- estrela invisivel, calada.
					Console.WriteLine($"{nome,-20}   FALTANDO -- a classe {(ClasseDeEstrela)c} ficaria sem arte");
					faltando++;
					continue;
				}

				Folha img = LerPng(caminho);
				Medida m = Medir(img);
				piorDeslize = Math.Max(piorDeslize, m.Deslize);

				string arquivo = Path.Combine(destino, nome + ".png");
				EscreverPng(arquivo, Encolher(img, LadoDaCopia));
				escritas++;

				Console.WriteLine($"{nome,-20} {img.Lado,6} {m.Nucleo,8:0.000} {m.Brilho,8:0.000} "
								  + $"{m.Cor,12} {m.Halo,10} {m.Deslize,8:0.0}");

				linhas.Add($"    {{ \"classe\": \"{(ClasseDeEstrela)c}\", \"variante\": {v}, "
						   + $"\"arquivo\": \"res://{PastaDaCopia}/{nome}.png\", "
						   + $"\"nucleo\": {Num(m.Nucleo)}, \"brilho\": {Num(m.Brilho)}, "
						   + $"\"cor\": \"{m.Cor}\", \"halo\": \"{m.Halo}\" }}");
			}
		}

		// O DESLIZE DO CENTRO importa porque o desenho poe o centro da folha EM CIMA da posicao da
		// estrela: se a arte estiver descentrada, o nucleo desenhado fica fora do raio que mata.
		Console.WriteLine($"\npior deslize do centro: {piorDeslize:0.0} px de 4096 "
						  + (piorDeslize < 24 ? "(centrada)" : "*** DESCENTRADA -- o desenho vai mentir ***"));

		string caminhoJson = Path.Combine(raizDoProjeto, Json);
		File.WriteAllText(caminhoJson,
			"{\n"
			+ $"  \"lado\": {LadoDaCopia},\n"
			+ "  \"folhas\": [\n"
			+ string.Join(",\n", linhas) + "\n"
			+ "  ]\n}\n",
			new UTF8Encoding(false));
		Console.WriteLine($"escrito: {caminhoJson}  ({linhas.Count} folhas)");

		if (escritas > 0) Importar(raizDoProjeto, destino, escritas);
		return faltando > 0 ? 1 : 0;
	}

	/// <summary>
	/// Numero em JSON com ponto decimal, SEMPRE.
	///
	/// Nesta maquina o separador da cultura e a virgula, e `0,5731` num JSON e um arquivo quebrado
	/// que o Godot recusa inteiro -- a tela do sistema abriria sem estrela e sem erro que apontasse
	/// pra ca. E o mesmo tipo de armadilha do "silencio no lugar de erro".
	/// </summary>
	private static string Num(double v) =>
		v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

	// =====================================================================
	// A MEDIDA
	// =====================================================================
	private readonly record struct Medida(double Nucleo, double Brilho, string Cor, string Halo, double Deslize);

	/// <summary>
	/// O QUE SE MEDE NUMA FOLHA.
	///
	/// Por PERFIL RADIAL e nao por pixel solto: uma folha pintada tem pontos brilhantes soltos no
	/// halo, e "o pixel mais longe que ainda tem alfa" devolveria o raio da folha inteira em toda
	/// familia. O que se quer e onde o DISCO acaba, entao a conta e a media do anel.
	///
	///   * NUCLEO -- o anel mais externo em que a media de alfa ainda passa de <see cref="AlfaDeNucleo"/>.
	///     E a "superficie" da estrela: e este raio que o desenho tem que casar com o raio letal.
	///   * BRILHO -- o anel mais externo com media acima de <see cref="AlfaVisivel"/>. Diz quanto da
	///     folha e halo, e portanto quanto a textura precisa transbordar do nucleo na tela.
	///   * COR    -- a media do nucleo. E a cor da estrela onde ela e solida.
	///   * HALO   -- a media do anel ENTRE o nucleo e a borda do brilho.
	///
	/// ============================ POR QUE DUAS CORES, E QUAL VAI PRA CARTA ============================
	/// A medida mostrou uma coisa que eu ia errar: **o nucleo e branco-quente em quase toda familia**.
	/// A ana vermelha mede `f4bc92` no miolo -- um pessego palido, praticamente indistinguivel do
	/// `f1d77e` da laranja. Pintar o pontinho da carta com a cor do nucleo faria oito classes virarem
	/// tres tons de creme, e a classe da estrela -- que e a informacao que a carta existe pra dar --
	/// sumiria.
	///
	/// O que da cor a uma estrela nesta arte e a COROA, e nao o miolo. Entao a carta usa o halo e a
	/// tela do sistema usa a folha inteira. Nenhuma das duas cores foi escolhida a olho.
	/// ==============================================================================================
	/// </summary>
	private static Medida Medir(Folha f)
	{
		int lado = f.Lado, meio = lado / 2;
		int nAneis = meio + 1;
		var somaA = new double[nAneis];
		var conta = new double[nAneis];
		var somaR = new double[nAneis];
		var somaG = new double[nAneis];
		var somaB = new double[nAneis];

		double cx = 0, cy = 0, peso = 0;
		double sr = 0, sg = 0, sb = 0, sn = 0;

		for (int y = 0; y < lado; y++)
		{
			double dy = y + 0.5 - meio;
			int linha = y * lado * 4;
			for (int x = 0; x < lado; x++)
			{
				int i = linha + x * 4;
				byte a = f.Px[i + 3];
				double dx = x + 0.5 - meio;

				if (a > 0) { cx += x * (double)a; cy += y * (double)a; peso += a; }

				int r = (int)Math.Sqrt(dx * dx + dy * dy);
				if (r < nAneis)
				{
					somaA[r] += a; conta[r]++;
					// A COR DO ANEL VEM PONDERADA PELO ALFA: um pixel transparente nao tem cor, e
					// media-la com os opacos puxaria todo halo pro preto do fundo.
					somaR[r] += f.Px[i] * (double)a;
					somaG[r] += f.Px[i + 1] * (double)a;
					somaB[r] += f.Px[i + 2] * (double)a;
				}

				if (a >= AlfaDeNucleo) { sr += f.Px[i]; sg += f.Px[i + 1]; sb += f.Px[i + 2]; sn++; }
			}
		}

		int nucleo = 0, brilho = 0;
		for (int r = 0; r < nAneis; r++)
		{
			if (conta[r] == 0) continue;
			double media = somaA[r] / conta[r];
			if (media >= AlfaDeNucleo) nucleo = r;
			if (media >= AlfaVisivel) brilho = r;
		}

		double deslize = peso > 0
			? Math.Sqrt(Math.Pow(cx / peso - (meio - 0.5), 2) + Math.Pow(cy / peso - (meio - 0.5), 2))
			: 0;

		string cor = sn > 0
			? Hexa(sr / sn, sg / sn, sb / sn)
			: "ffffff";

		// O HALO E O ANEL ENTRE A SUPERFICIE E A BORDA DO BRILHO -- e o pedaco cromatico da folha.
		double hr = 0, hg = 0, hb = 0, ha = 0;
		for (int r = nucleo + 1; r <= brilho && r < nAneis; r++)
		{
			hr += somaR[r]; hg += somaG[r]; hb += somaB[r]; ha += somaA[r];
		}
		string halo = ha > 0 ? Hexa(hr / ha, hg / ha, hb / ha) : cor;

		return new Medida(nucleo / (double)meio, brilho / (double)meio, cor, halo, deslize);
	}

	private static string Hexa(double r, double g, double b) =>
		$"{(int)Math.Clamp(Math.Round(r), 0, 255):x2}"
		+ $"{(int)Math.Clamp(Math.Round(g), 0, 255):x2}"
		+ $"{(int)Math.Clamp(Math.Round(b), 0, 255):x2}";

	// =====================================================================
	// O ENCOLHIMENTO
	// =====================================================================
	/// <summary>
	/// Reduz por media de caixa, COM ALFA PRE-MULTIPLICADO.
	///
	/// Sem a pre-multiplicacao, a media mistura a cor de pixels transparentes (que nas folhas e
	/// preto) com a dos opacos, e a borda do disco ganha uma auréola escura de um pixel -- o
	/// classico halo de redimensionamento. Como a copia e desenhada com o nucleo enorme na tela,
	/// esse um pixel viraria uma borda preta bem visivel em volta da estrela.
	/// </summary>
	private static Folha Encolher(Folha f, int lado)
	{
		int passo = f.Lado / lado;
		if (passo < 1 || passo * lado != f.Lado)
			throw new InvalidOperationException($"folha de {f.Lado} nao reduz exato pra {lado}");

		var saida = new byte[lado * lado * 4];
		double n = passo * (double)passo;

		for (int y = 0; y < lado; y++)
			for (int x = 0; x < lado; x++)
			{
				double sr = 0, sg = 0, sb = 0, sa = 0;
				for (int j = 0; j < passo; j++)
				{
					int linha = ((y * passo + j) * f.Lado + x * passo) * 4;
					for (int i = 0; i < passo; i++)
					{
						int p = linha + i * 4;
						double a = f.Px[p + 3];
						sr += f.Px[p] * a; sg += f.Px[p + 1] * a; sb += f.Px[p + 2] * a;
						sa += a;
					}
				}

				int o = (y * lado + x) * 4;
				saida[o + 3] = (byte)Math.Round(sa / n);
				if (sa > 0)
				{
					saida[o] = (byte)Math.Clamp(Math.Round(sr / sa), 0, 255);
					saida[o + 1] = (byte)Math.Clamp(Math.Round(sg / sa), 0, 255);
					saida[o + 2] = (byte)Math.Clamp(Math.Round(sb / sa), 0, 255);
				}
			}

		return new Folha(lado, saida);
	}

	// =====================================================================
	// PNG -- SO O QUE ESTAS FOLHAS SAO
	// =====================================================================
	/// <summary>Uma imagem quadrada RGBA de 8 bits. E o unico formato que estas 32 folhas usam.</summary>
	private readonly record struct Folha(int Lado, byte[] Px);

	/// <summary>
	/// Leitor de PNG -- 8 bits, RGBA, sem entrelacamento.
	///
	/// DELIBERADAMENTE ESTREITO. As 32 folhas foram conferidas e sao todas 4096x4096, bit 8, tipo 6,
	/// sem entrelacamento; um leitor geral seria dez vezes o codigo pra cobrir casos que nao existem
	/// nesta arvore. O que ele NAO faz e recusar calado: formato diferente estoura com a mensagem
	/// dizendo qual e (regra 0.5, "silencio no lugar de erro").
	/// </summary>
	private static Folha LerPng(string caminho)
	{
		byte[] b = File.ReadAllBytes(caminho);
		if (b.Length < 8 || b[0] != 0x89 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G')
			throw new InvalidDataException($"{caminho}: nao e PNG");

		int w = 0, h = 0, bits = 0, tipo = -1, entrelace = 0;
		var idat = new MemoryStream();

		int p = 8;
		while (p + 8 <= b.Length)
		{
			int tam = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
			string marca = Encoding.ASCII.GetString(b, p + 4, 4);
			int dados = p + 8;

			switch (marca)
			{
				case "IHDR":
					w = (b[dados] << 24) | (b[dados + 1] << 16) | (b[dados + 2] << 8) | b[dados + 3];
					h = (b[dados + 4] << 24) | (b[dados + 5] << 16) | (b[dados + 6] << 8) | b[dados + 7];
					bits = b[dados + 8]; tipo = b[dados + 9]; entrelace = b[dados + 12];
					break;
				case "IDAT":
					idat.Write(b, dados, tam);
					break;
			}

			p = dados + tam + 4;
			if (marca == "IEND") break;
		}

		if (bits != 8 || tipo != 6 || entrelace != 0)
			throw new InvalidDataException(
				$"{caminho}: esperava 8 bits RGBA sem entrelacamento, veio bits={bits} tipo={tipo} entrelace={entrelace}");
		if (w != h)
			throw new InvalidDataException($"{caminho}: esperava folha quadrada, veio {w}x{h}");

		idat.Position = 0;
		using var zip = new ZLibStream(idat, CompressionMode.Decompress);
		var cru = new byte[(long)h * (w * 4 + 1)];
		int lido = 0;
		while (lido < cru.Length)
		{
			int n = zip.Read(cru, lido, cru.Length - lido);
			if (n <= 0) throw new InvalidDataException($"{caminho}: IDAT acabou cedo ({lido}/{cru.Length})");
			lido += n;
		}

		// DESFILTRAGEM. Cada linha vem com um byte de filtro na frente, e os cinco filtros do PNG
		// referenciam o pixel a esquerda (a), o de cima (b) e o diagonal (c). Sem isto a imagem sai
		// como um borrao diagonal -- que e como um leitor incompleto falha, sem erro nenhum.
		var px = new byte[w * h * 4];
		int bpp = 4, linhaBytes = w * 4;
		for (int y = 0; y < h; y++)
		{
			int f = cru[y * (linhaBytes + 1)];
			int origem = y * (linhaBytes + 1) + 1;
			int saida = y * linhaBytes;
			int acima = (y - 1) * linhaBytes;

			for (int x = 0; x < linhaBytes; x++)
			{
				int bruto = cru[origem + x];
				int a = x >= bpp ? px[saida + x - bpp] : 0;
				int c = y > 0 && x >= bpp ? px[acima + x - bpp] : 0;
				int cima = y > 0 ? px[acima + x] : 0;

				int valor = f switch
				{
					0 => bruto,
					1 => bruto + a,
					2 => bruto + cima,
					3 => bruto + (a + cima) / 2,
					4 => bruto + Paeth(a, cima, c),
					_ => throw new InvalidDataException($"{caminho}: filtro {f} desconhecido na linha {y}"),
				};
				px[saida + x] = (byte)valor;
			}
		}

		return new Folha(w, px);
	}

	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}

	/// <summary>Escreve RGBA de 8 bits, uma linha por vez, com filtro 0. Simples de proposito.</summary>
	private static void EscreverPng(string caminho, Folha f)
	{
		var cru = new byte[f.Lado * (f.Lado * 4 + 1)];
		for (int y = 0; y < f.Lado; y++)
		{
			cru[y * (f.Lado * 4 + 1)] = 0;
			Array.Copy(f.Px, y * f.Lado * 4, cru, y * (f.Lado * 4 + 1) + 1, f.Lado * 4);
		}

		var comprimido = new MemoryStream();
		using (var zip = new ZLibStream(comprimido, CompressionLevel.SmallestSize, leaveOpen: true))
			zip.Write(cru, 0, cru.Length);

		using var fs = File.Create(caminho);
		fs.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

		var ihdr = new byte[13];
		Escrever32(ihdr, 0, f.Lado); Escrever32(ihdr, 4, f.Lado);
		ihdr[8] = 8; ihdr[9] = 6;   // 8 bits, RGBA
		Bloco(fs, "IHDR", ihdr);
		Bloco(fs, "IDAT", comprimido.ToArray());
		Bloco(fs, "IEND", []);
	}

	private static void Escrever32(byte[] d, int i, int v)
	{
		d[i] = (byte)(v >> 24); d[i + 1] = (byte)(v >> 16); d[i + 2] = (byte)(v >> 8); d[i + 3] = (byte)v;
	}

	private static void Bloco(Stream s, string marca, byte[] dados)
	{
		var tam = new byte[4];
		Escrever32(tam, 0, dados.Length);
		s.Write(tam);

		byte[] m = Encoding.ASCII.GetBytes(marca);
		s.Write(m); s.Write(dados);

		// O CRC DO PNG COBRE A MARCA E OS DADOS, nesta ordem -- e NAO o campo de tamanho. Errar o
		// alcance produz um arquivo que alguns leitores aceitam e o Godot recusa calado.
		uint crc = Crc(dados, Crc(m, 0xFFFFFFFF)) ^ 0xFFFFFFFF;
		var c = new byte[4];
		Escrever32(c, 0, (int)crc);
		s.Write(c);
	}

	private static readonly uint[] TabelaCrc = MontarCrc();

	private static uint[] MontarCrc()
	{
		var t = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
			t[n] = c;
		}
		return t;
	}

	/// <summary>CRC-32 do PNG, encadeavel: o resultado de um pedaco entra como `c` do proximo.</summary>
	private static uint Crc(byte[] d, uint c)
	{
		foreach (byte b in d) c = TabelaCrc[(c ^ b) & 0xFF] ^ (c >> 8);
		return c;
	}

	// =====================================================================
	// O IMPORT
	// =====================================================================
	/// <summary>
	/// Pede ao Godot que importe as copias novas, e depois CONFERE.
	///
	/// Mesmo caminho do <see cref="AtlasAnimado"/>, e pela mesma razao escrita la: PNG sem `.import`
	/// nao carrega, e o unico lugar em que isso aparece e o log do jogo quando alguem abre a tela.
	/// "Rodou sem erro" nao prova nada -- o que prova e o `.import` existir ao lado de cada arquivo.
	/// </summary>
	private static void Importar(string raizDoProjeto, string destino, int quantos)
	{
		string? godot = SceneBinary.AcharGodot();
		if (godot == null)
		{
			Console.WriteLine("AVISO: sem Godot -- as copias NAO foram importadas.");
			Console.WriteLine("       A tela do sistema vai abrir sem estrela nenhuma.");
			return;
		}

		Console.WriteLine($"  importando as {quantos} copias no Godot...");
		try
		{
			var psi = new System.Diagnostics.ProcessStartInfo(godot)
			{
				WorkingDirectory = raizDoProjeto,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			psi.ArgumentList.Add("--headless");
			psi.ArgumentList.Add("--path");
			psi.ArgumentList.Add(raizDoProjeto);
			psi.ArgumentList.Add("--import");

			using System.Diagnostics.Process? p = System.Diagnostics.Process.Start(psi);
			if (p == null) { Console.WriteLine("  ERRO: nao consegui rodar o Godot pro import"); return; }

			// OS DOIS FLUXOS DRENADOS AO MESMO TEMPO -- sem isso o Godot trava quando o stderr enche
			// (ele comenta cada arquivo importado). Armadilha ja documentada no `AtlasAnimado`.
			p.OutputDataReceived += (_, _) => { };
			p.ErrorDataReceived += (_, _) => { };
			p.BeginOutputReadLine();
			p.BeginErrorReadLine();

			if (!p.WaitForExit(10 * 60 * 1000))
			{
				p.Kill(entireProcessTree: true);
				Console.WriteLine("  ERRO: o import passou de 10 minutos -- abortado");
				return;
			}
		}
		catch (Exception e) { Console.WriteLine($"  ERRO no import: {e.Message}"); return; }

		int sem = Directory.GetFiles(destino, "*.png").Count(f => !File.Exists(f + ".import"));
		Console.WriteLine(sem == 0
			? "  copias importadas: todas"
			: $"  AVISO: {sem} copias SEM .import -- a tela do sistema nao vai desenhar estrela");
	}
}
