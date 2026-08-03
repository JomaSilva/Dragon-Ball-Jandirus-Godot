using Jandirus.Core.Stats;

namespace Jandirus.Core.Races;

/// <summary>
/// NASCIMENTO: o que acontece entre "escolhi raca e linhagem" e "existe um lutador".
///
/// Nao e so o `Class_Spread` do proto. Duas racas fogem dele:
///
///  * SAIYAJIN -- a linhagem (Saiyan x Primal Saiyan) NAO e classe, e o `statsaiyan()` do
///    BYOND rola a classe com uma tabela PROPRIA que o spread do proto nem conhece. O Primal
///    tem so duas saidas (2% Legendary Primal, 98% Normal Primal).
///  * HALFBREED -- nao tem proto proprio: as tres linhagens sao class_stats que vivem no
///    proto SAIYAJIN. A escolha do jogador ja E a classe.
///
/// A gravidade natal tambem entra aqui: um Saiyajin nasce aclimatado a 10g, senao ele seria
/// esmagado no proprio planeta no primeiro segundo de jogo.
/// </summary>
public static class Birth
{
	/// <summary>
	/// Gravidade que a raca ja domina de berco (o `GravMastered` que os `stat<Raca>()`
	/// cravam). Quem nao esta aqui comeca em 1 (gravidade da Terra).
	/// </summary>
	public static double GravidadeNatal(string raca) => raca switch
	{
		"Saiyan" or "Tsujin" => 10,     // nativos de Vegeta
		"Yardrat" => 23,
		"Demon" => 25,
		"Android" => 200,
		_ => 1,
	};

	/// <summary>
	/// Sorteia a classe. `linhagem` e o que a tela de criacao coletou (vazio quando a raca
	/// nao escolhe).
	/// </summary>
	public static string RollClass(RaceCatalog cat, string raca, string linhagem, Random rng)
	{
		if (raca == "Saiyan")
		{
			if (linhagem == "Primal Saiyan")
				return rng.Next(1, 1001) <= 20 ? "Legendary Primal Saiyan" : "Normal Primal Saiyan";

			// 1% Legendary, 4% Elite, 45% Low-Class, 50% Normal -- a tabela literal do statsaiyan
			int roll = rng.Next(1, 1001);
			if (roll <= 10) return "Legendary";
			if (roll <= 50) return "Elite";
			if (roll <= 500) return "Low-Class";
			return "Normal";
		}

		// Namekuseijin (cla) e Half-Saiyan (linhagem): a escolha JA e a classe
		if (linhagem.Length > 0) return linhagem;

		RaceProto? proto = cat.Get(raca);
		return proto != null ? proto.RollClass(rng) : "None";
	}

	/// <summary>
	/// Monta o lutador completo: genoma, stats, BP e os ajustes de berco que nao moram no
	/// proto. Esta e a porta unica de nascimento -- servidor e ferramentas usam so ela.
	/// </summary>
	public static Fighter Nascer(RaceCatalog cat, string raca, string linhagem, Random rng, string nome = "",
								 double bpPai = 0, double bpMae = 0)
	{
		// Half-Saiyan nao tem proto proprio: o corpo vem do Saiyajin, a linhagem manda no resto
		string protoRaca = raca == "Halfbreed" ? "Saiyan" : raca;

		var genoma = Genome.Pure(protoRaca);
		genoma.Class = RollClass(cat, raca, linhagem, rng);

		StatBlock bloco = genoma.Build(cat.Protos);
		Fighter f = Fighter.FromGenome(genoma, bloco, genoma.StartingBp(bloco, rng, bpPai, bpMae), nome);

		f.Race = raca;
		f.GravMastered = GravidadeNatal(raca);
		if (raca == "Saiyan") f.SaiyanLineage = linhagem.Length > 0 ? linhagem : "Saiyan";

		f.Tick();   // com a gravidade natal ja no lugar
		return f;
	}
}
