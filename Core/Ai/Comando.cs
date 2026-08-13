using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// O QUE A IA ESTA APERTANDO NESTE INSTANTE -- e nada alem disso.
///
/// ============================ ELE E O TECLADO, E NAO A ORDEM ============================
/// A regra que este tipo existe pra tornar MECANICA: *a IA e um jogador sem teclado*. Um NPC que
/// soca sem pagar estamina, que voa sem pagar Ki ou que transforma sem passar pelo `Avaliar` e
/// indistinguivel de um jogador por uns dez segundos e obviamente falso pra sempre depois disso.
///
/// Entao o cerebro nao chama funcao de jogo nenhuma: ele preenche ISTO, e quem executa
/// (<c>GameServer.AplicarComando</c>) so sabe traduzir cada campo pra a MESMA funcao que o pacote
/// do jogador aciona. O atuador **nao confere pre-condicao**; ele chama e deixa o jogo recusar.
/// Se um dia alguem quiser dar um atalho pra a IA, vai ter que escrever um campo novo aqui e uma
/// chamada nova la -- e as duas coisas aparecem numa varredura de arquivo.
/// ====================================================================================
///
/// ============================ ESTADO x PULSO, E POR QUE ISSO NAO E ENFEITE ============================
/// Um teclado de verdade tem dois tipos de tecla, e confundi-los produziu dois defeitos reais
/// neste servidor:
///
///   * ESTADO (tecla SEGURADA -- rumo, shift, espaco/control, guarda, C). O atuador compara com o
///     que o corpo JA esta fazendo e so chama na TRANSICAO. A guarda era chamada a 30 Hz
///     (`GameServer.Clone.cs`, antes desta camada) e so era idempotente por sorte: o `if` de
///     `CombatState.Guardar` (`CombatState.cs:185`) so dispara na SUBIDA, entao ninguem via o
///     `ContraPronto` sendo rearmado 30 vezes por segundo. Bastava alguem mexer naquele `if`.
///   * PULSO (tecla TOCADA -- soco, alternar voo, transformar). Vale por UM tique e some.
///
/// Sem a distincao, "carregar" nao teria como ser DESLIGADO: o comando diria "carrega" e nunca
/// diria "para", e a chamada interna `Carregar(pl,false)` -- que o comentario do despacho
/// (`GameServer.cs`) ja descrevia como existente -- nunca seria escrita. Era o caso: o corpo
/// possuido ficava carregando pra sempre.
/// ==================================================================================================
/// </summary>
public readonly struct Comando
{
	// =====================================================================
	// ESTADO -- teclas SEGURADAS, escritas em todo tique
	// =====================================================================

	/// <summary>Pra onde andar. Zero = parado. E o vetor de direcao do WASD.</summary>
	public Vec2 Rumo { get; init; }

	/// <summary>SHIFT. No chao e correr (e o `PodeCorrer` COBRA); no ar e o Superflight.</summary>
	public bool Correndo { get; init; }

	/// <summary>ESPACO e CONTROL segurados. Quem os le e o `TickDoVoo`, e so pra quem esta voando.</summary>
	public bool QuerSubir { get; init; }
	public bool QuerDescer { get; init; }

	/// <summary>ALT: a guarda erguida.</summary>
	public bool Guardar { get; init; }

	/// <summary>C segurado: reunindo energia.</summary>
	public bool Carregar { get; init; }

	// =====================================================================
	// PULSO -- vale UM tique
	// =====================================================================

	/// <summary>ESPACO: golpe leve.</summary>
	public bool Leve { get; init; }

	/// <summary>SHIFT+ESPACO: golpe pesado (investida longa).</summary>
	public bool Pesado { get; init; }

	/// <summary>A tecla de voo: LIGA se estiver no chao, DESLIGA se estiver no ar. E um toggle so.</summary>
	public bool AlternarVoo { get; init; }

	/// <summary>C tocado duas vezes: SUBIR um degrau da escada de formas.</summary>
	public bool SubirForma { get; init; }

	/// <summary>Voltar um degrau. Existe pro dia em que a IA precisar economizar Ki desligando a forma.</summary>
	public bool DescerForma { get; init; }

	/// <summary>
	/// USAR UMA TECNICA: o id do verb, exatamente como o `C2S.Habilidade` do jogador o manda.
	/// Nulo = nenhuma. **Hoje nunca sai nulo-diferente em producao** -- ver <see cref="Arsenal"/>.
	///
	/// ============================ ELE FOI RECUSADO UMA VEZ, E POR UM MOTIVO QUE MUDOU ============================
	/// Na camada passada este campo foi deixado de fora com uma frase: *"a IA ainda nao usa tecnica,
	/// e o campo pra ela nasceria orfao"*. Estava certo entao e nao esta mais: agora existe uma
	/// receita (`Plano.Atirar`) que o preenche, um arsenal que responde quando, e uma bancada que o
	/// exercita ponta a ponta com uma tecnica sintetica. Orfao e campo sem consumidor -- e nao campo
	/// cujo consumidor ainda nao tem o que consumir.
	///
	/// O canal e o MESMO do jogador, e isso importa mais do que parece: e por `UsarHabilidade` que
	/// passam a recarga, o "voce nao sabe isso", o custo em Ki e o grito da tecnica no chat. Uma IA
	/// que chamasse o efeito direto (`Kamehameha(npc)`) pularia os quatro de uma vez.
	/// ======================================================================================================
	/// </summary>
	public string? Habilidade { get; init; }

	// =====================================================================
	// DADO -- nem tecla nem pulso: um numero que acompanha o gesto
	// =====================================================================

	/// <summary>
	/// MARCAR ALGUEM (o id). Zero = **nao mexe na mira**.
	///
	/// ============================ E A MIRA, E ELA JA EXISTIA NO JOGO ============================
	/// Um ataque de longe precisa saber pra onde vai. O jogo ja tem essa resposta e ela nao e um
	/// vetor: e o ALVO MARCADO -- o `C2S.Alvo` que o jogador manda clicando em alguem, lido pelo
	/// `GameServer.Marcado`, que ja confere zona, morte, intocavel e ATE a regra de altura. Inventar
	/// um `Mira` em pixels aqui seria criar a segunda mira do jogo, e a segunda e a que diverge.
	///
	/// ZERO SIGNIFICA "NAO MEXE" e nao "limpa", ao contrario do pacote -- e a divergencia e
	/// deliberada: `default(Comando)` tem zero em tudo, e um zero que LIMPASSE faria todo tique de
	/// IA sem mira apagar a marcacao. O funil continua sendo o mesmo (`Mirar`), e ele continua
	/// sabendo limpar; a IA e que nunca pede isso -- a mira ja se limpa sozinha quando o marcado
	/// morre ou muda de zona, que e comportamento de producao.
	/// ========================================================================================
	/// </summary>
	public int Marcar { get; init; }

	/// <summary>O comando de quem nao vai fazer nada neste tique. Nao e "parar": e "nenhuma tecla".</summary>
	public static readonly Comando Nenhum = new();
}
