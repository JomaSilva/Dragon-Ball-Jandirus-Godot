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

	private PauseMenu? _pause;

	public override void _Ready()
	{
		// ============================ QUAL BUILD ESTA RODANDO ============================
		// Existe porque nesta sessao a bancada rodou o binario ANTIGO depois de um erro de compile
		// e deu "TUDO OK" -- e o dono e eu passamos uma rodada discutindo um conserto que nunca
		// chegou na tela dele. A hora do .dll e a unica coisa que resolve isso sem adivinhacao:
		// se a linha abaixo nao mudar depois de compilar, o jogo esta com a versao velha.
		// ============================================================================
		try
		{
			string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (dll.Length > 0 && System.IO.File.Exists(dll))
				GD.Print($"[build] {System.IO.File.GetLastWriteTime(dll):dd/MM HH:mm:ss}  ({dll.GetFile()})");
		}
		catch (Exception e) { GD.Print($"[build] nao deu pra ler a data do binario: {e.Message}"); }

		RegistrarTeclas();

		Config = Settings.Carregar();
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

		// A TINTA DO CABELO E DO RABO, medida na TELA. Vive aqui em cima com as outras bancadas sem
		// mundo porque ela nao precisa de rede nem de zona -- so da folha, do shader e de um quadro
		// desenhado. E ela PRECISA de janela: e a foto que responde.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagtinta") >= 0)
		{
			AddChild(new RoboDeTinta { Name = "RoboDeTinta" });
			return;
		}

		// diagnostico da SOMBRA: confere o leque contra o raycast, celula a celula
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvisao") >= 0)
		{
			AddChild(new VisaoDiag { Name = "DiagVisao" });
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

		MontarLogin();

		// MUSICA DESDE A PRIMEIRA TELA: no BYOND a criacao de personagem tinha trilha, e e
		// ela que volta toda vez que o menu de pause abre.
		AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu);

		if (GameClient.Instance is { } cli)
		{
			cli.SlotsRecebidos += AoReceberSlots;
			cli.Joined += (_, _, _, _) => AoEntrarNoMundo();
			cli.Rejected += motivo => _status.Text = $"recusado: {motivo}";
		}

		AutoConectar();
	}

	// =====================================================================
	// TELA 1 -- LOGIN
	// =====================================================================
	private void MontarLogin()
	{
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
		// A GRADE DA BANCADA e o fantasma de assentar construcao.
		AddChild(new TelaDeConstrucao { Name = "Construcao" });
		// A TELA DE TROCA DE MAPA. Ver `TelaDeCarregamento`: ela nao acelera nada, ela ANUNCIA.
		AddChild(new TelaDeCarregamento { Name = "Carregando" });

		_pause = new PauseMenu { Name = "Pause" };
		_pause.Desconectar += VoltarAoLogin;
		AddChild(_pause);

		// a musica do lugar assume; o tema de menu sai de cena
		AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Menu);

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

		// --diagnebulosa: bancada da NUVEM DE GALAXIA do Ultra Instinto. Separada do `--diagforma` de
		// proposito: aquela mede catalogo e texto (e o `Posar` dela pinta o node direto, sem rede),
		// esta veste a forma PELA REDE e tira a foto. Ver RoboDeNebulosa.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagnebulosa") >= 0)
			AddChild(new RoboDeNebulosa { Name = "RoboDeNebulosa" });

		// --diagbalao: bancada do BALAO DE FALA sobre a cabeca. Confere o portao de canal (OOC nao
		// e voz de personagem), a busca do corpo pelo NOME que vem no pacote, a quebra de linha, a
		// substituicao com piso de leitura, e as duas coisas que so aparecem com o corpo no ar ou
		// em cena: a subida junto de quem voa e a fala de cinematica alheia. Ver RoboDeBalao.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagbalao") >= 0)
			AddChild(new RoboDeBalao { Name = "RoboDeBalao" });

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

		// --diagvoo: bancada do VOO. Vem com `--vooteste` no servidor, que da a skill de voo (sem
		// ela nao ha o que medir) e tira o freeflight do admin (sem isso o custo mediria zero).
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvoo") >= 0)
			AddChild(new RoboDeVoo { Name = "RoboDeVoo" });

		// --diagvolta: bancada da VOLTA DO PLANETA. Anda ate a beirada e confere que sai pela outra.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagvolta") >= 0)
			AddChild(new RoboDeVolta { Name = "RoboDeVolta" });

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
		foreach (Node n in GetChildren()) n.QueueFree();
		_pause = null;
		_selecao = null;
		MontarLogin();
		AudioDirector.Instance?.Ambiente(null);
		AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu);
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

	/// <summary>
	/// As acoes ficam em codigo, nao no project.godot: o formato serializado de InputEvent
	/// e verboso e facil de corromper na mao, e assim o mapeamento fica versionado junto
	/// com a logica que o le.
	/// </summary>
	private static void RegistrarTeclas()
	{
		Registrar("move_left", Key.A, Key.Left);
		Registrar("move_right", Key.D, Key.Right);
		Registrar("move_up", Key.W, Key.Up);
		Registrar("move_down", Key.S, Key.Down);

		Registrar("train", Key.T);
		Registrar("meditate", Key.M);

		// COMBATE. ESPACO soca e ALT ergue a guarda -- as teclas do jogo original.
		//
		// SHIFT E CORRER, e correr E o golpe pesado: no original nao havia tecla de "soco
		// forte", o ataque ficava pesado justamente quando saia em dash (`1 + dash_delay`).
		// Manter uma tecla so pra isso seria inventar controle que o jogo nunca teve -- e
		// justamente a tecla que o dono pediu pra correr.
		Registrar("attack", Key.Space);
		Registrar("run", Key.Shift);
		Registrar("guard", Key.Alt);

		// onde mirar: 0 solta a mira, 1-5 escolhem a regiao (o mesmo que clicar no boneco)
		Registrar("aim_none", Key.Key0);
		Registrar("aim_head", Key.Key1);
		Registrar("aim_torso", Key.Key2);
		Registrar("aim_abdomen", Key.Key3);
		Registrar("aim_arms", Key.Key4);
		Registrar("aim_legs", Key.Key5);
		Registrar("lethal", Key.K);

		// TRANSFORMAR. "C" e a tecla do original -- ela SOBE a escada sozinha, e quem decide
		// qual degrau cabe e o servidor. "X" desce pra base de uma vez.
		Registrar("transformar", Key.C);
		Registrar("reverter", Key.X);

		// VOO. "V" liga e desliga o pairar; R/F (ou PageUp/PageDown) sobem e descem.
		//
		// NAO REUSAM O ESPACO nem o SHIFT de proposito. Espaco e o soco e Shift e a corrida --
		// as duas coisas que mais se faz no ar. Subir com a mesma tecla de socar deixaria voar e
		// lutar mutuamente exclusivos, que e o oposto do que voar serve.
		Registrar("voar", Key.V);
		Registrar("subir", Key.R, Key.Pageup);
		Registrar("descer", Key.F, Key.Pagedown);

		static void Registrar(string acao, params Key[] teclas)
		{
			if (!InputMap.HasAction(acao)) InputMap.AddAction(acao, 0.2f);
			foreach (Key k in teclas)
				InputMap.ActionAddEvent(acao, new InputEventKey { PhysicalKeycode = k });
		}
	}
}
