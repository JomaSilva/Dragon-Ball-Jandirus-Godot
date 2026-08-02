namespace Jandirus.Core.Races;

/// <summary>
/// O PROTOTIPO de uma raca: os numeros crus que o BYOND guardava em
/// `/datum/genetics/proto/X`. Imutavel e global -- um por raca, nunca por personagem.
///
/// No DM isso era um par de dicionarios (`m_stats` com 12 chaves e `misc_stats` com 14)
/// mais `class_stats` (por classe) e `Class_Spread` (o sorteio). Mantemos a forma de
/// dicionario de proposito: a lista de stats ainda vai crescer e um dicionario deixa o
/// dado vir de arquivo em vez de virar 26 campos digitados a mao.
/// </summary>
public sealed class RaceProto
{
	public string Name = "";

	/// <summary>Stats de combate (Physical Offense, Ki Skill, Technique, Speed...).</summary>
	public Dictionary<string, double> Stats = new(StringComparer.Ordinal);

	/// <summary>Stats diversos (Potential, Regeneration, Lifespan, Starting BP, Anger...).</summary>
	public Dictionary<string, double> Misc = new(StringComparer.Ordinal);

	/// <summary>
	/// Por classe, as chaves que a classe SUBSTITUI (nao multiplica -- o comentario do DM
	/// dizia o contrario, mas o codigo que roda substitui).
	/// </summary>
	public Dictionary<string, Dictionary<string, double>> ClassStats = new(StringComparer.Ordinal);

	/// <summary>
	/// Sorteio de classe. NAO e roleta ponderada: no DM e um prob() SEQUENCIAL, e a ULTIMA
	/// entrada absorve todo mundo que falhou. Por isso a classe comum vem por ultimo.
	/// </summary>
	public List<(string Classe, int Chance)> ClassSpread = [];

	public double Stat(string chave) => Stats.GetValueOrDefault(chave);
	public double MiscStat(string chave) => Misc.GetValueOrDefault(chave);

	/// <summary>Sorteia a classe do jeito do DM: prob sequencial, ultima absorve o resto.</summary>
	public string RollClass(Random rng)
	{
		if (ClassSpread.Count == 0) return "None";
		for (int i = 0; i < ClassSpread.Count; i++)
		{
			(string classe, int chance) = ClassSpread[i];
			if (i == ClassSpread.Count - 1) return classe;      // a ultima leva quem sobrou
			if (rng.Next(1, 101) <= chance) return classe;
		}
		return ClassSpread[^1].Classe;
	}
}
