namespace Jandirus.Core.Stats;

/// <summary>
/// O PODER DE LUTA NUMA FRASE DO SERVIDOR: em NUMERO so pra quem tem medidor; pros outros, RELATIVO.
///
/// ============================ A REGRA PRINCIPAL DO BP (dono) ============================
/// *"Nao e possivel saber o proprio bp ou de ninguem sem um medidor como um scouter, sem scouter vc n tem
/// como saber seu bp de nenhuma forma, oq vc sabe e se alguem e mais forte ou mais fraco q vc em porcentagem"*.
///
/// O corte do PACOTE mora no servidor (`GameServer.Sigilo.cs`: a ficha sai sem o numero pra quem nao tem
/// scouter). O que faltava era o TEXTO: seis frases de chat diziam o BP de quem as lia a qualquer um -- o
/// desejo de poder, o desejo supremo, a recusa do Planet Destroy, o aviso e o resultado da fusao
/// Namekuseijin, e a recusa do Oozaru Dourado. O dono as achou pelo playtest de 2026-10-12 e mandou fechar
/// todas "do mesmo jeito": numero so com scouter, senao porcentagem.
///
/// TRES JEITOS DE DIZER SEM O NUMERO, e cada frase usa o que NAO deixa refazer a conta:
///   * QUANTO CRESCEU (<see cref="QuantoMaisForte"/>): "cerca de 25% mais forte". Nao ha absoluto em lugar
///     nenhum da frase.
///   * A FRACAO DE UM LIMIAR SECRETO (<see cref="FracaoDoLimiar"/>): "cerca de 62% do que ela pede". So serve
///     quando o limiar e PESSOAL e escondido (a porta sorteada do Oozaru Dourado): e fracao de um numero que
///     ninguem conhece.
///   * ALCANCA OU NAO, escrito no proprio lugar: quando o limiar e do MUNDO e sai na mesma frase (a gravidade
///     do planeta no Planet Destroy, o minimo da conquista), a porcentagem entregaria o BP -- limiar vezes
///     fracao. Ai so se diz de que lado o corpo esta, que e o que a propria recusa ja conta.
/// ========================================================================================
/// </summary>
public static class PoderNaFrase
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas --desejoteste, --planetateste, --fusaoduplateste e --formasteste): as frases
	/// trazem o numero mesmo pra quem nao tem medidor.
	/// </summary>
	public static bool NumeroSemMedidorDeTeste;

	/// <summary>
	/// ESTA FRASE PODE TRAZER O NUMERO? So com medidor -- o `scouteron` do DM (`Inventory.dm:155`); no servidor
	/// quem responde e o `TemScouter` do leitor da frase.
	/// </summary>
	public static bool EmNumero(bool temMedidor) => temMedidor || NumeroSemMedidorDeTeste;

	/// <summary>A partir de quantas vezes o crescimento deixa de ser dito em porcento ("900%") e vira "vezes".</summary>
	public const double VezesEmVezDePorcento = 10;

	/// <summary>
	/// QUANTO O PODER CRESCEU, sem numero absoluto: "cerca de 25% mais forte", "cerca de 12 vezes mais forte".
	/// E a unica leitura de progresso de quem nao tem aparelho, e o proprio DM a deixa passar (o multiplicador
	/// e relativo: nao revela de quanto se partiu).
	///
	/// O PEDACO ENTRA DEPOIS DE "voce esta" / "voce ficou", e por isso nao leva acento nenhum: metade das frases
	/// do servidor que o usam sao escritas sem acento, e a outra metade com.
	/// </summary>
	public static string QuantoMaisForte(double antes, double depois)
	{
		if (antes <= 0) return "muito mais forte";
		double vezes = depois / antes;
		if (vezes <= 1) return "igual a antes";
		if (vezes >= VezesEmVezDePorcento) return $"cerca de {vezes:0.#} vezes mais forte";

		double porcento = (vezes - 1) * 100;
		return porcento < 0.1 ? $"menos de {0.1:0.#}% mais forte" : $"cerca de {porcento:0.#}% mais forte";
	}

	/// <summary>
	/// QUANTO DE UM LIMIAR SECRETO O CORPO JA TEM: "cerca de 62% do que ela pede". Nunca diz 100% (quem chegou
	/// la nao ouve recusa), e so vale pra limiar que o jogador nao tem como conhecer -- ver o cabecalho.
	/// </summary>
	public static string FracaoDoLimiar(double tem, double pede)
	{
		if (pede <= 0 || tem <= 0) return "pouco do que ela pede";
		return $"cerca de {Math.Min(tem / pede * 100, 99):0}% do que ela pede";
	}

	/// <summary>
	/// QUANTO DO PODER DE OUTRO CORPO ENTROU EM QUEM LE: "todo o poder dele", "10% do poder dele". E a regra
	/// da absorcao dita sem numero -- a fracao de um poder que quem le nao conhece.
	/// </summary>
	public static string ParteDoPoderDele(double parte, double poderDele)
	{
		if (poderDele <= 0 || parte <= 0) return "parte do poder dele";
		double porcento = parte / poderDele * 100;
		return porcento >= 99.95 ? "todo o poder dele" : $"{porcento:0.#}% do poder dele";
	}

	/// <summary>
	/// UM PODER MEDIDO CONTRA O DE QUEM LE -- a unica comparacao que a regra do dono deixa a quem nao tem
	/// medidor: "cerca de 3,2 vezes o seu", "cerca de 25% maior que o seu", "cerca de 80% do seu".
	/// </summary>
	public static string ContraOSeu(double outro, double seu)
	{
		if (seu <= 0) return "muito maior que o seu";
		double vezes = outro / seu;
		if (vezes >= VezesEmVezDePorcento) return $"cerca de {vezes:0.#} vezes o seu";
		if (vezes < 1) return $"cerca de {vezes * 100:0}% do seu";

		double aMais = (vezes - 1) * 100;
		return aMais < 0.1 ? "igual ao seu" : $"cerca de {aMais:0.#}% maior que o seu";
	}
}
