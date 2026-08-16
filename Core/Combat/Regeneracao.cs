using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// ============================ O EIXO DA CURA -- `assign_regen()` do DM, colado ============================
/// `Code/Modules/Races/Genetics/Genetic_Datum.dm:247-267`. **Nada aqui e invencao**: o DM ja separa
/// quem cura de quem nao cura por UM numero do genoma (`misc_stats["Regeneration"]`), e desse numero
/// ele deriva quatro coisas. Este struct e essas quatro coisas.
///
/// O que o dono pediu ja estava desenhado la, e a tabela dele bate linha a linha:
///
///   | raca      | Reg | <see cref="CuraEmCombate"/> | <see cref="MembroVolta"/> |
///   |-----------|-----|------------------|-----------------|
///   | Humano    |   1 | nao              | nao             |
///   | Saiyajin  |   1 | nao              | nao             |
///   | Namekuseijin | 10 | **NAO** (`return` proprio) | **nao** (a ativa e que resolve) |
///   | Kai/Meta/Tsujin/Yardrat | 10 | sim | sim |
///   | Bio-Androide | 30 | sim           | sim             |
///   | **Majin** | 100 | **SIM**          | **SIM**         |
///
/// **O NAMEKUSEIJIN TEM UM `return` SO DELE** (`:253-258`), e o comentario do proprio autor do DM
/// explica: *"REWORK NAMEKUSEIJIN: sem regeneracao passiva acelerada -- nada de regen em combate,
/// membro decepado NAO volta sozinho... A regeneracao racial agora e a skill ATIVA
/// Namekian_Regeneration"*. E exatamente o que o dono descreveu de memoria.
///
/// **O COMENTARIO DO DM MENTE SOBRE O FROST DEMON.** `Genetic_Datum.dm:259-260` diz
/// *"(Majin/Bio-Android/Frost)"*, mas `staticer.dm:39` da `Regeneration = 1` ao Icer -- ou seja ele
/// NAO passa do 5 e NAO tem `fastRegen`. O comentario e velho; quem manda e a tabela.
/// =========================================================================================================
/// </summary>
public readonly record struct PerfilDeRegen
{
	/// <summary>O `misc_stats["Regeneration"]` cru, guardado pra quem quiser mostrar/depurar.</summary>
	public double Regeneration { get; init; }

	/// <summary>
	/// O `fastRegen`: **cura MESMO COM A TAG DE COMBATE NO AR**. E ESTE o eixo do pedido do dono --
	/// "so o Majin regenera em combate" nao e um `if (raca == Majin)`, e `Regeneration >= 5` com o
	/// desvio do Namekuseijin. `Injuries.dm:251/290/293` e `master.dm:185/186` repetem a MESMA
	/// guarda `(!combatTag || fastRegen)` nos quatro canais de cura passiva.
	/// </summary>
	public bool CuraEmCombate { get; init; }

	/// <summary>
	/// O `passiveRegen`. Racas comuns dividem por 5 (`:264`), as aceleradas nao (`:262`). Alimenta o
	/// `SpreadHeal(0.1 * passiveRegen)` de `Injuries.dm:294`.
	/// </summary>
	public double Passiva { get; init; }

	/// <summary>
	/// O `activeRegen` = `1 + Reg/25`. Ele NAO e usado pela skill ativa do Namekuseijin (que cura ao
	/// cheio, ver `namekian.dm:197-204`) -- ele alimenta o `prob(5*activeRegen)` do buffer de membro
	/// (`Injuries.dm:295`) e a `Regenerate` das arvores de Alien/Android/Demonio (`alien.dm:88`).
	/// </summary>
	public double Ativa { get; init; }

	/// <summary>
	/// O `DeathRegen` = `round(Reg/10)` -- e `round()` de UM argumento no BYOND e **PISO**, nao
	/// arredondamento. Com Reg 8 (Gray) da 0; com Reg 10 da 1; com Reg 100 (Majin) da 10.
	/// </summary>
	public int RegenDeMorte { get; init; }

	/// <summary>
	/// O `canheallopped` (`:266-267`): **membro decepado volta SOZINHO**. E o mesmo bit que decide
	/// se perder um nucleo poe em COMA em vez de matar (`Injuries.dm:277`).
	/// </summary>
	public bool MembroVolta => RegenDeMorte >= 1;

	/// <summary>O padrao de quem nao tem proto: Humano/Saiyajin, `Regeneration = 1`.</summary>
	public static PerfilDeRegen Comum => De("Human", 1);

	/// <summary>
	/// `assign_regen()`, linha a linha. <paramref name="raca"/> e o nome que o port usa; o teste do
	/// DM e `Race == "Namekian" || Parent_Race == "Namekian"`, e no port a linhagem de Namek
	/// aparece pelo mesmo nome (as tres tribos sao classes do proto "Namekian", nao racas irmas).
	/// </summary>
	public static PerfilDeRegen De(string raca, double regeneration)
	{
		double reg = Math.Max(regeneration, 0);
		double ativa = 1 + reg / 25;

		// O RAMO PROPRIO DO NAMEKUSEIJIN (`Genetic_Datum.dm:253-258`), e ele sai ANTES de tudo:
		// sem cura em combate, taxa passiva de raca comum e DeathRegen ZERO -- ou seja o membro
		// perdido dele NAO volta sozinho, e perder um nucleo MATA. Quem devolve membro pra ele e a
		// skill ativa `Namekian_Regeneration` (ver `GameServer.Raciais.Regenerar`).
		if (raca is "Namekian")
			return new PerfilDeRegen
			{
				Regeneration = reg,
				CuraEmCombate = false,
				Passiva = (0.2 + reg / 50) / 5,
				Ativa = ativa,
				RegenDeMorte = 0,
			};

		bool rapida = reg >= 5;
		return new PerfilDeRegen
		{
			Regeneration = reg,
			CuraEmCombate = rapida,                                    // `:259`
			Passiva = rapida ? 0.2 + reg / 50 : (0.2 + reg / 50) / 5,   // `:261-264`
			Ativa = ativa,                                             // `:265`
			RegenDeMorte = (int)Math.Floor(reg / 10),                   // `:266` -- round() de 1 arg = PISO
		};
	}
}

/// <summary>
/// ============================ A CURA PASSIVA INTEIRA, NUM LUGAR SO ============================
/// **ESTE ARQUIVO E A REGRA.** Antes dele o port tinha UMA taxa (`CombatKnobs.RegenPorSegundo`,
/// 1,667 de vida por segundo em cada membro), plana, igual pra toda raca, todo membro, e desligada
/// por um `EmCombate > 0` que valia pra todo mundo -- inclusive pro Majin. O resultado, medido
/// contra o DM: **um braco quebrado saia de "Quebrado" em 9 s onde o original leva 105**, e ficava
/// inteiro em 57 s onde o original leva 598.
///
/// O DM nao tem uma taxa: tem QUATRO CANAIS, todos no mesmo laco de 0,3 s (`Stats.dm:36/45/67` --
/// `statify()` e `HealthSync()` na mesma volta, `sleep(3)`), e **todos os quatro perguntam a MESMA
/// coisa antes de curar**: `(!combatTag || fastRegen) && has_regen_energy()`.
///
///   1. **o MEMBRO se costura** -- `Injuries.dm:251-253`: `prob(10) || (prob(20) && vital)` ->
///      `health += 0.1 * regenerationrate`. E o unico canal que enxerga o membro individualmente,
///      e e ele que faz um braco (rate 2) sarar 16x mais rapido que um rabo (rate 0,125).
///   2. **o pingo passivo** -- `Injuries.dm:293-294`: `prob(75)` -> `SpreadHeal(0.1*passiveRegen)`.
///      E o canal que a RACA move: 0,044 no humano, 2,20 no Majin. Cinquenta vezes.
///   3. **o socorro raro** -- `Injuries.dm:290-292`: `prob(5) && prob(1)` -> `SpreadHeal(1)`.
///   4. **o da defesa** -- `master.dm:185-186`, dentro do `statify()`: acima de 25% de HP cura
///      `0.001 * Ephysdef * max(staminapercent*10, 1)`, abaixo cura `0.02` fixo. E o canal que
///      premia quem tem corpo duro, e e ele que faz um corpo em frangalhos sarar mais rapido
///      **por porcento** do que um so arranhado.
///
/// ============================ POR QUE AQUI NAO HA DADO, E POR QUE ISSO E FIEL ============================
/// O port roda a 30 Hz e o DM a 3,33 Hz. Rolar `prob(10)` noventa vezes por segundo daria uma cura
/// NOVE VEZES maior; rolar a cada 0,3 s exigiria um segundo relogio dentro do tique. O que este
/// arquivo usa e a **ESPERANCA por ciclo dividida pelo ciclo**, que e o mesmo numero que o dado do
/// DM entrega na media -- e a bancada mediu os dois lados: 105 s no DM contra 105 s aqui.
///
/// A UNICA coisa que continua no dado e a **escolha de QUAL membro volta** (`pick()` de
/// `Injuries.dm:303-309`): ali o sorteio nao e ruido, e a regra -- ele pode cair num membro so
/// machucado e DESPERDICAR o ponto, e e isso que faz um corpo destrocado demorar mais pra recuperar
/// o braco perdido do que um corpo intacto sem braco.
/// ====================================================================================================
/// </summary>
public static class Regeneracao
{
	/// <summary>
	/// O CICLO DO LACO DE STATS DO BYOND, em segundos: `sleep(3)` em `Stats.dm:67`.
	///
	/// Todo numero deste arquivo e POR CICLO no original, e e por ele que se divide pra virar taxa
	/// por segundo. Ele nao e "0,3 porque parece": e o mesmo 0,3 que `UltraInstinct.dm:62`
	/// (`UI_TICKSECS`), `UltraEgo.dm:80` e `1A Defines.dm:44/51` ja citam como a duracao da volta.
	/// </summary>
	public const double CicloDoDm = 0.3;

	// =====================================================================
	// OS PORTOES -- as mesmas duas perguntas dos quatro canais do DM
	// =====================================================================
	/// <summary>
	/// `has_regen_energy()` (`Injuries.dm:210-213`), literal: *"regen needs USABLE energy"*.
	///
	/// **1% E O PISO DE PROPOSITO** -- o comentario do DM conta o defeito que o gerou: um residuo
	/// abaixo de 1% (que o HUD mostra como 0%) mantinha o corpo "alimentado" e um jogador lendo
	/// 0% de vigor E 0% de nutricao na tela continuava regenerando.
	/// </summary>
	public static bool TemEnergia(Fighter f) =>
		f.stamina > f.maxstamina * 0.01
		|| f.CurrentNutrition > Nutricao.Tanque(f.Metabolism) * 0.01;

	/// <summary>
	/// A GUARDA QUE OS QUATRO CANAIS REPETEM: `(!combatTag || fastRegen) && has_regen_energy()`.
	///
	/// **E ESTA A LINHA QUE O DONO PEDIU.** "Ninguem cura em combate, exceto quem tem o eixo" cabe
	/// aqui inteira, e nao ha uma segunda copia dela em lugar nenhum do port.
	/// </summary>
	public static bool PodeCurar(in PerfilDeRegen r, Fighter f, bool emCombate) =>
		(!emCombate || r.CuraEmCombate) && TemEnergia(f);

	// =====================================================================
	// CANAL 1 -- O MEMBRO SE COSTURA
	// =====================================================================
	/// <summary>
	/// A CHANCE, POR CICLO, de o membro ganhar o pedaco dele -- `prob(10) || (prob(20) && vital)`
	/// (`Injuries.dm:252`).
	///
	/// O `||` do BYOND e curto-circuito, entao a chance combinada do vital NAO e 30%: e
	/// `1 - 0,9 x 0,8 = 28%`. Somar os dois daria 7% a mais de cura em cabeca, torso, abdomen,
	/// cerebro e orgaos -- justamente os membros que decidem nocaute e morte.
	/// </summary>
	public static double ChanceDoMembro(bool vital) => vital ? 1 - 0.9 * 0.8 : 0.10;

	/// <summary>
	/// QUANTO DE VIDA POR SEGUNDO este membro se costura sozinho.
	///
	/// `vital` no DM e `vital = 1`, que na tabela do port e tudo que NAO e <see cref="Vitalidade.Membro"/>
	/// (nucleo e interno) -- confere um a um com `mobparts.dm`: Head/Brain/Torso/Abdomen/Organs
	/// tem `vital = 1`; Arm/Hand/Leg/Foot/Tail/Reproductive_Organs tem `vital = 0`.
	/// </summary>
	public static double TaxaDoMembro(BodyPart p) =>
		p.TaxaDeRegen <= 0 ? 0
		: ChanceDoMembro(p.Papel != Vitalidade.Membro) * 0.1 * p.TaxaDeRegen / CicloDoDm;

	// =====================================================================
	// CANAIS 2, 3 e 4 -- o que o `SpreadHeal` espalha
	// =====================================================================
	/// <summary>
	/// QUANTO DE VIDA POR SEGUNDO cada membro **alcancavel e ferido** recebe dos tres canais de
	/// `SpreadHeal`.
	///
	/// **"ALCANCAVEL" E O `targetable` DO DM, E ELE NAO E O `isnested`.** `SpreadHeal` sem
	/// `FocusVitals` filtra por `C.targetable` (`Injuries.dm:112`), e so o Cerebro e os Orgaos tem
	/// `targetable = 0` (`mobparts.dm:157/210`) -- **mao e pe, que sao aninhados, RECEBEM**. No port
	/// isso e exatamente <see cref="Vitalidade.Interno"/>, e nao <see cref="BodyPart.Aninhado"/>:
	/// trocar um pelo outro deixaria mao e pe sem tres dos quatro canais de cura.
	///
	/// O cerebro e os orgaos nao ficam a ver navios -- eles tem o canal 1 (que nao filtra por
	/// `targetable`) e a propagacao da cura nao existe, exatamente como no original.
	/// </summary>
	public static double TaxaEspalhada(in PerfilDeRegen r, Fighter f)
	{
		double hp = f.HP;
		double porCiclo = 0;

		// CANAL 2 -- `Injuries.dm:293-294`. O `HP < 99.99` e do original e esta aqui, e nao no
		// chamador: o canal 1 (membro) NAO tem essa guarda, entao um corpo com o HP medio em 100 e um
		// dedo quebrado continua costurando o dedo. Era o que a guarda global do port apagava.
		if (r.Passiva > 0 && hp < 99.99) porCiclo += 0.75 * (0.1 * r.Passiva);

		// CANAL 3 -- `Injuries.dm:290-292`: `prob(5)` e DENTRO dele `prob(1)`, ou seja 0,05%.
		// Rende 0,017 de vida por minuto. Esta aqui porque e barato e porque some-lo e mais
		// honesto que decidir que "e pequeno demais pra portar".
		porCiclo += 0.05 * 0.01;

		// CANAL 4 -- `master.dm:185-186`, dentro do `statify()`. Os dois ramos sao exclusivos e o
		// corte e em 25 de HP. `HPregenbuff` e `THPregen` do DM sao 1 pra todo mundo neste port (nao
		// ha escritor pros dois), entao entram como 1 em vez de virarem dois campos mortos.
		if (hp > 25 && hp < 99.99) porCiclo += 0.001 * f.Ephysdef * Math.Max(f.staminapercent * 10, 1);
		else if (hp <= 25) porCiclo += 0.02;

		return porCiclo / CicloDoDm;
	}

	// =====================================================================
	// O BUFFER DE MEMBRO -- o Majin que refaz o braco sozinho
	// =====================================================================
	/// <summary>Quantos pontos o buffer precisa juntar pra devolver um membro (`Injuries.dm:299`).</summary>
	public const double PontosPorMembro = 25;

	/// <summary>
	/// PONTOS POR SEGUNDO que o corpo junta rumo a devolver um membro --
	/// `prob(5*activeRegen) || prob(2*DeathRegen)` (`Injuries.dm:295`).
	///
	/// Curto-circuito de novo, entao a combinada e `1 - (1-a)(1-b)` e nao `a+b`. No Majin
	/// (`activeRegen 5`, `DeathRegen 10`) sao `1 - 0,75 x 0,80 = 40%` por ciclo, ou 1,333 pontos por
	/// segundo: **25 pontos em 18,75 s**. E o *"nem e mt longo"* do dono, e o numero e do original.
	/// </summary>
	public static double PontosPorSegundo(in PerfilDeRegen r)
	{
		if (!r.MembroVolta) return 0;
		double a = Math.Clamp(0.05 * r.Ativa, 0, 1);
		double b = Math.Clamp(0.02 * r.RegenDeMorte, 0, 1);
		return (1 - (1 - a) * (1 - b)) / CicloDoDm;
	}

	/// <summary>Vigor que cada ponto de buffer cobra: `stamina -= 0.5 * (maxstamina/100)`.</summary>
	public const double VigorPorPonto = 0.005;

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>O que aconteceu neste tique -- so pra quem quiser anunciar.</summary>
	public readonly record struct Resultado(bool Curou, BodyPart? MembroDeVolta);

	/// <summary>
	/// UMA PASSADA DE CURA PASSIVA. **A UNICA.** Servidor e bancada entram por aqui, e nao ha uma
	/// segunda soma de vida passiva em lugar nenhum -- ver o cabecalho da classe.
	///
	/// ============================ ELA RODA COM O CORPO NO CHAO, E TEM QUE RODAR ============================
	/// O `RegenerarPassivo` do port saia em `pl.Ficha.KO`. Isso nao incomodava enquanto o nocaute
	/// era um cronometro de 12 s que terminava empurrando toda parte pra 30% de graca. Com o nocaute
	/// virando o COMA do DM -- que acaba quando o nucleo ferido sobe acima do limiar
	/// (`Injuries.dm:286-289`) -- um corpo que nao regenera caido **nunca acorda**: e a cura que
	/// decide a hora de levantar. O `HealthSync` do original nunca perguntou `KO`, e agora este
	/// tambem nao pergunta.
	/// =================================================================================================
	/// </summary>
	/// <param name="rng">
	/// So o `pick()` de qual membro volta usa isto (`Injuries.dm:303`). O resto e determinístico --
	/// ver o cabecalho da classe.
	/// </param>
	public static Resultado Tique(Body corpo, in PerfilDeRegen r, Fighter f, bool emCombate,
								  ref double buffer, double dt, Random rng)
	{
		if (f.dead || dt <= 0 || !PodeCurar(r, f, emCombate)) return default;

		bool curou = false;
		bool faltaAlgo = false;
		double espalhada = TaxaEspalhada(r, f) * dt;

		foreach (BodyPart p in corpo.Partes)
		{
			if (p.Decepado) { faltaAlgo = true; continue; }
			if (p.Vida >= p.VidaMax) continue;
			faltaAlgo = true;

			double ganho = TaxaDoMembro(p) * dt;
			// `targetable` -- ver `TaxaEspalhada`: cerebro e orgaos ficam so com o canal 1
			if (p.Papel != Vitalidade.Interno) ganho += espalhada;
			if (ganho <= 0) continue;

			p.Vida = Math.Min(p.Vida + ganho, p.VidaMax);
			curou = true;
		}

		// ---- e o membro que volta inteiro -------------------------------
		// A GUARDA E `passiveRegen &&` do DM (`Injuries.dm:293`): o buffer mora DENTRO daquele `if`,
		// entao quem tem `passiveRegen` zerado nao junta ponto nenhum. Ninguem tem hoje (o minimo do
		// genoma e 0,044), mas a linha e do original e nao custa nada.
		//
		// ============================ O `faltaAlgo` E UM DESVIO, E ELE E DELIBERADO ============================
		// No DM o buffer sobe **sem perguntar se ha o que regenerar**: um Majin inteiro, parado, sem
		// um arranhao, continua rodando `prob(40)` e continua pagando `stamina -= 0.5%` por acerto --
		// 0,67% do folego por segundo, a barra inteira em 150 s, pra sempre. E vazamento, e nao regra:
		// o `pick()` la embaixo sorteia entre membros com `health < maxhealth`, entao com o corpo
		// inteiro a lista e VAZIA e os 25 pontos sao gastos em nada.
		//
		// Aqui o buffer so anda quando existe **alguma coisa incompleta** -- que e exatamente a
		// condicao em que ele poderia fazer algo. O corpo arranhado continua pagando (e continua
		// desperdicando a vez num membro que nao e o decepado, que e o efeito que faz um Majin
		// destrocado demorar mais pra refazer o braco). O corpo INTEIRO para de sangrar folego.
		// ==================================================================================================
		BodyPart? voltou = null;
		if (r.Passiva > 0 && r.MembroVolta && faltaAlgo)
		{
			double pontos = PontosPorSegundo(r) * dt;
			if (pontos > 0)
			{
				buffer += pontos;
				// `stamina -= 0.5 * (maxstamina/100)` por ponto: refazer o corpo CANSA, e e o unico
				// preco que o Majin paga pela regeneracao passiva mais rapida do jogo.
				f.stamina = Math.Max(0, f.stamina - pontos * VigorPorPonto * f.maxstamina);

				while (buffer >= PontosPorMembro)
				{
					buffer -= PontosPorMembro;
					voltou = SortearMembroDoBuffer(corpo, rng) ?? voltou;
				}
			}
		}

		return new Resultado(curou, voltou);
	}

	/// <summary>
	/// O `pick()` de `Injuries.dm:301-309`, e ele e mais cruel do que parece: a lista e de TODO
	/// membro com `health &lt; maxhealth` -- machucado ou decepado --, sorteia-se UM, e **so se ele for
	/// o decepado e que alguma coisa acontece**. Cair num braco arranhado gasta os 25 pontos a toa.
	///
	/// Portar isso como "devolve o decepado se houver" seria acelerar o Majin destrocado justamente
	/// no caso em que o original o faz esperar. Devolve o membro que voltou, ou nulo.
	/// </summary>
	public static BodyPart? SortearMembroDoBuffer(Body corpo, Random rng)
	{
		var candidatos = new List<BodyPart>();
		foreach (BodyPart p in corpo.Partes)
		{
			// O ANINHADO COM A RAIZ CAIDA FICA DE FORA, e nao e liberalidade: `Body.Regenerar` o
			// RECUSA (`mobparts_logic.dm:130-131`) e ele volta junto com a raiz de qualquer jeito.
			// Deixa-lo na urna so faria a vez ser gasta em nada -- e com o braco decepado, a mao
			// decepada dobraria a espera do Majin sem que nada acontecesse na tela.
			if (p.Dono != null && corpo.Achar(p.Dono) is { Decepado: true }) continue;
			if (p.Decepado || p.Vida < p.VidaMax) candidatos.Add(p);
		}

		if (candidatos.Count == 0) return null;
		BodyPart escolhido = candidatos[rng.Next(candidatos.Count)];
		return corpo.Regenerar(escolhido) ? escolhido : null;
	}
}
