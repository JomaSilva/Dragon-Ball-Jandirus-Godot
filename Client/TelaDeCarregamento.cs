using Godot;

namespace Jandirus.Client;

/// <summary>
/// A TELA DE CARREGAMENTO da troca de mapa.
///
/// ============================ O QUE ELA RESOLVE, E O QUE NAO ============================
/// Ela NAO deixa a troca mais rapida, e e importante dizer isso em vez de fingir. O custo de
/// entrar num planeta esta na montagem do tilemap (medido: 758 ms so na Terra), e esse trabalho
/// acontece na thread principal, dentro do renderizador -- nao ha como joga-lo pra outra thread
/// nem parcela-lo sem reescrever como o mapa e desenhado.
///
/// O que ela resolve e outra coisa, e nao e pouca: hoje a janela simplesmente PARA. O jogo fica
/// sem responder, o depurador congela junto, e do lado de fora isso e indistinguivel de um
/// travamento -- o dono descreveu exatamente assim, "como se tudo congelasse".
///
/// Com a tela no ar antes do trabalho comecar, o mesmo segundo passa a ser um carregamento
/// anunciado. O jogador sabe o que esta acontecendo e para onde esta indo.
/// =========================================================================================
///
/// ============================ O TRUQUE E DEIXAR UM QUADRO PASSAR ============================
/// Mostrar a tela e chamar a carga na mesma passada nao mostra nada: o quadro so vai pra tela no
/// fim do laco, e a carga bloqueia antes disso -- a tela apareceria DEPOIS de tudo, por um
/// instante, o que e pior que nao ter.
///
/// Por isso o `Cobrir` espera dois `ProcessFrame` antes de soltar o trabalho. Dois e nao um: o
/// primeiro fecha o quadro em que a tela foi criada, o segundo garante que ele foi desenhado.
/// =============================================================================================
/// </summary>
public partial class TelaDeCarregamento : CanvasLayer
{
	public static TelaDeCarregamento? Instancia { get; private set; }

	private Control _raiz = null!;
	private Label _titulo = null!, _dica = null!;

	public override void _Ready()
	{
		Instancia = this;
		Layer = 90;   // acima de tudo menos o menu de pause; ela cobre o mundo de proposito
		Montar();
	}

	public override void _ExitTree() { if (Instancia == this) Instancia = null; }

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// OPACO, e nao um veu: a metade do mundo que ja foi descarregada apareceria por baixo, e
		// meio planeta desenhado e mais estranho que tela cheia.
		var fundo = new ColorRect { Color = new Color(0.04f, 0.05f, 0.08f), AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(fundo);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		var caixa = new VBoxContainer();
		caixa.AddThemeConstantOverride("separation", 10);
		centro.AddChild(caixa);

		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 28);
		_titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		caixa.AddChild(_titulo);

		_dica = new Label { Text = "carregando...", HorizontalAlignment = HorizontalAlignment.Center };
		_dica.AddThemeColorOverride("font_color", Tema.TextoFraco);
		caixa.AddChild(_dica);
	}

	/// <summary>
	/// POE A TELA NO AR e so entao roda o trabalho pesado.
	///
	/// O `async void` aqui e deliberado e nao um descuido: quem chama e um manipulador de evento de
	/// rede, que nao tem como esperar. O metodo devolve na hora, a tela sobe, e o resto acontece
	/// dois quadros depois -- ver o cabecalho da classe.
	/// </summary>
	public async void Cobrir(string destino, Action trabalho)
	{
		_titulo.Text = destino;
		_raiz.Visible = true;

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// O TRABALHO PROTEGIDO: se ele estourar, a tela TEM que sair. Uma excecao no meio da carga
		// com a tela presa no ar deixaria o jogador olhando "carregando..." pra sempre, sem nem
		// saber que algo quebrou.
		try { trabalho(); }
		finally
		{
			// MAIS UM QUADRO ANTES DE SUMIR. O trabalho termina, mas o primeiro quadro do mapa novo
			// ainda nao foi desenhado -- tirar a tela agora mostraria o congelamento que ela veio
			// esconder, so que no fim em vez de no comeco.
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			_raiz.Visible = false;
		}
	}
}
