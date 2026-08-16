using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A FUSAO, FOTOGRAFADA (`--diagfotofusao`) ============================
/// A `--fusaoduplateste` mede a fusao inteira em NUMERO, com defeitos injetados, e fecha tudo verde.
/// **E ela ficaria verde com a fusao desenhada careca e de calcao**: entre o `LookDeFusao` do servidor e
/// o pixel ha o `PeerLook`, o `_fusaoDaZona` do `World`, a pilha de camadas do `CharacterVisual`, o
/// `CabelosDeForma` e o shader do corpo. Este projeto ja catalogou esse cego cinco vezes (a memoria "a
/// bancada mede INTENCAO"), e as fotos que o dono pediu sao exatamente a metade que so o olho fecha:
///
///     fusao-a0-nasce.png       a luz NASCENDO entre os dois -- os dois corpos a vista
///     fusao-a0b-janela-limpa.png  a tela SEM disco nenhum, assim que o estouro acaba
///     fusao-a1-cena.png        o AUGE do estouro: **UM** disco, no ponto medio dos dois
///     fusao-a2-branco.png      o corpo completamente BRANCO, so a silhueta, logo depois da virada
///     fusao-a3-branco-escoando.png  o branco na cauda, com a silhueta ja legivel
///     fusao-b1-metamoro.png    a METAMORO pronta -- colete metamoriano, cabelo do Vegito
///     fusao-b2-potara.png      a POTARA pronta -- brinco SOMADO a roupa de quem convidou
///     fusao-B-lado-a-lado.png  as duas coladas, que e como o dono pediu ("roupa e cabelo diferem")
///     fusao-c1-ssj4.png        a fusao em SSJ4, de cabelo VERMELHO
///
/// ============================ E A PROVA CENTRAL E CONTADA NO PIXEL ============================
/// *"n e um em cada personagem, e so UM efeito em cima dos dois personagens, atualmente sao 2 e fica
/// estranho"*. Ate a fase 1 isso era medido por CAMPO (`LuzesDaFusaoDeTeste == 1`) -- um campo diz
/// quantos NODES a cena criou, e nao quantos claroes o dono ve. Aqui a foto do auge e VARRIDA: mascara
/// de cor calibrada na propria imagem, componentes conexos, e **contagem de nucleos** por transformada
/// de distancia. Ver <see cref="ContagemDeDiscos"/>, que explica por que contar componentes sozinho
/// ficaria VERDE com o defeito de volta (os dois discos do defeito se ENCOSTAM, sempre).
///
/// Cada tomada sai em DOIS arquivos: a tela cheia (prova o LUGAR) e um `-perto.png` recortado e
/// ampliado em Nearest -- num quadro de 1920x1080 um boneco de 32 px nao sustenta afirmacao nenhuma
/// sobre a roupa que ele esta vestindo.
/// ================================================================================================
///
/// ============================ NADA AQUI FUNDE NINGUEM ============================
/// A cena sai do `ComecarACenaDaFusao` (o MESMO funil por onde a danca resolvida e a Potara aceita
/// entram), a fusao sai do `if (agora >= c.Funde)` do `TickDaCenaDeFusao`, a roupa e o cabelo saem do
/// `Fundir`/`VestirAFusao`, o desfazer sai do `Separar` e o SSJ4 sai do `admin_forma` do menu P.
///
/// O CONVITE E O QUICK TIME EVENT FICAM DE FORA de proposito: eles nao tem pixel, sao medidos de ponta
/// a ponta pela `--fusaoduplateste`, e uma tela de QTE por cima da cinematica atrapalharia justamente a
/// foto que esta bancada existe pra tirar. Ver `GameServer.FotoDaFusao.cs`.
/// ================================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     &lt;godot&gt; --path . --host --rede 7908 --diagfotofusao --position 1920,0 \
///              --raca Saiyan --conta bancada_foto_fusao --nome Olheiro
///
/// A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o dono trabalha no principal.
/// </summary>
public partial class RoboDeFotoDeFusao : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S
		=> Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>Depois disto ela desiste e conta o que faltou -- bancada travada se le como bancada morta.</summary>
	private const double Paciencia = 300;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private int _outro;
	private bool _tireiNasce, _tireiLimpo, _tireiCena, _tireiBranco, _tireiBrancoMeio,
				 _tireiBrancoTarde, _tireiPronta;
	private string _cabeloDoJogador = "";

	/// <summary>A hora local e se este mundo esta de noite -- ver `GameServer.SolAPinoNaFotoDeFusao`.</summary>
	private double _hora;
	private bool _noite;

	/// <summary>
	/// Quantos quadros a bancada achou o menu do jogo ABERTO e o fechou. Ver o bloco no
	/// <see cref="_Process"/>: e a marca de que alguem mexeu na janela durante a rodada, e ela vai pro
	/// log porque uma bancada que se defende calada esconde o que a proxima pessoa precisa saber.
	/// </summary>
	private int _fecheiOMenu;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --    " + oque);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private const int PAssentar = 0, PMontar = 1,
					  PPortaoLonge = 2, PPortaoPerto = 3, PPuxao = 4, PBorda = 5, PVoltarAoPalco = 6,
					  PMetaCena = 7, PMetaFim = 8,
					  PPotCena = 9, PPotFim = 10,
					  PSsj4 = 11, PFim = 12;

	/// <summary>
	/// QUANTOS ESTOUROS O ROTEIRO TEM -- lido dos BEATS, e nao um `3` escrito aqui.
	///
	/// A luz da fusao deixou de ser cortina e virou `flick` -- e o dono cortou pra UM
	/// (*"essa animacao do fusion light so toca uma vez"*, ver `Efeito.LuzDaFusao`). O obturador conta
	/// estouro, e "quantos ha" so tem sentido se vier da mesma lista que o jogo toca: um `1` escrito
	/// aqui mentiria no dia em que o dono pedisse os tres do DU de volta.
	/// </summary>
	private static int EstourosDoRoteiro
	{
		get
		{
			int n = 0;
			foreach (Jandirus.Core.Forms.Beat b in Jandirus.Core.Forms.Cinematicas.Fusao.Beats)
				if (b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.LuzDaFusao)) n++;
			return n;
		}
	}

	/// <summary>
	/// EM QUE QUADRO DA FOLHA O AUGE E FOTOGRAFADO -- **o 3, e ele e o quadro mais cheio**.
	///
	/// Medidos os sete quadros do `flick`: 468 / 6.640 / 5.937 / **9.548** / 5.662 / 132 / 24 pixels
	/// opacos. O quadro 3 e o unico em que o miolo solido atravessa a folha inteira (112 px de ponta a
	/// ponta), e por isso ele e o unico em que "o disco cobre os dois" e uma afirmacao com sentido.
	///
	/// **E ele tambem e o instante SEM ANEL**, e isso importa pra a medida: o anel de choque
	/// (`Efeito.AnelDeChoque`, beats 1,0 / 2,0 / 3,0, meio segundo cada) e desenhado com a cor da AURA a
	/// 0,75 de alfa, e sobre a grama ele cai **na mesma cor do disco** -- medido na foto: `cadbd2`
	/// dos dois. Fotografar com anel na tela colaria uma coroa na mancha e inflaria a caixa. O quadro 3
	/// do terceiro estouro cai em 2,8-2,9 s: o anel dos 2,0 morreu em 2,5 e o dos 3,0 ainda nao nasceu.
	/// </summary>
	private const int QuadroDoAuge = 3;

	/// <summary>
	/// A PECA QUE O JOGADOR VESTE ANTES DE TUDO -- ver o bloco no <see cref="Montar"/>. Qualquer peca
	/// do catalogo serve; esta e uma das primeiras do `visual.json` e nao tem nada de especial.
	/// </summary>
	private const string PecaDoJogador = "Blue suit";

	/// <summary>
	/// ============================ OS DOIS CORPOS NASCEM NO **PORTAO DO CONVITE** ============================
	/// Eram dois tiles ("perto o bastante pra caberem no mesmo recorte"). Isso deixava a prova central
	/// sem dente: a 64 px um do outro, os dois discos do defeito ficam com os nucleos a 128 px de tela e
	/// se fundem num miolo so -- a contagem devolveria 1 com o defeito na tela.
	///
	/// ============================ E ELA E MAIOR QUE A DO JOGO, DE PROPOSITO ============================
	/// **Em jogo os dois chegam COLADOS** -- a Danca e a Namekuseijin exigem `Fusao.TilesColados` (um
	/// tile) e a Potara PUXA os dois ate isso valer (`Fusao.PuxaOsCorpos`). Fotografar a cena com os
	/// corpos a um tile seria fotografar o caso real, e seria uma bancada PIOR: dois discos a 32 px um
	/// do outro tem area e caixa quase iguais as de um disco so, e a contagem perderia o dente contra o
	/// defeito que ela existe pra pegar.
	///
	/// Entao a distancia da FOTO e um palco, e nao uma afirmacao sobre o jogo: quatro tiles e a
	/// separacao que faz duas copias da folha darem 1,40 de area e 146% de caixa (medido -- ver
	/// `ContagemDeDiscos`). Que o clarao cubra os dois AQUI e, portanto, uma afirmacao MAIS FORTE que a
	/// do jogo, e nao mais fraca.
	/// ==================================================================================================
	/// </summary>
	private static Vec2 OPalcoDaFoto => new(TilesDoPalcoDaFoto * ZoneCollision.TileSize, 0);

	/// <inheritdoc cref="OPalcoDaFoto"/>
	private const int TilesDoPalcoDaFoto = 4;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv)
		{
			Nota("sem servidor no processo (`--diagfotofusao` precisa de `--host`)");
			Fechar();
			return;
		}

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s) no passo {_passo}"); Fechar(); return; }
		_t += delta;

		// ============================ O MENU FICA FECHADO, E ISTO E UM ACHADO DE RODADA ============================
		// Esta bancada abre JANELA, e janela numa maquina em que o dono esta trabalhando recebe clique e
		// tecla. Duas rodadas foram estragadas por isso e de dois jeitos diferentes: numa o menu P mandou
		// um `admin_forma [0|ssj1]` e derrubou a forma no meio da espera do SSJ4; noutra o painel do menu
		// ficou ABERTO por cima da cena -- a foto do auge saiu com a lista de abas e um fundo escuro, a
		// cor amostrada no meio do clarao deu `1c1a1f` e onze provas cairam por um motivo que nao e do
		// jogo. **A guarda de luminosidade nao pega este caso**: o texto do menu e claro, entao "da pra
		// olhar" continua verdadeiro numa foto que nao mostra a cena.
		//
		// Fechar por quadro custa uma comparacao de bool e resolve os dois: o menu nao consegue ficar
		// aberto tempo bastante pra entrar numa foto, e nenhum botao dele fica sob o cursor.
		// ======================================================================================================
		if (MenuJogo.Instancia is { Visible: true } menu) { menu.Fechar(); _fecheiOMenu++; }

		// O SOL A PINO A RODADA INTEIRA, e nao so na largada. Ver `GameServer.SolAPinoNaFotoDeFusao`:
		// a primeira rodada desta bancada fechou "TUDO OK" com as cinco fotos PRETAS, porque o mundo
		// estava de noite e as checagens de campo leem o `LookDeFusao` e nao o pixel.
		(_hora, _noite) = srv.SolAPinoNaFotoDeFusao(cli.LocalId);

		switch (_passo)
		{
			case PAssentar: Assentar(cli, mundo); break;
			case PMontar: Montar(srv, cli, mundo); break;
			case PPortaoLonge: OPortaoDeLonge(srv, cli, mundo); break;
			case PPortaoPerto: OPortaoColado(srv, cli, mundo); break;
			case PPuxao: OPuxao(srv, cli, mundo); break;
			case PBorda: ABordaDoPuxao(srv, cli, mundo); break;
			case PVoltarAoPalco: VoltarAoPalco(srv, cli, mundo); break;
			case PMetaCena: ACena(srv, cli, mundo, TipoDeFusao.Danca); break;
			case PMetaFim: OFimDaMetamoro(srv, cli); break;
			case PPotCena: ACena(srv, cli, mundo, TipoDeFusao.Potara); break;
			case PPotFim: OFimDaPotara(srv, cli); break;
			case PSsj4: OSsj4(srv, cli, mundo); break;
			default: Fechar(); break;
		}
	}

	/// <summary>
	/// ============================ ESTE ROBO PROCESSA **DEPOIS** DE TODO MUNDO ============================
	/// A `Mira` de um quadro descreve a imagem que aquele quadro desenhou -- posicao dos dois corpos,
	/// posicao da luz, zoom da camera. Mas quem MOVE essas coisas sao outros nodes, no `_Process` deles:
	/// o corpo interpola entre dois snapshots, a camera persegue o corpo e ainda TREME durante a cena.
	/// Lido na ordem padrao da arvore, este robo lia a posicao de ANTES de o quadro ser montado -- e o
	/// recorte saia deslocado do boneco que ele existe pra mostrar (medido: a foto A2 saiu com o corpo
	/// fora do centro, e a legenda dizia "so a silhueta").
	///
	/// Prioridade alta = roda por ultimo. Assim a `Mira` do quadro N e lida com o mundo ja na pose do
	/// quadro N, e o `GetImage()` do quadro N+1 devolve exatamente essa imagem.
	/// ==================================================================================================
	/// </summary>
	public override void _Ready() => ProcessPriority = 10_000;

	private void Virar(int proximo) { _passo = proximo; _t = 0; _sub = 0; }

	/// <summary>Em que ponto do passo atual a bancada esta. Zerado pelo <see cref="Virar"/>.</summary>
	private int _sub;

	private void Sub(int proximo) { _sub = proximo; _t = 0; }

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	/// <summary>
	/// TRES SEGUNDOS ANTES DE QUALQUER COISA. O corpo local nasce, a zona carrega, a aparencia chega --
	/// e uma foto tirada no primeiro quadro mostra o chao antes das camadas assentarem. E a mesma
	/// espera que as outras bancadas de foto deste projeto fazem, pelo mesmo motivo.
	/// </summary>
	private void Assentar(GameClient cli, World mundo)
	{
		if (_t < 3) return;
		if (mundo.CorpoDeTeste(cli.LocalId) == null) return;
		Virar(PMontar);
	}

	// =====================================================================
	// 1) O PALCO -- o cabelo do par nomeado e o segundo corpo
	// =====================================================================
	/// <summary>
	/// O par Goku + Vegeta e a UNICA regra de cabelo que o dono nomeou (*"se um tiver o cabelo do
	/// Vegeta e o outro do Goku, a fusao usa `VegitoHairPVP.png`"*), entao a foto tem que montar esse
	/// par -- com dois penteados quaisquer a regra nao apareceria e a foto nao provaria nada.
	///
	/// O penteado do JOGADOR e devolvido na limpeza (ver `GameServer.FotoDaFusao.LimparAFotoDeFusao`).
	/// </summary>
	private void Montar(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_outro == 0)
		{
			_cabeloDoJogador = srv.EstadoNaFotoDeFusao(cli.LocalId).Cabelo;
			srv.CabeloNaFotoDeFusao(cli.LocalId, "Goku");

			// ============================ A PECA VESTIDA E O QUE SEPARA AS DUAS REGRAS ============================
			// Sem ela as duas fotos desenhariam UMA camada cada e a comparacao nao diria nada: a
			// Metamoro SUBSTITUI o guarda-roupa e a Potara SOMA ao dele, e um guarda-roupa vazio faz as
			// duas regras darem o mesmo resultado. A primeira rodada desta bancada afirmou "a roupa e SO
			// o colete" e ficou verde sem ter provado nada. Ver `GameServer.VestirNaFotoDeFusao`.
			// ==================================================================================================
			Conferir(srv.VestirNaFotoDeFusao(cli.LocalId, PecaDoJogador),
					 $"o jogador esta vestindo alguma coisa ('{PecaDoJogador}') -- sem isso a Metamoro "
				   + "e a Potara desenhariam a mesma camada e a comparacao nao provaria nada");

			_outro = srv.ForjarParaAFotoDeFusao(
				cli.LocalId, OPalcoDaFoto, "Vegeta", 900_000, "Vegeta");

			Conferir(_outro != 0, "o segundo corpo nasceu ao lado (fusao entre dois nao se fotografa com um)");
			if (_outro == 0) { Fechar(); return; }
			return;
		}

		// UM SEGUNDO PRA A APARENCIA CHEGAR: ela nao viaja no snapshot, vem no `PeerLook`.
		if (_t < 1.2) return;

		Conferir(mundo.CorpoDeTeste(_outro) != null, "...e ele esta DESENHADO na minha tela");
		Nota($"cabelo meu = '{srv.EstadoNaFotoDeFusao(cli.LocalId).Cabelo}', "
		   + $"dele = '{srv.EstadoNaFotoDeFusao(_outro).Cabelo}'");
		Nota($"os dois estao a {TilesDoPalcoDaFoto} tiles (o palco da foto -- em jogo eles chegam "
		   + $"colados, a {Fusao.TilesColados}), "
		   + $"zoom da camera {Zoom():0.##}x");

		// O PORTAO DA DANCA E ARMADO AQUI, uma vez -- ver `GameServer.PrepararAMetamoroNaFotoDeFusao`
		// sobre por que sem isto a metade "colada" da R1 reprovaria por skill em vez de por distancia.
		srv.PrepararAMetamoroNaFotoDeFusao(cli.LocalId, _outro);

		Virar(PPortaoLonge);
	}

	// =====================================================================
	// R1/R2/R3) A COREOGRAFIA -- e ela entra pela PORTA, e nao pelo meio
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTE BLOCO ACRESCENTA, E POR QUE ELE PRECISOU EXISTIR ============================
	/// A fase 1 desta bancada fotografava o CLARAO: ela plantava os dois corpos onde queria e mandava a
	/// cinematica comecar (`ComecarACenaNaFotoDeFusao`). Isso responde *"um efeito so, em cima dos
	/// dois"*, que era metade do pedido -- e deixa a outra metade sem foto nenhuma. A outra metade e a
	/// COREOGRAFIA, e o dono a descreveu passo a passo: *"na potara quando ela comecar eles sao puxados
	/// um pro lado do outro e QUANDO SE ENCOSTAREM a cinematica comeca"*.
	///
	/// Aqui a bancada entra pela porta: <see cref="Jandirus.Server.GameServer.ConvidarNaFotoDeFusao"/> e
	/// o mesmo `Convidar` do verb, o aceite e o mesmo `ResponderAoConvite` do `fus_sim`, e dali em
	/// diante quem anda com os corpos e o `TickDoPuxaoDeFusao` de producao. **Esta bancada nao move
	/// ninguem pra perto de ninguem** -- ela so planta o palco e olha.
	///
	/// ============================ E A REGUA E A POSICAO **DESENHADA** ============================
	/// A distancia medida e a dos corpos NA TELA (`World.PosicaoDesenhadaDe`), e nao a `Pos` do
	/// servidor. As duas divergem por um tique inteiro de interpolacao, e o que o dono ve e a primeira.
	/// A do servidor viaja no log ao lado, como conferencia.
	/// ================================================================================================
	/// </summary>
	private const int TilesDoPortaoDeLonge = 8;

	/// <summary>
	/// DE ONDE O PUXAO PARTE -- os <see cref="Fusao.TilesDaPotara"/> do `oview(usr,20)`
	/// (`Potara_Fusion.dm:96`), ou seja **a distancia maxima que o jogo permite**.
	///
	/// Nao e um palco escolhido pra caber: e o pior caso do proprio jogo, e por isso a rampa que a
	/// bancada grava e a mais longa que existe. Aos 20 tiles o segundo corpo comeca FORA da tela (a
	/// camera mostra 12,5 tiles pra cada lado no zoom 2), e por isso a primeira foto do puxao so dispara
	/// quando os dois cabem juntos -- ver <see cref="CabemOsDoisNaFoto"/>.
	/// </summary>
	private static int TilesDoPuxao => Fusao.TilesDaPotara;

	/// <summary>Quanto a bancada espera o cliente assentar depois de plantar um palco novo.</summary>
	private const double EsperaDeAssentar = 1.2;

	/// <summary>Por quanto tempo a distancia e observada quando NAO ha puxao (o contra-exemplo da R2).</summary>
	private const double JanelaDoContraExemplo = 1.0;

	/// <summary>A rampa do puxao, quadro a quadro: quanto tempo, quanto desenhado, quanto no servidor.</summary>
	private readonly List<(double T, float Desenhado, double Servidor, bool EmCena)> _rampaDoPuxao = [];

	/// <summary>A mesma leitura, sem puxao nenhum -- o contra-exemplo que da sentido a rampa.</summary>
	private readonly List<float> _reguaSemPuxao = [];

	/// <summary>
	/// A DISTANCIA ENTRE OS DOIS CORPOS **COMO ELES ESTAO DESENHADOS**, em pixels de MUNDO.
	///
	/// ============================ DESENHADA NAO E "DE TELA", E A DIFERENCA CUSTOU UMA FOTO ============================
	/// `World.PosicaoDesenhadaDe` devolve a posicao do NO mais o deslocamento de altura do visual -- ou
	/// seja o lugar do mundo onde o boneco esta pintado, e nao um pixel da janela. Ela e a regua certa
	/// pra R2 (*"eles andam um pro outro"*) justamente por ser a interpolada, a que o jogador ve mexer,
	/// e nao a `Pos` que o servidor acabou de escrever.
	///
	/// Mas ela **nao serve pra enquadrar**: a camera tem zoom 2, entao 628 px de mundo sao 1256 px de
	/// JANELA -- e a primeira foto do puxao saiu com o segundo corpo a 2.184 px numa tela de 1.920,
	/// ou seja fora dela, num painel de grama vazia. Onde a pergunta e "cabe na foto?", o numero tem que
	/// ser multiplicado pelo <see cref="Zoom"/>.
	/// ==============================================================================================================
	/// </summary>
	private static float DistanciaEntreOsDesenhados(World mundo, int a, int b)
	{
		if (mundo.PosicaoDesenhadaDe(a) is not { } pa) return -1;
		if (mundo.PosicaoDesenhadaDe(b) is not { } pb) return -1;
		return pa.DistanceTo(pb);
	}

	/// <summary>
	/// OS DOIS CABEM NA FOTO? -- a distancia de MUNDO convertida pra janela pelo zoom da camera.
	///
	/// A camera persegue o corpo local, ou seja ele esta sempre no meio da tela; o outro esta a
	/// `distancia x zoom` px dali. Meia largura de tela ja seria a borda -- 45% deixa o boneco inteiro
	/// dentro com folga, e o recorte da tira (<see cref="FracaoDaTira"/>) ainda o alcanca depois de o
	/// <see cref="Encaixar"/> empurrar a caixa pra dentro.
	/// </summary>
	private bool CabemOsDoisNaFoto(float distanciaDeMundo) =>
		distanciaDeMundo > 0
		&& distanciaDeMundo * Zoom() <= (GetViewport()?.GetVisibleRect().Size.X ?? 1920) * 0.45f;

	/// <summary>
	/// R1 (primeira metade) + o CONTRA-EXEMPLO da R2 -- **a mesma foto responde as duas**.
	///
	/// De <see cref="TilesDoPortaoDeLonge"/> tiles a Metamoro tem que ser recusada (o `get_dist > 1` do
	/// DU, <see cref="Fusao.TilesColados"/>), e como nao ha convite fechado tambem nao ha puxao: a
	/// distancia entre os dois **nao anda sozinha**. E isso que faz a rampa do puxao significar alguma
	/// coisa -- sem esta janela, "a distancia diminuiu" poderia ser a camera, a interpolacao ou o vento.
	/// </summary>
	private void OPortaoDeLonge(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		switch (_sub)
		{
			case 0:
				srv.ZerarRecargaNaFotoDeFusao(cli.LocalId, _outro);
				Conferir(srv.PlantarNoCorredorNaFotoDeFusao(cli.LocalId, _outro, TilesDoPortaoDeLonge),
						 $"R1 os dois foram plantados num corredor LIVRE, a {TilesDoPortaoDeLonge} tiles "
					   + "(parede manda mais que puxao -- sem corredor a prova ficaria vermelha por "
					   + "colisao e nao por fusao)");
				Sub(1);
				return;

			case 1:
			{
				if (_t < EsperaDeAssentar || DistanciaEntreOsDesenhados(mundo, cli.LocalId, _outro) < 0) return;

				// **E O MOTIVO IMPORTA**: um "nao entrou" por skill, por raca ou por poder desigual
				// deixaria esta prova verde sem que o portao da DISTANCIA tivesse sido consultado -- e a
				// metade "colada" logo abaixo estaria medindo outra coisa. Ver `AvaliarOConvite`.
				var (entrou, porque) = srv.ConvidarNaFotoDeFusao(cli.LocalId, _outro, TipoDeFusao.Danca);
				Conferir(!entrou && porque == Jandirus.Core.Social.RecusaDeFusao.Longe,
						 $"R1 a METAMORO a {TilesDoPortaoDeLonge} tiles e RECUSADA **por DISTANCIA** "
					   + $"(motivo: {porque}) -- o portao e {Fusao.TilesColados} tile, o `oview(1)` do DU");
				Sub(2);
				return;
			}

			default:
				_reguaSemPuxao.Add(DistanciaEntreOsDesenhados(mundo, cli.LocalId, _outro));
				if (_t < JanelaDoContraExemplo) return;

				float menor = _reguaSemPuxao.Min(), maior = _reguaSemPuxao.Max();
				var e = srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro);

				Conferir(!e.Puxando && !e.EmCena && !e.Fundido,
						 "R1 ...e recusado o convite NADA comeca: nem puxao, nem cinematica, nem fusao");

				// O CONTRA-EXEMPLO DA R2, e ele e uma razao e nao um numero absoluto: a tela inteira anda
				// junto quando a camera se mexe, e o que se afirma e que o VAO ENTRE OS DOIS nao encolheu.
				Conferir(maior > 0 && (maior - menor) / maior < 0.05f,
						 $"R2(contra) sem puxao a distancia entre os dois NAO diminui -- {_reguaSemPuxao.Count} "
					   + $"quadros em {JanelaDoContraExemplo:0.#} s, de {maior:0} a {menor:0} px de mundo "
					   + $"({(maior - menor) / Math.Max(1f, maior):P1} de variacao)");

				TomarDaCoreografia(mundo, cli, "fusao-r1-longe-recusada",
					$"R1 a METAMORO a {TilesDoPortaoDeLonge} tiles: recusada, e os dois continuam onde estavam");
				Virar(PPortaoPerto);
				return;
		}
	}

	/// <summary>
	/// R1 (segunda metade) -- **os MESMOS dois corpos**, agora no tile ao lado, e o convite ENTRA.
	///
	/// E o vizinho de DIAGONAL, que e o pior caso do portao: em linha reta ele esta a 1,41 tile e no
	/// `get_dist` do BYOND (Chebyshev sobre turfs, <see cref="Fusao.DistanciaEmTilesDoDm"/>) ele esta a
	/// 1 -- e o `oview(1)` do original o inclui. Provar com o vizinho ORTOGONAL seria provar o caso
	/// facil e deixar passar exatamente a conta que a fase 1 corrigiu.
	/// </summary>
	private void OPortaoColado(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_sub == 0)
		{
			srv.PorPertoNaFotoDeFusao(_outro, cli.LocalId,
				new Vec2(ZoneCollision.TileSize, ZoneCollision.TileSize));
			Sub(1);
			return;
		}

		if (_t < EsperaDeAssentar || DistanciaEntreOsDesenhados(mundo, cli.LocalId, _outro) < 0) return;

		// O PORTAO E REARMADO AQUI, e nao so no `Montar`: o `PowerLevel` reescreve o `expressedBP` de
		// todo corpo a cada tique, e entre uma coisa e outra passaram uns dez segundos de coreografia.
		// Ver `GameServer.PrepararAMetamoroNaFotoDeFusao` sobre por que sem isto a recusa aqui seria por
		// poder desigual e a prova diria "distancia".
		srv.PrepararAMetamoroNaFotoDeFusao(cli.LocalId, _outro);

		var (entrou, porque) = srv.ConvidarNaFotoDeFusao(cli.LocalId, _outro, TipoDeFusao.Danca);
		Conferir(entrou,
				 "R1 ...e os MESMOS dois corpos no tile ao lado (o vizinho de DIAGONAL, que o `get_dist` "
			   + $"conta como 1): a METAMORO e ACEITA -- o convite entra na mesa (motivo: {porque})");

		TomarDaCoreografia(mundo, cli, "fusao-r1-colado-aceita",
			"R1 os mesmos dois colados: a METAMORO e aceita");

		// A MESA VOLTA A FICAR LIMPA: o pendente da Danca barraria a Potara do passo seguinte
		// (`RecusaDeFusao.JaTemPedido`), e a R2 reprovaria por um motivo que nao e o dela.
		srv.LimparPedidoNaFotoDeFusao(_outro);
		Virar(PPuxao);
	}

	/// <summary>Quanto o puxao pode demorar antes de a bancada desistir dele e falar.</summary>
	private const double PacienciaDoPuxao = 10.0;

	private bool _tireiPuxaoA, _tireiPuxaoB, _tireiEncontro, _houvePuxaoSemCena;
	private double _distanciaInicialDoPuxao = -1;

	/// <summary>
	/// R2 e R3 -- **os dois andam sozinhos, e a cinematica so comeca quando encostam**.
	///
	/// ============================ TRES AFIRMACOES, TRES MEDIDAS DIFERENTES ============================
	///   * **R2**: a distancia DESENHADA entre os dois cai. A rampa inteira e gravada (um ponto por
	///     quadro), e nao dois pontos escolhidos -- dois pontos escondem um teleporte, e teleporte e
	///     exatamente o defeito que a velocidade do `step_to` existe pra evitar.
	///   * **R3 (a ordem)**: existe pelo menos um quadro com o puxao VIVO e a cinematica AINDA NAO. Se
	///     a cena comecasse junto com o aceite, este contador ficaria em zero.
	///   * **R3 (o encontro)**: quando a cena comeca, os dois estao a <see cref="Fusao.TilesColados"/> --
	///     ou seja ela comecou por CHEGAR, e nao por prazo.
	/// ==============================================================================================
	/// </summary>
	private void OPuxao(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		switch (_sub)
		{
			case 0:
				srv.ZerarRecargaNaFotoDeFusao(cli.LocalId, _outro);
				Conferir(srv.PlantarNoCorredorNaFotoDeFusao(cli.LocalId, _outro, TilesDoPuxao),
						 $"R2 os dois foram plantados a {TilesDoPuxao} tiles -- o `oview(usr,20)` do "
					   + "`Potara_Fusion.dm:96`, que e a distancia MAXIMA que a Potara aceita");
				Sub(1);
				return;

			case 1:
			{
				if (_t < EsperaDeAssentar) return;

				Conferir(srv.ConvidarNaFotoDeFusao(cli.LocalId, _outro, TipoDeFusao.Potara).Entrou,
						 $"R2 a POTARA a {TilesDoPuxao} tiles CONVIDA (onde a Metamoro foi recusada a "
					   + $"{TilesDoPortaoDeLonge}) -- e por isso ela tem o que fechar");
				Conferir(srv.AceitarNaFotoDeFusao(_outro), "R2 ...e o convidado aceitou pelo `fus_sim`");

				var e0 = srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro);
				_distanciaInicialDoPuxao = e0.DistanciaPx;
				Conferir(e0.Puxando, "R2 ...e o PUXAO comecou (e nao a cinematica)");
				Conferir(!e0.EmCena && !e0.Fundido,
						 "R3 ...e no instante do aceite ainda NAO ha cinematica nem fusao");
				Sub(2);
				return;
			}

			default:
			{
				var e = srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro);
				float desenhado = DistanciaEntreOsDesenhados(mundo, cli.LocalId, _outro);
				_rampaDoPuxao.Add((_t, desenhado, e.DistanciaPx, e.EmCena));
				if (e.Puxando && !e.EmCena) _houvePuxaoSemCena = true;

				// ---- as duas fotos do puxao: a primeira quando os DOIS CABEM na foto, a segunda no fim ----
				// Ver `CabemOsDoisNaFoto`: aos 20 tiles do convite o segundo corpo esta a 1.256 px de
				// JANELA numa tela de 1.920 com a camera centrada no primeiro, ou seja fora dela. A
				// primeira versao disparava por distancia de MUNDO e gravou um painel de grama vazia.
				if (!_tireiPuxaoA && CabemOsDoisNaFoto(desenhado))
				{
					_tireiPuxaoA = true;
					TomarDaCoreografia(mundo, cli, "fusao-r2-puxao-longe",
						"R2 o puxao: os dois ainda longe, ja andando um pro outro");
				}
				else if (_tireiPuxaoA && !_tireiPuxaoB && desenhado > 0 && _distanciaInicialDoPuxao > 0
						 && e.DistanciaPx < _distanciaInicialDoPuxao * 0.30)
				{
					_tireiPuxaoB = true;
					TomarDaCoreografia(mundo, cli, "fusao-r2-puxao-perto",
						"R2 ...e o mesmo puxao ja quase fechado");
				}

				if (e.EmCena)
				{
					Conferir(_houvePuxaoSemCena,
							 "R3 houve pelo menos um quadro com o PUXAO correndo e a cinematica ainda "
						   + $"NAO ({_rampaDoPuxao.Count} quadros de rampa gravados)");
					Conferir(!e.Puxando, "R3 ...e ao comecar a cena o puxao ja tinha acabado (um dono de cada vez)");

					// NA MOEDA DO BYOND, e nao em pixels: quem decide que "encostaram" e o
					// `JaEncostaram`, que pergunta ao `get_dist` (Chebyshev sobre TURFS). Dois corpos em
					// turfs vizinhos na diagonal estao a 1 ali e a ate 90 px em linha reta -- uma prova
					// em pixels reprovaria a cena certa, e foi o que ela fez na primeira rodada (53 px).
					Conferir(e.DistanciaTiles <= Fusao.TilesColados,
							 $"R3 ...e a cena comecou com os dois ENCOSTADOS -- {e.DistanciaTiles:0} tile "
						   + $"no `get_dist` do BYOND (teto {Fusao.TilesColados}), {e.DistanciaPx:0} px "
						   + "em linha reta");

					RelatarARampa();
					Sub(3);
					return;
				}

				if (_t > PacienciaDoPuxao)
				{
					Conferir(false, $"R2 o puxao chegou a encostar em {PacienciaDoPuxao:0} s "
								  + $"(parou a {e.DistanciaPx:0} px; puxando = {e.Puxando})");
					RelatarARampa();
					Virar(PBorda);
				}
				return;
			}

			case 3:
			{
				// ============================ A FOTO DO ENCONTRO ESPERA O **DESENHO** ENCOSTAR ============================
				// O servidor decide "encostaram" pela `Pos` que ele acabou de escrever; o cliente desenha
				// o corpo remoto perseguindo o alvo do ultimo snapshot, e a 2.560 px/s de fechamento ele
				// fica uns dois tiques atras -- medido na rampa: o servidor le 53 px de mundo enquanto o
				// desenho ainda mostra 280. Fotografar no instante da DECISAO daria um painel com os dois
				// separados sob a legenda "encostaram", que e o contrario do que a tira existe pra contar.
				//
				// Entao a decisao e medida no tique do servidor (logo acima) e a FOTO espera o fato da
				// tela. O prazo abaixo e paciencia e nao relogio: sem ele a bancada penduraria calada.
				// ======================================================================================================
				if (!_tireiEncontro)
				{
					float desenhado = DistanciaEntreOsDesenhados(mundo, cli.LocalId, _outro);
					if ((desenhado > 0 && desenhado <= 3 * ZoneCollision.TileSize) || _t > 1.5)
					{
						_tireiEncontro = true;
						TomarDaCoreografia(mundo, cli, "fusao-r3-encostaram",
							"R3 encostaram -- e SO agora a cinematica comeca");
						Conferir(desenhado > 0 && desenhado <= 3 * ZoneCollision.TileSize,
								 $"R3 ...e os dois chegaram a se encostar NA TELA ({desenhado:0} px de "
							   + $"mundo, teto de 3 tiles) -- o desenho persegue o snapshot e chega "
							   + "depois da decisao do servidor");
					}
				}

				// A CENA CORRE ATE O FIM pelo relogio de producao -- e so entao a fusao se desfaz. O
				// clarao e fotografado no palco de 4 tiles, onde a contagem de discos tem dente (ver
				// `OPalcoDaFoto`).
				if (_t < Jandirus.Core.Forms.Cinematicas.Fusao.SegundosPreso + 1.5) return;

				var e = srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro);
				Conferir(e.Fundido, "R3 ...e a fusao da Potara chegou a existir no fim da cena "
									+ "(a coreografia inteira, do convite ao corpo fundido)");

				// A RECARGA E CARIMBADA NA SEPARACAO, e nao no `Fundir` -- `Fusion.dm:320` e `:334`. Ler
				// o carimbo com a fusao ainda de pe media o instante errado (e reprovava por isso).
				srv.SepararNaFotoDeFusao(cli.LocalId, "a bancada vai medir a borda do puxao agora");
				Conferir(srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro).Recarga > 0,
						 "R3 ...e desfeita a fusao a recarga de 1 h foi cobrada de quem FUNDIU "
					   + "(`Fusion.dm:320` -- compare com a borda, logo abaixo)");
				Virar(PBorda);
				return;
			}
		}
	}

	/// <summary>A rampa inteira no log -- e ela e a prova, e nao a ilustracao dela.</summary>
	private void RelatarARampa()
	{
		if (_rampaDoPuxao.Count < 2)
		{ Conferir(false, "R2 a rampa do puxao tem quadros pra medir"); return; }

		var medidos = _rampaDoPuxao.FindAll(p => p.Desenhado > 0);
		float primeira = medidos.Count > 0 ? medidos[0].Desenhado : -1;
		float ultima = medidos.Count > 0 ? medidos[^1].Desenhado : -1;

		Conferir(medidos.Count >= 2 && ultima < primeira * 0.5f,
				 $"R2 **os dois ANDAM um pro outro sozinhos**: o vao entre os corpos DESENHADOS caiu de "
			   + $"{primeira:0} pra {ultima:0} px de mundo em {medidos[^1].T - medidos[0].T:0.00} s "
			   + $"({medidos.Count} quadros medidos)");

		Conferir(_rampaDoPuxao[0].Servidor > _rampaDoPuxao[^1].Servidor,
				 $"R2 ...e o servidor conta a mesma historia ({_rampaDoPuxao[0].Servidor:0} -> "
			   + $"{_rampaDoPuxao[^1].Servidor:0} px de mundo)");

		// **E A QUEDA E MONOTONICA**, o que separa "andou" de "teleportou e voltou": nenhum quadro
		// afasta os dois. A tolerancia de meio pixel e a da interpolacao do corpo remoto, que persegue o
		// alvo do snapshot e pode ficar um fio atras num quadro.
		int subiu = 0;
		for (int i = 1; i < medidos.Count; i++)
			if (medidos[i].Desenhado > medidos[i - 1].Desenhado + 0.5f) subiu++;
		Conferir(subiu == 0,
				 $"R2 ...e o vao so ENCURTA, quadro a quadro ({subiu} quadro(s) afastaram) -- "
			   + "andar e diferente de piscar de um lugar pro outro");

		// A RAMPA INTEIRA, e nao dois pontos: dois pontos nao distinguem "andou" de "teleportou".
		var passos = new List<string>();
		foreach ((double t, float des, double srv, bool cena) in _rampaDoPuxao)
			passos.Add($"{t:0.00}s:{(des < 0 ? "fora" : $"{des:0}")}/{srv:0}{(cena ? "*" : "")}");
		Nota("R2 a rampa (tempo : desenhado / servidor, px de MUNDO, * = ja em cena): "
		   + string.Join("  ", passos));
	}

	/// <summary>
	/// R3 (a borda) -- **quem nao chega NAO funde, e nem pela metade**.
	///
	/// O corte que o original nao tem: o `while` do `Potara_Fusion.dm:124` gira pra sempre com o input
	/// dos dois desligado. Aqui o nocaute e um dos quatro cortes do <c>TickDoPuxaoDeFusao</c>, e o que a
	/// bancada cobra depois dele sao as quatro coisas que "nem pela metade" quer dizer: sem cena, sem
	/// fusao, **sem a recarga de 1 h cobrada** e **sem ninguem preso**.
	/// </summary>
	private void ABordaDoPuxao(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		switch (_sub)
		{
			case 0:
				srv.ZerarRecargaNaFotoDeFusao(cli.LocalId, _outro);
				Conferir(srv.PlantarNoCorredorNaFotoDeFusao(cli.LocalId, _outro, TilesDoPuxao),
						 "R3(borda) os dois de volta ao corredor livre");
				Sub(1);
				return;

			case 1:
				if (_t < EsperaDeAssentar) return;
				Conferir(srv.ConvidarNaFotoDeFusao(cli.LocalId, _outro, TipoDeFusao.Potara).Entrou
						 && srv.AceitarNaFotoDeFusao(_outro),
						 "R3(borda) o convite fechou e o puxao comecou de novo");
				Sub(2);
				return;

			case 2:
			{
				// TRES QUADROS DE PUXAO ANTES DE DERRUBAR: sem isso a borda mediria "um puxao que nunca
				// andou", e o corte pareceria funcionar por nao ter havido nada.
				var e = srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro);
				if (_t < 0.10) return;

				Conferir(e.Puxando && e.DistanciaPx < TilesDoPuxao * ZoneCollision.TileSize,
						 $"R3(borda) o puxao estava correndo e ja tinha fechado terreno "
					   + $"({e.DistanciaPx:0} px de {TilesDoPuxao * ZoneCollision.TileSize})");

				srv.DerrubarNaFotoDeFusao(_outro, caido: true);
				Sub(3);
				return;
			}

			default:
			{
				if (_t < 0.35) return;
				var e = srv.EstadoDaCoreografiaNaFotoDeFusao(cli.LocalId, _outro);

				Conferir(!e.Puxando && !e.EmCena && !e.Fundido,
						 "R3(borda) **um dos dois nao chega (nocaute): a fusao NAO acontece** -- "
					   + "nem puxao, nem cinematica, nem fusao");
				Conferir(e.Recarga == 0,
						 "R3(borda) ...e a recarga de 1 h NAO foi cobrada (ela e o preco de uma fusao "
					   + "que ACONTECEU -- compare com a R3 de cima, onde ela foi)");
				Conferir(!e.SemRedeas,
						 "R3(borda) ...e ninguem ficou travado sem as redeas do proprio corpo");

				TomarDaCoreografia(mundo, cli, "fusao-r3-borda-nao-funde",
					"R3 a borda: o passageiro cai no meio do puxao e a fusao nao acontece");

				srv.DerrubarNaFotoDeFusao(_outro, caido: false);
				Virar(PVoltarAoPalco);
				return;
			}
		}
	}

	/// <summary>
	/// O PALCO DE 4 TILES DE VOLTA -- daqui em diante e a bancada da fase 1, intacta: o clarao, a
	/// contagem de discos e as roupas das duas fusoes. Ver <see cref="OPalcoDaFoto"/> sobre por que a
	/// contagem precisa dos 4 tiles e nao dos <see cref="Fusao.TilesColados"/> do jogo.
	/// </summary>
	private void VoltarAoPalco(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_sub == 0)
		{
			srv.ZerarRecargaNaFotoDeFusao(cli.LocalId, _outro);
			srv.PorPertoNaFotoDeFusao(_outro, cli.LocalId, OPalcoDaFoto);
			Sub(1);
			return;
		}

		if (_t < EsperaDeAssentar || mundo.CorpoDeTeste(_outro) == null) return;
		Virar(PMetaCena);
	}

	/// <summary>
	/// UMA FOTO DA COREOGRAFIA -- recorte largo, centrado no MEIO dos dois, escala 1.
	///
	/// Largo porque estas fotos afirmam coisas sobre a DISTANCIA entre dois bonecos, e um recorte que
	/// nao coubesse os dois nao afirmaria nada; escala 1 porque elas entram na tira da sequencia, e o
	/// <see cref="Montar"/> so cola imagens do mesmo tamanho.
	/// </summary>
	private void TomarDaCoreografia(World mundo, GameClient cli, string nome, string rotulo)
	{
		Vector2 meu = mundo.PosicaoDesenhadaDe(cli.LocalId) is { } a ? ParaTela(a) : Meio();
		Vector2 dele = mundo.PosicaoDesenhadaDe(_outro) is { } b ? ParaTela(b) : meu;

		Tomar(new Mira
			  {
				  Nome = nome, Rotulo = rotulo, Meu = meu, Dele = dele,
				  DeleDesenhado = mundo.PosicaoDesenhadaDe(_outro) != null,
				  Zoom = Zoom(), Quando = _t,
			  },
			  (meu + dele) * 0.5f, lado: 0, escala: 1);
	}

	// =====================================================================
	// 2 e 4) A CENA -- as cinco tomadas, as mesmas nos dois tipos
	// =====================================================================
	/// <summary>
	/// UM METODO PROS DOIS TIPOS. A cinematica e a MESMA (`Cinematicas.Fusao`); o que difere e a roupa
	/// e o cabelo que aparecem no fim -- e e por isso que a tira final e "lado a lado".
	///
	/// As tomadas da cena so saem na primeira passada: sao a mesma cena, e dois arquivos iguais nao
	/// provam nada a mais. O que a segunda passada acrescenta e a foto da fusao PRONTA.
	///
	/// ============================ O OBTURADOR DISPARA POR **FATO**, E NAO POR RELOGIO ============================
	/// A versao anterior clicava em `beat + 0,30 s` contados do momento em que o SERVIDOR comecou a
	/// cena. A primeira rodada com janela mostrou o preco: a foto do "auge do ultimo estouro" saiu no
	/// quadro 4 do estouro do MEIO. Entre o `ComecarACenaDaFusao` e o `_Ready` desta cena no cliente
	/// passam ~0,35 s de rede, e nenhum relogio de bancada adivinha isso.
	///
	/// Agora cada clique espera pelo ESTADO da cena viva -- quantos estouros ja cairam, se a luz esta
	/// acesa, em que quadro da folha ela esta, quanto de branco o shader tem. O relogio continua
	/// existindo, mas so como PACIENCIA (ver <see cref="TempoDeSobra"/>): bancada travada tem que
	/// morrer falando.
	/// ========================================================================================================
	/// </summary>
	private void ACena(Jandirus.Server.GameServer srv, GameClient cli, World mundo, TipoDeFusao tipo)
	{
		bool metamoro = tipo == TipoDeFusao.Danca;

		// ---- o disparo, uma vez ----
		if (!_disparei)
		{
			_disparei = true;
			bool foi = srv.ComecarACenaNaFotoDeFusao(cli.LocalId, _outro, tipo);
			Conferir(foi, $"{(metamoro ? "METAMORO" : "POTARA")}: a cinematica comecou pelo funil de producao");
			if (!foi) { Fechar(); return; }
			_t = 0;
			return;
		}

		Transformacao? cena = AchaCena();

		// ============================ O CLIQUE E SEMPRE DO QUADRO **ANTERIOR** ============================
		// `GetViewport().GetTexture().GetImage()` devolve o ULTIMO QUADRO JA DESENHADO -- ou seja, o de
		// antes deste `_Process`. Entao o estado da cena e guardado a CADA quadro (o `Instantanea` la
		// embaixo) e o gatilho pergunta pelo instantaneo ANTERIOR, que e o unico que descreve a imagem
		// que existe pra pegar. A regua vai junto -- que quadro da folha, onde os dois corpos estao
		// desenhados, que zoom a camera tem --, porque a camera TREME durante a cena e os sete quadros
		// do `flick` tem 26, 112, 126, 112, 88, 13 e 6 px de lado. Comparar a mancha de um quadro com a
		// regua de outro erra nas duas direcoes, e erra calado.
		//
		// ============================ E A PRIMEIRA VERSAO DISTO **PERDIA A FOTO** ============================
		// Ela armava num quadro e disparava no seguinte, aproveitando so metade dos quadros. O quadro 3
		// do `flick` dura 0,1 s -- seis quadros de tela a 60 fps, tres a 30 --, e na primeira rodada com
		// janela a foto do auge simplesmente NAO SAIU: o log fechou sem ela, sem falha nenhuma, porque
		// "a foto que nao foi tirada" nao tinha quem reprovasse. Com o instantaneo rolando, todo quadro
		// em que a folha esta no 3 e uma chance -- e o `Fechar` agora cobra as cinco tomadas da cena.
		// ==================================================================================================
		//
		// A CENA ACABOU (ou nunca chegou). O `AchaCena` so devolve cena RODANDO -- entao "nula depois de
		// ter havido foto" e o fim dela, e e nesse instante que a fusao pronta se fotografa.
		if (cena == null)
		{
			_anterior = null;
			if (_tireiNasce) { if (!_tireiPronta) AFusaoPronta(srv, cli, mundo, metamoro); return; }
			if (_t > TempoDeSobra)
			{ Conferir(false, "a cena da fusao chegou ao cliente"); Virar(metamoro ? PMetaFim : PPotFim); }
			return;
		}

		float lavagem = mundo.VisualLocalDeTeste?.LavagemDeTeste?.Mistura ?? -1;
		if (metamoro)
		{
			_maxEstouros = Math.Max(_maxEstouros, cena.EstourosDaLuzDeTeste);
			if (cena.QuadroDaLuzDeTeste >= 0) _quadrosVistos.Add(cena.QuadroDaLuzDeTeste);
			if (cena.LuzDaFusaoAcesaDeTeste) _quadrosAcesos.Add(cena.QuadroDaLuzDeTeste);
			_lavagemMaxima = Math.Max(_lavagemMaxima, lavagem);
			OFilmeDaCena(mundo, cena);
		}

		// O GATILHO PERGUNTA PELO QUADRO ANTERIOR -- ver o bloco de cima.
		if (_anterior is { } q && Gatilho(q, out string nome, out string rotulo))
		{
			q.Nome = nome;
			q.Rotulo = rotulo;
			_anterior = null;
			Revelar(mundo, cena, q, metamoro);
			return;
		}

		// ...e este quadro vira o "anterior" do proximo.
		_anterior = Instantanea(mundo, cena, cli, lavagem);

		// ---- C) a fusao PRONTA fica no bloco de cima: ela e fotografada quando a cena SOME ----
		if (_t > TempoDeSobra && !_tireiPronta) AFusaoPronta(srv, cli, mundo, metamoro);
	}

	/// <summary>
	/// QUAL FOTO ESTE QUADRO JA DESENHADO MERECE -- o roteiro inteiro do obturador, num lugar so.
	///
	/// Todos os gatilhos sao FATOS da cena (quantos estouros ja cairam, se a luz esta acesa, em que
	/// quadro da folha ela esta, quanto de branco o shader tem) e nenhum deles e relogio.
	/// </summary>
	private bool Gatilho(Mira q, out string nome, out string rotulo)
	{
		// ============================ AS TRES PRIMEIRAS EXIGEM O IRMAO DESENHADO (`q.DeleDesenhado`) ============================
		// As tres afirmam coisas sobre OS DOIS ("no ponto medio dos dois", "os dois a vista"), e uma
		// afirmacao sobre dois corpos medida com um corpo so nao e afirmacao: e ruido. Isso ja custou uma
		// rodada -- a foto do auge saiu DEPOIS da virada, com o passageiro ja selado, e as duas medidas
		// que dependiam dele reprovaram sem que nada estivesse errado com o desenho. As duas ultimas
		// (A2 e A3) sao do corpo JA FUNDIDO e por isso nao pedem o irmao: ele nao existe mais.
		// ====================================================================================================================

		// ---- A0) a luz NASCENDO: primeiro quadro do PRIMEIRO estouro ----
		// O quadro 0 da folha tem 26 px de lado (medido). No palco desta foto (4 tiles) ele NAO alcanca
		// nenhum dos dois corpos -- e e por isso que esta e a foto em que da pra ver a luz e os dois
		// juntos. (Em jogo os dois chegam colados e o quadro 0 ja os toca; ver `OPalcoDaFoto`.)
		if (!_tireiNasce && q.DeleDesenhado && q is { Estouros: 1, Acesa: true, Quadro: 0 })
			return Sim(out nome, out rotulo, "fusao-a0-nasce", "A0 a luz NASCENDO entre os dois corpos");

		// ---- A1) O AUGE -- a foto que conta os discos ----
		// ============================ HAVIA TRES ESTOUROS AQUI, E ELE ERA O PRIMEIRO ============================
		// O roteiro tinha tres `flick` (0,0 / 2,0 / 2,5) e este obturador escolhia o PRIMEIRO por duas
		// razoes medidas com a janela aberta: (1) o anel de choque chega a tela na MESMA cor do disco
		// sobre a grama (`cadbd2` nos dois), e so o primeiro caia antes de qualquer anel; (2) a
		// defasagem de rede (0,3 a 1,0 s medidos) fazia o ULTIMO atravessar a virada, e numa rodada a
		// foto do "auge" saiu com a fusao ja feita e o passageiro ja selado.
		//
		// Com UM estouro so a escolha deixou de existir -- e as duas razoes continuam valendo pra ele:
		// o primeiro anel da cena e o da PROPRIA virada, e o estouro acaba exatamente nela.
		// =====================================================================================================
		if (_tireiNasce && !_tireiCena && q.DeleDesenhado && q is { Estouros: 1, Acesa: true }
			&& q.Quadro == QuadroDoAuge)
			return Sim(out nome, out rotulo, "fusao-a1-cena", "A1 o auge do estouro -- UM disco");

		// ---- A0b) A TELA LIMPA, assim que o estouro se apaga ----
		// ============================ ELA NAO PEDE MAIS O SEGUNDO CORPO, E ISSO E O PEDIDO ============================
		// Enquanto havia tres estouros, esta foto era o VAO ENTRE ELES -- e ali os dois corpos ainda
		// estavam no mapa, entao o gatilho cobrava `DeleDesenhado`. Com um estouro so, o unico instante
		// sem disco e **a partir da virada** (o `flick` acaba nela, por construcao), e na virada o
		// passageiro ja foi pro selo: exigir o segundo corpo aqui seria esperar por um estado que a
		// propria cena acabou de tornar impossivel -- a bancada penduraria ate a paciencia acabar.
		//
		// O QUE ELA PROVA CONTINUA SENDO O MESMO, e continua sendo o essencial: **a folha nao e uma
		// cortina**. Depois do estouro nao ha uma unica cor de disco na tela, e e por isso que da pra
		// ver a fusao que acabou de nascer.
		// ==========================================================================================================
		// ============================ E ELA ESPERA O CLARAO DE TELA ESCOAR, PELA MESMA RAZAO DAS TRES DO BRANCO ============================
		// O beat da virada apaga o `flick` E acende o `ColorRect` de tela cheia, no MESMO instante.
		// Medido: com o clarao no pico a varredura acha 74 mil pixels "da cor do disco" espalhados pelo
		// chao -- que e o clarao pintando o chao da cor do clarao, e nao um disco. A pergunta desta foto
		// e sobre a FOLHA (*"ela nao e uma cortina"*), e a folha ja acabou; esperar os 0,45 s do clarao
		// nao afrouxa nada, e as tres fotos do branco repetem a mesma medida mais tarde na cauda.
		// ==================================================================================================================================
		if (_tireiCena && !_tireiLimpo && q is { Estouros: >= 1, Acesa: false }
			&& q.Clarao < ClaraoJaEscoou)
			return Sim(out nome, out rotulo, "fusao-a0b-janela-limpa",
					   "A0b a tela SEM disco nenhum, assim que o estouro acaba");

		// ============================ A2 / A2b / A3 -- **TRES** INSTANTES DO BRANCO, E ELES SAO A R6 ============================
		// *"meça a lavagem branca em tres instantes e mostre que ela cai. Um valor so nao prova
		// escoamento"*. Eram dois; o do meio entrou agora, e os tres viram uma CURVA medida no PIXEL
		// (ver `OBrancoNoPixel`) e nao so tres leituras do uniform do shader.
		//
		// E os tres tambem sao a prova mais longa da R5: os tres saem DEPOIS de a folha ter apagado, e
		// os tres medem "nao ha disco nenhum aqui" -- o ultimo deles a mais de dois ciclos da folha do
		// fim dela. Um laco teria voltado a pintar em algum dos tres.
		// =====================================================================================================================
		// ============================ E AS TRES ESPERAM O **CLARAO DE TELA** ESCOAR ============================
		// O beat da virada acende um `ColorRect` de tela cheia na cor da AURA lerpada pra branco
		// (`Transformacao.Clarao`), e a fusao **nao tem forma**: a cor cai no `?? Cinematicas.CorDaFuria`,
		// ou seja VERMELHO. Medir "o corpo esta branco" com meia tela de vermelho por cima mede o
		// clarao e nao a lavagem -- e foi exatamente o que aconteceu: a A2 saiu com a lavagem em 0,99 e
		// **0,0% de branco no pixel**, com o boneco rosa sobre um chao verde-oliva.
		//
		// O clarao dura 0,45 s em expo-out e a lavagem 3,0 s, entao esperar por ele nao custa o climax:
		// quando a tela limpa, a lavagem ainda esta acima de 0,80. E a espera e por FATO (`q.Clarao`
		// lido do node) e nao por prazo, como todo o resto deste obturador.
		// ==================================================================================================
		if (_tireiLimpo && !_tireiBranco && q.Clarao < ClaraoJaEscoou && q.Lavagem > 0.80f)
			return Sim(out nome, out rotulo, "fusao-a2-branco", "A2 o corpo branco, so a silhueta");

		if (_tireiBranco && !_tireiBrancoMeio && q.Clarao < ClaraoJaEscoou
			&& q.Lavagem is > 0.48f and < 0.62f)
			return Sim(out nome, out rotulo, "fusao-a2b-branco-meio",
					   "A2b o branco na metade do escoamento");

		// ---- A3) O BRANCO ESCOANDO, com a poeira ja baixando ----
		// O beat da virada solta anel de choque e tremor junto com o branco, e a terra que eles
		// levantam desenha POR CIMA do boneco (`Decalques.cs:433`, a fumaca em `ZIndex = 100` -- pedido
		// do dono). Na cauda a poeira ja abriu e o branco ainda esta la. As tres ficam gravadas: **uma
		// prova a INTENSIDADE e a ultima prova a SILHUETA**.
		if (_tireiBrancoMeio && !_tireiBrancoTarde && q.Clarao < ClaraoJaEscoou
			&& q.Lavagem is > 0.12f and < 0.28f)
			return Sim(out nome, out rotulo, "fusao-a3-branco-escoando",
					   "A3 o branco escoando, a silhueta legivel");

		nome = rotulo = "";
		return false;
	}

	/// <summary>
	/// ABAIXO DISTO A TELA ESTA LIMPA DO CLARAO. 2% de alfa num `ColorRect` de tela cheia move um canal
	/// de cor em menos de 6 de 255 -- abaixo da tolerancia de cor com que esta bancada mede qualquer
	/// coisa (<see cref="ContagemDeDiscos.ToleranciaDeCor"/>, 0,13). Ver o bloco no <see cref="Gatilho"/>.
	/// </summary>
	private const float ClaraoJaEscoou = 0.02f;

	private static bool Sim(out string nome, out string rotulo, string n, string r)
	{
		nome = n; rotulo = r;
		return true;
	}

	// =====================================================================
	// R5) O FILME DA CENA -- e ele e quem responde "a animacao toca UMA vez"
	// =====================================================================
	/// <summary>
	/// ============================ UMA FOTO NAO PROVA "NAO REINICIOU" ============================
	/// A A0b mostra a tela sem disco no instante em que o estouro acaba, e isso e uma prova pontual: um
	/// laco que recomecasse meio segundo depois passaria por baixo dela sem encostar. *"Prove que ela
	/// nao reinicia (conte os quadros ou observe o ciclo por mais tempo que a duracao dela)"* -- entao a
	/// bancada OBSERVA: um registro por quadro da cena inteira (3,7 s contra os 0,7 s da folha, ou seja
	/// mais de CINCO ciclos de sobra).
	///
	/// O que fica gravado sao as quatro coisas que separam "tocou uma vez" de tudo o mais:
	///
	///   * quantas vezes a luz ACENDEU (transicao apagada -> acesa). O `flick` do BYOND toca uma vez e
	///     volta ao `icon_state` vazio, e o roteiro tem UM beat: tem que dar 1;
	///   * se o numero do quadro da folha alguma vez ANDOU PRA TRAS enquanto acesa (um laco reinicia em
	///     0 sem apagar, e nesse caminho o contador de acendimentos ficaria em 1);
	///   * o instante em que ela se apagou;
	///   * o instante em que o corpo deixou de ser DOIS -- que e a virada, e o amarre da R5.
	/// ==========================================================================================
	/// </summary>
	private void OFilmeDaCena(World mundo, Transformacao cena)
	{
		bool acesa = cena.LuzDaFusaoAcesaDeTeste;
		int quadro = cena.QuadroDaLuzDeTeste;

		if (acesa && !_luzEstavaAcesa) _acendimentos++;
		if (acesa && _luzEstavaAcesa && quadro >= 0 && quadro < _ultimoQuadroAceso) _quadroVoltouAtras++;
		if (!acesa && _luzEstavaAcesa && _tLuzApagou < 0) _tLuzApagou = _t;
		if (acesa) _ultimoQuadroAceso = quadro;
		_luzEstavaAcesa = acesa;

		// ---- O AMARRE: o corpo ainda e DOIS, ou ja e UM? ----
		// A pergunta e feita ao MUNDO DESENHADO e nao ao servidor: o passageiro sai da zona na virada, e
		// "ele ainda esta na minha tela" e exatamente o que o dono ve. O bit da fusao vem junto porque
		// os dois tem que virar no mesmo instante -- um corpo que sumisse sem o outro virar fusao seria
		// um passageiro perdido, e nao uma virada.
		bool irmaoNaTela = mundo.CorpoDeTeste(_outro) != null;
		bool souFusao = mundo.VisualLocalDeTeste?.EhFusaoDeTeste == true;

		// ============================ O MARCO DO "AINDA E DOIS" E O **AUGE**, E NAO O ULTIMO QUADRO ============================
		// Era o ultimo quadro com a luz acesa, e ele perdeu por UM QUADRO: numa rodada o irmao saiu da
		// tela em 0,72 s e a luz apagou em 0,73 -- dois relogios diferentes (a remocao vem num snapshot
		// do servidor, o fim da folha e contado pelo cliente) que so podiam empatar por sorte. A prova
		// virava uma corrida de um decimo e nao dizia nada sobre o desenho.
		//
		// O auge (`QuadroDoAuge`, o quadro mais cheio da folha) e um instante NOMEADO no meio da
		// animacao, e ele responde a mesma pergunta com folga: com o clarao no pico, ainda havia dois
		// corpos na tela. O "depois do fim ja e um" continua sendo o amarre de tempo, logo abaixo.
		// ==================================================================================================================
		if (acesa && quadro == QuadroDoAuge && irmaoNaTela) _irmaoNoAuge = true;
		if (!irmaoNaTela && _tIrmaoSumiu < 0 && _acendimentos > 0) _tIrmaoSumiu = _t;
		if (souFusao && _tVirouFusao < 0) _tVirouFusao = _t;
	}

	private bool _luzEstavaAcesa;
	private int _acendimentos, _quadroVoltouAtras, _ultimoQuadroAceso = -1;
	private double _tLuzApagou = -1, _tIrmaoSumiu = -1, _tVirouFusao = -1;
	private bool _irmaoNoAuge;

	/// <summary>
	/// QUANTO O AMARRE DA R5 PODE ESCORREGAR, em segundos.
	///
	/// Nao e folga de gosto: o instante da virada e decidido pelo SERVIDOR (`if (agora >= c.Funde)`) e
	/// o instante em que a luz apaga e contado pelo CLIENTE (a duracao da folha). Entre um e outro ha
	/// uma volta de rede e um tique de 30 Hz. Meio segundo cobre isso com sobra e continua tendo dente
	/// contra o defeito que ele existe pra pegar: um prazo fixo escrito a mao no lugar do fim da
	/// animacao erra por SEGUNDOS, e nao por decimos.
	/// </summary>
	private const double FolgaDoAmarre = 0.5;

	/// <summary>
	/// O QUE O FILME DA CENA CONTOU -- lido uma vez, no fim da passada da Metamoro.
	/// </summary>
	private void ARegraDaAnimacao()
	{
		Nota($"R5 o filme da cena: {_acendimentos} acendimento(s), quadro voltou atras "
		   + $"{_quadroVoltouAtras}x, luz apagou em {_tLuzApagou:0.00}s, irmao sumiu da tela em "
		   + $"{_tIrmaoSumiu:0.00}s, virei fusao em {_tVirouFusao:0.00}s "
		   + $"(a folha dura {Jandirus.Core.Forms.Cinematicas.SegundosDaLuzDaFusao:0.00}s e a cena "
		   + $"{Jandirus.Core.Forms.Cinematicas.Fusao.SegundosPreso:0.0}s)");

		Conferir(_acendimentos == 1,
				 $"R5 **a animacao acende UMA VEZ** na cena inteira ({_acendimentos} acendimento(s) em "
			   + $"{Jandirus.Core.Forms.Cinematicas.Fusao.SegundosPreso:0.0} s -- cabem "
			   + $"{Jandirus.Core.Forms.Cinematicas.Fusao.SegundosPreso / Jandirus.Core.Forms.Cinematicas.SegundosDaLuzDaFusao:0.#} ciclos da folha)");

		Conferir(_quadroVoltouAtras == 0,
				 $"R5 ...e o quadro da folha nunca voltou atras enquanto ela estava acesa "
			   + $"({_quadroVoltouAtras} vez(es)) -- um laco reinicia sem apagar, e este e o caminho "
			   + "que o contador de acendimentos nao pegaria");

		Conferir(_tLuzApagou > 0, $"R5 ...e ela APAGOU antes do fim da cena ({_tLuzApagou:0.00} s)");

		// ---- O AMARRE, AS DUAS METADES ----
		Conferir(_irmaoNoAuge,
				 $"R5 **antes do fim da animacao o corpo ainda e DOIS** -- no quadro {QuadroDoAuge} da "
			   + "folha (o auge do clarao) o segundo corpo ainda estava desenhado na tela");

		Conferir(_tIrmaoSumiu > Jandirus.Core.Forms.Cinematicas.SegundosDaLuzDaFusao * 0.9,
				 $"R5 ...e ele SO saiu depois de a animacao ter praticamente acabado ({_tIrmaoSumiu:0.00} s "
			   + $"contra os {Jandirus.Core.Forms.Cinematicas.SegundosDaLuzDaFusao:0.00} s da folha) -- "
			   + "uma virada adiantada apareceria aqui, e nao no amarre de tempo");

		Conferir(_tIrmaoSumiu > 0 && _tLuzApagou > 0
				 && Math.Abs(_tIrmaoSumiu - _tLuzApagou) < FolgaDoAmarre,
				 $"R5 **e depois do fim ja e UM**: o segundo corpo saiu da tela {_tIrmaoSumiu - _tLuzApagou:+0.00;-0.00} s "
			   + $"do fim da animacao (folga de {FolgaDoAmarre:0.0} s -- a virada e do servidor e o fim "
			   + "da folha e do cliente, ha uma volta de rede entre os dois)");

		Conferir(_tVirouFusao > 0 && _tLuzApagou > 0
				 && Math.Abs(_tVirouFusao - _tLuzApagou) < FolgaDoAmarre,
				 $"R5 ...e o corpo que ficou virou FUSAO no mesmo instante ({_tVirouFusao - _tLuzApagou:+0.00;-0.00} s "
			   + "do fim da animacao) -- os dois nao viraram um sumico e um nascimento separados");
	}

	/// <summary>
	/// QUANTO A BANCADA ESPERA POR UMA CENA antes de desistir dela -- a duracao do roteiro mais a folga
	/// que o proprio tocador ja usa como teto. Nao e o relogio do obturador: e a hora de morrer falando.
	/// </summary>
	private static double TempoDeSobra =>
		Jandirus.Core.Forms.Cinematicas.Fusao.SegundosPreso + Transformacao.FolgaDoTeto + 3.0;

	private bool _disparei;

	/// <summary>O que a cena mostrou enquanto rodava -- so pra a bancada saber falar quando erra.</summary>
	private int _maxEstouros;
	private float _lavagemMaxima = -1;
	private readonly SortedSet<int> _quadrosVistos = [], _quadrosAcesos = [];

	private void AFusaoPronta(Jandirus.Server.GameServer srv, GameClient cli, World mundo, bool metamoro)
	{
		_tireiPronta = true;
		if (metamoro)
		{
			Nota($"a cena mostrou {_maxEstouros} estouro(s) de {EstourosDoRoteiro}; quadros vistos "
			   + $"[{string.Join(",", _quadrosVistos)}], acesos [{string.Join(",", _quadrosAcesos)}]; "
			   + $"lavagem maxima {_lavagemMaxima:0.##}");
			ARegraDaAnimacao();
		}
		var e = srv.EstadoNaFotoDeFusao(cli.LocalId);

		Conferir(e.Fundido, $"{(metamoro ? "METAMORO" : "POTARA")}: a fusao existe no fim da cena");
		Nota($"{(metamoro ? "metamoro" : "potara")}: nome '{e.Nome}', cabelo '{e.Cabelo}', "
		   + $"roupa [{string.Join(", ", e.Roupa.Select(NomeCurto))}]");

		Conferir(e.Cabelo == Fusao.EstiloDoVegito,
				 $"...e o cabelo e o do Vegito (Goku + Vegeta) -- deu '{e.Cabelo}'");

		Vector2 onde = mundo.PosicaoDesenhadaDe(cli.LocalId) is { } p ? ParaTela(p) : Meio();
		if (metamoro)
		{
			Conferir(e.Roupa.Length == 1 && e.Roupa[0].Contains("Metamoran", StringComparison.OrdinalIgnoreCase),
					 "...e a roupa e SO o colete metamoriano (ela SUBSTITUI o guarda-roupa)");
			Retrato("fusao-b1-metamoro", "B1 a METAMORO pronta", onde);
		}
		else
		{
			Conferir(e.Roupa.Any(r => r.Contains("potara", StringComparison.OrdinalIgnoreCase)),
					 "...e o brinco Potara esta vestido");
			Conferir(e.Roupa.Length > 1,
					 $"...e a roupa de quem convidou veio JUNTO (a peca SOMA) -- {e.Roupa.Length} camadas");
			Retrato("fusao-b2-potara", "B2 a POTARA pronta", onde);
		}

		Virar(metamoro ? PMetaFim : PPotFim);
	}

	// =====================================================================
	// O OBTURADOR DE DOIS TEMPOS
	// =====================================================================
	/// <summary>
	/// O QUE FOI ARMADO NUM QUADRO PRA SER FOTOGRAFADO NO SEGUINTE. Ver o bloco no <see cref="ACena"/>.
	/// Tudo aqui e do quadro que a foto vai mostrar -- inclusive as posicoes, porque a camera TREME.
	/// </summary>
	private sealed class Mira
	{
		public string Nome = "", Rotulo = "";
		public Vector2 Meu, Dele, Luz;
		public bool DeleDesenhado;
		public (Vector2 Tamanho, double Area)? Disco;
		public int Quadro = -1, Estouros, Luzes, Pedras;
		public bool Acesa;
		public float Lavagem = -1, Clarao;
		public float Zoom = 1;
		public double Quando;
	}

	/// <summary>O estado do quadro que **ja foi desenhado** -- ver o bloco no <see cref="ACena"/>.</summary>
	private Mira? _anterior;

	private Mira Instantanea(World mundo, Transformacao cena, GameClient cli, float lavagem)
	{
		Vector2? dele = mundo.PosicaoDesenhadaDe(_outro);
		return new Mira
		{
			Meu = mundo.PosicaoDesenhadaDe(cli.LocalId) is { } a ? ParaTela(a) : Vector2.Zero,
			Dele = dele is { } b ? ParaTela(b) : Vector2.Zero,
			DeleDesenhado = dele != null,
			Luz = cena.PosDaLuzDaFusaoDeTeste is { } l ? ParaTela(l) : Vector2.Zero,
			Disco = cena.CaixaDoDiscoDesenhadoDeTeste,
			Quadro = cena.QuadroDaLuzDeTeste,
			Estouros = cena.EstourosDaLuzDeTeste,
			Luzes = cena.LuzesDaFusaoDeTeste,
			Pedras = cena.PedrasVivasDeTeste,
			Acesa = cena.LuzDaFusaoAcesaDeTeste,
			Lavagem = lavagem,
			Clarao = cena.ClaraoNaTelaDeTeste,
			Zoom = Zoom(),
			Quando = _t,
		};
	}

	/// <summary>
	/// ============================ O OBTURADOR SO **PEGA** A IMAGEM -- MEDIR E GRAVAR FICAM PRA DEPOIS ============================
	/// A primeira versao media e gravava aqui dentro, e isso quebrou a propria bancada: dois `SavePng`
	/// de 1920x1080 mais a varredura de uma regiao de 768x768 congelam o cliente por uns tres decimos --
	/// e o quadro 3 do `flick` dura UM decimo. Ou seja, tirar a foto A0 fazia perder a A1, que esta 0,3 s
	/// depois. O sintoma era o pior possivel: a foto seguinte simplesmente nao existia, e nada reprovava.
	///
	/// Agora o caminho quente e so `GetImage()` (uma leitura de textura). A analise no pixel e a gravacao
	/// dos PNG acontecem no <see cref="Fechar"/>, quando ja nao ha cena nenhuma pra atrapalhar. As
	/// afirmacoes que leem CAMPO VIVO (a lavagem do shader, quantas luzes a cena criou, quantas pedras
	/// estao no ar) viajam dentro da <see cref="Mira"/>, fotografadas no mesmo quadro que a imagem.
	/// ========================================================================================================================
	/// </summary>
	private void Revelar(World mundo, Transformacao cena, Mira m, bool metamoro)
	{
		switch (m.Nome)
		{
			case "fusao-a0-nasce": _tireiNasce = true; break;
			case "fusao-a0b-janela-limpa": _tireiLimpo = true; break;
			case "fusao-a1-cena": _tireiCena = true; break;
			case "fusao-a2-branco": _tireiBranco = true; break;
			case "fusao-a2b-branco-meio": _tireiBrancoMeio = true; break;
			case "fusao-a3-branco-escoando": _tireiBrancoTarde = true; break;
		}
		if (!metamoro) return;   // a segunda passada e a mesma cena: dois arquivos iguais nao provam nada

		// O QUE SO EXISTE AGORA: o shader do corpo local. Ele nao viaja na `Mira` porque a lavagem tem
		// tres partes (mistura, cor e a pilha de camadas) e duas delas so interessam nestes
		// instantes -- carregar tudo em toda `Mira` seria fotografar o mundo inteiro a 60 por segundo.
		if (m.Nome is "fusao-a2-branco") OBranco(mundo, forte: true);
		if (m.Nome is "fusao-a3-branco-escoando") OBranco(mundo, forte: false);

		// AS TRES DO BRANCO SAO RETRATO, e nao paisagem: la ja nao ha dois corpos nem disco pra
		// enquadrar -- ha UM boneco lavado de branco, e num recorte de cena ele sai com 30 px de altura
		// no meio de um descampado. O que essas fotos afirmam e a SILHUETA, e silhueta se ve de
		// perto. (Elas ganham tira propria por isso: `Montar` cola imagens do mesmo tamanho.)
		bool branco = m.Nome is "fusao-a2-branco" or "fusao-a2b-branco-meio" or "fusao-a3-branco-escoando";
		Tomar(m, branco ? m.Meu : (m.Meu + m.Dele) * 0.5f,
			  branco ? LadoDoRetrato : LadoDaCena, branco ? EscalaDoRetrato : EscalaDaCena);
	}

	/// <summary>
	/// A ANALISE NO PIXEL DE TUDO O QUE FOI FOTOGRAFADO -- rodada no fim, com o mundo ja parado.
	/// A ordem e a das tomadas, pra o log continuar contando a cena na ordem em que ela aconteceu.
	/// </summary>
	private void MedirAsFotos()
	{
		// ============================ A A1 MEDE PRIMEIRO, E ISSO E ORDEM E NAO ESTILO ============================
		// Ela e quem deixa a REGUA DE BRANCO (`_corDoDisco`) pra as medidas que afirmam a AUSENCIA do
		// clarao (a janela limpa, as duas fotos da coreografia) e pra a curva do escoamento (R6).
		//
		// **E a regua sai do AUGE, e nao do nascimento.** Ela saia da A0, onde a folha esta no quadro 0 --
		// 26 px de arte, 104 px de tela -- e a amostra e uma caixinha de 9 px no ponto em que a cena diz
		// que a luz esta. Numa rodada isso devolveu `79524d`, um marrom: um quadro de defasagem entre a
		// posicao lida e a imagem pega ja poe a caixinha fora de um disco desse tamanho. Com a regua
		// errada TUDO o que depende dela vira ruido -- e as duas provas que cairam naquela rodada
		// (a janela limpa e o escoamento) reprovaram sem que nada estivesse errado com o jogo.
		//
		// No auge o disco tem 448 px de tela e o miolo solido atravessa a folha inteira: ali a caixinha
		// nao tem como cair fora. E a ordem tem que ser explicita porque as fotos da coreografia entram
		// na lista ANTES dela -- uma dependencia que so funciona por ordem de insercao quebra calada.
		// ====================================================================================================
		foreach (Tomada t in _tomadas)
			if (t is { Nome: "fusao-a1-cena", Quadro: { } tela1, Mira: { } m1 }) OAuge(m1, tela1);

		foreach (Tomada t in _tomadas)
		{
			if (t.Quadro is not { } tela || t.Mira is not { } m) continue;
			switch (t.Nome)
			{
				case "fusao-a0-nasce": ONascimento(m, tela); break;
				case "fusao-a0b-janela-limpa": AJanelaLimpa(m, tela); break;
				case "fusao-r1-longe-recusada": SemDiscoNenhum(m, tela, "R1(longe)"); break;
				case "fusao-r3-borda-nao-funde": SemDiscoNenhum(m, tela, "R3(borda)"); break;
				case "fusao-a2-branco": SemDiscoNenhum(m, tela, "R5(a2)"); break;
				case "fusao-a2b-branco-meio": SemDiscoNenhum(m, tela, "R5(a2b)"); break;
				case "fusao-a3-branco-escoando": SemDiscoNenhum(m, tela, "R5(a3)"); break;
			}
		}

		OBrancoNoPixel();
		OsDoisSaoLegiveis();
	}

	// =====================================================================
	// R6) O BRANCO **NO PIXEL** -- tres instantes, e a curva tem que cair
	// =====================================================================
	/// <summary>
	/// ============================ O UNIFORM DIZ O QUE FOI PEDIDO; A FOTO DIZ O QUE FOI PINTADO ============================
	/// O <see cref="OBranco"/> le `CharacterVisual.LavagemDeTeste`, que e o parametro do shader. A
	/// memoria deste projeto tem um verbete inteiro pra esse cego (*"a bancada mede INTENCAO: uniform
	/// escrito nao e pixel desenhado"*), e a fase 2 pediu justamente a metade que falta.
	///
	/// ============================ E A REGUA E A COR DO DISCO, MEDIDA NESTA MESMA RODADA ============================
	/// "Branco" nao pode ser um `luz > 0,9` escrito aqui: a tela chega multiplicada pelo `CanvasModulate`
	/// da hora (medido: o `f8f8f8` da folha vira `d6d6cc` ao meio-dia). A cor de referencia e a
	/// <see cref="_corDoDisco"/>, amostrada na foto A0 do MESMO minuto, do MESMO planeta e com o MESMO
	/// ceu -- ou seja o branco desta cena, e nao um branco de catalogo.
	///
	/// **A conta e conservadora de proposito**: a fumaca da cratera desenha POR CIMA do boneco
	/// (`Decalques.cs:433`, `ZIndex = 100`, pedido do dono) e ela e MARROM -- ou seja ela so pode
	/// DERRUBAR a brancura medida, nunca inflar. Uma curva que cai medida assim cai de verdade.
	/// ==========================================================================================================
	/// </summary>
	private void OBrancoNoPixel()
	{
		if (_corDoDisco is not { } branco)
		{ Conferir(false, "R6: sem a cor de branco medida na A0 nao ha regua pra medir o escoamento"); return; }

		var curva = new List<(string Nome, float Brancura)>();
		string[] osTresEOPronto =
		[
			"fusao-a2-branco", "fusao-a2b-branco-meio", "fusao-a3-branco-escoando", "fusao-b1-metamoro",
		];
		foreach (string nome in osTresEOPronto)
		{
			if (Achar(nome) is not { Quadro: { } tela, Mira: { } m }) continue;
			Rect2I caixa = CaixaDoBoneco(m, tela);
			curva.Add((nome, ContagemDeDiscos.CoberturaDe(
				ContagemDeDiscos.Mascara(tela, caixa, branco, ContagemDeDiscos.ToleranciaDeCor),
				caixa.Size, caixa.Position, caixa)));
		}

		Nota("R6 a brancura MEDIDA NO PIXEL (fracao da caixa do boneco na cor de branco da A0, "
		   + $"`{branco.ToHtml(false)}`): "
		   + string.Join("  ", curva.ConvertAll(c => $"{c.Nome[6..]} {c.Brancura:P1}"))
		   + "; lavagem do shader e clarao de tela em cada uma: "
		   + string.Join("  ", osTresEOPronto.Select(
				n => Achar(n)?.Mira is { } mm ? $"{n[6..]} lav {mm.Lavagem:0.00}/clarao {mm.Clarao:0.00}" : "")
			   .Where(s => s.Length > 0)));

		if (curva.Count < 3)
		{ Conferir(false, $"R6 ha tres instantes do branco pra medir (achei {curva.Count})"); return; }

		Conferir(curva[0].Brancura > curva[1].Brancura && curva[1].Brancura > curva[2].Brancura,
				 $"R6 **o branco ESCOA, e escoa no pixel**: {curva[0].Brancura:P1} -> {curva[1].Brancura:P1} "
			   + $"-> {curva[2].Brancura:P1} em tres instantes da mesma cena");

		// ============================ O CONTRA-EXEMPLO E O **CLIMAX** CONTRA A FUSAO PRONTA ============================
		// A fusao pronta e o MESMO boneco, na MESMA caixa, medido com a MESMA regua, e sem lavagem
		// nenhuma. Sem ela, "12,7% e muito" seria uma opiniao.
		//
		// A comparacao e com o CLIMAX e nao com a cauda, e a razao e a propria conta: a brancura mede
		// quantos pixels estao DENTRO da tolerancia do branco, e no fim do escoamento (mistura ~0,3) o
		// boneco ja saiu dela quase todo -- a cauda CONVERGE pro valor da fusao pronta, que e exatamente
		// o que "o branco escoou ate acabar" quer dizer. Pedir que a cauda ainda seja maior que a fusao
		// pronta seria pedir a conta pra resolver o que ela acabou de mostrar que sumiu.
		// ==========================================================================================================
		if (curva.Count >= 4)
			Conferir(curva[0].Brancura > curva[3].Brancura * 4f,
					 $"R6 ...e no climax o MESMO boneco mede {curva[0].Brancura / Math.Max(0.0001f, curva[3].Brancura):0}x "
				   + $"mais branco do que a fusao PRONTA ({curva[0].Brancura:P1} contra {curva[3].Brancura:P1}) "
				   + "-- era a lavagem, e nao o boneco ser claro");
	}

	// =====================================================================
	// 4) OS DOIS SAO LEGIVEIS: eles ANTES, e a fusao branca DEPOIS
	// =====================================================================
	/// <summary>
	/// *"O dono quer ver a fusao acontecer"*. A A0 ja prova que os DOIS estao desenhados antes (ver
	/// <see cref="OsDoisAparecem"/>); esta prova o outro lado -- **depois do clarao ha um corpo
	/// desenhado ali**, e nao um descampado.
	///
	/// A regua e a mesma da A0 e a mesma do resto desta bancada: a cor do chao medida na PROPRIA foto,
	/// e "ha corpo" e a caixa do boneco ter muito mais pixel fora dessa cor do que uma mancha qualquer
	/// da mesma cena.
	/// </summary>
	private void OsDoisSaoLegiveis()
	{
		foreach ((string nome, string quando) in
				 new[] { ("fusao-a3-branco-escoando", "a fusao BRANCA, na cauda do escoamento"),
						 ("fusao-b1-metamoro", "a fusao PRONTA, depois de tudo") })
		{
			if (Achar(nome) is not { Quadro: { } tela, Mira: { } m }) continue;

			Rect2I caixa = CaixaDoBoneco(m, tela);
			Rect2I regiao = ContagemDeDiscos.Dentro(
				ContagemDeDiscos.CaixaEmVolta(m.Meu, LadoDoCorpo(m) * 6), tela);
			Color chao = ContagemDeDiscos.CorMediana(tela, regiao);

			float conteudo = ContagemDeDiscos.ConteudoDe(tela, caixa, chao, ToleranciaDoChao);
			float tipico = ContagemDeDiscos.ChaoTipico(tela, regiao, LadoDoCorpo(m), chao,
													   ToleranciaDoChao, [caixa]);

			Conferir(conteudo > ConteudoMinimoDeCorpo && conteudo > tipico * 4f,
					 $"4) DEPOIS do clarao ha CORPO desenhado -- {quando}: {conteudo:P1} fora da cor do "
				   + $"chao contra {tipico:P1} numa mancha qualquer (chao {chao.ToHtml(false)})");
		}
	}

	/// <summary>
	/// A CAIXA DO BONECO NESTA FOTO -- e ela e um retangulo em pe, e nao um quadrado.
	///
	/// O sprite tem 32x48 e a posicao que o mundo devolve e a dos PES; um quadrado centrado ali corta o
	/// cabelo e engole meio tile de grama embaixo. 1,5 tile de largura por 2,0 de altura, deslocado meia
	/// altura pra cima, e o que envolve o boneco sem sobra -- e sobra e o que dilui as duas medidas que
	/// dependem desta caixa (a brancura e o "ha corpo aqui").
	/// </summary>
	private static Rect2I CaixaDoBoneco(Mira m, Image tela)
	{
		int larg = (int)(1.5f * ZoneCollision.TileSize * m.Zoom);
		int alt = (int)(2.0f * ZoneCollision.TileSize * m.Zoom);
		return ContagemDeDiscos.Dentro(
			new Rect2I((int)m.Meu.X - larg / 2, (int)m.Meu.Y - (int)(alt * 0.80f), larg, alt), tela);
	}

	/// <summary>
	/// **NAO HA DISCO NENHUM NESTA FOTO** -- a mesma conta da <see cref="AJanelaLimpa"/>, usada pelas
	/// fotos da coreografia.
	///
	/// Ela e o que faz "a Metamoro foi recusada" e "a fusao nao aconteceu" serem afirmacoes sobre a TELA
	/// e nao sobre um campo: recusado o convite, o jogador nao ve clarao nenhum.
	/// </summary>
	private void SemDiscoNenhum(Mira m, Image tela, string tomada)
	{
		if (_corDoDisco is not { } cor)
		{ Conferir(false, $"{tomada}: sem a cor de disco da A0 nao ha como afirmar a ausencia dela"); return; }

		Rect2I regiao = RecorteDaTira((m.Meu + m.Dele) * 0.5f, tela);
		bool[] mascara = ContagemDeDiscos.Mascara(tela, regiao, cor, ContagemDeDiscos.ToleranciaDeCor);

		// O PISO E O DA FOLHA NO SEU MENOR QUADRO UTIL: 20% do quadro 0 (26 px de lado na escala 1, ou
		// seja ~2.700 px de tela no zoom 2). Abaixo disso nao ha disco, ha respingo de cor parecida.
		List<ContagemDeDiscos.Mancha> manchas =
			ContagemDeDiscos.Manchas(mascara, regiao.Size, regiao.Position, PisoDeManchaSemCena(m));

		Conferir(manchas.Count == 0,
				 $"{tomada} **nao ha clarao nenhum na tela** ({manchas.Count} mancha(s) da cor do disco "
			   + $"em {regiao.Size.X}x{regiao.Size.Y})");
	}

	/// <summary>
	/// O PISO DE MANCHA QUANDO NAO HA CENA -- e por isso ele nao pode sair da `Mira` como o
	/// <see cref="PisoDeMancha"/> faz: sem cena nao ha folha desenhada, e `m.Disco` e nulo.
	///
	/// 20% do QUADRO 0 da folha (o menor quadro que a cena chega a mostrar, 26 px de lado medidos), na
	/// escala 2 do DM e no zoom da camera. Ou seja: o menor clarao que esta cena e capaz de desenhar
	/// ainda seria pego por esta conta.
	/// </summary>
	private static int PisoDeManchaSemCena(Mira m)
	{
		const int LadoDoQuadroZero = 26;
		float lado = LadoDoQuadroZero * Transformacao.EscalaDaLuzDaFusao * m.Zoom;
		return Math.Max(64, (int)(lado * lado * FracaoDoPiso));
	}

	// =====================================================================
	// AS MEDIDAS NO PIXEL
	// =====================================================================
	/// <summary>
	/// A COR DE DISCO QUE ESTA FOTO MEDIU -- guardada pra a foto da janela limpa poder afirmar a
	/// AUSENCIA dela. Sem uma cor medida, "nao ha disco nenhum aqui" seria uma afirmacao sobre um
	/// limiar inventado.
	/// </summary>
	private Color? _corDoDisco;

	/// <summary>
	/// A0 -- A LUZ NASCE E OS DOIS APARECEM. Aqui a folha esta no quadro 0 (26 px de lado), que no
	/// portao do convite nao alcanca nenhum dos dois corpos: da pra afirmar as duas coisas na MESMA
	/// imagem, que e o que o dono pediu quando disse que quer VER a fusao acontecer.
	/// </summary>
	private void ONascimento(Mira m, Image tela)
	{
		Achado a = Varrer(m, tela);
		if (a.Regiao.Size.X == 0) return;

		Nota($"A0 (a {m.Quando:0.00} s do pedido) cor de disco medida na tela: {a.Cor.ToHtml(false)} "
		   + $"(a folha e `f8f8f8`/`eafeff`/`e0f2fe` -- a diferenca e o `CanvasModulate` da hora)");

		Conferir(m.Luzes == 1, $"A0 a cena criou UMA luz (campo: {m.Luzes})");
		ContarDiscos(a, "A0", esperado: 1);

		// E OS DOIS ESTAO A VISTA -- e "a vista" nao e "nao tapado": e "ha corpo desenhado aqui".
		OsDoisAparecem(a, "A0", tela, m);
	}

	/// <summary>
	/// A0b -- **A TELA LIMPA**, e ela e a prova de que a cena nao e uma cortina.
	///
	/// O defeito que a fase 1 fechou tinha DUAS metades: duas copias da folha, e a folha tocando EM
	/// LACO os 7 segundos (o `flick` do BYOND toca uma vez e volta ao `icon_state` vazio). A segunda
	/// metade so tem prova em imagem: **uma foto da cena sem uma unica cor de disco na tela**.
	///
	/// ELA NAO COBRA MAIS "os dois a vista" -- ver o gatilho: o instante sem disco e a partir da virada,
	/// e ali ja nao ha dois. Quem prova que os dois estavam la e a A0 (a luz nascendo entre eles).
	/// </summary>
	private void AJanelaLimpa(Mira m, Image tela)
	{
		if (_corDoDisco is not { } cor)
		{ Conferir(false, "A0b: sem a cor de disco da A1 nao ha como afirmar a ausencia dela"); return; }

		Rect2I regiao = RegiaoDe(m, tela);
		bool[] mascara = ContagemDeDiscos.Mascara(tela, regiao, cor, ContagemDeDiscos.ToleranciaDeCor);
		int pintados = 0;
		foreach (bool b in mascara) if (b) pintados++;

		// ============================ O PISO NAO PODE SAIR DA `Mira` AQUI ============================
		// Ele saia do `PisoDeMancha(m)`, que e 20% da folha DESENHADA neste quadro -- e neste quadro nao
		// ha folha desenhada nenhuma (o `m.Disco` e nulo, que e justamente o que a foto afirma). O piso
		// desabava pro minimo de 64 px, e ai qualquer clarao de tela ou nuvem clara vira "um disco".
		// O piso certo e o do MENOR quadro que a cena e capaz de desenhar -- ver `PisoDeManchaSemCena`.
		// ==========================================================================================
		List<ContagemDeDiscos.Mancha> manchas =
			ContagemDeDiscos.Manchas(mascara, regiao.Size, regiao.Position, PisoDeManchaSemCena(m));

		Conferir(manchas.Count == 0,
				 $"A0b depois do estouro **nao ha disco nenhum** na tela ({manchas.Count} mancha(s), "
			   + $"{pintados} px da cor do disco em {regiao.Size.X}x{regiao.Size.Y})");

	}

	/// <summary>
	/// A1 -- **O AUGE, E A CONTA QUE O DONO PEDIU**.
	///
	/// Quatro numeros independentes, e os quatro comparados com UMA copia da folha do jeito que ela
	/// esta desenhada neste quadro (`Transformacao.CaixaDoDiscoDesenhadoDeTeste`), nunca com um pixel
	/// escrito aqui.
	/// </summary>
	private void OAuge(Mira m, Image tela)
	{
		Achado a = Varrer(m, tela);
		if (a.Regiao.Size.X == 0) return;

		// ============================ A REGUA DE BRANCO DA RODADA SAI DAQUI ============================
		// Ver o bloco no <see cref="MedirAsFotos"/>: no auge o disco tem 448 px de tela e miolo solido de
		// ponta a ponta, entao a caixinha de amostra nao tem como cair fora dele. E a guarda abaixo e a
		// irma da de luminosidade das fotos: o mundo tem noite, nublado, caverna e sombra de predio, e
		// uma regua que nao for MUITO mais clara que o chao desta mesma foto nao e a cor do clarao --
		// dizer isso e melhor do que medir com ela e gravar numeros sem sentido.
		// ==========================================================================================
		_corDoDisco = a.Cor;
		Color chaoDaA1 = ContagemDeDiscos.CorMediana(tela, a.Regiao);
		float luzDoDisco = Math.Max(a.Cor.R, Math.Max(a.Cor.G, a.Cor.B));
		float luzDoChao = Math.Max(chaoDaA1.R, Math.Max(chaoDaA1.G, chaoDaA1.B));
		Conferir(luzDoDisco > luzDoChao * 1.8f,
				 $"A1 a cor amostrada no meio do clarao e MESMO a dele -- {a.Cor.ToHtml(false)}, "
			   + $"{luzDoDisco / Math.Max(0.001f, luzDoChao):0.0}x mais clara que o chao desta foto "
			   + $"({chaoDaA1.ToHtml(false)}); e ela e a regua de branco do resto da rodada");

		Nota($"A1 tirada a {m.Quando:0.00} s do pedido, no quadro {m.Quadro} do estouro {m.Estouros}");
		Conferir(m.Luzes == 1, $"A1 a cena criou UMA luz (campo: {m.Luzes})");
		Conferir(m.Pedras > 0, $"A1 ...e ha pedra levitando (achei {m.Pedras})");
		ContarDiscos(a, "A1", esperado: 1);

		// ============================ E NO AUGE ELES ESTAO TAPADOS -- DE PROPOSITO ============================
		// A folha tem alfa BINARIO (medido: so 0 e 255 nos 147.456 pixels) e desenha no dobro do tamanho
		// (`_Fusion.dm:121`): onde ela desenha, ela TAPA -- e **tapar e o pedido do dono** (*"aparece
		// entre eles cobrindo ambos"*). O que nao pode e a cortina: o clarao e um ESTOURO de 0,7 s que
		// acaba NA virada, e dali em diante a tela mostra a fusao branca.
		// Esta medida existe pra a escolha ficar ESCRITA NA FOTO, e nao so no comentario: se um dia o
		// disco parar de cobrir, a A1 e quem diz.
		// ==================================================================================================
		float meu = ContagemDeDiscos.CoberturaDe(a.Mascara, a.Regiao.Size, a.Regiao.Position,
												 ContagemDeDiscos.CaixaEmVolta(m.Meu, LadoDoCorpo(m)));
		float dele = ContagemDeDiscos.CoberturaDe(a.Mascara, a.Regiao.Size, a.Regiao.Position,
												  ContagemDeDiscos.CaixaEmVolta(m.Dele, LadoDoCorpo(m)));
		Conferir(meu > 0.85f && dele > 0.85f,
				 $"A1 no auge o disco cobre os DOIS corpos, inteiros ({meu:P0} e {dele:P0}) -- a escala 2 "
			   + $"do DM alcanca os {TilesDoPalcoDaFoto} tiles do palco -- e em jogo eles chegam a "
			   + $"{Fusao.TilesColados}, que e folga de sobra");
	}

	/// <summary>A2/A3 -- o branco, medido no shader e fotografado.</summary>
	private void OBranco(World mundo, bool forte)
	{
		(float Mistura, Color Cor, float Achatar)? lav = mundo.VisualLocalDeTeste?.LavagemDeTeste;
		if (forte)
		{
			Conferir(lav is { Mistura: > 0.85f },
					 $"A2 o corpo esta LAVADO de branco no climax (mistura {lav?.Mistura ?? -1:0.##})");
			Conferir(lav is { } l && l.Cor.R > 0.95f && l.Cor.G > 0.95f && l.Cor.B > 0.95f,
					 $"A2 ...e a cor da lavagem e BRANCA (deu {lav?.Cor.ToString() ?? "nada"})");

			(int Camadas, int Cores, float Menor) pilha =
				mundo.VisualLocalDeTeste?.LavagemDaPilhaDeTeste ?? (0, 0, 0);
			Conferir(pilha.Cores <= 1,
					 $"A2 ...e o boneco INTEIRO lava da MESMA cor ({pilha.Cores} cores em {pilha.Camadas} camadas)");
			return;
		}

		Conferir(lav is { Mistura: > 0.05f and < 0.60f },
				 $"A3 o branco ESCOA com o tempo (mistura {lav?.Mistura ?? -1:0.##} na cauda)");
	}

	/// <summary>O que uma varredura devolve -- a regiao olhada, a mascara e a cor que a calibrou.</summary>
	private sealed class Achado
	{
		public Rect2I Regiao;
		public bool[] Mascara = [];
		public Color Cor;
		public List<ContagemDeDiscos.Mancha> Manchas = [];
		public double AreaEsperada;
		public Vector2 TamanhoEsperado;
		public Vector2 Meio;
		public float Zoom = 1;
	}

	/// <summary>
	/// A VARREDURA: recorta a regiao, amostra a cor do disco no ponto em que a CENA diz que a luz esta,
	/// e acha as manchas dela.
	///
	/// ============================ A REGIAO EXISTE POR CAUSA DO HUD ============================
	/// O painel de vida, o boneco de papel e a caixa de fala sao de outra `CanvasLayer` -- eles nao
	/// levam o `CanvasModulate` da hora, entao o texto claro deles chega a tela em `ffffff` e por
	/// pouco (0,30 de distancia) nao entra numa mascara larga. Recortar uma janela em volta da cena e
	/// mais honesto do que escrever uma regra de "ignore texto", e ainda deixa a varredura barata.
	/// ======================================================================================
	/// </summary>
	private Achado Varrer(Mira m, Image tela)
	{
		var a = new Achado { Meio = (m.Meu + m.Dele) * 0.5f, Zoom = m.Zoom };
		if (m.Disco is not { } d)
		{ Conferir(false, $"{m.Rotulo}: a cena nao soube dizer o tamanho da folha desenhada"); return a; }

		a.TamanhoEsperado = d.Tamanho * m.Zoom;
		a.AreaEsperada = d.Area * m.Zoom * m.Zoom;
		a.Regiao = RegiaoDe(m, tela);

		// A COR, AMOSTRADA NO PONTO EM QUE A CENA DIZ QUE A LUZ ESTA. Mediana de uma caixinha, e nao um
		// pixel: um pixel na borda serrilhada da folha devolveria a grama.
		a.Cor = ContagemDeDiscos.CorMediana(
			tela, ContagemDeDiscos.Dentro(ContagemDeDiscos.CaixaEmVolta(m.Luz, 9), tela));

		a.Mascara = ContagemDeDiscos.Mascara(tela, a.Regiao, a.Cor, ContagemDeDiscos.ToleranciaDeCor);
		a.Manchas = ContagemDeDiscos.Manchas(a.Mascara, a.Regiao.Size, a.Regiao.Position, PisoDeMancha(m));
		return a;
	}

	/// <summary>
	/// A CONTA DOS DISCOS -- quatro afirmacoes independentes sobre a mesma mancha.
	///
	/// Ver <see cref="ContagemDeDiscos"/>: contar componentes conexos sozinho NAO responde a pergunta
	/// do dono, porque os dois discos do defeito sempre se encostam. Quem responde e a contagem de
	/// NUCLEOS -- e a largura e a area, que denunciam a copia encostada por outro caminho.
	/// </summary>
	private void ContarDiscos(Achado a, string tomada, int esperado)
	{
		Conferir(a.Manchas.Count == esperado,
				 $"{tomada} ha **{esperado}** mancha de disco na tela (achei {a.Manchas.Count}; "
			   + $"piso de {(int)(a.AreaEsperada * FracaoDoPiso)} px)");
		if (a.Manchas.Count == 0) return;

		ContagemDeDiscos.Mancha maior = a.Manchas[0];

		// ============================ **QUANTAS COPIAS DA FOLHA ESTAO PINTADAS AQUI** ============================
		// Esta e a conta que responde ao dono ao pe da letra (*"atualmente sao 2"*), e ela e a unica das
		// cinco que sobrevive a copias ENCOSTADAS: area medida dividida pela area de uma copia do quadro
		// que esta na tela. Duas copias no portao do convite se sobrepoem 57% e dao 1,40 -- medido com o
		// defeito injetado de volta.
		// ====================================================================================================
		double copias = maior.Area / Math.Max(1.0, a.AreaEsperada);
		Conferir(copias is > 0.80 and < 1.25,
				 $"{tomada} ...e ha **{esperado} copia** da folha pintada ali, e nao duas "
			   + $"({copias:0.00} copias: {maior.Area} px contra {a.AreaEsperada:0} de uma so)");

		float largura = maior.Caixa.Size.X / Math.Max(1f, a.TamanhoEsperado.X);
		float altura = maior.Caixa.Size.Y / Math.Max(1f, a.TamanhoEsperado.Y);
		Conferir(largura is > 0.85f and < 1.15f && altura is > 0.85f and < 1.15f,
				 $"{tomada} ...e a mancha tem a CAIXA de uma copia so "
			   + $"({maior.Caixa.Size.X}x{maior.Caixa.Size.Y} px contra "
			   + $"{a.TamanhoEsperado.X:0}x{a.TamanhoEsperado.Y:0} esperados -- {largura:P0} / {altura:P0})");

		// O CENTRO NAO SEPARA UM DE DOIS, e isso esta medido: duas copias simetricas tem o centro de
		// massa no MESMO ponto medio (com o defeito injetado ele saiu a 43 px). Ele responde outra
		// pergunta -- *"a luz esta onde a fusao vai nascer, e nao em cima de um dos dois"* --, e e por
		// isso que ele fica aqui junto e nao no lugar das duas de cima.
		float longe = maior.Centro.DistanceTo(a.Meio);
		Conferir(longe < ZoneCollision.TileSize * a.Zoom,
				 $"{tomada} ...e o centro dela e o PONTO MEDIO dos dois corpos desenhados "
			   + $"({longe:0} px de tela, menos de um tile)");

		// ============================ E O NUCLEO E **NOTA**, PORQUE ELE NAO SEPARA AQUI ============================
		// A ideia era esta: transformada de distancia, e "dois miolos" denunciaria as duas copias mesmo
		// encostadas. **Ela nao funciona nesta geometria, e o numero mostra por que**: duas copias a 4
		// tiles (256 px de tela) com raio de 224 px se cruzam a 184 px de profundidade, e o miolo de uma
		// copia so vai ate 206 px. Sobram 22 px de folga -- perto demais pra distinguir coisa nenhuma.
		//
		// Deixar a afirmacao de pe seria pior do que nao ter: com o defeito injetado ela ficou VERDE
		// dizendo "1 nucleo" com dois discos na tela. Uma checagem que nao sabe ficar vermelha e
		// decoracao, e este projeto ja catalogou o custo disso. Entao ela vira NUMERO no log -- ela
		// ainda separa quando a folha e pequena (o quadro 0) -- e quem responde por copia encostada sao
		// a AREA e a CAIXA, que responderam (1,40 e 146%).
		// ======================================================================================================
		(int Quantos, List<Vector2> Centros, int Maior) nuc = ContagemDeDiscos.Nucleos(
			a.Mascara, a.Regiao.Size, a.Regiao.Position, maior.Caixa, ContagemDeDiscos.FracaoDoNucleo);
		Nota($"{tomada} nucleos luminosos: {nuc.Quantos} "
		   + $"({string.Join(" e ", nuc.Centros.ConvertAll(c => $"({c.X:0},{c.Y:0})"))}), "
		   + $"miolo de {nuc.Maior} px");
	}

	/// <summary>
	/// OS DOIS CORPOS ESTAO DESENHADOS AQUI -- e a pergunta e "ha corpo", nao "nao ha disco".
	///
	/// ============================ A REGUA E O CHAO DA PROPRIA FOTO ============================
	/// A caixa de CONTROLE fica no ponto medio dos dois: no portao do convite ela esta a dois tiles de
	/// cada um, e nessas duas fotos nao ha luz por cima dela. A cor mediana dali e "o chao daqui" --
	/// medida na mesma imagem, com a mesma hora, o mesmo clima e o mesmo planeta. Um verde escrito no
	/// codigo seria a mesma armadilha do limiar de brilho que ja fez esta bancada fechar verde com
	/// cinco fotos pretas.
	///
	/// Entao "ha corpo aqui" e: a caixa do corpo tem MUITO mais pixel fora da cor do chao do que a
	/// caixa de controle tem. E o contra-exemplo esta na mesma linha -- se o disco tivesse tapado
	/// tudo, a cobertura seria alta e o conteudo cairia junto com ela.
	/// ==================================================================================
	/// </summary>
	private void OsDoisAparecem(Achado a, string tomada, Image tela, Mira m)
	{
		int lado = LadoDoCorpo(m);
		Rect2I cxMeu = ContagemDeDiscos.CaixaEmVolta(m.Meu, lado);
		Rect2I cxDele = ContagemDeDiscos.CaixaEmVolta(m.Dele, lado);

		// O CHAO E A MEDIANA DA REGIAO INTEIRA, e nao de uma caixinha escolhida: os dois bonecos, a
		// arvore e os pedregulhos levitando somados nao chegam a metade dela, entao a mediana E a
		// grama. Uma unica caixa de controle daria a cor de qualquer coisa que estivesse ali.
		Color chao = ContagemDeDiscos.CorMediana(tela, a.Regiao);
		float cMeu = ContagemDeDiscos.ConteudoDe(tela, ContagemDeDiscos.Dentro(cxMeu, tela), chao, ToleranciaDoChao);
		float cDele = ContagemDeDiscos.ConteudoDe(tela, ContagemDeDiscos.Dentro(cxDele, tela), chao, ToleranciaDoChao);
		float tipico = ContagemDeDiscos.ChaoTipico(tela, a.Regiao, lado, chao, ToleranciaDoChao,
												   [cxMeu, cxDele, ContagemDeDiscos.CaixaEmVolta(m.Luz, lado)]);

		float tapMeu = ContagemDeDiscos.CoberturaDe(a.Mascara, a.Regiao.Size, a.Regiao.Position, cxMeu);
		float tapDele = ContagemDeDiscos.CoberturaDe(a.Mascara, a.Regiao.Size, a.Regiao.Position, cxDele);

		Conferir(tapMeu < 0.05f && tapDele < 0.05f,
				 $"{tomada} nenhum dos dois corpos esta tapado pelo disco ({tapMeu:P0} e {tapDele:P0})");

		Conferir(cMeu > ConteudoMinimoDeCorpo && cDele > ConteudoMinimoDeCorpo
				 && cMeu > tipico * 4f && cDele > tipico * 4f,
				 $"{tomada} ...e ha CORPO desenhado nos dois lugares -- fora da cor do chao "
			   + $"{cMeu:P1} e {cDele:P1}, contra {tipico:P1} numa mancha qualquer desta cena "
			   + $"(chao medido {chao.ToHtml(false)})");
	}

	/// <summary>
	/// QUANTO UM PIXEL PRECISA FUGIR DA COR DO CHAO PRA CONTAR COMO "NAO E CHAO", em distancia RGB.
	/// 0,18 e folgado o bastante pra a variacao da textura de grama (medida: a mancha tipica de chao
	/// desta cena fica abaixo de 1%) e apertado o bastante pra o contorno preto, o cabelo e a roupa dos
	/// bonecos entrarem.
	/// </summary>
	private const float ToleranciaDoChao = 0.18f;

	/// <summary>
	/// QUANTO DE UMA CAIXA DE CORPO TEM QUE SER "NAO CHAO" PRA HAVER CORPO ALI.
	///
	/// **4% e medido, e o piso duplo e o que da sentido a ele**: um boneco de 32x48 px numa caixa de
	/// 1,5 tile deixa entre 5% e 15% de pixel fora da cor da grama, conforme o contraste da roupa
	/// (medido: 14,4% no de azul, 8,9% no de pele). A mancha de chao tipica da mesma cena fica em 0,0%.
	/// O piso sozinho seria arbitrario; a razao de 4x sozinha seria vazia quando o tipico desse zero.
	/// Os dois juntos dizem "ha muito mais coisa aqui do que em qualquer outro lugar deste chao".
	/// </summary>
	private const float ConteudoMinimoDeCorpo = 0.04f;

	/// <summary>
	/// O PISO DE UMA MANCHA: 20% de UMA copia da folha no quadro que esta na tela. Relativo, e nao
	/// absoluto, porque os sete quadros do `flick` variam de 24 a 9.548 pixels opacos -- um piso fixo
	/// engoliria o quadro 0 inteiro ou deixaria passar respingo no quadro 3.
	/// </summary>
	private const float FracaoDoPiso = 0.20f;

	private static int PisoDeMancha(Mira m) =>
		Math.Max(64, (int)((m.Disco?.Area ?? 0) * m.Zoom * m.Zoom * FracaoDoPiso));

	/// <summary>
	/// UMA CAIXA DE CORPO TEM 1,5 TILE DE LADO. Dois tiles diluem o boneco em chao (medido: o mesmo
	/// corpo cai de 14,4% pra 9,7% de "nao chao" so por causa da moldura de grama que sobra) e um tile
	/// so cortaria o cabelo e os pes. 1,5 e o que envolve o sprite de 32x48 sem sobra.
	/// </summary>
	private static int LadoDoCorpo(Mira m) => (int)(1.5f * ZoneCollision.TileSize * m.Zoom);

	/// <summary>
	/// A JANELA VARRIDA: tres vezes o disco esperado, ou o dobro do vao entre os dois corpos -- o que
	/// for maior. Com o defeito de volta (uma copia em cada corpo) a mancha fica ~57% mais larga, e ela
	/// tem que caber INTEIRA aqui dentro: uma mancha cortada pela borda mediria menos e a bancada
	/// ficaria verde pelo motivo errado.
	/// </summary>
	private static Rect2I RegiaoDe(Mira m, Image tela)
	{
		float vao = m.Meu.DistanceTo(m.Dele);
		float largo = Math.Max(3f * (m.Disco?.Tamanho.X ?? 0) * m.Zoom, 2f * vao + 4f * ZoneCollision.TileSize * m.Zoom);
		int lado = Math.Max(64, (int)largo);
		return ContagemDeDiscos.Dentro(ContagemDeDiscos.CaixaEmVolta((m.Meu + m.Dele) * 0.5f, lado), tela);
	}

	// =====================================================================
	// 3) ENTRE AS DUAS: desfaz, recoloca o corpo e repete
	// =====================================================================
	private void OFimDaMetamoro(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		srv.SepararNaFotoDeFusao(cli.LocalId, "a bancada vai fotografar a Potara agora");

		var e = srv.EstadoNaFotoDeFusao(cli.LocalId);
		Conferir(!e.Fundido, "desfeita a metamoro, o corpo volta a ser o do jogador");
		Conferir(e.Roupa.All(r => !r.Contains("Metamoran", StringComparison.OrdinalIgnoreCase)),
				 "...e o colete metamoriano SAI junto (nada fica vestido pra sempre)");

		// O PASSAGEIRO VOLTA AO LADO DE QUEM DIRIGIA -- ou seja, EM CIMA de mim. O portao pra o lado e
		// cenario, e nao a coisa medida: sem isso os dois corpos sairiam empilhados na foto seguinte.
		srv.PorPertoNaFotoDeFusao(_outro, cli.LocalId, OPalcoDaFoto);

		_disparei = false;
		_anterior = null;
		_tireiNasce = _tireiLimpo = _tireiCena = _tireiBranco = _tireiBrancoTarde = _tireiPronta = false;
		Virar(PPotCena);
	}

	private void OFimDaPotara(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;
		Virar(PSsj4);
	}

	// =====================================================================
	// 5) O SSJ4 DE CABELO VERMELHO
	// =====================================================================
	/// <summary>
	/// *"toda fusao tem cabelo vermelho no SSJ4, tendo ou nao o cabelo do Vegito"*.
	///
	/// A forca vem do `admin_forma` porque o degrau de verdade pede maestria, Oozaru Dourado despertado
	/// e BP de porta -- forjar a escada inteira pra tirar uma foto seria escrever um segundo motor de
	/// progressao. O que a foto mede e o CABELO, e ele nao sabe por onde a forma chegou.
	///
	/// **A fusao continua de pe aqui** (a Potara nao foi desfeita), que e o ponto: sem ela na tela, o
	/// SSJ4 sairia com a folha dourada de sempre.
	/// </summary>
	private void OSsj4(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (!_pediuSsj4)
		{
			_pediuSsj4 = true;
			_ssj4Pedidos++;
			srv.FormaNaFotoDeFusao(cli.LocalId, "ssj4");
			return;
		}

		var e = srv.EstadoNaFotoDeFusao(cli.LocalId);
		bool chegou = e.Forma == "ssj4";

		// ============================ E SE ALGUEM MEXER NA JANELA, ELA REFAZ E **DIZ** ============================
		// Esta bancada abre janela, e janela numa maquina em que o dono esta trabalhando recebe clique.
		// Numa rodada o menu P mandou um `admin_forma [0|ssj1]` no meio da espera e a forma caiu de ssj4
		// pra ssj1 -- tres provas vermelhas por um motivo que nao e do jogo. Repetir o pedido recupera;
		// o CONTADOR e o que impede isso de virar tapa-buraco: uma forma que nao gruda por defeito de
		// verdade aparece aqui como uma fileira de tentativas, e nao como silencio.
		// ======================================================================================================
		if (!chegou && _t > 6 * _ssj4Pedidos && _ssj4Pedidos < 4)
		{
			_ssj4Pedidos++;
			srv.FormaNaFotoDeFusao(cli.LocalId, "ssj4");
			return;
		}

		// ============================ ESPERAR O NUMERO NAO BASTA -- ESPERE A CENA ACABAR ============================
		// A primeira rodada esperou 6 s depois de o SERVIDOR dizer `Forma == "ssj4"` e fotografou o
		// cabelo do VEGITO sem tinta nenhuma. O motivo esta escrito no `World`: quando ha cinematica de
		// forma em curso, a aparencia nova **fica pendurada** (`_pendentes`) e so e vestida no beat que
		// ASSUME. Ou seja, o campo do servidor chega muito antes do pixel -- e um relogio de bancada
		// nunca vai adivinhar o comprimento de uma cena que muda com a maestria.
		//
		// Entao a espera e por FATO e nao por prazo: **nenhuma cinematica rodando** e a folha do cabelo
		// ja trocada. O prazo continua existindo, mas como PACIENCIA (a bancada tem que morrer falando).
		// ========================================================================================================
		bool cenaRodando = AchaCena() != null;
		bool vestiu = mundo.VisualLocalDeTeste?.CabeloDeTeste == CabelosDeForma.FolhaDoSsj4DaFusao;
		if ((!chegou || cenaRodando || !vestiu) && _t < 40) return;

		// MEIO SEGUNDO DEPOIS DE TUDO ASSENTAR: a foto e do quadro, e o quadro precisa ter sido
		// desenhado com a folha nova.
		if (_esperandoAssentar < 0) { _esperandoAssentar = _t; return; }
		if (_t - _esperandoAssentar < 0.5) return;

		Nota($"SSJ4: cena rodando = {cenaRodando}, folha ja trocada = {vestiu}, esperei {_t:0.#} s, "
		   + $"{_ssj4Pedidos} pedido(s) de forma");

		Conferir(chegou, $"a fusao chegou ao SSJ4 (forma '{e.Forma}')");

		CharacterVisual? vis = mundo.VisualLocalDeTeste;
		Conferir(vis is { EhFusaoDeTeste: true },
				 "o cliente sabe que este corpo e uma FUSAO (o bit que decide o vermelho)");
		Conferir(vis?.CabeloDeTeste == CabelosDeForma.FolhaDoSsj4DaFusao,
				 $"...e a folha na cabeca e a do Gogeta (deu {NomeCurto(vis?.CabeloDeTeste ?? "")})");

		(Vector3 Tinta, int Modo)? tinta = vis?.TintaDoCabeloDeTeste;
		var esperada = new Color(Fusao.VermelhoDoCabeloDaFusao);
		Conferir(tinta is { } t
				 && Math.Abs(t.Tinta.X - esperada.R) < 0.02f
				 && Math.Abs(t.Tinta.Y - esperada.G) < 0.02f
				 && Math.Abs(t.Tinta.Z - esperada.B) < 0.02f,
				 $"...e a tinta e o vermelho `{Fusao.VermelhoDoCabeloDaFusao}` (deu {tinta?.Tinta.ToString() ?? "nada"})");
		Conferir(tinta is { Modo: 0 },
				 $"...e ela SOMA e nao matiza (tinta_modo = {tinta?.Modo.ToString() ?? "?"})");

		Vector2 onde = mundo.PosicaoDesenhadaDe(cli.LocalId) is { } p ? ParaTela(p) : Meio();
		Retrato("fusao-c1-ssj4", "C1 a fusao em SSJ4, de cabelo vermelho", onde);
		Virar(PFim);
	}

	private bool _pediuSsj4;

	/// <summary>Quantas vezes a forma foi pedida. Ver o bloco no <see cref="OSsj4"/>.</summary>
	private int _ssj4Pedidos;

	/// <summary>Quando tudo assentou (negativo = ainda nao). Ver o bloco no <see cref="OSsj4"/>.</summary>
	private double _esperandoAssentar = -1;

	// =====================================================================
	// AS FOTOS
	// =====================================================================
	private sealed class Tomada
	{
		public string Nome = "", Rotulo = "";
		public Image? Quadro, Perto, Tira;
		public Mira? Mira;
		public Vector2 Centro;
		public int Lado = 288, Escala = 3;
	}

	/// <summary>
	/// OS DOIS RECORTES.
	///
	/// **O DA CENA** tem que caber os dois corpos no portao do convite (4 tiles = 256 px de tela) MAIS
	/// o disco inteiro (448 px no quadro cheio): 544 px de lado, ampliados 2x.
	///
	/// **O DO RETRATO** e apertado de proposito: num quadro de 1920x1080 a 1x, a diferenca entre um
	/// colete metamoriano e um brinco Potara sao uns doze pixels no meio de um boneco de 32. *"A roupa
	/// e so o colete"* e uma afirmacao que a tela cheia nao consegue sustentar.
	///
	/// Ampliacao em Nearest nos dois, o filtro dos outros robos deste projeto: em pixel art,
	/// interpolar e inventar.
	/// </summary>
	private const int LadoDaCena = 544, EscalaDaCena = 2, LadoDoRetrato = 288, EscalaDoRetrato = 3;

	/// <summary>
	/// O RECORTE DA **TIRA DA SEQUENCIA** -- **tres quartos da janela**, escala 1.
	///
	/// ============================ E ELE E UMA FRACAO, E NAO UM NUMERO DE PIXELS ============================
	/// Ja foi `900`, e isso quebrou duas vezes na mesma rodada. Primeiro porque a janela desta bancada
	/// nao tem o tamanho que a linha de comando pede (o `--resolution 1600x900` nao pegou: a foto saiu
	/// 1920x1080), e um lado cravado ou sobra ou nao cabe. Depois porque quando a caixa passava da borda
	/// o recorte ENCOLHIA -- e o `Montar` **pula em silencio** os paineis de tamanho diferente, entao a
	/// tira saia com um painel desenhado e cinco pretos, sem uma linha vermelha em lugar nenhum.
	///
	/// Como fracao, todos os paineis da rodada tem exatamente o mesmo tamanho por construcao, e ele
	/// acompanha a janela. Os 3/4 deixam de fora a moldura do HUD (o painel de vida no canto, o boneco
	/// de papel, a caixa de fala) sem cortar a acao, que a camera mantem no meio.
	///
	/// Escala 1 porque a tira ja sai com quase nove mil pixels de largura; ampliar aqui nao acrescenta
	/// informacao nenhuma.
	/// </summary>
	private const float FracaoDaTira = 0.75f;

	/// <summary>
	/// O RECORTE DA TIRA NESTA IMAGEM -- <see cref="FracaoDaTira"/> da janela, centrado no assunto e
	/// EMPURRADO pra dentro (ver <see cref="Encaixar"/>), nunca encolhido.
	/// </summary>
	private static Rect2I RecorteDaTira(Vector2 centro, Image tela)
	{
		int w = (int)(tela.GetWidth() * FracaoDaTira), h = (int)(tela.GetHeight() * FracaoDaTira);
		return Encaixar(new Rect2I((int)centro.X - w / 2, (int)centro.Y - h / 2, w, h), tela);
	}

	private readonly List<Tomada> _tomadas = [];

	private Tomada? Achar(string nome) => _tomadas.Find(t => t.Nome == nome);

	/// <summary>
	/// PEGA O QUADRO, e so isso -- ver o bloco no <see cref="Revelar"/> sobre por que gravar aqui
	/// dentro fazia a bancada perder a foto seguinte.
	/// </summary>
	private void Tomar(Mira m, Vector2 centro, int lado, int escala)
	{
		var t = new Tomada
		{
			Nome = m.Nome, Rotulo = m.Rotulo, Mira = m,
			Centro = centro, Lado = lado, Escala = escala,
		};
		_tomadas.Add(t);

		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty())
		{
			Nota($"{m.Rotulo}: SEM FOTO (headless nao renderiza -- rode com janela)");
			return;
		}

		tela.Convert(Image.Format.Rgba8);
		t.Quadro = tela;
	}

	/// <summary>Uma tomada de retrato (a fusao pronta), que nao mede pixel nenhum.</summary>
	private void Retrato(string nome, string rotulo, Vector2 centro) =>
		Tomar(new Mira { Nome = nome, Rotulo = rotulo, Meu = centro, Dele = centro, Zoom = Zoom() },
			  centro, LadoDoRetrato, EscalaDoRetrato);

	/// <summary>RECORTA E GRAVA tudo o que foi pego -- no fim, com o mundo ja parado.</summary>
	private void GravarAsFotos()
	{
		foreach (Tomada t in _tomadas)
		{
			if (t.Quadro is not { } tela) continue;
			Gravar(tela, t.Nome + ".png", t.Rotulo);

			// AS FOTOS DA COREOGRAFIA NAO TEM RETRATO (`Lado <= 0`): elas afirmam coisas sobre a
			// DISTANCIA entre dois bonecos, e um recorte apertado num deles nao afirma distancia
			// nenhuma. Pra elas o "perto" e o proprio recorte da tira.
			Image perto = t.Lado > 0
				? tela.GetRegion(ContagemDeDiscos.Dentro(
					ContagemDeDiscos.CaixaEmVolta(t.Centro, t.Lado), tela))
				: tela.GetRegion(RecorteDaTira(t.Centro, tela));
			perto.Convert(Image.Format.Rgba8);
			if (t.Escala > 1)
				perto.Resize(perto.GetWidth() * t.Escala, perto.GetHeight() * t.Escala,
							 Image.Interpolation.Nearest);
			t.Perto = perto;
			Gravar(perto, t.Nome + "-perto.png", t.Rotulo + " (recorte)");

			// ============================ O RECORTE DA TIRA E UM TERCEIRO, E ELE NAO E ENFEITE ============================
			// Os tres recortes desta bancada tem tres tamanhos porque afirmam tres coisas diferentes (a
			// cena, o retrato, a sequencia). O `Montar` cola imagens do MESMO tamanho e CALA quando elas
			// diferem -- a primeira tira da `--diagraio` saiu um retangulo preto sem erro nenhum no log.
			// Entao a tira da sequencia ganha um recorte proprio, do mesmo lado pra todos os paineis.
			// ==========================================================================================================
			t.Tira = tela.GetRegion(RecorteDaTira(t.Centro, tela));
			t.Tira.Convert(Image.Format.Rgba8);

			// ============================ A FOTO TEM QUE DAR PRA OLHAR -- E ISTO E UM ACHADO ============================
			// A primeira rodada desta bancada fechou "TUDO OK, cinco tomadas" e as cinco fotos estavam
			// PRETAS: o mundo estava de noite. Todas as checagens de campo ficaram verdes porque elas
			// leem o `LookDeFusao`, e nao o pixel -- ou seja, a bancada que existe pra cobrir o cego do
			// pixel caiu nele.
			//
			// A GUARDA E NO PIXEL E NAO NO RELOGIO, de proposito: "esta de dia" e uma TEORIA sobre por
			// que a foto ficaria clara (e ela ja estava errada uma vez -- a regua era a Terra, e a
			// bancada nasce noutro planeta). "O quadro mais claro passa de 30% de luz" e o FATO, e ele
			// pega tambem tempestade, eclipse, caverna e qualquer coisa que ninguem previu.
			// ========================================================================================================
			Conferir(MaisClaroDe(perto) > 0.30f,
					 $"{t.Rotulo}: a foto da pra OLHAR (mais claro {MaisClaroDe(perto):0.00}, "
				   + $"hora local {_hora:0.00}{(_noite ? ", e este mundo esta de NOITE" : "")})");
		}
	}

	private void Gravar(Image img, string arquivo, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + arquivo);
			img.SavePng(caminho);
			_linhas.Add($"         {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>
	/// A TIRA, COLADA -- *"uma foto de metamoro e uma de potara, LADO A LADO, porque roupa e cabelo
	/// diferem"*, que e o pedido literal. Arquivos separados obrigam quem le a abrir duas janelas e
	/// comparar de cabeca, e a comparacao E a afirmacao.
	/// </summary>
	/// <summary>
	/// A CAIXA **EMPURRADA** PRA DENTRO DA IMAGEM, sem encolher.
	///
	/// ============================ ELA EXISTE PORQUE A PRIMEIRA TIRA SAIU PRETA ============================
	/// O <see cref="ContagemDeDiscos.Dentro"/> CORTA a caixa que passa da borda, e corte e o certo pra
	/// medir (uma varredura nao pode ler pixel que nao existe). Pra a tira e o errado: o `Montar` cola
	/// imagens do MESMO tamanho e **pula em silencio** as que diferem -- e um painel recortado na borda
	/// da tela virava cinco paineis pretos e um so com desenho. Foi o que aconteceu na primeira rodada
	/// desta tira, e nada reprovou, porque "a tira saiu feia" nao tem quem meca.
	///
	/// Empurrar preserva o tamanho e so encolhe no caso em que a tela e MENOR que o recorte -- e ai o
	/// `Montar` continua sendo quem decide, com todos os paineis iguais.
	/// ==================================================================================================
	/// </summary>
	private static Rect2I Encaixar(Rect2I r, Image img)
	{
		int larg = Math.Min(r.Size.X, img.GetWidth());
		int alt = Math.Min(r.Size.Y, img.GetHeight());
		int x = Math.Clamp(r.Position.X, 0, img.GetWidth() - larg);
		int y = Math.Clamp(r.Position.Y, 0, img.GetHeight() - alt);
		return new Rect2I(x, y, larg, alt);
	}

	private void Montar(string arquivo, string[] nomes, bool daTira = false)
	{
		// DOS RECORTES e nao das telas cheias: duas telas de 1920 px encostadas dao um arquivo de
		// 3840 px em que os sujeitos continuam com 32 px. Os quadros cheios seguem gravados um a um.
		var uteis = new List<Image>();
		foreach (string n in nomes)
			if ((daTira ? Achar(n)?.Tira : Achar(n)?.Perto ?? Achar(n)?.Quadro) is { } q) uteis.Add(q);
		if (uteis.Count == 0) return;

		const int Vao = 8;
		int larg = uteis[0].GetWidth(), alt = uteis[0].GetHeight();
		Image colagem = Image.CreateEmpty(larg * uteis.Count + Vao * (uteis.Count - 1), alt,
										  false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < uteis.Count; i++)
		{
			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados e CALA quando nao tem (a primeira tira
			// da `--diagraio` saiu um retangulo preto sem erro nenhum no log).
			var pedaco = (Image)uteis[i].Duplicate();
			pedaco.Convert(Image.Format.Rgba8);
			if (pedaco.GetWidth() != larg || pedaco.GetHeight() != alt) continue;
			colagem.BlitRect(pedaco, new Rect2I(Vector2I.Zero, pedaco.GetSize()),
							 new Vector2I(i * (larg + Vao), 0));
		}

		Gravar(colagem, arquivo, "a tira");
	}

	// =====================================================================
	// UTILITARIOS
	// =====================================================================
	/// <summary>
	/// A CINEMATICA VIVA, achada na arvore. O `World.CenaEmCurso` e privado (e tem que ser: ele e o
	/// roteador de quem veste o que), entao a bancada varre os nos -- e varrer e o certo aqui, porque
	/// o que ela quer saber e *"existe UMA cena rodando?"*, e nao *"a cena do corpo X"*.
	/// </summary>
	private Transformacao? AchaCena() => PrimeiraCena(GetTree()?.Root);

	private static Transformacao? PrimeiraCena(Node? n)
	{
		if (n == null) return null;
		if (n is Transformacao { Rodando: true } t) return t;
		foreach (Node f in n.GetChildren())
			if (PrimeiraCena(f) is { } achou) return achou;
		return null;
	}

	/// <summary>De mundo pra TELA -- o mesmo `CanvasTransform` que desenhou o quadro.</summary>
	private Vector2 ParaTela(Vector2 mundo) => (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;

	/// <summary>O zoom da camera, lido do `CanvasTransform` e nao do `World.PisoDoZoom`.</summary>
	private float Zoom() => (GetViewport()?.CanvasTransform ?? Transform2D.Identity).Scale.X;

	private Vector2 Meio()
	{
		Rect2 v = GetViewport()?.GetVisibleRect() ?? new Rect2(0, 0, 2, 2);
		return v.Size * 0.5f;
	}

	/// <summary>
	/// O PIXEL MAIS CLARO DA IMAGEM, em luz de 0 a 1. Amostrado de 4 em 4 px porque a pergunta e
	/// *"da pra olhar?"* e nao *"qual e o maximo exato"*.
	/// </summary>
	private static float MaisClaroDe(Image img)
	{
		float maior = 0;
		for (int y = 0; y < img.GetHeight(); y += 4)
			for (int x = 0; x < img.GetWidth(); x += 4)
			{
				Color c = img.GetPixel(x, y);
				float luz = Math.Max(c.R, Math.Max(c.G, c.B));
				if (luz > maior) maior = luz;
			}
		return maior;
	}

	/// <summary>So o nome do arquivo -- um `res://` inteiro por linha de log nao se le.</summary>
	private static string NomeCurto(string caminho) =>
		caminho.Length == 0 ? "(nada)" : caminho[(caminho.LastIndexOf('/') + 1)..];

	// =====================================================================
	// O FIM
	// =====================================================================
	/// <summary>
	/// AS TOMADAS QUE ESTA BANCADA EXISTE PRA TIRAR -- e o <see cref="Fechar"/> COBRA as nove.
	///
	/// ============================ "A FOTO QUE NAO SAIU" NAO TINHA QUEM REPROVASSE ============================
	/// Na primeira rodada com janela do obturador por FATO, a foto do auge nao saiu (o gatilho perdia a
	/// janela de 0,1 s do quadro 3) e o log fechou com **TUDO OK**: todas as afirmacoes da A1 moram
	/// DENTRO do bloco que so roda quando a foto e tirada, entao nao tirar a foto era o unico jeito de
	/// nunca reprovar. E o mesmo cego que a memoria deste projeto chama de "a bancada mede INTENCAO",
	/// so que na forma mais boba dele: ausencia de medida lida como medida boa.
	/// ====================================================================================================
	/// </summary>
	private static readonly string[] AsTomadas =
	[
		"fusao-r1-longe-recusada", "fusao-r1-colado-aceita",
		"fusao-r2-puxao-longe", "fusao-r2-puxao-perto", "fusao-r3-encostaram",
		"fusao-r3-borda-nao-funde",
		"fusao-a0-nasce", "fusao-a0b-janela-limpa", "fusao-a1-cena",
		"fusao-a2-branco", "fusao-a2b-branco-meio", "fusao-a3-branco-escoando",
		"fusao-b1-metamoro", "fusao-b2-potara", "fusao-c1-ssj4",
	];

	private void Fechar()
	{
		_acabou = true;

		foreach (string t in AsTomadas)
			Conferir(Achar(t)?.Quadro != null, $"a tomada `{t}` foi tirada");

		MedirAsFotos();
		GravarAsFotos();

		// ============================ AS TIRAS -- E O NOME DELAS NAO PODE COLIDIR ============================
		// `fusao-A2-branco.png` e o que estava escrito aqui, e no Windows ele **e o mesmo arquivo** que a
		// tomada `fusao-a2-branco.png`: a tira sobregravava a foto de tela cheia do climax, calada. A
		// diferenca entre as duas era so a caixa alta, e o sistema de arquivos nao a enxerga. Daqui em
		// diante toda tira comeca por `fusao-tira-`, que nenhuma tomada usa.
		// =================================================================================================
		Montar("fusao-tira-metamoro-e-potara.png", ["fusao-b1-metamoro", "fusao-b2-potara"]);
		Montar("fusao-tira-clarao.png", ["fusao-a0-nasce", "fusao-a1-cena", "fusao-a0b-janela-limpa"]);
		Montar("fusao-tira-escoamento.png",
			   ["fusao-a2-branco", "fusao-a2b-branco-meio", "fusao-a3-branco-escoando"]);
		Montar("fusao-tira-portao.png", ["fusao-r1-longe-recusada", "fusao-r1-colado-aceita"]);

		// ============================ **A TIRA QUE A FASE 2 PEDIU** ============================
		// *"Uma TIRA da sequencia (dois corpos -> puxao -> clarao -> branco -> escoando) vale mais que
		// fotos soltas"*. Os cinco paineis sao os cinco instantes, na ordem em que aconteceram, e os
		// tres primeiros vem de fusoes DIFERENTES da mesma rodada -- o puxao e da Potara de verdade (do
		// convite ao corpo fundido) e o clarao e do palco de 4 tiles, onde a contagem de discos tem
		// dente. A tira e a leitura; as afirmacoes estao cada uma na sua foto.
		// ===================================================================================
		Montar("fusao-tira-sequencia.png",
			   ["fusao-r2-puxao-longe", "fusao-r2-puxao-perto", "fusao-r3-encostaram",
				"fusao-a0-nasce", "fusao-a1-cena", "fusao-a2-branco", "fusao-a3-branco-escoando"],
			   daTira: true);

		S?.LimparAFotoDeFusao();

		if (_cabeloDoJogador.Length > 0)
			Nota($"o cabelo do jogador ('{_cabeloDoJogador}') foi devolvido na limpeza");

		if (_fecheiOMenu > 0)
			Nota($"ATENCAO: o menu do jogo estava aberto em {_fecheiOMenu} quadro(s) e a bancada o fechou "
			   + "-- alguem mexeu nesta janela durante a rodada (ver o bloco no `_Process`)");

		GD.Print("\n[fotofusao] ===== A FUSAO, FOTOGRAFADA =====");
		foreach (string l in _linhas) GD.Print("[fotofusao] " + l);
		GD.Print(_falhas.Count == 0
			? $"[fotofusao] ===== TUDO OK ({_tomadas.Count} tomadas) ====="
			: $"[fotofusao] ===== {_falhas.Count} FALHA(S) =====\n[fotofusao]   "
			  + string.Join("\n[fotofusao]   ", _falhas));
		GetTree().Quit();
	}
}
