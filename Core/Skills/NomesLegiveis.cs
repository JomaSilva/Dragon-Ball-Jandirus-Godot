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

	// =====================================================================
	// A CATEGORIA DE UMA SKILL
	// =====================================================================
	/// <summary>
	/// O `type` da skill do DM, reduzido a UMA de seis categorias em portugues.
	///
	/// ============================ POR QUE NAO MOSTRAR O `type` COMO ESTA ============================
	/// Sao 24 strings distintas no catalogo e elas nao sao categorias, sao o que cada autor digitou:
	/// `"Misc"` e `"misc"`, `"Spirit Buff"` e `"Sprit Buff"` (erro de digitacao), `"Magic"` e `"Magical"`,
	/// `"Physical"` e `"Physical Buff"`. A aba Skills imprimia isso em verde na coluna de valor, onde
	/// o olho le STATUS -- e "Body Buff" em verde ao lado de "Basic Training" lia como "ativa".
	///
	/// A tabela mora no Core porque ela decide DUAS coisas do cliente de uma vez -- o texto da aba e
	/// o icone de categoria do card -- e uma segunda copia num dos dois envelheceria calada.
	/// ==================================================================================================
	/// </summary>
	public static string Categoria(string tipo)
	{
		string t = tipo.Trim().ToLowerInvariant();
		if (t.Length == 0) return "diversos";
		if (t.Contains("buff")) return "buff";
		if (t.Contains("form") || t.Contains("transformation") || t.Contains("fusion")) return "forma";
		if (t.Contains("magic")) return "magia";
		if (t.Contains("ki") || t.Contains("spirit attack")) return "ki";
		if (t.Contains("physical") || t.Contains("body skill") || t.Contains("melee")) return "físico";
		return "diversos";
	}

	// =====================================================================
	// A CONDICAO DE UMA REGRA DE ARVORE, EM PORTUGUES
	// =====================================================================
	/// <summary>
	/// `kiawarenessskill>=1` -> "percepção de Ki ≥ 1"; `Rank=='Demon Lord'` -> "cargo = Demon Lord";
	/// `invested>=4&amp;&amp;Class!='None'` -> "marcos investidos ≥ 4 e classe ≠ None".
	///
	/// E a expressao do DM que o extrator escreveu (ver `RegraDeArvore`), lida token a token. Ela
	/// existe porque o veredito devolve a condicao CRUA (`Veredito.Acendedor`) -- e crua ela e um
	/// nome de variavel na cara do jogador. O que o port nao conhece sai com o `_` trocado por
	/// espaco, que e a mesma regra do <see cref="Habilidade"/>: melhor um nome estranho do que
	/// esconder a condicao.
	/// </summary>
	public static string Condicao(string condicao)
	{
		if (condicao.Length == 0) return "sempre";
		var sb = new System.Text.StringBuilder();
		int i = 0;
		while (i < condicao.Length)
		{
			char c = condicao[i];
			if (c == '\'')
			{
				int f = condicao.IndexOf('\'', i + 1);
				if (f < 0) f = condicao.Length;
				sb.Append(condicao[(i + 1)..f].Replace('_', ' '));
				i = f + 1;
				continue;
			}
			if (char.IsLetter(c) || c == '_')
			{
				int j = i;
				while (j < condicao.Length && (char.IsLetterOrDigit(condicao[j]) || condicao[j] is '_' or '.')) j++;
				sb.Append(Identificador(condicao[i..j]));
				i = j;
				continue;
			}
			if (Pega("&&", " e ") || Pega("||", " ou ") || Pega(">=", " ≥ ") || Pega("<=", " ≤ ")
				|| Pega("==", " = ") || Pega("!=", " ≠ ") || Pega(">", " > ") || Pega("<", " < ")
				|| Pega("!", "não ") || Pega("?", "(condição que o port não lê)"))
				continue;
			// O ESPACO FICA. A condicao do extrator vem sem espaco nenhum (`kiawarenessskill>=1`), e o
			// leitor os DESCARTAVA -- so que o acendedor por DEGRAU (`SkillBook.AcendedorDe`) e uma
			// FRASE na mesma gramatica ('Basic Ki Awareness' chega ao nivel 100), e sem esta linha ela
			// saia colada na tela: "chegaaonivel100". Os operadores ja entram com espaco dos dois lados;
			// o espaco dobrado e desfeito logo abaixo, entao nada muda pras condicoes de arvore.
			sb.Append(c);
			i++;
		}
		// os operadores entram com espaco dos dois lados; dois vizinhos deixariam espaco dobrado
		string s = sb.ToString();
		while (s.Contains("  ")) s = s.Replace("  ", " ");
		return s.Trim();

		bool Pega(string tok, string troca)
		{
			if (string.CompareOrdinal(condicao, i, tok, 0, tok.Length) != 0) return false;
			sb.Append(troca);
			i += tok.Length;
			return true;
		}
	}

	private static string Identificador(string id) => id switch
	{
		"invested" => "marcos investidos",
		"Class" => "classe",
		"Race" => "raça",
		"Rank" => "cargo",
		"weaponeq" => "arma equipada",
		"hasssj" => "Super Saiyajin despertado",
		"hasssj2" => "Super Saiyajin 2 despertado",
		"gravitate" => "gravidade dominada",
		"kiawarenessskill" => "percepção de Ki",
		"kicirculationskill" => "circulação de Ki",
		"kicontrolskill" => "controle de Ki",
		"kiefficiencyskill" => "eficiência de Ki",
		"kigatheringskill" => "reunião de Ki",
		"kieffusionskill" => "efusão de Ki",
		"kibuffskill" => "reforço de Ki",
		"effusionspecial" => "especialidade de efusão",
		"effspec" => "especialidade de efusão",
		"hasdemitype" => "linhagem de semideus",
		_ => Campo(id).Replace('_', ' '),
	};
}
