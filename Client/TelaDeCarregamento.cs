using Godot;

namespace Jandirus.Client;

/// <summary>
/// A TELA DE CARREGAMENTO -- da troca de mapa **e da entrada no mundo**.
///
/// ============================ AS DUAS PORTAS SAO DIFERENTES, E POR ISSO SAO DOIS METODOS ============================
/// <see cref="Cobrir"/> serve a TROCA DE MAPA: ali o trabalho e uma chamada sincrona que da pra
/// embrulhar -- mostra, deixa dois quadros passarem, roda, some.
///
/// <see cref="Levantar"/> + <see cref="Soltar"/> servem a ENTRADA NO MUNDO, que nao cabe naquele
/// molde: entre o clique em "Entrar no mundo" e o corpo desenhado ha uma IDA E VOLTA DE REDE. Nao
/// existe um `Action` pra embrulhar; existe um clique aqui e um `JoinAccepted` chegando depois.
/// Entao a tela sobe no clique (`Levantar`) e cai quando o mundo foi DESENHADO (`Soltar`).
///
/// O pedido do dono foi literalmente este caminho: *"quando terminar de criar o personagem aparecer
/// uma tela de loading ate o personagem spawnar etc, pq fica uns 1 a 2 segundos na tela azul do
/// byond ate carregar pela primeira vez"*. A "tela azul" era o `ColorRect` de `Tema.Fundo` que o
/// `Boot.MontarLogin` deixa vivo por baixo de tudo -- medido: 1462 ms olhando um retangulo liso.
/// ====================================================================================================================
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
		Escrever(destino, "carregando...");
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

	private void Escrever(string titulo, string dica)
	{
		_titulo.Text = titulo;
		_dica.Text = dica;
	}

	/// <summary>A tela esta no ar AGORA? Pra bancada e pra nao levantar duas vezes.</summary>
	public bool NoAr => _raiz.Visible;

	/// <summary>
	/// SOBE A TELA E FICA -- a metade de ENTRADA NO MUNDO. Ver o cabecalho da classe.
	///
	/// Sincrona e sem `await` nenhum de proposito: quem chama e o manipulador do clique, e o clique
	/// acontece no processamento de ENTRADA do quadro, antes do desenho dele. Ou seja, a tela ja
	/// esta no ar no MESMO quadro em que o botao foi apertado -- nao ha um so quadro entre o clique
	/// e a cobertura, que e exatamente onde o fundo chapado aparecia.
	///
	/// Quem a derruba e o <see cref="Soltar"/>, e so ele.
	/// </summary>
	public void Levantar(string titulo, string dica)
	{
		Escrever(titulo, dica);
		_raiz.Visible = true;
	}

	/// <summary>
	/// DERRUBA A TELA -- mas so DEPOIS DE UM QUADRO DESENHADO. Ver o cabecalho da classe.
	///
	/// ============================ SAIR POR FATO, NUNCA POR RELOGIO ============================
	/// A tentacao aqui e um temporizador ("some depois de 1,5 s"), e ele erraria dos dois lados: o
	/// jogador de maquina lenta veria a tela sair antes do mundo, e o de maquina rapida ficaria
	/// olhando carregamento com o jogo pronto atras. Pior: neste projeto ja se mediu a APARENCIA do
	/// servidor chegando ate 6 s antes do pixel -- "o servidor disse que existe" tambem nao serve.
	///
	/// O fato certo e `frame_post_draw`: ele e emitido depois que o quadro foi DESENHADO. Quem chama
	/// este metodo esta dentro do `_process` do quadro em que o mundo inteiro foi montado; esperar um
	/// `frame_post_draw` daqui e esperar exatamente aquele quadro -- o primeiro com o corpo na tela,
	/// o mesmo que paga a montagem do cenario e a compilacao dos shaders.
	///
	/// E POR ISSO NAO HA BURACO: a tela so e retirada DEPOIS que um quadro com o mundo ja foi
	/// desenhado por baixo dela. O quadro seguinte troca cobertura por jogo, sem um so quadro de
	/// fundo chapado no meio -- que e o defeito que o dono descreveu.
	/// ==========================================================================================
	/// </summary>
	public async void Soltar()
	{
		if (!_raiz.Visible) return;

		// GUARDA CONTRA DUAS SOLTURAS: duas corrotinas esperando o mesmo sinal derrubariam a tela
		// duas vezes -- inofensivo hoje, e um jeito de a segunda apagar uma cobertura que a primeira
		// ja tinha levantado de novo (entrar, sair pro login, entrar de novo depressa).
		if (_soltando) return;
		_soltando = true;

		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

		_soltando = false;
		_raiz.Visible = false;
	}

	private bool _soltando;
}
