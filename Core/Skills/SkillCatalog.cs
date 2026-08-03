namespace Jandirus.Core.Skills;

/// <summary>Uma skill (ou uma ARVORE de skills) como o catalogo extraido do DM a descreve.</summary>
public sealed class Skill
{
	public string Path = "";
	public string Nome = "";
	public string Desc = "";
	public string Tipo = "Physical";
	public int Tier = 1;
	public int Custo = 1;
	public int MaxNivel = 3;

	/// <summary>Quantos pre-requisitos da pra PULAR (o `prereqthreshold` do original).</summary>
	public int Folga;

	public bool Arvore;
	public bool Comum;      // common_sense: qualquer um aprende, sem gate de raca/classe
	public bool Ligada;     // enabled
	public bool SoVilao;
	public bool Ensinavel;
	public bool CustoFixo;

	public string[] Racas = [];
	public string[] Classes = [];
	public string[] PreReqs = [];
	public string[] Galhos = [];   // so nas arvores: as skills penduradas nela

	/// <summary>
	/// O EFEITO PASSIVO: campo do lutador -> quanto somar, pra sempre, ao aprender. Vem do
	/// corpo do `after_learn()` do DM, que em 85 skills e literalmente `physoffBuff += 0.4`.
	/// </summary>
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);

	/// <summary>As habilidades ATIVAS que a skill destrava (os `assignverb` do DM).</summary>
	public string[] Verbos = [];

	/// <summary>Buff MULTIPLICATIVO (`MedMod *= 2`) -- canal separado do aditivo.</summary>
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);

	/// <summary>Buff pelo modificador GENETICO (`add_to_stat("Energy Level", 0.1)`).</summary>
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);

	/// <summary>Atribuicao direta: flags de capacidade e contadores de arvore.</summary>
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);

	/// <summary>O estilo de luta que esta skill concede. Vazio em todas menos nove.</summary>
	public string Estilo = "";

	/// <summary>Uma folha que nao soma nada nem destrava nada AINDA nao tem efeito portado.</summary>
	public bool SemEfeito => !Arvore && Buffs.Count == 0 && Verbos.Length == 0 && Estilo.Length == 0
		&& Mults.Count == 0 && Genes.Count == 0 && Flags.Count == 0;
}

/// <summary>
/// O CATALOGO DE SKILLS, lido do `skills.json` que o Tools/AssetPipeline extrai da arvore de
/// tipos do DM (comando `skills`). 319 skills em 47 arvores.
///
/// POR QUE ISTO E DADO E NAO CODIGO: sao trezentas e dezenove. Transcrever requisito de raca,
/// custo e pre-requisito a mao garante erro de digitacao, e erro de requisito nao aparece em
/// teste -- aparece quando um jogador nao consegue aprender a skill da propria raca e ninguem
/// entende por que. Reextrair e um comando.
///
/// O EFEITO TAMBEM VEM DAQUI, ate onde ele e dado e nao logica. 85 skills so SOMAM num campo
/// (`Buffs`) e 48 destravam uma habilidade ativa (`Verbos`) -- as duas coisas sao extraidas.
/// O que sobra sao as 200 folhas cujo efeito e um sistema inteiro (estilos de luta, rituais):
/// essas aparecem com <c>SemEfeito</c> ligado, e e assim que da pra medir o que falta em vez
/// de achar que esta tudo pronto.
/// </summary>
public sealed class SkillCatalog
{
	private readonly Dictionary<string, Skill> _porPath = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Arvores que TODO personagem recebe (Body, Mind, Spirit).</summary>
	public List<string> ArvoresDeTodos { get; private set; } = [];

	public Dictionary<string, List<string>> ArvorePorRaca { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, List<string>> ArvorePorClasse { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

	public int Total => _porPath.Count;
	public IEnumerable<Skill> Todas => _porPath.Values;

	public Skill? Get(string path) => _porPath.GetValueOrDefault(path);

	public IEnumerable<Skill> Arvores => _porPath.Values.Where(s => s.Arvore);

	/// <summary>
	/// AS ARVORES DESTE PERSONAGEM. E o `generatetrees()` do original: as tres centrais pra todo
	/// mundo, mais a racial da raca-mae, mais o que a classe acrescentar.
	/// </summary>
	public List<Skill> ArvoresDe(string raca, string classe)
	{
		var l = new List<Skill>();
		void Por(string p) { if (_porPath.TryGetValue(p, out Skill? s) && !l.Contains(s)) l.Add(s); }

		foreach (string p in ArvoresDeTodos) Por(p);
		if (ArvorePorRaca.TryGetValue(raca, out List<string>? r)) foreach (string p in r) Por(p);
		if (ArvorePorClasse.TryGetValue(classe, out List<string>? c)) foreach (string p in c) Por(p);
		return l;
	}

	/// <summary>
	/// Este personagem PODE, em principio, destravar esta skill?
	///
	/// E o `canLearnSkill` + `skillUnlockOK` do original, com a mesma ordem de decisao: skill
	/// desligada nunca; so-vilao pede vilao; `common_sense` libera pra todos; sem lista de raca
	/// NEM de classe tambem libera (a arvore ja e o gate); com lista, tem que casar.
	/// </summary>
	public bool Permitida(Skill s, string raca, string classe, bool vilao)
	{
		if (!s.Ligada) return false;
		if (s.SoVilao && !vilao) return false;
		if (s.Comum) return true;
		if (s.Racas.Length == 0 && s.Classes.Length == 0) return true;
		return s.Racas.Contains(raca, StringComparer.OrdinalIgnoreCase)
			|| s.Classes.Contains(classe, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Os pre-requisitos foram cumpridos?
	///
	/// O original conta ao contrario e vale copiar a conta: `precheck = prereqs.len - folga`, e
	/// cada pre-requisito que voce TEM abate um. Sobrando zero ou menos, passou. Ou seja a folga
	/// nao e "ignore os requisitos", e "de N caminhos, basta cumprir N menos a folga".
	/// </summary>
	public static bool PreReqsOk(Skill s, ICollection<string> aprendidas)
	{
		if (s.PreReqs.Length == 0) return true;
		int falta = s.PreReqs.Length - s.Folga;
		foreach (string p in s.PreReqs) if (aprendidas.Contains(p)) falta--;
		return falta <= 0;
	}

	/// <summary>
	/// O CUSTO em marcos. O original normaliza o custo pelo TIER quando `fixedcost` e 0 -- uma
	/// skill de tier 3 custa mais que uma de tier 1, e e isso que impede comprar o topo da
	/// arvore antes da base.
	/// </summary>
	public static int CustoDe(Skill s) => s.CustoFixo ? s.Custo : Math.Max(1, s.Tier);

	// =====================================================================
	// LEITURA
	// =====================================================================
	public static SkillCatalog Parse(string skillsJson, string arvoresJson)
	{
		var cat = new SkillCatalog();

		foreach (string bloco in Blocos(skillsJson))
		{
			var s = new Skill
			{
				Path = Str(bloco, "path"),
				Nome = Str(bloco, "nome"),
				Desc = Str(bloco, "desc"),
				Tipo = Str(bloco, "tipo"),
				Tier = Num(bloco, "tier", 1),
				Custo = Num(bloco, "custo", 1),
				MaxNivel = Num(bloco, "maxnivel", 3),
				Folga = Num(bloco, "folga", 0),
				Arvore = Num(bloco, "arvore", 0) != 0,
				Comum = Num(bloco, "comum", 0) != 0,
				Ligada = Num(bloco, "ligada", 1) != 0,
				SoVilao = Num(bloco, "vilao", 0) != 0,
				Ensinavel = Num(bloco, "ensinavel", 0) != 0,
				CustoFixo = Num(bloco, "custofixo", 0) != 0,
				Racas = Lista(bloco, "racas"),
				Classes = Lista(bloco, "classes"),
				PreReqs = Lista(bloco, "prereqs"),
				Galhos = Lista(bloco, "galhos"),
				Verbos = Lista(bloco, "verbos"),
				Estilo = Str(bloco, "estilo"),
			};
			Pares(bloco, "buffs", s.Buffs);
			Pares(bloco, "mults", s.Mults);
			Pares(bloco, "genes", s.Genes);
			Pares(bloco, "flags", s.Flags);
			if (s.Path.Length > 0) cat._porPath[s.Path] = s;
		}

		cat.ArvoresDeTodos = [.. Lista(arvoresJson, "todos")];
		cat.ArvorePorRaca = Mapa(arvoresJson, "raca");
		cat.ArvorePorClasse = Mapa(arvoresJson, "classe");
		return cat;
	}

	/// <summary>Le uma lista plana de "chave=valor" pra dentro de um dicionario.</summary>
	private static void Pares(string bloco, string chave, Dictionary<string, double> destino)
	{
		foreach (string item in Lista(bloco, chave))
		{
			int ig = item.IndexOf('=');
			if (ig <= 0) continue;
			if (double.TryParse(item[(ig + 1)..], System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double v))
				destino[item[..ig]] = v;
		}
	}

	/// <summary>Cada `{ ... }` de primeiro nivel. Os valores nao tem chaves dentro, entao basta contar.</summary>
	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int dp = bloco.IndexOf(':', i);
		int a = bloco.IndexOf('"', dp + 1);
		if (a < 0) return "";
		// respeita a barra de escape que o emissor poe em aspas dentro do texto
		var sb = new System.Text.StringBuilder();
		for (int k = a + 1; k < bloco.Length; k++)
		{
			if (bloco[k] == '\\' && k + 1 < bloco.Length) { sb.Append(bloco[++k]); continue; }
			if (bloco[k] == '"') break;
			sb.Append(bloco[k]);
		}
		return sb.ToString();
	}

	private static int Num(string bloco, string chave, int padrao)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return padrao;
		int dp = bloco.IndexOf(':', i) + 1;
		int fim = dp;
		while (fim < bloco.Length && (char.IsDigit(bloco[fim]) || bloco[fim] is ' ' or '-')) fim++;
		return int.TryParse(bloco[dp..fim].Trim(), out int v) ? v : padrao;
	}

	private static string[] Lista(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return [];
		int a = bloco.IndexOf('[', i);
		int b = bloco.IndexOf(']', a + 1);
		if (a < 0 || b < 0) return [];
		var l = new List<string>();
		string dentro = bloco[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			l.Add(dentro[(q1 + 1)..q2]);
			k = q2 + 1;
		}
		return [.. l];
	}

	/// <summary>`"raca": { "Saiyan": [...], "Human": [...] }`</summary>
	private static Dictionary<string, List<string>> Mapa(string json, string chave)
	{
		var d = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		int i = json.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return d;
		int a = json.IndexOf('{', i);
		int b = json.IndexOf('}', a + 1);
		if (a < 0 || b < 0) return d;

		string dentro = json[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			string nome = dentro[(q1 + 1)..q2];

			int c1 = dentro.IndexOf('[', q2);
			int c2 = dentro.IndexOf(']', c1 + 1);
			if (c1 < 0 || c2 < 0) break;

			var l = new List<string>();
			int p = c1;
			while (true)
			{
				int r1 = dentro.IndexOf('"', p + 1);
				if (r1 < 0 || r1 > c2) break;
				int r2 = dentro.IndexOf('"', r1 + 1);
				if (r2 < 0 || r2 > c2) break;
				l.Add(dentro[(r1 + 1)..r2]);
				p = r2;
			}
			d[nome] = l;
			k = c2 + 1;
		}
		return d;
	}
}
