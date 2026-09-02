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

	/// <summary>
	/// O `teacher` do DM: quem sabe esta skill pode ENSINA-LA. E a lista que o `Teach_Skill`
	/// (`teachable.dm:9-10`) monta -- 83 skills no catalogo de hoje.
	/// </summary>
	public bool Ensinavel;

	/// <summary>
	/// `teachCost` -- quantos marcos o ALUNO precisa ter pra ser ensinado. 0 = nao declarado, e ai
	/// vale o custo normal. Ver <see cref="SkillCatalog.CustoDoEnsino"/>.
	/// </summary>
	public int CustoDeEnsino;

	public bool CustoFixo;

	/// <summary>`can_forget`: da pra esquecer -- e e o que a arvore encolhendo pode tirar de volta.</summary>
	public bool Esquecivel = true;

	/// <summary>
	/// SO NAS ARVORES: o `allowedtier` de nascenca e o `maxtier` (`trees.dm:10-11`). A vitrine do DM
	/// so mostra skill com `tier &lt;= allowedtier` (`HtmlUI.dm:820`, `SkillTreesWindow.dm:169`), e o
	/// `allowedtier` sobe pelo `growbranches()` -- as <see cref="RegrasDeGalho"/>.
	/// </summary>
	public int TierInicial = 1;
	public int TierMax = 1;

	/// <summary>SO NAS ARVORES: o `growbranches()` traduzido em dado (`tipo;alvo;condicao`), na ordem do DM.</summary>
	public string[] Regras = [];

	/// <summary>As mesmas regras, ja lidas. Ver <see cref="RegraDeArvore"/>.</summary>
	public RegraDeArvore[] RegrasDeGalho = [];

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

	/// <summary>
	/// A ESCOLHA UNICA: casas MUTUAMENTE EXCLUSIVAS de efeito. Vazio em 316 das 317 folhas.
	///
	/// Uma skill no jogo inteiro faz isto -- a `Great Robotic Alliance` do Metamoriano
	/// (`meta.dm:104-125`): ao aprender, o DM abre um `input()` de tres casas e o dono fica com
	/// UMA. Somar as tres daria os tres conjuntos de uma vez; por isso elas nao moram em
	/// <see cref="Buffs"/>, e por isso a skill precisa da escolha registrada pra valer alguma
	/// coisa (ver <see cref="EfeitosDeSkill"/>).
	/// </summary>
	public Escolha[] Escolhas = [];

	/// <summary>
	/// A SKILL CUJA ESCOLHA ESTA SEGUE (typepath). `Grace` nao pergunta: ela abre
	/// `switch(savant.trinitytype)` (Bodybuilding.dm:243) e entra na casa que `TheHolyTrinity`
	/// escolheu. O extrator casa as duas pelos rotulos das casas; o efeito le a casa da lider --
	/// ver <see cref="EfeitosDeSkill.CasaEscolhida"/>. Vazio pra quem escolhe sozinha.
	/// </summary>
	public string EscolhaSegue = "";

	/// <summary>
	/// O GANHO NA COMPRA COM EXPRESSAO: `BP += max(1, BP*0.01)`, `hiddenpotential += relBPmax*2`
	/// (One_Hundred, Bodybuilding.dm:89-92; One_Punch/One_Training com `relBPmax*0.5`). Nao e buff
	/// (nao se reaplica no login: e um ganho que o corpo ABSORVE uma vez e devolve ao esquecer), e o
	/// valor depende da ficha no instante da compra. Ver <see cref="GanhoNaCompra"/>.
	/// </summary>
	public GanhoNaCompra[] Compra = [];

	/// <summary>
	/// Uma folha que nao soma nada nem destrava nada AINDA nao tem efeito portado.
	///
	/// A ESCOLHA CONTA COMO EFEITO mesmo antes de o dono escolher: o que o censo mede aqui e se
	/// o PORT sabe o que a skill faz, nao se um personagem especifico ja decidiu. Enquanto isto
	/// nao contava, a `Great Robotic Alliance` aparecia na mesma coluna que uma skill cujo efeito
	/// ninguem leu -- e as duas coisas pedem trabalho oposto.
	/// </summary>
	public bool SemEfeito => !Arvore && Buffs.Count == 0 && Verbos.Length == 0 && Estilo.Length == 0
		&& Mults.Count == 0 && Genes.Count == 0 && Flags.Count == 0 && Escolhas.Length == 0
		&& Compra.Length == 0;
}

/// <summary>
/// UMA CASA da escolha unica. Os canais sao os mesmos da <see cref="Skill"/> pelo mesmo motivo:
/// somar num campo que deveria multiplicar so da o numero certo quando a base e 1.
/// </summary>
public sealed class Escolha
{
	public string Rotulo = "";
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);
	public string[] Verbos = [];
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
		if (ArvorePorRaca.TryGetValue(NomeDoDm(raca), out List<string>? r)) foreach (string p in r) Por(p);
		if (ArvorePorClasse.TryGetValue(classe, out List<string>? c)) foreach (string p in c) Por(p);
		return l;
	}

	/// <summary>
	/// ============================ A CHAVE DESTE ARQUIVO E A GRAFIA DO **DM**, E A DA FICHA NAO E ============================
	/// `skilltrees.json` e GERADO pelo `AssetPipeline` a partir do `mobhandlers.dm`, entao as chaves
	/// dele sao os nomes do original: `"Bio-Android"`, `"Frost Demon"`, `"Saibamen"`, `"Spirit Doll"`.
	/// O `races.json` gravou a folha do typepath: `"BioAndroid"`, `"Icer"`, `"Saibaman"`,
	/// `"SpiritDoll"`. O `Fighter.Race` fala a segunda lingua.
	///
	/// **QUATRO RACAS ESTAVAM SEM ARVORE RACIAL, EM SILENCIO.** O `TryGetValue` nao casava, o
	/// `ArvoresDe` devolvia so as tres centrais (Body/Mind/Spirit) e a aba de aprendizado abria com o
	/// ramo da raca simplesmente ausente -- nao vazio nem cinza: ausente. Nenhuma das duas pontas
	/// tinha como notar, porque cada uma so conhece a propria grafia.
	///
	/// O BURACO APARECEU CACANDO O BIO-ANDROIDE (a arvore `bioandroid` traz a `Tail Absorb`, que e o
	/// motor de evolucao dele), e o conserto e o mesmo para os quatro de proposito: consertar so um
	/// deixaria tres bugs identicos vivos e um precedente dizendo que isso e normal.
	///
	/// A TRADUCAO MORA AQUI e nao numa copia por chamador porque quem le a chave e ESTE arquivo. O
	/// `Permitida` logo abaixo continua comparando a grafia da ficha contra `Skill.Racas` -- e certo:
	/// aquela lista sai do MESMO extrator, mas por outro campo, e mexer nela sem medir trocaria um
	/// buraco por outro. Ela esta anotada no relatorio como divida separada.
	/// ==========================================================================================================================
	/// </summary>
	private static string NomeDoDm(string raca) => raca switch
	{
		Races.BioAndroids.Raca => Races.BioAndroids.RacaDoDm,
		Races.FormasDeFrost.Raca => Races.FormasDeFrost.ClasseNormal,   // "Icer" -> "Frost Demon"
		"Saibaman" => "Saibamen",
		"SpiritDoll" => "Spirit Doll",
		_ => raca,
	};

	/// <summary>
	/// Este personagem PODE, em principio, destravar esta skill?
	///
	/// E o `skillUnlockOK` do original (`Skills Master/mobhandlers.dm:59`): so-vilao pede vilao;
	/// `common_sense` libera pra todos; sem lista de raca NEM de classe tambem libera (a arvore ja e
	/// o gate); com lista, tem que casar.
	///
	/// O `enabled` NAO MORA AQUI. Ele morava (`if (!s.Ligada) return false`), e era o defeito de
	/// origem da progressao inteira: `enabled = 0` no DM nao e "desligada", e "trancada ATE o
	/// pre-requisito entrar" (`skill.dm:26`, "set to 0 and modify with other skills to establish
	/// prereqs") -- quem o acende e o `testskillprereqs()` (`trees.dm:28-40`) e os `enableskill()`
	/// dos `growbranches()`. Quem le isso agora e o <see cref="SkillBook.Avaliar"/>, com o estado
	/// da arvore na mao.
	/// </summary>
	public bool Permitida(Skill s, string raca, string classe, bool vilao)
	{
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

	/// <summary>
	/// O CUSTO **DE SER ENSINADO** -- `teachCost`, e o `skillcost` quando ele nao existe. Copia
	/// literal do `Study` (`teachable.dm:43-45`).
	///
	/// ============================ POR QUE O RAMO "SEM teachCost" CAI NO <see cref="CustoDe"/> ============================
	/// La o codigo diz `teachingcost = S.skillcost`, e o `skillcost` do DM **ja chega normalizado
	/// pelo tier**: quem faz isso e o `/datum/skill/New()` (`skill.dm:31`, *"house rule: a skill's
	/// Milestone cost equals its tier"*), que roda antes de qualquer um poder ler o campo. Ou seja
	/// `S.skillcost` em tempo de execucao E o <see cref="CustoDe"/>, e ler `Skill.Custo` cru aqui
	/// devolveria o numero ESCRITO no arquivo -- que em 300 skills e o `1` do padrao, e nao o tier.
	/// ==============================================================================================================
	/// </summary>
	public static int CustoDoEnsino(Skill s) => s.CustoDeEnsino > 0 ? s.CustoDeEnsino : CustoDe(s);

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
				CustoDeEnsino = Num(bloco, "custoensino", 0),
				CustoFixo = Num(bloco, "custofixo", 0) != 0,
				Esquecivel = Num(bloco, "esquecivel", 1) != 0,
				TierInicial = Num(bloco, "tierinicial", 1),
				TierMax = Num(bloco, "tiermax", 1),
				Regras = Lista(bloco, "regras"),
				Racas = Lista(bloco, "racas"),
				Classes = Lista(bloco, "classes"),
				PreReqs = Lista(bloco, "prereqs"),
				Galhos = Lista(bloco, "galhos"),
				Verbos = Lista(bloco, "verbos"),
				Estilo = Str(bloco, "estilo"),
				EscolhaSegue = Str(bloco, "escolhasegue"),
			};
			Pares(bloco, "buffs", s.Buffs);
			Pares(bloco, "mults", s.Mults);
			Pares(bloco, "genes", s.Genes);
			Pares(bloco, "flags", s.Flags);
			s.Escolhas = [.. Lista(bloco, "escolhas").Select(Casa)];
			// o ganho que nao parseia cai fora AQUI (e nao na compra): uma expressao que o Core nao
			// le nao pode virar um `+=` de zero em silencio
			s.Compra = [.. Lista(bloco, "compra").Select(GanhoNaCompra.Parse).Where(g => g != null)!];
			// regra que o Core nao entende (tipo desconhecido) cai fora aqui, e nao na avaliacao:
			// o `growbranches()` de uma arvore nunca pode derrubar a compra das outras
			s.RegrasDeGalho = [.. s.Regras.Select(RegraDeArvore.Parse).Where(r => r != null)!];
			if (s.Path.Length > 0) cat._porPath[s.Path] = s;
		}

		cat.ArvoresDeTodos = [.. Lista(arvoresJson, "todos")];
		cat.ArvorePorRaca = Mapa(arvoresJson, "raca");
		cat.ArvorePorClasse = Mapa(arvoresJson, "classe");
		return cat;
	}

	/// <summary>
	/// Le uma casa da escolha unica: "rotulo|campo=valor|*campo=valor|g:stat=valor|!campo=valor|v:verbo".
	///
	/// FORMATO PLANO pelo mesmo motivo do resto do arquivo: o leitor daqui fatia o JSON por
	/// `{`..`}` de primeiro nivel, e uma chave dentro do bloco quebraria TODA skill dali pra
	/// frente -- em silencio. O prefixo diz o canal; sem prefixo, e o aditivo.
	/// </summary>
	private static Escolha Casa(string cru)
	{
		string[] p = cru.Split('|');
		var e = new Escolha { Rotulo = p.Length > 0 ? p[0] : "" };
		var verbos = new List<string>();
		for (int i = 1; i < p.Length; i++)
		{
			string item = p[i];
			if (item.StartsWith("v:", StringComparison.Ordinal)) { verbos.Add(item[2..]); continue; }
			int ig = item.IndexOf('=');
			if (ig <= 0) continue;
			if (!double.TryParse(item[(ig + 1)..], System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double v)) continue;
			string campo = item[..ig];
			if (campo.StartsWith('*')) e.Mults[campo[1..]] = v;
			else if (campo.StartsWith("g:", StringComparison.Ordinal)) e.Genes[campo[2..]] = v;
			else if (campo.StartsWith('!')) e.Flags[campo[1..]] = v;
			else e.Buffs[campo] = v;
		}
		e.Verbos = [.. verbos];
		return e;
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

	// ============================ OS TRES LEITORES SAO `internal`, E NAO PRIVADOS ============================
	// O `MarcoDoCatalogo` le um json com a MESMA forma (lista plana de blocos sem chave aninhada), e
	// escrever um segundo leitor la seria a segunda casa da mesma regra -- a armadilha 4 da casa
	// ("dois lados calculando a mesma coisa"), so que aplicada a formato de arquivo: o dia em que
	// alguem ensinasse este leitor a escapar uma sequencia nova, o outro continuaria sem saber.
	// `internal` e nao `public` porque isto e detalhe do Core e nao contrato de ninguem de fora.
	// =====================================================================================================

	/// <summary>Cada `{ ... }` de primeiro nivel. Os valores nao tem chaves dentro, entao basta contar.</summary>
	internal static IEnumerable<string> Blocos(string s)
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

	internal static string Str(string bloco, string chave)
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

	internal static int Num(string bloco, string chave, int padrao)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return padrao;
		int dp = bloco.IndexOf(':', i) + 1;
		int fim = dp;
		while (fim < bloco.Length && (char.IsDigit(bloco[fim]) || bloco[fim] is ' ' or '-')) fim++;
		return int.TryParse(bloco[dp..fim].Trim(), out int v) ? v : padrao;
	}

	internal static string[] Lista(string bloco, string chave)
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
