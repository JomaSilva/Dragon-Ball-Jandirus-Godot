using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCO DE PROVA DO COMBATE. Duelos completos, sem subir o jogo.
///
/// Existe pelo mesmo motivo dos outros bancos: combate e cheio de numero invisivel. "Ta bom
/// assim?" nao se responde olhando o codigo -- se responde vendo quantos socos uma luta dura,
/// quanto o gap de poder decide, e se o nao-letal realmente nao mata. Cada rodada aqui roda a
/// MESMA funcao que o servidor roda.
/// </summary>
public static class CombatBench
{
	public static void Run(RaceCatalog? cat)
	{
		Console.WriteLine("=== CADENCIA ===");
		Cadencia();

		Console.WriteLine("\n=== GAP DE PODER (BPModulus) ===");
		Gap();

		Anatomia(cat);
		Guarda(cat);

		Console.WriteLine("\n=== DUELOS ===");
		Duelos(cat);

		Console.WriteLine("\n=== NAO-LETAL x LETAL ===");
		Letalidade(cat);

		Console.WriteLine("\n=== CORPO: quebrar, decepar, cair ===");
		Corpo();
		Rabo(cat);
	}

	// =====================================================================
	private static void Cadencia()
	{
		var f = new Fighter();
		Console.WriteLine("  Eactspeed  leve      pesado    (o BYOND fazia 0,667 s no leve)");
		foreach (double act in new double[] { 20, 16, 12, 8 })
		{
			f.Eactspeed = act;
			Console.WriteLine($"  {act,9:0}  {CombatMath.Cadencia(f):0.000} s  {CombatMath.Cadencia(f, 3):0.000} s"
							  + (act == 20 ? "   <- stat inicial" : "   (Ki carregado)"));
		}
	}

	private static void Gap()
	{
		Console.WriteLine("  meu BP / dele      multiplicador de dano");
		foreach ((double a, double b) in new[] { (100.0, 100.0), (200.0, 100.0), (1000.0, 100.0),
												 (100.0, 200.0), (100.0, 1000.0), (100.0, 1e6) })
			Console.WriteLine($"  {a,10:0} / {b,-10:0}  {CombatMath.BpModulus(a, b):0.####}x");
	}

	// =====================================================================
	/// <summary>Monta um lutador de verdade, pelo mesmo caminho que o servidor usa.</summary>
	private static (Fighter F, CombatState C) Lutador(RaceCatalog? cat, string raca, double bp, Random rng)
	{
		Fighter f = cat != null
			? Birth.Nascer(cat, raca, "", rng, raca)
			: new Fighter { Race = raca, BP = bp };

		f.BP = bp;
		f.Tick();
		f.Ki = f.MaxKi;
		f.Tick();
		return (f, new CombatState(f, raca == "Saiyan", raca == "Namekian"));
	}

	/// <summary>
	/// Uma luta ate alguem cair. Devolve quantos golpes levou e quem ganhou.
	///
	/// Os dois batem alternadamente na cadencia de cada um -- nao e um turno "justo": quem
	/// tem Eactspeed melhor golpeia mais vezes no mesmo tempo, e e assim que deve ser.
	/// </summary>
	private static (int Golpes, string Vencedor, double Segundos) Duelo(
		(Fighter F, CombatState C) a, (Fighter F, CombatState C) b, Random rng, bool letal,
		double tetoSegundos = 300)
	{
		a.C.Letal = letal;
		b.C.Letal = letal;

		double t = 0, proxA = 0, proxB = 0;
		int golpes = 0;
		const double passo = 1.0 / 30;

		while (t < tetoSegundos)
		{
			t += passo;
			a.C.Tick(passo);
			b.C.Tick(passo);

			if (t >= proxA && a.C.PodeAtacar())
			{
				MeleeResolver.Resolver(a.C, b.C, 0, rng);
				proxA = t + CombatMath.Cadencia(a.F);
				golpes++;
				if (b.F.dead) return (golpes, a.F.Name, t);
			}
			if (t >= proxB && b.C.PodeAtacar())
			{
				MeleeResolver.Resolver(b.C, a.C, 0, rng);
				proxB = t + CombatMath.Cadencia(b.F);
				golpes++;
				if (a.F.dead) return (golpes, b.F.Name, t);
			}

			// sem morte, KO decide
			if (letal) continue;
			if (a.F.KO) return (golpes, b.F.Name, t);
			if (b.F.KO) return (golpes, a.F.Name, t);
		}
		return (golpes, "ninguem (deu o tempo)", t);
	}

	private static void Duelos(RaceCatalog? cat)
	{
		var rng = new Random(1234);
		Console.WriteLine("  cenario                        golpes   duracao   vencedor");

		void Caso(string rotulo, string racaA, double bpA, string racaB, double bpB, bool letal = true)
		{
			var a = Lutador(cat, racaA, bpA, rng);
			var b = Lutador(cat, racaB, bpB, rng);
			a.F.Name = $"{racaA}({Curto(bpA)})";
			b.F.Name = $"{racaB}({Curto(bpB)})";
			(int g, string v, double s) = Duelo(a, b, rng, letal);
			Console.WriteLine($"  {rotulo,-28}  {g,6}   {s,6:0.0}s   {v}");
		}

		Caso("iguais, 100 BP", "Human", 100, "Human", 100);
		Caso("iguais, 100k BP", "Human", 100_000, "Human", 100_000);
		Caso("2x mais forte", "Human", 200, "Human", 100);
		Caso("10x mais forte", "Human", 1000, "Human", 100);
		Caso("Saiyajin x Humano (igual BP)", "Saiyan", 1000, "Human", 1000);
		Caso("Namekuseijin x Humano", "Namekian", 1000, "Human", 1000);
		Caso("iguais, NAO-letal", "Human", 100, "Human", 100, letal: false);
	}

	/// <summary>
	/// Quanto UM soco tira, e quantos socos aguenta um membro. E o numero que decide se a luta
	/// dura dez segundos ou dois minutos, e ele nao aparece em lugar nenhum do codigo.
	/// </summary>
	private static void Anatomia(RaceCatalog? cat)
	{
		Console.WriteLine();
		Console.WriteLine("=== UM GOLPE, POR DENTRO ===");
		var rng = new Random(99);
		Console.WriteLine("  cenario                  acerto%   dano/golpe   socos p/ zerar um membro");

		void Caso(string rotulo, double bpA, double bpB, double angulo = 0)
		{
			var a = Lutador(cat, "Human", bpA, rng);
			var b = Lutador(cat, "Human", bpB, rng);
			a.C.Letal = true;

			int acertos = 0;
			double soma = 0;
			const int n = 4000;
			for (int i = 0; i < n; i++)
			{
				b.C.Corpo.Restaurar();          // isola o golpe: sempre contra corpo inteiro
				b.F.dead = false;
				b.F.KO = false;
				GolpeResultado r = MeleeResolver.Resolver(a.C, b.C, angulo, rng);
				if (!r.Encostou) continue;
				acertos++;
				soma += r.Dano;
			}
			double medio = acertos == 0 ? 0 : soma / acertos;
			string socos = medio <= 0 ? "-" : $"{100 / medio:0.#}";
			Console.WriteLine($"  {rotulo,-22}  {acertos * 100.0 / n,6:0.0}%   {medio,10:0.##}   {socos,8}");
		}

		Caso("iguais (de frente)", 100, 100);
		Caso("iguais (pelas costas)", 100, 100, 180);
		Caso("2x mais forte", 200, 100);
		Caso("10x mais forte", 1000, 100);
		Caso("10x mais FRACO", 100, 1000);
		Console.WriteLine("  (membro = 100 de vida; nucleo zerado = morte, abaixo de 20% = nocaute)");
	}

	/// <summary>
	/// A guarda vale a pena? Mede quantos golpes ela apara, quantos viram contra-ataque e
	/// quanto passa quando ela cede.
	/// </summary>
	private static void Guarda(RaceCatalog? cat)
	{
		Console.WriteLine();
		Console.WriteLine("=== GUARDA E CONTRA-ATAQUE ===");
		var rng = new Random(5);
		var a = Lutador(cat, "Human", 100, rng);
		var d = Lutador(cat, "Human", 100, rng);
		a.C.Letal = true;

		int aparou = 0, contra = 0, passou = 0;
		double danoAparado = 0, danoSolto = 0;
		const int n = 3000;

		for (int i = 0; i < n; i++)
		{
			d.C.Corpo.Restaurar();
			d.F.dead = false;
			d.F.KO = false;
			// O tempo anda ANTES de a guarda subir: e assim que a recarga do contra-ataque
			// escoa em jogo. Subir a guarda depois e o que rearma a janela.
			d.C.Guardar(false);
			d.C.Tick(2.0);
			d.C.Guardar(true);
			if (i % 2 == 0) d.C.TempoDeGuarda = 1.0;   // metade ja estava de guarda ha tempo
			d.F.Ki = d.F.MaxKi;
			a.C.Stun = 0;

			GolpeResultado r = MeleeResolver.Resolver(a.C, d.C, 0, rng);
			switch (r.Desfecho)
			{
				case Desfecho.Aparou: aparou++; danoAparado += r.Dano; break;
				case Desfecho.Contra: contra++; break;
				case Desfecho.Acertou:
				case Desfecho.Critico: passou++; danoSolto += r.Dano; break;
			}
		}

		Console.WriteLine($"  aparados : {aparou * 100.0 / n,5:0.0}%  ({(aparou == 0 ? 0 : danoAparado / aparou):0.##} de dano cada)");
		Console.WriteLine($"  contras  : {contra * 100.0 / n,5:0.0}%  <- CONSERTO 3: no BYOND isto era 0%, sempre");
		Console.WriteLine($"  passaram : {passou * 100.0 / n,5:0.0}%  ({(passou == 0 ? 0 : danoSolto / passou):0.##} de dano cada -- a guarda cedeu)");
	}

	// =====================================================================
	private static void Letalidade(RaceCatalog? cat)
	{
		var rng = new Random(7);
		foreach (bool letal in new[] { false, true })
		{
			var a = Lutador(cat, "Human", 5000, rng);
			var b = Lutador(cat, "Human", 100, rng);
			a.C.Letal = letal;

			int golpes = 0;
			for (int i = 0; i < 400 && !b.F.dead; i++)
			{
				MeleeResolver.Resolver(a.C, b.C, 0, rng);
				golpes++;
			}

			double menor = b.C.Corpo.Partes.Min(p => p.Fracao);
			Console.WriteLine($"  {(letal ? "LETAL    " : "nao-letal")}: {golpes} golpes | vida {b.C.Corpo.Vida():0.0}"
							  + $" | pior membro {menor * 100:0.0}% | KO={b.F.KO} morto={b.F.dead}"
							  + $" | membros perdidos {b.C.Corpo.Perdidos().Count()}");
		}
		Console.WriteLine("  (nao-letal NAO pode matar nem arrancar membro; pode nocautear)");
	}

	// =====================================================================
	private static void Corpo()
	{
		Body c = Body.Novo(comRabo: true);
		Console.WriteLine($"  partes: {c.Partes.Count} | vida inicial: {c.Vida():0.0}");

		BodyPart braco = c.Achar("Braco direito")!;
		c.Ferir(braco, 85, letal: true);
		Console.WriteLine($"  braco a {braco.Fracao * 100:0}% -> quebrado={braco.Quebrado}"
						  + $" | vida do corpo {c.Vida():0.0} | KO={c.DeveNocautear()} morte={c.DeveMorrer()}");

		List<BodyPart> caiu = c.Decepar(braco);
		Console.WriteLine($"  decepado -> caiu junto: {string.Join(", ", caiu.Select(p => p.Nome))}"
						  + "   <- CONSERTO 2: a mao vai com o braco");
		Console.WriteLine($"  vida do corpo apos decepar: {c.Vida():0.0} (o decepado sai do denominador)");

		BodyPart cabeca = c.Achar("Cabeca")!;
		c.Ferir(cabeca, 60, letal: true);
		BodyPart cerebro = c.Achar("Cerebro")!;
		Console.WriteLine($"  golpe de 60 na cabeca -> cabeca {cabeca.Fracao * 100:0}%,"
						  + $" cerebro {cerebro.Fracao * 100:0}% (propagacao de 20%)");

		c.Ferir(cabeca, 40, letal: true);
		Console.WriteLine($"  cabeca zerada -> KO={c.DeveNocautear()} morte={c.DeveMorrer()}");
	}

	/// <summary>
	/// O RABO DO SAIYAJIN. Prova que a regra de arrancar existe de fato -- no original ela
	/// era codigo morto (testava `hpratio < 0.6` contra um `hpratio` com PISO em 0,6, ou
	/// seja: impossivel). Aqui a conta usa a vida real do corpo.
	/// </summary>
	private static void Rabo(RaceCatalog? cat)
	{
		Console.WriteLine();
		Console.WriteLine("=== O RABO ===");
		var rng = new Random(31);

		var a = Lutador(cat, "Saiyan", 20_000, rng);
		var d = Lutador(cat, "Saiyan", 1_000, rng);
		a.C.Letal = true;

		BodyPart? rabo = d.C.Corpo.Achar("Rabo");
		Console.WriteLine($"  Saiyajin nasce com rabo? {(rabo != null ? "sim" : "NAO")}"
						  + $" | partes: {d.C.Corpo.Partes.Count}");
		if (rabo == null) return;

		// DE FRENTE nao arranca, por mais que bata
		int golpes = 0;
		while (golpes < 300 && !rabo.Decepado && !d.F.dead)
		{
			MeleeResolver.Resolver(a.C, d.C, 0, rng);
			golpes++;
		}
		Console.WriteLine($"  {golpes} golpes DE FRENTE -> rabo decepado: {rabo.Decepado}"
						  + $" (vida do corpo {d.C.Corpo.Vida():0.0})");

		// PELAS COSTAS, com o corpo ja abaixo de 60%, arranca
		d.C.Reviver();
		foreach (BodyPart p in d.C.Corpo.Partes) p.Vida = p.VidaMax * 0.5;
		d.C.SincronizarVida();

		golpes = 0;
		while (golpes < 300 && !rabo.Decepado && !d.F.dead)
		{
			MeleeResolver.Resolver(a.C, d.C, 180, rng);
			golpes++;
		}
		Console.WriteLine($"  PELAS COSTAS a 50% de vida -> rabo decepado: {rabo.Decepado}"
						  + $" em {golpes} golpe(s)");
	}

	private static string Curto(double v) => v >= 1000 ? $"{v / 1000:0.#}k" : $"{v:0}";
}
