using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A CENA DO MUNDO. Junta rede, personagens e iluminacao.
///
/// ILUMINACAO: no BYOND a "sombra" era tile preto por cima do chao e nao havia luz de
/// verdade (o render e do cliente e nao ha como intervir). Aqui usamos o sistema 2D nativo:
/// um <see cref="CanvasModulate"/> escurece a cena inteira (o ambiente/noite) e cada fonte
/// de luz e um <see cref="PointLight2D"/> que RECUPERA o brilho onde alcanca. Parede vira
/// <see cref="LightOccluder2D"/> e projeta sombra sozinha.
/// </summary>
public partial class World : Node2D
{
	/// <summary>O mundo vivo. O menu de pause precisa dele pra mexer no zoom.</summary>
	public static World? Instancia { get; private set; }

	/// <summary>A hora LOCAL deste planeta (0 = meia-noite, 1 = meia-noite de novo).</summary>
	public double? Hora => _luzDoMundo?.Fase;

	/// <summary>O ceu daqui, agora: hora, fase da lua e altura dela. Nulo antes de montar.</summary>
	public Jandirus.Core.World.EstadoDoCeu? Ceu => _luzDoMundo?.Estado;

	/// <summary>A ficha de ceu do planeta atual: rotacao, dia/noite e lua.</summary>
	public Jandirus.Core.World.RelogioDoPlaneta RelogioDoLugar =>
		_luzDoMundo?.Relogio ?? Jandirus.Core.World.RelogioDoPlaneta.Padrao;

	/// <summary>O que pode cair do ceu neste planeta -- o `allowedWeatherTypes` do DM.</summary>
	public Jandirus.Core.World.ClimaDoPlaneta ClimaDoLugar =>
		_luzDoMundo?.ClimaDaqui ?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;

	/// <summary>O clima de agora. Nulo antes de montar a cena.</summary>
	public Jandirus.Core.World.EstadoDoClima? TempoQueFaz => _luzDoMundo?.TempoQueFaz;

	/// <summary>A hora do UNIVERSO em segundos -- a mesma pra todo planeta. Quem manda e o servidor.</summary>
	public double TempoDoMundo => _luzDoMundo?.Tempo ?? 0;

	private Camera2D? _camera;

	private const string Manifesto = "res://Assets/Maps/manifest.json";

	private Jandirus.Core.Appearance.VisualCatalog? _visual;
	// a aparencia de cada um chega UMA vez, e pode chegar ANTES do boneco existir
	private readonly Dictionary<int, (string Raca, string Genero, Jandirus.Core.Appearance.Appearance Ap)> _looks = [];

	/// <summary>
	/// EM QUE FORMA (e em que fera) CADA CORPO DA ZONA ESTA -- a memoria que faltava.
	///
	/// ============================ POR QUE GUARDAR, SE O PACOTE JA CHEGA ============================
	/// Pela mesma razao do `_looks` logo acima, e o servidor acabou de criar o caso: quando eu entro
	/// numa zona ele me manda o estado de forma de todos que ja estao la (`GameServer.SincronizarFormas`)
	/// -- e nesse instante nenhum daqueles bonecos existe ainda. Quem CRIA `RemotePlayer` e o snapshot,
	/// que vem por outro canal e chega depois. Sem esta memoria o `AoMudarForma` acharia `Corpo(id)`
	/// nulo, sairia calado, e a sincronia inteira do servidor morreria a um metro da tela.
	///
	/// Guarda sempre, aplica se o boneco existir, e o nascimento do boneco consulta o que ficou guardado
	/// (`VestirCorpoInteiro`). Sao as mesmas tres regras do `_looks` e das feridas.
	/// ============================================================================================
	///
	/// GUARDA O ID DE REDE e nao o `FormaDef`: e o que vem no fio, e resolver a entrada do catalogo na
	/// hora de aplicar mantem uma so conversao (a do `AoMudarForma`).
	/// </summary>
	private readonly Dictionary<int, int> _formaDaZona = [];

	/// <summary>
	/// QUEM DA ZONA DOMINOU (100% de maestria) A FORMA EM QUE ESTA. Gemeo do <see cref="_formaDaZona"/>
	/// logo acima e guardado pelas MESMAS tres razoes: o pacote pode chegar antes do boneco existir,
	/// aplica-se quando ele existir, e o nascimento do boneco consulta o que ficou guardado.
	///
	/// SO O SUPER SAIYAJIN USA ISTO hoje -- e o Grade 4, que troca a folha de cabelo (ver
	/// `Catalogo.SufixoDoCabeloDe`). E um HashSet e nao um bool por corpo porque a pergunta e sobre a
	/// forma ATUAL: quem sai da forma sai do conjunto no mesmo pacote que o tirou dela.
	/// </summary>
	private readonly HashSet<int> _dominouDaZona = [];

	/// <summary>
	/// QUEM DA ZONA ESTA SENDO DIRIGIDO PELO SERVIDOR agora (a furia lendaria, o Oozaru sem controle).
	/// Gemeo do <see cref="_dominouDaZona"/> logo acima e guardado pelas MESMAS tres razoes -- o fato
	/// pode chegar antes do boneco existir, aplica-se quando ele existir, e o nascimento do boneco
	/// consulta o que ficou guardado (`VestirAFormaSemCena`).
	///
	/// ============================ NAO HA CANAL NOVO AQUI ============================
	/// Isto e alimentado pelo `EntityState.SemRedeas` do SNAPSHOT -- o bit `flags2 &amp; 0x20` que o
	/// Oozaru ja abriu e que ja viaja pra todo mundo que ve o corpo. Um `S2C` proprio pra "a pupila
	/// mudou de cor" seria um segundo caminho pra manter em dia, e o unico jeito de ele discordar do
	/// primeiro seria calado.
	///
	/// O SNAPSHOT E A FONTE CERTA e nao um pacote de evento por outro motivo: a posse tambem MUDA por
	/// eventos que nao sao dela (o corpo cai, a forma se desfaz por Ki zerado). O snapshot diz o
	/// ESTADO todo tique, entao nao ha combinacao de eventos que o deixe velho.
	/// ==========================================================================
	/// </summary>
	private readonly HashSet<int> _semRedeasDaZona = [];

	private readonly Dictionary<int, Jandirus.Core.Forms.FormaOozaru> _feraDaZona = [];
	private LocalPlayer? _local;
	private readonly Dictionary<int, RemotePlayer> _remotos = [];
	private Iluminacao _luzDoMundo = null!;
	private ZoneCatalog? _catalogo;
	private ZoneCollision? _colisao;
	private Node2D? _zonaAtual;

	/// <summary>Que zona o <see cref="_zonaAtual"/> representa -- a chave do cache ao sair dela.</summary>
	private ZoneKey _zonaDoAtual;

	/// <summary>
	/// PLANETAS AINDA VIVOS, fora da arvore. E a resposta ao pedido do dono: "planetas q vc acabou
	/// de sair e ainda esta na memoria terem um load praticamente instantaneo".
	///
	/// ============================ POR QUE ISTO E RAPIDO ============================
	/// Medido: entrar num planeta custava 1049 ms, dos quais 943 ms eram o PARSE do .tscn (10 MB
	/// de texto) e 55 ms o `Instantiate` -- reconstruir o mapa de 250 mil celulas do TileMapLayer.
	/// Guardar o `PackedScene` mata o parse; guardar a INSTANCIA mata os dois. Reentrar vira um
	/// `AddChild`.
	///
	/// A CHAVE E A `ZoneKey` INTEIRA, com a seed. Cachear planeta gerado so pelo nome devolveria o
	/// mundo de OUTRA seed -- e o chao que o servidor calculou deixaria de bater com o desenhado.
	///
	/// TETO DE DOIS, e o numero tem motivo: uma zona pre-feita instanciada custa dezenas de MB (sao
	/// 253 mil celulas em 3 camadas), entao guardar as 40 seria mais de um giga. Dois cobrem o caso
	/// real -- onde estou e de onde acabei de sair, que e atravessar uma porta e voltar.
	/// ==============================================================================
	/// </summary>
	private readonly Dictionary<ZoneKey, Node2D> _zonasVivas = [];
	private readonly List<ZoneKey> _ordemDoCache = [];
	private const int TetoDoCache = 2;

	/// <summary>
	/// As cenas ja lidas do disco. Segurar a referencia e o que MANTEM o recurso no cache do
	/// Godot: ele solta no instante em que a ultima referencia morre, e a versao anterior guardava
	/// a cena numa variavel LOCAL -- ou seja, relia e reprocessava o arquivo inteiro toda vez.
	/// </summary>
	private readonly Dictionary<string, PackedScene> _cenas = [];

	/// <summary>Os mapas de colisao e de visao, que nunca mudam. 312 KB por zona.</summary>
	private readonly Dictionary<string, ZoneCollision?> _mapas = [];

	/// <summary>
	/// O TILESET, SEGURADO PELO PROCESSO INTEIRO.
	///
	/// ============================ O PASSAGEIRO ESCONDIDO ============================
	/// Dentro dos ~940 ms que carregar um planeta custava, ~274 ms eram ESTE arquivo -- e ele nao
	/// melhora em binario (medido: 274 ms em texto contra 275 em binario). Nao e parse: e montar
	/// 125 fontes de atlas e ~7.800 tiles.
	///
	/// Quem o mantinha vivo eram os TileMapLayer da zona carregada. Ou seja Terra -> Espaco ->
	/// Terra: o espaco nao tem cena, a ultima referencia morria, e voltar pra um planeta pagava os
	/// 274 ms de novo. Uma referencia estatica custa o mesmo tanto de memoria que ja se pagava
	/// enquanto havia um planeta na tela, e elimina o caso.
	///
	/// `static` de proposito: o custo e do PROCESSO, nao da cena do mundo -- relogar nao deve
	/// remonta-lo.
	/// ===============================================================================
	/// </summary>
	private static TileSet? _tilesetVivo;
	private Node2D _atores = null!;

	/// <summary>
	/// AS PORTAS DA ZONA ATUAL, por celula. Vivem fora da cena do planeta de proposito.
	///
	/// A cena fica CACHEADA entre visitas (ver <see cref="_zonasVivas"/>), e porta tem ESTADO --
	/// pendura-la la dentro faria o estado da visita passada voltar junto com o cenario. Aqui elas
	/// nascem e morrem com a entrada na zona, e quem diz o estado e sempre o servidor.
	/// </summary>
	private Node2D _portas = null!;
	private readonly Dictionary<(int X, int Y), Porta> _portasPorCelula = [];

	private Visao _veu = null!;
	private CeuDoEspaco _ceu = null!;
	private Node2D _orbes = null!;
	// quem e quem: o nome chega junto com a aparencia, UMA vez, e a aba People precisa dele
	private readonly Dictionary<int, string> _nomes = [];

	public override void _Ready()
	{
		Instancia = this;
		const string dados = "res://Assets/Data/visual.json";
		if (Godot.FileAccess.FileExists(dados))
			_visual = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
		else GD.PushWarning("[world] sem visual.json -- rode o AssetPipeline (comando 'visual')");

		// ORDENAR POR Y, do mundo inteiro. E o que poe o personagem ATRAS da arvore quando ele
		// esta acima dela e NA FRENTE quando esta abaixo. O Godot so funde a ordenacao quando
		// ela e CONTINUA de pai pra filho: se `Atores` ou a cena do planeta nao ordenassem, cada
		// um viraria um bloco fechado e os dois nunca se intercalariam.
		YSortEnabled = true;

		// SEGURA O TILESET antes de qualquer zona -- ver o campo _tilesetVivo. Feito uma vez por
		// processo; sem ele, toda volta do espaco re-paga ~274 ms de montagem de atlas.
		_tilesetVivo ??= ResourceLoader.Load<TileSet>("res://Assets/Maps/tileset.tres");

		MontarCenario();

		_atores = new Node2D { Name = "Atores", YSortEnabled = true };
		AddChild(_atores);

		// AS PORTAS ORDENAM POR Y como todo o resto: elas sao parede, e quem passa por baixo tem
		// que aparecer na frente. Fora de `Atores` porque a lista delas e trocada inteira a cada
		// zona, e varrer os atores procurando porta seria trabalho pra nada.
		_portas = new Node2D { Name = "Portas", YSortEnabled = true };
		AddChild(_portas);

		_veu = new Visao { Name = "Visao" };
		AddChild(_veu);
		MontarDecalques();

		_ceu = new CeuDoEspaco { Name = "Ceu", Visible = false };
		AddChild(_ceu);
		_orbes = new Node2D { Name = "Planetas" };
		AddChild(_orbes);

		if (GameClient.Instance is { } cli)
		{
			cli.Joined += AoEntrar;
			cli.SnapshotReceived += AoReceberSnapshot;
			cli.PeerLeft += AoSair;
			cli.PeerLooked += AoReceberAparencia;
			cli.Falou += AoFalar;
			cli.Golpe += AoGolpe;
			cli.FormaMudou += AoMudarForma;
			cli.OozaruMudou += AoVirarOozaru;
			cli.VizinhancaMudou += DesenharPlanetas;
			cli.ObrasMudaram += DesenharObras;
			cli.EfeitoCaiu += AoCairEfeito;
			cli.Piscou += AoPiscar;
			cli.ClashBaque += AoBaqueDeEmbate;

			// O MEU CORPO SOME TAMBEM. O bit `Oculto` do snapshot esconde os OUTROS -- o corpo local
			// nao vem por snapshot, entao ele continuava na tela se teleportando a cada meio segundo
			// no meio de uma cena que promete "ninguem consegue ver os dois". A foto da bancada
			// mostrou exatamente isso: o boneco parado no centro, sob as raias de velocidade.
			//
			// Nao mexe na invisibilidade da TECNICA (ali o dono continua se vendo, que e o certo pra
			// quem escolheu sumir e precisa se localizar): isto vale so pelo embate.
			// ============================ NENHUMA LAMBDA DAQUI PRA BAIXO ============================
			// Estas sete assinaturas eram lambdas anonimas e por isso NAO TINHAM COMO ser canceladas
			// no `_ExitTree` -- `-=` exige o mesmo delegate, e lambda nova nunca e igual a anterior.
			//
			// O `GameClient` sobrevive ao logout (o `VoltarAoLogin` derruba os filhos do Boot, e ele
			// nao e um deles), entao o World MORTO continuava assinado. A pior delas era o
			// `ZoneChanged`: na sessao seguinte, trocar de mapa fazia o World velho tambem carregar
			// zona, mexendo em `_zonaAtual` -- um `PlanetaPreFeito` ja liberado. E como esse caminho
			// passa pelo `TelaDeCarregamento.Cobrir`, que e `async void`, a excecao escapava sem
			// ninguem pegar.
			// =======================================================================================
			cli.ClashComecou += AoComecarEmbate;
			cli.ClashAcabou += AoAcabarEmbate;
			cli.ClashVislumbre += AoVislumbre;
			cli.ClimaMudou += AoMudarClima;
			cli.RaioCaiu += AoCairRaio;
			_luzDoMundo.Forcado = cli.ClimaForcado;

			cli.PortasMudaram += AoMudarPortas;
			cli.CenarioCaiu += AoCairCenario;
			cli.CenarioRefeito += AoRefazerCenario;
			cli.FeridasMudaram += AoMudarFeridas;
			// O Boot instancia o World DENTRO do callback de Joined, ou seja, este _Ready
			// roda DEPOIS do evento. Assinar nao basta: se ja entramos, aplica agora.
			if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.Zone, cli.LocalSpawn, cli.LocalName);

			cli.ZoneChanged += AoMudarZona;
		}
	}

	public override void _ExitTree()
	{
		// OS PLANETAS GUARDADOS TEM QUE MORRER AQUI, e isto nao e opcional.
		//
		// Um node fora da arvore e sem pai e ORFAO: nada o coleta. O GC do C# tambem nao -- pra um
		// `Node` comum o Dispose so solta o involucro gerenciado, o objeto nativo fica. Sem esta
		// varredura, cada volta ao menu (ou cada relog) abandonaria dezenas de MB por zona
		// cacheada, e o vazamento so apareceria depois de horas jogando.
		foreach (Node2D no in _zonasVivas.Values)
			if (GodotObject.IsInstanceValid(no)) { SoltarDaArvore(no); no.Free(); }
		_zonasVivas.Clear();
		_ordemDoCache.Clear();

		if (Instancia == this) Instancia = null;
		if (GameClient.Instance is { } cli)
		{
			cli.Joined -= AoEntrar;
			cli.SnapshotReceived -= AoReceberSnapshot;
			cli.PeerLeft -= AoSair;
			cli.PeerLooked -= AoReceberAparencia;
			cli.Falou -= AoFalar;
			cli.Golpe -= AoGolpe;
			cli.FormaMudou -= AoMudarForma;
			// METODO NOMEADO E `-=`, e nao lambda: ver a nota do `_Ready`. O `GameClient` sobrevive
			// ao logout, entao assinatura que nao se cancela vira ouvinte orfao na sessao seguinte.
			cli.OozaruMudou -= AoVirarOozaru;
			cli.VizinhancaMudou -= DesenharPlanetas;
			cli.ObrasMudaram -= DesenharObras;
			cli.EfeitoCaiu -= AoCairEfeito;
			cli.Piscou -= AoPiscar;
			cli.PortasMudaram -= AoMudarPortas;
			cli.CenarioCaiu -= AoCairCenario;
			cli.CenarioRefeito -= AoRefazerCenario;
			cli.FeridasMudaram -= AoMudarFeridas;
			// Os sete que faltavam -- ver a nota no `_Ready`. O `ZoneChanged` e o mais grave: um
			// World morto ainda assinado carrega zona por cima da sessao nova.
			cli.ClashComecou -= AoComecarEmbate;
			cli.ClashAcabou -= AoAcabarEmbate;
			cli.ClashVislumbre -= AoVislumbre;
			cli.ClashBaque -= AoBaqueDeEmbate;
			cli.ClimaMudou -= AoMudarClima;
			cli.RaioCaiu -= AoCairRaio;
			cli.ZoneChanged -= AoMudarZona;
		}
	}

	// =====================================================================
	// OS AVISOS DO SERVIDOR QUE ERAM LAMBDA
	// =====================================================================
	private void AoComecarEmbate(int _, int __, int ___, float ____, float _____)
	{
		if (_local != null) _local.Visible = false;
	}

	private void AoAcabarEmbate(int _, int __)
	{
		if (_local != null) _local.Visible = true;
	}

	/// <summary>
	/// O VISLUMBRE: por um quinto de segundo o meu proprio corpo volta a tela, ja no meio de um
	/// golpe. Pros OUTROS isto chega sozinho pelo snapshot (bit `Oculto` + pose Atacando); so o
	/// corpo local precisa de aviso, porque e o unico que nao vem por snapshot.
	/// </summary>
	private void AoVislumbre(bool aparece, Jandirus.Core.World.Facing olhar)
	{
		if (_local == null) return;
		_local.Visible = aparece;
		// PELO DONO DA POSE, e nao escrevendo no visual direto. O corpo local reescreve o
		// proprio estado a cada quadro (`LocalPlayer.LerAcoes`), entao uma pose posta por
		// fora durava um quadro so -- e o vislumbre mostrava o OUTRO socando e eu parado.
		//
		// A DIRECAO VEM DO SERVIDOR: no embate quem decide quem encara quem e ele, e o
		// teclado do jogador nao opina. Sem isto os dois socavam pra lados diferentes.
		if (aparece) _local.PosarDeGolpe(Jandirus.Net.Protocol.AttackPoseMs / 1000.0, olhar);
	}

	/// <summary>
	/// O CLIMA FORCADO: so ele vem do servidor. O natural as duas pontas calculam do mesmo tempo
	/// do mundo -- ver `S2C.Clima`.
	/// </summary>
	private void AoMudarClima()
	{
		if (_luzDoMundo != null && GameClient.Instance is { } cli) _luzDoMundo.Forcado = cli.ClimaForcado;
	}

	/// <summary>
	/// O RAIO CAI NUM PONTO DO MAPA, e o servidor conta pra zona inteira: quem esta olhando pra la
	/// ve o risco, quem nao esta ve o clarao e ouve o trovao atrasado.
	/// </summary>
	private void AoCairRaio(Vec2 onde, float semente) =>
		_luzDoMundo?.Raio(new Vector2(onde.X, onde.Y), semente);

	private void AoMudarZona(ZoneKey z, Vec2 spawn)
	{
		// GUARDA ANTES DE TROCAR: a posicao de agora ainda e a da zona VELHA. Se ela era o
		// espaco, e ali que a nave ficou. Ver `UltimaNoEspaco`.
		if (Jandirus.Core.World.Espaco.EhEspaco(_zonaDoAtual) && PosicaoLocal is { } antes)
			UltimaNoEspaco = antes;

		// ============================ OS CORPOS DA ZONA VELHA NAO VEM JUNTO ============================
		// Todo `RemotePlayer` na tela e de quem estava na zona ANTERIOR -- o corte de
		// interesse do servidor so manda snapshot de quem divide a zona comigo. Ao trocar,
		// aqueles bonecos deixam de receber pacote e ficam parados no lugar onde estavam,
		// agora sobre o cenario do planeta novo.
		//
		// Limpar aqui e o certo e nao custa nada: quem estiver na zona nova chega no
		// primeiro snapshot, que vem no tique seguinte.
		// ================================================================================================
		EsvaziarRemotos();

		// A TELA DE CARREGAMENTO ENTRA ANTES DO TRABALHO. Ver `TelaDeCarregamento`: a
		// montagem do tilemap bloqueia a thread principal por quase um segundo, e sem uma
		// tela no ar isso e indistinguivel de o jogo ter travado.
		//
		// O `Teleportar` vai DENTRO do mesmo bloco: mover o corpo antes da zona carregar o
		// poria por um quadro nas coordenadas novas sobre o cenario velho.
		void Trocar()
		{
			CarregarZona(z, new Vector2(spawn.X, spawn.Y));
			_local?.Teleportar(spawn);
		}

		if (TelaDeCarregamento.Instancia is { } tela) tela.Cobrir(NomeDaZona(z), Trocar);
		else Trocar();
	}

	/// <summary>
	/// UM EFEITO CAIU (ou saiu) DE MIM -- e este metodo existe porque o canal estava ORFAO.
	///
	/// ============================ A API QUE NINGUEM CHAMAVA ============================
	/// O servidor mandava `carga_inicio`, `carregando`, `aura_carga` e `aura_ki` desde o comeco. O
	/// unico assinante do canal tratava so `cegueira`; todo o resto chegava pela rede e caia no
	/// chao. Enquanto isso o cliente acendia a aura DA TECLA, ignorando a recusa do servidor -- que
	/// e como o dono acabou vendo luz e som sem ter Ki Unlocked.
	///
	/// E o mesmo defeito que este projeto ja cometeu no sigilo do BP e nas flags de nivel: a regra
	/// escrita de um lado e ninguem do outro. **Escrever a regra e ligar a regra sao dois
	/// trabalhos.**
	/// ==================================================================================
	///
	/// SO O CORPO LOCAL PASSA POR AQUI. Os outros da zona ja vem resolvidos no snapshot (que
	/// carrega os mesmos dois bits) -- o corpo local e que fica de fora dele, porque ele se desenha
	/// sozinho.
	/// </summary>
	/// <remarks>
	/// INTERNAL pelo mesmo motivo do <see cref="AoMudarForma"/>: e por aqui que o `aura_ki` chega, e
	/// e a sobrecarga que decide se o contorno aparece. Uma bancada que escrevesse `_sobrecarregados` na
	/// mao estaria testando o campo, nao o canal.
	/// </remarks>
	internal void AoCairEfeito(string efeito, long ms)
	{
		bool ligado = ms != 0;

		switch (efeito)
		{
			// A CEGUEIRA E O CAMPO DE VISAO INDO A ZERO -- ver Visao.CegoAte.
			case "cegueira":
				_veu.CegoAte = ms <= 0 ? 0 : Time.GetTicksMsec() + (ulong)ms;
				break;

			// O ZUMBIDO segue o ESTADO da carga, e nao a aura -- e a divisao do proprio DM: o
			// `poweruprunning`, que liga o laco do `aurapowered.wav`, fica FORA do gate do
			// `canPower` (Meditate.dm:193). Quem so tem Ki Unlocked ouve a energia subindo mesmo
			// sem aura nenhuma, e e o unico retorno que ele tem de que a tecla fez alguma coisa.
			case "carregando":
				if (_local?.GetNodeOrNull<CargaVisual>("Carga") is { } som) som.Som(ligado);
				break;

			// ============================ O VOO TINHA FICADO MUDO ============================
			// O original toca `buku.wav` ao sair do chao e `buku_land.wav` nos DOIS jeitos de
			// descer (pousar de proposito, flying.dm:91, e cair de exausto, Stats.dm:422). Os dois
			// arquivos ja estavam convertidos e ninguem os chamava.
			//
			// EU TINHA POSTO UM ZUMBIDO EM LACO por cima disso, argumentando que um estado caro e
			// silencioso e um estado que o jogador esquece ligado. O dono cortou -- "enquanto ta
			// voando n deveria ter som nenhum" --, e ele esta certo por dois motivos: o original
			// tambem e mudo no ar, e um zumbido que dura minutos vira ruido, nao aviso. Quem lembra
			// que o voo esta ligado e a barra de Ki descendo.
			// ================================================================================
			case "voo":
				if (ligado && _local != null) AudioDirector.EfeitoNoLugar(_local, Trilha.Decolagem, 0.8f);
				break;

			case "pouso":
				if (_local != null) AudioDirector.EfeitoNoLugar(_local, Trilha.Pouso, 0.8f);
				break;

			// A AURA DO POWER-UP: essa sim pede controle de ki de verdade
			// (`canPower && stamina > 1`, o gate do Meditate.dm:181).
			case "aura_carga":
				_auraDaCarga = ligado;
				AplicarChamaDaCargaLocal();
				break;

			// PASSOU DOS 110% DE KI. Independente da tecla: quem esta sobrecarregado continua
			// aceso depois de soltar o C, e so apaga quando o Ki volta pro lugar.
			//
			// ENTRA PELO MESMO FUNIL DO CORPO ALHEIO (`MarcarSobrecarga`), e nao num campo proprio:
			// este canal e o meu Ki, o bit `EntityState.Sobrecarregado` e o Ki dos outros, e os dois
			// dizem A MESMA COISA. Enquanto eram dois estados separados o contorno tinha duas regras
			// (Ki no meu corpo, FORMA no dos outros) -- e era a errada que os outros viam.
			case "aura_ki":
				MarcarSobrecarga(GameClient.Instance?.LocalId ?? 0, ligado);
				break;
		}
	}

	/// <summary>
	/// ALGUEM PISCOU. A miragem nasce em `de` -- a posicao de ONDE o corpo saiu, que veio no
	/// pacote justamente porque quando ele chega o corpo ja esta no destino.
	/// </summary>
	/// <summary>O relato do golpe chegou com investida: o `AoPiscar` do mesmo gesto nao toca som.</summary>
	private bool _investiuAgora;

	private void AoPiscar(int quem, Vec2 de)
	{
		Node2D? corpo = Corpo(quem);
		if (corpo == null) return;

		// O MEU VULTO SAI PELO MESMO CAMINHO DO SOCO. Pro corpo LOCAL, quem sabe de onde ele saiu e
		// o proprio cliente -- ele guardou a posicao no instante do duplo clique. A posicao que vem
		// no pacote e do SERVIDOR: esta atrasada e chega por outro canal, sem ordem garantida com a
		// correcao que move o corpo. Ver `LocalPlayer.DeixarVulto`.
		if (GameClient.Instance?.LocalId == quem && _local != null) _local.DeixarVulto();
		else Zanzoken.Deixar(_atores, corpo, new Vector2(de.X, de.Y));

		// O SOM DE TELEPORTE SO NO TELEPORTE. Desde que o dash passou a anunciar pelo mesmo pacote,
		// um shift+espaco de quem tem Afterimage tocava DOIS sons -- o rasgo da investida (por
		// `h.Investiu`) e este. Sao gestos diferentes: investir e correr pra cima do outro, piscar e
		// sumir de um lugar e aparecer noutro.
		if (!_investiuAgora) AudioDirector.EfeitoNoLugar(corpo, Trilha.Teleporte, 0.7f);
		_investiuAgora = false;
	}

	/// <summary>
	/// O BAQUE INVISIVEL DO ZANZO CLASH: os dois corpos se cruzaram AQUI.
	///
	/// ============================ POR QUE E SO ISTO QUE SE VE ============================
	/// Os dois estao invisiveis, e o dono foi explicito sobre os teleportes: "esses teleporte n sao
	/// zanzoken entao n tem after image". Entao nao ha corpo nem vulto pra desenhar -- o que a
	/// vista pega e o ESTRAGO, e e o estrago que faz o encontro parecer rapido demais pra ser
	/// visto, em vez de dois bonecos piscando pela tela.
	/// ====================================================================================
	///
	/// Reusa o vocabulario de impacto que ja existe (faisca, anel, poeira, tremor, som), sem
	/// inventar um efeito proprio: um encontro de dois lutadores tem que soar como os golpes que o
	/// jogo ja da, so que sem quem os deu.
	/// </summary>
	private void AoBaqueDeEmbate(Vec2 onde)
	{
		var p = new Vector2(onde.X, onde.Y);

		// O MESMO VOCABULARIO DO CRITICO, que e o golpe mais forte que o jogo desenha: faisca
		// quente grande, anel dourado largo e um anel de gelo por cima (o encontro e de velocidade,
		// nao de forca bruta). Sem os dois aneis o cruzamento sumia no meio do cenario.
		CombatFx.Impacto(_atores, p, 1.6f, Quente);
		CombatFx.Onda(_atores, p, 96, Dourado, 0.28);
		CombatFx.Onda(_atores, p, 150, Gelo, 0.34);
		PoeiraDeEstrago.Soltar(_atores, p);

		// SO TREME PRA QUEM ESTA NO EMBATE. Um cruzamento a cada meio segundo sacudiria a tela de
		// quem so passava por perto seis a nove vezes seguidas -- e a regra do `Tremer` ja diz que
		// tremor e de quem esta na briga.
		if (GameClient.Instance?.EmClash == true) Sacudir(7f);

		// SOM NO PONTO, e nao no corpo: nao ha corpo visivel ali. Um marcador vive o tempo do
		// efeito e se apaga sozinho.
		var eco = new Node2D { Position = p };
		_atores.AddChild(eco);
		AudioDirector.EfeitoNoLugar(eco, Trilha.Teleporte, 0.85f);
		AudioDirector.EfeitoNoLugar(eco, Trilha.Acerto(3), 0.7f);
		GetTree().CreateTimer(2.0).Timeout += () => { if (IsInstanceValid(eco)) eco.QueueFree(); };
	}

	/// <summary>Segurando o C. So do meu corpo -- nos outros isso e o bit `EntityState.Carregando`.</summary>
	private bool _auraDaCarga;

	/// <summary>
	/// QUEM ESTA ACIMA DOS 100% DE KI, por id -- e o MEU id entra aqui como qualquer outro.
	///
	/// ============================ UM CONJUNTO, E NAO UM BOOL MEU MAIS UM BIT DELES ============================
	/// Este campo era um `bool _sobrecarga` que so falava do dono da tela, e a consequencia estava
	/// escrita com todas as letras no `AcenderFormaNoCorpo`: *"o cliente nao sabe o Ki dos OUTROS"*.
	/// Nao sabia MESMO, quando aquilo foi escrito -- hoje sabe: `EntityState.Sobrecarregado` viaja no
	/// snapshot 30x/s por corpo (`Protocol.cs:1104`, escrito em `GameServer.cs:2384`) e ja era
	/// consumido aqui, so que entregue exclusivamente a `CargaVisual`.
	///
	/// Enquanto a verdade do meu Ki morava num campo e a dos outros num bit, o contorno tinha DUAS
	/// regras pro mesmo pixel -- Ki no meu corpo, FORMA no corpo alheio -- e quem se transformava
	/// ficava contornado pra sempre na tela dos outros. Um conjunto por id fecha a divergencia por
	/// construcao: nao ha "o meu caso" pra alguem esquecer de atualizar.
	/// =====================================================================================================
	/// </summary>
	private readonly HashSet<int> _sobrecarregados = [];

	/// <summary>O MEU Ki passou dos 100%? Derivado do mesmo conjunto que serve os corpos remotos.</summary>
	private bool SobrecargaLocal =>
		GameClient.Instance is { } c && _sobrecarregados.Contains(c.LocalId);

	/// <summary>
	/// O KI DE ALGUEM PASSOU (ou voltou) DOS 100% -- o FUNIL UNICO dos dois caminhos.
	///
	/// Entram por aqui o canal de efeito do meu corpo (`aura_ki`, que o servidor manda so pra mim) e o
	/// bit do snapshot de todos os outros. Sai daqui o contorno, pelo mesmo `AplicarContorno` nos dois
	/// casos -- e a chama da carga, que so o meu corpo desenha a partir deste lado (nos outros ela ja
	/// vem resolvida no proprio snapshot).
	///
	/// SO TRABALHA NA MUDANCA: o bit chega por tique, por corpo, e reescrever os uniformes de todas as
	/// camadas de todo mundo 30x/s pra dizer a mesma coisa e trabalho puro. O `Add`/`Remove` do
	/// conjunto ja responde "mudou?" sem um campo a mais pra manter em dia.
	/// </summary>
	private void MarcarSobrecarga(int id, bool ligado)
	{
		// ZERO E "NINGUEM" NESTE ARQUIVO INTEIRO (ver `Corpo`), e sem esta linha ele entraria no
		// conjunto e ficaria la pra sempre -- ninguem sai da zona com id 0 pra apaga-lo.
		if (id == 0) return;
		if (!(ligado ? _sobrecarregados.Add(id) : _sobrecarregados.Remove(id))) return;
		AplicarContorno(id);
		// A CHAMA DA CARGA DO MEU CORPO TAMBEM DEPENDE DISTO: quem passa dos 100% continua com a chama
		// acesa depois de soltar o C (ver `CargaVisual.Definir`). Nos corpos remotos essa mesma linha
		// e escrita pelo snapshot, com os dois bits que vem nele.
		if (GameClient.Instance is { } c && id == c.LocalId) AplicarChamaDaCargaLocal();
	}

	/// <summary>
	/// ============================ QUEM ESTA DIRIGINDO ESTE CORPO MUDOU ============================
	/// Gemeo do <see cref="MarcarSobrecarga"/> logo acima, e escrito no mesmo molde de proposito: um
	/// bit do snapshot, um conjunto por zona, e trabalho SO quando ele vira. A 30 Hz, repintar o olho
	/// todo tique seria escrever o mesmo uniform de shader trinta vezes por segundo pra cada corpo em
	/// campo -- e a virada e o unico instante que interessa.
	///
	/// ============================ POR QUE SO O OLHO, E NAO UM REVESTIR INTEIRO ============================
	/// Porque revestir a forma atual DESTRUIRIA O OOZARU: quem virou macaco tem a escada na BASE (o
	/// `Apeshit` reverte antes de chamar a fera), entao um `VestirAFormaSemCena` disparado pela virada
	/// da posse arrancaria o corpo do macaco exatamente no instante em que ele perde o controle -- o
	/// unico instante em que ela acontece. Ver `CharacterVisual._formaVestida`, que e quem sabe qual
	/// forma esta no corpo e repinta so o que a posse muda.
	/// ================================================================================================
	///
	/// `internal` pelo mesmo motivo do <see cref="AoMudarForma"/>: e por aqui que o bit do fio vira
	/// pixel, e a bancada precisa exercitar ESTE caminho e nao o `MarcarSemRedeas` na mao -- provar que
	/// o metodo funciona sem provar que alguem o chama e o defeito que a fase inteira veio consertar.
	/// </summary>
	internal void AoMudarPosse(int id, bool semRedeas)
	{
		// ZERO E "NINGUEM" NESTE ARQUIVO INTEIRO, e o conjunto guarda quem SAI da zona pela mesma
		// porta que o `_sobrecarregados`: pelo bit chegando `false`.
		if (id == 0) return;
		if (!(semRedeas ? _semRedeasDaZona.Add(id) : _semRedeasDaZona.Remove(id))) return;

		if (Corpo(id)?.GetNodeOrNull<CharacterVisual>("Visual") is { } vis)
			vis.MarcarSemRedeas(semRedeas);
	}

	/// <summary>
	/// A CHAMA DE CARGA DO MEU CORPO. Nulo antes de entrar no mundo.
	///
	/// So o corpo LOCAL passa por aqui, e a razao nao mudou: os outros vem resolvidos no snapshot, que
	/// carrega os mesmos dois bits (`World.AoReceberSnapshot`); o corpo local e que fica de fora dele.
	/// </summary>
	private void AplicarChamaDaCargaLocal()
	{
		if (_local?.GetNodeOrNull<CargaVisual>("Carga") is { } cg)
			cg.Definir(_auraDaCarga || SobrecargaLocal, SobrecargaLocal);
	}

	// ---------------------------------------------------------------------
	// cenario de teste + iluminacao
	// ---------------------------------------------------------------------
	private void MontarCenario()
	{
		// chao de reserva: so aparece se a zona nao tiver cena (planeta procedural ainda sem
		// mapa, ou manifesto faltando). Com a cena carregada ele fica atras e nao se ve.
		var chao = new ColorRect
		{
			Color = new Color("2f3b2a"),
			Size = new Vector2(1600, 1200),
			Position = new Vector2(-200, -200),
			ZIndex = -100,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(chao);

		// A LUZ DO MUNDO mora numa classe propria: ciclo do dia, fontes de cenario e a visao
		// do personagem. Ver Iluminacao.cs -- inclusive pra entender por que o dia nao e
		// branco puro (sem margem entre luz e sombra, parede nao esconde nada).
		_luzDoMundo = new Iluminacao { Name = "Iluminacao" };
		AddChild(_luzDoMundo);
	}

	// ---------------------------------------------------------------------
	// rede -> cena
	// ---------------------------------------------------------------------
	/// <summary>
	/// Instancia a cena do planeta (decisao do dono: cada pre-feito e uma cena propria).
	/// Trocar de zona descarrega a anterior -- e o que impede carregar Namek pra quem esta
	/// na Terra, do mesmo jeito que o corte de interesse impede receber os pacotes de la.
	/// </summary>
	/// <param name="centro">
	/// ONDE O CORPO VAI APARECER, em pixels de mundo. O cenario e montado em volta DELE e nao da
	/// camera: quando esta funcao roda, a camera ainda esta no mapa ANTERIOR (quem a move e o
	/// `Teleportar`, que so acontece depois), e montar em volta dela poria o chao do outro lado do
	/// planeta. Nulo = nao sei, use a camera (o caminho do recarregamento por admin, em que
	/// ninguem saiu do lugar).
	/// </param>
	private void CarregarZona(ZoneKey zona, Vector2? centro = null)
	{
		GuardarZonaAtual();

		// O CEU DO LUGAR, ANTES DE QUALQUER SAIDA. Cada planeta tem a propria rotacao, a propria
		// defasagem, a propria lua e os proprios climas (ver `Core.World.Ceu` e `Core.World.Clima`),
		// e esta funcao tem cinco caminhos que terminam em `return` -- espaco, gerado, sem cena,
		// pre-feita, erro. Pendurar isto num deles deixaria os outros quatro com o ceu do planeta
		// ANTERIOR: chuva de sangue de Vegeta caindo na Terra.
		_luzDoMundo.Relogio = Planetas.Relogio(zona);
		_luzDoMundo.ClimaDaqui = Planetas.Clima(zona);
		_luzDoMundo.SalDoClima = Jandirus.Core.World.Clima.SalDaZona(zona);

		_catalogo ??= Godot.FileAccess.FileExists(Manifesto)
			? ZoneCatalog.Parse(Godot.FileAccess.GetFileAsString(Manifesto))
			: null;

		// O ESPACO NAO TEM CENA. Nao ha .dmm nem tileset: o que se ve e gerado por chunk (ver
		// CeuDoEspaco) e os planetas chegam do servidor. Sem colisao e sem veu, tambem -- no
		// vazio nao ha parede pra bater nem pra esconder nada.
		bool espaco = Jandirus.Core.World.Espaco.EhEspaco(zona);
		_ceu.Visible = espaco;
		_ceu.Seed = GameClient.Instance?.SeedDoUniverso ?? 0;
		if (espaco)
		{
			_colisao = null;
			_veu.Mapa = null;
			_veu.Colisao = null;
			_veu.Camadas = [];
			_decalques?.Limpar();   // marca da Terra nao vai pra Namek
			if (_local != null) _local.Mapa = null;
			_zonaDoAtual = zona;   // ver o comentario no ramo pre-feito
			DesenharPlanetas();
			AudioDirector.Instance?.Ambiente("");
			GD.Print("[world] zona: ESPACO (gerado por chunk)");
			Chat.Sistema("voce esta no espaco. Encoste num planeta pra pousar.");
			return;
		}
		foreach (Node n in _orbes.GetChildren()) n.QueueFree();

		// PLANETA GERADO: nao ha cena no catalogo, e nem deveria haver -- ele nasce da seed.
		// Este ramo tem que vir ANTES da consulta ao catalogo, senao a zona cai no aviso de
		// "sem cena" e o jogador pousa num chao provisorio em vez do planeta dele.
		if (zona.Kind == Jandirus.Core.World.ZoneKey.KindProcedural)
		{
			var gerado = new PlanetaProcedural { Name = "Planeta" };
			// MESMO LUGAR DA CENA PRE-FEITA (`_zonaAtual`): e o que garante que a proxima troca de
			// zona derrube o planeta gerado junto, pelo caminho que ja existe. Pendurar num no
			// paralelo deixaria o mundo antigo desenhado por baixo do novo.
			_zonaAtual = gerado;
			_zonaDoAtual = zona;   // a chave do cache: ver o comentario no ramo pre-feito
			AddChild(gerado);
			MoveChild(gerado, 0);   // atras dos atores, como a cena pre-feita

			// ONDE MONTAR O CHAO, antes de gerar: o planeta so pinta o que a camera alcanca, e a
			// camera ainda esta no mapa de onde o jogador saiu (ver o parametro `centro`).
			gerado.CentroInicial = centro;
			gerado.PedacoPintado -= ReaplicarEstrago;
			gerado.PedacoPintado += ReaplicarEstrago;
			gerado.Entrar(zona.Seed);   // e a seed do SERVIDOR que decide o mundo

			_colisao = gerado.Colisao;
			_veu.Mapa = gerado.Sombra ?? gerado.Colisao;
			_veu.Colisao = gerado.Colisao;
			_veu.Camadas = CamadasDoCenario(gerado);
			if (_local != null) _local.Mapa = _colisao;
			DesenharObras();
			ReaplicarEstrago();
			AudioDirector.Instance?.Ambiente("");
			GD.Print($"[world] zona GERADA: {gerado.Ficha()}");
			Chat.Sistema(gerado.Ficha());
			return;
		}

		ZoneEntry? e = _catalogo?.Get(zona);
		if (e == null || !ResourceLoader.Exists(e.Cena))
		{
			GD.PushWarning($"[world] sem cena pra zona '{zona}' -- usando o chao provisorio");
			_colisao = null;
			if (_local != null) _local.Mapa = null;
			_zonaDoAtual = zona;   // a quarta saida: ver o comentario no ramo pre-feito
			return;
		}

		ulong tLoad = Time.GetTicksUsec();

		// AINDA VIVO? Entao nao ha o que ler nem instanciar -- so recolocar na arvore.
		bool jaNaArvore = false;
		if (_zonasVivas.Remove(zona, out Node2D? guardado) && GodotObject.IsInstanceValid(guardado))
		{
			_ordemDoCache.Remove(zona);
			_zonaAtual = guardado;

			// ELA NUNCA SAIU DA ARVORE (ver `GuardarZonaAtual`): voltar e so reacender. Sem isto o
			// `AddChild` la embaixo reclamaria de um node que ja tem pai.
			jaNaArvore = guardado.GetParent() == this;
			if (jaNaArvore)
			{
				guardado.Visible = true;
				guardado.ProcessMode = ProcessModeEnum.Inherit;
			}

			GD.Print($"[perf] {e.Zona}: DA MEMORIA em {(Time.GetTicksUsec() - tLoad) / 1000.0:0.0} ms");
		}
		else
		{
			if (!_cenas.TryGetValue(e.Cena, out PackedScene? cena))
			{
				cena = ResourceLoader.Load<PackedScene>(e.Cena);
				_cenas[e.Cena] = cena;   // segura a referencia: e isto que mantem o recurso cacheado
			}
			ulong tInst = Time.GetTicksUsec();
			_zonaAtual = cena.Instantiate<Node2D>();
			GD.Print($"[perf] {e.Zona}: load {(tInst - tLoad) / 1000.0:0.0} ms"
					 + $" | instantiate {(Time.GetTicksUsec() - tInst) / 1000.0:0.0} ms");
		}

		// SEM z_index PROPRIO. Um z fixo separaria a cena dos atores em duas camadas estanques
		// e a arvore voltaria a ficar sempre na frente (ou sempre atras) do personagem. Quem
		// decide e o Y, e pra isso os dois precisam viver no MESMO z.
		//
		// O MoveChild(_, 0) NAO E ENFEITE: o AddChild poe no FIM da lista, e com YSortEnabled o
		// indice do filho e o desempate quando o Y empata -- sem ele o cenario inteiro desenha na
		// frente dos personagens. Vale igual pro caminho do cache.
		if (!jaNaArvore) AddChild(_zonaAtual);

		// O `MoveChild(_, 0)` VALE PROS DOIS CAMINHOS. Com `YSortEnabled`, o indice do filho e o
		// desempate quando o Y empata -- e as zonas escondidas no cache continuam na lista, entao
		// a que volta precisa ser reposta na frente ou o cenario desenha por cima dos corpos.
		MoveChild(_zonaAtual, 0);

		_zonaDoAtual = zona;

		// ============================ O CENARIO ENTRA AGORA, E SO O PEDACO DAQUI ============================
		// As camadas da cena vem VAZIAS: as celulas moram no `.pedacos` e chegam em blocos de 64x64
		// conforme a camera anda (ver `PlanetaPreFeito.Semear` e `PintorDePedacos`). Era isto ou
		// continuar pagando 708 ms de montagem no primeiro quadro toda vez que alguem troca de mapa.
		//
		// REASSINAR TODA VEZ, tirando antes: uma zona que volta do cache passa por aqui de novo, e
		// sem o `-=` o estrago seria reaplicado uma vez por visita acumulada.
		ulong tPed = Time.GetTicksUsec();
		if (_zonaAtual is PlanetaPreFeito pre)
		{
			pre.PedacoPintado -= ReaplicarEstrago;
			pre.PedacoPintado += ReaplicarEstrago;
			pre.Semear(e.Pedacos, centro);
			GD.Print($"[perf] {e.Zona}: {pre.PedacosVivos} pedaco(s) montado(s) na chegada");
		}

		ulong tCol = Time.GetTicksUsec();

		// A MESMA colisao que o servidor usa. Sem ela o cliente atravessa parede, o servidor
		// recusa e devolve correcao -- e e ESSA briga que faz o personagem tremer no muro.
		_colisao = MapaCacheado(e.Colisao);
		if (_colisao == null) GD.PushWarning($"[world] zona '{zona}' sem colisao: da pra atravessar parede");
		if (_local != null) _local.Mapa = _colisao;

		// O QUE CEGA e outro mapa: parede e porta cegam, arvore e cerca nao (ver MapConverter).
		_veu.Mapa = MapaCacheado(e.Visao);
		_veu.Colisao = _colisao;
		_veu.Camadas = CamadasDoCenario(_zonaAtual);
		if (_veu.Mapa == null) GD.PushWarning($"[world] zona '{zona}' sem mapa de visao: parede nao esconde nada");

		MontarPortas(e);

		// AS CONSTRUCOES TAMBEM, e isto CONSERTA UM DEFEITO ANTIGO: `DesenharObras` so rodava no
		// evento `ObrasMudaram`, e no login o pacote de construcoes chega ANTES de o `World`
		// existir (o servidor manda antes do `JoinAccepted`, de proposito). Ou seja: quem entrava
		// num planeta com construcoes de pe nao via NENHUMA delas ate alguem erguer ou aparafusar
		// outra. Passou despercebido enquanto elas eram so desenho; agora que elas viram parede,
		// seria o cliente atravessando o que o servidor barra.
		DesenharObras();

		// E O ESTRAGO QUE JA ESTAVA FEITO. Este ramo -- o do planeta PRE-FEITO, que e onde se joga
		// -- era o unico dos quatro que NAO reaplicava. O metodo existe justamente porque a versao
		// anterior disto era um `foreach` solto que caiu no ramo errado; ele foi extraido, ganhou o
		// comentario explicando o tombo... e continuou sendo chamado so no ramo do planeta gerado.
		//
		// O sintoma e o desync de mapa que o dono fotografou: o servidor tem buraco onde o cliente
		// desenha parede, os dois discordam sobre onde da pra andar, e o corpo treme no muro.
		ReaplicarEstrago();

		GD.Print($"[world] zona carregada: {e.Zona} (z{e.Z}, {e.W}x{e.H})"
				 + (_colisao != null ? " com colisao" : " SEM colisao"));
		Chat.Sistema($"voce esta em {e.Zona}.");

		// o som do LUGAR: vento, mar, cidade. Troca junto com o planeta.
		AudioDirector.Instance?.Ambiente(Trilha.AmbienteDe(e.Zona));
		AudioDirector.Instance?.Musica(Trilha.MusicaDe(e.Zona), AudioDirector.Camada.Lugar);

		ulong tLuz = Time.GetTicksUsec();
		// fogueiras, tochas e lava do planeta novo (as do anterior somem junto com ele)
		_luzDoMundo.CarregarLuzes(e.Luzes);

		// E O INDICE DE TILES, que a destruicao de cenario precisa. Aqui, e nao na primeira celula
		// que cai: la ele cairia no meio do quadro do primeiro golpe pesado da partida.
		CarregarIndiceDeTiles();
		GD.Print($"[perf] {e.Zona}: pedacos {(tCol - tPed) / 1000.0:0.0} ms"
				 + $" | mapas {(tLuz - tCol) / 1000.0:0.0} ms | luzes {(Time.GetTicksUsec() - tLuz) / 1000.0:0.0} ms"
				 + $" | TOTAL {(Time.GetTicksUsec() - tLoad) / 1000.0:0.0} ms");

		// E AGORA O QUE VEM DEPOIS. Ver `_fimDaCarga`: o desenho do tilemap so e montado no
		// primeiro quadro, e e ali que a janela congela.
		_fimDaCarga = Time.GetTicksUsec();
		_zonaMedida = e.Zona;
	}

	/// <summary>
	/// SAI DA ZONA ATUAL guardando-a viva, se couber no cache.
	///
	/// `RemoveChild` e nao `QueueFree`: fora da arvore o node nao processa nada (a SceneTree so
	/// chama `_Process` em quem esta nela) e o TileMapLayer solta sozinho, no `EXIT_TREE`, os
	/// canvas items de render e os occluders. Ou seja, guardar custa RAM e nao custa CPU nem GPU.
	///
	/// QUEM SAI DO CACHE E LIBERADO NA HORA, com `Free()` e nao `QueueFree()`: um node sem pai e
	/// sem arvore e ORFAO -- nada o coleta, e o GC do C# tambem nao (pra um node comum o Dispose
	/// so solta o involucro gerenciado). Esquecer isso e vazar dezenas de MB por troca de zona.
	/// </summary>
	private void GuardarZonaAtual()
	{
		if (_zonaAtual == null) return;

		ulong tSai = Time.GetTicksUsec();

		Node2D saindo = _zonaAtual;
		ZoneKey chave = _zonaDoAtual;
		_zonaAtual = null;

		// zona sem chave util (o mundo ainda nao entrou em nenhuma): nao da pra cachear
		if (chave.Hash == 0) { RemoveChild(saindo); saindo.Free(); return; }

		// ============================ SAIR DA ARVORE CUSTA CARO, E NAO PRECISAVA ============================
		// `RemoveChild` dispara `EXIT_TREE` no planeta inteiro, e um `TileMapLayer` de 500x500 solta
		// ali TODOS os canvas items de render e os occluders -- centenas de milhares deles. Voltar
		// pra zona reconstroi tudo de novo. Era o custo que nao aparecia em medicao nenhuma: os
		// marcos de `CarregarZona` comecam DEPOIS desta funcao, e o modo headless nem desenha.
		//
		// O cache ja aceitava o preco de manter a zona VIVA em memoria. Manter tambem NA ARVORE, so
		// que invisivel e sem processar, custa a mesma RAM e economiza os dois lados da conta: nao
		// desmonta ao sair, nao remonta ao voltar.
		//
		// INVISIVEL NAO DESENHA E NAO OCLUI: a visibilidade do Godot desce pela arvore inteira, e
		// occluder de node escondido nao entra no calculo de luz.
		// ====================================================================================================
		saindo.Visible = false;
		saindo.ProcessMode = ProcessModeEnum.Disabled;

		_zonasVivas[chave] = saindo;
		_ordemDoCache.Remove(chave);
		_ordemDoCache.Add(chave);

		while (_ordemDoCache.Count > TetoDoCache)
		{
			ZoneKey velha = _ordemDoCache[0];
			_ordemDoCache.RemoveAt(0);
			if (_zonasVivas.Remove(velha, out Node2D? no) && GodotObject.IsInstanceValid(no))
			{
				// SO QUEM SAI DO CACHE PAGA O DESMONTE -- e ai ele e inevitavel, porque a zona vai
				// mesmo embora. Uma vez a cada `TetoDoCache` trocas, em vez de a cada troca.
				SoltarDaArvore(no);
				no.Free();
			}
		}

		double ms = (Time.GetTicksUsec() - tSai) / 1000.0;
		if (ms > 5) GD.Print($"[perf] saindo de {chave.Name}: {ms:0.0} ms");
	}

	/// <summary>
	/// TIRA A ZONA DA ARVORE antes de liberar.
	///
	/// Desde que o cache passou a guardar as zonas DENTRO da arvore (escondidas), todo `Free()`
	/// precisa saber que pode haver um pai. O Godot desliga sozinho, mas fazer isso explicitamente
	/// e o que impede o proximo leitor de achar que zona guardada esta fora da arvore -- que era
	/// verdade ate aqui e deixou de ser.
	/// </summary>
	private void SoltarDaArvore(Node no)
	{
		if (no.GetParent() == this) RemoveChild(no);
	}

	/// <summary>
	/// O `.col` e o `.vis` de uma zona -- lidos UMA vez por sessao.
	///
	/// Sao 312 KB por zona que nunca mudam em runtime, e o caminho antigo relia os dois do disco a
	/// cada entrada. Custa pouco (0,3 ms medidos) e nao ha razao nenhuma pra pagar de novo; as 40
	/// zonas juntas dao ~12 MB.
	/// </summary>
	private ZoneCollision? MapaCacheado(string caminho)
	{
		if (_mapas.TryGetValue(caminho, out ZoneCollision? m)) return m;
		m = Godot.FileAccess.FileExists(caminho)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(caminho))
			: null;
		_mapas[caminho] = m;
		return m;
	}

	/// <summary>
	/// AS PORTAS DA ZONA: uma leva de nodes, trocada inteira a cada entrada.
	///
	/// TODAS NASCEM FECHADAS, e o `.col`/`.vis` da zona volta ao estado de arquivo. E o que impede
	/// a porta de ficar aberta pra sempre: os dois mapas sao CACHEADOS por sessao, entao sem este
	/// `FecharTudo` a abertura da visita passada sobreviveria a viagem ao outro planeta. Logo depois
	/// o servidor manda a lista do que esta aberto AGORA (ver `GameServer.MandarPortas`).
	/// </summary>
	private void MontarPortas(ZoneEntry e)
	{
		foreach (Porta p in _portasPorCelula.Values)
			if (GodotObject.IsInstanceValid(p)) p.QueueFree();
		_portasPorCelula.Clear();
		_veu.Portas.Clear();

		_colisao?.FecharTudo();
		_veu.Mapa?.FecharTudo();

		if (e.Portas.Length == 0 || !Godot.FileAccess.FileExists(e.Portas)) return;

		foreach (PortaDoMapa d in PortasDaZona.Parse(Godot.FileAccess.GetFileAsString(e.Portas)))
		{
			if (!ResourceLoader.Exists(d.Arte)) continue;
			Porta p = Porta.Criar(d);
			_portas.AddChild(p);
			_portasPorCelula[(d.X, d.Y)] = p;
			_veu.Portas[new Vector2I(d.X, d.Y)] = p;
		}
	}

	/// <summary>
	/// O SERVIDOR DISSE que porta abriu ou fechou. Com <paramref name="completo"/>, a lista e o
	/// estado INTEIRO da zona -- tudo que nao estiver nela esta fechado.
	///
	/// ABRIR MEXE NOS DOIS MAPAS, e por dois motivos diferentes: o `.col` porque a porta deixa de
	/// barrar o corpo, e o `.vis` porque ela deixa de barrar a vista. Esquecer o segundo daria uma
	/// porta que se atravessa mas que continua projetando sombra de muro.
	/// </summary>
	private void AoMudarPortas(bool completo, List<(int X, int Y, bool Aberta)> portas)
	{
		if (completo)
		{
			_colisao?.FecharTudo();
			_veu.Mapa?.FecharTudo();
			foreach (Porta p in _portasPorCelula.Values) p.Definir(aberta: false, animar: false);
		}

		foreach ((int x, int y, bool aberta) in portas)
		{
			if (aberta) { _colisao?.Abrir(x, y); _veu.Mapa?.Abrir(x, y); }
			else { _colisao?.Fechar(x, y); _veu.Mapa?.Fechar(x, y); }

			// SEM ANIMACAO no pacote completo: uma porta que ja estava aberta antes de eu chegar
			// nao deve abrir de novo na minha frente.
			if (_portasPorCelula.TryGetValue((x, y), out Porta? porta) && GodotObject.IsInstanceValid(porta))
				porta.Definir(aberta, animar: !completo);
		}
	}

	/// <summary>
	/// UMA CELULA DO CENARIO CAIU -- o corpo arremessado derrubou a parede.
	///
	/// No original o turf destruido e SUBSTITUIDO por `/turf/Ground/Ground8` (chao liso): destruir
	/// nao abre buraco, aplaina. Aqui e o mesmo: a celula some das camadas de DECOR e OBJETOS (que
	/// e onde muro, arvore e cerca moram) e o chao que ja estava embaixo aparece.
	///
	/// A CENA E CACHEADA entre visitas, entao apagar celula nela sobrevive a saida do planeta -- e
	/// aqui isso e o COMPORTAMENTO CERTO, nao um vazamento: o que caiu ficou caido. O servidor
	/// guarda a mesma lista e a reenvia a quem chega (`MandarCenario`), entao quem entra depois ve
	/// o mesmo estrago. O mapa so volta ao normal quando o servidor reinicia, como no BYOND.
	/// </summary>
	/// <summary>
	/// O TILE QUE FICA NO LUGAR do que foi destruido: `/turf/Ground/Ground8`, o mesmo do DM.
	///
	/// Sai do `tiles.json` -- o indice que o conversor de mapa ja publica com a fonte e a
	/// coordenada de cada estado. Procurar pelo NOME em vez de fixar numeros e o que faz isto
	/// sobreviver a proxima reconversao: os ids de fonte sao atribuidos por ordem de descoberta e
	/// mudam quando um mapa muda.
	/// </summary>
	private (int Fonte, Vector2I Coord)? ChaoDestruido()
	{
		if (_chaoDestruido.HasValue) return _chaoDestruido;
		CarregarIndiceDeTiles();
		if (_indice!.Achar("Ground", "Ground8") is not { } t) return null;
		_chaoDestruido = (t.Fonte, new Vector2I(t.X, t.Y));
		return _chaoDestruido;
	}

	/// <summary>
	/// LE O INDICE DE TILES, uma vez por sessao.
	///
	/// ============================ FORA DO QUADRO DA BRIGA ============================
	/// Sao 87 KB de JSON. Ele era lido PREGUICOSAMENTE, na primeira celula de cenario destruida da
	/// sessao -- ou seja, dentro do mesmo quadro em que a poeira, a faisca e o tremor estao sendo
	/// criados, no primeiro golpe pesado da partida. Um engasgo unico, mas exatamente no pior
	/// instante possivel.
	///
	/// Agora sai junto com a zona, onde ja se paga o carregamento de tudo o mais.
	/// ================================================================================
	/// </summary>
	private void CarregarIndiceDeTiles()
	{
		const string indice = "res://Assets/Data/tiles.json";
		_indice ??= Jandirus.Core.World.CatalogoDeTiles.Parse(
			Godot.FileAccess.FileExists(indice) ? Godot.FileAccess.GetFileAsString(indice) : "");
	}

	private Jandirus.Core.World.CatalogoDeTiles? _indice;

	private (int Fonte, Vector2I Coord)? _chaoDestruido;

	private void AoCairCenario(int cx, int cy) => AplicarEstrago(cx, cy, poeira: true);

	/// <summary>
	/// UM ADMIN REFEZ O CENARIO DESTA ZONA.
	///
	/// ============================ POR QUE NAO DA PRA DESFAZER CELULA A CELULA ============================
	/// <see cref="AplicarEstrago"/> APAGA as camadas do tilemap e escreve terra batida por cima. O
	/// que estava ali antes -- qual tile, de qual atlas, em qual das cinco camadas -- nao fica
	/// guardado em lugar nenhum: quem sabe disso e o arquivo da cena, no disco.
	///
	/// Entao restaurar e jogar a cena fora e instanciar de novo. E caro (~50 ms, ver o `[perf]` da
	/// carga de zona) e e um verb de admin: acontece uma vez, quando alguem quis o cenario de volta.
	///
	/// A COLISAO E OUTRA HISTORIA e nao precisa de recarga: a queda nunca escreveu no dado do disco,
	/// so pos a celula numa camada por cima (`ZoneCollision.Abrir`). Um `Fechar` por celula desfaz.
	/// Precisa acontecer ANTES do `CenarioCaido.Clear()` do cliente -- por isso o evento sai com a
	/// lista ainda cheia.
	/// ====================================================================================================
	/// </summary>
	private void AoRefazerCenario(ulong zonaLimpa)
	{
		if (GameClient.Instance is not { } cli) return;

		// NAO E A ZONA EM QUE ESTOU? Entao o unico problema e a cena SUJA guardada no cache: eu vi
		// as paredes cairem ali, viajei, e a instancia foi pro `_zonasVivas` com os tiles apagados.
		// Voltar pra la reusaria essa copia e o buraco reapareceria so pra mim. Jogar fora resolve
		// -- a proxima entrada le do disco.
		if (zonaLimpa != _zonaDoAtual.Hash)
		{
			ZoneKey alvo = _zonasVivas.Keys.FirstOrDefault(z => z.Hash == zonaLimpa);
			if (alvo.Hash != 0 && _zonasVivas.Remove(alvo, out Node2D? velha))
			{
				// A ZONA GUARDADA CONTINUA NA ARVORE, so escondida (ver `GuardarZonaAtual`) -- tirar
				// antes de liberar deixa explicito de onde ela sai.
				if (GodotObject.IsInstanceValid(velha)) { SoltarDaArvore(velha); velha.Free(); }
				_ordemDoCache.Remove(alvo);
			}
			return;
		}

		foreach ((int cx, int cy) in cli.CenarioCaido)
		{
			_colisao?.Fechar(cx, cy);
			_veu.Mapa?.Fechar(cx, cy);
		}

		// A CENA CACHEADA TAMBEM ESTA SUJA. Ela e a MESMA instancia que vai voltar quando alguem
		// reentrar na zona -- guardar sem limpar seria refazer o cenario pra quem esta aqui e
		// devolver o buraco pra quem chegar depois.
		ZoneKey zona = _zonaDoAtual;
		if (_zonaAtual != null)
		{
			RemoveChild(_zonaAtual);
			_zonaAtual.Free();
			_zonaAtual = null;
		}
		if (_zonasVivas.Remove(zona, out Node2D? guardado) && GodotObject.IsInstanceValid(guardado))
		{
			SoltarDaArvore(guardado);
			guardado.Free();
		}
		_ordemDoCache.Remove(zona);

		_zonaDoAtual = default;   // senao o `GuardarZonaAtual` de dentro do CarregarZona reempilha o nada
		CarregarZona(zona, _local?.Position);
		Chat.Sistema("o cenario desta zona foi refeito.");
	}

	/// <summary>
	/// REPOE O ESTRAGO que ja estava feito quando esta zona carregou.
	///
	/// ============================ POR QUE UM METODO E NAO DUAS LINHAS ============================
	/// A primeira versao disto era um `foreach` solto colado depois de `DesenharObras()` -- e caiu no
	/// ramo ERRADO. `CarregarZona` tem tres saidas (espaco, planeta gerado, planeta pre-feito) e a
	/// chamada foi parar na do gerado, que termina em `return` -- o planeta PRE-FEITO, que e onde o
	/// dono estava jogando, nunca reaplicava nada. O desync de mapa que eu disse ter consertado
	/// continuou inteiro.
	///
	/// Virando metodo, cada ramo chama uma linha e da pra ver de longe qual deles esqueceu.
	/// ==============================================================================================
	/// </summary>
	private void ReaplicarEstrago()
	{
		if (GameClient.Instance is not { } cli) return;
		foreach ((int cx, int cy) in cli.CenarioCaido) AplicarEstrago(cx, cy, poeira: false);
	}

	/// <param name="poeira">Falso ao REAPLICAR o que ja tinha caido: o estrago e velho, o efeito nao.</param>
	private void AplicarEstrago(int cx, int cy, bool poeira)
	{
		// ============================ A TERRA REVIRADA VALE SEMPRE ============================
		// Eu tinha posto `if (poeira)` aqui, copiando a regra da POEIRA -- e estava errado. Poeira e
		// um ACONTECIMENTO: ela so faz sentido pra quem viu a parede cair, e por isso nao sai na
		// lista do que ja estava caido quando o jogador chegou. Terra revirada e o contrario: e
		// ESTADO do mapa, tao permanente quanto o buraco que ela cerca.
		//
		// Com o `if`, quem entrasse no planeta depois via os buracos sem marca nenhuma em volta, e
		// dois jogadores no mesmo lugar viam chaos diferentes. O dono notou: "o damaged ground n e
		// sincronizado com jogadores q entraram dps no planeta".
		//
		// SINCRONIZA SEM MANDAR UM BYTE porque o sorteio dos vizinhos e funcao pura da celula (ver
		// `Embaralhar`): quem chega depois recalcula exatamente as mesmas marcas.
		// =====================================================================================
		ChaoDanificadoEmVolta(cx, cy);

		_colisao?.Abrir(cx, cy);
		_veu.Mapa?.Abrir(cx, cy);
		// A CELULA DEIXOU DE CEGAR -- e o leque de visao precisa saber AGORA. Ele so se refaz
		// quando o olho ou a tela se mexem, entao sem isto a sombra da parede derrubada ficava
		// projetada no chao ate o jogador dar um passo. Ver `Visao.Invalidar`.
		_veu.Invalidar();

		var celula = new Vector2I(cx, cy);

		// A COR DO QUE VAI CAIR, LIDA ANTES DE CAIR. Depois do `EraseCell` a celula esta vazia e nao
		// ha mais de que tirar cor -- e poeira de pedra cinza saindo marrom entrega que o efeito e
		// generico. Ver `CorDoEstrago`.
		Color? corDoTile = poeira ? CorDoEstrago(celula) : null;

		// TODAS AS CAMADAS SAEM, E O CHAO E REPOSTO.
		//
		// A primeira versao pulava a camada 0 ("e o chao, apagar abriria buraco") e isso estava
		// errado: um muro que e o UNICO turf da celula mora justamente na camada 0 -- o conversor
		// poe o primeiro turf em `Chao` e so o empilhado em `Decor`. Ou seja, exatamente as paredes
		// que o dono tentou derrubar eram as que nao sumiam.
		//
		// O original nao "apaga" turf nenhum: ele SUBSTITUI por `/turf/Ground/Ground8`
		// (`NewTurfs.dm`). Aqui e o mesmo -- apaga tudo e escreve o Ground8 no chao, entao a celula
		// destruida fica com terra batida em vez de vazio.
		for (int i = 0; i < _veu.Camadas.Length; i++)
			if (IsInstanceValid(_veu.Camadas[i])) _veu.Camadas[i].EraseCell(celula);

		if (_veu.Camadas.Length > 0 && IsInstanceValid(_veu.Camadas[0]) && ChaoDestruido() is { } g)
			_veu.Camadas[0].SetCell(celula, g.Fonte, g.Coord);

		// POEIRA E CASCALHO. O `createDust` do original -- sem ele o tile simplesmente TROCA, e uma
		// parede que some sem nada no lugar nao le como "caiu", le como falha de desenho.
		const int t = ZoneCollision.TileSize;
		var centro = new Vector2(cx * t + t / 2f, cy * t + t / 2f);
		Vector2 rumo = PosicaoLocal is { } eu ? (centro - eu).Normalized() : Vector2.Zero;
		if (!poeira) return;
		PoeiraDeEstrago.Soltar(_atores, centro, rumo, corDoTile);
		// A FAISCA TAMBEM CLAREIA A COR DO TILE, em vez do bege fixo: e a mesma materia saltando.
		CombatFx.Impacto(_atores, centro, 1.2f,
			(corDoTile ?? new Color(0.55f, 0.45f, 0.34f)).Lerp(Colors.White, 0.45f));
	}

	/// <summary>
	/// A COR MEDIA DO QUE ESTA DESENHADO NUMA CELULA -- pra poeira sair da cor do que quebrou.
	///
	/// ============================ POR QUE LER PIXEL, E POR QUE SO UMA VEZ ============================
	/// Uma leitura de imagem e um retorno da GPU pra memoria: caro o bastante pra nao caber num
	/// quadro de briga, e uma rajada derruba de tres a dez celulas de uma vez.
	///
	/// Mas o que se le nao e a CELULA, e o TIPO DE TILE -- e sao poucos tipos num mapa inteiro. Com
	/// cache por `(fonte, atlas)`, o custo e uma vez por tipo por sessao e zero dali em diante; a
	/// imagem da folha tambem fica guardada, entao dois tiles do mesmo atlas custam uma leitura so.
	///
	/// LE DE CIMA PRA BAIXO: a camada mais alta e a que o jogador ve, e portanto a que ele espera
	/// que vire poeira. Um muro sobre grama tem que soltar pedra, nao capim.
	/// ================================================================================================
	/// </summary>
	private Color? CorDoEstrago(Vector2I celula)
	{
		for (int i = _veu.Camadas.Length - 1; i >= 0; i--)
		{
			TileMapLayer camada = _veu.Camadas[i];
			if (!IsInstanceValid(camada) || camada.TileSet is not { } ts) continue;

			int fonte = camada.GetCellSourceId(celula);
			if (fonte < 0) continue;                       // camada vazia aqui
			Vector2I atlas = camada.GetCellAtlasCoords(celula);
			if (CorDoTile(ts, fonte, atlas) is { } c) return c;
		}
		return null;
	}

	private readonly Dictionary<(int Fonte, Vector2I Atlas), Color?> _corPorTile = [];
	private readonly Dictionary<int, Image?> _folhaPorFonte = [];

	private Color? CorDoTile(TileSet ts, int fonte, Vector2I atlas)
	{
		if (_corPorTile.TryGetValue((fonte, atlas), out Color? pronta)) return pronta;

		Color? achada = null;
		try
		{
			if (ts.GetSource(fonte) is TileSetAtlasSource fonteAtlas && fonteAtlas.Texture != null)
			{
				if (!_folhaPorFonte.TryGetValue(fonte, out Image? folha))
					_folhaPorFonte[fonte] = folha = fonteAtlas.Texture.GetImage();

				if (folha != null)
				{
					Rect2I r = fonteAtlas.GetTileTextureRegion(atlas);
					achada = MediaDaRegiao(folha, r);
				}
			}
		}
		catch (Exception e)
		{
			// FALHA ALTO, MAS UMA VEZ SO: textura comprimida sem leitura de CPU e o caso real aqui,
			// e insistir a cada celula derrubada seria trocar um efeito por um travamento.
			GD.PushWarning($"[poeira] nao consegui ler a cor do tile {fonte}/{atlas}: {e.Message}");
		}

		_corPorTile[(fonte, atlas)] = achada;
		return achada;
	}

	/// <summary>
	/// A media dos pixels OPACOS de um recorte. Amostra de 4 em 4 px: um tile de 32 tem 64 amostras,
	/// e a media de cor nao melhora com mais que isso.
	///
	/// PIXEL TRANSPARENTE FICA DE FORA. Muita arte de cenario e uma silhueta com fundo vazio, e
	/// incluir o vazio puxaria toda cor pro preto -- a poeira de qualquer coisa sairia escura.
	/// </summary>
	private static Color? MediaDaRegiao(Image folha, Rect2I r)
	{
		double sr = 0, sg = 0, sb = 0;
		int n = 0;
		int x1 = Math.Min(r.Position.X + r.Size.X, folha.GetWidth());
		int y1 = Math.Min(r.Position.Y + r.Size.Y, folha.GetHeight());

		for (int y = Math.Max(r.Position.Y, 0); y < y1; y += 4)
			for (int x = Math.Max(r.Position.X, 0); x < x1; x += 4)
			{
				Color p = folha.GetPixel(x, y);
				if (p.A < 0.35f) continue;
				sr += p.R; sg += p.G; sb += p.B;
				n++;
			}

		return n == 0 ? null : new Color((float)(sr / n), (float)(sg / n), (float)(sb / n));
	}

	/// <summary>
	/// As TileMapLayer da cena da zona, na ORDEM em que sao desenhadas.
	///
	/// E o que o veu precisa pra repintar a parede por cima da sombra (ver `Visao.Camadas`).
	/// Funciona igual em planeta pre-feito e em gerado, porque os dois chegam aqui como
	/// `_zonaAtual` -- a busca e por tipo, nao por nome de node.
	/// </summary>
	private static TileMapLayer[] CamadasDoCenario(Node? raiz)
	{
		if (raiz == null) return [];
		var achadas = new List<TileMapLayer>();
		Varrer(raiz);
		return [.. achadas];

		void Varrer(Node n)
		{
			foreach (Node f in n.GetChildren())
			{
				if (f is TileMapLayer c) achadas.Add(c);
				Varrer(f);
			}
		}
	}

	/// <summary>Zoom novo vindo do menu de pause, sem recarregar nada.</summary>
	public void AplicarZoom(int z)
	{
		if (_camera != null) _camera.Zoom = new Vector2(z, z);
	}

	private void AoEntrar(int id, ZoneKey zona, Vec2 spawn, string nome)
	{
		CarregarZona(zona, new Vector2(spawn.X, spawn.Y));
		if (_local != null) return;

		var corpo = new LocalPlayer { Name = "LocalPlayer", Position = new Vector2(spawn.X, spawn.Y), Mapa = _colisao };
		// A AURA ENTRA ANTES DO CORPO. Com o `ZIndex = 0` do `SpriteDeAura` quem decide
		// quem fica atras e a ORDEM DE IRMAO -- e o irmao mais novo desenha por cima.
		// Trocar o z sem trocar esta ordem inverteria o defeito: a aura taparia o rosto.
		corpo.AddChild(new Aura { Name = "Aura" });
		corpo.AddChild(new CargaVisual { Name = "Carga" });
		corpo.AddChild(NovoVisual());
		// ============================ E A NEBULOSA ENTRA DEPOIS DO CORPO ============================
		// Ela era o irmao MAIS VELHO (a primeira linha deste bloco) justamente pra desenhar por tras, e
		// o dono corrigiu: "o efeito deveria ficar sobre o corpo e nao atras". Aqui a correcao e UMA
		// LINHA DE LUGAR -- pela mesma regra escrita logo acima, o irmao mais novo desenha por cima.
		//
		// NAO SE RESOLVE COM `ZIndex`: z vence Y-sort, entao a nuvem passaria por cima das arvores e
		// paredes do cenario (o tombo que o `_cabelo` ja levou aqui). Ela fica em z 0 como todo o resto
		// do corpo e continua entrando na profundidade da cena junto com ele.
		//
		// Quem impede que ela TAPE o boneco e o alfa, e nao a ordem: ver `veu_no_corpo` no shader.
		// ==========================================================================================
		corpo.AddChild(new NebulosaDaForma { Name = "Nebulosa" });
		corpo.AddChild(new RaiosDaForma { Name = "Raios" });
		corpo.AddChild(new RastroDeCorrida { Name = "Rastro" });
		// SO O SEU personagem tem campo de visao -- e o que VOCE enxerga, nao algo que os outros
		// veem. Por isso o veu mora no mundo e apenas SEGUE o corpo.
		_veu.Alvo = corpo;

		// ZOOM INTEIRO e SEM suavizacao. Em arte de pixel um zoom quebrado (2,5x) mapeia
		// texel em pixel de tela de forma irregular e a imagem CINTILA andando; e a suavizacao
		// deixa a camera atrasada em relacao ao corpo, o que faz o cenario inteiro parecer
		// tremer em volta do personagem. As duas coisas juntas eram o "tremendo".
		int z = Boot.Config.Zoom;
		var cam = new Camera2D { Enabled = true, PositionSmoothingEnabled = false, Zoom = new Vector2(z, z) };
		corpo.AddChild(cam);
		_camera = cam;

		corpo.AddChild(new HealthBar { Name = "Vida" });
		// O BALAO E DO CORPO LOCAL TAMBEM. Nao e enfeite: quando VOCE fala, a frase aparece onde
		// os outros a veem aparecer -- sem isso nao ha como saber se o balao esta alto demais, se
		// o texto quebrou ou se a fala saiu, a nao ser perguntando pra outra pessoa.
		corpo.AddChild(new BalaoDeFala { Name = "Balao" });
		_atores.AddChild(corpo);
		_local = corpo;

		// OS VERBS FIXOS entram agora. As skills registram os DELAS quando sao aprendidas
		// (`Habilidades`); estes existem pra todo personagem, entao nascem com o corpo.
		VerbosDoJogo.Registrar();
		VestirCorpoInteiro(id, corpo);
		GD.Print($"[world] {nome} pronto em {zona}");
		Chat.Sistema($"bem-vindo, {nome}.");
	}

	/// <remarks>
	/// ============================ `internal` PELO MESMO MOTIVO DO <see cref="AoMudarForma"/> ============================
	/// Este e o unico lugar do jogo que FAZ NASCER um corpo remoto -- e o nascimento e metade da regra
	/// da sincronia de forma: o pacote de estado chega quando nenhum destes bonecos existe, e quem o
	/// transforma em pixel e o `VestirCorpoInteiro` daqui de dentro. Com o metodo privado, a bancada so
	/// poderia chamar `VestirCorpoInteiro` na mao, ou seja provar que a funcao funciona sem provar que
	/// alguem a chama -- que e exatamente o defeito que a fase inteira veio consertar.
	/// ================================================================================================================
	/// </remarks>
	internal void AoReceberSnapshot(List<EntityState> estados)
	{
		ulong agora = Time.GetTicksMsec();
		foreach (EntityState e in estados)
		{
			if (GameClient.Instance != null && e.Id == GameClient.Instance.LocalId)
			{
				// EU ME DESENHO SOZINHO -- mas nem tudo do meu corpo e meu.
				//
				// A posicao eu prevejo e o servidor confere; a ALTURA nao: quem sobe, desce, cobra o
				// Ki e derruba por exaustao e o `TickDoVoo`, no servidor. O corpo local nao tinha por
				// onde saber a propria altitude, porque este laco descartava o proprio estado inteiro
				// -- e sem ela o cliente nao sabe nem se pode atravessar parede.
				//
				// Entao o pacote nao e ignorado: os campos que o servidor DECIDE sao entregues, e o
				// resto (posicao, direcao) continua sendo previsao local.
				//
				// ============================ E QUANDO O CORPO NAO E MEU? ============================
				// Quando a fera toma as redeas (ou qualquer outra possessao), posicao, direcao e
				// "esta andando" PASSAM A SER campos que o servidor decide -- e esta lista era o
				// unico lugar do jogo que nao tinha sido avisado. Por isso o macaco andava animado na
				// tela dos outros e deslizava na do dono: la o corpo e um `RemotePlayer`, que sempre
				// consumiu `Moving`/`Facing`; aqui o corpo escolhia a pose pelo passo do TECLADO, que
				// durante a possessao e sempre zero.
				//
				// Entrega SEMPRE, com ou sem posse: quem decide obedecer e o `LocalPlayer` (o bit
				// `SemRedeas` vem junto), e mandar so as vezes criaria um segundo caminho pra manter
				// em dia. Sem posse, ele guarda e ignora.
				// ====================================================================================
				_local?.ReceberAltura(e.Voando, e.Altitude);
				_local?.ReceberPosse(e.SemRedeas, e.Pos, (Facing)e.Facing, e.Moving, e.Pose);
				// A PUPILA E DO CORPO E NAO DO CONTROLE, entao ela vale pro meu tambem: quem perde as
				// redeas em furia lendaria PRECISA ver o proprio olho apagar, senao a unica pessoa que
				// nao sabe o que aconteceu com aquele corpo e a dona dele.
				AoMudarPosse(e.Id, e.SemRedeas);
				continue;
			}

			if (!_remotos.TryGetValue(e.Id, out RemotePlayer? r))
			{
				r = new RemotePlayer { Name = $"Remoto{e.Id}", Position = new Vector2(e.Pos.X, e.Pos.Y) };
				// A AURA ENTRA ANTES DO CORPO. Com o `ZIndex = 0` do `SpriteDeAura` quem decide
				// quem fica atras e a ORDEM DE IRMAO -- e o irmao mais novo desenha por cima.
				// Trocar o z sem trocar esta ordem inverteria o defeito: a aura taparia o rosto.
				r.AddChild(new Aura { Name = "Aura" });
				r.AddChild(new CargaVisual { Name = "Carga" });
				r.AddChild(NovoVisual());
				// A NEBULOSA DESENHA POR CIMA DO BONECO -- ver o irmao deste bloco no `AoEntrar`, que
				// explica por que e ordem de irmao e nao `ZIndex`. AS DUAS LISTAS TEM QUE ANDAR JUNTAS:
				// a nuvem do vizinho por tras e a sua por cima seria o mesmo efeito com duas leituras.
				r.AddChild(new NebulosaDaForma { Name = "Nebulosa" });
				r.AddChild(new RaiosDaForma { Name = "Raios" });
				r.AddChild(new RastroDeCorrida { Name = "Rastro" });
				r.AddChild(new HealthBar { Name = "Vida" });
				r.AddChild(new BalaoDeFala { Name = "Balao" });
				_atores.AddChild(r);
				_remotos[e.Id] = r;
				VestirCorpoInteiro(e.Id, r);
				GD.Print($"[world] entrou no meu campo de visao: id {e.Id}");
			}

			// `e.Voando` ENTRA AGORA, e ele era o unico campo do `EntityState` que o corpo remoto nao
			// consumia: a ALTURA vinha (a sombra e o deslocamento do desenho dependem dela), o ESTADO
			// de voo nao -- e por isso decolar e pousar eram mudos pra quem estava do lado. Ver
			// `RemotePlayer.OuvirODecolar`.
			r.Receive(e.Pos, (Facing)e.Facing, e.Moving, e.Deitado, e.Pose, e.Correndo, e.Rabo, e.Altitude,
					  e.Voando);

			// ============================ QUEM VOA ALTO SOME DE VISTA ============================
			// "Se a pessoa estiver voando muito alto, as pessoas que estao no chao nem conseguem ver
			// elas -- so se voar alto tambem." Um andar de folga (ver `Voo.Enxerga`): quem paira
			// rasante CONTINUA visivel pra quem esta no chao, e tem que continuar, porque ele pode
			// bater neles -- levar soco de alguem invisivel seria pior que injusto, seria
			// incompreensivel.
			//
			// FILTRAR AQUI E NAO NO SERVIDOR e a mesma escolha do `Oculto` (ver EntityState): o
			// snapshot de uma zona e UM buffer compartilhado, e recortar por destinatario custaria um
			// buffer por jogador. Fica anotado o que e: quem mexer no cliente ve quem voa alto.
			// =====================================================================================
			if (r.GetNodeOrNull<HealthBar>("Vida") is { } barra) barra.Vida = e.Vida / 100f;
			r.GuardarVida(e.Vida);   // os decalques de sangue perguntam por ela

			// ============================ O KI DELE ACIMA DOS 100% -- E ISSO NAO E SO DA CHAMA ============================
			// O bit `Sobrecarregado` ja viajava e ja era consumido AQUI, so que entregue exclusivamente a
			// `CargaVisual`. O contorno do corpo alheio, que e a outra coisa que ele decide, foi escrito
			// pela FORMA por todo esse tempo -- e era o "quem se transforma fica sempre com a outline
			// mesmo sem ativar a aura" que o dono viu.
			//
			// FORA DO `if` DA CARGA de proposito: o contorno nao depende do node `Carga` existir, e
			// pendurar a segunda regra dentro da guarda da primeira e literalmente como esta se perdeu.
			// Ver `MarcarSobrecarga`, que so trabalha quando o bit MUDA.
			// =========================================================================================================
			MarcarSobrecarga(e.Id, e.Sobrecarregado);

			// ============================ E QUEM ESTA DIRIGINDO AQUELE CORPO ============================
			// Irmao exato da linha acima -- outro bit do snapshot que so trabalha quando MUDA, e que
			// decide um pixel do desenho. Aqui ele decide a PUPILA: branca enquanto a furia lendaria
			// dirige, verde quando o dono retoma (`Catalogo.CorDoOlho(d, semRedeas)`).
			//
			// ISTO E INFORMACAO DE JOGO E NAO ENFEITE, e por isso ela nao podia ficar so na tela do
			// dono: quem esta lutando contra um Legendary precisa saber se o que vem em cima dele e uma
			// pessoa decidindo ou um corpo largado -- as duas coisas se enfrentam de jeitos diferentes.
			// =======================================================================================
			AoMudarPosse(e.Id, e.SemRedeas);

			// A AURA DE POWER-UP DO OUTRO. Vem no snapshot justamente pra isto (ver
			// EntityState.Carregando): quem esta lutando precisa ver o adversario juntando poder.
			if (r.GetNodeOrNull<CargaVisual>("Carga") is { } cg)
			{
				cg.Definir(e.Carregando, e.Sobrecarregado);
				// ...E O SOM TAMBEM. Quem esta do lado ouve o zumbido de quem junta energia, como
				// ouve o soco. Isto faltava: o `Som` so era chamado pro corpo LOCAL (pelo canal de
				// `Efeito`, que e pessoal), entao carregar Ki era mudo pra todo mundo menos pra quem
				// carregava -- e no BYOND o `emit_Sound` da carga sai no mundo como qualquer outro.
				//
				// O bit ja viajava: `EntityState.Carregando` esta no snapshot desde que a aura
				// passou a ser visivel pros outros. Faltava CONSUMI-LO aqui. E o `EfeitoNoLugar` ja
				// e posicional, entao o volume cai com a distancia sem mais nada.
				cg.Som(e.Carregando);
			}

			// INVISIVEL: some, mas o no CONTINUA VIVO e recebendo posicao. Apagar o corpo faria
			// ele reaparecer no lugar errado quando voltasse (o cliente teria perdido a
			// interpolacao inteira) -- e reaparecer teleportando entrega quem estava escondido.
			//
			// ============================ UMA ATRIBUICAO SO, E ISTO ERA UM DEFEITO DE PE ============================
			// A regra do voo alto (o bloco comentado la em cima) era escrita numa linha propria e
			// APAGADA aqui, vinte linhas depois, por um `r.Visible = !e.Oculto` que nao olhava altura
			// nenhuma -- a ultima escrita vence sempre. Ou seja: quem voava alto continuava desenhado
			// na tela de quem estava no chao, e o comentario descrevia uma regra que nao existia.
			// Achado pela bancada do balao de fala (`--diagbalao`), que perguntou se o texto some
			// junto com o dono.
			// ==================================================================================================
			r.Visible = !e.Oculto
					 && Jandirus.Core.World.Voo.Enxerga(
							Jandirus.Core.World.Voo.Andar(_local?.Altitude ?? 0f),
							Jandirus.Core.World.Voo.Andar(e.Altitude));
		}
	}

	/// <summary>
	/// O NOME DO LUGAR, pra tela de carregamento. O espaco nao tem entrada no catalogo.
	/// </summary>
	private string NomeDaZona(ZoneKey z)
	{
		if (Jandirus.Core.World.Espaco.EhEspaco(z)) return "Espaço";
		return _catalogo?.Get(z)?.Zona.Replace('_', ' ') is { Length: > 0 } n ? n : z.Name.Replace('_', ' ');
	}

	/// <summary>
	/// APAGA TODOS OS CORPOS REMOTOS. Chamado ao trocar de zona -- ver o `ZoneChanged`.
	///
	/// A APARENCIA CONTINUA GUARDADA de proposito: `_looks` e a ficha visual de cada pessoa, e ela
	/// chega UMA vez por sessao (`PeerLook`). Jogar fora aqui faria quem voltasse pro mesmo planeta
	/// aparecer com o corpo padrao ate o servidor reenviar -- e ele so reenvia em troca de zona,
	/// entao seria um boneco errado a cada ida e volta.
	/// </summary>
	private void EsvaziarRemotos()
	{
		// ============================ A FORMA DELES VAI JUNTO, E AQUI ELA DIVERGE DO `_looks` ============================
		// A aparencia de ficha fica de proposito (o paragrafo acima). O estado de forma NAO pode ficar:
		// eu acabei de sair da zona daquela gente, e o servidor so vai voltar a falar deles quando eu
		// pisar de novo onde eles estao -- e ele so manda pacote de QUEM TEM O QUE DIZER
		// (`GameServer.MandarEstadoDeForma` cala sobre quem esta na base). Guardando, um Super Saiyajin
		// que voltou ao normal enquanto eu estava em Namek renasceria dourado na minha tela quando eu
		// voltasse, e a memoria velha nunca mais seria contestada.
		//
		// SO OS REMOTOS. O meu id nao esta em `_remotos` e o meu boneco nao e destruido aqui: apagar a
		// minha propria linha deixaria o `VestirCorpoInteiro` do proximo corpo local sem o que vestir.
		foreach (int id in _remotos.Keys)
		{
			_formaDaZona.Remove(id);
			_feraDaZona.Remove(id);
			// E O QUE A FORMA E O KI DELES DEIXARAM ESCRITO. As duas tabelas sao DERIVADAS das de
			// cima (a receita do contorno vem da forma; a sobrecarga vem do snapshot, que so chega
			// de quem divide a zona comigo), entao elas tem que morrer na mesma linha -- senao o
			// mesmo id reaparecendo noutro planeta herda o contorno de uma forma que ja acabou.
			_contornoDaForma.Remove(id);
			_sobrecarregados.Remove(id);
		}

		foreach (RemotePlayer r in _remotos.Values) r.QueueFree();
		_remotos.Clear();

		if (GameClient.Instance is { } c && c.AlvoId != 0)
		{
			_marca = null;
			c.SendAlvo(0);
		}
	}

	/// <remarks>
	/// `internal` pelo mesmo motivo do <see cref="AoReceberSnapshot"/>: e o par dele. A memoria de forma
	/// que aquele metodo consome e ESTE que apaga, e "apagar" e uma regra com consequencia visivel (um
	/// Super Saiyajin que voltou ao normal fora do meu campo de visao nao pode renascer dourado na minha
	/// tela). Provar isso exige as duas pontas na mesma bancada.
	/// </remarks>
	internal void AoSair(int id)
	{
		if (_remotos.Remove(id, out RemotePlayer? r)) r.QueueFree();
		_looks.Remove(id);
		_nomes.Remove(id);

		// A FORMA MORRE COM A PESSOA, e aqui ela DIVERGE do `_looks` (que fica de proposito). A
		// aparencia de ficha e permanente e chega uma vez por sessao; a forma e volatil -- quem saiu de
		// vista pode voltar em outra forma, ou na base. Guardar seria vestir o boneco que renasce com um
		// estado que o servidor nao afirma mais. E ele afirma de novo na entrada: `SincronizarFormas`.
		_formaDaZona.Remove(id);
		_feraDaZona.Remove(id);
		// E as duas tabelas que DERIVAM delas -- ver o irmao deste bloco no `EsvaziarRemotos`.
		_contornoDaForma.Remove(id);
		_sobrecarregados.Remove(id);

		// O ALVO SAIU DE CENA. A marca morre junto com o corpo dela (e filha), mas a referencia
		// ficaria pendurada -- e o servidor tambem solta o alvo do lado dele (ver Marcado()).
		if (GameClient.Instance is { } c && c.AlvoId == id)
		{
			_marca = null;
			c.SendAlvo(0);
		}
	}

	// =====================================================================
	// O GOLPE CHEGOU
	// =====================================================================
	/// <summary>
	/// Segundos sem golpe ate a musica de luta sair do ar.
	///
	/// ERA 12, E O DONO SENTIU: "o tempo de combate ta mt curto, mt menor do q era no byond". Ele
	/// esta certo e o numero do original e explicito -- `combat_tag_duration = 900` decisegundos,
	/// com o comentario "(900 = 1 min 30 s)" (`UpdateFightingList.dm:8`). Doze segundos faziam a
	/// musica cair no meio de qualquer briga que tivesse uma pausa pra recuperar folego, que e
	/// justamente o ritmo deste combate.
	///
	/// E O MESMO NUMERO do <c>CombatKnobs.TagDeCombate</c> de proposito: a musica e a tag sao a
	/// MESMA coisa vista de dois lados. Deixar duas constantes livres foi o que permitiu elas
	/// divergirem em 7,5x sem ninguem notar.
	/// </summary>
	/// ( e nao  porque a do Core e um campo ajustavel de balanceamento --
	/// e ler o valor VIVO e justamente o que garante que as duas nao voltem a divergir.)
	private static double SegundosDeLuta => Jandirus.Core.Combat.CombatKnobs.TagDeCombate;
	private double _lutaAte;

	/// <summary>
	/// O relato de um golpe, vindo do servidor. Aqui NAO se calcula nada -- o resultado ja
	/// veio decidido. O que este metodo faz e traduzir o desfecho no que o jogador sente:
	/// piscada, som e a musica de combate entrando.
	///
	/// O DANO so vem quando eu sou um dos dois envolvidos. De espectador o evento chega sem
	/// numero -- da pra ver a briga, nao pra ler a ficha alheia.
	/// </summary>
	private void AoGolpe(Protocol.HitEvent h)
	{
		Node2D? quemBate = Corpo(h.Atacante);
		Node2D? quemLeva = Corpo(h.Alvo);

		bool souEu = GameClient.Instance is { } c && (h.Atacante == c.LocalId || h.Alvo == c.LocalId);

		// A IMAGEM REMANESCENTE, agora pra TODOS -- inclusive pra mim.
		//
		// Antes o meu vulto saia na tecla, sem esperar a rede, "pro controle nao ter atraso". So que
		// o cliente nao sabe se a investida aconteceu (alvo, Ki, parede) e ficava deixando miragem
		// parado. Quem sabe e o servidor, e ele responde no relato -- pro meu corpo o vulto sai na
		// posicao GUARDADA de antes do golpe (ver LocalPlayer._deOndeSai), entao o atraso da rede
		// nao muda onde ele aparece.
		// O RASGO DA INVESTIDA sai quando o servidor CONFIRMA que houve arranque.
		//
		// Ele saia na tecla, e o dono pegou: "ao ficar parado e bater segurando o shift ele faz o
		// som de corrida". Segurar SHIFT e apertar espaco parado nao investe em nada -- nao ha
		// alvo, o `Aproximar` desiste na primeira linha -- mas o cliente ja tinha tocado o som.
		// Quem sabe se houve deslocamento e quem o executa.
		if (h.Investiu && quemBate != null)
		{
			AudioDirector.EfeitoNoLugar(quemBate, Trilha.Dash, 0.7f);
			_investiuAgora = true;   // o `AoPiscar` deste mesmo gesto nao repete som
		}

		// A MIRAGEM DO DASH NAO NASCE MAIS AQUI.
		//
		// Ela nascia, e pro corpo REMOTO nascia no lugar errado: este relato nao carrega posicao, e
		// a chamada caia em `corpo.GlobalPosition` -- o vulto aparecia onde o corpo JA tinha
		// chegado, em vez de onde ele saiu. Agora o servidor anuncia a piscada pelo `S2C.Zanzo`,
		// que leva o ponto de PARTIDA, e o `AoPiscar` desenha os dois casos pelo mesmo caminho.
		//
		// (O corpo LOCAL continua usando a posicao que ELE guardou no instante do gesto -- ver
		// `LocalPlayer.DeixarVulto`. O `AoPiscar` ja trata isso.)

		// ============================ O ATACANTE CHEGA JUNTO COM O SOCO ============================
		// O arranque e um teleporte de servidor de ate 128 px que acontece no MESMO instante em que
		// este relato e emitido -- mas a posicao nova so viria no snapshot do proximo tique, e o
		// corpo remoto ainda interpolaria ate la. Enquanto isso a faisca, que nasce no MEIO dos dois
		// corpos DESENHADOS, estourava no chao vazio.
		//
		// O relato agora traz a coordenada (ver `HitEvent.PosAtacante`), e aqui ela e CRAVADA. So
		// pro corpo remoto: o proprio atacante ja recebeu a correcao no mesmo canal confiavel, e o
		// `LocalPlayer` ja aplica sem suavizar.
		if (h.Alvo != 0 && quemBate is RemotePlayer atacante)
			atacante.Cravar(new Vector2(h.PosAtacante.X, h.PosAtacante.Y));

		var desfecho = (Jandirus.Core.Combat.Desfecho)h.Desfecho;
		// ============================ A FAISCA NASCE NO DESENHO, NAO NO NO ============================
		// Voando, o corpo e desenhado ate 160 px acima do no. Tirar o meio das POSICOES DOS NOS punha
		// o clarao no chao enquanto os dois se socavam no ceu -- o mesmo defeito que o rastro de
		// corrida tinha, e a mesma familia da queixa antiga de "a hitbox pega MUITO longe": efeito
		// desenhado longe de onde a acao se ve.
		//
		// Repare que a POSICAO DE VERDADE (a que decide alcance e dano) continua sendo a do no. O que
		// muda aqui e so onde o EFEITO aparece, e ele tem que aparecer onde os corpos estao na tela.
		// =============================================================================================
		Vector2 pa = Desenhado(quemBate), pv = Desenhado(quemLeva);
		Vector2 meio = quemBate != null && quemLeva != null
			? (pa + pv) * 0.5f
			: quemLeva != null ? pv : quemBate != null ? pa : Vector2.Zero;
		Vector2 rumo = quemBate != null && quemLeva != null
			? (pv - pa).Normalized()
			: Vector2.Zero;

		switch (desfecho)
		{
			case Jandirus.Core.Combat.Desfecho.Acertou:
			case Jandirus.Core.Combat.Desfecho.Critico:
			{
				bool crit = desfecho == Jandirus.Core.Combat.Desfecho.Critico;
				float forca = h.Nivel switch { >= 3 => 1.35f, 2 => 1.0f, _ => 0.8f };
				if (crit) forca = 1.6f;

				// ============================ CENARIO SO FAZ BARULHO ============================
				// Um acerto SEM alvo e um soco em parede, arvore ou bancada (ver
				// `GameServer.SocarCenario`) -- corpo nenhum, entao `Alvo` vem zero. O relato e o
				// mesmo de propósito, pra o som e o nivel do baque nao precisarem de um segundo
				// canal, mas o DESENHO nao pode ser o mesmo.
				//
				// A faisca nasce no meio dos dois corpos, e sem o segundo corpo esse meio vira a
				// posicao de quem bateu: o clarao estourava EM CIMA do personagem, ao lado da
				// parede, como se ele tivesse acertado a si mesmo. Quem desenha o estrago de
				// cenario e a poeira da celula que cai (`PoeiraDeEstrago`), que ja acontece no
				// lugar certo e so quando algo realmente cede.
				// ===============================================================================
				bool emCorpo = quemLeva != null;

				if (emCorpo)
				{
					Piscar(quemLeva, Quente, crit ? Dourado : Laranja, rumo, crit ? 0.22 : 0.15);
					CombatFx.Impacto(_atores, meio, forca, crit ? Dourado : Colors.White);
					if (crit) CombatFx.Onda(_atores, meio, 96, Dourado);
					Tremer(souEu, crit ? 8f : h.Nivel switch { >= 3 => 5f, 2 => 3f, _ => 1.5f });
				}

				// DOIS sons por golpe, como no original: o assobio sai de quem BATE e o baque
				// de quem APANHA. Separar os dois e o que da direcao ao impacto -- um som so,
				// no meio, soa como se ninguem tivesse acertado ninguem.
				Som(quemBate, Trilha.Assobio, 0.6f);
				Som(quemLeva ?? quemBate, Trilha.Acerto(h.Nivel));
				break;
			}

			case Jandirus.Core.Combat.Desfecho.Aparou:
				Piscar(quemLeva, Gelo, Gelo, rumo, 0.12);
				CombatFx.Impacto(_atores, meio, 0.6f, Gelo);
				// O bloqueio NAO e mudo no original: ele passa pelo mesmo `Damage()` e toca o
				// baque de impacto. Aqui sai abafado, com o "tin" da aparada por cima -- o
				// golpe chegou, so nao chegou inteiro, e o som precisa dizer as duas coisas.
				Som(quemLeva ?? quemBate, Trilha.Acerto(h.Nivel), 0.55f);
				Som(quemLeva ?? quemBate, Trilha.Aparou);
				break;

			case Jandirus.Core.Combat.Desfecho.Contra:
				// quem apanha e quem BATEU: o contra-ataque devolve o golpe
				Piscar(quemBate, Quente, Gelo, -rumo, 0.2);
				CombatFx.Impacto(_atores, meio, 1.1f, Gelo);
				Tremer(souEu, 6f);
				// os DOIS sons juntos, como no original -- o "tin" da aparada e o brilho do
				// acerto perfeito sao um som so na cabeca de quem jogou
				Som(quemLeva, Trilha.ContraAtaque);
				Som(quemLeva, Trilha.ContraAtaqueParry, 0.8f);
				Som(quemBate, Trilha.Acerto(2));
				break;

			case Jandirus.Core.Combat.Desfecho.Esquivou:
				// QUEM ESQUIVA COM AFTERIMAGE deixa o vulto SIMPLES no lugar de onde saiu. E o
				// `deflection+=20` do buff Afterimage do original (`Buff Effects.dm:29-46`), que
				// larga uma imagem no turf a cada tique enquanto dura -- aqui, no instante que
				// importa. O servidor ja marca quem tem a skill no relato (`h.ZanzoEsquiva`).
				if (h.ZanzoEsquiva && quemLeva != null)
					Zanzoken.Deixar(_atores, quemLeva, null, EstiloDeVulto.Simples);
				// o unico desfecho que era MUDO na tela: agora deixa o borrao do Zanzoken
				if (quemLeva != null) CombatFx.Esquiva(_atores, quemLeva.Position, -rumo);
				Som(quemBate, Trilha.SocoNoAr(), 0.7f);
				break;

			case Jandirus.Core.Combat.Desfecho.Errou:
				Som(quemBate, Trilha.SocoNoAr(), 0.7f);   // o corte do soco passando em falso
				break;
		}

		if (h.Decepou || h.Rabo) Som(quemLeva, Trilha.Decepou);
		if (h.Morreu || h.Nocauteou)
		{
			Som(quemLeva, Trilha.Queda);
			CombatFx.Onda(_atores, meio, 224, Sangue, 0.35);
			Tremer(souEu, 14f);
		}
		if (h.Morreu || h.Nocauteou || h.Decepou) Piscar(quemLeva, Sangue, Sangue, rumo, 0.4);

		// MUSICA DE LUTA: entra no primeiro golpe que me envolve e sai sozinha depois de um
		// tempo sem troca. A camada Combate cede pra transformacao e volta quando ela acaba.
		//
		// SOCO NO AR NAO E LUTA. Sem esta guarda, treinar sozinho num canto do mapa poria a
		// trilha de batalha no ar -- e o jogador ficaria ouvindo tema de briga socando o vento.
		if (!souEu || h.Alvo == 0) return;
		// uma faixa DIFERENTE a cada briga -- sao 39 na pasta `battle ost`
		if (_lutaAte <= 0) AudioDirector.Instance?.Musica(Trilha.Combate(), AudioDirector.Camada.Combate);
		_lutaAte = SegundosDeLuta;

		if (h.TemDano && h.Membro.Length > 0)
			Hud.Instancia?.Narrar(h, GameClient.Instance!.LocalId);
	}

	/// <summary>
	/// QUANDO A CARGA DA ZONA TERMINOU -- pra medir o QUADRO SEGUINTE. Zero = nada pendente.
	///
	/// ============================ O CUSTO NAO ESTA NA CARGA ============================
	/// `CarregarZona` mede 130 ms de ponta a ponta, com janela e tudo -- e o dono ve tres segundos
	/// de tela congelada. Os dois numeros sao verdadeiros: o que a funcao faz e barato, e o que
	/// vem DEPOIS dela nao.
	///
	/// Um `TileMapLayer` de 500x500 nao monta a estrutura de desenho ao entrar na arvore; ele a
	/// monta quando o renderizador precisa dela, no primeiro quadro. Esse trabalho acontece fora de
	/// qualquer medicao que comece e termine dentro da carga -- e o modo headless, que usa um
	/// renderizador de mentira, nunca o faz.
	///
	/// Este marco fecha o buraco: ele conta do fim da carga ate o proximo `_Process`, que so roda
	/// depois de o quadro ter sido desenhado.
	/// ===================================================================================
	/// </summary>
	private ulong _fimDaCarga;
	private string _zonaMedida = "";

	public override void _Process(double delta)
	{
		if (_fimDaCarga != 0)
		{
			double ms = (Time.GetTicksUsec() - _fimDaCarga) / 1000.0;
			_fimDaCarga = 0;
			if (ms > 5) GD.Print($"[perf] {_zonaMedida}: PRIMEIRO QUADRO {ms:0.0} ms (montagem do tilemap)");
		}

		TickDoTremor(delta);

		EfeitosDaAltura();
		TickDosDecalques(delta);

		if (_lutaAte <= 0) return;
		_lutaAte -= delta;
		if (_lutaAte <= 0) AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Combate);
	}

	/// <summary>A nevoa de altitude. Nasce junto com o mundo e fica quieta enquanto ninguem sobe.</summary>
	private NevoaDeAltitude? _nevoa;

	/// <summary>O ultimo zoom aplicado, pra nao reescrever a camera todo quadro.</summary>
	private int _zoomAgora;

	/// <summary>Abaixo disto o personagem deixa de ser legivel na tela. Ver `EfeitosDaAltura`.</summary>
	private const int PisoDoZoom = 2;

	/// <summary>
	/// O QUE A ALTURA FAZ NA TELA: afasta a camera, abre o veu e turva o mundo.
	///
	/// ============================ O ZOOM E INTEIRO, E TEM QUE SER ============================
	/// A tentacao e interpolar o zoom continuamente com a altura -- fica macio e esta ERRADO. A
	/// camera deste jogo nasceu com `Zoom` inteiro e sem suavizacao por um motivo escrito no proprio
	/// codigo que a cria: em arte de pixel um zoom quebrado (2,5x) mapeia texel em pixel de tela de
	/// forma irregular, e a imagem CINTILA quando o mundo se move. Foi uma queixa real ("tremendo").
	///
	/// Entao a camera afasta em DEGRAUS -- 3x, 2x, 1x --, cada um num terco da subida. O degrau
	/// aparece, e e o preco certo: um salto de zoom uma vez a cada sete tiles incomoda muito menos
	/// que o cenario inteiro cintilando o tempo todo.
	/// ========================================================================================
	/// </summary>
	private void EfeitosDaAltura()
	{
		if (_local == null) return;

		float f = Jandirus.Core.World.Voo.Fracao(_local.Altitude);

		// A NEVOA. Ela mesma suaviza o numero (ver NevoaDeAltitude), entao aqui vai o valor cru.
		if (_nevoa == null && f > 0f)
		{
			_nevoa = new NevoaDeAltitude { Name = "NevoaDeAltitude" };
			AddChild(_nevoa);
		}
		if (_nevoa != null) _nevoa.Fracao = f;

		// O VEU. Ele desenha a sombra que as PAREDES fazem -- e quem esta por cima das paredes nao
		// tem sombra nenhuma pra receber. Some junto com a colisao, no MESMO limiar, porque as duas
		// respondem a mesma pergunta: "este corpo ainda esta no meio do cenario?".
		float opacidade = 1f - Mathf.Clamp(
			_local.Altitude / Jandirus.Core.World.Voo.AlturaQueAtravessa, 0f, 1f);
		if (!Mathf.IsEqualApprox(_veu.Modulate.A, opacidade))
			_veu.Modulate = new Color(1, 1, 1, opacidade);
		_veu.Visible = opacidade > 0.01f;

		// ============================ A CAMERA AFASTA UM DEGRAU, E SO UM ============================
		// A primeira versao descia dois (3x -> 2x -> 1x, ou 2x -> 1x -> 1x). O dono: "voando alto
		// assim fica com um zoom out MUITO grande, nem da pra ver o personagem" -- e na foto ele
		// tinha razao, o boneco virou tres pixels.
		//
		// O PISO E O QUE CONSERTA, e ele nao e arbitrario: abaixo de 2x um sprite de 32 px ocupa
		// 32 px de tela num monitor de 1920, e o personagem deixa de ser legivel. Afastar a camera
		// pra "mostrar mais mundo" nao vale perder de vista o corpo -- pra mostrar mais mundo ja
		// existe o veu, que abre com a altura sem mexer em escala nenhuma.
		//
		// Quem joga em 2x (o padrao) nao ve mudanca de zoom; quem joga em 3x ganha um degrau.
		// ============================================================================================
		if (_camera == null) return;
		int cheio = Math.Max(1, Boot.Config.Zoom);
		int z = f < 0.5f ? cheio : Math.Max(PisoDoZoom, cheio - 1);
		if (z > cheio) z = cheio;
		if (z == _zoomAgora) return;
		_zoomAgora = z;
		_camera.Zoom = new Vector2(z, z);
	}

	/// <summary>Largura do mapa de colisao, em CELULAS. So pras bancadas.</summary>
	public int LarguraDoMapaDeTeste => _colisao?.Width ?? 0;

	/// <summary>A altura do meu corpo agora, em pixels. So pras bancadas.</summary>
	public float AlturaDeTeste => _local?.Altitude ?? 0f;

	/// <summary>
	/// O MEU CORPO ESTA DENTRO DE UMA CELULA BLOQUEADA AGORA? So pras bancadas.
	///
	/// ============================ POR QUE ESTA PERGUNTA, E NAO "cruzou a reta?" ============================
	/// A bancada media "quantas paredes ha na reta entre onde comecei e onde parei". Anda bem pra
	/// quem voa (voando o caminho E a reta), e MENTE pra quem anda: o `MoveRules.Advance` DESLIZA
	/// nas quinas, entao um corpo barrado contorna o obstaculo e termina num ponto cuja reta ate a
	/// origem passa ao LADO da parede. O teste dizia "cruzou 0" nos dois casos e nao separava nada.
	///
	/// "Estou dentro de parede?" nao tem esse problema, e e exatamente a diferenca que se quer
	/// provar: andando isso e IMPOSSIVEL (a colisao recusa o passo), voando e o estado normal de
	/// quem esta por cima de um muro.
	/// ======================================================================================================
	/// </summary>
	public bool DentroDeParedeDeTeste
	{
		get
		{
			if (_colisao == null || PosicaoLocal is not { } p) return false;
			return Jandirus.Core.World.MoveRules.Occupied(_colisao, new Jandirus.Core.World.Vec2(p.X, p.Y));
		}
	}

	/// <summary>Que animacao o MEU corpo esta tocando. So pras bancadas.</summary>
	public string AnimacaoLocalDeTeste => _local?.GetNodeOrNull<CharacterVisual>("Visual")?.AnimacaoDeTeste ?? "";

	/// <summary>Quanto a nevoa esta mostrando AGORA, de 0 a 1. So pras bancadas.</summary>
	public float NevoaDeTeste => _nevoa?.FracaoNaTela ?? 0f;

	/// <summary>O zoom que a altura escolheu. So pras bancadas.</summary>
	public int ZoomDeTeste => _zoomAgora;

	/// <summary>
	/// As camadas de cenario da zona carregada. So pras bancadas -- e a `RoboDeNebulosa` que precisa
	/// dela: pra provar que um efeito de forma some ATRAS de uma arvore, alguem tem que primeiro
	/// ACHAR uma arvore, e o cenario e tile e nao node (nao ha o que procurar com `FindChild`).
	/// </summary>
	public TileMapLayer[] CamadasDoCenarioDeTeste => CamadasDoCenario(_zonaAtual);

	/// <summary>
	/// ANDA NUMA DIRECAO, pelo caminho de sempre. So pras bancadas.
	///
	/// Usa o PILOTO AUTOMATICO (o nav system), e nao um atalho: o passo continua passando pelo
	/// `MoveRules` e sendo conferido pelo servidor. Uma bancada que teleporta o corpo pra beirada
	/// testaria o teleporte, e o que se quer testar e CHEGAR la andando.
	/// </summary>
	public void AndarDeTeste(Vector2 rumo)
	{
		if (_local == null || PosicaoLocal is not { } p) return;
		Vector2 longe = p + rumo.Normalized() * 100_000f;
		_local.Destino = new Jandirus.Core.World.Vec2(longe.X, longe.Y);
	}

	/// <summary>Solta o piloto automatico: o corpo para. So pras bancadas.</summary>
	public void PararDeTeste() { if (_local != null) _local.Destino = null; }

	/// <summary>
	/// O CENTRO DA PAREDE MAIS PROXIMA, em busca por aneis. So pras bancadas.
	///
	/// ============================ POR QUE A BANCADA PRECISA MIRAR ============================
	/// A primeira versao do teste de voo andava pra LESTE e conferia se o caminho tinha cruzado
	/// parede. Na Terra, do ponto de nascimento pro leste, nao ha nenhuma em 290 px -- entao ele
	/// dizia "0 celulas de parede" e reprovava um voo que estava certo.
	///
	/// Um teste que depende de o mapa ter um obstaculo por acaso nao testa nada: se o mapa mudar,
	/// ele passa a mentir nos dois sentidos. Mirar resolve na raiz.
	/// ========================================================================================
	/// </summary>
	public Vector2? ParedeMaisPertoDeTeste(Vector2 de, int raioEmCelulas = 40)
	{
		if (_colisao == null) return null;
		int t = Jandirus.Core.World.ZoneCollision.TileSize;
		int cx = (int)MathF.Floor(de.X / t), cy = (int)MathF.Floor(de.Y / t);

		for (int r = 2; r <= raioEmCelulas; r++)
			for (int dx = -r; dx <= r; dx++)
			for (int dy = -r; dy <= r; dy++)
			{
				if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca do anel
				int x = cx + dx, y = cy + dy;
				if (x < 0 || y < 0 || x >= _colisao.Width || y >= _colisao.Height) continue;
				// A BORDA DO MUNDO NAO SERVE: ela e indestrutivel E da a volta no planeta, entao
				// voar contra ela testaria o `DarAVolta` e nao a colisao.
				if (_colisao.NaBorda(x, y, 4)) continue;
				if (_colisao.BlockedCell(x, y)) return new Vector2((x + 0.5f) * t, (y + 0.5f) * t);
			}
		return null;
	}

	/// <summary>
	/// QUANTAS CELULAS DE PAREDE HA NA RETA entre dois pontos. So pras bancadas.
	///
	/// E o unico jeito de provar que voar atravessa cenario sem depender de conhecer o mapa: se o
	/// corpo saiu de A, chegou em B, e a reta AB cruza parede, entao ele passou por cima. No chao a
	/// mesma reta nao existiria -- o corpo teria parado na primeira.
	/// </summary>
	public int ParedesNaRetaDeTeste(Vector2 a, Vector2 b)
	{
		if (_colisao == null) return 0;
		int t = Jandirus.Core.World.ZoneCollision.TileSize;
		int passos = Math.Max(1, (int)(a.DistanceTo(b) / (t * 0.5f)));
		var vistas = new HashSet<int>();
		int n = 0;
		for (int i = 0; i <= passos; i++)
		{
			Vector2 p = a.Lerp(b, i / (float)passos);
			int cx = (int)MathF.Floor(p.X / t), cy = (int)MathF.Floor(p.Y / t);
			if (!vistas.Add(cy * _colisao.Width + cx)) continue;
			if (_colisao.BlockedCell(cx, cy)) n++;
		}
		return n;
	}

	/// <summary>
	/// ONDE UM CORPO ESTA DESENHADO agora. So pras bancadas.
	///
	/// A pergunta importa porque a posicao DESENHADA e a posicao do SERVIDOR sao coisas
	/// diferentes -- e foi a diferenca entre as duas que produziu a queixa da "hitbox que pega
	/// longe". Sem uma leitura do que esta na tela, nenhum numero prova o conserto.
	/// </summary>
	public Vector2? PosicaoDesenhadaDe(int id) => Corpo(id) is { } c ? Desenhado(c) : null;

	/// <summary>
	/// ONDE UM CORPO ESTA DESENHADO: a posicao do no MAIS o deslocamento de altura do visual.
	///
	/// Os dois so coincidem no chao. Quem voa tem o no na posicao real (que e o que a colisao, o
	/// alcance do soco e o Y-sort usam) e o desenho ate 160 px acima -- ver `LocalPlayer.AplicarAltura`.
	/// Todo EFEITO que quer nascer "onde o corpo esta" tem que perguntar aqui, e nao ao no.
	/// </summary>
	private static Vector2 Desenhado(Node2D? corpo)
		=> corpo == null ? Vector2.Zero
		   : corpo.Position + (corpo.GetNodeOrNull<CharacterVisual>("Visual")?.Position ?? Vector2.Zero);

	private Node2D? Corpo(int id)
	{
		if (id == 0) return null;
		if (GameClient.Instance is { } c && id == c.LocalId) return _local;
		return _remotos.TryGetValue(id, out RemotePlayer? r) ? r : null;
	}

	/// <summary>
	/// O corpo de um id, pela MESMA busca que o jogo usa. So pras bancadas.
	///
	/// Achar o boneco por nome de node (`FindChild("Remoto42")`) daria o mesmo objeto por outro
	/// caminho -- e passaria mesmo se ele nunca tivesse entrado no `_remotos`, que e o mapa de
	/// onde a fala, o alvo e a forma tiram o corpo de alguem.
	/// </summary>
	public Node2D? CorpoDeTeste(int id) => Corpo(id);

	// A PALETA DO IMPACTO, num lugar so. Sao as cores que o jogador aprende a ler sem pensar:
	// quente = acertou, dourado = critico, gelo = defendeu, sangue = alguem caiu.
	private static readonly Color Quente = new(1.0f, 0.95f, 0.90f);
	private static readonly Color Dourado = new(1.0f, 0.85f, 0.35f);
	private static readonly Color Laranja = new(1.0f, 0.70f, 0.30f);
	private static readonly Color Gelo = new(0.55f, 0.80f, 1.00f);
	private static readonly Color Sangue = new(1.0f, 0.35f, 0.30f);

	private static void Piscar(Node2D? quem, Color cor, Color contorno, Vector2 rumo, double segundos = 0.15)
		=> quem?.GetNodeOrNull<CharacterVisual>("Visual")?.Impacto(cor, contorno, rumo, segundos);

	/// <summary>
	/// O SOLAVANCO DA CAMERA. So pra quem esta NA briga -- tremer a tela de quem so passava
	/// por perto e o caminho mais rapido pra enjoar o jogador.
	///
	/// Mexe no `Offset`, nao na `Position`: a camera e FILHA do personagem, e escrever na
	/// posicao brigaria com o movimento a cada quadro.
	/// </summary>
	private void Tremer(bool souEu, float forca)
	{
		if (souEu) LevantarTremor(forca, QuedaDoImpacto, CadenciaDoImpacto);
	}

	/// <summary>
	/// Sacode a camera de fora (a cinematica de transformacao usa). O tremor vale so pra
	/// QUEM esta olhando -- e camera, nao mundo.
	/// </summary>
	/// <param name="forca">Amplitude em pixels de camera.</param>
	/// <param name="peso">Fracao da amplitude, pra quem quer o mesmo tremor mais fraco.</param>
	/// <param name="queda">
	/// Quanta forca some por segundo -- e portanto quanto o solavanco DURA (`forca / queda`). O
	/// padrao e o do combate; a cinematica passa o
	/// <see cref="Jandirus.Core.Forms.Cinematicas.QuedaDoTremor"/>, que e cinco vezes mais lento.
	/// </param>
	/// <param name="cadencia">
	/// Segundos que a camera segura cada rumo -- e portanto a VELOCIDADE do tremor. O padrao e o do
	/// combate; a cinematica passa o <see cref="Jandirus.Core.Forms.Cinematicas.CadenciaDoTremor"/>.
	/// </param>
	public void Sacudir(float forca, float peso = 1f,
						float queda = QuedaDoImpacto, float cadencia = CadenciaDoImpacto)
		=> LevantarTremor(forca * Mathf.Clamp(peso, 0f, 1f), queda, cadencia);

	/// <summary>
	/// COMO O TREMOR DE IMPACTO CAI E TROCA DE RUMO -- soco, critico, embate de clash.
	///
	/// A queda de 40/s e a de sempre: um critico de forca 8 sacode por 0,2 s e um soco leve de 1,5
	/// por 0,04 s. E o solavanco SECO que o combate quer, e ele nao entrou na queixa do dono.
	///
	/// A cadencia de 1/60 s reproduz o que o codigo antigo fazia por acidente -- um rumo novo a cada
	/// `_Process`. A diferenca e que agora e um NUMERO e nao a taxa de quadros: quem jogava a 144 fps
	/// tinha um tremor duas vezes e meia mais rapido que quem jogava a 60, na mesma cena.
	/// </summary>
	private const float QuedaDoImpacto = 40f, CadenciaDoImpacto = 1f / 60f;

	/// <summary>
	/// LEVANTA O TREMOR -- e quem levanta manda no JEITO dele.
	///
	/// A amplitude e uma so pra tela inteira e vence a maior (era assim antes e continua). O que
	/// mudou e que ela nao viaja mais sozinha: a queda e a cadencia vem junto, porque um soco e uma
	/// transformacao nao tremem igual. Sem isso, deixar a cinematica mais lenta e mais longa deixaria
	/// TODO impacto do jogo mais lento e mais longo junto.
	/// </summary>
	private void LevantarTremor(float forca, float queda, float cadencia)
	{
		if (forca <= _tremor) return;

		// TREMIDA NOVA COMECA NO CENTRO. Saltar direto pro primeiro rumo sorteado seria um tranco de
		// um quadro -- exatamente o que a cadencia existe pra tirar. Nao vale pra tremida que apenas
		// se RENOVA (o rumor continuo da cinematica reacende a cada quadro): ali o rumo tem que
		// continuar de onde estava, senao a camera nunca sairia do centro.
		if (_tremor <= 0)
		{
			_tremorRelogio = 0;
			_tremorDe = Vector2.Zero;
			_tremorPara = RumoDoTremor();
		}

		_tremor = forca;
		_tremorQueda = queda;
		// CADENCIA ZERO TRAVARIA O `while` DO TICK. Nenhum caminho de hoje chega a zero, mas o preco
		// do piso e uma comparacao e o preco do descuido e o cliente congelado.
		_tremorCadencia = Mathf.Max(cadencia, 0.001f);
	}

	/// <summary>
	/// O TREMOR, QUADRO A QUADRO: a amplitude cai e o rumo passeia.
	///
	/// ============================ A FREQUENCIA ERA A TAXA DE QUADROS ============================
	/// Isto sorteava um `Offset` novo a CADA `_Process`. Ou seja: a velocidade do tremor nao era um
	/// numero deste jogo, era o fps da maquina -- 60 pulos por segundo num PC, 144 em outro, e menos
	/// ainda quando o quadro engasga. E dai que vinha o "rapido demais" das cinematicas: 60 pulos por
	/// segundo nao leem como chao tremendo, leem como zumbido.
	///
	/// Agora o rumo tem RELOGIO PROPRIO (<see cref="_tremorCadencia"/>) e a camera CAMINHA de um pro
	/// outro. Duas consequencias: da pra pedir um tremor lento sem baixar o fps, e o tremor passou a
	/// ser o mesmo em qualquer maquina.
	/// ========================================================================================
	///
	/// A CAMERA SO E CONSULTADA NA HORA DE ESCREVER. Antes a guarda era `_tremor > 0 && _camera !=
	/// null`, o que deixava a amplitude PRESA la em cima enquanto nao houvesse camera -- e ela
	/// descarregaria de uma vez no instante em que uma aparecesse.
	/// </summary>
	private void TickDoTremor(double delta)
	{
		if (_tremor <= 0) return;

		_tremor = Mathf.MoveToward(_tremor, 0, (float)delta * _tremorQueda);
		if (_tremor <= 0)
		{
			if (_camera != null) _camera.Offset = Vector2.Zero;
			return;
		}

		_tremorRelogio += (float)delta;
		while (_tremorRelogio >= _tremorCadencia)
		{
			_tremorRelogio -= _tremorCadencia;
			_tremorDe = _tremorPara;
			_tremorPara = RumoDoTremor();
		}

		// A CAMERA CAMINHA ENTRE OS RUMOS, nao salta. Sem o `Lerp` a cadencia lenta viraria um
		// estrobo de doze posicoes por segundo -- mais devagar que antes, sim, mas tambem mais duro.
		if (_camera != null)
			_camera.Offset = _tremorDe.Lerp(_tremorPara, _tremorRelogio / _tremorCadencia) * _tremor;
	}

	/// <summary>Um ponto qualquer do quadrado [-1,1]. O contador e como a bancada mede a cadencia.</summary>
	private static Vector2 RumoDoTremor()
	{
		RumosDoTremorDeTeste++;
		return new Vector2(Sorte.Randf() * 2 - 1, Sorte.Randf() * 2 - 1);
	}

	/// <summary>
	/// Quantos rumos o tremor ja sorteou desde que o cliente subiu. So a bancada le: contar rumos e
	/// o unico jeito de medir a CADENCIA de fora sem depender do fps em que a medicao rodou.
	/// </summary>
	public static int RumosDoTremorDeTeste;

	/// <summary>Roda um quadro do tremor com o `delta` que a bancada quiser. Ver <see cref="TickDoTremor"/>.</summary>
	public void TickDoTremorDeTeste(double delta) => TickDoTremor(delta);

	/// <summary>A amplitude do tremor agora. So a bancada le.</summary>
	public float TremorDeTeste => _tremor;

	private float _tremor;
	private float _tremorQueda = QuedaDoImpacto, _tremorCadencia = CadenciaDoImpacto, _tremorRelogio;
	private Vector2 _tremorDe, _tremorPara;
	private static readonly RandomNumberGenerator Sorte = new();

	private static void Som(Node2D? onde, string caminho, float volume = 1f)
	{
		if (onde != null && caminho.Length > 0) AudioDirector.EfeitoNoLugar(onde, caminho, volume);
	}

	private static CharacterVisual NovoVisual() => new() { Name = "Visual" };

	/// <summary>
	/// A aparencia de alguem. Pode chegar ANTES do boneco existir (o snapshot e quem cria
	/// o RemotePlayer, e ele vem por outro canal) -- entao guarda sempre, e veste se ja der.
	///
	/// ============================ E "VESTIR" AQUI E O CORPO INTEIRO, NAO SO A FICHA ============================
	/// Este metodo chamava `CharacterVisual.Vestir` direto, e era o GEMEO POBRE do nascimento: a ficha
	/// entrava e a FORMA nao. `Vestir` remonta as camadas do zero -- ele reescreve `_cabeloBase`
	/// (`CharacterVisual.cs:678`), nao repoe as coladas e nao repoe o contorno --, entao um `PeerLook`
	/// que chegasse depois do boneco DESPIA quem estava transformado: pelagem certa (essa o `Vestir`
	/// preserva, linha 716), cabelo base de volta, sem contorno, sem raios, sem nebulosa.
	///
	/// E chegar depois e metade dos casos: o `PeerLook` vem no canal CONFIAVEL e o boneco nasce do
	/// SNAPSHOT, canal NAO-confiavel -- nao ha ordem garantida entre canais diferentes. Ver
	/// <see cref="VestirCorpoInteiro"/>, que e o funil unico dos dois caminhos.
	/// ======================================================================================================
	/// </summary>
	/// <remarks>
	/// `internal` PELO MESMO MOTIVO DO <see cref="AoFalar"/>: este e o UNICO lugar do jogo que
	/// escreve o mapa id -> nome (`_nomes`), e a busca reversa do balao de fala vive dele. Com o
	/// metodo privado, a bancada so alcanca o atalho do proprio jogador (`LocalName`) -- provaria o
	/// balao de quem esta jogando e deixaria sem teste o dos OUTROS, que e onde o balao serve pra
	/// alguma coisa.
	/// </remarks>
	internal void AoReceberAparencia(int id, string nome, string raca, string genero,
									Jandirus.Core.Appearance.Appearance ap)
	{
		_looks[id] = (raca, genero, ap);
		_nomes[id] = nome;
		if (_visual == null) return;
		// PELO MESMO `Corpo(id)` que a forma, a fala e o alvo usam: eram dois ramos escritos a mao
		// aqui (um pro `_local`, outro pro `_remotos`), e essa era a terceira copia daquela busca.
		if (Corpo(id) is { } corpo) VestirCorpoInteiro(id, corpo);
	}

	// =====================================================================
	// FALA SOBRE A CABECA
	// =====================================================================
	/// <summary>
	/// ALGUEM FALOU E EU OUVI: acha o corpo e poe a frase sobre a cabeca dele.
	///
	/// ============================ O PACOTE TRAZ NOME, NAO ID ============================
	/// `S2C.Chat` carrega `[canal][autor][texto]` (ver `GameClient`), e mudar isso mexeria no
	/// protocolo do chat inteiro por causa de um balao. Nao precisa: o cliente JA TEM o mapa
	/// id -> nome (`_nomes`, escrito por `PeerLook`), entao a busca reversa resolve com o que
	/// existe. O preco e o caso de dois personagens homonimos na mesma zona -- o balao iria pro
	/// primeiro. Nomes de personagem sao unicos por conta e a colisao e teorica; se um dia deixar
	/// de ser, o conserto e por o id no pacote e apagar esta busca.
	/// ====================================================================================
	///
	/// O portao de canal e do <see cref="BalaoDeFala.EhDeCorpo"/> -- OOC e LOOC sao do jogador e
	/// nao do personagem, Sistema nem autor tem, e o sussurro de longe chega sem texto.
	/// </summary>
	/// <remarks>
	/// `internal` PELO MESMO MOTIVO DO <see cref="AoMudarForma"/>: quem chama isto em jogo e o
	/// evento `GameClient.Falou`, e evento nao se dispara de fora da classe que o declara. Com o
	/// metodo privado a bancada so poderia chamar `BalaoDeFala.Dizer` na mao -- ou seja, provar que
	/// o balao desenha sem provar que a FALA chega nele, que e justamente onde moram as duas regras
	/// que importam (o portao de canal e a busca do corpo pelo nome).
	/// </remarks>
	internal void AoFalar(Protocol.Fala canal, string autor, string texto)
	{
		if (autor.Length == 0 || !BalaoDeFala.EhDeCorpo(canal, texto)) return;
		if (Corpo(IdPeloNome(autor)) is not { } corpo) return;
		corpo.GetNodeOrNull<BalaoDeFala>("Balao")?.Dizer(canal, texto);
	}

	/// <summary>
	/// O id de quem se chama assim, ou 0. O proprio jogador vem primeiro porque o `LocalName` e a
	/// unica leitura que nao depende de o `PeerLook` ja ter chegado.
	/// </summary>
	private int IdPeloNome(string nome)
	{
		if (GameClient.Instance is { } cli && cli.LocalName == nome) return cli.LocalId;
		foreach ((int id, string n) in _nomes)
			if (n == nome) return id;
		return 0;
	}

	/// <summary>
	/// Como se chama este id, ou "". O `_nomes` chega pelo `PeerLook`; o proprio jogador tem o
	/// atalho porque o nome dele existe desde o login, antes de qualquer pacote de aparencia.
	/// </summary>
	private string NomeDe(int id)
	{
		if (GameClient.Instance is { } cli && id == cli.LocalId) return cli.LocalName;
		return _nomes.TryGetValue(id, out string? n) ? n : "";
	}

	// =====================================================================
	// TRANSFORMACAO
	// =====================================================================
	/// <summary>
	/// Alguem mudou de forma. Vale pra QUALQUER um da zona, nao so pra mim: ver o adversario
	/// virar Super Saiyajin na sua frente e metade da graca.
	/// </summary>
	/// <remarks>
	/// ============================ `internal` E NAO `private`, E O MOTIVO E A BANCADA ============================
	/// Quem chama isto em jogo e o evento `GameClient.FormaMudou`, e evento nao se dispara de fora da
	/// classe que o declara. Com o metodo privado a unica bancada possivel era remontar as duas linhas
	/// que ele faz (`NoDegrau` + `Transformacao.Rodar`) dentro do robo -- ou seja, testar uma COPIA da
	/// regra. Este projeto ja deu verde tres vezes assim, com o jogo quebrado.
	///
	/// Aberto pra o `--diagforma` chamar O METODO, os tres degraus de maestria sao medidos onde eles
	/// acontecem de verdade: se alguem trocar o `NoDegrau` daqui por um `if (primeira)`, a bancada
	/// reprova. O que continua de fora e so o decodificador do byte (mora no `switch` do
	/// `GameClient`) -- anotado la na bancada como buraco conhecido.
	/// ========================================================================================================
	/// </remarks>
	internal void AoMudarForma(int id, int de, int para, Jandirus.Core.Forms.DegrauDeCena degrau,
							   bool dominada = false)
	{
		// ANTES DO `return` DO CORPO NULO, e a ordem e a regra inteira: o pacote de sincronia da
		// entrada na zona chega justamente quando os bonecos ainda nao nasceram. Registrando so depois
		// de achar o corpo, a memoria guardaria exatamente os casos que ja funcionavam e perderia os
		// que ela existe pra cobrir. Ver `_formaDaZona`.
		_formaDaZona[id] = para;

		// O DOMINIO JUNTO E NO MESMO GESTO -- ver `_dominouDaZona`. Guardar num lugar e ler no outro
		// e como o `_looks` e as feridas ja envelheceram bem; separar os dois registros seria criar a
		// chance de um corpo com forma guardada e dominio esquecido.
		if (dominada) _dominouDaZona.Add(id); else _dominouDaZona.Remove(id);

		Node2D? corpo = Corpo(id);
		if (corpo == null) return;

		Jandirus.Core.Forms.FormaDef? def = Jandirus.Core.Forms.Catalogo.PorRede((ushort)para);
		if (def is { Id: "base" }) def = null;   // a base existe como entrada, mas nao acende nada

		PrepararAuraDaForma(corpo, def);

		// ============================ O CONTORNO LOCAL SE GUARDA EM TODO CAMINHO ============================
		// Isto morava LA EMBAIXO, depois do `return` da cinematica -- e por isso ele nunca rodava quando
		// havia cena. O estrago que o dono viu: "se eu voltar pra base e virar ssj dnv o outline n volta".
		//
		// A conta era esta: voltar pra base passa pelo caminho SEM cena e zera `_forcaDoBrilho`; virar
		// Super Saiyajin de novo (maestria < 50% -> cena CURTA) saia por aquele `return` e nunca mais
		// reescrevia a forca. O `Assumir` da cena pintava o contorno direto no sprite, e o PRIMEIRO
		// pacote de carga que chegasse chamava o aplicador do contorno e o apagava com o valor velho --
		// zero. Dois donos escrevendo o mesmo pixel, e o que sobrevivia era o desatualizado.
		//
		// GUARDAR NAO E ACENDER, e e isso que torna seguro fazer isto ANTES da cena: quem decide se o
		// contorno aparece e a sobrecarga de Ki (`_sobrecarregados`), nao a forma. Um jogador que entra
		// na cena ja passando dos 100% ve a linha durante ela -- e isso e verdade, nao vazamento: desde
		// que o contorno passou a significar "passei do meu limite", ele nao tem por que esperar o fim
		// da transformacao pra dizer isso.
		//
		// E ISTO VALE PRA QUALQUER ID DA ZONA, e nao so pro meu: era `GuardarBrilhoDaForma`, que saia
		// calado quando o corpo nao era o local. Ver `GuardarContornoDaForma`.
		// ================================================================================================
		GuardarContornoDaForma(id, def);

		// ============================ QUANDO HA CINEMATICA, ELA MANDA ============================
		// Enquanto houver cena o cabelo, a aura e os raios NAO sao aplicados aqui: quem os aplica e o
		// beat `Assumir` da cena, la no fim. Aplicar agora deixaria o personagem ja transformado
		// assistindo a propria transformacao -- e o cabelo do SSJ1, que no DM PISCA entre o normal e o
		// dourado durante a cena, nao teria pra onde piscar.
		//
		// A aura de LUZ tambem espera: ela e o clarao do fim.
		//
		// ============================ ISTO ERA `if (primeira)` -- SAO TRES DEGRAUS AGORA ============================
		// O dono: *"ate vc ter 50% de maestria de uma forma, ela ainda vai ter um tempo pra se
		// transformar, menor q a primeira cinematica mas ainda assim vai ser lenda"*. Ou seja o caminho
		// de baixo (transformacao direta, sem cena) deixou de ser "tudo o que nao e estreia" e passou a
		// ser so o terceiro degrau -- quem domina a forma.
		//
		// O DEGRAU VEM DO SERVIDOR e a CENA sai do Core: `NoDegrau` e a mesma funcao que o servidor
		// consultaria se precisasse do prazo. Escolher a cena aqui, no cliente, a partir de um numero
		// que o servidor mandou, e o que garante que os dois nunca discordem sobre quanto tempo o corpo
		// fica preso -- porque so existe uma conta, e ela mora no Core.
		// ====================================================================================================
		if (def != null
			&& Jandirus.Core.Forms.Cinematicas.NoDegrau(def, degrau) is { } cena)
		{
			bool souEu = id == GameClient.Instance?.LocalId;
			// O NOME VAI JUNTO: e com ele que quem ASSISTE le "Zx: AINDA MAIS ALEM!" no chat. O
			// balao sobre a cabeca nao precisa dele (o corpo ja esta em maos), a linha do chat sim.
			Transformacao.Rodar(_atores, corpo, def, cena, souEu, NomeDe(id));

			// O `** Nome **` E DA ESTREIA. Ele e o carimbo de acontecimento, irmao da musica que toca
			// uma vez na vida do personagem (`ssj1_music_played`); repeti-lo a cada transformacao o
			// esvaziaria. Quem se transforma de novo ja recebe do servidor o `Nome (x2,5)` de sempre.
			// O NOME PELO FUNIL (`Catalogo.NomeDe`) e nao `def.Nome`: o `dominada` que chegou no pacote e
			// o mesmo bit que decide o cabelo `SSjFP` tres linhas abaixo, e um carimbo de chat que
			// dissesse "Super Saiyajin" sobre um corpo com cabelo de Full Power seria a tela e o texto
			// discordando. (Na estreia ele e sempre falso -- maestria zero --, e e justamente por isso
			// que ele tem que passar por aqui: um dia a condicao muda e ninguem vai lembrar desta linha.)
			if (souEu && degrau == Jandirus.Core.Forms.DegrauDeCena.Estreia)
				Chat.Sistema($"** {Jandirus.Core.Forms.Catalogo.NomeDe(def, dominada)} **");
			return;
		}

		VestirAFormaSemCena(id, corpo, def);

		if (def == null)
		{
			if (id == GameClient.Instance?.LocalId) Chat.Sistema("voce volta ao normal.");
			return;
		}

		// ============================ TRANSFORMAR E DIRETO SO PRA QUEM DOMINA A FORMA ============================
		// Este caminho era "tudo o que nao e estreia". Hoje ele e o TERCEIRO degrau e so ele:
		// `DegrauDeCena.Nenhuma`, que o servidor manda quando a maestria da forma alvo passou dos 50%
		// (`Cinematicas.MaestriaQueDispensaCena`). Entre a estreia e o dominio ha a cena encurtada, que
		// sai pelo `return` la de cima.
		//
		// O dono, sobre este degrau: *"apartir de 50% de maestria a transformaçao vira instantanea"*.
		// E o que "instantanea" quer dizer e literalmente estas quatro linhas -- o corpo NAO e preso, os
		// raios e o contorno acendem no mesmo quadro em que o pacote chega.
		// ====================================================================================================
		// Ele ja tinha descrito o defeito que este caminho existe pra evitar: *"vc colocou uma animacao
		// ao se transformar que ele parece que diminui de tamanho, mas era pra ser algo direto que
		// simplesmente ativa os raios e o contorno"*. A onda de choque que causava aquilo saiu do jogo
		// inteiro depois disso (ver `Efeito.Onda`, aposentado no Core).
		//
		// O som fica: ele marca o instante sem tomar a tela.
		//
		// ============================ MAS SO QUANDO HOUVE INSTANTE ============================
		// `de == para` e o pacote de ESTADO que o servidor manda pra quem acaba de entrar na zona
		// (`GameServer.MandarEstadoDeForma`): "ele ESTA em SSJ3", e nao "ele acabou de virar SSJ3".
		// Sem esta condicao, chegar num planeta com tres transformados dispararia tres estalos de
		// transformacoes que aconteceram antes de eu por o pe ali -- a mesma mentira da cinematica,
		// so que no ouvido. E ela nao tira o som de ninguem: "de X pra X" nao e uma mudanca que exista.
		// ====================================================================================
		if (de != para) AudioDirector.EfeitoNoLugar(corpo, Trilha.Dash, 0.9f);
	}

	/// <summary>
	/// A FORMA POSTA NO CORPO DIRETO, sem cena nenhuma: cabelo, corpo proprio, contorno e raios no
	/// mesmo quadro. <c>def == null</c> desfaz tudo e devolve o lutador comum.
	///
	/// ============================ EXTRAIDO PORQUE SAO DOIS DONOS, E ERAM DUAS VERDADES ============================
	/// Estas linhas moravam soltas no <see cref="AoMudarForma"/>, e o <see cref="AoVirarOozaru"/> tinha
	/// uma copia parcial delas pra desfazer a fera -- copia com a ORDEM TROCADA, `AcenderFormaNoCorpo`
	/// antes do `CorpoDaForma`. Era inofensivo so porque aquele caminho sempre passava `null` (sem
	/// camada nova pra nascer sem contorno); no dia em que o macaco precisasse aparecer sem cena --
	/// que e exatamente hoje, pra quem entra numa zona onde a fera ja existe -- a copia estaria errada.
	/// ========================================================================================================
	/// </summary>
	private void VestirAFormaSemCena(int id, Node2D corpo, Jandirus.Core.Forms.FormaDef? def)
	{
		// --- o cabelo: o Saiyajin doura ---
		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is { } vis)
		{
			// O CORPO PROPRIO DA FORMA (a pelagem do SSJ4, o macaco inteiro). Voltar pra base TEM que
			// tira-lo -- senao o jogador volta ao normal peludo e vermelho, pra sempre.
			vis.CorpoDaForma(def?.Corpo ?? Jandirus.Core.Forms.CorpoDeForma.Nenhum);

			// ANTES DO CABELO, e nao e ordem decorativa: e este fato que decide se o Super Saiyajin
			// pede a folha `SSj` ou a `SSjFP` (o Grade 4). Marcado AQUI e nao no chamador porque este
			// e o funil por onde passam os tres caminhos que vestem sem cena -- a troca de forma, o
			// nascimento do boneco e a saida do Oozaru. Ver `CharacterVisual.MarcarFormaDominada`.
			vis.MarcarFormaDominada(_dominouDaZona.Contains(id));

			// E QUEM DIRIGE O CORPO, NO MESMO GESTO E PELO MESMO MOTIVO. Este e o funil por onde nasce
			// o boneco de quem eu ainda nao tinha visto -- entrar numa zona onde alguem ja esta em furia
			// lendaria tem que me mostrar o olho apagado dele no primeiro quadro, e nao so na proxima
			// vez que a posse virar. Ver `CharacterVisual.MarcarSemRedeas`.
			vis.MarcarSemRedeas(_semRedeasDaZona.Contains(id));

			// UMA CHAMADA PRA TRES DECISOES -- qual sprite, se pinta, e o que o rabo faz. Eram duas
			// linhas aqui e duas iguais no `Transformacao.Vestir`, com um comentario em cada uma
			// explicando a ordem entre elas; hoje a ordem mora dentro do metodo e a regra mora no Core
			// (`Catalogo.ModoDoCabelo`). Ver `CharacterVisual.VestirCabeloDaForma`.
			//
			// DEPOIS DO `CorpoDaForma`, e isso continua sendo regra: quem tem corpo proprio (SSJ4,
			// Oozaru) ja traz o proprio rabo, e o `PintarRabo` la dentro pergunta por essa camada pra
			// nao deixar tinta armada num node escondido.
			vis.VestirCabeloDaForma(def);

			// O OVERLAY COLADO NO CORPO (a fagulha do Legendary, o brilho do ki divino). MESMA REGRA
			// DE ORDEM dos dois de cima: ele cria camada nova, e camada nova nasce com todo uniform no
			// padrao -- o contorno la embaixo tem que vir DEPOIS. Ver `CharacterVisual.ColadasDaForma`.
			vis.ColadasDaForma(def);
		}

		// O QUE A CENA TERIA ACENDIDO NO FIM DELA, e que aqui acende agora: os RAIOS e a nebulosa.
		AcenderFormaNoCorpo(corpo, def);

		// E O CONTORNO NO MESMO QUADRO, e nao no seguinte. `CorpoDaForma` CRIA uma camada nova (a
		// pelagem do SSJ4) com material proprio, e todo uniform dela nasce no padrao -- contorno zero.
		// O `_Process` do `CharacterVisual` a acertaria no quadro seguinte (ver `AplicarContorno`), mas
		// um quadro com metade do boneco contornada e visivel numa transformacao instantanea, que e
		// justamente quando este caminho roda.
		//
		// QUEM DECIDE SE ACENDE CONTINUA SENDO O KI: esta chamada so RELE o par (forma, sobrecarga).
		AplicarContorno(id);
	}

	/// <summary>
	/// A COR QUE A AURA VAI TER -- a luz em volta do corpo e a folha que a carga desenha.
	/// <c>def == null</c> apaga.
	///
	/// PREPARA, NAO ACENDE (ver `Aura.Preparar`): a aura so nasce da carga de Ki. Transformar
	/// escolhe a cor; quem acende e a tecla C.
	///
	/// Virou metodo porque o Oozaru precisa exatamente disto e de mais nada -- ele nao tem cabelo
	/// nem corpo proprio pra trocar (ver <see cref="AoVirarOozaru"/>). Copiar o bloco criaria duas
	/// verdades sobre a folha da aura, e a segunda envelheceria calada.
	/// </summary>
	private static void PrepararAuraDaForma(Node2D corpo, Jandirus.Core.Forms.FormaDef? def)
	{
		// --- a aura: e o gancho que ficou esperando desde a Etapa 7b ---
		if (corpo.GetNodeOrNull<Aura>("Aura") is { } aura)
		{
			// ============================ A VOLTA ESCREVE O MESMO QUE A IDA ============================
			// Este ramo era so `aura.Apagar()`, e nisso estava a segunda queixa do dono: "a aura da base
			// ainda ta brilhando, e ela sai DOURADA".
			//
			// `Apagar` desliga a luz e o desenho, mas NAO desfaz as duas coisas que a ida escreveu: a
			// FOLHA (`AuraSSjBig`, que e arte ja dourada e nao se tinge -- ver `SpriteDeAura.SemTinta`) e
			// a COR guardada (`_corAcesa`). O node ficava em base carregando a chama do Super Saiyajin,
			// esperando alguem acende-lo -- e ha quem acenda: o `Efeito.AuraBase` da cena do Oozaru
			// precisou escrever `Folha(Base)` na marra justamente porque nao dava pra confiar no que
			// estava guardado ali. Isso deixa de ser necessario.
			//
			// A COR DA BASE E O `Aura.CorDoKiCru` e nao o `Aura` da entrada `base` do catalogo, que
			// e `ffffff`: branco multiplicando a folha colorivel APAGA a arte (defeito ja pago uma vez,
			// ver `Aura.Acender`). (Aquela constante morava na `CargaVisual` com o nome `CorCarga`,
			// de quando a carga tinha cor propria -- hoje ha uma resposta so e ela mora na `Aura`.)
			//
			// APAGAR VEM PRIMEIRO. `Preparar` num node ACESO troca a cor E acende (ver `Aura.Preparar`);
			// na ordem inversa a base piscaria por um quadro antes de sumir.
			if (def == null) aura.Apagar();

			// A FOLHA ANTES DA COR. "Toda forma usa colorablebigaura MENOS o LSSJ" -- e quem
			// decide isso e o Core (`Catalogo.Folha`), pela LINHA da forma, e nao um campo por
			// forma que alguem esqueceria de preencher num degrau novo. `Folha(null)` ja devolve
			// `Base`, entao a volta cai na folha certa sem precisar de um caso proprio.
			aura.Folha(Jandirus.Core.Forms.Catalogo.Folha(def));
			// O TERCEIRO ARGUMENTO E A GUARDA DA LUZ, e este e um dos dois lugares do jogo que sabem
			// a resposta: `def == null` E estar na base. Sem forma o node `Aura` nao acende nem que o
			// Ki passe dos 100% -- na base quem desenha e a `CargaVisual`, e ela nao ilumina.
			// Ver `Aura.Aplicar`.
			//
			// A COR E A FORCA SAO PERGUNTADAS (`Aura.CorDaChamaDe` / `ForcaDaChamaDe`) e nao
			// recalculadas: as mesmas duas contas moram no `Transformacao.Vestir` e agora tambem na
			// chama da cinematica -- tres desenhos da mesma chama nao podem ter tres contas.
			aura.Preparar(Aura.CorDaChamaDe(def), Aura.ForcaDaChamaDe(def), def != null);
		}

		// A CARGA E O SEGUNDO DESENHO DA MESMA COISA. Sem esta linha, carregar transformado desenhava
		// a folha da BASE tingida de amarelo em vez da `AuraSSjBig`.
		//
		// AQUI TAMBEM SE ESCREVIA `carga.FormaAcesa = def != null`, o bit que suprimia a chama da carga
		// enquanto a forma tivesse aura propria. Ele morreu junto com a supressao (ver o topo de
		// `CargaVisual.Pintar`): quem impede as duas chamas hoje e a `Aura.ChamaDaCarga`, que e a mesma
		// linha que acende a luz -- e nao um bit repetido em tres arquivos.
		corpo.GetNodeOrNull<CargaVisual>("Carga")?.Folha(Jandirus.Core.Forms.Catalogo.Folha(def));
	}

	/// <summary>
	/// ============================ O CONTORNO E DO KI, NAO DA FORMA ============================
	/// Regra do dono: "vamos fazer ele so aparece quando o ki passa de 100% na transformaçao, entao
	/// 100% ou menos = sem contorno brilhoso, >100% = contorno brilhoso" -- e a cor continua sendo
	/// a da forma.
	///
	/// Isso muda o que o contorno SIGNIFICA. Antes ele dizia "estou transformado", e ficava aceso o
	/// tempo todo; agora ele diz "estou passando do meu limite", e so aparece quando o Ki esta
	/// comprimido acima do teto. Um Super Saiyajin parado deixa de brilhar -- ele brilha quando se
	/// esforca, que e quando a imagem tem o que dizer.
	///
	/// A COR e guardada na troca de forma e o ACENDER e do canal de sobrecarga (`aura_ki`), o mesmo
	/// que ja acende a aura. Guardar em vez de acender e o que permite ele sair na cor certa no
	/// instante em que o Ki passa, sem a forma precisar avisar de novo -- e o mesmo desenho do
	/// `Aura.Preparar`, que resolveu este exato problema pra a aura.
	/// ================================================================================
	///
	/// ============================ E ISSO VALE PRA QUALQUER CORPO DA ZONA ============================
	/// Este metodo comecava com `if (corpo != _local) return;`, e o corpo alheio era servido por uma
	/// SEGUNDA regra (a forma, com a conta `0,35 + Intensidade * 0,13`) escrita no
	/// `AcenderFormaNoCorpo` e repetida no `Transformacao.Vestir`. Tres escritas, duas verdades: na
	/// minha tela um Super Saiyajin parado apagava; na tela dos OUTROS ele ficava contornado enquanto
	/// a transformacao durasse, foi o que o dono viu ("quem se transforma fica sempre com a outline
	/// mesmo sem ativar a aura"), e por tabela o SSJ1 saia em 0,48 contra 0,87 do SSJ4 -- a mesma
	/// conta que o corpo local ja tinha aposentado.
	///
	/// A justificativa daquela divisao ("o cliente nao sabe o Ki dos OUTROS -- sigilo do scouter")
	/// CADUCOU: ver `_sobrecarregados`. O que continua sendo sigilo e o NUMERO (BP e Ki alheios); o
	/// bit "ele esta acima do proprio limite" e coisa que se ve de longe, e o servidor ja o publica.
	/// ==========================================================================================
	/// </summary>
	private void GuardarContornoDaForma(int id, Jandirus.Core.Forms.FormaDef? def)
	{
		_contornoDaForma[id] = (
			// A COR DO CONTORNO NAO E A DA AURA -- ver `Catalogo.CorDoContorno`. Era `def.Aura` aqui, e
			// era por isso que ajustar a aura de um degrau mexia no brilho de todos: a escada Saiyajin
			// tem sete tons de dourado, e o contorno herdava cada um deles.
			new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(def)),
			// ============================ A FORCA E DO FATO, NAO DA FORMA ============================
			// Era `0.35 + Intensidade * 0.13`, herdado de quando o contorno dizia "sou desta forma" --
			// ali fazia sentido um SSJ4 brilhar mais que um SSJ1.
			//
			// Agora ele diz "passei dos 100%", e esse fato e o MESMO em qualquer forma. Com a conta
			// velha o SSJ1 saia em 0,48 contra 0,87 do SSJ4, e o dono viu exatamente isso: contorno no
			// SSJ4, nenhum no SSJ1 -- 0,48 desaparece atras da propria aura, que e grande e clara.
			//
			// A MESMA FORCA PRA TODAS e QUAL forca sao decisoes do dono e moram no CORE
			// (`Catalogo.ForcaDoContorno`), e nao mais num literal aqui. Foi o segundo pedido dele sobre
			// este numero ("um pouco mais fraco") que mudou o lugar: a bancada precisa medir a faixa que
			// o jogador ve, e um literal escondido num metodo privado do `World` obrigaria a bancada a
			// repetir o valor -- que e como se mede um ciclo que o jogo nao tem.
			//
			// E ISTO E O TOPO DO PULSO, e nao o valor desenhado: o contorno respira daqui pra baixo, ate
			// `Catalogo.PisoDoPulsoDoContorno`. Quem faz a respiracao e o
			// `CharacterVisual.ForcaNaFaseDoPulso`, porque quem tem relogio de quadro e o visual -- este
			// lado so responde "quanto", como ja fazia.
			def == null ? 0f : Jandirus.Core.Forms.Catalogo.ForcaDoContorno,
			// A SEGUNDA COR, quando a forma oscila (so o Beast). Nula = contorno parado -- ver
			// `CharacterVisual.AnimarContorno`. Ela e GUARDADA junto com a primeira pelo mesmo motivo
			// que a primeira e guardada: quem acende e o pacote de sobrecarga, e ele nao sabe a forma.
			ContornoAlterna(def));

		AplicarContorno(id);
	}

	/// <summary>
	/// A cor e a forca que o contorno de CADA CORPO DA ZONA tera quando o Ki dele passar dos 100%.
	///
	/// Por id e nao tres campos soltos: eram `_corDoBrilho`/`_forcaDoBrilho`/`_corAlternaDoBrilho`, do
	/// dono da tela e so dele, e era essa singularidade que obrigava o corpo alheio a ter regra
	/// propria. Some com o corpo -- ver `AoSair` e `EsvaziarRemotos`.
	/// </summary>
	private readonly Dictionary<int, (Color Cor, float Forca, Color? Alterna)> _contornoDaForma = [];

	/// <summary>
	/// A OUTRA PONTA do contorno da forma, ou nulo quando ele nao oscila. Mesmo formato do
	/// <see cref="Jandirus.Core.Forms.Catalogo.CorDoRabo"/>: o Core devolve hexa ou nulo, e quem
	/// converte pra <see cref="Color"/> e o cliente. Existe pra os dois chamadores daqui (o
	/// <see cref="GuardarContornoDaForma"/> e a bancada) nao repetirem o mesmo ternario.
	/// </summary>
	private static Color? ContornoAlterna(Jandirus.Core.Forms.FormaDef? def) =>
		Jandirus.Core.Forms.Catalogo.CorDoContornoAlterna(def) is { } hexa ? new Color(hexa) : null;

	/// <summary>
	/// ACENDE OU APAGA O CONTORNO DE UM CORPO QUALQUER DA ZONA -- a COR vem da forma
	/// (<see cref="_contornoDaForma"/>), o ACENDER vem do Ki (<see cref="_sobrecarregados"/>).
	///
	/// ============================ ESTE E O FUNIL, E ELE E UM SO PRA OS DOIS CORPOS ============================
	/// Era `AplicarBrilhoLocal`, e so olhava pro `_local`. O corpo alheio tinha regra propria em dois
	/// outros arquivos, e por isso um Super Saiyajin remoto ficava contornado o tempo inteiro na sua
	/// tela enquanto o seu proprio so acendia acima dos 100%. Nao ha "o meu caso" aqui: quem pergunta
	/// passa um id, e a resposta sai do mesmo par de tabelas.
	///
	/// SEM ENTRADA NA TABELA O CONTORNO E ZERO, e nao ha `if` dizendo isso: um corpo de quem nunca se
	/// transformou nao tem linha em `_contornoDaForma`, o `default` da tupla e forca 0, e forca 0 nao
	/// desenha nem anima (ver `CharacterVisual.AnimarContorno`). E o mesmo idioma do
	/// `Catalogo.CorDoContorno`, que devolve branco neutro justamente porque quem chama passa forca 0.
	///
	/// NAO PRECISA CORRER ATRAS DE CAMADA NOVA: a pelagem do SSJ4 nasce com material zerado, mas o
	/// `_Process` do `CharacterVisual` reescreve o contorno em TODAS as camadas de silhueta enquanto
	/// ele estiver aceso (ver `EscreverContorno`) -- entao a camada que nascer depois se acerta no
	/// quadro seguinte, e apagado nao ha o que acertar.
	/// =====================================================================================================
	/// </summary>
	private void AplicarContorno(int id)
	{
		if (Corpo(id)?.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;
		(Color cor, float forca, Color? alterna) = _contornoDaForma.TryGetValue(id, out var g)
			? g : (Colors.White, 0f, (Color?)null);
		vis.AuraDaForma(cor, _sobrecarregados.Contains(id) ? forca : 0f, alterna);
	}

	/// <summary>
	/// O QUE SE VE DE LONGE FORA DA AURA: os RAIOZINHOS e a NEBULOSA.
	///
	/// ============================ O CONTORNO SAIU DAQUI ============================
	/// Ele morava neste metodo, so pro corpo REMOTO e pela FORMA:
	///
	///   corpo REMOTO -> contorno pela FORMA (aceso enquanto a transformacao durar)
	///   corpo LOCAL  -> contorno pelo KI (so acima dos 100%)
	///
	/// Duas verdades sobre o mesmo pixel, e a errada era a que os OUTROS viam. Hoje ha uma
	/// (<see cref="AplicarContorno"/>) e ela vale pros dois corpos -- a divida que justificava a
	/// divisao (*"o cliente nao sabe o Ki dos OUTROS"*) caducou; ver `_sobrecarregados`.
	///
	/// OS RAIOS SAO DOS DOIS, e ja eram: eles sao da FORMA e nao do Ki. Este metodo continua existindo
	/// exatamente por isso -- ha coisa que a forma acende sozinha, e ha coisa que so o Ki acende. O
	/// buraco que fechou quando o contorno saiu da guarda `corpo != _local` foi o corpo LOCAL nunca
	/// receber `RaiosDaForma.Definir`: os raios do dono so eram acesos pela cinematica
	/// (`Transformacao.Assumir`) e NUNCA apagados -- voltar pra base deixava o jogador crepitando
	/// dourado na forma base pra sempre. Era a metade audivel do "a aura da base ainda ta brilhando".
	/// ================================================================================================
	///
	/// CHAMADO SO QUANDO NAO HA CENA. Havendo cinematica, quem acende isto e o `Transformacao.Vestir`,
	/// degrau a degrau.
	/// </summary>
	private void AcenderFormaNoCorpo(Node2D corpo, Jandirus.Core.Forms.FormaDef? def)
	{
		// --- os RAIOZINHOS: o VOLUME e da forma (`FormaDef.Raios`), a COR e da `CorDosRaios` ---
		// Era `def.Aura` nos dois, e por isso a faisca do SSJ2 e do SSJ3 saia dourada.
		//
		// `Raios > 0` E NAO `def != null`: e o mesmo teste que o `Transformacao.Assumir` faz, e sem ele
		// o SSJ1 e o SSJ4 (que tem `Raios = 0`) ligavam o node pra emitir zero raio -- um `_Process`
		// e um sorteio por quadro, por corpo, pra nao desenhar nada.
		if (corpo.GetNodeOrNull<RaiosDaForma>("Raios") is { } raios)
			raios.Definir(def is { Raios: > 0 },
						  new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(def)), def?.Raios ?? 0);

		// --- A NEBULOSA: quem responde e o Core (`TemNebulosa`), derivado da LINHA da forma ---
		// Ela e dos DOIS corpos, como a faisca e ao contrario do contorno: a nuvem e da FORMA e nao
		// do Ki -- ver o cabecalho deste metodo.
		if (corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is { } nebulosa)
			nebulosa.Definir(Jandirus.Core.Forms.Catalogo.TemNebulosa(def));
	}

	/// <summary>
	/// ALGUEM VIROU (OU DEIXOU DE SER) OOZARU. Irmao do <see cref="AoMudarForma"/>, e pelo mesmo
	/// motivo dele: ver o adversario virar um macaco de dez metros na sua frente e do mundo, nao
	/// ficha pessoal -- por isso vale pra qualquer id da zona e resolve o corpo pelo mesmo
	/// <c>Corpo(id)</c>.
	///
	/// ============================ A CENA DELE E PROPRIA, E POR ISSO ELA E BUSCADA POR ID ============================
	/// `Cinematicas.Para` desvia a LINHA inteira do Oozaru pra cena certa (ver la), e este ponto usa
	/// `Cinematicas.Oozaru` direto porque o estado nao viaja em `S2C.Forma`: nao ha `FormaDef`
	/// chegando pelo fio, so o enum <see cref="Jandirus.Core.Forms.FormaOozaru"/>.
	///
	/// (Historico que explica o cuidado: aquela funcao TINHA fallback por `Ordem` e nunca devolvia
	/// null -- e as ordens do Oozaru colidem com as da escada, 10 = SSJ1 e 20 = SSJ2. Chamar `Para`
	/// aqui poria um macaco de dez metros assistindo, parado, a cinematica de Super Saiyajin. O
	/// fallback foi DELETADO desde entao: hoje `Para` desvia a linha e devolve `null` de proposito
	/// quando nao ha cena, e a bancada acusa o null. O desvio pela linha continua sendo o que protege
	/// este ponto, mas o motivo de nao chamar `Para` aqui virou o outro: nao ha `FormaDef` no fio.)
	/// ==========================================================================================
	///
	/// A CENA TOCA TODA VEZ QUE A FERA NASCE, e nao so na estreia -- ver o comentario de
	/// `Cinematicas.Oozaru`. So o texto no chat e da estreia.
	///
	/// "TODA VEZ QUE ELA NASCE" e nao "todo pacote": desde a sincronia de entrada de zona existe um
	/// pacote que descreve uma fera que nasceu ANTES de eu chegar, e esse vem com
	/// <see cref="Jandirus.Core.Forms.DegrauDeCena.Nenhuma"/>. Ver o bloco da bifurcacao la embaixo.
	/// </summary>
	/// <remarks>
	/// `internal` pelo mesmo motivo do <see cref="AoMudarForma"/>: e por AQUI que se prova que o
	/// macaco NAO segue os tres degraus de maestria. A prova nao e um `if` que se le -- e chamar este
	/// metodo com `primeira: false` (que em qualquer outra forma daria a cena encurtada) e conferir
	/// que a cena que nasce e a CHEIA. Ver `RoboDeForma.AFeraForaDosDegraus`.
	/// </remarks>
	internal void AoVirarOozaru(int id, Jandirus.Core.Forms.FormaOozaru forma, bool primeira,
								Jandirus.Core.Forms.DegrauDeCena degrau)
	{
		_feraDaZona[id] = forma;   // antes do corpo nulo, pelo mesmo motivo do `AoMudarForma`

		Node2D? corpo = Corpo(id);
		if (corpo == null) return;

		// O ESTADO VIRA ENTRADA DE CATALOGO, e nao um `switch` de cores aqui. As duas linhas ja
		// existiam em `Formas.cs` (`oozaru`, castanho; `oozaru_dourado`, dourado com 2 raios) e
		// nada as lia -- campo escrito e nunca lido e exatamente o que este projeto nao deixa ficar.
		Jandirus.Core.Forms.FormaDef? def = forma switch
		{
			Jandirus.Core.Forms.FormaOozaru.Regular => Jandirus.Core.Forms.Catalogo.Def("oozaru"),
			Jandirus.Core.Forms.FormaOozaru.Dourado => Jandirus.Core.Forms.Catalogo.Def("oozaru_dourado"),
			_ => null,
		};

		// ============================ DEIXAR DE SER O MACACO ============================
		// Este bloco nao existia, e a falta dele ficou coberta enquanto o macaco nao tinha corpo:
		// sem `FormaDef.Corpo` nao havia camada pra tirar. Agora ha, e sair sem desfaze-la deixaria o
		// jogador de bicho pra sempre.
		//
		// O CABELO TAMBEM VOLTA. O `oozaru_dourado` tem `SufixoDoCabelo = "SSj"` e o `Assumir` da
		// cena o aplica -- num corpo de criatura ele fica escondido e ninguem ve, mas ele CONTINUA
		// trocado por baixo. Sem esta linha, o instante em que o macaco desfaz devolveria um
		// lutador em forma base com o penteado de Super Saiyajin.
		//
		// E o `S2C.Forma` do SSJ4 chega LOGO DEPOIS deste pacote (canal confiavel e ordenado, ver
		// `GameServer.DesfazerOozaru`), reacendendo por cima o que for dele.
		if (def == null)
		{
			PrepararAuraDaForma(corpo, null);
			GuardarContornoDaForma(id, null);
			VestirAFormaSemCena(id, corpo, null);
			return;
		}

		// A COR E A FOLHA DA FORMA sao escolhidas AGORA, antes da cena -- e a mesma regra do
		// `AoMudarForma`: o beat `Assumir` acende, e ele precisa achar a cor ja guardada. O que NAO
		// vem antes e o `AcenderFormaNoCorpo`: contorno remoto e raios sao o fim da cena, nao o comeco
		// dela (o macaco estrearia ja brilhando, assistindo a propria transformacao).
		//
		// O CACHE DO CONTORNO LOCAL VEM JUNTO, e pelo mesmo motivo do `AoMudarForma`: guardar nao e
		// acender (quem acende e a sobrecarga de Ki), e sem esta linha o dono entraria no macaco
		// carregando a cor da forma anterior -- o mesmo defeito, na segunda das duas portas.
		PrepararAuraDaForma(corpo, def);
		GuardarContornoDaForma(id, def);

		// ============================ A FERA QUE JA ESTAVA LA NAO SE TRANSFORMA DE NOVO ============================
		// `NoDegrau` no lugar do `Cinematicas.Oozaru` cravado: e o MESMO funil do `AoMudarForma`, e ele
		// devolve exatamente a cena do macaco (`Para` desvia pela LINHA -- ver `Cinematicas.Para`).
		// A diferenca e o unico caso em que ele devolve nulo aqui: `DegrauDeCena.Nenhuma`, que o servidor
		// so manda pra quem acaba de ENTRAR na zona.
		//
		// Sem esta bifurcacao, consertar "quem chega nao ve quem ja e macaco" teria criado algo pior:
		// quem chega ficaria PRESO assistindo uma transformacao de dez metros que aconteceu antes de ele
		// existir naquele planeta. A cena do Oozaru dura o roteiro inteiro e o corpo nao anda durante ela.
		//
		// O CHAT SEGUE O MESMO DESTINO. `** Oozaru **` e carimbo de acontecimento; ele exige `primeira`
		// (que a sincronia manda `false`) E a cena. Ver `Protocol.S2C.Oozaru`.
		// ======================================================================================================
		if (Jandirus.Core.Forms.Cinematicas.NoDegrau(def, degrau) is not { } cena)
		{
			VestirAFormaSemCena(id, corpo, def);
			return;
		}

		Transformacao.Rodar(_atores, corpo, def, cena, id == GameClient.Instance?.LocalId, NomeDe(id));

		// PELO FUNIL TAMBEM, com `dominada: false` -- e o `false` aqui e um FATO e nao um chute: o
		// `S2C.Oozaru` e outro pacote e nao carrega o bit, e nao precisa, porque o unico nome que anda
		// com maestria e o do `ssj1` (ver `Catalogo.DominouOSuperSaiyajin`) e nenhuma forma da fera e
		// ele. Passar pelo funil e o que garante que, no dia em que uma segunda forma ganhar nome
		// derivado, o compilador traga alguem ate esta linha em vez de ela mentir calada.
		if (primeira && id == GameClient.Instance?.LocalId)
			Chat.Sistema($"** {Jandirus.Core.Forms.Catalogo.NomeDe(def, dominada: false)} **");
	}

	// O `Degrau(Forma)` QUE MORAVA AQUI FOI DELETADO. Ele era um switch de cinco casos que dizia o
	// quao exagerada e a cinematica, e era o quarto lugar que uma forma nova precisava tocar -- a
	// consequencia de esquecer era sutil e feia: o SSJ4 estreava com o tranco de um SSJ1. Agora o
	// numero e `FormaDef.Intensidade`, campo da propria entrada do catalogo.

	// =====================================================================
	// ALVO
	// =====================================================================
	/// <summary>
	/// Poe na cena os planetas que o servidor mandou. Chamado quando a CHUNK muda -- andar
	/// dentro da mesma chunk nao muda o que ha em volta.
	/// </summary>
	/// <summary>
	/// PLANTA AS CONSTRUCOES da zona. Refaz a lista inteira em vez de casar id por id: sao
	/// dezenas, nao milhares, e o pacote so chega quando algo muda -- nunca por tick.
	///
	/// VAO DENTRO DE `_atores`, junto dos corpos, e nao numa camada propria: se ficassem numa
	/// camada separada, o Y-sort nao teria como intercalar personagem e bancada, e o jogador
	/// passaria sempre na frente ou sempre atras.
	/// </summary>
	private void DesenharObras()
	{
		foreach (Node n in _atores.GetChildren())
			if (n is ObraDesenhada) n.QueueFree();
		if (GameClient.Instance is not { } cli) return;

		// AS CONSTRUCOES SAO PAREDE, e a lista e refeita inteira -- entao a camada de bloqueio
		// tambem. Sem o `LimparObras`, uma bancada derrubada continuaria barrando o caminho, e o
		// jogador levaria correcao de movimento num lugar onde nao ha nada desenhado.
		_colisao?.LimparObras();

		foreach (GameClient.ObraInfo o in cli.Obras)
		{
			// A ANCORA E A BASE DA CELULA que o servidor bloqueia. Desenhar no ponto solto onde o
			// jogador estava poria o desenho e a parede em lugares diferentes.
			(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(o.Pos.X, o.Pos.Y);
			const int t = ZoneCollision.TileSize;

			_atores.AddChild(new ObraDesenhada
			{
				Name = "Obra" + o.Id,
				Position = new Vector2(cx * t, (cy + 1) * t),
				Tipo = o.Tipo,
				Dono = o.Dono,
				Aparafusada = o.Aparafusada,
				Lab = o.Lab,
				Arte = o.Arte,
				Estado = o.Estado,
				Pixel = o.Pixel,
			});

			// QUEM DIZ QUE BLOQUEIA E O SERVIDOR, no proprio pacote. O cliente NAO pergunta ao
			// catalogo local: aquele so tem o que ELE pode comprar, e a bancada de outra pessoa tem
			// que barrar do mesmo jeito.
			if (o.Densa) _colisao?.Bloquear(cx, cy);
		}
	}

	private void DesenharPlanetas()
	{
		foreach (Node n in _orbes.GetChildren()) n.QueueFree();
		if (GameClient.Instance is not { } cli) return;
		if (!Jandirus.Core.World.Espaco.EhEspaco(cli.Zone)) return;

		_ceu.Seed = cli.SeedDoUniverso;
		foreach (GameClient.PlanetaInfo p in cli.Planetas)
			_orbes.AddChild(new PlanetaDesenhado
			{
				Name = "P_" + p.Nome,
				Position = p.Pos,
				Nome = p.Nome,
				Raio = p.Raio,
				Seed = p.Seed,
				Premade = p.Premade,
				// O TIPO decide o icone do planeta GERADO (selva, deserto, gelo). Ele nao trafega:
				// as duas pontas derivam da seed pela MESMA funcao do Core, que e o que ja faz o
				// mundo inteiro nascer igual dos dois lados sem mandar um byte de mapa.
				Tipo = p.Premade ? ""
					 : Jandirus.Core.World.MundoProcedural.DaSeed(p.Seed, p.Nome).Bioma.ToString(),
			});
	}

	/// <summary>Manda o piloto automatico levar o corpo ate um ponto (o nav system).</summary>
	public void Pilotar(Vector2 destino)
	{
		if (_local == null) return;
		_local.Destino = new Vec2(destino.X, destino.Y);
	}

	/// <summary>
	/// A ULTIMA POSICAO NO ESPACO -- de onde eu desci.
	///
	/// ============================ POR QUE A CARTA PRECISA DISTO ============================
	/// `PosicaoLocal` e a coordenada do CORPO, e ela troca de significado ao pousar: no espaco e
	/// coordenada de GALAXIA, em terra e coordenada de SUPERFICIE. O spawn da Terra, por exemplo,
	/// e (7984, 8016) -- que na galaxia cai a 11 mil px da origem, FORA do raio 220 da propria
	/// Terra. Usar isso no mapa punha o "voce esta aqui" num ponto sem sentido, fazia o
	/// "centralizar em mim" centrar no vazio e calculava o tempo de viagem a partir de uma posicao
	/// falsa.
	///
	/// Guardado no momento em que a zona muda, ou seja no instante ANTES do pouso: e onde a nave
	/// ficou. Pra os sete pre-feitos o mapa prefere a posicao do proprio planeta (que e exata);
	/// isto aqui e o que salva o mundo GERADO, cuja posicao ninguem mais sabe depois da descida.
	/// =======================================================================================
	/// </summary>
	public Vector2? UltimaNoEspaco { get; private set; }

	/// <summary>Pra onde o piloto automatico esta indo, ou nulo. E o que o mapa estelar desenha.</summary>
	public Vec2? DestinoDoPiloto => _local != null && IsInstanceValid(_local) ? _local.Destino : null;

	/// <summary>Desliga o piloto. O mesmo caminho que encostar numa tecla de movimento usa.</summary>
	public void SoltarPiloto()
	{
		if (_local != null && IsInstanceValid(_local)) _local.Destino = null;
	}

	/// <summary>Onde EU estou, em coordenada de mundo. Nulo antes de entrar.</summary>
	public Vector2? PosicaoLocal => _local != null && IsInstanceValid(_local) ? _local.GlobalPosition : null;

	/// <summary>
	/// Onde esta quem eu MARQUEI. Nulo se nao ha alvo, ou se ele nao esta na minha zona.
	///
	/// Existe pro corpo local se VIRAR pro alvo ao socar. O servidor ja fazia isso (`Atacar` gira
	/// `a.Facing` pelo marcado), mas o servidor so manda direcao pros OUTROS -- o proprio sprite e
	/// desenhado pelo cliente, com a direcao que ELE calculou do movimento. Resultado: marcar
	/// alguem nas costas e apertar espaco fazia o personagem socar de costas na tela do dono, mesmo
	/// com o golpe saindo certo no servidor.
	/// </summary>
	public Vector2? PosicaoDoAlvo
	{
		get
		{
			int id = GameClient.Instance?.AlvoId ?? 0;
			if (id == 0) return null;
			return _remotos.TryGetValue(id, out RemotePlayer? r) && IsInstanceValid(r) ? r.GlobalPosition : null;
		}
	}

	/// <summary>
	/// As portas da zona atual, por celula. PUBLICO SO PRA BANCADA (`--porta`): e o unico jeito de
	/// um teste sem janela conferir que a porta abriu de verdade -- que a animacao trocou, que o
	/// mapa de colisao soltou a celula e que o de visao parou de cegar.
	/// </summary>
	public IReadOnlyDictionary<(int X, int Y), Porta> Portas => _portasPorCelula;

	/// <summary>O mapa de colisao da zona atual. Ver <see cref="Portas"/>.</summary>
	public ZoneCollision? Colisao => _colisao;

	/// <summary>O mapa do que CEGA na zona atual. Ver <see cref="Portas"/>.</summary>
	public ZoneCollision? Visao => _veu?.Mapa;

	/// <summary>A skill que abre a piscada por duplo clique -- a mesma da imagem remanescente.</summary>
	private const string PathDoZanzoken = "/datum/skill/ki/Afterimage";

	/// <summary>Quanto perto do clique um personagem precisa estar pra ser o escolhido.</summary>
	private const float RaioDoClique = 22f;

	private MarcaDeAlvo? _marca;

	/// <summary>
	/// DUPLO CLIQUE MARCA O ALVO; duplo clique no vazio solta.
	///
	/// Vale pra qualquer um na tela, mesmo longe: marcar e dizer "e com esse ai", nao e o
	/// golpe. Quem decide se da pra alcancar e o servidor, na hora do soco.
	/// </summary>
	public override void _UnhandledInput(InputEvent e)
	{
		if (e is not InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left }) return;
		if (Foco.Digitando || GameClient.Instance is not { } cli) return;

		Vector2 alvo = GetGlobalMousePosition();
		int escolhido = 0;
		float melhor = RaioDoClique * RaioDoClique;

		foreach ((int id, RemotePlayer r) in _remotos)
		{
			if (!IsInstanceValid(r)) continue;
			// o clique acerta o CORPO, e o corpo esta acima do pe do sprite
			float d = (r.GlobalPosition - alvo).LengthSquared();
			if (d >= melhor) continue;
			melhor = d;
			escolhido = id;
		}

		// CLICOU NO VAZIO E TENHO ZANZOKEN: e uma PISCADA, nao um "soltar alvo".
		//
		// O gesto e o mesmo do dono: "ao dar double click no chao ele de o teleporte, deixando a
		// miragem a onde estava". Clicar em CIMA de alguem continua marcando alvo -- as duas coisas
		// nao competem porque uma pede um corpo e a outra pede chao.
		//
		// Quem NAO tem a skill mantem o comportamento antigo (duplo clique no vazio solta o alvo),
		// senao a tecnica roubaria um controle que ja existia.
		if (escolhido == 0 && cli.SkillsAprendidas.Contains(PathDoZanzoken))
		{
			// DE ONDE EU SAIO, gravado AGORA -- no instante do gesto, como o soco ja fazia. Quando
			// o servidor confirmar, a miragem nasce aqui e nao na posicao (atrasada) que ele mandar.
			// Ver `LocalPlayer.DeixarVulto`.
			_local?.MarcarSaida();
			cli.SendZanzoken(new Vec2(alvo.X, alvo.Y));
			GetViewport().SetInputAsHandled();
			return;
		}

		cli.SendAlvo(escolhido);
		MarcarNaCena(escolhido);
		GetViewport().SetInputAsHandled();
	}

	/// <summary>Poe (ou tira) o anel de mira aos pes de quem foi escolhido.</summary>
	private void MarcarNaCena(int id)
	{
		if (_marca != null) { _marca.QueueFree(); _marca = null; }

		if (id == 0)
		{
			Chat.Sistema("alvo solto.");
			return;
		}
		if (!_remotos.TryGetValue(id, out RemotePlayer? r) || !IsInstanceValid(r)) return;

		_marca = new MarcaDeAlvo { Name = "Alvo", Position = new Vector2(0, 14) };
		r.AddChild(_marca);
		Chat.Sistema($"alvo: {(_nomes.TryGetValue(id, out string? n) && n.Length > 0 ? n : $"#{id}")}");
	}

	/// <summary>
	/// Quem esta na tela AGORA, por nome. E a aba "People" do menu -- o "Known People" do
	/// original.
	///
	/// So entra quem o veu deixa ver: estar na mesma zona nao basta, tem que estar do lado de
	/// ca da parede. E a primeira coisa que <see cref="Visao.Ve"/> passou a servir alem de
	/// desenhar.
	/// </summary>
	public List<string> NomesVisiveis()
	{
		var nomes = new List<string>();
		foreach ((int id, RemotePlayer r) in _remotos)
		{
			if (!IsInstanceValid(r) || !_veu.Ve(r.GlobalPosition)) continue;
			nomes.Add(_nomes.TryGetValue(id, out string? n) && n.Length > 0 ? n : $"desconhecido #{id}");
		}
		nomes.Sort(StringComparer.OrdinalIgnoreCase);
		return nomes;
	}

	/// <summary>
	/// O ESTADO VISUAL INTEIRO DE UM CORPO, escrito de uma vez: ficha, feridas, forma e fera.
	///
	/// ============================ UM SO LUGAR DESCREVE UM CORPO ============================
	/// Isto se chamava `VestirSePuder` e era so o NASCIMENTO. Havia um segundo caminho, o
	/// <see cref="AoReceberAparencia"/>, que chamava `CharacterVisual.Vestir` sozinho quando o
	/// `PeerLook` chegava num corpo que JA existia -- e `Vestir` remonta as camadas do zero: ele
	/// reescreve o penteado base (`CharacterVisual.cs:678`), nao repoe as coladas e nao repoe o
	/// contorno. Ou seja o caminho de aparencia DESPIA a forma de quem estava transformado.
	///
	/// A corrida e real e nao teorica: o `PeerLook` viaja no canal CONFIAVEL
	/// (`GameServer.TrocarAparencias`) e o boneco nasce do SNAPSHOT, canal NAO-confiavel. Nao ha ordem
	/// garantida entre canais diferentes -- ora a ficha chega antes do corpo (e ai o nascimento veste
	/// tudo), ora chega depois (e ai a ficha despia a forma). Foi essa metade que o dono viu como "as
	/// transformacoes nao estao sincronizando com quem acabou de entrar no server".
	///
	/// Os dois caminhos passam a sair daqui. E a mesma razao de existir do `_formaDaZona` e do
	/// `_looks`: descricao parcial nao veste -- ela sobrescreve um pedaco e deixa o resto do estado
	/// anterior no corpo.
	/// ==================================================================================
	/// </summary>
	private void VestirCorpoInteiro(int id, Node corpo)
	{
		var v = corpo.GetNodeOrNull<CharacterVisual>("Visual");
		if (v == null) return;

		if (_visual != null && _looks.TryGetValue(id, out var l)) v.Vestir(_visual, l.Ap, l.Raca, l.Genero);

		// AS FERIDAS TAMBEM SAO GUARDADAS. Mesma razao do `_looks`: o pacote pode ter chegado antes
		// de este boneco existir -- e um corpo que nasce limpo depois de o servidor ja ter dito que
		// ele esta destrocado e o mesmo desencontro que a aparencia teve.
		if (GameClient.Instance?.Feridas.TryGetValue(id, out var m) == true) v.Ferir(m, id);

		// ============================ E A FORMA, QUE ERA O TERCEIRO DESENCONTRO ============================
		// O servidor manda o estado de forma de todo mundo assim que eu entro na zona
		// (`GameServer.SincronizarFormas`) -- e naquele instante nenhum destes bonecos existia. Este e o
		// ponto em que o pacote que chegou cedo demais finalmente vira pixel.
		//
		// SEM CENA, sempre: um corpo que acaba de nascer na minha tela nunca esta "se transformando".
		// Por isso o caminho e o `VestirAFormaSemCena` direto, e nao um `AoMudarForma` reencenado.
		// ============================================================================================
		if (corpo is not Node2D n2) return;
		if (_formaDaZona.TryGetValue(id, out int rede)
			&& Jandirus.Core.Forms.Catalogo.PorRede((ushort)rede) is { Id: not "base" } df)
		{
			PrepararAuraDaForma(n2, df);
			GuardarContornoDaForma(id, df);
			VestirAFormaSemCena(id, n2, df);
		}

		// A FERA POR CIMA DA ESCADA, na mesma ordem em que os dois pacotes chegam do servidor: o corpo
		// do macaco substitui o do lutador, nunca o contrario.
		if (_feraDaZona.TryGetValue(id, out Jandirus.Core.Forms.FormaOozaru fera)
			&& Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Oozaru.Id(fera)) is { } dfera)
		{
			PrepararAuraDaForma(n2, dfera);
			GuardarContornoDaForma(id, dfera);
			VestirAFormaSemCena(id, n2, dfera);
		}

		// E O CONTORNO, MESMO SEM FORMA NENHUMA. Os dois blocos acima ja o escrevem quando ha forma; um
		// corpo que nasce (ou reveste a ficha) na BASE tambem tem que ter a resposta escrita, senao ele
		// fica com o que estivesse no material -- e num `Vestir` que remonta camadas isso e zero por
		// acidente, e nao por regra. Ver `AplicarContorno`.
		AplicarContorno(id);
	}

	/// <summary>O CharacterVisual do meu proprio boneco. SO PRA BANCADA (`--diagferida`).</summary>
	public CharacterVisual? VisualLocalDeTeste =>
		_local != null && IsInstanceValid(_local) ? _local.GetNodeOrNull<CharacterVisual>("Visual") : null;

	/// <summary>
	/// A FICHA VISUAL GUARDADA DE ALGUEM (raca, genero, aparencia). SO PRA BANCADA -- ver
	/// `--diagforma`, `ORaboEOOlhoSobrevivemAFicha`.
	///
	/// Existe pra a bancada poder DEVOLVER o que achou. Aquele teste manda uma ficha adulterada pelo
	/// <see cref="AoReceberAparencia"/> (o unico canal que existe pra isso) pra provar que a forma nao
	/// e despida por ela -- e uma ficha adulterada no `_looks` do jogador local nao morre com o teste:
	/// todo <see cref="VestirCorpoInteiro"/> seguinte a leria, e as FOTOS que a bancada tira depois
	/// sairiam com o boneco errado. Sem este leitor a bancada so poderia restaurar um palpite
	/// ("Saiyan"/"Male"), que e como um teste passa a mentir sobre o proximo.
	/// </summary>
	internal (string Raca, string Genero, Jandirus.Core.Appearance.Appearance Ap)? LookDeTeste(int id) =>
		_looks.TryGetValue(id, out var l) ? l : null;

	/// <summary>Chegou mascara nova: acha o boneco (meu ou de outro) e repinta.</summary>
	private void AoMudarFeridas(int id)
	{
		if (GameClient.Instance is not { } cli || !cli.Feridas.TryGetValue(id, out var m)) return;

		CharacterVisual? v = id == cli.LocalId
			? _local?.GetNodeOrNull<CharacterVisual>("Visual")
			: _remotos.TryGetValue(id, out RemotePlayer? r) && IsInstanceValid(r)
				? r.GetNodeOrNull<CharacterVisual>("Visual")
				: null;
		v?.Ferir(m, id);
	}
}
