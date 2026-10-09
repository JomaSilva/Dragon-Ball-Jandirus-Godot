using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// A BANCADA DO SOB TETO -- e ela mede o DADO PUBLICADO, nao a regra que o conversor acabou de aplicar.
///
/// ============================ O QUE PODE DAR ERRADO NAO E A REGRA ============================
/// A regra do `CelulaInterna.SobTeto` tem tres linhas e concordaria consigo mesma pra sempre. O que
/// quebra calado e a LIGACAO entre ela e o mapa:
///
///   1. o `.dentro` nao existe (o comando `dentro` nunca rodou) e continua nevando dentro do Banco;
///   2. o `.dentro` existe e o `CarregarDentro` o RECUSA CALADO (tamanho de outro mapa);
///   3. o plano saiu ESPELHADO ou deslocado -- o BYOND conta o Y de baixo pra cima --, e o retangulo
///      seco cai no meio do campo, a vinte tiles do predio;
///   4. um `.dentro` velho sobra de um andar que perdeu o interior;
///   5. alguem passa a ler o bit cru do mapa e a parede derrubada continua "sob teto".
///
/// Os cinco sao costura entre o conversor, o arquivo e o Core. Por isso esta bancada abre os arquivos
/// com o leitor de PRODUCAO e confere contra numeros que NAO sairam do conversor.
/// ============================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- dentro-prova &lt;BYOND&gt;/Maps [Assets/Maps]
/// </summary>
public static class DentroBench
{
	private static int _ok, _falhas;

	private static void Conferir(bool cond, string oque)
	{
		if (cond) { _ok++; Console.WriteLine($"    ok    {oque}"); }
		else { _falhas++; Console.WriteLine($"    FALHA {oque}"); }
	}

	/// <summary>
	/// OS ANDARES QUE TEM INTERIOR, e a lista e ESCRITA A MAO de proposito.
	///
	/// Deriva-la do disco faria a bancada concordar com qualquer coisa que o conversor produzisse,
	/// inclusive com zero arquivos. Os numeros vieram de uma contagem INDEPENDENTE do `.dmm` (um
	/// leitor em Python, 2026-10-08), separados pelas tres fontes do `Interiores`:
	/// `Area` = celulas pintadas com `.../Inside`; `Erguido` = `/turf/build` em area externa;
	/// `Cidade` = o carimbo de Vegeta.
	///
	/// A COLUNA `Erguido` E UMA TRAVA: a mudanca desses turfs pra area interna depende de uma condicao
	/// do DM que foi conferida A MAO pras tres areas daqui (ver o cabecalho do `Interiores`). Turf
	/// erguido novo em outro andar deixa isto vermelho, e quem ler vem conferir a condicao.
	/// </summary>
	private static readonly (string Arq, int Area, int Erguido, int Cidade)[] Esperados =
	[
		("z01_Earth",                    1164,   0,   0),
		("z02_Namek",                    2047,   0,   0),
		("z03_Vegeta",                   1239,   0, 854),
		("z04_Icer",                     3491,   0,   0),
		("z05_Arconia",                  1488,  36,   0),
		("z06_Afterlife",                  40,   0,   0),
		("z09_Hell",                      280,   0,   0),
		("z10_Heaven",                   4267,   0,   0),
		("z11_Hera",                      472, 101,   0),
		("z12_Lookout",                 15517,   9,   0),
		("z13_Hyperbolic_Time_Chamber",   850,   0,   0),
		("z19_Big_Geti_Star",            2622,   0,   0),
		("z20_Negative_Earth",             72,   0,   0),
		("z21_Arlia",                     864,   0,   0),
		("z22_Vegeta_Cave",             31229,   0,   0),
		("z23_Earth_Cave",              53878,   0,   0),
		("z24_Interdimension",           1626,   0,   0),
		("z25_Inbetween_Realm",            90,   0,   0),
		("z28_Large_Space_Station",     28882,   0,   0),
		("z31_God_Realm",                1363,   0,   0),
	];

	/// <summary>
	/// O BANCO DA TERRA, em celula do port: o retangulo 12x6 que o `.dmm` pinta com
	/// `/area/Earth/Inside` (BYOND x 68-79, y 238-243; a linha do port e `500 - y`).
	/// E o predio onde todo Human nasce, e foi nele que o dono viu nevar.
	/// </summary>
	private const int BancoX0 = 67, BancoX1 = 78, BancoY0 = 257, BancoY1 = 262;

	/// <summary>O berco (`/obj/SpawnPoint`, BYOND 74,240) e a porta (`Door4`, BYOND 74,238).</summary>
	private const int BercoX = 73, BercoY = 260, PortaX = 73, PortaY = 262;

	public static int Run(string pastaDmm, string pastaMaps)
	{
		_ok = _falhas = 0;
		Console.WriteLine("============================================================");
		Console.WriteLine(" BANCADA DO SOB TETO -- a area `Inside` do original");
		Console.WriteLine("============================================================");

		// ============================ 1. O PLANO EXISTE E VOLTA PELO LEITOR DE PRODUCAO ============================
		// Quem le e o `ZoneCollision.CarregarDentro` -- o mesmo que o servidor e o cliente chamam. Um
		// parser proprio aqui testaria o parser da bancada.
		Console.WriteLine("\n  [1] o plano existe, carrega, e tem o numero de celulas contado no .dmm");
		var planos = new Dictionary<string, ZoneCollision>(StringComparer.Ordinal);
		foreach ((string arq, int area, int erguido, int cidade) in Esperados)
		{
			string col = Path.Combine(pastaMaps, arq + ".col");
			string den = Path.Combine(pastaMaps, arq + ".dentro");

			if (!File.Exists(den)) { Conferir(false, $"{arq}: falta o `.dentro` -- rode `-- dentro`"); continue; }
			if (!File.Exists(col)) { Conferir(false, $"{arq}: falta o `.col`"); continue; }

			ZoneCollision? m = ZoneCollision.Load(File.ReadAllBytes(col));
			if (m == null) { Conferir(false, $"{arq}: o `.col` nao carregou"); continue; }

			bool leu = m.CarregarDentro(File.ReadAllBytes(den));
			Conferir(leu, $"{arq}: o `.dentro` volta pelo `CarregarDentro` de producao");
			if (!leu) continue;
			planos[arq] = m;

			int conta = 0;
			for (int y = 0; y < m.Height; y++)
				for (int x = 0; x < m.Width; x++)
					if (m.NasceuDentro(x, y)) conta++;
			int esperado = area + erguido + cidade;
			Conferir(conta == esperado,
					 $"{arq}: {conta} celulas sob teto (esperado {esperado} = {area} de area"
					 + (erguido > 0 ? $" + {erguido} de turf erguido" : "")
					 + (cidade > 0 ? $" + {cidade} da cidade" : "") + ")");

			// O VISITADOR DO PLANO (`ParaCadaCelulaQueNasceuDentro`) e o caminho que o teto do cliente
			// usa, e ele pula os bytes zerados: um erro de indice ali apagaria -- ou deslocaria -- o
			// interior de uma zona inteira sem mexer na contagem de cima. Tem que entregar as MESMAS
			// celulas que a pergunta celula a celula, e cada uma uma vez so.
			ZoneCollision plano = m;
			var vistas = new HashSet<(int, int)>();
			int foraDoPlano = 0;
			plano.ParaCadaCelulaQueNasceuDentro((x, y) =>
			{
				if (!plano.NasceuDentro(x, y)) foraDoPlano++;
				vistas.Add((x, y));
			});
			Conferir(vistas.Count == conta && foraDoPlano == 0,
					 $"{arq}: o visitador do plano entrega as mesmas {conta} celulas ({vistas.Count} distintas, {foraDoPlano} fora do plano)");
		}

		// ============================ 2. NENHUM `.dentro` SOBRANDO ============================
		// Um arquivo que nao esta na lista e um andar que GANHOU interior sem ninguem conferir, ou um
		// resto de conversao antiga. Nos dois casos o jogo confiaria nele.
		Console.WriteLine("\n  [2] nao ha `.dentro` fora da lista");
		var esperados = new HashSet<string>(Esperados.Select(e => e.Arq), StringComparer.Ordinal);
		string[] sobrando = Directory.Exists(pastaMaps)
			? [.. Directory.GetFiles(pastaMaps, "*.dentro")
				.Select(f => Path.GetFileNameWithoutExtension(f))
				.Where(n => !esperados.Contains(n)).OrderBy(n => n, StringComparer.Ordinal)]
			: [];
		Conferir(sobrando.Length == 0,
				 sobrando.Length == 0 ? "nenhum `.dentro` no disco fora dos 20 andares da lista"
									  : "arquivo(s) fora da lista: " + string.Join(", ", sobrando));

		// ============================ 3. O BANCO, CELULA A CELULA ============================
		// E a prova de que o plano nao saiu espelhado nem deslocado: o retangulo tem que cair EM CIMA
		// do predio, e o anel de uma celula em volta dele tem que estar inteiro do lado de fora.
		Console.WriteLine("\n  [3] o Banco da Terra: o retangulo 12x6 inteiro, e nada em volta");
		if (planos.TryGetValue("z01_Earth", out ZoneCollision? terra))
		{
			int dentro = 0, anelFora = 0, anel = 0;
			for (int y = BancoY0 - 1; y <= BancoY1 + 1; y++)
				for (int x = BancoX0 - 1; x <= BancoX1 + 1; x++)
				{
					bool noPredio = x >= BancoX0 && x <= BancoX1 && y >= BancoY0 && y <= BancoY1;
					bool sob = CelulaInterna.SobTeto(terra, x, y, caiu: false);
					if (noPredio) { if (sob) dentro++; }
					else { anel++; if (!sob) anelFora++; }
				}
			Conferir(dentro == 72, $"as 72 celulas do predio estao sob teto ({dentro} de 72)");
			Conferir(anelFora == anel, $"o anel em volta do predio esta inteiro ao ar livre ({anelFora} de {anel})");

			Conferir(CelulaInterna.SobTeto(terra, BercoX, BercoY, caiu: false) && !terra.BlockedCell(BercoX, BercoY),
					 $"o berco ({BercoX},{BercoY}) esta sob teto e e chao livre");
			Conferir(CelulaInterna.SobTeto(terra, PortaX, PortaY, caiu: false),
					 $"a porta ({PortaX},{PortaY}) e do predio");
			Conferir(!CelulaInterna.SobTeto(terra, PortaX, PortaY + 1, caiu: false)
					 && !terra.BlockedCell(PortaX, PortaY + 1),
					 $"um passo pra fora da porta ({PortaX},{PortaY + 1}) e chao livre AO AR LIVRE");

			// ============================ 4. A CELULA QUE CAI VOLTA PRO LADO DE FORA ============================
			// `NewTurfs.dm:13-17`. O bit do mapa nao muda (o arquivo e imutavel): quem desconta e o
			// `caiu` que cada ponta tira da propria lista de estrago.
			Console.WriteLine("\n  [4] celula interna destruida deixa de estar sob teto");
			Conferir(!CelulaInterna.SobTeto(terra, BercoX, BercoY, caiu: true),
					 "o piso do Banco, derrubado, passa a ser ar livre");
			Conferir(terra.NasceuDentro(BercoX, BercoY),
					 "...e o bit cru do mapa continua ligado (por isso ninguem deve pergunta-lo no lugar do `SobTeto`)");

			// ============================ OS CONTRA-EXEMPLOS ============================
			// Cada defeito e ligado, a pergunta de producao e refeita, e a resposta TEM que ser a
			// errada -- e o que prova que as conferencias acima ficariam vermelhas com ele.
			Console.WriteLine("\n  [5] os dois defeitos injetados mudam a resposta");
			CelulaInterna.SemPlanoDeTeste = true;
			try
			{
				Conferir(!CelulaInterna.SobTeto(terra, BercoX, BercoY, caiu: false),
						 "(defeito injetado: o plano nao vale) o berco deixa de estar sob teto -- a [3] reprova");
			}
			finally { CelulaInterna.SemPlanoDeTeste = false; }

			CelulaInterna.CaidaContinuaInternaDeTeste = true;
			try
			{
				Conferir(CelulaInterna.SobTeto(terra, BercoX, BercoY, caiu: true),
						 "(defeito injetado: a caida continua interna) o piso derrubado segue sob teto -- a [4] reprova");
			}
			finally { CelulaInterna.CaidaContinuaInternaDeTeste = false; }
		}
		else Conferir(false, "sem o plano da Terra nao ha como conferir o Banco");

		// ============================ 6. DE ONDE VEIO CADA CELULA, CONTRA O `.dmm` ============================
		// A contagem total da [1] fecharia com as fontes trocadas (36 a mais de area e 36 a menos de
		// turf erguido). Aqui cada celula do plano e classificada pelo que o `.dmm` diz DELA, e as
		// tres colunas tem que bater uma a uma.
		Console.WriteLine("\n  [6] cada celula do plano tem a fonte que a lista diz");
		foreach ((string _, DmmMap.Result dados, int off) in MapConverter.LerMapas(pastaDmm))
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = MapConverter.NomeDoAndar(dados, nivel, off);
				if (!planos.TryGetValue(nome, out ZoneCollision? plano)) continue;
				(string _, int areaEsp, int erguidoEsp, int cidadeEsp) = Esperados.First(e => e.Arq == nome);

				int deArea = 0, deErguido = 0, outras = 0;
				for (int y = 0; y < nivel.Height; y++)
					for (int x = 0; x < nivel.Width; x++)
					{
						if (!plano.NasceuDentro(x, y)) continue;
						string? k = nivel.Cells[x, y];
						string? ultimoTurf = null, area = null;
						if (k != null && dados.Keys.TryGetValue(k, out string[]? tipos))
							foreach (string tp in tipos)
							{
								string bp = DmmMap.BasePath(tp);
								if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
								else if (bp.StartsWith("/area", StringComparison.Ordinal)) area = bp;
							}
						if (area != null && Interiores.EhAreaInterna(area)) deArea++;
						else if (ultimoTurf != null && Interiores.EhTurfErguido(ultimoTurf)) deErguido++;
						else outras++;
					}

				// O que nao e area nem turf erguido SO pode ser a cidade de Vegeta, que nao esta no `.dmm`.
				Conferir(deArea == areaEsp && deErguido == erguidoEsp && outras == cidadeEsp,
						 $"{nome}: area {deArea}/{areaEsp}, turf erguido {deErguido}/{erguidoEsp}, "
						 + $"fora do .dmm {outras}/{cidadeEsp}");
			}

		Console.WriteLine("\n============================================================");
		Console.WriteLine($" {_ok} ok, {_falhas} falha(s)");
		Console.WriteLine("============================================================");
		return _falhas;
	}
}
