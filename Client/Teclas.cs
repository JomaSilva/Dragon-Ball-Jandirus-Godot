using Godot;

namespace Jandirus.Client;

/// <summary>Em que bloco da tela de teclas esta acao aparece.</summary>
public enum GrupoDeTecla { Movimento, Combate, Voo, Acao, Interface }

/// <summary>
/// UMA TECLA DO JOGO -- a linha da tabela.
/// </summary>
/// <param name="Id">
/// O nome da acao. Pras que vao pro `InputMap` ele E o nome da acao la (`"move_left"`), porque e
/// por ele que o `LocalPlayer` e os robos de bancada leem. Pras outras e so a chave desta tabela.
/// </param>
/// <param name="Rotulo">Como a tela de teclas chama isto.</param>
/// <param name="Padrao">
/// As teclas de fabrica. E um ARRAY porque algumas acoes tem apelido de nascenca (andar pra
/// esquerda e A **e** seta esquerda) -- ver <see cref="Teclas.Religar"/> pro que acontece com o
/// apelido quando o jogador escolhe outra tecla.
/// </param>
/// <param name="NoInputMap">
/// Esta acao vira acao do `InputMap`? So as do JOGO viram: e de la que o `LocalPlayer` le (e os
/// quatro robos de bancada, que dirigem o jogo com `Input.ActionPress`). As da INTERFACE (menu,
/// interagir, mochila, ajuda) sao lidas evento a evento pelas proprias telas, via
/// <see cref="Teclas.Bate"/> -- registra-las no `InputMap` sem ninguem ler seria acao morta.
/// </param>
/// <param name="Fixa">
/// Nao se religa. Sao tres, e as tres sao SAIDA: o ESC (que fecha sete telas, inclusive a que
/// desfaria a ligacao), o ENTER e a barra (que abrem a caixa de fala). Elas estao NESTA tabela e
/// nao numa lista a parte de proposito -- e essa a diferenca entre "a tecla P esta livre?" ser
/// verdade e ser mentira. Uma segunda lista envelheceria calada.
/// </param>
public sealed record AcaoDeTecla(
	string Id,
	string Rotulo,
	GrupoDeTecla Grupo,
	Key[] Padrao,
	bool NoInputMap = true,
	bool Fixa = false);

/// <summary>O que uma tecla do jogador dispara.</summary>
/// <param name="Tipo">"verbo" ou "forma".</param>
/// <param name="Chave">
/// O id ESTAVEL do alvo: a <see cref="Verbo.ChaveOuNome"/> pro verbo, o `FormaDef.Id` pra forma.
/// Nunca o nome mostrado -- ver o cabecalho de <see cref="Verbo.Chave"/>.
/// </param>
/// <param name="Rotulo">
/// Como o alvo se chamava quando a ligacao foi feita. E o que a tela mostra quando o alvo NAO
/// existe neste personagem: sem ele a linha seria "tec:kamehameha" numa tela de jogador.
/// </param>
public sealed record Atalho(string Tipo, string Chave, string Rotulo);

/// <summary>
/// O REGISTRO DE TECLAS -- quem manda em cada tecla do teclado, num lugar so.
///
/// ============================ POR QUE ELE PRECISOU EXISTIR ============================
/// Antes desta tabela havia dois mundos. As vinte acoes de jogo nasciam em `Boot.RegistrarTeclas`
/// e iam pro `InputMap`; as sete teclas de interface (P, E, I, TAB, ESC, ENTER, /) eram `if
/// (k.Keycode == Key.P)` espalhados por sete arquivos. Enquanto nada se religava, os dois mundos
/// podiam se ignorar.
///
/// O dono pediu tecla pra qualquer coisa em qualquer tecla. No instante em que isso existe, uma
/// varredura do `InputMap` responderia "a tecla P esta livre" -- e estaria MENTINDO, porque o P
/// abre o menu por um `if` que o `InputMap` nunca viu. O jogador ligaria o Kamehameha ao P e
/// receberia o menu junto do golpe, sem nada na tela explicando por que.
///
/// Entao a regra e: **toda tecla que o jogo usa mora nesta tabela**, inclusive as que nao se
/// religam. Conflito e uma pergunta so (<see cref="DonoDe"/>), feita numa tabela so.
/// ======================================================================================
///
/// ============================ O `InputMap` CONTINUA SENDO A PROJECAO ============================
/// As acoes de jogo continuam indo pro `InputMap` com os MESMOS NOMES de sempre. Nao e detalhe:
/// os quatro robos de bancada (`RoboDeColada`, `RoboDeNebulosa`, `RoboDeForma`, `RoboDeDoisCorpos`)
/// dirigem o jogo com `Input.ActionPress("move_right")`. Um despacho paralelo -- "eu leio a tecla e
/// chamo a funcao" -- teria quebrado os quatro de uma vez, e religar a tecla e exatamente reescrever
/// o evento da acao, que e o que <see cref="Aplicar"/> faz.
///
/// `ActionEraseEvents` ANTES de acrescentar, sempre: o `ActionAddEvent` so soma, e sem apagar o
/// jogador que religasse "socar" ficaria com as DUAS teclas socando.
/// ========================================================================================
/// </summary>
public static class Teclas
{
	/// <summary>
	/// A TABELA. Ela e a unica fonte da verdade -- "restaurar o padrao" e reler esta lista, e nao
	/// consultar uma segunda copia dos padroes escrita em algum outro lugar.
	/// </summary>
	public static readonly AcaoDeTecla[] Todas =
	[
		// ---------------------------------------------------------- movimento
		new("move_left", "andar pra esquerda", GrupoDeTecla.Movimento, [Key.A, Key.Left]),
		new("move_right", "andar pra direita", GrupoDeTecla.Movimento, [Key.D, Key.Right]),
		new("move_up", "andar pra cima", GrupoDeTecla.Movimento, [Key.W, Key.Up]),
		new("move_down", "andar pra baixo", GrupoDeTecla.Movimento, [Key.S, Key.Down]),

		// SHIFT E CORRER, e correr E o golpe pesado: no original nao havia tecla de "soco forte",
		// o ataque ficava pesado justamente quando saia em dash (`1 + dash_delay`).
		new("run", "correr (e golpe pesado)", GrupoDeTecla.Movimento, [Key.Shift]),

		// ---------------------------------------------------------- combate
		new("attack", "socar", GrupoDeTecla.Combate, [Key.Space]),
		new("guard", "guarda", GrupoDeTecla.Combate, [Key.Alt]),
		new("lethal", "alternar golpe letal", GrupoDeTecla.Combate, [Key.K]),
		new("aim_none", "soltar a mira", GrupoDeTecla.Combate, [Key.Key0]),
		new("aim_head", "mirar a cabeca", GrupoDeTecla.Combate, [Key.Key1]),
		new("aim_torso", "mirar o torso", GrupoDeTecla.Combate, [Key.Key2]),
		new("aim_abdomen", "mirar o abdomen", GrupoDeTecla.Combate, [Key.Key3]),
		new("aim_arms", "mirar os bracos", GrupoDeTecla.Combate, [Key.Key4]),
		new("aim_legs", "mirar as pernas", GrupoDeTecla.Combate, [Key.Key5]),

		// ---------------------------------------------------------- voo
		// NAO REUSAM O ESPACO nem o SHIFT de proposito -- sao as duas coisas que mais se faz no ar.
		// O F E O VOO por ordem do dono ("troque o botao default do FLY pra F"). O F era do `descer`,
		// entao o descer foi pro G -- vizinho do F, livre, e mantem o apelido PageDown. O V, que era
		// do voo, fica LIVRE de proposito: e onde entra a tecla de falar por voz.
		new("voar", "pairar (liga e desliga)", GrupoDeTecla.Voo, [Key.F]),
		new("subir", "subir no ar", GrupoDeTecla.Voo, [Key.R, Key.Pageup]),
		new("descer", "descer no ar", GrupoDeTecla.Voo, [Key.G, Key.Pagedown]),

		// NADAR MORA NO GRUPO DO VOO porque a pergunta que ele responde e a mesma -- "por onde este
		// corpo passa" --, e nao porque ele seja voo: nadar nao levanta o corpo do chao.
		//
		// O **N** E O QUE SOBROU E E O QUE CASA: o I do original ja e a mochila neste port
		// (`Hotkeys_Defaults.dm:10` dava o I pro Swim), o S e andar pra baixo, e o F e o V acabaram
		// de ser tomados pelo voo e pela voz. N esta livre em toda a tabela e e a inicial de nadar.
		new("nadar", "nadar (liga e desliga)", GrupoDeTecla.Voo, [Key.N]),

		// ---------------------------------------------------------- acao
		// UMA TECLA, TRES GESTOS: segurar carrega Ki, soltar para, e dois toques dentro da janela
		// sobem a escada. Ver `LocalPlayer.LerTeclaC`.
		new("transformar", "carregar Ki (segurar) e subir a escada (dois toques)",
			GrupoDeTecla.Acao, [Key.C]),
		new("reverter", "voltar ao normal", GrupoDeTecla.Acao, [Key.X]),
		new("train", "treinar", GrupoDeTecla.Acao, [Key.T]),
		new("meditate", "meditar", GrupoDeTecla.Acao, [Key.M]),

		// A VOZ. O V ficou livre quando o voo foi pro F, e e nele que ela entra -- "ao apertar V",
		// literal. Entra NESTA tabela (e nao num `if (k.Keycode == Key.V)` dentro do microfone) pelo
		// motivo do cabecalho: senao "a tecla V esta livre?" voltaria a mentir, e quem ligasse uma
		// tecnica ao V passaria a transmitir o quarto dele junto com o golpe.
		//
		// NO `InputMap` como as outras de jogo: e o `Microfone` que le, com `IsActionPressed`, e e
		// assim que a bancada consegue apertar a tecla de verdade em vez de chamar a funcao na mao.
		new("falar_voz", "falar por voz (segurar)", GrupoDeTecla.Acao, [Key.V]),

		// ---------------------------------------------------------- interface
		// FORA DO `InputMap`: quem le sao as proprias telas, evento a evento (ver `NoInputMap`).
		new("ui_menu", "menu do jogo", GrupoDeTecla.Interface, [Key.P], NoInputMap: false),
		new("ui_interagir", "interagir com o que esta perto", GrupoDeTecla.Interface, [Key.E], NoInputMap: false),
		new("ui_mochila", "mochila", GrupoDeTecla.Interface, [Key.I], NoInputMap: false),
		new("ui_ajuda", "mostrar/esconder a lista de teclas", GrupoDeTecla.Interface, [Key.Tab], NoInputMap: false),

		// AS TRES QUE NAO SE RELIGAM -- ver `AcaoDeTecla.Fixa`.
		new("fixa_sair", "fechar a tela aberta / pausa", GrupoDeTecla.Interface,
			[Key.Escape], NoInputMap: false, Fixa: true),
		new("fixa_falar", "abrir a caixa de fala", GrupoDeTecla.Interface,
			[Key.Enter, Key.KpEnter], NoInputMap: false, Fixa: true),
		new("fixa_comando", "abrir a caixa ja com a barra de comando", GrupoDeTecla.Interface,
			[Key.Slash], NoInputMap: false, Fixa: true),
	];

	/// <summary>As teclas de AGORA de cada acao. Comeca no padrao e muda com <see cref="Religar"/>.</summary>
	private static readonly Dictionary<string, Key[]> _agora = [];

	/// <summary>As teclas que o JOGADOR ligou a um verbo ou a uma forma.</summary>
	private static readonly Dictionary<Key, Atalho> _atalhos = [];

	private static Settings? _cfg;

	/// <summary>Alguma ligacao mudou -- a tela de teclas e a ajuda do HUD se redesenham.</summary>
	public static event Action? Mudou;

	public static AcaoDeTecla? Acao(string id) => Array.Find(Todas, a => a.Id == id);

	/// <summary>As teclas de agora desta acao. Vazio = ela ficou SEM tecla (o jogador deu a dela a outra coisa).</summary>
	public static Key[] Teclado(string id) =>
		_agora.TryGetValue(id, out Key[]? k) ? k : Acao(id)?.Padrao ?? [];

	/// <summary>Os atalhos do jogador, pra tela desenhar.</summary>
	public static IEnumerable<KeyValuePair<Key, Atalho>> Ligados => _atalhos;

	public static Atalho? AtalhoDe(Key k) => _atalhos.GetValueOrDefault(k);

	/// <summary>A tecla ligada a este alvo, ou <see cref="Key.None"/>.</summary>
	public static Key TeclaDo(string tipo, string chave)
	{
		foreach ((Key k, Atalho a) in _atalhos)
			if (a.Tipo == tipo && a.Chave == chave) return k;
		return Key.None;
	}

	// =====================================================================
	// APLICAR
	// =====================================================================
	/// <summary>
	/// Monta a tabela a partir do padrao, poe por cima o que estava gravado, e projeta no `InputMap`.
	///
	/// Chamado UMA vez no boot, e de novo a cada mudanca (por <see cref="Salvar"/>).
	/// </summary>
	public static void Aplicar(Settings cfg)
	{
		_cfg = cfg;
		_agora.Clear();
		_atalhos.Clear();

		foreach (AcaoDeTecla a in Todas) _agora[a.Id] = a.Padrao;

		// O QUE ESTAVA GRAVADO. Acao desconhecida (nome que sumiu do jogo) e IGNORADA em silencio:
		// o config e de MAQUINA e sobrevive a versoes, e recusar o arquivo inteiro por uma linha
		// velha apagaria as outras vinte ligacoes junto.
		foreach (Settings.LigacaoDeTecla l in cfg.LigacoesDeTecla)
		{
			if (Acao(l.Acao) is not { Fixa: false }) continue;
			_agora[l.Acao] = LerTecla(l.Tecla) is { } k and not Key.None ? [k] : [];
		}

		// ============================ O GRAVADO GANHA DO PADRAO -- ACHADO TROCANDO O VOO PRO F ============================
		// Ate aqui o padrao e o gravado eram escritos um por cima do outro SEM ninguem perguntar se
		// batiam. Enquanto o padrao nao mudava, dava no mesmo. No instante em que ele muda -- o voo
		// saindo do V pro F --, quem tivesse `descer = F` gravado no save ficaria com o `descer` (do
		// arquivo) E o `voar` (do padrao novo) na MESMA tecla, os dois projetados no `InputMap`: um
		// toque no F levantaria voo e mandaria descer junto.
		//
		// E ficaria CALADO, que e o pior: `DonoDe` devolve o PRIMEIRO da tabela, entao a tela de
		// teclas mostraria o F como sendo so do "pairar" e nunca revelaria o outro dono. O jogador
		// veria o corpo teimando e nao teria onde olhar.
		//
		// A regra: a tecla que alguem ESCOLHEU expulsa a mesma tecla de quem so a tinha de fabrica --
		// e a mesma decisao do `Liberar`, que ja e o que acontece quando a troca e feita ao vivo. O
		// perdedor fica SEM tecla em vez de perder calado, e a tela mostra a linha vazia.
		// ==========================================================================================
		foreach (Settings.AtalhoGravado g in cfg.AtalhosDeTecla)
		{
			if (LerTecla(g.Tecla) is not { } k || k == Key.None) continue;
			if (g.Tipo.Length == 0 || g.Chave.Length == 0) continue;
			_atalhos[k] = new Atalho(g.Tipo, g.Chave, g.Rotulo);
		}

		// O ATALHO DE VERBO CONTA COMO ESCOLHA IGUAL: quem tiver o Kamehameha no F escolheu o F tanto
		// quanto quem religou uma acao pra la, e o padrao novo nao pode passar por cima dele calado.
		HashSet<Key> escolhidas = [.. _atalhos.Keys];
		foreach (Settings.LigacaoDeTecla l in cfg.LigacoesDeTecla)
			if (Acao(l.Acao) is { Fixa: false } && LerTecla(l.Tecla) is { } e && e != Key.None)
				escolhidas.Add(e);

		foreach (AcaoDeTecla a in Todas)
		{
			if (a.Fixa || cfg.LigacoesDeTecla.Any(l => l.Acao == a.Id)) continue;   // gravada: e ela quem expulsa
			Key[] tem = _agora[a.Id];
			if (Array.Exists(tem, escolhidas.Contains))
				_agora[a.Id] = [.. tem.Where(k => !escolhidas.Contains(k))];
		}

		Projetar();
		Mudou?.Invoke();
	}

	/// <summary>
	/// Escreve a tabela no `InputMap`. So as acoes de jogo -- ver <see cref="AcaoDeTecla.NoInputMap"/>.
	/// </summary>
	private static void Projetar()
	{
		foreach (AcaoDeTecla a in Todas)
		{
			if (!a.NoInputMap) continue;
			if (!InputMap.HasAction(a.Id)) InputMap.AddAction(a.Id, 0.2f);

			// APAGAR PRIMEIRO. Sem isto religar SOMA uma tecla em vez de trocar, e a antiga
			// continua valendo -- o defeito que fez esta funcao existir.
			InputMap.ActionEraseEvents(a.Id);
			foreach (Key k in Teclado(a.Id))
				InputMap.ActionAddEvent(a.Id, new InputEventKey { PhysicalKeycode = k });
		}
	}

	private static void Salvar()
	{
		if (_cfg is not { } cfg) return;

		cfg.LigacoesDeTecla.Clear();
		foreach (AcaoDeTecla a in Todas)
		{
			if (a.Fixa) continue;
			Key[] agora = Teclado(a.Id);
			// SO O QUE FUGIU DO PADRAO vai pro arquivo. Gravar tudo faria o config congelar os
			// padroes de hoje: mudar a tecla de fabrica amanha nao chegaria a ninguem que ja jogou.
			if (agora.Length == a.Padrao.Length && !agora.Where((k, i) => k != a.Padrao[i]).Any()) continue;
			cfg.LigacoesDeTecla.Add(new Settings.LigacaoDeTecla
			{
				Acao = a.Id,
				Tecla = agora.Length > 0 ? agora[0].ToString() : "",
			});
		}

		cfg.AtalhosDeTecla.Clear();
		foreach ((Key k, Atalho a) in _atalhos)
			cfg.AtalhosDeTecla.Add(new Settings.AtalhoGravado
			{
				Tecla = k.ToString(), Tipo = a.Tipo, Chave = a.Chave, Rotulo = a.Rotulo,
			});

		cfg.Gravar();
		Projetar();
		Mudou?.Invoke();
	}

	// =====================================================================
	// CONFLITO
	// =====================================================================
	/// <summary>
	/// QUEM MANDA NESTA TECLA HOJE? Nulo = ninguem.
	///
	/// E a unica pergunta de conflito do jogo, e ela varre a tabela INTEIRA (jogo, interface e
	/// fixas) mais os atalhos do jogador. Ver o cabecalho da classe pro que acontecia sem isso.
	/// </summary>
	public static (string Rotulo, bool Fixa)? DonoDe(Key k)
	{
		foreach (AcaoDeTecla a in Todas)
			if (Array.IndexOf(Teclado(a.Id), k) >= 0) return (a.Rotulo, a.Fixa);

		return _atalhos.TryGetValue(k, out Atalho? at) ? (at.Rotulo, false) : null;
	}

	/// <summary>Da pra ligar alguma coisa a esta tecla? As fixas nao -- ver <see cref="AcaoDeTecla.Fixa"/>.</summary>
	public static bool Elegivel(Key k) => k != Key.None && DonoDe(k) is not { Fixa: true };

	/// <summary>
	/// Tira a tecla de quem a tinha. **O dono anterior fica SEM tecla** em vez de perde-la calado
	/// pra outro -- a tela pergunta antes, e depois mostra a linha vazia, que e o unico jeito de o
	/// jogador ver que a troca custou alguma coisa.
	/// </summary>
	private static void Liberar(Key k)
	{
		_atalhos.Remove(k);
		foreach (AcaoDeTecla a in Todas)
		{
			if (a.Fixa) continue;
			Key[] tem = Teclado(a.Id);
			if (Array.IndexOf(tem, k) < 0) continue;
			_agora[a.Id] = [.. tem.Where(x => x != k)];
		}
	}

	// =====================================================================
	// MEXER
	// =====================================================================
	/// <summary>
	/// Poe esta acao nesta tecla. Devolve falso quando a tecla e fixa (ESC e companhia).
	///
	/// A TECLA NOVA SUBSTITUI O APELIDO. Quem religa "andar pra esquerda" pra J perde a seta
	/// esquerda junto -- e proposital: guardar o apelido faria a tela dizer "J" enquanto a seta
	/// continuasse andando, e o jogador que quis TIRAR a seta nao teria como. "Restaurar" devolve
	/// as duas.
	/// </summary>
	public static bool Religar(string id, Key k)
	{
		if (Acao(id) is not { Fixa: false }) return false;
		if (!Elegivel(k)) return false;
		Liberar(k);
		_agora[id] = [k];
		Salvar();
		return true;
	}

	public static void Restaurar(string id)
	{
		if (Acao(id) is not { Fixa: false } a) return;
		foreach (Key k in a.Padrao) Liberar(k);
		_agora[id] = a.Padrao;
		Salvar();
	}

	/// <summary>
	/// Tudo de volta ao de fabrica: teclas E atalhos.
	///
	/// O "padrao" e reler <see cref="Todas"/> -- nao ha segunda tabela de padroes pra divergir.
	/// </summary>
	public static void RestaurarTudo()
	{
		_agora.Clear();
		_atalhos.Clear();
		foreach (AcaoDeTecla a in Todas) _agora[a.Id] = a.Padrao;
		Salvar();
	}

	/// <summary>Liga um verbo (ou uma forma) a uma tecla. Falso = tecla fixa.</summary>
	public static bool Ligar(Key k, Atalho a)
	{
		if (!Elegivel(k)) return false;
		// O MESMO ALVO SO TEM UMA TECLA: ligar de novo MUDA a tecla, nao acrescenta uma segunda.
		Key antiga = TeclaDo(a.Tipo, a.Chave);
		if (antiga != Key.None) _atalhos.Remove(antiga);
		Liberar(k);
		_atalhos[k] = a;
		Salvar();
		return true;
	}

	public static void Desligar(string tipo, string chave)
	{
		Key k = TeclaDo(tipo, chave);
		if (k == Key.None) return;
		_atalhos.Remove(k);
		Salvar();
	}

	// =====================================================================
	// LER O TECLADO
	// =====================================================================
	/// <summary>
	/// A TECLA FISICA do evento. E o que o jogo inteiro usa (`Boot`, `ClashQte`, `MenuDeInteracao`):
	/// `Keycode` muda com o layout, `PhysicalKeycode` e a tecla onde o dedo esta.
	/// </summary>
	public static Key Fisica(InputEventKey k) =>
		k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;

	/// <summary>Este evento e a tecla desta acao? E como as telas de interface perguntam.</summary>
	public static bool Bate(string id, InputEventKey k) =>
		Array.IndexOf(Teclado(id), Fisica(k)) >= 0;

	/// <summary>
	/// O NOME QUE APARECE NA TELA.
	///
	/// A tabela guarda a tecla FISICA (posicao no teclado), mas quem le a tela esta olhando pra
	/// LETRA IMPRESSA. Num ABNT2 ou num AZERTY os dois divergem, e mostrar a fisica crua escreveria
	/// uma letra que nao e a que a pessoa acabou de apertar.
	/// </summary>
	public static string Nome(Key k)
	{
		if (k == Key.None) return "--";

		// ============================ O PORTAO DO HEADLESS -- ACHADO RODANDO ============================
		// `KeyboardGetKeycodeFromPhysical` nao existe no servidor grafico nulo, e nao devolve zero: ele
		// **imprime um erro** ("Not supported by this display server") com pilha inteira, uma vez POR
		// TECLA. O HUD monta a lista de teclas no boot, entao a primeira bancada headless que rodei
		// cuspiu doze pilhas de erro antes de a primeira checagem sair -- e nenhum teste teria pego
		// isso, porque nada falha: a lista sai certa e o log fica ilegivel.
		//
		// Sem teclado fisico nao ha layout pra traduzir, e a tecla fisica E a resposta certa.
		// =========================================================================================
		Key mostrada = k;
		if (!Headless && DisplayServer.KeyboardGetKeycodeFromPhysical(k) is var m and not Key.None)
			mostrada = m;

		string s = OS.GetKeycodeString(mostrada);
		return s.Length > 0 ? s : k.ToString();
	}

	/// <summary>Rodando sem janela? Cache: `GetName` atravessa o motor e isto e perguntado por tecla.</summary>
	private static bool? _headless;
	private static bool Headless =>
		_headless ??= DisplayServer.GetName() == "headless";

	/// <summary>As teclas de uma acao escritas juntas ("A / Left"). Vazio = ela ficou sem tecla.</summary>
	public static string NomeDaAcao(string id)
	{
		Key[] k = Teclado(id);
		return k.Length == 0 ? "-- sem tecla --" : string.Join(" / ", k.Select(Nome));
	}

	private static Key? LerTecla(string s) =>
		Enum.TryParse(s, ignoreCase: false, out Key k) ? k : null;

	// =====================================================================
	// A AJUDA DO HUD
	// =====================================================================
	/// <summary>
	/// A LISTA DO TAB. Ela LE a tabela em vez de escrever "C" na mao -- quem religou o carregar de
	/// Ki pro G tem que ver G aqui, senao a ajuda passa a ensinar errado o proprio jogador que a
	/// mudou.
	///
	/// E uma lista escolhida, e nao a tabela inteira: sao 27 acoes e o painel cabe uma duzia. O que
	/// entra e o que se usa lutando; o resto se descobre na tela de teclas, que agora existe.
	/// </summary>
	public static (string Tecla, string Oque)[] Ajuda()
	{
		string P(string id) => NomeDaAcao(id);
		string U(string id) => Teclado(id) is [var k, ..] ? Nome(k) : "--";

		return
		[
			($"{U("move_up")}{U("move_left")}{U("move_down")}{U("move_right")}", "andar"),
			(U("run"), "correr"),
			(U("attack"), "socar"),
			($"{U("run")} + {U("attack")}", "investida + golpe pesado"),
			(U("guard"), "guarda (na hora certa = contra-ataque)"),
			($"{U("aim_none")} - {U("aim_legs")}", "onde mirar (ou clique no boneco)"),
			(U("lethal"), "alternar golpe letal"),
			($"{U("train")} / {U("meditate")}", "treinar / meditar"),
			(U("transformar"), "segurar = Ki  ·  dois toques = subir"),
			(U("falar_voz"), "falar por voz (segurar)"),
			(P("ui_ajuda"), "esconder esta lista"),
			(P("ui_menu"), "menu do jogo"),
			(P("fixa_sair"), "pausa (e a tela de teclas)"),
		];
	}
}
