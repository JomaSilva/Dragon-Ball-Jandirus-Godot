using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA OTHER -- os verbs da categoria "Other" do original, agrupados por TEMA em cartoes.
///
/// ============================ POR QUE AGRUPAR, E POR QUE POR TABELA ============================
/// O original despejava os 91 verbs de "Other" numa coluna alfabetica (`ui_tab_verbs`,
/// HtmlUI.dm:578): "Accept Challenge" ao lado de "Accept Elder Seat", que nada tem a ver um com o
/// outro, e o "Bank: Balance" a tres telas de rolagem do "Fund Earth". Alfabetico e a ordem de quem
/// JA SABE o nome; quem abre a aba pra descobrir o que da pra fazer precisa da ordem do ASSUNTO --
/// e foi isso que o dono chamou de "cru".
///
/// A TABELA E POR NOME, e nao um campo novo no `Verbo`: o record e compartilhado com as outras abas,
/// com a busca e com as teclas, e quem registra um verb (`VerbosDoJogo`, `Habilidades`) nao deveria
/// precisar saber como a aba Other se desenha. O preco e que verb novo cai em "Diversos" ate alguem o
/// pendurar num tema -- e "Diversos" e VISIVEL na tela, que e o jeito de essa divida nao envelhecer
/// calada.
///
/// ============================ O CONTRATO COM AS BANCADAS NAO MUDA ============================
/// Cada verb continua sendo um `Button` com `Text = v.Nome`, o mesmo `Disabled` e o mesmo `Acionar`
/// (ver <see cref="BotaoDe"/>): a `--diagtecla` acha "Toggle Knockback" e "Who" pelo texto e aperta
/// o sinal do botao, e a `--diagskills` confere que "Convidar aluno" NAO esta aqui. O que mudou e
/// so ONDE o botao mora (dentro de um cartao, ao lado da frase do que ele faz).
/// ==========================================================================================
/// </summary>
public partial class MenuJogo
{
	// =====================================================================
	// A TABELA DE TEMAS DA ABA OTHER
	// =====================================================================
	/// <summary>O tema de quem nao esta na tabela. Sempre o ULTIMO cartao, e sempre visivel.</summary>
	private const string Diversos = "Diversos";

	/// <summary>
	/// A ORDEM DOS CARTOES, fixa: primeiro o que se faz todo dia (treinar, lutar, conviver), depois
	/// as portas dos cargos, por ultimo o que sobrou. Nao e alfabetica de proposito -- alfabetico
	/// poria "Cópias" antes de "Treino".
	/// </summary>
	private static readonly string[] OrdemDosGruposDeOutros =
	[
		"Treino e estudo", "Combate", "Sociedade", "Namek", "Ofertas recebidas", "Cópias (Split Form)",
		"Genkidama", "Trono de Vegeta", "Deus da Destruição", "Sala do Tempo", Diversos,
	];

	/// <summary>
	/// VERB -> TEMA, pelo nome. Os nomes sao os literais de `VerbosDoJogo.Registrar`.
	///
	/// "Accept Youth" e "Accept Potential" NAO estao em "Namek" de proposito: a juventude vem do Grand
	/// Kai e do Demon Lord (`OtherworldRankSkills.dm:170`), e o potencial de quem tem a skill -- so o
	/// assento de Anciao e coisa de Namek. Um cartao "Namek" com "Accept Youth" dentro mentiria na tela.
	/// Os quatro sao RESPOSTAS a uma oferta que alguem fez, e e isso que o cartao deles diz.
	/// </summary>
	private static readonly Dictionary<string, string> GrupoDeOutrosPorNome = new(StringComparer.OrdinalIgnoreCase)
	{
		["Training Session"] = "Treino e estudo",
		["View Session"] = "Treino e estudo",
		["Study"] = "Treino e estudo",
		["Bolt"] = "Treino e estudo",
		["Tech Catalog"] = "Treino e estudo",

		["Toggle Knockback"] = "Combate",
		["Toggle SSJ Grades"] = "Combate",
		["Clear Buffs"] = "Combate",

		["Who"] = "Sociedade",
		["Ranks"] = "Sociedade",
		["Karma"] = "Sociedade",
		["Rank Duty"] = "Sociedade",
		["Fund Earth"] = "Sociedade",
		["Remember Person"] = "Sociedade",
		["Request Friendship"] = "Sociedade",
		["Declare Rival"] = "Sociedade",
		["Known People"] = "Sociedade",

		["Appoint Elder"] = "Namek",
		["Accept Elder Seat"] = "Namek",
		["Decline Elder Seat"] = "Namek",

		["Accept Youth"] = "Ofertas recebidas",
		["Decline Youth"] = "Ofertas recebidas",
		["Accept Potential"] = "Ofertas recebidas",
		["Decline Potential"] = "Ofertas recebidas",

		["Give Power to Spirit Bomb"] = "Genkidama",
		["Refuse Spirit Bomb"] = "Genkidama",

		["Name Heir"] = "Trono de Vegeta",
		["Remove Heir"] = "Trono de Vegeta",
		["Line of Succession"] = "Trono de Vegeta",

		["Challenge God of Destruction"] = "Deus da Destruição",
		["Accept Challenge"] = "Deus da Destruição",
		["Postpone Challenge"] = "Deus da Destruição",
		["Title Status"] = "Deus da Destruição",
	};

	/// <summary>
	/// AS FAMILIAS COM PREFIXO ("Bank: Balance", "Split Form: Follow", "Time Chamber: Authorize"): o
	/// prefixo JA E o tema, e uma linha por membro aqui seria a lista que envelhece quando a familia
	/// ganha um verb novo.
	/// </summary>
	private static readonly (string Prefixo, string Grupo)[] GrupoDeOutrosPorPrefixo =
	[
		("Bank:", "Sociedade"),
		("Split Form:", "Cópias (Split Form)"),
		("Time Chamber:", "Sala do Tempo"),
	];

	/// <summary>
	/// SO PRA BANCADA: finge que a tabela nao existe, e todo verb cai em "Diversos". E a injecao da
	/// `--diagabas` F13 -- ela prova que a regra "ha 3+ cartoes por tema" fica VERMELHA sem a tabela,
	/// e que nenhum verb se perde no caminho (o fallback ainda desenha todos).
	/// </summary>
	internal static bool SemTabelaDeGruposDeTeste;

	/// <summary>SO PRA BANCADA: a ordem fixa dos cartoes, pra cobrar que a tela a respeita.</summary>
	internal static IReadOnlyList<string> OrdemDosGruposDeOutrosDeTeste => OrdemDosGruposDeOutros;

	private static string GrupoDeOutros(Verbo v)
	{
		if (SemTabelaDeGruposDeTeste) return Diversos;
		if (GrupoDeOutrosPorNome.TryGetValue(v.Nome, out string? grupo)) return grupo;
		foreach ((string prefixo, string g) in GrupoDeOutrosPorPrefixo)
			if (v.Nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) return g;
		return Diversos;
	}

	// =====================================================================
	// A ABA
	// =====================================================================
	/// <summary>A aba Other: os cartoes por tema da categoria. E daqui que a aba se redesenha.</summary>
	private void AbaOutros() => ListaDeVerbos(Verbos.Outros);

	/// <summary>
	/// OS VERBS DE UMA CATEGORIA, EM CARTOES POR TEMA. A Admin usa o mesmo desenho pros verbs dela
	/// (com a tabela de la, ver `GrupoDeAdmin` em MenuJogo.Admin.cs); uma categoria sem tabela vira um
	/// cartao so, com o nome dela.
	///
	/// Dentro do cartao os verbs ficam em DUAS colunas quando sao dois ou mais -- e a grade dos verbs de
	/// aprendizado da Learning: empilhados em largura total, 46 botoes com a frase embaixo empurrariam
	/// a metade util da aba pra fora da tela. Um verb sozinho ocupa a largura toda: coluna vazia ao lado
	/// de um botao so e buraco, nao grade.
	/// </summary>
	private void ListaDeVerbos(string categoria)
	{
		var lista = Verbos.Da(categoria).ToList();
		if (lista.Count == 0)
		{
			// a mesma frase do original quando a categoria estava vazia ("No actions here.")
			Nota("Nenhuma acao aqui ainda.", Cartao(categoria));
			return;
		}

		foreach ((string grupo, List<Verbo> verbos) in GruposDeVerbos(lista, categoria))
		{
			VBoxContainer corpo = Cartao(grupo);
			// O TEMA VAI EM METADADO no painel do cartao: e por ele que a bancada conta "cartoes por
			// tema" sem confundi-los com os outros cartoes da aba Admin (aviso, clima, contas...).
			corpo.GetParent()?.SetMeta("grupo", grupo);
			Control pai = verbos.Count >= 2 ? Colunas(corpo) : corpo;
			foreach (Verbo v in verbos) pai.AddChild(BotaoComDescricao(BotaoDe(v), v.Descricao));
		}
	}

	/// <summary>
	/// Reparte uma lista de verbs (ja em ordem alfabetica, que e a de `Verbos.Da`) nos temas da
	/// categoria, devolvendo os temas NA ORDEM FIXA da tabela e so os que tem alguem dentro. Quem cai
	/// fora da tabela vai pro ultimo tema ("Diversos"). Deterministico: mesma lista, mesma saida.
	/// </summary>
	private static IEnumerable<(string Grupo, List<Verbo> Verbos)> GruposDeVerbos(List<Verbo> lista, string categoria)
	{
		(string[] ordem, Func<Verbo, string> grupoDe) = categoria switch
		{
			Verbos.Outros => (OrdemDosGruposDeOutros, (Func<Verbo, string>)GrupoDeOutros),
			Verbos.Admin => (OrdemDosGruposDeAdmin, (Func<Verbo, string>)GrupoDeAdmin),
			_ => (new[] { categoria }, (Func<Verbo, string>)(_ => categoria)),
		};

		var porGrupo = new Dictionary<string, List<Verbo>>(StringComparer.Ordinal);
		foreach (Verbo v in lista)
		{
			string g = grupoDe(v);
			if (Array.IndexOf(ordem, g) < 0) g = ordem[^1];   // tema que a tabela cita mas a ordem nao lista: cai no ultimo
			if (!porGrupo.TryGetValue(g, out List<Verbo>? dentro)) porGrupo[g] = dentro = [];
			dentro.Add(v);
		}
		foreach (string g in ordem)
			if (porGrupo.TryGetValue(g, out List<Verbo>? dentro)) yield return (g, dentro);
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`). A basica tem so o
	/// `comum` (a lista de verbs e estatica e muda por `Verbos.Mudou`, que ja redesenha). O que ESTA
	/// aba desenha alem disso e o `Disabled` de cada botao, que vem de `PodeAgora` -- "Remember
	/// Person" acende quando se marca alguem. Um caractere por verb, nos mesmos valores em que o
	/// botao e pintado: sem isto o botao ficaria apagado ate outra coisa qualquer remontar a pagina.
	/// </summary>
	// O NOME ENTRA JUNTO DO SINAL: desde que os verbs de cargo e de raca so APARECEM pra quem os tem
	// (`Verbo.Mostrar`), a LISTA muda quando um cargo chega -- e uma lista de outro tamanho, ou de
	// outros nomes com o mesmo tamanho, tem que remontar a pagina. So o sinal nao distinguiria
	// "Fund Earth entrou" de "Study saiu".
	private string ExtraDaAssinaturaDeOutros(SheetState f) =>
		string.Join("|", Verbos.Da(Verbos.Outros).Select(v => v.Nome + (v.PodeAgora ? '+' : '-')));
}
