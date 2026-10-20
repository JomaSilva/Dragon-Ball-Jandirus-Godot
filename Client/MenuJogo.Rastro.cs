using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) A BUSCA E O RASTRO NA ABA SENSE/SCAN -- a regra em `RastroDoSense`, o HUD em `RastroNaTela`. A aba (`MenuJogo.Sentidos.cs`)
/// so chama estas pecas: a caixa "procurar pessoa pelo nome...", o cartao "Rastro" e o nome clicavel de cada presenca.
///
/// MORAM NUM ARQUIVO PROPRIO de proposito: o robo `--diagsenserastro` le a dica da caixa e o defeito injetado da busca global, e um arquivo
/// novo que le linha nova de arquivo velho nao compila sozinho se o velho voltar atras.
/// </summary>
public partial class MenuJogo
{
	/// <summary>O que foi digitado na busca da aba. Mora aqui, e nao no campo: a pagina e refeita a cada pacote do Sense.</summary>
	private string _buscaDoSense = "";

	/// <summary>A dica da caixa -- e por ela que o `CampoQueAtravessa` devolve o foco ao campo remontado, e que o robo o acha.</summary>
	internal const string DicaDaBuscaDoSense = "procurar pessoa pelo nome...";

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsenserastro`): a busca GLOBAL do menu ("procurar acao...") fica fora da assinatura desta aba, como
	/// ficou ate 2026-10-19 -- digitar nela com a aba Sense aberta nao trocava a pagina pelos achados ate o proximo pacote do Sense
	/// chegar (e parado, ele nao chega). Falso em jogo.
	/// </summary>
	internal static bool BuscaGlobalForaDaAssinaturaDeTeste;

	private void CaixaDeBuscaDoSense()
	{
		var campo = new LineEdit
		{
			PlaceholderText = DicaDaBuscaDoSense,
			Text = _buscaDoSense,
			ClearButtonEnabled = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		// CADA LETRA REFILTRA: a busca entra na assinatura da aba, e o `CampoQueAtravessa` devolve o foco e o cursor ao campo novo.
		campo.TextChanged += t => { _buscaDoSense = t; Redesenhar(); };
		_conteudo.AddChild(campo);
	}

	/// <summary>O cartao "Rastro": a mesma frase do selo do HUD e o botao que para.</summary>
	private void CartaoDoRastro(List<Protocol.PresencaState> lista)
	{
		VBoxContainer c = Cartao("Rastro", destaque: true);
		Protocol.PresencaState? p = RastroDoSense.Leitura(lista);
		var frase = new Label { Text = RastroNaTela.Frase(p, RastroDoSense.Rotulo), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		frase.AddThemeColorOverride("font_color", p == null ? Tema.TextoFraco : Tema.Texto);
		c.AddChild(frase);
		var parar = new Button { Text = "Parar o rastro", FocusMode = Control.FocusModeEnum.None };
		parar.Pressed += RastroDoSense.Parar;
		c.AddChild(BotaoComDescricao(parar, "a seta e o selo do alto da tela somem"));
	}

	/// <summary>
	/// O NOME QUE SEGUE: o titulo do cartao recebe o clique (segue; de novo, para). Continua sendo um `Label` -- ver o cabecalho da
	/// classe. A troca vai pro fim do quadro: ela remonta a pagina, e este rotulo e destruido dentro do proprio evento.
	/// </summary>
	private static void NomeQueSegue(Label titulo, Protocol.PresencaState p, int linha)
	{
		bool seguido = RastroDoSense.EhOAlvo(p);
		titulo.MouseFilter = Control.MouseFilterEnum.Stop;
		titulo.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		titulo.TooltipText = seguido ? "clique pra parar de seguir" : "clique pra seguir pelo Sense: uma seta aponta pra essa presença";
		titulo.SetMeta("rastro", seguido ? "seguido" : "nome");
		titulo.GuiInput += e =>
		{
			if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
			titulo.AcceptEvent();
			Callable.From(() => { if (seguido) RastroDoSense.Parar(); else RastroDoSense.Seguir(p, linha); }).CallDeferred();
		};
	}

	/// <summary>
	/// O PEDACO DA BUSCA E DO RASTRO NA ASSINATURA DA ABA (`ExtraDaAssinaturaDeSentidos`): as duas buscas e quem esta sendo seguido
	/// trocam o que a pagina desenha sem que a lista do Sense mude -- e a lista era a unica coisa que la estava. A busca GLOBAL faltava
	/// desde sempre: ela troca a pagina inteira pelos achados (`Montar`), e esta aba, sem assinatura basica, nao a via.
	/// </summary>
	private string AssinaturaDaBuscaEDoRastro() =>
		$"{_buscaDoSense.Trim()}|{(BuscaGlobalForaDaAssinaturaDeTeste ? "" : _busca.Text.Trim())}|{RastroDoSense.Nome}/{RastroDoSense.Assinatura}";
}
