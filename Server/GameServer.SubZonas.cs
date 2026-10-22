using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// AS SUB-ZONAS DE UM PLANETA, LADO DO SERVIDOR -- o Lookout e as cavernas sao o planeta delas pro ceu, pro clima e pro fim do mundo, e
/// do Lookout se sobe pro espaco. A regra pura (que sub-zona e de que planeta, e o castelo do Rei Vegeta no canto do Lookout) mora em
/// <see cref="SubZonas"/>; aqui ela vira plateia, pacote e porta.
///
/// O dono (2026-10-21): *"sincronizar o horario, clima e efeitos de destruiçao de planetas em sub zonas do planeta, como cavernas do
/// planeta vegeta com o proprio planeta, lookout com a terra . e pode ir pro espaço do proprio lookout pois é considerado terra, as
/// cavernas de vegeta ainda nao dariam pelo fato de estarem em uma caverna e nao ceu a berto, e obviamente n da pra voar pro espaço pela
/// sala do tempo"*. Sobre o fim do mundo, perguntado: *"Efeitos e morte"*. E a segunda metade do pedido, a trilha: *"enquanto vc estiver
/// em um planeta q esta explodindo, a trilha sonora do jogo vai ser a mesma q toca ao entrar em combate, o mesmo vale quando vc ta em
/// um planeta q ta com boss de saga ativo"* -- o servidor diz ao cliente onde ha chefe (<see cref="MandarChefesNosPlanetas"/>), e o
/// cliente decide a musica (`World.SubZonas.cs`).
///
/// ============================ O QUE MUDA, PORTA A PORTA ============================
///   * HORA E LUA: o relogio de cada corpo e o do ceu de onde ele pisa (<see cref="RelogioDoLugar"/>) -- o anuncio da lua, o Oozaru
///     dos NPCs e os verbs de admin do ceu. A ficha sem corpo ja vem do planeta pelo Core (`Ceu.RelogioDaZona`).
///   * CLIMA: o forcado e guardado na chave do PLANETA (`ForcarClima`), anunciado a quem esta debaixo dele (<see cref="PlateiaDoCeu"/>),
///     mandado a quem chega pelo ceu do lugar (`MandarClima`) e reenviado a quem cruza a fronteira do castelo dentro do Lookout
///     (<see cref="TickDasSubZonas"/>). O raio cai por ceu (`TickDoRaio`). A natural ja vem do planeta pelo Core (`Clima.DaZona`).
///   * FIM DO MUNDO: o pavio, o tremor, os avisos, o chao que racha e o commit varrem a <see cref="PlateiaDoCeu"/> -- quem esta na
///     caverna ou no Lookout morre junto, ou sobe pro espaco com quem sobreviveu no chao. Quem chega numa sub-zona de um mundo que ja
///     morreu e rebocado pro espaco (<see cref="TickDasSubZonas"/>); quem acorda la vai pro berco (`OndeEsteCorpoPodeAcordar`).
///   * ESPACO: do Lookout se decola pelo planeta do ceu dele (<see cref="ChaoDaDecolagem"/>); da caverna e da Sala do Tempo, nao
///     (<see cref="SemCeuPraRomper"/>), e a recusa vem ANTES de o voo cair.
///   * A SALA DO TEMPO FICA DE FORA DE TUDO: e outra dimensao (`Planet = "Hyperbolic Time Dimension"`, `Areas.dm:276-285`).
/// ===================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// OS DEFEITOS INJETADOS -- cada um e o jogo de antes de 2026-10-21, lido pela propria linha da regra
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste`): o ceu forcado e o fim do mundo veem SO A ZONA -- o temporal da Terra nao chega no
	/// Lookout, a explosao nao alcanca a caverna, e quem esta la sai ileso. Lido por <see cref="PlateiaDoCeu"/>, <see cref="PlanetaDoCorpo"/>,
	/// pelo `ForcarClima` e pelo `ClimaAgora`. Falso em jogo, sempre.
	/// </summary>
	internal static bool PlateiaSoDaZonaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste` e robo `--diagsubzonas`): do Lookout nao se sobe pro espaco -- "nao fica no mapa do
	/// universo". Lido por <see cref="ChaoDaDecolagem"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool LookoutSemEspacoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste`): a caverna e a Sala do Tempo nao recusam a subida -- e a caverna, que e do planeta,
	/// subiria pro espaco dele. Lido por <see cref="SemCeuPraRomper"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool CavernaRompeOCeuDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste`): a sub-zona de um planeta que ja explodiu continua de pe pra quem chega depois -- quem
	/// sai da Sala do Tempo pisa no Lookout de uma Terra que nao existe. Lido por <see cref="SubZonaDeMundoMorto"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool SubZonaMortaAbertaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste` e robo `--diagsubzonas`): o servidor nao conta a ninguem onde ha chefe de saga no
	/// mundo -- o cliente nao tem como tocar a trilha de combate por ele. Lido por <see cref="PlanetasComChefe"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool ChefesCaladosDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste`): quem cruza a fronteira do castelo dentro do Lookout fica com o ceu forcado do lado de
	/// onde veio. Lido pelo <see cref="TickDasSubZonas"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool ClimaNaoSegueOPeDeTeste;

	// =====================================================================
	// AS ESCUTAS DA BANCADA -- nulas em jogo
	// =====================================================================
	/// <summary>Cada ceu forcado mandado: pra quem, de que ceu (a chave do planeta), e o tipo. Ver `AnunciarClima` e `MandarClima`.</summary>
	internal static List<(int Quem, ulong Ceu, TipoDeClima Tipo)>? EscutaDoClimaMandado;

	/// <summary>Cada descarga: perto de quem, e de que ceu. Ver `RaioNoCeu`.</summary>
	internal static List<(int Perto, ulong Ceu)>? EscutaDosRaios;

	/// <summary>Cada lista de planetas com chefe mandada: pra quem, e as chaves. Ver <see cref="MandarChefesNosPlanetas"/>.</summary>
	internal static List<(int Para, string[] Chaves)>? EscutaDosChefesNosPlanetas;

	// =====================================================================
	// O CEU DE UM CORPO
	// =====================================================================
	/// <summary>
	/// DE QUE ZONA E O CEU DE ONDE ESTE CORPO PISA -- o planeta da sub-zona, Vegeta no castelo do Lookout, ou a propria zona. A pergunta
	/// pura, pro relogio e pra lua.
	/// </summary>
	private static ZoneKey ZonaDoCeuDe(ServerPlayer pl) => SubZonas.ZonaDoCeu(pl.Zone, pl.Pos);

	/// <summary>
	/// O PLANETA DESTE CORPO, pro ceu forcado e pro fim do mundo: o mesmo que <see cref="ZonaDoCeuDe"/>, com o defeito injetado do jogo de
	/// antes (<see cref="PlateiaSoDaZonaDeTeste"/>), em que cada zona era so ela.
	/// </summary>
	private ZoneKey PlanetaDoCorpo(ServerPlayer pl) => PlateiaSoDaZonaDeTeste ? pl.Zone : ZonaDoCeuDe(pl);

	/// <summary>
	/// O relogio do ceu de onde este corpo esta. Ver <see cref="ZonaDoCeuDe"/> e `Ceu.RelogioDaZona`.
	///
	/// O defeito <see cref="SubZonas.CeuProprioDeTeste"/> so deixa de remapear DENTRO de `RelogioDaZona`, e so quando a zona
	/// recebida ainda e a sub-zona. No jogo este metodo ja entrega o planeta-mae: sem a pergunta do defeito aqui, a lua cheia
	/// da Terra nasceria no Lookout mesmo com o defeito ligado, e a regua B3 passaria com esta linha apagada.
	/// </summary>
	public RelogioDoPlaneta RelogioDoLugar(ServerPlayer pl) =>
		RelogioDaZona(SubZonas.CeuProprioDeTeste ? pl.Zone : ZonaDoCeuDe(pl));

	/// <summary>
	/// QUEM ESTA DEBAIXO DO CEU DESTE PLANETA: a zona dele e quem esta nas sub-zonas dele debaixo do mesmo ceu (no Lookout, pela posicao
	/// -- o castelo e de Vegeta). E a plateia do clima forcado e do fim do mundo. Lista NOVA a cada chamada: quem a varre pode tirar gente
	/// da zona no meio (a morte, a evacuacao).
	/// </summary>
	private List<ServerPlayer> PlateiaDoCeu(ZoneKey ceu)
	{
		var plateia = new List<ServerPlayer>(ZoneList(ceu.Hash));
		if (PlateiaSoDaZonaDeTeste) return plateia;
		foreach (ZoneKey sub in SubZonas.SubZonasDe(ceu))
			foreach (ServerPlayer p in ZoneList(sub.Hash))
				if (ZonaDoCeuDe(p).Equals(ceu)) plateia.Add(p);
		return plateia;
	}

	// =====================================================================
	// O ESPACO
	// =====================================================================
	/// <summary>
	/// DE QUE PLANETA SE DECOLA DAQUI: o do ceu do lugar -- do Lookout, a Terra (dono: *"pode ir pro espaço do proprio lookout pois é
	/// considerado terra"*), e do castelo do Rei Vegeta, Vegeta. Fora das sub-zonas e a propria zona, e o `Decolar` segue como sempre.
	/// </summary>
	private ZoneKey ChaoDaDecolagem(ServerPlayer pl) => LookoutSemEspacoDeTeste ? pl.Zone : ZonaDoCeuDe(pl);

	/// <summary>
	/// POR QUE DAQUI NAO SE ROMPE O CEU -- a caverna (sem ceu aberto) e a Sala do Tempo (outra dimensao). Vazio = nada a recusar por isto.
	/// A frase e do Core (`SubZonas.PorQueNaoSobe`).
	/// </summary>
	private string SemCeuPraRomper(ServerPlayer pl) => CavernaRompeOCeuDeTeste ? "" : SubZonas.PorQueNaoSobe(pl.Zone);

	/// <summary>
	/// A decolagem de producao (`Decolar`), pro robo `--diagsubzonas`. O atalho `decolar` do cliente nao leva ninguem sem a flag
	/// na lista das bancadas; o voo que rompe a atmosfera chama este mesmo `Decolar`.
	/// </summary>
	internal void DecolarNaFoto(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) Decolar(pl);
	}

	/// <summary>
	/// So o robo `--diagsubzonas`: poe o corpo noutro ponto da MESMA zona pela porta de producao do teleporte (`CravarPosicao`, que
	/// manda a correcao ao cliente) -- a plataforma do Templo e o patio do castelo do Rei Vegeta sao o mesmo mapa.
	/// </summary>
	internal void CravarNaFoto(int id, Vec2 onde)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) CravarPosicao(pl, onde);
	}

	/// <summary>
	/// So o robo `--diagsubzonas`: empurra o relogio do mundo ate a Terra marcar esta hora -- o mesmo `AjustarCeuDaTerra` do
	/// `--horateste` -- e manda a hora nova a todo jogador agora, sem esperar a sincronia do `TickDoCeu`.
	/// </summary>
	internal void HoraDaTerraNaFoto(double hora)
	{
		AjustarCeuDaTerra(hora);
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer != null) MandarCeu(p);
	}

	// =====================================================================
	// O FIM DO MUNDO, DEPOIS DO FIM
	// =====================================================================
	/// <summary>
	/// O PLANETA MORTO DE QUE ESTE CORPO PISA UMA SUB-ZONA -- nulo se ele nao esta numa sub-zona, ou se o planeta dela vive. A caverna e o
	/// Lookout morrem com o planeta (dono, *"Efeitos e morte"*): quem chega la depois do fim nao acha chao. Ver a nota da
	/// `SaidaParaUmMundoMorto` (`GameServer.Destruicao.cs`).
	/// </summary>
	private ZoneKey? SubZonaDeMundoMorto(ServerPlayer pl)
	{
		if (SubZonaMortaAbertaDeTeste) return null;
		return SubZonas.PlanetaMae(pl.Zone, pl.Pos) is { } mae && ZonaMorta(mae) ? mae : null;
	}

	// =====================================================================
	// O TIQUE (1 Hz, do `TickDoCeu`)
	// =====================================================================
	/// <summary>O ceu cujo forcado cada jogador recebeu por ultimo (a chave do planeta). Ver `MandarClima`.</summary>
	private readonly Dictionary<int, ulong> _ceuDoClimaMandado = [];

	/// <summary>A ultima lista de planetas com chefe que saiu, juntada numa linha -- pra so mandar quando muda.</summary>
	private string _chefesMandados = "";

	/// <summary>
	/// AS TRES COISAS DAS SUB-ZONAS QUE ANDAM COM O RELOGIO, a 1 Hz -- pendurado no tique do ceu (`TickDoCeu`), que ja e o dono do relogio
	/// do mundo, pra nao mexer na linha de tiques do `GameServer.cs`:
	///   1. QUEM CRUZOU A FRONTEIRA DO CASTELO dentro do Lookout recebe o ceu forcado do outro planeta -- a unica zona com dois ceus;
	///   2. QUEM ESTA NUMA SUB-ZONA DE UM MUNDO MORTO e rebocado pro espaco em volta dele: chegou depois do fim (a saida da Sala do Tempo, o
	///      voo que entra no castelo de uma Vegeta morta). E o "tique de Stats" do DM pra quem esta numa area destruida (`Stats.dm:427-434`);
	///   3. OS CHEFES DE SAGA NO MUNDO: quando a lista de planetas com chefe muda, ela sai pra todo mundo.
	/// </summary>
	private void TickDasSubZonas()
	{
		if (!ClimaNaoSegueOPeDeTeste)
			foreach (ServerPlayer pl in _players.Values)
				if (pl.Peer != null && SubZonas.TemDoisCeus(pl.Zone)
					&& _ceuDoClimaMandado.TryGetValue(pl.Id, out ulong mandado) && mandado != PlanetaDoCorpo(pl).Hash)
					MandarClima(pl);

		// A FAXINA: quem saiu do mundo nao precisa de entrada (a vaga do id pode voltar com outro corpo, e o login manda de novo).
		if (_ceuDoClimaMandado.Count > _players.Count)
			foreach (int id in _ceuDoClimaMandado.Keys.ToList())
				if (!_players.ContainsKey(id)) _ceuDoClimaMandado.Remove(id);

		foreach (ZoneKey sub in SubZonas.Todas())
			foreach (ServerPlayer pl in ZoneList(sub.Hash).ToList())
			{
				if (pl.Peer == null || pl.Ficha.dead || SubZonaDeMundoMorto(pl) is not { } morto) continue;
				Avisar(pl, $"{NomeDoMundo(morto)} não existe mais -- e {pl.Zone.Name} caiu junto com ele.");
				GD.Print($"[server] {pl.Name} estava em '{pl.Zone.Name}', sub-zona de '{morto.Name}' (DESTRUIDO) -- vai pro espaco");
				EvacuarParaOEspaco(pl, morto);
			}

		AnunciarChefesSeMudou();
	}

	// =====================================================================
	// OS CHEFES DE SAGA NO MUNDO -- `S2C.ChefesNosPlanetas`
	// =====================================================================
	/// <summary>
	/// OS PLANETAS COM CHEFE DE SAGA NO MUNDO AGORA (a fase `NoMundo` da cadeia, mesmo antes de alguem lutar), pela chave
	/// (`ChaveDePlaneta.Texto`), em ordem. O planeta e o de ONDE O CORPO ESTA -- o Cell do torneio na arena da Terra, um chefe que
	/// entrasse numa caverna (o planeta dela) -- e, sem corpo, o do elo. Bills e Whis nao sao chefes de saga (nao estao na cadeia), e o
	/// vilao do Inferno tambem nao conta: o Inferno nao e planeta.
	/// </summary>
	private List<string> PlanetasComChefe()
	{
		var chaves = new List<string>();
		if (ChefesCaladosDeTeste) return chaves;
		foreach (EloDaSaga elo in _cadeia)
		{
			if (_sagas.Do(elo.Id) is not { } est) continue;
			foreach (EstadoDoChefe ec in est.Chefes)
			{
				if (ec.Fase != FaseDoChefe.NoMundo) continue;
				ZoneKey onde = _players.TryGetValue(ec.Corpo, out ServerPlayer? corpo)
					? SubZonas.ZonaDoCeu(corpo.Zone, corpo.Pos)
					: ZoneKey.Premade(elo.Planeta);
				if (ChaveDePlaneta.Da(onde) is { } chave && !chaves.Contains(chave.Texto)) chaves.Add(chave.Texto);
			}
		}
		chaves.Sort(StringComparer.Ordinal);
		return chaves;
	}

	/// <summary>A lista mudou desde o ultimo envio? Entao sai pra todo jogador. Ver <see cref="TickDasSubZonas"/>.</summary>
	private void AnunciarChefesSeMudou()
	{
		List<string> agora = PlanetasComChefe();
		string assinatura = string.Join("|", agora);
		if (assinatura == _chefesMandados) return;
		_chefesMandados = assinatura;
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer != null) MandarChefesNosPlanetas(p, agora);
	}

	/// <summary>
	/// OS PLANETAS COM CHEFE DE SAGA, pra UM jogador -- no login, e sempre que a lista muda (pra todos). LISTA INTEIRA, como o
	/// `S2C.Mortos`: e pequena (um planeta por chefe no mundo) e o cliente decide sozinho se o chao dele esta na lista.
	/// </summary>
	private void MandarChefesNosPlanetas(ServerPlayer pl) => MandarChefesNosPlanetas(pl, PlanetasComChefe());

	private void MandarChefesNosPlanetas(ServerPlayer pl, List<string> chaves)
	{
		var w = Protocol.Begin(Protocol.S2C.ChefesNosPlanetas);
		int n = Math.Min(chaves.Count, 255);
		w.Put((byte)n);
		for (int i = 0; i < n; i++) w.Put(chaves[i]);
		EscutaDosChefesNosPlanetas?.Add((pl.Id, [.. chaves.Take(n)]));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
