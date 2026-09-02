using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// O GANHO NA COMPRA COM EXPRESSAO -- `savant.BP += max(1, savant.BP*0.01)`,
/// `savant.hiddenpotential += savant.relBPmax*2` (One_Hundred, Bodybuilding.dm:89-92; One_Punch e
/// One_Training com `relBPmax*0.5`, :335-338 e :381-384).
///
/// ============================ POR QUE NAO E UM BUFF ============================
/// O extrator so lia CONSTANTE (`physoffBuff += 0.5`) e estas linhas caiam no chao: as tres skills
/// saiam do `skills.json` so com o `staminagainMod`, e o "+1% de BP e potencial escondido" que a
/// descricao promete nunca acontecia. Mas elas tambem nao cabem no canal dos buffs, por tres razoes:
///
///   1. o VALOR depende da ficha no instante da compra (1% do BP DE ENTAO), e nao e uma constante
///      do catalogo;
///   2. NAO se reaplica no login. O `BP` e persistido; reaplicar "1% do BP" a cada entrada e o
///      personagem que vira deus em vinte relogs -- a idempotencia dos buffs e por RAZAO, e aqui o
///      razao e o proprio numero que foi somado, guardado por skill (<see cref="SkillBook.GanhosNaCompra"/>);
///   3. ao ESQUECER, o DM devolve exatamente o que somou (`savant.BP -= storedBP`, :98) -- o
///      `storedBP` do datum e o nosso razao.
///
/// A expressao e avaliada pelo MESMO avaliador das regras de arvore (<see cref="RegraDeArvore"/>),
/// com o lutador como contexto: `BP` e `relBPmax` sao campos publicos da ficha, e `max()` entrou na
/// gramatica por causa daqui.
/// ==============================================================================
/// </summary>
public sealed class GanhoNaCompra
{
	/// <summary>O campo do lutador que recebe (`BP`, `hiddenpotential`).</summary>
	public string Campo = "";

	/// <summary>+1 soma, -1 subtrai (`-=`).</summary>
	public int Sinal = 1;

	/// <summary>A expressao como o extrator a escreveu (sem `savant.`, sem espacos).</summary>
	public string Expressao = "";

	private RegraDeArvore.Expressao? _expr;

	/// <summary>Le `campo+=expr` / `campo-=expr`. Nulo se a expressao nao e legivel pelo Core.</summary>
	public static GanhoNaCompra? Parse(string cru)
	{
		int op = cru.IndexOf("+=", StringComparison.Ordinal);
		int sinal = 1;
		if (op < 0) { op = cru.IndexOf("-=", StringComparison.Ordinal); sinal = -1; }
		if (op <= 0 || op + 2 >= cru.Length) return null;
		var g = new GanhoNaCompra { Campo = cru[..op].Trim(), Sinal = sinal, Expressao = cru[(op + 2)..].Trim() };
		g._expr = RegraDeArvore.Expressao.Parse(g.Expressao);
		if (g._expr == null || g.Campo.Length == 0) return null;
		return g;
	}

	/// <summary>Quanto esta compra rende AGORA, sobre esta ficha. Nulo = a expressao le algo que a ficha nao tem.</summary>
	public double? Avaliar(Fighter f)
	{
		_expr ??= RegraDeArvore.Expressao.Parse(Expressao);
		if (_expr == null) return null;
		RegraDeArvore.Valor v = _expr.Avaliar(ContextoDeRegra.De(f, f.Race, f.Class));
		return v.Num is { } n ? n * Sinal : null;
	}

	/// <summary>
	/// APLICA os ganhos de <paramref name="s"/> na ficha, UMA vez, e registra no livro o que somou --
	/// em cada campo o valor de VERDADE somado, pra que <see cref="Desfazer"/> devolva exatamente
	/// isso. Devolve quanto foi somado por campo (vazio = nada a fazer, ou ja aplicado).
	///
	/// Ja aplicado nao aplica de novo: e a guarda contra o relog e contra a compra repetida.
	/// </summary>
	public static Dictionary<string, double> Aplicar(Fighter f, SkillBook livro, Skill s)
	{
		var somou = new Dictionary<string, double>(StringComparer.Ordinal);
		if (s.Compra.Length == 0 || livro.GanhosNaCompra.ContainsKey(s.Path)) return somou;

		foreach (GanhoNaCompra g in s.Compra)
		{
			System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(g.Campo);
			if (fi == null) continue;
			if (g.Avaliar(f) is not { } quanto) continue;
			fi.SetValue(f, (double)fi.GetValue(f)! + quanto);
			somou[g.Campo] = somou.GetValueOrDefault(g.Campo) + quanto;
		}
		if (somou.Count > 0) livro.RegistrarGanho(s.Path, somou);
		return somou;
	}

	/// <summary>
	/// DEVOLVE o que a compra de <paramref name="path"/> somou -- o `before_forget()` (`savant.BP -=
	/// storedBP`, Bodybuilding.dm:98). Nada acontece se a skill nunca somou nada.
	///
	/// O DM escreve `hiddenpotential = min(0, hiddenpotential - hiddenpot)` (:100) -- `min` onde
	/// claramente quis `max`: esquecer a skill zeraria (ou negativaria) o potencial escondido inteiro.
	/// NAO copiado: aqui o campo devolve o que ganhou e para no zero, que e o que a linha quis dizer.
	/// </summary>
	public static Dictionary<string, double> Desfazer(Fighter f, SkillBook livro, string path)
	{
		if (!livro.GanhosNaCompra.TryGetValue(path, out Dictionary<string, double>? somou)) return [];
		var devolveu = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach ((string campo, double quanto) in somou)
		{
			System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(campo);
			if (fi == null) continue;
			double atual = (double)fi.GetValue(f)!;
			// o piso do BP e 1 (a ficha nasce em 1 e nada no port o deixa cair a zero)
			double piso = campo == "BP" ? 1 : 0;
			fi.SetValue(f, Math.Max(piso, atual - quanto));
			devolveu[campo] = quanto;
		}
		livro.EsquecerGanho(path);
		return devolveu;
	}
}
