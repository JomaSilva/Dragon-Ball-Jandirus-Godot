using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA MUDEZ (`--diagmudez`) ============================
/// O PRIMEIRO PEDIDO DO DONO, literal: *"durante os clashs apertar os botoes ta ABRINDO MENUS etc.
/// faca q enquanto ta tendo QUALQUER CLASH (ki ou zanzoclash) as HOTKEYS SAO DESATIVADAS pra n ficar
/// abrindo varios menus"*.
///
/// ============================ UMA LINHA POR ATALHO, COM O NOME ============================
/// "Nenhum menu abriu" ficaria verde apertando UM botao. Sao **dezessete** teclas de jogador, e
/// **quatorze delas estao entre as 22 letras que o embate sorteia** (C, E, F, G, I, K, M, N, P, Q, R,
/// T, V, X) -- entao a bancada aperta as dezessete, uma por uma, e escreve o nome de cada uma na
/// linha dela. Foi assim que se descobriu que cinco caminhos vazavam e que o V (o microfone) era um
/// deles sem ninguem ter visto.
/// =====================================================================================
///
/// ============================ E O CONTRA-EXEMPLO E O DEFEITO INJETADO ============================
/// A mesma varredura roda FORA do embate, e ali as dezessete tem que ABRIR. Nao e conforto: e
/// literalmente o jogo de antes da mudanca, e ele e o unico jeito de a familia de cima nao poder
/// passar verde por vacuo -- uma tecla quebrada, um node que nao nasceu, um pacote que nunca sai. Se
/// as dezessete estivessem mudas o tempo todo, as duas familias juntas dizem isso na cara.
/// ==========================================================================================
///
/// ============================ O QUE ELA NAO PODE DEIXAR QUEBRAR ============================
///   * **O QUICK TIME EVENT.** As 22 letras do embate continuam chegando ao servidor -- calar o
///     `ClashQte` seria desligar o embate pra consertar os menus. O defeito injetado desta familia e
///     exatamente a tentacao que foi recusada no desenho: pendurar o embate no `Foco.Digitando`. Com
///     um campo de texto em foco a letra para de sair, e e o que a injecao mostra.
///   * **O MOVIMENTO.** O dono ja cobrou isto uma vez, em maiusculas: *"pedi pra tu consertar a aura
///     PQ TU QUEBROU A MOVIMENTACAO"*. Aqui se mede a INTENCAO que sai no fio (o bit `InputAndando`
///     do `InputState`), e nao a posicao -- durante o embate o servidor TELEPORTA os dois a cada
///     cruzamento, e "o corpo nao saiu do lugar" seria a medida errada num corpo que atravessa o
///     campo. Fora do embate se mede a posicao TAMBEM, que e o que prova que a medida de intencao
///     nao esta medindo o nada.
/// =====================================================================================
///
/// COMO RODAR -- um processo so, hospedando (as fixtures do embate sao do `--mudezteste`):
///     Godot --headless --path . --host --rede 7918 --mudezteste --kiteste --bpteste 200000
///           --conta bancada_mudez --nome Mudo --raca Saiyan --diagmudez
/// </summary>
public partial class RoboDeMudez : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? Servidor => Jandirus.Server.GameServer.Instance;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];

	/// <summary>
	/// O PLACAR DA INJECAO, separado do outro -- a mesma disciplina da `--diagtecla`: verde na rodada
	/// real e "nao viu defeito"; vermelho com o defeito na frente e "sabe olhar".
	/// </summary>
	private int _injOk, _injFalha;
	private readonly List<string> _injPassouBatido = [];

	private static void Nota(string linha) => GD.Print("[mudez] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque
			 + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	// =====================================================================
	// O FIO -- os bytes que chegaram no servidor
	// =====================================================================
	/// <summary>
	/// O QUE SAIU NO FIO, e nao o que o cliente pretendia. Metade destas dezessete teclas nao abre
	/// tela nenhuma -- ela manda pacote --, e a unica leitura honesta de "a tecla disparou" e o que o
	/// servidor RECEBEU (`GameServer.EspiaoDeEntrada`, a mesma porta que a `--diagtecla` usa).
	/// </summary>
	private readonly List<byte[]> _fio = [];
	private readonly object _trava = new();

	private void Marcar() { lock (_trava) _fio.Clear(); }

	private byte[][] Colher() { lock (_trava) return [.. _fio]; }

	private bool Saiu(Protocol.C2S op)
	{
		foreach (byte[] p in Colher()) if (p.Length > 0 && p[0] == (byte)op) return true;
		return false;
	}

	/// <summary>
	/// UM BIT DO `InputState`. Ele e o ULTIMO byte do pacote (opcode, sequencia, posicao, bandeiras),
	/// e e assim que se le "o cliente esta pedindo pra andar / subir / descer" sem escrever um
	/// decodificador que envelheceria junto com o pacote.
	/// </summary>
	private bool BitDeInput(byte bit)
	{
		foreach (byte[] p in Colher())
			if (p.Length > 2 && p[0] == (byte)Protocol.C2S.InputState && (p[^1] & bit) != 0) return true;
		return false;
	}

	// =====================================================================
	// TECLADO DE MENTIRA -- eventos de verdade, injetados no motor
	// =====================================================================
	/// <summary>
	/// AS DUAS METADES PREENCHIDAS. `PhysicalKeycode` e o que o registro le (`Teclas.Fisica`) e o que
	/// o `InputMap` casa; `Keycode` e o que o chat e o menu leem pro ESC e pro ENTER. Um teclado de
	/// verdade manda os dois. (A mesma nota da `--diagtecla`.)
	/// </summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = apertada });

	private static void Toque(Key k) { Tecla(k, true); Tecla(k, false); }

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static T? Procurar<T>(Node raiz, Func<T, bool> quer) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t && quer(t)) return t;
			if (Procurar(f, quer) is { } achado) return achado;
		}
		return null;
	}

	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	private static Node? Achar(string nome) =>
		Engine.GetMainLoop() is SceneTree t ? t.Root.FindChild(nome, true, false) : null;

	/// <summary>Uma tela que nao tem `Instancia`: pergunta-se ao NODE dela se ha algo desenhado.</summary>
	private static bool TelaAberta(string nome) =>
		Achar(nome) is { } n && n.GetChildren().OfType<Control>().Any(c => c.Visible);

	/// <summary>Quantos paineis do HUD estao acesos. O da ajuda (TAB) e o unico que liga e desliga.</summary>
	private static int PaineisDoHud() =>
		Hud.Instancia is { } h ? Todos<PanelContainer>(h).Count(p => p.Visible) : 0;

	private int _paineisAntes;

	// =====================================================================
	// O EMBATE, VISTO DO CLIENTE
	// =====================================================================
	private char _letraPedida;
	private int _letras, _fins;
	private Protocol.TipoDeEmbate _tipo;

	public override void _Ready()
	{
		Jandirus.Server.GameServer.EspiaoDeEntrada = bytes => { lock (_trava) _fio.Add(bytes); };

		if (C is { } cli)
		{
			cli.ClashComecou += Comecou;
			cli.ClashTeclaPedida += Pediu;
			cli.ClashAcabou += Acabou;
		}

		// O MICROFONE SEM MICROFONE: a maquina de bancada roda headless, onde o driver de audio e o
		// Dummy. A fonte de teste substitui o aparelho e mais nada -- o `Falando`, que e o que esta
		// bancada le, continua sendo escrito pelo mesmo `QuerFalar` do jogo. Ver `Microfone`.
		Microfone.FonteDeTeste = _ => false;
		Boot.Config.VozLigada = true;
		Boot.Config.VozApertarParaFalar = true;

		_roteiro = Roteiro().GetEnumerator();
	}

	public override void _ExitTree()
	{
		Jandirus.Server.GameServer.EspiaoDeEntrada = null;
		Microfone.FonteDeTeste = null;
		if (C is not { } cli) return;
		cli.ClashComecou -= Comecou;
		cli.ClashTeclaPedida -= Pediu;
		cli.ClashAcabou -= Acabou;
	}

	private void Comecou(Protocol.TipoDeEmbate tipo, int a, int b, int ms, float meu, float dele)
	{
		_tipo = tipo;
		Nota($"...o embate COMECOU ({tipo}, {ms} ms)");
	}

	private void Pediu(char c, int ms) { _letraPedida = c; _letras++; }

	private void Acabou(int venc, int perd) { _fins++; _letraPedida = '\0'; }

	// =====================================================================
	// AS DEZESSETE TECLAS DO JOGADOR
	// =====================================================================
	/// <summary>
	/// UM ATALHO MEDIDO: o nome que sai na linha, a tecla, se ela e de SEGURAR (as sondas do
	/// `IsActionPressed` nao respondem a um toque de um quadro), o que conta como "disparou" e como
	/// desfazer o que ele fez.
	/// </summary>
	private sealed record Botao(string Rotulo, Key Tecla, bool Segurar, Func<bool> Disparou, Action? Fechar = null);

	/// <summary>
	/// A TABELA -- e ela e a resposta a "quais atalhos?". Sao as teclas de JOGADOR que o
	/// `Foco.AtalhosMudos` cala: as quatro de interface, as duas da caixa de fala e as onze sondas do
	/// corpo. As que NAO estao aqui estao de fora de proposito, e cada ausencia tem motivo escrito:
	/// o ANDAR (trava propria no vetor -- ver a familia do movimento), a GUARDA (soltar o ALT encerra
	/// o lado de quem segura um feixe com as maos) e o SOCO (o espaco nao e letra, e o corpo ja esta
	/// preso pelo `Stun`).
	/// </summary>
	private Botao[] Botoes() =>
	[
		new("P  -- o menu do jogo", Key.P, false,
			() => MenuJogo.Instancia is { Visible: true }, () => MenuJogo.Instancia?.Fechar()),
		new("TAB -- a ajuda de teclas do HUD", Key.Tab, false,
			() => PaineisDoHud() > _paineisAntes, () => Toque(Key.Tab)),
		new("I  -- a mochila", Key.I, false,
			() => TelaAberta("Inventario"), () => Toque(Key.I)),
		// O `NaTela` DELE, e nao "algum Control visivel dentro do node": o menu de interacao tem uma
		// DICA ("[E] corpo de Fulano") que fica acesa sempre que ha alvo por perto -- e ela e um Control
		// filho. Lendo o node inteiro, a bancada acusaria o E de estar aberto o tempo todo, e a linha do
		// embate ficaria vermelha com o jogo intacto. (Achado rodando, com o corpo no chao ao lado.)
		new("E  -- o menu de interacao", Key.E, false,
			() => MenuDeInteracao.Instancia?.NaTela == true, () => Toque(Key.E)),
		new("ENTER -- a caixa de fala", Key.Enter, false,
			() => Chat.Digitando, () => Toque(Key.Escape)),
		new("/  -- a caixa de fala ja com o comando", Key.Slash, false,
			() => Chat.Digitando, () => Toque(Key.Escape)),
		new("V  -- o microfone", Key.V, true, () => Microfone.Falando),
		new("F  -- voar", Key.F, false, () => Saiu(Protocol.C2S.Habilidade)),
		new("N  -- nadar", Key.N, false, () => Saiu(Protocol.C2S.Habilidade)),
		new("Q  -- agarrar", Key.Q, false, () => Saiu(Protocol.C2S.Habilidade)),
		new("C  -- carregar Ki", Key.C, true, () => Saiu(Protocol.C2S.Carregar)),
		new("X  -- reverter a forma", Key.X, false, () => Saiu(Protocol.C2S.Transformar)),
		new("T  -- treinar", Key.T, false, () => Saiu(Protocol.C2S.Activity)),
		new("M  -- meditar", Key.M, false,
			() => TelaDeMeditacao.Instancia?.NaTela == true, () => Toque(Key.Escape)),
		new("K  -- alternar o golpe letal", Key.K, false, () => Saiu(Protocol.C2S.Lethal)),
		new("R  -- subir no ar", Key.R, true, () => BitDeInput(Protocol.InputSubir)),
		new("G  -- descer no ar", Key.G, true, () => BitDeInput(Protocol.InputDescer)),
	];

	/// <summary>
	/// APERTA UM ATALHO E COLHE. `Segurar` existe porque metade destas teclas nao e lida por evento:
	/// elas sao SONDAS (`IsActionPressed`) dentro do laco de fisica, e um toque que aperta e solta no
	/// mesmo quadro pode cair inteiro entre dois quadros de fisica.
	/// </summary>
	private bool _disparou;

	private IEnumerable<double> Apertar(Botao b)
	{
		_paineisAntes = PaineisDoHud();
		Marcar();
		if (b.Segurar) Tecla(b.Tecla, true); else Toque(b.Tecla);
		yield return 0.30;

		// ============================ O RETRATO SAI COM A TECLA AINDA APERTADA ============================
		// **Achado rodando**: o microfone e um ESTADO enquanto a tecla esta em baixo (`Falando`), e a
		// primeira versao olhava depois de soltar -- lia falso e acusava o V de mudo num jogo em que
		// ele funcionava. As teclas de PACOTE sao o oposto: o pacote pode chegar ao servidor um quadro
		// depois. Entao olha-se duas vezes e as duas SOMAM: o estado que so existe com a tecla em
		// baixo, e o pacote que so chega depois dela.
		// ==========================================================================================
		_disparou = b.Disparou();
		if (b.Segurar) Tecla(b.Tecla, false);
		yield return 0.10;
		_disparou |= b.Disparou();
	}

	/// <summary>A varredura inteira, com uma linha por tecla. O `esperado` e o que se afirma.</summary>
	private IEnumerable<double> Varrer(string prefixo, bool esperado)
	{
		foreach (Botao b in Botoes())
		{
			foreach (double d in Apertar(b)) yield return d;
			bool disparou = _disparou;
			Checa($"{prefixo} {b.Rotulo}", disparou == esperado, disparou ? "disparou" : "mudo");
			if (disparou) { b.Fechar?.Invoke(); yield return 0.12; }
		}
	}

	// =====================================================================
	// O EMBATE SOB ENCOMENDA
	// =====================================================================
	/// <summary>
	/// GARANTE QUE HA UM EMBATE DE PE, do tipo pedido -- e o que faz a varredura de dezessete teclas
	/// caber num encontro que dura de 3 a 6 segundos: quando o embate acaba no meio dela, a bancada
	/// arma outro e continua de onde parou, em vez de medir o silencio de um embate que ja terminou.
	/// </summary>
	private IEnumerable<double> GarantirEmbate(bool deKi)
	{
		if (C is not { } cli || Servidor is not { } srv) yield break;
		if (cli.EmClash) yield break;

		bool armou = deKi ? srv.EmbateDeKiDeTeste(cli.LocalId) : srv.EmbateDeVelocidadeDeTeste(cli.LocalId);
		if (!armou) { Nota("  --     o servidor NAO armou o embate"); yield break; }

		// ESPERA O PACOTE, e nao um numero de quadros: quem diz que o embate comecou e o `Comecou`
		// que chega pela rede -- que e a mesma coisa que faria um embate de verdade comecar.
		for (int i = 0; i < 120 && !cli.EmClash; i++) yield return 0.05;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerator<double>? _roteiro;
	private double _espera = 2.5;

	public override void _Process(double delta)
	{
		if (_roteiro == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; return; }
		_espera = _roteiro.Current;
	}

	private IEnumerable<double> Roteiro()
	{
		Nota("=================== A MUDEZ DOS ATALHOS ===================");

		for (int i = 0; i < 400 && C is not { Connected: true }; i++) yield return 0.05;
		Checa("o mundo subiu e o cliente esta conectado", C is { Connected: true });
		Checa("as fixtures do embate estao no ar (`--mudezteste` no servidor)",
			  Servidor != null, Servidor == null ? "sem servidor neste processo" : "");
		if (C is not { Connected: true } || Servidor == null) { foreach (double d in Fechamento()) yield return d; yield break; }

		foreach (double d in F0_ForaDoEmbate()) yield return d;
		foreach (double d in F1_DuranteOZanzoClash()) yield return d;
		foreach (double d in F2_OFimDevolve()) yield return d;
		foreach (double d in F3_DuranteAColisaoDeKi()) yield return d;
		foreach (double d in F4_OsFinsAnormais()) yield return d;
		foreach (double d in F5_OEmbateDosOutros()) yield return d;
		foreach (double d in Fechamento()) yield return d;
	}

	private IEnumerable<double> Fechamento()
	{
		if (C is { } cli) Servidor?.SoltarEmbateDeTeste(cli.LocalId);
		Servidor?.LimparAMudezDeTeste();
		yield return 0.2;

		Nota("==========================================================");
		Nota($"PLACAR: {_ok} OK, {_falha} FALHA");
		if (_falha > 0) foreach (string f in _reprovadas) Nota($"   falhou: {f}");
		Nota($"INJECAO: {_injOk} pegou, {_injFalha} PASSOU BATIDO");
		if (_injFalha > 0) foreach (string f in _injPassouBatido) Nota($"   passou batido: {f}");
		Nota("==========================================================");
		yield return 0.5;

		// FECHA O QUE ELA SUBIU -- como os outros robos de bancada. Sem isto o processo fica de pe
		// depois do placar e quem roda por script espera pra sempre.
		GetTree()?.Quit();
	}

	// =====================================================================
	// F0 -- FORA DO EMBATE: AS DEZESSETE ABREM
	// =====================================================================
	/// <summary>
	/// O CONTRA-EXEMPLO, QUE E O JOGO DE ANTES. Sem ele, calar as teclas PRA SEMPRE passaria verde na
	/// familia seguinte -- e "nenhum menu abriu" e verdade tambem num cliente com o teclado morto.
	/// </summary>
	private IEnumerable<double> F0_ForaDoEmbate()
	{
		Nota("--- F0: FORA do embate as dezessete teclas ABREM (o contra-exemplo) ---");
		Checa("nao ha embate nenhum de pe (senao esta familia mede a outra)", C?.EmClash != true);

		// O MOVIMENTO, DUAS VEZES: a INTENCAO que sai no fio e a POSICAO que o corpo alcanca. A
		// segunda existe pra a primeira nao poder ser vacua -- um bit ligado num corpo que nao anda
		// nao prova nada, e e a posicao que o dono ve.
		Vector2 antes = World.Instancia?.PosicaoLocal ?? default;
		Marcar();
		Tecla(Key.D, true);
		yield return 0.7;
		Tecla(Key.D, false);
		bool pediuAndar = BitDeInput(Protocol.InputAndando);
		float andou = (World.Instancia?.PosicaoLocal ?? default).DistanceTo(antes);
		yield return 0.2;

		Checa("MOVIMENTO fora do embate: o cliente PEDE pra andar (bit `InputAndando` no fio)", pediuAndar);
		Checa("MOVIMENTO fora do embate: e o corpo SAI DO LUGAR", andou > 8, $"{andou:0} px");

		// A TECLA E PRECISA DE ALVO: o menu de interacao nao abre no vazio (obra, veiculo ou corpo no
		// chao). Sem isto o E ficaria mudo com o jogo INTACTO, e o contra-exemplo acusaria de defeito
		// o unico caso em que ele esta certo. Ver `CadaverPertoDeTeste`.
		//
		// **DEPOIS DA MEDIDA DE MOVIMENTO, E A ESQUERDA**: um corpo posto no caminho e uma parede, e a
		// primeira versao punha o cadaver exatamente pra onde a bancada anda -- zero pixel percorrido,
		// e a familia do movimento reprovando por um obstaculo que ela mesma criou.
		Checa("ha um corpo no chao ao lado (o alvo da tecla E)",
			  Servidor?.CadaverPertoDeTeste(C?.LocalId ?? 0) == true);
		yield return 0.4;

		foreach (double d in Varrer("FORA do embate ABRE:", esperado: true)) yield return d;

		// A ATIVIDADE VOLTA PRO LUGAR: o T ligou o treino, e treinar durante o resto da bancada
		// mudaria o BP no meio das medidas.
		C?.SendActivity(Protocol.Activity.Parado);
		yield return 0.2;
	}

	// =====================================================================
	// F1 -- DURANTE O ZANZO CLASH: AS DEZESSETE CALAM
	// =====================================================================
	private IEnumerable<double> F1_DuranteOZanzoClash()
	{
		Nota("--- F1: durante o ZANZO CLASH as dezessete ficam MUDAS ---");

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		Checa("o ZANZO CLASH esta de pe (o `Comecou` chegou pelo fio)",
			  C?.EmClash == true && _tipo == Protocol.TipoDeEmbate.Velocidade, $"tipo {_tipo}");
		Checa("...e o cliente sabe que os atalhos estao mudos", Foco.AtalhosMudos);

		// AS DEZESSETE, uma linha cada -- rearmando o embate se ele acabar no meio da varredura.
		foreach (Botao b in Botoes())
		{
			foreach (double d in GarantirEmbate(deKi: false)) yield return d;
			foreach (double d in Apertar(b)) yield return d;
			bool disparou = _disparou;
			Checa($"NO ZANZO CLASH fica mudo: {b.Rotulo}", !disparou, disparou ? "DISPAROU" : "mudo");
			if (disparou) { b.Fechar?.Invoke(); yield return 0.12; }
		}

		// ---- O QUICK TIME EVENT CONTINUA VIVO ----
		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		foreach (double d in ALetraChega("NO ZANZO CLASH")) yield return d;

		// ---- O MOVIMENTO ----
		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		Marcar();
		Tecla(Key.D, true);
		yield return 0.6;
		Tecla(Key.D, false);
		bool pediu = BitDeInput(Protocol.InputAndando);
		yield return 0.1;
		Checa("MOVIMENTO no embate: o cliente para de PEDIR passo -- e pelo vetor do `LocalPlayer`, "
			  + "que ja era assim antes desta mudanca", !pediu);
	}

	/// <summary>
	/// A LETRA DO EMBATE CONTINUA SAINDO -- e a linha que impede o conserto de virar "desliguei o
	/// embate pra parar de abrir menu".
	///
	/// O DEFEITO INJETADO E A TENTACAO RECUSADA NO DESENHO: pendurar o embate no `Foco.Digitando`. O
	/// `ClashQte` le por ele, entao qualquer campo de texto em foco cala a letra -- que e o que
	/// aconteceria com o embate inteiro se ele morasse la. Aqui o campo e um do menu do jogo, posto
	/// em foco na mao (o P esta mudo, e e esse o ponto).
	/// </summary>
	private IEnumerable<double> ALetraChega(string prefixo)
	{
		for (int i = 0; i < 60 && _letraPedida == '\0'; i++) yield return 0.05;
		char pedida = _letraPedida;
		Checa($"{prefixo}: o servidor esta pedindo letra", pedida != '\0');
		if (pedida == '\0') yield break;

		Marcar();
		Toque(TeclaDaLetra(pedida));
		yield return 0.25;
		Checa($"{prefixo}: a letra do quick time event CONTINUA saindo (a tecla '{pedida}' chegou ao servidor)",
			  Saiu(Protocol.C2S.ClashTecla));

		// ---- A INJECAO ----
		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara("Admin");
		yield return 0.35;
		LineEdit? campo = MenuJogo.Instancia is { } m ? Procurar<LineEdit>(m, _ => true) : null;
		campo?.GrabFocus();
		yield return 0.2;

		bool digitando = Foco.Digitando;
		for (int i = 0; i < 60 && _letraPedida == '\0'; i++) yield return 0.05;
		Marcar();
		Toque(TeclaDaLetra(_letraPedida == '\0' ? pedida : _letraPedida));
		yield return 0.25;
		bool saiu = Saiu(Protocol.C2S.ClashTecla);

		campo?.ReleaseFocus();
		MenuJogo.Instancia?.Fechar();
		yield return 0.2;

		Injeta($"{prefixo}: com um campo de texto em foco a letra PARA de sair -- e por isso o embate "
			   + "nao pode morar no `Foco.Digitando`", digitando && !saiu,
			   $"digitando={digitando}, letra saiu={saiu}");
	}

	/// <summary>A tecla fisica de uma letra do alfabeto do embate.</summary>
	private static Key TeclaDaLetra(char c) => Key.A + (c - 'A');

	// =====================================================================
	// F2 -- O FIM DEVOLVE OS ATALHOS
	// =====================================================================
	private IEnumerable<double> F2_OFimDevolve()
	{
		Nota("--- F2: acabado o embate, as teclas VOLTAM ---");

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		int fins = _fins;
		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		for (int i = 0; i < 80 && _fins == fins; i++) yield return 0.05;

		Checa("o fim do embate chegou ao cliente (`Acabou`)", _fins > fins);
		yield return 0.2;
		Checa("...e o silencio caiu junto", C?.EmClash != true && !Foco.AtalhosMudos);

		foreach (double d in Apertar(Botoes()[0])) yield return d;
		bool abriu = MenuJogo.Instancia is { Visible: true };
		Checa("...e o P volta a abrir o menu", abriu);
		if (abriu) { MenuJogo.Instancia?.Fechar(); yield return 0.15; }

		Marcar();
		Tecla(Key.D, true);
		yield return 0.6;
		Tecla(Key.D, false);
		Checa("...e o cliente volta a PEDIR passo (o movimento nao ficou preso)",
			  BitDeInput(Protocol.InputAndando));
		yield return 0.2;
	}

	// =====================================================================
	// F3 -- DURANTE A COLISAO DE KI
	// =====================================================================
	/// <summary>
	/// O SEGUNDO EMBATE, com a sua propria linha. Ele nasce de outro gatilho (dois feixes se
	/// encontrando dentro do tique do projetil), tem outra duracao e vem por outro `Comecou` -- e o
	/// pedido do dono diz *"QUALQUER CLASH (ki ou zanzoclash)"*. Uma familia so cobriria metade.
	/// </summary>
	private IEnumerable<double> F3_DuranteAColisaoDeKi()
	{
		Nota("--- F3: durante a COLISAO DE KI as dezessete ficam MUDAS ---");

		foreach (double d in GarantirEmbate(deKi: true)) yield return d;
		Checa("a COLISAO DE KI esta de pe (o gatilho dos feixes disparou sozinho, no tique)",
			  C?.EmClash == true && _tipo != Protocol.TipoDeEmbate.Velocidade, $"tipo {_tipo}");
		if (C?.EmClash != true) yield break;

		foreach (Botao b in Botoes())
		{
			foreach (double d in GarantirEmbate(deKi: true)) yield return d;
			foreach (double d in Apertar(b)) yield return d;
			bool disparou = _disparou;
			Checa($"NA COLISAO DE KI fica mudo: {b.Rotulo}", !disparou, disparou ? "DISPAROU" : "mudo");
			if (disparou) { b.Fechar?.Invoke(); yield return 0.12; }
		}

		foreach (double d in GarantirEmbate(deKi: true)) yield return d;
		foreach (double d in ALetraChega("NA COLISAO DE KI")) yield return d;

		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		yield return 0.4;
	}

	// =====================================================================
	// F4 -- OS FINS ANORMAIS
	// =====================================================================
	/// <summary>
	/// O EMBATE PODE ACABAR MAL, e o teclado tem que voltar de todo jeito. Duas formas, e elas sao
	/// diferentes na raiz:
	///   * **MORTE do adversario** -- o fim de verdade, pelo tique (`e.A.Ficha.dead`): o `Acabou`
	///     chega e o silencio cai por ele;
	///   * **O FIM QUE NUNCA CHEGA** -- nocaute, troca de zona, logout, pacote perdido. Aqui nao ha
	///     pacote nenhum a esperar, e quem devolve o teclado e o PRAZO do proprio cliente
	///     (`GameClient.EmClash`). Esta e a unica familia que mede o prazo, e ela mede as duas pontas:
	///     ele nao pode cair cedo demais (o embate ainda esta correndo) nem ficar de pe pra sempre.
	/// </summary>
	private IEnumerable<double> F4_OsFinsAnormais()
	{
		Nota("--- F4: o embate acabando MAL tambem devolve as teclas ---");

		// ---- (a) o adversario morreu no meio ----
		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		int fins = _fins;
		bool matou = Servidor?.MatarAdversarioDeTeste(C?.LocalId ?? 0) == true;
		for (int i = 0; i < 100 && _fins == fins; i++) yield return 0.05;
		Checa("o adversario MORREU no meio do embate e o fim chegou", matou && _fins > fins);
		Checa("...e o silencio caiu com ele", C?.EmClash != true);

		// ---- (b) o fim que nunca chega ----
		const int MsDoFantasma = 1200;
		Servidor?.AnuncioSemEmbateDeTeste(C?.LocalId ?? 0, MsDoFantasma);
		yield return 0.25;
		Checa("um embate que o servidor ESQUECEU ainda cala os atalhos (o `Comecou` sozinho basta)",
			  C?.EmClash == true);

		foreach (double d in Apertar(Botoes()[0])) yield return d;
		Checa("...e o P continua mudo enquanto o prazo do embate corre",
			  MenuJogo.Instancia is not { Visible: true });

		// O PRAZO E `duracao + folga`. Espera-se ate um pouco DEPOIS dele: cair antes seria soltar o
		// teclado no meio de um embate vivo, e nao cair nunca seria o bug que o prazo existe pra
		// impedir.
		for (int i = 0; i < 120 && C?.EmClash == true; i++) yield return 0.05;
		Checa("...e passado o prazo o silencio morre SOZINHO, sem nenhum pacote de fim",
			  C?.EmClash != true);

		foreach (double d in Apertar(Botoes()[0])) yield return d;
		bool abriu = MenuJogo.Instancia is { Visible: true };
		Checa("...e o P volta a abrir o menu", abriu);
		if (abriu) { MenuJogo.Instancia?.Fechar(); yield return 0.15; }
	}

	// =====================================================================
	// F5 -- O EMBATE DOS OUTROS
	// =====================================================================
	/// <summary>
	/// O FIM DA BRIGA ALHEIA NAO E O MEU. Numa zona com quatro lutadores ha mais de um embate ao mesmo
	/// tempo -- e enquanto o `Acabou` era anuncio de ZONA, o fim do embate dos outros derrubava o
	/// `EmClash` de quem ainda estava no proprio: teclado destravado no meio, tela do quick time event
	/// fechada, e o desfecho da briga alheia escrito na cara do jogador.
	///
	/// A INJECAO E O DEFEITO DE ANTES, LITERAL: o servidor manda pro jogador o `Acabou` de um embate
	/// que nao e dele -- byte por byte o que o `AvisarZona` fazia chegar. O criterio tem que cair.
	/// </summary>
	private IEnumerable<double> F5_OEmbateDosOutros()
	{
		Nota("--- F5: o fim do embate ALHEIO nao solta o meu teclado ---");

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		if (C?.EmClash != true) { Checa("havia um embate meu de pe pra medir", false); yield break; }

		bool alheio = Servidor?.EmbateAlheioDeTeste(C?.LocalId ?? 0) == true;
		yield return 0.5;
		Checa("um embate ALHEIO comecou e acabou na minha zona", alheio);
		Checa("...e o MEU continua de pe, com os atalhos mudos", C?.EmClash == true && Foco.AtalhosMudos);

		foreach (double d in Apertar(Botoes()[0])) yield return d;
		Checa("...e o P continua sem abrir o menu", MenuJogo.Instancia is not { Visible: true });

		// ---- A INJECAO: o fim alheio chegando como o `AvisarZona` fazia chegar ----
		bool aindaMudo = C?.EmClash == true;
		Servidor?.FimAlheioComoZonaDeTeste(C?.LocalId ?? 0);
		yield return 0.3;
		Injeta("o fim alheio mandado PRO JOGADOR (o `AvisarZona` de antes) derruba o silencio no meio "
			   + "do meu embate", aindaMudo && C?.EmClash != true,
			   $"antes mudo={aindaMudo}, depois mudo={C?.EmClash == true}");

		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		yield return 0.3;
	}
}
