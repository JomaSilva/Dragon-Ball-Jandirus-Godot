using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Uma skill, do jeito que o jogo precisa saber dela pra ensinar e cobrar.</summary>
public sealed class SkillDef
{
	public string Path = "";          // "/datum/skill/Assassain/Backstab"
	public string Nome = "";
	public string Desc = "";
	public string Tipo = "Physical";  // Physical | Ki | Magic -- o `skilltype` do DM
	public int Tier = 1;
	public int Custo = 1;             // skillcost
	public int MaxNivel = 3;
	public bool CustoFixo;            // fixedcost: NAO normaliza o custo pelo tier
	public bool Comum;                // common_sense: qualquer um aprende
	public bool Ligada = true;        // enabled
	public bool SoVilao;              // villainonly
	public bool Ensinavel;            // teacher
	public bool Arvore;               // e um /datum/skill/tree/... (o galho, nao a folha)
	public List<string> Racas = [];
	public List<string> Classes = [];
	public List<string> PreReqs = [];
	public List<string> Constituintes = [];   // so nas arvores: as skills que penduram nela

	/// <summary>
	/// O QUE A SKILL FAZ AO SER APRENDIDA: campo -> quanto soma. Extraido do corpo de
	/// `after_learn()`, onde 333 skills fazem exatamente `savant.physoffBuff += 0.1`.
	/// </summary>
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);

	/// <summary>
	/// AS HABILIDADES ATIVAS que a skill destrava, pelo nome do verb do DM
	/// (`assignverb(/mob/keyable/verb/Solar_Flare)` -> `Solar_Flare`).
	/// </summary>
	public List<string> Verbos = [];

	/// <summary>
	/// BUFF MULTIPLICATIVO (`savant.MedMod *= 2`). Canal SEPARADO do aditivo de proposito: no
	/// DM os campos que levam `*=` sao modificadores que nascem em 1 (MedMod, kicapacityMod,
	/// PDrainMod), e somar 2 num campo que deveria DOBRAR e um erro que passa despercebido --
	/// da o numero certo quando o valor de base e 1 e o numero errado em todo o resto.
	/// </summary>
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);

	/// <summary>
	/// O SEGUNDO CANAL DE BUFF PERMANENTE: `savant.genome.add_to_stat("Energy Level", 0.1)`.
	/// Nao passa pelos campos `*Buff` -- mexe nos MODIFICADORES da genetica e forca o recalculo.
	/// 23 skills usam isto, e um extrator que so olhasse `savant.<campo> +=` perderia todas.
	/// </summary>
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);

	/// <summary>
	/// ATRIBUICAO DIRETA (`savant.can_sup_el = 1`): flags de capacidade e contadores de arvore
	/// como `bodyskill`/`bodyreadiness` -- que NAO sao mortos, sao o gate de quatro arvores
	/// (Body.dm:24-44). Guardado a parte do aditivo porque a semantica e SETAR, nao somar.
	/// </summary>
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);

	/// <summary>
	/// O ESTILO DE LUTA que a skill concede (`attachedstyle = new /datum/style/KameStyle`).
	/// Vazio na esmagadora maioria: so nove skills em todo o jogo penduram estilo.
	/// </summary>
	public string Estilo = "";
	public int PreReqFolga;                   // prereqthreshold: quantos prereqs da pra pular
	public string? Pai;

	/// <summary>
	/// `expbarrier`: quanto exp custa UM nivel. Preenchido pelo <see cref="DmEffectorScanner"/>,
	/// que ja varre os mesmos arquivos pra ler o `effector()`. 0 = a skill nao declarou, e o DM
	/// cai no padrao 100 (Skills Master/skill.dm:9).
	///
	/// SEM ISTO TODA SKILL SOBE NA BARREIRA 100: as arvores de Ki pedem 5.000 (Ki Trees/
	/// Effusion.dm:36) e crescem 3% por nivel dali em diante, entao o erro nao e de margem -- e
	/// de cinquenta vezes, e sempre a favor do jogador.
	/// </summary>
	public int ExpBarreira;
}

/// <summary>
/// Le as 709 skills direto da arvore de tipos do DM.
///
/// POR QUE EXTRAIR E NAO TRANSCREVER: sao setecentas. Escrever isso a mao garante erro de
/// digitacao em requisito de raca, em custo e em pre-requisito -- e erro de requisito nao
/// aparece em teste, aparece meses depois quando alguem nao consegue aprender a skill da
/// propria raca. O DM e a fonte de verdade; o que sai daqui e uma COPIA MECANICA dela.
///
/// A MESMA ARMADILHA DO SCANNER DE TURF, e vale repetir: a arvore do DM e por INDENTACAO, e
/// dentro de um bloco de skill ha CORPOS DE PROC (`after_learn()`, `effector()`) que tambem sao
/// blocos indentados e que atribuem coisas. `savant.physoffBuff += 0.1` dentro de `after_learn()`
/// nao e propriedade da skill. Toda subarvore de um identificador que termina em "(...)" e
/// ignorada.
///
/// O EFEITO PASSIVO SAI DAQUI TAMBEM. A maioria esmagadora das skills nao "faz" nada: ela SOMA.
/// 333 corpos de `after_learn()` sao literalmente `savant.physoffBuff += 0.1` e mais nada, e isso e
/// dado, nao logica -- vai pra `Buffs`. O que continua fora sao as skills ATIVAS (Kaio-ken, Sense,
/// Zanzoken): essas tem corpo de verdade e sao escritas a mao em <c>Server/GameServer.Skills.cs</c>.
/// </summary>
public static class DmSkillScanner
{
	/// <summary>`savant.physoffBuff += 0.1` -- a forma dos efeitos de aprendizado.</summary>
	private static readonly Regex RxBuff = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+=|-=)\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>`savant.MedMod *= 2` -- buff MULTIPLICATIVO, outro canal.</summary>
	private static readonly Regex RxMult = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\*=|/=)\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>`savant.kioffBuff++` -- o mesmo que `+= 1`, escrito curto.</summary>
	private static readonly Regex RxIncr = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+\+|--)", RegexOptions.Compiled);

	/// <summary>`savant.genome.add_to_stat(""Energy Level"", 0.1)` -- buff pela genetica.</summary>
	private static readonly Regex RxGene = new(
		@"genome\.(?<op>add_to_stat|sub_to_stat)\(\s*""(?<s>[^""]+)""\s*,\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>`savant.contents += new/obj/SplitForm` -- o SEGUNDO canal de habilidade ativa.</summary>
	private static readonly Regex RxObj = new(
		@"savant\.contents\s*\+=\s*new\s*/obj/(?:[A-Za-z0-9_]+/)*(?<o>[A-Za-z0-9_]+)", RegexOptions.Compiled);

	/// <summary>`savant.can_sup_el = 1` -- atribuicao direta (flag ou contador de arvore).</summary>
	private static readonly Regex RxSet = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>-?[0-9.]+)\s*$", RegexOptions.Compiled);

	/// <summary>`assignverb(/mob/keyable/verb/Solar_Flare)` -- a forma das skills ATIVAS.</summary>
	/// <summary>
	/// `/datum/skill/general/Hardened_Body/after_learn()` -- a SEGUNDA forma de declarar o
	/// efeito, com o caminho inteiro na linha em vez de aninhado no bloco da skill.
	///
	/// O DM aceita as duas e o jogo usa as duas: 87 skills declaram aninhado e CENTO E
	/// DEZESSEIS declaram assim. Eu so enxergava a primeira forma, e por isso dava skills como
	/// "Hardened Body" (cuja descricao promete `P.Def+++`) como sem efeito nenhum -- ela tem
	/// `physdefBuff += 0.2`, so que quatro linhas abaixo do bloco, num typepath proprio.
	/// </summary>
	private static readonly Regex RxLearnSolto = new(
		@"^(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)+)/after_learn\(\)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxVerb = new(
		@"assignverb\(\s*/(?:[A-Za-z0-9_]+/)*verb/(?<v>[A-Za-z0-9_]+)", RegexOptions.Compiled);

	private static readonly Regex RxProp = new(
		@"^(?<ind>\t*)(?<k>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>.+?)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxTypePath = new(
		@"^(?<ind>\t*)(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)*)\s*$", RegexOptions.Compiled);

	/// <summary>`list("Saiyan","Human")` e `list(/datum/skill/a, new/datum/skill/b)`.</summary>
	private static readonly Regex RxItem = new(
		"\"(?<s>[^\"]*)\"|(?<p>/?(?:new\\s*/)?datum/skill(?:/[A-Za-z0-9_]+)+)", RegexOptions.Compiled);

	public static Dictionary<string, SkillDef> Scan(string pastaCode)
	{
		var defs = new Dictionary<string, SkillDef>(StringComparer.OrdinalIgnoreCase);

		foreach (string arq in Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories))
			Ler(arq, defs);

		// HERANCA: `/datum/skill/Assassain/Backstab` herda de `/datum/skill/Assassain` se ele
		// existir, senao de `/datum/skill`. Sem isto, uma skill que so declara `name` e `tier`
		// perde raca, classe e tipo do pai -- e vira inaprendivel pra quem deveria poder.
		foreach (SkillDef d in defs.Values)
		{
			int corte = d.Path.LastIndexOf('/');
			if (corte <= 0) continue;
			string pai = d.Path[..corte];
			if (defs.ContainsKey(pai)) d.Pai = pai;
		}
		foreach (SkillDef d in defs.Values) Herdar(d, defs, 0);

		return defs;
	}

	private static void Herdar(SkillDef d, Dictionary<string, SkillDef> defs, int prof)
	{
		if (prof > 12 || d.Pai == null || !defs.TryGetValue(d.Pai, out SkillDef? p)) return;
		Herdar(p, defs, prof + 1);
		if (d.Racas.Count == 0) d.Racas = [.. p.Racas];
		if (d.Classes.Count == 0) d.Classes = [.. p.Classes];
		if (d.Desc.Length == 0) d.Desc = p.Desc;
	}

	/// <summary>
	/// Le um arquivo .dm reconstruindo a arvore de tipos POR INDENTACAO.
	///
	/// O DM aceita as duas formas, e o jogo usa as duas:
	///
	///     /datum/skill/Assassain/Backstab      <- caminho absoluto, uma linha
	///     /datum/skill/tree                    <- ...e caminho RELATIVO, aninhado:
	///         meta                                  vira /datum/skill/tree/meta
	///             name = "Meta Racials"
	///
	/// A primeira versao so entendia a forma absoluta e perdia blocos inteiros em silencio --
	/// a arvore racial dos Metamorianos, por exemplo, some do catalogo e aquele povo fica sem
	/// progressao nenhuma sem que nada acuse. Uma pilha por nivel de indentacao resolve os dois
	/// casos com o mesmo codigo.
	/// </summary>
	private static void Ler(string arq, Dictionary<string, SkillDef> defs)
	{
		string[] linhas = File.ReadAllLines(arq);

		// pilha[i] = o typepath que abre no nivel de indentacao i
		var pilha = new string[64];
		int indProc = -1;

		// O `after_learn()` E A EXCECAO A REGRA DE PULAR CORPO DE PROC: e nele que mora o EFEITO da
		// skill, e em 333 delas o efeito e mecanico -- `savant.physoffBuff += 0.1`. E o unico corpo
		// de proc que da pra ler sem interpretar DM. (O `before_forget()` e o espelho negativo do
		// mesmo; ignorado de proposito -- desaprender e reconstruir a soma do zero.)
		int indLearn = -1;      // indentacao da linha `after_learn(...)` aberta
		string? donoLearn = null;

		// DENTRO DE UM /* */? O DM tem codigo morto de verdade guardado em bloco comentado -- dez
		// definicoes de `after_learn` e uma skill de estilo inteira. Sem isto o catalogo do jogo
		// ganha skills que NAO EXISTEM em runtime, e o pior tipo delas: a que parece funcionar.
		bool comentado = false;

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i].TrimEnd();
			if (linha.Length == 0) continue;

			int ind = 0;
			while (ind < linha.Length && linha[ind] == '\t') ind++;
			if (ind >= pilha.Length - 1) continue;
			string corpo = linha[ind..];
			if (corpo.StartsWith("/*", StringComparison.Ordinal)) comentado = true;
			if (comentado)
			{
				if (corpo.Contains("*/", StringComparison.Ordinal)) comentado = false;
				continue;
			}
			if (corpo.Length == 0 || corpo.StartsWith("//", StringComparison.Ordinal)) continue;

			// COMENTARIO COLADO NO FIM DA LINHA. O DM permite `/datum/skill/mind/Ki_Unlocked//nota`
			// SEM espaco nenhum, e o teste de typepath la embaixo (so letra, digito, `_` e `/`)
			// reprovava a linha inteira -- o bloco sumia calado.
			//
			// O QUE ISSO CUSTOU: a `Ki Unlocked` e a RAIZ da arvore de Ki, e e ela que entrega o
			// Sense (nivel 5), o Kiai (10), o voo (30) e o Basic Blast (35). Ela nao estava no
			// catalogo, e nada acusava: a arvore aparecia, so que sem a porta de entrada.
			//
			// Nao da pra cortar isso antes do teste de bloco `/* */` -- uma linha pode abrir bloco
			// depois do codigo. Por isso vem aqui, ja passada aquela guarda.
			int duasBarras = corpo.IndexOf("//", StringComparison.Ordinal);
			if (duasBarras > 0) corpo = corpo[..duasBarras].TrimEnd();
			if (corpo.Length == 0) continue;

			if (indLearn >= 0 && ind <= indLearn) { indLearn = -1; donoLearn = null; }
			if (indProc >= 0 && ind <= indProc) indProc = -1;

			// DENTRO do after_learn: so colhe buff e SEGUE. Nao sai daqui por causa de um `if(...)`
			// -- o corpo tem condicionais, e tratar cada uma como "outro proc" fazia perder tudo
			// depois da primeira linha de logica. Quem fecha o bloco e a indentacao, so ela.
			if (indLearn >= 0)
			{
				if (donoLearn != null && defs.TryGetValue(donoLearn, out SkillDef? alvo))
				{
					foreach (Match mb in RxBuff.Matches(corpo))
					{
						if (!double.TryParse(mb.Groups["v"].Value, NumberStyles.Float,
								CultureInfo.InvariantCulture, out double vb)) continue;
						double sinal = mb.Groups["op"].Value == "-=" ? -1 : 1;
						string campo = mb.Groups["c"].Value;
						alvo.Buffs[campo] = alvo.Buffs.GetValueOrDefault(campo) + vb * sinal;
					}
					foreach (Match mm in RxMult.Matches(corpo))
					{
						if (!double.TryParse(mm.Groups["v"].Value, NumberStyles.Float,
								CultureInfo.InvariantCulture, out double vm) || vm == 0) continue;
						string campo = mm.Groups["c"].Value;
						double fator = mm.Groups["op"].Value == "/=" ? 1 / vm : vm;
						// COMPOEM, nao substitui: duas skills que dobram o mesmo campo dao 4x.
						alvo.Mults[campo] = alvo.Mults.GetValueOrDefault(campo, 1) * fator;
					}
					foreach (Match mi in RxIncr.Matches(corpo))
					{
						string campo = mi.Groups["c"].Value;
						double d = mi.Groups["op"].Value == "--" ? -1 : 1;
						alvo.Buffs[campo] = alvo.Buffs.GetValueOrDefault(campo) + d;
					}
					foreach (Match mg in RxGene.Matches(corpo))
					{
						if (!double.TryParse(mg.Groups["v"].Value, NumberStyles.Float,
								CultureInfo.InvariantCulture, out double vg)) continue;
						double sinal = mg.Groups["op"].Value == "sub_to_stat" ? -1 : 1;
						string stat = mg.Groups["s"].Value;
						alvo.Genes[stat] = alvo.Genes.GetValueOrDefault(stat) + vg * sinal;
					}
					foreach (Match ms in RxSet.Matches(corpo))
					{
						if (double.TryParse(ms.Groups["v"].Value, NumberStyles.Float,
								CultureInfo.InvariantCulture, out double vs))
							alvo.Flags[ms.Groups["c"].Value] = vs;
					}
					foreach (Match mv in RxVerb.Matches(corpo))
					{
						string v = mv.Groups["v"].Value;
						if (!alvo.Verbos.Contains(v)) alvo.Verbos.Add(v);
					}
					// O SEGUNDO CANAL DE VERB. Em BYOND, um obj dentro de `mob.contents` empresta os
					// verbs dele ao dono -- entao `contents += new/obj/SplitForm` concede uma
					// habilidade sem passar por `assignverb`. Doze skills fazem so isso, e um
					// extrator que so olhasse `assignverb` as daria como "sem efeito".
					if (DmStyleScanner.RxAtacha.Match(corpo) is { Success: true } me)
					{
						// `new` SEM tipo usa o tipo declarado no var da skill -- e o caso da Martial
						// Arts, cujo `attachedstyle` ja e um /datum/style/BasicStyle. Sem este
						// fallback a unica skill de estilo que TODO mundo pega sairia sem estilo.
						string idE = me.Groups["id"].Value;
						alvo.Estilo = idE.Length > 0 ? idE : "BasicStyle";
					}
					foreach (Match mo in RxObj.Matches(corpo))
					{
						string v = mo.Groups["o"].Value;
						if (!alvo.Verbos.Contains(v)) alvo.Verbos.Add(v);
					}
				}
				continue;
			}

			if (indProc >= 0) continue;   // corpo de proc que nao interessa

			// linha de continuacao do DM (barra invertida no fim)
			while (corpo.EndsWith('\\') && i + 1 < linhas.Length) corpo = corpo[..^1] + linhas[++i].Trim();

			// --- propriedade ---
			int igual = corpo.IndexOf('=');
			bool ehProp = igual > 0 && corpo[igual - 1] is not ('=' or '!' or '<' or '>' or '+' or '-' or '*' or '/');
			if (ehProp)
			{
				string dono = ind > 0 ? pilha[ind - 1] ?? "" : "";
				if (dono.Length > 0 && defs.TryGetValue(dono, out SkillDef? d))
					Aplicar(d, corpo[..igual].Trim(), corpo[(igual + 1)..].Trim());
				continue;
			}

			// --- corpo de proc: `after_learn()`, `login(mob/x)`, `growbranches()` ---
			if (corpo.EndsWith(')') && corpo.Contains('('))
			{
				// a forma de TOPO: o caminho da skill vem na propria linha
				if (RxLearnSolto.Match(corpo) is { Success: true } ml)
				{
					indLearn = ind;
					donoLearn = Normalizar(ml.Groups["p"].Value);
				}
				else if (corpo.StartsWith("after_learn(", StringComparison.Ordinal) && ind > 0)
				{
					indLearn = ind;
					donoLearn = pilha[ind - 1];
				}
				else indProc = ind;
				continue;
			}

			// --- segmento de typepath ---
			// so aceita o que PARECE typepath: letras, digitos, `_` e `/`. Isso corta linhas de
			// codigo solto (`..()`, `return`, `if(...)`) que caem aqui sem `=` e sem `()`.
			if (!corpo.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '/')) continue;

			// ABSOLUTO SEM A BARRA. O DM aceita `datum/skill/tree/Body` no comeco da linha, sem
			// a barra inicial, e o jogo usa as duas formas -- as arvores centrais (Body, Mind,
			// Spirit) sao justamente assim. Tratar isso como segmento relativo fazia elas
			// sumirem do catalogo, e sao as arvores que TODO personagem recebe.
			string pai = ind > 0 ? pilha[ind - 1] ?? "" : "";
			bool absoluto = corpo.StartsWith('/') || corpo.StartsWith("datum/", StringComparison.Ordinal);
			string caminho = absoluto ? Normalizar(corpo)
						   : pai.Length > 0 ? pai + "/" + corpo.Trim('/')
						   : "";
			if (caminho.Length == 0) continue;

			pilha[ind] = caminho;
			for (int k = ind + 1; k < pilha.Length; k++) pilha[k] = null!;

			// `proc` e `verb` sao ramos do MOTOR, nao skills; e `/datum/skill/tree/proc/x` nao
			// e uma arvore chamada "proc"
			if (caminho.Contains("/proc/") || caminho.Contains("/verb/") || caminho.EndsWith("/proc") || caminho.EndsWith("/verb"))
				continue;
			if (!caminho.StartsWith("/datum/skill", StringComparison.OrdinalIgnoreCase)) continue;

			if (!defs.ContainsKey(caminho))
				defs[caminho] = new SkillDef
				{
					Path = caminho,
					Arvore = caminho.StartsWith("/datum/skill/tree", StringComparison.OrdinalIgnoreCase),
				};
		}
	}

	private static void Aplicar(SkillDef d, string chave, string valor)
	{
		// tira o `var/` de declaracoes com valor e comentarios de fim de linha
		valor = Regex.Replace(valor, @"\s//.*$", "").Trim();
		string cru = valor.Trim('"');

		switch (chave.ToLowerInvariant())
		{
			case "name": d.Nome = cru; break;
			case "desc": d.Desc = cru; break;
			case "skilltype": d.Tipo = cru; break;
			case "tier": if (int.TryParse(cru, out int t)) d.Tier = t; break;
			case "skillcost": if (int.TryParse(cru, out int c)) d.Custo = c; break;
			case "maxlevel": if (int.TryParse(cru, out int ml)) d.MaxNivel = ml; break;
			case "prereqthreshold": if (int.TryParse(cru, out int pt)) d.PreReqFolga = pt; break;
			case "fixedcost": d.CustoFixo = cru is "1" or "TRUE"; break;
			case "common_sense": d.Comum = cru is "1" or "TRUE"; break;
			case "enabled": d.Ligada = cru is not ("0" or "FALSE"); break;
			case "villainonly": d.SoVilao = cru is "1" or "TRUE"; break;
			case "teacher": d.Ensinavel = cru is "1" or "TRUE"; break;
			case "compatible_races": d.Racas = Textos(valor); break;
			case "compatible_classes": d.Classes = Textos(valor); break;
			case "prereqs": d.PreReqs = Caminhos(valor); break;
			case "constituentskills": d.Constituintes = Caminhos(valor); break;
		}
	}

	private static List<string> Textos(string v)
	{
		var l = new List<string>();
		foreach (Match m in RxItem.Matches(v))
			if (m.Groups["s"].Success && m.Groups["s"].Value.Length > 0) l.Add(m.Groups["s"].Value);
		return l;
	}

	private static List<string> Caminhos(string v)
	{
		var l = new List<string>();
		foreach (Match m in RxItem.Matches(v))
			if (m.Groups["p"].Success) l.Add(Normalizar(m.Groups["p"].Value));
		return l;
	}

	private static string Normalizar(string p)
	{
		p = p.Replace("new", "").Replace(" ", "").Trim();
		return p.StartsWith('/') ? p : "/" + p;
	}

	// =====================================================================
	// QUEM GANHA QUAL ARVORE
	// =====================================================================
	/// <summary>Arvores que TODO MUNDO recebe, e as que vem por raca ou por classe.</summary>
	public sealed class Concessoes
	{
		public List<string> Todos = [];
		public Dictionary<string, List<string>> PorRaca = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, List<string>> PorClasse = new(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Le o `generatetrees()` (Skill Trees/mobhandlers.dm:43) e transcreve QUEM GANHA O QUE.
	///
	/// E um proc, nao uma tabela -- mas e um proc com uma forma so, repetida vinte e cinco
	/// vezes: `if(r_race=="Namekian") getTree(new /datum/skill/tree/namek)`. Ler isso e mais
	/// seguro que copiar na mao: um nome de raca digitado errado aqui nao quebra nada visivel,
	/// so faz um povo inteiro nunca receber a propria arvore racial.
	/// </summary>
	public static Concessoes LerConcessoes(string pastaCode)
	{
		var c = new Concessoes();
		string arq = Path.Combine(pastaCode, "Modules", "Skills", "Skill Trees", "mobhandlers.dm");
		if (!File.Exists(arq)) return c;

		var rxRaca = new Regex(@"r_race\s*==\s*""(?<r>[^""]+)""", RegexOptions.Compiled);
		var rxClasse = new Regex(@"Class\s*==\s*""(?<c>[^""]+)""", RegexOptions.Compiled);
		var rxArvore = new Regex(@"getTree\(\s*new\s*(?<p>/datum/skill/tree/[A-Za-z0-9_/]+)", RegexOptions.Compiled);

		string[] linhas = File.ReadAllLines(arq);
		int ini = Array.FindIndex(linhas, l => l.Contains("proc/generatetrees", StringComparison.Ordinal));
		if (ini < 0) return c;

		string? raca = null, classe = null;
		int indRaca = -1, indClasse = -1;
		bool racialLigado = false;

		for (int i = ini + 1; i < linhas.Length; i++)
		{
			string l = linhas[i].TrimEnd();
			if (l.Length == 0) continue;
			int ind = 0; while (ind < l.Length && l[ind] == '\t') ind++;
			if (ind == 0) break;   // saiu do proc
			// linha comentada nao concede arvore nenhuma -- o `//getTree(Custom_Attacks)` do
			// original e uma arvore DESLIGADA, e le-la daria a todo mundo uma arvore que nem
			// existe no catalogo
			if (l[ind..].StartsWith("//", StringComparison.Ordinal)) continue;

			// o `else` do `if(GenerateRacials == null || == 0)` separa as gerais das raciais
			if (l.Trim() == "else") { racialLigado = true; raca = classe = null; continue; }

			if (raca != null && ind <= indRaca) raca = null;
			if (classe != null && ind <= indClasse) classe = null;

			Match mr = rxRaca.Match(l);
			if (mr.Success) { raca = mr.Groups["r"].Value; indRaca = ind; classe = null; continue; }

			Match mc = rxClasse.Match(l);
			if (mc.Success) { classe = mc.Groups["c"].Value; indClasse = ind; continue; }

			Match ma = rxArvore.Match(l);
			if (!ma.Success) continue;
			string arvore = ma.Groups["p"].Value;

			if (classe != null) Somar(c.PorClasse, classe, arvore);
			else if (raca != null) Somar(c.PorRaca, raca, arvore);
			else if (!racialLigado && !c.Todos.Contains(arvore)) c.Todos.Add(arvore);
		}
		return c;
	}

	private static void Somar(Dictionary<string, List<string>> d, string chave, string valor)
	{
		if (!d.TryGetValue(chave, out List<string>? l)) d[chave] = l = [];
		if (!l.Contains(valor)) l.Add(valor);
	}

	public static void EscreverConcessoes(string caminho, Concessoes c)
	{
		var sb = new StringBuilder();
		sb.Append("{\n");
		sb.Append($"  \"todos\": [{string.Join(", ", c.Todos.Select(J))}],\n");
		sb.Append("  \"raca\": {\n");
		sb.Append(string.Join(",\n", c.PorRaca.Select(kv => $"    {J(kv.Key)}: [{string.Join(", ", kv.Value.Select(J))}]")));
		sb.Append("\n  },\n  \"classe\": {\n");
		sb.Append(string.Join(",\n", c.PorClasse.Select(kv => $"    {J(kv.Key)}: [{string.Join(", ", kv.Value.Select(J))}]")));
		sb.Append("\n  }\n}\n");
		Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}

	// =====================================================================
	// SAIDA
	// =====================================================================
	public static void Escrever(string caminho, Dictionary<string, SkillDef> defs)
	{
		var sb = new StringBuilder();
		sb.Append("[\n");
		bool primeiro = true;

		foreach (SkillDef d in defs.Values.OrderBy(x => x.Path, StringComparer.Ordinal))
		{
			// ARVORE nao e skill: e o galho onde as skills penduram. Sai com a marca propria
			// porque a interface de aprendizado precisa das duas coisas.
			if (d.Nome.Length == 0) continue;   // bloco sem nome: e classe-base, nao skill

			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append("  { ");
			sb.Append($"\"path\": {J(d.Path)}, \"nome\": {J(d.Nome)}, \"desc\": {J(d.Desc)}, ");
			sb.Append($"\"tipo\": {J(d.Tipo)}, \"tier\": {d.Tier}, \"custo\": {d.Custo}, ");
			sb.Append($"\"maxnivel\": {d.MaxNivel}, \"folga\": {d.PreReqFolga}, ");
			sb.Append($"\"arvore\": {(d.Arvore ? 1 : 0)}, \"comum\": {(d.Comum ? 1 : 0)}, ");
			sb.Append($"\"ligada\": {(d.Ligada ? 1 : 0)}, \"vilao\": {(d.SoVilao ? 1 : 0)}, ");
			sb.Append($"\"ensinavel\": {(d.Ensinavel ? 1 : 0)}, \"custofixo\": {(d.CustoFixo ? 1 : 0)}, ");
			sb.Append($"\"racas\": [{string.Join(", ", d.Racas.Select(J))}], ");
			sb.Append($"\"classes\": [{string.Join(", ", d.Classes.Select(J))}], ");
			sb.Append($"\"prereqs\": [{string.Join(", ", d.PreReqs.Select(J))}], ");
			sb.Append($"\"galhos\": [{string.Join(", ", d.Constituintes.Select(J))}], ");
			// LISTA PLANA, NAO OBJETO ANINHADO ("physoffBuff=0.4"). O leitor do jogo e um parser
			// de uma pagina que fatia o arquivo por `{`..`}` de primeiro nivel; uma chave dentro do
			// bloco quebraria TODA skill dali pra frente, e em silencio. Formato serve leitor.
			sb.Append($"\"buffs\": [{string.Join(", ", d.Buffs.Select(kv =>
				J($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}")))}], ");
			sb.Append($"\"verbos\": [{string.Join(", ", d.Verbos.Select(J))}], ");
			sb.Append($"\"mults\": [{Pares(d.Mults)}], ");
			sb.Append($"\"genes\": [{Pares(d.Genes)}], ");
			sb.Append($"\"flags\": [{Pares(d.Flags)}], ");
			sb.Append($"\"estilo\": {J(d.Estilo)}");
			sb.Append(" }");
		}
		sb.Append("\n]\n");

		Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}

	/// <summary>Um dicionario como lista plana de "chave=valor" -- ver o comentario de `buffs`.</summary>
	private static string Pares(Dictionary<string, double> d) => string.Join(", ", d.Select(kv =>
		J($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}")));

	private static string J(string s) =>
		"\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";
}
