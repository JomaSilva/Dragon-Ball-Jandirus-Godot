using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A ABSORCAO DO MAJIN NA TELA, DOS DOIS LADOS (`--diagmajin`).
///
/// ============================ O PEDIDO DO DONO (2026-10-09) ============================
/// *"cheque pra mim se a absorçao do majin ta funcionando e se o jogador absorvido vai pro interior do corpo
/// do majin enfrentar o clone do corpo original pra escapar (assim como era no DM)"*.
///
/// A regra inteira ja e cobrada sem janela pela `--majinteste` (as cinco saidas, as contas, os verbs, o
/// luto). O que so existe AQUI e o que o jogador ve e aperta: o cenario do interior desenhado, a imagem do
/// Majin ao lado dele, o caminho de volta pro mundo, os botoes do menu e a roupa no boneco.
/// =======================================================================================
///
/// ============================ DOIS ATOS, E QUEM ESCOLHE E A RACA ============================
/// O corpo local e um so por processo, entao sao DUAS corridas da mesma bancada:
///
///   * `--raca Human`  -- o ATO DO PRESO, que e a pergunta do dono na letra. Um Majin forjado me absorve
///     pelo verb de producao; eu acordo dentro dele, a imagem dele vem me bater, eu a venco A SOCO (pacotes
///     de golpe de verdade) e sou cuspido de volta. Depois, a contraprova e a quarta saida (ele me expele).
///   * `--raca Majin --skillteste /datum/skill/general/buuabsorb` -- o ATO DO MAJIN. Eu aperto ABSORVER no
///     menu, a vitima some pra dentro de mim, o menu ganha o `Expelir: nome`, eu visto a roupa dela, e o
///     botao de expelir a devolve.
/// ==========================================================================================
///
/// ============================ O QUE E MEDIDO NO PIXEL ============================
/// "O interior esta desenhado" nao se le de campo nenhum: e a regra das TRES FOTOS (a da `--diagboca`), com
/// as CAMADAS DO CENARIO escondidas nas duas de fora. Tinta do cenario e o pixel que difere das duas fotos
/// sem ele, onde as duas concordam -- e a regua cobra que ela cubra a janela em volta do corpo. A imagem do
/// Majin passa pela mesma regua, com o node dela.
///
/// AS CONTRAPROVAS:
///   * do PRESO -- `World.InteriorDoMajinSemPlantaDeTeste` desliga o ramo do `CarregarZona` (o estado de
///     antes: a zona caia no chao provisorio, sem parede e sem desenho) e as MESMAS reguas tem que reprovar;
///   * do MAJIN -- `MenuJogo.AbsorvidosNaoRemontamDeTeste` cala o remonte dos botoes, e a regua do
///     `Expelir: nome` tem que reprovar com gente dentro; e `CensoDeSkills.OutroCanalEhMudoDeTeste` devolve
///     o botao cinza "Buu Absorb (nao portada)" e o "efeito ainda nao portado" da ficha da skill.
/// ================================================================================
///
/// COMO RODAR -- um processo so, com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 7990 --bpteste 3000000 --horateste 0.5 --campoteste --diagmajin \
///           --position 1920,0 --resolution 1600x900 --raca Human --conta bancada_dmajin --nome Preso
///
///     Godot --path . --host --rede 7991 --bpteste 3000000 --horateste 0.5 --campoteste --diagmajin \
///           --position 1920,0 --resolution 1600x900 --raca Majin --conta bancada_dmajin_m --nome Bubu \
///           --skillteste /datum/skill/general/buuabsorb
///
/// As fotos saem em `user://dmajin-*.png`. Comecar por `dmajin-0-historia.png` (fora, dentro, a luta, de
/// volta) e `dmajin-5-antes-e-depois.png`; no ato do Majin, `dmajin-6-roupa.png`, `dmajin-7-menu.png` e
/// `dmajin-8-menu-expelir.png`.
/// </summary>
public partial class RoboDaAbsorcaoMajin : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private const int T = ZoneCollision.TileSize;

	/// <summary>
	/// O PODER DO MAJIN DA CENA. O corpo local entra com 3 milhoes (`--bpteste`): a imagem nasce com METADE
	/// do poder expresso do Majin e com os sete stats dele, e tem que ser vencivel A SOCO dentro da
	/// paciencia da bancada. (Que a imagem tem a metade exata e conta da `--majinteste`.)
	///
	/// A LUTA E DE VERDADE, e a segunda corrida provou: com um Majin de 1 milhao e stats 5 a imagem me
	/// NOCAUTEOU em sete segundos, de longe, a rajadas de ki -- e eu fiquei la dentro, que e a regra
	/// ("se perder, continua absorvido"). Daqui os 600 mil e os stats 1 do `MajinDaFoto`.
	/// </summary>
	private const double BpDoMajin = 600_000;

	/// <summary>O poder da vitima do ato do Majin. Bem abaixo dele: ela so precisa cair e ser engolida.</summary>
	private const double BpDaVitima = 400_000;

	/// <summary>Meio lado da janela medida em volta do corpo, em px de mundo.</summary>
	private const float MeiaJanela = 150f;

	/// <summary>
	/// Quanto da janela tem que ser tinta do cenario pra o interior contar como DESENHADO. Nao e perto de
	/// cem porque o HUD e o chat cobrem parte da janela, e os corpos tambem; sem o bolsao a medida e 0%.
	/// </summary>
	private const float FracaoDeCenario = 0.6f;

	/// <summary>O 0,12 das outras bancadas de foto: abaixo disso e ruido do viewport.</summary>
	private const float Epsilon = 0.12f;

	/// <summary>
	/// O LIMIAR FINO, so pra regua do cenario. O tile de carne (`Tiles 1.21.2011` / `a 1`) tem o miolo quase
	/// preto, e com 0,12 esse miolo nao diferia do fundo preto que aparece quando as camadas somem: a sonda
	/// contava 47% a 62% de um bolsao que cobre a tela inteira. As tres fotos sao do MESMO quadro parado
	/// (a arvore esta pausada), entao o ruido entre elas e zero e o limiar pode descer.
	/// </summary>
	private const float EpsilonDoCenario = 0.03f;

	/// <summary>De quanto em quanto o corpo local pede um golpe, na luta contra a imagem.</summary>
	private const double CadenciaDoGolpe = 0.35;

	private const double Paciencia = 240;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida, _delta;
	private int _passo, _sub;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready()
	{
		// A ARVORE E PAUSADA PRA FOTOGRAFAR (ver `Obturador`); sem isto a bancada congelaria junto.
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos()
	{
		World.InteriorDoMajinSemPlantaDeTeste = false;
		MenuJogo.AbsorvidosNaoRemontamDeTeste = false;
		Jandirus.Core.Skills.CensoDeSkills.OutroCanalEhMudoDeTeste = false;
	}

	private const int PAssentar = 0,
					  // o ato do PRESO
					  PPlantar = 1, PDerrubar = 2, PAbsorver = 3, PDentro = 4, PCenario = 5, PImagem = 6, PChegar = 7,
					  PVencer = 8, PDeVolta = 9, PDefeitoAbsorver = 10, PDefeitoDentro = 11, PDefeitoCenario = 12,
					  PDefeitoExpelir = 13,
					  // o ato do MAJIN
					  MPlantar = 20, MApertar = 21, MAbsorvido = 22, MMenu = 23, MExpelir = 24, MSolto = 25,
					  MDefeito = 26;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagmajin` precisa de `--host`)"); Fechar(); return; }

		_delta = delta;
		_vida += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo}.{_sub})"); Fechar(); return; }
		_t += delta;
		Sondar(mundo, srv, cli);

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;

			case PPlantar: PlantarOMajin(mundo, srv, cli); break;
			case PDerrubar: SerDerrubado(mundo, srv, cli); break;
			case PAbsorver: SerAbsorvido(srv, cli); break;
			case PDentro: Dentro(mundo, srv, cli); break;
			case PCenario: OCenario(mundo, cli); break;
			case PImagem: AImagem(mundo, srv, cli); break;
			case PChegar: AImagemChega(mundo, srv, cli); break;
			case PVencer: VencerAImagem(mundo, srv, cli); break;
			case PDeVolta: DeVolta(mundo, srv, cli); break;
			case PDefeitoAbsorver: DefeitoAbsorver(srv, cli); break;
			case PDefeitoDentro: DefeitoDentro(mundo); break;
			case PDefeitoCenario: DefeitoNoCenario(mundo, cli); break;
			case PDefeitoExpelir: SerExpelido(mundo, srv, cli); break;

			case MPlantar: PlantarAVitima(mundo, srv, cli); break;
			case MApertar: ApertarAbsorver(srv, cli); break;
			case MAbsorvido: Absorvi(mundo, srv, cli); break;
			case MMenu: OMenu(cli); break;
			case MExpelir: ApertarExpelir(cli); break;
			case MSolto: Soltei(mundo, srv, cli); break;
			case MDefeito: DefeitoDoMenu(srv, cli); break;

			default: Fechar(); break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _sub = 0; _t = 0; }

	private void Sub(int proximo) { _sub = proximo; _t = 0; }

	// =====================================================================
	// A SONDA DO DEITAR: quando cada ponta ficou sabendo
	// =====================================================================
	private (bool Servidor, bool Ficha, bool Corpo, bool Desenho) _caido;

	/// <summary>Quando o servidor virou e o desenho ainda nao acompanhou (-1 = em dia), e o pior atraso visto.</summary>
	private double _viradaEm = -1, _maiorAtraso;
	private int _viradas;

	/// <summary>Ate quanto o desenho pode demorar pra seguir o servidor: a ficha anda a 5 Hz (`TickFichas`).</summary>
	private const double AtrasoToleradoDoDesenho = 0.35;

	/// <summary>
	/// QUATRO RELOGIOS DA MESMA PERGUNTA ("estou caido?"): o servidor, a ficha que chegou ao cliente, o corpo
	/// local e o desenho. Cada virada sai no relato com o segundo e o quadro, e o atraso do desenho vira regua
	/// no fim (<see cref="AtrasoToleradoDoDesenho"/>).
	///
	/// ============================ ELA NASCEU DE DUAS FOTOS QUE PARECIAM ATRASADAS ============================
	/// Na primeira corrida a foto de fora mostrava o corpo "de pe" um segundo depois do nocaute, e a de dentro
	/// o mostrava deitado dois segundos depois de acordar. NENHUMA das duas era atraso, e foi esta sonda que
	/// disse: as quatro pontas viram no mesmo quadro.
	///   * la fora o corpo estava deitado, sim -- caido "pro sul", que e de cabeca pra cima (`DeitarPor`), e
	///     na foto isso se le como um corpo de pe. Hoje o nocaute da cena tem autor (`DerrubarNaFotoDoMajin`);
	///   * la dentro a imagem do Majin tinha me nocauteado DE NOVO. A luta e de verdade.
	/// ========================================================================================================
	/// </summary>
	private void Sondar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		var agora = (srv.CorpoNaFotoDoMajin(cli.LocalId).Caido, cli.Sheet.KO,
					 (mundo.CorpoDeTeste(cli.LocalId) as LocalPlayer)?.CaidoDeTeste ?? false,
					 mundo.VisualLocalDeTeste?.DeitadoDeTeste ?? false);

		if (agora.Item1 != _caido.Servidor && _viradaEm < 0) { _viradaEm = _vida; _viradas++; }
		if (_viradaEm >= 0 && agora.Item4 == agora.Item1)
		{
			_maiorAtraso = Math.Max(_maiorAtraso, _vida - _viradaEm);
			_viradaEm = -1;
		}

		if (agora == _caido) return;
		_caido = agora;
		static string S(bool b) => b ? "SIM" : "nao";
		Nota($"[{_vida:0.00} s, quadro {Engine.GetFramesDrawn()}, passo {_passo}.{_sub}] caido? servidor={S(agora.Item1)} "
			 + $"ficha={S(agora.Item2)} corpo={S(agora.Item3)} desenho={S(agora.Item4)}");
	}

	// =====================================================================
	// 0) O BERCO ASSENTA, E A RACA ESCOLHE O ATO
	// =====================================================================
	private bool _souMajin;
	private ZoneKey _zonaDeFora;

	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3) return;

		// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim).
		(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
		if ((ko || morto) && _t < 30) return;
		Conferir(!ko && !morto, "o corpo entrou em pe e acordado");

		bool praca = srv.AssentarNaBoca(cli.LocalId, 4);
		Conferir(praca, "achei uma PRACA com os quatro lados livres (quatro tiles em cada)");
		if (!praca) { Fechar(); return; }

		// MEIO-DIA E CEU LIMPO: a foto de fora e a de dentro saem com a mesma luz de cena.
		srv.CravarMeioDiaDaVariedade(cli.LocalId);

		_souMajin = srv.EhMajinNaFoto(cli.LocalId);
		_zonaDeFora = cli.Zone;
		Nota(_souMajin
			? "ATO DO MAJIN: o corpo local e um Majin -- ele absorve pelo botao do menu e expele pelo botao do menu"
			: "ATO DO PRESO: o corpo local e absorvido por um Majin, luta la dentro e sai");
		Ir(_souMajin ? MPlantar : PPlantar);
	}

	// =====================================================================
	// ATO DO PRESO -- 1) um Majin ao lado, e eu caido aos pes dele
	// =====================================================================
	private int _majin, _imagem;
	private string _folhaDoMajin = "";
	private int _roupasDoMajin;
	private readonly List<Image> _historia = [];

	private void PlantarOMajin(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_majin == 0)
		{
			if (_t < 1.0) return;   // a correcao de posicao do `AssentarNaBoca` precisa atravessar o fio
			_majin = srv.MajinDaFoto(cli.LocalId, new Vec2(T, 0), BpDoMajin);
			Conferir(_majin != 0 && srv.EhMajinNaFoto(_majin),
				"um MAJIN nasceu um tile a leste (forjado: a raca, a skill Buu Absorb e a aparencia sao de producao)");
			if (_majin == 0) { Fechar(); return; }
			srv.ApontarNaBoca(_majin, Facing.West);   // de frente pra mim: e a frente dele que recebe quem sai
			_t = 0;
			return;
		}
		if (_t < 1.5) return;   // o corpo e a aparencia dele atravessam o fio

		CharacterVisual? v = VisualDe(mundo, _majin);
		_folhaDoMajin = v?.FolhaDoCorpoDeTeste ?? "";
		_roupasDoMajin = v?.RoupasNoCorpoDeTeste().Count ?? 0;
		Conferir(v != null && _folhaDoMajin.Length > 0,
			$"...e esta desenhado na minha tela (corpo `{Curto(_folhaDoMajin)}`, {_roupasDoMajin} peca(s) de roupa)");
		Ir(PDerrubar);
	}

	private void SerDerrubado(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			Conferir(srv.DerrubarNaFotoDoMajin(cli.LocalId, new Vec2(1, 0)), "fui NOCAUTEADO (o nocaute de producao) aos pes do Majin");
			Sub(1);
			return;
		}
		// A FOTO ESPERA O DESENHO, e mais meio segundo: a pose muda num quadro e a foto e do quadro anterior.
		bool deitado = mundo.VisualLocalDeTeste?.DeitadoDeTeste == true;
		if (_sub == 1)
		{
			if (!deitado && _t < 4) return;
			Conferir(deitado, $"...e o cliente me desenha caido ({_t:0.00} s depois do nocaute)");
			Sub(2);
			return;
		}
		if (_t < 0.5) return;

		Guardar(_historia, Foto(mundo, MeuLugar(mundo, cli), "dmajin-1-fora.png", "1. FORA: nocauteado aos pes do Majin"));
		Ir(PAbsorver);
	}

	// =====================================================================
	// ATO DO PRESO -- 2) o Majin absorve, e eu vou pra DENTRO dele
	// =====================================================================
	private float _distInicial;

	/// <summary>O meu corpo no servidor, no mesmo quadro em que o Majin apertou o verb. Ver <see cref="SerAbsorvido"/>.</summary>
	private (bool Existe, bool Caido, double Vida, Vec2 Pos, ZoneKey Zona, double Poder, bool EmCombate) _chegada;

	private void SerAbsorvido(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			srv.HabilidadeNaFotoDoMajin(_majin, "absorver");
			// A CHEGADA E LIDA AQUI, no quadro do verb e na conta do servidor: a imagem bate em menos de dois
			// segundos, e a primeira corrida cobrou "inteiro" um segundo e meio depois -- com 96 de vida, ja
			// depois do primeiro golpe dela.
			_chegada = srv.CorpoNaFotoDoMajin(cli.LocalId);
			Sub(1);
			return;
		}

		if (!InteriorDoMajin.EhOInterior(cli.Zone))
		{
			if (_t < 6) return;
			Conferir(false, $"o Majin apertou ABSORVER e o cliente foi levado pra dentro dele (a zona do cliente continua `{cli.Zone.Name}`)");
			Fechar();
			return;
		}

		Conferir(InteriorDoMajin.Anfitriao(cli.Zone) == _majin,
			$"o Majin apertou ABSORVER (o verb de producao) e o CLIENTE foi levado pro INTERIOR dele "
			+ $"(zona `{cli.Zone.Name}`, anfitriao #{InteriorDoMajin.Anfitriao(cli.Zone)})");

		// A IMAGEM NASCE A TRES CELULAS, e comeca a andar no tique seguinte: a distancia de nascenca e lida ja.
		(_, _imagem, _) = srv.PrisaoNaFotoDoMajin(cli.LocalId);
		_distInicial = Vec2.Distance(srv.CorpoNaFotoDoMajin(_imagem).Pos, srv.CorpoNaFotoDoMajin(cli.LocalId).Pos);
		Ir(PDentro);
	}

	private void Dentro(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O LUGAR MONTADO, A IMAGEM NA TELA E EU DE PE -- e a foto sai JA, com a imagem ainda a tres tiles: em
		// menos de dois segundos ela esta em cima de mim atirando, e o clarao do ki cobre os dois.
		bool montou = mundo.ZonaMontadaDeTeste == cli.Zone.Hash;
		bool imagemNaTela = (VisualDe(mundo, _imagem)?.FolhaDoCorpoDeTeste.Length ?? 0) > 0;
		bool dePe = mundo.VisualLocalDeTeste?.DeitadoDeTeste == false;
		if (_sub == 0)
		{
			if (!(montou && imagemNaTela && dePe) && _t < 8) return;
			Conferir(montou, "o mundo do cliente montou a zona do interior");
			Conferir(dePe, $"...e me desenha DE PE ({_t:0.00} s depois de o cliente trocar de zona)");
			Sub(1);
			return;
		}
		if (_t < 0.3) return;   // a pose muda num quadro, a foto e do quadro anterior, e o corpo dela acaba de aparecer
		Guardar(_historia, Foto(mundo, Meio(mundo, cli, _imagem), "dmajin-2-dentro.png", "2. DENTRO: o interior do Majin, e a imagem dele vindo pra cima de mim"));

		(int majin, int imagem, _) = srv.PrisaoNaFotoDoMajin(cli.LocalId);
		Conferir(majin == _majin && imagem != 0 && imagem == _imagem,
			$"o servidor guarda o registro: preso DENTRO do Majin #{majin}, com uma imagem de guarda (corpo #{imagem})");

		Conferir(InteriorDoMajin.Anfitriao(_chegada.Zona) == _majin && !_chegada.Caido && _chegada.Vida >= 99.5,
			$"o preso chega ACORDADO e INTEIRO, no mesmo tique do verb (vida {_chegada.Vida:0.#}; fui engolido nocauteado)");

		var ganho = srv.GanhoNaFotoDoMajin(_majin);
		Conferir(ganho.Dentro == 1 && ganho.Poder > 0,
			$"enquanto eu estiver la dentro o Majin usa +{ganho.Poder:N0} de poder (10% do meu) e {ganho.Verbos} verb(s) que eram meus");

		(int largura, bool desenha, bool parede) = LerOLugar(mundo);
		Conferir(largura == InteriorDoMajin.Lado, $"o mapa do cliente e o bolsao: {largura} celulas de lado (la: `MAJIN_POCKET_SIZE` = {InteriorDoMajin.Lado})");
		Conferir(desenha, "a celula debaixo do meu corpo tem tile (o chao de carne, `Tiles 1.21.2011` / `a 1`)");
		Conferir(parede, "a borda do bolsao e PAREDE na copia do cliente, e o chao em que eu piso nao");
		Ir(PCenario);
	}

	/// <summary>As tres reguas de campo do lugar: o lado do mapa, o tile debaixo do corpo e a parede da borda.</summary>
	private static (int Largura, bool Desenha, bool Parede) LerOLugar(World mundo)
	{
		Vector2 p = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
		int cx = (int)MathF.Floor(p.X / T), cy = (int)MathF.Floor(p.Y / T);
		return (mundo.LarguraDoMapaDeTeste, mundo.CelulaDesenhaDeTeste(cx, cy),
				mundo.CelulaBloqueiaDeTeste(0, cy) && !mundo.CelulaBloqueiaDeTeste(cx, cy));
	}

	private Image? _dentroBom;
	private float _fracaoBoa;

	/// <summary>A REGUA DE PIXEL DO LUGAR: tres fotos, com as camadas do cenario escondidas nas duas de fora.</summary>
	private void OCenario(World mundo, GameClient cli)
	{
		if (_obtFase == 0) Mirar(mundo.CamadasDoCenarioDeTeste);
		if (!Obturador(out Fotos f)) return;
		if (f.Falhou) { Conferir(false, "a janela renderizou o interior"); Ir(PImagem); return; }

		Vector2 centro = MeuLugar(mundo, cli);
		_fracaoBoa = Fracao(f, centro, "dmajin-cenario-mascara.png");
		Conferir(_fracaoBoa >= FracaoDeCenario,
			$"o interior esta PINTADO na tela: {_fracaoBoa:P0} da janela em volta do corpo e tinta do cenario "
			+ $"(tres fotos, com e sem as {mundo.CamadasDoCenarioDeTeste.Length} camadas; a regua pede {FracaoDeCenario:P0})");
		_dentroBom = Recortar(f.Com, centro, MeiaJanela, MeiaJanela);
		Ir(PImagem);
	}

	// =====================================================================
	// ATO DO PRESO -- 3) a imagem do Majin: esta la, e ele, vem ate mim e me bate
	// =====================================================================
	private void AImagem(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_obtFase == 0)
		{
			Node2D? corpo = mundo.CorpoDeTeste(_imagem);
			CharacterVisual? v = VisualDe(mundo, _imagem);
			string folha = v?.FolhaDoCorpoDeTeste ?? "";
			if ((corpo == null || folha.Length == 0) && _t < 4) return;   // o corpo e a aparencia dela atravessam o fio

			Conferir(corpo is RemotePlayer, "a IMAGEM do Majin esta no mundo do cliente, dentro do bolsao");
			Conferir(folha.Length > 0 && folha == _folhaDoMajin,
				$"...e tem o CORPO dele (`{Curto(folha)}`, o mesmo que eu vi la fora)");
			int roupas = v?.RoupasNoCorpoDeTeste().Count ?? -1;
			if (_roupasDoMajin > 0)
				Conferir(roupas == 0,
					$"...SEM a roupa dele: la so o icone, o cabelo e os olhos sao copiados (o Majin veste {_roupasDoMajin} peca(s), a imagem {roupas})");
			else Nota($"o Majin sorteado nao veste roupa nenhuma: a regua da imagem sem roupa nao mede nada nesta corrida (a imagem tem {roupas})");

			if (corpo == null) { Ir(PChegar); return; }
			Mirar([corpo]);
		}
		if (!Obturador(out Fotos f)) return;
		if (f.Falhou) { Conferir(false, "a janela renderizou a imagem"); Ir(PChegar); return; }

		int tinta = Tinta(f, mundo.PosicaoDesenhadaDe(_imagem) ?? Vector2.Zero, 40f, "dmajin-imagem-mascara.png").Tinta;
		Conferir(tinta > 60, $"...e esta PINTADA na tela, ao meu lado ({tinta} px de tinta do corpo dela)");
		Ir(PChegar);
	}

	private void AImagemChega(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		var eu = srv.CorpoNaFotoDoMajin(cli.LocalId);
		var ela = srv.CorpoNaFotoDoMajin(_imagem);
		float d = Vec2.Distance(eu.Pos, ela.Pos);
		bool chegou = ela.Existe && d <= 1.6f * T;
		if (!(chegou && eu.EmCombate) && _t < 8) return;

		Conferir(chegou, $"a imagem VEIO ATE MIM sozinha (nasceu a {_distInicial / T:0.#} tiles, esta a {d / T:0.#}) -- eu nao dei um passo");
		Conferir(eu.EmCombate, "...e me ATACOU: estou em combate sem ter pedido um golpe");
		Guardar(_historia, Foto(mundo, Meio(mundo, cli, _imagem), "dmajin-3-luta.png", "3. A LUTA: a imagem do Majin em cima de mim"));
		_menorVida = ela.Vida;
		Ir(PVencer);
	}

	// =====================================================================
	// ATO DO PRESO -- 4) vencer a imagem A SOCO, e sair
	// =====================================================================
	private double _proximoGolpe, _menorVida = 100;
	private int _golpes;

	/// <summary>
	/// A LUTA E DE VERDADE: o corpo local anda pelo piloto de producao e pede golpes pelo `SendAction`, o
	/// mesmo pacote da tecla. Quem decide se o golpe pega, quanto tira e quando a imagem esta vencida e o
	/// servidor (`guard_watch`: nocauteada, morta ou com a vida em 15).
	///
	/// DE FRENTE PRA ELA: a direcao do corpo local e previsao do cliente e so muda ANDANDO, entao enquanto
	/// a imagem nao estiver na frente (ou estiver longe) o piloto anda pra cima dela.
	/// </summary>
	private void VencerAImagem(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (!InteriorDoMajin.EhOInterior(cli.Zone))
		{
			mundo.PararDeTeste();
			Conferir(cli.Zone.Hash == _zonaDeFora.Hash,
				$"VENCI A IMAGEM a soco ({_golpes} golpes pedidos; a vida dela chegou a {_menorVida:0}) e fui CUSPIDO de volta "
				+ $"pro mundo: o cliente esta em `{cli.Zone.Name}`");
			Ir(PDeVolta);
			return;
		}
		if (_t > 60)
		{
			mundo.PararDeTeste();
			Conferir(false, $"venci a imagem em 60 s ({_golpes} golpes pedidos; a vida dela chegou a {_menorVida:0})");
			Fechar();
			return;
		}

		var ela = srv.CorpoNaFotoDoMajin(_imagem);
		if (!ela.Existe) return;   // vencida: a olhada do servidor solta em ate 0,8 s
		_menorVida = Math.Min(_menorVida, ela.Vida);

		Vector2 eu = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
		var rumo = new Vector2(ela.Pos.X - eu.X, ela.Pos.Y - eu.Y);
		float d = rumo.Length();
		Vec2 frente = MeleeArea.Frente(mundo.OlharLocalDeTeste);
		bool deFrente = d > 0.01f && (rumo / d).Dot(new Vector2(frente.X, frente.Y)) > 0.7f;
		if (d > 1.3f * T || !deFrente) mundo.IrAteDeTeste(ela.Pos);
		else mundo.PararDeTeste();

		_proximoGolpe -= _delta;
		if (_proximoGolpe > 0 || d > 2.5f * T) return;
		_proximoGolpe = CadenciaDoGolpe;
		_golpes++;
		cli.SendAction();

		// A FOTO DO CORPO A CORPO, uma vez: a de `AImagemChega` costuma pegar a rajada de ki dela, de longe.
		if (_golpes == 3 && d <= 1.5f * T)
			Guardar(_historia, Foto(mundo, Meio(mundo, cli, _imagem), "dmajin-3b-soco.png", "3b. O REVIDE: eu, a soco, em cima da imagem"));
	}

	private void DeVolta(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		bool montou = mundo.ZonaMontadaDeTeste == _zonaDeFora.Hash;
		if ((!montou || _t < 1.5) && _t < 8) return;
		Conferir(montou, "o mundo do cliente remontou a zona de fora");

		var eu = srv.CorpoNaFotoDoMajin(cli.LocalId);
		var majin = srv.CorpoNaFotoDoMajin(_majin);
		float d = Vec2.Distance(eu.Pos, majin.Pos);
		Conferir(!eu.Caido, "saio DE PE: quem vence a imagem nao sai nocauteado");
		Conferir(eu.Zona.Hash == majin.Zona.Hash && d <= 2.5f * T, $"...ao lado do Majin (a {d / T:0.#} tiles dele)");
		Conferir(srv.PrisaoNaFotoDoMajin(cli.LocalId).Majin == 0 && !srv.CorpoNaFotoDoMajin(_imagem).Existe,
			"o registro e a imagem sumiram do servidor");
		var ganho = srv.GanhoNaFotoDoMajin(_majin);
		Conferir(ganho.Poder == 0 && ganho.Verbos == 0 && ganho.Dentro == 0, "o Majin perdeu o poder e os verbs que tinha por minha causa");

		Guardar(_historia, Foto(mundo, MeuLugar(mundo, cli), "dmajin-4-de-volta.png", "4. DE VOLTA: cuspido pro mundo, de pe, ao lado do Majin"));
		Colar("dmajin-0-historia.png", _historia, "A HISTORIA INTEIRA: fora / dentro / a luta / de volta");
		Ir(PDefeitoAbsorver);
	}

	// =====================================================================
	// ATO DO PRESO -- 5) a contraprova (o cliente sem o ramo do interior) e a quarta saida (expelir)
	// =====================================================================
	private void DefeitoAbsorver(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				World.InteriorDoMajinSemPlantaDeTeste = true;
				srv.DerrubarNaFotoDoMajin(cli.LocalId, new Vec2(1, 0));
				Sub(1);
				return;

			case 1:
				if (_t < 0.8) return;
				srv.HabilidadeNaFotoDoMajin(_majin, "absorver");
				Sub(2);
				return;

			default:
				if (InteriorDoMajin.EhOInterior(cli.Zone)) { Ir(PDefeitoDentro); return; }
				if (_t < 6) return;
				World.InteriorDoMajinSemPlantaDeTeste = false;
				Conferir(false, "(defeito injetado) o Majin me absorveu de novo");
				Ir(-1);
				return;
		}
	}

	private (int Largura, bool Desenha, bool Parede) _lugarRuim;

	private void DefeitoDentro(World mundo)
	{
		if (_t < 1.5) return;
		_lugarRuim = LerOLugar(mundo);
		Ir(PDefeitoCenario);
	}

	private void DefeitoNoCenario(World mundo, GameClient cli)
	{
		if (_obtFase == 0) Mirar(mundo.CamadasDoCenarioDeTeste);
		bool acabou;
		Fotos f;
		try { acabou = Obturador(out f); }
		catch { World.InteriorDoMajinSemPlantaDeTeste = false; throw; }
		if (!acabou) return;
		World.InteriorDoMajinSemPlantaDeTeste = false;
		if (f.Falhou) { Conferir(false, "(defeito injetado) a janela renderizou"); Ir(PDefeitoExpelir); return; }

		Vector2 centro = MeuLugar(mundo, cli);
		float fracao = Fracao(f, centro, null);
		Conferir(_lugarRuim.Largura != InteriorDoMajin.Lado && !_lugarRuim.Desenha && !_lugarRuim.Parede && fracao < FracaoDeCenario,
			$"(defeito injetado: o cliente sem o ramo do interior) as reguas do lugar REPROVAM -- mapa de {_lugarRuim.Largura} celulas, "
			+ $"celula do corpo {(_lugarRuim.Desenha ? "com" : "SEM")} tile, borda {(_lugarRuim.Parede ? "com" : "SEM")} parede, "
			+ $"{fracao:P0} de tinta de cenario (na producao: {_fracaoBoa:P0})");

		Image? ruim = Recortar(f.Com, centro, MeiaJanela, MeiaJanela);
		if (ruim != null && _dentroBom != null)
			Colar("dmajin-5-antes-e-depois.png", [ruim, _dentroBom],
				  "DENTRO DO MAJIN: a esquerda como era (a zona sem cenario: o chao provisorio), a direita o bolsao de carne");
		Ir(PDefeitoExpelir);
	}

	private void SerExpelido(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			srv.HabilidadeNaFotoDoMajin(_majin, "expelir");
			Sub(1);
			return;
		}
		bool fora = !InteriorDoMajin.EhOInterior(cli.Zone) && mundo.ZonaMontadaDeTeste == _zonaDeFora.Hash;
		if ((!fora || _t < 1.2) && _t < 8) return;

		var eu = srv.CorpoNaFotoDoMajin(cli.LocalId);
		Conferir(fora && !eu.Caido,
			"A QUARTA SAIDA: o Majin apertou EXPELIR e eu voltei pro mundo, de pe (sem vencer imagem nenhuma)");
		Conferir(srv.GanhoNaFotoDoMajin(_majin).Dentro == 0, "...e ele ficou sem ninguem dentro");
		Ir(-1);
	}

	// =====================================================================
	// ATO DO MAJIN -- 1) uma pessoa caida ao lado, e os botoes do meu menu
	// =====================================================================
	private int _vitima, _numero;
	private string _nomeDaVitima = "";
	private List<string> _roupasDaVitima = [], _roupasAntes = [];
	private readonly List<Image> _tiraDaRoupa = [];

	private static Verbo? Botao(string chave) => Verbos.Todos.FirstOrDefault(v => v.Chave == chave);

	/// <summary>O que a ficha da skill Buu Absorb diz na compra -- a mesma montagem que a aba de skills desenha.</summary>
	private static string FichaDaSkill()
	{
		Jandirus.Core.Skills.SkillCatalog? cat = MenuJogo.CatalogoPublico();
		Jandirus.Core.Skills.Skill? s = cat?.Get(AbsorcaoMajin.Skill);
		return cat == null || s == null ? "" : string.Join(" | ", Jandirus.Core.Skills.FichaDeSkill.Montar(cat, s, null).NaCompra);
	}

	private void PlantarAVitima(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_vitima == 0)
		{
			if (_t < 1.0) return;

			// SO CENA: a criacao automatica faz o Majin sem cor de corpo, que e um vulto quase preto.
			srv.PintarNaFotoDoMajin(cli.LocalId);

			Conferir(Botao("hab:absorver") != null, "o menu do Majin tem o botao ABSORVER");
			Conferir(Botao("hab:expelir") is { PodeAgora: false }, "...e o EXPELIR TODOS, apagado: nao ha ninguem dentro de mim");

			string ficha = FichaDaSkill();
			Conferir(Botao("hab:" + AbsorcaoMajin.Verbo) == null,
				$"...e SO ele: o verb da skill (`{AbsorcaoMajin.Verbo}`) nao vira um segundo botao, cinza e \"(nao portada)\"");
			Conferir(ficha.Contains("Buu Absorb", StringComparison.Ordinal) && !ficha.Contains("não portado", StringComparison.Ordinal),
				$"a ficha da skill diz o que ela da, sem \"efeito ainda nao portado\" (`{ficha}`)");
			try
			{
				Jandirus.Core.Skills.CensoDeSkills.OutroCanalEhMudoDeTeste = true;
				Habilidades.Montar(cli.Atributos.Raca ?? "");
				string fichaRuim = FichaDaSkill();
				Conferir(Botao("hab:" + AbsorcaoMajin.Verbo) is { PodeAgora: false } && fichaRuim.Contains("não portado", StringComparison.Ordinal),
					$"(defeito injetado: verb de outro canal tratado como mudo) as duas reguas REPROVAM -- o botao cinza volta, "
					+ $"e a ficha diz `{fichaRuim}`");
			}
			finally
			{
				Jandirus.Core.Skills.CensoDeSkills.OutroCanalEhMudoDeTeste = false;
				Habilidades.Montar(cli.Atributos.Raca ?? "");
			}

			_vitima = srv.VitimaDaBoca(cli.LocalId, new Vec2(1, 0), tiles: 1, bp: BpDaVitima);
			Conferir(_vitima != 0, "uma PESSOA nasceu um tile a leste (forjada: com conta, e com a roupa de verdade de um habitante)");
			if (_vitima == 0) { Fechar(); return; }
			_t = 0;
			return;
		}
		if (_t < 1.5) return;   // o corpo e a aparencia dela atravessam o fio

		_roupasDaVitima = VisualDe(mundo, _vitima)?.RoupasNoCorpoDeTeste() ?? [];
		_roupasAntes = mundo.VisualLocalDeTeste?.RoupasNoCorpoDeTeste() ?? [];
		Nota($"antes: eu visto {_roupasAntes.Count} peca(s); a vitima veste {_roupasDaVitima.Count} ({string.Join(", ", _roupasDaVitima.Select(Curto))})");
		Guardar(_tiraDaRoupa, Foto(mundo, Meio(mundo, cli, _vitima), "dmajin-6a-antes.png", "ANTES: o Majin e a vitima, cada um com a sua roupa", 110f, 90f));
		Ir(MApertar);
	}

	// =====================================================================
	// ATO DO MAJIN -- 2) ABSORVER pelo botao do menu
	// =====================================================================
	private void ApertarAbsorver(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				Conferir(srv.DerrubarNaFotoDoMajin(_vitima, new Vec2(-1, 0)), "a vitima foi NOCAUTEADA (o nocaute de producao) ao meu lado");
				Sub(1);
				return;

			case 1:
				if (_t < 0.8) return;
				// O BOTAO, e nao o pacote: o `Acionar` e o que o clique no menu (e a tecla ligada a ele) chama.
				Botao("hab:absorver")?.Acionar?.Invoke();
				Sub(2);
				return;

			default:
				if (cli.AbsorvidosDoMajin.Count > 0) { Ir(MAbsorvido); return; }
				if (_t < 5) return;
				Conferir(false, "apertei ABSORVER no menu e a lista de absorvidos chegou do servidor");
				Fechar();
				return;
		}
	}

	private void Absorvi(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.5) return;   // a vitima sai da minha zona e a minha aparencia nova atravessam o fio

		GameClient.AbsorvidoDoMajin a = cli.AbsorvidosDoMajin[0];
		_numero = a.Numero;
		_nomeDaVitima = a.Nome;
		Conferir(cli.AbsorvidosDoMajin.Count == 1 && !a.Devorado && a.Nome.Length > 0,
			$"apertei ABSORVER no menu e o servidor respondeu com a lista: 1 absorvido (`{a.Nome}`, selado e nao devorado)");

		(int majin, int imagem, _) = srv.PrisaoNaFotoDoMajin(_vitima);
		var ela = srv.CorpoNaFotoDoMajin(_vitima);
		Conferir(majin == cli.LocalId && InteriorDoMajin.Anfitriao(ela.Zona) == cli.LocalId && imagem != 0,
			$"no servidor a vitima esta DENTRO DE MIM (zona `{ela.Zona.Name}` #{InteriorDoMajin.Anfitriao(ela.Zona)}), com uma imagem minha de guarda");
		Conferir(mundo.CorpoDeTeste(_vitima) == null, "...e SUMIU da minha tela: foi engolida");

		var ganho = srv.GanhoNaFotoDoMajin(cli.LocalId);
		Conferir(ganho.Poder > 0, $"eu uso +{ganho.Poder:N0} de poder emprestado dela (10%)");

		Verbo? um = Botao($"hab:expelir:{_numero}");
		Conferir(um != null && um.Nome.Contains(a.Nome, StringComparison.Ordinal) && Botao("hab:expelir") is { PodeAgora: true },
			$"o menu ganhou o botao `{um?.Nome ?? "Expelir: ..."}`, e o EXPELIR TODOS acendeu");

		List<string> agora = mundo.VisualLocalDeTeste?.RoupasNoCorpoDeTeste() ?? [];
		if (_roupasDaVitima.Count > 0)
			Conferir(_roupasDaVitima.All(agora.Contains) && !_roupasDaVitima.All(_roupasAntes.Contains),
				$"EU VISTO A ROUPA DELA: o meu boneco tem agora {agora.Count} peca(s) ({string.Join(", ", agora.Select(Curto))})");
		else Nota("a vitima sorteada nao veste roupa nenhuma: a regua da roupa nao mede nada nesta corrida");

		Guardar(_tiraDaRoupa, Foto(mundo, MeuLugar(mundo, cli), "dmajin-6b-depois.png", "DEPOIS: a vitima la dentro, e a roupa dela em mim", 110f, 90f));
		Ir(MMenu);
	}

	/// <summary>
	/// O MENU ABERTO NA ABA DAS SKILLS, em duas fotos: o topo (a skill e o `Absorver`) e, rolando, os botoes
	/// de expelir. E ali que o `Expelir: nome` aparece pro jogador -- e a regua cobra que ele esteja
	/// DESENHADO na pagina, e nao so no registro de verbos.
	/// </summary>
	private void OMenu(GameClient cli)
	{
		if (MenuJogo.Instancia is not { } menu) { Conferir(false, "ha um menu de jogo na arvore pra fotografar"); Ir(MExpelir); return; }
		switch (_sub)
		{
			case 0:
				menu.Abrir();
				menu.IrPara(Verbos.Skills);
				Sub(1);
				return;

			case 1:
			{
				if (_t < 0.5) return;
				if (Tela() is { } topo) Gravar(topo, "dmajin-7-menu.png", "O MENU DO MAJIN com gente dentro: a skill e o `Absorver`");

				Button? expelir = BotaoNaPagina(menu, $"Expelir: {_nomeDaVitima}");
				Conferir(expelir != null, $"o botao `Expelir: {_nomeDaVitima}` esta DESENHADO na aba de skills do menu");
				if (expelir != null) RoloDe(expelir)?.EnsureControlVisible(expelir);
				Sub(2);
				return;
			}

			default:
				if (_t < 0.4) return;
				if (Tela() is { } baixo) Gravar(baixo, "dmajin-8-menu-expelir.png", "...e, rolando a aba, o `Expelir todos` e o `Expelir: nome`");
				menu.Fechar();
				Ir(MExpelir);
				return;
		}
	}

	/// <summary>O botao da aba de skills cujo texto comeca assim -- o que o jogador clicaria.</summary>
	private static Button? BotaoNaPagina(MenuJogo menu, string comeco)
	{
		if (menu.PaginaDeTeste(Verbos.Skills) is not { } pagina) return null;
		foreach (Node n in pagina.FindChildren("*", "Button", true, false))
			if (n is Button b && b.Text.StartsWith(comeco, StringComparison.Ordinal)) return b;
		return null;
	}

	private static ScrollContainer? RoloDe(Control c)
	{
		for (Node? n = c.GetParent(); n != null; n = n.GetParent())
			if (n is ScrollContainer s) return s;
		return null;
	}

	// =====================================================================
	// ATO DO MAJIN -- 3) EXPELIR pelo botao do menu
	// =====================================================================
	private void ApertarExpelir(GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 0.4) return;
			Botao($"hab:expelir:{_numero}")?.Acionar?.Invoke();
			Sub(1);
			return;
		}
		if (cli.AbsorvidosDoMajin.Count == 0) { Ir(MSolto); return; }
		if (_t < 5) return;
		Conferir(false, $"apertei `Expelir: {_nomeDaVitima}` no menu e a lista esvaziou");
		Ir(MSolto);
	}

	private void Soltei(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.5) return;   // a vitima volta pra minha zona e o corpo dela atravessa o fio

		var eu = srv.CorpoNaFotoDoMajin(cli.LocalId);
		var ela = srv.CorpoNaFotoDoMajin(_vitima);
		float d = Vec2.Distance(eu.Pos, ela.Pos);
		Conferir(cli.AbsorvidosDoMajin.Count == 0 && ela.Existe && ela.Zona.Hash == eu.Zona.Hash && d <= 2.5f * T,
			$"apertei `Expelir: {_nomeDaVitima}` no menu e ela foi CUSPIDA de volta, ao meu lado (a {d / T:0.#} tiles)");
		Conferir(mundo.CorpoDeTeste(_vitima) != null, "...e voltou pra minha tela");
		Nota(ela.Caido
			? "ela saiu caida: a minha imagem a nocauteou la dentro (expelir nao acorda ninguem, e la tambem nao)"
			: "ela saiu de pe");

		var ganho = srv.GanhoNaFotoDoMajin(cli.LocalId);
		Conferir(ganho.Poder == 0 && ganho.Verbos == 0 && ganho.Dentro == 0, "eu perdi o poder e os verbs que tinha por causa dela");
		Conferir(Botao($"hab:expelir:{_numero}") == null && Botao("hab:expelir") is { PodeAgora: false },
			"o botao dela saiu do menu, e o EXPELIR TODOS apagou de novo");

		List<string> agora = mundo.VisualLocalDeTeste?.RoupasNoCorpoDeTeste() ?? [];
		if (_roupasDaVitima.Count > 0)
			Conferir(_roupasDaVitima.All(agora.Contains),
				"a roupa da ultima refeicao FICA em mim (la so a absorcao seguinte a troca: `majin_clear_outfit` tem um chamador so)");

		Guardar(_tiraDaRoupa, Foto(mundo, Meio(mundo, cli, _vitima), "dmajin-6c-solta.png", "SOLTA: ela de volta ao meu lado, e a roupa dela ainda em mim", 110f, 90f));
		Colar("dmajin-6-roupa.png", _tiraDaRoupa, "A ROUPA DA ULTIMA REFEICAO: antes / com ela la dentro / depois de expelir");
		Ir(MDefeito);
	}

	// =====================================================================
	// ATO DO MAJIN -- 4) a contraprova: o menu que nao remonta os botoes
	// =====================================================================
	private void DefeitoDoMenu(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				MenuJogo.AbsorvidosNaoRemontamDeTeste = true;
				// O RUMO E O MEU: ela voltou um passo a minha frente, onde quer que isso tenha caido.
				srv.DerrubarNaFotoDoMajin(_vitima, srv.CorpoNaFotoDoMajin(cli.LocalId).Pos - srv.CorpoNaFotoDoMajin(_vitima).Pos);
				Sub(1);
				return;

			case 1:
				if (_t < 0.8) return;
				// PELO GANCHO, que vence a recarga de 2 s do verb: aqui o que se mede e o menu, e nao o botao.
				srv.HabilidadeNaFotoDoMajin(cli.LocalId, "absorver");
				Sub(2);
				return;

			case 2:
			{
				if (cli.AbsorvidosDoMajin.Count == 0)
				{
					if (_t < 5) return;
					MenuJogo.AbsorvidosNaoRemontamDeTeste = false;
					Conferir(false, "(defeito injetado) absorvi de novo e a lista chegou");
					Ir(-1);
					return;
				}
				if (_t < 0.6) return;   // tempo de sobra pra qualquer remonte que fosse acontecer

				int numero = cli.AbsorvidosDoMajin[0].Numero;
				bool semBotao = Botao($"hab:expelir:{numero}") == null;
				MenuJogo.AbsorvidosNaoRemontamDeTeste = false;
				Conferir(semBotao,
					"(defeito injetado: o menu nao remonta quando a lista muda) a regua do botao REPROVA -- ha 1 absorvido "
					+ "e nenhum `Expelir: nome` no menu");

				// E A SAIDA DE TODOS, pelo pacote do botao `Expelir todos` (que nao depende do remonte: ele e fixo).
				cli.SendHabilidade("expelir");
				Sub(3);
				return;
			}

			default:
				if (cli.AbsorvidosDoMajin.Count > 0 && _t < 5) return;
				Conferir(cli.AbsorvidosDoMajin.Count == 0 && srv.GanhoNaFotoDoMajin(cli.LocalId).Dentro == 0,
					"EXPELIR TODOS soltou quem estava dentro de mim");
				Ir(-1);
				return;
		}
	}

	// =====================================================================
	// O OBTURADOR: tres fotos do mesmo instante, com os alvos escondidos nas duas de fora
	// =====================================================================
	private struct Fotos
	{
		public Image Com, Sem, Sem2;
		public bool Falhou;
	}

	private int _obtFase, _obtQuadros;
	private Image? _obtCom, _obtSem;
	private readonly List<CanvasItem> _obtAlvos = [];
	private readonly List<bool> _obtEram = [];

	private void Mirar(IEnumerable<CanvasItem> alvos)
	{
		_obtAlvos.Clear();
		_obtAlvos.AddRange(alvos);
	}

	/// <summary>
	/// ESCONDE, FOTOGRAFA, MOSTRA, FOTOGRAFA, ESCONDE, FOTOGRAFA -- com a arvore pausada, e dois quadros de
	/// folga a cada troca (`GetImage` devolve o ULTIMO quadro renderizado). Os alvos sao os do
	/// <see cref="Mirar"/>; SEM ALVO NENHUM as tres fotos saem iguais e a tinta e zero, que e a resposta
	/// certa pra "nao ha cenario pra esconder". Devolve falso enquanto nao acabou.
	/// </summary>
	private bool Obturador(out Fotos f)
	{
		f = default;
		switch (_obtFase)
		{
			case 0:
				GetTree().Paused = true;
				_obtEram.Clear();
				foreach (CanvasItem c in _obtAlvos) _obtEram.Add(IsInstanceValid(c) && c.Visible);
				Mostrar(false);
				_obtFase = 1; _obtQuadros = 0;
				return false;

			case 1:
				if (_obtQuadros++ < 2) return false;
				_obtSem = Tela();
				Mostrar(true);
				_obtFase = 2; _obtQuadros = 0;
				return false;

			case 2:
				if (_obtQuadros++ < 2) return false;
				_obtCom = Tela();
				Mostrar(false);
				_obtFase = 3; _obtQuadros = 0;
				return false;

			default:
			{
				if (_obtQuadros++ < 2) return false;
				Image? sem2 = Tela();
				Mostrar(true);

				if (_obtCom == null || _obtSem == null || sem2 == null) f.Falhou = true;
				else { f.Com = _obtCom; f.Sem = _obtSem; f.Sem2 = sem2; }
				ZerarObturador();
				return true;
			}
		}
	}

	/// <summary>Mostra (cada um como estava antes) ou esconde os alvos do obturador.</summary>
	private void Mostrar(bool sim)
	{
		for (int i = 0; i < _obtAlvos.Count; i++)
			if (IsInstanceValid(_obtAlvos[i])) _obtAlvos[i].Visible = sim && _obtEram[i];
	}

	private void ZerarObturador()
	{
		_obtFase = 0; _obtQuadros = 0;
		_obtCom = null; _obtSem = null;
		_obtAlvos.Clear();
		_obtEram.Clear();
		if (GetTree() is { Paused: true } t) t.Paused = false;
	}

	// =====================================================================
	// A MEDIDA
	// =====================================================================
	/// <summary>A janela medida: tantos px de mundo pra cada lado do ponto, cortada na borda da imagem.</summary>
	private Rect2I Janela(Image img, Vector2 centroNoMundo, float meiaLargura, float meiaAltura)
	{
		Vector2 c = NaImagem(img, centroNoMundo);
		float e = EscalaDaImagem(img);
		int x0 = Math.Clamp((int)(c.X - meiaLargura * e), 0, img.GetWidth() - 1), x1 = Math.Clamp((int)(c.X + meiaLargura * e), 1, img.GetWidth());
		int y0 = Math.Clamp((int)(c.Y - meiaAltura * e), 0, img.GetHeight() - 1), y1 = Math.Clamp((int)(c.Y + meiaAltura * e), 1, img.GetHeight());
		return new Rect2I(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
	}

	/// <summary>Quantos pixels da janela sao tinta dos alvos, e quantos pixels a janela tem.</summary>
	private (int Tinta, int Total) Tinta(Fotos f, Vector2 centroNoMundo, float meia, string? mascara, float limiar = Epsilon)
	{
		Rect2I j = Janela(f.Com, centroNoMundo, meia, meia);
		Image? pintada = null;
		if (mascara != null) { pintada = f.Com.GetRegion(j); pintada.Convert(Image.Format.Rgba8); }

		int n = 0;
		for (int y = j.Position.Y; y < j.Position.Y + j.Size.Y; y++)
			for (int x = j.Position.X; x < j.Position.X + j.Size.X; x++)
			{
				if (!EhTinta(f, x, y, limiar)) continue;
				pintada?.SetPixel(x - j.Position.X, y - j.Position.Y, new Color(1f, 0f, 1f));
				n++;
			}

		if (pintada != null && mascara != null) Gravar(pintada, mascara, "A MASCARA (magenta = o que a sonda contou como tinta)");
		return (n, j.Size.X * j.Size.Y);
	}

	/// <summary>A fracao da janela em volta do corpo que e tinta do CENARIO -- com o limiar fino dele.</summary>
	private float Fracao(Fotos f, Vector2 centroNoMundo, string? mascara)
	{
		(int tinta, int total) = Tinta(f, centroNoMundo, MeiaJanela, mascara, EpsilonDoCenario);
		return total > 0 ? tinta / (float)total : 0f;
	}

	/// <summary>A regra das tres fotos: difere das DUAS sem o alvo, e as duas concordam entre si ali.</summary>
	private static bool EhTinta(Fotos f, int x, int y, float limiar)
	{
		Color com = f.Com.GetPixel(x, y), sem = f.Sem.GetPixel(x, y), sem2 = f.Sem2.GetPixel(x, y);
		return !Difere(sem, sem2, limiar) && Difere(com, sem, limiar) && Difere(com, sem2, limiar);
	}

	private static bool Difere(Color p, Color q, float limiar)
		=> MathF.Abs(p.R - q.R) > limiar || MathF.Abs(p.G - q.G) > limiar || MathF.Abs(p.B - q.B) > limiar;

	// =====================================================================
	// AS FERRAMENTAS DE CENA E DE TELA
	// =====================================================================
	private static CharacterVisual? VisualDe(World mundo, int id) => mundo.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>Onde o MEU corpo esta desenhado, em px de mundo.</summary>
	private static Vector2 MeuLugar(World mundo, GameClient cli) => mundo.PosicaoDesenhadaDe(cli.LocalId) ?? Vector2.Zero;

	/// <summary>O meio do caminho entre o meu corpo e o de outro, desenhados -- o centro das fotos a dois.</summary>
	private static Vector2 Meio(World mundo, GameClient cli, int outro)
	{
		Vector2 eu = MeuLugar(mundo, cli);
		return mundo.PosicaoDesenhadaDe(outro) is { } o ? (eu + o) / 2f : eu;
	}

	/// <summary>So o nome do arquivo de uma folha, pras linhas do relato.</summary>
	private static string Curto(string caminho) => caminho.Length == 0 ? "?" : caminho[(caminho.LastIndexOf('/') + 1)..];

	private static void Guardar(List<Image> tira, Image? foto) { if (foto != null) tira.Add(foto); }

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>De ponto do mundo pra pixel da imagem (a `CanvasTransform` e a razao imagem / viewport).</summary>
	private Vector2 NaImagem(Image img, Vector2 mundo)
	{
		Vector2 v = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		Vector2 tam = GetViewport()?.GetVisibleRect().Size ?? img.GetSize();
		return new Vector2(v.X * img.GetWidth() / tam.X, v.Y * img.GetHeight() / tam.Y);
	}

	/// <summary>Quantos pixels de imagem vale um pixel de mundo -- medido, e nao lido do `Zoom`.</summary>
	private float EscalaDaImagem(Image img)
	{
		float e = (NaImagem(img, new Vector2(T, 0)) - NaImagem(img, Vector2.Zero)).Length() / T;
		return e > 0.01f ? e : 1f;
	}

	private Image? Recortar(Image img, Vector2 centroNoMundo, float meiaLargura, float meiaAltura)
	{
		Image r = img.GetRegion(Janela(img, centroNoMundo, meiaLargura, meiaAltura));
		r.Convert(Image.Format.Rgba8);
		return r;
	}

	/// <summary>A foto do instante, recortada em volta de um ponto do mundo, gravada e devolvida pras tiras.</summary>
	private Image? Foto(World mundo, Vector2 centroNoMundo, string nome, string rotulo, float meiaLargura = 170f, float meiaAltura = 120f)
	{
		if (Tela() is not { } tela) { Nota($"{rotulo}: a janela nao devolveu imagem"); return null; }
		Image? r = Recortar(tela, centroNoMundo, meiaLargura, meiaAltura);
		if (r != null) Gravar(r, nome, rotulo);
		return r;
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

	/// <summary>As fotos lado a lado (o `BlitRect` exige o mesmo formato nos dois lados, e cala quando nao tem).</summary>
	private void Colar(string nome, List<Image> pedacos, string rotulo)
	{
		if (pedacos.Count == 0) return;

		const int Vao = 8;
		int alt = 0, larg = 0;
		foreach (Image p in pedacos) { alt = Math.Max(alt, p.GetHeight()); larg += p.GetWidth() + Vao; }

		Image colagem = Image.CreateEmpty(Math.Max(1, larg - Vao), Math.Max(1, alt), false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));
		int x = 0;
		foreach (Image p in pedacos)
		{
			Image c = p.Duplicate() as Image ?? p;
			c.Convert(Image.Format.Rgba8);
			colagem.BlitRect(c, new Rect2I(Vector2I.Zero, c.GetSize()), new Vector2I(x, 0));
			x += c.GetWidth() + Vao;
		}
		Gravar(colagem, nome, rotulo);
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		DesligarOsDefeitos();
		if (GetTree() is { } t) t.Paused = false;
		Mostrar(true);
		World.Instancia?.PararDeTeste();
		MenuJogo.Instancia?.Fechar();
		if (S is { } srv)
		{
			// QUEM ESTIVER DENTRO DE ALGUEM SAI ANTES DE OS CORPOS SUMIREM, pela porta de producao: os meus (no
			// ato do Majin) pelo verb, os do Majin forjado pelo `LimparAFotoDoMajin`.
			if (_souMajin && C is { } cli) srv.HabilidadeNaFotoDoMajin(cli.LocalId, "expelir");
			srv.LimparAFotoDoMajin();
			srv.LimparAFoto();
		}

		if (_viradaEm >= 0) _maiorAtraso = Math.Max(_maiorAtraso, _vida - _viradaEm);
		if (_viradas > 0)
			Conferir(_maiorAtraso <= AtrasoToleradoDoDesenho,
				$"o DESENHO do meu corpo seguiu o servidor nas {_viradas} virada(s) de \"caido\" (pior atraso: {_maiorAtraso:0.00} s; "
				+ $"a regua pede {AtrasoToleradoDoDesenho:0.00})");

		GD.Print("\n[dmajin] ===== A ABSORCAO DO MAJIN NA TELA -- " + (_souMajin ? "O ATO DO MAJIN" : "O ATO DO PRESO") + " =====");
		foreach (string l in _linhas) GD.Print("[dmajin] " + l);
		GD.Print(_falhas.Count == 0
			? "[dmajin] ===== TUDO OK ====="
			: $"[dmajin] ===== {_falhas.Count} FALHA(S) =====\n[dmajin]   " + string.Join("\n[dmajin]   ", _falhas));
		GetTree().Quit();
	}
}
