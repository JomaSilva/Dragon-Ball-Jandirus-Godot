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
/// entao esses estao listados.
///
/// ============================ A TABELA E CONFERIDA CONTRA O CATALOGO INTEIRO ============================
/// Ela cresceu de nove nomes pra sessenta quando a ficha passou a ler os DEGRAUS de nivel
/// (<see cref="FichaDeSkill"/>): os contadores da arvore da Mente (`kiawarenessskill`), as pericias
/// das dez familias de tiro (`beamskill`), os numeros das tecnicas (`SpiritBallCost`) e as chaves
/// (`canPower`) so existiam nos degraus, e nenhum deles tinha nome em portugues -- a ficha teria
/// mostrado "kiawarenessskill +1" pro jogador. A bancada `--menteskills` (secao 8) varre TODOS os
/// campos, genes, chaves e contadores que o `skills.json` e o `niveis.json` tocam e cobra que cada
/// um passe por <see cref="Conhece"/> / <see cref="ConheceChave"/> / <see cref="ConheceGene"/>: um
/// nome novo no extrator sem linha aqui fica vermelho la, em vez de vazar cru pra tela.
/// ========================================================================================================
/// </summary>
public static class NomesLegiveis
{
	private static readonly Dictionary<string, string> Campos = new(StringComparer.Ordinal)
	{
		// ---- os stats do lutador (a raiz, sem o sufixo de canal) ----
		["physoff"] = "ataque físico",
		["physdef"] = "defesa física",
		["technique"] = "técnica",
		["kioff"] = "ataque de Ki",
		["kidef"] = "defesa de Ki",
		// O STAT "Ki Skill" -- e o nome que a aba Stats ja usa ("Pericia de Ki"). Ate aqui ele saia
		// como "controle de Ki", que e o nome do CONTADOR `kicontrolskill` da arvore da Mente: os
		// dois na mesma ficha ("controle de Ki +0,05" do `kiskillBuff` ao lado de "controle de Ki +1"
		// do `kicontrolskill`) eram duas coisas com um nome so.
		["kiskill"] = "perícia de Ki",
		["magi"] = "magia",
		["magiskill"] = "magia",
		["speed"] = "velocidade",
		["willpower"] = "força de vontade",
		["staminagain"] = "recuperação de fôlego",
		["maxstamina"] = "fôlego máximo",
		["kiregen"] = "regeneração de Ki",
		["kicapacity"] = "capacidade de Ki",
		["satiation"] = "saciedade",
		["HPregen"] = "regeneração de vida",
		["bodyskill"] = "domínio corporal",
		["bodyreadiness"] = "prontidão corporal",
		["mana_cap"] = "reserva de mana",
		["mana_cap_mod"] = "reserva de mana",
		["BP"] = "BP",
		["hiddenpotential"] = "potencial oculto",
		["relBPmax"] = "ganho de BP",

		// ---- os campos que a genetica resolve no nascimento (o destino dos genes) ----
		["KiMod"] = "nível de energia",
		["BPMod"] = "poder de luta",
		["techmod"] = "técnica",
		["ascBPmod"] = "ascensão",
		["RegenerationDeSkill"] = "regeneração",

		// ---- as formas Saiyajin ----
		["lssjmult"] = "poder Legendary",
		["lssjdrain"] = "dreno Legendary",
		["lssjenergymod"] = "custo de energia Legendary",
		["restssjmult"] = "poder do SSJ contido",
		["unrestssjmult"] = "poder do SSJ descontrolado",
		["Omult"] = "multiplicador Oozaru",
		["Apeshitskill"] = "fala em Oozaru",
		["KaiokenMastery"] = "maestria do Kaioken",

		// ---- os CONTADORES da arvore da Mente (Mind.dm): o que os degraus somam e o que as regras
		//      de arvore leem (`kiawarenessskill>=1` acende a Basic Ki Awareness, Mind.dm:19) ----
		["kiawarenessskill"] = "percepção de Ki",
		["kicirculationskill"] = "circulação de Ki",
		["kicontrolskill"] = "controle de Ki",
		["kiefficiencyskill"] = "eficiência de Ki",
		["kigatheringskill"] = "acúmulo de Ki",
		["kieffusionskill"] = "efusão de Ki",
		["kibuffskill"] = "reforço de Ki",
		["kiarmor"] = "armadura de Ki",

		// ---- as pericias das dez familias de tiro (Effusion.dm e irmas) ----
		["beamskill"] = "perícia de beam",
		["blastskill"] = "perícia de blast",
		["guidedskill"] = "perícia de tiro guiado",
		["homingskill"] = "perícia de tiro teleguiado",
		["targetedskill"] = "perícia de tiro por alvo",
		["volleyskill"] = "perícia de rajada",
		["kiaiskill"] = "perícia de Kiai",
		["kidebuffskill"] = "perícia de debuff de Ki",
		["kidefenseskill"] = "perícia de defesa de Ki",
		["chargedskill"] = "perícia de tiro carregado",
		["bonusShots"] = "tiros extras por rajada",
		["createdbeamamount"] = "beams criáveis",
		["createdblastamount"] = "blasts criáveis",

		// ---- os numeros proprios de tecnicas ----
		["MedMod"] = "ganho ao meditar",
		["SpiritBallCost"] = "custo da Spirit Ball",
		["SpiritBallDamage"] = "dano da Spirit Ball",
		["SpiritFistCost"] = "custo do Spirit Fist",
		["SpiritFistDamage"] = "dano do Spirit Fist",
		["PDrain"] = "dreno permanente",
		["flightability"] = "perícia de voo",
		["jirenskill"] = "perícia de Jiren",
	};

	/// <summary>
	/// `physoffBuff` -> "ataque físico". O nome INTEIRO primeiro (`mana_cap_mod`, `MedMod`), e so
	/// depois a raiz sem o sufixo de canal, que e detalhe interno.
	/// </summary>
	public static string Campo(string campo)
	{
		if (Campos.TryGetValue(campo, out string? inteiro)) return inteiro;
		return Campos.GetValueOrDefault(Raiz(campo), Raiz(campo));
	}

	/// <summary>A tabela tem este campo (inteiro ou pela raiz)? E a pergunta da bancada de cobertura.</summary>
	public static bool Conhece(string campo) => Campos.ContainsKey(campo) || Campos.ContainsKey(Raiz(campo));

	private static string Raiz(string campo)
	{
		foreach (string suf in new[] { "Buff", "Mod", "buff", "mod" })
			if (campo.EndsWith(suf, StringComparison.Ordinal) && campo.Length > suf.Length)
				return campo[..^suf.Length];
		return campo;
	}

	/// <summary>`Solar_Flare` -> "Solar Flare".</summary>
	public static string Habilidade(string verb) => verb.Replace('_', ' ');

	// =====================================================================
	// OS GENES
	// =====================================================================
	/// <summary>
	/// O STAT DO GENOMA como o DM o indexa (`add_to_stat("Energy Level", 0.05)`), em portugues. A
	/// traducao pra CAMPO do lutador mora em <c>EfeitosDeSkill.DeGene</c>; esta e a de TELA, e as
	/// duas listam os mesmos sete nomes.
	/// </summary>
	private static readonly Dictionary<string, string> Genes = new(StringComparer.Ordinal)
	{
		["Energy Level"] = "nível de energia",
		["Battle Power"] = "poder de luta",
		["Potential"] = "potencial",
		["Ascension Mod"] = "ascensão",
		["Regeneration"] = "regeneração",
		["Skillpoint Mod"] = "ganho de marcos",
		["Tech Modifier"] = "técnica",
		["Lifespan"] = "longevidade",
	};

	public static string Gene(string stat) => Genes.GetValueOrDefault(stat, stat.ToLowerInvariant());
	public static bool ConheceGene(string stat) => Genes.ContainsKey(stat);

	// =====================================================================
	// AS CHAVES (`campo = n`)
	// =====================================================================
	/// <summary>
	/// UMA CHAVE ESCRITA POR SKILL, como frase -- ou NULO quando ela e marca interna e nao efeito.
	///
	/// A chave nao e numero somado: `canPower = 1` e "agora voce pode carregar Ki", nao "+1 de
	/// canPower". Cada uma pede a frase dela, e a frase e o que este port faz com a chave (a
	/// `hellstar_disabled = 0` LIBERA; `didbodychange = 1` e um marcador que o Body.dm usa pra
	/// recalcular a aparencia, e o jogador nao tem o que fazer com ele -- sai nulo e a ficha o omite).
	/// </summary>
	public static string? Chave(string chave, double valor)
	{
		string n = Numero(valor);
		return chave switch
		{
			// ---- marcas internas: nao sao efeito, a ficha nao as lista ----
			"didbodychange" or "legAngerMigrated" or "gravitatecheck" or "expbarrier" => null,

			// ---- a arvore da Mente ----
			"KiUnlockPercent" => "desperta o Ki",
			"MeditateGivesKiRegen" => "meditar passa a regenerar Ki",
			"canPower" => "libera carregar Ki (tecla C)",
			"buffregen" => "buffs de Ki passam a curar com o tempo",
			"effusionspecial" => "abre a árvore Effusive Specialty",
			"effspec" => $"especialidade de efusão = {n}",
			"KiSpecialty" => "especialidade de Ki",

			// ---- tecnicas e golpes ----
			"HBCost" => $"custo do Hell Ball = {n}",
			"rushmod" => $"golpes por rush = {n}",
			"giant_form_efficiency" => $"eficiência da Forma Gigante = {n}",
			"canbigform" => "libera a Forma Gigante",
			"cangivepower" => "pode doar poder a outro",
			"kiforceful" => "os ataques de Ki empurram",
			"kiinterfere" => "os ataques de Ki interferem",
			"kishock" => "os ataques de Ki atordoam",
			"CanViewFrozenTime" => "enxerga o tempo parado",
			"GotWeaponBuff" => "buff de arma",
			"haszanzo" => $"sabe o Zanzoken (nível {n})",
			"can_stretch_arms" => "estica os braços (agarra de longe)",
			"partplant" => "parte planta: meditar na água alimenta",
			"psythre" => "Psycho Thread ligado",

			// ---- magia ----
			"word_power" => "fala com poder (magia)",
			"ritual_power" => "pode ativar rituais",
			"can_sup_el" => "libera o super elemento",
			"magiBuff" => $"magia = {n}",

			// ---- formas e linhagens ----
			"hasayyform" => $"libera a transformação Alien (estágio {n})",
			"snamek" => "Super Namekuseijin",
			"hellstar_disabled" => valor == 0 ? "libera a Estrela do Inferno" : "bloqueia a Estrela do Inferno",
			"hasFPLB" => "libera o Limit Breaker (SSJ4 Full Power)",
			"ismssj" => "Super Saiyajin dominado",
			"ssj3able" => "pode alcançar o SSJ3",
			"ssj3mastery" => $"maestria do SSJ3 = {n}",
			"ssjmult" => $"poder do SSJ = {n}",
			"ssjmod" => $"modificador do SSJ = {n}",
			"ssjdrain" => $"dreno do SSJ = {n}",
			"ssjenergymod" => $"custo de energia do SSJ = {n}",
			"ssj2mod" => $"modificador do SSJ2 = {n}",
			"ssj2energymod" => $"custo de energia do SSJ2 = {n}",
			"restssjdrain" => $"dreno do SSJ contido = {n}",
			"unrestssjdrain" => $"dreno do SSJ descontrolado = {n}",
			"legendaryAngerBonus" => $"+{Numero(valor / 100)}× ao teto de raiva",
			"jirenskill" => $"perícia de Jiren = {n}",
			"pitted" => $"trilha Arlian escolhida ({n})",
			"gravitate" => "domina a gravidade",
			"flightability" => $"perícia de voo = {n}",
			// `savant.teleskill=70` so pra Yardrat (yardrat.dm:85-87) -- chega pela casa POR RACA
			"teleskill" => $"perícia de teletransporte = {n}",

			_ => $"{Campo(chave)} = {n}",
		};
	}

	/// <summary>A tabela de chaves tem esta (com frase ou como marca interna)?</summary>
	public static bool ConheceChave(string chave) => chave switch
	{
		"didbodychange" or "legAngerMigrated" or "gravitatecheck" or "expbarrier"
		or "KiUnlockPercent" or "MeditateGivesKiRegen" or "canPower" or "buffregen" or "effusionspecial"
		or "effspec" or "KiSpecialty" or "HBCost" or "rushmod" or "giant_form_efficiency" or "canbigform"
		or "cangivepower" or "kiforceful" or "kiinterfere" or "kishock" or "CanViewFrozenTime" or "GotWeaponBuff"
		or "haszanzo" or "can_stretch_arms" or "partplant" or "psythre" or "word_power" or "ritual_power"
		or "can_sup_el" or "magiBuff" or "hasayyform" or "snamek" or "hellstar_disabled" or "hasFPLB" or "ismssj"
		or "ssj3able" or "ssj3mastery" or "ssjmult" or "ssjmod" or "ssjdrain" or "ssjenergymod" or "ssj2mod"
		or "ssj2energymod" or "restssjdrain" or "unrestssjdrain" or "legendaryAngerBonus" or "jirenskill"
		or "pitted" or "gravitate" or "flightability" or "teleskill" => true,
		_ => false,
	};

	// =====================================================================
	// OS CONTADORES DE EVENTO (exp por golpe disparado)
	// =====================================================================
	/// <summary>`beamcounter` -> "beam disparado": o evento que credita exp nas familias de tiro.</summary>
	public static string Contador(string contador) => contador switch
	{
		"beamcounter" => "beam disparado",
		"blastcounter" => "blast disparado",
		"kibuffcounter" => "buff de Ki usado",
		"kidebuffcounter" => "debuff de Ki usado",
		"kidefensecounter" => "defesa de Ki usada",
		"guidedcounter" => "tiro guiado disparado",
		"homingcounter" => "tiro teleguiado disparado",
		"kiaicounter" => "Kiai disparado",
		"targetedcounter" => "tiro por alvo disparado",
		"volleycounter" => "rajada disparada",
		"effusioncounter" => "efusão disparada",
		_ => contador,
	};

	public static bool ConheceContador(string contador) => Contador(contador) != contador;

	// =====================================================================
	// OS ESTADOS DO CORPO QUE RENDEM EXP
	// =====================================================================
	/// <summary>A condicao de ganho de exp (<see cref="RegraDeNivel.Estado"/>), como o jogador a le.</summary>
	public static string Estado(RegraDeNivel.Estado estado) => estado switch
	{
		RegraDeNivel.Estado.Sempre => "o tempo todo",
		RegraDeNivel.Estado.Meditando => "meditando",
		RegraDeNivel.Estado.Voando => "voando",
		RegraDeNivel.Estado.Treinando => "treinando",
		RegraDeNivel.Estado.Ocioso => "sem meditar nem voar",
		RegraDeNivel.Estado.Lutando => "em luta",
		RegraDeNivel.Estado.TreinandoOuLutando => "treinando ou em luta",
		RegraDeNivel.Estado.Estudando => "estudando alguém",
		RegraDeNivel.Estado.Observando => "observando alguém",
		RegraDeNivel.Estado.ComBuffDeKi => "com um buff de Ki de pé",
		RegraDeNivel.Estado.KiAcimaDoNormal => "com o Ki acima do normal, mais quanto mais acima",
		RegraDeNivel.Estado.GastandoKi => "ao gastar Ki",
		RegraDeNivel.Estado.TanqueAbaixoDe90 => "com o Ki abaixo de 90%, mais quanto mais vazio",
		// morta no original: ver `RegraDeNivel.Estado.MeditacaoProfunda`
		RegraDeNivel.Estado.MeditacaoProfunda => "em meditação profunda (nunca acontece: morta no jogo antigo)",
		RegraDeNivel.Estado.Senao => "senão",
		_ => estado.ToString(),
	};

	/// <summary>
	/// NUMERO PRA TELA, com virgula decimal e sem depender da cultura da maquina: "0,05", "2", "1,5".
	/// Cliente e servidor (e a bancada) montam a MESMA frase em qualquer Windows.
	/// </summary>
	public static string Numero(double v) =>
		v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

	/// <summary>"+0,05" / "-1" / "+20".</summary>
	public static string ComSinal(double v) => (v >= 0 ? "+" : "") + Numero(v);

	/// <summary>"5.000" -- inteiro com ponto de milhar.</summary>
	public static string Inteiro(double v) =>
		((long)Math.Round(v)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');

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

	// Os contadores da Mente (`kiawarenessskill`...) NAO estao mais aqui: moram na tabela de campos,
	// e o `default` chega neles. Uma tabela so -- a segunda copia e a que envelhece.
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
		"effusionspecial" => "especialidade de efusão",
		"effspec" => "especialidade de efusão",
		"hasdemitype" => "linhagem de semideus",
		_ => Campo(id).Replace('_', ' '),
	};
}
