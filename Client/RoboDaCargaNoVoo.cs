using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A CARGA E O RAIO DE QUEM VOA, FOTOGRAFADOS EM TRES ALTURAS (`--diagcargavoo`).
///
/// ============================ O PEDIDO DO DONO (2026-10-09) ============================
/// *"o efeito de carregar o beam e soltar ele, deve acompanhar o personagem n importa a altura q ele
/// esteja voando"*.
///
/// SAO DUAS COISAS NA FRASE, e a bancada mede as duas em cada altura:
///   * a CARGA -- a bola que se junta na mao (<see cref="CargaDeRaioVisual"/>). Era ela o defeito: o node
///     nasce quando o canal abre, com o corpo JA parado no ar, e ninguem o levantava (ver
///     <see cref="SubirComOVoo.AoNascer"/>). Ficava no plano do chao: 12 px abaixo das maos pairando, 160 no teto;
///   * o RAIO solto -- que ja subia (o `ProjetilDesenhado.Altitude`, a cena C da `--diagboca`, numa altura so).
///     Aqui ele e cobrado nas mesmas tres alturas, porque o pedido diz "n importa a altura".
/// =======================================================================================
///
/// ============================ COMO SE MEDE, E POR QUE NAO PELO CAMPO ============================
/// Pela regra das TRES FOTOS da `--diagboca` (o cabecalho de la conta por que sao tres): com a arvore
/// PAUSADA, esconde-se o node, fotografa-se, mostra-se, fotografa-se, esconde-se, fotografa-se. Tinta do
/// node e o pixel que difere das duas fotos sem ele, onde as duas concordam. O centro dessa tinta e
/// comparado com onde o corpo esta DESENHADO.
///
/// O campo (`Position` do node igual a do `Visual`) tambem e conferido, e nao basta: e exatamente o tipo de
/// leitura que ja ficou verde nesta casa com a tela errada.
///
/// A CONTRAPROVA liga o defeito de antes (`SubirComOVoo.FilhoNovoFicaNoChaoDeTeste`) na altura do meio e
/// cobra que a MESMA regua ache a bola no chao; a do raio zera a altura no node do tiro, como a cena C da
/// `--diagboca`.
///
/// E O CORPO DOS OUTROS: a mesma bola num corpo REMOTO voando (`RemotePlayer`), porque o defeito morava nos
/// dois corpos e eu so me vejo voando -- os outros eu vejo o tempo todo.
/// ==================================================================================================
///
/// COMO RODAR -- um processo so, com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 7988 --vooteste --bpteste 3000000 --horateste 0.5 --campoteste \
///           --diagcargavoo --position 1920,0 --resolution 1600x900 \
///           --raca Human --conta bancada_cargavoo --nome Voador
///
/// As fotos saem em `user://cargavoo-*.png`. Comecar pelas tiras: `cargavoo-1-carga.png` (as tres alturas),
/// `cargavoo-2-raio.png`, `cargavoo-3-antes-e-depois.png` e `cargavoo-4-corpo-alheio.png`.
/// </summary>
public partial class RoboDaCargaNoVoo : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>O raio da cena: 1,8 s de carga com a pericia deste corpo -- ha tempo de fotografar a bola cheia.</summary>
	private const string Verbo = "Kamehameha";

	/// <summary>
	/// AS TRES ALTURAS, em pixel de mundo: pairando (a de quem so liga o voo), o meio da subida e o teto.
	/// Na tela sao 12, 80 e 160 px acima do chao (`Voo.EscalaNaTela`).
	/// </summary>
	private static readonly float[] Alturas = [Voo.AlturaDePairar, 10 * ZoneCollision.TileSize, Voo.AlturaMaxima];

	/// <summary>
	/// EM QUAL DELAS OS DEFEITOS SAO INJETADOS: a do meio. Pairando o desvio (12 px) e pequeno demais pra
	/// uma foto de antes e depois; no teto o ponto do chao pode cair fora da janela.
	/// </summary>
	private const int DaContraprova = 1;

	/// <summary>Ate onde o centro da tinta da bola pode ficar do ponto da mao, em px de mundo.</summary>
	private const float FolgaDaMao = 4f;

	/// <summary>Ate onde o centro da tinta do raio (atirado pro lado) pode ficar da linha do corpo, em px de mundo.</summary>
	private const float FolgaDoRaio = 8f;

	/// <summary>Meio lado da janela medida em volta do corpo desenhado, em px de mundo: cabe o ponto do chao do teto.</summary>
	private const float MeiaJanela = 200f;

	/// <summary>O 0,12 das outras bancadas de foto: abaixo disso e ruido do viewport.</summary>
	private const float Epsilon = 0.12f;

	/// <summary>Quanto a bola vive antes da foto: ela enche em meio segundo (`CargaDeRaioVisual`).</summary>
	private const double IdadeDaCarga = 0.75;

	/// <summary>Quantos tiles a cabeca do raio anda antes da foto dele.</summary>
	private const double TilesDoRaio = 3.0;

	private const double Paciencia = 240;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo, _iAltura;

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

	public override void _ExitTree() => SubirComOVoo.FilhoNovoFicaNoChaoDeTeste = false;

	private const int PAssentar = 0, PVirar = 1, PSubir = 2, PApertar = 3, PCarga = 4, PRaio = 5, PRaioNoChao = 6,
					  PProxima = 7, PDefeitoSubir = 8, PDefeitoApertar = 9, PDefeitoCarga = 10,
					  PAlheioPlantar = 11, PAlheioApertar = 12, PAlheioCarga = 13;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagcargavoo` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s) no passo {_passo}"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case PVirar: VirarPraLeste(mundo, srv, cli); break;
			case PSubir: Subir(mundo, srv, cli, Alturas[_iAltura], PApertar); break;
			case PApertar: Apertar(srv, cli.LocalId, PCarga); break;
			case PCarga: NaCarga(mundo, srv, cli); break;
			case PRaio: NoRaio(mundo, srv, cli); break;
			case PRaioNoChao: RaioNoChao(mundo, srv, cli); break;
			case PProxima: Proxima(srv, cli); break;
			case PDefeitoSubir: Subir(mundo, srv, cli, Alturas[DaContraprova], PDefeitoApertar); break;
			case PDefeitoApertar: DefeitoApertar(srv, cli); break;
			case PDefeitoCarga: DefeitoNaCarga(mundo, srv, cli); break;
			case PAlheioPlantar: AlheioPlantar(mundo, srv, cli); break;
			case PAlheioApertar: Apertar(srv, _alheio, PAlheioCarga); break;
			case PAlheioCarga: AlheioNaCarga(mundo, srv, cli); break;
			default: Fechar(); break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; _desdeQueNasceu = -1; _alturaVista = -1; _paradaDesde = 0; }

	/// <summary>A ultima altura desenhada que o passo viu, e desde quando ela nao muda. Ver <see cref="Subir"/>.</summary>
	private float _alturaVista = -1;
	private double _paradaDesde;

	// =====================================================================
	// 0) O BERCO ASSENTA, E O CORPO VIRA PRO LADO
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3) return;

		// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim).
		(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
		if ((ko || morto) && _t < 30) return;
		Conferir(!ko && !morto, "o corpo entrou em pe e acordado");

		(int skills, int verbos) = srv.ArmarParaAVariedade(cli.LocalId);
		Conferir(skills > 0 && verbos > 0, $"o corpo aprendeu o catalogo ({skills} skills, {verbos} verbos) -- o raio sai pelo VERB");

		bool praca = srv.AssentarNaBoca(cli.LocalId, 6);
		Conferir(praca, "achei uma PRACA com os quatro lados livres (seis tiles em cada)");
		if (!praca) { Fechar(); return; }

		// MEIO-DIA E CEU LIMPO: de dia a `LuzDeKi` nao acende, e luz entraria na mascara como tinta.
		srv.CravarMeioDiaDaVariedade(cli.LocalId);
		Ir(PVirar);
	}

	private bool _parou;

	/// <summary>
	/// PRO LESTE, ANDANDO (a direcao do corpo local e previsao do cliente: ver `RoboDeBocaDeCano.A_Virar`).
	/// De lado porque a regua do raio e "o centro da tinta fica na LINHA do corpo": pra cima ou pra baixo o
	/// proprio feixe anda no eixo que se mede.
	/// </summary>
	private void VirarPraLeste(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (!_parou)
		{
			(_, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);
			if (olhar != Facing.East && _t < 2.0) { mundo.AndarDeTeste(Vector2.Right); return; }
			mundo.PararDeTeste();
			_parou = true;
			_t = 0;
			return;
		}
		if (_t < 0.35) return;
		srv.ReporNaBoca(cli.LocalId);
		if (_t < 1.1) return;   // a correcao de posicao precisa atravessar o fio

		(_, Facing agora, _, _) = srv.CorpoDaBoca(cli.LocalId);
		Conferir(agora == Facing.East, $"o corpo virou pro leste andando, e o servidor concorda (diz {agora})");
		Ir(PSubir);
	}

	// =====================================================================
	// 1) SOBE, APERTA, FOTOGRAFA A CARGA, FOTOGRAFA O RAIO -- por altura
	// =====================================================================
	private void Subir(World mundo, Jandirus.Server.GameServer srv, GameClient cli, float altura, int depois)
	{
		// REAFIRMADA A CADA QUADRO: a altura e CONDICAO da foto, e nao o que se mede (quem mede subida e a `--diagvoo`).
		bool subiu = srv.VoarNaBoca(cli.LocalId, altura);
		if (!subiu) { Conferir(false, $"o corpo levantou voo pelo funil de producao ({altura:0} px)"); Fechar(); return; }
		srv.ReporNaBoca(cli.LocalId);

		// O DESENHO PERSEGUE A ALTURA DO SERVIDOR (`LocalPlayer.SeguirAltura`), e a cena espera ele CHEGAR E PARAR:
		// enquanto a altura desenhada anda, a varredura dos filhos roda todo quadro e levantaria a bola de
		// qualquer jeito -- o defeito que esta bancada mede so existe com o corpo parado no ar.
		float agora = mundo.AlturaDeTeste;
		if (MathF.Abs(agora - _alturaVista) > 0.001f) { _alturaVista = agora; _paradaDesde = _t; }
		bool chegou = MathF.Abs(agora - altura) <= 3f && _t - _paradaDesde >= 0.4;
		if (!chegou && _t < 6) return;

		Conferir(chegou,
			$"{Rotulo(altura)}: o corpo esta desenhado a {mundo.AlturaDeTeste:0} px de altura "
			+ $"({mundo.AlturaDeTeste * Voo.EscalaNaTela:0} px acima do chao, na tela)");
		Ir(depois);
	}

	private static string Rotulo(float altura) => $"ALTURA {altura:0} ({altura / ZoneCollision.TileSize:0.#} tiles)";

	private void Apertar(Jandirus.Server.GameServer srv, int quem, int depois)
	{
		srv.RegarOKiDaVariedade(quem);
		srv.LimparOsTirosDaBoca(quem);
		string resposta = srv.GatilhoDaVariedade(quem, Verbo);
		if (resposta.Length > 0) Nota($"o servidor respondeu ao {Verbo}: \"{resposta}\"");
		Ir(depois);
	}

	/// <summary>Quando a bola da vez nasceu, no relogio do passo. -1 = ainda nao.</summary>
	private double _desdeQueNasceu = -1;

	/// <summary>
	/// A BOLA DESTE CORPO JA ESTA CHEIA? Devolve o node quando ela existe ha <see cref="IdadeDaCarga"/>;
	/// nulo enquanto espera. `desistiu` = o canal nao abriu em 4 s (ou a bola morreu antes da foto).
	/// </summary>
	private CargaDeRaioVisual? BolaCheia(World mundo, int quem, out bool desistiu)
	{
		desistiu = false;
		var bola = mundo.CorpoDeTeste(quem)?.GetNodeOrNull<CargaDeRaioVisual>(CargaDeRaioVisual.NomeDoNode);
		if (bola == null)
		{
			desistiu = _desdeQueNasceu >= 0 || _t > 4;
			return null;
		}
		if (_desdeQueNasceu < 0) _desdeQueNasceu = _t;
		return _t - _desdeQueNasceu >= IdadeDaCarga ? bola : null;
	}

	private readonly List<Image> _tiraDaCarga = [], _tiraDoRaio = [];
	private Image? _cargaBoaDaContraprova, _raioBomDaContraprova;

	private void NaCarga(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		float altura = Alturas[_iAltura];
		if (_obtFase == 0)
		{
			CargaDeRaioVisual? bola = BolaCheia(mundo, cli.LocalId, out bool desistiu);
			if (desistiu) { Conferir(false, $"{Rotulo(altura)}: o canal abriu e a bola da carga nasceu"); Ir(PProxima); return; }
			if (bola == null) return;
			_obtAlvo = bola;
		}
		if (!Obturador(mundo, cli.LocalId, out Fotos f)) return;
		if (f.Falhou) { Conferir(false, $"{Rotulo(altura)}: a janela renderizou a carga"); Ir(PProxima); return; }

		JulgarACarga(mundo, srv, f, cli.LocalId, Rotulo(altura), $"cargavoo-carga-{altura:0}-mascara.png");
		Image recorte = Recorte(f);
		_tiraDaCarga.Add(recorte);
		if (_iAltura == DaContraprova) _cargaBoaDaContraprova = recorte;
		Ir(PRaio);
	}

	/// <summary>
	/// A REGUA DA CARGA: ha tinta, o centro dela esta NA MAO do corpo desenhado (a conta do proprio
	/// <see cref="CargaDeRaioVisual.Mao"/>), e o node carrega o mesmo deslocamento do `Visual`.
	/// </summary>
	private void JulgarACarga(World mundo, Jandirus.Server.GameServer srv, Fotos f, int quem, string rotulo, string mascara)
	{
		Medida m = Medir(f, mascara);
		(_, Facing olhar, _, _) = srv.CorpoDaBoca(quem);
		Vector2 mao = CargaDeRaioVisual.Mao(olhar);
		float erro = (m.Centro - mao).Length();

		Conferir(m.Pixels > 30, $"{rotulo}: a bola da carga esta pintada na tela ({m.Pixels} px de tinta)");
		Conferir(m.Pixels > 30 && erro <= FolgaDaMao,
			$"{rotulo}: ...e ela se junta NA MAO do corpo desenhado -- centro da tinta em ({m.Centro.X:0.#}, {m.Centro.Y:0.#}) px "
			+ $"do centro do sprite, contra ({mao.X:0.#}, {mao.Y:0.#}) da conta da mao ({erro:0.#} px de erro)");

		Node2D? corpo = mundo.CorpoDeTeste(quem);
		Vector2? doVisual = corpo?.GetNodeOrNull<CharacterVisual>("Visual")?.Position;
		Vector2? daBola = corpo?.GetNodeOrNull<CargaDeRaioVisual>(CargaDeRaioVisual.NomeDoNode)?.Position;
		Conferir(doVisual != null && daBola != null && doVisual.Value.DistanceTo(daBola.Value) < 0.01f,
			$"{rotulo}: ...e o node da bola subiu com o corpo (bola em {daBola}, sprite em {doVisual})");
	}

	private void NoRaio(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		float altura = Alturas[_iAltura];
		if (_obtFase == 0)
		{
			(bool vivo, _, _, _, double andou, _) = srv.TiroDaBoca(cli.LocalId);
			ProjetilDesenhado? no = vivo ? NoDoTiro(mundo, srv) : null;
			if (no == null || andou < TilesDoRaio)
			{
				if (_t < 10) return;
				Conferir(false, $"{Rotulo(altura)}: o {Verbo} saiu e andou {TilesDoRaio:0} tiles (vivo={vivo}, andou={andou:0.0})");
				Ir(PProxima);
				return;
			}
			_obtAlvo = no;
		}
		if (!Obturador(mundo, cli.LocalId, out Fotos f)) return;
		if (f.Falhou) { Conferir(false, $"{Rotulo(altura)}: a janela renderizou o raio"); Ir(PProxima); return; }

		Medida m = Medir(f, null);
		Conferir(mundo.CorpoDeTeste(cli.LocalId)?.GetNodeOrNull(CargaDeRaioVisual.NomeDoNode) == null,
			$"{Rotulo(altura)}: SOLTOU -- a bola da carga morreu no instante em que o feixe nasceu");
		Conferir(m.Pixels > 200, $"{Rotulo(altura)}: o raio esta pintado na janela ({m.Pixels} px de tinta)");
		Conferir(m.Pixels > 200 && MathF.Abs(m.Centro.Y) <= FolgaDoRaio && m.Centro.X > 0,
			$"{Rotulo(altura)}: ...e ele sai NA ALTURA DO CORPO -- centro da tinta {m.Centro.Y:+0.#;-0.#;0} px da linha do sprite "
			+ $"(o plano do chao ficou {altura * Voo.EscalaNaTela:0} px abaixo), {m.Centro.X:0.#} px a frente");

		Image recorte = Recorte(f);
		_tiraDoRaio.Add(recorte);
		_medidaDoRaio = m;
		if (_iAltura == DaContraprova) { _raioBomDaContraprova = recorte; Ir(PRaioNoChao); }
		else Ir(PProxima);
	}

	private Medida _medidaDoRaio;
	private float _alturaGuardada = -1;

	/// <summary>
	/// A CONTRAPROVA DA REGUA DO RAIO: `Altitude = 0` no node do tiro, que e o estado de antes de 2026-09-23
	/// (o campo existia no servidor e nao chegava ao desenho). A mesma medida tem que achar o feixe no chao.
	/// </summary>
	private void RaioNoChao(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		float altura = Alturas[_iAltura];
		if (_obtFase == 0)
		{
			if (NoDoTiro(mundo, srv) is not { } no) { Conferir(false, "(defeito injetado) o raio ainda estava de pe pra contraprova"); Ir(PProxima); return; }
			_alturaGuardada = no.Altitude;
			no.Altitude = 0;
			_obtAlvo = no;
		}
		bool acabou;
		Fotos f;
		try { acabou = Obturador(mundo, cli.LocalId, out f); }
		catch { RestaurarAAlturaDoTiro(); throw; }
		if (!acabou) return;
		RestaurarAAlturaDoTiro();
		if (f.Falhou) { Conferir(false, "(defeito injetado) a janela renderizou o raio no chao"); Ir(PProxima); return; }

		Medida m = Medir(f, null);
		float desce = altura * Voo.EscalaNaTela;
		Conferir(m.Pixels > 200 && m.Centro.Y > _medidaDoRaio.Centro.Y + desce - FolgaDoRaio,
			$"(defeito injetado: `Altitude = 0` no node do tiro) a regua do raio REPROVA -- o feixe cai pro plano do chao, "
			+ $"{m.Centro.Y:0.#} px abaixo do corpo (na producao: {_medidaDoRaio.Centro.Y:+0.#;-0.#;0}; o chao fica a {desce:0})");

		if (_raioBomDaContraprova != null)
			Colar("cargavoo-2b-raio-antes-e-depois.png", [Recorte(f), _raioBomDaContraprova],
				  $"O RAIO a {altura:0} px: a esquerda no plano do chao (defeito injetado), a direita na altura do corpo");
		Ir(PProxima);
	}

	private void RestaurarAAlturaDoTiro()
	{
		if (_alturaGuardada < 0) return;
		if (_obtAlvo is ProjetilDesenhado no && IsInstanceValid(no)) no.Altitude = _alturaGuardada;
		_alturaGuardada = -1;
	}

	private void Proxima(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.LimparOsTirosDaBoca(cli.LocalId);
		if (_t < 0.5) return;   // o canal fechado e os tiros recolhidos atravessam o fio

		if (++_iAltura < Alturas.Length) { Ir(PSubir); return; }

		Colar("cargavoo-1-carga.png", _tiraDaCarga, "A CARGA nas tres alturas (pairando / meio / teto): a bola na mao do corpo");
		Colar("cargavoo-2-raio.png", _tiraDoRaio, "O RAIO SOLTO nas tres alturas: o feixe na linha do corpo");
		Ir(PDefeitoSubir);
	}

	// =====================================================================
	// 2) A CONTRAPROVA DA CARGA: o filho que nasce no ar fica no chao
	// =====================================================================
	private void DefeitoApertar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// LIGADO ATE A BOLA NASCER (ela nasce no snapshot, um ou dois quadros depois do aperto) e desligado no
		// mesmo quadro em que ela aparece. O `_ExitTree` e o `Fechar` desligam de novo, haja o que houver.
		SubirComOVoo.FilhoNovoFicaNoChaoDeTeste = true;
		Apertar(srv, cli.LocalId, PDefeitoCarga);
	}

	private void DefeitoNaCarga(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		float altura = Alturas[DaContraprova];
		if (_obtFase == 0)
		{
			CargaDeRaioVisual? bola;
			bool desistiu;
			try { bola = BolaCheia(mundo, cli.LocalId, out desistiu); }
			finally
			{
				if (_desdeQueNasceu >= 0 || _t > 4) SubirComOVoo.FilhoNovoFicaNoChaoDeTeste = false;
			}
			if (desistiu) { Conferir(false, "(defeito injetado) o canal abriu e a bola da carga nasceu"); Ir(PAlheioPlantar); return; }
			if (bola == null) return;
			_obtAlvo = bola;
		}
		if (!Obturador(mundo, cli.LocalId, out Fotos f)) return;
		if (f.Falhou) { Conferir(false, "(defeito injetado) a janela renderizou a carga"); Ir(PAlheioPlantar); return; }

		Medida m = Medir(f, "cargavoo-carga-defeito-mascara.png");
		(_, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);
		Vector2 noChao = CargaDeRaioVisual.Mao(olhar) + new Vector2(0, altura * Voo.EscalaNaTela);
		float erro = (m.Centro - noChao).Length();
		Conferir(m.Pixels > 30 && erro <= FolgaDaMao + 2f,
			$"(defeito injetado: o filho que nasce no ar nao e levantado) a regua da carga REPROVA -- a bola se junta no plano "
			+ $"do chao, em ({m.Centro.X:0.#}, {m.Centro.Y:0.#}) px do sprite: {altura * Voo.EscalaNaTela:0} px ABAIXO das maos "
			+ $"({m.Pixels} px de tinta, {erro:0.#} px do ponto do chao)");

		if (_cargaBoaDaContraprova != null)
			Colar("cargavoo-3-antes-e-depois.png", [Recorte(f), _cargaBoaDaContraprova],
				  $"A CARGA a {altura:0} px: a esquerda como era (a bola no chao, o corpo la em cima), a direita na mao");

		srv.LimparOsTirosDaBoca(cli.LocalId);
		Ir(PAlheioPlantar);
	}

	// =====================================================================
	// 3) O CORPO DOS OUTROS: a mesma bola num `RemotePlayer` voando
	// =====================================================================
	private int _alheio;

	private void AlheioPlantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		float altura = Alturas[DaContraprova];
		srv.VoarNaBoca(cli.LocalId, altura);
		srv.ReporNaBoca(cli.LocalId);

		if (_alheio == 0)
		{
			if (_t < 0.6) return;
			// TRES TILES A OESTE: atras de quem olha pro leste, dentro da tela, e fora do caminho de qualquer raio.
			_alheio = srv.VitimaDaBoca(cli.LocalId, new Vec2(-1, 0), tiles: 3, bp: 3_000_000);
			Conferir(_alheio != 0, "outro corpo nasceu tres tiles a oeste (forjado, com aparencia de verdade)");
			if (_alheio == 0) { Fechar(); return; }
			srv.ArmarParaAVariedade(_alheio);
			srv.VoarNaBoca(_alheio, altura, ensinar: true);   // corpo forjado nasce sem a skill de voo
			_t = 0;
			return;
		}

		bool voa = srv.VoarNaBoca(_alheio, altura);
		if (_t < 1.5) return;   // o corpo, a aparencia e a altura dele atravessam o fio

		(_, _, float alturaDele, bool voando) = srv.CorpoDaBoca(_alheio);
		Conferir(voa && voando && mundo.CorpoDeTeste(_alheio) is RemotePlayer r && MathF.Abs(r.AlturaDeTeste - altura) <= 3f,
			$"CORPO ALHEIO: ele voa a {alturaDele:0} px e esta desenhado la "
			+ $"({(mundo.CorpoDeTeste(_alheio) as RemotePlayer)?.AlturaDeTeste:0} px na minha tela)");
		Ir(PAlheioApertar);
	}

	private void AlheioNaCarga(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_obtFase == 0)
		{
			CargaDeRaioVisual? bola = BolaCheia(mundo, _alheio, out bool desistiu);
			if (desistiu) { Conferir(false, "CORPO ALHEIO: o canal dele abriu e a bola da carga nasceu"); Ir(-1); return; }
			if (bola == null) return;
			_obtAlvo = bola;
		}
		if (!Obturador(mundo, _alheio, out Fotos f)) return;
		if (f.Falhou) { Conferir(false, "CORPO ALHEIO: a janela renderizou a carga dele"); Ir(-1); return; }

		JulgarACarga(mundo, srv, f, _alheio, "CORPO ALHEIO", "cargavoo-carga-alheia-mascara.png");
		Gravar(Recorte(f), "cargavoo-4-corpo-alheio.png", "A CARGA NO CORPO DE OUTRO, voando: a bola na mao dele");
		Ir(-1);
	}

	// =====================================================================
	// O OBTURADOR: tres fotos do mesmo instante, com um node escondido nas duas de fora
	// =====================================================================
	private struct Fotos
	{
		public Image Com, Sem, Sem2;
		public Vector2 Corpo;    // onde o corpo de referencia esta DESENHADO, em pixel de imagem
		public float Escala;     // pixel de imagem por pixel de mundo
		public bool Falhou;
	}

	private int _obtFase, _obtQuadros;
	private Image? _obtCom, _obtSem;
	private CanvasItem? _obtAlvo;

	/// <summary>
	/// ESCONDE, FOTOGRAFA, MOSTRA, FOTOGRAFA, ESCONDE, FOTOGRAFA -- com a arvore pausada, e dois quadros de
	/// folga a cada troca (`GetImage` devolve o ULTIMO quadro renderizado). O alvo e o <see cref="_obtAlvo"/>,
	/// escrito por quem chama antes da primeira volta. Devolve falso enquanto nao acabou.
	/// </summary>
	private bool Obturador(World mundo, int corpoDeReferencia, out Fotos f)
	{
		f = default;
		bool tem = _obtAlvo != null && IsInstanceValid(_obtAlvo);
		switch (_obtFase)
		{
			case 0:
				if (!tem) { f.Falhou = true; ZerarObturador(); return true; }
				GetTree().Paused = true;
				_obtAlvo!.Visible = false;
				_obtFase = 1; _obtQuadros = 0;
				return false;

			case 1:
				if (_obtQuadros++ < 2) return false;
				_obtSem = Tela();
				if (tem) _obtAlvo!.Visible = true;
				_obtFase = 2; _obtQuadros = 0;
				return false;

			case 2:
				if (_obtQuadros++ < 2) return false;
				_obtCom = Tela();
				if (tem) _obtAlvo!.Visible = false;
				_obtFase = 3; _obtQuadros = 0;
				return false;

			default:
			{
				if (_obtQuadros++ < 2) return false;
				Image? sem2 = Tela();
				if (tem) _obtAlvo!.Visible = true;

				if (_obtCom == null || _obtSem == null || sem2 == null) f.Falhou = true;
				else
				{
					f.Com = _obtCom; f.Sem = _obtSem; f.Sem2 = sem2;
					f.Corpo = NaImagem(_obtCom, mundo.PosicaoDesenhadaDe(corpoDeReferencia) ?? Vector2.Zero);
					f.Escala = EscalaDaImagem(_obtCom);
				}
				ZerarObturador();
				return true;
			}
		}
	}

	private void ZerarObturador()
	{
		_obtFase = 0; _obtQuadros = 0;
		_obtCom = null; _obtSem = null;
		if (GetTree() is { Paused: true } t) t.Paused = false;
	}

	private ProjetilDesenhado? NoDoTiro(World mundo, Jandirus.Server.GameServer srv)
	{
		int id = srv.IdDoTiroDaBoca();
		return id == 0 ? null : mundo.FindChild($"Tiro{id}", recursive: true, owned: false) as ProjetilDesenhado;
	}

	// =====================================================================
	// A MEDIDA
	// =====================================================================
	private struct Medida
	{
		public int Pixels;
		public Vector2 Centro;   // o centro da tinta, em px de MUNDO, contado do centro do sprite desenhado
	}

	/// <summary>A janela medida: <see cref="MeiaJanela"/> em volta do corpo desenhado, cortada na borda da imagem.</summary>
	private static Rect2I Janela(Fotos f)
	{
		int meia = (int)MathF.Round(MeiaJanela * f.Escala);
		int x0 = Math.Clamp((int)f.Corpo.X - meia, 0, f.Com.GetWidth() - 1), x1 = Math.Clamp((int)f.Corpo.X + meia, 1, f.Com.GetWidth());
		int y0 = Math.Clamp((int)f.Corpo.Y - meia, 0, f.Com.GetHeight() - 1), y1 = Math.Clamp((int)f.Corpo.Y + meia, 1, f.Com.GetHeight());
		return new Rect2I(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
	}

	private Medida Medir(Fotos f, string? mascara)
	{
		var m = new Medida();
		Rect2I j = Janela(f);
		Image? pintada = null;
		if (mascara != null) { pintada = f.Com.GetRegion(j); pintada.Convert(Image.Format.Rgba8); }

		double somaX = 0, somaY = 0;
		for (int y = j.Position.Y; y < j.Position.Y + j.Size.Y; y++)
			for (int x = j.Position.X; x < j.Position.X + j.Size.X; x++)
			{
				if (!EhTinta(f, x, y)) continue;
				pintada?.SetPixel(x - j.Position.X, y - j.Position.Y, new Color(1f, 0f, 1f));
				m.Pixels++;
				somaX += (x + 0.5f - f.Corpo.X) / f.Escala;
				somaY += (y + 0.5f - f.Corpo.Y) / f.Escala;
			}

		if (m.Pixels > 0) m.Centro = new Vector2((float)(somaX / m.Pixels), (float)(somaY / m.Pixels));
		if (pintada != null && mascara != null) Gravar(pintada, mascara, "A MASCARA (magenta = o que a sonda contou como tinta do node)");
		return m;
	}

	/// <summary>A regra das tres fotos: difere das DUAS sem o node, e as duas concordam entre si ali.</summary>
	private static bool EhTinta(Fotos f, int x, int y)
	{
		Color com = f.Com.GetPixel(x, y), sem = f.Sem.GetPixel(x, y), sem2 = f.Sem2.GetPixel(x, y);
		return !Difere(sem, sem2) && Difere(com, sem) && Difere(com, sem2);
	}

	private static bool Difere(Color p, Color q)
		=> MathF.Abs(p.R - q.R) > Epsilon || MathF.Abs(p.G - q.G) > Epsilon || MathF.Abs(p.B - q.B) > Epsilon;

	// =====================================================================
	// AS FERRAMENTAS DE TELA
	// =====================================================================
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

	/// <summary>Quantos pixels de imagem vale um pixel de mundo -- medido, e nao lido do `Zoom` (ele muda com a altura).</summary>
	private float EscalaDaImagem(Image img)
	{
		float e = (NaImagem(img, new Vector2(ZoneCollision.TileSize, 0)) - NaImagem(img, Vector2.Zero)).Length() / ZoneCollision.TileSize;
		return e > 0.01f ? e : 1f;
	}

	/// <summary>O recorte da foto COM o node, na janela medida -- o que vai pras tiras.</summary>
	private static Image Recorte(Fotos f)
	{
		Image r = f.Com.GetRegion(Janela(f));
		r.Convert(Image.Format.Rgba8);
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

		SubirComOVoo.FilhoNovoFicaNoChaoDeTeste = false;
		RestaurarAAlturaDoTiro();
		if (GetTree() is { } t) t.Paused = false;
		if (S is { } srv && C is { } cli)
		{
			srv.LimparOsTirosDaBoca(cli.LocalId);
			if (_alheio != 0) srv.LimparOsTirosDaBoca(_alheio);
			srv.PousarNaBoca(cli.LocalId);
			srv.LimparAFoto();
		}

		GD.Print("\n[cargavoo] ===== A CARGA E O RAIO DE QUEM VOA, EM TRES ALTURAS -- FOTOGRAFADOS =====");
		foreach (string l in _linhas) GD.Print("[cargavoo] " + l);
		GD.Print(_falhas.Count == 0
			? "[cargavoo] ===== TUDO OK ====="
			: $"[cargavoo] ===== {_falhas.Count} FALHA(S) =====\n[cargavoo]   " + string.Join("\n[cargavoo]   ", _falhas));
		GetTree().Quit();
	}
}
