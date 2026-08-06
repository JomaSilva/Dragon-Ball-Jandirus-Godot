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
			cli.Golpe += AoGolpe;
			cli.FormaMudou += AoMudarForma;
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
			cli.Golpe -= AoGolpe;
			cli.FormaMudou -= AoMudarForma;
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
	private void AoCairEfeito(string efeito, long ms)
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
				AplicarAuraLocal();
				break;

			// PASSOU DOS 110% DE KI. Independente da tecla: quem esta sobrecarregado continua
			// aceso depois de soltar o C, e so apaga quando o Ki volta pro lugar.
			case "aura_ki":
				_sobrecarga = ligado;
				AplicarAuraLocal();
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

	private bool _auraDaCarga, _sobrecarga;

	/// <summary>Escreve os dois estados no corpo local. Nulo antes de entrar no mundo.</summary>
	private void AplicarAuraLocal()
	{
		if (_local?.GetNodeOrNull<CargaVisual>("Carga") is { } cg)
			cg.Definir(_auraDaCarga || _sobrecarga, _sobrecarga);
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
		corpo.AddChild(NovoVisual());
		corpo.AddChild(new Aura { Name = "Aura" });
		corpo.AddChild(new CargaVisual { Name = "Carga" });
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
		_atores.AddChild(corpo);
		_local = corpo;

		// OS VERBS FIXOS entram agora. As skills registram os DELAS quando sao aprendidas
		// (`Habilidades`); estes existem pra todo personagem, entao nascem com o corpo.
		VerbosDoJogo.Registrar();
		VestirSePuder(id, corpo);
		GD.Print($"[world] {nome} pronto em {zona}");
		Chat.Sistema($"bem-vindo, {nome}.");
	}

	private void AoReceberSnapshot(List<EntityState> estados)
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
				_local?.ReceberAltura(e.Voando, e.Altitude);
				continue;
			}

			if (!_remotos.TryGetValue(e.Id, out RemotePlayer? r))
			{
				r = new RemotePlayer { Name = $"Remoto{e.Id}", Position = new Vector2(e.Pos.X, e.Pos.Y) };
				r.AddChild(NovoVisual());
				r.AddChild(new Aura { Name = "Aura" });
				r.AddChild(new CargaVisual { Name = "Carga" });
				r.AddChild(new RastroDeCorrida { Name = "Rastro" });
				r.AddChild(new HealthBar { Name = "Vida" });
				_atores.AddChild(r);
				_remotos[e.Id] = r;
				VestirSePuder(e.Id, r);
				GD.Print($"[world] entrou no meu campo de visao: id {e.Id}");
			}

			r.Receive(e.Pos, (Facing)e.Facing, e.Moving, e.Deitado, e.Pose, e.Correndo, e.Rabo, e.Altitude);

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
			r.Visible = Jandirus.Core.World.Voo.Enxerga(
				Jandirus.Core.World.Voo.Andar(_local?.Altitude ?? 0f),
				Jandirus.Core.World.Voo.Andar(e.Altitude));
			if (r.GetNodeOrNull<HealthBar>("Vida") is { } barra) barra.Vida = e.Vida / 100f;
			r.GuardarVida(e.Vida);   // os decalques de sangue perguntam por ela

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
			r.Visible = !e.Oculto;
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
		foreach (RemotePlayer r in _remotos.Values) r.QueueFree();
		_remotos.Clear();

		if (GameClient.Instance is { } c && c.AlvoId != 0)
		{
			_marca = null;
			c.SendAlvo(0);
		}
	}

	private void AoSair(int id)
	{
		if (_remotos.Remove(id, out RemotePlayer? r)) r.QueueFree();
		_looks.Remove(id);
		_nomes.Remove(id);

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

		if (_tremor > 0 && _camera != null)
		{
			_tremor = Mathf.MoveToward(_tremor, 0, (float)delta * 40f);
			_camera.Offset = _tremor <= 0
				? Vector2.Zero
				: new Vector2(Sorte.Randf() * 2 - 1, Sorte.Randf() * 2 - 1) * _tremor;
		}

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
		if (souEu && forca > _tremor) _tremor = forca;
	}

	/// <summary>
	/// Sacode a camera de fora (a cinematica de transformacao usa). O tremor vale so pra
	/// QUEM esta olhando -- e camera, nao mundo.
	/// </summary>
	public void Sacudir(float forca, float peso = 1f)
	{
		float f = forca * Mathf.Clamp(peso, 0f, 1f);
		if (f > _tremor) _tremor = f;
	}

	private float _tremor;
	private static readonly RandomNumberGenerator Sorte = new();

	private static void Som(Node2D? onde, string caminho, float volume = 1f)
	{
		if (onde != null && caminho.Length > 0) AudioDirector.EfeitoNoLugar(onde, caminho, volume);
	}

	private static CharacterVisual NovoVisual() => new() { Name = "Visual" };

	/// <summary>
	/// A aparencia de alguem. Pode chegar ANTES do boneco existir (o snapshot e quem cria
	/// o RemotePlayer, e ele vem por outro canal) -- entao guarda sempre, e veste se ja der.
	/// </summary>
	private void AoReceberAparencia(int id, string nome, string raca, string genero,
									Jandirus.Core.Appearance.Appearance ap)
	{
		_looks[id] = (raca, genero, ap);
		_nomes[id] = nome;
		if (_visual == null) return;
		if (GameClient.Instance != null && id == GameClient.Instance.LocalId)
			_local?.GetNode<CharacterVisual>("Visual").Vestir(_visual, ap, raca, genero);
		else if (_remotos.TryGetValue(id, out RemotePlayer? r))
			r.GetNode<CharacterVisual>("Visual").Vestir(_visual, ap, raca, genero);
	}

	// =====================================================================
	// TRANSFORMACAO
	// =====================================================================
	/// <summary>
	/// Alguem mudou de forma. Vale pra QUALQUER um da zona, nao so pra mim: ver o adversario
	/// virar Super Saiyajin na sua frente e metade da graca.
	/// </summary>
	private void AoMudarForma(int id, int de, int para, bool primeira)
	{
		Node2D? corpo = Corpo(id);
		if (corpo == null) return;

		var forma = (Jandirus.Core.Forms.Forma)para;
		Jandirus.Core.Forms.FormaDef? def = Jandirus.Core.Forms.EscadaSaiyajin.Def(forma);

		// --- a aura: e o gancho que ficou esperando desde a Etapa 7b ---
		if (corpo.GetNodeOrNull<Aura>("Aura") is { } aura)
		{
			if (def == null) aura.Apagar();
			else aura.Acender(new Color(def.Aura), 0.8f + Degrau(forma) * 0.5f);
		}

		// A AURA DA FORMA CALA A DE CARGA. E a regra do proprio DM: o `AuraCheck()` desiste na
		// hora quando ja ha aura de forma acesa, com o comentario "o power-up NAO empilha a aura
		// base por cima (ficavam 2 auras)". Duas PointLight2D no mesmo corpo somam energia e
		// lavam o sprite -- o personagem vira um borrao branco em vez de um Super Saiyajin.
		if (corpo.GetNodeOrNull<CargaVisual>("Carga") is { } carga) carga.FormaAcesa = def != null;

		// --- o cabelo: o Saiyajin doura ---
		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is { } vis)
			vis.PintarCabelo(def == null ? null : new Color(def.Cabelo));

		if (def == null)
		{
			if (id == GameClient.Instance?.LocalId) Chat.Sistema("voce volta ao normal.");
			return;
		}

		// --- a cinematica: SO na primeira vez daquela forma ---
		if (primeira) Transformacao.Rodar(_atores, corpo, new Color(def.Aura), Degrau(forma));
		else
		{
			// nas seguintes, um tranco curto e o som -- o momento continua existindo, so nao
			// para o jogo por tres segundos
			Sacudir(3f + Degrau(forma));
			AudioDirector.EfeitoNoLugar(corpo, Trilha.Dash, 0.9f);
		}
	}

	/// <summary>Quao alto na escada esta a forma. Escala o exagero da cena.</summary>
	private static int Degrau(Jandirus.Core.Forms.Forma f) => f switch
	{
		Jandirus.Core.Forms.Forma.Ssj1 => 1,
		Jandirus.Core.Forms.Forma.Grade2 or Jandirus.Core.Forms.Forma.Grade3 => 2,
		Jandirus.Core.Forms.Forma.Ssj2 => 2,
		Jandirus.Core.Forms.Forma.Ssj3 => 3,
		Jandirus.Core.Forms.Forma.Ssj4 => 4,
		_ => 1,
	};

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

	/// <summary>Veste um boneco recem-criado com a aparencia que ja tiver chegado.</summary>
	private void VestirSePuder(int id, Node corpo)
	{
		var v = corpo.GetNodeOrNull<CharacterVisual>("Visual");
		if (v == null) return;

		if (_visual != null && _looks.TryGetValue(id, out var l)) v.Vestir(_visual, l.Ap, l.Raca, l.Genero);

		// AS FERIDAS TAMBEM SAO GUARDADAS. Mesma razao do `_looks`: o pacote pode ter chegado antes
		// de este boneco existir -- e um corpo que nasce limpo depois de o servidor ja ter dito que
		// ele esta destrocado e o mesmo desencontro que a aparencia teve.
		if (GameClient.Instance?.Feridas.TryGetValue(id, out var m) == true) v.Ferir(m, id);
	}

	/// <summary>O CharacterVisual do meu proprio boneco. SO PRA BANCADA (`--diagferida`).</summary>
	public CharacterVisual? VisualLocalDeTeste =>
		_local != null && IsInstanceValid(_local) ? _local.GetNodeOrNull<CharacterVisual>("Visual") : null;

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
