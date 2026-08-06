using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO VOO (`--diagvoo`).
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// Olhando pra tela da pra ver que o boneco subiu. O que a foto NAO diz:
///   * o Ki de decolagem foi o do DM (`80/flightability`), ou um numero que eu inventei?
///   * o dreno por segundo bate com `max(35/hab + 450*rapido/hab, 1) x 6`?
///   * la em cima o corpo REALMENTE atravessa parede -- ou so parece, porque nao passou por uma?
///   * o teto RECUSA quem nao tem os portoes do Space_Flight? (teto que nunca recusa = teto nenhum)
///   * ficar sem Ki DERRUBA, ou o corpo fica pairando de graca?
///   * o veu, a nevoa e o zoom respondem a altura, ou ficaram escritos e nunca ligados?
/// ==================================================================================
///
/// COMO RODAR (uma janela so; o servidor sobe junto pelo `--host`):
///     Godot --path . --host --vooteste --diagvoo --nome Voador --conta voo
///
/// O `--vooteste` da a skill de voo no nivel 2 -- sem ela nao ha o que medir, porque a skill so
/// sobe de nivel com horas de jogo.
/// </summary>
public partial class RoboDeVoo : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _passo;
	private double _t;

	private double _kiAntesDeDecolar;
	private double _kiNoInicioDaMedida;
	private double _segundosDeMedida;
	private float _alturaMaxVista;
	private Vector2 _ondeSubiu;
	private Vector2? _paredeAlvo;
	private Vector2 _rumoDaParede;
	private Vector2 _deOndeAndou;
	private bool _entrouNoChao, _entrouVoando;
	private ZoneKey _zonaDoTeto;
	private bool _avisoDoTeto;

	/// <summary>
	/// O MENOR Ki QUE APARECEU, lido todo quadro.
	///
	/// Ler o Ki no fim nao prova nada: a queda por exaustao acontece num instante (`Ki &lt;= 5`) e a
	/// regeneracao passiva ja o trouxe de volta pra ~8 quando a bancada foi olhar. Foi o que a
	/// primeira rodada mediu -- 8,2 contra um limiar de 5 --, e reprovou uma regra que funcionou.
	/// </summary>
	private double _kiMinimo = double.MaxValue;

	/// <summary>
	/// AS REGRAS DE ANDAR, conferidas uma a uma contra o que o dono ditou.
	///
	/// Sao funcoes PURAS do Core -- nao precisam de rede, de mapa nem de dois jogadores. Testar isto
	/// com dois corpos de verdade custaria uma segunda janela e daria a MESMA resposta; o que dois
	/// corpos provariam a mais e que o combate CHAMA estas funcoes, e isso o teste da parede ja
	/// mostra pro caminho de colisao.
	/// </summary>
	private void ConferirAndares()
	{
		Conferir(Voo.Andar(0f) == 0, "chao e o andar 0");
		Conferir(Voo.Andar(Voo.AlturaDePairar) == 1, "a altura de pairar e o andar 1");
		Conferir(Voo.Andar(Voo.AlturaMaxima) == Voo.Andares, $"o teto e o andar {Voo.Andares}");

		Conferir(Voo.PodeAcertar(1, 0), "quem paira rasante ALCANCA quem esta no chao");
		Conferir(!Voo.PodeAcertar(0, 1), "...e quem esta no chao NAO alcanca de volta (a regra e assimetrica)");
		Conferir(!Voo.PodeAcertar(2, 0) && !Voo.PodeAcertar(3, 0), "de alto demais nao se alcanca o chao");
		Conferir(Voo.PodeAcertar(2, 2), "dois no MESMO andar se alcancam");
		Conferir(!Voo.PodeAcertar(2, 3) && !Voo.PodeAcertar(3, 2), "dois voando em andares DIFERENTES nao");

		Conferir(Voo.Enxerga(0, 1) && Voo.Enxerga(1, 0), "quem paira rasante ainda e VISTO de baixo");
		Conferir(!Voo.Enxerga(0, 2) && !Voo.Enxerga(0, 3), "quem voa alto SOME pra quem esta no chao");
		Conferir(Voo.Enxerga(3, 3), "...e so quem sobe junto continua vendo");
	}

	/// <summary>Quantos pixels separam o DESENHO do corpo da posicao do no.</summary>
	private static float Vao(World mundo, GameClient cli)
	{
		if (mundo.PosicaoLocal is not { } no) return 0f;
		if (mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } desenho) return 0f;
		return no.DistanceTo(desenho);
	}

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	/// <summary>Salva a tela, se houver renderizador. No headless o `GetImage` volta vazio.</summary>
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add("  ok     foto salva em " + caminho);
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}

	public override void _Ready()
	{
		// O AVISO DO TETO E UMA LINHA DE CHAT, e e a unica prova de que o portao RECUSOU (em vez de
		// simplesmente nao ter chegado la em cima). Escutar o canal de sistema e o jeito de a
		// bancada ver o mesmo que o jogador ve.
		if (GameClient.Instance is { } cli) cli.Falou += AoOuvir;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.Falou -= AoOuvir;
	}

	private void AoOuvir(Jandirus.Net.Protocol.Fala canal, string quem, string texto)
	{
		if (texto.Contains("limite da atmosfera")) _avisoDoTeto = true;
	}

	public override void _Process(double delta)
	{
		if (_acabou || GameClient.Instance is not { Connected: true } cli) return;
		if (World.Instancia is not { } mundo) return;

		_alturaMaxVista = Math.Max(_alturaMaxVista, mundo.AlturaDeTeste);
		if (_passo > 17) _kiMinimo = Math.Min(_kiMinimo, cli.Sheet.Ki);   // so na fase da exaustao

		// ============================ A PROVA E POR QUADRO, NAO POR FOTO ============================
		// "Entrou em parede alguma vez?" so se responde OLHANDO SEMPRE: o corpo atravessa um muro de
		// um tile em uma fracao de segundo, e a leitura de um passo por segundo passaria batido nele
		// nove vezes em dez -- o teste diria "nao atravessou" com o voo funcionando.
		// ============================================================================================
		if (mundo.DentroDeParedeDeTeste)
		{
			if (_passo <= 7) _entrouNoChao = true;
			else if (_passo <= 13) _entrouVoando = true;
		}

		_t += delta;
		if (_t < 1.0) return;
		double dt = _t;
		_t = 0;

		switch (_passo++)
		{
			case 0:
			{
				// ---------- no chao ----------
				Conferir(mundo.AlturaDeTeste == 0f, "comeca no chao (altura 0)");
				Conferir(mundo.NevoaDeTeste < 0.01f, "sem nevoa no chao");
				Conferir(Vao(mundo, cli) < 1f,
					$"NO CHAO o desenho e o no coincidem (vao {Vao(mundo, cli):0.0} px)");
				ConferirAndares();

				// ============================ O CONTROLE, NO CHAO ============================
				// Provar que VOANDO se atravessa parede nao prova nada sozinho: se a colisao
				// estivesse quebrada pra todo mundo, o resultado seria exatamente o mesmo. O que
				// da sentido ao teste e a comparacao -- o MESMO corpo, o MESMO rumo, a MESMA
				// parede, uma vez com os pes no chao e outra no ar.
				//
				// Entao primeiro ele anda contra a parede andando, e o esperado e BATER nela.
				// ============================================================================
				_ondeSubiu = mundo.PosicaoLocal ?? Vector2.Zero;
				_paredeAlvo = mundo.ParedeMaisPertoDeTeste(_ondeSubiu);
				Conferir(_paredeAlvo != null,
					_paredeAlvo is { } pa
						? $"achou uma parede pra atravessar, a {pa.DistanceTo(_ondeSubiu):0} px"
						: "achou uma parede pra atravessar");
				_rumoDaParede = ((_paredeAlvo ?? _ondeSubiu + Vector2.Right * 100f) - _ondeSubiu).Normalized();
				_deOndeAndou = _ondeSubiu;
				mundo.AndarDeTeste(_rumoDaParede);
				break;
			}

			case 1:
			case 2:
			case 3:
			case 4:
			case 5:
			case 6:
				break;   // andando ate esbarrar

			case 7:
			{
				mundo.PararDeTeste();
				Vector2 parou = mundo.PosicaoLocal ?? _deOndeAndou;
				Conferir(!_entrouNoChao,
					$"NO CHAO a parede BARRA: andou {parou.DistanceTo(_deOndeAndou):0} px contra ela "
					+ "e NUNCA ficou dentro de celula bloqueada");

				_kiAntesDeDecolar = cli.Sheet.Ki;
				cli.SendHabilidade("voar");
				break;
			}

			case 8:
			{
				// ---------- o custo de decolagem, contra a formula do DM ----------
				// `flying.dm:86` -- 80/flightability, sem Superflight. A bancada nasce com
				// flightability 75 (nivel 2), entao o esperado e 80/75 = 1,0667.
				double esperado = Voo.CustoParaLigar(Voo.HabilidadeNivel1, false);
				double cobrado = _kiAntesDeDecolar - cli.Sheet.Ki;
				// a folga cobre o dreno do proprio tique de voo que ja rodou entre um passo e outro
				double drenoDeUmSegundo = Voo.CustoPorSegundo(Voo.HabilidadeNivel1, false) * 1.4;
				Conferir(cobrado >= esperado - 0.5 && cobrado <= esperado + drenoDeUmSegundo,
					$"decolagem cobrou {cobrado:0.00} de Ki (formula do DM: {esperado:0.00}, "
					+ $"mais ate {drenoDeUmSegundo:0.0} de dreno do 1o segundo)");

				Conferir(mundo.AlturaDeTeste > 0f, $"o corpo saiu do chao ({mundo.AlturaDeTeste:0} px)");
				Conferir(Mathf.Abs(mundo.AlturaDeTeste - Voo.AlturaDePairar) < 4f,
					$"parou sozinho na altura de pairar ({mundo.AlturaDeTeste:0} de {Voo.AlturaDePairar:0} px)");
				Conferir(Voo.AtravessaCenario(mundo.AlturaDeTeste),
					"a altura de pairar JA passa por cima do cenario (o isflying do DM)");

				// O SPRITE DE VOO. A folha de cada personagem tem um estado desenhado pra isso (o
				// `icon_state = "Flight"` do DM), e ele estava sendo reescrito pra "default" todo
				// quadro no corpo LOCAL -- so quem olhava de fora via a pose certa.
				// ============================ ONDE OS EFEITOS NASCEM ============================
				// Voando, o corpo e DESENHADO acima do no. Tres efeitos carimbavam na posicao do NO e
				// ficavam no chao enquanto o personagem cortava o ceu: o rastro de corrida, a miragem
				// do Zanzoken e a faisca do impacto. O dono viu como "o efeito de dash no fly ta
				// bugado".
				//
				// Os tres passaram a perguntar pelo DESENHO, e `PosicaoDesenhadaDe` e a resposta unica.
				// Se este vao for zero voando, os tres voltaram pro chao.
				// ===============================================================================
				Conferir(Math.Abs(Vao(mundo, cli) - mundo.AlturaDeTeste * Voo.EscalaNaTela) < 2f,
					$"o DESENHO do corpo esta {Vao(mundo, cli):0} px acima do no "
					+ $"(altura {mundo.AlturaDeTeste:0} x escala {Voo.EscalaNaTela}) -- e dai que sai"
					+ " o rastro, a miragem e a faisca");

				Conferir(mundo.AnimacaoLocalDeTeste.StartsWith("flight"),
					$"o corpo trocou pro sprite de VOO da folha dele (animacao \"{mundo.AnimacaoLocalDeTeste}\")");

				// ---------- agora VOANDO, contra a parede mais proxima DAQUI ----------
				// O rumo E recalculado: a caminhada de controle terminou barrada, contornando o
				// obstaculo, entao o corpo nao esta mais onde estava e a direcao velha pode nao
				// apontar pra parede nenhuma. Manter o rumo antigo fazia o voo sair pro nada -- e o
				// teste reprovava por falta de parede, nao por falta de voo.
				_kiNoInicioDaMedida = cli.Sheet.Ki;
				_segundosDeMedida = 0;
				_ondeSubiu = mundo.PosicaoLocal ?? _ondeSubiu;
				_paredeAlvo = mundo.ParedeMaisPertoDeTeste(_ondeSubiu);
				_rumoDaParede = ((_paredeAlvo ?? _ondeSubiu + Vector2.Right * 100f) - _ondeSubiu).Normalized();
				mundo.AndarDeTeste(_rumoDaParede);
				break;
			}

			// ============================ O VOO PRECISA DE TEMPO PRA CHEGAR ============================
			// Tres segundos davam ~390 px, e a parede mais proxima do ponto de nascimento da Terra
			// esta MAIS LONGE que isso -- entao a reta percorrida nao cruzava nada e o teste dizia
			// "0 celulas de parede" com o voo funcionando. Nao era o voo que falhava: era a bancada
			// que parava antes de chegar no obstaculo que ela mesma tinha escolhido.
			// ==========================================================================================
			case 9:
			case 10:
			case 11:
			case 12:
				_segundosDeMedida += dt;
				break;

			case 13:
			{
				_segundosDeMedida += dt;
				mundo.PararDeTeste();

				// ---------- o dreno por segundo, contra a formula do DM ----------
				double porSegundo = (_kiNoInicioDaMedida - cli.Sheet.Ki) / _segundosDeMedida;
				double alvo = Voo.CustoPorSegundo(Voo.HabilidadeNivel1, false);
				// A REGENERACAO PASSIVA ANDA JUNTO e puxa o saldo pra cima -- entao o medido fica
				// ABAIXO do bruto. A conferencia e por faixa: tem que estar entre metade e o valor
				// cheio. Fora disso nao e regeneracao, e formula errada.
				Conferir(porSegundo > alvo * 0.5 && porSegundo <= alvo * 1.15,
					$"o Ki drena a {porSegundo:0.0}/s (formula do DM: {alvo:0.0}/s bruto, "
					+ "menos a regeneracao passiva)");

				// ---------- atravessou parede? ----------
				// O MESMO CORPO, A MESMA COLISAO, O MESMO GESTO. A unica coisa que mudou foi a
				// altura -- e e por isso que as duas linhas juntas provam alguma coisa. Uma sozinha
				// nao provaria: colisao quebrada pra todo mundo daria "atravessou" nas duas.
				Vector2 agora = mundo.PosicaoLocal ?? _ondeSubiu;
				Conferir(agora.DistanceTo(_ondeSubiu) > 64f,
					$"andou {agora.DistanceTo(_ondeSubiu):0} px voando");
				Conferir(_entrouVoando,
					"VOANDO a mesma parede NAO barra: o corpo chegou a ficar DENTRO de celula bloqueada");

				// ---------- sobe ate o teto ----------
				Godot.Input.ActionPress("subir");
				break;
			}

			case 14:
			case 15:
			case 16:
				break;   // subindo (6 tiles/s ate 20 tiles: ~3,3 s)

			case 17:
			{
				_zonaDoTeto = cli.Zone;
				Conferir(_alturaMaxVista >= Voo.AlturaMaxima - 8f,
					$"chegou no teto ({_alturaMaxVista:0} de {Voo.AlturaMaxima:0} px)");
				Conferir(mundo.AlturaDeTeste <= Voo.AlturaMaxima + 0.01f,
					$"e o teto SEGUROU: nunca passou de {_alturaMaxVista:0} px");

				// ---------- os portoes do Space_Flight ----------
				// A BANCADA NAO PODE LER O PROPRIO expressedBP, e isso e regra do jogo e nao falha:
				// sem scouter o servidor manda `SemLeitura` (NaN) no lugar do numero -- e o sigilo
				// do BP (`GameServer.Sigilo.cs`). A primeira versao comparava esse NaN com 2000, o
				// que da falso pra QUALQUER lado, e reprovava sozinha.
				//
				// O que importa provar continua provado, e por evidencia que o cliente pode ver: o
				// teto recusou, disse por que, e a zona nao mudou. Quem sabe o BP e o servidor, e e
				// ele quem aplica o portao.
				Conferir(_avisoDoTeto, "o teto RECUSOU quem nao tem os portoes, e disse por que (aviso no chat)");
				Conferir(!Espaco.EhEspaco(_zonaDoTeto), $"e NAO foi pro espaco (zona: {_zonaDoTeto.Name})");

				// A FOTO NO TETO. Duas coisas so o olho responde, e as duas ja foram queixa:
				// a nevoa turvar a GUI (ela e uma camada, e camada errada cobre o HUD) e o corpo
				// nao trocar pro sprite de voo.
				Fotografar("user://voo-no-teto.png");

				// ---------- a tela respondeu a altura? ----------
				Conferir(mundo.NevoaDeTeste > 0.3f,
					$"a nevoa de altitude subiu com o corpo ({mundo.NevoaDeTeste:0.00} de 1,00)");
				// O ZOOM TEM PISO, e o teste tem que provar o PISO e nao o afastamento: o dono
				// reclamou justamente de afastar demais ("nem da pra ver o personagem").
				Conferir(mundo.ZoomDeTeste >= 2,
					$"a camera NAO afastou alem do piso legivel ({Boot.Config.Zoom}x -> {mundo.ZoomDeTeste}x, piso 2x)");

				// ---------- agora derruba por exaustao, com o SHIFT ----------
				// O `Superflight` deixou de ser verb e virou a tecla de correr (ver
				// `GameServer.Voo.MarchaDeVoo`) -- entao a bancada tem que APERTAR SHIFT E ANDAR,
				// que e o gesto do jogador. Chamar um verb aqui testaria um caminho que nao existe
				// mais.
				//
				// A 25 de proficiencia isso custa (35+450)/25 x 6 = 116 Ki/s: com ~100 de Ki maximo
				// o corpo cai em cerca de um segundo, e e o unico jeito de provar a regra do
				// `Stats.dm:413` sem esperar minutos.
				Godot.Input.ActionRelease("subir");
				Godot.Input.ActionPress("run");
				mundo.AndarDeTeste(_rumoDaParede);
				break;
			}

			case 18:
			case 19:
			case 20:
			case 21:
				break;   // queimando o Ki

			case 22:
				// A FOLGA E DE AMOSTRAGEM, e nao de regra. A ficha chega a 5 Hz e a regeneracao roda
				// a 30: o cliente ve o Ki quando ele JA voltou um pouco, entao o menor valor
				// observavel fica alguns decimos acima do limiar que o servidor de fato aplicou.
				// Um segundo de folga cobre isso sem deixar passar uma formula errada -- errar a
				// regra do `Stats.dm:413` daria dezenas de diferenca, nao decimos.
				Conferir(_kiMinimo <= Voo.KiQueDerruba + 1,
					$"o Superflight torrou o Ki ate o limiar do DM ({_kiMinimo:0.0} de "
					+ $"{Voo.KiQueDerruba:0}; agora ja regenerou pra {cli.Sheet.Ki:0.0})");
				Conferir(mundo.AlturaDeTeste == 0f,
					$"e ficar sem Ki DERRUBOU: o corpo esta no chao ({mundo.AlturaDeTeste:0} px)");
				Conferir(mundo.NevoaDeTeste < 0.15f, "a nevoa foi embora junto com a altura");
				break;

			default:
				_acabou = true;
				Godot.Input.ActionRelease("subir");
				Godot.Input.ActionRelease("descer");
				Godot.Input.ActionRelease("run");
				GD.Print("\n[voo] ===== BANCADA DO VOO =====");
				foreach (string l in _passos) GD.Print("[voo] " + l);
				GD.Print(_falhas.Count == 0
					? "[voo] ===== TUDO OK ====="
					: $"[voo] ===== {_falhas.Count} FALHA(S) =====\n[voo]   " + string.Join("\n[voo]   ", _falhas));
				break;
		}
	}
}
