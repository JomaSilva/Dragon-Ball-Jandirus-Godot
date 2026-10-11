using Godot;
using Jandirus.Core.Economia;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A FAIXA DA MINERACAO -- o que quem minera precisa ENXERGAR enquanto fica parado: com que ferramenta, quanto
/// rende por segundo, quanto ja tem no bolso e como esta a reserva do planeta. Pedido do dono de 2026-10-10
/// (*"adicionando um verb chamado mineirar ao qual vc comeca a pegar resource por segundo"*); a regra e do
/// servidor (`GameServer.Recursos.cs`) e os numeros chegam prontos pelo fio (`S2C.Mineracao`, `S2C.Carteira`).
///
/// ELA APARECE COM A ATIVIDADE LOCAL (`GameClient.Atividade`), e nao com a confirmacao do servidor: o jogador
/// apertou "Minerar" e a tela responde no mesmo quadro, como o "MEDITANDO" do HUD. Se o servidor recusar (nao
/// ha o que minerar aqui), a atividade cai em seguida e a faixa some com ela.
///
/// SEM ASSINATURA NENHUMA: ela le o cliente por quadro e so reescreve os rotulos quando algum numero mudou --
/// o desenho da `SaidaDaMente`, pelo mesmo motivo (um node que vive o tempo todo nao tem o que vazar).
///
/// O BOTAO "Parar" faz o mesmo que apertar o verb de novo; andar tambem para.
/// </summary>
public partial class PainelDeMineracao : CanvasLayer
{
	public static PainelDeMineracao? Instancia { get; private set; }

	private PanelContainer _faixa = null!;
	private Label _titulo = null!, _taxa = null!, _saldo = null!, _reserva = null!;
	private ProgressBar _barra = null!;
	private StyleBoxFlat _cheio = null!;
	private Button _parar = null!;

	/// <summary>O que esta escrito agora, numa chave so: os rotulos so sao refeitos quando ela muda.</summary>
	private string _desenhado = "";

	public override void _EnterTree() => Instancia = this;
	public override void _ExitTree() { if (Instancia == this) Instancia = null; }

	/// <summary>
	/// ONDE ELA FICA: logo ACIMA do chat, alinhada com ele pela esquerda, e crescendo pra cima.
	///
	/// A primeira versao ficava no pe da tela, ao centro, numa linha so -- e a conta, feita com uma foto da tela
	/// na frente, disse por que nao: a tela e de 1280 de largura (`project.godot`, esticada), o chat ocupa de 12 a
	/// 552, e uma linha de ~650 px centrada em 640 cobriria as falas. Acima do chat nao ha nada (o painel de vida acaba la em
	/// cima), o lugar nao depende da largura da janela, e e ao lado de onde o servidor escreve o que a mineracao
	/// rendeu.
	///
	/// ============================ ELA LE O RETANGULO DO CHAT, E NAO OS NUMEROS DELE ============================
	/// Estes dois numeros eram a regra: a margem (12) e o topo do chat a 292 px do pe, mais um respiro de 8. O chat
	/// passou a ter o tamanho que o jogador quiser (`Chat.Tamanho.cs`), e a faixa passou a medir o painel dele NA
	/// TELA a cada quadro em que aparece -- ver <see cref="Assentar"/>. Com o chat de fabrica a conta da exatamente
	/// estes dois; eles ficaram so como o lugar de quem nasce sem chat (uma bancada sem ele).
	/// ============================================================================================================
	/// </summary>
	private const float MargemEsquerda = 12, PeAcimaDoChat = 300;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): a faixa volta a usar os numeros FIXOS do chat de fabrica -- com o
	/// chat maior ela fica por cima das falas, com o chat menor fica solta no meio da tela. Falso em jogo, sempre.
	/// </summary>
	public static bool NumerosFixosDoChatDeTeste;

	/// <summary>
	/// **O LUGAR DA FAIXA, PELO RETANGULO REAL DO CHAT**: em cima dele, alinhada pela esquerda, com um respiro -- ou,
	/// se ali ela bateria no painel de status (um chat alto com um painel grande nao deixam vao), a direita do chat,
	/// no pe (`Janelas.JuntoDoChat`). So escreve quando o lugar muda.
	/// </summary>
	private void Assentar()
	{
		if (NumerosFixosDoChatDeTeste || Chat.RetanguloNaTela is not { } chat)
		{
			Por(MargemEsquerda, -PeAcimaDoChat);
			return;
		}

		float tela = GetViewport().GetVisibleRect().Size.Y;
		Vector2 faixa = _faixa.Size.Max(_faixa.GetCombinedMinimumSize());
		Vector2 canto = Janelas.JuntoDoChat(chat, faixa, 0f, Hud.RetanguloDoStatus?.End.Y ?? 0f);
		Por(canto.X, canto.Y - tela);   // (ancora de baixo: o deslocamento e negativo)
	}

	private void Por(float esquerda, float pe)
	{
		if (_faixa.OffsetLeft != esquerda) _faixa.OffsetLeft = esquerda;
		if (_faixa.OffsetTop != pe || _faixa.OffsetBottom != pe) { _faixa.OffsetTop = pe; _faixa.OffsetBottom = pe; }
	}

	public override void _Ready()
	{
		Layer = 5;   // por cima do HUD e do chat; por baixo da saida da mente e das telas que cobrem tudo

		var raiz = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(raiz);
		AddChild(raiz);

		_faixa = Tema.Painel1(8);
		_faixa.AnchorTop = 1; _faixa.AnchorBottom = 1;
		_faixa.OffsetLeft = MargemEsquerda;
		_faixa.OffsetTop = -PeAcimaDoChat; _faixa.OffsetBottom = -PeAcimaDoChat;
		_faixa.GrowVertical = Control.GrowDirection.Begin;   // o pe fica onde esta; o que crescer, cresce pra cima
		// LARGURA DE QUEM JA TEM OS NUMEROS: sem isto ela nasceria estreita ("MINERANDO...") e alargaria um
		// instante depois, quando o servidor responde.
		_faixa.CustomMinimumSize = new Vector2(400, 0);
		_faixa.Visible = false;
		raiz.AddChild(_faixa);

		var linhas = new VBoxContainer();
		linhas.AddThemeConstantOverride("separation", 4);
		_faixa.AddChild(linhas);

		// EM CIMA: o que o corpo esta fazendo, quanto rende, e o botao de parar na ponta.
		var cima = new HBoxContainer();
		cima.AddThemeConstantOverride("separation", 12);
		linhas.AddChild(cima);
		_titulo = Rotulo(cima, Tema.Destaque, 13);
		_taxa = Rotulo(cima, Tema.Bom, 13);
		cima.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

		_parar = new Button
		{
			Text = "Parar",
			// NUNCA O FOCO DO TECLADO: um botao focado engole o Espaco e o Enter, que sao o soco e o chat.
			FocusMode = Control.FocusModeEnum.None,
			TooltipText = "Para de minerar. Andar também para.",
		};
		_parar.AddThemeFontSizeOverride("font_size", 12);
		_parar.Pressed += LocalPlayer.PedirMineracao;
		cima.AddChild(_parar);

		// EMBAIXO: o bolso e o chao -- o numero da reserva e uma barra fina, que e quem diz se o planeta esta acabando.
		var baixo = new HBoxContainer();
		baixo.AddThemeConstantOverride("separation", 12);
		linhas.AddChild(baixo);
		_saldo = Rotulo(baixo, Tema.Texto, 12);
		_reserva = Rotulo(baixo, Tema.TextoFraco, 12);
		_cheio = new StyleBoxFlat { BgColor = Tema.Bom };
		_cheio.SetCornerRadiusAll(3);
		_barra = new ProgressBar
		{
			CustomMinimumSize = new Vector2(90, 6),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			ShowPercentage = false,
			MaxValue = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_barra.AddThemeStyleboxOverride("fill", _cheio);
		baixo.AddChild(_barra);
	}

	private static Label Rotulo(Control pai, Color cor, int tamanho)
	{
		var l = new Label { VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", tamanho);
		l.AddThemeColorOverride("font_color", cor);
		pai.AddChild(l);
		return l;
	}

	public override void _Process(double delta)
	{
		GameClient? cli = GameClient.Instance;
		bool minerando = cli is { Connected: true, Atividade: Protocol.Activity.Minerando };
		if (minerando) Assentar();   // ANTES de aparecer: ela nao pode nascer num lugar e pular pra outro
		if (_faixa.Visible != minerando) _faixa.Visible = minerando;
		if (!minerando || cli == null) { _desenhado = ""; return; }

		GameClient.MineracaoVista m = cli.Mineracao;
		string chave = $"{m.Ativa}|{(byte)m.Ferramenta}|{m.PorSegundo}|{m.Reserva}|{cli.Recursos}";
		if (chave == _desenhado) return;
		_desenhado = chave;

		// ANTES DA RESPOSTA DO SERVIDOR (o primeiro instante depois do clique) ainda nao ha numero: a faixa diz
		// que esta comecando em vez de mostrar o da mineracao anterior.
		_titulo.Text = m.Ativa ? "MINERANDO " + Recurso.ComQue(m.Ferramenta) : "MINERANDO...";
		_taxa.Text = m.Ativa ? $"+{m.PorSegundo:0.#} recursos/s" : "";
		_saldo.Text = $"você tem {cli.Recursos:N0}";
		// "DO PLANETA", SEM O NOME: o nome que o cliente tem (o da zona, "Earth") nao e o que o servidor fala no
		// chat ao comecar ("Reserva de Terra: 100%") -- seriam dois nomes pro mesmo chao na mesma tela.
		_reserva.Text = m.Ativa ? $"reserva do planeta: {TextoDaReserva(m.Reserva)}" : "";
		_barra.Visible = m.Ativa;
		_barra.Value = Math.Clamp(m.Reserva, 0, 1);
		_cheio.BgColor = m.Reserva > 0.5f ? Tema.Bom : m.Reserva > 0.15f ? Tema.Destaque : Tema.Perigo;
	}

	/// <summary>
	/// A RESERVA EM PORCENTO, arredondada PRA BAIXO e com uma casa: "100%" so com ela cheia de verdade. Num
	/// planeta de meio milhao um minuto de furadeira tira 0,1%, e "100%" na tela diria que minerar nao gasta o chao.
	/// </summary>
	internal static string TextoDaReserva(float fracao) =>
		fracao >= 1f ? "100%" : $"{Math.Floor(Math.Max(fracao, 0f) * 1000) / 10:0.0}%";

	// =====================================================================
	// O QUE A BANCADA `--diagminerar` PERGUNTA
	// =====================================================================
	/// <summary>A faixa esta na tela?</summary>
	internal bool NaTela => _faixa.Visible;

	/// <summary>O retangulo da faixa na tela -- quem se empilha em cima dela (o canto de assistir o torneio) mede aqui.</summary>
	internal Rect2 RetanguloNaTela => _faixa.GetGlobalRect();

	/// <summary>Onde a faixa esta desenhada, em pixels de tela -- pra a bancada cobrar que ela nao cobre o chat nem o HUD.</summary>
	internal Rect2 RetanguloDeTeste => _faixa.GetGlobalRect();

	/// <summary>O que a faixa diz, como o jogador le.</summary>
	internal string TextoDeTeste => $"{_titulo.Text} | {_taxa.Text} | {_saldo.Text} | {_reserva.Text}";

	/// <summary>Aperta o "Parar", como o dedo do jogador.</summary>
	internal void PararDeTeste() => _parar.EmitSignal(BaseButton.SignalName.Pressed);
}
