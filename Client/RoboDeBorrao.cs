using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O BORRAO DO DASH, FOTOGRAFADO (`--diagborrao`) ============================
/// O dono, palavra por palavra:
///   1. *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores"*;
///   2. *"o RANGE DO TELEPORTE DO DASH ta mt grande, parece EXTREMAMENTE MAIOR q os dos jogadores"*.
///
/// A irma desta bancada e a `--borraoteste`: 41 provas, sete defeitos injetados, tudo verde. **E ela
/// fecharia verde com o borrao nunca desenhado na tela.** Tudo o que ela ve e estado de servidor -- o
/// `w.Length` do pacote `S2C.Zanzo`, o contador de anuncios, a posicao do corpo. Entre esse pacote e o
/// pixel ha o fio, o `GameClient`, o `World.AoPiscar`, a escolha da origem no `LocalPlayer`
/// (`OrigemDoSalto`) e o `RastroDeCorrida` segurando o pedido ate a posicao de CHEGADA existir.
///
/// E o relato do dono e sobre o que ele VE. Essa metade so o olho fecha, e este projeto ja catalogou o
/// cego cinco vezes (a memoria "a bancada mede INTENCAO").
/// ==========================================================================================================
///
/// ============================ AS SEIS CENAS ============================
///   A. CONTROLE       ninguem arranca -- zero copias de rastro, zero tinta. E o contra-exemplo que
///                     impede "tem tinta em tudo" de passar por "o borrao funciona".
///   B. O MESMO QUADRO o NPC e o JOGADOR arrancam JUNTOS, e a foto e UMA SO. Tres faixas paralelas:
///                     a do jogador, a do NPC e a de QUEM NAO ARRANCA -- o contra-exemplo mora dentro
///                     da mesma foto, e nao numa foto ao lado.
///   C. (dentro de B)  a faixa do parado tem que ficar LIMPA.
///   D. A FERA         o corpo POSSUIDO (`AssumirOCorpo` -- Oozaru, furia lendaria) arranca sozinho, e
///                     o rastro tem que comecar na origem que veio do SERVIDOR.
///   F. O DEFEITO      o portao ANTIGO (`if (zanzo)`) volta e o NPC salta de novo: **o mesmo salto de
///                     268 px, e zero pixel de rastro**. E a foto do ANTES do dono -- e a prova de que
///                     esta medicao de pixel sabe ficar vazia. Desfeito o defeito, o rastro volta.
///   E. O OUTRO        um SEGUNDO PROCESSO (`--socar`) arranca, e o borrao dele chega aqui pelo fio.
/// ======================================================================
///
/// ============================ COMO O PIXEL E MEDIDO -- E POR QUE ASSIM ============================
/// A arvore e PAUSADA e a tela e fotografada QUATRO vezes: com as copias do rastro, sem elas, com de
/// novo e sem de novo. A diferenca entre "com" e "sem" e o rastro e MAIS NADA -- nao ha camera se
/// mexendo, nem tremor de soco, nem quadro de animacao trocando, nem poeira nascendo. E a receita da
/// `--diagboca`, e o par "sem/sem2" existe pra medir o CHAO DE RUIDO: se ele nao for zero, a conta toda
/// e suspeita e a bancada diz isso em vez de somar barulho como se fosse tinta.
///
/// As copias sao achadas pelo grupo `RastroDeCorrida.GrupoDoRastro` -- uma etiqueta de uma linha, posta
/// no unico lugar que cria copia. Sem ela nao ha alca nenhuma: a copia nasce solta no palco, esmaece por
/// um `Tween` e se libera.
/// ==============================================================================================
///
/// COMO RODAR -- um processo so (dois, pra cena E), e ele PRECISA de janela:
///
///     &lt;godot&gt; --path . --host --rede 7916 --diagborrao --position 1920,0 --resolution 1600x900 \
///              --raca Human --conta bancada_foto_borrao --nome Olheiro
///
/// A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o dono trabalha no principal.
/// </summary>
public partial class RoboDeBorrao : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S
		=> Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>Depois disto ela desiste e conta o que faltou -- bancada travada le como bancada morta.</summary>
	private const double Paciencia = 260;

	/// <summary>
	/// O VAO ENTRE QUEM SALTA E O ALVO, em pixels de mundo. Com a marca, o arranque pesado busca ate 480
	/// (`AlcanceDoDashMarcado`) e para a 32 do alvo -- entao 300 px de vao dao **268 px de salto**, que e
	/// o mesmo numero que a familia F da `--borraoteste` mede nos dois lados.
	///
	/// **E ELE E ESCOLHIDO PELO QUADRO, e nao pela regra.** Com o zoom 2 (ver <see cref="ZoomDaCena"/>)
	/// uma janela de 1600 px mostra 800 px de mundo: um salto de 268 px cabe com a origem a 264 px da
	/// borda. Nos 448 px que o alcance maximo permite, a ponta velha do rastro sairia do enquadramento --
	/// e a foto provaria menos do que a regra permite.
	/// </summary>
	private const float VaoDoSalto = 300f;

	/// <summary>Afastamento entre as tres faixas, em pixels de mundo. Tres tiles: perto pra caber no quadro, longe pra a tinta de uma nao encostar na outra.</summary>
	private const float Faixa = 96f;

	/// <summary>
	/// ZOOM 2 (o minimo do jogo) e nao o 3 de fabrica. Em 3, 1600 px de janela mostram 533 px de mundo e
	/// um salto de 268 px com a camera centrada na CHEGADA poria a origem a 2 px da borda esquerda.
	/// </summary>
	private const int ZoomDaCena = 2;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private int _npc, _alvoDoNpc, _alvoDoJogador, _quieto;
	private Vec2 _origem, _rumo, _lado;
	private Facing _olhar = Facing.East;

	/// <summary>
	/// ============================ UMA AFIRMACAO POR TAG, E NAO UMA POR QUADRO ============================
	/// As cenas desta bancada esperam por coisas (o rastro nascer, o pacote chegar, o obturador terminar),
	/// e enquanto esperam elas rodam A CADA QUADRO. A primeira rodada saiu com a mesma linha treze vezes
	/// no relatorio e um placar de dezenas de falhas que eram uma so -- um placar que conta quadros em vez
	/// de afirmacoes nao serve pra dizer quantas coisas estao quebradas.
	///
	/// A TAG e o que fica gravado, e nao o texto: o texto carrega numeros que mudam de quadro a quadro (a
	/// contagem de copias, os pixels de tinta), entao filtrar por ele deixaria passar a mesma afirmacao
	/// varias vezes com valores diferentes -- que e o defeito de novo, so que mais dificil de ver.
	/// =================================================================================================
	/// </summary>
	private readonly HashSet<string> _jaAfirmei = [];

	private void Conferir(string tag, bool ok, string oque)
	{
		if (!_jaAfirmei.Add(tag)) return;
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --    " + oque);

	public override void _Ready()
	{
		// SEM ISTO A PAUSA SERIA ETERNA: o obturador congela a arvore, e um robo que congela junto
		// nunca chega no quadro seguinte pra descongelar. Mesma nota da `--diagboca`.
		ProcessMode = ProcessModeEnum.Always;

		// ============================ ESTE ROBO TEM QUE PENSAR ANTES DO CORPO ============================
		// A cena B aperta a TECLA (`Input.ActionPress("attack")`), e o `LocalPlayer` a le com
		// `IsActionJustPressed` -- que so e verdadeiro no QUADRO em que a tecla desceu. Com a prioridade
		// de fabrica (0) o corpo processa antes deste no na ordem da arvore: a tecla descia depois de ele
		// ja ter olhado, e no quadro seguinte ela ja nao era "recem-apertada".
		//
		// O resultado media exatamente como um sistema quebrado: o jogador nunca arrancava (`SaiuDe`
		// ficava em (0,0), o salto dava 11 mil px) e o rastro do NPC morria durante os dois segundos que a
		// bancada passava esperando o salto que nao vinha. Nenhuma das duas coisas era do jogo.
		// ============================================================================================
		ProcessPriority = -100;

		if (C is { } cli) cli.Piscou += AoPiscarAlguem;
	}

	public override void _ExitTree()
	{
		// METODO NOMEADO E `-=`: a memoria "assinaturas vazadas" existe porque lambda anonima nao da pra
		// cancelar, e o `GameClient` sobrevive ao logout.
		if (C is { } cli) cli.Piscou -= AoPiscarAlguem;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private const int PAssentar = 0, PMontar = 1, PA_Controle = 2, PB_Armar = 3, PB_Saltar = 4,
					  PB_Medir = 5, PD_Possuir = 6, PD_Medir = 7,
					  PF_Quebrar = 8, PF_Medir = 9, PF_Voltou = 10,
					  PE_Esperar = 11, PFim = 12;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagborrao` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s) no passo {_passo}"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli, mundo); break;
			case PMontar: Montar(srv, cli, mundo); break;
			case PA_Controle: A_Controle(srv, cli, mundo); break;
			case PB_Armar: B_Armar(srv, cli); break;
			case PB_Saltar: B_Saltar(srv, cli); break;
			case PB_Medir: B_Medir(srv, cli, mundo); break;
			case PD_Possuir: D_Possuir(srv, cli); break;
			case PD_Medir: D_Medir(srv, cli, mundo); break;
			case PF_Quebrar: F_Quebrar(srv, cli); break;
			case PF_Medir: F_Medir(srv, cli, mundo); break;
			case PF_Voltou: F_Voltou(srv, cli, mundo); break;
			case PE_Esperar: E_Esperar(srv, cli, mundo); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) O BERCO ASSENTA E A CAMERA AFASTA
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		// TRES SEGUNDOS: um corpo recem-criado entra nocauteado por um instante e o `PodeMexerOCorpo`
		// recusaria tudo, com razao. Esperar o estado e mais honesto que escrever `KO = false`.
		if (_t < 3) return;

		mundo.AplicarZoom(ZoomDaCena);

		// ============================ O PALCO: TRES FAIXAS CAMINHAVEIS, LADO A LADO ============================
		// Doze tiles (768 px) por faixa: o vao de 300 px, mais o corpo do alvo, mais folga pra ninguem
		// terminar encostado numa quina. **Caminhavel quer dizer parede E AGUA** -- a familia F da
		// `--borraoteste` nasceu medindo "0 arranques, 0 px andados, Ki intacto" por estar num corredor
		// sem parede dentro de um lago.
		// ===================================================================================================
		(bool achou, Vec2 origem, Facing rumo) = srv.PalcoDoBorrao(cli.LocalId, 12, Faixa);
		Conferir("palco", achou, "achei TRES faixas paralelas caminhaveis (parede E agua) pra montar a cena");
		if (!achou) { Fechar(); return; }

		_origem = origem;
		_olhar = rumo;
		_rumo = Jandirus.Core.Combat.MeleeArea.Frente(rumo);
		_lado = new Vec2(-_rumo.Y, _rumo.X);
		Nota($"palco em ({origem.X:0},{origem.Y:0}), rumo {rumo}, zoom {ZoomDaCena}x");
		Virar(PMontar);
	}

	// =====================================================================
	// 1) OS QUATRO CORPOS
	// =====================================================================
	private void Montar(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_npc == 0)
		{
			srv.PorNoPontoNaFotoDoBorrao(cli.LocalId, _origem, _olhar);
			(Vec2 meu, _, _, _) = srv.EstadoNaFotoDoBorrao(cli.LocalId);

			// A faixa do MEIO e a do jogador (a camera mora nele); a de cima e a do NPC; a de baixo e a
			// de quem nao arranca. Os deslocamentos sao relativos ao corpo do jogador porque e assim que
			// o `ForjarCorpoDeFoto` recebe (`dono.Pos + desloc`).
			_alvoDoJogador = srv.ForjarParaAFotoDoBorrao(cli.LocalId, _rumo * VaoDoSalto, "AlvoJ", 5_000_000);
			_npc = srv.ForjarParaAFotoDoBorrao(cli.LocalId, _lado * -Faixa, "Npc", 5_000_000);
			_alvoDoNpc = srv.ForjarParaAFotoDoBorrao(cli.LocalId,
				_lado * -Faixa + _rumo * VaoDoSalto, "AlvoN", 5_000_000);
			_quieto = srv.ForjarParaAFotoDoBorrao(cli.LocalId, _lado * Faixa, "Quieto", 5_000_000);

			Conferir("forjados", _npc != 0 && _alvoDoNpc != 0 && _alvoDoJogador != 0 && _quieto != 0,
					 "os quatro corpos entraram no mundo pelo `PorNoMundo`");
			if (_npc == 0 || _quieto == 0) { Fechar(); return; }

			srv.ApontarNaFotoDoBorrao(_npc, _olhar);
			srv.ApontarNaFotoDoBorrao(_quieto, _olhar);
			_ = meu;
			_t = 0;
			return;
		}

		// ============================ ESPERA O CORPO SER **DESENHADO**, e nao so existir ============================
		// Um corpo forjado sem aparencia apresentada nasce como marcador invisivel, e a foto sai com o
		// chao e nada mais -- com todas as checagens verdes, porque elas leem o servidor. A `--diagraio`
		// ja pagou essa foto duas vezes.
		// =======================================================================================================
		if (mundo.CorpoDeTeste(_npc) == null || mundo.CorpoDeTeste(_quieto) == null
			|| mundo.CorpoDeTeste(_alvoDoNpc) == null || mundo.CorpoDeTeste(_alvoDoJogador) == null)
		{
			if (_t < 10) return;
			Conferir("desenhados", false, "os quatro corpos tem NODE DESENHADO na tela (sem isto toda foto abaixo e do chao vazio)");
			Fechar();
			return;
		}
		if (_t < 1.0) return;   // um segundo pra o sprite vestir e a interpolacao assentar

		Conferir("desenhados", true, "os quatro corpos tem node desenhado na tela");
		Virar(PA_Controle);
	}

	// =====================================================================
	// A) O CONTROLE: NINGUEM ARRANCA
	// =====================================================================
	/// <summary>
	/// **O CONTRA-EXEMPLO QUE VEM ANTES.** Sem ele, "ha tinta na faixa do NPC" e compativel com "esta
	/// bancada acha tinta em qualquer lugar" -- e uma medida que nunca deu zero nao sabe dar zero.
	/// </summary>
	private void A_Controle(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_t < 0.8) return;

		int copias = GetTree().GetNodesInGroup(RastroDeCorrida.GrupoDoRastro).Count;
		Conferir("controle-nos", copias == 0, $"CONTROLE: com ninguem arrancando nao existe copia de rastro nenhuma "
							  + $"na arvore ({copias} achadas)");

		if (Obturar(mundo, "borrao-A-controle", "A: ninguem arranca -- o CONTROLE") is { } m)
		{
			Conferir("controle-ruido", m.Ruido == 0,
				$"CONTROLE: o chao de ruido do obturador e ZERO ({m.Ruido} px diferentes entre duas fotos "
				+ "iguais) -- com a arvore pausada nada mais se mexe, e por isso a tinta de baixo e rastro");
			Conferir("controle-tinta", m.Total == 0,
				$"CONTROLE: e a tela COM e SEM rastro sao o mesmo pixel a pixel ({m.Total} px de tinta)");
			Virar(PB_Armar);
		}
	}

	// =====================================================================
	// B) O MESMO QUADRO: O NPC E O JOGADOR ARRANCAM JUNTOS
	// =====================================================================
	private void B_Armar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.3) return;

		// TODO MUNDO DE VOLTA NA MARCA. Depois de qualquer salto os corpos ficam a 32 px do alvo, e dali
		// nao ha investida nenhuma (`dist - DistanciaDeParada < DeslocamentoMinimo`).
		Marcar(srv, cli);
		Virar(PB_Saltar);
	}

	/// <summary>Repoe os quatro corpos e as duas marcas -- o estado de partida de qualquer cena de salto.</summary>
	private void Marcar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.PorNoPontoNaFotoDoBorrao(cli.LocalId, _origem, _olhar);
		srv.PorNoPontoNaFotoDoBorrao(_alvoDoJogador, _origem + _rumo * VaoDoSalto, _olhar);
		srv.PorNoPontoNaFotoDoBorrao(_npc, _origem - _lado * Faixa, _olhar);
		srv.PorNoPontoNaFotoDoBorrao(_alvoDoNpc, _origem - _lado * Faixa + _rumo * VaoDoSalto, _olhar);
		srv.PorNoPontoNaFotoDoBorrao(_quieto, _origem + _lado * Faixa, _olhar);

		// ============================ A MARCA E O QUE ESTICA O ARRANQUE ============================
		// Sem ela o pesado busca 160 px e este vao de 300 nao teria alvo nenhum -- nem pro NPC, nem pro
		// jogador. **E ela nao e privilegio de NPC**: a IA marca todo tique pelo `Cerebro` e o jogador
		// marca com o duplo clique, e e SO ISSO que separa os dois alcances do relato 2.
		// ======================================================================================
		srv.MarcarNaFotoDoBorrao(_npc, _alvoDoNpc);
		cli.SendAlvo(_alvoDoJogador);   // o mesmo pacote `C2S.Alvo` do duplo clique
	}

	private float _saltoDoNpc, _saltoDoJogador;
	private bool _apertei;
	private Vec2 _saiuNpc, _saiuJogador;

	/// <summary>
	/// OS DOIS ARRANCAM NO MESMO QUADRO -- e e por isso que existe UMA foto e nao duas.
	///
	/// O NPC sai pelo <see cref="Jandirus.Server.GameServer.SaltarNaFotoDoBorrao"/>, que e o `Atacar`; o
	/// jogador sai pela TECLA. `Input.ActionPress` e o caminho do teclado de verdade
	/// (<see cref="LocalPlayer"/> le `run` na linha 619 e `attack` na 952, no mesmo quadro), e ele
	/// importa: e o `LerAcoes` que escreve o `_deOndeSai`, a origem que o corpo LOCAL usa pro rastro.
	/// Mandar `SendAction` direto pularia essa linha e o borrao do jogador nasceria de uma posicao velha
	/// -- que e literalmente um dos defeitos que este sistema ja teve.
	/// </summary>
	/// <summary>
	/// AS DUAS TECLAS E O ARRANQUE DO NPC, NO MESMO QUADRO -- e nada mais.
	///
	/// **A PRIMEIRA VERSAO ESPERAVA AQUI, e isso matava a cena.** Ela segurava ate dois segundos pelo
	/// `DashLivreEm` do jogador chegar pelo fio; o rastro dura 0,13 s. Quando a espera acabava, o borrao
	/// do NPC ja tinha esmaecido havia mais de um segundo e a bancada media zero copia -- com o jogo
	/// certo. Quem pode esperar e a cena SEGUINTE, que espera pelo RASTRO e nao pelo relogio.
	/// </summary>
	/// <summary>
	/// ============================ O NPC SALTA UM PISCAR DEPOIS DA TECLA, E ISSO E O CONTRARIO DE TRAPACA ============================
	/// Os dois caminhos tem atrasos diferentes, e nao ha como nao terem: o salto do NPC e decidido DENTRO
	/// deste processo (o `Atacar` roda na hora), enquanto o do jogador da a volta -- pacote de tecla ate o
	/// servidor, `Correction` de volta, e so entao o corpo local se move e o `RastroDeCorrida` resolve o
	/// pedido pendente.
	///
	/// Disparados no mesmo quadro, o rastro do NPC nasce uns quatro quadros antes. A primeira rodada
	/// fotografou exatamente isso: 6 copias do NPC contra 10 do jogador, e a faixa dele com 138 px de
	/// tinta contra 268 -- **o rastro do NPC ja estava se recolhendo**, que e o que ele foi feito pra
	/// fazer (a ponta velha morre primeiro, `VidaDoArranque * Lerp(0.55, 1, t)`).
	///
	/// Uma foto assim compara um rastro NOVO com um rastro VELHO e chama a diferenca de "o NPC borra
	/// menos". Este atraso alinha as IDADES, que e a unica forma de a foto comparar o EFEITO.
	/// ============================================================================================================================
	/// </summary>
	private const double AtrasoDoNpc = 0.03;


	private void B_Saltar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		if (!_apertei)
		{
			_apertei = true;
			Godot.Input.ActionPress("run");      // SHIFT: o golpe vira pesado, e a investida vira longa
			Godot.Input.ActionPress("attack");   // ESPACO
			return;
		}
		if (_t < 0.5 + AtrasoDoNpc) return;

		srv.SaltarNaFotoDoBorrao(_npc);

		// PELO CARIMBO DO ANUNCIO, e nao pela posicao de agora -- e pela MESMA porta que o jogador. Ler
		// os dois lados por portas diferentes e como se produz "dois alcances" onde ha um: a diferenca
		// sai do METODO de medir e nao do jogo, e e exatamente esse o erro que esta bancada investiga.
		(bool houve, Vec2 sn, Vec2 pn, float quanto) = srv.SaltoAnunciadoNaFotoDoBorrao(_npc);
		_saltoDoNpc = houve ? quanto : 0;
		_saiuNpc = sn;
		Nota($"o NPC saltou {_saltoDoNpc:0} px, de ({sn.X:0},{sn.Y:0}) a ({pn.X:0},{pn.Y:0})");
		Virar(PB_Medir);
	}

	private void B_Medir(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		// SOLTA AS TECLAS no quadro seguinte ao aperto: `IsActionJustPressed` so dispara na borda, e uma
		// tecla segurada pra sempre faria o corpo socar de novo no meio da medicao.
		Godot.Input.ActionRelease("attack");
		Godot.Input.ActionRelease("run");

		// ============================ ESPERA AS DUAS FAIXAS TEREM COPIA, E SO ENTAO FOTOGRAFA ============================
		// O rastro do arranque nao e desenhado no quadro do pacote: o `RastroDeCorrida` guarda o pedido e
		// so o resolve quando o corpo JA ESTA no destino -- e a chegada vem por outro canal (a `Correction`
		// pro corpo local, o snapshot pro remoto), sem ordem garantida. Fotografar no primeiro quadro
		// depois do salto pegaria a tela antes de qualquer copia existir, e a bancada leria "o borrao nao
		// saiu" com o borrao a caminho.
		//
		// E a espera olha as DUAS faixas: e a unica forma de a foto poder dizer "no mesmo quadro".
		// =============================================================================================================
		// ============================ O SALTO DO JOGADOR NAO SE LE NA POSICAO -- ELE SE LE NO CARIMBO ============================
		// Duas armadilhas opostas, e as duas foram medidas aqui antes de esta linha existir:
		//
		//   CEDO DEMAIS -- ler no quadro da tecla pega o `SaiuDe` de fabrica (zero), e o "salto" vira a
		//   distancia da origem do mundo ate o palco. A primeira rodada relatou 11.133 px.
		//
		//   TARDE DEMAIS -- ler a POSICAO dois ou tres quadros depois ja pega o corpo RECONCILIADO: o
		//   cliente ainda estava mandando input da posicao velha quando a `Correction` saiu, e o servidor
		//   acomoda alguns pixels disso dentro do `OrcamentoPx`. Duas rodadas relataram 259 e 257 px onde
		//   a regra manda 268 -- **e o salto tinha sido de 268 nas duas**. Com o alvo medido a exatamente
		//   300 px da partida, nao sobrava outra explicacao.
		//
		// E o corpo do NPC nao sofre disso, porque nao ha cliente do outro lado dele. Ou seja: uma
		// bancada que lesse os dois pela posicao acusaria "o NPC salta mais que o jogador" -- **o relato
		// do dono, nascido do metodo de medir**. Por isso os dois lados saem do MESMO carimbo, escrito no
		// instante do anuncio (`_saltoDeCadaCorpo`, em `AnunciarZanzo`), quando `Pos` ainda e o destino.
		// ====================================================================================================================
		if (_saltoDoJogador == 0)
		{
			(bool houve, Vec2 sj, Vec2 pj, float quanto) = srv.SaltoAnunciadoNaFotoDoBorrao(cli.LocalId);
			if (houve && quanto > 20f)
			{
				_saltoDoJogador = quanto;
				_saiuJogador = sj;
				(Vec2 agora, _, _, _) = srv.EstadoNaFotoDoBorrao(cli.LocalId);
				(Vec2 pa, _, _, _) = srv.EstadoNaFotoDoBorrao(_alvoDoJogador);
				Nota($"o JOGADOR saltou {quanto:0} px, de ({sj.X:0},{sj.Y:0}) a ({pj.X:0},{pj.Y:0})"
					 + $" -- alvo em ({pa.X:0},{pa.Y:0}), a {(pa - sj).Length:0} px da partida; o corpo ja"
					 + $" esta em ({agora.X:0},{agora.Y:0}), {(agora - pj).Length:0} px atras (reconciliacao)");
			}
		}

		(int naFaixaDoNpc, int naFaixaDoJogador, int naFaixaDoQuieto) = CopiasPorFaixa(mundo);

		// ============================ DISPARA NO PRIMEIRO QUADRO EM QUE AS DUAS TEM RASTRO -- E SO ISSO ============================
		// **A JANELA INTEIRA E DE 0,13 s**, e essa e a restricao que manda em tudo aqui. Duas tentativas
		// de ser mais exigente ja custaram a cena, e as duas pareciam prudentes na hora de escrever:
		//
		//   "espera as duas terem metade das copias" -- as duas nunca estao cheias ao mesmo tempo (o
		//   salto do NPC e decidido dentro deste processo e o do jogador da a volta pelo fio), entao a
		//   condicao vencia por prazo e a foto saia com ZERO px de tinta nas tres faixas;
		//
		//   "da 0,25 s de folga antes de fotografar" -- 0,25 s e o DOBRO da vida do rastro. Garantia de
		//   fotografar a tela vazia.
		//
		// Entao a foto e do primeiro instante em que as duas faixas tem rastro, e o desequilibrio de
		// idade entre elas fica REGISTRADO em vez de evitado: o relatorio traz a contagem de copias ao
		// lado da tinta, e a comparacao entre os dois lados e feita POR COPIA (ver mais abaixo), que e o
		// numero que nao depende de quantas sobreviveram.
		// ======================================================================================================================
		if (Mathf.Min(naFaixaDoNpc, naFaixaDoJogador) < 1 && _t < 0.9 && _obtFase == 0) return;

		Conferir("b-npc-nos", naFaixaDoNpc > 0,
			$"O NPC BORRA: ha {naFaixaDoNpc} copia(s) de rastro na faixa dele -- e o relato 1 do dono");
		Conferir("b-jog-nos", naFaixaDoJogador > 0,
			$"...e o JOGADOR tambem, no MESMO quadro ({naFaixaDoJogador} copia(s)) -- e o "
			+ "\"igual os jogadores\" do relato");
		Conferir("b-quieto-nos", naFaixaDoQuieto == 0,
			$"...e QUEM NAO ARRANCOU nao tem nenhuma ({naFaixaDoQuieto}) -- o contra-exemplo mora "
			+ "DENTRO da mesma foto");

		if (Obturar(mundo, "borrao-B-mesmo-quadro",
					$"B: NPC (em cima) e JOGADOR (no meio) arrancando juntos; parado embaixo") is not { } m)
			return;

		Conferir("b-ruido", m.Ruido == 0, $"o chao de ruido continua ZERO ({m.Ruido} px)");

		GD.Print($"[borrao-foto] tinta: NPC {m.Npc} px | jogador {m.Jogador} px | parado {m.Quieto} px "
				 + $"| fora das faixas {m.Total - m.Npc - m.Jogador - m.Quieto} px");
		_linhas.Add($"  tinta  NPC {m.Npc} px | JOGADOR {m.Jogador} px | PARADO {m.Quieto} px "
					+ $"| ruido {m.Ruido} px");

		Conferir("b-npc-tinta", m.Npc > 200,
			$"EM PIXEL: a faixa do NPC ganhou {m.Npc} px de tinta que nao existiam sem o rastro");
		Conferir("b-jog-tinta", m.Jogador > 200,
			$"EM PIXEL: a faixa do JOGADOR ganhou {m.Jogador} px");
		Conferir("b-quieto-tinta", m.Quieto == 0,
			$"EM PIXEL: a faixa do PARADO nao ganhou NADA ({m.Quieto} px) -- borrao e do DESLOCAMENTO");

		// ============================ E OS DOIS BORROES SAO O MESMO EFEITO, e nao um parecido ============================
		// Mesmo salto, mesmo corpo, mesma receita: a tinta dos dois tem que ficar na mesma ordem de
		// grandeza. Uma faixa com dez vezes a tinta da outra seria "o NPC borra, mas de outro jeito" --
		// que e o relato do dono com outra roupa.
		// =============================================================================================================
		// ============================ A COMPARACAO E POR COPIA, E NAO POR FAIXA ============================
		// A tinta TOTAL de uma faixa depende de quantas copias ainda estavam vivas no instante do
		// obturador, e isso e latencia do fio -- nao e propriedade do efeito. Medido nos dois sentidos:
		// 6 x 10 copias numa rodada e 10 x 2 na seguinte, o que dava razoes de tinta de 0,53 e 5,36 pro
		// **mesmo** efeito rodando certo nas duas.
		//
		// O que nao depende da idade e a tinta POR COPIA: cada copia e uma silhueta do mesmo corpo,
		// borrada pela mesma receita. Se as duas faixas gastam a mesma tinta por copia, e o mesmo efeito
		// -- e essa e a afirmacao que o dono fez ("igual os jogadores").
		// ==============================================================================================
		double porCopiaNpc = naFaixaDoNpc == 0 ? 0 : (double)m.Npc / naFaixaDoNpc;
		double porCopiaJog = naFaixaDoJogador == 0 ? 0 : (double)m.Jogador / naFaixaDoJogador;
		double razao = porCopiaJog == 0 ? 0 : porCopiaNpc / porCopiaJog;
		_linhas.Add($"  tinta  por copia: NPC {porCopiaNpc:0} px/copia ({naFaixaDoNpc} copias) | "
					+ $"JOGADOR {porCopiaJog:0} px/copia ({naFaixaDoJogador} copias)");
		Conferir("b-razao", razao is > 0.5 and < 2.0,
			$"...e cada copia dos dois gasta a MESMA tinta (NPC {porCopiaNpc:0} px/copia x jogador "
			+ $"{porCopiaJog:0} px/copia, razao {razao:0.00}) -- e o MESMO efeito, e nao um parecido");

		// ============================ O SALTO, MEDIDO NOS DOIS, NO MESMO VAO ============================
		// O relato 2 em pixel de tela: com o mesmo vao e a mesma tecla, os dois andam o mesmo tanto. A
		// familia F da `--borraoteste` mede isto no servidor; aqui ele e conferido na cena que a foto
		// mostra, e contra a REGRA (`vao - DistanciaDeParada`).
		// ==========================================================================================
		float esperado = VaoDoSalto - 32f;
		Conferir("b-alcance-igual", Mathf.Abs(_saltoDoNpc - _saltoDoJogador) < 1f,
			$"O ALCANCE E O MESMO: NPC {_saltoDoNpc:0} px, jogador {_saltoDoJogador:0} px, no MESMO vao");
		Conferir("b-alcance-regra", Mathf.Abs(_saltoDoNpc - esperado) < 1f,
			$"...e ele e o da REGRA ({esperado:0} px = vao - `DistanciaDeParada`), e nao dois erros iguais");

		// ============================ O RASTRO CONTA O TRAJETO, e nao um sopro no corpo ============================
		// **Esta e a metade do relato 2 que a foto responde.** Um salto de 268 px sem rastro e um corpo
		// que simplesmente aparece, e teleporte sem rastro le como mais longe do que o mesmo salto com
		// rastro. Pra o borrao explicar o trajeto ele precisa OCUPAR o trajeto -- a tinta tem que se
		// estender da origem ate a chegada, e nao morar num tufo em volta do boneco.
		// =====================================================================================================
		Conferir("b-trajeto", m.ComprimentoDoNpc > _saltoDoNpc * 0.5f,
			$"o rastro do NPC ATRAVESSA o trajeto: a tinta se estende por {m.ComprimentoDoNpc:0} px de "
			+ $"mundo, num salto de {_saltoDoNpc:0} px (rastro nao e um sopro em volta do corpo)");

		Virar(PD_Possuir);
	}

	// =====================================================================
	// D) A FERA: O CORPO POSSUIDO
	// =====================================================================
	/// <summary>
	/// O corpo do JOGADOR passa a ser dirigido pelo servidor -- e a fera do Oozaru e a furia lendaria,
	/// pela porta unica (`AssumirOCorpo`).
	///
	/// **E ELE E O UNICO CASO EM QUE A ORIGEM DO RASTRO VEM DO PACOTE.** Com as redeas, o corpo local
	/// usa o `_deOndeSai` que ele mesmo guardou na tecla; sem elas o `LerAcoes` nao roda e esse campo
	/// ficaria parado no ultimo soco do dono -- possivelmente noutra zona, e o rastro nasceria como uma
	/// faixa atravessando o mapa. Ver `LocalPlayer.OrigemDoSalto`.
	/// </summary>
	private void D_Possuir(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		Marcar(srv, cli);
		srv.PossuirNaFotoDoBorrao(cli.LocalId, true);

		(_, _, _, bool possuido) = srv.EstadoNaFotoDoBorrao(cli.LocalId);
		Conferir("d-posse", possuido, "o corpo do jogador esta POSSUIDO (`AssumirOCorpo` -- a porta unica do Oozaru "
						   + "e da furia lendaria)");

		srv.SaltarNaFotoDoBorrao(cli.LocalId);
		(bool houveF, Vec2 sf, Vec2 pf, float quantoF) = srv.SaltoAnunciadoNaFotoDoBorrao(cli.LocalId);
		_saltoDaFera = houveF ? quantoF : 0;
		_saiuFera = sf;
		Nota($"a FERA saltou {_saltoDaFera:0} px, de ({sf.X:0},{sf.Y:0}) a ({pf.X:0},{pf.Y:0})");
		_ = pf;
		Virar(PD_Medir);
	}

	private float _saltoDaFera;
	private Vec2 _saiuFera;

	private void D_Medir(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		(_, int naFaixaDoJogador, _) = CopiasPorFaixa(mundo);
		if (naFaixaDoJogador == 0 && _t < 1.5 && _obtFase == 0) return;

		Conferir("d-nos", naFaixaDoJogador > 0,
			$"A FERA BORRA: o corpo possuido deixou {naFaixaDoJogador} copia(s) de rastro");

		if (Obturar(mundo, "borrao-D-fera", "D: o corpo POSSUIDO (Oozaru / furia lendaria) arrancando") is not { } m)
			return;

		Conferir("d-tinta", m.Jogador > 200, $"EM PIXEL: a faixa da fera ganhou {m.Jogador} px de tinta");
		_linhas.Add($"  tinta  FERA {m.Jogador} px | ruido {m.Ruido} px");

		// ============================ A ORIGEM VEIO DO SERVIDOR, E DA PRA PROVAR ============================
		// Se o corpo local tivesse usado o `_deOndeSai` (que ninguem escreveu nesta cena -- o `LerAcoes`
		// nao roda sem redeas), o rastro comecaria no ultimo lugar em que a TECLA foi apertada: a origem
		// do salto do jogador na cena B, a uma faixa e um salto daqui. A ponta velha da tinta responde
		// isso sem perguntar a ninguem.
		// ================================================================================================
		float daOrigemCerta = (m.PontaVelha - new Vector2(_saiuFera.X, _saiuFera.Y)).Length();
		Conferir("d-origem", daOrigemCerta < 64f,
			$"...e o rastro comeca na origem que veio do SERVIDOR ({daOrigemCerta:0} px dela) -- sem as "
			+ "redeas o `_deOndeSai` do cliente nunca foi escrito");

		srv.PossuirNaFotoDoBorrao(cli.LocalId, false);
		Virar(PF_Quebrar);
	}

	// =====================================================================
	// F) O DEFEITO, FOTOGRAFADO -- E O "ANTES" DO DONO
	// =====================================================================
	/// <summary>
	/// ============================ UMA BANCADA DE FOTO QUE NUNCA FOI VISTA VAZIA NAO SABE FICAR VAZIA ============================
	/// As cenas A a E acham rastro e ficam verdes. **Verde e barato.** Se esta medicao de pixel
	/// respondesse "tem tinta" pra qualquer tela, as cinco cenas seriam decorativas -- e a cena A (o
	/// controle) so descarta o caso de a bancada achar tinta com NINGUEM saltando, e nao o caso de ela
	/// achar tinta com alguem saltando SEM borrao. Que e exatamente o estado que o dono relatou.
	///
	/// Entao o portao ANTIGO volta (`if (zanzo)` no lugar de `if (investiu)`), o NPC salta de novo, e o
	/// que tem que sair da foto e: **o mesmo salto de 268 px, e zero pixel de rastro.** Depois o portao
	/// e desfeito e o rastro tem que voltar -- mede, estraga, mede, conserta, mede, que e o `Mutacao` da
	/// `--borraoteste` feito com a tela em vez do pacote.
	///
	/// E A FOTO QUE SAI DAQUI E O "ANTES" DO RELATO: o corpo aparecendo no destino sem nada explicando o
	/// trajeto. Posta ao lado da `borrao-B-mesmo-quadro-tinta.png`, ela e a diferenca inteira.
	/// ========================================================================================================================
	/// </summary>
	private void F_Quebrar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		Marcar(srv, cli);
		srv.PortaoAntigoNaFotoDoBorrao(true);
		_saltoComDefeito = srv.SaltarNaFotoDoBorrao(_npc);
		Nota($"DEFEITO INJETADO (o portao antigo, `if (zanzo)`): o NPC saltou {_saltoComDefeito:0} px");
		Virar(PF_Medir);
	}

	private float _saltoComDefeito, _saltoDepoisDoConserto;

	private void F_Medir(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		// MEIO SEGUNDO E DE PROPOSITO MAIS DO QUE O RASTRO DURA: aqui a espera nao pode perder nada,
		// porque a afirmacao e que NAO HA nada. Fotografar cedo demais provaria "ainda nao chegou".
		if (_t < 0.5) return;

		(int naFaixaDoNpc, _, _) = CopiasPorFaixa(mundo);
		Conferir("f-salto", Mathf.Abs(_saltoComDefeito - _saltoDoNpc) < 1f,
			$"DEFEITO INJETADO: o SALTO continua o mesmo ({_saltoComDefeito:0} px) -- o defeito nao e no "
			+ "movimento, e no que o jogador VE dele");
		Conferir("f-nos", naFaixaDoNpc == 0,
			$"...e o rastro SUMIU: {naFaixaDoNpc} copia(s) na faixa do NPC. **Esta e a tela que o dono "
			+ "descreveu** -- o corpo aparece no destino e nada conta o trajeto");

		if (Obturar(mundo, "borrao-F-defeito",
					"F: o DEFEITO injetado (portao antigo) -- o mesmo salto, e nenhum rastro") is not { } m)
			return;

		Conferir("f-tinta", m.Npc == 0,
			$"EM PIXEL: a faixa do NPC ganhou {m.Npc} px de tinta com o defeito de pe (a mesma medicao "
			+ "que achou 31 mil px na cena B)");

		// ---- E DESFEITO O DEFEITO, O RASTRO VOLTA ----
		// O `Marcar` REPOE OS CORPOS antes do segundo salto, e ele nao e cerimonia: depois do salto com
		// defeito o NPC esta a 32 px do alvo, e dali `dist - DistanciaDeParada` fica abaixo do
		// `DeslocamentoMinimo` -- nao ha investida nenhuma pra fazer. Sem esta linha o terceiro passo do
		// `Mutacao` reprovava com "0 copias, salto de 0 px", e a leitura obvia ("o conserto nao voltou")
		// era falsa: o corpo simplesmente nao tinha pra onde saltar.
		Marcar(srv, cli);
		srv.PortaoAntigoNaFotoDoBorrao(false);
		_saltoDepoisDoConserto = srv.SaltarNaFotoDoBorrao(_npc);
		Virar(PF_Voltou);
	}

	private void F_Voltou(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		(int naFaixaDoNpc, _, _) = CopiasPorFaixa(mundo);
		if (naFaixaDoNpc == 0 && _t < 0.9) return;

		Conferir("f-voltou", naFaixaDoNpc > 0,
			$"...e desfeito o defeito o rastro VOLTA ({naFaixaDoNpc} copia(s), salto de "
			+ $"{_saltoDepoisDoConserto:0} px) -- era a causa, e nao um estrago que ficou");
		Virar(PE_Esperar);
	}

	// =====================================================================
	// E) O OUTRO JOGADOR
	// =====================================================================
	/// <summary>Quem piscou por ultimo, e se veio com miragem -- o gancho do `S2C.Zanzo` chegando pelo fio.</summary>
	private void AoPiscarAlguem(int quem, Vec2 de, bool vulto)
	{
		_ultimoQuePiscou = quem;
		_origemDoOutro = de;
		_vultoDoOutro = vulto;
	}

	private int _ultimoQuePiscou;
	private Vec2 _origemDoOutro;
	private bool _vultoDoOutro;

	/// <summary>
	/// ============================ O UNICO PEDACO QUE PRECISA DE DOIS PROCESSOS ============================
	/// Cenas A a D provam o ramo REMOTO do `World.AoPiscar` (o NPC) e o ramo LOCAL (o jogador e a fera).
	/// **Nao ha um terceiro ramo** -- `Corpo(quem)` devolve corpo local, remoto e de NPC pela mesma linha.
	/// Mas "o pacote atravessa o fio ate outra pessoa" e uma afirmacao sobre o FIO, e num processo so os
	/// dois lados sao a mesma memoria: e o mesmo motivo pelo qual a `testar-vista.bat` sobe duas janelas.
	///
	/// Entao a `ver-o-borrao.bat` sobe um segundo cliente com `--socar --socaralvo Olheiro`, que marca
	/// este corpo e vem batendo -- e o arranque dele chega aqui como `S2C.Zanzo` de um id que nao e o meu
	/// nem nenhum dos que esta bancada forjou. Sem o segundo processo esta cena diz SEM COBERTURA e nao
	/// reprova nada: uma bancada que falha por falta de vizinho vira ruido.
	/// ==================================================================================================
	/// </summary>
	private void E_Esperar(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		bool ehOutro = _ultimoQuePiscou != 0 && _ultimoQuePiscou != cli.LocalId
					&& _ultimoQuePiscou != _npc && _ultimoQuePiscou != _quieto
					&& _ultimoQuePiscou != _alvoDoNpc && _ultimoQuePiscou != _alvoDoJogador;

		if (!ehOutro)
		{
			// ============================ A ISCA TEM QUE ANDAR, SENAO SO HA UM SALTO ============================
			// O `--socar` chega, marca o `Quieto`, arranca uma vez e para a 32 px dele -- dali nao ha
			// investida nenhuma (`dist - DistanciaDeParada < DeslocamentoMinimo`) e o segundo processo
			// nunca mais borra. Um unico salto, no instante errado, e cena nenhuma.
			//
			// Entao a isca vai e volta na faixa dela a cada 2,5 s: cada ida reabre um vao de 300 px e o
			// visitante o fecha com um arranque -- dentro do quadro, e quantas vezes forem precisas ate
			// o obturador pegar um. Quem se move e o corpo forjado, nao o fotografo: mexer na camera
			// entre as fotos nao muda a medida (a arvore esta pausada), mas mexeria no ENQUADRAMENTO.
			// ================================================================================================
			_iscaRelogio -= (float)GetProcessDeltaTime();
			if (_iscaRelogio <= 0)
			{
				_iscaRelogio = 2.5f;
				_iscaLonge = !_iscaLonge;
				srv.PorNoPontoNaFotoDoBorrao(_quieto,
					_origem + _lado * Faixa + (_iscaLonge ? _rumo * VaoDoSalto : Vec2.Zero), _olhar);
			}

			if (_t < EsperaPeloOutro) return;
			Nota($"CENA E SEM COBERTURA: nenhum outro jogador arrancou em {EsperaPeloOutro:0} s. "
				 + "Ela precisa do segundo processo (`--socar --socaralvo <nome do host>`); "
				 + "sozinha, a bancada nao tem como fabricar um vizinho com `Peer` proprio.");
			Virar(PFim);
			return;
		}

		int copias = GetTree().GetNodesInGroup(RastroDeCorrida.GrupoDoRastro).Count;
		if (copias == 0 && _t < EsperaPeloOutro && _obtFase == 0) return;

		Conferir("e-nos", copias > 0,
			$"O BORRAO ALHEIO VIAJA: o arranque do jogador id {_ultimoQuePiscou} chegou pelo fio "
			+ $"(`S2C.Zanzo`, origem ({_origemDoOutro.X:0},{_origemDoOutro.Y:0}), vulto={_vultoDoOutro}) "
			+ $"e virou {copias} copia(s) de rastro nesta tela");

		if (Obturar(mundo, "borrao-E-outro-jogador",
					$"E: o borrao de OUTRO JOGADOR (id {_ultimoQuePiscou}), vindo pelo fio") is { } m)
		{
			_linhas.Add($"  tinta  OUTRO JOGADOR {m.Total} px | ruido {m.Ruido} px");
			Conferir("e-tinta", m.Total > 200, $"EM PIXEL: o borrao alheio pintou {m.Total} px desta tela");
			Virar(PFim);
		}
	}

	/// <summary>Quanto se espera pelo segundo processo. Curto: se ele nao subiu, ele nao vai subir.</summary>
	private const double EsperaPeloOutro = 45;

	private float _iscaRelogio;
	private bool _iscaLonge;

	// =====================================================================
	// AS COPIAS, POR FAIXA
	// =====================================================================
	/// <summary>
	/// QUANTAS COPIAS DE RASTRO CAEM EM CADA FAIXA -- pelo grupo, e pela posicao DESENHADA delas.
	///
	/// Nao substitui a medida de pixel (uma copia pode existir e nao pintar nada -- invisivel, alfa zero,
	/// fora da tela), mas responde uma pergunta que a medida de pixel nao responde sozinha: **de quem** e
	/// aquela tinta. E e ela que diz quando fotografar, porque a foto so vale se as duas faixas ja
	/// tiverem rastro.
	/// </summary>
	private (int Npc, int Jogador, int Quieto) CopiasPorFaixa(World mundo)
	{
		int n = 0, j = 0, q = 0;
		var eixoNpc = new Vector2(_origem.X - _lado.X * Faixa, _origem.Y - _lado.Y * Faixa);
		var eixoJog = new Vector2(_origem.X, _origem.Y);
		var eixoQui = new Vector2(_origem.X + _lado.X * Faixa, _origem.Y + _lado.Y * Faixa);
		var perp = new Vector2(_lado.X, _lado.Y);

		foreach (Node no in GetTree().GetNodesInGroup(RastroDeCorrida.GrupoDoRastro))
		{
			if (no is not Node2D n2 || !IsInstanceValid(n2)) continue;
			Vector2 p = n2.GlobalPosition;
			// A DISTANCIA E PERPENDICULAR ao rumo: ao longo da faixa a copia pode estar em qualquer
			// ponto (e esse e o assunto), mas ATRAVESSADA ela so pode estar na sua.
			float dn = Mathf.Abs((p - eixoNpc).Dot(perp));
			float dj = Mathf.Abs((p - eixoJog).Dot(perp));
			float dq = Mathf.Abs((p - eixoQui).Dot(perp));
			float menor = Mathf.Min(dn, Mathf.Min(dj, dq));
			if (menor > Faixa * 0.5f) continue;   // longe das tres: nao e desta cena
			if (menor == dn) n++;
			else if (menor == dj) j++;
			else q++;
		}
		return (n, j, q);
	}

	// =====================================================================
	// O OBTURADOR: QUATRO FOTOS COM A ARVORE PAUSADA
	// =====================================================================
	private sealed record Medida(int Total, int Npc, int Jogador, int Quieto, int Ruido,
								 float ComprimentoDoNpc, Vector2 PontaVelha);

	private int _obtFase, _obtQuadros;
	private Image? _obtCom, _obtSem;
	private readonly List<Node2D> _obtCopias = [];
	private Transform2D _obtCamera = Transform2D.Identity;

	/// <summary>
	/// ============================ COM, SEM, COM, SEM -- E A ARVORE PARADA ============================
	/// Devolve nulo enquanto nao terminou (ele consome varios quadros) e a <see cref="Medida"/> no fim.
	///
	/// DOIS QUADROS DE FOLGA A CADA TROCA: `GetImage` devolve o ULTIMO quadro RENDERIZADO, e um pedido
	/// feito no mesmo quadro em que a visibilidade mudou fotografa o estado de ANTES dela. A `--diagboca`
	/// registra esse tombo, e ele custa uma foto silenciosamente errada.
	///
	/// A SEGUNDA FOTO "SEM" NAO E SOBRA: ela e o CHAO DE RUIDO. `sem` contra `sem2` sao duas fotos da
	/// mesma coisa, e a diferenca entre elas e o quanto esta bancada mede sem ter nada pra medir. Sem
	/// esse numero, "a faixa do NPC ganhou 3000 px" nao se distingue de "esta tela toda cintila".
	/// ==============================================================================================
	/// </summary>
	private Medida? Obturar(World mundo, string arquivo, string rotulo)
	{
		switch (_obtFase)
		{
			case 0:
				_obtCopias.Clear();
				foreach (Node no in GetTree().GetNodesInGroup(RastroDeCorrida.GrupoDoRastro))
					if (no is Node2D n2 && IsInstanceValid(n2)) _obtCopias.Add(n2);

				// A CAMERA E GUARDADA AQUI, com a arvore ainda viva: e ela que converte mundo em pixel
				// nas contas abaixo, e depois da pausa ela nao muda mais.
				_obtCamera = GetViewport()?.CanvasTransform ?? Transform2D.Identity;
				GetTree().Paused = true;
				_obtFase = 1;
				_obtQuadros = 0;
				return null;

			case 1:
				if (_obtQuadros++ < 2) return null;
				_obtCom = Tela();
				Mostrar(false);
				_obtFase = 2;
				_obtQuadros = 0;
				return null;

			case 2:
				if (_obtQuadros++ < 2) return null;
				_obtSem = Tela();
				Mostrar(true);
				_obtFase = 3;
				_obtQuadros = 0;
				return null;

			case 3:
				if (_obtQuadros++ < 2) return null;
				// a terceira foto (com de novo) so existe pra devolver a tela ao estado real antes da
				// quarta; ela nao entra em conta nenhuma
				Mostrar(false);
				_obtFase = 4;
				_obtQuadros = 0;
				return null;

			default:
			{
				if (_obtQuadros++ < 2) return null;
				Image? sem2 = Tela();
				Mostrar(true);
				GetTree().Paused = false;
				_obtFase = 0;

				if (_obtCom == null || _obtSem == null || sem2 == null)
				{
					Nota($"{rotulo}: SEM FOTO (headless nao renderiza -- rode com janela)");
					return new Medida(0, 0, 0, 0, -1, 0, Vector2.Zero);
				}

				Medida m = Comparar(_obtCom, _obtSem, sem2, arquivo, rotulo);
				_obtCom = _obtSem = null;
				return m;
			}
		}
	}

	private void Mostrar(bool sim)
	{
		foreach (Node2D n in _obtCopias) if (IsInstanceValid(n)) n.Visible = sim;
	}

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>
	/// Quanto um canal precisa mudar pra a bancada chamar de tinta.
	///
	/// **BAIXO DE PROPOSITO.** A copia mais fraca do rastro sai com alfa 0,10 (`AlfaDoArranque` 0,34 x a
	/// rampa 0,3), e sobre um chao de tom parecido isso e uma mudanca de poucos niveis. Cortar alto
	/// jogaria fora justamente a ponta velha do rastro -- que e a que prova que ele atravessa o trajeto.
	/// O que autoriza um corte baixo e o chao de ruido ser ZERO: com a arvore pausada nada mais muda um
	/// unico nivel, entao nao ha o que confundir com tinta fraca.
	/// </summary>
	private const int Limiar = 6;

	private Medida Comparar(Image com, Image sem, Image sem2, string arquivo, string rotulo)
	{
		int w = com.GetWidth(), h = com.GetHeight();
		var mascara = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		mascara.Fill(new Color(0, 0, 0, 1));

		// AS TRES FAIXAS EM PIXEL DE TELA. A conta vai pelo eixo perpendicular, como a das copias.
		Vector2 pNpc = NaImagem(com, _origem - _lado * Faixa);
		Vector2 pJog = NaImagem(com, _origem);
		Vector2 pQui = NaImagem(com, _origem + _lado * Faixa);
		Vector2 perp = (NaImagem(com, _origem + _lado * 64f) - pJog).Normalized();
		Vector2 aoLongo = (NaImagem(com, _origem + _rumo * 64f) - pJog).Normalized();
		float meiaFaixa = (pJog - pNpc).Length() * 0.5f;

		int total = 0, nNpc = 0, nJog = 0, nQui = 0, ruido = 0;
		float minAoLongo = float.MaxValue, maxAoLongo = float.MinValue;
		Vector2 pontaVelha = Vector2.Zero;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color a = com.GetPixel(x, y), b = sem.GetPixel(x, y), c = sem2.GetPixel(x, y);
				if (Dif(b, c) > Limiar) ruido++;
				if (Dif(a, b) <= Limiar) continue;

				total++;
				mascara.SetPixel(x, y, new Color(1f, 0.4f, 0.1f, 1f));

				var p = new Vector2(x, y);
				float dn = Mathf.Abs((p - pNpc).Dot(perp));
				float dj = Mathf.Abs((p - pJog).Dot(perp));
				float dq = Mathf.Abs((p - pQui).Dot(perp));
				float menor = Mathf.Min(dn, Mathf.Min(dj, dq));
				if (menor > meiaFaixa) continue;

				if (menor == dn)
				{
					nNpc++;
					float t = (p - pNpc).Dot(aoLongo);
					if (t < minAoLongo) { minAoLongo = t; pontaVelha = p; }
					if (t > maxAoLongo) maxAoLongo = t;
				}
				else if (menor == dj)
				{
					nJog++;
					// A CENA D nao tem faixa de NPC: la o sujeito e o corpo do jogador, e a ponta velha
					// tem que sair DELE. Uma segunda varredura so pra isso seria uma copia da mesma
					// conta -- e a `PontaVelha` e usada por uma cena de cada vez.
					if (nNpc == 0)
					{
						float t = (p - pJog).Dot(aoLongo);
						if (t < minAoLongo) { minAoLongo = t; pontaVelha = p; }
						if (t > maxAoLongo) maxAoLongo = t;
					}
				}
				else nQui++;
			}

		// O COMPRIMENTO VOLTA PRA MUNDO: a legenda e as afirmacoes falam em px de mundo (a mesma moeda
		// do salto), e a tela esta com zoom.
		float escala = (NaImagem(com, _origem + _rumo * 64f) - pJog).Length() / 64f;
		float comprimento = maxAoLongo > minAoLongo && escala > 0 ? (maxAoLongo - minAoLongo) / escala : 0;
		Vector2 pontaNoMundo = _obtCamera.AffineInverse() * (pontaVelha / EscalaDaImagem(com));

		Gravar(com, arquivo + ".png", rotulo);
		Gravar(mascara, arquivo + "-tinta.png", rotulo + " (so o rastro)");
		Gravar(Tira(sem, com, mascara), arquivo + "-tira.png", rotulo + " (sem / com / so o rastro)");

		return new Medida(total, nNpc, nJog, nQui, ruido, comprimento, pontaNoMundo);
	}

	private static int Dif(Color a, Color b) => (int)(255 * Mathf.Max(Mathf.Abs(a.R - b.R),
		Mathf.Max(Mathf.Abs(a.G - b.G), Mathf.Abs(a.B - b.B))));

	/// <summary>
	/// De mundo pra PIXEL DA IMAGEM. Sao duas conversoes e nao uma: a `CanvasTransform` leva mundo a
	/// pixel de VIEWPORT, e a imagem pode ter outro tamanho (escala da janela). Misturar as duas foi o
	/// tombo que a `--diagboca` registra com o nome de `EscalaDaImagem`.
	/// </summary>
	private Vector2 NaImagem(Image img, Vec2 mundo)
		=> _obtCamera * new Vector2(mundo.X, mundo.Y) * EscalaDaImagem(img);

	private float EscalaDaImagem(Image img)
	{
		Vector2 tela = GetViewport()?.GetVisibleRect().Size ?? new Vector2(img.GetWidth(), img.GetHeight());
		return tela.X <= 0 ? 1f : img.GetWidth() / tela.X;
	}

	/// <summary>A tira: SEM rastro, COM rastro, e so o rastro. Tres quadros que so juntos contam a historia.</summary>
	private static Image Tira(Image sem, Image com, Image mascara)
	{
		const int Vao = 8;
		int w = com.GetWidth(), h = com.GetHeight();
		Image t = Image.CreateEmpty(w * 3 + Vao * 2, h, false, Image.Format.Rgba8);
		t.Fill(new Color(0.06f, 0.06f, 0.06f));
		var r = new Rect2I(Vector2I.Zero, new Vector2I(w, h));
		t.BlitRect(sem, r, Vector2I.Zero);
		t.BlitRect(com, r, new Vector2I(w + Vao, 0));
		t.BlitRect(mascara, r, new Vector2I(2 * (w + Vao), 0));
		return t;
	}

	private void Gravar(Image img, string arquivo, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + arquivo);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}");
			_linhas.Add($"         {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private void Fechar()
	{
		_acabou = true;
		if (GetTree() is { Paused: true } t) t.Paused = false;
		Godot.Input.ActionRelease("attack");
		Godot.Input.ActionRelease("run");
		// O PORTAO ANTIGO NAO PODE SOBREVIVER A BANCADA. Se ela morrer no meio da cena F (paciencia
		// vencida, excecao, Ctrl+C), o servidor ficaria de pe com o defeito do dono LIGADO.
		S?.PortaoAntigoNaFotoDoBorrao(false);
		if (C is { } cli) S?.PossuirNaFotoDoBorrao(cli.LocalId, false);
		S?.LimparAFotoDoBorrao();

		GD.Print("\n[borrao-foto] ===== O BORRAO DO DASH, FOTOGRAFADO =====");
		foreach (string l in _linhas) GD.Print("[borrao-foto] " + l);
		GD.Print(_falhas.Count == 0
			? "[borrao-foto] ===== TUDO OK ====="
			: $"[borrao-foto] ===== {_falhas.Count} FALHA(S) =====\n[borrao-foto]   "
			  + string.Join("\n[borrao-foto]   ", _falhas));
		GetTree().Quit();
	}
}
