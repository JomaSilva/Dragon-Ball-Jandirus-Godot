namespace Jandirus.Core.Skills;

/// <summary>
/// A FICHA DE UMA SKILL EM TEXTO: o que ela faz, em portugues, a partir dos DOIS canais que o
/// extrator traz do DM -- a COMPRA (`after_learn()`: <see cref="Skill.Buffs"/>, <see cref="Skill.Verbos"/>,
/// <see cref="Skill.Flags"/>...) e o NIVEL (`effector()`: os <see cref="Degrau"/> da <see cref="RegraDeNivel"/>,
/// e as fontes de exp que fazem a skill subir).
///
/// ============================ A FICHA SO LIA A COMPRA, E A MENTE MORA NO NIVEL ============================
/// O dono abriu a ficha de "Basic Ki Awareness" e leu *"O efeito mecânico desta habilidade ainda
/// não foi portado."* -- em TODAS as dezessete de "Strength of Mind" menos a Ki Unlocked. O efeito
/// estava portado: `kiawarenessskill += 1` a cada 5 niveis, o Study Other no nivel 10, a Advanced
/// acesa no 100 (`Mind.dm:171-186`), tudo aplicado pelo `NiveisDeSkill.Aplicar` e provado pela
/// `--menteskills`. So que a ficha do cliente montava o texto so de `Skill.Buffs/Mults/Verbos/Estilo`
/// -- o canal da COMPRA -- e na Mente esse canal e vazio por desenho do DM: o `after_learn()` dela e
/// um `to_chat`. Dezesseis skills com efeito de verdade e uma tela dizendo que nao tinham nenhum.
///
/// E nao era so a Mente: e toda skill cujo golpe mora no `switch(level)` (o Dash Attack no nivel 2
/// do Green Dean, o Hokuto Hyakuretsu Ken no 2 do Hokuto no Shinken -- 60 dos 189 verbs do jogo).
///
/// AGORA A PERGUNTA E UMA SO, feita aqui (<see cref="TemEfeitoPortado"/>) e respondida igual pela
/// ficha, pela aba Skills, pelo censo (`CensoDeSkills`) e pelo console do extrator: uma folha so e
/// "sem efeito" quando NEM a compra NEM degrau nenhum lhe da coisa alguma.
/// ========================================================================================================
///
/// ============================ POR QUE MORA NO CORE, E NAO NO CLIENTE ============================
/// O texto e funcao PURA do catalogo e das regras de nivel -- nada de Godot. Morando aqui, a
/// bancada do servidor (`--menteskills`, secao 8) monta a MESMA frase que a tela vai mostrar e
/// cobra, uma a uma, que nenhuma das dezessete saia muda e que nenhum nome cru do DM
/// (`kiawarenessskill`) vaze pra tela. No cliente ela seria so olho.
/// ==============================================================================================
/// </summary>
public static class FichaDeSkill
{
	/// <summary>O texto da ficha, em pedacos, pra tela pintar cada um do jeito dele.</summary>
	public sealed class Texto
	{
		/// <summary>O que a COMPRA da, uma linha por efeito ("ataque físico +0,4", "habilidade nova: Kiai").</summary>
		public List<string> NaCompra = [];

		/// <summary>Como ela SOBE: ate que nivel, o que o nivel custa e o que rende exp. Vazio = nao sobe.</summary>
		public string Progressao = "";

		/// <summary>O que cada NIVEL da ("a cada 5 níveis: percepção de Ki +1", "nível 10: habilidades novas: ...").</summary>
		public List<string> PorNivel = [];

		/// <summary>A soma no topo ("no nível 100, somando tudo: percepção de Ki +20, ..."). Vazio quando nada acumula.</summary>
		public string NoTopo = "";

		/// <summary>Ela faz ALGUMA coisa neste port -- na compra ou por nivel?</summary>
		public bool TemEfeito => NaCompra.Count > 0 || PorNivel.Count > 0;

		/// <summary>Uma linha so, pra aba Skills e pro tooltip do card.</summary>
		public string Resumo = "";
	}

	/// <summary>A frase da ficha quando <see cref="Texto.TemEfeito"/> e falso. Uma so, pra bancada cobrar pelo texto.</summary>
	public const string SemEfeitoAinda = "O efeito mecânico desta habilidade ainda não foi portado.";

	/// <summary>
	/// A MARCA DE HONESTIDADE numa linha: o campo que o DM soma e que o <c>Fighter</c> deste port NAO
	/// TEM. O `EfeitosDeSkill.Aplicar` descarta esse campo calado (`Campo()` devolve nulo); a ficha
	/// diria "custo da Spirit Ball ×0,5" prometendo um numero que nunca chega -- que e exatamente o
	/// defeito que esta classe existe pra matar, na direcao oposta.
	/// </summary>
	public const string CampoQueOPortNaoTem = "ainda sem efeito neste port";

	/// <summary>Um degrau que da alguma coisa -- e nao so avisa ou troca a barreira.</summary>
	public static bool DegrauTemEfeito(Degrau d) =>
		d.Buffs.Count > 0 || d.Mults.Count > 0 || d.Genes.Count > 0 || d.Flags.Count > 0
		|| d.Verbos.Length > 0 || d.VerbosPorCasa.Length > 0 || d.Destrava.Length > 0 || d.Concede.Length > 0;

	/// <summary>
	/// A PERGUNTA UNICA: o port sabe o que esta skill faz? Compra (<see cref="Skill.SemEfeito"/> ao
	/// contrario) OU algum degrau com efeito. Arvore nunca e "sem efeito" -- ela nao e uma skill.
	/// </summary>
	public static bool TemEfeitoPortado(Skill s, RegraDeNivel? r) =>
		!s.SemEfeito || (r != null && Array.Exists(r.Degraus, DegrauTemEfeito));

	// =====================================================================
	// MONTAR
	// =====================================================================
	public static Texto Montar(SkillCatalog cat, Skill s, RegraDeNivel? r)
	{
		var t = new Texto();

		// ---- a compra ----
		foreach ((string campo, double v) in s.Buffs) t.NaCompra.Add(Soma(campo, v));
		foreach ((string campo, double v) in s.Mults) t.NaCompra.Add(Fator(campo, v));
		foreach ((string stat, double v) in s.Genes) t.NaCompra.Add(Gene(stat, v));
		foreach ((string chave, double v) in s.Flags)
			if (NomesLegiveis.Chave(chave, v) is { } frase) t.NaCompra.Add(frase);
		foreach (GanhoNaCompra g in s.Compra) t.NaCompra.Add(Ganho(g));
		if (s.Verbos.Length > 0) t.NaCompra.Add(Habilidades(s.Verbos));
		if (s.Estilo.Length > 0) t.NaCompra.Add($"estilo de luta: {s.Estilo}");
		if (s.Escolhas.Length > 0)
		{
			// as casas saem RECUADAS (quatro espacos): a tela as pinta sem o marcador de item
			t.NaCompra.Add("escolha única entre:");
			foreach (Escolha e in s.Escolhas) t.NaCompra.Add($"    {e.Rotulo}: {TextoDaCasa(e)}");
		}
		foreach (Escolha e in s.PorRaca) t.NaCompra.Add($"se for {e.Rotulo}: {TextoDaCasa(e)}");

		// ---- o nivel ----
		// SEM REGRA DE NIVEL NAO HA O QUE DIZER. O `maxlevel = 3` de fabrica do DM (skill.dm:10) esta
		// em 200 folhas cujo nivel nao muda NADA -- o extrator so emite regra pra quem tem degrau, e
		// sem degrau subir e um numero que ninguem le. Dizer "ainda nao sobe de nivel" aqui seria
		// anunciar como divida do port uma coisa que no original tambem nao acontece.
		int max = Math.Max(1, s.MaxNivel);
		if (r != null)
		{
			t.Progressao = Progressao(r, max);
			// os periodicos primeiro (sao o ganho constante), depois os marcos em ordem de nivel
			foreach (Degrau d in r.Degraus.Where(d => d.Periodo > 0).OrderBy(d => d.Periodo))
				if (Partes(cat, d) is { Count: > 0 } p) t.PorNivel.Add($"a cada {d.Periodo} níveis: {string.Join("; ", p)}");
			foreach (Degrau d in r.Degraus.Where(d => d.Periodo == 0).OrderBy(d => d.Nivel))
				if (Partes(cat, d) is { Count: > 0 } p) t.PorNivel.Add($"nível {d.Nivel}: {string.Join("; ", p)}");
			t.NoTopo = NoTopo(r, max);
		}

		t.Resumo = Resumo(t, r, max);
		return t;
	}

	/// <summary>O que uma casa da escolha unica (ou de uma raca) da, numa frase.</summary>
	public static string TextoDaCasa(Escolha e)
	{
		var p = new List<string>();
		foreach ((string campo, double v) in e.Buffs) p.Add(Soma(campo, v));
		foreach ((string campo, double v) in e.Mults) p.Add(Fator(campo, v));
		foreach ((string stat, double v) in e.Genes) p.Add(Gene(stat, v));
		foreach ((string chave, double v) in e.Flags)
			if (NomesLegiveis.Chave(chave, v) is { } frase) p.Add(frase);
		if (e.Verbos.Length > 0) p.Add(Habilidades(e.Verbos));
		return p.Count > 0 ? string.Join(", ", p) : "sem efeito portado ainda";
	}

	// =====================================================================
	// AS LINHAS
	// =====================================================================
	private static string Marca(string campo) =>
		EfeitosDeSkill.CampoExiste(campo) ? "" : $" ({CampoQueOPortNaoTem})";

	private static string Soma(string campo, double v) =>
		$"{NomesLegiveis.Campo(campo)} {NomesLegiveis.ComSinal(v)}{Marca(campo)}";

	private static string Fator(string campo, double v) =>
		$"{NomesLegiveis.Campo(campo)} ×{NomesLegiveis.Numero(v)}{Marca(campo)}";

	private static string Gene(string stat, double v) =>
		$"gene de {NomesLegiveis.Gene(stat)} {NomesLegiveis.ComSinal(v)}"
		+ (EfeitosDeSkill.GeneExiste(stat) ? "" : $" ({CampoQueOPortNaoTem})");

	/// <summary>`BP += (max(1,BP*0.01))` -> "na compra: BP + max(1, BP×0,01)".</summary>
	private static string Ganho(GanhoNaCompra g)
	{
		string e = g.Expressao;
		// o extrator embrulha a expressao inteira em parenteses; um par que fecha so no fim e ruido
		while (e.Length > 2 && e[0] == '(' && e[^1] == ')' && FechaSoNoFim(e)) e = e[1..^1];
		return $"na compra: {NomesLegiveis.Campo(g.Campo)} {(g.Sinal > 0 ? "+" : "−")} "
			 + e.Replace("*", "×").Replace(",", ", ").Replace('.', ',');
	}

	private static bool FechaSoNoFim(string e)
	{
		int nivel = 0;
		for (int i = 0; i < e.Length; i++)
		{
			if (e[i] == '(') nivel++;
			else if (e[i] == ')' && --nivel == 0 && i < e.Length - 1) return false;
		}
		return nivel == 0;
	}

	/// <summary>
	/// "habilidade nova: Kiai" / "habilidades novas: Study Other, Focus Skill". O nome vem da tabela de
	/// tecnicas quando ela tem a tecnica; a que nao esta portada sai dizendo isso, como o botao dela.
	/// </summary>
	private static string Habilidades(string[] verbos)
	{
		var nomes = verbos.Select(v =>
			Tecnicas.Get(v) is { Modo: not Modo.NaoPortada } t
				? t.Nome
				: $"{Tecnicas.Get(v)?.Nome ?? NomesLegiveis.Habilidade(v)} (efeito ainda não portado)").ToList();
		return (nomes.Count == 1 ? "habilidade nova: " : "habilidades novas: ") + string.Join(", ", nomes);
	}

	/// <summary>Tudo que um degrau da, em pedacos. Vazio = o degrau so avisa (ou so troca a barreira).</summary>
	private static List<string> Partes(SkillCatalog cat, Degrau d)
	{
		var p = new List<string>();
		foreach ((string campo, double v) in d.Buffs) p.Add(Soma(campo, v));
		foreach ((string campo, double v) in d.Mults) p.Add(Fator(campo, v));
		foreach ((string stat, double v) in d.Genes) p.Add(Gene(stat, v));
		foreach ((string chave, double v) in d.Flags)
			if (NomesLegiveis.Chave(chave, v) is { } frase) p.Add(frase);
		if (d.Verbos.Length > 0) p.Add(Habilidades(d.Verbos));
		if (d.VerbosPorCasa.Length > 0)
			p.Add("conforme a casa escolhida: " + string.Join(", ",
				d.VerbosPorCasa.Select(pc => $"{pc.Casa} → {Tecnicas.Get(pc.Verbo)?.Nome ?? NomesLegiveis.Habilidade(pc.Verbo)}")));
		foreach (string alvo in d.Destrava) p.Add($"libera a compra de {cat.Get(alvo)?.Nome ?? alvo}");
		foreach ((string path, int nivel) in d.Concede)
			p.Add($"concede a skill {cat.Get(path)?.Nome ?? path}{(nivel > 0 ? $" (já no nível {nivel})" : "")}");
		return p;
	}

	// =====================================================================
	// COMO ELA SOBE
	// =====================================================================
	/// <summary>
	/// "Sobe até o nível 100; o 1º nível pede 5.000 de experiência e cada um seguinte custa 3% a mais.
	/// Ganha experiência: estudando alguém (×2); meditando antes do nível 10 (×2), senão (×1)."
	///
	/// AS CORRENTES `if / else if / else` SAEM COMO ALTERNATIVAS ("..., senão ..."), porque e o que
	/// elas sao: vale o primeiro ramo que valer, e so ele (ver <see cref="RegraDeNivel.GanhoPorEstado.Cadeia"/>).
	/// Escreve-las como fontes independentes diria ao jogador que meditar E o `senão` rendem juntos.
	/// </summary>
	private static string Progressao(RegraDeNivel r, int max)
	{
		var fontes = new List<string>();
		if (r.GanhoPorTempo) fontes.Add("com o tempo, só de existir");
		foreach (RegraDeNivel.GanhoPorEstado g in r.PorEstado.Where(g => g.Cadeia == 0)) fontes.Add(Fonte(g));
		foreach (int cadeia in r.PorEstado.Where(g => g.Cadeia != 0).Select(g => g.Cadeia).Distinct())
			fontes.Add(string.Join(", ", r.PorEstado.Where(g => g.Cadeia == cadeia).Select(Fonte)));
		foreach (RegraDeNivel.GanhoPorContador c in r.PorContador)
			fontes.Add($"a cada {NomesLegiveis.Contador(c.Contador)} (×{NomesLegiveis.Numero(c.Quanto)})");

		string custo = $"o 1º nível pede {NomesLegiveis.Inteiro(r.BarreiraEm(0))} de experiência"
					 + (r.Crescimento > 1
						 ? $" e cada um seguinte custa {NomesLegiveis.Numero((r.Crescimento - 1) * 100)}% a mais"
						 : "");
		string sobe = $"Sobe até o nível {max}; {custo}.";
		return fontes.Count == 0
			? sobe + " Neste port ainda não há fonte de experiência pra ela."
			: sobe + " Ganha experiência: " + string.Join("; ", fontes) + ".";
	}

	private static string Fonte(RegraDeNivel.GanhoPorEstado g)
	{
		string peso = $"(×{NomesLegiveis.Numero(g.Quanto)})";
		if (g.Quando == RegraDeNivel.Estado.Senao) return $"senão {peso}";
		string quando = NomesLegiveis.Estado(g.Quando);
		if (g.NivelMenorQue > 0) quando += $" antes do nível {g.NivelMenorQue}";
		if (g.NivelMinimo > 0) quando += $" do nível {g.NivelMinimo} em diante";
		return $"{quando} {peso}";
	}

	// =====================================================================
	// A SOMA NO TOPO
	// =====================================================================
	/// <summary>
	/// O que os degraus somam ate o nivel maximo, campo a campo -- so quando algum campo e tocado mais
	/// de uma vez (um periodico, ou dois marcos no mesmo campo). E a conta que o jogador nao faz de
	/// cabeca: "percepção de Ki +1 a cada 5" e "+20 no nível 100" sao a mesma frase, e so a segunda
	/// responde "vale a pena?".
	/// </summary>
	private static string NoTopo(RegraDeNivel r, int max)
	{
		var soma = new Dictionary<string, double>(StringComparer.Ordinal);
		var toques = new Dictionary<string, int>(StringComparer.Ordinal);
		var genes = new Dictionary<string, double>(StringComparer.Ordinal);
		var fator = new Dictionary<string, double>(StringComparer.Ordinal);
		// NA MESMA ORDEM DAS LINHAS POR NIVEL (periodicos primeiro): o campo do ganho constante
		// ("percepção de Ki +20") abre a soma, e nao o do marco isolado
		foreach (Degrau d in r.Degraus.OrderBy(d => d.Periodo > 0 ? 0 : 1).ThenBy(d => d.Periodo > 0 ? d.Periodo : d.Nivel))
		{
			int vezes = RegraDeNivel.Vezes(d, max);
			if (vezes <= 0) continue;
			foreach ((string campo, double v) in d.Buffs)
			{
				soma[campo] = soma.GetValueOrDefault(campo) + v * vezes;
				toques[campo] = toques.GetValueOrDefault(campo) + vezes;
			}
			foreach ((string stat, double v) in d.Genes)
			{
				genes[stat] = genes.GetValueOrDefault(stat) + v * vezes;
				toques["gene:" + stat] = toques.GetValueOrDefault("gene:" + stat) + vezes;
			}
			foreach ((string campo, double v) in d.Mults)
			{
				double f = fator.GetValueOrDefault(campo, 1);
				for (int i = 0; i < vezes; i++) f *= v;
				fator[campo] = f;
				toques["mult:" + campo] = toques.GetValueOrDefault("mult:" + campo) + vezes;
			}
		}
		if (toques.Count == 0 || toques.Values.Max() < 2) return "";

		var partes = new List<string>();
		foreach ((string campo, double v) in soma) partes.Add(Soma(campo, v));
		foreach ((string stat, double v) in genes) partes.Add(Gene(stat, v));
		foreach ((string campo, double v) in fator) partes.Add(Fator(campo, v));
		return $"no nível {max}, somando tudo: {string.Join(", ", partes)}";
	}

	// =====================================================================
	// A LINHA UNICA
	// =====================================================================
	private static string Resumo(Texto t, RegraDeNivel? r, int max)
	{
		var partes = t.NaCompra.Where(l => !l.StartsWith("    ", StringComparison.Ordinal)).ToList();
		if (r != null && t.PorNivel.Count > 0)
		{
			// a soma no topo quando ha, senao os primeiros degraus; e as habilidades que o nivel da
			partes.Add(t.NoTopo.Length > 0 ? t.NoTopo : $"até o nível {max}: {string.Join("; ", t.PorNivel.Take(2))}");
			var verbos = r.Degraus.SelectMany(d => d.Verbos).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (verbos.Length > 0) partes.Add("por nível, " + Habilidades(verbos));
		}
		return string.Join(", ", partes);
	}
}
