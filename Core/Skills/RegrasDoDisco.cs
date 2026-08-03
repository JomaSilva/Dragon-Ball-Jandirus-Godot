namespace Jandirus.Core.Skills;

/// <summary>
/// CARREGA AS REGRAS DE NIVEL do `niveis.json` que o AssetPipeline extrai (comando `effector`).
///
/// POR QUE ISTO E SEPARADO DO <see cref="RegrasDeNivel"/>: aquele arquivo tem uma tabela escrita
/// a mao com um punhado de skills -- foi o que permitiu o motor nascer testavel antes de existir
/// extracao. Esta classe e a porta pro lote EXTRAIDO, que e 101 skills e 342 degraus. As duas
/// convivem porque `Registrar` sobrescreve por path: o que vier do disco vence a semente.
///
/// O FORMATO E UMA LISTA PLANA de tres tipos de registro (`skill`, `exp`, `degrau`), e nao um
/// objeto aninhado por skill. Nao e capricho: o leitor de JSON deste projeto e um parser de meia
/// pagina que fatia por `{`..`}` de primeiro nivel, e chave aninhada quebraria TUDO dali pra
/// frente em silencio. O extrator emite plano de proposito -- formato serve leitor.
/// </summary>
public static class RegrasDoDisco
{
	/// <summary>Le e registra. Devolve quantas regras entraram.</summary>
	public static int Carregar(string json)
	{
		// path -> regra em construcao. Os registros de uma mesma skill vem em sequencia, mas
		// depender disso seria depender da ordem do extrator; um mapa nao depende.
		var emObra = new Dictionary<string, RegraDeNivel>(StringComparer.OrdinalIgnoreCase);
		var degraus = new Dictionary<string, List<Degrau>>(StringComparer.OrdinalIgnoreCase);

		foreach (string bloco in Blocos(json))
		{
			string tipo = Str(bloco, "tipo");
			string path = Str(bloco, "path");
			if (path.Length == 0) continue;

			switch (tipo)
			{
				case "skill":
				{
					RegraDeNivel r = Regra(emObra, path);
					double b = Num(bloco, "barreira", NiveisDeSkill.BarreiraPadrao);
					// barreira 0 no extrator quer dizer "a skill nao declarou" -- e ai vale o
					// padrao do DM, nao zero (que faria a skill subir de nivel todo tique)
					r.Barreira = b > 0 ? b : NiveisDeSkill.BarreiraPadrao;

					double taxa = Num(bloco, "curvataxa", 0);
					r.Crescimento = taxa > 0 ? taxa : 1;
					break;
				}

				case "exp":
				{
					RegraDeNivel r = Regra(emObra, path);
					// SO E "POR TEMPO" QUEM SOBE SOZINHO. O extrator marca `contador` quando o
					// exp vem de uma acao contada (golpes disparados, blasts); nesse caso quem
					// credita e o evento, nao o relogio -- ver `NiveisDeSkill.Creditar`.
					if (Str(bloco, "contador").Length == 0 && Num(bloco, "prob", 0) > 0)
						r.GanhoPorTempo = true;
					break;
				}

				case "degrau":
				{
					int nivel = (int)Num(bloco, "nivel", -1);
					// PERIODICO NAO ENTRA. `if(level % 5 == 0)` nao e um degrau, e uma familia
					// deles -- expandir isso em vinte degraus iguais aqui seria inventar uma
					// estrutura que o extrator nao afirmou. Fica de fora, e o relatorio conta.
					if (nivel < 0 || Num(bloco, "periodo", 0) > 0) break;

					var d = new Degrau { Nivel = nivel, Aviso = Str(bloco, "msg") };
					foreach (string par in Lista(bloco, "buffs"))
					{
						int ig = par.IndexOf('=');
						if (ig <= 0) continue;
						if (double.TryParse(par[(ig + 1)..], System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double v))
							d.Buffs[par[..ig]] = v;
					}
					d.Verbos = Lista(bloco, "verbos");
					if (d.Buffs.Count == 0 && d.Verbos.Length == 0 && d.Aviso.Length == 0) break;

					if (!degraus.TryGetValue(path, out List<Degrau>? l)) degraus[path] = l = [];
					l.Add(d);
					break;
				}
			}
		}

		int n = 0;
		foreach ((string path, RegraDeNivel r) in emObra)
		{
			if (degraus.TryGetValue(path, out List<Degrau>? l)) r.Degraus = [.. l.OrderBy(d => d.Nivel)];
			// SKILL SEM DEGRAU NENHUM NAO VIRA REGRA. Ela subiria de nivel pra sempre sem nada
			// acontecer -- barulho no save e no relatorio, efeito zero na tela.
			if (r.Degraus.Length == 0) continue;
			RegrasDeNivel.Registrar(r);
			n++;
		}
		return n;
	}

	private static RegraDeNivel Regra(Dictionary<string, RegraDeNivel> mapa, string path)
	{
		if (!mapa.TryGetValue(path, out RegraDeNivel? r)) mapa[path] = r = new RegraDeNivel { Path = path };
		return r;
	}

	// ---- o mesmo leitor de meia pagina dos outros catalogos ----
	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		var sb = new System.Text.StringBuilder();
		for (int k = a + 1; k < bloco.Length; k++)
		{
			if (bloco[k] == '\\' && k + 1 < bloco.Length) { sb.Append(bloco[++k]); continue; }
			if (bloco[k] == '"') break;
			sb.Append(bloco[k]);
		}
		return sb.ToString();
	}

	private static double Num(string bloco, string chave, double padrao)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return padrao;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '.' or '-' or ' ')) b++;
		return double.TryParse(bloco[a..b].Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : padrao;
	}

	private static string[] Lista(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return [];
		int a = bloco.IndexOf('[', i);
		int b = bloco.IndexOf(']', a + 1);
		if (a < 0 || b < 0) return [];
		var l = new List<string>();
		string dentro = bloco[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			l.Add(dentro[(q1 + 1)..q2]);
			k = q2 + 1;
		}
		return [.. l];
	}
}
