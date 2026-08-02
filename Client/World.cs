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

		if (GameClient.Instance is { } cli)
		{
			cli.Joined += AoEntrar;
			cli.SnapshotReceived += AoReceberSnapshot;
			cli.PeerLeft += AoSair;
			cli.PeerLooked += AoReceberAparencia;
			cli.Golpe += AoGolpe;
			// O Boot instancia o World DENTRO do callback de Joined, ou seja, este _Ready
			// roda DEPOIS do evento. Assinar nao basta: se ja entramos, aplica agora.
			if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.Zone, cli.LocalSpawn, cli.LocalName);

			cli.ZoneChanged += (z, spawn) =>
			{
				CarregarZona(z);
				if (_local != null) _local.Position = new Vector2(spawn.X, spawn.Y);
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
		}
	}

	private void AoSair(int id)
	{
		if (_remotos.Remove(id, out RemotePlayer? r)) r.QueueFree();
		_looks.Remove(id);
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
		if (_visual == null) return;
		if (GameClient.Instance != null && id == GameClient.Instance.LocalId)
			_local?.GetNode<CharacterVisual>("Visual").Vestir(_visual, ap, raca, genero);
		else if (_remotos.TryGetValue(id, out RemotePlayer? r))
			r.GetNode<CharacterVisual>("Visual").Vestir(_visual, ap, raca, genero);
	}

	/// <summary>Veste um boneco recem-criado com a aparencia que ja tiver chegado.</summary>
	private void VestirSePuder(int id, Node corpo)
	{
		if (_visual == null || !_looks.TryGetValue(id, out var l)) return;
		corpo.GetNode<CharacterVisual>("Visual").Vestir(_visual, l.Ap, l.Raca, l.Genero);
	}
}
