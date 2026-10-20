namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ O TORNEIO DO CELL (dono, 2026-10-19) ============================
/// *"na boss fight do cell se ele virar perfeito, ele avisa q em 5 dias tera um torneio do cell, ele faz um torneio na terra q so entra player
/// sem npc, e os playes enfrentam ele no x1, se ele cair da arena ele so vai falar q n importa essa regra e n para a luta assim so da pra vencer
/// ele no torneio se vc derrotar ele nesse x1, se ele vencer todos os jogadores ele explode a terra com um kamehameha. faça isso"*. Perguntado,
/// o dono decidiu o resto:
///   * nos dias ate o torneio o Cell SOME, e volta na arena da Terra (a do torneio de artes marciais) no dia marcado;
///   * so JOGADOR desafia, um por vez, e cada um tem UMA chance; quem perde e nocauteado -- ninguem morre no x1 (*"nocautear apenas"*);
///   * A ARENA: o desafiante que pisa fora dela perde, *"a nao ser q o cell saia da arena, ai ele fala q essa regra é inutil e tira ela, e a
///     luta continua"* -- dali em diante ninguem mais perde por sair;
///   * ele vence todos = a fila vazia por 5 min depois da ultima luta (cada um ja teve a sua chance) -> o Kamehameha na Terra;
///   * nocauteado no x1, *"o jogo vai falar q ele foi morto na luta e volta super perfeito e apartir dai a luta vira livre, todos vs cell"*.
///
/// ============================ OS CELL JR (dono, 2026-10-19, logo depois) ============================
/// *"se o cell cair da arena alem dele falar q vai ignorar essa regra, ele vai spawnar cell jr (...) ao todo 7, ao qual vao mirar nos
/// participantes q nao estao lutando no torneio, nesse momento os participantes vao poder lutar mas nao contra o cell, somente contra os cell
/// jr, enquanto o cell enfrenta no x1 o jogador q o empurrou pra fora da arena"* -- e *"os cell jr vao ter 90% do bp do cell perfeito"*. Os
/// sete nascem em volta dele, com a folha `Cell Jr.png` (um corpo reservado do bio-androide, <see cref="CorpoDoCellJr"/>), e cada um mira um
/// jogador de fora da luta perto da arena; o Cell continua intocavel pra eles, e o desafiante tambem. Acabado o torneio, os que sobraram ficam
/// soltos (a IA hostil escolhe quem esta perto); acabada a saga, vao embora com ela.
/// ================================================================================================================
///
/// ============================ O KAMEHAMEHA E UM RAIO DE VERDADE (dono, 2026-10-19, depois das fotos) ============================
/// *"o kamehameha dele q destrou o planeta poderia usar o proprio efeito de beam do kamehameha q ja tem no jogo, e aumentar ele pra 2x o
/// tamanho normal"* -- e *"se outro jogador jogar um beam contra esse kamehameha pode ter colisao de ki, e se o jogador vencer essa colisao o
/// cell morre na hora (mesma coisa q no anime com gohan)"*. Entao o Kamehameha do fim do torneio deixou de ser um desenho a parte: e o raio
/// da tecnica, pelo canal de sempre (a carga na mao, a cabeca que voa, o embate de ki), com <see cref="EscalaDoKamehameha"/> vezes o tamanho
/// do normal, na direcao do jogador mais perto. PERDEU a disputa: o Cell MORRE ali, e de vez -- sem a volta Super Perfeita, como no anime.
/// Chegou ao fim sem perder -- ninguem disputou, ou o JOGADOR perdeu a disputa (o dono, 2026-10-20: *"Se o jogador perder o clash obviamente a
/// terra tb explode"*) --: a Terra explode. EMPATOU: os dois estouram e ele carrega outro.
/// ================================================================================================================
///
/// ============================ O SUPER PERFEITO PERDENDO (dono, 2026-10-20) ============================
/// *"Ele no super perfeito tb usaria o kamehameha q destrói planeta se ele estiver perdendo (hp do torso ou cabeça abaixo de 25%)"*. Na luta
/// livre, o Super Perfeito de pe com o tronco ou a cabeca abaixo de <see cref="FracaoDoPerdendo"/> da vida (<see cref="Perdendo"/>) para de
/// lutar e carrega o MESMO Kamehameha do fim do torneio: o raio 2x, intocavel na carga, e o embate de ki -- o jogador vence, ele morre de vez;
/// o jogador perde (ou ninguem disputa), a Terra explode; empate, ele carrega outro. E logo depois: *"Caso ele seja nocauteado por sofrer um alto
/// dano de uma vez, ele começa a usar o kamehameha (...) basicamente deveria ser impossível ele nao jogar o kamehameha no final"* -- caido, ele
/// levanta e carrega; e o golpe que o mataria vira o Kamehameha. O fim dele e a disputa: perder pra ela o mata, e nada mais.
/// ================================================================================================================
///
/// AQUI FICAM OS NUMEROS, AS DUAS CONTAS DA ARENA E AS FALAS. Quem poe o Cell na arena, chama o desafiante e julga a luta e o
/// `GameServer.TorneioDoCell.cs`. Os numeros que o dono nao deu (a contagem, o respiro entre lutas, o prazo de quem some, a fuga) sao
/// ESCOLHA MINHA e estao listados pra ele no relatorio.
/// =================================================================================================================================
/// </summary>
public static class TorneioDoCell
{
	/// <summary>A fila vazia por este tanto (contado so com jogador online) e ele venceu todos -- o dono: *"Fila vazia por 5 min"*.</summary>
	public const double SegundosDeFilaVazia = 5 * 60;

	/// <summary>Quanto antes do fim da espera ele avisa que a Terra vai acabar.</summary>
	public const double SegundosDoUltimoAviso = 60;

	/// <summary>A contagem antes do "COMECEM!" -- a mesma do torneio de artes marciais.</summary>
	public const int SegundosDeContagem = 3;

	/// <summary>O respiro entre uma luta e a proxima: o perdedor sai, o Cell se refaz e volta pro meio.</summary>
	public const double SegundosEntreLutas = 8;

	/// <summary>O desafiante que some (deslogou, saiu da Terra, morreu por outro caminho) tem este prazo pra voltar -- depois e W.O.</summary>
	public const double SegundosDeAusencia = 20;

	/// <summary>
	/// A FUGA: voar pra longe nao e sair da arena (a regra e de quem PISA fora), mas tambem nao pode segurar o torneio pra sempre. Mais de
	/// <see cref="CelulasDeFuga"/> celulas alem da beirada por <see cref="SegundosDeFuga"/> segundos e desistir da luta.
	/// </summary>
	public const int CelulasDeFuga = 20;

	/// <summary>...por este tanto de segundos seguidos.</summary>
	public const double SegundosDeFuga = 30;

	/// <summary>Quantos Cell Jr o Cell solta quando cai da arena -- o dono: *"ao todo 7"*.</summary>
	public const int QuantosCellJr = 7;

	/// <summary>O BP de cada Cell Jr, em fracao do BP do Cell Perfeito de agora -- o dono: *"90% do bp do cell perfeito"*.</summary>
	public const double FracaoDoBpDoCellJr = 0.9;

	/// <summary>O molde do Cell Jr no `npcs.json`.</summary>
	public const string MoldeDoCellJr = "cell_jr";

	/// <summary>
	/// O CORPO DO CELL JR: um indice RESERVADO do corpo do bio-androide (o mesmo truque da saga do Majin, `SagaDoMajin.PrimeiroCorpo`), longe
	/// dos degraus (0 a 3) -- e por ele que a tela desenha a folha <see cref="FolhaDoCellJr"/> (`VisualCatalog.CorpoSprite`). A criacao saneia
	/// o indice pra dentro da lista, entao nenhum jogador nasce com ele.
	/// </summary>
	public const int CorpoDoCellJr = 201;

	/// <summary>A folha do Cell Jr -- a do pedido do dono.</summary>
	public const string FolhaDoCellJr = "res://Assets/Sprites/Character Icons/Bioandroids/Cell Jr.tres";

	/// <summary>Quem os Cell Jr miram: jogador de fora da luta a ate este tanto de tiles do meio da arena (a plateia e quem esta por perto).</summary>
	public const int TilesDaPlateia = 60;

	/// <summary>
	/// O KAMEHAMEHA QUE EXPLODE A TERRA: o raio da tecnica com este tanto de vezes o tamanho normal (a `EscalaVisual` do tiro, que o raio
	/// comum tira do `wavemult` dele, 1 no Kamehameha) -- o dono: *"aumentar ele pra 2x o tamanho normal"*.
	/// </summary>
	public const double EscalaDoKamehameha = 2;

	/// <summary>
	/// A CARGA dele na mao antes de o raio sair: o tempo de quem quer disputar se por na frente e carregar o seu. ESCOLHA MINHA. Cabe com folga
	/// no prazo do raio de NPC (`EmbateDeKi.SegundosDeFeixeDeNpc`, 12 s contados desde a abertura do canal) -- sobra pra ele voar e disputar.
	/// </summary>
	public const double SegundosDeCargaDoKamehameha = 6;

	/// <summary>
	/// (2026-10-20) O SUPER PERFEITO ESTA PERDENDO com o tronco ou a cabeca abaixo desta fracao da vida do membro -- o dono: *"hp do torso ou
	/// cabeça abaixo de 25%"*. Estritamente abaixo: 25% ainda nao e.
	/// </summary>
	public const double FracaoDoPerdendo = 0.25;

	/// <summary>A cabeca do corpo (`Body`, a parte `/obj/bodyparts/Head`): o tronco e o `Body.NomeDoTronco`.</summary>
	private const string NomeDaCabeca = "Cabeca";

	/// <summary>
	/// O SUPER PERFEITO PERDENDO: o TRONCO ou a CABECA abaixo de <see cref="FracaoDoPerdendo"/> da vida deles -- so esses dois (o dono nomeou os
	/// dois); braco e perna feridos nao contam.
	/// </summary>
	public static bool Perdendo(Jandirus.Core.Combat.Body corpo)
	{
		foreach (Jandirus.Core.Combat.BodyPart p in corpo.Partes)
			if ((p.Nome == Jandirus.Core.Combat.Body.NomeDoTronco || p.Nome == NomeDaCabeca) && p.Vida < p.VidaMax * FracaoDoPerdendo) return true;
		return false;
	}

	/// <summary>O RING-OUT do desafiante: pisou fora (sem voar) com a regra de pe = perdeu.</summary>
	public static bool PerdePorSair(bool regraDePe, bool voando, bool dentro) => regraDePe && !voando && !dentro;

	/// <summary>
	/// ...e o do Cell nao o derruba: derruba a REGRA (*"ele fala q essa regra é inutil e tira ela, e a luta continua"*). A mesma pergunta, e
	/// e de proposito uma funcao a parte: e a resposta, e nao a pergunta, que muda.
	/// </summary>
	public static bool CellDerrubaARegra(bool regraDePe, bool voando, bool dentro) => regraDePe && !voando && !dentro;

	// =====================================================================
	// AS FALAS -- o que o mundo ouve e o que o Cell diz no balao
	// =====================================================================

	/// <summary>O anuncio pro mundo inteiro, quando ele vira Perfeito e sai.</summary>
	public static string Anuncio(string cell, int dias) =>
		$"{cell.ToUpperInvariant()} ALCANÇOU A FORMA PERFEITA -- e parou a luta: daqui a {dias} dia(s) começa o TORNEIO DO {cell.ToUpperInvariant()} " +
		"na arena da Terra. Só jogadores, um de cada vez, contra ele. Se ninguém o vencer, a Terra acaba.";

	/// <summary>O que ele diz antes de sumir.</summary>
	public static string FalaDoAnuncio(int dias) =>
		$"Eu sou perfeito. Esmagar vocês assim seria um desperdício... Daqui a {dias} dia(s), o meu torneio. Venham um de cada vez.";

	/// <summary>O lembrete de cada virada de dia.</summary>
	public static string Lembrete(string cell, int dias) => $"Faltam {dias} dia(s) para o TORNEIO DO {cell.ToUpperInvariant()} na arena da Terra.";

	/// <summary>A abertura, no dia.</summary>
	public static string Abertura(string cell) =>
		$"O TORNEIO DO {cell.ToUpperInvariant()} COMEÇOU! Ele espera na arena da Terra: pise nela pra desafiá-lo, um de cada vez. " +
		"Quem sai da arena perde, quem é nocauteado perde -- e cada um tem uma chance só.";

	/// <summary>A volta depois de um reinicio do servidor no meio do torneio.</summary>
	public static string Retomada(string cell) => $"{cell} continua esperando na arena da Terra -- o torneio dele não acabou.";

	/// <summary>Quem vem agora.</summary>
	public static string Desafio(string cell, string desafiante) => $"TORNEIO DO {cell.ToUpperInvariant()}: {desafiante} sobe na arena!";

	/// <summary>O Cell venceu a luta.</summary>
	public static string CellVence(string cell, string desafiante, string como) =>
		$"{cell.ToUpperInvariant()} vence {desafiante} ({como}). Quem é o próximo? Pise na arena.";

	/// <summary>O que ele diz quando cai fora da arena -- e a regra cai junto.</summary>
	public const string FalaDaRegra = "Fora da arena? Essa regra é inútil. A partir de agora ela não vale mais -- a luta continua!";

	/// <summary>...e o mundo.</summary>
	public static string RegraCaiu(string cell) =>
		$"{cell} saiu da arena e declarou a regra INÚTIL: daqui em diante ninguém perde por sair da arena.";

	/// <summary>O que ele diz soltando os Cell Jr.</summary>
	public const string FalaDosCellJr = "E ja que as regras acabaram... deixem-me apresentar meus filhos. Brinquem com eles enquanto eu termino esta luta.";

	/// <summary>...e o mundo.</summary>
	public static string AnuncioDosCellJr(string cell, int quantos) =>
		$"{cell.ToUpperInvariant()} soltou {quantos} CELL JR! Eles caçam quem está fora da luta -- e o Cell continua o x1.";

	/// <summary>Nocauteado no x1: o jogo diz que ele morreu na luta.</summary>
	public static string MortoNaLuta(string cell, string desafiante) =>
		$"{desafiante.ToUpperInvariant()} DERROTOU {cell.ToUpperInvariant()}! {cell} foi MORTO na luta!";

	/// <summary>A volta Super Perfeita, na arena -- e o fim do torneio.</summary>
	public static string VoltaSuperPerfeito(string cell) =>
		$"{cell.ToUpperInvariant()} VOLTOU -- SUPER PERFEITO! O torneio acabou: agora é a luta livre, TODOS contra {cell}!";

	/// <summary>O ultimo minuto da espera.</summary>
	public static string FalaDoUltimoMinuto => "Ninguém mais? Mais um minuto... e a Terra acaba.";

	/// <summary>O Kamehameha: o mundo ouve, e o Cell diz.</summary>
	public static string AnuncioDoKamehameha(string cell) =>
		$"Ninguém mais desafiou {cell}. Ele junta as mãos... um KAMEHAMEHA vai cair sobre a Terra, e nada o atinge enquanto ele carrega! "
		+ "Só um raio de ki à altura pode pará-lo.";

	public const string FalaDoKamehameha = "Então ninguém mais... KA... ME... HA... ME...";

	/// <summary>(2026-10-20) O Kamehameha do Super Perfeito perdendo, na luta livre: o mundo ouve, e o Cell diz.</summary>
	public static string AnuncioDoKamehamehaDoSuperPerfeito(string cell) =>
		$"{cell.ToUpperInvariant()} está perdendo... e junta as mãos: um KAMEHAMEHA vai cair sobre a Terra, e nada o atinge enquanto ele carrega! "
		+ "Só um raio de ki à altura pode pará-lo.";

	public const string FalaDoKamehamehaDoSuperPerfeito = "Se eu cair, a Terra cai comigo! KA... ME... HA... ME...";

	/// <summary>(2026-10-20) ...e o do Super Perfeito CAIDO -- nocauteado, ou com o golpe que o mataria: ele se levanta por um fio.</summary>
	public static string AnuncioDoKamehamehaDoSuperPerfeitoCaido(string cell) =>
		$"{cell.ToUpperInvariant()} cai... e se levanta por um fio! Ele junta as mãos: um KAMEHAMEHA vai cair sobre a Terra, e nada o atinge enquanto "
		+ "ele carrega! Só um raio de ki à altura pode pará-lo.";

	/// <summary>(2026-10-19) O Kamehameha dele PERDEU a disputa de ki: o mundo ouve que ele morreu ali, e que a Terra esta salva.</summary>
	public static string CellDesintegrado(string cell, string heroi) =>
		$"O ataque de {heroi.ToUpperInvariant()} ENGOLIU o Kamehameha de {cell}! {cell.ToUpperInvariant()} foi DESINTEGRADO -- a Terra está salva!";

	/// <summary>...e EMPATOU: os dois estouraram no ar, e ele carrega outro.</summary>
	public static string KamehamehaEmpatou(string cell) => $"Os dois ataques explodem no ar! {cell} junta as mãos de novo...";

	// AS RECUSAS -- quem pisa na arena sem poder
	public const string JaLutou = "você já teve a sua chance contra o Cell: só uma por jogador.";
	public const string SeMeteu = "você se meteu na luta de outro: o Cell não aceita mais o seu desafio.";
	public static string ArenaOcupada(string desafiante) => $"a arena é de {desafiante} e do Cell agora: espere a sua vez fora dela.";
	public static string NaoPega(string desafiante) => $"o Cell nem sente: a luta é de {desafiante}. Você está fora do torneio dele.";
}
