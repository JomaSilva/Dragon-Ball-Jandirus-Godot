using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A VARIEDADE, FOTOGRAFADA COM O TIRO SAINDO DA MAO (`--diagvariedade`) ============================
/// O pedido do dono foi *"vi q vc n ta utilizando os ICONES DE BEAM q era usado no byond pra dar mais
/// VARIEDADE nos ataques de ki"*. Isso e uma queixa de OLHO, e so uma foto responde.
///
/// A `--diagartedeki` ja mede a tabela, ja prova que as folhas carregam e ja compara pixel -- mas ela
/// monta os `ProjetilDesenhado` COM A MAO. Entre a tabela e a tela ha um caminho que ela pula inteiro:
///
///     o verb -> `UsarHabilidade` -> gate da skill -> `Canalizar`/`Disparar` ->
///     `ArteDeProjetil.De(verbo, raca, classe, semente)` -> o `ushort` do anuncio de nascimento ->
///     `World.AoNascerTiro` -> `Vestir` -> o `_Draw` do node
///
/// Um elo qualquer daquela fila escrito e nao ligado devolve exatamente a queixa do dono -- todo ataque
/// com a mesma cara -- com a bancada de arte inteirinha verde. Entao **aqui nada e forjado**: cada tiro
/// sai pelo botao (`GameServer.GatilhoDaVariedade` chama a mesma `UsarHabilidade` do `C2S.Habilidade`),
/// e o que a foto pega e o node que o pacote de nascimento criou.
/// ==========================================================================================================================
///
/// ============================ COMO A COMPARACAO E FEITA, E POR QUE ASSIM ============================
/// Comparar duas fotos inteiras nao serve: **o cenario deste jogo se DESTROI**. A segunda rodada desta
/// bancada mostrou isso na cara -- depois de meia duzia de raios a arvore da moldura tinha sumido e a
/// grama tinha virado areia, e a partir dali 100% dos pixels "mudavam" em relacao a foto do comeco.
/// Toda tecnica ficava "diferente" de todas, inclusive de si mesma.
///
/// Entao cada tiro e fotografado DUAS vezes:
///
///   1. o mesmo recorte VAZIO, um segundo antes de apertar o botao (o fundo DAQUELE tiro);
///   2. o recorte com o tiro nele.
///
/// A diferenca entre as duas e a MASCARA: os pixels que sao o tiro, e so eles. O cenario, seja qual for
/// o estado dele, cai fora nos dois lados da conta. Duas tecnicas sao comparadas mascara contra
/// mascara -- forma E cor --, e e por isso que o resultado nao depende de quanto chao ja foi arrancado.
///
/// AS OUTRAS DUAS REGRAS:
///   * **O MESMO INSTANTE GEOMETRICO.** O obturador nao dispara no relogio: dispara quando a cabeca do
///     tiro andou <see cref="TilesDoObturador"/> tiles. Tecnicas tem velocidades diferentes, e
///     fotografar "meio segundo depois" poria cada tiro num lugar diferente do recorte.
///   * **O CONTROLE.** A primeira tecnica que deu certo e disparada DE NOVO no fim, e as duas mascaras
///     dela sao comparadas entre si. Esse numero e o CHAO DO RUIDO (a folha anima, a particula sai
///     diferente). Nenhum par de tecnicas diferentes pode ficar igual ou abaixo dele -- e essa e a
///     linha que reprova se dois tipos voltarem a desenhar igual.
/// ================================================================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     &lt;godot&gt; --path . --host --rede 7953 --bpteste 300000000 --horateste 0.5 --diagvariedade \
///              --position 1920,0 --resolution 1600x900 --raca Human --conta bancada_var --nome Variado
///
/// As fotos saem em `user://variedade-*.png`: uma por tecnica, mais o MOSAICO com todas lado a lado.
/// </summary>
public partial class RoboDeVariedadeDeKi : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	/// <summary>
	/// UMA TECNICA POR FOLHA DO CATALOGO -- e a lista e escrita A MAO de proposito.
	///
	/// Varrer a `ArteDeProjetil` pra fotografar a `ArteDeProjetil` sempre passaria. O que esta escrito
	/// aqui e o que o JOGADOR aperta, e a bancada reprova no dia em que uma delas parar de sair.
	///
	/// FICARAM DE FORA as duas do cerco (`Ki_Bomb`, `Hellzone_Grenade`), que nascem EM VOLTA DO ALVO
	/// (`blasts.dm:434`) -- a vinte tiles daqui, fora do recorte --, e as duas barragens (`Scattershot`,
	/// `Energy_Barrage`), que sorteiam a direcao de cada bola (`step(A,randdir)`): a regra do recorte
	/// fixo nao aguenta um tiro que voa pra qualquer lado.
	/// </summary>
	private static readonly (string Verbo, string Rotulo)[] Roteiro =
	[
		// ---- OS RAIOS (canal: aperta uma vez, a cabeca nasce sozinha quando a carga fecha)
		("Ki_Wave",           "Onda de Ki"),
		("Kamehameha",        "Kamehameha"),      // a folha e SORTEADA por personagem (`rand(1,6)`)
		("Masenko",           "Masenko"),
		("Makkankosappo",     "Makankosappo"),
		("Massive_Beam",      "Raio Colossal"),
		("Final_Flash",       "Final Flash"),
		("GalicGun",          "Galick Ho"),
		("Death_Beam",        "Death Beam"),
		("Dodompa",           "Dodon Ray"),
		("Enkumei",           "Enkumei"),
		("Boom_Wave",         "Boom Wave"),

		// ---- AS BOLAS E OS DISCOS
		("Basic_Blast",       "Bola de Ki"),      // RACIAL: Human = `1.dmi`
		("Charged_Shot",      "Tiro Carregado"),
		("Guided_Ball",       "Esfera Teleguiada"),
		("Kienzan",           "Kienzan"),
		("Scattering_Bullet", "Bala Dispersa"),
		("Spirit_Gun",        "Spirit Gun"),
		("Kikoho",            "Kikoho"),
		("KillDriver",        "Kill Driver"),
		("BusterShell",       "Buster Shell"),
		("Paralysis",         "Paralisia"),
	];

	/// <summary>
	/// AS DUAS ESCOLHAS DO JOGADOR, na MESMA tecnica -- o `pick_game_icon` do DM
	/// (`customattacks.dm:543-568`), que e o unico lugar em que a arte e ESCOLHA e nao tabela.
	///
	/// Duas folhas que o recorte por tipo do original permite a um RAIO (`Beams` + `Techniques`), e o
	/// verbo e o MESMO nas duas fotos: `ca_editar` -> `ca_arte` -> `ca_salvar`. Duas tecnicas diferentes
	/// mostrariam duas tecnicas diferentes, que e outra pergunta.
	///
	/// RAIO E NAO BOLA de proposito: a mesa nasce em Beam (`CriarTecnica`), e sair do Beam obriga a
	/// desfazer os modificadores de raio -- uma recusa legitima do jogo que na bancada so atrapalha.
	/// </summary>
	private const ArteDeKi EscolhaA = ArteDeKi.Beam2, EscolhaB = ArteDeKi.Kamehameha5;

	// =====================================================================
	// OS NUMEROS DA FOTO
	// =====================================================================
	/// <summary>Quantos tiles a cabeca do tiro anda antes do obturador.</summary>
	private const double TilesDoObturador = 4.0;

	/// <summary>O lado do recorte, em pixel de tela. 192 = seis tiles: cabe o tiro e sobra cenario.</summary>
	private const int Lado = 192;

	/// <summary>
	/// DIFERENCA DE CANAL a partir da qual dois pixels contam como DIFERENTES.
	///
	/// ============================ 0,12 E MEDIDO, E NAO ESCOLHIDO ============================
	/// A terceira rodada desta bancada rodou com 0,03 (8 de 255) e o recorte VAZIO, fotografado duas
	/// vezes seguidas, ja diferia em 8.448 dos 36.864 pixels: quase um quarto da imagem "mudava" com
	/// nada acontecendo. Com um chao de ruido desse tamanho -- maior que o proprio tiro, que ocupa
	/// uns dez mil -- a comparacao nao decide nada, e 148 pares foram declarados iguais entre si.
	///
	/// A bancada MEDE esse ruido em quatro limiares e imprime os quatro (ver <see cref="OChaoDoCenario"/>),
	/// justamente pra este numero nao virar um chute com aparencia de constante. Um tiro de ki e branco
	/// ou saturado sobre grama e areia: ele passa de 0,12 com folga, e o tremor do cenario nao.
	/// ==================================================================================
	/// </summary>
	private const float Epsilon = 0.12f;

	/// <summary>Depois disto ela desiste da tecnica da vez e passa pra proxima.</summary>
	private const double PacienciaPorTiro = 14;

	/// <summary>Depois disto ela desiste de tudo.</summary>
	private const double Paciencia = 600;

	// =====================================================================
	// OS PASSOS
	// =====================================================================
	private const int PAssentar = 0, PFundo = 1, PRoteiro = 2, PAntesDoTiro = 3, PEsperar = 4,
					  PDepoisDoTiro = 5, PEscolhas = 6, PControle = 7, PComparar = 8, PMosaico = 9;

	// =====================================================================
	// O ESTADO
	// =====================================================================
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	/// <summary>
	/// UMA FOTO: o recorte com o tiro, o mesmo recorte VAZIO de um segundo antes, e a mascara que sai da
	/// diferenca dos dois -- os pixels que sao o tiro, e so eles.
	/// </summary>
	private sealed class Tomada
	{
		public string Verbo = "", Rotulo = "";
		public Image? Recorte, Fundo;
		public bool[]? Mascara;
		public ArteDeKi ArteNoCliente, ArteNoServidor;
		public TipoDeProjetil Tipo;
		public int PixelsDoTiro;
		public double AndouTiles;
		public float Escala = 1;
		public bool Desenhado;
	}

	private readonly List<Tomada> _tomadas = [];
	private Rect2I _recorte;
	private Image? _fundoDaVez, _telaDeFundo;
	private Vector2 _rumo = Vector2.Down;
	private int _chaoDoCenario;

	private bool _acabou;
	private double _t, _vida, _tDoTiro;
	private int _passo, _iDoRoteiro, _slotDaInventada, _escolhasFeitas, _depoisDoTiro = PRoteiro;
	private string _verboDaInventada = "";
	private Tomada? _emCurso;
	private CanvasLayer? _mosaico;
	private int _quadrosDoMosaico;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	// =====================================================================
	// O LACO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv)
		{
			Nota("sem servidor no processo (`--diagvariedade` precisa de `--host`)");
			Fechar();
			return;
		}

		_vida += delta;
		if (_vida > Paciencia && _passo < PComparar)
		{
			Nota($"acabou a paciencia ({Paciencia:0} s) -- compara o que deu tempo de fotografar");
			Virar(PComparar);
		}

		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case PFundo: OChaoDoCenario(mundo); break;
			case PRoteiro: ApertarOBotao(srv, cli); break;
			case PAntesDoTiro: OFundoDaVez(mundo, srv, cli); break;
			case PEsperar: EsperarOTiro(mundo, srv, cli, delta); break;
			case PDepoisDoTiro: OFundoDepois(srv, cli); break;
			case PEscolhas: AsDuasEscolhas(srv, cli); break;
			case PControle: OControle(srv, cli); break;
			case PComparar: Comparar(); break;
			case PMosaico: EsperarOMosaico(); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) O CORPO ARMA E APONTA
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// Tres segundos pro mundo assentar: a zona carrega, o `PeerLook` chega (e com ele a COR do ki,
		// que e o que o tiro soma na folha) e a camera para de escorregar.
		if (_t < 3) return;

		// ============================ ELE PRECISA ESTAR DE PE ============================
		// A primeira rodada perdeu o PRIMEIRO tiro com *"voce esta caido"*: um personagem recem-criado
		// entra nocauteado por um instante, e o `PodeAtirar` recusa com razao. Um segundo depois ele
		// levanta sozinho. Esperar o estado e mais honesto que escrever `KO = false`.
		// ============================================================================
		(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
		if ((ko || morto) && _t < 30) return;
		Conferir(!ko && !morto, "o corpo esta de pe e vivo antes do primeiro tiro");

		(int skills, int verbos) = srv.ArmarParaAVariedade(cli.LocalId);
		Conferir(skills > 0 && verbos > 0,
				 $"o corpo aprendeu o catalogo ({skills} skills) e reconhece {verbos} verbos");

		(_, _, double ki, double maxKi, double folego) = srv.EstadoDaVariedade(cli.LocalId);
		Nota($"tanques do corpo: Ki {ki:N0}/{maxKi:N0}, folego {folego:0.#}");

		// O RUMO E ACHADO NO MAPA, e nao escolhido: um tiro que morre numa pedra a dois tiles nunca
		// chega ao ponto do obturador.
		Facing f = srv.RumoLivreDaVariedade(cli.LocalId, 22);
		srv.ApontarParaAVariedade(cli.LocalId, f);
		Vec2 d = MeleeArea.Frente(f);
		_rumo = new Vector2(d.X, d.Y);
		Nota($"o corpo olha pra {f} -- vinte e dois tiles livres nesse rumo, no mapa do servidor");

		// O ALVO MARCADO, a vinte tiles: tres tecnicas do roteiro o exigem, e a vinte tiles ele fica
		// fora do recorte (que pega quatro). Ver `MarcarAlvoDaVariedade`.
		int alvo = srv.MarcarAlvoDaVariedade(cli.LocalId, f, 20);
		Conferir(alvo != 0, "ha um alvo MARCADO a vinte tiles (o que o duplo clique faz)");

		Virar(PFundo);
	}

	// =====================================================================
	// 1) O CHAO DO CENARIO -- duas fotos do recorte VAZIO, uma atras da outra
	// =====================================================================
	/// <summary>
	/// O SEGUNDO CHAO DE RUIDO, medido ANTES de qualquer tiro: duas fotos do mesmo recorte vazio,
	/// separadas por meio segundo. O que diferir aqui e o cenario se mexendo sozinho -- e nao ha tiro
	/// nenhum dentro. A comparacao final adota o MAIOR entre este e o do controle, porque um chao
	/// subestimado deixa passar exatamente o defeito que esta bancada procura.
	/// </summary>
	private void OChaoDoCenario(World mundo)
	{
		if (S is { } s0 && C is { } c0 && _fundoDaVez == null) s0.CravarMeioDiaDaVariedade(c0.LocalId);

		Image? tela = Tela();
		if (tela == null) { Conferir(false, "a janela renderiza (no headless a foto sai vazia)"); Fechar(); return; }

		if (_fundoDaVez == null)
		{
			if (Alvo(mundo) is not { } centro) { Nota("sem corpo local"); Fechar(); return; }
			_recorte = Janela(tela, centro);
			_fundoDaVez = Recortar(tela);
			Gravar(tela, "variedade-0-cena.png", "a cena inteira, antes de qualquer tiro");
			Gravar(_fundoDaVez, "variedade-0-fundo.png", "o RECORTE vazio");
			Nota($"o recorte: {_recorte} (tela {tela.GetWidth()}x{tela.GetHeight()})");
			_t = 0;
			return;
		}

		if (_t < 0.5) return;

		// OS QUATRO LIMIARES, IMPRESSOS. O `Epsilon` adotado tem que ser uma escolha visivel: sem esta
		// linha ele seria uma constante no meio do arquivo que ninguem sabe justificar. Ver la.
		Image agora = Recortar(tela);
		foreach (float eps in (float[])[0.03f, 0.08f, 0.12f, 0.20f])
			Nota($"   ruido do cenario a {eps:0.00}: {Diferentes(agora, _fundoDaVez, eps)} px "
			   + $"de {_recorte.Size.X * _recorte.Size.Y}");

		_chaoDoCenario = Diferentes(agora, _fundoDaVez, Epsilon);
		Nota($"CHAO 1 (cenario): o MESMO recorte VAZIO, duas vezes seguidas, difere em "
		   + $"{_chaoDoCenario} px -- ruido puro, sem tiro dentro (limiar {Epsilon:0.00})");

		_fundoDaVez = null;
		Virar(PRoteiro);
	}

	// =====================================================================
	// 2) A FILA DO ROTEIRO
	// =====================================================================
	private void ApertarOBotao(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O CONTROLE VEM LOGO DEPOIS DO PRIMEIRO TIRO, e nao no fim -- ver `OControle`.
		if (_tomadas.Count == 1 && !_controleFeito) { Virar(PControle); return; }

		if (_iDoRoteiro >= Roteiro.Length) { Virar(PEscolhas); return; }

		(string verbo, string rotulo) = Roteiro[_iDoRoteiro++];
		Pedir(verbo, rotulo, PRoteiro);
	}

	private bool _controleFeito;

	/// <summary>Enfileira um disparo. Quem tira o fundo e aperta o botao e o <see cref="OFundoDaVez"/>.</summary>
	private void Pedir(string verbo, string rotulo, int depois)
	{
		_emCurso = new Tomada { Verbo = verbo, Rotulo = rotulo };
		_depoisDoTiro = depois;
		Virar(PAntesDoTiro);
	}

	// =====================================================================
	// 3) O FUNDO **DESTE** TIRO, E ENTAO O BOTAO
	// =====================================================================
	/// <summary>
	/// A foto do recorte VAZIO um segundo antes de apertar -- ver o cabecalho sobre por que ela e por
	/// tiro e nao uma so pra rodada inteira (resumo: **o cenario deste jogo se destroi**, e cada raio
	/// arranca um pedaco do chao que a foto seguinte encontraria diferente).
	///
	/// O RECORTE **NAO** E RECALCULADO AQUI, e isso e deliberado. Ele e o mesmo retangulo de tela do
	/// comeco ao fim da rodada: as mascaras de todas as tecnicas tem que cair na MESMA grade pra poderem
	/// ser comparadas pixel a pixel, e um retangulo que se reancora a cada foto desloca a mascara em um
	/// ou dois pixels -- o que, num tiro de contorno fino, e diferenca suficiente pra inflar o chao do
	/// ruido acima do proprio sinal. O que se confere aqui e se ele PRECISARIA ter mudado: se precisou,
	/// o corpo saiu da ancora e a bancada diz isso em vez de ficar verde por acaso.
	/// </summary>
	private void OFundoDaVez(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.LimparOsTirosDaVariedade(cli.LocalId);
		srv.RegarOKiDaVariedade(cli.LocalId);
		// DE VOLTA AO MESMO PONTO -- ver `ReporNaAncora`.
		srv.ReporNaAncora(cli.LocalId);

		// ============================ O MEIO-DIA E CRAVADO UMA VEZ SO, NO COMECO ============================
		// A sexta rodada cravava o meio-dia ANTES DE CADA TIRO, e isso saiu pior que a doenca: o
		// `AdminMeioDia` pula pro PROXIMO meio-dia, ou seja um dia inteiro do jogo de cada vez, e a luz
		// nova chega ao cliente pelo tique do ceu (1 Hz) -- ela caia ENTRE a foto do fundo e a foto do
		// tiro, e a primeira mascara saiu com 36.510 px de "tiro" que eram a tela inteira mudando de cor.
		//
		// E o remedio nao era preciso: a mascara ja compara com um fundo de UM SEGUNDO antes, e um
		// segundo de relogio do mundo nao muda pixel nenhum (o ruido do cenario mede ZERO). Quem sofria
		// com a luz andando era so o CONTROLE, que ficava a seis minutos do original -- e a resposta
		// pra isso foi mover o controle pra logo depois do primeiro tiro. Ver `OControle`.
		// ==============================================================================================

		// Um segundo pro tiro anterior sumir da tela (o pacote de morte, o estouro e a poeira).
		if (_t < 1.0) return;

		Image? tela = Tela();
		if (tela == null) { Conferir(false, "a janela renderiza"); Fechar(); return; }

		if (Alvo(mundo) is { } centro)
		{
			var daria = Janela(tela, centro);
			int deslize = Math.Abs(daria.Position.X - _recorte.Position.X)
						+ Math.Abs(daria.Position.Y - _recorte.Position.Y);
			if (deslize > 1)
				Nota($"{_emCurso?.Rotulo}: o corpo saiu da ancora em {deslize} px "
				   + $"-- {daria.Position} contra {_recorte.Position}");
		}

		// A TELA INTEIRA, e nao o recorte: o retangulo definitivo so e escolhido DEPOIS, quando se
		// sabe onde a cabeca do tiro parou (ver `EsperarOTiro`). Guardando a tela toda, o fundo e o
		// tiro podem ser recortados no MESMO lugar, seja ele qual for.
		_telaDeFundo = tela;

		string resposta = srv.GatilhoDaVariedade(cli.LocalId, _emCurso?.Verbo ?? "");
		if (resposta.Length > 0) Nota($"{_emCurso?.Rotulo}: o servidor respondeu \"{resposta}\"");

		_tDoTiro = 0;
		Virar(PEsperar);
	}

	// =====================================================================
	// 4) ESPERA A CABECA ANDAR, E FOTOGRAFA
	// =====================================================================
	private void EsperarOTiro(World mundo, Jandirus.Server.GameServer srv, GameClient cli, double delta)
	{
		_tDoTiro += delta;

		(bool achou, int idDoTiro, ArteDeKi arte, TipoDeProjetil tipo, _, double andou, float escala)
			= srv.TiroDaVariedade(cli.LocalId);

		if (_tDoTiro > PacienciaPorTiro)
		{
			Conferir(false, $"{_emCurso?.Rotulo}: o tiro saiu e andou {TilesDoObturador:0} tiles "
						  + $"(achou={achou}, andou={andou:0.0})");
			Adiante(srv, cli);
			return;
		}

		if (!achou || andou < TilesDoObturador) return;

		// O NODE TEM QUE EXISTIR NA TELA -- e nao basta o servidor achar que mandou. Este e o elo que a
		// `--diagartedeki` nao atravessa: um `ushort` perdido no anuncio de nascimento cairia
		// exatamente aqui, com o projetil vivo no servidor e nada desenhado.
		ArteDeKi noCliente = ArteDeKi.Nenhuma;
		bool desenhado = false;
		Vector2 cabecaNaTela = Vector2.Zero;
		foreach ((int id, ArteDeKi a, TipoDeProjetil _, Vector2 onde, float _) in mundo.TirosDesenhados())
			if (id == idDoTiro) { noCliente = a; desenhado = true; cabecaNaTela = NaTela(onde); }

		// UM QUADRO DE FOLGA pro node nascer: o anuncio chega pelo canal confiavel e o `AoNascerTiro`
		// roda na leitura do pacote, que pode cair depois do `_Process` desta bancada.
		if (!desenhado && _tDoTiro < PacienciaPorTiro - 1) return;

		Image? tela = Tela();
		if (tela == null) { Conferir(false, "a janela renderiza"); Fechar(); return; }

		// ============================ O RECORTE E CENTRADO NA CABECA DO TIRO ============================
		// A quinta rodada mostrou por que: o obturador dispara no PRIMEIRO quadro em que a cabeca passou
		// dos quatro tiles, e cada quadro anda meio tile -- ou seja o tiro para em qualquer ponto de uma
		// faixa de uns dezesseis pixels. Num recorte de tela fixa, isso desloca o desenho, e um raio
		// FINO deslocado oito pixels nao se sobrepoe a si mesmo: as duas fotos do controle (o MESMO Ki
		// Wave) discordavam em 2.192 px so por causa disso, o que e mais tinta do que a Bola de Ki
		// inteira tem.
		//
		// Centrando na cabeca, o desenho cai sempre na mesma grade. O CENARIO e que sai de lugar entre
		// uma foto e outra -- e ele nao esta em julgamento: o fundo e recortado no MESMO retangulo,
		// tirado da tela inteira guardada um segundo antes, entao a mascara continua limpa.
		// ==========================================================================================
		_recorte = Janela(tela, desenhado ? cabecaNaTela : Alvo(mundo) ?? Vector2.Zero);

		Tomada t = _emCurso ?? new Tomada();
		t.Recorte = Recortar(tela);
		t.Fundo = _telaDeFundo == null ? null : Recortar(_telaDeFundo);
		t.ArteNoCliente = noCliente;
		t.ArteNoServidor = arte;
		t.Tipo = tipo;
		t.AndouTiles = andou;
		t.Escala = escala;
		t.Desenhado = desenhado;

		// O TIRO MORRE AQUI, e nao depois: o que vier a seguir e a foto do cenario COM o estrago que
		// este tiro ja fez, e nenhum estrago novo pode entrar nela.
		srv.LimparOsTirosDaVariedade(cli.LocalId);
		Virar(PDepoisDoTiro);
	}

	// =====================================================================
	// 5) O FUNDO **DEPOIS** DO TIRO -- e ele e o que faz a mascara ser o tiro
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE SAO DOIS FUNDOS, E NAO UM ============================
	/// **O tiro CAVA o chao enquanto voa.** A setima rodada mostrou isso num par que devia ser gemeo: as
	/// duas fotos do MESMO Ki Wave, disparadas com quinze segundos de intervalo, discordavam em 60% --
	/// mais do que o Kamehameha discorda do Galick Ho. Olhando as fotos, o feixe estava no mesmo lugar,
	/// do mesmo tamanho e da mesma cor nas duas; o que mudava era o CHAO: um bloco de grama que virou
	/// areia entre a foto do fundo e a do tiro. Aquilo entrava na mascara como se fosse desenho de ki.
	///
	/// Com um segundo fundo tirado DEPOIS (e com o tiro ja recolhido), o estrago aparece nos dois lados
	/// da conta e sai dela. A mascara passa a ser o que difere do fundo de ANTES **e** do de DEPOIS --
	/// ou seja, o que so existia enquanto o tiro estava na tela.
	/// ======================================================================================
	/// </summary>
	private void OFundoDepois(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.ReporNaAncora(cli.LocalId);
		if (_t < 1.0) return;   // o tempo de o pacote de morte chegar e o node sumir

		Tomada? t = _emCurso;
		if (t?.Recorte == null) { Adiante(srv, cli); return; }

		Image? tela = Tela();
		Image? depois = tela == null ? null : Recortar(tela);

		t.Mascara = Mascara(t.Recorte, t.Fundo, depois);
		t.PixelsDoTiro = Contar(t.Mascara);
		_tomadas.Add(t);

		Gravar(t.Recorte, $"variedade-{_tomadas.Count:00}-{Arquivavel(t.Verbo)}.png",
			   $"{t.Rotulo}: {ArteDeKiNoCliente.Rotulo(t.ArteNoCliente)}, {t.PixelsDoTiro} px de tiro");

		Conferir(t.Desenhado && t.ArteNoCliente == t.ArteNoServidor,
			$"{t.Rotulo}: a arte do SERVIDOR ({t.ArteNoServidor}) e a que o CLIENTE vestiu "
			+ $"({t.ArteNoCliente}) sao a mesma -- o `ushort` do nascimento atravessou o fio");
		Conferir(t.PixelsDoTiro > 150,
			$"{t.Rotulo}: o tiro DESENHOU no recorte ({t.PixelsDoTiro} px que nem o fundo de antes nem "
			+ "o de depois tinham)");

		Adiante(srv, cli);
	}

	/// <summary>Fecha o tiro da vez e volta pro passo que pediu este disparo.</summary>
	private void Adiante(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.LimparOsTirosDaVariedade(cli.LocalId);
		_emCurso = null;
		_fundoDaVez = null;
		_telaDeFundo = null;
		Virar(_depoisDoTiro);
	}

	// =====================================================================
	// 5) AS DUAS ESCOLHAS DO JOGADOR, NA MESMA TECNICA
	// =====================================================================
	private void AsDuasEscolhas(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_escolhasFeitas)
		{
			case 0:
				_verboDaInventada = srv.InventarTecnicaComArte(
					cli.LocalId, TipoDeProjetil.Beam, EscolhaA, "Escolha do jogador", out string relato);
				_slotDaInventada = Jandirus.Core.Skills.TecnicaCustomizada.IdDoVerbo(_verboDaInventada);

				Conferir(_verboDaInventada.Length > 0,
					$"o jogador inventou uma tecnica e ESCOLHEU a folha {EscolhaA} pelos botoes da mesa");
				if (relato.Length > 0) Nota("a mesa respondeu: " + relato);
				if (_verboDaInventada.Length == 0) { Virar(PComparar); return; }

				_escolhasFeitas = 1;
				Pedir(_verboDaInventada, $"Inventada ({EscolhaA})", PEscolhas);
				return;

			case 1:
				// SEGUNDA ESCOLHA, MESMA TECNICA: o jogador reabre a mesa e troca a folha.
				bool trocou = srv.TrocarArteDaInventada(cli.LocalId, _slotDaInventada, EscolhaB);
				Conferir(trocou, $"...e depois MUDOU DE IDEIA pra {EscolhaB}, na MESMA tecnica");
				_escolhasFeitas = 2;
				if (!trocou) { Virar(PComparar); return; }

				Pedir(_verboDaInventada, $"Inventada ({EscolhaB})", PEscolhas);
				return;

			default:
				srv.EsquecerAsInventadasDaVariedade(cli.LocalId);
				Virar(PComparar);
				return;
		}
	}

	// =====================================================================
	// 6) O CONTROLE -- a primeira tecnica que deu certo, de novo
	// =====================================================================
	/// <summary>
	/// O CHAO DO RUIDO DO DESENHO. Duas mascaras da MESMA tecnica, no mesmo lugar e no mesmo instante
	/// geometrico, nao saem identicas: a folha anima, a particula sai diferente. Esse numero e quanto
	/// duas fotos podem diferir SEM que a arte tenha mudado.
	///
	/// A REPETIDA E A PRIMEIRA QUE DEU CERTO, e nao a primeira do roteiro -- a primeira rodada desta
	/// bancada repetiu a `Roteiro[0]` (que naquela vez tinha FALHADO) e acabou comparando duas tecnicas
	/// DIFERENTES como se fossem a mesma: o chao saiu tres vezes maior que qualquer par de verdade e os
	/// 28 pares foram declarados "parecidos demais".
	///
	/// ============================ E ELE SAI LOGO DEPOIS DELA, E NAO NO FIM DA RODADA ============================
	/// Um dia do jogo dura 24 minutos e a rodada leva seis: um controle tirado no fim esta a um quarto de
	/// dia de luz do original. Foi assim na sexta rodada, e o resultado foi um chao de 94% -- as duas
	/// fotos do MESMO Ki Wave discordando em quase tudo, porque com o sol mais alto o contraste caiu e a
	/// mascara encolheu de 36.510 px pra 2.231. Um chao desses aprova qualquer coisa.
	///
	/// Colado no original, o controle mede o que ele existe pra medir: a variacao do DESENHO (o quadro
	/// da animacao, o meio-tile de sobra do obturador), com tudo o mais igual.
	/// ========================================================================================================
	/// </summary>
	private void OControle(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_tomadas.Count == 0 || _controleFeito) { Virar(PRoteiro); return; }

		_controleFeito = true;
		Tomada primeira = _tomadas[0];
		Pedir(primeira.Verbo, "CONTROLE: " + primeira.Rotulo, PRoteiro);
	}

	// =====================================================================
	// 7) A COMPARACAO
	// =====================================================================
	private void Comparar()
	{
		_linhas.Add("=== O QUE AS FOTOS DIZEM, LADO A LADO ===");

		List<Tomada> t0 = _tomadas.FindAll(x => x.Mascara != null);
		Nota($"(o cenario VAZIO, fotografado duas vezes, diferiu em {_chaoDoCenario} px -- ver o limiar)");

		Conferir(t0.Count >= 15, $"deu pra fotografar {t0.Count} tiros disparados de verdade");

		// AS FOLHAS DISTINTAS QUE CHEGARAM NA TELA -- a resposta numerica do pedido do dono.
		var folhas = new HashSet<ArteDeKi>();
		foreach (Tomada t in t0) folhas.Add(t.ArteNoCliente);
		Conferir(folhas.Count >= 15,
			$"os tiros fotografados vestiram {folhas.Count} folhas DISTINTAS do BYOND "
			+ "(antes desta funcionalidade era UMA, e o desenho era por primitiva)");

		// ============================ A LINHA QUE REPROVA SE DOIS TIPOS DESENHAREM IGUAL ============================
		// Todos contra todos, mascara contra mascara: o cenario ja saiu da conta dos dois lados, e o que
		// resta e forma e cor do tiro.
		//
		// E O LIMIAR NAO E UM NUMERO ESCRITO AQUI -- ele e MEDIDO na propria rodada, e por isso esta
		// bancada nao tem constante pra alguem afrouxar no dia em que ficar vermelha. Os pares se
		// separam sozinhos em duas familias:
		//
		//   * MESMA FOLHA -- o controle (a primeira tecnica repetida) e qualquer par que caia na mesma
		//     arte por direito (o Kamehameha sorteado e a tecnica inventada podem coincidir, e na sexta
		//     rodada coincidiram: 3,6% de discordancia). Aqui esta o quanto duas fotos do MESMO desenho
		//     diferem.
		//   * FOLHAS DIFERENTES -- todo o resto.
		//
		// A afirmacao e que as duas familias NAO SE TOCAM: o par de folhas diferentes mais parecido tem
		// que discordar mais que o par de mesma folha mais discordante. Se um dia duas tecnicas passarem
		// a desenhar igual, elas caem na faixa da primeira familia e esta linha fica vermelha sem que
		// ninguem tenha que escolher um numero.
		// ====================================================================================================
		double piorIgual = 0, melhorDiferente = 2;
		string parIgual = "", parDiferente = "";
		int pares = 0, iguais = 0;

		for (int i = 0; i < t0.Count; i++)
			for (int j = i + 1; j < t0.Count; j++)
			{
				pares++;
				double d = Divergencia(t0[i], t0[j]);
				string nome = $"{t0[i].Rotulo} x {t0[j].Rotulo}";

				if (t0[i].ArteNoCliente == t0[j].ArteNoCliente)
				{
					iguais++;
					Nota($"MESMA FOLHA ({t0[i].ArteNoCliente}): {nome} -- discordam em {d:P1}");
					// `>=` e nao `>`: com um par so, e ele discordando em ZERO (que e o resultado bom),
					// o `>` nunca dispara e o relatorio sai com o nome do par em branco.
					if (d >= piorIgual) { piorIgual = d; parIgual = nome; }
				}
				else if (d < melhorDiferente) { melhorDiferente = d; parDiferente = nome; }
			}

		Nota($"{pares} pares medidos, {iguais} deles de MESMA folha");
		Nota($"o par de MESMA folha que mais discorda: {parIgual} -- {piorIgual:P1}");
		Nota($"o par de folhas DIFERENTES que menos discorda: {parDiferente} -- {melhorDiferente:P1}");

		Conferir(iguais > 0, "houve pelo menos um par de MESMA folha pra dar escala a comparacao "
						   + "(o controle: a primeira tecnica disparada duas vezes)");
		Conferir(melhorDiferente > piorIgual,
			$"as duas familias NAO SE TOCAM: folha diferente comeca em {melhorDiferente:P1} e folha "
			+ $"igual termina em {piorIgual:P1} -- {melhorDiferente - piorIgual:P1} de folga");

		// ---- as duas escolhas do jogador, na MESMA tecnica
		List<Tomada> escolhas = _tomadas.FindAll(x => x.Rotulo.StartsWith("Inventada", StringComparison.Ordinal));
		if (escolhas.Count == 2)
			Conferir(Divergencia(escolhas[0], escolhas[1]) > piorIgual,
				$"as DUAS ESCOLHAS do jogador no MESMO verbo desenham diferente "
				+ $"(discordam em {Divergencia(escolhas[0], escolhas[1]):P1}, contra {piorIgual:P1} de "
				+ $"duas fotos do mesmo desenho): {escolhas[0].ArteNoCliente} x {escolhas[1].ArteNoCliente}");
		else Nota($"as duas escolhas do jogador nao foram fotografadas ({escolhas.Count} de 2)");

		// ---- a tabela, tecnica por tecnica
		_linhas.Add("=== A TABELA, LIDA DAS FOTOS ===");
		foreach (Tomada t in _tomadas)
			Nota($"{t.Rotulo,-26} {ArteDeKiNoCliente.Rotulo(t.ArteNoCliente),-34} "
			   + $"{t.Tipo,-7} escala {t.Escala:0.0}x  andou {t.AndouTiles:0.0}t  "
			   + $"{t.PixelsDoTiro,6} px de tiro");

		Montar();
		Virar(PMosaico);
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// A MASCARA DO TIRO: onde o recorte com tiro difere do recorte vazio do mesmo instante.
	///
	/// E ela que faz esta bancada sobreviver a um cenario DESTRUTIVEL. Comparar dois recortes crus mede
	/// o chao arrancado entre um disparo e o outro; comparar mascaras mede o tiro.
	/// </summary>
	private static bool[] Mascara(Image tiro, Image? antes, Image? depois)
	{
		int w = tiro.GetWidth(), h = tiro.GetHeight();
		var m = new bool[w * h];
		if (antes == null && depois == null) return m;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color p = tiro.GetPixel(x, y);
				// **E**, e nao **OU**: o chao que o tiro cavou difere do fundo de ANTES e nao do de
				// DEPOIS, entao ele cai fora. Ver o cabecalho do `OFundoDepois`.
				bool vale = true;
				if (antes != null && Dentro(antes, x, y)) vale &= Difere(p, antes.GetPixel(x, y));
				if (depois != null && Dentro(depois, x, y)) vale &= Difere(p, depois.GetPixel(x, y));
				m[y * w + x] = vale;
			}
		return m;
	}

	private static bool Dentro(Image img, int x, int y) => x < img.GetWidth() && y < img.GetHeight();

	private static bool Difere(Color p, Color q, float eps = Epsilon)
		=> Mathf.Abs(p.R - q.R) > eps || Mathf.Abs(p.G - q.G) > eps || Mathf.Abs(p.B - q.B) > eps;

	private static int Contar(bool[]? m)
	{
		if (m == null) return 0;
		int n = 0;
		foreach (bool b in m) if (b) n++;
		return n;
	}

	/// <summary>
	/// QUANTO DUAS TOMADAS DIFEREM -- em FORMA e em COR.
	///
	/// Um pixel conta quando so uma das duas tem tiro ali (forma), ou quando as duas tem e a cor nao
	/// bate (cor). So a forma nao bastaria: duas folhas com a mesma silhueta e paletas diferentes
	/// passariam por iguais, e a cor e metade do que o BYOND faz aqui (`icon += rgb()`).
	/// </summary>
	private static (int Discorda, int Uniao) Distancia(Tomada a, Tomada b)
	{
		if (a.Mascara == null || b.Mascara == null || a.Recorte == null || b.Recorte == null)
			return (int.MaxValue, 1);

		int w = Math.Min(a.Recorte.GetWidth(), b.Recorte.GetWidth());
		int h = Math.Min(a.Recorte.GetHeight(), b.Recorte.GetHeight());
		int n = 0, uniao = 0;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				bool ta = Tem(a, x, y), tb = Tem(b, x, y);
				if (ta || tb) uniao++;

				// ============================ UM PIXEL DE FOLGA, E ELE E O PONTO ============================
				// O obturador dispara no primeiro quadro em que a cabeca passou dos quatro tiles, e cada
				// quadro anda meio tile: o desenho para em qualquer ponto de uma faixa de alguns pixels.
				// Num feixe FINO (o Ki Wave tem uns seis pixels de largura) um deslocamento de um pixel ja
				// faz um terco da forma "nao coincidir" -- e foi exatamente isso que a oitava rodada
				// mediu: as duas fotos do MESMO Ki Wave discordavam em 21,5%, mais do que o Galick Ho
				// discorda do Kamehameha5 (16,7%). O chao ficava acima do sinal.
				//
				// Com a vizinhanca de um pixel, "esta tinta tem correspondente do outro lado?" para de
				// depender de meio quadro de sorte, e uma forma REALMENTE diferente continua sem
				// correspondente -- que e o que se quer perguntar.
				// ======================================================================================
				if (ta && !Perto(b, x, y)) { n++; continue; }
				if (tb && !Perto(a, x, y)) { n++; continue; }

				// A COR, tambem com a folga: so conta como discordancia se a cor daqui nao bate com
				// NENHUMA das cores do outro lado ali perto. Sem a cor, duas folhas de mesma silhueta e
				// paletas diferentes passariam por iguais -- e a cor e metade do que o BYOND faz aqui
				// (`icon += rgb()`).
				if (ta && tb && !CorPerto(a, b, x, y)) n++;
			}
		return (n, Math.Max(uniao, 1));
	}

	private static bool Tem(Tomada t, int x, int y)
		=> t.Mascara != null && t.Recorte != null
		   && x >= 0 && y >= 0 && x < t.Recorte.GetWidth() && y < t.Recorte.GetHeight()
		   && t.Mascara[y * t.Recorte.GetWidth() + x];

	/// <summary>Ha tinta desta tomada em algum dos nove pixels em volta (incluindo o proprio)?</summary>
	private static bool Perto(Tomada t, int x, int y)
	{
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if (Tem(t, x + dx, y + dy)) return true;
		return false;
	}

	/// <summary>A cor de `a` neste ponto bate com a de algum pixel COM TINTA de `b` ali perto?</summary>
	private static bool CorPerto(Tomada a, Tomada b, int x, int y)
	{
		Color c = a.Recorte!.GetPixel(x, y);
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if (Tem(b, x + dx, y + dy) && !Difere(c, b.Recorte!.GetPixel(x + dx, y + dy)))
					return true;
		return false;
	}

	/// <summary>
	/// ============================ A MEDIDA E UMA RAZAO, E NAO UMA CONTAGEM ============================
	/// Quanto da tinta envolvida DISCORDA: `pixels que discordam / pixels que qualquer um dos dois
	/// pintou`. Zero quer dizer "o mesmo desenho"; um quer dizer "nao tem um pixel em comum".
	///
	/// A quinta rodada provou por que a contagem crua nao serve: o Final Flash pinta 36.864 pixels e a
	/// Esfera Teleguiada pinta 304. Um limiar unico em pixel ou reprova a Esfera contra tudo, ou aprova
	/// qualquer par de raios. E foi o que aconteceu -- a Bola de Ki e a Esfera Teleguiada, que sao
	/// desenhos OBVIAMENTE diferentes (uma gota e um circulo com anel tracejado), discordavam em 340 px
	/// e cairam abaixo de um chao de 2.192 herdado de um raio.
	///
	/// Em razao, as duas discordam em quase toda a tinta que tem -- que e a leitura certa.
	/// ============================================================================================
	/// </summary>
	private static double Divergencia(Tomada a, Tomada b)
	{
		(int discorda, int uniao) = Distancia(a, b);
		return discorda == int.MaxValue ? 1 : (double)discorda / uniao;
	}

	/// <summary>QUANTOS PIXELS DIFEREM entre dois recortes crus. So o chao do cenario usa isto.</summary>
	private static int Diferentes(Image a, Image b, float eps)
	{
		int w = Math.Min(a.GetWidth(), b.GetWidth()), h = Math.Min(a.GetHeight(), b.GetHeight());
		int n = 0;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				if (Difere(a.GetPixel(x, y), b.GetPixel(x, y), eps)) n++;
		return n;
	}

	private Image Recortar(Image tela)
	{
		Image r = tela.GetRegion(_recorte);
		r.Convert(Image.Format.Rgba8);
		return r;
	}

	/// <summary>
	/// ONDE, NA TELA, O TIRO VAI ESTAR NO INSTANTE DO OBTURADOR: a posicao DESENHADA do corpo mais
	/// quatro tiles no rumo do disparo.
	///
	/// Sai da posicao desenhada e nao da do servidor porque o recorte e de TELA: a camera interpola, e
	/// um recorte ancorado na conta do servidor escorregaria um ou dois pixels a cada foto.
	/// </summary>
	private Vector2? Alvo(World mundo)
		=> mundo.PosicaoLocal is { } eu
			? NaTela(eu + _rumo * (float)(TilesDoObturador * ZoneCollision.TileSize))
			: null;

	/// <summary>A janela do recorte, empurrada pra DENTRO da tela -- nunca cortada.</summary>
	private static Rect2I Janela(Image tela, Vector2 centro)
	{
		int lado = Math.Min(Lado, Math.Min(tela.GetWidth(), tela.GetHeight()));
		int x0 = Math.Clamp((int)centro.X - lado / 2, 0, tela.GetWidth() - lado);
		int y0 = Math.Clamp((int)centro.Y - lado / 2, 0, tela.GetHeight() - lado);
		return new Rect2I(x0, y0, lado, lado);
	}

	private Vector2 NaTela(Vector2 mundo)
		=> (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private void Gravar(Image img, string nome, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private static string Arquivavel(string s)
	{
		var sb = new System.Text.StringBuilder();
		foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
		return sb.ToString();
	}

	// =====================================================================
	// O MOSAICO
	// =====================================================================
	/// <summary>
	/// TODAS AS TECNICAS NUMA IMAGEM SO, com o nome debaixo de cada uma.
	///
	/// E o formato do pedido e nao enfeite: "se dois sairem iguais, a variedade nao esta ligada" e uma
	/// leitura de OLHO, e vinte e tres arquivos separados obrigam quem le a abrir vinte e tres janelas e
	/// comparar de cabeca. Os recortes individuais continuam gravados -- um mosaico e sempre uma escolha
	/// de quem montou.
	///
	/// ELE E DESENHADO COM NODES E FOTOGRAFADO, e nao colado com `BlitRect`, por um motivo so: assim ele
	/// tem RÓTULO. Uma grade de vinte e tres quadradinhos sem nome nao responde "qual e qual".
	/// </summary>
	private void Montar()
	{
		if (_tomadas.Count == 0) return;

		var camada = new CanvasLayer { Layer = 200, Name = "MosaicoDaVariedade" };
		AddChild(camada);

		Vector2 tela = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1600, 900);
		camada.AddChild(new ColorRect
		{
			Color = new Color(0.05f, 0.05f, 0.07f),
			Size = tela + new Vector2(16, 16),
			Position = new Vector2(-8, -8),
		});

		int colunas = Mathf.CeilToInt(Mathf.Sqrt(_tomadas.Count * 1.7f));
		int linhas = Mathf.CeilToInt(_tomadas.Count / (float)colunas);
		const int Rot = 15;
		float lado = Mathf.Min((tela.X - 16) / colunas - 6, (tela.Y - 16) / linhas - 6 - Rot);

		for (int i = 0; i < _tomadas.Count; i++)
		{
			Tomada t = _tomadas[i];
			if (t.Recorte == null) continue;

			var canto = new Vector2(8 + i % colunas * (lado + 6), 8 + i / colunas * (lado + 6 + Rot));

			camada.AddChild(new TextureRect
			{
				Texture = ImageTexture.CreateFromImage(t.Recorte),
				Position = canto,
				Size = new Vector2(lado, lado),
				StretchMode = TextureRect.StretchModeEnum.Scale,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			});

			var nome = new Label
			{
				Text = t.Rotulo,
				Position = canto + new Vector2(0, lado),
				Size = new Vector2(lado, Rot),
				ClipText = true,
			};
			nome.AddThemeFontSizeOverride("font_size", 11);
			camada.AddChild(nome);
		}

		_mosaico = camada;
	}

	/// <summary>
	/// O MOSAICO SO EXISTE NO QUADRO SEGUINTE (os nodes ainda nao desenharam). Sem esta espera a foto
	/// sai preta -- e sem erro nenhum, que e o jeito mais caro de descobrir.
	/// </summary>
	private void EsperarOMosaico()
	{
		if (_mosaico == null) { Fechar(); return; }
		if (_quadrosDoMosaico++ < 4) return;

		Image? tela = Tela();
		if (tela != null) Gravar(tela, "variedade-mosaico.png", "O MOSAICO: todas as tecnicas lado a lado");
		else Nota("o mosaico nao pode ser fotografado");

		_mosaico.QueueFree();
		_mosaico = null;
		Fechar();
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		if (S is { } srv && C is { } cli)
		{
			srv.EsquecerAsInventadasDaVariedade(cli.LocalId);
			srv.LimparOsTirosDaVariedade(cli.LocalId);
			srv.LimparAFoto();   // o alvo marcado sai do mundo pelo mesmo caminho das outras bancadas
		}

		GD.Print("\n[variedade] ===== A VARIEDADE DOS ATAQUES DE KI, FOTOGRAFADA =====");
		foreach (string l in _linhas) GD.Print("[variedade] " + l);
		GD.Print(_falhas.Count == 0
			? "[variedade] ===== TUDO OK ====="
			: $"[variedade] ===== {_falhas.Count} FALHA(S) =====\n[variedade]   "
			  + string.Join("\n[variedade]   ", _falhas));
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
