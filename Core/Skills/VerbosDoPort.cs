namespace Jandirus.Core.Skills;

/// <summary>
/// OS VERBS QUE O PORT DA A UMA SKILL QUE, NO DM, AGIA POR OUTRO SISTEMA.
///
/// O catalogo (`skills.json`) traz o verb que o `after_learn()` de cada skill concede com `assignverb`. Ha skill
/// que nao concede verb nenhum e mesmo assim FAZ alguma coisa no original, por um sistema inteiro que este port nao
/// tem -- a Materialize ensina um RITUAL (`known_ritual_ma_types`, `Magic_Tree.dm:252-258`), e ritual pede circulo,
/// palavra, ingrediente e energia magica. Quando o port da a uma skill dessas um corpo proprio, o verb entra AQUI,
/// escrito por cima do catalogo no fim do `SkillCatalog.Parse` (como os `AcendedoresDoPort`): o servidor, o cliente
/// e a ferramenta leem o mesmo catalogo, entao o `SabeTecnica`, o botao do menu, a ficha e o aviso de compra passam
/// a enxerga-lo sem mais nenhuma linha.
///
/// O `skills.json` NAO MUDA: ele continua sendo o que o extrator le do DM, e a diferenca fica nesta tabela, datada
/// e com o motivo ao lado.
/// </summary>
public static class VerbosDoPort
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o catalogo e lido sem os verbs do port -- a Materialize volta a so
	/// somar pericia esoterica. A v0.2.0. Falso em jogo, sempre.
	/// </summary>
	public static bool SemCorrecaoDeTeste;

	public static readonly (string Skill, string Verbo)[] Todos =
	[
		// (2026-10-13) O dono: *"tem skills q n estao funcionando como materialize etc"*. Ver `Items.Materializacao`.
		(Items.Materializacao.Skill, Items.Materializacao.Verbo),
	];

	/// <summary>Escreve os verbs do port nas skills do catalogo recem-lido. Chamado no fim do `SkillCatalog.Parse`.</summary>
	public static void Aplicar(SkillCatalog cat)
	{
		if (SemCorrecaoDeTeste) return;
		foreach ((string skill, string verbo) in Todos)
			if (cat.Get(skill) is { } s && !s.Verbos.Contains(verbo, StringComparer.OrdinalIgnoreCase))
				s.Verbos = [.. s.Verbos, verbo];
	}
}
