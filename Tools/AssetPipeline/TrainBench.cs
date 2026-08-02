using System.Globalization;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCO DE PROVA DO TREINO.
///
/// Aqui o que interessa nao e um numero exato e sim a CURVA: quanto tempo custa dobrar o
/// poder em cada atividade, e como gravidade, peso e marcos mudam esse tempo. E o unico jeito
/// de saber se o ritmo do jogo sobreviveu a troca de engine -- uma formula pode estar
/// transcrita corretamente e mesmo assim render 100x mais ou menos por tick.
///
/// O relogio e simulado: cada "segundo" roda o numero de ticks que o laco do BYOND rodaria
/// (o Stats dorme 0,2s, entao ~5 chamadas por segundo).
/// </summary>
public static class TrainBench
{
	private const int TicksPorSegundo = 5;    // o laco Stats do BYOND: sleep(2) decimos

	public static void Run(RaceCatalog cat)
	{
		var ci = CultureInfo.InvariantCulture;

		Console.WriteLine("== RITMO DE TREINO ==");
		Console.WriteLine("   Todo mundo comeca em BP 1000 pras colunas serem comparaveis entre si.");
		Console.WriteLine("   Relogio simulado: 5 ticks/s (o laco Stats do BYOND dorme 0,2s), e o spar a");
		Console.WriteLine("   1 golpe/s -- ninguem acerta cinco socos por segundo a luta inteira.\n");
		Console.WriteLine($"{"CENARIO",-40} {"10 min",12} {"1 hora",12} {"6 horas",12} {"x/hora",8}");
		Console.WriteLine(new string('-', 90));

		Cenario(cat, "parado (so o acumulador de ocio)", "Saiyan", "Saiyan", _ => { }, Atividade.Nada);
		Cenario(cat, "Humano meditando", "Human", "", f => f.med = true, Atividade.Meditacao);
		Cenario(cat, "Humano treinando (1g)", "Human", "", f => f.train = true, Atividade.Treino);
		Cenario(cat, "Saiyajin treinando (1g)", "Saiyan", "Saiyan", f => f.train = true, Atividade.Treino);
		Cenario(cat, "Saiyajin em Vegeta (10g)", "Saiyan", "Saiyan", f =>
		{
			f.train = true; f.Planetgrav = 10;
		}, Atividade.Treino);
		Cenario(cat, "Saiyajin em 100g", "Saiyan", "Saiyan", f =>
		{
			f.train = true; f.gravmult = 99; f.GravMastered = 100;
		}, Atividade.Treino);
		Cenario(cat, "Saiyajin em 500g (teto)", "Saiyan", "Saiyan", f =>
		{
			f.train = true; f.gravmult = 499; f.GravMastered = 500;
		}, Atividade.Treino);
		Cenario(cat, "Saiyajin com peso no limite", "Saiyan", "Saiyan", f => f.train = true,
				Atividade.Treino, pesoNoLimite: true);
		Cenario(cat, "Saiyajin com peso no DOBRO do limite", "Saiyan", "Saiyan", f => f.train = true,
				Atividade.Treino, pesoNoLimite: true, fatorPeso: 2);
		Cenario(cat, "Saiyajin lutando (spar)", "Saiyan", "Saiyan", f => f.IsInFight = true, Atividade.Spar);
		Cenario(cat, "Saiyajin ja Super Saiyajin", "Saiyan", "Saiyan", f =>
		{
			f.train = true; f.ReachMilestone("ssj");
		}, Atividade.Treino);
		Cenario(cat, "Saiyajin na Sala do Tempo", "Saiyan", "Saiyan", f =>
		{
			f.train = true; f.Planetgrav = 10; f.zoneGainMult = GainKnobs.TimeChamberMult;
		}, Atividade.Treino);

		Console.WriteLine("\n  A Sala do Tempo so deixa ficar 40 min por visita: e a coluna de [1 hora] cortada");
		Console.WriteLine("  em dois tercos, uma vez por dia. E por isso que ela vale 280x e mesmo assim nao");
		Console.WriteLine("  quebra o jogo sozinha.");

		// -----------------------------------------------------------------
		Console.WriteLine("\n== TETO PESSOAL (relBPmax) ==\n");
		Console.WriteLine($"{"RACA/CLASSE",-30} {"Potential",10} {"BPMod",8} {"teto",12}");
		Console.WriteLine(new string('-', 64));
		var rng2 = new Random(7);
		foreach ((string raca, string lin) in new[]
				 { ("Human", ""), ("Saiyan", "Saiyan"), ("Namekian", "Warrior clan"), ("Kai", ""), ("Majin", "") })
		{
			Fighter f = Birth.Nascer(cat, raca, lin, rng2, raca);
			Console.WriteLine($"{raca + "/" + f.Class,-30} {f.UPMod,10:0.##} {f.BPMod,8:0.##} " +
							  $"{(f.relBPmax / Math.Max(f.BP, 1)).ToString("0.##", ci) + "x BP",12}");
		}
		Console.WriteLine("\n  O teto anda junto com o BP: treinar sempre tem pra onde ir. O que ele limita e");
		Console.WriteLine("  o quanto se ganha DE UMA VEZ -- e o freio contra salto absurdo num tick so.");

		// -----------------------------------------------------------------
		Console.WriteLine("\n== ZENKAI ==\n");
		ZenkaiCaso(cat, "derrota comum vs inimigo 10x", 10, false);
		ZenkaiCaso(cat, "derrota com o corpo em farrapos", 10, true);
		ZenkaiCaso(cat, "inimigo absurdo (1000x), teto corta", 1000, false);
		ZenkaiCaso(cat, "inimigo absurdo + em farrapos", 1000, true);
		Console.WriteLine("\n  O teto e sobre o BP FINAL: comum no maximo DOBRA a base, em farrapos TRIPLICA.");
		Console.WriteLine("  Recarga de 1 hora de relogio real, e para de vir perto do BP de SSJ3.");
	}

	private enum Atividade { Nada, Treino, Meditacao, Spar }

	private static void Cenario(RaceCatalog cat, string nome, string raca, string linhagem,
								Action<Fighter> montar, Atividade atv,
								bool pesoNoLimite = false, double fatorPeso = 1)
	{
		var rng = new Random(12345);
		Fighter f = Birth.Nascer(cat, raca, linhagem, rng, nome);
		f.BP = 1000;                 // mesma largada pra todo mundo
		montar(f);
		f.Tick();

		// o "limite" do peso e o recorde do proprio corpo, que so existe depois do primeiro
		// tick -- vestir peso ANTES disso compara com um teto que ainda nao foi calculado
		if (pesoNoLimite)
		{
			f.Weighted = f.weight_cap_hw / Math.Max(f.Planetgrav + f.gravmult, 1) * fatorPeso;
			f.Tick();
		}

		double inicial = f.BP;
		GainKnobs.TopBP = Math.Max(GainKnobs.TopBP, f.BP);

		double dezMin = 0, umaHora = 0;
		for (int seg = 1; seg <= 6 * 3600; seg++)
		{
			for (int t = 0; t < TicksPorSegundo; t++)
			{
				switch (atv)
				{
					case Atividade.Treino:
						// o laco do BYOND passa `6/(1+ln(missedtrain))`, que cai conforme se
						// repete o mesmo golpe. Uso o ritmo de quem treina variando direcao.
						f.TrainGain(rng, 6.0 / (1 + Math.Log(2)));
						break;
					case Atividade.Meditacao:
						f.MedGain(rng);
						break;
					case Atividade.Spar:
						if (t == 0) f.AttackGain(rng);   // 1 golpe por segundo
						break;
					case Atividade.Nada:
						f.BufferTick();
						break;
				}
				if (atv != Atividade.Nada) f.GravGain();
			}

			f.Tick();                    // refaz relBPmax, Egains e o peso
			GainKnobs.TopBP = Math.Max(GainKnobs.TopBP, f.BP);

			if (seg == 600) dezMin = f.BP;
			if (seg == 3600) umaHora = f.BP;
		}

		// Parado ninguem GANHA BP -- o que enche e o acumulador, que so entra no proximo
		// treino. Mostrar o BP puro daria uma linha de zeros que esconde o mecanismo.
		if (atv == Atividade.Nada)
		{
			Console.WriteLine($"{nome,-40} {"(guardado)",12} {"",12} {f.BPBuffer,12:0.0} {"->",7}");
			return;
		}

		double porHora = umaHora / Math.Max(inicial, 1e-9);
		Console.WriteLine($"{nome,-40} {dezMin,12:0.0} {umaHora,12:0.0} {f.BP,12:0.0} {porHora,7:0.0}x");
	}

	private static void ZenkaiCaso(RaceCatalog cat, string nome, double vezesMaisForte, bool ferido)
	{
		var rng = new Random(99);
		Fighter f = Birth.Nascer(cat, "Saiyan", "Saiyan", rng, "cobaia");
		f.BP = 1000;
		f.Tick();

		double antes = f.BP;
		Fighter.ZenkaiResult r = f.GainZenkai(f.expressedBP * vezesMaisForte, agoraMs: 1_000_000, muitoFerido: ferido);
		Console.WriteLine($"  {nome,-38} {antes,8:0} -> {f.BP,10:0}  ({r}" +
						  (f.UltimoZenkaiNoTeto ? ", no teto)" : ")"));
	}
}
