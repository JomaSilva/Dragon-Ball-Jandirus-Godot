namespace Jandirus.Core.Skills;

/// <summary>Por que a licao nao rolou. <see cref="Pode"/> = rolou.</summary>
public enum RecusaDeLicao
{
	Pode = 0,
	NaoExiste,
	NaoSabe,          // o mestre nao tem a skill
	NaoSeEnsina,      // `teacher = FALSE` no tipo
	AprendeuDeAlguem, // `wastaught` -- **quem foi ensinado nao repassa**
	EleMesmo,
	SemAssinatura,
	AlunoJaSabe,      // o filtro de alvos do `Teach_Skill` (`teachable.dm:12`)
	Longe,
	Recarga,
	SemMarcos,
}

/// <summary>
/// O ENSINO DE SKILL -- porte de `Code/Modules/Skills/Skills Master/teachable.dm`.
///
/// ============================ NAO E O DISCIPULADO, E ELE NAO PEDE MESTRE ============================
/// Sao dois sistemas com o mesmo nome de rua, e no DM eles nao se tocam em linha nenhuma: o
/// `MasterStudent.dm` faz VINCULO (ganho de treino, despertar assistido, 5 alunos), e este arquivo
/// faz TRANSMISSAO DE SKILL entre dois corpos vizinhos quaisquer. O `Teach_Skill` nao consulta o
/// `mst_list`, nao pergunta quem e aluno de quem e nao paga recarga de mestre.
///
/// **Amarrar os dois seria inventar regra.** Quem ensina o Kamehameha ao estranho que chegou perto
/// nao vira mestre dele, e um mestre nao ganha o direito de despejar o arsenal no aluno. Fica
/// escrito pra ninguem "consertar" isso depois: a INDEPENDENCIA e a decisao.
/// ================================================================================================
///
/// ============================ O QUE O ALUNO PAGA: **NADA** ============================
/// Esta e a descoberta que muda o sistema, e ela esta escrita em duas linhas do original que
/// discordam do resumo que se costuma fazer dele:
///
///     if(skillpoints >= teachingcost)                                    // teachable.dm:47
///         input("... It costs [teachingcost] and you have [src.skillpoints].
///                **Learning won't remove Milestones, however.**")        // teachable.dm:48
///
/// E depois disso o caminho inteiro (`learnSkill` -> `S.learn`, `mobhandlers.dm:68` +
/// `skill.dm:34`) **nao encosta em `skillpoints`**. O custo e um LIMIAR: e a prova de que o aluno
/// ja andou o bastante pra merecer a licao, nao um preco. E o proprio jogo diz isso ao jogador, em
/// caixa alta, dentro da caixa de dialogo.
///
/// A consequencia de desenho e grande e vale ser dita: **ensinar e barato pra quem recebe e de
/// graca pra quem da**. O que limita o sistema nao e economia, sao as duas travas que sobram --
/// `teacher` (poucas skills se ensinam) e `wastaught` (quem recebeu nao repassa).
/// =====================================================================================
///
/// ============================ E O MESTRE NAO GANHA NADA ============================
/// Nem marco, nem experiencia, nem nivel: o `Study` so manda duas mensagens
/// (`teachable.dm:51-52`). A ausencia esta anotada porque um sistema de ensino sem recompensa
/// PARECE inacabado, e a proxima pessoa a ler vai querer completa-lo -- e nao e pra completar sem
/// o dono mandar.
/// =============================================================================
/// </summary>
public static class EnsinoDeSkill
{
	/// <summary>
	/// `sleep(60)` no fim do `Teach_Skill` (`teachable.dm:22`) -- e tique do BYOND e 1/10 s, entao
	/// sao **6 segundos**. (A conversao errada por 1/12 ja custou meses a este port.)
	///
	/// **E POR MESTRE, e comeca no GESTO** -- e as duas coisas sao do DM: o `sleep` prende o verb de
	/// quem ensinou (nao o de quem aprendeu), e o unico caminho que escapa dele e o `return` do
	/// "Cancel" (`:17`), ou seja o mestre que DESISTE nao paga. Quem oferece paga.
	/// </summary>
	public const double RecargaSegundos = 6;

	/// <summary>
	/// `view(1)` (`teachable.dm:12` e `:19`): ensino e coisa de quem esta encostado.
	///
	/// Um tile, e nao os 4 do convite de mestre: la o vinculo e uma conversa que sobrevive a um
	/// passo pra tras, aqui e a mao no ombro.
	/// </summary>
	public const int TilesDaLicao = 1;

	/// <summary>
	/// QUANTO TEMPO A OFERTA FICA DE PE, em segundos. NAO EXISTE NO DM pelo mesmo motivo que o
	/// <see cref="Discipulado.PrazoDoConviteSegundos"/> nao existe: la o `input()` CONGELA quem
	/// responde. Aqui a resposta e um verb, e pendente sem prazo vira caixa de entrada.
	///
	/// Bem mais curto que o do discipulado (60 s) de proposito: virar aluno de alguem e uma decisao
	/// de vida, aceitar uma licao de quem esta encostado em voce e um gesto de agora -- e a
	/// <see cref="RecargaSegundos"/> de quem ensina e de 6 s, entao uma oferta que durasse um minuto
	/// deixaria dez ofertas empilhadas na fila de uma pessoa so.
	/// </summary>
	public const double PrazoDaOfertaSegundos = 20;

	/// <summary>
	/// O CUSTO EM MARCOS -- que o aluno precisa TER e **nao gasta**. Ver o cabecalho da classe.
	/// A conta em si mora no <see cref="SkillCatalog.CustoDoEnsino"/>, junto do custo de compra.
	/// </summary>
	public static int Custo(Skill s) => SkillCatalog.CustoDoEnsino(s);

	/// <summary>
	/// ESTE LIVRO PODE REPASSAR ESTA SKILL? As tres condicoes do `Teach_Skill` (`:9-10`), e a
	/// terceira e a regra inteira do sistema:
	///
	///   1. ele **sabe** a skill;
	///   2. o tipo diz que ela se ensina (`teacher = TRUE`);
	///   3. ele **nao a aprendeu de alguem** (`teacher = FALSE` na copia ensinada, `:53`).
	///
	/// A (3) e o que impede a corrente: A ensina B, e B nao ensina C. Sem ela uma skill rara
	/// atravessa o servidor inteiro numa tarde, e o `teacher` do catalogo vira decoracao.
	/// </summary>
	public static bool PodeRepassar(SkillBook livro, Skill s) =>
		s.Ensinavel && livro.Sabe(s.Path) && !livro.FoiEnsinada(s.Path);

	/// <summary>
	/// TUDO O QUE A LICAO PERGUNTA, sem nada de servidor dentro -- e na ordem em que o original
	/// pergunta, que e a ordem que a mensagem de recusa precisa ter.
	///
	/// ============================ O QUE **NAO** ESTA AQUI, E E DE PROPOSITO ============================
	/// Arvore, raca, classe, pre-requisito, `enabled` e `villainonly` **nao sao consultados**. Nao e
	/// esquecimento: o `Study` (`teachable.dm:46`) pergunta
	///
	///     if(canLearnSkill(S) || S.teacher == TRUE)
	///
	/// e a skill que chega ali veio de uma lista filtrada por `teacher == TRUE` (`:9-10`) -- ou
	/// seja, **o segundo lado do `||` e sempre verdadeiro e todos aqueles portoes estao desligados
	/// no caminho do ensino**. E isso e o desenho, nao um descuido de la: o comentario do
	/// `RankChat` (`RankTree.dm:21`) manda em voz alta que *"todas as skills de cargo PRECISAM
	/// estar desabilitadas"* exatamente pra que a unica porta delas seja o ensino. Ligar esses
	/// portoes aqui apagaria as 83 skills ensinaveis do jogo de uma vez, em silencio.
	///
	/// **A unica porta de aprendizado que sobrevive e o `AlunoJaSabe`**, e ela vem do FILTRO DE
	/// ALVOS (`:12`, `!(Choice.type in M.learned_skills)`) e nao do `Study` -- que sozinho deixaria
	/// reaprender o que ja se sabe.
	/// ================================================================================================
	/// </summary>
	/// <param name="distanciaTiles">Negativo = nao conferir.</param>
	public static RecusaDeLicao Avaliar(SkillBook doMestre, SkillBook doAluno, Skill? s,
										bool mestreTemAssinatura, bool alunoTemAssinatura,
										bool mesmaPessoa, double distanciaTiles, bool naRecarga)
	{
		if (s == null || s.Arvore) return RecusaDeLicao.NaoExiste;
		if (!doMestre.Sabe(s.Path)) return RecusaDeLicao.NaoSabe;
		if (!s.Ensinavel) return RecusaDeLicao.NaoSeEnsina;
		if (doMestre.FoiEnsinada(s.Path)) return RecusaDeLicao.AprendeuDeAlguem;
		if (!mestreTemAssinatura || !alunoTemAssinatura) return RecusaDeLicao.SemAssinatura;
		if (mesmaPessoa) return RecusaDeLicao.EleMesmo;
		if (doAluno.Sabe(s.Path)) return RecusaDeLicao.AlunoJaSabe;
		if (distanciaTiles >= 0 && distanciaTiles > TilesDaLicao) return RecusaDeLicao.Longe;
		if (naRecarga) return RecusaDeLicao.Recarga;

		// O LIMIAR, E SO O LIMIAR. Ver o cabecalho: nada e debitado aqui nem em lugar nenhum.
		if (doAluno.MarcosLivres < Custo(s)) return RecusaDeLicao.SemMarcos;
		return RecusaDeLicao.Pode;
	}

	/// <summary>
	/// A LICAO ACONTECE -- o miolo do `Study` (`teachable.dm:53-55`), e ele cabe em uma linha porque
	/// **nada e cobrado de ninguem**:
	///
	///     nS.teacher = FALSE ; nS.wastaught = TRUE ; src.learnSkill(nS, 0, 0)
	///
	/// Devolve `false` quando a avaliacao ja tinha recusado -- quem chama e o servidor, que so
	/// chega aqui com <see cref="RecusaDeLicao.Pode"/>; a guarda existe pra que uma segunda porta
	/// nao apareca no dia em que alguem chamar isto direto.
	/// </summary>
	public static bool Ensinar(SkillBook doAluno, Skill s)
	{
		if (doAluno.Sabe(s.Path)) return false;
		doAluno.DarComoEnsinada(s.Path);
		return true;
	}

	/// <summary>
	/// O QUE ESTE LIVRO TEM PRA ENSINAR -- a lista que o `Teach_Skill` monta (`:9-10`).
	/// </summary>
	public static IEnumerable<Skill> Repassaveis(SkillCatalog cat, SkillBook livro) =>
		livro.Aprendidas.Select(cat.Get).Where(s => s != null && PodeRepassar(livro, s))!;

	/// <summary>
	/// `Forget_Studied_Skill` (`teachable.dm:25`): so se esquece o que foi ENSINADO.
	///
	/// ============================ E AQUI EU **NAO** DEVOLVO MARCO, DE OLHO ABERTO ============================
	/// O `forget()` do DM (`skill.dm:70-80`) faz `savant.skillpoints += src.skillcost` em toda skill
	/// esquecida. Encaixado no ensino -- onde o aluno **nao pagou nada** (ver o cabecalho) -- isso
	/// nao e reembolso, e uma IMPRESSORA DE MARCOS: aprenda de graca, esqueca, fique com os pontos,
	/// repita com o proximo professor.
	///
	/// Nao ha diferenca de leitura possivel aqui: as duas metades sao literais do mesmo arquivo, e
	/// juntas produzem marco do nada. Porto a estrutura (o `wastaught` e o unico esquecivel) e
	/// deixo o reembolso de fora -- **e isto e uma decisao a confirmar com o dono**, nao um
	/// esquecimento; se ele quiser a fidelidade estrita, e uma linha (`livro.Conceder(Custo(s))`).
	/// =====================================================================================================
	/// </summary>
	public static bool Esquecer(SkillBook livro, Skill s)
	{
		if (!livro.FoiEnsinada(s.Path)) return false;
		livro.Esquecer(s.Path);
		return true;
	}
}
