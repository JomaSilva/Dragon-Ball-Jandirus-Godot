using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-11-01, o pedido 210) A SAGA SAIYAJIN -- o que ela precisou que a cadeia nao tinha. O dono: *"quando um jogador na terra(apos a saga do
/// freeza no planeta vegeta) bater 500 de bp , raditz ira pousar na terra com sua spacepod fazendk a cratera etc ... quando o raditz perder ele
/// avisara q em 7 dias(do jogo) nappa e vegeta irao pra terra"*, e depois: *"Vegeta n luta e fica so observando o nappa lutar, quando nappa
/// perder ai sim vegeta começa a lutar"*. O resto e DADO (os dois elos e os tres moldes no `npcs.json`); aqui moram as tres pecas novas:
///
///   * A SPACE POD (`ChefeDaSaga.ChegaDePod`): a pod cai num lugar sorteado de terra firme do planeta (a escolha do dono), a pelo menos
///     `PatrulhaGalactica.TilesEntrePods` das que cairam antes (duas crateras uma em cima da outra bugam o desenho), abre a cratera dos pods
///     (`AbrirACrateraDoPod`) e fica no chao como mobilia do mapa; o chefe sai ao lado. So na chegada: quem volta de um reinicio ou do alem
///     nasce como sempre;
///   * A CENA DE QUEM CHEGA DE POD ENTRA EM FILA (`AnunciarChegadaDePod`): o Nappa e o Vegeta chegam na mesma virada do dia, e uma cena nova
///     tira a anterior da tela (`World.AoChegarOChefe`) -- a segunda espera a primeira acabar;
///   * O CHEFE QUE OBSERVA (`ChefeDaSaga.ObservaAteCair`): enquanto o parceiro esta de pe ele nao luta -- fica perto dele, olhando, sem presa
///     -- e NAO SE FERE (`CombatState.NaoSeFere`, a do Whis), senao daria pra mata-lo sem ele revidar. O parceiro caiu: ele entra na luta, com
///     a fala dele, e vira um chefe como os outros (caca o planeta).
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// A SPACE POD
	// =====================================================================
	/// <summary>Onde caiu cada pod de chefe nesta sessao -- a proxima nao cai perto delas. So memoria (a pod e mobilia do mapa, fora do disco).</summary>
	private readonly List<(ZoneKey Zona, Vec2 Onde)> _podsDosChefes = [];

	/// <summary>Quantas pods de chefe cairam neste processo -- so pras bancadas.</summary>
	internal int PodsDeChefeDeTeste;

	/// <summary>
	/// O LUGAR DA POD: sorteado em terra firme da zona (o mesmo sorteio do pouso das naves, `PousoSorteadoEm`), longe das pods de chefe que ja
	/// cairam ali. Nulo sem mapa ou sem lugar -- e o chefe nasce no ponto de sempre, sem pod.
	/// </summary>
	private Vec2? PousoDaPodDoChefe(ZoneKey zona)
	{
		const int T = ZoneCollision.TileSize;
		for (int i = 0; i < 24; i++)
		{
			if (PousoSorteadoEm(zona, naveGrande: false) is not { } p) return null;
			if (!_podsDosChefes.TrueForAll(q => !q.Zona.Equals(zona) || Vec2.Distance(q.Onde, p) >= PatrulhaGalactica.TilesEntrePods * T)) continue;
			_podsDosChefes.Add((zona, p));
			return p;
		}
		return null;
	}

	/// <summary>O ponto livre logo abaixo da pod -- e de la que o chefe sai.</summary>
	private Vec2 AoLadoDaPod(ZoneKey zona, Vec2 pod)
	{
		Vec2 abaixo = pod + new Vec2(0, ZoneCollision.TileSize);
		return MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(abaixo) ?? abaixo;
	}

	/// <summary>
	/// A POD NO CHAO E A CRATERA: a Spacepod como mobilia do mapa (sem dono, aparafusada, fora do disco -- a mesma das naves da Patrulha) e a
	/// cratera dos pods que chegam do espaco, pra zona inteira.
	/// </summary>
	private void PorAPodDoChefe(ZoneKey zona, Vec2 onde)
	{
		var pod = new Obra { Id = _proximaObraId++, Tipo = "Spacepod", DoMapa = true, Aparafusada = true, X = onde.X, Y = onde.Y };
		pod.PorZona(zona);
		_noChao.Add(pod);
		AbrirACrateraDoPod(zona, onde);
		AplicarColisaoDasObras(zona);
		MandarObras(zona);
		PodsDeChefeDeTeste++;
	}

	// =====================================================================
	// A FILA DAS CENAS DE QUEM CHEGA DE POD
	// =====================================================================
	/// <summary>
	/// O INTERVALO ENTRE DUAS CENAS DE CHEGADA: a cena do cliente dura 6,5 s (`CenaDaChegada.Duracao`) e uma nova tira a anterior da tela -- meio
	/// segundo de folga.
	/// </summary>
	internal const double SegundosEntreCenasDePod = 7.0;

	/// <summary>As cenas que esperam a vez (o chefe, o planeta, a frase), na ordem em que chegaram.</summary>
	private readonly List<(ServerPlayer Chefe, string Planeta, string Frase)> _cenasDePodNaFila = [];

	/// <summary>Quando a ultima cena de chegada de pod saiu (ms de parede). A bancada o recua pra nao esperar.</summary>
	internal long UltimaCenaDePodMs = long.MinValue / 2;

	/// <summary>A cena da chegada de quem caiu de pod: sai agora se a ultima ja acabou e ninguem espera; senao entra na fila.</summary>
	private void AnunciarChegadaDePod(ServerPlayer chefe, string planeta, string frase)
	{
		long agora = NowMs();
		if (_cenasDePodNaFila.Count == 0 && agora >= UltimaCenaDePodMs + (long)(SegundosEntreCenasDePod * 1000))
		{
			UltimaCenaDePodMs = agora;
			AnunciarChegadaDoChefe(chefe, planeta, frase);
			return;
		}
		_cenasDePodNaFila.Add((chefe, planeta, frase));
	}

	/// <summary>A proxima cena da fila, quando a vez dela chega (o tique das sagas, 1 Hz). Quem saiu do mundo nesse meio tempo perde a cena.</summary>
	private void TickDasCenasDePod()
	{
		if (_cenasDePodNaFila.Count == 0) return;
		long agora = NowMs();
		if (agora < UltimaCenaDePodMs + (long)(SegundosEntreCenasDePod * 1000)) return;
		(ServerPlayer chefe, string planeta, string frase) = _cenasDePodNaFila[0];
		_cenasDePodNaFila.RemoveAt(0);
		if (!_players.TryGetValue(chefe.Id, out ServerPlayer? ainda) || !ReferenceEquals(ainda, chefe)) return;
		UltimaCenaDePodMs = agora;
		AnunciarChegadaDoChefe(chefe, planeta, frase);
	}

	// =====================================================================
	// O CHEFE QUE OBSERVA
	// =====================================================================
	/// <summary>Um chefe que observa: o corpo (pela REFERENCIA -- id se reusa), o estado do elo dele e a ficha (o parceiro e a fala).</summary>
	private sealed record ObservadorDaSaga(ServerPlayer Corpo, EstadoDoElo Est, ChefeDaSaga Ficha, EloDaSaga Elo);

	/// <summary>Os chefes que estao observando agora, pelo id do corpo. So memoria: depois de um reinicio o nascimento os poe de volta.</summary>
	private readonly Dictionary<int, ObservadorDaSaga> _observadoresDaSaga = [];

	/// <summary>Este corpo e um chefe observando o parceiro agora? So pras bancadas e o palco.</summary>
	internal bool ObservandoDeTeste(int id) =>
		_observadoresDaSaga.TryGetValue(id, out ObservadorDaSaga? o) && _players.TryGetValue(id, out ServerPlayer? c) && ReferenceEquals(o.Corpo, c);

	/// <summary>O parceiro esta de pe (ou ainda chegando)? Caido, consumido, ou fora do estado: nao.</summary>
	private static bool ParceiroDePe(EstadoDoElo est, ChefeDaSaga ficha) =>
		est.Chefes.Find(c => string.Equals(c.Molde, ficha.ObservaAteCair, StringComparison.OrdinalIgnoreCase)) is
			{ Fase: FaseDoChefe.NoMundo or FaseDoChefe.ACaminho };

	/// <summary>
	/// O CHEFE NASCEU (chegando, voltando do reinicio ou do alem): se a ficha dele manda observar e o parceiro esta de pe, ele comeca olhando
	/// -- e intocavel ate o parceiro cair. Chamado pelo `NascerChefeDaSaga`.
	/// </summary>
	private void ComecarAObservarSeFor(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer npc)
	{
		if (Array.Find(elo.Chefes, c => c.Molde == ec.Molde) is not { ObservaAteCair.Length: > 0 } ficha) return;
		if (!ParceiroDePe(est, ficha)) return;
		_observadoresDaSaga[npc.Id] = new ObservadorDaSaga(npc, est, ficha, elo);
		npc.Combate.NaoSeFere = true;
		GD.Print($"[server] SAGA '{elo.Id}': {npc.Name} so observa enquanto '{ficha.ObservaAteCair}' estiver de pe");
	}

	/// <summary>
	/// O TIQUE DA IA DE QUEM OBSERVA -- o ramo dele no `TicarUmCorpo`, antes do habitante. Devolve verdade enquanto ele observa (e ai ja o moveu:
	/// perto do parceiro, do lado de la de quem o parceiro enfrenta, olhando a luta); o parceiro caiu, ele entra na luta e devolve falso, e o
	/// tique segue pelo ramo de chefe de sempre.
	/// </summary>
	private bool ObservandoOParceiro(ServerPlayer npc, double dt)
	{
		if (!_observadoresDaSaga.TryGetValue(npc.Id, out ObservadorDaSaga? o)) return false;
		if (!ReferenceEquals(o.Corpo, npc)) { _observadoresDaSaga.Remove(npc.Id); return false; }
		if (!ParceiroDePe(o.Est, o.Ficha)) { EntrarNaLuta(o); return false; }

		// (2026-11-01, o pedido 214) QUEM FOI PLANTADO ESPERA A VEZ NA FILEIRA AO PE DE QUEM O PLANTOU (os Saibamen ao lado do Nappa), olhando
		// a luta do que esta lutando...
		if (o.Ficha.PlantadoPor.Length > 0 && CorpoDoChefeDoElo(o.Est, o.Ficha.PlantadoPor) is { } plantador && plantador.Zone.Equals(npc.Zone))
		{
			AndarAteAEspera(npc, PontoDaEspera(plantador, VagaDoPlantado(o.Elo, o.Ficha)),
							PlantadoQueLuta(o.Elo, o.Est, o.Ficha.PlantadoPor) ?? plantador, dt, folgaEmTiles: 0.4f);
			return true;
		}
		// ...e QUEM PLANTOU olha a luta do plantado que esta lutando agora (o parceiro dele e o ultimo da fila, que ainda espera ao lado dele).
		if (PlantadoQueLuta(o.Elo, o.Est, o.Ficha.Molde) is { } lutando && lutando.Zone.Equals(npc.Zone))
		{
			FicarOlhando(npc, lutando, dt);
			return true;
		}

		EstadoDoChefe? parceiro = o.Est.Chefes.Find(c => string.Equals(c.Molde, o.Ficha.ObservaAteCair, StringComparison.OrdinalIgnoreCase));
		if (parceiro is not { Fase: FaseDoChefe.NoMundo } || !_players.TryGetValue(parceiro.Corpo, out ServerPlayer? corpo) || !corpo.Zone.Equals(npc.Zone))
		{
			npc.Moving = false;   // o parceiro ainda nao chegou (ou esta noutro lugar): espera onde esta
			return true;
		}
		FicarOlhando(npc, corpo, dt);
		return true;
	}

	/// <summary>A distancia do parceiro em que quem observa fica (tiles), e a partir de quanto ele corre pra la.</summary>
	private const float TilesDeQuemObserva = 5, TilesPraCorrerAteOParceiro = 10;

	/// <summary>
	/// FICA OLHANDO: o ponto e a `TilesDeQuemObserva` tiles do parceiro, do lado OPOSTO ao jogador mais perto dele (atras de quem luta, como o
	/// Vegeta em cima da pedra); sem ninguem perto, a oeste dele. Longe do ponto, anda (correndo se longe); no ponto, para e olha o parceiro.
	/// </summary>
	private void FicarOlhando(ServerPlayer npc, ServerPlayer parceiro, double dt)
	{
		const float T = ZoneCollision.TileSize;
		ServerPlayer? oponente = null;
		float melhor = 30 * T;
		foreach (ServerPlayer p in ZoneList(parceiro.Zone.Hash))
		{
			if (!EhJogador(p) || p.Ficha.dead) continue;
			float d = (p.Pos - parceiro.Pos).Length;
			if (d < melhor) { melhor = d; oponente = p; }
		}
		Vec2 lado = oponente != null && melhor > 1 ? (parceiro.Pos - oponente.Pos).Normalized() : new Vec2(-1, 0);
		Vec2 ponto = parceiro.Pos + lado * (TilesDeQuemObserva * T);

		AndarAteAEspera(npc, ponto, parceiro, dt);
	}

	/// <summary>
	/// ANDA ATE O PONTO DA ESPERA (correndo, se longe) e, nele, para e olha <paramref name="olharPara"/>. O passo de quem observa e, desde o
	/// pedido 214, o de quem espera a vez na fileira ao pe de quem o plantou (com folga menor: a fileira tem um tile e meio entre um e outro).
	/// </summary>
	private void AndarAteAEspera(ServerPlayer npc, Vec2 ponto, ServerPlayer olharPara, double dt, float folgaEmTiles = 1.5f)
	{
		const float T = ZoneCollision.TileSize;
		Vec2 falta = ponto - npc.Pos;
		float dist = falta.Length;
		if (dist <= folgaEmTiles * T)
		{
			npc.Moving = false;
			npc.Correndo = false;
			npc.Ficha.dashing = false;
			Vec2 olhar = olharPara.Pos - npc.Pos;
			if (olhar.LengthSquared > 1) npc.Facing = MoveRules.FacingFrom(olhar, npc.Facing);
			return;
		}
		Vec2 rumo = falta * (1f / dist);
		AplicarComando(npc, new Comando { Rumo = rumo, Olhar = rumo, Correndo = dist > TilesPraCorrerAteOParceiro * T }, dt);
	}

	// =====================================================================
	// (2026-11-01, o pedido 214) OS PLANTADOS -- os Saibamen do Nappa
	// =====================================================================
	/// <summary>O corpo do chefe deste molde, se ele esta no mundo agora.</summary>
	private ServerPlayer? CorpoDoChefeDoElo(EstadoDoElo est, string molde) =>
		est.Chefes.Find(c => string.Equals(c.Molde, molde, StringComparison.OrdinalIgnoreCase)) is { Fase: FaseDoChefe.NoMundo } ec
		&& _players.TryGetValue(ec.Corpo, out ServerPlayer? corpo) ? corpo : null;

	/// <summary>A vaga deste plantado na fileira de quem o plantou (0 = o primeiro) -- a ordem da lista do elo.</summary>
	private static int VagaDoPlantado(EloDaSaga elo, ChefeDaSaga ficha) =>
		Math.Max(0, Array.IndexOf(elo.Chefes.Where(c => string.Equals(c.PlantadoPor, ficha.PlantadoPor, StringComparison.OrdinalIgnoreCase)).ToArray(), ficha));

	/// <summary>
	/// O LUGAR DA ESPERA: uma fileira dois tiles ao sul de quem plantou, um tile e meio entre um e outro, centrada nele -- os seis Saibamen ao
	/// pe do Nappa.
	/// </summary>
	private static Vec2 PontoDaEspera(ServerPlayer plantador, int vaga)
	{
		const float T = ZoneCollision.TileSize;
		return plantador.Pos + new Vec2((vaga - 2.5f) * 1.5f * T, 2 * T);
	}

	/// <summary>Onde brota quem e plantado: na vaga dele da fileira de quem o plantou. Sem quem plantou no mundo (nesta zona), nulo.</summary>
	private Vec2? PontoDoPlantado(EloDaSaga elo, EstadoDoElo est, ChefeDaSaga ficha, ZoneKey zona) =>
		CorpoDoChefeDoElo(est, ficha.PlantadoPor) is { } plantador && plantador.Zone.Equals(zona)
			? AoLadoDaPod(zona, PontoDaEspera(plantador, VagaDoPlantado(elo, ficha))) : null;

	/// <summary>O plantado deste chefe que esta LUTANDO agora (no mundo e sem esperar a vez) -- o primeiro da fileira que ainda esta de pe.</summary>
	private ServerPlayer? PlantadoQueLuta(EloDaSaga elo, EstadoDoElo est, string plantador)
	{
		foreach (ChefeDaSaga f in elo.Chefes)
			if (string.Equals(f.PlantadoPor, plantador, StringComparison.OrdinalIgnoreCase)
				&& CorpoDoChefeDoElo(est, f.Molde) is { } c && !_observadoresDaSaga.ContainsKey(c.Id))
				return c;
		return null;
	}

	/// <summary>
	/// O PARCEIRO CAIU: quem observava volta a se ferir, diz a fala dele pro mundo e vira um chefe como os outros (o proximo tique da IA ja o
	/// leva pelo ramo de sempre, que caca o planeta).
	/// </summary>
	private void EntrarNaLuta(ObservadorDaSaga o)
	{
		_observadoresDaSaga.Remove(o.Corpo.Id);
		o.Corpo.Combate.NaoSeFere = false;
		if (o.Ficha.AnuncioAoEntrarNaLuta.Length > 0) AnunciarNoMundo(o.Ficha.AnuncioAoEntrarNaLuta);
		GD.Print($"[server] SAGA: '{o.Ficha.ObservaAteCair}' caiu -- {o.Corpo.Name} para de observar e entra na luta");
	}

	/// <summary>Um chefe do elo caiu (`MorreuOChefe`): quem o observava entra na luta NA HORA, e nao no proximo tique da mente dele.</summary>
	private void AcordarOsQueObservavam(EstadoDoElo est, string molde)
	{
		if (_observadoresDaSaga.Count == 0) return;
		foreach (ObservadorDaSaga o in _observadoresDaSaga.Values.ToList())
			if (ReferenceEquals(o.Est, est) && string.Equals(o.Ficha.ObservaAteCair, molde, StringComparison.OrdinalIgnoreCase)
				&& _players.TryGetValue(o.Corpo.Id, out ServerPlayer? c) && ReferenceEquals(c, o.Corpo))
				EntrarNaLuta(o);
	}
}
