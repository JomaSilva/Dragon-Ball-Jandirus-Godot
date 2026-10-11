namespace Jandirus.Core.Economia;

/// <summary>
/// A LENHA -- derrubar arvore a soco rende um pouco de recurso, e a arvore rebrota.
///
/// Pedido do dono de 2026-10-10: *"derrubar arvores no soco (durante combate n conta) vc ganha uma quantidade
/// pequena de resources pra deixar o jogo mais minecraft derrubando arvore pra construir coisas. essas arvores dps
/// de um tempo voltam ou spawnam em outro local."*
///
/// ============================ O QUE O ORIGINAL TEM DE PARECIDO ============================
/// O DM tem a coleta a soco, so que noutra moeda: o `obj/Raw_Material/Lumber_Tree` ("Oak Tree",
/// `Crafting/Woodcutting.dm:3-11`) apanha no `MeleeAttack` (`attack_proc.dm:26-47`), perde `durability` (10, de 2 a
/// 4 por soco conforme a maestria de Woodcutting -- `Crafting Master.dm:343`) e, zerada, solta de 1 a 4 toras
/// (`matnum = rand(1,4)`, `:354`). Quem repoe e o `obj/matspawners` (`:377-397`): outro no nasce num ponto sorteado
/// a ate 15 tiles, pulando chao que tem dono (`newloc.proprietor`).
///
/// Daqui saem as tres formas deste arquivo -- uns poucos socos, um punhado sorteado, e a volta num ponto perto se
/// o lugar foi tomado. O que NAO veio: a maestria de Woodcutting (as maestrias de oficio nao foram portadas) e a
/// tora como item; aqui a arvore paga em recurso, que e o que o dono pediu.
/// =======================================================================================
///
/// ============================ POR QUE A ARVORE DEIXOU DE SER PAREDE PRO SOCO ============================
/// Nos mapas a arvore virou TILE (o `/obj/Trees` do `.dmm` e uma celula da camada de objetos, com o bit dela no
/// `.col`), e no planeta sorteado ela nasce tile. Tile cai pela regra do turf: `expressedBP >= 20` e um `prob(34)`
/// por soco (`attack_proc.dm:104`, `GameServer.SocarCenario`). Personagem novo expressa de 2 a 21: quase todo
/// novato socava a arvore pra sempre sem derrubar -- e e pra ele, no planeta sorteado, que este pedido existe.
/// No DM a arvore e OBJ de armadura 1 (`barrier.dm:8-9`): cai no primeiro soco de qualquer um. Aqui ficou no meio:
/// todo mundo derruba, e quem ja derrubava parede derruba de primeira.
/// =========================================================================================================
///
/// So as CONTAS moram aqui. Quem sabe onde ha arvore e <see cref="Jandirus.Core.World.ArvoresDoMapa"/>; quem
/// derruba, paga e replanta e o servidor (`GameServer.Lenha.cs`).
/// </summary>
public static class Lenha
{
	// =====================================================================
	// O TRONCO
	// =====================================================================

	/// <summary>
	/// O `expressedBP` que derruba de UM soco: o mesmo 20 da resistencia de todo cenario
	/// (<see cref="Jandirus.Core.Combat.Empurrao.ResistenciaPadrao"/>, `buildable.dm:353-360`). Quem ja abria parede
	/// no punho nao precisa de mais de um golpe numa arvore.
	/// </summary>
	public const double ForcaQueDerrubaDeUmaVez = 20;

	/// <summary>
	/// QUANTOS SOCOS O MAIS FRACO DOS CORPOS PRECISA. Cinco: e a conta do no de coleta do DM pra quem tem a maestria
	/// no nivel dele (`durability = 10`, 2 por soco -- `attack_proc.dm:37`).
	/// </summary>
	public const int SocosDoMaisFraco = 5;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a arvore volta a ser parede pro soco -- o `expressedBP >= 20` com
	/// `prob(34)` de antes de 2026-10-10, sem pagamento. Lido pela primeira linha do `GameServer.SocarArvore`.
	/// </summary>
	public static bool TroncoComoParedeDeTeste;

	/// <summary>
	/// QUANTO DO TRONCO UM SOCO LEVA, de 0 a 1. Proporcional a forca de quem bate, com piso (ninguem precisa de mais
	/// de <see cref="SocosDoMaisFraco"/>) e teto (um soco nao derruba mais que uma arvore).
	/// </summary>
	public static double ParteDoTroncoPorSoco(double expressedBP) =>
		Math.Clamp(expressedBP / ForcaQueDerrubaDeUmaVez, 1.0 / SocosDoMaisFraco, 1.0);

	/// <summary>Quantos socos desta forca derrubam uma arvore inteira. So pra explicar e pra bancada.</summary>
	public static int SocosPraDerrubar(double expressedBP) =>
		(int)Math.Ceiling(1.0 / ParteDoTroncoPorSoco(expressedBP) - 1e-9);

	/// <summary>O tronco cedeu? A folga e do ponto flutuante: cinco socos de um quinto somam 0,9999999.</summary>
	public static bool Cedeu(double troncoLevado) => troncoLevado >= 1.0 - 1e-9;

	// =====================================================================
	// O PAGAMENTO
	// =====================================================================

	/// <summary>
	/// A FAIXA QUE UMA ARVORE RENDE, em recursos -- *"uma quantidade pequena"*. Sorteada por queda, como as toras do
	/// DM (`matnum = rand(1,4)`). NAO sai da reserva do planeta (a arvore rebrota; e uma fonte a parte -- esta no
	/// contrato da economia, `Recursos.cs`).
	/// </summary>
	public const int RecursosMinimos = 5, RecursosMaximos = 12;

	/// <summary>O que ESTA queda rende, com este sorteio (0 a 1). O sorteio e argumento: quem sorteia e o servidor.</summary>
	public static int RecursosDaQueda(double sorteio)
	{
		int faixa = RecursosMaximos - RecursosMinimos + 1;
		return RecursosMinimos + Math.Clamp((int)(sorteio * faixa), 0, faixa - 1);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): a arvore que cai no meio de uma briga paga como qualquer outra.
	/// Lido pela propria regra, em <see cref="Paga"/>.
	/// </summary>
	public static bool PagaEmCombateDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): paga a quem derrubou, seja quem for -- NPC, clone, corpo possuido.
	/// Lido pela propria regra, em <see cref="Paga"/>.
	/// </summary>
	public static bool PagaQualquerUmDeTeste;

	/// <summary>
	/// QUEM ESTA SENDO ESMAGADO CONTA COMO EM COMBATE: a arvore cai, e nao paga. Esmagado e o corpo num chao mais
	/// pesado que ele domina, ou com peso de treino acima do limite (`Esmagamento.Esmaga`).
	///
	/// O dono (2026-10-10), perguntado se a gravidade acima do limite conta como combate: *"sim conta como combate,
	/// n da pra mineirar se vc nem consegue se mover"*. Ele falava de minerar e do curativo; a arvore foi alinhada
	/// ao mesmo principio. Quem se aclimata (a maestria alcanca a gravidade) ou tira o peso volta a receber NA HORA:
	/// o que barra e estar sendo esmagado agora, e nao a tag de combate que o esmagamento deixou acesa.
	///
	/// PRA TIRAR A ARVORE DESSA REGRA, troque pra `false` -- e o esmagado passa a receber (so a briga com alguem
	/// barra). E `static` e nao `const` porque as bancadas (`--lenhateste`, `--diaglenha`) invertem esta linha pra
	/// provar os dois lados. Lida pela propria linha do portao, em `GameServer.PagarALenha`.
	/// </summary>
	public static bool EsmagadoContaComoBriga = true;

	/// <summary>
	/// ESTA QUEDA PAGA? So o soco de um JOGADOR que esta com as proprias maos no corpo, e fora de combate.
	///
	/// *"durante combate n conta"*: a arvore pode cair no meio de uma luta, e ai ela e so cenario quebrado. Dois
	/// motivos fazem o "em combate" da lenha, e o servidor sabe qual foi pra dizer a frase certa: a BRIGA COM ALGUEM
	/// (`CombatState.EmBrigaComAlguem`: 90 s desde o ultimo golpe trocado com outro corpo -- a `combatTag` do DM) e o
	/// ESMAGAMENTO (<see cref="EsmagadoContaComoBriga"/>). A tag de combate inteira deste port NAO e lida: o dano sem
	/// algoz tambem a arma, e ela fica 90 s acesa depois que o esmagamento acabou.
	/// </summary>
	/// <param name="jogadorComAsProprias">Tem dono na tela e e o dono quem dirige (nao e NPC, clone nem corpo possuido).</param>
	/// <param name="emCombate">Trocou golpe com outro corpo nos ultimos 90 s, ou esta sendo esmagado (e isso conta).</param>
	public static bool Paga(bool jogadorComAsProprias, bool emCombate) =>
		(jogadorComAsProprias || PagaQualquerUmDeTeste) && (!emCombate || PagaEmCombateDeTeste);

	// =====================================================================
	// A REBROTA
	// =====================================================================

	/// <summary>
	/// EM QUANTO TEMPO A ARVORE VOLTA, em segundos: de oito a doze minutos, sorteado por arvore. NOSSO -- o
	/// `matspawners` do DM nao tem prazo (ele repoe ate um teto por no). O sorteio e o que impede um bosque derrubado
	/// de uma vez de voltar inteiro no mesmo segundo.
	/// </summary>
	public const double RebrotaMinS = 480, RebrotaMaxS = 720;

	/// <summary>O prazo DESTA arvore, com este sorteio (0 a 1).</summary>
	public static double SegundosAteRebrotar(double sorteio) =>
		RebrotaMinS + Math.Clamp(sorteio, 0, 1) * (RebrotaMaxS - RebrotaMinS);

	/// <summary>
	/// ATE ONDE A ARVORE PROCURA OUTRO CHAO quando o lugar dela foi tomado (base de jogador, maquina, nave), em
	/// tiles: os 15 do `rand(15,-15)` do `matspawners` (`Crafting Master.dm:392`). Aqui e o MAIS PERTO que serve, e nao
	/// um sorteio no quadrado -- o bosque continua onde estava.
	/// </summary>
	public const int RaioDeReplantio = 15;

	/// <summary>
	/// DE QUANTO EM QUANTO TEMPO a arvore que nao achou chao tenta de novo, em segundos. E o caso de alguem parado
	/// em cima do toco, ou de um lugar todo construido em volta.
	/// </summary>
	public const double NovaTentativaS = 30;

	/// <summary>
	/// QUANTAS ARVORES VOLTAM POR PASSADA (uma por segundo) em cada zona. Cada volta e um pacote confiavel por pessoa
	/// na zona; o teto espalha por alguns segundos a volta de uma floresta que caiu junta.
	/// </summary>
	public const int RebrotasPorPassada = 24;
}
