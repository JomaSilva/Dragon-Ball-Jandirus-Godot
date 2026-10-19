using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-18) O COMBUSTIVEL DAS NAVES -- quem gasta, quem para a nave seca, quem abastece e quem manda a barra. Os numeros moram no
/// Core (`Combustivel`), com o pedido do dono e as duas respostas dele no cabecalho.
///
/// ============================ ONDE O TANQUE SE GASTA: ONDE A NAVE ANDA, E SO LA ============================
/// Dois lugares movem uma nave, e os dois pagam pelo que ela andou: o `TickDasNaves` (a pilotada copia o piloto -- o que ela andou no
/// tique) e o `RumarSozinha` (a nave grande sem ninguem ao leme -- o passo dela). Decolar, pousar e a reentrada do foguete trocam de zona
/// e NAO gastam: sao saltos, e o tanque paga viagem.
/// =======================================================================================================
///
/// ============================ O TANQUE VAZIO PARA A NAVE DE VEZ (o dono) ============================
///   * ao leme o corpo nao anda (`PodeMexerOCorpo`, pelo <see cref="_pilotosSemCombustivel"/>) -- e o cliente nem tenta: a barra diz que
///     secou (`LocalPlayer.PorQueNaoAnda`);
///   * num planeta, quem estava no ar DESCE E POUSA, e nao decola de novo (`DecolarANavePousada`);
///   * nada lanca: nem o pod pelo menu (`LancarNave`), nem a nave grande da ponte (`LancarDaPonte`), nem o rumo sozinho;
///   * a nave que seguia sozinha para onde estiver e avisa quem esta dentro, o dono e quem a mandou.
/// No espaco ela fica a deriva ate alguem abastecer: o `nave_abastecer` vale EM QUALQUER LUGAR, pago com os recursos de quem abastece --
/// o piloto, se ainda tem no bolso, ou quem vier trazer (*"ate alguem trazer recursos"*, a opcao que o dono escolheu).
/// =================================================================================================
///
/// AO LEME, O KI DO PILOTO NAO PAGA O VOO (o dono escolheu "so combustivel") -- ver <see cref="AoLemeQuemPagaEANave"/>, lido no `TickDoVoo`.
///
/// O TANQUE SO VAI PRA MOCHILA CHEIO (`PegarNave`): o item nao guarda estado e a nave assentada nasce cheia -- recolher e assentar seria
/// abastecer de graca em qualquer lugar.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--naveteste`, camada 9)
	// =====================================================================
	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 9): a nave anda sem gastar nada -- o jogo de antes. Lido pelo <see cref="GastarCombustivel"/>. Falso em jogo, sempre.</summary>
	internal static bool NaveNaoGastaDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 9): o tanque vazio nao segura a nave -- ela anda seca. Lido pelo <see cref="ParadoPorFaltaDeCombustivel"/>. Falso em jogo, sempre.</summary>
	internal static bool TanqueVazioAndaDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 9): o recolher aceita a nave de tanque furado (e assentada de novo ela volta cheia). Lido pelo `PegarNave`. Falso em jogo, sempre.</summary>
	internal static bool RecolheComTanqueFuradoDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 9): ao leme o voo volta a cobrar o Ki do piloto -- o jogo de antes. Lido pelo <see cref="AoLemeQuemPagaEANave"/>. Falso em jogo, sempre.</summary>
	internal static bool PilotoPagaOVooDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 9): abastecer nao cobra recurso nenhum. Lido pelo <see cref="Abastecer"/>. Falso em jogo, sempre.</summary>
	internal static bool AbastecerDeGracaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--naveteste`, camada 9): o tanque gasta como um RELOGIO -- o mesmo tanto por tique andado, qualquer que seja a
	/// distancia --, e nao pela distancia que o dono pediu. Lido pelo <see cref="GastarCombustivel"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool GastoPorTempoDeTeste;

	// =====================================================================
	// O NIVEL
	// =====================================================================
	/// <summary>O TANQUE DESTA NAVE agora. O campo negativo e "cheio de fabrica" (ver `Nave.Tanque`); quem nao tem tanque tem zero.</summary>
	private static double NivelDoTanque(Nave n) =>
		!Combustivel.TemTanque(n.Tipo) ? 0 : n.Tanque < 0 ? Combustivel.Capacidade(n.Tipo) : n.Tanque;

	/// <summary>Esta nave tem tanque, e ele secou? (O foguete, sem tanque, nunca esta vazio.)</summary>
	private static bool TanqueVazio(Nave n) => Combustivel.TemTanque(n.Tipo) && NivelDoTanque(n) <= 0;

	/// <summary>A frase de quem tenta andar, decolar ou lancar de tanque vazio -- uma so, pras quatro portas.</summary>
	private string FraseDoTanqueVazio(Nave n) =>
		$"{NomeDaNave(n)} está sem combustível. Abasteça (tecla E > Abastecer): cada unidade custa "
		+ $"{Combustivel.RecursosPorUnidade:0.#} recurso -- recursos se mineram num planeta (verb Minerar).";

	// =====================================================================
	// O GASTO
	// =====================================================================
	/// <summary>Quem pilota uma nave de tanque vazio, neste tique. Refeito no comeco de cada `TickDoCombustivelDoTique` -- ver o porque la.</summary>
	private readonly HashSet<int> _pilotosSemCombustivel = [];

	/// <summary>Algum tanque mudou desde a ultima gravacao? A gravacao sai de dez em dez segundos (`TickDoCombustivel`).</summary>
	private bool _tanqueMudou;

	/// <summary>
	/// A NAVE ANDOU <paramref name="px"/> PIXELS: o tanque paga (`Combustivel.GastoDoPasso` -- a distancia, e so ela). Sem tanque, ou ja seco,
	/// nada. Secou AGORA: a nave para (<see cref="AcabouOCombustivel"/>).
	/// </summary>
	private void GastarCombustivel(Nave n, double px)
	{
		if (NaveNaoGastaDeTeste || px <= 0 || !Combustivel.TemTanque(n.Tipo)) return;
		double antes = NivelDoTanque(n);
		if (antes <= 0) return;

		double gasto = GastoPorTempoDeTeste
			? Combustivel.GastoPorTile(n.Tipo) * Protocol.TickSeconds * MoveRules.BaseSpeedPx / ZoneCollision.TileSize
			: Combustivel.GastoDoPasso(n.Tipo, px);
		double depois = Math.Max(0, antes - gasto);
		n.Tanque = depois;
		n.GastoNoSegundo += antes - depois;
		_tanqueMudou = true;
		if (depois <= 0) AcabouOCombustivel(n);
	}

	/// <summary>
	/// O TANQUE SECOU: a nave para de vez. A que seguia sozinha perde o rumo; quem esta no ar sobre um planeta desce e pousa (o voo cai, e o
	/// `TickDoVoo` faz a descida de sempre); e quem esta a bordo fica sabendo. A barra sai NA HORA: o cliente do piloto para de prever o passo
	/// no mesmo instante, em vez de levar correcao de volta ate a proxima volta de um segundo.
	/// </summary>
	private void AcabouOCombustivel(Nave n)
	{
		GD.Print($"[server] nave #{n.Id} ({n.Tipo}) ficou sem combustivel em {n.Zona}");
		string texto = $"{NomeDaNave(n)} fica sem combustível e para de vez. Abasteça (tecla E > Abastecer) -- "
					   + "com recursos no bolso dá pra abastecer de qualquer lugar, até no espaço.";

		if (n.TemRumo)
		{
			n.TemRumo = false;
			AvisarDoRumo(n, texto);
		}
		else AvisarABordo(n, texto);

		if (n.PilotoId != 0 && _players.TryGetValue(n.PilotoId, out ServerPlayer? piloto))
		{
			_pilotosSemCombustivel.Add(piloto.Id);
			piloto.Moving = false;
			if (piloto.Voando && !Espaco.EhEspaco(piloto.Zone))
			{
				piloto.Voando = false;
				MandarEfeito(piloto, "voo", 0);
				MandarFicha(piloto);
			}
		}

		GravarNaves();
		MandarCombustivelDaNave(n);
	}

	/// <summary>
	/// O AVISO A BORDO: na nave grande, quem esta DENTRO -- e quem largou o corpo la dentro pra ir ao leme ou pra observar ouve pelo corpo
	/// que ficou (ver `Mandar`, `GameServer.Chat.cs`); no pod, o piloto.
	/// </summary>
	private void AvisarABordo(Nave n, string texto)
	{
		if (NaveGrande.EhNaveGrande(n.Tipo))
		{
			foreach (ServerPlayer abordo in ZoneList(NaveGrande.ZonaDoInterior(n.Id).Hash).ToList())
				Avisar(abordo, texto);
			return;
		}
		if (n.PilotoId != 0 && _players.TryGetValue(n.PilotoId, out ServerPlayer? piloto)) Avisar(piloto, texto);
	}

	/// <summary>
	/// AO LEME, O CORPO NAO ANDA COM O TANQUE SECO. Lido no `PodeMexerOCorpo` -- que e perguntado por corpo e por tique (a IA tambem), e por
	/// isso a resposta vem de um conjunto refeito uma vez por tique, e nao de uma varredura das naves por pergunta.
	/// </summary>
	private bool ParadoPorFaltaDeCombustivel(ServerPlayer pl) =>
		!TanqueVazioAndaDeTeste && _pilotosSemCombustivel.Count > 0 && _pilotosSemCombustivel.Contains(pl.Id);

	/// <summary>
	/// AO LEME QUEM PAGA O VOO E A NAVE (o dono, 2026-10-18: *"so combustivel"*). Pilotar continua contando como voar -- a altura, a agua e o
	/// cenario por baixo --, mas o Ki do piloto nao desce por isso, e a proficiencia de voo tambem nao sobe (ela e paga por esse Ki,
	/// `flying.dm:34,40`: sem preco, sem treino). Lido no `TickDoVoo`.
	/// </summary>
	private bool AoLemeQuemPagaEANave(ServerPlayer pl) => !PilotoPagaOVooDeTeste && EstaPilotando(pl.Id);

	// =====================================================================
	// O ABASTECER -- o verbo `nave_abastecer`
	// =====================================================================
	/// <summary>
	/// ABASTECE A NAVE com os recursos de quem pede: a que ele pilota, a da ponte em que ele esta (ou o interior dela), a que ele observa, ou
	/// a parada ao alcance da mao -- nessa ordem. Em qualquer lugar, ate no espaco (o dono: *"ate alguem trazer recursos"*). Enche o que
	/// falta, ou o que o bolso paga; unidade inteira.
	/// </summary>
	private void Abastecer(ServerPlayer pl)
	{
		Nave? n = NaveDoPiloto(pl) ?? NaveDaPonteDe(pl) ?? NaveDesteInterior(pl) ?? NaveQueObservo(pl) ?? NavePerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave por perto pra abastecer."); return; }
		if (!Combustivel.TemTanque(n.Tipo)) { Avisar(pl, $"{NomeDaNave(n)} não usa combustível."); return; }

		double nivel = NivelDoTanque(n), capacidade = Combustivel.Capacidade(n.Tipo);
		if (Combustivel.Cheio(n.Tipo, nivel)) { Avisar(pl, $"o tanque de {NomeDaNave(n)} já está cheio ({capacidade:N0})."); return; }

		double falta = Math.Ceiling(capacidade - nivel);
		double cabeNoBolso = Math.Floor(pl.Ficha.Recursos / Combustivel.RecursosPorUnidade);
		double unidades = AbastecerDeGracaDeTeste ? falta : Math.Min(falta, cabeNoBolso);
		if (unidades <= 0)
		{
			Avisar(pl, $"você não tem recursos pra abastecer {NomeDaNave(n)}: faltam {falta:N0} de combustível, e cada unidade custa "
					   + $"{Combustivel.RecursosPorUnidade:0.#} recurso. Minere num planeta (verb Minerar) e volte.");
			return;
		}

		double custo = unidades * Combustivel.RecursosPorUnidade;
		if (!AbastecerDeGracaDeTeste && !CobrarRecursos(pl, custo))
		{
			Avisar(pl, $"os recursos não chegam: abastecer {unidades:N0} custa {custo:N0}, e você tem {Math.Floor(pl.Ficha.Recursos):N0}.");
			return;
		}

		n.Tanque = Math.Min(capacidade, nivel + unidades);
		_pilotosSemCombustivel.Remove(n.PilotoId);
		GravarNaves();
		Avisar(pl, Combustivel.Cheio(n.Tipo, n.Tanque)
			? $"você enche o tanque de {NomeDaNave(n)}: {unidades:N0} de combustível por {custo:N0} recursos."
			: $"você põe {unidades:N0} de combustível em {NomeDaNave(n)} ({custo:N0} recursos): o tanque fica em "
			  + $"{n.Tanque:N0} de {capacidade:N0} -- os recursos acabaram antes de encher.");
		GD.Print($"[server] {pl.Name} abasteceu a nave #{n.Id} com {unidades:0} ({custo:0} recursos) -> {n.Tanque:0}/{capacidade:0}");
		MandarCombustivelDaNave(n);
	}

	// =====================================================================
	// A BARRA -- `S2C.Combustivel`
	// =====================================================================
	/// <summary>
	/// O TANQUE QUE ESTE JOGADOR VE: o da nave que ele pilota (modo 1) ou o da que ele observa (modo 2). Nenhuma, ou uma sem tanque (o
	/// foguete): modo 0, e a barra some. Sai no embarque, no desembarque, ao abastecer, no instante em que seca e uma vez por segundo
	/// (`TickDoCombustivel`) -- e esse "por segundo" e o gasto da ultima volta, como a mineracao mostra o que o ultimo segundo rendeu.
	/// </summary>
	private void MandarCombustivel(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		byte modo = 0;
		Nave? n = NaveDoPiloto(pl);
		if (n != null) modo = 1;
		else if (NaveQueObservo(pl) is { } vista) { n = vista; modo = 2; }
		if (n == null || !Combustivel.TemTanque(n.Tipo)) { modo = 0; n = null; }

		var w = Protocol.Begin(Protocol.S2C.Combustivel);
		w.Put(modo);
		w.Put(n != null ? NomeDaNave(n) : "");
		w.Put((float)(n != null ? NivelDoTanque(n) : 0));
		w.Put((float)(n != null ? Combustivel.Capacidade(n.Tipo) : 0));
		w.Put((float)(n?.GastoPorSegundo ?? 0));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>A barra desta nave pra quem a ve: o piloto e quem a observa.</summary>
	private void MandarCombustivelDaNave(Nave n)
	{
		if (n.PilotoId != 0 && _players.TryGetValue(n.PilotoId, out ServerPlayer? piloto)) MandarCombustivel(piloto);
		foreach (int id in _observadores)
			if (_players.TryGetValue(id, out ServerPlayer? o) && o.ObservaNave == n.Id) MandarCombustivel(o);
	}

	/// <summary>Quando a proxima volta de um segundo do tanque sai, e quando a proxima gravacao pode sair.</summary>
	private long _proximaVoltaDoTanque, _proximaGravacaoDoTanque;

	/// <summary>
	/// O TIQUE DO TANQUE, chamado no fim do `TickDasNaves`: refaz quem esta ao leme de tanque seco (o conjunto que o `PodeMexerOCorpo` le) e,
	/// uma vez por segundo, fecha o "por segundo" de cada nave e manda a barra a quem a ve. Devolve se e hora de gravar as naves (de dez em
	/// dez segundos, enquanto algum tanque mudou -- o tanque de quem esta voando nao pode viver so na memoria, nem ir pro disco a 30 Hz).
	/// </summary>
	private bool TickDoCombustivel(long agora)
	{
		_pilotosSemCombustivel.Clear();
		foreach (Nave n in _naves)
			if (n.PilotoId != 0 && TanqueVazio(n)) _pilotosSemCombustivel.Add(n.PilotoId);

		if (agora < _proximaVoltaDoTanque) return false;
		_proximaVoltaDoTanque = agora + 1000;

		foreach (Nave n in _naves)
		{
			if (!Combustivel.TemTanque(n.Tipo)) continue;
			n.GastoPorSegundo = n.GastoNoSegundo;
			n.GastoNoSegundo = 0;
			if (n.PilotoId != 0 && _players.TryGetValue(n.PilotoId, out ServerPlayer? piloto)) MandarCombustivel(piloto);
		}
		foreach (int id in _observadores)
			if (_players.TryGetValue(id, out ServerPlayer? o)) MandarCombustivel(o);

		if (!_tanqueMudou || agora < _proximaGravacaoDoTanque) return false;
		_tanqueMudou = false;
		_proximaGravacaoDoTanque = agora + 10_000;
		return true;
	}
}
