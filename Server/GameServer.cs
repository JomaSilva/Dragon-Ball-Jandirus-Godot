using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>Um jogador conectado, do ponto de vista do servidor.</summary>
public sealed class ServerPlayer
{
	public int Id;
	/// <summary>
	/// A CONEXAO. NULA quando este 'jogador' e um NPC -- o clone da meditacao, e mais tarde
	/// os NPCs do mundo. Todo envio passa por `Peer?.Send`, entao um corpo sem dono existe no
	/// mundo, aparece no snapshot dos outros e apanha normalmente, so nao recebe nada.
	/// </summary>
	public NetPeer? Peer;
	public string Name = "";
	public ZoneKey Zone;
	public Vec2 Pos;
	public Facing Facing;
	public bool Moving;
	public float SpeedStat = 1f;
	public string Race = "";
	public string Class = "";
	public long LastInputMs;   // pra medir o dt real entre inputs (o cliente nao dita o tempo)
	public int Corrections;    // quantas vezes ja foi puxado de volta (sinal de cheat/lag cronico)

	/// <summary>
	/// A FICHA AUTORITATIVA. Vive so aqui: o cliente recebe os numeros prontos, nunca os
	/// ingredientes. Stat e BP sao a unica coisa que "cliente calcula" NAO cobre -- movimento
	/// da pra recalcular e conferir, poder nao.
	/// </summary>
	public Fighter Ficha = null!;

	/// <summary>
	/// O CORPO e o estado de luta. Vive so na memoria do servidor e morre com a sessao --
	/// guarda erguida, atordoamento e recarga de golpe nao sao patrimonio de personagem. O
	/// que persiste (vida dos membros) e gravado a parte no <see cref="CharacterSave"/>.
	/// </summary>
	public CombatState Combate = null!;

	/// <summary>Assinatura da ultima ficha lenta enviada -- ver MandarAtributos.</summary>
	public string SigAtributos = "";

	/// <summary>O que este personagem aprendeu, e quantos marcos tem pra gastar.</summary>
	public Jandirus.Core.Skills.SkillBook Livro = null!;

	/// <summary>O NIVEL de cada skill. Sobe sozinho enquanto a skill tem dono.</summary>
	public Jandirus.Core.Skills.NiveisDeSkill Niveis = new();

	/// <summary>
	/// KARMA. Sobe protegendo, desce matando -- e a porta moral dos cargos (a escola do Grou
	/// pede karma zero ou MENOS; o Senhor do Inferno, -50 ou pior).
	/// </summary>
	public int Karma;

	/// <summary>
	/// O CEREBRO. Nulo em jogador de verdade; preenchido no clone da meditacao (e depois nos
	/// NPCs do mundo). Ter cerebro E o que define um corpo como NPC.
	/// </summary>
	public Jandirus.Core.Ai.Cerebro? Cerebro;

	/// <summary>Em que chunk do espaco estou. Trocar de chunk dispara o pacote de vizinhanca.</summary>
	public ChunkId ChunkAtual;

	/// <summary>De onde decolei.</summary>
	public string PlanetaDeOrigem = "";

	/// <summary>Rate-limit do aviso de "planeta sem superficie".</summary>
	public long AvisoDePousoAte;

	/// <summary>De quem este clone e reflexo (0 = nao e clone).</summary>
	public int DonoDoClone;

	/// <summary>O clone que este jogador invocou (0 = nenhum), e de onde ele veio.</summary>
	public int CloneId;
	public ZoneKey ZonaDeOrigem;
	public Vec2 PosDeOrigem;

	/// <summary>Quando a regeneracao volta a ficar disponivel (relogio real, ms).</summary>
	public long RegenLivreEm;

	/// <summary>Em que forma esta e quanto domina de cada uma.</summary>
	public Jandirus.Core.Forms.EstadoDeForma Forma = new();
	public string SigSkills = "";

	/// <summary>Bits de <see cref="Protocol.Poder"/> que as skills aprendidas acenderam.</summary>
	public Protocol.Poder Poderes;

	/// <summary>Quando o corpo volta a se mexer depois de morrer (relogio real, ms).</summary>
	public long RenasceEm;

	/// <summary>
	/// DEBRUCADO NUMA RESEARCH STATION. Nao entra no <see cref="Protocol.Activity"/> junto de
	/// treinar e meditar de proposito: aqueles dois sao POSES que os outros veem, e a pose vai no
	/// snapshot pra todo mundo. Estudar so interessa a quem estuda.
	/// </summary>
	public bool Estudando;

	/// <summary>Esta correndo AGORA (concedido pelo servidor, nao afirmado pelo cliente).</summary>
	public bool Correndo;

	/// <summary>Quando o proximo dash de aproximacao libera (relogio real, ms).</summary>
	public long DashLivreEm;

	/// <summary>
	/// Ate quando uma correcao de movimento e ESPERADA (acabei de dar dash neste jogador).
	///
	/// O dash reposiciona o personagem no servidor, mas o cliente ja tinha pacotes em voo
	/// com a posicao antiga -- eles chegam e sao corrigidos, o que e certo. O que NAO pode e
	/// isso poluir o contador de correcoes: ele existe pra denunciar cheat e lag cronico, e
	/// um sinal que dispara sozinho toda vez que o jogo funciona nao denuncia nada.
	/// </summary>
	public long CorrecaoEsperadaAte;

	/// <summary>
	/// EM QUEM EU ESTOU MIRANDO (0 = ninguem). Marcado com duplo clique pelo jogador.
	///
	/// Um alvo marcado passa NA FRENTE do cone: e ele que leva o soco e e nele que a investida
	/// fecha, mesmo com outro mais perto e mesmo estando atras. Sem isso, brigar em grupo vira
	/// loteria -- o golpe vai em quem por acaso encostou primeiro.
	/// </summary>
	public int AlvoId;

	/// <summary>Quem me derrubou por ultimo -- e de quem o Zenkai cobra a conta.</summary>
	public int UltimoAgressor;

	/// <summary>Assinatura do ultimo corpo enviado -- so remanda quando algum membro muda.</summary>
	public string CorpoEnviado = "";

	/// <summary>Aparencia: so o servidor guarda a versao saneada.</summary>
	public Jandirus.Core.Appearance.Appearance Visual = new();
	public string Planeta = "Earth", Genero = "Male", Linhagem = "";
	public int Idade = 18;
	public long CriadoEm;

	/// <summary>A conta a que este personagem pertence, e em qual dos tres slots ele mora.</summary>
	public string Conta = "";
	public int Slot = -1;

	/// <summary>Ate quando a pose de soco fica no ar (relogio real, ms).</summary>
	public long AtaqueAte;

	/// <summary>
	/// A pose que os outros veem. Sai do ESTADO do servidor, nao de um pedido do cliente --
	/// senao daria pra aparecer meditando no meio de uma luta.
	/// </summary>
	public Protocol.Pose Pose(long agoraMs)
	{
		if (Ficha.dead || Ficha.KO) return Protocol.Pose.Nocauteado;
		if (agoraMs < AtaqueAte) return Protocol.Pose.Atacando;
		if (Ficha.train) return Protocol.Pose.Treinando;
		if (Ficha.med) return Protocol.Pose.Meditando;
		return Protocol.Pose.Normal;
	}

	/// <summary>O rabo ainda esta no corpo? Falso pra quem nunca teve.</summary>
	public bool TemRaboAgora() => Combate?.Corpo.Achar("Rabo") is { Decepado: false };

	public SheetState Sheet() => new()
	{
		Class = Class,
		BP = Ficha.BP,
		ExpressedBP = Ficha.expressedBP,
		Ki = Ficha.Ki,
		MaxKi = Ficha.MaxKi,
		HP = Ficha.HP,
		SpeedStat = SpeedStat,
		// a cadencia vai calculada: ela muda com o Ki carregado, e o cliente precisa dela
		// pro proprio cooldown e pra duracao da animacao de soco
		SocoMs = (int)Math.Round(CombatMath.Cadencia(Ficha) * 1000),
		MembrosRuins = (byte)Math.Min(255, Combate?.Corpo.Partes.Count(p => p.Decepado || p.Quebrado) ?? 0),
		Estado = (byte)((Ficha.KO ? 1 : 0) | (Ficha.dead ? 2 : 0)
						| (Combate?.Bloqueando == true ? 4 : 0) | (Combate?.Letal == true ? 8 : 0)
						| (TemRaboAgora() ? 16 : 0)),
	};
}

/// <summary>
/// SERVIDOR. Roda como autoload; so liga de verdade quando o processo sobe com --server
/// (o mesmo executavel serve de cliente e de servidor headless).
///
/// AUTORIDADE: o cliente calcula o proprio movimento e manda a posicao; o servidor CONFERE
/// com <see cref="MoveRules.ValidateStep"/> e devolve correcao quando o passo nao cabe no
/// tempo decorrido. O tempo usado na conta e o do SERVIDOR (relogio local entre pacotes),
/// nunca um dt vindo do cliente: dt e a variavel que todo cheat de velocidade infla.
///
/// INTERESSE: o snapshot de cada jogador so leva quem compartilha o mesmo
/// <see cref="ZoneKey.Hash"/>. E o que substitui o "mesmo z" do BYOND.
/// </summary>
public partial class GameServer : Node
{
	public static GameServer? Instance { get; private set; }
	public bool Running { get; private set; }

	private readonly NetManager _net;
	private readonly EventBasedNetListener _listener = new();
	private readonly Dictionary<int, ServerPlayer> _players = [];
	private readonly Dictionary<NetPeer, ServerPlayer> _byPeer = [];
	// quem ja entrou na CONTA mas ainda esta escolhendo personagem
	private readonly Dictionary<NetPeer, AccountSave> _logados = [];
	// a conta de quem ja esta em jogo -- e nela que o personagem e gravado
	private readonly Dictionary<NetPeer, AccountSave> _contas = [];
	private readonly Dictionary<ulong, List<ServerPlayer>> _zones = [];  // hash da zona -> quem esta la
	private int _nextId = 1;
	private double _accumulator;
	private int _tickCount;
	private bool _carregado;
	private ZoneCatalog? _catalogo;
	private RaceCatalog? _racas;
	private Jandirus.Core.Appearance.VisualCatalog? _visual;
	private AccountStore? _store;
	private readonly Random _rng = new();

	/// <summary>BP forcado por `--bpteste`. 0 = desligado (o normal).</summary>
	private double _bpDeTeste;
	private double _techDeTeste, _zeniDeTeste;
	private int _marcosDeTeste;
	private List<string> _skillsDeTeste = [];

	/// <summary>As fichas dos planetas (gravidade e tipo), lidas de `planetas.json`.</summary>
	private CatalogoDePlanetas? _planetas;

	/// <summary>
	/// AS SKILLS SUBINDO SOZINHAS. E o `effector()` do original: enquanto a skill tem dono, ela
	/// acumula exp, passa da barreira e sobe -- e cada degrau destrava alguma coisa.
	///
	/// A SUBIDA E ANUNCIADA COM O TEXTO DO DM quando ele existe. E o momento em que o jogo
	/// ensina: ninguem descobre sozinho que a Kicker no nivel 2 da um chute novo.
	/// </summary>
	private void TickDosNiveis()
	{
		if (_skills == null) return;
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Ficha.dead || pl.Livro == null) continue;

			List<Jandirus.Core.Skills.NiveisDeSkill.Subida> subiu =
				pl.Niveis.Efetor(_rng, _skills, pl.Livro);
			if (subiu.Count == 0) continue;

			pl.Niveis.Aplicar(pl.Ficha);
			pl.Ficha.Statify();
			pl.SigAtributos = "";

			foreach (Jandirus.Core.Skills.NiveisDeSkill.Subida s in subiu)
			{
				GD.Print($"[server] {pl.Name}: {s.Nome} chegou ao nivel {s.Nivel}");
				Avisar(pl, s.Degrau is { Aviso.Length: > 0 } d
					? $"{s.Nome} — {d.Aviso}"
					: $"{s.Nome} chega ao nível {s.Nivel}.");
			}
			HabilidadesMudaram(pl);
		}
	}

	/// <summary>Um degrau pode ter concedido verb novo: o menu do cliente precisa saber.</summary>
	private static void HabilidadesMudaram(ServerPlayer pl) => MandarSkills(pl, forcar: true);

	private void CarregarNiveis()
	{
		const string cj = "res://Assets/Data/niveis.json";
		if (!Godot.FileAccess.FileExists(cj))
		{
			GD.PushWarning("[server] sem niveis.json -- rode o AssetPipeline (comando 'effector')");
			return;
		}
		int n = Jandirus.Core.Skills.RegrasDoDisco.Carregar(Godot.FileAccess.GetFileAsString(cj));
		GD.Print($"[server] niveis de skill: {n} regras ({Jandirus.Core.Skills.RegrasDeNivel.Total} no total)");
	}

	private void CarregarPlanetas()
	{
		const string cj = "res://Assets/Data/planetas.json";
		if (!Godot.FileAccess.FileExists(cj))
		{
			GD.PushWarning("[server] sem planetas.json -- rode o AssetPipeline (comando 'planetas')");
			return;
		}
		_planetas = CatalogoDePlanetas.Parse(Godot.FileAccess.GetFileAsString(cj));
		GD.Print($"[server] planetas: {_planetas.Total} com gravidade propria");
	}

	/// <summary>
	/// POE O PESO DO CHAO na ficha. E o `Planetgrav` do original, e ele estava parado em 1 pra
	/// todo mundo desde o comeco do port -- treinar em Vegeta (10x) rendia igual a treinar na
	/// Terra, e nada na tela dizia isso. Um multiplicador que existe e nunca muda e pior que um
	/// que nao existe: da a impressao de estar valendo.
	///
	/// NO ESPACO A GRAVIDADE E ZERO (o `if("Space") Planetgrav=0` do DM) -- e por isso que
	/// ninguem treina viajando.
	/// </summary>
	private void AplicarGravidade(ServerPlayer pl)
	{
		if (_planetas == null) return;
		FichaDePlaneta f = _planetas.De(pl.Zone.Name);
		if (Math.Abs(pl.Ficha.Planetgrav - f.Gravidade) < 1e-9) return;

		pl.Ficha.Planetgrav = f.Gravidade;
		pl.Ficha.Statify();
		pl.SigAtributos = "";
		if (f.Gravidade > 1)
			Avisar(pl, $"o chão de {f.Nome} puxa {f.Gravidade:0.##} vezes mais forte. Cada passo custa, "
					   + "e cada treino rende.");
	}

	/// <summary>
	/// De quantos em quantos ticks a ficha e recalculada. O BYOND rodava o statify/powerlevel
	/// a cada ~0,3s; 6 ticks de 30 Hz da 5 Hz, mesma ordem de grandeza. Nao adianta rodar a
	/// 30 Hz: nada que entra na conta muda mais rapido que isso.
	/// </summary>
	private const int TicksPorFicha = 6;

	/// <summary>
	/// UM SEGUNDO EXATO, e nao "o mesmo tick da ficha". O dreno das tecnicas sustentadas e
	/// POR SEGUNDO no original (`sleep(10)` = 10 decimos); cobra-lo junto do tick de ficha, que
	/// e 5 Hz, faria a invisibilidade custar cinco vezes o preco e cair sozinha em segundos.
	/// Cadencia errada num dreno nao parece bug, parece balanceamento ruim.
	/// </summary>
	private const int TicksPorSegundo = 30;

	// zona inicial de todo mundo enquanto nao existe criacao de personagem
	private static readonly ZoneKey SpawnZone = ZoneKey.Premade("Earth");

	/// <summary>
	/// O MESMO ponto de nascimento do BYOND: `locate(rand(240,260), rand(240,260), 1)`, o
	/// campo aberto no meio da Terra. Em pixel isso e o centro do tile (249, 250) -- o canto
	/// (320, 320) que estava aqui era um lugar arbitrario de teste, longe de tudo.
	/// </summary>
	private static readonly Vec2 SpawnPos = new(249 * 32 + 16, 250 * 32 + 16);

	public GameServer()
	{
		_net = new NetManager(_listener)
		{
			AutoRecycle = true,
			UpdateTime = 15,
			ChannelsCount = 2,
		};
	}

	public override void _Ready()
	{
		Instance = this;
		SetProcess(false);   // dormente ate alguem mandar subir

		string[] args = OS.GetCmdlineArgs();

		// A BANCADA E LIDA ANTES DA GUARDA. O servidor tambem sobe DENTRO do cliente (o botao
		// "Hospedar" e o `--host`), e nesse caminho este _Ready sai na linha seguinte -- ler a
		// flag depois dela a deixava sem efeito justamente no modo em que se testa.
		int bpIdx = Array.IndexOf(args, "--bpteste");
		if (bpIdx >= 0 && bpIdx + 1 < args.Length && double.TryParse(args[bpIdx + 1],
				System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double bpT))
		{
			_bpDeTeste = bpT;
			GD.Print($"[server] BANCADA: todo personagem entra com BP {bpT:N0}");
		}

		// `--techteste`: entra com tecnologia e zeni. Existe pelo mesmo motivo do `--bpteste`: o
		// laboratorio de DNA pede 70 de tecnologia, que sao horas de estudo -- sem atalho nao ha
		// como um teste automatico chegar la, e o sistema inteiro ficaria sem prova.
		int tIdx = Array.IndexOf(args, "--techteste");
		if (tIdx >= 0)
		{
			_techDeTeste = 80;
			_zeniDeTeste = 5_000_000;
			if (tIdx + 1 < args.Length && double.TryParse(args[tIdx + 1],
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double tv)) _techDeTeste = tv;
			GD.Print($"[server] BANCADA: tecnologia {_techDeTeste:0} e {_zeniDeTeste:N0} zeni pra todo mundo");
		}

		// `--gestacaoteste N`: encurta a gestacao do bio-androide pra N segundos. Sem isto o teste
		// levaria DOZE HORAS -- e um sistema que so da pra testar em doze horas nao e testado.
		int gIdx = Array.IndexOf(args, "--gestacaoteste");
		if (gIdx >= 0 && gIdx + 1 < args.Length && double.TryParse(args[gIdx + 1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double gv))
		{
			_gestacaoDeTeste = gv;
			GD.Print($"[server] BANCADA: gestacao de bio-androide em {gv:0}s");
		}

		// `--marcosteste N`: entra com N marcos. A progressao profunda (destravar arvore por
		// `bodyskill`, chegar na Martial Arts, ganhar um estilo) custa uma dezena de compras --
		// com os tres marcos iniciais nao ha como um teste automatico alcancar nada disso.
		int mIdx = Array.IndexOf(args, "--marcosteste");
		if (mIdx >= 0 && mIdx + 1 < args.Length && int.TryParse(args[mIdx + 1], out int mv))
		{
			_marcosDeTeste = mv;
			GD.Print($"[server] BANCADA: {mv} marcos pra todo mundo");
		}

		// `--skillteste a,b,c`: concede as skills listadas (typepath) a quem entrar.
		// A cadeia de pre-requisitos de uma tecnica tem meia duzia de degraus; um teste automatico
		// que precise andar a cadeia inteira testa a PROGRESSAO, nao a tecnica. Isto separa as duas
		// perguntas: a progressao ja tem teste proprio, e aqui eu quero ver a tecnica DISPARAR.
		int sIdx = Array.IndexOf(args, "--skillteste");
		if (sIdx >= 0 && sIdx + 1 < args.Length)
		{
			_skillsDeTeste = [.. args[sIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries
													   | StringSplitOptions.TrimEntries)];
			GD.Print($"[server] BANCADA: concedendo {_skillsDeTeste.Count} skills");
		}

		if (Array.IndexOf(args, "--server") < 0) return;   // processo de cliente

		int port = Protocol.DefaultPort;
		int idx = Array.IndexOf(args, "--port");
		if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int p)) port = p;
		Start(port);
	}

	/// <summary>
	/// Sobe o servidor NESTE processo. Serve tanto pro servidor dedicado (`--server`) quanto
	/// pro jogador que quer hospedar a propria partida sem abrir outro programa -- e o mesmo
	/// codigo, so muda quem chama. Quem hospeda depois conecta em 127.0.0.1 como qualquer um.
	/// </summary>
	public bool Start(int port = Protocol.DefaultPort)
	{
		if (Running) return true;

		if (!_carregado)
		{
			CarregarZonas();
			CarregarRacas();
			CarregarVisual();
			Wire();
			_carregado = true;
		}

		Running = _net.Start(port);
		SetProcess(Running);
		GD.Print(Running
			? $"[server] escutando na porta {port} | tick {Protocol.TickHz} Hz"
			: $"[server] FALHOU ao abrir a porta {port} (ja tem alguem usando?)");
		return Running;
	}

	public void Stop()
	{
		if (!Running) return;
		_net.Stop();
		Running = false;
		SetProcess(false);
		_players.Clear();
		_byPeer.Clear();
		_zones.Clear();
		GD.Print("[server] parado");
	}

	/// <summary>
	/// Le o manifesto das zonas e a colisao de cada uma. Sao ~31 KB por andar, entao carregar
	/// TUDO no boot custa pouco e evita hitch quando alguem troca de planeta.
	/// </summary>
	private void CarregarZonas()
	{
		const string manifesto = "res://Assets/Maps/manifest.json";
		if (!Godot.FileAccess.FileExists(manifesto))
		{
			GD.PushWarning("[server] sem manifest.json: rode o Tools/AssetPipeline -- movimento so sera validado por VELOCIDADE");
			return;
		}

		_catalogo = ZoneCatalog.Parse(Godot.FileAccess.GetFileAsString(manifesto));
		int ok = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (!Godot.FileAccess.FileExists(e.Colisao)) continue;
			e.Mapa = ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(e.Colisao));
			if (e.Mapa != null) ok++;
		}
		GD.Print($"[server] zonas: {_catalogo.Todas.Count()} | com colisao: {ok}");
	}

	/// <summary>
	/// Os prototipos raciais extraidos do DM. Sem eles o servidor ainda aceita gente, mas
	/// todo mundo nasce com stat 1 -- entao a falta e ruidosa de proposito.
	/// </summary>
	private void CarregarRacas()
	{
		const string dados = "res://Assets/Data/races.json";
		if (!Godot.FileAccess.FileExists(dados))
		{
			GD.PushWarning("[server] sem races.json: rode o AssetPipeline (comando 'races') -- todo mundo nascera generico");
			return;
		}
		_racas = RaceCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
		GD.Print($"[server] racas: {_racas.Count} protos");
	}

	/// <summary>
	/// O catalogo de aparencia e a pasta de saves. O catalogo e o MESMO arquivo que o cliente
	/// usa na tela de criacao -- e o que torna "escolha so o que existe" uma regra de verdade
	/// e nao uma gentileza do cliente.
	/// </summary>
	private void CarregarVisual()
	{
		const string dados = "res://Assets/Data/visual.json";
		if (Godot.FileAccess.FileExists(dados))
		{
			_visual = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
			GD.Print($"[server] aparencia: {_visual.Cabelos.Count} cabelos, {_visual.Roupas.Count} roupas");
		}
		else GD.PushWarning("[server] sem visual.json: rode o AssetPipeline (comando 'visual')");

		// os saves ficam FORA do res:// (que e so leitura numa build exportada)
		string pasta = ProjectSettings.GlobalizePath("user://saves");
		_store = new AccountStore(pasta);
		CarregarSkills();
		CarregarTech();
		CarregarEstilos();
		CarregarPlanetas();
		CarregarNiveis();

		// OS QUATRO LOTES SE ANUNCIAM. Cada um vive no proprio arquivo e registra as tecnicas dele
		// -- portar o proximo lote nao mexe nesta lista, so acrescenta uma linha.
		RegistrarTecnicasG1();
		RegistrarTecnicasG2();
		RegistrarTecnicasG3();
		RegistrarTecnicasG4();
		CarregarCargos();
		Directory.CreateDirectory(pasta);
		GD.Print($"[server] contas: {_store.Quantas()} em {pasta}");
	}

	private void Wire()
	{
		_listener.ConnectionRequestEvent += req => req.AcceptIfKey(Protocol.ConnectionKey);
		_listener.PeerConnectedEvent += peer => GD.Print($"[server] conexao de {peer.Address}");
		_listener.PeerDisconnectedEvent += (peer, info) => Drop(peer);
		_listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
		{
			try { Handle(peer, reader); }
			catch (Exception ex) { GD.PushWarning($"[server] pacote invalido de {peer.Address}: {ex.Message}"); }
		};
	}

	public override void _Process(double delta)
	{
		if (!Running) return;
		_net.PollEvents();

		_accumulator += delta;
		while (_accumulator >= Protocol.TickSeconds)
		{
			_accumulator -= Protocol.TickSeconds;
			Tick();
		}
	}

	public override void _ExitTree()
	{
		if (Running) _net.Stop();
		Instance = null;
	}

	// ---------------------------------------------------------------------
	// recepcao
	// ---------------------------------------------------------------------
	private void Handle(NetPeer peer, NetPacketReader reader)
	{
		var id = (Protocol.C2S)reader.GetByte();
		switch (id)
		{
			case Protocol.C2S.Login: Login(peer, reader.GetString(24), reader.GetString(64)); break;
			case Protocol.C2S.PickSlot: PickSlot(peer, reader.GetByte()); break;
			case Protocol.C2S.CreateChar:
				CreateChar(peer, reader.GetByte(), reader.GetDraft(), reader.GetAppearance()); break;
			case Protocol.C2S.InputState: Input(peer, reader); break;
			case Protocol.C2S.Activity:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				var q = (Protocol.Activity)reader.GetByte();
				a.Ficha.train = q == Protocol.Activity.Treinando;
				a.Ficha.med = q == Protocol.Activity.Meditando;
				GD.Print($"[server] {a.Name}: {q} (BP {a.Ficha.BP:0.0})");
				break;
			}
			case Protocol.C2S.Action:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				Atacar(a, (Protocol.Golpe)reader.GetByte());
				break;
			}
			case Protocol.C2S.Guard:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				a.Combate.Guardar(reader.GetBool());
				break;
			}
			case Protocol.C2S.Aim:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				string z = Protocol.ZonaDe(reader.GetByte());
				a.Combate.ZonaMirada = z.Length > 0 ? z : null;
				break;
			}
			case Protocol.C2S.Lethal:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				a.Combate.Letal = reader.GetBool();
				GD.Print($"[server] {a.Name}: golpe {(a.Combate.Letal ? "LETAL" : "nao-letal")}");
				break;
			}
			// TECNOLOGIA pelo mesmo padrao do cargo: comando + argumento num canal so. Ver
			// `GameServer.Tech.cs` -- sao nove comandos e nenhum merece um id de protocolo.
			case Protocol.C2S.Estilo:
			{
				string qual = reader.GetString(32);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemLuta)) TrocarEstilo(quemLuta, qual);
				break;
			}

			case Protocol.C2S.Tech:
			{
				string cmd = reader.GetString(24);
				string arg = reader.GetString(48);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemConstroi)) ComandoDeTech(quemConstroi, cmd, arg);
				break;
			}

			case Protocol.C2S.Cargo:
			{
				string chave = reader.GetString(32);
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? quemQuer)) break;
				if (chave.Length == 0) MandarCargos(quemQuer);
				else { ReivindicarCargo(quemQuer, chave); MandarCargos(quemQuer); }
				break;
			}
			case Protocol.C2S.Habilidade:
			{
				string hab = reader.GetString(48);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemUsa)) UsarHabilidade(quemUsa, hab);
				break;
			}
			case Protocol.C2S.Transformar:
			{
				bool subir = reader.GetBool();
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemTransforma)) Transformar(quemTransforma, subir);
				break;
			}
			case Protocol.C2S.Aprender:
			{
				string path = reader.GetString(96);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemAprende)) Aprender(quemAprende, path);
				break;
			}
			case Protocol.C2S.Alvo:
			{
				int alvo = reader.GetInt();
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? quem)) break;
				// so vale mirar em quem existe e esta na MESMA zona -- o resto e cliente inventando
				quem.AlvoId = alvo != 0 && _players.TryGetValue(alvo, out ServerPlayer? o)
							  && o != quem && o.Zone.Hash == quem.Zone.Hash ? alvo : 0;
				break;
			}
			case Protocol.C2S.Chat:
			{
				var canal = (Protocol.Fala)reader.GetByte();
				string texto = reader.GetString(Protocol.MaxFala);
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				Falar(a, canal, texto);
				break;
			}
			case Protocol.C2S.Ping:
			{
				var w = Protocol.Begin(Protocol.S2C.Pong);
				w.Put(reader.GetLong());
				peer.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
				break;
			}
		}
	}

	// =====================================================================
	// PASSO 1: ENTRAR NA CONTA
	// =====================================================================
	/// <summary>
	/// Login por SERVIDOR: o par conta+senha e a identidade inteira aqui dentro. Conta que
	/// nao existe e CRIADA na hora -- e o comportamento de "primeiro acesso" que o jogador
	/// espera, e nao ha nada a proteger numa conta que ninguem usou ainda.
	/// </summary>
	private void Login(NetPeer peer, string conta, string senha)
	{
		if (_byPeer.ContainsKey(peer) || _logados.ContainsKey(peer)) return;

		conta = conta.Trim();
		if (conta.Length < 2) { Recusar(peer, "escolha um nome de conta com pelo menos 2 letras"); return; }
		if (senha.Length < 3) { Recusar(peer, "escolha uma senha de pelo menos 3 caracteres"); return; }
		if (_store == null) { Recusar(peer, "servidor sem armazenamento"); return; }

		// a mesma conta em duas telas brigaria pelo arquivo
		if (_logados.Values.Any(a => string.Equals(a.Conta, conta, StringComparison.OrdinalIgnoreCase))
			|| _players.Values.Any(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)))
		{
			Recusar(peer, "essa conta ja esta conectada");
			return;
		}

		AccountSave? acc = _store.Carregar(conta);
		if (acc == null)
		{
			(string sal, string hash) = AccountStore.Cadastrar(senha);
			acc = new AccountSave { Conta = conta, Sal = sal, Hash = hash, CriadaEm = NowMs() };
			_store.Gravar(acc);
			GD.Print($"[server] conta NOVA: {conta}");
		}
		else if (!AccountStore.Confere(acc, senha))
		{
			Recusar(peer, "senha incorreta");
			GD.Print($"[server] senha errada na conta '{conta}' de {peer.Address}");
			return;
		}

		acc.VistoEm = NowMs();
		_logados[peer] = acc;
		MandarSlots(peer, acc);
		GD.Print($"[server] conta '{conta}' entrou | slots ocupados: {acc.Slots.Count(x => x != null)}/{AccountStore.Slots}");
	}

	/// <summary>A tela de selecao inteira num pacote: os tres slots com o que ela mostra.</summary>
	private static void MandarSlots(NetPeer peer, AccountSave acc)
	{
		var w = Protocol.Begin(Protocol.S2C.SlotList);
		w.Put((byte)AccountStore.Slots);
		for (int i = 0; i < AccountStore.Slots; i++)
		{
			CharacterSave? c = acc.Slots[i];
			new SlotInfo
			{
				Ocupado = c != null,
				Nome = c?.Nome ?? "", Raca = c?.Raca ?? "", Classe = c?.Ficha.Class ?? "",
				Genero = c?.Genero ?? "Male", Idade = c?.Idade ?? 0, BP = c?.Ficha.BP ?? 0,
				Visual = c?.Visual ?? new Jandirus.Core.Appearance.Appearance(),
			}.Write(w);
		}
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// PASSO 2: ESCOLHER OU CRIAR O PERSONAGEM
	// =====================================================================
	private void PickSlot(NetPeer peer, int slot)
	{
		if (!_logados.TryGetValue(peer, out AccountSave? acc)) { Recusar(peer, "faca login primeiro"); return; }
		if (slot < 0 || slot >= AccountStore.Slots) { Recusar(peer, "slot invalido"); return; }

		CharacterSave? c = acc.Slots[slot];
		if (c == null) { Recusar(peer, "esse slot esta vazio"); return; }

		Entrar(peer, acc, slot, c);
	}

	private void CreateChar(NetPeer peer, int slot, CharacterDraft ficha,
						   Jandirus.Core.Appearance.Appearance visual)
	{
		if (!_logados.TryGetValue(peer, out AccountSave? acc)) { Recusar(peer, "faca login primeiro"); return; }
		if (slot < 0 || slot >= AccountStore.Slots) { Recusar(peer, "slot invalido"); return; }
		if (acc.Slots[slot] != null) { Recusar(peer, "esse slot ja tem um personagem"); return; }

		string motivo = ValidarFicha(ficha);
		if (motivo.Length > 0) { Recusar(peer, motivo); GD.Print($"[server] ficha recusada: {motivo}"); return; }

		string nome = ficha.Name.Trim();
		if (_players.Values.Any(o => string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase)))
		{
			Recusar(peer, "ja tem alguem em jogo com esse nome");
			return;
		}

		Fighter lutador = Nascer(ficha, nome);

		// APARENCIA: saneada, nunca recusada. Ela nao da vantagem nenhuma, entao um indice
		// fora da faixa vira o padrao em vez de derrubar a conexao. O que NAO se aceita e
		// caminho de sprite fora do catalogo -- isso e o cliente inventando arquivo.
		string ajuste = _visual?.Sanear(visual, ficha.Race, ficha.Gender) ?? "";
		if (ajuste.Length > 0) GD.Print($"[server] aparencia de {nome} ajustada: {ajuste}");

		var novo = new CharacterSave
		{
			Nome = nome, Raca = ficha.Race, Planeta = ficha.Planet, Genero = ficha.Gender,
			Linhagem = ficha.ChosenClass, Idade = ficha.Age, Visual = visual, Ficha = lutador,
			Zona = SpawnZone.Name, X = SpawnPos.X, Y = SpawnPos.Y, CriadoEm = NowMs(),
		};
		acc.Slots[slot] = novo;
		_store?.Gravar(acc);
		GD.Print($"[server] {acc.Conta} criou '{nome}' no slot {slot + 1} | {novo.Raca}/{lutador.Class}");

		Entrar(peer, acc, slot, novo);
	}

	// =====================================================================
	// PASSO 3: PISAR NO MUNDO
	// =====================================================================
	private void Entrar(NetPeer peer, AccountSave acc, int slot, CharacterSave c)
	{
		var pl = new ServerPlayer { Id = _nextId++, Peer = peer, LastInputMs = NowMs() };
		AccountStore.ParaJogador(c, pl);
		pl.Conta = acc.Conta;
		pl.Slot = slot;
		pl.Zone = ZoneKey.Premade(c.Zona);
		if (pl.Pos.X == 0 && pl.Pos.Y == 0) pl.Pos = SpawnPos;
		pl.Facing = Facing.South;
		pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed);
		PrepararCombate(pl, c);
		PrepararSkills(pl, c);
		pl.Niveis = new Jandirus.Core.Skills.NiveisDeSkill();
		pl.Niveis.DoSave(c?.Niveis);
		pl.Niveis.Aplicar(pl.Ficha);   // o nivel veio do disco; o que ele soma, nao
		AplicarPoderes(pl);
		AplicarEfeitos(pl);

		// BANCADA: `--bpteste N` da BP a quem entrar. Existe porque a escada de transformacao
		// comeca em 1,5 MILHAO de BP base e um personagem novo nasce com 9 -- sem isto nao ha
		// como exercitar transformacao num teste automatico. So vale em servidor de teste.
		if (_bpDeTeste > 0) { pl.Ficha.BP = _bpDeTeste; pl.Ficha.Statify(); }
		if (_techDeTeste > 0) pl.Ficha.techskill = Math.Max(pl.Ficha.techskill, _techDeTeste);
		if (_zeniDeTeste > 0) pl.Ficha.Zeni = Math.Max(pl.Ficha.Zeni, _zeniDeTeste);
		if (_marcosDeTeste > 0 && pl.Livro.MarcosLivres < _marcosDeTeste) pl.Livro.Conceder(_marcosDeTeste);
		foreach (string sk in _skillsDeTeste) pl.Livro.Dar(sk);

		// A FORMA NAO ATRAVESSA O LOGOUT: quem sai SSJ3 volta na base. O que persiste e a
		// MAESTRIA (semanas de jogo) e quais formas ja despertaram (a cinematica so roda uma vez).
		pl.Forma = new Jandirus.Core.Forms.EstadoDeForma();
		pl.Forma.Maestria.DoSave(c?.Maestrias);
		if (c?.FormasDespertadas is { Count: > 0 }) pl.Forma.JaDespertou = [.. c.FormasDespertadas];
		AplicarForma(pl);

		_logados.Remove(peer);
		_contas[peer] = acc;
		_players[pl.Id] = pl;

		// SO AGORA da pra mandar as construcoes: `MandarObras` varre `_players` procurando quem
		// esta na zona, e quem acabou de entrar so esta la depois desta linha.
		MandarObras(pl.Zone);
		MandarCatalogoDeObras(pl);
		AplicarEstilo(pl);   // o estilo veio do save; os multiplicadores nao
		MandarEstilos(pl);
		AplicarGravidade(pl);
		_byPeer[peer] = pl;
		ZoneList(pl.Zone.Hash).Add(pl);
		Persistir(pl);

		var w = Protocol.Begin(Protocol.S2C.JoinAccepted);
		w.Put(pl.Id);
		w.PutZone(pl.Zone);
		w.PutVec(pl.Pos);
		w.Put(pl.Name);
		pl.Sheet().Write(w);
		w.PutAppearance(pl.Visual);
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		TrocarAparencias(pl);

		GD.Print($"[server] {pl.Name} entrou (id {pl.Id}) em {pl.Zone} | {pl.Race}/{pl.Class} " +
				 $"| BP {pl.Ficha.BP:0.0} (expresso {pl.Ficha.expressedBP:0})");
	}

	/// <summary>
	/// A ficha e DADO DO CLIENTE. Raca fora da lista do planeta, ou linhagem escolhida numa
	/// raca que nao escolhe, seria personagem forjado.
	/// </summary>
	private static string ValidarFicha(CharacterDraft ficha)
	{
		string motivo = ficha.Validar();
		if (motivo.Length == 0 && Array.IndexOf(CharacterDraft.RacasDoPlaneta(ficha.Planet), ficha.Race) < 0)
			motivo = "raca nao pertence a esse planeta";
		if (motivo.Length == 0 && ficha.ChosenClass.Length > 0
			&& Array.IndexOf(CharacterDraft.EscolhasDeClasse(ficha.Race), ficha.ChosenClass) < 0)
			motivo = "essa raca nao escolhe linhagem";
		return motivo;
	}

	/// <summary>
	/// Apresenta o recem-chegado a quem ja estava na zona, e vice-versa.
	///
	/// A aparencia vai UMA VEZ por pessoa, num pacote proprio -- nao no snapshot. Uma ficha
	/// de aparencia tem nome de estilo e ate quatro caminhos de roupa; mandar isso 30x por
	/// segundo por jogador seria gastar toda a banda pra repetir o que nao muda.
	/// </summary>
	private void TrocarAparencias(ServerPlayer novo)
	{
		List<ServerPlayer> zona = ZoneList(novo.Zone.Hash);

		NetDataWriter Ficha(ServerPlayer p)
		{
			var w = Protocol.Begin(Protocol.S2C.PeerLook);
			w.Put(p.Id);
			w.Put(p.Name);
			w.Put(p.Race);
			w.Put(p.Genero);
			w.PutAppearance(p.Visual);
			return w;
		}

		NetDataWriter meu = Ficha(novo);
		foreach (ServerPlayer outro in zona)
		{
			outro.Peer?.Send(meu, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (outro != novo)
				novo.Peer?.Send(Ficha(outro), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	private static void Recusar(NetPeer peer, string motivo)
	{
		var neg = Protocol.Begin(Protocol.S2C.JoinRejected);
		neg.Put(motivo);
		peer.Send(neg, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Grava o personagem. Chamado ao entrar, de tempos em tempos e ao sair.</summary>
	private void Persistir(ServerPlayer pl)
	{
		if (_store == null || pl.Slot < 0) return;
		if (pl.Peer == null || !_contas.TryGetValue(pl.Peer, out AccountSave? acc)) return;
		pl.Ficha.Class = pl.Class;
		acc.Slots[pl.Slot] = AccountStore.DeJogador(pl, NowMs());
		acc.VistoEm = NowMs();
		_store.Gravar(acc);
	}

	/// <summary>
	/// NASCIMENTO. O SERVIDOR sorteia a classe e monta a ficha -- a tela de criacao so
	/// escolheu identidade e, nas tres racas que escolhem, a linhagem. Sorteio no cliente
	/// seria sorteio ate sair o resultado desejado.
	/// </summary>
	private Fighter Nascer(CharacterDraft ficha, string nome)
	{
		if (_racas == null)
			return new Fighter { Name = nome, Race = ficha.Race, BP = 1 };

		return Birth.Nascer(_racas, ficha.Race, ficha.ChosenClass, _rng, nome);
	}

	private void Input(NetPeer peer, NetPacketReader reader)
	{
		if (!_byPeer.TryGetValue(peer, out ServerPlayer? pl)) return;

		Vec2 claimed = reader.GetVec();
		byte flags = reader.GetByte();
		var facing = (Facing)(flags & 0x03);
		bool moving = (flags & Protocol.InputAndando) != 0;
		bool querCorrer = (flags & Protocol.InputCorrendo) != 0;

		// QUEM ESTA NO CHAO NAO ANDA. A checagem tem que vir antes da validacao de passo:
		// senao o corpo caido "anda" livremente enquanto a pose diz que ele esta desmaiado.
		if (pl.Ficha.dead || pl.Ficha.KO)
		{
			pl.LastInputMs = NowMs();
			pl.Moving = false;
			var parado = Protocol.Begin(Protocol.S2C.Correction);
			parado.PutVec(pl.Pos);
			peer.Send(parado, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			return;
		}

		// O DT E DO SERVIDOR. Se o cliente pudesse dizer quanto tempo passou, "passou 1
		// minuto" viraria teleporte com validacao aprovando.
		long now = NowMs();
		float dt = MathF.Max(0f, (now - pl.LastInputMs) / 1000f);
		pl.LastInputMs = now;

		// CORRER E CONCEDIDO, NAO DECLARADO. O cliente PEDE; quem decide e este metodo, e ele
		// COBRA por segundo de corrida. Sem isto, o bit de "correndo" seria 60% de velocidade
		// gratuita pra qualquer cliente modificado -- e o tipo de coisa que nao da pra
		// detectar depois, porque o movimento fica dentro do que a validacao aceita.
		bool correndo = querCorrer && moving && PodeCorrer(pl, dt);

		ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;
		if (MoveRules.ValidateStep(pl.Pos, claimed, dt, pl.SpeedStat, mapa, out Vec2 ok, correndo))
		{
			pl.Pos = ok;
		}
		else
		{
			pl.Pos = ok;
			bool esperada = now < pl.CorrecaoEsperadaAte;
			if (!esperada) pl.Corrections++;
			// Correcao em jogo HONESTO nao deveria existir: as duas pontas rodam a MESMA regra
			// de colisao e de velocidade. Se este aviso aparecer, o cliente esta sendo puxado de
			// volta -- e e exatamente isso que o jogador sente como o personagem tremendo.
			// (A do dash e a excecao: o servidor moveu o personagem de proposito.)
			if (!esperada && pl.Corrections % 30 == 1)
				GD.PushWarning($"[server] {pl.Name}: {pl.Corrections} correcoes de movimento (dt={dt:0.000}s)");

			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.PutVec(pl.Pos);
			peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		pl.Facing = facing;
		pl.Moving = moving;
		pl.Correndo = correndo;
		pl.Ficha.dashing = correndo;   // entra na conta de dano: +2 pra quem bate, x1.25 pra quem apanha
	}

	/// <summary>
	/// Correr custa Ki por segundo. Sem energia, o corpo simplesmente nao corre -- e o mesmo
	/// desenho do original, onde cada arranque de dash descontava Ki.
	/// </summary>
	private static bool PodeCorrer(ServerPlayer pl, float dt)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) return false;

		double custo = pl.Ficha.MaxKi * CustoCorridaPorSegundo * Math.Min(dt, 0.25f);
		if (pl.Ficha.Ki < custo) return false;

		pl.Ficha.Ki -= custo;
		return true;
	}

	/// <summary>Fracao do Ki maximo gasta por segundo de corrida.</summary>
	private const double CustoCorridaPorSegundo = 0.02;

	private void Drop(NetPeer peer)
	{
		_logados.Remove(peer);   // caiu na tela de selecao: nao ha nada a salvar
		if (!_byPeer.Remove(peer, out ServerPlayer? pl)) { _contas.Remove(peer); return; }
		Persistir(pl);
		_contas.Remove(peer);   // ANTES de soltar: sair do jogo nao pode custar o progresso
		_players.Remove(pl.Id);
		ZoneList(pl.Zone.Hash).Remove(pl);

		var w = Protocol.Begin(Protocol.S2C.PeerLeft);
		w.Put(pl.Id);
		foreach (ServerPlayer other in ZoneList(pl.Zone.Hash))
			other.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		GD.Print($"[server] {pl.Name} saiu");
	}

	// ---------------------------------------------------------------------
	// tick: um snapshot por ZONA (nao por jogador) e o corte de interesse
	// ---------------------------------------------------------------------
	/// <summary>De quantos ticks em quantos o progresso vai pro disco (30 Hz x 3600 = 2 min).</summary>
	private const int TicksPorSave = 30 * 120;

	private void Tick()
	{
		// O combate anda no tick CHEIO (30 Hz): recarga de golpe e atordoamento sao contados
		// em fracao de segundo, e a ficha so roda a 5 Hz.
		TickCombate(Protocol.TickSeconds);

		// A FORMA COBRA NO TICK CHEIO. O dreno de Ki e por segundo e derruba quem ficou sem
		// folego -- cobrar so a 5 Hz deixaria a forma sobreviver por fracoes de segundo alem
		// do Ki zerado, e e justamente nesse instante que a luta costuma virar.
		foreach (ServerPlayer pl in _players.Values) TickDaForma(pl, Protocol.TickSeconds);

		// os NPCs pensam e agem no tick cheio, pelas mesmas funcoes do jogador
		TickDosClones(Protocol.TickSeconds);

		// no espaco: troca de chunk e pouso por encostar
		foreach (ServerPlayer pl in _players.Values.ToList()) TickDoEspaco(pl);

		// O TIQUE DAS SKILLS anda junto do da ficha, e nao por acaso: `NiveisDeSkill` foi
		// calibrado em 0,2 s por tique (5 Hz) e `TicksPorFicha` da exatamente isso a 30 Hz.
		// Cadencia errada aqui nao quebra nada -- so faz a skill subir mais rapido ou mais
		// devagar do que o original, calada.
		if (++_tickCount % TicksPorFicha == 0) { TickFichas(); TickDosNiveis(); }
		if (_tickCount % TicksPorSegundo == 0)
			{ TickDasTecnicas(); TickDoEstudo(); TickDaGestacao(); TickDosEstilos(); TickDosBuffs(); TickTecnicasG2(); }

		// SALVAMENTO PERIODICO: sem isto, uma queda do servidor custa tudo desde o login.
		// Dois minutos e o maximo de treino que alguem pode perder.
		if (_tickCount % TicksPorSave == 0)
			foreach (ServerPlayer pl in _players.Values) Persistir(pl);

		long agora = NowMs();
		foreach (List<ServerPlayer> zona in _zones.Values)
		{
			if (zona.Count == 0) continue;

			// O ESPACO E UMA ZONA SO, e por isso o corte la nao pode ser a zona: todo mundo
			// esta nela. Quem decide o trafego e a CHUNK -- e como o recorte muda de jogador
			// pra jogador, o buffer nao da pra compartilhar e cada um recebe o seu.
			if (Espaco.EhEspaco(zona[0].Zone)) { SnapshotDoEspaco(zona, agora); continue; }

			var w = Protocol.Begin(Protocol.S2C.Snapshot);
			w.Put((ushort)zona.Count);
			foreach (ServerPlayer pl in zona)
				new EntityState
				{
					Id = pl.Id, Pos = pl.Pos, Facing = (byte)pl.Facing,
					Moving = pl.Moving, Pose = pl.Pose(agora),
					Vida = (byte)Math.Clamp(Math.Round(pl.Ficha.HP), 0, 100),
					Rabo = pl.TemRaboAgora(),
					Oculto = EstaOculto(pl.Id),
				}.Write(w);

			// mesmo buffer pra todos daquela zona: quem esta noutro planeta nao recebe nada
			foreach (ServerPlayer pl in zona)
				pl.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Sequenced);
		}
	}

	/// <summary>
	/// Recalcula a ficha de cada jogador e manda de volta o que MUDOU. O pacote so sai quando
	/// algum numero mexeu -- num servidor parado isso e zero trafego.
	/// </summary>
	private void TickFichas()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			double antes = pl.Ficha.expressedBP;
			double antesKi = pl.Ficha.Ki;
			double antesHp = pl.Ficha.HP;
			double antesAct = pl.Ficha.Eactspeed;   // muda a cadencia do soco, que vai na ficha

			Treinar(pl.Ficha);
			pl.Ficha.Tick(agoraMs: NowMs());
			pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed);
			GainKnobs.TopBP = Math.Max(GainKnobs.TopBP, pl.Ficha.BP);

			if (antes == pl.Ficha.expressedBP && antesKi == pl.Ficha.Ki && antesHp == pl.Ficha.HP
				&& antesAct == pl.Ficha.Eactspeed) continue;

			var w = Protocol.Begin(Protocol.S2C.Stats);
			pl.Sheet().Write(w);
			pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	/// <summary>
	/// O ganho de BP da vez. Roda no SERVIDOR e so aqui -- o cliente declara o que esta
	/// fazendo, mas quem soma poder e este metodo. A gravidade entra sempre: ela nao treina
	/// sozinha, ela MULTIPLICA quem ja esta treinando.
	/// </summary>
	private void Treinar(Fighter f)
	{
		if (f.train) f.TrainGain(_rng, 6.0 / (1 + Math.Log(2)));
		else if (f.med) f.MedGain(_rng);
		else { f.BufferTick(); return; }   // parado: so acumula pro proximo treino

		f.GravGain();
	}

	/// <summary>Move um jogador de zona e avisa as duas pontas (usado pela troca de planeta).</summary>
	public void MoveToZone(int playerId, ZoneKey destino, Vec2 spawn)
	{
		if (!_players.TryGetValue(playerId, out ServerPlayer? pl)) return;

		ZoneList(pl.Zone.Hash).Remove(pl);
		pl.Zone = destino;
		pl.Pos = spawn;
		ZoneList(destino.Hash).Add(pl);

		var w = Protocol.Begin(Protocol.S2C.ZoneChanged);
		w.PutZone(destino);
		w.PutVec(spawn);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		// AS CONSTRUCOES SAO POR ZONA: sem reenviar, quem muda de planeta continua vendo as
		// construcoes do planeta anterior desenhadas no chao do novo.
		MandarObras(destino);
		pl.Estudando = false;   // ninguem estuda de outro planeta
		AplicarGravidade(pl);   // o chao mudou: o peso dele tambem
	}

	private List<ServerPlayer> ZoneList(ulong hash)
	{
		if (!_zones.TryGetValue(hash, out List<ServerPlayer>? l))
		{
			l = [];
			_zones[hash] = l;
		}
		return l;
	}

	private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
