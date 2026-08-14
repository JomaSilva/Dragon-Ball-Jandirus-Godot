namespace Jandirus.Tools;

/// <summary>
/// A CIDADE DE VEGETA -- dois laboratorios, seis casas mobiliadas e o regenerador.
///
/// ============================ POR QUE ELA NAO VINHA NO `.dmm` ============================
/// Ela e o UNICO cenario do jogo inteiro que o original nao guarda no mapa: `World.dm:112` chama
/// `Build_Vegeta_Structures()` (`Code/Modules/Globals/VegetaCity.dm:64`) a cada boot, e o proc
/// CARIMBA 854 celulas em z3 com `new /turf/...(locate(x,y,3))`. Varri o DM atras de outro
/// construtor assim e nao ha nenhum: Terra, Namek e o resto vem 100% do `.dmm`, e os outros procs
/// de boot (`Build_Sky_NPCs`, `Build_Planet_Population`) poem MOB, nao cenario.
///
/// Como o port so le o `.dmm`, o bairro simplesmente nunca existiu aqui. O dono viu o buraco pelo
/// lado de dentro: "falta itens como BANCO etc no planeta VEGETA" -- sessenta tiles a oeste do
/// ponto onde ele nasce ha um campo vazio onde o original tem duas casas e um laboratorio.
///
/// ============================ POR QUE CARIMBAR NA CONVERSAO, E NAO NO BOOT DO SERVIDOR ============================
/// A alternativa era repetir o `Build_Vegeta_Structures` em C# no boot do servidor. Ela custa muito
/// mais e entrega menos:
///
///   * PAREDE precisa de DESENHO. O tile so nasce no `.pedacos`, que sai daqui; um muro criado em
///     runtime teria que virar override de celula no `ZoneCollision` (como as portas) E arranjar
///     alguem pra desenha-lo -- duas camadas novas pra pintar chao de casa.
///   * As MAQUINAS ja tem caminho pronto: `Obras.PorTypepath` reconhece a bancada e o regenerador
///     no meio do mapa e os manda pro `.objetos`, de onde o servidor os ergue
///     (`GameServer.CarregarObjetosDoMapa`). Carimbando aqui, elas entram por esse caminho sem uma
///     linha nova.
///   * A PORTA idem: vai pro `.portas` e vira entidade.
///
/// DETERMINISMO: isto e funcao pura do `.dmm` mais as constantes abaixo -- o mesmo mapa entra, o
/// mesmo mapa sai. E carimba SO onde hoje ha chao nu (medido: as 854 celulas sao `DirtHD2`,
/// `GrassHD3` e `GrassHD1`, sem um unico `/obj`), entao nao move nada que ja estivesse la.
/// ==============================================================================================
/// </summary>
public static class CidadeDeVegeta
{
	/// <summary>O z do jogo em que a cidade e erguida -- `var/oz = 3` (VegetaCity.dm:68).</summary>
	public const int Z = 3;

	// ---- as plantas, copiadas letra por letra de VegetaCity.dm:74-97 ----
	// "." = chao, "+" = porta, " " = nao encosta na celula.

	/// <summary>A casa saiyajin (11x9): cama, comodas, mesas, piano, estantes e janelas.</summary>
	private static readonly string[] Casa =
	[
		"WWWOWWWOWWW",
		"Wb..d..r.sW",
		"Wb.....r.sW",
		"W.........W",
		"O....p....O",
		"W.........W",
		"Wk.......kW",
		"W.........W",
		"WWWWW+WWWWW",
	];

	/// <summary>O laboratorio (13x10): bancadas, computadores, arquivos e estantes.</summary>
	private static readonly string[] Lab =
	[
		"WWWWOWWWOWWWW",
		"W..L.m.m.L..W",
		"W...........W",
		"WF.........FW",
		"O...........O",
		"Wk.........kW",
		"W...........W",
		"W..L.m.m.L..W",
		"W...........W",
		"WWWWW+WWWWWWW",
	];

	/// <summary>As paredes de pedra de castelo -- `VEG_WALL` (VegetaCity.dm:11-16).</summary>
	private static string? Parede(char c) => c switch
	{
		'W' => "/turf/CastleWall/Center",
		'O' => "/turf/CastleWall/Window2",
		'T' => "/turf/CastleWall/Torch2",
		'B' => "/turf/CastleWall/Banner2",
		_ => null,
	};

	/// <summary>A mobilia -- `VEG_FURN` (VegetaCity.dm:18-28).</summary>
	private static string? Movel(char c) => c switch
	{
		'b' => "/obj/buildables/Bed",
		'k' => "/obj/buildables/Bookshelf",
		'd' => "/obj/buildables/drawer",
		's' => "/obj/buildables/stool",
		'r' => "/obj/buildables/rtable",
		'p' => "/obj/buildables/piano",
		'L' => "/obj/Technology/Research_Station",
		'm' => "/obj/buildables/Computer2",
		'F' => "/obj/buildables/Files",
		_ => null,
	};

	public const string Porta = "/turf/Door/Door2";
	public const string ChaoDeCasa = "/turf/CastleFloor/Carpet";   // `carpet`, VegetaCity.dm:69
	public const string ChaoDeLab = "/turf/Tile/Tile16";           // `labfl`,  VegetaCity.dm:70
	public const string Regenerador = "/obj/items/Regenerator";    // VegetaCity.dm:106

	/// <summary>
	/// A parede de castelo bloqueia; o chao e a porta nao. Publico porque a bancada precisa da MESMA
	/// resposta -- ela e quem cobra "parede prometida bloqueia" e "chao prometido se atravessa", e
	/// uma segunda tabela de "quais typepaths sao parede" seria a primeira a envelhecer.
	/// </summary>
	public static bool EhParede(string turf) =>
		turf.StartsWith("/turf/CastleWall/", StringComparison.Ordinal);

	/// <summary>
	/// Maquina (vai pro `.objetos` e vira construcao de pe) contra mobilia (fica sendo tile).
	///
	/// O `/obj/items/Regenerator` esta aqui junto do `/obj/Technology/` porque o catalogo de obras o
	/// reconhece igual: quem separa maquina de movel no port e o `Obras.PorTypepath`, e nao a pasta
	/// do typepath. A bancada usa esta mesma linha pra saber ONDE procurar cada peca.
	/// </summary>
	public static bool EhMaquina(string obj) =>
		obj.StartsWith("/obj/Technology/", StringComparison.Ordinal) || obj == Regenerador;

	/// <summary>Uma estampa: a planta, o canto SUDOESTE em coordenada BYOND, e o chao dela.</summary>
	private readonly record struct Estampa(string[] Planta, int Ox, int Oy, string Chao);

	/// <summary>
	/// Onde cada predio fica -- VegetaCity.dm:100-101 (labs) e 116-118 (casas).
	///
	/// A ORDEM E FIXA porque ela decide a ordem em que as chaves novas do `.dmm` nascem, e chave
	/// nova e o que muda o arquivo de saida. Uma lista sorteada daria um `.pedacos` diferente a
	/// cada rodada com o mesmo mapa de entrada.
	/// </summary>
	private static readonly Estampa[] Predios =
	[
		new(Lab, 60, 333, ChaoDeLab),
		new(Lab, 60, 318, ChaoDeLab),
		new(Casa, 78, 338, ChaoDeCasa),
		new(Casa, 78, 320, ChaoDeCasa),
		new(Casa, 92, 338, ChaoDeCasa),
		new(Casa, 92, 320, ChaoDeCasa),
		new(Casa, 106, 338, ChaoDeCasa),
		new(Casa, 106, 320, ChaoDeCasa),
	];

	/// <summary>O que o carimbo fez -- e o que ele NAO conseguiu fazer, que e o que importa.</summary>
	public sealed record Relatorio(
		int Celulas, int Paredes, int Portas, int Moveis, int Maquinas, List<string> SemArte);

	/// <summary>
	/// UMA PECA PROMETIDA: onde ela fica, em coordenada BYOND, e o que ela e.
	///
	/// `Obj` nulo quer dizer chao puro (ou parede, ou porta -- o que estiver em <see cref="Turf"/>).
	/// </summary>
	public readonly record struct Peca(int Bx, int By, string Turf, string? Obj);

	/// <summary>
	/// A PROMESSA DO CONSTRUTOR, celula a celula, SEM tocar em mapa nenhum.
	///
	/// ============================ POR QUE ISTO E UMA FUNCAO SEPARADA ============================
	/// Porque a bancada precisa da MESMA lista que o carimbo usa, e a unica forma honesta de ela ter
	/// isso e as duas lerem daqui. A alternativa -- a bancada com um `string[]` de "o que tem que
	/// existir em Vegeta" -- e uma segunda verdade sobre a cidade: no dia em que alguem acrescentar
	/// uma casa, o carimbo a ergue e a bancada continua verde sem nunca ter olhado pra ela. Lista
	/// escrita a mao apodrece calada, e uma bancada que apodrece calada e pior que bancada nenhuma.
	///
	/// Ela e PURA de proposito: nao consulta arte, nao consulta o `.dmm`, nao filtra nada. O que ela
	/// devolve e o que o `Build_Vegeta_Structures` do DM PROMETE. Quem decide o que consegue virar
	/// pixel e o <see cref="Erguer"/> (que tem o `temArte` na mao) -- e a bancada cobra JUSTAMENTE a
	/// diferenca entre as duas listas: peca prometida e nao entregue tem que estar ausente do mapa e
	/// NAO pode ter sobrado a colisao dela.
	/// ==========================================================================================
	/// </summary>
	public static IEnumerable<Peca> Planta()
	{
		foreach (Estampa e in Predios)
		{
			int h = e.Planta.Length;
			for (int r = 0; r < h; r++)
			{
				string linha = e.Planta[r];
				// `var/yy = oy + (h - r)` com r contando de 1 no DM -- a PRIMEIRA linha e o norte.
				int by = e.Oy + (h - 1 - r);
				for (int c = 0; c < linha.Length; c++)
				{
					char ch = linha[c];
					if (ch == ' ') continue;
					int bx = e.Ox + c;

					if (Parede(ch) is { } muro) { yield return new Peca(bx, by, muro, null); continue; }
					if (ch == '+') { yield return new Peca(bx, by, Porta, null); continue; }
					yield return new Peca(bx, by, e.Chao, Movel(ch));
				}
			}
		}

		// O REGENERADOR no meio do primeiro laboratorio (VegetaCity.dm:104-114). Ele vem DEPOIS das
		// estampas, e a ordem importa: a celula dele ja foi assentada pelo carimbo do lab, e este
		// segundo passe so acrescenta o obj. No original ele nasce com TODOS os upgrades no talo;
		// aqui ele nasce como o port sabe faze-lo, porque o port ainda nao tem niveis por maquina.
		yield return new Peca(66, 338, ChaoDeLab, Regenerador);
	}

	/// <summary>
	/// A CONVERSAO DE COORDENADA, numa casa so.
	///
	/// O eixo Y INVERTE: no BYOND ele cresce pra cima e o `DmmLevel` guarda as linhas na ordem do
	/// arquivo. E a mesma conta do `Destinos` (`altura - by`). Errar aqui poe a cidade espelhada em
	/// cima do oceano sem sintoma nenhum a nao ser "nao esta la" -- e poria a BANCADA olhando pro
	/// mesmo lugar errado, concordando com o defeito. Por isso ela e uma funcao, e nao duas contas.
	/// </summary>
	public static (int X, int Y) NoPort(Peca p, int altura) => (p.Bx - 1, altura - p.By);

	/// <summary>
	/// CARIMBA A CIDADE no andar dado, mexendo no dado lido do `.dmm` antes de qualquer conversao.
	///
	/// <paramref name="temArte"/> responde "este typepath consegue virar pixel?". ELE E O FREIO,
	/// e existe por uma regra que este port ja pagou caro pra aprender: nada pode ser SOLIDO E
	/// INVISIVEL. Duas pecas da cidade tem `icon_state` que a folha nao tem -- uma delas
	/// (`/obj/buildables/rtable`) e DENSA --, e carimba-las produziria exatamente a parede
	/// fantasma que estamos consertando. Sem arte, a peca NAO ENTRA e vai pro relatorio.
	/// </summary>
	public static Relatorio Erguer(DmmMap.Result dados, DmmLevel nivel, Func<string, bool> temArte)
	{
		var semArte = new List<string>();
		var conferidas = new Dictionary<string, bool>(StringComparer.Ordinal);

		bool Da(string bp)
		{
			if (conferidas.TryGetValue(bp, out bool ja)) return ja;
			bool pode = temArte(bp);
			conferidas[bp] = pode;
			if (!pode) semArte.Add(bp);
			return pode;
		}

		// AS PAREDES SAO CONFERIDAS ANTES DE UM TIJOLO SER POSTO. Uma casa com metade das paredes
		// e pior que casa nenhuma: os buracos seriam solidos ou nao conforme a peca, e o dono
		// veria um comodo aberto que nao da pra atravessar. Faltando estrutura, nao se ergue nada.
		string[] estruturais = [ChaoDeCasa, ChaoDeLab, Porta, "/turf/CastleWall/Center", "/turf/CastleWall/Window2"];
		foreach (string bp in estruturais) Da(bp);
		if (semArte.Count > 0) return new Relatorio(0, 0, 0, 0, 0, semArte);

		// CHAVE NOVA POR COMPOSICAO. O `.dmm` guarda a celula como uma chave curta pra uma lista de
		// typepaths; celulas iguais compartilham chave. Reaproveitar a mesma logica aqui mantem o
		// dicionario pequeno e, sobretudo, DETERMINISTICO: a mesma composicao sempre cai na mesma
		// chave, na mesma ordem.
		var porComposicao = new Dictionary<string, string>(StringComparer.Ordinal);
		int proxima = 0;

		string Chave(string[] tipos)
		{
			string id = string.Join(",", tipos);
			if (porComposicao.TryGetValue(id, out string? k)) return k;
			do { k = "veg" + proxima++; } while (dados.Keys.ContainsKey(k));
			dados.Keys[k] = tipos;
			porComposicao[id] = k;
			return k;
		}

		var tocadas = new HashSet<(int, int)>();
		int paredes = 0, portas = 0, moveis = 0, maquinas = 0;

		// Troca o TURF da celula e, opcionalmente, poe um obj em cima.
		void Escrever(int bx, int by, string turf, string? obj)
		{
			// A conta do eixo Y mora no `NoPort` -- ver o cabecalho dele.
			(int ax, int ay) = NoPort(new Peca(bx, by, "", null), nivel.Height);
			if (ax < 0 || ay < 0 || ax >= nivel.Width || ay >= nivel.Height) return;

			string? atual = nivel.Cells[ax, ay];
			string[] antes = atual != null && dados.Keys.TryGetValue(atual, out string[]? v) ? v : [];

			// O TURF NOVO SUBSTITUI O VELHO e o resto fica -- que e o que `new /turf/X(cur)` faz no
			// BYOND: ele troca o chao e nao encosta no que esta em cima dele.
			var novo = new List<string> { turf };
			foreach (string t in antes)
				if (!t.StartsWith("/turf", StringComparison.Ordinal)) novo.Add(t);
			if (obj != null) novo.Insert(1, obj);

			nivel.Cells[ax, ay] = Chave([.. novo]);
			tocadas.Add((ax, ay));
		}

		// A LISTA VEM DO `Planta()`, e o filtro de arte e AQUI. Ver o cabecalho daquele metodo: o
		// que ele devolve e a PROMESSA; o que sai daqui e o que a folha de sprites deixou entregar.
		foreach (Peca p in Planta())
		{
			if (EhParede(p.Turf)) { Escrever(p.Bx, p.By, p.Turf, null); paredes++; continue; }
			if (p.Turf == Porta) { Escrever(p.Bx, p.By, p.Turf, null); portas++; continue; }

			if (p.Obj is { } movel && Da(movel))
			{
				Escrever(p.Bx, p.By, p.Turf, movel);
				if (EhMaquina(movel)) maquinas++;
				else moveis++;
				continue;
			}

			// chao puro -- e tambem onde cai o movel sem arte: o DM assenta o piso ANTES de pousar
			// a mobilia (VegetaCity.dm:59), entao a sala continua sendo uma sala.
			Escrever(p.Bx, p.By, p.Turf, null);
		}

		return new Relatorio(tocadas.Count, paredes, portas, moveis, maquinas, semArte);
	}
}
