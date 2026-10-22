using System;
using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// QUEM ESTA NO AGARRAO VAI JUNTO NA VIAGEM QUE VOCE ESCOLHEU -- menos pro espaco.
///
/// Pedido do dono (2026-10-21): "caso eu esteja segurando alguem e esteja carregando essa pessoa
/// e entre em uma nave ou outra zona, eu levo ela comigo, a nao ser espaco pq forcar levar pro
/// espaco e op." Vale nos dois apertos (Segurando e Carregando).
///
/// O funil e ESTE arquivo, e nao o <see cref="GameServer.MoveToZone"/> cru. O Instant Transmission
/// tambem chama o <c>MoveToZone</c> (agente <c>teleporte</c>): ele move o grupo dele por conta
/// propria. Por isso a carona nao mora la dentro. Quem quiser reaproveitar -- o teleporte, no dia
/// em que fizer sentido -- chama <see cref="ViajarVoluntario"/> no lugar do <c>MoveToZone</c>, ou
/// <see cref="AgarradoQueViaja"/> e move os dois na MESMA chamada. Os campos do agarrao sobrevivem
/// sozinhos se os dois trocam de zona antes do <c>TickDoAgarrao</c>; se so um vai, o tique limpa o
/// lado de quem segura calado, porque <c>QuemEuSeguro</c> exige a mesma zona.
///
/// Decolar do Lookout e do agente <c>subzonas</c>. Se a subida de la passar por <c>Decolar</c>, o
/// destino espaco solta aqui. Um <c>MoveToZone</c> novo pro vazio, fora deste funil, nao solta.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada --levarjuntoteste) -- falsos em jogo, sempre
	// =====================================================================
	/// <summary>DEFEITO INJETADO: a viagem voluntaria volta a levar so quem andou. Lido por <see cref="ViajarVoluntario"/>.</summary>
	internal static bool LevarJuntoNaoLevaDeTeste;

	/// <summary>DEFEITO INJETADO: o destino espaco volta a levar o agarrado a forca. Lido por <see cref="ViajarVoluntario"/>.</summary>
	internal static bool LevarJuntoLevaProEspacoDeTeste;

	/// <summary>DEFEITO INJETADO: o preso (PorQueNaoSaiDaqui, sem ser a esfera) volta a ser arrastado. Lido por <see cref="ViajarVoluntario"/>.</summary>
	internal static bool LevarJuntoLevaOPresoDeTeste;

	/// <summary>DEFEITO INJETADO: a esfera do agarrado nao recusa a porta que recusaria ele sozinho. Lido por <see cref="ViajarVoluntario"/>.</summary>
	internal static bool LevarJuntoIgnoraEsferaDeTeste;

	/// <summary>DEFEITO INJETADO: a Sala do Tempo deixa o agarrado furar chave, lotacao e recarga. Lido pela porta da sala.</summary>
	internal static bool LevarJuntoFuraASalaDeTeste;

	/// <summary>DEFEITO INJETADO: o pod solta com a frase da tecla, sem dizer que e por causa do espaco. Lido por <see cref="SoltarPraNaoLevarAoEspaco"/>.</summary>
	internal static bool LevarJuntoPodNaoAvisaDeTeste;

	/// <summary>O miolo da frase que o jogador le quando o aperto acaba porque o destino e o espaco (ou o pod, que vai pra la).</summary>
	internal const string FraseDeNaoLevarAoEspaco = "não dá pra levar alguém à força pro espaço";

	/// <summary>O miolo da frase quando quem e carregado nao pode sair de onde esta e quem carrega segue sozinho.</summary>
	internal const string FraseDeQuemFicaPreso = "não dá pra levar quem não pode sair daqui";

	/// <summary>O miolo da frase quando a esfera do agarrado recusa a porta pra fora do planeta.</summary>
	internal const string FraseDaEsferaDoAgarrado = "não atravessa com essa pessoa";

	/// <summary>
	/// QUEM VIAJA NO AGARRAO, ou nulo. So quem esta em <c>_players</c>: cadaver e boneco largado
	/// nao entram (o <c>MoveToZone</c> nem os ve). O tique seguinte limpa o aperto orfao, como ja fazia.
	/// </summary>
	private ServerPlayer? AgarradoQueViaja(ServerPlayer pl)
	{
		if (QuemEuSeguro(pl) is not { } d) return null;
		return _players.ContainsKey(d.Id) ? d : null;
	}

	/// <summary>
	/// A VIAGEM VOLUNTARIA. Devolve falso quando NINGUEM se moveu (a porta recusou os dois).
	/// Verdadeiro quando quem viaja mudou de zona -- sozinho, com o agarrado, ou tendo soltado.
	///
	/// <paramref name="esferasRecusam"/> e das portas que ja recusam quem carrega esfera comum
	/// (a Capital Ship). Caverna, Lookout, Sala e pouso nao passam verdadeiro: la a rede do
	/// <c>MoveToZone</c> continua mandando na esfera, e este arquivo nao mexe nela.
	/// <paramref name="aoLevar"/> roda no agarrado SO quando ele vai de verdade (a sessao da Sala).
	/// </summary>
	private bool ViajarVoluntario(ServerPlayer pl, ZoneKey destino, Vec2 onde,
		bool esferasRecusam = false, Action<ServerPlayer>? aoLevar = null)
	{
		ServerPlayer? carga = AgarradoQueViaja(pl);
		if (carga == null)
		{
			MoveToZone(pl.Id, destino, onde);
			return true;
		}

		// ESPACO: solta e segue. Forcar alguem pro vazio e o que o dono chamou de op. O defeito
		// injetado pula este ramo e cai no "leva junto" de baixo -- a bancada ve os dois no espaco.
		if (Espaco.EhEspaco(destino) && !LevarJuntoLevaProEspacoDeTeste)
		{
			Soltar(pl, MotivoDaSoltura.ProEspaco);
			MoveToZone(pl.Id, destino, onde);
			GD.Print($"[server] {pl.Name} solta {carga.Name}: destino {destino.Name} e espaco");
			return true;
		}

		// ESFERA COMUM na mao de quem e carregado, numa porta que ja recusaria ele sozinho. Os dois
		// ficam: largar a esfera no chao pelo MoveToZone seria a viagem que ninguem escolheu, e a
		// porta voluntaria recusa em vez de largar. A Black Star nao prende -- quem muda isso e o
		// PorQueAsEsferasPrendem (agente estrelanegra); aqui so se pergunta.
		if (esferasRecusam && !LevarJuntoIgnoraEsferaDeTeste
			&& PorQueAsEsferasPrendem(carga) is { Length: > 0 })
		{
			Avisar(pl, $"{carga.Name} carrega Esferas do Dragão, e elas não saem do planeta: você {FraseDaEsferaDoAgarrado}.");
			AvisarSePessoa(carga, $"{pl.Name} não atravessa com você: as Esferas do Dragão que você carrega não saem daqui.");
			return false;
		}

		// PRESO DE VERDADE (sala, selo, mente, torneio, bolso). A esfera tambem responde em
		// PorQueNaoSaiDaqui, mas ela nao e prisao: caverna e Lookout levam a esfera junto, e a
		// porta de cima ja decidiu o embarque. Quem carrega segue; o preso fica.
		if (!LevarJuntoLevaOPresoDeTeste && PrisaoSeguraOAgarrado(carga))
		{
			Avisar(carga, PorQueNaoSaiDaqui(carga));
			Soltar(pl, MotivoDaSoltura.FicouPreso);
			MoveToZone(pl.Id, destino, onde);
			GD.Print($"[server] {pl.Name} segue sem {carga.Name}: ele nao pode sair de {carga.Zone.Name}");
			return true;
		}

		if (LevarJuntoNaoLevaDeTeste)
		{
			MoveToZone(pl.Id, destino, onde);
			return true;
		}

		MoverOsDoisNaMesmaChamada(pl, carga, destino, onde, aoLevar);
		return true;
	}

	/// <summary>
	/// OS DOIS NA MESMA CHAMADA, no mesmo ponto. O tique do colo cola o corpo no quadro seguinte;
	/// ate la, a mesma coordenada nao dispara a soltura por distancia do modo Segurando.
	/// A carencia da passagem (e da nuvem) copia pra o agarrado: sem isto o laco do mesmo tique
	/// o veria na boca de volta e o devolveria sozinho.
	/// </summary>
	private void MoverOsDoisNaMesmaChamada(ServerPlayer pl, ServerPlayer carga, ZoneKey destino, Vec2 onde,
		Action<ServerPlayer>? aoLevar)
	{
		aoLevar?.Invoke(carga);
		if (_acabouDeAtravessar.TryGetValue(pl.Id, out long livre))
			_acabouDeAtravessar[carga.Id] = livre;
		_naBocaDaPassagem.Add(carga.Id);
		carga.Moving = false;
		MoveToZone(pl.Id, destino, onde);
		MoveToZone(carga.Id, destino, onde);
		GD.Print($"[server] {pl.Name} leva {carga.Name} para {destino.Name} ({pl.ModoDoAgarrao})");
	}

	/// <summary>
	/// POR QUE NAO SAI DAQUI, tirando a esfera. Vazio no PorQueNaoSaiDaqui, ou so a esfera, nao segura.
	/// </summary>
	private bool PrisaoSeguraOAgarrado(ServerPlayer carga)
	{
		string tudo = PorQueNaoSaiDaqui(carga);
		if (tudo.Length == 0) return false;
		string esferas = PorQueAsEsferasPrendem(carga);
		return esferas.Length == 0 || tudo != esferas;
	}

	/// <summary>
	/// O POD NAO TROCA DE ZONA, mas vai pro espaco. O aperto acaba aqui, com a frase do espaco, antes
	/// do <c>TomarOLeme</c> (que tambem solta, pela tecla, e nao diria por que). Sem ninguem no agarrao, nao fala.
	/// </summary>
	private void SoltarPraNaoLevarAoEspaco(ServerPlayer pl)
	{
		if (AgarradoQueViaja(pl) == null) return;
		Soltar(pl, LevarJuntoPodNaoAvisaDeTeste ? MotivoDaSoltura.Tecla : MotivoDaSoltura.ProEspaco);
	}

	/// <summary>
	/// A SALA DO TEMPO, OLHADA PELO AGARRADO. Vazio se ninguem vai no colo ou se ele passaria sozinho.
	/// A lotacao conta quem carrega, que ainda nao ocupou vaga. O defeito devolve vazio: a porta deixa
	/// os dois entrarem mesmo quando o agarrado seria recusado.
	/// </summary>
	private string PorQueLevarOAgarradoBarraASala(ServerPlayer pl, bool daTerra)
	{
		if (LevarJuntoFuraASalaDeTeste) return "";
		ServerPlayer? carga = AgarradoQueViaja(pl);
		if (carga == null) return "";

		if (carga.Ficha.dead) return $"{carga.Name} não entra na Sala do Tempo: mortos não treinam. Você também não entra.";
		if (carga.Ficha.KO) return $"{carga.Name} não entra na Sala do Tempo: mal se sustenta de pé. Você também não entra.";
		if (carga.Voando) return $"{carga.Name} está no ar. Pouse antes de atravessar a porta da Sala do Tempo. Você também não entra.";

		if (daTerra && carga.SalaPreso)
			return $"{carga.Name} ficou preso na sessão anterior da Sala do Tempo. Você também não entra.";

		int ocupadas = daTerra ? QuantosNaSala() : QuantosNaSalaDePlaneta(SalaDePlaneta.Interior(pl.Zone));
		if (ocupadas + 1 >= LotacaoDaSala)
			return $"a Sala do Tempo não cabe vocês dois: já tem {ocupadas} pessoa(s), e o máximo é {LotacaoDaSala}. Você também não entra.";

		if (daTerra && !carga.SalaAutorizada)
			return $"{carga.Name} não tem a chave do Guardião da Terra. Você também não entra.";

		long falta = FimDaRecargaDasSalas(carga) - NowMs();
		if (falta > 0)
			return $"{carga.Name} ainda não se recuperou da distorção temporal. Você também não entra.";

		return "";
	}

	/// <summary>A sessao da Sala da Terra, a mesma que a porta abre em quem entrou andando.</summary>
	private void AbrirSessaoDaSalaDaTerra(ServerPlayer pl, long agora)
	{
		pl.SalaAutorizada = false;
		pl.SalaUltimaEntrada = agora;
		pl.SalaDilatacao = SalaDePlaneta.DilatacaoDaTerra;
		pl.SalaPlaneta = SalaDoTempo.PortaDaTerra;
		pl.SalaDiasDaSessao = 0;
		pl.SalaJanelaAte = 0;
		_avisoDaSala.Remove(pl.Id);
		if (pl.Assinatura.Length > 0) _naSala[pl.Assinatura] = agora;
	}

	/// <summary>A sessao da sala de um mundo sorteado. O nome do mundo e o de FORA, capturado antes da viagem.</summary>
	private void AbrirSessaoDaSalaDePlaneta(ServerPlayer pl, long agora, double dilatacao, string mundo)
	{
		pl.SalaUltimaEntrada = agora;
		pl.SalaDilatacao = dilatacao;
		pl.SalaPlaneta = mundo;
		pl.SalaDiasDaSessao = 0;
		pl.SalaJanelaAte = 0;
		_avisoDaSala.Remove(pl.Id);
		_saidaPendenteDaSala.Remove(pl.Id);
		_avisadosDaPortaFria.Remove(pl.Id);
	}
}
