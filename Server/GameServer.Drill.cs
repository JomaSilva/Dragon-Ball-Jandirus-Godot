using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A DRILL -- `/obj/Power_Drill`, com a regra do dono (2026-10-10): *"a maquina "Drill" de tech 30 fica
/// pegando recursos de forma automatica no planeta q foi instalada, vc pode investir mais recurso ou zenni no
/// menu de interaçao pra deixar ela melhor e pegar mais recursos por segundo"*.
///
/// ============================ O QUE HAVIA NO LUGAR DISTO ============================
/// Nada: ela se fabricava e se assentava como qualquer obra (o catalogo a traz do DM), e nao havia uma
/// linha de codigo pra ela. Os NUMEROS e a CONTA moram em <see cref="Drill"/>; aqui mora o que acontece.
/// ====================================================================================
///
/// ============================ COMO ELA TRABALHA SEM NINGUEM OLHANDO ============================
/// Nao ha "producao por tique". Cada Drill guarda ATE QUANDO ja foi contada (`DrillDaObra.ContadoAteMs`,
/// no `mundo.json` junto com o estoque), e por em dia e fazer a conta do tempo que passou. Quem poe em dia:
///
///   * o TIQUE LENTO (<see cref="TickDasDrills"/>), de cinco em cinco segundos, pra TODAS as Drills de TODAS
///     as zonas -- com gente perto ou nao. E ele que faz a reserva do planeta cair de verdade enquanto o
///     dono esta fora (e as rochas de minerio, que nascem conforme a reserva, pararem de nascer);
///   * QUEM MEXE NELA (ver, coletar, melhorar, recolher), na hora -- o numero que o jogador ve e o de agora.
///
/// Servidor desligado, planeta sorteado que saiu da memoria: ninguem conta, e nada se perde -- a proxima
/// conta paga o tempo inteiro (ate o teto do estoque, e ate o que o planeta tiver).
/// ===============================================================================================
///
/// ============================ O QUE FOI DECIDIDO AQUI (o dono nao disse) ============================
///   * ELA NAO PEDE PARAFUSO: assentada, trabalha. E o original -- o `Power_Drill` nao tem `Bolt`.
///   * SO O DONO COLETA E MELHORA (a conta de quem a assentou, a mesma regra de recolher uma obra). Ver o
///     estado, qualquer um ve.
///   * CHEIA, ELA PARA, e o tempo parada nao volta: o estoque e o que obriga alguem a passar por ela.
///   * RECOLHIDA, NAO PERDE NADA: o estoque vai pro saldo de quem a recolhe e o nivel fica guardado na
///     ficha dele (`Fighter.DrillsRecolhidas`) -- a proxima Drill que ele assentar volta nesse nivel. O
///     item na mochila nao tem identidade pra carregar isso, e o menu da tecla E nao tem confirmacao.
///   * DESTRUIDA NA PORRADA, leva o estoque junto (como o regenerador leva a bateria).
/// ====================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): qualquer um coleta e melhora a Drill alheia. Lido pela
	/// propria linha do dono, em <see cref="ADrillEDele"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool DrillDeQualquerUmDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): a melhoria sobe o nivel mesmo quando a cobranca recusa. Lido
	/// pela propria linha da cobranca, em <see cref="MelhorarADrill"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool DrillMelhoraSemPagarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): recolher a Drill joga fora o estoque e o nivel -- o que
	/// acontece com qualquer obra de estado que vai pra mochila. Lido pela propria linha do gancho, em
	/// <see cref="AoRecolherADrill"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool DrillRecolhidaPerdeTudoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --recursosteste): as Drills da caverna sao contadas A PARTE das da superficie,
	/// cada grupo indo sozinho a reserva -- num planeta quase seco, o primeiro grupo leva tudo. Lido pela propria
	/// linha que junta os dois, em <see cref="OndeEstaAReservaDa"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool DrillDaCavernaContaAParteDeTeste;

	/// <summary>ESPIA DE BANCADA -- nulo em jogo. Recebe (quem perguntou, o estado) de cada `S2C.Drill` montado.</summary>
	internal Action<int, EstadoDeDrill>? EscutaDeDrill;

	private long _proximaLeituraDasDrills, _proximaGravacaoDasDrills;

	/// <summary>Quantas obras havia no chao na ultima volta: mudou, alguma Drill pode ter acabado de ser assentada.</summary>
	private int _obrasNaUltimaVoltaDasDrills = -1;

	/// <summary>O estoque (ou o carimbo) de alguma Drill andou desde a ultima gravacao do mundo.</summary>
	private bool _drillsMudaram;

	private readonly List<DrillDaObra> _drillsDaVez = [];
	private readonly HashSet<ZoneKey> _zonasComDrill = [];

	private bool ComandoDeDrill(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case Drill.VerboVer: VerADrill(pl); return true;
			case Drill.VerboInfo: InfoDaDrill(pl); return true;
			case Drill.VerboColetar: ColetarDaDrill(pl); return true;
			case Drill.VerboMelhorar: MelhorarADrill(pl, arg); return true;
			default: return false;
		}
	}

	// =====================================================================
	// O RELOGIO
	// =====================================================================
	/// <summary>
	/// UM RELOGIO SO PRO MUNDO, como o dos tanques e o da gravidade -- e nao um laco por maquina, que e o
	/// `while(src) ... sleep(300)` do original (`Tier2.dm:443-455`).
	/// </summary>
	private void TickDasDrills()
	{
		// A DRILL QUE ACABOU DE SER ASSENTADA ganha o estado (e o dono, o aviso de onde ela esta) no tique
		// seguinte, e nao na proxima leitura. Custa uma comparacao de inteiros por tique: o `posicionar` e de
		// outro arquivo, e esta e a porta que nao mexe nele.
		if (_noChao.Count != _obrasNaUltimaVoltaDasDrills)
		{
			_obrasNaUltimaVoltaDasDrills = _noChao.Count;
			long chegada = NowMs();
			foreach (Obra o in _noChao)
				if (o.Tipo == Drill.Tipo && o.Drill == null) EstadoDaDrill(o, chegada);
		}

		long agora = NowMs();
		if (!VenceuOPrazo(ref _proximaLeituraDasDrills, agora, Drill.SegundosEntreLeituras)) return;
		PorTodasAsDrillsEmDia(agora);

		// O ESTOQUE QUE MUDOU VAI PRO DISCO SOZINHO, de minuto em minuto. Nao e pra a Drill nao perder (o par
		// estoque + carimbo velho so faz a proxima conta comecar mais atras): e pro PLANETA nao pagar duas
		// vezes -- a reserva ja caiu, e um servidor que caisse sem gravar tiraria dela de novo o mesmo tempo.
		if (_drillsMudaram && VenceuOPrazo(ref _proximaGravacaoDasDrills, agora, Drill.SegundosEntreGravacoes))
		{
			_drillsMudaram = false;
			GravarMundo();
		}
	}

	/// <summary>
	/// O PRAZO VENCEU? Vencido, ja fica rearmado pra daqui a <paramref name="segundos"/>.
	///
	/// UM PRAZO MAIS LONGE QUE O PROPRIO INTERVALO TAMBEM VALE COMO VENCIDO: so chega la se o relogio andou
	/// pra tras (o acerto de hora da maquina, uma bancada que devolveu o adianto), e esperar por ele pararia
	/// todas as Drills do servidor ate a parede alcancar.
	/// </summary>
	private static bool VenceuOPrazo(ref long prazo, long agora, double segundos)
	{
		long intervalo = (long)(segundos * 1000);
		long falta = prazo - agora;
		if (falta > 0 && falta <= intervalo) return false;
		prazo = agora + intervalo;
		return true;
	}

	/// <summary>Poe em dia as Drills de todas as zonas -- planeta por planeta, porque a reserva e do planeta.</summary>
	private void PorTodasAsDrillsEmDia(long agora)
	{
		_zonasComDrill.Clear();
		foreach (Obra o in _noChao)
			if (o.Tipo == Drill.Tipo) _zonasComDrill.Add(OndeEstaAReservaDa(o.Zona));
		foreach (ZoneKey zona in _zonasComDrill) PorAsDrillsEmDia(zona, agora);
	}

	/// <summary>
	/// A ZONA DONA DA RESERVA de onde uma Drill desta zona tira: o planeta, pra superficie e pra CAVERNA dele
	/// (<see cref="Recurso.PlanetaDaReserva"/>); a propria zona, onde nao ha reserva (o Mirante, uma estacao).
	/// E a chave do "todas juntas": Drill na Terra e Drill na caverna da Terra dividem o mesmo numero, entao
	/// entram na mesma conta.
	/// </summary>
	private ZoneKey OndeEstaAReservaDa(ZoneKey zona) =>
		DrillDaCavernaContaAParteDeTeste ? zona : Recurso.PlanetaDaReserva(zona) ?? zona;

	/// <summary>
	/// POE EM DIA AS DRILLS QUE TIRAM DA RESERVA DESTA ZONA, todas juntas, e devolve a reserva (nulo onde nao ha).
	///
	/// TODAS JUNTAS mesmo quando quem pergunta mexe numa so: a reserva e uma, e contar so a Drill de quem
	/// abriu o menu a deixaria passar na frente das outras num planeta quase seco. "Todas" inclui as da caverna
	/// do planeta (e, perguntado pela caverna, as da superficie): ver <see cref="OndeEstaAReservaDa"/>.
	/// </summary>
	private ReservaDePlaneta? PorAsDrillsEmDia(ZoneKey zona, long agora)
	{
		zona = OndeEstaAReservaDa(zona);
		ReservaDePlaneta? reserva = ReservaDe(zona);

		_drillsDaVez.Clear();
		foreach (Obra o in _noChao)
			if (o.Tipo == Drill.Tipo && OndeEstaAReservaDa(o.Zona).Equals(zona)) _drillsDaVez.Add(EstadoDaDrill(o, agora));
		if (_drillsDaVez.Count == 0) return reserva;

		if (reserva == null)
		{
			// SEM RESERVA HA DOIS CASOS, e so um deles e "aqui nao ha o que extrair":
			//   * o lugar nao e planeta (Lookout, interior, espaco): o carimbo anda, pra a Drill nao acumular
			//     um tempo que nunca vai valer nada;
			//   * e planeta, mas ninguem sabe a reserva dele AGORA (mundo sorteado fora da memoria depois de um
			//     reinicio): a conta ESPERA. O carimbo fica onde esta, e o tempo inteiro e pago quando alguem
			//     pousar la e a reserva voltar a existir.
			if (!Espaco.EhPlaneta(zona))
				foreach (DrillDaObra d in _drillsDaVez) d.ContadoAteMs = agora;
			return null;
		}

		if (Drill.PorEmDia(_drillsDaVez, reserva, agora).Pedido > 0) _drillsMudaram = true;
		return reserva;
	}

	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>
	/// O ESTADO DESTA DRILL, nascendo na primeira vez que alguem pergunta -- pelo motivo escrito no
	/// `MaquinaPerto` da gravidade: a que ja estava no `mundo.json` de antes volta do disco sem ele.
	///
	/// O CARIMBO NASCE AGORA, e nao no dia em que a obra foi erguida: uma Drill assentada antes de este
	/// sistema existir nao ganha de presente o estoque de dias em que ela nao fazia nada.
	/// </summary>
	private DrillDaObra EstadoDaDrill(Obra o, long agora)
	{
		if (o.Drill is { } ja) return ja;

		var d = new DrillDaObra { ContadoAteMs = agora };
		o.Drill = d;
		_drillsMudaram = true;

		// QUEM A ASSENTOU ESTA AO LADO DELA (o `posicionar` foi ha um tique). Se ele tem melhorias guardadas de
		// uma Drill que recolheu, elas voltam nesta; e e a ele que a maquina diz onde esta.
		if (DonoPresente(o) is not { } dono) return d;
		int guardado = TirarOMaiorNivelGuardado(dono);
		if (guardado > 1)
		{
			d.Nivel = Drill.NivelValido(guardado);
			Avisar(dono, $"as melhorias que você guardou voltam com ela: a Drill é assentada no nível {d.Nivel}.");
		}

		ReservaDePlaneta? reserva = ReservaDe(o.Zona);
		// NA CAVERNA A FALA DIZ DE QUEM E O CHAO, como a de quem comeca a minerar: o numero que desce e o do planeta.
		string deOnde = Cavernas.EhCaverna(o.Zona) ? $"da reserva de {NomeDaReserva(o.Zona)}, que esta caverna divide com a superfície" : "dos recursos deste planeta";
		Avisar(dono, reserva == null
			? "a Drill está instalada, mas AQUI NÃO HÁ O QUE EXTRAIR: ela só trabalha num planeta -- na superfície, ou numa caverna dele."
			: reserva.Atual <= Drill.Folga
				? "a Drill está instalada, mas este planeta está ESGOTADO: ela só vai extrair o que a reserva regenerar."
				: $"a Drill começa a extrair: {Drill.Taxa(d.Nivel):0.#} por segundo {deOnde} "
				  + $"(reserva em {reserva.Fracao * 100:0}%). Aperte E nela pra coletar ou melhorar.");
		return d;
	}

	/// <summary>
	/// O DONO DESTA OBRA, se ele esta NA ZONA dela. Por CONTA, como a obra o guarda -- e so jogador tem conta
	/// (clone, boneco e NPC nascem sem). O corpo com fio ganha de um forjado da bancada.
	/// </summary>
	private ServerPlayer? DonoPresente(Obra o)
	{
		if (o.DonoConta.Length == 0 || !_zones.TryGetValue(o.Zona.Hash, out List<ServerPlayer>? naZona)) return null;

		ServerPlayer? achado = null;
		foreach (ServerPlayer p in naZona)
		{
			if (!string.Equals(p.Conta, o.DonoConta, StringComparison.OrdinalIgnoreCase)) continue;
			if (p.Peer != null) return p;
			achado ??= p;
		}
		return achado;
	}

	/// <summary>O maior nivel que este personagem guardou ao recolher uma Drill -- e a entrada sai da lista. Zero = nenhum.</summary>
	private static int TirarOMaiorNivelGuardado(ServerPlayer pl)
	{
		List<int> guardadas = pl.Ficha.DrillsRecolhidas ??= [];
		if (guardadas.Count == 0) return 0;

		int maior = guardadas.Max();
		guardadas.Remove(maior);
		return maior;
	}

	/// <summary>
	/// ESTA DRILL E DESTE JOGADOR? A regra de recolher uma obra (`PegarObra`): a conta de quem a assentou. A
	/// que nao tem dono (ninguem a assentou) e de quem chegar.
	/// </summary>
	private bool ADrillEDele(Obra o, ServerPlayer pl) =>
		// (2026-10-25) a sem dono continua de quem chegar; e a liberada, e a do corpo que o Neo Tuffle dirige -- ver `MexeComoDono`
		DrillDeQualquerUmDeTeste || MexeComoDono(o, pl, semDonoEDeTodos: true);

	/// <summary>
	/// A DRILL AO ALCANCE DA MAO (a mais perto, como toda maquina do menu da tecla E), JA POSTA EM DIA -- ela
	/// e as outras do planeta. Nulo = nao ha nenhuma por perto.
	/// </summary>
	private DrillDaObra? DrillPerto(ServerPlayer pl, out Obra? obra, out ReservaDePlaneta? reserva)
	{
		reserva = null;
		obra = ObraQueAceita(pl, Drill.VerboColetar);
		if (obra == null) return null;

		long agora = NowMs();
		DrillDaObra d = EstadoDaDrill(obra, agora);
		reserva = PorAsDrillsEmDia(obra.Zona, agora);
		return d;
	}

	private EstadoDeDrill EstadoPra(ServerPlayer pl, Obra o, DrillDaObra d, ReservaDePlaneta? reserva) => new(
		o.Id, d.Nivel, d.Estoque, Drill.Situacao(d, reserva), reserva?.Fracao ?? 0,
		ADrillEDele(o, pl), o.DonoNome ?? "", pl.Ficha.Zeni, pl.Ficha.Recursos);

	// =====================================================================
	// OS VERBOS DO MENU DA TECLA E
	// =====================================================================
	/// <summary>
	/// "COMO ESTA A DRILL?" -- o painel do menu pergunta ao abrir, e o servidor responde com o `S2C.Drill`
	/// (calado: nao escreve no chat). De longe, a resposta e "nenhuma".
	/// </summary>
	private void VerADrill(ServerPlayer pl)
	{
		DrillDaObra? d = DrillPerto(pl, out Obra? obra, out ReservaDePlaneta? reserva);
		MandarDrill(pl, d == null || obra == null ? EstadoDeDrill.Nenhuma : EstadoPra(pl, obra, d, reserva));
	}

	/// <summary>"Ver estado": as mesmas linhas do painel, no chat -- onde elas ficam.</summary>
	private void InfoDaDrill(ServerPlayer pl)
	{
		DrillDaObra? d = DrillPerto(pl, out Obra? obra, out ReservaDePlaneta? reserva);
		if (d == null || obra == null) { Avisar(pl, "não há Drill por perto."); return; }

		Avisar(pl, $"-- {NomeDaObra(obra)} --");
		foreach (string linha in Drill.Descrever(EstadoPra(pl, obra, d, reserva))) Avisar(pl, "  " + linha);
		// (2026-10-19) O TETO DE QUEM PERGUNTA: o painel e o mesmo pra todo mundo, e a tecnologia de cada um leva a Drill a um nivel.
		int teto = TetoPorTecnologia.NivelDaDrill(pl.Ficha.techskill);
		if (!Drill.NoMaximo(d.Nivel) && teto < Drill.NivelMaximo)
			Avisar(pl, d.Nivel >= teto ? $"  a sua tecnologia já não leva esta Drill além do nível {teto}."
				: $"  a sua tecnologia leva esta Drill até o nível {teto}.");
	}

	private void ColetarDaDrill(ServerPlayer pl)
	{
		DrillDaObra? d = DrillPerto(pl, out Obra? obra, out ReservaDePlaneta? reserva);
		if (d == null || obra == null) { Avisar(pl, "não há Drill por perto."); return; }
		if (!ADrillEDele(obra, pl)) { Avisar(pl, $"esta Drill é de {obra.DonoNome}: só o dono coleta."); return; }

		double coletado = Drill.Coletar(d);
		if (coletado <= 0)
		{
			Avisar(pl, Drill.Situacao(d, reserva) switch
			{
				SituacaoDaDrill.SemReserva => "a Drill está vazia: aqui não há o que extrair.",
				SituacaoDaDrill.PlanetaEsgotado => "a Drill está vazia: o planeta está esgotado, e ela só extrai o que a reserva regenerar.",
				_ => "a Drill ainda não juntou nada.",
			});
			MandarDrill(pl, EstadoPra(pl, obra, d, reserva));
			return;
		}

		// O MUNDO E A FICHA VAO PRO DISCO JUNTOS, na hora: o estoque saiu de um e entrou na outra, e um
		// arquivo gravado sem o par deixaria o recurso nos dois lugares, ou em nenhum.
		DarRecursos(pl, coletado);
		GravarMundo();
		Persistir(pl);

		GD.Print($"[server] {pl.Name} coletou {coletado:0} recursos da Drill #{obra.Id} em {obra.Zona}");
		Avisar(pl, $"você coleta {coletado:N0} recursos da Drill. Agora você tem {Math.Floor(pl.Ficha.Recursos + Drill.Folga):N0}.");
		MandarDrill(pl, EstadoPra(pl, obra, d, reserva));
	}

	/// <summary>
	/// MELHORAR: um nivel por vez, pago em zeni ou -- pelo dobro -- em recursos. Quem escolhe a moeda e o
	/// jogador (o argumento do verbo), e quem cobra e o contrato (`Cobrar`): sem saldo NAQUELA moeda ele
	/// recusa, e nao completa com a outra.
	/// </summary>
	private void MelhorarADrill(ServerPlayer pl, string comOQue)
	{
		DrillDaObra? d = DrillPerto(pl, out Obra? obra, out ReservaDePlaneta? reserva);
		if (d == null || obra == null) { Avisar(pl, "não há Drill por perto."); return; }
		if (!ADrillEDele(obra, pl)) { Avisar(pl, $"esta Drill é de {obra.DonoNome}: só o dono melhora."); return; }
		if (Drill.NoMaximo(d.Nivel)) { Avisar(pl, $"esta Drill já está no nível máximo ({Drill.NivelMaximo})."); return; }
		// (2026-10-19) O TETO DE QUEM PAGA (dono: *"o mesmo pra maquina de regeneraçao e drill"*) -- ver `TetoPorTecnologia`. A
		// recusa vem antes da cobranca, e o painel volta com o estado de agora.
		int teto = TetoPorTecnologia.NivelDaDrill(pl.Ficha.techskill);
		if (d.Nivel >= teto)
		{
			Avisar(pl, TetoPorTecnologia.Recusa("esta Drill", pl.Ficha.techskill, $"nível {teto}", proporcional: true));
			MandarDrill(pl, EstadoPra(pl, obra, d, reserva));
			return;
		}

		Moeda moeda;
		switch (comOQue)
		{
			case Drill.PagarComZeni: moeda = Moeda.Zeni; break;
			case Drill.PagarComRecursos: moeda = Moeda.Recursos; break;
			default: Avisar(pl, "escolha com o que pagar a melhoria: zeni ou recursos."); return;
		}

		double custo = Drill.CustoDaMelhoria(d.Nivel);
		bool emRecursos = moeda == Moeda.Recursos;
		double preco = emRecursos ? Recurso.CustoEmRecursos(custo) : custo;
		string nomeDaMoeda = emRecursos ? "recursos" : "zeni";

		if (!Cobrar(pl, custo, moeda) && !DrillMelhoraSemPagarDeTeste)
		{
			double tem = emRecursos ? pl.Ficha.Recursos : pl.Ficha.Zeni;
			Avisar(pl, $"levar a Drill ao nível {d.Nivel + 1} custa {preco:N0} {nomeDaMoeda} -- você tem {Math.Floor(tem + Drill.Folga):N0}.");
			MandarDrill(pl, EstadoPra(pl, obra, d, reserva));
			return;
		}

		// A CONTA DO TEMPO JA FOI FECHADA NO NIVEL VELHO (o `DrillPerto` pos em dia antes): a taxa nova so
		// vale daqui pra frente.
		d.Nivel++;
		GravarMundo();
		Persistir(pl);

		GD.Print($"[server] {pl.Name} melhorou a Drill #{obra.Id} pro nivel {d.Nivel} ({preco:0} {nomeDaMoeda})");
		Avisar(pl, $"você melhora a Drill por {preco:N0} {nomeDaMoeda}: nível {d.Nivel}, "
				   + $"{Drill.Taxa(d.Nivel):0.#} recursos por segundo e estoque de {Drill.Teto(d.Nivel):N0}.");
		MandarCatalogoDeObras(pl);   // o zeni mudou, e a aba Tech mostra o que ele ainda compra
		MandarDrill(pl, EstadoPra(pl, obra, d, reserva));
	}

	// =====================================================================
	// RECOLHER
	// =====================================================================
	/// <summary>
	/// O DONO RECOLHE A DRILL PRA MOCHILA (o `pegar` de toda obra, que chama isto antes de tira-la do chao):
	/// o estoque vai pro saldo dele e o nivel fica guardado na ficha -- ver "o que foi decidido aqui" no
	/// cabecalho. Quem chega aqui ja passou pelas guardas do `PegarObra`: e o dono, e a mochila tem lugar.
	/// </summary>
	private void AoRecolherADrill(ServerPlayer pl, Obra o)
	{
		if (DrillRecolhidaPerdeTudoDeTeste || o.Drill is not { } d) return;

		PorAsDrillsEmDia(o.Zona, NowMs());
		double coletado = Drill.Coletar(d);
		if (coletado > 0)
		{
			DarRecursos(pl, coletado);
			Avisar(pl, $"o estoque da Drill vem junto: {coletado:N0} recursos.");
		}

		if (d.Nivel <= 1) return;
		(pl.Ficha.DrillsRecolhidas ??= []).Add(d.Nivel);
		Avisar(pl, $"as melhorias ficam com você: a próxima Drill que você assentar volta no nível {d.Nivel}.");
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	/// <summary>O estado de UMA Drill, pra quem perguntou e so pra ele (`S2C.Drill`, ver <see cref="FioDaDrill"/>).</summary>
	private void MandarDrill(ServerPlayer pl, EstadoDeDrill e)
	{
		EscutaDeDrill?.Invoke(pl.Id, e);
		if (pl.Peer == null) return;

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Drill);
		FioDaDrill.Escrever(w, e);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
