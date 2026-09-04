using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Skills da `--diagabas` (F15). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ELA REPETE OS CONTRATOS DA `--diagskills` F8 -- o rotulo que COMECA com "STRENGTH OF BODY" e traz
/// a contagem, o `ValorDesenhado("Skills", "Basic Training") == "tier 1  ·  buff"`, o rotulo com
/// "ataque físico +0,1" -- porque a aba virou cartoes e e exatamente isso que um redesenho quebra
/// sem querer. Como este personagem nasce sem skill, ela aprende Basic Training pelo MESMO pacote
/// que o botao "Comprar" da ficha manda (`C2S.Aprender`) e so entao le a aba.
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F15_Skills(MenuJogo menu, GameClient cli)
	{
		Nota("--- F15: Skills: o que ja sei, em cartoes por arvore (os contratos da --diagskills F8) ---");
		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		Checa("o catalogo de skills existe no cliente", cat != null);
		if (cat == null) return;
		Skill? bt = cat.Todas.FirstOrDefault(s => s.Nome == "Basic Training");
		Checa("'Basic Training' existe no catalogo", bt != null);
		if (bt == null) return;

		Button? aba = Botao(menu, "Skills");
		if (aba == null) { Checa("achei a aba Skills na barra", false); return; }
		await Clicar(aba);
		await Quadros(2);
		Control? pg = menu.PaginaDeTeste("Skills");
		Checa("a aba Skills esta na tela", pg is { Visible: true } && menu.AbaDeTeste == "Skills", menu.AbaDeTeste);
		if (pg == null) return;

		// ---- antes de aprender: o cartao vazio e o das acoes
		bool nadaAinda = !cli.SkillsAprendidas.Contains(bt.Path);
		if (nadaAinda && cli.SkillsAprendidas.Count == 0)
			Checa("sem nada aprendido, a aba diz isso num cartao ('APRENDIDAS (0)')", Rotulos(pg).Any(l => l.Text.StartsWith("APRENDIDAS") && l.Text.Contains("(0)")));
		List<Verbo> acoes = Verbos.Da(Verbos.Skills).ToList();
		if (acoes.Count > 0)
		{
			Checa($"o cartao 'Ações ({acoes.Count})' lista cada verb de Skills como Button com o texto exato",
				  acoes.All(v => BotaoDeTextoExato(pg, v.Nome) != null) && Rotulos(pg).Any(l => l.Text.StartsWith("AÇÕES") && l.Text.Contains($"({acoes.Count})")),
				  string.Join(", ", acoes.Select(v => v.Nome)));
			Checa("...cada um com a frase do que faz visivel", acoes.All(v => v.Descricao.Length == 0 || Rotulo(pg, v.Descricao) != null));
		}

		// ---- aprende Basic Training pelo MESMO pacote que o botao 'Comprar' da ficha manda
		if (nadaAinda)
		{
			int marcos = cli.MarcosLivres;
			cli.SendAprender(bt.Path);
			bool chegou = await Ate(() => cli.SkillsAprendidas.Contains(bt.Path), 6);
			Checa("Basic Training foi aprendida (C2S.Aprender, o pacote do botao 'Comprar' da ficha)", chegou, $"marcos {marcos} -> {cli.MarcosLivres}");
			if (!chegou) return;
			await Quadros(4);
		}
		pg = menu.PaginaDeTeste("Skills")!;
		Checa("a aba Skills continua a da vez depois de aprender", pg.Visible && menu.AbaDeTeste == "Skills");

		// ---- os contratos da F8
		Checa("CONTRATO F8: um rotulo COMECA com 'STRENGTH OF BODY' e traz a contagem entre parenteses",
			  Rotulos(pg).Any(l => l.Text.StartsWith("STRENGTH OF BODY") && l.Text.Contains("(")));
		PanelContainer? cartao = Todos(pg).OfType<PanelContainer>()
			.FirstOrDefault(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString().StartsWith("Strength of Body"));
		Checa("...e ele e o TITULO de um cartao (a arvore virou cartao)", cartao != null);
		string? valor = menu.ValorDesenhado("Skills", "Basic Training");
		Checa("CONTRATO F8: ValorDesenhado('Skills','Basic Training') == 'tier 1  ·  buff'", valor == "tier 1  ·  buff", valor ?? "(nao achei a linha)");
		HBoxContainer? linha = Todos(pg).OfType<HBoxContainer>().FirstOrDefault(h => h.HasMeta("linha") && h.GetMeta("linha").AsString() == "Basic Training");
		Checa("a linha de Basic Training mora DENTRO do cartao da arvore dela", linha != null && cartao != null && cartao.IsAncestorOf(linha));
		Checa("CONTRATO F8: um rotulo diz o efeito ('ataque físico +0,1')",
			  Rotulos(pg).Any(l => l.Text.Contains("ataque físico +0,1") || l.Text.Contains("ataque físico +0.1")));
		Checa("o nome da skill sai em destaque (Tema.Texto, 14 px), nao apagado como um rotulo comum",
			  linha?.GetChild(0) is Label nome && nome.GetThemeColor("font_color") == Tema.Texto && nome.GetThemeFontSize("font_size") == 14);
		Checa("DEFEITO H continua morto: nenhum rotulo com o `type` cru do DM",
			  !Rotulos(pg).Any(l => l.Text is "Body Buff" or "Sprit Buff" or "misc" or "Misc" or "Physical"));
		Checa("CONTRA-EXEMPLO: 'Evasion Training' (nao aprendida) NAO tem linha", menu.ValorDesenhado("Skills", "Evasion Training") == null);
		Checa("CONTRA-EXEMPLO: sem skill ensinada nao ha cartao 'Avulsas'",
			  !Todos(pg).OfType<PanelContainer>().Any(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString().StartsWith("Avulsas")));
		Checa("...e o cartao 'APRENDIDAS (0)' sumiu", !Rotulos(pg).Any(l => l.Text.StartsWith("APRENDIDAS")));

		// ---- foto e pixel
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? foto = await Foto();
		await Guardar("depois-09-skills", foto);
		if (cartao != null)
		{
			(Color cor, float frac) = Moda(foto, Caixa(cartao.GetGlobalRect(), 4));
			ChecaNoPixel("o cartao da arvore e pintado com a paleta do tema (moda de pixel)", foto != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}%)");
		}
	}
}
