using System.Globalization;
using System.Reflection;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>O que uma regra de `growbranches()` faz quando a condicao dela vale.</summary>
public enum TipoDeRegra
{
	/// <summary>`allowedtier = N`: a vitrine da arvore passa a mostrar ate o tier N.</summary>
	Tier,

	/// <summary>`enabletree(X)`: a arvore X passa a ser deste personagem.</summary>
	AbreArvore,

	/// <summary>`enableskill(X)`: a skill X (galho DESTA arvore) acende -- o `enabled = 1` do DM.</summary>
	Acende,

	/// <summary>`disableskill(X)`: a skill X apaga. `*` = todos os galhos desta arvore.</summary>
	Apaga,
}

/// <summary>
/// UMA REGRA DO `growbranches()` DE UMA ARVORE, lida do `skills.json` (`regras: ["tipo;alvo;condicao"]`).
///
/// ============================ POR QUE O CORE AVALIA UMA EXPRESSAO EM VEZ DE TER 46 `if` ============================
/// Cada arvore do DM declara a propria regra num corpo de proc, e o extrator
/// (`Tools/AssetPipeline/DmGalhosScanner.cs`) traduz o corpo em linhas `tipo;alvo;condicao` -- a
/// condicao e a EXPRESSAO DO DM, normalizada (`invested>=4`, `bodyskill>2`, `Class!='None'`).
///
/// A alternativa era transcrever as 46 a mao aqui. Foi o que existia: o `RecalcularDestravadas`
/// tinha as quatro portas do `Body.dm:24-32` e nenhuma outra -- nem o tier de vitrine, nem
/// Wrestling, nem Assassain, nem as tres arvores de Ki. Tabela escrita a mao envelhece no dia em
/// que alguem mexe num `.dm`; a expressao extraida envelhece junto com o `.dm`.
///
/// O avaliador e de tres valores: **verdadeiro, falso e DESCONHECIDO**. Identificador que o port nao
/// tem (`hasssj`, `Parent_Race`, `godki_at`) vale desconhecido, e desconhecido nunca dispara regra --
/// nem pra acender, nem pra apagar. E o censo (<see cref="CensoDeSkills"/>) diz qual identificador
/// faltou, em vez de a porta ficar fechada calada.
/// ==========================================================================================================
/// </summary>
public sealed class RegraDeArvore
{
	public TipoDeRegra Tipo;

	/// <summary>Typepath da skill/arvore, ou `*` (todos os galhos) nas regras de `Apaga`/`Acende`.</summary>
	public string Alvo = "";

	/// <summary>So em <see cref="TipoDeRegra.Tier"/>: o `allowedtier` que a regra escreve.</summary>
	public int TierAlvo;

	/// <summary>A condicao normalizada; vazia = sempre.</summary>
	public string Condicao = "";

	private Expressao? _expr;
	private bool _parseFalhou;

	/// <summary>A linha crua, como veio do json -- pro censo e pra tela citarem.</summary>
	public string Cru = "";

	public static RegraDeArvore? Parse(string cru)
	{
		string[] p = cru.Split(';', 3);
		if (p.Length < 2) return null;
		var r = new RegraDeArvore { Alvo = p[1], Condicao = p.Length > 2 ? p[2] : "", Cru = cru };
		switch (p[0])
		{
			case "tier":
				r.Tipo = TipoDeRegra.Tier;
				if (!int.TryParse(p[1], out r.TierAlvo)) return null;
				break;
			case "arvore": r.Tipo = TipoDeRegra.AbreArvore; break;
			case "acende": r.Tipo = TipoDeRegra.Acende; break;
			case "apaga": r.Tipo = TipoDeRegra.Apaga; break;
			default: return null;
		}
		return r;
	}

	/// <summary>A condicao vale? `null` = nao da pra saber (algum identificador o port nao tem).</summary>
	public bool? Vale(ContextoDeRegra ctx)
	{
		if (Condicao.Length == 0) return true;
		if (_expr == null && !_parseFalhou)
		{
			_expr = Expressao.Parse(Condicao);
			_parseFalhou = _expr == null;
		}
		if (_expr == null) { ctx.Desconhecidos.Add("(expressao ilegivel) " + Condicao); return null; }
		Valor v = _expr.Avaliar(ctx);
		return v.Verdade;
	}

	/// <summary>
	/// SE A CONDICAO E UM DEGRAU SIMPLES DE INVESTIMENTO -- `invested>=N`, `invested>N`, ou isso
	/// E mais alguma coisa --, quanto e preciso ter investido. E o que a tela mostra como "proximo
	/// degrau" e o que o veredito devolve em "falta investir X".
	/// </summary>
	public int? InvestidoMinimo
	{
		get
		{
			int? min = null;
			foreach (string parte in Condicao.Split("&&"))
			{
				string s = parte.Trim('(', ')');
				if (s.StartsWith("invested>=", StringComparison.Ordinal)
					&& int.TryParse(s["invested>=".Length..], out int a)) min = Math.Max(min ?? 0, a);
				else if (s.StartsWith("invested>", StringComparison.Ordinal)
					&& int.TryParse(s["invested>".Length..], out int b)) min = Math.Max(min ?? 0, b + 1);
			}
			return min;
		}
	}

	/// <summary>Os identificadores que a condicao le -- pro censo dizer o que o port nao tem.</summary>
	public IEnumerable<string> Identificadores => Expressao.Identificadores(Condicao);

	// =====================================================================
	// A EXPRESSAO
	// =====================================================================
	/// <summary>Um valor de tres naturezas: numero, texto, ou desconhecido.</summary>
	internal readonly record struct Valor(double? Num, string? Str)
	{
		public static readonly Valor Desconhecido = new(null, null);
		public bool EhDesconhecido => Num == null && Str == null;

		/// <summary>Verdade do DM: numero != 0, texto nao vazio; desconhecido continua desconhecido.</summary>
		public bool? Verdade => Num != null ? Num != 0 : Str != null ? Str.Length > 0 : null;

		public static Valor De(bool? b) => b == null ? Desconhecido : new(b.Value ? 1 : 0, null);
	}

	/// <summary>
	/// O AVALIADOR -- descida recursiva sobre a gramatica do DM que os `growbranches()` usam:
	///
	///     ou    := e ('||' e)*
	///     e     := nao ('&amp;&amp;' nao)*
	///     nao   := '!' nao | cmp
	///     cmp   := soma (('=='|'!='|'>='|'&lt;='|'>'|'&lt;') soma)?
	///     soma  := prod (('+'|'-') prod)*
	///     prod  := un (('*'|'/') un)*
	///     un    := '-' un | prim
	///     prim  := numero | 'texto' | identificador | funcao '(' ou (',' ou)* ')' | '(' ou ')' | '?'
	///
	/// `?` e o que o extrator escreve quando a condicao vem de um laco que o port nao enxerga
	/// (`for(... in savant.learned_skills)`): desconhecido por construcao.
	///
	/// AS FUNCOES sao as quatro do DM que os ganhos na compra usam -- `max`, `min`, `round`, `abs`
	/// (`storedBP = max(1, savant.BP*0.01)`, Bodybuilding.dm:89). `round()` de UM argumento e PISO no
	/// BYOND (nao arredondamento), e com dois e o arredondamento ao multiplo do segundo.
	/// </summary>
	internal abstract class Expressao
	{
		public abstract Valor Avaliar(ContextoDeRegra ctx);

		public static Expressao? Parse(string s)
		{
			var p = new Parser(s);
			try
			{
				Expressao e = p.Ou();
				return p.Fim ? e : null;
			}
			catch (FormatException) { return null; }
		}

		public static IEnumerable<string> Identificadores(string s)
		{
			var vistos = new HashSet<string>(StringComparer.Ordinal);
			int i = 0;
			while (i < s.Length)
			{
				char c = s[i];
				if (c == '\'') { int f = s.IndexOf('\'', i + 1); i = f < 0 ? s.Length : f + 1; continue; }
				if (char.IsLetter(c) || c == '_')
				{
					int j = i;
					while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '_' or '.')) j++;
					string id = s[i..j];
					if (vistos.Add(id)) yield return id;
					i = j;
					continue;
				}
				i++;
			}
		}
	}

	private sealed class Literal(Valor v) : Expressao
	{
		public override Valor Avaliar(ContextoDeRegra ctx) => v;
	}

	private sealed class Identificador(string nome) : Expressao
	{
		public override Valor Avaliar(ContextoDeRegra ctx)
		{
			switch (nome)
			{
				case "invested": return new Valor(ctx.Invested, null);
				case "Class": return new Valor(null, ctx.Classe);
				case "Race": return new Valor(null, ctx.Raca);
			}
			double? v = ctx.Contador(nome);
			if (v == null) { ctx.Desconhecidos.Add(nome); return Valor.Desconhecido; }
			return new Valor(v, null);
		}
	}

	private sealed class Funcao(string nome, Expressao[] args) : Expressao
	{
		public override Valor Avaliar(ContextoDeRegra ctx)
		{
			var v = new double[args.Length];
			for (int i = 0; i < args.Length; i++)
			{
				Valor a = args[i].Avaliar(ctx);
				if (a.Num is not { } n) return Valor.Desconhecido;
				v[i] = n;
			}
			switch (nome)
			{
				case "max": return v.Length == 0 ? Valor.Desconhecido : new Valor(v.Max(), null);
				case "min": return v.Length == 0 ? Valor.Desconhecido : new Valor(v.Min(), null);
				case "abs": return v.Length == 1 ? new Valor(Math.Abs(v[0]), null) : Valor.Desconhecido;
				case "round":
					// `round(x)` e PISO no BYOND; `round(x, n)` arredonda ao multiplo de n
					if (v.Length == 1) return new Valor(Math.Floor(v[0]), null);
					if (v.Length == 2 && v[1] != 0) return new Valor(Math.Round(v[0] / v[1], MidpointRounding.AwayFromZero) * v[1], null);
					return Valor.Desconhecido;
				default: return Valor.Desconhecido;
			}
		}
	}

	private sealed class Unario(char op, Expressao a) : Expressao
	{
		public override Valor Avaliar(ContextoDeRegra ctx)
		{
			Valor v = a.Avaliar(ctx);
			return op switch
			{
				'!' => Valor.De(v.Verdade is { } b ? !b : null),
				'-' => v.Num is { } n ? new Valor(-n, null) : Valor.Desconhecido,
				_ => Valor.Desconhecido,
			};
		}
	}

	private sealed class Binario(string op, Expressao a, Expressao b) : Expressao
	{
		public override Valor Avaliar(ContextoDeRegra ctx)
		{
			// LOGICA DE TRES VALORES, com curto-circuito de verdade: `Class!='None' && gotlegendchoice`
			// e FALSO pra quem nao e da classe mesmo que o outro lado seja desconhecido. Sem isto toda
			// condicao com um identificador que o port nao tem viraria desconhecida inteira.
			if (op == "&&")
			{
				bool? x = a.Avaliar(ctx).Verdade;
				if (x == false) return Valor.De(false);
				bool? y = b.Avaliar(ctx).Verdade;
				if (y == false) return Valor.De(false);
				return Valor.De(x == true && y == true ? true : null);
			}
			if (op == "||")
			{
				bool? x = a.Avaliar(ctx).Verdade;
				if (x == true) return Valor.De(true);
				bool? y = b.Avaliar(ctx).Verdade;
				if (y == true) return Valor.De(true);
				return Valor.De(x == false && y == false ? false : null);
			}

			Valor va = a.Avaliar(ctx), vb = b.Avaliar(ctx);
			if (va.EhDesconhecido || vb.EhDesconhecido) return Valor.Desconhecido;

			if (va.Str != null || vb.Str != null)
			{
				// texto so compara por igualdade; misturar texto com numero e desconhecido
				if (va.Str == null || vb.Str == null) return Valor.Desconhecido;
				return op switch
				{
					"==" => Valor.De(string.Equals(va.Str, vb.Str, StringComparison.Ordinal)),
					"!=" => Valor.De(!string.Equals(va.Str, vb.Str, StringComparison.Ordinal)),
					_ => Valor.Desconhecido,
				};
			}

			double x2 = va.Num!.Value, y2 = vb.Num!.Value;
			return op switch
			{
				"==" => Valor.De(x2 == y2),
				"!=" => Valor.De(x2 != y2),
				">=" => Valor.De(x2 >= y2),
				"<=" => Valor.De(x2 <= y2),
				">" => Valor.De(x2 > y2),
				"<" => Valor.De(x2 < y2),
				"+" => new Valor(x2 + y2, null),
				"-" => new Valor(x2 - y2, null),
				"*" => new Valor(x2 * y2, null),
				"/" => y2 == 0 ? Valor.Desconhecido : new Valor(x2 / y2, null),
				_ => Valor.Desconhecido,
			};
		}
	}

	private sealed class Parser(string s)
	{
		private int _i;
		public bool Fim => _i >= s.Length;

		private bool Pega(string tok)
		{
			if (string.CompareOrdinal(s, _i, tok, 0, tok.Length) != 0) return false;
			_i += tok.Length;
			return true;
		}

		public Expressao Ou()
		{
			Expressao e = E();
			while (Pega("||")) e = new Binario("||", e, E());
			return e;
		}

		private Expressao E()
		{
			Expressao e = Nao();
			while (Pega("&&")) e = new Binario("&&", e, Nao());
			return e;
		}

		// um `!` no comeco de um operando e sempre NAO: o `!=` so aparece DEPOIS de um operando, e
		// quem o consome e o `Cmp`
		private Expressao Nao() => Pega("!") ? new Unario('!', Nao()) : Cmp();

		private Expressao Cmp()
		{
			Expressao e = Soma();
			foreach (string op in new[] { "==", "!=", ">=", "<=", ">", "<" })
				if (Pega(op)) return new Binario(op, e, Soma());
			return e;
		}

		private Expressao Soma()
		{
			Expressao e = Prod();
			while (true)
			{
				if (Pega("+")) e = new Binario("+", e, Prod());
				else if (Pega("-")) e = new Binario("-", e, Prod());
				else return e;
			}
		}

		private Expressao Prod()
		{
			Expressao e = Un();
			while (true)
			{
				if (Pega("*")) e = new Binario("*", e, Un());
				else if (Pega("/")) e = new Binario("/", e, Un());
				else return e;
			}
		}

		private Expressao Un() => Pega("-") ? new Unario('-', Un()) : Prim();

		private Expressao Prim()
		{
			if (Fim) throw new FormatException();
			char c = s[_i];
			if (c == '(')
			{
				_i++;
				Expressao e = Ou();
				if (!Pega(")")) throw new FormatException();
				return e;
			}
			if (c == '?') { _i++; return new Literal(Valor.Desconhecido); }
			if (c == '\'')
			{
				int f = s.IndexOf('\'', _i + 1);
				if (f < 0) throw new FormatException();
				string txt = s[(_i + 1)..f];
				_i = f + 1;
				return new Literal(new Valor(null, txt));
			}
			if (char.IsDigit(c) || c == '.')
			{
				int j = _i;
				while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
				if (!double.TryParse(s[_i..j], NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
					throw new FormatException();
				_i = j;
				return new Literal(new Valor(n, null));
			}
			if (char.IsLetter(c) || c == '_')
			{
				int j = _i;
				while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '_' or '.')) j++;
				string id = s[_i..j];
				_i = j;

				// CHAMADA DE FUNCAO: `max(1,BP*0.01)`. So as quatro conhecidas viram `Funcao`; um
				// nome desconhecido seguido de `(` e erro de parse, e nao "desconhecido" -- uma
				// expressao que o Core nao le nao pode virar um ganho de zero em silencio.
				if (id is "max" or "min" or "round" or "abs" && Pega("("))
				{
					var args = new List<Expressao>();
					if (!Pega(")"))
					{
						args.Add(Ou());
						while (Pega(",")) args.Add(Ou());
						if (!Pega(")")) throw new FormatException();
					}
					return new Funcao(id, [.. args]);
				}
				return new Identificador(id);
			}
			throw new FormatException();
		}
	}
}

/// <summary>
/// O QUE UMA REGRA ENXERGA DO PERSONAGEM: quanto ele investiu na arvore em questao, a classe e a
/// raca, e um leitor de contadores (`bodyskill`, `kieffusionskill`, `lssj`...).
///
/// O leitor e uma FUNCAO e nao o <see cref="Fighter"/> pra que o Core seja testavel sem ficha: a
/// bancada passa um dicionario, o servidor passa <see cref="De"/>. O cliente nunca avalia nada --
/// ele recebe o RESULTADO no pacote (ver `SkillBook.Arvores`).
/// </summary>
public sealed class ContextoDeRegra
{
	public int Invested;
	public string Classe = "";
	public string Raca = "";
	public Func<string, double?> Contador = _ => null;

	/// <summary>
	/// AS SKILLS QUE UM DEGRAU DE NIVEL JA ACENDEU -- o `enableskill()` disparado de dentro do
	/// `effector()` (Mind.dm:186), lido AO VIVO do <see cref="NiveisDeSkill.Destravadas"/>.
	///
	/// E uma funcao e nao uma lista pelo mesmo motivo do <see cref="Contador"/>: o livro guarda o
	/// contexto e recalcula preguicosamente; uma lista copiada envelheceria no primeiro nivel que
	/// subisse depois dela. Vazio nas bancadas de mesa e no cliente (que recebe o RESULTADO).
	/// </summary>
	public Func<IEnumerable<string>> DestravadasPorDegrau = () => [];

	/// <summary>Identificadores que ninguem soube responder nesta avaliacao. Diagnostico.</summary>
	public HashSet<string> Desconhecidos { get; } = new(StringComparer.Ordinal);

	/// <summary>Sem contador nenhum: e o que o cliente e as bancadas de mesa usam.</summary>
	public static ContextoDeRegra Vazio(string raca, string classe) => new() { Raca = raca, Classe = classe };

	/// <summary>
	/// O leitor de verdade: os campos `double` publicos do lutador, pelo nome que o DM usa -- a
	/// cadeia de stats foi portada 1:1 de proposito, entao `savant.bodyskill` e `Fighter.bodyskill`.
	///
	/// Cache proprio, e nao o de <see cref="EfeitosDeSkill"/>: aquele anota nome desconhecido no
	/// relatorio "campos que o DM BUFFA e o port nao tem", e um identificador de CONDICAO
	/// (`godki_at`, `hasssj`) nao e um campo buffado -- iria sujar o relatorio errado.
	/// </summary>
	public static ContextoDeRegra De(Fighter f, string raca, string classe) => new()
	{
		Raca = raca,
		Classe = classe,
		Contador = nome => Campo(nome) is { } fi ? (double)fi.GetValue(f)! : null,
	};

	private static readonly Dictionary<string, FieldInfo?> Cache = new(StringComparer.Ordinal);

	/// <summary>O port tem este contador? (o censo pergunta isto pra dizer o que falta.)</summary>
	public static bool PortConhece(string ident) =>
		ident is "invested" or "Class" or "Race" || Campo(ident) != null;

	private static FieldInfo? Campo(string nome)
	{
		lock (Cache)
		{
			if (Cache.TryGetValue(nome, out FieldInfo? fi)) return fi;
			fi = typeof(Fighter).GetField(nome, BindingFlags.Public | BindingFlags.Instance);
			if (fi != null && fi.FieldType != typeof(double)) fi = null;
			Cache[nome] = fi;
			return fi;
		}
	}
}
