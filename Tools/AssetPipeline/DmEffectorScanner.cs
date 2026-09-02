using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>
/// UM DEGRAU: o que a skill ganha ao chegar num nivel.
///
/// Duas formas, e as duas valem: <c>Nivel</c> preenchido e o degrau EXATO (`if(level == 25)`),
/// <c>Periodo</c> preenchido e o degrau PERIODICO (`if(level % 5 == 0)`) -- que rende a cada N
/// niveis ate o teto. As arvores de Ki vivem do periodico: a Basic Ki Effusion tem tres degraus
/// escritos (5, 10, 20) que na pratica sao SESSENTA aplicacoes ao longo de 100 niveis.
/// </summary>
public sealed class DegrauEffector
{
	public int Nivel = -1;    // -1 = nao e degrau de nivel exato
	public int Periodo;       // >0 = `if(level % N == 0)`, repete

	// OS MESMOS QUATRO CANAIS DO after_learn, pelo mesmo motivo: somar num campo que deveria
	// MULTIPLICAR da o numero certo quando a base e 1 e o numero errado em todo o resto.
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);

	public List<string> Verbos = [];      // assignverb(...) e contents += new/obj/...
	public List<string> Destrava = [];    // enableskill(...) / enabletree(...): abre OUTRA skill
	public List<string> Concede = [];     // new/datum/skill/x + learn(): entrega uma skill inteira

	/// <summary>A frase que o DM manda pro jogador no degrau -- fonte pro texto em portugues.</summary>
	public string Msg = "";

	/// <summary>
	/// O QUE NAO DEU PRA LER, verbatim. Enquanto isto estiver vazio o degrau e DADO puro e pode
	/// ir pro JSON sem ninguem reescrever nada. Uma linha aqui e um aviso: alguem tem que olhar.
	/// </summary>
	public List<string> Logica = [];

	public bool Puro => Logica.Count == 0;

	public bool Vazio => Logica.Count == 0 && Verbos.Count == 0 && Destrava.Count == 0
					  && Concede.Count == 0 && Buffs.Count == 0 && Mults.Count == 0
					  && Genes.Count == 0 && Flags.Count == 0;
}

/// <summary>
/// DE ONDE VEM O EXP. O effector e chamado a cada 2 decimos (skill.dm:39-42), e e nele que a
/// skill se treina sozinha -- nao ha proc de "ganhar exp" em lugar nenhum.
/// </summary>
public sealed class FonteExp
{
	public double Quanto = 1;
	public int Prob;              // 0 = todo tique; 50 = `if(prob(50)) exp++`
	public string Contador = "";  // `savant.blastcounter`: treina POR USO, nao por tempo
	public bool Curva;            // passou por KiSkillGains() (Mind.dm:859-871), que escala por Ki
	public string Cond = "";      // o gate verbatim: "savant.med", "savant.flight", ...
	public string Cru = "";       // a linha do DM inteira, pra conferencia
}

/// <summary>O effector() de UMA skill, lido.</summary>
public sealed class EffectorDef
{
	public string Path = "";
	public string Arquivo = "";   // relativo a pasta code, com a linha: "Modules/.../Body.dm"
	public int Linha;

	/// <summary>`expbarrier` declarado como propriedade. 0 = nao declarou (o DM cai em 100, skill.dm:9).</summary>
	public int ExpBarreira;
	/// <summary>`maxlevel` declarado. 0 = nao declarou (o DM cai em 3, skill.dm:10).</summary>
	public int MaxNivel;

	/// <summary>
	/// A BARREIRA QUE CRESCE: `expbarrier=(5000*1.03**level)` (Mind.dm:88). O degrau seguinte
	/// custa 3% mais que o anterior, e a barreira declarada como propriedade so vale ate o
	/// primeiro levelup -- dai em diante e esta conta. Sem ela, subir do 1 ao 100 custaria o
	/// mesmo que subir do 1 ao 2.
	/// </summary>
	public double BarreiraBase;
	public double BarreiraCrescimento;

	/// <summary>Chamou `..()`? Sem isso o motor de nivel do pai NAO roda (skill.dm:86-95).</summary>
	public bool ChamaBase;
	/// <summary>Corpo que e so `return`: desliga o motor de proposito (supersaiyan.dm:231-232).</summary>
	public bool Inerte;

	public List<FonteExp> Exps = [];
	public List<DegrauEffector> Degraus = [];

	/// <summary>O que roda TODO TIQUE fora de qualquer degrau -- o efeito passivo da skill.</summary>
	public DegrauEffector Passivo = new();

	public int Linhas;            // tamanho do corpo, pra medir quanto sobrou de fora
}

/// <summary>
/// LE O TIQUE DAS SKILLS -- o corpo de `effector()`.
///
/// O QUE O effector() E: o proc base esta em `Modules/Skills/Skills Master/skill.dm:86-95` e faz
/// so a contabilidade -- limita `level` pelo `maxlevel`, e quando `exp >= expbarrier` zera o exp,
/// sobe um nivel e liga a flag `levelup`. Quem chama e o `learn()` (skill.dm:39-42) e o `login()`
/// (skill.dm:50-52), num laco `while(savant)` com `sleep(2)`: DUAS DECIMAS de segundo por tique,
/// pra sempre, enquanto o dono estiver online.
///
/// POR QUE ISSO IMPORTA PRO PORT: o `after_learn()` ja extraido diz o que a skill da NA HORA em que
/// e comprada. O effector diz todo o resto -- como ela SOBE (a skill se treina sozinha, com o tempo
/// e com o uso) e o que cada nivel destrava. Sem ele, cada skill do jogo vira uma compra unica e
/// morta: os 352 degraus somem, as tecnicas que so chegam no nivel 25 nunca chegam, e a arvore de
/// Ki inteira -- que e de nivel 1 a 100 -- fica presa no nivel que o jogador comprou.
///
/// AS DUAS FAMILIAS. O effector nao tem uma forma so, tem duas, e a segunda e a maior:
///
///   1) DEGRAU CURTO (as fisicas, maxlevel 2-3): `if(level &lt; maxlevel) if(prob(50)) exp++`
///      seguido de `switch(level)` com um `if(N)` por nivel que concede um verb.
///      Ex.: Bodybuilding/grace, `Core Trees/Body.dm:178-186` -- nivel 2 da o Falling_Kick.
///
///   2) LEVELUP + MODULO (as arvores de Ki, maxlevel 100): tudo dentro de um `if(levelup)`, com
///      `if(level % 5 == 0)` pra buff periodico e `if(level == 25)` pra marco, mais uma barreira
///      que cresce sozinha. Ex.: `Ki Trees/Effusion.dm:43-77`.
///
/// A SEGUNDA FAMILIA NAO USA switch NENHUM -- ha 52 `switch(level)` no jogo contra 142 `if(level
/// == N)` e 93 `if(level % N == 0)`. Um extrator que so olhasse `switch` perderia a maior parte
/// dos degraus, e justamente os das skills que mais rendem.
///
/// A REGRA DE HONESTIDADE. Guarda transparente (`if(levelup)`, `if(levelup == 1)`) NAO muda a quem
/// o efeito pertence: ela so quer dizer "no tique em que voce chegou aqui". Qualquer OUTRA
/// condicao poe o galho inteiro em quarentena: as linhas vao verbatim pra `Logica` e nada delas e
/// colhido. E de proposito -- em `Misc/flying.dm:26-28` o verb Space_Flight esta atras de um
/// `if(savant.spacebreather)`, e dar isso como "nivel 1 concede Space_Flight" entregaria voo
/// espacial a raca nenhuma... a todas.
/// </summary>
public static class DmEffectorScanner
{
	// ---------------------------------------------------------------------
	// AS FORMAS DE DECLARAR O effector
	// ---------------------------------------------------------------------
	/// <summary>
	/// A forma DE TOPO: `/datum/skill/sense/effector()`, com o caminho inteiro na linha.
	///
	/// Sao as DUAS formas, como no `after_learn` -- e la essa distincao ja custou 116 skills.
	/// Aqui sao 27 effectors declarados assim contra 107 aninhados; perder os 27 tiraria do
	/// catalogo o Kaio-ken, o voo, o Afterimage e a arvore Namekiana inteira.
	/// Aceita sem a barra inicial (`datum/skill/ki/Afterimage/effector()`, Ki/misc.dm:50) e com
	/// comentario no fim (`supersaiyan.dm:26`).
	/// </summary>
	private static readonly Regex RxEffTopo = new(
		@"^(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)+)/effector\(\)\s*(//.*)?$", RegexOptions.Compiled);

	// ---------------------------------------------------------------------
	// OS CANAIS DE EFEITO -- os mesmos do DmSkillScanner, pelas mesmas razoes.
	// Estao repetidos aqui porque la sao `private` e aquele arquivo esta em uso por outro
	// trabalho; copiar cinco regex e mais barato que arriscar um conflito de escrita nele.
	// ---------------------------------------------------------------------
	private static readonly Regex RxBuff = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+=|-=)\s*(?<v>[0-9.]+)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxMult = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\*=|/=)\s*(?<v>[0-9.]+)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxIncr = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+\+|--)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxGene = new(
		@"savant\.genome\.(?<op>add_to_stat|sub_to_stat)\(\s*""(?<s>[^""]+)""\s*,\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	private static readonly Regex RxSet = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>-?[0-9.]+)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxObj = new(
		@"^savant\.contents\s*\+=\s*new\s*/obj/(?:[A-Za-z0-9_]+/)*(?<o>[A-Za-z0-9_]+)", RegexOptions.Compiled);

	private static readonly Regex RxVerb = new(
		@"^assignverb\(\s*/(?:[A-Za-z0-9_]+/)*verb/(?<v>[A-Za-z0-9_]+)", RegexOptions.Compiled);

	// ---------------------------------------------------------------------
	// OS CANAIS QUE SO O effector TEM
	// ---------------------------------------------------------------------
	/// <summary>`enableskill(/datum/skill/mind/Advanced_Ki_Effusion)`: o nivel ABRE outra skill.</summary>
	private static readonly Regex RxDestrava = new(
		@"^(?:enableskill|enabletree)\(\s*(?:new\s*)?(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)+)", RegexOptions.Compiled);

	/// <summary>`var/datum/skill/A = new/datum/skill/sense` + `A.learn(savant, 1)` (Mind.dm:103-104).</summary>
	private static readonly Regex RxConcede = new(
		@"=\s*new\s*(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)+)", RegexOptions.Compiled);

	/// <summary>`expbarrier=(5000*1.03**level)` e `expbarrier = 10000 * (3 ** level)`.</summary>
	private static readonly Regex RxBarreiraCurva = new(
		@"^expbarrier\s*=\s*\(?\s*(?<b>[0-9.]+)\s*\*\s*\(?\s*(?<g>[0-9.]+)\s*\*\*\s*level", RegexOptions.Compiled);

	private static readonly Regex RxBarreiraFixa = new(
		@"^expbarrier\s*=\s*\(?\s*(?<v>[0-9.]+)\s*\)?\s*$", RegexOptions.Compiled);

	/// <summary>`exp++`, `exp += 1`, `exp+=KiSkillGains(10*(savant.blastcounter-attackcounter))`.</summary>
	private static readonly Regex RxExp = new(
		@"^exp\s*(?<op>\+\+|--|\+=|-=)\s*(?<r>.*)$", RegexOptions.Compiled);

	/// <summary>O contador que faz a skill treinar POR USO: `savant.blastcounter`.</summary>
	private static readonly Regex RxContador = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*counter)", RegexOptions.Compiled);

	private static readonly Regex RxNum = new(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.Compiled);
	private static readonly Regex RxChat = new(@"to_chat\([^,]+,\s*""(?<m>[^""]*)""", RegexOptions.Compiled);

	// --- as condicoes que abrem um degrau ---
	private static readonly Regex RxCondNivel = new(@"^level\s*==\s*(?<n>[0-9]+)$", RegexOptions.Compiled);
	private static readonly Regex RxCondModulo = new(@"^level\s*%\s*(?<k>[0-9]+)\s*==\s*0$", RegexOptions.Compiled);
	private static readonly Regex RxCondProb = new(@"^prob\((?<p>[0-9]+)\)$", RegexOptions.Compiled);

	private static readonly Regex RxTypePath = new(
		@"^(?<p>/?[A-Za-z_][A-Za-z0-9_]*(?:/[A-Za-z0-9_]+)*)$", RegexOptions.Compiled);

	/// <summary>A propriedade `expbarrier = 5000` / `maxlevel = 100` no bloco da skill.</summary>
	private static readonly Regex RxPropInt = new(
		@"^(?<k>expbarrier|maxlevel)\s*=\s*(?<v>[0-9]+)\s*$", RegexOptions.Compiled);

	// =====================================================================
	// LEITURA
	// =====================================================================
	public static Dictionary<string, EffectorDef> Scan(string pastaCode) => Scan(pastaCode, out _);

	/// <param name="barreiras">
	/// O `expbarrier` de TODA skill que declarou um, tenha effector ou nao -- sao 180 declaracoes
	/// contra 99 skills com tique. Sai a parte porque quem consome e o catalogo, nao o tique.
	/// </param>
	public static Dictionary<string, EffectorDef> Scan(string pastaCode, out Dictionary<string, int> barreiras)
	{
		var defs = new Dictionary<string, EffectorDef>(StringComparer.OrdinalIgnoreCase);
		var props = new Dictionary<string, (int barreira, int maxnivel)>(StringComparer.OrdinalIgnoreCase);

		foreach (string arq in Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories))
			Ler(arq, pastaCode, defs, props);

		// as propriedades sao lidas na MESMA passada, mas podem aparecer DEPOIS do effector: na
		// forma de topo (`/datum/skill/x/effector()`) o bloco da skill fica linhas acima, e na
		// aninhada o `effector()` costuma vir antes do fim do bloco. Por isso o casamento e aqui,
		// no fim, e nao na hora de abrir o effector.
		foreach (EffectorDef e in defs.Values)
			if (props.TryGetValue(e.Path, out (int barreira, int maxnivel) p))
			{
				e.ExpBarreira = p.barreira;
				e.MaxNivel = p.maxnivel;
			}

		barreiras = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach ((string caminho, (int barreira, int _)) in props)
			if (barreira > 0) barreiras[caminho] = barreira;

		return defs;
	}

	/// <summary>Um contexto aberto no corpo do effector: um bloco por nivel de indentacao.</summary>
	private sealed class Ctx
	{
		public int Ind;
		public DegrauEffector? Deg;   // o degrau a que este bloco pertence (herdado do pai)
		public bool Switch;           // e um `switch(level)`: os filhos `if(N)` sao niveis
		public bool Quarentena;       // condicao que nao sei ler: nada daqui pra dentro e colhido
		public string Cond = "";      // a condicao verbatim, pra sair no relatorio e nas fontes de exp
	}

	private static void Ler(string arq, string raiz, Dictionary<string, EffectorDef> defs,
						   Dictionary<string, (int, int)> props)
	{
		string[] linhas = File.ReadAllLines(arq);
		string rel = Path.GetRelativePath(raiz, arq).Replace('\\', '/');

		var pilha = new string[64];      // typepath aberto em cada nivel de indentacao
		int profCom = 0;                 // profundidade de /* */ (o DM permite um dentro do outro)
		int indProc = -1;                // corpo de proc que nao interessa
		int indEff = -1;                 // indentacao da linha `effector()`
		EffectorDef? eff = null;
		var ctx = new List<Ctx>();

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i].TrimEnd();
			if (linha.Length == 0) continue;

			int ind = 0;
			while (ind < linha.Length && linha[ind] == '\t') ind++;
			if (ind >= pilha.Length - 1) continue;
			string corpo = linha[ind..];

			// CODIGO MORTO EM /* */: o DM guarda ali coisa que nem compila. Ler isso poe no
			// catalogo degrau que nunca acontece -- o pior tipo, o que parece funcionar.
			//
			// A CONTA E A MESMA DO CATALOGO DE SKILLS, e por isso e a mesma FUNCAO: os dois
			// scanners leem os mesmos .dm, e as duas armadilhas (`/*/` fechando a propria abertura
			// e `/* */` aninhado) valem igual aqui. Duas copias divergiriam, e divergir aqui e o
			// degrau existir num arquivo e nao no outro.
			corpo = DmSkillScanner.SemComentario(corpo, ref profCom).TrimEnd();
			if (corpo.Length == 0) continue;

			// COMENTARIO COLADO NO TYPEPATH. `/datum/skill/mind/Ki_Unlocked//this skill is the
			// basis for all other ki skills` (Core Trees/Mind.dm:65) e um typepath valido com um
			// comentario grudado sem espaco. Quem so olha a linha inteira nao a reconhece como
			// caminho e PERDE O BLOCO TODO -- e este bloco e a Ki Unlocked, a raiz da arvore de
			// Ki: e ela que entrega o Kiai, o voo, o Basic Blast e o proprio Sense.
			corpo = Limpar(corpo);
			if (corpo.Length == 0) continue;

			// linha de continuacao do DM (barra invertida no fim)
			while (corpo.EndsWith('\\') && i + 1 < linhas.Length) corpo = corpo[..^1] + linhas[++i].Trim();

			// fechou o effector?
			if (indEff >= 0 && ind <= indEff) { indEff = -1; eff = null; ctx.Clear(); }
			if (indProc >= 0 && ind <= indProc) indProc = -1;

			// --- dentro do corpo do effector ---
			if (eff != null && indEff >= 0)
			{
				eff.Linhas++;
				Corpo(eff, ctx, ind, corpo);
				continue;
			}
			if (indProc >= 0) continue;

			// --- abre um effector? ---
			string? dono = null;
			if (RxEffTopo.Match(corpo) is { Success: true } mt) dono = Normalizar(mt.Groups["p"].Value);
			else if (corpo.StartsWith("effector()", StringComparison.Ordinal) && ind > 0) dono = pilha[ind - 1];

			if (dono != null)
			{
				// `/datum/skill/proc/effector()` e o proc BASE (skill.dm:86), nao a skill de
				// ninguem -- ele e o motor que os overrides chamam com `..()`.
				if (dono.EndsWith("/proc", StringComparison.Ordinal) || dono.Contains("/proc/", StringComparison.Ordinal))
				{
					indProc = ind;
					continue;
				}
				if (!defs.TryGetValue(dono, out eff))
					defs[dono] = eff = new EffectorDef { Path = dono, Arquivo = rel, Linha = i + 1 };
				indEff = ind;
				ctx.Clear();
				continue;
			}

			// --- propriedade da skill (`expbarrier = 5000`) ---
			if (RxPropInt.Match(corpo) is { Success: true } mp && ind > 0 && pilha[ind - 1] is { } donoProp)
			{
				(int b, int m) = props.GetValueOrDefault(donoProp);
				int v = int.Parse(mp.Groups["v"].Value, CultureInfo.InvariantCulture);
				props[donoProp] = mp.Groups["k"].Value == "expbarrier" ? (v, m) : (b, v);
				continue;
			}

			// --- outro corpo de proc: pula ---
			if (corpo.EndsWith(')') && corpo.Contains('(')) { indProc = ind; continue; }
			if (corpo.Contains('=')) continue;   // outra propriedade, nao interessa aqui

			// --- segmento de typepath (a pilha que diz de quem e o `effector()` aninhado) ---
			if (!RxTypePath.IsMatch(corpo)) continue;
			string pai = ind > 0 ? pilha[ind - 1] ?? "" : "";
			bool absoluto = corpo.StartsWith('/') || corpo.StartsWith("datum/", StringComparison.Ordinal);
			string caminho = absoluto ? Normalizar(corpo)
						   : pai.Length > 0 ? pai + "/" + corpo.Trim('/')
						   : "";
			if (caminho.Length == 0) continue;
			pilha[ind] = caminho;
			for (int k = ind + 1; k < pilha.Length; k++) pilha[k] = null!;
		}
	}

	/// <summary>Tira comentario de fim de linha sem quebrar string com `//` dentro.</summary>
	private static string Limpar(string s)
	{
		bool aspas = false;
		for (int i = 0; i + 1 < s.Length; i++)
		{
			if (s[i] == '"') aspas = !aspas;
			else if (!aspas && s[i] == '/' && s[i + 1] == '/') return s[..i].TrimEnd();
		}
		return s.TrimEnd();
	}

	// =====================================================================
	// O CORPO DO effector, linha a linha
	// =====================================================================
	private static void Corpo(EffectorDef eff, List<Ctx> ctx, int ind, string linha)
	{
		// fecha os blocos que esta linha deixou pra tras
		while (ctx.Count > 0 && ctx[^1].Ind >= ind) ctx.RemoveAt(ctx.Count - 1);

		// desmonta a corrente de condicoes: `if(level < maxlevel) if(prob(50)) exp++` sao DUAS
		// condicoes e um comando, tudo na mesma linha (o DM permite, e 24 skills escrevem assim)
		var conds = new List<string>();
		string resto = linha;
		while (Condicao(resto, out string c, out string r)) { conds.Add(c); resto = r; }

		if (conds.Count > 0 && resto.Length == 0)
		{
			// linha que so ABRE bloco: empilha um contexto pro que vem indentado abaixo
			ctx.Add(Abrir(eff, ctx, ind, conds));
			return;
		}

		// linha de comando (com ou sem condicao inline)
		Ctx atual = ctx.Count > 0 ? ctx[^1] : new Ctx { Ind = -1 };
		bool quarentena = atual.Quarentena;
		var gates = new List<string>();
		if (atual.Cond.Length > 0) gates.Add(atual.Cond);

		// a condicao inline pode ser transparente, pode abrir degrau, pode ser opaca
		DegrauEffector? deg = atual.Deg;
		int prob = 0;
		foreach (string c in conds)
		{
			if (RxCondProb.Match(c) is { Success: true } mprob)
			{ prob = int.Parse(mprob.Groups["p"].Value, CultureInfo.InvariantCulture); continue; }
			if (Transparente(c)) continue;
			if (RxCondNivel.Match(c) is { Success: true } mn)
			{ deg = Degrau(eff, int.Parse(mn.Groups["n"].Value, CultureInfo.InvariantCulture), 0); continue; }
			if (RxCondModulo.Match(c) is { Success: true } mm)
			{ deg = Degrau(eff, -1, int.Parse(mm.Groups["k"].Value, CultureInfo.InvariantCulture)); continue; }
			gates.Add(c);
			quarentena = true;
		}

		Aplicar(eff, deg ?? eff.Passivo, quarentena, prob, string.Join(" && ", gates), resto, linha);
	}

	/// <summary>Empilha o contexto de um bloco que abre (`switch(level)`, `if(level == 25)`, ...).</summary>
	private static Ctx Abrir(EffectorDef eff, List<Ctx> ctx, int ind, List<string> conds)
	{
		Ctx pai = ctx.Count > 0 ? ctx[^1] : new Ctx { Ind = -1 };
		var novo = new Ctx { Ind = ind, Deg = pai.Deg, Quarentena = pai.Quarentena, Cond = pai.Cond };

		foreach (string c in conds)
		{
			// `switch(level)`: quem manda sao os filhos `if(N)`
			if (c == "switch:level") { novo.Switch = true; continue; }
			if (c.StartsWith("switch:", StringComparison.Ordinal)) { novo.Quarentena = true; novo.Cond = c; continue; }

			// ramo do switch de nivel: `if(2)`
			if (pai.Switch && RxNum.IsMatch(c))
			{ novo.Deg = Degrau(eff, (int)double.Parse(c, CultureInfo.InvariantCulture), 0); continue; }

			if (Transparente(c)) continue;
			if (RxCondNivel.Match(c) is { Success: true } mn)
			{ novo.Deg = Degrau(eff, int.Parse(mn.Groups["n"].Value, CultureInfo.InvariantCulture), 0); continue; }
			if (RxCondModulo.Match(c) is { Success: true } mm)
			{ novo.Deg = Degrau(eff, -1, int.Parse(mm.Groups["k"].Value, CultureInfo.InvariantCulture)); continue; }

			// QUALQUER outra condicao poe o galho em quarentena -- ver a nota de honestidade
			// no cabecalho da classe (o caso do Space_Flight em flying.dm:26-28).
			novo.Quarentena = true;
			novo.Cond = novo.Cond.Length > 0 ? novo.Cond + " && " + c : c;
		}
		return novo;
	}

	/// <summary>
	/// Guarda que NAO muda a quem o efeito pertence.
	///
	/// `if(levelup)` so quer dizer "no tique em que voce chegou neste nivel" -- e o proprio motor
	/// do pai que liga essa flag (skill.dm:94). `level &lt; maxlevel` quer dizer "ainda da pra
	/// subir". Tratar as duas como condicao de verdade poria em quarentena o `switch(level)`
	/// inteiro de 49 skills, que e exatamente onde estao os degraus.
	/// </summary>
	private static bool Transparente(string c) =>
		c is "levelup" or "levelup == 1" or "levelup==1" or "level < maxlevel" or "level<maxlevel"
		  or "savant" or "!isnull(savant)";

	private static DegrauEffector Degrau(EffectorDef eff, int nivel, int periodo)
	{
		// MESMO NIVEL DUAS VEZES SOMA, nao substitui: ha effector que escreve `if(level == 100)`
		// em dois lugares do mesmo corpo, e os dois acontecem.
		DegrauEffector? d = eff.Degraus.Find(x => x.Nivel == nivel && x.Periodo == periodo);
		if (d != null) return d;
		d = new DegrauEffector { Nivel = nivel, Periodo = periodo };
		eff.Degraus.Add(d);
		return d;
	}

	/// <summary>Fatia `if(<c>cond</c>) <c>resto</c>` respeitando parenteses aninhados.</summary>
	private static bool Condicao(string s, out string cond, out string resto)
	{
		cond = resto = "";
		if (s == "else") { cond = "else"; return true; }

		string[] chaves = ["if", "else if", "while", "for", "switch", "spawn"];
		foreach (string ch in chaves)
		{
			if (!s.StartsWith(ch, StringComparison.Ordinal)) continue;
			int p = ch.Length;
			while (p < s.Length && s[p] == ' ') p++;
			if (p >= s.Length || s[p] != '(') continue;

			int prof = 0, fim = -1;
			bool aspas = false;
			for (int i = p; i < s.Length; i++)
			{
				if (s[i] == '"') aspas = !aspas;
				if (aspas) continue;
				if (s[i] == '(') prof++;
				else if (s[i] == ')' && --prof == 0) { fim = i; break; }
			}
			if (fim < 0) continue;

			cond = s[(p + 1)..fim].Trim();
			// o `switch` sai marcado porque muda o significado dos filhos `if(N)`
			if (ch == "switch") cond = "switch:" + cond;
			resto = s[(fim + 1)..].Trim();
			return true;
		}
		return false;
	}

	// =====================================================================
	// COLHER UM COMANDO
	// =====================================================================
	private static void Aplicar(EffectorDef eff, DegrauEffector deg, bool quarentena,
								int prob, string cond, string cmd, string cru)
	{
		if (cmd.Length == 0) return;

		// --- contabilidade do proprio motor: nao e efeito, e encanamento ---
		if (cmd == "..()") { eff.ChamaBase = true; return; }
		if (cmd == "return") { if (eff.Linhas == 1) eff.Inerte = true; return; }
		if (cmd.StartsWith("set ", StringComparison.Ordinal)) return;
		if (cmd.StartsWith("levelup", StringComparison.Ordinal) && cmd.Contains('=')) return;

		// --- a frase que o jogador le no degrau ---
		if (cmd.StartsWith("to_chat(", StringComparison.Ordinal))
		{
			if (deg.Msg.Length == 0 && RxChat.Match(cmd) is { Success: true } mc)
				deg.Msg = mc.Groups["m"].Value;
			return;
		}

		// --- exp: a fonte vale mesmo em quarentena, porque a CONDICAO e o dado ---
		// (`if(savant.med) exp+=KiSkillGains(2)` -- meditar treina; o gate e a informacao)
		if (RxExp.Match(cmd) is { Success: true } me)
		{
			string r = me.Groups["r"].Value.Trim();
			var f = new FonteExp
			{
				Prob = prob,
				Cond = cond,
				Cru = cru,
				Curva = r.Contains("KiSkillGains", StringComparison.Ordinal),
				Quanto = 1,
			};
			if (me.Groups["op"].Value is "+=" or "-=")
			{
				// o primeiro numero da expressao e a taxa: `KiSkillGains(10*(blastcounter-...))`
				Match mnum = Regex.Match(r, @"[0-9]+(\.[0-9]+)?");
				if (mnum.Success) f.Quanto = double.Parse(mnum.Value, CultureInfo.InvariantCulture);
			}
			if (me.Groups["op"].Value is "-=" or "--") f.Quanto = -f.Quanto;
			if (RxContador.Match(r) is { Success: true } mct) f.Contador = mct.Groups["c"].Value;
			else if (RxContador.Match(cond) is { Success: true } mcc) f.Contador = mcc.Groups["c"].Value;
			eff.Exps.Add(f);
			return;
		}

		// --- a barreira que cresce ---
		if (cmd.StartsWith("expbarrier", StringComparison.Ordinal))
		{
			if (RxBarreiraCurva.Match(cmd) is { Success: true } mb)
			{
				eff.BarreiraBase = double.Parse(mb.Groups["b"].Value, CultureInfo.InvariantCulture);
				eff.BarreiraCrescimento = double.Parse(mb.Groups["g"].Value, CultureInfo.InvariantCulture);
				return;
			}
			if (RxBarreiraFixa.Match(cmd) is { Success: true } mf)
			{
				// barreira trocada NUM DEGRAU (`expbarrier=60` no nivel 1, Giant Form.dm:36):
				// vai como flag do degrau, porque o valor so vale dali pra frente
				deg.Flags["expbarrier"] = double.Parse(mf.Groups["v"].Value, CultureInfo.InvariantCulture);
				return;
			}
			deg.Logica.Add(cru);
			return;
		}

		// DAQUI PRA BAIXO SO SE NAO ESTIVER EM QUARENTENA. A linha existe e vai pro relatorio,
		// mas nao vira dado: buff atras de condicao que nao sei ler nao e buff daquele nivel.
		if (quarentena) { deg.Logica.Add(cru); return; }

		if (RxVerb.Match(cmd) is { Success: true } mv) { Somar(deg.Verbos, mv.Groups["v"].Value); return; }
		if (RxObj.Match(cmd) is { Success: true } mo) { Somar(deg.Verbos, mo.Groups["o"].Value); return; }
		if (RxDestrava.Match(cmd) is { Success: true } md) { Somar(deg.Destrava, Normalizar(md.Groups["p"].Value)); return; }
		if (RxConcede.Match(cmd) is { Success: true } mcd) { Somar(deg.Concede, Normalizar(mcd.Groups["p"].Value)); return; }
		if (cmd.Contains(".learn(", StringComparison.Ordinal))
		{
			// O PAR DO `new` ACIMA, e ele carrega o `baselevel`: `A.learn(savant, 1)` (Mind.dm:104)
			// entrega o Sense ja no nivel 1; `A.learn(savant, 0)` (:110) entrega o voo no zero. Vai
			// colado no caminho (`/datum/skill/sense=1`) porque a lista e plana e o leitor do jogo
			// fatia por `=` -- o mesmo formato dos buffs.
			Match mn = Regex.Match(cmd, @"\.learn\(\s*savant\s*,\s*(?<n>[0-9]+)");
			if (mn.Success && deg.Concede.Count > 0 && !deg.Concede[^1].Contains('='))
				deg.Concede[^1] = deg.Concede[^1] + "=" + mn.Groups["n"].Value;
			return;
		}

		if (RxGene.Match(cmd) is { Success: true } mg)
		{
			double v = double.Parse(mg.Groups["v"].Value, CultureInfo.InvariantCulture);
			if (mg.Groups["op"].Value == "sub_to_stat") v = -v;
			string stat = mg.Groups["s"].Value;
			deg.Genes[stat] = deg.Genes.GetValueOrDefault(stat) + v;
			return;
		}
		if (RxBuff.Match(cmd) is { Success: true } mbf)
		{
			double v = double.Parse(mbf.Groups["v"].Value, CultureInfo.InvariantCulture);
			if (mbf.Groups["op"].Value == "-=") v = -v;
			string c2 = mbf.Groups["c"].Value;
			deg.Buffs[c2] = deg.Buffs.GetValueOrDefault(c2) + v;
			return;
		}
		if (RxIncr.Match(cmd) is { Success: true } mi)
		{
			string c2 = mi.Groups["c"].Value;
			deg.Buffs[c2] = deg.Buffs.GetValueOrDefault(c2) + (mi.Groups["op"].Value == "--" ? -1 : 1);
			return;
		}
		if (RxMult.Match(cmd) is { Success: true } mm2)
		{
			double v = double.Parse(mm2.Groups["v"].Value, CultureInfo.InvariantCulture);
			if (v == 0) { deg.Logica.Add(cru); return; }
			string c2 = mm2.Groups["c"].Value;
			deg.Mults[c2] = deg.Mults.GetValueOrDefault(c2, 1) * (mm2.Groups["op"].Value == "/=" ? 1 / v : v);
			return;
		}
		if (RxSet.Match(cmd) is { Success: true } ms)
		{
			deg.Flags[ms.Groups["c"].Value] = double.Parse(ms.Groups["v"].Value, CultureInfo.InvariantCulture);
			return;
		}

		deg.Logica.Add(cru);
	}

	private static void Somar(List<string> l, string v) { if (!l.Contains(v)) l.Add(v); }

	private static string Normalizar(string p)
	{
		p = p.Replace("new", "").Replace(" ", "").Trim();
		return p.StartsWith('/') ? p : "/" + p;
	}

	// =====================================================================
	// LIGACAO COM O CATALOGO DE SKILLS
	// =====================================================================
	/// <summary>
	/// Copia pro <see cref="SkillDef"/> o `expbarrier` -- que o DmSkillScanner NAO le (ele trata
	/// `maxlevel`, `tier`, `skillcost` e esquece a barreira, e sem ela toda skill do port sobe de
	/// nivel na barreira padrao 100 em vez das 5.000 das arvores de Ki: cinquenta vezes rapido).
	/// </summary>
	public static int Aplicar(Dictionary<string, SkillDef> skills, Dictionary<string, int> barreiras)
	{
		int n = 0;
		foreach ((string caminho, int v) in barreiras)
			if (skills.TryGetValue(caminho, out SkillDef? s)) { s.ExpBarreira = v; n++; }
		return n;
	}

	// =====================================================================
	// SAIDA
	// =====================================================================
	/// <summary>
	/// UM REGISTRO POR LINHA DE DADO, tudo plano.
	///
	/// Nada de objeto aninhado, e a regra desta pasta: o leitor do jogo e um parser de uma pagina
	/// que fatia o arquivo pelos `{`..`}` de primeiro nivel, e uma chave dentro do bloco quebraria
	/// TODO registro dali pra frente -- em silencio. Por isso o degrau nao mora dentro da skill:
	/// ele e um registro irmao, marcado por `tipo` e amarrado pelo `path`.
	///
	/// tipo = "skill"   : o cabecalho (barreira, curva, quantas linhas sobraram de fora)
	/// tipo = "degrau"  : um nivel (ou um periodo) e o que ele da
	/// tipo = "passivo" : o que roda todo tique fora de qualquer degrau
	/// tipo = "exp"     : de onde vem o exp que faz a skill subir
	/// </summary>
	public static void Escrever(string caminho, Dictionary<string, EffectorDef> effs)
	{
		var sb = new StringBuilder();
		sb.Append("[\n");
		bool primeiro = true;

		foreach (EffectorDef e in effs.Values.OrderBy(x => x.Path, StringComparer.Ordinal))
		{
			Virgula();
			sb.Append($"  {{ \"tipo\": \"skill\", \"path\": {J(e.Path)}, \"dm\": {J($"{e.Arquivo}:{e.Linha}")}, ");
			sb.Append($"\"barreira\": {e.ExpBarreira}, \"maxnivel\": {e.MaxNivel}, ");
			sb.Append($"\"curvabase\": {N(e.BarreiraBase)}, \"curvataxa\": {N(e.BarreiraCrescimento)}, ");
			sb.Append($"\"chamabase\": {(e.ChamaBase ? 1 : 0)}, \"inerte\": {(e.Inerte ? 1 : 0)}, ");
			sb.Append($"\"degraus\": {e.Degraus.Count}, \"linhas\": {e.Linhas}, ");
			sb.Append($"\"logica\": {e.Degraus.Sum(d => d.Logica.Count) + e.Passivo.Logica.Count} }}");

			foreach (FonteExp f in e.Exps)
			{
				Virgula();
				sb.Append($"  {{ \"tipo\": \"exp\", \"path\": {J(e.Path)}, \"quanto\": {N(f.Quanto)}, ");
				sb.Append($"\"prob\": {f.Prob}, \"contador\": {J(f.Contador)}, \"curva\": {(f.Curva ? 1 : 0)}, ");
				sb.Append($"\"cond\": {J(f.Cond)}, \"dm\": {J(f.Cru)} }}");
			}

			foreach (DegrauEffector d in e.Degraus.OrderBy(x => x.Periodo > 0 ? 1000 + x.Periodo : x.Nivel))
			{
				Virgula();
				Bloco("degrau", e, d);
			}

			if (!e.Passivo.Vazio) { Virgula(); Bloco("passivo", e, e.Passivo); }
		}
		sb.Append("\n]\n");

		Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
		return;

		void Virgula() { if (!primeiro) sb.Append(",\n"); primeiro = false; }

		void Bloco(string tipo, EffectorDef dono, DegrauEffector d)
		{
			sb.Append($"  {{ \"tipo\": \"{tipo}\", \"path\": {J(dono.Path)}, ");
			sb.Append($"\"nivel\": {d.Nivel}, \"periodo\": {d.Periodo}, ");
			sb.Append($"\"buffs\": [{Pares(d.Buffs)}], \"mults\": [{Pares(d.Mults)}], ");
			sb.Append($"\"genes\": [{Pares(d.Genes)}], \"flags\": [{Pares(d.Flags)}], ");
			sb.Append($"\"verbos\": [{string.Join(", ", d.Verbos.Select(J))}], ");
			sb.Append($"\"destrava\": [{string.Join(", ", d.Destrava.Select(J))}], ");
			sb.Append($"\"concede\": [{string.Join(", ", d.Concede.Select(J))}], ");
			sb.Append($"\"msg\": {J(d.Msg)}, ");
			sb.Append($"\"logica\": [{string.Join(", ", d.Logica.Select(J))}] }}");
		}
	}

	/// <summary>O relatorio de cobertura: quanto do tique virou dado e quanto ainda e codigo.</summary>
	public static string Resumo(Dictionary<string, EffectorDef> effs)
	{
		var sb = new StringBuilder();
		int comNivel = effs.Values.Count(e => e.Degraus.Count > 0);
		var degraus = effs.Values.SelectMany(e => e.Degraus).ToList();

		sb.AppendLine($"effectors lidos      : {effs.Count}");
		sb.AppendLine($"com degrau de nivel  : {comNivel}");
		sb.AppendLine($"sem `..()` (nao sobe): {effs.Values.Count(e => !e.ChamaBase)}");
		sb.AppendLine($"degraus              : {degraus.Count}  "
					+ $"({degraus.Count(d => d.Periodo > 0)} periodicos, {degraus.Count(d => d.Nivel >= 0)} exatos)");
		sb.AppendLine($"  so dado            : {degraus.Count(d => d.Puro && !d.Vazio)}");
		sb.AppendLine($"  vazios (so texto)  : {degraus.Count(d => d.Vazio)}");
		sb.AppendLine($"  com logica         : {degraus.Count(d => !d.Puro)}");
		sb.AppendLine($"verbos concedidos    : {degraus.SelectMany(d => d.Verbos).Distinct().Count()}");
		sb.AppendLine($"skills destravadas   : {degraus.SelectMany(d => d.Destrava).Distinct().Count()}");
		sb.AppendLine($"fontes de exp        : {effs.Values.Sum(e => e.Exps.Count)}"
					+ $"  ({effs.Values.SelectMany(e => e.Exps).Count(f => f.Contador.Length > 0)} por USO, "
					+ $"{effs.Values.SelectMany(e => e.Exps).Count(f => f.Prob > 0)} por sorteio)");
		sb.AppendLine($"barreira que cresce  : {effs.Values.Count(e => e.BarreiraCrescimento > 0)}");
		sb.AppendLine();
		sb.AppendLine("os mais carregados de logica (linhas que ninguem leu):");
		foreach (EffectorDef e in effs.Values
					.OrderByDescending(x => x.Degraus.Sum(d => d.Logica.Count) + x.Passivo.Logica.Count)
					.Take(10))
			sb.AppendLine($"  {e.Degraus.Sum(d => d.Logica.Count) + e.Passivo.Logica.Count,4}/{e.Linhas,-4} "
						+ $"{e.Path}   {e.Arquivo}:{e.Linha}");
		return sb.ToString();
	}

	private static string Pares(Dictionary<string, double> d) => string.Join(", ", d.Select(kv =>
		J($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}")));

	private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

	private static string J(string s) =>
		"\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";
}
