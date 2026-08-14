using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O MAPA DA GALAXIA -- desenhado, com zoom, arrasto e clique.
///
/// ============================ POR QUE UM MAPA, E NAO UMA LISTA ============================
/// A aba Nav mostrava uma lista dos corpos celestes das tres chunks em volta: no maximo um punhado
/// de nomes, sem nocao nenhuma de ONDE eles estao. Isso responde "o que ha por perto" e nao
/// responde a pergunta que um mapa estelar existe pra responder -- "pra onde eu vou".
///
/// O pedido do dono foi direto: "faca um mapa do espaco com todos os planetas que o servidor sabe
/// onde estao, e esse minimapa e interativo... voce clica nos planetas e seleciona viajar... e voce
/// pode dar zoom e zoom out". E o `Nav System` do original, que o `Chronology.dm` descreve como
/// "o mapa da galaxia".
/// ==========================================================================================
///
/// ============================ DE ONDE VEM CADA PLANETA ============================
/// Nao ha pacote de rede nenhum aqui, e nao precisa haver: o universo e uma FUNCAO PURA da seed do
/// mundo (<see cref="Sistemas.Do"/>), entao o cliente chega exatamente a mesma resposta que o
/// servidor sem trocar um byte. E o mesmo principio que ja desenha o ceu.
///
///   * OS SETE PRE-FEITOS (`Espaco.PreFeitos`) aparecem SEMPRE, em qualquer zoom. Sao os mundos
///     com mapa proprio -- os que "o servidor sabe onde estao" no sentido forte.
///   * OS PROCEDURAIS aparecem quando o zoom chega perto o bastante pra varredura valer a pena.
///     Nao e economia de preguica: a galaxia inteira tem CENTENAS DE MILHARES de planetas (ate 10
///     por sistema, um sistema a cada 32x32 chunks), e desenhar tudo de uma vez seria um borrao
///     cinza que nao ajuda ninguem a escolher destino.
/// ==================================================================================
///
/// VIAJAR E O PILOTO AUTOMATICO QUE JA EXISTE (`LocalPlayer.Destino`): ele preenche a direcao que
/// as teclas preencheriam, no passo normal, e o servidor valida cada passo como valida qualquer
/// outro. Nao ha teleporte -- Terra a Namek continua custando sete dias in-game.
/// </summary>
public partial class MapaEstelar : Control
{
	// =====================================================================
	// ESTADO DA CAMERA
	// =====================================================================
	/// <summary>O ponto do MUNDO que esta no centro do widget.</summary>
	private Vector2 _centro;

	/// <summary>Pixels de TELA por pixel de MUNDO. Cresce ao aproximar.</summary>
	private float _escala = 1f / 4000f;

	/// <summary>
	/// Os limites do zoom.
	///
	/// O de perto poe uma chunk inteira (2048 px) em ~300 px de tela, que e onde da pra ver a
	/// diferenca entre dois planetas vizinhos.
	///
	/// ============================ POR QUE O DE LONGE ABRIU 3,3x ============================
	/// Era 1/12000, "cabe os sete pre-feitos com folga", e isso bastava enquanto a carta so tinha
	/// esses sete pontos: ali fora nao havia mais nada pra ver.
	///
	/// Agora ha. Com a galaxia desenhada em estrelas, afastar continua trazendo informacao, e o
	/// limite de 1/12000 tinha virado uma parede que impedia justamente a visao que a camada de
	/// sistemas passou a oferecer.
	///
	/// E ele tem uma segunda funcao, que a bancada exigiu: e por ELE que o teto de
	/// <see cref="CelulasMax"/> consegue disparar. Com 1/12000 o enquadramento mais aberto possivel
	/// cobria ~6.600 celulas, ou seja o teto ficava permanentemente abaixo do alcance -- um teto que
	/// nunca dispara e indistinguivel de teto nenhum (Parte 3, item 3). Em 1/40000 o zoom minimo
	/// cobre dezenas de milhares e o corte acontece de verdade, com a carta voltando ao esqueleto.
	/// ====================================================================================
	/// </summary>
	private const float EscalaMin = 1f / 40000f;
	private const float EscalaMax = 0.15f;

	/// <summary>Quanto cada passo de roda multiplica o zoom.</summary>
	private const float PassoDeZoom = 1.25f;

	private bool _arrastando;
	private Vector2 _ultimoMouse;

	/// <summary>O planeta clicado. E ele que o botao "Viajar" usa.</summary>
	public PlanetaNoEspaco? Selecionado { get; private set; }

	/// <summary>
	/// O SISTEMA clicado. Existe junto com <see cref="Selecionado"/> e nao no lugar dele: clicar num
	/// mundo tambem seleciona o sistema dele, porque o destino de uma viagem e sempre um corpo mas o
	/// que se ABRE e sempre um sistema.
	/// </summary>
	public SistemaSolar? SistemaSelecionado { get; private set; }

	/// <summary>Avisa a aba que a selecao mudou -- e ela quem desenha o painel do destino.</summary>
	public event Action? SelecaoMudou;

	/// <summary>Duplo clique num planeta: viajar direto, sem passar pelo botao.</summary>
	public event Action<PlanetaNoEspaco>? PediuViagem;

	/// <summary>
	/// DUPLO CLIQUE NUM SISTEMA: abre a tela dele.
	///
	/// E o pedido do dono, literal -- "ao clicar DUAS VEZES no sistema vc vai abrir o MAPA DO
	/// SISTEMA". O mesmo gesto viaja quando o alvo e um MUNDO e entra quando o alvo e um SISTEMA,
	/// porque as duas coisas sao "o passo seguinte" pro que esta debaixo do cursor.
	/// </summary>
	public event Action<SistemaSolar>? PediuSistema;

	// =====================================================================
	// O CACHE DA VARREDURA
	// =====================================================================
	/// <summary>
	/// Um BLOCO de 32x32 chunks. A varredura acontece por bloco, e nao por chunk, por uma razao
	/// simples: mover o mapa um pixel muda o conjunto de chunks visiveis, mas quase nunca muda o
	/// conjunto de BLOCOS -- entao o cache acerta em vez de revarrer a cada quadro.
	/// </summary>
	private const int ChunksPorBloco = 32;

	/// <summary>
	/// QUANTOS BLOCOS DA PRA VARRER PROCURANDO PLANETA.
	///
	/// Cada bloco custava 1024 chamadas de `PlanetaDaChunk`; hoje custa UMA de <see cref="Sistemas.Do"/>
	/// (90 ns medidos), porque o bloco virou a celula do sistema. O teto deixou de ser sobre custo e
	/// passou a ser sobre LEGIBILIDADE: acima de sessenta blocos o resultado seriam milhares de
	/// pontos a menos de um pixel um do outro.
	///
	/// ATENCAO -- ele NAO governa mais o que a carta mostra, so os PLANETAS. Ver <see cref="CelulasMax"/>.
	/// </summary>
	private const int BlocosVisiveisMax = 60;

	/// <summary>
	/// QUANTAS CELULAS DA PRA VARRER PROCURANDO SISTEMA -- e por que este teto e cem vezes maior.
	///
	/// ============================ O PONTINHO VIROU O SISTEMA ============================
	/// Antes, longe o bastante, a carta mostrava so os sete pre-feitos e o resto era preto -- e o
	/// jogador nao tinha como saber que havia mais universo ali. Isso era consequencia do teto de
	/// PLANETAS: enumerar planetas custa a montagem do nome de cada corpo (~5 us por sistema
	/// medidos na bancada), entao sessenta celulas ja era o limite.
	///
	/// Uma ESTRELA nao custa isso. `Sistemas.Do` e 90 ns e nao toca em planeta nenhum, entao a carta
	/// consegue desenhar a galaxia inteira do enquadramento aberto: 9.000 celulas custam 0,81 ms, e
	/// a varredura so refaz quando a camera anda. E exatamente o que o dono pediu -- "cada pontinho
	/// e um SISTEMA".
	///
	/// O NUMERO SAIU DA MEDIDA E NAO DO GOSTO: o "ver tudo" (os sete pre-feitos enquadrados, que e o
	/// enquadramento mais aberto que alguem usa de verdade) cobre ~20.000 celulas numa janela de
	/// 1152 px. O teto tem que ficar acima disso, senao a carta abriria vazia na propria tela
	/// inicial dela.
	/// ==================================================================================
	/// </summary>
	private const int CelulasMax = 30_000;

	/// <summary>
	/// QUANTOS PIXELS DE TELA UMA CELULA PRECISA TER pra valer a pena desenhar a estrela dela.
	///
	/// ============================ ESTE E O TETO QUE IMPORTA, E POR QUE ELE NAO E UMA CONTAGEM ============================
	/// Uma contagem de celulas depende do TAMANHO DA JANELA: o mesmo zoom cobre tres vezes mais
	/// celulas num monitor largo, entao um teto por contagem dispara pra um jogador e nao dispara
	/// pro outro -- e nenhum dos dois consegue reproduzir o que o outro ve. Ceto que depende de
	/// hardware nao e teto, e o problema tambem nao e esse: e que abaixo de tres pixels por celula
	/// as estrelas se encostam e a galaxia vira uma mancha cinza uniforme, que e exatamente o borrao
	/// que o cabecalho deste arquivo ja dizia querer evitar.
	///
	/// Medido: "ver tudo" da 4,4 px por celula (desenha), e o zoom minimo da 1,6 px (nao desenha).
	/// A contagem continua existindo como guarda de CUSTO, mas quem manda no que se ve e este.
	/// =============================================================================================================
	/// </summary>
	private const float CelulaMinimaPx = 3f;

	// =====================================================================
	// A MAGNITUDE LIMITE -- por que a carta nao mostra TODA estrela
	// =====================================================================
	/// <summary>
	/// QUANTAS ESTRELAS A CARTA DESENHA DE UMA VEZ.
	///
	/// ============================ O CUSTO NAO ESTAVA ONDE EU MEDI ============================
	/// Eu media a VARREDURA (`Sistemas.Do`, 90 ns) e concluia que 20.000 celulas cabiam num quadro.
	/// Cabem: a varredura leva 1,8 ms. O que nao cabe e o DESENHO -- cada estrela sao dois
	/// `DrawCircle`, e um `DrawCircle` do Godot e um poligono de ~24 vertices. Vinte mil estrelas sao
	/// 1,2 MILHAO de vertices por quadro, e a bancada travou por dez minutos sem chegar ao fim.
	///
	/// E a armadilha da Parte 3, item 2, outra vez: "custo fora da janela medida". Medir a funcao que
	/// eu escrevi e nao a consequencia dela.
	/// =====================================================================================
	///
	/// A saida nao e desligar a galaxia: e a MAGNITUDE LIMITE, que e o que qualquer carta estelar de
	/// papel faz -- longe, so as brilhantes; aproximando, as fracas aparecem. Ver
	/// <see cref="MagnitudeLimite"/>.
	/// </summary>
	private const int SistemasDesenhadosAlvo = 1500;

	/// <summary>
	/// A ESCADA DE MAGNITUDE: raio letal minimo, e que fatia da galaxia sobrevive a ele.
	///
	/// As fracoes sao os PESOS de `Sistemas.Tabela` somados do maior raio pro menor (2%+9% de
	/// gigantes no teto, 5% de gigante branca, 8% de azul, 12% de branca, 16% de amarela, 18% de
	/// laranja, 30% de ana vermelha). Sao a distribuicao de verdade e nao um chute, entao a
	/// contagem que sai daqui bate com o que a varredura devolve.
	/// </summary>
	private static readonly (float raio, double fracao)[] Magnitudes =
	[
		(2048, 0.11), (1750, 0.16), (1350, 0.24), (980, 0.36), (700, 0.52), (520, 0.70), (360, 1.00),
	];

	/// <summary>
	/// O RAIO MINIMO DE ESTRELA que este enquadramento desenha.
	///
	/// Procura da mais fraca pra mais forte e para na primeira que cabe no orcamento. Se nem so as
	/// gigantes cabem, mostra so as gigantes e aceita passar do alvo -- e melhor uma carta com o
	/// dobro de pontos do que uma carta vazia.
	/// </summary>
	private static float MagnitudeLimite(long celulas)
	{
		for (int i = Magnitudes.Length - 1; i >= 0; i--)
			if (celulas * Magnitudes[i].fracao <= SistemasDesenhadosAlvo) return Magnitudes[i].raio;
		return Magnitudes[0].raio;
	}

	/// <summary>
	/// O SISTEMA DE CADA CELULA, guardado a parte dos planetas dela.
	///
	/// Duas tabelas e nao uma porque os dois custos sao de ordens diferentes: o sistema e barato e
	/// entra sempre, o planeta e caro e so entra no zoom fechado. Guardar os dois juntos obrigaria a
	/// pagar o caro pra ter o barato.
	///
	/// Guarda o NULO tambem, e desde o `Sistemas.VaziosPor256` isso deixou de ser um detalhe: 37,5%
	/// das celulas nao tem sistema nenhum (mais as raras anuladas por ancorado). Sem guardar o nulo,
	/// mais de um terco do enquadramento seria re-hasheado a cada quadro pra dar nulo de novo.
	/// </summary>
	private static readonly Dictionary<(int X, int Y), SistemaSolar?> _celulas = [];

	private static readonly Dictionary<(int X, int Y), List<PlanetaNoEspaco>> _cache = [];
	private static ulong _seedDoCache;

	/// <summary>
	/// TETO DO CACHE. Passar o mapa de ponta a ponta num zoom fechado visita milhares de blocos, e
	/// nenhum deles e jogado fora sozinho -- um cache sem teto e um vazamento com outro nome. Ao
	/// estourar, esvazia inteiro: revarrer o enquadramento de agora custa alguns quadros e nada
	/// mais, e um LRU aqui seria mais codigo do que o problema merece.
	/// </summary>
	private const int BlocosNoCacheMax = 4000;

	/// <summary>Quantos blocos varrer POR QUADRO. O resto espera o proximo -- o mapa nao trava.</summary>
	private const int BlocosPorQuadro = 6;

	/// <summary>
	/// Teto do cache de celulas. Bem maior que o de planetas porque cada entrada e uma struct e nao
	/// uma lista, e porque um zoom aberto ja visita milhares de uma vez.
	/// </summary>
	private const int CelulasNoCacheMax = 40_000;

	// =====================================================================
	// CICLO
	// =====================================================================
	public override void _Ready()
	{
		// PARA O EVENTO AQUI. Este widget mora dentro do ScrollContainer do menu: sem `Stop`, o
		// arrasto arrastaria a rolagem e o clique vazaria pro que estiver atras.
		//
		// `Stop` NAO segura a roda do mouse, e a correcao dela mora no `_GuiInput` -- ver o bloco
		// comprido no braco `WheelUp`/`WheelDown`, que explica por que o freio da roda e outro.
		MouseFilter = MouseFilterEnum.Stop;
		// ALTO O BASTANTE PRA SER CARTA, baixo o bastante pra o painel do destino caber embaixo
		// sem rolagem. Medido na tela: com 380 o botao "Viajar" ficava fora da area visivel.
		CustomMinimumSize = new Vector2(0, 330);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		FocusMode = FocusModeEnum.Click;

		// O ENQUADRAMENTO DEPENDE DO TAMANHO, e no `_Ready` o tamanho ainda e zero -- o container
		// so o resolve no proximo quadro. Enquadrar agora usaria a medida de reserva e o mapa
		// nasceria com o zoom errado; por isso a primeira vez acontece no `Resized`.
		Resized += () => { if (!_enquadrou) { _enquadrou = true; VerTudo(); } };
		VerTudo();
	}

	private bool _enquadrou;

	/// <summary>Redesenha todo quadro: o corpo do jogador se move, e o mapa mostra onde ele esta.</summary>
	public override void _Process(double delta) => QueueRedraw();

	/// <summary>
	/// ONDE EU ESTOU NA GALAXIA -- que NAO e onde o meu corpo esta.
	///
	/// A coordenada do corpo so vale como coordenada de galaxia NO ESPACO. Pousado, ela vira
	/// coordenada de superficie (o spawn da Terra e (7984, 8016), que na galaxia fica a 11 mil px
	/// da origem -- fora da propria Terra). Entao:
	///
	///   * no espaco  -> a posicao do corpo, que e a da nave;
	///   * num dos sete -> a posicao do PLANETA, que e exata e nao depende de memoria nenhuma;
	///   * num gerado -> de onde eu desci (`World.UltimaNoEspaco`), que e a unica pista que sobra:
	///     a posicao de um mundo sorteado sai da CHUNK dele, e o nome nao diz qual chunk e.
	/// </summary>
	public static Vector2? MinhaPosicaoNaGalaxia()
	{
		if (GameClient.Instance is not { } cli) return null;

		// ============================ A BORDO, "EU" SOU A NAVE ============================
		// O interior de uma Capital Ship nao fica em lugar nenhum do universo -- ele e uma
		// `ZoneKey.Interior`, sem chunk e sem planeta. Sem esta linha a escada abaixo cairia no
		// `UltimaNoEspaco`, que e de onde o jogador DESCEU pela ultima vez: alguem que embarcou na
		// Terra e viajou uma hora se veria na Terra.
		//
		// A resposta chega do servidor pelo "Observar" da ponte (`Protocol.S2C.Nave`), que e o unico
		// que sabe onde o casco esta -- o cliente nao recebe pacote nenhum da zona de fora.
		if (cli.NaveVista is { } nave) return nave.Pos;

		if (Espaco.EhEspaco(cli.Zone)) return World.Instancia?.PosicaoLocal;

		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, cli.Zone.Name, StringComparison.OrdinalIgnoreCase))
				return new Vector2(p.Pos.X, p.Pos.Y);

		return World.Instancia?.UltimaNoEspaco;
	}

	// =====================================================================
	// CONVERSAO
	// =====================================================================
	private Vector2 ParaTela(Vector2 mundo) => (mundo - _centro) * _escala + Size / 2f;
	private Vector2 ParaMundo(Vector2 tela) => (tela - Size / 2f) / _escala + _centro;

	// =====================================================================
	// ENTRADA
	// =====================================================================
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton b)
		{
			switch (b.ButtonIndex)
			{
				// ============================ A RODA DA ZOOM, E NAO ROLA A PAGINA ============================
				// O pedido do dono, literal: *"vc roda o scroll do mouse ele DESCE A TELA ao inves de dar
				// ZOOM, deveria dar PRIORIDADE PRO ZOOM ao inves de rolar a pagina"*.
				//
				// O zoom nunca deixou de funcionar -- o que faltava era o evento PARAR aqui. Medido, um
				// clique de roda pra baixo: a escala caiu o passo exato (0,00007662 -> 0,00006130) E o
				// `ScrollContainer` do menu rolou 52 px no mesmo quadro. O jogador via as duas coisas, e
				// 52 px de pagina andando ganham do olho contra 25% de zoom.
				//
				// O `MouseFilter = Stop` do `_Ready` NAO fecha isso: no Godot 4 todo Control nasce com
				// `mouse_force_pass_scroll_events = true`, uma excecao que existe pra rolagem ANINHADA
				// funcionar e que, de proposito, fura o `Stop` -- so pros eventos de roda.
				//
				// POR QUE `AcceptEvent()`, E POR QUE AQUI DENTRO:
				//   * `AcceptEvent` marca o evento como consumido no viewport, e essa marca e o UNICO
				//     freio que a subida pela arvore respeita ANTES de consultar o `force_pass`. Nao
				//     basta o `return`: o `return` sai do MEU tratador, nao da subida.
				//   * no braco da roda, e nao no `_Ready` (`MouseForcePassScrollEvents = false` tambem
				//     resolveria): assim eu engulo so o evento que eu REALMENTE tratei. Roda horizontal
				//     e gestos de pan, que este mapa nao trata, continuam subindo pra quem trata.
				//   * e JAMAIS no ScrollContainer: ele e o unico rolador vertical do menu P e rola TODAS
				//     as abas (Stats, Learning, Tech...). Consertar o mapa mexendo nele trocaria este
				//     defeito por um bem maior.
				//
				// A consequencia e a que o dono pediu: DENTRO do retangulo do mapa a roda so da zoom; um
				// pixel fora dele a pagina rola como sempre rolou.
				// ==========================================================================================
				case MouseButton.WheelUp when b.Pressed: Aproximar(PassoDeZoom, b.Position); AcceptEvent(); return;
				case MouseButton.WheelDown when b.Pressed: Aproximar(1f / PassoDeZoom, b.Position); AcceptEvent(); return;

				case MouseButton.Left when b.Pressed:
					GrabFocus();

					// O PLANETA GANHA DO SISTEMA quando os dois estao debaixo do cursor: no zoom em
					// que os mundos aparecem, o alvo que o jogador esta mirando e o mundo -- a
					// estrela ainda esta ali, mas ela nao e destino de viagem.
					PlanetaNoEspaco? clicado = PlanetaEm(b.Position);
					if (clicado != null)
					{
						// DUPLO CLIQUE VIAJA. E o mesmo gesto que ja marca alvo no mundo -- quem
						// aprendeu um aprendeu o outro.
						if (b.DoubleClick && Selecionado is { } s && s.Nome == clicado.Value.Nome)
						{
							PediuViagem?.Invoke(clicado.Value);
							return;
						}
						Selecionado = clicado;
						SistemaSelecionado = SistemaDe(clicado.Value);
						SelecaoMudou?.Invoke();
						return;
					}

					SistemaSolar? sis = SistemaEm(b.Position);
					if (sis != null)
					{
						// DUPLO CLIQUE NUM SISTEMA ENTRA NELE -- o pedido do dono, literal.
						if (b.DoubleClick && SistemaSelecionado is { } j && j.Id == sis.Value.Id)
						{
							PediuSistema?.Invoke(sis.Value);
							return;
						}
						SistemaSelecionado = sis;
						Selecionado = null;
						SelecaoMudou?.Invoke();
						return;
					}

					// clicou no vazio: comeca a arrastar
					_arrastando = true;
					_ultimoMouse = b.Position;
					return;

				case MouseButton.Left: _arrastando = false; return;
			}
		}

		if (e is InputEventMouseMotion m && _arrastando)
		{
			// ARRASTA O MAPA, e nao a camera: o mundo acompanha o dedo. Por isso a subtracao.
			_centro -= (m.Position - _ultimoMouse) / _escala;
			_ultimoMouse = m.Position;
			QueueRedraw();
		}
	}

	/// <summary>
	/// Zoom ANCORADO NO CURSOR: o ponto do mundo sob o mouse continua sob o mouse.
	///
	/// Sem a ancora, aproximar sempre puxa pro centro e o jogador perde de vista o que estava
	/// olhando -- ele aproxima pra ver um planeta e o planeta foge pra fora da tela.
	/// </summary>
	private void Aproximar(float fator, Vector2 ancoraNaTela)
	{
		Vector2 antes = ParaMundo(ancoraNaTela);
		_escala = Mathf.Clamp(_escala * fator, EscalaMin, EscalaMax);
		Vector2 depois = ParaMundo(ancoraNaTela);
		_centro += antes - depois;
		QueueRedraw();
	}

	public void Zoom(float fator) => Aproximar(fator, Size / 2f);

	/// <summary>Enquadra os sete mundos com mapa proprio. E o "de onde tudo se ve".</summary>
	public void VerTudo()
	{
		Vector2 min = Vector2.Inf, max = -Vector2.Inf;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			var v = new Vector2(p.Pos.X, p.Pos.Y);
			min = min.Min(v);
			max = max.Max(v);
		}
		_centro = (min + max) / 2f;

		Vector2 tam = Size.X > 1 ? Size : new Vector2(640, 380);
		// A MARGEM E MAIOR NA VERTICAL porque o NOME e desenhado ABAIXO do disco: com margem igual
		// nos dois eixos, o planeta do extremo de baixo (Icer) cabia e o rotulo dele saia da moldura.
		Vector2 span = (max - min) * new Vector2(1.25f, 1.5f);
		_escala = Mathf.Clamp(Mathf.Min(tam.X / Mathf.Max(span.X, 1), tam.Y / Mathf.Max(span.Y, 1)),
							  EscalaMin, EscalaMax);
		QueueRedraw();
	}

	/// <summary>Centraliza em mim, num zoom em que da pra escolher vizinho.</summary>
	public void VerMim()
	{
		if (MinhaPosicaoNaGalaxia() is not { } eu) return;
		_centro = eu;
		_escala = Mathf.Clamp(0.02f, EscalaMin, EscalaMax);
		QueueRedraw();
	}

	// =====================================================================
	// QUAIS PLANETAS ESTAO NA TELA
	// =====================================================================
	/// <summary>
	/// A VARREDURA PROCEDURAL ESTA LIGADA NESTE ZOOM?
	///
	/// E a pergunta que a interface faz pra explicar por que so ha sete pontos na tela. Sem ela, um
	/// zoom aberto pareceria um universo quase vazio -- e o jogador nao teria como saber que basta
	/// aproximar.
	/// </summary>
	public bool VendoProcedurais => BlocosVisiveis(out _, out _, out _, out _) <= BlocosVisiveisMax;

	/// <summary>
	/// A varredura de SISTEMAS esta ligada?
	///
	/// DUAS CONDICOES, e elas medem coisas diferentes: a primeira e LEGIBILIDADE (uma celula tem que
	/// caber num ponto visivel) e a segunda e CUSTO. Ver <see cref="CelulaMinimaPx"/> e
	/// <see cref="CelulasMax"/>.
	/// </summary>
	public bool VendoSistemas =>
		Sistemas.CelulaPx * _escala >= CelulaMinimaPx
		&& BlocosVisiveis(out _, out _, out _, out _) <= CelulasMax;

	/// <summary>
	/// Quantos blocos o enquadramento cobre, e quais.
	///
	/// Devolve a CONTAGEM antes de montar lista nenhuma: num zoom bem aberto sao milhoes de pares,
	/// e alocar milhoes de tuplas pra depois descobrir que sao demais e o jeito caro de descobrir.
	/// </summary>
	private long BlocosVisiveis(out int bx0, out int by0, out int bx1, out int by1)
	{
		const float LadoDoBloco = ChunksPorBloco * (float)Espaco.ChunkPx;
		Vector2 canto0 = ParaMundo(Vector2.Zero);
		Vector2 canto1 = ParaMundo(Size);

		bx0 = Mathf.FloorToInt(canto0.X / LadoDoBloco); bx1 = Mathf.FloorToInt(canto1.X / LadoDoBloco);
		by0 = Mathf.FloorToInt(canto0.Y / LadoDoBloco); by1 = Mathf.FloorToInt(canto1.Y / LadoDoBloco);
		return (long)(bx1 - bx0 + 1) * (by1 - by0 + 1);
	}

	/// <summary>
	/// Os planetas de um bloco, varridos uma vez e guardados.
	///
	/// ============================ UM BLOCO E EXATAMENTE UMA CELULA DE SISTEMA ============================
	/// O <see cref="ChunksPorBloco"/> daqui e o <see cref="Sistemas.ChunksPorSistema"/> valem os dois
	/// 32 -- e nao por coincidencia: a celula de sistema foi escolhida com esse lado JUSTAMENTE pra
	/// coincidir com o bloco de cache que este widget ja tinha. Entao o bloco (bx,by) e a celula
	/// (bx,by), e a varredura que custava 1.024 hashes passou a custar 1.
	///
	/// A asercao abaixo existe porque a igualdade e uma coincidencia PROPOSITAL entre duas constantes
	/// que moram em arquivos diferentes: se alguem mexer numa, o mapa passaria a ler celula errada e
	/// desenharia planetas fora de lugar, calado.
	/// ================================================================================================
	///
	/// O cache e ESTATICO e sobrevive a fechar o menu: a resposta nao muda enquanto a seed for a
	/// mesma, e revarrer ao reabrir a aba seria pagar de novo por nada. Troca de servidor (seed
	/// diferente) joga tudo fora.
	/// </summary>
	private static List<PlanetaNoEspaco> DoBloco(ulong seed, int bx, int by)
	{
		if (_cache.TryGetValue((bx, by), out List<PlanetaNoEspaco>? pronto)) return pronto;
		if (_cache.Count > BlocosNoCacheMax) _cache.Clear();

		var achados = new List<PlanetaNoEspaco>();
		if (DaCelula(seed, bx, by) is { } s)
			foreach (PlanetaNoEspaco p in s.Planetas())
				if (!p.Premade) achados.Add(p);   // os pre-feitos ja entram por fora, sempre

		_cache[(bx, by)] = achados;
		return achados;
	}

	/// <summary>
	/// O SISTEMA DE UMA CELULA. Barato, e por isso ele e o que a carta sempre tem.
	///
	/// A troca de seed (servidor diferente) esvazia AS DUAS tabelas aqui, e nao no `DoBloco`: se a
	/// limpeza morasse la, um zoom aberto -- que nunca chama `DoBloco` -- desenharia a galaxia do
	/// servidor anterior.
	/// </summary>
	private static SistemaSolar? DaCelula(ulong seed, int cx, int cy)
	{
		System.Diagnostics.Debug.Assert(ChunksPorBloco == Sistemas.ChunksPorSistema,
			"o bloco da carta tem que ser a celula de sistema -- ver o comentario acima");

		if (seed != _seedDoCache) { _cache.Clear(); _celulas.Clear(); _seedDoCache = seed; }
		if (_celulas.TryGetValue((cx, cy), out SistemaSolar? pronto)) return pronto;
		if (_celulas.Count > CelulasNoCacheMax) _celulas.Clear();

		SistemaSolar? s = Sistemas.Do(seed, cx, cy);
		_celulas[(cx, cy)] = s;
		return s;
	}

	/// <summary>
	/// O ULTIMO RESULTADO de <see cref="NaTela"/>, e o enquadramento que o produziu.
	///
	/// `_Process` pede um redesenho TODO QUADRO (o corpo se move), e a versao anterior montava uma
	/// lista nova a cada um -- centenas de planetas copiados 60 vezes por segundo, dezenas de KB de
	/// lixo por segundo enquanto a aba estivesse aberta, pra um resultado que so muda quando a
	/// camera muda. Agora ela so e refeita quando o enquadramento anda, ou quando a varredura
	/// trouxe bloco novo.
	/// </summary>
	private List<PlanetaNoEspaco> _naTela = [];
	private List<SistemaSolar> _sistemasNaTela = [];

	/// <summary>A magnitude limite do ultimo enquadramento. E o que a nota de rodape conta.</summary>
	private float _magnitude = 360f;
	private Vector2 _enquadradoEm;
	private float _enquadradoNa;
	private int _blocosQuandoEnquadrei = -1;

	private List<PlanetaNoEspaco> NaTela()
	{
		Revarrer();
		return _naTela;
	}

	/// <summary>Os sistemas do enquadramento. Sao eles os "pontinhos" da carta.</summary>
	private List<SistemaSolar> SistemasNaTela()
	{
		Revarrer();
		return _sistemasNaTela;
	}

	private void Revarrer()
	{
		if (_blocosQuandoEnquadrei == _cache.Count
			&& _enquadradoNa == _escala && _enquadradoEm == _centro) return;

		Varrer();
		_enquadradoEm = _centro;
		_enquadradoNa = _escala;
		_blocosQuandoEnquadrei = _cache.Count;
	}

	private void Varrer()
	{
		// OS PRE-FEITOS SEMPRE. Eles sao sete e sao o esqueleto do mapa: sem eles, um zoom aberto
		// mostraria um retangulo vazio e o jogador nao teria como se situar.
		var planetas = new List<PlanetaNoEspaco>(Espaco.PreFeitos());
		var sistemas = new List<SistemaSolar>();
		_naTela = planetas;
		_sistemasNaTela = sistemas;

		ulong seed = GameClient.Instance?.SeedDoUniverso ?? 0;
		if (seed == 0) return;

		long quantas = BlocosVisiveis(out int bx0, out int by0, out int bx1, out int by1);
		if (!VendoSistemas) return;   // longe demais ate pra estrela: so o esqueleto

		bool comPlanetas = quantas <= BlocosVisiveisMax;
		float magnitude = _magnitude = MagnitudeLimite(quantas);
		int varridosAgora = 0;

		for (int by = by0; by <= by1; by++)
			for (int bx = bx0; bx <= bx1; bx++)
			{
				// A ESTRELA ENTRA SEM ORCAMENTO POR QUADRO: e um hash de 90 ns. Racionar isto faria a
				// galaxia aparecer aos pedacos ao arrastar, que e pior do que o custo que se economiza.
				//
				// O QUE A CORTA E A MAGNITUDE, e o ANCORADO passa por cima dela: um sistema com
				// planeta pre-feito e o esqueleto do mapa, e some-lo faria o jogador perder a
				// referencia de onde a Terra fica justamente no zoom em que ele procura por ela.
				if (DaCelula(seed, bx, by) is { } s && (s.Ancorado || s.Estrela.Raio >= magnitude))
					sistemas.Add(s);

				if (!comPlanetas) continue;

				// ORCAMENTO POR QUADRO PRO CARO: enumerar os planetas de um sistema monta uma string
				// por corpo (~5 us medidos). Varrer sessenta celulas de uma vez no quadro em que o
				// jogador soltou a roda daria um engasgo visivel.
				if (!_cache.ContainsKey((bx, by)) && ++varridosAgora > BlocosPorQuadro) continue;
				planetas.AddRange(DoBloco(seed, bx, by));
			}
	}

	/// <summary>Raio do disco na TELA. Preso num minimo pra um mundo distante nao sumir.</summary>
	private float RaioNaTela(PlanetaNoEspaco p) => Mathf.Clamp(p.Raio * _escala, 2.5f, 46f);

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagnav`)
	// =====================================================================
	/// <summary>
	/// O que este enquadramento mostraria. PUBLICO SO PRA BANCADA.
	///
	/// Sem isto, "o mapa mostra os planetas" e uma afirmacao: sem janela nao ha o que olhar, e o
	/// `_Draw` nao devolve nada. E o mesmo motivo do `Portas` publico do World.
	/// </summary>
	public List<PlanetaNoEspaco> PlanetasDeTeste() => NaTela();

	/// <summary>Os sistemas que este enquadramento mostraria. PUBLICO SO PRA BANCADA.</summary>
	public List<SistemaSolar> SistemasDeTeste() => SistemasNaTela();

	public float EscalaDeTeste => _escala;
	public Vector2 CentroDeTeste => _centro;

	/// <summary>A magnitude limite deste enquadramento. SO PRA BANCADA -- e ela que prova o corte.</summary>
	public float MagnitudeDeTeste => _magnitude;

	/// <summary>
	/// QUANTAS CELULAS O ENQUADRAMENTO COBRE. SO PRA BANCADA, e ela e a metade que faltava.
	///
	/// Contar quantos sistemas foram DESENHADOS nao mede teto nenhum: um numero baixo pode ser
	/// "o corte trabalhou" ou "nao havia nada ali". O que prova o corte e a razao entre o que o
	/// enquadramento COBRE e o que ele desenha -- e o primeiro dos dois so existe aqui dentro.
	/// </summary>
	public long CelulasDoEnquadramentoDeTeste => BlocosVisiveis(out _, out _, out _, out _);

	/// <summary>Os dois tetos da varredura de sistemas, em numero. SO PRA BANCADA.</summary>
	public static (float CelulaMinima, int CelulasMaximas) TetosDeTeste => (CelulaMinimaPx, CelulasMax);

	/// <summary>
	/// OS DOIS FINS DA ESCADA DE ZOOM. SO PRA BANCADA.
	///
	/// Sem eles a bancada so conseguiria afirmar "a escala parou de mudar" -- e isso fica VERDE tambem
	/// quando o zoom morreu por inteiro. O que separa "o limite segurou" de "a roda parou de funcionar"
	/// e comparar o ponto de parada com o limite ESCRITO aqui.
	/// </summary>
	public static (float Min, float Max) LimitesDeTeste => (EscalaMin, EscalaMax);

	/// <summary>
	/// A CONVERSAO MUNDO &lt;-&gt; TELA, em coordenadas GLOBais. SO PRA BANCADA.
	///
	/// A bancada empurra roda de verdade pelo viewport, e pra isso ela precisa de ponto GLOBAL -- o
	/// <see cref="ParaTela"/> devolve ponto LOCAL do widget. Ela ja teve uma copia da formula dentro
	/// dela, e copia de formula e o tipo de coisa que fica verde depois que o original muda: o dia em
	/// que o desenho ganhar uma margem, a copia continuaria apontando pro lugar velho e a checagem da
	/// ancora aprovaria um zoom que fugiu do cursor.
	/// </summary>
	public Vector2 TelaDeTeste(Vector2 mundo) => GetGlobalRect().Position + ParaTela(mundo);

	/// <summary>A volta do <see cref="TelaDeTeste"/>: que ponto do mundo esta debaixo deste pixel.</summary>
	public Vector2 MundoDeTeste(Vector2 telaGlobal) => ParaMundo(telaGlobal - GetGlobalRect().Position);

	/// <summary>Clica onde este sistema esta desenhado. E o caminho do mouse, sem mouse.</summary>
	public bool ClicarNoSistema(SistemaSolar s)
	{
		SistemaSolar? achado = SistemaEm(ParaTela(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y)));
		if (achado is not { } a || a.Id != s.Id) return false;
		SistemaSelecionado = a;
		Selecionado = null;
		SelecaoMudou?.Invoke();
		return true;
	}

	/// <summary>O duplo clique que abre a tela do sistema, sem mouse. SO PRA BANCADA.</summary>
	public bool AbrirSistema(SistemaSolar s)
	{
		if (!ClicarNoSistema(s)) return false;
		PediuSistema?.Invoke(s);
		return true;
	}

	/// <summary>
	/// O DUPLO CLIQUE DE VERDADE: dois <c>InputEventMouseButton</c> entregues ao
	/// <see cref="_GuiInput"/>, no ponto de tela onde a estrela esta desenhada.
	///
	/// ============================ POR QUE ELE NAO E O `AbrirSistema` ============================
	/// O <see cref="AbrirSistema"/> dispara `PediuSistema` na mao. Isso prova que a MOLDURA reage ao
	/// evento, e nao prova nada do gesto: ele pula o `SistemaEm` (o alvo debaixo do cursor), pula a
	/// disputa com o planeta (que ganha do sistema no zoom fechado) e pula a guarda de que o duplo
	/// clique so entra num sistema JA SELECIONADO. Os tres sao o gesto que o dono pediu, e os tres
	/// morreriam calados -- o `AbrirSistema` continuaria verde.
	///
	/// Por isso este manda os dois cliques de verdade, na ordem de verdade: um simples pra
	/// selecionar, um com `DoubleClick` pra entrar. Se a guarda da selecao sumir, o primeiro clique
	/// deixa de ser necessario e a bancada tem como perguntar isso (ver `SoDuploCliqueNoSistema`).
	/// =========================================================================================
	/// </summary>
	public bool DuploCliqueNoSistema(SistemaSolar s)
	{
		Vector2 tela = ParaTela(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y));
		_GuiInput(new InputEventMouseButton
		{ ButtonIndex = MouseButton.Left, Pressed = true, Position = tela });
		_GuiInput(new InputEventMouseButton
		{ ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = tela });
		return SistemaSelecionado is { } j && j.Id == s.Id;
	}

	/// <summary>
	/// SO O DUPLO CLIQUE, sem o clique de selecao antes. SO PRA BANCADA -- e um controle NEGATIVO.
	///
	/// Ele tem que NAO abrir nada: e a guarda que impede um duplo clique perdido no vazio de
	/// arrastar o jogador pra dentro de um sistema que ele nem tinha mirado.
	/// </summary>
	public void SoDuploCliqueNoSistema(SistemaSolar s)
	{
		SistemaSelecionado = null;
		Selecionado = null;
		_GuiInput(new InputEventMouseButton
		{
			ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true,
			Position = ParaTela(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y)),
		});
	}

	/// <summary>Move a camera como o arrasto faria. So pra bancada.</summary>
	public void IrPara(Vector2 mundo) { _centro = mundo; QueueRedraw(); }

	/// <summary>
	/// ENQUADRAMENTO EXATO, em PIXELS DE TELA POR CELULA DE SISTEMA. SO PRA BANCADA.
	///
	/// ============================ POR QUE NAO BASTAVA `IrPara` + `Zoom` ============================
	/// O `Zoom` multiplica por 1,25 a cada chamada, entao "chegar perto de 20 px por celula" e uma
	/// escada que para onde calhar -- e o zoom em que a FOTO para muda com o tamanho da janela e com
	/// o ponto de partida. Duas fotos tiradas assim (a de antes e a de depois) sairiam em escalas
	/// DIFERENTES, e ai a comparacao visual nao compara nada: a grade some ou aparece so por causa do
	/// enquadramento.
	///
	/// A unidade e a CELULA e nao o pixel de mundo de proposito: a reticula que o dono viu tem o passo
	/// da celula, entao "quantos pixels de tela uma celula ocupa" e exatamente a variavel que decide se
	/// o olho consegue ver linha e coluna. Ver `RoboDeNav.Enquadrar`.
	/// ============================================================================================
	/// </summary>
	public void EnquadrarDeTeste(Vector2 centro, float pxPorCelula)
	{
		_centro = centro;
		_escala = Mathf.Clamp(pxPorCelula / (float)Sistemas.CelulaPx, EscalaMin, EscalaMax);
		QueueRedraw();
	}

	/// <summary>Clica onde este planeta esta. E o caminho do mouse, sem mouse.</summary>
	public bool ClicarEm(PlanetaNoEspaco p)
	{
		PlanetaNoEspaco? achado = PlanetaEm(ParaTela(new Vector2(p.Pos.X, p.Pos.Y)));
		if (achado == null) return false;
		Selecionado = achado;
		SelecaoMudou?.Invoke();
		return true;
	}

	/// <summary>
	/// O SISTEMA DE UM PLANETA. Nao guarda vinculo nenhum: pergunta a funcao pura qual celula a
	/// posicao dele cai, que e a mesma conta que o poe la.
	/// </summary>
	private static SistemaSolar? SistemaDe(PlanetaNoEspaco p) =>
		GameClient.Instance is { } cli && cli.SeedDoUniverso != 0
			? Sistemas.Em(cli.SeedDoUniverso, p.Pos)
			: null;

	/// <summary>Raio do GLIFO da estrela na tela. Ver <see cref="DesenharSistema"/>.</summary>
	private float RaioDoGlifo(SistemaSolar s)
	{
		// O PISO CRESCE COM O RAIO DE VERDADE: no zoom aberto tudo colapsaria no mesmo pontinho, e
		// a classe da estrela -- que e o que a carta tem de novo pra dizer -- sumiria. Uma ana fica
		// com 2,2 px e uma gigante com 4,4, monotonico no raio real e nunca invertido.
		float piso = 2.2f + s.Estrela.Raio / Sistemas.RaioLetalMaximo * 2.2f;
		return Mathf.Clamp(s.Estrela.Raio * _escala, piso, 44f);
	}

	private SistemaSolar? SistemaEm(Vector2 tela)
	{
		SistemaSolar? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (SistemaSolar s in SistemasNaTela())
		{
			Vector2 c = ParaTela(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y));
			float d = c.DistanceTo(tela);
			if (d > Mathf.Max(RaioDoGlifo(s) + 8f, 11f) || d >= melhorDist) continue;
			melhor = s;
			melhorDist = d;
		}
		return melhor;
	}

	private PlanetaNoEspaco? PlanetaEm(Vector2 tela)
	{
		PlanetaNoEspaco? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (PlanetaNoEspaco p in NaTela())
		{
			Vector2 c = ParaTela(new Vector2(p.Pos.X, p.Pos.Y));
			float d = c.DistanceTo(tela);
			// ALVO GENEROSO: o disco pode ter tres pixels, e ninguem acerta tres pixels com o mouse.
			// Dez de folga e o que faz o mapa parecer que responde.
			if (d > Mathf.Max(RaioNaTela(p) + 10f, 12f) || d >= melhorDist) continue;
			melhor = p;
			melhorDist = d;
		}
		return melhor;
	}

	// =====================================================================
	// DESENHO
	// =====================================================================
	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color("070912"));
		DesenharGrade();

		Vector2? eu = MinhaPosicaoNaGalaxia();
		// AS ESTRELAS PRIMEIRO, atras: um mundo desenhado atras do proprio sol seria invisivel
		// justamente no zoom em que ele acabou de aparecer.
		foreach (SistemaSolar s in SistemasNaTela()) DesenharSistema(s);
		foreach (PlanetaNoEspaco p in NaTela()) DesenharPlaneta(p, eu);

		DesenharRota(eu);
		DesenharEu(eu);
		DesenharNota();
		DrawRect(new Rect2(Vector2.Zero, Size), Tema.Borda, false, 1);
	}

	/// <summary>
	/// A LINHA QUE EXPLICA O QUE FALTA NA TELA.
	///
	/// Sem ela, a magnitude limite e indistinguivel de um universo vazio: o jogador ve poucos pontos
	/// e conclui que ali nao ha nada, em vez de "aproxime". Um corte silencioso e a mesma familia de
	/// defeito do "silencio no lugar de erro" -- so que aqui quem e enganado e o jogador.
	/// </summary>
	private void DesenharNota()
	{
		string nota = !VendoSistemas
			? "longe demais: so os mundos com mapa proprio. Aproxime."
			: _magnitude > 360f
				? $"so estrelas a partir de {_magnitude:0} px de raio -- aproxime pra as menores aparecerem"
				: VendoProcedurais ? "" : "todas as estrelas. Aproxime mais pra ver os mundos.";

		if (nota.Length == 0) return;
		DrawString(ThemeDB.FallbackFont, new Vector2(8, Size.Y - 7), nota,
				   HorizontalAlignment.Left, Size.X - 16, 10, Tema.TextoFraco);
	}

	/// <summary>
	/// A GRADE DE CHUNKS -- so quando cada uma cabe em pelo menos 24 px de tela.
	///
	/// Ela e a escala do mapa: sem nada de referencia, um espaco preto com pontos nao diz se dois
	/// planetas estao a uma hora ou a uma semana um do outro.
	/// </summary>
	private void DesenharGrade()
	{
		float passo = Espaco.ChunkPx * _escala;
		if (passo < 24f) return;

		var cor = new Color(Tema.Borda, 0.25f);
		Vector2 canto = ParaMundo(Vector2.Zero);
		float x0 = Mathf.Floor(canto.X / Espaco.ChunkPx) * Espaco.ChunkPx;
		float y0 = Mathf.Floor(canto.Y / Espaco.ChunkPx) * Espaco.ChunkPx;

		for (float x = x0; ParaTela(new Vector2(x, 0)).X <= Size.X; x += Espaco.ChunkPx)
		{
			float sx = ParaTela(new Vector2(x, 0)).X;
			DrawLine(new Vector2(sx, 0), new Vector2(sx, Size.Y), cor);
		}
		for (float y = y0; ParaTela(new Vector2(0, y)).Y <= Size.Y; y += Espaco.ChunkPx)
		{
			float sy = ParaTela(new Vector2(0, y)).Y;
			DrawLine(new Vector2(0, sy), new Vector2(Size.X, sy), cor);
		}
	}

	/// <summary>
	/// UM SISTEMA NA CARTA -- o pontinho que o dono pediu que virasse sistema.
	///
	/// ============================ TRES NIVEIS, E ELES SAO O ZOOM ============================
	/// 1. LONGE: so o glifo colorido. A cor e o HALO MEDIDO da folha de arte, entao a classe da
	///    estrela se le de relance sem legenda nenhuma (ver `ArteDeEstrela`).
	/// 2. MEDIO: aparece o disco fraco do ALCANCE do sistema (`RaioDoSistema`) e o nome. E o que
	///    diz "este ponto tem coisa dentro" antes de o zoom chegar nos planetas.
	/// 3. PERTO: a folha de arte de verdade, escalada pelo nucleo medido -- a mesma conta da tela
	///    do sistema, entao a estrela nao muda de tamanho ao entrar nela.
	///
	/// A textura so entra a partir de 10 px porque abaixo disso ela e um borrao de um pixel que
	/// custa 1 MB de VRAM; e acima disso o enquadramento nao cabe mais que um punhado de celulas
	/// (uma celula mede 65.536 px), entao nao ha risco de carregar dezenas de folhas de uma vez.
	/// =====================================================================================
	/// </summary>
	private void DesenharSistema(SistemaSolar s)
	{
		Vector2 c = ParaTela(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y));
		float r = RaioDoGlifo(s);
		if (c.X < -r - 80 || c.Y < -r - 80 || c.X > Size.X + r + 80 || c.Y > Size.Y + r + 80) return;

		Color cor = ArteDeEstrela.Halo(s.Estrela);
		bool marcado = SistemaSelecionado is { } j && j.Id == s.Id;
		float alcance = (float)s.RaioDoSistema * _escala;

		// PONTO PEQUENO E UM RETANGULO, e nao um circulo.
		//
		// Um `DrawCircle` do Godot e um poligono de ~24 vertices; com milhares de estrelas na tela
		// isso e milhao de vertice por quadro pra desenhar coisas de tres pixels, que na tela sao
		// quadradas de qualquer jeito. Dois triangulos dao o mesmo pixel por 1/12 do custo.
		if (r < 4f && !marcado)
		{
			DrawRect(new Rect2(c - new Vector2(r, r), r * 2, r * 2), cor);
			if (!s.Ancorado) return;   // sem rotulo, sem coroa: nesta escala nao caberiam
		}
		else
		{
			if (alcance > 7f) DrawArc(c, alcance, 0, Mathf.Tau, 40, new Color(cor, 0.14f), 1f);

			if (r >= 10f && ArteDeEstrela.Textura(s.Estrela) is { } t)
			{
				float lado = ArteDeEstrela.LadoDaFolha(s.Estrela, r);
				DrawTextureRect(t, new Rect2(c - new Vector2(lado, lado) / 2f, lado, lado), false);
			}
			else
			{
				DrawCircle(c, r * 1.7f, new Color(cor, 0.18f));   // a coroa
				DrawCircle(c, r, cor);
			}
		}

		if (marcado)
		{
			DrawArc(c, r + 6, 0, Mathf.Tau, 28, Tema.Bom, 2f);
			DrawArc(c, Mathf.Max(alcance, r + 10), 0, Mathf.Tau, 48, new Color(Tema.Bom, 0.35f), 1f);
		}

		// O NOME so quando ha espaco. O ancorado ("Sistema de Earth") aparece antes dos outros:
		// e por ele que o jogador se situa na galaxia.
		if (!marcado && r < 5f && !s.Ancorado) return;

		Font f = ThemeDB.FallbackFont;
		int tam = s.Ancorado ? 12 : 10;
		string nome = s.NomeDaEstrela;
		Vector2 medida = f.GetStringSize(nome, HorizontalAlignment.Left, -1, tam);
		DrawString(f, c + new Vector2(-medida.X / 2f, -r - 5), nome,
				   HorizontalAlignment.Left, -1, tam, s.Ancorado ? Tema.Texto : Tema.TextoFraco);
	}

	private void DesenharPlaneta(PlanetaNoEspaco p, Vector2? eu)
	{
		Vector2 c = ParaTela(new Vector2(p.Pos.X, p.Pos.Y));
		float r = RaioNaTela(p);
		if (c.X < -r - 60 || c.Y < -r - 60 || c.X > Size.X + r + 60 || c.Y > Size.Y + r + 60) return;

		// PRE-FEITO x GERADO se distinguem pela COR, e a diferenca importa: so o pre-feito tem
		// superficie pra pousar hoje. Um mapa que os desenha igual promete pouso onde nao ha.
		bool ehSelecionado = Selecionado is { } s && s.Nome == p.Nome;

		// ============================ O MUNDO MORTO NAO PODE PARECER VIVO ============================
		// A carta e a unica coisa que o jogador consulta antes de gastar horas de voo -- e ela
		// **enumera planetas sozinha**, sem perguntar ao servidor. Sem esta consulta ela desenharia
		// um disco azul cheio, com nome, e um botao "Viajar" em cima de um campo de destrocos.
		//
		// O DESENHO E O DE UM CADAVER e nao um sumico: apagar o planeta da carta seria pior --
		// quem conhecia Vegeta acharia que a carta quebrou, e ninguem saberia que a saga aconteceu.
		// Fica um anel vazio, cinza, sem miolo: a forma diz "havia algo aqui".
		// ======================================================================================
		bool morto = GameClient.Instance?.Mortos.Morto(p) ?? false;
		Color cor = morto ? new Color("4a4a4e") : p.Premade ? Tema.Destaque : new Color("6d7ba8");

		if (morto)
		{
			DrawArc(c, r, 0, Mathf.Tau, 24, new Color(cor, 0.85f), 1.5f);
			DrawArc(c, r * 0.45f, 0, Mathf.Tau, 16, new Color(cor, 0.4f), 1f);
		}
		else
		{
			DrawCircle(c, r, new Color(cor, 0.22f));            // a atmosfera
			DrawCircle(c, r * 0.72f, cor);                       // o corpo
			if (p.Premade) DrawArc(c, r + 2, 0, Mathf.Tau, 24, new Color(cor, 0.55f), 1.5f);
		}

		if (ehSelecionado)
		{
			DrawArc(c, r + 7, 0, Mathf.Tau, 32, Tema.Bom, 2f);
			DrawArc(c, r + 11, 0, Mathf.Tau, 32, new Color(Tema.Bom, 0.35f), 1f);
		}

		// O NOME so quando ha espaco pra ele: num zoom aberto, trezentos rotulos empilhados viram
		// uma mancha branca que esconde justamente os planetas.
		if (!ehSelecionado && r < 5f && !p.Premade) return;

		Font fonte = ThemeDB.FallbackFont;
		int tam = p.Premade ? 13 : 11;

		// O ROTULO DIZ O QUE ACONTECEU. "Vegeta" desenhado em cinza ainda parece um planeta que
		// existe -- e a diferenca entre "nao consigo pousar" e "nao ha onde pousar" e a coisa que o
		// jogador precisa saber antes de sair voando pra la.
		string rotulo = morto ? $"{p.Nome} (destruído)" : p.Nome;
		Vector2 medida = fonte.GetStringSize(rotulo, HorizontalAlignment.Left, -1, tam);
		DrawString(fonte, c + new Vector2(-medida.X / 2f, r + tam + 2), rotulo,
				   HorizontalAlignment.Left, -1, tam,
				   morto ? new Color("8a6a6a") : p.Premade ? Tema.Texto : Tema.TextoFraco);

		// "VOCE ESTA AQUI": estou dentro da area de pouso deste planeta.
		if (eu is { } meu && meu.DistanceTo(new Vector2(p.Pos.X, p.Pos.Y)) <= p.Raio)
			DrawArc(c, r + 4, 0, Mathf.Tau, 28, Tema.Ki, 1.5f);
	}

	/// <summary>A linha do piloto automatico, e a do destino que esta so selecionado.</summary>
	private void DesenharRota(Vector2? eu)
	{
		if (eu is not { } meu) return;
		Vector2 de = ParaTela(meu);

		if (World.Instancia?.DestinoDoPiloto is { } indo)
		{
			Vector2 ate = ParaTela(new Vector2(indo.X, indo.Y));
			DrawLine(de, ate, new Color(Tema.Bom, 0.75f), 2f);
			DrawArc(ate, 6, 0, Mathf.Tau, 16, Tema.Bom, 2f);
		}
		else if (Selecionado is { } s)
		{
			// TRACEJADA enquanto e so uma intencao. Cheia quando o piloto esta ligado -- a diferenca
			// entre "pensei nisso" e "estou indo" tem que ser visivel sem ler texto.
			Tracejada(de, ParaTela(new Vector2(s.Pos.X, s.Pos.Y)), new Color(Tema.Texto, 0.35f));
		}
	}

	private void Tracejada(Vector2 a, Vector2 b, Color cor)
	{
		float total = a.DistanceTo(b);
		if (total < 1f) return;
		Vector2 dir = (b - a) / total;
		for (float t = 0; t < total; t += 12f)
			DrawLine(a + dir * t, a + dir * Mathf.Min(t + 6f, total), cor, 1.5f);
	}

	private void DesenharEu(Vector2? eu)
	{
		if (eu is not { } meu) return;
		Vector2 c = ParaTela(meu);
		DrawArc(c, 7, 0, Mathf.Tau, 20, Tema.Ki, 2f);
		DrawLine(c + new Vector2(-11, 0), c + new Vector2(-4, 0), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(4, 0), c + new Vector2(11, 0), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(0, -11), c + new Vector2(0, -4), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(0, 4), c + new Vector2(0, 11), Tema.Ki, 1.5f);
	}
}
