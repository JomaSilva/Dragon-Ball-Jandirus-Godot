namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ QUEM E "GENTE" PRA EFEITO DE REGRA DE MUNDO ============================
/// UMA pergunta, UM lugar. Todo laco que varre corpos e TOMA UMA DECISAO -- acordar uma saga, escolher
/// um alvo, colher DNA, subir nivel de skill, tirar a media do servidor -- pergunta aqui.
///
/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
/// No DM a pergunta nem se faz: as varreduras de regra de mundo correm sobre `player_list`, que **so
/// tem jogador**, e ainda assim o autor poe um `!M.client` por cima (`BossEvents.dm:373-374`). Sao
/// DUAS defesas. Este port fundiu jogador e NPC numa colecao so (`_players`), entao a segunda defesa
/// virou a UNICA -- e ela e um marcador so, escrito a mao em cada chamador.
///
/// Isso ficou invisivel por meses porque quase todo corpo do mundo era um jogador: "varre todo mundo"
/// e "varre os jogadores" davam o mesmo numero. Deixou de dar quando o port ganhou corpos SEM DONO de
/// varias origens ao mesmo tempo -- o povoamento por planeta, os proprios chefes de saga (os corpos
/// mais fortes do mundo), o clone da mente, a fera possuida e o boneco do corpo largado.
/// ==================================================================================
///
/// ============================ OS TRES MARCADORES, E POR QUE SAO TRES ============================
/// Nenhum deles sozinho responde. A regra e a CONJUNCAO, e cada metade cobre o que a outra erra:
///
///   * `temDonoNaTela` (o `Peer` do servidor) -- "ha uma pessoa recebendo o que este corpo ve".
///     **Sozinho ele conta o boneco do corpo largado no dia em que o boneco entrar na lista** (ele
///     carrega a Ficha do dono de proposito, pro Sense enxergar quem esta em transe);
///   * `papel` (<see cref="PapelDeNpc"/>) -- "este corpo saiu de um MOLDE". **Sozinho ele conta o
///     clone da mente e o boneco**, que nao tem papel nenhum;
///   * `donoDoCorpoLargado` -- "eu sou o corpo que alguem deixou pra tras". Terceira perna porque o
///     boneco e o unico caso que passa pelas duas primeiras.
///
/// **O CRITERIO NAO PODE SER "TEM CEREBRO"** -- e a armadilha mais facil deste port. Um jogador em
/// Oozaru sem controle ou em furia lendaria TEM cerebro (`SemAsRedeas`) e continua sendo jogador: o
/// corpo esta possuido, a pessoa nao deixou de existir. Ver <see cref="EhJogador"/>.
/// ==========================================================================================
/// </summary>
public static class Gente
{
	/// <summary>
	/// ============================ ISTO E UM JOGADOR? ============================
	/// A resposta unica. Os casos dificeis, decididos aqui e nao em cada chamador:
	///
	///   * **jogador em Oozaru selvagem / furia lendaria** -- E JOGADOR. O corpo esta possuido pela
	///     IA, mas o dono esta na tela e o corpo e dele. (`Cerebro != null` nao entra nesta conta.)
	///   * **jogador dentro da mente, ou na ponte da nave** -- E JOGADOR. Trocar de zona nao muda
	///     quem voce e.
	///   * **NPC (cidadao, Rei, chefe de saga)** -- NAO. Tem `papel`, e e o corpo mais forte do
	///     mundo justamente quando mais custa contar errado: um chefe contado dispara o marco
	///     SEGUINTE, que nasce um chefe maior, que dispara o proximo -- a cadeia inteira num
	///     servidor vazio.
	///   * **clone/reflexo da mente** -- NAO. Ele nasce com o `expressedBP` do dono, entao contar
	///     este corpo e contar o dono duas vezes, uma delas com o numero errado.
	///   * **boneco do corpo largado** -- NAO. Ele COMPARTILHA a Ficha do dono: contar os dois e
	///     contar a mesma pessoa duas vezes.
	///   * **corpo forjado de bancada** -- NAO, enquanto nao tiver dono na tela. Ele tem conta e
	///     assinatura (por isso o `EhPessoa` do convivio diria que sim), e nao tem ninguem olhando.
	///   * **CADAVER** (o corpo que fica no chao ate alguem enterrar, `Core/World/Cadaver.cs`) -- NAO, e
	///     ele **nao precisou de um quarto marcador**: nao tem dono na tela e nao saiu de molde nenhum,
	///     entao as duas primeiras pernas ja o cortam e ele cai no TERCEIRO grupo, junto do clone e do
	///     boneco. E o lugar certo: um cadaver nao conta pra lotacao de planeta, nao entra na reposicao
	///     de habitante e nao renasce. O `ServerPlayer.ECadaver` existe por OUTRA pergunta -- *"de onde
	///     voce veio"*, que nunca e derivavel do estado (ver o campo).
	///
	/// A FERA E O CLONE **NAO SAO EXCECOES A ESCREVER**: nenhum dos dois tem dono na tela, entao a
	/// primeira metade da conjuncao ja os corta. O que a segunda e a terceira cobrem e o dia em que
	/// alguem der um `Peer` a um corpo desses -- que e exatamente o que a bancada faz pra medir.
	/// ========================================================================
	/// </summary>
	/// <param name="temDonoNaTela">`ServerPlayer.Peer != null`.</param>
	/// <param name="papel">`ServerPlayer.Papel` -- de que molde este corpo saiu (nulo em jogador).</param>
	/// <param name="donoDoCorpoLargado">`ServerPlayer.DonoDoCorpoLargado` (0 = nao sou boneco de ninguem).</param>
	public static bool EhJogador(bool temDonoNaTela, PapelDeNpc? papel, int donoDoCorpoLargado)
		=> temDonoNaTela && papel == null && donoDoCorpoLargado == 0;

	/// <summary>
	/// ============================ ISTO E UM CORPO DO MUNDO (NPC)? ============================
	/// A outra ponta, e ela **nao e a negacao** de <see cref="EhJogador"/>: ha um terceiro grupo. O
	/// clone da mente e o boneco do corpo largado nao sao jogador NEM NPC do mundo -- eles nao contam
	/// pra lotacao de planeta, nao entram na reposicao de habitante e nao saem do mundo quando morrem.
	///
	/// Ela existe porque a mesma conjuncao (`!temDono && papel != null`) estava escrita a mao em
	/// quatro lugares -- os tres contadores do povoamento e a remocao do NPC morto --, e "a linha nova
	/// em tres dos quatro lugares" e o defeito que mais se repetiu neste port.
	/// ====================================================================================
	/// </summary>
	public static bool EhNpcDoMundo(bool temDonoNaTela, PapelDeNpc? papel)
		=> !temDonoNaTela && papel != null;
}
