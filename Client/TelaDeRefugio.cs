using Godot;

namespace Jandirus.Client;

/// <summary>
/// **O SEU PLANETA NATAL ACABOU. PARA ONDE VOCE VOLTA?**
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
/// *"quando uma raca fica sem planeta natal, o jogador pode ou spawnar em um planeta q ele
/// conquistou ou em um planeta proximo do planeta natal dele"*.
///
/// O "ou ... ou" e ESCOLHA, e esta e a tela dela. **Ela so existe quando as duas existem**: com uma
/// saida so, o servidor manda o corpo pra la e nao pergunta nada (`GameServer.Refugio.cs`), e a tela
/// nem chega a ser empurrada -- abrir uma tela sem decisao e roubar o clique de quem esta morto no
/// meio de uma briga.
/// ================================================================================
///
/// ============================ O MOLDE E O DA CASA, E ISSO NAO E DETALHE ============================
/// `CanvasLayer` (camada 4) + `Control` ancorado + `CenterContainer` + `Tema.Painel1` + uma coluna --
/// exatamente a <see cref="TelaDeMeditacao"/>, o menu da tecla E e a confirmacao de apagar
/// personagem. **A tela de apagar teve que ser refeita** justamente por ter fugido daqui: era um
/// `ConfirmationDialog` do Godot, e o dono fotografou o resultado (*"ta TODA TORTA, e se eu coloco
/// em FULL SCREEN o jogo e dps em JANELA, ela MUDA DE POSICAO"*). Duas causas, as duas do TIPO do
/// node: o `AcceptDialog` da o retangulo inteiro a todo filho (os textos se sobrepunham) e o
/// `PopupCentered` centra UMA VEZ, guardando pixels absolutos.
///
/// Ancora nao tem "hora de centrar": ela e recalculada a cada resize, de graca. Nao ha desenho novo
/// aqui e nao devia haver -- este jogo ja tem uma cara pra "escolha uma coisa".
/// ==============================================================================================
///
/// ============================ O QUE ELA NAO FAZ ============================
/// **NAO DECIDE NADA E NAO GUARDA NADA.** Cada botao manda um verbo (`refugio &lt;chave&gt;` ou
/// `refugio vizinhanca`) e o servidor responde com o pacote atualizado. A marca de "escolhido" que
/// aparece aqui e o `Dominio.EhOSpawn` do servidor, e nao um estado local que poderia divergir dele.
///
/// **NAO TRAVA TECLA NENHUMA** -- ela nao entra no <see cref="Foco"/>, como as irmas dela. O jogo
/// continua correndo por baixo, o prazo de 60 s do Outro Mundo continua andando, e quem nao escolher
/// volta a vida pelo padrao (a vizinhanca de casa) sem ficar preso em lugar nenhum.
/// =========================================================================
/// </summary>
public partial class TelaDeRefugio : CanvasLayer
{
	/// <summary>A tela viva, ou nula antes do mundo. E por aqui que o menu e o servidor a alcancam.</summary>
	public static TelaDeRefugio? Instancia { get; private set; }

	private Control _raiz = null!;
	private VBoxContainer _coluna = null!;

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu da tecla E e da telinha de meditar
		Instancia = this;

		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// O FUNDO ESCURO E O QUE FAZ A CAIXA SER MODAL SEM UMA SUBJANELA -- mesmo truque da
		// confirmacao de apagar personagem: um Control que para o mouse come o clique, entao nao da
		// pra apertar um botao do mundo atras da pergunta.
		var fundo = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.72f),
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		_raiz.AddChild(fundo);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(18);
		centro.AddChild(painel);

		_coluna = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		_coluna.AddThemeConstantOverride("separation", 8);
		painel.AddChild(_coluna);

		if (GameClient.Instance is { } cli)
		{
			// METODO NOMEADO E NAO LAMBDA: assinatura com lambda nao da pra cancelar, e este projeto
			// ja pagou essa conta (19 orfaos por relog). Ver `_ExitTree`.
			cli.RefugioMudou += Redesenhar;
			cli.RefugioPediuAbrir += Abrir;
		}

		Redesenhar();
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.RefugioMudou -= Redesenhar;
			cli.RefugioPediuAbrir -= Abrir;
		}
		if (Instancia == this) Instancia = null;
	}

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	/// <summary>
	/// ABRE A TELA. Chamada pelo servidor (uma vez por sessao, na chegada ao Outro Mundo) e pelo
	/// botao do menu.
	///
	/// **Ela se recusa a abrir sem motivo**: sem o berco destruido nao ha nada pra escolher, e uma
	/// tela vazia empurrada na cara do jogador seria pior que nenhuma. O botao do menu tambem so
	/// aparece nessa condicao -- sao duas guardas pro mesmo "nao", e a de dentro e a que vale.
	/// </summary>
	public void Abrir()
	{
		if (GameClient.Instance is not { RefugioPrecisa: true }) return;
		Redesenhar();
		_raiz.Visible = true;
	}

	public void Fechar() => _raiz.Visible = false;

	/// <summary>A tela esta na tela? Le o NODE, e nao um `bool` proprio -- ver `TelaDeMeditacao.NaTela`.</summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>
	/// ESC FECHA, e fechar NAO escolhe nada -- o servidor ja tem um padrao e ele continua valendo.
	///
	/// `_Input` e nao `_UnhandledInput` pelo mesmo motivo medido na tela de apagar personagem: o
	/// `_Input` roda antes, entao a coisa mais na frente da tela fecha primeiro, e o menu de pausa
	/// (que escuta a mesma tecla) nao abre junto.
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (!NaTela || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;

		Fechar();
		GetViewport().SetInputAsHandled();
	}

	// =====================================================================
	// O QUE ESTA NA TELA
	// =====================================================================
	/// <summary>
	/// REFAZ A COLUNA a partir do que o servidor mandou. Tudo o que se le aqui e dado do servidor --
	/// nenhuma regra nasce nesta tela.
	/// </summary>
	private void Redesenhar()
	{
		if (!IsInstanceValid(_coluna)) return;
		foreach (Node n in _coluna.GetChildren()) { _coluna.RemoveChild(n); n.QueueFree(); }

		GameClient? cli = GameClient.Instance;
		if (cli is not { RefugioPrecisa: true }) { Fechar(); return; }

		var titulo = new Label
		{
			Text = $"{cli.RefugioNatal.ToUpperInvariant()} NÃO EXISTE MAIS",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Word,
		};
		titulo.AddThemeFontSizeOverride("font_size", 20);
		titulo.AddThemeColorOverride("font_color", Tema.Perigo);
		_coluna.AddChild(titulo);

		var lead = new Label
		{
			Text = "O planeta onde o seu povo nasce foi destruído. Escolha para onde o seu corpo "
				 + "volta da próxima vez que você morrer.",
			AutowrapMode = TextServer.AutowrapMode.Word,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		lead.AddThemeColorOverride("font_color", Tema.TextoFraco);
		lead.AddThemeFontSizeOverride("font_size", 13);
		_coluna.AddChild(lead);
		_coluna.AddChild(new HSeparator());

		// ---- B1: OS PLANETAS QUE ESTE PERSONAGEM CONQUISTOU -------------
		if (cli.RefugioDominios.Count > 0)
		{
			_coluna.AddChild(Tema.Rotulo("um planeta que você conquistou"));
			foreach (GameClient.RefugioDominio d in cli.RefugioDominios)
			{
				string chave = d.Chave;
				var b = new Button
				{
					Text = (d.Escolhido ? "● " : "○ ") + d.Nome
						 + $"  —  {d.Minutos:0} min de onde era casa",
					TooltipText = "Você renasce na sua própria bandeira, enquanto for o soberano "
								+ "deste planeta. É a mesma escolha do botão 'Renascer aqui' da bandeira.",
				};
				if (d.Escolhido) b.AddThemeColorOverride("font_color", Tema.Destaque);
				b.Pressed += () => GameClient.Instance?.SendVerbo("refugio", chave);
				_coluna.AddChild(b);
			}
		}

		// ---- B2: A VIZINHANCA DE CASA -----------------------------------
		if (cli.RefugioVizinhos.Count > 0)
		{
			_coluna.AddChild(Tema.Rotulo("ou a vizinhança de casa"));

			bool nenhumDominioEscolhido = !cli.RefugioDominios.Exists(d => d.Escolhido);
			var vizinhanca = new Button
			{
				Text = (nenhumDominioEscolhido ? "● " : "○ ")
					 + $"O mundo vivo mais perto — {cli.RefugioVizinhos.Count} possíve"
					 + (cli.RefugioVizinhos.Count > 1 ? "is" : "l"),
				TooltipText = "A mesma estrela, outro chão. Qual dos mundos sai é sorteio do berço -- "
							+ "a gravidade de um mundo vira treino de graça, então o jogo não deixa "
							+ "ninguém apontar qual.",
			};
			if (nenhumDominioEscolhido) vizinhanca.AddThemeColorOverride("font_color", Tema.Destaque);
			vizinhanca.Pressed += () => GameClient.Instance?.SendVerbo("refugio", "vizinhanca");
			_coluna.AddChild(vizinhanca);

			// A LISTA DAS CANDIDATAS E O QUE FAZ A OPCAO SIGNIFICAR ALGUMA COISA -- mesmo argumento
			// (e mesmo texto) da tela de criacao: "um planeta aleatorio" sem dizer quais e um botao
			// que promete o desconhecido.
			foreach (GameClient.RefugioVizinho v in cli.RefugioVizinhos)
			{
				var l = new Label
				{
					Text = $"    {v.Nome} — {v.Minutos:0.0} min de voo, gravidade {v.Gravidade:0}x"
						 + (v.Serve ? "" : "  (pesado demais para um recém-nascido)"),
					AutowrapMode = TextServer.AutowrapMode.Word,
				};
				l.AddThemeColorOverride("font_color", v.Serve ? Tema.TextoFraco : Tema.Perigo);
				l.AddThemeFontSizeOverride("font_size", 12);
				_coluna.AddChild(l);
			}

			if (cli.RefugioReserva)
			{
				var aviso = new Label
				{
					Text = "Perto de casa só sobrou mundo pesado. Um mundo pesado ainda é melhor que "
						 + "lugar nenhum -- e o corpo se acostuma com a gravidade do berço.",
					AutowrapMode = TextServer.AutowrapMode.Word,
				};
				aviso.AddThemeColorOverride("font_color", Tema.Perigo);
				aviso.AddThemeFontSizeOverride("font_size", 12);
				_coluna.AddChild(aviso);
			}
		}
		else
		{
			var vazio = new Label
			{
				Text = "Não sobrou nada vivo perto de onde era casa.",
				AutowrapMode = TextServer.AutowrapMode.Word,
			};
			vazio.AddThemeColorOverride("font_color", Tema.Perigo);
			vazio.AddThemeFontSizeOverride("font_size", 12);
			_coluna.AddChild(vazio);
		}

		_coluna.AddChild(new HSeparator());

		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		_coluna.AddChild(fechar);
	}

}
