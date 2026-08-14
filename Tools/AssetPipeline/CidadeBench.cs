using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA CIDADE E DAS PAREDES INVISIVEIS -- `cidade &lt;pastaMaps&gt; &lt;pastaCode&gt;`.
///
/// ============================ A QUEIXA QUE A TROUXE ============================
/// "percebi q ta faltando itens como BANCO etc no planeta VEGETA, e ainda tem uma PAREDE INVISIVEL
/// no meio do mapa". Duas frases, e a suspeita inicial era que fossem UM defeito -- um predio que
/// existe na logica e nao tem arte da exatamente esses dois sintomas. Medido, eram DOIS defeitos
/// independentes; mas a suspeita valia, porque o par "solido e invisivel" ja nasceu neste port
/// quatro vezes por caminhos diferentes: a bandeira de conquista sem `.dmi` convertido, o
/// `Ship_Control`/`Ship_Pad` fora do catalogo, 35 atlas escritos e nunca importados, e a costura de
/// Hera.
///
/// Consertar as duas ocorrencias nao impede a quinta. O que impede e ALGUEM PERGUNTAR, sempre, e
/// e isso que este arquivo e.
/// ==============================================================================
///
/// ============================ POR QUE ELA NAO E O RELATORIO DO CONVERSOR ============================
/// O `MapConverter` ja imprime "PAREDE INVISIVEL" e "MAQUINA SEM DESENHO" -- e isso e necessario e
/// NAO e suficiente, por um motivo de calendario: aquele relatorio so existe no minuto em que
/// alguem roda o pipeline. O que o jogo carrega e o `Assets/Maps` que esta no disco, e ele pode ter
/// sido escrito ha tres meses, por outra versao do conversor, com outro `construcoes.json` ao lado.
///
/// Esta bancada le OS ARQUIVOS QUE O JOGO LE, e nao o `.dmm`. Ela responde "o que esta publicado
/// hoje tem parede invisivel?", que e uma pergunta diferente de "a conversao de hoje produziria
/// uma?".
/// ================================================================================================
///
/// ============================ AS SEIS FAMILIAS ============================
///  1. NADA SOLIDO SEM DESENHO -- a queixa, palavra por palavra. Varre TODA zona: celula que
///     bloqueia e que ninguem desenha, descontada a borda do mundo (ver `Fantasmas`).
///  2. A RECIPROCA -- nada desenhado como predio que se atravesse. Maquina densa tem que bloquear,
///     porta fechada tem que bloquear, parede prometida tem que bloquear... e CHAO prometido NAO
///     pode bloquear, que e a mesma doenca vista do outro lado.
///  3. A TABELA DA CIDADE -- cada peca que o construtor promete, com o seu nome e a sua linha. A
///     lista vem do <see cref="CidadeDeVegeta.Planta"/>, e nao de um `string[]` daqui: lista escrita
///     a mao apodrece calada.
///  4. O BANCO E ALCANCAVEL -- caminhar do ponto de nascimento ate ele, pela colisao de verdade, e
///     chegar perto o bastante pro verbo pegar.
///  5. A MESMA VARREDURA NA TERRA -- se a doenca e generica, o exame tem que ser generico. A 1 e a
///     2 ja rodam nas 40 zonas; aqui a Terra ganha o mesmo par banco+alcance que Vegeta.
///  6. O ALARME DA CARGA -- construcao que bloqueia e nao tem arte tem que APARECER, e nao virar
///     parede fantasma. A pergunta e a do Core (`CatalogoDeObras.SemDesenho`), a mesma que o
///     servidor faz no boot.
/// ==========================================================================
///
/// ============================ CADA FAMILIA REPROVA -- E ISSO E MEDIDO AQUI DENTRO ============================
/// Uma bancada que nunca ficou vermelha nao provou nada. As lembrancas deste port sao explicitas
/// nisso ("uniform escrito != pixel desenhado", "as duas telas concordam fica verde com as duas
/// erradas igual"), entao cada familia roda DUAS vezes: uma contra os arquivos de verdade, onde ela
/// tem que ficar verde, e outra contra uma copia com o defeito INJETADO, onde ela tem que ficar
/// vermelha. A segunda rodada conta como checagem tambem -- ela e quem prova que a primeira sabe
/// falhar.
///
/// As injecoes sao as da secao <see cref="Injecoes"/> e cada uma diz, no proprio nome, que defeito
/// ela finge.
/// =========================================================================================================
///
/// ============================ COMO CADA FAMILIA REPROVA -- MEDIDO, NAO SUPOSTO ============================
/// O placar limpo e **56 OK, 0 FALHAS**. Os seis defeitos abaixo foram postos um por vez -- quatro
/// nos ARQUIVOS (numa copia de Assets/Maps) e dois no CODIGO DE PRODUCAO -- e o placar e o que a
/// bancada imprimiu. Repare que cada um derruba uma familia DIFERENTE: e isso que diz que as seis
/// nao sao a mesma pergunta escrita seis vezes.
///
///  * **UMA PAREDE SEM DONO** (acender o bit de colisao em (200,200) de Vegeta, com o chao ainda
///    desenhado) -> **55 OK, 1 FALHA**: "TODA celula que bloqueia tem dono". E o defeito mais
///    importante do arquivo, porque e o unico que a leitura de "solido e mudo" NAO pega -- a grama
///    continua pintada embaixo da maquina sem sprite. Ver o cabecalho de `Fonte.Barra`.
///
///  * **O BANCO SEM FISICA** (apagar o bit de colisao em (122,214)) -> **54 OK, 2 FALHAS**: cai a
///    reciproca ("toda maquina densa e toda porta BLOQUEIAM") e cai a familia do banco, porque
///    alcancavel exige bloquear. E a queixa antiga do dono, "eu atravesso ele".
///
///  * **O BANCO ENGOLIDO** (tirar a linha `Bank` do `z03_Vegeta.objetos`) -> **50 OK, 2 FALHAS**. E
///    o estado exato em que Vegeta estava: o `.dmm` tinha o banco e o `.objetos` nao. Repare que
///    caem SEIS checagens de uma vez (as do banco de Vegeta somem junto com ele).
///
///  * **UMA BANCADA A MENOS** (tirar uma `Research_Station` do `.objetos`) -> **55 OK, 1 FALHA**, e
///    a linha vermelha e a da tabela: "as 8 pecas estao no mapa: 7". A cidade continua de pe; o que
///    reprova e a PROMESSA do construtor, que e de onde a lista vem.
///
///  * **O ALCANCE DE USO QUEBRADO** (`Interacoes.Alcance` de 64 pra 8) -> **51 OK, 2 FALHAS**, uma
///    em Vegeta e outra na Terra. O modo de falha e educativo: nao e "o verbo recusou", e "nao ha
///    NENHUMA celula de onde alcance" -- o corpo nao cabe na mesma celula da maquina, entao um raio
///    menor que um tile deixa o banco inutil sem tirar nada do mapa.
///
///  * **O ALARME CEGO** (`CatalogoDeObras.SemDesenho` devolvendo lista vazia) -> **55 OK, 1 FALHA**:
///    "[injecao] uma construcao densa e sem arte no catalogo REPROVA: 0 acusadas". So a familia 6
///    cai, e cai pela INJECAO -- o catalogo de verdade nao tem nenhuma, entao a afirmacao positiva
///    continuaria verde para sempre com a regra morta. E a razao de a injecao existir.
///
/// UMA NOTA SOBRE O QUE **NAO** DERRUBOU NADA, porque foi a lição mais cara desta rodada: acender o
/// bit de colisao SEM apagar o desenho (a primeira versao do primeiro defeito) deixou a bancada
/// **51 OK, 0 FALHAS** -- verde, com uma parede invisivel plantada. As tres leituras que existiam
/// ate ali ("perdeu a arte", "sem miolo", "chao dos dois lados") sao todas sobre celula SEM DESENHO,
/// e uma maquina sem sprite sobre grama tem desenho. A familia so passou a valer quando a pergunta
/// mudou de "alguem desenha aqui?" pra "quem responde por esta parede?".
/// =========================================================================================================
/// </summary>
public static class CidadeBench
{
	private static int _ok, _falhou;

	private static void Afirmar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; Console.WriteLine($"[cidade]   OK    {oque}"); return; }
		_falhou++;
		Console.WriteLine($"[cidade]   FALHA {oque}   {detalhe}");
	}

	/// <summary>O nome curto da zona de Vegeta no manifesto.</summary>
	private const string ZonaDaCidade = "Vegeta";

	/// <summary>A outra cidade, a do `.dmm`: a familia 5 roda nela o que a 4 roda em Vegeta.</summary>
	private const string ZonaDaTerra = "Earth";

	/// <summary>
	/// O PONTO DE NASCIMENTO de uma zona pre-feita, copiado do servidor (`GameServer.SpawnPos`).
	///
	/// Nao e o `/obj/SpawnPoint` do `.dmm`: o port poe todo mundo no MEIO do mapa e depois procura
	/// chao livre (`PontoLivrePerto`). A familia 4 anda a partir DAQUI, e nao de onde o BYOND
	/// punha o saiyajin -- caminhar de um ponto em que ninguem nasce nao prova nada sobre chegar.
	/// </summary>
	private static readonly Vec2 Nascimento = new(249 * 32 + 16, 250 * 32 + 16);

	public static int Run(string pastaMaps, string pastaCode, string pastaDmm)
	{
		_ok = 0; _falhou = 0;
		Console.WriteLine("=== A CIDADE DE VEGETA E AS PAREDES INVISIVEIS ===\n");

		string manifesto = Path.Combine(pastaMaps, "manifest.json");
		if (!File.Exists(manifesto))
		{
			Console.WriteLine($"sem {manifesto} -- rode o comando 'maps' antes.");
			return 1;
		}
		ZoneCatalog cat = ZoneCatalog.Parse(File.ReadAllText(manifesto));

		string dataDir = Path.Combine(Path.GetDirectoryName(pastaMaps.TrimEnd('/', '\\'))!, "Data");
		string cjArq = Path.Combine(dataDir, "construcoes.json");
		string tjArq = Path.Combine(dataDir, "tiles.json");
		if (!File.Exists(cjArq) || !File.Exists(tjArq))
		{
			Console.WriteLine($"sem {cjArq} ou {tjArq} -- rode o comando 'tech' e depois 'maps'.");
			return 1;
		}
		string cjTexto = File.ReadAllText(cjArq);
		CatalogoDeObras obras = CatalogoDeObras.Parse(cjTexto);
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(File.ReadAllText(tjArq));

		Console.WriteLine("lendo a arvore de tipos DM (icone e icon_state de cada typepath)...");
		Dictionary<string, TurfDef> turfs = DmTurfScanner.Scan(Path.GetFullPath(pastaCode));
		Console.WriteLine("lendo os `.dmm` de origem (quem tinha desenho antes da conversao)...");
		var fonte = new Fonte(pastaDmm, turfs);
		Console.WriteLine($"typepaths: {turfs.Count} | construcoes: {obras.Total} | atlas no tileset: {tiles.Total}"
						  + $" | andares na fonte: {fonte.Niveis}\n");

		Familia1(cat, pastaMaps, fonte);
		Zona? vegeta = Ler(cat, pastaMaps, ZonaDaCidade, comTiles: true);
		Zona? terra = Ler(cat, pastaMaps, ZonaDaTerra, comTiles: false);

		if (vegeta == null) Afirmar($"a zona '{ZonaDaCidade}' existe no manifesto", false);
		else
		{
			Familia2(vegeta, obras);
			Familia3(vegeta, obras, tiles, turfs);
			Familia4(vegeta, obras, "Vegeta");
		}

		if (terra == null) Afirmar($"a zona '{ZonaDaTerra}' existe no manifesto", false);
		else Familia5(terra, obras);

		Familia6(cjTexto, obras);

		Console.WriteLine($"\n[cidade] ===== {_ok} OK, {_falhou} FALHA(S) =====\n");
		return _falhou == 0 ? 0 : 1;
	}

	// =====================================================================
	// OS ARQUIVOS DE UMA ZONA, do jeito que o jogo os le
	// =====================================================================
	private sealed class Zona
	{
		public string Nome = "";
		public int W, H;
		public ZoneCollision Col = null!;

		/// <summary>Celulas em que ALGUMA camada do `.pedacos` desenha alguma coisa.</summary>
		public HashSet<long> Desenhadas = [];

		/// <summary>`(fonte, ax, ay)` de cada camada, por celula. So montado quando pedido.</summary>
		public Dictionary<long, List<(int F, int X, int Y)>>? Quadros;

		public List<ObjetoDoMapa> Maquinas = [];
		public List<PortaDoMapa> Portas = [];

		public long K(int x, int y) => (long)y * W + x;
		public bool Desenha(int x, int y) => Desenhadas.Contains(K(x, y));
	}

	private static Zona? Ler(ZoneCatalog cat, string pastaMaps, string nome, bool comTiles)
	{
		ZoneEntry? e = cat.Get(nome);
		return e == null ? null : Ler(pastaMaps, e, comTiles);
	}

	private static Zona? Ler(string pastaMaps, ZoneEntry e, bool comTiles)
	{
		string arqCol = Local(pastaMaps, e.Colisao);
		string arqPed = Local(pastaMaps, e.Pedacos);
		if (arqCol.Length == 0 || !File.Exists(arqCol)) return null;

		ZoneCollision? col = ZoneCollision.Load(File.ReadAllBytes(arqCol));
		if (col == null) return null;

		// A AGUA VEM JUNTO, e nao e detalhe: esta bancada afirma "da pra chegar no banco ANDANDO",
		// e desde que a agua virou a terceira classe de celula (ver `ClasseDeAgua`) um percurso que
		// atravessa um lago nao existe mais a pe. Sem esta leitura a bancada continuaria verde
		// julgando um mapa que o jogo nao tem -- que e o modo de falha mais caro daqui: os dois
		// lados concordando e os dois errados.
		string arqAgua = Local(pastaMaps, e.CaminhoDaAgua);
		if (arqAgua.Length > 0 && File.Exists(arqAgua)) col.CarregarAgua(File.ReadAllBytes(arqAgua));

		var z = new Zona { Nome = e.Zona, W = col.Width, H = col.Height, Col = col };
		if (comTiles) z.Quadros = [];

		if (File.Exists(arqPed) && PedacosDoMapa.Ler(File.ReadAllBytes(arqPed)) is { } ped)
			for (int c = 0; c < ped.Camadas.Length; c++)
				for (int cy = ped.Cy0; cy < ped.Cy1; cy++)
					for (int cx = ped.Cx0; cx < ped.Cx1; cx++)
					{
						if (!ped.Achar(cx, cy, c, out int ini, out int q)) continue;
						for (int i = 0; i < q; i++)
						{
							CelulaDePedaco cel = ped.Celula(ini, i);
							long k = z.K(cel.X, cel.Y);
							z.Desenhadas.Add(k);
							if (z.Quadros == null) continue;
							if (!z.Quadros.TryGetValue(k, out List<(int, int, int)>? l))
								z.Quadros[k] = l = [];
							l.Add((cel.Fonte, cel.Ax, cel.Ay));
						}
					}

		string arqObj = Local(pastaMaps, e.Objetos);
		if (arqObj.Length > 0 && File.Exists(arqObj))
			z.Maquinas = ObjetosDoMapa.Parse(File.ReadAllText(arqObj));

		string arqPor = Local(pastaMaps, e.Portas);
		if (arqPor.Length > 0 && File.Exists(arqPor))
			z.Portas = PortasDaZona.Parse(File.ReadAllText(arqPor));

		return z;
	}

	private static string Local(string pastaMaps, string res) =>
		res.Length == 0 ? "" : Path.Combine(pastaMaps, Path.GetFileName(res));

	// =====================================================================
	// FAMILIA 1: NADA SOLIDO SEM DESENHO
	// =====================================================================
	/// <summary>
	/// AS CELULAS QUE BLOQUEIAM E QUE NINGUEM DESENHA -- descontada a borda do mundo.
	///
	/// ============================ POR QUE A BORDA PRECISA DE UMA REGRA, E NAO DE UMA EXCECAO ============================
	/// O `.dmm` cerca cada retangulo com `/turf/Other/Blank`: denso, sem icone. Isso e legitimo --
	/// e o jeito do BYOND de dizer "o mapa acaba aqui" -- e sao dezenas de milhares de celulas. Uma
	/// varredura que as contasse como defeito seria vermelha pra sempre e ninguem olharia pra ela.
	///
	/// Mas a MESMA turf aparece no meio do oceano de Hera: uma coluna inteira em x=250, de y=1 a
	/// y=500, com agua nas duas colunas vizinhas -- uma costura que o mapeador deixou ao emendar
	/// dois pedacos. Essa era a queixa do dono, e ela e indistinguivel da borda pelo typepath.
	///
	/// A DIFERENCA E TOPOLOGICA, E ELA SEPARA OS 40 MAPAS SEM UMA EXCECAO ESCRITA A MAO: o vazio tem
	/// MIOLO e a costura nao. Uma regiao que e limite de mundo sempre contem alguma celula cujos
	/// quatro vizinhos tambem sao vazio; uma linha de um tile de espessura nao contem nenhuma.
	///
	///     z11 Hera        500 celulas, miolo 0       -> costura (a queixa do dono)
	///     z12 Lookout  14.315 celulas, miolo 11.536  -> BORDA   (o ceu em volta da plataforma)
	///
	/// O COMPONENTE CRESCE POR CELULA BLOQUEADA, E NAO SO POR CELULA MUDA -- e isto foi medido, nao
	/// escolhido. A primeira versao crescia so pelas mudas e acusou UMA celula em Arconia: (417,198),
	/// um pixel na beirada do vazio do leste, separado do resto dele por uma unica celula que
	/// bloqueia E desenha (o `.dmm` empilha um `/turf/Other/Blank` com uma pedra em cima). Pela
	/// topologia da area ele e vazio; pela topologia das MUDAS ele era uma ilha sem miolo. Uma
	/// varredura que acusa a quina de todo vazio decorado vira ruido, e ruido e como uma bancada
	/// morre.
	///
	/// O PRECO E EXPLICITO: uma celula muda COLADA numa parede desenhada que chegue ate o vazio sai
	/// perdoada por aqui. Quem cobre esse buraco e a <see cref="Costuras"/>, logo abaixo, que nao
	/// depende de conectividade nenhuma.
	///
	/// E A REGRA E REESCRITA AQUI DE PROPOSITO, em vez de importada do `MapConverter`. Se as duas
	/// pontas chamassem a mesma funcao, um erro nela deixaria as duas de acordo -- e "as duas telas
	/// concordam" e como quatro bugs visuais atravessaram quatro mil checagens verdes neste projeto.
	/// A bancada tem que ser capaz de discordar do conversor.
	/// ==============================================================================================================
	/// </summary>
	private static List<(int X, int Y)> Fantasmas(Zona z)
	{
		HashSet<long> mudos = Mudas(z);

		bool EhMudo(int x, int y) => x >= 0 && y >= 0 && x < z.W && y < z.H && mudos.Contains(z.K(x, y));
		bool TemMiolo(int x, int y) => EhMudo(x - 1, y) && EhMudo(x + 1, y) && EhMudo(x, y - 1) && EhMudo(x, y + 1);
		bool Barra(int x, int y) => x >= 0 && y >= 0 && x < z.W && y < z.H && z.Col.BlockedCell(x, y);

		var vistos = new HashSet<long>();
		var fantasmas = new List<(int, int)>();
		var pilha = new Stack<long>();
		foreach (long inicio in mudos)
		{
			if (vistos.Contains(inicio)) continue;

			// o componente e de celulas BLOQUEADAS; so as MUDAS dele e que sao julgadas
			var acusadas = new List<long>();
			bool miolo = false;
			var doComp = new List<long>();
			vistos.Add(inicio);
			pilha.Push(inicio);
			while (pilha.Count > 0)
			{
				long k = pilha.Pop();
				int x = (int)(k % z.W), y = (int)(k / z.W);
				doComp.Add(k);
				if (EhMudo(x, y))
				{
					acusadas.Add(k);
					if (TemMiolo(x, y)) miolo = true;
				}
				foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
				{
					int nx = x + dx, ny = y + dy;
					if (!Barra(nx, ny)) continue;
					long nk = z.K(nx, ny);
					if (vistos.Add(nk)) pilha.Push(nk);
				}
			}
			if (miolo) continue;
			foreach (long k in acusadas) fantasmas.Add(((int)(k % z.W), (int)(k / z.W)));
		}
		return fantasmas;
	}

	/// <summary>As celulas que bloqueiam e que ninguem -- tile, maquina ou porta -- desenha.</summary>
	private static HashSet<long> Mudas(Zona z)
	{
		var maq = new HashSet<long>(z.Maquinas.Select(m => z.K(m.X, m.Y)));
		var por = new HashSet<long>(z.Portas.Select(p => z.K(p.X, p.Y)));

		var mudos = new HashSet<long>();
		for (int y = 0; y < z.H; y++)
			for (int x = 0; x < z.W; x++)
			{
				long k = z.K(x, y);
				if (!z.Col.BlockedCell(x, y) || z.Desenhadas.Contains(k) || maq.Contains(k) || por.Contains(k))
					continue;
				mudos.Add(k);
			}
		return mudos;
	}

	/// <summary>
	/// A COSTURA: uma celula muda com CHAO PISAVEL E DESENHADO dos DOIS lados opostos.
	///
	/// ============================ POR QUE ESTA SEGUNDA REGRA EXISTE ============================
	/// Porque a de cima tem um buraco conhecido (ver o cabecalho dela) e porque esta aqui nao tem
	/// que julgar topologia nenhuma: ela descreve o que o JOGADOR sente. Se da pra estar a oeste
	/// dela e da pra estar a leste dela, e no meio ha alguma coisa que para o corpo e que a tela nao
	/// mostra, entao existe uma parede invisivel ali. Nao ha leitura em que isso seja "o mapa acaba
	/// aqui" -- o mapa continua, e ele continua dos dois lados.
	///
	/// E A QUEIXA DO DONO, literalmente: a coluna de `/turf/Other/Blank` em x=250 de Hera, com
	/// `/turf/Water/Water3` nas duas colunas vizinhas. Agua nos dois lados, nada no meio, quinhentas
	/// celulas de cima a baixo do mapa.
	/// ========================================================================================
	/// </summary>
	private static List<(int X, int Y)> Costuras(Zona z)
	{
		HashSet<long> mudos = Mudas(z);
		var saida = new List<(int, int)>();

		bool Pisavel(int x, int y) =>
			x >= 0 && y >= 0 && x < z.W && y < z.H && !z.Col.BlockedCell(x, y) && z.Desenhadas.Contains(z.K(x, y));

		foreach (long k in mudos)
		{
			int x = (int)(k % z.W), y = (int)(k / z.W);
			if ((Pisavel(x - 1, y) && Pisavel(x + 1, y)) || (Pisavel(x, y - 1) && Pisavel(x, y + 1)))
				saida.Add((x, y));
		}
		return saida;
	}

	/// <summary>
	/// O `.dmm` DE ORIGEM, indexado pelo z do jogo -- a unica testemunha de quem TINHA arte.
	///
	/// ============================ POR QUE A BANCADA PRECISA DA FONTE ============================
	/// A primeira versao desta familia julgava pela geometria: "celula solida que ninguem desenha e
	/// defeito, menos a borda do mundo, que se reconhece por ter miolo". Ela achou 760 celulas em
	/// Arconia e no Lookout -- e as 760 sao `/turf/Other/Blank`, que **no BYOND tambem nao desenha
	/// nada**. Sao as divisorias com que o mapeador partiu um canvas de 500x500 em varias areas: o
	/// jogador do jogo ORIGINAL esbarra exatamente nelas. Chamar isso de defeito do port e cobrar do
	/// port uma coisa que ele copiou certo, e uma bancada que cobra o impossivel e desligada.
	///
	/// A PERGUNTA CERTA NAO E GEOMETRICA, E DE FIDELIDADE: esta celula solida tinha DESENHO na fonte?
	///
	///   * a fonte nao desenha nada ali  -> o BYOND tambem nao desenhava. HERANCA, e nao regressao.
	///   * a fonte TEM icone e o port nao pintou -> o port PERDEU a arte pelo caminho. E o defeito,
	///     e e a familia inteira das quatro ocorrencias que este port ja teve (a bandeira sem `.dmi`
	///     convertido, o `Ship_Control` fora do catalogo, os 35 atlas nunca importados, e qualquer
	///     construcao futura que entre sem sprite).
	///
	/// E ESTA PERGUNTA NAO PRECISA DE NENHUMA LISTA DE EXCECAO -- nem de baseline, nem de "menos o
	/// Lookout", nem de contagem escrita a mao. Ela nasce do dado e envelhece junto com ele.
	/// ==========================================================================================
	/// </summary>
	private sealed class Fonte
	{
		private readonly Dictionary<int, (DmmMap.Result D, DmmLevel N)> _porZ = [];
		private readonly Dictionary<string, TurfDef> _turfs;

		public Fonte(string pastaDmm, Dictionary<string, TurfDef> turfs)
		{
			_turfs = turfs;
			int off = 0;
			foreach (string arq in MapConverter.OrdemDoDme(pastaDmm))
			{
				DmmMap.Result d = DmmMap.Read(arq);
				foreach (DmmLevel n in d.Levels) _porZ[n.Z + off] = (d, n);
				off += d.Levels.Count;
			}
		}

		public int Niveis => _porZ.Count;

		/// <summary>
		/// A fonte DESENHA alguma coisa nesta celula? Nulo quando este z nao veio de `.dmm` nenhum
		/// (o mapa de andar gerado, por exemplo) -- e ai a bancada nao tem o que afirmar.
		/// </summary>
		public bool? Pinta(int z, int x, int y)
		{
			if (!_porZ.TryGetValue(z, out (DmmMap.Result D, DmmLevel N) lv)) return null;
			if (x < 0 || y < 0 || x >= lv.N.Width || y >= lv.N.Height) return null;

			string? k = lv.N.Cells[x, y];
			if (k == null || !lv.D.Keys.TryGetValue(k, out string[]? tipos)) return false;
			foreach (string tp in tipos)
				if (_turfs.TryGetValue(DmmMap.BasePath(tp), out TurfDef? td) && td.Icon != null)
					return true;
			return false;
		}

		/// <summary>
		/// A fonte BLOQUEIA nesta celula? E a pergunta simetrica da <see cref="Pinta"/>, e ela e a que
		/// pega o defeito que "solido e mudo" NAO pega.
		///
		/// ============================ O BURACO QUE ELA TAPA, E ELE E GRANDE ============================
		/// "Celula solida que ninguem desenha" parece cobrir a queixa inteira -- e nao cobre o caso
		/// MAIS PROVAVEL de acontecer de novo. Uma maquina sem sprite posta sobre grama deixa a GRAMA
		/// desenhada: a celula continua tendo desenho, e a varredura de mudas passa por ela sorrindo.
		/// Isto foi medido, e nao imaginado: acender o bit de colisao em (200,200) de Vegeta, que e a
		/// forma exata desse defeito, deixou a bancada 51 OK / 0 FALHAS.
		///
		/// A pergunta que pega e a do DONO DA PAREDE: toda celula que bloqueia tem que ter alguem
		/// respondendo por ela. Sao quatro donos possiveis, e nenhum e opiniao:
		///
		///   * a FONTE tem coisa densa (ou barreira) ali -- o BYOND tambem bloqueia;
		///   * e uma PORTA (`.portas`);
		///   * e uma MAQUINA (`.objetos`);
		///   * ou esta na PLANTA DA CIDADE, que e a unica geometria que o port acrescenta por conta
		///     propria -- e ela e declarada em `CidadeDeVegeta.Planta()`, nao adivinhada.
		///
		/// Fora desses quatro, o port inventou uma parede. Nao ha leitura em que isso seja de proposito.
		/// ===========================================================================================
		/// </summary>
		public bool? Barra(int z, int x, int y)
		{
			if (!_porZ.TryGetValue(z, out (DmmMap.Result D, DmmLevel N) lv)) return null;
			if (x < 0 || y < 0 || x >= lv.N.Width || y >= lv.N.Height) return null;

			string? k = lv.N.Cells[x, y];
			if (k == null || !lv.D.Keys.TryGetValue(k, out string[]? tipos)) return false;
			foreach (string tp in tipos)
			{
				string bp = DmmMap.BasePath(tp);

				// A PASSAGEM E DENSA NO DM E NAO BLOQUEIA AQUI: o `Enter()` teleporta antes de o
				// bloqueio valer. E a mesma excecao que o `MarcarSolidos` do conversor faz -- e ela
				// tem que estar nos DOIS lados, senao a bancada acusaria toda escada do jogo.
				if (Passagens.Eh(bp)) continue;

				// BARREIRA BLOQUEIA SEM SER DENSA (`NOENTER`/`selectivecollide`, barrier.dm:61-62), e o
				// `kaio_gate` fica de fora porque e CONDICIONAL -- mesma regra do conversor.
				if (bp.StartsWith("/obj/barrier/", StringComparison.Ordinal)
					&& !bp.Contains("kaio_gate", StringComparison.OrdinalIgnoreCase)) return true;

				if (_turfs.TryGetValue(bp, out TurfDef? td) && td.Density) return true;
			}
			return false;
		}
	}

	private static void Familia1(ZoneCatalog cat, string pastaMaps, Fonte fonte)
	{
		Console.WriteLine("--- 1. NADA SOLIDO SEM DESENHO (a queixa) ---");
		Console.WriteLine("        a pergunta e de FIDELIDADE: a celula solida e muda TINHA arte na fonte?");

		int zonas = 0, perdidas = 0, heranca = 0, semFonte = 0, totalCosturas = 0, inventadas = 0;
		Zona? vegetaGuardada = null, terraGuardada = null;
		int zVegeta = 3, zTerra = 1;
		foreach (ZoneEntry e in cat.Todas)
		{
			Zona? z = Ler(pastaMaps, e, comTiles: false);
			if (z == null) continue;
			zonas++;

			(int p, int h, int s, List<(int X, int Y)> quais) = Julgar(z, e.Z, fonte);
			perdidas += p; heranca += h; semFonte += s;

			List<(int X, int Y)> inv = Inventadas(z, e.Z, fonte);
			inventadas += inv.Count;
			if (inv.Count > 0)
				Console.WriteLine($"        {z.Nome,-24} {inv.Count,6} PAREDES SEM DONO (nem fonte, nem porta, "
								  + $"nem maquina, nem planta)  -- ex.: {string.Join(" ", inv.Take(5).Select(q => $"({q.X},{q.Y})"))}");

			List<(int X, int Y)> c = Costuras(z);
			totalCosturas += c.Count;

			// GUARDA UM PAR DE VERDADE PRA INJECAO DA FAMILIA 1: uma divisoria herdada e um chao
			// desenhado da mesma zona. Ver a nota em `Injecoes.ParedeInvisivel`.
			if (Injecoes.Divisoria == null && c.Count > 0 && p == 0)
			{
				Injecoes.Divisoria = (z.Nome, e.Z, c[0].X, c[0].Y);
				for (int dy = -3; dy <= 3 && Injecoes.Pintada == null; dy++)
					for (int dx = -3; dx <= 3 && Injecoes.Pintada == null; dx++)
					{
						int nx = c[0].X + dx, ny = c[0].Y + dy;
						if (nx < 0 || ny < 0 || nx >= z.W || ny >= z.H) continue;
						if (z.Col.BlockedCell(nx, ny) || !z.Desenha(nx, ny)) continue;
						Injecoes.Pintada = (z.Nome, e.Z, nx, ny);
					}
			}

			if (p > 0)
				Console.WriteLine($"        {z.Nome,-24} {p,6} PERDERAM A ARTE (a fonte pinta, o port nao)"
								  + $"  -- ex.: {string.Join(" ", quais.Take(5).Select(q => $"({q.X},{q.Y})"))}");
			else if (h > 0 || c.Count > 0)
				Console.WriteLine($"        {z.Nome,-24} {h,6} mudas HERDADAS do .dmm "
								  + $"({c.Count} delas com chao dos dois lados: divisorias do mapeador)");

			if (z.Nome == ZonaDaCidade) { vegetaGuardada = z; zVegeta = e.Z; }
			if (z.Nome == ZonaDaTerra) { terraGuardada = z; zTerra = e.Z; }
		}

		Afirmar($"varri as zonas do manifesto ({zonas})", zonas >= 20, $"{zonas}");
		Afirmar("a fonte foi lida e cobre os andares pre-feitos", fonte.Niveis >= 20, $"{fonte.Niveis}");
		Afirmar("NENHUMA celula solida perdeu a arte que a fonte tem -- em zona nenhuma",
				perdidas == 0, $"{perdidas}");
		Afirmar("TODA celula que bloqueia tem dono: fonte, porta, maquina ou a planta da cidade",
				inventadas == 0, $"{inventadas} sem dono");
		Console.WriteLine($"        {heranca} muda(s) herdadas do `.dmm` ({totalCosturas} sao divisoria com chao "
						  + $"dos dois lados), e {semFonte} sem fonte pra conferir");

		// a queixa e sobre VEGETA; a Terra e a outra cidade. As duas ganham a sua linha, e as duas
		// tem que estar limpas TAMBEM da heranca: cidade nao e canvas partido a divisoria.
		if (vegetaGuardada != null)
			Afirmar("  ...e Vegeta, onde o dono esbarrou, nao tem NENHUMA celula solida e muda",
					Mudas(vegetaGuardada).Count == 0, $"{Mudas(vegetaGuardada).Count}");
		if (terraGuardada != null)
			Afirmar("  ...nem a Terra, que e a outra cidade",
					Mudas(terraGuardada).Count == 0, $"{Mudas(terraGuardada).Count}");

		Injecoes.ParedeInvisivel(vegetaGuardada, zVegeta, terraGuardada, zTerra, fonte);
		Console.WriteLine();
	}

	/// <summary>
	/// Julga as mudas de uma zona contra a fonte: quantas PERDERAM arte, quantas sao HERANCA, e
	/// quantas nao tem fonte pra conferir.
	/// </summary>
	private static (int Perdidas, int Heranca, int SemFonte, List<(int X, int Y)> Quais) Julgar(
		Zona z, int zDoJogo, Fonte fonte)
	{
		int perdidas = 0, heranca = 0, semFonte = 0;
		var quais = new List<(int, int)>();
		foreach (long k in Mudas(z))
		{
			int x = (int)(k % z.W), y = (int)(k / z.W);
			switch (fonte.Pinta(zDoJogo, x, y))
			{
				case true: perdidas++; quais.Add((x, y)); break;
				case false: heranca++; break;
				default: semFonte++; break;
			}
		}
		return (perdidas, heranca, semFonte, quais);
	}

	/// <summary>
	/// AS PAREDES QUE O PORT INVENTOU: celulas que bloqueiam e cujo dono nao existe.
	///
	/// Os quatro donos legitimos estao no cabecalho de <see cref="Fonte.Barra"/>. A planta da cidade
	/// entra aqui como conjunto porque ela e a UNICA geometria que o port acrescenta por conta
	/// propria -- e ela e declarada, entao nao ha nada a adivinhar.
	/// </summary>
	private static List<(int X, int Y)> Inventadas(Zona z, int zDoJogo, Fonte fonte)
	{
		var maq = new HashSet<long>(z.Maquinas.Select(m => z.K(m.X, m.Y)));
		var por = new HashSet<long>(z.Portas.Select(p => z.K(p.X, p.Y)));

		var daCidade = new HashSet<long>();
		if (zDoJogo == CidadeDeVegeta.Z)
			foreach (CidadeDeVegeta.Peca p in CidadeDeVegeta.Planta())
			{
				(int cx, int cy) = CidadeDeVegeta.NoPort(p, z.H);
				daCidade.Add(z.K(cx, cy));
			}

		var saida = new List<(int, int)>();
		for (int y = 0; y < z.H; y++)
			for (int x = 0; x < z.W; x++)
			{
				if (!z.Col.BlockedCell(x, y)) continue;
				long k = z.K(x, y);
				if (maq.Contains(k) || por.Contains(k) || daCidade.Contains(k)) continue;
				if (fonte.Barra(zDoJogo, x, y) != false) continue;   // nulo = sem fonte, nao acusa
				saida.Add((x, y));
			}
		return saida;
	}

	// =====================================================================
	// FAMILIA 2: A RECIPROCA
	// =====================================================================
	/// <summary>
	/// O QUE E DESENHADO COMO PREDIO E QUE SE ATRAVESSA.
	///
	/// A metade esquecida do defeito. Uma parede que nao para o corpo nao gera queixa de "parede
	/// invisivel" -- ela gera "atravessei a parede", que foi LITERALMENTE uma queixa anterior deste
	/// dono ("o banco n tem fisica, eu atravesso ele"). Sao a mesma discordancia entre quem desenha
	/// e quem bloqueia, com o sinal trocado, e uma bancada que so cobre um lado deixa o outro voltar.
	/// </summary>
	private static int Atravessaveis(Zona z, CatalogoDeObras obras, List<string>? detalhe = null)
	{
		int ruim = 0;
		foreach (ObjetoDoMapa m in z.Maquinas)
		{
			Construcao? c = obras.Get(m.Id);
			if (c is not { Densa: true }) continue;
			if (z.Col.BlockedCell(m.X, m.Y)) continue;
			ruim++;
			detalhe?.Add($"{m.Id} em ({m.X},{m.Y})");
		}
		foreach (PortaDoMapa p in z.Portas)
		{
			if (z.Col.BlockedCell(p.X, p.Y)) continue;
			ruim++;
			detalhe?.Add($"porta em ({p.X},{p.Y})");
		}
		return ruim;
	}

	private static void Familia2(Zona z, CatalogoDeObras obras)
	{
		Console.WriteLine("--- 2. A RECIPROCA: nada desenhado como predio que se atravesse ---");

		var quais = new List<string>();
		int ruim = Atravessaveis(z, obras, quais);
		int densas = z.Maquinas.Count(m => obras.Get(m.Id) is { Densa: true });
		Console.WriteLine($"        {z.Nome}: {z.Maquinas.Count} maquina(s) ({densas} densas) e {z.Portas.Count} porta(s)");
		Afirmar("toda maquina densa e toda porta do mapa BLOQUEIAM",
				ruim == 0, string.Join(", ", quais.Take(6)));

		// AS PAREDES E OS CHAOS DA CIDADE, os dois sentidos na mesma passada. A lista vem do
		// construtor -- ver a familia 3.
		int paredeSolta = 0, chaoTrancado = 0;
		foreach (CidadeDeVegeta.Peca p in CidadeDeVegeta.Planta())
		{
			(int x, int y) = CidadeDeVegeta.NoPort(p, z.H);
			if (CidadeDeVegeta.EhParede(p.Turf)) { if (!z.Col.BlockedCell(x, y)) paredeSolta++; }
			else if (p.Turf != CidadeDeVegeta.Porta && p.Obj == null)
			{
				// chao puro: tem que dar pra pisar. Um piso que bloqueia e a mesma parede invisivel
				// -- so que dentro de casa, onde o dono vai passar todo dia.
				if (z.Col.BlockedCell(x, y)) chaoTrancado++;
			}
		}
		Afirmar("as paredes prometidas da cidade BLOQUEIAM", paredeSolta == 0, $"{paredeSolta} soltas");
		Afirmar("o chao prometido da cidade SE ATRAVESSA", chaoTrancado == 0, $"{chaoTrancado} trancados");

		Injecoes.PredioAtravessavel(z, obras);
		Console.WriteLine();
	}

	// =====================================================================
	// FAMILIA 3: A TABELA DA CIDADE
	// =====================================================================
	/// <summary>
	/// CADA ITEM DA CIDADE, COM O SEU NOME E A SUA LINHA -- e a lista vem do construtor.
	///
	/// ============================ ONDE CADA TIPO DE PECA E PROCURADA ============================
	/// As pecas nao moram todas no mesmo arquivo, e e por isso que "854 celulas carimbadas" nao
	/// prova nada:
	///
	///   * MAQUINA (bancada, regenerador) -> `.objetos`, e o servidor as ergue como construcao;
	///   * PORTA                          -> `.portas`, e ela vira entidade no cliente;
	///   * PAREDE, CHAO e MOBILIA         -> `.pedacos`, como celula de tilemap.
	///
	/// E o `.pedacos` nao guarda typepath: ele guarda `(fonte, x, y)` do atlas. Entao a conferencia
	/// aqui e a volta inteira do caminho -- typepath -> `icon`/`icon_state` (a arvore do DM) ->
	/// `(fonte, x, y)` (o `tiles.json`) -> a celula. Conferir so "tem ALGUMA coisa desenhada aqui"
	/// passaria verde com a casa inteira pintada de grama.
	///
	/// A TIRA ANIMADA CONTA COMO A MESMA PECA: quando um estado tem mais de um quadro, o conversor
	/// reaponta a celula pro atlas companheiro `X__anim`. E o mesmo desenho, noutra folha.
	/// ==========================================================================================
	/// </summary>
	private static void Familia3(Zona z, CatalogoDeObras obras, CatalogoDeTiles tiles,
								 Dictionary<string, TurfDef> turfs)
	{
		Console.WriteLine("--- 3. A TABELA: cada peca que o construtor promete ---");

		// fonte -> nome do atlas, pra reconhecer a tira animada companheira
		var nomePorFonte = new Dictionary<int, string>();
		foreach (AtlasDeTiles a in tiles.Todos) nomePorFonte[a.Fonte] = a.Nome;

		var prometidas = new Dictionary<string, List<CidadeDeVegeta.Peca>>(StringComparer.Ordinal);
		var ordem = new List<string>();
		foreach (CidadeDeVegeta.Peca p in CidadeDeVegeta.Planta())
			foreach (string bp in new[] { p.Turf, p.Obj }.Where(s => s != null).Cast<string>())
			{
				if (!prometidas.TryGetValue(bp, out List<CidadeDeVegeta.Peca>? l))
				{
					prometidas[bp] = l = [];
					ordem.Add(bp);
				}
				l.Add(p);
			}

		var maqPorCelula = new Dictionary<long, string>();
		foreach (ObjetoDoMapa m in z.Maquinas) maqPorCelula[z.K(m.X, m.Y)] = m.Id;
		var portaCelulas = new HashSet<long>(z.Portas.Select(p => z.K(p.X, p.Y)));

		int semArteEsperadas = 0;
		foreach (string bp in ordem.OrderBy(s => s, StringComparer.Ordinal))
		{
			List<CidadeDeVegeta.Peca> pecas = prometidas[bp];
			bool ehObj = bp.StartsWith("/obj", StringComparison.Ordinal);
			// so as pecas em que ESTE typepath e o papel principal
			List<CidadeDeVegeta.Peca> minhas = ehObj
				? pecas.Where(p => p.Obj == bp).ToList()
				: pecas.Where(p => p.Turf == bp).ToList();

			turfs.TryGetValue(bp, out TurfDef? td);
			string icone = td?.Icon == null ? "" : td.Icon;
			string estado = td?.IconState ?? "";
			(int Fonte, int X, int Y)? alvo = icone.Length == 0 ? null : tiles.Achar(icone, estado);
			string tira = icone.Length == 0 ? "" : Path.GetFileNameWithoutExtension(icone) + "__anim";

			int achadas = 0;
			string onde;
			if (CidadeDeVegeta.EhMaquina(bp))
			{
				onde = ".objetos";
				string? id = obras.PorTypepath(bp)?.Id;
				foreach (CidadeDeVegeta.Peca p in minhas)
				{
					(int x, int y) = CidadeDeVegeta.NoPort(p, z.H);
					if (id != null && maqPorCelula.GetValueOrDefault(z.K(x, y)) == id) achadas++;
				}
			}
			else if (bp == CidadeDeVegeta.Porta)
			{
				onde = ".portas";
				foreach (CidadeDeVegeta.Peca p in minhas)
				{
					(int x, int y) = CidadeDeVegeta.NoPort(p, z.H);
					if (portaCelulas.Contains(z.K(x, y))) achadas++;
				}
			}
			else
			{
				onde = ".pedacos";
				foreach (CidadeDeVegeta.Peca p in minhas)
				{
					(int x, int y) = CidadeDeVegeta.NoPort(p, z.H);
					if (Desenha(z, nomePorFonte, x, y, alvo, tira)) achadas++;
				}
			}

			bool temArte = alvo != null;
			string veredito = temArte
				? achadas == minhas.Count ? "ok" : "FALTANDO"
				: achadas == 0 ? "sem arte, corretamente AUSENTE" : "SEM ARTE E MESMO ASSIM NO MAPA";

			Console.WriteLine($"        {bp,-38} {achadas,4}/{minhas.Count,-4} {onde,-9} "
							  + $"{(temArte ? $"{Path.GetFileNameWithoutExtension(icone)}:\"{estado}\"" : "-- sem arte --"),-32} {veredito}");

			if (temArte) Afirmar($"  {bp}: as {minhas.Count} pecas estao no mapa", achadas == minhas.Count, $"{achadas}");
			else
			{
				semArteEsperadas++;
				// PECA RECUSADA NAO PODE TER DEIXADO A COLISAO PRA TRAS. E o caso do `rtable`, que
				// no DM tem `density = 1`: carimba-la sem desenho seria fabricar a propria parede
				// invisivel que esta bancada existe pra impedir.
				int bloqueando = minhas.Count(p =>
				{
					(int x, int y) = CidadeDeVegeta.NoPort(p, z.H);
					return z.Col.BlockedCell(x, y);
				});
				Afirmar($"  {bp}: sem arte -- fica FORA do mapa", achadas == 0, $"{achadas} entraram");
				Afirmar($"  {bp}: ...e sem deixar a colisao pra tras", bloqueando == 0, $"{bloqueando} bloqueiam");
			}
		}

		Console.WriteLine($"        ({semArteEsperadas} typepath(s) sem arte na folha -- bug latente do DM, ver o relatorio)");
		Injecoes.PecaSumida(z, obras, tiles, turfs, nomePorFonte);
		Console.WriteLine();
	}

	/// <summary>Esta celula desenha ESTE quadro (ou a tira animada dele)?</summary>
	private static bool Desenha(Zona z, Dictionary<int, string> nomePorFonte, int x, int y,
								(int Fonte, int X, int Y)? alvo, string tira)
	{
		if (alvo == null || z.Quadros == null) return false;
		if (!z.Quadros.TryGetValue(z.K(x, y), out List<(int F, int X, int Y)>? l)) return false;
		foreach ((int f, int ax, int ay) in l)
		{
			if (f == alvo.Value.Fonte && ax == alvo.Value.X && ay == alvo.Value.Y) return true;
			if (tira.Length > 0 && nomePorFonte.GetValueOrDefault(f)?.Equals(tira, StringComparison.OrdinalIgnoreCase) == true)
				return true;
		}
		return false;
	}

	// =====================================================================
	// FAMILIA 4 e 5: O BANCO E ALCANCAVEL
	// =====================================================================
	/// <summary>
	/// DA PRA CHEGAR NO BANCO E MEXER NELE?
	///
	/// ============================ POR QUE "ESTA NO `.objetos`" NAO BASTA ============================
	/// O banco de Vegeta ja esteve nas tres situacoes em que uma bancada ingenua fica verde:
	///   * ele existe no `.dmm` -- e nao chegava ao `.objetos` (o `objeto ??=` da cadeira o engolia);
	///   * a logica dele existe inteira no servidor (`GameServer.Banco.cs`) -- com ZERO bancos no mundo;
	///   * e ele e a UNICA maquina do jogo com `custo: -1`, ou seja, nao da pra fabricar um. Um
	///     saiyajin sem banco em Vegeta nao tem como guardar zeni sem sair do planeta.
	///
	/// Entao a pergunta certa nao e "ele esta na lista", e sim as tres coisas que o jogador faz: eu
	/// ANDO ate la (busca em largura pela colisao de verdade, do ponto de nascimento), eu CHEGO
	/// PERTO o bastante (o `Interacoes.Alcance`, que e o mesmo numero que o servidor cobra) e ele
	/// ACEITA um verbo.
	/// ==============================================================================================
	/// </summary>
	private static void Banco(Zona z, CatalogoDeObras obras, string quem)
	{
		List<ObjetoDoMapa> bancos = z.Maquinas.Where(m => m.Id == "Bank").ToList();
		Afirmar($"{quem}: ha banco no mapa", bancos.Count > 0, $"{z.Maquinas.Count} maquinas, nenhum banco");
		if (bancos.Count == 0) return;

		Construcao? c = obras.Get("Bank");
		Afirmar($"{quem}: o banco esta no catalogo, com arte e densidade",
				c is { Densa: true } && c.Arte.Length > 0, c == null ? "ausente" : $"densa={c.Densa} arte='{c.Arte}'");
		Afirmar($"{quem}: e o catalogo lhe da verbos", Interacoes.De("Bank").Length > 0
				&& Interacoes.Aceita("Bank", "banco_ver"));

		// TODOS OS BANCOS DA ZONA, e nao o primeiro: a Terra tem dois, e um deles poderia estar
		// murado sem que a bancada notasse. "O banco existe" e uma pergunta sobre a lista; "da pra
		// chegar nele" e uma pergunta sobre CADA um.
		int alcancaveis = 0;
		foreach (ObjetoDoMapa b in bancos)
		{
			bool bloqueia = z.Col.BlockedCell(b.X, b.Y);
			(int px, int py, int passos) = Caminhar(z, b);
			if (passos >= 0 && bloqueia) alcancaveis++;
			Console.WriteLine($"        {quem}: banco em ({b.X},{b.Y}) -- bloqueia={(bloqueia ? "sim" : "NAO")}, "
							  + (passos < 0 ? "SEM CAMINHO do nascimento"
											: $"a {passos} passo(s) do nascimento, de pe em ({px},{py})"));
			if (passos >= 0)
				Afirmar($"{quem}: ({b.X},{b.Y}) -- dali o verbo alcanca (|dx|,|dy| <= {Interacoes.Alcance:0})",
						Alcanca(px, py, b), $"({px},{py})");
		}
		Afirmar($"{quem}: todo banco do mapa bloqueia E da pra chegar nele andando",
				alcancaveis == bancos.Count, $"{alcancaveis}/{bancos.Count}");
	}

	/// <summary>
	/// A CONTA DO ALCANCE E A DO SERVIDOR, campo por campo (`ObraQueAceita`): a maquina fica no meio
	/// da celula com o deslocamento dos pes descontado, e o corpo idem. Refazer a conta "por cima"
	/// aqui daria um raio parecido e um veredito diferente exatamente na beirada, que e onde o
	/// jogador reclama.
	/// </summary>
	private static bool Alcanca(int px, int py, ObjetoDoMapa b)
	{
		const int t = ZoneCollision.TileSize;
		float mx = b.X * t + t / 2f, my = b.Y * t + t / 2f - MoveRules.FeetOffsetY;
		float cx = px * t + t / 2f, cy = py * t + t / 2f - MoveRules.FeetOffsetY;
		return Math.Abs(mx - cx) <= Interacoes.Alcance && Math.Abs(my - cy) <= Interacoes.Alcance;
	}

	/// <summary>
	/// BUSCA EM LARGURA pela colisao de verdade, do ponto de nascimento ate a primeira celula livre
	/// de onde o banco esteja ao alcance. Devolve `(-1,-1,-1)` quando nao ha caminho.
	/// </summary>
	private static (int X, int Y, int Passos) Caminhar(Zona z, ObjetoDoMapa alvo)
	{
		Vec2 nasce = z.Col.PontoLivrePerto(Nascimento);
		int sx = (int)MathF.Floor(nasce.X / ZoneCollision.TileSize);
		int sy = (int)MathF.Floor(nasce.Y / ZoneCollision.TileSize);
		if (z.Col.BlockedCell(sx, sy)) return (-1, -1, -1);

		// ============================ A PORTA FECHADA NAO E PAREDE, E ISTO A BANCADA APRENDEU APANHANDO ============================
		// A primeira versao daqui tratava o `.col` como verdade final e disse que o banco da TERRA
		// era inalcancavel. Ele nao e: ele fica DENTRO de um predio, e a porta em (73,262) bloqueia
		// no arquivo de proposito -- ela abre quando alguem chega de frente (`PortasDaZona.VaiEntrar`,
		// provado pela bancada `portas`). Andar so por celula livre e andar por um mundo em que
		// nenhuma porta existe, e nesse mundo tudo que e de dentro de casa fica inalcancavel.
		// ====================================================================================================================
		var portas = new HashSet<long>(z.Portas.Select(p => z.K(p.X, p.Y)));

		var visto = new HashSet<long> { z.K(sx, sy) };
		var fila = new Queue<(int X, int Y, int D)>();
		fila.Enqueue((sx, sy, 0));
		while (fila.Count > 0)
		{
			(int x, int y, int d) = fila.Dequeue();
			if (Alcanca(x, y, alvo)) return (x, y, d);
			foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
			{
				int nx = x + dx, ny = y + dy;
				if (nx < 0 || ny < 0 || nx >= z.W || ny >= z.H) continue;
				long nk = z.K(nx, ny);
				if (z.Col.BlockedCell(nx, ny) && !portas.Contains(nk)) continue;
				if (!visto.Add(nk)) continue;
				fila.Enqueue((nx, ny, d + 1));
			}
		}
		return (-1, -1, -1);
	}

	private static void Familia4(Zona z, CatalogoDeObras obras, string quem)
	{
		Console.WriteLine("--- 4. O BANCO E ALCANCAVEL (Vegeta) ---");
		Banco(z, obras, quem);

		// AS MAQUINAS DA CIDADE tambem tem que estar de pe -- o construtor promete 8 bancadas e o
		// regenerador, e uma bancada de pesquisa que o mapa nao entrega e o mesmo buraco do banco.
		int prometidas = CidadeDeVegeta.Planta().Count(p => p.Obj != null && CidadeDeVegeta.EhMaquina(p.Obj));
		int deLab = z.Maquinas.Count(m => m.Id == "Research_Station");
		Console.WriteLine($"        a cidade promete {prometidas} maquina(s); no mapa ha {deLab} bancada(s) de pesquisa "
						  + $"e {z.Maquinas.Count(m => m.Id == "Regenerator")} regenerador(es)");

		Injecoes.BancoIlhado(z, obras);
		Console.WriteLine();
	}

	private static void Familia5(Zona z, CatalogoDeObras obras)
	{
		Console.WriteLine("--- 5. A MESMA VARREDURA NA OUTRA CIDADE (Terra) ---");
		Banco(z, obras, "Terra");
		Afirmar("Terra: nenhuma maquina densa se atravessa", Atravessaveis(z, obras) == 0);
		Console.WriteLine();
	}

	// =====================================================================
	// FAMILIA 6: O ALARME DA CARGA
	// =====================================================================
	private static void Familia6(string cjTexto, CatalogoDeObras obras)
	{
		Console.WriteLine("--- 6. O ALARME: construcao que bloqueia sem arte tem que APARECER ---");

		List<Construcao> mudas = obras.SemDesenho().ToList();
		Afirmar("o catalogo publicado nao tem construcao densa e muda",
				mudas.Count == 0, string.Join(", ", mudas.Select(c => c.Id)));

		int densas = obras.Todas.Count(c => c.Densa);
		int comArte = obras.Todas.Count(c => c.Arte.Length > 0);
		Console.WriteLine($"        {obras.Total} no catalogo | {densas} bloqueiam | {comArte} tem arte");

		Injecoes.ConstrucaoMuda(cjTexto);
		Console.WriteLine();
	}

	// =====================================================================
	// AS INJECOES
	// =====================================================================
	/// <summary>
	/// O DEFEITO POSTO DE PROPOSITO, uma familia por vez.
	///
	/// ============================ POR QUE ELAS SAO CHECAGEM, E NAO COMENTARIO ============================
	/// "A bancada passou" e uma afirmacao sobre o codigo; "a bancada sabe reprovar" e uma afirmacao
	/// sobre a BANCADA, e so a segunda vale alguma coisa quando tudo esta verde. Este projeto ja
	/// pagou por isso: quatro defeitos visuais atravessaram quatro mil checagens verdes porque as
	/// checagens mediam o que o codigo ESCREVEU, e nao o que aparecia.
	///
	/// Cada injecao abaixo estraga uma COPIA em memoria (a colisao volta ao arquivo pelo
	/// `FecharTudo`/`LimparObras`, e o catalogo injetado e um texto novo), roda a MESMA funcao que a
	/// familia roda, e cobra o vermelho. Nenhum arquivo do disco e tocado.
	///
	/// A INJECAO DA BORDA e a mais importante das seis, e e a unica que cobra VERDE: ela prova que a
	/// familia 1 nao virou "toda celula sem desenho e defeito". Sem ela, bastaria a regra topologica
	/// estar quebrada pra todo o resto do arquivo ficar vermelho -- ou, pior, pra ela passar a
	/// aceitar tudo e nunca mais achar uma costura.
	/// ===================================================================================================
	/// </summary>
	private static class Injecoes
	{
		/// <summary>
		/// Familia 1: a construcao que entra sem sprite, e a divisoria herdada que NAO pode reprovar.
		///
		/// AS DUAS METADES SAO IGUALMENTE NECESSARIAS. A primeira prova que a familia acusa; a
		/// segunda prova que ela nao acusa TUDO -- e sem esta segunda metade a regra poderia ter
		/// virado "toda celula solida e muda e defeito", que fica vermelha em 760 celulas que o
		/// BYOND tambem nao desenha e que seriam impossiveis de consertar.
		/// </summary>
		public static void ParedeInvisivel(Zona? vegeta, int zVegeta, Zona? terra, int zTerra, Fonte fonte)
		{
			foreach ((Zona? z, int zj) in new[] { (vegeta, zVegeta), (terra, zTerra) })
			{
				if (z == null) continue;

				// --- DEFEITO 1: a celula perdeu o desenho que a fonte tem ---
				(int fx, int fy) = Livre(z, 200, 200);
				z.Col.Bloquear(fx, fy);
				bool desenhava = z.Desenhadas.Remove(z.K(fx, fy));

				(int perdidas, _, _, List<(int X, int Y)> quais) = Julgar(z, zj, fonte);
				Afirmar($"[injecao] {z.Nome}: celula solida que PERDEU o desenho da fonte ({fx},{fy}) REPROVA",
						quais.Contains((fx, fy)), $"{perdidas} acusadas, nenhuma ali");
				// ...e as duas leituras geometricas a acham sozinhas tambem, cada uma pelo seu lado
				Afirmar($"[injecao] {z.Nome}: ...a leitura topologica (sem miolo) acha a mesma celula",
						Fantasmas(z).Contains((fx, fy)));
				Afirmar($"[injecao] {z.Nome}: ...e a da costura (chao dos dois lados) tambem",
						Costuras(z).Contains((fx, fy)));
				if (desenhava) z.Desenhadas.Add(z.K(fx, fy));

				// --- DEFEITO 2: A MAQUINA SEM SPRITE SOBRE CHAO DESENHADO ---
				// Este e o caso que as tres afirmacoes acima NAO pegam, e e o mais provavel de
				// acontecer de novo: o chao continua pintado embaixo dela. So a pergunta do DONO DA
				// PAREDE responde. Medido: com a celula ainda desenhada, as tres de cima ficam verdes.
				Afirmar($"[injecao] {z.Nome}: ...e com o chao AINDA desenhado as tres de cima passam batido",
						Julgar(z, zj, fonte).Perdidas == 0 && !Fantasmas(z).Contains((fx, fy)));
				Afirmar($"[injecao] {z.Nome}: uma parede SEM DONO em ({fx},{fy}) REPROVA",
						Inventadas(z, zj, fonte).Contains((fx, fy)));
				z.Col.LimparObras();
			}

			// --- O LEGITIMO: as divisorias que o `.dmm` ja traz nao podem reprovar ---
			//
			// As DUAS celulas abaixo sao ACHADAS pela varredura da familia 1, e nao escritas aqui:
			// uma divisoria de verdade (das 760) e um chao desenhado de verdade. Cravar coordenadas
			// faria esta prova envelhecer no primeiro remapeamento, e o modo de falha seria a
			// bancada afirmando "a fonte nao pinta ali" sobre uma celula que nem existe mais.
			if (Divisoria is { } dv)
				Afirmar($"[injecao] a fonte responde 'nao pinta' na divisoria herdada de {dv.Zona} ({dv.X},{dv.Y})",
						fonte.Pinta(dv.Z, dv.X, dv.Y) == false, $"{fonte.Pinta(dv.Z, dv.X, dv.Y)}");
			else Afirmar("[injecao] achei uma divisoria herdada pra conferir", false);

			if (Pintada is { } pc)
				Afirmar($"[injecao] ...e responde 'pinta' no chao desenhado de {pc.Zona} ({pc.X},{pc.Y})",
						fonte.Pinta(pc.Z, pc.X, pc.Y) == true, $"{fonte.Pinta(pc.Z, pc.X, pc.Y)}");
			else Afirmar("[injecao] achei um chao desenhado pra conferir", false);
		}

		/// <summary>Uma divisoria herdada de verdade, achada pela varredura -- ver a nota acima.</summary>
		public static (string Zona, int Z, int X, int Y)? Divisoria;

		/// <summary>Um chao desenhado de verdade, da MESMA zona da divisoria.</summary>
		public static (string Zona, int Z, int X, int Y)? Pintada;

		/// <summary>Familia 2: a maquina densa que deixou de bloquear -- "eu atravesso o banco".</summary>
		public static void PredioAtravessavel(Zona z, CatalogoDeObras obras)
		{
			ObjetoDoMapa? densa = z.Maquinas
				.Cast<ObjetoDoMapa?>()
				.FirstOrDefault(m => obras.Get(m!.Value.Id) is { Densa: true } && z.Col.BlockedCell(m.Value.X, m.Value.Y));
			if (densa == null) { Afirmar("[injecao] ha maquina densa em Vegeta pra estragar", false); return; }

			z.Col.Abrir(densa.Value.X, densa.Value.Y);
			int ruim = Atravessaveis(z, obras);
			Afirmar($"[injecao] a maquina '{densa.Value.Id}' deixando de bloquear REPROVA", ruim > 0, $"{ruim}");
			z.Col.FecharTudo();
		}

		/// <summary>Familia 3: uma bancada de pesquisa apagada do `.objetos`.</summary>
		public static void PecaSumida(Zona z, CatalogoDeObras obras, CatalogoDeTiles tiles,
									  Dictionary<string, TurfDef> turfs, Dictionary<int, string> nomePorFonte)
		{
			const string bp = "/obj/Technology/Research_Station";
			string? id = obras.PorTypepath(bp)?.Id;
			var celulas = new HashSet<long>(z.Maquinas.Where(m => m.Id == id).Select(m => z.K(m.X, m.Y)));
			int antes = Contar(z, bp, celulas);

			ObjetoDoMapa? vitima = z.Maquinas.Cast<ObjetoDoMapa?>().FirstOrDefault(m => m!.Value.Id == id);
			if (vitima == null) { Afirmar($"[injecao] ha '{id}' no mapa pra apagar", false); return; }
			celulas.Remove(z.K(vitima.Value.X, vitima.Value.Y));
			int depois = Contar(z, bp, celulas);

			Afirmar($"[injecao] apagar uma bancada do .objetos REPROVA a tabela",
					depois == antes - 1 && antes > 0, $"{antes} -> {depois}");

			// ...e a peca de TILE (a parede) sumindo do `.pedacos` tambem
			const string muro = "/turf/CastleWall/Center";
			turfs.TryGetValue(muro, out TurfDef? td);
			(int, int, int)? alvo = td?.Icon == null ? null : tiles.Achar(td.Icon, td.IconState ?? "");
			string tira = td?.Icon == null ? "" : Path.GetFileNameWithoutExtension(td.Icon) + "__anim";
			CidadeDeVegeta.Peca? parede = CidadeDeVegeta.Planta().Cast<CidadeDeVegeta.Peca?>()
				.FirstOrDefault(p => p!.Value.Turf == muro);
			if (parede == null || alvo == null) { Afirmar("[injecao] ha parede prometida pra apagar", false); return; }

			(int px, int py) = CidadeDeVegeta.NoPort(parede.Value, z.H);
			bool viaAntes = Desenha(z, nomePorFonte, px, py, alvo, tira);
			List<(int F, int X, int Y)>? guarda = z.Quadros?.GetValueOrDefault(z.K(px, py));
			z.Quadros?.Remove(z.K(px, py));
			bool viaDepois = Desenha(z, nomePorFonte, px, py, alvo, tira);
			Afirmar($"[injecao] apagar o desenho da parede ({px},{py}) REPROVA a tabela",
					viaAntes && !viaDepois, $"antes={viaAntes} depois={viaDepois}");
			if (guarda != null && z.Quadros != null) z.Quadros[z.K(px, py)] = guarda;
		}

		private static int Contar(Zona z, string bp, HashSet<long> celulas) =>
			CidadeDeVegeta.Planta()
				.Where(p => p.Obj == bp)
				.Count(p => { (int x, int y) = CidadeDeVegeta.NoPort(p, z.H); return celulas.Contains(z.K(x, y)); });

		/// <summary>Familia 4: o banco emparedado -- existe, tem arte, e ninguem chega nele.</summary>
		public static void BancoIlhado(Zona z, CatalogoDeObras obras)
		{
			ObjetoDoMapa? banco = z.Maquinas.Cast<ObjetoDoMapa?>().FirstOrDefault(m => m!.Value.Id == "Bank");
			if (banco == null) { Afirmar("[injecao] ha banco em Vegeta pra emparedar", false); return; }

			// um anel solido de raio 3 em volta dele: o BFS nao pode furar
			ObjetoDoMapa b = banco.Value;
			for (int d = -3; d <= 3; d++)
			{
				z.Col.Bloquear(b.X + d, b.Y - 3); z.Col.Bloquear(b.X + d, b.Y + 3);
				z.Col.Bloquear(b.X - 3, b.Y + d); z.Col.Bloquear(b.X + 3, b.Y + d);
			}
			(_, _, int passos) = Caminhar(z, b);
			Afirmar("[injecao] o banco emparedado REPROVA (existe, tem arte, e ninguem chega)",
					passos < 0, $"chegou em {passos} passo(s)");
			z.Col.LimparObras();
		}

		/// <summary>
		/// Familia 6: uma construcao densa e sem arte enfiada no catalogo -- PELO PARSER DE VERDADE.
		///
		/// A injecao e um bloco de texto acrescentado ao `construcoes.json` e re-lido pelo
		/// `CatalogoDeObras.Parse`, e nao um objeto montado na mao: o que o servidor le no boot e
		/// texto, e um catalogo montado em memoria nao exercita o parser -- que ja e por onde um
		/// campo pode se perder calado.
		/// </summary>
		public static void ConstrucaoMuda(string cjTexto)
		{
			const string muda = "{ \"id\": \"TESTE_Parede_Fantasma\", \"nome\": \"parede fantasma\", "
							  + "\"desc\": \"injetada pela bancada\", \"custo\": 1, \"tech\": 0, \"racas\": [], "
							  + "\"arte\": \"\", \"estado\": \"\", \"densa\": 1, \"px\": 0, \"py\": 0, "
							  + "\"tipo\": \"/obj/TESTE/ParedeFantasma\" }";

			int fim = cjTexto.LastIndexOf(']');
			string sujo = fim < 0 ? cjTexto : cjTexto[..fim].TrimEnd().TrimEnd(',') + ",\n  " + muda + "\n]";

			CatalogoDeObras injetado = CatalogoDeObras.Parse(sujo);
			List<Construcao> mudas = injetado.SemDesenho().ToList();
			Afirmar("[injecao] uma construcao densa e sem arte no catalogo REPROVA",
					mudas.Any(c => c.Id == "TESTE_Parede_Fantasma"), $"{mudas.Count} acusadas");

			// ...e a MESMA construcao COM arte nao pode reprovar -- senao o alarme e so um contador
			CatalogoDeObras comArte = CatalogoDeObras.Parse(
				sujo.Replace("\"arte\": \"\"", "\"arte\": \"res://Assets/Sprites/Misc/x.tres\""));
			Afirmar("[injecao] ...e a mesma construcao COM arte nao reprova",
					!comArte.SemDesenho().Any(c => c.Id == "TESTE_Parede_Fantasma"));
		}

		/// <summary>A primeira celula livre a partir de (x,y) -- a injecao precisa de chao, nao de parede.</summary>
		private static (int X, int Y) Livre(Zona z, int x, int y)
		{
			for (int r = 0; r < 200; r++)
				for (int dy = -r; dy <= r; dy++)
					for (int dx = -r; dx <= r; dx++)
					{
						int nx = x + dx, ny = y + dy;
						if (nx < 5 || ny < 5 || nx >= z.W - 10 || ny >= z.H - 10) continue;
						if (z.Col.BlockedCell(nx, ny)) continue;
						// e com folga em volta, pra o bloco 5x5 da segunda metade caber em chao livre
						bool folga = true;
						for (int a = 0; a < 5 && folga; a++)
							for (int b = 0; b < 5 && folga; b++)
								if (z.Col.BlockedCell(nx + a, ny + b)) folga = false;
						if (folga) return (nx, ny);
					}
			return (x, y);
		}
	}
}
