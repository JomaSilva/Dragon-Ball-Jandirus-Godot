using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// ============================ BANCADA DA CURA -- "a cura e privilegio" ============================
/// O dono, em uma frase: *"o TEMPO DE KO ta mt curto e FERIMENTOS estao REGENERANDO MT RAPIDO em
/// racas sem passiva de regeneracao... um MAJIN e a UNICA raca q PASSIVAMENTE regenera rapido MESMO
/// EM COMBATE... ate mesmo um NAMEK N TEM REGENERACAO PASSIVA EM COMBATE, somente a ATIVA"*.
///
/// ============================ POR QUE ELA EXISTE, E O QUE ELA NAO DEIXA ACONTECER ============================
/// **UM NUMERO DE CURA NAO SE OLHA NA TELA.** Ninguem senta cinco minutos vendo um braco sarar pra
/// dizer se esta certo -- e foi exatamente por isso que o port passou meses com um braco quebrado
/// saindo de "Quebrado" em **9 s** onde o original leva **105**, e ninguem viu. Era um numero so
/// (`CombatKnobs.RegenPorSegundo`), plano, e ele parecia razoavel.
///
/// Esta bancada roda o `Jandirus.Core` DE VERDADE -- o mesmo `CombatState.Tick` e o mesmo
/// `Regeneracao.Tique` que o servidor chama, a 30 Hz -- e faz as perguntas na forma em que o dono as
/// fez: quanto tempo, para QUEM, e em que situacao. Ela nao afirma "a constante e X": afirma
/// **quantos segundos** o jogador vai passar esperando, que e a unica coisa que ele sente.
///
/// A licao de `dbclimax-port-bancada-nasce-no-lugar-errado` esta valendo aqui: nenhuma familia
/// comeca ja no estado que quer medir. O corpo ENTRA em combate levando dano, o nocaute ENTRA pelo
/// funil (`Nocautear`), e o membro decepado ENTRA pelo `Decepar`.
/// ========================================================================================================
///
/// ============================ COMO CADA FAMILIA REPROVA -- MEDIDO, NAO SUPOSTO ============================
/// O placar limpo e **26 OK, 0 FALHAS**. Cada defeito abaixo foi posto no CODIGO DE PRODUCAO, um por
/// vez, com `dotnet build` no meio, e desfeito depois -- o placar e o que a bancada imprimiu. Repare
/// que **cada um derruba uma familia diferente**: e isso que diz que as seis nao sao a mesma pergunta
/// escrita seis vezes.
///
///  * **O RAMO DO NAMEKUSEIJIN APAGADO** (`PerfilDeRegen.De` sem o `return` proprio, ou seja o
///    Namekuseijin caindo na regra geral `Reg >= 5`) -> **22 OK, 4 FALHAS**: as duas do eixo, o
///    "nao cura com a briga de pe" e o "nao refaz sozinho" (ele passava a refazer o braco em 84,7 s).
///
///  * **A GUARDA DE COMBATE FORA** (`PodeCurar` sem o `!emCombate ||`) -> **21 OK, 5 FALHAS**: as
///    tres racas comuns passam a costurar o braco no meio da briga (+4,4 de vida em 30 s) **e o
///    nocaute despenca de 146 s pra 56 s** -- porque quem acorda o corpo e a cura. Duas familias
///    caindo pelo mesmo defeito, e essa e a unica cadeia do arquivo em que isso e certo.
///
///  * **A TAXA PLANA DE VOLTA** (o membro somando `CombatKnobs.RegenPorSegundo * dt` em vez dos
///    quatro canais) -> **23 OK, 3 FALHAS**, e ela reencena o defeito ORIGINAL com os numeros
///    originais: o braco sai de "Quebrado" em **9,0 s** e fica inteiro em **48,0 s**. A terceira
///    vermelha e o rabo, que passa a sarar no mesmo tempo do braco -- a prova de que a taxa e por
///    MEMBRO mede mesmo o `regenerationrate`.
///
///  * **`TetoDoNocaute = 12`** (o cronometro cravado de antes) -> **24 OK, 2 FALHAS**, e as duas
///    saem com 12,0 s redondos. Repare que a familia 6 (levantar) continua VERDE: o teto e o prazo,
///    e o `Un_KO` e a cura, e eles nao sao a mesma coisa.
///
///  * **O `Levantar` ANTIGO** (empurrar TODA parte pra 1,5x o limiar, e nao so os vitais) -> **25 OK,
///    1 FALHA**: o braco pula de 5 pra 30 ao levantar. E a familia mais barata do arquivo e a que
///    pega o defeito mais caro -- o nocaute como atalho de cura.
/// =======================================================================================================
/// </summary>
public static class CuraBench
{
	private static int _falhas;
	private const double Dt = 1.0 / 30;

	public static int Rodar(RaceCatalog? cat)
	{
		Console.WriteLine("=== A CURA E PRIVILEGIO -- prazos medidos no Core de verdade ===\n");

		if (cat == null)
		{
			Console.WriteLine("  (sem races.json: passe o caminho -- `cura Assets/Data/races.json`)");
			return 1;
		}

		OEixo(cat);
		OMembroQuebrado(cat);
		EmCombate(cat);
		OMembroPerdido(cat);
		ONocaute(cat);
		OLevantar(cat);

		Console.WriteLine(_falhas == 0
			? "\n=== TUDO VERDE ==="
			: $"\n=== {_falhas} PROVA(S) VERMELHA(S) ===");
		return _falhas == 0 ? 0 : 1;
	}

	// =====================================================================
	// 1. O EIXO -- quem tem o que, e de onde isso saiu
	// =====================================================================
	/// <summary>
	/// A TABELA QUE O DONO DESENHOU, lida do `races.json`. Se ela sair diferente do esperado, o
	/// problema esta no dado extraido do DM e nao no codigo -- e e por isso que ela e impressa
	/// inteira em vez de so afirmada.
	/// </summary>
	private static void OEixo(RaceCatalog cat)
	{
		Console.WriteLine("-- 1. O EIXO (`assign_regen()`): quem cura em combate, quem refaz membro --");
		Console.WriteLine("  raca            Reg   emCombate  passiva  ativa  DeathRegen  membroVolta");

		foreach (string nome in cat.Protos.Keys.OrderBy(x => x, StringComparer.Ordinal))
		{
			double reg = cat.Get(nome)!.MiscStat("Regeneration");
			PerfilDeRegen p = PerfilDeRegen.De(nome, reg);
			Console.WriteLine($"  {nome,-14} {reg,4:0}   {(p.CuraEmCombate ? "SIM" : "nao"),-9}"
							  + $"  {p.Passiva,6:0.000}  {p.Ativa,5:0.00}  {p.RegenDeMorte,9}  "
							  + (p.MembroVolta ? "SIM" : "nao"));
		}

		PerfilDeRegen majin = Perfil(cat, "Majin"), namek = Perfil(cat, "Namekian"), hum = Perfil(cat, "Human");

		Prova("o MAJIN cura em combate", majin.CuraEmCombate);
		Prova("o MAJIN refaz membro perdido sozinho", majin.MembroVolta);
		Prova("o NAMEKUSEIJIN **NAO** cura em combate (o `return` proprio do rework do DM)",
			  !namek.CuraEmCombate);
		Prova("...e o membro perdido dele NAO volta sozinho (`DeathRegen = 0`) -- so pela ATIVA",
			  !namek.MembroVolta);
		Prova("o HUMANO nao tem nem uma coisa nem outra", !hum.CuraEmCombate && !hum.MembroVolta);
		Prova("o FROST DEMON (Icer) NAO cura em combate, apesar do comentario do DM dizer que sim",
			  !Perfil(cat, "Icer").CuraEmCombate);
		Prova("o SHAPESHIFTER nao refaz membro (estava na lista cravada do port, e nao devia)",
			  !Perfil(cat, "Shapeshifter").MembroVolta);
		Prova("KAI, META, TSUJIN e YARDRAT refazem membro (estavam FORA da lista cravada)",
			  Perfil(cat, "Kai").MembroVolta && Perfil(cat, "Meta").MembroVolta
			  && Perfil(cat, "Tsujin").MembroVolta && Perfil(cat, "Yardrat").MembroVolta);
		Console.WriteLine();
	}

	// =====================================================================
	// 2. O MEMBRO QUEBRADO -- "deveria DEMORAR BASTANTE"
	// =====================================================================
	private static void OMembroQuebrado(RaceCatalog cat)
	{
		Console.WriteLine("-- 2. UM BRACO QUEBRADO, sozinho e FORA de combate --");
		Console.WriteLine("  raca            sair de \"Quebrado\" (>20%)   ficar INTEIRO (100%)");

		foreach (string raca in new[] { "Human", "Saiyan", "Namekian", "BioAndroid", "Majin" })
		{
			(Fighter f, CombatState c) = Corpo(cat, raca);
			BodyPart braco = c.Corpo.Achar("Braco esquerdo")!;

			// ENTRA PELO DANO, e nao escrevendo a vida: o `Ferir` e o funil, e e ele que decide o
			// piso do nao-letal. Escrever `braco.Vida = 5` mediria um estado que o jogo nao produz.
			c.Corpo.Ferir(braco, braco.VidaMax * 0.95, letal: true);
			c.SincronizarVida();

			double ateSarar = Correr(f, c, 1200, () => !braco.Quebrado);
			double ateCheio = Correr(f, c, 3600, () => braco.Vida >= braco.VidaMax - 1e-6);

			Console.WriteLine($"  {raca,-14} {Tempo(ateSarar),-27} {Tempo(ateCheio)}");

			if (raca == "Human")
			{
				// O ALVO E O DM MEDIDO: 105 s pra sair de "Quebrado", 598 s pra ficar inteiro.
				// A tolerancia e larga de proposito -- o canal 4 depende do `Ephysdef` do
				// personagem, que muda com a raca e com o BP. O que esta sendo afirmado e a ORDEM
				// DE GRANDEZA, e ela e o pedido do dono: o port entregava 9 s e 57 s.
				Prova($"o humano leva MINUTOS, e nao segundos, pra desquebrar o braco ({Tempo(ateSarar)})",
					  ateSarar > 60 && ateSarar < 220, $"{ateSarar:0} s");
				Prova($"...e leva quase DEZ MINUTOS pra ele ficar inteiro ({Tempo(ateCheio)})",
					  ateCheio > 350 && ateCheio < 900, $"{ateCheio:0} s");
			}
		}

		// ============================ O RABO, QUE E O CASO EXTREMO DO `regenerationrate` ============================
		// Ele prova que a taxa e POR MEMBRO e nao uma so -- mas **nao prova por dezesseis**, e a
		// primeira versao desta prova errou justamente ai. O rabo tem `regenerationrate` 0,125 contra
		// os 2 do braco, ou seja **1/16 do canal 1**; so que os canais 2, 3 e 4 (os `SpreadHeal`) sao
		// IGUAIS pra todo membro alcancavel, e sao eles que seguram o piso. A razao real fica perto de
		// 2x, e e assim no DM tambem. Afirmar "> 200 s" era afirmar um numero absoluto tirado do
		// nada; o que tem sentido e comparar com o braco medido logo acima.
		// =======================================================================================================
		(Fighter fs, CombatState cs) = Corpo(cat, "Saiyan");
		BodyPart? rabo = cs.Corpo.Achar("Rabo");
		BodyPart bracoS = cs.Corpo.Achar("Braco esquerdo")!;
		if (rabo != null)
		{
			cs.Corpo.Ferir(rabo, rabo.VidaMax * 0.95, letal: true);
			cs.Corpo.Ferir(bracoS, bracoS.VidaMax * 0.95, letal: true);
			cs.SincronizarVida();
			// OS DOIS SARAM AO MESMO TEMPO no mesmo corpo, entao o segundo `Correr` comeca DEPOIS do
			// primeiro ter passado -- e o rabo ja curou durante ele. Sem somar, o rabo apareceria
			// mais RAPIDO que o braco (medido: 88 s contra 108), que e o oposto do que acontece.
			double tBraco = Correr(fs, cs, 3600, () => !bracoS.Quebrado);
			double tRabo = tBraco + Correr(fs, cs, 3600, () => !rabo.Quebrado);
			Console.WriteLine($"  (no MESMO corpo: braco {Tempo(tBraco)} x RABO {Tempo(tRabo)}"
							  + " -- `regenerationrate` 2 contra 0,125)");
			Prova("o rabo sara bem mais devagar que o braco DO MESMO CORPO (a taxa e por MEMBRO)",
				  tRabo > tBraco * 1.5, $"rabo {tRabo:0} s x braco {tBraco:0} s");
		}
		Console.WriteLine();
	}

	// =====================================================================
	// 3. EM COMBATE -- so quem tem o eixo
	// =====================================================================
	private static void EmCombate(RaceCatalog cat)
	{
		Console.WriteLine("-- 3. O MESMO BRACO, com a TAG DE COMBATE no ar --");

		// ============================ A TABELA E DO DONO, E NAO DO CODIGO ============================
		// Isto era `bool esperado = PerfilDeRegen.De(raca, ...).CuraEmCombate` -- a bancada PERGUNTAVA
		// ao codigo sob teste o que ela deveria esperar dele. Medido: apagando o ramo do Namekuseijin
		// de `PerfilDeRegen.De` (o `return` proprio), esta familia continuava **VERDE**, porque os dois
		// lados da comparacao andavam juntos. E a lembranca do port inteiro numa linha: "as duas telas
		// concordam fica verde com as duas erradas igual".
		//
		// Agora a coluna da direita e a tabela que o dono desenhou, escrita a mao. Ela so muda se
		// ALGUEM decidir muda-la.
		// =========================================================================================
		foreach ((string raca, bool esperado) in new[]
				 { ("Human", false), ("Saiyan", false), ("Namekian", false), ("Majin", true), ("BioAndroid", true) })
		{
			(Fighter f, CombatState c) = Corpo(cat, raca);
			BodyPart braco = c.Corpo.Achar("Braco esquerdo")!;
			c.Corpo.Ferir(braco, braco.VidaMax * 0.95, letal: true);
			c.SincronizarVida();

			double antes = braco.Vida;
			// A TAG ENTRA PELO FUNIL e e RENOVADA todo segundo -- e o que uma briga de verdade faz.
			for (int i = 0; i < 30 * 30; i++)
			{
				if (i % 30 == 0) c.EntrarEmCombate();
				Passo(f, c);
			}
			double ganho = braco.Vida - antes;

			Console.WriteLine($"  {raca,-14} 30 s de briga -> {(ganho > 0.01 ? $"+{ganho:0.00} de vida" : "NADA")}"
							  + $"   (esperado: {(esperado ? "cura" : "nao cura")})");
			Prova($"...{raca}: {(esperado ? "CURA" : "nao cura")} com a briga de pe",
				  esperado ? ganho > 0.5 : ganho < 1e-6, $"{ganho:0.000}");
		}
		Console.WriteLine();
	}

	// =====================================================================
	// 4. O MEMBRO PERDIDO
	// =====================================================================
	private static void OMembroPerdido(RaceCatalog cat)
	{
		Console.WriteLine("-- 4. UM BRACO ARRANCADO: volta sozinho? --");

		foreach (string raca in new[] { "Human", "Saiyan", "Namekian", "BioAndroid", "Majin" })
		{
			(Fighter f, CombatState c) = Corpo(cat, raca);
			BodyPart braco = c.Corpo.Achar("Braco esquerdo")!;
			c.Corpo.Decepar(braco);   // pelo funil, que leva a mao junto
			c.SincronizarVida();

			double t = Correr(f, c, 600, () => !braco.Decepado);
			bool volta = !double.IsPositiveInfinity(t);
			Console.WriteLine($"  {raca,-14} {(volta ? Tempo(t) : "NUNCA (so pela maquina, ou pela ativa do Namek)")}");

			if (raca == "Majin")
				Prova($"o MAJIN refaz o braco em pouco tempo -- \"nem e mt longo\" ({Tempo(t)})",
					  volta && t < 60, $"{t:0.0} s");
			if (raca is "Human" or "Saiyan" or "Namekian")
				Prova($"...{raca} NAO refaz sozinho, nem em 10 minutos", !volta, $"{t:0.0} s");
		}

		// E O CUSTO DO MAJIN: refazer membro CANSA (`stamina -= 0.5 * maxstamina/100` por ponto)
		{
			(Fighter f, CombatState c) = Corpo(cat, "Majin");
			BodyPart braco = c.Corpo.Achar("Braco esquerdo")!;
			c.Corpo.Decepar(braco);
			c.SincronizarVida();
			double folego = f.stamina / f.maxstamina;
			Correr(f, c, 600, () => !braco.Decepado);
			double depois = f.stamina / f.maxstamina;
			Console.WriteLine($"  (e custa folego: {folego * 100:0.0}% -> {depois * 100:0.0}% do vigor)");
			Prova("refazer o membro COBRA vigor do Majin (nao e de graca)", depois < folego - 1e-9,
				  $"{folego:0.000} -> {depois:0.000}");
		}
		Console.WriteLine();
	}

	// =====================================================================
	// 5. O NOCAUTE
	// =====================================================================
	private static void ONocaute(RaceCatalog cat)
	{
		Console.WriteLine("-- 5. QUANTO TEMPO O CORPO FICA NO CHAO (nucleo quebrado + tag de 90 s) --");

		foreach (string raca in new[] { "Human", "Saiyan", "Namekian", "Majin" })
		{
			(Fighter f, CombatState c) = Corpo(cat, raca);
			BodyPart torso = c.Corpo.Achar("Torso")!;

			// O CORPO ENTRA NO NOCAUTE PELO CAMINHO DE VERDADE: leva dano letal no torso ate o
			// nucleo cruzar a linha, com a tag de combate erguida como um soco ergueria.
			c.EntrarEmCombate();
			c.Corpo.Ferir(torso, torso.VidaMax * 0.95, letal: true);
			c.SincronizarVida();
			// `porVital: true` PORQUE ACABOU DE PERGUNTAR AO CORPO -- e o par exato que o
			// `MeleeResolver` escreve (`if (d.Corpo.DeveNocautear()) d.Nocautear(..., porVital: true)`).
			// Sem ele o corpo espera o TETO inteiro (225 s) em vez de acordar pela cura, e as tres
			// linhas desta familia saem todas iguais -- que foi como esta prova ficou vermelha da
			// primeira vez. O `true` nao e enfeite: e ele que liga o coma a regeneracao.
			if (c.Corpo.DeveNocautear()) c.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);

			double t = Correr(f, c, 600, () => !f.KO);
			Console.WriteLine($"  {raca,-14} {Tempo(t)}");

			if (raca is "Human" or "Saiyan")
				Prova($"...{raca} passa MINUTOS no chao (eram 12 s cravados) -- {Tempo(t)}",
					  t > 90 && t < MeleeResolver.TetoDoNocaute, $"{t:0} s");
			if (raca == "Majin")
				Prova($"...o MAJIN levanta MUITO antes, porque cura no meio da briga -- {Tempo(t)}",
					  t < 60, $"{t:0} s");
		}

		Console.WriteLine($"  (o TETO, pra quem nao consegue subir: {MeleeResolver.TetoDoNocaute:0} s "
						  + "-- `KO.dm:116`, rand(2000,2500) decisegundos)");
		Console.WriteLine();
	}

	// =====================================================================
	// 6. LEVANTAR NAO E CURAR
	// =====================================================================
	private static void OLevantar(RaceCatalog cat)
	{
		Console.WriteLine("-- 6. LEVANTAR DO NOCAUTE NAO DESQUEBRA O BRACO --");

		(Fighter f, CombatState c) = Corpo(cat, "Human");
		BodyPart braco = c.Corpo.Achar("Braco esquerdo")!;
		BodyPart torso = c.Corpo.Achar("Torso")!;
		c.Corpo.Ferir(braco, braco.VidaMax * 0.95, letal: true);
		c.Corpo.Ferir(torso, torso.VidaMax * 0.95, letal: true);
		c.SincronizarVida();
		c.Nocautear(MeleeResolver.TetoDoNocaute);

		double bracoAntes = braco.Vida;
		c.Levantar();

		Console.WriteLine($"  torso {torso.Vida:0.0}/100 (o `SpreadHeal(25,1,1)` do `Un_KO` pega os VITAIS)");
		Console.WriteLine($"  braco {braco.Vida:0.0}/100 (nao e vital: fica como estava)");

		Prova("o TORSO ganha os 25 do `Un_KO` e sai da linha de nocaute",
			  torso.Vida >= 25 && !c.Corpo.DeveNocautear(), $"{torso.Vida:0.0}");
		Prova("o BRACO continua QUEBRADO -- nocaute deixou de ser botao de cura",
			  braco.Quebrado && Math.Abs(braco.Vida - bracoAntes) < 1e-9, $"{braco.Vida:0.0}");
		Console.WriteLine();
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	private static PerfilDeRegen Perfil(RaceCatalog cat, string raca) =>
		PerfilDeRegen.De(raca, cat.Get(raca)?.MiscStat("Regeneration") ?? 1);

	/// <summary>
	/// UM CORPO NASCIDO PELO CAMINHO DE PRODUCAO: `Birth.Nascer` + o eixo tirado do proto, que e o
	/// que `GameServer.EixoDeRegen` faz. Montar a ficha na mao mediria outro personagem.
	/// </summary>
	private static (Fighter, CombatState) Corpo(RaceCatalog cat, string raca)
	{
		Fighter f = Birth.Nascer(cat, raca, "", new Random(1234), raca);
		f.Tick();
		f.Ki = f.MaxKi;
		f.stamina = f.maxstamina;
		f.Tick();
		return (f, new CombatState(f, raca == "Saiyan", Perfil(cat, raca)));
	}

	/// <summary>Um tique de servidor, na ordem do `TickCombate`: cronometros, depois cura.</summary>
	private static void Passo(Fighter f, CombatState c)
	{
		c.Tick(Dt);
		double buffer = c.BufferDeMembro;
		Regeneracao.Tique(c.Corpo, c.Corpo.Regen, f, c.EmCombate > 0, ref buffer, Dt, _rng);
		c.BufferDeMembro = buffer;
		c.SincronizarVida();
	}

	private static readonly Random _rng = new(4321);

	/// <summary>
	/// Roda ate a condicao virar verdadeira, e devolve os SEGUNDOS. Infinito quando nao virou --
	/// e o infinito e uma resposta: "este membro nunca volta sozinho" e o que o dono pediu pra
	/// raca comum.
	///
	/// A FICHA ANDA A 5 Hz, como no servidor (`TickCombate` chama `Ficha.Tick` mais raro que o
	/// tique): o `staminapercent` e o `Ephysdef` alimentam o canal 4 da cura, entao congelar a
	/// ficha mediria outra taxa.
	/// </summary>
	private static double Correr(Fighter f, CombatState c, double limiteSegundos, Func<bool> ate)
	{
		int n = (int)(limiteSegundos / Dt);
		for (int i = 0; i < n; i++)
		{
			if (i % 6 == 0) f.Tick();
			Passo(f, c);
			if (ate()) return (i + 1) * Dt;
		}
		return double.PositiveInfinity;
	}

	private static string Tempo(double s) =>
		double.IsPositiveInfinity(s) ? "nunca"
		: s < 90 ? $"{s:0.0} s"
		: $"{s:0} s ({s / 60:0.0} min)";

	private static void Prova(string oQue, bool ok, string detalhe = "")
	{
		if (!ok) _falhas++;
		Console.WriteLine($"    [{(ok ? "ok" : "FALHOU")}] {oQue}"
						  + (ok || detalhe.Length == 0 ? "" : $"  -- {detalhe}"));
	}
}
