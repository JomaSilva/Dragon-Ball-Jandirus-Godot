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

	/// <summary>A hora do dia (0 = meia-noite, 1 = meia-noite de novo). O HUD mostra.</summary>
	public double? Hora => _luzDoMundo?.Fase;

	private Camera2D? _camera;

	private const string Manifesto = "res://Assets/Maps/manifest.json";

	private Jandirus.Core.Appearance.VisualCatalog? _visual;
	// a aparencia de cada um chega UMA vez, e pode chegar ANTES do boneco existir
	private readonly Dictionary<int, (string Raca, string Genero, Jandirus.Core.Appearance.Appearance Ap)> _looks = [];
	private LocalPlayer? _local;
	private readonly Dictionary<int, RemotePlayer> _remotos = [];
	private readonly Dictionary<int, ulong> _ultimoPacoteMs = [];
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
			cli.PortasMudaram += AoMudarPortas;
			// O Boot instancia o World DENTRO do callback de Joined, ou seja, este _Ready
			// roda DEPOIS do evento. Assinar nao basta: se ja entramos, aplica agora.
			if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.Zone, cli.LocalSpawn, cli.LocalName);

			cli.ZoneChanged += (z, spawn) =>
			{
				CarregarZona(z);
				_local?.Teleportar(spawn);
			};
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
			if (GodotObject.IsInstanceValid(no)) no.Free();
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
		}
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
	private void AoPiscar(int quem, Vec2 de)
	{
		Node2D? corpo = Corpo(quem);
		if (corpo == null) return;
		Zanzoken.Deixar(_atores, corpo, new Vector2(de.X, de.Y));
		AudioDirector.EfeitoNoLugar(corpo, Trilha.Teleporte, 0.7f);
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
	private void CarregarZona(ZoneKey zona)
	{
		GuardarZonaAtual();

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
			if (_local != null) _local.Mapa = null;
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
			AddChild(gerado);
			MoveChild(gerado, 0);   // atras dos atores, como a cena pre-feita
			gerado.Entrar(zona.Seed);   // e a seed do SERVIDOR que decide o mundo

			_colisao = gerado.Colisao;
			_veu.Mapa = gerado.Sombra ?? gerado.Colisao;
			_veu.Colisao = gerado.Colisao;
			_veu.Camadas = CamadasDoCenario(gerado);
			if (_local != null) _local.Mapa = _colisao;
			DesenharObras();
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
			return;
		}

		ulong tLoad = Time.GetTicksUsec();

		// AINDA VIVO? Entao nao ha o que ler nem instanciar -- so recolocar na arvore.
		if (_zonasVivas.Remove(zona, out Node2D? guardado) && GodotObject.IsInstanceValid(guardado))
		{
			_ordemDoCache.Remove(zona);
			_zonaAtual = guardado;
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
		AddChild(_zonaAtual);
		MoveChild(_zonaAtual, 0);

		_zonaDoAtual = zona;

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

		GD.Print($"[world] zona carregada: {e.Zona} (z{e.Z}, {e.W}x{e.H})"
				 + (_colisao != null ? " com colisao" : " SEM colisao"));
		Chat.Sistema($"voce esta em {e.Zona}.");

		// o som do LUGAR: vento, mar, cidade. Troca junto com o planeta.
		AudioDirector.Instance?.Ambiente(Trilha.AmbienteDe(e.Zona));
		AudioDirector.Instance?.Musica(Trilha.MusicaDe(e.Zona), AudioDirector.Camada.Lugar);

		ulong tLuz = Time.GetTicksUsec();
		// fogueiras, tochas e lava do planeta novo (as do anterior somem junto com ele)
		_luzDoMundo.CarregarLuzes(e.Luzes);
		GD.Print($"[perf] {e.Zona}: mapas {(tLuz - tCol) / 1000.0:0.0} ms | luzes {(Time.GetTicksUsec() - tLuz) / 1000.0:0.0} ms"
				 + $" | TOTAL {(Time.GetTicksUsec() - tLoad) / 1000.0:0.0} ms");
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

		Node2D saindo = _zonaAtual;
		ZoneKey chave = _zonaDoAtual;
		_zonaAtual = null;

		RemoveChild(saindo);

		// zona sem chave util (o mundo ainda nao entrou em nenhuma): nao da pra cachear
		if (chave.Hash == 0) { saindo.Free(); return; }

		_zonasVivas[chave] = saindo;
		_ordemDoCache.Remove(chave);
		_ordemDoCache.Add(chave);

		while (_ordemDoCache.Count > TetoDoCache)
		{
			ZoneKey velha = _ordemDoCache[0];
			_ordemDoCache.RemoveAt(0);
			if (_zonasVivas.Remove(velha, out Node2D? no) && GodotObject.IsInstanceValid(no)) no.Free();
		}
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
		CarregarZona(zona);
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
		VestirSePuder(id, corpo);
		GD.Print($"[world] {nome} pronto em {zona}");
		Chat.Sistema($"bem-vindo, {nome}.");
	}

	private void AoReceberSnapshot(List<EntityState> estados)
	{
		ulong agora = Time.GetTicksMsec();
		foreach (EntityState e in estados)
		{
			if (GameClient.Instance != null && e.Id == GameClient.Instance.LocalId) continue; // eu me desenho sozinho

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

			double desde = _ultimoPacoteMs.TryGetValue(e.Id, out ulong antes) ? (agora - antes) / 1000.0 : Protocol.TickSeconds;
			_ultimoPacoteMs[e.Id] = agora;
			r.Receive(e.Pos, (Facing)e.Facing, e.Moving, e.Pose, desde, e.Rabo);
			if (r.GetNodeOrNull<HealthBar>("Vida") is { } barra) barra.Vida = e.Vida / 100f;

			// A AURA DE POWER-UP DO OUTRO. Vem no snapshot justamente pra isto (ver
			// EntityState.Carregando): quem esta lutando precisa ver o adversario juntando poder.
			if (r.GetNodeOrNull<CargaVisual>("Carga") is { } cg) cg.Definir(e.Carregando, e.Sobrecarregado);

			// INVISIVEL: some, mas o no CONTINUA VIVO e recebendo posicao. Apagar o corpo faria
			// ele reaparecer no lugar errado quando voltasse (o cliente teria perdido a
			// interpolacao inteira) -- e reaparecer teleportando entrega quem estava escondido.
			r.Visible = !e.Oculto;
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
		_ultimoPacoteMs.Remove(id);
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
			AudioDirector.EfeitoNoLugar(quemBate, Trilha.Dash, 0.7f);

		if (h.Zanzo && quemBate != null)
		{
			if (GameClient.Instance?.LocalId == h.Atacante) _local?.DeixarVulto();
			else Zanzoken.Deixar(_atores, quemBate);
		}

		var desfecho = (Jandirus.Core.Combat.Desfecho)h.Desfecho;
		Vector2 meio = quemBate != null && quemLeva != null
			? (quemBate.Position + quemLeva.Position) * 0.5f
			: (quemLeva ?? quemBate)?.Position ?? Vector2.Zero;
		Vector2 rumo = quemBate != null && quemLeva != null
			? (quemLeva.Position - quemBate.Position).Normalized()
			: Vector2.Zero;

		switch (desfecho)
		{
			case Jandirus.Core.Combat.Desfecho.Acertou:
			case Jandirus.Core.Combat.Desfecho.Critico:
			{
				bool crit = desfecho == Jandirus.Core.Combat.Desfecho.Critico;
				float forca = h.Nivel switch { >= 3 => 1.35f, 2 => 1.0f, _ => 0.8f };
				if (crit) forca = 1.6f;

				Piscar(quemLeva, Quente, crit ? Dourado : Laranja, rumo, crit ? 0.22 : 0.15);
				CombatFx.Impacto(_atores, meio, forca, crit ? Dourado : Colors.White);
				if (crit) CombatFx.Onda(_atores, meio, 96, Dourado);
				Tremer(souEu, crit ? 8f : h.Nivel switch { >= 3 => 5f, 2 => 3f, _ => 1.5f });
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
				// importa. O servidor ja marca quem tem a skill no relato (`h.Zanzo`).
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

	public override void _Process(double delta)
	{
		if (_tremor > 0 && _camera != null)
		{
			_tremor = Mathf.MoveToward(_tremor, 0, (float)delta * 40f);
			_camera.Offset = _tremor <= 0
				? Vector2.Zero
				: new Vector2(Sorte.Randf() * 2 - 1, Sorte.Randf() * 2 - 1) * _tremor;
		}

		if (_lutaAte <= 0) return;
		_lutaAte -= delta;
		if (_lutaAte <= 0) AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Combate);
	}

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

	/// <summary>Onde EU estou, em coordenada de mundo. Nulo antes de entrar.</summary>
	public Vector2? PosicaoLocal => _local != null && IsInstanceValid(_local) ? _local.GlobalPosition : null;

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
		if (_visual == null || !_looks.TryGetValue(id, out var l)) return;
		corpo.GetNode<CharacterVisual>("Visual").Vestir(_visual, l.Ap, l.Raca, l.Genero);
	}
}
