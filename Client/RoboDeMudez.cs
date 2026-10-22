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
/// "Nenhum menu abriu" ficaria verde apertando UM botao. Sao **dezoito** teclas de jogador, e
/// **quatorze delas estavam entre as 22 letras que o embate sorteava** (C, E, F, G, I, K, M, N, P, Q,
/// R, T, V, X; desde 2026-10-19 ele e so WASD, e o silencio ficou) -- entao a bancada aperta as
/// dezoito, uma por uma, e escreve o nome de cada uma na linha dela. Foi assim que se descobriu que
/// cinco caminhos vazavam e que o V (o microfone) era um deles sem ninguem ter visto.
/// =====================================================================================
///
/// ============================ E O CONTRA-EXEMPLO E O DEFEITO INJETADO ============================
/// A mesma varredura roda FORA do embate, e ali as dezoito tem que ABRIR. Nao e conforto: e
/// literalmente o jogo de antes da mudanca, e ele e o unico jeito de a familia de cima nao poder
/// passar verde por vacuo -- uma tecla quebrada, um node que nao nasceu, um pacote que nunca sai. Se
/// as dezoito estivessem mudas o tempo todo, as duas familias juntas dizem isso na cara.
/// ==========================================================================================
///
/// ============================ O QUE ELA NAO PODE DEIXAR QUEBRAR ============================
///   * **O QUICK TIME EVENT.** As letras do embate (so W, A, S e D desde 2026-10-19: ver
///     <see cref="SoOWasdResponde"/>) continuam chegando ao servidor -- calar o
///     `ClashQte` seria desligar o embate pra consertar os menus. O defeito injetado desta familia e
///     exatamente a tentacao que foi recusada no desenho: pendurar o embate no `Foco.Digitando`. Com
///     um campo de texto em foco a letra para de sair, e e o que a injecao mostra.
///   * **O MOVIMENTO.** O dono ja cobrou isto uma vez, em maiusculas: *"pedi pra tu consertar a aura
///     PQ TU QUEBROU A MOVIMENTACAO"*. Aqui se mede a INTENCAO que sai no fio (o bit `InputAndando`
///     do `InputState`), e nao a posicao -- durante o embate o servidor TELEPORTA os dois a cada
///     cruzamento, e "o corpo nao saiu do lugar" seria a medida errada num corpo que atravessa o
///     campo. Fora do embate se mede a posicao TAMBEM, que e o que prova que a medida de intencao
///     nao esta medindo o nada.
///   * **A GUARDA E O SOCO.** As duas teclas que ficaram FORA da tabela de dezoito -- e "de proposito"
///     virou MEDIDA na `F3` e na `F6`: o ALT sobe e desce (pacote `Guard` nas duas transicoes) e o
///     espaco sai (`Action`), **dentro dos dois embates**. A guarda na COLISAO DE KI e o caso limite
///     do arquivo: soltar o ALT ENCERRA o lado de quem segura o feixe, entao um portao que a
///     alcancasse nao "desabilitaria uma hotkey" -- faria o jogador PERDER a disputa de ki por estar
///     num embate.
/// =====================================================================================
///
/// ============================ E A ORDEM IMPORTA, QUE E A FAMILIA `F6` ============================
/// A `F1` e a `F3` provam que o ESC ficou mudo -- e um portao que calasse o corpo INTEIRO passaria
/// verde nelas tambem. Por isso a `F6` aperta o ESC DE VERDADE com o embate correndo e mede todo o
/// resto **depois** dele: a guarda, o soco, a letra do quick time event, e o andar (bit e pixels) do
/// outro lado do embate.
/// ============================================================================================
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
	/// O QUE SAIU NO FIO, e nao o que o cliente pretendia. Metade destas dezoito teclas nao abre
	/// tela nenhuma -- ela manda pacote --, e a unica leitura honesta de "a tecla disparou" e o que o
	/// servidor RECEBEU (`GameServer.EspiaoDeEntrada`, a mesma porta que a `--diagtecla` usa).
	/// </summary>
	private readonly List<byte[]> _fio = [];
	private readonly object _trava = new();

	private void Marcar() { lock (_trava) _fio.Clear(); }

	private byte[][] Colher() { lock (_trava) return [.. _fio]; }

	// =====================================================================
	// AS DUAS ESTRADAS -- a medida que substituiu o relogio
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE UM CONTADOR DE PACOTES E, E O RELOGIO NAO ERA ============================
	/// Esta bancada media com SONO FIXO: apertava a tecla, dormia 0,40 s e dizia o veredito. Sao 55 das
	/// 81 provas penduradas nessa janela, e numa maquina ocupada ela nao fecha -- o que a tornava
	/// **flaky nas duas direcoes**, e a segunda e a que envenena:
	///
	///   * em F0 e F2 a afirmacao e *"a tecla DISPAROU"*: quadro faminto => VERMELHO FALSO. Chato, mas
	///     visivel -- foi assim que ela apareceu ("C -- carregar Ki" e "volta a PEDIR passo");
	///   * em F1 e F3 a afirmacao e *"a tecla NAO disparou"*: quadro faminto => **VERDE FALSO**. A
	///     bancada certificaria uma mudez que nunca aconteceu, e ninguem jamais veria. Sao 34 das 55.
	///
	/// **O FATO QUE SUBSTITUI O RELOGIO E A VOLTA COMPLETA DO FIO**, e sao duas, uma por sentido:
	///
	///   * <see cref="VoltasDoCliente"/> -- um `C2S.InputState` colhido pelo `_Process` do SERVIDOR. O
	///     `LocalPlayer` o manda a 30 Hz de dentro do MESMO `_Process` que roda todas as sondas de
	///     tecla (`LerAcoes`, `LerTeclaC`, `LerMira`), entao cada pacote destes e a prova de que um
	///     quadro inteiro do cliente rodou, serializou e ATRAVESSOU -- exatamente a estrada que as onze
	///     teclas de pacote percorrem;
	///   * <see cref="_voltasDoServidor"/> -- um `S2C.Snapshot` recebido pelo cliente, a 30 Hz. E a
	///     prova do sentido contrario: o servidor tiquetaqueou e os bytes dele chegaram aqui.
	///
	/// Afirmar "nao veio" depois de N voltas PROVADAS e uma afirmacao; depois de 0,40 s de relogio de
	/// parede e um palpite sobre a carga da maquina.
	/// ==========================================================================================================
	/// </summary>
	private int _voltasDoCliente;

	/// <inheritdoc cref="_voltasDoCliente"/>
	private int _voltasDoServidor;

	private int VoltasDoCliente() { lock (_trava) return _voltasDoCliente; }

	/// <summary>Metodo NOMEADO pra poder sair no `_ExitTree` -- lambda nao se cancela.</summary>
	private void ContarSnapshot(List<Jandirus.Net.EntityState> _) => _voltasDoServidor++;

	/// <summary>
	/// QUANTAS VOLTAS BASTAM. Sete no total (quatro com a tecla em baixo, tres depois de solta), contra
	/// as ~12 que a janela de 0,40 s dava numa maquina ociosa -- so que estas sao CONTADAS e nao
	/// esperadas. O numero e maior que um porque as teclas de pacote nao viajam pelo canal do
	/// `InputState` (elas vao no `ChannelReliable`, ele no `ChannelState`): cada volta a mais e mais uma
	/// colheita do servidor em que um pacote confiavel de um quadro anterior ja teria aparecido.
	/// </summary>
	private const int VoltasEmBaixo = 4, VoltasDepoisDeSoltar = 3;

	/// <summary>
	/// O TETO, em segundos de relogio de parede. Ele NAO e a medida -- e o para-quedas pra a bancada nao
	/// pendurar pra sempre se o cliente cair, e por isso ele e generoso o bastante pra nunca ser o que
	/// decide um veredito numa maquina que ainda esta viva.
	/// </summary>
	private const double TetoDaEspera = 8.0;

	/// <summary>Espera ate o FATO acontecer (ou ate o para-quedas abrir). Um poll por quadro.</summary>
	private static IEnumerable<double> AteQue(Func<bool> fato, double teto = TetoDaEspera)
	{
		ulong fim = Time.GetTicksMsec() + (ulong)(teto * 1000);
		while (!fato() && Time.GetTicksMsec() < fim) yield return 0;
	}

	/// <summary>
	/// AS DUAS ESTRADAS ANDARAM <paramref name="voltas"/> VEZES. E o que uma afirmacao NEGATIVA ("nao
	/// disparou", "nao pediu passo", "o meu embate nao caiu") precisa antes de poder dizer "nao veio".
	/// </summary>
	private IEnumerable<double> AsEstradasAndaram(int voltas)
	{
		int cliente = VoltasDoCliente() + voltas;
		int servidor = _voltasDoServidor + voltas;
		foreach (double d in AteQue(() => VoltasDoCliente() >= cliente && _voltasDoServidor >= servidor))
			yield return d;
	}

	/// <summary>
	/// O FATO, OU A PROVA DE QUE ELE TEVE CHANCE. Sai no INSTANTE em que o fato acontece (e o que faz a
	/// bancada ficar mais rapida numa maquina ociosa) e, se ele nao acontecer, so desiste depois de as
	/// duas estradas terem andado -- que e o que faz o "nao aconteceu" valer alguma coisa.
	/// </summary>
	private IEnumerable<double> AteOFatoOuAsEstradas(Func<bool> fato, int voltas)
	{
		int cliente = VoltasDoCliente() + voltas;
		int servidor = _voltasDoServidor + voltas;
		foreach (double d in AteQue(() => fato()
									   || (VoltasDoCliente() >= cliente && _voltasDoServidor >= servidor)))
			yield return d;
	}

	private bool Saiu(Protocol.C2S op)
	{
		foreach (byte[] p in Colher()) if (p.Length > 0 && p[0] == (byte)op) return true;
		return false;
	}

	/// <summary>
	/// UM BIT DO `InputState`. Ele e o ULTIMO byte do pacote (opcode, sequencia, hora, posicao, bandeiras),
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
		Jandirus.Server.GameServer.EspiaoDeEntrada = bytes =>
		{
			lock (_trava)
			{
				_fio.Add(bytes);
				// A VOLTA DO CLIENTE E CONTADA AQUI e nao lida do `_fio`: o `Marcar()` esvazia o fio a
				// cada tecla, e um contador que zera junto nao serve pra medir "quantos quadros meus o
				// servidor ja colheu". Ver `_voltasDoCliente`.
				if (bytes.Length > 0 && bytes[0] == (byte)Protocol.C2S.InputState) _voltasDoCliente++;
			}
		};

		if (C is { } cli)
		{
			cli.ClashComecou += Comecou;
			cli.ClashTeclaPedida += Pediu;
			cli.ClashAcabou += Acabou;
			cli.SnapshotReceived += ContarSnapshot;
			cli.Falou += AoOuvir;
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
		// (2026-10-19) os dois botoes de defeito do quick time event so de WASD -- o `finally` de cada um ja os desliga;
		// isto e pro processo que morre no meio de uma familia.
		Jandirus.Core.Combat.LetrasDoEmbate.TecladoInteiroDeTeste = false;
		LocalPlayer.LetraDoQteAndaNaGuardaDeTeste = false;
		if (C is not { } cli) return;
		cli.ClashComecou -= Comecou;
		cli.ClashTeclaPedida -= Pediu;
		cli.ClashAcabou -= Acabou;
		cli.SnapshotReceived -= ContarSnapshot;
		cli.Falou -= AoOuvir;
	}

	/// <summary>(2026-10-19) Os avisos do servidor no chat -- a `F8` le neles o convite de sair de lado pelas setas.</summary>
	private readonly List<string> _avisos = [];

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _avisos.Add(texto);
	}

	/// <summary>
	/// QUANDO O EMBATE DE PE SE VENCE, em ticks de relogio do motor. O `Comecou` traz a duracao no
	/// pacote -- e e ela que diz se ainda cabe uma medida inteira dentro dele. Ver
	/// <see cref="GarantirEmbate"/>.
	/// </summary>
	private ulong _embateAte;

	private void Comecou(Protocol.TipoDeEmbate tipo, int a, int b, int ms, float meu, float dele)
	{
		_tipo = tipo;
		_embateAte = Time.GetTicksMsec() + (ulong)Math.Max(0, ms);
		Nota($"...o embate COMECOU ({tipo}, {ms} ms)");
	}

	private void Pediu(char c, int ms)
	{
		_letraPedida = c;
		_letras++;
		if (_contandoLetras) { _letrasVistas.Add(c); _contadas++; }
	}

	/// <summary>
	/// (2026-10-19) AS LETRAS QUE O SERVIDOR PEDIU NESTA RODADA, e quantas -- dos tres embates que ela arma (o Zanzo Clash, a
	/// colisao de ki e a disputa de guarda), conferidas no <see cref="Fechamento"/>. Fora da janela do botao do teclado
	/// inteiro (<see cref="SoOWasdResponde"/>), que pede letra de fora de proposito.
	/// </summary>
	private readonly HashSet<char> _letrasVistas = [];
	private int _contadas;
	private bool _contandoLetras = true;

	private void Acabou(int venc, int perd) { _fins++; _letraPedida = '\0'; _ultimoVenc = venc; _ultimoPerd = perd; }

	/// <summary>O par do ultimo `Acabou` que chegou -- a `F8` le nele a saida de lado (sem vencedor, e eu no lugar de quem saiu).</summary>
	private int _ultimoVenc, _ultimoPerd;

	// =====================================================================
	// AS DEZOITO TECLAS DO JOGADOR
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
		new("T  -- treinar", Key.T, false,
			() => TelaDeTreino.Instancia?.NaTela == true, () => TelaDeTreino.Instancia?.Fechar()),
		new("M  -- meditar", Key.M, false,
			() => TelaDeMeditacao.Instancia?.NaTela == true, () => Toque(Key.Escape)),
		new("K  -- alternar o golpe letal", Key.K, false, () => Saiu(Protocol.C2S.Lethal)),
		new("R  -- subir no ar", Key.R, true, () => BitDeInput(Protocol.InputSubir)),
		new("G  -- descer no ar", Key.G, true, () => BitDeInput(Protocol.InputDescer)),
		// ============================ A DEZOITO, E ELA ESTAVA FORA HA MUITO TEMPO ============================
		// O MENU DE PAUSA era o unico caminho de tecla do cliente que nao perguntava ao `Foco`, e esta
		// tabela nao o media: os `Toque(Key.Escape)` que ja apareciam aqui sao todos de FECHAR (a caixa
		// de fala, a meditacao), nunca de abrir. As 78 provas anteriores nunca encostaram nele.
		//
		// **POR ULTIMO NA TABELA, e nao e arrumacao**: o ESC e a tecla de DESFAZER de metade das linhas
		// acima (o `Fechar` do ENTER, do `/` e do M e um `Toque(Key.Escape)`). Posto no meio, um ESC que
		// escapasse do portao abriria a pausa por cima do resto da varredura e as linhas seguintes
		// mediriam teclas atras de uma tela preta.
		//
		// E O `Fechar` DELE E O PROPRIO ESC: fechar nunca e bloqueado (ver `PauseMenu._UnhandledInput`),
		// entao o gesto de saida e o mesmo de entrada -- e se um dia alguem tornar o portao SIMETRICO,
		// esta bancada trava com a tela preta aberta em vez de passar verde. Que e o que se quer.
		// ==============================================================================================
		new("ESC -- o menu de pausa", Key.Escape, false,
			() => TelaAberta("Pause"), () => Toque(Key.Escape)),
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
		_disparou = false;
		if (b.Segurar) Tecla(b.Tecla, true); else Toque(b.Tecla);

		// ============================ O RETRATO SAI COM A TECLA AINDA APERTADA ============================
		// **Achado rodando**: o microfone e um ESTADO enquanto a tecla esta em baixo (`Falando`), e a
		// primeira versao olhava depois de soltar -- lia falso e acusava o V de mudo num jogo em que
		// ele funcionava. As teclas de PACOTE sao o oposto: o pacote pode chegar ao servidor um quadro
		// depois. Entao sao DUAS fases e elas somam: o estado que so existe com a tecla em baixo, e o
		// pacote que so chega depois dela.
		//
		// ============================ E NENHUMA DAS DUAS E UM SONO ============================
		// Eram `yield return 0.30` e `yield return 0.10`, e essa janela de 0,40 s era o veredito de 55
		// das 81 provas -- ver `_voltasDoCliente` pro estrago. Agora cada fase sai no INSTANTE em que a
		// tecla dispara (verde de imediato, sem esperar relogio nenhum) e, quando ela NAO dispara, so
		// desiste depois de as duas estradas terem andado -- o que transforma o "ficou mudo" de palpite
		// em afirmacao.
		// ================================================================================
		foreach (double d in AteOFatoOuAsEstradas(b.Disparou, VoltasEmBaixo)) yield return d;
		_disparou = b.Disparou();

		if (b.Segurar) Tecla(b.Tecla, false);
		if (_disparou) yield break;   // ja sabemos a resposta: nao ha por que pagar a segunda fase

		foreach (double d in AteOFatoOuAsEstradas(b.Disparou, VoltasDepoisDeSoltar)) yield return d;
		_disparou = b.Disparou();
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
	/// ============================ QUANTO TEMPO DE EMBATE UMA MEDIDA PRECISA TER PELA FRENTE ============================
	/// **Achado sob carga, e ele produziu as duas especies de mentira ao mesmo tempo.** O
	/// `GarantirEmbate` so olhava se HAVIA embate -- nunca quanto faltava dele. Com a maquina afogada
	/// cada medida demora mais, e o embate (3,0 a 6,3 s) passou a se vencer NO MEIO delas:
	///
	///   * numa afirmacao NEGATIVA ("a tecla fica muda") isso e **VERMELHO FALSO** -- foi o
	///     `R -- subir no ar` da rodada 5, que disparou porque o silencio tinha acabado meio segundo
	///     antes, com o jogo perfeito;
	///   * numa afirmacao POSITIVA ("a guarda continua viva no embate") e pior: e **VERDE FALSO**. A
	///     bancada certificaria que o ALT sobe durante o embate tendo medido um cliente sem embate
	///     nenhum -- e ninguem jamais veria.
	///
	/// A CORRECAO E EXIGIR PRAZO, e o prazo esta no proprio pacote: o `Comecou` traz a duracao, e
	/// <see cref="_embateAte"/> guarda o instante do fim. Faltando menos que <paramref name="msMinimos"/>,
	/// este embate nao serve pra medir nada -- solta-se ele e arma-se outro inteiro.
	/// ==============================================================================================================
	/// </summary>
	private const int MsMinimosDeEmbate = 2500;

	/// <summary>
	/// GARANTE QUE HA UM EMBATE DE PE, do tipo pedido e com prazo pela frente -- e o que faz a varredura
	/// de dezoito teclas caber num encontro que dura de 3 a 6 segundos: quando o embate acaba (ou esta
	/// pra acabar) no meio dela, a bancada arma outro e continua de onde parou, em vez de medir o
	/// silencio de um embate que ja terminou.
	/// </summary>
	private IEnumerable<double> GarantirEmbate(bool deKi, int msMinimos = MsMinimosDeEmbate, double fracaoDoRival = 1)
	{
		if (C is not { } cli || Servidor is not { } srv) yield break;

		// ============================ E O TIPO TEM QUE SER O PEDIDO, NAO "ALGUM EMBATE" ============================
		// **Achado sob carga.** Este metodo so perguntava se HAVIA embate. So que o ZanzoClash tambem
		// nasce SOZINHO (melee mutuo com Afterimage) contra o rival de bancada, que continua brigando o
		// tempo todo -- entao a `F3` chegava com um embate de VELOCIDADE de pe, este metodo dava por
		// satisfeito, e a familia da colisao de ki media o embate errado. Na rodada em que isso
		// aconteceu ela nem mediu: caiu no `yield break` e levou 23 provas junto (70 no lugar de 96).
		// ======================================================================================================
		bool tipoCerto = deKi
			? _tipo != Protocol.TipoDeEmbate.Velocidade
			: _tipo == Protocol.TipoDeEmbate.Velocidade;

		// PRAZO CURTO DEMAIS (ou tipo errado) = NAO SERVE. Ver `MsMinimosDeEmbate`.
		if (cli.EmClash && (!tipoCerto || Time.GetTicksMsec() + (ulong)msMinimos >= _embateAte))
		{
			srv.SoltarEmbateDeTeste(cli.LocalId);
			foreach (double d in AteQue(() => !cli.EmClash, 4.0)) yield return d;
		}

		if (!cli.EmClash)
		{
			bool armou = deKi
				? srv.EmbateDeKiDeTeste(cli.LocalId)
				: srv.EmbateDeVelocidadeDeTeste(cli.LocalId, fracaoDoRival);
			if (!armou) { Nota("  --     o servidor NAO armou o embate"); yield break; }

			// ESPERA O PACOTE, e nao um numero de quadros: quem diz que o embate comecou e o `Comecou`
			// que chega pela rede -- que e a mesma coisa que faria um embate de verdade comecar.
			for (int i = 0; i < 120 && !cli.EmClash; i++) yield return 0.05;
		}

		// ============================ E ARREMESSADO NAO SE MEDE TECLA NENHUMA ============================
		// Todo embate FECHA com uma pancada (`GolpeDeSaida`), e pancada ARREMESSA. Arremessado, o
		// `LocalPlayer._Process` RETORNA no `if (_empurrado)` (linha 653) antes de ler uma unica tecla
		// e antes de mandar `InputState` -- ou seja, TODA sonda fica muda e nenhum bit sai no fio, com
		// o teclado perfeito. Medir ali daria verde em qualquer familia de silencio, de graca.
		// ========================================================================================
		foreach (double d in AteQue(() => C?.Sheet.Empurrado != true, 6.0)) yield return d;
	}

	/// <summary>
	/// UMA TECLA MEDIDA DENTRO DE UM EMBATE, **com a premissa conferida no fim**. O
	/// <see cref="GarantirEmbate"/> exige prazo ANTES; esta confere que ele realmente sobrou DEPOIS, e
	/// refaz a medida quando nao sobrou. Uma medida em que a premissa caiu nao e uma falha: e uma
	/// medida que nao aconteceu, e trata-la como falha e o vermelho falso que a rodada 5 mostrou.
	/// </summary>
	private IEnumerable<double> ApertarNoEmbate(Botao b, bool deKi)
	{
		for (int tentativa = 0; tentativa < 4; tentativa++)
		{
			foreach (double d in GarantirEmbate(deKi)) yield return d;
			foreach (double d in Apertar(b)) yield return d;
			bool valeu = C?.EmClash == true;

			// FECHA O QUE ABRIU ANTES DE QUALQUER COISA -- inclusive antes de refazer: a proxima
			// tentativa mediria a tecla seguinte por tras de uma tela aberta.
			if (_disparou) { b.Fechar?.Invoke(); yield return 0.12; }
			if (valeu) yield break;

			Nota($"  --     o embate venceu no meio de \"{b.Rotulo}\": refazendo");
		}
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
		foreach (double d in F6_OQueOEscNaoQuebrou()) yield return d;
		foreach (double d in F7_ATelaDePausaDentroDoJogo()) yield return d;
		foreach (double d in F8_AGuardaAndaEOPassoDeLadoSolta()) yield return d;
		foreach (double d in F9_AFolgaDepoisDoFim()) yield return d;
		foreach (double d in Fechamento()) yield return d;
	}

	private IEnumerable<double> Fechamento()
	{
		if (C is { } cli) Servidor?.SoltarEmbateDeTeste(cli.LocalId);
		Servidor?.LimparAMudezDeTeste();
		yield return 0.2;

		// (2026-10-19) O ALFABETO DA RODADA INTEIRA: *"faça os quick time event do jogo usar apenas wasd"* -- toda letra que o
		// servidor pediu, nos tres embates. O contra-exemplo e o do `SoOWasdResponde` (o teclado inteiro de antes).
		string vistas = string.Concat(_letrasVistas.OrderBy(l => l));
		Checa($"o servidor so pediu W, A, S ou D: {_contadas} letras nesta rodada, todas de \"{vistas}\"",
			  _contadas >= 4 && _letrasVistas.All(l => "WASD".IndexOf(l) >= 0), vistas);

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
	// F0 -- FORA DO EMBATE: AS DEZOITO ABREM
	// =====================================================================
	/// <summary>
	/// O CONTRA-EXEMPLO, QUE E O JOGO DE ANTES. Sem ele, calar as teclas PRA SEMPRE passaria verde na
	/// familia seguinte -- e "nenhum menu abriu" e verdade tambem num cliente com o teclado morto.
	/// </summary>
	private IEnumerable<double> F0_ForaDoEmbate()
	{
		Nota("--- F0: FORA do embate as dezoito teclas ABREM (o contra-exemplo) ---");
		Checa("nao ha embate nenhum de pe (senao esta familia mede a outra)", C?.EmClash != true);

		// O MOVIMENTO, DUAS VEZES: a INTENCAO que sai no fio e a POSICAO que o corpo alcanca. A
		// segunda existe pra a primeira nao poder ser vacua -- um bit ligado num corpo que nao anda
		// nao prova nada, e e a posicao que o dono ve.
		Vector2 antes = World.Instancia?.PosicaoLocal ?? default;
		float Andou() => (World.Instancia?.PosicaoLocal ?? default).DistanceTo(antes);

		Marcar();
		Tecla(Key.D, true);
		// PELO FATO E NAO POR 0,7 s: as DUAS afirmacoes desta familia (o bit no fio e os 8 px no chao)
		// sao o que se espera, e a tecla so e solta quando as duas ja aconteceram. Numa maquina ociosa
		// isto sai em dois quadros; numa maquina afogada ele espera o quanto for -- que e a diferenca
		// entre medir o jogo e medir a carga do PC.
		foreach (double d in AteQue(() => BitDeInput(Protocol.InputAndando) && Andou() > 8)) yield return d;
		bool pediuAndar = BitDeInput(Protocol.InputAndando);
		float andou = Andou();
		Tecla(Key.D, false);
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

		// O CONTRA-EXEMPLO DAS DUAS QUE **NAO** SE CALAM. Sem estas duas linhas, "a guarda continua
		// viva no embate" seria verde tambem num cliente onde o ALT nunca funcionou -- exatamente a
		// mesma armadilha que fez esta familia F0 existir pras dezoito da tabela. Elas vem ANTES da
		// varredura porque a varredura mexe na tela (abre mochila, chat, meditacao) e a guarda so sobe
		// com o corpo parado e sem campo de texto em foco.
		foreach (double d in AGuardaSobeEDesce("FORA do embate")) yield return d;
		foreach (double d in OSocoSai("FORA do embate")) yield return d;

		foreach (double d in Varrer("FORA do embate ABRE:", esperado: true)) yield return d;

		// A ATIVIDADE VOLTA PRO LUGAR: o T ligou o treino, e treinar durante o resto da bancada
		// mudaria o BP no meio das medidas.
		C?.SendActivity(Protocol.Activity.Parado);
		yield return 0.2;
	}

	// =====================================================================
	// F1 -- DURANTE O ZANZO CLASH: AS DEZOITO CALAM
	// =====================================================================
	private IEnumerable<double> F1_DuranteOZanzoClash()
	{
		Nota("--- F1: durante o ZANZO CLASH as dezoito ficam MUDAS ---");

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		Checa("o ZANZO CLASH esta de pe (o `Comecou` chegou pelo fio)",
			  C?.EmClash == true && _tipo == Protocol.TipoDeEmbate.Velocidade, $"tipo {_tipo}");
		Checa("...e o cliente sabe que os atalhos estao mudos", Foco.AtalhosMudos);

		// AS DEZOITO, uma linha cada -- rearmando o embate se ele acabar (ou estiver pra acabar) no
		// meio da varredura. Ver `ApertarNoEmbate`.
		foreach (Botao b in Botoes())
		{
			foreach (double d in ApertarNoEmbate(b, deKi: false)) yield return d;
			bool disparou = _disparou;
			Checa($"NO ZANZO CLASH fica mudo: {b.Rotulo}", !disparou, disparou ? "DISPAROU" : "mudo");
		}

		// ---- O QUICK TIME EVENT CONTINUA VIVO ----
		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		foreach (double d in ALetraChega("NO ZANZO CLASH")) yield return d;

		// ---- (2026-10-19) ...E SO O WASD RESPONDE ----
		foreach (double d in SoOWasdResponde("NO ZANZO CLASH")) yield return d;

		// ---- O MOVIMENTO ----
		//
		// ============================ O EMBATE TEM QUE ESTAR DE PE **NO FIM** DA JANELA TAMBEM ============================
		// **Achado rodando, e ele reprovou uma rodada com o jogo intacto.** O `GarantirEmbate` so
		// arma um embate NOVO quando nao ha nenhum -- ele nao olha quanto falta pro atual. Chegando aqui
		// depois da varredura das dezoito e da letra, o embate de pe pode estar nos ultimos 100 ms: ele
		// vence NO MEIO da janela de medicao, o D que esta em baixo volta a valer, o corpo anda, o bit
		// sobe e a linha cai acusando um vazamento que nao existe.
		//
		// A CORRECAO E CONFERIR A PREMISSA NO FIM E REFAZER A MEDIDA, e nao alargar tolerancia: o que se
		// afirma e "com o embate de pe o cliente nao pede passo", entao uma medida em que o embate NAO
		// ficou de pe nao e uma falha -- e uma medida que nao aconteceu.
		// ==============================================================================================================
		bool pediu = true;
		for (int tentativa = 0; tentativa < 4; tentativa++)
		{
			foreach (double d in GarantirEmbate(deKi: false)) yield return d;
			Marcar();
			Tecla(Key.D, true);
			// AFIRMACAO NEGATIVA => ESTRADA PROVADA. Aqui nao ha fato a esperar: o que se afirma e que o
			// bit NAO aparece. Com o sono de 0,6 s um quadro faminto dava VERDE FALSO -- a bancada
			// certificaria um movimento travado que nunca foi travado. Agora ela le depois de sete
			// voltas contadas.
			foreach (double d in AsEstradasAndaram(VoltasEmBaixo + VoltasDepoisDeSoltar)) yield return d;
			pediu = BitDeInput(Protocol.InputAndando);
			bool valeu = C?.EmClash == true;   // o embate sobreviveu a janela inteira?
			Tecla(Key.D, false);
			yield return 0.1;
			if (valeu) break;
			Nota("  --     o embate venceu no meio da medida do movimento: refazendo");
		}
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
		// AFIRMACAO POSITIVA => ESPERA PELO FATO. Sai no quadro em que o byte chega.
		foreach (double d in AteQue(() => Saiu(Protocol.C2S.ClashTecla))) yield return d;
		Checa($"{prefixo}: a letra do quick time event CONTINUA saindo (a tecla '{pedida}' chegou ao servidor)",
			  Saiu(Protocol.C2S.ClashTecla));

		// ---- A INJECAO ----
		//
		// ============================ AQUI O FATO PRECISOU SER INVENTADO ============================
		// Este era o unico dos treze pontos de sono que nao tinha sinal nenhum a esperar: abrir o menu,
		// ir pra aba "Admin", achar um `LineEdit` e chamar `GrabFocus` sao quatro gestos e nenhum deles
		// anuncia que terminou. Trocar o sono por um laco de poll no MESMO criterio nao teria adiantado
		// nada -- o criterio e que faltava.
		//
		// Os dois fatos NOVOS sao estes: (1) a aba montou quando existe um `LineEdit` debaixo do menu, e
		// (2) o foco pegou quando o `Foco.Digitando` vira verdadeiro -- que e, alias, exatamente a
		// condicao que a injecao vai usar como premissa duas linhas abaixo. Se ela nunca virar, o
		// para-quedas abre e a linha sai "PASSOU BATIDO" com `digitando=false` escrito no detalhe, que
		// e um diagnostico e nao um silencio.
		// =====================================================================================
		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara("Admin");

		// TETO CURTO NOS TRES: sao gestos LOCAIS (montar aba, pegar foco) e a proxima letra de um
		// cruzamento vem em 500-700 ms. O para-quedas de 8 s e pra rede, nao pra tela -- aqui ele so
		// faria a bancada demorar 24 s pra dizer o que ela ja sabe.
		LineEdit? campo = null;
		foreach (double d in AteQue(() =>
				 (campo = MenuJogo.Instancia is { } m ? Procurar<LineEdit>(m, _ => true) : null) != null, 3.0))
			yield return d;

		campo?.GrabFocus();
		foreach (double d in AteQue(() => Foco.Digitando, 3.0)) yield return d;

		bool digitando = Foco.Digitando;
		foreach (double d in AteQue(() => _letraPedida != '\0', 3.0)) yield return d;
		Marcar();
		Toque(TeclaDaLetra(_letraPedida == '\0' ? pedida : _letraPedida));
		// AFIRMACAO NEGATIVA (a letra NAO deve sair com o campo em foco) => ESTRADA PROVADA.
		foreach (double d in AteOFatoOuAsEstradas(() => Saiu(Protocol.C2S.ClashTecla),
												  VoltasEmBaixo + VoltasDepoisDeSoltar))
			yield return d;
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

	/// <summary>
	/// (2026-10-19) O QUICK TIME EVENT E SO WASD -- *"faça os quick time event do jogo usar apenas wasd e nao o teclado todo pq
	/// a galera ta reclamando muito"*. Duas pontas, uma lista (`LetrasDoEmbate`): o servidor so pede W, A, S ou D (as letras
	/// da rodada inteira sao conferidas no <see cref="Fechamento"/>), e a tela so manda W, A, S ou D -- uma letra de fora (o
	/// H, que nao e atalho de nada) e engolida e NAO vai pro servidor: nao e resposta, e nao custa o ponto do erro.
	///
	/// O CONTRA-EXEMPLO E O TECLADO INTEIRO DE ANTES (`LetrasDoEmbate.TecladoInteiroDeTeste`), e ele vale nas duas pontas: o
	/// servidor volta a pedir letra de fora do WASD, e o mesmo H volta a ir pro servidor como resposta. As letras pedidas
	/// com o botao ligado nao entram na conta da rodada: o embate em que elas nasceram acaba aqui, e a conta volta no proximo.
	/// </summary>
	private IEnumerable<double> SoOWasdResponde(string prefixo)
	{
		// (a) O JOGO DE AGORA. Refeita se o embate vencer no meio: com ele acabado o H tambem nao sairia, e o verde seria
		//     de graca (a mesma premissa conferida no fim do `ApertarNoEmbate`).
		bool hSaiu = true, valeu = false;
		for (int tentativa = 0; tentativa < 4 && !valeu; tentativa++)
		{
			foreach (double d in GarantirEmbate(deKi: false)) yield return d;
			foreach (double d in AteQue(() => _letraPedida != '\0', 3.0)) yield return d;
			if (tentativa == 0) { yield return 0.15; Fotografar("user://mudez-1-wasd.png"); }   // o anel com a letra (so com janela)
			Marcar();
			Toque(Key.H);
			// AFIRMACAO NEGATIVA => ESTRADA PROVADA.
			foreach (double d in AteOFatoOuAsEstradas(() => Saiu(Protocol.C2S.ClashTecla), VoltasEmBaixo + VoltasDepoisDeSoltar))
				yield return d;
			hSaiu = Saiu(Protocol.C2S.ClashTecla);
			valeu = C?.EmClash == true;
			if (!valeu) Nota("  --     o embate venceu no meio da medida do H: refazendo");
		}
		Checa($"{prefixo}: uma letra FORA do WASD (o H) nao e resposta -- a tela a engole e ela NAO vai pro servidor",
			  valeu && !hSaiu, $"em embate {valeu}, H no fio {hSaiu}");

		// (b) O CONTRA-EXEMPLO: o teclado inteiro de antes.
		_contandoLetras = false;
		char pedidaDeAntes = '\0';
		bool hSaiuDeAntes = false;
		Jandirus.Core.Combat.LetrasDoEmbate.TecladoInteiroDeTeste = true;
		try
		{
			foreach (double d in GarantirEmbate(deKi: false)) yield return d;
			// A LETRA QUE ESTAVA NA TELA PODE TER NASCIDO ANTES DO BOTAO; a seguinte, nao. Espera-se uma de fora do WASD -- o
			// que o jogo de agora nunca pede, e por isso so o botao a faz chegar.
			foreach (double d in AteQue(() => _letraPedida != '\0' && "WASD".IndexOf(_letraPedida) < 0, 4.0)) yield return d;
			pedidaDeAntes = _letraPedida;
			Marcar();
			Toque(Key.H);
			foreach (double d in AteQue(() => Saiu(Protocol.C2S.ClashTecla), 3.0)) yield return d;
			hSaiuDeAntes = Saiu(Protocol.C2S.ClashTecla);
		}
		finally { Jandirus.Core.Combat.LetrasDoEmbate.TecladoInteiroDeTeste = false; }
		Injeta($"{prefixo}: (o teclado inteiro de antes) o servidor volta a pedir letra FORA do WASD",
			   pedidaDeAntes != '\0' && "WASD".IndexOf(pedidaDeAntes) < 0, $"pediu '{pedidaDeAntes}'");
		Injeta($"{prefixo}: (o teclado inteiro de antes) o MESMO H volta a ir pro servidor como resposta", hSaiuDeAntes);

		// (o `Acabou` vem pelo mesmo canal ordenado das letras: depois dele nao chega mais nenhuma deste embate)
		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		foreach (double d in AteQue(() => C?.EmClash != true, 4.0)) yield return d;
		_contandoLetras = true;
	}

	// =====================================================================
	// O QUE O PORTAO **NAO** PODE CALAR
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS TECLAS QUE FICARAM DE FORA DA TABELA ============================
	/// A GUARDA e o SOCO estao fora do <see cref="Botoes"/> **de proposito** -- o comentario da tabela
	/// diz isso desde sempre --, e ate agora "de proposito" era so uma frase: nenhuma linha media que
	/// elas continuam vivas. Uma tabela de dezoito mudas nao distingue *"calei o que o dono pediu"* de
	/// *"calei o teclado"*, e a segunda e literalmente o defeito que ja foi cobrado em maiusculas
	/// (*"pedi pra tu consertar a aura PQ TU QUEBROU A MOVIMENTACAO"*).
	///
	/// E A GUARDA NAO E DETALHE NA COLISAO DE KI: soltar o ALT **ENCERRA** o lado de quem segura o
	/// feixe com as maos (`LadoOk`). Um portao que alcancasse esta leitura nao "desabilitaria uma
	/// hotkey" -- ele faria o jogador PERDER a disputa de ki por ter o embate de pe. Por isso ela e
	/// medida DENTRO dos dois embates, e nao so fora.
	///
	/// O PACOTE E A MEDIDA, como no resto desta bancada: `LocalPlayer` so manda o `Guard` na
	/// TRANSICAO (`if (guardaAgora != _guarda)`), entao a subida e a descida sao dois fatos distintos
	/// e os dois sao afirmados. A descida tambem e a FAXINA: sem ela a proxima medicao nasceria com a
	/// guarda ja em cima e nao teria transicao pra mandar -- ficaria vermelha com o jogo intacto.
	/// ==================================================================================================
	/// </summary>
	private IEnumerable<double> AGuardaSobeEDesce(string prefixo)
	{
		Marcar();
		Tecla(Key.Alt, true);
		foreach (double d in AteOFatoOuAsEstradas(() => Saiu(Protocol.C2S.Guard), VoltasEmBaixo)) yield return d;
		bool subiu = Saiu(Protocol.C2S.Guard);

		// SEM `yield` ENTRE A TECLA E O `Marcar`: nenhum quadro corre no meio, entao o pacote da
		// DESCIDA so pode nascer depois desta marca -- e o fio comeca vazio de verdade.
		Tecla(Key.Alt, false);
		Marcar();
		foreach (double d in AteOFatoOuAsEstradas(() => Saiu(Protocol.C2S.Guard), VoltasDepoisDeSoltar))
			yield return d;
		bool desceu = Saiu(Protocol.C2S.Guard);

		Checa($"{prefixo}: a GUARDA (ALT) SOBE -- o pacote `Guard` chega ao servidor", subiu,
			  subiu ? "" : "nenhum `Guard` no fio");
		Checa($"{prefixo}: ...e DESCE ao soltar o ALT (e e por isso que o portao nao pode alcanca-la)",
			  desceu, desceu ? "" : "nenhum `Guard` no fio");
	}

	/// <summary>
	/// ============================ QUANTAS VOLTAS O SOCO PRECISA, E POR QUE E MAIS QUE AS OUTRAS ============================
	/// **Achado rodando, e ele quase virou uma prova moeda-ao-ar.** No ZanzoClash cada cruzamento manda
	/// um VISLUMBRE (`World.AoVislumbre`), e o vislumbre chama `PosarDeGolpe(AttackPoseMs)` -- 240 ms de
	/// relogio de pose no corpo LOCAL, uma vez a cada 500..700 ms de cruzamento
	/// (`GameServer.ZanzoClash.cs:52`). E a trava "UM soco por vez" (`_ataqueAte <= 0`, `LerAcoes`) le
	/// esse MESMO relogio.
	///
	/// Ou seja: durante o ZanzoClash o espaco cai dentro da pose de golpe em 34 a 48% das vezes, e um
	/// toque unico com janela de sete voltas (~230 ms) reprovava sem defeito nenhum -- foi o que a
	/// primeira rodada desta familia mostrou, e e exatamente a especie de prova instavel que esta
	/// bancada acabou de terminar de exterminar.
	///
	/// A CORRECAO NAO E ESPERAR MAIS: e INSISTIR, um toque por quadro, e dar um limite que passe por
	/// cima de uma pose inteira -- 14 voltas a 30 Hz sao ~470 ms, contra os 240 ms da pose mais longa.
	/// E ela NAO afrouxa o que a prova pega: se alguem pendurar a leitura do soco no
	/// `Foco.AtalhosMudos` (o portao), marteladas nao adiantam nada e a linha cai igual.
	/// ======================================================================================================================
	/// </summary>
	private const int VoltasDoSoco = 14;

	/// <summary>
	/// O SOCO. Fora da tabela pelo motivo escrito la ("o espaco nao e letra, e o corpo ja esta preso
	/// pelo `Stun`") -- e o que se mede aqui e o CLIENTE ainda pedindo, nao o servidor obedecendo: quem
	/// decide se o golpe acerta, se o corpo esta atordoado e se ha alvo e o servidor, e sempre foi.
	/// </summary>
	private IEnumerable<double> OSocoSai(string prefixo)
	{
		Marcar();
		int cliente = VoltasDoCliente() + VoltasDoSoco;
		int servidor = _voltasDoServidor + VoltasDoSoco;
		ulong fim = Time.GetTicksMsec() + (ulong)(TetoDaEspera * 1000);

		// UM TOQUE POR QUADRO ate o pacote sair. `IsActionJustPressed` vale por UM quadro: um toque
		// engolido pela pose de golpe esta perdido pra sempre, e nao ha o que esperar dele. Ver
		// `VoltasDoSoco`.
		while (!Saiu(Protocol.C2S.Action)
			   && (VoltasDoCliente() < cliente || _voltasDoServidor < servidor)
			   && Time.GetTicksMsec() < fim)
		{
			Toque(Key.Space);
			yield return 0;
		}

		bool socou = Saiu(Protocol.C2S.Action);
		Checa($"{prefixo}: o SOCO (espaco) continua saindo -- o pacote `Action` chega ao servidor",
			  socou, socou ? "" : "nenhum `Action` no fio depois de 14 voltas martelando");
	}

	/// <summary>
	/// ============================ O ANDAR **DEPOIS** DE UM EMBATE, E POR QUE ELE PRECISA DE DOIS RUMOS ============================
	/// **Achado rodando, e ele reprovou uma rodada com o jogo intacto.** O bit `InputAndando` nao e "a
	/// tecla esta em baixo": e `(_pos - antes).LengthSquared > 0.01f` (`LocalPlayer.cs:698`), com o
	/// comentario ao lado dizendo *"empurrando a parede o personagem fica parado de pe"*. O bit e os
	/// pixels sao **o mesmo fato**, nao duas medidas independentes.
	///
	/// E o ZanzoClash ANDA COM A BRIGA pelo mapa: a cada cruzamento ele sorteia um ponto novo e so
	/// garante que as celulas DOS DOIS estao livres (`GameServer.ZanzoClash.cs:790`) -- a celula
	/// vizinha, que e pra onde a bancada quer dar o passo, pode ser parede. Foi o que aconteceu: `+0 px
	/// no eixo X` com o teclado perfeito, porque o embate largou o corpo encostado em alguma coisa.
	///
	/// OS QUATRO RUMOS RESOLVEM SEM AFROUXAR NADA. A afirmacao e *"o movimento voltou"*, e o embate
	/// larga o corpo encostado em coisas: parede de um lado, e o CORPO DO ADVERSARIO do outro -- o
	/// vencedor nasce *atras* do perdedor (`GameServer.ZanzoClash.cs:994`) e corpo tambem e colisao no
	/// cliente (`MoveRules.Advance` com `Vizinhanca`, sem nenhum empurrao de desencaixe depois). Dois
	/// rumos ja reprovaram uma rodada com o jogo intacto. E o SINAL continua exigido em cada tentativa
	/// (D soma em X, A subtrai, S soma em Y, W subtrai): e o que impede uma correcao de posicao do
	/// servidor de pagar por um passo que o teclado nao deu.
	///
	/// E O VEREDITO E DIVIDIDO EM DOIS, pelo motivo escrito la embaixo, no `Checa`: quem responde "o
	/// portao quebrou o andar?" e o CLIENTE estar livre, e nao o chao estar livre.
	///
	/// E O "ANTES" SO E MARCADO DEPOIS DAS ESTRADAS ANDAREM: a correcao do ultimo salto do embate ainda
	/// esta atravessando o fio quando ele acaba, e marcar em cima dela creditava ao teclado uma
	/// distancia que o teclado nao andou (532 px numa rodada, 9 px na outra, pro MESMO gesto).
	/// ==============================================================================================================================
	/// </summary>
	private IEnumerable<double> OCorpoAndaDeNovo(string prefixo)
	{
		// ============================ O EMBATE FECHA COM UMA PANCADA, E PANCADA ARREMESSA ============================
		// **Achado sob carga**: os quatro rumos davam `0,0 px` e o bit nunca subia -- nao por parede
		// nenhuma, mas porque o `GolpeDeSaida` do proprio embate ainda estava jogando o corpo. E
		// arremessado o `LocalPlayer._Process` RETORNA no `if (_empurrado)` (linha 653) antes de ler
		// uma tecla e antes de mandar `InputState`.
		//
		// Esperar o arremesso acabar nao afrouxa a afirmacao -- ELA CONTINUA SENDO "o movimento
		// volta": so deixa de cobrar o passo de um corpo que, por desenho, ainda esta no ar.
		// ======================================================================================================
		foreach (double d in AteQue(() => C?.Sheet.Empurrado != true, 6.0)) yield return d;

		// ============================ E O NOCAUTE TAMBEM PARA O CORPO, POR OUTRA PORTA ============================
		// `_caido = ficha.Imobilizado` (`LocalPlayer.cs:327`), e caido o vetor de andar vira
		// `Vector2.Zero` -- outra vez os quatro rumos em 0,0 px com o teclado perfeito. O
		// `GolpeDeSaida` que fecha o embate pode nocautear, e o corpo levanta sozinho quando o prazo
		// do nocaute vence.
		// ====================================================================================================
		foreach (double d in AteQue(() => C?.Sheet.Imobilizado != true, 12.0)) yield return d;

		foreach (double d in AsEstradasAndaram(VoltasEmBaixo + VoltasDepoisDeSoltar)) yield return d;

		bool pediu = false;
		float andou = 0;
		string rumo = "", tentados = "";

		// ============================ O MOTIVO SE COLHE **DURANTE**, NAO DEPOIS ============================
		// A primeira versao lia o `PorQueNaoAnda` depois de soltar a tecla, e ele saia vazio ("nada o
		// prende") numa linha vermelha -- porque o que prendia o corpo era transitorio (o rival de
		// bancada continua brigando ali do lado, e cada golpe dele arremessa de novo) e ja tinha
		// passado quando a bancada foi perguntar. Um diagnostico colhido fora do instante do defeito
		// nao diagnostica nada.
		// ============================================================================================
		var motivos = new HashSet<string>();
		float colado = float.MaxValue;

		foreach ((Key tecla, string nome, Vector2 eixo) in new[]
		{
			(Key.D, "direita",  new Vector2(+1, 0)),
			(Key.A, "esquerda", new Vector2(-1, 0)),
			(Key.S, "baixo",    new Vector2(0, +1)),
			(Key.W, "cima",     new Vector2(0, -1)),
		})
		{
			Vector2 daqui = World.Instancia?.PosicaoLocal ?? default;
			float Delta() => ((World.Instancia?.PosicaoLocal ?? default) - daqui).Dot(eixo);

			Marcar();
			Tecla(tecla, true);
			// TETO CURTO DE PROPOSITO (2 s): cobrir 8 px e coisa de dois quadros mesmo numa maquina
			// afogada, e o rumo bloqueado tem que desistir rapido pra o proximo ter vez. O para-quedas
			// de 8 s aqui so faria a bancada demorar 32 s pra dizer o que ela ja sabe.
			ulong fim = Time.GetTicksMsec() + 2000;
			while (!(BitDeInput(Protocol.InputAndando) && Delta() > 8) && Time.GetTicksMsec() < fim)
			{
				if ((World.Instancia?.PorQueOCorpoNaoAnda ?? "") is { Length: > 0 } m) motivos.Add(m);
				// E QUANDO NADA O PRENDE, QUEM RECUSA O PASSO E O MUNDO. Corpo alheio colado e
				// indistinguivel de parede daqui -- ver `World.DistanciaDoCorpoMaisPertoDeTeste`.
				colado = Math.Min(colado, World.Instancia?.DistanciaDoCorpoMaisPertoDeTeste ?? float.MaxValue);
				yield return 0;
			}

			pediu = BitDeInput(Protocol.InputAndando);
			andou = Delta();
			rumo = nome;
			tentados += $"{nome} {andou:0.0}px; ";
			Tecla(tecla, false);
			yield return 0.2;

			if (pediu && andou > 8) break;
		}

		bool andouMesmo = pediu && andou > 8;
		string chao = tentados.TrimEnd()
			+ $"  [corpo alheio mais perto: {(colado == float.MaxValue ? "nenhum" : $"{colado:0} px")}]";

		// ============================ AS DUAS PERGUNTAS SAO DUAS, E SO UMA E DO PORTAO ============================
		// Isto era UMA afirmacao ("o corpo anda") e ela piscava vermelha sob carga em ~40% das rodadas.
		// Tres palpites depois -- parede, arremesso, nocaute --, o diagnostico colhido DURANTE a
		// tentativa fechou a conta: **em toda falha o cliente estava livre** ("nada o prendeu"), e o
		// corpo alheio mais perto chegou a estar a 558 px. Ou seja, o teclado pedia o passo e quem o
		// recusava era o MUNDO (o embate caminha pelo mapa e larga os dois onde calhar -- agua, quina,
		// beirada), num ponto do jogo que nao tem nada a ver com hotkey nenhuma.
		//
		// ENTAO SAO DUAS LINHAS, e a divisao nao perde nada:
		//
		//   1. **A do portao, e ela e DURA**: nada no CLIENTE esta prendendo o corpo. Um portao que
		//      congelasse o andar -- que e a queixa em maiusculas do dono -- NAO TEM COMO passar por
		//      aqui: `PorQueNaoAnda` e a mesma e unica expressao que zera o vetor no `_Process`, entao
		//      um congelamento novo aparece nela pelo nome, sempre, sem depender de haver chao livre.
		//   2. **A do chao**: o corpo cobre distancia num dos quatro rumos. Ela so REPROVA quando o
		//      cliente estava preso -- porque ai a culpa e do cliente e a linha 1 ja disse qual foi.
		//      Mundo recusando passo vira nota no log, com a distancia medida, e nao um vermelho que
		//      nao quer dizer nada. Uma prova que pisca e uma prova que ninguem le.
		// ====================================================================================================
		Checa($"{prefixo}: nada no CLIENTE prende o corpo (o vetor de andar voltou a valer)",
			  motivos.Count == 0,
			  motivos.Count == 0 ? "" : $"PRESO POR: {string.Join(" / ", motivos)}");

		Checa($"{prefixo}: ...e o corpo SAI DO LUGAR, no eixo da tecla apertada",
			  andouMesmo || motivos.Count == 0,
			  andouMesmo ? $"{andou:0} px pra {rumo}"
						 : $"o MUNDO recusou o passo nos quatro rumos (o cliente estava livre) -- {chao}");
	}

	/// <summary>
	/// A LETRA, SEM A INJECAO. O <see cref="ALetraChega"/> completo abre o menu do jogo pra provar que o
	/// embate nao pode morar no `Foco.Digitando`; aqui a pergunta e outra e mais estreita -- *depois do
	/// gesto do ESC*, a letra ainda sai? --, e abrir menu no meio seria trocar o que se mede.
	/// </summary>
	private IEnumerable<double> ALetraAindaSai(string prefixo)
	{
		foreach (double d in AteQue(() => _letraPedida != '\0', 3.0)) yield return d;
		char pedida = _letraPedida;
		if (pedida == '\0') { Checa($"{prefixo}: o servidor esta pedindo letra", false); yield break; }

		Marcar();
		Toque(TeclaDaLetra(pedida));
		foreach (double d in AteOFatoOuAsEstradas(() => Saiu(Protocol.C2S.ClashTecla),
												  VoltasEmBaixo + VoltasDepoisDeSoltar))
			yield return d;
		Checa($"{prefixo}: a letra '{pedida}' do quick time event CONTINUA chegando ao servidor",
			  Saiu(Protocol.C2S.ClashTecla));
	}

	// =====================================================================
	// F2 -- O FIM DEVOLVE OS ATALHOS
	// =====================================================================
	private IEnumerable<double> F2_OFimDevolve()
	{
		Nota("--- F2: acabado o embate, as teclas VOLTAM ---");

		// (2026-10-18) COM O RIVAL FRACO: no desempate do 0x0 o golpe de saida e do jogador, e o "o corpo anda de novo" mede um corpo de pe.
		foreach (double d in GarantirEmbate(deKi: false, fracaoDoRival: 0.2)) yield return d;
		int fins = _fins;
		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		for (int i = 0; i < 80 && _fins == fins; i++) yield return 0.05;

		Checa("o fim do embate chegou ao cliente (`Acabou`)", _fins > fins);
		foreach (double d in AteQue(() => C?.EmClash != true && !Foco.AtalhosMudos)) yield return d;
		Checa("...e o silencio caiu junto", C?.EmClash != true && !Foco.AtalhosMudos);

		foreach (double d in Apertar(Botoes()[0])) yield return d;
		bool abriu = MenuJogo.Instancia is { Visible: true };
		Checa("...e o P volta a abrir o menu", abriu);
		if (abriu) { MenuJogo.Instancia?.Fechar(); yield return 0.15; }

		// A SEGUNDA DAS DUAS QUE PISCAVAM VERMELHO. Era `Tecla(D) / 0,6 s / le`, e o bit tem que ser
		// produzido por um quadro de fisica, serializado e colhido pelo servidor dentro dessa janela --
		// hoje ela espera pelo BIT, que e o fato que ela afirma.
		//
		// E DEPOIS PASSOU A ESPERAR PELO RUMO CERTO TAMBEM: um unico rumo reprovava quando o embate
		// largava o corpo encostado numa parede (o bit E os pixels sao o mesmo fato -- ver
		// `OCorpoAndaDeNovo`, que e onde isso esta medido e explicado).
		foreach (double d in OCorpoAndaDeNovo("...e depois do fim do embate")) yield return d;
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
		Nota("--- F3: durante a COLISAO DE KI as dezoito ficam MUDAS ---");

		foreach (double d in GarantirEmbate(deKi: true)) yield return d;
		Checa("a COLISAO DE KI esta de pe (o gatilho dos feixes disparou sozinho, no tique)",
			  C?.EmClash == true && _tipo != Protocol.TipoDeEmbate.Velocidade, $"tipo {_tipo}");
		if (C?.EmClash != true) yield break;

		foreach (Botao b in Botoes())
		{
			foreach (double d in ApertarNoEmbate(b, deKi: true)) yield return d;
			bool disparou = _disparou;
			Checa($"NA COLISAO DE KI fica mudo: {b.Rotulo}", !disparou, disparou ? "DISPAROU" : "mudo");
		}

		foreach (double d in GarantirEmbate(deKi: true)) yield return d;
		foreach (double d in ALetraChega("NA COLISAO DE KI")) yield return d;

		// ---- E O QUE O PORTAO NAO ALCANCA, MEDIDO ONDE ELE MAIS CUSTARIA ----
		// A GUARDA AQUI E O CASO LIMITE do arquivo inteiro: e esta a disputa em que soltar o ALT
		// encerra o lado de quem segura o feixe. Se um dia alguem trocar o `Foco.Digitando` do
		// `LerAcoes` por `Foco.AtalhosMudos` "pra ficar igual aos outros", estas duas linhas caem.
		foreach (double d in GarantirEmbate(deKi: true)) yield return d;
		foreach (double d in AGuardaSobeEDesce("NA COLISAO DE KI")) yield return d;

		foreach (double d in GarantirEmbate(deKi: true)) yield return d;
		foreach (double d in OSocoSai("NA COLISAO DE KI")) yield return d;

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
		// O FATO E O `Comecou` CHEGANDO -- o mesmo pacote que um embate de verdade manda. Com o sono de
		// 0,25 s, um quadro faminto lia o `EmClash` antes de o anuncio atravessar o fio e reprovava a
		// unica familia que mede o prazo.
		foreach (double d in AteQue(() => C?.EmClash == true)) yield return d;
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
		// A FIXTURE E SINCRONA (o embate alheio comeca e acaba dentro da propria chamada), entao nao ha
		// pacote NENHUM a esperar -- e o que se afirma logo abaixo e que nada chegou. Estrada provada:
		// sete voltas do SERVIDOR pra ca depois do fim alheio, e so entao "o meu continua de pe".
		foreach (double d in AsEstradasAndaram(VoltasEmBaixo + VoltasDepoisDeSoltar)) yield return d;
		Checa("um embate ALHEIO comecou e acabou na minha zona", alheio);
		Checa("...e o MEU continua de pe, com os atalhos mudos", C?.EmClash == true && Foco.AtalhosMudos);

		foreach (double d in Apertar(Botoes()[0])) yield return d;
		Checa("...e o P continua sem abrir o menu", MenuJogo.Instancia is not { Visible: true });

		// ---- A INJECAO: o fim alheio chegando como o `AvisarZona` fazia chegar ----
		bool aindaMudo = C?.EmClash == true;
		Servidor?.FimAlheioComoZonaDeTeste(C?.LocalId ?? 0);
		// O DEFEITO INJETADO TEM QUE DERRUBAR O SILENCIO -- entao o fato esperado e a QUEDA. Se ela nao
		// vier, desiste-se depois das sete voltas e a linha sai "PASSOU BATIDO", que e o veredito certo.
		foreach (double d in AteOFatoOuAsEstradas(() => C?.EmClash != true,
												  VoltasEmBaixo + VoltasDepoisDeSoltar))
			yield return d;
		Injeta("o fim alheio mandado PRO JOGADOR (o `AvisarZona` de antes) derruba o silencio no meio "
			   + "do meu embate", aindaMudo && C?.EmClash != true,
			   $"antes mudo={aindaMudo}, depois mudo={C?.EmClash == true}");

		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		yield return 0.3;
	}

	// =====================================================================
	// F6 -- O QUE O PORTAO DO ESC **NAO** QUEBROU
	// =====================================================================
	/// <summary>
	/// ============================ A FAMILIA QUE MEDE O ESTRAGO QUE NAO HOUVE ============================
	/// As familias F1 e F3 provam que o ESC ficou MUDO. Isso, sozinho, e metade da pergunta: um portao
	/// que calasse o corpo inteiro tambem passaria verde nelas. A outra metade e esta -- **apertar o
	/// ESC de verdade, com o embate correndo, e so entao medir o resto do jogo**.
	///
	/// A ORDEM E O PONTO. O ESC vem PRIMEIRO e tudo o mais e medido DEPOIS dele: e a diferenca entre
	/// "a guarda funciona" (que a F0 ja diz) e "a guarda funciona **depois do gesto que foi
	/// bloqueado**". Um portao mal escrito -- um `SetInputAsHandled` no caminho recusado, um flag
	/// global que ficasse ligado, um `GetTree().Paused` -- passaria na F1 e cairia aqui.
	///
	/// E O ANDAR FECHA A FAMILIA, com as duas medidas que a F0 usa (o bit no fio E os pixels no chao),
	/// porque *"PQ TU QUEBROU A MOVIMENTACAO"* foi escrito assim, em maiusculas, por um dono que estava
	/// olhando pro corpo dele e nao pro protocolo. O congelamento DURANTE o embate e de propósito e e
	/// anterior a tudo isto (`LocalPlayer`, o vetor que vira `Vector2.Zero` no `EmClash`) -- o que esta
	/// familia afirma e que ele DESCONGELA por inteiro do outro lado.
	/// ==================================================================================================
	/// </summary>
	private IEnumerable<double> F6_OQueOEscNaoQuebrou()
	{
		Nota("--- F6: com o ESC apertado NO MEIO do embate, o resto do jogo continua inteiro ---");

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		if (C?.EmClash != true) { Checa("havia um embate de pe pra medir o ESC", false); yield break; }

		// O GESTO. Nao e simulacao de nada: e o mesmo `InputEventKey` que o teclado manda, no mesmo
		// `_UnhandledInput` do `PauseMenu` de producao.
		Toque(Key.Escape);
		// AFIRMACAO NEGATIVA ("a pausa NAO abriu") => ESTRADA PROVADA, como em toda a bancada.
		foreach (double d in AsEstradasAndaram(VoltasEmBaixo)) yield return d;
		bool abriu = TelaAberta("Pause");
		Checa("o ESC no meio do embate NAO abriu a tela de pausa", !abriu, abriu ? "ABRIU" : "mudo");
		// COM O DEFEITO INJETADO ELA ABRE, e a tela preta ficaria por cima do resto da familia. Fechar
		// aqui e o que faz as linhas seguintes continuarem medindo o jogo, e nao um vidro escuro.
		if (abriu) { Toque(Key.Escape); yield return 0.15; }

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		foreach (double d in AGuardaSobeEDesce("DEPOIS DO ESC, no ZANZO CLASH")) yield return d;

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		foreach (double d in OSocoSai("DEPOIS DO ESC, no ZANZO CLASH")) yield return d;

		foreach (double d in GarantirEmbate(deKi: false)) yield return d;
		foreach (double d in ALetraAindaSai("DEPOIS DO ESC, no ZANZO CLASH")) yield return d;

		// ---- E O ANDAR VOLTA INTEIRO DO OUTRO LADO DO EMBATE ----
		Servidor?.SoltarEmbateDeTeste(C?.LocalId ?? 0);
		foreach (double d in AteQue(() => C?.EmClash != true)) yield return d;
		Checa("o embate soltou e o silencio caiu junto", C?.EmClash != true);

		// A QUEIXA EM MAIUSCULAS DO DONO, MEDIDA NO CHAO -- e no chao mesmo, pelos pixels e pelo eixo
		// da tecla. Ver `OCorpoAndaDeNovo` pro que o embate faz com a posicao e por que sao dois rumos.
		foreach (double d in OCorpoAndaDeNovo("...e depois do ESC no meio do embate")) yield return d;
	}

	// =====================================================================
	// F7 -- A TELA DE PAUSA **DENTRO DO JOGO** CONTINUA SENDO A DE PAUSA
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA FAMILIA GUARDA ============================
	/// A `PauseMenu` deixou de ser so a tela de pausa: ela agora atende TAMBEM o lobby, e um unico
	/// metodo (`AjustarAoContexto`) decide qual das duas ela e, olhando pro `World.Instancia`. Isso e
	/// bom -- uma tela so, um lugar so que decide --, mas poe as duas telas na mesma linha de codigo:
	/// **quem mexer no lado do lobby pode trocar o lado do jogo sem ver.**
	///
	/// Uma bancada de lobby nao pega isso por construcao: la nao ha mundo, e o `NoMundo` responde
	/// `false` sempre. Esta bancada e a unica do projeto que tem um mundo de verdade na tela com um
	/// `PauseMenu` de producao em cima -- entao a metade "dentro do jogo" do contrato mora aqui.
	///
	/// As quatro afirmacoes sao as quatro que o `AjustarAoContexto` troca, e nada mais:
	/// o titulo, o rotulo do botao de voltar, o `Desconectar` VISIVEL e o botao de teclas VIVO.
	/// ==================================================================================
	/// </summary>
	// =====================================================================
	// F9 -- A FOLGA DEPOIS DO FIM (2026-10-18)
	// =====================================================================
	/// <summary>
	/// O DONO, DE NOVO: *"QTE contra seu clone na sua mente ao meditar profundamente, caso o botao q o jogo pede seja M vc acaba saindo da
	/// meditaçao, dnv vou pedir pra q quando tenha QTE o jogo apenas veja o input pro QTE e nada mais, pra n abrir menus e n fazer isso q ta
	/// acontecendo"*. As familias de cima mediam o silencio DURANTE o embate, e ele estava inteiro; o que vazava era o fim: o silencio caia
	/// junto com o `Acabou`, e a ultima letra -- apertada no fim, chegando depois dele -- ia pro jogo. Tres medidas:
	///   (a) logo depois do fim (o corpo ja livre), o P apertado nao abre o menu -- a FOLGA; passada a folga, ele volta a abrir;
	///   (b) DENTRO DA MENTE: o M apertado logo depois do fim de um embate NAO tira da meditacao profunda -- foto `mudez-9-mente.png`;
	///   (c) CONTRA-EXEMPLO (`GameClient.SemFolgaDoQteDeTeste`, o jogo de antes): o mesmo M, no mesmo instante, TIRA da mente -- foto
	///       `mudez-9-defeito.png`.
	/// </summary>
	private IEnumerable<double> F9_AFolgaDepoisDoFim()
	{
		Nota("--- F9: logo DEPOIS do fim do embate as teclas continuam mudas (a FOLGA), e o M na mente nao tira de la ---");
		if (C is not { } cli || Servidor is not { } srv) yield break;
		int eu = cli.LocalId;
		Botao p = Botoes()[0];

		// ---- (a) a folga, fora da mente ----
		foreach (double d in GarantirEmbate(deKi: false, fracaoDoRival: 0.2)) yield return d;
		int fins = _fins;
		srv.SoltarEmbateDeTeste(eu);
		foreach (double d in AteQue(() => _fins > fins, 4.0)) yield return d;
		Checa("(a) o embate acabou (`Acabou`): o corpo esta livre (`EmClash` caiu) e os atalhos continuam MUDOS -- a folga",
			  _fins > fins && !cli.EmClash && Foco.AtalhosMudos, $"EmClash {cli.EmClash}, mudos {Foco.AtalhosMudos}");
		foreach (double d in Apertar(p)) yield return d;
		bool vazou = _disparou;
		Fotografar("user://mudez-9-folga.png");
		if (vazou) { p.Fechar?.Invoke(); yield return 0.12; }
		Checa("...e o P apertado na folga NAO abre o menu: a letra atrasada nao vaza", !vazou, vazou ? "ABRIU" : "mudo");
		foreach (double d in AteQue(() => !Foco.AtalhosMudos, 4.0)) yield return d;
		Checa("...e a folga acaba sozinha", !Foco.AtalhosMudos);
		foreach (double d in Apertar(p)) yield return d;
		bool abriu = _disparou;
		if (abriu) { p.Fechar?.Invoke(); yield return 0.15; }
		Checa("...e passada a folga o P volta a abrir o menu", abriu);

		// ---- (b) dentro da mente ----
		// A BRIGA DOS EMBATES DE ANTES ACABA: *"nao da pra meditar no meio de uma briga"*. No jogo o dono medita em paz e o embate acontece LA DENTRO.
		srv.SemBrigaDeTeste(eu);
		yield return 0.2;
		cli.SendActivity(Protocol.Activity.Meditando);
		yield return 0.3;
		cli.SendHabilidade("mente");
		foreach (double d in AteQue(() => srv.NaMenteNoTeste(eu) && SaidaDaMente.NaMente, 10.0)) yield return d;
		bool dentro = srv.NaMenteNoTeste(eu) && SaidaDaMente.NaMente;
		Checa("(b) (preparo) o corpo mergulhou na mente (meditar + mergulhar, os pacotes da telinha)", dentro);
		if (!dentro) yield break;
		yield return 1.0;   // a mente assenta: o reflexo nasce, a tela troca de zona

		foreach (double d in OMNoFimDoEmbate()) yield return d;
		bool ficou = srv.NaMenteNoTeste(eu);
		Fotografar("user://mudez-9-mente.png");
		Checa("...e o M apertado logo depois do fim de um embate DENTRO da mente NAO tira da meditacao profunda -- *\"caso o botao q o jogo pede "
			  + "seja M vc acaba saindo da meditaçao\"*", ficou);

		// ---- (c) o contra-exemplo: o jogo de antes ----
		GameClient.SemFolgaDoQteDeTeste = true;
		bool saiu;
		try
		{
			foreach (double d in OMNoFimDoEmbate()) yield return d;
			saiu = !srv.NaMenteNoTeste(eu);
			Fotografar("user://mudez-9-defeito.png");
		}
		finally { GameClient.SemFolgaDoQteDeTeste = false; }
		Injeta("(o silencio cai junto com o `Acabou`, o jogo de antes) o MESMO M, no mesmo instante, TIRA o jogador da mente", saiu);

		if (srv.NaMenteNoTeste(eu)) cli.SendHabilidade("sairdamente");
		foreach (double d in AteQue(() => !srv.NaMenteNoTeste(eu), 6.0)) yield return d;
		cli.SendActivity(Protocol.Activity.Parado);
		yield return 0.5;
	}

	/// <summary>
	/// UM EMBATE NA MENTE, O FIM, E O M NA HORA: arma o embate (o rival nasce ao lado, na mesma zona), solta, e aperta o M assim que o `Acabou`
	/// chega -- a letra atrasada. Espera o que a saida levaria (a onda de 1,8 s e a viagem) antes de devolver.
	/// </summary>
	private IEnumerable<double> OMNoFimDoEmbate()
	{
		if (C is not { } cli || Servidor is not { } srv) yield break;
		foreach (double d in GarantirEmbate(deKi: false, fracaoDoRival: 0.2)) yield return d;
		int fins = _fins;
		srv.SoltarEmbateDeTeste(cli.LocalId);
		foreach (double d in AteQue(() => _fins > fins, 4.0)) yield return d;
		Toque(Key.M);
		foreach (double d in AteQue(() => !srv.NaMenteNoTeste(cli.LocalId), 4.0)) yield return d;
	}

	/// <summary>A foto da tela como esta (so com janela -- o headless nao renderiza).</summary>
	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		Nota("  --     foto salva em " + caminho);
	}

	private IEnumerable<double> F7_ATelaDePausaDentroDoJogo()
	{
		Nota("--- F7: dentro do jogo, a tela de pausa continua sendo a de PAUSA ---");

		// (2026-10-18) A FOLGA DO FIM DA FAMILIA DE ANTES TEM QUE TER PASSADO: o ESC daqui e um atalho, e na folga ele e mudo (ver a F9).
		foreach (double d in AteQue(() => !Foco.AtalhosMudos, 4.0)) yield return d;

		if (PauseMenu.Instancia is not { } menu)
		{
			Checa("ha uma tela de pausa de producao na arvore", false);
			yield break;
		}

		Checa("ha MUNDO na tela (senao esta familia mediria o lobby por engano)", World.Instancia != null);

		// PELA TECLA, e nao pelo `Abrir()`: e o gesto do jogador, e ele passa pelo portao do embate.
		Toque(Key.Escape);
		yield return 0.4;
		Checa("o ESC fora do embate ABRE a tela de pausa", menu.Aberto && TelaAberta("Pause"));

		Node raiz = menu;
		Checa("o titulo diz PAUSA (e nao OPÇÕES, que e o rotulo do lobby)",
			  Todos<Label>(raiz).Any(l => l.Text == "PAUSA"),
			  Todos<Label>(raiz).FirstOrDefault(l => l.Text is "PAUSA" or "OPÇÕES")?.Text ?? "(nenhum)");
		Checa("o botao de voltar diz 'Voltar ao jogo' (no lobby ele vira 'Fechar')",
			  Todos<Button>(raiz).Any(b => b.IsVisibleInTree() && b.Text == "Voltar ao jogo"));
		Checa("'Desconectar' esta VISIVEL dentro do jogo (no lobby ele some)",
			  Todos<Button>(raiz).Any(b => b.IsVisibleInTree() && b.Text.StartsWith("Desconectar")));
		Checa("'Configurar teclas' continua vivo",
			  Todos<Button>(raiz).Any(b => b.IsVisibleInTree()
										   && b.Text.StartsWith("Configurar teclas") && !b.Disabled));

		// ---- E O ESC FECHA, que e a metade que nunca pode ser bloqueada ----
		Toque(Key.Escape);
		yield return 0.4;
		Checa("o ESC fecha a tela de pausa de volta", !menu.Aberto);
	}

	// =====================================================================
	// F8 -- A GUARDA ANDA, E O PASSO DE LADO SOLTA A DISPUTA DE GUARDA (dono, 2026-10-12)
	// =====================================================================
	/// <summary>
	/// *"se o defensor for mais forte ele pode tentar se mover pros lados e sair da frente do ataque de ki enquanto
	/// defende com a guarda"* -- e, perguntado se andar de guarda erguida valia so ali ou sempre: *"Sempre, como no
	/// BYOND"*. A `--embatekiteste` (familia 14) mede a metade do SERVIDOR com pacotes forjados; aqui se mede a do
	/// CLIENTE, com as teclas de verdade:
	///
	///   1. ALT + DIRECAO: o corpo anda e a guarda NAO desce (nenhum `Guard` sai no fio enquanto ele anda; no servidor
	///      ela continua erguida). O contra-exemplo e o jogo de antes (`LocalPlayer.AndarBaixaAGuardaDeTeste`);
	///   2. NA DISPUTA DE GUARDA (um raio de verdade contra a guarda do jogador, `GameServer.EmbateDeGuardaDeTeste`): o
	///      eixo do raio chega e a tela convida a sair PELAS SETAS; a seta AO LONGO do raio nao e passo (o cliente nem
	///      pede: nao ha correcao por pacote); o W -- de lado, mas letra do quick time event desde 2026-10-19 -- so
	///      responde: a letra chega ao servidor e o corpo fica; a seta DE LADO solta -- o fim chega sem vencedor, o corpo
	///      anda de guarda erguida, ileso, e a tela do quick time event diz que ele saiu e fecha. Os contra-exemplos: a
	///      porta fechada no servidor (`KiContraCorpo.SemSaidaDeLadoDeTeste`: a mesma seta, e a disputa continua de pe) e
	///      o WASD andando na disputa (`LocalPlayer.LetraDoQteAndaNaGuardaDeTeste`: o mesmo W tira da disputa).
	///
	/// O RAIO VEM DO LESTE (o rival nasce oito tiles a direita, como o da `F3`): as setas pra direita e pra esquerda sao ao
	/// longo dele, as pra cima e pra baixo de lado -- e o W e o S tambem seriam, se ainda fossem passo ali.
	/// </summary>
	private IEnumerable<double> F8_AGuardaAndaEOPassoDeLadoSolta()
	{
		Nota("--- F8: a guarda ANDA (ALT + direcao), e o passo de lado SOLTA a disputa de guarda ---");
		if (C is not { } cli || Servidor is not { } srv) yield break;

		srv.SoltarEmbateDeTeste(cli.LocalId);
		foreach (double d in AteQue(() => !cli.EmClash && cli.Sheet.Empurrado != true && cli.Sheet.Imobilizado != true, 12.0))
			yield return d;

		Vector2 Onde() => World.Instancia?.PosicaoLocal ?? default;

		// ---------------------------------------------------------------- 1) ALT + direcao
		Marcar();
		Tecla(Key.Alt, true);
		foreach (double d in AteOFatoOuAsEstradas(() => Saiu(Protocol.C2S.Guard), VoltasEmBaixo)) yield return d;
		foreach (double d in AteQue(() => srv.GuardaErguidaDeTeste(cli.LocalId), 2.0)) yield return d;
		bool subiu = srv.GuardaErguidaDeTeste(cli.LocalId);

		// ANDANDO DE GUARDA ERGUIDA: o primeiro dos quatro rumos em que o mundo deixar (o mesmo criterio da `F2`).
		float andou = 0;
		bool desceu = false;
		foreach ((Key tecla, Vector2 eixo) in new[] { (Key.S, new Vector2(0, 1)), (Key.W, new Vector2(0, -1)), (Key.A, new Vector2(-1, 0)), (Key.D, new Vector2(1, 0)) })
		{
			Vector2 daqui = Onde();
			Marcar();   // dali em diante, qualquer `Guard` no fio e uma DESCIDA
			Tecla(tecla, true);
			ulong fim = Time.GetTicksMsec() + 2000;
			while ((Onde() - daqui).Dot(eixo) <= 12 && Time.GetTicksMsec() < fim) yield return 0;
			foreach (double d in AsEstradasAndaram(VoltasDepoisDeSoltar)) yield return d;
			andou = (Onde() - daqui).Dot(eixo);
			desceu = Saiu(Protocol.C2S.Guard);
			Tecla(tecla, false);
			yield return 0.2;
			if (andou > 8) break;
		}
		Checa("ALT + direcao: a guarda SOBE e o corpo ANDA com ela", subiu && andou > 8, $"guarda no servidor {subiu}, andou {andou:0} px");
		Checa("...e ela NAO DESCE: nenhum `Guard` sai no fio enquanto ele anda, e no servidor a guarda continua erguida",
			  !desceu && srv.GuardaErguidaDeTeste(cli.LocalId), desceu ? "saiu um `Guard` no fio" : "");

		// O CONTRA-EXEMPLO: o jogo de antes. A mesma tecla, com a guarda ainda erguida -- e ela desce.
		LocalPlayer.AndarBaixaAGuardaDeTeste = true;
		bool desceuComOBotao = false;
		try
		{
			foreach ((Key tecla, Vector2 eixo) in new[] { (Key.W, new Vector2(0, -1)), (Key.S, new Vector2(0, 1)), (Key.D, new Vector2(1, 0)), (Key.A, new Vector2(-1, 0)) })
			{
				Vector2 daqui = Onde();
				Marcar();
				Tecla(tecla, true);
				ulong fim = Time.GetTicksMsec() + 2000;
				while (!Saiu(Protocol.C2S.Guard) && Time.GetTicksMsec() < fim) yield return 0;
				desceuComOBotao = Saiu(Protocol.C2S.Guard);
				Tecla(tecla, false);
				yield return 0.2;
				if (desceuComOBotao || (Onde() - daqui).Dot(eixo) > 8) break;
			}
		}
		finally { LocalPlayer.AndarBaixaAGuardaDeTeste = false; }
		Injeta("com o jogo de antes (andar baixa a guarda), a mesma tecla faz o `Guard` de descida sair", desceuComOBotao);

		// ---------------------------------------------------------------- 2) a disputa de guarda
		// A GUARDA VOLTA A SUBIR (o ALT continua apertado; o botao de cima a tinha baixado) e ENVELHECE: a guarda que
		// acabou de subir e o gesto do deflect, e nao o de segurar o raio.
		foreach (double d in AteQue(() => srv.GuardaErguidaDeTeste(cli.LocalId), 3.0)) yield return d;
		yield return 0.7;

		_avisos.Clear();   // (o aviso que conta e o desta disputa)
		bool armou = srv.EmbateDeGuardaDeTeste(cli.LocalId);
		foreach (double d in AteQue(() => cli.EmClash && _tipo == Protocol.TipoDeEmbate.FeixeContraGuarda, 8.0)) yield return d;
		bool dePe = cli.EmClash && _tipo == Protocol.TipoDeEmbate.FeixeContraGuarda;
		Checa("a DISPUTA DE GUARDA esta de pe (um raio de verdade encontrou a guarda do jogador)", armou && dePe, $"armou {armou}, tipo {_tipo}");
		if (!dePe)
		{
			Tecla(Key.Alt, false);
			yield break;
		}

		foreach (double d in AteQue(() => cli.EixoDoRaioNasMaos != null, 3.0)) yield return d;
		Jandirus.Core.World.Vec2 eixoDoRaio = cli.EixoDoRaioNasMaos ?? default;
		Checa("...o cliente recebeu o EIXO do raio: unitario, e deitado (o rival esta a leste)",
			  cli.EixoDoRaioNasMaos != null && Math.Abs(eixoDoRaio.Length - 1f) < 1e-3f && Math.Abs(eixoDoRaio.X) > 0.9f, $"eixo {eixoDoRaio}");
		yield return 0.2;
		bool Na(string trecho) => Achar("ClashQte") is { } q && Todos<Label>(q).Any(l => l.IsVisibleInTree() && l.Text.Contains(trecho));
		Checa("...e a tela do quick time event convida a sair de lado PELAS SETAS", TelaAberta("ClashQte") && Na("ande de lado pelas setas"));
		Checa("...e o aviso do servidor no chat tambem manda sair PELAS SETAS",
			  _avisos.Exists(a => a.Contains("ande de lado pelas setas")), string.Join(" | ", _avisos));
		Fotografar("user://mudez-8-setas.png");   // a letra e o convite (so com janela)

		// (a) A SETA AO LONGO DO RAIO: nao e passo.
		Vector2 antes = Onde();
		Marcar();
		Tecla(Key.Right, true);
		foreach (double d in AsEstradasAndaram(VoltasEmBaixo + VoltasDepoisDeSoltar)) yield return d;
		bool pediuAoLongo = BitDeInput(Protocol.InputAndando);
		float andouAoLongo = (Onde() - antes).Length();
		Tecla(Key.Right, false);
		yield return 0.1;
		Checa("NA DISPUTA DE GUARDA a seta AO LONGO do raio nao e passo: o corpo fica, o cliente nem pede (nao ha correcao por pacote) e a disputa continua",
			  cli.EmClash && !pediuAoLongo && andouAoLongo < 0.5f, $"em disputa {cli.EmClash}, pediu {pediuAoLongo}, andou {andouAoLongo:0.0} px");

		// (a2) (2026-10-19) O W E DE LADO, MAS E LETRA DO QUICK TIME EVENT: so responde. A letra chega ao servidor, o corpo
		//      fica e a disputa continua -- com o WASD andando, o toque que responde seria o mesmo que tira da disputa.
		antes = Onde();
		Marcar();
		Tecla(Key.W, true);
		foreach (double d in AsEstradasAndaram(VoltasEmBaixo + VoltasDepoisDeSoltar)) yield return d;
		bool letraDoW = Saiu(Protocol.C2S.ClashTecla);
		bool pediuComW = BitDeInput(Protocol.InputAndando);
		float andouComW = (Onde() - antes).Length();
		Tecla(Key.W, false);
		yield return 0.1;
		Checa("NA DISPUTA DE GUARDA o W (de lado ao raio) e do QUICK TIME EVENT: a letra chega ao servidor, o corpo fica e a disputa continua",
			  cli.EmClash && letraDoW && !pediuComW && andouComW < 0.5f,
			  $"em disputa {cli.EmClash}, letra no fio {letraDoW}, pediu passo {pediuComW}, andou {andouComW:0.0} px");

		// (b) O CONTRA-EXEMPLO PRIMEIRO, enquanto a disputa esta de pe: com a porta fechada no SERVIDOR, a seta de lado
		//     nao solta nada. (O cliente da o passo e o servidor o devolve -- e o corpo tremendo que a regra evita.)
		Jandirus.Core.Combat.KiContraCorpo.SemSaidaDeLadoDeTeste = true;
		bool continuou;
		try
		{
			int finsDaInjecao = _fins;
			Tecla(Key.Down, true);
			foreach (double d in AsEstradasAndaram(VoltasEmBaixo + VoltasDepoisDeSoltar)) yield return d;
			Tecla(Key.Down, false);
			continuou = cli.EmClash && _fins == finsDaInjecao;
		}
		finally { Jandirus.Core.Combat.KiContraCorpo.SemSaidaDeLadoDeTeste = false; }
		Injeta("com a porta da saida fechada no servidor (o jogo de antes), a seta de lado NAO solta a disputa", continuou);
		if (!cli.EmClash)
		{
			Checa("a disputa de guarda continuou de pe ate a medida da saida", false, "acabou antes");
			Tecla(Key.Alt, false);
			yield break;
		}
		// (o passo da injecao foi devolvido pelo servidor: espera o corpo assentar antes de medir a saida de verdade)
		foreach (double d in AsEstradasAndaram(VoltasEmBaixo)) yield return d;

		// (c) A SETA DE LADO: solta.
		antes = Onde();
		int fins = _fins;
		double vida = srv.VidaDoCorpoDeTeste(cli.LocalId);
		Marcar();
		Tecla(Key.Up, true);
		foreach (double d in AteQue(() => !cli.EmClash, 4.0)) yield return d;
		bool soltou = !cli.EmClash && _fins > fins;
		bool disseQueSaiu = false;
		ulong ate = Time.GetTicksMsec() + 900;   // segue andando: e o tempo de sair de cima do raio
		while (Time.GetTicksMsec() < ate)
		{
			disseQueSaiu |= Na("VOCE SAIU DA FRENTE");
			yield return 0;
		}
		float andouDeLado = Math.Abs(Onde().Y - antes.Y);
		bool guardaDesceu = Saiu(Protocol.C2S.Guard);
		bool guardaNoServidor = srv.GuardaErguidaDeTeste(cli.LocalId);
		Tecla(Key.Up, false);

		Checa("a SETA de lado solta a disputa: o fim chega SEM vencedor e comigo no lugar de quem saiu",
			  soltou && _ultimoVenc == 0 && _ultimoPerd == cli.LocalId, $"soltou {soltou}, venc {_ultimoVenc}, perd {_ultimoPerd} (eu sou {cli.LocalId})");
		Checa("...o corpo ANDA de lado, de guarda erguida (nenhum `Guard` desceu; no servidor ela continua de pe)",
			  andouDeLado > 8 && !guardaDesceu && guardaNoServidor, $"andou {andouDeLado:0} px, `Guard` no fio {guardaDesceu}, guarda no servidor {guardaNoServidor}");
		Checa("...ILESO: a vida do corpo no servidor nao desceu", srv.VidaDoCorpoDeTeste(cli.LocalId) >= vida - 1e-6,
			  $"vida {vida:0.##} -> {srv.VidaDoCorpoDeTeste(cli.LocalId):0.##}");
		Checa("...e a tela diz VOCE SAIU DA FRENTE", disseQueSaiu);
		foreach (double d in AteQue(() => !TelaAberta("ClashQte"), 6.0)) yield return d;
		Checa("...e FECHA", !TelaAberta("ClashQte"));

		// (d) (2026-10-19) O CONTRA-EXEMPLO DO W: o jogo de antes, com o WASD andando na disputa. Uma disputa nova (a de cima
		//     fechou), com a guarda ainda erguida -- e o mesmo W da (a2) tira o jogador dela.
		yield return 0.7;   // a guarda envelhece de novo: a que acabou de assentar e o gesto do deflect
		bool rearmou = srv.EmbateDeGuardaDeTeste(cli.LocalId);
		foreach (double d in AteQue(() => cli.EmClash && _tipo == Protocol.TipoDeEmbate.FeixeContraGuarda && cli.EixoDoRaioNasMaos != null, 8.0))
			yield return d;
		bool emDisputa = cli.EmClash && cli.EixoDoRaioNasMaos != null;
		bool saiuComW = false;
		if (rearmou && emDisputa)
		{
			int finsDoW = _fins;
			LocalPlayer.LetraDoQteAndaNaGuardaDeTeste = true;
			try
			{
				Tecla(Key.W, true);
				foreach (double d in AteQue(() => !cli.EmClash, 4.0)) yield return d;
				saiuComW = !cli.EmClash && _fins > finsDoW && _ultimoVenc == 0 && _ultimoPerd == cli.LocalId;
			}
			finally
			{
				Tecla(Key.W, false);
				LocalPlayer.LetraDoQteAndaNaGuardaDeTeste = false;
			}
		}
		Injeta("(o WASD andando na disputa, o jogo de antes) o MESMO W tira o jogador da disputa de guarda", saiuComW,
			   $"rearmou {rearmou}, em disputa {emDisputa}");

		Tecla(Key.Alt, false);
		srv.SoltarEmbateDeTeste(cli.LocalId);
		yield return 0.3;
	}
}
