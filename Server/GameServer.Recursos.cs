using System.Text.Json;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// UMA MINERACAO EM CURSO -- o que o servidor guarda de quem esta minerando. Mora no corpo
/// (<see cref="ServerPlayer.Mineracao"/>): nasce com o verb e morre com ele, sem dicionario pra sanear.
/// </summary>
public sealed class Mineracao
{
	/// <summary>Onde comecou. Sair do lugar -- andar, ser arremessado, trocar de zona -- acaba com ela.</summary>
	public ulong Zona;
	public Vec2 Onde;

	/// <summary>A ferramenta do ultimo segundo (a melhor da mochila, relida a cada volta).</summary>
	public Ferramenta Ferramenta;

	/// <summary>Quanto saiu no ultimo segundo, e quanto ja saiu nesta leva -- o que a tela e a frase de fim dizem.</summary>
	public double UltimoSegundo, Total;
}

/// <summary>
/// O RECURSO NO SERVIDOR -- o saldo de cada jogador, a reserva de cada planeta e o verb "Minerar". A regra e as
/// contas estao em <see cref="Recurso"/> e <see cref="ReservaDePlaneta"/>.
///
/// ESTE ARQUIVO E O CONTRATO que os outros sistemas usam (Drill, rochas de minerio, arvore derrubada, custo de
/// tecnologia e de base): as quatro assinaturas de baixo (`ReservaDe`, `DarRecursos`, `CobrarRecursos`,
/// `Cobrar`) nao mudam sem manter a antiga funcionando.
///
/// ============================ O QUE MORA AQUI ============================
///   * AS RESERVAS (`reservas.json`): uma por planeta em que alguem ja mexeu. Cheia e a resposta padrao e nao
///     vai pro disco; a mexida vai com a hora, e o que cresceu desde entao se deriva dela -- com o servidor
///     ligado (<see cref="TickDosRecursos"/>) ou desligado (<see cref="ReservaDePlaneta.DoDisco"/>).
///   * A CARTEIRA (`S2C.Carteira`): zeni e recursos do jogador, mandados toda vez que um dos dois muda por
///     aqui. E o que faz a aba Tech, a Stats e a faixa da mineracao andarem sem reenviar o catalogo.
///   * MINERAR: a terceira atividade do corpo, ao lado de treinar e meditar (`C2S.Activity`). Rende por
///     segundo, tirado da reserva do planeta, e cai quando o corpo anda, age, apanha ou o chao seca.
/// ==========================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// AS RESERVAS DOS PLANETAS
	// =====================================================================
	/// <summary>A reserva de cada planeta, pela zona (<see cref="ZoneKey.Hash"/>). Nasce cheia na primeira consulta.</summary>
	private readonly Dictionary<ulong, ReservaDePlaneta> _reservas = [];

	private string CaminhoDasReservas => System.IO.Path.Combine(_store?.Pasta ?? ".", "reservas.json");

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): planeta destruido continua com reserva -- o nucleo de antes
	/// de 2026-10-10, que so perguntava se a zona era um planeta. Lido pelas duas linhas que o conferem.
	/// </summary>
	internal bool MortoTemReservaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): a reserva so existe na memoria -- o nucleo de antes de
	/// 2026-10-10, em que todo reinicio do servidor devolvia cada planeta cheio. Lido pelo proprio
	/// <see cref="SalvarReservas"/>.
	/// </summary>
	internal bool ReservasSoNaMemoriaDeTeste;

	/// <summary>
	/// A RESERVA DESTE PLANETA -- ou NULO onde nao ha o que minerar.
	///
	/// "PLANETA" E O DO <see cref="Espaco.EhPlaneta"/>: mundo sorteado, ou pre-feito em que se pousa (Terra,
	/// Namek, Vegeta...). O Lookout, o alem, a Sala do Tempo, o interior de nave, o espaco: nulo -- e o pedido do
	/// dono (*"menos em locais como lookout"*).
	///
	/// PLANETA DESTRUIDO NAO TEM RESERVA: nulo enquanto ele estiver morto, e o registro some junto. Restaurado,
	/// ele volta CHEIO -- quem o traz de volta traz um mundo inteiro (e o que o `RessuscitarPlaneta` anuncia:
	/// "voltou a existir"), e nao o buraco que a mineracao tinha deixado.
	///
	/// O TETO SAI DAS MEDIDAS DO PLANETA (<see cref="MedidasDoPlaneta"/>), e nao do mapa carregado: o lado de
	/// um mundo gerado e funcao pura da seed, entao a resposta existe mesmo com a zona descarregada -- e e o que
	/// deixa um Drill seguir tirando de um planeta onde nao ha ninguem.
	/// </summary>
	internal ReservaDePlaneta? ReservaDe(ZoneKey zona)
	{
		if (_reservas.TryGetValue(zona.Hash, out ReservaDePlaneta? ja))
		{
			if (!ODonoMorreu(ja)) return ja;
			_reservas.Remove(zona.Hash);
			_reservasMudaramDeConjunto = true;
			return null;
		}

		if (ChaveDePlaneta.Da(zona) is not { } chave) return null;
		if (!MortoTemReservaDeTeste && _mortos.Morto(chave)) return null;

		var nova = new ReservaDePlaneta(Recurso.MaximoDoPlaneta(MedidasDoPlaneta(zona).Lado))
		{
			Chave = chave.Texto,
			Nome = zona.Name,
		};
		_reservas[zona.Hash] = nova;
		return nova;
	}

	/// <summary>O planeta desta reserva foi destruido? A pergunta e feita pela chave que ela mesma guarda.</summary>
	private bool ODonoMorreu(ReservaDePlaneta r) =>
		!MortoTemReservaDeTeste && ChaveDePlaneta.Ler(r.Chave, r.Nome, out ChaveDePlaneta chave) && _mortos.Morto(chave);

	/// <summary>
	/// LE O `reservas.json`. Esvazia antes de ler (a recarga da `--wipeteste` chama de novo com o servidor de
	/// pe) e NAO GRAVA: o que cresceu com o servidor desligado e somado na memoria, e o arquivo so e regravado
	/// quando alguem tira alguma coisa -- um carregador que grava ao ler regrava a pasta a cada boot.
	///
	/// DEPOIS do livro dos mortos na sequencia de boot: o registro de um planeta que morreu e descartado aqui.
	/// </summary>
	private void CarregarReservas()
	{
		_reservas.Clear();
		_mexidasGravadas = 0;
		_reservasMudaramDeConjunto = false;
		try
		{
			if (!System.IO.File.Exists(CaminhoDasReservas)) return;

			var lista = JsonSerializer.Deserialize<List<ReservaGravada>>(
				System.IO.File.ReadAllText(CaminhoDasReservas),
				new JsonSerializerOptions { IncludeFields = true });
			if (lista == null) return;

			long agora = NowMs();
			int descartadas = 0;
			foreach (ReservaGravada g in lista)
			{
				ZoneKey zona = ChaveDePlaneta.ZonaDe(g.Chave, g.Nome);
				if (g.Chave.Length == 0 || ChaveDePlaneta.Da(zona) is not { } chave)
				{
					GD.PushWarning($"[server] reservas.json: '{g.Nome}' ({g.Chave}) nao e um planeta deste universo -- descartado");
					descartadas++;
					continue;
				}
				if (!MortoTemReservaDeTeste && _mortos.Morto(chave)) { descartadas++; continue; }

				_reservas[zona.Hash] = ReservaDePlaneta.DoDisco(g, Recurso.MaximoDoPlaneta(MedidasDoPlaneta(zona).Lado), agora);
			}

			GD.Print($"[server] reservas: {_reservas.Count} planeta(s) com a reserva mexida"
				   + (descartadas > 0 ? $", {descartadas} registro(s) descartado(s)" : "")
				   + (_reservas.Count > 0
					   ? " (" + string.Join(", ", _reservas.Values.Select(r => $"{r.Nome}={r.Fracao * 100:0}%")) + ")"
					   : ""));
		}
		catch (Exception e) { GD.PushWarning($"[server] reservas.json ilegivel: {e.Message}"); }
	}

	/// <summary>
	/// GRAVA O LIVRO INTEIRO, numa escrita atomica (`.tmp` + `File.Move`) -- o desenho do `SalvarPlanetasMortos`.
	/// So as reservas MEXIDAS: cheia e ausencia (ver <see cref="ReservaGravada"/>).
	///
	/// O PORTAO DA LIMPEZA vale aqui como pros outros: enquanto a limpeza total corre, nada grava.
	/// </summary>
	private void SalvarReservas()
	{
		if (ReservasSoNaMemoriaDeTeste) return;
		if (_store == null || _limpezaEmCurso) return;
		try
		{
			long agora = NowMs();
			List<ReservaGravada> lista = [.. _reservas.Values.Where(r => !r.Cheia)
															 .Select(r => r.Gravar(agora))
															 .OrderBy(g => g.Chave, StringComparer.Ordinal)];
			string tmp = CaminhoDasReservas + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(
				lista, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDasReservas, overwrite: true);

			_mexidasGravadas = MexidasDeTodas();
			_reservasMudaramDeConjunto = false;
		}
		catch (Exception e) { GD.PushWarning($"[server] nao consegui gravar reservas.json: {e.Message}"); }
	}

	/// <summary>O `Zerar` da limpeza total: todo planeta volta cheio. So memoria -- quem apaga o arquivo e a vassoura.</summary>
	private void ZerarReservas()
	{
		_reservas.Clear();
		_mexidasGravadas = 0;
		_reservasMudaramDeConjunto = false;
		_proximaGravacaoDasReservas = 0;
	}

	/// <summary>Quantos planetas estao com a reserva abaixo do teto -- a contagem que a previa da limpeza mostra.</summary>
	private int ReservasMexidas() => _reservas.Values.Count(r => !r.Cheia);

	/// <summary>
	/// HA O QUE GRAVAR? Duas fontes, e nenhuma depende de quem tirou avisar: a soma das
	/// <see cref="ReservaDePlaneta.Mexidas"/> andou (alguem chamou `Tirar` -- a mao, o Drill ou a rocha), ou o
	/// conjunto mudou (um planeta morreu e levou o registro).
	/// </summary>
	private bool ReservasPorGravar() => _reservasMudaramDeConjunto || MexidasDeTodas() != _mexidasGravadas;

	private long MexidasDeTodas()
	{
		long soma = 0;
		foreach (ReservaDePlaneta r in _reservas.Values) soma += r.Mexidas;
		return soma;
	}

	private long _mexidasGravadas;
	private bool _reservasMudaramDeConjunto;
	private long _proximaGravacaoDasReservas, _proximaVoltaDosRecursos, _ultimaVoltaDasReservas;

	/// <summary>
	/// DE QUANTO EM QUANTO TEMPO O LIVRO VAI PRO DISCO, quando ha o que gravar. Quem minera tira a cada segundo;
	/// um arquivo por segundo seria pagar disco por um numero que a hora do registro ja corrige. Uma queda do
	/// servidor perde no maximo isto de mineracao DO PLANETA (o saldo do jogador segue o autosave dele).
	/// </summary>
	private const double SegundosEntreGravacoesDasReservas = 30;

	// =====================================================================
	// O TIQUE (1 Hz)
	// =====================================================================
	/// <summary>
	/// UMA VOLTA POR SEGUNDO: as reservas crescem, o planeta que morreu perde a dele, quem minera recebe, e o
	/// livro vai pro disco se mudou. Chamado todo tique; quem conta o segundo e ele (o desenho do
	/// `TickDosRegeneradores`).
	///
	/// O TEMPO E O DO RELOGIO DE PAREDE: e a mesma hora que carimba o arquivo, entao o que a reserva cresce
	/// com o servidor de pe e o que ela cresceria com ele desligado -- um tique atrasado ou uma pausa do
	/// processo nao comem minerio.
	///
	/// A VOLTA SEGUINTE E MARCADA A PARTIR DA ANTERIOR, e nao de agora: marcada de agora, cada volta chegaria
	/// ate um tique atrasada (33 ms) e "por segundo" renderia 3% a menos pra sempre. Atraso grande (o processo
	/// parado) nao se paga em voltas acumuladas -- quem minera recebe por volta, nao por tempo.
	/// </summary>
	private void TickDosRecursos()
	{
		// A CARTEIRA NAO ESPERA O SEGUNDO: quem mudou de saldo por fora do funil fica sabendo neste tique.
		VigiarAsCarteiras();

		long agora = NowMs();
		if (agora < _proximaVoltaDosRecursos) return;
		_proximaVoltaDosRecursos = agora - _proximaVoltaDosRecursos > 2000 ? agora + 1000 : _proximaVoltaDosRecursos + 1000;

		double dt = _ultimaVoltaDasReservas == 0 ? 0 : Math.Max(0, agora - _ultimaVoltaDasReservas) / 1000.0;
		_ultimaVoltaDasReservas = agora;

		List<ulong>? mortas = null;
		foreach ((ulong hash, ReservaDePlaneta r) in _reservas)
		{
			if (ODonoMorreu(r)) (mortas ??= []).Add(hash);
			else r.Regenerar(dt);
		}
		if (mortas != null)
		{
			foreach (ulong hash in mortas) _reservas.Remove(hash);
			_reservasMudaramDeConjunto = true;
		}

		TickDaMineracao();

		if (agora >= _proximaGravacaoDasReservas && ReservasPorGravar())
		{
			SalvarReservas();
			_proximaGravacaoDasReservas = agora + (long)(SegundosEntreGravacoesDasReservas * 1000);
		}
	}

	// =====================================================================
	// A CARTEIRA -- o saldo do jogador, e o cliente sabendo dele
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): o saldo muda no servidor e o cliente nao fica sabendo -- o
	/// nucleo de antes de 2026-10-10. Lido pelo proprio <see cref="MandarCarteira"/>.
	/// </summary>
	internal bool CarteiraMudaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): cobra recurso de quem nao tem (o saldo fica negativo).
	/// Lido pela propria guarda, em <see cref="CobrarRecursos"/>.
	/// </summary>
	internal bool CobraSemSaldoDeTeste;

	/// <summary>Quantas carteiras ja foram mandadas -- o que a bancada le pra afirmar "o cliente ficou sabendo".</summary>
	private long _carteirasMandadas;

	/// <summary>
	/// A CARTEIRA DESTE JOGADOR, PRO CLIENTE DELE: zeni e recursos, num pacote de dezessete bytes.
	///
	/// O ZENI VAI JUNTO, e nao por economia: ate aqui ele so chegava dentro do catalogo de construcoes
	/// (`S2C.Tech`, cem ofertas com nome e arte), e todo sistema que mexia em zeni reenviava o catalogo inteiro
	/// pra atualizar um numero. Quem cobra por <see cref="Cobrar"/> -- uma parede de base por clique -- nao pode
	/// custar isso; e a aba refaz a pergunta "cabe no bolso?" com a carteira nova
	/// (<see cref="Jandirus.Core.Tech.CatalogoDeObras.ComSaldo"/>).
	/// </summary>
	private void MandarCarteira(ServerPlayer pl)
	{
		if (CarteiraMudaDeTeste) return;
		_carteirasMandadas++;

		// O QUE O CLIENTE FICA SABENDO, anotado: e contra isto que a vigia compara (ver `VigiarAsCarteiras`).
		CarteiraVista vista = _carteirasVistas.GetOrCreateValue(pl);
		vista.Mandada = true;
		vista.Zeni = pl.Ficha.Zeni;
		vista.Recursos = pl.Ficha.Recursos;

		if (pl.Peer == null) return;

		var w = Protocol.Begin(Protocol.S2C.Carteira);
		w.Put(pl.Ficha.Zeni);
		w.Put(pl.Ficha.Recursos);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Soma no saldo de recursos do jogador, e o cliente dele fica sabendo. Quantia nula ou negativa nao faz nada.</summary>
	internal void DarRecursos(ServerPlayer pl, double quanto)
	{
		if (!(quanto > 0) || double.IsInfinity(quanto)) return;
		pl.Ficha.Recursos += quanto;
		MandarCarteira(pl);
	}

	/// <summary>Cobra recursos. Sem saldo: FALSO, e nao cobra nada (nem um pedaco).</summary>
	internal bool CobrarRecursos(ServerPlayer pl, double quanto)
	{
		if (!(quanto >= 0) || double.IsInfinity(quanto)) return false;
		if (!CobraSemSaldoDeTeste && pl.Ficha.Recursos < quanto) return false;
		pl.Ficha.Recursos -= quanto;
		MandarCarteira(pl);
		return true;
	}

	/// <summary>
	/// COBRA O PRECO DE UMA OBRA na moeda que o jogador escolheu: em zeni, o custo; em recursos, o dobro
	/// (<see cref="Recurso.CustoEmRecursos"/>). Sem saldo NAQUELA moeda: falso, e nao cobra nada -- nunca
	/// completa com a outra. Cobrou: o cliente fica sabendo do saldo novo (<see cref="MandarCarteira"/>).
	/// </summary>
	internal bool Cobrar(ServerPlayer pl, double custoEmZeni, Moeda moeda)
	{
		if (!(custoEmZeni >= 0) || double.IsInfinity(custoEmZeni)) return false;
		if (moeda == Moeda.Recursos) return CobrarRecursos(pl, Recurso.CustoEmRecursos(custoEmZeni));

		if (pl.Ficha.Zeni < custoEmZeni) return false;
		pl.Ficha.Zeni -= custoEmZeni;
		MandarCarteira(pl);
		return true;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): as melhorias de maquina, a conversao em androide e a
	/// estatua suprema so aceitam zeni -- o estado de antes de 2026-10-10: a moeda escolhida e ignorada, e a
	/// cobranca sai em zeni. Lido pela propria linha que cobra, em <see cref="CobrarOuExplicar"/>.
	/// </summary>
	internal bool SoZeniPagaDeTeste;

	/// <summary>
	/// COBRA NA MOEDA ESCOLHIDA, OU EXPLICA POR QUE NAO DEU -- o <see cref="Cobrar"/> do contrato com a frase da
	/// recusa. E a porta das cinco coisas que o dono mandou aceitarem recurso (2026-10-10): *"as melhorias da
	/// maquina de gravidade, do regenerador e da nave; a conversao em androide; a estatua SUPREMA. mude pra
	/// aceitarem resource, o enma mantenha so zeni"*. O Enma, o banco e a verba do Presidente nao passam por aqui.
	///
	/// A FRASE DIZ O PRECO NAS DUAS MOEDAS E O QUE HA NOS DOIS BOLSOS: quem escolheu a moeda que nao tinha le, na
	/// mesma linha, se a outra pagava. Sem saldo NAQUELA moeda nao cobra nada -- nunca completa com a outra.
	/// </summary>
	/// <param name="oQue">O que esta sendo pago, como comeca a frase da recusa ("o próximo degrau", "bateria maior").</param>
	internal bool CobrarOuExplicar(ServerPlayer pl, double custoEmZeni, Moeda moeda, string oQue)
	{
		if (SoZeniPagaDeTeste) moeda = Moeda.Zeni;
		if (Cobrar(pl, custoEmZeni, moeda)) return true;

		Avisar(pl, $"{oQue} custa {Recurso.DoisPrecos(custoEmZeni)} -- você tem {CarteiraEscrita(pl)}, e "
				   + (moeda == Moeda.Recursos ? "os recursos não chegam." : "o zeni não chega."));
		return false;
	}

	/// <summary>O que ha nos dois bolsos, escrito ("1.200 zeni e 300 recursos") -- pra baixo, pra nao prometer o que falta.</summary>
	private static string CarteiraEscrita(ServerPlayer pl) =>
		$"{Math.Floor(pl.Ficha.Zeni):N0} zeni e {Math.Floor(pl.Ficha.Recursos):N0} recursos";

	// =====================================================================
	// A VIGIA DA CARTEIRA -- o funil de quem nao passa pelo funil
	// =====================================================================
	/// <summary>O que o cliente de um jogador ouviu por ultimo da carteira dele. Ver <see cref="VigiarAsCarteiras"/>.</summary>
	private sealed class CarteiraVista
	{
		public bool Mandada;
		public double Zeni, Recursos;
	}

	/// <summary>
	/// POR CORPO (o objeto), e nao por id: quem loga de novo e outro `ServerPlayer`, nasce sem registro, e o
	/// primeiro tique lhe conta a carteira. Tabela FRACA: o registro morre com o corpo, sem faxina no logout.
	/// </summary>
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ServerPlayer, CarteiraVista> _carteirasVistas = new();

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--recursosteste` e `--diagabas`): so o funil avisa -- o estado de antes de
	/// 2026-10-10, em que o zeni que mudava por fora dele (a missao de cargo, o premio do torneio, o desejo de
	/// riqueza) ficava velho na tela ate o proximo catalogo. Lido pela propria vigia.
	/// </summary>
	internal bool CarteiraSoPeloFunilDeTeste;

	/// <summary>
	/// A VIGIA, A CADA TIQUE: quem tem no bolso um numero diferente do que o cliente ouviu por ultimo, ouve de
	/// novo (<see cref="MandarCarteira"/>).
	///
	/// ============================ POR QUE UMA VIGIA, E NAO UMA CHAMADA EM CADA LUGAR ============================
	/// O zeni e um campo publico da ficha, e mais de quinze lugares escreviam nele direto -- o banco, a missao e a verba
	/// de cargo, o tributo de planeta, o desejo dos dois dragoes, a estatua, o Enma, a conversao em androide, as
	/// dez melhorias de maquina, a nave, o premio do torneio. So os que lembravam de reenviar o catalogo de
	/// construcoes inteiro (cem ofertas, pra atualizar um numero) chegavam a tela; o resto deixava a aba Stats e a
	/// Tech com o zeni velho. Uma chamada a mais em cada um consertaria os de hoje e deixaria a mesma porta
	/// aberta pro proximo. Aqui a pergunta e feita uma vez, pelo ESTADO: o que o cliente sabe e o que a
	/// ficha tem? -- e ninguem mais precisa lembrar de nada.
	///
	/// O funil (<see cref="DarRecursos"/>, <see cref="CobrarRecursos"/>, <see cref="Cobrar"/>) continua avisando
	/// na hora: quem passa por ele ja sai com a carteira anotada, e a vigia nao tem o que mandar de novo. Desde
	/// que passaram a aceitar recurso, as melhorias de maquina, a nave, o androide e a estatua suprema cobram por
	/// ele (<see cref="CobrarOuExplicar"/>); o banco, o Enma, os cargos, o torneio e os desejos seguem com a vigia.
	///
	/// O CUSTO e uma consulta e duas comparacoes por corpo por tique; o pacote (dezessete bytes) so sai quando
	/// algum dos dois numeros mudou.
	/// =============================================================================================================
	/// </summary>
	private void VigiarAsCarteiras()
	{
		if (CarteiraSoPeloFunilDeTeste) return;
		foreach (ServerPlayer pl in _players.Values)
		{
			if (CarteiraEmDia(pl)) continue;
			MandarCarteira(pl);
		}
	}

	/// <summary>
	/// O cliente deste jogador sabe da carteira o que a ficha tem agora? `Equals`, e nao `==`: um saldo que
	/// virasse NaN nunca seria igual a si mesmo, e sairia um pacote por tique.
	/// </summary>
	private bool CarteiraEmDia(ServerPlayer pl) =>
		_carteirasVistas.TryGetValue(pl, out CarteiraVista? vista) && vista.Mandada
		&& vista.Zeni.Equals(pl.Ficha.Zeni) && vista.Recursos.Equals(pl.Ficha.Recursos);

	// =====================================================================
	// MINERAR -- o verb do dono
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): andar nao interrompe a mineracao. Lido pela propria
	/// pergunta, em <see cref="PorQueNaoMinera"/>.
	/// </summary>
	internal bool MineraAndandoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): a mineracao paga a taxa cheia sem tirar da reserva do
	/// planeta -- recurso vindo do nada, e um planeta que nunca seca. Lido pela propria linha que tira, em
	/// <see cref="TickDaMineracao"/>.
	/// </summary>
	internal bool MineraDoNadaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): o portao nao pergunta pelo esmagamento -- o estado de antes
	/// da decisao do dono, em que quem estava acima do limite so era barrado pelo relogio de luta que o dano da
	/// gravidade arma (depois da primeira ferida), e sem ouvir que era a gravidade. Lido pela propria pergunta,
	/// em <see cref="PorQueNaoMinera"/>.
	/// </summary>
	internal bool SemPortaoDoEsmagamentoDeTeste;

	/// <summary>Quanto o corpo pode sair de onde comecou (px) sem que isso conte como ter andado.</summary>
	private const float FolgaDaMineracao = 4f;

	/// <summary>
	/// POR QUE ESTE CORPO NAO PODE MINERAR AGORA -- vazio = pode. A MESMA pergunta pra comecar e pra seguir
	/// (<paramref name="emCurso"/> nulo = esta pedindo pra comecar): a lista de "nao pode" e uma so, e as duas
	/// perguntas que so quem ja esta minerando responde (saiu do lugar? trocou de zona?) vem no fim.
	///
	/// *"ao estar mineirando e igual meditacao vc n pode se mover nem fazer nada"*: andar, voar, nadar, brigar
	/// (bater ou apanhar acende o relogio de luta, <see cref="Jandirus.Core.Combat.CombatState.LutandoDeVerdade"/>),
	/// ser esmagado pela gravidade ou pelo peso e carregar Ki derrubam; os comandos de corpo (soco, guarda,
	/// tecnica, transformar) derrubam na chegada do pacote -- ver <see cref="AgirInterrompeAMineracao"/>.
	/// </summary>
	private string PorQueNaoMinera(ServerPlayer pl, Mineracao? emCurso)
	{
		if (pl.Ficha.KO || pl.Ficha.dead) return Recurso.SemCondicoesDeMinerar;
		if (SemAsRedeas(pl) || pl.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao) return Recurso.AFeraNaoMinera;
		if (EmCena(pl) || PresoNoTorneio(pl.Id) || EstaPilotando(pl.Id)) return Recurso.NaoDaPraMinerarAgora;
		if (pl.Voando || pl.Altitude > 0f) return "pouse antes: não dá pra minerar do ar.";
		if (pl.Nadando) return "não dá pra minerar dentro d'água.";
		if (pl.Carregando || EnraizadoPorKi(pl.Id)) return "não dá pra minerar reunindo energia.";

		// ============================ ESMAGADO NAO MINERA (decisao do dono, 2026-10-10) ============================
		// *"sim conta como combate, n da pra mineirar se vc nem consegue se mover"* -- a resposta dele a
		// "gravidade acima do limite deve contar como combate pra minerar?". Acima do limite do corpo, pela
		// gravidade do lugar (planeta ou maquina) ou pelo peso de treino, nao se comeca e nao se continua.
		//
		// A PERGUNTA E PELO ESTADO (`Esmagamento.Esmaga`, a mesma do `TickDoEsmagamento`), e nao pelo relogio de
		// luta que o dano do esmagamento arma: o relogio so acende depois da primeira ferida (ate um segundo
		// depois de pisar no chao pesado -- tempo de comecar e ser derrubado), e quem era barrado por ele ouvia
		// "no meio de uma briga" sem ter trocado um golpe com ninguem. A frase diz O QUE esmaga, com os numeros
		// do aviso de esmagamento, porque a saida de cada um e outra: tirar os pesos, ou dominar a gravidade.
		// =========================================================================================================
		if (!SemPortaoDoEsmagamentoDeTeste && Esmagamento.Esmaga(pl.Ficha))
		{
			string fim = emCurso == null ? "você não consegue minerar" : "você para de minerar";
			return Esmagamento.PorPeso(pl.Ficha)
				? $"o peso que você carrega está te esmagando: {fim}. Tire os pesos primeiro."
				: $"a gravidade daqui está te esmagando ({Esmagamento.Gravidade(pl.Ficha):0.#}x contra "
				  + $"{pl.Ficha.GravMastered:0.#}x que você domina): {fim}.";
		}

		// O RELOGIO DE LUTA (10 s desde a ultima pancada, dada ou levada). Quem o armou decide a frase: um golpe
		// trocado com ALGUEM e briga; ferida sem algoz (o resto de um esmagamento que acabou de passar, o preco de
		// uma tecnica no proprio corpo) nao e, e dizer "briga" a quem esta sozinho no mapa so confunde.
		if (pl.Combate is { LutandoDeVerdade: > 0 } luta)
		{
			if (luta.EmBrigaComAlguem)
				return emCurso == null ? "não dá pra minerar no meio de uma briga." : "a briga interrompe a mineração.";
			return emCurso == null
				? "você acabou de se ferir: espere alguns segundos antes de minerar."
				: "um ferimento interrompe a mineração.";
		}

		if (ReservaDe(pl.Zone) is not { } reserva) return Recurso.NaoHaOQueMinerar;
		if (reserva.Atual < Recurso.PorSegundoNaMao)
			return "este planeta está esgotado: não sobrou o que tirar. A reserva volta devagar com o tempo.";

		if (emCurso == null) return "";
		if (emCurso.Zona != pl.Zone.Hash) return "você para de minerar.";
		if (!MineraAndandoDeTeste
			&& (pl.Moving || (pl.Pos - emCurso.Onde).LengthSquared > FolgaDaMineracao * FolgaDaMineracao))
			return "você para de minerar.";
		return "";
	}

	/// <summary>
	/// O DONO DECLAROU QUE O CORPO MINERA (ou que parou) -- a terceira atividade do `C2S.Activity`, chamada pelo
	/// `DefinirAtividade` junto do treino e da meditacao. Quem decide se comeca e o servidor; recusado, o
	/// cliente fica sabendo (a pose e o HUD dele nao podem dizer "minerando" de um corpo que nao esta).
	/// </summary>
	private void DefinirMineracao(ServerPlayer pl, bool quer)
	{
		if (!quer) { PararDeMinerar(pl, "você para de minerar.", avisarOCliente: false); return; }
		if (pl.Mineracao != null) return;

		string porque = PorQueNaoMinera(pl, null);
		if (porque.Length > 0)
		{
			Avisar(pl, porque);
			MandarMineracao(pl);
			return;
		}

		ReservaDePlaneta reserva = ReservaDe(pl.Zone)!;   // o `PorQueNaoMinera` acabou de conferir
		Ferramenta f = Recurso.MelhorFerramenta(pl.Mochila);
		pl.Mineracao = new Mineracao { Zona = pl.Zone.Hash, Onde = pl.Pos, Ferramenta = f };

		Avisar(pl, $"você começa a minerar {Recurso.ComQue(f)}: +{Recurso.PorSegundo(f):0} recursos por segundo. "
				 + $"Reserva de {NomeDaReserva(pl.Zone)}: {reserva.Fracao * 100:0}%. Andar ou agir interrompe.");
		GD.Print($"[server] {pl.Name} comecou a minerar em {pl.Zone.Name} ({f}, reserva {reserva.Fracao * 100:0}%)");
		MandarMineracao(pl);
	}

	/// <summary>
	/// A MINERACAO ACABA. Sem mineracao em curso nao faz (nem diz) nada -- e por isso todo mundo pode chamar sem
	/// perguntar antes. <paramref name="avisarOCliente"/> falso e pro caso em que foi o proprio cliente que
	/// parou (ele ja sabe); a frase sai nos dois.
	/// </summary>
	private void PararDeMinerar(ServerPlayer pl, string motivo, bool avisarOCliente = true)
	{
		if (pl.Mineracao is not { } m) return;
		pl.Mineracao = null;

		Avisar(pl, m.Total > 0
			? $"{motivo} Você tirou {m.Total:N0} recursos (tem {pl.Ficha.Recursos:N0})."
			: motivo);
		GD.Print($"[server] {pl.Name} parou de minerar: {m.Total:N0} recursos na leva ({motivo})");
		if (avisarOCliente) MandarMineracao(pl);
	}

	/// <summary>
	/// QUEM MINERA E AGE, PARA DE MINERAR -- chamado no despacho, pra todo comando de corpo
	/// (<see cref="ComandoDeCorpo"/>: soco, guarda, carga de Ki, tecnica, transformacao, Zanzoken) menos o
	/// proprio `Activity`, que tem a sua porta. E o "nem fazer nada" do pedido: o gesto sai, e a mineracao cai.
	/// </summary>
	private void AgirInterrompeAMineracao(NetPeer peer, Protocol.C2S id)
	{
		if (!ComandoQueInterrompeAMineracao(id)) return;
		if (_byPeer.TryGetValue(peer, out ServerPlayer? pl)) PararDeMinerar(pl, "você para de minerar.");
	}

	/// <summary>Este pacote e um gesto do corpo que derruba a mineracao? Todo comando de corpo, menos o proprio `Activity`.</summary>
	private static bool ComandoQueInterrompeAMineracao(Protocol.C2S id) =>
		id != Protocol.C2S.Activity && ComandoDeCorpo(id);

	/// <summary>
	/// O SEGUNDO DE QUEM MINERA: confere se ainda pode, tira da reserva do planeta o que a melhor ferramenta da
	/// mochila rende, e paga O QUE SAIU -- <see cref="ReservaDePlaneta.Tirar"/> e a unica porta. Reserva que nao
	/// deu o segundo inteiro secou: paga o resto e para.
	/// </summary>
	private void TickDaMineracao()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Mineracao is not { } m) continue;

			string porque = PorQueNaoMinera(pl, m);
			if (porque.Length > 0) { PararDeMinerar(pl, porque); continue; }

			ReservaDePlaneta reserva = ReservaDe(pl.Zone)!;   // o `PorQueNaoMinera` acabou de conferir
			m.Ferramenta = Recurso.MelhorFerramenta(pl.Mochila);
			double pedido = Recurso.PorSegundo(m.Ferramenta);
			double saiu = MineraDoNadaDeTeste ? pedido : reserva.Tirar(pedido);

			m.UltimoSegundo = saiu;
			m.Total += saiu;
			DarRecursos(pl, saiu);

			if (saiu < pedido)
			{
				PararDeMinerar(pl, "a reserva deste planeta secou: você tirou o que restava. Ela volta devagar com o tempo.");
				continue;
			}
			MandarMineracao(pl);
		}
	}

	/// <summary>
	/// O ESTADO DA MINERACAO, PRO DONO DO CORPO: se esta minerando, com o que, quanto rendeu o ultimo segundo e
	/// como esta a reserva do planeta. Sai uma vez por segundo enquanto durar, e uma vez ao acabar (ou ao ser
	/// recusada) -- e esse ultimo que faz o cliente largar a pose.
	/// </summary>
	private void MandarMineracao(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		Mineracao? m = pl.Mineracao;

		var w = Protocol.Begin(Protocol.S2C.Mineracao);
		w.Put(m != null);
		w.Put((byte)(m?.Ferramenta ?? Ferramenta.Mao));
		// ANTES DO PRIMEIRO SEGUNDO ainda nao saiu nada: a tela mostra o que a ferramenta VAI render.
		w.Put((float)(m == null ? 0 : m.Total > 0 ? m.UltimoSegundo : Recurso.PorSegundo(m.Ferramenta)));
		w.Put((float)(ReservaDe(pl.Zone)?.Fracao ?? 0));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Como a reserva deste planeta se chama numa frase: o nome batizado, ou o que o jogador ouve ("Terra").</summary>
	private string NomeDaReserva(ZoneKey zona) =>
		ChaveDePlaneta.Da(zona) is { } chave ? NomeFaladoDe(zona, chave) : zona.Name;

	// =====================================================================
	// O QUE AS BANCADAS DE CLIENTE PEDEM
	// =====================================================================
	/// <summary>
	/// SO PRA BANCADA (`--diagabas` F14b e `--diagminerar`, com o servidor no mesmo processo): acerta a carteira
	/// -- e, se pedida, a tecnologia -- de um jogador, e conta ao cliente dele pelo caminho de sempre (o
	/// catalogo com o veredito de cada oferta, e a carteira atras). Falso se o jogador nao existe.
	/// </summary>
	public bool EconomiaDeTeste(int idDoJogador, double zeni, double recursos, double? tech = null)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return false;
		pl.Ficha.Zeni = zeni;
		pl.Ficha.Recursos = recursos;
		if (tech is { } nivel) pl.Ficha.techskill = nivel;
		MandarCatalogoDeObras(pl);
		return true;
	}

	/// <summary>
	/// SO PRA BANCADA (`--diagminerar`): deixa na mochila deste jogador SO esta ferramenta de minerar (a pa, a
	/// furadeira, ou nenhuma das duas) e conta ao cliente dele. Falso se o jogador nao existe.
	/// </summary>
	public bool FerramentaDeTeste(int idDoJogador, Ferramenta f)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return false;
		pl.Mochila.Tirar(CatalogoDeItens.Pa, pl.Mochila.Quantos(CatalogoDeItens.Pa));
		pl.Mochila.Tirar(CatalogoDeItens.Furadeira, pl.Mochila.Quantos(CatalogoDeItens.Furadeira));
		if (f == Ferramenta.Pa) pl.Mochila.Guardar(CatalogoDeItens.Pa);
		if (f == Ferramenta.Furadeira) pl.Mochila.Guardar(CatalogoDeItens.Furadeira);
		MandarMochila(pl, forcar: true);
		return true;
	}

	/// <summary>
	/// SO PRA BANCADA (`--diagminerar`): poe a reserva do planeta em que este jogador esta em
	/// <paramref name="quanto"/> -- enchida pelo tempo (um dia inteiro de regeneracao, mesmo com a trava de
	/// bancada ligada) e depois tirada pela porta de sempre, pro arquivo ficar sabendo -- e devolve quanto
	/// ficou. Negativo = o jogador nao existe ou a zona dele nao tem reserva.
	/// </summary>
	public double ReservaDeTeste(int idDoJogador, double quanto)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl) || ReservaDe(pl.Zone) is not { } r) return -1;

		bool trava = Recurso.ReservaNaoRegeneraDeTeste;
		Recurso.ReservaNaoRegeneraDeTeste = false;
		r.Regenerar(Recurso.HorasPraEncher * 3600.0);
		Recurso.ReservaNaoRegeneraDeTeste = trava;

		r.Tirar(r.Atual - Math.Clamp(quanto, 0, r.Maximo));
		return r.Atual;
	}

	/// <summary>SO PRA BANCADA: quanto ha na reserva do planeta deste jogador (negativo = nao ha reserva).</summary>
	public double ReservaAtualDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? pl) && ReservaDe(pl.Zone) is { } r ? r.Atual : -1;

	/// <summary>
	/// SO PRA BANCADA (`--diagabas`): mexe no zeni deste jogador POR FORA do funil -- a ficha escrita direto, como
	/// fazem a missao de cargo, o premio do torneio e o desejo de riqueza --, sem avisar ninguem. Quem tem de
	/// perceber e a vigia (<see cref="VigiarAsCarteiras"/>). Falso se o jogador nao existe.
	/// </summary>
	public bool ZeniPorForaDeTeste(int idDoJogador, double quanto)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return false;
		pl.Ficha.Zeni += quanto;
		return true;
	}

	/// <summary>SO PRA BANCADA: este jogador esta minerando, na conta do servidor?</summary>
	public bool MinerandoDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? pl) && pl.Mineracao != null;

	// O SALDO DE RECURSOS NA CONTA DO SERVIDOR ja tem porta de bancada: `RecursosDeTeste`, em `GameServer.Lenha.cs`.
}
