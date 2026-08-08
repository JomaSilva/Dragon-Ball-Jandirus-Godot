using Jandirus.Core.Forms;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DAS DISCIPLINAS DIVINAS (`disciplinas`).
///
/// ============================ O QUE ELA TEM QUE PROVAR ============================
/// O sistema inteiro se apoia em UMA ideia que e facil de escrever e facil de quebrar: **as duas
/// energias fazem coisas diferentes**. A REAL destrava e nunca desce; a ATUAL escala os bonus,
/// drena com o uso e NAO volta em combate.
///
/// Um teste que so confere numeros nao pega isso. O que pega e exercitar o CICLO: ligar, drenar
/// ate o chao, ver o bonus sumir junto, ficar parado e ver voltar -- e conferir que ele para no
/// teto da maestria REAL. E confirmar o avesso: que a energia NAO volta lutando.
///
/// Um dreno que nunca esvazia e indistinguivel de dreno nenhum, e uma regeneracao sem teto e
/// indistinguivel de nao ter maestria.
/// =================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- disciplinas
/// </summary>
public static class DisciplinasBench
{
	private static int _ok, _falhou;

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  OK   {nome}"); }
		else { _falhou++; Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	private static void Perto(string nome, double obtido, double esperado, double tol = 1e-6) =>
		Checa(nome, Math.Abs(obtido - esperado) <= tol, $"obtido {obtido:0.####}, esperado {esperado:0.####}");

	public static int Rodar()
	{
		Console.WriteLine("=== BANCADA DAS DISCIPLINAS DIVINAS ===\n");

		Faixas();
		Numeros();
		Ciclo();
		UnboundEgo();
		PortaDaMorte();
		Exclusao();
		AMaestriaEDaSkill();

		Console.WriteLine($"\n=== {_ok} OK, {_falhou} FALHA ===");
		return _falhou == 0 ? 0 : 1;
	}

	// =====================================================================
	// 1. AS CINCO FAIXAS
	// =====================================================================
	private static void Faixas()
	{
		Console.WriteLine("[1] AS CINCO FAIXAS DE CADA DISCIPLINA");

		foreach (DisciplinaDef def in Disciplinas.Todas)
		{
			Checa($"{def.Nome}: cinco faixas", def.Degraus.Length == 5, $"{def.Degraus.Length}");

			// AS FAIXAS SAO 0/20/40/60/80 nas duas -- e do DM (`UI_UNLOCK_*` / `UE_UNLOCK_*`).
			double[] esperadas = [0, 20, 40, 60, 80];
			double[] obtidas = [.. def.Degraus.Select(d => d.Pct).Order()];
			Checa($"{def.Nome}: faixas em 0/20/40/60/80",
				  obtidas.SequenceEqual(esperadas), string.Join("/", obtidas));

			// DUAS DELAS SAO FORMAS, e as formas tem que existir no catalogo -- um id errado aqui
			// daria um degrau que anuncia uma forma que ninguem consegue assumir.
			var comForma = def.Degraus.Where(d => d.Forma.Length > 0).ToList();
			Checa($"{def.Nome}: duas faixas sao formas", comForma.Count == 2, $"{comForma.Count}");
			foreach (Degrau d in comForma)
				Checa($"{def.Nome}: a forma '{d.Forma}' existe no catalogo", Catalogo.Def(d.Forma) != null);

			// UMA ATIVA (a de 80%) e o resto passiva.
			Checa($"{def.Nome}: exatamente uma faixa ATIVA",
				  def.Degraus.Count(d => d.Ativa) == 1);
			Checa($"{def.Nome}: a ativa e a de 80%",
				  def.Degraus.First(d => d.Ativa).Pct == 80);

			// TODA FAIXA TEM TEXTO. Uma passiva sem descricao e uma passiva que ninguem sabe que tem.
			foreach (Degrau d in def.Degraus)
				if (d.Desc.Length < 20) Checa($"{def.Nome}/{d.Nome}: tem descricao util", false, d.Desc);
		}

		// A ESCADA E MONOTONICA: `Maior` e `Proximo` tem que concordar em toda maestria.
		DisciplinaDef ui = Disciplinas.UltraInstinct;
		Checa("a 0% o maior degrau e o toggle", ui.Maior(0)?.Pct == 0);
		Checa("a 0% o proximo e o de 20%", ui.Proximo(0)?.Pct == 20);
		Checa("a 59% o maior e o de 40%", ui.Maior(59)?.Pct == 40, $"{ui.Maior(59)?.Pct}");
		Checa("a 60% o maior e o de 60%", ui.Maior(60)?.Pct == 60);
		Checa("a 100% nao ha proximo", ui.Proximo(100) == null);
		Checa("a 100% tudo esta destravado",
			  ui.Degraus.All(d => ui.Destravou(100, d.Nome)));
		Checa("a 0% SO o toggle esta destravado",
			  ui.Degraus.Count(d => ui.Destravou(0, d.Nome)) == 1);
		Console.WriteLine();
	}

	// =====================================================================
	// 2. OS NUMEROS LITERAIS DO BYOND
	// =====================================================================
	private static void Numeros()
	{
		Console.WriteLine("[2] NUMEROS LITERAIS (UltraInstinct.dm / UltraEgo.dm)");

		// ULTRA INSTINCT: `UI_EVADE_MELEE_PCT 0.45` / `UI_EVADE_BLAST_PCT 0.30`, sobre a ATUAL.
		Perto("esquiva melee com 100% de precisao = 45%", Disciplinas.ChanceDeEsquiva(100, false), 45);
		Perto("esquiva de ki com 100% = 30%", Disciplinas.ChanceDeEsquiva(100, true), 30);
		Perto("esquiva melee com 50% = 22,5%", Disciplinas.ChanceDeEsquiva(50, false), 22.5);
		// E COM ZERO DE PRECISAO A PASSIVA NAO FAZ NADA -- e o ponto do cansaco.
		Perto("esquiva com precisao zerada = 0%", Disciplinas.ChanceDeEsquiva(0, false), 0);

		// ULTRA EGO: cada efeito e "base + porEnergia x energia". Com 100 batem os numeros do DM.
		Perto("recuo com energia cheia = 5%", Disciplinas.Recuo(100), 5);
		Perto("recuo com energia zerada = 1%", Disciplinas.Recuo(0), 1);
		Perto("ki devorado com energia cheia = 35%", Disciplinas.KiDevorado(100), 35);
		Perto("ki devorado com energia zerada = 20%", Disciplinas.KiDevorado(0), 20);
		Perto("reducao de dano com energia cheia = 25%", Disciplinas.ReducaoDeDano(100), 25);
		Perto("reducao de dano com energia zerada = 15%", Disciplinas.ReducaoDeDano(0), 15);
		Perto("explosao com energia cheia = 100% do golpe", Disciplinas.Explosao(100), 100);
		Perto("explosao com energia zerada = 50% do golpe", Disciplinas.Explosao(0), 50);

		// A AURA NUNCA ZERA O DANO. Se a reducao passasse de 100 o golpe viraria cura.
		Checa("a reducao de dano nunca chega a 100%", Disciplinas.ReducaoDeDano(100) < 100);

		// A MAESTRIA REAL: 0,04/s = ~42 min de luta continua pra 100%.
		double minutos = 100 / Disciplinas.UltraInstinct.RealPorSegundo / 60;
		Checa($"maestria REAL leva ~{minutos:0} min de combate continuo",
			  minutos is > 35 and < 45, $"{minutos:0.0}");

		// O GATILHO ENSINA MUITO MAIS RAPIDO que o tempo: esquivar vale 5 segundos de luta.
		Checa("uma esquiva vale mais que um segundo de luta",
			  Disciplinas.UltraInstinct.RealPorGatilho > Disciplinas.UltraInstinct.RealPorSegundo);
		// E o proc da aura vale METADE do da esquiva -- e do DM (`UE_REAL_PER_PROC 0.1`).
		Perto("o proc da aura vale metade da esquiva",
			  Disciplinas.PoderDaDestruicao.RealPorGatilho, Disciplinas.UltraInstinct.RealPorGatilho / 2);

		Perto("aprender pede 70% de maestria divina", Disciplinas.GodKiParaAprender, 70);
		Console.WriteLine();
	}

	// =====================================================================
	// 3. O CICLO DAS DUAS ENERGIAS -- o coracao do sistema
	// =====================================================================
	private static void Ciclo()
	{
		Console.WriteLine("[3] O CICLO: drena com uso, NAO volta em combate, teto na REAL");

		DisciplinaDef def = Disciplinas.UltraInstinct;
		var e = new EstadoDeDisciplina { Aprendida = true, Real = 100, Atual = 100, Ligada = true };

		// 1. LIGADA, FORA DE FORMA: cai 0,5 por segundo.
		for (int i = 0; i < 10; i++) e.TickAtual(def, 1, emForma: false, emCombate: true);
		Perto("10s com o toggle ligado: 100 -> 95", e.Atual, 95);

		// 2. E A ESQUIVA CAI JUNTO -- o bonus segue a energia, e nao o contrario.
		Perto("a esquiva acompanhou a queda", Disciplinas.ChanceDeEsquiva(e.Atual, false), 95 * 0.45);

		// 3. NA FORMA drena MENOS (0,35/s) -- transformar e mais barato que so ficar ligado.
		double antes = e.Atual;
		e.TickAtual(def, 1, emForma: true, emCombate: true);
		Perto("1s transformado cobra 0,35 (menos que o toggle)", antes - e.Atual, 0.35);

		// 4. EM COMBATE NAO REGENERA, nem com tudo desligado. E a regra que faz o cansaco existir.
		e.Ligada = false;
		double naLuta = e.Atual;
		for (int i = 0; i < 30; i++) e.TickAtual(def, 1, emForma: false, emCombate: true);
		Perto("30s parado EM COMBATE nao devolve nada", e.Atual, naLuta);

		// 5. FORA DE COMBATE volta a 0,25/s.
		for (int i = 0; i < 10; i++) e.TickAtual(def, 1, emForma: false, emCombate: false);
		Perto("10s fora de combate devolve 2,5", e.Atual, naLuta + 2.5);

		// 6. O TETO E A MAESTRIA REAL. Este e o teste que prova que o teto DISPARA -- um teto que
		//    nunca e alcancado nao se distingue de teto nenhum.
		var meio = new EstadoDeDisciplina { Aprendida = true, Real = 40, Atual = 0 };
		for (int i = 0; i < 1000; i++) meio.TickAtual(def, 1, emForma: false, emCombate: false);
		Perto("com 40% de maestria a precisao para em 40", meio.Atual, 40);
		Checa("...e o teto DISPAROU (nao chegou a 100)", meio.Atual < 100);

		// 7. A ENERGIA NUNCA FICA NEGATIVA por mais que se drene.
		var seca = new EstadoDeDisciplina { Aprendida = true, Real = 100, Atual = 1, Ligada = true };
		for (int i = 0; i < 100; i++) seca.TickAtual(def, 1, emForma: false, emCombate: true);
		Perto("drenar alem do fim para em zero", seca.Atual, 0);

		// 8. A MAESTRIA REAL SO SOBE, e anuncia a faixa cruzada.
		var m = new EstadoDeDisciplina { Aprendida = true };
		Checa("cruzar 20% anuncia o Sign", m.SubirReal(def, 20)?.Nome == "Ultra Instinct -Sign-",
			  m.Real.ToString("0.#"));
		Checa("subir dentro da mesma faixa nao anuncia nada", m.SubirReal(def, 5) == null);
		Checa("cruzar 40% anuncia o Accelerated Battle",
			  m.SubirReal(def, 20)?.Nome == "Accelerated Battle");
		m.SubirReal(def, 1000);
		Perto("a maestria REAL para em 100", m.Real, 100);
		Checa("a REAL nunca desce sozinha (o tick so mexe na ATUAL)", m.Real == 100);
		Console.WriteLine();
	}

	// =====================================================================
	// 4. O UNBOUND EGO
	// =====================================================================
	private static void UnboundEgo()
	{
		Console.WriteLine("[4] UNBOUND EGO (o BP que vem de estar quebrado)");

		bool[] inteiros = [false, false, false, false, false, false];

		// CORPO INTEIRO: nenhum bonus. Este e o caso que separa "a passiva funciona" de "a passiva
		// esta sempre ligada".
		double[] cheios = [1, 1, 1, 1, 1, 1];
		Perto("corpo intacto nao rende bonus", Disciplinas.UnboundEgo(cheios, inteiros, out _), 1);

		// MEIO CORPO: 6 membros x 5% x 0,5 = 15%.
		double[] meio = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5];
		Perto("todos a meia vida = +15%", Disciplinas.UnboundEgo(meio, inteiros, out _), 1.15);

		// NO PICO (perdeu 90%+): 6 x 5% x 0,9 = 27%, mais os 20% de "todos no pico" = 47%.
		double[] pico = [0.1, 0.1, 0.1, 0.1, 0.1, 0.1];
		double noPico = Disciplinas.UnboundEgo(pico, inteiros, out bool todos);
		Checa("todos no pico e detectado", todos);
		Perto("todos no pico = +47%", noPico, 1.47);

		// UM MEMBRO QUEBRADO PERDE O BONUS DELE, e derruba o "todos no pico".
		bool[] umQuebrado = [true, false, false, false, false, false];
		double comQuebra = Disciplinas.UnboundEgo(pico, umQuebrado, out bool todos2);
		Checa("o membro quebrado sai da conta", comQuebra < noPico, $"{comQuebra:0.###} vs {noPico:0.###}");
		Checa("...mas os outros cinco continuam no pico", todos2);
		Perto("cinco no pico = +42,5%", comQuebra, 1 + (5 * 5 * 0.9 + 20) / 100.0);

		// E QUEBRAR TUDO ZERA: o Ultra Ego premia quem esta apanhando e DE PE.
		bool[] tudoQuebrado = [true, true, true, true, true, true];
		Perto("corpo todo quebrado nao rende nada", Disciplinas.UnboundEgo(pico, tudoQuebrado, out bool t3), 1);
		Checa("...e 'todos no pico' nao dispara com o corpo todo quebrado", !t3);
		Console.WriteLine();
	}

	// =====================================================================
	// 5. A PORTA DA MORTE
	// =====================================================================
	/// <summary>
	/// A MORTE TEM UMA PORTA SO, E O SEGURO ESTA DENTRO DELA.
	///
	/// ============================ O QUE ESTA SECAO PROTEGE ============================
	/// Antes deste rework havia SETE lugares chamando `Morrer()`. Um seguro contra a morte escrito
	/// em um deles funcionaria contra soco e nao contra Kamehameha, e o jogador nao teria como
	/// saber por que. O gancho `CombatState.NegarMorte` resolveu isso -- mas so continua resolvido
	/// enquanto TODO chamador olhar o retorno.
	///
	/// Por isso esta secao faz duas coisas diferentes: exercita o gancho E **le o codigo-fonte**
	/// procurando `Morrer()` cujo retorno foi jogado fora. A segunda e a que sobrevive a mim: quem
	/// acrescentar a oitava porta daqui a seis meses vai ver a bancada falhar no mesmo dia.
	/// ==============================================================================
	/// </summary>
	private static void PortaDaMorte()
	{
		Console.WriteLine("[5] A PORTA DA MORTE (o seguro vale pra TODA morte)");

		var f = new Jandirus.Core.Stats.Fighter();
		var c = new Jandirus.Core.Combat.CombatState(f);

		// 1. SEM GANCHO, morre normal.
		Checa("sem gancho, Morrer() mata e devolve true", c.Morrer() && f.dead);

		// 2. MORRER DE NOVO nao conta como morte nova -- senao o prazo de renascer e o Zenkai
		//    seriam pagos duas vezes pelo mesmo cadaver.
		Checa("morrer duas vezes devolve false na segunda", !c.Morrer());

		// 3. COM GANCHO QUE NEGA: nao morre, e devolve false.
		var f2 = new Jandirus.Core.Stats.Fighter();
		int chamadas = 0;
		var c2 = new Jandirus.Core.Combat.CombatState(f2) { NegarMorte = _ => { chamadas++; return true; } };
		Checa("com o gancho negando, Morrer() devolve false", !c2.Morrer());
		Checa("...e o corpo NAO morreu", !f2.dead);
		Checa("...e o gancho foi consultado", chamadas == 1, $"{chamadas}");

		// 4. O ADMIN PASSA POR CIMA. Uma ferramenta que uma mecanica de jogador bloqueia deixa de
		//    ser ferramenta.
		Checa("ignorarSeguro: true atravessa o gancho", c2.Morrer(ignorarSeguro: true) && f2.dead);
		Checa("...sem consultar o gancho de novo", chamadas == 1, $"{chamadas}");

		// 5. O RESOLVEDOR DE SOCOS NAO PODE DIZER QUE MATOU quando a morte foi negada. Este e o
		//    defeito concreto: `r.Morreu` alimenta o prazo de renascer e o Zenkai, entao um `true`
		//    aqui daria "voce morreu" pra quem continua de pe.
		var atacante = new Jandirus.Core.Stats.Fighter { BP = 1e9 };
		atacante.Statify();
		var ca = new Jandirus.Core.Combat.CombatState(atacante) { Letal = true };

		var vitima = new Jandirus.Core.Stats.Fighter { BP = 1 };
		vitima.Statify();
		bool negou = true;
		var cv = new Jandirus.Core.Combat.CombatState(vitima) { NegarMorte = _ => negou };

		var rng = new Random(1);
		bool viuMorte = false, sobreviveu = false;
		for (int i = 0; i < 200 && !viuMorte; i++)
		{
			Jandirus.Core.Combat.GolpeResultado r =
				Jandirus.Core.Combat.MeleeResolver.Resolver(ca, cv, 0, rng);
			if (r.Morreu) viuMorte = true;
			if (cv.Corpo.DeveMorrer() && !vitima.dead) sobreviveu = true;
		}
		Checa("o soco chegou a ponto de matar (o teste exercita mesmo)", sobreviveu,
			  "o corpo nunca chegou perto da morte -- o teste nao provou nada");
		Checa("com a morte negada, o resolvedor NAO reporta morte", !viuMorte);

		// 6. E COM O GANCHO SOLTO ele reporta -- senao o teste acima passaria num jogo onde
		//    ninguem morre nunca.
		negou = false;
		bool morreuAgora = false;
		for (int i = 0; i < 200 && !morreuAgora; i++)
		{
			Jandirus.Core.Combat.GolpeResultado r =
				Jandirus.Core.Combat.MeleeResolver.Resolver(ca, cv, 0, rng);
			if (r.Morreu) morreuAgora = true;
		}
		Checa("sem negar, o resolvedor reporta a morte normalmente", morreuAgora);

		// 7. A EXPLOSAO VEM DO GOLPE QUE QUASE MATOU, e escala com a energia.
		Perto("explosao: 1000 de golpe com energia zerada = 500", 1000 * Disciplinas.Explosao(0) / 100, 500);
		Perto("explosao: 1000 de golpe com energia cheia = 1000", 1000 * Disciplinas.Explosao(100) / 100, 1000);

		// 8. E A VARREDURA DO CODIGO-FONTE. Ver o cabecalho desta secao.
		VarrerChamadasDeMorrer();
		Console.WriteLine();
	}

	/// <summary>
	/// LE OS FONTES procurando `Morrer()` cujo retorno foi ignorado.
	///
	/// Uma chamada com o retorno solto e uma porta que mata mesmo quando o seguro negou. E o unico
	/// jeito de garantir isso pro codigo que ainda nao existe.
	/// </summary>
	private static void VarrerChamadasDeMorrer()
	{
		string raiz = Directory.GetCurrentDirectory();
		var suspeitas = new List<string>();
		int total = 0;

		foreach (string dir in new[] { "Core", "Server" })
		{
			string caminho = Path.Combine(raiz, dir);
			if (!Directory.Exists(caminho)) continue;

			foreach (string arq in Directory.EnumerateFiles(caminho, "*.cs", SearchOption.AllDirectories))
			{
				string[] linhas = File.ReadAllLines(arq);
				for (int i = 0; i < linhas.Length; i++)
				{
					string l = linhas[i].Trim();
					if (!l.Contains(".Morrer(")) continue;
					if (l.StartsWith("//") || l.StartsWith("///")) continue;   // comentario
					if (l.Contains("public bool Morrer")) continue;            // a propria definicao
					total++;

					// O RETORNO FOI USADO se a chamada esta dentro de uma condicao, atribuida, ou
					// se e a passagem explicita de admin. Sobra: `x.Morrer();` solto.
					bool usado = l.Contains("if (") || l.Contains("&& ") || l.Contains("|| ")
							  || l.Contains("=") || l.Contains("ignorarSeguro")
							  || l.Contains("return");
					if (!usado) suspeitas.Add($"{Path.GetFileName(arq)}:{i + 1}  {l}");
				}
			}
		}

		Checa($"as {total} chamadas de Morrer() olham o retorno", suspeitas.Count == 0,
			  string.Join(" | ", suspeitas));
		if (total < 5)
			Checa("a varredura achou as chamadas (o teste nao rodou no vazio)", false,
				  $"so {total} chamadas -- rodou da pasta errada?");
	}

	// =====================================================================
	// 6. AS DUAS SE EXCLUEM
	// =====================================================================
	private static void Exclusao()
	{
		Console.WriteLine("[6] AS DUAS DISCIPLINAS SE EXCLUEM");

		Checa("a oposta do Ultra Instinto e o Poder da Destruicao",
			  Disciplinas.Oposta(TipoDeDisciplina.UltraInstinct) == TipoDeDisciplina.PoderDaDestruicao);
		Checa("e vice-versa",
			  Disciplinas.Oposta(TipoDeDisciplina.PoderDaDestruicao) == TipoDeDisciplina.UltraInstinct);

		// E AS FORMAS SEGUEM A MESMA REGRA -- o catalogo ja fechava as linhas; aqui e a conferencia
		// de que as duas metades do sistema concordam.
		var comUi = new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal",
									   GodKi: 100, ProficienciaUi: 50);
		HashSet<LinhaDeForma> l1 = Catalogo.LinhasAbertas(comUi);
		Checa("quem trilhou o Ultra Instinto nao ve as formas de Ultra Ego",
			  l1.Contains(LinhaDeForma.UltraInstinct) && !l1.Contains(LinhaDeForma.UltraEgo));

		var comUe = new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal",
									   GodKi: 100, EnergiaUe: 50);
		HashSet<LinhaDeForma> l2 = Catalogo.LinhasAbertas(comUe);
		Checa("quem trilhou o Poder da Destruicao nao ve as formas de Ultra Instinto",
			  l2.Contains(LinhaDeForma.UltraEgo) && !l2.Contains(LinhaDeForma.UltraInstinct));

		// A MAESTRIA REAL DA FAIXA E A MESMA QUE O CATALOGO COBRA. Se divergissem, a faixa
		// anunciaria "Sign destravado" e a forma continuaria recusando -- o pior tipo de defeito,
		// porque o jogador ve as duas coisas e nao tem como saber qual esta certa.
		Perto("o Sign pede a mesma maestria na faixa e no catalogo",
			  Catalogo.Def("ui_sign")!.PedeProficienciaUi, 1);
		Checa("...e a faixa do Sign e 20%", Disciplinas.UiSign == 20);
		Checa("o Perfected pede 100 de proficiencia no catalogo",
			  Catalogo.Def("ui_perfected")!.PedeProficienciaUi == 100);

		// AS DUAS FORMAS DE CADA LINHA PEDEM O GOD KI DE APRENDER.
		foreach (string id in new[] { "ui_sign", "ui_perfected", "destroyer", "ultra_ego" })
			Checa($"{id} pede {Disciplinas.GodKiParaAprender:0}% de ki divino",
				  Math.Abs(Catalogo.Def(id)!.PedeGodKi - Disciplinas.GodKiParaAprender) < 1e-9);

		// A MAESTRIA DESSAS FORMAS -- que e da SKILL e nao delas -- tem secao propria: ver
		// <see cref="AMaestriaEDaSkill"/>.
		Console.WriteLine();
	}

	// =====================================================================
	// 7. A MAESTRIA DAS QUATRO FORMAS DIVINAS E DA **SKILL**
	// =====================================================================
	/// <summary>
	/// ============================ A FRASE DO DONO, E ELA VALE PRAS QUATRO ============================
	/// *"o ultra instinct e perfected ultra instinct estao ganhando maestria como transformacoes, ambas
	/// essas formas sao da skill de ultra instinct, entao usar elas aumenta a maestria na SKILL e nao
	/// nelas em si"* -- e depois *"sim pode mexer no ultra ego e destroyer tb e a mesma coisa"*.
	///
	/// ============================ POR QUE ESTA SECAO NAO CONFERE "SUBIU" ============================
	/// Quem sobe alguma coisa e o servidor, e o tique dele nao roda aqui: **a metade viva desta regra
	/// mora no `--formasteste`** (`GameServer.DisciplinaFormaTeste.cs`), que tiqua o corpo de verdade e
	/// mede as duas barras no MESMO tique. O que esta secao cobre e a metade que nao precisa de mundo, e
	/// que e onde a regra pode sumir sem ninguem ver:
	///
	///   1. a DERIVACAO -- uma so, por PERTENCER A DISCIPLINA, e cada forma na disciplina CERTA;
	///   2. o LIVRO, que recusa as quatro seja quem for que escreva;
	///   3. o SAVE de quem ja jogou, que descarta FALANDO;
	///   4. e a varredura dos FONTES, que e a unica linha capaz de reprovar o codigo que ainda nao
	///      existe -- o segundo ramo que alguem escreveria daqui a seis meses.
	/// ==========================================================================================
	/// </summary>
	private static void AMaestriaEDaSkill()
	{
		Console.WriteLine("[7] A MAESTRIA DAS QUATRO FORMAS DIVINAS E DA SKILL");

		// --- (a) A PORTA DO CATALOGO E A FAIXA QUE CONCEDE A FORMA CONCORDAM ---
		// A CHECAGEM QUE IMPORTA DAQUI A UM ANO: ela cruza as duas metades do dado -- a porta do
		// catalogo (`PedeProficienciaUi`/`PedeEnergiaUe`) e a faixa que concede a forma (`Degrau.Forma`).
		// Um degrau novo de disciplina que entrasse so no catalogo passaria pela porta e voltaria a
		// acumular maestria MORTA, calada: a regra sumiria sem ninguem ver, que e exatamente o defeito
		// que este rework removeu. Aqui ele reprova em vez de sumir.
		foreach (FormaDef d in Catalogo.Todas)
		{
			bool porta = d.PedeProficienciaUi > 0 || d.PedeEnergiaUe > 0;
			bool reclamada = Disciplinas.DaForma(d.Id) != null;
			if (porta || reclamada)
				Checa($"{d.Id}: a porta de disciplina e a faixa que a concede concordam",
					  porta == reclamada, $"porta {porta}, faixa {reclamada}");
		}

		// --- (b) A DERIVACAO E UMA SO, E CADA FORMA CAI NA DISCIPLINA CERTA ---
		(int erradas, int reclamadas) = Reclamadas(id => Disciplinas.DaForma(id)?.Def.Tipo);
		Checa($"a derivacao acerta as {Catalogo.Todas.Length} formas do catalogo", erradas == 0, $"{erradas} erradas");
		Checa("...e ela reclama exatamente QUATRO", reclamadas == 4, $"{reclamadas}");

		// E CADA UMA PELA PROPRIA DISCIPLINA -- varrido pelo DADO e nao por uma lista de quatro ids:
		// o laco pergunta a cada faixa de cada disciplina, entao um degrau novo (ou uma TERCEIRA
		// disciplina) entra nesta checagem sozinho, sem ninguem vir aqui escrever o nome dele.
		foreach (DisciplinaDef def in Disciplinas.Todas)
			foreach (Degrau g in def.Degraus)
			{
				if (g.Forma.Length == 0) continue;
				Checa($"`{g.Forma}` credita {def.Nome} (e nao a outra escola)",
					  Disciplinas.DaForma(g.Forma)?.Def.Tipo == def.Tipo,
					  Disciplinas.DaForma(g.Forma)?.Def.Nome ?? "ninguem");
				Checa($"...e devolve a FAIXA de {g.Pct:0}% que a concede",
					  Disciplinas.DaForma(g.Forma)?.Faixa.Pct == g.Pct);
			}

		// O CONTRA-EXEMPLO: uma forma da escada comum NAO e reclamada. Sem esta linha, um `DaForma`
		// que respondesse "sim" pra tudo (ou o `Maestrias.Por` recusando o jogo inteiro) passaria
		// verde -- e ninguem no servidor subiria maestria nenhuma, calado.
		foreach (string id in new[] { "ssj1", "ssj2", "ssj3", "beast", Catalogo.IdMistico })
			Checa($"`{id}` NAO e forma de disciplina (a escada comum continua com maestria propria)",
				  Disciplinas.DaForma(id) == null);
		Checa("id vazio e id desconhecido nao viram forma de disciplina",
			  Disciplinas.DaForma("") == null && Disciplinas.DaForma(null) == null
			  && Disciplinas.DaForma("forma_que_nao_existe") == null);

		// --- (c) OS QUATRO PALPITES DEFEITUOSOS: a varredura ENXERGA? ---
		// ============================ E AQUI QUE "DOIS RAMOS" REPROVA ============================
		// A regra pedida e que a derivacao seja UMA -- por PERTENCER A DISCIPLINA. As duas maneiras
		// erradas de escrever isso sao ramificar por ID (uma lista, que envelhece no proximo degrau) e
		// ramificar por LINHA (que precisa de um `if` pro Ultra Instinto e outro pro Ultra Ego). As duas
		// passam pela MESMA conferencia acima e as duas tem que reprovar -- a de linha reprovando nas
		// DUAS formas da outra escola e a prova em numero de que um ramo so nao cobre as quatro.
		//
		// E o terceiro palpite e o defeito que as checagens de "subiu" nao pegam: creditar a disciplina
		// ERRADA. Ele reprova nas quatro (2 que ganharam a escola trocada, 2 que ficaram sem ninguem).
		// =====================================================================================
		Console.WriteLine("  --     agora os PALPITES DEFEITUOSOS: as linhas abaixo tem que dizer que reprovaram");

		(int porId, _) = Reclamadas(id => id is "ui_sign" or "ui_perfected"
			? TipoDeDisciplina.UltraInstinct : null);
		Checa($"[injetado] derivar por LISTA DE IDS do Ultra Instinto reprova nas 2 formas da outra "
			+ $"escola (deu {porId})", porId == 2);

		(int porLinha, _) = Reclamadas(id => Catalogo.Def(id)?.Linha == LinhaDeForma.UltraInstinct
			? TipoDeDisciplina.UltraInstinct : null);
		Checa($"[injetado] derivar por LINHA com UM ramo reprova nas mesmas 2 (deu {porLinha}) -- e por "
			+ "isso a derivacao nao pode ser por linha", porLinha == 2);

		(int trocada, _) = Reclamadas(id => Catalogo.Def(id)?.Linha switch
		{
			LinhaDeForma.UltraInstinct => TipoDeDisciplina.PoderDaDestruicao,
			LinhaDeForma.UltraEgo => TipoDeDisciplina.UltraInstinct,
			_ => null,
		});
		Checa($"[injetado] creditar a disciplina TROCADA reprova nas quatro (deu {trocada})", trocada == 4);

		(int todas, int reclamouTudo) = Reclamadas(_ => TipoDeDisciplina.UltraInstinct);
		Checa($"[injetado] 'toda forma e de disciplina' reprova em {Catalogo.Todas.Length - 2} "
			+ $"(deu {todas}) e reclama o catalogo inteiro ({reclamouTudo})",
			  todas == Catalogo.Todas.Length - 2 && reclamouTudo == Catalogo.Todas.Length);

		// --- (d) O LIVRO RECUSA AS QUATRO, SEJA QUEM FOR QUE ESCREVA ---
		// O tique da forma, o verb de admin e o carregamento do save passam todos pelo mesmo `Por`.
		var livro = new Maestrias();
		foreach (DisciplinaDef def in Disciplinas.Todas)
			foreach (Degrau g in def.Degraus)
			{
				if (g.Forma.Length == 0) continue;
				livro.Por(g.Forma, 100);
				Checa($"`{g.Forma}` nao guarda maestria propria no livro",
					  Math.Abs(livro.De(g.Forma)) < 1e-12, $"{livro.De(g.Forma):0.##}");
				livro.Subir(g.Forma, 50, out string marco);
				Checa($"...nem pelo `Subir` do tique (e sem anunciar marco)",
					  Math.Abs(livro.De(g.Forma)) < 1e-12 && marco.Length == 0, marco);
			}
		livro.Por("ssj1", 42);
		Checa("...e a escada comum continua guardando", Math.Abs(livro.De("ssj1") - 42) < 1e-12);
		Checa("...e o `Subir` dela continua subindo",
			  livro.Subir("ssj1", 58, out string m100) && m100.Contains("DOMINADA"), m100);

		// --- (e) O SAVE DE QUEM JA JOGOU: descarta, e descarta FALANDO ---
		var doDisco = new Dictionary<string, double>
		{
			[Catalogo.Rede("ui_sign").ToString()] = 37,
			[Catalogo.Rede("destroyer").ToString()] = 12,
			[Catalogo.Rede("ssj1").ToString()] = 55,
		};
		var recarregado = new Maestrias();
		List<string> descartadas = recarregado.DoSave(doDisco);
		Checa("o save descarta a maestria das formas de disciplina",
			  Math.Abs(recarregado.De("ui_sign")) < 1e-12 && Math.Abs(recarregado.De("destroyer")) < 1e-12);
		Checa("...e devolve os NOMES delas pra o jogador ser avisado",
			  descartadas.Count == 2
			  && descartadas.Contains(Catalogo.Def("ui_sign")!.Nome)
			  && descartadas.Contains(Catalogo.Def("destroyer")!.Nome),
			  string.Join(", ", descartadas));
		Checa("...sem encostar no resto do livro", Math.Abs(recarregado.De("ssj1") - 55) < 1e-12);
		Checa("um save SEM as quatro nao avisa nada (o aviso e perda de alguem, nao ruido)",
			  new Maestrias().DoSave(new Dictionary<string, double>
			  { [Catalogo.Rede("ssj1").ToString()] = 10 }).Count == 0);

		// --- (f) E A VARREDURA DOS FONTES ---
		VarrerOsFontes();
		Console.WriteLine();
	}

	/// <summary>
	/// RODA UM PALPITE DE "de que disciplina e esta forma?" CONTRA O CATALOGO INTEIRO e devolve
	/// (quantas erradas, quantas reclamadas).
	///
	/// ============================ ELA E FUNCAO DE UM PALPITE, E ISSO E O CONTROLE ============================
	/// Uma conferencia que so roda com a resposta certa nao prova que ENXERGA: `Catalogo.Todas` vazio,
	/// filtro errado, comparacao invertida -- em qualquer desses casos ela diria "0 erradas" e o
	/// relatorio sairia verde por vacuidade. Recebendo o palpite de fora, a MESMA conferencia roda com
	/// a derivacao de verdade (tem que dar zero) e com quatro DEFEITUOSAS (tem que reprovar, e com o
	/// numero exato de formas fora do lugar).
	///
	/// A VERDADE E ESCRITA AQUI E NAO IMPORTADA: chamar `Disciplinas.DaForma` dos dois lados compararia
	/// a funcao com ela mesma -- verde eterno, inclusive no dia em que ela passasse a responder outra
	/// coisa. O que se cobra e a REGRA: uma forma e de uma disciplina quando uma FAIXA dela a concede.
	/// ====================================================================================================
	/// </summary>
	private static (int Erradas, int Reclamadas) Reclamadas(Func<string, TipoDeDisciplina?> palpite)
	{
		int erradas = 0, reclamadas = 0;

		foreach (FormaDef d in Catalogo.Todas)
		{
			TipoDeDisciplina? devia = null;
			foreach (DisciplinaDef def in Disciplinas.Todas)
				foreach (Degrau g in def.Degraus)
					if (string.Equals(g.Forma, d.Id, StringComparison.Ordinal)) devia = def.Tipo;

			TipoDeDisciplina? deu = palpite(d.Id);
			if (deu != devia) erradas++;
			if (deu != null) reclamadas++;
		}
		return (erradas, reclamadas);
	}

	// =====================================================================
	// A VARREDURA DOS FONTES -- a unica linha que reprova o codigo que ainda nao existe
	// =====================================================================
	/// <summary>
	/// Os ids das formas de disciplina, tirados do DADO -- uma terceira disciplina entra sozinha.
	/// </summary>
	private static string[] IdsDeDisciplina =>
		[.. Disciplinas.Todas.SelectMany(d => d.Degraus).Select(g => g.Forma).Where(f => f.Length > 0)];

	/// <summary>
	/// AS LINHAS SUSPEITAS DE UM ARQUIVO: o segundo ramo, escrito a mao.
	///
	/// ============================ O QUE ELA PROCURA, E POR QUE ISSO ============================
	/// A regra do dono se cumpre hoje porque existe UMA derivacao (`Disciplinas.DaForma`) e seis
	/// consumidores dela. O jeito de essa regra morrer nao e alguem apagar a funcao -- e alguem
	/// escrever o segundo ramo em outro lugar, num dia em que a funcao pareca longe demais. Sao duas
	/// formas de escrever esse ramo, e as duas sao pegaveis por leitura:
	///
	///   * CITANDO O ID (`if (forma == "ui_sign")`) -- uma lista, que envelhece no proximo degrau;
	///   * RAMIFICANDO POR LINHA junto de maestria/proficiencia (`Linha == LinhaDeForma.UltraEgo`
	///     numa linha que fala de `Maestria`) -- que precisa de um `if` por escola.
	///
	/// O QUE FICA DE FORA DA VARREDURA, de proposito: o CATALOGO e as faixas (onde os ids sao
	/// DECLARADOS -- `Formas.cs`, `Disciplinas.cs`, `Cinematicas.cs`) e as BANCADAS (`*Bench`, `Robo*`,
	/// `*Teste*`), que citam id porque e o trabalho delas -- exercitar aquela forma pelo nome.
	/// ======================================================================================
	/// </summary>
	private static List<string> Suspeitas(string arquivo, IReadOnlyList<string> linhas)
	{
		string nome = Path.GetFileName(arquivo);
		bool ehBancada = nome.Contains("Bench") || nome.Contains("Robo") || nome.Contains("Teste");
		bool ehDado = nome is "Formas.cs" or "Disciplinas.cs" or "Cinematicas.cs";
		var achadas = new List<string>();
		if (ehBancada || ehDado) return achadas;

		for (int i = 0; i < linhas.Count; i++)
		{
			string l = linhas[i].Trim();
			// COMENTARIO NAO E RAMO. Metade dos comentarios deste port cita `ui_sign` justamente pra
			// explicar a regra -- varrer texto de comentario transformaria documentacao em falha.
			if (l.StartsWith("//") || l.StartsWith("*") || l.StartsWith("/*")) continue;

			foreach (string id in IdsDeDisciplina)
				if (l.Contains($"\"{id}\""))
					achadas.Add($"{nome}:{i + 1} cita `{id}` fora do catalogo -- {l}");

			bool porLinha = l.Contains("LinhaDeForma.UltraInstinct") || l.Contains("LinhaDeForma.UltraEgo");
			bool falaDeMaestria = l.Contains("Maestria") || l.Contains("Proficiencia") || l.Contains("Real");
			if (porLinha && falaDeMaestria)
				achadas.Add($"{nome}:{i + 1} ramifica por LINHA numa linha de maestria -- {l}");
		}
		return achadas;
	}

	/// <inheritdoc cref="Suspeitas"/>
	private static void VarrerOsFontes()
	{
		string raiz = Directory.GetCurrentDirectory();
		var suspeitas = new List<string>();
		var funil = new Dictionary<string, int>(StringComparer.Ordinal);
		int arquivos = 0;

		foreach (string dir in new[] { "Core", "Server", "Client", "Net" })
		{
			string caminho = Path.Combine(raiz, dir);
			if (!Directory.Exists(caminho)) continue;

			foreach (string arq in Directory.EnumerateFiles(caminho, "*.cs", SearchOption.AllDirectories))
			{
				string[] linhas = File.ReadAllLines(arq);
				arquivos++;
				suspeitas.AddRange(Suspeitas(arq, linhas));

				// OS CONSUMIDORES DO FUNIL. Nao e enfeite: se a derivacao fosse chamada de UM lugar so,
				// "uma so derivacao" seria verdade por nao haver o que derivar -- e o dia em que o
				// segundo consumidor nascesse ele nasceria copiado.
				int n = linhas.Count(l => l.Contains("Disciplinas.DaForma(") && !l.Trim().StartsWith("//")
										  && !l.Trim().StartsWith("///"));
				if (n > 0) funil[Path.GetFileName(arq)] = n;
			}
		}

		Checa($"a varredura leu os fontes ({arquivos} arquivos)", arquivos > 100, $"{arquivos}");
		Checa($"nenhum SEGUNDO RAMO escrito a mao fora do catalogo e das bancadas "
			+ $"({suspeitas.Count} achado(s))", suspeitas.Count == 0, string.Join(" | ", suspeitas));

		int consumidores = funil.Values.Sum();
		Checa($"a derivacao e UMA e COMPARTILHADA: {consumidores} chamadas de `Disciplinas.DaForma` em "
			+ $"{funil.Count} arquivos ({string.Join(", ", funil.Select(kv => $"{kv.Key} x{kv.Value}"))})",
			  consumidores >= 4 && funil.Count >= 3);

		// ============================ E A VARREDURA ENXERGA? TRES DEFEITOS INJETADOS ============================
		// Sem estas linhas, `Suspeitas` podia estar devolvendo lista vazia por qualquer motivo -- filtro
		// invertido, `Contains` errado, a extensao errada -- e o verde de cima seria vazio. Os tres casos
		// sao os tres jeitos de escrever o segundo ramo, mais o controle de que codigo LIMPO passa.
		// ==================================================================================================
		Console.WriteLine("  --     agora os DEFEITOS INJETADOS na varredura: tem que ser pegos");
		List<string> porId = Suspeitas("Qualquer.cs",
			["if (forma == \"ui_sign\") est.Real += 0.04 * dt;"]);
		Checa($"[injetado] um `if (forma == \"ui_sign\")` num arquivo comum e pego (deu {porId.Count})",
			  porId.Count == 1);

		List<string> porLinha = Suspeitas("Qualquer.cs",
			["if (d.Linha == LinhaDeForma.UltraEgo) return Maestria.De(id);"]);
		Checa($"[injetado] um ramo por LINHA em codigo de maestria e pego (deu {porLinha.Count})",
			  porLinha.Count == 1);

		Checa("[injetado] e um comentario citando `ui_sign` NAO e pego (documentacao nao e ramo)",
			  Suspeitas("Qualquer.cs", ["// o `Maestria.De(\"ui_sign\")` lia zero pra sempre"]).Count == 0);
		Checa("[injetado] e o mesmo ramo DENTRO de uma bancada nao e pego",
			  Suspeitas("RoboDeForma.cs", ["if (forma == \"ui_sign\") return 1;"]).Count == 0);
	}
}
