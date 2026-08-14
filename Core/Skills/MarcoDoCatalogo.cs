namespace Jandirus.Core.Skills;

/// <summary>
/// O CATALOGO COMO ELE ERA -- a linha d'agua contra a qual toda re-extracao e medida.
///
/// ============================ POR QUE UM ARQUIVO CONGELADO, E NAO UMA CONTA ============================
/// O extrator e mexido com frequencia: uma guarda de comentario que nao funcionava, um `after_learn`
/// declarado de outra forma, um `proc/choose` que ninguem lia. Cada conserto desses **reescreve o
/// `skills.json` inteiro**, e a unica coisa que se olha depois e o numero que melhorou ("116 skills a
/// mais", "mais uma deixou de ser muda"). O que ninguem olha e o outro lado: *alguma skill que ja
/// funcionava parou de funcionar?*
///
/// Esse defeito nao aparece em teste nenhum. Uma skill que perdeu o `buffs` continua no catalogo,
/// continua comprada, continua no menu -- ela so nao faz mais nada. E como o extrator reescreve o
/// arquivo todo, o `git diff` de uma re-extracao tem centenas de linhas e ninguem le.
///
/// Entao a comparacao precisa de um ANTES que nao seja calculado do agora. Este arquivo e esse
/// antes: `Assets/Data/catalogo-marco.json`, tirado do `git HEAD` -- o catalogo como estava antes
/// desta frente comecar. Ele **nao e regenerado por rotina**: um marco que se atualiza sozinho
/// concorda com qualquer defeito no dia seguinte, que e exatamente o modo de falha
/// "a checagem le a constante que devia defender" da armadilha 7 da casa.
///
/// **NAO HA COMANDO PRA REGERA-LO, e a ausencia e a decisao.** Um `dotnet run -- marco` seria usado
/// no primeiro dia em que a bancada ficasse vermelha, e a perda sumiria com um commit. Quando um dia
/// o marco precisar mesmo andar (porque o catalogo mudou de FORMA, e nao de conteudo), a receita e
/// esta, escrita a mao e de proposito: `git show &lt;commit&gt;:Assets/Data/skills.json` e
/// `:Assets/Data/niveis.json`, uma linha `{"tipo":"skill",...}` por skill com a CONTAGEM de cada
/// canal, uma `{"tipo":"degrau",...}` por skill que concede verbo por nivel, e a primeira linha
/// dizendo de que commit veio.
/// ==================================================================================================
///
/// ============================ O QUE E PERDA E O QUE NAO E ============================
/// Skill NOVA nao e perda (o extrator melhorou). Efeito que CRESCEU nao e perda. Perda e:
///   * uma skill que existia e sumiu do catalogo;
///   * uma skill que somava/multiplicava/destravava e passou a nao somar/multiplicar/destravar;
///   * um verbo que uma skill (ou um degrau) concedia e nao concede mais.
///
/// E perda EXPLICADA continua sendo perda -- ela so tem dono. Ver <see cref="PerdasExplicadas"/>.
/// ==================================================================================
/// </summary>
public static class MarcoDoCatalogo
{
	/// <summary>Uma linha do marco: o que a skill fazia, em contagem, e os verbos que ela dava.</summary>
	public sealed class Linha
	{
		public string Path = "";
		public int Buffs, Mults, Genes, Flags, Escolhas;
		public string Estilo = "";
		public string[] Verbos = [];

		/// <summary>Ela fazia ALGUMA coisa? E a mesma pergunta do `Skill.SemEfeito`, do lado de la.</summary>
		public bool Fazia => Buffs > 0 || Mults > 0 || Genes > 0 || Flags > 0
						  || Escolhas > 0 || Estilo.Length > 0 || Verbos.Length > 0;
	}

	public sealed class Marco
	{
		public string Origem = "";
		public List<Linha> Skills = [];

		/// <summary>
		/// path -> verbos que os DEGRAUS daquela skill concediam (`niveis.json`).
		///
		/// ENTRAM TAMBEM OS DE LISTA VAZIA, e nao e desperdicio: uma regra de nivel que nao concede
		/// verbo nenhum ainda concede BUFF por degrau, e ela sumir do `niveis.json` e uma perda que
		/// nenhuma outra comparacao veria. Foi assim que a arvore `Focused` -- inteira dentro de um
		/// `/* */` no DM -- apareceu.
		/// </summary>
		public Dictionary<string, string[]> VerbosDeDegrau = new(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// AS PERDAS QUE TEM DONO -- e cada uma diz por que perder foi o certo.
	///
	/// Esta tabela e o motivo de a checagem poder ficar verde sem virar mentira: sem ela, a unica
	/// saida diante de uma perda legitima seria afrouxar a regra (ou regravar o marco), e as duas
	/// coisas apagam o defeito junto com a explicacao.
	/// </summary>
	public static readonly Dictionary<string, string> PerdasExplicadas = new(StringComparer.OrdinalIgnoreCase)
	{
		// A arvore inteira mora dentro de um `/* */` no DM (`Rudimentary Effusion.dm`), e o extrator
		// a tratava como viva porque a guarda de comentario testava "a linha contem `*/`?" -- e em
		// `/*/datum/skill/tree/Focused` ela achava o `*/` da PROPRIA abertura. O conserto da guarda
		// tirou 1 degrau dos 134. Perder codigo morto e ganho, mas ele passa aqui de qualquer jeito.
		["/datum/skill/tree/Focused"] =
			"arvore inteiramente comentada no DM; sumiu quando a guarda de /* */ passou a funcionar",
	};

	/// <summary>
	/// O LADO "DEPOIS" DOS DEGRAUS: le o `niveis.json` DE HOJE e devolve path -> verbos.
	///
	/// ============================ POR QUE DO JSON, E NAO DO `RegrasDeNivel` ============================
	/// A primeira versao desta familia comparava o marco com o mapa que o servidor tem CARREGADO, e
	/// acusou 33 perdas -- todas falsas. O motivo esta escrito no proprio carregador: *"skill sem
	/// degrau nenhum nao vira regra"* (`RegrasDoDisco`), e o `niveis.json` tem 133 skills contra 101
	/// que concedem degrau. As 33 nao foram perdidas na extracao: elas nunca foram carregadas, de
	/// propria decisao, e o log do boot ja as conta.
	///
	/// A pergunta desta familia e sobre a EXTRACAO ("a re-extracao quebrou alguma coisa?"), entao os
	/// dois lados tem que ser extracao: json de ontem contra json de hoje. Comparar arquivo com
	/// memoria mede o carregador junto, e ai a bancada fica vermelha por um motivo que nao e o dela --
	/// que e como esta linha nasceu.
	/// ==============================================================================================
	/// </summary>
	public static Dictionary<string, string[]> LerNiveisDeHoje(string json)
	{
		var m = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		foreach (string b in SkillCatalog.Blocos(json))
		{
			string path = SkillCatalog.Str(b, "path");
			if (path.Length == 0) continue;

			string[] verbos = SkillCatalog.Lista(b, "verbos");
			if (!m.TryGetValue(path, out string[]? antes)) { m[path] = verbos; continue; }
			if (verbos.Length > 0) m[path] = [.. antes.Union(verbos, StringComparer.OrdinalIgnoreCase)];
		}
		return m;
	}

	/// <summary>Le o marco do texto do json. Formato plano, mesmo leitor do <see cref="SkillCatalog"/>.</summary>
	public static Marco Ler(string json)
	{
		var m = new Marco();
		foreach (string b in SkillCatalog.Blocos(json))
		{
			switch (SkillCatalog.Str(b, "tipo"))
			{
				case "origem":
					m.Origem = SkillCatalog.Str(b, "nota");
					break;

				case "skill":
					m.Skills.Add(new Linha
					{
						Path = SkillCatalog.Str(b, "path"),
						Buffs = SkillCatalog.Num(b, "buffs", 0),
						Mults = SkillCatalog.Num(b, "mults", 0),
						Genes = SkillCatalog.Num(b, "genes", 0),
						Flags = SkillCatalog.Num(b, "flags", 0),
						Escolhas = SkillCatalog.Num(b, "escolhas", 0),
						Estilo = SkillCatalog.Str(b, "estilo"),
						Verbos = SkillCatalog.Lista(b, "verbos"),
					});
					break;

				case "degrau":
					m.VerbosDeDegrau[SkillCatalog.Str(b, "path")] = SkillCatalog.Lista(b, "verbos");
					break;
			}
		}
		return m;
	}

	/// <summary>O resultado da comparacao. Tudo em lista: numero sozinho nao diz o que consertar.</summary>
	public sealed class Diferenca
	{
		/// <summary>Estava no marco e nao esta mais no catalogo.</summary>
		public List<string> Sumiram = [];

		/// <summary>Fazia alguma coisa e nao faz mais -- a perda que importa.</summary>
		public List<string> Emudeceram = [];

		/// <summary>Concedia um verbo e nao concede mais (skill OU degrau).</summary>
		public List<string> VerbosPerdidos = [];

		/// <summary>Perdas que estao em <see cref="PerdasExplicadas"/> -- contadas e nao acusadas.</summary>
		public List<string> Explicadas = [];

		/// <summary>Nasceu depois do marco. Nao e defeito; e o tamanho do ganho.</summary>
		public List<string> Nasceram = [];

		/// <summary>Mudou de efeito sem perder nada (cresceu). Tambem nao e defeito.</summary>
		public List<string> Cresceram = [];

		/// <summary>Zero perda NAO explicada.</summary>
		public bool Intacto => Sumiram.Count == 0 && Emudeceram.Count == 0 && VerbosPerdidos.Count == 0;
	}

	/// <summary>
	/// COMPARA O MARCO COM O CATALOGO DE AGORA.
	///
	/// <paramref name="verbosDeDegrauDeAgora"/> e o mapa path -> verbos do `niveis.json` DE HOJE (ver
	/// <see cref="LerNiveisDeHoje"/>). Ele entra porque DOIS dos verbos de tiro do jogo sao concedidos
	/// por degrau e nao por skill -- comparar so o `skills.json` deixaria justamente esses dois fora
	/// do radar, que e o buraco que o proprio censo ja levou uma vez.
	/// </summary>
	public static Diferenca Comparar(
		Marco marco, SkillCatalog agora, IReadOnlyDictionary<string, string[]> verbosDeDegrauDeAgora)
	{
		var d = new Diferenca();
		var noMarco = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (Linha l in marco.Skills)
		{
			noMarco.Add(l.Path);
			Skill? s = agora.Get(l.Path);

			if (s == null)
			{
				(PerdasExplicadas.ContainsKey(l.Path) ? d.Explicadas : d.Sumiram)
					.Add($"{l.Path}{Motivo(l.Path)}");
				continue;
			}

			// AS ARVORES FICAM DE FORA DESTA PERGUNTA. `Skill.SemEfeito` responde `false` pra toda
			// arvore por construcao (o `!Arvore` curto-circuita), entao as 47 apareceriam como
			// "cresceram" em toda rodada -- 47 linhas de ruido que fariam ninguem ler o relatorio.
			// O que uma arvore tem a perder (o galho, o degrau) e conferido pelos dois outros
			// caminhos: ela some do catalogo, ou some das regras de nivel.
			if (s.Arvore) continue;

			bool fazAgora = !s.SemEfeito;
			if (l.Fazia && !fazAgora)
				(PerdasExplicadas.ContainsKey(l.Path) ? d.Explicadas : d.Emudeceram)
					.Add($"{l.Path}{Motivo(l.Path)}");
			else if (!l.Fazia && fazAgora)
				d.Cresceram.Add(l.Path);

			foreach (string v in l.Verbos)
				if (!s.Verbos.Contains(v, StringComparer.OrdinalIgnoreCase))
					(PerdasExplicadas.ContainsKey(l.Path) ? d.Explicadas : d.VerbosPerdidos)
						.Add($"{l.Path} nao concede mais '{v}'{Motivo(l.Path)}");
		}

		foreach (Skill s in agora.Todas)
			if (!noMarco.Contains(s.Path)) d.Nasceram.Add(s.Path);

		// OS DEGRAUS, pela mesma regra e no mesmo relatorio. Duas perdas possiveis: a REGRA inteira
		// sumiu (a skill nao sobe mais de nivel) ou um verbo dela sumiu.
		foreach ((string path, string[] verbos) in marco.VerbosDeDegrau)
		{
			if (!verbosDeDegrauDeAgora.TryGetValue(path, out string[]? hoje))
			{
				(PerdasExplicadas.ContainsKey(path) ? d.Explicadas : d.Sumiram)
					.Add($"as regras de nivel de {path} sumiram{Motivo(path)}");
				continue;
			}

			foreach (string v in verbos)
				if (!hoje.Contains(v, StringComparer.OrdinalIgnoreCase))
					(PerdasExplicadas.ContainsKey(path) ? d.Explicadas : d.VerbosPerdidos)
						.Add($"o degrau de {path} nao concede mais '{v}'{Motivo(path)}");
		}

		foreach (List<string> l in new[] { d.Sumiram, d.Emudeceram, d.VerbosPerdidos, d.Explicadas,
										   d.Nasceram, d.Cresceram })
			l.Sort(StringComparer.OrdinalIgnoreCase);
		return d;
	}

	private static string Motivo(string path) =>
		PerdasExplicadas.TryGetValue(path, out string? m) ? $"  ({m})" : "";
}
