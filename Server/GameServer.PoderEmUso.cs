using Jandirus.Core.Stats;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// **POWER UP E POWER DOWN, DO LADO DO SERVIDOR** -- relaxar e ficar pronto, o retrato do estado que vai pra
/// tela de quem joga, e as quatro pontas que faltavam em volta do sigilo que o lote G9 ja tinha portado
/// (`Conceal_Power` e `Power_Control`, `GameServer.Tecnicas.G9.cs`). Pedido do dono de 2026-10-11:
/// *"Verifique pra mim se a capacidade de dar power up e power down pra poder esconder a presença ou ficar
/// mais fraco de propósito ja ta no jogo, se nao implemente."* A regra pura mora em `Core.Stats.PoderEmUso`.
///
/// ============================ O QUE JA EXISTIA, E O QUE FALTAVA ============================
/// JA EXISTIA: baixar pra uma porcentagem e esconder o poder (os dois verbos do G9, atras do nivel 5 de Basic
/// Ki Control, como no DM -- `Mind.dm:279-283`), o poder expresso lendo os dois (`Fighter.Power.cs`), o Sense,
/// o scouter, a telepatia e a IA lendo o poder expresso, e a tecla C subindo o `powerMod` de volta
/// (`CargaDeKi`, estagio `Retomando`).
///
/// FALTAVA, e mora aqui:
///   1. **RELAXAR / FICAR PRONTO** (<see cref="AlternarProntidao"/>): o `Toggle Readiness` do DM, verbo de
///      todo corpo, com a raiva que nao deixa relaxar (<see cref="ConferirARaivaDeQuemRelaxa"/>);
///   2. **A TELA SABER** (<see cref="MandarPoderEmUso"/>): nada dizia ao dono do corpo quanto do proprio poder
///      ele estava usando nem se o escondia -- o "N% efetivo" do HUD mede o corpo (Ki, ferimento, peso), e nao
///      o controle de poder. Um jogador a 1% via "100% efetivo" e batia como uma crianca;
///   3. **QUEM ESCONDE CONTINUA ESCONDIDO** (<see cref="_escondendoOPoder"/>): o `isconcealed` da ficha e um
///      campo so, escrito tambem pela invisibilidade, pelo Sneak e pelo embate de Zanzoken -- que o apagavam ao
///      acabar, levando junto o sigilo de quem o tinha ligado de proposito;
///   4. **O PODER NAO VEM DO DISCO** (<see cref="AcordarComOPoderInteiro"/>): `powerMod` e `tmp` no DM
///      (`base.dm:38`) e aqui a ficha vai inteira pro save -- quem deslogava a 1% relogava a 1%;
///   5. **O SIGILO NAO ENTREGA O BP DE NINGUEM** (<see cref="PoderQueSeCompara"/>, e o corte do multiplicador
///      em `GameServer.Sigilo.cs`): com o poder expresso travado em 5 -- uma constante que todo mundo conhece
///      -- as leituras RELATIVAS viravam absolutas. Ver os dois cabecalhos;
///   6. **CAIDO NAO MEXE** (<see cref="PorQueNaoMexeNoPoder"/>).
/// ===========================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 0. O ESTADO DE SESSAO, E OS DEFEITOS INJETADOS
	// =====================================================================

	/// <summary>
	/// QUEM LIGOU O `Conceal_Power` POR VONTADE PROPRIA. O `Fighter.isconcealed` e o EFEITO (o poder expresso
	/// travado), e tem outros quatro escritores: a invisibilidade, o Sneak, a invisibilidade de admin e o embate
	/// de Zanzoken, todos pelo `_invisiveis`. Sem esta lista os quatro, ao acabar, apagavam o campo e o sigilo de
	/// quem o ligou ia junto, calado -- e o proprio `Conceal_Power`, que so invertia o campo, MOSTRAVA o poder de
	/// quem o apertasse invisivel. No DM o `isconcealed` so e escrito pelo `Conceal_Power`
	/// (`Power Control.dm:77` e `:82`): a mistura e deste port.
	///
	/// De sessao, por id: no DM o campo e `tmp` (`:6`), e o login o apaga (`AcordarVisivel`).
	/// </summary>
	private readonly HashSet<int> _escondendoOPoder = [];

	/// <summary>O que cada TELA recebeu por ultimo (a tela junto do valor: id se reusa -- ver `_fatosMandados`).</summary>
	private readonly Dictionary<int, (NetPeer? Tela, PoderEmUsoState Estado)> _poderEmUsoMandado = [];

	/// <summary>
	/// A BANCADA ESCUTA O QUE SAIU, como a `EscutaDeSentidos`: pacote que saiu no fio nao volta, e corpo forjado
	/// nao tem `Peer`. Nula em jogo; com ela ligada o tique tambem considera corpos sem tela.
	/// </summary>
	internal static List<(int Quem, NetDataWriter Pacote)>? EscutaDoPoderEmUso;

	/// <summary>O contador que tira os 5 Hz do retrato de dentro de um tique que roda a 30.</summary>
	private int _tiquesDoPoderEmUso;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): nocauteado, cadaver ou possuido voltam a mexer no proprio poder
	/// (o estado de antes de 2026-10-11). Lido pela propria <see cref="PorQueNaoMexeNoPoder"/>.
	/// </summary>
	internal bool PoderMexeCaidoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o sigilo de quem o ligou de proposito volta a cair quando uma
	/// tecnica de sumir (ou um embate de Zanzoken) acaba. Lido pela propria linha do conserto, em
	/// <see cref="TickDoPoderEmUso"/>.
	/// </summary>
	internal bool SigiloCaiComOutraTecnicaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o `powerMod` gravado no disco volta a valer no login -- quem saiu
	/// do jogo a 1% entra a 1%. Lido pela propria <see cref="AcordarComOPoderInteiro"/>.
	/// </summary>
	internal bool PoderVemDoDiscoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): quem esconde o poder volta a medir os outros contra o proprio
	/// poder expresso, travado em 5 -- e le o BP exato de cada um na aba Sense. Lido pela propria
	/// <see cref="PoderQueSeCompara"/>.
	/// </summary>
	internal static bool SenseContraOPisoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o multiplicador total de quem esconde o poder volta a viajar
	/// inteiro pra quem nao tem scouter -- `5 / BP`, de onde o BP base sai por uma divisao. Lido pela propria linha
	/// do corte, em `FichaVisivel` (`GameServer.Sigilo.cs`).
	/// </summary>
	internal static bool MultiplicadorEntregaOBpDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o retrato do poder em uso so sai uma vez por tela -- quem baixa o
	/// poder continua lendo "100%". Lido pela propria <see cref="MandarPoderEmUso"/>.
	/// </summary>
	internal bool RetratoDoPoderCongeladoDeTeste;

	// =====================================================================
	// 1. QUEM PODE MEXER NO PROPRIO PODER
	// =====================================================================

	/// <summary>
	/// **ESTE CORPO PODE MEXER NO PROPRIO PODER AGORA?** Vazio = pode; senao, a frase. A pergunta dos tres
	/// verbos (baixar, esconder, relaxar/ficar pronto).
	///
	/// O DM NAO PERGUNTA NADA (`ReadyUp`, `Conceal_Power` e `Power_Control` nao tem um `if(KO)`): la o nocauteado
	/// troca de estado no chao. Aqui vale a regra da casa pros verbos de corpo (`GameServer.Validacoes.cs`,
	/// "o corpo pode?"), e a linha de corte e a mesma da tecla C (`Carregar`): nocauteado e cadaver nao mexem, e o
	/// fantasma de pe no Outro Mundo mexe como um vivo. O possuido (a fera, a furia lendaria) ja e barrado no
	/// despacho do pacote (`ComandoDeCorpo`); a linha daqui e pra quem chama por dentro.
	/// </summary>
	private string PorQueNaoMexeNoPoder(ServerPlayer pl)
	{
		if (PoderMexeCaidoDeTeste) return "";
		if (pl.Ficha.KO) return "caído você não controla o próprio poder.";
		if (EhCadaver(pl)) return "um corpo sem vida não controla poder nenhum.";
		if (SemAsRedeas(pl)) return "você não manda neste corpo agora.";
		return "";
	}

	/// <summary>
	/// O PODER MUDOU PELA MAO DE QUEM JOGA: o poder expresso acompanha NA HORA (e nao no proximo tique de ficha,
	/// ate 200 ms depois -- ver <see cref="RepercutirPoder"/>: nessa janela o soco, a guarda e o Sense alheio ainda
	/// leriam o poder de antes), a ficha vai junto e o retrato do estado tambem.
	/// </summary>
	private void AoMexerNoPoder(ServerPlayer pl)
	{
		RepercutirPoder(pl);
		MandarFicha(pl);
		MandarPoderEmUso(pl);
	}

	// =====================================================================
	// 2. RELAXAR / FICAR PRONTO -- `Power Control.dm:39-52`
	// =====================================================================

	/// <summary>
	/// O `Toggle Readiness` DO ORIGINAL: com o poder inteiro, relaxa (1% do poder); em qualquer ponto abaixo, fica
	/// pronto (100%, na hora). E verbo de TODO corpo (`mob/default/verb/ReadyUp`): nao pede o controle de ki que os
	/// outros dois pedem -- e e por ele que quem ainda nao sabe conter o poder numa porcentagem consegue ficar
	/// fraco de proposito, e voltar.
	///
	/// As frases do DM: "You are relaxed." / "You are now ready." (`:43`, `:50`). A raiva e conferida aqui mesmo,
	/// e nao so no tique: quem tenta relaxar furioso ouve as duas frases do `RelaxCheck` no mesmo aperto.
	/// </summary>
	private void AlternarProntidao(ServerPlayer pl)
	{
		if (PorQueNaoMexeNoPoder(pl) is { Length: > 0 } nao) { Avisar(pl, nao); return; }

		bool relaxou = PoderEmUso.AlternarProntidao(pl.Ficha);
		Avisar(pl, relaxou
			? "você relaxa: o seu poder cai ao mínimo (1%). Aperte de novo pra ficar pronto."
			: "você fica pronto: o seu poder volta inteiro.");
		if (relaxou) ConferirARaivaDeQuemRelaxa(pl);
		AoMexerNoPoder(pl);
	}

	/// <summary>
	/// A RAIVA ACORDA QUEM RELAXA -- o `RelaxCheck` (`Power Control.dm:90-95`), com as duas frases dele: *"You can't
	/// relax, you're just too angry!"* e *"You are now itching for a fight."*. Devolve se acordou.
	///
	/// A raiva deste port e DERIVADA das janelas (`ProjetarRaiva`), e as duas que existem -- a comum, do amigo
	/// nocauteado, e a extrema, do luto -- ficam acima do limiar enquanto duram: furioso nao se relaxa.
	/// </summary>
	private bool ConferirARaivaDeQuemRelaxa(ServerPlayer pl)
	{
		if (!PoderEmUso.ARaivaAcorda(pl.Ficha)) return false;
		Avisar(pl, "você não consegue relaxar: está furioso demais!");
		Avisar(pl, "o seu poder volta inteiro -- você está louco por uma briga.");
		return true;
	}

	// =====================================================================
	// 3. O TIQUE
	// =====================================================================

	/// <summary>
	/// O TIQUE DO PODER EM USO. Duas cadencias no mesmo metodo:
	///
	///   * a 30 Hz, e ANTES do tique de ficha: quem esconde o poder de proposito continua escondido. Uma tecnica de
	///     sumir que acaba escreve `isconcealed = false`; a conta de poder (`PowerLevel`) so roda no tique de
	///     ficha, entao repor o campo aqui, antes dela, nao deixa o poder de verdade aparecer nem por um tique --
	///     e o Sense de quem estava perto nao pisca. O conjunto quase sempre esta vazio;
	///   * a 5 Hz (a cadencia do `RelaxCheck`, que no DM anda com o `GlobalStats`): a raiva de quem relaxou, e o
	///     retrato pra tela -- que so sai quando muda.
	/// </summary>
	private void TickDoPoderEmUso()
	{
		if (!SigiloCaiComOutraTecnicaDeTeste)
			foreach (int id in _escondendoOPoder)
				if (_players.TryGetValue(id, out ServerPlayer? quem) && !quem.Ficha.isconcealed)
					quem.Ficha.isconcealed = true;

		if (++_tiquesDoPoderEmUso % TicksEntreFatos != 0) return;

		foreach (ServerPlayer pl in _players.Values)
		{
			if (!EhJogador(pl) && EscutaDoPoderEmUso == null) continue;   // NPC nao relaxa nem tem tela
			if (ConferirARaivaDeQuemRelaxa(pl)) AoMexerNoPoder(pl);
			MandarPoderEmUso(pl);
		}

		// A FAXINA, como a dos fatos: id de quem saiu nao fica na memoria.
		if (_poderEmUsoMandado.Count <= _players.Count) return;
		foreach (int id in _poderEmUsoMandado.Keys.ToList())
			if (!_players.ContainsKey(id)) _poderEmUsoMandado.Remove(id);
	}

	// =====================================================================
	// 4. O RETRATO PRA TELA
	// =====================================================================

	/// <summary>
	/// O RETRATO DESTE CORPO, AGORA: quanto do proprio poder esta em uso e os quatro bits. E a unica funcao que
	/// decide o que a tela le -- a bancada a chama direto, e o tique a chama por <see cref="MandarPoderEmUso"/>.
	/// </summary>
	internal PoderEmUsoState PoderEmUsoDe(ServerPlayer pl)
	{
		BitsDoPoderEmUso bits = BitsDoPoderEmUso.Nenhum;
		if (pl.Ficha.isconcealed) bits |= BitsDoPoderEmUso.Escondido;
		if (_escondendoOPoder.Contains(pl.Id)) bits |= BitsDoPoderEmUso.PorVontade;
		if (_sigiloPronto.TryGetValue(pl.Id, out long livre) && NowMs() < livre) bits |= BitsDoPoderEmUso.SigiloEmEspera;
		if (PoderEmUso.Relaxado(pl.Ficha)) bits |= BitsDoPoderEmUso.Relaxado;
		return new PoderEmUsoState((byte)PoderEmUso.Porcento(pl.Ficha), bits);
	}

	/// <summary>Manda o retrato deste jogador se mudou -- ou se esta TELA ainda nao recebeu nenhum.</summary>
	private void MandarPoderEmUso(ServerPlayer pl)
	{
		NetPeer? tela = pl.Peer;
		if (tela == null && EscutaDoPoderEmUso == null) return;

		PoderEmUsoState agora = PoderEmUsoDe(pl);
		if (_poderEmUsoMandado.TryGetValue(pl.Id, out (NetPeer? Tela, PoderEmUsoState Estado) antes)
			&& ReferenceEquals(antes.Tela, tela) && (antes.Estado == agora || RetratoDoPoderCongeladoDeTeste))
			return;
		_poderEmUsoMandado[pl.Id] = (tela, agora);

		NetDataWriter w = Protocol.Begin(Protocol.S2C.PoderEmUso);
		agora.Write(w);
		EscutaDoPoderEmUso?.Add((pl.Id, w));
		tela?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// 5. O LOGIN, E QUEM SAI
	// =====================================================================

	/// <summary>
	/// O PODER CONTIDO NAO VEM DO DISCO. No DM o `powerMod` e `tmp` (`base.dm:37-38`): todo login nasce com o poder
	/// inteiro. Aqui a `Fighter` vai inteira pro save, e quem saia do jogo relaxado (ou a 10%) voltava assim -- sem
	/// nada na tela que dissesse. Chamado no login, ao lado do `AcordarVisivel`, que faz o mesmo pelo `isconcealed`.
	/// Um valor que nao e numero (um save estragado) tambem volta a 1.
	/// </summary>
	private void AcordarComOPoderInteiro(ServerPlayer pl)
	{
		if (PoderVemDoDiscoDeTeste && double.IsFinite(pl.Ficha.powerMod)) return;
		pl.Ficha.powerMod = PoderEmUso.Cheio;
	}

	/// <summary>Este id saiu do mundo: o que era dele some junto (id se reusa). Chamado pelo `EsquecerG9`.</summary>
	private void EsquecerOPoderEmUso(int id)
	{
		_escondendoOPoder.Remove(id);
		_poderEmUsoMandado.Remove(id);
	}

	// =====================================================================
	// 6. O SIGILO NAO ENTREGA O BP DE NINGUEM
	// =====================================================================

	/// <summary>
	/// **CONTRA QUE PODER ESTE CORPO MEDE OS OUTROS** -- o denominador do "N% do seu poder" do Sense
	/// (`GameServer.Sentidos.cs`) e do "parece ter Nx o seu poder" da Transmissao Instantanea
	/// (`GameServer.Tecnicas.G11.cs`).
	///
	/// E o proprio poder EXPRESSO, como no DM (`HtmlUI.dm:375`, `round((D.expressedBP/max(expressedBP,1))*100,1)`)
	/// -- menos quando ele esta ESCONDIDO. Escondido, o expresso e 5 (`base.dm:114-115`), uma constante que qualquer
	/// jogador conhece: a razao contra ela e o BP do outro com outra unidade ("200000% do seu poder" e 10.000 de BP,
	/// de cabeca). No DM e assim; aqui a regra principal do dono vale mais (*"sem scouter vc n tem como saber seu bp
	/// de nenhuma forma, oq vc sabe é se alguem é mais forte ou mais fraco q vc em porcentagem"*).
	///
	/// DIVERGENCIA DECLARADA: escondido, o corpo mede os outros contra o proprio BP BASE -- o que ele vale sem forma
	/// e sem carga. Nenhum dos dois lados da razao e conhecido de quem le, e ela continua dizendo o que o dono quer
	/// que ela diga: mais forte ou mais fraco que voce. (O mesmo vale pra quem some por invisibilidade, Sneak ou
	/// embate: todos travam o expresso em 5 pelo mesmo campo.)
	/// </summary>
	internal static double PoderQueSeCompara(ServerPlayer eu) =>
		eu.Ficha.isconcealed && !SenseContraOPisoDeTeste ? eu.Ficha.BP : eu.Ficha.expressedBP;
}
