using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Ki da `--diagabas`. Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA AFIRMA: o CONTRATO da `--diagbancada` ("Percentual" e "Teto de carga", nas formas de sempre)
/// continua de pe e casa com a ficha; a barra "Ki atual" tem o trilho no teto de carga como a do HUD; a
/// ficha lenta trouxe as DEZENOVE pericias (o fio novo) e a aba desenha uma barra por pericia, rotulada
/// pelo `NomesLegiveis` (nenhum nome cru do DM), escrevendo exatamente o valor que veio; e o pixel do
/// cartao na paleta. Contra-exemplo injetado: um rotulo cru na varredura.
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F5_Ki(MenuJogo menu, GameClient cli)
	{
		Nota("--- F5: Ki: a faixa, o contrato (Percentual / Teto de carga) e as dezenove pericias com nome em portugues ---");
		Button? b = Botao(menu, "Ki");
		Checa("a aba Ki tem botao na barra", b != null);
		if (b == null) return;
		await Clicar(b);
		await Quadros(4);
		Control? pg = menu.PaginaDeTeste("Ki");
		Checa("a pagina de Ki esta montada", pg != null);
		if (pg == null) return;

		// O REDESENHO FORCADO faz a aba ler o MESMO SheetState que a bancada vai comparar (ver `ForcarRedesenho`).
		menu.ForcarRedesenho();
		await Quadros(2);
		SheetState f = cli.Sheet;

		// ---- 1) o contrato ----
		string? pct = menu.ValorDesenhado("Ki", "Percentual");
		Checa("'Percentual' casa ^\\d+(,\\d+)?%$ (o contrato da --diagbancada)", pct != null && Regex.IsMatch(pct, @"^\d+([,.]\d+)?%$"), pct ?? "(nulo)");
		string? teto = menu.ValorDesenhado("Ki", "Teto de carga");
		Checa("'Teto de carga' casa ^\\d+% do tanque$", teto != null && Regex.IsMatch(teto, @"^\d+% do tanque$"), teto ?? "(nulo)");
		Checa("...e os dois sao os numeros da ficha (RazaoDeKi, TrilhoDeKi), no mesmo arredondamento",
			  pct == $"{f.RazaoDeKi * 100:0.#}%" && teto == $"{f.TrilhoDeKi * 100:0}% do tanque", $"{pct} / {teto} vs {f.RazaoDeKi * 100:0.#}% / {f.TrilhoDeKi * 100:0}%");
		string? faixa = menu.ValorDesenhado("Ki", "Ki");
		Checa("a Faixa 'Ki' tem o percentual grande e a legenda com o tanque e o teto",
			  faixa != null && faixa.StartsWith($"{f.RazaoDeKi * 100:0.#}%") && faixa.Contains("do tanque"), faixa ?? "(nula)");

		// ---- 2) a barra de Ki, com o trilho do HUD ----
		List<ProgressBar> barras = Todos(pg).OfType<ProgressBar>().Where(x => x.HasMeta("barra")).ToList();
		ProgressBar? barraKi = barras.FirstOrDefault(x => x.GetMeta("barra").AsString() == "Ki atual");
		Checa("a barra 'Ki atual' tem o trilho no teto de carga (MaxValue = TrilhoDeKi), como a do HUD",
			  barraKi != null && Math.Abs(barraKi.MaxValue - f.TrilhoDeKi) < 1e-6, $"{barraKi?.MaxValue} vs {f.TrilhoDeKi}");
		Checa("...e o valor dela e a razao de Ki da ficha",
			  barraKi != null && Math.Abs(barraKi.Value - Math.Clamp(f.RazaoDeKi, 0, f.TrilhoDeKi)) < 1e-6, $"{barraKi?.Value} vs {f.RazaoDeKi}");
		string? kiAtual = menu.ValorDesenhado("Ki", "Ki atual");
		Checa("'Ki atual' escreve \"N / N\" (atual / tanque)", kiAtual == $"{f.Ki:N0} / {f.MaxKi:N0}", kiAtual ?? "(nulo)");

		// ---- 3) as dezenove pericias ----
		float[] ps = cli.Atributos.Pericias ?? [];
		Checa("a ficha lenta trouxe as dezenove pericias (o fio novo no fim do AtributosState)", ps.Length == 19, $"{ps.Length}");
		string[] nomes = Protocol.PericiasDeKi.Select(p => NomesLegiveis.Campo(p.Campo)).ToArray();
		Checa("a tabela de nomes conhece as dezenove, e nenhum nome tem 'skill' dentro",
			  Protocol.PericiasDeKi.All(p => NomesLegiveis.Conhece(p.Campo)) && nomes.Distinct().Count() == 19 && SemNomeCru(nomes), string.Join(" | ", nomes));
		List<ProgressBar> barrasDePericia = barras.Where(x => nomes.Contains(x.GetMeta("barra").AsString())).ToList();
		Checa("ha uma barra por pericia, rotulada pelo NomesLegiveis (19)", barrasDePericia.Count == 19, $"{barrasDePericia.Count}");
		List<string> textos = Rotulos(pg).Select(l => l.Text).ToList();
		Checa("nenhum rotulo da aba com nome cru do DM ('skill', 'flightability', 'kiarmor')", SemNomeCru(textos),
			  string.Join(" | ", textos.Where(t => !SemNomeCru([t]))));
		Injeta("um rotulo 'kiawarenessskill' injetado na varredura reprova a regra do nome legivel", !SemNomeCru([.. textos, "kiawarenessskill"]));

		bool valores = ps.Length == 19;
		var errados = new List<string>();
		for (int i = 0; i < ps.Length && i < nomes.Length; i++)
		{
			string? v = menu.ValorDesenhado("Ki", nomes[i]);
			if (v != $"{ps[i]:0.#}") { valores = false; errados.Add($"{nomes[i]}: '{v}' vs '{ps[i]:0.#}'"); }
		}
		Checa("cada barra escreve o valor que veio no fio ({v:0.#}), na ordem da tabela Protocol.PericiasDeKi", valores, string.Join(" | ", errados));
		Checa("...e as barras sao sobre 100 (o teto pratico): MaxValue 1, valor = pericia/100",
			  barrasDePericia.All(x => Math.Abs(x.MaxValue - 1) < 1e-9)
			  && barrasDePericia.All(x => Math.Abs(x.Value - Math.Min(ps[Array.IndexOf(nomes, x.GetMeta("barra").AsString())] / 100.0, 1)) < 1e-6));

		// ---- 4) os cartoes: energia, dominio (8) e tecnicas (11) ----
		List<PanelContainer> cartoes = Todos(pg).OfType<PanelContainer>().Where(x => x.HasMeta("cartao")).ToList();
		List<string> titulos = cartoes.Select(x => x.GetMeta("titulo").AsString()).ToList();
		Checa("os cartoes 'Energia', 'Domínio de Ki' e 'Técnicas de Ki' existem", new[] { "Energia", "Domínio de Ki", "Técnicas de Ki" }.All(titulos.Contains), string.Join(",", titulos));
		PanelContainer? dominio = cartoes.FirstOrDefault(x => x.GetMeta("titulo").AsString() == "Domínio de Ki");
		PanelContainer? tecnicas = cartoes.FirstOrDefault(x => x.GetMeta("titulo").AsString() == "Técnicas de Ki");
		int nd = dominio == null ? 0 : Todos(dominio).OfType<ProgressBar>().Count();
		int nt = tecnicas == null ? 0 : Todos(tecnicas).OfType<ProgressBar>().Count();
		Checa("o dominio tem 8 barras e as tecnicas 11 (o corte Protocol.PericiasDeDominio)", nd == Protocol.PericiasDeDominio && nt == 19 - Protocol.PericiasDeDominio, $"{nd} / {nt}");

		// ---- 5) a foto e o pixel ----
		Image? foto = await Foto();
		await Guardar("ki-01-a-aba", foto);
		PanelContainer? energia = cartoes.FirstOrDefault(x => x.GetMeta("titulo").AsString() == "Energia");
		if (energia != null)
		{
			(Color cor, float frac) = Moda(foto, Caixa(energia.GetGlobalRect(), 4));
			ChecaNoPixel("o cartao 'Energia' e pintado com a paleta do tema (moda de pixel do retangulo dele)",
						 foto != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}% do cartao)");
		}
	}

	/// <summary>A REGRA DO NOME LEGIVEL, como funcao pura pra caber contra-exemplo: nada de `...skill`, `flightability`, `kiarmor` na tela.</summary>
	private static bool SemNomeCru(IEnumerable<string> textos) =>
		!textos.Any(t => t.Contains("skill", StringComparison.OrdinalIgnoreCase)
					  || t.Contains("flightability", StringComparison.Ordinal)
					  || t.Contains("kiarmor", StringComparison.Ordinal));
}
