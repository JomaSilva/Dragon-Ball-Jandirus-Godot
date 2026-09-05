using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ OS TRES PEDIDOS, FOTOGRAFADOS (`--diagcorpos`) ============================
/// A `--doiscorposteste` mede os tres pedidos do dono em NUMERO, com oito defeitos injetados, e fecha
/// 103 de 103. **E ela ficaria verde com o corpo desenhado atravessando o outro na tela**: entre a caixa
/// dos pes do servidor e o pixel ha o snapshot, o `World`, a interpolacao do corpo remoto e o Y-sort.
///
/// Este projeto ja catalogou esse cego quatro vezes (a memoria "a bancada mede INTENCAO"), e as fotos
/// que o dono pediu sao exatamente a metade que so o olho fecha:
///
///     corpos-A-colisao.png      dois corpos andando um contra o outro, e parando
///     corpos-B-no-colo.png      alguem sendo CARREGADO no ar
///     corpos-C-cadaver.png      o cadaver no chao, ANTES e DEPOIS do enterro
/// ======================================================================================================
///
/// ============================ NADA AQUI E FORJADO ============================
/// O passo sai do `AplicarComando` (o atuador da IA, onde mora o `MoveRules.Advance` com a
/// `Vizinhanca`), o agarrao sai do `AlternarAgarrao` (a tecla), o voo sai do `AlternarVoo` (o verbo
/// `Fly`), a morte sai do `CombatState.Morrer` e o cadaver nasce do laco de NPC morto do `TickCombate`.
/// **E o enterro sai do `SendVerbo("enterrar")` do PROPRIO CLIENTE** -- o mesmo pacote que o botao do
/// menu da tecla E manda. Ver `GameServer.FotoDosCorpos.cs`.
/// ==========================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     &lt;godot&gt; --path . --host --rede 7942 --diagcorpos --position 1920,0 --resolution 1600x900 \
///              --raca Human --conta bancada_foto_corpos --nome Olheiro
///
/// A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o dono trabalha no principal.
/// </summary>
public partial class RoboDeColisao : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S
		=> Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>Depois disto ela desiste e conta o que faltou -- uma bancada travada se le como bancada morta.</summary>
	private const double Paciencia = 240;

	/// <summary>
	/// QUANTO O CORPO SOBE NA CENA B, em pixels de mundo.
	///
	/// **NAO E O TETO DO VOO, E E DE PROPOSITO.** O cliente desenha altura com
	/// <see cref="Voo.EscalaNaTela"/> = 0,25 -- entao 576 px de altitude viram 144 px de deslocamento na
	/// tela, e os dois corpos sairiam do enquadramento junto com a sombra deles. Cento e sessenta px de
	/// mundo dao 40 px de tela: alto o bastante pra o andar de voo ser 1 (o que a legenda cobra em
	/// numero) e baixo o bastante pra a foto mostrar o corpo, a SOMBRA e o chao no mesmo quadro -- que e
	/// o que prova que ele esta no ar.
	/// </summary>
	private const float AlturaDaCena = 5 * ZoneCollision.TileSize;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;
	private int _obrasAntes = -1;

	private int _alfa, _beta, _defunto;
	private Vec2 _origem, _rumo;
	private Facing _olhar = Facing.South;
	private float _ultimoPasso;
	private Vec2 _ondeCaiu;

	/// <summary>Quando o cadaver apareceu no SERVIDOR -- o zero do obturador que espera o DESENHO.</summary>
	private double _viOCadaverEm = -1;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --    " + oque);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private const int PAssentar = 0, PMontar = 1, PA_Antes = 2, PA_Colidir = 3,
					  PA_Ocupado = 11, PA_Livre = 12, PA_Alvo = 14,
					  PB_Pegar = 4, PB_Subir = 5,
					  PC_Matar = 6, PC_Cadaver = 7, PC_Enterrar = 8, PC_Escrever = 13, PC_Depois = 9,
					  PFim = 10;

	/// <summary>O que a bancada escreve na lapide -- e o que a C2 tem que ler de volta.</summary>
	private const string EpitafioDaBancada = "Aqui jaz o alvo da bancada, que caiu pra provar a colisao";

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagcorpos` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s) no passo {_passo}"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case PMontar: Montar(srv, cli, mundo); break;
			case PA_Antes: A_Antes(mundo); break;
			case PA_Colidir: A_Colidir(srv, mundo, delta); break;
			case PA_Ocupado: A_Ocupado(srv, cli, mundo); break;
			case PA_Livre: A_Livre(srv, cli, mundo); break;
			case PA_Alvo: A_Alvo(cli, mundo); break;
			case PB_Pegar: B_Pegar(srv, mundo); break;
			case PB_Subir: B_Subir(srv, mundo); break;
			case PC_Matar: C_Matar(srv, cli); break;
			case PC_Cadaver: C_Cadaver(srv, cli, mundo); break;
			case PC_Enterrar: C_Enterrar(srv, cli); break;
			case PC_Escrever: C_Escrever(srv, cli); break;
			case PC_Depois: C_Depois(srv, cli, mundo); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// TRES SEGUNDOS: um corpo recem-criado entra nocauteado por um instante, e o `PodeMexerOCorpo`
		// recusaria tudo com razao. Esperar o estado e mais honesto que escrever `KO = false`.
		if (_t < 3) return;

		_obrasAntes = srv.ObrasAgoraNaFotoDeCorpos();

		// ============================ A CENA PRECISA CABER NA CAMERA ============================
		// Seis tiles, e nao os dezesseis da `--doiscorposteste`: aquela mede um passeio longo (pra
		// "nao atravessou" nao se confundir com "nao deu tempo"), esta so precisa de tres tiles entre os
		// dois corpos -- e de que o palco fique DENTRO do enquadramento, porque a camera segue o
		// jogador e um palco a vinte tiles seria uma foto do nada.
		// ====================================================================================
		(bool achou, Vec2 origem, Facing rumo) = srv.PalcoDaFotoDeCorpos(cli.LocalId, 6);
		Conferir(achou, "achei uma reta caminhavel (parede E agua) pra montar a cena de colisao");
		if (!achou) { Fechar(); return; }

		_origem = origem;
		_olhar = rumo;
		_rumo = Jandirus.Core.Combat.MeleeArea.Frente(rumo);
		Nota($"palco em ({origem.X:0},{origem.Y:0}), rumo {rumo}");
		Virar(PMontar);
	}

	// =====================================================================
	// 1) OS DOIS CORPOS
	// =====================================================================
	private void Montar(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_alfa == 0)
		{
			(Vec2 meu, _, _, _, _, _, _, _, _, _) = srv.EstadoDoCorpoNaFoto(cli.LocalId);
			_alfa = srv.ForjarParaAFotoDeCorpos(cli.LocalId, _origem - meu, "Alfa", 5_000_000);
			_beta = srv.ForjarParaAFotoDeCorpos(cli.LocalId,
				_origem + _rumo * (3 * ZoneCollision.TileSize) - meu, "Beta", 5_000_000);
			Conferir(_alfa != 0 && _beta != 0, "os dois corpos entraram no mundo pelo `PorNoMundo`");
			if (_alfa == 0 || _beta == 0) { Fechar(); return; }

			srv.EnsinarAVoarNaFotoDeCorpos(_alfa);   // a mesma concessao do `--vooteste`
			srv.ApontarNaFotoDeCorpos(_alfa, _olhar);
			_t = 0;
			return;
		}

		// ============================ ESPERA O CORPO SER **DESENHADO**, e nao so existir ============================
		// Um corpo forjado sem aparencia apresentada nasce como marcador invisivel, e a foto sai com o
		// chao e nada mais -- com todas as checagens verdes, porque elas leem o estado do servidor. A
		// `--diagraio` ja pagou essa foto duas vezes.
		// =======================================================================================================
		if (mundo.CorpoDeTeste(_alfa) == null || mundo.CorpoDeTeste(_beta) == null)
		{
			if (_t < 8) return;
			Conferir(false, "os dois corpos tem NODE DESENHADO na tela (sem isto toda foto abaixo e do chao vazio)");
			Fechar();
			return;
		}
		if (_t < 1.0) return;   // um segundo pra o sprite vestir e a interpolacao assentar

		Conferir(true, "os dois corpos tem node desenhado na tela");
		Virar(PA_Antes);
	}

	// =====================================================================
	// A) DOIS CORPOS COLIDINDO
	// =====================================================================
	private void A_Antes(World mundo)
	{
		if (_t < 0.4) return;
		Tomar(mundo, "corpos-a1-antes", "A1: os dois separados, tres tiles -- o CONTROLE", _alfa, _beta);
		Virar(PA_Colidir);
	}

	/// <summary>
	/// ANDA CONTRA O OUTRO, todo quadro, e MEDE o passo.
	///
	/// **O NUMERO E A METADE DA FOTO.** Um corpo parado ao lado de outro nao diz se ele parou porque
	/// esbarrou ou porque nunca tentou andar -- e essa e a diferenca que a foto sozinha nao mostra. O
	/// `EmpurrarNaFotoDeCorpos` devolve quanto o corpo andou DE FATO no tique: com o comando dado e o
	/// passo em zero, o que esta na frente dele e um corpo.
	/// </summary>
	private void A_Colidir(Jandirus.Server.GameServer srv, World mundo, double delta)
	{
		_ultimoPasso = srv.EmpurrarNaFotoDeCorpos(_alfa, _rumo, delta);
		if (_t < 3.0) return;

		(Vec2 pa, _, _, _, _, _, _, _, _, _) = srv.EstadoDoCorpoNaFoto(_alfa);
		(Vec2 pb, _, _, _, _, _, _, _, _, _) = srv.EstadoDoCorpoNaFoto(_beta);
		float vao = (pb - pa).Length;

		Tomar(mundo, "corpos-a2-colidindo",
			  $"A2: Alfa ANDANDO contra Beta -- passo do tique {_ultimoPasso:0.00} px, vao {vao:0} px",
			  _alfa, _beta);

		Conferir(_ultimoPasso < 0.5f,
			$"COLIDIRAM: com o comando de andar dado, o passo do tique e {_ultimoPasso:0.00} px "
			+ "(um corpo na frente para o passo)");
		Conferir(vao < 2.5f * ZoneCollision.TileSize,
			$"...e eles pararam ENCOSTADOS ({vao:0} px de vao), e nao a meio mapa de distancia");
		Conferir(vao > Jandirus.Core.World.MoveRules.BodyHalfW,
			$"...e NAO um DENTRO do outro ({vao:0} px -- as caixas dos pes tem "
			+ $"{2 * Jandirus.Core.World.MoveRules.BodyHalfW:0} px de largura)");

		Montar("corpos-A-colisao.png", ["corpos-a1-antes", "corpos-a2-colidindo"]);

		// A GUARDA SOBE AGORA, e o proximo passo espera o SNAPSHOT dela chegar. Ver `A_Ocupado`.
		srv.GuardarNaFotoDeCorpos(_beta, true);
		Virar(PA_Ocupado);
	}

	// =====================================================================
	// A3) O CORPO **OCUPADO**, VISTO DO LADO DO CLIENTE
	// =====================================================================
	/// <summary>
	/// ============================ A METADE QUE SO ESTE PROCESSO ENXERGA ============================
	/// A `--doiscorposteste` mede o pedido do dono -- *"n de pra empurar npcs ou outros players ao andar
	/// contra eles enquando eles batem ou fazem outra coisa"* -- com dois corpos do SERVIDOR, e fecha.
	/// **E ela ficaria verde com o byte de ocupacao nunca saindo do servidor**: quem para o corpo do
	/// JOGADOR e a previsao do cliente (o servidor abstem-se de conferir corpo no `ValidateStep`, e a
	/// razao esta escrita la), entao se `EntityState.Ocupacao` nao chegasse aqui, o jogador continuaria
	/// atravessando quem esta batendo -- e nenhuma linha da outra bancada ficaria vermelha.
	///
	/// Este passo le a resposta pelo caminho do jogador, inteiro e sem atalho:
	///
	///     servidor -> `EstadoDe` -> `EntityState.Write` -> rede -> `Read` -> `World.AoReceberSnapshot`
	///              -> `RemotePlayer.Receive` -> `RemotePlayer.Ocupacao` -> `MontarGradeDeCorpos`
	///              -> `GradeDeCorpos.Quem`
	///
	/// ============================ E COM O CONTRA-EXEMPLO NA MESMA POSICAO ============================
	/// A pergunta e feita DUAS vezes no mesmo ponto e com o mesmo modo (`Voando`), mudando so uma
	/// coisa: a guarda de Beta. Com ela erguida ele barra; abaixada, ele deixa passar -- que e o
	/// `mob/Cross` do DM e o comportamento de sempre. Uma medida so, do lado positivo, ficaria verde
	/// com um cliente que barrasse TODO mundo (e ai voar viraria impossivel e ninguem saberia por que).
	/// ============================================================================================
	/// </summary>
	private void A_Ocupado(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		// MEIO SEGUNDO: o snapshot sai a 30 Hz, mas o corpo remoto e desenhado com atraso fixo e a
		// `MontarGradeDeCorpos` usa a posicao DESENHADA. Perguntar no mesmo quadro em que a guarda subiu
		// mediria o pacote anterior.
		if (_t < 0.5) return;

		if (mundo.CorpoDeTeste(_beta) is not { } corpoDeBeta)
		{
			Conferir(false, "A3: Beta tem corpo desenhado neste cliente (sem ele nao ha grade a medir)");
			Virar(PB_Pegar);
			return;
		}

		// A POSICAO E O ANDAR SAO OS DO **DESENHO**, que e o que a grade do cliente usa.
		Vec2 pesDeBeta = ClasseDeCorpo.Pes(new Vec2(corpoDeBeta.Position.X, corpoDeBeta.Position.Y));
		int andarDeBeta = Voo.Andar((corpoDeBeta as RemotePlayer)?.AlturaDeTeste ?? 0f);

		// DE LONGE, pra o `jaSobrepondo` nao ser quem responde: a pergunta e "ele me barra ao chegar",
		// e nao "eu ja estava dentro dele".
		Vec2 deLonge = pesDeBeta - _rumo * (4 * ZoneCollision.TileSize);

		int BarrouVoando(out Ocupacao oc)
		{
			var grade = new GradeDeCorpos();
			mundo.MontarGradeDeCorpos(grade);
			return grade.Quem(pesDeBeta, andarDeBeta, cli.LocalId, deLonge, ModoDeTravessia.Voando, out oc);
		}

		int comGuarda = BarrouVoando(out Ocupacao ocupacaoNaTela);
		Nota($"A3: a grade DESTE CLIENTE responde id {comGuarda} pra quem chega VOANDO, "
		   + $"e diz que ele esta '{CorpoOcupado.Nome(ocupacaoNaTela)}'");

		Conferir(ocupacaoNaTela == Ocupacao.Guardando,
			"A3: a OCUPACAO de Beta ATRAVESSOU O FIO e chegou na grade deste cliente "
			+ $"(o servidor diz guarda erguida; a tela diz '{CorpoOcupado.Nome(ocupacaoNaTela)}')");
		Conferir(comGuarda == _beta,
			"A3: ...e por causa dela ele BARRA quem chega VOANDO -- o pedido do dono, "
			+ "medido no cliente que preve o passo do jogador");

		// ---- O CONTRA-EXEMPLO: a MESMA pergunta com a guarda abaixada ----
		srv.GuardarNaFotoDeCorpos(_beta, false);
		_t = 0;
		Virar(PA_Livre);
	}

	private void A_Livre(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_t < 0.5) return;

		if (mundo.CorpoDeTeste(_beta) is not { } corpoDeBeta) { Virar(PA_Alvo); return; }
		Vec2 pesDeBeta = ClasseDeCorpo.Pes(new Vec2(corpoDeBeta.Position.X, corpoDeBeta.Position.Y));
		int andarDeBeta = Voo.Andar((corpoDeBeta as RemotePlayer)?.AlturaDeTeste ?? 0f);
		Vec2 deLonge = pesDeBeta - _rumo * (4 * ZoneCollision.TileSize);

		var grade = new GradeDeCorpos();
		mundo.MontarGradeDeCorpos(grade);
		int semGuarda = grade.Quem(pesDeBeta, andarDeBeta, cli.LocalId, deLonge,
								   ModoDeTravessia.Voando, out Ocupacao ocupacao);
		int aPe = grade.Quem(pesDeBeta, andarDeBeta, cli.LocalId, deLonge, ModoDeTravessia.APe);

		Conferir(ocupacao == Ocupacao.Livre && semGuarda == 0,
			"A3: CONTRA-EXEMPLO -- com a guarda ABAIXADA o MESMO corpo, no MESMO ponto, deixa passar "
			+ $"quem voa (id {semGuarda}, '{CorpoOcupado.Nome(ocupacao)}') -- e o `mob/Cross` do DM");
		Conferir(aPe == _beta,
			"A3: ...e A PE ele continua barrando, ocupado ou nao (a regra antiga nao foi trocada, "
			+ "so ganhou um segundo termo)");

		Virar(PA_Alvo);
	}

	/// <summary>
	/// A4 -- O DUPLO CLIQUE EM MIM SOLTA O ALVO. O dono (2026-09-05): *"com zanzoken ou nao, ao clicar em si
	/// mesmo 2 vezes tiraria qualquer target"*. O gesto e dado sem mouse (`DuploCliqueDeTeste`) nos pixels onde
	/// o Beta e o meu corpo estao DESENHADOS -- a mesma conta que o clique de verdade faz. A skill e plantada
	/// so no cliente (`SkillsAprendidas` e o que o `World` consulta); o servidor nao a tem e recusa a piscada
	/// do contra-exemplo em silencio, o que e o suficiente: a pergunta e o que o CLIENTE pede.
	/// </summary>
	private void A_Alvo(GameClient cli, World mundo)
	{
		if (_t < 0.3) return;
		if (mundo.CorpoDeTeste(_beta) is not { } beta || mundo.PosicaoLocalDeTeste is not { } eu)
		{
			Conferir(false, "A4: preciso do Beta desenhado e do meu corpo pra dar os duplos cliques");
			Virar(PB_Pegar);
			return;
		}
		Vector2 emBeta = beta.GlobalPosition;
		mundo.DuploCliqueDeTeste(emBeta);
		Conferir(cli.AlvoId == _beta, $"A4: duplo clique em cima do Beta MARCA o Beta (alvo {cli.AlvoId}, Beta {_beta})");

		int piscadas = mundo.ZanzokensPedidosDeTeste;
		mundo.DuploCliqueDeTeste(eu);
		Conferir(cli.AlvoId == 0 && mundo.ZanzokensPedidosDeTeste == piscadas,
			"A4: duplo clique em MIM solta o alvo -- e nao pede piscada nenhuma (sem Zanzoken)");

		bool sabia = cli.SkillsAprendidas.Contains(World.PathDoZanzoken);
		cli.SkillsAprendidas.Add(World.PathDoZanzoken);
		try
		{
			mundo.DuploCliqueDeTeste(emBeta);
			Conferir(cli.AlvoId == _beta, "A4: COM Zanzoken, duplo clique no Beta continua marcando");
			mundo.DuploCliqueDeTeste(eu);
			Conferir(cli.AlvoId == 0 && mundo.ZanzokensPedidosDeTeste == piscadas,
				"A4: COM Zanzoken, duplo clique em MIM solta o alvo e NAO vira piscada (era o defeito: uma piscada de zero pixels)");
			Vector2 chao = eu + new Vector2(2 * ZoneCollision.TileSize, 0);
			mundo.DuploCliqueDeTeste(chao);
			Conferir(mundo.ZanzokensPedidosDeTeste == piscadas + 1 && cli.AlvoId == 0,
				"A4: CONTRA-EXEMPLO -- COM Zanzoken, duplo clique no CHAO continua sendo a piscada (e nao mexe no alvo)");
		}
		finally { if (!sabia) cli.SkillsAprendidas.Remove(World.PathDoZanzoken); }
		Virar(PB_Pegar);
	}

	// =====================================================================
	// B) CARREGADO NO AR
	// =====================================================================
	private void B_Pegar(Jandirus.Server.GameServer srv, World mundo)
	{
		if (_t < 0.3) return;

		srv.AgarrarNaFotoDeCorpos(_alfa);   // 1o toque: segurando
		srv.AgarrarNaFotoDeCorpos(_alfa);   // 2o toque: no colo -- `grabMode=2`, `Grabbing.dm:78-84`

		(_, _, _, _, int agarrando, _, bool carregando, _, _, _) = srv.EstadoDoCorpoNaFoto(_alfa);
		Conferir(agarrando == _beta && carregando,
			"o DUPLO TOQUE da tecla de agarrar pos Beta no colo de Alfa (`grabMode=2`)");
		if (!carregando) { Virar(PC_Matar); return; }

		Tomar(mundo, "corpos-b1-no-chao", "B1: no colo, ainda no CHAO -- o controle da altura", _alfa, _beta);

		srv.VoarNaFotoDeCorpos(_alfa, true);
		srv.SubirNaFotoDeCorpos(_alfa, true);
		Virar(PB_Subir);
	}

	private void B_Subir(Jandirus.Server.GameServer srv, World mundo)
	{
		(_, float ha, int andarA, _, _, _, _, _, _, _) = srv.EstadoDoCorpoNaFoto(_alfa);
		if (ha < AlturaDaCena && _t < 12) return;

		srv.SubirNaFotoDeCorpos(_alfa, false);
		if (_t < 12.5) return;   // meio segundo pra a altura assentar e o desenho acompanhar

		(_, ha, andarA, bool voandoA, _, _, _, _, _, _) = srv.EstadoDoCorpoNaFoto(_alfa);
		(_, float hb, int andarB, bool voandoB, _, int presoPor, _, _, _, _) =
			srv.EstadoDoCorpoNaFoto(_beta);

		Tomar(mundo, "corpos-b2-no-ar",
			  $"B2: CARREGADO NO AR -- quem carrega {ha:0} px (andar {andarA}), "
			+ $"o carregado {hb:0} px (andar {andarB})",
			  _alfa, _beta);

		Conferir(andarA > 0, $"quem carrega esta MESMO no ar (andar {andarA}, {ha:0} px)");
		Conferir(Mathf.Abs(ha - hb) < 0.01f,
			$"...e o CARREGADO esta na MESMA altitude ({hb:0} px x {ha:0} px)");
		Conferir(andarA == andarB, $"...e portanto no mesmo andar de voo ({andarB} x {andarA})");
		Conferir(voandoB, "...e ele conta como VOANDO no fio (`EntityState.Voando`, derivado da ALTURA)");
		Conferir(presoPor == _alfa, "...e continua preso a quem o carrega depois da subida");

		// ============================ A SOMBRA E O QUE O OLHO LE ============================
		// A foto de um corpo no ar e a foto de um corpo desenhado mais pra cima -- o que, sozinho, e
		// indistinguivel de um corpo um tile mais ao norte. O que separa os dois na tela e a SOMBRA no
		// chao, que o `RemotePlayer.CriarSombra` desenha sob demanda pra quem tem altura. Por isso a
		// legenda diz a altura e a linha abaixo cobra que a sombra exista.
		// ==================================================================================
		Conferir(SombraDe(World.Instancia, _alfa),
			"...e quem carrega tem SOMBRA desenhada no chao (e ela, e nao a posicao, que faz o olho ler 'no ar')");

		Montar("corpos-B-no-colo.png", ["corpos-b1-no-chao", "corpos-b2-no-ar"]);

		// DESCE E LARGA -- e o corpo largado cai sozinho pelo `TickDoVoo`, que e o que a bancada de
		// numero ja mede. Aqui a descida so existe pra a cena C comecar com todo mundo no chao.
		srv.AgarrarNaFotoDeCorpos(_alfa);
		srv.VoarNaFotoDeCorpos(_alfa, false);
		Virar(PC_Matar);
	}

	private static bool SombraDe(World? mundo, int id)
		=> mundo?.CorpoDeTeste(id)?.GetNodeOrNull("SombraDeVoo") != null;

	// =====================================================================
	// C) O CADAVER, ANTES E DEPOIS DO ENTERRO
	// =====================================================================
	private void C_Matar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// ESPERA OS DOIS POUSAREM: um cadaver fotografado com um corpo caindo em cima nao mostra nada.
		if (_t < 6) return;

		if (_defunto == 0)
		{
			// ============================ O DEFUNTO NASCE PERTO DO JOGADOR ============================
			// Enterrar e do JOGADOR (`SendVerbo("enterrar")`, o mesmo pacote do botao do menu da tecla E),
			// e o servidor cobra o alcance de `Interacoes.Alcance` (64 px POR EIXO) contra a posicao do
			// CORPO. Um defunto no palco de colisao, a cinco tiles, seria recusado com razao -- e a foto
			// do "depois" mostraria o mesmo cadaver.
			// ======================================================================================
			_defunto = srv.ForjarParaAFotoDeCorpos(cli.LocalId,
				new Vec2(1.5f * ZoneCollision.TileSize, 0), "Defunto", 5_000_000);
			Conferir(_defunto != 0, "o terceiro corpo entrou no mundo, ao alcance do braco do jogador");
			if (_defunto == 0) { Fechar(); return; }
			_t = 0;
			return;
		}

		if (_t < 1.5) return;

		(_ondeCaiu, _, _, _, _, _, _, _, _, _) = srv.EstadoDoCorpoNaFoto(_defunto);
		Conferir(srv.MatarNaFotoDeCorpos(_defunto),
			"ele MORREU pelo funil de producao (`CombatState.Morrer`) -- e o cadaver nasce do "
			+ "laco de NPC morto do `TickCombate`, o `mobDeath.dm:15` do original");
		Virar(PC_Cadaver);
	}

	private void C_Cadaver(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		// ============================ O CADAVER NAO NASCE NA MORTE: NASCE NA PARTIDA ============================
		// Quinze segundos (`Alem.MsNoChao`), e a espera nao e folga -- e o desenho. Enquanto o prazo
		// corre, **o corpo no chao ainda e A PESSOA**: o cadaver so e erguido no instante em que o corpo
		// sai do mundo (pro jogador, quando ele viaja pro alem; pro corpo sem dono, quando o
		// `_npcsPraTirar` o recolhe). Assim o lugar tem sempre UM corpo, e nao dois empilhados.
		//
		// A primeira rodada esperava dez segundos e reprovou com "o cadaver ficou no chao" -- ela estava
		// olhando pro corpo certo, cinco segundos cedo demais.
		// ====================================================================================================
		(int idCorpo, string nome) = srv.CadaverPertoNaFotoDeCorpos(cli.LocalId);
		if (idCorpo == 0)
		{
			if (_t < Alem.MsNoChao / 1000.0 + 8) return;
			Conferir(false, $"o cadaver ficou no chao, ao alcance do jogador (esperei "
						  + $"{Alem.MsNoChao / 1000.0 + 8:0} s -- {Alem.MsNoChao / 1000} de `MsNoChao` e mais 8)");
			Fechar();
			return;
		}
		// ============================ E ESPERA O NODE DELE APARECER, DE VERDADE ============================
		// Esta guarda era `if (CorpoDeTeste == null) { if (_t < 10) return; }` -- **sem `else`**, ou seja
		// ela nao esperava nada: no primeiro quadro em que o cadaver existia no servidor, o robo seguia
		// em frente e reprovava "ele esta DESENHADO na tela". O corpo nao estava errado; o node ainda nao
		// tinha nascido (a aparencia sai por pacote confiavel e o snapshot por outro canal).
		//
		// A licao e a mesma que o obturador da `--diagpose` ja tinha escrito: **estado de servidor nao e
		// pixel**, e quem fotografa tem que esperar o desenho -- e falhar com prazo, nao no primeiro
		// quadro.
		// ================================================================================================
		if (_viOCadaverEm < 0) _viOCadaverEm = _t;
		if (mundo.CorpoDeTeste(idCorpo) == null && _t - _viOCadaverEm < 8) return;

		// ...e mais um segundo pra a pose de DEITADO chegar e a interpolacao assentar.
		if (_t - _viOCadaverEm < 1.0) return;

		(Vec2 pos, _, _, _, _, _, _, bool eCadaver, _, _) = srv.EstadoDoCorpoNaFoto(idCorpo);

		Tomar(mundo, "corpos-c1-cadaver", $"C1: o CADAVER no chao -- \"{nome}\"", idCorpo, cli.LocalId);

		Conferir(eCadaver, $"O CORPO FICOU: ha um cadaver no chao (\"{nome}\")");
		Conferir((pos - _ondeCaiu).Length < 1f,
			$"...no ponto EXATO onde ele caiu ({(pos - _ondeCaiu).Length:0.0} px de diferenca)");
		Conferir(mundo.CorpoDeTeste(idCorpo) != null,
			"...e ele esta DESENHADO na tela (o cadaver e um corpo, e o snapshot o manda como um)");

		// ============================ E ELE ESTA DEITADO **NO DESENHO**, e nao so no campo ============================
		// `ServerPlayer.Deitado` e um `bool` do servidor e ele estaria verdadeiro mesmo se o cliente
		// desenhasse o cadaver em pe -- que e a diferenca inteira entre esta bancada e a de numero. A
		// animacao `ko` e a UNICA das 48 do corpo que e um desenho deitado (`CharacterVisual.PoseDeitada`),
		// entao perguntar o nome dela e perguntar o pixel.
		// =========================================================================================================
		string pose = mundo.CorpoDeTeste(idCorpo)?.GetNodeOrNull<CharacterVisual>("Visual")?.PoseDeTeste ?? "";
		Conferir(pose.StartsWith("ko", StringComparison.Ordinal),
			$"...DEITADO no desenho, e nao so no campo (a animacao do corpo e `{pose}` -- `ko` e a unica "
			+ "das 48 que e um desenho deitado)");

		_defunto = idCorpo;   // dali pra frente o sujeito das fotos e o CADAVER, e nao quem morreu
		Virar(PC_Enterrar);
	}

	private void C_Enterrar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		// ============================ O ENTERRO SAI DO MENU DA TECLA E, DE VERDADE ============================
		// Antes esta bancada mandava `SendVerbo("enterrar")` na mao, "o mesmo pacote do botao". Deixou de
		// ser o mesmo em 2026-09-04: o botao "Enterrar" agora abre uma CAIXA DE TEXTO (`Forma.Texto`, o
		// `input()` do `Corpse.dm:20`) e o verbo sai com o epitafio como argumento. Um atalho aqui
		// provaria o servidor e deixaria de fora justamente a caixa -- entao o E e apertado de verdade
		// (pelo `_UnhandledInput`, como o `RoboDeEmbarque`), o botao e o do menu, e o texto e digitado
		// na caixa. O passo seguinte (`C_Escrever`) le o que ela mostrou e escreve por cima.
		// ==================================================================================================
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
		Nota("apertei E sobre o cadaver -- o menu de interacao, e nao um verbo na mao");
		Virar(PC_Escrever);
	}

	/// <summary>
	/// A CAIXA DE TEXTO DA LAPIDE, pelo caminho do jogador: o menu aberto, o botao "Enterrar", a caixa
	/// ja preenchida com a resposta pronta do DM (`"Here lies [name]"`), o texto digitado por cima e o
	/// "Gravar". O que sai no fio e o verbo `enterrar` com o texto; a C2 confere a lapide.
	/// </summary>
	private void C_Escrever(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;
		MenuDeInteracao? menu = MenuDeInteracao.Instancia;
		Conferir(menu is { NaTela: true }, "o E abriu o menu de interacao sobre o cadaver");
		if (menu is not { NaTela: true }) { Virar(PC_Depois); return; }

		string titulo = menu.TituloDesenhado;
		Conferir(menu.ApertarDesenhado("Enterrar"),
				 $"...com o botao Enterrar (o menu tem: {string.Join(" | ", menu.BotoesDesenhados())})");
		Conferir(menu.CaixaDeTextoNaTela,
				 "...e Enterrar abre a CAIXA DE TEXTO da lapide (`Forma.Texto`) em vez de mandar o verbo direto");

		string esperado = Jandirus.Core.World.Cadaver.EpitafioPadrao(Jandirus.Core.World.Cadaver.QuemJazEm(titulo));
		string inicial = menu.DigitarNaCaixa(EpitafioDaBancada);
		Conferir(inicial == esperado,
				 $"...ja preenchida com o padrao do DM, \"{esperado}\" (`Corpse.dm:20`) -- estava \"{inicial}\"");
		Nota($"digitei \"{EpitafioDaBancada}\" e apertei Gravar: o verbo `enterrar` sai com o texto como argumento");
		Virar(PC_Depois);
	}

	private void C_Depois(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		// UM SEGUNDO E MEIO: o enterro manda `MandarObras` pela rede e o cliente remonta as construcoes
		// da zona. Fotografar no mesmo quadro mostraria o corpo ja apagado e a lapide ainda nao desenhada.
		if (_t < 1.5) return;

		(bool achou, string tipo, string epitafio, Vec2 onde) = srv.LapideNaFotoDeCorpos(cli.LocalId);
		(int aindaTem, _) = srv.CadaverPertoNaFotoDeCorpos(cli.LocalId);

		// O ENQUADRAMENTO DA C2 E O MESMO DA C1 -- o corpo do JOGADOR, que nao andou entre as duas. A
		// lapide nao e uma entidade (e uma `Obra`, desenhada pelo cenario), entao ela nao tem posicao
		// desenhada pra enquadrar; e enquadrar pelo cadaver seria impossivel, porque ele acabou de sumir.
		// Mesma camera, mesmo canto do mundo: e o que faz as duas fotos serem comparaveis.
		Tomar(mundo, "corpos-c2-enterrado",
			  $"C2: DEPOIS do enterro -- \"{epitafio}\" ({tipo})",
			  cli.LocalId);

		Conferir(aindaTem == 0, "ENTERROU: o corpo sumiu do mundo");
		Conferir(achou, "...e nasceu uma LAPIDE no lugar");
		Conferir(achou && (onde - _ondeCaiu).Length < 1f,
			$"...no ponto onde o corpo estava ({(onde - _ondeCaiu).Length:0.0} px de diferenca)");
		Conferir(epitafio == EpitafioDaBancada,
				 $"...com o epitafio DIGITADO na caixa gravado nela: \"{epitafio}\" (o servidor limpou e guardou o que veio no verbo)");

		Montar("corpos-C-cadaver.png", ["corpos-c1-cadaver", "corpos-c2-enterrado"]);
		Virar(PFim);
	}

	// =====================================================================
	// AS FOTOS
	// =====================================================================
	private sealed class Tomada
	{
		public required string Nome;
		public required string Rotulo;

		/// <summary>A tela inteira -- prova o LUGAR (que planeta, que canto do mapa).</summary>
		public Image? Quadro;

		/// <summary>O recorte ampliado em volta dos sujeitos -- prova a CENA. Ver <see cref="Tomar"/>.</summary>
		public Image? Perto;
	}

	/// <summary>
	/// ============================ O RECORTE, E POR QUE ELE E OBRIGATORIO AQUI ============================
	/// A primeira rodada gravou so a tela cheia, e as tres fotos ficaram tecnicamente certas e ilegiveis:
	/// num quadro de 1600x900 num zoom de 1x, dois corpos encostados sao dois bonecos de 32 px a doze
	/// pixels um do outro, no canto inferior. *"O vao e de 12 px"* e uma afirmacao que a imagem nao
	/// consegue sustentar nesse tamanho -- e uma foto que so confirma o que o texto ja disse nao esta
	/// provando nada.
	///
	/// Dez tiles de recorte e ampliacao de 3x em Nearest (o filtro dos outros robos deste projeto: em
	/// pixel art, interpolar e inventar). Os dois arquivos ficam: a tela cheia prova o LUGAR, o recorte
	/// prova a CENA -- e as tiras sao montadas com o recorte.
	/// ==================================================================================================
	/// </summary>
	private const int LadoDoRecorte = 320, EscalaDoRecorte = 3;

	private readonly List<Tomada> _tomadas = [];

	private Tomada? Achar(string nome) => _tomadas.Find(t => t.Nome == nome);

	/// <summary>
	/// A FOTO E A TELA INTEIRA, e nao um recorte em volta de um corpo.
	///
	/// As bancadas de aparencia deste projeto recortam 192 px em volta do boneco porque a pergunta delas
	/// e sobre o corpo. **A pergunta desta e sobre a RELACAO entre dois corpos** -- o vao entre eles, um
	/// no ar e o outro no chao, o cadaver e a lapide no mesmo lugar --, e um recorte apertado o
	/// bastante pra mostrar detalhe corta justamente o segundo sujeito da cena.
	/// </summary>
	private void Tomar(World mundo, string nome, string rotulo, params int[] focos)
	{
		var t = new Tomada { Nome = nome, Rotulo = rotulo };

		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty())
		{
			Nota($"{rotulo}: SEM FOTO (headless nao renderiza -- rode com janela)");
			_tomadas.Add(t);
			return;
		}

		tela.Convert(Image.Format.Rgba8);
		t.Quadro = tela;
		Gravar(tela, nome + ".png", rotulo);

		if (CentroDe(mundo, focos) is { } centro)
		{
			Image perto = tela.GetRegion(Janela(tela, centro));
			perto.Convert(Image.Format.Rgba8);
			perto.Resize(perto.GetWidth() * EscalaDoRecorte, perto.GetHeight() * EscalaDoRecorte,
						 Image.Interpolation.Nearest);
			t.Perto = perto;
			Gravar(perto, nome + "-perto.png", rotulo + " (recorte)");
		}
		else Nota($"{rotulo}: sem recorte -- nenhum dos sujeitos tem corpo desenhado");

		_tomadas.Add(t);
		_linhas.Add($"  foto  {rotulo}");
	}

	/// <summary>
	/// O CENTRO DA CENA, em pixels de TELA: a media das posicoes desenhadas dos sujeitos.
	///
	/// **DESENHADAS, e nao as do servidor.** O corpo remoto e interpolado (ele vai ~1 tique atras) e o
	/// sprite de quem esta no ar e deslocado pra cima por <see cref="Voo.EscalaNaTela"/> -- enquadrar
	/// pela posicao do servidor poria o corpo no ar fora do proprio recorte, que e exatamente a foto que
	/// esta cena existe pra tirar.
	/// </summary>
	private Vector2? CentroDe(World mundo, int[] focos)
	{
		if (focos.Length == 0) return null;
		Transform2D t = GetViewport()?.CanvasTransform ?? Transform2D.Identity;

		var soma = Vector2.Zero;
		int n = 0;
		foreach (int id in focos)
			if (mundo.PosicaoDesenhadaDe(id) is { } p) { soma += t * p; n++; }
		return n == 0 ? null : soma / n;
	}

	/// <summary>A janela do recorte, empurrada pra DENTRO da tela -- nunca cortada.</summary>
	private static Rect2I Janela(Image tela, Vector2 centro)
	{
		int lado = Math.Min(LadoDoRecorte, Math.Min(tela.GetWidth(), tela.GetHeight()));
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
	/// A TIRA, COLADA -- o ANTES e o DEPOIS lado a lado.
	///
	/// Arquivos separados obrigam quem le a abrir duas janelas e comparar de cabeca; e as tres cenas
	/// deste pedido sao todas comparativas ("separados x colididos", "no chao x no ar", "corpo x
	/// lapide"). Os quadros individuais continuam gravados.
	/// </summary>
	private void Montar(string arquivo, string[] nomes)
	{
		// A TIRA E FEITA DOS RECORTES, e nao das telas cheias: ela existe pra ser OLHADA lado a lado, e
		// duas telas de 1600 px encostadas dao um arquivo de 3200 px em que os sujeitos continuam com
		// 32 px. Os quadros cheios seguem gravados um a um.
		var uteis = new List<Image>();
		foreach (string n in nomes) if ((Achar(n)?.Perto ?? Achar(n)?.Quadro) is { } q) uteis.Add(q);
		if (uteis.Count == 0) return;

		const int Vao = 8;
		int larg = uteis[0].GetWidth(), alt = uteis[0].GetHeight();
		Image colagem = Image.CreateEmpty(larg * uteis.Count + Vao * (uteis.Count - 1), alt,
										  false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < uteis.Count; i++)
		{
			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados e CALA quando nao tem (a primeira tira da
			// `--diagraio` saiu um retangulo preto sem erro nenhum no log).
			var pedaco = (Image)uteis[i].Duplicate();
			pedaco.Convert(Image.Format.Rgba8);
			if (pedaco.GetWidth() != larg || pedaco.GetHeight() != alt) continue;
			colagem.BlitRect(pedaco, new Rect2I(Vector2I.Zero, pedaco.GetSize()),
							 new Vector2I(i * (larg + Vao), 0));
		}

		Gravar(colagem, arquivo, "a tira");
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private void Fechar()
	{
		_acabou = true;
		S?.LimparAFotoDeCorpos(_obrasAntes);

		GD.Print("\n[corpos] ===== OS TRES PEDIDOS, FOTOGRAFADOS =====");
		foreach (string l in _linhas) GD.Print("[corpos] " + l);
		GD.Print(_falhas.Count == 0
			? $"[corpos] ===== TUDO OK ({_tomadas.Count} tomadas) ====="
			: $"[corpos] ===== {_falhas.Count} FALHA(S) =====\n[corpos]   "
			  + string.Join("\n[corpos]   ", _falhas));
		GetTree().Quit();
	}
}
