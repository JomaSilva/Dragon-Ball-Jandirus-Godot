using System.Diagnostics;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA CAMADA DE SISTEMAS SOLARES (`sistemas`).
///
/// ============================ O QUE ELA TEM QUE PROVAR, E POR QUE ============================
/// A camada de sistemas trocou o universo inteiro por baixo do jogo: onde havia um planeta solto a
/// cada 40 chunks, ha uma estrela por celula de 32x32 com 1 a 10 planetas em orbita. Duas coisas
/// nessa troca nao doem na hora e doem depois:
///
///   1. A TERRA E O ZERO DAS COORDENADAS. O sistema dela nasce ANCORADO nela -- a estrela e que se
///      muda pra perto do planeta. Se em algum ponto do caminho a posicao da Terra passar a ser
///      RECALCULADA (`estrela + direcao * semieixo`) em vez de devolvida literal, ela volta com erro
///      de arredondamento de `float`, e tudo que se medir depois estara medindo outro universo. Por
///      isso a PRIMEIRA secao desta bancada compara bit a bit, e as outras so rodam se ela passar.
///
///   2. TETO QUE NAO DISPARA E TETO NENHUM (regra 0.7). Esta camada tem tres limites de verdade --
///      o raio letal de 2.048 px, as 10 orbitas e as celulas anuladas pelos ancorados -- e a bancada
///      faz os TRES dispararem, com o caso adversarial e nao com amostra aleatoria.
/// ==========================================================================================
///
/// O QUE ELA NAO PROVA, e nao tem como: que o DESENHO da estrela casa com o raio letal. Isso e
/// pixel, e a assinatura nao tem pixel nenhum dentro dela -- vai ficar verde com a estrela matando
/// 37% fora de onde parece. A prova daquilo e uma FOTO com o anel da coroa desenhado sobre o raio.
///
///     dotnet run --project Tools/AssetPipeline -- sistemas [seed]
/// </summary>
public static class SistemasBench
{
	private static int _falhas;

	private static void Afirmar(bool ok, string oque)
	{
		if (!ok) _falhas++;
		Console.WriteLine($"  [{(ok ? "OK  " : "FALHA")}] {oque}");
	}

	/// <summary>
	/// O DEFEITO INJETADO: a MESMA conta, com a entrada estragada de proposito. Ela tem que REPROVAR
	/// -- e e isso, e so isso, que prova que a checagem irma tem dentes.
	///
	/// A arvore deste projeto esta estavel: nenhuma das afirmacoes desta bancada fica vermelha hoje.
	/// Uma bancada inteira de linhas verdes que ninguem sabe como derrubar e indistinguivel de uma
	/// bancada que nao confere nada (regra 0.7 escrita de outro jeito), entao cada familia daqui traz
	/// junto o universo em que ela cai.
	/// </summary>
	private static void Contraprova(bool reprovou, string oque)
	{
		if (!reprovou) _falhas++;
		Console.WriteLine($"  [{(reprovou ? "OK  " : "FALHA")}] [defeito injetado] {oque}");
	}

	/// <summary>Os bits de um `float`. Comparacao de posicao de pre-feito nao usa tolerancia.</summary>
	private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

	public static void Run(ulong seed)
	{
		_falhas = 0;

		ATerraNaoAndou();
		OsAncorados(seed);
		OsTetos(seed);
		AsDistancias(seed);
		AAssinatura(seed);
		OCusto(seed);
		AGrade(seed);

		Console.WriteLine();
		Console.WriteLine(_falhas == 0
			? "=== TUDO VERDE ==="
			: $"=== {_falhas} FALHA(S) -- nao siga em frente ===");
	}

	// =====================================================================
	// 1) A TERRA NAO ANDOU
	// =====================================================================
	/// <summary>
	/// A PROVA DE QUE OS SETE PRE-FEITOS FICARAM ONDE ESTAVAM.
	///
	/// Ela nao compara "perto o bastante": compara os `float` BIT A BIT (`BitConverter`), porque a
	/// falha que se teme aqui e exatamente de um ULP -- a que passa despercebida por qualquer
	/// tolerancia que alguem escolheria.
	///
	/// E ela pergunta pelo CAMINHO DE PRODUCAO, nao pela lista: pega o planeta da orbita do pre-feito
	/// no sistema ancorado (`SistemaSolar.Planeta`) e pelo `Espaco.PorPerto` da chunk dele, que e por
	/// onde o servidor e o pouso enxergam o universo. Comparar `PreFeitos()` com `PreFeitos()` daria
	/// verde com o jogo quebrado.
	/// </summary>
	private static void ATerraNaoAndou()
	{
		Console.WriteLine("=== 1) OS SETE PRE-FEITOS NAO SE MOVERAM (comparacao BIT A BIT) ===");

		// A Terra e o zero das coordenadas: o literal, antes de qualquer coisa.
		PlanetaNoEspaco terra = Espaco.PreFeitos().First();
		Afirmar(terra.Nome == "Earth" && terra.Pos.X == 0f && terra.Pos.Y == 0f,
			$"a Terra e o primeiro pre-feito e esta em (0,0) -- lida: {terra.Pos}");

		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			SistemaSolar s = Sistemas.ComPreFeito.First(a => a.PreFeito.Nome == p.Nome);
			PlanetaNoEspaco pelaOrbita = s.Planeta(s.OrbitaPreFeita);

			bool bitABit = BitConverter.SingleToInt32Bits(pelaOrbita.Pos.X) == BitConverter.SingleToInt32Bits(p.Pos.X)
						&& BitConverter.SingleToInt32Bits(pelaOrbita.Pos.Y) == BitConverter.SingleToInt32Bits(p.Pos.Y)
						&& pelaOrbita.Raio == p.Raio && pelaOrbita.Seed == p.Seed && pelaOrbita.Premade;

			// E PELO CAMINHO QUE O SERVIDOR USA. Se `PorPerto` deixar de achar um pre-feito, o
			// planeta some do jogo sem nenhum erro -- e o sintoma seria "a Terra sumiu do mapa".
			List<PlanetaNoEspaco> perto = Espaco.PorPerto(0, p.Chunk);
			bool achado = perto.Any(q => q.Nome == p.Nome
									  && BitConverter.SingleToInt32Bits(q.Pos.X) == BitConverter.SingleToInt32Bits(p.Pos.X)
									  && BitConverter.SingleToInt32Bits(q.Pos.Y) == BitConverter.SingleToInt32Bits(p.Pos.Y));

			// A SEED DO MUNDO NAO PODE MEXER NELES. Os ancorados saem do nome, nao da seed: se um
			// dia alguem os fizer depender da seed, cada servidor teria uma Terra num lugar.
			List<PlanetaNoEspaco> outraSeed = Espaco.PorPerto(0xDEADBEEF, p.Chunk);
			bool estavel = outraSeed.Any(q => q.Nome == p.Nome
										   && BitConverter.SingleToInt32Bits(q.Pos.X) == BitConverter.SingleToInt32Bits(p.Pos.X));

			Afirmar(bitABit && achado && estavel,
				$"{p.Nome,-12} literal {p.Pos} == orbita#{s.OrbitaPreFeita} do sistema ancorado, "
				+ $"visivel em PorPerto{(estavel ? " e igual em qualquer seed" : " MAS MUDA COM A SEED")}");
		}

		// ============================ E A COMPARACAO TEM DENTES? ============================
		// "Bit a bit" so vale alguma coisa se um bit importar. O defeito injetado e o menor que existe
		// -- UM ULP -- porque e exatamente ele que uma tolerancia escolhida a olho deixaria passar, e
		// e ele que a Terra ganharia se `Planeta(k)` voltasse a CALCULAR a posicao do pre-feito.
		PlanetaNoEspaco namek = Espaco.PreFeitos().ElementAt(1);
		float umUlp = BitConverter.Int32BitsToSingle(Bits(namek.Pos.X) + 1);
		Contraprova(Bits(umUlp) != Bits(namek.Pos.X),
			$"um ULP em Namek e REJEITADO ({namek.Pos.X:F4} contra {umUlp:F4}, diferenca {umUlp - namek.Pos.X:E2} px)");

		// E O `PorPerto` NAO DIZ SIM PRA TUDO. Sem esta linha, um `PorPerto` que devolvesse os sete
		// pre-feitos em qualquer chunk do universo passaria em todas as linhas acima.
		Contraprova(!Espaco.PorPerto(0, new ChunkId(5_000, 5_000)).Exists(q => q.Nome == "Earth"),
			"a Terra NAO aparece no `PorPerto` de uma chunk a 10 milhoes de px -- a busca olha onde diz que olha");
	}

	// =====================================================================
	// 2) OS SISTEMAS ANCORADOS
	// =====================================================================
	private static void OsAncorados(ulong seed)
	{
		Console.WriteLine();
		Console.WriteLine("=== 2) OS SISTEMAS ANCORADOS: a estrela e que se mudou ===");

		foreach (SistemaSolar s in Sistemas.ComPreFeito)
		{
			double a = s.Semieixo(s.OrbitaPreFeita);
			double d = (s.PreFeito.Pos - s.Estrela.Pos).Length;
			Console.WriteLine($"  {s.PreFeito.Nome,-12} em ({s.PreFeito.Pos.X,11:N0},{s.PreFeito.Pos.Y,11:N0})  "
				+ $"{s.Estrela.Classe,-15} R={s.Estrela.Raio,5:N0}  estrela em ({s.Estrela.Pos.X,11:N0},{s.Estrela.Pos.Y,11:N0})  "
				+ $"orbita#{s.OrbitaPreFeita} a {a,6:N0} px  celula {s.Id}  Rsys={s.RaioDoSistema:N0}");

			// A distancia real ate a estrela tem que ser o semieixo (a menos do arredondamento de
			// float ao guardar a posicao da estrela).
			Afirmar(Math.Abs(d - a) < 1.0, $"    {s.PreFeito.Nome}: distancia ate a estrela = semieixo ({d:N1} vs {a:N1})");
			Afirmar(d > s.Estrela.Raio + Sistemas.FolgaDaCoroa - 1,
				$"    {s.PreFeito.Nome}: esta FORA da coroa da propria estrela");
		}

		// A TERRA ORBITA UM SOL AMARELO POR REGRA ESCRITA, e nao por hash.
		SistemaSolar sol = Sistemas.ComPreFeito.First(x => x.PreFeito.Nome == "Earth");
		Afirmar(sol.Estrela.Classe == ClasseDeEstrela.Amarela,
			$"a estrela da Terra e Amarela por regra escrita (lida: {sol.Estrela.Classe})");

		// DOIS ANCORADOS NA MESMA CELULA seria uma estrela comendo a outra -- e o `Sistemas.Do`
		// devolveria sempre o primeiro da lista, calado.
		bool colididos = Sistemas.ComPreFeito.GroupBy(x => x.Id).Any(g => g.Count() > 1);
		Afirmar(!colididos, "nenhum par de sistemas ancorados divide a mesma celula");

		bool sobrepostos = false;
		foreach (SistemaSolar a in Sistemas.ComPreFeito)
			foreach (SistemaSolar b in Sistemas.ComPreFeito)
				if (!ReferenceEquals(a.PreFeito.Nome, b.PreFeito.Nome) && a.PreFeito.Nome != b.PreFeito.Nome)
					if ((a.Estrela.Pos - b.Estrela.Pos).Length < a.RaioDoSistema + b.RaioDoSistema) sobrepostos = true;
		Afirmar(!sobrepostos, "nenhum par de sistemas ancorados se sobrepoe");

		// O DEFEITO INJETADO NA MESMA CONTA: dois ancorados postos NO MESMO PONTO tem que acusar
		// sobreposicao. Sem isto, a linha acima ficaria verde se o criterio estivesse invertido ou se
		// `RaioDoSistema` viesse zerado -- os dois defeitos que ela existe pra pegar.
		SistemaSolar p0 = Sistemas.ComPreFeito[0], p1 = Sistemas.ComPreFeito[1];
		Contraprova((p0.Estrela.Pos - p0.Estrela.Pos).Length < p0.RaioDoSistema + p1.RaioDoSistema,
			$"o mesmo criterio ACUSA quando dois sistemas dividem o ponto ({p0.RaioDoSistema + p1.RaioDoSistema:N0} px "
			+ "de raio somado contra 0 de distancia)");

		// ============================ A GUARDA DA CELULA ANULADA ============================
		// Ela e a mais perigosa deste arquivo: com os sete pre-feitos de hoje e a seed de hoje ela
		// NUNCA dispara, ou seja, e indistinguivel de nao existir (regra 0.7 / Parte 3 item 1). E
		// ela nao e supérflua -- um ancorado pode nascer colado na divisa da celula dele, e ai um
		// sistema gerado vizinho, que so e obrigado a ficar o RAIO DELE PROPRIO da divisa, alcanca.
		//
		// ============================ E DESDE O `VaziosPor256` ELA CONTA NULO ERRADO ============================
		// Contar `Do(...) is null` era o teste certo enquanto o unico nulo era esta guarda. Hoje 37,5%
		// das celulas sao sorteadas VAZIAS, entao "achei um nulo" ficaria verde na primeira seed
		// olhada, provando o sorteio e nao a guarda -- exatamente o modo de falha que esta secao
		// inteira existe pra evitar. Por isso o teste passou a ler o MOTIVO (`CelulaVazia`).
		// ====================================================================================================
		var celulasAncoradas = Sistemas.ComPreFeito.Select(x => x.Id).ToHashSet();

		int AnuladasPerto(ulong s)
		{
			int n = 0;
			foreach (SistemaId c in celulasAncoradas)
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
					{
						var alvo = new SistemaId(c.Sx + dx, c.Sy + dy);
						if (celulasAncoradas.Contains(alvo)) continue;
						Sistemas.Do(s, alvo.Sx, alvo.Sy, out CelulaVazia porque);
						if (porque == CelulaVazia.AnuladaPorAncorado) n++;
					}
			return n;
		}

		int anuladasNaSeed = 0, vaziasNaSeed = 0, visitadas = 0;
		for (int sy = -40; sy <= 40; sy++)
			for (int sx = -40; sx <= 40; sx++)
			{
				if (celulasAncoradas.Contains(new SistemaId(sx, sy))) continue;
				visitadas++;
				Sistemas.Do(seed, sx, sy, out CelulaVazia p);
				if (p == CelulaVazia.AnuladaPorAncorado) anuladasNaSeed++;
				else if (p == CelulaVazia.Sorteada) vaziasNaSeed++;
			}
		Console.WriteLine($"  na seed {seed}: {visitadas:N0} celulas geradas visitadas, {anuladasNaSeed} ANULADAS pela guarda, "
						  + $"{vaziasNaSeed:N0} sorteadas vazias ({vaziasNaSeed * 100.0 / visitadas:F1}%)");

		ulong seedQueDispara = 0; int quantasLa = 0;
		for (ulong s = 1; s <= 5000 && seedQueDispara == 0; s++)
			if (AnuladasPerto(s) is > 0 and var q) { seedQueDispara = s; quantasLa = q; }
		Console.WriteLine($"  primeira seed em que a guarda DISPARA: {seedQueDispara} ({quantasLa} celula(s) anulada(s))");
		Afirmar(seedQueDispara != 0, "a guarda da celula anulada esta LIGADA (ha seed em que ela recusa uma celula)");
		Afirmar(anuladasNaSeed < visitadas / 100, "e ela e rara: nunca chega a 1% das celulas");

		// A CONTRAPROVA DO MOTIVO: os dois nulos tem que ser DISTINGUIVEIS. Se `CelulaVazia` fosse
		// escrito e nunca lido -- ou se o `out` devolvesse sempre o mesmo -- a linha de cima ficaria
		// verde de graca. Aqui se exige que os DOIS valores aconteçam na mesma varredura.
		Afirmar(vaziasNaSeed > 0 && AnuladasPerto(seedQueDispara) > 0,
			"os DOIS motivos de celula vazia acontecem e sao distinguiveis (sorteio x guarda)");
	}

	// =====================================================================
	// 3) OS TETOS
	// =====================================================================
	private static void OsTetos(ulong seed)
	{
		Console.WriteLine();
		Console.WriteLine("=== 3) OS TETOS DISPARAM (caso ADVERSARIAL, nao amostra) ===");

		// TETO DO RAIO LETAL. `PlanetaSob` varre o 3x3 de chunks em volta de quem toca: um corpo cujo
		// centro fique a mais de 1 chunk da borda dele e ATRAVESSAVEL SEM MORRER. O caso adversarial
		// e o centro colado na divisa da chunk -- amostra aleatoria acha isso quase nunca.
		Console.WriteLine("  raio      pior distancia centro..borda");
		foreach (double R in new double[] { 199, 2047, Sistemas.RaioLetalMaximo, 2049, 4096 })
		{
			int pior = 0;
			for (int k = 0; k < 4096; k++)
			{
				double cx = Espaco.ChunkPx * 10 - 0.001 - k * 0.0007;
				foreach (int s in new[] { -1, 1 })
				{
					ChunkId a = ChunkId.De(new Vec2((float)cx, 0));
					ChunkId b = ChunkId.De(new Vec2((float)(cx + s * R), 0));
					pior = Math.Max(pior, Math.Abs(a.X - b.X));
				}
			}
			Console.WriteLine($"  {R,7:N0} px  {pior} chunk(s)  {(pior <= Espaco.RaioAtivo ? "detectavel" : "INVISIVEL com RaioAtivo=" + Espaco.RaioAtivo)}");
			if (R <= Sistemas.RaioLetalMaximo)
				Afirmar(pior <= Espaco.RaioAtivo, $"  raio {R:N0} (<= teto) e detectavel pelo 3x3");
			else
				Afirmar(pior > Espaco.RaioAtivo, $"  raio {R:N0} (> teto) seria INVISIVEL -- e por isso o teto existe");
		}

		double maiorRaio = Enum.GetValues<ClasseDeEstrela>().Max(Sistemas.RaioDaClasse);
		Afirmar(maiorRaio <= Sistemas.RaioLetalMaximo,
			$"nenhuma classe passa do teto ({maiorRaio:N0} <= {Sistemas.RaioLetalMaximo:N0})");
		Afirmar(Sistemas.ComPreFeito.Concat(Amostra(seed, 60)).Any(s => s.Estrela.Raio >= Sistemas.RaioLetalMaximo),
			"o TETO do raio letal DISPARA: ha estrelas exatamente nele");

		// TETO DAS ORBITAS.
		SistemaSolar[] amostra = Amostra(seed, 150).ToArray();
		int maiorN = amostra.Max(s => s.Orbitas), menorN = amostra.Min(s => s.Orbitas);
		double mediaN = amostra.Average(s => s.Orbitas);
		Console.WriteLine($"  orbitas por sistema em {amostra.Length:N0} sistemas: min {menorN}, max {maiorN}, media {mediaN:F2}");
		Afirmar(maiorN == Sistemas.PlanetasMaximo, "o TETO de 10 orbitas DISPARA");
		Afirmar(menorN == 1, "e o piso de 1 orbita tambem");

		// A DISTRIBUICAO DE CLASSES: a arte disponivel tem que cobrir tudo o que sai do hash.
		Console.WriteLine("  classe                raio   fatia");
		foreach (ClasseDeEstrela c in Enum.GetValues<ClasseDeEstrela>())
		{
			int n = amostra.Count(s => s.Estrela.Classe == c);
			Console.WriteLine($"  {c,-18} {Sistemas.RaioDaClasse(c),6:N0} px  {n * 100.0 / amostra.Length,5:F1}%");
			Afirmar(n > 0, $"  a classe {c} sai do hash (a arte dela nao esta parada)");
		}
	}

	// =====================================================================
	// 4) AS DISTANCIAS
	// =====================================================================
	private static void AsDistancias(ulong seed)
	{
		Console.WriteLine();
		Console.WriteLine("=== 4) AS DISTANCIAS: o que o desenho promete, medido ===");

		SistemaSolar[] amostra = Amostra(seed, 150).ToArray();

		// (a) NENHUM CORPO NASCE DENTRO DA COROA. E a invariante que faz o teto do raio letal e a
		// distancia entre orbitas caberem juntos.
		double menorFolga = double.MaxValue;
		double maiorRsys = 0;
		int habitaveis = 0, semHabitavel = 0;
		foreach (SistemaSolar s in amostra)
		{
			maiorRsys = Math.Max(maiorRsys, s.RaioDoSistema);
			int naFaixa = 0;
			for (int k = 0; k < s.Orbitas; k++)
			{
				PlanetaNoEspaco p = s.Planeta(k);
				double d = (p.Pos - s.Estrela.Pos).Length;
				menorFolga = Math.Min(menorFolga, d - s.Estrela.Raio - p.Raio);
				if (s.Habitavel(k)) naFaixa++;
			}
			habitaveis += naFaixa;
			if (naFaixa == 0) semHabitavel++;
		}
		// ============================ 1 PX DE ERRO A 10 MILHOES DE PX ============================
		// A folga TEORICA e 900 - 199 = 701 px, e a medida da 700. A diferenca nao e bug: `Vec2`
		// guarda `float`, e a 1e7 px de distancia da origem um `float` so representa multiplos de
		// ~1 px. Isso e irrelevante pro jogo (a chunk tem 2048 px e o menor corpo tem 110), mas
		// qualquer asercao apertada aqui ficaria vermelha em regiao longe e verde perto da Terra --
		// que e a pior forma de teste que existe. A tolerancia e explicita e o motivo esta escrito.
		// ====================================================================================
		Console.WriteLine($"  menor folga entre a superficie de um planeta e a da estrela: {menorFolga:N1} px "
						  + "(teorica 701; o `float` do Vec2 vale ~1 px a 1e7 de distancia)");
		Afirmar(menorFolga > 0, "nenhum planeta nasce DENTRO da estrela");
		Afirmar(menorFolga >= Sistemas.FolgaDaCoroa - 199 - 4,
			$"a folga da coroa ({Sistemas.FolgaDaCoroa:N0}) cobre o maior planeta (199 px)");

		Console.WriteLine($"  maior raio de sistema medido: {maiorRsys:N0} px (teto {Sistemas.RaioSistemaTeto:N0})");
		Afirmar(maiorRsys <= Sistemas.RaioSistemaTeto,
			"nenhum sistema passa do teto -- e dele que sai a caixa livre e a separacao garantida");

		// ============================ A GARANTIA MUDOU DE FORMA, E ELA E POR PAR ============================
		// Era: a caixa livre punha toda estrela a `RaioSistemaTeto` da divisa, logo duas geradas
		// distavam SEMPRE 46.000 px -- um numero unico, igual pra todo mundo, e a reticula que o dono
		// enxergou na carta. Hoje a margem e o raio do PROPRIO sistema, entao nao ha mais "a separacao
		// minima": ha uma por par, `rsysA + rsysB + 2*FolgaEntreSistemas`. A afirmacao segue a mesma
		// invariante e nao o mesmo NUMERO -- afirmar 46.000 hoje seria afirmar a reticula de volta.
		//
		// O ancorado continua fora da caixa (nasce onde o pre-feito esta), e por isso ele entra so na
		// medida de SOBRA, que e a invariante que vale pros dois casos.
		// =================================================================================================
		double menorSepGerada = double.MaxValue, menorSobra = double.MaxValue, menorSobraGerada = double.MaxValue;
		int paresGerados = 0;
		for (int sy = -60; sy < 60; sy++)
			for (int sx = -60; sx < 60; sx++)
			{
				if (Sistemas.Do(seed, sx, sy) is not { } a) continue;
				foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
				{
					if (Sistemas.Do(seed, sx + dx, sy + dy) is not { } b) continue;
					double dist = (a.Estrela.Pos - b.Estrela.Pos).Length;
					double sobra = dist - a.RaioDoSistema - b.RaioDoSistema;
					if (!a.Ancorado && !b.Ancorado)
					{
						paresGerados++;
						menorSepGerada = Math.Min(menorSepGerada, dist);
						menorSobraGerada = Math.Min(menorSobraGerada, sobra);
					}
					menorSobra = Math.Min(menorSobra, sobra);
				}
			}
		Console.WriteLine($"  {paresGerados:N0} pares de gerados vizinhos: menor separacao entre as ESTRELAS {menorSepGerada:N0} px, "
						  + $"menor sobra entre as BORDAS {menorSobraGerada:N0} px");
		Afirmar(menorSobraGerada >= 2 * Sistemas.FolgaEntreSistemas - 8,
			$"todo par de gerados guarda a folga de 2x{Sistemas.FolgaEntreSistemas:N0} px -- e a desigualdade "
			+ "`margemA+margemB >= rsysA+rsysB` valendo celula a celula, sem consultar vizinho");

		Console.WriteLine($"  menor sobra entre as BORDAS de dois sistemas vizinhos quaisquer: {menorSobra:N0} px");
		Afirmar(menorSobra > 0, "nenhum par de sistemas vizinhos se sobrepoe, ancorado incluido");

		// (b2) O POUSO AINDA ENXERGA OS CORPOS. `PlanetaSob` varre `Espaco.PorPerto`, que passou a
		// enumerar CELULAS em vez de chunks -- se o corte novo errar por uma celula, o planeta some
		// da deteccao e o jogador atravessa o mundo sem pousar, calado. Confere o corpo mais externo
		// de cada sistema, que e o que fica mais longe da celula da estrela.
		int conferidos = 0, achados = 0, falsosPositivos = 0;
		foreach (SistemaSolar s in amostra.Take(2000))
		{
			PlanetaNoEspaco p = s.Planeta(s.Orbitas - 1);
			if (p.Premade) continue;
			conferidos++;
			if (Espaco.PlanetaSob(seed, p.Pos) is { } sob && sob.Nome == p.Nome) achados++;
			// e o ponto de decolagem, que nasce FORA do disco, nao pode disparar o pouso
			if (Espaco.PlanetaSob(seed, Espaco.PontoDeDecolagem(p)) is not null) falsosPositivos++;
		}
		Console.WriteLine($"  pouso: {achados:N0}/{conferidos:N0} corpos externos detectados por PlanetaSob; "
						  + $"{falsosPositivos} pousos falsos no ponto de decolagem");
		Afirmar(conferidos > 0 && achados == conferidos, "todo corpo e detectado pelo caminho de pouso de verdade");
		Afirmar(falsosPositivos == 0, "e o ponto de decolagem nao dispara pouso (senao nunca se sairia do planeta)");

		// (c) A ZONA HABITAVEL significa alguma coisa.
		Console.WriteLine($"  zona habitavel [3R,7R]: {(amostra.Length - semHabitavel) * 100.0 / amostra.Length:F0}% "
						  + $"dos sistemas tem ao menos uma orbita nela ({habitaveis:N0} orbitas no total)");
		Afirmar(semHabitavel > 0 && semHabitavel < amostra.Length / 2,
			"a faixa quase sempre significa alguma coisa, e as vezes significa 'este sistema e morto'");

		// (d) TEMPOS DE VOO. O numero que decide se o sistema e navegavel a mao.
		Console.WriteLine();
		void T(string o, double px) =>
			Console.WriteLine($"  {o,-48} {px,8:N0} px  {px / MoveRules.BaseSpeedPx,6:F0} s base   "
							  + $"({px / (MoveRules.BaseSpeedPx * 11),5:F0} s no teto de 11x)");
		T("estrela -> 1a orbita", Sistemas.FolgaDaCoroa);
		T("orbita -> orbita vizinha (minimo)", Sistemas.PassoMinimo);
		T("orbita -> orbita vizinha (maximo)", Sistemas.PassoMaximo);
		T("PIOR caso a mao (orbitas opostas, maior sistema)", 2 * maiorRsys);
		T("estrela -> estrela vizinha (o menor par medido)", menorSepGerada);
		T("estrela -> estrela vizinha (media da grade)", Sistemas.CelulaPx);

		// (e) TERRA -> NAMEK CONTINUA CUSTANDO SETE DIAS. E a escala de que sai todo o resto.
		//
		// ============================ MEDIDA DAS DUAS POSICOES, E NAO DA CONSTANTE ============================
		// Esta linha ja comparou `Espaco.DistanciaTerraNamek` com ela mesma -- um numero conferido
		// contra si proprio, que fica verde com os dois planetas em qualquer lugar. Quem manda sao as
		// duas POSICOES LITERAIS de `Espaco.PreFeitos()`, e a viagem de verdade mede 6,98 dias: Namek
		// esta em `(d*0,71, -d*0,70)`, um vetor de comprimento `d*0,99702` e nao `d`.
		//
		// A bancada AFIRMA os 6,98 e nao conserta: o conserto e mover Namek, e a posicao de um
		// pre-feito ancora o sistema solar dele -- mudaria a celula, a estrela e a assinatura do
		// universo pra todo mundo que ja tem personagem. O erro vale 0,3% (30 s em 168 min).
		// ==================================================================================================
		PlanetaNoEspaco terraP = Espaco.PreFeitos().First(), namekP = Espaco.PreFeitos().ElementAt(1);
		double terraNamek = (namekP.Pos - terraP.Pos).Length;
		double diasReais = Espaco.DiasInGame(terraNamek);
		Console.WriteLine();
		Console.WriteLine($"  Terra -> Namek pelas posicoes LITERAIS: {terraNamek:N0} px = {diasReais:F2} dias in-game "
						  + $"= {Espaco.SegundosDeViagem(terraNamek) / 60:F0} min reais "
						  + $"(a constante diz {Espaco.DistanciaTerraNamek:N0} px = {Espaco.DiasInGame(Espaco.DistanciaTerraNamek):F2} dias)");
		Afirmar(Math.Abs(diasReais - 7) < 0.05, "a viagem do anime continua custando 7 dias in-game");
		Afirmar(Math.Abs(terraNamek - Espaco.DistanciaTerraNamek) / Espaco.DistanciaTerraNamek < 0.005,
			"...e ela fica a 0,3% da constante derivada -- a diferenca e o vetor (0,71 / -0,70) nao ter comprimento 1");

		// O DEFEITO INJETADO: a mesma conta com o dobro da distancia NAO pode passar por sete dias.
		// Uma tolerancia frouxa aqui deixaria uma mudanca de escala do universo inteiro passar calada.
		Contraprova(Math.Abs(Espaco.DiasInGame(terraNamek * 2) - 7) >= 0.05,
			$"o dobro da distancia da {Espaco.DiasInGame(terraNamek * 2):F2} dias e REPROVA -- a tolerancia de 0,05 dia tem dentes");
	}

	// =====================================================================
	// 5) A ASSINATURA
	// =====================================================================
	private static void AAssinatura(ulong seed)
	{
		Console.WriteLine();
		Console.WriteLine("=== 5) A ASSINATURA: e ela que prova que as duas pontas concordam ===");

		var relogio = Stopwatch.StartNew();
		ulong a1 = Sistemas.Assinatura(seed, -16, -16, 32);
		relogio.Stop();
		ulong a2 = Sistemas.Assinatura(seed, -16, -16, 32);
		ulong outra = Sistemas.Assinatura(seed + 1, -16, -16, 32);
		ulong desloc = Sistemas.Assinatura(seed, -15, -16, 32);

		int planetas = 0;
		for (int sy = -16; sy < 16; sy++)
			for (int sx = -16; sx < 16; sx++)
				if (Sistemas.Do(seed, sx, sy) is { } s) planetas += s.Orbitas;

		Console.WriteLine($"  32x32 celulas ({32 * 32:N0} sistemas, {planetas:N0} planetas): {a1:X16} "
						  + $"em {relogio.Elapsed.TotalMilliseconds:F2} ms");
		Afirmar(a1 == a2, "duas passadas com a mesma seed dao a MESMA assinatura");
		Afirmar(a1 != outra, $"a seed vizinha da outra ({outra:X16})");
		Afirmar(a1 != desloc, $"a regiao deslocada de uma celula da outra ({desloc:X16})");
		Afirmar(relogio.Elapsed.TotalMilliseconds < 33,
			"e ela cabe num tique de 33 ms -- nao precisa do Encomendar/TickDasGeracoes");

		// ============================ A SEED ENTRA NA ASSINATURA, E TEM QUE ENTRAR ============================
		// Uma tentacao aqui e afirmar que a celula da Terra assina IGUAL em duas seeds, ja que os
		// ancorados nao dependem da seed. E falso, e por bom motivo: a assinatura come a seed na
		// primeira linha, de proposito -- sem isso ela nao distinguiria dois servidores. Quem tem que
		// ser igual sao os CORPOS, e e isso que se compara.
		// ==================================================================================================
		SistemaId cTerra = Sistemas.ComPreFeito.First(x => x.PreFeito.Nome == "Earth").Id;
		SistemaSolar? tA = Sistemas.Do(seed, cTerra.Sx, cTerra.Sy);
		SistemaSolar? tB = Sistemas.Do(seed + 991, cTerra.Sx, cTerra.Sy);

		bool iguais = tA is { } x1 && tB is { } x2 && x1.Orbitas == x2.Orbitas
					  && x1.Estrela.Classe == x2.Estrela.Classe && x1.OrbitaPreFeita == x2.OrbitaPreFeita
					  && BitConverter.SingleToInt32Bits(x1.Estrela.Pos.X) == BitConverter.SingleToInt32Bits(x2.Estrela.Pos.X)
					  && Enumerable.Range(0, x1.Orbitas).All(k =>
							 BitConverter.SingleToInt32Bits(x1.Planeta(k).Pos.X) == BitConverter.SingleToInt32Bits(x2.Planeta(k).Pos.X)
						  && BitConverter.SingleToInt32Bits(x1.Planeta(k).Pos.Y) == BitConverter.SingleToInt32Bits(x2.Planeta(k).Pos.Y));

		Console.WriteLine($"  a celula da estrela da Terra {cTerra} em duas seeds: "
						  + $"{tA?.Estrela.Classe} R={tA?.Estrela.Raio:N0} / {tB?.Estrela.Classe} R={tB?.Estrela.Raio:N0}");
		Afirmar(iguais, "o sistema ancorado da Terra e IDENTICO em qualquer seed do mundo, corpo por corpo");
		Afirmar(Sistemas.Assinatura(seed, cTerra.Sx, cTerra.Sy, 1) != Sistemas.Assinatura(seed + 991, cTerra.Sx, cTerra.Sy, 1),
			"e mesmo assim a assinatura difere, porque ela come a seed -- e o que distingue dois servidores");

		// ============================ E A ASSINATURA TEM QUE TER MUDADO ============================
		// O universo mudou de forma nesta rodada, entao a assinatura de hoje NAO pode ser a de ontem.
		// Isso parece obvio e nao e: a assinatura come a enumeracao inteira, e um `Do` que ignorasse a
		// <see cref="RegraDaGrade"/> -- por exemplo, se a margem por celula tivesse sido escrita e
		// deixada orfa -- assinaria igual dos dois jeitos, e a carta continuaria uma reticula com a
		// bancada inteira verde. E a mesma familia de defeito que ja mordeu este projeto: o corte de
		// sigilo de BP foi escrito e a API ficou 100% orfa.
		//
		// A prova viva disto atravessa o fio na `--diaguniverso`, onde o "hoje" vem do PROCESSO do
		// servidor -- la ela tambem pega o servidor que ficou no build velho.
		ulong deHoje = Sistemas.Assinatura(seed, -16, -16, 32);
		ulong deOntem = Sistemas.Assinatura(seed, -16, -16, 32, RegraDaGrade.AntesDaReclamacao);
		Console.WriteLine($"  a mesma regiao nos dois universos: hoje {deHoje:X16} | antes {deOntem:X16}");
		Afirmar(deHoje != deOntem,
			"a assinatura MUDOU -- o universo de hoje nao e o da reclamacao, e os botoes da grade chegam ao resultado");
	}

	// =====================================================================
	// 6) O CUSTO
	// =====================================================================
	private static void OCusto(ulong seed)
	{
		Console.WriteLine();
		Console.WriteLine("=== 6) O CUSTO (regra 0.5: se mede, nao se estima) ===");

		// aquecimento: a primeira passada paga o JIT e mediria o compilador
		long lixo = 0;
		for (int i = 0; i < 200_000; i++) if (Sistemas.Do(seed, i, 3) is { } s) lixo += s.Orbitas;

		var relogio = Stopwatch.StartNew();
		const int N = 2_000_000;
		for (int i = 0; i < N; i++) if (Sistemas.Do(seed, i, 5) is { } s) lixo += s.Orbitas;
		relogio.Stop();
		double nsCelula = relogio.Elapsed.TotalMilliseconds * 1e6 / N;
		Console.WriteLine($"  Sistemas.Do (1 celula = 32x32 chunks): {nsCelula,6:F1} ns");

		// ============================ O CAMINHO QUENTE, E ELE TEM DOIS PRECOS ============================
		// `PorPerto` roda por tique por jogador (pelo `PlanetaSob` e pelo `MandarVizinhanca`), e o
		// custo dele NAO e um numero so: quando nenhum sistema alcanca, o prefiltro corta e nao se
		// monta planeta nenhum; quando um alcanca, cada orbita vira um `PlanetaNoEspaco` com NOME, e
		// montar string e o que domina (a `Assinatura` ja tinha medido isso).
		//
		// Medir so um chunk daria um numero que muda sozinho quando a densidade muda -- foi o que
		// aconteceu aqui: com o `VaziosPor256`, o chunk (700,-300) que antes custava 1,96 us passou a
		// custar 0,19 us porque a celula dele ficou VAZIA. O custo nao caiu 10x; a amostra e que
		// trocou de caso. Entao os dois casos sao medidos, e nomeados.
		// ==============================================================================================
		// O PIOR CASO E O CHUNK DA PROPRIA ESTRELA: dali o prefiltro nunca corta e todas as orbitas do
		// sistema entram na conta -- e com elas a montagem dos nomes.
		Vec2 naEstrela = Amostra(seed, 40).First(s => !s.Ancorado && s.Orbitas == Sistemas.PlanetasMaximo).Estrela.Pos;
		var dentro = new ChunkId((int)Math.Floor(naEstrela.X / Espaco.ChunkPx), (int)Math.Floor(naEstrela.Y / Espaco.ChunkPx));
		ChunkId vazio = ChunkVazio(seed);

		double UsDe(ChunkId c)
		{
			for (int i = 0; i < 20_000; i++) lixo += Espaco.PorPerto(seed, c).Count;
			var r = Stopwatch.StartNew();
			const int M = 200_000;
			for (int i = 0; i < M; i++) lixo += Espaco.PorPerto(seed, c).Count;
			r.Stop();
			return r.Elapsed.TotalMilliseconds * 1000 / M;
		}

		double usDentro = UsDe(dentro), usVazio = UsDe(vazio);
		Console.WriteLine($"  Espaco.PorPerto DENTRO de um sistema {dentro} ({Espaco.PorPerto(seed, dentro).Count} corpos): {usDentro,6:F2} us");
		Console.WriteLine($"  Espaco.PorPerto no VAZIO           {vazio} (0 corpos): {usVazio,6:F2} us   (lixo={lixo})");
		Afirmar(usDentro < 33.0, "o pior caso cabe num tique com folga, mesmo somado por jogador");

		// A CARTA ESTELAR no zoom mais aberto que existe. Era aqui que o teto de 60 blocos morava.
		double telaW = 640, telaH = 330, esc = 1.0 / 12000;
		double mw = telaW / esc, mh = telaH / esc;
		double celulas = Math.Ceiling(mw / Sistemas.CelulaPx + 1) * Math.Ceiling(mh / Sistemas.CelulaPx + 1);
		Console.WriteLine($"  carta no zoom 1/12.000 ({mw / 1e6:F2}M x {mh / 1e6:F2}M px): {celulas:N0} celulas "
						  + $"= {celulas * nsCelula / 1e6:F2} ms");
		Afirmar(celulas * nsCelula / 1e6 < 33,
			"a galaxia visivel inteira, no zoom mais aberto, cabe num quadro");
	}

	// =====================================================================
	// 7) A GRADE
	// =====================================================================
	/// <summary>
	/// A RETICULA SUMIU? -- e a unica secao desta bancada que existe por causa de uma reclamacao.
	///
	/// ============================ O QUE O DONO VIU, E COMO ISSO VIRA NUMERO ============================
	/// *"senti q os sistemas estao mt certinhos, deveriam ter posicoes mais RANDOMICAS e ter ESPACOS
	/// VAZIOS entre eles"*. Um olho le "grade" por tres sinais, e cada um vira uma medida daqui:
	///
	///   1. AS COLUNAS -- as estrelas compartilham quase o mesmo x dentro da celula. Mede-se a FAIXA
	///      do offset dentro da celula.
	///   2. O ESPACAMENTO IGUAL -- toda estrela a mesma distancia da vizinha. Mede-se o CV (desvio
	///      sobre media) da distancia ao vizinho mais proximo.
	///   3. AS LINHAS -- o vizinho mais proximo esta sempre pra cima, pra baixo ou pro lado. Mede-se
	///      a fracao de vizinhos a menos de 15 graus de um eixo.
	///   4. A FORMA -- o histograma do espacamento e um pico unico com cauda zero dos dois lados.
	///
	/// E o VAZIO nao e nenhum dos quatro: e a distancia de um ponto QUALQUER ate a estrela mais
	/// proxima. Ela nao muda com jitter nenhum -- so a rejeicao a move, e e por isso que as duas
	/// correcoes precisaram existir juntas.
	/// ================================================================================================
	///
	/// ============================ NENHUM LIMIAR CRAVADO: TUDO E RAZAO ============================
	/// Esta secao ja teve `Afirmar(cv > 0.20)` e um vetor de literais com o histograma de ontem. Os
	/// dois envelhecem sozinhos: no dia em que a celula, o passo entre orbitas ou o numero de planetas
	/// mudarem, eles ficam verdes ou vermelhos por um motivo que nao tem nada a ver com a grade -- e
	/// ninguem vai saber dizer qual dos dois.
	///
	/// O "antes" passou a ser MEDIDO, pela MESMA `Sistemas.Do`, com os botoes da
	/// <see cref="RegraDaGrade"/> nas posicoes de ontem (e o padrao dos botoes neutros do
	/// `RoboDeForma`). Tres universos saem da mesma funcao e do mesmo sorteio de pontos:
	///
	///     hoje        margem = raio do proprio sistema, 96/256 celulas vazias
	///     antes       margem = RaioSistemaTeto pra todos, nenhuma celula vazia  (a reclamacao)
	///     esburacado  como hoje, mas 224/256 vazias                             (o excesso)
	///
	/// O que se afirma e a RAZAO entre hoje e ontem, e a autorizacao pra isso e a secao (1): os botoes
	/// velhos tem que reencontrar os quatro numeros que a Fase 0 publicou. Se nao reencontrarem, a
	/// razao esta dividindo por uma invencao e a secao inteira nao vale nada.
	///
	/// E COMO CADA FAMILIA REPROVA (a arvore esta estavel, entao o defeito e injetado):
	///   * a reticula        -> o campo de ONTEM passa pela mesma funcao de julgamento e cai nos 4 sinais;
	///   * o vazio           -> ontem cai por falta de deserto, o esburacado cai por deserto demais;
	///   * a busca 3x3       -> a mesma varredura com o raio de todo sistema inflado 4x acusa;
	///   * o botao neutro    -> a assinatura com os botoes velhos TEM que diferir da de hoje.
	/// ==========================================================================================
	///
	/// O QUE ELA NAO CONSERTA, e esta escrito aqui pra nao virar surpresa: o sinal 3 fica em ~60% e
	/// nao em 33%. Isso e a estrutura de UMA estrela por celula, e nao a margem -- o vizinho mais
	/// proximo mora quase sempre numa das quatro celulas de aresta, que estao nos eixos. Derrubar
	/// esse numero exigiria varios candidatos por celula com rejeicao por distancia, e com isso o
	/// endereco `SistemaId` deixaria de identificar um sistema -- e ele identifica em `MapaEstelar`,
	/// `TelaDoSistema`, `Berco` e na `Assinatura`.
	/// </summary>
	private static void AGrade(ulong seed)
	{
		Console.WriteLine();
		Console.WriteLine("=== 7) A GRADE: ela sumiu? (a secao que responde a reclamacao do dono) ===");

		const int Lado = 100;         // 10.000 celulas = 6,55M x 6,55M px
		const int Pontos = 120_000;   // os MESMOS pontos, na mesma ordem, nos tres universos

		// ============================ OS TRES UNIVERSOS SAO A MESMA FUNCAO ============================
		// Nenhum deles e reimplementado aqui: os tres saem de `Sistemas.Do` com os botoes da
		// <see cref="RegraDaGrade"/> em posicoes diferentes. E o que permite afirmar RAZAO em vez de
		// limiar, e o que faz o "antes" envelhecer junto com o "hoje" quando alguem mexer no gerador.
		// ==========================================================================================
		Campo hoje = Medir(seed, RegraDaGrade.Hoje, "hoje", Lado, Pontos);
		Campo antes = Medir(seed, RegraDaGrade.AntesDaReclamacao, "antes", Lado, Pontos);
		Campo furado = Medir(seed, RegraDaGrade.Hoje.ComVazios(VaziosDoDefeito), "esburacado", Lado, Pontos);

		// =============================================================
		// (0) O BOTAO NEUTRO E NEUTRO
		// =============================================================
		// Sem esta checagem, todo o resto da secao poderia estar comparando duas invencoes. Ela exige
		// que a sobrecarga com `RegraDaGrade.Hoje` devolva BIT A BIT o que o jogo recebe da sobrecarga
		// curta -- ou seja, que o denominador e o numerador da razao saiam do mesmo caminho de producao.
		int divergiram = 0;
		for (int sy = -8; sy < 8; sy++)
			for (int sx = -8; sx < 8; sx++)
			{
				SistemaSolar? a = Sistemas.Do(seed, sx, sy);
				SistemaSolar? b = Sistemas.Do(seed, sx, sy, out _, RegraDaGrade.Hoje);
				if ((a is null) != (b is null)) { divergiram++; continue; }
				if (a is { } x && b is { } y
					&& (Bits(x.Estrela.Pos.X) != Bits(y.Estrela.Pos.X) || Bits(x.Estrela.Pos.Y) != Bits(y.Estrela.Pos.Y)
						|| x.Orbitas != y.Orbitas || x.RaioDoSistema != y.RaioDoSistema)) divergiram++;
			}
		Afirmar(divergiram == 0,
			"o botao `Hoje` e NEUTRO: 256 celulas saem bit a bit iguais pela sobrecarga que o JOGO chama");
		Afirmar(Sistemas.Assinatura(seed, -16, -16, 32) == Sistemas.Assinatura(seed, -16, -16, 32, RegraDaGrade.Hoje),
			"...e a assinatura tambem -- o 'antes' e medido pela funcao de producao, nao por uma copia dela");
		Contraprova(Sistemas.Assinatura(seed, -16, -16, 32) != Sistemas.Assinatura(seed, -16, -16, 32, RegraDaGrade.AntesDaReclamacao),
			"e com os botoes VELHOS a mesma funcao assina outro universo -- os botoes nao sao decorativos");

		// =============================================================
		// (1) OS BOTOES VELHOS REPRODUZEM O ONTEM
		// =============================================================
		// A Fase 0 mediu o codigo que estava no ar, na MESMA seed e na MESMA regiao, e publicou quatro
		// numeros. Se a `RegraDaGrade.AntesDaReclamacao` nao os reencontrar, ela nao e o universo de
		// ontem -- e ai toda razao calculada abaixo estaria dividindo por uma invencao. Esta e a linha
		// que autoriza o resto da secao.
		Console.WriteLine($"  os botoes VELHOS reproduzem o ontem? (Fase 0, mesma seed, mesma regiao)");
		Afirmar(Math.Abs(antes.CvNn - 0.083) <= 0.012, $"    CV do espacamento {antes.CvNn:F3} (publicado 0,083)");
		Afirmar(Math.Abs(antes.ViesEixo - 96.6) <= 4.0, $"    vies de eixo {antes.ViesEixo:F1}% (publicado 96,6%)");
		Afirmar(Math.Abs(antes.FaixaOffsetPct - 29.8) <= 2.0, $"    faixa do offset {antes.FaixaOffsetPct:F1}% (publicado 29,8%)");
		Afirmar(Math.Abs(antes.AteMax - 54_713) / 54_713.0 <= 0.15,
			$"    o ponto mais vazio do universo a {antes.AteMax:N0} px (publicado 54.713)");
		Afirmar(antes.Cheias == antes.Total && antes.Sorteadas == 0,
			$"    e TODA celula tinha sistema ({antes.Cheias:N0} de {antes.Total:N0}) -- nao havia vazio nenhum");

		// =============================================================
		// (2) A TABELA -- hoje contra ontem, e a RAZAO entre os dois
		// =============================================================
		Console.WriteLine();
		Console.WriteLine($"  {"medida",-46} {"hoje",10} {"antes",10} {"razao",9}");
		void Linha(string o, double h, double a, string f = "N0")
			=> Console.WriteLine($"  {o,-46} {h.ToString(f),10} {a.ToString(f),10} {(a == 0 ? "--" : (h / a).ToString("F2") + "x"),9}");

		Linha("celulas com sistema (%)", hoje.Cheias * 100.0 / hoje.Total, antes.Cheias * 100.0 / antes.Total, "F1");
		Linha("faixa do offset dentro da celula (% do lado)", hoje.FaixaOffsetPct, antes.FaixaOffsetPct, "F1");
		Linha("caixa livre media (% do lado)", hoje.MediaLivrePct, antes.MediaLivrePct, "F1");
		Linha("CV do vizinho mais proximo", hoje.CvNn, antes.CvNn, "F3");
		Linha("vies de eixo (%; isotropico daria 33,3)", hoje.ViesEixo, antes.ViesEixo, "F1");
		Linha("caixas de 5.000 px com >= 1% da massa", hoje.CaixasVivas, antes.CaixasVivas, "F0");
		Linha("maior caixa do histograma (%)", hoje.PicoHist, antes.PicoHist, "F1");
		Linha("vizinho mais proximo: media (px)", hoje.MediaNn, antes.MediaNn);
		Linha("vizinho mais proximo: max / media", hoje.MaxNn / hoje.MediaNn, antes.MaxNn / antes.MediaNn, "F2");
		Linha("ponto qualquer -> estrela: media (px)", hoje.AteMedia, antes.AteMedia);
		Linha("ponto qualquer -> estrela: max (px)", hoje.AteMax, antes.AteMax);
		Linha("ponto qualquer -> estrela: max / media", hoje.AteMax / hoje.AteMedia, antes.AteMax / antes.AteMedia, "F2");
		Console.WriteLine($"  (o esburacado de controle, {VaziosDoDefeito}/256 vazias: {furado.Cheias * 100.0 / furado.Total:F1}% "
						  + $"de celulas cheias, ponto->estrela max {furado.AteMax:N0} px, "
						  + $"{furado.SemNo3x3Pct:F1}% dos pontos sem NENHUMA estrela no 3x3)");

		// =============================================================
		// (3) A RETICULA SUMIU -- o julgamento, e o mesmo julgamento aplicado a ONTEM
		// =============================================================
		Console.WriteLine();
		(bool semGrade, string laudo) = SemReticula(hoje, antes);
		Afirmar(semGrade, "A RETICULA SUMIU -- " + laudo);

		// O DEFEITO INJETADO E O PROPRIO UNIVERSO DE ONTEM: a mesma funcao de julgamento, com o campo
		// que o dono reclamou na entrada. Ela tem que reprovar nos QUATRO criterios de uma vez -- se
		// reprovasse em menos, algum dos quatro estaria medindo outra coisa.
		(bool aindaGrade, string laudoOntem) = SemReticula(antes, antes);
		Contraprova(!aindaGrade, "o universo de ONTEM, julgado pela MESMA conta, reprova -- " + laudoOntem);

		// =============================================================
		// (4) O HISTOGRAMA -- as DUAS colunas medidas, nenhuma escrita a mao
		// =============================================================
		// Media e CV podem concordar com formas muito diferentes, e e no histograma que a reticula
		// aparecia a olho nu: um pico unico com CAUDA ZERO dos dois lados. A coluna "antes" ja foi um
		// vetor de literais neste arquivo -- ela agora e MEDIDA na mesma rodada, pela mesma funcao, e
		// por isso acompanha sozinha qualquer mudanca no gerador.
		Console.WriteLine();
		Console.WriteLine($"  vizinho mais proximo: hoje n={hoje.NnContados:N0} media {hoje.MediaNn:N0} px "
						  + $"[{hoje.MinNn:N0} .. {hoje.MaxNn:N0}] | antes n={antes.NnContados:N0} media {antes.MediaNn:N0} px "
						  + $"[{antes.MinNn:N0} .. {antes.MaxNn:N0}]");
		Console.WriteLine($"    {"caixa (px)",-20} {"hoje",22} {"antes",8}");
		for (int b = 0; b < hoje.Hist.Length; b++)
		{
			if (hoje.Hist[b] < 0.05 && antes.Hist[b] < 0.05) continue;
			string faixa = b == hoje.Hist.Length - 1
				? $"{(b * PassoDaCaixa):N0}+"
				: $"{b * PassoDaCaixa,7:N0}..{(b + 1) * PassoDaCaixa:N0}";
			Console.WriteLine($"    {faixa,-20} {hoje.Hist[b],5:F1}% {new string('#', (int)Math.Round(hoje.Hist[b] / 1.5)),-15} {antes.Hist[b],6:F1}%");
		}

		// =============================================================
		// (5) EXISTEM VAZIOS -- E ELES TEM DOIS JEITOS DE ESTAR ERRADOS
		// =============================================================
		// O vazio nao e nenhum dos sinais de grade: e a distancia de um ponto QUALQUER ate a estrela
		// mais proxima, e jitter nenhum a move -- so a rejeicao. Por isso as duas correcoes precisaram
		// existir juntas.
		//
		// E ele REPROVA DOS DOIS LADOS, o que e a parte que uma bancada de "tem que ter vazio" perderia:
		// um universo sem deserto (o de ontem) e um universo so de deserto reprovam pela MESMA funcao,
		// e os tres campos passam por ela abaixo.
		Console.WriteLine();
		Console.WriteLine($"  do ponto mais vazio: hoje {hoje.AteMax:N0} px "
						  + $"({hoje.AteMax / MoveRules.BaseSpeedPx / 60:F1} min base, {hoje.AteMax / (MoveRules.BaseSpeedPx * 11):F0} s no teto de 11x) "
						  + $"| antes {antes.AteMax:N0} px | esburacado {furado.AteMax:N0} px");
		Console.WriteLine($"  pontos sem NENHUMA estrela no 3x3 de celulas: hoje {hoje.SemNo3x3Pct:F2}%, "
						  + $"antes {antes.SemNo3x3Pct:F2}%, esburacado {furado.SemNo3x3Pct:F2}% "
						  + "(o `EstrelaPerto` devolve falso, e o `TickDoSol` nao faz nada -- e o esperado)");

		(bool vazioOk, string laudoVazio) = VazioSaudavel(hoje);
		Afirmar(vazioOk, "EXISTE DESERTO, E ELE NAO E DEMAIS -- " + laudoVazio);
		Afirmar(hoje.AteMax / antes.AteMax > 2.0,
			$"...e o pior lugar do universo ficou {hoje.AteMax / antes.AteMax:F2}x mais longe de uma estrela do que ontem "
			+ $"({hoje.AteMax:N0} contra {antes.AteMax:N0} px)");

		// OS DOIS DEFEITOS INJETADOS, um de cada lado da mesma funcao.
		(bool ontemOk, string laudoSemDeserto) = VazioSaudavel(antes);
		Contraprova(!ontemOk, "ONTEM reprova por falta de deserto -- " + laudoSemDeserto);
		(bool furadoOk, string laudoFurado) = VazioSaudavel(furado);
		Contraprova(!furadoOk, $"e o universo com {VaziosDoDefeito}/256 celulas vazias reprova por deserto DEMAIS -- " + laudoFurado);

		// =============================================================
		// (6) A DESIGUALDADE DA BUSCA 3x3 CONTINUA DE PE
		// =============================================================
		// ============================ PROVADA VARRENDO, E NAO AFIRMADA ============================
		// O argumento e simples e esta no `PorPerto`: um sistema a duas celulas tem a estrela a pelo
		// menos `CelulaPx + margem` do ponto consultado. Argumento nao e medida, e o modo de falha aqui
		// e calado -- o jogador voa por dentro de um sistema que o servidor nao ve, sem erro nenhum.
		//
		// Entao ela e varrida de dois jeitos, que erram de formas diferentes:
		//   * EXAUSTIVA e geometrica: pra cada uma das 10.000 celulas de cada universo, a distancia da
		//     estrela ate o ponto mais proximo cujo 3x3 NAO cobre a celula dela e um minimo fechado
		//     (`min(2C-ox, C+ox, 2C-oy, C+oy)`). Isso vale pra TODOS os pontos do plano de uma vez, e e
		//     por isso que ela vem primeiro -- amostra de pontos nunca prova ausencia;
		//   * POR PONTOS: 50.000 pontos sorteados contra o anel de 3 celulas em volta, que e a mesma
		//     pergunta feita do lado de quem consulta.
		// ======================================================================================
		Console.WriteLine();
		Console.WriteLine($"  busca 3x3, varredura EXAUSTIVA das {hoje.Total:N0} celulas de cada universo: "
						  + $"hoje {hoje.EscapamDo3x3} escapam, antes {antes.EscapamDo3x3}, esburacado {furado.EscapamDo3x3} "
						  + $"| menor folga medida {hoje.MenorFolgaDo3x3:N0} px contra o maior raio {hoje.MaiorRsys:N0} px");
		Afirmar(hoje.EscapamDo3x3 == 0 && antes.EscapamDo3x3 == 0 && furado.EscapamDo3x3 == 0,
			"nenhum disco de sistema alcanca um ponto de fora do 3x3, em NENHUM dos tres universos");
		Console.WriteLine($"  e por pontos: 50.000 pontos x anel de 3 celulas -> hoje {hoje.EscapamAnel}, "
						  + $"antes {antes.EscapamAnel}, esburacado {furado.EscapamAnel} sistema(s) alcancando de fora do 3x3");
		Afirmar(hoje.EscapamAnel == 0 && antes.EscapamAnel == 0 && furado.EscapamAnel == 0,
			"...e a varredura por PONTOS concorda com a geometrica -- a busca nao precisou ser alargada");

		// A MARGEM MAXIMA TEM QUE CABER EM MEIA CELULA, senao a caixa livre vira negativa e a estrela
		// nasce FORA da propria celula -- e ai o endereco `SistemaId` deixa de dizer onde o sistema
		// esta, o que quebraria a busca inteira antes mesmo do 3x3.
		Afirmar(Sistemas.RaioSistemaTeto + Sistemas.FolgaEntreSistemas < Sistemas.CelulaPx / 2,
			$"a margem maxima ({Sistemas.RaioSistemaTeto + Sistemas.FolgaEntreSistemas:N0} px) cabe em meia celula "
			+ $"({Sistemas.CelulaPx / 2:N0} px)");

		// O DEFEITO INJETADO: a MESMA varredura com o raio dos sistemas inflado. Sem ela, "0 escapam"
		// seria indistinguivel de uma varredura que nao olha nada -- e foi assim que a conta ficou
		// verde por tres rodadas antes de alguem perguntar quantos sistemas ela chegou a examinar.
		Console.WriteLine($"  com o raio de TODO sistema inflado {FatorDaInjecao}x (a mesma varredura): "
						  + $"hoje {hoje.EscapamInflado:N0} sistemas passam a escapar do 3x3");
		Contraprova(hoje.EscapamInflado > 0 && antes.EscapamInflado > 0,
			$"a varredura ACUSA quando o disco cresce: {hoje.EscapamInflado:N0} de {hoje.Cheias:N0} sistemas "
			+ $"({hoje.EscapamInflado * 100.0 / hoje.Cheias:F1}%) alcancariam fora do 3x3");
	}

	// =====================================================================
	// AS PECAS DA SECAO 7
	// =====================================================================
	/// <summary>Largura da caixa do histograma de vizinho mais proximo.</summary>
	private const double PassoDaCaixa = 5_000;

	/// <summary>Quantas caixas o histograma tem. A ultima e "e acima".</summary>
	private const int CaixasDoHistograma = 28;      // 0 .. 135.000+ px

	/// <summary>
	/// A TAXA DE VAZIO DO UNIVERSO DE CONTROLE -- 87,5% das celulas sem nada.
	///
	/// Ele nao e uma proposta: e o defeito do outro lado. A varredura da rodada anterior mostrou que a
	/// partir de 128/256 uma em cada setenta viagens ja comeca sem estrela nenhuma a vista, e 224 poe
	/// isso muito alem do que o dono pediu. Ele existe pra fazer a funcao de julgamento do vazio
	/// reprovar POR EXCESSO -- sem ele, "existe deserto" seria satisfeito por um universo vazio.
	/// </summary>
	private const int VaziosDoDefeito = 224;

	/// <summary>Por quanto o raio de todo sistema e multiplicado no defeito injetado da busca 3x3.</summary>
	private const double FatorDaInjecao = 4;

	/// <summary>
	/// O RESULTADO DE MEDIR UM UNIVERSO INTEIRO. Um objeto por rodada de <see cref="Medir"/>, e a
	/// secao 7 compara tres deles -- hoje, ontem e o esburacado de controle.
	/// </summary>
	private sealed class Campo
	{
		public string Nome = "";
		public int Total, Cheias, Sorteadas, Anuladas, Gerados;

		/// <summary>Faixa do offset da estrela dentro da celula, em % do lado. E o sinal "COLUNAS".</summary>
		public double FaixaOffsetPct;
		public double MinLivrePct, MediaLivrePct, MaiorRsys;

		/// <summary>Distancia ao vizinho mais proximo. CV e o sinal "ESPACAMENTO IGUAL".</summary>
		public double MediaNn, CvNn, MinNn, MaxNn;
		public int NnContados;

		/// <summary>% de vizinhos a menos de 15 graus de um eixo. E o sinal "LINHAS".</summary>
		public double ViesEixo;

		/// <summary>Fracao da massa por caixa de <see cref="PassoDaCaixa"/> px.</summary>
		public double[] Hist = new double[CaixasDoHistograma];
		public double PicoHist;
		public int CaixasVivas;

		/// <summary>De um ponto QUALQUER ate a estrela mais proxima. E o VAZIO.</summary>
		public double AteMedia, AteP99, AteMax;
		public int PontosSemEstrela;
		public double SemNo3x3Pct;

		/// <summary>Quantos sistemas alcancam um ponto de fora do 3x3 -- tem que ser zero.</summary>
		public int EscapamDo3x3, EscapamAnel, EscapamInflado;
		public double MenorFolgaDo3x3 = double.MaxValue;
	}

	/// <summary>
	/// MEDE UM UNIVERSO INTEIRO PELO CAMINHO DE PRODUCAO.
	///
	/// A unica coisa que muda entre as tres chamadas e a <see cref="RegraDaGrade"/>: a enumeracao, o
	/// vizinho mais proximo, o vazio e a varredura do 3x3 sao o MESMO codigo nos tres. E isso que faz
	/// a razao entre dois campos significar alguma coisa.
	///
	/// O mapa e um VETOR e nao um dicionario: a medida do vazio faz 13x13 consultas por ponto vezes
	/// 120 mil pontos vezes tres universos -- 60 milhoes de consultas. Com dicionario a secao levava
	/// segundos e ninguem a rodaria.
	///
	/// A BORDA DA REGIAO FICA DE FORA de toda medida de distancia (7 celulas): la o vizinho verdadeiro
	/// pode estar fora do que foi enumerado, e o numero sairia inflado justamente onde ninguem olharia.
	/// </summary>
	private static Campo Medir(ulong seed, RegraDaGrade regra, string nome, int lado, int pontos)
	{
		int half = lado / 2;
		const int Borda = 7, Busca = 6;
		double C = Sistemas.CelulaPx;
		var c = new Campo { Nome = nome, Total = lado * lado };

		var tem = new bool[lado * lado];
		var ex_ = new double[lado * lado];
		var ey_ = new double[lado * lado];
		var er = new double[lado * lado];

		double minOff = double.MaxValue, maxOff = double.MinValue, somaLivre = 0;
		c.MinLivrePct = double.MaxValue;

		for (int sy = -half; sy < half; sy++)
			for (int sx = -half; sx < half; sx++)
			{
				int i = (sy + half) * lado + (sx + half);
				if (Sistemas.Do(seed, sx, sy, out CelulaVazia porque, regra) is not { } s)
				{
					if (porque == CelulaVazia.Sorteada) c.Sorteadas++; else c.Anuladas++;
					continue;
				}
				c.Cheias++;
				tem[i] = true;
				ex_[i] = s.Estrela.Pos.X; ey_[i] = s.Estrela.Pos.Y; er[i] = s.RaioDoSistema;

				// A VARREDURA EXAUSTIVA DO 3x3, e ela inclui o ANCORADO. O ponto mais proximo cujo 3x3
				// nao cobre esta celula esta a `min(2C-ox, C+ox, 2C-oy, C+oy)` da estrela -- fechado,
				// valendo pra todos os pontos do plano de uma vez.
				double ox = s.Estrela.Pos.X - sx * C, oy = s.Estrela.Pos.Y - sy * C;
				double maisPerto = Math.Min(Math.Min(2 * C - ox, C + ox), Math.Min(2 * C - oy, C + oy));
				c.MenorFolgaDo3x3 = Math.Min(c.MenorFolgaDo3x3, maisPerto);
				if (s.RaioDoSistema >= maisPerto) c.EscapamDo3x3++;
				if (s.RaioDoSistema * FatorDaInjecao >= maisPerto) c.EscapamInflado++;

				if (s.Ancorado) continue;

				c.Gerados++;
				c.MaiorRsys = Math.Max(c.MaiorRsys, s.RaioDoSistema);
				minOff = Math.Min(minOff, ox); maxOff = Math.Max(maxOff, ox);
				double margem = (regra.MargemFixa > 0 ? regra.MargemFixa : s.RaioDoSistema) + regra.Folga;
				double livre = (C - 2 * margem) / C * 100;
				c.MinLivrePct = Math.Min(c.MinLivrePct, livre); somaLivre += livre;
			}

		c.FaixaOffsetPct = c.Gerados > 0 ? (maxOff - minOff) / C * 100 : 0;
		c.MediaLivrePct = somaLivre / Math.Max(1, c.Gerados);

		// O VIZINHO MAIS PROXIMO, e a direcao dele.
		var nn = new List<double>(c.Cheias);
		int noEixo = 0;
		for (int sy = -half + Borda; sy < half - Borda; sy++)
			for (int sx = -half + Borda; sx < half - Borda; sx++)
			{
				int i = (sy + half) * lado + (sx + half);
				if (!tem[i]) continue;
				double melhor = double.MaxValue, bx = 0, by = 0;
				for (int dy = -Busca; dy <= Busca; dy++)
					for (int dx = -Busca; dx <= Busca; dx++)
					{
						if ((dx | dy) == 0) continue;
						int j = (sy + dy + half) * lado + (sx + dx + half);
						if (!tem[j]) continue;
						double vx = ex_[j] - ex_[i], vy = ey_[j] - ey_[i], d2 = vx * vx + vy * vy;
						if (d2 < melhor) { melhor = d2; bx = vx; by = vy; }
					}
				if (melhor == double.MaxValue) continue;
				nn.Add(Math.Sqrt(melhor));
				double ang = Math.Atan2(Math.Abs(by), Math.Abs(bx)) * 180 / Math.PI;
				if (Math.Min(ang, 90 - ang) <= 15) noEixo++;
			}

		if (nn.Count > 0)
		{
			nn.Sort();
			c.NnContados = nn.Count;
			c.MediaNn = nn.Average();
			c.MinNn = nn[0]; c.MaxNn = nn[^1];
			c.CvNn = Math.Sqrt(nn.Sum(v => (v - c.MediaNn) * (v - c.MediaNn)) / nn.Count) / c.MediaNn;
			c.ViesEixo = noEixo * 100.0 / nn.Count;
			foreach (double v in nn) c.Hist[Math.Min(CaixasDoHistograma - 1, (int)(v / PassoDaCaixa))] += 100.0 / nn.Count;
			c.PicoHist = c.Hist.Max();
			c.CaixasVivas = c.Hist.Count(h => h >= 1.0);
		}

		// O VAZIO: de um ponto qualquer ate a estrela mais proxima. A MESMA semente nos tres universos,
		// pra que a comparacao seja dos campos e nao dos sorteios.
		var rng = new Random(12345);
		var ate = new List<double>(pontos);
		int semNo3x3 = 0;
		for (int k = 0; k < pontos; k++)
		{
			int sx = rng.Next(-half + Borda, half - Borda), sy = rng.Next(-half + Borda, half - Borda);
			double x = sx * C + rng.NextDouble() * C, y = sy * C + rng.NextDouble() * C;
			double melhor = double.MaxValue; bool no3x3 = false;
			for (int dy = -Busca; dy <= Busca; dy++)
				for (int dx = -Busca; dx <= Busca; dx++)
				{
					int j = (sy + dy + half) * lado + (sx + dx + half);
					if (!tem[j]) continue;
					if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) no3x3 = true;
					double vx = ex_[j] - x, vy = ey_[j] - y, d2 = vx * vx + vy * vy;
					if (d2 < melhor) melhor = d2;
				}
			if (!no3x3) semNo3x3++;
			// SEM ESTRELA NENHUMA EM 13x13 CELULAS (851 mil px) e um caso a mais, e nao um ponto a
			// menos: descarta-lo baixaria o maximo do universo esburacado justamente onde ele e pior.
			if (melhor == double.MaxValue) c.PontosSemEstrela++;
			else ate.Add(Math.Sqrt(melhor));
		}
		ate.Sort();
		c.AteMedia = ate.Count > 0 ? ate.Average() : 0;
		c.AteP99 = ate.Count > 0 ? ate[ate.Count * 99 / 100] : 0;
		c.AteMax = ate.Count > 0 ? ate[^1] : 0;
		c.SemNo3x3Pct = semNo3x3 * 100.0 / pontos;

		// E A VARREDURA POR PONTOS DO 3x3, que e a mesma pergunta feita do lado de quem consulta.
		for (int k = 0; k < 50_000; k++)
		{
			int sx = rng.Next(-half + Borda, half - Borda), sy = rng.Next(-half + Borda, half - Borda);
			double x = sx * C + rng.NextDouble() * C, y = sy * C + rng.NextDouble() * C;
			for (int dy = -3; dy <= 3; dy++)
				for (int dx = -3; dx <= 3; dx++)
				{
					if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) continue;   // o 3x3 ja ve
					int j = (sy + dy + half) * lado + (sx + dx + half);
					if (!tem[j]) continue;
					double vx = ex_[j] - x, vy = ey_[j] - y;
					if (vx * vx + vy * vy < er[j] * er[j]) c.EscapamAnel++;
				}
		}

		return c;
	}

	/// <summary>
	/// O JULGAMENTO DA RETICULA -- os quatro sinais de uma vez, cada um como RAZAO contra o campo de
	/// ontem, e o laudo dizendo qual passou e qual nao.
	///
	/// Os limiares sao de RAZAO e nao de valor: "o CV mais que dobrou" continua significando a mesma
	/// coisa no dia em que a celula ou o passo entre orbitas mudarem, e "o CV passa de 0,20" nao.
	/// Aplicar esta funcao ao proprio campo de ontem devolve razao 1,00 em tudo -- e e assim que ela
	/// reprova.
	/// </summary>
	private static (bool ok, string laudo) SemReticula(Campo alvo, Campo referencia)
	{
		double rColunas = alvo.FaixaOffsetPct / referencia.FaixaOffsetPct;
		double rCv = alvo.CvNn / referencia.CvNn;
		double rVies = alvo.ViesEixo / referencia.ViesEixo;
		double rCaixas = (double)alvo.CaixasVivas / Math.Max(1, referencia.CaixasVivas);
		double rPico = alvo.PicoHist / referencia.PicoHist;

		bool colunas = rColunas > 2.0;
		bool espacamento = rCv > 2.0;
		bool linhas = rVies < 0.80;
		bool forma = rCaixas > 2.0 && rPico < 0.60;

		string laudo =
			$"COLUNAS {(colunas ? "sim" : "NAO")} ({rColunas:F2}x a faixa do offset: {alvo.FaixaOffsetPct:F1}% contra {referencia.FaixaOffsetPct:F1}%) | "
			+ $"ESPACAMENTO {(espacamento ? "sim" : "NAO")} ({rCv:F2}x o CV: {alvo.CvNn:F3} contra {referencia.CvNn:F3}) | "
			+ $"LINHAS {(linhas ? "sim" : "NAO")} ({rVies:F2}x o vies: {alvo.ViesEixo:F1}% contra {referencia.ViesEixo:F1}%) | "
			+ $"FORMA {(forma ? "sim" : "NAO")} ({rCaixas:F2}x as caixas ocupadas e {rPico:F2}x o pico)";

		return (colunas && espacamento && linhas && forma, laudo);
	}

	/// <summary>
	/// O JULGAMENTO DO VAZIO, e ele reprova DOS DOIS LADOS.
	///
	/// "Tem deserto" sozinho e satisfeito por um universo vazio, e "da pra atravessar" sozinho e
	/// satisfeito pela reticula de ontem. As duas metades tem que valer juntas, e e por isso que a
	/// funcao e uma so e os tres campos passam por ela.
	///
	/// O teto da travessia e o unico numero absoluto desta secao, e ele e absoluto de proposito: 300 s
	/// no teto de velocidade e o que o jogador aguenta olhando pro nada, e isso nao muda quando o
	/// gerador mudar. Ele sai da mesma varredura de taxas que escolheu o <see cref="Sistemas.VaziosPor256"/>.
	/// </summary>
	private static (bool ok, string laudo) VazioSaudavel(Campo c)
	{
		double s11 = c.AteMax / (MoveRules.BaseSpeedPx * 11);
		double razao = c.AteMax / Math.Max(1, c.AteMedia);

		bool temDeserto = razao > 2.5;
		bool atravessavel = s11 < 300 && c.PontosSemEstrela == 0;
		bool habitado = c.Cheias * 2 > c.Total && c.SemNo3x3Pct < 1.0;

		string laudo =
			$"DESERTO {(temDeserto ? "sim" : "NAO")} (o pior lugar a {razao:F2}x a media: {c.AteMax:N0} contra {c.AteMedia:N0} px) | "
			+ $"ATRAVESSAVEL {(atravessavel ? "sim" : "NAO")} ({s11:F0} s no teto de 11x, {c.PontosSemEstrela} pontos sem estrela em 13x13 celulas) | "
			+ $"HABITADO {(habitado ? "sim" : "NAO")} ({c.Cheias * 100.0 / c.Total:F1}% das celulas, {c.SemNo3x3Pct:F2}% dos pontos com o 3x3 vazio)";

		return (temDeserto && atravessavel && habitado, laudo);
	}

	/// <summary>
	/// UM CHUNK NO MEIO DO NADA -- o 3x3 de celulas em volta dele nao tem sistema nenhum. Deterministico
	/// (varre em ordem fixa), e e ele que mede o preco do `PorPerto` quando o prefiltro corta tudo.
	/// </summary>
	private static ChunkId ChunkVazio(ulong seed)
	{
		for (int sy = 0; sy < 400; sy++)
			for (int sx = 0; sx < 400; sx++)
			{
				bool limpo = true;
				for (int dy = -1; dy <= 1 && limpo; dy++)
					for (int dx = -1; dx <= 1 && limpo; dx++)
						if (Sistemas.Do(seed, sx + dx, sy + dy) is not null) limpo = false;
				if (!limpo) continue;

				// o meio da celula, em chunks
				double px = (sx + 0.5) * Sistemas.CelulaPx, py = (sy + 0.5) * Sistemas.CelulaPx;
				return new ChunkId((int)Math.Floor(px / Espaco.ChunkPx), (int)Math.Floor(py / Espaco.ChunkPx));
			}
		return new ChunkId(700, -300);
	}

	/// <summary>Uma amostra quadrada de sistemas gerados, pra distribuicao. Pula os anulados.</summary>
	private static IEnumerable<SistemaSolar> Amostra(ulong seed, int lado)
	{
		for (int sy = -lado; sy < lado; sy++)
			for (int sx = -lado; sx < lado; sx++)
				if (Sistemas.Do(seed, sx, sy) is { } s) yield return s;
	}
}
