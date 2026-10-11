using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A SAIDA DA MEDITACAO PROFUNDA, NA TELA -- uma faixa no alto com o botao de acordar.
///
/// O item 1a do playtest de 2026-10-10: *"adicionar um botao de saida (UI + tecla de atalho) que tire o jogador
/// da meditacao profunda a qualquer momento"*. A porta ja existia e ninguem a achava: era um verb no fim da aba
/// Learning do menu P, e nem a mensagem de entrada falava dela. No original o gesto e a propria tecla de
/// meditar (`if(mind_session) mind_exit()`, `Meditate.dm:20-21`) -- que aqui so reabria a telinha.
///
/// ============================ POR ESTADO, E NAO POR EVENTO ============================
/// A faixa aparece ENQUANTO a zona do cliente for uma mente (`DimensaoMental.EhAMente`), lido a cada quadro.
/// Nao ha "entrei / sai" pra alguem esquecer de avisar: e a mesma pergunta que o servidor faz pra decidir se
/// alguem esta em transe, e por isso a saida esta na tela ate no estado que nao devia existir -- o jogador
/// numa mente sem corpo pra voltar.
///
/// O BOTAO E A TECLA MANDAM O MESMO VERBO (`mente_sair`), pelo canal de verbos: o servidor o atende sempre,
/// inclusive com o corpo possuido ou preso na espera do torneio (`GameServer.SairDoTranse`).
/// ====================================================================================
/// </summary>
public partial class SaidaDaMente : CanvasLayer
{
	public static SaidaDaMente? Instancia { get; private set; }

	private PanelContainer _faixa = null!;
	private Button _botao = null!;
	private string _teclaNoBotao = "";

	/// <summary>O verbo da saida. Um nome so pras duas portas daqui (o botao e a tecla de meditar).</summary>
	public const string Verbo = "mente_sair";

	/// <summary>Pede ao servidor pra acordar. E o que o botao e a tecla de meditar (dentro da mente) fazem.</summary>
	public static void Pedir() => GameClient.Instance?.SendVerbo(Verbo);

	/// <summary>O cliente esta numa mente agora? A pergunta que decide a faixa, a tecla e a queda da atividade.</summary>
	public static bool NaMente => GameClient.Instance is { } cli && DimensaoMental.EhAMente(cli.Zone);

	public override void _EnterTree() => Instancia = this;
	public override void _ExitTree() { if (Instancia == this) Instancia = null; }

	public override void _Ready()
	{
		Layer = 6;   // por cima do HUD; por baixo das telas que cobrem tudo (pausa, carregamento)

		var ancora = new CenterContainer
		{
			AnchorRight = 1,
			OffsetTop = 8,
			MouseFilter = Control.MouseFilterEnum.Ignore,   // so o botao pega clique: o resto e mundo
		};
		Tema.Aplicar(ancora);
		AddChild(ancora);

		_faixa = Tema.Painel1(8);
		_faixa.Visible = false;
		ancora.AddChild(_faixa);

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 10);
		_faixa.AddChild(linha);

		var titulo = new Label { Text = "MEDITAÇÃO PROFUNDA", VerticalAlignment = VerticalAlignment.Center };
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		linha.AddChild(titulo);

		_botao = new Button
		{
			// NUNCA O FOCO DO TECLADO: um botao focado engole o Espaco e o Enter, que la dentro sao o soco e o chat.
			FocusMode = Control.FocusModeEnum.None,
			TooltipText = "Abre os olhos e volta pro seu corpo, como ele estava ao entrar.",
		};
		_botao.Pressed += Pedir;
		linha.AddChild(_botao);
		RotularOBotao();
	}

	public override void _Process(double delta)
	{
		bool naMente = NaMente;
		if (_faixa.Visible != naMente) _faixa.Visible = naMente;
		if (naMente) RotularOBotao();
	}

	/// <summary>O botao diz a tecla que faz a mesma coisa -- a de meditar, que o jogador pode ter trocado.</summary>
	private void RotularOBotao()
	{
		Key[] teclas = Teclas.Teclado("meditate");
		string tecla = teclas.Length > 0 ? Teclas.Nome(teclas[0]) : "";
		if (tecla == _teclaNoBotao && _botao.Text.Length > 0) return;
		_teclaNoBotao = tecla;
		_botao.Text = tecla.Length > 0 ? $"Acordar ({tecla})" : "Acordar";
	}

	// =====================================================================
	// O QUE A BANCADA `--diagtranse` PERGUNTA
	// =====================================================================
	/// <summary>A faixa esta na tela?</summary>
	internal bool NaTela => _faixa.Visible;

	/// <summary>O texto do botao, como o jogador o le.</summary>
	internal string TextoDoBotao => _botao.Text;

	/// <summary>Aperta o botao, como o dedo do jogador.</summary>
	internal void ApertarDeTeste() => _botao.EmitSignal(BaseButton.SignalName.Pressed);
}
