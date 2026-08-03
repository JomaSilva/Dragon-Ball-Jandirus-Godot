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
	private Node2D _atores = null!;
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

		MontarCenario();

		_atores = new Node2D { Name = "Atores", YSortEnabled = true };
		AddChild(_atores);

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
			cli.EfeitoCaiu += (efeito, ms) =>
			{
				// A CEGUEIRA E O CAMPO DE VISAO INDO A ZERO -- ver Visao.CegoAte.
				if (efeito == "cegueira")
					_veu.CegoAte = ms <= 0 ? 0 : Time.GetTicksMsec() + (ulong)ms;
			};
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
		}
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
		if (_zonaAtual != null) { _zonaAtual.QueueFree(); _zonaAtual = null; }

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
			if (_local != null) _local.Mapa = _colisao;
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

		var cena = ResourceLoader.Load<PackedScene>(e.Cena);
		_zonaAtual = cena.Instantiate<Node2D>();
		// SEM z_index PROPRIO. Um z fixo separaria a cena dos atores em duas camadas estanques
		// e a arvore voltaria a ficar sempre na frente (ou sempre atras) do personagem. Quem
		// decide e o Y, e pra isso os dois precisam viver no MESMO z.
		AddChild(_zonaAtual);
		MoveChild(_zonaAtual, 0);

		// A MESMA colisao que o servidor usa. Sem ela o cliente atravessa parede, o servidor
		// recusa e devolve correcao -- e e ESSA briga que faz o personagem tremer no muro.
		_colisao = Godot.FileAccess.FileExists(e.Colisao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(e.Colisao))
			: null;
		if (_colisao == null) GD.PushWarning($"[world] zona '{zona}' sem colisao: da pra atravessar parede");
		if (_local != null) _local.Mapa = _colisao;

		// O QUE CEGA e outro mapa: parede e porta cegam, arvore e cerca nao (ver MapConverter).
		_veu.Mapa = Godot.FileAccess.FileExists(e.Visao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(e.Visao))
			: null;
		if (_veu.Mapa == null) GD.PushWarning($"[world] zona '{zona}' sem mapa de visao: parede nao esconde nada");

		GD.Print($"[world] zona carregada: {e.Zona} (z{e.Z}, {e.W}x{e.H})"
				 + (_colisao != null ? " com colisao" : " SEM colisao"));
		Chat.Sistema($"voce esta em {e.Zona}.");

		// o som do LUGAR: vento, mar, cidade. Troca junto com o planeta.
		AudioDirector.Instance?.Ambiente(Trilha.AmbienteDe(e.Zona));
		AudioDirector.Instance?.Musica(Trilha.MusicaDe(e.Zona), AudioDirector.Camada.Lugar);

		// fogueiras, tochas e lava do planeta novo (as do anterior somem junto com ele)
		_luzDoMundo.CarregarLuzes(e.Luzes);
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
	/// <summary>Segundos sem golpe ate a musica de luta sair do ar.</summary>
	private const double SegundosDeLuta = 12;
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

		foreach (GameClient.ObraInfo o in cli.Obras)
			_atores.AddChild(new ObraDesenhada
			{
				Name = "Obra" + o.Id,
				Position = o.Pos,
				Tipo = o.Tipo,
				Dono = o.Dono,
				Aparafusada = o.Aparafusada,
				Lab = o.Lab,
			});
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
