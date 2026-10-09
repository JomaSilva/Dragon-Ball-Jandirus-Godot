using Godot;

namespace Jandirus.Client;

/// <summary>
/// A GOTA NA TELA: a ondulacao de tela cheia da entrada e da saida do transe.
///
/// *"faca um SHADER legal q deixa a TELA ONDULANDO IGUAL UMA GOTA quando cai na agua quando a
/// pessoa vai entrar na meditacao profunda [...] e quando ele DERROTAR O CLONE dele tb faca isso
/// mas pra VOLTAR pro mundo real, pq atualmente a transicao ta MT RAPIDA E MT SECA sem efeito
/// nenhum."*
///
/// ============================ E O MESMO HOSPEDEIRO DA NEVOA, DE PROPOSITO ============================
/// `CanvasLayer` na camada **0** + um `ColorRect` de tela cheia com um shader que le
/// `hint_screen_texture`. Nao ha um segundo jeito de fazer efeito de tela neste cliente, e nao devia
/// haver: quem inventasse o segundo repetiria a queixa que o primeiro ja levou do dono --
/// *"a gui ta ficando toda com efeito, deveria ser tudo menos a GUI"*. Zero e a camada do MUNDO;
/// interface comeca no 1 e fica intacta. Ver o quadro de camadas em <see cref="NevoaDeAltitude"/>.
///
/// As duas podem estar no ar ao mesmo tempo (ondular voando alto) e isso compoe sozinho: a segunda a
/// ser desenhada le o resultado da primeira.
/// ==================================================================================================
///
/// ============================ NAO HA BIT DE "ESTOU ONDULANDO" ============================
/// O pedido foi explicito: *"Interrompeu, a onda morre [...] **Derive** de 'estou em transicao' em
/// vez de guardar um bit que alguem tenha que apagar."*
///
/// Aqui a onda E o relogio: <see cref="Ondulando"/> e `_resta > 0` e nada mais. Ninguem "desliga" a
/// gota -- ela acaba. Quem precisa corta-la no meio (o servidor, quando a viagem e cancelada) manda
/// o MESMO canal de efeito com `ms = 0`, e o unico efeito disso e zerar o relogio. Nao existe estado
/// que possa ficar aceso sem dono.
/// ====================================================================================
///
/// CUSTO: um passe de tela cheia enquanto ela dura, e nada quando nao dura. Ver
/// <see cref="_Process"/> -- o `ColorRect` sai da composicao (`Visible = false`) fora da onda, que e
/// o que de fato importa: um shader com `hint_screen_texture` VISIVEL obriga o renderizador a copiar
/// o framebuffer todo quadro, e essa copia e o custo real (a conta por pixel e um `texture()` e umas
/// duas dezenas de operacoes). Invisivel, ele nao copia nada.
/// </summary>
public partial class GotaNaTela : CanvasLayer
{
	/// <summary>A camada do MUNDO. Ver o cabecalho -- a interface nao ondula.</summary>
	private const int Camada = 0;

	private ColorRect? _pano;
	private ShaderMaterial? _mat;

	/// <summary>Quanto falta da onda, em segundos. Zero = nao ha onda. E o estado inteiro.</summary>
	private double _resta;

	/// <summary>Quanto a onda dura ao todo -- o divisor que transforma o resto em fase 0..1.</summary>
	private double _total;

	/// <summary>ESTOU ONDULANDO? Derivado do relogio; nao ha campo pra isso.</summary>
	public bool Ondulando => _resta > 0;

	/// <summary>
	/// A FASE QUE O SHADER ESTA RECEBENDO AGORA (0 = impacto, 1 = morreu). So pras bancadas -- e ela
	/// e o valor ESCRITO no uniform, e nao um espelho calculado do lado de fora: a licao registrada
	/// deste port e que "uniform escrito" ja e um degrau mais perto do pixel que "campo do C#".
	/// </summary>
	public float FaseNaTela { get; private set; }

	/// <summary>O arquivo do shader. Publico pra o `Aquecimento` po-lo na fila de carga do lobby -- ver <see cref="Ensaiar"/>.</summary>
	public const string CaminhoDoShader = "res://Assets/Shaders/Gota.gdshader";

	public override void _Ready()
	{
		Layer = Camada;

		if (NovoPano() is not { } novo) return;
		(_pano, _mat) = novo;
		_pano.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_pano);
	}

	/// <summary>
	/// O PANO DA GOTA: o retangulo e o material dele, como a tela os usa -- invisivel ate a primeira onda. UMA receita pra
	/// duas casas, a da tela (<see cref="_Ready"/>) e a do palco do ensaio (<see cref="Ensaiar"/>): e isso que faz a
	/// pipeline montada no ensaio ser a que a primeira gota acha pronta. Nulo se o shader nao carregou.
	/// </summary>
	private static (ColorRect Pano, ShaderMaterial Tinta)? NovoPano()
	{
		var sh = GD.Load<Shader>(CaminhoDoShader);
		if (sh == null) { GD.PushWarning("[gota] Gota.gdshader nao carregou"); return null; }

		var tinta = new ShaderMaterial { Shader = sh };
		var pano = new ColorRect
		{
			Name = "Gota",
			Material = tinta,
			Visible = false,
			// ELE NAO PODE ROUBAR O CLIQUE -- o mesmo cuidado (e o mesmo motivo) da nevoa: o
			// retangulo cobre a tela inteira, e mirar com o mouse tem que continuar funcionando.
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		return (pano, tinta);
	}

	/// <summary>O lado do pano do ensaio, em pixels do palco: a pipeline e do shader e do jeito de desenhar, e nao do tamanho.</summary>
	private const float PanoDoEnsaio = 32f;

	/// <summary>
	/// O ENSAIO DO LOBBY (ver `Aquecimento.AtosDaGota`): o pano da gota, pela receita de producao (<see cref="NovoPano"/>),
	/// ACESO num palco fora da tela -- pra o shader ser compilado e a pipeline dele montada ALI, e nao no quadro em que a
	/// primeira onda do processo comeca.
	///
	/// ============================ O QUE ISTO TIRA DA ENTRADA NO TRANSE ============================
	/// Esta classe so nasce na primeira onda do processo (`World.AoCairEfeito`), e o shader dela nem na fila de carga do
	/// aquecimento estava: era lido do disco, compilado e desenhado no mesmo quadro -- o da entrada na meditacao profunda.
	/// MEDIDO em 2026-10-09 pela `--diagestouro --avulsos`, o quadro da primeira onda, em relogio:
	///
	///     com o driver de video FRIO .......... 52 a 59 ms   script 8 a 11, a espera pelo shader 21 a 24, a pipeline 19 a 28
	///     com o driver quente ................. 34 a 35 ms   so a espera: e o cache de shader do Godot vazio, o de quem abre
	///                                                        o jogo pela primeira vez
	///     a gemea ILUMINADA, driver frio ...... 61 a 64 ms   58 a 61 deles a pipeline do item COM luz, montada depois da sem luz
	///
	/// A GEMEA EXISTE PORQUE O PANO E ALCANCADO PELAS LUZES DO MUNDO: ele mora numa `CanvasLayer` de camada 0, que e a faixa
	/// de camadas das luzes -- basta a aura de uma forma, um tiro de ki ou uma fogueira na tela, de noite. Quem meditava
	/// de dia pagava o primeiro quadro; na primeira onda com uma luz na tela, pagava o outro, maior.
	///
	/// DEPOIS: nenhuma pipeline nasce na primeira onda, com luz ou sem ela, e o quadro custa 9 a 10 ms (8 a 10 o da gemea),
	/// com o driver quente ou frio. O QUE O ATO NAO PAGA: os 7 ms de script da `GotaNaTela` nascendo e do codigo dela
	/// rodando pela primeira vez -- o palco tem o pano, e nao o node.
	/// =============================================================================================
	///
	/// NO MEIO DA ONDA (`fase` 0,3) e SEM RELOGIO: quem anda a fase e o <see cref="_Process"/> de uma `GotaNaTela` viva, e
	/// aqui nao ha uma. Morre com o palco do aquecimento.
	/// </summary>
	public static void Ensaiar(Node2D pai, Vector2 onde)
	{
		if (NovoPano() is not { } novo) return;
		(ColorRect pano, ShaderMaterial tinta) = novo;
		pano.Name = "GotaDoEnsaio";
		pano.Size = new Vector2(PanoDoEnsaio, PanoDoEnsaio);
		pano.Position = onde - pano.Size * 0.5f;
		pano.Visible = true;
		tinta.SetShaderParameter("fase", 0.3f);
		pai.AddChild(pano);
	}

	/// <summary>
	/// A GOTA CAIU: ondula por <paramref name="segundos"/>.
	///
	/// Chamar de novo REINICIA em vez de somar -- duas ondas empilhadas nao existem no lago, e a
	/// segunda entrada (um pacote repetido, um duplo clique) tem que parecer a primeira.
	/// </summary>
	public void Cair(double segundos)
	{
		if (segundos <= 0) { Parar(); return; }
		_total = segundos;
		_resta = segundos;
		Aplicar(0f);
		if (_pano != null) _pano.Visible = true;
	}

	/// <summary>
	/// CORTA A ONDA AGORA. E o "interrompeu, a onda morre": o servidor manda o mesmo efeito com
	/// `ms = 0` quando a viagem deixa de acontecer (nocaute, morte, o outro sumindo, o golpe que
	/// arranca do transe antes da hora).
	/// </summary>
	public void Parar()
	{
		_resta = 0;
		_total = 0;
		Aplicar(0f);
		if (_pano != null) _pano.Visible = false;
	}

	public override void _Process(double delta)
	{
		if (_resta <= 0) return;

		_resta -= delta;
		if (_resta <= 0) { Parar(); return; }

		// A FASE E O QUE JA PASSOU, e ela e sempre derivada dos dois relogios -- nao ha um terceiro
		// numero acumulando erro.
		Aplicar((float)(1.0 - _resta / _total));
	}

	private void Aplicar(float fase)
	{
		FaseNaTela = fase;
		if (_mat == null) return;

		_mat.SetShaderParameter("fase", fase);

		// A PROPORCAO E LIDA TODO QUADRO e nao no nascimento: a janela deste jogo e redimensionavel
		// (e as bancadas a redimensionam), e um anel calibrado pra 16:9 vira uma elipse deitada no
		// instante em que alguem arrasta a borda.
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		if (tela.Y > 0.5f) _mat.SetShaderParameter("proporcao", tela.X / tela.Y);
	}
}
