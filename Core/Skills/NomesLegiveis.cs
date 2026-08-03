namespace Jandirus.Core.Skills;

/// <summary>
/// TRADUZ O VOCABULARIO DO CODIGO PRO VOCABULARIO DO JOGADOR.
///
/// `physoffBuff` e `Solar_Flare` sao nomes de campo e de verb do DM -- servem pra casar as duas
/// bases e nao deviam nunca aparecer na tela. Mas o catalogo e extraido, entao o unico nome que o
/// jogo TEM e esse; sem uma traducao aqui, ou a interface mostra `staminagainMod` pro jogador ou
/// alguem escreve uma segunda tabela de 300 nomes a mao (que e o que se queria evitar extraindo).
///
/// A REGRA GERAL RESOLVE A MAIORIA e a tabela so cobre o que a regra erraria: `_` vira espaco
/// resolve os 47 verbs de uma vez. Campo de stat nao tem regra possivel (`kioff` nao vira "Ki Off"),
/// entao esses estao listados -- sao nove, nao trezentos.
/// </summary>
public static class NomesLegiveis
{
	private static readonly Dictionary<string, string> Campos = new(StringComparer.Ordinal)
	{
		["physoff"] = "ataque físico",
		["physdef"] = "defesa física",
		["technique"] = "técnica",
		["kioff"] = "ataque de Ki",
		["kidef"] = "defesa de Ki",
		["kiskill"] = "controle de Ki",
		["magi"] = "magia",
		["magiskill"] = "magia",
		["speed"] = "velocidade",
		["willpower"] = "força de vontade",
		["staminagain"] = "recuperação de fôlego",
		["maxstamina"] = "fôlego máximo",
		["kiregen"] = "regeneração de Ki",
		["satiation"] = "saciedade",
		["HPregen"] = "regeneração de vida",
		["bodyskill"] = "domínio corporal",
		["bodyreadiness"] = "prontidão corporal",
		["mana_cap"] = "reserva de mana",
		["lssjmult"] = "poder Legendary",
	};

	/// <summary>`physoffBuff` -> "ataque físico". Tira o sufixo de canal, que e detalhe interno.</summary>
	public static string Campo(string campo)
	{
		string raiz = campo;
		foreach (string suf in new[] { "Buff", "Mod", "buff", "mod" })
			if (raiz.EndsWith(suf, StringComparison.Ordinal) && raiz.Length > suf.Length)
			{ raiz = raiz[..^suf.Length]; break; }
		return Campos.GetValueOrDefault(raiz, raiz);
	}

	/// <summary>`Solar_Flare` -> "Solar Flare".</summary>
	public static string Habilidade(string verb) => verb.Replace('_', ' ');
}
