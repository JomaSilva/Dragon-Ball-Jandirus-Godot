using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DOS PLANETAS SORTEADOS -- `procedural [mundos] [raizDoProjeto]`.
///
/// Duas queixas do dono em 2026-10-10, as duas sobre os mundos gerados:
///
///   1. *"planetas procedurais estao com esses blocos estranhos de madeira no mapa"* -- a MONTANHA era
///      pintada com a janela das casas (`Walls`/`Wall1`). Ver `GeradorDeTerreno.Rocha`.
///   2. *"esses planetas sempre devem ter uma arvore frutifera (maça) pro player nunca morrer de fome"* --
///      o POMAR. Ver `Core/World/Pomar.cs`.
///
/// ============================ POR QUE ELA LE O DISCO ============================
/// O defeito 1 nao estava em conta nenhuma: o gerador devolvia um par de NOMES perfeitamente valido, o
/// `tiles.json` o resolvia, o cliente o pintava -- e o desenho era o errado. Nada que so olhe o Core
/// enxerga isso. Entao esta bancada resolve o tile da montanha de cada mundo no `tiles.json` de verdade,
/// abre o PNG que o jogo carrega e compara os 1024 pixels com os do `turf/pspace_mountain` do original
/// (`'JEFcliff.dmi'`, estado "", `ProceduralSpace.dm:562-564`). Uma troca de estado, de folha ou de
/// indice reprova aqui antes de chegar na tela.
/// ================================================================================
///
/// OS MUNDOS SAO OS DE VERDADE: quem decide que um planeta existe, e com que seed, e o `Sistemas.Do`.
/// Sortear um `ulong` qualquer mediria o gerador, e nao o universo em que se joga.
///
///     dotnet run --project Tools/AssetPipeline -- procedural 300
/// </summary>
public static class ProceduralBench
{
	private static int _ok, _falhas;

	private static void Afirmar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; Console.WriteLine($"[procedural]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhas++; Console.WriteLine($"[procedural]   FALHA {oque}   [{detalhe}]"); }
	}

	/// <summary>Tres universos: a seed de fabrica do port e duas quaisquer. A amostra roda os tres em rodizio.</summary>
	private static readonly ulong[] Universos = [20260802UL, 1234567UL, 987654321UL];

	private readonly record struct Mundo(PlanetaNoEspaco Planeta, MundoProcedural Ficha);

	public static int Run(int quantos, string raiz)
	{
		_ok = _falhas = 0;
		Console.WriteLine("[procedural] ================ OS PLANETAS SORTEADOS: A MONTANHA E O POMAR (dono, 2026-10-10) ================");

		try
		{
			List<Mundo> mundos = Amostra(quantos);
			var biomas = new HashSet<BiomaDeTerreno>(mundos.Select(m => m.Ficha.Bioma));
			Afirmar($"(montagem) {quantos} mundos SORTEADOS do universo de producao, nos cinco biomas que a seed sorteia",
					mundos.Count == quantos && biomas.Count == 5,
					$"{mundos.Count} mundos, lado {mundos.Min(m => m.Ficha.Lado)}..{mundos.Max(m => m.Ficha.Lado)}, biomas: {string.Join(", ", biomas.Order())}");
			if (mundos.Count == 0) return 1;

			Regua? regua = Regua.Montar(raiz);
			Afirmar("(montagem) o `tiles.json`, a folha da montanha e a `JEFcliff.png` do original estao no disco",
					regua != null, raiz);

			Medir(mundos, regua);
			if (regua != null)
			{
				OsPreFeitos(raiz, regua);
				ACaixaDaRocha(regua);
			}
			OPomarForjado();
		}
		catch (Exception e)
		{
			Afirmar($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			GeradorDeTerreno.MontanhaDeJanelaDeTeste = false;
			Pomar.PlanetaSemPomarDeTeste = false;
			Console.WriteLine($"[procedural] ================ {_ok} OK, {_falhas} FALHA(S) ================");
		}
		return _falhas;
	}

	/// <summary>Os primeiros <paramref name="quantos"/> mundos sorteados, varrendo as celulas de sistema dos tres universos.</summary>
	private static List<Mundo> Amostra(int quantos)
	{
		var l = new List<Mundo>(quantos);
		for (int anel = 1; anel <= 400 && l.Count < quantos; anel++)
		{
			foreach (ulong universo in Universos)
			{
				// uma diagonal por anel e por universo: celulas longe umas das outras, e nunca a mesma duas vezes
				if (Sistemas.Do(universo, anel * 3, anel * 5 + 1) is not { } sistema) continue;
				foreach (PlanetaNoEspaco p in sistema.Planetas())
				{
					if (p.Premade || l.Count >= quantos) continue;   // pre-feito tem mapa proprio: nao passa pelo gerador
					l.Add(new Mundo(p, MundoProcedural.DaSeed(p.Seed, p.Nome)));
				}
			}
		}
		return l;
	}

	// =====================================================================
	// A REGUA DA MONTANHA
	// =====================================================================
	/// <summary>O que a bancada precisa do disco pra julgar um tile: o indice, e a arte do original.</summary>
	private sealed class Regua
	{
		public required string Raiz { get; init; }
		public required CatalogoDeTiles Tiles { get; init; }

		/// <summary>A `JEFcliff.png`, e o canto do quadro do estado "" dentro dela.</summary>
		public required string Penhasco { get; init; }
		public required int PenhascoX { get; init; }
		public required int PenhascoY { get; init; }

		private readonly Dictionary<TileVisual, (int Diferentes, string Onde)> _julgados = [];

		public static Regua? Montar(string raiz)
		{
			string indice = Path.Combine(raiz, "Assets", "Data", "tiles.json");
			string penhasco = Path.Combine(raiz, "Assets", "Sprites", "Turfs", "JEFcliff.png");
			if (!File.Exists(indice) || DmiFile.Read(penhasco) is not { } dmi) return null;

			// O ESTADO "" PELO NOME, e nao "o quadro 0": a posicao sai dos metadados do proprio .dmi.
			int quadro = 0;
			bool achou = false;
			foreach (DmiState s in dmi.States)
			{
				if (s.Name.Length == 0) { achou = true; break; }
				quadro += Math.Max(1, s.Dirs) * Math.Max(1, s.Frames);
			}
			if (!achou || dmi.IconWidth != 32 || dmi.IconHeight != 32) return null;

			int colunas = Math.Max(1, dmi.SheetWidth / dmi.IconWidth);
			return new Regua
			{
				Raiz = raiz,
				Tiles = CatalogoDeTiles.Parse(File.ReadAllText(indice)),
				Penhasco = penhasco,
				PenhascoX = quadro % colunas * 32,
				PenhascoY = quadro / colunas * 32,
			};
		}

		public string ArquivoDe(AtlasDeTiles a) =>
			Path.Combine(Raiz, a.ResPath["res://".Length..].Replace('/', Path.DirectorySeparatorChar));

		/// <summary>
		/// QUANTOS PIXELS DESTE TILE DIFEREM DO PENHASCO DO DM. Zero = e o mesmo desenho. Negativo = nem deu
		/// pra comparar (o tile nao resolve, a folha nao abre, o icone nao e 32x32) -- e isso tambem reprova.
		/// </summary>
		public (int Diferentes, string Onde) Julgar(TileVisual v)
		{
			if (_julgados.TryGetValue(v with { Tinta = null }, out (int, string) ja)) return ja;

			(int, string) r;
			if (Tiles.Achar(v) is not { } t || Tiles.Atlas(v.Atlas) is not { } atlas)
				r = (-1, $"'{v.Atlas}'/'{v.Estado}' nao esta no tiles.json");
			else if (atlas.LarguraDoIcone != 32 || atlas.AlturaDoIcone != 32
					 || PngPixels.Ler(ArquivoDe(atlas)) is not { } folha || PngPixels.Ler(Penhasco) is not { } dm)
				r = (-1, $"nao consegui abrir {atlas.ResPath} como folha de 32x32");
			else
			{
				int diferentes = 0;
				for (int y = 0; y < 32; y++)
					for (int x = 0; x < 32; x++)
					{
						int a = ((t.Y * 32 + y) * folha.Largura + t.X * 32 + x) * 4;
						int b = ((PenhascoY + y) * dm.Largura + PenhascoX + x) * 4;
						if (folha.Rgba[a] != dm.Rgba[b] || folha.Rgba[a + 1] != dm.Rgba[b + 1]
							|| folha.Rgba[a + 2] != dm.Rgba[b + 2] || folha.Rgba[a + 3] != dm.Rgba[b + 3]) diferentes++;
					}
				r = (diferentes, $"{v.Atlas}/{v.Estado} = fonte {t.Fonte} ({t.X},{t.Y})");
			}
			return _julgados[v with { Tinta = null }] = r;
		}
	}

	// =====================================================================
	// 1 e 2. CADA MUNDO, UMA VEZ: A MONTANHA E O POMAR
	// =====================================================================
	private static void Medir(List<Mundo> mundos, Regua? regua)
	{
		Console.WriteLine("[procedural] -- 1) a montanha (os \"blocos de madeira\") e 2) o pomar, mundo a mundo --");

		// ---- a montanha ----
		int comMontanha = 0, montanhaErrada = 0;
		long celulasDeMontanha = 0;
		string exemploDeMontanha = "", tileDaMontanha = "";

		// ---- o pomar ----
		int semMacieira = 0, chegadaLonge = 0, semCaminho = 0, emChaoRuim = 0, repetidas = 0, demais = 0, pertoDemais = 0;
		int mudouOTerreno = 0, cavados = 0, naoRepete = 0, naRocha = 0, rochasGrandes = 0;
		var aneis = new int[Pomar.RaioDaChegada + 1];
		string exemploDoPomar = "";
		ulong assinaturaDosPomares = 14695981039346656037UL;
		var porFaixa = new SortedDictionary<int, (int Mundos, long Macieiras, long Setores)>();
		var porBioma = new SortedDictionary<BiomaDeTerreno, (int Mundos, long Montanha, long Macieiras, long Setores)>();

		for (int k = 0; k < mundos.Count; k++)
		{
			Mundo m = mundos[k];
			TerrenoGerado t = GeradorDeTerreno.Gerar(m.Ficha.Parametros());

			// ---- 1. a montanha ----
			int montanha = 0, mx = -1, my = -1;
			for (int i = 0; i < t.Chao.Length; i++)
			{
				if (t.Chao[i] != (byte)ClasseDeTerreno.Montanha) continue;
				if (montanha++ == 0) { mx = i % t.Largura; my = i / t.Largura; }
			}
			celulasDeMontanha += montanha;
			if (montanha > 0 && regua != null)
			{
				comMontanha++;
				// O VISUAL SAI DA CELULA, pela mesma pergunta que o cliente faz ao pintar.
				(int diferentes, string onde) = regua.Julgar(t.VisualDoChao(mx, my));
				tileDaMontanha = onde;
				if (diferentes != 0)
				{
					montanhaErrada++;
					if (exemploDeMontanha.Length == 0)
						exemploDeMontanha = $"{m.Planeta.Nome} (seed {m.Planeta.Seed}): {onde}, {diferentes} pixel(s) diferentes do DM";
				}
			}

			// ---- 2. o pomar ----
			foreach (byte c in t.Cobertura)
				if (c == (byte)CoberturaDeTerreno.RochaGrande) rochasGrandes++;
			IReadOnlyList<(int X, int Y)> pomar = t.Macieiras;
			int setores = Pomar.SetoresPorLado(t.Largura) * Pomar.SetoresPorLado(t.Altura);
			string problema = "";

			if (pomar.Count == 0) { semMacieira++; problema = "nenhuma macieira"; }
			else
			{
				(int ax, int ay) = pomar[0];
				int anel = Math.Max(Math.Abs(ax - t.SpawnCelX), Math.Abs(ay - t.SpawnCelY));
				if (anel < 1 || anel > Pomar.RaioDaChegada) { chegadaLonge++; problema = $"a da chegada esta a {anel} tiles do pouso"; }
				else aneis[anel]++;

				if (!ChegaAPe(t, pomar)) { semCaminho++; problema = "do pouso nao se chega a pe ate a macieira da chegada"; }

				var vistas = new HashSet<(int, int)>();
				for (int i = 0; i < pomar.Count; i++)
				{
					(int x, int y) = pomar[i];
					if (!vistas.Add((x, y))) { repetidas++; problema = $"({x},{y}) aparece duas vezes"; }
					if (!ChaoFirme(t, x, y)) { emChaoRuim++; problema = $"({x},{y}) nao e chao firme e livre: {t.ClasseEm(x, y)}/{t.CoberturaEm(x, y)}"; }
					if (NoDesenhoDeUmaRocha(t, x, y)) { naRocha++; problema = $"({x},{y}) esta dentro do desenho de uma rocha grande"; }
					if (i > 0 && Math.Max(Math.Abs(x - t.SpawnCelX), Math.Abs(y - t.SpawnCelY)) <= Pomar.RaioDaChegada)
					{ pertoDemais++; problema = $"a espalhada ({x},{y}) caiu dentro do raio da chegada"; }
				}
				if (pomar.Count - 1 > setores) { demais++; problema = $"{pomar.Count - 1} espalhadas em {setores} setores"; }

				foreach ((int x, int y) in pomar)
					assinaturaDosPomares = (assinaturaDosPomares ^ Espaco.Misturar(m.Planeta.Seed, (ulong)(uint)x, (ulong)(uint)y)) * 1099511628211UL;
			}
			if (problema.Length > 0 && exemploDoPomar.Length == 0)
				exemploDoPomar = $"{m.Planeta.Nome} (seed {m.Planeta.Seed}, {m.Ficha.Descricao()}): {problema}";

			// O POMAR NAO MEXE NO CHAO: o mesmo mundo, gerado SEM ele, tem de ter a mesma assinatura. So o
			// ultimo recurso (o pouso isolado, que abre UMA celula) pode mudar -- e ele e contado a parte.
			// E a segunda geracao COM ele tem de dar o mesmo pomar. Os dois nos primeiros 40 mundos: sao
			// duas geracoes a mais por mundo.
			if (k < 40)
			{
				TerrenoGerado denovo = GeradorDeTerreno.Gerar(m.Ficha.Parametros());
				if (!denovo.Macieiras.SequenceEqual(pomar)) naoRepete++;

				Pomar.PlanetaSemPomarDeTeste = true;
				TerrenoGerado sem;
				try { sem = GeradorDeTerreno.Gerar(m.Ficha.Parametros()); }
				finally { Pomar.PlanetaSemPomarDeTeste = false; }
				if (sem.Assinatura() != t.Assinatura())
				{
					if (pomar.Count > 0 && sem.ClasseEm(pomar[0].X, pomar[0].Y) != t.ClasseEm(pomar[0].X, pomar[0].Y)) cavados++;
					else mudouOTerreno++;
				}
			}

			int faixa = m.Ficha.Lado <= 256 ? 256 : m.Ficha.Lado <= 384 ? 384 : m.Ficha.Lado <= 512 ? 512 : m.Ficha.Lado <= 768 ? 768 : 1000;
			(int fm, long fa, long fs) = porFaixa.GetValueOrDefault(faixa);
			porFaixa[faixa] = (fm + 1, fa + pomar.Count, fs + setores);
			(int bm, long bmo, long ba, long bs) = porBioma.GetValueOrDefault(m.Ficha.Bioma);
			porBioma[m.Ficha.Bioma] = (bm + 1, bmo + montanha, ba + Math.Max(0, pomar.Count - 1), bs + setores);
		}

		foreach ((BiomaDeTerreno b, (int n, long mo, long ma, long se)) in porBioma)
			Console.WriteLine($"[procedural]        {b,-10} {n,4} mundo(s) | montanha {mo,9:N0} celula(s) | "
							  + $"espalhadas {ma,5} em {se,5} setor(es) ({100.0 * ma / Math.Max(1, se):0}%)");
		foreach ((int faixa, (int n, long ma, long se)) in porFaixa)
			Console.WriteLine($"[procedural]        lado ate {faixa,4}: {n,4} mundo(s), {(double)ma / n,5:0.0} macieira(s) por mundo "
							  + $"({(double)se / n:0.0} setor(es))");
		Console.WriteLine($"[procedural]        a macieira da chegada, por distancia do pouso (em tiles): "
						  + string.Join("  ", Enumerable.Range(1, Pomar.RaioDaChegada).Select(a => $"{a}:{aneis[a]}")));
		Console.WriteLine($"[procedural]        assinatura dos pomares desta amostra: {assinaturaDosPomares:X16} "
						  + "(tem de ser a MESMA em outra execucao e em outra maquina)");

		// ---------------- 1. A MONTANHA ----------------
		if (regua != null)
		{
			Afirmar($"**A MONTANHA DE TODO MUNDO E O PENHASCO DO DM**: os 1024 pixels do tile sao os do `JEFcliff` \"\" ({comMontanha} mundos com montanha, {celulasDeMontanha:N0} celulas)",
					comMontanha > 0 && montanhaErrada == 0,
					montanhaErrada == 0 ? tileDaMontanha : $"{montanhaErrada} de {comMontanha} mundos; ex.: {exemploDeMontanha}");

			// ---- as seis paletas, inclusive a que a seed nunca sorteia (Rochoso: so pre-feito a usaria) ----
			var ruins = new List<string>();
			foreach (BiomaDeTerreno b in Enum.GetValues<BiomaDeTerreno>())
			{
				TerrenoGerado pequeno = GeradorDeTerreno.Gerar(new ParametrosDeTerreno { Seed = 7, Largura = 16, Altura = 16, Bioma = b });
				(int dif, string onde) = regua.Julgar(pequeno.VisualDaClasse(ClasseDeTerreno.Montanha));
				if (dif != 0) ruins.Add($"{b}: {onde} ({dif})");
			}
			Afirmar("...e isso vale nas SEIS paletas (a do Rochoso a seed nao sorteia, e ela tambem tem montanha)",
					ruins.Count == 0, string.Join("; ", ruins));

			// ---- o resto da paleta resolve ----
			List<CatalogoDeTiles.Veredito> buracos = [.. regua.Tiles.Conferir().Where(v => v.Usado && !v.Resolve)];
			Afirmar("todo tile que as paletas USAM resolve no `tiles.json` (chao e cobertura: nenhuma celula nasce vazia)",
					buracos.Count == 0,
					string.Join("; ", buracos.Take(4).Select(v => $"{v.Bioma}.{v.Papel} '{v.Pedido.Atlas}'/'{v.Pedido.Estado}': {v.Motivo}")));

			// ---- A CONTRAPROVA: o jogo de antes do conserto, no mesmo binario ----
			int reprovados = 0, vistos = 0, pixels = 0;
			string ondeEra = "";
			GeradorDeTerreno.MontanhaDeJanelaDeTeste = true;
			try
			{
				foreach (Mundo m in mundos.Take(24))
				{
					TerrenoGerado t = GeradorDeTerreno.Gerar(m.Ficha.Parametros());
					int i = Array.IndexOf(t.Chao, (byte)ClasseDeTerreno.Montanha);
					if (i < 0) continue;
					vistos++;
					(int dif, string onde) = regua.Julgar(t.VisualDoChao(i % t.Largura, i / t.Largura));
					if (dif != 0) { reprovados++; pixels = dif; ondeEra = onde; }
				}
			}
			finally { GeradorDeTerreno.MontanhaDeJanelaDeTeste = false; }
			Afirmar("(defeito injetado: a montanha volta a ser a JANELA das casas) a MESMA regua reprova todo mundo que tem montanha",
					vistos > 0 && reprovados == vistos, $"{reprovados} de {vistos} reprovados; {ondeEra}, {pixels} de 1024 pixels diferentes do DM");
		}

		// ---------------- 2. O POMAR ----------------
		Afirmar($"**TODO MUNDO TEM MACIEIRA** ({mundos.Count} mundos)", semMacieira == 0, $"{semMacieira} sem nenhuma; ex.: {exemploDoPomar}");
		Afirmar($"**...E UMA DELAS ESTA A ATE {Pomar.RaioDaChegada} TILES DO PONTO DE CHEGADA** (nunca em cima dele)",
				chegadaLonge == 0 && semMacieira == 0, $"{chegadaLonge} fora do raio; ex.: {exemploDoPomar}");
		Afirmar("...e do pouso se CHEGA A PE ate ela, pela colisao de verdade do mundo (nem rio nem montanha no meio)",
				semCaminho == 0 && semMacieira == 0, $"{semCaminho} sem caminho; ex.: {exemploDoPomar}");
		Afirmar("toda macieira nasce em CHAO FIRME E LIVRE: nem agua, nem montanha, nem em cima de arvore/planta/rocha, nem na beirada",
				emChaoRuim == 0, $"{emChaoRuim} em chao ruim; ex.: {exemploDoPomar}");
		Afirmar("nenhuma celula repetida, nenhuma espalhada dentro do raio da chegada, e no maximo uma espalhada por setor",
				repetidas == 0 && pertoDemais == 0 && demais == 0, $"repetidas {repetidas}, perto demais {pertoDemais}, alem do teto {demais}");

		double[] medias = [.. porFaixa.Values.Select(v => (double)v.Macieiras / v.Mundos)];
		bool cresce = medias.Length >= 2;
		for (int i = 1; i < medias.Length; i++) cresce &= medias[i] > medias[i - 1];
		long todas = porFaixa.Values.Sum(v => v.Macieiras) - mundos.Count, todosOsSetores = porFaixa.Values.Sum(v => v.Setores);
		Afirmar($"A QUANTIDADE ACOMPANHA O TAMANHO: a media por mundo cresce de faixa em faixa, e 4 em cada 5 setores de {Pomar.LadoDoSetor} tiles tem a sua",
				cresce && todas * 5 >= todosOsSetores * 4,
				$"medias por faixa: {string.Join(" < ", medias.Select(x => x.ToString("0.0")))}; {todas} espalhadas em {todosOsSetores} setores");

		Afirmar("a mesma seed da o MESMO pomar na segunda geracao (40 mundos)", naoRepete == 0, $"{naoRepete} divergiram");
		Afirmar("O POMAR NAO MEXE NO CHAO: gerado sem ele, cada um dos 40 mundos tem a mesma assinatura (a seed antiga da o mesmo mapa)",
				mudouOTerreno == 0, $"{mudouOTerreno} mudaram; {cavados} precisaram do ultimo recurso (abrir uma celula ao lado do pouso)");

		// ---- A CONTRAPROVA ----
		int semNada = 0, olhados = 0;
		Pomar.PlanetaSemPomarDeTeste = true;
		try
		{
			foreach (Mundo m in mundos.Take(24))
			{
				olhados++;
				if (GeradorDeTerreno.Gerar(m.Ficha.Parametros()).Macieiras.Count == 0) semNada++;
			}
		}
		finally { Pomar.PlanetaSemPomarDeTeste = false; }
		Afirmar("(defeito injetado: o mundo nasce sem pomar) a regua do \"todo mundo tem macieira\" reprova os mesmos mundos",
				olhados > 0 && semNada == olhados, $"{semNada} de {olhados} sem nenhuma");

		// ---- A ROCHA GRANDE: a macieira nao nasce dentro do desenho dela ----
		Afirmar($"NENHUMA MACIEIRA NASCE DENTRO DO DESENHO DE UMA ROCHA GRANDE (o topo da pedra passa da parede dela; {rochasGrandes:N0} rochas na amostra)",
				naRocha == 0 && rochasGrandes > 0, $"{naRocha} macieira(s) dentro do desenho; ex.: {exemploDoPomar}");

		int dentroComDefeito = 0, macieirasComDefeito = 0;
		Pomar.CanteiroNaRochaDeTeste = true;
		try
		{
			foreach (Mundo m in mundos.Take(200))
			{
				TerrenoGerado t = GeradorDeTerreno.Gerar(m.Ficha.Parametros());
				foreach ((int x, int y) in t.Macieiras)
				{
					macieirasComDefeito++;
					if (NoDesenhoDeUmaRocha(t, x, y)) dentroComDefeito++;
				}
			}
		}
		finally { Pomar.CanteiroNaRochaDeTeste = false; }
		Afirmar("(defeito injetado: o canteiro pode cair atras da pedra) a mesma regua acha macieiras dentro do desenho de uma rocha",
				dentroComDefeito > 0, $"{dentroComDefeito} de {macieirasComDefeito} macieiras, nos primeiros {Math.Min(200, mundos.Count)} mundos");
	}

	/// <summary>
	/// ESTA CELULA ESTA DENTRO DO DESENHO DE ALGUMA ROCHA GRANDE? A conta e a da bancada (a caixa do desenho a
	/// partir de cada ancora), e a caixa em si e conferida contra o PNG em <see cref="ACaixaDaRocha"/>.
	/// </summary>
	private static bool NoDesenhoDeUmaRocha(TerrenoGerado t, int x, int y)
	{
		for (int ay = y; ay < y + RochaGrande.FileirasDoDesenho; ay++)
			for (int ax = x - RochaGrande.ColunasDeCadaLado; ax <= x + RochaGrande.ColunasDeCadaLado; ax++)
				if (t.CoberturaEm(ax, ay) == CoberturaDeTerreno.RochaGrande) return true;
		return false;
	}

	/// <summary>
	/// A CAIXA DO DESENHO DA ROCHA GRANDE CONFERE COM O PNG? Os dois numeros do Core
	/// (`RochaGrande.FileirasDoDesenho` e `ColunasDeCadaLado`) sao o que o pomar usa pra desviar da pedra, e
	/// numero escrito a mao sobre uma arte envelhece calado quando a arte ou a descida da camada mudam.
	///
	/// ONDE O JOGO DESENHA A FOLHA, a partir do canto de cima da celula da ANCORA: o tileset centra o atlas na
	/// celula e poe o PE dele no pe dela (o `texture_origin` de todo tile maior que 32), e a camada do cliente
	/// desce `RochaGrande.DescidaPx` (`PlanetaProcedural.Camada`).
	/// </summary>
	private static void ACaixaDaRocha(Regua regua)
	{
		if (regua.Tiles.Atlas(RochaGrande.Atlas) is not { } atlas || PngPixels.Ler(regua.ArquivoDe(atlas)) is not { } folha)
		{
			Afirmar("(montagem) a folha da rocha grande esta no `tiles.json` e abre", false, RochaGrande.Atlas);
			return;
		}

		// PIXEL QUE SE VE: alfa acima de um oitavo. A folha tem um halo de alfa 1 a 11 em volta da pedra (resto
		// de recorte), que nao aparece na tela e alargaria a caixa em uma fileira e em toda a largura do atlas.
		const int alfaQueSeVe = 32;

		const int T = ZoneCollision.TileSize;
		int x0 = T / 2 - atlas.LarguraDoIcone / 2, y0 = T + RochaGrande.DescidaPx - atlas.AlturaDoIcone;
		int fora = 0, fileiras = 0, colunas = 0;
		for (int cy = -10; cy <= 4; cy++)
			for (int cx = -8; cx <= 8; cx++)
			{
				bool temPedra = false;
				for (int py = 0; py < T && !temPedra; py++)
					for (int px = 0; px < T && !temPedra; px++)
					{
						int tx = cx * T + px - x0, ty = cy * T + py - y0;
						if (tx < 0 || ty < 0 || tx >= folha.Largura || ty >= folha.Altura) continue;
						temPedra = folha.Rgba[(ty * folha.Largura + tx) * 4 + 3] > alfaQueSeVe;
					}
				if (!temPedra) continue;

				if (cy > 0 || cy <= -RochaGrande.FileirasDoDesenho || Math.Abs(cx) > RochaGrande.ColunasDeCadaLado) fora++;
				fileiras = Math.Max(fileiras, 1 - cy);
				colunas = Math.Max(colunas, Math.Abs(cx));
			}

		Afirmar("a CAIXA do desenho da rocha grande e a do PNG: nenhum pixel da pedra cai fora dela, e ela nao sobra em nenhum lado",
				fora == 0 && fileiras == RochaGrande.FileirasDoDesenho && colunas == RochaGrande.ColunasDeCadaLado,
				$"{fora} celula(s) com pedra fora da caixa; a pedra ocupa {fileiras} fileira(s) e chega a {colunas} coluna(s) de cada lado "
				+ $"(o Core diz {RochaGrande.FileirasDoDesenho} e {RochaGrande.ColunasDeCadaLado})");
	}

	/// <summary>Chao em que se anda, sem nada em cima, fora da faixa da beirada -- pela colisao E pelas grades.</summary>
	private static bool ChaoFirme(TerrenoGerado t, int x, int y) =>
		t.Colisao.ServeDeChao(x, y)
		&& t.ClasseEm(x, y) is ClasseDeTerreno.Praia or ClasseDeTerreno.Planicie or ClasseDeTerreno.Colina
		&& t.CoberturaEm(x, y) == CoberturaDeTerreno.Nada
		&& x >= Pomar.MargemDaBeirada && y >= Pomar.MargemDaBeirada
		&& x < t.Largura - Pomar.MargemDaBeirada && y < t.Altura - Pomar.MargemDaBeirada;

	/// <summary>
	/// DO POUSO SE CHEGA A PE ATE UMA CELULA ENCOSTADA NA MACIEIRA DA CHEGADA? Busca em largura PROPRIA (nao
	/// e a do `Pomar`): passo em cruz, pela colisao do mundo como o corpo a sente (parede E agua barram quem
	/// anda), com as macieiras ja de pe -- elas sao densas. Janela de duas vezes o raio da chegada.
	/// </summary>
	private static bool ChegaAPe(TerrenoGerado t, IReadOnlyList<(int X, int Y)> pomar)
	{
		(int ax, int ay) = pomar[0];
		var arvores = new HashSet<(int, int)>(pomar);
		const int janela = 2 * Pomar.RaioDaChegada;

		var vistas = new HashSet<(int, int)> { (t.SpawnCelX, t.SpawnCelY) };
		var fila = new Queue<(int X, int Y)>();
		fila.Enqueue((t.SpawnCelX, t.SpawnCelY));
		while (fila.Count > 0)
		{
			(int x, int y) = fila.Dequeue();
			if (Math.Max(Math.Abs(x - ax), Math.Abs(y - ay)) <= 1) return true;

			foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
			{
				int nx = x + dx, ny = y + dy;
				if (Math.Max(Math.Abs(nx - t.SpawnCelX), Math.Abs(ny - t.SpawnCelY)) > janela) continue;
				if (arvores.Contains((nx, ny)) || t.Colisao.Bloqueia(nx, ny, ModoDeTravessia.APe)) continue;
				if (vistas.Add((nx, ny))) fila.Enqueue((nx, ny));
			}
		}
		return false;
	}

	// =====================================================================
	// 3. OS PLANETAS DO CATALOGO
	// =====================================================================
	/// <summary>
	/// O MESMO DEFEITO NOS MAPAS DESENHADOS A MAO? La o tile de cada celula vem do `.dmm`, e a janela e
	/// legitima numa casa -- o que seria defeito e ela pintada EM BLOCO, como cenario. A regua e a maior
	/// mancha contigua de `Walls`/`Wall1` em cada `.pedacos`.
	/// </summary>
	private static void OsPreFeitos(string raiz, Regua regua)
	{
		Console.WriteLine("[procedural] -- 3) os planetas do catalogo: a janela pintada em bloco? --");

		string pasta = Path.Combine(raiz, "Assets", "Maps");
		if (regua.Tiles.Achar("Walls", "Wall1") is not { } janela || regua.Tiles.Achar("Walls", "Wall13") is not { } rocha)
		{
			Afirmar("(montagem) o `tiles.json` tem a `Walls`/`Wall1` e a `Walls`/`Wall13`", false);
			return;
		}

		int mapas = 0, comJanela = 0, maiorMancha = 0, comRocha = 0;
		long celulasDeJanela = 0, celulasDeRocha = 0;
		string ondeAMaior = "";
		foreach (string arq in Directory.GetFiles(pasta, "*.pedacos").Order(StringComparer.Ordinal))
		{
			if (PedacosDoMapa.Ler(File.ReadAllBytes(arq)) is not { } ped) continue;
			mapas++;

			var janelas = new HashSet<(int, int)>();
			long rochas = 0;
			for (int cy = ped.Cy0; cy < ped.Cy1; cy++)
				for (int cx = ped.Cx0; cx < ped.Cx1; cx++)
					for (int camada = 0; camada < ped.Camadas.Length; camada++)
					{
						if (!ped.Achar(cx, cy, camada, out int inicio, out int n)) continue;
						for (int i = 0; i < n; i++)
						{
							CelulaDePedaco c = ped.Celula(inicio, i);
							if (c.Fonte != janela.Fonte) continue;
							if (c.Ax == janela.X && c.Ay == janela.Y) janelas.Add((c.X, c.Y));
							else if (c.Ax == rocha.X && c.Ay == rocha.Y) rochas++;
						}
					}

			if (rochas > 0) { comRocha++; celulasDeRocha += rochas; }
			if (janelas.Count == 0) continue;
			comJanela++;
			celulasDeJanela += janelas.Count;
			int mancha = MaiorMancha(janelas);
			if (mancha > maiorMancha) { maiorMancha = mancha; ondeAMaior = Path.GetFileName(arq); }
		}

		Afirmar($"**NOS {mapas} MAPAS PRE-FEITOS A JANELA NAO E CENARIO**: nenhuma mancha contigua de `Wall1` passa de {JanelasSeguidas} celulas",
				mapas > 0 && maiorMancha <= JanelasSeguidas,
				$"{celulasDeJanela} celula(s) de janela em {comJanela} mapa(s); a maior mancha tem {maiorMancha}" + (ondeAMaior.Length > 0 ? $" ({ondeAMaior})" : ""));
		Afirmar("...e a varredura ENXERGA a rocha: a `Wall13` (o desenho do penhasco do DM) esta pintada nos mapas desenhados a mao",
				celulasDeRocha > 0, $"{celulasDeRocha:N0} celula(s) em {comRocha} mapa(s)");
	}

	/// <summary>Quantas janelas seguidas uma casa pode ter sem aquilo ser "pintado em bloco".</summary>
	private const int JanelasSeguidas = 6;

	private static int MaiorMancha(HashSet<(int X, int Y)> celulas)
	{
		var vistas = new HashSet<(int, int)>();
		int maior = 0;
		foreach ((int X, int Y) c in celulas)
		{
			if (!vistas.Add(c)) continue;
			int n = 0;
			var fila = new Queue<(int X, int Y)>();
			fila.Enqueue(c);
			while (fila.Count > 0)
			{
				(int x, int y) = fila.Dequeue();
				n++;
				foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					if (celulas.Contains((x + dx, y + dy)) && vistas.Add((x + dx, y + dy))) fila.Enqueue((x + dx, y + dy));
			}
			maior = Math.Max(maior, n);
		}
		return maior;
	}

	// =====================================================================
	// 4. O POMAR EM CHAO FORJADO -- os casos que a seed quase nunca sorteia
	// =====================================================================
	/// <summary>
	/// OS DOIS CASOS RAROS, EM GRADES ESCRITAS A MAO. A amostra de cima nao os sorteia (um pouso num ilheu
	/// de um tile e um em milhoes), e regra que nunca roda e regra que nao existe. Quem responde e o
	/// `Pomar.Plantar` de producao, com as grades cruas -- o mesmo que o gerador chama.
	/// </summary>
	private static void OPomarForjado()
	{
		Console.WriteLine("[procedural] -- 4) o pomar em chao forjado: o ilheu e o rio --");
		const int n = 32, sx = 16, sy = 16;

		// ---- O ILHEU: tudo mar, menos a celula do pouso ----
		(byte[] chao, byte[] cobertura, byte[] bits, byte[] agua) = Mar(n);
		Secar(n, chao, agua, sx, sy);
		(int X, int Y)[] pomar = Pomar.Plantar(n, n, chao, cobertura, bits, agua, sx, sy, seed: 99);
		bool abriu = pomar.Length == 1
					 && Math.Max(Math.Abs(pomar[0].X - sx), Math.Abs(pomar[0].Y - sy)) == 1
					 && chao[pomar[0].Y * n + pomar[0].X] == (byte)ClasseDeTerreno.Planicie
					 && (agua[(pomar[0].Y * n + pomar[0].X) >> 3] & (1 << ((pomar[0].Y * n + pomar[0].X) & 7))) == 0;
		Afirmar("O ULTIMO RECURSO: pouso num ilheu de UM tile -> uma vizinha vira planicie seca e a macieira nasce nela",
				abriu, pomar.Length == 0 ? "nenhuma macieira" : $"{pomar.Length} macieira(s), a primeira em ({pomar[0].X},{pomar[0].Y})");

		// ---- O RIO: uma ilha de 3x1 com o pouso no meio, e um campo enorme a tres tiles, do outro lado da agua ----
		(chao, cobertura, bits, agua) = Mar(n);
		for (int x = sx - 1; x <= sx + 1; x++) Secar(n, chao, agua, x, sy);
		for (int y = sy + 3; y < n; y++)
			for (int x = 0; x < n; x++) Secar(n, chao, agua, x, y);
		pomar = Pomar.Plantar(n, n, chao, cobertura, bits, agua, sx, sy, seed: 99);
		Afirmar("O RIO: com campo aberto a tres tiles DO OUTRO LADO da agua, a macieira da chegada fica na ilha do pouso",
				pomar.Length >= 1 && pomar[0].Y == sy && Math.Abs(pomar[0].X - sx) == 1,
				pomar.Length == 0 ? "nenhuma macieira" : $"a da chegada em ({pomar[0].X},{pomar[0].Y}); o pouso e ({sx},{sy})");

		// ---- O CAMPO: tudo planicie -> fora da clareira, fora da linha e das colunas do pouso ----
		(chao, cobertura, bits, agua) = Mar(n);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++) Secar(n, chao, agua, x, y);
		pomar = Pomar.Plantar(n, n, chao, cobertura, bits, agua, sx, sy, seed: 99);
		Afirmar("O CAMPO ABERTO: a macieira da chegada nasce no anel 2, fora da LINHA e das COLUNAS do pouso (nao barra o primeiro passo nem cobre quem chegou)",
				pomar.Length >= 1 && Math.Abs(pomar[0].X - sx) == 2 && Math.Abs(pomar[0].Y - sy) is 1 or 2,
				pomar.Length == 0 ? "nenhuma macieira" : $"a da chegada em ({pomar[0].X},{pomar[0].Y}); o pouso e ({sx},{sy})");
	}

	private static (byte[] Chao, byte[] Cobertura, byte[] Bits, byte[] Agua) Mar(int n)
	{
		var chao = new byte[n * n];
		var agua = new byte[(n * n + 7) / 8];
		Array.Fill(chao, (byte)ClasseDeTerreno.Agua);
		Array.Fill(agua, (byte)0xFF);
		return (chao, new byte[n * n], new byte[(n * n + 7) / 8], agua);
	}

	private static void Secar(int n, byte[] chao, byte[] agua, int x, int y)
	{
		int i = y * n + x;
		chao[i] = (byte)ClasseDeTerreno.Planicie;
		agua[i >> 3] &= (byte)~(1 << (i & 7));
	}
}
