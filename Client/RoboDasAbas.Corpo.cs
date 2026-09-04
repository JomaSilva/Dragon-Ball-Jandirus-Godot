using System.Text.RegularExpressions;
using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Corpo da `--diagabas`. Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA AFIRMA, contra a tela montada de verdade: a Faixa "Vida" com a legenda de feridos/decepados;
/// o boneco em escala 2 dentro da moldura; uma barra por membro vivo (o Saiyajin da bancada tem 15) na
/// COR DA FAIXA do boneco (`BodyDoll.Cor`), uma pilula por decepado; os cartoes por regiao com acento;
/// a palavra de ferimento do `injury_word` nas seis faixas; e o pixel do cartao na paleta. Com dois
/// contra-exemplos injetados: uma cor trocada e um nome cru do fio.
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F4_Corpo(MenuJogo menu, GameClient cli)
	{
		Nota("--- F4: Corpo: o boneco em dobro, um cartao por regiao, uma barra por membro na cor da faixa ---");
		Button? b = Botao(menu, "Body");
		Checa("a aba Body tem botao na barra", b != null);
		if (b == null) return;
		await Clicar(b);
		await Quadros(4);
		Control? pg = menu.PaginaDeTeste("Body");
		Checa("a pagina de Body esta montada", pg != null);
		if (pg == null) return;

		List<Protocol.ParteState> partes = cli.Corpo;
		Checa("o corpo chegou do servidor (a lista de membros nao esta vazia)", partes.Count > 0, $"{partes.Count} membros");
		int feridos = partes.Count(p => !p.Decepado && p.Vida < 100);
		int decepados = partes.Count(p => p.Decepado);
		int vivos = partes.Count - decepados;

		// ---- 1) a faixa ----
		string? vida = menu.ValorDesenhado("Body", "Vida");
		Checa("a Faixa 'Vida' escreve a vida em % e a legenda \"N membros feridos · M decepados\"",
			  vida != null && Regex.IsMatch(vida, @"^\d+%   \d+ membros feridos · \d+ decepados$"), vida ?? "(nula)");
		Checa("...e a legenda conta o que a lista de membros tem",
			  vida != null && vida.EndsWith($"{feridos} membros feridos · {decepados} decepados"), vida ?? "(nula)");

		// ---- 2) o boneco ----
		BodyDoll? boneco = Todos(pg).OfType<BodyDoll>().FirstOrDefault();
		Checa("o boneco de dano (BodyDoll) esta na pagina, em escala 2, numa moldura que reserva 192x192",
			  boneco != null && boneco.Scale == new Vector2(2, 2)
			  && boneco.GetParent() is Control moldura && moldura.CustomMinimumSize == new Vector2(192, 192),
			  $"escala {boneco?.Scale} moldura {(boneco?.GetParent() as Control)?.CustomMinimumSize}");

		// ---- 3) uma barra por membro vivo, uma pilula por decepado ----
		List<ProgressBar> barras = Todos(pg).OfType<ProgressBar>().Where(x => x.HasMeta("barra")).ToList();
		Checa($"ha uma barra por membro vivo ({vivos}) -- e o Saiyajin da bancada tem 15 membros",
			  barras.Count == vivos && partes.Count == 15, $"{barras.Count} barras, {partes.Count} membros, {decepados} decepados");
		int pilulas = Todos(pg).OfType<PanelContainer>().Count(x => x.HasMeta("pilula") && x.GetMeta("pilula").AsString() == "DECEPADO");
		Checa("...e uma pilula DECEPADO por membro decepado", pilulas == decepados, $"{pilulas} pilulas, {decepados} decepados");

		// ---- 4) as cores: a barra de cada membro e a cor da FAIXA do boneco pra vida dele ----
		var vidaPorNome = partes.Where(p => !p.Decepado).ToDictionary(p => MenuJogo.NomeBonitoDoMembro(p.Nome), p => (int)p.Vida);
		List<(string Nome, Color Cor)> lidas = barras
			.Select(x => (x.GetMeta("barra").AsString(), (x.GetThemeStylebox("fill") as StyleBoxFlat)?.BgColor ?? new Color(0, 0, 0)))
			.ToList();
		Checa("cada barra tem a cor da FAIXA do boneco (BodyDoll.Cor) pra vida daquele membro",
			  CoresDasFaixas(lidas, vidaPorNome), string.Join(" ", lidas.Select(l => $"{l.Nome}={Hex(l.Cor)}")));
		if (lidas.Count > 0)
		{
			List<(string Nome, Color Cor)> trocada = [.. lidas];
			trocada[0] = (trocada[0].Nome, BodyDoll.CorDecepado);
			Injeta("uma barra pintada de roxo (a cor de decepado) no lugar da cor da faixa reprova a prova de cores",
				   !CoresDasFaixas(trocada, vidaPorNome), $"{trocada[0].Nome} -> {Hex(BodyDoll.CorDecepado)}");
		}

		// ---- 5) as regioes, com acento ----
		List<string> titulos = Todos(pg).OfType<PanelContainer>().Where(x => x.HasMeta("cartao")).Select(x => x.GetMeta("titulo").AsString()).ToList();
		Checa("os cartoes sao Corpo, Cabeça, Tronco, Braços, Pernas e Rabo (o Saiyajin tem rabo)",
			  new[] { "Corpo", "Cabeça", "Tronco", "Braços", "Pernas", "Rabo" }.All(titulos.Contains), string.Join(",", titulos));
		Checa("nenhum nome de membro veio cru do fio (Cabeca, Cerebro, Abdomen, Orgaos, Braco, Mao, Pe -- sem acento)",
			  !Rotulos(pg).Any(l => NomeCruDoFio(l.Text)));
		Injeta("um rotulo 'Braco esquerdo' injetado na varredura reprova a regra do acento", NomeCruDoFio("Braco esquerdo"));

		// ---- 6) a palavra de ferimento (o injury_word do DM, HtmlUI.dm:313-319), nas seis faixas e nas bordas ----
		Checa("a palavra de ferimento e o injury_word do DM: 100 Saudável | 99-80 Levemente ferido | 79-60 Ferido | 59-40 Gravemente ferido | 39-20 Crítico | 19-0 Quebrado",
			  MenuJogo.PalavraDeFerimento(100) == "Saudável"
			  && MenuJogo.PalavraDeFerimento(99) == "Levemente ferido" && MenuJogo.PalavraDeFerimento(80) == "Levemente ferido"
			  && MenuJogo.PalavraDeFerimento(79) == "Ferido" && MenuJogo.PalavraDeFerimento(60) == "Ferido"
			  && MenuJogo.PalavraDeFerimento(59) == "Gravemente ferido" && MenuJogo.PalavraDeFerimento(40) == "Gravemente ferido"
			  && MenuJogo.PalavraDeFerimento(39) == "Crítico" && MenuJogo.PalavraDeFerimento(20) == "Crítico"
			  && MenuJogo.PalavraDeFerimento(19) == "Quebrado" && MenuJogo.PalavraDeFerimento(0) == "Quebrado");
		Checa("CONTRA-EXEMPLO: a palavra e a cor cortam no MESMO lugar (80 e 79 tem palavras E cores diferentes)",
			  MenuJogo.PalavraDeFerimento(80) != MenuJogo.PalavraDeFerimento(79) && !Perto(BodyDoll.Cor(80), BodyDoll.Cor(79)));
		string? cabeca = menu.ValorDesenhado("Body", "Cabeça");
		Checa("a linha 'Cabeça' escreve \"NN%   ·   palavra\", e a palavra e a da vida dela",
			  cabeca != null && Regex.IsMatch(cabeca, @"^\d+%   ·   .+$")
			  && (!vidaPorNome.TryGetValue("Cabeça", out int vc) || cabeca == $"{vc}%   ·   {MenuJogo.PalavraDeFerimento(vc)}"),
			  cabeca ?? "(nula)");

		// ---- 7) a foto e o pixel ----
		Image? foto = await Foto();
		await Guardar("corpo-01-a-aba", foto);
		PanelContainer? tronco = Todos(pg).OfType<PanelContainer>().FirstOrDefault(x => x.HasMeta("cartao") && x.GetMeta("titulo").AsString() == "Tronco");
		if (tronco != null)
		{
			(Color cor, float frac) = Moda(foto, Caixa(tronco.GetGlobalRect(), 4));
			ChecaNoPixel("o cartao 'Tronco' e pintado com a paleta do tema (moda de pixel do retangulo dele)",
						 foto != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}% do cartao)");
		}
	}

	/// <summary>A REGRA DAS CORES, como funcao pura pra caber contra-exemplo: toda barra na cor da faixa da vida do membro.</summary>
	private static bool CoresDasFaixas(List<(string Nome, Color Cor)> lidas, Dictionary<string, int> vidaPorNome) =>
		lidas.Count > 0 && lidas.All(l => vidaPorNome.TryGetValue(l.Nome, out int v) && Perto(l.Cor, BodyDoll.Cor(v)));

	/// <summary>Um texto que e nome de membro DO FIO (sem acento) -- o que nao pode aparecer na tela.</summary>
	private static bool NomeCruDoFio(string t) =>
		t is "Cabeca" or "Cerebro" or "Abdomen" or "Orgaos"
		|| t.StartsWith("Braco ", StringComparison.Ordinal) || t.StartsWith("Mao ", StringComparison.Ordinal) || t.StartsWith("Pe ", StringComparison.Ordinal);
}
