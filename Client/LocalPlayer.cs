using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O personagem QUE VOCE controla. Movimento LIVRE em pixel, calculado localmente (por isso
/// nao ha latencia nenhuma no controle) e enviado ao servidor, que confere.
///
/// A COLISAO E A MESMA DAS DUAS PONTAS: cliente e servidor chamam <see cref="MoveRules"/>
/// sobre o mesmo <see cref="ZoneCollision"/>. Sem isso o cliente atravessa a parede, o
/// servidor recusa, manda correcao, o cliente empurra de novo -- e o sintoma que chega ao
/// jogador e o personagem TREMENDO no muro.
///
/// POSICAO EXATA x POSICAO DESENHADA. Sao duas, de proposito:
///
///   _pos      -- float cru, e a VERDADE da simulacao. Vai pro servidor e pro MoveRules.
///   Position  -- o mesmo valor NUM PONTO DA GRADE, e so isso o motor desenha.
///
/// A razao e o `snap_2d_transforms_to_pixel`: ele arredonda a transformacao de cada objeto
/// na hora de desenhar, mas NAO arredonda a transformacao de canvas que a camera gera. Como
/// a camera e FILHA deste no e le a posicao fracionaria, o corpo pulava de ponto em ponto da
/// grade enquanto o mundo rolava em subpixel: o boneco oscilava a cada quadro. Escrevendo ja
/// na grade, a camera herda o MESMO ponto e nada mais se desloca em relacao a nada.
///
/// ============================ QUAL GRADE -- E POR QUE ESSA E A QUEIXA DO DONO ============================
/// A grade era **pixel de MUNDO** (`Floor(_pos)`), e ai esta o defeito que o dono relatou como
/// *"andando pros lados o personagem fica borrado/tremendo"*. Com a camera em zoom 2, um pixel de
/// mundo sao DOIS pixels de tela: o cenario inteiro so podia rolar de 2 em 2 px de tela, nunca 1,
/// nunca 3. E o passo real de caminhada e 2,3 px de tela por quadro. O desenho saia 2,2,2,4 --
/// nenhum quadro certo, 15% deles com o passo DOBRADO. Isso e o tremor, e ele e SIMETRICO.
///
/// A grade agora e **pixel de TELA** (`Floor(_pos * zoom) / zoom`), que e a menor grade que ainda
/// deixa a arte de pixel nitida -- um pixel de tela e o menor deslocamento que um monitor sabe
/// mostrar. O erro maximo de quantizacao cai de `zoom` px de tela pra 1 px, e a sequencia vira
/// 2,3,2,2,3 (media 2,3, a certa). Em zoom 3 a melhora e de tres pra um.
///
/// O QUE ELA **NAO** FAZ, DE PROPOSITO: sair da grade de pixel de tela. Deslizar em subpixel
/// mataria o tremor de vez, mas ai o motor reamostra a arte e a imagem CINTILA andando -- o remedio
/// pior que a doenca, e o mesmo motivo pelo qual o `Settings.Zoom` e inteiro por construcao. A
/// bancada `--diagrolagem` cobra as duas coisas ao mesmo tempo: erro abaixo de 1 px de tela E o
/// cenario sempre assentando na MESMA fracao de pixel.
///
/// E o que o dono ve como assimetria nao esta neste arquivo: o sprite de perfil tem ~10 px de
/// largura contra ~26 px de altura, entao o mesmo erro vale 10% do corpo indo de lado e 3,8% indo
/// pra cima. O tremor sempre foi igual nos dois eixos; so a fracao visivel dele e que muda.
/// ==========================================================================================================
/// </summary>
public partial class LocalPlayer : Node2D
{
	[Export] public float SpeedStat = 1f;

	/// <summary>A geometria da zona. Nulo = zona sem colisao carregada (anda livre).</summary>
	public ZoneCollision? Mapa;

	/// <summary>
	/// OS OUTROS CORPOS -- o segundo plano da mesma pergunta que o <see cref="Mapa"/> responde.
	///
	/// O objeto e criado UMA vez e reenchido por quadro (ver `World.MontarGradeDeCorpos`): o
	/// `Recomecar` e O(1) e a grade nao aloca por tique, entao isto nao entra na conta do quadro.
	/// </summary>
	private readonly Jandirus.Core.World.GradeDeCorpos _corpos = new();

	private CharacterVisual _visual = null!;

	/// <summary>
	/// ============================ DERIVADO, PORQUE 40 JA FICOU VELHO ============================
	/// Segundos que o corpo pode ficar sem poder ANDAR antes de o cliente destravar sozinho. E uma
	/// rede: "preso pra sempre" nao pode ser um estado alcancavel.
	///
	/// Isto era `40.0` cravado, com um comentario meu dizendo "generoso: a cena mais longa do jogo
	/// prende 32". Era verdade no dia em que escrevi. Depois as cenas voltaram aos tempos reais do
	/// DM e a do SSJ3 passou a prender 140 s -- entao a rede disparava aos 40 e devolvia o
	/// controle NO MEIO da cinematica. O dono viu: "durante a cinematica do nada o jogo libera ele
	/// andar dnv".
	///
	/// O salva-vidas virou o afogamento, e por um motivo que o proprio projeto ja sabia evitar: o
	/// outro cao de guarda (`Transformacao.PrazoMaximoPreso`) e `CenaMaisLonga * 2` e acompanhou a
	/// mudanca sozinho. Este ficou pra tras porque eu escrevi um NUMERO onde cabia uma CONTA.
	///
	/// Agora ele sai do catalogo: o dobro da cena mais longa, com piso de 40 s pra o caso de
	/// alguem encurtar tudo. Cena nova, por mais longa que seja, ja nasce coberta.
	/// ================================================================================
	/// </summary>
	/// <summary>A rede, pra bancada afirmar que ela cobre a cena mais longa.</summary>
	public static double SegundosAteDestravarDeTeste => SegundosAteDestravar;

	private static double SegundosAteDestravar =>
		Math.Max(40.0, Jandirus.Core.Forms.Cinematicas.CenaMaisLonga * 2.0);

	private double _presoHa;


	private Facing _facing = Facing.South;
	private Vec2 _pos;
	private double _sendAccumulator;
	private const double SendInterval = 1.0 / 30.0; // manda estado na mesma taxa do tick do servidor

	private Protocol.Activity _atividade = Protocol.Activity.Parado;
	private double _ataqueAte;
	private bool _guarda;

	/// <summary>
	/// SHIFT segurado. E um PEDIDO: o servidor concede enquanto houver Ki e cobra por segundo
	/// (ver `GameServer.PodeCorrer`). Andar mais rapido do que ele concedeu so gera correcao,
	/// entao quando o Ki acaba o cliente volta sozinho ao passo normal -- a ficha avisa.
	/// </summary>
	private bool _correndo;

	/// <summary>
	/// A CADENCIA DO SOCO, em segundos, dita pelo SERVIDOR (chega na ficha). Nao e uma
	/// constante porque nao e fixa: ela sai do `Eactspeed`, que cai quando o personagem
	/// carrega Ki -- carregar poder deixa a luta mais rapida, nao so mais forte. Usar o
	/// numero do servidor tambem garante que este cliente nunca tente um golpe que la vai
	/// ser recusado por recarga.
	/// </summary>
	private double _cadencia = Protocol.AttackPoseMs / 1000.0;

	// ============================== VOO ==============================

	/// <summary>
	/// A minha altura, em pixels. NAO E PREVISTA: quem sobe, desce, cobra o Ki e derruba por
	/// exaustao e o servidor (`GameServer.Voo.cs`), e ela chega pelo snapshot.
	///
	/// Prever altura localmente seria pior do que inutil: o cliente nao sabe quanto Ki sobrou depois
	/// do dreno deste tique, entao ele erraria justamente no instante em que a altura importa -- o
	/// da queda por exaustao --, e o corpo desceria no servidor enquanto continuava subindo na tela.
	/// </summary>
	private float _altitude;

	/// <summary>
	/// A ALTURA QUE SE DESENHA -- a do servidor perseguida QUADRO A QUADRO.
	///
	/// ============================ MEDIDO: 81% DOS QUADROS PARADOS ============================
	/// A altura chega no SNAPSHOT, a 30 Hz, e a tela roda muito acima disso. Aplicada crua, a
	/// bancada mediu 81% dos 483 quadros da subida sem mexer nada -- e como a CAMERA segue a altura
	/// (`AplicarAltura`), o que treme nao e o boneco: e o mundo inteiro.
	///
	/// E o MESMO problema que o arremesso ja resolvia fatiando o passo grosso do DM no tique do
	/// servidor (`TickDoEmpurrao`), so que um andar acima: ali era passo de 0,1 s dentro de tique de
	/// 33 ms; aqui e pacote de 33 ms dentro de quadro de 8 ms. Andar e correr nunca tiveram isso
	/// porque o corpo local integra no PROPRIO quadro e o remoto tem buffer com atraso.
	///
	/// A SEPARACAO E O QUE IMPORTA: isto e SO desenho. Quem decide se o corpo atravessa parede
	/// continua sendo <see cref="_altitude"/>, o valor cru do servidor -- suavizar a REGRA poria as
	/// duas pontas em desacordo por alguns quadros, que e como nasce correcao de posicao em jogo
	/// honesto.
	/// ========================================================================================
	/// </summary>
	private float _altitudeNaTela;

	/// <summary>
	/// Quanto a altura desenhada persegue a do servidor, por segundo.
	///
	/// 25 nao e chute: a subida do servidor e de 6 tiles/s (192 px/s), e o atraso permanente de uma
	/// perseguicao exponencial e `velocidade / taxa` -- 192/25 da menos de 8 px de altura, ou 2 px
	/// no desenho. Mais lento arrasta visivelmente; mais rapido volta a deixar quadro parado.
	/// </summary>
	private const float PerseguicaoDaAltura = 25f;

	/// <summary>O que ela ja foi no quadro anterior -- so pra nao repintar o que nao mudou.</summary>
	private float _altitudeDesenhada = -1f;

	private SombraDeVoo? _sombra;

	/// <summary>
	/// SONDA: quadros com altura, e quantos deles NAO mexeram o desenho. So pras bancadas.
	///
	/// A pergunta que ela responde: a altura anda no ritmo do QUADRO ou no do PACOTE? Se metade dos
	/// quadros nao mexe nada, ela esta andando a 30 Hz contra uma tela de 60 -- e o mundo inteiro
	/// treme junto, porque a camera segue a altura.
	/// </summary>
	public static int QuadrosDeAltura, QuadrosParadosDeAltura;

	/// <summary>
	/// A altura DESENHADA, em pixels. E o que o mundo usa pra nevoa, veu e zoom, e o que a bancada
	/// le -- tudo isso e desenho, e desenho anda no ritmo do quadro.
	/// </summary>
	public float Altitude => _altitudeNaTela;

	/// <summary>
	/// Estou acima do cenario? Pergunta de REGRA, entao usa o valor CRU do servidor -- e a MESMA
	/// pergunta que ele faz, pelo mesmo numero. Suavizar aqui poria as duas pontas em desacordo.
	/// </summary>
	public bool AtravessandoCenario => Voo.AtravessaCenario(_altitude);

	/// <summary>O servidor mandou a minha altura. Ver o laco de snapshot em <c>World</c>.</summary>
	public void ReceberAltura(bool voando, float altitude)
		=> _altitude = voando ? altitude : 0f;

	/// <summary>
	/// O SERVIDOR MANDOU: EU ESTOU COM UM RAIO NA MAO (ou parei de estar).
	///
	/// Irmao exato do <see cref="ReceberAltura"/> logo acima, e chega pelo mesmo laco de snapshot no
	/// `World`. Ver o comentario de la -- este estado e do servidor de ponta a ponta, e nao ha versao
	/// local dele pra espelhar.
	/// </summary>
	public void ReceberCanalDeKi(bool canal, bool atirando)
	{
		_canalDeKi = canal;
		_raioSaindo = canal && atirando;
	}

	/// <summary>Ha um canal de ki de pe neste corpo -- carregando OU atirando. Ver `Protocol.Pose.Canalizando`.</summary>
	private bool _canalDeKi;

	/// <summary>
	/// O raio JA ESTA SAINDO da mao (o `beaming` do DM), e nao so sendo reunido.
	///
	/// SAO DOIS CAMPOS E NAO UM `enum` de tres estados de proposito: cada um responde a UMA pergunta
	/// que ja tem consumidor -- `_canalDeKi` diz "o corpo esta preso" e `_raioSaindo` diz "a pose e a
	/// do feixe". Um enum obrigaria os dois leitores a conhecer os tres valores pra responder a sua
	/// metade.
	/// </summary>
	private bool _raioSaindo;

	/// <summary>
	/// ESTOU NADANDO. Vem da FICHA (`SheetState.Nadando`), e nao do snapshot nem de um palpite local.
	///
	/// ============================ POR QUE ELE NAO E LOCAL ============================
	/// Nadar parece um estado do teclado (aperta N, liga), e nao e: o servidor pode RECUSAR (fora
	/// d'agua, nocauteado) e pode DESLIGAR sozinho (a agua acabou, o Ki acabou). Um espelho local
	/// otimista acertaria no caso comum e mentiria exatamente nos quatro casos em que a resposta
	/// importa -- e um cliente que se acha nadando em terra seca preveria o passo por uma regra que
	/// o servidor ja nao aceita, o que vira correcao de posicao (o corpo tremendo) em jogo honesto.
	///
	/// A FICHA E CONTINUA (5 Hz, e na hora quando muda), entao nao ha evento a perder. Mesmo canal e
	/// mesmo motivo do `Empurrado`, que e a outra coisa que muda COMO este corpo atravessa o mundo.
	/// ================================================================================
	/// </summary>
	private bool _nadando;

	/// <summary>
	/// COMO ESTE CORPO ESTA ATRAVESSANDO AGORA -- a MESMA funcao que o servidor chama
	/// (<c>ClasseDeAgua.ModoDe</c>), com o mesmo estado, pra que nao exista a faixa em que uma ponta
	/// passa pela agua e a outra recusa.
	///
	/// O `_empurrado` nao entra: durante o arremesso este metodo nem e alcancado -- o `_Process`
	/// devolve antes, deslizando ate a posicao que o servidor confirmou (e la o modo `Arremessado`
	/// e escolhido no `TickDoEmpurrao`, no servidor).
	/// </summary>
	private ModoDeTravessia ModoDoCorpo =>
		ClasseDeAgua.ModoDe(arremessado: false, noAr: _altitude > 0f, nadando: _nadando);

	/// <summary>
	/// ============================ O SERVIDOR ESTA DIRIGINDO ESTE CORPO ============================
	/// Chega no snapshot, 30x/s, junto da altura (ver `World.AoReceberSnapshot`). Enquanto
	/// <paramref name="semRedeas"/> for verdade este corpo e PASSAGEIRO: a posicao vira alvo a
	/// perseguir e a pose vem de quem dirige, exatamente como um `RemotePlayer` sempre fez.
	///
	/// O ESTADO NAO TEM PRAZO NEM CONTADOR de proposito. Ele e reafirmado a cada pacote, entao no
	/// quadro em que o servidor parar de dizer "sem redeas" o corpo volta a ser do dono -- e nao ha
	/// como isto virar a trava permanente que ja travou jogador neste projeto (o portao global de
	/// input, cujo contador `static` sobrevivia ate ao respawn -- ver `_Process`).
	/// ============================================================================================
	/// </summary>
	public void ReceberPosse(bool semRedeas, Vec2 pos, Facing facing, bool andando, Protocol.Pose pose)
	{
		if (semRedeas && !_semRedeas)
		{
			// COMECOU AGORA. O corpo local guarda estados que o dono ligou e que o servidor acabou de
			// desligar sozinho (`TomarAsRedeas` zera a guarda). Sem esta faxina, largar as teclas
			// depois da possessao nao geraria pacote -- as duas pontas discordariam calado.
			_alvoDaPosse = pos;               // comeca perseguindo de onde estou: nada de salto inicial
			_guarda = false;
			_correndo = false;
			if (Destino != null) { Destino = null; Chat.Sistema("piloto automatico desligado."); }
			Chat.Sistema("o seu corpo nao te obedece.");
		}

		_semRedeas = semRedeas;
		if (!semRedeas) return;

		_alvoDaPosse = pos;
		_facing = facing;      // pra as redeas voltarem com o corpo olhando pra onde ele esta olhando
		_andandoNaPosse = andando;

		// A POSE DE GOLPE E EVENTO, nao estado: reinicia a animacao a cada soco novo, senao uma
		// sequencia de golpes vira um ciclo continuo. E o MESMO tratamento do `RemotePlayer.Receive`
		// -- e tem que ser, porque quem esta desenhando e o mesmo servidor pros dois.
		if (pose == Protocol.Pose.Atacando && _poseDaPosse != pose)
			_visual.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
		else _visual.SetPose(pose);
		_poseDaPosse = pose;
	}

	/// <summary>Quem dirige este corpo e o servidor. Ver <see cref="ReceberPosse"/>.</summary>
	private bool _semRedeas;

	/// <summary>Ate onde quem dirige ja levou o corpo -- o cliente persegue este ponto.</summary>
	private Vec2 _alvoDaPosse;

	/// <summary>Andando e pose ditados por quem dirige. A animacao sai daqui, e nao do teclado.</summary>
	private bool _andandoNaPosse;
	private Protocol.Pose _poseDaPosse = Protocol.Pose.Normal;

	/// <summary>
	/// Quanto o corpo persegue, por segundo, o ponto que o servidor mandou.
	///
	/// PERSEGUIR e nao teleportar: o ponto chega a 30 Hz e a tela roda acima disso, entao aplica-lo
	/// cru deixaria a maioria dos quadros parada e o resto saltando -- e como a camera e FILHA deste
	/// no, o que treme nao e o boneco, e o mundo inteiro. E o mesmo argumento (e o mesmo remedio) da
	/// altura de voo, ver <see cref="PerseguicaoDaAltura"/>.
	///
	/// 20 deixa o atraso permanente em `velocidade / taxa` -- uns 10 px na velocidade de caminhada,
	/// constante, que nao se acumula. Salto grande (teleporte, troca de zona) nao se interpola: ver
	/// <see cref="SaltoDaPosse"/>.
	/// </summary>
	private const float PerseguicaoDaPosse = 20f;

	/// <summary>
	/// Acima disto o ponto novo nao e perseguido, e CRAVADO. E o mesmo corte do `RemotePlayer`:
	/// corpo nenhum anda tanto entre dois snapshots, entao so ha uma explicacao possivel (teleporte,
	/// volta do planeta, troca de zona) -- e teleporte nao se desliza.
	/// </summary>
	private const float SaltoDaPosse = 160f;

	public override void _Ready()
	{
		_visual = GetNode<CharacterVisual>("Visual");
		_sombra = new SombraDeVoo { Name = "SombraDeVoo" };
		AddChild(_sombra);
		_pos = new Vec2(Position.X, Position.Y);   // o World ja nasceu com o spawn no construtor
		Desenhar();

		if (GameClient.Instance is not { } cli) return;

		cli.Corrected += OnCorrected;
		// A velocidade vem da FICHA que o servidor calculou. Andar mais rapido do que ele
		// concedeu so gera correcao, entao nao existe motivo pra divergir.
		cli.SheetUpdated += OnSheet;
		if (cli.Sheet.SpeedStat > 0) SpeedStat = cli.Sheet.SpeedStat;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Corrected -= OnCorrected;
		cli.SheetUpdated -= OnSheet;
	}

	private void OnSheet(SheetState ficha)
	{
		if (ficha.SpeedStat > 0) SpeedStat = ficha.SpeedStat;
		if (ficha.SocoMs > 0) _cadencia = ficha.SocoMs / 1000.0;
		if (ficha.Empurrado && !_empurrado) _alvoDoVoo = _pos;   // comeca o voo de onde estou
		_caido = ficha.Imobilizado;
		_deitado = EstouDeitado(ficha);
		// O ANGULO DO CORPO DEITADO VEM DO SERVIDOR, e SO daqui -- 2 bits no Estado, aplicados a CADA
		// pacote, como o `RemotePlayer` faz com o snapshot. Ter duas fontes (o `_facing` local e o do
		// servidor) foi o defeito que o dono fotografou nas duas telas, e depois o corpo "girando
		// bugado" no arremesso: um escrevia o angulo por quadro e o outro o desfazia.
		//
		// ============================ NAO E "SO NO INSTANTE DA QUEDA", E ISSO E DO DM ============================
		// Havia aqui um `caiuAgora` calculado e nunca usado, e um comentario prometendo girar so na
		// queda. Quem decide se o corpo caido acompanha e o SERVIDOR: o vivo derrubado gira quando
		// apanha (`M.dir = get_dir(M,src)`, `CombatMovement.dm:302`) e o morto nunca gira (o cadaver do
		// DM e um `/obj`, e aqui e o `ServerPlayer.ApontarRumoDoGolpe` que o recusa). Se ESTA ponta
		// aplicasse o angulo uma vez so, a minha tela e a de quem me olha discordariam no primeiro
		// golpe que me virasse no chao -- que e exatamente o par de fotos de onde este bloco nasceu.
		// =======================================================================================================
		// O NOCAUTE usa o sprite DEITADO; o arremesso usa o ACORDADO. Sao duas tabelas de rotacao
		// diferentes -- ver `CharacterVisual.VoarPara`.
		var dir = (Facing)((ficha.Estado >> 6) & 3);
		_dirDoCorpo = dir;   // a folha do voo tambem sai daqui -- ver _Process
		if (_deitado) _visual.DeitarPor(dir);
		// O PUXAO DA FUSAO NAO GIRA O CORPO. Ele e dirigido pelo servidor como o arremesso (mesmo bit
		// `Empurrado`, mesma correcao, mesmo deslize), mas quem esta sendo puxado pro outro esta DE PE
		// -- com a rotacao do arremesso, dois lutadores atraidos no eixo vertical apareceriam de cabeca
		// pra baixo deslizando um pro outro. Ver `SheetState.PuxadoNaFusao`.
		else if (ficha.Empurrado && !ficha.PuxadoNaFusao) _visual.VoarPara(dir);
		else _visual.GirarPara(default);
		_empurrado = ficha.Empurrado;   // o servidor esta dirigindo o corpo: ver _Process
		_puxadoNaFusao = ficha.PuxadoNaFusao;   // ...e este diz que ele NAO esta sendo arremessado
		_nadando = ficha.Nadando;       // ...e este diz por ONDE ele passa: ver _nadando
		_visual.MostrarRabo(ficha.Rabo);
		// 3% de folga sobre o custo de um segundo de corrida: no fio do Ki, correr e desistir
		// a cada quadro daria um solavanco a cada passo
		_temKiPraCorrer = ficha.MaxKi <= 0 || ficha.Ki > ficha.MaxKi * 0.03;

		// A FICHA NAO ACENDE MAIS AURA NENHUMA.
		//
		// Ela acendia -- `Definir(_carregando || _sobrecarregado, ...)` -- e esse era o SEGUNDO
		// ponto do defeito que o dono viu: mesmo consertando a tecla, o proximo pacote de ficha
		// (varios por segundo) religava a luz que o servidor tinha negado. Quem sabe se a carga
		// esta acontecendo e o servidor, e ele ja avisa pelo canal de efeito.
	}

	/// <summary>Nocauteado ou morto: o servidor recusa qualquer passo, entao aqui nem se tenta.</summary>
	private bool _caido;

	/// <summary>
	/// ============================ "NAO POSSO AGIR" E "ESTOU NO CHAO" SAO DUAS COISAS ============================
	/// Eram uma so -- <see cref="SheetState.Imobilizado"/> (`KO || Morto`) mandava nas duas -- e a
	/// morte virou um PERCURSO no meio do caminho: hoje um morto pode estar **de pe**, andando no
	/// Outro Mundo (`Alem.MortoDePe`, o `spawn Un_KO()` que o `Death.dm:89` faz ANTES de mover).
	///
	/// Com uma so, o dono da tela via a si mesmo DEITADO no Outro Mundo -- corpo girado 90 graus,
	/// sprite `ko`, e a auréola desenhada AO LADO da cabeca (a folha tem um estado `ko` proprio, que
	/// e o desenho pra um corpo caido) -- enquanto o servidor (`ServerPlayer.Pose`/`Deitado`) e TODAS
	/// as outras telas o desenhavam de pe com a auréola sobre a cabeca. As duas telas discordando
	/// sobre o mesmo corpo, que e a familia de defeito que este port ja pagou duas vezes (o corpo
	/// girando no arremesso, a chama do vizinho).
	///
	/// A REGRA E A DO SERVIDOR, LIDA DA MESMA FUNCAO -- nao ha copia aqui: `Alem.MortoDePe` mora no
	/// `Core` e e a mesma que o `ServerPlayer.Deitado` pergunta. O `Empurrado` (o arremesso) continua
	/// tendo tabela propria, que e o `TiquesDeVoo` do outro lado.
	///
	/// **`_caido` NAO MUDOU**, e isso e deliberado: quem recusa o passo do morto e o servidor
	/// (`PodeMexerOCorpo` tem `!dead`), e deixar o cliente tentar andar so produziria correcao de
	/// posicao -- o elastico. Aqui mudou o DESENHO, que era o que estava mentindo.
	/// ========================================================================================================
	/// </summary>
	private bool _deitado;

	/// <summary>
	/// O <see cref="ServerPlayer.Deitado"/> do lado de ca, menos o arremesso (que ja tem caminho
	/// proprio logo acima). Ver <see cref="_deitado"/>.
	/// </summary>
	private static bool EstouDeitado(SheetState f) =>
		f.KO || (f.Morto && !Jandirus.Core.World.Alem.MortoDePe(true, GameClient.Instance?.Zone.Name ?? ""));

	/// <summary>
	/// PRENSADO NO CHAO pela gravidade ou pelo peso -- a mesma regra que o servidor aplica em
	/// `PodeMexerOCorpo`, lida da MESMA funcao do `Core` e do MESMO numero que veio no fio.
	///
	/// E uma propriedade e nao um campo de proposito: nao ha o que sincronizar nem o que esquecer de
	/// limpar. Tirar o peso ou sair do planeta muda a razao no proximo pacote de atributos e o corpo
	/// anda de novo -- sem um bit pendurado esperando alguem apaga-lo.
	/// </summary>
	private static bool Prensado =>
		GameClient.Instance is { } c
		&& c.Atributos.Esmagamento >= Jandirus.Core.Stats.Esmagamento.RazaoQuePrende;

	/// <summary>Estou sendo ARREMESSADO -- quem move o corpo agora e o servidor.</summary>
	private bool _empurrado;

	/// <summary>
	/// ...E O QUE ME MOVE E O PUXAO DA FUSAO, e nao um golpe. Ver <see cref="SheetState.PuxadoNaFusao"/>:
	/// o <see cref="_empurrado"/> continua ligado (ele e o que faz este cliente parar de integrar tecla),
	/// e este bit tira as duas coisas que sao do ARREMESSO e nao do puxao -- a rotacao de 90 graus do
	/// corpo e a direcao "de onde veio a pancada".
	/// </summary>
	private bool _puxadoNaFusao;

	/// <summary>
	/// ============================ POR QUE O CORPO NAO ANDA AGORA -- UM LUGAR SO ============================
	/// Vazio = ele anda. E esta propriedade **e a propria condicao** que zera o vetor de andar la no
	/// `_Process` (o `input = ... ? Vector2.Zero : ...`), e nao uma copia dela: ter a lista escrita
	/// duas vezes seria garantir que um dia elas discordem, e a que mente e sempre a que ninguem le.
	///
	/// ELA EXISTE PORQUE "O CORPO NAO SAIU DO LUGAR" NAO E UM DIAGNOSTICO. Sao NOVE estados diferentes
	/// que prendem o corpo, cada um por um motivo legitimo, e de fora eles sao todos identicos: zero
	/// pixel. A bancada da mudez perdeu duas rodadas inteiras adivinhando qual deles era (primeiro
	/// parede, depois o arremesso do golpe de saida do embate -- e nao era nenhum dos dois todas as
	/// vezes), e o unico jeito de isso nao se repetir e o proprio corpo dizer o nome.
	///
	/// OS DOIS PRIMEIROS SAO INALCANCAVEIS DAQUI de proposito: o `_semRedeas` e o `_empurrado` fazem o
	/// `_Process` RETORNAR antes desta linha (por isso incluí-los nao muda o jogo em nada). Eles estao
	/// aqui porque de FORA eles sao a mesma coisa que os outros sete -- um corpo que nao anda --, e
	/// quem le esta propriedade esta perguntando exatamente isso.
	/// ==================================================================================================
	/// </summary>
	public string PorQueNaoAnda =>
		  _semRedeas                             ? "sem as redeas (o servidor dirige)"
		: _empurrado                             ? "arremessado"
		: _caido                                 ? "caido (nocaute ou morte)"
		: _carregando                            ? "carregando Ki (C)"
		: _canalDeKi                             ? "com um canal de Ki de pe"
		: Foco.Digitando                         ? "campo de texto em foco"
		: GameClient.Instance?.EmClash == true    ? "num embate"
		: Transformacao.PrendendoOCorpo           ? "preso pela cinematica de transformacao"
		: Prensado                                ? "prensado pela gravidade (ou pelo peso)"
		: "";

	/// <summary>
	/// A DIRECAO DO CORPO DEITADO/VOANDO, ditada pelo servidor.
	///
	/// Nao e o `_facing`. O `_facing` e pra onde eu OLHO, e durante o voo ele fica congelado na
	/// ultima direcao antes do golpe -- o `_Process` volta antes das linhas que o atualizam. O
	/// observador, esse, recebe a direcao do ARREMESSO e escolhe a folha por ela. Resultado: quem
	/// era jogado pro leste olhando pro sul se via como `walk_south` girado e todo mundo o via como
	/// `walk_east` girado. Mesmo angulo, desenhos diferentes.
	///
	/// E o mesmo argumento que ja vale pro angulo: UMA fonte. Ter duas foi o defeito que o dono
	/// fotografou nas duas telas.
	/// </summary>
	private Facing _dirDoCorpo = Facing.South;

	/// <summary>
	/// PRA ONDE ESTE CORPO ESTA VIRADO -- o mesmo nome que o `RemotePlayer` ja publicava, pra que
	/// quem le a direcao de um corpo na tela leia a MESMA coisa nos dois.
	///
	/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
	/// O rastro da agua perguntava a direcao com `corpo is RemotePlayer r ? r.OlharDeTeste : South`.
	/// O corpo do DONO nao e `RemotePlayer` -- ele caia no `else` e recebia `South` FIXO, ou seja
	/// eixo norte-sul sempre. Foi exatamente o que o dono fotografou: voando de leste pra oeste sobre
	/// a agua, a onda continuava desenhada de pe. Quem visse OUTRO jogador voando de lado veria a
	/// onda certa; so o proprio corpo estava travado, porque so ele nao tinha por onde ser lido.
	/// =======================================================================================
	///
	/// NAO E UM SEGUNDO RUMO: e o mesmo par de campos que o `_Process` usa pra escolher a folha do
	/// sprite, devolvido na MESMA ordem. Arremessado, quem manda e o `_dirDoCorpo` (o `_Process`
	/// volta antes das linhas que atualizam o `_facing`, e desenhar por ele daria uma onda apontando
	/// pra onde o corpo olhava ANTES do golpe); nos outros casos e o `_facing`. Guardar um rumo
	/// proprio aqui seria criar a segunda fonte que ja custou caro neste arquivo.
	///
	/// PARADO NAO VOLTA PRO PADRAO: o `_facing` so muda quando ha tecla de direcao
	/// (`MoveRules.FacingFrom` devolve `atual` com vetor nulo), entao pairar sobre a agua mantem a
	/// ultima onda -- que e o que um corpo parado no ar faz.
	/// </summary>
	public Facing OlharDeTeste => _empurrado ? _dirDoCorpo : _facing;

	/// <summary>
	/// A POSICAO EXATA, o float cru antes de qualquer arredondamento. So pras bancadas.
	///
	/// O <c>World.PosicaoLocal</c> devolve o <c>GlobalPosition</c>, que e a posicao **DESENHADA** --
	/// ja passada pelo <see cref="Desenhar"/>. Medir o tremor com ela seria comparar o resultado do
	/// arredondamento consigo mesmo: o erro daria zero sempre. Quem mede quantizacao precisa das
	/// DUAS pontas, e esta e a de cima.
	/// </summary>
	public Vec2 PosicaoExataDeTeste => _pos;

	/// <summary>Ainda ha Ki pro servidor conceder a corrida.</summary>
	private bool _temKiPraCorrer = true;

	/// <summary>
	/// PRA ONDE O PILOTO AUTOMATICO ESTA INDO (nulo = ninguem pilotando). E o nav system: o
	/// jogador escolhe um planeta na aba Nav e o corpo vai sozinho, no passo normal.
	/// </summary>
	public Vec2? Destino;

	/// <summary>SHIFT segurado AGORA -- o modificador do golpe, independente de estar andando.</summary>
	private bool _shift;

	/// <summary>
	/// Anda no QUADRO DE RENDER, nao no passo de fisica.
	///
	/// Este no nao usa fisica do Godot (a colisao e nossa, por mapa de bits), e integrar a
	/// 60 Hz fixo enquanto a tela desenha noutra cadencia faz alguns quadros receberem dois
	/// passos e outros nenhum -- tremor de amostragem, visivel como engasgo. Uma atualizacao
	/// por quadro desenhado resolve na raiz. O `Advance` ja e por dt, com teto pra alt-tab.
	/// </summary>
	public override void _Process(double delta)
	{
		// ============================ A REDE PRECISA DE UM LUGAR SEM SAIDA ============================
		// A vigilancia da tranca estava no caminho de FISICA -- e aquele metodo tem `return` antes
		// dela (corpo arremessado, e o ramo de voo). Bastava o jogador estar num desses estados pra
		// a rede nunca rodar, que e o pior lugar possivel pra por uma rede: ela falharia exatamente
		// nos casos estranhos, que sao os que produzem o defeito.
		// ================================================================================================
		Transformacao.VigiarTranca(delta);

		// ============================ SEM AS REDEAS, ESTE CORPO E PASSAGEIRO ============================
		// Enquanto o servidor dirige (a fera solta, e qualquer possessao que venha depois) este quadro
		// nao le UMA tecla: o vetor de andar, o SHIFT, o piloto automatico e o `LerAcoes` ficam todos
		// abaixo deste ponto. E a diferenca entre "o dono nao consegue mexer" e "o dono mexe e nao
		// adianta", que foi o que ele viu: *"eu ainda posso tentar mexer ai ele faz animaçao mas
		// continua deslizando"* -- a tecla dava o passo local (animacao de andar) e a posicao vinha da
		// IA (deslize). Duas fontes escrevendo o mesmo corpo.
		//
		// A POSE VEM DE QUEM DIRIGE, e e isso que mata o deslize: a animacao deixa de ser medida no
		// passo do teclado (`andando = _pos - antes`, la embaixo) e passa a ser o `Moving` que a IA
		// escreveu -- o MESMO campo pelo qual todo mundo ja via o macaco andando na tela deles.
		//
		// A REDE FICA ACIMA de proposito (`VigiarTranca`): este `return` e mais um caminho sem saida, e
		// a rede num caminho sem saida nao vigia nada.
		//
		// NAO MANDA `SendState`. Nao ha o que declarar -- o servidor recusaria o passo e responderia
		// uma correcao por PACOTE (trinta por segundo) so pra repetir o que o snapshot ja diz.
		if (_semRedeas)
		{
			Vec2 falta = _alvoDaPosse - _pos;
			_pos = falta.LengthSquared > SaltoDaPosse * SaltoDaPosse
				? _alvoDaPosse                                    // teleporte nao se desliza
				: _pos + falta * MathF.Min(1f, (float)(delta * PerseguicaoDaPosse));
			Desenhar();
			_visual.SetMotion(_facing, _andandoNaPosse);

			// O rastro e o borrao de corrida sao do DONO correndo. Possessao pode durar um minuto, e
			// um rastro pendurado nesse tempo todo e um efeito que ninguem sabe de onde veio.
			_visual.Correr(false, Vector2.Zero);
			if (GetNodeOrNull<RastroDeCorrida>("Rastro") is { } rastroParado) rastroParado.Definir(false);

			// A ALTURA CONTINUA ANDANDO. Ela e do servidor tambem, mas quem a DESENHA e este quadro --
			// pular estas duas linhas congelaria a sombra e o zoom de quem foi possuido no ar.
			SeguirAltura(delta);
			AplicarAltura();

			// A UNICA TECLA QUE CONTINUA VALENDO: M. Meditar e a saida da fera (o `angertick` do DM),
			// e o servidor deixa esse pacote passar justamente pra que a paralisia tenha resposta.
			// Sem esta linha o dono ficaria com a saida bloqueada pelo proprio cliente.
			LerAtividade(andando: false, soASaida: true);
			return;
		}

		// ESCREVENDO NO CHAT NAO SE JOGA. `Input.IsActionPressed` le a TECLA FISICA e nao sabe
		// que ha um campo de texto com foco -- sem esta guarda, escrever "sai da frente" faz o
		// personagem andar pra direita (D), treinar (T) e mirar na cabeca (1).
		// CARREGANDO NAO SE ANDA -- e nao e "rende menos", e nao anda.
		//
		// A versao anterior so SUSPENDIA o ganho em movimento, e o dono voltou: "ao apertar C o
		// personagem tem q ficar PARADO e n pode se mexer enquanto carrega". Ele esta certo e o
		// motivo e de leitura: um personagem que anda enquanto "carrega" nao mostra que a carga
		// parou -- ele parece estar carregando andando, que e justamente o que a regra proibe.
		// Travar o corpo faz a regra ser OBVIA sem uma linha de texto.
		// NO EMBATE O CORPO NAO E MEU. Durante o ZanzoClash quem escreve a posicao e o servidor,
		// que recoloca os dois a cada cruzamento -- se as teclas continuassem valendo, o jogador
		// brigaria com o proprio efeito e receberia trinta correcoes por segundo.
		// A CINEMATICA DE ESTREIA PRENDE O CORPO -- o `move = 0` do DM. So pelo tempo de
		// `Cinematica.SegundosPreso`, que vai ate o beat em que a forma FICA -- ver o comentario la
		// sobre por que o prazo e a soma dos `sleep` do DM. Eu tinha escrito aqui que "vinte segundos
		// parado com alguem socando voce nao e cinematografia" e por isso comprimi as cenas; o dono
		// decidiu o contrario, com a pergunta na mao: prazos como o DM, e o corpo preso o tempo
		// inteiro da transformacao -- 140 s no SSJ3. Quem joga decide o que e longo demais.
		// ============================ SO O MOVIMENTO E BLOQUEADO ============================
		// Eu tinha trocado isto por um portao GLOBAL que barrava as 15 leituras de tecla deste
		// arquivo. Ninguem pediu: o dono queria a aura consertada, e disse depois, com todas as
		// letras, que "o personagem ficar proibido de se mover na animaçao ja tava funcionando".
		//
		// O portao global tinha um modo de falha que este nao tem: o contador e `static` e
		// sobrevive a troca de zona, a morte e ao respawn, entao um unico vazamento travava o
		// jogador PARA SEMPRE -- e foi o que aconteceu. Zerar so o vetor de andar, no pior caso,
		// deixa alguem parado enquanto a cena roda.
		// ================================================================================
		// ============================ PRENSADO PELA GRAVIDADE (OU PELO PESO) ============================
		// O `gravParalysis` do original (`Gravity.dm:67` -> `movement handler.dm:132`): a partir de
		// quatro vezes a maestria de gravidade -- ou quatro vezes o que o corpo aguenta de peso -- o
		// corpo fica PRESO no chao. Ele continua socando e defendendo; so nao sai do lugar.
		//
		// A GUARDA PRECISA ESTAR DOS DOIS LADOS. O servidor ja recusa o passo pelo `PodeMexerOCorpo`;
		// sem esta linha o cliente continuaria integrando o input e levando uma correcao por pacote --
		// trinta por segundo, o personagem TREMENDO no lugar. E o modo de falha classico deste port, e
		// a defesa e sempre a mesma: as duas pontas decidindo pelo MESMO numero e pela MESMA funcao.
		//
		// O numero vem da ficha lenta (`AtributosState.Esmagamento`) porque `SheetState.Estado` nao
		// tem mais bit livre -- os dois ultimos sao a direcao de quem esta caido.
		// ============================================================================================
		// ============================ E COM UM RAIO NA MAO O CORPO FICA PLANTADO ============================
		// `canmove = 0` na carga (`beams.dm:294`) e ate o `stopbeaming()` (`beams.dm:221`), lido pelo
		// `if(!canmove) mobTime = 0` do `movement handler.dm:88`. O servidor ja recusa o passo desde
		// sempre -- o `EnraizadoPorKi` esta no `PodeMexerOCorpo` --, mas o CLIENTE nao sabia: ele
		// continuava integrando as teclas e comendo uma correcao por pacote, que e o corpo TREMENDO no
		// lugar durante o raio inteiro. E o mesmo modo de falha (e a mesma defesa) do bloco de cima.
		//
		// Este e o consumidor do `_canalDeKi`, e por isso ele existe separado do `_raioSaindo`: a pose
		// so muda quando o feixe SAI, mas o corpo ja esta preso desde que a carga comeca.
		// ================================================================================================
		// A LISTA MORA NO `PorQueNaoAnda`, E SO LA. Ela estava escrita aqui como um `||` de sete
		// termos; virou propriedade quando ficou claro que "o corpo nao saiu do lugar" precisa dizer
		// QUAL dos sete foi -- e duas listas iguais em dois lugares e uma promessa de divergirem.
		var input = PorQueNaoAnda.Length > 0
			? Vector2.Zero   // no chao nao se anda: ver OnSheet
			: new Vector2(
				Godot.Input.GetActionStrength("move_right") - Godot.Input.GetActionStrength("move_left"),
				Godot.Input.GetActionStrength("move_down") - Godot.Input.GetActionStrength("move_up"));

		var dir = new Vec2(input.X, input.Y);
		bool tentandoAndar = dir.LengthSquared > 1e-6f;

		// PILOTO AUTOMATICO (o nav system). NAO e teleporte nem velocidade extra: ele so
		// preenche a direcao que o jogador preencheria com as teclas, e o passo continua
		// passando pelo MoveRules e sendo conferido pelo servidor. Uma viagem de sete dias tem
		// que custar sete dias.
		// CAIDO, CARREGANDO OU COM UM RAIO NA MAO, O PILOTO DESLIGA -- e nao "fica guiando um corpo
		// que nao anda".
		//
		// As tres condicoes zeram o INPUT logo acima, mas o piloto roda DEPOIS e reencheria `dir`.
		// O cliente entao andava; o servidor, que recusa movimento de quem esta carregando ou no
		// chao, devolvia uma Correction por PACOTE -- trinta por segundo, o corpo tremendo e o
		// console cuspindo aviso. Segurar C no meio de uma viagem e o caso comum.
		//
		// O RAIO ENTROU AQUI JUNTO COM O `input` LA EM CIMA, e nao depois: as duas linhas dizem a
		// MESMA frase ("este corpo nao anda") e uma sem a outra reproduz exatamente o defeito que
		// este comentario ja conta -- foi assim que a carga de Ki o produziu da primeira vez.
		if ((_caido || _carregando || _canalDeKi) && Destino != null)
		{
			Destino = null;
			Chat.Sistema("piloto automatico desligado.");
		}

		if (!tentandoAndar && Destino is { } alvo)
		{
			Vec2 rumo = alvo - _pos;
			if (rumo.LengthSquared < 64 * 64) { Destino = null; Chat.Sistema("voce chegou."); }
			else { dir = rumo.Normalized(); tentandoAndar = true; }
		}
		else if (tentandoAndar && Destino != null)
		{
			// encostou numa tecla: assume o controle. Piloto que ignora o piloto e um sequestro.
			Destino = null;
			Chat.Sistema("piloto automatico desligado.");
		}

		// SHIFT FAZ DUAS COISAS, e elas NAO sao a mesma:
		//   * andando, ele CORRE (velocidade maior, cobrada em Ki pelo servidor);
		//   * socando, ele e o modificador do golpe PESADO / investida longa.
		// Se as duas dependessem de estar andando, segurar SHIFT parado e apertar espaco
		// daria um soco leve -- e o dono pediu justamente SHIFT+ESPACO como o golpe forte.
		_shift = !_caido && Godot.Input.IsActionPressed("run");

		// O C TEM PRIORIDADE SOBRE O SHIFT.
		//
		// O dono: "ao correr e segurar C ele ainda faz o som de carregar ki -- o certo seria ele
		// dar prioridade ao C e parar de correr e começar a carregar o ki parado". Faz sentido:
		// reunir energia exige pe plantado (o servidor ja nao rende nada em movimento), entao
		// deixar a corrida ligada punha o jogador num estado que nao produz nada e ainda gasta Ki.
		// Segurar C desliga a corrida; soltar devolve.
		// NO AR, O SHIFT E O SUPERFLIGHT -- e o mesmo borrao.
		//
		// O `_temKiPraCorrer` fica de FORA quando se voa, de proposito: voando, quem cobra o Ki e o
		// `TickDoVoo` pela formula do DM, e o servidor concede a velocidade a qualquer um que esteja
		// no ar (ver `GameServer.Input`). Manter o teste de Ki da corrida aqui faria o borrao piscar
		// no cliente enquanto o servidor continuava dando a velocidade -- as duas pontas contando
		// historias diferentes do mesmo gesto.
		bool querCorrer = _shift && tentandoAndar && !_carregando
			&& (_altitude > 0f || _temKiPraCorrer);
		if (querCorrer && !_correndo) AudioDirector.EfeitoNoLugar(this, Trilha.Dash, 0.5f);
		_correndo = querCorrer;

		// ARREMESSADO NAO DIRIGE. Enquanto o servidor esta jogando o corpo, o cliente para de
		// integrar o input e so segue as correcoes -- senao os dois empurram o mesmo corpo em
		// direcoes diferentes, que e a briga que faz o personagem TREMER.
		// ARREMESSADO: o corpo ANDA sozinho na direcao do voo, no cliente, em vez de esperar a
		// correcao de cada tique.
		//
		// A primeira versao so travava o input e deixava o servidor teleportar o corpo a cada 0,1 s.
		// Funcionava e ficava HORRIVEL -- dez saltos de dois tiles em vez de um voo. O dono: "o
		// knock back n ta fluido, o personagem voa meio travado".
		//
		// Agora o cliente INTERPOLA: guarda o rumo que a ultima correcao revelou e desliza nele, e a
		// correcao seguinte so ajusta o erro. O servidor continua sendo o dono da posicao -- ele so
		// parou de ser o unico a mover o corpo.
		if (_empurrado)
		{
			// PERSEGUE A POSICAO DO SERVIDOR em vez de simular por conta propria.
			//
			// A versao anterior integrava o rumo sozinha e a correcao de cada tique batia de frente
			// com ela -- o corpo avancava, era puxado, avancava de novo. Na tela isso e o tranco que
			// o dono viu, e a camera (que e filha do corpo) piscava junto.
			//
			// Aqui nao ha simulacao paralela: o cliente so DESLIZA ate o ultimo ponto que o servidor
			// confirmou. Ele nunca passa dele, entao nao ha o que corrigir de volta.
			Vec2 falta = _alvoDoVoo - _pos;
			float passo = (float)(delta * VelocidadeDoVoo);
			_pos = falta.LengthSquared <= passo * passo ? _alvoDoVoo : _pos + falta.Normalized() * passo;
			Desenhar();
			// A MESMA FOLHA QUE OS OUTROS VEEM -- menos no puxao da fusao.
			//
			// `_dirDoCorpo` e o `DirecaoDeitado` do servidor, e ele responde *"de onde veio a pancada"*
			// (`RumoDoGolpe`). E a pergunta certa pro arremesso e **nao tem resposta nenhuma** num puxao:
			// o valor que estaria la e o do ultimo golpe que este corpo levou, que pode ser de minutos
			// atras. Durante o puxao o corpo fica com a direcao que ele proprio tinha -- de pe, parado,
			// deslizando pro outro --, que e o que o jogador ve como "estou sendo atraido".
			_visual.SetMotion(_puxadoNaFusao ? _facing : _dirDoCorpo, false);
			return;   // QUEM GIRA E O `OnSheet`: o angulo vem do servidor, e so ele.
		}

		Vec2 antes = _pos;
		// VOANDO ALTO, NAO HA MAPA -- e o `isflying` do original, e a MESMA linha que o servidor
		// escreve em `GameServer.Input`. As duas pontas decidem pelo mesmo numero e pela mesma
		// funcao, entao nao existe a faixa de altura em que uma passa e a outra recusa (que e o
		// que viraria correcao de posicao em jogo honesto -- o personagem tremendo na parede).
		// ...E ABAIXO DELA, A AGUA: quem esta baixo consulta o mapa, e o `modo` diz se a agua o para.
		// A funcao e a MESMA do servidor (ver `ModoDoCorpo`).
		// ============================ ...E OS CORPOS, NO MESMO `Advance` ============================
		// *"faca com q personagens N CONSIGAM PASSAR DENTRO DO OUTRO andando"*. A grade e reenchida
		// aqui, por quadro, e entra pelo MESMO parametro que o mapa -- nao ha um segundo teste depois
		// deste passo empurrando o corpo pra fora de alguem.
		//
		// **E ELE E PREVISAO, NAO PEDIDO**: o servidor NAO confere corpo no `ValidateStep` (ver la o
		// porque -- corpo e dado dinamico que as duas pontas veem em instantes diferentes, e cobrar
		// concordancia sobre ele so produziria correcao em jogo honesto). Ou seja: esbarrar em gente e
		// resolvido aqui, e nada disto gera pacote nem tremor.
		// ==========================================================================================
		World.Instancia?.MontarGradeDeCorpos(_corpos);
		var vizinhos = new Jandirus.Core.World.Vizinhanca(
			_corpos, GameClient.Instance?.LocalId ?? 0, Jandirus.Core.World.Voo.Andar(_altitude));

		_pos = MoveRules.Advance(_pos, dir, (float)delta, SpeedStat,
			AtravessandoCenario ? null : Mapa, out _, _correndo, ModoDoCorpo, vizinhos);
		Desenhar();

		// ANDANDO = saiu do lugar, nao = apertou a tecla. Empurrando a parede o personagem
		// fica parado de pe em vez de marchar sem sair do lugar.
		bool andando = (_pos - antes).LengthSquared > 0.01f;

		// ============================ A CINEMATICA MANDA NA POSE, E MANDA TODO QUADRO ============================
		// Durante a cena de estreia o corpo fica PARADO e virado pra frente -- o `move = 0;
		// dir = SOUTH` do DM.
		//
		// A guarda precisa estar AQUI, e nao numa chamada unica no comeco da cena. Esta linha roda
		// a cada quadro e reescreveria qualquer pose que a cinematica tivesse posto -- e o
		// resultado era o personagem "andando" durante a transformacao inteira. E o MESMO tombo que
		// o comentario do sprite de voo, umas linhas abaixo, ja documenta ter acontecido antes: o
		// corpo local decide a propria pose por quadro, entao quem quiser manda-la tem que entrar
		// no quadro.
		// ================================================================================================
		// O RELOGIO DA SAIDA DE EMERGENCIA. Ver `SegundosAteDestravar`.
		if (Transformacao.PrendendoOCorpo)
		{
			_presoHa += delta;
			if (_presoHa > SegundosAteDestravar)
			{
				Transformacao.DestravarTudo($"o corpo local ficou {_presoHa:0}s sem aceitar comando");
				_presoHa = 0;
				_visual.TravarPose(false);   // a pose tambem: as duas trancas andam juntas
			}
		}
		else _presoHa = 0;

		if (Transformacao.PrendendoOCorpo)
		{
			_facing = Facing.South;
			_visual.SetMotion(Facing.South, moving: false);
		}
		else
		{
			if (tentandoAndar) _facing = MoveRules.FacingFrom(dir, _facing);
			_visual.SetMotion(_facing, andando);
		}

		// O BORRAO SEGUE A INTENCAO (`_correndo`), e nao o deslocamento quadro a quadro.
		//
		// ============================ POR QUE MUDOU ============================
		// A versao anterior usava `_correndo && andando`, com o argumento de que rastro saindo de
		// quem nao saiu do lugar nao faz sentido. O argumento e bom e o efeito colateral era pior:
		// `andando` e `(_pos - antes) > 0,01 px`, que PISCA -- um quadro curto, uma quina de
		// parede, um passo diagonal raspando. Cada piscada zerava o alvo do borrao e a subida/
		// descida (0,10 s / 0,22 s) transformava isso num pulso visivel. Foi o que o dono viu:
		// "ele da umas piscadinhas".
		//
		// `_correndo` ja exige tecla de direcao E Ki (`_shift && tentandoAndar && _temKiPraCorrer`),
		// entao empurrar parede segurando SHIFT ainda mostra rastro -- mas isso e um estado raro e
		// visivelmente travado, enquanto a piscada acontecia correndo em linha reta.
		// =======================================================================
		Vec2 desl = _pos - antes;
		_visual.Correr(_correndo, new Vector2(desl.X, desl.Y));

		// O RASTRO segue o deslocamento REAL: parado empurrando parede nao deixa rastro, porque
		// rastro e do corpo que passou por um lugar.
		if (GetNodeOrNull<RastroDeCorrida>("Rastro") is { } rastro) rastro.Definir(_correndo && andando);

		LerAcoes(tentandoAndar, delta);

		// SUBIR E DESCER. Sao PEDIDOS continuos, como correr: o servidor so obedece a quem esta
		// voando (`TickDoVoo`), entao apertar no chao nao faz nada. Escrevendo no chat ninguem sobe.
		//
		// ============================ DAQUI PRA BAIXO E TUDO SONDA, E SONDA NAO SE CALA SOZINHA ============================
		// Estas leituras nao sao eventos: sao perguntas ao `InputMap` dentro do laco de fisica. O
		// `SetInputAsHandled` do quick time event NAO as alcanca -- o unico lugar onde da pra barrar
		// uma sonda e o ponto de LEITURA. Por isso todas elas trocaram `Foco.Digitando` por
		// `Foco.AtalhosMudos`: o R (subir), o G (descer), o F (voar), o N (nadar), o Q (agarrar), o C
		// (carga), o X (reverter), o T (treinar), o M (meditar) e o K (letal) **sao dez das 22 letras
		// que o embate sorteia**, e sem isto responder a letra pedida ligava o voo, mandava meditar ou
		// carregava Ki no meio do embate. Era a queixa do dono, medida.
		//
		// O QUE **NAO** ENTROU, e por que: o vetor de ANDAR (ele ja tem a sua propria trava, logo acima,
		// e mexer nele foi o erro que o dono cobrou uma vez), a GUARDA (soltar o ALT ENCERRA o lado de
		// quem segura um feixe com as maos -- calar a leitura seria PERDER a disputa de ki pelo portao)
		// e o SOCO (o espaco nao e letra, e o corpo ja esta preso pelo `Stun`).
		// ==================================================================================================================
		bool subir = !Foco.AtalhosMudos && Godot.Input.IsActionPressed("subir");
		bool descer = !Foco.AtalhosMudos && Godot.Input.IsActionPressed("descer");

		// V ALTERNA O VOO. Vai pelo canal de habilidade -- e o MESMO caminho do verb do menu, pra
		// que a tecla e o botao nao virem duas regras que precisam concordar.
		if (!Foco.AtalhosMudos && Godot.Input.IsActionJustPressed("voar"))
			GameClient.Instance?.SendHabilidade("voar");

		// N ALTERNA O NADO, pelo MESMO canal do voo e do botao do menu. Quem decide se liga (tem agua
		// aqui? esta de pe?) e o servidor -- o cliente so pede, e recebe a resposta na ficha.
		if (!Foco.AtalhosMudos && Godot.Input.IsActionJustPressed("nadar"))
			GameClient.Instance?.SendHabilidade("nadar");

		// Q CICLA O AGARRAO -- pegar, levantar no colo, soltar. Pelo MESMO canal do voo e do nado, e
		// pela mesma razao: quem decide se ha alguem ao alcance, se o corpo pode agarrar e em que
		// estado o aperto esta e o SERVIDOR. O cliente so pede.
		//
		// **NAO HA TECLA DE ARREMESSAR**: segurando alguem, o primeiro passo o joga (`Throw.dm:1-3`),
		// e quem ve isso e o `TickDoAgarrao` do servidor lendo o `Moving` que este mesmo laco ja
		// manda. Uma tecla a mais aqui seria um gesto que o original nao tem.
		if (!Foco.AtalhosMudos && Godot.Input.IsActionJustPressed("agarrar"))
			GameClient.Instance?.SendHabilidade("agarrar");

		SeguirAltura(delta);
		AplicarAltura();

		_sendAccumulator += delta;
		if (_sendAccumulator >= SendInterval)
		{
			_sendAccumulator -= SendInterval;
			GameClient.Instance?.SendState(_pos, _facing, andando, _correndo, subir, descer);   // o servidor recebe o EXATO
		}
	}

	/// <summary>
	/// PERSEGUE a altura do servidor, por quadro. Ver <see cref="_altitudeNaTela"/>.
	///
	/// A sonda conta so os quadros em que ela DEVERIA estar andando (ainda ha distancia a cobrir) --
	/// contar os quadros parados no teto acusaria como defeito exatamente o comportamento certo.
	/// </summary>
	private void SeguirAltura(double delta)
	{
		float falta = _altitude - _altitudeNaTela;
		bool deveriaAndar = MathF.Abs(falta) > 0.5f;

		if (deveriaAndar)
		{
			QuadrosDeAltura++;
			float antes = _altitudeNaTela;
			_altitudeNaTela += falta * Mathf.Min(1f, (float)delta * PerseguicaoDaAltura);
			if (Mathf.IsEqualApprox(antes, _altitudeNaTela)) QuadrosParadosDeAltura++;
		}
		else
		{
			// COLADO: crava. Perseguicao exponencial nunca CHEGA, e um rabo de meio pixel no pouso
			// deixaria a sombra e a nevoa vivas depois de o corpo ja estar no chao.
			_altitudeNaTela = _altitude;
		}
	}

	/// <summary>
	/// LEVANTA O DESENHO DO CORPO, e so o desenho -- o node fica onde esta (ver
	/// <see cref="SubirComOVoo.Aplicar"/> pra o porque).
	///
	/// ============================ AQUI HAVIA UMA LISTA DE NOMES, E ELA ERA O DEFEITO ============================
	/// Este metodo enumerava a mao quem sobe: `"Aura"`, `"Vida"`, `"Balao"`, `"Carga"`... e esqueceu
	/// alguem QUATRO vezes. O proprio comentario que estava aqui registrava tres delas antes de a
	/// quarta (a nebulosa do Ultra Instinto) ser encontrada por um robo. Uma lista escrita a mao nao
	/// tem como saber que nasceu um node novo em `World.AoEntrar`: o unico aviso e o defeito na tela.
	///
	/// A pergunta foi invertida -- **todo filho visual sobe, menos quem declarar que nao** -- e a
	/// resposta mudou de lugar: ela mora agora na declaracao de cada node (`IFicaNoChao`), que e onde
	/// quem escreve o node esta olhando. O node novo entra na conta sozinho, sem tocar neste arquivo.
	///
	/// A CAMERA SUMIU DAQUI E CONTINUA SUBINDO. Ela tinha uma linha propria (achada por tipo, porque
	/// nasce sem nome); agora e so mais um filho `Node2D` e cai no caso normal da varredura.
	/// ========================================================================================================
	/// </summary>
	private void AplicarAltura()
	{
		if (Mathf.IsEqualApprox(_altitudeNaTela, _altitudeDesenhada)) return;
		_altitudeDesenhada = _altitudeNaTela;

		SubirComOVoo.Aplicar(this, new Vector2(0, -_altitudeNaTela * Voo.EscalaNaTela));

		// A SOMBRA E O CONTRARIO DE TODO O RESTO, e por isso ela tem canal proprio: o vao entre ela e o
		// corpo E a altura na tela. Ela declara `IFicaNoChao`, entao a varredura acima passa por ela.
		if (_sombra != null) _sombra.Altura = _altitudeNaTela;
	}

	/// <summary>O no fica sempre num ponto da grade de PIXEL DE TELA -- ver o cabecalho da classe.</summary>
	private void Desenhar() => Position = NoPontoDaGrade(new Vector2(_pos.X, _pos.Y), World.GradeDeDesenho);

	/// <summary>
	/// O PONTO DA GRADE mais proximo (por baixo) de uma posicao de mundo.
	///
	/// Publica e estatica por dois motivos. O primeiro e que ela nao e so do corpo local: o
	/// <see cref="RemotePlayer"/> desenha na MESMA grade, e dois bonecos lado a lado que
	/// arredondassem de jeitos diferentes andariam com passos diferentes. O segundo e a bancada
	/// `--diagrolagem`, que precisa medir ESTA conta -- uma bancada que reescreve a formula que
	/// julga nao julga nada: ela ficaria verde com o jogo errado desde que os dois erros combinassem.
	///
	/// `pontos` e quantos pontos da grade cabem num pixel de mundo, e quem responde e
	/// <see cref="World.GradeDeDesenho"/>. Com `pontos = 1` a conta e identica ao `Floor` que estava
	/// aqui antes.
	/// </summary>
	public static Vector2 NoPontoDaGrade(Vector2 exata, float pontos)
	{
		if (!(pontos > 1f)) return new Vector2(MathF.Floor(exata.X), MathF.Floor(exata.Y));
		return new Vector2(MathF.Floor(exata.X * pontos) / pontos,
						   MathF.Floor(exata.Y * pontos) / pontos);
	}

	/// <summary>
	/// PÕE O CORPO NA POSE DE GOLPE por um tempo, sem que o golpe tenha vindo do teclado.
	///
	/// ============================ POR QUE ISTO PRECISA EXISTIR ============================
	/// O corpo LOCAL não recebe pose por snapshot -- ele mesmo decide a sua, e `LerAcoes` reescreve
	/// o estado a cada quadro ("default", "train", "meditate"). A única coisa que segura a pose de
	/// soco é o relógio `_ataqueAte`, e ele só é armado no ramo do teclado.
	///
	/// Quem chamava `RestartState("attack")` de fora -- o vislumbre do Zanzo Clash -- via a pose
	/// durar exatamente UM quadro: no seguinte, `LerAcoes` passava por cima. E como o corpo REMOTO
	/// recebe a pose pelo snapshot, ela ficava lá; era essa a assimetria de "o outro aparece
	/// socando e eu não".
	///
	/// Com isto o relógio e a pose andam juntos, e há um dono só pra os dois.
	/// ======================================================================================
	/// </summary>
	public void PosarDeGolpe(double segundos, Facing? olhar = null)
	{
		if (segundos <= 0) return;

		// A DIRECAO, QUANDO QUEM MANDA E O SERVIDOR. No embate e ele quem crava quem encara quem
		// (ver `GameServer.Cruzar`), e o teclado do jogador nao opina -- sem isto o meu boneco
		// socava pro lado que eu estava segurando enquanto o outro socava pro lado certo.
		if (olhar is { } d)
		{
			_facing = d;
			_visual.SetMotion(d, false);
		}

		_ataqueAte = segundos;
		_visual.RestartState("attack", segundos);
	}

	/// <summary>
	/// T treina, M medita, ESPACO soca. O cliente so DECLARA -- quem soma BP (e mais tarde
	/// quem calcula o dano) e o servidor. A animacao roda na hora pra nao ter atraso no
	/// controle; o servidor confirma pros OUTROS verem.
	/// </summary>
	private void LerAcoes(bool andando, double delta)
	{
		if (_ataqueAte > 0) _ataqueAte -= delta;

		if (_caido)
		{
			// caido: a guarda cai junto, e a pose vem do servidor como qualquer outra
			if (_guarda) { _guarda = false; GameClient.Instance?.SendGuard(false); }
			// O SPRITE DE NOCAUTE SO PRA QUEM ESTA MESMO NO CHAO -- ver `_deitado`.
			//
			// ============================ E O DE PE PRECISA SER ESCRITO, NAO BASTA NAO ESCREVER ============================
			// `SetMotion` (que roda todo quadro, la em cima) so mexe em DIRECAO e "esta andando"; quem
			// escolhe o ESTADO e o `SetState`, e ele e pegajoso -- uma vez em `ko`, fica em `ko`. Um
			// morto que caiu na Terra e depois subiu pro Outro Mundo passaria a estadia inteira com o
			// sprite de nocaute na PROPRIA tela (e girado 90 graus, e com a auréola desenhada AO LADO
			// da cabeca, que e o que a folha desenha pro estado `ko`) enquanto todas as outras telas o
			// mostravam de pe. Ver `_deitado`.
			//
			// `PorAPoseDoCorpo` e o MESMO chooser do corpo acordado: ele cobre o voo e o nado, que um
			// `SetState("default")` cravado aqui perderia -- e no alem se anda voando.
			// ========================================================================================================
			if (_deitado) _visual.SetState("ko");
			else PorAPoseDoCorpo();
			return;
		}

		// GUARDA. Segurar ALT ergue o braco; erguer a guarda no instante do golpe vira
		// contra-ataque, e por isso ela e um estado continuo e nao um toque.
		bool guardaAgora = Godot.Input.IsActionPressed("guard") && !andando && !Foco.Digitando;
		if (guardaAgora != _guarda)
		{
			_guarda = guardaAgora;
			GameClient.Instance?.SendGuard(_guarda);
		}

		if (!Foco.AtalhosMudos) LerMira();

		if (!Foco.AtalhosMudos) LerTeclaC(delta);
		else if (_carregando) { _carregando = false; GameClient.Instance?.SendCarregar(false); }

		if (!Foco.AtalhosMudos && Godot.Input.IsActionJustPressed("reverter"))
			GameClient.Instance?.SendTransformar(false);

		// UM soco por vez. Sem esta trava, martelar o espaco re-armava o cronometro a cada
		// tecla e o personagem ficava preso na pose de soco pra sempre -- e como todo estado
		// do .dmi tem loop, o ciclo se repetia sem nunca voltar a ficar de pe.
		if (!Foco.Digitando && Godot.Input.IsActionJustPressed("attack") && _ataqueAte <= 0)
		{
			// SHIFT + ESPACO = GOLPE PESADO, e com ele a investida longa. So ESPACO = golpe
			// leve, com um passo curto pra fechar o meio metro que falta. Nao ha tecla
			// separada de "soco forte": no original o golpe so ficava pesado quando saia em
			// dash (`1 + dash_delay` virava o `Type`), e SHIFT ja e essa escolha.
			// VIRA PRO ALVO MARCADO, na tela, ANTES de socar.
			//
			// O servidor ja girava o `Facing` pelo marcado (`GameServer.Atacar`) -- mas a direcao
			// que ele calcula so viaja pros OUTROS, no snapshot. O meu proprio sprite e desenhado
			// por mim, com a direcao que saiu do MEU movimento. Sem esta linha, marcar alguem que
			// esta atras e apertar espaco desenhava o soco pro lado errado enquanto o golpe, no
			// servidor, saia certo -- as duas pontas contando historias diferentes do mesmo golpe.
			if (World.Instancia?.PosicaoDoAlvo is { } alvo)
			{
				var pAlvo = new Vec2(alvo.X - _pos.X, alvo.Y - _pos.Y);
				_facing = MoveRules.FacingFrom(pAlvo, _facing);
				_visual.SetMotion(_facing, false);
			}

			Protocol.Golpe golpe = _shift ? Protocol.Golpe.Pesado : Protocol.Golpe.Leve;
			double dura = _cadencia * Protocol.PesoDoGolpe(golpe);

			// DE ONDE EU SAIRIA, guardado pro caso de ter havido investida.
			//
			// O VULTO NAO NASCE AQUI. Nascia -- e o dono viu o defeito: apertar SHIFT+ESPACO sem
			// ninguem por perto deixava miragem parado no lugar. O cliente nao tem como saber se a
			// investida ACONTECEU: quem escolhe o alvo, cobra o Ki e testa a parede no caminho e o
			// servidor. Ele responde isso no relato do golpe (`HitEvent.Zanzo`), e o vulto sai la --
			// neste ponto, que e onde o corpo estava quando a tecla foi apertada.
			//
			// ============================ VALE PROS DOIS GOLPES ============================
			// Isto so rodava no golpe PESADO, e estava errado: o servidor chama `Aproximar` nos DOIS
			// (`longo: golpe == Pesado` -- GameServer.Combat.cs), com 160px no pesado e 80px no leve.
			// Ou seja, o soco LEVE tambem investe, tambem marca `Zanzo`, e tambem faz o cliente
			// chamar `DeixarVulto()` -- so que com a posicao guardada no ULTIMO shift+espaco.
			//
			// O dono descreveu exatamente isso: "ao apertar espaco dentro do range do tp sem o shift,
			// o efeito do zanzoken acontece no ultimo local q usei o shift+espaco, ele n ta
			// atualizando com a posiçao atual do player". A miragem podia nascer do outro lado do
			// mapa, no lugar onde ele tinha investido minutos antes.
			// ==============================================================================
			_deOndeSai = new Vector2(_pos.X, _pos.Y);

			_ataqueAte = dura;
			// a animacao e ESTICADA pra caber no golpe: com a cadencia nova (~0,33 s) o ciclo
			// que veio do .dmi (~0,8 s) nao terminaria antes do proximo soco
			_visual.RestartState("attack", dura);
			GameClient.Instance?.SendAction(golpe);
		}

		LerAtividade(andando);

		// a pose de soco tem prioridade enquanto dura
		if (_ataqueAte > 0) return;

		PorAPoseDoCorpo();
	}

	/// <summary>
	/// A POSE DO CORPO ACORDADO -- voo/nado, treino, meditacao ou parado.
	///
	/// ============================ POR QUE ELA VIROU FUNCAO ============================
	/// Ela era o fim do <see cref="LerAcoes"/>, e o `if (_caido)` de la em cima RETORNA antes de
	/// chegar aqui. Isso estava certo enquanto "caido" e "morto" eram a mesma coisa; hoje um morto
	/// pode estar **de pe** no Outro Mundo (ver <see cref="_deitado"/>), e ele precisa da mesma pose
	/// de sempre -- inclusive a de VOO, que e como se anda por la.
	///
	/// Sem esta chamada o `SetState` fica pegajoso: quem caiu na Terra em `ko` e depois subiu passa a
	/// estadia inteira com o sprite de nocaute na propria tela. Copiar as tres linhas pra dentro do
	/// `if` teria dado a segunda casa da mesma escolha -- e a primeira a envelhecer seria a de la.
	/// ==============================================================================
	/// </summary>
	private void PorAPoseDoCorpo()
	{
		// ============================ O SPRITE DE VOO EXISTE, E NAO ERA USADO ============================
		// A folha de cada personagem tem um estado `flight` desenhado -- o mesmo que o `icon_state =
		// "Flight"` do original (`flying.dm:114`). O `CharacterVisual.SetPose(Voando)` ja mapeava
		// pra ele e o servidor ja mandava a pose... mas so pros OUTROS.
		//
		// O corpo LOCAL nao recebe pose por snapshot: ele decide a sua aqui, e esta linha reescrevia
		// o estado pra "default" TODO QUADRO. Resultado: quem voava se via andando no ar enquanto
		// todo mundo o via em pose de voo. E o mesmo tipo de assimetria que ja tinha aparecido no
		// soco do Zanzo Clash.
		//
		// A altura e o criterio, e nao um `_voando` proprio: ela vem do servidor e ja e a fonte de
		// tudo o mais (colisao, nevoa, sombra). Enquanto o corpo estiver acima do chao -- inclusive
		// CAINDO por exaustao --, a pose e a de voo.
		// ================================================================================================
		// ============================ NADAR ENTRA NA MESMA LINHA, E DE PROPOSITO ============================
		// A pose e a MESMA folha (`flight`), porque o original faz literalmente isso
		// (`icon_state = "Flight"`, `Swim.dm:17`). O que o nado NAO traz junto e o resto do voo: a
		// altura continua zero, entao o corpo nao sobe na tela (o `AplicarAltura` nao mexe nada), a
		// sombra nao nasce (o node so e criado quando a altura sai de zero) e a nevoa de altitude
		// tambem nao. "Entrar na animacao do fly sem sair do chao" e, aqui, exatamente esta linha.
		// ================================================================================================
		// ============================ O RAIO NA MAO VEM ANTES DO VOO, E ISSO E O DM ============================
		// `usr.icon_state = "Blast"` (`beams.dm:280`) e escrito DEPOIS do `icon_state = "Flight"` de
		// quem ja estava voando (`flying.dm:114`), e nada reescreve o Flight por tique -- so o NADO se
		// reafirma assim (`Stats.dm:399`). Entao no original o raio ganha do voo, e o corpo continua no
		// ar (a altura e outro campo) com a pose do feixe.
		//
		// A ORDEM E A MESMA DO SERVIDOR, e tem que ser: quem decide a pose dos OUTROS e o
		// `ServerPlayer.Pose`, que poe o canal acima do `Voando` pelo mesmo argumento. Duas ordens
		// diferentes dariam o defeito classico deste arquivo -- o dono se vendo pairar enquanto todo
		// mundo o ve atirando.
		//
		// E SO A FASE DE TIRO MUDA A POSE: carregar nao escreve `icon_state` nenhum no original (o
		// bloco de carga do verb so poe `forceicon`, som e o overlay -- `beams.dm:288-300`), entao quem
		// esta reunindo energia cai na escada normal e fica no idle, com o brilho da
		// `CargaDeRaioVisual` por cima. Ver `Protocol.Pose.Canalizando`.
		// ===================================================================================================
		if (_raioSaindo) { _visual.SetPose(Protocol.Pose.Canalizando, canalAtirando: true); return; }

		if (_altitude > 0f || _nadando) { _visual.SetState("flight"); return; }

		// NAO existe pose de guarda nos .dmi (o corpo tem meditate, train, attack, flight, ko e
		// mais nada) -- entao guardar mostra a pose parada, e quem avisa que a guarda esta
		// erguida e o HUD. Inventar arte aqui so daria um personagem em pose errada.
		_visual.SetState(_atividade switch
		{
			Protocol.Activity.Treinando => "train",
			Protocol.Activity.Meditando => "meditate",
			_ => "default",
		});
	}

	/// <summary>
	/// T TREINA, M MEDITA.
	///
	/// ============================ POR QUE ISTO SAIU DO `LerAcoes` ============================
	/// Porque MEDITAR E A SAIDA DA FERA. O `TickDoOozaru` derruba a forma pela raiva de quem esta
	/// meditando -- e a unica resposta de quem perdeu o controle sem ter pericia --, e o servidor
	/// deixa o pacote de atividade passar de proposito. Se a unica porta desse teclado ficasse
	/// dentro do `LerAcoes` (que a possessao pula inteiro), a saida existiria no servidor e seria
	/// inalcancavel pelo jogador: uma regra escrita e desligada, que e a falha assinatura deste port.
	///
	/// <paramref name="soASaida"/> corta o TREINO e deixa so a meditacao: treinar com uma fera
	/// dirigindo o seu corpo nao e um estado que o jogo deva aceitar, e o servidor renderia BP por
	/// ele.
	/// ====================================================================================
	/// </summary>
	private void LerAtividade(bool andando, bool soASaida = false)
	{
		Protocol.Activity nova = _atividade;
		// "treinar" e "meditar" sao T e M -- no meio de uma frase, nao; e no meio de um embate tambem
		// nao, que as duas estao entre as 22 letras sorteadas. Ver `Foco.AtalhosMudos`.
		if (Foco.AtalhosMudos) { }
		else if (!soASaida && Godot.Input.IsActionJustPressed("train"))
			nova = _atividade == Protocol.Activity.Treinando ? Protocol.Activity.Parado : Protocol.Activity.Treinando;
		else if (Godot.Input.IsActionJustPressed("meditate"))
		{
			// ============================ O M PERGUNTA ANTES DE MEDITAR ============================
			// *"faca q ao MEDITAR uma TELINHA vai abrir e perguntar se vc quer so MEDITAR NORMAL ou ir
			// pra MEDITACAO PROFUNDA"*. A pergunta e a <see cref="TelaDeMeditacao"/>, e quem RESPONDE
			// por ela e o `EscolheuMeditar` aqui embaixo -- a atividade e deste arquivo (ela e a pose
			// do proprio corpo), e uma telinha que mandasse o pacote sozinha deixaria o `_atividade`
			// mentindo: o M seguinte reabriria a pergunta em vez de parar a meditacao.
			//
			// SAIR NAO E ESCOLHA: quem ja esta meditando aperta M e para, como sempre. A pergunta e da
			// ENTRADA, e um menu pra levantar seria um clique a mais em cima de um gesto que ja estava
			// certo.
			//
			// ANDANDO NAO ABRE: o `if (andando)` logo abaixo derrubaria o pedido de qualquer jeito, e
			// uma telinha que aparece pra ser ignorada e pior do que nao aparecer.
			//
			// POSSUIDO NAO ABRE, e esta e a linha que precisa de cuidado: com a fera no comando o M e a
			// UNICA saida (`soASaida`), e a profunda nem existe la (o canal de habilidade recusa quem
			// esta sem as redeas). Uma pergunta no meio da posse poria uma tela entre o jogador e a
			// unica resposta que a paralisia tem.
			//
			// SEM A TELINHA MONTADA (mundo ainda subindo, bancada sem interface), o M volta a meditar
			// direto. Uma tecla que nao responde porque um node nao nasceu seria a pior das falhas.
			// ==================================================================================
			if (_atividade == Protocol.Activity.Meditando) nova = Protocol.Activity.Parado;
			else if (!andando && !soASaida && TelaDeMeditacao.Instancia is { } pergunta)
			{
				// O M FECHA O QUE O M ABRIU -- a mesma toc-toc do menu da tecla E.
				if (pergunta.NaTela) pergunta.Fechar();
				else pergunta.Abrir(EscolheuMeditar);
				return;
			}
			else nova = Protocol.Activity.Meditando;
		}

		if (andando) nova = Protocol.Activity.Parado;   // nao se treina correndo

		if (nova != _atividade)
		{
			_atividade = nova;
			GameClient.Instance?.SendActivity(nova);
		}
	}

	/// <summary>
	/// A RESPOSTA DA TELINHA (ver <see cref="TelaDeMeditacao"/>): `true` = profunda, `false` = normal.
	///
	/// **METODO NOMEADO, e nao lambda**, pelo motivo de sempre neste projeto: o que se entrega pra
	/// outro node tem que ter nome pra poder ser lido (e, no dia em que virar assinatura, cancelado).
	///
	/// ============================ OS DOIS PACOTES, NESTA ORDEM ============================
	/// A profunda manda DOIS: a atividade (`C2S.Activity`, que escreve o `Ficha.med` do servidor) e a
	/// habilidade (`C2S.Habilidade`, que e a porta da mente). A ordem importa, porque o servidor
	/// recusa quem nao esta meditando -- e ela esta GARANTIDA: os dois saem no mesmo canal confiavel
	/// e ordenado (`Protocol.ChannelReliable`, `ReliableOrdered`), entao o `case "mente"` roda depois
	/// do `case Activity` do mesmo jogador, sempre.
	///
	/// O `SendHabilidade` e o MESMO que o verb apagado do menu P mandava: a telinha trocou a porta,
	/// nao o encanamento. Nada mudou no servidor por causa dela.
	/// ================================================================================
	/// </summary>
	private void EscolheuMeditar(bool profunda)
	{
		// PELO CAMINHO DE SEMPRE: escrever o `_atividade` aqui e o que faz a pose de meditar aparecer
		// no proprio corpo (`PorAPoseDoCorpo`) e o M seguinte PARAR a meditacao em vez de reperguntar.
		_atividade = Protocol.Activity.Meditando;
		GameClient.Instance?.SendActivity(Protocol.Activity.Meditando);
		if (profunda) GameClient.Instance?.SendHabilidade("mente");
	}

	/// <summary>
	/// O QUE ESTE CORPO ESTA FAZENDO (parado, treinando, meditando). SO PRA BANCADA (`--diagforma`).
	///
	/// Existe por causa de UMA linha: o `LerAtividade(soASaida: true)` la em cima, dentro do `return`
	/// da posse. Ela e a saida da fera do lado do cliente, e o jeito dela sumir e o pior possivel --
	/// ninguem ve falta de uma tecla que so importa enquanto o corpo nao responde a nenhuma outra.
	/// Sem esta propriedade a bancada teria que perguntar ao `GameClient` se um pacote saiu, e o
	/// `SendActivity` some no fio igual ao `Avisar` do servidor.
	/// </summary>
	public Protocol.Activity AtividadeDeTeste => _atividade;

	// =====================================================================
	// A TECLA C
	// =====================================================================
	/// <summary>Segurando C agora (o `is_drawing` do original).</summary>
	private bool _carregando;

	/// <summary>Quanto falta do prazo do toque duplo. 0 = nao ha toque pendente.</summary>
	private double _duploAte;

	/// <summary>
	/// A JANELA DO TOQUE DUPLO, em segundos. E o `spawn(10) dblclk=0` do DM -- dez decimos.
	///
	/// Um segundo parece largo pra duplo clique de mouse, mas isto NAO e duplo clique: e uma tecla
	/// que tambem tem funcao de segurar, e quem toca duas vezes esta alternando entre dois gestos
	/// com o mesmo dedo. Janela curta faria o jogador falhar a transformacao e sair carregando.
	/// </summary>
	private const double JanelaDoDuplo = 1.0;

	/// <summary>
	/// C FAZ TRES COISAS, e era isso que faltava: o dono avisou que "o C não é só pra transformar".
	///
	///   segurar          reune energia (o servidor decide o quanto, ver `CargaDeKi`)
	///   tocar duas vezes tenta subir a escada de transformacao
	///   soltar           para
	///
	/// A ORDEM IMPORTA: o toque e detectado ANTES de a carga comecar, e a carga comeca no mesmo
	/// quadro. Isso deixa o gesto natural -- tocar duas vezes carrega um tiquinho no meio, que e
	/// exatamente o que acontecia no original (`Draw_Energy` chama `Energy_Draw` E conta o clique
	/// na mesma passada).
	/// </summary>
	private void LerTeclaC(double delta)
	{
		if (_duploAte > 0) _duploAte -= delta;

		if (Godot.Input.IsActionJustPressed("transformar"))
		{
			if (_duploAte > 0) { _duploAte = 0; GameClient.Instance?.SendTransformar(true); }
			else _duploAte = JanelaDoDuplo;
		}

		// SEGURAR. Caido nao carrega -- o servidor recusa de qualquer jeito, e mandar mesmo assim
		// seria um pacote por quadro pra ouvir nao.
		bool quer = !_caido && Godot.Input.IsActionPressed("transformar");
		if (quer == _carregando) return;

		_carregando = quer;
		GameClient.Instance?.SendCarregar(quer);

		// E SO ISSO. A tecla PEDE; quem decide e o servidor, e quem acende e o `World` ao receber
		// o efeito de volta (ver World.AoCairEfeito).
		//
		// A VERSAO ANTERIOR ACENDIA AQUI MESMO, com o comentario "o meu corpo nao entra no
		// snapshot, entao tem que ser ligado aqui". O raciocinio estava certo sobre o snapshot e
		// errado sobre a conclusao: o corpo local realmente nao vem no snapshot, mas a resposta
		// nao era adivinhar -- era usar o canal de efeito, que o servidor ja mandava e ninguem
		// escutava. Sem isso, apertar C sem Ki Unlocked acendia aura pra um poder que o servidor
		// tinha recusado, e o jogador ficava com luz, som e Ki parado.
	}

	/// <summary>O typepath da skill que libera a imagem remanescente (`misc.dm:35`).</summary>
	private const string PathDoZanzoken = "/datum/skill/ki/Afterimage";

	/// <summary>
	/// Sei fazer o Zanzoken?
	///
	/// O CLIENTE PODE DECIDIR ISTO SOZINHO porque a resposta nao muda nada no mundo -- ela decide
	/// se um sprite semitransparente aparece por meio segundo. A lista de skills ja chega aqui
	/// (`S2C.Skills`), entao perguntar ao servidor seria uma ida e volta de rede pra desenhar.
	///
	/// Se algum dia o vulto virar mecanica (confundir a mira do adversario, como o
	/// `Zanzoken_Afterimage` de nivel 4 faz no original), a decisao muda de lado na hora: efeito
	/// que altera o resultado da luta e do servidor, sem excecao.
	/// </summary>
	private static bool TemZanzoken() =>
		GameClient.Instance?.SkillsAprendidas.Contains(PathDoZanzoken) == true;

	/// <summary>
	/// Onde o corpo estava quando o golpe saiu. O relato do servidor chega um RTT depois, e ate la
	/// o personagem JA investiu -- desenhar o vulto na posicao do momento o poria em cima do alvo,
	/// que e o oposto de "ficou pra tras".
	/// </summary>
	private Vector2 _deOndeSai;

	/// <summary>
	/// GRAVA DE ONDE EU SAIO, pra um gesto que ainda vai ser confirmado pelo servidor.
	///
	/// O soco ja fazia isso sozinho; a PISCADA por duplo clique precisava disto e nao tinha. Ver
	/// o comentario do <see cref="DeixarVulto"/>.
	/// </summary>
	public void MarcarSaida() => _deOndeSai = new Vector2(_pos.X, _pos.Y);

	/// <summary>
	/// O servidor confirmou o deslocamento: e aqui que o vulto nasce, na posicao que o CLIENTE
	/// guardou no instante do gesto.
	///
	/// ============================ POR QUE NAO USAR A POSICAO DO SERVIDOR ============================
	/// O pacote da piscada (`S2C.Zanzo`) traz a posicao de onde o corpo saiu, e a piscada usava ELA.
	/// Duas coisas dao errado nisso, e nenhuma acontece no caminho do soco:
	///
	///   1. E UMA POSICAO ATRASADA. `pl.Pos` no servidor e a ultima que CHEGOU por pacote de input.
	///      Entre o cliente mandar o duplo clique e o servidor atender, o corpo ja andou -- entao a
	///      miragem nasce alguns pixels atras de onde o jogador realmente estava.
	///
	///   2. OS DOIS PACOTES VEM POR CANAIS DIFERENTES. A `Correction` (que move o corpo) vai no canal
	///      CONFIAVEL e o `Zanzo` (que cria a miragem) no NAO CONFIAVEL -- e o LiteNetLib nao garante
	///      ordem ENTRE canais. Ou seja, a ordem em que o corpo salta e a miragem aparece muda de
	///      pacote pra pacote, e as vezes a miragem nasce em cima do corpo que ainda nao saiu.
	///
	/// O caminho do soco nunca teve nenhum dos dois problemas porque ele NAO PERGUNTA a posicao ao
	/// servidor: o cliente guarda onde estava no instante da tecla e desenha ali. O dono notou
	/// exatamente essa assimetria -- "o tp via dash nao causa o bug visual" --, e a correcao e fazer
	/// a piscada usar o mesmo caminho, nao inventar um terceiro.
	/// ================================================================================================
	/// </summary>
	public void DeixarVulto(Vector2 doServidor)
	{
		if (GetParent() is { } palco) Zanzoken.Deixar(palco, this, OrigemDoSalto(doServidor));
	}

	/// <summary>
	/// O BORRAO DO SALTO, no meu proprio corpo -- pela MESMA origem que o vulto usa.
	///
	/// Ele nao e a outra metade do <see cref="DeixarVulto"/>: o vulto e a Afterimage (skill), o borrao
	/// e o deslocamento (qualquer corpo). Estao lado a lado porque compartilham a origem.
	///
	/// **NAO HA NADA AQUI QUE SEJA "DO JOGADOR"**: o `RastroDeCorrida.Arranque` e o mesmo node e o mesmo
	/// metodo que o corpo remoto e o do NPC chamam. Este atalho existe so pra escolher a ORIGEM, que e a
	/// unica coisa que o corpo local sabe melhor que o pacote -- e nem sempre (ver `OrigemDoSalto`).
	/// </summary>
	public void BorrarArranque(Vector2 doServidor)
	{
		if (GetNodeOrNull<RastroDeCorrida>("Rastro") is { } rastro) rastro.Arranque(OrigemDoSalto(doServidor));
	}

	/// <summary>
	/// DE ONDE ESTE CORPO SALTOU -- e a resposta muda conforme quem esta dirigindo.
	///
	/// ============================ COM AS REDEAS: A MINHA, GUARDADA ============================
	/// Quem apertou a tecla fui eu, e eu guardei a posicao no instante do gesto (`_deOndeSai`). As duas
	/// razoes pra nao usar a do pacote estao inteiras no <see cref="DeixarVulto"/>: ela esta ATRASADA
	/// (e a ultima que chegou ao servidor por input) e vem por um canal SEM ORDEM garantida com a
	/// correcao que move o corpo.
	///
	/// ============================ SEM AS REDEAS: A DO SERVIDOR, SEMPRE ============================
	/// **E aqui mora a fera do Oozaru e a furia lendaria.** Um corpo possuido tambem arranca -- so que
	/// quem aperta a tecla e o cerebro, no servidor. O `_deOndeSai` so e escrito no `LerAcoes`, que a
	/// posse nao roda: ele ficaria parado no ultimo soco que o DONO deu, possivelmente minutos e uma
	/// zona atras. O borrao nasceria como uma faixa atravessando o mapa.
	///
	/// Nao e hipotese: e literalmente o defeito que o dono ja relatou uma vez neste mesmo campo --
	/// *"o efeito do zanzoken acontece no ultimo local q usei o shift+espaco"*. Sem redeas, as duas
	/// objecoes acima nem se aplicam: o corpo ja nao anda por input nenhum (ele PERSEGUE o ponto do
	/// servidor, ver `ReceberPosse`), entao a posicao do pacote e a unica verdade que existe.
	/// ==========================================================================================
	/// </summary>
	private Vector2 OrigemDoSalto(Vector2 doServidor) => _semRedeas ? doServidor : _deOndeSai;

	/// <summary>
	/// Onde mirar. A zona nao GARANTE o membro -- so pesa o sorteio a favor dele -- mas e o
	/// que permite ir atras das pernas de quem foge ou da cabeca de quem esta quase caindo.
	/// </summary>
	private static void LerMira()
	{
		if (GameClient.Instance is not { } cli) return;

		byte? zona = null;
		if (Godot.Input.IsActionJustPressed("aim_none")) zona = 0;
		else if (Godot.Input.IsActionJustPressed("aim_head")) zona = 1;
		else if (Godot.Input.IsActionJustPressed("aim_torso")) zona = 2;
		else if (Godot.Input.IsActionJustPressed("aim_abdomen")) zona = 3;
		else if (Godot.Input.IsActionJustPressed("aim_arms")) zona = 4;
		else if (Godot.Input.IsActionJustPressed("aim_legs")) zona = 5;
		if (zona.HasValue) cli.SendAim(zona.Value);

		if (Godot.Input.IsActionJustPressed("lethal")) cli.SendLethal(!cli.Letal);
	}

	/// <summary>
	/// O servidor recusou o passo. Aqui NAO se discute: a posicao dele vale. Escreve na
	/// posicao EXATA, nao so no no -- senao o proximo quadro parte da posicao antiga e a
	/// correcao e engolida.
	/// </summary>
	private void OnCorrected(Vec2 pos)
	{
		// NO MEIO DO VOO a correcao tambem REVELA O RUMO: e a diferenca entre onde o servidor diz
		// que estou e onde eu estava. Sem isso o cliente nao teria como deslizar sozinho -- o rumo
		// do arremesso nunca viaja num campo proprio.
		// NO VOO a correcao vira ALVO, nao teleporte: o corpo desliza ate la (ver _Process). Fora
		// dele continua valendo na hora -- correcao de passo tem que ser imediata.
		if (_empurrado) { _alvoDoVoo = pos; return; }
		// SEM AS REDEAS, IDEM: a correcao que ainda estiver no ar (pacotes de input que sairam antes de
		// a fera assumir) vira ALVO. Aplicada crua, ela teleportaria o corpo entre dois quadros e
		// desfaria a perseguicao -- que e o proprio deslize voltando por outra porta.
		if (_semRedeas) { _alvoDaPosse = pos; return; }
		_pos = pos;
		Desenhar();
	}

	/// <summary>Ate onde o servidor ja arremessou o corpo. O cliente desliza ate aqui.</summary>
	private Vec2 _alvoDoVoo;

	/// <summary>
	/// Velocidade do voo em px/s. E a mesma do servidor -- dois tiles a cada 0,1 s = 640 px/s --,
	/// entao o cliente chega no mesmo lugar e a correcao seguinte quase nao tem o que corrigir.
	/// </summary>
	private const double VelocidadeDoVoo =
		Jandirus.Core.Combat.Empurrao.TilesPorTique * ZoneCollision.TileSize / Jandirus.Core.Combat.Empurrao.SegundosPorTique;

	/// <summary>
	/// PARA ONDE O SERVIDOR ME MANDOU. Troca de zona, decolagem, pouso, saida da mente.
	///
	/// Escreve na posicao EXATA (`_pos`) e nao so no no. Escrever so no no e um bug silencioso:
	/// `_pos` e a verdade da simulacao -- e dela que sai o proximo passo e o que se manda pro
	/// servidor -- entao o corpo continuaria andando a partir do lugar ANTIGO e o servidor
	/// corrigiria pra sempre. Em terra isso passava batido (os spawns sao perto); no espaco a
	/// distancia e de milhoes de pixels e o defeito virou visivel na hora.
	/// </summary>
	public void Teleportar(Vec2 pos)
	{
		_pos = pos;
		// O ALVO DA POSSESSAO VAI JUNTO. Sem isto, quem for teleportado enquanto o servidor dirige o
		// corpo seria puxado de volta pelo alvo antigo no quadro seguinte -- ate o proximo snapshot
		// chegar, o corpo andaria pro lugar de onde acabou de sair.
		_alvoDaPosse = pos;
		Destino = null;   // chegou noutro lugar: o piloto anterior nao vale mais
		Desenhar();
	}
}
