using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// A TECLA E: chegue perto de alguma coisa e veja o que da pra fazer com ela.
///
/// ============================ O GESTO QUE NAO EXISTIA ============================
/// Ate aqui, cada maquina do mundo era usada por um caminho proprio e invisivel: o banco tinha
/// tres verbos escondidos na busca do menu, a bancada de pesquisa tinha um verbo "estudar" que so
/// funcionava se voce ja soubesse que existia, e a arvore era pintura. Nada na tela dizia que
/// aquilo respondia a alguma coisa.
///
/// O pedido do dono foi uma porta so: "chegar perto do objeto e apertar E, assim vai abrir uma
/// tela q seria um menu te interaçoes possiveis daquele objeto, isso serve pra tudo".
/// =================================================================================
///
/// ============================ O QUE ELE NAO FAZ ============================
/// NAO DECIDE NADA. As acoes vem do <see cref="Interacoes"/>, que o servidor le tambem, e cada
/// botao manda o MESMO verbo que ja existia. Este arquivo e uma porta, nao um sistema: se ele
/// sumisse, os comandos continuariam funcionando pela busca do menu.
///
/// E NAO ALCANCA PORTA. Elas abrem por encostar, e trocar isso por um menu de duas opcoes seria
/// piorar um gesto que ja esta bom.
/// ===========================================================================
/// </summary>
public partial class MenuDeInteracao : CanvasLayer
{
	/// <summary>
	/// A QUE DISTANCIA A COISA RESPONDE, em pixels -- e o numero E DO CORE.
	///
	/// Havia uma copia de 64 aqui e outra no servidor; elas concordavam por acaso, e uma TERCEIRA (a
	/// do console da ponte) divergia em 16 px -- um menu que abria e um verbo que falhava, que e
	/// exatamente o que este comentario dizia que nao podia acontecer. Hoje ha um numero so:
	/// <see cref="Interacoes.Alcance"/>, e a historia inteira esta escrita la.
	/// </summary>
	private const float Alcance = Interacoes.Alcance;

	private Control _raiz = null!;
	private VBoxContainer _lista = null!;
	private Label _titulo = null!;
	private Label _dica = null!;

	// O `_alvo` (o ID da obra aberta) FOI DELETADO: ele so servia pra reachar o nome do alvo na lista
	// de construcoes, e o alvo passou a poder ser um VEICULO, que nao esta nessa lista. Quem responde
	// pelo titulo agora e `_tipoDoAlvo`, que vale pros dois casos -- e o id nao tinha segundo uso.

	/// <summary>O menu vivo, ou nulo antes do mundo. SO PRA BANCADA (`--diagembarque`).</summary>
	public static MenuDeInteracao? Instancia { get; private set; }

	public override void _Ready()
	{
		Layer = 4;   // acima do menu do jogo (3), abaixo do quick time event (5)
		Instancia = this;
		Montar();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(14);
		centro.AddChild(painel);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
		caixa.AddThemeConstantOverride("separation", 6);
		painel.AddChild(caixa);

		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 20);
		caixa.AddChild(_titulo);
		caixa.AddChild(new HSeparator());

		_lista = new VBoxContainer();
		_lista.AddThemeConstantOverride("separation", 4);
		caixa.AddChild(_lista);

		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);

		// A DICA FLUTUANTE, no pe da tela: ela e o que ENSINA a tecla. Sem ela, um menu que so abre
		// com E e tao escondido quanto os verbos que ele veio substituir.
		_dica = new Label
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
			OffsetLeft = -160, OffsetRight = 160, OffsetTop = -104, OffsetBottom = -80,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_dica.AddThemeColorOverride("font_color", Tema.Destaque);
		AddChild(_dica);
	}

	public override void _Process(double delta)
	{
		// A DICA SEGUE O QUE ESTA POR PERTO, e some quando o menu abre (o menu ja e a resposta).
		if (_raiz.Visible) { _dica.Visible = false; return; }

		// O VEICULO VEM PRIMEIRO -- ver `Abrir`.
		if (VeiculoMontado().Length > 0)
		{
			_dica.Visible = true;
			_dica.Text = $"[E] {NomeDoVeiculo()}";
			return;
		}

		GameClient.ObraInfo? perto = MaisPerto();

		// O CORPO NO CHAO DISPUTA COM O QUE ESTA POR PERTO, e ganha pela REGRA QUE JA EXISTIA -- o mais
		// perto. Ver `Abrir`.
		if (CadaverGanha(perto))
		{
			_dica.Visible = true;
			_dica.Text = $"[E] {NomeDoCadaver()}";
			return;
		}

		_dica.Visible = perto != null;
		if (perto is { } o) _dica.Text = $"[E] {NomeDe(o)}";
	}

	/// <summary>
	/// ============================ O CORPO AOS MEUS PES -- o terceiro tipo de alvo da tecla E ============================
	/// *"basta apertar E perto do corpo pra enterrar"*. Ele entra pela mesma porta do VEICULO e pelo
	/// mesmo motivo, ja escrito no `VeiculoMontado`: **todo o resto deste menu procura alvo em
	/// `cli.Obras`**, que e a lista de construcoes da zona, e um cadaver nao esta la -- ele e um CORPO,
	/// e corpo viaja pelo snapshot, que nao diz qual deles esta morto.
	///
	/// Entao o servidor DIZ qual esta ao alcance (`Protocol.S2C.Cadaver`) e o menu o trata como alvo.
	/// Zero = nenhum.
	/// ================================================================================================================
	/// </summary>
	private static int Cadaver()
	{
		if (GameClient.Instance is not { } cli) return 0;
		return cli.CadaverPerto;
	}

	/// <summary>O nome que vai na tela ("corpo de Fulano") -- vem NO PACOTE, como o do veiculo.</summary>
	private static string NomeDoCadaver()
	{
		if (GameClient.Instance is not { } cli) return "corpo";
		return cli.NomeDoCadaver.Length > 0 ? cli.NomeDoCadaver : "corpo";
	}

	/// <summary>
	/// ============================ O CORPO GANHA DA MOBILIA? PELA DISTANCIA, COMO SEMPRE ============================
	/// **Nao ha prioridade inventada aqui**, e nao pode haver: "o mais perto ganha" ja era a regra deste
	/// menu (e o que faz o veiculo ganhar de tudo -- ele esta a distancia ZERO, embaixo de voce). Um
	/// corpo caido ao lado de uma macieira precisa da mesma resposta.
	///
	/// A POSICAO DELE SAI DO MUNDO DESENHADO e nao do pacote: o pacote diz QUAL corpo, e o corpo ja
	/// esta na tela (ele veio no snapshot como qualquer outro). Mandar a coordenada junto seria uma
	/// segunda verdade sobre onde ele esta -- e ela ficaria velha no instante em que alguem o
	/// arremessasse, que e uma coisa que se faz com cadaver o tempo todo neste jogo.
	///
	/// SEM O CORPO NA TELA (ele saiu de vista, o pacote chegou antes do snapshot), a resposta e NAO:
	/// oferecer "enterrar" apontando pra nada seria o botao que existe e o comando que falha.
	/// ==========================================================================================================
	/// </summary>
	private static bool CadaverGanha(GameClient.ObraInfo? obra)
	{
		if (Cadaver() == 0) return false;
		if (GameClient.Instance is not { } cli) return false;
		if (World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return false;
		if (World.Instancia?.PosicaoDesenhadaDe(Cadaver()) is not { } corpo) return false;

		// O MESMO CORTE POR EIXO do `MaisPerto`, e pelo mesmo motivo escrito la: o servidor mede assim
		// (`Math.Abs` em X e Y separados), e usar raio aqui abriria o menu em pontos que ele recusaria.
		if (Math.Abs(corpo.X - eu.X) > Alcance || Math.Abs(corpo.Y - eu.Y) > Alcance) return false;

		return obra is not { } o || eu.DistanceSquaredTo(corpo) < eu.DistanceSquaredTo(o.Pos);
	}

	/// <summary>
	/// A NAVE QUE ESTA EMBAIXO DE MIM, ou "" -- o alvo que nao esta no chao.
	///
	/// ============================ POR QUE ELE NAO PODE SAIR DA LISTA DE OBRAS ============================
	/// Todo o resto deste menu procura alvo em `cli.Obras`, que e a lista de construcoes da ZONA. Uma
	/// nave pilotada nao esta la de proposito: ela deixou de estar no chao e passou a andar com o
	/// corpo (`NavesParadasEm` filtra `PilotoId == 0`), e mandar a lista inteira 30x/s pra acompanhar
	/// um veiculo seria o canal errado -- aquele pacote e confiavel e so sai quando algo MUDA.
	///
	/// Entao o servidor DIZ qual e (`Protocol.S2C.Veiculo`), e ele vira alvo por outro caminho. Sem
	/// isto o piloto era a unica pessoa do servidor que nao alcancava os comandos do proprio veiculo,
	/// nem o de descer dele -- o buraco que a aba Nav tapava com uma fileira de botoes.
	/// ==================================================================================================
	/// </summary>
	private static string VeiculoMontado()
	{
		if (GameClient.Instance is not { } cli) return "";
		return Interacoes.DoVeiculo(cli.VeiculoMontado).Length > 0 ? cli.VeiculoMontado : "";
	}

	/// <summary>O nome da nave montada -- ele vem no pacote, pelo mesmo motivo do nome das obras.</summary>
	private static string NomeDoVeiculo()
	{
		if (GameClient.Instance is not { } cli) return "";
		return cli.NomeDoVeiculo.Length > 0 ? cli.NomeDoVeiculo : cli.VeiculoMontado.Replace('_', ' ');
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		// O "E" E UMA DAS LETRAS QUE O EMBATE SORTEIA -- ver `Foco.AtalhosMudos`.
		if (Foco.AtalhosMudos) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (_raiz.Visible && k.Keycode == Key.Escape)
		{
			Fechar();
			GetViewport().SetInputAsHandled();
			return;
		}

		// PELO REGISTRO, e nao por `Key.E` escrito aqui. Duas coisas dependem disso: a tecla e
		// religavel como qualquer outra, e a pergunta "essa tecla esta livre?" enxerga esta linha --
		// sem o registro ela responderia "sim" sobre o E e entregaria o menu de interacao junto com
		// o que o jogador ligou. Ver o cabecalho do `Teclas`.
		if (!Teclas.Bate("ui_interagir", k)) return;

		if (_raiz.Visible) Fechar();
		else Abrir();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>
	/// A COISA INTERATIVA MAIS PROXIMA, ou nula.
	///
	/// O CORTE E POR EIXO, e nao por raio, porque e assim que o servidor mede (`ObraPerto` compara
	/// `Math.Abs` em X e Y separados). Usar distancia euclidiana aqui abriria o menu em pontos que
	/// o servidor consideraria longe -- de novo, o botao existiria e o comando falharia.
	/// </summary>
	private static GameClient.ObraInfo? MaisPerto()
	{
		if (GameClient.Instance is not { } cli) return null;
		if (World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;

		GameClient.ObraInfo? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (GameClient.ObraInfo o in cli.Obras)
		{
			if (!Interacoes.Interativo(o.Tipo)) continue;
			if (Math.Abs(o.Pos.X - eu.X) > Alcance || Math.Abs(o.Pos.Y - eu.Y) > Alcance) continue;

			float d = eu.DistanceSquaredTo(o.Pos);
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// O NOME QUE VAI NA TELA. Ele vem NO PACOTE, junto do objeto (`ObraInfo.Nome`), e nao do
	/// catalogo local: o catalogo do cliente so tem o que ele pode COMPRAR, e mobilia de mapa (banco,
	/// macieira, porta da Sala do Tempo, as duas pecas da ponte) nunca entra nele -- essas caiam no
	/// ultimo recurso e apareciam em ingles. Ver `ObraInfo`.
	///
	/// O `Replace('_', ' ')` continua como ultimo recurso pra um tipo que o servidor mandou sem nome
	/// (linha faltando no `construcoes.json`): nome feio e melhor que nome vazio.
	/// </summary>
	private static string NomeDe(GameClient.ObraInfo o) =>
		o.Nome.Length > 0 ? o.Nome : o.Tipo.Replace('_', ' ');

	/// <summary>Que pagina do menu esta aberta. Vazio = a raiz do objeto.</summary>
	private string _pagina = "";

	/// <summary>
	/// O TIPO DO ALVO ABERTO. Guardado, e nao relido da lista de obras a cada quadro: o alvo pode
	/// ser um VEICULO, que nao esta na lista -- e uma nave em movimento sairia do alcance no meio do
	/// menu se ele fosse reperguntado.
	/// </summary>
	private string _tipoDoAlvo = "";

	/// <summary>O menu aberto e o do veiculo montado, e nao o de um objeto do chao.</summary>
	private bool _noVeiculo;

	/// <summary>
	/// ABRE O MENU DO QUE ESTA AO ALCANCE.
	///
	/// ============================ O VEICULO GANHA DE QUALQUER COISA NO CHAO ============================
	/// Nao por prioridade inventada: porque ele nao esta A DOIS TILES, ele esta EMBAIXO de voce -- a
	/// distancia e zero, e "o mais perto ganha" ja era a regra deste menu. Quem pilota um pod por
	/// cima de uma bancada quer o pod; a bancada volta a responder assim que ele descer.
	///
	/// E e o pedido do dono na ponte: *"ao apertar E DNV vc volta pra dentro da nave"*. Do leme da
	/// Capital Ship o console esta noutra ZONA -- nao ha objeto nenhum a alcance --, entao sem esta
	/// porta nao haveria como largar o leme.
	/// ================================================================================================
	/// </summary>
	private void Abrir()
	{
		_pagina = "";

		if (VeiculoMontado() is { Length: > 0 } v)
		{
			_noVeiculo = true;
			_tipoDoAlvo = v;
			_nomeDoAlvo = NomeDoVeiculo();
			Desenhar(v);
			_raiz.Visible = true;
			return;
		}

		GameClient.ObraInfo? perto = MaisPerto();

		// ============================ O CORPO NO CHAO, PELA MESMA REGRA DE SEMPRE ============================
		// Ele nao "tem prioridade": ele ganha quando esta MAIS PERTO, que e a unica regra que este menu
		// sempre teve. Ver `CadaverGanha`.
		if (CadaverGanha(perto))
		{
			_noVeiculo = false;
			_noCadaver = true;
			_tipoDoAlvo = "";
			_nomeDoAlvo = NomeDoCadaver();
			Desenhar("");
			_raiz.Visible = true;
			return;
		}

		if (perto is not { } o) return;
		_noVeiculo = false;
		_noCadaver = false;
		_tipoDoAlvo = o.Tipo;
		_nomeDoAlvo = NomeDe(o);
		Desenhar(o.Tipo);
		_raiz.Visible = true;
	}

	/// <summary>O menu aberto e o de um CORPO no chao, e nao o de um objeto nem o de um veiculo.</summary>
	private bool _noCadaver;

	/// <summary>
	/// Desenha a pagina atual do objeto. A raiz e `tipo`; um submenu e `tipo/chave`.
	/// </summary>
	private void Desenhar(string tipo)
	{
		string chave = _pagina.Length == 0 ? tipo : $"{tipo}/{_pagina}";
		_titulo.Text = _pagina.Length == 0 ? NomeDoAlvo() : NomeDoAlvo() + " — melhorias";

		foreach (Node n in _lista.GetChildren()) n.QueueFree();

		// VOLTAR SO EXISTE DENTRO DE UM SUBMENU. Na raiz ele seria o mesmo que Fechar, e dois
		// botoes pra sair da mesma tela e uma escolha que o jogador nao devia ter que fazer.
		if (_pagina.Length > 0)
		{
			var voltar = new Button { Text = "< Voltar" };
			voltar.Pressed += () => { _pagina = ""; Desenhar(tipo); };
			_lista.AddChild(voltar);
		}

		// A MESMA TELA, OUTRA TABELA: montado num veiculo o que se pode fazer com ele e outra coisa
		// (sair, largar o leme, lancar) e quase nada do que se faz com ele parado (entrar, pegar).
		// Ver `Interacoes.DoVeiculo` pro porque de nao ser uma lista so.
		//
		// VEICULO NAO TEM SUBMENU hoje, e por isso ele le sempre a raiz: nenhuma das tres naves
		// declara `Forma.Submenu`. Se um dia declarar, a chave composta ja esta montada acima.
		// O CADAVER E A TERCEIRA TABELA, e ele nao tem submenu nem tipo: as acoes de um corpo no chao
		// sao as mesmas para todo corpo (ver `Interacoes.DoCadaver`, que explica por que e uma so).
		foreach (Interacoes.Acao a in _noCadaver ? Interacoes.DoCadaver()
											     : _noVeiculo ? Interacoes.DoVeiculo(tipo)
														      : Interacoes.De(chave))
		{
			Interacoes.Acao acao = a;
			var b = new Button { Text = a.Rotulo, TooltipText = a.Dica };
			b.Pressed += () => Escolher(tipo, acao);
			_lista.AddChild(b);
		}
	}

	private void Escolher(string tipo, Interacoes.Acao acao)
	{
		switch (acao.Forma)
		{
			case Interacoes.Forma.Submenu:
				_pagina = acao.Arg;
				Desenhar(tipo);
				break;

			case Interacoes.Forma.Numero:
				AbrirTeclado(acao);
				break;

			case Interacoes.Forma.Texto:
				AbrirCaixaDeTexto(acao);
				break;

			default:
				// TUDO PELO CANAL DE VERBOS. O `abrir_tech` abria a grade da bancada de pesquisa por
				// aqui; a grade morreu (fabricar e na aba Tech do menu P) e com ela o unico desvio que
				// este `default` tinha.
				GameClient.Instance?.SendVerbo(acao.Verbo, acao.Arg);
				Fechar();
				break;
		}
	}

	/// <summary>
	/// O NOME NO ALTO DO MENU. Guardado ao abrir, e nao relido: o alvo pode ser um veiculo, que nao
	/// esta na lista de obras, e uma obra pode sumir da lista com o menu dela aberto (alguem a
	/// recolheu) -- e um titulo que fica vazio no meio da tela e pior que um titulo velho.
	/// </summary>
	private string _nomeDoAlvo = "";

	private string NomeDoAlvo() => _nomeDoAlvo;

	// =====================================================================
	// O TECLADO NUMERICO
	// =====================================================================
	/// <summary>O teclado aberto agora, se houver. Ver <see cref="TecladoNumerico"/>.</summary>
	private CenterContainer? _teclado;

	private void AbrirTeclado(Interacoes.Acao acao)
	{
		FecharTeclado();

		var t = new TecladoNumerico(acao.Rotulo, acao.Min, acao.Max,
			v => { GameClient.Instance?.SendVerbo(acao.Verbo, $"{v:0}"); Fechar(); },
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(t);
		_raiz.AddChild(_teclado);
	}

	/// <summary>
	/// A CAIXA DE TEXTO -- a terceira pergunta que o menu aprendeu (a lapide: *"deveria ter a opcao de
	/// escrever manualmente na lapide, abrindo uma caixa de texto"*, 2026-09-04). Mesmo lugar e mesmo
	/// ciclo de vida do teclado numerico; o valor inicial vem da tabela das acoes (`Interacoes.TextoInicial`).
	/// </summary>
	private void AbrirCaixaDeTexto(Interacoes.Acao acao)
	{
		FecharTeclado();

		int teto = acao.Max > 0 ? (int)acao.Max : Jandirus.Net.Protocol.MaxArgDeVerbo;
		var c = new CaixaDeTexto(acao.Rotulo, Interacoes.TextoInicial(acao, _nomeDoAlvo), teto,
			texto => { GameClient.Instance?.SendVerbo(acao.Verbo, texto); Fechar(); },
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(c);
		_raiz.AddChild(_teclado);
	}

	private void FecharTeclado()
	{
		_teclado?.QueueFree();
		_teclado = null;
	}

	private void Fechar()
	{
		FecharTeclado();
		_raiz.Visible = false;
		_pagina = "";
		_tipoDoAlvo = "";
		_nomeDoAlvo = "";
		_noVeiculo = false;
		_noCadaver = false;
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagembarque`)
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ELA PRECISA EXISTIR ============================
	/// "O menu abriu com a opcao de embarcar" e uma afirmacao que, sem isto, ninguem consegue
	/// conferir: o menu e um `CanvasLayer` com `Button`s dentro, e um `Button` nao devolve nada. A
	/// bancada de servidor pode provar que `Interacoes.De("Capital_Ship")` tem cinco linhas -- e o
	/// arquivo `dbclimax-port-bancada-mede-intencao` conta o que essa leitura vale: quatro defeitos
	/// visuais passaram por quatro mil checagens verdes porque a bancada media a TABELA, e o defeito
	/// morava no widget que a desenha.
	///
	/// Entao aqui nao se le tabela nenhuma: le-se o `_lista`, que e o node que estava na tela, e
	/// aperta-se o `Button` que estava nele. O que a bancada afirma e o que o jogador veria.
	/// =====================================================================================
	///
	/// TUDO SO-LEITURA MENOS UM: o <see cref="ApertarDesenhado"/> emite o MESMO sinal que o dedo
	/// emite -- ele nao chama `Escolher` por dentro, senao a bancada pularia justamente a ligacao
	/// (`b.Pressed += ...`) que pode estar faltando.
	/// </summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>O titulo desenhado no alto do menu aberto -- vazio quando nao ha menu.</summary>
	public string TituloDesenhado => NaTela && IsInstanceValid(_titulo) ? _titulo.Text : "";

	/// <summary>
	/// A DICA FLUTUANTE do pe da tela ("[E] Console da Ponte"), ou vazio quando ela nao esta acesa.
	///
	/// Ela e a metade que ENSINA a tecla, e e a que reprova o caso "longe": um menu que nao abre
	/// prova pouco se a dica continua acesa dizendo que ha alguma coisa ali.
	/// </summary>
	public string DicaNaTela => IsInstanceValid(_dica) && _dica.Visible ? _dica.Text : "";

	/// <summary>Os rotulos dos botoes que o menu aberto DESENHOU, de cima pra baixo.</summary>
	public List<string> BotoesDesenhados()
	{
		var r = new List<string>();
		if (!NaTela || !IsInstanceValid(_lista)) return r;
		foreach (Node n in _lista.GetChildren())
			if (n is Button b && IsInstanceValid(b)) r.Add(b.Text);
		return r;
	}

	/// <summary>Aperta um botao do menu aberto pelo rotulo. Falso quando ele nao esta na tela.</summary>
	public bool ApertarDesenhado(string rotulo)
	{
		if (!NaTela || !IsInstanceValid(_lista)) return false;
		foreach (Node n in _lista.GetChildren())
		{
			if (n is not Button b || !IsInstanceValid(b) || b.Text != rotulo) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	/// <summary>O teclado numerico esta aberto? (a segunda pagina do menu, ver <see cref="Forma"/>).</summary>
	public bool TecladoNaTela => _teclado != null && IsInstanceValid(_teclado);

	/// <summary>A caixa de texto (`Forma.Texto`) esta na tela? Mora no mesmo lugar do teclado numerico.</summary>
	public bool CaixaDeTextoNaTela =>
		TecladoNaTela && _teclado!.GetChildCount() > 0 && _teclado.GetChild(0) is CaixaDeTexto;

	/// <summary>
	/// DIGITA NA CAIXA DE TEXTO como o jogador digitaria -- o campo inteiro, e nao tecla a tecla: o
	/// `LineEdit` e do Godot e nao tem regra propria a provar -- e confirma pelo mesmo "Gravar". Devolve
	/// o que o campo MOSTRAVA antes: o valor inicial (`Interacoes.TextoInicial`), pra bancada comparar
	/// com a resposta pronta do `input()` do DM.
	/// </summary>
	public string DigitarNaCaixa(string texto, bool confirmar = true)
	{
		if (!CaixaDeTextoNaTela || _teclado!.GetChild(0) is not CaixaDeTexto c) return "";
		string antes = c.DigitarDeTeste(texto);
		if (confirmar) c.ConfirmarDeTeste();
		return antes;
	}

	/// <summary>
	/// DIGITA UM NUMERO NO TECLADO ABERTO e confirma -- tecla a tecla, pelos mesmos botoes.
	///
	/// NAO chama o `aoConfirmar` por dentro de proposito: o teto de algarismos do teclado
	/// (`TecladoNumerico`) e justamente uma das coisas que podem nao caber no `Max` que a acao
	/// declarou, e um atalho que "digita" o numero inteiro passaria por cima disso. Devolve o que o
	/// VISOR ficou mostrando, pra a bancada poder comparar com o que se pediu.
	/// </summary>
	public string DigitarNoTeclado(string numero, bool confirmar = true)
	{
		if (!TecladoNaTela || _teclado!.GetChildCount() == 0) return "";
		if (_teclado.GetChild(0) is not TecladoNumerico t) return "";

		foreach (char c in numero) t.ApertarTecla(c.ToString());
		string visor = t.VisorDeTeste;
		if (confirmar) t.ApertarTecla("Confirmar");
		return visor;
	}
}
