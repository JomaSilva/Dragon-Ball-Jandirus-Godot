using System;
using System.Diagnostics;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO TERRENO GERADO -- quanto custa fazer um mundo, e que mundos o universo sorteia.
///
/// ============================ POR QUE ELA EXISTE ============================
/// O teto de tamanho dos planetas gerados sempre foi um numero MEDIDO, nao escolhido: gerar nao da
/// pra fatiar (a colisao tem que existir inteira antes do primeiro passo), e no servidor esse tempo
/// para o tick de todo mundo. O comentario do `MundoProcedural` cita medicoes desde a primeira
/// versao -- so que elas eram feitas a mao e envelheciam caladas.
///
/// A segunda metade e a que realmente decide se um teto e seguro: nao adianta saber que um mundo de
/// 1000 custa 220 ms sem saber COM QUE FREQUENCIA o universo sorteia um. Como o tamanho sai da
/// gravidade, e a gravidade tem cauda pesada de proposito, o caso caro tem que ser o caso raro --
/// e isso e uma afirmacao verificavel, nao uma esperanca.
/// ============================================================================
///
///     dotnet run --project Tools/AssetPipeline -- terreno [amostras]
/// </summary>
public static class TerrenoBench
{
	public static void Run(int amostras)
	{
		Console.WriteLine("=== CUSTO DE GERAR, POR TAMANHO ===");
		Console.WriteLine($"{"lado",6} {"celulas",10} {"gerar",9} {"us/celula",10} {"memoria",9}");

		foreach (int lado in new[] { 192, 256, 352, 500, 700, 1000 })
		{
			// UMA GERACAO DE AQUECIMENTO, fora da conta: a primeira paga o JIT do gerador inteiro
			// e sairia duas a tres vezes mais cara que as seguintes. Medir isso seria medir o
			// compilador, nao o algoritmo.
			Medir(lado, 1);

			double ms = Medir(lado, 3);
			long celulas = (long)lado * lado;
			Console.WriteLine($"{lado,6} {celulas,10:N0} {ms,7:0.0} ms {ms * 1000 / celulas,9:0.00} "
							  + $"{celulas * 2 / 1024.0 / 1024.0,7:0.0} MB");
		}

		Console.WriteLine();
		Console.WriteLine($"=== O QUE O UNIVERSO SORTEIA ({amostras:N0} planetas) ===");

		int[] baldes = new int[6];
		string[] rotulos = ["ate 250", "251-350", "351-500", "501-700", "701-900", "901-1000"];
		double somaLado = 0;
		int maior = 0;

		// AS SEEDS SAO AS DE VERDADE: quem decide que um planeta existe e com que seed e o
		// `Sistemas.Do`, varrendo celula por celula e pegando as orbitas. Sortear um `ulong` qualquer
		// mediria a escada de gravidade, e nao o universo.
		int achados = 0;
		for (int i = 0; achados < amostras && i < amostras; i++)
		{
			if (Sistemas.Do(1234567, i % 4096, i / 4096) is not { } s) continue;

			foreach (PlanetaNoEspaco p in s.Planetas())
			{
				if (p.Premade) continue;   // pre-feito tem mapa proprio: nao passa pelo gerador
				achados++;

				MundoProcedural m = MundoProcedural.DaSeed(p.Seed, p.Nome);
				somaLado += m.Lado;
				if (m.Lado > maior) maior = m.Lado;

				int b = m.Lado <= 250 ? 0 : m.Lado <= 350 ? 1 : m.Lado <= 500 ? 2
					  : m.Lado <= 700 ? 3 : m.Lado <= 900 ? 4 : 5;
				baldes[b]++;
			}
		}

		if (achados == 0) { Console.WriteLine("nenhum planeta encontrado -- confira o `Sistemas.Do`"); return; }

		for (int b = 0; b < baldes.Length; b++)
		{
			double pct = baldes[b] * 100.0 / achados;
			Console.WriteLine($"  lado {rotulos[b],-9} {baldes[b],6} ({pct,5:0.0}%)  {new string('#', (int)pct)}");
		}

		Console.WriteLine($"\n  lado medio {somaLado / achados:0} | maior sorteado {maior}");

		// O NUMERO QUE IMPORTA: quanto do tempo de servidor a cauda cara representa. Um planeta de
		// 1000 custa ~8x um de 352, mas so aparece em 3% dos mundos.
		double custoMedio = somaLado / achados * (somaLado / achados) * 0.22 / 1000;
		Console.WriteLine($"  custo medio estimado por pouso novo: ~{custoMedio:0} ms");
	}

	private static double Medir(int lado, int vezes)
	{
		var relogio = new Stopwatch();
		for (int i = 0; i < vezes; i++)
		{
			var p = new ParametrosDeTerreno
			{
				Seed = (ulong)(0xABCDEF + i * 7919),
				Largura = lado,
				Altura = lado,
				Bioma = BiomaDeTerreno.Jardim,
				Gravidade = 5,
				Nome = "bancada",
			};
			relogio.Start();
			GeradorDeTerreno.Gerar(p);
			relogio.Stop();
		}
		return relogio.Elapsed.TotalMilliseconds / vezes;
	}
}
