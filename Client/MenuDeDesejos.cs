using Godot;
using Jandirus.Core.Magic;

namespace Jandirus.Client;

/// <summary>
/// **A JANELA DE DESEJOS** -- o que aparece quando o dragao sobe.
///
/// ============================ O PEDIDO DO DONO (2026-10-10) ============================
/// *"ao invocar o dragao das esferas, deveria aparecer um menu com varios desejos q vc pode escolher e caso
/// tenha um target o jogo vai perguntar se e pra vc ou pra outra pessoa q vc pode escolher e nao ter q abrir um
/// menu e colocar um id de pedido."*
///
/// Era: o dragao subia, o chat ganhava vinte linhas com um id entre colchetes em cada uma, e a aba World tinha
/// uma caixa de texto pra escrever o id e o nome do alvo. Agora sao tres etapas, todas de clicar:
///
///   1. A LISTA: cada desejo com o nome e uma linha do que faz. O que nao pode ser pedido agora aparece
///      apagado, com o motivo embaixo.
///   2. "PRA QUEM?", so nos desejos que alcancam alguem: "Para mim" e a lista de quem o desejo alcanca -- com
///      o alvo MARCADO de quem pede (o duplo clique) na frente.
///   3. CONFIRMAR: o que vai ser pedido, pra quem, e quanto custa. So aqui sai o pedido.
/// ======================================================================================
///
/// ============================ ELA NAO DECIDE NADA ============================
/// Tudo o que ela desenha veio no `S2C.MenuDoDragao` (`Core/Magic/MenuDoDragao.cs`): as linhas, os motivos,
/// as respostas, e ate o verbo e o argumento de cada pedido, que ela devolve como recebeu. Quem monta a lista
/// e quem confere o pedido e o servidor (`GameServer.MenuDoDragao.cs`). Um cliente mexido que desenhe outra
/// lista so consegue mandar verbo que o servidor recusa.
///
/// E ELA NAO PERGUNTA DE NOVO: enquanto esta aberta o servidor a mantem em dia (a contagem cai, alguem chega ao
/// alcance, o alvo marcado troca) e a fecha quando o dragao vai embora. Fechada, ela avisa (`db_menu fim`).
/// =============================================================================
///
/// ============================ NA DIREITA DA TELA, E O JOGO CONTINUA ============================
/// O dragao sobe um tile ao norte de quem invocou, no meio da tela, com onze tiles de altura: uma caixa no
/// centro taparia justamente o que acabou de aparecer. Ela mora na direita, e o resto da tela continua sendo
/// do jogo -- o clique fora dela chega ao mundo (da pra marcar alguem com duplo clique e ve-lo subir pro alto
/// da lista), e ela nao trava tecla nenhuma, como o menu do P: nenhum botao dela pega o foco do teclado (um
/// botao com foco responde a ENTER e a espaco, e ENTER e a tecla de falar).
///
/// E EMBAIXO DO HUD, NAO EM CIMA DELE: a beirada direita ja tem dono -- em cima, o bloco da mira e do boneco do
/// corpo; embaixo, o disco do Dragon Radar de quem tem o aparelho. A janela se assenta entre os dois (ver
/// <see cref="Assentar"/>): quem escolhe um desejo no meio de uma briga continua vendo o proprio corpo.
/// ================================================================================================
/// </summary>
public partial class MenuDeDesejos : CanvasLayer
{
	/// <summary>A janela viva, ou nula antes do mundo. E por aqui que a aba World e as bancadas a alcancam.</summary>
	public static MenuDeDesejos? Instancia { get; private set; }

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagdesejos`): a janela ignora o aviso de que o dragao foi embora e continua
	/// na tela, oferecendo pedidos a um dragao que nao existe mais. Falso em jogo, sempre.
	/// </summary>
	public static bool FicaAbertaSemDragaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagdesejos`): a janela vai da margem de cima a de baixo, por cima do bloco da
	/// mira e do boneco do HUD -- o que ela fazia na primeira foto. Falso em jogo, sempre.
	/// </summary>
	public static bool CobreOHudDeTeste;

	/// <summary>A largura da janela, em pixels. O texto de uma linha de desejo quebra dentro dela.</summary>
	private const int Largura = 500;

	/// <summary>A folga entre a janela e as bordas da tela.</summary>
	private const int Margem = 24;

	/// <summary>O respiro entre a janela e o que o HUD ocupa na beirada direita (em cima e embaixo).</summary>
	private const int FolgaDoHud = 8;

	/// <summary>
	/// A menor altura em que a janela ainda serve: o cabecalho, quatro linhas de desejo e o botao de fechar. Se
	/// desviar do HUD a deixaria menor que isto (uma janela de jogo baixa), ela para de desviar -- primeiro do
	/// disco do radar, depois do bloco de cima.
	/// </summary>
	private const int AlturaMinima = 420;

	private enum Etapa { Lista, Resposta, Confirmar }

	private Control _raiz = null!;
	private PanelContainer _painel = null!;
	private Label _nome = null!, _titulo = null!, _contagem = null!, _estado = null!;
	private ScrollContainer _rolagem = null!;
	private VBoxContainer _corpo = null!;
	private Button _voltar = null!;

	// ============================ O QUE A JANELA LEMBRA ============================
	// So ONDE o jogador esta nas tres etapas -- e isso fica quando ela fecha, pra reabrir no mesmo ponto (o
	// dono fecha pra olhar o mundo e volta). Quantos pedidos restam, quem esta na lista e o que pode ser pedido
	// nao moram aqui: sao do servidor e chegam de novo a cada menu.
	// ===============================================================================
	private Etapa _etapa = Etapa.Lista;

	/// <summary>A linha aberta nas etapas 2 e 3 (<see cref="EntradaDoDragao.Id"/>).</summary>
	private string _entrada = "";

	/// <summary>A resposta escolhida na etapa 2 (<see cref="EscolhaDoDragao.Pedido"/>). Vazio = a linha nao pergunta.</summary>
	private string _resposta = "";

	/// <summary>A assinatura do que esta desenhado: menu + etapa. Igual, nao se refaz nada.</summary>
	private string _desenhado = "";

	/// <summary>O relogio do dragao, contado aqui entre um menu e o seguinte (<see cref="MenuDoDragao.Segundos"/>).</summary>
	private double _segundos;

	public override void _Ready()
	{
		Layer = 4;   // a das telas "do mundo": o menu da tecla E, a mochila, o refugio
		Instancia = this;
		Montar();

		if (GameClient.Instance is { } cli)
		{
			// METODOS NOMEADOS, E NAO LAMBDAS: o `GameClient` sobrevive ao logout, e assinatura com lambda nao
			// da pra cancelar (19 orfaos por relog, ja pagos uma vez). Ver `_ExitTree`.
			cli.MenuDoDragaoMudou += AoMudarOMenu;
			cli.MenuDoDragaoPediuAbrir += AoPedirAbrir;
			cli.DragaoFoiEmbora += AoDragaoIrEmbora;
		}
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.MenuDoDragaoMudou -= AoMudarOMenu;
			cli.MenuDoDragaoPediuAbrir -= AoPedirAbrir;
			cli.DragaoFoiEmbora -= AoDragaoIrEmbora;
		}
		if (Instancia == this) Instancia = null;
	}

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	private void Montar()
	{
		// A RAIZ NAO PEGA O MOUSE: fora do painel, a tela e do jogo. Ver o cabecalho.
		_raiz = new Control
		{
			AnchorRight = 1, AnchorBottom = 1, Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// COLADA NA DIREITA. Ancora, e nao posicao: ela e recalculada a cada resize, de graca (a caixa que
		// guardava pixels foi a que o dono fotografou torta -- ver `TelaDeRefugio`). O alto e o pe se acertam
		// pelo que o HUD ocupa naquela beirada: ver `Assentar`.
		_painel = Tema.Painel1(14);
		_painel.AnchorLeft = 1;
		_painel.AnchorRight = 1;
		_painel.AnchorTop = 0;
		_painel.AnchorBottom = 1;
		_painel.OffsetLeft = -(Largura + Margem);
		_painel.OffsetRight = -Margem;
		_painel.OffsetTop = Margem;
		_painel.OffsetBottom = -Margem;
		// O PAINEL PARA O MOUSE: um conteiner deixa o clique passar pro pai, e o pai aqui o ignora -- sem isto
		// o clique num espaco vazio da janela ia parar no mundo atras dela.
		_painel.MouseFilter = Control.MouseFilterEnum.Stop;
		_raiz.AddChild(_painel);

		var coluna = new VBoxContainer();
		coluna.AddThemeConstantOverride("separation", 6);
		_painel.AddChild(coluna);

		_nome = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_nome.AddThemeFontSizeOverride("font_size", 22);
		_nome.AddThemeColorOverride("font_color", Tema.Destaque);
		coluna.AddChild(_nome);

		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 12);
		_titulo.AddThemeColorOverride("font_color", Tema.TextoFraco);
		coluna.AddChild(_titulo);

		_contagem = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_contagem.AddThemeFontSizeOverride("font_size", 14);
		coluna.AddChild(_contagem);

		_estado = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		_estado.AddThemeFontSizeOverride("font_size", 12);
		coluna.AddChild(_estado);
		coluna.AddChild(new HSeparator());

		_rolagem = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		coluna.AddChild(_rolagem);

		_corpo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_corpo.AddThemeConstantOverride("separation", 5);
		_rolagem.AddChild(_corpo);

		// O RODAPE NAO ROLA: "Voltar" e "Fechar" ficam sempre a mao. Na ponta de uma lista comprida (trinta
		// mortos pra escolher um) o "Voltar" so aparecia depois de rolar tudo -- e numa janela baixa ficava
		// cortado pela beirada da rolagem.
		coluna.AddChild(new HSeparator());
		var pe = new HBoxContainer();
		pe.AddThemeConstantOverride("separation", 6);
		coluna.AddChild(pe);

		_voltar = Botao("‹ Voltar");
		_voltar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_voltar.Visible = false;   // so nas etapas 2 e 3 -- ver `Redesenhar`
		_voltar.Pressed += Voltar;
		pe.AddChild(_voltar);

		Button fechar = Botao("Fechar (Esc)");
		fechar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		fechar.TooltipText = "o dragão continua de pé: reabra pela aba World, \"Ver os desejos\"";
		fechar.Pressed += Fechar;
		pe.AddChild(fechar);
	}

	/// <summary>
	/// UM BOTAO DESTA JANELA: so o mouse o aperta. Sem isto o botao clicado fica com o foco do teclado, e o
	/// proximo ENTER (a tecla de falar) ou espaco o apertaria de novo -- "Fechar" fechando sozinho, ou pior,
	/// "PEDIR" pedindo. A janela fica aberta enquanto o jogador joga; ela nao pode ser dona de tecla nenhuma.
	/// </summary>
	private static Button Botao(string texto) => new() { Text = texto, FocusMode = Control.FocusModeEnum.None };

	// =====================================================================
	// ONDE ELA SE ASSENTA
	// =====================================================================
	/// <summary>O bloco da direita do HUD (a mira e o boneco do corpo) e o disco do Dragon Radar, achados uma vez.</summary>
	private Control? _blocoDoHud, _radarDoHud;

	/// <summary>O HUD em que os dois de cima foram achados: outro HUD (um relog), outra procura.</summary>
	private Hud? _hudConhecido;

	/// <summary>
	/// **O ALTO E O PE DA JANELA**, pelo que o HUD ocupa na beirada direita AGORA:
	///
	///   * EM CIMA, o bloco da mira e do boneco do corpo: a janela comeca embaixo dele;
	///   * EMBAIXO, o disco do Dragon Radar -- so de quem tem o aparelho, e so enquanto ele esta ligado: a
	///     janela termina em cima dele, se ainda sobrar <see cref="AlturaMinima"/>.
	///
	/// OS DOIS SAO LIDOS DO HUD, e nao copiados em numero: o bloco de cima cresce pra esquerda e muda de altura
	/// com o que tiver dentro, e uma medida cravada aqui envelheceria calada (o proprio `Hud.MontarDireita`
	/// conta essa historia). Sao achados pelo NOME que o `Hud` da aos nodes ("Corpo", "RadarDeEsferas"), e
	/// assim o HUD nao precisa saber que esta janela existe; em troca, um node renomeado la deixaria a janela
	/// por cima dele -- e e isso que a bancada `--diagdesejos` mede na tela: o retangulo da janela nao encosta
	/// no do boneco nem no da mira.
	///
	/// Roda a cada quadro em que a janela esta na tela, e so mexe no que mudou: sao duas leituras de retangulo.
	/// </summary>
	private void Assentar()
	{
		float tela = GetViewport().GetVisibleRect().Size.Y;
		float alto = Margem, pe = tela - Margem;

		if (!CobreOHudDeTeste && Hud.Instancia is { } hud)
		{
			// A PROCURA E UMA SO por HUD: de novo se o HUD e outro (um relog) ou se um dos achados morreu.
			bool morreu = (_blocoDoHud != null && !IsInstanceValid(_blocoDoHud)) || (_radarDoHud != null && !IsInstanceValid(_radarDoHud));
			if (hud != _hudConhecido || morreu)
			{
				_hudConhecido = hud;
				_blocoDoHud = PainelEmVoltaDe(hud.FindChild("Corpo", true, false));
				_radarDoHud = hud.FindChild("RadarDeEsferas", true, false) as Control;
			}

			if (_blocoDoHud is { } bloco && bloco.IsVisibleInTree())
			{
				float embaixoDoBloco = bloco.GetGlobalRect().End.Y + FolgaDoHud;
				if (pe - embaixoDoBloco >= AlturaMinima) alto = Math.Max(alto, embaixoDoBloco);
			}

			if (_radarDoHud is { } radar && radar.IsVisibleInTree())
			{
				float emCimaDoRadar = radar.GetGlobalRect().Position.Y - FolgaDoHud;
				if (emCimaDoRadar - alto >= AlturaMinima) pe = emCimaDoRadar;
			}
		}

		// (ancora de cima em 0 e a de baixo em 1: o deslocamento de baixo e negativo.)
		if (Math.Abs(_painel.OffsetTop - alto) > 0.5f) _painel.OffsetTop = alto;
		if (Math.Abs(_painel.OffsetBottom - (pe - tela)) > 0.5f) _painel.OffsetBottom = pe - tela;
	}

	/// <summary>O painel do HUD que contem um node: o primeiro `PanelContainer` subindo a arvore, ou nulo.</summary>
	private static Control? PainelEmVoltaDe(Node? dentro)
	{
		for (Node? n = dentro; n != null; n = n.GetParent())
			if (n is PanelContainer p) return p;
		return null;
	}

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	// QUEM ABRE E O SERVIDOR (`AoPedirAbrir`): o dragao subiu, ou o jogador apertou "Ver os desejos" na aba
	// World -- que manda `db_menu` e recebe o menu de agora, com `abrir`. Nao ha um "abrir" local: a janela
	// nunca aparece com uma lista guardada.

	/// <summary>
	/// FECHA, e avisa o servidor: ele para de acertar uma janela que ninguem esta olhando. O dragao continua de
	/// pe e a etapa em que o jogador estava fica guardada.
	/// </summary>
	public void Fechar()
	{
		if (!NaTela) return;
		_raiz.Visible = false;
		GameClient.Instance?.SendVerbo("db_menu", "fim");
	}

	/// <summary>A janela esta na tela? Le o NODE, e nao um `bool` proprio -- ver `TelaDeMeditacao.NaTela`.</summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>
	/// O DIRETOR DO TRAILER FILMA O DRAGAO SUBINDO (`--trailer`, a cena das esferas) e pede o desejo por verbo:
	/// uma janela abrindo sozinha por cima da tomada estragaria a tomada.
	/// </summary>
	private static bool Filmando => Array.IndexOf(OS.GetCmdlineArgs(), "--trailer") >= 0;

	private void AoPedirAbrir()
	{
		if (GameClient.Instance is not { MenuDoDragao: { } m } cli) return;
		if (Filmando) { cli.SendVerbo("db_menu", "fim"); return; }

		// O SERVIDOR MANDOU ABRIR NUMA LINHA (o botao "Derrubar a estatua", o preco esperando a palavra): a
		// janela ja abre nela -- na pergunta, se a linha pergunta, ou direto no confirmar.
		if (m.Foco.Length > 0 && m.Entrada(m.Foco) is { Pode: true, Direto: false } alvo)
		{
			_entrada = alvo.Id;
			_resposta = "";
			_etapa = alvo.TemPergunta ? Etapa.Resposta : Etapa.Confirmar;
		}

		// O MENU DO P SAI DA FRENTE: ele mora no meio da tela, esta janela encosta nele, e quem apertou
		// "INVOCAR" ou "Ver os desejos" la dentro ja fez o que tinha pra fazer nele. (DESTACADO numa janela do
		// sistema ele nao esta na frente de nada, e fica: ver `MenuJogo.NaFrenteDoJogo`.)
		if (MenuJogo.NaFrenteDoJogo) MenuJogo.Instancia?.Fechar();

		Assentar();   // antes de aparecer: ela nao pode nascer por cima do HUD e pular de lugar um quadro depois
		_raiz.Visible = true;
		Redesenhar(forcar: true);
	}

	/// <summary>
	/// CHEGOU UM MENU. O relogio do dragao se acerta AQUI, e so aqui: o menu so chega de novo quando algo nele
	/// muda, e ate la quem conta os segundos e o `_Process` -- redesenhar por um clique nao pode devolver o
	/// relogio ao numero do ultimo pacote.
	/// </summary>
	private void AoMudarOMenu()
	{
		if (GameClient.Instance?.MenuDoDragao is { } m) _segundos = m.Segundos;
		if (NaTela) Redesenhar();
	}

	/// <summary>
	/// O DRAGAO FOI EMBORA: a janela fecha, e a etapa guardada vai junto -- a proxima invocacao comeca na lista,
	/// e nao no "matar quem?" de um dragao que ja nao existe.
	/// </summary>
	private void AoDragaoIrEmbora(QualDragao qual)
	{
		if (FicaAbertaSemDragaoDeTeste) return;
		// O AVISO E DO DRAGAO QUE ESTA JANELA MOSTRA. O servidor so o manda a quem olhava aquele dragao, e esta
		// conferencia e a rede de baixo: um aviso atrasado nao pode fechar o menu de OUTRO dragao.
		if (GameClient.Instance?.MenuDoDragao is { } m && m.Dragao != qual) return;
		VoltarPraLista();
		if (IsInstanceValid(_raiz)) _raiz.Visible = false;
	}

	/// <summary>
	/// ESC VOLTA UMA ETAPA, e na lista fecha. `_Input` e nao `_UnhandledInput` pelo motivo medido na tela de
	/// apagar personagem: ele roda antes, entao a coisa mais na frente da tela responde primeiro, e o menu de
	/// pausa (que escuta a mesma tecla) nao abre junto.
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (!NaTela || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;

		if (_etapa == Etapa.Lista) Fechar();
		else Voltar();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>
	/// Com a janela na tela: ela se mantem assentada (o HUD muda de tamanho, a janela do jogo tambem), e o relogio
	/// do dragao anda entre um menu e outro -- a linha da contagem mostra quanto ele ainda espera.
	/// </summary>
	public override void _Process(double delta)
	{
		if (!NaTela) return;
		Assentar();

		if (_segundos <= 0) return;
		_segundos = Math.Max(0, _segundos - delta);
		if (GameClient.Instance?.MenuDoDragao is { } m)
		{
			string texto = TextoDaContagem(m);
			if (_contagem.Text != texto) _contagem.Text = texto;
		}
	}

	// =====================================================================
	// AS TRES ETAPAS
	// =====================================================================
	private void VoltarPraLista()
	{
		_etapa = Etapa.Lista;
		_entrada = "";
		_resposta = "";
	}

	/// <summary>Uma etapa pra tras: do confirmar pra pergunta (se a linha pergunta), e dali pra lista.</summary>
	private void Voltar()
	{
		MenuDoDragao? m = GameClient.Instance?.MenuDoDragao;
		if (_etapa == Etapa.Confirmar && m?.Entrada(_entrada) is { TemPergunta: true })
		{
			_etapa = Etapa.Resposta;
			_resposta = "";
		}
		else VoltarPraLista();
		Redesenhar();
	}

	private void AbrirEntrada(EntradaDoDragao e)
	{
		if (!e.Pode) return;
		if (e.Direto) { Mandar(e.Verbo, e.Pedido); return; }

		// (2026-10-22) "CONFIGURAR O DRAGAO" NAO TEM ETAPA AQUI: ele abre o menu da estatua (`JanelaDaEstatua`) com o que o set tem -- o
		// servidor manda a configuracao de agora no argumento da linha, e o menu devolve a nova pelo mesmo verbo (`db_refazer`).
		if (e.Id == AcoesDoDragao.Configurar && ConfigDaEstatua.Ler(e.Pedido) is { } atual && GameClient.Instance is { } cli
			&& JanelaDaEstatua.Instancia is { } estatua)
		{
			estatua.AbrirParaRefazer(cli.Zone.Name, cli.NomeDoLugar, atual);
			return;
		}

		_entrada = e.Id;
		_resposta = "";
		_etapa = e.TemPergunta ? Etapa.Resposta : Etapa.Confirmar;
		Redesenhar();
	}

	private void Responder(EscolhaDoDragao c)
	{
		if (!c.Pode) return;
		_resposta = c.Pedido;
		_etapa = Etapa.Confirmar;
		Redesenhar();
	}

	/// <summary>
	/// O PEDIDO SAI -- o verbo e o argumento que o servidor escreveu na linha, devolvidos como vieram. A janela
	/// volta pra lista na hora: a resposta do servidor e o menu seguinte (a contagem caiu) ou o dragao indo
	/// embora (a janela fecha), e a recusa, quando ha, e uma frase dele no chat.
	/// </summary>
	private void Mandar(string verbo, string pedido)
	{
		GameClient.Instance?.SendVerbo(verbo, pedido);
		VoltarPraLista();
		Redesenhar();
	}

	/// <summary>
	/// A ETAPA GUARDADA AINDA EXISTE NESTE MENU? O menu muda por baixo de quem esta escolhendo (a pessoa saiu do
	/// alcance, o desejo ficou apagado, o dragao e outro): o que nao existe mais devolve o jogador um passo.
	/// </summary>
	private void ConferirAEtapa(MenuDoDragao m)
	{
		if (_etapa == Etapa.Lista) return;

		if (m.Entrada(_entrada) is not { Pode: true } e) { VoltarPraLista(); return; }
		if (!e.TemPergunta)
		{
			if (_etapa == Etapa.Resposta) _etapa = Etapa.Confirmar;
			_resposta = "";
			return;
		}
		if (_etapa == Etapa.Confirmar && !e.Escolhas.Exists(c => c.Pode && c.Pedido == _resposta))
		{
			_etapa = Etapa.Resposta;
			_resposta = "";
		}
	}

	private string TextoDaContagem(MenuDoDragao m)
	{
		string relogio = _segundos > 0 ? $"{(int)Math.Ceiling(_segundos) / 60}:{(int)Math.Ceiling(_segundos) % 60:00}" : "";

		if (m.Dragao == QualDragao.Comum && m.Total > 0)
		{
			if (m.DePe)
				return $"Pedidos restantes nesta invocação: {m.Restam} de {m.Total}"
					 + (relogio.Length > 0 ? $"  ·  o dragão espera mais {relogio}" : "");
			return (m.Total == 1 ? "1 pedido por invocação" : $"{m.Total} pedidos por invocação")
				 + (m.Restam < m.Total ? $"  ·  restam {m.Restam} nesta ativação" : "");
		}

		// O SUPER SHENRON atende um desejo so, e a frase do alto ja diz isso: aqui so cabe o prazo de uma
		// pergunta que espera a palavra do jogador (o preco, a oferta de guarda).
		return relogio.Length > 0 ? $"A pergunta vence em {relogio}" : "";
	}

	/// <summary>
	/// REFAZ A JANELA a partir do menu do servidor e da etapa do jogador. Nao refaz nada se o que esta desenhado
	/// ja e isso: o menu chega de novo sempre que qualquer coisa nele muda, e uma lista que se remonta a toa
	/// pisca embaixo do mouse.
	/// </summary>
	private void Redesenhar(bool forcar = false)
	{
		if (!IsInstanceValid(_corpo)) return;
		if (GameClient.Instance?.MenuDoDragao is not { } m) return;

		ConferirAEtapa(m);

		string agora = $"{m.Assinatura()}\n{_etapa}|{_entrada}|{_resposta}";
		if (!forcar && agora == _desenhado) return;
		bool mudouDeEtapa = !_desenhado.EndsWith($"\n{_etapa}|{_entrada}|{_resposta}", StringComparison.Ordinal);
		_desenhado = agora;

		_nome.Text = m.Nome;
		_titulo.Text = m.Titulo;
		_titulo.Visible = m.Titulo.Length > 0;
		_contagem.Text = TextoDaContagem(m);
		_contagem.Visible = _contagem.Text.Length > 0;
		_estado.Text = m.Estado;
		_estado.Visible = m.Estado.Length > 0;
		// DA PRA PEDIR: a frase e so informacao. NAO DA: ela e o motivo, e ganha o dourado do tema.
		_estado.AddThemeColorOverride("font_color", m.DePe ? Tema.Texto : Tema.Nutricao);

		foreach (Node n in _corpo.GetChildren()) { _corpo.RemoveChild(n); n.QueueFree(); }

		switch (_etapa)
		{
			case Etapa.Lista: DesenharALista(m); break;
			case Etapa.Resposta: DesenharAPergunta(m.Entrada(_entrada)!); break;
			case Etapa.Confirmar: DesenharOConfirmar(m, m.Entrada(_entrada)!); break;
		}

		// "VOLTAR" SO EXISTE ONDE HA PRA ONDE VOLTAR: na pergunta e no confirmar.
		_voltar.Visible = _etapa != Etapa.Lista;

		// ETAPA NOVA COMECA DO ALTO; a mesma etapa com o menu em dia fica onde o jogador rolou.
		if (mudouDeEtapa) _rolagem.ScrollVertical = 0;
	}

	// ---------------------------------------------------------------- 1. a lista
	private void DesenharALista(MenuDoDragao m)
	{
		List<EntradaDoDragao> desejos = [.. m.Entradas.Where(e => !e.Acao)];
		List<EntradaDoDragao> gestos = [.. m.Entradas.Where(e => e.Acao)];

		if (desejos.Count > 0)
		{
			// O SERVIDOR MANDA PRIMEIRO O QUE DA PRA PEDIR (`GameServer.PorNaOrdemDaJanela`); aqui so entra o
			// titulo de cada trecho: a primeira linha apagada abre o das que nao dao. Sem nenhuma pedivel (o
			// dragao nao esta de pe, ou e de outro) a lista inteira e so leitura, com um titulo so.
			bool haPediveis = desejos.Exists(e => e.Pode);
			_corpo.AddChild(Tema.Rotulo(haPediveis ? "escolha o seu desejo" : "o que este dragão atende"));

			bool separou = false;
			foreach (EntradaDoDragao e in desejos)
			{
				if (haPediveis && !e.Pode && !separou)
				{
					_corpo.AddChild(Tema.Rotulo("não dá pra pedir agora"));
					separou = true;
				}
				_corpo.AddChild(CartaoDaLinha(e));
			}
		}

		if (gestos.Count > 0)
		{
			_corpo.AddChild(Tema.Rotulo("outros gestos"));
			foreach (EntradaDoDragao e in gestos) _corpo.AddChild(CartaoDaLinha(e));
		}
	}

	/// <summary>
	/// UMA LINHA DA LISTA: o nome, uma linha do que faz e, embaixo, o motivo -- em vermelho se a linha esta
	/// apagada, em dourado se e so um aviso de uma linha que da pra pedir.
	/// </summary>
	private PanelContainer CartaoDaLinha(EntradaDoDragao e)
	{
		var conteudo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		conteudo.AddThemeConstantOverride("separation", 1);

		var cabeca = new HBoxContainer();
		var nome = new Label { Name = "Nome", Text = e.Nome, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		nome.AddThemeFontSizeOverride("font_size", 15);
		nome.AddThemeColorOverride("font_color", !e.Pode ? Tema.TextoFraco : e.Perigo ? Tema.Perigo : Tema.Texto);
		cabeca.AddChild(nome);

		// A SETA DIZ QUE A LINHA PERGUNTA ANTES DE PEDIR: o jogador sabe, antes de clicar, que "Matar alguém"
		// ainda vai perguntar quem -- e que "Riqueza" nao.
		if (e.Pode && e.TemPergunta)
		{
			var seta = new Label { Text = "escolher ›" };
			seta.AddThemeFontSizeOverride("font_size", 11);
			seta.AddThemeColorOverride("font_color", Tema.Destaque);
			cabeca.AddChild(seta);
		}
		conteudo.AddChild(cabeca);

		if (e.Desc.Length > 0)
		{
			var desc = new Label { Name = "Desc", Text = e.Desc, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			desc.AddThemeFontSizeOverride("font_size", 11);
			desc.AddThemeColorOverride("font_color", Tema.TextoFraco);
			conteudo.AddChild(desc);
		}

		if (e.Motivo.Length > 0)
		{
			var motivo = new Label { Name = "Motivo", Text = e.Motivo, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			motivo.AddThemeFontSizeOverride("font_size", 11);
			motivo.AddThemeColorOverride("font_color", e.Pode ? Tema.Nutricao : Tema.Perigo);
			conteudo.AddChild(motivo);
		}

		StyleBoxFlat moldura = e.Pode
			? Tema.Caixa(Tema.PainelClaro, e.Perigo ? Tema.Perigo : Tema.Borda, 8)
			: Tema.Caixa(Tema.PainelApagado, Tema.BordaApagada, 8);
		PanelContainer cartao = Cartao(conteudo, moldura, e.Pode ? () => AbrirEntrada(e) : null);
		// A IDENTIDADE VAI EM METADADO, e nao no `Name`: o Godot renomeia irmaos homonimos.
		cartao.SetMeta("linha", e.Id);
		cartao.SetMeta("pode", e.Pode);
		return cartao;
	}

	// ---------------------------------------------------------------- 2. "pra quem?"
	private void DesenharAPergunta(EntradaDoDragao e)
	{
		var nome = new Label { Text = e.Nome, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		nome.AddThemeFontSizeOverride("font_size", 17);
		_corpo.AddChild(nome);

		var pergunta = new Label { Name = "Pergunta", Text = e.Pergunta, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		pergunta.AddThemeFontSizeOverride("font_size", 15);
		pergunta.AddThemeColorOverride("font_color", Tema.Destaque);
		_corpo.AddChild(pergunta);

		foreach (EscolhaDoDragao c in e.Escolhas) _corpo.AddChild(CartaoDaResposta(c));
	}

	private PanelContainer CartaoDaResposta(EscolhaDoDragao c)
	{
		var conteudo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		conteudo.AddThemeConstantOverride("separation", 1);

		var rotulo = new Label { Name = "Nome", Text = c.Rotulo };
		rotulo.AddThemeFontSizeOverride("font_size", 15);
		rotulo.AddThemeColorOverride("font_color", !c.Pode ? Tema.TextoFraco : c.Marcado ? Tema.Destaque : Tema.Texto);
		conteudo.AddChild(rotulo);

		if (c.Nota.Length > 0)
		{
			var nota = new Label { Name = "Nota", Text = c.Nota, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			nota.AddThemeFontSizeOverride("font_size", 11);
			nota.AddThemeColorOverride("font_color", Tema.TextoFraco);
			conteudo.AddChild(nota);
		}

		StyleBoxFlat moldura = !c.Pode ? Tema.Caixa(Tema.PainelApagado, Tema.BordaApagada, 8)
							 : Tema.Caixa(Tema.PainelClaro, c.Marcado ? Tema.BordaViva : Tema.Borda, 8);
		PanelContainer cartao = Cartao(conteudo, moldura, c.Pode ? () => Responder(c) : null);
		cartao.SetMeta("resposta", c.Rotulo);
		cartao.SetMeta("pode", c.Pode);
		cartao.SetMeta("marcado", c.Marcado);
		return cartao;
	}

	// ---------------------------------------------------------------- 3. confirmar
	private void DesenharOConfirmar(MenuDoDragao m, EntradaDoDragao e)
	{
		EscolhaDoDragao? c = e.TemPergunta ? e.Escolhas.Find(x => x.Pedido == _resposta) : null;

		var chamada = new Label { Text = e.Acao ? "Confirmar:" : $"Pedir a {m.Nome}:" };
		chamada.AddThemeFontSizeOverride("font_size", 13);
		chamada.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_corpo.AddChild(chamada);

		var conteudo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		conteudo.AddThemeConstantOverride("separation", 2);

		var nome = new Label { Name = "Nome", Text = e.Nome, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		nome.AddThemeFontSizeOverride("font_size", 17);
		nome.AddThemeColorOverride("font_color", e.Perigo ? Tema.Perigo : Tema.Destaque);
		conteudo.AddChild(nome);

		if (c != null)
		{
			var quem = new Label
			{
				Name = "Quem", AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Text = c.Nota.Length > 0 ? $"{c.Rotulo}  —  {c.Nota}" : c.Rotulo,
			};
			quem.AddThemeFontSizeOverride("font_size", 14);
			conteudo.AddChild(quem);
		}

		if (e.Desc.Length > 0)
		{
			var desc = new Label { Text = e.Desc, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			desc.AddThemeFontSizeOverride("font_size", 12);
			desc.AddThemeColorOverride("font_color", Tema.TextoFraco);
			conteudo.AddChild(desc);
		}

		PanelContainer cartao = Cartao(conteudo, Tema.Caixa(Tema.PainelClaro, e.Perigo ? Tema.Perigo : Tema.BordaViva, 10), null);
		cartao.SetMeta("confirmar", e.Id);
		_corpo.AddChild(cartao);

		// QUANTO CUSTA. So o dragao comum conta pedidos; o Super atende um e as sete se vao, e a frase do alto
		// ja diz. Gesto nao gasta pedido nenhum.
		if (!e.Acao && m.Dragao == QualDragao.Comum && m.Total > 0)
		{
			var custo = new Label
			{
				Name = "Custo", AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Text = m.Restam <= 1
					? "É o último pedido desta invocação: concedido, o dragão vai embora e as sete se espalham."
					: $"Gasta 1 dos {m.Restam} pedidos que restam nesta invocação.",
			};
			custo.AddThemeFontSizeOverride("font_size", 12);
			custo.AddThemeColorOverride("font_color", Tema.Nutricao);
			_corpo.AddChild(custo);
		}

		if (e.Perigo)
		{
			var aviso = new Label { Text = "Não tem volta.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
			aviso.AddThemeFontSizeOverride("font_size", 12);
			aviso.AddThemeColorOverride("font_color", Tema.Perigo);
			_corpo.AddChild(aviso);
		}

		string verbo = e.Verbo, pedido = c?.Pedido ?? e.Pedido;
		Button pedir = Botao(e.Acao ? "CONFIRMAR" : "PEDIR");
		pedir.Name = "Pedir";
		pedir.AddThemeColorOverride("font_color", e.Perigo ? Tema.Perigo : Tema.Destaque);
		pedir.Pressed += () => Mandar(verbo, pedido);
		_corpo.AddChild(pedir);
	}

	/// <summary>
	/// UM CARTAO CLICAVEL: um painel no tema com o conteudo por cima de um botao invisivel -- o "card rico" da
	/// aba de skills (`MenuJogo.CartaoRico`), e pelo motivo escrito la: o texto de um `Button` tem uma cor e um
	/// tamanho so, e uma linha destas tem tres. O botao e `Flat` e so AVISA o mouse; quem acende e a moldura do
	/// painel, com a mesma chapa e a mesma borda laranja de todo botao do jogo (`Tema.CaixaSobOMouse`).
	///
	/// Sem `aoClicar` o cartao e so um quadro: a linha apagada nao acende nem responde.
	/// </summary>
	private static PanelContainer Cartao(Control conteudo, StyleBoxFlat moldura, Action? aoClicar)
	{
		var cartao = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		cartao.AddThemeStyleboxOverride("panel", moldura);

		if (aoClicar != null)
		{
			var b = new Button { Name = "Clique", Flat = true, FocusMode = Control.FocusModeEnum.None };
			b.Pressed += aoClicar;
			StyleBoxFlat sobOMouse = Tema.CaixaSobOMouse(moldura);
			b.MouseEntered += () => cartao.AddThemeStyleboxOverride("panel", sobOMouse);
			b.MouseExited += () => cartao.AddThemeStyleboxOverride("panel", moldura);
			cartao.AddChild(b);
		}

		IgnorarOMouse(conteudo);
		cartao.AddChild(conteudo);
		return cartao;
	}

	/// <summary>O conteudo de um cartao nao pode engolir o clique que e do botao embaixo dele.</summary>
	private static void IgnorarOMouse(Control c)
	{
		c.MouseFilter = Control.MouseFilterEnum.Ignore;
		foreach (Node f in c.GetChildren())
			if (f is Control filho) IgnorarOMouse(filho);
	}

	// =====================================================================
	// O QUE AS BANCADAS LEEM -- os NODES desenhados, e nao o menu que chegou
	// =====================================================================
	/// <summary>Em que etapa a janela esta: "Lista", "Resposta" ou "Confirmar".</summary>
	public string EtapaDeTeste => _etapa.ToString();

	public string NomeDesenhado => NaTela ? _nome.Text : "";
	public string ContagemDesenhada => NaTela && _contagem.Visible ? _contagem.Text : "";
	public string EstadoDesenhado => NaTela && _estado.Visible ? _estado.Text : "";

	/// <summary>O retangulo do painel na tela -- a bancada confere que ele nao tapa o meio, onde o dragao sobe.</summary>
	public Rect2 RetanguloDoPainel => _painel.GetGlobalRect();

	/// <summary>O retangulo do "Voltar" do rodape na tela, ou vazio quando ele nao existe (a janela fechada; a etapa da lista).</summary>
	public Rect2 RetanguloDoVoltar => NaTela && _voltar.Visible ? _voltar.GetGlobalRect() : default;

	private static string TextoDe(Node cartao, string nome) =>
		cartao.FindChild(nome, true, false) is Label l ? l.Text : "";

	private IEnumerable<PanelContainer> Cartoes(string meta) =>
		NaTela ? _corpo.GetChildren().OfType<PanelContainer>().Where(c => IsInstanceValid(c) && !c.IsQueuedForDeletion() && c.HasMeta(meta))
			   : [];

	/// <summary>As linhas da LISTA como estao desenhadas, de cima pra baixo: id, nome, se da pra clicar e o motivo escrito.</summary>
	public List<(string Id, string Nome, bool Pode, string Motivo)> LinhasDesenhadas() =>
		[.. Cartoes("linha").Select(c => (c.GetMeta("linha").AsString(), TextoDe(c, "Nome"),
										 c.FindChild("Clique", false, false) is Button, TextoDe(c, "Motivo")))];

	/// <summary>Os titulos de trecho da LISTA como estao escritos, de cima pra baixo ("ESCOLHA O SEU DESEJO", ...).</summary>
	public List<string> TitulosDesenhados() =>
		NaTela ? [.. _corpo.GetChildren().OfType<Label>().Where(l => !l.IsQueuedForDeletion()).Select(l => l.Text)] : [];

	/// <summary>Clica numa linha da lista pelo id. Falso quando ela nao esta na tela ou esta apagada.</summary>
	public bool ApertarLinha(string id)
	{
		foreach (PanelContainer c in Cartoes("linha"))
		{
			if (c.GetMeta("linha").AsString() != id || c.FindChild("Clique", false, false) is not Button b) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	/// <summary>A pergunta da segunda etapa como esta escrita ("Juventude pra quem?"), ou vazio fora dela.</summary>
	public string PerguntaDesenhada =>
		NaTela && _corpo.FindChild("Pergunta", false, false) is Label l ? l.Text : "";

	/// <summary>As respostas da segunda etapa como estao desenhadas: rotulo, nota, se da pra clicar e se e o alvo marcado.</summary>
	public List<(string Rotulo, string Nota, bool Pode, bool Marcado)> RespostasDesenhadas() =>
		[.. Cartoes("resposta").Select(c => (TextoDe(c, "Nome"), TextoDe(c, "Nota"),
											c.FindChild("Clique", false, false) is Button, c.GetMeta("marcado").AsBool()))];

	/// <summary>Clica numa resposta pelo rotulo. Falso quando ela nao esta na tela ou esta apagada.</summary>
	public bool ApertarResposta(string rotulo)
	{
		foreach (PanelContainer c in Cartoes("resposta"))
		{
			if (TextoDe(c, "Nome") != rotulo || c.FindChild("Clique", false, false) is not Button b) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	/// <summary>O que a etapa de confirmar diz que vai ser pedido: o nome da linha e pra quem.</summary>
	public (string Nome, string Quem, string Custo) ConfirmarDesenhado()
	{
		PanelContainer? c = Cartoes("confirmar").FirstOrDefault();
		return c == null ? ("", "", "")
			: (TextoDe(c, "Nome"), TextoDe(c, "Quem"), _corpo.FindChild("Custo", false, false) is Label l ? l.Text : "");
	}

	/// <summary>
	/// Aperta "Pedir" (o botao da etapa de confirmar) ou "Voltar" (o do rodape, que so existe nas etapas 2 e 3).
	/// Falso se o botao nao esta na tela.
	/// </summary>
	public bool ApertarBotao(string nome)
	{
		if (!NaTela) return false;
		Button? b = nome == "Voltar" ? (_voltar.Visible ? _voltar : null) : _corpo.FindChild(nome, false, false) as Button;
		if (b == null) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}
}
