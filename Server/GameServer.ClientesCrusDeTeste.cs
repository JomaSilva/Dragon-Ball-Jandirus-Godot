using Godot;
using Jandirus.Core;
using Jandirus.Core.Races;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS CLIENTES CRUS DAS BANCADAS DE SERVIDOR -- o que a `--paradateste`, a `--adminlocalteste` e a
/// `--saidaembateteste` tem em comum: gente de verdade entrando pela PORTA, sem janela e sem `GameClient`.
///
/// Um <see cref="ClienteCruDeTeste"/> e um `NetManager` que fala o fio de producao (o pedido de conexao com a
/// versao, o login, criar ou escolher personagem) e guarda o que o servidor respondeu. A
/// <see cref="MesaDeClientesCrus"/> junta os clientes de uma bancada e os gestos que todas repetem -- entrar,
/// logar, entrar no mundo, SAIR (a queda do par, que roda o `Drop` de producao) -- bombeando a rede ela mesma:
/// estas bancadas rodam com a porta aberta e a arvore ainda sem processar o servidor.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// UM CLIENTE CRU E PERSISTENTE: um `NetManager` so, que disca, cai e disca de novo do MESMO soquete -- o que
	/// o `GameClient` faz. Fala o fio de producao (o pedido de conexao com a versao, o `C2S.Login`, o
	/// `C2S.CreateChar`, o `C2S.PickSlot`) e guarda o que o servidor respondeu.
	/// </summary>
	private sealed class ClienteCruDeTeste
	{
		public readonly NetManager Rede;
		private readonly EventBasedNetListener _ouvinte = new();
		private NetPeer? _par;

		public bool Conectado { get; private set; }
		/// <summary>O `S2C.Versao` chegou: o aperto de mao passou.</summary>
		public bool VersaoChegou { get; private set; }
		/// <summary>O `S2C.JoinAccepted` chegou: ha um corpo no mundo.</summary>
		public bool NoMundo { get; private set; }
		/// <summary>O motivo da ultima queda (o nome do `DisconnectReason`), ou nulo.</summary>
		public string? Caiu { get; private set; }
		/// <summary>O texto do ultimo `S2C.JoinRejected`, ou nulo.</summary>
		public string? Recusa { get; private set; }
		/// <summary>O ultimo `S2C.SlotList`, lido pelo leitor de producao, ou nulo.</summary>
		public List<SlotInfo>? Slots { get; private set; }
		/// <summary>(2026-10-17) O bebe a caminho que veio no fim do ultimo `S2C.SlotList` (o leitor de producao), ou nulo.</summary>
		public Jandirus.Core.Races.OfertaDeBebe? Bebe { get; private set; }
		/// <summary>Quantas fichas lentas (`S2C.Atributos`, o pacote que leva os PODERES) chegaram nesta conexao.</summary>
		public int FichasLentas { get; private set; }
		/// <summary>Os poderes da ultima ficha lenta, e a soma de todos os que ja chegaram nesta conexao (nem um pacote escapa).</summary>
		public uint UltimosPoderes { get; private set; }
		public uint PoderesJaVistos { get; private set; }
		/// <summary>(2026-10-17) A ultima ficha lenta inteira, como chegou pelo leitor de producao -- a idade e os meses dela (`--calendarioteste`).</summary>
		public Protocol.AtributosState? UltimaFicha { get; private set; }
		public int PortaLocal => Rede.LocalPort;

		/// <summary>
		/// A PROVA DE ANFITRIAO que este cliente manda no pedido de conexao, perguntada A CADA discagem -- ou nulo
		/// (o padrao: um cliente de fora, que nao manda prova nenhuma). O cliente "do mesmo processo" pergunta ao
		/// servidor, como o `GameClient`; quem forja devolve os bytes que quiser.
		/// </summary>
		public Func<byte[]?>? ProvaAoDiscar;

		public ClienteCruDeTeste()
		{
			Rede = new NetManager(_ouvinte) { AutoRecycle = true, UpdateTime = 5, ChannelsCount = Protocol.TotalDeCanais };
			_ouvinte.PeerConnectedEvent += _ => Conectado = true;
			_ouvinte.PeerDisconnectedEvent += (_, info) => { Conectado = false; Caiu = info.Reason.ToString(); };
			_ouvinte.NetworkReceiveEvent += (_, r, _, _) => AoReceber(r);
			Rede.Start();
		}

		private void AoReceber(NetPacketReader r)
		{
			if (r.AvailableBytes < 1) return;
			switch ((Protocol.S2C)r.GetByte())
			{
				case Protocol.S2C.Versao: VersaoChegou = true; break;
				case Protocol.S2C.JoinRejected: Recusa = r.GetString(120); break;
				case Protocol.S2C.JoinAccepted: NoMundo = true; break;
				case Protocol.S2C.SlotList:
				{
					int n = r.GetByte();
					var slots = new List<SlotInfo>(n);
					for (int i = 0; i < n; i++) slots.Add(SlotInfo.Read(r));
					Bebe = r.GetOfertaDeBebe();
					Slots = slots;
					break;
				}
				case Protocol.S2C.Atributos:
				{
					Protocol.AtributosState ficha = Protocol.AtributosState.Read(r);   // o leitor de producao
					UltimaFicha = ficha;
					uint poderes = ficha.Poderes;
					FichasLentas++;
					UltimosPoderes = poderes;
					PoderesJaVistos |= poderes;
					break;
				}
			}
		}

		/// <summary>Disca (de novo) do mesmo soquete, com o pedido de conexao de producao.</summary>
		public void Discar(int porta, IdentidadeDeBuild versao)
		{
			Conectado = VersaoChegou = NoMundo = false;
			Caiu = Recusa = null;
			Slots = null;
			FichasLentas = 0;
			UltimosPoderes = PoderesJaVistos = 0;
			_par = Rede.Connect("127.0.0.1", porta, FioDaVersao.PedidoDeConexao(versao, ProvaAoDiscar?.Invoke()));
		}

		public void Login(string conta, string senha = "teste")
		{
			Recusa = null;
			Slots = null;
			NetDataWriter w = Protocol.Begin(Protocol.C2S.Login);
			w.Put(conta);
			w.Put(senha);
			_par?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		public void CriarPersonagem(int slot, CharacterDraft ficha)
		{
			NetDataWriter w = Protocol.Begin(Protocol.C2S.CreateChar);
			w.Put((byte)slot);
			w.PutDraft(ficha);
			w.PutAppearance(new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" });
			_par?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		public void EscolherSlot(int slot)
		{
			NetDataWriter w = Protocol.Begin(Protocol.C2S.PickSlot);
			w.Put((byte)slot);
			_par?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		/// <summary>Sai por vontade propria, como quem fecha o jogo: o servidor recebe a queda do par e roda o `Drop` de sempre.</summary>
		public void Sair() => _par?.Disconnect();

		/// <summary>
		/// (2026-10-18) UM PACOTE DE JOGO montado pela bancada, pelo canal de sempre -- a `--tuffleteste` manda a guarda erguida pelo dono de
		/// um corpo possuido, que o `Handle` tem de engolir.
		/// </summary>
		public void Mandar(NetDataWriter w) => _par?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		public void Fechar() => Rede.Stop();
	}

	/// <summary>Bombeia o servidor (se estiver de pe) e os clientes de bancada ate a condicao, ou ate o teto. Devolve se ela veio.</summary>
	private bool BombearClientesCrus(Func<bool> ate, int tetoMs, ClienteCruDeTeste[] clientes)
	{
		var relogio = System.Diagnostics.Stopwatch.StartNew();
		while (relogio.ElapsedMilliseconds < tetoMs)
		{
			if (Running) _net.PollEvents();
			foreach (ClienteCruDeTeste c in clientes) c.Rede.PollEvents();
			if (ate()) return true;
			Thread.Sleep(2);
		}
		return ate();
	}

	/// <summary>A ficha de um personagem de bancada, igual a do caminho automatico do cliente (`Boot.AutoEscolher`).</summary>
	private static CharacterDraft FichaDeClienteCru(string nome)
	{
		var ficha = new CharacterDraft
		{
			Name = nome, Race = "Human",
			Planet = Array.Find(CharacterDraft.Planetas, p => Array.IndexOf(CharacterDraft.RacasDoPlaneta(p), "Human") >= 0) ?? "Earth",
			Gender = "Male",
			Backstory = "Personagem de bancada, criado por um cliente cru de bancada de servidor.",
		};
		ficha.Age = Math.Min(18, ficha.IdadeMaxima);
		string[] linhagens = CharacterDraft.EscolhasDeClasse("Human");
		if (linhagens.Length > 0) ficha.ChosenClass = linhagens[0];
		return ficha;
	}

	private static bool MesmosMembrosDeTeste(Dictionary<string, double[]> a, Dictionary<string, double[]> b) =>
		a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out double[]? outro) && kv.Value.SequenceEqual(outro));

	/// <summary>
	/// A MESA DE UMA BANCADA: os clientes crus dela e os gestos que todas repetem, cada um pelo fio e pela porta
	/// de producao.
	/// </summary>
	private sealed class MesaDeClientesCrus(GameServer servidor, int porta, params ClienteCruDeTeste[] todos)
	{
		private readonly IdentidadeDeBuild _versao = servidor.MinhaVersao;

		public bool Bombeia(Func<bool> ate, int tetoMs) => servidor.BombearClientesCrus(ate, tetoMs, todos);

		/// <summary>O aperto de mao. Devolve se o servidor aceitou a conexao e disse a versao.</summary>
		public bool Entra(ClienteCruDeTeste cli)
		{
			// QUEM AINDA NAO LEU A DESPEDIDA de uma parada (ou da propria saida) ainda se acha conectado, e o `Connect`
			// do LiteNetLib devolve o par VELHO em vez de discar (medido na primeira rodada da `--paradateste`: o
			// cliente "entrava" e caia em seguida). Primeiro ele le a queda -- como o cliente do jogo, que so volta ao
			// menu depois dela.
			if (cli.Conectado) Bombeia(() => !cli.Conectado, 4000);
			cli.Discar(porta, _versao);
			return Bombeia(() => cli.VersaoChegou || cli.Caiu != null, 4000) && cli.VersaoChegou;
		}

		/// <summary>Manda o login e espera a lista de slots ou a recusa. Devolve se o servidor RESPONDEU.</summary>
		public bool Loga(ClienteCruDeTeste cli, string conta, int tetoMs = 4000)
		{
			cli.Login(conta);
			return Bombeia(() => cli.Slots != null || cli.Recusa != null, tetoMs);
		}

		public ServerPlayer? CorpoDe(string conta) =>
			servidor.Jogadores.FirstOrDefault(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));

		/// <summary>Do aperto de mao ao corpo no mundo: loga, cria o personagem (ou escolhe o que ja existe) e entra.</summary>
		public ServerPlayer? EntraNoMundo(ClienteCruDeTeste cli, string conta, string nome)
		{
			if (!Entra(cli) || !Loga(cli, conta) || cli.Slots is not { Count: > 0 } slots) return null;
			if (slots[0].Ocupado) cli.EscolherSlot(0);
			else cli.CriarPersonagem(0, FichaDeClienteCru(nome));
			return Bombeia(() => cli.NoMundo || cli.Recusa != null, 8000) && cli.NoMundo ? CorpoDe(conta) : null;
		}

		/// <summary>A SAIDA NORMAL: o cliente desconecta e o servidor roda o `Drop` de producao. Devolve se a conta saiu inteira.</summary>
		public bool SaiDoJogo(ClienteCruDeTeste cli, string conta)
		{
			cli.Sair();
			return Bombeia(() => !cli.Conectado && CorpoDe(conta) == null && servidor.ContaViva(conta) == null, 4000);
		}

		/// <summary>O par que o SERVIDOR tem pra este cliente (pela porta local dele), ou nulo.</summary>
		public NetPeer? ParDe(ClienteCruDeTeste cli) => servidor._net.ConnectedPeerList.FirstOrDefault(p => p.Port == cli.PortaLocal);

		/// <summary>O personagem do slot 1 desta conta, lido DO DISCO agora.</summary>
		public CharacterSave? NoDisco(string conta) => servidor._store?.Carregar(conta)?.Slots[0];

		/// <summary>
		/// O QUE O TIQUE MANDARIA: roda o tique de combate de producao (e ele que manda a ficha lenta, com os
		/// poderes, de quem mudou) e deixa os pacotes chegarem nos clientes.
		/// </summary>
		public void TiqueEEntrega()
		{
			servidor.TickCombate(Protocol.TickSeconds);
			Bombeia(() => false, 120);
		}

		public void Fecha()
		{
			foreach (ClienteCruDeTeste cli in todos) cli.Fechar();
			// O servidor le a despedida dos clientes de bancada (o `Drop` de cada um) antes de seguir a vida.
			for (int i = 0; i < 20 && servidor.Running; i++) { servidor._net.PollEvents(); Thread.Sleep(2); }
		}
	}
}
