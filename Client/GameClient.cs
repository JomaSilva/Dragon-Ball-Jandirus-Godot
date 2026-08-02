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
	public void SendState(Vec2 pos, Facing facing, bool moving)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.InputState);
		w.PutVec(pos);
		w.Put((byte)(((byte)facing & 0x03) | (moving ? 0x80 : 0x00)));
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
		if (!Connected) return;
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
				GD.Print($"[client] entrei como id {LocalId} em {Zone} @ {spawn} " +
						 $"| {Sheet.Class} | BP {Sheet.ExpressedBP:0}");
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
					GD.Print($"[client] BP {antes:0} -> {Sheet.BP:0}");
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

			case Protocol.S2C.Correction:
				Corrected?.Invoke(reader.GetVec());
				break;

			case Protocol.S2C.PeerLeft:
				PeerLeft?.Invoke(reader.GetInt());
				break;

			case Protocol.S2C.ZoneChanged:
			{
				Zone = reader.GetZone();
				Vec2 spawn = reader.GetVec();
				ZoneChanged?.Invoke(Zone, spawn);
				break;
			}
		}
	}
}
