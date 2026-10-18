using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// **POWER UP E POWER DOWN, DO LADO DO SERVIDOR** -- relaxar e ficar pronto, o retrato do estado que vai pra
/// tela de quem joga, e as pontas em volta da barra de poder que o lote G9 portou (`Power_Control`,
/// `GameServer.Tecnicas.G9.cs`). Pedido do dono de 2026-10-11: *"Verifique pra mim se a capacidade de dar power
/// up e power down pra poder esconder a presença ou ficar mais fraco de propósito ja ta no jogo, se nao
/// implemente."* A regra pura mora em `Core.Stats.PoderEmUso`.
///
/// ============================ DOIS GESTOS (regra do dono, 2026-10-12) ============================
/// Ate 2026-10-12 eram tres, como no DM: a barra, relaxar (1% do poder, de todo corpo) e esconder (o
/// `Conceal_Power`, com trava de 5 s). O dono jogou e fundiu os dois ultimos -- *"tire o botao de esconder o ki e
/// faça com q o de relaxar faca o poder do personagem cair pra 5 de poder de luta sempre, ficando invisivel pra
/// scouters etc, mas extremamente fraco fisicamente"* -- e fechou a porta: *"relaxar e o poder cair deveria ser
/// somente pra quem sabe controlar o proprio poder"*. O que cada gesto faz esta no cabecalho de
/// `Core.Stats.PoderEmUso`. Deste lado mora:
///
///   1. **RELAXAR / FICAR PRONTO** (<see cref="AlternarProntidao"/>): so relaxa quem tem o controle de ki; ficar
///      pronto nao pede nada. Com a raiva que nao deixa relaxar (<see cref="ConferirARaivaDeQuemRelaxa"/>) e a
///      carga que acorda (<see cref="AcordarPelaCarga"/>);
///   2. **A TELA SABER** (<see cref="MandarPoderEmUso"/>): o "N% efetivo" do HUD mede o corpo (Ki, ferimento,
///      peso), e nao o controle de poder. Um jogador a 1% via "100% efetivo" e batia como uma crianca;
///   3. **O PODER NAO VEM DO DISCO** (<see cref="AcordarComOPoderInteiro"/>): `powerMod` e `tmp` no DM
///      (`base.dm:38`) e aqui a ficha vai inteira pro save -- quem deslogava a 1% (ou relaxado) relogava assim;
///   4. **ESCONDIDO NAO ENTREGA O BP DE NINGUEM** (<see cref="PoderQueSeCompara"/>, e o corte do multiplicador
///      em `GameServer.Sigilo.cs`): com o poder expresso travado em 5 -- uma constante que todo mundo conhece
///      -- as leituras RELATIVAS viravam absolutas. Ver os dois cabecalhos;
///   5. **A BARRA NAO ESCONDE** (<see cref="TemEnergiaQueSeSente"/>): o piso dos 5 de quem nao se sente e medido
///      sem ela;
///   6. **CAIDO NAO MEXE** (<see cref="PorQueNaoMexeNoPoder"/>).
///
/// (O QUE SAIU COM O `Conceal_Power`: a trava de 5 s, a lista de quem escondia por vontade e a reposicao do
/// `isconcealed` a 30 Hz -- relaxar e um campo proprio da ficha, que tecnica de sumir nenhuma escreve.)
/// ===================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 0. O ESTADO DE SESSAO, E OS DEFEITOS INJETADOS
	// =====================================================================

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
	/// DEFEITO INJETADO (bancada `--poderteste`): relaxar volta a ser de todo corpo, como no DM -- quem nunca
	/// aprendeu a controlar o proprio poder some do scouter com um botao. Lido pela propria linha da porta, em
	/// <see cref="AlternarProntidao"/>.
	/// </summary>
	internal bool RelaxarSemControleDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o `powerMod` e o relaxado gravados no disco voltam a valer no
	/// login -- quem saiu do jogo a 1% entra a 1%. Lido pela propria <see cref="AcordarComOPoderInteiro"/>.
	/// </summary>
	internal bool PoderVemDoDiscoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): quem esta escondido volta a medir os outros contra o proprio
	/// poder expresso, travado em 5 -- e le o BP exato de cada um na aba Sense. Lido pela propria
	/// <see cref="PoderQueSeCompara"/>.
	/// </summary>
	internal static bool SenseContraOPisoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o multiplicador total de quem esta escondido volta a viajar
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
	/// **ESTE CORPO PODE MEXER NO PROPRIO PODER AGORA?** Vazio = pode; senao, a frase. A pergunta dos dois
	/// verbos (baixar, relaxar/ficar pronto).
	///
	/// O DM NAO PERGUNTA NADA (`ReadyUp` e `Power_Control` nao tem um `if(KO)`): la o nocauteado troca de estado
	/// no chao. Aqui vale a regra da casa pros verbos de corpo (`GameServer.Validacoes.cs`, "o corpo pode?"), e a
	/// linha de corte e a mesma da tecla C (`Carregar`): nocauteado e cadaver nao mexem, e o fantasma de pe no
	/// Outro Mundo mexe como um vivo. O possuido (a fera, a furia lendaria) ja e barrado no despacho do pacote
	/// (`ComandoDeCorpo`); a linha daqui e pra quem chama por dentro.
	/// </summary>
	private string PorQueNaoMexeNoPoder(ServerPlayer pl)
	{
		if (PoderMexeCaidoDeTeste) return "";
		if (pl.Ficha.KO) return "caído você não controla o próprio poder.";
		if (EhCadaver(pl)) return "um corpo sem vida não controla poder nenhum.";
		if (SemAsRedeas(pl)) return "você não manda neste corpo agora.";
		if (Jandirus.Core.Races.BioAndroids.EmLarva(pl.Ficha)) return FraseDaLarvaSelada;   // (2026-10-17) a barra tambem
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
	/// O `Toggle Readiness` DO ORIGINAL, com a regra do dono de 2026-10-12: com o poder inteiro, RELAXA -- o poder
	/// expresso trava em 5, o Sense, o scouter e a telepatia deixam de achar o corpo, e o vigor volta o mais
	/// depressa que ha; relaxado, ou em qualquer ponto abaixo de 100% da barra, FICA PRONTO (100%, na hora).
	///
	/// **RELAXAR PEDE CONTROLE DE KI** (o `Power_Control`, do degrau 5 de Basic Ki Control): *"relaxar e o poder
	/// cair deveria ser somente pra quem sabe controlar o proprio poder"*. DIVERGENCIA DECLARADA de
	/// `Power Control.dm:39` (`mob/default/verb/ReadyUp`, de todo corpo). FICAR PRONTO NAO PEDE NADA: ninguem
	/// fica preso abaixo do proprio poder por ter perdido a porta. A porta e conferida ANTES do estado do corpo,
	/// pra a recusa de quem nao a tem ser sempre a mesma (e a que a tabela de requisitos do menu declara).
	///
	/// As frases do DM: "You are relaxed." / "You are now ready." (`:43`, `:50`). A raiva e conferida aqui mesmo,
	/// e nao so no tique: quem tenta relaxar furioso ouve as duas frases do `RelaxCheck` no mesmo aperto.
	/// </summary>
	private void AlternarProntidao(ServerPlayer pl)
	{
		if (PoderEmUso.VaiRelaxar(pl.Ficha) && !RelaxarSemControleDeTeste && !SabeTecnica(pl, PoderEmUso.VerboDoControle))
		{
			Avisar(pl, "você ainda não sabe controlar o próprio poder: relaxar pede Basic Ki Control no nível 5.");
			return;
		}
		if (PorQueNaoMexeNoPoder(pl) is { Length: > 0 } nao) { Avisar(pl, nao); return; }

		bool relaxou = PoderEmUso.AlternarProntidao(pl.Ficha);
		Avisar(pl, relaxou
			? "você relaxa: o seu poder cai ao mínimo e ninguém mais sente a sua energia -- nem scouter. "
			  + "Você fica fraco de verdade até ficar pronto de novo."
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
		Avisar(pl, "você não consegue relaxar: está furioso demais!", Jandirus.Net.Protocol.Fala.AvisoDeRaiva);
		Avisar(pl, "o seu poder volta inteiro -- você está louco por uma briga.", Jandirus.Net.Protocol.Fala.AvisoDeRaiva);
		return true;
	}

	/// <summary>
	/// REUNIR ENERGIA ACORDA QUEM RELAXA (`PoderEmUso.ACargaAcorda`): chamado pelo `Carregar` quando a tecla C e
	/// aceita. O corpo deixa de estar relaxado e a barra comeca do piso -- a carga a sobe aos poucos.
	///
	/// SO PRA QUEM TEM CONTROLE DE KI (`canPower`): sem ele a carga so enche o tanque e nunca sobe a barra
	/// (`CargaDeKi.Passo`), e acordar no piso deixaria o corpo preso em 1% do poder. Esse corpo continua relaxado
	/// enquanto enche o tanque, e volta ficando pronto.
	/// </summary>
	private void AcordarPelaCarga(ServerPlayer pl)
	{
		if (!CargaDeKi.TemControle(pl.Ficha) || !PoderEmUso.ACargaAcorda(pl.Ficha)) return;
		Avisar(pl, "você deixa de relaxar: a sua energia volta a aparecer, e sobe aos poucos enquanto você a reúne.");
		AoMexerNoPoder(pl);
	}

	// =====================================================================
	// 3. O TIQUE
	// =====================================================================

	/// <summary>
	/// O TIQUE DO PODER EM USO, a 5 Hz (a cadencia do `RelaxCheck`, que no DM anda com o `GlobalStats`): a raiva de
	/// quem relaxou, e o retrato pra tela -- que so sai quando muda.
	/// </summary>
	private void TickDoPoderEmUso()
	{
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
	/// O RETRATO DESTE CORPO, AGORA: quanto do proprio poder a barra deixa passar e os dois bits. E a unica funcao
	/// que decide o que a tela le -- a bancada a chama direto, e o tique a chama por <see cref="MandarPoderEmUso"/>.
	/// </summary>
	internal PoderEmUsoState PoderEmUsoDe(ServerPlayer pl)
	{
		BitsDoPoderEmUso bits = BitsDoPoderEmUso.Nenhum;
		if (PoderEmUso.Escondido(pl.Ficha)) bits |= BitsDoPoderEmUso.Escondido;
		if (PoderEmUso.Relaxado(pl.Ficha)) bits |= BitsDoPoderEmUso.Relaxado;
		// (2026-10-15) A RAIVA VAI NO MESMO RETRATO: o HUD a escreve embaixo da vida (dono: *"seria bom ter um aviso em baixo
		// da vida (...) q vc ta com raiva mostrando se ta com raiva ou furioso"*). E ele sai quando muda, a 5 Hz.
		switch (NivelDaRaivaDe(pl))
		{
			case Jandirus.Core.Forms.NivelDeRaiva.Extrema: bits |= BitsDoPoderEmUso.Furioso; break;
			case Jandirus.Core.Forms.NivelDeRaiva.Comum: bits |= BitsDoPoderEmUso.ComRaiva; break;
		}
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
		pl.Ficha.relaxado = false;
	}

	/// <summary>Este id saiu do mundo: o retrato que a tela dele recebeu some junto (id se reusa). Chamado pelo `EsquecerG9`.</summary>
	private void EsquecerOPoderEmUso(int id) => _poderEmUsoMandado.Remove(id);

	// =====================================================================
	// 6. QUEM SE SENTE, E CONTRA QUE PODER
	// =====================================================================

	/// <summary>
	/// **ESTE CORPO TEM ENERGIA QUE SE SINTA?** -- o piso dos 5 de BP do Sense, do scouter e da telepatia
	/// (`D.expressedBP <= 5`, `HtmlUI.dm:370` e `:411`; `Communication.dm:53`), medido SEM A BARRA
	/// (`PoderEmUso.SemABarra`). O dono (2026-10-12): *"vc mudar teu poder na barra de % n esconde o ki do scouter,
	/// ou sense, so serve pra se segurar ou usar todo o poder, esconder o ki totalmente so com relaxar"*.
	///
	/// Quem relaxa expressa a constante 5 e cai fora por aqui; quem se segura a 1% com 400 de poder expressa 4 e
	/// CONTINUA na lista, com a leitura baixa que a barra lhe da.
	/// </summary>
	internal static bool TemEnergiaQueSeSente(ServerPlayer o) => PoderEmUso.SemABarra(o.Ficha) > BpMinimoSentivel;

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
	/// que ela diga: mais forte ou mais fraco que voce. (Vale pra quem relaxa e pra quem some por invisibilidade,
	/// Sneak ou embate: todos travam o expresso em 5 -- `PoderEmUso.Escondido`.)
	/// </summary>
	internal static double PoderQueSeCompara(ServerPlayer eu) =>
		PoderEmUso.Escondido(eu.Ficha) && !SenseContraOPisoDeTeste ? eu.Ficha.BP : eu.Ficha.expressedBP;
}
