using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Equip da `--diagabas` (F3). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA AFIRMA, pelo que a tela ESCREVEU:
///   * os quatro cartoes existem e o aviso "vem com o sistema de itens" morreu;
///   * a pilula do scouter diz o que o BIT e a MOCHILA dizem ("não tem" nesta bancada, que nasce sem
///     item), "LIGADO" nao aparece com o bit apagado, e o botao de ligar existe APAGADO;
///   * as cinco linhas de aparelhos existem, com o nome do CATALOGO, o icone carregado e a pilula que
///     a mochila manda; "Tirar os pesos" so existe com pesos vestidos;
///   * o cartao de combate le a ficha (cadencia, letal, guarda) e NAO inventa os numeros que o cliente
///     nao tem (dano, penetracao, precisao, deflexao);
///   * contra-exemplos de rotulo, a chapa no pixel, determinismo e quietude, como na F2.
///
/// O QUE ELA NAO MEDE, e por que: o ramo LIGADO (o verbo `item_equipar` acendendo o bit e o BP virando
/// numero). A bancada disca num servidor dedicado e nao ha verbo de jogo que ponha um scouter na
/// mochila -- a janela `ScouterNaMochilaDeTeste` e `internal` do servidor, so alcancavel no mesmo
/// processo. Quem cobre esse ramo e a `--diagbancada` (passos 26-36, `RoboDeBancada.cs`), que equipa
/// o scouter e le `ValorDesenhado("Stats", "Battle Power")` esperando "N   (base N)".
/// </summary>
public partial class RoboDasAbas
{
	/// <summary>Os cinco aparelhos que a aba lista -- a mesma lista de `MenuJogo.AparelhosDaAbaEquip`, na mesma ordem.</summary>
	private static readonly string[] AparelhosEsperados =
		[CatalogoDeItens.NavSystem, CatalogoDeItens.Radar, CatalogoDeItens.Traje, CatalogoDeItens.Respirador, CatalogoDeItens.BrincosPotara];

	/// <summary>Os numeros do `ui_tab_equip` do DM que o cliente NAO recebe. Se um rotulo comecar com um deles, alguem inventou.</summary>
	private static readonly string[] NumerosQueOClienteNaoTem = ["Dano", "Penetra", "Precis", "Deflex", "damage", "accuracy"];

	private async System.Threading.Tasks.Task F3_Equip(MenuJogo menu, GameClient cli)
	{
		Nota("--- F3: Equip: scouter, pesos, aparelhos e combate -- o que esta vestido, ligado e carregado ---");
		Control? pg = await AbrirAbaPeloBotao(menu, "Equip");
		Checa("a pagina de Equip existe e esta visivel depois do clique na aba", pg is { Visible: true }, menu.AbaDeTeste);
		if (pg == null) return;
		menu.ForcarRedesenho();
		await Quadros(2);
		SheetState f = cli.Sheet;
		Inventario mochila = cli.Mochila;

		// ---- os cartoes ----
		List<string> cartoes = TitulosDosCartoes(pg);
		Checa("quatro cartoes de secao: Scouter, Pesos, Aparelhos e roupas, Combate",
			  cartoes.Count == 4 && new[] { "Scouter", "Pesos", "Aparelhos e roupas", "Combate" }.All(cartoes.Contains), string.Join(",", cartoes));
		Checa("CONTRA-EXEMPLO: o aviso 'Vem com o sistema de itens' morreu", !Rotulos(pg).Any(l => l.Text.Contains("Vem com o sistema de itens")));

		// ---- o scouter: a pilula diz o que o bit e a mochila dizem ----
		bool ligado = cli.Atributos.Tem(Protocol.Poder.Scouter);
		bool temScouter = mochila.Quantos(CatalogoDeItens.Scouter) > 0;
		string esperado = ligado ? "LIGADO" : temScouter ? "DESLIGADO" : "não tem";
		PanelContainer? cScouter = CartaoPorTitulo(pg, "Scouter");
		List<string> pilScouter = cScouter == null ? [] : PilulasDaAba(cScouter);
		Checa($"a pilula do scouter diz o que o bit e a mochila dizem ('{esperado}': bit={ligado}, na mochila={temScouter})",
			  pilScouter.Contains(esperado), string.Join(",", pilScouter));
		Checa("CONTRA-EXEMPLO: 'LIGADO' NAO aparece com o bit apagado", ligado || !pilScouter.Contains("LIGADO"));
		Checa("o titulo do cartao do scouter so acende em laranja (destaque) com o bit aceso",
			  cScouter != null && cScouter.GetChild(0) is VBoxContainer corpo && corpo.GetChild(0) is Label t
			  && (t.GetThemeColor("font_color") == Tema.Destaque) == ligado,
			  $"ligado={ligado}");
		Button? bLigar = Botao(pg, "Ligar o scouter") ?? Botao(pg, "Desligar o scouter");
		Checa("o botao de ligar EXISTE e esta APAGADO sem o item na mochila (aceso so com o item)",
			  bLigar != null && bLigar.Disabled == !temScouter,
			  bLigar == null ? "botao ausente" : $"'{bLigar.Text}' {(bLigar.Disabled ? "(apagado)" : "(aceso)")}");
		Checa("...e o texto do botao segue o estado ('Ligar' apagado, 'Desligar' ligado)",
			  bLigar != null && bLigar.Text == (ligado ? "Desligar o scouter" : "Ligar o scouter"), bLigar?.Text ?? "(nulo)");
		Checa("o icone do scouter carregou (o mesmo sprite da mochila: um TextureRect 'Icone' com textura)",
			  cScouter != null && Todos(cScouter).OfType<TextureRect>().Any(t => t.Name == "Icone" && t.Texture != null));

		// ---- os pesos ----
		bool vestidos = cli.Atributos.PesoMult > 1.001;
		string ganho = menu.ValorDesenhado("Equip", "Ganho no treino") ?? "(nulo)";
		Checa("'Ganho no treino' e o PesoMult da ficha lenta, com barra",
			  ganho == $"{cli.Atributos.PesoMult:0.##}x" && BarrasDaAba(pg).Any(b => b.GetMeta("barra").AsString() == "Ganho no treino"), ganho);
		Checa("o botao 'Tirar os pesos' so existe com pesos vestidos", (Botao(pg, "Tirar os pesos") != null) == vestidos, $"vestidos={vestidos} (PesoMult {cli.Atributos.PesoMult:0.##})");
		PanelContainer? cPesos = CartaoPorTitulo(pg, "Pesos");
		string pesoEsperado = vestidos ? "VESTIDOS" : mochila.Quantos(CatalogoDeItens.Pesos) > 0 ? "guardados" : "não tem";
		Checa($"a pilula dos pesos diz '{pesoEsperado}'", cPesos != null && PilulasDaAba(cPesos).Contains(pesoEsperado), cPesos == null ? "sem cartao" : string.Join(",", PilulasDaAba(cPesos)));

		// ---- os aparelhos: cada linha com o nome do catalogo, o icone e a pilula que a mochila manda ----
		var erros = new List<string>();
		foreach (string id in AparelhosEsperados)
		{
			ItemDef? def = CatalogoDeItens.Get(id);
			HBoxContainer? linha = LinhaDoAparelho(pg, id);
			if (def == null || linha == null) { erros.Add($"{id}: {(def == null ? "fora do catalogo" : "sem linha na aba")}"); continue; }
			bool nomeNaTela = Rotulos(linha).Any(l => l.Text == def.Nome);
			bool icone = Todos(linha).OfType<TextureRect>().Any(t => t.Name == "Icone" && t.Texture != null);
			string esperada = mochila.Quantos(id) > 0 ? "na mochila" : "—";
			List<string> pilulasDaLinha = PilulasDaAba(linha);
			if (!nomeNaTela || !icone || !pilulasDaLinha.Contains(esperada))
				erros.Add($"{id}: nome={nomeNaTela} icone={icone} pilulas=[{string.Join(",", pilulasDaLinha)}] esperada='{esperada}'");
		}
		Checa("as 5 linhas de aparelhos (Nav System, Dragon Radar, Roupa Espacial, Respirador, Brincos Potara) existem, com o nome do catalogo, o icone carregado e a pilula que a mochila manda",
			  erros.Count == 0, erros.Count == 0 ? "5 de 5" : string.Join(" | ", erros));
		HBoxContainer? nav = LinhaDoAparelho(pg, CatalogoDeItens.NavSystem);
		Checa("a pilula 'aba Nav' so aparece com o bit Poder.Nav aceso",
			  nav != null && PilulasDaAba(nav).Contains("aba Nav") == cli.Atributos.Tem(Protocol.Poder.Nav), $"nav={cli.Atributos.Tem(Protocol.Poder.Nav)}");

		// ---- combate: a ficha, e nada inventado ----
		Checa("'Cadência do soco' e a da ficha", menu.ValorDesenhado("Equip", "Cadência do soco") == $"{f.SocoMs} ms", menu.ValorDesenhado("Equip", "Cadência do soco") ?? "(nulo)");
		List<string> pilulas = PilulasDaAba(pg);
		Checa("o Golpe e uma PILULA (LETAL / não-letal) e diz o que a ficha diz", pilulas.Contains(f.Letal ? "LETAL" : "não-letal"), string.Join(",", pilulas));
		Checa("a Guarda e uma PILULA (ERGUIDA / baixa) e diz o que a ficha diz", pilulas.Contains(f.Guarda ? "ERGUIDA" : "baixa"));
		List<string> textos = Rotulos(pg).Select(l => l.Text).ToList();
		// SO OS ROTULOS DE LINHA (meta `linha`), e nao todo texto da aba: um numero inventado viraria uma
		// linha "Precisão ..... 12"; ja "precisa de um scouter" e a descricao de um botao, e a primeira
		// rodada reprovou por ela (o radical "Precis" pegou "precisa").
		List<string> rotulosDeLinha = Todos(pg).OfType<HBoxContainer>().Where(h => h.IsVisibleInTree() && h.HasMeta("linha")).Select(h => h.GetMeta("linha").AsString()).ToList();
		List<string> inventados = rotulosDeLinha.Where(t => NumerosQueOClienteNaoTem.Any(n => t.StartsWith(n, StringComparison.OrdinalIgnoreCase))).ToList();
		Checa("CONTRA-EXEMPLO: os numeros que o cliente NAO tem (dano, penetracao, precisao, deflexao do DM) NAO viram linha", inventados.Count == 0, string.Join(" | ", rotulosDeLinha));

		// ---- contra-exemplos de rotulo ----
		List<string> crus = textos.Where(t => NomesCrusDoDm.Any(n => t.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();
		Checa("CONTRA-EXEMPLO: nenhum rotulo da aba traz nome cru de campo do DM", crus.Count == 0, string.Join(" | ", crus));
		List<string> nave = textos.Where(t => PalavrasDeNaveDasAbas.Any(p => t.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();
		Checa("CONTRA-EXEMPLO: nenhum rotulo da aba com palavra de nave", nave.Count == 0, string.Join(" | ", nave));
		string letal = Teclas.NomeDaAcao("lethal"), guarda = Teclas.NomeDaAcao("guard");
		Checa("as teclas da nota de combate saem do registro (a tecla do letal e a da guarda, nao '-- sem tecla --')",
			  letal != "-- sem tecla --" && textos.Any(t => t.Contains(letal) && t.Contains(guarda)), $"letal={letal} guarda={guarda}");

		// ---- a foto e o pixel ----
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? foto = await Foto();
		await Guardar("equip-01", foto);
		bool naTela = cScouter != null && GetViewport().GetVisibleRect().Encloses(cScouter.GetGlobalRect());
		if (foto != null && !naTela)
		{
			Nota($"  PULADA  a chapa do cartao 'Scouter' no pixel   [o cartao nao esta inteiro na tela: {cScouter?.GetGlobalRect()}]");
			_pulados++;
			_naoMedidos.Add("chapa do cartao 'Scouter' no pixel");
		}
		else
		{
			(Color cor, float frac) = Moda(foto, cScouter == null ? new Rect2I() : Caixa(cScouter.GetGlobalRect(), 4));
			ChecaNoPixel("a chapa do cartao 'Scouter' na FOTO e a chapa clara do tema (a moda do retangulo esta na paleta)",
						 foto != null, NaPaleta(cor) && Perto(cor, Tema.PainelClaro),
						 $"moda {Hex(cor)} em {frac * 100:0}% do retangulo (paleta {Hex(Tema.PainelClaro)})");
		}

		// ---- injecao 1: a pilula do scouter trocada na arvore -- a prova sabe reprovar ----
		if (cScouter != null && Todos(cScouter).OfType<PanelContainer>().FirstOrDefault(p => p.HasMeta("pilula") && p.GetMeta("pilula").AsString() == esperado) is { } pil)
		{
			pil.SetMeta("pilula", "LIGADO");
			List<string> trocadas = PilulasDaAba(cScouter);
			Injeta("com a pilula do scouter trocada pra 'LIGADO' na arvore, as provas 'diz o que o bit diz' e 'LIGADO nao aparece' REPROVAM",
				   !trocadas.Contains(esperado) && (ligado || trocadas.Contains("LIGADO")), string.Join(",", trocadas));
			pil.SetMeta("pilula", esperado);
		}
		// ---- injecao 2: uma linha de aparelho escondida -- a prova das cinco reprova ----
		if (nav != null)
		{
			nav.Visible = false;
			await Quadros(1);
			Injeta("com a linha do Nav System escondida na arvore, a prova das 5 linhas REPROVA",
				   LinhaDoAparelho(pg, CatalogoDeItens.NavSystem) == null);
			nav.Visible = true;
			await Quadros(1);
		}

		// ---- determinismo e quietude ----
		string antes = ImpressaoDaPagina(pg);
		menu.ForcarRedesenho();
		await Quadros(2);
		string depois = ImpressaoDaPagina(menu.PaginaDeTeste("Equip")!);
		Checa("DETERMINISMO: a mesma mochila e a mesma ficha remontam a MESMA arvore, na mesma ordem", antes == depois, $"{antes.Length} vs {depois.Length} chars");
		int r0 = menu.RemontagensDeTeste;
		await Segundos(1.5);
		Checa("QUIETUDE: em 1,5 s parado a pagina de Equip nao e remontada a cada pacote de ficha (no maximo 1 remontagem)",
			  menu.RemontagensDeTeste - r0 <= 1, $"remontagens {r0} -> {menu.RemontagensDeTeste}");
	}

	/// <summary>A linha de um aparelho na aba, pelo metadado `aparelho` (o id do item). Nula se nao esta visivel.</summary>
	private static HBoxContainer? LinhaDoAparelho(Node pg, string id) =>
		Todos(pg).OfType<HBoxContainer>().FirstOrDefault(h => h.IsVisibleInTree() && h.HasMeta("aparelho") && h.GetMeta("aparelho").AsString() == id);
}
