using Godot;

namespace Jandirus.Client;

/// <summary>
/// A ABERTURA DO JOGO -- o titulo "DRAGON BALL JANDIRUS" cai na tela como no trailer, respira um instante, SOBE pro
/// alto e o menu aparece.
///
/// O dono (2026-10-11): *"ao abrir o jogo ter tipo uma animaçao igual a animaçao q vc fez pro titulo do jogo no
/// trailer, aparecendo o titulo do jogo (...) e dps dessa animaçao o titulo sobe pro topo da tela e o menu normal do
/// jogo aparece"*.
///
/// ============================ A RECEITA E A DO TRAILER, NUMERO POR NUMERO ============================
/// O titulo do trailer foi feito em pos-producao (`Trailer/bruto/t2/titulo_anim.py`, Python + Pillow). Aqui a mesma
/// conta roda dentro do jogo -- os tempos e os numeros abaixo sao os de la, e a funcao <see cref="Em"/> e o
/// `quadro(t)` dela:
///
///     0,00 s  a tela nasce BRANCA e o branco some em 0,35 s (o estouro de onde a cena vinha)
///     0,35 s  o logo CAI: de 2,6x a 1x em 0,22 s, ganhando corpo (o alfa vai de 0,25 a 1)
///     0,57 s  o IMPACTO: um clarao (0,16 s), a tela TREME (0,32 s), um ANEL de choque abre (0,7 s), 70 FAISCAS
///             voam e caem (0,9 s), e atras do logo acende o BRILHO laranja, que fica pulsando
///     dali    o logo RESPIRA (cresce 4,5% ao longo dos 11 s do clipe do trailer: quase parado)
///
/// O fundo tambem e o de la: preto azulado, uma mancha de luz no meio e 260 estrelas paradas. As estrelas e as
/// faiscas sao as MESMAS do trailer, uma a uma (<see cref="SorteioDaReceita"/>).
///
/// VALE O QUE O TRAILER MOSTRA, E NAO O QUE A RECEITA ESCREVE. Ela da ao anel e as faiscas um alfa que cai com o
/// tempo -- e desenha os dois direto num quadro RGBA, onde o `ImageDraw` do Pillow nao mistura: grava o pixel, e o
/// alfa e jogado fora quando o quadro vira RGB. No clipe do trailer (`titulo_clip.mp4`, conferido quadro a quadro) o
/// anel e branco CHEIO do comeco ao fim, e as faiscas sao amarelas cheias ate sumirem de uma vez aos 0,9 s. Aqui
/// tambem: o anel e opaco, e as faiscas so desmaiam no ultimo decimo (<see cref="DesmaioDasFaiscas"/>) -- a unica
/// diferenca pro clipe, pra elas nao piscarem fora todas no mesmo quadro.
///
/// E O QUE E SO DO JOGO: 1,55 s depois do impacto (<see cref="Respiro"/>) o titulo sobe pro lugar dele no alto do
/// menu (<see cref="TituloDoJogo"/>) em 0,75 s (<see cref="Subida"/>), encolhendo, enquanto o fundo se desfaz e o
/// menu aparece por baixo. Do primeiro quadro ao menu na mao: 2,87 s.
///
/// ============================ TUDO DESENHADO COM O QUE O LOBBY JA DESENHA ============================
/// Esta e a PRIMEIRA coisa que o processo poe na tela, e neste projeto o primeiro uso de shader, de material ou de
/// uma forma nova de desenhar custa quadros (ver o cabecalho do `Aquecimento`). Entao nao ha shader nem material
/// aqui: tudo e retangulo -- liso (`DrawRect`) ou com textura (`DrawTextureRect`) --, a mesma forma de desenhar dos
/// paineis de cor e do texto que o menu ja usa. O anel e uma elipse de 96 retangulos girados, e nao um arco: o arco
/// e a linha do motor sao OUTRA forma de desenhar, com montagem propria no primeiro uso.
///
/// E O MENU E DESENHADO POR BAIXO DESDE O PRIMEIRO QUADRO, atras do fundo opaco: o custo de primeira vez dele (as
/// letras, as chapas) cai no primeiro quadro do processo, como sempre caiu, e nao no meio da subida do titulo.
///
/// ============================ O RELOGIO PARA NO ENGASGO, NAO PULA ============================
/// O relogio da animacao anda no maximo <see cref="PassoMaximo"/> por quadro: um quadro comprido (a maquina
/// ocupada, o arranque) CONGELA a animacao por um instante em vez de comer um pedaco dela. E so comeca a andar
/// depois que o primeiro quadro -- o mais comprido do processo -- ja foi pra tela, branco.
///
/// ============================ E O AQUECIMENTO ESPERA O TITULO PARAR ============================
/// O que o jogo carrega no arranque vem de uma thread e nao atrapalha. O ENSAIO dos efeitos nao: ele e trabalho da
/// thread principal, um ato por quadro, e num jogo recem-instalado um ato so para a tela por 0,4 s. Ele comeca
/// 1 a 2 s depois de a janela abrir -- no meio do anel, das faiscas ou da subida. Entao, enquanto o titulo esta em
/// MOVIMENTO, a abertura pede calma (<see cref="PedeCalma"/>) e o ensaio espera; na janela em que o logo so respira
/// (meio segundo), e depois da abertura, ele corre. O que vem da thread de carga nao espera nada.
///
/// MEDIDO em 2026-10-11 pela `--diagabertura` (janela de 1280x720 num monitor de 120 Hz; quadro mediano de 8,3 ms):
///
///                                           o pior quadro COM O TITULO      o pior quadro da
///                                           EM MOVIMENTO                    abertura inteira
///     cache de shader do Godot vazio ....... 10,6 a 16,9 ms ................ 112 a 163 ms (no respiro: um ato do ensaio)
///     cache cheio (quem ja abriu o jogo) ... 14,8 a 16,9 ms ................ 34,6 ms (idem)
///     SEM pedir calma (`--aberturasemcalma`) 408 ms (a poeira do ensaio, no meio da subida), e 14 atos
///                                           e 30 pipelines em cima do titulo em movimento
///
/// (Os mais compridos dos quadros em movimento tem 16 a 17 ms -- um quadro de 120 Hz repetido -- e caem sempre nos
/// mesmos dois lugares: no BAQUE, que e onde o tocador do som nasce, e 0,15 s depois dele.)
///
/// O quadro comprido que sobra cai com a imagem PARADA (o logo assentado, o brilho pulsando devagar): o relogio
/// congela nele e ninguem ve. O PRECO e do ensaio: do primeiro ato ao fim ele leva 2,0 a 2,3 s, em vez de 1,0 a
/// 1,5 s (a subida do titulo o interrompe), e acaba uns 6 s depois de o processo nascer -- ainda antes de alguem
/// conseguir logar. A fila da thread de carga nao muda: 2,05 a 2,23 s com a abertura, 2,20 a 2,34 s sem ela.
///
/// ============================ QUANDO ELA RODA ============================
/// Uma vez por execucao, e so numa partida aberta por GENTE: quem decide e o `Boot` (`Boot.Abertura.cs`). Qualquer
/// tecla ou clique PULA pro estado final -- e o gesto que pula morre aqui, nao chega no menu.
/// </summary>
public partial class AberturaDoJogo : CanvasLayer
{
	// =====================================================================
	// OS NUMEROS DA RECEITA (`Trailer/bruto/t2/titulo_anim.py`; px de um quadro de 1920x1080)
	// =====================================================================
	/// <summary>O quadro em que a receita foi escrita. Aqui ele se encaixa na tela pelo eixo mais apertado.</summary>
	public const float LarguraDoQuadro = 1920f, AlturaDoQuadro = 1080f;

	/// <summary>`ENTRA`: em quanto tempo some o branco do comeco -- e so depois o logo cai.</summary>
	public const double Entra = 0.35;

	/// <summary>`QUEDA`: quanto dura a queda do logo.</summary>
	public const double Queda = 0.22;

	/// <summary>O logo cai de 1 + 1,6 = 2,6x ate 1x, e comeca a queda com um quarto do corpo.</summary>
	public const float EscalaDaQueda = 1.6f, AlfaNoComecoDaQueda = 0.25f;

	/// <summary>O logo assentado fica 8 px acima do centro do quadro.</summary>
	public const float SubidaDoCentro = 8f;

	/// <summary>O TREMOR do impacto: 26 px que morrem em 0,32 s, balancando a 190 e a 150 rad/s.</summary>
	public const double DuracaoDoTremor = 0.32;
	public const float ForcaDoTremor = 26f, PressaDoTremorX = 190f, PressaDoTremorY = 150f;

	/// <summary>
	/// O ANEL de choque: abre de 120 a 2020 px de raio em 0,7 s, achatado a 62%. OPACO enquanto dura (ver "VALE O QUE
	/// O TRAILER MOSTRA" no cabecalho) -- e numa tela 16:9 ele ja saiu pelos cantos aos 0,27 s.
	/// </summary>
	public const double DuracaoDoAnel = 0.7;
	public const float RaioInicialDoAnel = 120f, CursoDoAnel = 1900f, Achatamento = 0.62f;

	/// <summary>O anel e um traco branco de 16 px com um fio amarelado de 8 px por cima, na beirada de fora.</summary>
	public const float GrossuraDoAnel = 16f, GrossuraDoFio = 8f;
	public static readonly Color CorDoFio = Color.Color8(255, 220, 120);

	/// <summary>AS FAISCAS: 70, voando 0,9 s, com pressa de 500 a 1500 px/s, 3 a 8 px de raio, e caindo (380 px/s2).</summary>
	public const double DuracaoDasFaiscas = 0.9;

	/// <summary>
	/// DIVERGENCIA DECLARADA do clipe do trailer: la as faiscas somem todas no mesmo quadro, aos 0,9 s; aqui elas
	/// desmaiam ao longo dos ultimos 0,12 s (cheias ate os 0,78 s).
	/// </summary>
	public const double DesmaioDasFaiscas = 0.12;
	public const int QuantasFaiscas = 70;
	public const float QuedaDasFaiscas = 380f;
	public static readonly Color CorDasFaiscas = Color.Color8(255, 225, 120);

	/// <summary>O CLARAO do impacto: 85% de branco que somem em 0,16 s.</summary>
	public const double DuracaoDoClarao = 0.16;
	public const float ForcaDoClarao = 0.85f;

	/// <summary>O BRILHO de tras do logo pulsa em volta de 0,72 (+-0,28), a 2,4 rad/s.</summary>
	public const float BrilhoMedio = 0.72f, BrilhoQuePulsa = 0.28f, PressaDoBrilho = 2.4f;

	/// <summary>O RESPIRO: o logo cresce 4,5% do impacto ate o fim do clipe do trailer, que tem 11 s.</summary>
	public const float CrescimentoDoRespiro = 0.045f;
	public const double DuracaoDoClipe = 11.0;

	/// <summary>O FUNDO: preto azulado (6, 5, 14) com uma mancha que chega a (52, 25, 40) no meio, e 260 estrelas de 2 px.</summary>
	public static readonly Color CorDoFundo = Color.Color8(6, 5, 14), CorDaMancha = Color.Color8(52, 25, 40);
	public const float RaioDaManchaX = 0.55f, RaioDaManchaY = 0.62f;   // fracoes da largura e da altura da tela
	public const int QuantasEstrelas = 260;
	public const float LadoDaEstrela = 2f;
	private static readonly int[] BrilhosDeEstrela = [60, 90, 130, 200];

	/// <summary>A semente do `random.seed(7)` da receita: as estrelas e as faiscas saem dela.</summary>
	public const uint Semente = 7;

	// =====================================================================
	// O QUE E SO DO JOGO (ajustavel)
	// =====================================================================
	/// <summary>Do impacto ate o titulo comecar a subir: os 0,9 s das faiscas e mais um instante so respirando.</summary>
	public const double Respiro = 1.55;

	/// <summary>Quanto dura a subida do titulo pro alto do menu.</summary>
	public const double Subida = 0.75;

	/// <summary>
	/// Quanto ANTES de a subida comecar a abertura volta a pedir calma. Um ato do ensaio do `Aquecimento` nao custa so
	/// o quadro em que roda: o que ele poe no palco e desenhado -- e as pipelines dele sao montadas -- nesse quadro e
	/// nos dois seguintes. MEDIDO (`--diagabertura`, 2026-10-11): com o ultimo ato colado no comeco da subida, o
	/// primeiro quadro dela tinha 47 ms e montava 2 pipelines. O relogio anda no maximo 0,05 s por quadro, entao
	/// 0,15 s sao pelo menos tres quadros de folga.
	/// </summary>
	public const double FolgaAntesDaSubida = 0.15;

	/// <summary>O fundo da abertura acaba de se desfazer um pouco ANTES de o titulo pousar (a esta fracao da subida).</summary>
	public const float FundoSomeEm = 0.85f;

	/// <summary>
	/// Depois de PULAR, por quanto tempo a abertura ainda engole clique e tecla: o segundo toque de um clique duplo
	/// nao pode cair num botao do menu que acabou de aparecer.
	/// </summary>
	public const double GuardaDoPulo = 0.25;

	/// <summary>O maximo que o relogio da animacao anda num quadro. Ver "O RELOGIO PARA NO ENGASGO" no cabecalho.</summary>
	public const double PassoMaximo = 0.05;

	/// <summary>
	/// O SOM DA QUEDA: o baque de um corpo arremessado batendo no chao -- no DM, `Throw.dm:165`, o martelo
	/// (`Hammer Skills.dm:87`) e o fim do arremesso de um golpe (`Movement Effects.dm:60`). Ja estava no jogo e
	/// ninguem o tocava. Sai pelo barramento de EFEITOS, como todo som pontual: obedece o volume das opcoes.
	///
	/// NAO E UMA CONSTANTE DO `Trilha` DE PROPOSITO: o que mora la entra na fila do `Aquecimento`, que esta no ar
	/// justamente neste primeiro segundo -- e tocar um arquivo que ainda esta na thread de carga trava o jogo (o
	/// cabecalho do `SonsPresos`). Este e so daqui, e e lido uma vez, no primeiro quadro.
	/// </summary>
	public const string SomDoImpacto = "res://Assets/Sounds/Effects/landharder.ogg";
	public const float VolumeDoImpacto = 0.8f;

	/// <summary>Acima das telas do lobby (login 1, criacao 2, selecao 3); abaixo das opcoes (20) e do carregamento (90).</summary>
	public const int Camada = 10;

	private const int SegmentosDoAnel = 96;

	/// <summary>O instante do IMPACTO, o do comeco da SUBIDA e o do FIM, no relogio da animacao.</summary>
	public const double Impacto = Entra + Queda, ComecoDaSubida = Impacto + Respiro, Fim = ComecoDaSubida + Subida;

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--diagabertura`); falsos em jogo, sempre
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --diagabertura): a queda do logo dura o DOBRO da receita. Lido pela propria linha
	/// da conta, em <see cref="Em"/> -- a tabela de tempos da bancada tem de reprovar.
	/// </summary>
	public static bool QuedaLentaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagabertura): o gesto que pula NAO morre na abertura -- o clique segue pro botao
	/// do menu que esta embaixo, e o ESC abre as opcoes. Lido pelas duas linhas que seguram o gesto: a que o engole,
	/// no `_Input`, e a do escudo do mouse, no `Montar`.
	/// </summary>
	public static bool PuloVazaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagabertura): o titulo do menu NAO e apagado enquanto a abertura corre -- dois
	/// titulos na tela quando o fundo dela se desfaz. Lido pela propria linha que o apaga, em <see cref="Mirar"/>.
	/// </summary>
	public static bool SemApagarOTituloDoMenuDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagabertura, rodada `--aberturasemcalma`, ligado pelo `Boot`): a abertura NAO
	/// pede calma -- o ensaio do `Aquecimento` corre por cima do titulo em movimento, e cada ato dele para a tela.
	/// Lido pela propria regra, em <see cref="PedeCalma"/>.
	/// </summary>
	public static bool SemPedirCalmaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagabertura): a abertura reprisa -- a trava de "uma vez por execucao" nao vale.
	/// Lido pela propria linha da trava, em <see cref="Comecar"/>.
	/// </summary>
	public static bool ReprisaDeTeste;

	// =====================================================================
	// A CONTA -- o `quadro(t)` da receita, e a subida
	// =====================================================================
	/// <summary>
	/// O QUE A ABERTURA MOSTRA NUM INSTANTE. Medidas em px do quadro da receita (1920x1080); alfas de 0 a 1.
	/// </summary>
	/// <param name="T">O instante, no relogio da animacao.</param>
	/// <param name="Clarao">O branco por cima de tudo: o do comeco, e depois o do impacto.</param>
	/// <param name="TemLogo">O logo ja esta na tela (a queda comecou)?</param>
	/// <param name="Escala">O tamanho do logo: 2,6 no alto da queda, 1 assentado.</param>
	/// <param name="AlfaDoLogo">O corpo do logo: 0,25 no comeco da queda, 1 no impacto.</param>
	/// <param name="Tremor">O deslocamento do logo no tremor do impacto.</param>
	/// <param name="Brilho">O brilho laranja de tras (zero antes do impacto).</param>
	/// <param name="RaioDoAnel">O raio do anel de choque.</param>
	/// <param name="AlfaDoAnel">Um enquanto o anel dura, zero sem anel.</param>
	/// <param name="TempoDoImpacto">Ha quanto tempo foi o impacto (negativo antes dele): as faiscas voam por ele.</param>
	/// <param name="AlfaDasFaiscas">Um enquanto as faiscas voam, caindo a zero no desmaio do fim.</param>
	/// <param name="NaSubida">Quanto da subida pro alto ja foi, de 0 a 1, ja suavizado.</param>
	/// <param name="Fundo">Quanto do fundo da abertura ainda cobre o menu, de 1 a 0.</param>
	public readonly record struct Estado(
		double T, float Clarao, bool TemLogo, float Escala, float AlfaDoLogo, Vector2 Tremor, float Brilho,
		float RaioDoAnel, float AlfaDoAnel, float TempoDoImpacto, float AlfaDasFaiscas, float NaSubida, float Fundo)
	{
		/// <summary>A animacao chegou ao fim: o titulo pousou.</summary>
		public bool Acabou => T >= Fim;
	}

	/// <summary>
	/// A ABERTURA NUM INSTANTE -- funcao PURA do relogio. Ate o comeco da subida e o `quadro(t)` do
	/// `titulo_anim.py`, linha por linha; a bancada a confere contra uma tabela escrita a mao.
	/// </summary>
	public static Estado Em(double t)
	{
		// o branco de onde a cena veio, sumindo
		float clarao = t < Entra ? MathF.Pow((float)(1.0 - Math.Max(t, 0.0) / Entra), 1.5f) : 0f;

		double tq = t - Entra;
		if (tq < 0) return new Estado(t, clarao, false, 1f + EscalaDaQueda, 0f, Vector2.Zero, 0f, 0f, 0f, -1f, 0f, 0f, 1f);

		// (defeito injetado: a queda dura o dobro -- ver `QuedaLentaDeTeste`)
		double queda = QuedaLentaDeTeste ? Queda * 2.0 : Queda;
		float k = (float)Math.Min(1.0, tq / queda);
		double ti = tq - queda;

		// A QUEDA (cubica: despenca e freia em cima) e, depois dela, o respiro
		float escala = k < 1f
			? 1f + EscalaDaQueda * (1f - k) * (1f - k) * (1f - k)
			: 1f + CrescimentoDoRespiro * (float)(ti / Math.Max(0.1, DuracaoDoClipe - Entra - Queda));
		float alfa = k < 1f ? Math.Min(1f, AlfaNoComecoDaQueda + k) : 1f;
		if (ti < 0) return new Estado(t, clarao, true, escala, alfa, Vector2.Zero, 0f, 0f, 0f, (float)ti, 0f, 0f, 1f);

		// O TREMOR: os px inteiros da receita (`int(...)` do Python corta pra zero)
		Vector2 tremor = Vector2.Zero;
		if (ti < DuracaoDoTremor)
		{
			float u = (float)(1.0 - ti / DuracaoDoTremor);
			float forca = ForcaDoTremor * u * u;
			tremor = new Vector2(MathF.Truncate(forca * MathF.Sin((float)ti * PressaDoTremorX)),
								 MathF.Truncate(forca * MathF.Cos((float)ti * PressaDoTremorY)));
		}

		float brilho = BrilhoMedio + BrilhoQuePulsa * MathF.Sin((float)ti * PressaDoBrilho);

		float raio = 0f, alfaDoAnel = 0f;
		if (ti < DuracaoDoAnel)
		{
			float u = (float)(1.0 - ti / DuracaoDoAnel);
			raio = RaioInicialDoAnel + CursoDoAnel * (1f - u * u);
			alfaDoAnel = 1f;   // opaco, como no clipe: ver "VALE O QUE O TRAILER MOSTRA"
		}

		// (cheias, como no clipe -- e o desmaio do fim, que e daqui: ver `DesmaioDasFaiscas`)
		float alfaDasFaiscas = ti < DuracaoDasFaiscas ? (float)Math.Min(1.0, (DuracaoDasFaiscas - ti) / DesmaioDasFaiscas) : 0f;

		if (ti < DuracaoDoClarao) clarao = ForcaDoClarao * (float)(1.0 - ti / DuracaoDoClarao);

		// A SUBIDA (so do jogo): devagar, depressa, devagar -- e o fundo some um pouco antes de o titulo pousar
		float s = (float)Math.Clamp((t - ComecoDaSubida) / Subida, 0.0, 1.0);
		return new Estado(t, clarao, true, escala, alfa, tremor, brilho, raio, alfaDoAnel, (float)ti, alfaDasFaiscas,
						  Suave(s), 1f - Suave(Math.Min(1f, s / FundoSomeEm)));
	}

	/// <summary>Devagar, depressa, devagar (cubica dos dois lados).</summary>
	private static float Suave(float s) => s < 0.5f ? 4f * s * s * s : 1f - MathF.Pow(-2f * s + 2f, 3f) / 2f;

	/// <summary>
	/// O RETANGULO DO LOGO ASSENTADO numa tela deste tamanho (sem tremor, na escala 1): o quadro da receita encaixado
	/// pelo eixo mais apertado -- em 16:9 o logo tem 61% da largura da tela, como no trailer.
	/// </summary>
	public static Rect2 LogoAssentado(Vector2 tela)
	{
		float k = Unidade(tela);
		var tamanho = new Vector2(TituloDoJogo.LarguraDaReceita * k, TituloDoJogo.LarguraDaReceita * k / TituloDoJogo.Proporcao);
		return new Rect2(tela / 2f - new Vector2(0f, SubidaDoCentro * k) - tamanho / 2f, tamanho);
	}

	/// <summary>Quantos px desta tela vale 1 px do quadro da receita.</summary>
	public static float Unidade(Vector2 tela) => Math.Min(tela.X / LarguraDoQuadro, tela.Y / AlturaDoQuadro);

	/// <summary>
	/// ONDE O LOGO ESTA num estado: assentado (com a escala e o tremor da receita) e, na subida, a caminho do lugar
	/// dele no alto do menu -- o centro em linha reta, o tamanho encolhendo por RAZAO (a metade do caminho e a media
	/// geometrica dos dois tamanhos: e o que o olho le como encolher por igual).
	/// </summary>
	public static Rect2 LugarDoLogo(Estado e, Vector2 tela, Rect2 noMenu)
	{
		Rect2 assentado = LogoAssentado(tela);
		Vector2 centro = assentado.GetCenter() + e.Tremor * Unidade(tela);
		float largura = assentado.Size.X * e.Escala;
		if (e.NaSubida > 0f && noMenu.Size.X > 0f)
		{
			centro = centro.Lerp(noMenu.GetCenter(), e.NaSubida);
			largura *= MathF.Pow(noMenu.Size.X / largura, e.NaSubida);
		}
		var tamanho = new Vector2(largura, largura / TituloDoJogo.Proporcao);
		return new Rect2(centro - tamanho / 2f, tamanho);
	}

	// =====================================================================
	// O ESTADO DO PROCESSO
	// =====================================================================
	/// <summary>A abertura que esta correndo AGORA (nulo antes dela, depois dela, e em toda corrida em que ela nao roda).</summary>
	public static AberturaDoJogo? Viva { get; private set; }

	/// <summary>Quantas vezes a abertura comecou neste processo pela porta do jogo. Em jogo, zero ou uma.</summary>
	public static int Vezes { get; private set; }

	/// <summary>
	/// A ABERTURA PEDE CALMA: o titulo esta em movimento, e trabalho pesado da thread principal apareceria como um
	/// engasgo dele. Quem pergunta e o ensaio do `Aquecimento`, a cada quadro. Falso em toda corrida sem abertura.
	/// </summary>
	public static bool PedeCalma => !SemPedirCalmaDeTeste && Viva is { QuerCalma: true };

	/// <summary>O que a ultima abertura deste processo fez -- pro log e pra bancada.</summary>
	/// <param name="Segundos">Do primeiro quadro que andou ate o fim, no relogio de parede.</param>
	/// <param name="NoRelogio">Onde o relogio da animacao estava quando ela acabou (<see cref="Fim"/> se correu inteira).</param>
	/// <param name="Quadros">Quantos quadros ela durou.</param>
	/// <param name="PiorQuadroMs">O quadro mais comprido dela, e em que instante da animacao ele caiu.</param>
	/// <param name="PiorEmMovimentoMs">O mais comprido entre os quadros em que o titulo estava em MOVIMENTO.</param>
	public readonly record struct Corrida(double Segundos, double NoRelogio, int Quadros, double PiorQuadroMs, double PiorQuadroEm,
										  double PiorEmMovimentoMs, bool Pulada, string Motivo);

	public static Corrida? UltimaCorrida { get; private set; }

	// =====================================================================
	// A ABERTURA
	// =====================================================================
	private Func<TituloDoJogo?> _alvo = () => null;
	private TituloDoJogo? _mirado;

	private Control _fundo = null!, _estrelas = null!, _cena = null!;
	private ColorRect _clarao = null!;

	private static Texture2D? _mancha, _ponto;
	private static (float X, float Y, float Brilho)[] _ceu = [];
	private static (float Angulo, float Pressa, float Raio)[] _faiscas = [];

	private double _t;
	private Estado _estado = Em(0);
	private Rect2 _noMenu;
	private bool _largou, _posada, _tocou, _encerrada;
	private int _quadroDeNascimento, _quadros;
	private ulong _largada, _ultimo, _guardaAte, _largouAosMs;
	private double _piorMs, _piorEm, _piorEmMovimentoMs;

	/// <summary>O relogio da animacao, em segundos.</summary>
	public double Relogio => _t;

	/// <summary>O que esta sendo mostrado agora.</summary>
	public Estado Agora => _estado;

	/// <summary>O logo da abertura esta desenhado agora (a queda ja comecou e ela nao acabou)?</summary>
	public static bool LogoNaTela => Viva is { _estado.TemLogo: true };

	/// <summary>Onde o logo da abertura esta desenhado agora, em px da base de desenho.</summary>
	public Rect2 LogoAgora => LugarDoLogo(_estado, _cena.Size, _noMenu);

	/// <summary>
	/// O TITULO ESTA EM MOVIMENTO: tudo menos a janela em que ele so respira -- do fim das faiscas ao comeco da
	/// subida. (Posada pela bancada ela nao se mexe.)
	/// </summary>
	public bool EmMovimento => !_posada && (_t < Impacto + DuracaoDasFaiscas || _t >= ComecoDaSubida);

	/// <summary>
	/// A ABERTURA QUER CALMA AGORA: o titulo em movimento, e a folga de antes da subida (<see cref="FolgaAntesDaSubida"/>).
	/// O ensaio do `Aquecimento` so anda fora disto: dos 1,47 s aos 1,97 s do relogio dela, e depois que ela acaba.
	/// </summary>
	public bool QuerCalma => !_posada && (_t < Impacto + DuracaoDasFaiscas || _t >= ComecoDaSubida - FolgaAntesDaSubida);

	/// <summary>
	/// COMECA A ABERTURA por cima do menu. <paramref name="alvo"/> devolve o titulo do menu principal DE AGORA (o
	/// lugar onde ela pousa), ou nulo se o menu saiu da tela -- e ai ela acaba na hora.
	/// </summary>
	/// <returns>Nulo se ela ja rodou neste processo.</returns>
	public static AberturaDoJogo? Comecar(Node pai, Func<TituloDoJogo?> alvo)
	{
		// UMA VEZ POR EXECUCAO: voltar ao menu (cair do servidor, desconectar, sair da selecao) nao a reprisa.
		if (Vezes > 0 && !ReprisaDeTeste) return null;
		Vezes++;
		return Nascer(pai, alvo);
	}

	/// <summary>
	/// PALCO DA BANCADA: mais uma abertura neste processo, pela mesma maquina, sem contar como a do jogo -- pra
	/// pular de tres jeitos e fotografar instante por instante. <paramref name="posadaEm"/> a deixa PARADA naquele
	/// instante do relogio (ver <see cref="Posar"/>).
	/// </summary>
	internal static AberturaDoJogo ComecarDeTeste(Node pai, Func<TituloDoJogo?> alvo, double? posadaEm = null)
	{
		AberturaDoJogo a = Nascer(pai, alvo);
		if (posadaEm is { } t) a.Posar(t);
		return a;
	}

	private static AberturaDoJogo Nascer(Node pai, Func<TituloDoJogo?> alvo)
	{
		// (uma por vez: a anterior, se ainda esta na guarda do pulo, sai)
		if (pai.GetNodeOrNull("Abertura") is AberturaDoJogo velha) { velha.Name = "AberturaVelha"; velha.QueueFree(); }
		var a = new AberturaDoJogo { Name = "Abertura", _alvo = alvo };
		pai.AddChild(a);
		return a;
	}

	public override void _Ready()
	{
		Layer = Camada;
		Viva = this;
		_quadroDeNascimento = Engine.GetFramesDrawn();
		PrepararOSorteio();
		Montar();

		// O SOM DO IMPACTO, lido agora -- o primeiro quadro do processo e comprido de qualquer jeito -- pra que no
		// quadro da queda ele so toque. Pela porta de todo som de efeito (ver `SomDoImpacto`).
		SonsPresos.Carregar(SomDoImpacto);

		Mirar();
		Mostrar();
	}

	public override void _ExitTree()
	{
		if (Viva == this) Encerrar("saiu da arvore");
	}

	/// <summary>As estrelas e as faiscas da receita, sorteadas uma vez por processo -- na ordem em que ela sorteia.</summary>
	private static void PrepararOSorteio()
	{
		if (_ceu.Length > 0) return;
		var sorteio = new SorteioDaReceita(Semente);

		_ceu = new (float, float, float)[QuantasEstrelas];
		for (int i = 0; i < _ceu.Length; i++)
		{
			int x = sorteio.Abaixo((int)LarguraDoQuadro), y = sorteio.Abaixo((int)AlturaDoQuadro);
			_ceu[i] = (x / LarguraDoQuadro, y / AlturaDoQuadro, BrilhosDeEstrela[sorteio.Abaixo(BrilhosDeEstrela.Length)] / 255f);
		}

		_faiscas = new (float, float, float)[QuantasFaiscas];
		for (int i = 0; i < _faiscas.Length; i++)
			_faiscas[i] = ((float)sorteio.Entre(0, Math.Tau), (float)sorteio.Entre(500, 1500), (float)sorteio.Entre(3, 8));
	}

	/// <summary>As 4 primeiras estrelas e a primeira faisca, como a receita as sorteia: a bancada confere contra o Python.</summary>
	internal static string SorteioDeTeste()
	{
		PrepararOSorteio();
		return string.Create(System.Globalization.CultureInfo.InvariantCulture,
			$"{string.Join(" ", _ceu.Take(4).Select(e => $"({MathF.Round(e.X * LarguraDoQuadro)},{MathF.Round(e.Y * AlturaDoQuadro)},{MathF.Round(e.Brilho * 255f)})"))} | {_faiscas[0].Angulo:0.00000} {_faiscas[0].Pressa:0.000} {_faiscas[0].Raio:0.00000}");
	}

	private void Montar()
	{
		// ---- o fundo: a cor, a mancha de luz e as estrelas. Um no so, pra se desfazer junto na subida. ----
		_fundo = Cheio(new Control());
		AddChild(_fundo);
		_fundo.AddChild(Cheio(new ColorRect { Color = CorDoFundo }));
		_fundo.AddChild(new TextureRect
		{
			Texture = _mancha ??= FazerAMancha(),
			// a mancha e uma elipse com 55% da largura e 62% da altura da tela de RAIO: passa das bordas
			AnchorLeft = 0.5f - RaioDaManchaX, AnchorRight = 0.5f + RaioDaManchaX,
			AnchorTop = 0.5f - RaioDaManchaY, AnchorBottom = 0.5f + RaioDaManchaY,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
		_estrelas = Cheio(new Control());
		_estrelas.Draw += DesenharAsEstrelas;   // paradas: so se redesenham se a tela mudar de tamanho
		_fundo.AddChild(_estrelas);

		// ---- a cena: o brilho, o logo, o anel e as faiscas, redesenhados a cada quadro ----
		_cena = Cheio(new Control { TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps });
		_cena.Draw += DesenharACena;
		AddChild(_cena);
		_ponto ??= FazerOPonto();

		// ---- o branco por cima de tudo ----
		_clarao = Cheio(new ColorRect { Color = Colors.White });
		AddChild(_clarao);

		// ---- o escudo: enquanto a abertura corre, o mouse nao chega no menu que esta embaixo ----
		// (defeito injetado: sem escudo, o mouse passa -- ver `PuloVazaDeTeste`)
		Control escudo = Cheio(new Control { Name = "Escudo" });
		if (!PuloVazaDeTeste) escudo.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(escudo);
	}

	private static T Cheio<T>(T c) where T : Control
	{
		c.AnchorRight = 1f;
		c.AnchorBottom = 1f;
		c.MouseFilter = Control.MouseFilterEnum.Ignore;
		return c;
	}

	/// <summary>
	/// A MANCHA DE LUZ do meio do fundo: a cor do centro com o alfa caindo como `(1 - d)^2`. Posta sobre a cor do
	/// fundo ela da exatamente a conta da receita (`fundo + 46*mancha`, canal a canal).
	/// </summary>
	private static ImageTexture FazerAMancha()
	{
		const int lado = 128;
		byte[] px = new byte[lado * lado * 4];
		for (int y = 0; y < lado; y++)
			for (int x = 0; x < lado; x++)
			{
				float dx = (x + 0.5f) / lado * 2f - 1f, dy = (y + 0.5f) / lado * 2f - 1f;
				float m = Math.Clamp(1f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
				int i = (y * lado + x) * 4;
				px[i] = (byte)CorDaMancha.R8;
				px[i + 1] = (byte)CorDaMancha.G8;
				px[i + 2] = (byte)CorDaMancha.B8;
				px[i + 3] = (byte)MathF.Round(255f * m * m);
			}
		return ImageTexture.CreateFromImage(Image.CreateFromData(lado, lado, false, Image.Format.Rgba8, px));
	}

	/// <summary>O disco de uma faisca: branco, com a beirada macia. A cor entra na hora de desenhar.</summary>
	private static ImageTexture FazerOPonto()
	{
		const int lado = 32;
		byte[] px = new byte[lado * lado * 4];
		for (int y = 0; y < lado; y++)
			for (int x = 0; x < lado; x++)
			{
				float dx = x + 0.5f - lado / 2f, dy = y + 0.5f - lado / 2f;
				float cobre = Math.Clamp(lado / 2f - 0.5f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
				int i = (y * lado + x) * 4;
				px[i] = px[i + 1] = px[i + 2] = 255;
				px[i + 3] = (byte)MathF.Round(255f * cobre);
			}
		Image img = Image.CreateFromData(lado, lado, false, Image.Format.Rgba8, px);
		img.GenerateMipmaps();   // a faisca tem de 2 a 8 px na tela: sem isso o disco reduzido serrilha
		return ImageTexture.CreateFromImage(img);
	}

	// =====================================================================
	// O QUADRO
	// =====================================================================
	public override void _Process(double delta)
	{
		ulong agora = Time.GetTicksUsec();
		if (_encerrada)
		{
			// A GUARDA DO PULO acabou: o escudo sai e o menu e do jogador.
			if (agora >= _guardaAte) QueueFree();
			return;
		}

		Mirar();
		if (_mirado == null) { Encerrar("o menu saiu de baixo dela"); return; }

		if (!_posada) AndarORelogio(agora);
		Mostrar();
		if (!_posada && _estado.Acabou) Encerrar("pousou");
	}

	/// <summary>
	/// QUEM E O TITULO DO MENU AGORA -- e ele fica APAGADO enquanto a abertura desenha o dela. O menu pode ser
	/// remontado por baixo (uma queda de conexao): o titulo novo e mirado, e o velho, solto.
	/// </summary>
	private void Mirar()
	{
		TituloDoJogo? agora = _alvo();
		if (agora != null && (!IsInstanceValid(agora) || !agora.IsVisibleInTree())) agora = null;
		if (agora == _mirado) return;

		if (_mirado != null && IsInstanceValid(_mirado)) _mirado.Apagado = false;
		_mirado = agora;
		// (defeito injetado: o titulo do menu fica aceso por baixo -- ver `SemApagarOTituloDoMenuDeTeste`)
		if (_mirado != null && !SemApagarOTituloDoMenuDeTeste) _mirado.Apagado = true;
	}

	private void AndarORelogio(ulong agora)
	{
		// O RELOGIO SO LARGA DEPOIS QUE UM QUADRO DELA FOI PRA TELA: o primeiro quadro do processo leva o arranque
		// inteiro nas costas, e a animacao nao pode passar por dentro dele. Ate la a tela fica branca, parada.
		if (!_largou)
		{
			if (Engine.GetFramesDrawn() <= _quadroDeNascimento) return;
			_largou = true;
			_largada = _ultimo = agora;
			_largouAosMs = Time.GetTicksMsec();
			return;
		}

		double passo = (agora - _ultimo) / 1_000_000.0;
		_ultimo = agora;
		_quadros++;
		if (passo * 1000.0 > _piorMs) { _piorMs = passo * 1000.0; _piorEm = _t; }
		if (EmMovimento && passo * 1000.0 > _piorEmMovimentoMs) _piorEmMovimentoMs = passo * 1000.0;

		// (o relogio de parede, e nao o `delta`: o do motor ja vem podado e nao serve pra medir o quadro)
		_t += Math.Min(passo, PassoMaximo);

		// O BAQUE, no quadro em que o logo assenta.
		if (!_tocou && _t >= Impacto)
		{
			_tocou = true;
			AudioDirector.Instance?.Efeito(SomDoImpacto, VolumeDoImpacto);
		}
	}

	/// <summary>Poe na tela o estado do relogio de agora.</summary>
	private void Mostrar()
	{
		_estado = Em(_t);
		if (_mirado != null) _noMenu = _mirado.ArteNaTela;

		_clarao.Color = new Color(1f, 1f, 1f, _estado.Clarao);
		_clarao.Visible = _estado.Clarao > 0.002f;
		_fundo.Modulate = new Color(1f, 1f, 1f, _estado.Fundo);
		_cena.QueueRedraw();
	}

	private void DesenharAsEstrelas()
	{
		Vector2 tela = _estrelas.Size;
		float lado = Math.Max(1f, LadoDaEstrela * Unidade(tela));
		foreach ((float x, float y, float brilho) in _ceu)
			_estrelas.DrawRect(new Rect2(x * tela.X, y * tela.Y, lado, lado), new Color(brilho, brilho, brilho));
	}

	private void DesenharACena()
	{
		Estado e = _estado;
		if (!e.TemLogo) return;

		Vector2 tela = _cena.Size;
		float k = Unidade(tela);
		Vector2 centro = tela / 2f;

		// o brilho e o logo: na subida o brilho vai do pulso da receita ao brilho parado do menu
		TituloDoJogo.DesenharEm(_cena, LugarDoLogo(e, tela, _noMenu), Mathf.Lerp(e.Brilho, TituloDoJogo.BrilhoParado, e.NaSubida), e.AlfaDoLogo);

		// o anel de choque: o traco branco e, na beirada de fora dele, o fio amarelado
		if (e.AlfaDoAnel > 0.002f)
		{
			float rx = e.RaioDoAnel * k, ry = rx * Achatamento;
			TracarAElipse(centro, rx, ry, GrossuraDoAnel * k, Colors.White);
			TracarAElipse(centro, rx, ry, GrossuraDoFio * k, CorDoFio);
		}

		// as faiscas: saem do centro, cada uma com o seu rumo e a sua pressa, e caem
		if (e.AlfaDasFaiscas > 0.002f && _ponto is { } ponto)
		{
			float ti = e.TempoDoImpacto;
			var cor = new Color(CorDasFaiscas, e.AlfaDasFaiscas);
			foreach ((float angulo, float pressa, float raio) in _faiscas)
			{
				var onde = new Vector2(centro.X + MathF.Cos(angulo) * pressa * ti * k,
									   centro.Y + (MathF.Sin(angulo) * pressa * ti * Achatamento + QuedaDasFaiscas * ti * ti) * k);
				float r = raio * k;
				_cena.DrawTextureRect(ponto, new Rect2(onde.X - r, onde.Y - r, 2f * r, 2f * r), false, cor);
			}
		}
	}

	/// <summary>
	/// UMA ELIPSE TRACADA COM RETANGULOS GIRADOS, do lado de DENTRO do contorno (como o `ellipse(..., width=)` do
	/// Pillow). Retangulo, e nao arco: ver "TUDO DESENHADO COM O QUE O LOBBY JA DESENHA" no cabecalho. A cor e OPACA:
	/// os retangulos se cobrem nas emendas, e com transparencia cada emenda sairia mais clara que o traco.
	/// </summary>
	private void TracarAElipse(Vector2 centro, float rx, float ry, float grossura, Color cor)
	{
		float ax = Math.Max(0f, rx - grossura / 2f), ay = Math.Max(0f, ry - grossura / 2f);
		Vector2 de = centro + new Vector2(ax, 0f);
		for (int i = 1; i <= SegmentosDoAnel; i++)
		{
			float a = Mathf.Tau * i / SegmentosDoAnel;
			Vector2 ate = centro + new Vector2(MathF.Cos(a) * ax, MathF.Sin(a) * ay);
			Vector2 d = ate - de;
			_cena.DrawSetTransform(de, d.Angle());
			// meio px a mais em cada ponta: as emendas nao deixam fresta na beirada de fora
			_cena.DrawRect(new Rect2(-0.5f, -grossura / 2f, d.Length() + 1f, grossura), cor);
			de = ate;
		}
		_cena.DrawSetTransform(Vector2.Zero);
	}

	// =====================================================================
	// PULAR, E ACABAR
	// =====================================================================
	/// <summary>
	/// QUALQUER TECLA OU CLIQUE PULA -- e o gesto MORRE AQUI. O `_Input` vem antes da interface e do `_UnhandledInput`
	/// de todo mundo: o clique que pula nao aperta o botao do menu que esta embaixo, e o ESC que pula nao abre as
	/// opcoes. Enquanto a abertura corre (e na guarda depois do pulo) toda tecla e todo botao de mouse param aqui.
	/// </summary>
	public override void _Input(InputEvent e)
	{
		if (e is not (InputEventKey or InputEventMouseButton or InputEventJoypadButton or InputEventScreenTouch)) return;

		if (!_encerrada && EhGestoDePular(e)) Pular();
		// (defeito injetado: o gesto segue pra quem estiver embaixo -- ver `PuloVazaDeTeste`)
		if (!PuloVazaDeTeste) GetViewport().SetInputAsHandled();
	}

	/// <summary>
	/// O QUE PULA: tecla apertada (nao a repeticao de uma tecla presa, e nao as teclas que so acompanham outra --
	/// Shift, Ctrl, Alt, a do Windows, as de trava e a de foto da tela), botao de mouse (nao a rodinha), botao de
	/// controle e toque.
	/// </summary>
	private static bool EhGestoDePular(InputEvent e) => e switch
	{
		InputEventKey { Pressed: true, Echo: false } k => k.Keycode is not (Key.Shift or Key.Ctrl or Key.Alt or Key.Meta
			or Key.Capslock or Key.Numlock or Key.Scrolllock or Key.Print),
		InputEventMouseButton { Pressed: true } m => m.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown
			or MouseButton.WheelLeft or MouseButton.WheelRight),
		InputEventJoypadButton { Pressed: true } => true,
		InputEventScreenTouch { Pressed: true } => true,
		_ => false,
	};

	/// <summary>PULA pro estado final: o titulo no alto, o menu na tela. O escudo fica mais <see cref="GuardaDoPulo"/>.</summary>
	public void Pular()
	{
		if (_encerrada) return;
		Encerrar("pulada", pulada: true);
	}

	/// <summary>
	/// PALCO DA BANCADA: para a abertura num instante do relogio e a deixa la (pra fotografar). Posada ela nao anda,
	/// nao toca, nao pede calma e nao acaba sozinha; some com <see cref="Soltar"/>.
	/// </summary>
	internal void Posar(double t)
	{
		_posada = true;
		_t = t;
		if (IsNodeReady()) Mostrar();
	}

	/// <summary>PALCO DA BANCADA: tira da tela uma abertura posada.</summary>
	internal void Soltar() => Encerrar("solta pela bancada");

	private void Encerrar(string motivo, bool pulada = false)
	{
		if (_encerrada) return;
		_encerrada = true;
		if (Viva == this) Viva = null;

		// O TITULO DO MENU ASSUME no mesmo quadro em que o da abertura some: um titulo so, sem quadro vazio.
		if (_mirado != null && IsInstanceValid(_mirado)) _mirado.Apagado = false;
		_mirado = null;

		if (!_posada)
		{
			ulong agora = Time.GetTicksUsec();
			var corrida = new Corrida(_largou ? (agora - _largada) / 1_000_000.0 : 0.0, _t, _quadros, _piorMs, _piorEm, _piorEmMovimentoMs, pulada, motivo);
			UltimaCorrida = corrida;
			GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
				$"[abertura] fim ({motivo}): largou aos {_largouAosMs} ms do processo; {corrida.Segundos:0.00} s de relogio de parede, {corrida.NoRelogio:0.00} s de animacao, {corrida.Quadros} quadros;"
				+ $" o pior, {corrida.PiorQuadroMs:0.0} ms aos {corrida.PiorQuadroEm:0.00} s (com o titulo em movimento, {corrida.PiorEmMovimentoMs:0.0} ms)"));
		}

		if (!IsInsideTree()) return;
		_fundo.Visible = _cena.Visible = _clarao.Visible = false;

		// PULADA, o escudo ainda fica um instante (ver `GuardaDoPulo`); no fim natural o menu e do jogador na hora.
		if (pulada) _guardaAte = Time.GetTicksUsec() + (ulong)(GuardaDoPulo * 1_000_000.0);
		else QueueFree();
	}
}
