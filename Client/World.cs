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

	private Camera2D? _camera;

	private const string Manifesto = "res://Assets/Maps/manifest.json";

	private Jandirus.Core.Appearance.VisualCatalog? _visual;
	// a aparencia de cada um chega UMA vez, e pode chegar ANTES do boneco existir
	private readonly Dictionary<int, (string Raca, string Genero, Jandirus.Core.Appearance.Appearance Ap)> _looks = [];
	private LocalPlayer? _local;
	private readonly Dictionary<int, RemotePlayer> _remotos = [];
	private readonly Dictionary<int, ulong> _ultimoPacoteMs = [];
	private CanvasModulate _ambiente = null!;
	private ZoneCatalog? _catalogo;
	private ZoneCollision? _colisao;
	private Node2D? _zonaAtual;
	private Node2D _atores = null!;

	public override void _Ready()
	{
		Instancia = this;
		const string dados = "res://Assets/Data/visual.json";
		if (Godot.FileAccess.FileExists(dados))
			_visual = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
		else GD.PushWarning("[world] sem visual.json -- rode o AssetPipeline (comando 'visual')");

		MontarCenario();

		_atores = new Node2D { Name = "Atores" };
		AddChild(_atores);

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

		// AMBIENTE: quanto mais escuro, mais a luz aparece. Branco puro = dia sem sombra.
		_ambiente = new CanvasModulate { Color = new Color("39406b") }; // crepusculo
		AddChild(_ambiente);
	}

	/// <summary>Textura de luz radial gerada em codigo: nao depende de arte importada.</summary>
	private static GradientTexture2D LuzRadial(int raio, Color cor)
	{
		var g = new Gradient();
		g.SetColor(0, cor);
		g.SetColor(1, new Color(cor.R, cor.G, cor.B, 0));
		return new GradientTexture2D
		{
			Gradient = g,
			Width = raio * 2,
			Height = raio * 2,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(1.0f, 0.5f),
		};
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
		_zonaAtual.ZIndex = -50;
		AddChild(_zonaAtual);
		MoveChild(_zonaAtual, 0); // atras dos atores

		// A MESMA colisao que o servidor usa. Sem ela o cliente atravessa parede, o servidor
		// recusa e devolve correcao -- e e ESSA briga que faz o personagem tremer no muro.
		_colisao = Godot.FileAccess.FileExists(e.Colisao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(e.Colisao))
			: null;
		if (_colisao == null) GD.PushWarning($"[world] zona '{zona}' sem colisao: da pra atravessar parede");
		if (_local != null) _local.Mapa = _colisao;

		GD.Print($"[world] zona carregada: {e.Zona} (z{e.Z}, {e.W}x{e.H})"
				 + (_colisao != null ? " com colisao" : " SEM colisao"));

		// o som do LUGAR: vento, mar, cidade. Troca junto com o planeta.
		AudioDirector.Instance?.Ambiente(Trilha.AmbienteDe(e.Zona));
		AudioDirector.Instance?.Musica(Trilha.MusicaDe(e.Zona), AudioDirector.Camada.Lugar);
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

		// a luz que o personagem carrega
		corpo.AddChild(new PointLight2D
		{
			Name = "Luz",
			Texture = LuzRadial(160, Colors.White),
			Energy = 1.1f,
			ShadowEnabled = true,
			Color = new Color("ffe9c4"),
		});

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
				r.AddChild(new HealthBar { Name = "Vida" });
				_atores.AddChild(r);
				_remotos[e.Id] = r;
				VestirSePuder(e.Id, r);
				GD.Print($"[world] entrou no meu campo de visao: id {e.Id}");
			}

			double desde = _ultimoPacoteMs.TryGetValue(e.Id, out ulong antes) ? (agora - antes) / 1000.0 : Protocol.TickSeconds;
			_ultimoPacoteMs[e.Id] = agora;
			r.Receive(e.Pos, (Facing)e.Facing, e.Moving, e.Pose, desde);
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
		switch (desfecho)
		{
			case Jandirus.Core.Combat.Desfecho.Acertou:
			case Jandirus.Core.Combat.Desfecho.Critico:
				Piscar(quemLeva, desfecho == Jandirus.Core.Combat.Desfecho.Critico
					? new Color(1.6f, 0.5f, 0.5f) : new Color(1.4f, 0.6f, 0.6f));
				// DOIS sons por golpe, como no original: o assobio sai de quem BATE e o baque
				// de quem APANHA. Separar os dois e o que da direcao ao impacto -- um som so,
				// no meio, soa como se ninguem tivesse acertado ninguem.
				Som(quemBate, Trilha.Assobio, 0.6f);
				Som(quemLeva ?? quemBate, Trilha.Acerto(h.Nivel));
				break;

			case Jandirus.Core.Combat.Desfecho.Aparou:
				Piscar(quemLeva, new Color(0.7f, 0.85f, 1.5f));
				Som(quemLeva ?? quemBate, Trilha.Aparou);
				break;

			case Jandirus.Core.Combat.Desfecho.Contra:
				// quem apanha e quem BATEU: o contra-ataque devolve o golpe
				Piscar(quemBate, new Color(0.7f, 0.85f, 1.5f));
				Som(quemLeva, Trilha.ContraAtaque);
				Som(quemBate, Trilha.Acerto(2));
				break;

			case Jandirus.Core.Combat.Desfecho.Esquivou:
			case Jandirus.Core.Combat.Desfecho.Errou:
				Som(quemBate, Trilha.SocoNoAr(), 0.7f);   // o corte do soco passando em falso
				break;
		}

		if (h.Decepou || h.Rabo) Som(quemLeva, Trilha.Decepou);
		if (h.Morreu || h.Nocauteou) Som(quemLeva, Trilha.Queda);
		if (h.Morreu || h.Nocauteou || h.Decepou) Piscar(quemLeva, new Color(1.8f, 0.35f, 0.35f), 0.4);

		// MUSICA DE LUTA: entra no primeiro golpe que me envolve e sai sozinha depois de um
		// tempo sem troca. A camada Combate cede pra transformacao e volta quando ela acaba.
		if (!souEu) return;
		// uma faixa DIFERENTE a cada briga -- sao 39 na pasta `battle ost`
		if (_lutaAte <= 0) AudioDirector.Instance?.Musica(Trilha.Combate(), AudioDirector.Camada.Combate);
		_lutaAte = SegundosDeLuta;

		if (h.TemDano && h.Membro.Length > 0)
			Hud.Instancia?.Narrar(h, GameClient.Instance!.LocalId);
	}

	public override void _Process(double delta)
	{
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

	private static void Piscar(Node2D? quem, Color cor, double segundos = 0.15)
		=> quem?.GetNodeOrNull<CharacterVisual>("Visual")?.Impacto(cor, segundos);

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
