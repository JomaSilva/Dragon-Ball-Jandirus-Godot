using Godot;

namespace Jandirus.Client;

/// <summary>
/// O AQUECIMENTO DO PROCESSO -- o trabalho de UMA VEZ POR SESSAO, feito enquanto o jogador ainda
/// esta no lobby em vez de no pior instante possivel.
///
/// ============================ O NUMERO QUE JUSTIFICA ESTA CLASSE ============================
/// Medido com janela, quadro a quadro, do clique em "Entrar no mundo" ate o corpo aparecer -- as
/// duas colunas sao o MESMO binario, separadas so pelo `--semaquecimento` (ver `RoboDeCarga`):
///
///                                 sem aquecimento     com aquecimento
///     1a entrada do processo ....... 1586 ms  ......... 866 ms
///       -- rede + montagem .......... 899 ms  ......... 486 ms
///       -- o quadro que desenhou .... 687 ms  ......... 381 ms
///     2a entrada, mesmo processo .... 575 ms  ......... 457 ms
///
/// (866 ms e o jogador que gastou alguns segundos no lobby. Quem loga em ~1 s pega o aquecimento
/// pela metade e entra em 922 ms -- ver <see cref="Concluir"/>. Nunca fica PIOR que sem aquecimento:
/// e o mesmo trabalho, feito antes do clique em vez de depois dele.)
///
/// **720 ms saem da frente do jogador.** Nao eram carga de zona (essa custa ~75 ms, medidos pelo
/// proprio `[perf] TOTAL` do `World.CarregarZona`) nem servidor. Eram trabalho de UMA VEZ POR
/// PROCESSO, e e por isso que a segunda entrada sempre foi barata -- ela ja encontrava tudo pronto:
///
///   * `res://Assets/Maps/tileset.tres` -- ~630 ms. Nao e parse (274 ms em texto contra 275 em
///     binario, ja medido no `World._tilesetVivo`): e montar 125 fontes de atlas, 163 PNG e
///     ~7.800 tiles. Ele e o passageiro escondido do `World._Ready`.
///   * os SHADERS do mundo -- cada um paga um "Shader cache miss" na primeira vez. (Medido: o
///     cache de shader EM DISCO nao muda o quadro caro -- 502 ms com ele frio, 502 ms com ele
///     quente. Ou seja compilar shader NAO era o gargalo, e vale registrar pra ninguem gastar uma
///     sessao ali.)
///   * as CHAPAS DO BONECO DE VIDA do HUD -- 10 folhas que so aparecem quando o HUD monta, isto
///     e, dentro do mesmo quadro em que o corpo nasce.
///   * as FOLHAS DE APARENCIA dos NPC -- ver <see cref="PedirAsAparencias"/>. Sao elas que sobraram
///     depois do tileset, e sozinhas valeram outros ~130 ms do quadro caro (502 -> 346 ms).
///
/// ============================ POR QUE ISTO E CONSERTO E NAO MAQUIAGEM ============================
/// Nada disto depende do `JoinAccepted`: o tileset e o mesmo pra todo mundo, os shaders sao os
/// mesmos, as chapas do HUD sao as mesmas. Nao ha uma unica informacao do personagem em nada que
/// esta lista carrega -- e por isso pode ser carregado ANTES de existir personagem.
///
/// O padrao ja existia no projeto e so nao alcancava o peso de verdade: o `Boot.PrepararCriacao`
/// monta a tela de criacao escondida por `CallDeferred`, com o comentario *"o jogador esta lendo a
/// lista de slots -- e o momento em que uns milissegundos nao custam nada"*. Aqui e o mesmo
/// momento, com os milissegundos que custam.
/// =================================================================================================
///
/// ============================ EM OUTRA THREAD, E ISSO NAO E ENFEITE ============================
/// `ResourceLoader.Load` cru aqui trocaria um congelamento por outro: o lobby ficaria 600 ms sem
/// responder bem quando o jogador esta clicando nele. O `LoadThreadedRequest` poe o trabalho numa
/// thread de carga e devolve o quadro na hora; o `_Process` daqui so RECOLHE o que ficou pronto.
/// ===============================================================================================
///
/// ============================ E SE O JOGADOR FOR MAIS RAPIDO QUE A THREAD? ============================
/// A primeira versao desta classe respondia "nada quebra, o `ResourceLoader` espera a carga que ja
/// comecou". **Era mentira, e travava o jogo.** Um cliente automatico (`--host`, o modo das outras
/// quarenta bancadas) entra no mundo no primeiro segundo, e o `ResourceLoader.Load` do
/// `World._Ready` batia de frente com o `LoadThreadedRequest` do MESMO tileset ainda no ar: a thread
/// principal parava ali e nunca mais saia. Medido: 240 s sem uma linha de log depois de
/// `[client] entrei como id 10`, e a mesma rodada com `--semaquecimento` terminando em 40 s.
///
/// O conserto e <see cref="Concluir"/>: antes de qualquer um poder pedir estes arquivos, a fila
/// pendente e RECOLHIDA com `LoadThreadedGet` -- que e a porta feita pra esperar uma carga em
/// andamento, e nao competir com ela. Depois disso nao ha nada no ar pra colidir, e todo
/// `ResourceLoader.Load` do jogo cai no cache.
///
/// A licao vale mais que o conserto: **"provavelmente nao acontece" nao e uma corrida resolvida.**
/// Aqui a corrida so nao aparecia na janela porque o jogador humano gasta segundos no lobby -- e as
/// bancadas, que entram em milissegundos, caiam nela todas as vezes.
/// =======================================================================================================
///
/// ============================ SEGURAR A REFERENCIA E O QUE MANTEM O CACHE ============================
/// O cache do Godot e por contagem de referencia: ele solta o recurso no instante em que a ultima
/// some. Carregar e jogar fora seria aquecer o forno e abrir a porta. Por isso <see cref="_presos"/>
/// e **estatica** -- o custo e do PROCESSO, e relogar nao deve repaga-lo (a mesma razao pela qual o
/// `World._tilesetVivo` e estatico).
/// ====================================================================================================
/// </summary>
public partial class Aquecimento : Node
{
	/// <summary>
	/// O QUE VALE A PENA AQUECER, em ordem de custo. So entra aqui o que **nao depende do
	/// personagem**: se um item desta lista precisar saber quem entrou, ele esta no lugar errado.
	/// </summary>
	private static readonly string[] Lista =
	[
		// O MONSTRO. Sozinho ele e ~630 ms dos ~1253 -- ver o cabecalho da classe.
		"res://Assets/Maps/tileset.tres",

		// OS SHADERS DO MUNDO, na ordem em que a entrada os pede. Cada um paga a compilacao uma
		// vez por processo, e aqui ela sai da frente do jogador.
		"res://Assets/Shaders/Clima.gdshader",            // Iluminacao/ClimaNaTela, no MontarCenario
		"res://Assets/Shaders/Raio.gdshader",             // idem
		"res://Assets/Shaders/Personagem.gdshader",       // CharacterVisual: o corpo
		"res://Assets/Shaders/Aura.gdshader",             // SpriteDeAura
		"res://Assets/Shaders/NebulosaDaForma.gdshader",  // NebulosaDaForma
		"res://Assets/Shaders/RaioDaForma.gdshader",      // RaiosDaForma
		"res://Assets/Shaders/Embate.gdshader",           // ClashQte, montado junto com o resto

		// AS CHAPAS DO BONECO DE VIDA. Sao 10 folhas, e todas caem no mesmo quadro em que o `Hud`
		// e montado -- ou seja, no quadro do corpo. Ver `BodyDoll.Quadro`.
		"res://Assets/Sprites/Misc/HUD/health_hud.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_base.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_head.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_torso.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_abdomen.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_reproductive_organs.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_leftarm.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_rightarm.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_leftleg.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_rightleg.tres",

		// as barras do HUD, pelo mesmo motivo das chapas
		"res://Assets/Sprites/Misc/HUD/KiBar.tres",
		"res://Assets/Sprites/Misc/HUD/StaBar.tres",
		"res://Assets/Sprites/Misc/HUD/GKiBar.tres",
	];

	/// <summary>O que ja veio. Estatica de proposito -- ver o cabecalho.</summary>
	private static readonly List<Resource> _presos = [];

	/// <summary>Quem esta aquecendo agora. Nulo depois que a fila esvazia.</summary>
	private static Aquecimento? _vivo;

	/// <summary>Ja pedimos nesta sessao? Relogar nao repete o pedido: o cache continua quente.</summary>
	private static bool _pedido;

	/// <summary>Verdadeiro quando a fila esvaziou (com ou sem falha em algum item).</summary>
	public static bool Terminou { get; private set; }

	/// <summary>Quantos itens ja foram recolhidos -- pra bancada e pro log.</summary>
	public static int Prontos => _presos.Count;

	/// <summary>
	/// Quantos itens foram PEDIDOS ao todo -- publico pra bancada dizer "x de y".
	///
	/// E o pedido e nao o tamanho da <see cref="Lista"/> porque ha DUAS filas: a fixa e as folhas de
	/// aparencia, que saem do `visual.json` e por isso so sao contadas em tempo de execucao.
	/// </summary>
	public static int Total { get; private set; }

	/// <summary>Quanto tempo o aquecimento levou de ponta a ponta, em ms. Zero enquanto nao acabou.</summary>
	public static double Ms { get; private set; }

	private static ulong _comecou;

	/// <summary>O que ainda nao voltou. Esvazia conforme o `_Process` recolhe.</summary>
	private readonly List<string> _faltando = [];

	private readonly List<string> _falharam = [];

	/// <summary>A segunda fila ja foi pedida? Ver <see cref="PedirAsAparencias"/>.</summary>
	private bool _aparenciasPedidas;

	/// <summary>
	/// ============================ A SEGUNDA FILA: AS FOLHAS DE APARENCIA ============================
	/// COMO SE SABE QUE SAO ELAS: medido com `--verbose`, na janela entre o `JoinAccepted` e o
	/// `[perf] PRIMEIRO QUADRO`, das 267 linhas de recurso **31 sao folhas de CABELO** e 6 de roupa.
	/// Elas nao vem do personagem do jogador -- vem da rajada de aparencias dos NPC do primeiro
	/// snapshot, e caem todas dentro do unico quadro que o jogador fica esperando. A prova do custo
	/// esta no proprio processo: a MESMA entrada feita uma segunda vez, com as folhas ja em memoria,
	/// desenha em 278 ms contra 502 ms.
	///
	/// A LISTA SAI DO CATALOGO E NAO DE UM PALPITE: os caminhos vem do `visual.json`, o MESMO arquivo
	/// que o `World._Ready` e a criacao de personagem leem. Escrever nomes de cabelo aqui seria criar
	/// uma segunda lista pra sair de sincronia com a primeira no dia em que alguem puser um penteado.
	///
	/// FILA SEPARADA E PEDIDA DEPOIS: sao ~250 arquivos pequenos contra 21 grandes. Pedidos juntos,
	/// os pequenos entrariam na frente do tileset na fila do carregador e atrasariam justamente o
	/// item que sozinho vale ~630 ms.
	///
	/// O QUE CUSTA: as folhas ficam em memoria desde o lobby. Nao e memoria NOVA -- e a mesma que
	/// qualquer planeta povoado ia pedir de qualquer jeito, uns segundos depois e no pior momento.
	/// ================================================================================================
	/// </summary>
	private void PedirAsAparencias()
	{
		_aparenciasPedidas = true;
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) return;

		Jandirus.Core.Appearance.VisualCatalog cat;
		try { cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados)); }
		catch (Exception e) { _falharam.Add("visual.json (" + e.Message + ")"); return; }

		var caminhos = new List<string>();
		foreach ((string _, string? sprite) in cat.Cabelos) if (sprite is { Length: > 0 }) caminhos.Add(sprite);
		caminhos.AddRange(cat.Roupas);
		caminhos.AddRange(cat.Armaduras);
		if (cat.Olhos is { Length: > 0 }) caminhos.Add(cat.Olhos);
		// OS CORPOS, os dois generos. `Tons` fica de fora de proposito: aquilo sao nomes de cor, e
		// nao caminhos de arquivo -- pedi-los ao `ResourceLoader` so encheria a lista de falhas.
		foreach (Jandirus.Core.Appearance.BodyOptions b in cat.Corpos.Values)
		{
			caminhos.AddRange(b.Masculino);
			caminhos.AddRange(b.Feminino);
		}
		caminhos.Add(Jandirus.Core.Appearance.VisualCatalog.CorpoPadraoM);
		caminhos.Add(Jandirus.Core.Appearance.VisualCatalog.CorpoPadraoF);

		Pedir(caminhos);
	}

	/// <summary>Poe na fila o que existir, sem nunca deixar um caminho ruim derrubar o jogo.</summary>
	private void Pedir(IEnumerable<string> caminhos)
	{
		foreach (string caminho in caminhos)
		{
			// ARQUIVO QUE NAO EXISTE NAO E MOTIVO DE PARAR O JOGO. Isto aqui e uma OTIMIZACAO: se
			// alguem renomear um shader, o caminho de producao continua carregando pelo nome novo e
			// o unico prejuizo e voltar ao tempo de antes. Um erro fatal aqui seria trocar um
			// engasgo por uma tela preta.
			if (_faltando.Contains(caminho)) continue;
			if (!ResourceLoader.Exists(caminho)) { _falharam.Add(caminho + " (nao existe)"); continue; }
			if (ResourceLoader.LoadThreadedRequest(caminho) != Error.Ok) { _falharam.Add(caminho + " (recusado)"); continue; }
			_faltando.Add(caminho);
			Total++;
		}
	}

	public override void _Ready()
	{
		// UMA VEZ POR PROCESSO. O `Boot.VoltarAoLogin` derruba e remonta o lobby, e sem esta guarda
		// cada relog dispararia a fila de novo -- pedindo ao `ResourceLoader` cargas de coisas que ja
		// estao na memoria.
		if (_pedido) { Terminou = true; SetProcess(false); return; }
		_pedido = true;
		_vivo = this;
		_comecou = Time.GetTicksUsec();

		Pedir(Lista);
		if (_faltando.Count == 0) { PedirAsAparencias(); if (_faltando.Count == 0) Encerrar(); }
	}

	/// <summary>
	/// FECHA A FILA AGORA, ESPERANDO O QUE FALTA -- e a porta de saida obrigatoria do aquecimento.
	///
	/// ============================ CHAME ISTO ANTES DE QUALQUER UM PEDIR OS ARQUIVOS ============================
	/// Enquanto houver `LoadThreadedRequest` no ar, um `ResourceLoader.Load` do mesmo caminho vindo do
	/// jogo TRAVA a thread principal (ver o cabecalho da classe -- foi medido, com 240 s de log mudo).
	/// Este metodo recolhe o pendente com `LoadThreadedGet`, que e a porta feita pra ESPERAR uma carga
	/// em andamento. Depois dele nao ha mais nada no ar, e o jogo pode pedir o que quiser.
	///
	/// ELE PODE BLOQUEAR, e isso e o certo: o pior caso e esperar o que faltava do aquecimento, que e
	/// trabalho que teria de ser feito de qualquer jeito e um pouco mais adiante. No caso normal -- o
	/// jogador digitou conta e senha, gastou alguns segundos -- a fila ja esta vazia e isto custa zero.
	///
	/// O PIOR CASO, MEDIDO: um cliente que loga em ~1 s espera 377 ms aqui, no LOBBY, e a segunda fila
	/// (as folhas de aparencia) nem chega a ser pedida. Ou seja quem entra correndo fica com o
	/// aquecimento pela METADE -- e mesmo assim a entrada cai de 1586 ms pra 922 ms. Quem entra no
	/// ritmo de gente pega as duas filas e 866 ms. Em nenhum dos dois casos se paga MAIS do que sem
	/// aquecimento nenhum: e o mesmo trabalho, so que antes do clique em vez de depois dele.
	/// ============================================================================================================
	/// </summary>
	public static void Concluir()
	{
		if (_vivo is not { } eu || eu._faltando.Count == 0) return;

		ulong t0 = Time.GetTicksUsec();
		int esperados = eu._faltando.Count;

		foreach (string caminho in eu._faltando)
		{
			// `LoadThreadedGet` de um item ainda em andamento ESPERA a thread de carga terminar --
			// e exatamente o que se quer aqui, e e o que o `Load` cru nao faz.
			if (ResourceLoader.LoadThreadedGet(caminho) is { } r) _presos.Add(r);
			else eu._falharam.Add(caminho + " (voltou nulo no fecho)");
		}
		eu._faltando.Clear();

		GD.Print($"[aquece] fecho antecipado: esperei {esperados} recurso(s) por"
				 + $" {(Time.GetTicksUsec() - t0) / 1000.0:0} ms -- o jogador foi mais rapido que a thread");
		eu.Encerrar();
	}

	public override void _Process(double delta)
	{
		// RECOLHE O QUE ESTIVER PRONTO, e so isso. Nada aqui bloqueia: `LoadThreadedGetStatus` e uma
		// consulta, e o `LoadThreadedGet` de um item que ja voltou e uma entrega, nao uma carga.
		for (int i = _faltando.Count - 1; i >= 0; i--)
		{
			string caminho = _faltando[i];
			ResourceLoader.ThreadLoadStatus estado = ResourceLoader.LoadThreadedGetStatus(caminho);
			if (estado == ResourceLoader.ThreadLoadStatus.InProgress) continue;

			_faltando.RemoveAt(i);
			if (estado != ResourceLoader.ThreadLoadStatus.Loaded) { _falharam.Add(caminho + $" ({estado})"); continue; }

			if (ResourceLoader.LoadThreadedGet(caminho) is { } r) _presos.Add(r);
			else _falharam.Add(caminho + " (voltou nulo)");
		}

		if (_faltando.Count > 0) return;

		// A PRIMEIRA FILA ACABOU: agora sim as folhas de aparencia -- ver `PedirAsAparencias`.
		if (!_aparenciasPedidas) { PedirAsAparencias(); if (_faltando.Count > 0) return; }
		Encerrar();
	}

	private void Encerrar()
	{
		SetProcess(false);
		Terminou = true;
		_vivo = null;   // nao ha mais nada no ar: o `Concluir` vira um retorno imediato
		Ms = (Time.GetTicksUsec() - _comecou) / 1000.0;
		GD.Print($"[aquece] {_presos.Count} de {Total} recurso(s) quentes em {Ms:0} ms"
				 + $" ({Lista.Length} do mundo + {Total - Lista.Length} folhas de aparencia)"
				 + (_falharam.Count > 0 ? $" -- {_falharam.Count} de fora: {string.Join(", ", _falharam)}" : ""));
	}
}
