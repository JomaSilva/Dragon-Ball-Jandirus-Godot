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
	/// NINGUEM NASCE ESMAGADO -- o `race.dm:130-131` do original, que ate agora era so CITADO.
	///
	/// ============================ A REGRA ESTAVA ESCRITA E NAO LIGADA ============================
	/// `GravMastered = max(GravMastered, PlanetGravity(spawnPlanet))`, com o comentario do autor do
	/// DM: *"every race starts acclimated to its home/spawn planet's gravity so high-grav races
	/// aren't crushed/frozen at spawn"*. O port cita essa linha em QUATRO comentarios (dois no
	/// `Berco.cs`, um aqui, um no `CharacterDraft`) -- inclusive pra justificar o teto de 15g dos
	/// bercos sorteados -- e nunca a executou: <see cref="GravidadeNatal"/> acostuma o corpo a
	/// gravidade da RACA, e nao a do lugar onde ele efetivamente nasce.
	///
	/// Enquanto o esmagamento nao existia, isso nao tinha sintoma. Com ele ligado (camada 2 da Sala
	/// do Tempo), a diferenca e a vida do personagem: um Frost Demon nasce em Icer Planet, que puxa
	/// **15x**, com maestria 1 -- razao 15, muito acima das 4 que PRENDEM o corpo no chao. Ele
	/// nasceria imovel, perdendo 3 de vida por segundo, sem ter dado um passo. O mesmo valia pro
	/// Heran (Hera, 10x) e pra qualquer berco sorteado acima da maestria da raca.
	/// ==========================================================================================
	///
	/// O `max` E A REGRA INTEIRA: quem ja domina mais que o berco (o Android, com 200) nao PERDE
	/// maestria por nascer num lugar leve. E o piso de 1 existe porque o espaco tem gravidade 0 e
	/// "acostumado a zero" nao quer dizer nada.
	/// </summary>
	public static void AclimatarAoBerco(Stats.Fighter f, double gravidadeDoBerco) =>
		f.GravMastered = Math.Max(f.GravMastered, Math.Max(gravidadeDoBerco, 1));

	/// <summary>
	/// Sorteia a classe. `linhagem` e o que a tela de criacao coletou (vazio quando a raca
	/// nao escolhe).
	/// </summary>
	public static string RollClass(RaceCatalog cat, string raca, string linhagem, Random rng)
	{
		if (raca == "Saiyan")
		{
			// A TABELA DA LINHAGEM, em milesimos e na ordem do `statsaiyan` -- um sorteio de 1 a 1000 cai na primeira faixa que o cobre.
			(string Classe, int Milesimos)[] tabela = TabelaSaiyajin(linhagem);
			int roll = rng.Next(1, 1001), soma = 0;
			foreach ((string classe, int milesimos) in tabela)
			{
				soma += milesimos;
				if (roll <= soma) return classe;
			}
			return tabela[^1].Classe;
		}

		// Namekuseijin (cla) e Half-Saiyan (linhagem): a escolha JA e a classe
		if (linhagem.Length > 0) return linhagem;

		RaceProto? proto = cat.Get(raca);
		return proto != null ? proto.RollClass(rng) : "None";
	}

	/// <summary>
	/// (2026-10-16) A CLASSE MAIS RARA DESTA RACA -- a de menor chance no sorteio dela (o `Class_Spread` do proto; o Saiyajin pela
	/// tabela propria do <see cref="RollClass"/>: o Lendario, ou o Lendario Primal na linhagem Primal). O verb de admin
	/// `admin_classe_rara` a garante no proximo personagem de uma conta (`AccountSave.ProximaClasseRara`).
	///
	/// VAZIO = A RACA NAO TEM CLASSE RARA: sem sorteio, uma classe so, ou todas com a mesma chance (o Semideus: tres de 33).
	/// EMPATE ENTRE AS MAIS RARAS (o Namekuseijin: Guerreiro e Demonio, 25 cada) = a que o jogador escolheu, se for uma delas; senao
	/// uma delas pelo <paramref name="rng"/>.
	/// </summary>
	public static string ClasseMaisRara(RaceCatalog cat, string raca, string linhagem, Random rng)
	{
		if (raca == "Saiyan") return linhagem == "Primal Saiyan" ? "Legendary Primal Saiyan" : "Legendary";
		if (cat.Get(raca) is not { ClassSpread.Count: > 1 } proto) return "";
		int menor = proto.ClassSpread.Min(c => c.Chance), maior = proto.ClassSpread.Max(c => c.Chance);
		if (menor == maior) return "";
		List<string> raras = [.. proto.ClassSpread.Where(c => c.Chance == menor).Select(c => c.Classe)];
		return raras.Contains(linhagem) ? linhagem : raras[rng.Next(raras.Count)];
	}

	/// <summary>
	/// AS DUAS TABELAS DO SAIYAJIN, em milesimos (`statsaiyan.dm`): 1% Legendary, 4% Elite, 45% Low-Class, 50% Normal; na linhagem
	/// Primal, 2% Legendary Primal e 98% Normal Primal. Uma copia so -- o sorteio (<see cref="RollClass"/>) e a regua do filho
	/// (<see cref="ChanceDaClasse"/>) leem a mesma.
	/// </summary>
	private static (string Classe, int Milesimos)[] TabelaSaiyajin(string linhagem) => linhagem == "Primal Saiyan"
		? [("Legendary Primal Saiyan", 20), ("Normal Primal Saiyan", 980)]
		: [("Legendary", 10), ("Elite", 40), ("Low-Class", 450), ("Normal", 500)];

	/// <summary>
	/// (2026-10-17) A CHANCE DE UMA CLASSE NASCER NESTA RACA, em milesimos -- a regua da "mais rara entre os pais" do filho
	/// (`Filhos.ClasseDoFilho`). O Saiyajin pela tabela da linhagem; o resto pelo `Class_Spread` do proto. Zero = essa classe nao nasce
	/// aqui (ou nesta linhagem).
	/// </summary>
	public static double ChanceDaClasse(RaceCatalog cat, string raca, string linhagem, string classe)
	{
		if (raca == "Saiyan")
		{
			foreach ((string c, int milesimos) in TabelaSaiyajin(linhagem))
				if (c == classe) return milesimos;
			return 0;
		}
		if (cat.Get(raca) is not { } proto) return 0;
		foreach ((string c, int chance) in proto.ClassSpread)
			if (c == classe) return chance * 10;
		return 0;
	}

	/// <summary>(2026-10-16) Esta raca TEM classe rara? E por ela que o sorteio de raca da garantia filtra. Ver <see cref="ClasseMaisRara"/>.</summary>
	public static bool TemClasseRara(RaceCatalog cat, string raca) => ClasseMaisRara(cat, raca, "", new Random(0)).Length > 0;

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
