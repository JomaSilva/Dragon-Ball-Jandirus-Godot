using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
	/// <summary>
	/// A MARCA DE UMA CELULA: um numero que muda se qualquer campo dela mudar.
	///
	/// Somar os campos nao serviria -- trocar X por Y daria o mesmo total. Passar cada campo por
	/// uma mistura (FNV) faz duas celulas parecidas caírem longe uma da outra, e o XOR das marcas
	/// nao depende da ORDEM, que e justamente o que o agrupamento em pedacos muda.
	/// </summary>
	private static ulong Marca(Jandirus.Core.World.CelulaDePedaco c, int camada)
	{
		ulong h = 1469598103934665603UL;
		ulong[] campos = [(ushort)c.X, (ushort)c.Y, c.Fonte, c.Ax, c.Ay, (ulong)camada];
		foreach (ulong v in campos) h = (h ^ v) * 1099511628211UL;
		return h;
	}

	private static ulong Assinar(List<Jandirus.Core.World.CelulaDePedaco> celulas, int camada)
	{
		ulong x = 0;
		foreach (Jandirus.Core.World.CelulaDePedaco c in celulas) x ^= Marca(c, camada);
		return x;
	}

	private const int Cell = 32;

	/// <summary>
	/// AS CAMADAS NAO CONSTROEM FISICA -- e essa linha vale quase um segundo por mapa.
	///
	/// ============================ O QUE ELA CONSERTA ============================
	/// O tileset traz poligono de colisao em todo tile denso (447 deles), e um `TileMapLayer` com
	/// colisao ligada monta um corpo de fisica pra CADA celula solida no primeiro quadro. Sao tres
	/// camadas de 500x500 por planeta, e o trabalho todo cai num quadro so: medido, 798 ms de tela
	/// congelada so na Terra.
	///
	/// E o pior e que NINGUEM USA. O movimento deste jogo nao passa por fisica do Godot em lugar
	/// nenhum -- nao ha `CharacterBody2D`, `move_and_slide` nem consulta de mundo fisico no cliente
	/// inteiro. Quem decide onde da pra andar e o `ZoneCollision`, o bitset do `.col`, no cliente e
	/// no servidor. A fisica do tilemap era uma copia paralela da mesma verdade, construida toda
	/// troca de mapa e nunca consultada.
	///
	/// A OCLUSAO FICA. Ela e usada de verdade: e o que faz parede esconder o que esta atras
	/// (`Iluminacao` e `Visao`), e sai por outro campo do tileset.
	///
	/// O POLIGONO CONTINUA NO TILESET de proposito: ele nao custa nada parado, e e o que faz o
	/// EDITOR mostrar onde ha parede pra quem for pintar mapa a mao.
	/// ============================================================================
	/// </summary>
	private const string SemFisica = "collision_enabled = false\n";


	/// <summary>
	/// DEFEITO INJETADO (bancada): o estado volta a ser procurado SEM OLHAR A CAIXA, e ganha o primeiro
	/// da folha -- `Computer2` (a torre) cai em `computer2` (um pedaco de outra maquina). Falso na
	/// conversao, sempre.
	/// </summary>
	public static bool EstadoSemCaixaDeTeste;

	/// <summary>
	/// Um estado da folha: o nome EXATO que o `.dmi` declara, onde o primeiro quadro dele mora, e
	/// quantas direcoes e quadros ele ocupa (os quadros de uma direcao ficam a `Dirs` de distancia).
	/// </summary>
	private readonly record struct EstadoDaFolha(string Nome, int Indice, int Dirs, int Quadros);

	private sealed class Fonte
	{
		public int Id;
		public string ResPath = "";

		/// <summary>Caminho no DISCO -- e a chave do dicionario de fontes, e o que o TurfDef guarda.</summary>
		public string Chave = "";
		public int IconW, IconH, Cols;

		/// <summary>
		/// A ANCORA PROPRIA desta fonte: o `texture_origin` que TODO tile dela declara, no lugar do padrao
		/// da folha (centrado, base no pe da celula). Nula em toda fonte que nao e variante -- ver
		/// <see cref="AncoraDoTipo"/>.
		/// </summary>
		public (int X, int Y)? Origem;

		/// <summary>
		/// O que distingue uma VARIANTE da fonte comum da mesma folha: `@x,y`, a ancora dela. Duas fontes
		/// podem dividir a textura; o dicionario de fontes as separa por <see cref="Entrada"/>.
		/// </summary>
		public string Sufixo => Origem is { } o ? $"@{o.X},{o.Y}" : "";

		/// <summary>A chave desta fonte no dicionario de fontes (e o que o `TurfDef.Atlas` guarda).</summary>
		public string Entrada => Chave + Sufixo;

		/// <summary>
		/// O nome com que a fonte entra no `tiles.json`. Vazio = o nome do arquivo, que e o de toda fonte
		/// descoberta pela conversao cheia; as acrescentadas trazem o delas (ver <see cref="NomeNoIndice"/>).
		/// </summary>
		public string Nome = "";

		/// <summary>Os estados pelo nome EXATO. Nome repetido na mesma folha fica com o primeiro.</summary>
		public Dictionary<string, EstadoDaFolha> Exatos = new(StringComparer.Ordinal);

		/// <summary>
		/// Os mesmos estados SEM OLHAR A CAIXA, o primeiro de cada nome. E a rede de baixo do
		/// <see cref="Achar"/>, e e o que vai pro `tiles.json` -- o leitor dele (`CatalogoDeTiles`)
		/// tambem nao olha a caixa, entao mandar os dois nomes faria o ultimo apagar o primeiro la.
		/// </summary>
		public Dictionary<string, EstadoDaFolha> SemCaixa = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// O ESTADO QUE ESTE NOME PEDE: o de nome EXATO, e so na falta dele o que difere na caixa.
		///
		/// ============================ `Computer2` NAO E `computer2` ============================
		/// O BYOND casa `icon_state` letra por letra, e a `Lab.dmi` tem os dois: `computer2` (quadro
		/// 184, um PEDACO de outra maquina) e `Computer2` (quadro 293, a torre inteira). Procurando sem
		/// caixa ganhava o primeiro, e os 8 computadores dos laboratorios de Vegeta
		/// (`/obj/buildables/Computer2`, `buildobjects.dm:265-268`) saiam com o pedaco -- um dos "icones
		/// cortados" da queixa do dono.
		///
		/// A REDE SEM CAIXA FICA, e e divergencia declarada: no BYOND o nome que so difere na caixa nao
		/// casa, e o desenho cai no estado vazio da folha. Aqui ele continua casando, porque tira-la
		/// trocaria por outro desenho tudo que hoje depende dela -- e isso e uma decisao por tipo, nao um
		/// efeito colateral. O relatorio do andar conta quantas celulas passam por ela.
		/// ======================================================================================
		/// </summary>
		public EstadoDaFolha? Achar(string nome)
		{
			if (!EstadoSemCaixaDeTeste && Exatos.TryGetValue(nome, out EstadoDaFolha exato)) return exato;
			return SemCaixa.TryGetValue(nome, out EstadoDaFolha parecido) ? parecido : null;
		}

		/// <summary>Os estados como o .dmi os declara -- e daqui que saem as animacoes.</summary>
		public List<DmiState> States = [];

		/// <summary>Quantos quadros a folha tem de verdade (o resto da grade e sobra vazia).</summary>
		public int TotalQuadros;
		public HashSet<(int X, int Y)> Usadas = [];
		public HashSet<(int X, int Y)> Densas = [];
		public HashSet<(int X, int Y)> Opacas = [];

		/// <summary>
		/// O ATLAS COMPANHEIRO desta folha -- uma tira por estado que nao cabia numa linha.
		/// Nulo quando todas as animacoes desta folha ja cabiam. Ver <see cref="AtlasAnimado"/>.
		/// </summary>
		public Fonte? Companheira;

		/// <summary>
		/// Estado reempacotado (pelo nome EXATO, o mesmo que o <see cref="Achar"/> devolve) -> a LINHA
		/// dele no companheiro. Vazio = nada foi reempacotado.
		/// </summary>
		public Dictionary<string, int> Refeitos = new(StringComparer.Ordinal);

		/// <summary>Por linha do companheiro: quantos quadros e quanto dura cada um (ja em segundos).</summary>
		public List<double[]> Duracoes = [];
	}

	/// <param name="soFisica">
	/// SO A FISICA, E SO PRA LIBERTAR. A conversao cheia reescreve tileset, `tiles.json`, os 40
	/// `.tscn`/`.pedacos` e o indice de sprites -- que resolve nome repetido por `TryAdd` e trocaria 21
	/// artes sem ninguem ter pedido (ver a nota do comando `agua` em `Program.cs`). Com esta chave a
	/// MESMA passada roda, pela mesma regra, mas nada de arte e escrito: os `paredes`/`cegos` novos sao
	/// comparados com o `.col`/`.vis` do disco e so os bits que a regra nova NAO bloqueia mais sao
	/// apagados (`LibertarNaColisao`). Um bit novo que o disco nao tem e RELATADO, nunca gravado --
	/// ele viria de uma diferenca entre o indice em memoria e o tileset do disco, nao de mapa.
	/// </param>
	public static void Convert(string dmmDir, string spritesDir, string outDir, Dictionary<string, TurfDef> turfs,
							   bool soFisica = false) =>
		Passada(dmmDir, spritesDir, outDir, turfs, soFisica, trava: null);

	/// <summary>
	/// REPINTA UM ANDAR SO, COM AS FONTES PRESAS AS DO DISCO -- o comando `repintar`.
	///
	/// ============================ POR QUE NAO E A CONVERSAO CHEIA ============================
	/// O `id` de cada fonte do tileset nasce de um contador por ORDEM DE DESCOBERTA (`proxId++`), e o
	/// `.pedacos` de cada planeta guarda esse numero em toda celula. Reconverter um andar pela passada
	/// cheia renumeraria as fontes -- ou seja, obrigaria a reescrever o tileset, o `tiles.json` e os 40
	/// `.pedacos` pra consertar um planeta, e ainda trocaria de arquivo as folhas de nome repetido (o
	/// `DU/Items/Lab.png` contra o `Misc/Objects/Technology/Lab.png`: quem ganha e a ordem do disco).
	///
	/// Aqui as fontes NAO sao descobertas: sao SEMEADAS do `Assets/Data/tiles.json` (id, textura,
	/// colunas, tamanho do icone) e conferidas contra o `tileset.tres` ao lado. A MESMA passada roda,
	/// pelas mesmas regras de desenho e de fisica, e so o andar pedido sai -- numa pasta de rascunho.
	/// Tileset, indice, manifesto, as tiras `__anim` e as cenas binarias ficam como estao.
	/// ========================================================================================
	///
	/// ============================ FALHA ALTO, E NAO GRAVA NADA ============================
	/// Se o andar pedir uma folha que o tileset do disco nao tem, um estado que so existe noutro
	/// arquivo de mesmo nome, ou um quadro que o tileset nao declara (ou declara com outra animacao), a
	/// lista sai no console e NENHUM arquivo e escrito. Um `.pedacos` com uma celula apontando pra
	/// fora do tileset nao da erro no jogo: o Godot so nao desenha a celula, e o buraco apareceria
	/// meses depois sem dizer de onde veio.
	/// ======================================================================================
	/// </summary>
	/// <param name="pastaDoDisco">O `Assets/Maps` vivo: de onde sai o `tileset.tres` (e, ao lado, o `Data/tiles.json`).</param>
	/// <param name="rascunho">Onde os arquivos do andar sao escritos. Nao pode ser a pasta viva.</param>
	/// <returns>0 se o andar saiu; 1 se faltou alguma coisa e nada foi gravado.</returns>
	public static int RepintarAndar(string dmmDir, string spritesDir, string pastaDoDisco, string rascunho,
									Dictionary<string, TurfDef> turfs, int z)
	{
		string? raiz = AcharRaiz(pastaDoDisco);
		if (raiz == null)
		{
			Console.WriteLine($"ERRO: nao achei o project.godot subindo de {pastaDoDisco} -- sem ele nao ha `res://` pra casar");
			return 1;
		}
		if (string.Equals(Path.GetFullPath(rascunho).TrimEnd('\\', '/'), Path.GetFullPath(pastaDoDisco).TrimEnd('\\', '/'),
						  StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("ERRO: o rascunho e a propria pasta viva. Este comando reescreve o `.col` e o `.vis` do andar "
							  + "por inteiro; quem aplica o rascunho e quem o revisou.");
			return 1;
		}
		return Passada(dmmDir, spritesDir, rascunho, turfs, soFisica: false,
					   new Trava
					   {
						   Z = z, Raiz = raiz, PastaDoDisco = pastaDoDisco,
						   Sprites = Path.GetFullPath(spritesDir), Icones = new IconesDoDm(dmmDir),
					   });
	}

	/// <summary>
	/// REPINTA TODOS OS ANDARES COM AS FONTES PRESAS E ACRESCENTA AS QUE FALTAM -- o comando `acrescentar`.
	///
	/// E o <see cref="RepintarAndar"/> com uma licenca a mais: quando um tipo pede uma fonte que o
	/// tileset do disco nao tem (a folha do jogo no lugar da homonima, a arte que nunca foi copiada, a
	/// variante com a ancora do BYOND -- ver <see cref="FonteDoTipo"/>), ela NASCE, com um `id` depois
	/// do ultimo do disco. Por isso sao os 40 andares numa passada so: o `id` de uma fonte nova tem que
	/// ser o mesmo em todo `.pedacos` que a usa.
	///
	/// TUDO SAI NO RASCUNHO, espelhando o repo: `Assets/Maps` (o `tileset.tres` com os acrescimos e os
	/// arquivos de cada andar), `Assets/Data/tiles.json` e, pra arte que nao estava em `Assets/Sprites`,
	/// o PNG no caminho que ele tera. Nada do que o disco tem e reescrito -- ver
	/// <see cref="EscreverAcrescimos"/>.
	/// </summary>
	/// <returns>0 se saiu; 1 se faltou alguma coisa e nada dos andares foi gravado.</returns>
	public static int AcrescentarAndares(string dmmDir, string spritesDir, string pastaDoDisco, string rascunho,
										 Dictionary<string, TurfDef> turfs)
	{
		string? raiz = AcharRaiz(pastaDoDisco);
		if (raiz == null)
		{
			Console.WriteLine($"ERRO: nao achei o project.godot subindo de {pastaDoDisco} -- sem ele nao ha `res://` pra casar");
			return 1;
		}
		string pastaDeMapas = Path.Combine(Path.GetFullPath(rascunho), "Assets", "Maps");
		if (string.Equals(pastaDeMapas.TrimEnd('\\', '/'), Path.GetFullPath(pastaDoDisco).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("ERRO: o rascunho e a propria raiz do projeto. Este comando reescreve os planos de todos os "
							  + "andares por inteiro; quem aplica o rascunho e quem o revisou.");
			return 1;
		}
		return Passada(dmmDir, spritesDir, pastaDeMapas, turfs, soFisica: false,
					   new Trava
					   {
						   Z = Trava.Todos, Raiz = raiz, PastaDoDisco = pastaDoDisco, Novas = [],
						   Rascunho = Path.GetFullPath(rascunho), Sprites = Path.GetFullPath(spritesDir),
						   Icones = new IconesDoDm(dmmDir),
					   });
	}

	/// <summary>
	/// O TILESET DO DISCO, visto por quem repinta um andar sem poder mexer nele (ver
	/// <see cref="RepintarAndar"/>): o que ele declara, o que mais existe em `Assets/Sprites` com o
	/// mesmo nome, e a lista do que o andar pediu e ele nao tem.
	/// </summary>
	private sealed class Trava
	{
		public required int Z;
		public required string Raiz;
		public required string PastaDoDisco;

		/// <summary>
		/// O `tiles.json` que acompanha ESTE tileset: o da pasta `Data` ao lado da de mapas. No repo e o
		/// `Assets/Data/tiles.json`; apontando o comando pra um rascunho ja com acrescimos, e o do rascunho.
		/// </summary>
		public string Indice => Path.GetFullPath(Path.Combine(PastaDoDisco, "..", "Data", "tiles.json"));

		/// <summary>
		/// Onde uma textura `res://` esta no disco: no repo, ou -- a arte que ainda nao foi aplicada -- na
		/// arvore de rascunho de onde o tileset veio.
		/// </summary>
		public string ArquivoDe(string res)
		{
			string rel = res["res://".Length..].Replace('/', Path.DirectorySeparatorChar);
			string noRepo = Path.GetFullPath(Path.Combine(Raiz, rel));
			string aoLado = Path.GetFullPath(Path.Combine(PastaDoDisco, "..", "..", rel));
			return File.Exists(noRepo) || !File.Exists(aoLado) ? noRepo : aoLado;
		}

		/// <summary>O `Z` de quem pede TODOS os andares (o comando `acrescentar`); o `repintar` pede um.</summary>
		public const int Todos = 0;

		public bool Pede(int z) => Z == Todos || Z == z;

		/// <summary>
		/// AS FONTES QUE ESTA PASSADA PODE ACRESCENTAR ao tileset do disco, na ordem em que nasceram. Nula
		/// = nenhuma: o andar que pedir uma fonte que o disco nao tem falha alto (o `repintar`).
		/// </summary>
		public List<Fonte>? Novas;

		/// <summary>O id da proxima fonte acrescentada: o maior do disco + 1, e dali pra frente.</summary>
		public int ProximoId;

		/// <summary>
		/// A raiz do rascunho, que espelha a do repo (`Assets/Maps`, `Assets/Data`, `Assets/Sprites`): pra
		/// onde vao o tileset e o indice com os acrescimos, e o PNG de arte que nunca tinha sido copiada.
		/// </summary>
		public string Rascunho = "";

		/// <summary>A pasta `Assets/Sprites` viva.</summary>
		public string Sprites = "";

		/// <summary>Os arquivos de icone do original, pela ordem dos FILE_DIR. Nulo = nao perguntar ao DM qual folha e.</summary>
		public IconesDoDm? Icones;

		/// <summary>PNG copiado pro rascunho nesta passada (caminho `res://`): arte que o Godot ainda tem que importar.</summary>
		public List<string> Copiados = [];

		/// <summary>`sources/N` -> tile declarado -> quantos quadros ele anima (1 = parado).</summary>
		public Dictionary<int, Dictionary<(int X, int Y), int>> Declarados = [];

		/// <summary>Nome de atlas -> todo `.png` de `Assets/Sprites` com esse nome, preso ou nao.</summary>
		public Dictionary<string, List<string>> NoDisco = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Folha do tileset que nao pode ser usada (sumiu, mudou de grade...) -> o motivo.</summary>
		public Dictionary<string, string> Suspeitas = new(StringComparer.OrdinalIgnoreCase);

		private readonly List<string> _faltas = [];
		private readonly HashSet<string> _vistas = new(StringComparer.Ordinal);

		/// <summary>O que o andar pediu e o tileset do disco nao tem. Vazia = pode gravar.</summary>
		public IReadOnlyList<string> Faltas => _faltas;

		public void Faltar(string oque)
		{
			if (_vistas.Add(oque)) _faltas.Add(oque);
		}

		/// <summary>Quantos quadros o tileset do disco anima neste tile. 0 = o tile nao esta declarado.</summary>
		public int QuadrosDeclarados(int fonte, (int X, int Y) c) =>
			Declarados.TryGetValue(fonte, out Dictionary<(int X, int Y), int>? tiles) ? tiles.GetValueOrDefault(c) : 0;
	}

	private static int Passada(string dmmDir, string spritesDir, string outDir, Dictionary<string, TurfDef> turfs,
							   bool soFisica, Trava? trava)
	{
		Directory.CreateDirectory(outDir);
		if (soFisica) Console.WriteLine("SO FISICA: tileset, tiles.json, cenas, pedacos e luzes ficam como estao; "
										 + "o .col e o .vis so PERDEM bits");

		// O DM ERROU O NOME DE UM ESTADO, e o mapa usa o certo -- ver `EstadoDoMapa`.
		foreach (TurfDef td in turfs.Values) td.IconState = EstadoDoMapa(td.Path, td.IconState);
		var fichas = new Fichas(turfs);

		// A RAIZ DO PROJETO GODOT, achada pelo `project.godot` -- NAO o diretorio de trabalho.
		//
		// Os caminhos `res://` do tileset saiam do cwd, e o resultado era que rodar o pipeline
		// de DENTRO de Tools/AssetPipeline escrevia `res://../../Assets/Sprites/...`: nem o
		// jogo nem o editor acham a textura, o TileSet carrega com ZERO tiles e o editor cospe
		// "TileSetAtlasSource has no tile at (0,0)" pra cada celula do mapa. De qual pasta o
		// comando foi chamado nao pode decidir o conteudo do arquivo gerado.
		string? raizAchada = trava?.Raiz ?? AcharRaiz(outDir);
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
		//
		// PRESO, O INDICE NAO E VARRIDO: cada nome aponta pra folha que o tileset do disco ja usa, e so
		// pra ela. O que mais houver em `Assets/Sprites` com o mesmo nome fica anotado a parte
		// (`Trava.NoDisco`), pra o `Garantir` acusar o andar que precisaria dele.
		var atlasPorNome = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var fontes = new Dictionary<string, Fonte>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<string>> varridos = trava?.NoDisco ?? atlasPorNome;
		foreach (string png in Directory.GetFiles(spritesDir, "*.png", SearchOption.AllDirectories))
		{
			string chave = Path.GetFileNameWithoutExtension(png);
			if (!varridos.TryGetValue(chave, out List<string>? l)) varridos[chave] = l = [];
			l.Add(png);
		}
		if (trava != null)
		{
			if (!Semear(trava, fontes, atlasPorNome)) return 1;
		}
		else
		{
			int repetidos = atlasPorNome.Count(kv => kv.Value.Count > 1);
			if (repetidos > 0)
				Console.WriteLine($"nomes de atlas repetidos: {repetidos} (resolvidos pelo icon_state)");
		}

		var semAtlas = new HashSet<string>();
		int proxId = 0;

		// ---- passada 1: descobrir quais tiles cada mapa usa ----
		// CADA .dmm NUMERA O PROPRIO z A PARTIR DE 1 -- o deslocamento vem do `.dme`. Ver `LerMapas`.
		List<(string Arquivo, DmmMap.Result Dados, int Offset)> mapas = LerMapas(dmmDir);

		// OS BERCOS ANTES DA PASSADA 2: o marco de cada andar vai na linha dele do manifesto.
		Dictionary<int, MarcoDoDmm> bercos = ExtrairBercos(mapas);

		// ============================ A CIDADE DE VEGETA NAO ESTA NO `.dmm` ============================
		// Ela e o unico cenario do jogo que o original ERGUE POR CODIGO no boot (`VegetaCity.dm`), e
		// por isso nunca existiu no port: quem le so o mapa nao ve o que o mapa nao guarda. Ela entra
		// AQUI, antes da passada 1, porque as pecas dela precisam registrar atlas como qualquer outra
		// celula -- carimbar depois deixaria as paredes sem fonte no tileset.
		//
		// O FREIO E O `temArte`: peca sem desenho nao e carimbada. Ver `CidadeDeVegeta.Erguer`.
		bool TemArte(string bp)
		{
			if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null) return false;
			Fonte? f = Garantir(td.Icon, td.IconState, raiz, fontes, atlasPorNome, semAtlas, ref proxId, trava);
			return f != null && f.Achar(td.IconState ?? "") != null;
		}
		// A cidade ficou de pe? O plano do que nasce sob teto (`.dentro`) so marca as celulas dela
		// se sim -- ver `CelulasInternas`.
		bool cidadeDeVegetaDePe = false;
		foreach ((string _, DmmMap.Result d3, int off3) in mapas)
			foreach (DmmLevel n3 in d3.Levels)
			{
				if (n3.Z + off3 != CidadeDeVegeta.Z) continue;
				CidadeDeVegeta.Relatorio r = CidadeDeVegeta.Erguer(d3, n3, TemArte);
				cidadeDeVegetaDePe = r.Celulas > 0;
				Console.WriteLine($"cidade de Vegeta: {r.Celulas} celulas carimbadas "
								  + $"({r.Paredes} paredes, {r.Portas} portas, {r.Moveis} moveis, "
								  + $"{r.Maquinas} maquinas)");
				// O QUE NAO ENTROU SE ANUNCIA. Peca sem arte que virasse celula densa seria uma
				// parede invisivel -- exatamente o defeito que este pipeline existe pra nao produzir.
				foreach (string bp in r.SemArte)
					Console.WriteLine($"   SEM ARTE, NAO CARIMBADA: {bp} "
									  + $"(icon_state '{(turfs.GetValueOrDefault(bp)?.IconState ?? "")}' "
									  + $"nao existe em '{turfs.GetValueOrDefault(bp)?.Icon ?? "?"}')");
			}

		// TURF E OBJ, os dois. So o turf era registrado, e essa era a causa de duas queixas que
		// pareciam separadas: "falta coisa no mapa" e "tem parede invisivel". Sao a MESMA coisa
		// -- 41% dos prefabs da Terra tem um /obj (arvore, minerio, cerca, cadeira), o desenho
		// ignorava todos e a colisao NAO: uma AppleTree densa virava um muro que ninguem via.
		//
		// PRESO, SO O ANDAR PEDIDO REGISTRA: a pergunta e "o tileset do disco tem o que ESTE andar
		// usa?", e ela nao pode reprovar por uma folha que so outro planeta pede.
		foreach ((string _, DmmMap.Result dados, int off1) in mapas)
			foreach (string[] tipos in trava == null ? dados.Keys.Values : ComposicoesDoAndar(dados, off1, trava.Z))
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!Desenhavel(bp)) continue;
					if (fichas.De(tp) is not { Icon: not null } td) continue;

					// PRESO, o nome da folha nao basta pra saber a fonte -- ver `FonteDoTipo`. A ficha que ja
					// foi resolvida (o mesmo tipo noutra composicao) nao pergunta de novo.
					Fonte? f = td.Atlas != null && fontes.TryGetValue(td.Atlas, out Fonte? resolvida) ? resolvida
						: trava == null ? Garantir(td.Icon, td.IconState, raiz, fontes, atlasPorNome, semAtlas, ref proxId)
						: FonteDoTipo(td, trava, fontes, atlasPorNome, semAtlas, ref proxId);
					if (f == null) continue;
					td.Atlas = f.Entrada;   // guarda QUAL fonte venceu: o resto do pipeline usa esta

					(int X, int Y) coord = Coord(f, td.IconState, td.Dir);
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
		//
		// PRESO, NADA DISTO RODA: a fisica do tile e as tiras de animacao sao assunto do TILESET, e o
		// tileset e o do disco (as tiras dele ja foram planejadas no `Semear`).
		if (trava == null)
		{
			int marcados = MarcarSolidos(turfs, fontes, atlasPorNome);
			int opacos = fontes.Values.Sum(f => f.Opacas.Count);
			Console.WriteLine($"tiles com fisica : {marcados} | com oclusao: {opacos}");
		}

		// ============================ AS ANIMACOES QUE NAO CABIAM ============================
		// Tem que vir ANTES do tileset E antes das cenas: o reempacote cria FONTES NOVAS e muda a
		// coordenada das celulas que usam esses estados. Fazer depois deixaria as cenas apontando
		// pro quadro parado enquanto o tileset ja anunciava o animado.
		if (!soFisica && trava == null)
		{
			int reempacotadas = Reempacotar(fontes, raiz, ref proxId);
			if (reempacotadas > 0)
				Console.WriteLine($"animacoes reempacotadas: {reempacotadas} (nao cabiam numa linha do atlas)");

			EscreverTileSet(Path.Combine(outDir, "tileset.tres"), fontes);

			// O INDICE DE TILES SAI JUNTO DO TILESET, e tem que ser aqui: os `source id` nascem de
			// um contador por ordem de descoberta, entao um `tiles.json` velho ao lado de um
			// `tileset.tres` novo nao da erro nenhum -- da o SPRITE ERRADO. Gerar os dois na mesma
			// passada e o que impede os dois de envelhecerem em ritmos diferentes.
			//
			// Quem consome: o planeta PROCEDURAL. O gerador do Core devolve `TileVisual(atlas,
			// estado)` -- nomes -- e sem esta tabela nada no jogo sabe transformar isso em celula.
			TileIndex.Resultado idx = TileIndex.Escrever(
				Path.Combine(raiz, "Assets", "Data", "tiles.json"),
				fontes.Values.Select(f => new FonteDeAtlas(
					f.Id, f.Chave, f.ResPath, f.IconW, f.IconH, f.Cols,
					f.SemCaixa.ToDictionary(kv => kv.Key, kv => kv.Value.Indice, StringComparer.OrdinalIgnoreCase))));
			Console.WriteLine($"indice de tiles  : {idx.Atlas} atlas, {idx.Estados} estados"
							  + (idx.Colisoes.Count > 0 ? $" | {idx.Colisoes.Count} nome(s) em disputa" : ""));
			foreach (string c in idx.Colisoes) Console.WriteLine("   " + c);
		}

		// ============================ O MAPA DOS DESTINOS, ANTES DE CONVERTER ============================
		// Uma passagem da Terra aponta pro z 23 (a caverna), que so vai ser convertido daqui a vinte
		// andares. Resolver o destino na hora exigiria que a ordem de conversao casasse com a ordem
		// dos destinos -- o que nao acontece, e nem daria pra garantir com passagens de ida e volta.
		//
		// A CONTA DO Y E A INVERSAO DO BYOND: la o eixo cresce pra CIMA e aqui pra baixo, entao a
		// linha `by` vira `altura - by`. Errar isto poria cada chegada espelhada na vertical -- e,
		// pior, sem sintoma nenhum a nao ser "a caverna me cospe no lugar errado".
		var porZ = new Dictionary<int, (string Nome, int W, int H)>();
		foreach ((string _, DmmMap.Result d2, int off2) in mapas)
			foreach (DmmLevel n2 in d2.Levels)
				porZ[n2.Z + off2] = (NomeDoAndar(d2, n2, off2)[(NomeDoAndar(d2, n2, off2).IndexOf('_') + 1)..],
									 n2.Width, n2.Height);

		Destinos = (bx, by, bz) =>
		{
			if (!porZ.TryGetValue(bz, out (string Nome, int W, int H) alvo)) return null;
			int cx = Math.Clamp(bx - 1, 0, alvo.W - 1);
			int cy = Math.Clamp(alvo.H - by, 0, alvo.H - 1);
			const int t = Jandirus.Core.World.ZoneCollision.TileSize;
			return (alvo.Nome, cx * t + t / 2f, cy * t + t / 2f);
		};

		// PRESO: folha que faltou ja e motivo pra parar, antes de pintar uma celula.
		if (trava is { Faltas.Count: > 0 }) return Recusar(trava, "ao registrar as folhas do andar");

		// AS FONTES QUE FALTAVAM, no rascunho: o tileset e o indice do disco com elas a mais. Antes das
		// cenas, porque uma celula apontando pra um `id` que nenhum tileset declara e o buraco calado
		// que o modo preso existe pra nao produzir.
		if (trava is { Novas: { } novas })
		{
			if (novas.Count > 0) EscreverAcrescimos(trava, trava.Indice);
			Console.WriteLine($"FONTES ACRESCENTADAS: {novas.Count}"
							  + (novas.Count > 0 ? $" (ids {novas[0].Id}..{novas[^1].Id}) -- tileset.tres e tiles.json do rascunho" : " -- o tileset do disco ja tem tudo"));
			foreach (Fonte f in novas)
				Console.WriteLine($"   {f.Id,4}  {f.Nome,-44} {f.ResPath["res://Assets/Sprites/".Length..]}"
								  + (f.Origem is { } o ? $"   ancora ({o.X},{o.Y})" : ""));
			foreach (string res in trava.Copiados)
				Console.WriteLine($"   PNG NOVO (falta importar no Godot): {res}");
		}

		// ---- passada 2: uma cena por andar + o mapa de colisao que o SERVIDOR le ----
		int cenas = 0, celulas = 0, bloqueadas = 0, totalPortas = 0, totalMaquinas = 0, totalPassagens = 0;
		int totalAgua = 0, totalDuro = 0, totalNuvem = 0, totalDentro = 0;
		int libertadas = 0, cegasLibertadas = 0, novasNaoGravadas = 0;
		var manifesto = new List<string>();
		foreach ((string arquivo, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				if (trava != null && !trava.Pede(nivel.Z + off)) continue;

				string nome = NomeDoAndar(dados, nivel, off);
				string cena = Path.Combine(outDir, nome + ".tscn");

				// A CENA MANDA. O que ela DESENHOU e o que bloqueia -- a colisao sai da mesma
				// passada, nao de uma segunda leitura do .dmm com suas proprias regras. Duas
				// funcoes calculando "o que e parede" por caminhos diferentes divergiam em ~2%
				// das celulas, e divergencia entre o que se ve e o que se atravessa e
				// exatamente a queixa que estamos consertando.
				celulas += EscreverCena(cena, nome, nivel, dados, fichas, fontes, atlasPorNome,
										semAtlas, ref proxId, out HashSet<(int, int)> paredes,
										out HashSet<(int, int)> cegos,
										out List<string> portas,
										out List<string> maquinas,
										out List<string> passagens, gravar: !soFisica, trava);

				// PRESO: a cena nao gravou nada se uma celula apontou pra fora do tileset do disco, e os
				// planos e as listas do andar tambem nao saem -- um `.col` novo ao lado de um `.pedacos`
				// velho e o par "solido e invisivel" de novo.
				if (trava is { Faltas.Count: > 0 }) return Recusar(trava, $"ao pintar {nome}");

				if (soFisica)
				{
					(int lc, int nc) = LibertarNaColisao(Path.Combine(outDir, nome + ".col"), nivel.Width, nivel.Height, paredes);
					(int lv, int nv) = LibertarNaColisao(Path.Combine(outDir, nome + ".vis"), nivel.Width, nivel.Height, cegos);
					AcompanharListas(outDir, nome, portas, passagens, out int portasTiradas, out bool passagensMudaram);
					libertadas += lc; cegasLibertadas += lv; novasNaoGravadas += nc + nv;
					if (lc + lv + nc + nv + portasTiradas > 0 || passagensMudaram)
						Console.WriteLine($"  {nome}: .col -{lc} | .vis -{lv}"
										  + (nc + nv > 0 ? $" | bits NOVOS nao gravados: .col {nc}, .vis {nv}" : "")
										  + (portasTiradas > 0 ? $" | .portas -{portasTiradas}" : "")
										  + (passagensMudaram ? $" | .passagens reescrito ({passagens.Count})" : ""));
					cenas++;
					continue;
				}
				bloqueadas += EscreverColisao(Path.Combine(outDir, nome + ".col"),
											  nivel.Width, nivel.Height, paredes);
				// mesmo formato, outro proposito: este e o que o CAMPO DE VISAO consulta
				EscreverColisao(Path.Combine(outDir, nome + ".vis"), nivel.Width, nivel.Height, cegos);

				// ...e este e a TERCEIRA CLASSE DE CELULA: agua. Mesmo formato de novo, e um
				// arquivo separado pelo mesmo motivo que o `.vis` e separado do `.col` -- as tres
				// perguntas ("para o corpo", "esconde", "e agua") divergem entre si em quase toda
				// celula que importa. Zona seca nao ganha arquivo. Ver `ConverterAguas`.
				List<(int X, int Y)> molhadas = CelulasDeAgua(nivel, dados, turfs);
				string arqAgua = Path.Combine(outDir, nome + ".agua");
				if (molhadas.Count > 0)
				{
					EscreverColisao(arqAgua, nivel.Width, nivel.Height, molhadas);
					totalAgua += molhadas.Count;
				}
				else if (File.Exists(arqAgua)) File.Delete(arqAgua);

				// ...e esta e a QUARTA CLASSE: a nuvem. Mesmo formato de novo, e arquivo separado pelo
				// mesmo motivo dos outros -- "para o corpo", "esconde", "e agua", "cede a um soco" e
				// "e ceu" sao cinco perguntas que divergem entre si em quase toda celula que importa.
				// Zona sem ceu nao ganha arquivo. Ver `ConverterNuvens` e `Core/World/Ceu.cs`.
				List<(int X, int Y)> nuvens = CelulasDeNuvem(nivel, dados, turfs);
				string arqNuvem = Path.Combine(outDir, nome + ".nuvem");
				if (nuvens.Count > 0)
				{
					EscreverColisao(arqNuvem, nivel.Width, nivel.Height, nuvens);
					totalNuvem += nuvens.Count;
				}
				else if (File.Exists(arqNuvem)) File.Delete(arqNuvem);

				// ...e este e O QUE NAO SE QUEBRA: o `destroyable = 0` do original. Arquivo separado
				// pelo mesmo motivo dos outros dois -- "para o corpo", "esconde", "e agua" e "cede a
				// um soco" sao quatro perguntas que divergem entre si em quase toda celula que
				// importa. Ver `ConverterDuros`, que e o caminho que roda sozinho.
				List<(int X, int Y)> duras = CelulasDuras(nivel, dados, turfs);
				string arqDuro = Path.Combine(outDir, nome + ".duro");
				if (duras.Count > 0)
				{
					EscreverColisao(arqDuro, nivel.Width, nivel.Height, duras);
					totalDuro += duras.Count;
				}
				else if (File.Exists(arqDuro)) File.Delete(arqDuro);

				// ...e este e O QUE NASCE SOB TETO: a area `Inside` do original, onde nao cai clima nem
				// noite. Quinto plano no mesmo formato, e arquivo separado pelo motivo dos outros quatro.
				// Ver `ConverterInteriores`, que e o caminho que roda sozinho, e `Core/World/CelulaInterna.cs`.
				List<(int X, int Y)> internas = CelulasInternas(
					nivel, dados, cidadeDeVegetaDePe && nivel.Z + off == CidadeDeVegeta.Z);
				string arqDentro = Path.Combine(outDir, nome + ".dentro");
				if (internas.Count > 0)
				{
					EscreverColisao(arqDentro, nivel.Width, nivel.Height, internas);
					totalDentro += internas.Count;
				}
				else if (File.Exists(arqDentro)) File.Delete(arqDentro);

				// AS PORTAS DA ZONA. Sai sempre, mesmo vazio: um arquivo que as vezes existe e as
				// vezes nao vira um `if` no leitor, e um `if` a menos vale o punhado de bytes.
				File.WriteAllText(Path.Combine(outDir, nome + ".portas"),
					"[" + string.Join(",\n ", portas) + "]",
					new System.Text.UTF8Encoding(false));
				totalPortas += portas.Count;

				// AS MAQUINAS DO MAPA, pelo mesmo caminho e pelo mesmo motivo das portas.
				File.WriteAllText(Path.Combine(outDir, nome + ".objetos"),
					"[" + string.Join(",\n ", maquinas) + "]",
					new System.Text.UTF8Encoding(false));
				totalMaquinas += maquinas.Count;

				// AS PASSAGENS, pelo mesmo caminho e pelo mesmo motivo das portas.
				File.WriteAllText(Path.Combine(outDir, nome + ".passagens"),
					"[" + string.Join(",\n ", passagens) + "]",
					new System.Text.UTF8Encoding(false));
				totalPassagens += passagens.Count;

				cenas++;

				string zona = nome[(nome.IndexOf('_') + 1)..];
				manifesto.Add($"  {{ \"zona\": \"{zona}\", \"z\": {nivel.Z + off}, \"cena\": \"res://Assets/Maps/{nome}.tscn\", " +
							  $"\"pedacos\": \"res://Assets/Maps/{nome}.pedacos\", " +
							  $"\"colisao\": \"res://Assets/Maps/{nome}.col\", \"visao\": \"res://Assets/Maps/{nome}.vis\", " +
							  $"\"agua\": \"res://Assets/Maps/{nome}.agua\", " +
						  $"\"nuvem\": \"res://Assets/Maps/{nome}.nuvem\", " +
						  $"\"duro\": \"res://Assets/Maps/{nome}.duro\", " +
						  $"\"dentro\": \"res://Assets/Maps/{nome}.dentro\", " +
							  $"\"luzes\": \"res://Assets/Maps/{nome}.luz\", " +
							$"\"portas\": \"res://Assets/Maps/{nome}.portas\", " +
							$"\"objetos\": \"res://Assets/Maps/{nome}.objetos\", " +
							$"\"passagens\": \"res://Assets/Maps/{nome}.passagens\", " +
							  $"\"w\": {nivel.Width}, \"h\": {nivel.Height}"
							  + CamposDeBerco(bercos.TryGetValue(nivel.Z + off, out MarcoDoDmm marco) ? marco : null)
							  + " }");
			}

		if (soFisica)
		{
			Console.WriteLine($"mapas lidos    : {mapas.Count}");
			Console.WriteLine($"andares        : {cenas}");
			Console.WriteLine($"col libertadas : {libertadas} celula(s) que bloqueavam sem ninguem que as desenhasse");
			Console.WriteLine($"vis libertadas : {cegasLibertadas} celula(s) que cegavam por baixo de outro turf");
			Console.WriteLine($"novas ignoradas: {novasNaoGravadas} (bits que a passada quis e o disco nao tem -- so relato)");
			return 0;
		}

		if (trava != null)
		{
			if (cenas == 0)
			{
				Console.WriteLine($"ERRO: nenhum dos mapas tem o andar z{trava.Z:00} -- nada foi gravado");
				return 1;
			}
			int acrescentadas = trava.Novas?.Count ?? 0;
			Console.WriteLine((trava.Z == Trava.Todos ? $"andares repintados: {cenas}" : $"andar repintado: z{trava.Z:00}")
							  + $", {celulas} celulas, com as {fontes.Count - acrescentadas} fontes do tileset do disco"
							  + (trava.Novas != null ? $" e {acrescentadas} acrescentada(s)" : ""));
			Console.WriteLine($"   .col {bloqueadas} | agua {totalAgua} | ceu {totalNuvem} | duro {totalDuro} | sob teto {totalDentro}"
							  + $" | portas {totalPortas} | maquinas {totalMaquinas} | passagens {totalPassagens}");
			Console.WriteLine($"   escrito em {outDir} -- "
							  + (acrescentadas > 0 ? "o tileset e o tiles.json com os acrescimos estao no MESMO rascunho; " : "tileset, tiles.json, ")
							  + "manifesto e o que ha em Assets NAO foram tocados");
			return 0;
		}

		File.WriteAllText(Path.Combine(outDir, "manifest.json"),
			"[\n" + string.Join(",\n", manifesto) + "\n]\n", new UTF8Encoding(false));

		Conferir(outDir, fontes.Count);

		Console.WriteLine($"mapas lidos    : {mapas.Count}");
		Console.WriteLine($"cenas geradas  : {cenas}");
		Console.WriteLine($"portas         : {totalPortas}");
		Console.WriteLine($"maquinas       : {totalMaquinas} (saem do tilemap e viram construcao)");
		Console.WriteLine($"passagens      : {totalPassagens} (celulas que levam a outro mapa)");
		Console.WriteLine($"agua           : {totalAgua} celulas (terceira classe: para a pe, nao para nadando/voando)");
		Console.WriteLine($"ceu            : {totalNuvem} celulas (quarta classe: SO quem voa passa; z6/z12 derrubam)");
		Console.WriteLine($"duro           : {totalDuro} celulas (destroyable=0: bloqueia e NAO cede a soco nenhum)");
		Console.WriteLine($"sob teto       : {totalDentro} celulas (area Inside: sem clima, sem noite, sem lua)");
		Console.WriteLine($"celulas        : {celulas}");
		Console.WriteLine($"fontes no tileset: {fontes.Count}");
		if (semAtlas.Count > 0)
			Console.WriteLine($"SEM atlas ({semAtlas.Count}): {string.Join(", ", semAtlas.Take(8))}");
		return 0;
	}

	/// <summary>A lista do que faltou, no console. Devolve o codigo de saida do comando.</summary>
	private static int Recusar(Trava trava, string quando)
	{
		Console.WriteLine($"\nNADA FOI GRAVADO: {quando}, {trava.Faltas.Count} coisa(s) que o andar pede e o tileset do disco nao tem:");
		foreach (string f in trava.Faltas) Console.WriteLine("   - " + f);
		Console.WriteLine("   (o conserto e por o que falta no tileset -- o comando `acrescentar`, que so acrescenta fontes; a "
						  + "conversao cheia, `maps`, renumera as que existem -- e repintar de novo)");
		return 1;
	}

	/// <summary>As composicoes de typepath que as celulas de UM andar usam, cada uma uma vez.</summary>
	private static IEnumerable<string[]> ComposicoesDoAndar(DmmMap.Result dados, int offset, int z)
	{
		var vistas = new HashSet<string>(StringComparer.Ordinal);
		foreach (DmmLevel nivel in dados.Levels)
		{
			if (z != Trava.Todos && nivel.Z + offset != z) continue;
			for (int y = 0; y < nivel.Height; y++)
				for (int x = 0; x < nivel.Width; x++)
					if (nivel.Cells[x, y] is { } k && vistas.Add(k) && dados.Keys.TryGetValue(k, out string[]? tipos))
						yield return tipos;
		}
	}

	/// <summary>
	/// A FICHA DE CADA COISA QUE O MAPA POE NUMA CELULA: a do tipo, ou uma copia dela quando a
	/// INSTANCIA troca a aparencia.
	///
	/// O `.dmm` deixa cada instancia sobrescrever variaveis (`Teleporter{icon = 'Icons/Turfs/Turf
	/// 57.dmi'; icon_state = "13"}`), e o conversor lia so o tipo: as duas bocas de caverna de Vegeta
	/// sairam com o brilho de teleporte no lugar do buraco na rocha. Aqui a instancia que troca icone,
	/// estado ou direcao ganha ficha propria, uma por texto de instancia -- e e a MESMA nas duas
	/// passadas, porque e nela que a primeira anota qual atlas venceu.
	/// </summary>
	private sealed class Fichas(Dictionary<string, TurfDef> doTipo)
	{
		private readonly Dictionary<string, TurfDef?> _porInstancia = new(StringComparer.Ordinal);

		public TurfDef? De(string tp)
		{
			if (tp.IndexOf('{') < 0) return doTipo.GetValueOrDefault(tp.Trim());
			if (_porInstancia.TryGetValue(tp, out TurfDef? pronta)) return pronta;

			TurfDef? ficha = doTipo.GetValueOrDefault(DmmMap.BasePath(tp));
			if (ficha != null)
			{
				IReadOnlyDictionary<string, string> vars = DmmMap.Variaveis(tp);
				if (DmTurfScanner.MudaAparencia(vars)) ficha = ficha.ComVariaveisDeInstancia(vars);
			}
			return _porInstancia[tp] = ficha;
		}
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

			// PASSAGEM DENSA NAO E PAREDE, e a diferenca era fatal. `toeg` (a subida da torre do
			// Karin), `tohbtc` e `fromhbtc` tem `density = 1` no DM -- mas la o `Enter()` dispara
			// na TENTATIVA de entrar, e o teleporte acontece antes de o bloqueio valer.
			//
			// Aqui a densidade virava um bit de parede no `.col`, e o servidor recusava o passo:
			// o corpo nunca chegava a pisar na celula, e o gatilho que a leva pro Templo Sagrado
			// nunca rodava. Foi o que o dono viu -- "a porta pra subir na torre do karin n ta
			// funcionando". Era uma parede com desenho de escada. Ver `Passagens.Eh`.
			if (Passagens.Eh(td.Path)) continue;

			// O typepath pode nunca ter aparecido num .dmm (e ai nao tem `Atlas`), mas o atlas
			// DELE pode ja estar no tileset por causa de outro typepath. Resolve sem REGISTRAR
			// nada novo: o objetivo e marcar parede no que ja existe, nao inchar o arquivo com
			// folhas que ninguem pediu.
			Fonte? f = td.Atlas != null && fontes.TryGetValue(td.Atlas, out Fonte? direta)
				? direta
				: ResolverExistente(td.Icon, td.IconState, atlasPorNome, fontes);
			if (f == null) continue;

			// acha o estado pelo nome pra saber quantos quadros e direcoes ele ocupa
			if (f.Achar(td.IconState ?? "") is not { } st) continue;

			for (int i = 0; i < st.Dirs * st.Quadros; i++)
			{
				(int X, int Y) c = Indice(f, st.Indice + i);
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
			if (f.Achar(estado ?? "") != null) return f;
		}
		return primeira;
	}

	/// <summary>
	/// PORTA. No BYOND ela e um turf DENSO que abre no `Enter()` -- denso no papel e atravessavel
	/// na pratica, porque encostar nele ja o abre.
	///
	/// ATE AQUI ELA ERA UMA EXCECAO, e uma excecao espalhada por tres lugares: saia da colisao (ou
	/// seja, ficava aberta pra sempre), continuava cegando o campo de visao, e o desenho dela era o
	/// quadro errado. Agora ela SAI DO MAPA COMO ENTIDADE, na lista `.portas`: nasce fechada
	/// (bloqueia e cega, igual ao DM) e quem a abre e o servidor, em runtime.
	///
	/// Este predicado sobrou pra duas coisas: decidir o que vai pra lista, e manter a porta fora do
	/// `MarcarSolidos` -- fisica FIXA do tileset numa coisa que abre voltaria a lacrar a casa.
	/// </summary>
	private static bool EhPorta(string bp) =>
		bp.StartsWith("/turf/Door/", StringComparison.Ordinal)
		// A PORTA DE NAMEK tem o MESMO codigo de abrir (Enter + Open/Close + flick) e mora noutro
		// ramo da arvore -- entao ela nao casava aqui e virava PAREDE. Medido: 27 celulas em z02,
		// col=1 e vis=1. As casas de Namek estavam lacradas.
		|| bp.StartsWith("/turf/NamekBuildings/NamekDoor", StringComparison.Ordinal) ||
		bp.StartsWith("/turf/build/Door/", StringComparison.Ordinal);

	/// <summary>
	/// Typepath que vira TILE na cena. Turf e o chao/parede; obj e tudo que fica em cima dele.
	/// `/mob` fica de fora: NPC nao e cenario, e entidade -- entra pelo servidor, nao pelo mapa.
	///
	/// E O EFEITO QUE SE APAGA SOZINHO TAMBEM FICA DE FORA. `/obj/Tornado` chama `deleteMe()` de 10 a
	/// 20 segundos depois de nascer (`dusts.dm:178-183`: `spawn(rand(100,200))`), entao o que o
	/// mapeador deixou no `.dmm` de Vegeta some antes de alguem chegar la. Assado como tile, ele ficava
	/// girando no mesmo lugar pra sempre.
	/// </summary>
	private static bool Desenhavel(string bp) =>
		(bp.StartsWith("/turf", StringComparison.Ordinal) || bp.StartsWith("/obj", StringComparison.Ordinal))
		&& bp != "/obj/Tornado";

	/// <summary>
	/// A aresta de penhasco (`/obj/barrier/Edges/*`, `barrier.dm:81-211`): um risco rente ao chao, sem
	/// altura -- por isso e ela quem desce pra decoracao quando divide a celula com um objeto que tem
	/// corpo. Ver <see cref="DoisDaCelula"/>.
	/// </summary>
	internal static bool EhAresta(string bp) => bp.StartsWith("/obj/barrier/Edges/", StringComparison.Ordinal);

	/// <summary>
	/// DEFEITO INJETADO (bancada): o PRIMEIRO objeto da lista fica com a camada e ninguem desce pra
	/// decoracao -- o pinheiro que o mapa lista depois de uma aresta volta a sumir. Falso na conversao,
	/// sempre.
	/// </summary>
	public static bool PrimeiroObjetoVenceDeTeste;

	/// <summary>O que a escolha da camada precisa saber de um objeto solto da celula.</summary>
	/// <param name="TemArte">A folha dele esta no tileset: ha o que pintar.</param>
	/// <param name="CabeNumTile">O desenho e de 32x32 -- pode ficar abaixo dos atores sem que se note.</param>
	/// <param name="Aresta">E um risco de penhasco (<see cref="EhAresta"/>).</param>
	/// <param name="Camada">O `layer` do DM (<see cref="TurfDef.Camada"/>).</param>
	internal readonly record struct Solto(bool TemArte, bool CabeNumTile, bool Aresta, double Camada);

	/// <summary>
	/// MAIS DE UM OBJETO NA MESMA CELULA: quem fica na camada de objetos, quem desce pra decoracao, e
	/// quantos sobram sem camada.
	///
	/// ============================ O PRIMEIRO DA LISTA NAO E O DE CIMA ============================
	/// A camada de objetos guarda UM tile por celula, e quem ficava com ela era o primeiro `/obj` que o
	/// `.dmm` lista. Fora de Vegeta isso apagava coisa que o original mostra:
	///   - Icer (z04) lista a aresta ANTES do pinheiro em dez celulas (`Edge5N, LargePineSnow`): ficava
	///     o risco da aresta, e o pinheiro -- denso, `Plants.dm:332-335` -- sumia;
	///   - o Alem (z06) poe a lampada, o micro-ondas e o interfone EM CIMA da mesa (`o38, lamp`,
	///     `buildobjects.dm:36` e `:326`): ficava a mesa vazia. Catorze celulas la, e mais uma no
	///     Inferno, no Paraiso e na caverna da Terra.
	///
	/// A REGRA E A DO BYOND: com o mesmo `layer`, quem foi gerado por ultimo desenha por cima ("the
	/// order the sprites were generated in is the final tie-breaker", `Understanding the renderer` da
	/// referencia), e o mapa cria os objetos na ordem em que a celula os lista. Fica na camada de
	/// objetos o CORPO (o que nao e aresta) de maior `layer` -- no empate, o ultimo da lista.
	///
	/// QUEM DESCE PRA DECORACAO: o corpo logo abaixo dele, se o desenho couber num tile; senao a
	/// primeira aresta. So com arestas (a quina de um penhasco sao DUAS na mesma celula, a do norte e
	/// a do oeste), a primeira da lista desce e a segunda fica por cima: elas se cruzam no canto, e a
	/// ordem aparece.
	///
	/// DIVERGENCIAS DECLARADAS:
	///   - a decoracao fica ABAIXO dos atores e nao ordena por Y com eles. Um desenho de 32x32 nao
	///     passa do proprio tile, entao quem esta na celula vizinha nao o cobre nem e coberto por ele;
	///     um desenho maior (arvore) nao desce -- fica sem camada, e e contado;
	///   - a aresta desce mesmo quando o mapa a lista DEPOIS do corpo (no BYOND ela riscaria por cima
	///     do pe da arvore): quem tem altura e que precisa ordenar por Y com quem passa;
	///   - o MESMO objeto posto duas vezes na celula (o `.dmm` repete arvore, aresta e espuma) desenha
	///     uma vez -- quem chama ja entrega a lista sem repeticao. Num desenho opaco nao muda um pixel;
	///   - `step_x/step_y/pixel_x/pixel_y` da INSTANCIA (o micro-ondas 10 px acima do tampo) nao cabem
	///     num tile: o objeto aparece assentado na celula.
	///
	/// OBJETO SEM ARTE NAO DISPUTA: o gerador de bicho (`icon = null`) nao desenha nada, e nao tira a
	/// vez de quem desenha. Quando NINGUEM da celula tem arte volta o primeiro, so pra conta do
	/// "objeto SEM arte" do relatorio.
	/// =============================================================================================
	/// </summary>
	/// <param name="soltos">Os `/obj` desenhaveis da celula que nao sao maquina, na ordem do `.dmm` e sem repeticao.</param>
	internal static (string? Objeto, string? DeBaixo, int SemCamada) DoisDaCelula(
		IReadOnlyList<string> soltos, Func<string, Solto> ficha)
	{
		if (soltos.Count == 0) return (null, null, 0);
		if (PrimeiroObjetoVenceDeTeste) return (soltos[0], null, soltos.Count - 1);

		var corpos = new List<(string Tp, Solto S)>();
		var arestas = new List<string>();
		foreach (string tp in soltos)
		{
			Solto s = ficha(tp);
			if (!s.TemArte) continue;
			if (s.Aresta) arestas.Add(tp);
			else corpos.Add((tp, s));
		}

		if (corpos.Count == 0)
			return arestas.Count switch
			{
				0 => (soltos[0], null, 0),
				1 => (arestas[0], null, 0),
				_ => (arestas[1], arestas[0], arestas.Count - 2),
			};

		int cima = 0, abaixo = -1;
		for (int i = 1; i < corpos.Count; i++)
			if (corpos[i].S.Camada >= corpos[cima].S.Camada) cima = i;
		for (int i = 0; i < corpos.Count; i++)
			if (i != cima && corpos[i].S.CabeNumTile
				&& (abaixo < 0 || corpos[i].S.Camada >= corpos[abaixo].S.Camada)) abaixo = i;

		string? deBaixo = abaixo >= 0 ? corpos[abaixo].Tp : arestas.Count > 0 ? arestas[0] : null;
		return (corpos[cima].Tp, deBaixo, corpos.Count + arestas.Count - (deBaixo == null ? 1 : 2));
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada): a celula volta a guardar so tres desenhos (chao, decoracao e objetos)
	/// -- o trilho do meio da quina da ponte de Namek volta a sumir. Falso na conversao, sempre.
	/// </summary>
	public static bool SoTresDesenhosDeTeste;

	/// <summary>Um desenho da celula: um turf (o de verdade, ou um underlay) ou um objeto solto com arte.</summary>
	/// <param name="Real">E o turf de verdade da celula -- o ULTIMO da lista do mapa.</param>
	/// <param name="CabeNumTile">O desenho e de 32x32 (ver <see cref="Solto"/>).</param>
	/// <param name="Camada">O `layer` do DM.</param>
	internal readonly record struct Desenho(string Tipo, bool Turf, bool Real, bool CabeNumTile, double Camada);

	/// <summary>
	/// ESTA CELULA NAO CABE EM "CHAO, DECORACAO E OBJETOS"? Duas maneiras de nao caber:
	///   - sao mais de dois desenhos CHATOS (tres turfs empilhados, tres arestas, duas arestas e uma
	///     arvore): a quina da ponte de Namek e `N025, decor/bridgeW, Bridge/Edges/bridgeS`, e so o
	///     primeiro e o ultimo eram desenhados;
	///   - um turf tem `layer` MAIOR que o de um objeto de um tile: no BYOND ele desenha por cima do
	///     objeto (a mesa `/turf/decor/Table4`, layer 4, cobre a faca `o3`, layer 3), e a camada de
	///     objetos fica acima de toda decoracao.
	/// Quem nao cai aqui segue pelo caminho de sempre, sem mudar uma celula.
	/// </summary>
	/// <param name="desenhos">Os turfs da celula (sem repeticao) e depois os objetos soltos com arte, na ordem do mapa.</param>
	internal static bool PrecisaDePilha(IReadOnlyList<Desenho> desenhos)
	{
		int objetos = desenhos.Count(d => !d.Turf);
		if (desenhos.Count - Math.Min(objetos, 1) > 2) return true;
		return desenhos.Any(o => !o.Turf && o.CabeNumTile && desenhos.Any(t => t.Turf && t.Camada > o.Camada));
	}

	/// <summary>
	/// A PILHA DE UMA CELULA, NA ORDEM EM QUE O BYOND A DESENHA: do menor `layer` pro maior e, no
	/// empate, na ordem do mapa (os turfs empilhados viram underlays do ultimo e cada um desenha na
	/// camada DELE -- ver o laco das celulas; objeto tem `layer` 3, um acima do turf comum).
	///
	/// QUEM VAI PRA CAMADA DE OBJETOS (a unica que ordena por Y com os atores): o corpo ALTO mais de
	/// cima -- a arvore, que no original mora num plano acima de tudo (`plane=8`, `Plants.dm:19`) --;
	/// sem corpo alto, o ultimo da pilha, se for um objeto. Um turf no topo (a mesa por cima da faca)
	/// deixa a camada de objetos vazia: os dois ficam chatos, na ordem certa.
	///
	/// OS OUTROS SAO CHATOS, de baixo pra cima: chao, decoracao e, dali em diante, uma camada a mais
	/// por desenho (`Decor2`, `Decor3`...). DIVERGENCIA DECLARADA: todo chato fica ABAIXO dos atores,
	/// inclusive o turf de `layer` 4 ou 5 que no BYOND desenharia por cima de quem passa (o vidro da
	/// estacao, a escada sobre o teleporte).
	/// </summary>
	internal static (List<Desenho> Chatos, Desenho? NoObjetos) PilhaDaCelula(IReadOnlyList<Desenho> desenhos)
	{
		List<Desenho> ordem = [.. desenhos.OrderBy(d => d.Camada)];   // estavel: no empate vale a ordem do mapa
		int alto = ordem.FindLastIndex(d => !d.Turf && !d.CabeNumTile);
		int noObjetos = alto >= 0 ? alto : ordem.Count > 0 && !ordem[^1].Turf ? ordem.Count - 1 : -1;

		Desenho? deCima = noObjetos >= 0 ? ordem[noObjetos] : null;
		if (noObjetos >= 0) ordem.RemoveAt(noObjetos);
		if (SoTresDesenhosDeTeste && ordem.Count > 2) ordem.RemoveRange(1, ordem.Count - 2);
		return (ordem, deCima);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada): a mesa redonda volta a pedir o estado que o DM escreveu (`rtable`),
	/// que a folha nao tem -- e volta a ficar fora da cidade, sem desenho e sem corpo. Falso na
	/// conversao, sempre.
	/// </summary>
	public static bool MesaComEstadoDoDmDeTeste;

	/// <summary>O tipo cujo estado o DM escreveu errado, o que ele escreveu e o que a folha tem.</summary>
	internal const string MesaRedonda = "/obj/buildables/rtable", EstadoErradoDaMesa = "rtable", EstadoDaMesa = "round table";

	/// <summary>
	/// O ESTADO COM QUE O MAPA DESENHA UM TIPO: o do DM, menos onde o DM errou o nome.
	///
	/// ============================ DIVERGENCIA DECLARADA: A MESA REDONDA ============================
	/// `/obj/buildables/rtable` declara `icon_state="rtable"` (`buildobjects.dm:311-315`), e a folha
	/// `!!!  house furniture.dmi` nao tem esse estado -- a mesa redonda dela chama `round table`. E erro
	/// de digitacao do original. No BYOND o estado que nao existe cai no estado de nome vazio da folha
	/// (`icon_state var (atom)` da referencia: "the default null state will be displayed if it
	/// exists"), que nesta folha e um tufo de flores: as 12 mesas da cidade de Vegeta sao flores que
	/// param o corpo.
	///
	/// Aqui ela e desenhada com `round table` e bloqueia (`density=1`, `buildobjects.dm:315`). Sem a
	/// troca nao ha quadro pra pintar, o freio da cidade a recusa (ver `CidadeDeVegeta.Erguer`) e a
	/// casa fica sem mesa nenhuma.
	/// ==============================================================================================
	/// </summary>
	internal static string? EstadoDoMapa(string tipo, string? estadoDoDm) =>
		!MesaComEstadoDoDmDeTeste && tipo == MesaRedonda && estadoDoDm == EstadoErradoDaMesa ? EstadoDaMesa : estadoDoDm;

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
		HashSet<string> semAtlas, ref int proxId, Trava? trava = null)
	{
		string chave = Path.GetFileNameWithoutExtension(icone);
		if (!atlasPorNome.TryGetValue(chave, out List<string>? candidatos) || candidatos.Count == 0)
		{
			// PRESO: nome que existe em `Assets/Sprites` e nao esta no tileset do disco e folha que a
			// conversao cheia traria -- o andar nao pode sair sem ela. (Nome que nao existe em lugar
			// nenhum e o `.dmi` que nunca virou `.png`: fica sem desenho, como sempre ficou.)
			if (trava != null && trava.Suspeitas.TryGetValue(chave, out string? motivo))
				trava.Faltar($"a folha '{chave}' esta no tileset do disco e nao serve mais: {motivo}");
			else if (trava != null && trava.NoDisco.TryGetValue(chave, out List<string>? soltas))
				trava.Faltar($"a folha '{chave}' ({Path.GetRelativePath(raiz, soltas[0])}) nao esta no tileset do disco");
			semAtlas.Add(icone);
			return null;
		}

		string png = candidatos[0];
		if (candidatos.Count > 1) png = Escolher(candidatos, estado ?? "");

		// A CHAVE DO CACHE E O CAMINHO, nao o nome do icone: dois `Namek.dmi` diferentes sao
		// duas fontes diferentes, e guardar por nome faria um sobrescrever o outro de novo.
		if (fontes.TryGetValue(png, out Fonte? existente))
		{
			// PRESO: o estado que a folha do tileset nao tem e OUTRO arquivo de mesmo nome tem e o caso
			// em que a conversao cheia abriria uma fonte nova. Aqui ele cairia no quadro 0, calado.
			if (trava != null && existente.Achar(estado ?? "") == null
				&& trava.NoDisco.TryGetValue(chave, out List<string>? homonimos)
				&& homonimos.FirstOrDefault(h => !string.Equals(h, png, StringComparison.OrdinalIgnoreCase)
												 && TemEstado(h, estado ?? "", StringComparison.OrdinalIgnoreCase)) is { } outro)
				trava.Faltar($"o estado \"{estado}\" de '{chave}' so existe em {Path.GetRelativePath(raiz, outro)}, "
							 + $"e o tileset do disco usa {existente.ResPath}");
			return existente;
		}

		Fonte? f = Abrir(png, raiz, proxId);
		if (f == null) { semAtlas.Add(icone); return null; }

		proxId++;
		fontes[png] = f;
		return f;
	}

	/// <summary>
	/// QUAL DOS ARQUIVOS DE MESMO NOME: o que tem o estado pedido com o nome EXATO; na falta, o que o
	/// tem ignorando a caixa; na falta, a folha maior. A ordem das duas primeiras e a do
	/// <see cref="Fonte.Achar"/> -- uma folha com `computer2` nao pode ganhar de outra com `Computer2`
	/// quando o tipo pede `Computer2`.
	/// </summary>
	private static string Escolher(List<string> candidatos, string estado) =>
		candidatos.FirstOrDefault(c => TemEstado(c, estado, StringComparison.Ordinal))
		?? candidatos.FirstOrDefault(c => TemEstado(c, estado, StringComparison.OrdinalIgnoreCase))
		?? candidatos.OrderByDescending(c => new FileInfo(c).Length).First();

	private static bool TemEstado(string png, string estado, StringComparison caixa) =>
		DmiFile.Read(png) is { } m && m.States.Any(st => string.Equals(st.Name, estado, caixa));

	// =====================================================================
	// A FONTE DE UM TIPO QUANDO O NOME DA FOLHA NAO BASTA -- o que o `acrescentar` acrescenta
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada): a folha volta a ser achada so pelo NOME -- o `White rock.dmi` do
	/// minerio de Icer volta a ser a rocha de 250x250 da arvore `DU/`. Falso na conversao, sempre.
	/// </summary>
	public static bool FolhaPeloNomeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): todo desenho maior que o tile volta a ser centrado, e o `pixel_y` e
	/// o `step_y` voltam a ser ignorados -- a estatua do Inferno volta pra cima do degrau. Falso na
	/// conversao, sempre.
	/// </summary>
	public static bool TudoCentradoDeTeste;

	/// <summary>
	/// ONDE O BYOND POE UM DESENHO, dito como o `texture_origin` de um tile -- ou nulo quando o padrao
	/// da folha (<see cref="DeclararTiles"/>: centrado, base no pe da celula) ja e isso.
	///
	/// ============================ SO A ARVORE E CENTRADA ============================
	/// O BYOND ancora todo icone no canto INFERIOR ESQUERDO do tile e soma `pixel_x`/`pixel_y`. O jogo
	/// centra na mao as ARVORES (`Plants.dm:31-35`, no `New()` de `/obj/Trees`: `pixel_x = 16 -
	/// largura/2`) e a Reincarnation_Tree (`ReincarnationTree.dm:9-12`) -- e mais ninguem. O tileset
	/// centrava TODO tile maior que 32: a fonte de sangue do Inferno (`/turf/decor/PondBlood`, 145x112)
	/// saia 56 px a esquerda, as duas estatuas da escada 33 px (uma em cima do degrau), o portao 98.
	///
	/// `/obj/Trees/PalmTreeLeft` chama o `..()` e DEPOIS escreve `pixel_x = largura/2 - 16`
	/// (`Plants.dm:186-190`): e a unica arvore que nao fica centrada.
	///
	/// A CONTA: o Godot desenha o tile centrado na celula e SUBTRAI o `texture_origin`. A borda esquerda
	/// do desenho cai em `16 - largura/2 - origem.x` e a base em `16 + altura/2 - origem.y`, medidas do
	/// canto de cima da celula; o BYOND as quer em `pixel_x` e em `32 - pixel_y`. Os dois sao inteiros:
	/// numa folha de lado impar sobra meio pixel, o mesmo que o padrao ja deixa.
	/// ================================================================================
	/// </summary>
	internal static (int X, int Y)? AncoraDoTipo(string tipo, double pixelX, double pixelY, int largura, int altura)
	{
		int padraoY = (altura - Cell) / 2;
		if (TudoCentradoDeTeste) return null;

		const string palmeira = "/obj/Trees/PalmTreeLeft";
		bool centrada = tipo == "/obj/Reincarnation_Tree"
						|| (tipo.StartsWith("/obj/Trees/", StringComparison.Ordinal) && tipo != palmeira);
		if (tipo == palmeira) pixelX = largura / 2.0 - 16;

		int x = centrada ? 0 : -((largura - Cell) / 2) - (int)Math.Round(pixelX);
		int y = padraoY + (int)Math.Round(pixelY);
		return (x, y) == (0, padraoY) ? null : (x, y);
	}

	/// <summary>
	/// O quadro que este estado/direcao usa na folha PRESA tem outro desenho na folha do jogo? Nulo =
	/// nao deu pra comparar. Folha do jogo que nao tem o estado nao e "outro desenho": e o caso em que
	/// o BYOND cairia no estado vazio, e ai quem responde e a rede sem caixa do <see cref="Fonte.Achar"/>.
	/// </summary>
	private static bool? QuadroDifere(Fonte presa, string doJogo, string estado, int dir)
	{
		if (DmiFile.Read(doJogo) is not { } m) return null;
		if (m.IconWidth != presa.IconW || m.IconHeight != presa.IconH) return true;
		if (presa.Achar(estado) is not { } naPresa) return false;

		int cols = Math.Max(1, m.SheetWidth / Math.Max(1, m.IconWidth)), idx = 0;
		foreach (DmiState st in m.States)
		{
			int dirs = Math.Max(1, st.Dirs);
			if (st.Name == estado)
			{
				int i = idx + Math.Max(0, Direcoes.NaFolha(dir, dirs));
				(int X, int Y) a = Indice(presa, naPresa.Indice + Math.Max(0, Direcoes.NaFolha(dir, naPresa.Dirs)));
				return !PngPixels.QuadrosIguais(presa.Chave, a.X * presa.IconW, a.Y * presa.IconH, doJogo,
												i % cols * m.IconWidth, i / cols * m.IconHeight, presa.IconW, presa.IconH);
			}
			idx += dirs * Math.Max(1, st.Frames);
		}
		return false;
	}

	/// <summary>
	/// O NOME DE UMA FONTE ACRESCENTADA NO `tiles.json`: `folha[~pasta.com.pontos][@x,y]`.
	///
	/// NAO PODE TER BARRA, e nao pode repetir um nome que ja existe: o leitor do jogo
	/// (`CatalogoDeTiles.Parse`) corta o nome na ultima barra e guarda um atlas por nome, o ULTIMO
	/// vencendo -- uma segunda `White rock` trocaria, calada, a folha de quem ja pede `White rock`
	/// pelo nome. O `~pasta` so entra na folha HOMONIMA (o nome puro ja e de outra); o `@x,y` e a
	/// ancora de uma variante (<see cref="Fonte.Sufixo"/>). O <see cref="Semear"/> le os dois de volta.
	/// </summary>
	private static string NomeNoIndice(string png, string pastaDeSprites, bool nomeTomado, (int X, int Y)? origem)
	{
		string nome = Path.GetFileNameWithoutExtension(png);
		if (nomeTomado)
			nome += "~" + (Path.GetDirectoryName(Path.GetRelativePath(pastaDeSprites, png)) ?? "")
				.Replace('\\', '.').Replace('/', '.');
		return nome + (origem is { } o ? $"@{o.X},{o.Y}" : "");
	}

	/// <summary>A ancora escrita no fim de um nome do indice (`@x,y`), ou nula.</summary>
	private static (int X, int Y)? OrigemDoNome(string nome)
	{
		int arroba = nome.LastIndexOf('@');
		if (arroba < 0) return null;
		string[] xy = nome[(arroba + 1)..].Split(',');
		return xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y) ? (x, y) : null;
	}

	/// <summary>Conta um tile declarado: a coordenada e quantos quadros ele anima (o formato do <see cref="DeclararTiles"/>).</summary>
	private static void AnotarTile(Dictionary<(int X, int Y), int> tiles, string linha)
	{
		if (linha.Length == 0 || !char.IsDigit(linha[0]) || RxTile.Match(linha) is not { Success: true } m) return;
		(int X, int Y) c = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
		int quadros = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) + 1 : 1;
		tiles[c] = Math.Max(tiles.GetValueOrDefault(c), quadros);
	}

	/// <summary>
	/// A FOLHA DE UM TIPO: a presa, a nao ser que o jogo queira dizer outro arquivo.
	///   - sem folha presa (o tileset nunca teve o nome): a copia do arquivo do jogo em `Assets/Sprites`,
	///     ou -- arte que nunca foi copiada -- o caminho que ela TERA, dentro do rascunho, e de onde copiar;
	///   - com folha presa: a do jogo so quando e OUTRO arquivo e o quadro pedido tem outro desenho nele.
	///     Onde o desenho e o mesmo a presa fica, por mais que seja a homonima (as portas `Door4` e sete
	///     usuarios de `Lab.dmi` pintam hoje a copia da arvore `DU/`, com os mesmos pixels): trocar a
	///     fonte delas mexeria em `.portas`, na cidade de Vegeta e em quem acha o tile pelo nome, sem
	///     mudar um pixel.
	/// </summary>
	/// <returns>A folha (vazia = nenhuma); de onde copia-la, se ela ainda nao existe; e se a comparacao de quadros falhou.</returns>
	private static (string Png, string? CopiarDe, bool SemComparar) FolhaDoTipo(
		IconesDoDm? icones, string sprites, string rascunho, string icone, string estado, int dir, Fonte? presa)
	{
		string daPresa = presa?.Chave ?? "";
		if (FolhaPeloNomeDeTeste || icones?.Resolver(icone, estado) is not { } doDm) return (daPresa, null, false);

		string rel = icones.Espelho(doDm);
		string espelho = Path.GetFullPath(Path.Combine(sprites, rel));
		if (presa == null)
		{
			if (File.Exists(espelho)) return (espelho, null, false);
			return DmiFile.Read(doDm) == null || rascunho.Length == 0
				? ("", null, false)
				: (Path.GetFullPath(Path.Combine(rascunho, "Assets", "Sprites", rel)), doDm, false);
		}
		if (!File.Exists(espelho) || string.Equals(espelho, Path.GetFullPath(presa.Chave), StringComparison.OrdinalIgnoreCase))
			return (daPresa, null, false);

		bool? difere = QuadroDifere(presa, espelho, estado, dir);
		return difere == null ? (espelho, null, true) : (difere.Value ? espelho : daPresa, null, false);
	}

	/// <summary>
	/// A MESMA ESCOLHA DE FOLHA, pra bancada perguntar sem converter nada: a folha presa e dita pelo
	/// caminho do `.png` dela (nulo = o tileset nao tem folha pro nome).
	/// </summary>
	internal static string FolhaDoTipoParaBancada(IconesDoDm icones, string sprites, string rascunho, string icone,
												  string estado, string? pngDaPresa) =>
		FolhaDoTipo(icones, sprites, rascunho, icone, estado, Direcoes.Sul,
					pngDaPresa == null ? null : Abrir(pngDaPresa, sprites, 0)).Png;

	/// <summary>
	/// A FONTE DE UM TIPO, PRESA -- a folha que o tileset do disco tem pro nome, a nao ser que o tipo
	/// precise de OUTRA fonte:
	///   - a folha do JOGO, quando a presa e uma homonima e o quadro pedido tem outro desenho nela
	///     (ver <see cref="IconesDoDm"/>), ou quando o tileset nunca teve folha nenhuma pro nome (arte
	///     que nao foi copiada: `jungletree3.png`, as 11 arvores de Hera que nao aparecem nem param);
	///   - uma VARIANTE com ancora propria, quando o BYOND nao centra o desenho (<see cref="AncoraDoTipo"/>).
	///
	/// NADA DO QUE O DISCO TEM E MEXIDO. A variante divide a textura com a fonte comum e nasce com outro
	/// `id`: o tile que ja existe serve a arvore centrada E ao desenho ancorado, e mudar a ancora dele
	/// deslocaria o que hoje esta certo em todos os mapas.
	///
	/// SO O `acrescentar` CRIA (<see cref="Trava.Novas"/>). No `repintar`, o tipo que pedir uma fonte que
	/// o disco nao tem vira falta -- e a falta diz qual comando a traz.
	/// </summary>
	private static Fonte? FonteDoTipo(TurfDef td, Trava trava, Dictionary<string, Fonte> fontes,
		Dictionary<string, List<string>> atlasPorNome, HashSet<string> semAtlas, ref int proxId)
	{
		string icone = td.Icon!, estado = td.IconState ?? "";
		if (EhPorta(td.Path) && estado.Length == 0) estado = "Closed";
		string nome = Path.GetFileNameWithoutExtension(icone);
		Fonte? presa = atlasPorNome.TryGetValue(nome, out List<string>? l) && l.Count > 0 ? fontes.GetValueOrDefault(l[0]) : null;

		// 1. A FOLHA
		(string png, string? copiarDe, bool semComparar) =
			FolhaDoTipo(trava.Icones, trava.Sprites, trava.Rascunho, icone, estado, td.Dir, presa);
		if (semComparar)
			trava.Faltar($"{td.Path}: nao deu pra comparar o quadro \"{estado}\" de {presa!.ResPath} com o da folha do jogo ({png})");
		if (png.Length == 0) return Garantir(icone, td.IconState, trava.Raiz, fontes, atlasPorNome, semAtlas, ref proxId, trava);

		// a arte que nunca foi copiada nasce no rascunho, e o `res://` dela e o que tera no repo
		string raizDoRes = copiarDe == null ? trava.Raiz : trava.Rascunho;
		string pastaDeSprites = copiarDe == null ? trava.Sprites : Path.Combine(trava.Rascunho, "Assets", "Sprites");

		// 2. A ANCORA
		bool aPresa = presa != null && png == presa.Chave;
		DmiFile.Result? meta = aPresa ? null : DmiFile.Read(copiarDe ?? png);
		if (!aPresa && meta == null) { semAtlas.Add(icone); return null; }
		(int X, int Y)? origem = AncoraDoTipo(td.Path, td.PixelX, td.PixelY,
											  aPresa ? presa!.IconW : meta!.IconWidth, aPresa ? presa!.IconH : meta!.IconHeight);
		if (aPresa && origem == null)
			return Garantir(icone, td.IconState, trava.Raiz, fontes, atlasPorNome, semAtlas, ref proxId, trava);

		string entrada = png + (origem is { } o ? $"@{o.X},{o.Y}" : "");
		if (fontes.TryGetValue(entrada, out Fonte? ja)) return ja;

		// 3. A FONTE NOVA
		string nomeNovo = NomeNoIndice(png, pastaDeSprites, nomeTomado: presa != null && !aPresa, origem);
		if (trava.Novas == null)
		{
			trava.Faltar($"{td.Path}: pede a fonte `{nomeNovo}` ({Path.GetRelativePath(pastaDeSprites, png).Replace('\\', '/')}), "
						 + "que o tileset do disco nao tem -- quem a traz e o comando `acrescentar`");
			return null;
		}
		if (copiarDe != null && !File.Exists(png))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(png)!);
			File.Copy(copiarDe, png);
		}

		if (Abrir(png, raizDoRes, trava.ProximoId) is not { } nova) { semAtlas.Add(icone); return null; }
		nova.Origem = origem;
		nova.Nome = nomeNovo;
		trava.ProximoId++;
		fontes[nova.Entrada] = nova;
		trava.Novas.Add(nova);
		if (copiarDe != null && !trava.Copiados.Contains(nova.ResPath)) trava.Copiados.Add(nova.ResPath);
		if (presa == null && origem == null) atlasPorNome[nome] = [png];   // o nome estava livre: passa a ser desta folha

		// ...E A TIRA DELA, se a folha tem animacao que nao cabe numa linha e a tira ja esta composta no
		// disco (compor e o Godot quem faz, e este comando nao o chama). Sem a tira, a celula que
		// precisar dela e acusada na hora de pintar.
		List<AtlasAnimado.Tira> plano = PlanejarTiras(nova);
		Fonte tira = Companheira(nova, plano, trava.ProximoId, raizDoRes);
		if (plano.Count > 0 && File.Exists(tira.Chave))
		{
			tira.Origem = origem;
			tira.Nome = Path.GetFileNameWithoutExtension(tira.Chave) + nomeNovo[Path.GetFileNameWithoutExtension(png).Length..];
			trava.ProximoId++;
			nova.Companheira = tira;
			fontes[tira.Entrada] = tira;
			trava.Novas.Add(tira);
		}

		// o que a fonte nova DECLARA: a mesma lista que o `Por` cobra das fontes do disco
		foreach (Fonte f in nova.Companheira == null ? [nova] : new[] { nova, nova.Companheira })
		{
			var bloco = new StringBuilder();
			DeclararTiles(bloco, f);
			Dictionary<(int X, int Y), int> tiles = trava.Declarados[f.Id] = [];
			foreach (string linha in bloco.ToString().Split('\n')) AnotarTile(tiles, linha);
		}
		return nova;
	}

	/// <summary>O trecho de UMA fonte no `tileset.tres`: a textura, o atlas com os tiles dela e a linha de `sources/`.</summary>
	private static (int Tiles, int Animados) BlocoDaFonte(Fonte f, StringBuilder ext, StringBuilder sub, StringBuilder res)
	{
		string extId = $"{f.Id}_atlas";
		ext.Append($"[ext_resource type=\"Texture2D\" path=\"{f.ResPath}\" id=\"{extId}\"]\n");

		sub.Append($"[sub_resource type=\"TileSetAtlasSource\" id=\"Atlas_{f.Id}\"]\n");
		sub.Append($"texture = ExtResource(\"{extId}\")\n");
		sub.Append($"texture_region_size = Vector2i({f.IconW}, {f.IconH})\n");
		(int Tiles, int Animados) conta = DeclararTiles(sub, f);
		sub.Append('\n');

		res.Append($"sources/{f.Id} = SubResource(\"Atlas_{f.Id}\")\n");
		return conta;
	}

	/// <summary>
	/// GRAVA NO RASCUNHO O TILESET E O INDICE DO DISCO COM AS FONTES NOVAS -- e so com elas a mais.
	///
	/// ============================ SO ACRESCIMO, E NO TEXTO ============================
	/// O `tileset.tres` e de todos os mapas: uma fonte que mudasse de `id`, de textura ou de grade
	/// trocaria o desenho de celulas que este comando nem leu. Entao ele NAO e reescrito a partir das
	/// fontes em memoria (que e o que a conversao cheia faz, e por isso ela renumera): o texto do disco
	/// e copiado byte a byte e recebe tres insercoes -- as texturas novas depois da ultima
	/// `ext_resource`, os atlas novos antes do `[resource]`, as linhas de `sources/` no fim -- mais o
	/// `load_steps` do cabecalho, que conta os recursos. O `tiles.json` ganha as entradas novas depois
	/// da ultima. Nenhuma linha que existe e tocada alem do cabecalho.
	/// ==================================================================================
	/// </summary>
	private static void EscreverAcrescimos(Trava trava, string indiceDoDisco)
	{
		List<Fonte> novas = trava.Novas!;
		string pastaMaps = Path.Combine(trava.Rascunho, "Assets", "Maps"), pastaData = Path.Combine(trava.Rascunho, "Assets", "Data");
		Directory.CreateDirectory(pastaMaps);
		Directory.CreateDirectory(pastaData);

		var ext = new StringBuilder();
		var sub = new StringBuilder();
		var res = new StringBuilder();
		foreach (Fonte f in novas) BlocoDaFonte(f, ext, sub, res);

		string texto = File.ReadAllText(Path.Combine(trava.PastaDoDisco, "tileset.tres"));
		string nl = texto.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string Fim(StringBuilder sb) => nl == "\n" ? sb.ToString() : sb.ToString().Replace("\n", nl);

		int ultimaTextura = texto.LastIndexOf("[ext_resource ", StringComparison.Ordinal);
		int fimDasTexturas = texto.IndexOf('\n', ultimaTextura) + 1;
		int recurso = texto.IndexOf(nl + "[resource]" + nl, StringComparison.Ordinal) + nl.Length;
		Match passos = Regex.Match(texto, @"load_steps=(\d+)");
		if (ultimaTextura < 0 || recurso < nl.Length || !passos.Success || passos.Index > texto.IndexOf('\n'))
			throw new InvalidDataException("o tileset.tres do disco nao tem a forma que o `EscreverTileSet` escreve");

		string cabecalho = texto[..passos.Groups[1].Index] + (int.Parse(passos.Groups[1].Value) + 2 * novas.Count)
						   + texto[(passos.Groups[1].Index + passos.Groups[1].Length)..fimDasTexturas];
		File.WriteAllText(Path.Combine(pastaMaps, "tileset.tres"),
						  cabecalho + Fim(ext) + texto[fimDasTexturas..recurso] + Fim(sub) + texto[recurso..] + Fim(res),
						  new UTF8Encoding(false));

		string indice = File.ReadAllText(indiceDoDisco);
		int ultima = indice.LastIndexOf('}');
		var entradas = new StringBuilder();
		foreach (Fonte f in novas)
			entradas.Append(",\n").Append(TileIndex.Entrada(f.Nome, new FonteDeAtlas(
				f.Id, f.Chave, f.ResPath, f.IconW, f.IconH, f.Cols,
				f.SemCaixa.ToDictionary(kv => kv.Key, kv => kv.Value.Indice, StringComparer.OrdinalIgnoreCase))));
		string nlDoIndice = indice.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		File.WriteAllText(Path.Combine(pastaData, "tiles.json"),
						  indice[..(ultima + 1)] + entradas.ToString().Replace("\n", nlDoIndice) + indice[(ultima + 1)..],
						  new UTF8Encoding(false));
	}

	/// <summary>
	/// ABRE UMA FOLHA: le a grade e os estados do `.png` (que e o `.dmi` copiado cru, com os metadados
	/// dentro) e monta a fonte com o `id` dado. Nulo quando o arquivo nao e um PNG que se leia.
	/// </summary>
	private static Fonte? Abrir(string png, string raiz, int id)
	{
		DmiFile.Result? meta = DmiFile.Read(png);
		if (meta == null) return null;

		var f = new Fonte
		{
			Id = id,
			Chave = png,
			ResPath = "res://" + Path.GetRelativePath(raiz, png).Replace('\\', '/'),
			IconW = meta.IconWidth,
			IconH = meta.IconHeight,
			Cols = Math.Max(1, meta.SheetWidth / Math.Max(1, meta.IconWidth)),
		};

		int idx = 0;
		foreach (DmiState st in meta.States)
		{
			var e = new EstadoDaFolha(st.Name, idx, Math.Max(1, st.Dirs), Math.Max(1, st.Frames));
			f.Exatos.TryAdd(st.Name, e);
			f.SemCaixa.TryAdd(st.Name, e);
			idx += e.Dirs * e.Quadros;
		}
		f.States = meta.States;
		f.TotalQuadros = idx;
		return f;
	}

	/// <summary>
	/// ONDE O CONVERSOR APONTA `(estado, direcao)` NUMA FOLHA -- a mesma conta que pinta o mapa
	/// (<see cref="Fonte.Achar"/> + <see cref="Coord"/>), aberta pra bancada perguntar sem converter
	/// nada. Nulo quando a folha nao abre ou nao tem o estado.
	/// </summary>
	internal static (int X, int Y)? QuadroNaFolha(string png, string? estado, int dir = Direcoes.Sul) =>
		Abrir(png, Path.GetDirectoryName(png) ?? "", 0) is { } f && f.Achar(estado ?? "") != null
			? Coord(f, estado, dir)
			: null;

	// =====================================================================
	// AS FONTES PRESAS -- o tileset do disco, lido de volta (ver RepintarAndar)
	// =====================================================================

	/// <summary>
	/// SEMEIA AS FONTES A PARTIR DO DISCO, em vez de descobri-las por ordem.
	///
	/// QUEM MANDA E O `tiles.json`: o `id` de cada folha, a textura, as colunas e o tamanho do icone. O
	/// `tileset.tres` ao lado entra pra duas coisas que o indice nao guarda -- quais tiles ele DECLARA
	/// (e com quantos quadros de animacao) e a prova de que os dois arquivos falam do mesmo `id`. Os
	/// estados de cada folha vem do proprio `.png`, que traz os nomes com a caixa certa (o indice so
	/// guarda o primeiro de cada nome sem caixa) -- e tem que ser os MESMOS que o indice lista, cada
	/// um no mesmo quadro: folha que mudou por dentro desde o tileset nao e a folha presa.
	///
	/// FOLHA QUE NAO BATE NAO DERRUBA A SEMEADURA: ela fica de fora, com o motivo anotado, e so vira
	/// falta se o andar a pedir. O tileset tem 162 fontes e Vegeta usa 39.
	///
	/// AS TIRAS `__anim` NAO SAO COMPOSTAS, SAO REENCONTRADAS: o plano de cada uma (que estado mora
	/// em que linha) e funcao pura da folha dona, entao e refeito por <see cref="PlanejarTiras"/> e
	/// casado com a tira que o tileset ja tem -- linha por linha, com o numero de quadros de cada uma.
	/// Dona com plano e sem tira que case fica sem companheira, e a celula que precisar dela e acusada
	/// na hora de pintar, com o estado e a linha que faltaram (ver o `Por`).
	/// </summary>
	/// <returns>Falso quando nem da pra comecar (falta o indice ou o tileset).</returns>
	private static bool Semear(Trava trava, Dictionary<string, Fonte> fontes, Dictionary<string, List<string>> atlasPorNome)
	{
		string arqIndice = trava.Indice;
		string arqTileset = Path.Combine(trava.PastaDoDisco, "tileset.tres");
		if (!File.Exists(arqIndice) || !File.Exists(arqTileset))
		{
			Console.WriteLine($"ERRO: sem {arqIndice} ou sem {arqTileset} -- nao ha tileset do disco pra prender");
			return false;
		}

		Dictionary<int, string> texturas = LerTileSet(arqTileset, trava.Declarados);
		trava.ProximoId = texturas.Count == 0 ? 0 : texturas.Keys.Max() + 1;
		Jandirus.Core.World.CatalogoDeTiles indice =
			Jandirus.Core.World.CatalogoDeTiles.Parse(File.ReadAllText(arqIndice));

		string NoDisco(string res) => trava.ArquivoDe(res);

		var tiras = new List<Jandirus.Core.World.AtlasDeTiles>();
		var planos = new Dictionary<string, List<AtlasAnimado.Tira>>(StringComparer.OrdinalIgnoreCase);
		foreach (Jandirus.Core.World.AtlasDeTiles a in indice.Todos.OrderBy(a => a.Fonte))
		{
			if (!texturas.TryGetValue(a.Fonte, out string? textura) || textura != a.ResPath)
			{
				trava.Suspeitas[a.Nome] = $"o tiles.json diz fonte {a.Fonte} = {a.ResPath} e o tileset.tres diz "
										  + (textura ?? "que essa fonte nao existe");
				continue;
			}
			// FONTE ACRESCENTADA: o nome traz a ancora da variante (`@x,y`) e, na folha homonima, a pasta
			// (`~pasta`) -- ver `NomeNoIndice`. A variante divide a textura com a fonte comum e entra no
			// dicionario pela `Entrada` dela; o nome PURO continua sendo so o da fonte comum.
			(int X, int Y)? origem = OrigemDoNome(a.Nome);
			int fimDaFolha = a.Nome.IndexOfAny(['~', '@']);
			string folha = fimDaFolha < 0 ? a.Nome : a.Nome[..fimDaFolha];
			if (folha.EndsWith(AtlasAnimado.Sufixo, StringComparison.Ordinal)) { tiras.Add(a); continue; }

			string png = NoDisco(a.ResPath);
			Fonte? f = File.Exists(png) ? Abrir(png, trava.Raiz, a.Fonte) : null;
			if (f != null)
			{
				f.ResPath = a.ResPath;
				f.Origem = origem;
				f.Nome = a.Nome;
			}
			if (f == null) { trava.Suspeitas[a.Nome] = $"{a.ResPath} nao abre"; continue; }
			if (f.Cols != a.Colunas || f.IconW != a.LarguraDoIcone || f.IconH != a.AlturaDoIcone)
			{
				trava.Suspeitas[a.Nome] = $"{a.ResPath} tem hoje {f.Cols} colunas de {f.IconW}x{f.IconH} e o tiles.json "
										  + $"guarda {a.Colunas} de {a.LarguraDoIcone}x{a.AlturaDoIcone}";
				continue;
			}
			if (a.Estados.Count != f.SemCaixa.Count
				|| a.Estados.Any(kv => !f.SemCaixa.TryGetValue(kv.Key, out EstadoDaFolha e) || Indice(f, e.Indice) != kv.Value))
			{
				trava.Suspeitas[a.Nome] = $"os estados de {a.ResPath} nao sao mais os que o tiles.json lista "
										  + $"({f.SemCaixa.Count} na folha de hoje, {a.Estados.Count} no indice)";
				continue;
			}
			fontes[f.Entrada] = f;
			if (origem == null && !a.Nome.Contains('~')) atlasPorNome[a.Nome] = [png];
			planos[f.Entrada] = PlanejarTiras(f);
		}

		int casadas = 0;
		foreach (Jandirus.Core.World.AtlasDeTiles a in tiras)
		{
			string pngDaTira = NoDisco(a.ResPath);
			string pngDaDona = pngDaTira[..^(AtlasAnimado.Sufixo.Length + ".png".Length)] + ".png";
			string daDona = pngDaDona + (OrigemDoNome(a.Nome) is { } o ? $"@{o.X},{o.Y}" : "");
			if (!fontes.TryGetValue(daDona, out Fonte? dona)) continue;   // a dona ja ficou de fora, com motivo

			Fonte tira = Companheira(dona, planos[daDona], a.Fonte, trava.Raiz);
			tira.ResPath = a.ResPath;
			tira.Origem = dona.Origem;
			tira.Nome = a.Nome;
			bool linhasBatem = Enumerable.Range(0, dona.Duracoes.Count)
				.All(l => trava.QuadrosDeclarados(a.Fonte, (0, l)) == dona.Duracoes[l].Length);
			if (!File.Exists(tira.Chave) || tira.Cols != a.Colunas || !linhasBatem) continue;

			dona.Companheira = tira;
			fontes[tira.Entrada] = tira;
			casadas++;
		}

		Console.WriteLine($"FONTES PRESAS: {fontes.Count} do tileset do disco ({casadas} sao tiras de animacao reencontradas)"
						  + (trava.Suspeitas.Count > 0 ? $" | {trava.Suspeitas.Count} fora, so acusadas se o andar pedir" : ""));
		foreach ((string nome, string motivo) in trava.Suspeitas.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
			Console.WriteLine($"   fora: {nome} -- {motivo}");
		return true;
	}

	private static readonly Regex RxTextura = new(
		@"^\[ext_resource type=""Texture2D"" path=""([^""]+)"" id=""(\d+)_atlas""\]", RegexOptions.Compiled);
	private static readonly Regex RxAtlas = new(@"^\[sub_resource type=""TileSetAtlasSource"" id=""Atlas_(\d+)""\]", RegexOptions.Compiled);
	private static readonly Regex RxTile = new(@"^(\d+):(\d+)/(?:0 = 0|animation_frame_(\d+)/duration\b)", RegexOptions.Compiled);

	/// <summary>
	/// O QUE O `tileset.tres` DECLARA: a textura de cada fonte e, por fonte, cada tile com o numero de
	/// quadros que ele anima. O formato e o que o <see cref="EscreverTileSet"/> escreve, linha a linha.
	/// </summary>
	private static Dictionary<int, string> LerTileSet(string caminho, Dictionary<int, Dictionary<(int X, int Y), int>> declarados)
	{
		var texturas = new Dictionary<int, string>();
		Dictionary<(int X, int Y), int>? tiles = null;
		foreach (string linha in File.ReadLines(caminho))
		{
			if (linha.Length == 0) continue;
			if (linha[0] == '[')
			{
				tiles = null;
				if (RxTextura.Match(linha) is { Success: true } t) texturas[int.Parse(t.Groups[2].Value)] = t.Groups[1].Value;
				else if (RxAtlas.Match(linha) is { Success: true } a) declarados[int.Parse(a.Groups[1].Value)] = tiles = [];
				continue;
			}
			if (tiles != null) AnotarTile(tiles, linha);
		}
		return texturas;
	}

	/// <summary>
	/// PLANEJA E COMPOE OS ATLAS COMPANHEIROS: uma tira por estado animado que nao cabia numa linha.
	///
	/// A FISICA E A OCLUSAO VAO JUNTO. Um tile de agua que virou tira continua sendo o mesmo turf:
	/// se o original era denso ou opaco, a tira tem que ser tambem, senao a parede animada deixa de
	/// bloquear ao ser reempacotada -- um defeito que so apareceria no tile que anima.
	/// </summary>
	private static int Reempacotar(Dictionary<string, Fonte> fontes, string raiz, ref int proxId)
	{
		var trabalho = new Dictionary<string, (string, int, int, int, List<AtlasAnimado.Tira>)>();
		var novas = new List<Fonte>();
		int total = 0;

		foreach (Fonte f in fontes.Values.ToList())
		{
			List<AtlasAnimado.Tira> tiras = PlanejarTiras(f);
			if (tiras.Count == 0) continue;
			total += tiras.Count;

			Fonte comp = Companheira(f, tiras, proxId++, raiz);

			// a fisica/oclusao do original vale pra tira: e o mesmo turf desenhado noutro lugar
			foreach ((string estado, int linha) in f.Refeitos)
			{
				(int X, int Y) orig = Coord(f, estado);
				if (f.Densas.Contains(orig)) comp.Densas.Add((0, linha));
				if (f.Opacas.Contains(orig)) comp.Opacas.Add((0, linha));
			}

			f.Companheira = comp;
			novas.Add(comp);
			trabalho[f.Chave] = (comp.Chave, f.IconW, f.IconH, f.Cols, tiras);
		}

		if (total == 0) return 0;

		AtlasAnimado.Compor(trabalho, raiz, Environment.GetEnvironmentVariable("GODOT") is { Length: > 0 } g && File.Exists(g) ? g : null);

		// SO ENTRA NO TILESET O QUE FOI ESCRITO DE VERDADE. Se o Godot nao rodou, a fonte
		// companheira apontaria pra um arquivo inexistente e o tileset inteiro falharia ao carregar
		// -- pior que ficar sem a animacao.
		int entraram = 0;
		foreach (Fonte comp in novas)
		{
			if (!File.Exists(comp.Chave)) continue;
			fontes[comp.Chave] = comp;
			entraram++;
		}

		if (entraram == novas.Count) return total;

		// nao deu: desfaz os apontamentos pra ninguem ficar apontando pro vazio
		foreach (Fonte f in fontes.Values)
			if (f.Companheira != null && !File.Exists(f.Companheira.Chave))
			{
				f.Companheira = null;
				f.Refeitos.Clear();
			}
		return 0;
	}

	/// <summary>
	/// O PLANO DAS TIRAS DE UMA FOLHA: quais estados animados nao cabem numa linha dela, e em que
	/// linha do companheiro cada um vai morar (anotado na propria folha, em `Refeitos` e `Duracoes`).
	/// E funcao pura da folha -- por isso serve tanto pra COMPOR o companheiro
	/// (<see cref="Reempacotar"/>) quanto pra REENCONTRAR o que o disco ja tem (<see cref="Semear"/>).
	///
	/// SO A DIRECAO SUL VAI PRA TIRA. Tudo que anima nos mapas de hoje olha pro sul (agua, lava, o
	/// brilho do teleporte), e reempacotar as quatro direcoes de cada estado quadruplicaria a imagem
	/// por celulas que nao existem. A celula que pedir OUTRA direcao de um estado destes fica no atlas
	/// original, parada, e sai no relatorio de animacao perdida do andar.
	/// </summary>
	private static List<AtlasAnimado.Tira> PlanejarTiras(Fonte f)
	{
		var tiras = new List<AtlasAnimado.Tira>();
		int idx = 0;

		foreach (DmiState st in f.States)
		{
			var e = new EstadoDaFolha(st.Name, idx, Math.Max(1, st.Dirs), Math.Max(1, st.Frames));

			// se cabe na linha, o atlas original ja declara o tile animado (ver `DeclararTiles`)
			if (e.Quadros > 1 && !CabeNaLinha(f, e))
			{
				var q = new int[e.Quadros];
				for (int k = 0; k < e.Quadros; k++) q[k] = idx + k * e.Dirs;

				var dur = new double[e.Quadros];
				for (int k = 0; k < e.Quadros; k++)
				{
					double d10 = k < st.Delays.Length ? st.Delays[k] : 1;
					dur[k] = Math.Max(d10, 0.1) / 10.0;   // decimos do BYOND -> segundos
				}

				// nome repetido na folha: vale a tira do PRIMEIRO, que e o estado que o `Achar` devolve
				f.Refeitos.TryAdd(st.Name, tiras.Count);
				f.Duracoes.Add(dur);
				tiras.Add(new AtlasAnimado.Tira { Origem = f.Chave, Quadros = q, Linha = tiras.Count });
			}

			idx += e.Dirs * e.Quadros;
		}
		return tiras;
	}

	/// <summary>A fonte do atlas companheiro de uma folha: uma linha por tira, na largura da mais comprida.</summary>
	private static Fonte Companheira(Fonte f, List<AtlasAnimado.Tira> tiras, int id, string raiz)
	{
		string destino = Path.ChangeExtension(f.Chave, null) + AtlasAnimado.Sufixo + ".png";
		int largura = tiras.Count == 0 ? 0 : tiras.Max(t => t.Quadros.Length);
		return new Fonte
		{
			Id = id,
			Chave = destino,
			ResPath = "res://" + Path.GetRelativePath(raiz, destino).Replace('\\', '/'),
			IconW = f.IconW,
			IconH = f.IconH,
			Cols = largura,
			TotalQuadros = largura * tiras.Count,
			Duracoes = f.Duracoes,
		};
	}

	/// <summary>
	/// O quadro de um estado NUMA DIRECAO -- o primeiro da animacao dela. Estado que a folha nao tem
	/// cai no quadro 0 e direcao que o estado nao tem cai na primeira; quem pinta o mapa conta os dois
	/// casos antes de chegar aqui (ver o `Por`).
	/// </summary>
	private static (int X, int Y) Coord(Fonte f, string? estado, int dir = Direcoes.Sul) =>
		f.Achar(estado ?? "") is { } e ? Indice(f, e.Indice + Math.Max(0, Direcoes.NaFolha(dir, e.Dirs))) : (0, 0);

	/// <summary>
	/// Os quadros deste estado, na direcao dada, cabem todos na MESMA LINHA do atlas?
	///
	/// E a condicao pra o `DeclararTiles` conseguir declarar o tile animado no atlas ORIGINAL --
	/// com `animation_columns = 0` o Godot le os quadros correndo pra direita a partir da coordenada
	/// base, e virar a linha nao existe. Os quadros de uma direcao ficam a `Dirs` celulas um do outro.
	/// </summary>
	private static bool CabeNaLinha(Fonte f, EstadoDaFolha e, int dirNaFolha = 0) =>
		Indice(f, e.Indice + dirNaFolha).X + (e.Quadros - 1) * e.Dirs < f.Cols
		&& e.Indice + (e.Quadros - 1) * e.Dirs + dirNaFolha < f.TotalQuadros;

	/// <summary>
	/// OS MAPAS NA ORDEM DO `.dme`, cada um com o DESLOCAMENTO de z dele. CADA .dmm NUMERA O PROPRIO z A
	/// PARTIR DE 1; o z real do jogo vem da ordem em que o .dme inclui os arquivos: o 1o mapa ocupa
	/// z1..zN, o 2o continua de zN+1, e assim por diante. Sem este deslocamento, quatro mapas diferentes
	/// gerariam quatro "z01". Partilhado entre a conversao inteira e o comando `bercos`, que so reescreve
	/// os marcos -- duas leituras com dois deslocamentos seriam a receita de um berco no andar errado.
	/// </summary>
	internal static List<(string Arquivo, DmmMap.Result Dados, int Offset)> LerMapas(string dmmDir)
	{
		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}
		return mapas;
	}

	// =====================================================================
	// OS BERCOS -- o `/obj/SpawnPoint` de cada andar
	// =====================================================================
	/// <summary>
	/// Um `/obj/SpawnPoint` do `.dmm` (`SpawnPoints.dm:40-49`), ja em CELULA do port.
	///
	/// ============================ O Y JA SAI VIRADO, E SAI DE GRACA ============================
	/// O `DmmLevel.Cells[x, y]` guarda a linha 0 como a PRIMEIRA do arquivo, que no BYOND e o y mais
	/// ALTO (`y = H`). Ou seja: BYOND (bx, by) = (x + 1, H - y), e a celula do port -- que conta de cima
	/// pra baixo -- e (bx - 1, H - by) = (x, y). O indice do arquivo E a celula; nenhuma conta a mais, e
	/// portanto nenhuma chance de espelhar o berco na vertical (o defeito que a `--diagberco` confere
	/// contra uma leitura independente do `.dmm`).
	/// ============================================================================================
	/// </summary>
	internal readonly record struct MarcoDoDmm(int Cx, int Cy, string Planeta, string Raca, string Nome);

	/// <summary>`nome = "Spawnpoint (Namek)"; spawnPlanet = "Namek"; spawnRace = "Namekian"` -- as vars inline de um obj.</summary>
	private static readonly Regex RxVarInline = new(@"(\w+)\s*=\s*(?:""([^""]*)""|([-\d.]+))", RegexOptions.Compiled);

	/// <summary>
	/// O MARCO DE CADA ANDAR (z real -> marco), lido dos `/obj/SpawnPoint` do `.dmm`.
	///
	/// So o PRIMEIRO de cada andar conta -- e o `GotoPlanet` do DM tambem para no primeiro achado
	/// (`SpawnPoints.dm:86-92`). Marco com `Disabled = 1` e pulado, como la (`:77`). Nos 40 andares de hoje
	/// sao 14 marcos, um por planeta, nenhum desabilitado.
	/// </summary>
	internal static Dictionary<int, MarcoDoDmm> ExtrairBercos(List<(string Arquivo, DmmMap.Result Dados, int Offset)> mapas)
	{
		var saida = new Dictionary<int, MarcoDoDmm>();
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
		{
			var marcos = new Dictionary<string, MarcoDoDmm>(StringComparer.Ordinal);
			foreach ((string chave, string[] tipos) in dados.Keys)
				foreach (string tp in tipos)
				{
					if (DmmMap.BasePath(tp) != "/obj/SpawnPoint") continue;
					var vars = new Dictionary<string, string>(StringComparer.Ordinal);
					int a = tp.IndexOf('{');
					if (a >= 0)
						foreach (Match m in RxVarInline.Matches(tp[a..]))
							vars[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
					if (vars.GetValueOrDefault("Disabled", "0") != "0") continue;
					// os padroes sao os do tipo (`SpawnPoints.dm:46-47`): Human na Terra
					marcos[chave] = new MarcoDoDmm(0, 0,
						vars.GetValueOrDefault("spawnPlanet", "Earth"),
						vars.GetValueOrDefault("spawnRace", "Human"),
						vars.GetValueOrDefault("name", "Spawnpoint"));
				}
			if (marcos.Count == 0) continue;

			foreach (DmmLevel nivel in dados.Levels)
			{
				int z = nivel.Z + off;
				for (int y = 0; y < nivel.Height && !saida.ContainsKey(z); y++)
					for (int x = 0; x < nivel.Width; x++)
					{
						string? k = nivel.Cells[x, y];
						if (k == null || !marcos.TryGetValue(k, out MarcoDoDmm m)) continue;
						saida[z] = m with { Cx = x, Cy = y };
						break;
					}
			}
		}
		return saida;
	}

	/// <summary>
	/// Os campos do marco na linha do manifesto -- CHATOS DE PROPOSITO (`berco_x`, `berco_y`,
	/// `berco_planeta`, `berco_raca`) e nao um objeto aninhado: o `ZoneCatalog.Parse` do Core acha cada
	/// zona por par de chaves `{`/`}`, e um objeto dentro do objeto fecharia a zona no lugar errado.
	/// </summary>
	internal static string CamposDeBerco(MarcoDoDmm? m) => m is { } b
		? $", \"berco_x\": {b.Cx}, \"berco_y\": {b.Cy}, \"berco_planeta\": \"{b.Planeta}\", \"berco_raca\": \"{b.Raca}\""
		: "";

	/// <summary>
	/// REESCREVE SO OS MARCOS num manifesto que ja existe -- o comando `bercos` do pipeline.
	///
	/// Existe pelo mesmo motivo dos comandos `agua`, `duro` e `nuvem`: reconverter os 40 andares pra
	/// acrescentar quatro campos numa linha reescreveria as cenas inteiras (e o indice de tiles resolve
	/// nome repetido por `TryAdd`, entao uma conversao cheia muda arte que ninguem pediu pra mudar). A
	/// conversao cheia (`maps`) TAMBEM escreve os campos, pela mesma funcao; este comando so os repoe.
	/// Devolve quantos andares ganharam marco.
	/// </summary>
	internal static int ReescreverBercosNoManifesto(string dmmDir, string manifesto)
	{
		Dictionary<int, MarcoDoDmm> bercos = ExtrairBercos(LerMapas(dmmDir));
		string[] linhas = File.ReadAllLines(manifesto);
		var rxZ = new Regex(@"""z"":\s*(\d+)");
		var rxBerco = new Regex(@",\s*""berco_x"":.*?""berco_raca"":\s*""[^""]*""");
		int escritos = 0;
		for (int i = 0; i < linhas.Length; i++)
		{
			Match mz = rxZ.Match(linhas[i]);
			if (!mz.Success) continue;
			string limpa = rxBerco.Replace(linhas[i], "");
			int fecha = limpa.LastIndexOf('}');
			if (fecha < 0) continue;
			int z = int.Parse(mz.Groups[1].Value, CultureInfo.InvariantCulture);
			string campos = CamposDeBerco(bercos.TryGetValue(z, out MarcoDoDmm b) ? b : null);
			linhas[i] = limpa[..fecha].TrimEnd() + campos + " }" + limpa[(fecha + 1)..];
			if (campos.Length > 0) escritos++;
		}
		File.WriteAllText(manifesto, string.Join("\n", linhas) + "\n", new UTF8Encoding(false));
		return escritos;
	}

	/// <summary>Le a ordem dos .dmm no .dme: e ela que define o z real de cada mapa.</summary>
	/// <summary>
	/// A ordem em que o `.dme` inclui os `.dmm` -- e ela DECIDE o z de cada nivel (ver a chamada).
	///
	/// `internal` e nao `private` porque a bancada `cidade` precisa da MESMA ordem pra achar, no
	/// `.dmm`, a celula que ela esta julgando no `.col`. Uma segunda copia desta leitura acertaria
	/// hoje e erraria no dia em que alguem acrescentasse um mapa no meio da lista -- e o sintoma
	/// seria a bancada julgando Arconia com as celulas do Inferno, calada.
	/// </summary>
	internal static List<string> OrdemDoDme(string dmmDir)
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

	/// <summary>O que a raiz da cena precisa declarar sobre si.</summary>
	private readonly record struct FichaDeCena(string Nome, double Gravidade, int Tipo);

	/// <summary>
	/// A ficha do planeta a partir do nome do ANDAR (`z03_Vegeta` -> `Vegeta`).
	///
	/// Os nomes de cena vem numerados por causa do `z` do BYOND; a tabela de gravidade e
	/// indexada pelo nome limpo. Casar os dois aqui evita que a numeracao vaze pro resto do jogo.
	/// </summary>
	private static FichaDeCena FichaDaZona(string nomeDaCena)
	{
		string limpo = nomeDaCena;
		int corte = limpo.IndexOf('_');
		if (corte > 0 && limpo.StartsWith('z')) limpo = limpo[(corte + 1)..];
		limpo = limpo.Replace('_', ' ');

		Jandirus.Core.World.FichaDePlaneta f = Planetas.De(limpo);
		return new FichaDeCena(limpo, f.Gravidade, IndiceDoTipo(f.Tipo));
	}

	/// <summary>
	/// O `TipoDePlaneta` e um enum do CLIENTE e o .tscn guarda enum como INTEIRO. A ordem tem
	/// que bater com a declaracao em `Client/Planeta.cs` -- se alguem reordenar la, Namek vira
	/// deserto e nada acusa. Por isso a lista esta escrita aqui inteira, e nao derivada.
	/// </summary>
	private static int IndiceDoTipo(string tipo) => tipo switch
	{
		"Jardim" => 1,
		"Deserto" => 2,
		"Gelado" => 3,
		"Vulcanico" => 4,
		"Morto" => 5,
		_ => 0,   // Rochoso
	};

	/// <summary>As fichas extraidas do DM. Quem preenche e o Program, antes de converter.</summary>
	private static Jandirus.Core.World.CatalogoDePlanetas Planetas =
		Jandirus.Core.World.CatalogoDePlanetas.Parse("[]");

	public static void UsarPlanetas(Jandirus.Core.World.CatalogoDePlanetas c) => Planetas = c;

	/// <summary>
	/// O CATALOGO DE CONSTRUCOES, pra reconhecer maquina no meio do mapa.
	///
	/// Vazio quando o `construcoes.json` nao existe -- e ai nada e extraido e tudo continua virando
	/// tile, que e o comportamento antigo. Um pipeline pela metade nao pode APAGAR objeto do mapa.
	/// </summary>
	private static Jandirus.Core.Tech.CatalogoDeObras Obras =
		Jandirus.Core.Tech.CatalogoDeObras.Parse("[]");

	public static void UsarObras(Jandirus.Core.Tech.CatalogoDeObras c) => Obras = c;

	/// <summary>
	/// ONDE FICA UMA COORDENADA BYOND, no mundo convertido.
	///
	/// Preenchido antes da segunda passada, porque uma passagem da Terra aponta pra um z que so vai
	/// ser convertido depois -- resolver na hora exigiria que a ordem dos mapas casasse com a ordem
	/// dos destinos, o que nao acontece nem por acaso.
	/// </summary>
	private static Func<int, int, int, (string Zona, float Px, float Py)?>? Destinos;

	internal static string NomeDoAndar(DmmMap.Result dados, DmmLevel nivel, int offset)
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
		string nome = sb.Length > 0 ? sb.ToString() : "Area";

		// AREA GENERICA CEDE LUGAR AO NOME DO MUNDO. Nove andares tem `/area/Outside` como area
		// dominante, e o catalogo guarda um por nome -- o Templo e as duas cavernas nunca chegavam
		// a existir. Ver `Passagens.NomeDoZ`.
		int z = nivel.Z + offset;
		if (Passagens.NomeGenerico(nome) && Passagens.NomeDoZ(z) is { } canonico) nome = canonico;

		return $"z{z:00}_{nome}";
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
			(int Tiles, int Animados) conta = BlocoDaFonte(f, ext, sub, res);
			totalTiles += conta.Tiles;
			totalAnimados += conta.Animados;
			passos += 2;   // a textura e o atlas
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
		Fichas fichas, Dictionary<string, Fonte> fontes,
		Dictionary<string, List<string>> atlasPorNome, HashSet<string> semAtlas, ref int proxId,
		out HashSet<(int, int)> paredes, out HashSet<(int, int)> cegos,
		out List<string> portasDaCena, out List<string> maquinasDaCena,
		out List<string> passagensDaCena, bool gravar = true, Trava? trava = null)
	{
		var passagens = new List<string>();
		int semDestino = 0;

		// local de verdade: um `out` nao pode ser capturado por funcao local
		var muros = new HashSet<(int, int)>();

		// O QUE CEGA e diferente do que BLOQUEIA, e por isso sao dois mapas.
		//
		// O `opacity` do BYOND nao serve aqui: este jogo praticamente nao usa (93 ocorrencias no
		// codigo inteiro, quase todas `mouse_opacity`), ou seja, no original dava pra ver o
		// planeta todo atraves das casas. Quem define a regra e o dono: "sombra de tiles como
		// paredes e arvores". Entao CEGA TUDO QUE E DENSO -- turf ou obj, casa, muro, porta,
		// arvore, cerca, pedra.
		//
		// Continua sendo um mapa SEPARADO do `.col` porque os dois divergem nos dois sentidos: a
		// porta cega e nao bloqueia (da pra atravessar), e a borda do mundo bloqueia sem cegar
		// nada de interessante.
		var vendados = new HashSet<(int, int)>();
		// ============================ AS CELULAS NAO VAO MAIS PRA DENTRO DA CENA ============================
		// Ate aqui cada camada saia como um `tile_map_data` no `.tscn`: 9,6 MB de texto so na Terra,
		// 659 ms de parse, e -- o pior -- 708 ms no PRIMEIRO QUADRO, porque o TileMapLayer monta o
		// desenho das 266 mil celulas de uma vez quando o renderizador as pede. Era a travada de
		// tres segundos ao trocar de mapa.
		//
		// Agora elas saem num `.pedacos` ao lado, agrupadas em blocos de 64x64, e o cliente monta
		// so os que a camera alcanca (ver `Core.World.PedacosDoMapa` e `Client.PintorDePedacos`).
		// Enquanto o dado estivesse na cena isso seria impossivel: o Godot o monta inteiro antes de
		// alguem poder escolher.
		// ====================================================================================================
		var bytes = new List<Jandirus.Core.World.CelulaDePedaco>();
		var decoracao = new List<Jandirus.Core.World.CelulaDePedaco>();
		var objetos = new List<Jandirus.Core.World.CelulaDePedaco>();

		// AS MAQUINAS DO MAPA saem da cena e viram uma lista ao lado -- mesmo caminho da porta.
		var interativos = new List<string>();

		// AS PORTAS SAEM DO MAPA COMO LISTA. Ate agora a porta era uma EXCECAO espalhada: o
		// `EhPorta` a tirava da colisao (aberta pra sempre), ela continuava cegando, e o guarda
		// do campo de visao apagava a tela em cima dela. Como entidade, ela nasce FECHADA
		// (bloqueia e cega, como no DM) e o estado passa a ser dela -- nenhum dos tres lugares
		// precisa mais saber que porta existe.
		var portas = new List<string>();

		int usadas = 0, comObj = 0, objSemArte = 0;

		// O QUE AS DUAS REGRAS DE EMPILHAMENTO FIZERAM NESTE ANDAR -- ver o laco das celulas.
		int underlaysPorCima = 0, arestasPorBaixo = 0, corposPorBaixo = 0, objetosSemCamada = 0;

		// E O QUE A FOLHA RESPONDEU TORTO: estado casado so ignorando a caixa, direcao que o estado nao tem.
		int soSemCaixa = 0;
		var semDirecao = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Maquina que o catalogo reconhece mas que nao conseguiu virar desenho.</summary>
		var maquinaSemArte = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Celulas que ALGUEM desenha: tile de qualquer camada, porta ou maquina.</summary>
		var desenhadas = new HashSet<(int, int)>();

		// ============================ QUAL "VAZIO" E BORDA E QUAL E COSTURA DO MAPEADOR ============================
		// `/turf/Other/Blank` e denso e sem icone, e a regra ate aqui era simples demais: TODO Blank
		// vira parede, porque "denso e sem icone e geometria deliberada". Isso e verdade no ANEL que
		// cerca o retangulo e no VAZIO em volta do Lookout -- e e falso no meio de um oceano.
		//
		// O dono achou o caso pelo lado de dentro: "tem uma PAREDE INVISIVEL no meio do mapa". Medido,
		// z11 (Hera) tem uma COLUNA INTEIRA de Blank em x=250 do `.dmm`, de y=1 a y=500, com
		// `/turf/Water/Water3` nas duas colunas vizinhas. E uma costura que o mapeador deixou ao
		// emendar dois pedacos de agua aberta -- 500 celulas solidas cortando o mapa ao meio, e sobre
		// agua azul lisa ela e literalmente invisivel.
		//
		// A DIFERENCA E TOPOLOGICA, e nao de posicao: o VAZIO tem MIOLO e a COSTURA nao. Uma regiao de
		// Blank que e limite de mundo sempre contem alguma celula cujos quatro vizinhos tambem sao
		// Blank (o anel tem espessura, o vazio do Lookout tem 11.536 celulas de miolo); uma linha de
		// um tile de largura nao tem nenhuma. Testar isso separa os quarenta mapas sem uma unica
		// excecao escrita a mao:
		//
		//     z11 Hera        500 celulas, miolo 0  -> costura   (a queixa do dono)
		//     z13 Sala        500 celulas, miolo 0  -> costura   (o anel da coluna 499)
		//     z05 Arconia       5 celulas, miolo 0  -> costura   (celulas soltas)
		//     z10 Heaven        3 celulas, miolo 0  -> costura
		//     z05 Arconia     313 celulas, miolo 83 -> BORDA     (a mordida no leste do mapa)
		//     z12 Lookout   14.315 celulas, miolo 11.536 -> BORDA (o ceu em volta da plataforma)
		//     z15..z18/z22/z23  mapa inteiro        -> BORDA     (as cavernas e o `Outside`)
		//
		// NAO DA PRA CONSERTAR ISSO DEIXANDO DE BLOQUEAR TUDO que e denso e invisivel: seria devolver
		// o bug que este trecho conserta, com quase dois milhoes de celulas de borda de mundo abertas.
		// ==========================================================================================
		var mudas = new HashSet<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;
				foreach (string tp in tipos)
					if (fichas.De(tp) is { Icon: null, Density: true })
					{ mudas.Add((x, y)); break; }
			}

		var costuras = new HashSet<(int, int)>();
		if (mudas.Count > 0)
		{
			var visto = new HashSet<(int, int)>();
			var fila = new Queue<(int, int)>();
			var comp = new List<(int, int)>();
			foreach ((int, int) semente in mudas)
			{
				if (!visto.Add(semente)) continue;
				comp.Clear();
				fila.Enqueue(semente);
				bool temMiolo = false;
				while (fila.Count > 0)
				{
					(int cx, int cy) = fila.Dequeue();
					comp.Add((cx, cy));
					int vizinhos = 0;
					foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					{
						(int, int) n = (cx + dx, cy + dy);
						if (!mudas.Contains(n)) continue;
						vizinhos++;
						if (visto.Add(n)) fila.Enqueue(n);
					}
					if (vizinhos == 4) temMiolo = true;
				}
				if (!temMiolo) foreach ((int, int) c in comp) costuras.Add(c);
			}
		}

		// QUANTAS CELULAS APONTAM PRA UMA TIRA. Sem este numero, "178 estados reempacotados" parece
		// prova de que a animacao chegou ao mapa -- e nao e: o reempacotamento escreve o atlas e
		// declara o tile, mas quem faz a animacao APARECER e a celula apontar pra la. Foram duas
		// coisas separadas quebrando em silencio (o PNG sem `.import` e o estado padrao com guarda
		// de nulo), e nenhuma das duas aparecia em contador nenhum.
		int repontadas = 0, repontadasPadrao = 0;
		var semEstado = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Celulas cujo estado TEM mais de um quadro no .dmi mas foi pintado parado.</summary>
		var semAnimacao = new Dictionary<string, int>(StringComparer.Ordinal);
		var luzes = new List<LuzDeTile>();

		/// <summary>Turfs densos que ficaram POR BAIXO de outro turf e por isso nao tem fisica: quantos de cada.</summary>
		var subsolo = new Dictionary<string, int>(StringComparer.Ordinal);

		// Poe UM typepath numa camada. Devolve se conseguiu desenhar.
		//
		// `cega` diz se ESTA camada corta a linha de visao. Ver o comentario da chamada dos
		// objetos: cenario solto (arvore, pedra, poste) PARA, mas nao ESCONDE.
		//
		// ============================ `fisica`: SO O ULTIMO TURF DA CELULA TEM CORPO ============================
		// Uma celula do `.dmm` pode listar dois turfs -- `(/turf/decor/Table4, /turf/Tile/Tile5)` no
		// castelo de Vegeta, `(/turf/decor/SnowBush, /turf/Grass/Grass23)` na neve. No BYOND so o
		// ULTIMO existe: cada `new /turf` substitui o anterior, e o que sobra do primeiro e so um
		// desenho (qual dos dois fica por cima e assunto do laco das celulas). Densidade, opacidade
		// e `Enter()`: tudo do ultimo.
		//
		// A FISICA nao fazia isso: as duas chamadas de `Por` somavam `muros` e `vendados`, e a mesa
		// densa escondida embaixo do piso virava uma parede invisivel. Foi o que o dono viu no
		// castelo: "o icone nao aparece, so a hitbox, em varios locais do mapa". O `.agua`, o `.duro`
		// e o `.nuvem` sempre perguntaram pelo ultimo turf (ver `CelulasDeAgua`); este era o unico
		// dos cinco mapas de celula que perguntava pelos dois.
		// ========================================================================================================
		//
		// `acende` diz se a LUZ deste typepath vale, e a luz e de quem APARECE: o turf de baixo nao
		// acende escondido sob o de cima, e acende quando e desenhado por cima dele. Sem dizer nada, ela
		// acompanha a `fisica` (o turf de verdade da celula e os objetos acendem).
		//
		// `tp` E O TYPEPATH COMO O MAPA O ESCREVE, com as variaveis da instancia: a ficha sai dele (ver
		// `Fichas`), e as regras que so olham o tipo usam o `bp`.
		bool Por(string? tp, List<Jandirus.Core.World.CelulaDePedaco> destino, int x, int y, bool cega = true, bool fisica = true,
				 bool? acende = null)
		{
			if (tp == null) return false;
			if (fichas.De(tp) is not { } td) return false;
			string bp = td.Path;
			if (!fisica && td.Density) subsolo[bp] = subsolo.GetValueOrDefault(bp) + 1;

			// BORDA DO MUNDO. `/turf/Other/Blank` e denso, opaco e NAO TEM ICONE -- e o limite
			// do mapa, invisivel de proposito. A regra "so bloqueia o que da pra ver" (certa
			// pra arvore sem sprite) tirava a parede do vazio: quase 2 MILHOES de celulas, e
			// andares inteiros ficaram sem borda. Denso E SEM ICONE e geometria deliberada, nao
			// arte que faltou.
			// ...MENOS quando este vazio e uma COSTURA e nao o limite do mundo. Ver o levantamento
			// de topologia la em cima: e a parede invisivel que o dono atravessou o mapa pra achar.
			if (td.Icon == null)
			{
				if (fisica && td.Density && !costuras.Contains((x, y)))
				{
					muros.Add((x, y));
					if (cega) vendados.Add((x, y));
				}
				return false;
			}
			if (td.Atlas == null || !fontes.TryGetValue(td.Atlas, out Fonte? f)) return false;

			// ============================ NULO E O ESTADO PADRAO, NAO "SEM ESTADO" ============================
			// O `IconState` chega NULO quando o DM nunca declarou `icon_state` -- e no BYOND isso quer
			// dizer o estado de nome VAZIO da folha, que existe e tem nome `""`. Sao coisas diferentes
			// no C# e a MESMA coisa no jogo.
			//
			// A confusao entre as duas custou TODA animacao cujo estado animado e o padrao. O
			// repontamento pra tira reempacotada tinha uma guarda `estado != null`, entao a celula
			// continuava apontando pro atlas ORIGINAL (quadro parado) enquanto a tira animada ficava no
			// tileset sem ninguem usando. Foi o que o dono viu: "a research table ainda ta sem animaçao
			// nenhuma e so alguns icones q estao" -- a ResearchBench tem 5 quadros no estado padrao.
			//
			// Normalizar aqui, uma vez, e o que faz o resto do metodo parar de ter que lembrar disso.
			// ================================================================================================
			string estado = EstadoDaCelula(td, x, y, nivel.Height) ?? "";

			// A PORTA FECHADA E O ESTADO "Closed". O DM so o escreve dentro do `New()`, e o
			// DmTurfScanner ignora corpo de proc de proposito -- entao `IconState` vinha nulo e o
			// `Coord` caia no indice 0, que e o estado VAZIO da folha (um desenho diferente).
			// Como o indice 0 e o fallback silencioso, nem o relatorio de "estado que faltou"
			// pegava este caso.
			if (EhPorta(bp) && estado.Length == 0) estado = "Closed";
			EstadoDaFolha? achado = f.Achar(estado);
			if (achado == null)
			{
				string chave = $"{bp} -> {td.Icon} estado \"{estado}\"";
				semEstado[chave] = semEstado.GetValueOrDefault(chave) + 1;
			}
			else if (achado.Value.Nome != estado) soSemCaixa++;

			// ============================ A DIRECAO ESCOLHE O QUADRO ============================
			// Um estado direcional guarda um desenho por direcao, na ordem sul, norte, leste, oeste (ver
			// `Direcoes`), e o `dir` do tipo diz qual deles e o da celula. O conversor pintava sempre o
			// primeiro: as 24 arestas de penhasco (`barrier.dm:92-211`) sao 6 estados de 4 direcoes, e
			// todas saiam viradas pro sul -- o risco da borda ficava do lado errado do tile, que e como
			// "sprite cortado" aparece na tela. O tileset ja declarava um tile por direcao.
			// ====================================================================================
			int dirNaFolha = achado is { } e0 ? Direcoes.NaFolha(td.Dir, e0.Dirs) : 0;
			if (dirNaFolha < 0)
			{
				string chave = $"{bp} dir={td.Dir} num estado de {achado!.Value.Dirs} direcoes (\"{estado}\")";
				semDirecao[chave] = semDirecao.GetValueOrDefault(chave) + 1;
				dirNaFolha = 0;
			}

			(int X, int Y) c = achado is { } e1 ? Indice(f, e1.Indice + dirNaFolha) : (0, 0);

			// ESTADO REEMPACOTADO MORA NOUTRA FONTE. A celula tem que apontar pra tira, senao ela
			// continua pintando o quadro parado do atlas original enquanto o tile animado que o
			// tileset declarou fica sem ninguem usando. (A tira so guarda a direcao sul -- ver
			// `PlanejarTiras`.)
			Fonte fUsada = f;
			int linha = -1;
			bool planejada = dirNaFolha == 0 && achado is { } e2 && f.Refeitos.TryGetValue(e2.Nome, out linha);
			bool naTira = planejada && f.Companheira != null;
			if (naTira)
			{
				fUsada = f.Companheira!;
				c = (0, linha);
				repontadas++;
				// as do estado PADRAO sao exatamente as que a guarda de nulo comia
				if (estado.Length == 0) repontadasPadrao++;
			}

			// ============================ ANIMACAO PROMETIDA E NAO ENTREGUE ============================
			// O .dmi diz quantos quadros o estado tem. Se ele tem mais de um e a celula NAO acabou numa
			// tira, ela depende de o atlas original ter conseguido declarar o tile animado -- e isso so
			// acontece quando os quadros cabem na MESMA LINHA (ver `Reempacotar`). Fora disso, a celula
			// e pintada parada e ninguem avisa.
			//
			// Este relatorio existe porque "178 estados reempacotados" nao prova nada sobre o que aparece
			// na tela: ja quebrou por PNG sem `.import` e por guarda de nulo no estado padrao, e nenhuma
			// das duas aparecia em contador nenhum. Aqui a pergunta e a que importa -- ESTA CELULA ANIMA?
			// =========================================================================================
			bool animaNoOriginal = achado is { Quadros: > 1 } e3 && CabeNaLinha(f, e3, dirNaFolha);
			if (!naTira && achado is { Quadros: > 1 } e4 && !animaNoOriginal)
			{
				string k = $"{bp} -> {td.Icon} \"{estado}\" ({e4.Quadros} quadros)";
				semAnimacao[k] = semAnimacao.GetValueOrDefault(k) + 1;
			}

			// A PORTA NAO E PINTADA NO TILEMAP -- ela vira um NODE, e o node desenha os quatro
			// estados dela.
			//
			// POR QUE NAO DA PRA DESENHAR O NODE POR CIMA DO TILE: o quadro "Open" do BYOND e um VAO,
			// quase todo transparente (medido na Door1: 64 pixels opacos de 1024). Com o tile fechado
			// embaixo, abrir a porta mostraria a porta fechada atraves do buraco.
			//
			// E POR QUE NAO APAGAR A CELULA EM RUNTIME: a cena da zona fica CACHEADA entre visitas
			// (ver `World.GuardarZonaAtual`). Apagar e repor celula nela e mutar um objeto que
			// sobrevive a saida do planeta -- sair com a porta aberta deixaria o buraco pra sempre.
			//
			// O que fica embaixo e o mesmo que ficava no BYOND: o turf de chao, se o prefab tinha um,
			// e o vazio se nao tinha (la o turf E a camada de baixo, entao porta aberta ja mostrava
			// o escuro).
			// POR BAIXO DE OUTRO TURF NAO HA PORTA NEM MAQUINA: a `Door4` que o `.dmm` empilha debaixo do
			// teleportador do castelo de Vegeta e so o desenho de uma porta. Vira tile, como qualquer
			// underlay -- e nao um node que abre pro nada.
			bool porta = fisica && EhPorta(bp);

			// ============================ MAQUINA NAO E CENARIO ============================
			// Banco, bancada de pesquisa, sala de gravidade, regenerador, os laboratorios: no
			// original todos tem VERBOS (`set src in oview(1)`), ou seja, sao coisas com que se
			// INTERAGE. Aqui eles viravam celula de tilemap -- pintura no chao, sem estado e sem
			// resposta -- ao lado de uma copia construida por jogador, que e um node e funciona.
			// Duas coisas iguais na tela, uma viva e outra nao.
			//
			// Agora saem do tilemap pelo MESMO caminho da porta: uma lista ao lado da cena, e o
			// servidor as registra como construcoes do mapa (ver `.objetos` e
			// `GameServer.CarregarObjetosDoMapa`). O que as reconhece e o `create_type` do
			// catalogo de tecnologia -- a mesma chave que ja diz qual arte e qual densidade cada
			// uma tem, entao nao ha tabela nova pra manter em sincronia.
			//
			// A COLISAO CONTINUA NO `.col`, como a da porta: a maquina nao anda, e o bit ja esta
			// calculado. O que muda e so quem DESENHA.
			Jandirus.Core.Tech.Construcao? maquina = (porta || !fisica) ? null : Obras.PorTypepath(bp);

			if (porta)
			{
				// o .tres da folha e o mesmo caminho do .png, com outra extensao. Sai o da fonte
				// ORIGINAL de proposito: o companheiro de animacao (ver AtlasAnimado) so existe pro
				// tileset, e o node quer o SpriteFrames com os estados nomeados.
				string tres = Path.ChangeExtension(f.ResPath, ".tres");
				portas.Add($"{{ \"x\": {x}, \"y\": {y}, \"arte\": \"{tres}\" }}");
			}
			else if (maquina != null)
			{
				interativos.Add($"{{ \"x\": {x}, \"y\": {y}, \"id\": \"{maquina.Id}\" }}");
			}
			else
			{
				// ============================ PRESO: A CELULA SO APONTA PRO QUE O DISCO DECLARA ============================
				// O tile tem que existir no `tileset.tres` do disco E animar o numero de quadros que a
				// folha de hoje pede: uma folha que ganhou um quadro desde o tileset tocaria a animacao
				// antiga, e uma tira planejada que o disco nao tem deixaria a celula parada. Nos dois
				// casos o certo e a conversao cheia, e nao um `.pedacos` novo apontando pro velho. (No
				// estado que a folha nao tem a celula cai no quadro 0, e ai so se cobra que ele exista.)
				// ==========================================================================================================
				if (trava != null)
				{
					int declarados = trava.QuadrosDeclarados(fUsada.Id, c);
					int esperados = naTira ? f.Duracoes[linha].Length
						: animaNoOriginal ? achado!.Value.Quadros
						: achado == null ? declarados
						: 1;
					if (planejada && !naTira)
						trava.Faltar($"{bp}: o estado \"{estado}\" de {f.ResPath} precisa da tira de animacao (linha {linha}) "
									 + "e o tileset do disco nao tem a companheira dela");
					else if (declarados == 0)
						trava.Faltar($"{bp}: o tile ({c.X},{c.Y}) de {fUsada.ResPath} (fonte {fUsada.Id}, estado \"{estado}\") "
									 + "nao esta declarado no tileset do disco");
					else if (declarados != esperados)
						trava.Faltar($"{bp}: o tile ({c.X},{c.Y}) de {fUsada.ResPath} (estado \"{estado}\") anima {declarados} "
									 + $"quadro(s) no tileset do disco e a folha de hoje pede {esperados}");
				}

				// A ALTERNATIVA DO TILE NAO E GRAVADA porque ela sempre foi 0 aqui -- o formato do
				// `.pedacos` a deixa de fora e economiza dois bytes por celula.
				destino.Add(new Jandirus.Core.World.CelulaDePedaco(
					(short)x, (short)y, (ushort)fUsada.Id, (ushort)c.X, (ushort)c.Y));
				usadas++;
			}

			// ESTA CELULA TEM DONO NA TELA -- tile, porta ou maquina. E o outro lado da conta do
			// relatorio de parede fantasma la embaixo: solido sem ninguem que o desenhe e defeito.
			desenhadas.Add((x, y));

			// SO BLOQUEIA O QUE FOI DESENHADO -- e a porta e a excecao declarada, senao a casa
			// fica lacrada com um desenho de porta na frente.
			// ============================ BARREIRA BLOQUEIA SEM SER DENSA ============================
			// `/obj/barrier/*` para o corpo no BYOND por `NOENTER`/`selectivecollide`, e o comentario do
			// proprio jogo diz isso com todas as letras: "Barriers block via selectivecollide/NOENTER
			// (NOT native density)" (barrier.dm:61-62). O pipeline le so `density`, entao as cercas e
			// bordas do original viraram cenario atravessavel.
			//
			// Medido: 8.765 celulas nos 4 mapas -- 1.683 so na Terra. E o maior buraco de colisao do
			// port, e nao aparecia em lugar nenhum porque cada uma parece uma cerca decorativa.
			//
			// APROXIMACAO DECLARADA: o `NOENTER` do original e POR DIRECAO (`list(SOUTH, SOUTHWEST...)`)
			// e o `.col` e 1 bit por celula, sem direcao. Bloquear inteiro erra pro lado seguro -- uma
			// borda de mapa que so barrava por um lado agora barra pelos dois. O `kaio_gate` fica de
			// fora: ele e condicional (`kaiTrainingAllowed`) e virar parede fixa trancaria o treino.
			bool barreira = bp.StartsWith("/obj/barrier/", StringComparison.Ordinal)
							&& !bp.Contains("kaio_gate", StringComparison.OrdinalIgnoreCase);

			// A PORTA BLOQUEIA E CEGA, como no original: `density = 1` e `opacity = 1` enquanto
			// fechada (Doors.dm:56-65). Quem a abre e o servidor, em runtime -- ver GameServer.Portas.
			// A PASSAGEM SAI DA COLISAO pelo mesmo motivo da porta: no DM ela e densa, mas o
			// `Enter()` teleporta ANTES de o bloqueio valer. Marcada como parede, ela vira uma
			// escada que ninguem sobe. Ver `MarcarSolidos`.
			if (fisica && (td.Density || barreira) && !Passagens.Eh(bp)) muros.Add((x, y));

			// ...e a porta CEGA mesmo sem bloquear: ela esta fechada no desenho.
			// ============================ O QUE CEGA NAO E O QUE BLOQUEIA ============================
			// O dono fotografou uma PEDRA projetando leque preto de muro. A pedra e
			// `/turf/decor/LargeRock` (`Turfs.dm:1662`): densa -- voce nao passa por cima -- e
			// obviamente nao esconde nada, porque ela bate na canela.
			//
			// TENTEI `opacity` PRIMEIRO, e estava errado. O BYOND tem o campo certo (`opacity = 1`
			// e literalmente "bloqueia visao") e o `DmTurfScanner` ja o extraia. So que ESTE codigo
			// nao o mantem: sao 397 declaracoes de `density` contra 45 de `opacity` no jogo inteiro.
			// Trocar um pelo outro derrubou as celulas que cegam de 5.956 pra 174 -- ou seja, teria
			// deixado os MUROS transparentes. O campo certo existe e o dado nao esta la.
			//
			// O que o dado TEM e a arvore de tipos, e nela `turf/decor` e exatamente a familia do
			// cenario solto (Rock, LargeRock, firewood). E a MESMA regra que ja vale pros `/obj`
			// oito linhas abaixo -- "cenario solto PARA, mas nao ESCONDE" --, agora aplicada a um
			// ramo de turf que tinha escapado dela.
			// =====================================================================================
			bool decor = bp.StartsWith("/turf/decor", StringComparison.Ordinal);
			if (fisica && td.Density && cega && !decor) vendados.Add((x, y));

			// FONTE DE LUZ. Fogueira, tocha, lampada e lava acendem o cenario -- ver LightCatalog.
			if ((acende ?? fisica) && LightCatalog.Da(bp) is { } luz)
				luzes.Add(new LuzDeTile(x, y, luz.Raio, luz.Cor, luz.Forca, luz.Tremula));

			return true;
		}

		// O que a escolha da camada de objetos precisa saber de um `/obj` solto -- ver `DoisDaCelula`.
		// "Tem arte" e a mesma pergunta que o `Por` faz antes de pintar.
		Solto FichaDoSolto(string tp)
		{
			TurfDef? td = fichas.De(tp);
			Fonte? f = td is { Icon: not null, Atlas: { } atlas } ? fontes.GetValueOrDefault(atlas) : null;
			return new Solto(f != null, f is { IconW: Cell, IconH: Cell }, EhAresta(DmmMap.BasePath(tp)), td?.Camada ?? 3);
		}

		// os `/obj` soltos da celula da vez, na ordem do mapa e sem repeticao (a lista e uma so, reusada)
		var soltos = new List<string>();

		// ...e os turfs dela, pra pilha da celula que nao cabe em tres desenhos (ver `PilhaDaCelula`). O
		// mesmo turf posto duas vezes vale a ULTIMA vez: o de baixo fica inteiro debaixo do de cima.
		var turfsDaCelula = new List<string>();
		var desenhos = new List<Desenho>();

		// AS CAMADAS CHATAS ALEM DA DECORACAO (`Decor2`, `Decor3`...), criadas quando a primeira celula
		// do andar as pede. Andar que nao empilha continua com as tres de sempre, no arquivo e na cena.
		var maisDecoracao = new List<List<Jandirus.Core.World.CelulaDePedaco>>();
		int celulasEmPilha = 0;

		// O turf, pra pilha: tem desenho (ou e porta/maquina, que desenha fora do tilemap)?
		bool TurfDesenha(string tp) =>
			fichas.De(tp) is { Icon: not null, Atlas: { } atlas } && fontes.ContainsKey(atlas);

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
				//
				// Todos guardam o typepath COMO O MAPA O ESCREVE (com as variaveis da instancia): e
				// dele que sai a ficha de quem troca icone, e o destino de quem teleporta.
				string? fundo = null, topo = null, maquina = null;
				bool tinhaObj = false;
				soltos.Clear();
				turfsDaCelula.Clear();

				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);

					// ============================ AS PASSAGENS SAEM COMO LISTA ============================
					// Uma celula que TELEPORTA pra outro mapa nao e cenario nem porta: ela nao abre, nao
					// bloqueia, e o que importa dela e o DESTINO -- que nao cabe num tile.
					//
					// O typepath COMPLETO vai pro extrator, e nao o `bp`: metade das passagens guarda o
					// destino nas propriedades da instancia (`{gotox = 354; gotoy = 2; gotoz = 12}`), e
					// o `BasePath` corta exatamente essa parte. Ver `Passagens`.
					//
					// A CELULA CONTINUA SENDO DESENHADA. No original a boca da caverna e um desenho como
					// outro qualquer, e apagar o tile deixaria um buraco no chao onde havia uma entrada.
					// ...E SO O ULTIMO TURF PODE SER UMA. Um teleportador por baixo de outro turf foi
					// substituido no BYOND como qualquer underlay; extrair a passagem dele lacraria (e
					// teleportaria) uma celula que no original e chao comum. Os `/obj` nunca sao underlay.
					if (bp.StartsWith("/turf", StringComparison.Ordinal))
					{
						fundo ??= tp;
						topo = tp;                        // sempre o ultimo visto
						turfsDaCelula.Remove(tp);
						turfsDaCelula.Add(tp);
					}
					else if (Passagens.De(tp) is { } destObj && Destinos != null)
					{
						if (Destinos(destObj.X, destObj.Y, destObj.Z) is { } onde)
							passagens.Add(LinhaDePassagem(x, y, onde, destObj));
						else semDestino++;
					}
					else if (bp.StartsWith("/obj", StringComparison.Ordinal) && Desenhavel(bp))
					{
						tinhaObj = true;

						// ============================ A MAQUINA E A MOBILIA NAO DISPUTAM A CELULA ============================
						// Antes havia um `objeto` so, preenchido pelo PRIMEIRO `/obj` da lista, e isso
						// engoliu o unico banco de Vegeta. A celula (123,286) do `.dmm` e
						// `['/obj/buildables/chair', '/obj/Bank', '/turf/Tile/Tile5', ...]`: a cadeira vem
						// primeiro, travava a variavel, e `Obras.PorTypepath` nunca era chamado com
						// `/obj/Bank`. O banco nao ficou invisivel -- ele nunca chegou ao `.objetos`, e o
						// bit de densidade dele nunca foi assado. Quatro tiles do ponto onde o jogador
						// nasce em Vegeta, e a UNICA maquina perdida dos 26 andares.
						//
						// Elas nao competem porque nao vao pro mesmo lugar: a maquina sai da cena e vira
						// construcao (`interativos`), a mobilia continua sendo celula de tilemap. Cabem
						// as duas na mesma celula, como cabiam no original.
						//
						// SO ESTA CELULA EMPILHA MOVEL EM CIMA DE MAQUINA hoje: das 1.823 celulas com
						// dois ou mais `/obj` nos 26 mapas, 1.797 sao pares de `/obj/barrier/Edges`.
						//
						// OS OUTROS SO SE JUNTAM AQUI. A camada de objetos guarda um tile por celula, e
						// quem fica com ele (e quem desce pra decoracao) se decide com a lista inteira na
						// mao -- ver `DoisDaCelula`. O mesmo objeto posto duas vezes entra uma vez so.
						if (Obras.PorTypepath(bp) != null) maquina ??= tp;
						else if (!soltos.Contains(tp)) soltos.Add(tp);
					}
				}

				if (topo != null && Passagens.De(topo) is { } dest && Destinos != null)
				{
					if (Destinos(dest.X, dest.Y, dest.Z) is { } onde) passagens.Add(LinhaDePassagem(x, y, onde, dest));
					else semDestino++;
				}

				// uma celula com um turf so nao precisa de decoracao; com dois ou mais, o
				// primeiro e o chao e o ULTIMO vai por cima
				//
				// ============================ ...MENOS QUANDO O DE BAIXO TEM A CAMADA MAIOR ============================
				// Os turfs que o `.dmm` empilha viram, no BYOND, UM turf (o ultimo) com os outros de
				// underlay ("When multiple turfs are stacked ... there is actually only one turf (the
				// topmost) and the rest are all underlays", `underlays var (atom)` da referencia; o
				// carregador de mapa do proprio jogo faz igual, `lib/iainperegrine.dmm_suite/reader.dm:
				// 136-147`). E underlay que e retrato de outro objeto desenha na camada DELE, nao embaixo
				// do dono ("the drawing layer of that object is used", `overlays var (atom)`).
				//
				// `/turf/decor` declara `layer=4` (`Turfs.dm:1657`), o dobro de um turf comum. Entao em
				// `(/turf/decor/Table4, /turf/Tile/Tile5)` o piso e o turf de verdade e a mesa aparece POR
				// CIMA dele -- e aqui ela ia pro chao e o piso a cobria: 7 mesas do castelo de Vegeta, 12
				// plantas e uma ponta de ponte sumiam debaixo do proprio chao (20 celulas no planeta).
				//
				// SO O DESENHO TROCA DE CAMADA. O corpo continua sendo o do ULTIMO turf: a mesa por cima
				// do piso aparece e nao bloqueia, que e o que ela faz no original.
				//
				// ...E A LUZ VAI COM O DESENHO. O DM nao tem luz nenhuma (`TurfOnNew.dm:1`); aqui a tocha
				// acende porque se ve a chama. Num templo de Namek ha duas `/turf/decor/Torch3` listadas
				// antes do piso: a chama aparece por cima dele, entao a luz dela vale.
				// =======================================================================================================
				// QUEM FICA COM A CAMADA DE OBJETOS, E QUEM DESCE. Uma celula com um objeto so -- quase
				// todas -- nao tem o que escolher.
				string? objeto = soltos.Count == 1 ? soltos[0] : null, embaixo = null;
				int semLugar = 0;
				if (soltos.Count > 1) (objeto, embaixo, semLugar) = DoisDaCelula(soltos, FichaDoSolto);

				// ============================ A CELULA QUE NAO CABE EM TRES DESENHOS ============================
				// Tres turfs empilhados, tres arestas, a mesa por cima da faca -- ver `PrecisaDePilha`. A
				// lista so e montada pra quem pode precisar: tres coisas na celula, ou um objeto em cima de
				// um turf de `layer` alto (a cachoeira, `MOB_LAYER+1`, cobre a aresta que o mapa pos nela).
				bool emPilha = false, objetoChato = false;
				if (turfsDaCelula.Count + soltos.Count > 2 || (soltos.Count > 0 && topo != null && fichas.De(topo)?.Camada > 3))
				{
					desenhos.Clear();
					foreach (string t in turfsDaCelula)
						if (TurfDesenha(t)) desenhos.Add(new Desenho(t, Turf: true, Real: t == topo, CabeNumTile: true, fichas.De(t)!.Camada));
					foreach (string o in soltos)
						if (FichaDoSolto(o) is { TemArte: true } s) desenhos.Add(new Desenho(o, Turf: false, Real: false, s.CabeNumTile, s.Camada));
					emPilha = PrecisaDePilha(desenhos);
				}

				bool decorOcupado = false;
				if (emPilha)
				{
					// DE BAIXO PRA CIMA: chao, decoracao e, dali em diante, uma camada a mais por desenho. O
					// turf de verdade pinta com o corpo dele; o underlay e so desenho, e acende quando aparece
					// por cima do turf de verdade (a regra da tocha, no bloco de cima); o objeto para e nao cega.
					(List<Desenho> chatos, Desenho? noObjetos) = PilhaDaCelula(desenhos);
					double camadaDoReal = topo == null ? 0 : fichas.De(topo)?.Camada ?? 0;
					int vaga = 0;
					foreach (Desenho d in chatos)
					{
						while (maisDecoracao.Count < vaga - 1) maisDecoracao.Add([]);
						List<Jandirus.Core.World.CelulaDePedaco> destino = vaga == 0 ? bytes : vaga == 1 ? decoracao : maisDecoracao[vaga - 2];
						bool pintou = !d.Turf ? Por(d.Tipo, destino, x, y, cega: false)
							: d.Real ? Por(d.Tipo, destino, x, y)
							: Por(d.Tipo, destino, x, y, fisica: false, acende: d.Camada > camadaDoReal);
						if (!pintou) continue;
						vaga++;
						objetoChato |= !d.Turf;
					}
					// o turf de verdade que nao desenha (o vazio denso) ainda tem corpo
					if (topo != null && !desenhos.Exists(d => d.Real)) Por(topo, bytes, x, y);
					objeto = noObjetos?.Tipo;
					embaixo = null;
					celulasEmPilha++;
				}
				else
				{
					objetosSemCamada += semLugar;

					bool empilhado = topo != null && DmmMap.BasePath(fundo!) != DmmMap.BasePath(topo);
					bool underlayPorCima = empilhado && fichas.De(fundo!) is { } deBaixo && fichas.De(topo!) is { } deCima
										   && deBaixo.Camada > deCima.Camada;
					if (underlayPorCima)
					{
						Por(topo, bytes, x, y);
						decorOcupado = Por(fundo, decoracao, x, y, fisica: false, acende: true);
						if (decorOcupado) underlaysPorCima++;
					}
					else
					{
						// sem empilhar, o turf da celula e o ULTIMO da lista (que e o unico, ou o mesmo tipo repetido)
						Por(empilhado ? fundo : topo, bytes, x, y, fisica: !empilhado);   // por baixo de outro turf, e so desenho
						decorOcupado = empilhado && Por(topo, decoracao, x, y);
					}
				}

				// O DE BAIXO, na decoracao: abaixo dos atores, em vez de ordenar por Y com eles (as
				// divergencias estao em `DoisDaCelula`). Com a decoracao ocupada por outro turf ele
				// continua sem lugar -- uma quarta camada pediria cena e cliente novos.
				if (embaixo != null)
				{
					if (decorOcupado || !Por(embaixo, decoracao, x, y, cega: false)) objetosSemCamada++;
					else if (EhAresta(DmmMap.BasePath(embaixo))) arestasPorBaixo++;
					else corposPorBaixo++;
				}

				// A CAMADA DE OBJETOS NAO CEGA -- e o que tira a sombra das arvores.
				//
				// A sombra em si estava CERTA (geometria de bloco projetada a partir dos pes), mas
				// ficava esquisita porque a premissa era errada: uma arvore nao e um muro. Ela tem
				// copa, tronco e vaos, e a sombra dela num campo aberto virava um leque preto de
				// borda reta saindo de cada tronco -- muitos leques, sem nada os unindo, num terreno
				// que deveria ser aberto. Muro projeta sombra porque muro E um plano opaco;
				// vegetacao nao e.
				//
				// A ARVORE CONTINUA PARANDO O CORPO: `muros` (fisica) segue recebendo a celula; so
				// `vendados` (visao) deixa de receber. Bloquear passagem e bloquear visao viraram
				// duas perguntas separadas, que e o que o DM fazia com `density` e `opacity`.
				// A MAQUINA PRIMEIRO, e por um caminho separado: ela sai da cena pela lista
				// `interativos` e nao disputa o tile com a mobilia. Ver o comentario do
				// `Obras.PorTypepath` vinte linhas acima.
				bool posMaq = Por(maquina, objetos, x, y, cega: false);
				if (maquina != null && !posMaq)
				{
					string tipoDaMaquina = DmmMap.BasePath(maquina);
					maquinaSemArte[tipoDaMaquina] = maquinaSemArte.GetValueOrDefault(tipoDaMaquina) + 1;
				}

				bool posObj = Por(objeto, objetos, x, y, cega: false) || objetoChato;
				if (posObj || posMaq) comObj++;
				if (tinhaObj && !posObj && !posMaq) objSemArte++;
			}

		// ============================ A COSTURA TAMBEM PRECISA FECHAR NO DESENHO ============================
		// Tirar o bloqueio resolve metade: a celula de vazio continua sem tile, e no meio de um oceano
		// azul liso ela vira uma FRESTA PRETA de um tile cortando o mapa de cima a baixo. Trocar
		// "parede invisivel" por "risco preto" e trocar um defeito por outro -- e o segundo o dono ve
		// de longe.
		//
		// A COSTURA PEGA O CHAO DO VIZINHO, e nao um tile escolhido a dedo: ela existe porque o
		// mapeador emendou dois pedacos do MESMO terreno, entao o desenho certo dela e, literalmente,
		// o do lado. Oeste primeiro e depois leste/norte/sul, sempre na mesma ordem -- a saida e a
		// mesma toda vez que o mesmo `.dmm` entra.
		//
		// ISTO NAO INVENTA ARTE. O tile copiado ja esta no mapa, a uma celula de distancia; nada de
		// novo entra no tileset. Onde nao ha vizinho com chao (o anel na beirada do retangulo, onde
		// do lado de fora nao ha nada), a celula continua vazia -- que e o certo, porque ali o mapa
		// realmente acaba.
		if (costuras.Count > 0)
		{
			var chao = new Dictionary<(int, int), Jandirus.Core.World.CelulaDePedaco>();
			foreach (Jandirus.Core.World.CelulaDePedaco c in bytes) chao[(c.X, c.Y)] = c;

			int remendadas = 0;
			foreach ((int x, int y) in costuras.OrderBy(c => c.Item2).ThenBy(c => c.Item1))
			{
				if (chao.ContainsKey((x, y))) continue;
				foreach ((int dx, int dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
				{
					if (!chao.TryGetValue((x + dx, y + dy), out Jandirus.Core.World.CelulaDePedaco v)) continue;
					bytes.Add(v with { X = (short)x, Y = (short)y });
					usadas++;
					remendadas++;
					break;
				}
			}
			if (remendadas > 0)
				Console.WriteLine($"  {nome}: {remendadas} celula(s) de costura remendadas com o chao do vizinho");
		}

		var sb = new StringBuilder();
		sb.Append("[gd_scene load_steps=3 format=3]\n\n");
		sb.Append("[ext_resource type=\"TileSet\" path=\"res://Assets/Maps/tileset.tres\" id=\"1_ts\"]\n");
		sb.Append("[ext_resource type=\"Script\" path=\"res://Client/PlanetaPreFeito.cs\" id=\"1_pl\"]\n\n");

		// A RAIZ ORDENA: sem `y_sort_enabled` aqui as camadas nao se misturam com os
		// personagens -- o Godot so funde a ordenacao quando ela e continua de pai pra filho.
		//
		// E A RAIZ SABE QUE PLANETA ELA E. O script vai colado nela com nome, gravidade e tipo
		// ja preenchidos, entao quem abre `z03_Vegeta.tscn` no editor VE que ali a gravidade e
		// 10. A alternativa -- uma tabela central com essas informacoes -- obriga quem edita o
		// mapa a lembrar de um segundo arquivo, e o que ele esquecer nao falha em teste nenhum:
		// falha no dia em que alguem treinar em Vegeta e ganhar o mesmo que na Terra.
		//
		// O SERVIDOR NAO LE ISTO (ele roda sem cena). Ele le o mesmo dado do `planetas.json`,
		// que sai do MESMO extrator -- uma fonte, duas leituras, cada uma do lado que a usa.
		FichaDeCena ficha = FichaDaZona(nome);
		sb.Append($"[node name=\"{nome}\" type=\"Node2D\"]\n");
		sb.Append("y_sort_enabled = true\n");
		sb.Append("script = ExtResource(\"1_pl\")\n");
		sb.Append($"NomeDoPlaneta = \"{ficha.Nome}\"\n");
		sb.Append($"Gravidade = {ficha.Gravidade.ToString("0.##", CultureInfo.InvariantCulture)}\n");
		sb.Append($"Tipo = {ficha.Tipo}\n");
		sb.Append("Procedural = false\n\n");

		// ============================ TRES CAMADAS, TRES ALTURAS ============================
		// O corpo do jogador desenha em z 0, ordenando por Y junto dos objetos. Pra ele ficar
		// ACIMA do chao e da decoracao e ABAIXO das arvores, as tres precisam de z DIFERENTES --
		// e nao havia degrau entre o chao (-1) e os atores (0), entao a decoracao dividia o z 0
		// com o jogador e qualquer tufo de grama com Y maior desenhava por cima dele.
		//
		//   -2  chao          (grama, terra)
		//   -1  decoracao     (litoral, plantas, cadeiras)
		//    0  atores E objetos, ordenando por Y entre si (arvores)
		// ====================================================================================

		// ============================ AS CAMADAS NASCEM VAZIAS ============================
		// Elas sao a MOLDURA (tileset, altura, ordenacao) e nao o conteudo. As celulas chegam do
		// `.pedacos` em blocos de 64x64, conforme a camera anda -- ver o comentario la em cima e
		// `Client.PintorDePedacos`. Uma camada com dado aqui voltaria a ser montada inteira no
		// primeiro quadro, que e exatamente o que se esta tirando.
		// ==================================================================================

		// O CHAO NUNCA ORDENA e fica sempre embaixo: e chao. Ordenar 250 mil tiles de grama
		// contra os personagens seria caro e nao mudaria nada -- ninguem passa atras da grama.
		sb.Append("[node name=\"Chao\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("z_index = -2\n");
		sb.Append(SemFisica);
		sb.Append("tile_set = ExtResource(\"1_ts\")\n\n");

		// DECORACAO: o turf que estava POR CIMA do chao no prefab. E onde moram a porta, o
		// litoral curvo, as plantas e as cadeiras -- tudo que o "primeiro turf vence" comia.
		//
		// NAO ORDENA MAIS POR Y. Ela esta inteira abaixo dos atores agora, entao ordenar nao muda
		// desenho nenhum -- so custaria a classificacao de mais uma camada de 250 mil celulas.
		// Nada aqui e alto o bastante pra alguem passar atras: quem tem altura mora em `Objetos`.
		sb.Append("[node name=\"Decor\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("z_index = -1\n");
		sb.Append(SemFisica);
		sb.Append("tile_set = ExtResource(\"1_ts\")\n\n");

		// ============================ AS CAMADAS A MAIS: `Decor2`, `Decor3`... ============================
		// So no andar que tem celula em pilha (ver `PilhaDaCelula`), e so quantas a mais funda pede. Mesmo
		// `z_index` da decoracao: quem decide a ordem entre elas e a ordem dos nos, e cada uma vem DEPOIS
		// da anterior -- acima da decoracao, abaixo dos objetos e dos atores.
		//
		// O CLIENTE NAO MUDA: ele acha a camada de cada pedaco pelo NOME que o `.pedacos` traz
		// (`PlanetaPreFeito.FonteDoArquivo.Achar`) e apaga o que caiu por TIPO (`World.CamadasDoCenario`).
		// Mas o jogo carrega a cena BINARIA (`.scn`): quem aplicar um `.tscn` com camada nova tem que
		// regerar o `.scn` (comando `binario`) ANTES de por o `.pedacos` -- com a cena velha, o cliente
		// avisa que a camada nao existe e os desenhos dela simplesmente nao aparecem.
		var nomesDasCamadas = new List<string> { "Chao", "Decor" };
		var celulasDasCamadas = new List<List<Jandirus.Core.World.CelulaDePedaco>> { bytes, decoracao };
		for (int i = 0; i < maisDecoracao.Count; i++)
		{
			nomesDasCamadas.Add($"Decor{i + 2}");
			celulasDasCamadas.Add(maisDecoracao[i]);
			sb.Append($"[node name=\"Decor{i + 2}\" type=\"TileMapLayer\" parent=\".\"]\n");
			sb.Append("z_index = -1\n");
			sb.Append(SemFisica);
			sb.Append("tile_set = ExtResource(\"1_ts\")\n\n");
		}
		nomesDasCamadas.Add("Objetos");
		celulasDasCamadas.Add(objetos);

		// SEGUNDA CAMADA: o que fica EM CIMA do chao. Precisa ser um layer proprio porque o
		// TileMapLayer guarda UM tile por celula -- arvore e grama na mesma celula sao duas
		// camadas, nao duas entradas na mesma.
		// OBJETOS ORDENAM POR Y. E o que poe o personagem ATRAS da arvore quando ele esta
		// acima dela e NA FRENTE quando esta abaixo. `z_index` fixo mataria isso: dentro de um
		// mesmo z quem decide e o Y, entre z diferentes quem decide e sempre o z.
		sb.Append("[node name=\"Objetos\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("y_sort_enabled = true\n");
		sb.Append(SemFisica);
		sb.Append("tile_set = ExtResource(\"1_ts\")\n");

		// SO GRAVA QUANDO PEDIDO: a reconversao de fisica (`fisica`, ver `Convert`) passa por aqui pra
		// obter `paredes`/`cegos` pela MESMA regra da cena, e nao pode reescrever cena, pedacos nem luz.
		// E PRESO SO GRAVA O QUE CABE NO TILESET DO DISCO: com uma falta anotada, nada sai (ver `Trava`).
		if (gravar && trava is not { Faltas.Count: > 0 })
		{
			File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));

			// AS CELULAS, AO LADO DA CENA. A ordem dos nomes casa com a ordem em que as camadas foram
			// declaradas acima -- e o que deixa o cliente achar o `TileMapLayer` de cada pedaco sem
			// depender de procurar por nome numa arvore que ele nao montou.
			string arqPedacos = Path.ChangeExtension(caminho, ".pedacos");
			Jandirus.Core.World.PedacosDoMapa.Escrever(
				arqPedacos,
				Jandirus.Core.World.PedacosDoMapa.LadoPadrao,
				[.. nomesDasCamadas],
				[.. celulasDasCamadas]);

			// ============================ LER DE VOLTA E CONFERIR A CONTA ============================
			// Um mapa que perde celulas no caminho nao falha: ele DESENHA errado, e so alguem olhando
			// pro chao percebe -- meses depois, sem saber de onde veio. Este pipeline ja produziu um
			// defeito assim (o `tile_map_data` recusado em silencio por causa do numero de formato: a
			// cena carregava, o layer ficava vazio e nada avisava).
			//
			// CONTAR NAO BASTA: uma celula que caisse no balde errado, ou com o X e o Y trocados,
			// passaria por uma conferencia de quantidade. A assinatura mistura camada, posicao e quadro
			// de cada celula e NAO depende da ordem -- que e o que muda de propósito no agrupamento.
			//
			// Sao ~5 ms por mapa pra transformar "confio no meu agrupamento" em "esta escrito".
			int esperadas = celulasDasCamadas.Sum(c => c.Count);
			ulong assinado = 0;
			for (int c = 0; c < celulasDasCamadas.Count; c++) assinado ^= Assinar(celulasDasCamadas[c], c);

			Jandirus.Core.World.PedacosDoMapa? relido =
				Jandirus.Core.World.PedacosDoMapa.Ler(File.ReadAllBytes(arqPedacos));
			if (relido == null)
			{
				Console.WriteLine($"  {nome}: ERRO -- o .pedacos que acabei de escrever nao volta a ler");
			}
			else
			{
				ulong lido = 0;
				for (int c = 0; c < relido.Camadas.Length; c++)
					for (int cy = relido.Cy0; cy < relido.Cy1; cy++)
						for (int cx = relido.Cx0; cx < relido.Cx1; cx++)
						{
							if (!relido.Achar(cx, cy, c, out int ini, out int q)) continue;
							for (int i = 0; i < q; i++) lido ^= Marca(relido.Celula(ini, i), c);
						}

				if (relido.TotalDeCelulas != esperadas || lido != assinado)
					Console.WriteLine($"  {nome}: ERRO NO .pedacos -- escrevi {esperadas} celulas "
									  + $"(assinatura {assinado:X16}) e reli {relido.TotalDeCelulas} "
									  + $"({lido:X16})");
			}

			LightCatalog.Escrever(Path.ChangeExtension(caminho, ".luz"), luzes);
			if (luzes.Count > 0) Console.WriteLine($"  {nome}: {luzes.Count} fontes de luz");
		}
		if (subsolo.Count > 0)
			Console.WriteLine($"  {nome}: {subsolo.Values.Sum()} celula(s) com turf denso POR BAIXO de outro, sem fisica -- "
							  + string.Join(", ", subsolo.OrderByDescending(kv => kv.Value).Take(4)
								  .Select(kv => $"{kv.Key[(kv.Key.LastIndexOf('/') + 1)..]} x{kv.Value}")));
		paredes = muros;
		cegos = vendados;
		portasDaCena = portas;
		maquinasDaCena = interativos;
		passagensDaCena = passagens;
		if (passagens.Count > 0)
			Console.WriteLine($"  {nome}: {passagens.Count} passagem(ns) pra outros mapas");
		if (semDestino > 0)
			Console.WriteLine($"  {nome}: ATENCAO -- {semDestino} passagem(ns) apontam pra um z que nao foi convertido");
		if (interativos.Count > 0)
			Console.WriteLine($"  {nome}: {interativos.Count} maquina(s) saem do tilemap");
		if (comObj > 0 || objSemArte > 0)
			Console.WriteLine($"  {nome}: {comObj} objetos desenhados"
							  + (objSemArte > 0 ? $" | {objSemArte} celulas com objeto SEM arte" : ""));
		if (repontadas > 0)
			Console.WriteLine($"  {nome}: {repontadas} celulas apontam pra uma tira animada"
							  + (repontadasPadrao > 0 ? $" ({repontadasPadrao} no estado PADRAO)" : ""));
		if (celulasEmPilha > 0)
			Console.WriteLine($"  {nome}: {celulasEmPilha} celula(s) em PILHA (mais de tres desenhos, ou turf por cima de objeto)"
							  + $" -- {maisDecoracao.Count} camada(s) a mais na cena: "
							  + string.Join(", ", Enumerable.Range(2, maisDecoracao.Count).Select(i => $"Decor{i} ({maisDecoracao[i - 2].Count})")));
		if (underlaysPorCima + arestasPorBaixo + corposPorBaixo + objetosSemCamada > 0)
			Console.WriteLine($"  {nome}: {underlaysPorCima} turf(s) de baixo desenhados POR CIMA (camada maior que a do turf de cima)"
							  + $" | {arestasPorBaixo} aresta(s) na decoracao, por baixo de outro objeto"
							  + (corposPorBaixo > 0 ? $" | {corposPorBaixo} objeto(s) de um tile na decoracao, por baixo de outro" : "")
							  + (objetosSemCamada > 0 ? $" | {objetosSemCamada} objeto(s) com arte SEM camada (a celula so guarda dois)" : ""));
		if (soSemCaixa > 0)
			Console.WriteLine($"  {nome}: {soSemCaixa} celula(s) casaram o estado so IGNORANDO A CAIXA (no BYOND cairiam no estado vazio)");
		foreach ((string q, int n) in semDirecao.OrderByDescending(kv => kv.Value))
			Console.WriteLine($"     DIRECAO QUE O ESTADO NAO TEM, pintada com a primeira: {n,7}x  {q}");

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

		// ============================ NADA PODE SER SOLIDO E INVISIVEL CALADO ============================
		// Esta e a regra que faltava, e ela vale mais que os tres consertos que a trouxeram. As duas
		// queixas do dono ("falta o banco" e "tem parede invisivel no meio do mapa") sao o mesmo
		// defeito visto de dois lados: alguma coisa que o servidor sabe que existe e que a tela nao
		// mostra. O port ja produziu esse par pelo menos quatro vezes -- a bandeira de conquista sem
		// `.dmi` convertido, o `Ship_Control`/`Ship_Pad` fora do catalogo, 35 atlas escritos e nunca
		// importados, e agora a costura de Hera.
		//
		// Um erro visivel aqui vale mais que trinta e cinco atlas mudos: o custo de descobrir isto
		// pelo jogo e o dono andando mil tiles ate esbarrar no nada.
		//
		// O QUE E LEGITIMO FICA DE FORA da conta, e sao dois casos so: a BORDA DO MUNDO (`mudas`, o
		// Blank com miolo) e a PASSAGEM, que e desenhada mas nao bloqueia. Tudo o mais que bloqueia
		// tem que ter dono na tela.
		var fantasmas = muros.Where(c => !desenhadas.Contains(c) && !mudas.Contains(c)).ToList();
		if (fantasmas.Count > 0)
		{
			Console.WriteLine($"     PAREDE INVISIVEL: {fantasmas.Count} celula(s) BLOQUEIAM e ninguem as desenha");
			foreach ((int x, int y) in fantasmas.OrderBy(c => c.Item2).ThenBy(c => c.Item1).Take(8))
				Console.WriteLine($"        ({x},{y})");
		}

		if (maquinaSemArte.Count > 0)
		{
			Console.WriteLine($"     MAQUINA SEM DESENHO: {maquinaSemArte.Values.Sum()} celula(s). O catalogo "
							  + "a reconhece e o `.dmi` nao virou arte -- ela NAO entrou no `.objetos`:");
			foreach ((string q, int n) in maquinaSemArte.OrderByDescending(kv => kv.Value))
				Console.WriteLine($"        {n,7}x  {q}");
		}

		if (costuras.Count > 0)
			Console.WriteLine($"  {nome}: {costuras.Count} celula(s) de vazio DESARMADAS "
							  + "(sem miolo -- costura do mapeador, nao limite do mundo)");

		if (semAnimacao.Count > 0)
		{
			Console.WriteLine($"     ANIMACAO PERDIDA: {semAnimacao.Values.Sum()} celulas em "
							  + $"{semAnimacao.Count} estado(s) que TEM mais de um quadro e ficaram paradas");
			foreach ((string q, int n) in semAnimacao.OrderByDescending(kv => kv.Value).Take(8))
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

		// SPRITE MAIOR QUE O TILE: BASE NO CHAO DA CELULA, CENTRADO NA HORIZONTAL.
		//
		// Nao e palpite -- e a regra que o proprio jogo escreve. O BYOND ancora o icone no
		// canto INFERIOR ESQUERDO do tile (cresce pra cima e pra direita), e este jogo corrige
		// isso na mao, em Code/Modules/Turfs/Plants.dm:31-35:
		//
		//     var/icon/I = icon(icon,icon_state)
		//     trees_pixelx_cache[ck] = 16 - I.Width()/2
		//     pixel_x = trees_pixelx_cache[ck]
		//
		// `16 - largura/2` poe o CENTRO do icone no centro do tile, e `pixel_y` nunca e tocado
		// -- ou seja: centro no meio, base no chao. A mesma formula reaparece em
		// Reincarnation_Tree e Lumber_Tree. Eu tinha portado o padrao do engine (canto
		// inferior ESQUERDO) e nao a correcao do jogo: a arvore saia 32 px a direita e a
		// colisao ficava visivelmente fora dela.
		//
		// `texture_origin` e SUBTRAIDO da posicao de desenho: positivo move pra CIMA e pra
		// ESQUERDA. O Godot ja desenha centrado na horizontal, entao x = 0; levar a base ao pe
		// da celula pede y = +(altura-32)/2.
		int desY = (f.IconH - Cell) / 2;
		bool grande = f.IconW != Cell || f.IconH != Cell;

		void Ancora((int X, int Y) c, StringBuilder onde)
		{
			// A VARIANTE TRAZ A ANCORA DELA (o desenho que o BYOND nao centra -- ver `AncoraDoTipo`), e
			// vale pra folha de 32x32 tambem: e o `step_y` de uma instancia do mapa.
			if (f.Origem is { } propria)
			{
				onde.Append($"{c.X}:{c.Y}/0/texture_origin = Vector2i({propria.X}, {propria.Y})\n");
				return;
			}
			if (!grande) return;
			onde.Append($"{c.X}:{c.Y}/0/texture_origin = Vector2i(0, {desY})\n");

			// SEM `y_sort_origin`. Ele parece certo ("ordena pelo pe") e estava meio tile
			// errado: o personagem ordena pelo Y do NO, que e o centro do sprite dele, e os
			// tiles de 32x32 nao recebiam ajuste nenhum. Deslocar so os tiles grandes em +16
			// criava tres referencias diferentes na mesma comparacao.
			//
			// Com todo mundo em 0 a conta fecha sozinha: a base do tile grande fica em
			// centro+16 (por causa do desY acima) e os pes do personagem em centro+16 (sprite
			// de 32 px centrado no no). Os dois +16 se cancelam e ordenar pelo centro passa a
			// ser exatamente ordenar pela base.
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

		// ============================ O COMPANHEIRO E SO TIRAS ============================
		// Uma linha por estado, comecando sempre na coluna zero -- foi pra isso que ele foi feito.
		// Entao aqui nao ha teste de "cabe": cabe por construcao, e o `animation_columns = 0` que o
		// Godot ja entendia volta a valer sem nenhuma aritmetica de wrap.
		if (f.States.Count == 0 && f.Duracoes.Count > 0)
		{
			for (int linha = 0; linha < f.Duracoes.Count; linha++)
			{
				double[] dur = f.Duracoes[linha];
				var t = new StringBuilder();
				t.Append($"0:{linha}/0 = 0\n");
				t.Append($"0:{linha}/animation_columns = 0\n");
				t.Append($"0:{linha}/animation_speed = 1.0\n");
				for (int q = 0; q < dur.Length; q++)
					t.Append($"0:{linha}/animation_frame_{q}/duration = "
							 + dur[q].ToString("0.####", CultureInfo.InvariantCulture) + "\n");
				Ancora((0, linha), t);
				Fisica((0, linha), t);
				linhas.Add((0, linha, t.ToString()));
				animados++;
				for (int q = 0; q < dur.Length; q++) ocupadas.Add((q, linha));
			}

			foreach ((int X, int Y, string Texto) l in linhas.OrderBy(l => l.Y).ThenBy(l => l.X))
				sub.Append(l.Texto);
			return (linhas.Count, animados);
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
	/// O ESTADO QUE O TIPO DECLARA NAO SEGURA O MOSAICO. `VegetaWaterHD` escreve `icon_state="0,0"`
	/// (`NewTurfs.dm:212-217`), e o `New()` chama o `autofill()` em TODO turf HD com tamanho, sem
	/// perguntar se ja havia estado (`TurfOnNew.dm:11-14`) -- o `"0,0"` e so o que o editor de mapa
	/// mostra. Devolver o declarado repetia o canto noroeste em toda celula: o lago inteiro de Vegeta
	/// (79.135 celulas) e a lava saiam com o mesmo pedaco de 32 px lado a lado, com a emenda aparecendo
	/// em toda borda de celula. Os 13 tipos que declaram estado tem o mosaico completo na folha.
	///
	/// O EIXO Y: o `autofill` usa o y do BYOND, que conta DE BAIXO PRA CIMA, e o DmmMap guarda
	/// em ordem de arquivo (linha 0 no topo). Sem converter, o mosaico sai espelhado na
	/// vertical -- funciona, e fica sutilmente errado, que e pior que quebrar.
	/// </summary>
	private static string? EstadoDaCelula(TurfDef td, int x, int y, int altura)
	{
		if (!td.IsHD) return td.IconState;

		int bx = x + 1;              // o BYOND indexa a partir de 1
		int by = altura - y;         // e conta de baixo pra cima

		if (td.GetWidth > 0 && td.GetHeight > 0)
			return $"{bx % (td.GetWidth / Cell)},{by % (td.GetHeight / Cell)}";

		return $"{bx & 1},{by & 1}";
	}

	/// <summary>Indice linear de quadro -> coordenada na grade do atlas.</summary>
	private static (int X, int Y) Indice(Fonte f, int i) => (i % f.Cols, i / f.Cols);

	private static string Inv(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

	// =====================================================================
	// A AGUA -- a terceira classe de celula (ver Core/World/Agua.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS DE AGUA DESTE ANDAR.
	///
	/// ============================ QUAL TURF DECIDE, NUMA CELULA COM VARIOS ============================
	/// O ULTIMO, e e a mesma regra que o desenho ja usa vinte linhas acima ("o ultimo turf vence" --
	/// no DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o anterior, entao quem existe no fim e o
	/// unico que existe). Perguntar pelo PRIMEIRO daria agua onde o mapeador pos uma ponte ou uma
	/// pedra por cima do lago, e chao onde ele pos agua por cima da areia.
	/// ================================================================================================
	///
	/// Sai como funcao propria, e nao dentro do `EscreverCena`, porque ela precisa rodar tambem no
	/// comando `agua` -- que existe justamente pra NAO reconverter os sprites (ver `Program.cs`).
	/// Duas derivacoes de "o que e agua" divergiriam do mesmo jeito que a colisao e a cena
	/// divergiam em 2% das celulas antes de virarem uma passada so.
	/// </summary>
	internal static List<(int X, int Y)> CelulasDeAgua(DmmLevel nivel, DmmMap.Result dados,
													   Dictionary<string, TurfDef> turfs)
	{
		var molhadas = new List<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
				}

				if (ultimoTurf == null || !turfs.TryGetValue(ultimoTurf, out TurfDef? td)) continue;
				if (Aguas.Eh(ultimoTurf, td)) molhadas.Add((x, y));
			}
		return molhadas;
	}

	// =====================================================================
	// A NUVEM -- a quarta classe de celula (ver Core/World/Ceu.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS DE NUVEM DESTE ANDAR.
	///
	/// **A MESMA REGRA DE DESEMPATE DA AGUA**, e por isso ela e uma copia da forma e nao da decisao:
	/// vale o ULTIMO turf da celula, porque no DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o
	/// anterior. Quem existe no fim e o unico que existe. Perguntar pelo primeiro daria nuvem onde o
	/// mapeador pos uma plataforma por cima dela -- e no Templo, que e quase todo ceu com ilhas de
	/// piso, isso seria a diferenca entre um mapa jogavel e um buraco.
	///
	/// QUEM DECIDE E o <see cref="Ceus.Eh"/>, que por sua vez delega pro `Aguas.EhCeu` -- a MESMA
	/// leitura que exclui o ceu da agua vinte linhas acima. Ver o cabecalho de `Ceus`.
	/// </summary>
	internal static List<(int X, int Y)> CelulasDeNuvem(DmmLevel nivel, DmmMap.Result dados,
													  Dictionary<string, TurfDef> turfs)
	{
		var nuvens = new List<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
				}

				if (ultimoTurf == null || !turfs.TryGetValue(ultimoTurf, out TurfDef? td)) continue;
				if (Nuvens.Eh(ultimoTurf, td)) nuvens.Add((x, y));
			}
		return nuvens;
	}

	/// <summary>
	/// Grava o `.nuvem` de todos os andares E MAIS NADA -- o irmao do <see cref="ConverterAguas"/>, e a
	/// justificativa dele vale palavra por palavra: a conversao cheia reescreveria o tileset, os 40
	/// `.tscn`/`.pedacos` e o indice de sprites pra buscar UM bit por celula.
	///
	/// ZONA SEM NUVEM NAO GANHA ARQUIVO, e um `.nuvem` velho de uma zona que perdeu a nuvem e APAGADO --
	/// mesma regra do `.agua`, e pelo mesmo motivo: um arquivo antigo em que o leitor confia e pior
	/// que arquivo nenhum. Aqui ele seria especialmente feio, porque nuvem que sobrou de um mapa
	/// antigo **derruba gente** em vez de so parar.
	/// </summary>
	public static void ConverterNuvens(string dmmDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		int andares = 0, comCeu = 0, total = 0, apagados = 0, derrubam = 0;
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".nuvem");
				List<(int X, int Y)> nuvens = CelulasDeNuvem(nivel, dados, turfs);
				andares++;

				if (nuvens.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, nuvens);
				comCeu++;
				total += nuvens.Count;

				// RELER O QUE ACABOU DE SER ESCRITO, pelo mesmo motivo do `.agua`: o que interessa nao
				// e "o conversor decidiu N celulas", e "o objeto que o JOGO consulta responde ceu em N
				// celulas". O `CarregarNuvem` RECUSA CALADO quando o tamanho nao bate, e uma recusa
				// calada aqui seria verde na bancada e chao comum em jogo -- que e literalmente o bug
				// que esta tarefa conserta.
				//
				// E O NOME DA ZONA ENTRA NA RELEITURA, e nao um `true` de conveniencia: e ele que
				// decide se esta nuvem derruba, e conferir o plano sem conferir o desfecho deixaria
				// passar o caso em que o mapa tem ceu e o Core nao sabe pra onde manda-lo.
				string zona = nome[(nome.IndexOf('_') + 1)..];
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				int conferidas = 0;
				if (!relido.CarregarNuvem(File.ReadAllBytes(caminho), zona))
					Console.WriteLine($"  {nome,-34} FALHOU: o .nuvem escrito nao volta pelo CarregarNuvem");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
							if (relido.EhNuvem(x, y)) conferidas++;

				var destino = Jandirus.Core.World.ClasseDeNuvem.DestinoDaQueda(zona);
				if (destino != null) derrubam++;

				string aviso = conferidas == nuvens.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {nuvens.Count,8} celulas de ceu  "
								  + (destino is { } d2 ? $"DERRUBA -> {d2.Zona} ({d2.Bx},{d2.By})" : "so barra")
								  + aviso);
			}

		Console.WriteLine($"andares: {andares} | com ceu: {comCeu} ({derrubam} derrubam) | celulas: {total}"
						  + (apagados > 0 ? $" | .nuvem apagados: {apagados}" : ""));
	}

	// =====================================================================
	// O QUE NASCE SOB TETO -- a area `Inside` do original (ver Interiores.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS QUE NASCEM SOB TETO NESTE ANDAR. As tres fontes estao no cabecalho de
	/// <see cref="Interiores"/>; esta funcao so as junta.
	///
	/// A AREA E O ULTIMO `/area` DA CHAVE e o turf e o ULTIMO `/turf`, a mesma regra de desempate da
	/// agua e do duro.
	///
	/// Sai como funcao propria, e nao dentro do `EscreverCena`, pelo mesmo motivo dos outros planos:
	/// ela roda tambem no comando `dentro`, que existe pra NAO reconverter os sprites.
	/// </summary>
	/// <param name="comACidadeDeVegeta">
	/// Marca as celulas que o `Build_Vegeta_Structures` carimba (`VegetaCity.dm:42`). So faz sentido
	/// no andar dela E com a cidade de pe no que o jogo carrega: marcar a planta sobre chao nu daria
	/// oito retangulos secos no meio do campo. Quem decide e o chamador, que e quem sabe se ela foi
	/// erguida (ver <see cref="CidadeDeVegetaDePe"/>).
	/// </param>
	internal static List<(int X, int Y)> CelulasInternas(DmmLevel nivel, DmmMap.Result dados,
														 bool comACidadeDeVegeta)
	{
		var internas = new HashSet<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null, area = null;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
					else if (bp.StartsWith("/area", StringComparison.Ordinal)) area = bp;
				}

				if ((area != null && Interiores.EhAreaInterna(area))
					|| (ultimoTurf != null && Interiores.EhTurfErguido(ultimoTurf)))
					internas.Add((x, y));
			}

		if (comACidadeDeVegeta)
			foreach (CidadeDeVegeta.Peca p in CidadeDeVegeta.Planta())
			{
				(int x, int y) = CidadeDeVegeta.NoPort(p, nivel.Height);
				if (x >= 0 && y >= 0 && x < nivel.Width && y < nivel.Height) internas.Add((x, y));
			}

		return [.. internas];
	}

	/// <summary>
	/// A CIDADE DE VEGETA ESTA DE PE no `.col` que o jogo carrega?
	///
	/// O comando `dentro` nao reconverte o mapa, entao ele nao tem o relatorio do
	/// <see cref="CidadeDeVegeta.Erguer"/> pra perguntar. Mas o `Erguer` e tudo-ou-nada na estrutura
	/// (faltando arte de parede ele nao poe um tijolo), entao UMA parede prometida responde por todas:
	/// se ela bloqueia no `.col`, a cidade foi carimbada.
	/// </summary>
	internal static bool CidadeDeVegetaDePe(Jandirus.Core.World.ZoneCollision? col, int altura)
	{
		if (col == null) return false;
		foreach (CidadeDeVegeta.Peca p in CidadeDeVegeta.Planta())
		{
			if (!CidadeDeVegeta.EhParede(p.Turf)) continue;
			(int x, int y) = CidadeDeVegeta.NoPort(p, altura);
			return col.BlockedCell(x, y);
		}
		return false;
	}

	/// <summary>
	/// Grava o `.dentro` de todos os andares E MAIS NADA -- irmao do <see cref="ConverterAguas"/> e
	/// do <see cref="ConverterDuros"/>, e pelo motivo deles: a conversao cheia reescreve o tileset, o
	/// `tiles.json`, os 40 `.tscn`/`.pedacos` e o indice de sprites pra buscar UM bit por celula.
	///
	/// ANDAR SEM INTERIOR NAO GANHA ARQUIVO, e um `.dentro` velho de um andar que perdeu o interior e
	/// APAGADO: o leitor confiaria nele e deixaria um pedaco de campo aberto sem chuva pra sempre.
	///
	/// NAO PRECISA DA ARVORE DE TIPOS (`pastaCode`), ao contrario dos irmaos: a regra so le o nome da
	/// area e o prefixo do turf, e os dois estao no proprio `.dmm`.
	/// </summary>
	public static void ConverterInteriores(string dmmDir, string outDir)
	{
		Directory.CreateDirectory(outDir);

		int andares = 0, comInterior = 0, total = 0, apagados = 0;
		foreach ((string _, DmmMap.Result dados, int off) in LerMapas(dmmDir))
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".dentro");
				string arqCol = Path.Combine(outDir, nome + ".col");
				Jandirus.Core.World.ZoneCollision? col = File.Exists(arqCol)
					? Jandirus.Core.World.ZoneCollision.Load(File.ReadAllBytes(arqCol))
					: null;

				bool andarDaCidade = nivel.Z + off == CidadeDeVegeta.Z;
				bool cidade = andarDaCidade && CidadeDeVegetaDePe(col, nivel.Height);
				if (andarDaCidade && !cidade)
					Console.WriteLine($"  {nome,-34} AVISO: a cidade de Vegeta NAO esta de pe no .col -- "
									  + "as celulas dela NAO foram marcadas");

				List<(int X, int Y)> internas = CelulasInternas(nivel, dados, cidade);
				andares++;

				if (internas.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, internas);
				comInterior++;
				total += internas.Count;

				// RELER O QUE ACABOU DE SER ESCRITO, pelo mesmo motivo do `.agua` e do `.duro`: o que
				// interessa nao e "o conversor decidiu N celulas", e "o objeto que o JOGO consulta
				// responde N". O `CarregarDentro` RECUSA CALADO quando o tamanho nao bate, e uma recusa
				// calada aqui seria verde na bancada e neve dentro do Banco em jogo.
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				int conferidas = 0, densas = 0;
				if (!relido.CarregarDentro(File.ReadAllBytes(caminho)))
					Console.WriteLine($"  {nome,-34} FALHOU: o .dentro escrito nao volta pelo CarregarDentro");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
						{
							if (!relido.NasceuDentro(x, y)) continue;
							conferidas++;
							// QUANTAS SAO PAREDE/TELHADO: o predio entra inteiro, e o numero mostra o
							// quanto do plano e casca (o que se ve de fora) e o quanto e piso.
							if (col != null && col.BlockedCell(x, y)) densas++;
						}

				string aviso = conferidas == internas.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {internas.Count,8} celulas sob teto"
								  + $" ({densas} sao parede/telhado/porta fechada)"
								  + (cidade ? " [com a cidade de Vegeta]" : "") + aviso);
			}

		Console.WriteLine($"andares: {andares} | com interior: {comInterior} | celulas: {total}"
						  + (apagados > 0 ? $" | .dentro apagados: {apagados}" : ""));
	}

	// =====================================================================
	// O QUE NAO SE QUEBRA -- o `destroyable` do original (ver Duros.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS INDESTRUTIVEIS DESTE ANDAR.
	///
	/// ============================ A REGRA NAO E A MESMA DA AGUA, E ISSO IMPORTA ============================
	/// A agua pergunta pelo ULTIMO turf, porque no DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o
	/// anterior -- quem existe no fim e o unico que existe. Aqui vale o mesmo pros TURFS, e por isso
	/// esta funcao tambem procura o ultimo.
	///
	/// So que `destroyable` nao e uma propriedade da CELULA, e da coisa que sobreviveria: uma
	/// `/obj/barrier` coexiste com o turf, e derrubar o chao debaixo dela nao a apaga (no original
	/// `Destroy()` troca o TURF por `/turf/Ground/Ground8` e a barreira continua bloqueando pelo
	/// `selectivecollide`). Como no port a barreira nao e entidade -- ela virou celula solida do
	/// `.col` --, abrir a celula a apagaria de vez. Entao a celula e dura se o ULTIMO TURF for duro
	/// **ou** se houver QUALQUER obj indestrutivel nela. Ver <see cref="Duros"/>.
	/// ======================================================================================================
	///
	/// Sai como funcao propria, e nao dentro do `EscreverCena`, pelo mesmo motivo da agua: ela
	/// precisa rodar tambem no comando `duro`, que existe justamente pra NAO reconverter os sprites.
	/// </summary>
	internal static List<(int X, int Y)> CelulasDuras(DmmLevel nivel, DmmMap.Result dados,
													  Dictionary<string, TurfDef> turfs)
	{
		var duras = new List<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null;
				bool objDuro = false;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
					else if (Duros.EhObjDuro(bp)) objDuro = true;
				}

				if (objDuro) { duras.Add((x, y)); continue; }
				if (ultimoTurf != null && turfs.TryGetValue(ultimoTurf, out TurfDef? td) && Duros.Eh(td))
					duras.Add((x, y));
			}
		return duras;
	}

	/// <summary>
	/// Grava o `.duro` de todos os andares E MAIS NADA -- irmao do <see cref="ConverterAguas"/>, e
	/// pelo mesmo motivo dele: a conversao cheia reescreve o tileset, o `tiles.json`, os 40
	/// `.tscn`/`.pedacos` e o indice de sprites, e o indice resolve nome repetido por `TryAdd` --
	/// rodar tudo pra buscar UM bit por celula reescreveria 21 artes (4 delas genuinamente
	/// diferentes) sem ninguem ter pedido.
	///
	/// A ZONA SEM NADA DURO NAO GANHA ARQUIVO, e um `.duro` velho de uma zona que amoleceu e
	/// APAGADO -- deixar o antigo seria pior que nao ter nenhum: o leitor confiaria nele e uma
	/// parede que virou destrutivel continuaria eterna, calada.
	/// </summary>
	public static void ConverterDuros(string dmmDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		int andares = 0, comDuro = 0, total = 0, apagados = 0, alcancaveis = 0;
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".duro");
				List<(int X, int Y)> duras = CelulasDuras(nivel, dados, turfs);
				andares++;

				if (duras.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, duras);
				comDuro++;
				total += duras.Count;

				// ============================ RELER O QUE ACABOU DE SER ESCRITO ============================
				// A mesma regra do `.agua`, e pelo mesmo motivo: o que interessa nao e "o conversor
				// decidiu N celulas", e "o objeto que o JOGO consulta responde N". Sao coisas
				// diferentes -- o `.duro` passa por um cabecalho, por um bitset e por um leitor que
				// RECUSA CALADO quando o tamanho nao bate (`ZoneCollision.CarregarDuro`), e uma recusa
				// calada aqui e exatamente o defeito que este projeto mais paga caro: verde na
				// bancada, quebrando o vazio em jogo.
				// ==========================================================================================
				var lido = Jandirus.Core.World.ZoneCollision.Load(File.ReadAllBytes(
					Path.Combine(outDir, nome + ".col")));
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				int conferidas = 0, semColisao = 0, tocaveis = 0;
				if (!relido.CarregarDuro(File.ReadAllBytes(caminho)))
					Console.WriteLine($"  {nome,-34} FALHOU: o .duro escrito nao volta pelo CarregarDuro");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
						{
							if (!relido.Indestrutivel(x, y)) continue;
							conferidas++;
							if (lido == null) continue;
							// DURO E NAO BLOQUEIA e legitimo: `turf/Other/Sky1` tem `density = 0` e
							// `destroyable = 0`. So vale saber quantos sao -- pra esses o bit nao
							// muda desfecho nenhum, ja que so quem bloqueia chega ao `DerrubarCelula`.
							if (!lido.BlockedCell(x, y)) { semColisao++; continue; }
							// O QUE ESTE PASSE CONSERTA, medido: celula dura, que bloqueia, e que
							// ENCOSTA em chao livre -- ou seja, alcancavel por um punho, e portanto
							// uma das que caiam.
							for (int d = 0; d < 4 && conferidas > 0; d++)
							{
								int vx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
								int vy = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
								if (vx < 0 || vy < 0 || vx >= nivel.Width || vy >= nivel.Height) continue;
								if (!lido.BlockedCell(vx, vy)) { tocaveis++; break; }
							}
						}

				alcancaveis += tocaveis;
				string aviso = conferidas == duras.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {duras.Count,8} celulas duras"
								  + $" ({tocaveis} encostam em chao livre"
								  + (semColisao > 0 ? $", {semColisao} nem bloqueiam" : "") + ")"
								  + aviso);
			}

		Console.WriteLine($"andares: {andares} | com celula dura: {comDuro} | celulas: {total}"
						  + $" | ALCANCAVEIS A SOCO: {alcancaveis}"
						  + (apagados > 0 ? $" | .duro apagados: {apagados}" : ""));
	}

	/// <summary>
	/// Grava o `.agua` de todos os andares E MAIS NADA -- nem tileset, nem cena, nem sprite.
	///
	/// ============================ POR QUE UM CAMINHO SO PRA ISTO ============================
	/// A conversao cheia reescreve o tileset, o `tiles.json`, os 40 `.tscn`/`.pedacos` e o indice de
	/// sprites -- e o indice resolve nome repetido por `TryAdd`, entao rodar tudo pra buscar UM bit
	/// por celula reescreveria 21 artes (4 delas genuinamente diferentes) sem ninguem ter pedido.
	/// O dado que falta e novo e independente: um bitset por andar, derivado do `.dmm` e da arvore
	/// de tipos, que nao toca em nenhum arquivo que ja existe.
	///
	/// O `.agua` E UM ARQUIVO NOVO, nao uma cauda do `.col`: a cauda do `.col` ja tem dono (o plano
	/// de grupo da sombra, ver `ZoneCollision.Load`), e uma zona sem agua simplesmente nao ganha
	/// arquivo -- o leitor trata a ausencia como "sem agua", que e a verdade.
	/// =======================================================================================
	/// </summary>
	public static void ConverterAguas(string dmmDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		int andares = 0, comAgua = 0, total = 0, apagados = 0;
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".agua");
				List<(int X, int Y)> molhadas = CelulasDeAgua(nivel, dados, turfs);
				andares++;

				// ZONA SECA NAO GANHA ARQUIVO -- e um `.agua` velho de uma zona que virou seca e
				// APAGADO. Deixar o arquivo antigo seria pior que nao ter nenhum: o leitor confiaria
				// nele e o lago continuaria existindo pra colisao depois de sumir do mapa.
				if (molhadas.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, molhadas);
				comAgua++;
				total += molhadas.Count;

				// ============================ RELER O QUE ACABOU DE SER ESCRITO ============================
				// Nao e paranoia: o que interessa nao e "o conversor decidiu N celulas", e "o objeto
				// que o JOGO consulta responde agua em N celulas". Sao coisas diferentes -- o
				// `.agua` passa por um cabecalho, por um bitset e por um leitor que RECUSA calado
				// quando o tamanho nao bate (`ZoneCollision.CarregarAgua`), e uma recusa calada aqui
				// seria exatamente o defeito que este projeto mais paga caro: verde na bancada, seco
				// em jogo. Entao a conta sai do `EhAgua`, celula por celula.
				//
				// A SOBREPOSICAO COM O `.col` TAMBEM SAI, porque ela e legitima e vale ser vista: uma
				// `/obj/barrier` ou uma ponte por cima do lago deixa a celula parede E agua. Parede
				// vence pra todo mundo (ver `ZoneCollision.Bloqueia`), que e o certo -- ninguem nada
				// atravessando uma cerca.
				var lido = Jandirus.Core.World.ZoneCollision.Load(File.ReadAllBytes(
					Path.Combine(outDir, nome + ".col")));
				int conferidas = 0, sobrepostas = 0;
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				if (!relido.CarregarAgua(File.ReadAllBytes(caminho)))
					Console.WriteLine($"  {nome,-34} FALHOU: o .agua escrito nao volta pelo CarregarAgua");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
							if (relido.EhAgua(x, y))
							{
								conferidas++;
								if (lido != null && lido.BlockedCell(x, y)) sobrepostas++;
							}

				string aviso = conferidas == molhadas.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {molhadas.Count,8} celulas de agua"
								  + (sobrepostas > 0 ? $" ({sobrepostas} tambem parede no .col)" : "")
								  + aviso);
			}

		Console.WriteLine($"andares: {andares} | com agua: {comAgua} | celulas: {total}"
						  + (apagados > 0 ? $" | .agua apagados (zona secou): {apagados}" : ""));
	}

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
	/// <summary>Uma linha do `.passagens`: a celula de origem, a zona e o ponto de chegada que o DM cravou.</summary>
	private static string LinhaDePassagem(int x, int y, (string Zona, float Px, float Py) onde, Passagens.Destino dest) =>
		$"{{ \"x\": {x}, \"y\": {y}, \"zona\": \"{onde.Zona}\", "
		+ $"\"dx\": {onde.Px:0}, \"dy\": {onde.Py:0}, "
		+ $"\"nome\": \"{(dest.Nome.Length > 0 ? dest.Nome : onde.Zona)}\" }}";

	/// <summary>
	/// NA RECONVERSAO SO DE FISICA, AS LISTAS TAMBEM ACOMPANHAM A REGRA -- pelo mesmo principio do `.col`:
	/// o `.portas` so PERDE (a porta por baixo do teleportador some; nenhuma porta nova e inventada, o
	/// `arte` das que ficam e o do disco), e o `.passagens` e reescrito por inteiro, porque ele nao
	/// depende de arte nenhuma -- so do `.dmm` e da tabela de destinos.
	/// </summary>
	private static void AcompanharListas(string outDir, string nome, List<string> portas, List<string> passagens,
										 out int portasTiradas, out bool passagensMudaram)
	{
		portasTiradas = 0;
		passagensMudaram = false;

		string arqPortas = Path.Combine(outDir, nome + ".portas");
		if (File.Exists(arqPortas))
		{
			var quer = new HashSet<(int, int)>(portas.Select(CelulaDaLinha));
			List<string> velhas = LinhasDaLista(File.ReadAllText(arqPortas));
			List<string> ficam = velhas.Where(l => quer.Contains(CelulaDaLinha(l))).ToList();
			portasTiradas = velhas.Count - ficam.Count;
			if (portasTiradas > 0)
				File.WriteAllText(arqPortas, "[" + string.Join(",\n ", ficam) + "]", new UTF8Encoding(false));
		}

		string arqPassagens = Path.Combine(outDir, nome + ".passagens");
		string novo = "[" + string.Join(",\n ", passagens) + "]";
		string velho = File.Exists(arqPassagens) ? File.ReadAllText(arqPassagens) : "";
		if (!string.Equals(velho.Replace("\r\n", "\n"), novo, StringComparison.Ordinal))
		{
			File.WriteAllText(arqPassagens, novo, new UTF8Encoding(false));
			passagensMudaram = true;
		}
	}

	/// <summary>As linhas `{ ... }` de uma lista JSON escrita por este conversor.</summary>
	private static List<string> LinhasDaLista(string json)
	{
		var linhas = new List<string>();
		int i = 0;
		while (true)
		{
			int a = json.IndexOf('{', i);
			if (a < 0) break;
			int b = json.IndexOf('}', a);
			if (b < 0) break;
			linhas.Add(json[a..(b + 1)]);
			i = b + 1;
		}
		return linhas;
	}

	/// <summary>A celula (x, y) de uma linha `{ "x": N, "y": N, ... }`.</summary>
	private static (int, int) CelulaDaLinha(string linha)
	{
		int Campo(string nome)
		{
			int i = linha.IndexOf($"\"{nome}\"", StringComparison.Ordinal);
			if (i < 0) return -1;
			int dp = linha.IndexOf(':', i) + 1;
			int fim = dp;
			while (fim < linha.Length && (char.IsDigit(linha[fim]) || linha[fim] == ' ' || linha[fim] == '-')) fim++;
			return int.TryParse(linha[dp..fim].Trim(), out int v) ? v : -1;
		}
		return (Campo("x"), Campo("y"));
	}

	/// <summary>
	/// APAGA DO ARQUIVO OS BITS QUE A REGRA NOVA NAO BLOQUEIA MAIS -- e so isso.
	///
	/// Le o `.col`/`.vis` do disco (mesmo formato do `EscreverColisao`), compara com o conjunto
	/// recem-calculado e limpa o que esta ligado no disco e nao esta no conjunto. Um bit que o
	/// conjunto tem e o disco nao e CONTADO e
	/// devolvido, nunca gravado: gravar de novo o arquivo inteiro reintroduziria as diferencas do
	/// indice de sprites em memoria (ver `Convert(soFisica)`), e o que se quer aqui e libertar as
	/// mesas por baixo do piso, nao reconverter o mundo.
	/// </summary>
	/// <returns>(quantas celulas foram libertadas, quantos bits novos ficaram so no relato)</returns>
	internal static (int Libertadas, int NovasNaoGravadas) LibertarNaColisao(string caminho, int w, int h,
																			 IEnumerable<(int, int)> novas)
	{
		if (!File.Exists(caminho)) return (0, 0);
		byte[] dados = File.ReadAllBytes(caminho);
		int precisa = (w * h + 7) / 8;
		if (dados.Length < 8 + precisa || dados[0] != 'J' || dados[1] != 'C' || dados[2] != 'O' || dados[3] != 'L')
		{
			Console.WriteLine($"  {Path.GetFileName(caminho)}: nao e um JCOL que eu entenda -- nao mexo");
			return (0, 0);
		}
		int fw = dados[4] | (dados[5] << 8), fh = dados[6] | (dados[7] << 8);
		if (fw != w || fh != h)
		{
			Console.WriteLine($"  {Path.GetFileName(caminho)}: {fw}x{fh} no arquivo, {w}x{h} no .dmm -- nao mexo");
			return (0, 0);
		}

		var quer = new HashSet<int>();
		foreach ((int x, int y) in novas)
			if (x >= 0 && y >= 0 && x < w && y < h) quer.Add(y * w + x);

		int libertadas = 0, novasNaoGravadas = 0;
		for (int i = 0; i < w * h; i++)
		{
			bool ligado = (dados[8 + (i >> 3)] & (1 << (i & 7))) != 0;
			if (ligado && !quer.Contains(i))
			{
				dados[8 + (i >> 3)] &= (byte)~(1 << (i & 7));
				libertadas++;
			}
			else if (!ligado && quer.Contains(i)) novasNaoGravadas++;
		}
		if (libertadas > 0) File.WriteAllBytes(caminho, dados);
		return (libertadas, novasNaoGravadas);
	}

	private static int EscreverColisao(string caminho, int w, int h, IEnumerable<(int, int)> paredes)
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

}
