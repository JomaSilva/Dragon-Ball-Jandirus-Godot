using System.Reflection;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// O QUE AS SKILLS FAZEM NO CORPO -- a parte passiva, que e a maioria.
///
/// SAO QUATRO CANAIS, e descobrir isso custou uma varredura inteira do DM. A primeira versao daqui
/// so conhecia o primeiro e dava 200 skills como "sem efeito"; eram 180, e a diferenca eram skills
/// que faziam efeito por um caminho que ninguem tinha olhado:
///
///   1. ADITIVO      `savant.physoffBuff += 0.4`   -- 87 skills. O canal obvio.
///   2. MULTIPLICATIVO `savant.MedMod *= 2`        -- 6 skills. Campos que nascem em 1; somar 2 num
///                                                    deles daria o numero certo so por acidente.
///   3. GENETICO     `savant.genome.add_to_stat("Energy Level", 0.1)` -- 21 skills. NAO passa pelos
///                                                    campos `*Buff`: mexe no modificador da raca.
///   4. ATRIBUICAO   `savant.can_sup_el = 1`       -- 34 skills. Flags de capacidade e CONTADORES
///                                                    de arvore (`bodyskill`, `bodyreadiness`).
///
/// O NOME DO CAMPO DO DM E O NOME DO CAMPO DAQUI nos canais 1, 2 e 4, e por isso a entrega e por
/// REFLEXAO em vez de um switch de sessenta casos: a cadeia de stats foi portada 1:1 de proposito,
/// entao o switch seria uma segunda copia da mesma tabela -- e tabela duplicada e tabela que
/// diverge. Reflexao aqui e barata: isto roda ao APRENDER e ao ENTRAR, nunca no tick.
///
/// O CANAL 3 PRECISA DE TRADUCAO (<see cref="DeGene"/>) porque o DM indexa a genetica por texto
/// ("Energy Level") e o port ja resolveu isso em campo na hora do nascimento (`KiMod`).
///
/// E O CAMPO QUE NAO EXISTE VIRA RELATORIO, nao silencio. <see cref="Desconhecidos"/> junta todo
/// nome que o DM buffa e que o port ainda nao tem. Sem isso, uma skill que promete "+regeneracao"
/// e nao faz nada parece funcionar -- ninguem descobre pela tela, so pela planilha, anos depois.
///
/// APLICAR E IDEMPOTENTE. Guarda o que aplicou da ultima vez e desfaz antes de aplicar de novo,
/// entao aprender uma skill, deslogar, entrar e reaplicar tudo NAO empilha o buff duas vezes. Um
/// sistema de efeito permanente que soma no login e o jeito classico de um personagem virar um
/// deus depois de vinte relogs.
/// </summary>
public static class EfeitosDeSkill
{
	/// <summary>Campos que o DM buffa e que este port ainda nao tem. Diagnostico, nao erro.</summary>
	public static readonly SortedSet<string> Desconhecidos = new(StringComparer.Ordinal);

	private static readonly Dictionary<string, FieldInfo?> Cache = new(StringComparer.Ordinal);

	/// <summary>
	/// A TRADUCAO DO CANAL GENETICO: o nome que o DM usa como indice do `modifiers[]` da genetica,
	/// e o campo do <see cref="Fighter"/> que o port ja resolveu pra ele no nascimento
	/// (ver <c>Fighter.FromGenome</c> -- e de la que estes pares saem, nao de adivinhacao).
	///
	/// O que nao esta aqui e porque o port ainda nao tem o conceito, e vai sair no relatorio em vez
	/// de virar um `+=` num campo parecido. "Regeneration" nao e `HPregen`; chutar seria pior que
	/// admitir.
	/// </summary>
	private static readonly Dictionary<string, string> DeGene = new(StringComparer.Ordinal)
	{
		["Energy Level"] = "KiMod",
		["Battle Power"] = "BPMod",
		["Tech Modifier"] = "techmod",
		["Ascension Mod"] = "ascBPmod",
	};

	/// <summary>
	/// O que a soma de todas as skills aprendidas da, canal a canal.
	/// Aditivo e genetico SOMAM; multiplicativo COMPOE (duas skills que dobram dao 4x).
	/// </summary>
	public static (Dictionary<string, double> Soma, Dictionary<string, double> Fator, Dictionary<string, double> Set)
		Totalizar(SkillCatalog cat, IEnumerable<string> aprendidas)
	{
		var soma = new Dictionary<string, double>(StringComparer.Ordinal);
		var fator = new Dictionary<string, double>(StringComparer.Ordinal);
		var set = new Dictionary<string, double>(StringComparer.Ordinal);

		foreach (string path in aprendidas)
		{
			Skill? s = cat.Get(path);
			if (s == null) continue;

			foreach ((string campo, double v) in s.Buffs)
				soma[campo] = soma.GetValueOrDefault(campo) + v;

			// o canal genetico cai no MESMO balde do aditivo depois de traduzido -- os dois
			// terminam somando num campo do lutador, e separa-los aqui so criaria dois caminhos
			// pra desfazer no relog
			foreach ((string stat, double v) in s.Genes)
			{
				if (!DeGene.TryGetValue(stat, out string? campo)) { Desconhecidos.Add($"gene:{stat}"); continue; }
				soma[campo] = soma.GetValueOrDefault(campo) + v;
			}

			foreach ((string campo, double v) in s.Mults)
				fator[campo] = fator.GetValueOrDefault(campo, 1) * v;

			// ATRIBUICAO: o ULTIMO vence, e nao ha ordem definida entre skills. Nao ha caso real
			// de duas skills setarem a mesma flag com valores diferentes -- se houvesse, o DM
			// tambem nao teria ordem. Fica o maior, que e o unico criterio estavel.
			foreach ((string campo, double v) in s.Flags)
				set[campo] = Math.Max(set.GetValueOrDefault(campo), v);
		}
		return (soma, fator, set);
	}

	/// <summary>
	/// Poe no lutador exatamente os efeitos das skills que ele sabe -- nem mais, nem de novo.
	/// Devolve quantos campos foram efetivamente mexidos.
	/// </summary>
	public static int Aplicar(Fighter f, SkillCatalog cat, IEnumerable<string> aprendidas)
	{
		(Dictionary<string, double> soma, Dictionary<string, double> fator, Dictionary<string, double> set)
			= Totalizar(cat, aprendidas);
		int mexidos = 0;

		// --- ADITIVO: desfaz o que saiu, ajusta o que mudou ---
		foreach ((string campo, double antigo) in f.BuffsDeSkill)
			if (!soma.ContainsKey(campo)) mexidos += Somar(f, campo, -antigo) ? 1 : 0;

		foreach ((string campo, double v) in soma)
		{
			double delta = v - f.BuffsDeSkill.GetValueOrDefault(campo);
			if (Math.Abs(delta) > 1e-9 && Somar(f, campo, delta)) mexidos++;
		}

		// --- MULTIPLICATIVO: DIVIDE o fator antigo antes de multiplicar o novo ---
		foreach ((string campo, double antigo) in f.MultsDeSkill)
			if (!fator.ContainsKey(campo) && antigo != 0) mexidos += Multiplicar(f, campo, 1 / antigo) ? 1 : 0;

		foreach ((string campo, double v) in fator)
		{
			double antigo = f.MultsDeSkill.GetValueOrDefault(campo, 1);
			if (antigo == 0) antigo = 1;
			double raz = v / antigo;
			if (Math.Abs(raz - 1) > 1e-9 && Multiplicar(f, campo, raz)) mexidos++;
		}

		// --- ATRIBUICAO: o que saiu volta a zero (o default do DM pra flag e contador) ---
		foreach ((string campo, double _) in f.FlagsDeSkill)
			if (!set.ContainsKey(campo)) mexidos += Escrever(f, campo, 0) ? 1 : 0;

		foreach ((string campo, double v) in set)
			if (Math.Abs(v - f.FlagsDeSkill.GetValueOrDefault(campo)) > 1e-9 && Escrever(f, campo, v)) mexidos++;

		f.BuffsDeSkill = soma;
		f.MultsDeSkill = fator;
		f.FlagsDeSkill = set;
		return mexidos;
	}

	/// <summary>O cache UNICO de reflexao sobre o Fighter. `internal` pra que o motor de
	/// niveis use este e nao escreva um segundo -- ver NiveisDeSkill.Somar.</summary>
	internal static FieldInfo? Campo(string nome)
	{
		if (Cache.TryGetValue(nome, out FieldInfo? fi)) return fi;
		fi = typeof(Fighter).GetField(nome, BindingFlags.Public | BindingFlags.Instance);
		if (fi != null && fi.FieldType != typeof(double)) fi = null;
		Cache[nome] = fi;
		if (fi == null) Desconhecidos.Add(nome);
		return fi;
	}

	private static bool Somar(Fighter f, string campo, double delta)
	{
		FieldInfo? fi = Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! + delta);
		return true;
	}

	private static bool Multiplicar(Fighter f, string campo, double razao)
	{
		FieldInfo? fi = Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! * razao);
		return true;
	}

	private static bool Escrever(Fighter f, string campo, double valor)
	{
		FieldInfo? fi = Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, valor);
		return true;
	}
}
