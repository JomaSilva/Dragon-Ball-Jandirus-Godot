using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// CLIENTE DE REDE. Autoload: mantem a conexao viva entre trocas de cena.
/// Nao desenha nada e nao decide nada de jogo: recebe, traduz e emite eventos C#
/// pra cena consumir.
/// </summary>
public partial class GameClient : Node
{
	public static GameClient? Instance { get; private set; }

	public bool Connected => _peer is { ConnectionState: ConnectionState.Connected };
	public int LocalId { get; private set; }
	public ZoneKey Zone { get; private set; }
	// guardados porque o World nasce DEPOIS do Joined: sem isto ele perde o evento e o
	// jogador local nunca e criado (so os remotos, que chegam por snapshot)
	public Vec2 LocalSpawn { get; private set; }
	public string LocalName { get; private set; } = "";
	public int Ping => _peer?.Ping ?? 0;

	/// <summary>A aparencia que o servidor ACEITOU (pode ter sido ajustada).</summary>
	public Jandirus.Core.Appearance.Appearance Visual { get; private set; } = new();

	/// <summary>A ficha que o SERVIDOR calculou. O cliente so exibe -- nunca deriva daqui.</summary>
	public SheetState Sheet { get; private set; }
	public event Action<SheetState>? SheetUpdated;

	public event Action<int, ZoneKey, Vec2, string>? Joined;      // id, zona, spawn, nome
	public event Action<List<EntityState>>? SnapshotReceived;
	public event Action<Vec2>? Corrected;                          // servidor recusou meu passo
	public event Action<int>? PeerLeft;
	public event Action<ZoneKey, Vec2>? ZoneChanged;
	public event Action<string>? Rejected;
	/// <summary>Os tres slots da conta -- e a tela de selecao.</summary>
	public event Action<List<SlotInfo>>? SlotsRecebidos;
	/// <summary>Um golpe foi resolvido pelo servidor. Vale pra som, piscada e musica de luta.</summary>
	public event Action<Protocol.HitEvent>? Golpe;

	/// <summary>O estado de cada membro do MEU corpo. So chega quando muda.</summary>
	public event Action<List<Protocol.ParteState>>? CorpoAtualizado;

	/// <summary>O ultimo corpo recebido -- pra quem nasce depois do pacote (o HUD).</summary>
	public List<Protocol.ParteState> Corpo { get; private set; } = [];

	/// <summary>
	/// A FICHA LENTA: atributos e o que o personagem sabe fazer. Chega quando muda, nao por
	/// tick -- e o que decide quais abas o menu (tecla P) mostra.
	/// </summary>
	public Protocol.AtributosState Atributos { get; private set; }
	public event Action<Protocol.AtributosState>? AtributosRecebidos;
	/// <summary>A aparencia de alguem da zona: id, nome, raca, genero, ficha visual.</summary>
	public event Action<int, string, string, string, Jandirus.Core.Appearance.Appearance>? PeerLooked;

	private readonly NetManager _net;
	private readonly EventBasedNetListener _listener = new();
	private NetPeer? _peer;
	private string _conta = "", _senha = "";
	private readonly List<EntityState> _scratch = [];

	public GameClient()
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
		_listener.PeerConnectedEvent += _ =>
		{
			var w = Protocol.Begin(Protocol.C2S.Login);
			w.Put(_conta);
			w.Put(_senha);
			_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		};
		_listener.NetworkReceiveEvent += (peer, reader, ch, method) =>
		{
			try { Handle(reader); }
			catch (Exception ex) { GD.PushWarning($"[client] pacote invalido: {ex.Message}"); }
		};
		_listener.PeerDisconnectedEvent += (peer, info) =>
		{
			GD.Print($"[client] desconectado: {info.Reason}");
			_peer = null;
		};
		_net.Start();
	}

	public override void _Process(double delta) => _net.PollEvents();

	public override void _ExitTree()
	{
		_net.Stop();
		Instance = null;
	}

	/// <summary>
	/// Conecta e faz LOGIN NA CONTA. O personagem vem depois: quem sabe quais slots existem
	/// e o servidor, entao a tela de selecao (e a de criacao) so fazem sentido apos isto.
	/// </summary>
	public void Conectar(string host, int port, string conta, string senha)
	{
		_conta = conta;
		_senha = senha;
		_peer = _net.Connect(host, port, Protocol.ConnectionKey);
		GD.Print($"[client] conectando em {host}:{port} como conta \"{conta}\"");
	}

	/// <summary>"Quero jogar com o personagem do slot N."</summary>
	public void PedirSlot(int slot)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.PickSlot);
		w.Put((byte)slot);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>"O slot N esta vazio: cria este personagem nele."</summary>
	public void CriarPersonagem(int slot, CharacterDraft ficha, Jandirus.Core.Appearance.Appearance visual)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.CreateChar);
		w.Put((byte)slot);
		w.PutDraft(ficha);
		w.PutAppearance(visual);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	public void Desconectar()
	{
		_peer?.Disconnect();
		_peer = null;
	}

	/// <summary>Manda a posicao que EU calculei. O servidor confere e corrige se nao couber.</summary>
	/// <summary>
	/// A SEQUENCIA DO INPUT. Sobe a cada pacote e nunca volta.
	///
	/// Ela existe por um motivo so: deixar o servidor saber se um pacote foi montado ANTES ou
	/// DEPOIS de ele ter teleportado o personagem. Sem esse carimbo os dois estados sao
	/// indistinguiveis -- e o servidor tratava "posicao velha porque o pacote e velho" como
	/// "posicao velha porque o cliente esta errado", e puxava o jogador pra tras.
	/// </summary>
	private uint _seq;

	/// <summary>A ultima sequencia que o servidor confirmou ter processado (vem na correcao).</summary>
	public uint SeqConfirmada { get; private set; }

	public void SendState(Vec2 pos, Facing facing, bool moving, bool correndo = false)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.InputState);
		w.Put(++_seq);
		w.PutVec(pos);
		w.Put((byte)(((byte)facing & 0x03)
					 | (correndo ? Protocol.InputCorrendo : 0)
					 | (moving ? Protocol.InputAndando : 0)));
		_peer!.Send(w, Protocol.ChannelState, DeliveryMethod.Sequenced);
	}

	/// <summary>Declara o que estou fazendo. O servidor e quem decide o que isso vale em BP.</summary>
	public void SendActivity(Protocol.Activity a)
	{
		if (!Connected) return;
		Atividade = a;
		var w = Protocol.Begin(Protocol.C2S.Activity);
		w.Put((byte)a);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		ActivityChanged?.Invoke(a);
	}

	public Protocol.Activity Atividade { get; private set; }
	public event Action<Protocol.Activity>? ActivityChanged;

	/// <summary>
	/// GOLPEAR. So um PEDIDO: quem escolhe o alvo, rola a pontaria e calcula o dano e o
	/// servidor -- este cliente nem sabe o BP de quem esta na frente. A animacao roda na hora
	/// pra o controle nao ter atraso, mas ela nao promete acerto nenhum.
	/// </summary>
	public void SendAction(Protocol.Golpe golpe = Protocol.Golpe.Leve)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Action);
		w.Put((byte)golpe);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Guarda erguida ou baixada.</summary>
	public void SendGuard(bool erguida)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Guard);
		w.Put(erguida);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Zona do corpo que estou mirando (indice em <see cref="Protocol.Zonas"/>).</summary>
	public void SendAim(byte zona)
	{
		if (!Connected || zona == ZonaMirada) return;   // clicar duas vezes na mesma regiao nao e evento
		ZonaMirada = zona;
		var w = Protocol.Begin(Protocol.C2S.Aim);
		w.Put(zona);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		MiraMudou?.Invoke(zona);
	}

	/// <summary>O `murderToggle`: lutar pra valer (arranca membro e mata) ou nao.</summary>
	public void SendLethal(bool letal)
	{
		if (!Connected) return;
		Letal = letal;
		var w = Protocol.Begin(Protocol.C2S.Lethal);
		w.Put(letal);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		LetalidadeMudou?.Invoke(letal);
	}

	/// <summary>
	/// Falar. Quem ouve e decidido no SERVIDOR (ver GameServer.Chat.cs) -- daqui sai o canal e
	/// o texto, e nada mais.
	/// </summary>
	public void SendChat(Protocol.Fala canal, string texto)
	{
		if (!Connected || texto.Length == 0) return;
		var w = Protocol.Begin(Protocol.C2S.Chat);
		w.Put((byte)canal);
		w.Put(texto.Length > Protocol.MaxFala ? texto[..Protocol.MaxFala] : texto);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Alguem falou e eu estou no alcance: canal, quem falou, o que foi dito.</summary>
	public event Action<Protocol.Fala, string, string>? Falou;

	/// <summary>
	/// O QUE EU APRENDI e quantos marcos tenho. Chega quando muda, como o corpo e os atributos.
	/// </summary>
	public HashSet<string> SkillsAprendidas { get; } = new(StringComparer.OrdinalIgnoreCase);
	public int MarcosTotais { get; private set; }
	public int MarcosLivres { get; private set; }
	public event Action? SkillsMudaram;

	/// <summary>Os efeitos ligados em mim agora ("invisivel", "escudo"). So pra HUD.</summary>
	public HashSet<string> EfeitosAtivos { get; } = new(StringComparer.Ordinal);

	/// <summary>Caiu um efeito: id e por quantos ms (0 = saiu, negativo = enquanto durar).</summary>
	public event Action<string, long>? EfeitoCaiu;

	/// <summary>Uma construcao de pe na minha zona.</summary>
	/// <summary>
	/// Uma construcao de pe. `Arte`/`Estado`/`Pixel` vem DO SERVIDOR e nao do catalogo local: o
	/// catalogo do cliente so tem o que ELE pode comprar, e a bancada de outra pessoa tem que
	/// aparecer do mesmo jeito.
	/// </summary>
	public readonly record struct ObraInfo(int Id, string Tipo, Vector2 Pos, bool Aparafusada, int Lab, string Dono,
										   string Arte, string Estado, Vector2 Pixel, bool Densa);

	/// <summary>Uma linha do catalogo de tecnologia, com o motivo do nao (0 = pode).</summary>
	public readonly record struct OfertaDeObra(string Id, string Nome, double Custo, double Tech, int Recusa);

	/// <summary>Um estilo de luta que eu sei, com a maestria e o teto dela.</summary>
	public readonly record struct EstiloInfo(string Id, string Nome, double Maestria, double Teto);

	public List<EstiloInfo> Estilos { get; private set; } = [];
	public string EstiloAtual { get; private set; } = "";
	public event Action? EstilosMudaram;

	/// <summary>Assume (ou solta, com "-") uma postura de luta.</summary>
	public void SendEstilo(string id)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Estilo);
		w.Put(id);
		_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	public List<ObraInfo> Obras { get; private set; } = [];
	public List<OfertaDeObra> Catalogo { get; private set; } = [];
	public double TechNivel { get; private set; }
	public double Zeni { get; private set; }
	public double TechXp { get; private set; }
	public double TechXpAlvo { get; private set; } = 100;
	public event Action? ObrasMudaram;
	public event Action? TechMudou;

	/// <summary>
	/// PORTAS: `completo` (esta e a lista inteira da zona) + as celulas que mudaram.
	///
	/// A lista nao fica guardada aqui, ao contrario das obras: o estado de porta e do MAPA -- ele
	/// vive no `ZoneCollision` e nos nodes que o `World` criou. Guardar uma segunda copia so criaria
	/// a chance de as duas discordarem.
	/// </summary>
	public event Action<bool, List<(int X, int Y, bool Aberta)>>? PortasMudaram;

	/// <summary>
	/// AS FERIDAS DE CADA CORPO DA ZONA, guardadas por id.
	///
	/// GUARDADAS, e nao so emitidas: o pacote chega quando o servidor quer, e o boneco de quem ele
	/// descreve pode nem existir ainda -- quem CRIA o `RemotePlayer` e o snapshot. E a mesma dupla
	/// do `PeerLook`, que ja resolveu isto: guarda sempre, aplica se o boneco existir, e o
	/// nascimento do boneco consulta o que estava guardado.
	/// </summary>
	public readonly Dictionary<int, Jandirus.Core.Combat.MascaraDeFeridas> Feridas = [];

	/// <summary>Chegou (ou mudou) a mascara de alguem: id.</summary>
	public event Action<int>? FeridasMudaram;

	/// <summary>Uma celula do cenario caiu (knockback contra parede): virou chao.</summary>
	public event Action<int, int>? CenarioCaiu;

	/// <summary>
	/// UM ADMIN REFEZ O CENARIO DESTA ZONA: esqueca todo o estrago.
	///
	/// Evento separado do <see cref="CenarioCaiu"/> porque a acao e de outra natureza. Uma celula
	/// que cai e uma alteracao pontual, e o cliente sabe fazer (apaga as camadas, escreve terra
	/// batida). Desfazer, nao: ele nao guardou o que havia ali antes. A unica volta possivel e
	/// recarregar a zona do disco -- e e isso que o `World` faz ao ouvir isto.
	/// </summary>
	public event Action<ulong>? CenarioRefeito;

	/// <summary>
	/// TUDO QUE JA CAIU nesta zona, guardado.
	///
	/// ============================ POR QUE PRECISA SER GUARDADO ============================
	/// O servidor manda a lista do estrago (`MandarCenario`) no MESMO instante em que aceita o
	/// jogador -- e nessa hora o `World` ainda nao existe, entao ninguem esta ouvindo o evento. O
	/// resultado e um mapa DIFERENTE em cada tela: quem estava presente viu a parede cair, quem
	/// chegou depois continua vendo a parede de pe. E como a parede tambem e colisao, os dois
	/// passam a discordar sobre onde da pra andar -- que e o desync de posicao que o dono
	/// fotografou.
	///
	/// E EXATAMENTE O MESMO DEFEITO que as construcoes tinham (`DesenharObras` so rodava no
	/// evento). A cura e a mesma: guardar aqui e reaplicar quando a zona carrega.
	/// =====================================================================================
	/// </summary>
	public readonly List<(int X, int Y)> CenarioCaido = [];

	/// <summary>Zera o estrago guardado. Devolve `true` pra caber no `when` do `switch`.</summary>
	private bool LimparCenario() { CenarioCaido.Clear(); return true; }

	/// <summary>O canal unico de tecnologia. Ver `GameServer.Tech.cs`.</summary>
	/// <summary>
	/// O CANAL DOS VERBS: comando + argumento. Mesmo formato do <see cref="SendTech"/>, e pelo
	/// mesmo motivo -- sao dezenas de acoes soltas, e um opcode por acao encheria o protocolo.
	/// Quem autoriza (admin, por exemplo) e o SERVIDOR.
	/// </summary>
	public void SendVerbo(string cmd, string arg = "")
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Verbo);
		w.Put(cmd);
		w.Put(arg);
		_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	public void SendTech(string cmd, string arg = "")
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Tech);
		w.Put(cmd);
		w.Put(arg);
		_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>"Quero comprar esta skill." Quem valida e o servidor -- daqui so sai o pedido.</summary>
	public void SendAprender(string path)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Aprender);
		w.Put(path);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// Usa uma habilidade ativa, por id. Um canal so pra todas (ver GameServer.Raciais.cs):
	/// tecnica nova nao precisa de pacote novo.
	/// </summary>
	public void SendHabilidade(string id)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Habilidade);
		w.Put(id);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Um planeta no mapa do universo, do jeito que o cliente precisa desenha-lo.</summary>
	public readonly record struct PlanetaInfo(string Nome, Vector2 Pos, float Raio, ulong Seed, bool Premade);

	/// <summary>Os planetas da minha vizinhanca no espaco. Chega quando a CHUNK muda.</summary>
	public List<PlanetaInfo> Planetas { get; private set; } = [];
	public ulong SeedDoUniverso { get; private set; }
	public event Action? VizinhancaMudou;

	/// <summary>Um cargo do mundo: chave, quem ocupa ("" = vago), e o que falta PRA MIM.</summary>
	public readonly record struct CargoInfo(string Chave, string Dono, string Falta);

	public List<CargoInfo> Cargos { get; private set; } = [];
	public event Action? CargosMudaram;

	/// <summary>Chave vazia = so me manda a lista; com chave = reivindico aquele cargo.</summary>
	public void SendCargo(string chave = "")
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Cargo);
		w.Put(chave);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// Uma conta do servidor, como o painel de admin a desenha. Sem senha: o servidor nunca manda
	/// sal nem hash (ver <see cref="Protocol.S2C.Contas"/>).
	/// </summary>
	public readonly record struct ContaInfo(string Conta, bool Admin, bool Banida, bool Online, string Personagens);

	public List<ContaInfo> Contas { get; private set; } = [];
	public event Action? ContasMudaram;

	/// <summary>Alguem mudou de forma: quem, de que forma, pra qual, e se foi a PRIMEIRA vez.</summary>
	public event Action<int, int, int, bool>? FormaMudou;

	/// <summary>
	/// QUE HORAS SAO NO UNIVERSO, em segundos. Quem manda e o servidor (`S2C.Ceu`), e entre um
	/// pacote e outro isto anda sozinho no `_Process` do <see cref="Iluminacao"/> -- nao pra
	/// inventar a hora, mas pra a luz nao andar aos saltos a cada correcao de quinze segundos.
	///
	/// Vale ZERO ate o primeiro pacote chegar, e quem le trata isso: desenhar o mundo na hora
	/// errada por um quadro e pior do que esperar um.
	/// </summary>
	public double TempoDoMundo { get; private set; }
	public bool TempoChegou { get; private set; }
	public event Action<double>? HoraDoMundo;

	/// <summary>
	/// O CLIMA FORCADO da minha zona -- o unico pedaco do ceu que vem do servidor.
	///
	/// O clima NATURAL nao chega por pacote nenhum: ele e funcao pura da ficha do planeta mais o
	/// <see cref="TempoDoMundo"/>, que ja esta sincronizado. Ver `S2C.Clima`.
	/// </summary>
	public Jandirus.Core.World.ClimaForcado ClimaForcado { get; private set; }
	public event Action? ClimaMudou;

	/// <summary>
	/// CAIU UM RAIO em (x, y) do mundo, com esta semente de desenho.
	///
	/// Vem do servidor, e nao do sorteio de cada cliente, pra que a mesma descarga aconteca no
	/// mesmo lugar e com a mesma forma pra todo mundo da zona. Ver `S2C.Raio`.
	/// </summary>
	public event Action<Vec2, float>? RaioCaiu;

	/// <summary>
	/// Sobe (ou desce) a escada de transformacao. O cliente NAO escolhe a forma -- pede a
	/// direcao e o servidor decide qual degrau cabe, como a tecla C do original.
	/// </summary>
	public void SendTransformar(bool subir)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Transformar);
		w.Put(subir);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// Segurei ou soltei o C: reunir energia.
	///
	/// SO NA BORDA -- o cliente manda quando o estado MUDA, nunca por quadro. Carregar dura
	/// segundos e um pacote a 60 Hz durante um power-up de dez segundos seriam 600 pacotes pra
	/// dizer duas coisas. Quem conta o tempo e o tick do servidor, que ja roda de qualquer jeito.
	///
	/// CONFIAVEL E ORDENADO porque perder o "soltei" deixaria o personagem carregando pra sempre
	/// -- um estado que so o proximo aperto de tecla desfaria.
	/// </summary>
	/// <summary>
	/// "Quero piscar pra este ponto." So um PEDIDO: o servidor confere skill, Ki, alcance,
	/// recarga e parede -- se o cliente decidisse, a tecnica seria teleporte livre.
	/// </summary>
	public void SendZanzoken(Vec2 destino)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Zanzoken);
		w.PutVec(destino);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Alguem piscou: o id e DE ONDE ele saiu. A miragem nasce na origem.</summary>
	public event Action<int, Vec2>? Piscou;

	// =====================================================================
	// O ZANZO CLASH
	// =====================================================================
	/// <summary>
	/// ESTOU NUM EMBATE? Enquanto for `true` o corpo nao anda: quem manda nele e o servidor.
	///
	/// A METADE DO CLIENTE E OBRIGATORIA. O servidor ja recusa o passo e devolve correcao, mas se
	/// so ele soubesse, o corpo tremeria -- trinta pedidos de passo por segundo, trinta correcoes
	/// de volta. E o mesmo par de travas da tecla C (ver <see cref="LocalPlayer"/>).
	///
	/// NAO E BIT DE FICHA de proposito: o byte de estado do <see cref="SheetState"/> esta CHEIO
	/// (os dois ultimos bits sao a direcao da queda). O `Comecou`/`Acabou` chega por canal
	/// confiavel e ordenado, que da a mesma garantia.
	/// </summary>
	public bool EmClash { get; private set; }

	/// <summary>
	/// Comecou: eu, o outro, quantos ms dura, e QUANTO VALE cada acerto de cada um.
	///
	/// A vantagem de poder viaja porque o jogador precisa dela pra ler a propria situacao: num
	/// encontro desigual o mais fraco pode acertar todas as letras e ainda perder, e descobrir isso
	/// so no fim seria o quick time event mentindo sobre o que estava sendo disputado.
	/// </summary>
	public event Action<int, int, int, float, float>? ClashComecou;

	/// <summary>Aperte ESTA letra, dentro deste prazo (ms).</summary>
	public event Action<char, int>? ClashTeclaPedida;

	/// <summary>O placar, do meu ponto de vista: meus pontos, os dele.</summary>
	public event Action<float, float>? ClashPlacar;

	/// <summary>Os dois se cruzaram AQUI. E o unico sinal visivel do embate.</summary>
	public event Action<Vec2>? ClashBaque;

	/// <summary>Acabou: quem venceu, quem perdeu.</summary>
	public event Action<int, int>? ClashAcabou;

	/// <summary>
	/// UM VISLUMBRE: apareci (true) ou sumi de novo (false).
	///
	/// So o corpo LOCAL precisa disto -- os outros aparecem sozinhos, pelo bit `Oculto` do snapshot.
	/// </summary>
	public event Action<bool>? ClashVislumbre;

	/// <summary>
	/// A tecla CRUA que o jogador apertou. Quem julga se ela e a pedida e o SERVIDOR -- daqui sai
	/// so o que foi digitado, senao um cliente mexido acertaria todas.
	/// </summary>
	public void SendClashTecla(char c)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.ClashTecla);
		w.Put((byte)c);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	public void SendCarregar(bool ligado)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Carregar);
		w.Put(ligado);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Em quem estou mirando (0 = ninguem). Marcado com duplo clique.</summary>
	public int AlvoId { get; private set; }
	public event Action<int>? AlvoMudou;

	/// <summary>
	/// Marca (ou solta, com 0) o alvo. O servidor confere se aquele id existe e esta na mesma
	/// zona -- daqui so sai a intencao.
	/// </summary>
	public void SendAlvo(int id)
	{
		if (!Connected || id == AlvoId) return;
		AlvoId = id;
		var w = Protocol.Begin(Protocol.C2S.Alvo);
		w.Put(id);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		AlvoMudou?.Invoke(id);
	}

	public byte ZonaMirada { get; private set; }
	public bool Letal { get; private set; }
	public event Action<byte>? MiraMudou;
	public event Action<bool>? LetalidadeMudou;

	private void Handle(NetPacketReader reader)
	{
		var id = (Protocol.S2C)reader.GetByte();
		switch (id)
		{
			case Protocol.S2C.JoinAccepted:
			{
				LocalId = reader.GetInt();
				Zone = reader.GetZone();
				Vec2 spawn = reader.GetVec();
				string nome = reader.GetString();
				LocalSpawn = spawn;
				LocalName = nome;
				Sheet = SheetState.Read(reader);
				Visual = reader.GetAppearance();   // o servidor devolve a versao SANEADA
				// A seed do universo vem JUNTO: a carta estelar precisa dela em terra firme, e nao
				// so depois de decolar. Ver `MapaEstelar`.
				SeedDoUniverso = reader.GetULong();
				// SEM CLASSE E SEM BP. Isto e o console DO JOGADOR (o .log dele), nao o do servidor:
				// imprimir a classe aqui e contar no arquivo o que a regra esconde na tela.
				GD.Print($"[client] entrei como id {LocalId} em {Zone.Name} @ ({spawn.X:0}, {spawn.Y:0})");
				Joined?.Invoke(LocalId, Zone, spawn, nome);
				SheetUpdated?.Invoke(Sheet);
				break;
			}
			case Protocol.S2C.JoinRejected:
			{
				string motivo = reader.GetString(120);
				GD.Print($"[client] RECUSADO: {motivo}");
				Rejected?.Invoke(motivo);
				break;
			}

			case Protocol.S2C.SlotList:
			{
				int n = reader.GetByte();
				var slots = new List<SlotInfo>(n);
				for (int i = 0; i < n; i++) slots.Add(SlotInfo.Read(reader));
				GD.Print($"[client] conta aceita | {slots.Count(x => x.Ocupado)} de {n} slots ocupados");
				SlotsRecebidos?.Invoke(slots);
				break;
			}

			case Protocol.S2C.PeerLook:
			{
				int quem = reader.GetInt();
				string nome = reader.GetString(24);
				string raca = reader.GetString(24);
				string genero = reader.GetString(8);
				PeerLooked?.Invoke(quem, nome, raca, genero, reader.GetAppearance());
				break;
			}

			case Protocol.S2C.Stats:
			{
				double antes = Sheet.BP;
				Sheet = SheetState.Read(reader);
				// so registra saltos de 10%: o pacote chega varias vezes por segundo e um log
				// por pacote afogaria o console
				if (antes > 0 && Sheet.BP >= antes * 1.1)
				{
					GD.Print($"[client] BP {antes:0} -> {Sheet.BP:0}");
					// O SALTO SE SENTE, NAO SE LE. O numero cru aqui furava o sigilo inteiro por um
					// caminho que ninguem olhava -- o chat. O DM ja da o molde: o que o corpo percebe e
					// a PROPORCAO do salto, nunca o valor.
					Chat.Sistema(antes > 0 && Sheet.BP / antes >= 1.5
						? "algo se rompe por dentro: seu poder deu um salto que voce nao esperava."
						: "voce se sente mais forte do que ontem.");
				}
				SheetUpdated?.Invoke(Sheet);
				break;
			}

			case Protocol.S2C.Snapshot:
			{
				int n = reader.GetUShort();
				_scratch.Clear();
				for (int i = 0; i < n; i++) _scratch.Add(EntityState.Read(reader));
				SnapshotReceived?.Invoke(_scratch);
				break;
			}
			case Protocol.S2C.Hit:
				Golpe?.Invoke(Protocol.HitEvent.Read(reader));
				break;

			case Protocol.S2C.Chat:
			{
				var canal = (Protocol.Fala)reader.GetByte();
				string autor = reader.GetString(32);
				Falou?.Invoke(canal, autor, reader.GetString(Protocol.MaxFala));
				break;
			}

			// UM EFEITO CAIU (ou saiu de cima) DE MIM. Um canal so pra todas as tecnicas: id do
			// efeito + por quantos ms (0 = acabou, negativo = enquanto durar). Ver
			// `GameServer.Tecnicas.cs`; a alternativa era um pacote por tecnica, e sao 47.
			case Protocol.S2C.Zanzo:
			{
				int quem = reader.GetInt();
				Piscou?.Invoke(quem, reader.GetVec());
				break;
			}

			// O ZANZO CLASH, os cinco momentos num opcode so. Ver `Protocol.ClashSub`.
			case Protocol.S2C.Clash:
			{
				switch ((Protocol.ClashSub)reader.GetByte())
				{
					case Protocol.ClashSub.Comecou:
					{
						int a = reader.GetInt(), b = reader.GetInt(), ms = reader.GetInt();
						float meu = reader.GetFloat(), dele = reader.GetFloat();
						// SO CHEGA A QUEM ESTA NO EMBATE: e um pacote pessoal, e o `a` sou eu.
						EmClash = true;
						ClashComecou?.Invoke(a, b, ms, meu, dele);
						break;
					}
					case Protocol.ClashSub.Tecla:
					{
						char c = (char)reader.GetByte();
						ClashTeclaPedida?.Invoke(c, reader.GetInt());
						break;
					}
					case Protocol.ClashSub.Placar:
						ClashPlacar?.Invoke(reader.GetFloat(), reader.GetFloat());
						break;
					case Protocol.ClashSub.Baque:
						ClashBaque?.Invoke(reader.GetVec());
						break;
					case Protocol.ClashSub.Vislumbre:
						ClashVislumbre?.Invoke(reader.GetBool());
						break;

					case Protocol.ClashSub.Acabou:
					{
						int venc = reader.GetInt(), perd = reader.GetInt();
						EmClash = false;
						ClashAcabou?.Invoke(venc, perd);
						break;
					}
				}
				break;
			}

			case Protocol.S2C.Efeito:
			{
				string efeito = reader.GetString(24);
				long ms = reader.GetLong();
				if (ms == 0) EfeitosAtivos.Remove(efeito); else EfeitosAtivos.Add(efeito);
				EfeitoCaiu?.Invoke(efeito, ms);
				break;
			}

			// AS CONSTRUCOES da minha zona. Chega no login, ao trocar de zona e quando alguem
			// ergue ou derruba alguma -- nunca por tick: predio nao se move.
			// OS ESTILOS: qual esta ativo, quais eu aprendi e a maestria de cada um. So quando muda.
			case Protocol.S2C.Estilos:
			{
				EstiloAtual = reader.GetString(32);
				int n = reader.GetByte();
				var l = new List<EstiloInfo>(n);
				for (int i = 0; i < n; i++)
					l.Add(new EstiloInfo(reader.GetString(32), reader.GetString(32),
						reader.GetDouble(), reader.GetDouble()));
				Estilos = l;
				EstilosMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Construcoes:
			{
				int n = reader.GetUShort();
				var l = new List<ObraInfo>(n);
				for (int i = 0; i < n; i++)
					l.Add(new ObraInfo(reader.GetInt(), reader.GetString(48),
						new Vector2(reader.GetFloat(), reader.GetFloat()),
						reader.GetBool(), reader.GetByte(), reader.GetString(32),
						reader.GetString(160), reader.GetString(48),
						new Vector2(reader.GetFloat(), reader.GetFloat()), reader.GetBool()));
				Obras = l;
				ObrasMudaram?.Invoke();
				break;
			}

			// AS PORTAS mudaram de estado -- ou, com `completo`, esta e a lista inteira da zona.
			// Nao vai por evento agregado como as obras: o `World` e o unico interessado e ele
			// precisa saber SE foi a lista completa (pra fechar tudo antes de aplicar).
			// UMA CELULA DO CENARIO CAIU. O corpo arremessado derrubou a parede.
			// Com `limpar`, e o contrario: um admin refez tudo (ver `MandarLimpezaDeCenario`).
			case Protocol.S2C.Cenario:
			{
				bool limpar = reader.GetBool();
				int dcx = reader.GetUShort(), dcy = reader.GetUShort();
				if (limpar)
				{
					// O EVENTO SAI COM A LISTA AINDA CHEIA, de proposito: quem ouve precisa saber
					// QUAIS celulas refechar na colisao antes de a lista sumir. E leva a ZONA, porque
					// esta mensagem vai pra todo mundo -- inclusive pra quem esta noutro planeta com
					// a cena suja guardada no cache.
					ulong zonaLimpa = reader.GetULong();
					CenarioRefeito?.Invoke(zonaLimpa);
					if (zonaLimpa == Zone.Hash) CenarioCaido.Clear();
					break;
				}
				CenarioCaido.Add((dcx, dcy));
				CenarioCaiu?.Invoke(dcx, dcy);
				break;
			}

			case Protocol.S2C.Porta:
			{
				(bool completo, List<(int X, int Y, bool Aberta)> portas) = reader.GetPortas();
				PortasMudaram?.Invoke(completo, portas);
				break;
			}

			case Protocol.S2C.Tech:
			{
				TechNivel = reader.GetDouble();
				Zeni = reader.GetDouble();
				TechXp = reader.GetDouble();
				TechXpAlvo = reader.GetDouble();
				int n = reader.GetUShort();
				var l = new List<OfertaDeObra>(n);
				for (int i = 0; i < n; i++)
					l.Add(new OfertaDeObra(reader.GetString(48), reader.GetString(48),
						reader.GetDouble(), reader.GetDouble(), reader.GetByte()));
				Catalogo = l;
				TechMudou?.Invoke();
				break;
			}

			case Protocol.S2C.Ceu:
			{
				TempoDoMundo = reader.GetDouble();
				TempoChegou = true;
				HoraDoMundo?.Invoke(TempoDoMundo);
				break;
			}

			case Protocol.S2C.Clima:
			{
				ClimaForcado = new Jandirus.Core.World.ClimaForcado
				{
					Tipo = (Jandirus.Core.World.TipoDeClima)reader.GetByte(),
					Ate = reader.GetDouble(),
					Duracao = reader.GetDouble(),
					Forca = reader.GetFloat(),
				};
				ClimaMudou?.Invoke();
				break;
			}

			case Protocol.S2C.Raio:
			{
				Vec2 onde = reader.GetVec();
				RaioCaiu?.Invoke(onde, reader.GetFloat());
				break;
			}

			case Protocol.S2C.Vizinhanca:
			{
				SeedDoUniverso = reader.GetULong();
				reader.GetInt(); reader.GetInt();          // a chunk: o cliente deriva da posicao
				int n = reader.GetByte();
				var l = new List<PlanetaInfo>(n);
				for (int i = 0; i < n; i++)
					l.Add(new PlanetaInfo(reader.GetString(48),
						new Vector2(reader.GetFloat(), reader.GetFloat()),
						reader.GetFloat(), reader.GetULong(), reader.GetBool()));
				Planetas = l;
				VizinhancaMudou?.Invoke();
				break;
			}

			case Protocol.S2C.Cargos:
			{
				int n = reader.GetByte();
				var lista = new List<CargoInfo>(n);
				for (int i = 0; i < n; i++)
					lista.Add(new CargoInfo(reader.GetString(32), reader.GetString(32), reader.GetString(160)));
				Cargos = lista;
				CargosMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Feridas:
			{
				int quem = reader.GetInt();
				Feridas[quem] = reader.GetFeridas();
				FeridasMudaram?.Invoke(quem);
				break;
			}

			case Protocol.S2C.Contas:
			{
				int n = reader.GetUShort();
				var lista = new List<ContaInfo>(n);
				for (int i = 0; i < n; i++)
					lista.Add(new ContaInfo(reader.GetString(48), reader.GetBool(), reader.GetBool(),
											reader.GetBool(), reader.GetString(160)));
				Contas = lista;
				ContasMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Forma:
			{
				int quem = reader.GetInt();
				int de = reader.GetUShort();
				int para = reader.GetUShort();
				FormaMudou?.Invoke(quem, de, para, reader.GetBool());
				break;
			}

			case Protocol.S2C.Skills:
			{
				MarcosTotais = reader.GetInt();
				MarcosLivres = reader.GetInt();
				int quantas = reader.GetUShort();
				SkillsAprendidas.Clear();
				for (int i = 0; i < quantas; i++) SkillsAprendidas.Add(reader.GetString(96));
				SkillsMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Atributos:
				Atributos = Protocol.AtributosState.Read(reader);
				AtributosRecebidos?.Invoke(Atributos);
				break;

			case Protocol.S2C.Corpo:
				Corpo = reader.GetCorpo();
				CorpoAtualizado?.Invoke(Corpo);
				break;

			// A CORRECAO VEM CARIMBADA com a sequencia que o servidor considerou. Guardar o
			// carimbo permite ao cliente ignorar correcao mais velha que a ultima que ele ja
			// aplicou -- caso o canal entregue fora de ordem.
			case Protocol.S2C.Correction:
			{
				uint seq = reader.GetUInt();
				Vec2 pos = reader.GetVec();
				if (seq < SeqConfirmada) break;   // correcao atrasada: a atual ja e mais nova
				SeqConfirmada = seq;
				Corrected?.Invoke(pos);
				break;
			}

			case Protocol.S2C.PeerLeft:
				PeerLeft?.Invoke(reader.GetInt());
				break;

			// TROCOU DE PLANETA: o estrago do anterior nao vale aqui. O servidor manda a lista da
			// zona nova logo em seguida (`MandarCenario`).
			case Protocol.S2C.ZoneChanged when LimparCenario():
			{
				Zone = reader.GetZone();
				Vec2 spawn = reader.GetVec();
				ZoneChanged?.Invoke(Zone, spawn);
				break;
			}
		}
	}
}
