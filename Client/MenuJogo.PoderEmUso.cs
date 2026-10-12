using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// **O CONTROLE DE PODER NA ABA KI** -- a faixa fixa onde se ve quanto do proprio poder esta em uso e se mexe
/// nele: baixar pra uma porcentagem (o deslizador) e relaxar / ficar pronto. Pedido do dono de 2026-10-11
/// (power up e power down), com a regra dele de 2026-10-12: *"tire o botao de esconder o ki e faça com q o de
/// relaxar faca o poder do personagem cair pra 5 de poder de luta sempre, ficando invisivel pra scouters etc"*.
///
/// ============================ O QUE FALTAVA NA TELA ============================
/// O verbo do servidor ja existia (`GameServer.Tecnicas.G9.cs`), e o jogador nao tinha como usa-lo: o botao
/// "Controle de Poder" da aba Skills mandava o verbo SEM numero e o servidor respondia "Use
/// Power_Control:&lt;1 a 100&gt;" -- e nao ha onde digitar isso. E depois de baixar, nada dizia em que estado ele
/// estava. Esta faixa e o lugar das duas coisas.
///
/// ============================ FIXA, FORA DA ROLAGEM -- E O MOTIVO E O DESLIZADOR ============================
/// A pagina da aba Ki e REMONTADA a cada mudanca de Ki (a assinatura dela carrega o Ki inteiro: regenerando,
/// sao cinco remontagens por segundo). Um deslizador dentro dela seria destruido no meio do arrasto, e um
/// botao, debaixo do dedo -- a armadilha que este menu ja pagou (`MenuJogo.Redesenhar`). A faixa mora FORA da
/// rolagem, como a de marcos da Learning (`MontarFaixaDeMarcos`): nasce uma vez e se atualiza no lugar.
///
/// ============================ QUEM VE O QUE ============================
/// O deslizador e o botao de relaxar so existem pra quem TEM o controle de ki (o `Power_Control`, do nivel 5 de
/// Basic Ki Control: `PosseDoCliente.SabeTecnica`, a lista que o servidor aceita) -- a regra da casa: sem o
/// requisito, o botao nao existe. Uma excecao, e ela e de saida: quem esta ABAIXO do proprio poder (relaxado ou
/// contido) ve o "Ficar pronto" de qualquer jeito, porque voltar nao pede porta nenhuma. Esconder botao nao e
/// permissao: o servidor confere.
///
/// SEM NUMERO DE PODER em lugar nenhum daqui: so a porcentagem do PROPRIO poder em uso.
/// ================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>Os dois ids que esta faixa manda pelo canal de habilidade.</summary>
	public const string VerboDeBaixar = "Power_Control", VerboDaProntidao = "prontidao";

	private PanelContainer _painelDoPoder = null!;
	private Label _poderGrande = null!, _poderLegenda = null!, _poderEscolhido = null!, _poderNota = null!;
	private HBoxContainer _poderLinhaDeBaixar = null!;
	private HSlider _poderDeslizador = null!;
	private Button _poderBaixar = null!, _poderProntidao = null!;

	/// <summary>
	/// O QUE O DESLIZADOR MARCA. Mora fora dos controles pelo motivo do `_avisoDigitado` da aba de admin: e
	/// escolha do jogador, e nao estado do servidor -- o retrato que chega nao pode mexer nela.
	/// </summary>
	private int _porcentoEscolhido = 50;

	private static PoderEmUsoState PoderDeAgora => GameClient.Instance?.PoderEmUso ?? PoderEmUsoState.Cheio;

	/// <summary>A faixa nasce escondida, entre a barra de abas e a rolagem; quem a mostra e o <see cref="MostrarOPoderEmUsoNaAba"/>.</summary>
	private void MontarOPoderEmUso(VBoxContainer coluna)
	{
		_painelDoPoder = new PanelContainer { Visible = false, Name = "PoderEmUso" };
		_painelDoPoder.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 8));
		coluna.AddChild(_painelDoPoder);

		var pilha = new VBoxContainer();
		pilha.AddThemeConstantOverride("separation", 6);
		_painelDoPoder.AddChild(pilha);

		// ---- 1) o estado: o numero grande e a frase ----
		var estado = new HBoxContainer();
		estado.AddThemeConstantOverride("separation", 12);
		pilha.AddChild(estado);
		Label rotulo = Tema.Rotulo("Poder em uso");
		rotulo.VerticalAlignment = VerticalAlignment.Center;
		estado.AddChild(rotulo);
		_poderGrande = new Label { Text = "100%", VerticalAlignment = VerticalAlignment.Center };
		_poderGrande.AddThemeFontSizeOverride("font_size", 26);
		estado.AddChild(_poderGrande);
		_poderLegenda = new Label
		{
			VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		_poderLegenda.AddThemeFontSizeOverride("font_size", 12);
		_poderLegenda.AddThemeColorOverride("font_color", Tema.TextoFraco);
		estado.AddChild(_poderLegenda);

		// ---- 2) baixar pra uma porcentagem: o deslizador (so pra quem tem o verbo) ----
		_poderLinhaDeBaixar = new HBoxContainer();
		_poderLinhaDeBaixar.AddThemeConstantOverride("separation", 10);
		pilha.AddChild(_poderLinhaDeBaixar);
		// SEM FOCO DE TECLADO: com o menu aberto o personagem continua andando, e um deslizador com foco
		// comeria as setas (e andaria junto com o corpo).
		_poderDeslizador = new HSlider
		{
			MinValue = 1, MaxValue = 100, Step = 1, Value = _porcentoEscolhido,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 0),
			TooltipText = "Quanto do seu poder usar. Arraste e aperte o botão ao lado.",
		};
		_poderDeslizador.ValueChanged += AoArrastarOPoder;
		_poderLinhaDeBaixar.AddChild(_poderDeslizador);
		_poderEscolhido = new Label
		{
			Text = "50%", CustomMinimumSize = new Vector2(46, 0), HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_poderLinhaDeBaixar.AddChild(_poderEscolhido);
		_poderBaixar = new Button { Text = "Baixar para 50%", FocusMode = Control.FocusModeEnum.None };
		_poderBaixar.Pressed += () => GameClient.Instance?.SendHabilidade($"{VerboDeBaixar}:{_porcentoEscolhido}");
		_poderLinhaDeBaixar.AddChild(_poderBaixar);

		// ---- 3) relaxar / ficar pronto ----
		var gestos = new HBoxContainer();
		gestos.AddThemeConstantOverride("separation", 10);
		pilha.AddChild(gestos);
		_poderProntidao = new Button { Text = "Relaxar", FocusMode = Control.FocusModeEnum.None };
		_poderProntidao.Pressed += () => GameClient.Instance?.SendHabilidade(VerboDaProntidao);
		gestos.AddChild(_poderProntidao);
		_poderNota = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_poderNota.AddThemeFontSizeOverride("font_size", 11);
		_poderNota.AddThemeColorOverride("font_color", Tema.TextoFraco);
		gestos.AddChild(_poderNota);

		AtualizarOPoderEmUso();
	}

	private void AoArrastarOPoder(double valor)
	{
		_porcentoEscolhido = (int)Math.Clamp(Math.Round(valor), 1, 100);
		AtualizarOPoderEmUso();
	}

	/// <summary>
	/// A faixa aparece na aba Ki, sem busca (a busca troca a pagina inteira). Chamado pelo `Redesenhar`, ao
	/// lado da faixa de marcos -- antes do desvio de cache, pra ela sumir ao trocar de aba mesmo sem remontagem.
	/// </summary>
	private void MostrarOPoderEmUsoNaAba()
	{
		_painelDoPoder.Visible = _aba == "Ki" && _busca.Text.Trim().Length == 0;
		if (_painelDoPoder.Visible) AtualizarOPoderEmUso();
	}

	/// <summary>
	/// ESCREVE O ESTADO NOS CONTROLES QUE JA EXISTEM. Nada e criado nem destruido aqui: e por isso que o
	/// deslizador aguenta o arrasto com o retrato chegando do servidor.
	/// </summary>
	private void AtualizarOPoderEmUso()
	{
		PoderEmUsoState e = PoderDeAgora;
		bool controla = PosseDoCliente.Atual.SabeTecnica(VerboDeBaixar);

		// RELAXADO A BARRA NAO DIZ NADA (o corpo soltou o poder todo): no lugar da porcentagem vai a palavra.
		_poderGrande.Text = e.Relaxado ? "mínimo" : $"{e.Porcento}%";
		_poderGrande.AddThemeColorOverride("font_color", e.Padrao ? Tema.Texto : Hud.Cor(e));
		_poderLegenda.Text = Hud.PorExtenso(e);

		// ---- baixar ----
		_poderLinhaDeBaixar.Visible = controla;
		_poderEscolhido.Text = $"{_porcentoEscolhido}%";
		_poderBaixar.Text = $"Baixar para {_porcentoEscolhido}%";
		bool abaixo = !e.Relaxado && _porcentoEscolhido < e.Porcento;
		_poderBaixar.Disabled = !abaixo;
		_poderBaixar.TooltipText = e.Relaxado
			? "Relaxado você não está usando poder nenhum: fique pronto primeiro."
			: abaixo
				? "Segura o seu poder nessa porcentagem, na hora."
				: "Isto só BAIXA: você já usa esse tanto ou menos. Pra subir, segure a tecla de carregar Ki -- ou fique pronto.";

		// ---- relaxar / ficar pronto: o `if(powerMod >= 1) ... else` do ReadyUp ----
		// Relaxar e de quem tem o controle de ki; FICAR PRONTO e de todo mundo que esta abaixo do proprio poder.
		bool relaxa = !e.Relaxado && e.Porcento >= 100;
		_poderProntidao.Visible = controla || !relaxa;
		_poderProntidao.Text = relaxa ? "Relaxar" : "Ficar pronto (100%)";
		_poderProntidao.TooltipText = relaxa
			? "Solta o corpo: o seu poder cai ao mínimo, ninguém mais sente a sua energia (nem scouter) e o vigor volta "
			  + "o mais depressa que há. Você fica fraco de verdade: bate e apanha como quem não tem poder. Furioso, você não consegue."
			: "Volta a usar o poder inteiro, na hora.";

		_poderNota.Text = controla
			? "Baixar é na hora e NÃO esconde você de ninguém -- só relaxar esconde. Quanto menos poder em uso, mais depressa o "
			  + "vigor volta. Pra subir: segure a tecla de carregar Ki (aos poucos) ou fique pronto."
			: "Conter o poder numa porcentagem e relaxar pedem controle de ki (Basic Ki Control 5).";
	}

	/// <summary>O RETRATO MUDOU (`S2C.PoderEmUso`): a faixa se atualiza no lugar, e a aba Stats (que escreve a frase) se refaz.</summary>
	private void AoPoderEmUso()
	{
		if (!Visible) return;
		if (_painelDoPoder.Visible) AtualizarOPoderEmUso();
		if (_aba == "Stats") Redesenhar();
	}

	/// <summary>A linha do estado no cartao Estado da aba Stats -- onde o original a escreve (`[Emotion] / [relaxedstate]`, HtmlUI.dm:221).</summary>
	private static void LinhaDoPoderEmUso(Control pai)
	{
		PoderEmUsoState e = PoderDeAgora;
		Linha("Poder em uso", Hud.PorExtenso(e), e.Padrao ? null : Hud.Cor(e), pai);
	}

	/// <summary>O pedaco do poder em uso na assinatura de cache de quem o desenha na PAGINA (a aba Stats).</summary>
	private static string AssinaturaDoPoderEmUso()
	{
		PoderEmUsoState e = PoderDeAgora;
		return $"{e.Porcento}/{(byte)e.Bits}";
	}

	/// <summary>
	/// O BOTAO "Controle de Poder" DA ABA SKILLS (e a tecla ligada a ele) ABRE ESTA FAIXA. O verbo sem numero
	/// nao faz nada no servidor; o numero se escolhe aqui. Devolve falso pra qualquer outro id -- e ai quem
	/// chamou manda o verbo como sempre (`Habilidades.DasSkills`).
	/// </summary>
	public static bool AbrirOControleDe(string id)
	{
		if (!string.Equals(id, VerboDeBaixar, StringComparison.OrdinalIgnoreCase) || Instancia is not { } m) return false;
		m._busca.Text = "";   // a busca vence a aba: com ela preenchida a faixa nao aparece
		if (!m.Visible) m.Abrir();
		m.FecharArvore();
		m._aba = "Ki";
		m.Redesenhar();
		return true;
	}

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagpoder`)
	// =====================================================================

	/// <summary>O que a faixa mostra AGORA, lido dos controles. So pra bancada.</summary>
	public sealed record PoderNaAba(bool Visivel, string Grande, string Legenda, bool TemDeslizador, int Escolhido,
									string Baixar, bool BaixarApagado, bool TemProntidao, string Prontidao, string Nota);

	public PoderNaAba PoderNaAbaDeTeste() => new(
		_painelDoPoder.Visible, _poderGrande.Text, _poderLegenda.Text, _poderLinhaDeBaixar.Visible, _porcentoEscolhido,
		_poderBaixar.Text, _poderBaixar.Disabled, _poderProntidao.Visible, _poderProntidao.Text, _poderNota.Text);

	/// <summary>Arrasta o deslizador (o mesmo `ValueChanged` do mouse). So pra bancada.</summary>
	public void ArrastarOPoderDeTeste(int porcento) => _poderDeslizador.Value = porcento;

	public Button BotaoDeBaixarDeTeste => _poderBaixar;
	public Button BotaoDaProntidaoDeTeste => _poderProntidao;
	public Control PainelDoPoderDeTeste => _painelDoPoder;
}
