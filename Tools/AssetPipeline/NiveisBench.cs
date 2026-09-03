using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DOS DEGRAUS DESCARTADOS E DOS BUFFS QUE O EXTRATOR NAO PEGAVA --
/// `dotnet run --project Tools/AssetPipeline -- niveis [Assets/Data]`.
///
/// ============================ O QUE ELA GUARDA ============================
/// Um censo das skills nas arvores achou que o MAIOR buraco do port nao era verb: era DADO ja
/// extraido e jogado fora pelo leitor. Cada familia abaixo e um desses buracos, e cada prova tem as
/// DUAS metades -- o nivel em que NAO rende e o nivel em que rende; a skill trancada e a mesma skill
/// acesa; a compra que soma e o esquecimento que devolve -- porque uma prova de uma metade so fica
/// verde numa funcao que responde sempre a mesma coisa.
///
///   F5a. O DEGRAU PERIODICO (`if(level % 5 == 0)`, Mind.dm:166) -- descartado pelo `RegrasDoDisco`;
///        as 30 maestrias de Ki perdiam o grosso do ganho. Nivel 4 nao rende, 5 rende, 35 rende 7x.
///   F5b. O `destrava` (`enableskill(Advanced_Ki_Awareness)` no nivel 100, Mind.dm:186) -- ignorado;
///        55 folhas saiam do censo como "sem acendedor". Trancada no 99, acesa no 100.
///   F5c. O GENE por degrau (`add_to_stat("Energy Level", 0.2)` a cada 20, Mind.dm:99) -- ignorado.
///   F5d. O `concede` (`new/datum/skill/sense` + `learn(savant, 1)`, Mind.dm:103) -- o Sense nunca
///        chegava por skill nenhuma.
///   F5e. O MULTIPLICATIVO por degrau (`SpiritBallCost /= 2`, Spirit.dm:321) -- sem canal.
///   F5f. A BARREIRA trocada no degrau (`expbarrier = 20`, Spirit.dm:323) -- caia em campo inexistente.
///   F1a. `pitted` (arlian.dm:88/110) -- campo que nao existia; a regra de arvore que o le nunca apagava a irma.
///   F1b. O gene `Regeneration` (spirit-doll.dm:33) -- fora da traducao, com o consumidor ja portado.
///   F1c. `HPregenbuff` (arlian.dm:108) -- campo que nao existia; o consumidor entrava com 1 cravado.
///   F1d. `KaiokenMastery` (kaioken.dm:93) -- campo que nao existia; a maestria morava num dicionario de sessao.
///   F1e. O GANHO NA COMPRA COM EXPRESSAO (`BP += max(1, BP*0.01)`, Bodybuilding.dm:89) -- o extrator so lia constante.
///   F1f. A ESCOLHA UNICA NA SEGUNDA FORMA (`switch(input(...) in list(...))` direto no `after_learn`,
///        Bodybuilding.dm:119) -- as tres casas eram SOMADAS; e a Grace, que SEGUE a escolha da Trinity.
///   R.   O RAZAO que registrava campo inexistente como aplicado -- a armadilha do save.
///   C.   O CENSO, antes e depois: quantas "sem acendedor" viraram "por degrau".
///
/// TUDO CORE DE PRODUCAO, sobre os `.json` NO DISCO -- os mesmos que o jogo carrega. Codigo de saida =
/// numero de falhas.
/// ==========================================================================
/// </summary>
public static class NiveisBench
{
	private static int _ok, _falhou;

	private static void Conferir(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; Console.WriteLine($"  ok     {oque}"); return; }
		_falhou++;
		Console.WriteLine($"  FALHA  {oque}   {detalhe}");
	}

	private static bool Perto(double a, double b, double eps = 1e-9) => Math.Abs(a - b) < eps;

	/// <summary>Um campo `double` da ficha pelo nome (reflexao, como o `EfeitosDeSkill` escreve). NaN = nao existe.</summary>
	private static double Le(Fighter f, string campo) =>
		typeof(Fighter).GetField(campo)?.GetValue(f) is double d ? d : double.NaN;

	private const string KiUnlocked = "/datum/skill/mind/Ki_Unlocked";
	private const string BasicAwareness = "/datum/skill/mind/Basic_Ki_Awareness";
	private const string AdvancedAwareness = "/datum/skill/mind/Advanced_Ki_Awareness";
	private const string PerfectAwareness = "/datum/skill/mind/Perfect_Ki_Awareness";
	private const string BasicEffusion = "/datum/skill/mind/Basic_Ki_Effusion";
	private const string AdvancedEffusion = "/datum/skill/mind/Advanced_Ki_Effusion";
	private const string Effusionmas = "/datum/skill/tree/effusionmas";
	private const string SpiritBall = "/datum/skill/Spirit_Ball";
	private const string SpiritFist = "/datum/skill/Spirit_Fist";
	private const string Sense = "/datum/skill/sense";
	private const string Flying = "/datum/skill/flying";
	private const string Stick = "/datum/skill/arlian/Stick";
	private const string Supa = "/datum/skill/arlian/Supa";
	private const string DollRegen = "/datum/skill/spiritdoll/DollRegen";
	private const string Play = "/datum/skill/spiritdoll/Play";
	private const string Regenerate = "/datum/skill/general/regenerate";
	private const string Kaioken = "/datum/skill/kaioken";
	private const string OneHundred = "/datum/skill/Bodybuilding/One_Hundred";
	private const string OnePunch = "/datum/skill/Bodybuilding/One_Punch";
	private const string Trinity = "/datum/skill/Bodybuilding/TheHolyTrinity";
	private const string Grace = "/datum/skill/Bodybuilding/Grace";

	public static int Run(string pastaDados)
	{
		Console.WriteLine("=== OS DEGRAUS DESCARTADOS E OS BUFFS QUE O EXTRATOR NAO PEGAVA ===\n");
		_ok = _falhou = 0;

		string cs = Path.Combine(pastaDados, "skills.json");
		string ct = Path.Combine(pastaDados, "skilltrees.json");
		string cn = Path.Combine(pastaDados, "niveis.json");
		if (!File.Exists(cs) || !File.Exists(ct) || !File.Exists(cn))
		{
			Conferir($"ha skills.json, skilltrees.json e niveis.json em {pastaDados}", false);
			return _falhou;
		}
		SkillCatalog cat = SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));
		int regras = RegrasDoDisco.Carregar(File.ReadAllText(cn));
		Console.WriteLine($"catalogo: {cat.Total} entradas | regras de nivel: {regras}\n");

		try
		{
			OPeriodico(cat);
			ODestrava(cat);
			OGene(cat);
			OConcede(cat);
			OMultiplicativo(cat);
			ABarreira();
			OPitted(cat);
			ORegeneration(cat);
			OHPregenbuff(cat);
			OKaiokenMastery(cat);
			OGanhoNaCompra(cat);
			AEscolhaNaSegundaForma(cat);
			OVerbPorCasa(cat);
			ORazao();
			OCenso(cat);
		}
		catch (Exception e)
		{
			_falhou++;
			Console.WriteLine($"  FALHA  a bancada rodou inteira   {e}");
		}

		Console.WriteLine($"\n=== NIVEIS: {_ok} OK, {_falhou} FALHA ===");
		return _falhou;
	}

	private static Fighter Corpo(string raca = "Human", double bp = 1000, string classe = "None")
	{
		var f = new Fighter { Race = raca, Class = classe, BP = bp };
		f.Statify();
		f.PowerLevel();   // e aqui que `relBPmax` nasce (Fighter.Power.cs:149) -- o ganho na compra o le
		return f;
	}

	// =====================================================================
	// F5a) O PERIODICO
	// =====================================================================
	private static void OPeriodico(SkillCatalog cat)
	{
		Console.WriteLine("-- F5a) O DEGRAU PERIODICO: `if(level % 5 == 0) kiawarenessskill += 1` (Mind.dm:166) --");
		RegraDeNivel? r = RegrasDeNivel.Get(BasicAwareness);
		Conferir("a regra da Basic Ki Awareness carregou", r != null);
		if (r == null) return;

		Conferir("ela tem os TRES periodicos do DM (5, 10, 20) alem dos exatos",
				 r.Degraus.Count(d => d.Periodo > 0) == 3 && r.Degraus.Any(d => d.Periodo == 5 && d.Buffs.GetValueOrDefault("kiawarenessskill") == 1),
				 string.Join(",", r.Degraus.Select(d => d.Periodo > 0 ? $"%{d.Periodo}" : $"={d.Nivel}")));
		// `FirstOrDefault`, nao `First`: com o periodico descartado (o defeito que esta familia guarda) a
		// prova tem que ficar VERMELHA, e nao derrubar a bancada inteira antes das outras familias
		Degrau? p5 = r.Degraus.FirstOrDefault(d => d.Periodo == 5);
		Conferir("`Vezes` do periodico 5: nivel 4 = 0, nivel 5 = 1, nivel 35 = 7, nivel 100 = 20",
				 p5 != null && RegraDeNivel.Vezes(p5, 4) == 0 && RegraDeNivel.Vezes(p5, 5) == 1
				 && RegraDeNivel.Vezes(p5, 35) == 7 && RegraDeNivel.Vezes(p5, 100) == 20);
		Conferir("no nivel 100 disparam QUATRO degraus de uma vez (==100, %5, %10, %20) -- os `if` sao irmaos",
				 r.DegrausEm(100).Count() == 4, $"{r.DegrausEm(100).Count()}");
		Conferir("...e no 35 so o %5 (a Basic Ki Awareness nao tem marco no 35)", r.DegrausEm(35).Count() == 1);

		// AS DUAS METADES no corpo
		Fighter f = Corpo();
		var n = new NiveisDeSkill();
		n.Por(BasicAwareness, 4);
		n.Aplicar(f);
		Conferir("nivel 4: kiawarenessskill NAO ganha o periodico (0)", Perto(f.kiawarenessskill, 0), $"{f.kiawarenessskill}");
		n.Por(BasicAwareness, 5);
		n.Aplicar(f);
		Conferir("nivel 5: GANHA (+1)", Perto(f.kiawarenessskill, 1), $"{f.kiawarenessskill}");
		n.Por(BasicAwareness, 35);
		n.Aplicar(f);
		Conferir("nivel 35, contra o DM: kiawarenessskill 7 (7 x %5), kicontrolskill 3 (3 x %10), kiskillBuff 0,05 (1 x %20)",
				 Perto(f.kiawarenessskill, 7) && Perto(f.kicontrolskill, 3) && Perto(f.kiskillBuff, 0.05),
				 $"{f.kiawarenessskill}/{f.kicontrolskill}/{f.kiskillBuff}");
		int mexidos = n.Aplicar(f);
		Conferir("reaplicar no mesmo nivel nao empilha (0 campos mexidos, mesmos numeros)",
				 mexidos == 0 && Perto(f.kiawarenessskill, 7) && Perto(f.kicontrolskill, 3), $"{mexidos}");
		n.Por(BasicAwareness, 100);
		n.Aplicar(f);
		// DM, nivel 100: %5 x20 = 20 awareness; %10 x10 = 10 control + 1 (lvl 50) + 2 (75) + 2 (100) = 15;
		// %20 x5 = 0,25 kiskillBuff; genes 75 e 100 = 0,05 + 0,05 = 0,10 de KiMod
		Conferir("nivel 100, contra o DM: awareness 20, control 15 (10 do %10 + 1 + 2 + 2 dos marcos), kiskillBuff 0,25",
				 Perto(f.kiawarenessskill, 20) && Perto(f.kicontrolskill, 15) && Perto(f.kiskillBuff, 0.25),
				 $"{f.kiawarenessskill}/{f.kicontrolskill}/{f.kiskillBuff}");
		n.Por(BasicAwareness, 5);
		n.Aplicar(f);
		Conferir("descer o nivel DESFAZ pelo razao (volta a +1, e nao fica em 20)", Perto(f.kiawarenessskill, 1) && Perto(f.kicontrolskill, 0),
				 $"{f.kiawarenessskill}/{f.kicontrolskill}");
	}

	// =====================================================================
	// F5b) O DESTRAVA
	// =====================================================================
	private static void ODestrava(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F5b) O `destrava`: `if(level == 100) enableskill(Advanced_Ki_Awareness)` (Mind.dm:186) --");

		Conferir("o niveis.json traz o destrava no degrau 100 da Basic Ki Awareness",
				 RegrasDeNivel.Get(BasicAwareness)?.Degraus.Any(d => d.Nivel == 100 && d.Destrava.Contains(AdvancedAwareness)) == true);
		Conferir("...e o indice invertido responde quem acende a Advanced (Basic no 100)",
				 RegrasDeNivel.DestravadaPor(AdvancedAwareness) is { Count: 1 } q && q[0].Path == BasicAwareness && q[0].Nivel == 100);

		Fighter f = Corpo();
		var livro = new SkillBook();
		livro.Conceder(60);
		livro.Dar(KiUnlocked);
		livro.Dar(BasicAwareness);
		var n = new NiveisDeSkill();
		ContextoDeRegra ctx = ContextoDeRegra.De(f, f.Race, f.Class);
		ctx.DestravadasPorDegrau = () => n.Destravadas();

		n.Por(BasicAwareness, 99);
		livro.Recalcular(cat, ctx, f.Race, f.Class);
		Veredito v99 = livro.Avaliar(cat, AdvancedAwareness, f.Race, f.Class, false);
		Conferir("Basic no 99: Advanced Ki Awareness esta TRANCADA -- e nao como 'morta': AguardaAcendedor",
				 v99.Motivo == Recusa.AguardaAcendedor, v99.Motivo.ToString());
		Conferir("...e o veredito diz o que fazer: 'Basic Ki Awareness chega ao nivel 100'",
				 v99.Acendedor.Contains("nivel 100") && v99.Acendedor.Contains("Basic Ki Awareness"), v99.Acendedor);

		n.Por(BasicAwareness, 100);
		livro.Recalcular(cat, ctx, f.Race, f.Class);
		Veredito v100 = livro.Avaliar(cat, AdvancedAwareness, f.Race, f.Class, false);
		Conferir("Basic no 100: Advanced Ki Awareness ACENDE (Pode)", v100.Motivo == Recusa.Pode, v100.Motivo.ToString());
		Conferir("...e a Perfect continua trancada (ela e do 100 da Advanced)",
				 livro.Avaliar(cat, PerfectAwareness, f.Race, f.Class, false).Motivo == Recusa.AguardaAcendedor);

		livro.Dar(AdvancedAwareness);
		n.Por(AdvancedAwareness, 100);
		livro.Recalcular(cat, ctx, f.Race, f.Class);
		Conferir("Advanced no 100: a Perfect acende -- a cadeia inteira Basic -> Advanced -> Perfect",
				 livro.Avaliar(cat, PerfectAwareness, f.Race, f.Class, false).Motivo == Recusa.Pode);

		// A CADEIA DA EFFUSION: fora da arvore Mind (o `ptree` do DM a deixaria morta -- divergencia declarada)
		f.kieffusionskill = 1;   // `arvore;effusionmas;kieffusionskill>=1` (Mind.dm:17)
		livro.Dar(BasicEffusion);
		n.Por(BasicEffusion, 99);
		livro.Recalcular(cat, ctx, f.Race, f.Class);
		Conferir("a Effusive Mastery abriu pelo contador (pre-condicao da prova)", livro.Destravadas.Contains(Effusionmas));
		Conferir("Basic Ki Effusion no 99: Advanced Ki Effusion aguarda", livro.Avaliar(cat, AdvancedEffusion, f.Race, f.Class, false).Motivo == Recusa.AguardaAcendedor);
		n.Por(BasicEffusion, 100);
		livro.Recalcular(cat, ctx, f.Race, f.Class);
		Conferir("Basic Ki Effusion no 100: Advanced Ki Effusion acende NA ARVORE effusionmas (o alvo e galho dela, nao da Mind)",
				 livro.Avaliar(cat, AdvancedEffusion, f.Race, f.Class, false).Motivo == Recusa.Pode,
				 livro.Avaliar(cat, AdvancedEffusion, f.Race, f.Class, false).Motivo.ToString());

		// O CONTEXTO SEM DEGRAU (o cliente, a bancada de mesa) nao inventa nada
		var semDegrau = new SkillBook();
		semDegrau.Conceder(60);
		semDegrau.Dar(KiUnlocked);
		semDegrau.Dar(BasicAwareness);
		semDegrau.Recalcular(cat, ContextoDeRegra.De(f, f.Race, f.Class), f.Race, f.Class);
		Conferir("sem o leitor de degraus no contexto, a Advanced continua trancada (controle: e o degrau que acende, nao o nivel solto)",
				 semDegrau.Avaliar(cat, AdvancedAwareness, f.Race, f.Class, false).Motivo == Recusa.AguardaAcendedor);
	}

	// =====================================================================
	// F5c) O GENE POR DEGRAU
	// =====================================================================
	private static void OGene(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F5c) O GENE por degrau: `add_to_stat(\"Energy Level\", 0.2)` a cada 20 niveis (Mind.dm:99) --");
		Fighter f = Corpo();
		double kimod0 = f.KiMod;
		var n = new NiveisDeSkill();
		n.Por(KiUnlocked, 19);
		n.Aplicar(f);
		Conferir("Ki Unlocked no 19: KiMod intacto", Perto(f.KiMod, kimod0), $"{f.KiMod}");
		n.Por(KiUnlocked, 20);
		n.Aplicar(f);
		Conferir("no 20: KiMod +0,2 (o gene traduzido, como no after_learn)", Perto(f.KiMod, kimod0 + 0.2), $"{f.KiMod - kimod0}");
		n.Por(KiUnlocked, 100);
		n.Aplicar(f);
		// 5 x 0,2 (%20) + 0,2 (lvl 75) + 0,3 (lvl 100) = 1,5
		Conferir("no 100: KiMod +1,5 (cinco de %20, mais os marcos 75 e 100)", Perto(f.KiMod, kimod0 + 1.5), $"{f.KiMod - kimod0}");
		Conferir("...e `gene:Energy Level` NAO esta em Desconhecidos", !EfeitosDeSkill.Desconhecidos.Contains("gene:Energy Level"));
	}

	// =====================================================================
	// F5d) O CONCEDE
	// =====================================================================
	private static void OConcede(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F5d) O `concede`: `new/datum/skill/sense` + `learn(savant, 1)` no nivel 5 (Mind.dm:103-104) --");
		var livro = new SkillBook();
		livro.Dar(KiUnlocked);
		var n = new NiveisDeSkill();
		n.Por(KiUnlocked, 4);
		Conferir("no 4: nada a conceder", !n.ConcessoesPendentes(livro).Any());
		n.Por(KiUnlocked, 5);
		var pend = n.ConcessoesPendentes(livro).ToList();
		Conferir("no 5: o Sense esta pendente, NO NIVEL 1 (o baselevel do learn)",
				 pend.Count == 1 && pend[0].Path == Sense && pend[0].Nivel == 1, string.Join(",", pend));
		livro.Dar(Sense);
		Conferir("...e some da fila quando o livro ja o tem (idempotente no login)", !n.ConcessoesPendentes(livro).Any());
		n.Por(KiUnlocked, 30);
		Conferir("no 30 o DADO traz o voo (`learn(savant, 0)`): e o SERVIDOR que o recusa, por decisao do dono (Voo.cs)",
				 n.ConcessoesPendentes(livro).Any(c => c.Path == Flying && c.Nivel == 0));
	}

	// =====================================================================
	// F5e) O MULTIPLICATIVO
	// =====================================================================
	private static void OMultiplicativo(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F5e) O MULTIPLICATIVO por degrau: `SpiritBallCost /= 2`, `SpiritBallDamage *= 2` (Spirit.dm:321-322) --");
		Fighter f = Corpo();
		Conferir("a ficha nasce com SpiritBallCost 2 e SpiritBallDamage 1 (Spirit.dm:340-342)", f.SpiritBallCost == 2 && f.SpiritBallDamage == 1);
		var n = new NiveisDeSkill();
		n.Por(SpiritBall, 0);
		n.Aplicar(f);
		Conferir("nivel 0: nada muda", f.SpiritBallCost == 2 && f.SpiritBallDamage == 1);
		n.Por(SpiritBall, 1);
		n.Aplicar(f);
		Conferir("nivel 1: custo 1, dano 2", Perto(f.SpiritBallCost, 1) && Perto(f.SpiritBallDamage, 2), $"{f.SpiritBallCost}/{f.SpiritBallDamage}");
		n.Por(SpiritBall, 2);
		n.Aplicar(f);
		Conferir("nivel 2: custo 0,5, dano 2,5 (x1,25) -- os degraus COMPOEM", Perto(f.SpiritBallCost, 0.5) && Perto(f.SpiritBallDamage, 2.5), $"{f.SpiritBallCost}/{f.SpiritBallDamage}");
		n.Aplicar(f);
		Conferir("reaplicar nao divide de novo", Perto(f.SpiritBallCost, 0.5) && Perto(f.SpiritBallDamage, 2.5));
		n.Por(SpiritBall, 0);
		n.Aplicar(f);
		Conferir("voltar ao 0 DESFAZ por divisao (custo 2, dano 1)", Perto(f.SpiritBallCost, 2) && Perto(f.SpiritBallDamage, 1), $"{f.SpiritBallCost}/{f.SpiritBallDamage}");

		Fighter g = Corpo();
		var m = new NiveisDeSkill();
		m.Por(SpiritFist, 2);
		m.Aplicar(g);
		Conferir("Spirit Fist nivel 2: SpiritFistCost 0,5 e SpiritFistDamage 1,25 (os campos que o verb agora le)",
				 Perto(g.SpiritFistCost, 0.5) && Perto(g.SpiritFistDamage, 1.25), $"{g.SpiritFistCost}/{g.SpiritFistDamage}");
		Conferir("...e nenhum dos quatro campos esta em Desconhecidos",
				 !EfeitosDeSkill.Desconhecidos.Contains("SpiritBallCost") && !EfeitosDeSkill.Desconhecidos.Contains("SpiritFistCost")
				 && !EfeitosDeSkill.Desconhecidos.Contains("SpiritFistDamage"));
	}

	// =====================================================================
	// F5f) A BARREIRA TROCADA NO DEGRAU
	// =====================================================================
	private static void ABarreira()
	{
		Console.WriteLine("\n-- F5f) A BARREIRA trocada no degrau: `expbarrier = 20` no nivel 1 (Spirit.dm:323) --");
		RegraDeNivel? r = RegrasDeNivel.Get(SpiritBall);
		Conferir("a Spirit Ball declara 10 (Spirit.dm:287): do 0 pro 1 custa 10", r != null && Perto(r.BarreiraEm(0), 10), $"{r?.BarreiraEm(0)}");
		Conferir("do 1 pro 2 custa 20 (o degrau trocou a barreira)", r != null && Perto(r.BarreiraEm(1), 20), $"{r?.BarreiraEm(1)}");
		Conferir("...e `expbarrier` nao e flag de degrau (nao vira `Escrever` num campo que nao existe)",
				 r != null && r.Degraus.All(d => !d.Flags.ContainsKey("expbarrier")) && !EfeitosDeSkill.Desconhecidos.Contains("expbarrier"));
		RegraDeNivel? ku = RegrasDeNivel.Get(KiUnlocked);
		Conferir("quem nao troca segue a curva: Ki Unlocked 5000 no 0, 5000 x 1,03 no 1",
				 ku != null && Perto(ku.BarreiraEm(0), 5000) && Perto(ku.BarreiraEm(1), 5150), $"{ku?.BarreiraEm(1)}");
	}

	// =====================================================================
	// F1a) pitted
	// =====================================================================
	private static void OPitted(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1a) `pitted`: a Stick escreve 1 (arlian.dm:88) e o treegrow apaga a Supa (arlian.dm:13-14) --");
		Conferir("o skills.json traz `pitted=1` na Stick e `pitted=2` na Supa",
				 cat.Get(Stick)?.Flags.GetValueOrDefault("pitted") == 1 && cat.Get(Supa)?.Flags.GetValueOrDefault("pitted") == 2);
		Conferir("...e a arvore Arlian traz o `treegrow()` como regra: `apaga;Supa;pitted==1`",
				 cat.Get("/datum/skill/tree/arlian")?.Regras.Contains("apaga;/datum/skill/arlian/Supa;pitted==1") == true,
				 string.Join(" | ", cat.Get("/datum/skill/tree/arlian")?.Regras ?? []));

		// o Arlian e uma CLASSE do Alien (skilltrees.json: `"classe": { "Arlian": [...] }`), nao uma raca
		Fighter f = Corpo("Alien", classe: "Arlian");
		var livro = new SkillBook();
		livro.Conceder(20);
		livro.Recalcular(cat, ContextoDeRegra.De(f, f.Race, f.Class), f.Race, f.Class);
		Conferir("Arlian sem nada: Stick E Supa compraveis", livro.PodeAprender(cat, Stick, f.Race, f.Class, false) == Recusa.Pode
				 && livro.PodeAprender(cat, Supa, f.Race, f.Class, false) == Recusa.Pode,
				 $"{livro.PodeAprender(cat, Stick, f.Race, f.Class, false)}/{livro.PodeAprender(cat, Supa, f.Race, f.Class, false)}");

		livro.Aprender(cat, Stick, f.Race, f.Class, false);
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		// lido por REFLEXAO, como a producao escreve: e assim que "o campo nao existe" (o defeito que
		// esta familia guarda) fica vermelho em vez de nem compilar
		Conferir("comprou Stick: `pitted` = 1 na ficha", Perto(Le(f, "pitted"), 1), $"{Le(f, "pitted")}");
		livro.Recalcular(cat, ContextoDeRegra.De(f, f.Race, f.Class), f.Race, f.Class);
		Conferir("...e a Supa esta APAGADA pela regra que le o pitted",
				 livro.PodeAprender(cat, Supa, f.Race, f.Class, false) == Recusa.Apagada,
				 livro.PodeAprender(cat, Supa, f.Race, f.Class, false).ToString());
		Conferir("`pitted` nao esta em Desconhecidos", !EfeitosDeSkill.Desconhecidos.Contains("pitted"));

		Fighter d = Corpo("SpiritDoll");
		var l2 = new SkillBook();
		l2.Conceder(20);
		l2.Dar(DollRegen);
		EfeitosDeSkill.Aplicar(d, cat, l2.Aprendidas, l2.Escolhas);
		l2.Recalcular(cat, ContextoDeRegra.De(d, d.Race, d.Class), d.Race, d.Class);
		Conferir("Spirit Doll com Doll Regeneration: Play Hard apagada (spirit-doll.dm:13-14)",
				 l2.PodeAprender(cat, Play, d.Race, d.Class, false) == Recusa.Apagada, l2.PodeAprender(cat, Play, d.Race, d.Class, false).ToString());
	}

	// =====================================================================
	// F1b) O GENE Regeneration
	// =====================================================================
	private static void ORegeneration(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1b) O gene `Regeneration`: `add_to_stat(\"Regeneration\", 10)` (spirit-doll.dm:33) --");
		Fighter f = Corpo("SpiritDoll");
		var livro = new SkillBook();
		livro.Dar(DollRegen);
		EfeitosDeSkill.Desconhecidos.Clear();
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		Conferir("Doll Regeneration: RegenerationDeSkill = 10", Perto(f.RegenerationDeSkill, 10), $"{f.RegenerationDeSkill}");
		Conferir("...e `gene:Regeneration` saiu de Desconhecidos", !EfeitosDeSkill.Desconhecidos.Contains("gene:Regeneration"));

		// O CONSUMIDOR: o perfil do Spirit Doll (raca 1 no races.json) com +10 vira cura em combate
		PerfilDeRegen sem = PerfilDeRegen.De("SpiritDoll", 1);
		PerfilDeRegen com = PerfilDeRegen.De("SpiritDoll", 1 + f.RegenerationDeSkill);
		Conferir("no PerfilDeRegen: sem a skill nao cura em combate; com os +10 cura (fastRegen, Genetic_Datum.dm:259) e o membro volta",
				 !sem.CuraEmCombate && com.CuraEmCombate && !sem.MembroVolta && com.MembroVolta);

		Fighter a = Corpo("Alien");
		var l2 = new SkillBook();
		l2.Dar(Regenerate);
		EfeitosDeSkill.Aplicar(a, cat, l2.Aprendidas, l2.Escolhas);
		Conferir("Regenerate (alien.dm:65): +5", Perto(a.RegenerationDeSkill, 5), $"{a.RegenerationDeSkill}");
	}

	// =====================================================================
	// F1c) HPregenbuff
	// =====================================================================
	private static void OHPregenbuff(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1c) `HPregenbuff`: a Supa soma 0,1 (arlian.dm:108) e o canal 4 da cura o multiplica (master.dm:185) --");
		Fighter f = Corpo("Arlian");
		f.HP = 60;
		f.staminapercent = 1;
		PerfilDeRegen r = PerfilDeRegen.De("Arlian", 1);
		double antes = Regeneracao.TaxaEspalhada(r, f);
		var livro = new SkillBook();
		livro.Dar(Supa);
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		Conferir("Supa: HPregenbuff = 1,1", Perto(f.HPregenbuff, 1.1), $"{f.HPregenbuff}");
		double depois = Regeneracao.TaxaEspalhada(r, f);
		Conferir("...e a cura passiva com 60 de HP SOBE (o canal 4 multiplica pelo campo)", depois > antes, $"{antes} -> {depois}");
		Conferir("`HPregenbuff` nao esta em Desconhecidos", !EfeitosDeSkill.Desconhecidos.Contains("HPregenbuff"));
	}

	// =====================================================================
	// F1d) KaiokenMastery
	// =====================================================================
	private static void OKaiokenMastery(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1d) `KaiokenMastery`: nasce 1 (kaioken.dm:3), a skill soma 3 (:93) --");
		Fighter f = Corpo();
		Conferir("a ficha nasce com KaiokenMastery 1", Perto(f.KaiokenMastery, 1));
		var livro = new SkillBook();
		livro.Dar(Kaioken);
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		Conferir("com a skill: 4 -- o numero de quem destravou a tecnica", Perto(f.KaiokenMastery, 4), $"{f.KaiokenMastery}");
		Conferir("`KaiokenMastery` nao esta em Desconhecidos", !EfeitosDeSkill.Desconhecidos.Contains("KaiokenMastery"));
		Conferir("...e o razao guarda o campo (ele existe agora): reaplicar nao soma de novo",
				 f.BuffsDeSkill.ContainsKey("KaiokenMastery") && EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas) == 0 && Perto(f.KaiokenMastery, 4));
	}

	// =====================================================================
	// F1e) O GANHO NA COMPRA COM EXPRESSAO
	// =====================================================================
	private static void OGanhoNaCompra(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1e) O GANHO NA COMPRA: `BP += max(1, BP*0.01)`, `hiddenpotential += relBPmax*2` (Bodybuilding.dm:89-92) --");
		Skill? s = cat.Get(OneHundred);
		Conferir("o skills.json traz as duas expressoes da One Hundred", s is { Compra.Length: 2 },
				 string.Join(" ; ", s?.Compra.Select(g => $"{g.Campo}{(g.Sinal > 0 ? "+=" : "-=")}{g.Expressao}") ?? []));
		Conferir("...e a One Punch com relBPmax*0,5", cat.Get(OnePunch)?.Compra.Any(g => g.Expressao.Contains("0.5")) == true);

		Fighter f = Corpo(bp: 1000);
		double bp0 = f.BP, hp0 = f.hiddenpotential, rel = f.relBPmax;
		Conferir("pre-condicao: a ficha tem relBPmax > 0 (senao a prova do potencial mede zero)", rel > 0, $"{rel}");
		var livro = new SkillBook();
		livro.Dar(OneHundred);
		Dictionary<string, double> somou = GanhoNaCompra.Aplicar(f, livro, s!);
		// contra o DM, numero por numero: max(1, 1000*0.01) = 10; relBPmax*2
		Conferir("BP: 1000 -> 1010 (max(1, 1%) = 10)", Perto(f.BP, bp0 + 10), $"{f.BP}");
		Conferir($"hiddenpotential: +relBPmax*2 = +{rel * 2:0.##}", rel > 0 && Perto(f.hiddenpotential, hp0 + rel * 2), $"{f.hiddenpotential - hp0}");
		Conferir("o livro registrou o que somou (o `storedBP` do datum)",
				 livro.GanhosNaCompra.TryGetValue(OneHundred, out var reg) && Perto(reg.GetValueOrDefault("BP"), 10) && somou.Count == 2);
		Conferir("aplicar de novo NAO soma de novo (o relog)", GanhoNaCompra.Aplicar(f, livro, s!).Count == 0 && Perto(f.BP, bp0 + 10));

		Fighter g = Corpo(bp: 50);
		var l2 = new SkillBook();
		l2.Dar(OneHundred);
		GanhoNaCompra.Aplicar(g, l2, s!);
		Conferir("com BP 50 o piso do `max(1, ...)` vale: +1, e nao +0,5", Perto(g.BP, 51), $"{g.BP}");

		Dictionary<string, double> devolveu = GanhoNaCompra.Desfazer(f, livro, OneHundred);
		Conferir("esquecer DEVOLVE exatamente o que somou (BP 1000, potencial de volta) -- o before_forget (:98-100)",
				 Perto(f.BP, bp0) && Perto(f.hiddenpotential, hp0) && devolveu.Count == 2 && !livro.GanhosNaCompra.ContainsKey(OneHundred),
				 $"{f.BP}/{f.hiddenpotential}");
		Conferir("desfazer de novo nao tira nada", GanhoNaCompra.Desfazer(f, livro, OneHundred).Count == 0 && Perto(f.BP, bp0));
	}

	// =====================================================================
	// F1f) A ESCOLHA UNICA NA SEGUNDA FORMA, e a que se herda
	// =====================================================================
	private static void AEscolhaNaSegundaForma(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1f) A ESCOLHA na 2a forma: `switch(input(...) in list(...))` direto no after_learn (Bodybuilding.dm:119) e a Grace que a segue (:243) --");
		Skill? t = cat.Get(Trinity);
		Skill? g = cat.Get(Grace);
		Conferir("a Holy Trinity tem TRES casas e NENHUM buff somado (antes saia com physdefBuff 0,6 = as tres juntas)",
				 t is { Escolhas.Length: 3, Buffs.Count: 0 }, $"{t?.Escolhas.Length} casas, {t?.Buffs.Count} buffs");
		Conferir("as casas sao Van-sama / Ricardo / Aniki com os numeros do DM (Van-sama: physdef 0,3, physoff 0,1)",
				 t?.Escolhas[0].Rotulo == "Van-sama" && Perto(t.Escolhas[0].Buffs.GetValueOrDefault("physdefBuff"), 0.3)
				 && Perto(t.Escolhas[0].Buffs.GetValueOrDefault("physoffBuff"), 0.1) && t.Escolhas[1].Rotulo == "Ricardo" && t.Escolhas[2].Rotulo == "Aniki");
		Conferir("a Grace tem tres casas, nenhum buff somado, e SEGUE a Trinity", g is { Escolhas.Length: 3, Buffs.Count: 0 } && g.EscolhaSegue == Trinity,
				 $"{g?.Escolhas.Length}/{g?.Buffs.Count}/{g?.EscolhaSegue}");

		Fighter f = Corpo();
		var livro = new SkillBook();
		livro.Dar(Trinity);
		livro.Dar(Grace);
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		Conferir("sem casa escolhida as duas NAO rendem nada (fiel: os buffs moram dentro do switch)",
				 Perto(f.physdefBuff, 0) && Perto(f.staminagainMod, 1) && Perto(f.willpowerMod, 1), $"{f.physdefBuff}/{f.staminagainMod}");

		livro.Escolher(cat, Trinity, 2);   // Ricardo
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		// Trinity/Ricardo: stamina +0,1, physdef +0,2, physoff +0,2, Lifespan (fora do port)
		// Grace/Ricardo (herdada): stamina +0,05, willpower +0,05, Regeneration +1
		Conferir("escolhida Ricardo na Trinity: physdef 0,2 e physoff 0,2 (so a casa dela)",
				 Perto(f.physdefBuff, 0.2) && Perto(f.physoffBuff, 0.2), $"{f.physdefBuff}/{f.physoffBuff}");
		Conferir("...e a Grace ENTRA NA MESMA CASA sem ninguem escolher nela: stamina 0,1 + 0,05, willpower +0,05, Regeneration +1",
				 Perto(f.staminagainMod, 1.15) && Perto(f.willpowerMod, 1.05) && Perto(f.RegenerationDeSkill, 1),
				 $"{f.staminagainMod}/{f.willpowerMod}/{f.RegenerationDeSkill}");
		Conferir("`CasaEscolhida` da Grace responde 2 (Ricardo) pela lider", EfeitosDeSkill.CasaEscolhida(cat, g!, livro.Escolhas) == 2);

		Fighter h = Corpo();
		var l2 = new SkillBook();
		l2.Dar(Trinity);
		l2.Dar(Grace);
		l2.Escolher(cat, Trinity, 1);   // Van-sama
		EfeitosDeSkill.Aplicar(h, cat, l2.Aprendidas, l2.Escolhas);
		Conferir("com Van-sama: physdef 0,3 na Trinity e willpower +0,05 / stamina +0,2 (0,1 + 0,1) -- outra casa, outros numeros",
				 Perto(h.physdefBuff, 0.3) && Perto(h.willpowerMod, 1.05) && Perto(h.staminagainMod, 1.2) && Perto(h.RegenerationDeSkill, 0),
				 $"{h.physdefBuff}/{h.willpowerMod}/{h.staminagainMod}");
	}

	// =====================================================================
	// F1g) O VERB POR CASA -- o degrau 2 da Trindade
	// =====================================================================
	/// <summary>
	/// `switch(TrinityType) if("Van-sama") assignverb(Taunt) ...` (Bodybuilding.dm:180-185): o degrau 2 da
	/// Holy Trinity concede UM verb conforme a casa escolhida ao aprender. O extrator punha o `switch`
	/// inteiro em quarentena (`logica`), e os tres verbs tinham corpo no lote G10 e porta NENHUMA -- a
	/// bancada do G10 os provava com um degrau sintetico. Aqui: o dado NO DISCO, o `VerbosAtivos` com a
	/// casa, e a fonte de exp (`if(savant.IsInFight) exp++`, :176) que ate entao era condicao desconhecida
	/// -- sem ela a Trindade nunca chegava ao nivel 2 e o verb nunca vinha.
	/// </summary>
	private static void OVerbPorCasa(SkillCatalog cat)
	{
		Console.WriteLine("\n-- F1g) O VERB POR CASA: o degrau 2 da Trindade concede UM dos tres conforme a casa (Bodybuilding.dm:180-185) --");
		RegraDeNivel? r = RegrasDeNivel.Get(Trinity);
		Degrau? d2 = r?.Degraus.FirstOrDefault(d => d.Nivel == 2);
		Conferir("o niveis.json NO DISCO traz o degrau 2 da Trindade com TRES verbs por casa e nenhum verb incondicional",
				 d2 is { VerbosPorCasa.Length: 3, Verbos.Length: 0 }
				 && d2.VerbosPorCasa.Any(p => p.Casa == "Van-sama" && p.Verbo == "Taunt")
				 && d2.VerbosPorCasa.Any(p => p.Casa == "Ricardo" && p.Verbo == "Slap")
				 && d2.VerbosPorCasa.Any(p => p.Casa == "Aniki" && p.Verbo == "Counter_Taunt"),
				 d2 == null ? "sem degrau 2" : string.Join(",", d2.VerbosPorCasa.Select(p => $"{p.Casa}|{p.Verbo}")));
		Conferir("...e o censo de verbs por degrau (`RegrasDeNivel.VerbosDeDegrau`) conta Taunt, Slap e Counter_Taunt",
				 new[] { "Taunt", "Slap", "Counter_Taunt" }.All(v => RegrasDeNivel.VerbosDeDegrau.Contains(v, StringComparer.OrdinalIgnoreCase)));

		var livro = new SkillBook();
		livro.Dar(Trinity);
		var n = new NiveisDeSkill();
		n.Por(Trinity, 2);
		string? Casa(string p) => EfeitosDeSkill.RotuloDaCasa(cat, livro.Escolhas, p);

		Conferir("no nivel 2 SEM casa escolhida, nenhum verb (TrinityType nulo: o switch do DM nao entra em ramo nenhum)",
				 !n.VerbosAtivos(Casa).Any(), string.Join(",", n.VerbosAtivos(Casa)));
		livro.Escolher(cat, Trinity, 1);
		Conferir("casa 1 (Van-sama): SO o Taunt", n.VerbosAtivos(Casa).SequenceEqual(["Taunt"]), string.Join(",", n.VerbosAtivos(Casa)));
		livro.Escolher(cat, Trinity, 2);
		Conferir("casa 2 (Ricardo): SO o Slap", n.VerbosAtivos(Casa).SequenceEqual(["Slap"]), string.Join(",", n.VerbosAtivos(Casa)));
		livro.Escolher(cat, Trinity, 3);
		Conferir("casa 3 (Aniki): SO o Counter_Taunt", n.VerbosAtivos(Casa).SequenceEqual(["Counter_Taunt"]), string.Join(",", n.VerbosAtivos(Casa)));
		Conferir("sem resolvedor de casa (quem nao tem livro), nenhum -- o lado seguro",
				 !n.VerbosAtivos().Any());
		n.Por(Trinity, 1);
		Conferir("no nivel 1, com a casa escolhida, nenhum: o verb e do DEGRAU 2 e nao da compra",
				 !n.VerbosAtivos(Casa).Any());

		// A FONTE DE EXP: em luta. `RegrasDoDisco` mapeava `savant.IsInFight` pra "condicao desconhecida".
		Conferir("a Trindade sobe de nivel EM LUTA (`if(savant.IsInFight) exp++`, :176) -- a condicao entrou como `Estado.Lutando`",
				 r != null && r.PorEstado.Any(g => g.Quando == RegraDeNivel.Estado.Lutando && g.Quanto > 0)
				 && RegrasDoDisco.CondicoesNaoEntendidas >= 0);
		var l3 = new SkillBook();
		l3.Dar(Trinity);
		var n3 = new NiveisDeSkill();
		n3.Por(Trinity, 1);
		var rng = new Random(20260902);
		for (int i = 0; i < 100; i++) n3.Efetor(rng, cat, l3, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Lutando: false));
		Conferir("cem tiques FORA de luta: exp zero, nivel 1 (a Trindade nao sobe por tempo nem meditando)",
				 n3.Nivel(Trinity) == 1 && n3.Exp(Trinity) == 0, $"nivel {n3.Nivel(Trinity)} exp {n3.Exp(Trinity)}");
		for (int i = 0; i < 100; i++) n3.Efetor(rng, cat, l3, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Lutando: true));
		Conferir("cem tiques EM LUTA (barreira 100, um de exp por tique): chega ao nivel 2 -- o verb da casa passa a existir",
				 n3.Nivel(Trinity) == 2, $"nivel {n3.Nivel(Trinity)} exp {n3.Exp(Trinity)}");
	}

	// =====================================================================
	// R) O RAZAO
	// =====================================================================
	private static void ORazao()
	{
		Console.WriteLine("\n-- R) O RAZAO so guarda o que PEGOU (a armadilha do save com campo que nao existia) --");
		const string json = """
			[
			  { "path": "/datum/skill/bancada/Fantasma", "nome": "Fantasma", "tipo": "Physical", "tier": 1, "custo": 1, "maxnivel": 1,
			    "buffs": ["campoQueNaoExiste=1", "physoffBuff=0.5"], "flags": ["chaveQueNaoExiste=1"], "mults": ["fatorQueNaoExiste=2"] }
			]
			""";
		SkillCatalog cat = SkillCatalog.Parse(json, "{}");
		Fighter f = Corpo();
		var livro = new SkillBook();
		livro.Dar("/datum/skill/bancada/Fantasma");
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		Conferir("o buff no campo que existe pegou (physoffBuff 0,5) e esta no razao",
				 Perto(f.physoffBuff, 0.5) && f.BuffsDeSkill.ContainsKey("physoffBuff"));
		Conferir("o buff e o mult em campo que NAO existe NAO entram no razao (antes entravam, e o save carregava a mentira)",
				 !f.BuffsDeSkill.ContainsKey("campoQueNaoExiste") && !f.MultsDeSkill.ContainsKey("fatorQueNaoExiste"),
				 string.Join(",", f.BuffsDeSkill.Keys.Concat(f.MultsDeSkill.Keys)));
		Conferir("...e ele esta em Desconhecidos, que e onde um campo que falta tem que aparecer",
				 EfeitosDeSkill.Desconhecidos.Contains("campoQueNaoExiste"));

		// AS FLAGS SAO A EXCECAO, E ELA E DE PROPOSITO: `FlagsDeSkill` e o ARMAZEM das chaves sem campo
		// que o catalogo de formas le por nome (`snamek`, `hasayyform` -- `Formas.PedeFlag`). Filtra-las
		// apagou a Super Namek e a forma Alien em 2026-09-02; a escrita das flags nao consulta o razao,
		// entao a armadilha do save nao as alcanca.
		Conferir("a CHAVE sem campo FICA no razao das flags -- e o armazem que o `Formas.PedeFlag` le (snamek/hasayyform)",
				 f.FlagsDeSkill.GetValueOrDefault("chaveQueNaoExiste") == 1, string.Join(",", f.FlagsDeSkill.Keys));
		const string jsonNamek = """
			[
			  { "path": "/datum/skill/bancada/SuperNamekDeBancada", "nome": "Super Namek de bancada", "tipo": "Physical", "tier": 1, "custo": 1, "maxnivel": 1,
			    "buffs": [], "flags": ["snamek=1"] }
			]
			""";
		SkillCatalog catN = SkillCatalog.Parse(jsonNamek, "{}");
		Fighter namek = Corpo("Namekian");
		var livroN = new SkillBook();
		livroN.Dar("/datum/skill/bancada/SuperNamekDeBancada");
		EfeitosDeSkill.Aplicar(namek, catN, livroN.Aprendidas, livroN.Escolhas);
		EfeitosDeSkill.Aplicar(namek, catN, livroN.Aprendidas, livroN.Escolhas);   // idempotente: a segunda nao apaga
		Conferir("`snamek=1` (uma chave que o Fighter nao tem como campo) chega a `FlagsDeSkill` e sobrevive a reaplicacao -- o elo skill -> forma",
				 namek.FlagsDeSkill.GetValueOrDefault("snamek") == 1, string.Join(",", namek.FlagsDeSkill.Keys));
		livroN.Esquecer("/datum/skill/bancada/SuperNamekDeBancada");
		EfeitosDeSkill.Aplicar(namek, catN, livroN.Aprendidas, livroN.Escolhas);
		Conferir("...e esquecer a skill tira a chave do armazem (a forma fecha junto)",
				 !namek.FlagsDeSkill.ContainsKey("snamek"));
	}

	// =====================================================================
	// C) O CENSO
	// =====================================================================
	private static void OCenso(SkillCatalog cat)
	{
		Console.WriteLine("\n-- C) O CENSO das trancadas, com os degraus na mao --");
		CensoDeSkills.Relatorio sem = CensoDeSkills.Levantar(cat);
		CensoDeSkills.Relatorio com = CensoDeSkills.Levantar(cat, RegrasDeNivel.VerbosDeDegrau, RegrasDeNivel.DestravadasPorDegrau);
		Console.WriteLine($"         sem degraus: {sem.SemAcendedor.Count} sem acendedor | com degraus: {com.SemAcendedor.Count} sem acendedor, {com.TrancadasPorDegrau.Count} por degrau");
		Conferir("com o niveis.json, pelo menos 30 folhas trancadas passam a ter acendedor POR DEGRAU", com.TrancadasPorDegrau.Count >= 30, $"{com.TrancadasPorDegrau.Count}");
		Conferir("...e as 'sem acendedor' CAEM (eram 55)", com.SemAcendedor.Count < sem.SemAcendedor.Count && com.SemAcendedor.Count <= 25, $"{com.SemAcendedor.Count}");
		Conferir("a Advanced Ki Awareness esta 'por degrau' (Basic Ki Awareness nivel 100)",
				 com.TrancadasPorDegrau.Any(x => x.Nome == "Advanced Ki Awareness" && x.Quem.Contains("100")));
		Conferir("a Basic Buff Mastery CONTINUA sem acendedor (o growbranches dela mora na arvore errada, BuffMastery.dm:15)",
				 com.SemAcendedor.Contains("Basic Buff Mastery"));
		int total = com.TrancadasPorPreReq + com.TrancadasSoVilao + com.TrancadasComAcendedor + com.AcendedorForaDoPort.Count
				  + com.TrancadasPorDegrau.Count + com.TrancadasSoPorEnsino + com.SemAcendedor.Count;
		Conferir("a conta fecha: as sete classes somam todas as `ligada: 0`", total == cat.Todas.Count(s => !s.Arvore && !s.Ligada), $"{total}");
		Console.WriteLine("         sem acendedor agora: " + string.Join("; ", com.SemAcendedor));
	}
}
