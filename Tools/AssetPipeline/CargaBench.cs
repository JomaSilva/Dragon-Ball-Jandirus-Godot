using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA CARGA DE KI (`dotnet run --project Tools/AssetPipeline -- carga [races.json]`).
///
/// POR QUE ELA EXISTE. Carregar Ki e um sistema que so se ve em jogo depois de segurar uma tecla
/// por dez segundos, e o que ele produz -- BP -- e justamente o numero que o jogo esconde do
/// jogador. Ou seja: a UNICA forma de saber se as tres etapas acontecem, em quanto tempo, e quanto
/// de poder rendem, e medir fora do jogo. Sem isto o sistema so poderia ser conferido por "parece
/// que subiu".
///
/// O QUE ELA PROVA, em ordem:
///   1. As duas CHAVES separam mesmo (sem Ki Unlocked nada acontece; sem Ki Control nao passa dos 100%).
///   2. O TEMPO de cada etapa bate com o do original (9 s pra encher com controle, 15 s sem).
///   3. O BP SOBE de verdade ao ultrapassar -- e quanto.
///   4. O EXCESSO cobra: acima de `kicapacity` sai dano e o Ki vaza sozinho de volta.
/// </summary>
public static class CargaBench
{
	/// <summary>O tick do servidor. Medir na cadencia real e o que torna os segundos comparaveis.</summary>
	private const double Dt = 1.0 / 30.0;

	public static void Run(RaceCatalog? cat)
	{
		Console.WriteLine("=== CARGA DE KI (a tecla C segurada) ===\n");
		Elo();
		Chaves(cat);
		Tempos(cat);
		Poder(cat);
		Preco(cat);
	}

	/// <summary>
	/// AS SKILLS REALMENTE LIGAM AS CHAVES?
	///
	/// ============================ POR QUE ESTE TESTE EXISTE ============================
	/// Este e o defeito que o projeto ja cometeu meia duzia de vezes: o extrator tira o dado do
	/// DM, o arquivo sai certo, e NINGUEM CONSOME. Foi exatamente o caso do `canPower` -- ele
	/// estava no `niveis.json` desde o comeco, com `"flags": ["canPower=1"]`, e o leitor de
	/// degraus so olhava `buffs`. A tecla C teria ficado pela metade sem nada acusar: sem erro,
	/// sem aviso, so um power-up que nunca passa dos 100%.
	///
	/// Entao aqui nao se testa a formula -- testa-se o ELO. Aprender a skill de verdade, pelo
	/// mesmo caminho do servidor, e ver se o campo do lutador mudou.
	/// ================================================================================
	/// </summary>
	private static void Elo()
	{
		Console.WriteLine("-- O ELO: a skill liga a chave? --");

		string cs = "Assets/Data/skills.json", ct = "Assets/Data/skilltrees.json", cn = "Assets/Data/niveis.json";
		if (!File.Exists(cs) || !File.Exists(ct)) { Console.WriteLine("  (sem skills.json -- rode da raiz do projeto)\n"); return; }

		var cat = Jandirus.Core.Skills.SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));
		var f = new Fighter { Race = "Human", BP = 500 };

		Console.WriteLine($"  antes de tudo            MeditateGivesKiRegen={f.MeditateGivesKiRegen:0}  canPower={f.canPower:0}");

		// 1) COMPRAR Ki Unlocked -> MeditateGivesKiRegen (canal de flags da COMPRA)
		Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(f, cat, ["/datum/skill/mind/Ki_Unlocked"]);
		Console.WriteLine($"  comprei Ki Unlocked      MeditateGivesKiRegen={f.MeditateGivesKiRegen:0}  canPower={f.canPower:0}"
						  + (f.MeditateGivesKiRegen != 0 ? "   OK" : "   <<< ELO ROTO"));

		// 2) SUBIR Basic Ki Control ao nivel 5 -> canPower (canal de flags do NIVEL)
		if (!File.Exists(cn)) { Console.WriteLine("  (sem niveis.json)\n"); return; }
		Jandirus.Core.Skills.RegrasDoDisco.Carregar(File.ReadAllText(cn));

		var niveis = new Jandirus.Core.Skills.NiveisDeSkill();
		niveis.Por("/datum/skill/mind/Basic_Ki_Control", 5);
		niveis.Aplicar(f);

		Console.WriteLine($"  Basic Ki Control nivel 5 MeditateGivesKiRegen={f.MeditateGivesKiRegen:0}  canPower={f.canPower:0}"
						  + (f.canPower != 0 ? "   OK" : "   <<< ELO ROTO (flags de degrau nao aplicadas)"));
		Console.WriteLine();
	}

	/// <summary>Monta um lutador pronto, pelo mesmo caminho do servidor.</summary>
	private static Fighter Novo(RaceCatalog? cat, double kiControl = 0, bool kiUnlocked = true)
	{
		Fighter f = cat != null
			? Birth.Nascer(cat, "Human", "", new Random(20260803), "Human")
			: new Fighter { Race = "Human", BP = 500 };

		f.Tick();
		f.MeditateGivesKiRegen = kiUnlocked ? 1 : 0;
		f.canPower = kiControl;
		f.Ki = f.MaxKi * 0.25;   // um quarto de tanque: da pra ver as tres etapas em fila
		f.Tick();
		return f;
	}

	// =====================================================================
	private static void Chaves(RaceCatalog? cat)
	{
		Console.WriteLine("-- AS DUAS CHAVES (10 s segurando C) --");
		Console.WriteLine("  quem                        Ki final    passou dos 100%?");

		foreach ((string nome, double control, bool unlock) in new[]
		{
			("sem Ki Unlocked", 0.0, false),
			("so Ki Unlocked", 0.0, true),
			("Ki Unlocked + Control 5", 1.0, true),
		})
		{
			Fighter f = Novo(cat, control, unlock);
			double antes = f.Ki;
			for (int i = 0; i < (int)(10 / Dt); i++) { CargaDeKi.Passo(f, Dt, mexendo: false); f.Tick(); }

			string passou = f.Ki > f.MaxKi * 1.001 ? "SIM" : "nao";
			string mexeu = Math.Abs(f.Ki - antes) < 1e-6 ? "  (a tecla e muda)" : "";
			Console.WriteLine($"  {nome,-26}  {f.Ki / f.MaxKi * 100,6:0.0}%    {passou}{mexeu}");
		}
		Console.WriteLine();
	}

	// =====================================================================
	private static void Tempos(RaceCatalog? cat)
	{
		Console.WriteLine("-- QUANTO DEMORA CADA ETAPA (do zero) --");
		Console.WriteLine("  quem                     encher 0->100%   depois, ate o teto");

		foreach ((string nome, double control) in new[] { ("so Ki Unlocked", 0.0), ("com Ki Control 5", 1.0) })
		{
			Fighter f = Novo(cat, control);
			f.Ki = 0;
			f.Tick();

			double t = 0, encheu = -1, teto = -1;
			double limite = CargaDeKi.TetoDeCarga(f);

			// 120 s de teto: se nao chegou nisso, nao chega -- e o relatorio tem que dizer isso em
			// vez de rodar pra sempre.
			while (t < 120)
			{
				CargaDeKi.Passo(f, Dt, mexendo: false);
				f.Tick();
				t += Dt;
				if (encheu < 0 && f.Ki >= f.MaxKi * 0.999) encheu = t;
				if (teto < 0 && f.Ki >= limite * 0.999) { teto = t; break; }
			}

			string ate = teto < 0 ? "nunca (sem controle)" : $"{teto - encheu,6:0.0} s";
			Console.WriteLine($"  {nome,-22}   {encheu,10:0.0} s   {ate}");
		}
		Console.WriteLine("  (o DM faz MaxKi/90 e MaxKi/150 por chamada, a ~10 chamadas/s -> 9 s e 15 s)\n");
	}

	// =====================================================================
	/// <summary>
	/// O PONTO DO SISTEMA INTEIRO: passar dos 100% de Ki E o buff de BP. Nao ha multiplicador
	/// escondido -- o `kiratio` entra no `statusBuff` do `PowerLevel` e o BP expresso sobe junto.
	/// </summary>
	private static void Poder(RaceCatalog? cat)
	{
		Console.WriteLine("-- O BUFF DE BP (carregando alem dos 100%) --");
		Console.WriteLine("  segundos    Ki       BP expresso    ganho");

		Fighter f = Novo(cat, kiControl: 1);
		f.Ki = f.MaxKi;
		f.Tick();
		double bp0 = f.expressedBP;

		for (int s = 0; s <= 30; s += 5)
		{
			if (s > 0)
				for (int i = 0; i < (int)(5 / Dt); i++) { CargaDeKi.Passo(f, Dt, mexendo: false); f.Tick(); }

			Console.WriteLine($"  {s,6} s   {f.Ki / f.MaxKi * 100,5:0}%   {f.expressedBP,12:N0}    {f.expressedBP / Math.Max(bp0, 1),5:0.00}x");
		}
		Console.WriteLine();
	}

	// =====================================================================
	private static void Preco(RaceCatalog? cat)
	{
		Console.WriteLine("-- O PRECO DE FICAR LA EM CIMA (sem segurar C) --");
		Console.WriteLine("  Ki inicial   dano em 5 s   Ki depois de 5 s   (kicapacity = teto seguro)");

		// AS DUAS ULTIMAS LINHAS PASSAM DO TETO SEGURO DE PROPOSITO. Carregar sozinho nao chega la
		// (a carga para em 140% e o teto e 169%), entao sem elas o ramo de dano ficaria sem prova
		// nenhuma -- e um `if` que nunca roda em teste e um `if` que ninguem sabe se funciona.
		// Quem chega nessa faixa e Kaio-ken e as tecnicas que empurram Ki, nao a tecla C.
		foreach (double razao in new[] { 1.0, 1.15, 1.3, 1.5, 1.75, 2.2 })
		{
			Fighter f = Novo(cat, kiControl: 1);
			f.Ki = f.MaxKi * razao;
			f.Tick();

			double dano = 0;
			for (int i = 0; i < (int)(5 / Dt); i++) { dano += CargaDeKi.PrecoDoExcesso(f, Dt); f.Tick(); }

			Console.WriteLine($"  {razao * 100,8:0}%   {dano,11:0.00}   {f.Ki / f.MaxKi * 100,14:0.0}%"
							  + (razao == 1.0 ? "   <- no limite: nada acontece" : ""));
		}

		// AS DUAS EM UNIDADES DIFERENTES, e imprimir as duas cruas ja escondeu um defeito uma vez:
		// `kicapacity` e ABSOLUTA (o Statify a faz `1,3*MaxKi*log(...)`) e `powerupcap` e RAZAO.
		// Aqui a primeira sai convertida pra razao, que e o unico jeito de as duas se compararem.
		Fighter amostra = Novo(cat, kiControl: 1);
		Console.WriteLine($"\n  este corpo: MaxKi {amostra.MaxKi:N0}"
						  + $"  ·  teto seguro {amostra.kicapacity / Math.Max(amostra.MaxKi, 1) * 100:0}% do MaxKi"
						  + $"  ·  a carga alcanca {amostra.powerupcap * 100:0}%");
		Console.WriteLine("  (a faixa entre os dois e a que DOI: da pra ir la, e cobra)");
	}
}
