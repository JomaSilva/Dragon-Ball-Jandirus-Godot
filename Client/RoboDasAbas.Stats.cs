using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Stats da `--diagabas` (F2). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA AFIRMA, e so pelo que a tela ESCREVEU (nunca pelo que a ficha diz sozinha):
///   * a faixa "Battle Power" existe e, sem scouter, diz "???" sem NENHUM digito (o sigilo);
///   * "Ki", "Nutrição" e "Poder efetivo" continuam na FORMA que a `--diagbancada` desmonta;
///   * quatro cartoes, 4 barras de vitais + 8 de atributos (com o trilho do Ki), as pilulas de
///     condicao e golpe, e o Estado lendo o cliente (marcos, lugar, cadencia, zeni);
///   * alto/medio/baixo de cada atributo bate com a ficha pela MESMA regra -- e a regra invertida
///     e reprovada (injecao 1); uma barra escondida derruba a contagem (injecao 2);
///   * nenhum rotulo com nome cru do DM nem palavra de nave, e a classe nao aparece (contra-exemplos);
///   * a chapa do cartao na FOTO e a do tema; a pagina e deterministica; e nao remonta em quietude.
///
/// As pecas de leitura no fim do arquivo (abrir aba pelo botao, barras, pilulas, cartoes, impressao
/// da pagina) servem a F2 e a F3 e DEVERIAM SUBIR pra `RoboDasAbas.cs` quando outra familia as usar.
/// </summary>
public partial class RoboDasAbas
{
	/// <summary>Nomes crus de campo do DM que NAO podem aparecer em rotulo nenhum destas abas.</summary>
	private static readonly string[] NomesCrusDoDm =
	[
		"physoff", "physdef", "kioff", "kidef", "kiskill", "technique", "magiskill", "staminagain",
		"willpower", "expressedbp", "hitspeed", "penetration", "deflection", "scouteron",
	];

	/// <summary>
	/// As palavras de nave da `--diagembarque` (`RoboDeEmbarque.PalavrasDeNave`), repetidas aqui
	/// porque la elas sao privadas. A varredura daquela bancada continua valendo; esta so falha antes.
	/// </summary>
	private static readonly string[] PalavrasDeNaveDasAbas =
		["nave", "leme", "desembarc", "recondicion", "lançar", "lancar", "pilotar", "embarc"];

	/// <summary>"120 / 120   (100%)" -- a forma que `RoboDeBancada.RazaoEntreParenteses` desmonta.</summary>
	private static readonly Regex FormaDeTanque = new(@"^\d[\d.,]* / \d[\d.,]*   \(\d+%\)$", RegexOptions.Compiled);

	/// <summary>"98%   (...)" -- a forma que `RoboDeBancada.Razao` desmonta (o primeiro numero).</summary>
	private static readonly Regex FormaDePorcentagem = new(@"^\d+(,\d+)?%", RegexOptions.Compiled);

	private static readonly string[] BarrasDeVitais = ["Vida", "Ki", "Vigor", "Nutrição"];
	private static readonly string[] BarrasDeAtributos =
		["Ofensiva Física", "Defesa Física", "Ofensiva de Ki", "Defesa de Ki", "Técnica", "Perícia de Ki", "Velocidade", "Esotérico"];

	private async System.Threading.Tasks.Task F2_Stats(MenuJogo menu, GameClient cli)
	{
		Nota("--- F2: Stats: a faixa do BP, quatro cartoes, barras, pilulas -- e a regra que sabe reprovar ---");
		Control? pg = await AbrirAbaPeloBotao(menu, "Stats");
		Checa("a pagina de Stats existe e esta visivel depois do clique na aba", pg is { Visible: true }, menu.AbaDeTeste);
		if (pg == null) return;

		// O REDESENHO FORCADO poe a pagina e a ficha no MESMO quadro (ver `MenuJogo.ForcarRedesenho`):
		// so assim da pra exigir igualdade exata entre o que esta escrito e o que a ficha diz.
		menu.ForcarRedesenho();
		await Quadros(2);
		SheetState f = cli.Sheet;

		// ---- a faixa e o sigilo ----
		bool temFaixa = Todos(pg).OfType<PanelContainer>().Any(p => p.IsVisibleInTree() && p.HasMeta("faixa") && p.GetMeta("faixa").AsString() == "Battle Power");
		Checa("a aba abre com a FAIXA 'Battle Power' (o numero grande da aba, meta 'faixa')", temFaixa);
		string bp = menu.ValorDesenhado("Stats", "Battle Power") ?? "(nulo)";
		bool semScouter = !cli.Atributos.Tem(Protocol.Poder.Scouter);
		Checa("sem scouter, a faixa escreve '???' e NENHUM digito de BP (o sigilo do poder de luta)",
			  semScouter && bp.Contains("???") && !bp.Any(char.IsDigit), bp);
		Checa("...e o texto lido pela porta e exatamente '???   (sem scouter)', o que a --diagbancada desmonta",
			  bp == "???   (sem scouter)", bp);

		// ---- as formas dos valores que a --diagbancada le ----
		string ki = menu.ValorDesenhado("Stats", "Ki") ?? "(nulo)";
		Checa("'Ki' tem a forma 'N / N   (NN%)'", FormaDeTanque.IsMatch(ki), ki);
		Checa("...e o (NN%) do Ki e o da ficha deste mesmo quadro", ki.EndsWith($"({f.RazaoDeKi * 100:0}%)"), $"tela '{ki}', ficha {f.RazaoDeKi * 100:0}%");
		string nut = menu.ValorDesenhado("Stats", "Nutrição") ?? "(nulo)";
		Checa("'Nutrição' tem a forma 'N / N   (NN%)'", FormaDeTanque.IsMatch(nut), nut);
		Checa("...e o (NN%) da Nutrição e o da ficha", nut.EndsWith($"({f.RazaoDeNutricao * 100:0}%)"), $"ficha {f.RazaoDeNutricao * 100:0}%");
		string efetivo = menu.ValorDesenhado("Stats", "Poder efetivo") ?? "(nulo)";
		Checa("'Poder efetivo' comeca com numero e '%'", FormaDePorcentagem.IsMatch(efetivo), efetivo);
		string vida = menu.ValorDesenhado("Stats", "Vida") ?? "(nulo)";
		Checa("'Vida' e a % da ficha", vida == $"{f.HP:0}%", $"tela '{vida}', ficha {f.HP:0}%");
		string vigor = menu.ValorDesenhado("Stats", "Vigor") ?? "(nulo)";
		Checa("'Vigor' e a razao VIVA da ficha rapida (a mesma da barra do HUD)", vigor == $"{f.RazaoDeVigor * 100:0}%", $"tela '{vigor}', ficha {f.RazaoDeVigor * 100:0}%");

		// ---- as barras ----
		List<ProgressBar> barras = BarrasDaAba(pg);
		Checa("as 4 barras de vitais existem (ProgressBar com meta 'barra': Vida, Ki, Vigor, Nutrição)",
			  BarrasDeVitais.All(v => barras.Any(b => b.GetMeta("barra").AsString() == v)),
			  string.Join(",", barras.Select(b => b.GetMeta("barra").AsString())));
		Checa("as 8 barras de atributos existem", BarrasDeAtributos.All(a => barras.Any(b => b.GetMeta("barra").AsString() == a)), $"{barras.Count} barras ao todo");
		ProgressBar? bKi = barras.FirstOrDefault(b => b.GetMeta("barra").AsString() == "Ki");
		Checa("a barra de Ki tem o TRILHO do teto de carga (MaxValue = TrilhoDeKi da ficha), como a do HUD",
			  bKi != null && Math.Abs(bKi.MaxValue - f.TrilhoDeKi) < 0.01, $"max {bKi?.MaxValue:0.##} / ficha {f.TrilhoDeKi:0.##}");
		ProgressBar? bVida = barras.FirstOrDefault(b => b.GetMeta("barra").AsString() == "Vida");
		Checa("a barra de Vida esta cheia na razao da ficha", bVida != null && Math.Abs(bVida.Value - f.HP / 100) < 0.02, $"{bVida?.Value:0.##} / ficha {f.HP / 100:0.##}");

		// ---- as pilulas ----
		List<string> pilulas = PilulasDaAba(pg);
		string condicao = f.Morto ? "MORTO" : f.KO ? "NOCAUTEADO" : "de pé";
		Checa("a Condição e uma PILULA (de pé / NOCAUTEADO / MORTO) e diz o que a ficha diz", pilulas.Contains(condicao), string.Join(",", pilulas));
		Checa("o Golpe e uma PILULA (LETAL / não-letal) e diz o que a ficha diz", pilulas.Contains(f.Letal ? "LETAL" : "não-letal"));

		// ---- os cartoes ----
		List<string> cartoes = TitulosDosCartoes(pg);
		Checa("quatro cartoes de secao (meta 'cartao'='secao'): Vitais, Atributos, Treino, Estado",
			  cartoes.Count == 4 && new[] { "Vitais", "Atributos", "Treino", "Estado" }.All(cartoes.Contains), string.Join(",", cartoes));

		// ---- o Estado le o cliente, e nao um numero guardado ----
		string marcos = menu.ValorDesenhado("Stats", "Marcos") ?? "(nulo)";
		Checa("'Marcos' escreve o saldo do cliente", marcos.StartsWith($"{cli.MarcosLivres} livres") && marcos.Contains($"{cli.MarcosTotais} na vida"), $"tela '{marcos}', cliente {cli.MarcosLivres}/{cli.MarcosTotais}");
		Checa("'Lugar' e o NOME da zona do cliente (nao coordenada)", menu.ValorDesenhado("Stats", "Lugar") == cli.Zone.Name, $"tela '{menu.ValorDesenhado("Stats", "Lugar")}', cliente '{cli.Zone.Name}'");
		Checa("'Cadência do soco' e a da ficha", menu.ValorDesenhado("Stats", "Cadência do soco") == $"{f.SocoMs} ms", menu.ValorDesenhado("Stats", "Cadência do soco") ?? "(nulo)");
		Checa("'Zeni' e o do cliente", menu.ValorDesenhado("Stats", "Zeni") == $"{cli.Zeni:N0}", menu.ValorDesenhado("Stats", "Zeni") ?? "(nulo)");

		// ---- alto / medio / baixo: a MESMA regra do ui_qual(), copiada aqui e invertida na injecao ----
		Protocol.AtributosState a = cli.Atributos;
		(string Nome, float Valor)[] atts =
		[
			("Ofensiva Física", a.PhysOff), ("Defesa Física", a.PhysDef), ("Ofensiva de Ki", a.KiOff), ("Defesa de Ki", a.KiDef),
			("Técnica", a.Technique), ("Perícia de Ki", a.KiSkill), ("Velocidade", a.Speed), ("Esotérico", a.Esoteric),
		];
		float media = atts.Sum(x => x.Valor) / Math.Max(atts.Length, 1);
		int errados = QualidadesErradas(menu, atts, media, invertida: false, out string detalhe);
		Checa("alto/médio/baixo de cada atributo bate com a ficha (20% acima da média = alto, 20% abaixo = baixo), e o numero e x10", errados == 0, detalhe);
		int foraDoMeio = atts.Count(x => x.Valor >= media * 1.2f || x.Valor <= media * 0.8f);
		if (foraDoMeio == 0)
		{
			Nota("  PULADA  (injecao) regra de alto/baixo invertida: os 8 atributos desta ficha sao 'médio', inverter nao muda nada");
			_pulados++;
			_naoMedidos.Add("(injecao) alto/baixo invertidos (ficha sem atributo fora do meio)");
		}
		else
		{
			int pegos = QualidadesErradas(menu, atts, media, invertida: true, out string d2);
			Injeta($"com a conta de alto/baixo INVERTIDA na bancada, a prova acima REPROVA ({foraDoMeio} atributo(s) fora do meio)", pegos > 0, d2);
		}

		// ---- contra-exemplos de rotulo ----
		List<string> textos = Rotulos(pg).Select(l => l.Text).ToList();
		List<string> crus = textos.Where(t => NomesCrusDoDm.Any(n => t.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();
		Checa("CONTRA-EXEMPLO: nenhum rotulo da aba traz nome cru de campo do DM (physoff, kiskill, staminagain...)", crus.Count == 0, string.Join(" | ", crus));
		List<string> nave = textos.Where(t => PalavrasDeNaveDasAbas.Any(p => t.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();
		Checa("CONTRA-EXEMPLO: nenhum rotulo da aba com palavra de nave (a varredura da --diagembarque)", nave.Count == 0, string.Join(" | ", nave));
		string classe = cli.Sheet.Class ?? "";
		Checa("CONTRA-EXEMPLO: a aba nao imprime a CLASSE (sorteio cego)",
			  !textos.Any(t => t.Contains("Legendary") || t.Contains("Prodig") || (classe.Length > 2 && t.Contains(classe))), classe);

		// ---- a foto e o pixel ----
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? foto = await Foto();
		await Guardar("stats-01-topo", foto);
		PanelContainer? vitais = CartaoPorTitulo(pg, "Vitais");
		bool naTela = vitais != null && GetViewport().GetVisibleRect().Encloses(vitais.GetGlobalRect());
		if (foto != null && !naTela)
		{
			Nota($"  PULADA  a chapa do cartao 'Vitais' no pixel   [o cartao nao esta inteiro na tela: {vitais?.GetGlobalRect()}]");
			_pulados++;
			_naoMedidos.Add("chapa do cartao 'Vitais' no pixel");
		}
		else
		{
			(Color cor, float frac) = Moda(foto, vitais == null ? new Rect2I() : Caixa(vitais.GetGlobalRect(), 4));
			ChecaNoPixel("a chapa do cartao 'Vitais' na FOTO e a chapa clara do tema (a moda do retangulo esta na paleta)",
						 foto != null, NaPaleta(cor) && Perto(cor, Tema.PainelClaro),
						 $"moda {Hex(cor)} em {frac * 100:0}% do retangulo (paleta {Hex(Tema.PainelClaro)})");
		}
		if (Ancestral<ScrollContainer>(pg) is { } rol2)
		{
			rol2.ScrollVertical = (int)rol2.GetVScrollBar().MaxValue;
			await Quadros(2);
			await Guardar("stats-02-rodape", await Foto());
			rol2.ScrollVertical = 0;
			await Quadros(1);
		}

		// ---- injecao 2: uma barra escondida derruba a contagem ----
		if (bVida != null)
		{
			bVida.Visible = false;
			await Quadros(1);
			List<ProgressBar> depois = BarrasDaAba(pg);
			Injeta("com a barra de Vida escondida na arvore, a prova 'as 4 barras de vitais existem' REPROVA",
				   depois.Count == barras.Count - 1 && !BarrasDeVitais.All(v => depois.Any(b => b.GetMeta("barra").AsString() == v)),
				   $"{barras.Count} -> {depois.Count} barras");
			bVida.Visible = true;
			await Quadros(1);
		}

		// ---- determinismo: a mesma ficha remonta a MESMA arvore ----
		string antes = ImpressaoDaPagina(pg);
		menu.ForcarRedesenho();
		await Quadros(2);
		string depoisDoRedesenho = ImpressaoDaPagina(menu.PaginaDeTeste("Stats")!);
		Checa("DETERMINISMO: a mesma ficha remonta a MESMA arvore de nodes, na mesma ordem, com os mesmos textos",
			  antes == depoisDoRedesenho, $"{antes.Length} vs {depoisDoRedesenho.Length} chars");

		// ---- quietude: a pagina nao remonta 5x/s ----
		int r0 = menu.RemontagensDeTeste;
		await Segundos(1.5);
		Checa("QUIETUDE: em 1,5 s parado a pagina de Stats nao e remontada a cada pacote de ficha (no maximo 1 remontagem)",
			  menu.RemontagensDeTeste - r0 <= 1, $"remontagens {r0} -> {menu.RemontagensDeTeste}");
	}

	/// <summary>
	/// QUANTOS ATRIBUTOS ESTAO ESCRITOS ERRADO na aba, pela regra do `ui_qual()` (copiada aqui de
	/// proposito: a bancada nao chama a do menu, senao provaria que a funcao e igual a ela mesma).
	/// `invertida` troca alto por baixo: e a injecao -- a mesma leitura tem que ficar vermelha.
	/// </summary>
	private static int QualidadesErradas(MenuJogo menu, (string Nome, float Valor)[] atts, float media, bool invertida, out string detalhe)
	{
		var erros = new List<string>();
		foreach ((string nome, float v) in atts)
		{
			string esperado = v >= media * 1.2f ? "alto" : v <= media * 0.8f ? "baixo" : "médio";
			if (invertida) esperado = esperado == "alto" ? "baixo" : esperado == "baixo" ? "alto" : esperado;
			string escrito = menu.ValorDesenhado("Stats", nome) ?? "(nulo)";
			if (!escrito.StartsWith($"{v * 10:0}   (") || !escrito.EndsWith($"({esperado})"))
				erros.Add($"{nome}: '{escrito}' (esperado {v * 10:0} {esperado})");
		}
		detalhe = erros.Count == 0 ? $"8 de 8 batem, média {media * 10:0}" : string.Join(" | ", erros);
		return erros.Count;
	}

	// =====================================================================
	// AS PECAS DE LEITURA DAS FAMILIAS F2 E F3 -- deveriam subir pra RoboDasAbas.cs
	// =====================================================================
	/// <summary>Abre uma aba PELO BOTAO dela (o caminho do dedo) e devolve a pagina montada.</summary>
	private async System.Threading.Tasks.Task<Control?> AbrirAbaPeloBotao(MenuJogo menu, string aba)
	{
		if (Botao(menu, aba) is { } b) await Clicar(b);
		await Quadros(4);
		return menu.PaginaDeTeste(aba);
	}

	private static List<ProgressBar> BarrasDaAba(Node pg) =>
		Todos(pg).OfType<ProgressBar>().Where(b => b.IsVisibleInTree() && b.HasMeta("barra")).ToList();

	private static List<string> PilulasDaAba(Node pg) =>
		Todos(pg).OfType<PanelContainer>().Where(p => p.IsVisibleInTree() && p.HasMeta("pilula")).Select(p => p.GetMeta("pilula").AsString()).ToList();

	private static List<PanelContainer> CartoesDaAba(Node pg) =>
		Todos(pg).OfType<PanelContainer>().Where(p => p.IsVisibleInTree() && p.HasMeta("cartao") && p.GetMeta("cartao").AsString() == "secao").ToList();

	private static List<string> TitulosDosCartoes(Node pg) => CartoesDaAba(pg).Select(p => p.GetMeta("titulo").AsString()).ToList();

	private static PanelContainer? CartaoPorTitulo(Node pg, string titulo) =>
		CartoesDaAba(pg).FirstOrDefault(p => p.GetMeta("titulo").AsString() == titulo);

	/// <summary>
	/// A ARVORE DA PAGINA COMO TEXTO: tipo de cada node, o texto dos rotulos, o valor das barras e os
	/// metadados. Duas montagens da mesma ficha tem que dar a mesma impressao -- e o determinismo.
	/// </summary>
	private static string ImpressaoDaPagina(Node pg)
	{
		var sb = new StringBuilder();
		string[] metas = ["linha", "pilula", "cartao", "titulo", "faixa", "estado", "aparelho", "barra"];
		foreach (Node n in Todos(pg))
		{
			sb.Append(n.GetType().Name);
			if (n is Label l) sb.Append('=').Append(l.Text);
			if (n is Button b) sb.Append('=').Append(b.Text).Append(b.Disabled ? "(apagado)" : "");
			if (n is ProgressBar p) sb.Append('=').Append(p.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
			foreach (string m in metas)
				if (n.HasMeta(m)) sb.Append(';').Append(m).Append(':').Append(n.GetMeta(m).AsString());
			sb.Append('\n');
		}
		return sb.ToString();
	}
}
