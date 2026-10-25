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

	/// <summary>
	/// (dono, 2026-10-23) A skill e do CARGO e nao e ataque de ki: nao passa adiante por porta nenhuma -- nem a quem chega perto,
	/// nem do mestre ao proprio aluno. Ver <see cref="EnsinoDeSkill.CargoTrancaOEnsino"/>.
	/// </summary>
	DoCargo,

	/// <summary>
	/// (dono, 2026-10-23) O Potential Unleashed (a skill `kai/Mystic`, que era a forma) nao se ensina mais por porta nenhuma: so o
	/// Ritual Mistico do Velho Kaiohshin o desperta. Ver <see cref="EnsinoDeSkill.SoPeloRitual"/>.
	/// </summary>
	SoPeloRitual,
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
/// `teacher` (poucas skills se ensinam) e `wastaught` (quem recebeu nao repassa). E desde 2026-10-23
/// uma terceira, do dono: do kit de um cargo so os ataques de ki passam adiante (<see cref="CargoTrancaOEnsino"/>).
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
	// skill que saiba**, inclusive as que nao se ensinam a estranho. (Menos a que fica no cargo -- `CargoTrancaOEnsino`, 2026-10-23.)
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

	// =====================================================================
	// DO CARGO, SO OS ATAQUES DE KI SE ENSINAM -- regra do dono (2026-10-23), e ela DIVERGE do DM de olho aberto
	// =====================================================================
	// *"ta dando pra ensinar skills de entrar na sala do tempo pros outros, unica skill de cargo q pode ensinar é a skill de
	// ataques de ki, fora isso n poderia ensinar mais nada"*.
	//
	// No DM a skill de cargo nasce `teacher = TRUE`, e o ensino e de proposito a porta dela (ver o `Avaliar`). Entao o Guardiao
	// mostrava a chave da Sala do Tempo (`rank/Permission`) a quem estivesse colado nele, e quem aprendia fazia chaves -- pra si
	// mesmo inclusive -- sem cargo nenhum, pra sempre.
	//
	// O QUE E "SKILL DE CARGO": o espaco `/datum/skill/rank/` (escolha do dono -- os estilos, o Kaio-ken, o Mistico e o Majin,
	// que tambem so um cargo da, continuam se ensinando como no DM). O QUE E "ATAQUE DE KI": todo verbo da skill e um tiro da
	// tabela de ataques de longe (`TecnicasDeLonge`): Kamehameha, Galick Ho, Final Flash, Death Ball, Spirit Bomb, Paralysis...
	// A pergunta e ao catalogo e nao a uma lista: o ataque de cargo que entrar na tabela se ensina sozinho, e a skill de cargo
	// nova que nao atira nao se ensina. A Fusion Dance e o Mafuba ficam ensinaveis por escolha do dono (a danca e tambem da
	// raca Metamoran e pede os dois dancarinos; o Mafuba sai como raio).
	//
	// E VALE NAS TRES PORTAS -- o verbo `ens_ensinar`, o cartao do aluno e a LICAO DE MESTRE (o mestre nao abre o que o cargo
	// guarda). Quem ja tinha aprendido assim perde a copia: ver `DadivaDeCargo.Revogavel`.

	/// <summary>
	/// DEFEITO INJETADO (bancada `--ensinoteste`, secao 11, e robo `--diagverbs`, F8): o kit de cargo inteiro volta a se ensinar
	/// -- a chave da Sala do Tempo passa de mao em mao, como antes de 2026-10-23. Lido pelo <see cref="CargoTrancaOEnsino"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool CargoSeEnsinaComoAntesDeTeste;

	/// <summary>As duas skills de cargo que nao sao ataque de ki e se ensinam mesmo assim -- escolha do dono (2026-10-23).</summary>
	private static readonly string[] CargoQueSeEnsinaPorEscolha = ["/datum/skill/rank/Fusion_Dance", "/datum/skill/rank/Mafuba"];

	/// <summary>ESTA SKILL E UM ATAQUE DE KI? Todo verbo dela e um tiro da tabela de longe -- e skill sem verbo nenhum nao e.</summary>
	public static bool AtaqueDeKi(Skill s) =>
		s.Verbos.Length > 0 && Array.TrueForAll(s.Verbos, v => TecnicasDeLonge.Get(v) != null);

	/// <summary>
	/// ESTA SKILL FICA NO CARGO -- e do espaco de cargo, nao e ataque de ki nem uma das duas da escolha do dono. A pergunta PURA,
	/// sem o defeito de teste: e ela que tambem diz qual copia ensinada sai do livro e o que o bio nao herda (o servidor a guarda
	/// num conjunto levantado uma vez so).
	/// </summary>
	public static bool FicaNoCargo(Skill s) =>
		s.Path.StartsWith(Ranks.DadivaDeCargo.EspacoDoCargo, StringComparison.OrdinalIgnoreCase)
		&& !AtaqueDeKi(s)
		&& !Array.Exists(CargoQueSeEnsinaPorEscolha, p => string.Equals(p, s.Path, StringComparison.OrdinalIgnoreCase));

	/// <summary>A TRAVA DO ENSINO -- a <see cref="FicaNoCargo"/> nas tres portas (o bloco acima).</summary>
	public static bool CargoTrancaOEnsino(Skill s) => !CargoSeEnsinaComoAntesDeTeste && FicaNoCargo(s);

	// =====================================================================
	// O POTENTIAL UNLEASHED NAO SE ENSINA -- regra do dono (2026-10-23), e ela DIVERGE do DM de olho aberto
	// =====================================================================
	// *"essa forma nao pode ser ensinada diretamente, precisando passar do ritual"*. No DM a skill `kai/Mystic` (`Race Trees/kai.dm:12`)
	// nasce `teacher = TRUE`: o Kaioshin a mostrava a quem estivesse colado nele, e o aluno ganhava a forma (ela era a skill). Agora so o
	// Ritual Mistico do Velho Kaiohshin (`Ranks.RitualDoKaioshin`) da a forma -- e o ensino da skill fecha por PORTA NENHUMA: o verbo
	// `ens_ensinar`, o cartao do aluno, a licao de mestre (as tres daqui), a heranca do bio (o parto pula a skill) e o botao "Ensinar" do
	// cliente. Quem ja a aprendeu fica com ela (o dono: "quem tem fica"), e a copia ensinada nao passava adiante de qualquer jeito.

	/// <summary>
	/// DEFEITO INJETADO (bancada `--ensinoteste`, familia 11m, e robo `--diagverbs`, F8m): o Potential Unleashed volta a se ensinar como
	/// antes de 2026-10-23. Lido pelo <see cref="SoPeloRitual"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MisticoSeEnsinaComoAntesDeTeste;

	/// <summary>A TRAVA DO ENSINO DO POTENTIAL UNLEASHED: a skill que era a forma so vem pelo ritual (o bloco acima).</summary>
	public static bool SoPeloRitual(Skill s) =>
		!MisticoSeEnsinaComoAntesDeTeste
		&& string.Equals(s.Path, Ranks.RitualDoKaioshin.SkillDoMistico, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// ESTE LIVRO PODE REPASSAR ESTA SKILL? As tres condicoes do `Teach_Skill` (`:9-10`), e a
	/// terceira e a regra inteira do sistema:
	///
	///   1. ele **sabe** a skill;
	///   2. o tipo diz que ela se ensina (`teacher = TRUE`) -- e, desde 2026-10-23, ela nao fica no cargo
	///      (<see cref="CargoTrancaOEnsino"/>) nem e o Potential Unleashed (<see cref="SoPeloRitual"/>);
	///   3. ele **nao a aprendeu de alguem** (`teacher = FALSE` na copia ensinada, `:53`).
	///
	/// A (3) e o que impede a corrente: A ensina B, e B nao ensina C. Sem ela uma skill rara
	/// atravessa o servidor inteiro numa tarde, e o `teacher` do catalogo vira decoracao.
	/// </summary>
	public static bool PodeRepassar(SkillBook livro, Skill s) =>
		s.Ensinavel && !CargoTrancaOEnsino(s)
		&& !SoPeloRitual(s)
		&& livro.Sabe(s.Path) && !livro.FoiEnsinada(s.Path);

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
	/// portoes aqui apagaria as 83 skills ensinaveis do jogo de uma vez, em silencio. (Desde 2026-10-23
	/// a porta do ensino so fica aberta pros ataques de ki do cargo: ver <see cref="CargoTrancaOEnsino"/>.)
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
		// ANTES da licao de mestre: ela nao abre o que o cargo guarda (dono, 2026-10-23).
		if (CargoTrancaOEnsino(s)) return RecusaDeLicao.DoCargo;
		// ...nem o Potential Unleashed, que so o ritual da (dono, 2026-10-23).
		if (SoPeloRitual(s)) return RecusaDeLicao.SoPeloRitual;
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
	/// O QUE ESTE MESTRE TEM PRA ENSINAR A UM ALUNO DELE -- tudo o que ele sabe e nao e arvore, menos o que fica no cargo
	/// (<see cref="CargoTrancaOEnsino"/>: a tela sabe disso pelo catalogo, e um botao que recusa pra todo mundo e so ruido) e o
	/// Potential Unleashed (<see cref="SoPeloRitual"/>, pelo mesmo motivo). A lista e o que a tela oferece; cada licao ainda passa
	/// pelo <see cref="Avaliar"/> (o que foi ensinado a ele nao repassa, e a tela nao sabe disso: quem diz e a recusa).
	/// </summary>
	public static IEnumerable<Skill> OQueOMestreEnsina(SkillCatalog cat, IEnumerable<string> aprendidas)
	{
		foreach (string path in aprendidas)
			if (cat.Get(path) is { Arvore: false, Nome.Length: > 0 } s && !CargoTrancaOEnsino(s)
				&& !SoPeloRitual(s))
				yield return s;
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
