using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// A BANCADA DO ULTIMO TURF (`subsolo`).
///
/// A queixa do dono (2026-09-06), no castelo de Vegeta: *"o icone nao aparecer, so a hitbox, em varios
/// locais do mapa"*. Uma celula do `.dmm` pode listar DOIS turfs, `(/turf/decor/Table4,
/// /turf/Tile/Tile5)`: no BYOND so o ultimo existe (cada `new /turf` substitui o anterior), e o piso e
/// o que se ve e o que se pisa. O conversor desenhava certo (o primeiro por baixo, o ultimo por cima)
/// mas somava a FISICA dos dois -- a mesa densa por baixo do piso virava parede invisivel.
///
/// Esta bancada le os 40 andares do `.dmm`, a arvore de tipos e o `.col` do DISCO e cobra, celula por
/// celula empilhada: turf denso por baixo de um turf que nao e denso (e sem obj denso nem barreira na
/// celula) NAO bloqueia; turf denso por cima (com arte, e que nao e passagem) bloqueia. Com o
/// contra-exemplo injetado -- os bits da regra velha religados em memoria -- a mesma cobranca tem que
/// REPROVAR. E as celulas nominais do castelo: a fileira de mesas livre, a parede acima bloqueando, o
/// banco (um `/obj` denso) bloqueando.
/// </summary>
public static class SubsoloBench
{
	public static int Run(string pastaDmm, string pastaCode, string pastaMaps)
	{
		int ok = 0, falhou = 0;
		void Afirmar(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; Console.WriteLine($"  OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; Console.WriteLine($"  FALHA {nome}   [{detalhe}]"); }
		}

		Console.WriteLine("==== SUBSOLO: so o ultimo turf da celula tem corpo ====");
		Dictionary<string, TurfDef> turfs = DmTurfScanner.Scan(pastaCode);

		int empilhadas = 0, subsolo = 0, subsoloLivre = 0, topoDenso = 0, topoBloqueia = 0, andares = 0;
		int regraVelhaBloquearia = 0;
		var porTipo = new Dictionary<string, int>(StringComparer.Ordinal);
		var ofensoras = new List<string>();
		ZoneCollision? vegeta = null;

		int offset = 0;
		foreach (string arq in MapConverter.OrdemDoDme(pastaDmm))
		{
			DmmMap.Result d = DmmMap.Read(arq);
			foreach (DmmLevel nivel in d.Levels)
			{
				string nome = MapConverter.NomeDoAndar(d, nivel, offset);
				string arqCol = Path.Combine(pastaMaps, nome + ".col");
				if (!File.Exists(arqCol) || ZoneCollision.Load(File.ReadAllBytes(arqCol)) is not { } col)
				{
					Console.WriteLine($"  {nome}: sem .col no disco -- pulado");
					continue;
				}
				andares++;
				if (nome.EndsWith("_Vegeta", StringComparison.Ordinal)) vegeta = col;

				// A REGRA VELHA, EM MEMORIA: os bits do disco mais os que o subsolo denso religaria.
				var velha = new HashSet<(int, int)>();

				for (int y = 0; y < nivel.Height; y++)
					for (int x = 0; x < nivel.Width; x++)
					{
						string? k = nivel.Cells[x, y];
						if (k == null || !d.Keys.TryGetValue(k, out string[]? tipos)) continue;
						List<string> bps = tipos.Select(DmmMap.BasePath).ToList();
						List<string> ts = bps.Where(bp => bp.StartsWith("/turf", StringComparison.Ordinal)).ToList();
						if (ts.Count < 2) continue;
						string fundo = ts[0], topo = ts[^1];
						if (fundo == topo) continue;
						empilhadas++;

						bool fundoDenso = turfs.TryGetValue(fundo, out TurfDef? tf) && tf.Density;
						bool topoDensoAqui = turfs.TryGetValue(topo, out TurfDef? tt) && tt.Density;
						bool topoTemArte = tt?.Icon != null;
						bool objDenso = bps.Any(bp => bp.StartsWith("/obj", StringComparison.Ordinal)
													  && ((turfs.TryGetValue(bp, out TurfDef? to) && to.Density)
														  || (bp.StartsWith("/obj/barrier/", StringComparison.Ordinal)
															  && !bp.Contains("kaio_gate", StringComparison.OrdinalIgnoreCase))));

						if (fundoDenso && !topoDensoAqui && !objDenso)
						{
							subsolo++;
							porTipo[fundo] = porTipo.GetValueOrDefault(fundo) + 1;
							if (!col.BlockedCell(x, y)) subsoloLivre++;
							else if (ofensoras.Count < 12) ofensoras.Add($"{nome} ({x},{y}) {fundo} sob {topo}");
							velha.Add((x, y));
						}
						if (topoDensoAqui && topoTemArte && !Passagens.Eh(topo))
						{
							topoDenso++;
							if (col.BlockedCell(x, y)) topoBloqueia++;
						}
					}

				// O CONTRA-EXEMPLO INJETADO deste andar: religa os bits do subsolo num mapa copiado e conta
				// quantos a cobranca acima reprovaria. Tem que ser TODOS os que a regra nova libertou.
				if (velha.Count > 0)
				{
					var bits = new byte[(nivel.Width * nivel.Height + 7) / 8];
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
							if (col.BlockedCell(x, y) || velha.Contains((x, y)))
							{
								int i = y * nivel.Width + x;
								bits[i >> 3] |= (byte)(1 << (i & 7));
							}
					ZoneCollision antiga = ZoneCollision.Montar(nivel.Width, nivel.Height, bits);
					foreach ((int x, int y) in velha) if (antiga.BlockedCell(x, y)) regraVelhaBloquearia++;
				}
			}
			offset += d.Levels.Count;
		}

		Console.WriteLine($"  andares lidos: {andares} | celulas com dois turfs: {empilhadas} | subsolo denso sem corpo por cima: {subsolo}");
		foreach ((string tipo, int n) in porTipo.OrderByDescending(kv => kv.Value).Take(8))
			Console.WriteLine($"     {tipo} x{n}");
		foreach (string o in ofensoras) Console.WriteLine($"     AINDA BLOQUEIA: {o}");

		Afirmar("ha celulas com turf denso POR BAIXO de um turf sem corpo (o caso existe no mapa, e nao e raro)", subsolo > 100, $"{subsolo}");
		Afirmar("TODAS elas estao LIVRES no .col do disco (o piso por cima e o que se pisa)", subsolo > 0 && subsoloLivre == subsolo, $"{subsoloLivre} de {subsolo}");
		Afirmar("e o turf denso POR CIMA continua bloqueando (a regra nao abriu o que devia ficar fechado)",
				topoDenso > 0 && topoBloqueia == topoDenso, $"{topoBloqueia} de {topoDenso}");
		Afirmar("contra-exemplo: com os bits da regra velha religados em memoria, a mesma cobranca reprova cada uma delas",
				regraVelhaBloquearia == subsolo, $"{regraVelhaBloquearia} de {subsolo}");

		// AS NOMINAIS DO CASTELO DE VEGETA: a fileira de mesas (118..123, 215) livre, a parede (118..126, 213)
		// bloqueando, e o banco (122,214) -- um `/obj/Bank` denso sobre um piso -- bloqueando.
		if (vegeta != null)
		{
			bool mesasLivres = Enumerable.Range(118, 6).All(x => !vegeta.BlockedCell(x, 215));
			bool paredeFechada = Enumerable.Range(118, 9).All(x => vegeta.BlockedCell(x, 213));
			Afirmar("Vegeta: a fileira de mesas do castelo (118..123, 215), `Table*` sob `Tile5`, esta LIVRE", mesasLivres);
			Afirmar("Vegeta: a parede acima dela (118..126, 213) continua BLOQUEANDO", paredeFechada);
			Afirmar("Vegeta: o banco (122,214), um `/obj/Bank` denso sobre o piso, continua BLOQUEANDO", vegeta.BlockedCell(122, 214));
			Afirmar("Vegeta: a celula da cadeira ao lado (119,214), `/obj/buildables/chair` sem densidade, esta LIVRE", !vegeta.BlockedCell(119, 214));
		}
		else Afirmar("o .col de Vegeta foi lido", false);

		Console.WriteLine($"==== {ok} OK, {falhou} FALHA(S) ====");
		return falhou == 0 ? 0 : 1;
	}
}
