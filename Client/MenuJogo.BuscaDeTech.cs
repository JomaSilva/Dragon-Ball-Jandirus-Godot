using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-21) A BUSCA DO CATALOGO NA ABA TECH -- pedido de jogador repassado pelo dono: *"Search no menu de tech"*. A aba
/// (`MenuJogo.Tech.cs`) so chama estas pecas: o titulo do catalogo com a caixa "procurar construção..." logo abaixo, o registro de cada card
/// e o filtro.
///
/// ============================ O QUE ELA PROCURA ============================
/// O NOME da oferta e a DESCRICAO do catalogo (a frase que o card so mostrava no tooltip), sem acento e sem caixa -- a mesma conta da busca
/// do Sense (`RastroDoSense.Normalizar`): "rapido" acha a pa pela descricao ("mais rápido"), "GASTER" acha o Gaster Blaster. So o CATALOGO
/// e filtrado: o cartao "Suas construções nesta zona" fica como esta. O "(N)" do titulo passa a contar os achados, e o cartao "Busca" (o
/// mesmo do Sense) diz quando nada casa.
///
/// ============================ A LETRA NAO REMONTA A PAGINA ============================
/// A Tech e a pagina mais pesada do menu P (~90 cards, ~1.600 nos -- ver o `comum` em `MenuJogo.cs`). A busca do Sense remonta a pagina a
/// cada letra, e la sao meia duzia de cartoes; aqui cada letra destruiria e montaria os noventa de novo. Entao a letra so ESCONDE e MOSTRA os
/// cards que ja estao montados (`Visible`, pelo texto guardado de cada um) e reescreve o titulo e a nota no lugar -- a grade nao reserva
/// lugar pra card escondido. A remontagem que vem por outro motivo (o catalogo ou a carteira chegando) nasce ja filtrada, e o
/// `CampoQueAtravessa` devolve o foco ao campo novo pela dica. O robo `--diagbuscatech` mede os dois caminhos e imprime o custo de cada.
/// </summary>
public partial class MenuJogo
{
	/// <summary>O que foi digitado na busca do catalogo. Mora aqui, e nao no campo: a pagina e refeita quando o catalogo ou a carteira chegam.</summary>
	private string _buscaDaTech = "";

	/// <summary>A dica da caixa -- unica no menu: e por ela que o `CampoQueAtravessa` devolve o foco ao campo remontado, e que o robo o acha.</summary>
	internal const string DicaDaBuscaDaTech = "procurar construção...";

	/// <summary>Os cards da pagina montada agora, com o texto em que a busca procura (o nome e a descricao): normalizado, e cru.</summary>
	private readonly List<(Control Card, string Normalizado, string Cru)> _cardsDaBusca = [];

	/// <summary>O titulo da secao do catalogo, e o cartao "Busca" com a nota de "nada casa", da pagina montada agora: reescritos no lugar.</summary>
	private Label? _tituloDoCatalogo, _notaDaBuscaDaTech;
	private Control? _cartaoDaBuscaDaTech;

	/// <summary>DEFEITO INJETADO (robo `--diagbuscatech`): o filtro ignora o que foi digitado e todo card continua na aba. Falso em jogo.</summary>
	internal static bool BuscaDaTechCegaDeTeste;

	/// <summary>DEFEITO INJETADO (robo `--diagbuscatech`): a busca so procura no nome, e a descricao fica de fora. Falso em jogo.</summary>
	internal static bool BuscaDaTechSoPeloNomeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagbuscatech`): a busca compara o texto cru -- "gaster" nao acha o "Gaster", "rapido" nao acha o "rápido".
	/// Falso em jogo.
	/// </summary>
	internal static bool BuscaDaTechCruaDeTeste;

	/// <summary>DEFEITO INJETADO (robo `--diagbuscatech`): o "(N)" do titulo continua contando o catalogo inteiro com a busca no meio. Falso em jogo.</summary>
	internal static bool BuscaDaTechContaTudoDeTeste;

	/// <summary>DEFEITO INJETADO (robo `--diagbuscatech`): quando nada casa, a pagina fica sem o cartao que diz isso. Falso em jogo.</summary>
	internal static bool BuscaDaTechSemNotaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagbuscatech`): cada letra REMONTA a pagina inteira, como a busca do Sense -- e o caminho que a medida do robo
	/// compara com o de esconder e mostrar. Falso em jogo.
	/// </summary>
	internal static bool BuscaDaTechRemontaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagbuscatech`): a pagina remontada por outro motivo nasce sem o filtro, com a busca escrita na caixa. Falso
	/// em jogo.
	/// </summary>
	internal static bool BuscaDaTechEsquecidaNaRemontagemDeTeste;

	/// <summary>O titulo da secao do catalogo, com a contagem (o `Secao` o escreve em caixa alta, pelo `Tema.Rotulo`).</summary>
	private static string TituloDoCatalogo(int n) => $"Catálogo de construções  ({n})";

	/// <summary>
	/// O TITULO DO CATALOGO, A CAIXA DE BUSCA LOGO ABAIXO DELE E O CARTAO "Busca" (escondido ate nada casar). Comeca a lista de cards da pagina
	/// que esta sendo montada: a da montagem anterior aponta pra nos que vao embora.
	/// </summary>
	private void CabecalhoDaBuscaDaTech(int total)
	{
		_cardsDaBusca.Clear();
		Secao(TituloDoCatalogo(total));
		// O ROTULO QUE O `Secao` ACABOU DE PENDURAR -- o ultimo `Label` da pagina (depois dele so vem o separador). O `Secao` mora no arquivo de
		// todas as abas e nao o devolve, e e ele que a busca reescreve a cada letra.
		_tituloDoCatalogo = _conteudo.GetChildren().OfType<Label>().Last();

		var campo = new LineEdit
		{
			PlaceholderText = DicaDaBuscaDaTech,
			Text = _buscaDaTech,
			ClearButtonEnabled = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			TooltipText = "procura no nome e na descrição de cada construção, sem acento e sem maiúscula",
		};
		campo.TextChanged += t =>
		{
			_buscaDaTech = t;
			if (BuscaDaTechRemontaDeTeste) ForcarRedesenho();
			else FiltrarOCatalogo();
		};
		_conteudo.AddChild(campo);

		VBoxContainer busca = Cartao("Busca");
		_notaDaBuscaDaTech = Nota("", busca);
		_cartaoDaBuscaDaTech = busca.GetParent<Control>();
		_cartaoDaBuscaDaTech.Visible = false;
	}

	/// <summary>O CARD ENTRA NA BUSCA com o texto em que ela procura: o nome da oferta e a descricao do catalogo.</summary>
	private void NaBuscaDaTech(Control card, string nome, string descricao)
	{
		string cru = BuscaDaTechSoPeloNomeDeTeste ? nome : nome + "\n" + descricao;
		_cardsDaBusca.Add((card, RastroDoSense.Normalizar(cru), cru));
	}

	/// <summary>O CARD FICA NA ABA? Termo vazio deixa todos; senao, o termo dentro do nome ou da descricao -- sem acento e sem caixa.</summary>
	private static bool CasaNaBuscaDaTech(string termo, string termoNormalizado, string normalizado, string cru)
	{
		if (termo.Length == 0 || BuscaDaTechCegaDeTeste) return true;
		return BuscaDaTechCruaDeTeste ? cru.Contains(termo, StringComparison.Ordinal) : normalizado.Contains(termoNormalizado, StringComparison.Ordinal);
	}

	/// <summary>
	/// O FILTRO, SEM REMONTAR: esconde o card que nao casa, mostra o que casa, e reescreve o "(N)" e a nota. Roda a cada letra e no fim de
	/// cada montagem da pagina (a busca sobrevive a remontagem).
	/// </summary>
	private void FiltrarOCatalogo()
	{
		string termo = _buscaDaTech.Trim();
		string termoNormalizado = RastroDoSense.Normalizar(termo);
		int achados = 0;
		foreach ((Control card, string normalizado, string cru) in _cardsDaBusca)
		{
			if (!IsInstanceValid(card)) continue;
			bool casa = CasaNaBuscaDaTech(termo, termoNormalizado, normalizado, cru);
			card.Visible = casa;
			if (casa) achados++;
		}
		if (_tituloDoCatalogo is { } titulo && IsInstanceValid(titulo))
			titulo.Text = TituloDoCatalogo(BuscaDaTechContaTudoDeTeste ? _cardsDaBusca.Count : achados).ToUpperInvariant();
		if (_notaDaBuscaDaTech is { } nota && IsInstanceValid(nota)) nota.Text = $"Nenhuma construção com \"{termo}\" no nome ou na descrição.";
		if (_cartaoDaBuscaDaTech is { } cartao && IsInstanceValid(cartao))
			cartao.Visible = achados == 0 && termo.Length > 0 && !BuscaDaTechSemNotaDeTeste;
	}
}
