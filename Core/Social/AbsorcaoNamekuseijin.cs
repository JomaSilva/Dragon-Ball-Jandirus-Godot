namespace Jandirus.Core.Social;

/// <summary>
/// ============================ A FUSAO NAMEKUSEIJIN, QUE E UMA ABSORCAO ============================
/// **TODOS OS NUMEROS DELA MORAM AQUI, e e de proposito**: o dono disse por escrito que vai querer
/// afinar isto depois, e caçar decimal espalhado por tres arquivos e o jeito de nunca afinar nada.
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
/// *"faca a fusao namek, q faz o namekuseijin liberar a transformacao super namek e tb ganhar um bonus
/// baseado no namek absorvido na fusao, o outro namek se for jogador, perde o personagem pra sempre (a
/// fusao e eterna), fundir com npc namek ganha BEM menos bp e outros bonus e nao ganha o super namek."*
///
/// Sao cinco regras, e elas nao sao todas do mesmo tipo -- metade e porte e metade e desenho novo. A
/// divisao esta escrita campo por campo aqui embaixo, porque ela diz de onde cada numero pode sair.
/// ==================================================================================
///
/// ============================ POR QUE ELA NAO PRODUZ UMA `FusaoAtiva` ============================
/// Este e o achado que decidiu o desenho inteiro, e ele nao e estetico.
///
/// O motor de fusao deste port guarda a fusao VIVA: um objeto com dono, passageiro, energia, stats
/// emprestados e um corpo trancado no selo. Isso funciona porque a Danca dura 15 minutos e a Potara 30
/// -- **e porque o `GameServer.Persistir` se recusa a gravar corpo fundido** (ver o item 3 do
/// `&lt;remarks&gt;` de la, que lista as tres coisas que o save nao sabe descrever: a zona do
/// passageiro, os stats emprestados e o `FuseBuff`).
///
/// Uma fusao ETERNA quebra essa troca pelos dois lados ao mesmo tempo:
///
///   * o dono nunca mais poderia ser gravado -- uma fusao que nao acaba e um personagem que nao salva;
///   * o passageiro seria um `ServerPlayer` vivo com o save APAGADO (regra N3), e cada caminho que
///     encosta nele -- o `Separar` do logout, o tique que ve o corpo cair, o `PassarOControle` --
///     devolveria ao mundo um corpo sem ficha nenhuma.
///
/// **O DM ja tem a saida, e ela e esta classe.** `Fusion.dm:301-310`: quando o passageiro reencarna (a
/// unica forma de a fusao Namekuseijin virar definitiva no original), o jogo pergunta ao dono *"make
/// this truly permanent? it becomes part of your core character"* e, no sim, faz
/// `Keeper.BP = FusedBaseBP` e marca `CompletelyPerm = 1`. Ou seja: **o estado terminal da fusao
/// Namekuseijin do proprio DM e o poder ASSADO no personagem, sem fusao viva nenhuma.** E exatamente o
/// que o dono descreveu ("absorvido", "e eterna", "perde o personagem"), e e o que esta classe faz.
///
/// O que se perde em relacao ao DM esta escrito e e pequeno: o passageiro-espectador selado
/// (`isnamekd`, `Set_Fusion_View`) e o `PassControl` da Namekuseijin. Os dois pressupoem um personagem
/// que continua existindo -- e a regra N3 diz que ele nao continua.
/// ==========================================================================================
/// </summary>
public static class AbsorcaoNamekuseijin
{
	// =====================================================================
	// N3 -- O CONSENTIMENTO DE QUEM VAI PERDER O PERSONAGEM
	// =====================================================================
	/// <summary>
	/// ============================ QUANTOS SEGUNDOS ENTRE AS DUAS CONFIRMACOES ============================
	/// A regra N3 (*"perde o personagem pra sempre"*) e a coisa mais irreversivel que este jogo faz, e o
	/// aceite dela **nao pode ser o mesmo botao** do aceite comum de fusao -- que promete, com todas as
	/// letras, *"voce volta quando a fusao acabar"*.
	///
	/// O portao mais forte que o port ja tem pra uma consequencia parecida e o `DeleteChar`: ele exige
	/// **digitar o nome do personagem**. Em jogo nao ha campo de texto -- os verbs carregam um id e mais
	/// nada --, entao o que substitui a digitacao e a combinacao de tres coisas:
	///
	///   1. um verb PROPRIO (`fus_namek_sim`), que nunca aceita mais nada, com o preco escrito no nome;
	///   2. **duas** confirmacoes, e entre a primeira e a segunda o servidor diz o que exatamente se
	///     perde (o nome, a raca, a idade e o poder daquele personagem);
	///   3. este intervalo minimo entre elas -- que e o que impede que um clique duplo, um macro ou um
	///     cliente modificado mandando o pacote duas vezes seguidas passe pelas duas.
	///
	/// **CINCO SEGUNDOS**, e o numero e escolhido contra o gesto e nao contra o relogio: e curto demais
	/// pra irritar quem decidiu, e longo demais pra caber num duplo clique ou numa rajada de pacote. O
	/// prazo do convite continua sendo o de sempre (`Fusao.PrazoDoConviteSegundos`, 60 s), entao cabem
	/// as duas confirmacoes com folga.
	/// ================================================================================================
	/// </summary>
	public const double SegundosEntreAsConfirmacoes = 5;

	// =====================================================================
	// N2 -- O BP QUANDO O ABSORVIDO E UM JOGADOR. **ISTO E PORTE.**
	// =====================================================================
	/// <summary>
	/// O GANHO DE BP AO ABSORVER UM JOGADOR: o BP final vira `(meu + dele) * 2`.
	///
	/// ============================ NAO E UM NUMERO MEU: E A CONTA DO DM, DUAS VEZES ============================
	/// `Fusion.dm:264` -- `FusedBaseBP = (Keeper.BP + Loser.BP) * 2` -- e a formula de TODA fusao do
	/// original, e ela nao pergunta o `FType`. E `Fusion.dm:308` -- `Keeper.BP = FusedBaseBP` -- e o
	/// que a assa no BP real quando a fusao Namekuseijin vira definitiva. As duas linhas juntas dizem
	/// que o poder permanente de quem absorveu **e** o `(A+B)*2`.
	///
	/// Por isso esta funcao nao tem constante propria: ela DELEGA pro <see cref="Fusao.BpDaFusao"/>,
	/// que e a mesma conta que a Danca e a Potara ja usam. Uma segunda escrita do mesmo `*2` seria a
	/// primeira a divergir.
	///
	/// **E ELA E "BASEADA NO ABSORVIDO" NO SENTIDO EXATO DO PEDIDO**: absorver alguem mais forte que
	/// voce multiplica muito mais que absorver um fraco. Piccolo + Nail (2,0 M + 0,4 M) da 4,8 M;
	/// Piccolo + Kami (2,0 M + 2,0 M) da 8,0 M.
	/// =====================================================================================================
	/// </summary>
	public static double BpDepoisDeAbsorverJogador(double meuBp, double bpDoAbsorvido) =>
		Fusao.BpDaFusao(meuBp, bpDoAbsorvido);

	// =====================================================================
	// N4 -- O NPC. **ISTO E DESENHO NOVO** (o DM nao distingue)
	// =====================================================================
	/// <summary>
	/// ============================ O DM NAO TEM ESTA REGRA, E O PEDIDO DO DONO TEM ============================
	/// Medido no `Fusion.dm`: o verb aceita qualquer `mob` do `oview(1)` (`:553`) e a formula so le
	/// `Loser.BP` (`:264`) -- **nao ha uma linha distinguindo jogador de NPC**. Na pratica fundir com
	/// NPC nunca acontecia la, mas por ACIDENTE e nao por regra: o aceite e um `input(M, ...)`
	/// (`:566`) e um mob sem `client` nunca responde "Yes".
	///
	/// Entao "NPC da BEM MENOS" e desenho novo. **A FORMA dele nao e**: ela e copiada do unico lugar
	/// do jogo que ja resolve essa mesma pergunta, a absorcao (`Absorption.dm:50-53`), transcrita:
	///
	///     if(M.Player)  usr.absorbadd += (M.BP/50) + M.absorbadd*(M.PowerPcnt/100)*(M.Anger/100)
	///     else          usr.absorbadd += (usr.bp_gain_base()*BPTick*1/250)
	///
	/// Leitura: jogador rende uma FRACAO do BP DELE; NPC rende um filete que **nao escala com quem foi
	/// comido**. O comentario do proprio DM ja declarava a intencao (`Absorption.dm:7-8`: *"NPCs will
	/// give lower gains than people"*). O que muda aqui e so a escala -- la e um tique de treino, aqui
	/// e um gesto de uma vez por hora.
	/// ====================================================================================================
	///
	/// O NPC RENDE UMA FRACAO DO BP DELE, e nao a soma dobrada: **10%**. Um NPC Namek de vilarejo tem BP
	/// de vilarejo, entao na pratica isto e ruido -- que e o ponto do "bem menos". Absorver um NPC forte
	/// (um chefe de saga Namekuseijin) rende de verdade, e ai o <see cref="TetoDoGanhoDeNpc"/> entra.
	/// </summary>
	public const double FracaoDoBpDoNpc = 0.10;

	/// <summary>
	/// O TETO DO GANHO POR NPC, como fracao do MEU proprio BP: **5%**.
	///
	/// ============================ ELE EXISTE POR UM EXPLOIT, E O EXPLOIT E REAL ============================
	/// O povoamento de planeta REPOE habitante (`GameServer.Povoamento`), e Namek e um planeta de
	/// Namekuseijin. Sem teto, "absorver o vilarejo" seria uma escada de poder infinita limitada so pela
	/// paciencia -- e o unico freio seria a recarga de 1 h (`Fusao.RecargaSegundos`, o
	/// `FUSION_COOLDOWN` do `Fusion.dm:6`), que ja se aplica aqui.
	///
	/// Com o teto, a conta fecha: 5% do proprio BP por hora, no MELHOR caso, contra as horas de treino
	/// que o mesmo tempo renderia. Absorver NPC vira o que o dono pediu que fosse -- um caminho que
	/// existe e que vale MUITO menos que o outro.
	/// ======================================================================================================
	/// </summary>
	public const double TetoDoGanhoDeNpc = 0.05;

	/// <summary>
	/// O BP DEPOIS DE ABSORVER UM NPC. Ver <see cref="FracaoDoBpDoNpc"/> e <see cref="TetoDoGanhoDeNpc"/>.
	///
	/// O `Math.Min` E A REGRA E NAO UMA SALVAGUARDA: e ele que faz o resultado depender de QUEM foi
	/// comido enquanto o NPC e fraco (o caso normal) e parar de depender quando ele e absurdamente
	/// forte -- que e a unica forma de "escala com o absorvido" e "nao vira escada" caberem juntas.
	/// </summary>
	public static double BpDepoisDeAbsorverNpc(double meuBp, double bpDoNpc) =>
		meuBp + Math.Min(Math.Max(bpDoNpc, 0) * FracaoDoBpDoNpc, Math.Max(meuBp, 0) * TetoDoGanhoDeNpc);

	/// <summary>
	/// O BP DEPOIS DE ABSORVER, **o funil unico dos dois casos**.
	///
	/// Um metodo e nao dois chamados de fora: quem absorve nao deve escolher a formula, deve dizer o
	/// que absorveu. As duas contas divergindo em um `if` escrito no servidor seria a copia que
	/// envelhece -- o defeito mais repetido deste port.
	/// </summary>
	public static double BpDepoisDeAbsorver(double meuBp, double bpDoAbsorvido, bool ehJogador) =>
		ehJogador ? BpDepoisDeAbsorverJogador(meuBp, bpDoAbsorvido)
				  : BpDepoisDeAbsorverNpc(meuBp, bpDoAbsorvido);

	// =====================================================================
	// N1 e N4 -- O QUE VEM ALEM DO BP ("outros bonus")
	// =====================================================================
	/// <summary>
	/// ============================ O "PUXAO PRA CIMA" -- STATS, KI E GRAVIDADE ============================
	/// **SO QUANDO O ABSORVIDO E JOGADOR.** *"fundir com npc namek ganha BEM menos bp **e outros
	/// bonus**"* -- os dois membros da frase, e nao so o primeiro.
	///
	/// A FORMA VEM DO OUTRO REPO, e la ela e literal. `DU-SOURCE-master\Code\Races\Nameks.dm:187-195`,
	/// dentro do `Puranto_Fusion()` (que e a fusao Namek do DU, com o mesmo desenho que o dono pediu --
	/// *"the person who offers the fusion has their character deleted and the other gets the boost"*,
	/// `:131`):
	///
	///     if(P.bp_mod &lt; bp_mod) P.bp_mod = bp_mod
	///     if(P.base_bp &lt; base_bp) P.base_bp = base_bp
	///     if(P.max_ki/P.Eff &lt; max_ki/Eff) P.max_ki = max_ki/Eff*P.Eff
	///     if(P.gravity_mastered &lt; gravity_mastered) P.gravity_mastered = gravity_mastered
	///     if(P.Health &lt; 100) P.Health = 100
	///     if(P.Ki &lt; P.max_ki) P.Ki = P.max_ki
	///
	/// Leitura: **quem absorve nunca PERDE nada** -- ele e puxado ate o nivel do absorvido em cada
	/// eixo, e fica onde estava nos eixos em que ja era melhor. E isso ja e, sozinho, "um bonus baseado
	/// no namek absorvido": comer um fraco quase nao muda nada, comer um forte sobe voce ate ele.
	///
	/// Neste port o eixo "o maior de cada" ja existe e ja e codigo de producao -- e o passo 2 do
	/// `GameServer.Fundir`, que a Danca e a Potara usam desde o pedido do dono (*"se jogador 1 tem 30
	/// de physical e o 2 tem 40, a fusao tem 40"*). A absorcao usa **o mesmo**, so que sem devolver.
	/// ================================================================================================
	/// </summary>
	public static bool HerdaOsStats(bool absorvidoEhJogador) => absorvidoEhJogador;

	/// <summary>
	/// AS SKILLS DO ABSORVIDO, e **so quando ele e jogador** -- mesma frase do dono do bloco acima.
	///
	/// ============================ ISTO RESPONDE UMA PERGUNTA QUE ESTAVA ABERTA ============================
	/// Existia aqui uma constante `Fusao.HerancaNaFusaoNamekuseijin`, escrita como pergunta em aberto:
	/// *"a Metamoro e a Potara herdam as skills dos dois e o maior stat de cada; o dono nunca disse se
	/// isso vale pra fusao PERMANENTE"*. Ela ficou `false` por meses e as duas bancadas liam a constante
	/// em vez de cravar zero, esperando a resposta.
	///
	/// **O pedido de agora responde de outro jeito, e a constante foi DELETADA.** O dono nao falou em
	/// "as skills dos dois": falou em *"um bonus baseado no namek absorvido"*, com escala diferente pra
	/// jogador e pra NPC. Isso nao e a heranca simetrica da Potara -- e este bloco. A skill entra no
	/// livro de quem absorveu **de vez** (nao ha `SkillsEmprestadas` porque nao ha separacao), e nao
	/// entra nenhuma quando o absorvido e um NPC.
	/// ================================================================================================
	/// </summary>
	public static bool HerdaAsSkills(bool absorvidoEhJogador) => absorvidoEhJogador;

	/// <summary>
	/// ============================ N1: A FUSAO DESTRAVA O SUPER NAMEKUSEIJIN ============================
	/// *"a fusao namek [...] faz o namekuseijin liberar a transformacao super namek"*, e *"fundir com
	/// npc namek [...] **nao ganha o super namek**"*. As duas metades sao esta linha.
	///
	/// **ISTO E PEDIDO NOVO, E NAO PORTE -- conferido.** O `Namekian_Fusion` do DM
	/// (`Fusion.dm:549-569`) nao encosta em `snamek` nem em skill nenhuma, e o UNICO escritor daquela
	/// flag no original inteiro e o `after_learn()` da skill comprada (`namekian.dm:37-38`). Quem vier
	/// procurar a linha do DM que justifica isto nao vai achar.
	///
	/// O QUE ELE DESTRAVA E O CAMINHO, e nao uma forma nova: ver
	/// <see cref="Forms.Catalogo"/> (a entrada `snamek`, que ja existia inteira) e
	/// <see cref="PathDaSkillDoSuperNamekuseijin"/>, que e a porta unica pela qual os DOIS caminhos
	/// deste sistema passam.
	/// ============================================================================================
	/// </summary>
	public static bool DestravaOSuperNamekuseijin(bool absorvidoEhJogador) => absorvidoEhJogador;

	// =====================================================================
	// A PORTA UNICA DO SUPER NAMEKUSEIJIN -- N1 e N5 desembocam AQUI
	// =====================================================================
	/// <summary>
	/// ============================ UMA PORTA, TRES CAMINHOS ============================
	/// A forma `snamek` e UMA entrada de catalogo (`Formas.cs`, linha Namekuseijin) e o portao dela e
	/// UM: `PedeFlag("snamek")`. Quem escreve essa flag e o `after_learn` desta skill -- **e so ele**,
	/// no DM (`namekian.dm:37-38`) e aqui (`EfeitosDeSkill`, canal ATRIBUICAO, `skills.json:247`).
	///
	/// Entao os tres jeitos de ganhar a forma sao tres jeitos de ganhar **esta skill**, e nao tres
	/// implementacoes da forma:
	///
	///   1. **COMPRAR na arvore racial** -- o caminho do DM, 2 pontos de marco, e ele continua valendo;
	///   2. **DESPERTAR pelo proprio poder (N5)** -- cruzar o limiar PESSOAL `snamekat`, do mesmo jeito
	///      que um Saiyajin vira Super Saiyajin ao cruzar o `ssjat` dele. Ver
	///      `LimiaresPessoais.RolarNamek`;
	///   3. **ABSORVER outro JOGADOR Namekuseijin (N1)** -- esta classe.
	///
	/// ============================ POR QUE A SKILL, E NAO UM BIT NOVO NO SAVE ============================
	/// Porque escrever `snamek = 1` na mao no `Fighter` **nao funciona, e falha calada**:
	/// `Fighter.FlagsDeSkill` e RECONSTRUIDO DO ZERO a partir do livro a cada `AplicarEfeitos`
	/// (`EfeitosDeSkill.cs:159-167`), e `AplicarEfeitos` roda no login, em toda compra de skill e no
	/// proprio caminho da absorcao. O bit escrito a mao sumiria no tique seguinte.
	///
	/// E porque a skill **ja persiste**: ela vai pro `CharacterSave.Skills` como qualquer outra. Um
	/// campo novo no save resolveria a mesma coisa custando um campo e um leitor a mais.
	/// ============================================================================================
	/// </summary>
	public const string PathDaSkillDoSuperNamekuseijin = "/datum/skill/namek/SuperNamek";
}
