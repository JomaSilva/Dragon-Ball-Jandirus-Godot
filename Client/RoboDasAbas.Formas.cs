using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Formas da `--diagabas` (F7), e as PECAS COMUNS das familias desta
/// frente (F7 Formas, F8 Cargos, F10 Gente, F11 Mundo, F12 Nav). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O QUE ELA PROVA, E COMO ============================
/// Pelo que a tela ESCREVEU, nunca pelo que o codigo mandou escrever: a faixa da forma pelo
/// `ValorDesenhado`, as duas linhas de contrato pelos parsers da `--diagbancada` (copiados aqui, ver
/// <see cref="MultiplicadorEscrito"/>), os cartoes e as pilulas pelo metadado que a peca escreve, a
/// chapa pela moda de pixel na foto.
///
/// E COM O CONTRA-EXEMPLO VIVO: a bancada nasce na base (nenhum cartao de forma, faixa "normal"),
/// forca o Super Saiyajin pelo verb de admin (um cartao, pilula EM USO, multiplicador acima de 1,5x)
/// e volta pra base. Nascer dentro do estado nunca testa a ENTRADA nele -- e a entrada e onde a
/// faixa, a pilula e o cartao tem que trocar de uma vez.
///
/// AS REGRAS SAO FUNCOES PURAS, e no fim cada uma recebe uma amostra estragada e tem que ficar
/// vermelha (a rodada de injecao). Regra que nao reprova o defeito que existe pra pegar e enfeite.
/// ==================================================================================
/// </summary>
public partial class RoboDasAbas
{
	// =====================================================================
	// AS REGRAS DA ABA FORMS -- puras, pra rodada de injecao chamar as MESMAS
	// =====================================================================
	/// <summary>A faixa "Forma" escreve o nome que a ficha tem? ("normal" na base, "Super Saiyajin" no SSJ1.)</summary>
	private static bool FaixaConcordaComAFicha(string? faixa, string nome) =>
		faixa != null && faixa.StartsWith(nome, StringComparison.Ordinal);

	/// <summary>"98%" ou "98,5%" -- exatamente como a `--diagbancada` desmonta (`Razao`), sem legenda pendurada.</summary>
	private static readonly Regex PoderEfetivoEscrito = new(@"^\d+(,\d)?%$", RegexOptions.Compiled);

	private static bool PoderEfetivoBemEscrito(string? txt, double inteireza) =>
		txt != null && PoderEfetivoEscrito.IsMatch(txt)
		&& Math.Abs(PrimeiroNumeroEscrito(txt) - inteireza * 100) <= 0.11;

	/// <summary>
	/// "×1,09" -- comeca com o ×, e o parser da `--diagbancada` le dele o mesmo numero da ficha. A
	/// tolerancia e a do `MultTexto`: duas casas ate 10, uma ate 100, nenhuma acima (0,5% no pior caso).
	/// </summary>
	private static bool MultiplicadorBemEscrito(string? txt, double mult)
	{
		if (txt == null || !txt.StartsWith('×')) return false;
		double lido = MultiplicadorEscrito(txt);
		return !double.IsNaN(lido) && Math.Abs(lido - mult) <= Math.Max(0.011, mult * 0.01);
	}

	// =====================================================================
	// OS PARSERS DA `--diagbancada`, COPIADOS (RoboDeBancada.cs: Numero, PrimeiroNumero, Multiplicador)
	// =====================================================================
	// Copiados e nao compartilhados porque a `RoboDeBancada` e outra classe e os dela sao privados; o
	// que se quer afirmar aqui e "o texto que a aba escreve e o que AQUELA bancada le", entao o parser
	// tem que ser o mesmo, letra por letra.
	private static readonly Regex TokenDeNumeroEscrito = new(@"[-+]?\d[\d.,]*", RegexOptions.Compiled);

	private static double NumeroEscrito(string? s)
	{
		if (s is null) return double.NaN;
		s = s.Trim();
		if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out double v)) return v;
		if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
		return double.NaN;
	}

	/// <summary>O primeiro numero de uma frase. "×1,09" -> 1,09.</summary>
	private static double PrimeiroNumeroEscrito(string? txt)
	{
		if (txt is null) return double.NaN;
		Match m = TokenDeNumeroEscrito.Match(txt);
		return m.Success ? NumeroEscrito(m.Value) : double.NaN;
	}

	/// <summary>"×2,77" -> 2,77;  "×1,2 mil" -> 1200. Desfaz o encurtamento do `MultTexto` -- e e por
	/// causa do " M"/" B" daqui que a linha do multiplicador NAO pode ser uma faixa com legenda.</summary>
	private static double MultiplicadorEscrito(string? txt)
	{
		double v = PrimeiroNumeroEscrito(txt);
		if (double.IsNaN(v) || txt is null) return v;
		if (txt.Contains(" mil", StringComparison.Ordinal)) return v * 1e3;
		if (txt.Contains(" M", StringComparison.Ordinal)) return v * 1e6;
		if (txt.Contains(" B", StringComparison.Ordinal)) return v * 1e9;
		return v;
	}

	// =====================================================================
	// PECAS COMUNS DAS FAMILIAS DESTA FRENTE -- ler cartoes, pilulas e linhas pelo METADADO
	// =====================================================================
	/// <summary>Os cartoes visiveis de um TIPO (`cartao` = "forma", "cargo", "pessoa", "secao").</summary>
	private static IEnumerable<PanelContainer> CartoesDeSecao(Node raiz, string tipo) =>
		Todos(raiz).OfType<PanelContainer>()
			.Where(p => p.IsVisibleInTree() && p.HasMeta("cartao") && p.GetMeta("cartao").AsString() == tipo);

	/// <summary>O cartao visivel cujo `titulo` e este (o titulo da secao, ou o nome do item).</summary>
	private static PanelContainer? CartaoComTitulo(Node raiz, string titulo) =>
		Todos(raiz).OfType<PanelContainer>().FirstOrDefault(p =>
			p.IsVisibleInTree() && p.HasMeta("cartao") && p.HasMeta("titulo") && p.GetMeta("titulo").AsString() == titulo);

	/// <summary>Os textos das pilulas visiveis debaixo de um node, na ordem da arvore.</summary>
	private static List<string> PilulasEm(Node raiz) =>
		[.. Todos(raiz).OfType<PanelContainer>()
			.Where(p => p.IsVisibleInTree() && p.HasMeta("pilula"))
			.Select(p => p.GetMeta("pilula").AsString())];

	/// <summary>A linha rotulo/valor com este rotulo (o mesmo metadado que o `ValorDesenhado` procura).</summary>
	private static HBoxContainer? LinhaEscrita(Node raiz, string rotulo) =>
		Todos(raiz).OfType<HBoxContainer>().FirstOrDefault(h => h.HasMeta("linha") && h.GetMeta("linha").AsString() == rotulo);

	private static bool DentroDoCartao(Node? n, PanelContainer? card) =>
		n != null && card != null && card.IsAncestorOf(n);

	/// <summary>
	/// O DESENHO INTEIRO DE UMA PAGINA, EM TEXTO: tipo, texto e metadados de cada node, na ordem da
	/// arvore. E a regua do determinismo: duas montagens da MESMA ficha tem que dar a mesma string.
	/// </summary>
	private static string ArvoreEmTexto(Node raiz)
	{
		var sb = new StringBuilder();
		foreach (Node n in Todos(raiz))
		{
			sb.Append(n.GetType().Name);
			if (n is Label l) sb.Append(':').Append(l.Text);
			else if (n is Button b) sb.Append(':').Append(b.Text).Append(b.Disabled ? "(off)" : "");
			if (n.HasMeta("cartao"))
				sb.Append(" cartao=").Append(n.GetMeta("cartao").AsString()).Append('/')
				  .Append(n.HasMeta("titulo") ? n.GetMeta("titulo").AsString() : "");
			if (n.HasMeta("pilula")) sb.Append(" pilula=").Append(n.GetMeta("pilula").AsString());
			if (n is CanvasItem ci && !ci.Visible) sb.Append(" (oculto)");
			sb.Append('\n');
		}
		return sb.ToString();
	}

	/// <summary>Remonta a aba da vez (ignorando a assinatura) e diz se a arvore saiu IGUAL. Ver <see cref="ArvoreEmTexto"/>.</summary>
	private async System.Threading.Tasks.Task<(bool Igual, int Nodes)> RemontaIgual(MenuJogo menu, Control pg)
	{
		string a = ArvoreEmTexto(pg);
		menu.ForcarRedesenho();
		await Quadros(3);
		string b = ArvoreEmTexto(pg);
		return (a == b, a.Count(ch => ch == '\n'));
	}

	/// <summary>Rola a pagina ate o alvo aparecer, pra foto e pra leitura de pixel.</summary>
	private async System.Threading.Tasks.Task RolarAteVer(Control? alvo)
	{
		if (alvo != null && Ancestral<ScrollContainer>(alvo) is { } rol) rol.EnsureControlVisible(alvo);
		await Quadros(3);
	}

	/// <summary>A chapa de um cartao, medida na foto: a moda de pixel do retangulo dele tem que ser da paleta do tema.</summary>
	private void ChapaDoCartaoNaPaleta(string oque, Image? foto, PanelContainer? card, Color? esperada = null)
	{
		if (card == null) { Checa(oque, false, "sem cartao pra medir"); return; }
		(Color cor, float frac) = Moda(foto, Caixa(card.GetGlobalRect(), 4));
		bool ok = esperada is { } e ? Perto(cor, e) : NaPaleta(cor);
		ChecaNoPixel(oque, foto != null, ok,
			$"moda {Hex(cor)} ({frac * 100:0}% do cartao){(esperada is { } x ? $", esperada {Hex(x)}" : "")}, retangulo {card.GetGlobalRect()}");
	}

	/// <summary>
	/// QUANTAS FORMAS A FICHA DIZ QUE ESTE CORPO DESPERTOU -- a MESMA regra da aba (maestria > 0, ou a
	/// forma em uso), lida do pacote e nao da tela. As formas de disciplina ficam de fora porque a
	/// bancada nasce sem disciplina (o crivo delas e outro e nao e exercitado aqui).
	/// </summary>
	private static int FormasDespertasNaFicha(GameClient cli)
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach ((ushort forma, float pct) in cli.Atributos.Maestrias ?? [])
			if (pct > 0 && Catalogo.PorRede(forma) is { } d && d.Id != Catalogo.IdBase) ids.Add(d.Id);
		if (Catalogo.PorRede(cli.Atributos.FormaAtual) is { } atual && atual.Id != Catalogo.IdBase) ids.Add(atual.Id);
		return ids.Count;
	}

	// =====================================================================
	// F7 -- FORMS
	// =====================================================================
	private async System.Threading.Tasks.Task F7_Formas(MenuJogo menu, GameClient cli)
	{
		Nota("--- F7: Formas: a faixa da forma, o cartao Agora, um cartao por forma despertada ---");
		cli.Falou += EcoarFalaDoJogo;   // o `admin_forma` responde pelo chat; ver `EcoarFalaDoJogo` (Cargos)
		try { await F7_FormasDeVerdade(menu, cli); }
		finally { cli.Falou -= EcoarFalaDoJogo; }
	}

	private async System.Threading.Tasks.Task F7_FormasDeVerdade(MenuJogo menu, GameClient cli)
	{
		menu.IrPara("Forms");
		await Quadros(3);
		Control? pg = menu.PaginaDeTeste("Forms");
		Checa("a pagina de Forms existe e esta na tela", pg is { Visible: true });
		if (pg == null) return;

		// ---------------- 1. NA BASE, que e como a bancada nasce ----------------
		ushort redeBase = Catalogo.Rede(Catalogo.IdBase);
		bool naBase = cli.Atributos.FormaAtual == redeBase || cli.Atributos.FormaAtual == 0;
		Checa("PRECONDICAO: a bancada esta na forma base", naBase, $"FormaAtual={cli.Atributos.FormaAtual}");

		string? faixa = menu.ValorDesenhado("Forms", "Forma");
		Checa("a FAIXA 'Forma' diz 'normal' na base", FaixaConcordaComAFicha(faixa, "normal"), faixa ?? "(nula)");
		Checa("...e e uma FAIXA (numero grande + legenda), nao a linha de sempre",
			  Todos(pg).OfType<PanelContainer>().Any(n => n.HasMeta("faixa") && n.GetMeta("faixa").AsString() == "Forma"));

		PanelContainer? agora = CartaoComTitulo(pg, "Agora");
		Checa("ha um cartao 'Agora'", agora != null);
		ContratoDoAgora(menu, pg, cli, agora, "na base");

		int despertas = FormasDespertasNaFicha(cli);
		int cartoes = CartoesDeSecao(pg, "forma").Count();
		Checa("um cartao por forma DESPERTADA na ficha (maestria > 0 ou em uso), e nenhum a mais",
			  cartoes == despertas, $"{cartoes} cartao(oes), {despertas} na ficha");
		if (despertas == 0)
			Checa("...e sem nenhuma, a aba diz que nao ha (a frase de vazio)",
				  Rotulos(pg).Any(l => l.Text.StartsWith("Nenhuma, ainda", StringComparison.Ordinal)));
		Checa("CONTRA-EXEMPLO: nenhuma pilula 'EM USO' com a forma base", !PilulasEm(pg).Contains("EM USO"));

		await RolarAteVer(agora);
		Image? f1 = await Foto();
		await Guardar("forms-01-na-base", f1);
		ChapaDoCartaoNaPaleta("a chapa do cartao 'Agora' e da paleta do tema (moda de pixel)", f1, agora);

		// ---------------- 2. O CONTRA-EXEMPLO VIVO: forcar o Super Saiyajin ----------------
		// Pelo verb de admin (`admin_forma`), que e o que a `--diagbancada` tambem usa: ele ignora BP,
		// maestria e linhagem, enche o Ki e passa pelo MESMO `AplicarForma` do jogo. A ficha lenta traz
		// `FormaAtual` e a rapida traz o `MultTotal`; os dois tem que chegar antes de ler a tela.
		ushort redeSsj = Catalogo.Rede(Catalogo.IdSsj1);
		string nomeSsj = Catalogo.NomeDe(Catalogo.Def(Catalogo.IdSsj1), false);
		cli.SendVerbo("admin_forma", Catalogo.IdSsj1);
		bool virou = await Ate(() => cli.Atributos.FormaAtual == redeSsj && cli.Sheet.MultTotal >= 1.5, 12);
		Checa($"admin_forma leva a bancada ao {nomeSsj} (FormaAtual e MultTotal chegam ao cliente)", virou,
			  $"FormaAtual={cli.Atributos.FormaAtual} (esperava {redeSsj}), MultTotal={cli.Sheet.MultTotal:0.###}");

		if (virou)
		{
			menu.ForcarRedesenho();
			await Quadros(3);

			faixa = menu.ValorDesenhado("Forms", "Forma");
			Checa($"a FAIXA agora diz '{nomeSsj}'", FaixaConcordaComAFicha(faixa, nomeSsj), faixa ?? "(nula)");
			Checa("...com a maestria e o dreno na legenda",
				  faixa != null && faixa.Contains("maestria", StringComparison.Ordinal) && faixa.Contains("dreno", StringComparison.Ordinal),
				  faixa ?? "(nula)");

			List<PanelContainer> deForma = CartoesDeSecao(pg, "forma").ToList();
			PanelContainer? card = CartaoComTitulo(pg, nomeSsj);
			Checa($"ha exatamente UM cartao de forma, e e o do {nomeSsj}", deForma.Count == 1 && card != null,
				  $"{deForma.Count} cartao(oes): {string.Join(", ", deForma.Select(c => c.GetMeta("titulo").AsString()))}");
			Checa("...com a pilula EM USO", card != null && PilulasEm(card).Contains("EM USO"),
				  card == null ? "sem cartao" : string.Join("|", PilulasEm(card)));
			Checa("...e a barra de maestria (ProgressBar com o metadado 'barra' = maestria)",
				  card != null && Todos(card).OfType<ProgressBar>().Any(b => b.HasMeta("barra") && b.GetMeta("barra").AsString() == "maestria"));
			Checa("...e a linha do dreno de Ki", card != null && LinhaEscrita(card, "dreno de Ki") != null);
			Checa("CONTRA-EXEMPLO: o cartao NAO se chama pelo nome cru da entrada em vez do funil `NomeDe` (os dois coincidem aqui, entao a prova e a faixa e o cartao dizerem o MESMO nome)",
				  card != null && faixa != null && faixa.StartsWith(card.GetMeta("titulo").AsString(), StringComparison.Ordinal));

			string? mult = menu.ValorDesenhado("Forms", "Multiplicador total");
			Checa("o multiplicador total subiu junto (>= 1,5x) e continua parseavel pela --diagbancada",
				  MultiplicadorBemEscrito(mult, cli.Sheet.MultTotal) && MultiplicadorEscrito(mult) >= 1.5,
				  $"texto '{mult}', ficha {cli.Sheet.MultTotal:0.###}");
			ContratoDoAgora(menu, pg, cli, CartaoComTitulo(pg, "Agora"), "em Super Saiyajin");

			await RolarAteVer(card);
			Image? f2 = await Foto();
			await Guardar("forms-02-super-saiyajin", f2);
			ChapaDoCartaoNaPaleta("a chapa do cartao da forma e a clara do tema (Tema.PainelClaro)", f2, card, Tema.PainelClaro);

			(bool igual, int nodes) = await RemontaIgual(menu, pg);
			Checa("DETERMINISMO: duas montagens da mesma ficha dao a MESMA arvore de nodes", igual, $"{nodes} nodes");
		}

		// ---------------- 3. DE VOLTA A BASE ----------------
		cli.SendVerbo("admin_forma", Catalogo.IdBase);
		bool voltou = await Ate(() => (cli.Atributos.FormaAtual == redeBase || cli.Atributos.FormaAtual == 0) && cli.Sheet.MultTotal < 1.5, 12);
		menu.ForcarRedesenho();
		await Quadros(3);
		Checa("de volta a base: a ficha lenta e a rapida voltaram", voltou,
			  $"FormaAtual={cli.Atributos.FormaAtual}, MultTotal={cli.Sheet.MultTotal:0.###}");
		faixa = menu.ValorDesenhado("Forms", "Forma");
		Checa("...a faixa volta a dizer 'normal'", FaixaConcordaComAFicha(faixa, "normal"), faixa ?? "(nula)");
		Checa("...nenhum cartao diz EM USO, e a forma despertada continua listada se a maestria passou de zero",
			  !PilulasEm(pg).Contains("EM USO") && CartoesDeSecao(pg, "forma").Count() == FormasDespertasNaFicha(cli),
			  $"{CartoesDeSecao(pg, "forma").Count()} cartao(oes), {FormasDespertasNaFicha(cli)} na ficha");

		// ---------------- 4. AS INJECOES: cada regra recebe uma amostra estragada ----------------
		Injeta("a regra do multiplicador reprova o texto sem o × ('1,09')", !MultiplicadorBemEscrito("1,09", 1.09));
		Injeta("...e reprova o numero que nao bate com a ficha ('×1,09' contra 2,0)", !MultiplicadorBemEscrito("×1,09", 2.0));
		Injeta("...e le '×1,2 mil' como 1200 (o parser da --diagbancada)", Math.Abs(MultiplicadorEscrito("×1,2 mil") - 1200) < 0.5);
		Injeta("a regra do poder efetivo reprova '98%   (x)' (o formato da aba Stats, com legenda)", !PoderEfetivoBemEscrito("98%   (x)", 0.98));
		Injeta("...e reprova '97%' contra uma inteireza de 0,98", !PoderEfetivoBemEscrito("97%", 0.98));
		Injeta("a regra da faixa reprova 'normal' quando a ficha esta em Super Saiyajin", !FaixaConcordaComAFicha("normal   maestria 0%", nomeSsj));
	}

	/// <summary>As duas linhas de contrato do cartao Agora, lidas pelo `ValorDesenhado` e desmontadas como a `--diagbancada` faz.</summary>
	private void ContratoDoAgora(MenuJogo menu, Control pg, GameClient cli, PanelContainer? agora, string quando)
	{
		string? mult = menu.ValorDesenhado("Forms", "Multiplicador total");
		Checa($"[{quando}] 'Multiplicador total' comeca com × e o parser da --diagbancada le dele o MultTotal da ficha",
			  MultiplicadorBemEscrito(mult, cli.Sheet.MultTotal), $"texto '{mult}', ficha {cli.Sheet.MultTotal:0.###}");
		HBoxContainer? linhaMult = LinhaEscrita(pg, "Multiplicador total");
		Checa($"[{quando}] ...e a linha esta DENTRO do cartao Agora e NAO e uma faixa com legenda",
			  DentroDoCartao(linhaMult, agora) && linhaMult != null && !linhaMult.HasMeta("faixa"));

		string? efetivo = menu.ValorDesenhado("Forms", "Poder efetivo");
		Checa($"[{quando}] 'Poder efetivo' casa ^\\d+(,\\d)?%$ e bate com a inteireza da ficha",
			  PoderEfetivoBemEscrito(efetivo, cli.Sheet.Inteireza), $"texto '{efetivo}', ficha {cli.Sheet.Inteireza * 100:0.#}");
		Checa($"[{quando}] ...e tambem mora no cartao Agora", DentroDoCartao(LinhaEscrita(pg, "Poder efetivo"), agora));
	}
}
