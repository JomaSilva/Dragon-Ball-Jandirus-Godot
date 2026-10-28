namespace Jandirus.Core.World;

/// <summary>
/// "AQUI SE CONSTROI?" -- a resposta por TIPO de zona, num lugar so, com a frase pro jogador.
///
/// O dono (2026-10-11), sobre a mente, o bolso da fusao e o interior do Majin: *"Pode recusar esses tb, n deveria
/// ser possivel contruir nada dentro desses interiores/ locais"*. E sobre a Sala do Tempo: *"construcoes dentro da
/// sala do tempo devem continuar la, so q salva apenas na dimensao daquela sala do tempo ... como se fosse um planeta
/// q salva as construcoes e bases"*.
///
/// ============================ A REGRA: TODO INTERIOR RECUSA, MENOS A SALA DO TEMPO E A NAVE ============================
///   * A MENTE (a propria e a alheia), o BOLSO DA FUSAO, o SELO e o INTERIOR DE UM MAJIN morrem com quem os sustenta -- e a
///     chave de quase todos e o id de SESSAO de alguem (`Interior("Interdimension", idDoJogador)`): o jogador #5 de hoje nao
///     e o #5 de amanha, e a maquina gravada na "mente #5" apareceria na mente de outra pessoa depois de um reinicio. La nao
///     se constroi NADA.
///   * A SALA DO TEMPO de um mundo (`SalaDePlaneta.Interior`) e um LUGAR: a chave dela e a seed do mundo, que nao
///     muda. O que se monta la fica la -- gravado no `mundo.json` com a chave INTEIRA (tipo, nome e seed), e por isso
///     so naquela Sala. A da Terra e zona de arquivo, como qualquer planeta.
///   * A NAVE GRANDE (dono, 2026-10-18: *"permita construir dentro da capital ship (gravidade , research etc mas nao paredes
///     etc) e se ela explodir esses itens saem da nave sem estarem parafusados"*). A chave dela e o id da NAVE, que o
///     `naves.json` guarda -- a obra volta do disco na mesma nave. Ela recusava pelo FIM do lugar, que nao tinha porta; agora
///     tem: quando a nave acaba (explode ou e recolhida), o que esta dentro sai pro lado do casco, SOLTO
///     (`GameServer.TirarAsObrasDaNave`). As passagens dela (a plataforma, a chegada, a porta da ponte) recusam pelo
///     `Assentamento`, e PAREDE nao sobe (o bloco de base, abaixo).
///   * INTERIOR NOVO RECUSA POR PADRAO (cai na ultima frase). Quem quiser que se construa num interior novo o poe
///     aqui de proposito, e responde antes onde a obra dele fica gravada e o que acontece quando o lugar acaba.
///
/// QUEM PERGUNTA: o `Posicionar` (maquina, semente, nave -- tudo o que sai da mochila pro chao), a cova
/// (`ErguerALapide`) e o ovo (`GameServer.Filhos`). O BLOCO de base tem a regra dele, mais dura (`RegrasDeBloco.ZonaAceita`:
/// interior nenhum -- nem a Sala do Tempo, nem a nave: e o "nao paredes" do dono). O que ja estiver gravado num lugar que
/// recusa fica de fora do mundo ao carregar, sem sair do arquivo -- ver `GameServer.CarregarMundoDeDisco`.
/// ==============================================================================================================
/// </summary>
public static class LugarDeObra
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --interioresteste): os interiores de SESSAO aceitam obra -- o jogo de antes de 2026-10-11, em que
	/// dava pra assentar maquina e abrir cova na mente, no bolso da fusao, no selo e dentro de um Majin. Falso em jogo, sempre.
	/// </summary>
	public static bool InterioresDeSessaoAceitamDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --naveteste, camada 6): a nave grande volta a recusar obra -- o jogo de antes de 2026-10-18, com a
	/// frase daquela recusa. Falso em jogo, sempre.
	/// </summary>
	public static bool NaveRecusaObraDeTeste;

	/// <summary>Por que NAO se constroi nesta zona -- a frase pro jogador --, ou nulo: aqui se constroi.</summary>
	public static string? PorQueNaoSeConstroi(ZoneKey z)
	{
		if (z.Kind != ZoneKey.KindInterior || SalaDoTempo.EhUmaSala(z)) return null;

		// A NAVE ACEITA: o fim dela tem porta (ver o cabecalho).
		if (Jandirus.Core.Tech.NaveGrande.EhInterior(z, out _))
			return NaveRecusaObraDeTeste
				? "não dá pra construir dentro de uma nave: se ela for destruída, o que estiver aqui dentro fica preso num lugar onde ninguém mais entra."
				: null;
		if (InterioresDeSessaoAceitamDeTeste) return null;

		if (DimensaoMental.EhAMente(z))
			return "não dá pra construir dentro de uma mente: ela se desfaz quando quem medita acorda, "
				   + "e o que ficasse de pé aqui se perderia com ela.";
		if (InteriorDoMajin.EhOInterior(z))
			return "não dá pra construir dentro de um Majin: este lugar só existe enquanto ele segura "
				   + "quem absorveu, e o que ficasse de pé aqui se perderia com ele.";
		return "não dá pra construir aqui: este lugar só existe enquanto você está preso nele, "
			   + "e o que ficasse de pé se perderia com ele.";
	}

	/// <summary>Aqui se constroi?</summary>
	public static bool Aceita(ZoneKey z) => PorQueNaoSeConstroi(z) == null;

	/// <summary>
	/// ============================ NA SALA DO TEMPO SO ENTRA O QUE NAO AUMENTA O GANHO (dono, 2026-10-27, o pedido 136) ============================
	/// *"faça com q n de pra colocar gravidade dentro da sala do tempo, é mt broken , no maximo regenerador"* -- e, perguntado o que mais
	/// fica de fora: *"saco de pancada tb nao kkkk, so coisas q nao aumenta os ganhos, entao regenerador, simulador , macieira etc"*. O tempo
	/// da Sala ja rende anos de treino num dia; a maquina de gravidade (o campo), o saco de pancada e a maquina de socos (o treino dobrado ao
	/// lado, e o soco do saco do DU) multiplicavam por cima. Essas tres nao se assentam numa Sala -- a da Terra e a de qualquer mundo; o resto
	/// (regenerador, simulador, macieira, pesquisa, bau...) continua entrando.
	/// AS QUE JA ESTAVAM LA (postas antes desta regra) ficam onde estao, mas NAO RENDEM: a gravidade nao liga, e o saco nao dobra o treino
	/// nem paga o soco (o dono: "Param de ligar").
	/// QUEM PERGUNTA: o `Posicionar` (assentar), o `DefinirGravidade` (ligar), o `TreinarNoSaco`, o `BonusDeTreinoPerto` e o `SocarOSaco`.
	/// ====================================================================================================================================
	/// </summary>
	public static readonly string[] AumentamOGanho = ["Gravity", "Punching_Bag", "Punching_Machine"];

	/// <summary>
	/// DEFEITO INJETADO (bancada --interioresteste, familia 1c): a Sala volta a aceitar e a fazer render o que aumenta o ganho -- o jogo
	/// de antes do pedido 136. Falso em jogo, sempre.
	/// </summary>
	public static bool SalaRendeOQueAumentaOGanhoDeTeste;

	/// <summary>Esta obra aumenta o ganho do treino (a gravidade, o saco, a maquina de socos)?</summary>
	public static bool AumentaOGanho(string tipo) =>
		Array.Exists(AumentamOGanho, t => string.Equals(t, tipo, StringComparison.OrdinalIgnoreCase));

	/// <summary>Esta obra fica sem render nesta zona -- uma Sala do Tempo, e uma obra que aumenta o ganho?</summary>
	public static bool NaoRendeAqui(ZoneKey z, string tipo) =>
		!SalaRendeOQueAumentaOGanhoDeTeste && SalaDoTempo.EhUmaSala(z) && AumentaOGanho(tipo);

	/// <summary>A frase de quem tenta ASSENTAR uma delas numa Sala.</summary>
	public const string NaoEntraNaSala =
		"isso não entra na Sala do Tempo: lá dentro só vale o que não aumenta o ganho do treino (regenerador, simulador, macieira...).";

	/// <summary>A frase de quem tenta LIGAR (ou treinar com) uma delas que ja estava numa Sala.</summary>
	public const string NaoRendeNaSala =
		"isso não funciona na Sala do Tempo: lá dentro só vale o que não aumenta o ganho do treino.";
}
