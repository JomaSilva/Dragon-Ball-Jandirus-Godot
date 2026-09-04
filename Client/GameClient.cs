using Godot;
using Jandirus.Core.Combat;
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

	/// <summary>
	/// A ZONA DESTA TELA, ESCRITA A MAO. So a bancada -- ver `--diagforma`.
	///
	/// ============================ ISTO NAO E FORJAR A REGRA, E FORJAR O LUGAR ============================
	/// A cinematica pergunta `Espaco.EhPlaneta(GameClient.Instance.Zone)` uma vez, no `_Ready`, e a
	/// resposta decide se quem esta longe sente o eco do tremor ou NADA (ver `Transformacao._noPlaneta`).
	/// O degrau do "nada" e o do ESPACO -- e a bancada roda sempre na Terra, entao ele nunca era medido.
	/// Ficou anotado como buraco conhecido por uma fase inteira.
	///
	/// Escrever a zona aqui poe a bancada no OUTRO estado legitimo do jogo ("estou no espaco", que e o
	/// que todo mundo que decola fica): o que roda depois e o codigo de producao inteiro, sem desvio.
	/// O que NAO se pode fazer -- e por isso a distincao vale a nota -- e forjar a RESPOSTA: repetir o
	/// `EhPlaneta` dentro do teste mediria o teste.
	///
	/// A escrita e sincrona e o valor volta no mesmo quadro; nada mais le a zona no meio disso.
	/// ================================================================================================
	/// </summary>
	internal ZoneKey ZonaDeTeste { get => Zone; set => Zone = value; }
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

	/// <summary>
	/// A CONEXAO CAIU -- queda do servidor ou estouro do prazo de silencio, os dois.
	///
	/// ============================ POR QUE ELE PRECISOU EXISTIR ============================
	/// A queda ja era conhecida aqui dentro (o `PeerDisconnectedEvent` abaixo), mas morria num
	/// `GD.Print`: ninguem de fora ficava sabendo. Isso bastava enquanto nada da interface dependia
	/// da resposta do servidor pra sair do lugar.
	///
	/// A cobertura da entrada no mundo depende: ela sobe no clique e so cai quando o mundo foi
	/// DESENHADO (ver `TelaDeCarregamento.Soltar` -- saida por FATO, nunca por relogio). Com o
	/// servidor morto no meio dessa espera o mundo nunca vem, e sem este evento o jogador ficava
	/// olhando "carregando..." **pra sempre**. Um jogador preso numa tela de carregamento e pior
	/// que os 1-2 s de tela vazia que a cobertura veio consertar.
	///
	/// Ele continua sendo um FATO e nao um relogio: quem decide que a conexao acabou e o
	/// LiteNetLib, que dispara tanto na queda anunciada quanto no fim do prazo de silencio.
	/// ======================================================================================
	/// </summary>
	public event Action<string>? Caiu;

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
	/// <summary>
	/// A aparencia de alguem da zona: id, nome, raca, genero, ficha visual -- e **de que TIPO de fusao
	/// este corpo e** (o ultimo campo; nulo = nao e fusao).
	///
	/// O dado anda junto da aparencia e nao dentro dela porque `Appearance` e o objeto que vai pro DISCO;
	/// ver `GameServer.TrocarAparencias`, que o escreve, e `ServerPlayer.LookDeFusao`. Quem o consome e
	/// uma regra so: no SSJ4, **a fusao da DANCA** usa a folha do Gogeta pintada de vermelho -- a Potara
	/// usa a mesma folha na cor normal. Ver `Fusao.TintaDoCabeloDaFusao`.
	///
	/// **ERA UM `bool`** ("este corpo e uma fusao") ate o dono corrigir a regra do cabelo. Com Danca e
	/// Potara divergindo o bit deixou de responder a pergunta, e o tipo passou a viajar no lugar dele.
	/// </summary>
	public event Action<int, string, string, string, Jandirus.Core.Appearance.Appearance,
					   Jandirus.Core.Social.TipoDeFusao?>? PeerLooked;

	private readonly NetManager _net;
	private readonly EventBasedNetListener _listener = new();
	private NetPeer? _peer;
	private string _conta = "", _senha = "";
	private readonly List<EntityState> _scratch = [];

	/// <summary>
	/// Os ataques de ki do ultimo snapshot. Lista REUSADA, como a dos corpos: o snapshot chega 30
	/// vezes por segundo e alocar uma lista por pacote e lixo por lixo.
	/// </summary>
	private readonly List<ProjetilState> _scratchTiros = [];

	public GameClient()
	{
		_net = new NetManager(_listener)
		{
			AutoRecycle = true,
			UpdateTime = 15,
			// TRES, e o servidor abre os mesmos tres -- ver `Protocol.ChannelVoz`.
			ChannelsCount = Protocol.TotalDeCanais,
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
			Caiu?.Invoke(info.Reason.ToString());   // ver o comentario do evento: a cobertura depende disto
		};
		_net.Start();
	}

	public override void _Process(double delta)
	{
		_net.PollEvents();

		// ============================ O RELOGIO DO MUNDO ANDA AQUI ============================
		// O comentario de <see cref="TempoDoMundo"/> ja PROMETIA isto ("entre um pacote e outro isto
		// anda sozinho"), e nao era verdade: quem andava era uma COPIA dentro do `Iluminacao`, e o
		// campo daqui ficava parado entre duas sincronias -- ou seja **pulava de 15 em 15 segundos**
		// pra todo mundo que lesse este campo em vez daquela copia (o menu, a carta, a lua).
		//
		// Passou a doer de verdade com a agonia de planeta: a rampa dos cinco minutos e funcao do
		// relogio, e um relogio que salta 15 s faz a crosta de magma engrossar aos degraus em vez de
		// crescer. Ver `IntensidadeDaAgonia`.
		//
		// SO DEPOIS DO PRIMEIRO PACOTE (`TempoChegou`): somar delta a partir de zero seria inventar
		// uma hora, que e exatamente o que a guarda do `Iluminacao` existe pra impedir.
		// ===================================================================================
		if (TempoChegou) TempoDoMundo += delta;
	}

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

	public void SendState(Vec2 pos, Facing facing, bool moving, bool correndo = false,
						  bool subir = false, bool descer = false)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.InputState);
		w.Put(++_seq);
		w.PutVec(pos);
		w.Put((byte)(((byte)facing & 0x03)
					 | (correndo ? Protocol.InputCorrendo : 0)
					 | (moving ? Protocol.InputAndando : 0)
					 | (subir ? Protocol.InputSubir : 0)
					 | (descer ? Protocol.InputDescer : 0)));
		_peer!.Send(w, Protocol.ChannelState, DeliveryMethod.Sequenced);
	}

	/// <summary>
	/// APAGA O PERSONAGEM DE UM SLOT. O <paramref name="nome"/> e o que o jogador DIGITOU -- quem
	/// confere se ele bate com o do save e o servidor (ver `GameServer.DeleteChar`).
	/// </summary>
	public void SendDeleteChar(int slot, string nome)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.DeleteChar);
		w.Put((byte)slot);
		w.Put(nome);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
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
	/// CHEGOU UM QUADRO DE VOZ: (id de quem fala, sequencia, distancia 0-255, ha parede, buffer, tamanho).
	///
	/// ============================ O BUFFER E REUSADO: CONSUMA AGORA ============================
	/// O ultimo par e o buffer INTERNO deste cliente e quantos bytes valem nele. Guardar a referencia
	/// pra usar no quadro seguinte le a voz de outra pessoa. E assim de proposito: sao 50 quadros por
	/// segundo por falante, e alocar um array por quadro seria lixo por lixo -- a mesma disciplina que
	/// o `_scratch` do snapshot ja segue, pelo mesmo motivo.
	/// ======================================================================================
	/// </summary>
	public event Action<int, ushort, byte, bool, byte[], int>? VozRecebida;

	/// <summary>O quadro de voz que acabou de chegar. Ver o aviso em <see cref="VozRecebida"/>.</summary>
	private readonly byte[] _vozEntrando = new byte[Jandirus.Core.Social.VozLocal.MaxBytesDeQuadro];

	/// <summary>
	/// MANDA UM QUADRO DE VOZ. **Nao ha destinatario** -- quem ouve e decisao do servidor. Ver
	/// <see cref="Protocol.C2S.Voz"/>.
	/// </summary>
	public void MandarVoz(ushort seq, byte[] dados, int tam)
	{
		if (!Connected || tam <= 0 || tam > Jandirus.Core.Social.VozLocal.MaxBytesDeQuadro) return;

		var w = Protocol.Begin(Protocol.C2S.Voz);
		w.Put(seq);
		w.Put((byte)tam);
		w.Put(dados, 0, tam);
		// NAO CONFIAVEL. Quadro perdido se joga fora: retransmitir chega depois do proximo e trava a
		// fila atras dele. Ver `Protocol.ChannelVoz`.
		_peer!.Send(w, Protocol.ChannelVoz, DeliveryMethod.Unreliable);
	}

	/// <summary>
	/// O servidor plantou um decalque no chao da zona. Ver `Client/Decalques.cs`.
	///
	/// O ultimo parametro so tem valor no <see cref="Protocol.Decal.Membro"/> (qual peca do corpo
	/// caiu); nos outros sete ele chega <c>Nenhuma</c> e e ignorado.
	/// </summary>
	public event Action<Protocol.Decal, Vec2, Facing, PecaDeCorpo>? DecalqueCaiu;

	/// <summary>
	/// O QUE EU APRENDI e quantos marcos tenho. Chega quando muda, como o corpo e os atributos.
	/// </summary>
	public HashSet<string> SkillsAprendidas { get; } = new(StringComparer.OrdinalIgnoreCase);
	public int MarcosTotais { get; private set; }
	public int MarcosLivres { get; private set; }

	/// <summary>
	/// O ESTADO DAS MINHAS ARVORES, como o servidor calculou: as que o progresso abriu, o tier de
	/// vitrine de cada uma (e o proximo degrau), as skills acesas e apagadas. Chega na cauda do
	/// `S2C.Skills` (ver `Protocol.PorEstadoDeSkills` pro porque de ser o resultado e nao os
	/// contadores). Quem consome poe isto num `SkillBook` com `CarregarEstado` e pergunta ao
	/// `Avaliar` -- a mesma funcao que o servidor usa pra decidir.
	/// </summary>
	public List<string> SkillsDestravadas { get; private set; } = [];
	public List<Jandirus.Core.Skills.EstadoDeArvore> SkillsArvores { get; private set; } = [];

	/// <summary>
	/// OS VERBS QUE EU POSSO ACIONAR HOJE, ditos pelo servidor -- a mesma lista que o `SabeTecnica` de la
	/// aceita. E daqui que nasce o botao de um verb concedido por NIVEL ou por CASA (o cliente nao tem o
	/// nivel das skills). Ver `Protocol.PorEstadoDeSkills` e `Habilidades.DasSkills`.
	/// </summary>
	public List<string> VerbosAtivos { get; private set; } = [];

	/// <summary>
	/// ESTE PERSONAGEM FOI DESIGNADO VILAO por um admin (`mob/var/isVillain` do DM).
	///
	/// Chega junto das skills, e serve **so pro desenho do menu**: quem decide se a compra sai e o
	/// servidor. Ver `GameServer.MandarSkills`.
	/// </summary>
	public bool SouVilao { get; private set; }

	/// <summary>
	/// OS PLANETAS MORTOS, replicados por `S2C.Mortos`.
	///
	/// O cliente PRECISA da lista porque ele enumera planetas sozinho: a carta estelar chama
	/// `Espaco.PreFeitos()` e `Sistemas.Do` direto e desenha o que esta a anos-luz daqui. Sem isto
	/// ela poria um botao "Viajar" em cima de um mundo que virou po.
	/// </summary>
	public readonly Jandirus.Core.World.RegistroDeMortos Mortos = new();

	/// <summary>
	/// QUANDO A AGONIA DE CADA PLANETA VENCE, em tempo de MUNDO. Chave = <see cref="Jandirus.Core.World.ChaveDePlaneta.Texto"/>.
	///
	/// Existe so no cliente e nao persiste: e a traducao de "segundos que faltam" (o que o servidor
	/// guarda e manda) pra "que horas isso acaba" (o que o desenho precisa). Ver o handler de
	/// <see cref="Protocol.S2C.Mortos"/>.
	/// </summary>
	private readonly Dictionary<string, double> _agoniaAte = [];

	/// <summary>
	/// ============================ A LISTA DE MORTOS CHEGOU -- E O QUE ISSO VIRA AQUI DENTRO ============================
	/// Separado do `case` do pacote por um motivo so: e o unico jeito de uma bancada exercitar a
	/// CONVERSAO de verdade (segundos que faltam -> prazo absoluto -> intensidade) em vez de escrever
	/// o resultado dela na mao. O `case` le os bytes e chama isto; a bancada monta a lista e chama
	/// isto. Um caminho, dois chamadores -- e nenhum atalho que pule o que se quer medir.
	///
	/// SUBSTITUI, NAO ACRESCENTA: o pacote e a lista INTEIRA (ver `S2C.Mortos`), e e exatamente isso
	/// que faz um planeta RESTAURADO por admin voltar a aparecer na carta sem precisar de um segundo
	/// pacote dizendo "esqueca aquele".
	///
	/// ============================ E O PRAZO DE QUEM JA MORREU NAO E APAGADO ============================
	/// O pacote do COMMIT chega com `faltam = 0` -- e e nesse instante que a explosao tem que ser
	/// desenhada. Limpar o dicionario inteiro e refaze-lo apagaria o prazo do unico planeta que
	/// precisa dele, e o mundo sumiria entre dois quadros em vez de estourar (que e literalmente o
	/// defeito que este trabalho veio consertar).
	///
	/// Entao so sai daqui a chave que **desapareceu da lista** -- ou seja, planeta RESSUSCITADO. Quem
	/// continua morto continua com o prazo vencido, e o `PlanetaDesenhado` usa esse numero NEGATIVO
	/// pra saber que esta no meio do proprio estouro.
	/// ==============================================================================================
	///
	/// ============================ "FALTAM" VIRA "ATE QUANDO", AQUI E SO AQUI ============================
	/// O servidor guarda e manda SEGUNDOS QUE RESTAM (ver `EstadoDaMorte.Faltam`: o relogio do mundo
	/// anda com o servidor desligado, e um prazo absoluto no disco faria uma noite fora consumir o
	/// pavio). O CLIENTE precisa do contrario: um prazo absoluto, pra desenhar a rampa como funcao
	/// pura do relogio, sem um pacote por quadro e sem um segundo contador andando por conta propria.
	/// Mesmo desenho do `ClimaForcado.Ate`, que ja faz isto do lado de la.
	/// ================================================================================================
	/// </summary>
	internal void AplicarMortos(List<Jandirus.Core.World.EstadoDaMorte> lista)
	{
		var vistos = new HashSet<string>(lista.Count);

		foreach (Jandirus.Core.World.EstadoDaMorte e in lista)
		{
			vistos.Add(e.Chave);

			// ============================ E O PRAZO **NEGATIVO** DE UM MUNDO JA MORTO TAMBEM ENTRA ============================
			// Antes o crivo era so `Faltam > 0`, e isso deixava um buraco que so aparecia com os
			// destrocos: quem estava online quando o planeta morreu ficava com o prazo vencido aqui
			// dentro e contava negativo direitinho, mas **quem chegava na orbita 30 s depois nao tinha
			// entrada nenhuma** -- o `SegundosAteOEstouro` devolvia nulo e ele nao via rescaldo nenhum.
			// Duas telas discordando no mesmo lugar e exatamente o que o *"server sync"* do dono proibe.
			//
			// O conserto nao inventou campo: o servidor passou a deixar o `Faltam` de um destruido
			// **continuar descendo** ate o fim da janela dos destrocos (ver `GameServer.TickDaDestruicao`),
			// e este lado passou a aceitar o numero negativo que ja vinha no pacote. Zero byte novo.
			//
			// O CRIVO E A FASE, e nao o sinal: pra quem ainda esta MORRENDO, `Faltam` e o que resta do
			// ESTAGIO (nao do estouro), e um valor negativo ali seria save corrompido -- nao um prazo.
			// =============================================================================================================
			if (e.Faltam > 0 || Jandirus.Core.World.MortePlanetaria.EstaMorto(e.Fase))
				_agoniaAte[e.Chave] = TempoDoMundo + e.Faltam;
		}

		foreach (string k in _agoniaAte.Keys.ToList())
			if (!vistos.Contains(k)) _agoniaAte.Remove(k);

		Mortos.Substituir(lista);
		MortosMudaram?.Invoke();
	}

	/// <summary>
	/// ============================ A AGONIA DESTE PLANETA, DE 0 A 1 ============================
	/// A MESMA fracao que o servidor usa pra apertar o ceu, encurtar o tremor e derrubar mais chao --
	/// <see cref="Jandirus.Core.World.MortePlanetaria.Intensidade"/>, no Core, chamada pelos dois
	/// lados. Uma segunda conta aqui seria uma segunda rampa: a crosta de magma no espaco divergiria
	/// do chao tremendo no primeiro ajuste, e nada apontaria pra isso.
	///
	/// Devolve 0 pra planeta vivo ou ja destruido -- ausencia e a resposta, como no registro.
	/// =====================================================================================
	/// </summary>
	public double IntensidadeDaAgonia(Jandirus.Core.World.ChaveDePlaneta chave)
	{
		if (Mortos.De(chave) is not { } e) return 0;
		return Jandirus.Core.World.MortePlanetaria.Intensidade(
			e.Fase, e.Estagio, SegundosAteOEstouro(chave) ?? e.Faltam);
	}

	/// <summary>
	/// Quantos segundos faltam pra este planeta estourar (nulo = nao esta em contagem).
	///
	/// **Pode ficar NEGATIVO por um instante**, e isso e de proposito: e a janela entre o relogio
	/// local cruzar o prazo e o `S2C.Mortos` do commit chegar. E nela que a explosao e desenhada --
	/// ver `Client/CeuDoEspaco.PlanetaDesenhado`. Cortar em zero apagaria o unico momento do efeito.
	/// </summary>
	public double? SegundosAteOEstouro(Jandirus.Core.World.ChaveDePlaneta chave) =>
		_agoniaAte.TryGetValue(chave.Texto, out double ate) ? ate - TempoDoMundo : null;

	/// <summary>A lista de mortos mudou -- a carta estelar se redesenha.</summary>
	public event Action? MortosMudaram;
	public event Action? SkillsMudaram;

	/// <summary>Os efeitos ligados em mim agora ("invisivel", "escudo"). So pra HUD.</summary>
	public HashSet<string> EfeitosAtivos { get; } = new(StringComparer.Ordinal);

	/// <summary>Caiu um efeito: id e por quantos ms (0 = saiu, negativo = enquanto durar).</summary>
	public event Action<string, long>? EfeitoCaiu;

	/// <summary>
	/// Uma construcao de pe. `Nome`/`Arte`/`Estado`/`Pixel` vem DO SERVIDOR e nao do catalogo local: o
	/// catalogo do cliente so tem o que ELE pode comprar, e a bancada de outra pessoa tem que
	/// aparecer do mesmo jeito.
	///
	/// O `Nome` ENTROU DEPOIS, e pelo mesmo argumento da arte -- ele so faltava. O catalogo do cliente
	/// vem do `Ofertas`, que **esconde mobilia de mapa** (custo negativo): banco, macieira, porta da
	/// Sala do Tempo e as duas pecas da ponte nunca chegavam nele, e o menu da tecla E caia no ultimo
	/// recurso (`tipo.Replace('_', ' ')`) -- o jogador lia "Ship Control" e "Time Chamber Door", em
	/// ingles, no meio de um jogo em portugues.
	/// </summary>
	public readonly record struct ObraInfo(int Id, string Tipo, string Nome, Vector2 Pos, bool Aparafusada,
										   int Lab, string Dono,
										   string Arte, string Estado, Vector2 Pixel, bool Densa);

	/// <summary>
	/// Uma linha do catalogo de tecnologia, com o motivo do nao (0 = pode) e a ARTE.
	///
	/// A arte vem do servidor porque o cliente nao le `construcoes.json` -- ele so conhece o que
	/// pode comprar, e conhece pelo pacote. Ver `MandarCatalogoDeObras`.
	/// </summary>
	public readonly record struct OfertaDeObra(string Id, string Nome, double Custo, double Tech,
											   int Recusa, string Arte, string Estado);

	/// <summary>Um estilo de luta que eu sei, com a maestria e o teto dela.</summary>
	public readonly record struct EstiloInfo(string Id, string Nome, double Maestria, double Teto);

	public List<EstiloInfo> Estilos { get; private set; } = [];
	public string EstiloAtual { get; private set; } = "";
	public event Action? EstilosMudaram;

	/// <summary>
	/// UM CHEFE QUE EU JA VI, e portanto posso reenfrentar dentro da propria mente.
	///
	/// O NOME VEM DO SERVIDOR porque o cliente nao le o `npcs.json` -- mesmo argumento (e mesma
	/// frase) da <see cref="OfertaDeObra"/> logo acima. Ver `Protocol.S2C.MenteChefes`.
	/// </summary>
	public readonly record struct ChefeVisto(string Molde, string Nome);

	public List<ChefeVisto> ChefesVistos { get; private set; } = [];
	public event Action? ChefesVistosMudaram;

	/// <summary>
	/// AS TECNICAS DE KI QUE EU INVENTEI, e o rascunho aberto na mesa (nulo = nenhum).
	///
	/// ============================ O CLIENTE NAO CALCULA PONTO NENHUM ============================
	/// Estes objetos sao o modelo do `Core`, mas a tela NUNCA chama `Aplicar` neles: ela manda o
	/// verbo `ca_comprar` e desenha o que voltou. E de proposito, e e a regra 4 da casa -- a tabela
	/// de precos tem dezoito linhas e tres guardas que o proprio DM escreveu diferente entre si;
	/// duas copias dela divergiriam no primeiro ajuste.
	///
	/// Que o modelo VIAJE nao e o mesmo que o cliente DECIDIR: ele viaja porque a tela precisa
	/// mostrar `Gasto`, e mostrar um `Gasto` calculado do lado errado e como um jogo passa a
	/// discordar de si mesmo.
	/// ========================================================================================
	/// </summary>
	public List<Jandirus.Core.Skills.TecnicaCustomizada> Customizadas { get; private set; } = [];
	public Jandirus.Core.Skills.TecnicaCustomizada? Mesa { get; private set; }
	public event Action? CustomizadasMudaram;

	/// <summary>Assume (ou solta, com "-") uma postura de luta.</summary>
	public void SendEstilo(string id)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Estilo);
		w.Put(id);
		_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>O que eu carrego. Chega inteiro quando muda -- ver `S2C.Inventario`.</summary>
	public Jandirus.Core.Items.Inventario Mochila { get; private set; } = new();
	public event Action? MochilaMudou;

	/// <summary>
	/// UMA COISA DE ESFERA NO CHAO DESTA ZONA: a estatua, uma das sete, ou o dragao de pe.
	///
	/// A FOLHA E UM SIMBOLO ("comum", "namek", "estatua", "shenron", "porunga") e nao um `res://` --
	/// ver `Protocol.S2C.Esferas`, que explica por que aqui e diferente da <see cref="ObraInfo"/>.
	/// Quem traduz e o `EsferaDesenhada.FolhaDe`.
	/// </summary>
	public readonly record struct EsferaInfo(int Id, Protocol.CoisaDeEsfera Tipo, int Numero,
											 Vector2 Pos, bool Inerte, string Folha);

	public List<EsferaInfo> Esferas { get; private set; } = [];
	public event Action? EsferasMudaram;

	/// <summary>Uma Super Esfera ao meu alcance, no espaco. Dono vazio = livre.</summary>
	public readonly record struct SuperInfo(int Numero, Vector2 Pos, string Dono, bool Minha);

	public List<SuperInfo> Supers { get; private set; } = [];

	/// <summary>Quantas das sete Super Esferas sao minhas. E o placar da aba Nav.</summary>
	public int MinhasSupers { get; private set; }

	/// <summary>A frase do radar dourado, ja resolvida pelo servidor. Vazia = sem sinal.</summary>
	public string SinalDourado { get; private set; } = "";
	public event Action? SupersMudaram;

	/// <summary>Um dominio meu que ainda esta de pe -- uma das saidas do refugio (a opcao B1).</summary>
	public readonly record struct RefugioDominio(string Chave, string Nome, bool Escolhido, float Minutos);

	/// <summary>Um mundo vivo perto de onde era casa -- a outra saida (a opcao B2).</summary>
	public readonly record struct RefugioVizinho(string Nome, float Minutos, float Gravidade, bool Serve);

	/// <summary>
	/// O PLANETA NATAL DESTE PERSONAGEM FOI DESTRUIDO? Enquanto for falso, nada do refugio existe --
	/// e a tela e o botao do menu somem. Ver <see cref="Protocol.S2C.Refugio"/>.
	/// </summary>
	public bool RefugioPrecisa { get; private set; }

	/// <summary>O nome que o jogador le do planeta que acabou ("Terra", e nao "Earth").</summary>
	public string RefugioNatal { get; private set; } = "";

	public List<RefugioDominio> RefugioDominios { get; private set; } = [];
	public List<RefugioVizinho> RefugioVizinhos { get; private set; } = [];

	/// <summary>Perto de casa so sobrou mundo pesado demais -- ver `MotivoDoRefugio.MundoPesado`.</summary>
	public bool RefugioReserva { get; private set; }

	public event Action? RefugioMudou;

	/// <summary>
	/// O SERVIDOR PEDIU PRA TELA APARECER AGORA. Evento separado do <see cref="RefugioMudou"/> de
	/// proposito: a atualizacao redesenha quem ja esta olhando, e este ABRE -- juntar os dois faria
	/// cada resposta de escolha reabrir a tela que o jogador acabou de fechar.
	/// </summary>
	public event Action? RefugioPediuAbrir;

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

	/// <summary>
	/// QUEM ESTA MORTO na minha zona -- o id de cada corpo com AUREOLA.
	///
	/// GUARDADO pelo mesmo motivo das <see cref="Feridas"/> e do `PeerLook`: o pacote e reliable e
	/// chega quando o servidor quer, e o boneco que ele descreve pode nem existir ainda (quem CRIA o
	/// `RemotePlayer` e o snapshot, por outro canal). Sem guardar, um morto que entra na minha tela
	/// nasceria sem aureola e so a ganharia se morresse de novo -- o que nao acontece.
	///
	/// UM CONJUNTO E NAO UM `Dictionary&lt;int,bool&gt;`: "nao esta aqui" ja quer dizer vivo, e um
	/// mapa de bools teria dois jeitos de dizer a mesma coisa (ausente e `false`).
	/// </summary>
	public readonly HashSet<int> ComAureola = [];

	/// <summary>Alguem morreu ou voltou a vida: id.</summary>
	public event Action<int>? AureolaMudou;

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
	/// <summary>
	/// O ESPIAO DE VERBOS -- nulo em jogo. SO PRA BANCADA (`--diagembarque`).
	///
	/// ============================ POR QUE UMA VARREDURA PRECISA DELE ============================
	/// A pergunta *"nao ha um segundo caminho pra a mesma acao"* nao se responde lendo rotulo de
	/// botao: o rotulo pode dizer "Ir" e o `Pressed` mandar `nave_lancar`. Quem sabe o que um botao
	/// FAZ e este metodo, porque ele e o funil unico -- e a unica varredura honesta e apertar tudo e
	/// ver o que sai por aqui.
	///
	/// E ele DEVOLVE se deixa passar (`false` = engole) porque apertar tudo pra ver o que sai nao
	/// pode ser o mesmo que MANDAR tudo: uma varredura que mande de verdade dispara viagem
	/// interestelar, invasao de planeta e o que mais estiver na aba. Com o espiao engolindo, a
	/// bancada exercita a LIGACAO do botao sem exercitar o comando.
	/// ========================================================================================
	/// </summary>
	public static Func<string, string, bool>? EspiaoDeVerbos;

	public void SendVerbo(string cmd, string arg = "")
	{
		// ANTES do `Connected`: o que esta sob varredura e o que a TELA tentou mandar, e um botao
		// religado a um verbo velho continua sendo um segundo caminho mesmo com o fio caido.
		if (EspiaoDeVerbos is { } espiao && !espiao(cmd, arg)) return;
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.Verbo);
		w.Put(cmd);
		w.Put(arg);
		_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O ESPIAO DO CANAL DE TECNOLOGIA -- nulo em jogo. SO PRA BANCADA (`--diaginstalar`).
	///
	/// ============================ POR QUE A REGRA 3 PRECISA DELE ============================
	/// *"a previa e LOCAL: nenhum outro jogador ve"*. A leitura barata disso e "a lista de obras nao
	/// mudou enquanto o fantasma andava" -- e ela e **fraca**, porque um pacote que sai e o servidor
	/// recusa tambem nao muda lista nenhuma. Ficaria verde num cliente que tagarela.
	///
	/// A pergunta forte e a outra ponta: **nenhum byte saiu**. Este e o funil unico do canal onde
	/// "posicionar" e "construir" viajam (ver <see cref="SendTech"/>), entao o que nao passa por aqui
	/// nao chegou ao servidor por ele.
	///
	/// Ele DEVOLVE se deixa passar, como o <see cref="EspiaoDeVerbos"/>: `false` engole. E o que
	/// permite a uma bancada exercitar a LIGACAO de um botao sem disparar a acao.
	/// =====================================================================================
	/// </summary>
	public static Func<string, string, bool>? EspiaoDeTech;

	public void SendTech(string cmd, string arg = "")
	{
		// ANTES do `Connected`, pelo mesmo motivo do canal de verbos: o que esta sob varredura e o
		// que a TELA tentou mandar.
		if (EspiaoDeTech is { } olho && !olho(cmd, arg)) return;
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

	/// <summary>
	/// ONDE ESTA A NAVE EM QUE EU ESTOU, na coordenada da galaxia -- o "Observar" da ponte.
	///
	/// Nulo quase sempre: ela so existe entre pedir Observar e sair do interior. Enquanto existe, a
	/// carta estelar mostra a NAVE no lugar de mim (ver `MapaEstelar.MinhaPosicaoNaGalaxia`) -- que e
	/// o que responde "onde eu estou" pra quem esta numa sala sem janela.
	/// </summary>
	public readonly record struct NaveNoMapa(Vector2 Pos, string Zona, float CascoPct);
	public NaveNoMapa? NaveVista { get; private set; }

	/// <summary>
	/// O TIPO DA NAVE QUE ESTA EMBAIXO DE MIM ("" = nenhuma) -- ver <see cref="Protocol.S2C.Veiculo"/>.
	///
	/// E o que faz a tecla E alcancar o proprio veiculo: a nave pilotada NAO esta na lista de
	/// construcoes da zona (ela deixou de estar no chao), entao sem este campo o piloto nao teria
	/// alvo nenhum pra apertar -- nem pra descer. Ver `MenuDeInteracao.Abrir`.
	///
	/// QUEM O ESCREVE E SO O SERVIDOR, nos dois sentidos: ele manda o tipo ao embarcar e manda vazio
	/// ao desembarcar. O cliente nao deduz nada daqui -- deduzir "sai da nave quando muda de zona"
	/// quebraria justamente o caso em que a nave leva voce pra outra zona (o lancamento).
	/// </summary>
	public string VeiculoMontado { get; private set; } = "";

	/// <summary>O nome da nave montada, pra o menu escrever. Vazio quando nao ha nenhuma.</summary>
	public string NomeDoVeiculo { get; private set; } = "";

	/// <summary>
	/// O CADAVER QUE ESTA AO ALCANCE DA MINHA MAO (0 = nenhum) -- ver <see cref="Protocol.S2C.Cadaver"/>.
	///
	/// Ele e o irmao exato do <see cref="VeiculoMontado"/> e existe pelo mesmo buraco: a tecla E procura
	/// alvo na lista de CONSTRUCOES da zona, e um cadaver nao e uma construcao -- ele e um corpo, e
	/// corpo viaja pelo snapshot, que nao diz qual deles esta morto.
	///
	/// QUEM O ESCREVE E SO O SERVIDOR, nos dois sentidos: ele manda o id ao aproximar e manda ZERO ao
	/// afastar. O cliente nao deduz nada daqui -- deduzir pela distancia exigiria que ele soubesse quais
	/// corpos sao cadaveres, que e justamente o que ele nao sabe.
	/// </summary>
	public int CadaverPerto { get; private set; }

	/// <summary>O nome do cadaver ao alcance ("corpo de Fulano"), pra o menu escrever no titulo.</summary>
	public string NomeDoCadaver { get; private set; } = "";

	/// <summary>Os planetas da minha vizinhanca no espaco. Chega quando a CHUNK muda.</summary>
	public List<PlanetaInfo> Planetas { get; private set; } = [];
	public ulong SeedDoUniverso { get; private set; }
	public event Action? VizinhancaMudou;

	/// <summary>
	/// Um cargo do mundo: chave, quem ocupa ("" = vago), o que falta PRA MIM, o que o cargo E
	/// (<paramref name="Desc"/>) e o que ele DA (<paramref name="Da"/>).
	///
	/// OS DOIS ULTIMOS SAO NOVOS, e a ausencia deles era um sistema inteiro invisivel: o painel
	/// listava trinta cargos e **nao dizia o que nenhum deles entrega**. Ver `OQueOCargoEntrega`, no
	/// servidor -- a lista sai da tabela que a dadiva executa, com o que ainda e botao mudo marcado.
	/// </summary>
	public readonly record struct CargoInfo(string Chave, string Dono, string Falta, string Desc, string Da);

	public List<CargoInfo> Cargos { get; private set; } = [];
	public event Action? CargosMudaram;

	/// <summary>
	/// UMA PESSOA QUE EU CONHECO, do jeito que a aba People precisa dela.
	///
	/// O <c>Nome</c> vem VAZIO pra quem tem vinculo sem ficha (pontos de amizade com alguem que
	/// voce apagou da lista, um rival que voce nunca anotou). A aba desenha esses como
	/// <c>??? (assinatura)</c> -- e o mesmo que o `HtmlUI.dm:374` do original faz com quem voce
	/// nao conhece.
	/// </summary>
	public readonly record struct ConhecidoInfo(string Assinatura, string Nome, string Raca,
											   string Classe, int Familiaridade, byte Relacao,
											   float Amizade, float Inimizade, bool Rival);

	public List<ConhecidoInfo> Conhecidos { get; private set; } = [];

	/// <summary>Quem pediu minha amizade e ainda espera resposta ("" = ninguem).</summary>
	public string PedidoDeAmizade { get; private set; } = "";
	public event Action? ConhecidosMudaram;

	/// <summary>
	/// QUEM EU SINTO (ou quem o scouter le) -- a lista da aba Sense/Scan, do jeito que veio no fio.
	///
	/// Chega a no maximo 1 Hz e so quando muda (ver `Protocol.S2C.Sentidos`). <see cref="SentidosSaoDoScouter"/>
	/// diz de qual dos dois modos a lista e: com o scouter ligado cada presenca traz o BP exato; sem ele o
	/// BP vem NaN e so a razao viaja (ver `GameServer.Sigilo`). A aba desenha o que veio e nada mais -- ela
	/// nao tem de onde tirar poder alheio, e e assim que o sigilo se sustenta.
	/// </summary>
	public List<Protocol.PresencaState> Sentidos { get; private set; } = [];
	public bool SentidosSaoDoScouter { get; private set; }
	public event Action? SentidosMudaram;

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

	/// <summary>
	/// A PREVIA DA LIMPEZA TOTAL: o inventario do que vai sumir + o codigo que a confirma.
	///
	/// ============================ ELA VIVE AQUI E NAO NO MENU ============================
	/// O codigo tem PRAZO (um minuto), e o menu se remonta varias vezes por segundo. Guardado num
	/// campo da tela, ele morreria na primeira remontagem -- o painel pediria a previa de novo
	/// sozinho, em laco, e o servidor sortearia um codigo novo a cada volta.
	///
	/// Codigo VAZIO quer dizer "nao ha previa" (nunca pedida, consumida, ou vencida) e e o que
	/// fecha o painel de perigo. Ver `Protocol.S2C.Limpeza`.
	/// ==================================================================================
	/// </summary>
	public readonly record struct PreviaDeLimpeza(string Codigo, int Segundos, List<string> Linhas);

	public PreviaDeLimpeza Limpeza { get; private set; } = new("", 0, []);
	public event Action? LimpezaMudou;

	/// <summary>
	/// FECHA O PAINEL DE PERIGO DESTE CLIENTE. So local: o codigo do servidor continua valendo ate
	/// vencer sozinho (um minuto), e nao ha verb de "cancelar" -- um caminho a mais pra manter com o
	/// unico efeito de encurtar um prazo que ja e curto. Quem desistiu so nao quer mais ver a tela.
	/// </summary>
	public void EsquecerLimpeza()
	{
		Limpeza = new PreviaDeLimpeza("", 0, []);
		LimpezaMudou?.Invoke();
	}

	/// <summary>
	/// Alguem mudou de forma: quem, de que forma, pra qual, QUANTA cena isso merece e se ele DOMINOU
	/// a forma que esta assumindo.
	///
	/// O quarto parametro era um `bool primeira` e virou <see cref="Jandirus.Core.Forms.DegrauDeCena"/>:
	/// sao TRES estados agora (estreia / encurtada / instantanea), e quem decide e o servidor -- ele e
	/// quem tem a maestria no instante da troca. Ver `Cinematicas.Degrau`.
	///
	/// O QUINTO E OUTRA FAIXA DA MESMA BARRA (100% em vez de 50%) e viaja pelo mesmo motivo: a
	/// maestria dos OUTROS nunca chega aqui -- `AtributosState.Maestrias` e a ficha de quem esta na
	/// frente da tela. Sem ele, o cabelo de Grade 4 dos outros jogadores seria indeduzivel.
	/// </summary>
	public event Action<int, int, int, Jandirus.Core.Forms.DegrauDeCena, bool>? FormaMudou;

	/// <summary>
	/// ALGUEM VIROU (OU DEIXOU DE SER) OOZARU: quem, qual macaco, se foi a PRIMEIRA vez, e QUANTA
	/// cena isso merece.
	///
	/// Irmao do <see cref="FormaMudou"/> e NAO um caso dele -- ver `Protocol.S2C.Oozaru`: quem
	/// entrasse pelo canal de forma cairia no fallback por ordem do `Cinematicas.Para`, que nunca
	/// devolve null, e o macaco gigante assistiria a cinematica de Super Saiyajin.
	///
	/// O tipo e o do Core (`FormaOozaru`) e nao um `byte` cru: quem escuta desenha de acordo com
	/// regular ou dourado, e um byte solto obrigaria cada ouvinte a lembrar o que 1 e 2 querem
	/// dizer. A traducao acontece uma vez, na leitura do pacote.
	///
	/// O QUARTO PARAMETRO NAO E REDUNDANTE COM O TERCEIRO: a cena do macaco toca toda vez (ela E a
	/// transformacao, nao a comemoracao da estreia), entao `primeira` nunca serviu pra decidir cena --
	/// ele so carimba o `** Nome **` no chat. Quem decide cena e o degrau, e ele so vale `Nenhuma`
	/// quando o pacote e ESTADO e nao acontecimento: eu cheguei numa zona onde a fera ja existia.
	/// </summary>
	public event Action<int, Jandirus.Core.Forms.FormaOozaru, bool, Jandirus.Core.Forms.DegrauDeCena>? OozaruMudou;

	/// <summary>
	/// A FURIA DE ALGUEM IRROMPEU -- o `AngerCinematic()` do DM (`Murder.dm:136`).
	///
	/// UM `int` E SO: quem. Nao ha grau, prazo nem duracao porque nao ha o que o cliente faca com
	/// eles -- ver `Protocol.S2C.Furia`. Quem decide SE isto sai (grau extremo, erupcao e nao
	/// prolongamento, a raiva nao vai virar transformacao, e a recarga de 60 s) e o servidor, e o
	/// pacote chegar ja e a decisao.
	///
	/// ============================ NAO E O <see cref="FormaMudou"/> COM OUTRO NOME ============================
	/// A cena da furia nao muda forma nenhuma -- ela roda por cima do corpo do jeito que ele esta, e o
	/// personagem termina exatamente como comecou. Empurra-la pelo canal de forma exigiria mandar uma
	/// forma falsa e o cliente teria que aprender a nao vesti-la, que e a familia de defeito que ja
	/// tirou o Oozaru daquele canal.
	/// ==================================================================================================
	/// </summary>
	public event Action<int>? FuriaIrrompeu;

	/// <summary>
	/// UMA CENA DO BIO-ANDROIDE COMECOU EM ALGUEM -- quem, e qual (ver
	/// <see cref="Jandirus.Core.Forms.Cinematicas.CenaBio"/>).
	///
	/// IRMAO DO <see cref="FuriaIrrompeu"/> e pelo mesmo argumento: sao ACONTECIMENTOS de zona, sem
	/// estado a sincronizar, e por isso nao viajam pelo canal de forma nem pelo de efeito. Duas das
	/// tres nao mudam forma nenhuma -- elas sobem um `bio_stage`, que ja chegou como troca de
	/// APARENCIA um instante antes.
	/// </summary>
	public event Action<int, Jandirus.Core.Forms.Cinematicas.CenaBio>? CenaDoBioComecou;

	/// <summary>
	/// A CINEMATICA DA FUSAO COMECOU -- e ela e a UNICA deste jogo com **dois** corpos: quem convidou
	/// (onde a fusao vai nascer) e quem aceitou.
	///
	/// Irmao do <see cref="CenaDoBioComecou"/> e pelo mesmo argumento escrito la: e ACONTECIMENTO de
	/// zona, sem estado a sincronizar. Ver `Protocol.S2C.CenaDeFusao` sobre por que ele nao cabe no
	/// canal de forma.
	/// </summary>
	public event Action<int, int>? CenaDeFusaoComecou;

	/// <summary>
	/// EM QUE MACACO **EU** ESTOU. Espelho local do ultimo <see cref="Protocol.S2C.Oozaru"/> que veio
	/// com o meu id -- o servidor continua sendo a autoridade, isto aqui e leitura pra a tela.
	///
	/// ============================ POR QUE UM CAMPO, E NAO UMA DERIVACAO ============================
	/// A regra do projeto e derivar antes de criar campo, e eu procurei de onde derivar: o
	/// `AtributosState` carrega `FormaAtual` e as maestrias, mas NAO o Oozaru -- e nao carrega de
	/// proposito, porque o Oozaru e estado PARALELO a escada (ver o cabecalho de `Core.Forms.Oozaru`)
	/// e a ficha lenta chega de tres em tres segundos, tarde demais pra um botao que some no instante
	/// em que a fera nasce. O `S2C.Oozaru` e a unica fonte, e ela e um EVENTO: quem chega depois nao
	/// pode perguntar. Guardar o ultimo valor e o minimo pra o botao da lua e o "Voltar ao normal"
	/// existirem sem cada um manter a sua propria copia (que foi como duas verdades nasceram antes).
	/// ============================================================================================
	/// </summary>
	public Jandirus.Core.Forms.FormaOozaru MeuOozaru { get; private set; }

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

	/// <summary>
	/// UM CORPO SALTOU: o id, DE ONDE ele saiu, e se ele deixa MIRAGEM.
	///
	/// O terceiro campo separa as duas camadas do mesmo gesto: o BORRAO do deslocamento (que todo
	/// corpo que salta ganha) e a miragem da Afterimage (que so quem tem a skill deixa). Ver
	/// `Protocol.S2C.Zanzo` e `World.AoPiscar`.
	/// </summary>
	public event Action<int, Vec2, bool>? Piscou;

	// =====================================================================
	// OS ATAQUES DE KI
	// =====================================================================
	/// <summary>
	/// ONDE ESTAO OS TIROS AGORA -- a lista inteira, a cada snapshot. Nao e delta: sao poucos e a
	/// posicao deles e o unico dado que muda, entao a lista cheia e mais barata que sincronizar
	/// diferencas (e nao tem como dessincronizar em silencio).
	/// </summary>
	public event Action<IReadOnlyList<ProjetilState>>? TirosNoAr;

	/// <summary>
	/// UM TIRO NASCEU. Vem no canal confiavel porque e o instante em que ha efeito pra tocar -- e
	/// porque e a unica hora em que o DONO (de quem sai a cor) e a ARTE sao ditos. Ver
	/// <see cref="NascimentoDeProjetil"/>.
	/// </summary>
	public event Action<NascimentoDeProjetil>? TiroNasceu;

	/// <summary>UM TIRO ACABOU: id, o motivo (`Core.Combat.FimDeProjetil`) e onde.</summary>
	public event Action<int, byte, Vec2>? TiroMorreu;

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
	///
	/// ============================ UM PRAZO, E NAO UM BIT ============================
	/// Ele era `{ get; private set; }` -- ligado no `Comecou`, desligado no `Acabou`. Virou um PRAZO
	/// porque agora ele **cala os atalhos do jogador** (ver `Foco.AtalhosMudos`), e um bit que alguem
	/// tem que lembrar de apagar e exatamente como este projeto ja perdeu tres vezes esta semana (a
	/// aureola presa no cadaver, o relogio da morte que nao rearmava, o pedido de musica eterno).
	/// Aqui o preco seria o pior de todos: um `Acabou` que nao chegasse -- morte, nocaute, troca de
	/// zona, logout do outro, um pacote perdido numa desconexao -- deixaria o teclado MUDO pra sempre.
	///
	/// O `Comecou` ja diz quanto o embate dura (e no de ki, o teto de 15 s), entao o fim tem hora
	/// marcada: o silencio morre sozinho mesmo que ninguem o mate. O `Acabou` continua chegando e
	/// continua mandando -- ele so deixou de ser a UNICA forma de sair daqui.
	/// ===============================================================================
	/// </summary>
	public bool EmClash => _clashAte > 0 && Time.GetTicksMsec() < _clashAte;

	/// <summary>Ate quando o embate pode durar, no relogio local. 0 = nao ha embate.</summary>
	private ulong _clashAte;

	/// <summary>
	/// A FOLGA DO PRAZO. O `ms` do `Comecou` e o relogio do SERVIDOR; daqui ate la ha a ida do
	/// pacote, o tique de 30 Hz que fecha o embate e a volta do `Acabou`. Dois segundos cobrem isso
	/// com sobra sem deixar o silencio arrastar depois da cena.
	/// </summary>
	private const ulong FolgaDoEmbate = 2_000;

	/// <summary>
	/// Comecou: QUE embate (ver <see cref="Protocol.TipoDeEmbate"/>), eu, o outro, quantos ms dura,
	/// e QUANTO VALE cada acerto de cada um.
	///
	/// A vantagem de poder viaja porque o jogador precisa dela pra ler a propria situacao: num
	/// encontro desigual o mais fraco pode acertar todas as letras e ainda perder, e descobrir isso
	/// so no fim seria o quick time event mentindo sobre o que estava sendo disputado.
	///
	/// O TIPO viaja pelo mesmo motivo, e e a unica coisa que separa o ZanzoClash da colisao de ki
	/// nesta ponta: a mecanica e identica (letra, prazo, cabo de guerra) e o que muda e o que a tela
	/// escreve. Ver o cabecalho de `Protocol.TipoDeEmbate`.
	/// </summary>
	public event Action<Protocol.TipoDeEmbate, int, int, int, float, float>? ClashComecou;

	/// <summary>Aperte ESTA letra, dentro deste prazo (ms).</summary>
	public event Action<char, int>? ClashTeclaPedida;

	/// <summary>O placar, do meu ponto de vista: meus pontos, os dele.</summary>
	public event Action<float, float>? ClashPlacar;

	/// <summary>Os dois se cruzaram AQUI. E o unico sinal visivel do embate.</summary>
	public event Action<Vec2>? ClashBaque;

	/// <summary>Acabou: quem venceu, quem perdeu. Os DOIS zerados = empate (so a colisao de ki).</summary>
	public event Action<int, int>? ClashAcabou;

	/// <summary>
	/// <summary>
	/// A LETRA QUE EU APERTEI ERA A CERTA (true) OU NAO (false).
	///
	/// Vem do servidor porque so ele julga -- e ele julga tambem o PRAZO, que daqui nao se ve.
	/// Ver <see cref="Protocol.ClashSub.Julgou"/>.
	/// </summary>
	public event Action<bool>? ClashJulgou;

	/// <summary>
	/// UM VISLUMBRE: apareci (true) ou sumi de novo (false), e PRA ONDE eu estou virado.
	///
	/// So o corpo LOCAL precisa disto -- os outros aparecem sozinhos, pelo bit `Oculto` do snapshot.
	///
	/// A DIRECAO VEM JUNTO porque o corpo local desenha o olhar que o TECLADO manda, e num embate
	/// quem manda e o servidor: sem ela, eu socava pro lado que estava segurando e o adversario
	/// socava pro lado certo. Ver `GameServer.MandarVislumbre`.
	/// </summary>
	public event Action<bool, Facing>? ClashVislumbre;

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
				// NINGUEM CONTINUA MACACO DESLOGADO -- `ServerPlayer.Oozaru` e estado VIVO e nasce
				// `Nao` a cada entrada (`GameServer.cs:77`). Sem zerar aqui, trocar de personagem
				// dentro da mesma sessao herdaria a fera do anterior e o botao da lua ficaria escondido
				// pra um corpo que nunca se transformou.
				MeuOozaru = Jandirus.Core.Forms.FormaOozaru.Nao;
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
				// A ORDEM DE LEITURA E A DE ESCRITA, e o tipo da fusao e o ULTIMO -- ver
				// `GameServer.TrocarAparencias`. Ler antes da aparencia embaralharia o pacote inteiro.
				Jandirus.Core.Appearance.Appearance ap = reader.GetAppearance();
				// ZERO NAO E UM TIPO, e virar nulo AQUI e o que impede o resto do cliente de carregar um
				// `TipoDeFusao` invalido: os valores sao os `FType` do DM (1, 2, 3) e nao ha o 0.
				byte tipoFus = reader.GetByte();
				PeerLooked?.Invoke(quem, nome, raca, genero, ap,
					tipoFus == 0 ? null : (Jandirus.Core.Social.TipoDeFusao)tipoFus);
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

				// O SEGUNDO BLOCO -- os ataques de ki no ar. Ele SEMPRE vem, mesmo vazio (ver
				// `GameServer.EscreverProjeteis`): um bloco opcional sem marcador desalinharia o
				// resto do pacote em silencio.
				int t = reader.GetUShort();
				_scratchTiros.Clear();
				for (int i = 0; i < t; i++) _scratchTiros.Add(ProjetilState.Read(reader));

				SnapshotReceived?.Invoke(_scratch);
				TirosNoAr?.Invoke(_scratchTiros);
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

			// UM QUADRO DE VOZ. Ele so chega porque o servidor JA decidiu que eu posso ouvir -- nao ha
			// nada a filtrar aqui, e nao pode haver: um filtro no cliente seria a admissao de que o
			// pacote chega em quem nao devia. Ver `GameServer.Voz.cs`.
			case Protocol.S2C.Voz:
			{
				int quem = reader.GetInt();
				ushort seq = reader.GetUShort();
				byte dist = reader.GetByte();
				bool parede = reader.GetByte() != 0;
				byte tam = reader.GetByte();
				// TAMANHO MENTIROSO E DESCARTE, antes de ler: o `GetBytes` estouraria e a excecao
				// viraria uma linha de log por quadro -- 50 por segundo por falante.
				if (tam == 0 || tam > Jandirus.Core.Social.VozLocal.MaxBytesDeQuadro
					|| reader.AvailableBytes < tam) break;
				reader.GetBytes(_vozEntrando, tam);
				VozRecebida?.Invoke(quem, seq, dist, parede, _vozEntrando, tam);
				break;
			}

			// UM EFEITO CAIU (ou saiu de cima) DE MIM. Um canal so pra todas as tecnicas: id do
			// efeito + por quantos ms (0 = acabou, negativo = enquanto durar). Ver
			// `GameServer.Tecnicas.cs`; a alternativa era um pacote por tecnica, e sao 47.
			case Protocol.S2C.Zanzo:
			{
				int quem = reader.GetInt();
				Vec2 de = reader.GetVec();
				// A ORDEM DE LEITURA E A DE ESCRITA: id, origem, e SO ENTAO o bool. Ler o bool
				// dentro do `Invoke` junto com o `GetVec` deixaria a ordem dos argumentos decidir
				// a ordem dos bytes -- que e o tipo de dependencia que so aparece quando alguem
				// reordena os parametros do evento.
				Piscou?.Invoke(quem, de, reader.GetBool());
				break;
			}

			// UM ATAQUE DE KI NASCEU OU MORREU. So os dois instantes -- a posicao vem no snapshot.
			case Protocol.S2C.Projetil:
			{
				var sub = (Protocol.ProjetilSub)reader.GetByte();
				int tiro = reader.GetInt();
				if (sub == Protocol.ProjetilSub.Nasceu)
				{
					int dono = reader.GetInt();
					byte tipo = reader.GetByte();
					ushort arte = reader.GetUShort();
					float escala = Protocol.DeEscalaDeProjetil(reader.GetByte());
					// A ORDEM E A DO `AnunciarProjetil`: escala, altura, posicao.
					float altura = Jandirus.Core.World.Voo.DeByte(reader.GetByte());
					TiroNasceu?.Invoke(new NascimentoDeProjetil(
						tiro, dono, tipo, arte, escala, altura, reader.GetVec()));
				}
				else
				{
					byte fim = reader.GetByte();
					TiroMorreu?.Invoke(tiro, fim, reader.GetVec());
				}
				break;
			}

			// OS EMBATES (ZanzoClash e colisao de ki), os momentos num opcode so. Ver `Protocol.ClashSub`.
			case Protocol.S2C.Clash:
			{
				switch ((Protocol.ClashSub)reader.GetByte())
				{
					case Protocol.ClashSub.Comecou:
					{
						var tipo = (Protocol.TipoDeEmbate)reader.GetByte();
						int a = reader.GetInt(), b = reader.GetInt(), ms = reader.GetInt();
						float meu = reader.GetFloat(), dele = reader.GetFloat();
						// SO CHEGA A QUEM ESTA NO EMBATE: e um pacote pessoal, e o `a` sou eu.
						// O PRAZO NASCE AQUI. Ver `EmClash` -- e ele que garante que o silencio dos
						// atalhos acaba mesmo que o `Acabou` nunca chegue.
						_clashAte = Time.GetTicksMsec() + (ulong)Math.Max(ms, 0) + FolgaDoEmbate;
						ClashComecou?.Invoke(tipo, a, b, ms, meu, dele);
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
					{
						bool aparece = reader.GetBool();
						ClashVislumbre?.Invoke(aparece, (Facing)reader.GetByte());
						break;
					}

					case Protocol.ClashSub.Julgou:
						ClashJulgou?.Invoke(reader.GetBool());
						break;

					case Protocol.ClashSub.Acabou:
					{
						int venc = reader.GetInt(), perd = reader.GetInt();
						// O EMBATE ACABOU ANTES DA HORA: o prazo cai agora. E o pacote e PESSOAL
						// (`Terminar` e `Anunciar` mandam so aos dois), senao o fim do embate alheio na
						// mesma zona destravaria o teclado de quem ainda esta preso no seu.
						_clashAte = 0;
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

			// OS CHEFES QUE EU JA VI. Lista inteira, como os estilos -- ver `S2C.MenteChefes`.
			case Protocol.S2C.MenteChefes:
			{
				int n = reader.GetByte();
				var l = new List<ChefeVisto>(n);
				for (int i = 0; i < n; i++)
					l.Add(new ChefeVisto(reader.GetString(64), reader.GetString(64)));
				ChefesVistos = l;
				ChefesVistosMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Customizadas:
			{
				int n = reader.GetByte();
				var l = new List<Jandirus.Core.Skills.TecnicaCustomizada>(n);
				for (int i = 0; i < n; i++) l.Add(Jandirus.Net.CustomWire.Ler(reader));
				Customizadas = l;
				// A MESA VEM NO MESMO PACOTE, e o `bool` na frente e o que separa "nao ha rascunho"
				// de "o rascunho e o primeiro da lista". Ver `S2C.Customizadas`.
				Mesa = reader.GetBool() ? Jandirus.Net.CustomWire.Ler(reader) : null;
				CustomizadasMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Construcoes:
			{
				int n = reader.GetUShort();
				var l = new List<ObraInfo>(n);
				for (int i = 0; i < n; i++)
					l.Add(new ObraInfo(reader.GetInt(), reader.GetString(48), reader.GetString(64),
						new Vector2(reader.GetFloat(), reader.GetFloat()),
						reader.GetBool(), reader.GetByte(), reader.GetString(32),
						reader.GetString(160), reader.GetString(48),
						new Vector2(reader.GetFloat(), reader.GetFloat()), reader.GetBool()));
				Obras = l;
				ObrasMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Esferas:
			{
				int n = reader.GetUShort();
				var l = new List<EsferaInfo>(n);
				for (int i = 0; i < n; i++)
					l.Add(new EsferaInfo(reader.GetInt(), (Protocol.CoisaDeEsfera)reader.GetByte(),
						reader.GetByte(), new Vector2(reader.GetFloat(), reader.GetFloat()),
						reader.GetBool(), reader.GetString(16)));
				Esferas = l;
				EsferasMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.SuperEsferas:
			{
				int n = reader.GetByte();
				var l = new List<SuperInfo>(n);
				for (int i = 0; i < n; i++)
					l.Add(new SuperInfo(reader.GetByte(),
						new Vector2(reader.GetFloat(), reader.GetFloat()),
						reader.GetString(24), reader.GetBool()));
				Supers = l;
				MinhasSupers = reader.GetByte();
				SinalDourado = reader.GetString(120);
				SupersMudaram?.Invoke();
				break;
			}

			// O REFUGIO: o planeta natal acabou, e estas sao as duas saidas. Ver `S2C.Refugio`.
			case Protocol.S2C.Refugio:
			{
				RefugioPrecisa = reader.GetBool();
				bool abrir = reader.GetBool();
				RefugioNatal = reader.GetString(32);

				int nd = reader.GetByte();
				var dominios = new List<RefugioDominio>(nd);
				for (int i = 0; i < nd; i++)
					dominios.Add(new RefugioDominio(reader.GetString(48), reader.GetString(32),
													reader.GetBool(), reader.GetFloat()));
				RefugioDominios = dominios;

				int nv = reader.GetByte();
				var vizinhos = new List<RefugioVizinho>(nv);
				for (int i = 0; i < nv; i++)
					vizinhos.Add(new RefugioVizinho(reader.GetString(32), reader.GetFloat(),
													reader.GetFloat(), reader.GetBool()));
				RefugioVizinhos = vizinhos;

				RefugioReserva = reader.GetBool();

				RefugioMudou?.Invoke();
				if (abrir) RefugioPediuAbrir?.Invoke();
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
						reader.GetDouble(), reader.GetDouble(), reader.GetByte(),
						reader.GetString(160), reader.GetString(48)));
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

			// UM DECALQUE NO CHAO, vindo do servidor. Hoje so o rastro de arremesso vem por aqui --
			// os outros o cliente decide sozinho. Ver `Client/Decalques.cs`.
			case Protocol.S2C.Decalque:
			{
				var tipo = (Protocol.Decal)reader.GetByte();
				Vec2 onde = reader.GetVec();
				var dir = (Facing)reader.GetByte();
				// O BYTE SO EXISTE PRO MEMBRO ARRANCADO -- mesma condicao do escritor
				// (`GameServer.MandarDecalque`). Ler incondicionalmente estouraria o buffer nos
				// outros sete tipos.
				PecaDeCorpo peca = tipo == Protocol.Decal.Membro
					? (PecaDeCorpo)reader.GetByte() : PecaDeCorpo.Nenhuma;
				DecalqueCaiu?.Invoke(tipo, onde, dir, peca);
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
					lista.Add(new CargoInfo(reader.GetString(32), reader.GetString(32), reader.GetString(160),
											reader.GetString(200), reader.GetString(400)));
				Cargos = lista;
				CargosMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Conhecidos:
			{
				int n = reader.GetUShort();
				PedidoDeAmizade = reader.GetString(64);
				var lista = new List<ConhecidoInfo>(n);
				for (int i = 0; i < n; i++)
					lista.Add(new ConhecidoInfo(
						reader.GetString(96), reader.GetString(64), reader.GetString(32),
						reader.GetString(48), reader.GetUShort(), reader.GetByte(),
						reader.GetFloat(), reader.GetFloat(), reader.GetBool()));
				Conhecidos = lista;
				ConhecidosMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Sentidos:
			{
				// A LISTA INTEIRA, como os conhecidos: o servidor so a manda quando ela muda, e ela SUBSTITUI
				// a anterior -- quem saiu do alcance simplesmente nao esta mais nela.
				(bool scan, List<Protocol.PresencaState> lista) = reader.GetSentidos();
				SentidosSaoDoScouter = scan;
				Sentidos = lista;
				SentidosMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Feridas:
			{
				int quem = reader.GetInt();
				Feridas[quem] = reader.GetFeridas();
				FeridasMudaram?.Invoke(quem);
				break;
			}

			case Protocol.S2C.Aureola:
			{
				int quem = reader.GetInt();
				if (reader.GetBool()) ComAureola.Add(quem);
				else ComAureola.Remove(quem);
				AureolaMudou?.Invoke(quem);
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

			case Protocol.S2C.Limpeza:
			{
				string codigo = reader.GetString(16);
				int segundos = reader.GetUShort();
				int n = reader.GetByte();
				var linhas = new List<string>(n);
				// O TETO DE 200 E O MESMO DO LADO DE LA (as linhas sao contagens curtas). Ler sem
				// teto seria confiar num numero que veio do fio -- e este pacote so chega pra admin,
				// mas "so chega pra admin" nunca foi uma garantia de tamanho.
				for (int i = 0; i < n; i++) linhas.Add(reader.GetString(200));
				Limpeza = new PreviaDeLimpeza(codigo, segundos, linhas);
				LimpezaMudou?.Invoke();
				break;
			}

			case Protocol.S2C.Forma:
			{
				int quem = reader.GetInt();
				int de = reader.GetUShort();
				int para = reader.GetUShort();
				// O BYTE VIRA `DegrauDeCena` AQUI e so aqui -- a mesma escolha do `S2C.Oozaru` logo
				// abaixo, e pelo mesmo motivo: um byte solto obrigaria cada ouvinte a lembrar o que 0,
				// 1 e 2 querem dizer. Valor desconhecido cai em `Nenhuma`, que e o certo pra um pacote
				// de estado: melhor uma transformacao sem cena do que um corpo preso por uma cena que
				// este binario nao sabe tocar.
				byte db = reader.GetByte();
				var degrau = Enum.IsDefined(typeof(Jandirus.Core.Forms.DegrauDeCena), db)
					? (Jandirus.Core.Forms.DegrauDeCena)db
					: Jandirus.Core.Forms.DegrauDeCena.Nenhuma;
				// O BIT DO DOMINIO vem depois do degrau. Ele NAO entrou no byte de cima justamente
				// por causa do `Enum.IsDefined` tres linhas acima: um bit alto ali derrubaria o
				// pacote inteiro pra `Nenhuma`. Ver `GameServer.PacoteDeForma`.
				bool dominada = reader.GetBool();
				FormaMudou?.Invoke(quem, de, para, degrau, dominada);
				break;
			}

			case Protocol.S2C.Oozaru:
			{
				int quem = reader.GetInt();
				// O BYTE VIRA `FormaOozaru` AQUI e so aqui. Um valor que o enum nao conheca cai em
				// `Nao` -- e o certo pra um pacote de estado: melhor um corpo que voltou ao normal
				// do que um macaco meio desenhado.
				byte b = reader.GetByte();
				var forma = Enum.IsDefined(typeof(Jandirus.Core.Forms.FormaOozaru), b)
					? (Jandirus.Core.Forms.FormaOozaru)b
					: Jandirus.Core.Forms.FormaOozaru.Nao;
				// GUARDA ANTES DE AVISAR: quem escuta o evento pode consultar o estado no mesmo
				// quadro, e um ouvinte que lesse `MeuOozaru` velho desenharia o quadro anterior.
				if (quem == LocalId) MeuOozaru = forma;
				bool estreou = reader.GetBool();
				// O DEGRAU TAMBEM AQUI, e pelo mesmo motivo do `S2C.Forma` logo acima. Ele existe pra
				// um caso so e ele e o que evita o remedio virar doenca: quando eu ENTRO numa zona onde
				// alguem ja e macaco, o servidor manda `Nenhuma` e a fera aparece pronta -- sem eu
				// assistir, preso, a uma transformacao que aconteceu antes de eu chegar.
				byte gb = reader.GetByte();
				var grau = Enum.IsDefined(typeof(Jandirus.Core.Forms.DegrauDeCena), gb)
					? (Jandirus.Core.Forms.DegrauDeCena)gb
					: Jandirus.Core.Forms.DegrauDeCena.Nenhuma;
				OozaruMudou?.Invoke(quem, forma, estreou, grau);
				break;
			}

			// A FURIA IRROMPEU EM ALGUEM. Um id e mais nada -- ver `Protocol.S2C.Furia` sobre por que o
			// pacote e vazio e por que ele nao e o `S2C.Efeito`.
			case Protocol.S2C.Furia:
				FuriaIrrompeu?.Invoke(reader.GetInt());
				break;

			// UMA CENA DO BIO-ANDROIDE COMECOU. Quem, e qual -- ver `Protocol.S2C.CenaDoBio`.
			//
			// O BYTE VIRA O ENUM AQUI E SEM CRIVO: o `Cinematicas.DoBio` ja trata o valor que ele nao
			// conhece devolvendo nulo (silencio), que e a resposta certa pra um servidor mais novo que
			// este cliente. Recusar aqui daria o mesmo resultado com um `if` a mais.
			case Protocol.S2C.CenaDoBio:
			{
				int quemNoBio = reader.GetInt();
				CenaDoBioComecou?.Invoke(
					quemNoBio, (Jandirus.Core.Forms.Cinematicas.CenaBio)reader.GetByte());
				break;
			}

			// A CINEMATICA DA FUSAO COMECOU. Dois ids, na ordem do dono do jogo: quem convidou e quem
			// aceitou -- ver `Protocol.S2C.CenaDeFusao`.
			//
			// AS DUAS LEITURAS SAO INCONDICIONAIS e vem antes de qualquer decisao: o `reader` e
			// sequencial, e sair no meio deixaria quatro bytes no buffer pro proximo pacote ler como
			// se fossem dele. E a mesma razao pela qual o caso do bio le os dois campos antes de
			// perguntar qualquer coisa.
			case Protocol.S2C.CenaDeFusao:
			{
				int donoDaFusao = reader.GetInt();
				int passageiroDaFusao = reader.GetInt();
				CenaDeFusaoComecou?.Invoke(donoDaFusao, passageiroDaFusao);
				break;
			}

			case Protocol.S2C.Skills:
			{
				MarcosTotais = reader.GetInt();
				MarcosLivres = reader.GetInt();
				// O bit de VILAO -- ver `GameServer.MandarSkills`. Ele decide se o menu desenha o
				// Planet Destroy como comprável ou como "so um vilao aprende isso".
				SouVilao = reader.GetBool();
				int quantas = reader.GetUShort();
				SkillsAprendidas.Clear();
				for (int i = 0; i < quantas; i++) SkillsAprendidas.Add(reader.GetString(96));
				// A CAUDA: o estado das arvores. Lida pelo mesmo leitor que a bancada do servidor usa
				// pra desmontar o pacote -- uma leitura so, pra que "o cliente le" e "o servidor
				// escreveu" nunca sejam duas versoes do mesmo layout.
				(SkillsDestravadas, SkillsArvores, VerbosAtivos) = Protocol.LerEstadoDeSkills(reader);
				SkillsMudaram?.Invoke();
				break;
			}

			case Protocol.S2C.Mortos:
			{
				int n = reader.GetByte();
				var lista = new List<Jandirus.Core.World.EstadoDaMorte>(n);
				for (int i = 0; i < n; i++)
					lista.Add(new Jandirus.Core.World.EstadoDaMorte
					{
						Chave = reader.GetString(64),
						Nome = reader.GetString(64),
						Fase = (Jandirus.Core.World.FaseDaMorte)reader.GetByte(),
						Estagio = reader.GetByte(),
						Faltam = reader.GetDouble(),
					});

				AplicarMortos(lista);
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

			case Protocol.S2C.Inventario:
				Mochila = reader.GetInventario();
				MochilaMudou?.Invoke();
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
			// ONDE ESTA A MINHA NAVE -- ver `Protocol.S2C.Nave` e `NaveVista`.
			case Protocol.S2C.Nave:
				NaveVista = new NaveNoMapa(new Vector2(reader.GetFloat(), reader.GetFloat()),
										   reader.GetString(48), reader.GetFloat());
				break;

			// QUAL NAVE ESTA EMBAIXO DE MIM -- o alvo da tecla E enquanto se pilota. Vazio = nenhuma.
			case Protocol.S2C.Veiculo:
				VeiculoMontado = reader.GetString(48);
				NomeDoVeiculo = reader.GetString(64);
				break;

			// QUAL CORPO ESTA AOS MEUS PES -- o alvo da tecla E pra enterrar. Zero = nenhum.
			case Protocol.S2C.Cadaver:
				CadaverPerto = reader.GetInt();
				NomeDoCadaver = reader.GetString(64);
				break;

			case Protocol.S2C.ZoneChanged when LimparCenario():
			{
				Zone = reader.GetZone();
				Vec2 spawn = reader.GetVec();

				// SAIU DA NAVE, A MARCACAO MORRE. Quem apaga e o cliente e nao o servidor, e ele
				// apaga AQUI porque este e o instante exato em que ele deixa de estar a bordo --
				// ver o comentario do opcode. Sem isto, a carta continuaria centrada num casco que
				// ficou pra tras (e, pior, num que pode ter explodido).
				if (!Jandirus.Core.Tech.NaveGrande.EhInterior(Zone, out _)) NaveVista = null;

				ZoneChanged?.Invoke(Zone, spawn);
				break;
			}
		}
	}
}
