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
///
/// ============================ O LUGAR NAO MORA AQUI: MORA NO `Bercos` ============================
/// Este arquivo monta o CORPO; <see cref="Bercos.Onde"/> decide ONDE ele aparece. Sao separados
/// porque as duas perguntas tem donos diferentes -- o corpo e funcao do catalogo de racas e de um
/// `Random`, e o lugar e funcao PURA da seed do personagem e do universo (as duas pontas precisam
/// chegar no mesmo planeta sem trocar pacote). Juntar os dois faria o nascimento depender de um
/// `Random` pra decidir um lugar que o cliente tambem tem que saber.
///
/// O ELO QUE FALTA LIGAR e a gravidade: o DM acostuma o corpo a gravidade do BERCO
/// (`race.dm:130-131` -- `GravMastered = max(GravMastered, PlanetGravity(spawnPlanet))`, com o
/// comentario do autor "so high-grav races aren't crushed/frozen at spawn"), e o
/// <see cref="GravidadeNatal"/> daqui so tem a metade da RACA. Enquanto todo mundo nascia na Terra
/// a diferenca nao aparecia; com berco de verdade ela aparece, e o `max` do DM e exatamente o que
/// impede um exilado de nascer esmagado no planeta pesado pra onde foi mandado.
/// ==============================================================================================
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
	///
	/// ============================ `classeForcada`: A CLASSE QUE NAO SE SORTEIA ============================
	/// Vazia em todo nascimento de jogador -- a classe dele SEMPRE sai do sorteio, e e por isso que o
	/// jogo tem a "dica de classe" no chat em vez de um menu.
	///
	/// Preenchida so por quem MONTA uma ficha pronta: os NPCs de molde (`Core/Npc`). O original faz o
	/// mesmo e pelo mesmo motivo -- `M.Class = class` antes do `StatRace()`, com o comentario
	/// *"pre-set (non-'None') so the stat procs skip the input() class roll"* (PlanetPopulation.dm:314,
	/// BossEvents.dm:221). Um chefe nao pode sortear a propria classe: a classe muda o poder, e o BP
	/// dele e promessa.
	///
	/// O PARAMETRO ENTRA AQUI E NAO NUM SEGUNDO `Nascer` porque esta funcao e a PORTA UNICA. Uma
	/// copia "igual mas com a classe cravada" teria que repetir a gravidade natal, a linhagem
	/// Saiyajin e o `Tick()` final -- e seria a copia que envelheceria.
	/// ================================================================================================
	/// </summary>
	public static Fighter Nascer(RaceCatalog cat, string raca, string linhagem, Random rng, string nome = "",
								 double bpPai = 0, double bpMae = 0, string classeForcada = "")
	{
		// Half-Saiyan nao tem proto proprio: o corpo vem do Saiyajin, a linhagem manda no resto
		string protoRaca = raca == "Halfbreed" ? "Saiyan" : raca;

		var genoma = Genome.Pure(protoRaca);
		genoma.Class = classeForcada.Length > 0 ? classeForcada : RollClass(cat, raca, linhagem, rng);

		StatBlock bloco = genoma.Build(cat.Protos);
		Fighter f = Fighter.FromGenome(genoma, bloco, genoma.StartingBp(bloco, rng, bpPai, bpMae), nome);

		f.Race = raca;
		f.GravMastered = GravidadeNatal(raca);
		if (raca == "Saiyan") f.SaiyanLineage = linhagem.Length > 0 ? linhagem : "Saiyan";

		f.Tick();   // com a gravidade natal ja no lugar
		return f;
	}
}
