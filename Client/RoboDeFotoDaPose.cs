using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A POSE DO RAIO, FOTOGRAFADA NO CORPO (`--diagpose`) ============================
/// O pedido do dono foi *"vc n colocou a ANIMACAO NO SPRITE dos personagem ao SOLTAR O BEAM. no
/// byond, ao CARREGAR o beam tinha o EFEITO DE CHARGE do beam, e ao SOLTAR o sprite ficava na
/// ANIMACAO DE SOCO pra DIRECAO q o beam esta sendo jogado, e ele so voltaria a posicao de IDLE
/// quando ele PARASSE DE USAR O BEAM (por vontade propria ou pq ALGUEM BATEU NELE e cancelou o
/// beam)"*.
///
/// Isso e uma queixa de OLHO, do comeco ao fim. A familia 4b da `--projetilteste` ja mede tudo o que
/// da pra medir em numero -- a entrada, as tres saidas, o soco que atravessa e devolve, a trava de
/// direcao, o NPC, o byte opcional --, e **passaria verde com a folha desenhando o mesmo boneco nas
/// duas poses**: entre `Pose.Canalizando` e o pixel ha o fio, o `World`, o `LocalPlayer`, o
/// `CharacterVisual.SetPose` e a escada do `Escolher`. Esta bancada existe pra essa metade.
/// ==========================================================================================================
///
/// ============================ COMO A DIFERENCA E MEDIDA, E POR QUE NAO PELA TELA ============================
/// Cada tomada guarda TRES leituras:
///
///   1. o RECORTE DA TELA em volta do corpo -- e a foto, pra o olho;
///   2. o QUADRO DESENHADO do corpo (`CharacterVisual.QuadroDoCorpoDeTeste()`) -- a textura exata que
///      o `AnimatedSprite2D` do corpo esta mostrando neste instante;
///   3. o QUADRO ZERO da animacao em que ele esta (`QuadroDoCorpoDeTeste(primeiro: true)`).
///
/// **As afirmacoes se apoiam nas duas ultimas, e essa e a razao de esta bancada existir.** O recorte
/// de tela muda sozinho por quatro motivos que nao sao a pose: o brilho da carga nasce POR CIMA do
/// boneco, o feixe sai da mao dele, o cenario deste jogo se destroi e a hora do mundo anda. Uma
/// bancada que so comparasse telas ficaria verde com o corpo eternamente no idle -- bastaria o
/// overlay da carga aparecer.
///
/// E as duas leituras de corpo respondem perguntas OPOSTAS, de proposito:
///
///   * "o corpo MUDOU de desenho" pergunta ao quadro DESENHADO. A fase da animacao so pode ADICIONAR
///     diferenca, nunca esconder: se ha diferenca, ha diferenca.
///   * "o corpo voltou ao MESMO desenho" pergunta ao quadro ZERO. Duas fotos do mesmo idle podem
///     cair em quadros diferentes do ciclo de respiracao (a primeira rodada desta bancada mediu 8 px
///     entre dois `default_south`), e cobrar zero no quadro desenhado seria cobrar sorte de fase.
/// ==========================================================================================================
///
/// ============================ O OBTURADOR ESPERA O DESENHO, E MEDE A ESPERA ============================
/// Nenhuma foto sai por relogio fixo. Toda foto de pose espera o DESENHO chegar
/// (<see cref="Assentou"/>) e grava QUANTO TEMPO ele levou -- e o atraso vira uma linha do relatorio.
///
/// A primeira rodada desta bancada nasceu com esperas fixas e reprovou seis vezes seguidas com
/// `default_south` no lugar de `blast_south`: o servidor ja dizia "atirando" e o cliente ainda nao
/// tinha desenhado o quadro seguinte. O defeito era da bancada, mas a licao e do jogo -- **um numero
/// que so aparece porque alguem o mediu e melhor que uma espera generosa que o esconde**.
/// ====================================================================================================
///
/// ============================ NADA AQUI E FORJADO ============================
/// O canal nasce e morre pelo BOTAO (`GameServer.GatilhoDaVariedade` -> `UsarHabilidade` -> `KiWave`,
/// a mesma funcao do `C2S.Habilidade`), o golpe da cena E sai pelo `Atacar` de producao, e o Ki acaba
/// porque o `TickDosCanaisDeKi` cobrou o aluguel. A bancada nao escreve `Pose`, nao toca em `_canais`
/// e nao chama `Canalizar` nem `FecharCanal`. Ver `GameServer.FotoDaPose.cs`.
/// ==========================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     &lt;godot&gt; --path . --host --rede 7940 --bpteste 300000000 --horateste 0.5 --diagpose \
///              --position 1920,0 --resolution 1600x900 --raca Human --conta bancada_pose --nome Poseiro
///
/// As fotos saem em `user://pose-*.png`. Comecar pelas TIRAS, que sao a leitura do pedido:
///     pose-A-tres-quadros.png      parado / carregando / soltando / depois de parar
///     pose-B-o-canal-inteiro.png   o comeco, o fim de um raio sustentado, e o Ki acabando
///     pose-C-direcao.png           o mesmo corpo atirando pra dois lados
///     pose-D-npc.png               um NPC parado e o mesmo NPC atirando
///     pose-E-apanhou.png           atirando / de volta ao idle depois do golpe
/// </summary>
public partial class RoboDeFotoDaPose : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>O verbo do raio basico -- o `Ki_Wave` de `beams.dm:270`.</summary>
	private const string Verbo = "Ki_Wave";

	/// <summary>O lado do recorte de tela, em pixels. O mesmo da `--diagvariedade`.</summary>
	private const int Lado = 192;

	/// <summary>As familias de animacao que valem como "pose de raio". Ver <see cref="Familia"/>.</summary>
	private static readonly string[] DoRaio = ["blast", "attack"];

	/// <summary>E a que vale como "idle". Uma so -- o `default_<dir>` de toda folha.</summary>
	private static readonly string[] DoIdle = ["default"];

	/// <summary>
	/// QUANTO A BANCADA ESPERA O DESENHO CHEGAR, em segundos.
	///
	/// Nao e uma folga: e um TETO. O snapshot sai a 30 Hz, entao a pose devia estar na tela em uns
	/// 30-50 ms; um segundo inteiro ja seria uma queixa de jogo ("o boneco troca de pose atrasado").
	/// Passado o teto a foto sai assim mesmo e a linha reprova com o atraso na mao.
	/// </summary>
	private const double TetoDoDesenho = 1.0;

	/// <summary>
	/// QUANTOS SEGUNDOS A CENA B SEGURA O RAIO.
	///
	/// Tem que ser MUITO maior que o `Protocol.AttackPoseMs` (240 ms), senao "a pose ficou" seria so
	/// "o prazo do soco ainda nao venceu" -- e e justamente essa a confusao que o pedido do dono
	/// desfaz ("so voltaria ao IDLE quando ele PARASSE de usar o beam"). Dezenove vezes o prazo.
	///
	/// E tem que ser MENOR que a vida do feixe: quando a cabeca morre de alcance o canal cai junto
	/// (`TickDosCanaisDeKi`), e a bancada estaria medindo o alcance em vez da pose.
	///
	/// QUATRO E MARGEM MEDIDA, E NAO UM NUMERO BONITO. A linha do feixe deste relatorio sai
	/// `andou 30,0 tiles` -- o `AlcanceTiles` cheio do Ki Wave -- tanto aos 4,5 s quanto aos 4,0 s:
	/// a cabeca chega no fim do alcance ANTES das duas marcas, e o canal continua de pe nas duas
	/// (foi medido verde nas duas rodadas). Ou seja o teto verdadeiro nao e o alcance da cabeca, e a
	/// margem esta aqui por prudencia e nao por diagnostico -- se um dia esta linha ficar vermelha
	/// com "o canal chegou vivo", e este o numero a baixar.
	/// </summary>
	private const double SegurarOCanal = 4.0;

	/// <summary>
	/// QUANTO A CENA E ESPERA A POEIRA DO POUSO BAIXAR antes de fotografar o idle. Ver `E_Depois`.
	/// </summary>
	private const double EsperaDaPoeira = 3.0;

	/// <summary>Quando o corpo arremessado pousou, no relogio da fase. -1 = ainda voando.</summary>
	private double _pousou = -1;

	/// <summary>Depois disto ela desiste e conta o que faltou. Ver <see cref="Fechar"/>.</summary>
	private const double Paciencia = 300;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private Facing[] _rumos = [];
	private int _npc, _valentao, _socos;

	// =====================================================================
	// O RELATORIO
	// =====================================================================
	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --    " + oque);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private const int PAssentar = 0, PA_Virar = 1, PA_Parado = 2, PA_Carregando = 3, PA_Atirando = 4,
					  PA_Soltou = 5,
					  PB_Comecar = 6, PB_Segurar = 7, PB_SemKi = 8,
					  PC_Direcao = 9,
					  PD_Npc = 10,
					  PE_Plantar = 11, PE_Atirando = 12, PE_Bater = 13, PE_Depois = 14,
					  PFim = 15;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagpose` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s) no passo {_passo}"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case PA_Virar: A_Virar(mundo, srv, cli); break;
			case PA_Parado: A_Parado(mundo, srv, cli); break;
			case PA_Carregando: A_Carregando(mundo, srv, cli); break;
			case PA_Atirando: A_Atirando(mundo, srv, cli); break;
			case PA_Soltou: A_Soltou(mundo, srv, cli); break;
			case PB_Comecar: B_Comecar(srv, cli); break;
			case PB_Segurar: B_Segurar(mundo, srv, cli); break;
			case PB_SemKi: B_SemKi(mundo, srv, cli); break;
			case PC_Direcao: C_Direcao(mundo, srv, cli); break;
			case PD_Npc: D_Npc(mundo, srv, cli); break;
			case PE_Plantar: E_Plantar(mundo, srv, cli); break;
			case PE_Atirando: E_Atirando(mundo, srv, cli); break;
			case PE_Bater: E_Bater(srv, cli); break;
			case PE_Depois: E_Depois(mundo, srv, cli); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; Rearmar(); }

	/// <summary>Rearma o obturador: a proxima espera comeca do zero. Ver <see cref="Assentou"/>.</summary>
	private void Rearmar() { _desde = -1; _chegou = -1; }

	// =====================================================================
	// O OBTURADOR QUE ESPERA O DESENHO
	// =====================================================================
	/// <summary>Quando a condicao do SERVIDOR ficou verdadeira, no relogio da fase. -1 = ainda nao.</summary>
	private double _desde = -1;

	/// <summary>Quando o DESENHO acompanhou. -1 = ainda nao. Ver <see cref="PousioDoObturador"/>.</summary>
	private double _chegou = -1;

	/// <summary>Quanto o desenho demorou pra acompanhar o servidor, na ultima espera fechada.</summary>
	private double _atraso;

	/// <summary>
	/// ============================ O OBTURADOR ESPERA O QUADRO SER PINTADO ============================
	/// `GetViewport().GetTexture().GetImage()` chamado dentro do `_Process` devolve o quadro
	/// **anterior** -- o que ja foi renderizado. Fotografar no MESMO quadro em que o desenho mudou
	/// grava a imagem de antes da mudanca, com o relatorio dizendo (com razao) que o estado ja e o
	/// novo. Texto certo, PNG errado: exatamente o tipo de foto que este projeto ja catalogou como
	/// "uniform escrito nao e pixel desenhado".
	///
	/// Aconteceu, e foi medido: numa rodada a foto A2 (carregando) saiu IDENTICA a A1 (parado) -- 0
	/// pixel de diferenca -- com a checagem do node de brilho VERDE. O brilho existia; ele so nao
	/// tinha sido pintado ainda no quadro que a camera entregou.
	///
	/// Cem milissegundos sao uns seis quadros a 60 Hz: folga de sobra pra o pintor, e curto demais pra
	/// distorcer qualquer uma das cenas (a mais curta, a carga, dura 400 ms).
	/// ==============================================================================================
	/// </summary>
	private const double PousioDoObturador = 0.10;

	/// <summary>
	/// O DESENHO JA ACOMPANHOU O SERVIDOR, E JA FOI PINTADO? Marca o instante em que o servidor virou
	/// (uma vez so), o instante em que o desenho o seguiu -- que e o <see cref="_atraso"/> que vai pro
	/// relatorio -- e so libera o obturador depois do <see cref="PousioDoObturador"/>.
	///
	/// Passado o <see cref="TetoDoDesenho"/> sem o desenho chegar, ela libera assim mesmo: a foto sai
	/// e quem chama reprova com o atraso na mao. Uma bancada que ficasse presa esperando nunca
	/// mostraria o defeito que estava procurando.
	/// </summary>
	private bool Assentou(World mundo, int id, string[] familias)
	{
		if (_desde < 0) _desde = _t;

		if (_chegou < 0)
		{
			bool bateu = Array.IndexOf(familias, Familia(AnimDe(mundo, id))) >= 0;
			if (!bateu)
			{
				if (_t - _desde < TetoDoDesenho) return false;
				_atraso = _t - _desde;    // desistiu: a foto sai como esta e a linha reprova
				return true;
			}
			_chegou = _t;
			_atraso = _t - _desde;
		}
		return _t - _chegou >= PousioDoObturador;
	}

	/// <summary>A linha que julga a espera. Sai depois de toda foto de pose.</summary>
	private void JulgarOAtraso(World mundo, int id, string[] familias, string oque)
		=> Conferir(Array.IndexOf(familias, Familia(AnimDe(mundo, id))) >= 0,
					$"{oque} -- o desenho acompanhou o servidor em {_atraso * 1000:0} ms "
					+ $"(teto {TetoDoDesenho * 1000:0} ms; agora `{AnimDe(mundo, id)}`)");

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3) return;

		// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE e o `PodeAtirar` recusa com razao --
		// a `--diagvariedade` perdeu o primeiro tiro assim. Esperar o estado e mais honesto que
		// escrever `KO = false`.
		(_, _, _, bool ko, bool morto, _, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);
		if ((ko || morto) && _t < 30) return;
		Conferir(!ko && !morto, "o corpo entrou em pe e acordado");

		// O CORPO ARMADO PELA MESMA JANELA DA `--diagvariedade`: livro cheio, degraus e tanque. E o
		// mesmo corpo que aquela bancada usa pra atirar as vinte e quatro tecnicas, e ele passa pelo
		// gate de skill do `UsarTecnica` como um jogador que aprendeu o raio. (A ancora que aquela
		// janela grava de lambuja e lida so pelo `ReporNaAncora`, que esta bancada nunca chama.)
		(int skills, int verbos) = srv.ArmarParaAVariedade(cli.LocalId);
		Conferir(skills > 0 && verbos > 0,
			$"o corpo aprendeu o raio pelo caminho do jogador ({skills} skills, {verbos} verbos)");

		// MEIO-DIA CRAVADO: a hora do mundo e sorteada e um dia do jogo dura 24 minutos. Sem isto as
		// fotos do fim da rodada teriam outra luz que as do comeco, e "o recorte mudou" passaria a
		// significar "o sol andou". Ver `CravarMeioDiaDaVariedade`.
		srv.CravarMeioDiaDaVariedade(cli.LocalId);

		// OS RUMOS LIVRES. O primeiro serve a todas as cenas; o segundo e o que faz a cena C existir.
		_rumos = srv.RumosLivresDaPose(cli.LocalId, 22);
		Conferir(_rumos.Length > 0, $"achei rumo livre pro raio andar ({_rumos.Length} dos 4)");
		if (_rumos.Length == 0) { Fechar(); return; }

		Virar(PA_Virar);
	}

	// =====================================================================
	// O CORPO VIRA ANDANDO -- e nao ha outro jeito honesto
	// =====================================================================
	/// <summary>
	/// ============================ A DIRECAO DO MEU CORPO E DO CLIENTE, E ISSO E DESENHO ============================
	/// A primeira rodada da cena C virava o corpo com `ApontarParaAVariedade` -- que escreve
	/// `pl.Facing` no SERVIDOR -- e reprovou: o servidor dizia `East`, o desenho dizia `blast_south`.
	///
	/// Nao era defeito do jogo. Posicao e direcao do corpo LOCAL sao previsao do cliente (o servidor
	/// so corrige), e o `LocalPlayer` escreve o proprio `Facing` a partir do vetor de input. Um
	/// jogador vira ANDANDO; escrever so a metade do servidor produz uma discordancia que so a
	/// bancada consegue criar.
	///
	/// Entao a bancada vira como o jogador vira: `World.AndarDeTeste` (o piloto automatico do cliente,
	/// o mesmo que a `--diagagua` usa), meio segundo, e para. E DEPOIS DE VIRAR ELA COBRA QUE AS DUAS
	/// PONTAS CONCORDEM -- que e uma afirmacao de verdade e nao um contorno: com `Facing` divergente,
	/// a pose apontaria pra um lado e o feixe sairia pro outro, que e exatamente o que o `turnlock` do
	/// DM existe pra impedir.
	/// ==========================================================================================================
	/// </summary>
	/// <returns>verdadeiro quando o corpo ja parou; o julgamento fica com quem chama.</returns>
	private bool VirarAndando(World mundo, Facing alvo)
	{
		if (_t < 0.6)
		{
			Vec2 d = Jandirus.Core.Combat.MeleeArea.Frente(alvo);
			mundo.AndarDeTeste(new Vector2(d.X, d.Y));
			return false;
		}
		if (_t < 1.0) { mundo.PararDeTeste(); return false; }
		return true;
	}

	/// <summary>As duas pontas concordam sobre pra onde este corpo olha? Ver <see cref="VirarAndando"/>.</summary>
	private void JulgarAVirada(World mundo, Jandirus.Server.GameServer srv, GameClient cli, Facing alvo)
	{
		(_, _, _, _, _, _, _, _, _, Facing olhar) = srv.EstadoDaPose(cli.LocalId);
		Conferir(olhar == alvo,
			$"o corpo ANDOU pro {alvo} e o SERVIDOR concorda (ele diz {olhar})");
		Conferir(AnimDe(mundo, cli.LocalId).EndsWith(SufixoDe(alvo), StringComparison.Ordinal),
			$"...e o DESENHO local ja esta virado pro mesmo lado (`{AnimDe(mundo, cli.LocalId)}`)");
	}

	private void A_Virar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (!VirarAndando(mundo, _rumos[0])) return;
		JulgarAVirada(mundo, srv, cli, _rumos[0]);
		Virar(PA_Parado);
	}

	// =====================================================================
	// A) OS QUADROS DE UM TIRO DE VERDADE
	// =====================================================================
	/// <summary>
	/// O CONTROLE, E ELE VEM PRIMEIRO. Sem o quadro de ANTES, "a pose do raio apareceu" nao tem com o
	/// que ser comparado -- e uma foto de boneco parado nao se distingue de uma foto de boneco parado.
	/// </summary>
	private void A_Parado(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		Tomar(mundo, cli.LocalId, "pose-a1-parado", "A1: parado, antes de tudo");
		Conferir(_tomadas[^1].Corpo != null,
			"o corpo tem QUADRO DESENHADO na tela (e nao so um node) -- sem isto tudo abaixo compara vazio");

		string relato = srv.GatilhoDaVariedade(cli.LocalId, Verbo);
		Nota($"apertei o botao do raio: {(relato.Length == 0 ? "(o servidor nao reclamou)" : relato)}");
		Virar(PA_Carregando);
	}

	/// <summary>
	/// A FASE DE CARGA -- e ela e a mais curta das tres (`chargedelay/log_10(chargedskill+beamskill)`
	/// da uns 0,4 s com a pericia deste corpo), entao o obturador nao espera relogio: dispara no
	/// primeiro quadro em que o servidor diz "canal de pe, feixe ainda nao saiu" E o cliente ja
	/// desenhou o brilho.
	///
	/// **ESPERAR O NODE DA CARGA E A METADE QUE IMPORTA.** Sem ele a foto sairia identica a do idle e
	/// a bancada estaria medindo a latencia do snapshot, nao a carga.
	/// </summary>
	private void A_Carregando(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, bool atirando, int carga, _, _, _, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);
		if (!canal)
		{
			if (_t < 3) return;
			Conferir(false, "o botao abriu um canal de ki (o servidor recusou o raio)");
			Fechar();
			return;
		}

		// O BRILHO PRECISA TER SIDO PINTADO, e nao so criado -- ver `PousioDoObturador`, que nasceu
		// desta foto: numa rodada a A2 saiu IDENTICA a A1 (0 px) com esta checagem VERDE.
		bool brilho = BrilhoDaCarga(mundo, cli.LocalId) != null;
		if (brilho && _chegou < 0) _chegou = _t;
		if (!atirando && (!brilho || _t - _chegou < PousioDoObturador) && _t < 2) return;

		Tomar(mundo, cli.LocalId, "pose-a2-carregando", $"A2: CARREGANDO (desenho de carga {carga})");
		Conferir(!atirando, "a foto A2 pegou a fase de CARGA (o feixe ainda nao tinha saido)");
		Conferir(brilho, $"...e o brilho da carga estava na tela por cima do corpo (`BlastCharges` {carga})");
		Virar(PA_Atirando);
	}

	private void A_Atirando(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, bool atirando, _, _, _, _, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);
		if (!canal) { Conferir(false, "o canal sobreviveu ate a fase de tiro"); Fechar(); return; }
		if (!atirando) return;
		if (!Assentou(mundo, cli.LocalId, DoRaio)) return;

		Tomar(mundo, cli.LocalId, "pose-a3-soltando", "A3: SOLTANDO o feixe");
		JulgarOAtraso(mundo, cli.LocalId, DoRaio, "a pose do raio chegou na TELA quando o feixe saiu");

		srv.GatilhoDaVariedade(cli.LocalId, Verbo);   // SAIDA 1: por vontade propria
		Virar(PA_Soltou);
	}

	private void A_Soltou(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, _, _, _, _, _, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);
		if (canal) { if (_t < 3) return; }
		if (!Assentou(mundo, cli.LocalId, DoIdle)) return;

		_linhas.Add("=== SAIDA 1: SOLTOU POR VONTADE PROPRIA ===");
		Conferir(!canal, "SAIDA 1 (apertou de novo): o canal fechou");
		Tomar(mundo, cli.LocalId, "pose-a4-depois", "A4: depois de PARAR");
		JulgarOAtraso(mundo, cli.LocalId, DoIdle, "...e o corpo voltou ao idle na TELA");

		JulgarACenaA();

		srv.LimparOsTirosDaVariedade(cli.LocalId);
		srv.RegarOKiDaVariedade(cli.LocalId);
		Virar(PB_Comecar);
	}

	/// <summary>
	/// ============================ O QUE OS QUATRO QUADROS TEM QUE DIZER ============================
	/// O dono pediu tres: carregando, soltando, e depois de parar. A bancada tira QUATRO, porque o
	/// quarto (o "parado, antes de tudo") e o unico jeito de a leitura "voltou ao idle" significar
	/// alguma coisa -- voltou ao QUE?
	///
	/// E as afirmacoes sao deliberadamente diferentes entre si:
	///
	///   * CARREGANDO x PARADO -- o CORPO tem que ser o MESMO desenho. Isso e o original e nao falta
	///     de arte: o bloco de carga do verb nao toca em `icon_state` (`beams.dm:288-300`), e ha ate a
	///     prova de que alguem tentou e desistiu -- no Boom Wave a linha esta la, comentada
	///     (`//usr.icon_state="Blast"`, `beams.dm:485`). Quem muda na tela e o OVERLAY, e e por isso
	///     que a diferenca desta dupla se cobra no RECORTE e nao no corpo.
	///   * SOLTANDO x CARREGANDO -- aqui sim o CORPO muda (`usr.icon_state = "Blast"`, `beams.dm:280`).
	///   * DEPOIS x PARADO -- o corpo volta a ser o mesmo desenho de antes de tudo.
	/// ==========================================================================================
	/// </summary>
	private void JulgarACenaA()
	{
		if (Achar("pose-a1-parado") is not { } parado || Achar("pose-a2-carregando") is not { } carregando
			|| Achar("pose-a3-soltando") is not { } soltando || Achar("pose-a4-depois") is not { } depois)
		{
			Conferir(false, "os quatro quadros da cena A foram tirados");
			return;
		}

		_linhas.Add("=== CENA A: OS QUADROS DE UM TIRO DE VERDADE ===");

		Conferir(soltando.Anim != parado.Anim,
			$"SOLTANDO o corpo troca de ANIMACAO: `{parado.Anim}` -> `{soltando.Anim}`");
		Conferir(Familia(soltando.Anim) is "blast" or "attack",
			$"...e a folha escolhida e a do raio (ou o soco, o degrau seguinte da escada): `{soltando.Anim}`");
		int mudou = MudouNoCorpo(soltando, carregando);
		Conferir(mudou > 0,
			$"...e o desenho do corpo MUDA DE PIXEL ao soltar ({mudou} px do quadro desenhado diferem "
			+ "do quadro de carga)");

		Conferir(carregando.Anim == parado.Anim && MesmoCorpo(carregando, parado) == 0,
			$"CARREGANDO o CORPO fica no idle -- e isso e o original, nao falta de arte "
			+ $"(`{carregando.Anim}`, {MesmoCorpo(carregando, parado)} px de diferenca no quadro zero)");

		Conferir(depois.Anim == parado.Anim,
			$"DEPOIS DE PARAR o corpo volta ao desenho de antes de tudo (`{depois.Anim}`)");
		Conferir(MesmoCorpo(depois, parado) == 0,
			$"...pixel a pixel, no quadro zero das duas animacoes ({MesmoCorpo(depois, parado)} px)");
		Nota($"(no quadro DESENHADO os dois idles diferem em {MudouNoCorpo(depois, parado)} px -- e a fase "
		   + "do ciclo de respiracao, e e por isso que a linha acima pergunta ao quadro zero)");

		// ---- a TELA (o que o dono ve; muda por mais coisas que a pose, e por isso vem rotulada)
		int telaCarga = DiferemNaTela(carregando, parado);
		int telaSolta = DiferemNaTela(soltando, carregando);
		Conferir(telaCarga > 0,
			$"na TELA, carregar ja muda a imagem ({telaCarga} px) -- e o brilho da carga, que nasce "
			+ "POR CIMA de um corpo parado");
		Conferir(telaSolta > 0,
			$"na TELA, soltar muda de novo ({telaSolta} px) -- a pose do corpo E o feixe saindo");
		Nota($"na TELA, depois de parar x parado: {DiferemNaTela(depois, parado)} px (nao e zero e nao "
		   + "deveria ser: o feixe que ja saiu continua no ar e o chao dele ficou riscado)");
		Conferir(telaCarga > 0 && telaSolta > 0,
			"as TRES POSES DA TELA sao diferentes entre si (parado -> carregando -> soltando)");

		Montar("pose-A-tres-quadros.png", [parado, carregando, soltando, depois]);
	}

	// =====================================================================
	// B) O CORPO FICA NA POSE O CANAL INTEIRO
	// =====================================================================
	private void B_Comecar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// SEM REAPONTAR: o corpo continua virado pra onde a cena A o deixou (`A_Virar`), e as duas
		// pontas ja foram cobradas la. Reescrever `Facing` aqui so no servidor recriaria a
		// discordancia que a `VirarAndando` existe pra nao ter.
		if (_t < 0.5) return;
		srv.GatilhoDaVariedade(cli.LocalId, Verbo);
		_foraDoBlast = _quadrosDoCanal = 0;
		_intruso = "";
		Virar(PB_Segurar);
	}

	/// <summary>Quantos quadros o corpo passou FORA da pose de raio DEPOIS de ela chegar. Tem que ser 0.</summary>
	private int _foraDoBlast, _quadrosDoCanal;

	/// <summary>O primeiro desenho intruso, se houve -- pra o relatorio nomea-lo.</summary>
	private string _intruso = "";

	/// <summary>
	/// SEGURA O RAIO E OLHA TODO QUADRO. Duas fotos (comeco e fim) respondem "estava la nas duas
	/// pontas" e deixam o meio no escuro; a contagem por quadro fecha o buraco -- se a pose piscar
	/// uma unica vez pro idle no meio de quatro segundos e meio, esta bancada sabe dizer QUAL desenho
	/// entrou no lugar.
	///
	/// A CONTAGEM SO COMECA DEPOIS DA FOTO B1, e a distincao nao e conveniencia: o atraso de chegada
	/// e uma pergunta (a linha do `JulgarOAtraso` a faz, com numero e teto) e "ele voltou pro idle no
	/// meio" e outra. Somar as duas daria um numero que nao responde nenhuma.
	/// </summary>
	private void B_Segurar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, bool atirando, _, _, _, _, double ki, double maxKi, _, _) = srv.EstadoDaPose(cli.LocalId);

		if (canal && !atirando) return;   // ainda carregando: a cena B comeca no feixe
		if (!canal && _quadrosDoCanal == 0) { Conferir(false, "a cena B abriu o canal"); Fechar(); return; }

		if (_quadrosDoCanal == 0)
		{
			if (!Assentou(mundo, cli.LocalId, DoRaio)) return;
			Tomar(mundo, cli.LocalId, "pose-b1-comeco", $"B1: o canal COMECOU (Ki {ki / Math.Max(maxKi, 1):P0})");
			JulgarOAtraso(mundo, cli.LocalId, DoRaio, "a cena B comecou COM a pose de raio na tela");
			_t = 0;
			_quadrosDoCanal = 1;
			return;
		}

		if (canal && atirando)
		{
			_quadrosDoCanal++;
			string anim = AnimDe(mundo, cli.LocalId);
			if (Array.IndexOf(DoRaio, Familia(anim)) < 0)
			{
				_foraDoBlast++;
				if (_intruso.Length == 0) _intruso = anim;
			}
		}

		if (_t < SegurarOCanal && canal) return;

		// ============================ ONDE O FEIXE ESTA, PRA A FOTO NAO PARECER MENTIRA ============================
		// A cabeca do Ki Wave anda ~3,3 tiles/s. Aos quatro segundos e meio ela ja saiu do recorte de
		// 192 px (seis tiles), entao a foto B2 mostra o corpo NA POSE e nenhum feixe ao lado dele --
		// que lida sem contexto parece um corpo posando por nada.
		//
		// A distancia sai medida junto, do proprio projetil do servidor. Ela nao e desculpa: e o numero
		// que separa "o feixe saiu do enquadramento" de "o feixe sumiu", e sem ele a diferenca entre as
		// duas ficaria por conta de quem olha.
		// =====================================================================================================
		(bool achou, _, _, _, _, double andou, _) = srv.TiroDaVariedade(cli.LocalId);
		Nota(achou
			? $"no instante da B2 a cabeca do feixe ja andou {andou:0.0} tiles -- fora do recorte de "
			  + $"{Lado / ZoneCollision.TileSize} tiles, e por isso ela nao aparece ao lado do corpo"
			: "no instante da B2 o servidor nao achou projetil deste dono");

		Tomar(mundo, cli.LocalId, "pose-b2-fim",
			  $"B2: {_t:0.0}s depois, o MESMO canal (Ki {ki / Math.Max(maxKi, 1):P0})");

		_linhas.Add("=== CENA B: O CORPO FICA NA POSE O CANAL INTEIRO ===");
		Conferir(canal, $"o canal chegou vivo aos {SegurarOCanal:0.0}s (a bancada nao mediu um raio que ja morreu)");
		Conferir(_quadrosDoCanal > 60, $"o canal foi olhado em {_quadrosDoCanal} quadros consecutivos");
		Conferir(_foraDoBlast == 0,
			$"...e em NENHUM deles o corpo voltou pro idle ({_foraDoBlast} quadros fora da pose de raio"
			+ (_intruso.Length > 0 ? $", o primeiro em `{_intruso}`" : "") + ")");
		Nota($"e {SegurarOCanal:0.0}s sao {SegurarOCanal * 1000 / Jandirus.Net.Protocol.AttackPoseMs:0}x o prazo "
		   + $"da pose de SOCO ({Jandirus.Net.Protocol.AttackPoseMs} ms) -- a pose do raio nao e um prazo");

		if (Achar("pose-b1-comeco") is { } b1 && Achar("pose-b2-fim") is { } b2)
			Conferir(b1.Anim == b2.Anim && Familia(b1.Anim) is "blast" or "attack",
				$"o comeco e o fim mostram a MESMA pose de raio (`{b1.Anim}` e `{b2.Anim}`)");

		// ---- SAIDA 2: o Ki acaba, e a pose sai junto do canal
		srv.SecarOKiDaPose(cli.LocalId);
		Virar(PB_SemKi);
	}

	private void B_SemKi(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, _, _, _, _, _, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);
		if (canal) { if (_t < 5) return; }
		if (!Assentou(mundo, cli.LocalId, DoIdle)) return;

		_linhas.Add("=== SAIDA 2: O KI ACABOU ===");
		Conferir(!canal, "SAIDA 2 (o Ki acabou): o canal caiu sozinho pelo `TickDosCanaisDeKi`");
		Tomar(mundo, cli.LocalId, "pose-b3-sem-ki", "B3: o Ki acabou e o raio morreu na mao");
		JulgarOAtraso(mundo, cli.LocalId, DoIdle, "...e o corpo voltou ao idle com ele");

		if (Achar("pose-b3-sem-ki") is { } b3 && Achar("pose-a1-parado") is { } p0)
			Conferir(MesmoCorpo(b3, p0) == 0,
				$"...pro MESMO desenho de antes de tudo, pixel a pixel ({MesmoCorpo(b3, p0)} px)");

		Tomada[] tira = [.. new[] { Achar("pose-b1-comeco"), Achar("pose-b2-fim"), Achar("pose-b3-sem-ki") }
						 .Where(t => t != null).Select(t => t!)];
		Montar("pose-B-o-canal-inteiro.png", tira);

		srv.LimparOsTirosDaVariedade(cli.LocalId);
		srv.RegarOKiDaVariedade(cli.LocalId);
		Virar(PC_Direcao);
	}

	// =====================================================================
	// C) A DIRECAO -- atira pra dois lados
	// =====================================================================
	private int _iDoRumo;

	private void C_Direcao(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_rumos.Length < 2)
		{
			Nota($"so ha {_rumos.Length} rumo livre neste berco: a cena C (dois lados) nao cabe");
			Virar(PD_Npc);
			return;
		}

		// ============================ DOIS LADOS SAO DOIS, E A GUARDA VEM ANTES ============================
		// A primeira rodada desta cena tinha esta linha DEPOIS do bloco que dispara, e o robo continuou
		// atirando: `pose-c3`, `pose-c4` e um `IndexOutOfRange` em `_rumos[4]`. O bug era da bancada e
		// nao do jogo, mas a licao vale escrita -- num laco por indice, quem sai do laco tem que ser
		// perguntado ANTES de quem usa o indice.
		// ================================================================================================
		if (_iDoRumo >= 2) { JulgarACenaC(); Virar(PD_Npc); return; }

		(bool canal, bool atirando, _, _, _, _, _, _, _, Facing olhar) = srv.EstadoDaPose(cli.LocalId);
		string nome = $"pose-c{_iDoRumo + 1}";

		// ---- VIRA ANDANDO (o jeito do jogador) e so entao aperta o botao. Ver `VirarAndando`.
		if (!canal && Achar(nome) == null)
		{
			if (!VirarAndando(mundo, _rumos[_iDoRumo])) return;
			JulgarAVirada(mundo, srv, cli, _rumos[_iDoRumo]);

			// O IDLE DESTE LADO, ANTES DE ATIRAR -- e ele e o CONTROLE da leitura de pixel la embaixo.
			// Sem ele, "os dois raios desenham igual" nao teria como ser separado de "a bancada nao
			// consegue ver diferenca nenhuma entre duas direcoes". Ver `JulgarACenaC`.
			Tomar(mundo, cli.LocalId, nome + "-idle", $"C{_iDoRumo + 1}: parado, virado pro {_rumos[_iDoRumo]}");

			srv.GatilhoDaVariedade(cli.LocalId, Verbo);
			_t = 0;
			return;
		}

		if (!atirando) { if (_t > 6) { Conferir(false, $"o raio do rumo {_rumos[_iDoRumo]} saiu"); Virar(PD_Npc); } return; }
		if (!Assentou(mundo, cli.LocalId, DoRaio)) return;

		Tomar(mundo, cli.LocalId, nome, $"C{_iDoRumo + 1}: atirando pro {olhar}");
		JulgarOAtraso(mundo, cli.LocalId, DoRaio, $"a pose do raio pro {olhar} chegou na tela");
		Conferir(olhar == _rumos[_iDoRumo],
			$"o corpo esta olhando pro rumo pedido ({_rumos[_iDoRumo]}, servidor diz {olhar})");
		Conferir(AnimDe(mundo, cli.LocalId).EndsWith(SufixoDe(olhar), StringComparison.Ordinal),
			$"...e a ANIMACAO desenhada carrega esse mesmo lado (`{AnimDe(mundo, cli.LocalId)}`)");

		srv.GatilhoDaVariedade(cli.LocalId, Verbo);   // solta
		srv.LimparOsTirosDaVariedade(cli.LocalId);
		srv.RegarOKiDaVariedade(cli.LocalId);
		_iDoRumo++;
		_t = 0;
		Rearmar();
	}

	/// <summary>
	/// O JULGAMENTO DOS DOIS LADOS -- e ele mudou de forma depois de a primeira rodada medir uma coisa
	/// que eu nao esperava.
	///
	/// ============================ O QUE E DO CODIGO E O QUE E DA FOLHA ============================
	/// `blast_south` e `blast_east` deste corpo sao o MESMO desenho, pixel a pixel. Isso nao e defeito
	/// e nao e a bancada cega: os dois IDLES do mesmo corpo (`default_south` x `default_east`) diferem
	/// em centenas de pixels na MESMA medida, feita pela MESMA funcao, na mesma rodada -- e e por isso
	/// que os dois idles sao fotografados aqui. A folha e que desenha o raio igual pros dois lados.
	///
	/// Entao a afirmacao desta cena e a que o pedido do dono realmente faz -- *"pra DIRECAO q o beam
	/// esta sendo jogado"* --, e ela e sobre o ESTADO ESCOLHIDO: o corpo pede `blast_<dir>` com o
	/// `<dir>` do tiro. Se um dia a arte desses dois lados divergir, o pixel acompanha sozinho, sem
	/// esta bancada mudar.
	///
	/// A LINHA DO CONTROLE E O QUE TORNA ISSO HONESTO: sem ela, "os dois raios desenham igual" seria
	/// indistinguivel de "esta bancada nao consegue ver diferenca entre duas direcoes".
	/// ==========================================================================================
	/// </summary>
	private void JulgarACenaC()
	{
		_linhas.Add("=== CENA C: A POSE VIRA JUNTO COM O TIRO ===");
		if (Achar("pose-c1") is not { } c1 || Achar("pose-c2") is not { } c2)
		{
			Conferir(false, "as duas fotos da cena C foram tiradas");
			return;
		}

		Conferir(c1.Anim != c2.Anim,
			$"atirando pra dois lados, a ANIMACAO do corpo e outra: `{c1.Anim}` x `{c2.Anim}`");
		Conferir(Familia(c1.Anim) == Familia(c2.Anim),
			$"...e os dois continuam na MESMA familia de pose ({Familia(c1.Anim)}) -- so o sufixo de "
			+ "direcao mudou, que e o que a maquina de direcao ja sabia fazer");

		if (Achar("pose-c1-idle") is { } i1 && Achar("pose-c2-idle") is { } i2)
		{
			int noIdle = MesmoCorpo(i1, i2), noRaio = MesmoCorpo(c1, c2);
			Conferir(noIdle > 0,
				$"(controle) o MESMO corpo parado desenha diferente nos dois lados ({noIdle} px entre "
				+ $"`{i1.Anim}` e `{i2.Anim}`) -- a medida de pixel enxerga direcao");
			Nota($"e o RAIO nos dois lados difere em {noRaio} px: "
			   + (noRaio == 0
				  ? "esta folha desenha o mesmo raio pros dois lados, e isso e da ARTE e nao do codigo "
				  + "(o estado pedido ja e o `blast_<dir>` certo, como as duas linhas acima mostram)"
				  : "esta folha tem desenho proprio por lado"));
			Montar("pose-C-direcao.png", [i1, c1, i2, c2]);
		}
		else
		{
			Nota("sem os dois idles de controle: a comparacao de pixel da cena C fica sem escala");
			Montar("pose-C-direcao.png", [c1, c2]);
		}
	}

	// =====================================================================
	// D) UM NPC ATIRANDO
	// =====================================================================
	/// <summary>
	/// O CORPO DE OUTRA PESSOA -- e ele nao passa pelo mesmo caminho de cliente que o meu.
	///
	/// O corpo LOCAL decide a propria pose no `LocalPlayer.PorAPoseDoCorpo`; o alheio recebe a dele
	/// pronta, no snapshot, e a aplica no `RemotePlayer.Receive`. Sao dois consumidores do mesmo
	/// estado, e este projeto ja pagou DUAS vezes por eles discordarem (o voo e o nado, ambos
	/// anotados dentro do `PorAPoseDoCorpo`). Um NPC atirando e a unica foto que prova o segundo.
	///
	/// E ele atira pela MESMA porta: `GatilhoDaVariedade` -> `UsarHabilidade` -> `KiWave`. Nao ha
	/// caminho de tiro proprio pra IA.
	/// </summary>
	private void D_Npc(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_npc == 0)
		{
			if (_t < 0.5) return;
			// TRES TILES AO LADO, no rumo que NAO e o do tiro: dentro do recorte da camera e fora da
			// linha do feixe (um NPC na frente do proprio raio seria fotografado apanhando).
			Vec2 d = Jandirus.Core.Combat.MeleeArea.Frente(_rumos.Length > 1 ? _rumos[1] : _rumos[0]);
			_npc = srv.ForjarCorpoDeFoto(cli.LocalId,
				new Vec2(d.X * 3 * ZoneCollision.TileSize, d.Y * 3 * ZoneCollision.TileSize),
				"Foto: o NPC que atira", 5_000_000, comEscada: false);
			Conferir(_npc != 0, "o NPC entrou no mundo pelo `PorNoMundo`");
			if (_npc == 0) { Virar(PE_Plantar); return; }

			srv.ArmarParaAVariedade(_npc);
			srv.ApontarParaAVariedade(_npc, _rumos[0]);
			_t = 0;
			return;
		}

		if (mundo.CorpoDeTeste(_npc) == null)
		{
			if (_t < 6) return;
			Conferir(false, "o NPC tem corpo DESENHADO na tela");
			Virar(PE_Plantar);
			return;
		}

		// O IDLE DELE, antes de atirar -- o controle desta cena.
		if (Achar("pose-d1-npc-parado") == null)
		{
			if (_t < 0.5) return;
			Tomar(mundo, _npc, "pose-d1-npc-parado", "D1: o NPC parado");
			srv.GatilhoDaVariedade(_npc, Verbo);
			_t = 0;
			Rearmar();
			return;
		}

		(bool canal, bool atirando, _, _, _, _, _, _, _, _) = srv.EstadoDaPose(_npc);
		if (!canal && _t > 5) { Conferir(false, "o NPC abriu um canal de ki pelo mesmo botao"); Virar(PE_Plantar); return; }
		if (!atirando) return;
		if (!Assentou(mundo, _npc, DoRaio)) return;

		Tomar(mundo, _npc, "pose-d2-npc-atirando", "D2: o NPC ATIRANDO");
		JulgarOAtraso(mundo, _npc, DoRaio, "a pose do raio chegou no corpo ALHEIO (`RemotePlayer`)");

		_linhas.Add("=== CENA D: UM NPC ATIRANDO ===");
		if (Achar("pose-d1-npc-parado") is { } d1 && Achar("pose-d2-npc-atirando") is { } d2)
		{
			Conferir(d1.Anim != d2.Anim,
				$"o corpo do NPC troca de animacao ao atirar: `{d1.Anim}` -> `{d2.Anim}`");
			Conferir(Familia(d2.Anim) is "blast" or "attack",
				$"...e a folha e a do raio (`{d2.Anim}`)");
			int px = MudouNoCorpo(d2, d1);
			Conferir(px > 0, $"...e o desenho mudou de pixel ({px} px)");
			Montar("pose-D-npc.png", [d1, d2]);
		}

		srv.GatilhoDaVariedade(_npc, Verbo);
		srv.LimparOsTirosDaVariedade(_npc);
		Virar(PE_Plantar);
	}

	// =====================================================================
	// E) APANHOU -- a saida que faltava no port inteiro
	// =====================================================================
	private void E_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O CORPO CONTINUA VIRADO PRA ONDE A CENA C O DEIXOU -- nao ha reaponte aqui pela mesma razao
		// da cena B: escrever `Facing` so no servidor criaria a discordancia que a `VirarAndando`
		// existe pra nao ter. Pra ESTA cena a direcao nem importa: o que se mede e a pose, e o rumo
		// so precisa ter espaco, o que os `_rumos` ja garantem.
		(_, _, _, _, _, _, _, _, _, Facing olhar) = srv.EstadoDaPose(cli.LocalId);
		if (_t < 0.5) return;

		srv.LimparOsTirosDaVariedade(cli.LocalId);
		srv.RegarOKiDaVariedade(cli.LocalId);   // vida cheia tambem: ver `ArmarOSocoDaPose`

		// ============================ O BP DELE E O DO ALVO, E ISSO E CALIBRACAO E NAO CAPRICHO ============================
		// A primeira rodada forjou o valentao com 100 mil de BP contra os 300 milhoes do alvo, e o
		// raio sobreviveu a SESSENTA E UM socos. A conta explica: `check` (`attack cmn.dm:110`) tem o
		// `BpModulus(meu, dele)` como fator e o `Limiar` tem o inverso -- um atacante mil vezes mais
		// fraco tem forca perto de zero contra um limiar enorme, e o `Avaliar` nunca chega no ramo do
		// arremesso. Nao havia defeito nenhum no jogo; a bancada e que tinha montado uma briga que o
		// jogo, com razao, recusa.
		// ==============================================================================================================
		// E ELE NASCE FORA DA LINHA DO FEIXE. Plantado na frente, o valentao seria a primeira coisa
		// que o raio acerta -- e ai o canal cairia pelo TIRO e nao pelo SOCO, com a bancada verde na
		// saida errada. Nasce ATRAS (o `Aproximar` do golpe fecha a distancia sozinho, que e o que
		// ele faz na briga de verdade).
		Vec2 d = Jandirus.Core.Combat.MeleeArea.Frente(olhar);
		_valentao = srv.ForjarCorpoDeFoto(cli.LocalId,
			new Vec2(-d.X * 2 * ZoneCollision.TileSize, -d.Y * 2 * ZoneCollision.TileSize),
			"Foto: o que bate", 300_000_000, comEscada: false);
		Conferir(_valentao != 0, "o corpo que bate entrou no mundo");
		if (_valentao == 0) { Virar(PFim); return; }

		srv.ArmarOSocoDaPose(_valentao);

		// O IDLE DESTE LADO, ANTES DE ATIRAR -- o controle de "voltou ao idle" da foto E2.
		//
		// A primeira rodada comparava a E2 com a foto A1, la do comeco da rodada, e reprovou com
		// `default_east` contra `default_south`: entre as duas o corpo tinha VIRADO (a cena C). O
		// controle certo de "voltou ao mesmo desenho" e o mesmo corpo, no mesmo lado, um instante
		// antes -- e nao um retrato de dez cenas atras.
		Tomar(mundo, cli.LocalId, "pose-e0-parado", $"E0: parado, virado pro {olhar} (o controle da E2)");

		srv.GatilhoDaVariedade(cli.LocalId, Verbo);
		Virar(PE_Atirando);
	}

	private void E_Atirando(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, bool atirando, _, _, _, _, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);
		if (!canal) { if (_t > 5) { Conferir(false, "a cena E abriu o canal"); Virar(PFim); } return; }
		if (!atirando) return;
		if (!Assentou(mundo, cli.LocalId, DoRaio)) return;

		Tomar(mundo, cli.LocalId, "pose-e1-atirando", "E1: atirando, antes do golpe");
		JulgarOAtraso(mundo, cli.LocalId, DoRaio, "SAIDA 3, antes: a pose do raio esta na tela");
		_socos = 0;
		Virar(PE_Bater);
	}

	/// <summary>
	/// BATE ATE O RAIO CAIR. Um soco pode ser esquivado, aparado, ou nao arremessar (o `Avaliar` tem
	/// quatro desfechos), entao a bancada insiste -- e conta.
	///
	/// A CONTAGEM ENTRA NO RELATORIO de proposito: "o raio cai no primeiro soco" e "o raio cai no
	/// sexagesimo" sao respostas diferentes sobre a mesma regra, e so a segunda mereceria um olhar --
	/// foi ela que denunciou a briga mal montada da primeira rodada (ver `E_Plantar`).
	/// </summary>
	private void E_Bater(Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool canal, _, _, bool ko, _, int voo, _, _, double vida, _) = srv.EstadoDaPose(cli.LocalId);

		if (!canal)
		{
			_linhas.Add("=== CENA E / SAIDA 3: ALGUEM BATEU NELE ===");
			Conferir(true, $"SAIDA 3 (apanhou): o canal caiu no {_socos}o soco -- o `if(KB) stopbeaming()` "
						 + $"de `beams.dm:73-74` (vida {vida:0}%, nocaute={ko}, voo={voo} tiques)");
			Conferir(!ko, "...e o corpo continua ACORDADO, entao a foto de depois mostra o idle e nao o nocaute");
			Conferir(voo > 0, $"...e ele foi ARREMESSADO ({voo} tiques de voo) -- e o `KB` do DM que derruba "
							+ "o raio, e nao o dano");
			Virar(PE_Depois);
			return;
		}

		if (_socos > 60) { Conferir(false, $"o soco derrubou o raio ({_socos} tentativas, voo={voo}, vida {vida:0}%)"); Virar(PFim); return; }
		if (_t < 0.05) return;

		srv.ProntoParaSocarDeNovo(_valentao);
		srv.SocarNaPose(_valentao, cli.LocalId);
		_socos++;
		_t = 0;
	}

	private void E_Depois(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(_, _, _, _, _, int voo, _, _, _, _) = srv.EstadoDaPose(cli.LocalId);

		// O ARREMESSO E CURTO (ate 10 tiques de 0,1 s) e ele DEITA o corpo -- a foto de "voltou ao
		// idle" tem que sair com os pes no chao, senao ela mostraria a pose de VOO do arremesso e
		// estaria certa pelo motivo errado.
		if (voo > 0) { _pousou = -1; if (_t < 6) return; }
		else if (_pousou < 0) _pousou = _t;

		// ============================ E ELE PRECISA SAIR DE DENTRO DA PROPRIA POEIRA ============================
		// Quem pousa de um arremesso abre CRATERA e levanta poeira, e a poeira desenha POR CIMA do
		// corpo. A primeira rodada desta cena fotografou um segundo depois do pouso: as checagens todas
		// verdes (o estado e lido do node, nao do pixel) e a foto mostrando um borrao marrom com o
		// boneco enterrado nele.
		//
		// E a MESMA armadilha que a `--diagraio` ja tinha anotado na cena A dela, com as mesmas
		// palavras -- o que quer dizer que ela nao foi lembrada aqui por dedução, e sim repetida.
		// ====================================================================================================
		if (_pousou >= 0 && _t - _pousou < EsperaDaPoeira) return;

		if (!Assentou(mundo, cli.LocalId, DoIdle)) return;

		Tomar(mundo, cli.LocalId, "pose-e2-depois", "E2: de volta ao IDLE depois do golpe");
		JulgarOAtraso(mundo, cli.LocalId, DoIdle, "...e o corpo voltou ao idle depois de pousar");

		if (Achar("pose-e1-atirando") is { } e1 && Achar("pose-e2-depois") is { } e2
			&& Achar("pose-e0-parado") is { } e0)
		{
			Conferir(e2.Anim != e1.Anim, $"...e o corpo saiu da pose de raio (`{e1.Anim}` -> `{e2.Anim}`)");
			Conferir(e2.Anim == e0.Anim,
				$"...pro MESMO idle em que ele estava um instante antes do tiro (`{e0.Anim}`)");
			Conferir(MesmoCorpo(e2, e0) == 0, $"...pixel a pixel ({MesmoCorpo(e2, e0)} px no quadro zero)");
			Conferir(MudouNoCorpo(e2, e1) > 0,
				$"...e o quadro do corpo mudou de fato ({MudouNoCorpo(e2, e1)} px)");
			Montar("pose-E-apanhou.png", [e0, e1, e2]);
		}

		Virar(PFim);
	}

	// =====================================================================
	// AS TOMADAS
	// =====================================================================
	/// <summary>
	/// UMA TOMADA: a foto da tela, os dois quadros do corpo, e o nome da animacao. Ver o cabecalho --
	/// as leituras respondem coisas diferentes e nenhuma substitui as outras.
	/// </summary>
	private sealed class Tomada
	{
		public required string Nome;
		public required string Rotulo;
		public Image? Recorte;

		/// <summary>O quadro que estava na TELA. Serve pra "mudou".</summary>
		public Image? Corpo;

		/// <summary>O quadro ZERO da animacao. Serve pra "e o mesmo desenho".</summary>
		public Image? Corpo0;

		public string Anim = "";
		public Vector2 Centro;
	}

	private readonly List<Tomada> _tomadas = [];

	private Tomada? Achar(string nome) => _tomadas.Find(t => t.Nome == nome);

	private void Tomar(World mundo, int id, string nome, string rotulo)
	{
		var t = new Tomada { Nome = nome, Rotulo = rotulo };

		if (Visual(mundo, id) is { } v)
		{
			t.Anim = v.PoseDeTeste;
			t.Corpo = v.QuadroDoCorpoDeTeste();
			t.Corpo0 = v.QuadroDoCorpoDeTeste(primeiro: true);
		}

		if (mundo.PosicaoDesenhadaDe(id) is { } p)
			t.Centro = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * p;

		if (Tela() is { } tela)
		{
			t.Recorte = tela.GetRegion(Janela(tela, t.Centro));
			t.Recorte.Convert(Image.Format.Rgba8);
			Gravar(t.Recorte, nome + ".png", rotulo);
		}
		else Nota($"{rotulo}: SEM FOTO (headless nao renderiza -- rode com janela)");

		_tomadas.Add(t);
		_linhas.Add($"  foto  {rotulo} -- corpo desenhando `{t.Anim}`");
	}

	/// <summary>O `CharacterVisual` deste corpo. Mesmo caminho que o `World.PosicaoDesenhadaDe` usa.</summary>
	private static CharacterVisual? Visual(World mundo, int id)
		=> mundo.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	private static string AnimDe(World mundo, int id) => Visual(mundo, id)?.PoseDeTeste ?? "";

	/// <summary>O node do brilho da carga, se ele existe neste corpo. Ver `CargaDeRaioVisual`.</summary>
	private static Node? BrilhoDaCarga(World mundo, int id)
		=> mundo.CorpoDeTeste(id)?.GetNodeOrNull(CargaDeRaioVisual.NomeDoNode);

	/// <summary>
	/// A FAMILIA DA ANIMACAO -- `blast_east` vira `blast`.
	///
	/// A comparacao e feita por familia em toda pergunta de POSE, e por sufixo em toda pergunta de
	/// DIRECAO. Sao as duas leituras que o pedido do dono pede e elas nao se misturam: "ficou na pose
	/// do raio" e de familia, "virou pro lado do tiro" e de sufixo.
	/// </summary>
	private static string Familia(string anim)
	{
		int i = anim.LastIndexOf('_');
		return i <= 0 ? anim : anim[..i];
	}

	/// <summary>O sufixo de direcao que a folha usa -- a MESMA funcao que o `CharacterVisual` monta.</summary>
	private static string SufixoDe(Facing f) => "_" + MoveRules.FacingSuffix(f);

	// =====================================================================
	// A CONTA DOS PIXELS
	// =====================================================================
	/// <summary>
	/// "O CORPO MUDOU?" -- pergunta ao quadro que estava NA TELA.
	///
	/// A fase da animacao so pode ADICIONAR diferenca aqui, nunca esconder: se este numero e maior que
	/// zero, o desenho mudou de fato. E o sentido em que essa leitura e a segura.
	/// </summary>
	private static int MudouNoCorpo(Tomada a, Tomada b) => Diferenca(a.Corpo, b.Corpo);

	/// <summary>
	/// "E O MESMO CORPO?" -- pergunta ao QUADRO ZERO das duas animacoes.
	///
	/// Duas fotos do mesmo idle caem em quadros diferentes do ciclo (a primeira rodada mediu 8 px
	/// entre dois `default_south`), entao cobrar zero no quadro desenhado seria cobrar sorte de fase.
	/// No quadro zero, "mesma animacao" e "mesmos pixels" viram a mesma frase.
	/// </summary>
	private static int MesmoCorpo(Tomada a, Tomada b) => Diferenca(a.Corpo0, b.Corpo0);

	private static int DiferemNaTela(Tomada a, Tomada b) => Diferenca(a.Recorte, b.Recorte);

	/// <summary>
	/// Tamanhos diferentes contam como diferenca TOTAL: duas folhas de tamanhos diferentes SAO dois
	/// desenhos diferentes (o Big Broly e 32x64), e devolver zero ali seria o pior erro possivel --
	/// "sao iguais" por nao caberem na mesma conta.
	/// </summary>
	private static int Diferenca(Image? a, Image? b)
	{
		if (a == null || b == null) return -1;
		if (a.GetWidth() != b.GetWidth() || a.GetHeight() != b.GetHeight())
			return a.GetWidth() * a.GetHeight() + b.GetWidth() * b.GetHeight();

		// A ALFA ENTRA NA CONTA, e ela e metade do que se pergunta: duas poses de um sprite se
		// distinguem tanto pela tinta que mudou quanto pela SILHUETA -- o braco que saiu de um lugar
		// deixa transparente onde havia corpo, e esse pixel tem que contar.
		const float Eps = 0.02f;
		int n = 0;
		for (int y = 0; y < a.GetHeight(); y++)
			for (int x = 0; x < a.GetWidth(); x++)
			{
				Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
				if (Mathf.Abs(p.A - q.A) > Eps) { n++; continue; }
				if (p.A < Eps && q.A < Eps) continue;   // dois vazios sao o mesmo vazio
				if (Mathf.Abs(p.R - q.R) > Eps || Mathf.Abs(p.G - q.G) > Eps || Mathf.Abs(p.B - q.B) > Eps) n++;
			}
		return n;
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	/// <summary>A janela do recorte, empurrada pra DENTRO da tela -- nunca cortada. Ver `--diagvariedade`.</summary>
	private static Rect2I Janela(Image tela, Vector2 centro)
	{
		int lado = Math.Min(Lado, Math.Min(tela.GetWidth(), tela.GetHeight()));
		int x0 = Math.Clamp((int)centro.X - lado / 2, 0, tela.GetWidth() - lado);
		int y0 = Math.Clamp((int)centro.Y - lado / 2, 0, tela.GetHeight() - lado);
		return new Rect2I(x0, y0, lado, lado);
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
	/// A TIRA, COLADA -- e ela e o formato do pedido, nao enfeite.
	///
	/// O dono pediu a cena "em tres quadros". Arquivos separados obrigam quem le a abrir tres janelas
	/// e comparar de cabeca; a tira poe os quadros lado a lado, no MESMO tamanho e em volta do MESMO
	/// corpo. Os recortes individuais continuam gravados -- uma tira e sempre uma escolha de quem
	/// montou.
	/// </summary>
	private void Montar(string arquivo, Tomada[] tira)
	{
		var uteis = new List<Image>();
		foreach (Tomada t in tira) if (t.Recorte != null) uteis.Add(t.Recorte);
		if (uteis.Count == 0) return;

		const int Vao = 6, Escala = 2;
		int lado = uteis[0].GetWidth();
		int largura = (lado * uteis.Count + Vao * (uteis.Count - 1)) * Escala;
		Image colagem = Image.CreateEmpty(largura, lado * Escala, false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < uteis.Count; i++)
		{
			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados e cala quando nao tem (a primeira tira
			// da `--diagraio` saiu um retangulo preto sem erro nenhum no log). Os recortes ja saem em
			// RGBA8 do `Tomar`; a copia aqui e pra a escala nao mexer no original.
			var pedaco = (Image)uteis[i].Duplicate();
			pedaco.Convert(Image.Format.Rgba8);
			pedaco.Resize(pedaco.GetWidth() * Escala, pedaco.GetHeight() * Escala, Image.Interpolation.Nearest);
			colagem.BlitRect(pedaco, new Rect2I(Vector2I.Zero, pedaco.GetSize()),
							 new Vector2I(i * (lado + Vao) * Escala, 0));
		}

		Gravar(colagem, arquivo, "a tira");
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private void Fechar()
	{
		_acabou = true;
		S?.LimparAFoto();

		GD.Print("\n[pose] ===== A POSE DO RAIO, FOTOGRAFADA NO CORPO =====");
		foreach (string l in _linhas) GD.Print("[pose] " + l);
		GD.Print(_falhas.Count == 0
			? $"[pose] ===== TUDO OK ({_tomadas.Count} tomadas) ====="
			: $"[pose] ===== {_falhas.Count} FALHA(S) =====\n[pose]   " + string.Join("\n[pose]   ", _falhas));
		GetTree().Quit();
	}
}
