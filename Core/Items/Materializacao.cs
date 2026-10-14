using Jandirus.Core.Appearance;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Items;

/// <summary>
/// **MATERIALIZAR** -- a skill `Materialize` (`Magic_Tree.dm:243-262`): criar um item do nada.
///
/// O dono, 2026-10-13: *"tem skills q n estao funcionando como materialize etc, essas skills q criam itens tem q ser
/// quando a implementaçao de dropar item tiver feita, pq por exemplo materialize criaria um item no chao (itens como
/// peso, roupa etc)"*.
///
/// ============================ NO DM ELA E UM RITUAL ============================
/// A skill ensina o ritual `Materialize_Item` (`Rituals_Manipulation.dm:521-570`) e a palavra que o ativa
/// (*"energy collesque"*): desenha-se o circulo, poe-se Gillyweed e Aconite nele, alimenta-se o circulo com 1.500 de
/// energia magica (`ritual_cost`) e diz-se a palavra. Ai o jogo pergunta o que fazer:
///   * "Clothes": um `Gi_Top` no chao, aos pes de quem conjura (`locate(u.x,u.y,u.z)`), com nome, folha e cor a
///     escolher;
///   * "Weight": uns pesos na mochila, com folha e cor a escolher e o peso que se quiser ate `intBPcap * Emagiskill`;
///   * "Weapon": uma arma de corpo a corpo, no chao.
///
/// ============================ AQUI ELA E UMA TECNICA, E O QUE MUDA ESTA DITO ============================
/// O port nao tem o sistema de magia (circulo, palavra, ingrediente, energia magica -- e a divida "magia" do
/// `CensoDeSkills`), e por isso a skill somava +1 de pericia esoterica e mais nada. Agora ela concede o verb
/// <see cref="Verbo"/> (`Skills.VerbosDoPort`), que cria o item direto:
///   * O PRECO e o do ritual, em Ki: os 1.500 de energia magica sao uma FRACAO da reserva magica de quem conjura
///     (`MagicCap = 2000 * Emagiskill * mana_cap_mod`, que o port ja calcula), e e essa fracao que sai do Ki -- tres
///     quartos pra quem tem pericia esoterica 1, menos pra quem tem mais (<see cref="Custo"/>). Os ingredientes nao
///     vieram: o port nao tem alquimia.
///   * A ROUPA e qualquer folha do guarda-roupa, na cor que se quiser (o DM deixa enviar um arquivo; aqui a escolha
///     e do catalogo, como a dos pesos e a da armadura). E a primeira fonte de roupa nova do jogo.
///   * OS PESOS sao o item `Weights` como ele sai da fabrica. REGRA DO DONO (2026-10-12): o peso maximo se COMPRA
///     (`PesoDeTreino`) -- materializa-los com o peso que se quisesse seria a porta dos fundos dessa regra. E caem
///     no CHAO, como a roupa (*"materialize criaria um item no chao (itens como peso, roupa etc)"*; no DM os pesos
///     iam pra mochila).
///   * A ARMA nao veio: o port nao tem arma de corpo a corpo como item.
/// ==========================================================================================================
/// </summary>
public static class Materializacao
{
	/// <summary>O typepath da skill -- `/datum/skill/general/materialization` (`Magic_Tree.dm:243`).</summary>
	public const string Skill = "/datum/skill/general/materialization";

	/// <summary>O verb que a skill concede neste port. E o id da tecnica (o botao, a tecla e o `SabeTecnica` leem daqui).</summary>
	public const string Verbo = "Materialize";

	/// <summary>
	/// O comando do canal de VERBOS por onde a tela manda a escolha: `materializar roupa/Clothes_GiTop/2A2A33`. Nao
	/// vai pelo canal de habilidade porque o id de habilidade cabe 48 letras, e o nome de uma folha mais a cor passam
	/// disso -- e o `GetString` do LiteNetLib devolve VAZIO acima do limite, nao trunca. O portao e o MESMO do botao:
	/// o servidor entrega o pedido ao `UsarHabilidade`.
	/// </summary>
	public const string VerboDeCanal = "materializar";

	/// <summary>O pedido dos pesos de treino.</summary>
	public const string DePesos = "pesos";

	/// <summary>O prefixo do pedido de uma roupa: `roupa/&lt;folha&gt;` ou `roupa/&lt;folha&gt;/&lt;RRGGBB&gt;`.</summary>
	public const string DeRoupa = "roupa";

	/// <summary>`ritual_cost = 1500` (`Rituals_Manipulation.dm:524`), em energia magica.</summary>
	public const double CustoDoRitual = 1500;

	/// <summary>
	/// QUANTO KI SAI: a fracao que os 1.500 do ritual sao da reserva magica deste corpo (`Fighter.MagicCap`), aplicada
	/// ao Ki cheio dele -- e nunca mais que o Ki inteiro.
	/// </summary>
	public static double Custo(Fighter f) => f.MaxKi * Math.Min(1, CustoDoRitual / Math.Max(f.MagicCap, 1));

	/// <summary>
	/// O ITEM QUE ESTE PEDIDO MATERIALIZA (o id dele, pronto pro chao), ou nulo com o motivo. A folha tem de estar no
	/// GUARDA-ROUPA: a armadura nao se materializa (ela e de tecnologia, e protege -- `ArmaduraDeTech`).
	/// </summary>
	public static string? Item(string pedido, VisualCatalog? cat, out string porque)
	{
		porque = "";
		string[] p = pedido.Split('/');
		if (p.Length == 1 && string.Equals(p[0], DePesos, StringComparison.OrdinalIgnoreCase)) return CatalogoDeItens.Pesos;

		if (p.Length is 2 or 3 && string.Equals(p[0], DeRoupa, StringComparison.OrdinalIgnoreCase) && p[1].Length > 0)
		{
			RoupaGuardada? peca = RoupaGuardada.Ler($"{RoupaGuardada.Prefixo}{p[1]}|{(p.Length == 3 ? p[2] : "")}");
			if (peca == null) { porque = "essa cor nao existe: sao seis digitos (RRGGBB)."; return null; }
			if (peca.Ficha(cat) == null) { porque = $"nao ha roupa chamada {p[1]} pra materializar."; return null; }
			return peca.Id;
		}

		porque = "materializar o que? Pesos de treino ou uma roupa.";
		return null;
	}
}
