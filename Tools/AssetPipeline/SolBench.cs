using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DAS CONTAS DO SOL (`sol`).
///
/// ============================ O QUE ELA PROVA, E O QUE ELA NAO PODE PROVAR ============================
/// Ela prova os NUMEROS: a curva de poder das 8 classes, o fator de dano, o piso, o teto suave, e a
/// desigualdade que segura tudo de pe -- **o dano minimo tem que ser maior que a cura passiva**.
///
/// Ela nao prova a CORRENTE (que o `TickDoEspaco` pergunta pela estrela, que o corpo e ferido pelo
/// funil de verdade, que o `Morrer()` e chamado e que o retorno e olhado). Aquilo e a bancada AO VIVO
/// `--solteste`, e as duas existem porque ja aconteceu neste projeto de as regras estarem certas e
/// nao haver chamador nenhum.
/// ==================================================================================================
///
/// E ELA IMPRIME A TABELA. O pedido do dono foi explicito: *"diga o NUMERO (dano por segundo, e
/// contra o que ele escala) e quanto tempo um corpo tipico aguenta"*. A matriz de 8 classes por 6
/// niveis de poder e a resposta, e ela e MEDIDA e nao estimada -- cada celula e uma simulacao de
/// tique com a cura ligada.
///
///     dotnet run --project Tools/AssetPipeline -- sol
/// </summary>
public static class SolBench
{
	private static int _falhas;

	private static void Afirmar(bool ok, string oque)
	{
		if (!ok) _falhas++;
		Console.WriteLine($"  [{(ok ? "OK  " : "FALHA")}] {oque}");
	}

	public static void Run()
	{
		_falhas = 0;

		OPoderDasEstrelas();
		OFatorDeDano();
		ADesigualdadeQueSeguraTudo();
		AGeometriaDaCoroa();
		ATabela();

		Console.WriteLine();
		Console.WriteLine(_falhas == 0
			? "=== TUDO VERDE ==="
			: $"=== {_falhas} FALHA(S) -- nao siga em frente ===");
	}

	private static readonly ClasseDeEstrela[] Classes = Enum.GetValues<ClasseDeEstrela>();

	// =====================================================================
	// 1) O PODER DAS ESTRELAS
	// =====================================================================
	private static void OPoderDasEstrelas()
	{
		Console.WriteLine("=== 1) O PODER DE CADA CLASSE (derivado do RAIO, nao de uma segunda tabela) ===");
		Console.WriteLine($"  {"classe",-18} {"raio",7} {"poder",16}");
		foreach (ClasseDeEstrela c in Classes)
			Console.WriteLine($"  {c,-18} {Sistemas.RaioDaClasse(c),7:N0} {CalorDaEstrela.PoderDe(c),16:N0}");
		Console.WriteLine();

		// AS DUAS PONTAS SAO ANCORADAS EM NUMEROS QUE O JOGO JA TINHA. Se um dia alguem mexer no raio
		// de uma classe sem olhar aqui, e esta afirmacao que vai reclamar.
		Afirmar(Math.Abs(CalorDaEstrela.PoderDe(ClasseDeEstrela.AnaVermelha)
						 - Jandirus.Core.Forms.LimiaresPessoais.RestSsjatInicial) < 1,
			"a menor estrela vale o limiar de Super Saiyajin (1e6 de BP)");

		double maior = CalorDaEstrela.PoderDe(ClasseDeEstrela.GiganteAzul);
		Afirmar(Math.Abs(maior - Jandirus.Core.Stats.Fighter.BpGainSoftcap) < maior * 0.10,
			$"a maior vale o teto de treino do jogo (1e9): {maior:N0}");

		// MONOTONICA POR RAIO -- nao por indice: duas classes tem o mesmo raio (as duas gigantes no
		// teto de 2.048), e afirmar "cresce estritamente" reprovaria a tabela certa.
		bool sobe = true;
		for (int i = 1; i < Classes.Length; i++)
			if (Sistemas.RaioDaClasse(Classes[i]) > Sistemas.RaioDaClasse(Classes[i - 1])
				&& CalorDaEstrela.PoderDe(Classes[i]) <= CalorDaEstrela.PoderDe(Classes[i - 1]))
				sobe = false;
		Afirmar(sobe, "estrela maior tem poder maior, sem excecao");

		// E NAO HA `Math.Pow` NO CAMINHO -- a quarta potencia sao tres multiplicacoes. O jeito de
		// afirmar isso sem ler o fonte e conferir a identidade exata contra a mesma conta a mao.
		double t = 700.0 / CalorDaEstrela.RaioDaMenorEstrela;
		Afirmar(CalorDaEstrela.PoderDoRaio(700) == CalorDaEstrela.PoderDaMenorEstrela * t * t * t * t,
			"a curva e `*` puro (bit a bit igual a conta a mao) -- sem `Math.Pow`");
	}

	// =====================================================================
	// 2) O FATOR DE DANO
	// =====================================================================
	private static void OFatorDeDano()
	{
		Console.WriteLine("=== 2) O FATOR: como o dano escala contra o BP ===");

		float raio = Sistemas.RaioDaClasse(ClasseDeEstrela.Amarela);
		double poder = CalorDaEstrela.PoderDoRaio(raio);
		Console.WriteLine($"  estrela amarela: raio {raio:N0} px, poder {poder:N0}, piso {CalorDaEstrela.PisoDe(raio):0.##}");
		Console.WriteLine($"  {"BP do corpo",16} {"razao",8} {"fator",8} {"dano/s",8}");
		foreach (double bp in new[] { 1e2, 1e5, 1e6, 1e7, 1e8, 1e9, 1e13 })
			Console.WriteLine($"  {bp,16:0.0e+0} {poder / bp,8:0.###} {CalorDaEstrela.Fator(raio, bp),8:0.###}"
							  + $" {CalorDaEstrela.DanoDeParidade * CalorDaEstrela.Fator(raio, bp),8:0.#}");
		Console.WriteLine();

		// MONOTONICO: mais poder, menos dano. Sem excecao, em quatorze ordens de grandeza -- e em
		// TODAS as classes, porque o piso agora depende do raio e cada classe tem a sua curva.
		bool cai = true;
		foreach (ClasseDeEstrela c in Classes)
		{
			double antes = double.MaxValue;
			for (double bp = 1; bp <= 1e14; bp *= 1.5)
			{
				double f = CalorDaEstrela.Fator(Sistemas.RaioDaClasse(c), bp);
				if (f > antes + 1e-12) cai = false;
				antes = f;
			}
		}
		Afirmar(cai, "ficar mais forte nunca aumenta o dano, em nenhuma classe (14 ordens de grandeza)");

		// O TETO SUAVE SATURA E NAO CORTA. Um `Math.Min` faria todo mundo abaixo de 1/8 do poder da
		// estrela morrer no MESMO instante -- o patamar e exatamente a leitura de "morte instantanea".
		double f1 = CalorDaEstrela.Fator(raio, poder / 100);
		double f2 = CalorDaEstrela.Fator(raio, poder / 10_000);
		Afirmar(f1 < CalorDaEstrela.TetoSuaveDoFator && f2 < CalorDaEstrela.TetoSuaveDoFator,
			$"o teto suave nunca e alcancado ({f2:0.###} < {CalorDaEstrela.TetoSuaveDoFator})");
		Afirmar(f2 > f1,
			$"...e o mais fraco ainda queima mais rapido que o fraco ({f2:0.###} contra {f1:0.###})");

		// ============================ O PISO DEPENDE DO RAIO -- E ISSO E O ACHADO DA BANCADA ============================
		// Com um piso constante esta afirmacao seria "0,30 == 0,30" nas oito classes, e a coluna do
		// corpo de 1e13 dava 75 s da ana vermelha a gigante azul: a classe da estrela deixava de
		// significar qualquer coisa pra quem esta no fim do jogo. Ver `CalorDaEstrela.PisoDe`.
		// ==========================================================================================================
		Console.WriteLine($"  {"classe",-18} {"piso",8} {"dano/s no piso",16}");
		foreach (ClasseDeEstrela c in Classes)
		{
			double p = CalorDaEstrela.PisoDe(Sistemas.RaioDaClasse(c));
			Console.WriteLine($"  {c,-18} {p,8:0.###} {CalorDaEstrela.DanoDeParidade * p,16:0.#}");
			Afirmar(Math.Abs(CalorDaEstrela.Fator(Sistemas.RaioDaClasse(c), 1e13) - p) < 1e-12,
				$"{c}: o corpo de 1e13 cai no piso desta classe ({p:0.###})");
		}
		Afirmar(CalorDaEstrela.PisoDe(Sistemas.RaioDaClasse(ClasseDeEstrela.GiganteAzul))
				> CalorDaEstrela.PisoDe(Sistemas.RaioDaClasse(ClasseDeEstrela.AnaVermelha)) * 5,
			"a gigante machuca o corpo mais forte 5x mais que a ana -- a classe ainda significa algo no fim do jogo");
	}

	// =====================================================================
	// 3) A DESIGUALDADE QUE SEGURA TUDO
	// =====================================================================
	/// <summary>
	/// **A afirmacao mais importante deste arquivo.** O piso do dano tem que ficar acima da cura
	/// passiva; se nao ficar, o sol nao machuca -- ele CURA, e o sistema inteiro vira um teto que
	/// nunca dispara.
	///
	/// Ela mora aqui e nao na bancada ao vivo porque as duas constantes sao do Core, e porque uma
	/// afirmacao que so roda com o Godot aberto e uma afirmacao que ninguem roda.
	/// </summary>
	private static void ADesigualdadeQueSeguraTudo()
	{
		Console.WriteLine("=== 3) O PISO DO DANO CONTRA A CURA PASSIVA ===");

		// O MENOR PISO DE TODOS -- o da menor estrela. Se ELE ficar acima da cura, todos ficam.
		double piso = CalorDaEstrela.DanoDeParidade
					  * CalorDaEstrela.PisoDe(Sistemas.RaioDaClasse(ClasseDeEstrela.AnaVermelha));
		double cura = CombatKnobs.RegenPorSegundo;
		Console.WriteLine($"  dano minimo {piso:0.##}/s   cura {cura:0.##}/s   liquido {piso - cura:0.##}/s");

		Afirmar(piso > cura,
			$"o dano MINIMO ({piso:0.##}/s) e maior que a cura ({cura:0.##}/s) -- ninguem e imune");
		Afirmar(piso > cura * 1.5,
			$"...e com margem de 50% ({piso / cura:0.##}x), pra um ajuste de cura nao virar imunidade calada");

		double maisForte = CalorDaEstrela.SegundosAteCeder(
			Falsa(ClasseDeEstrela.AnaVermelha), 1e13, 100, cura);
		Console.WriteLine($"  o corpo mais forte do jogo (BP 1e13) na estrela mais fraca: {maisForte:0} s");
		Afirmar(!double.IsInfinity(maisForte) && maisForte is > 30 and < 300,
			$"o pior caso do sistema fica entre 30 s e 5 min ({maisForte:0} s)");
	}

	// =====================================================================
	// 4) A GEOMETRIA DA COROA
	// =====================================================================
	private static void AGeometriaDaCoroa()
	{
		Console.WriteLine("=== 4) A COROA AVISA E NAO QUEIMA ===");

		Estrela e = Falsa(ClasseDeEstrela.Amarela);
		Afirmar(CalorDaEstrela.Onde(e, e.Raio) == ProximidadeDaEstrela.Dentro, "a borda ainda queima");
		Afirmar(CalorDaEstrela.Onde(e, e.Raio + 0.001) == ProximidadeDaEstrela.Coroa,
			"um milesimo de pixel fora ja e coroa");
		Afirmar(CalorDaEstrela.Onde(e, e.Raio * CalorDaEstrela.RazaoDaCoroa) == ProximidadeDaEstrela.Coroa,
			$"a coroa vai ate {CalorDaEstrela.RazaoDaCoroa}x o raio");
		Afirmar(CalorDaEstrela.Onde(e, e.Raio * CalorDaEstrela.RazaoDeSaida + 1, jaAvisado: true)
				== ProximidadeDaEstrela.Longe,
			$"quem foi avisado so sai em {CalorDaEstrela.RazaoDeSaida}x (a histerese)");
		Afirmar(CalorDaEstrela.RazaoDeSaida > CalorDaEstrela.RazaoDaCoroa,
			"a saida e mais longe que a entrada -- sem isso o aviso vira metralhadora");

		// QUANTO TEMPO A COROA DA: e o que decide se o aviso serve pra alguma coisa. Na menor estrela
		// sao ~4 s de voo base; na maior, ~22 s -- ou seja quanto maior o corpo, mais cedo o sinal,
		// que e a ordem certa porque tambem e dele que se leva mais tempo pra sair.
		Console.WriteLine($"  {"classe",-18} {"coroa comeca a",16} {"aviso da",12} {"travessia",12}");
		foreach (ClasseDeEstrela c in Classes)
		{
			float r = Sistemas.RaioDaClasse(c);
			double folga = r * (CalorDaEstrela.RazaoDaCoroa - 1);
			Console.WriteLine($"  {c,-18} {r * CalorDaEstrela.RazaoDaCoroa,13:N0} px"
							  + $" {Espaco.SegundosDeViagem(folga),9:0.0} s"
							  + $" {Espaco.SegundosDeViagem(r * 2),9:0.0} s");
		}
		Console.WriteLine();
	}

	// =====================================================================
	// 5) A TABELA
	// =====================================================================
	/// <summary>
	/// QUANTO TEMPO CADA CORPO AGUENTA EM CADA ESTRELA, em segundos -- SIMULADO tique a tique com a
	/// cura ligada, e nao pela formula fechada.
	///
	/// A simulacao existe porque a formula fechada mente um pouco: ela supoe o dano constante, e o
	/// dano nao e (o `expressedBP` cai com o corpo ferido e despenca no nocaute, ou seja o corpo
	/// queima cada vez mais rapido). Aqui a coluna e o piso da verdade -- o jogo mata um pouco antes.
	/// </summary>
	private static void ATabela()
	{
		Console.WriteLine("=== 5) QUANTOS SEGUNDOS CADA CORPO AGUENTA (simulado, cura ligada) ===");

		(string rotulo, double bp)[] corpos =
		[
			("recem-nascido 1e2", 1e2),
			("lutador 1e5", 1e5),
			("SSJ novo 2e6", 2e6),
			("veterano 5e7", 5e7),
			("de elite 1e9", 1e9),
			("teto 1e13", 1e13),
		];

		Console.Write($"  {"classe",-18}");
		foreach ((string rotulo, double _) in corpos) Console.Write($"{rotulo,18}");
		Console.WriteLine();

		foreach (ClasseDeEstrela c in Classes)
		{
			Estrela e = Falsa(c);
			Console.Write($"  {c,-18}");
			foreach ((string _, double bp) in corpos) Console.Write($"{Cozinhar(e, bp),17:0.0}s");
			Console.WriteLine();
		}
		Console.WriteLine();

		// AS DUAS PONTAS DO SISTEMA, afirmadas com numero: nem instantaneo, nem eterno.
		double pior = Cozinhar(Falsa(ClasseDeEstrela.GiganteAzul), 1e2);
		double melhor = Cozinhar(Falsa(ClasseDeEstrela.AnaVermelha), 1e13);
		Afirmar(pior > 0.5, $"o corpo mais fraco na maior estrela ainda leva {pior:0.00} s (nao e instantaneo)");
		Afirmar(melhor is > 30 and < 300, $"o mais forte na menor estrela leva {melhor:0} s (nao e imune)");
		Afirmar(melhor / pior > 30, $"a diferenca entre os extremos e de {melhor / pior:0}x");
	}

	/// <summary>
	/// SIMULA O TIQUE: dano da estrela menos cura, 30x por segundo, ate a vida do membro zerar.
	///
	/// O `expressedBP` fica congelado aqui de proposito -- este e o modelo SIMPLES, e o numero que
	/// ele devolve e o LIMITE SUPERIOR do tempo de vida. A bancada ao vivo mede o de verdade (com o
	/// BP caindo com o corpo) e ele e sempre menor ou igual.
	/// </summary>
	private static double Cozinhar(Estrela e, double bp)
	{
		const double dt = 1.0 / 30;
		double vida = 100, t = 0;
		double dps = CalorDaEstrela.DanoPorSegundo(e, bp);

		for (int i = 0; i < 30 * 600 && vida > 0; i++)
		{
			// A CURA VEM ANTES E TEM TETO, como no jogo: `Curar` nunca passa de `VidaMax`.
			vida = Math.Min(100, vida + CombatKnobs.RegenPorSegundo * dt);
			vida -= dps * dt;
			t += dt;
		}
		return t;
	}

	/// <summary>
	/// UMA ESTRELA DE PAPEL: so classe e raio, que e tudo que as contas leem. Nao ha varredura do
	/// universo aqui de proposito -- esta bancada e sobre a CURVA e nao sobre onde as estrelas estao
	/// (isso e a `sistemas`), e depender da seed faria a tabela mudar sem a formula mudar.
	/// </summary>
	private static Estrela Falsa(ClasseDeEstrela c) => new()
	{
		Classe = c,
		Raio = Sistemas.RaioDaClasse(c),
		Pos = Vec2.Zero,
	};
}
