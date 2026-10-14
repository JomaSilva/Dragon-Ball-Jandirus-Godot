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

	/// <summary>
	/// A skill nao se ensina a estranho, quem recebe e ALUNO de quem ensina -- e mesmo assim ela nao pende de arvore
	/// nenhuma do aluno (e de outra raca, de outra classe, so de vilao). Ver <see cref="EnsinoDeSkill.Avaliar"/>.
	/// </summary>
	NaoEDoCorpoDele,
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
/// **Ate 2026-10-13 amarrar os dois seria inventar regra**, e este cabecalho dizia exatamente isso. Nesse dia o
/// dono mandou: *"dar pra ensinar mais skills pros alunos e tb técnicas proprias"*. O ensino de sempre continua
/// sem pedir mestre -- quem ensina o Kamehameha ao estranho que chegou perto nao vira mestre dele --, e o que
/// entrou e uma SEGUNDA porta, que so abre entre mestre e aluno do discipulado: ver <see cref="LicaoDeMestre"/>.
/// Fora dela a independencia continua sendo a regra.
/// ================================================================================================
///
/// ============================ O QUE O ALUNO PAGA NA LICAO DE SEMPRE: **NADA** ============================
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

	// =====================================================================
	// A LICAO DO MESTRE AO PROPRIO ALUNO -- regra do dono (2026-10-13), e ela DIVERGE do DM de olho aberto
	// =====================================================================
	// *"dar pra ensinar mais skills pros alunos e tb técnicas proprias"*.
	//
	// O cabecalho desta classe diz que o ensino NAO pede vinculo de mestre, e continua verdade pras skills que
	// "se ensinam" (`teacher = TRUE`): qualquer um as mostra a qualquer um, de graca, como no original. O que entrou
	// e uma SEGUNDA porta, e ela so abre entre mestre e aluno do discipulado: **o mestre ensina ao aluno DELE qualquer
	// skill que saiba**, inclusive as que nao se ensinam a estranho.
	//
	// O QUE O MESTRE ABRE, E O QUE ELE NAO ABRE. Pro aluno, a licao do mestre e uma COMPRA COM A PORTA ABERTA: o
	// pre-requisito que falta, o tier da arvore que ele ainda nao alcancou e a skill que nasce apagada esperando um
	// acendedor deixam de barrar -- o mestre mostra o caminho. O que continua barrando e o que nenhum mestre muda: a
	// skill tem de pender de uma arvore que o ALUNO tem (raca, classe, vilao -- `SkillBook.Avaliar`), e ele PAGA os
	// marcos dela. (Nas de sempre o custo continua sendo so limiar.) Paga porque de graca o mestre despejaria o
	// arsenal inteiro em cinco alunos numa tarde, e os marcos deixariam de ser escolha -- e a mesma conta que fez o
	// DM limitar o que e `teacher`. DECISAO MINHA, declarada: o dono pediu a mecanica, nao o preco.
	//
	// E QUEM APRENDEU ASSIM NAO REPASSA, como toda copia ensinada (`wastaught`): senao o aluno viraria o caminho
	// pra skill sair do circulo do mestre.

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o mestre volta a so ensinar ao aluno o que ensinaria a qualquer um
	/// -- a v0.2.0. Lido pela <see cref="LicaoDeMestre"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MestreSoEnsinaOQueSeEnsinaDeTeste;

	/// <summary>
	/// ESTA LICAO E A DO MESTRE? -- a skill nao se ensina a estranho, e quem recebe e aluno de quem ensina. E ela
	/// quem COBRA os marcos do aluno (<see cref="Ensinar"/>).
	/// </summary>
	public static bool LicaoDeMestre(Skill s, bool eMeuAluno) =>
		!s.Ensinavel && eMeuAluno && !MestreSoEnsinaOQueSeEnsinaDeTeste;

	/// <summary>
	/// O que o livro do ALUNO diz desta skill barra a licao do mestre? So o que mestre nenhum muda: a skill nao e
	/// do corpo dele. Pre-requisito, tier, acendedor e marcos NAO entram aqui -- os tres primeiros o mestre abre, e os
	/// marcos tem a recusa propria deles.
	///
	/// (E a skill FECHADA -- `SkillsFechadas`, a que so promete um verb que este jogo ainda nao tem -- entra junto: a
	/// licao do mestre COBRA marcos, e cobraria por nada.)
	/// </summary>
	public static bool ForaDoCorpo(Recusa doAlunoSozinho) =>
		doAlunoSozinho is Recusa.NaoExiste or Recusa.SoVilao or Recusa.RacaOuClasse or Recusa.SemArvore or Recusa.Apagada
			or Recusa.ForaDoPort;

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
	/// <param name="eMeuAluno">Quem recebe e aluno (do discipulado) de quem ensina -- abre a licao do mestre, ver o bloco acima.</param>
	/// <param name="doAlunoSozinho">
	/// O que o livro do aluno responde se ELE fosse comprar esta skill (`SkillBook.PodeAprender`). So e lido na licao
	/// do mestre, e so pra saber se a skill e do corpo dele (<see cref="ForaDoCorpo"/>).
	/// </param>
	public static RecusaDeLicao Avaliar(SkillBook doMestre, SkillBook doAluno, Skill? s,
										bool mestreTemAssinatura, bool alunoTemAssinatura,
										bool mesmaPessoa, double distanciaTiles, bool naRecarga,
										bool eMeuAluno = false, Recusa doAlunoSozinho = Recusa.Pode)
	{
		if (s == null || s.Arvore) return RecusaDeLicao.NaoExiste;
		if (!doMestre.Sabe(s.Path)) return RecusaDeLicao.NaoSabe;
		if (!s.Ensinavel && !LicaoDeMestre(s, eMeuAluno)) return RecusaDeLicao.NaoSeEnsina;
		if (doMestre.FoiEnsinada(s.Path)) return RecusaDeLicao.AprendeuDeAlguem;
		if (!mestreTemAssinatura || !alunoTemAssinatura) return RecusaDeLicao.SemAssinatura;
		if (mesmaPessoa) return RecusaDeLicao.EleMesmo;
		if (doAluno.Sabe(s.Path)) return RecusaDeLicao.AlunoJaSabe;
		if (LicaoDeMestre(s, eMeuAluno) && ForaDoCorpo(doAlunoSozinho)) return RecusaDeLicao.NaoEDoCorpoDele;
		if (distanciaTiles >= 0 && distanciaTiles > TilesDaLicao) return RecusaDeLicao.Longe;
		if (naRecarga) return RecusaDeLicao.Recarga;

		// O LIMIAR. Nada e debitado AQUI: a licao de sempre nunca debita, e a de MESTRE debita no `Ensinar(cobrar)`.
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
	/// <param name="cobrar">
	/// A licao do MESTRE (<see cref="LicaoDeMestre"/>): os marcos da skill saem do aluno. Sem saldo ela nao acontece
	/// -- a avaliacao ja tinha recusado, e a guarda daqui e a mesma do `Sabe` logo abaixo.
	/// </param>
	public static bool Ensinar(SkillBook doAluno, Skill s, bool cobrar = false)
	{
		if (doAluno.Sabe(s.Path)) return false;
		if (cobrar)
		{
			if (doAluno.MarcosLivres < Custo(s)) return false;
			doAluno.MarcosLivres -= Custo(s);
		}
		doAluno.DarComoEnsinada(s.Path);
		return true;
	}

	/// <summary>
	/// O QUE ESTE MESTRE TEM PRA ENSINAR A UM ALUNO DELE -- tudo o que ele sabe e nao e arvore. A lista e o que a tela
	/// oferece; cada licao ainda passa pelo <see cref="Avaliar"/> (o que foi ensinado a ele nao repassa, e a tela nao
	/// sabe disso: quem diz e a recusa).
	/// </summary>
	public static IEnumerable<Skill> OQueOMestreEnsina(SkillCatalog cat, IEnumerable<string> aprendidas)
	{
		foreach (string path in aprendidas)
			if (cat.Get(path) is { Arvore: false, Nome.Length: > 0 } s) yield return s;
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
