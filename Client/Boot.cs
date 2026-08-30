using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// Entrada do jogo. Tres telas, nesta ordem:
///
///     LOGIN (servidor + conta + senha)  ->  SELECAO (3 slots)  ->  MUNDO
///
/// LOGIN POR SERVIDOR (decisao do dono): nao ha conta global. Em cada servidor voce tem um
/// perfil, e nele cabem tres personagens -- o modelo do Project Zomboid. Por isso a criacao
/// de personagem acontece DEPOIS de conectar: so o servidor sabe quais slots estao livres.
///
/// Toda a UI e feita com nodes do Godot, substituindo as abas HTML do BYOND.
/// </summary>
public partial class Boot : Node2D
{
	private LineEdit _conta = null!;
	private LineEdit _host = null!;
	private LineEdit _senha = null!;
	private CheckBox _lembrar = null!;
	private Label _status = null!;
	private Control _painel = null!;
	private VBoxContainer _listaPerfis = null!;

	private CharacterSelect? _selecao;
	private CreationScreen? _criacao;
	private int _slotAlvo = -1;

	private bool _auto;
	private string _autoNome = "Guerreiro", _autoRaca = "Human";

	/// <summary>As preferencias desta maquina. Uma so, lida por quem precisar.</summary>
	public static Settings Config { get; private set; } = new();

	// ============================ ESTAS DUAS TELAS VIVEM NO LOBBY **E** NO MUNDO ============================
	// Elas nasciam no `AoEntrarNoMundo` e por isso nao existiam antes de entrar -- o que deixava o
	// lobby sem tela de opcoes nenhuma (*"as vezes quero mudar o volume no lobby e n da"*). Agora
	// nascem aqui, atravessam a entrada no mundo e SOBREVIVEM a volta ao login (ver
	// `VoltarAoLogin`, que as poupa do `QueueFree`).
	//
	// Nao ha custo de mundo nelas: a `TelaDeTeclas` le duas fontes que ja saem vazias sem cliente
	// (`FormasDespertas.Minhas()` e `Verbos.Da`), e a `PauseMenu` se ajusta sozinha ao contexto.
	// ==========================================================================================
	private PauseMenu? _pause;
	private TelaDeTeclas? _teclas;

	public override void _Ready()
	{
		// ============================ O X DA JANELA PASSA A SER UMA PORTA NOSSA ============================
		// Com o `auto_accept_quit` ligado (o padrao) o fechamento da janela mata o processo sem
		// passar por nada: nao gravava o servidor local, nao avisava o servidor remoto. Desligado,
		// ele vira uma notificacao que o `_Notification` daqui atende. Ver `Saida.Encerrar`.
		//
		// **A CONTRAPARTIDA E SERIA E ESTA TRATADA LA**: enquanto isto for falso, uma excecao no
		// caminho de saida deixaria a janela sem X que funcione -- por isso o `Saida.Encerrar`
		// engole erro e sai de qualquer jeito.
		if (GetTree() is { } arv) arv.AutoAcceptQuit = false;

		try
		{
			string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (dll.Length > 0 && System.IO.File.Exists(dll))
				GD.Print($"[build] {System.IO.File.GetLastWriteTime(dll):dd/MM HH:mm:ss}  ({dll.GetFile()})");
		}
		catch (Exception e) { GD.Print($"[build] nao deu pra ler a data do binario: {e.Message}"); }

		// O CONFIG VEM ANTES DAS TECLAS, e a ordem inverteu de proposito: as teclas agora saem do
		// `Teclas`, que le a tabela de fabrica E o que o jogador religou -- e o que ele religou mora
		// no config. Registrar antes de carregar daria o padrao a todo mundo, sempre.
		Config = Settings.Carregar();
		Teclas.Aplicar(Config);
		Config.Aplicar();

		// processo servidor nao abre janela de login
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--server") >= 0)
		{
			GD.Print("[boot] modo servidor: sem interface");
			return;
		}

		// diagnostico das camadas de sprite, sem janela e sem rede
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvisual") >= 0)
		{
			AddChild(new VisualDiag { Name = "Diag" });
			return;
		}

		// QUEM A FUSAO E: nome, roupa, cabelo e o vermelho do SSJ4. Mora aqui em cima com as bancadas
		// sem mundo porque ela nao precisa de rede, de zona nem de login -- so do catalogo, das folhas
		// e do `CharacterVisual`. E ela NAO precisa de janela: o que ela le sao caminhos de arte e o
		// que o caminho de producao escreveu no material. Ver `RoboDeFusaoLook` pro que ela NAO prova.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagfusaolook") >= 0)
		{
			AddChild(new RoboDeFusaoLook { Name = "RoboDeFusaoLook" });
			return;
		}

		// A CINEMATICA DA FUSAO: a luz sobre os dois, as ondas, a pedra e o branco que escoa. Mora
		// aqui em cima ao lado da `--diagfusaolook` e pela mesma razao -- ela nao precisa de rede, de
		// zona nem de login, so do Core, das folhas e de dois `CharacterVisual`. E ela roda a cena no
		// PROPRIO relogio (`SetProcess(false)` + `_Process` a mao), entao tambem nao precisa de
		// janela. A metade do SERVIDOR (o `Fundir` que so roda na virada) e a `--cenafusaoteste`.
		// Ver `RoboDeCenaDeFusao`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagcenafusao") >= 0)
		{
			AddChild(new RoboDeCenaDeFusao { Name = "RoboDeCenaDeFusao" });
			return;
		}

		// A TINTA DO CABELO E DO RABO, medida na TELA. Vive aqui em cima com as outras bancadas sem
		// mundo porque ela nao precisa de rede nem de zona -- so da folha, do shader e de um quadro
		// desenhado. E ela PRECISA de janela: e a foto que responde.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagtinta") >= 0)
		{
			AddChild(new RoboDeTinta { Name = "RoboDeTinta" });
			return;
		}

		// A ARTE DOS ATAQUES DE KI. Mora aqui em cima, junto da `--diagtinta`, pela mesma razao:
		// arte de tiro nao precisa de rede, de zona nem de login -- so de folha, shader e um quadro
		// desenhado. As familias 1 e 2 (tabela e folhas do disco) rodam no HEADLESS; a familia 3
		// mede o pixel e precisa de janela. Ver RoboDeArteDeKi.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagartedeki") >= 0)
		{
			AddChild(new RoboDeArteDeKi { Name = "RoboDeArteDeKi" });
			return;
		}

		// A LUZ DOS ATAQUES DE KI -- irma da `--diagartedeki` e vizinha dela por isso: aquela mede o
		// que o tiro DESENHA, esta mede o que ele ACENDE. Familias 1 a 3 (mecanismo, luz orfa, teto)
		// rodam no headless; as 4 e 5 (custo por quadro e o pixel do chao) precisam de janela.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagluzdeki") >= 0)
		{
			AddChild(new RoboDeLuzDeKi { Name = "RoboDeLuzDeKi" });
			return;
		}

		// A GOTA NA TELA -- a ondulacao da entrada e da saida do transe. Ela e IRMA da `--diagluzdeki`
		// no argumento: as duas medem PIXEL porque as duas respondem perguntas que so o pixel
		// responde. Precisa de janela; no headless ela diz que nao mediu em vez de passar de graca.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diaggota") >= 0)
		{
			AddChild(new RoboDaGota { Name = "RoboDaGota" });
			return;
		}

		// diagnostico da SOMBRA: confere o leque contra o raycast, celula a celula
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvisao") >= 0)
		{
			AddChild(new VisaoDiag { Name = "DiagVisao" });
			return;
		}

		// O VAZIO DA SALA DO TEMPO (13.3): anda mil tiles pra fora do quarto desenhado e confere que
		// o chao continua vindo, que a colisao nao barra, que a volta do planeta nao dispara e que o
		// numero de pedacos vivos para de crescer. Vive aqui em cima com as outras bancadas sem
		// mundo: ela monta a cena do z13 na mao e nao precisa de rede nem de personagem.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvazio") >= 0)
		{
			AddChild(new RoboDeVazio { Name = "RoboDeVazio" });
			return;
		}

		// O INTERIOR DA CAPITAL SHIP: a metade que so o CLIENTE ve. As bancadas de servidor provam que
		// a planta existe e que o casco segura; esta prova que a sala e DESENHADA -- um `SetCell` num
		// tile que o tileset nao tem nao desenha e nao reclama. Vive aqui em cima com as outras
		// bancadas sem mundo: ela monta a cena na mao e nao precisa de rede nem de personagem.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagnave") >= 0)
		{
			AddChild(new RoboDeNave { Name = "RoboDeNave" });
			return;
		}

		// A VOZ, no lado que se OUVE: ida e volta pelo codec de producao, e a abafada da parede medida
		// em AMOSTRAS (energia em 3 kHz contra energia em 300 Hz). Vive aqui em cima com as outras
		// bancadas sem mundo -- ela nao precisa de rede, de personagem nem de microfone, e e por nao
		// precisar de microfone que ela pode rodar na maquina de qualquer um. O corte de alcance e do
		// servidor e se prova no `--vozteste`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvoz") >= 0)
		{
			AddChild(new RoboDeVoz { Name = "RoboDeVoz" });
			return;
		}

		// ============================ ESTA BANCADA VIVE ANTES DO MUNDO ============================
		// `--diagslot` dirige a TELA DE SELECAO -- criar, apagar, relogar. As outras bancadas sao
		// registradas em `AoEntrarNoMundo`, e la ela nunca rodaria: quando aquele metodo e chamado, a
		// tela de selecao ja passou. Foi exatamente o que aconteceu na primeira tentativa (nenhuma
		// linha de log). Aqui, junto das que tambem nao precisam de mundo.
		//
		// NAO montar o login: quem conecta e o robo, com conta e senha proprias.
		// =========================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagslot") >= 0)
		{
			AddChild(new RoboDeSlot { Name = "RoboDeSlot" });
			return;
		}

		// `--diagapagar` mede a TELA de apagar (a caixa de confirmacao): onde ela para na tela, o que
		// ela desenha por cima do que, e se a trava do nome e trava ou enfeite. Vive aqui pelo mesmo
		// motivo da `--diagslot` -- a tela que ela dirige some quando o mundo comeca.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagapagar") >= 0)
		{
			AddChild(new RoboDaTelaDeApagar { Name = "RoboDaTelaDeApagar" });
			return;
		}

		// `--diagki`: a METADE VIVA da bancada do sistema de ki -- conta nova pelo fio, tecnica
		// montada por verbo de rede, o cliente MENTINDO e o relogin. Anda junto do `--kideponta` do
		// servidor, que mede a outra metade e deixa o boneco de pe.
		//
		// AQUI EM CIMA, com a `--diagslot`, e pelo mesmo motivo dela mais um proprio: ela RELOGA, e
		// entrar pelo caminho normal montaria um `World` novo a cada volta. Ver `RoboDeKi`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagki") >= 0)
		{
			AddChild(new RoboDeKi { Name = "RoboDeKi" });
			return;
		}

		// AS OPCOES ANTES DA PRIMEIRA TELA. A ordem importa em duas pontas: os botoes do lobby
		// procuram a `PauseMenu.Instancia` na hora de montar, e a `PauseMenu` pergunta pela
		// `TelaDeTeclas.Instancia` pra decidir se o botao de teclas fica vivo.
		AddChild(_teclas = new TelaDeTeclas { Name = "Teclas" });
		_pause = new PauseMenu { Name = "Pause" };
		_pause.Desconectar += VoltarAoLogin;
		AddChild(_pause);

		MontarLogin();

		// MUSICA DESDE A PRIMEIRA TELA: no BYOND a criacao de personagem tinha trilha, e e
		// ela que volta toda vez que o menu de pause abre.
		AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu, "tela de login montada");

		if (GameClient.Instance is { } cli)
		{
			cli.SlotsRecebidos += AoReceberSlots;
			cli.Joined += (_, _, _, _) => AoEntrarNoMundo();
			cli.Rejected += motivo => _status.Text = $"recusado: {motivo}";
		}

		AutoConectar();

		// ============================ `--diagopcoes`: A BANCADA QUE NASCE **DEPOIS** DO LOBBY ============================
		// Ela e a unica que entra aqui embaixo, e nao la em cima com as outras, e o motivo e o
		// pedido dela: as outras bancadas SUBSTITUEM a tela de login (`return` antes do
		// `MontarLogin`), e esta precisa medir a tela de login DE VERDADE -- os botoes que o `Boot`
		// acabou de pendurar, a `PauseMenu` que ele acabou de criar, a trilha que ele acabou de
		// pedir. Nascer no lugar do lobby seria testar um lobby que nao e o do jogador.
		// ==========================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagopcoes") >= 0)
			AddChild(new RoboDeOpcoes { Name = "RoboDeOpcoes" });
	}

	/// <summary>
	/// O X DA JANELA (e o Alt+F4) SAO O MESMO GESTO QUE "Sair do jogo".
	///
	/// Nao havia handler nenhum de fechar janela no projeto: os dois so matavam o processo, sem
	/// gravar o servidor local e sem avisar o servidor remoto. Aqui eles entram no MESMO caminho do
	/// botao -- ver `Saida.Encerrar`, que termina em `Quit()` de qualquer jeito.
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest) Saida.Encerrar(GetTree(), "o X da janela");
	}

	// =====================================================================
	// TELA 1 -- LOGIN
	// =====================================================================
	/// <summary>
	/// ESTE PROCESSO E UMA SESSAO DE JOGADOR? Verdadeiro a partir do instante em que a tela de login
	/// e montada.
	///
	/// ============================ ELA EXISTE POR CAUSA DO X DA JANELA ============================
	/// O fechamento da janela passou a GRAVAR o servidor local antes de sair (ver `Saida.Encerrar`),
	/// e isso e certo pro jogo e **errado pras bancadas**: quase toda bancada deste projeto `return`
	/// antes desta funcao e mexe no mundo em memoria de proposito (corpos forjados, planetas mortos,
	/// cargos de mentira). Um X apertado no meio de uma delas gravaria esse estado por cima do mundo
	/// do dono.
	///
	/// A pergunta certa nao e "sou uma bancada?" -- isso seria adivinhar por prefixo de argumento --,
	/// e sim **"alguem chegou a jogar aqui?"**. Se a tela de login nunca apareceu, nao ha jogador, e
	/// nao ha nada de jogador pra salvar. A `--diagopcoes` e a unica bancada que nasce DEPOIS do
	/// login, e por isso ela atravessa o caminho de producao inteiro -- que e o que uma bancada de
	/// saida limpa tem que fazer.
	/// ==========================================================================================
	/// </summary>
	public static bool SessaoDeJogador { get; private set; }

	private void MontarLogin()
	{
		SessaoDeJogador = true;

		var camada = new CanvasLayer { Name = "LoginUI" };
		AddChild(camada);

		// FUNDO: sem ele a tela de login e texto solto sobre o cinza do motor, que e a cara
		// de projeto inacabado. Uma cor chapada da paleta ja muda tudo.
		var fundo = new ColorRect
		{
			Color = Tema.Fundo,
			AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		camada.AddChild(fundo);

		var centro = new CenterContainer
		{
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		Tema.Aplicar(centro);
		camada.AddChild(centro);
		_painel = centro;

		var colunas = new HBoxContainer();
		colunas.AddThemeConstantOverride("separation", 20);
		centro.AddChild(colunas);

		// --- formulario ---
		PanelContainer moldura = Tema.Painel1(20);
		colunas.AddChild(moldura);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		caixa.AddThemeConstantOverride("separation", 7);
		moldura.AddChild(caixa);

		var titulo = new Label { Text = "DRAGON BALL", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 30);
		titulo.AddThemeColorOverride("font_color", Tema.Texto);
		caixa.AddChild(titulo);

		var sub = new Label { Text = "J A N D I R U S", HorizontalAlignment = HorizontalAlignment.Center };
		sub.AddThemeFontSizeOverride("font_size", 17);
		sub.AddThemeColorOverride("font_color", Tema.Destaque);
		caixa.AddChild(sub);
		caixa.AddChild(new HSeparator());

		caixa.AddChild(Tema.Rotulo("Servidor"));
		_host = new LineEdit { Text = "127.0.0.1" };
		caixa.AddChild(_host);

		caixa.AddChild(Tema.Rotulo("Conta"));
		_conta = new LineEdit { MaxLength = 24, PlaceholderText = "seu perfil neste servidor" };
		caixa.AddChild(_conta);

		caixa.AddChild(Tema.Rotulo("Senha"));
		_senha = new LineEdit { Secret = true, MaxLength = 64 };
		caixa.AddChild(_senha);

		// Lembrar a SENHA e escolha separada de lembrar servidor+conta, porque so ela e
		// segredo: o arquivo de perfis fica em texto na pasta de dados do jogo.
		_lembrar = new CheckBox { Text = "lembrar a senha nesta maquina" };
		caixa.AddChild(_lembrar);

		var entrar = new Button { Text = "Entrar" };
		entrar.Pressed += Entrar;
		caixa.AddChild(entrar);

		// HOSPEDAR: sobe o servidor DENTRO deste processo e entra nele. E o mesmo servidor do
		// modo dedicado -- quem hospeda joga normalmente e os amigos conectam no IP dele.
		var hospedar = new Button { Text = "Hospedar partida (servidor local)" };
		hospedar.Pressed += Hospedar;
		caixa.AddChild(hospedar);

		// OPCOES E SAIR, a primeira das tres telas do lobby a receber a dupla. Ver `BotoesDoLobby`
		// pra por que ela e uma peca compartilhada e nao um botao aqui.
		caixa.AddChild(BotoesDoLobby.Montar(this));

		_status = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		caixa.AddChild(_status);

		// --- perfis salvos ---
		PanelContainer molduraLado = Tema.Painel1(16);
		colunas.AddChild(molduraLado);
		var lado = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
		lado.AddThemeConstantOverride("separation", 6);
		molduraLado.AddChild(lado);
		lado.AddChild(Tema.Rotulo("Servidores em que voce ja jogou"));

		_listaPerfis = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(260, 240),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		rolagem.AddChild(_listaPerfis);
		lado.AddChild(rolagem);

		RedesenharPerfis();
	}

	private void RedesenharPerfis()
	{
		foreach (Node n in _listaPerfis.GetChildren()) n.QueueFree();

		List<Perfil> perfis = Profiles.Carregar();
		if (perfis.Count == 0)
		{
			_listaPerfis.AddChild(new Label { Text = "  (nenhum ainda)" });
			return;
		}

		foreach (Perfil p in perfis)
		{
			var linha = new HBoxContainer();
			var b = new Button
			{
				Text = p.Rotulo,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				TooltipText = p.Senha.Length > 0 ? "senha lembrada" : "vai pedir a senha",
			};
			Perfil alvo = p;
			b.Pressed += () =>
			{
				_host.Text = alvo.Servidor;
				_conta.Text = alvo.Conta;
				_senha.Text = alvo.Senha;
				_lembrar.ButtonPressed = alvo.Senha.Length > 0;

				// PERFIL DE SERVIDOR PROPRIO: sobe o servidor antes de conectar. Sem isto o
				// jogador clica no proprio servidor e nao ha ninguem escutando.
				if (alvo.Hospedado) { Hospedar(); return; }

				if (alvo.Senha.Length > 0) Entrar();          // um clique so, como no Zomboid
				else _status.Text = "digite a senha";
			};
			linha.AddChild(b);

			var x = new Button { Text = "x", TooltipText = "esquecer este perfil" };
			x.Pressed += () => { Profiles.Esquecer(alvo); RedesenharPerfis(); };
			linha.AddChild(x);

			_listaPerfis.AddChild(linha);
		}
	}

	private void Hospedar()
	{
		if (Jandirus.Server.GameServer.Instance is not { } srv)
		{
			_status.Text = "servidor indisponivel";
			return;
		}

		// QUEM HOSPEDA E ADMIN, e o cliente nao precisa avisar nada disso: o servidor reconhece a
		// conexao pelo ENDERECO (ver `GameServer.EhHost`). Antes havia uma marca aqui, e ela
		// tornava o admin dependente de o jogo ter subido o servidor -- quem usava o `servidor.bat`
		// e entrava pelo IP da propria rede nunca era reconhecido, sendo a mesma maquina.
		if (!srv.Running && !srv.Start())
		{
			_status.Text = $"nao consegui abrir a porta {Jandirus.Net.Protocol.DefaultPort}";
			return;
		}

		_host.Text = "127.0.0.1";   // hospedar = jogar no proprio servidor
		_status.Text = "servidor no ar";
		_hospedando = true;
		Entrar();
	}

	/// <summary>Marcado quando esta entrada e num servidor que EU subi.</summary>
	private bool _hospedando;

	private void Entrar()
	{
		string conta = _conta.Text.Trim();
		string senha = _senha.Text;
		if (conta.Length < 2) { _status.Text = "escolha um nome de conta"; return; }
		if (senha.Length < 3) { _status.Text = "escolha uma senha de pelo menos 3 caracteres"; return; }

		_status.Text = "conectando...";
		Profiles.Lembrar(_host.Text.Trim(), conta, _lembrar.ButtonPressed ? senha : null, _hospedando);
		RedesenharPerfis();
		_hospedando = false;

		GameClient.Instance?.Conectar(_host.Text.Trim(), Jandirus.Net.Protocol.DefaultPort, conta, senha);
	}

	// =====================================================================
	// TELA 2 -- SELECAO DE PERSONAGEM
	// =====================================================================
	private void AoReceberSlots(List<Jandirus.Net.SlotInfo> slots)
	{
		if (_auto) { AutoEscolher(slots); return; }

		_painel.Visible = false;

		if (_selecao == null)
		{
			_selecao = new CharacterSelect { Name = "Selecao" };
			_selecao.Jogar += slot => GameClient.Instance?.PedirSlot(slot);
			_selecao.Criar += AbrirCriacao;
			_selecao.Sair += () =>
			{
				GameClient.Instance?.Desconectar();
				_selecao?.QueueFree();
				_selecao = null;
				_criacao?.QueueFree();
				_criacao = null;
				_painel.Visible = true;
				_status.Text = "";
			};
			AddChild(_selecao);
		}
		_selecao.Mostrar(slots);
		_selecao.Visible = true;

		// MONTA A CRIACAO AGORA, escondida. O jogador esta lendo a lista de slots -- e o
		// momento em que uns milissegundos nao custam nada. Ver CreationScreen.Reabrir().
		if (_criacao == null) CallDeferred(nameof(PrepararCriacao));
	}

	/// <summary>Monta a tela de criacao fora do caminho do clique.</summary>
	private void PrepararCriacao()
	{
		if (_criacao != null) return;
		_criacao = new CreationScreen { Name = "Criacao", Visible = false };
		_criacao.Pronto += (ficha, visual) =>
		{
			_status.Text = "criando personagem...";
			GameClient.Instance?.CriarPersonagem(_slotAlvo, ficha, visual);
		};
		_criacao.Cancelado += () => { if (_selecao != null) _selecao.Visible = true; };
		AddChild(_criacao);
	}

	/// <summary>Slot vazio: a criacao roda JA CONECTADO, e sabe em qual slot vai cair.</summary>
	private void AbrirCriacao(int slot)
	{
		_slotAlvo = slot;
		if (_selecao != null) _selecao.Visible = false;

		// se o clique chegou antes do CallDeferred (conexao muito rapida), monta aqui mesmo
		PrepararCriacao();
		_criacao!.Reabrir();
	}

	// =====================================================================
	// TELA 3 -- MUNDO
	// =====================================================================
	private void AoEntrarNoMundo()
	{
		_selecao?.QueueFree();
		_selecao = null;
		_criacao?.QueueFree();
		_criacao = null;
		if (_painel.GetParent() is { } pai) pai.QueueFree();   // some com a tela de login

		// O CHAT PRIMEIRO. As primeiras linhas de sistema saem durante o _Ready do World
		// ("bem-vindo", "voce chegou em X") -- montado depois, ele perderia justamente as
		// mensagens que explicam o que acabou de acontecer.
		AddChild(new Chat { Name = "Chat" });
		AddChild(new World { Name = "World" });
		AddChild(new Hud { Name = "Hud" });
		// O QUICK TIME EVENT DO EMBATE. Vive montado e invisivel: ele nasce de um pacote que chega
		// no meio de uma briga, e montar tela nessa hora e o jeito de perder o primeiro prazo.
		AddChild(new ClashQte { Name = "ClashQte" });
		AddChild(new MenuJogo { Name = "Menu" });
		// O CATALOGO DE CONSTRUCOES TAMBEM DO LADO DE CA. O servidor manda as OFERTAS (o que eu
		// posso comprar agora, com preco e motivo do nao), mas a mochila precisa de outra coisa: a
		// ficha de um item que eu JA TENHO -- nome, arte, descricao. Sem ela, uma maquina de
		// gravidade guardada seria um slot em branco.
		//
		// O arquivo ja viaja com o jogo, entao ler daqui nao expoe nada: ele e a lista do que
		// EXISTE, e nao a do que este personagem alcanca.
		const string cjObras = "res://Assets/Data/construcoes.json";
		if (Godot.FileAccess.FileExists(cjObras))
			Jandirus.Core.Items.CatalogoDeItens.Obras =
				Jandirus.Core.Tech.CatalogoDeObras.Parse(Godot.FileAccess.GetFileAsString(cjObras));

		// A TECLA E: uma porta so pra tudo com que se pode mexer no mundo. Ver `MenuDeInteracao`.
		AddChild(new MenuDeInteracao { Name = "Interacao" });
		// A TECLA I: a mochila.
		AddChild(new TelaDeInventario { Name = "Inventario" });
		// A TECLA M: meditar normal ou mergulhar na propria mente. Ver `TelaDeMeditacao` -- ela vive
		// montada e invisivel, e e o `LocalPlayer` que a abre (a atividade e dele).
		AddChild(new TelaDeMeditacao { Name = "Meditacao" });
		// A GRADE DA BANCADA e o fantasma de assentar construcao.
		AddChild(new TelaDeConstrucao { Name = "Construcao" });
		// A MESA ONDE O JOGADOR DESENHA AS PROPRIAS TECNICAS. Sem tecla propria de proposito: ela
		// abre pelo verb "Inventar tecnicas de ki", na aba Learning -- que e onde o
		// `Create_Attack`/`Customize_Attack` do original moram (`set category = "Learning"`).
		AddChild(new TelaDeTecnicas { Name = "Tecnicas" });
		// A TELA DE TROCA DE MAPA. Ver `TelaDeCarregamento`: ela nao acelera nada, ela ANUNCIA.
		AddChild(new TelaDeCarregamento { Name = "Carregando" });

		// ============================ A TELA DE TECLAS E A DE PAUSA JA EXISTEM -- ELAS SO VAO PRO FIM DA FILA ============================
		// As duas nascem no `_Ready`, no lobby (ver o campo `_pause`). O que se faz aqui e devolver a
		// ORDEM DE ENTRADA que elas tinham quando eram criadas neste ponto: `_UnhandledInput` corre
		// a arvore de tras pra frente, entao quem esta no fim da lista ouve a tecla primeiro.
		//
		// **ISTO NAO E ENFEITE.** Sem os dois `MoveChild` o menu de pausa passaria a ouvir o ESC
		// DEPOIS do `MenuDeInteracao` (que tambem le em `_UnhandledInput`), invertendo quem fecha o
		// que -- e a bancada `--diagmudez` cobra justamente que o ESC abra a pausa. A regra da casa
		// e nao mexer no que nao foi pedido; a ordem de entrada e exatamente isso.
		if (_teclas is { } tt) MoveChild(tt, -1);
		// O DISPARO das teclas que o jogador ligou. Nao desenha nada: le a tecla e chama a MESMA
		// acao que o botao do menu chamaria. Ver `Atalhos`.
		AddChild(new Atalhos { Name = "Atalhos" });
		if (_pause is { } pm) MoveChild(pm, -1);

		// a musica do lugar assume; o tema de menu sai de cena
		AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Menu, "entrei no mundo");

		// --treinar: comeca treinando. So serve pra teste headless (sem janela ninguem
		// aperta T) e pra medir o ritmo de ganho contra o banco de prova.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--treinar") >= 0)
			GameClient.Instance?.SendActivity(Jandirus.Net.Protocol.Activity.Treinando);

		// --socar: robo de teste que soca sem parar e narra o que o servidor devolve. E o
		// unico jeito de exercitar a CADEIA INTEIRA do combate sem janela: pacote de golpe ->
		// escolha de alvo -> resolucao -> transmissao -> relato. Dois processos com esta flag
		// no mesmo servidor brigam de verdade.
		// --diagmenu: bancada do menu -- abre, percorre as abas e mede quantas remontagens sobraram
		// depois do cache de paginas. Ver RoboDeMenu.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmenu") >= 0)
			AddChild(new RoboDeMenu { Name = "RoboDeMenu" });

		// --diagmuda: A PAREDE INVISIVEL, FOTOGRAFADA. O censo fecha em zero e a `--socoteste` mede a
		// recusa em numero -- e as duas ficariam verdes num mundo em que nada disso chega a tela, que e
		// justamente onde mora a queixa do dono. Roda duas vezes (com e sem `--semduro`) pra montar o
		// ANTES e o DEPOIS no mesmo binario, sem apagar arquivo nenhum. Ver RoboDeParedeMuda.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmuda") >= 0)
			AddChild(new RoboDeParedeMuda { Name = "RoboDeParedeMuda" });

		// --diagadmin: bancada de ADMIN. Confere que o bit chegou ao cliente, que a aba existe, que
		// os verbs respondem, que promover grava -- e, o principal, que APRENDER UMA SKILL nao apaga
		// o admin (o defeito que fazia o host nunca ver a aba). Ver RoboDeAdmin.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagadmin") >= 0)
			AddChild(new RoboDeAdmin { Name = "RoboDeAdmin" });

		// --diagnav: bancada da CARTA ESTELAR. Um mapa desenhado nao devolve nada -- esta bancada
		// mede o que o `_Draw` pinta: quantos planetas o enquadramento cobre, se os gerados entram
		// ao aproximar, quanto custa a varredura, e se clicar e viajar fazem o que prometem.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagnav") >= 0)
			AddChild(new RoboDeNav { Name = "RoboDeNav" });

		// --diagembarque: a bancada da TECLA E NAS NAVES. Irma da `--diagnav` e o oposto dela: aquela
		// mede o que SOBROU na aba (a carta), esta mede o que SAIU dela (os oito botoes de nave) e o
		// gesto que os substituiu -- chegar perto, apertar E, embarcar, achar a ponte, pilotar, voltar
		// e sair. Anda junto do `--embarqueteste` do servidor, que so entrega as fixtures. Ver
		// RoboDeEmbarque.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagembarque") >= 0)
			AddChild(new RoboDeEmbarque { Name = "RoboDeEmbarque" });

		// --diaginstalar: o CICLO fabricar -> mochila -> instalar, andado inteiro. Vizinha da
		// `--diagembarque` porque as duas medem um GESTO e nao um numero, e porque as duas passam
		// pelo mesmo `posicionar` do servidor. Pede o `--techteste` (nivel e dinheiro pra comprar).
		// Ver RoboDeInstalar -- e rode pelo `testar-instalar.bat`, que desvia a pasta de saves.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diaginstalar") >= 0)
		{
			var ri = new RoboDeInstalar { Name = "RoboDeInstalar" };
			// --instalaratraso <seg>: segura o comeco do roteiro pra o SEGUNDO CORPO
			// (`--olhoinstalar`, noutro processo) ter tempo de conectar e entrar na zona. Sem isso
			// ele perderia os primeiros marcos e a metade "os outros nao viram" ficaria verde por
			// ausencia. Ver `testar-instalar-dois.bat`.
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--instalaratraso"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segI))
				ri.Atraso = segI;
			AddChild(ri);
		}

		// --olhoinstalar: O SEGUNDO CORPO. Ele nao fabrica e nao clica -- ele OLHA, de outro
		// processo, com soquete e conta proprios. E a unica maneira de afirmar as duas metades que a
		// `--diaginstalar` so pode inferir de dentro: que ninguem mais ve a previa, e que depois do
		// clique todo mundo ve a obra. Ver RoboDeOlhoNoInstalar.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--olhoinstalar") >= 0)
			AddChild(new RoboDeOlhoNoInstalar { Name = "RoboDeOlhoNoInstalar" });

		// --fotoinstalar: A PREVIA FOTOGRAFADA. Precisa de JANELA (no headless o `GetImage` volta
		// vazio) e mede no PIXEL o que as outras medem em campo: que o fantasma e translucido de
		// verdade (a mistura com o chao de baixo, e nao um `Modulate` escrito) e que ele acompanha o
		// cursor. Ver RoboDeFotoDoInstalar.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--fotoinstalar") >= 0)
			AddChild(new RoboDeFotoDoInstalar { Name = "RoboDeFotoDoInstalar" });

		// --diaguniverso: a IRMA da `--diagnav`. Aquela mede a carta (o widget); esta mede o
		// UNIVERSO -- se as duas pontas enumeram o mesmo (assinatura pedida ao servidor PELO FIO),
		// se os sete pre-feitos continuam bit a bit onde estavam, se a faixa de 1 a 10 mundos e
		// verdade numa amostra grande, e se morrer no sol chega ate o cliente. Ver RoboDoUniverso.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diaguniverso") >= 0)
			AddChild(new RoboDoUniverso { Name = "RoboDoUniverso" });

		// --diagferida: bancada das FERIDAS. Confere a curva (roxo primeiro, sangue so no fim), que
		// as camadas certas recebem, e tira uma foto. Sobe junto do `--feridateste` do servidor,
		// que faz o corpo nascer com uma escada de estrago.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagferida") >= 0)
			AddChild(new RoboDeFerida { Name = "RoboDeFerida" });

		// --diagtintamundo: a irma da `--diagtinta` que roda DENTRO do jogo. A outra mede o cabelo e o
		// rabo numa cena de laboratorio; esta mede o boneco de verdade, com o ceu do planeta mandando na
		// luz, e e a unica que responde se o ambiente alcanca o personagem na arvore real.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagtintamundo") >= 0)
			AddChild(new RoboDeTintaNoMundo { Name = "RoboDeTintaNoMundo" });

		// --diagforma: bancada dos EFEITOS DE FORMA -- raiozinhos, contorno brilhoso e luz da aura.
		// Confere o que efeito visual esconde: shader que nao compilou, uniform que nao existe e
		// campo do catalogo que nunca chegou no node. Ver RoboDeForma.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagforma") >= 0)
			AddChild(new RoboDeForma { Name = "RoboDeForma" });

		// --diagchama: a FOTO da chama, que e a metade que a `--diagforma` nao alcanca. Aquela mede
		// o arquivo e o uniform (folha, quadro, RGB chapado, alfa identico ao do SSJ); esta segura a
		// tecla C no corpo de verdade e FOTOGRAFA -- a aura da base, a carga, o Ki acima de 100%, uma
		// forma que herda a base e o contra-exemplo de quem nao herda. Ela mede a foto contra um piso
		// de RUIDO tirado na hora, e nao no olho. Ver RoboDaChama.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagchama") >= 0)
			AddChild(new RoboDaChama { Name = "RoboDaChama" });

		// --diagsilencio: o SILENCIO DO ESPACO, ANDADO. A `--diagtrilha` liga e desliga o vacuo na
		// mao (responde "o corte funciona?"); esta faz o SERVIDOR mudar o corpo de zona -- planeta,
		// espaco, dentro da nave-capital, planeta de novo -- e pergunta ao `AudioServer` em cada
		// parada (responde "ao ENTRAR no espaco, o corte e pedido?"). Ver RoboDoSilencio.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagsilencio") >= 0)
			AddChild(new RoboDoSilencio { Name = "RoboDoSilencio" });

		// --diagcolada: bancada da COLADA DE FORMA, e ela so tira FOTO. As checagens de colada do
		// `--diagforma` (folha, tinta, pose, quadro) passaram verdes enquanto o dono via na tela um
		// brilho cinza em camera lenta -- entao esta monta uma TIRA de quadros consecutivos e deixa o
		// olho julgar cor e cadencia, que e o que numero nao responde. Ver RoboDeColada.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagcolada") >= 0)
			AddChild(new RoboDeColada { Name = "RoboDeColada" });

		// --diagolhada: a bancada que so OLHA -- cabelo e pupila, recortados na CABECA e ampliados.
		// Separada do `--diagcolada` porque aquela enquadra o corpo inteiro pra julgar CADENCIA de
		// animacao, e a 40 px de mundo o cabelo tem 10 px na foto: cabe julgar "tem brilho", nao cabe
		// julgar "este azul e marinho ou e ciano" -- que sao as tres reclamacoes de cor desta rodada.
		// Ver RoboDeOlhada.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagolhada") >= 0)
			AddChild(new RoboDeOlhada { Name = "RoboDeOlhada" });

		// --diagfera: a bancada da linha do Mistico, e ela so tira FOTO. Os cinco pedidos do dono desta
		// rodada (a chama do Mistico e a do Beast, o rabo branco, o olho vermelho e a faisca roxa) sao
		// todos "que cor esta na tela", e o `--diagforma` responde os cinco em NUMERO -- passando verde
		// enquanto a `FieryGod`, que nao se tinge, jogava fora a cor armada no node. O recorte aqui e o
		// CORPO com folga (a chama e maior que o boneco) e a tira tem oito quadros porque a faisca so
		// aparece em rajadas de 1,3 s a 2,7 s. Ver RoboDeFera.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagfera") >= 0)
			AddChild(new RoboDeFera { Name = "RoboDeFera" });

		// --diagmacaco: a foto de um NPC virando OOZARU -- antes, durante e depois. A bancada do
		// servidor (`--luaferateste`) prova que o gatilho DECIDE certo e nao olha a tela nenhuma vez;
		// esta responde a outra metade, que e a unica que o dono ve: o boneco alheio trocou de folha,
		// ficou maior e perdeu o cabelo? Ela nao escolhe o corpo -- quem diz qual e o `S2C.Oozaru`.
		// Sobe junto do `--macacovivo` do servidor. Ver RoboDeMacaco.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmacaco") >= 0)
			AddChild(new RoboDeMacaco { Name = "RoboDeMacaco" });

		// --diagbio: A ESCADA DO BIO-ANDROIDE EM RETRATO -- um por degrau, lado a lado. A bancada do
		// servidor (`--bioteste`, 159 provas) mede a escada inteira e **passaria verde com os quatro
		// degraus desenhando o mesmo boneco**: o caminho do desenho e outro, e a arte dos quatro
		// passou meses importada sem um consumidor. Esta responde a metade que o dono ve.
		// Sobe junto do `--biovivo` do servidor. Ver RoboDeBioRetrato.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagbio") >= 0)
			AddChild(new RoboDeBioRetrato { Name = "RoboDeBioRetrato" });

		// --diagolhar: OS TRES PEDIDOS VISUAIS do bio-androide -- os olhos da larva, o overlay que faz
		// o corpo brilhar na cinematica, e a morte que vira Super Saiyajin 2. O `--diagbio` fotografa
		// um degrau por vez e **nao alcanca nenhum dos tres**: os olhos sao 4 px (abaixo do piso de 3%
		// dele), a cinematica ele existe pra pular, e a morte precisa de dois corpos no mesmo quadro.
		// Aqui a medida e outra -- injeta-se o defeito e fotografa-se de novo tres quadros depois.
		// Sobe junto do `--bioolhar` do servidor. Ver RoboDeOlharDoBio.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagolhar") >= 0)
			AddChild(new RoboDeOlharDoBio { Name = "RoboDeOlharDoBio" });

		// --diagfilme: A CINEMATICA QUADRO A QUADRO. As tres bancadas de bio acima tiram UMA foto por
		// estado, e o defeito que o dono fotografou (*"ta MUDANDO O CORPO ANTES DA CINEMATICA ACABAR"*)
		// e uma afirmacao sobre ORDEM -- que nao cabe num quadro. Esta filma ~30 amostras por cena e
		// procura o INSTANTE da troca do desenho do corpo. Roda em DUAS racas (o bio, que troca pela
		// ficha na rede, e o Oozaru, que troca pelo catalogo no cliente) pra a regra nao ser um `if` de
		// bio, e a quarta cena roda com o defeito INJETADO, que ela exige ver reprovar.
		// Sobe junto do `--biofilme` do servidor. Ver RoboDeFilmeDoBio.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagfilme") >= 0)
			AddChild(new RoboDeFilmeDoBio { Name = "RoboDeFilmeDoBio" });

		// --diagvestido: a foto do POVO do planeta, vestido. A `--npcteste` ja tranca a regra (a
		// tabela do DM, o funil do jogador, a roupa como funcao pura da semente) -- esta responde a
		// outra metade, que e a unica que o dono ve: o vizinho aparece VESTIDO na tela, e da pra
		// dizer a raca dele pela roupa? A raca do jogador escolhe o planeta pelo berco. Ver
		// RoboDeVestido.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvestido") >= 0)
			AddChild(new RoboDeVestido { Name = "RoboDeVestido" });

		// --diagcena: a bancada que RODA a cinematica inteira, no relogio da engine, e fotografa ao
		// longo dela. O `--diagforma` ja tranca o ROTEIRO (uma cratera, no beat que assume, sem poeira
		// antes) -- esta responde a outra metade, que e a unica que o dono ve: o buraco APARECEU na
		// tela no fim? caiu raio do comeco ao fim? ha pedra em toda foto? sobrou quadrado marrom?
		// Ver RoboDeCena.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagcena") >= 0)
			AddChild(new RoboDeCena { Name = "RoboDeCena" });

		// --diagnebulosa: bancada da NUVEM DE GALAXIA do Ultra Instinto. Separada do `--diagforma` de
		// proposito: aquela mede catalogo e texto (e o `Posar` dela pinta o node direto, sem rede),
		// esta veste a forma PELA REDE e tira a foto. Ver RoboDeNebulosa.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagnebulosa") >= 0)
			AddChild(new RoboDeNebulosa { Name = "RoboDeNebulosa" });

		// --diagmostrador: a bancada que FOTOGRAFA a HUD. Os quatro pedidos desta rodada (a barra de
		// Ki acima de 100%, a barra de nutricao, a % de BP efetivo e o multiplicador total) sao todos
		// "o que aparece na tela que o dono olha o tempo todo", e o `--diagnebulosa` responde o
		// primeiro em NUMERO. Numero certo com desenho errado ja aconteceu quatro vezes nesta casa:
		// aqui cada afirmacao sai com a foto do quadro que a sustenta -- e a foto 2 pega HUD e menu P
		// no MESMO quadro, que e literalmente a queixa ("as duas telas discordam"). Ver RoboDeMostrador.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmostrador") >= 0)
			AddChild(new RoboDeMostrador { Name = "RoboDeMostrador" });

		// --diagbancada: a BANCADA do mostrador. Onde a `--diagmostrador` FOTOGRAFA pra o olho julgar,
		// esta MEDE -- e mede as duas telas uma contra a outra, pelo TEXTO que cada uma desenhou
		// (`Barra.TextoDeTeste` e `MenuJogo.ValorDesenhado`), que e a unica leitura capaz de separar
		// "as duas telas concordam" de "as duas telas leem o mesmo campo". A ficha entra como terceira
		// opiniao, nunca como as duas.
		//
		// E ela prova que sabe reprovar: depois da rodada real, injeta 22 defeitos conhecidos nas
		// amostras -- o corte em 100% num widget `Barra` de VERDADE, a aba congelada, a % que enxerga a
		// forma, o multiplicador por produto ingenuo, o BP vazando sem scouter -- e exige que as regras
		// nomeadas fiquem vermelhas. Ver RoboDeBancada.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagbancada") >= 0)
			AddChild(new RoboDeBancada { Name = "RoboDeBancada" });

		// --diagbalao: bancada do BALAO DE FALA sobre a cabeca. Confere o portao de canal (OOC nao
		// e voz de personagem), a busca do corpo pelo NOME que vem no pacote, a quebra de linha, a
		// substituicao com piso de leitura, e as duas coisas que so aparecem com o corpo no ar ou
		// em cena: a subida junto de quem voa e a fala de cinematica alheia. Ver RoboDeBalao.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagbalao") >= 0)
			AddChild(new RoboDeBalao { Name = "RoboDeBalao" });

		// --diagtecla: bancada das TECLAS CONFIGURAVEIS. Ela mede uma afirmacao ("a tecla faz
		// exatamente o que o botao faz") comparando os BYTES que chegaram no servidor pelos dois
		// gestos, e um contra-exemplo (o C, o ALT e o E continuam funcionando depois de o registro
		// unificar as teclas). Ver RoboDeTecla -- e leia o aviso sobre o `config.json` no cabecalho
		// dela antes de rodar: e o arquivo desta MAQUINA que ela mexe, e devolve no fim.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagtecla") >= 0)
			AddChild(new RoboDeTecla { Name = "RoboDeTecla" });

		// --diagmudez: a bancada da MUDEZ DOS ATALHOS -- o pedido do dono de que nenhuma tecla de
		// jogador dispare durante um embate. Ela aperta as DEZESSETE, uma linha cada, dentro dos DOIS
		// embates e fora deles (o contra-exemplo), e confere que a letra do quick time event e o
		// movimento continuam vivos. Sobe junto do `--mudezteste` do servidor, que e quem poe os
		// embates de verdade de pe. Ver RoboDeMudez.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmudez") >= 0)
			AddChild(new RoboDeMudez { Name = "RoboDeMudez" });

		// --porta: bancada AO VIVO das portas. Anda contra a porta mais proxima e narra o que
		// mede -- fechada bloqueia e cega, abriu ao encostar, atravessou, fechou sozinha. Sobe
		// junto do `--portateste` no servidor, que faz nascer colado numa.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--porta") >= 0)
			AddChild(new RoboDePorta { Name = "RoboDePorta" });

		// --diagclash: bancada do ZANZO CLASH. Ela MEDE, mas nao briga -- o embate so comeca com
		// dois lutadores se acertando ao mesmo tempo, entao ela vem acompanhada do robo de soco
		// (aqui e do outro lado). Ver RoboDeEmbate pro comando das duas pontas.
		// --diagpoeira: bancada da POEIRA. Vem com `--quebrarteste N` no servidor, e com N MAIOR que
		// o teto de efeitos vivos -- e o unico jeito de provar que o teto dispara.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagpoeira") >= 0)
			AddChild(new RoboDePoeira { Name = "RoboDePoeira" });

		// --diagdecalque: bancada dos DECALQUES DE CHAO. Vem com `--quebrarteste N` no servidor, que
		// derruba cenario -- e e a queda que pinta a terra revirada em volta.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagdecalque") >= 0)
			AddChild(new RoboDeDecalque { Name = "RoboDeDecalque" });

		// --diagmembroperdido: a bancada que FOTOGRAFA a amputacao em combate. A `--diagdecalque`
		// acima prova a maquina (a folha carrega, as dez pecas acham o recorte); esta prova o
		// PIXEL, e por isso ela precisa de JANELA, de golpe LETAL e de duas vitimas de verdade --
		// ela nao chama efeito nenhum, ela espera o `HitEvent` de uma briga. Anda junto de um
		// `--socar` no MESMO processo (ele bate; ela so escolhe alvo e mira). Ver
		// RoboDeMembroPerdido e `testar-membro-perdido.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmembroperdido") >= 0)
		{
			// `--membrofase N`: comeca na fase N (0 = bracos, 1 = pernas). Ver
			// RoboDeMembroPerdido.FaseInicial -- existe porque as duas vitimas podem nascer longe
			// uma da outra, e a fase B sozinha ainda responde o roteiro do dono.
			_ = int.TryParse(Arg(OS.GetCmdlineArgs(), "--membrofase"), out int faseM);
			AddChild(new RoboDeMembroPerdido { Name = "RoboDeMembroPerdido", FaseInicial = faseM });
		}

		// --diagrastro: a bancada que FOTOGRAFA os quatro sentidos do rastro da agua. Vem com
		// `--aguanoar` e `--vooteste` no servidor (o corpo nasce no ar sobre o meio do lago). A
		// `--diagdecalque` ja le o RECORTE que a onda recebeu; so a foto responde se o recorte
		// certo esta desenhado certo -- que e a queixa que o dono mandou EM FOTO. Ver
		// RoboDeRastroDaAgua e `testar-rastro.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagrastro") >= 0)
			AddChild(new RoboDeRastroDaAgua { Name = "RoboDeRastroDaAgua" });

		// --diagraio: as TRES FOTOS dos tres pedidos desta rodada -- o NPC que nao anda na
		// cinematica (com o rastro de posicao ao lado, nos tres momentos), o raio cruzando a margem
		// de um lago (sulco no seco, onda no molhado) e o corpo LEVADO pelo feixe em tres quadros.
		// Vem com `--aguateste` no servidor (o corpo nasce na beira do lago, no seco e virado pra
		// agua) e PRECISA de janela: no headless o `GetImage` volta vazio. As bancadas sem foto
		// (`--projetilteste` 8/9/10, `--iateste` 7b/7c/7d, `--diagdecalque`) ja medem estes mesmos
		// tres pedidos em numero e em byte; esta responde so a metade que o dono ve. Ver
		// RoboDeFotoDoRaio.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagraio") >= 0)
			AddChild(new RoboDeFotoDoRaio { Name = "RoboDeFotoDoRaio" });

		// --diagembateki: AS FOTOS DA COLISAO DE KI -- os dois feixes se empurrando (o feixe de quem
		// acerta ESTICA, o do outro ENCOLHE) e a explosao do empate de 15 s, com os dois corpos sendo
		// jogados pra tras. Irma da `--embatekiteste` (87 afirmacoes, sem janela) e a metade que aquela
		// nao pode ter: entre o `Feixe.Pos` do servidor e o feixe desenhado ha o snapshot, o
		// `ProjetilDesenhado` e a interpolacao, e a bancada de numero ficaria verde com os dois feixes
		// desenhados do mesmo tamanho. Precisa de `--host` (quem tem projetil e disputa e o servidor) e
		// de JANELA (no headless o `GetImage` volta vazio). Ver RoboDeFotoDoEmbateDeKi e
		// `testar-colisao-de-ki.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagembateki") >= 0)
			AddChild(new RoboDeFotoDoEmbateDeKi { Name = "RoboDeFotoDoEmbateDeKi" });

		// --diagboca: DE ONDE O FEIXE SAI E EM QUE CAMADA ELE E DESENHADO, fotografado. E a resposta
		// da queixa que o dono mandou EM FOTO ("os beams tao saindo DE CIMA do personagem, deveriam
		// sair DA FRENTE dele, NA FRENTE DO SPRITE deles"), e ela mede as DUAS leituras da frase: o
		// ponto de nascimento e a ordem de desenho. A familia 1-bis da `--projetilteste` ja mede a
		// primeira em numero -- e le o `Pos` do SERVIDOR, entao nao ve camada, nao ve altura e nao ve
		// a cauda do raio canalizado, que sao as tres coisas que aparecem so na tela. O par de fotos
		// e tirado com a ARVORE PAUSADA (a tela com o tiro, e a mesma tela com o node do tiro
		// escondido), entao a mascara e exatamente o que aquele tiro pintou -- e nao o boneco trocando
		// de pose. Precisa de `--host`, de JANELA e de `--horateste 0.5` (de dia a `LuzDeKi` nao
		// acende, e luz entraria na mascara como tinta). Ver RoboDeBocaDeCano e `testar-boca.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagboca") >= 0)
			AddChild(new RoboDeBocaDeCano { Name = "RoboDeBocaDeCano" });

		// --diagpose: A POSE DO CORPO COM O RAIO NA MAO, fotografada. Irma da `--diagraio` acima e a
		// outra metade dela: aquela fotografa o que o TIRO faz (rastro, agua, quem ele leva) e o
		// corpo dela nem entra no canal -- ela dispara pelo `Disparar` direto. Esta fotografa o
		// ATIRADOR: os quadros de um tiro de verdade, a pose durando o canal inteiro, as tres saidas,
		// a direcao, e o corpo de um NPC. A familia 4b da `--projetilteste` ja mede tudo isso em
		// NUMERO e **passaria verde com a folha desenhando o mesmo boneco nas duas poses** -- entre a
		// `Pose.Canalizando` e o pixel ha o fio, o `World`, o `LocalPlayer` e a escada do `Escolher`.
		// Precisa de `--host` (quem tem canal e o servidor) e de JANELA (no headless o `GetImage`
		// volta vazio). Ver RoboDeFotoDaPose e `testar-pose-do-raio.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagpose") >= 0)
			AddChild(new RoboDeFotoDaPose { Name = "RoboDeFotoDaPose" });

		// --diagcorpos: OS TRES PEDIDOS DO DONO, FOTOGRAFADOS -- dois corpos colidindo, alguem
		// sendo CARREGADO no ar, e o cadaver no chao antes e depois do enterro. A irma dela e a
		// `--doiscorposteste` (servidor, headless, 236 provas com oito defeitos injetados), e esta
		// e a metade que aquela nao pode ter: entre a caixa dos pes do servidor e o pixel ha o
		// snapshot, o `World`, a interpolacao do corpo remoto e o Y-sort -- e aquela ficaria verde
		// com o corpo desenhado atravessando o outro na tela. Precisa de `--host` (quem tem colisao
		// e o servidor) e de JANELA (no headless o `GetImage` volta vazio). Ver RoboDeColisao e
		// `ver-dois-corpos.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagcorpos") >= 0)
			AddChild(new RoboDeColisao { Name = "RoboDeColisao" });

		// --diagfotofusao: A FUSAO, FOTOGRAFADA -- a UNICA `FusionLight` no meio dos dois corpos (e a
		// janela LIMPA entre dois estouros dela), o corpo completamente branco no climax, a METAMORO e a
		// POTARA lado a lado (roupa e cabelo diferem) e o SSJ4 de cabelo vermelho. A irma dela e a
		// `--fusaoduplateste` (servidor, headless, 157 provas com defeitos injetados), e esta e a metade
		// que aquela nao pode ter: aquela ficaria
		// verde com a fusao desenhada careca e de calcao -- entre o `LookDeFusao` do servidor e o pixel
		// ha o `PeerLook`, o `_fusaoDaZona` do `World`, a pilha de camadas do `CharacterVisual`, o
		// `CabelosDeForma` e o shader do corpo. Precisa de `--host` (quem funde e o servidor) e de
		// JANELA (no headless o `GetImage` volta vazio). Ver RoboDeFotoDeFusao,
		// `GameServer.FotoDaFusao.cs` e `ver-a-fusao.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagfotofusao") >= 0)
			AddChild(new RoboDeFotoDeFusao { Name = "RoboDeFotoDeFusao" });

		// --diagborrao: O BORRAO DO DASH, FOTOGRAFADO -- os dois relatos do dono sobre o dash do NPC
		// ("npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores" e "o RANGE DO
		// TELEPORTE DO DASH ta mt grande"), medidos em PIXEL. A irma dela e a `--borraoteste`
		// (servidor, headless, 41 provas com sete defeitos injetados), e esta e a metade que aquela
		// nao pode ter: tudo o que aquela ve e estado de servidor, e ela ficaria verde com o borrao
		// nunca desenhado na tela -- entre o `S2C.Zanzo` e o pixel ha o fio, o `World.AoPiscar`, a
		// escolha da origem no `LocalPlayer` e o `RastroDeCorrida` esperando a posicao de CHEGADA.
		// Fotografa o NPC e o jogador arrancando NO MESMO QUADRO, com um terceiro corpo parado na
		// faixa de baixo como contra-exemplo dentro da mesma foto. Precisa de `--host` (quem manda o
		// NPC arrancar e o servidor) e de JANELA (no headless o `GetImage` volta vazio). Ver
		// RoboDeBorrao e `ver-o-borrao.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagborrao") >= 0)
			AddChild(new RoboDeBorrao { Name = "RoboDeBorrao" });

		// --diagvariedade: a VARIEDADE dos ataques de ki, fotografada com o tiro SAINDO DA MAO. A
		// `--diagartedeki` (la em cima, sem rede) monta os `ProjetilDesenhado` com a mao e ja prova a
		// tabela, as folhas e o pixel; esta atravessa o caminho inteiro que aquela pula -- o verb, o
		// gate da skill, o `Canalizar`/`Disparar`, o `ushort` do anuncio de nascimento e o
		// `AoNascerTiro` -- disparando uma tecnica por folha do catalogo pelo MESMO `UsarHabilidade`
		// do jogador, e comparando os recortes todos contra todos. Precisa de `--host` (quem tem
		// projetil e o servidor) e de JANELA (no headless o `GetImage` volta vazio). Ver
		// RoboDeVariedadeDeKi.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvariedade") >= 0)
			AddChild(new RoboDeVariedadeDeKi { Name = "RoboDeVariedadeDeKi" });

		// --diagvoo: bancada do VOO. Vem com `--vooteste` no servidor, que da a skill de voo (sem
		// ela nao ha o que medir) e tira o freeflight do admin (sem isso o custo mediria zero).
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvoo") >= 0)
			AddChild(new RoboDeVoo { Name = "RoboDeVoo" });

		// --diagagua: bancada da AGUA, a que OLHA. Anda contra o lago a pe, nada por cima dele, voa
		// por cima do MESMO ponto e fotografa os tres -- mais o soco na agua e o vizinho da outra
		// margem. Vem com `--aguateste` no servidor (que poe o corpo na beira e o vizinho do outro
		// lado) e precisa de JANELA: headless nao renderiza e as fotos saem vazias.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagagua") >= 0)
			AddChild(new RoboDeAgua { Name = "RoboDeAgua" });

		// --fotodamente: a FOTO da dimensao mental (o relato A do dono). Medita, mergulha e salva o
		// pixel -- com a cor media e a fracao de branco medidas no relatorio, pra a prova nao depender
		// de alguem abrir a pasta. Precisa de JANELA (headless nao renderiza).
		//
		// RODE DUAS VEZES: a segunda com `--menteantiga` nas DUAS pontas, que devolve o jogo ao estado
		// em que o dono o encontrou (o z24 do BYOND). Uma foto de um quarto branco nao prova que ele
		// mudou; o par prova. Ver RoboDaMente.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--fotodamente") >= 0)
			AddChild(new RoboDaMente { Name = "RoboDaMente" });

		// --diagmergulho: O MERGULHO INTEIRO, PELO GESTO DO JOGADOR -- os quatro pedidos desta rodada
		// (a telinha da tecla M, a gota na ida, a gota na volta por vitoria mas NAO no soco, e a mente
		// sem borda por pedaco) num caminho so. Ela e o meio que faltava entre as quatro bancadas que
		// ja existiam: a `--presoteste` mede a planta sem ninguem meditando, a `--menteviva` atravessa
		// a onda de proposito, a `--diaggota` chama o shader na mao sem rede e a `--fotodamente` para
		// na foto. Precisa de `--host` (as familias perguntam a AUTORIDADE) e de JANELA (as de pixel
		// dizem que nao mediram no headless, em vez de passar de graca).
		//
		// `--mergulhofamilia N`: roda UMA familia so. Existe pras duas injecoes que sao de FONTE (a
		// coleira e a folga de descarte do pintor) -- com ela, uma rodada de defeito custa meio minuto
		// em vez de quatro. Ver RoboDoMergulho e `ver-o-mergulho.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagmergulho") >= 0)
		{
			_ = int.TryParse(Arg(OS.GetCmdlineArgs(), "--mergulhofamilia"), out int familiaDoMergulho);
			AddChild(new RoboDoMergulho { Name = "RoboDoMergulho", SoAFamilia = familiaDoMergulho });
		}

		// --diagvolta: bancada da VOLTA DO PLANETA. Anda ate a beirada e confere que sai pela outra.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvolta") >= 0)
			AddChild(new RoboDeVolta { Name = "RoboDeVolta" });

		// --diagtrilha: bancada da TRILHA. Roda os roteiros do dono no jogo -- olhar o tema do lugar,
		// bater, deixar a tag cair, transformar, transformar DENTRO da briga, deixar a tag cair com o
		// ESC aberto -- e escreve o diario de trocas de faixa. E a unica que exercita o FIM NATURAL de
		// uma faixa (o `Finished` do Godot), que e o caminho de onde a queixa da musica de menu em
		// laco nasceu. Rode-a DUAS vezes, com `--raca Saiyan` e com `--raca Demon`: a segunda nasce no
		// Inferno, a unica zona com tema de lugar, e e o par das duas que prova de quem e a camada de
		// baixo. Ver RoboDeTrilha.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagtrilha") >= 0)
			AddChild(new RoboDeTrilha { Name = "RoboDeTrilha" });

		// --diagceu: bancada do CEU E DA LUA. Confere que a hora vem do servidor, que cada planeta
		// corre o proprio dia e que a fase da lua vira no ANOITECER (e nao no meio da noite). Anda
		// junto do `--luateste` no servidor -- sem ele a lua cheia so volta a cada tres horas.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagceu") >= 0)
			AddChild(new RoboDeLua { Name = "RoboDeLua" });

		// --diagclima: bancada do CLIMA. Confere que cada planeta sorteia da lista DELE (a do DM),
		// que as duas pontas chegam ao mesmo ceu sem trocar byte, que o custo das particulas e
		// fixo e que a nuvem apaga a lua. Anda junto do `--climateste <tipo>` no servidor.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagclima") >= 0)
			AddChild(new RoboDeClima { Name = "RoboDeClima" });

		// --dois <a|b>: bancada de DOIS PROCESSOS. `a` se transforma, `b` olha o corpo alheio e
		// fotografa. Os tres defeitos de corpo REMOTO (forma que nao chega a quem entrou depois,
		// contorno aceso sem Ki, aura solta) nao aparecem com um cliente so -- as bancadas de um
		// processo forjam fantasmas e chamam os handlers na mao, o que prova a funcao e nao a
		// corrida entre o canal confiavel e o snapshot. Ver RoboDeDoisCorpos.
		if (Arg(OS.GetCmdlineArgs(), "--dois") is { } papel)
		{
			var rd = new RoboDeDoisCorpos
			{
				Name = "RoboDeDoisCorpos",
				Papel = papel,
				Forma = Arg(OS.GetCmdlineArgs(), "--doisforma") ?? "",
				Rotulo = Arg(OS.GetCmdlineArgs(), "--doisrotulo") ?? papel,
				Conta = Arg(OS.GetCmdlineArgs(), "--conta") ?? "",
			};
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--doisatraso"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segD))
				rd.Atraso = segD;
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--doisvoar"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segV))
				rd.Voar = segV;
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--doiscarga"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segC))
				rd.Carga = segC;
			// `--doissoltar`: o segundo em que o A larga o C. E a VOLTA da regra do contorno -- sem
			// soltar, a bancada mede o interruptor acendendo e nunca desligando com a forma no corpo.
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--doissoltar"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segS))
				rd.Soltar = segS;
			// `--doisficha`: o segundo em que a FICHA volta a passar por cima da forma. No A e a hora do
			// `admin_ir` (que faz o servidor reemitir o `PeerLook`); no B e a expectativa de que ele
			// chegue num corpo que ja existe. Vai nos DOIS processos -- ver `RoboDeDoisCorpos.Ficha`.
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--doisficha"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segF))
				rd.Ficha = segF;
			if (int.TryParse(Arg(OS.GetCmdlineArgs(), "--doisfim"), out int nFim)) rd.Fim = nFim;
			AddChild(rd);
		}

		// --vida <a|b>: bancada do SIGILO DA VIDA ALHEIA, tambem de DOIS PROCESSOS. O `a` apanha, se
		// cura no meio da rodada e mede a PROPRIA vida (o contra-exemplo); o `b` olha aquele mesmo
		// corpo e prova que o NUMERO nao chegou nele -- so o GRAU de ferida. Nao cabe num processo
		// so pela mesma razao do `--dois`: a pergunta e sobre o que UM cliente sabe do corpo do
		// OUTRO, e com um processo os dois lados sao a mesma memoria.
		//
		// EXIGE `--feridateste` no servidor: a amputacao do nascimento e o caso pronto da familia do
		// membro arrancado (decepar de verdade depende de golpe letal em membro ja zerado, e bancada
		// que so as vezes arranca um braco nao mede nada nas outras vezes). Ver RoboDeSigiloDeVida.
		if (Arg(OS.GetCmdlineArgs(), "--vida") is { } papelDaVida)
		{
			var rv = new RoboDeSigiloDeVida
			{
				Name = "RoboDeSigiloDeVida",
				Papel = papelDaVida,
				// O NOME DO OUTRO, e nao "o primeiro do snapshot": o berco tem NPC (ver
				// `World.IdPeloNome`). Vai nos DOIS processos, com o nome trocado.
				Alvo = Arg(OS.GetCmdlineArgs(), "--vidaalvo") ?? "",
				Conta = Arg(OS.GetCmdlineArgs(), "--conta") ?? "",
			};
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--vidafim"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segVida))
				rv.Fim = segVida;
			AddChild(rv);
		}

		// --vista <a|b>: a VISTA POR ALTURA, com DUAS TELAS. O pedido do dono -- *"pessoas mt abaixo
		// da sua altura N CONSEGUIRIAM TE VER, mas pessoas em ALTURAS MAIORES q vc CONSEGUEM TE
		// VER"* -- e uma regra ASSIMETRICA, e assimetria so se prova comparando duas telas no mesmo
		// instante: numa o corpo esta, na outra nao. Num processo so os dois lados sao a mesma
		// memoria, e o `--diagvoo` (que ja cobre a regra pura) ficaria verde mesmo se o `World`
		// nunca chamasse `Voo.Enxerga` -- que foi exatamente o que aconteceu por meses.
		//
		// A tabela de expectativa dela e escrita A MAO e NAO chama `Voo.Enxerga`: comparar a tela
		// com a funcao que se julga fica verde com as duas erradas igual. Ver RoboDeVista e
		// `testar-vista.bat`.
		//
		// EXIGE `--vooteste` (a skill) e `--bpteste` (o tanque de Ki que segura o corpo no ar
		// durante a rodada inteira) no servidor.
		if (Arg(OS.GetCmdlineArgs(), "--vista") is { } papelDaVista)
		{
			var rvi = new RoboDeVista
			{
				Name = "RoboDeVista",
				Papel = papelDaVista,
				// O NOME DO OUTRO, e nao "o primeiro do snapshot" -- o berco tem NPC. Mesma
				// armadilha do `--vida` e do `--morte`.
				Alvo = Arg(OS.GetCmdlineArgs(), "--vistaalvo") ?? "",
			};
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--vistafim"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segVista))
				rvi.Fim = segVista;
			AddChild(rvi);
		}

		// --morte <a|b>: a MORTE VISTA DE FORA, tambem de DOIS PROCESSOS. O `a` morre (de soco letal
		// alheio, nao de bandeira) e conta pra que zona a morte o levou; o `b` hospeda, MATA no
		// combate e FOTOGRAFA a cabeca do morto, do lado da cabeca de um vivo.
		//
		// Nao cabe num processo so, e pela razao mais simples que existe: a auréola e um sinal pros
		// OUTROS. As duas bancadas que ela ja tinha nao a olham -- a `--alemteste` e headless (mede
		// o byte, nunca desenha) e o bloco do `--diagforma` chama `MostrarAureola` na mao num boneco
		// local, que prova a funcao e nao o percurso. Ver RoboDeMorteVista e `ver-a-morte.bat`.
		//
		// EXIGE `--vooteste` no servidor: a pergunta "a auréola acompanha um morto VOANDO?" depende
		// de o morto poder decolar, e o voo e skill.
		if (Arg(OS.GetCmdlineArgs(), "--morte") is { } papelDaMorte)
		{
			var rm = new RoboDeMorteVista
			{
				Name = "RoboDeMorteVista",
				Papel = papelDaMorte,
				// O NOME DO OUTRO, e nao "o primeiro do snapshot" -- mesma armadilha do `--vida`.
				Alvo = Arg(OS.GetCmdlineArgs(), "--mortealvo") ?? "",
				Conta = Arg(OS.GetCmdlineArgs(), "--conta") ?? "",
			};
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--mortefim"),
								System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out double segMorte))
				rm.Fim = segMorte;
			AddChild(rm);
		}

		// --velorio: a bancada de DOIS CORPOS da morte, do Outro Mundo e da auréola -- num processo
		// so, porque o `--host` e servidor e cliente ao mesmo tempo e o corpo de controle e um corpo
		// forjado no servidor que nasce ao lado do meu.
		//
		// Ela e a que JULGA (as outras tres medem): cada familia dela tem escrito no comentario qual
		// defeito a poe vermelha, e o par "morto tem / vivo NAO tem" e cobrado no mesmo quadro --
		// senao `TemAureola => true` passaria verde. Ver `RoboDoVelorio` e `testar-velorio.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--velorio") >= 0)
			AddChild(new RoboDoVelorio { Name = "RoboDoVelorio" });

		// --vozviva <a|b|c|d>: a bancada da VOZ com clientes de VERDADE, em processos separados.
		//
		// Ela existe porque as outras duas dizem, cada uma no proprio cabecalho, que nao medem o fio: a
		// `--diagvoz` mede o codec sem rede nenhuma, e a `--vozteste` mede o corte com corpos forjados
		// -- que nao tem `Peer`, ou seja nunca executam a linha de ENTREGA que o sistema inteiro existe
		// pra vigiar. Aqui o `a` injeta uma onda conhecida no lugar da captura e o `b`, noutro processo
		// e com outra memoria, mede o que saiu do decodificador DELE. Sobe com `--vozviva` no servidor.
		// Ver `Client/RoboDeVozViva.cs` e `testar-voz.bat`.
		if (Arg(OS.GetCmdlineArgs(), "--vozviva") is { } papelDaVoz)
			AddChild(new RoboDeVozViva { Name = "RoboDeVozViva", Papel = papelDaVoz });

		// --vozdupla <a|b>: a bancada da voz que JULGA, com DOIS corpos.
		//
		// A `--vozviva` mede e imprime tabelas; esta da veredito por familia e, pra cada familia, poe o
		// DEFEITO na frente da checagem e exige que a linha fique vermelha (inclusive o principal: o
		// servidor mandando pra zona inteira). O `a` fala apertando a tecla DE VERDADE -- evento de
		// teclado injetado no motor, porque duas das familias sao sobre a tecla e `Input.ActionPress`
		// pularia exatamente a ligacao tecla->acao. O `b` ouve, conta BYTES e julga; e ele o anfitriao,
		// porque quem cala tem que ser admin. Ver `Client/RoboDeVozDupla.cs` e `testar-voz-dupla.bat`.
		if (Arg(OS.GetCmdlineArgs(), "--vozdupla") is { } papelDaDupla)
			AddChild(new RoboDeVozDupla { Name = "RoboDeVozDupla", Papel = papelDaDupla });

		// --diagdesvio: bancada do DESVIO (o pedido do dono -- "falta o SOM do dodge e o EFEITO DE
		// DESVIO"). Precisa de DOIS corpos e de DESNIVEL DE PODER: entre iguais a pontaria acerta
		// 100% das vezes e nao ha esquiva nenhuma pra ver. Sobe com `--host --esquivateste N` deste
		// lado e um `--socar` do outro. Ver RoboDeDesvio e `testar-desvio.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagdesvio") >= 0)
			AddChild(new RoboDeDesvio { Name = "RoboDeDesvio" });

		// --diagcorpo: a IRMA da `--diagdesvio`, e o oposto dela. Aquela poe dois lutadores brigando e
		// FOTOGRAFA o que a tela mostra (a prova de cor e dela); esta DIRIGE o efeito e mede a maquina
		// -- o corpo some, o corpo VOLTA, dez trocas sobrepostas, e as quatro formas de interromper
		// uma no meio (nocaute, transformacao, troca de zona, remocao a forca). Sao coisas que uma
		// briga de verdade nao sabe encomendar. Sozinha, sem adversario, e roda `--headless`.
		// Ver RoboDoCorpoQueVolta e `testar-corpo.bat`.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagcorpo") >= 0)
			AddChild(new RoboDoCorpoQueVolta { Name = "RoboDoCorpoQueVolta" });

		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagclash") >= 0)
		{
			AddChild(new RoboDeEmbate { Name = "RoboDeEmbate" });
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--socar") < 0)
				AddChild(new RoboDeSoco { Name = "RoboDeSoco" });
		}

		if (Array.IndexOf(OS.GetCmdlineArgs(), "--socar") >= 0)
		{
			var robo = new RoboDeSoco();
			// `--mente N`: o robo medita e entra na propria mente depois de N segundos. E o unico
			// jeito de exercitar a IA do clone sem uma segunda pessoa no servidor.
			int im = Array.IndexOf(OS.GetCmdlineArgs(), "--mente");
			if (im >= 0 && im + 1 < OS.GetCmdlineArgs().Length
				&& double.TryParse(OS.GetCmdlineArgs()[im + 1], System.Globalization.NumberStyles.Float,
								   System.Globalization.CultureInfo.InvariantCulture, out double seg))
				robo.EntrarNaMenteEm = seg;
			// `--tech`: o robo percorre a cadeia inteira de tecnologia (construir, aparafusar,
			// estudar, instalar o laboratorio, virar androide). Vem junto do `--techteste` do
			// servidor, que da o nivel e o dinheiro.
			// `--socaralvo <nome>`: soca ESTE e mais ninguem. Num berco povoado a marcacao
			// automatica ("o primeiro do snapshot") pega um NPC. Ver RoboDeSoco.AlvoPreferido.
			robo.AlvoPreferido = Arg(OS.GetCmdlineArgs(), "--socaralvo") ?? "";
				// `--socarpesado`: SHIFT+ESPACO em vez de so ESPACO. Sem ele o robo so manda golpe
				// leve, cujo arranque busca 80 px -- ou seja, a INVESTIDA LONGA (a do relato do dono,
				// a que a IA usa) nunca era exercitada por ele. Ver RoboDeSoco.Pesado.
				robo.Pesado = Array.IndexOf(OS.GetCmdlineArgs(), "--socarpesado") >= 0;
			// `--socarperto <px>`: a que distancia o robo para de andar. Ver RoboDeSoco.PararA --
			// no padrao (40) os dois corpos se empilham, e a foto de qualquer bancada visual sai
			// com um sprite dentro do outro.
			if (float.TryParse(Arg(OS.GetCmdlineArgs(), "--socarperto"),
							   System.Globalization.NumberStyles.Float,
							   System.Globalization.CultureInfo.InvariantCulture, out float px) && px > 0)
				robo.PararA = px;
			// `--socarvoando <seg>`: o socador sobe pro andar 1 depois de N segundos. A regra de
			// alcance por altura e ASSIMETRICA (quem paira alcanca o chao, o chao nao alcanca de
			// volta), entao uma bancada que queira ver qualquer coisa acontecer NO AR precisa dos
			// dois corpos no mesmo andar. Ver RoboDeSoco.VoarEm -- exige `--vooteste` no servidor.
			if (double.TryParse(Arg(OS.GetCmdlineArgs(), "--socarvoando"),
							   System.Globalization.NumberStyles.Float,
							   System.Globalization.CultureInfo.InvariantCulture, out double segV) && segV > 0)
				robo.VoarEm = segV;
			robo.TestarTech = Array.IndexOf(OS.GetCmdlineArgs(), "--tech") >= 0;
			robo.Bio = Array.IndexOf(OS.GetCmdlineArgs(), "--bio") >= 0;
			int ie = Array.IndexOf(OS.GetCmdlineArgs(), "--espaco");
			if (ie >= 0 && ie + 1 < OS.GetCmdlineArgs().Length
				&& double.TryParse(OS.GetCmdlineArgs()[ie + 1], System.Globalization.NumberStyles.Float,
								   System.Globalization.CultureInfo.InvariantCulture, out double segE))
				robo.DecolarEm = segE;
			AddChild(robo);
		}
	}

	/// <summary>Saiu do servidor: derruba o mundo e reconstroi a tela de login do zero.</summary>
	private void VoltarAoLogin()
	{
		// AS DUAS TELAS DE MAQUINA NAO SAO DO MUNDO E NAO MORREM COM ELE. Elas nasceram antes do
		// login (`_Ready`) e continuam valendo depois dele -- derruba-las aqui deixaria o lobby de
		// volta sem opcoes e sem tela de teclas, que e exatamente o defeito que esta tarefa
		// conserta, so que reintroduzido pela porta dos fundos.
		foreach (Node n in GetChildren())
		{
			if (n == _pause || n == _teclas) continue;
			n.QueueFree();
		}
		_selecao = null;
		MontarLogin();
		AudioDirector.Instance?.Ambiente(null);
		// ZERA A MAQUINA ANTES DE PEDIR O TEMA. O `QueueFree` acima derruba o mundo e qualquer
		// cinematica no ar, mas o PEDIDO de musica dela nao morre junto (ele so morre quando a faixa
		// acaba) -- e um pedido de `Transformacao` de pe ganha do `Menu`, deixando a tela de login
		// muda. Ver `AudioDirector.Silenciar`.
		AudioDirector.Instance?.Silenciar("sai do mundo (volta ao login)");
		AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu, "tela de login de volta");
	}

	// =====================================================================
	// LINHA DE COMANDO (teste headless e clientes-robo)
	// =====================================================================
	/// <summary>
	/// `--connect &lt;host&gt;` (ou `--host`) entra sozinho: faz login e, se nao houver personagem,
	/// cria um padrao no primeiro slot. Sem isto nao da pra testar sem janela.
	/// </summary>
	private void AutoConectar()
	{
		string[] args = OS.GetCmdlineArgs();

		bool hospedando = Array.IndexOf(args, "--host") >= 0;

		// ============================ `--rede <n>`: A PORTA, PRAS DUAS PONTAS DE UMA VEZ ============================
		// A porta era a constante `Protocol.DefaultPort` nos dois lados, e por isso UMA bancada por
		// maquina: enquanto um `--host` estivesse no ar, nenhuma outra subia -- e as bancadas nao saem
		// sozinhas. (O `--port` que ja existia so serve pro servidor DEDICADO, `--server`; o cliente
		// continuava discando 7777, entao ele nunca fechava um par.) O nome nao e `--porta`: aquele ja
		// e a bancada das PORTAS de construcao, e reusa-lo trocaria uma bancada pela outra em silencio.
		//
		// UM SO NUMERO PRO SERVIDOR E PRO CLIENTE de proposito: quem hospeda disca em si mesmo, e dois
		// campos separados so poderiam divergir.
		// ==========================================================================================
		int porta = Jandirus.Net.Protocol.DefaultPort;
		if (int.TryParse(Arg(args, "--rede"), out int pRede) && pRede > 0) porta = pRede;

		// `--host` sobe o servidor e conecta em 127.0.0.1 logo abaixo -- e o endereco que faz o
		// servidor reconhecer o dono como admin, sem marca nenhuma daqui.
		if (hospedando && Jandirus.Server.GameServer.Instance is { } srvAuto) srvAuto.Start(porta);

		int i = Array.IndexOf(args, "--connect");
		string alvo = i >= 0 && i + 1 < args.Length ? args[i + 1] : (hospedando ? "127.0.0.1" : "");
		if (alvo.Length == 0) return;

		_autoNome = Arg(args, "--nome") ?? "Guerreiro";
		_autoRaca = Arg(args, "--raca") ?? "Human";
		_auto = true;

		GameClient.Instance?.Conectar(alvo, porta,
			Arg(args, "--conta") ?? _autoNome, Arg(args, "--senha") ?? "teste");
	}

	private static string? Arg(string[] args, string chave)
	{
		int i = Array.IndexOf(args, chave);
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	/// <summary>No modo automatico ninguem clica: joga o primeiro slot, ou cria no primeiro vazio.</summary>
	private void AutoEscolher(List<Jandirus.Net.SlotInfo> slots)
	{
		for (int i = 0; i < slots.Count; i++)
			if (slots[i].Ocupado) { GameClient.Instance?.PedirSlot(i); return; }

		var ficha = new CharacterDraft
		{
			Name = _autoNome, Race = _autoRaca,
			Planet = Array.Find(CharacterDraft.Planetas,
				pl => Array.IndexOf(CharacterDraft.RacasDoPlaneta(pl), _autoRaca) >= 0) ?? "Earth",
			Gender = "Male",

			// A HISTORIA E OBRIGATORIA, tambem pro robo. Ela nasceu com minimo de 10 caracteres
			// (regra do original) e o caminho automatico nao escrevia nenhuma -- o servidor passou a
			// recusar TODA bancada com "escreva a historia do personagem". Uma linha honesta aqui
			// vale mais que uma excecao no validador: a bancada tem que atravessar o mesmo portao
			// que o jogador, senao ela deixa de testar o portao.
			Backstory = "Personagem de bancada, criado por linha de comando para testes.",

			// `--bercoperto`: o caminho automatico marca "nascer perto de casa".
			//
			// A OPCAO SO EXISTE NA TELA DE CRIACAO, e o caminho automatico nao passa por ela -- ou
			// seja, sem esta linha o pedido de vizinho (metade do que o dono pediu) nao teria como
			// ser exercitado com um corpo de verdade: a bancada `--diagberco` prova a REGRA, esta
			// flag prova o POUSO (orbita, geracao do mundo fora do tique, colisao, chegada).
			PertoDeCasa = Array.IndexOf(OS.GetCmdlineArgs(), "--bercoperto") >= 0,
		};

		// A IDADE RESPEITA O AUGE DA RACA. Dezoito serve pra quase todas, mas nao pro Saibaman, que
		// vive dez anos -- e uma bancada de Saibaman seria recusada por idade.
		ficha.Age = Math.Min(18, ficha.IdadeMaxima);

		string[] linhagens = CharacterDraft.EscolhasDeClasse(_autoRaca);
		if (linhagens.Length > 0) ficha.ChosenClass = linhagens[0];

		// ============================ O CABELO DA BANCADA E O DO GOKU ============================
		// Pedido do dono, e ele tem razao: a aparencia padrao e `Bald`, e um teste de transformacao
		// num personagem CARECA nao prova que o cabelo troca de cor -- nao ha cabelo pra trocar.
		// Com o do Goku, a passagem pra dourado (e o piscar da cinematica do SSJ1) fica visivel na
		// foto, que e o unico juiz de efeito visual.
		// ====================================================================================
		var visual = new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" };

		GameClient.Instance?.CriarPersonagem(0, ficha, visual);
	}

	// ============================ AS TECLAS SAIRAM DAQUI ============================
	// `RegistrarTeclas()` morava neste arquivo e escrevia as vinte acoes direto no `InputMap`. Ela
	// virou a tabela do `Client/Teclas.cs` inteira -- inclusive as sete teclas de interface, que
	// nunca passaram pelo `InputMap` e por isso eram invisiveis a qualquer pergunta de conflito.
	//
	// O motivo esta no cabecalho do `Teclas`: com tecla configuravel, "a tecla P esta livre?" tem
	// que ser UMA pergunta numa tabela so, senao o jogo responde que sim e entrega o menu junto do
	// golpe. A chamada mudou de lugar la em cima (depois do `Settings.Carregar`, porque agora ela
	// precisa saber o que o jogador religou).
	// ==============================================================================
}
