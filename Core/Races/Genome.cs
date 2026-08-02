namespace Jandirus.Core.Races;

/// <summary>
/// O genoma VIVO de um personagem: de quem ele descende e em que proporcao, mais a classe.
/// Equivale ao "carrier" do DM (`mob.genome`), que guardava so `racial_protos` + `this_class`.
///
/// DUAS CORREÇOES DELIBERADAS em relacao ao BYOND (decididas pelo dono do projeto):
///
///  1) COMPOSICAO DE STAT. O DM fazia `stat / (peso/100)` -- uma DIVISAO. Com raca pura
///     (peso 100) isso e identidade e passava despercebido, mas filho de casal (peso 50)
///     saia com o DOBRO dos stats e filho de racas diferentes ou de ovo (peso 25) com o
///     QUADRUPLO -- nascer de gravidez era mecanicamente superior a criar no menu. O
///     comentario do proprio arquivo DM descrevia a intencao oposta. Aqui MULTIPLICAMOS
///     pelo peso: 50% de saiyajin com Physical Offense 3 contribui 1,5. Filho fica ENTRE
///     os pais, que e o que "meio saiyajin" deveria significar.
///
///  2) HERANCA DE BP. No DM o bonus dos pais era escrito e depois SOBRESCRITO pela semente
///     da raca -- ter pai forte nao valia nada. Aqui a semente vem primeiro e o bonus dos
///     pais entra DEPOIS (ver <see cref="StartingBp"/>).
/// </summary>
public sealed class Genome
{
	/// <summary>Ancestral -> percentual (soma ~100). Raca pura = um so, com 100.</summary>
	public Dictionary<string, double> Ancestry = new(StringComparer.Ordinal);

	public string Class = "None";
	public string MajorityRace = "";

	/// <summary>Ajustes acumulados fora do nascimento (itens, desejos, admin).</summary>
	public Dictionary<string, double> Modifiers = new(StringComparer.Ordinal);

	public static Genome Pure(string raca)
	{
		var g = new Genome();
		g.Ancestry[raca] = 100;
		g.MajorityRace = raca;
		return g;
	}

	public static Genome Child(string racaPai, string racaMae)
	{
		var g = new Genome();
		if (racaPai == racaMae)
		{
			g.Ancestry[racaPai] = 100;
			g.MajorityRace = racaPai;
		}
		else
		{
			g.Ancestry[racaPai] = 50;
			g.Ancestry[racaMae] = 50;
			g.MajorityRace = racaPai; // desempate resolvido pelo chamador quando houver regra propria
		}
		return g;
	}

	/// <summary>Quanto por cento desta ancestralidade o personagem tem (o `race_percent` do DM).</summary>
	public double RacePercent(string raca) => Ancestry.GetValueOrDefault(raca);

	/// <summary>
	/// Compoe os stats finais a partir dos protos dos ancestrais.
	/// Ordem: para cada ancestral, parte do proto, deixa a CLASSE substituir as chaves que ela
	/// define, e entao pondera pelo percentual. Somatorio no fim.
	/// </summary>
	public StatBlock Build(IReadOnlyDictionary<string, RaceProto> protos)
	{
		var saida = new StatBlock();
		if (Ancestry.Count == 0) return saida;

		foreach ((string nome, double pct) in Ancestry)
		{
			if (!protos.TryGetValue(nome, out RaceProto? proto)) continue;
			double peso = pct / 100.0;   // MULTIPLICA (o DM dividia -- ver o cabecalho da classe)

			var stats = new Dictionary<string, double>(proto.Stats, StringComparer.Ordinal);
			var misc = new Dictionary<string, double>(proto.Misc, StringComparer.Ordinal);

			// a classe SUBSTITUI a chave, nao soma nem multiplica
			if (proto.ClassStats.TryGetValue(Class, out Dictionary<string, double>? doClasse))
				foreach ((string k, double v) in doClasse)
				{
					if (stats.ContainsKey(k)) stats[k] = v;
					if (misc.ContainsKey(k)) misc[k] = v;
				}

			foreach ((string k, double v) in stats) saida.Stats[k] = saida.Stats.GetValueOrDefault(k) + v * peso;
			foreach ((string k, double v) in misc) saida.Misc[k] = saida.Misc.GetValueOrDefault(k) + v * peso;
		}

		foreach ((string k, double v) in Modifiers)
		{
			if (saida.Stats.ContainsKey(k)) saida.Stats[k] += v;
			else if (saida.Misc.ContainsKey(k)) saida.Misc[k] += v;
		}

		return saida;
	}

	/// <summary>
	/// BP inicial. A semente vem da raca/classe (formula do DM, 1:1) e SO DEPOIS entra o
	/// bonus dos pais -- no BYOND a ordem era inversa e a heranca morria sem deixar rastro.
	/// </summary>
	public double StartingBp(StatBlock stats, Random rng, double bpPai = 0, double bpMae = 0)
	{
		// caso cravado no DM: o Frost Demon mutante ignora a semente e nasce enorme
		if (Class == "Mutant Frost Demon") return rng.Next(1_000_000, 3_000_001);

		double sbp = stats.Misc.GetValueOrDefault("Starting BP");
		if (sbp <= 0) sbp = 1;

		int lo = (int)(1 / sbp * 100);
		int hi = (int)(200 * sbp);
		if (hi <= lo) hi = lo + 1;
		double bp = sbp / 10.0 + rng.Next(lo, hi) / 100.0;

		// heranca: uma fracao do poder de cada progenitor, aplicada DEPOIS da semente
		if (bpPai > 0) bp += rng.Next((int)(bpPai / 24), Math.Max((int)(bpPai / 6), (int)(bpPai / 24) + 1));
		if (bpMae > 0) bp += rng.Next((int)(bpMae / 24), Math.Max((int)(bpMae / 6), (int)(bpMae / 24) + 1));

		return Math.Max(1, bp);
	}
}

/// <summary>Resultado da composicao: os stats prontos pra virar os atributos do mob.</summary>
public sealed class StatBlock
{
	public Dictionary<string, double> Stats = new(StringComparer.Ordinal);
	public Dictionary<string, double> Misc = new(StringComparer.Ordinal);

	public double Get(string chave) => Stats.TryGetValue(chave, out double v) ? v : Misc.GetValueOrDefault(chave);
}
