namespace Jandirus.Core.Forms;

/// <summary>
/// A QUAL ESCADA a forma pertence.
///
/// ============================ POR QUE ISTO EXISTE ============================
/// No BYOND o degrau era um NUMERO solto (`mob/var/ssj = 3`), e a linha era descoberta por
/// exclusao espalhada pelo codigo: `if(FutureLineage && ssj == 1)`, `if(Class == "Legendary
/// Primal Saiyan")`, `if(lssj)`. Acrescentar um estagio significava caçar todo lugar que
/// comparava o numero -- o cabelo, a aura, a cinematica, o gate, o dreno, o save.
///
/// Aqui a linha e um CAMPO. Uma forma nova e uma entrada nova no <see cref="Catalogo"/> e mais
/// nada: quem desenha cabelo pergunta ao dado, quem decide gate pergunta ao dado, quem calcula
/// multiplicador pergunta ao dado.
/// ============================================================================
/// </summary>
public enum LinhaDeForma
{
	/// <summary>A escada Saiyajin comum: SSJ1 -> grades -> SSJ2 -> SSJ3 -> SSJ4 -> 4FP -> 4LB.</summary>
	Saiyajin,

	/// <summary>`FutureLineage`: substitui o SSJ1 por uma escada propria de 10 degraus.</summary>
	Futuro,

	/// <summary>`lssj`: a linha do Legendary comum (Wrathful ... Full Power Controlled).</summary>
	Legendary,

	/// <summary>`Class == "Legendary Primal Saiyan"`: ladder proprio, sobrepoe a Saiyajin inteira.</summary>
	LegendaryPrimal,

	/// <summary>`godki`: SSG / Blue / Blue Evolution. A escada divina PADRAO.</summary>
	GodKi,

	/// <summary>
	/// A escada divina da classe **Kaio**: SSG -> Rose -> Rose 2. `statsaiyan.dm:156`.
	///
	/// E linha PROPRIA e nao variante solta dentro da GodKi, e a razao e mecanica: o "degrau
	/// anterior" sai da Ordem dentro da linha, entao o Rose 2 acharia que vem do Blue Evolution --
	/// uma forma que a classe Kaio nao pode ter. Ficaria inalcancavel, calado.
	/// </summary>
	GodKiRose,

	/// <summary>
	/// A LINHA DO MISTICO: Mistico -> Beast. As duas moram no `Mystic.dm` do original.
	///
	/// ============================ ELA DEIXOU DE SER "A ESCADA DO PRODIGIAL" ============================
	/// Chamava-se `GodKiProdigial` porque, quando nasceu, so o Prodigial a alcancava -- ela era o
	/// SSG/Blue dele (`godki.dm:349-352`, "Prodigial NAO tem SSG/Blue"). Isso mudou por regra do
	/// dono: **o Mistico e concedido pelo RITUAL de um Kaioshin e TODA raca pode receber**. Um
	/// Namekiano sem uma gota de ki divino pode andar nesta linha, e o cabecalho do painel de
	/// formas dizia "Ki divino · Prodigial" pra ele.
	///
	/// O Beast continua sendo so do Prodigial -- mas isso e do CAMPO dele (classe + ki divino
	/// maduro), nao da linha. Renomear foi o compilador achando os consumidores por mim: as
	/// derivacoes de cor, aura, cabelo e raiva desta linha isolam o Beast por
	/// `PedeGodKi >= GodkiRoyalePct`, e o Mistico saiu de `PedeGodKi = 0` pra `-1` justamente
	/// porque agora ele nao pede ki divino nenhum.
	/// ============================================================================================
	/// </summary>
	Mistico,

	/// <summary>`UltraInstinct.dm`: Sign e Perfected.</summary>
	UltraInstinct,

	/// <summary>`UltraEgo.dm`: Destroyer Form e Ultra Ego.</summary>
	UltraEgo,

	/// <summary>
	/// O Oozaru. E PARALELA de proposito -- ver o cabecalho de <see cref="Oozaru"/>: nao se sobe
	/// pra ela, ela te pega. Esta no catalogo so pelos DADOS (nome, aura, mult) e pra o SSJ4
	/// poder exigi-la por campo em vez de por codigo.
	/// </summary>
	Oozaru,

	/// <summary>
	/// ============================ A ESCADA DO FROST DEMON -- `IcerTransform.dm` ============================
	/// A PRIMEIRA linha nao-Saiyajin do catalogo, e ela e diferente das dez de cima em duas coisas
	/// que valem estar escritas aqui, porque as duas vao surpreender quem ler o resto do arquivo:
	///
	/// 1. **A `Ordem` desta linha E o `fd_form` do DM** -- 1 a 7, e nao 10, 20, 30. A folga de dez em
	///    dez existe pra caber um estagio no meio (ver <see cref="FormaDef.Ordem"/>); aqui NAO cabe:
	///    o numero da forma escolhe o CORPO que o jogador desenhou na criacao
	///    (`Appearance.FormasDeFrost`, um sprite por degrau), entao um degrau novo no meio nao e uma
	///    entrada -- e um slot novo na tela de criacao e um save antigo com a lista curta.
	///
	/// 2. **Ela tem degraus ABAIXO da base.** As formas 1 a 4 sao SUPRESSAO (0,25x a 0,90x) e so o
	///    Mutante as tem: ele NASCE preso na primeira, com o BP de fabrica 4x maior por causa disso
	///    (`body_custom.dm:234`, `Genome.cs`). O 5 e a forma BASE (1x), o 6 e a primeira evolucao
	///    (10x) e o 7 e a Forma Black (20x).
	///
	/// E POR ISSO O FROST DEMON NAO DESCANSA NO `Catalogo.IdBase`: o repouso dele e uma ENTRADA desta
	/// linha (a 5 pro normal, a 1 pro Mutante) -- ver <see cref="Catalogo.PisoDaEscada"/>. Se ele
	/// descansasse na base, o Mutante ficaria com 1x de multiplicador em cima de um BP quadruplicado,
	/// que e exatamente o buraco que este porte veio fechar.
	/// =====================================================================================================
	/// </summary>
	FrostDemon,

	/// <summary>
	/// ============================ O SUPER NAMEKUSEIJIN -- `Super_Namek.dm` ============================
	/// UM degrau, 5x, e ele e a linha inteira. Nao ha escada: `snamek` e um booleano no DM (a skill
	/// `namek/SuperNamek` o escreve) e o `Loop` do buff so conhece `if(1)`.
	///
	/// E a linha mais BARATA que este catalogo ja recebeu, e nao por acaso: o
	/// <see cref="LimiaresPessoais.snamekat"/> ja era sorteado no nascimento e a chave `"snamekat"`
	/// ja estava no `Porta()` -- groundwork pago por uma sessao anterior que ficou sem consumidor.
	/// Ela e a prova de que o catalogo aguenta raca nao-Saiyajin sem mecanismo novo.
	/// ==================================================================================================
	/// </summary>
	Namekuseijin,

	/// <summary>
	/// ============================ O BIO-ANDROIDE -- `CellFormBuff.dm` ============================
	/// UMA entrada, e ela e a linha inteira: a **SUPER PERFEITA**, 8x.
	///
	/// ============================ POR QUE OS OUTROS QUATRO DEGRAUS NAO ESTAO AQUI ============================
	/// Larva, Imperfeito, Semi-Perfeito e Perfeito **nao sao formas** -- sao ESTADO PERMANENTE do
	/// corpo (`Stats.Fighter.bio_stage`, onde a decisao esta escrita por extenso). No DM eles fazem
	/// `BP *= 2` e `BP *= 4` no BP BASE e trocam o `oicon`; nao ha buff, nao ha dreno e nao ha volta.
	/// Poe-los aqui teria mentido no teto de treino, no teto do Zenkai e no `CapCheck`, que leem o BP
	/// base e nao o expresso.
	///
	/// A SUPER PERFEITA E O CONTRARIO DISSO em todos os pontos, e por isso ela E forma: `ssjBuff =
	/// cell4mult` (familia 1), dreno de 1% ao segundo, e ela **cai sozinha quando o Ki acaba** (o
	/// unico estado temporario que o bio-androide tem, `CellFormBuff.dm:13-19`).
	///
	/// ============================ E ELA **NAO** PEDE DNA SAIYAJIN ============================
	/// Vale dizer em voz alta porque a expectativa comum e a oposta: `Cell4()` (`CellFormBuff.dm:74`)
	/// nao olha raca, DNA, `canSSJ` nem `bio_saiyan_dna` -- uma unica vez. O que ela pede e o que
	/// esta nos campos abaixo: forma PERFEITA permanente, BP base suficiente, e **nao estar em
	/// nenhum degrau Super Saiyajin** (`!ssj`).
	///
	/// O SSJ2 do bio e OUTRA coisa, por outro caminho (a MORTE, `DNALabs.dm:649`), na linha Saiyajin
	/// -- e as duas se excluem, exatamente como no original: la as duas escrevem na mesma `mob/var/
	/// ssj`. Aqui a exclusao e o campo `ProibidoComFormaAtual` da entrada, que e a mesma frase dita
	/// por dado em vez de por colisao de variavel.
	/// =========================================================================================================
	/// </summary>
	BioAndroide,

	/// <summary>
	/// ============================ O HERAN -- `HeranBuff.dm` ============================
	/// Max Power e True Max Power, os dois com maestria em DEGRAUS pela % (`heran_form_mult()`), os
	/// dois nascendo da RAIVA (`heran.dm:20-52`, o mesmo `switch(Emotion)` do Super Saiyajin).
	///
	/// **A UNICA LINHA DO JOGO CUJO MULTIPLICADOR BASE SAI DA CLASSE** -- ver
	/// <see cref="FormaDef.BaseDaClasse"/>. Omega 1,30x, Epsilon 2,4x, Low-Class 3x
	/// (`statheran.dm:26-42`), e a inversao e o desenho do original: o Omega acende quase 7x mais
	/// tarde (`RolarHeran`) e transforma pior, pagando isso com stats muito melhores.
	/// ==================================================================================
	/// </summary>
	Heran,

	/// <summary>
	/// ============================ O ALIEN -- `Alien_Transformations.dm` ============================
	/// Duas formas, 2x e 4x, sem maestria e sem raiva: gates puros de BP em cima de uma skill
	/// comprada (`alien/transformation`, que escreve `hasayyform = 2`). E a linha mais simples do
	/// catalogo, e serve de contraste -- ela mostra que "forma nova" pode nao custar nada alem das
	/// duas entradas quando o original tambem nao cobra nada alem do numero.
	/// ==============================================================================================
	/// </summary>
	Alien,
}

/// <summary>
/// QUE CORPO ESTA FORMA DESENHA. E um SIMBOLO, e nao um caminho -- quem traduz pra `res://` e o
/// cliente (<see cref="Jandirus.Client.CorposDeForma"/>), que e a regra da casa: o Core nao
/// conhece o Godot.
///
/// ============================ POR QUE ISTO DEIXOU DE SER UM CAMINHO ============================
/// Ate aqui o campo era `string CorpoProprio = "res://.../SSj4_Body.tres"` -- um caminho fixo por
/// forma, escrito no Core. Isso resolvia o SSJ4 e o Oozaru porque a arte deles e a MESMA pra todo
/// mundo: a pelagem vermelha e vermelha em qualquer jogador, e o macaco e o mesmo macaco.
///
/// O corpo MUSCULOSO nao e assim. `supersaiyanbuff.dm:357-363` (`apply_ussj_body()`) escolhe o
/// arquivo pela PELE de quem se transformou:
///
///     if(icon == 'White Male.dmi' || icon == 'BaseWhiteMale.dmi') musc = 'White Male Muscular 3.dmi'
///     else if(icon == 'Tan Male.dmi'   ...)                       musc = 'Tan Male Muscular.dmi'
///     else if(icon == 'Black Male.dmi' ...)                       musc = 'BlackMaleMuscular3.dmi'
///
/// Ou seja: a forma diz **que corpo**, o personagem diz **de quem**. Um caminho no catalogo so
/// consegue dizer a primeira metade -- pra dizer as duas ele teria que virar tres campos (claro,
/// moreno, negro) e mentiria no dia em que entrasse um quarto tom, ou uma raca com outra folha.
///
/// Com o simbolo, o catalogo volta a dizer so o que ele sabe ("esta forma incha o corpo") e a
/// pergunta "inchado de que cor?" e respondida por quem tem a resposta: o cliente, olhando a folha
/// que o boneco JA esta vestindo. E o mesmo idioma do <see cref="FormaDef.SufixoDoCabelo"/>, que
/// tambem nao guarda arquivo nenhum -- guarda `"SSJ4"` e deixa o `CabelosDeForma` achar o penteado
/// daquele jogador.
/// ==============================================================================================
/// </summary>
public enum CorpoDeForma
{
	/// <summary>O corpo continua o da raca. E a maioria: toda a escada ate o SSJ3, o God Ki inteiro.</summary>
	Nenhum,

	/// <summary>
	/// O CORPO INCHADO, escolhido pela pele do jogador. `apply_ussj_body()` (grades) e
	/// `lssjbuff.dm:97,103` (Legendary).
	///
	/// E o unico valor deste enum cuja arte DEPENDE do personagem -- e por isso que o enum existe.
	/// Quem nao tem folha musculosa pra a pele dele (toda mulher, e toda raca que nao seja de
	/// corpo humano) simplesmente nao troca: ver <see cref="Jandirus.Client.CorposDeForma"/>.
	/// </summary>
	Musculoso,

	/// <summary>A pelagem vermelha do SSJ4. `supersaiyanbuff.dm:245` -- `saiyan4body`.</summary>
	Ssj4,

	/// <summary>O macaco de 96x96. `Oozaru.dm:137-139` -- `container.icon = 'oozaruhayate.dmi'`.</summary>
	Oozaru,

	/// <summary>O macaco dourado. Mesma folha, pelagem dourada -- `goldoozaruhayate.dmi`.</summary>
	OozaruDourado,

	/// <summary>
	/// ============================ O CORPO QUE O **JOGADOR** ESCOLHEU PRA ESTE DEGRAU ============================
	/// `icer_poll_icon()` (`IcerTransform.dm:129-137`): `if(1) icon = form1icon`, `if(2) icon =
	/// form2icon`... Cada forma do Frost Demon e um SPRITE DIFERENTE, e os sprites nao sao do
	/// catalogo -- sao os que o jogador escolheu na criacao (`Appearance.FormasDeFrost`, tres slots
	/// pro normal e sete pro Mutante).
	///
	/// ============================ POR QUE UM VALOR SO, E NAO SETE ============================
	/// Este e o SEGUNDO valor deste enum cuja arte depende do personagem, e ele e irmao do
	/// <see cref="Musculoso"/> ate na frase: **a forma diz QUE corpo, o personagem diz DE QUEM**. O
	/// que muda e por onde o personagem responde -- o Musculoso resolve pela PELE que o boneco ja
	/// veste, e este resolve pelo SLOT que o degrau aponta.
	///
	/// Sete valores (`Frost1`..`Frost7`) diriam a mesma coisa sete vezes e ainda obrigariam quem
	/// traduz a manter uma tabela paralela a `FormasDeFrost.DegrausDe`; com um valor so, o indice sai
	/// da propria entrada (<see cref="Catalogo.DegrauDoFrost"/> -> a `Ordem`, que nesta linha E o
	/// `fd_form`) e a lista de corpos sai da ficha. Ver `Jandirus.Client.CorposDeForma`.
	///
	/// **E O SLOT 0 E SEMPRE O REPOUSO** -- e e o que faz a aparencia de criacao e a forma de combate
	/// nao virarem duas verdades. `VisualCatalog.CorpoSprite` desenha `FormasDeFrost[0]` como "o
	/// corpo desta pessoa"; o degrau de repouso (<see cref="Catalogo.PisoDaEscada"/>) e o PRIMEIRO da
	/// lista de degraus da classe (1 pro Mutante, 5 pro normal), entao a camada que esta forma veste
	/// no repouso e o MESMO arquivo que o corpo ja usa. Elas concordam por construcao, e nao porque
	/// alguem lembrou de sincroniza-las.
	/// ==========================================================================================================
	/// </summary>
	FrostEscolhido,

	/// <summary>
	/// O CORPO DA SUPER PERFEITA -- `Bio Android 4.dmi` (`body_custom.dm:249`).
	///
	/// Valor FIXO e nao "escolhido", ao contrario dos dois vizinhos de cima: ha uma folha so, ela nao
	/// depende da pele nem de escolha na criacao. Entra no enum mesmo assim porque o que o catalogo
	/// diz e "esta forma troca o corpo", e a traducao pro `res://` e do cliente
	/// (<see cref="Jandirus.Client.CorposDeForma"/>) -- o Core nao conhece o Godot.
	///
	/// **E ELE E O BURACO DO ORIGINAL FECHADO POR CONSTRUCAO.** No DM, `dnl_bio_hatch` escreve
	/// `form2icon` e `form3icon` e **esquece o `form4icon`** (`DNALabs.dm:492-493`): um bio de
	/// laboratorio que alcance a Super Perfeita faz `icon = null` e so nao vira quadrado quebrado
	/// porque `Stats.dm:210` tem um fallback. Aqui a folha e do CATALOGO e nao da ficha, entao nao ha
	/// campo pra alguem esquecer de preencher.
	/// </summary>
	BioSuperPerfeito,
}

/// <summary>Como a maestria vira multiplicador.</summary>
public enum Curva
{
	/// <summary>
	/// DEGRAUS. O valor pula de um pra outro nos limiares de <see cref="FormaDef.Limiares"/>.
	/// Cobre o `stepped_mastery_mult` (SSJ1 2x->6x), o fixo (um valor so) e o Future SSJ.
	/// </summary>
	PorLimiar,

	/// <summary>RAMPA linear do primeiro ao ultimo valor conforme a maestria de 0 a 100.</summary>
	Rampa,
}

/// <summary>
/// UMA FLAG DE SKILL EXIGIDA, e o minimo que ela precisa valer. Ver <see cref="FormaDef.PedeFlag"/>.
///
/// O <paramref name="Campo"/> e o nome do `mob/var` do DM, tal e qual -- `"snamek"`, `"hasayyform"`
/// --, porque e assim que ele chega do `skills.json` e e assim que o `Fighter.FlagsDeSkill` o
/// guarda. Renomear pra portugues aqui obrigaria a uma tabela de traducao entre o extrator e o
/// catalogo, que e uma segunda copia da mesma tabela.
/// </summary>
public readonly record struct FlagDeSkill(string Campo, double Minimo = 1);

/// <summary>
/// TUDO que o jogo precisa saber de uma forma. **Uma forma nova e uma entrada nova aqui e mais
/// nada** -- e o motivo deste arquivo existir.
/// </summary>
public sealed class FormaDef
{
	// ================================ IDENTIDADE ================================

	/// <summary>Id de codigo, estavel e legivel: "ssj1", "blue_evolution", "primal_legendary4_full_power".</summary>
	public required string Id;

	/// <summary>
	/// O NUMERO QUE VAI NA REDE E NO SAVE.
	///
	/// ============================ POR QUE NAO E SO O ID DE TEXTO ============================
	/// Este projeto ja tem a cicatriz de uma mudanca de formato de save que APAGARIA contas (ver a
	/// nota de cor de roupa). As seis formas que existiam antes deste rework eram salvas como
	/// inteiro -- 0, 10, 15, 16, 20, 30, 40 -- e esses numeros continuam aqui, IDENTICOS. Um save
	/// antigo carrega sem conversao nenhuma; o codigo e que passou a falar por id de texto.
	/// =======================================================================================
	/// </summary>
	public required ushort IdRede;

	public required LinhaDeForma Linha;

	/// <summary>
	/// POSICAO NA LINHA. E daqui que sai o "de onde se sobe": o degrau anterior e simplesmente a
	/// entrada da MESMA linha com a maior <see cref="Ordem"/> abaixo desta.
	///
	/// Por isso acrescentar um estagio no meio nao mexe em mais nada: entre o 3 e o 4, poe 35.
	/// </summary>
	public required int Ordem;

	public required string Nome;
	public string Desc = "";

	// ================================ PODER ================================

	public Curva Como = Curva.PorLimiar;

	/// <summary>Os multiplicadores. Um elemento so = multiplicador fixo.</summary>
	public required double[] Mult;

	/// <summary>
	/// Em que maestria (0-100) cada valor de <see cref="Mult"/> liga. So vale em
	/// <see cref="Curva.PorLimiar"/>. Nulo = <see cref="Degraus"/> (o `stepped_mastery_mult`).
	/// </summary>
	public double[]? Limiares;

	/// <summary>
	/// A CURVA DESTA FORMA NAO SAI DA MAESTRIA DELA -- sai do KI DIVINO. Nulo = sai, como todas.
	///
	/// ============================ POR QUE NAO DEU PRA REUSAR O `Mult`/`Limiares` ============================
	/// O motor de degraus (<see cref="Catalogo.MultBruto"/>) chaveia numa unica variavel, e a do
	/// Mistico sao DUAS -- a linhagem (16x pra qualquer um, 18x pro Prodigial) e a maestria de ki
	/// divino (22x ao despertar, subindo ate 32x aos 33%). Duas variaveis independentes nao cabem
	/// numa tabela de uma dimensao, e a segunda metade ainda e RAMPA em cima de um DEGRAU, que e
	/// justamente a mistura que a <see cref="Curva"/> nao faz.
	///
	/// O `ChaveDoLimiar` **nao** servia: apesar do nome, ele escolhe qual limiar de BP pessoal
	/// (<see cref="LimiaresPessoais"/>) abre a forma -- e porta, nao curva.
	///
	/// Entao a curva virou UM campo com os numeros todos DENTRO dele (nenhum deles em codigo) e uma
	/// funcao so que o le, <see cref="Catalogo.MultPorGodKi"/>. Nao ha `if` por id em lugar nenhum:
	/// qualquer forma futura que escale com ki divino preenche este campo e pronto.
	/// ==================================================================================================
	/// </summary>
	public CurvaDeGodKi? EscalaComGodKi;

	/// <summary>
	/// RAMO LATERAL: esta forma sai do tronco e nao volta pra ele.
	///
	/// ============================ OS GRADES SAO UM DESVIO, NAO UM DEGRAU ============================
	/// Grade 2 e Grade 3 saem do SSJ1 -- e o SSJ2 tambem. Os tres sao IRMAOS, nao uma fila. Sem esta
	/// marca o SSJ2 acharia que vem do Grade 3 (a Ordem mais alta abaixo dele) e herdaria o piso
	/// dele SEMPRE, inclusive de um grade que o jogador nunca destravou -- um SSJ2 valendo 6x com
	/// zero de maestria, 50% acima do certo. A bancada pegou isto na primeira execucao.
	///
	/// O ramo AINDA TEM anterior (o grade le a maestria do SSJ1 pra abrir); o que ele nao e e o
	/// anterior DE NINGUEM.
	/// ==========================================================================================
	/// </summary>
	public bool ForaDoTronco;

	/// <summary>
	/// PISO SOBRE A FORMA ANTERIOR: "nunca vale menos que a anterior + isto".
	///
	/// `ssj_effective_mult()`. Sem ele um SSJ3 de raca nerfada ficaria MAIS FRACO que o proprio
	/// SSJ2 dela, e subir a escada puniria. 0 = sem piso, e os grades sao 0 de proposito (o SSJ1
	/// dominado, 6x, supera os dois).
	/// </summary>
	public double PisoSobreAnterior;

	/// <summary>
	/// O multiplicador da linha diluida (meio-Saiyajin). So o SSJ1 tem: `ssj1base` 2 -> 1,35.
	/// Nulo = a raca diluida usa o mesmo <see cref="Mult"/>.
	/// </summary>
	public double[]? MultDiluido;

	/// <summary>
	/// ============================ O MULTIPLICADOR BASE SAI DA **CLASSE** ============================
	/// Nulo em 38 das 41 entradas, e so a linha <see cref="LinhaDeForma.Heran"/> o preenche. Quando
	/// preenchido, o <see cref="Mult"/> deixa de ser o multiplicador e passa a ser a CURVA dele --
	/// fatores relativos que este numero escala.
	///
	/// ============================ POR QUE UM CAMPO E NAO SEIS ENTRADAS ============================
	/// No DM o Heran tem `mob/var/ssjmult`, escrito no nascimento pelo `special_info()` da genetica
	/// (`statheran.dm:26-42`): Omega 1,30 / Epsilon 2,4 / Low-Class 3. Ou seja **o multiplicador nao e
	/// da forma, e da pessoa** -- duas pessoas na MESMA forma multiplicam diferente, o que nenhuma
	/// outra linha deste jogo faz.
	///
	/// A alternativa era o precedente do Rose/Blue -- formas IRMAS na mesma <see cref="Ordem"/>, uma
	/// por classe (ver `EstaEmOuAcimaDe`). Ele funcionaria, e custaria SEIS entradas, seis ids de
	/// rede, seis cinematicas identicas e um `Anterior` que atravessa classe (o `heran2` da Omega
	/// acharia o `heran1` da Low-Class como degrau anterior, porque `Anterior` desempata por Ordem e
	/// nao por classe). Um campo que le o perfil diz a mesma coisa com duas entradas, e e o mesmo
	/// idioma do <see cref="EscalaComGodKi"/>, que ja existe justamente pra "o numero desta forma
	/// depende de quem esta nela".
	///
	/// A CLASSE QUE NAO ESTIVER NO MAPA CAI NO `""` (chave vazia = o `else` do DM). No Heran esse
	/// `else` e o Epsilon, que e literalmente o ramo sem `if` do `special_info()`.
	/// ==========================================================================================
	/// </summary>
	public IReadOnlyDictionary<string, double>? BaseDaClasse;

	/// <summary>
	/// RAMPA DE COMBATE ESTILO LSSJ: `max(piso pela maestria, rampa do combate)`. A luta continua
	/// sobe o multiplicador do minimo ao maximo em ~3 min (`LSSJ_RAMP_TICKS 600`), e a maestria so
	/// define um PISO. lssjbuff.dm:184.
	/// </summary>
	public bool CombateSobeAoMaximo;

	/// <summary>
	/// BONUS DE COMBATE ESTILO LEGENDARY PRIMAL: `. *= 1 + min(combatTime/720,1) * 0.2`. E OUTRA
	/// mecanica -- aqui o combate MULTIPLICA por cima do que a maestria ja deu.
	/// supersaiyanbuff.dm:572.
	/// </summary>
	public double BonusDeCombate;

	/// <summary>Fracao do Ki maximo drenada por segundo, por degrau de maestria.</summary>
	public double[] Dreno = [0];

	// ================================ PORTAS ================================

	/// <summary>
	/// BP BASE de fabrica. NAO E O NUMERO DE NINGUEM: cada personagem sorteia o proprio ao nascer
	/// (`statsaiyan.dm:45-77`) -- ver <see cref="LimiaresPessoais"/>. Este e o ponto de partida.
	/// </summary>
	public double PortaBp;

	/// <summary>Qual limiar pessoal manda nesta forma ("ssjat", "ssj2at"...). Vazio = usa o de fabrica.</summary>
	public string ChaveDoLimiar = "";

	/// <summary>
	/// ============================ A FORMA QUE SE **COMPRA** ANTES DE PODER TER ============================
	/// Nulo = a forma nao pede skill nenhuma, que e o caso das 38 primeiras entradas: as escadas de
	/// sangue vem do CORPO (raca, classe, linhagem) e as divinas vem de ENSINO, que ja tem canal
	/// proprio (`PedeGodKi`, `PedeProficienciaUi`, `SoPorConcessao`).
	///
	/// Tres entradas quebram isso e as tres sao do mesmo jeito no DM: `snamek()` so roda `if(snamek)`
	/// e `Alien_Trans()` so roda `if(hasayyform)` -- vars que nascem em ZERO e que apenas o
	/// `after_learn()` de uma skill comprada escreve (`namekian.dm:37-38`, `alien.dm:32-33`). Sem
	/// isto, comprar a skill nao faria nada e a forma seria de graca -- os dois defeitos ao mesmo
	/// tempo.
	///
	/// ============================ E ELE NAO INVENTA CANAL: O CANAL JA ESTAVA EXTRAIDO ============================
	/// `EfeitosDeSkill` ja separa QUATRO canais do `after_learn`, e o quarto -- ATRIBUICAO -- e
	/// exatamente este: `Skill.Flags` traz `"snamek=1"` e `"hasayyform=2"` direto do
	/// `Assets/Data/skills.json`, extraidos do DM pelo pipeline, e o `Aplicar` os deposita em
	/// <see cref="Jandirus.Core.Stats.Fighter.FlagsDeSkill"/> -- **inclusive os que o `Fighter` nao
	/// tem como campo**, que e o caso destes dois. Nao ha numero digitado a mao aqui: o catalogo so
	/// diz o NOME da flag e o minimo, e quem preenche o valor e o livro de skills do jogador.
	///
	/// O MINIMO EXISTE POR CAUSA DO ALIEN, e ele e literal: a skill escreve `hasayyform = 2`, a 1a
	/// forma cobra `if(hasayyform)` (>= 1) e a 2a cobra `if(hasayyform == 2)`. Um booleano perderia a
	/// segunda metade, e o campo unico com minimo cobre as duas sem um segundo canal.
	/// ========================================================================================================
	/// </summary>
	public FlagDeSkill? PedeFlag;

	/// <summary>Maestria exigida NO DEGRAU ANTERIOR. 0 = nao pede.</summary>
	public double PedeMaestria;

	/// <summary>
	/// DE QUAL FORMA ler a maestria de <see cref="PedeMaestria"/>. Vazio = a anterior da linha.
	///
	/// Existe pelas camadas divinas: o Blue Evolution e "Grade 2 + ki divino", entao ele pede o que
	/// o Grade 2 pede -- 50% de maestria no SSJ1 -- e nao 50% de maestria no Blue. Sem este campo a
	/// regra "Evolution = Grade 2 + god ki" so estaria no comentario.
	/// </summary>
	public string PedeMaestriaDe = "";

	/// <summary>Linhagem exigida ("Primal Saiyan"). Vazio = qualquer.</summary>
	public string PedeLinhagem = "";

	/// <summary>Classes que podem esta forma. Vazio = qualquer.</summary>
	public string[] PedeClasseUmaDe = [];

	/// <summary>
	/// ORIGENS que podem esta forma -- LINHAGEM **ou** CLASSE. Vazio = qualquer.
	///
	/// ============================ POR QUE OS DOIS CAMPOS ============================
	/// A "de onde voce vem" do Saiyajin mora em lugares diferentes conforme a raca, e isso e do
	/// original: o Saiyajin puro escolhe LINHAGEM (`SaiyanLineage`: "Saiyan" ou "Primal Saiyan") e
	/// o meio-Saiyajin escolhe CLASSE (`stathalfbreed.dm:71`: "New Generation", "Future Lineage" ou
	/// "Prodigial" -- e o proprio comentario de `Birth.RollClass` diz "a escolha JA e a classe").
	///
	/// Sao dois campos guardando UMA ideia. Perguntar aos dois e o que deixa a regra "God e Blue
	/// sao do Saiyajin de linhagem normal e do meio-Saiyajin New Generation" caber numa linha, em
	/// vez de virar um `if` de raca no meio do gate.
	/// ============================================================================
	/// </summary>
	public string[] PedeOrigemUmaDe = [];

	/// <summary>
	/// CLASSES QUE **NAO** TEM ESTA FORMA porque tem a variante dela.
	///
	/// "Rose no lugar do Blue" (`statsaiyan.dm:157`) e "o Prodigial NAO tem SSG/Blue"
	/// (`godki.dm:349`) sao os dois casos. Sem este campo a classe ficaria com as duas versoes do
	/// mesmo degrau -- e escolher entre 32x e 32x nao e escolha, e ruido na tela.
	/// </summary>
	public string[] ProibidoParaClasse = [];

	/// <summary>
	/// FORMA DE OUTRA LINHA que precisa ter sido despertada. E o pre-requisito do SSJ4: no DM ele
	/// so existe saindo do Oozaru Dourado, e isso estava codificado em `ApeshitRevert`. Aqui e um
	/// campo -- o dia que outra forma pedir outra forma, e uma string.
	/// </summary>
	public string PedeFormaDespertada = "";

	/// <summary>
	/// A FORMA EM QUE O CORPO PRECISA **ESTAR AGORA** pra entrar nesta. Vazio = qualquer.
	///
	/// ============================ AS DIVINAS SAO UMA CAMADA, NAO UMA ESCADA ============================
	/// Regra do dono, e ela e o desenho inteiro da linha God Ki: *"blue evolution/royal seria
	/// grade 2 + god ki, assim como o blue normal e o ssj + god ki"*. Ou seja nao se SOBE ate o
	/// Blue -- se esta em Super Saiyajin e se acende o ki divino por cima.
	///
	/// O DM diz o mesmo em codigo: `ssj` e `godki` sao vars SEPARADAS, e o `god_form_mult` le as
	/// duas ao mesmo tempo -- `if(ssj == 0 && lssj == 0) return 22` (SSG puro), senao 32 (Blue), e
	/// 56 quando `max(ssj,lssj) >= godki_ssj_cap`. O comentario de `godki.dm:347` fecha a conta:
	/// "Royale/Rose 2 56x (50%, so Elite/Kaio **via USSJ**)" -- e o USSJ e exatamente o Grade.
	///
	/// Quando este campo esta preenchido, o degrau anterior da propria linha e cobrado por
	/// DESPERTAR e nao por estar nele: ninguem esta em SSG e em Blue ao mesmo tempo.
	/// ============================================================================================
	/// </summary>
	public string[] PedeFormaAtual = [];

	/// <summary>Maestria de God Ki exigida (`GODKI_BLUE_PCT` 33, `GODKI_ROYALE_PCT` 50).</summary>
	public double PedeGodKi = -1;

	/// <summary>
	/// DEGRAU MINIMO DE BIO-ANDROIDE (`bio_stage`). 0 = nao pede, que e o caso de 41 das 42 entradas.
	///
	/// Preenchido so pela Super Perfeita, e ele e a metade `cell3 == 1 &amp;&amp; form3cantrevert` do
	/// gate do `Cell4()` (`CellFormBuff.dm:74`). Um campo e nao um `if` por id no `Avaliar` pelo
	/// motivo de sempre neste arquivo: quem decide gate e o DADO, e o dia em que o Cell Jr. ou uma
	/// segunda forma bio pedir outro degrau, e um numero e nao um ramo novo.
	/// </summary>
	public int PedeEstagioBio;

	/// <summary>
	/// ============================ O BIO DE LABORATORIO NAO CHEGA NESTA FORMA PELO CAMINHO NORMAL ============================
	/// Uma unica entrada a preenche (`ssj2`), e ela e o porte de uma regra que o DM escreve em TRES
	/// lugares porque la a variavel `hasssj2` pode ser escrita de fora:
	///
	///   * `SSj2()` (`supersaiyanbuff.dm:417-421`) **zera** um `hasssj2` concedido por engano e
	///     recusa -- *"seu nucleo bio exige um gatilho mais extremo que a raiva"*;
	///   * o ensino Mestre-Aluno recusa (`MasterStudent.dm:255, 338`);
	///   * o login REVOGA retroativamente quem pegou pela raiva (`DNALabs.dm:714-717`), e reverte.
	///
	/// **SEM ISTO O SISTEMA INTEIRO DO SSJ2 DO BIO SERIA LETRA MORTA.** Ele nasce com `canSSJ`, e
	/// `canSSJ` abre a escada Saiyajin: o SSJ2 esta no tronco dela, e tronco se abre com RAIVA. Um
	/// bio veria um amigo morrer e pularia a forma perfeita, o SSJ1 dominado, o BP e a propria morte
	/// -- todo o custo da unica transformacao que e SO dele.
	///
	/// A EXCECAO E "JA DESPERTOU": o unico lugar do jogo que libera `ssj2` pra um bio de laboratorio
	/// e o despertar pela morte (`GameServer.DespertarSsj2DoBio`). Depois dele a forma e dele como e
	/// de qualquer Saiyajin -- inclusive pra reentrar, que era o bug que o proprio DM documenta em
	/// `mst_form_apply` ("ficava com uma forma na qual nao conseguia reentrar").
	///
	/// **E O SSJ3 SAI DE GRACA ATRAS DESTA MESMA PORTA**, sem campo nenhum: ele pede 50% de maestria
	/// no SSJ2 (<see cref="PedeMaestriaDe"/>), e nao se treina uma forma que nao se tem.
	/// ========================================================================================================================
	/// </summary>
	public bool NegadaAoBioDeLaboratorio;

	/// <summary>
	/// FORMAS QUE **BLOQUEIAM** esta -- o oposto do <see cref="PedeFormaAtual"/>. Vazio = nenhuma.
	///
	/// ============================ ELE EXISTE PORQUE NO DM ISTO E UMA COLISAO DE VARIAVEL ============================
	/// `Cell4()` so roda `if(!ssj ...)` (`CellFormBuff.dm:74`): estar em qualquer degrau Super
	/// Saiyajin fecha a Super Perfeita. E a reciproca acontece por acidente e nao por regra -- a
	/// Super Perfeita FAZ `ssj = 1` (`:75`), entao o bloco do SSJ em `Transformation Controls.dm:51`
	/// passa a valer e o SSJ2 SOBRESCREVE a Super Perfeita, trocando um 8x por um 1,75x, calado.
	///
	/// O port nao tem uma `ssj` compartilhada pra colidir (a forma atual e um id de texto), entao o
	/// bloqueio precisa ser DITO. Dizer so o lado do DM (`!ssj`) deixaria o lado ruim vivo; por isso
	/// este campo e conferido nos DOIS sentidos -- ver o passo 4b do `EstadoDeForma.Avaliar`, que
	/// tambem recusa o SSJ2 de quem esta em Super Perfeita. **Isto e um conserto declarado, nao uma
	/// copia**: o original perde a forma mais forte do bio pela ordem em que dois `if` rodam.
	/// ==========================================================================================================
	/// </summary>
	public string[] ProibidoComFormaAtual = [];

	/// <summary>
	/// ESTA FORMA NAO SE CONQUISTA: ALGUEM A CONCEDE. Enquanto ninguem conceder, ela nao existe
	/// pro personagem -- nem treinando, nem com BP infinito, nem estando na forma de baixo.
	///
	/// ============================ E O GANCHO DO RITUAL, E ELE JA ESTA PRONTO ============================
	/// O Mistico e dado pelo RITUAL de um jogador Kaioshin (sistema ainda nao implementado). Quem
	/// concede e o servidor, chamando <see cref="EstadoDeForma.Liberar"/> com o id da forma -- o
	/// MESMO caminho que o Oozaru Dourado dominado ja usa pra abrir o SSJ4, e por isso ele nao
	/// precisou de campo novo no save: <see cref="EstadoDeForma.Liberadas"/> ja persiste.
	///
	/// A concessao SUBSTITUI a checagem de linha (<see cref="Catalogo.LinhasAbertas"/>): quem
	/// recebeu o ritual tem a linha aberta por ter recebido, e nao por raca, classe ou ki divino.
	/// Sem isso o Mistico so serviria pra quem ja tem uma escada, quando a regra do dono e que
	/// **toda raca pode receber**.
	/// ================================================================================================
	/// </summary>
	public bool SoPorConcessao;

	/// <summary>Energia de Ultra Ego exigida (`UE_UNLOCK_*`).</summary>
	public double PedeEnergiaUe;

	/// <summary>Proficiencia de Ultra Instinct exigida.</summary>
	public double PedeProficienciaUi;

	// ================================ APARENCIA ================================

	/// <summary>
	/// A COR DA AURA -- a chama em volta do corpo, E SO ELA.
	///
	/// Ela ja mandou em tres desenhos (aura, contorno e raios) e por isso todo ajuste de cor puxava
	/// um defeito atras. O contorno e os raios saem hoje de <see cref="Catalogo.CorDoContorno"/> e
	/// <see cref="Catalogo.CorDosRaios"/>, que na maioria das linhas DERIVAM deste campo -- entao
	/// mexer aqui continua movendo os tres, o que mudou e que agora da pra mover um sem os outros.
	/// </summary>
	public string Aura = "ffd24a";

	/// <summary>
	/// A TINTA QUE A FORMA SOMA NO CABELO. **Vazio = nao pinta**, e vazio e a maioria.
	///
	/// Ele valia outra coisa ate a varredura das formas: era "a cor do cabelo desta forma", tinha um
	/// hexa em todas as 36 entradas e NINGUEM o lia -- as unicas mencoes fora deste arquivo eram duas
	/// bancadas conferindo que tinha 6 caracteres. Era campo morto descrevendo uma decisao que o
	/// desenho nao tomava.
	///
	/// Hoje ele e a entrada de <see cref="Catalogo.CorDoCabelo"/>, e por tabela tambem a de
	/// <see cref="Catalogo.ModoDoCabelo"/> e a de <see cref="Catalogo.CorDoRabo"/> -- ver os tres.
	/// Preenche-lo numa forma que troca de sprite (a escada Saiyajin) faria o dourado voltar por
	/// cima de arte que ja e dourada, que e exatamente o que o dono vetou.
	/// </summary>
	public string Cabelo = "";

	/// <summary>
	/// A FORMA TEM CORPO PROPRIO? Um SIMBOLO -- ver <see cref="CorpoDeForma"/> pro porque de nao ser
	/// mais um caminho.
	///
	/// ============================ O SSJ4 NAO E SO CABELO ============================
	/// `supersaiyanbuff.dm:245` -- `container.updateOverlay(/obj/overlay/body/saiyan/saiyan4body)`.
	/// O SSJ4 e o SSJ4 Full Power TROCAM O CORPO (a pelagem vermelha), e nao so a cor do cabelo; e
	/// por isso que a linha do Oozaru desemboca neles. Eu tinha portado a escada inteira tratando
	/// toda forma como "cabelo + aura", e o SSJ4 saia com o corpo base.
	///
	/// <see cref="CorpoDeForma.Nenhum"/> = o corpo continua o da raca, que e o caso de toda a
	/// escada ate o SSJ3.
	/// ==========================================================================
	///
	/// ============================ E O `.Length > 0` VIROU PERGUNTA POR VALOR ============================
	/// Duas derivacoes deste arquivo perguntavam "tem corpo proprio?" querendo dizer **"e um dos tres
	/// SSJ4?"** -- <see cref="Catalogo.CorDoRabo"/> (a folha do SSJ4 ja desenha o rabo) e
	/// <see cref="Catalogo.CorDoOlho"/> (o amarelado). Com um campo booleano-por-acidente isso
	/// funcionava porque so o SSJ4 e o Oozaru o preenchiam; o dia em que o corpo MUSCULOSO entrasse
	/// no mesmo campo, os nove degraus inchados sairiam **de olho amarelo e sem rabo**, calados.
	///
	/// O enum e o que impede isso: as duas passaram a perguntar pelo VALOR, e a pergunta ficou mais
	/// perto do que elas queriam dizer desde o comeco.
	/// ================================================================================================
	/// </summary>
	public CorpoDeForma Corpo = CorpoDeForma.Nenhum;

	/// <summary>
	/// QUAL VARIANTE DE CABELO esta forma usa -- o sufixo da pasta `SSJ Hairs/`. Vazio = o normal.
	///
	/// O BYOND troca o overlay INTEIRO do cabelo (`removeOverlay(hair)` +
	/// `updateOverlay(ssj/ssj1)`); tingir o penteado normal de dourado nao e a mesma coisa: o Super
	/// Saiyajin tem o cabelo EM PE, nao amarelo. Ver <see cref="Jandirus.Client.CabelosDeForma"/>,
	/// que resolve o nome do arquivo e herda o degrau de baixo quando a variante nao existe.
	/// </summary>
	public string SufixoDoCabelo = "";

	/// <summary>
	/// FORCA DA CINEMATICA (1 a 5). Era um `switch` sobre o numero da forma no cliente
	/// (`World.cs`), e por isso toda forma nova nascia sem cinematica ate alguem lembrar de
	/// acrescentar o caso. Virou dado.
	/// </summary>
	public int Intensidade = 1;

	/// <summary>
	/// OS RAIOZINHOS QUE CORREM NO CORPO: 0 = nenhum, 1 = leve, 2 = cheio.
	///
	/// ============================ SETE FORMAS NO JOGO INTEIRO ============================
	/// Regra do dono, fechada em cinco mensagens: *"ssj n tem efeitos de raio"*, *"raiozinhos
	/// somente o lssj 2 do primal legendary o resto n tem raio"*, *"grade2 e grade3 nao tem raio
	/// mesmo, pode zerar"*, *"limit breaker tem raios vermelhos"* e -- a ultima, que acrescentou uma
	/// linha inteira -- *"Mistico: tudo igual a base, MAS ele TEM os raiozinhos, que estao faltando"*:
	///
	///   * <c>ssj2</c>               -> 1 (leve)
	///   * <c>ssj3</c>               -> 2 (cheio)
	///   * <c>primal_legendary2</c>  -> 2 (cheio)
	///   * <c>primal_legendary3</c>  -> 2 (cheio)
	///   * <c>ssj4_limit_breaker</c> -> 2 (cheio)
	///   * <c>mistico</c>            -> 1 (leve)
	///   * <c>beast</c>              -> 1 (leve)
	///
	/// Todo o resto e ZERO, inclusive os grades, o `future_ssj`, a linha Legendary comum inteira, o
	/// Blue, o Rose, o Ultra Ego e o `primal_legendary4_limit_breaker` -- que nao acompanha o irmao
	/// Saiyajin: o dono nomeou UM Limit Breaker, e nomear um nao e nomear os dois.
	///
	/// ============================ A LINHA DO MISTICO ENTROU DEPOIS, E COM FOLHA PROPRIA ============================
	/// Os cinco de cima saem do `electrictyeffects` da escada Saiyajin. Estes dois saem de OUTRO
	/// arquivo: `Electric_Mystic.dmi`, vestido pelo `/obj/overlay/effects/MysticEffect`
	/// (`Mystic.dm:20-23`) no `Buff()` do Mistico (`:37`) e mantido no do Beast (`:112`, com o
	/// comentario *"os raios do Mistico continuam no Beast"* escrito na propria linha). O `DeBuff()`
	/// de cada um o remove (`:56` e `:126`).
	///
	/// UMA FOLHA CADA, entao volume 1 pelos dois -- a mesma conta do SSJ2. E o BEAST **nao foi
	/// pedido**: o dono nomeou o Mistico. Ele entrou porque e o mesmo objeto de overlay do mesmo
	/// arquivo, e porque a linha perderia efeito ao subir se so o degrau de baixo faiscasse -- o mesmo
	/// argumento que ja fixou o volume do `primal_legendary3` (ver o fim deste cabecalho).
	///
	/// A COR DELES E A UNICA DO JOGO QUE NAO E ESCADA-DE-SANGUE NEM AURA -- ver `Catalogo.CorDosRaios`.
	///
	/// ============================ POR QUE O MAPA DO DM NAO VALE INTEIRO ============================
	/// `supersaiyanbuff.dm:208-257` acende `electrictyeffects` em mais degrau do que isto -- nos
	/// grades (`reg_elec`, `:221`) alem destes cinco. O catalogo copiava aquele mapa; o dono cortou
	/// os grades. Deixo o caminho anotado porque a pergunta "de onde saiu isso?" vai voltar, e a
	/// resposta e que eles SAIRAM de la de proposito, e nao que ninguem olhou.
	///
	/// O VOLUME, esse, continua saindo do original -- e a conta e QUANTAS FOLHAS o DM acende:
	///
	///   * `if(2)` (o SSJ2) acende UMA -- `updateOverlay(/obj/overlay/effects/electrictyeffects)`;
	///   * `if(3)` (o SSJ3) acende a MESMA e ainda soma `ElecAura1.dmi` e `Electric_Blue.dmi`;
	///   * `if(6)` (o Limit Breaker, `:254-257`) tem folha PROPRIA e sao duas: `AbsorbSparks.dmi` e
	///     `Lightning - Blue.dmi` (`EffectLayer.dm:24-29`), as duas marcadas `temporary = 0`, ou
	///     seja crepitar CONSTANTE e nao um lampejo de transformacao.
	///
	/// Uma folha contra tres: por isso o SSJ2 e 1 e o SSJ3 e 2, e nao os dois no mesmo numero.
	///
	/// E O `primal_legendary2` E O PROPRIO `if(3)`. O Legendary Primal reusa a var `ssj` com outra
	/// semantica (`MasterStudent.dm:197`: "ssj 2 = LSSJ, 3 = LSSJ2"), entao o LSSJ2 dele cai no
	/// MESMO galho do switch que o SSJ3 -- mesma folha, mesmo volume. Ele nao esta em 2 por
	/// simetria de enfeite; ele esta em 2 porque no original ele E aquele caso.
	///
	/// ============================ O UNICO VOLUME QUE NAO E LITERAL ============================
	/// O `primal_legendary3` (o `if(3.5)`, `:239-243`) acende UMA folha so, o que pela conta acima o
	/// poria em 1 -- ABAIXO do `primal_legendary2`, que fica um degrau embaixo dele. Ele fica em 2, e
	/// o desvio esta declarado aqui porque a fidelidade nesse ponto e acidente e nao intencao: as
	/// duas folhas a mais do `if(3)` sao um remendo posterior, e o proprio DM diz isso na linha
	/// (*"eletricidade visivel do SSJ3 (pedido do user), como o SSJ2 tem"*). O remendo caiu no galho
	/// que o LSSJ2 divide com o SSJ3 e nunca chegou ao galho proprio do LSSJ3. Copia-lo ao pe da
	/// letra faria a escada PERDER faisca ao subir, que na tela le como defeito e nao como porte.
	/// ======================================================================================
	///
	/// AQUI ESTA SO O VOLUME. A COR sai de <see cref="Catalogo.CorDosRaios"/>: azul nas quatro
	/// primeiras e VERMELHO no `ssj4_limit_breaker`.
	/// ==========================================================================
	/// </summary>
	public int Raios;

	/// <summary>
	/// O NUMERO DO `mob/var/ssj` DO DM. So serve pro comando de admin `DirectSSJ:<n>`, que fala a
	/// lingua do original. -1 = nao tem equivalente la.
	/// </summary>
	public double NumeroDm = -1;

	/// <summary>
	/// MULTIPLICADOR ABSOLUTO: substitui a escada em vez de somar-se a ela.
	///
	/// As formas divinas do DM vivem em `god_form_mult`, e NAO em `ssjBuff` -- ha comentario
	/// explicito em `Fusion.dm:33` e `base.dm:132` dizendo isso. Elas nao empilham com o SSJ; elas
	/// SUBSTITUEM o valor.
	/// </summary>
	public bool Absoluta;

	/// <summary>
	/// OS STATS DESTA FORMA -- nulo quando ela nao mexe em nenhum (o caso de 38 das 40 entradas hoje).
	/// Ver <see cref="ModsDeForma"/>.
	/// </summary>
	public ModsDeForma? Mods;
}

/// <summary>
/// ============================ O QUE UMA FORMA FAZ COM OS STATS ============================
/// Ate aqui o catalogo sabia dizer quanto uma forma multiplica o BP, quanto ela drena, que porta ela
/// pede e de que cor e a aura -- 40 campos -- e NAO sabia dizer que ela deixa o sujeito mais forte e
/// mais lento. Nao havia campo. O unico lugar do port onde uma forma mexia em stat era o Oozaru, com
/// tres linhas escritas a mao dentro do servidor (`GameServer.Oozaru.cs:211-213`).
///
/// E o DM MEXE, em varias (`Oozaru.dm:127-129`, `grays.dm:90-92`, `IcerTransform.dm:292-294`,
/// `Giant Form.dm:72-74`, `Majin.dm:37`). Este record e a casa que faltava, e por isso ele e do
/// CATALOGO e nao dos grades: o Gray, o Icer Full Power, o Giant Form e o Heran tambem tem mods la,
/// e uma solucao so-pros-grades seria a primeira coisa a reescrever quando o proximo chegasse.
///
/// ============================ TUDO AQUI E FATOR, E O NEUTRO E 1 ============================
/// 1,60 = +60% no R do stat antes do `StatCap`; 0,60 = -40%. Nao ha soma: ver o cabecalho do canal
/// em `Fighter.cs` pra por que o `T*` do DM nao serve (o `Tspeed` esta morto la e aqui).
///
/// **O `StatCap` COMPRIME, e isso e do jogo, nao defeito daqui.** Medido em stat cru 20: fator 1,25
/// no `physoff` vira +9,6% de `Ephysoff`; 1,50 vira +16,6%; 1,80 vira +22,7%; e de 2,00 pra cima a
/// curva ja saturou. Numeros "grandes" aqui sao normais e nao sao inflacao.
///
/// ============================ NENHUM DELES E DA FAMILIA DO `powerlevel()` ============================
/// Nem multiplica, nem soma na base, nem se aplica no fim: eles nao entram naquela conta. Sao
/// `statify()`, que e a outra metade do personagem. O DM separa as duas do mesmo jeito, e a separacao
/// tem consequencia de projeto: um Grade 3 lento continua marcando o mesmo numero no scouter, o que e
/// exatamente o que "forte e desajeitado" quer dizer. O preco em BP de uma forma tem canal proprio
/// (<see cref="FormaDef.Mult"/> -> `ssjBuff` -> familia 1) e o preco em folego tambem
/// (<see cref="FormaDef.Dreno"/>).
/// ====================================================================================================
/// </summary>
/// <param name="Physoff">Ofensiva FISICA. Grays/Icer/Oozaru/Giant todos mexem nesta.</param>
/// <param name="Physdef">Defesa fisica. So o Giant Form do DM usa hoje.</param>
/// <param name="Kioff">Ofensiva de KI -- o outro "offensive" que o dono citou.</param>
/// <param name="Kidef">Defesa de ki. Sem consumidor no DM ainda; existe pra fechar o par.</param>
/// <param name="Tecnica">
/// A PONTARIA. Quem quer que a forma ERRE mais tem que mexer AQUI e nao na velocidade: a chance de
/// acertar e `Etechnique` de quem BATE contra `Espeed` de quem APANHA
/// (`calcs.dm:120`, `CombatMath.Pontaria`) -- a velocidade do atacante nao entra na conta.
/// </param>
/// <param name="Speed">
/// A VELOCIDADE, e ela paga em dois lugares: quem esta lento e ACERTADO mais (entra no divisor da
/// pontaria alheia -- medido, 0,60 faz o adversario acertar +30%) e ANDA mais devagar
/// (`MoveRules.SpeedStatFrom`). O que ela NAO faz e mudar a cadencia; pra isso ha o campo abaixo.
/// </param>
/// <param name="Cadencia">
/// QUANTOS SOCOS POR SEGUNDO. Divide, como o `hitspeedMod` do DM (`attack cmn.dm:100`): 0,60 = soca
/// 1,67x mais devagar. Canal separado da velocidade porque no jogo ELES SAO SEPARADOS -- o
/// `Eactspeed` esta cravado em 20 e nao escuta o `Espeed` (medido; ver `CombatMath.Cadencia`).
/// </param>
public sealed record ModsDeForma(double Physoff = 1, double Physdef = 1,
								 double Kioff = 1, double Kidef = 1,
								 double Tecnica = 1, double Speed = 1, double Cadencia = 1);

/// <summary>
/// A CURVA DE UMA FORMA QUE ESCALA COM O KI DIVINO -- os quatro numeros e mais nada.
///
/// Lida so por <see cref="Catalogo.MultPorGodKi"/>. O desenho e o do Mistico, ditado pelo dono:
///
///   * quem nao e da <see cref="Origens"/> fica no <see cref="FormaDef.Mult"/> da entrada (16x) --
///     o ritual da a forma, nao a escada;
///   * quem e, mas nunca despertou o ki divino, fica em <see cref="SemGodKi"/> (18x);
///   * quem despertou comeca em <see cref="AoDespertar"/> (22x) e sobe **gradualmente** ate
///     <see cref="NoTopo"/> (32x) quando a maestria divina chega em <see cref="TopoEm"/> (33%).
///
/// O TETO E ESTRUTURAL, nao um `if` esquecivel: a rampa e um `Clamp` de 0 a 1, entao 40%, 70% ou
/// 100% de ki divino caem todos em 32x. Era a parte da regra mais facil de perder num `switch` de
/// degraus, e por isso ela e a propria forma da conta.
/// </summary>
/// <param name="Origens">Linhagens **ou** classes que sobem esta curva. Vazio = ninguem sobe.</param>
public sealed record CurvaDeGodKi(string[] Origens, double SemGodKi, double AoDespertar,
								  double NoTopo, double TopoEm);

/// <summary>
/// ============================ DE QUE RAIVA UMA FORMA NASCE ============================
/// Dois degraus, e eles sao ORDENADOS de proposito: o gate pergunta `perfil.Raiva >= exigida`, e
/// nunca `==`. Quem viu um amigo MORRER certamente viu um amigo cair -- com igualdade estrita, o
/// luto FECHARIA o Wrathful, que e o oposto do que o dono descreveu. A ordem e a regra; ela nao e
/// enfeite de enum, e quem reordenar isto inverte o jogo sem tocar em nenhum `if`.
///
/// ============================ OS DOIS GATILHOS SAO OS DO DM ============================
/// La eles sao o MESMO proc com um parametro -- `mob/proc/Do_Anger_Stuff(var/extreme = 0)`
/// (`Murder.dm:110`) --, e e por isso que aqui sao um nivel e nao dois sistemas:
///
///   * <see cref="Lendaria"/> -- `Do_Anger_Stuff(0)`, de `KO.dm:37`: um amigo foi NOCAUTEADO por
///     um inimigo na sua view. O `koByEnemy` de la (`KO.dm:30-33`) ainda exige `combatTag ||
///     IsInFight`, pra um `lastDamager` velho nao transformar um desmaio por gravidade em luta.
///   * <see cref="Extrema"/> -- `Do_Anger_Stuff(1)`, de `Death.dm:81` (amigo MORTO por um inimigo,
///     com o comentario *"friend was KILLED by an enemy -> EXTREMELY enraged"*) e de
///     `MajinSaga.dm:173` (amigo ABSORVIDO -- ve-lo sumir vale o mesmo que ve-lo morrer). **O
///     segundo continua sem chamador aqui**, e o motivo MUDOU -- ver o bloco logo abaixo.
///
/// ============================ A ABSORCAO EXISTE; ABSORVER AMIGO E QUE NAO ============================
/// Este texto dizia *"a absorcao do Majin nao existe neste port; quando a saga vier, ela chama o
/// mesmo gancho"*. A saga VEIO (`Core/Npc/Sagas.cs` + `GameServer.Sagas.cs`) e trouxe absorcao
/// junto: `AbsorverCompanheiroCaido` e o `cell_absorb_contact` do original, com o chefe subindo de
/// degrau em cima do companheiro nocauteado.
///
/// SO QUE ELA COME CHEFE, E NAO AMIGO. A presa e sempre outro `EstadoDoChefe` do mesmo elo -- um
/// NPC de roteiro --, e o luto deste arquivo e entre PESSOAS que se conhecem (o convivio, por
/// assinatura de conta). Ninguem nunca ve um amigo ser absorvido, entao o gancho continua mudo com
/// razao, e nao por fiacao esquecida.
///
/// O DIA EM QUE A PRESA PUDER SER UM JOGADOR, a chamada e uma linha dentro do
/// `AbsorverCompanheiroCaido`, com <see cref="Extrema"/> -- e a mesma porta do `AmigoAbatido` que a
/// morte ja usa. Ate la, este comentario diz o que E, e nao o que falta.
/// ================================================================================================
///
/// Os dois filtram por INIMIGO e pulam o proprio autor (`Death.dm:75-77`, `KO.dm:30-35`): um spar
/// entre amigos nao acende raiva nenhuma, e quem derrubou o amigo nao ganha forma por isso.
///
/// ============================ POR QUE `Lendaria` E NAO `Comum` ============================
/// Porque o desconto tem dono. Ordem do dono: *"o Legendary Saiyajin tem a skill `legendary anger`
/// e por isso precisa de MENOS: basta ver um amigo ser NOCAUTEADO ou apanhar muito numa luta"*.
/// Chamar este degrau de "comum" diria que ele e o padrao e que o luto e a excecao -- e a regra e
/// o contrario: **o padrao e a furia extrema**, e a linha Legendary e a unica que compra barato.
///
/// E o desconto e da LINHA e nao da skill, de proposito -- sem campo novo, como manda a casa. A
/// `Legendary_Anger` so existe pra quem e Legendary, e as formas dela ja sao recusadas a todo
/// mundo pelo `LinhasAbertas`; derivar do arco da linha diz a mesma coisa e ainda pega o degrau
/// Legendary que alguem acrescentar amanha.
///
/// ============================ ISTO DIVERGE DO DM DE PROPOSITO ============================
/// Vale saber antes de "consertar": la o Legendary e MAIS estrito, nao menos. `supersaiyan.dm:60-74`
/// cobra `ssjat * 1.5` no degrau "Angry" contra `ssjat * 1.2` do SSJ1 comum (`:105-118`), e sem a
/// escapatoria por `prob(SSJInspired)` que o comum tem. E a `Legendary_Anger` (`saiyan.dm:38-51`)
/// nao toca gatilho nenhum -- *"That's all it does- no sacrificed form, no drawbacks"* --: ela so
/// escreve `legendaryAngerBonus = 100`, que e TETO DE PODER (`base.dm:88` leva o `angerBuff` de 2x
/// pra 3x; `master.dm:180` leva o `MaxAnger` junto). O desconto de PERMISSAO e desenho novo,
/// ditado pelo dono, e o DM nao tem opiniao sobre ele.
/// ==================================================================================
/// </summary>
public enum NivelDeRaiva
{
	/// <summary>
	/// Calma. **E o zero do enum de proposito**: e o valor que o `default` de qualquer
	/// <see cref="PerfilDeFormas"/> carrega, e o unico lado seguro pra um esquecimento -- quem
	/// esquecer de preencher recusa a forma em vez de conceder.
	/// </summary>
	Nenhuma = 0,

	/// <summary>
	/// Ver um amigo CAIR numa luta. So a linha Legendary se abre com tao pouco -- e ela se abre
	/// porque a `legendary anger` e dela.
	/// </summary>
	Lendaria = 1,

	/// <summary>
	/// Ver um amigo proximo MORRER na sua frente. E o que o tronco Saiyajin pede, e e o que o
	/// Beast pede. Satisfaz a <see cref="Lendaria"/> por ser maior -- ver o cabecalho.
	/// </summary>
	Extrema = 2,
}

/// <summary>
/// QUEM E ESTE PERSONAGEM, do ponto de vista das transformacoes.
///
/// Uma foto dos gates. Existe pra <see cref="Catalogo"/> ser funcao PURA: as mesmas entradas dao
/// as mesmas formas abertas, no cliente e no servidor, sem nenhum dos dois precisar de um `mob`.
/// </summary>
/// <param name="Raiva">
/// EM QUE RAIVA ESTE CORPO ESTA AGORA -- a janela do luto, nao o humor. Ver <see cref="NivelDeRaiva"/>.
///
/// E o unico campo deste perfil que descreve um INSTANTE e nao uma conquista: linhagem, classe,
/// maestria divina e proficiencia sao permanentes, e isto dura dois minutos. Entra aqui mesmo
/// assim porque o gate que o le (<see cref="Catalogo.RaivaExigida"/>) e o mesmo `Avaliar` de
/// todos os outros, e um segundo funil "so pra raiva" e como o `Proxima` e o `PorQueNao`
/// divergiriam.
///
/// ============================ ERA UM `bool`, E O BOOLEANO ESTAVA MENTINDO ============================
/// Ele nasceu `RaivaExtrema` porque so o Beast cobrava furia. Com a regra do dono inteira -- o
/// tronco Saiyajin pedindo a furia do LUTO e a linha Legendary se contentando com ver um amigo
/// CAIR -- um booleano so tem duas saidas ruins: ou o Wrathful passa a exigir um amigo morto (e o
/// desconto que o dono pediu deixa de existir), ou o SSJ1 abre com um nocaute (e a regra dele
/// deixa de existir). Nao ha terceira: sao TRES estados, e `bool` guarda dois.
///
/// O COMPILADOR ACHOU OS CONSUMIDORES por causa da troca de nome junto com a de tipo. Um
/// `NivelDeRaiva RaivaExtrema` compilaria em todo lugar que escrevia `RaivaExtrema: true` -- e
/// `true` nem converte, mas o `with { RaivaExtrema = ... }` de uma bancada passaria despercebido.
/// ================================================================================================
///
/// **O PADRAO `Nenhuma` E A PARTE IMPORTANTE.** O <see cref="GodKi"/> logo acima teve que nascer
/// em `-1` porque o zero do record struct significaria "despertou com 0% de maestria" e ABRIRIA
/// porta divina de graca. Aqui o zero do enum e `Nenhuma`, ou seja o padrao da linguagem ja e o
/// lado seguro: quem esquecer de preencher RECUSA a forma em vez de concede-la. Foi por isso que
/// o enum comeca na calma e sobe, e nao o contrario.
/// </param>
/// <param name="EstagioBio">
/// O `bio_stage` deste corpo -- 0 pra quem nao e bio-androide de laboratorio. Le-se contra
/// <see cref="FormaDef.PedeEstagioBio"/>, e e a metade "forma perfeita" do gate da Super Perfeita.
///
/// **ZERO E O LADO SEGURO**, como a raiva e como o God Ki negativo: quem esquecer de preencher
/// recusa a forma em vez de conceder. E ele e o `default` da linguagem, entao nenhuma bancada,
/// nenhum NPC e nenhum desenho de cliente precisa saber que este campo existe.
/// </param>
/// <param name="CanSsj">
/// O `canSSJ` do original -- o BYPASS que abre a escada Super Saiyajin pra uma raca que nao a tem.
///
/// `Transformation Controls.dm:2` e literal sobre o que ele e: *"If this is ticked to 1, SSJ is
/// weaker"*. Hoje quem o recebe e o bio-androide de laboratorio nascido com DNA Saiyajin
/// (`DNALabs.dm:478`), e o "mais fraco" tem endereco -- ver <see cref="FormaDef.MultDiluido"/> e o
/// <c>SangueDiluido</c> do servidor, onde a decisao de reusar a linha diluida esta declarada.
/// </param>
public readonly record struct PerfilDeFormas(
	string Raca = "",
	string Classe = "",
	string Linhagem = "",
	bool Diluido = false,
	bool Legendary = false,
	bool Futuro = false,
	double GodKi = -1,
	double EnergiaUe = 0,
	double ProficienciaUi = 0,
	NivelDeRaiva Raiva = NivelDeRaiva.Nenhuma,
	IReadOnlyDictionary<string, double>? FlagsDeSkill = null,
	int EstagioBio = 0,
	bool CanSsj = false)
{
	/// <summary>
	/// QUANTO VALE ESTA FLAG DE SKILL NESTE CORPO. Zero quando ele nao sabe a skill que a escreve --
	/// e o zero e o valor de fabrica do `mob/var` do DM, entao o padrao ja e o lado seguro (quem
	/// esquecer de preencher o <see cref="FlagsDeSkill"/> RECUSA a forma em vez de conceder).
	///
	/// E o mesmo raciocinio do <see cref="Raiva"/>: o dicionario e NULAVEL porque a maioria dos
	/// chamadores (bancadas, NPCs sem livro, o cliente desenhando a base) nao tem livro de skills
	/// nenhum -- e nao ter livro nao pode ser confundido com "sabe tudo".
	/// </summary>
	public double Flag(string campo) => FlagsDeSkill?.GetValueOrDefault(campo) ?? 0;

	/// <summary>Um Saiyajin qualquer, sem nada de especial. Serve de padrao e de bancada.</summary>
	public static readonly PerfilDeFormas Comum = new(Raca: "Saiyan");

	/// <summary>
	/// O mesmo, de sangue DILUIDO (meio-Saiyajin). Nasceu quando o `bool diluido` do
	/// <see cref="Catalogo.Multiplicador"/> virou este perfil: sem ele, cada bancada da linha
	/// nerfada montaria o proprio -- e um `new(...)` escrito na mao e onde os campos divergem.
	///
	/// Chama-se `MeioSangue` e nao `Diluido` porque `Diluido` ja e o nome do CAMPO deste record --
	/// o compilador recusa os dois (CS8866).
	/// </summary>
	public static readonly PerfilDeFormas MeioSangue = Comum with { Diluido = true };
}

/// <summary>
/// O CATALOGO DE FORMAS -- os numeros literais do BYOND, um por entrada.
///
/// ============================ O QUE ESTE REWORK RESOLVEU ============================
/// O pedido foi textual: *"no byond tinha mt adaptacao pq la cada nivel de saiyajin era colocado
/// como um valor numerico, ai se vc quisesse adicionar um novo estagio era uma dor de cabeca pq
/// tinha q mudar varios arquivos pro cabelo ler, o personagem conseguir transformar etc"*.
///
/// O que mudou, concretamente: antes havia um `enum Forma` e QUATRO switches sobre ele espalhados
/// (multiplicador, dreno, intensidade da cinematica no cliente, numero do DirectSSJ no admin) mais
/// um quinto no <see cref="LimiaresPessoais"/>. Acrescentar o SSJ4 Full Power exigia tocar os
/// cinco -- e esquecer um dava uma forma que transformava mas nao tinha cabelo, ou que tinha
/// cabelo mas multiplicava por 1.
///
/// Agora sao CAMPOS da entrada. A prova disso e mecanica, nao verbal: a bancada `formas` conta as
/// entradas e falha se alguma tiver campo obrigatorio faltando.
/// ====================================================================================
///
/// TRES REGRAS DO ORIGINAL QUE SOBREVIVERAM INTEIRAS:
///
/// 1. **A PORTA E O BP BASE, NUNCA O EXPRESSO** (decisao de 2026-06-28): gatear pelo expresso
///    deixaria uma forma, uma raiva ou um zenkai "fingirem" o requisito da forma seguinte --
///    bastaria virar SSJ1 pra o SSJ2 abrir sozinho.
/// 2. **MAESTRIA EM DEGRAUS, nao rampa** (`stepped_mastery_mult`): dominar uma forma e um
///    ACONTECIMENTO, nao um deslizar de barra.
/// 3. **PISO DE (ANTERIOR + N)** (`ssj_effective_mult`).
/// </summary>
/// <summary>
/// QUAL DESENHO DE AURA A FORMA USA. E um SIMBOLO, nao um caminho: o Core nao conhece o Godot, e
/// quem traduz isto em `res://` e o cliente.
///
/// A regra, ditada pelo dono: "toda forma usa colorablebigaura.png MENOS o LSSJ". Por isso ela e
/// derivada da LINHA e nao um campo por forma -- um degrau novo de Legendary ja nasce com a folha
/// certa, que era o ponto inteiro de refazer o sistema de formas por dado.
/// </summary>
public enum FolhaDeAura
{
	/// <summary>`colorablebigaura`: a aura de todo mundo, tingida com a cor do personagem.</summary>
	Base,

	/// <summary>`AuraLSSjBig`: so as linhas Legendary.</summary>
	Lssj,

	/// <summary>
	/// `AuraSSjBig`: a escada Saiyajin comum (e a Futura). A arte JA E DOURADA -- esta folha nao
	/// se tinge, igual a do flash de cinematica.
	/// </summary>
	Ssj,

	/// <summary>
	/// ============================ AS DIVINAS TEM FOLHA PROPRIA, E SAO DUAS ============================
	/// `FieryGod.dmi` -- a CHAMA quente (pessego/laranja) do ki divino SEM Super Saiyajin por baixo.
	///
	/// Ela nao e escolhida por forma no original: `AuraObject.dm:174-176` troca o icone pela
	/// `container.gkaura` no ramo `else if(!setNJ && !container.ssj && !container.lssj)` -- ou seja
	/// **ki divino aceso e a escada Saiyajin ZERADA**. O `mob/var/gkaura = 'FieryGod.dmi'`
	/// (`AuraObject.dm:10`) e o padrao de todo mundo.
	///
	/// Quem cai aqui: o SSG (`ssj == 0`) e o SSG da linha Rose.
	///
	/// ============================ E A LINHA DO PRODIGIAL SAIU DAQUI ============================
	/// **No DM ela cai neste mesmo ramo** -- o Mistico e o Beast chamam `Revert()` ao entrar
	/// (`Mystic.dm:33` e `:138`), o que zera `ssj` e `lssj` e joga os dois aqui junto com o SSG. Foi
	/// leitura correta e ficou assim ate o dono derrubar: *"o mistico e beast tao usando a aura de
	/// carga do ssj god"*. Os dois voltaram pra a <see cref="Base"/>, que se tinge -- ver
	/// <see cref="Catalogo.Folha"/> e <see cref="Catalogo.ChamaDoJogador"/>, onde a divergencia esta
	/// declarada por extenso.
	/// ======================================================================================
	///
	/// **ELA NAO SE TINGE**: `icolor = null` na linha seguinte (`AuraObject.dm:175`). A arte ja vem
	/// colorida, igual a `AuraSSjBig` -- ver `SpriteDeAura.PreColorida`.
	/// ==============================================================================================
	/// </summary>
	DeusQuente,

	/// <summary>
	/// `FieryGodBlue.dmi` -- a CHAMA fria (azul) do ki divino COM Super Saiyajin por baixo.
	///
	/// Mesmo mecanismo, o outro ramo: `AuraObject.dm:167-169`, `if(container.godki?.usage) icon =
	/// container.sgkaura` DENTRO do bloco que so roda quando `ssj > 0` ou `lssj > 0`. E
	/// `sgkaura = 'FieryGodBlue.dmi'` (`AuraObject.dm:12`).
	///
	/// Quem cai aqui: Blue e Blue Evolution. Tambem nao se tinge (`icolor = null`, `:168`).
	/// </summary>
	DeusFrio,

	/// <summary>
	/// `Supa Saiyan Rose Aura-1.dmi` -- a chama do Super Saiyajin Rose, e ela e arte PROPRIA.
	///
	/// ============================ NO DM O ROSE TEM AURA AZUL, E ISSO NAO E O QUE O JOGO QUER ==========
	/// Conferido no original: `godki_mod` (a variavel do Rose, `godki.dm:21`) e lida em DOIS lugares
	/// no jogo inteiro -- `SaiyanObjects.dm:13` (cabelo) e `:104` (rabo). O `AuraObject.dm` **nao a
	/// consulta em lugar nenhum**: no BYOND um Rose acende a `FieryGodBlue`, AZUL, com cabelo e rabo
	/// rosa.
	///
	/// ============================ E A CORRECAO ANTERIOR ERA UM REMENDO ============================
	/// Esta entrada ja existiu como "a MESMA arte da <see cref="DeusFrio"/>, mas tingida", e o
	/// comentario dela dizia que nao havia folha Rose no repo. **Havia.** O dono a achou --
	/// `Supa Saiyan Rose Aura-1`, ja convertida e importada, e nenhum `.cs` a citava. E a terceira vez
	/// que este projeto encontra arte convertida e nunca ligada (os 35 atlas, depois a `FieryGod`), e
	/// a licao repetida e a mesma: "nao existe no repo" precisa ser MEDIDO, nao lembrado.
	///
	/// Com a arte de verdade na mao, o tingimento saiu junto -- ela ja e rosa. Ver
	/// `SpriteDeAura.PreColorida`, onde ela entrou ao lado das outras tres folhas pintadas.
	/// ==========================================================================================
	/// </summary>
	DeusRosa,

	/// <summary>
	/// ============================ ESTA NAO E UMA FOLHA: E O AVISO DE QUE NAO HA FOLHA ============================
	/// A linha do Ultra Instinto nao acende chama nenhuma. O que ela mostra e a NEBULOSA -- a nuvem
	/// procedural que envolve a silhueta (`Client/NebulosaDaForma.cs`), e que ja fica ligada o tempo
	/// todo como overlay da forma. Ordem do dono: *"a aura/carga do ultra instinto deveria ser essa
	/// aura em shaders, e nao o icone de carga atual"*.
	///
	/// ============================ E O ULTRA EGO ENTROU AQUI DEPOIS ============================
	/// Palavra do dono, literal: *"a aura/carga do ultra ego e a mesma do instinto superior so q ROXA
	/// ao inves de branca/prateada, mas tem os mesmos efeitos"*. "Os mesmos efeitos" e o que decide o
	/// desenho da solucao: nao ha efeito novo nem shader novo -- ha uma PALETA a mais, e por isso este
	/// simbolo passou a ter DOIS donos e nasceu a <see cref="Catalogo.PaletaDaNebulosa"/>.
	///
	/// SO O `ultra_ego`, e NAO a linha inteira: a `destroyer` fica de fora de proposito. O dono nomeou
	/// o Ultra Ego, e o DM diz que a diferenca visual entre as duas formas da disciplina e justamente
	/// o cabelo e a cena (`UltraEgo.dm:395-396`) -- dar a nuvem as duas apagaria essa diferenca. E o
	/// mesmo criterio que ja manteve a `destroyer` sem tinta de cabelo e sem cinematica.
	///
	/// ============================ POR QUE ISSO ENTRA AQUI, E NAO NUM `if` NO CLIENTE ============================
	/// O canal que ja existe e ESTE simbolo: quem decide e o Core (`Catalogo.Folha`, derivado da
	/// LINHA), e os TRES desenhistas da chama (`Aura`, `CargaVisual` e a chama da cinematica) ja o
	/// recebem pelo mesmo par de chamadas. Um `if (forma.Id == "ui_sign")` no cliente seria uma quarta
	/// descricao da mesma regra -- e a familia de defeito deste arquivo inteiro e justamente essa:
	/// duas verdades sobre a mesma forma, uma delas envelhecendo calada (ver o cabecalho da
	/// <see cref="Catalogo.Folha"/>, onde CINCO formas divinas ficaram anos na folha errada).
	///
	/// O GANHO DE BRINDE e que <see cref="Catalogo.TemNebulosa"/> passou a ser DERIVADO daqui em vez de
	/// repetir o predicado `Linha == UltraInstinct`. Eram duas fontes de verdade dizendo a mesma coisa;
	/// hoje uma linha nova que ganhe nebulosa a ganha nos dois lugares de uma vez, e e impossivel um
	/// corpo desenhar a nuvem E a chama -- porque "tem nuvem" e literalmente "nao tem folha".
	///
	/// E O ULTRA EGO COBROU ESSE BRINDE NA HORA: bastou a linha nova no `switch` da
	/// <see cref="Catalogo.Folha"/> pra ele parar de acender a `colorablebigaura` do `b96bff` E ganhar
	/// a nuvem, num movimento so. Se os dois fatos ainda fossem dois predicados, o `ultra_ego` teria
	/// saido com a nuvem roxa E a chama roxa por cima -- que foi exatamente a queixa original do dono
	/// sobre o Ultra Instinto.
	///
	/// QUEM TRADUZ ISTO EM `res://` (`SpriteDeAura.CaminhoDa`) devolve NULO pra este simbolo, e nulo la
	/// quer dizer "a minha nao e folha". O Core continua sem conhecer o Godot: aqui so ha o simbolo.
	/// ==========================================================================================================
	/// </summary>
	Nebulosa,
}

/// <summary>
/// ============================ AS QUATRO CORES DA NUVEM ============================
/// A paleta que o `NebulosaDaForma.gdshader` desenha, do que esta LONGE do corpo pro que esta
/// ENCOSTADO nele. Ela existe porque a nuvem passou a ter DOIS donos (Ultra Instinto e Ultra Ego) e
/// a unica coisa que os separa e a cor -- ver <see cref="Catalogo.PaletaDaNebulosa"/>.
///
/// A ORDEM DOS CAMPOS E A ORDEM DA RAMPA, e isso e proposital: e a mesma sequencia do
/// `neb_rampa(t)` do shader (`cor_borda` -> `cor_meio` -> `cor_perto`), entao ler a declaracao de uma
/// paleta e ver o degrade. <paramref name="Pontos"/> fica por ultimo porque ele nao esta na rampa:
/// e a microparticula que sobe POR CIMA de tudo, composta no fim do `fragment`.
///
/// SAO HEXA SEM `#`, como a <see cref="Catalogo.CorDoContorno"/> e a <see cref="Catalogo.CorDosRaios"/>
/// -- o Core nao conhece o Godot e nao tem tipo de cor. Quem traduz e o cliente.
/// </summary>
public readonly record struct PaletaDeNebulosa(string Borda, string Meio, string Perto, string Pontos);

/// <summary>
/// ============================ O OVERLAY COLADO NO CORPO -- E ELE NAO E UMA AURA ============================
/// A <see cref="FolhaDeAura"/> e a CHAMA: um sprite grande (96x96), irmao do boneco, desenhado ATRAS
/// dele. Isto aqui e outra coisa -- um desenho de 32x32 que veste o corpo POSE POR POSE, por cima de
/// tudo. No DM os dois sao objetos diferentes com plano diferente: a chama e `/obj/overlay/auras`
/// (`AuraObject.dm:18`), e estes sao `/obj/overlay/auras/gk` (`godki.dm:235`) e
/// `/obj/overlay/effects/menacing_aura` (`EffectLayer.dm:30`), os dois em `AURA_LAYER = 7`
/// (`Overlays.dm:9`) -- acima de corpo (2), roupa (3), cabelo (4) e chapeu (5).
///
/// A PROVA DE QUE E CAMADA E NAO FOLHA DE AURA ESTA NO ARQUIVO: as tres folhas de 320x320
/// (`god`, `god blue`, `god - grey`) trazem as MESMAS 24 animacoes do corpo -- `walk_south`,
/// `default_north`, `attack_east`, `flight_*`, `kb`, `ko`. Uma folha de aura tem UMA animacao
/// (`default`, 4 a 10 quadros). Elas foram desenhadas pra andar travadas na pose do boneco, que e
/// exatamente o `VIS_INHERIT_ICON_STATE` do `/obj/overlay` (`Overlays.dm:19`) e o contrato de camada
/// do `CharacterVisual`.
///
/// (Havia registro de que isto tinha sido notado e adiado -- a entrada `wrathful` deste catalogo ja
/// dizia que o `menacing_aura` "e um overlay de CORPO e nao cabelo". Ficou adiado porque nao havia
/// onde por: `Catalogo.Folha` devolve UMA folha, e o LSSJ precisa de DUAS.)
/// =========================================================================================================
/// </summary>
public enum FolhaColada
{
	/// <summary>
	/// `LSSJpowerz.dmi` -- as fagulhas do Legendary. Folha de UMA animacao (6 quadros de 32x32) e de
	/// um unico tom (`#d3ff26`), esparsa: 374 pixels acesos em 6144.
	///
	/// ============================ ELA NAO VEM DO DM, VEM DO DONO ============================
	/// Varrido o codigo inteiro do original: `LSSJpowerz` nao e citado em UM lugar sequer. O arquivo
	/// esta la (`Icons/Auras/LSSJpowerz.dmi`) e nunca foi ligado -- arte morta no BYOND tambem.
	/// Quem a pediu foi o dono, e esta anotado aqui porque a pergunta "de que linha do DM saiu isto?"
	/// vai voltar e a resposta e NENHUMA.
	/// </summary>
	PoderLendario,

	/// <summary>
	/// `god - grey.dmi` -- A FOLHA COLORIVEL do original, e a unica das tres que se tinge.
	///
	/// O DM a usa em SEIS lugares e em todos com uma cor por cima: verde no `menacing_aura`
	/// (`EffectLayer.dm:31-32`), roxo no `god_fury_aura` (`GodOfDestruction.dm:389`), carmesim no
	/// `ue_aura_pas` (`UltraEgo.dm:353`), vermelho no `fd_menacing_red` (`IcerTransform.dm:167`),
	/// branco no Anjo e purpura no Deus da Destruicao (`godki.dm:273` e `:281`). Medida: ela e
	/// CINZA PURO (150..192 nos tres canais), o que e o desenho certo pra receber cor.
	/// </summary>
	Ameacadora,

	/// <summary>
	/// `god.dmi` -- `gkoverlay` (`AuraObject.dm:14`), o brilho do ki divino SEM Super Saiyajin por
	/// baixo. `godki.dm:265-266`: `if(container.ssj==0 && container.lssj==0) icon = container.gkoverlay`.
	///
	/// Arte JA colorida (laranja/pessego, `#ffbe28` no tom dominante) -- nao se tinge, igual a chama
	/// <see cref="FolhaDeAura.DeusQuente"/> que a acompanha.
	/// </summary>
	Deus,

	/// <summary>
	/// `god blue.dmi` -- `sgkoverlay` (`AuraObject.dm:16`), o mesmo desenho em azul (`#2869ff`), pro
	/// ki divino COM Super Saiyajin por baixo (`godki.dm:295`). Tambem ja colorida.
	/// </summary>
	DeusAzul,
}

/// <summary>
/// UMA CAMADA COLADA: a folha e, quando ela e a cinza, a cor que a pinta. <see cref="Tinta"/> nulo
/// quer dizer "a arte ja vem na cor certa" -- e o caso de tres das quatro folhas.
///
/// A TINTA MULTIPLICA e nao soma, e a diferenca e do proprio DM: no cabelo ele faz `icon += rgb()`
/// (`SaiyanObjects.dm:18`) porque o sprite de penteado e um molde quase PRETO; aqui ele faz
/// `color = rgb()` (`EffectLayer.dm:32`), que na linguagem do BYOND e multiplicacao, porque a folha
/// e cinza claro. Somar num cinza de 192 estouraria tudo pro branco.
/// </summary>
public readonly record struct Colada(FolhaColada Folha, string? Tinta = null);

/// <summary>
/// ============================ TROCAR O SPRITE E TINGIR O BASE SAO COISAS DIFERENTES ============================
/// O port so sabia dizer uma delas: <see cref="FormaDef.SufixoDoCabelo"/> vazio significava "nao
/// troca", e nao havia como escrever "mantem o penteado e PINTA" nem "troca E pinta por cima". Com
/// isso tres formas sairam erradas ao mesmo tempo, e as tres foram apontadas pelo dono:
///
///   * o **Wrathful** trocava pro cabelo de Super Saiyajin. No DM ele poe
///     `updateOverlay(/obj/overlay/hairs/hair)` -- o penteado BASE, sem tinta nenhuma
///     (`HairObject.dm:208-210`);
///   * o **SSG** trocava pro cabelo de Super Saiyajin. No DM ele tambem usa o BASE, so que TINGIDO
///     de `rgb(226,51,28)` (`HairObject.dm:73-75`, dentro do `gdki_me` do `/hairs/hair`);
///   * o **Legendary** saia dourado. No DM o sprite e o `ussjhair` e o overlay `lssjhair` soma
///     `rgb(0,110,0)` por cima (`SaiyanObjects.dm:83-87`) -- troca E tinge.
///
/// ============================ POR QUE E DERIVADO E NAO UM CAMPO ============================
/// Nao ha campo novo: o modo sai de DOIS dados que a forma ja carrega -- o sufixo e a tinta
/// (<see cref="Catalogo.CorDoCabelo"/>, que le a <see cref="FormaDef.Cabelo"/>). Cada combinacao
/// das duas ja E um modo, e um campo a mais so criaria a chance de ele discordar dos dois.
///
/// O precedente e o mesmo de <see cref="Catalogo.Folha"/> e <see cref="Catalogo.NasceDaRaiva"/>: o
/// modo de falhar de um campo aqui seria CALADO -- um degrau novo com sufixo e tinta preenchidos e
/// modo esquecido nasceria em `Base`, ou seja careca de forma, e ninguem notaria ate transformar.
/// ==========================================================================================
/// </summary>
public enum ModoDoCabelo
{
	/// <summary>Nem troca nem tinge: o penteado do jogador, na cor dele. Wrathful, Mistico, Destroyer.</summary>
	Base,

	/// <summary>Troca pelo sprite da variante; sem tinta. A escada Saiyajin inteira.</summary>
	Trocar,

	/// <summary>Mantem o penteado base e TINGE. O SSG (vermelho) e o Ultra Ego (roxo).</summary>
	Tingir,

	/// <summary>
	/// Troca E tinge por cima do que achou. Legendary (verde), Blue (azul), Rose (rosa).
	///
	/// A tinta vale TAMBEM pra quem nao tem variante desenhada: o resolvedor devolve nulo, o
	/// penteado base fica e a tinta cai nele. E o mesmo resultado que o DM da pra quem nao tem a
	/// folha propria -- e a razao de a degradacao ser aceitavel aqui e nao na escada Saiyajin
	/// (ver <see cref="Catalogo.CorDoCabelo"/>).
	/// </summary>
	TrocarETingir,

	/// <summary>
	/// Troca QUANDO HA arte propria; so tinge quem ficou sem ela -- e os dois nunca se acumulam.
	///
	/// ============================ O CASO E DO ULTRA INSTINCT E O DM E EXPLICITO ============================
	/// `ui_apply_hair()` (`UltraInstinct.dm:296-303`) e um `if` de TRES ramos sobre o penteado:
	/// estilo Goku recebe o `Hair_UltraInstinct.dmi` / `Hair_MasteredUltraInstinct.dmi` (arte de UM
	/// personagem, sem tinta nenhuma); os OUTROS recebem o `/hairs/uisilver`, que e
	/// `icon = container.hair` + `rgb(185,190,200)` (`:288-293`). Sao overlays DIFERENTES, escolhidos
	/// um ou outro -- nao a mesma folha pintada.
	///
	/// Tingir por cima aqui nao seria uma aproximacao, seria estragar: a arte do UI ja e prateada, e
	/// somar prata em prata estoura pro branco chapado.
	/// ==================================================================================================
	/// </summary>
	TrocarOuTingir,

	/// <summary>
	/// Troca E RECOLORE (matiz, nao soma). So o Beast.
	///
	/// ============================ SOMAR NAO FAZ BRANCO-GELO ============================
	/// A receita do DM tem DOIS passos (`Mystic.dm:76-85`): `MapColors(0.34 x9)` -- que e um
	/// grayscale exato, cada canal virando `0,34*(r+g+b)` -- e SO ENTAO `Blend(rgb(70,74,84),
	/// ICON_ADD)`. E o primeiro passo que mata o loiro; sem ele, somar `#464A54` num cabelo de SSJ2
	/// dourado devolve dourado lavado, e nao o branco-gelo azulado da Fera.
	///
	/// O port ja tinha os dois passos num lugar so: o `tinta_modo = 1` do `Personagem.gdshader`, o
	/// MATIZ que a roupa usa -- ele guarda a LUMINANCIA do desenho (o sombreado dos fios) e
	/// substitui a COR. E a mesma ideia em uma passada, e nao havia por que inventar uma segunda.
	/// ==================================================================================
	/// </summary>
	TrocarERecolorir,
}

public static class Catalogo
{

	/// <summary>
	/// A FOLHA DE AURA DESTA FORMA. Ver <see cref="FolhaDeAura"/> pra o porque de ser derivada da
	/// linha. A bancada percorre o catalogo inteiro conferindo isto, entao uma linha Legendary nova
	/// que ficasse de fora reprovaria em vez de sair com a aura errada em silencio.
	/// </summary>
	/// <remarks>
	/// ============================ AS DIVINAS SAIRAM DA FOLHA COLORIVEL ============================
	/// Ate esta varredura, God, Blue, Rose, Mistico e Beast caiam todos em <see cref="FolhaDeAura.Base"/>
	/// -- a `colorablebigaura` tingida --, e o comentario de cima dizia que era de proposito "porque a
	/// cor delas e o que as distingue". Era engano meu: o original tem DUAS folhas dedicadas pra elas
	/// (`AuraObject.dm:10` e `:12`), ja convertidas e importadas no projeto, e mortas -- nenhum `.cs`
	/// citava `FieryGod` nem `FieryGodBlue`. Ver <see cref="FolhaDeAura.DeusQuente"/>.
	///
	/// O CORTE ENTRE AS DUAS E O MESMO <see cref="OrdemDoKiSobreOSuperSaiyajin"/> que o contorno ja
	/// usa, e nao por simetria de enfeite: no DM a escolha e literalmente "tem Super Saiyajin por
	/// baixo?" (`ssj > 0 || lssj > 0`), e o degrau 20 e onde o ki divino passa a acender SOBRE o SSJ1.
	/// Um degrau divino novo acima do Blue ja nasce com a chama fria sem tocar aqui.
	/// ==========================================================================================
	///
	/// ============================ MAS A LINHA DO MISTICO VOLTOU PRA A COLORIVEL ============================
	/// O paragrafo acima cita "God, Blue, Rose, Mistico e Beast" como o lote inteiro que subiu pra as
	/// folhas dedicadas. **Os dois ultimos desceram de novo**, por ordem do dono: *"o mistico e beast
	/// tao usando a aura de carga do ssj god"*, e ele nomeou os DOIS.
	///
	/// ISSO E DIVERGENCIA DECLARADA E NAO CONSERTO -- o DM POE os dois na `FieryGod` mesmo, e a leitura
	/// que os levou pra la continua correta linha por linha (`Mystic.dm:33` e `:138` chamam `Revert()`,
	/// que zera `ssj`/`lssj`, e o `AuraObject.dm:174-176` manda quem tem ki divino com a escada zerada
	/// pra a `container.gkaura`). Ler o `AuraObject.dm` e concluir que estes dois "estao errados aqui"
	/// nao e achado: e o caminho pra devolver a chama do SSG a eles e reabrir a queixa. A palavra do
	/// dono vence o DM, e este bloco existe pra isso ficar por escrito no lugar em que a tentacao mora.
	///
	/// E A VOLTA PRA A <see cref="FolhaDeAura.Base"/> NAO E "cair no fallback": e a unica folha do jogo
	/// que SE TINGE, e sao os dois degraus que precisam disso -- o Beast pra acender o `7d5af0` que a
	/// propria entrada dele declara (e que ate aqui nao chegava em pixel nenhum, porque a `FieryGod`
	/// nao se pinta), e o Mistico pra acender a chama do JOGADOR, que e o pedido literal
	/// (*"a aura do mistico tem q ser a mesma aura da BASE DO PERSONAGEM"*). Quem responde a segunda
	/// metade -- de QUE cor -- e a <see cref="ChamaDoJogador"/> logo abaixo; aqui so se escolhe o
	/// desenho.
	/// ====================================================================================================
	/// </remarks>
	public static FolhaDeAura Folha(FormaDef? d) => d?.Linha switch
	{
		LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal => FolhaDeAura.Lssj,
		// A BASE FICA DE FORA: `base` nao e transformacao, e o Saiyajin parado nao tem aura de SSJ.
		LinhaDeForma.Saiyajin or LinhaDeForma.Futuro when d.Id != IdBase => FolhaDeAura.Ssj,

		LinhaDeForma.GodKi     => d.Ordem >= OrdemDoKiSobreOSuperSaiyajin
									? FolhaDeAura.DeusFrio : FolhaDeAura.DeusQuente,
		LinhaDeForma.GodKiRose => d.Ordem >= OrdemDoKiSobreOSuperSaiyajin
									? FolhaDeAura.DeusRosa : FolhaDeAura.DeusQuente,

		// A LINHA DO MISTICO NAO TEM RAMO AQUI, e a AUSENCIA e a regra -- ver o bloco do `<remarks>`.
		// Havia um `LinhaDeForma.Mistico => FolhaDeAura.DeusQuente` nesta linha (o ramo do DM), e o
		// dono o derrubou nos dois degraus. Sem ramo, os dois caem no `_` de baixo, que e a folha
		// COLORIVEL -- a unica que aceita a cor que cada um deles ja declara.

		// A LINHA DO ULTRA INSTINTO NAO TEM CHAMA: ela tem a NUVEM. Ver `FolhaDeAura.Nebulosa` --
		// este simbolo e o que diz "nao ha folha", e e dele que o `TemNebulosa` sai hoje.
		LinhaDeForma.UltraInstinct => FolhaDeAura.Nebulosa,

		// O ULTRA EGO TEM A MESMA NUVEM, EM ROXO -- e SO ele, nao a linha. O corte e por `Ordem` como
		// o das divinas logo acima: a `destroyer` (Ordem 10) continua na folha colorivel com o `9b4dff`
		// que a entrada dela declara. Ver o bloco do Ultra Ego em `FolhaDeAura.Nebulosa` pro porque.
		LinhaDeForma.UltraEgo when d.Ordem >= OrdemDoEgoSobreADestruicao => FolhaDeAura.Nebulosa,

		_ => FolhaDeAura.Base,
	};

	/// <summary>
	/// ============================ ESTA FORMA ACENDE A CHAMA NA COR DO **JOGADOR**? ============================
	/// A <see cref="Folha"/> diz QUAL DESENHO a chama usa; esta diz DE QUEM E A COR dele. Sao duas
	/// perguntas e o par <see cref="FolhaDeAura.Base"/> / "pre-colorida" nao responde a segunda: uma
	/// folha PRE-COLORIDA e uma folha que ja tem a cor DENTRO do arquivo (`icolor = null` no DM), e a
	/// folha colorivel e uma folha que aceita QUALQUER cor de fora. Nenhuma das duas diz *"a cor de
	/// fora e a do jogador, e nao a da forma"* -- e confundir essas ideias e o que ja deixou aura
	/// cinza neste projeto uma vez.
	///
	/// ============================ O DONO PEDIU EXATAMENTE ISTO ============================
	/// *"a aura do mistico tem q ser a mesma aura da BASE DO PERSONAGEM, porem com os efeitos de
	/// raiozinhos q ja existem"* -- ou seja a chama pessoal, e nao mais uma cor de forma. E **e o que o
	/// DM faz**, o que torna este ponto PORTE e nao divergencia: sem ki divino aceso o
	/// `AuraObject.dm:191-192` usa `container.AURA` (a folha colorivel), e o `centerAura()` chamado em
	/// `:194` escreve `icolor = rgb(AuraR, AuraG, AuraB)` -- a cor sorteada no nascimento
	/// (`CharacterCreation.dm:25-27`). O Mistico nao pede ki divino nenhum (`PedeGodKi = -1`), entao no
	/// original ele cai ali.
	///
	/// ============================ E A COR SORTEADA AINDA NAO EXISTE NESTE PORT ============================
	/// A `Core/Appearance/Appearance.cs` nao tem campo de aura, nem sorteio, nem canal de rede, nem save.
	/// O que existe e UMA chama pessoal compartilhada -- o `Aura.CorDoKiCru` do cliente --, e ela e
	/// literalmente "a aura da base do personagem" que o dono nomeou. Entao esta funcao devolve a
	/// PERGUNTA ("use a do jogador") e nao a cor: no dia em que a cor virar campo de ficha, muda quem
	/// responde do lado do cliente e nem esta linha nem o catalogo sabem que algo mudou.
	///
	/// ============================ POR QUE `bool`, E NAO UM VALOR NOVO EM `FolhaDeAura` ============================
	/// Porque nao e uma folha. Um simbolo `FolhaDeAura.DoJogador` teria que ser traduzido em
	/// `res://` pela `SpriteDeAura.CaminhoDa` e apontaria pra a MESMA `colorablebigaura` da
	/// <see cref="FolhaDeAura.Base"/> -- duas folhas do enum pro mesmo `.tres`, que e exatamente o
	/// estado que ja fez o `SemTinta` mentir (ver o cabecalho da `SpriteDeAura.PreColorida`).
	///
	/// ============================ A DERIVACAO E O OUTRO LADO DE UM CORTE QUE JA EXISTE ============================
	/// `PedeGodKi >= GodkiRoyalePct` e o corte que o <see cref="ParDoContorno"/> e a
	/// <see cref="RaivaExigida"/> ja usam pra isolar o Beast dentro da linha do Mistico; aqui se usa o
	/// MESMO corte virado do avesso, e por isso os dois degraus nao podem divergir por engano: quem
	/// mexer no `PedeGodKi` do Beast move contorno, raiva e chama de uma vez, que e o que se quer.
	///
	/// E O BEAST FICA DE FORA COM RAZAO PROPRIA: ele tem cor de chama declarada na entrada (`7d5af0`,
	/// o `rgb(125,90,240)` do `Mystic.dm:95`) e o Mistico nao tem nada equivalente no DM. Nao e
	/// simetria quebrada -- sao dois degraus que o original descreve de dois jeitos diferentes.
	///
	/// TETO CONHECIDO, o mesmo do `ParDoContorno`: um terceiro degrau nesta linha acima do Beast ja
	/// nasce com chama propria. Quem inserir que decida de novo.
	/// ==========================================================================================================
	/// </summary>
	public static bool ChamaDoJogador(FormaDef? d) => d?.Linha switch
	{
		// SEM FORMA **E** A BASE SAO O MESMO ESTADO, e a base e o caso original desta pergunta: a
		// chama de quem nao esta transformado sempre foi a pessoal. Escrever isto aqui e o que
		// permite ao cliente ter UMA conta em vez de duas ("e base?" mais "e Mistico?").
		null => true,
		LinhaDeForma.Mistico => d.PedeGodKi < GodkiRoyalePct,

		// ============================ O FROST DEMON NAO TROCA DE CHAMA -- ELE TROCA DE CORPO ============================
		// A linha inteira, e nao um degrau: NENHUMA das sete formas acende overlay de aura no original.
		// `Frost_Demon_Forms` (`IcerTransform.dm:83-114`) faz tres coisas -- toca `1aura.wav`, chama
		// `icer_poll_icon()` e anuncia no chat. Quem veste aura nele e o GOLDEN (`/obj/overlay/icergod`,
		// que e skill separada e nao esta nesta linha) e o DESCONTROLE do Mutante (`fd_menacing_red`,
		// que e estado e nao forma).
		//
		// Derivado da LINHA pelo mesmo motivo de sempre: a 8a forma que alguem acrescentar aqui ja nasce
		// com a chama do dono. E a alternativa -- cada entrada declarando a propria cor de aura -- daria
		// um Frost Demon que muda de cor de chama ao evoluir, que e uma coisa que o jogo dele nunca fez.
		// ============================================================================================================
		LinhaDeForma.FrostDemon => true,

		_ => d.Id == IdBase,
	};

	/// <summary>
	/// O `rgb(110,255,140)` do `menacing_aura` (`EffectLayer.dm:32`) -- o verde que o DM poe na folha
	/// cinza pras formas Legendary. Vem do arquivo, nao foi escolhido.
	///
	/// NAO E O <see cref="VerdeLegendary"/> (`4dff5a`, o contorno), e a diferenca e de conta e nao de
	/// gosto: a tinta colada MULTIPLICA um cinza de 192, entao ela precisa nascer clara pra o
	/// resultado ter corpo (192 x 110/255 = 83 no canal mais fraco). O contorno e uma mistura sobre a
	/// silhueta e vive noutra escala. Duas cores pra a mesma ideia, e elas se movem por motivos
	/// diferentes -- o mesmo criterio que separa <see cref="RoxoDaFera"/> de <see cref="RoxoDoEgo"/>.
	/// </summary>
	private const string VerdeDaAmeaca = "6eff8c";

	/// <summary>
	/// O ROSA da colada do Rose. **NAO ha equivalente no DM** -- ver <see cref="Coladas"/>.
	///
	/// Escolhido pra casar com o <see cref="VerdeDaAmeaca"/> na unica coisa que importa numa tinta que
	/// multiplica: a ALTURA. O verde do arquivo tem canal maximo 255 e minimo 110 (saturacao 0,57);
	/// este tem 255 e 122 (0,52). Um rosa escuro sairia preto sobre o cinza, e um pastel sairia branco.
	///
	/// Ele e o mesmo hexa do <see cref="RosaDivino"/> do contorno POR COINCIDENCIA -- os dois querem
	/// dizer "a cor da linha Rose" -- e esta escrito separado exatamente por isso. Ver o cabecalho do
	/// <see cref="AzulDaFera"/>: reusar a constante faria o dia em que alguem ajustar o contorno mexer
	/// no desenho colado, e o defeito apareceria longe da mudanca.
	/// </summary>
	private const string RosaDaColada = "ff7ac6";

	/// <summary>
	/// ============================ AS CAMADAS COLADAS NO CORPO DESTA FORMA ============================
	/// Vazio pra a grande maioria -- so as duas linhas Legendary e as duas divinas tem alguma. Ver
	/// <see cref="FolhaColada"/> pra o porque de isto NAO caber em <see cref="Folha"/>.
	///
	/// A TABELA, ditada pelo dono:
	///
	///     Legendary / Legendary Primal  ->  `LSSJpowerz`  +  `god - grey` VERDE
	///     SSG (`ssg`, `rose_ssg`)       ->  `god`                (sem tinta)
	///     Blue (`blue`, `blue_evolution`) -> `god blue`          (sem tinta)
	///     Rose (`rose`, `rose2`)        ->  `god - grey` ROSA
	///
	/// ============================ E ELA E O DM, MENOS EM UM PONTO ============================
	/// O corte entre `god` e `god blue` NAO foi escolhido aqui: `godki.dm:265` pergunta
	/// `if(container.ssj==0 && container.lssj==0)` -- "tem Super Saiyajin por baixo?" --, que e o mesmo
	/// <see cref="OrdemDoKiSobreOSuperSaiyajin"/> que a <see cref="Folha"/> e o
	/// <see cref="ParDoContorno"/> ja usam. Um degrau divino novo acima do Blue nasce certo sem tocar
	/// nesta funcao, que e o ponto inteiro de derivar da Ordem.
	///
	/// O VERDE DO LEGENDARY tambem e literal (`EffectLayer.dm:30-33`), e no DM ele e ligado nos QUATRO
	/// degraus da linha (`lssjbuff.dm:85`, `:91`, `:106`, `:112`) e desligado no reverter (`:130`).
	/// A linha PRIMAL entra junto pelo mesmo precedente das outras derivacoes deste arquivo -- ela e a
	/// escada Legendary da linhagem primal, e ja compartilha folha de aura e cor de contorno com a
	/// irma.
	///
	/// ============================ O PONTO QUE NAO E O DM: O ROSE ============================
	/// No original o Rose usa `sgkoverlay` -- `god blue`, AZUL --, porque `godki.dm` nao consulta
	/// `godki_mod` em lugar nenhum. E a MESMA divergencia ja declarada em
	/// <see cref="FolhaDeAura.DeusRosa"/>, e pela mesma razao: uma forma cujo cabelo, rabo, contorno e
	/// chama sao rosa nao pode ter o unico desenho AZUL colado nela. O dono escolheu a folha cinza
	/// pintada, que e como o proprio DM resolve todo caso de cor propria (`GodOfDestruction.dm:389`,
	/// `UltraEgo.dm:353`, `IcerTransform.dm:167` -- todos `god - grey` mais uma cor).
	/// ============================================================================================
	///
	/// FICAM DE FORA, e nao por esquecimento: o Prodigial (Mistico e Beast chamam `Revert()` ao entrar
	/// -- `Mystic.dm:33` e `:138` -- e vivem com `godki.usage` sem forma Saiyajin, mas o dono nao os
	/// listou), o Ultra Instinct, o Ultra Ego e a escada Saiyajin comum. A tabela e dele.
	/// </summary>
	public static Colada[] Coladas(FormaDef? d) => d == null || d.Id == IdBase ? [] : d.Linha switch
	{
		LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal =>
			[new(FolhaColada.PoderLendario), new(FolhaColada.Ameacadora, VerdeDaAmeaca)],

		LinhaDeForma.GodKi => d.Ordem >= OrdemDoKiSobreOSuperSaiyajin
			? [new(FolhaColada.DeusAzul)] : [new(FolhaColada.Deus)],

		LinhaDeForma.GodKiRose => d.Ordem >= OrdemDoKiSobreOSuperSaiyajin
			? [new(FolhaColada.Ameacadora, RosaDaColada)] : [new(FolhaColada.Deus)],

		_ => [],
	};

	/// <summary>
	/// ============================ A COR DO RABO E OUTRA TABELA ============================
	/// O rabo NAO segue a cor do cabelo. O dono viu o resultado de eu ter ligado os dois: "o rabo
	/// no ssj ta marrom e n amarelo" -- era a `FormaDef.Cabelo` caindo no rabo, e no Oozaru esse
	/// campo e `5a3a1b`, marrom, porque descreve a PELAGEM.
	///
	/// No original a tabela do rabo e propria e minuscula (`SaiyanObjects.dm:100-118`): dourado
	/// na linha Saiyajin, verde no Legendary do FULL POWER pra cima, azul/rosa quando ha ki divino --
	/// e NENHUMA cor em todo o resto. Oozaru, Frost Demon, Majin, UI, UE: rabo sem tinta.
	///
	/// Nulo = nao pinta. E o caso mais comum de proposito.
	///
	/// ============================ ELA DEIXOU DE SER UMA TABELA E VIROU UMA DERIVACAO ============================
	/// Era um `switch` por (linha, faixa de Ordem) e errava em tres pontos ao mesmo tempo -- o
	/// Wrathful saia dourado, o C-Type saia verde e o SSG saia AZUL. Nenhum dos tres era erro de
	/// digitacao: eram tres tentativas de descrever, por faixa de numero, uma regra que no DM e outra.
	///
	/// A regra do DM, em uma frase: **o rabo recebe a MESMA tinta que o cabelo**, e quando o cabelo
	/// nao leva tinta nenhuma mas foi ERGUIDO (a arte de Super Saiyajin), o rabo fica dourado. E so.
	/// Derivando dela, os tres casos saem certos sem nenhum caso especial -- e um degrau novo tambem.
	/// ======================================================================================================
	///
	/// DIFERENCA ACEITA E ANOTADA: o DM faz `icon -= rgb(100,100,100)` antes de somar o azul/rosa
	/// tres vezes; o nosso shader so SOMA (`Personagem.gdshader:130`). Entao o Blue e o Rose saem
	/// mais saturados e menos escuros que la. Trocar isso exigiria a assinatura da tinta aceitar
	/// negativo, em tres chamadores -- nao vale o preco agora.
	/// ==============================================================================
	/// </summary>
	public static string? CorDoRabo(FormaDef? d)
	{
		if (d == null || d.Id == IdBase) return null;

		// ============================ UMA LINHA NAO TEM RABO DE FORMA ============================
		// A tabela inteira do rabo mora em `SaiyanObjects.dm:98-118` e e chaveada por `ssj`, `lssj` e
		// `godki` -- as vars da escada Saiyajin. O Oozaru nao escreve nenhuma delas: ele anda com o
		// rabo na cor base, sem tinta. (E ele nem PODERIA cair na derivacao de baixo -- a `Cabelo`
		// dele e `5a3a1b`, marrom, porque descreve a PELAGEM; ver o cabecalho desta funcao.)
		//
		// ============================ A LINHA DO MISTICO SAIU DAQUI ============================
		// Ela estava nesta lista pelo mesmo motivo que as divinas estiveram -- e a leitura do DM
		// continua correta: o Prodigial CHAMA `Revert()` ao entrar (`Mystic.dm:33` e `:138`), zera
		// `ssj`/`lssj`, e a tabela do `SaiyanObjects.dm` nao tem o que responder pra ele.
		//
		// O DONO PEDIU OUTRA COISA, e ela e DIVERGENCIA declarada e nao porte: *"o rabo do beast n ta
		// branco"* -- tem que ficar branco.
		//
		// E DE NOVO NAO PRECISOU DE REGRA NOVA, exatamente como no Ultra Instinto e no Ultra Ego logo
		// abaixo: bastou tirar a linha da lista, e a derivacao que ja existia ("o rabo recebe a MESMA
		// tinta que o cabelo") responde os DOIS degraus sozinha --
		//
		//   `beast`   -> `b6bac4` (o branco-gelo do `Mystic.dm:76-85`), e a cauda, que e escura
		//                (`313131` e `4d4d4d` medidos em `Tail.png`), SOMA pra branco: `31`+`b6` =
		//                (231,235,245) e `4d`+`b6` estoura os tres canais em 255. Branco de verdade,
		//                nao um cinza claro;
		//   `mistico` -> sem tinta de cabelo (`CorDoCabelo` devolve nulo -- a entrada nao tem `Cabelo =`)
		//                e fora da `escadaQueEscreveSsjOuLssj` la embaixo, entao **nulo**: rabo intacto.
		//                E isso importa tanto quanto o branco -- o dono NAO citou o Mistico, cujo
		//                enunciado inteiro e *"tudo igual a base"*. Ele nao pode ganhar cor de brinde.
		//
		// ASSIMETRIA CONHECIDA, e ela nao muda o resultado: o CABELO do Beast usa MATIZ
		// (`ModoDoCabelo.TrocarERecolorir` -- somar nao faz branco-gelo sobre loiro de SSJ2) e o rabo
		// sai por SOMA. Os dois tons da cauda sao escuros o bastante pra a soma saturar, entao os dois
		// canais chegam em branco por caminhos diferentes. Num rabo redesenhado mais claro isso deixa
		// de valer -- e por isso a bancada mede o PNG e nao compara strings.
		//
		// ============================ O ULTRA INSTINCT E O ULTRA EGO SAIRAM DAQUI ============================
		// Eram quatro linhas, e as duas divinas estavam nesta lista com o motivo certo -- **no DM o rabo
		// delas nao muda mesmo**: a tabela de `SaiyanObjects.dm` so olha `ssj`/`lssj`, e nem
		// `UltraInstinct.dm` nem `UltraEgo.dm` tem uma unica linha sobre cauda (conferido por varredura:
		// a unica ocorrencia de "tail" nos dois arquivos e `UltraEgo.dm:301`, que exclui a cauda da
		// contagem de membros).
		//
		// O DONO PEDIU OUTRA COISA, e ela e DIVERGENCIA declarada e nao porte: *"Perfected Ultra
		// Instinct: o RABO fica BRANCO"* e *"Ultra Ego: o RABO fica ROXO tambem"*.
		//
		// E O QUE CHAMA A ATENCAO E QUE NAO PRECISOU DE REGRA NOVA: bastou tirar as duas linhas da
		// lista, e a derivacao que ja existia ("o rabo recebe a MESMA tinta que o cabelo") responde os
		// QUATRO degraus na cor exata que ele pediu, sem um `if` por id:
		//
		//   `ui_perfected` -> `b9becb` (o `/hairs/uisilver` do `UltraInstinct.dm:291`) e o rabo, que e
		//                     cinza-escuro (`313131`/`4d4d4d` medidos em `Tail.png`), soma pra BRANCO;
		//   `ui_sign`      -> sem tinta de cabelo, entao sem tinta de rabo -- e o dono nomeou o
		//                     **Perfected**, nao a linha;
		//   `ultra_ego`    -> `8c32be` (`UltraEgo.dm:387-392`), que soma pro roxo claro `bd63ef`;
		//   `destroyer`    -> sem tinta, e por DOIS motivos que concordam: **o dono nao pediu a
		//                     Destroyer** (ele nomeou o Ultra Ego), e o DM diz que o cabelo base E a
		//                     diferenca visual entre as duas (`UltraEgo.dm:395-396`). Fica intocada.
		//
		// Ou seja o enunciado do dono e, por acaso, exatamente a regra que o arquivo ja tinha: quem
		// pinta o cabelo pinta o rabo. O que era excecao virou caso comum.
		//
		// ============================ NAO "CONSERTE" ISTO EM NOME DA FIDELIDADE ============================
		// Isto JA ACONTECEU uma vez: um agente anterior leu `SaiyanObjects.dm:89-130`, viu que a tabela
		// do rabo so olha `ssj`/`lssj`/`godki`, concluiu (corretamente!) que o DM nao pinta cauda de UI
		// nem de UE, e devolveu as duas linhas pra a lista de exclusao logo abaixo -- apagando um pedido
		// EXPLICITO do dono com uma justificativa impecavel.
		//
		// A regra: quando o dono PEDE uma coisa que o original nao faz, a palavra dele vence o DM. Ler o
		// `SaiyanObjects.dm` e concluir que estas duas cores "estao erradas" nao e achado, e o caminho
		// pra reintroduzir o bug. O que o DM decide aqui e o RECORTE (quem tem tinta de cabelo), nao a
		// existencia da cor.
		//
		// O bench guarda o conjunto (`RoboDeForma.cs`, bloco "2. O RABO"): ele reprova tanto quem
		// devolver as duas linhas pra a exclusao quanto quem "simetrizar" pintando as quatro. **O mesmo
		// vale agora pra o Beast** -- e a linha que guarda ele mede o pixel da cauda, nao o hexa.
		// ==================================================================================================
		if (d.Linha is LinhaDeForma.Oozaru) return null;

		// QUEM TEM CORPO PROPRIO JA TRAZ O PROPRIO RABO. `SaiyanObjects.dm:134`: `else if(container.ssj
		// >= 4) alpha = 0` -- o overlay do rabo some porque a folha do corpo SSJ4 ja o desenha. Aqui a
		// pergunta e por CAMPO e nao por degrau, e cobre o Legendary Primal 4/5/6 pelo mesmo motivo.
		//
		// E ELA E `FolhaTrazORabo` E NAO `Corpo != Nenhum`: o corpo MUSCULOSO tambem preenche este
		// campo e NAO desenha rabo nenhum -- ver o cabecalho da funcao. Escrito do jeito antigo, os
		// nove degraus inchados (grades + Legendary) perderiam o rabo ao inchar.
		if (FolhaTrazORabo(d.Corpo)) return null;

		// ============================ O RABO SEGUE A TINTA DO CABELO ============================
		// Regra do dono ("o rabo nas transformaçoes q mudam o cabelo mudam a cor do rabo tb") e e o
		// que o DM faz: a receita do rabo em `SaiyanObjects.dm:102-111` e LITERALMENTE a mesma do
		// cabelo em `:11-20` -- `-rgb(100,100,100)` e o mesmo azul (ou rosa) tres vezes. Derivar em
		// vez de repetir a tabela e o que garante que elas nao possam divergir.
		//
		// E DAQUI QUE SAI O VERMELHO DO SSG, que era a segunda queixa do dono. **O DM nao pinta esse
		// rabo**: a tinta divina esta presa dentro do `if(container.ssj)` da `:101`, e em SSG `ssj` e
		// 0 -- colateral do ramo, nao decisao. O port pintava AZUL ali (a linha GodKi inteira caia em
		// `0d49ee`), que e errado dos dois jeitos. Com a derivacao ele sai `e2331c`, a mesma cor que
		// o cabelo recebe.
		if (CorDoCabelo(d) is { } tinta) return tinta;

		// ============================ E QUEM ERGUE O CABELO DE SUPER SAIYAJIN FICA DOURADO ============================
		// `icon += rgb(218, 218, 38)` (`SaiyanObjects.dm:113` no ramo `ssj`, `:116` no `lssj == 2`).
		//
		// A GUARDA E O SUFIXO, e e ela que conserta o Wrathful: o `lssj == 1` nao aparece na tabela do
		// rabo -- o `else if(container.lssj)` da `:114` so tem caso pra `==2` e `>=3` --, e ele e
		// exatamente a forma que mantem o cabelo BASE. Ou seja "quem nao troca o cabelo nao pinta o
		// rabo" nao e uma coincidencia da tabela: e a tabela. O port dava dourado a ele por uma faixa
		// de `Ordem`, e o dono viu.
		//
		// ============================ E A SEGUNDA GUARDA NASCEU COM O ULTRA INSTINCT ============================
		// O dourado tem que ser POR ESCADA e nao so por sufixo, e isso so apareceu quando o UI saiu da
		// lista de exclusao la em cima: o `ui_sign` TEM sufixo (`UI`) e nao tem tinta -- ele cairia aqui
		// e sairia com rabo DOURADO, uma forma prateada com a cauda de Super Saiyajin.
		//
		// A causa e que os dois sufixos do Ultra Instinct nao nomeiam uma variante de penteado: nomeiam
		// UM ARQUIVO (ver `SufixoDoUltraInstinto`). "Ergueu o cabelo de Super Saiyajin" e outra coisa
		// que "trocou de sprite", e ate aqui as duas eram a mesma pergunta porque so escadas Saiyajin
		// chegavam neste ponto.
		//
		// A LISTA E POSITIVA de proposito: ela e o `if(container.ssj)` / `else if(container.lssj)` do
		// `SaiyanObjects.dm:101` e `:114` -- as linhas que escrevem aquelas duas vars. Escrita como
		// `is not (UltraInstinct or UltraEgo)` ela ficaria certa hoje e erraria calada na primeira linha
		// nova que nao fosse Saiyajin.
		bool escadaQueEscreveSsjOuLssj =
			d.Linha is LinhaDeForma.Saiyajin or LinhaDeForma.Futuro
					or LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal
					or LinhaDeForma.GodKi or LinhaDeForma.GodKiRose;

		return escadaQueEscreveSsjOuLssj && d.SufixoDoCabelo.Length > 0 ? DouradoDoRabo : null;
	}

	/// <summary>
	/// O `rgb(218,218,38)` de `SaiyanObjects.dm:113`. Uma escrita so: ele e a resposta de sete
	/// degraus, e a hora em que virasse dois literais seria a hora em que um deles envelheceria.
	/// </summary>
	private const string DouradoDoRabo = "dada26";

	/// <summary>
	/// ============================ A TINTA DO CABELO -- E SO ELA ============================
	/// Devolve o hexa que a forma SOMA no cabelo, ou NULO quando ela nao pinta. Nulo e o caso mais
	/// comum de proposito: a escada Saiyajin inteira nao pinta nada, ela TROCA a arte.
	///
	/// ============================ ISTO NAO REABRE O "NAO PINTE O CABELO" ============================
	/// O dono ja vetou uma tinta de cabelo, e o veto continua de pe: *"n coloque efeitos sobre o
	/// cabelo, somente no contorno dele"*. Aquilo era outra coisa -- era dourado sobre o penteado
	/// NORMAL pra fingir um Super Saiyajin em quem nao tem variante desenhada, e o resultado era um
	/// penteado comum amarelo. O que ha aqui e a tinta que a PROPRIA FORMA declara no original, e ela
	/// existe em cinco lugares e em nenhum outro:
	///
	///   * `HairObject.dm:73-75` -- SSG: o cabelo base + `rgb(226,51,28)`;
	///   * `SaiyanObjects.dm:11-20` -- Blue/Rose: o cabelo SSJ + `rgb(13,73,238)` / `rgb(238,51,130)`;
	///   * `SaiyanObjects.dm:83-87` -- Legendary: o `ussjhair` + `rgb(0,110,0)`;
	///   * `UltraEgo.dm:387-392` -- Ultra Ego: o cabelo base + `rgb(140,50,190)`;
	///   * `UltraInstinct.dm:288-293` -- UI Perfected sem estilo Goku: base + `rgb(185,190,200)`.
	///
	/// A prova de que a regra continua valendo e o que NAO esta na lista: `ssj1`, `ssj2`, `ssj3`,
	/// `ssj4`, os grades e o `future_ssj` devolvem nulo. Nenhum Super Saiyajin do jogo ganha tinta.
	/// ==========================================================================================
	///
	/// LE A <see cref="FormaDef.Cabelo"/>, que ate esta varredura era campo MORTO -- tinha um hexa em
	/// todas as 36 entradas e nenhum leitor fora das bancadas que so conferiam o comprimento. Ele
	/// passou a significar a tinta, e vazio passou a significar "nao pinta". Repurpor foi melhor que
	/// criar campo: um campo novo deixaria o velho mentindo ao lado dele.
	/// </summary>
	public static string? CorDoCabelo(FormaDef? d) =>
		d == null || d.Cabelo.Length == 0 ? null : d.Cabelo;

	/// <summary>
	/// ============================ A COR DOS OLHOS, FORMA A FORMA ============================
	/// Devolve o hexa que a forma SOMA no olho, ou nulo quando ela nao mexe nele. Nulo continua sendo
	/// um caso real -- Mistico, Prodigial, Oozaru e a base --, so deixou de ser a maioria.
	///
	/// ============================ ESTE EIXO E DO DONO, E NAO DO DM ============================
	/// **No original UMA forma mexe nos olhos, e e o Ultra Instinct.** Varrido: as unicas escritas de
	/// `/obj/overlay/eyes` fora da criacao e do relog sao `ui_apply_eyes()` / `ui_restore_eyes()`
	/// (`UltraInstinct.dm:306-313`) e a limpeza do Majin (`MajinSaga.dm:244`). Nem SSJ, nem Legendary,
	/// nem SSG, nem Ultra Ego tocam em olho no DM.
	///
	/// Entao a tabela abaixo e desenho novo, ditado pelo dono, e SO a linha do Ultra Instinct tem um
	/// hexa vindo do arquivo. As outras oito estao escolhidas aqui e o criterio de cada uma esta na
	/// constante -- ver <see cref="VerdeDoOlhoSuperSaiyajin"/> pro raciocinio que vale pra todas.
	/// ==================================================================================================
	///
	/// ============================ "SEM IRIS" E UMA COR, E ISSO E MEDIDO ============================
	/// O dono pediu LSSJ e Primal com *"olhos BRANCOS -- sem iris"*, e disse pra olhar o sprite antes de
	/// escolher como representar. (Depois ele disse QUANDO: so enquanto a furia dirige o corpo -- ver a
	/// sobrecarga <see cref="CorDoOlho(FormaDef?, bool)"/>. A medida abaixo continua valendo inteira, o
	/// que mudou foi de quem o branco e.) Olhado, e o sprite responde sozinho:
	///
	///   * `Eyes_Black.png` (a folha inteira, 224x192, 42 quadros) tem **90 pixels opacos**, todos
	///     `#000000` -- dois por quadro nos perfis, quatro de frente, zero nos quadros de costas. A
	///     camada de olhos do jogo **e a IRIS, e mais nada**;
	///   * o CORPO ja desenha o olho completo por baixo: cilio preto em `y=12`, esclerotica `#fcfdfd`
	///     em `x=13` e `x=18`, e uma iris AZUL (`#0099cc`/`#0066cc`) exatamente sob os pixels da
	///     camada. Medido em `NewPaleMale`, `BaseBlackMale`, `BaseTanFemale` e `BaseWhiteMale`.
	///
	/// **Por isso ESCONDER a camada nao apaga iris nenhuma: ela revela a iris azul do corpo.** Apagar a
	/// iris e PINTA-LA da cor da esclerotica que esta encostada nela -- e como a tinta e soma sobre
	/// preto (`clamp(0 + tinta)`), o hexa que chega na tela e exatamente o que se escreve aqui. Dai
	/// <see cref="BrancoSemIris"/> ser o `fcfdfd` medido no corpo e nao um `ffffff` redondo: qualquer
	/// outro valor deixaria um degrau de cor no meio do olho, que e uma iris palida em vez de nenhuma.
	///
	/// Nao ha canal novo, nem campo novo, nem camada escondida: "sem iris" e uma cor.
	/// ==========================================================================================
	///
	/// ============================ DERIVADO POR LINHA, COM DOIS CORTES E UMA EXCECAO ============================
	/// Os dois cortes ja existiam e sao reusados de proposito -- <see cref="FormaDef.Corpo"/>
	/// separa os tres SSJ4 dentro da escada (a mesma pergunta que o <see cref="CorDoRabo"/> faz), e o
	/// <see cref="OrdemDoKiSobreOSuperSaiyajin"/> separa SSG de Blue/Rose (o mesmo do
	/// <see cref="Folha"/>, do <see cref="ParDoContorno"/> e das <see cref="Coladas"/>).
	///
	/// A EXCECAO E UMA SO e o dono a nomeou: o `wrathful`. Ela nao sai de campo nenhum porque ela nao
	/// e consequencia de nada -- e a forma que fica com o corpo e o cabelo da base dentro de uma linha
	/// que ficaria de olhos brancos.
	/// ======================================================================================================
	///
	/// TRES CORES SAO DERIVADAS DA TINTA DO CABELO (<see cref="CorDoCabelo"/>) em vez de escritas: o
	/// vermelho dos dois SSG e o roxo do Ultra Ego. Elas podem, e as outras nao, e o criterio e o MODO:
	/// estas tres sao <see cref="ModoDoCabelo.Tingir"/> -- tinta feita pra SOMAR sobre um molde PRETO,
	/// que e exatamente o que o olho e. As do Blue/Rose/Legendary sao tinta de MATIZ (ver
	/// <see cref="AzulDoCabeloDivino"/>), onde o hexa e o PISO de uma rampa que o desenho dourado
	/// multiplica por ate 1,81 -- num olho preto nao ha rampa, e o piso vai cru pra tela: o `082b8d`
	/// do Blue Evolution daria um azul-marinho quase invisivel em dois pixels. Por isso o azul e o
	/// rosa do olho sao constantes proprias e o vermelho do SSG nao e.
	///
	/// E ISSO TAMBEM RESOLVE O CORTE DO ULTRA EGO sem faixa de Ordem: o DM diz que a Destroyer nao
	/// pintar o cabelo e *"a maior diferenca visual entre as duas formas"* (`UltraEgo.dm:395-396`), e
	/// perguntar pela tinta faz a Destroyer cair fora sozinha -- ela nao tem nenhuma.
	///
	/// FICA DE FORA a guarda `if(hascustomeye)` do `:306`: no DM ela protege quem SUBIU um olho
	/// proprio (arte enviada pelo jogador), e este port nao tem esse conceito -- toda cor de olho aqui
	/// e escolha de criacao, que no DM tambem recebe a prata. Anotado por ser a unica linha do
	/// `ui_apply_eyes` que nao veio.
	/// </summary>
	public static string? CorDoOlho(FormaDef? d) => CorDoOlho(d, semRedeas: false);

	/// <summary>
	/// A MESMA TABELA, SABENDO QUEM DIRIGE O CORPO.
	///
	/// ============================ O BRANCO MUDOU DE DONO: ELE E A POSSE, E NAO A LINHA ============================
	/// Ate aqui as duas linhas lendarias eram brancas SEMPRE. O dono corrigiu: *"quando o jogador tem
	/// o controle a pupila verde volta, deixa de ser branca"*. Ou seja o `fcfdfd` nunca foi a cor do
	/// Legendary -- ele e a cor de **um corpo que a furia esta dirigindo**, e a linha inteira parecia
	/// branca so porque o descontrole ainda nao existia neste port.
	///
	/// **E O DM DIZ ISSO EM VOZ ALTA, nas duas pontas.** A transformacao: *"[src]'s eyes go cold and
	/// empty as a monstrous Legendary fury takes hold"* (`lssjbuff.dm:289`). E o berserk, no instante
	/// em que a IA assume: *"Os olhos de [src] se apagam -- a furia Legendary assumiu o controle!"*
	/// (`:609`). O olho apagado sempre foi o INDICADOR do descontrole no original; o que faltava aqui
	/// era o descontrole.
	///
	/// O VERDE E O DA ESCADA (<see cref="VerdeDoOlhoSuperSaiyajin"/>) e nao um verde proprio: com as
	/// redeas na mao, um Legendary e um Super Saiyajin -- e o dono disse "a pupila verde **volta**",
	/// que e a palavra de quem esta falando de uma cor que ja existe.
	///
	/// ============================ E O AMARELO DO WRATHFUL SOBREVIVE ============================
	/// A posse e perguntada **antes** da excecao por id, e a ordem e a regra: o Wrathful e a primeira
	/// forma da linha e a mais crua de todas (maestria zero), entao ele e o degrau que MAIS perde o
	/// controle. Se o amarelo viesse antes, a unica forma em que o jogador vai passar a maior parte
	/// do tempo possuido seria justamente a que nao mostra isso.
	///
	/// Fora da posse ele continua amarelo, que e o pedido anterior do dono (*"excecao: `wrathful`:
	/// AMARELO"*) -- as duas regras nao se cruzam: uma diz a cor do dono do corpo, a outra diz que o
	/// corpo nao tem dono agora.
	/// ==================================================================================
	///
	/// <paramref name="semRedeas"/> e o `EntityState.SemRedeas` do fio -- o MESMO bit que ja viaja pra
	/// zona inteira desde o Oozaru (`Protocol.cs:1136`, `flags2 &amp; 0x20`). Nao ha canal novo: a cor do
	/// olho e informacao de jogo, e ela chega nos outros clientes pelo caminho que o macaco abriu.
	/// </summary>
	public static string? CorDoOlho(FormaDef? d, bool semRedeas)
	{
		if (d == null || d.Id == IdBase) return null;

		// A FURIA DIRIGINDO APAGA A IRIS -- ver o cabecalho, e repare que a pergunta e
		// `EhDescontrolavel` e nao "a linha e lendaria": as duas dao o mesmo hoje, mas quem responde
		// "esta forma pode fugir do controle?" e o dono da regra (<see cref="FuriaLendaria"/>), e uma
		// linha nova que entrasse naquele sistema ja nasceria mostrando o olho certo.
		//
		// O OOZARU NAO CAI AQUI, e nao precisa de guarda: ele nao tem camada de olhos desenhada (cai no
		// `_ => null` la embaixo) e a linha dele nao e descontrolavel por este sistema -- a fera tem o
		// prazo dela.
		if (semRedeas && FuriaLendaria.EhDescontrolavel(d)) return BrancoSemIris;

		// A UNICA EXCECAO POR ID DESTE ARQUIVO INTEIRO, e ela foi nomeada pelo dono. Vem ANTES do
		// `switch` porque a linha dela devolve o verde da escada: escrita como um `when` la dentro ela
		// viraria mais um corte por campo, e nao ha campo que a explique -- o Wrathful e a forma que
		// nao muda nada no corpo, e "nada" nao e um valor que se derive.
		if (d.Id == IdWrathful) return AmareloDoOlhoDoWrathful;

		return d.Linha switch
		{
			// OS TRES SSJ4, pelo campo e nao por `Ordem >= 40`: e a MESMA pergunta que o `CorDoRabo`
			// faz duas funcoes acima ("quem tem corpo proprio"), e ela e o que separa a pelagem
			// vermelha do resto da escada. O corte por numero daria o mesmo hoje e erraria no dia em
			// que entrasse um degrau entre o SSJ3 e o SSJ4.
			//
			// E ELE NAO SAI DA `Aura`, que seria o atalho: as tres auras do SSJ4 sao `ffc93a`,
			// `ffe14d` e **`ff2d2f`** -- o Limit Breaker sairia de olho VERMELHO, e o dono disse
			// "SSJ4 (as tres) amarelado".
			//
			// A PERGUNTA E POR VALOR (`== Ssj4`) e nao "tem corpo proprio": os grades 2 e 3 sao
			// desta mesma linha e hoje tambem trocam de corpo (o MUSCULOSO). Escrita como
			// `Corpo != Nenhum`, eles sairiam de olho amarelo de SSJ4 em vez do verde da escada.
			LinhaDeForma.Saiyajin or LinhaDeForma.Futuro when d.Corpo == CorpoDeForma.Ssj4
				=> AmareloDoOlhoDoSsj4,

			// A ESCADA SAIYAJIN INTEIRA, com a linha do FUTURO junto -- o mesmo precedente da `Folha`,
			// do `CorDoRabo` e da `CorDosRaios`: ela e a escada Saiyajin da linhagem do futuro, e
			// separa-la faria o `future_ssj` ser o unico Super Saiyajin de olho de outra cor.
			LinhaDeForma.Saiyajin or LinhaDeForma.Futuro => VerdeDoOlhoSuperSaiyajin,

			// AS DUAS LINHAS LENDARIAS **COM AS REDEAS NA MAO**, sem corte de degrau. O branco que
			// morava aqui subiu pra a guarda de posse la em cima (ver o cabecalho deste metodo): quem
			// dirige o proprio corpo em Legendary e um Super Saiyajin, e le o verde da escada.
			//
			// E ELAS CAEM NO MESMO BRACO que a escada Saiyajin de proposito, em vez de virarem um
			// `or` na linha de cima: as duas frases sao diferentes. La e "a escada e verde"; aqui e "a
			// furia, quando obedece, tambem e". Um dia so uma delas muda.
			LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal => VerdeDoOlhoSuperSaiyajin,

			// AS DUAS ESCADAS DIVINAS, no mesmo corte que a folha de aura e o contorno usam. Abaixo
			// dele esta o SSG, e o SSG e VERMELHO nas duas -- e o mesmo `e2331c` que o cabelo e o
			// rabo dele ja recebem, buscado e nao repetido.
			LinhaDeForma.GodKi     => d.Ordem >= OrdemDoKiSobreOSuperSaiyajin
										? AzulDoOlhoDivino : CorDoCabelo(d),
			LinhaDeForma.GodKiRose => d.Ordem >= OrdemDoKiSobreOSuperSaiyajin
										? RosaDoOlhoDivino : CorDoCabelo(d),

			// A LINHA INTEIRA, e esta e a unica cor do bloco que veio do arquivo: o `Buff()` do
			// `/obj/buff/UltraInstinct` (`:481`) **nao ramifica por estagio** -- Sign e Perfected
			// banham o olho da mesma prata, que e o "as duas" do dono.
			LinhaDeForma.UltraInstinct => PrataDoOlhoDoInstinto,

			// SO O DEGRAU QUE PINTA, e por isso a pergunta e a tinta e nao a Ordem -- ver o cabecalho.
			LinhaDeForma.UltraEgo => CorDoCabelo(d),

			// SO O BEAST DA LINHA DO MISTICO, no MESMO corte que o `ParDoContorno` e a `RaivaExigida`
			// ja usam pra isolar ele: dos dois degraus, so a Fera pede ki divino MADURO
			// (`GodkiRoyalePct`). Nao ha `if` por id nesta funcao alem do Wrathful, que o dono nomeou,
			// e este nao seria o segundo.
			//
			// PEDIDO LITERAL DO DONO -- *"o olho do beast era pra ser vermelho"* -- e ele e o unico
			// canal da Fera que nao deriva de nada: nenhuma cor que ela ja tem e vermelha (cabelo
			// `b6bac4`, chama `7d5af0`, contorno `3f8cff`/`b163ff`, faisca lilas). Ver
			// `VermelhoDaFera` pro porque de nao ser o `VermelhoDoLimitBreaker`.
			//
			// E O MISTICO CONTINUA NO `_` LOGO ABAIXO, que e metade do ponto: o enunciado dele e
			// *"igual a base, nada muda"* e o dono citou a Fera, nao a linha.
			LinhaDeForma.Mistico when d.PedeGodKi >= GodkiRoyalePct => VermelhoDaFera,

			// O MISTICO NAO MEXE NO OLHO, e e enunciado do dono ("igual a base, nada muda"). Ele cai
			// no `_` junto com o Oozaru, que nem olho desenhado tem.
			_ => null,
		};
	}

	/// <summary>
	/// COMO ESTA FORMA VESTE O CABELO. Ver <see cref="ModoDoCabelo"/> pra o desenho inteiro e pra o
	/// porque de ser derivado.
	/// </summary>
	public static ModoDoCabelo ModoDoCabelo(FormaDef? d)
	{
		if (d == null) return Forms.ModoDoCabelo.Base;

		bool troca = d.SufixoDoCabelo.Length > 0;
		bool tinge = CorDoCabelo(d) != null;

		if (!troca) return tinge ? Forms.ModoDoCabelo.Tingir : Forms.ModoDoCabelo.Base;
		if (!tinge) return Forms.ModoDoCabelo.Trocar;

		return d.Linha switch
		{
			// A ARTE DO ULTRA INSTINCT E DE UM PERSONAGEM SO (o Goku), e nao uma variante do penteado
			// de cada um -- por isso a tinta dele e ALTERNATIVA e nao acumulo. Derivado da LINHA pelo
			// mesmo criterio das outras derivacoes deste arquivo: um degrau novo de UI ja nasce certo.
			LinhaDeForma.UltraInstinct => Forms.ModoDoCabelo.TrocarOuTingir,

			// A LINHA DO MISTICO: dos dois degraus, so o Beast tem tinta (o Mistico devolve nulo em
			// `CorDoCabelo` e nem chega aqui), e a dele e a receita de grayscale do `Mystic.dm:81-83`.
			// Mesma derivacao que o `NasceDaRaiva` ja usa pra isolar o Beast dentro da linha.
			LinhaDeForma.Mistico => Forms.ModoDoCabelo.TrocarERecolorir,

			_ => Forms.ModoDoCabelo.TrocarETingir,
		};
	}

	/// <summary>
	/// Neutro. `d == null` e "sem forma nenhuma", e nesse caso ninguem desenha contorno nem raio --
	/// quem chama passa FORCA zero junto. A cor existe so pra a assinatura nao precisar ser anulavel.
	/// </summary>
	private const string BrancoNeutro = "ffffff";

	/// <summary>
	/// O dourado do SSJ1 (`ffd24a`), promovido a cor da ESCADA inteira pro contorno. As auras dos
	/// degraus variam de tom de proposito (`ffcf3a` no Grade 2, `fff08a` no SSJ3) -- e era essa
	/// variacao vazando pro contorno que fazia o brilho mudar de cor subindo a escada.
	/// </summary>
	private const string AmareloSaiyajin = "ffd24a";

	/// <summary>
	/// O azul-branco da faisca das ESCADAS DE SANGUE -- as quatro formas que a acendem menos o Limit
	/// Breaker (ver <see cref="CorDosRaios"/>). NUMERO ESCOLHIDO POR MIM e precisa do olho do dono: no
	/// original a faisca nao tem cor de aura nenhuma, e uma folha propria (`electrictyeffects`, e o
	/// SSJ3 soma duas), entao nao ha hexa pra copiar de la. Escolhi claro e lavado pra ele aparecer
	/// POR CIMA da aura dourada, que e grande e clara -- um azul saturado sumiria dentro dela.
	///
	/// E ELE VALE TAMBEM SOBRE A AURA VERDE do Legendary Primal, que foi a correcao do dono: o
	/// `primal_legendary2` saia verde-sobre-verde (a faisca herdava a aura) e desaparecia dentro da
	/// propria chama -- o mesmo motivo do azul, do outro lado do circulo cromatico.
	/// </summary>
	private const string AzulDaFaisca = "8fe3ff";

	/// <summary>
	/// ==================== AS CORES DE CONTORNO DAS OUTRAS LINHAS ====================
	/// Uma cor POR LINHA, ditada pelo dono. Cada uma esta escrita AQUI mesmo quando a
	/// <see cref="FormaDef.Aura"/> do degrau ja daria o mesmo tom por coincidencia -- e a
	/// coincidencia e justamente o perigo: no dia em que alguem escurecer a aura do Rose pra a chama
	/// ficar melhor, o contorno ia junto e ninguem ligaria uma coisa a outra. Foi esse acoplamento
	/// que criou a funcao. A regra escrita quebra ALTO (o valor muda aqui) ou nao quebra.
	///
	/// O outro ganho e o mesmo do <see cref="AmareloSaiyajin"/>: um tom SO por escada. As auras dos
	/// degraus variam de proposito (o Blue e `3ad2ff` e o Blue Evolution `1c7cff`), e era essa
	/// variacao vazando pro contorno que fazia o brilho mudar de cor subindo a linha.
	/// ================================================================================
	///
	/// O VERMELHO DO LIMIT BREAKER RESPONDE POR TRES DESENHOS, e e a unica cor do arquivo que faz
	/// isso: contorno (<see cref="ParDoContorno"/>), faisca (<see cref="CorDosRaios"/>) e -- por
	/// coincidencia de valor, nao por leitura -- a <see cref="FormaDef.Aura"/> dele no catalogo, que
	/// tambem e `ff2d2f`. Trocar o hexa AQUI move o contorno e o raio e deixa a aura pra tras; e
	/// proposital que sejam duas escritas (a aura e dado do catalogo), mas quem mexer numa tem que
	/// olhar a outra. A bancada `--diagforma` cobra as duas.
	/// </summary>
	private const string VermelhoDoLimitBreaker = "ff2d2f";

	/// <summary>
	/// O verde do Legendary, para a linha inteira -- do Wrathful ao Limit Breaker primal. E o tom do
	/// C-Type (`4dff5a`), o MEIO da faixa que a linha percorre (`76ff7a` .. `00ff2a`): puxar pro
	/// mais claro apagaria o contorno dos degraus altos, e pro mais escuro sumiria no corpo verde.
	///
	/// A LINHA INTEIRA, sem o corte do <see cref="CorDoRabo"/>: la o Wrathful e dourado porque no DM
	/// o RABO so verdeja no C-Type. A aura dele ja e verde desde o primeiro degrau, e contorno
	/// dourado sobre aura verde nao descreve forma nenhuma.
	/// </summary>
	private const string VerdeLegendary = "4dff5a";

	/// <summary>
	/// O azul do Blue e o rosa do Rose. Sao a MESMA escada em duas cores (ver
	/// <see cref="LinhaDeForma.GodKiRose"/>), entao andam sempre juntos -- inclusive no corte do
	/// <see cref="OrdemDoKiSobreOSuperSaiyajin"/>.
	/// </summary>
	private const string AzulDivino = "3ad2ff";

	/// <inheritdoc cref="AzulDivino"/>
	private const string RosaDivino = "ff7ac6";

	/// <summary>
	/// O ROXO do Ultra Ego, entre o `9b4dff` do Destroyer e o `b96bff` do Ultra Ego -- o mesmo
	/// criterio do <see cref="VerdeLegendary"/>: o meio da faixa que a linha percorre.
	/// </summary>
	private const string RoxoDoEgo = "a95cff";

	/// <summary>
	/// O BRANCO do Ultra Instinct. NAO e o <see cref="BrancoNeutro"/>, e a diferenca e de proposito:
	/// branco puro no contorno some contra cenario claro (ja aconteceu neste projeto, e so a foto
	/// mostrou). Este tem um fio do prateado-azulado da linha, o bastante pra o contorno existir
	/// contra o ceu e ainda ler como branco contra o corpo.
	/// </summary>
	private const string BrancoDoInstinto = "f0f6ff";

	/// <summary>
	/// ============================ AS DUAS CORES DA FERA ============================
	/// O Beast e a UNICA forma do jogo cujo contorno nao e uma cor: e uma ANIMACAO de cor. Pedido do
	/// dono -- *"beast fica trocando lentamente entre azul e roxo em uma transicao gradual"*.
	///
	/// O CORE SO DIZ QUAIS SAO AS DUAS PONTAS E QUANTO DURA A VOLTA (<see cref="SegundosDoCicloDoContorno"/>).
	/// Ele nao tem relogio e nao pode ter: quem conta quadro e o cliente (`CharacterVisual._Process`).
	/// Aqui a oscilacao e DADO, e nao comportamento -- do mesmo jeito que a `Aura` e um hexa e nao um
	/// desenho.
	///
	/// ============================ POR QUE AS DUAS TEM O MESMO BRILHO ============================
	/// Medido em luminancia (0,2126R + 0,7152G + 0,0722B): o azul da 131,9 e o roxo da 126,8, ou seja
	/// 4% de diferenca. E de proposito, e e a parte que mais importa do par: se uma ponta fosse mais
	/// clara que a outra, a oscilacao leria como PISCAR (o contorno sumindo e voltando) em vez de
	/// virar de cor. O que tem que se mover e a matiz, e so ela.
	///
	/// OS DOIS SAO MEDIOS E SATURADOS pelo mesmo motivo do <see cref="BrancoDoInstinto"/>: o cabelo do
	/// Beast e branco-gelo (`b6bac4`) e os dois degraus de Mistico abaixo dele sao pastel (a linha vai
	/// de `d8c8ff` a `b8a0ff`). Um azul pastel desapareceria dentro do proprio branco.
	///
	/// A AURA DELE DEIXOU DE SER QUASE-BRANCA -- era `e8e8e8` por engano meu e hoje e o `7d5af0` do
	/// `Mystic.dm:95`, que e roxo-azul. O par continua valendo: `7d5af0` e a chama, e estas duas sao o
	/// TRACO na silhueta, que precisa se ler CONTRA ela e nao dentro dela.
	///
	/// O ROXO NASCE PERTO DO <see cref="RoxoDoEgo"/> (`a95cff`) por coincidencia da roda de cores, e
	/// esta escrito separado exatamente por isso: sao formas de linhas diferentes que nao se devem
	/// nada, e reusar a constante faria o dia em que alguem ajustar o Ultra Ego mexer na Fera.
	/// ==============================================================================
	/// </summary>
	private const string AzulDaFera = "3f8cff";

	/// <inheritdoc cref="AzulDaFera"/>
	private const string RoxoDaFera = "b163ff";

	/// <summary>
	/// QUANTO DURA A VOLTA INTEIRA do contorno que oscila -- azul -> roxo -> azul.
	///
	/// QUATRO SEGUNDOS, e o numero e escolha minha (precisa do olho do dono). O pedido foi "lentamente"
	/// e "gradual", e ha um piso e um teto praticos:
	///   - abaixo de ~2 s a troca entra na faixa em que o olho le PISCA, e nao "muda de cor" -- e ai
	///     ela competiria com a faisca (que estala a 1,25/s) em vez de ficar por baixo dela;
	///   - acima de ~6 s a forma inteira acaba antes de o ciclo fechar numa luta normal, e o jogador
	///     nunca ve as duas cores -- a oscilacao viraria "o Beast as vezes e azul, as vezes e roxo",
	///     que e outra coisa.
	/// Em 4 s cabem duas voltas completas numa troca de golpes, e cada meia volta leva 2 s.
	///
	/// PUBLICO porque o cliente precisa dele pra girar o relogio e a bancada pra medir meia volta --
	/// e os dois tem que ler o MESMO numero, senao a bancada mede um ciclo que o jogo nao tem.
	/// </summary>
	public const double SegundosDoCicloDoContorno = 4.0;

	/// <summary>
	/// QUANTO O CONTORNO PINTA no corpo do dono da tela, no TOPO do pulso.
	///
	/// A MESMA FORCA PRA TODAS AS FORMAS, por decisao do dono. O contorno diz "passei dos 100% de
	/// Ki", e esse fato e o mesmo num SSJ1 e num Limit Breaker -- escalar pela intensidade da forma
	/// so fazia o SSJ1 sumir atras da propria aura, que e grande e clara.
	///
	/// ERA 1,0, e o dono avisou: *"o contorno deveria ficar um pouco mais fraco"*. 0,7 e a resposta,
	/// e e a de MENOS efeito colateral -- mexer aqui muda so a intensidade da linha, enquanto a
	/// outra saida obvia (escurecer a cor) esbarraria na regra "uma cor por linha" que a
	/// <see cref="CorDoContorno"/> existe pra garantir.
	///
	/// E ELE E O TOPO E NAO O VALOR DESENHADO: o contorno respira daqui ate
	/// <see cref="PisoDoPulsoDoContorno"/> -- 0,455 no fundo.
	///
	/// SO O CORPO LOCAL usa este numero. O corpo alheio segue a regra velha (contorno pela FORMA,
	/// `0,35 + Intensidade * 0,13`), porque o cliente nao sabe o Ki dos outros -- divida ja anotada
	/// no `World.AplicarContorno`.
	/// </summary>
	public const float ForcaDoContorno = 0.7f;

	/// <summary>
	/// ============================ O CONTORNO RESPIRA ============================
	/// Pedido do dono: *"o contorno deveria ficar um pouco mais fraco e ele ficar pulsando
	/// lentamente"*. Isto e a VOLTA INTEIRA do pulso -- forte -> fraco -> forte.
	///
	/// ============================ POR QUE NAO OS MESMOS 4 s DA COR ============================
	/// Reusar o <see cref="SegundosDoCicloDoContorno"/> era o caminho obvio e e o errado. As duas
	/// animacoes convivem no MESMO pixel do Beast (ele e o unico que oscila de cor), e com periodos
	/// iguais elas travariam em fase pra sempre: a Fera ficaria mais fraca exatamente quando vira
	/// roxa e mais forte exatamente quando volta ao azul. Ai nao ha duas leituras, ha uma -- e a
	/// troca de cor, que e a que o dono pediu por ultimo, seria a que sumiria dentro da outra.
	///
	/// 2,6 CONTRA 4,0 e 13/20: os dois relogios so reencontram a mesma fase a cada 52 s, ou seja
	/// nunca dentro de uma luta. A cor vira enquanto o brilho esta em qualquer ponto do pulso, e o
	/// olho le as duas coisas separadas.
	///
	/// O PISO DE ~2 s DA COR VALE AQUI TAMBEM (abaixo disso o olho le PISCA e nao "respira"), e o
	/// teto nao: brilho nao precisa de uma volta inteira pra ser lido, meia ja diz tudo. Por isso
	/// da pra ser mais rapido que a cor sem virar pisca-pisca.
	/// ========================================================================================
	/// </summary>
	public const double SegundosDoPulsoDoContorno = 2.6;

	/// <summary>
	/// O FUNDO DO PULSO, como FRACAO da forca pedida. 1,0 e o topo (a forca cheia da forma) e este e
	/// o vale -- entao o contorno anda entre 65% e 100% do que quem acende pediu.
	///
	/// E UMA FRACAO E NAO UM VALOR pra ele nao poder acender nada sozinho: multiplicando a forca
	/// pedida, forca 0 continua 0 em qualquer fase do relogio. A regra do Ki ("so acima de 100%") e
	/// obedecida pela ARITMETICA, e nao por uma guarda que alguem pode esquecer de escrever.
	///
	/// 35% DE FUNDO e escolha minha e precisa do olho do dono. Menos que isso e o contorno some no
	/// vale e volta -- vira o pisca-pisca que o piso de tempo existe pra evitar, so que no eixo da
	/// intensidade. Mais que isso e o pulso nao se ve.
	/// </summary>
	public const double PisoDoPulsoDoContorno = 0.65;

	/// <summary>
	/// ONDE O KI DIVINO ACENDE SOBRE O SUPER SAIYAJIN. Abaixo deste degrau (Ordem 10) esta o SSG,
	/// que e o ki divino sobre a forma BASE -- cabelo e aura VERMELHOS, e o `rose_ssg` e o mesmo.
	/// Ele nao e "o Blue mais fraco": e outra coisa, e pintar o contorno dele de azul (ou de rosa)
	/// seria dar a ele uma cor que a forma nao tem em lugar nenhum.
	///
	/// DERIVADO como o resto: um degrau divino novo acima do Blue ja nasce azul sem tocar aqui.
	/// </summary>
	private const int OrdemDoKiSobreOSuperSaiyajin = 20;

	/// <summary>
	/// ONDE O EGO ACENDE SOBRE A DESTRUICAO. Abaixo deste degrau (Ordem 10) esta a `destroyer`, que e
	/// a mesma disciplina com OUTRO desenho: cabelo base, sem cinematica e -- desde que a nuvem
	/// chegou -- sem nebulosa. O dono nomeou o Ultra Ego e so ele.
	///
	/// ESCRITO SEPARADO DO <see cref="OrdemDoKiSobreOSuperSaiyajin"/> apesar de os dois valerem 20: sao
	/// linhas diferentes que nao se devem nada, e reusar a constante faria o dia em que alguem mexer no
	/// corte das divinas mexer no Ultra Ego calado. E o mesmo criterio do par
	/// <see cref="RoxoDoEgo"/> / <see cref="RoxoDaFera"/>.
	/// </summary>
	private const int OrdemDoEgoSobreADestruicao = 20;

	/// <summary>
	/// ============================ O CONTORNO NAO E A AURA ============================
	/// A <see cref="FormaDef.Aura"/> estava servindo a TRES desenhos ao mesmo tempo: a chama em
	/// volta do corpo, o contorno brilhoso no proprio sprite e os raiozinhos. Enquanto foi um campo
	/// so, todo ajuste de cor pedido num deles saia errado nos outros dois -- e o defeito nunca
	/// aparecia onde a mudanca tinha sido feita. Sao tres coisas; agora sao tres fontes.
	///
	/// GANHO: mexer no contorno nao mexe mais na aura. PERDA: quem acrescentar uma forma agora tem
	/// tres perguntas a responder em vez de uma -- so que duas delas ja vem respondidas por
	/// derivacao, que e o ponto.
	///
	/// A REGRA, ditada pelo dono: UMA COR POR LINHA. Amarelo na escada Saiyajin, VERMELHO no
	/// `ssj4_limit_breaker`, VERDE no Legendary, AZUL no Blue, ROSA no Rose, BRANCO no Ultra
	/// Instinct e ROXO no Ultra Ego.
	///
	/// E UMA EXCECAO A REGRA: o Beast nao tem UMA cor, tem DUAS, e fica indo de uma a outra
	/// (<see cref="AzulDaFera"/>). Por isso a fonte de verdade destas funcoes e um PAR
	/// (<see cref="ParDoContorno"/>) e nao um hexa -- "cor parada" passou a ser o par com a segunda
	/// ponta nula, e nao mais o unico formato possivel.
	///
	/// ============================ POR QUE A REGRA ESTA ESCRITA MESMO ONDE A AURA JA ACERTA ==========
	/// Pra varias dessas linhas o `d.Aura` devolveria a cor certa por COINCIDENCIA -- a aura do Rose
	/// ja e rosa, a do Legendary ja e verde, a do Limit Breaker ja e vermelha. Escrever a regra assim
	/// mesmo e o ponto do arquivo inteiro: a coincidencia quebra EM SILENCIO no dia em que alguem
	/// ajustar a aura pela chama, e o defeito aparece longe da mudanca. Era exatamente esse o
	/// acoplamento que fez a `Aura` responder por coisas demais e que criou estas funcoes.
	/// ==============================================================================================
	///
	/// A ESCADA SAIYAJIN e amarela INTEIRA -- `ssj1`, `grade2`, `grade3`, `ssj2`, `ssj3`, `ssj4`,
	/// `ssj4_full_power`.
	///
	/// A EXCECAO E O `ssj4_limit_breaker` -- e ela e DERIVADA, nao um `if` por id: ele e o UNICO
	/// degrau da linha Saiyajin que exige ki divino (<see cref="FormaDef.PedeGodKi"/>, e `>= 0` e o
	/// mesmo teste que o <see cref="EstadoDeForma"/> usa pra recusar a forma). E e exatamente isso
	/// que ele significa -- "Rompe o teto do corpo Saiyajin". Um degrau divino novo na linha ja nasce
	/// com o vermelho em vez do dourado da escada.
	///
	/// TETO CONHECIDO, igual ao de <see cref="NasceDaRaiva"/>: um degrau divino que DEVESSE continuar
	/// dourado teria que virar excecao explicita aqui. Quem inserir que decida de novo.
	///
	/// A LINHA FUTURO entra junto, pelo mesmo precedente de <see cref="Folha"/> e
	/// <see cref="CorDoRabo"/>: ela e a escada Saiyajin da linhagem do futuro. Hoje isso nao muda
	/// pixel nenhum (a aura dela ja e o proprio `ffd24a`), mas separa-la faria o `future_ssj` ser o
	/// unico Super Saiyajin do jogo com contorno de outro tom no dia em que alguem mexer no amarelo.
	///
	/// AS DUAS ESCADAS DIVINAS (Blue e Rose) SAO CORTADAS PELA ORDEM, e o corte tem nome:
	/// <see cref="OrdemDoKiSobreOSuperSaiyajin"/>. O `ssg` e o `rose_ssg` ficam de fora e seguem a
	/// aura, porque o SSG e VERMELHO -- cabelo e aura -- e nao um Blue mais fraco.
	///
	/// A LINHA PRODIGIAL FECHOU O PONTO DE EXCECAO que este cabecalho deixou aberto: o Beast ganhou
	/// contorno proprio, e o dele nao e uma cor -- e um PAR que oscila (ver <see cref="AzulDaFera"/>).
	/// Sobrou o Oozaru seguindo a aura, e ele segue por decisao do dono: e o comportamento de hoje,
	/// e o menos surpreendente.
	///
	/// Nunca devolve nulo, e a diferenca pro <see cref="CorDoRabo"/> e proposital: "rabo sem tinta"
	/// e um caso real e comum; "contorno sem cor" nao e. Quem nao tem contorno passa forca zero.
	/// ================================================================================
	/// </summary>
	public static string CorDoContorno(FormaDef? d) => ParDoContorno(d).Cor;

	/// <summary>
	/// A SEGUNDA COR DO CONTORNO, ou NULO quando ele nao oscila -- que e o caso de 35 das 36 entradas.
	///
	/// Nulo quer dizer "contorno parado", e e o que o chamador testa: quem recebe nulo escreve a cor
	/// uma vez e nao gasta mais quadro nenhum com ela. Quem recebe uma cor tem que girar o relogio.
	///
	/// ============================ POR QUE ELA NAO E UM `switch` PROPRIO ============================
	/// Ela e a irma da <see cref="CorDoContorno"/> e as duas leem o MESMO
	/// <see cref="ParDoContorno"/>. Escrever dois `switch` com a mesma guarda seria duas verdades
	/// sobre quem oscila, e elas divergiriam no primeiro degrau novo: uma forma poderia ganhar a cor
	/// A numa funcao e a cor B na outra sem que nada reclamasse, e o defeito apareceria como um
	/// contorno que "as vezes fica travado no azul".
	/// ==========================================================================================
	/// </summary>
	public static string? CorDoContornoAlterna(FormaDef? d) => ParDoContorno(d).Alterna;

	/// <summary>
	/// O PAR: a cor do contorno e, quando ele oscila, a outra ponta. Fonte unica das duas irmas
	/// publicas -- ver <see cref="CorDoContornoAlterna"/> pra o porque de nao serem dois `switch`.
	///
	/// O `null` do segundo membro e o padrao e nao a excecao: `(cor, null)` le como "esta e parada".
	/// </summary>
	private static (string Cor, string? Alterna) ParDoContorno(FormaDef? d) =>
		d == null ? (BrancoNeutro, null) : d.Linha switch
	{
		// A `base` cai fora por este `when` e vai pra a propria aura (`ffffff`): ela esta na linha
		// Saiyajin do catalogo, mas nao e transformacao nenhuma.
		LinhaDeForma.Saiyajin or LinhaDeForma.Futuro when d.Id != IdBase =>
			(d.PedeGodKi < 0 ? AmareloSaiyajin : VermelhoDoLimitBreaker, null),

		LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal => (VerdeLegendary, null),

		LinhaDeForma.GodKi     when d.Ordem >= OrdemDoKiSobreOSuperSaiyajin => (AzulDivino, null),
		LinhaDeForma.GodKiRose when d.Ordem >= OrdemDoKiSobreOSuperSaiyajin => (RosaDivino, null),

		LinhaDeForma.UltraInstinct => (BrancoDoInstinto, null),
		LinhaDeForma.UltraEgo      => (RoxoDoEgo, null),

		// SO O BEAST DA LINHA DO MISTICO, e a derivacao e a MESMA que o `NasceDaRaiva` ja usa pra
		// isolar ele: dos dois degraus da linha, so o Beast pede ki divino MADURO
		// (`GodkiRoyalePct`). Nao ha `if` por id em lugar nenhum desta funcao e este nao seria o
		// primeiro. O Mistico fica abaixo do corte -- ele nao pede ki divino nenhum (`PedeGodKi`
		// = -1) -- e continua seguindo a aura.
		//
		// TETO CONHECIDO, igual ao de `NasceDaRaiva`: se um dia entrar um terceiro degrau nesta
		// linha acima do Beast, ele ja nasce oscilando. Quem inserir que decida de novo.
		LinhaDeForma.Mistico when d.PedeGodKi >= GodkiRoyalePct => (AzulDaFera, RoxoDaFera),

		_ => (d.Aura, null),
	};

	/// <summary>
	/// A COR DOS RAIOZINHOS. Irma da <see cref="CorDoContorno"/> e pelo mesmo motivo -- ver o
	/// cabecalho dela pra o porque de a `Aura` ter deixado de mandar em tudo.
	///
	/// A REGRA, do dono, fechada em duas frases: faisca AZUL nas escadas de SANGUE (`ssj2`, `ssj3`,
	/// `primal_legendary2` e `primal_legendary3`) e VERMELHA no `ssj4_limit_breaker` (*"limit breaker
	/// tem raios vermelhos"*). Eles saiam da aura -- dourados na escada Saiyajin, verdes na Primal --,
	/// e isso vinha de uma simplificacao minha anotada em <see cref="FormaDef.Raios"/> ("a cor sai da
	/// Aura"). Ela estava errada justamente onde o original TEM arte propria: a `electrictyeffects`
	/// do SSJ2/SSJ3 e uma folha branca-azulada, nao a aura tingida -- e a do Limit Breaker sao duas
	/// folhas so dele (`EffectLayer.dm:24-29`).
	///
	/// ============================ A FAIXA DE `Ordem` MORREU AQUI ============================
	/// Ate a correcao do dono este `switch` era `Ordem is >= 20 and < 40` -- a "faixa do relampago"
	/// dentro da escada Saiyajin. Ela nao sobrevive ao enunciado novo por DOIS lados ao mesmo tempo:
	/// o `ssj4_limit_breaker` tem faisca e esta em 42 (fora da faixa por cima), e o
	/// `primal_legendary2`/`primal_legendary3` tem faisca e nem sao dessa linha. Uma faixa que erra
	/// nas duas pontas nao e uma faixa; e um numero magico sobrevivendo.
	///
	/// O QUE SUBSTITUI E MAIS SIMPLES, e por isso mais dificil de envelhecer: nas escadas de SANGUE
	/// (Saiyajin, Futuro, Legendary e Legendary Primal) a faisca e AZUL, ponto. Fora delas ela segue
	/// a <see cref="FormaDef.Aura"/>, como toda cor sempre seguiu.
	///
	/// E CONTINUA NAO OLHANDO O VOLUME: `d.Raios > 0` como guarda seria o erro obvio e o mais
	/// tentador (afinal so cinco formas desenham raio). A cor passaria a depender do VOLUME, e os
	/// dois campos existem separados justamente porque respondem perguntas diferentes -- "de que cor
	/// e o raio desta forma" tem resposta ate pra quem nao acende nenhum, e e essa resposta que faz
	/// um degrau que GANHE faisca amanha ja nascer da cor certa.
	/// ==================================================================================
	///
	/// ============================ O VERMELHO E A MESMA DERIVACAO DO CONTORNO ============================
	/// Ele NAO e um `if` por id: e o `PedeGodKi >= 0` que a irma <see cref="ParDoContorno"/> ja usa
	/// pra isolar o mesmo degrau -- o UNICO da escada Saiyajin que exige ki divino. Escrever
	/// `d.Id == "ssj4_limit_breaker"` aqui daria a resposta certa e criaria uma SEGUNDA verdade sobre
	/// quem e o degrau divino da escada; do jeito que esta, o Limit Breaker so pode ficar vermelho no
	/// contorno e azul no raio se alguem mexer nas duas guardas.
	///
	/// E e por isso que ele fica coerente consigo mesmo: aura (`ff2d2f` no catalogo), contorno e raio
	/// saem os tres vermelhos, pelo mesmo motivo e nao por tres coincidencias.
	///
	/// O IRMAO DA OUTRA LINHA NAO ENTRA. O `primal_legendary4_limit_breaker` tambem pede
	/// `GodkiRoyalePct`, mas a guarda do vermelho e (Saiyajin ou Futuro) -- exatamente como no
	/// contorno, onde ele sai VERDE com o resto da linha dele. O dono nomeou um Limit Breaker.
	/// ============================================================================================
	/// </summary>
	public static string CorDosRaios(FormaDef? d) => d == null ? BrancoNeutro : d.Linha switch
	{
		LinhaDeForma.Saiyajin or LinhaDeForma.Futuro when d.PedeGodKi >= 0 => VermelhoDoLimitBreaker,

		// A `base` cai fora por este `when` e vai pra a propria aura (`ffffff`), igual ao contorno:
		// ela esta na linha Saiyajin do catalogo, mas nao e transformacao nenhuma.
		LinhaDeForma.Saiyajin or LinhaDeForma.Futuro or LinhaDeForma.Legendary
			or LinhaDeForma.LegendaryPrimal when d.Id != IdBase => AzulDaFaisca,

		// ============================ A LINHA PRODIGIAL TEM FAISCA PROPRIA, E ELA E DUAS ============================
		// Ela entrou junto com o `Raios` dos tres degraus (ver `FormaDef.Raios`), e a cor NAO podia
		// cair no `_ => d.Aura` de baixo: a aura do Mistico e `d8c8ff`, um lilas PALIDO, e faisca lilas
		// dentro de chama lilas e literalmente o defeito que o dono ja mandou consertar uma vez -- o
		// `primal_legendary2` saia verde-sobre-verde e sumia (ver `AzulDaFaisca`).
		//
		// ============================ ERA UMA RESPOSTA PRA OS DOIS, E O DONO SEPAROU ============================
		// *"no beast os raiozinhos sao roxos"*. Ele nomeou a Fera e SO ela, entao o Mistico fica no
		// branco -- e o corte e o mesmo `GodkiRoyalePct` do contorno, do olho e da `RaivaExigida`, nao
		// um `if` por id.
		//
		// **NAO CONFUNDA ISTO COM O ARGUMENTO ANTIGO.** O que estava escrito aqui era que a faisca da
		// linha e branca porque `Electric_Mystic.dmi` -- a folha que o `MysticEffect` (`Mystic.dm:20-23`)
		// veste -- foi medida e **nao tem matiz nenhuma** (1112 pixels em cinco tons neutros: `c4c4c4`,
		// `bdbdbd`, `ffffff`, `cbcbcb`, `d2d2d2`). A medida continua verdadeira e continua valendo pro
		// Mistico; o que ela NAO e, e um veto ao roxo. A decisao mudou de dono: aqui o DM perde.
		//
		// E O SEGUNDO ARGUMENTO DO BLOCO ANTIGO ESTAVA IMPRECISO, o que importa porque ele parecia
		// proibir cor: ele dizia que o branco existe *"pra o raio nao virar risco cinza"*, citando o
		// shader. O que o `RaioDaForma.gdshader:209` faz e `mix(cor.rgb, vec3(1.0), nucleo * 0.55)` --
		// **o nucleo ja e branco-quente por construcao, seja qual for a cor**, e a `:211` so devolve
		// cor as BORDAS. O aviso original era contra passar o CINZA `c4c4c4` do arquivo, nao contra uma
		// cor saturada. (A citacao tambem envelheceu: a frase mora na `:211`, nao na 139.)
		LinhaDeForma.Mistico when d.PedeGodKi >= GodkiRoyalePct => RoxoDaFaiscaDaFera,

		// FICA NO REALCE (`ffffff`) E NAO NO TOM DOMINANTE (`c4c4c4`) pelo que esta escrito acima: o
		// cinza do arquivo e que daria o risco cinza.
		LinhaDeForma.Mistico => BrancoDaFaiscaMistica,

		_ => d.Aura,
	};

	/// <summary>
	/// ============================ ESTA FORMA E ENVOLVIDA POR UMA NEBULOSA? ============================
	/// A nuvem de galaxia -- indigo profundo nas pontas, volutas violeta-azuladas no meio,
	/// ciano-claro quase branco encostado no corpo -- que o dono descreveu a partir da imagem de
	/// referencia do Ultra Instinto. Quem a desenha e o `NebulosaDaForma.gdshader`, do lado do
	/// cliente; quem responde SE ela existe e esta funcao, que e a irma da
	/// <see cref="CorDoContorno"/> e da <see cref="CorDosRaios"/> e mora aqui pelo mesmo motivo
	/// delas: efeito de forma se decide no catalogo, nao num `if` espalhado pelo cliente.
	///
	/// ============================ ELA CONTINUA DEVOLVENDO `bool`, E AGORA HA UMA IRMA QUE DA A COR ============================
	/// Aqui estava escrito o TETO: *"no dia em que uma SEGUNDA linha ganhar nebulosa de outra paleta,
	/// esta assinatura tem que virar a das irmas (devolver as cores, ou nulo)"*. O dia chegou -- o
	/// Ultra Ego --, e a resposta foi a metade certa daquela previsao: quem passou a devolver as cores
	/// foi uma funcao NOVA (<see cref="PaletaDaNebulosa"/>), e esta continua respondendo SIM/NAO.
	///
	/// Partir em duas, e nao trocar a assinatura, e o que mantem a regra do dono: *"o `Folha(d) ==
	/// Nebulosa` continua sendo a unica verdade sobre quem tem nuvem"*. Uma funcao que devolvesse
	/// "paleta ou nulo" viraria uma SEGUNDA maneira de perguntar a mesma coisa, e alguem escreveria
	/// `PaletaDaNebulosa(d) != null` em um lugar e `TemNebulosa(d)` em outro -- as duas fontes de
	/// verdade de sempre. Hoje a paleta CONSULTA esta funcao pra saber se existe; a pergunta e uma so.
	///
	/// O PRECO ESTA PAGO E VALE DIZE-LO: a paleta era ajuste de arte que morava nos `uniform` do
	/// `.gdshader` justamente pra o dono afinar o indigo olhando a tela, sem recompilar. Com DUAS
	/// paletas isso nao para de pe -- o shader nao sabe que forma esta vestindo, e um valor padrao so
	/// pode servir a uma delas. Os hexa mudaram-se pro Core, junto das irmas, e afinar a nuvem passou a
	/// custar uma compilacao. O default do shader continua sendo a paleta do Ultra Instinto, mas hoje
	/// ele e DOCUMENTACAO (e a previa do editor), nao a fonte.
	/// ==============================================================================================
	///
	/// NAO RAMIFICA POR <see cref="FormaDef.Ordem"/> DENTRO DA LINHA DO ULTRA INSTINTO, e isso e o DM:
	/// o `Buff()` do `UltraInstinct.dm:479-480` veste os mesmos dois overlays sem olhar o estagio,
	/// entao `ui_sign` e `ui_perfected` mostram exatamente a mesma coisa -- o que o dono confirmou de
	/// viva voz. Na linha do Ultra Ego ele RAMIFICA, e por outro motivo: ver abaixo.
	///
	/// ============================ O ULTRA EGO ENTROU; A DESTROYER NAO ============================
	/// Aqui estava escrito que *"o Ultra EGO fica de fora de proposito"*, e a razao dada era que a aura
	/// dele e a `god - grey` tingida (`UltraEgo.dm:353`). Isso valia enquanto o porte fosse literal; o
	/// dono passou por cima: *"a aura/carga do ultra ego e a mesma do instinto superior so q ROXA ao
	/// inves de branca/prateada, mas tem os mesmos efeitos"*. E DIVERGENCIA DECLARADA do DM, como o
	/// rabo branco do Perfected foi.
	///
	/// A `destroyer` fica de fora, e as duas metades disso concordam: **o dono nomeou o Ultra Ego**, e
	/// o DM diz que o cabelo (e, aqui, a cena) e a diferenca visual entre as duas formas da disciplina
	/// (`UltraEgo.dm:395-396`). Dar a nuvem as duas apagaria a unica coisa que as separa aos olhos.
	/// E o mesmo criterio que ja a deixou sem tinta de cabelo, sem tinta de rabo e sem cinematica.
	///
	/// ============================ E ELE E DERIVADO DA <see cref="Folha"/>, NAO UM SEGUNDO PREDICADO ============================
	/// Isto era `d?.Linha == LinhaDeForma.UltraInstinct` escrito por extenso -- a MESMA regra que a
	/// `Folha` ja respondia, em duas fontes de verdade. Elas nao divergiram por sorte: a `Folha` nao
	/// tinha ramo pra esta linha e devolvia `Base`, entao o Ultra Instinto acendia a nuvem E a chama
	/// `colorablebigaura` por cima, que foi exatamente a queixa do dono.
	///
	/// Agora ha UMA pergunta: "que folha esta forma usa?". <see cref="FolhaDeAura.Nebulosa"/> quer dizer
	/// "nenhuma -- a minha e a nuvem", e as duas respostas saem da mesma linha do mesmo `switch`. Uma
	/// linha nova com nebulosa nao tem como acender chama junto, porque os dois fatos sao um so.
	/// ====================================================================================================================
	/// </summary>
	public static bool TemNebulosa(FormaDef? d) => Folha(d) == FolhaDeAura.Nebulosa;

	// ================================================================================================
	// AS DUAS PALETAS DA NUVEM. Os oito hexa abaixo eram os `= vec4(...)` de tres uniforms e um
	// `vec3(1.0)` cravado no meio do `fragment` do `NebulosaDaForma.gdshader`. Mudaram-se pra ca
	// quando a nuvem ganhou um segundo dono -- ver o bloco "O PRECO ESTA PAGO" da `TemNebulosa`.
	// ================================================================================================

	/// <summary>
	/// A PONTA ESCURA da nuvem do Ultra Instinto: o indigo quase preto das bordas, longe do corpo.
	/// Valor do `.gdshader` sem uma virgula de mudanca -- ele foi calibrado na FOTO, em tres fundos
	/// diferentes (o `atenua_escuro` existe por causa desta cor sobre grama).
	/// </summary>
	private const string IndigoDaNebulosa = "241a3d";

	/// <inheritdoc cref="IndigoDaNebulosa"/>
	private const string VioletaDaNebulosa = "6f5fae";

	/// <inheritdoc cref="IndigoDaNebulosa"/>
	private const string CianoDaNebulosa = "d8e8ff";

	/// <summary>
	/// A MICROPARTICULA que sobe. Branco puro, e ele NAO estava num uniform: era `mix(rgb, vec3(1.0),
	/// pontos)` escrito no `fragment`. Virou dado aqui porque o Ultra Ego pede a mesma particula em
	/// outro tom -- e uma cor cravada dentro de um shader e uma cor que nenhuma forma pode mudar.
	/// </summary>
	private const string BrancoDosPontos = "ffffff";

	/// <summary>
	/// ============================ A MESMA NUVEM, EM ROXO ============================
	/// A ponta ESCURA da nuvem do Ultra Ego: ameixa quase preta. As tres cores abaixo nao sao arte
	/// nova -- sao a rampa do Ultra Instinto GIRADA pro eixo do Ego, e "girada" e literal:
	///
	///     ponta escura   `241a3d` H 257,1  ->  `2e1837` H 282,6   (L 30,7 -> 30,9)
	///     meio           `6f5fae` H 252,2  ->  `8d56ac` H 278,4   (L 104,1 -> 103,9)
	///     ponta clara    `d8e8ff` H 215,4  ->  `e9c0ff` H 279,0   (L 230,3 -> 205,3)
	///     particula      `ffffff`          ->  `f6e4ff` H 280,0   (L 255,0 -> 233,8)
	///
	/// A MATIZ DE CHEGADA E A DO PROPRIO EGO: `8c32be` -- o `rgb(140,50,190)` que o `UltraEgo.dm:387-392`
	/// poe no cabelo e que ja veste olho e rabo da forma -- mede H 278,6, e as tres pontas caem entre
	/// 278,4 e 282,6. Nao ha um roxo novo no jogo; ha o roxo do Ego em tres claridades.
	///
	/// ============================ AS DUAS PONTAS FORAM MEDIDAS, E SO UMA PODIA FICAR IGUAL ============================
	/// Este projeto ja calibrou uma rampa **so pelo topo** e o dono reclamou duas vezes: foi assim que
	/// o Blue saiu marinho e o Rose saiu vinho. Entao as duas pontas aqui foram medidas em luminancia
	/// (0,2126R + 0,7152G + 0,0722B), que e o que decide se a nuvem le igual:
	///
	///   * a ponta ESCURA fica em 30,9 contra 30,7 -- 0,6% de diferenca, ou seja a MESMA leitura. Ela e
	///     a que cobre mais area e a que o `atenua_escuro` afina sobre fundo claro; mudar o brilho dela
	///     mudaria o quanto a nuvem suja o cenario, que e correcao de foto ja paga.
	///   * a ponta CLARA cai pra 89% (205,3 contra 230,3), e a queda e o PEDIDO: em roxo com V=1 a
	///     luminancia e refem do verde, e manter 230 exigiria S=0,10 -- que e um lilas lavado, ou seja
	///     "branca/prateada" de novo, exatamente o que o dono mandou tirar. Os 11% compram S 0,153 ->
	///     0,247, e o anel colado no corpo continua sendo, de longe, a coisa mais clara da nuvem
	///     (205 contra 104 do meio).
	///
	/// A PARTICULA PERDE O MESMO TANTO e por isso continua se lendo como faisca: ela sai 1,14x acima da
	/// ponta clara (233,8 / 205,3), e no Ultra Instinto sai 1,11x (255,0 / 230,3). O contraste que a
	/// separa da nuvem e o mesmo; o que mudou foi o par inteiro.
	/// ==============================================================================================
	/// </summary>
	private const string AmeixaDaNebulosaDoEgo = "2e1837";

	/// <inheritdoc cref="AmeixaDaNebulosaDoEgo"/>
	private const string VioletaDaNebulosaDoEgo = "8d56ac";

	/// <inheritdoc cref="AmeixaDaNebulosaDoEgo"/>
	private const string LilasDaNebulosaDoEgo = "e9c0ff";

	/// <inheritdoc cref="AmeixaDaNebulosaDoEgo"/>
	private const string LilasDosPontosDoEgo = "f6e4ff";

	/// <summary>
	/// ============================ DE QUE COR E A NUVEM DESTA FORMA ============================
	/// A irma da <see cref="TemNebulosa"/>: aquela diz SE ha nuvem, esta diz DE QUE COR. Nulo quer
	/// dizer "esta forma nao tem nuvem", e a resposta sai da PROPRIA <see cref="TemNebulosa"/> -- nao
	/// ha um segundo predicado aqui, e nao pode haver (ver o bloco de la).
	///
	/// ============================ E O `_` NAO DEVOLVE A PALETA DO ULTRA INSTINTO ============================
	/// O ramo do Ultra Ego e nomeado e o `_` cai na paleta do Ultra Instinto -- o que parece um default
	/// preguicoso e nao e: quem chega no `_` ja passou pela `TemNebulosa`, entao ou e Ultra Instinto ou
	/// e uma LINHA NOVA que alguem acabou de por na <see cref="FolhaDeAura.Nebulosa"/>. Nesse dia a
	/// nuvem nova nasce indigo em vez de nascer sem cor nenhuma (o quad preto), e o defeito aparece na
	/// tela como "a cor errada" em vez de "sumiu" -- que e o que se conserta rapido.
	/// ============================================================================================
	/// </summary>
	public static PaletaDeNebulosa? PaletaDaNebulosa(FormaDef? d) =>
		!TemNebulosa(d) ? null
		: d!.Linha == LinhaDeForma.UltraEgo
			? new PaletaDeNebulosa(AmeixaDaNebulosaDoEgo, VioletaDaNebulosaDoEgo,
								   LilasDaNebulosaDoEgo, LilasDosPontosDoEgo)
			: new PaletaDeNebulosa(IndigoDaNebulosa, VioletaDaNebulosa,
								   CianoDaNebulosa, BrancoDosPontos);

	/// <summary>
	/// ============================ QUANTO ESTA FORMA LEVANTA O TETO DE KI ============================
	/// O `trueKiMod` do BYOND. Derivado de (Linha, Ordem), como <see cref="Folha"/> e
	/// <see cref="NasceDaRaiva"/> -- um degrau novo herda o teto do vizinho de ordem sem ninguem
	/// preencher campo nenhum.
	///
	/// Os VALORES vem do <see cref="Jandirus.Core.Stats.Fighter"/> e nao daqui, de proposito: no
	/// original eles sao variaveis que skills reescrevem (a Heran sobe o do SSJ1 pra 3; a arvore
	/// Legendary multiplica o dela por 1,5). Ver os campos la.
	///
	/// As linhas DIVINAS ficam em 1,0 -- conferido: `godki.dm`, `Mystic.dm`, `UltraInstinct.dm` e
	/// `UltraEgo.dm` nao tem uma unica escrita de teto de Ki. O Oozaru esta desligado de proposito
	/// (`Oozaru.dm:126`) e o Kaioken so LE o `KiMod`.
	///
	/// TETO CONHECIDO, igual ao de `NasceDaRaiva`: um degrau inserido entre duas `Ordem` existentes
	/// herda o teto do vizinho sem avisar. Quem inserir que decida de novo.
	/// ================================================================================
	/// </summary>
	public static double TetoDeKi(FormaDef? d, Jandirus.Core.Stats.Fighter f) =>
		d == null || d.Id == IdBase ? 1 : d.Linha switch
		{
			LinhaDeForma.Saiyajin or LinhaDeForma.Futuro or LinhaDeForma.LegendaryPrimal =>
				d.Ordem < 20 ? f.ssjenergymod      // ssj1, grades, future, primal_c_type
			  : d.Ordem < 30 ? f.ssj2energymod
			  : d.Ordem < 40 ? f.ssj3energymod     // ssj3, primal_legendary2/3
			  :                f.ssj4energymod,    // ssj4 e acima

			LinhaDeForma.Legendary =>
				d.Ordem < 20 ? f.rssjenergymod     // wrathful
			  : d.Ordem < 30 ? f.ussjenergymod     // c_type
			  :                f.lssjenergymod,    // legendary e full power

			// ============================ AS TRES LINHAS NOVAS, E SO DUAS LEEM O `Fighter` ============================
			// O HERAN E O ALIEN dividem o tanque do Saiyajin de proposito, e o DM diz isso na cara: o
			// `MaxPower/Loop` usa `container.ssjenergymod` / `ssj2energymod` (`HeranBuff.dm:57`, `:62`) com
			// o comentario *"herans share the saiyans energy boost"*, e o `Alien_Trans/Loop` usa
			// `ssjenergymod` nos DOIS degraus (`Alien_Transformations.dm:58`, `:63`) -- nao e engano, a
			// segunda forma Alien nao aumenta o tanque alem da primeira.
			//
			// Ler o campo do `Fighter` (e nao um literal) e o que faz a skill `Energy_Blues` do Heran
			// valer: ela poe `ssjenergymod = 3` e `ssj2energymod = 4` (`heran.dm:135-136`), e com um 2
			// cravado aqui ela nao faria nada -- exatamente a classe de defeito que este arquivo ja
			// documenta ("skill que promete e nao faz").
			//
			// O NAMEKUSEIJIN E O UNICO COM NUMERO PROPRIO, e por isso ele tem constante: `trueKiMod = 2`
			// esta escrito no buff dele (`Super_Namek.dm:41`) e nao sai de var nenhuma. Ver `SuperNamekKi`.
			// ====================================================================================================
			LinhaDeForma.Namekuseijin => SuperNamekKi,
			LinhaDeForma.Alien => f.ssjenergymod,
			LinhaDeForma.Heran => d.Ordem < 20 ? f.ssjenergymod : f.ssj2energymod,

			// A SUPER PERFEITA TAMBEM DIVIDE O TANQUE DO SAIYAJIN -- `trueKiMod = ssjenergymod`
			// (`CellFormBuff.dm:7`), pelo mesmo motivo literal do Heran e do Alien: o DM reusa o
			// campo. E ele PRECISA ser lido do `Fighter` e nao cravado, porque uma forma que drena
			// 1% do Ki por segundo com o tanque errado dura o tempo errado.
			LinhaDeForma.BioAndroide => f.ssjenergymod,

			_ => 1,                                 // GodKi, Rose, Prodigial, UI, UE, Oozaru, Frost
		};

	/// <summary>
	/// ============================ ESTA FORMA NASCE DA RAIVA? ============================
	/// A regra do DM numa frase: a raiva abre o TRONCO das escadas de SANGUE. O que sai do TREINO
	/// ja se declara no dado -- `ForaDoTronco` (os grades), `PedeMaestria` (os Full Power e Limit
	/// Breaker) e `PedeFormaDespertada` (o SSJ4, que vem do Oozaru). O que sai de RECURSO DIVINO e
	/// linha inteira (God Ki, Rose, UI, UE), e o Oozaru e a LUA, nao a furia.
	///
	/// DERIVADA, e nao um campo `PorRaiva` -- pelo mesmo motivo da <see cref="Folha"/> logo acima,
	/// e o modo de falhar de um campo aqui seria o pior deste projeto: CALADO. Um SSJ5 acrescentado
	/// sem preencher o campo simplesmente plantaria a cratera pequena, e ninguem notaria por meses.
	/// Derivando, ele nasce no tronco da linha e ja sai com a cratera grande.
	///
	/// CONFERIDO contra o DM entrada por entrada. Devolve 10: `ssj1`, `ssj2`, `future_ssj`,
	/// `wrathful`, `c_type`, `legendary`, `primal_c_type`, `primal_legendary`, `primal_legendary2`
	/// e `beast`.
	///
	/// ============================ O SSJ3 SAIU DESTA LISTA, E SAIU SOZINHO ============================
	/// Regra do dono: o SSJ3 nao libera com raiva -- ele pede 50% de maestria do SSJ2 e o poder
	/// minimo pessoal (`Transformation Controls.dm:46`). Ele nao e recusado aqui por nome: a entrada
	/// dele ganhou `PedeMaestria` e caiu pelo `PedeMaestria <= 0` que ja estava escrito, junto com
	/// os Full Power e os Limit Breaker. **Isso e a derivacao funcionando, nao um acaso feliz**:
	/// "raiva abre o que nao se treina" e a frase, e cobrar tempo dentro da forma anterior E treinar.
	///
	/// Ele estava aqui porque o porte copiou o `mst_form_needs_rage` (`MasterStudent.dm:246-249`),
	/// que lista `ssj, ssj2, ssj3, lssj1, heran1, heran2` -- so que aquela lista e do caminho
	/// MESTRE-ALUNO (o mestre provocando o despertar) e nao do jogador subindo sozinho. Os outros
	/// batem. A escada `Heran` ainda NAO existe no port (ver `LimiaresPessoais.cs`); no dia em que
	/// vier, os degraus dela nascem no tronco e esta funcao os pega sozinha, que e o ponto de derivar.
	/// ================================================================================================
	///
	/// ============================ NENHUMA FORMA DIVINA NASCE DA RAIVA. O BEAST E A UNICA EXCECAO ============================
	/// Ordem do dono, com o motivo dele junto -- e o motivo e que faz a regra valer pros degraus que
	/// ainda nao existem: **forma divina e tecnica e de mente calma; raiva e mortal e bruta**. God
	/// Ki, Rose, Ultra Instinct e Ultra Ego se abrem por ENSINO (o ritual do Kaioshin no Mistico, a
	/// cadeia Anjo -> aluno do UI, o Deus da Destruicao no UE -- ver `GameServer.Disciplinas.cs`) ou
	/// por MADURAR o ki divino. Nenhuma delas olha pra furia, e o `_ => false` la embaixo e essa
	/// frase escrita em codigo: quatro linhas inteiras recusadas de uma vez, e uma linha divina nova
	/// nasce recusada tambem.
	///
	/// O Ultra Instinto e o caso que explica o porque melhor que qualquer comentario meu: ele e a
	/// forma que so vem quando a mente PARA de reagir. Destravar isso com raiva seria contradizer a
	/// propria forma.
	///
	/// O BEAST fica de fora da regra porque ele nao e um deus -- ele e o que sai quando o Prodigial
	/// se RECUSA a virar deus (`godki.dm:349-352`: o ki divino dele nao vira SSG/Blue, vira FERA), e
	/// no DM ele desperta pelo mesmo `Emotion == "Very Angry"` do SSJ1/SSJ2, pelo mesmo `effector`
	/// (`Mystic.dm:65-67`). Ele e o unico gate de raiva ESTRITO do jogo.
	///
	/// O braco `Mistico` existe so pra isolar ele. A linha tem DUAS entradas -- o `mistico`, que e
	/// dom de ritual e nao pede ki divino nenhum (`PedeGodKi = -1`), e o `beast`, que pede ki divino
	/// MADURO --, entao `>= GodkiRoyalePct` separa os dois sem citar id. TETO CONHECIDO: um degrau
	/// novo nesta linha acima de 50% de ki divino contaria como raiva tambem. Quem o acrescentar que
	/// decida de novo -- e se ele for divino de verdade, o lugar dele e no `Nenhuma` do default.
	/// ======================================================================================================================
	/// </summary>
	public static bool NasceDaRaiva(FormaDef? d) => RaivaExigida(d) != NivelDeRaiva.Nenhuma;

	/// <summary>
	/// ============================ E DE QUE RAIVA? -- A AUTORIDADE, E `NasceDaRaiva` E O RESUMO DELA ============================
	/// <see cref="NasceDaRaiva"/> responde "esta forma vem da furia" (e e o que o cliente pergunta
	/// pra escolher o tamanho da cratera); esta responde **de QUE furia**, que e o que o gate cobra.
	/// Uma sai da outra de proposito: duas derivacoes separadas seriam duas verdades sobre a mesma
	/// regra, e o dia em que divergissem o jogo abriria uma forma cuja cratera diz que ela nao existe.
	///
	/// ============================ A REGRA, DITADA PELO DONO, EM TRES FRASES ============================
	///   1. *"as formas de RAIVA pedem RAIVA EXTREMA, que so vem quando um amigo proximo morre na
	///      frente do personagem -- a mesma condicao do Beast"*;
	///   2. *"o Legendary Saiyajin tem a skill `legendary anger` e por isso precisa de MENOS: basta
	///      ver um amigo ser NOCAUTEADO ou apanhar muito"* -- vale pra linha inteira;
	///   3. *"nenhuma forma divina libera por raiva"*, e o Beast e a unica excecao.
	///
	/// As tres estao aqui, e nas TRES o que decide e o ARCO DA LINHA -- nao uma lista de ids. Um
	/// degrau Legendary novo nasce no desconto; um degrau Saiyajin novo nasce no luto; um degrau
	/// divino novo nasce sem raiva. Era o ponto inteiro de refazer as formas por dado.
	///
	/// ============================ O `tronco` E O MESMO DE SEMPRE ============================
	/// "A raiva abre o que NAO SE TREINA": os tres jeitos de sair do tronco continuam sendo os tres
	/// campos que ja diziam isso -- `ForaDoTronco` (os grades), `PedeMaestria` (os Full Power, os
	/// Limit Breaker e o SSJ3) e `PedeFormaDespertada` (o SSJ4, que vem da lua). E por isso que o
	/// SSJ3 saiu da raiva **sozinho** quando ganhou os 50% de SSJ2: ninguem o citou pelo nome.
	///
	/// ============================ ELA TRANCA O SSJ1, E ISSO E A REGRA E NAO UM DEFEITO ============================
	/// Sublinhado porque a versao anterior desta funcao existia justamente pra NAO trancar. Por um
	/// tempo o preco disso foi alto e assumido: o port nao tinha sistema de amizade, ninguem acendia
	/// raiva, e o tronco Saiyajin so saia por verb de admin.
	///
	/// **NAO E MAIS ASSIM.** O convivio foi portado (`Core.Social.Convivio`, o known-people de
	/// `Contacts.dm` + `Friendship.dm`) e o gancho `GameServer.AmigoAbatido` tem chamador: ver um
	/// amigo cair ou morrer na sua frente, pelas maos de um inimigo. A bancada `raiva` [8] continua
	/// vigiando os fontes -- agora pra garantir que o chamador NAO SUMA.
	///
	/// A alternativa, na epoca, era afrouxar a regra pra as formas ficarem testaveis -- e afrouxar a
	/// regra e mentir sobre o jogo pra agradar o teste.
	///
	/// TETO CONHECIDO: um degrau novo que nasca da raiva **e** cobre ki divino cai no braco do
	/// Mistico sozinho. Quem o acrescentar que decida de novo.
	/// ========================================================================================================================
	/// </summary>
	public static NivelDeRaiva RaivaExigida(FormaDef? d)
	{
		if (d == null || d.Id == IdBase) return NivelDeRaiva.Nenhuma;

		bool tronco = !d.ForaDoTronco && d.PedeMaestria <= 0 && d.PedeFormaDespertada.Length == 0;

		return d.Linha switch
		{
			// O DESCONTO DA `legendary anger` -- e ele e da LINHA, nao da skill. Ver NivelDeRaiva.
			LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal
				=> tronco ? NivelDeRaiva.Lendaria : NivelDeRaiva.Nenhuma,

			// ============================ AS ESCADAS DE **SANGUE** -- e o Heran estava faltando ============================
			// O enunciado do cabecalho e "a raiva abre o TRONCO das escadas de SANGUE", e ate aqui essa
			// frase estava escrita no comentario e nao no codigo: o `switch` listava Saiyajin e Futuro, e
			// tudo o mais caia no `_ => Nenhuma`.
			//
			// **A LINHA HERAN PROVOU QUE ISSO ERA UM DEFEITO LATENTE, E O PROPRIO ARQUIVO JA O PREVIA E
			// ERRAVA A PREVISAO.** O comentario logo acima (o do SSJ3 saindo da lista) dizia: *"A escada
			// `Heran` ainda NAO existe no port; no dia em que vier, os degraus dela nascem no tronco e
			// esta funcao os pega sozinha, que e o ponto de derivar."* Nao pegaria: uma linha nova cai no
			// `_`, e o Heran teria acendido sem raiva nenhuma -- CALADO, que e o pior modo de falhar
			// deste projeto. E ele PEDE raiva no original, duas vezes: `heran.dm:20-52` roda o mesmo
			// `switch(savant.Emotion)` do Super Saiyajin nos dois degraus, e o `mst_form_needs_rage`
			// (`MasterStudent.dm:246-249`) lista `heran1` e `heran2` junto com `ssj` e `ssj2`.
			//
			// O CONSERTO E NA DERIVACAO E NAO NA ENTRADA: nenhuma das duas entradas Heran declara raiva.
			// O que mudou foi o braco passar a nomear a FAMILIA que a frase sempre descreveu -- as
			// escadas que vem do corpo com que a pessoa nasceu. As tres linhas nao-Saiyajin novas
			// separam-se sozinhas por aqui, e as duas que NAO pedem raiva ficam de fora por serem o que
			// sao: o Super Namekuseijin e as formas Alien se COMPRAM (`PedeFlag`), e o que se compra nao
			// se desperta -- o `snamek()` e o `Alien_Trans()` nao olham `Emotion` uma unica vez.
			//
			// O FROST DEMON TAMBEM E DE SANGUE E TAMBEM FICA DE FORA, e por escolha do original: o
			// `Frost_Demon_Forms` gateia por `fd_form_at` e maestria, nunca por furia. Ele esta no `_`
			// junto com as divinas, e o comentario existe pra ninguem "consertar" isso depois.
			// ==========================================================================================================
			LinhaDeForma.Saiyajin or LinhaDeForma.Futuro or LinhaDeForma.Heran
				=> tronco ? NivelDeRaiva.Extrema : NivelDeRaiva.Nenhuma,

			// So o Beast. O `mistico` da mesma linha e `PedeGodKi = -1` e cai fora sem citar id.
			LinhaDeForma.Mistico
				=> d.PedeGodKi >= GodkiRoyalePct ? NivelDeRaiva.Extrema : NivelDeRaiva.Nenhuma,

			// AS DIVINAS, O OOZARU E O QUE VIER: forma divina e tecnica e de mente calma.
			_ => NivelDeRaiva.Nenhuma,
		};
	}

	/// <summary>
	/// ESTA FORMA E INALCANCAVEL PELA ESCADA? Derivado da <see cref="LinhaDeForma"/>, como
	/// <see cref="Folha"/> e <see cref="NasceDaRaiva"/> -- sem campo novo.
	///
	/// So a linha do Oozaru, e ela e a definicao do conceito: "nao se sobe pra ele, ele acontece
	/// POR OLHAR A LUA" (cabecalho de <see cref="Oozaru"/>). As duas entradas dela existem aqui
	/// pelos DADOS (multiplicador, aura, corpo, cabelo) -- quem as liga e o `Apeshit`, e o unico
	/// gate delas mora no <see cref="Oozaru"/>, nao no <see cref="EstadoDeForma.Avaliar"/>.
	///
	/// Um dia isto pode valer pra mais linhas (uma forma de item, um emprestimo divino). Por isso e
	/// uma PERGUNTA e nao um `== LinhaDeForma.Oozaru` solto no meio do `Avaliar`.
	/// </summary>
	public static bool NaoSeSobePraEla(FormaDef? d) => d != null && d.Linha == LinhaDeForma.Oozaru;
	// ==================================================================================
	// OS `initial()` DA CONTA -- e NAO o limiar de ninguem (ver LimiaresPessoais)
	// ==================================================================================

	/// <summary>`ssjat` -- 1,5 milhao de BP base. supersaiyanbuff.dm:6.</summary>
	public const double SsjatInicial = 1_500_000;

	/// <summary>`ssj2at` -- 3,5 bilhoes de BP EXPRESSO. supersaiyanbuff.dm:37.</summary>
	public const double Ssj2atInicial = 3.5e9;

	/// <summary>`ssj3at` -- 20 bilhoes de BP EXPRESSO. supersaiyanbuff.dm:48.</summary>
	public const double Ssj3atInicial = 2e10;

	/// <summary>`rawssj4at` -- ja e BASE no original (rework 2026-07-10). supersaiyanbuff.dm:55.</summary>
	public const double RawSsj4atInicial = 15e9;

	/// <summary>`ultrassjat` -- 750 milhoes, o USSJ. supersaiyanbuff.dm:18.</summary>
	public const double UltrassjatInicial = 750e6;

	/// <summary>
	/// O DIVISOR QUE TRAZ O LIMIAR DO SSJ2 PRO BP BASE: `BP >= ssj2at/6`
	/// (Transformation Controls.dm:19). Dividir aqui e o que impede a forma anterior de "pagar" o
	/// requisito da seguinte.
	/// </summary>
	public const double Ssj1GateMult = 6;

	/// <summary>O mesmo pro SSJ3: `BP >= ssj3at/10` (Transformation Controls.dm:45).</summary>
	public const double Ssj2GateMult = 10;

	public const double PortaSsj1 = SsjatInicial;
	public const double PortaSsj2 = Ssj2atInicial / Ssj1GateMult;
	public const double PortaSsj3 = Ssj3atInicial / Ssj2GateMult;
	public const double PortaSsj4 = RawSsj4atInicial;

	/// <summary>
	/// ============================ A PORTA DE PODER DO OOZARU DOURADO ============================
	/// O dono pediu que o Dourado cobrasse, alem do SSJ1 dominado, "um poder minimo (tambem
	/// requisito pessoal, dentro da margem definida)". Ele NAO ganhou limiar novo: reusa o
	/// `ultrassjat`, que o <see cref="LimiaresPessoais"/> ja sorteia no nascimento de todo Saiyajin
	/// (`statsaiyan.dm:53`, a mesma margem `rand(9,13)/10` dos outros) e que hoje nao gateia NADA --
	/// os grades passaram a abrir por maestria no SSJ1. E o proprio comentario do campo la ja
	/// previa o dia em que ele voltasse a valer.
	///
	/// POR QUE NAO O `rawssj4at`, que seria o vizinho obvio: o Dourado e o pre-requisito do SSJ4
	/// (`PedeFormaDespertada` + `PedeMaestriaDe` na entrada `ssj4`). Dando a ele a MESMA chave do
	/// SSJ4, todo mundo que chega no Dourado ja passou da porta do SSJ4 -- e o `PortaBp` do SSJ4
	/// viraria uma checagem que nao pode mais recusar ninguem. Dado morto, o defeito que este port
	/// mais repetiu. Com o `ultrassjat` (750 mi contra 15 bi) as duas portas continuam sendo duas.
	///
	/// E o numero cai onde o degrau vive: acima do SSJ2 (583 mi de base) e abaixo do SSJ3 (2 bi).
	/// Dominar o Dourado custa ~22 luas cheias (<see cref="Oozaru.SegundosParaDominar"/>), entao a
	/// moagem comeca no meio da escada e paga no SSJ4 -- que e o unico jeito de ela caber numa vida.
	/// ==========================================================================================
	/// </summary>
	public const double PortaOozaruDourado = UltrassjatInicial;

	/// <summary>Maestria do SSJ1 que abre cada grade. `SSJ_GRADE2_PCT` / `SSJ_GRADE3_PCT`.</summary>
	public const double Grade2Pct = 50, Grade3Pct = 70;

	/// <summary>
	/// A MAESTRIA DE SSJ2 QUE ABRE O SSJ3. `Transformation Controls.dm:46` --
	/// `if(usr.ssj3able &amp;&amp; usr.ssj2mastery >= 50)`.
	///
	/// E o par do <see cref="PortaSsj3"/>, nao um substituto: o DM cobra as DUAS coisas na mesma
	/// linha (`usr.BP >= usr.ssj3at/10` **e** a maestria). O SSJ3 e a unica forma do tronco que
	/// pede tempo DENTRO da forma anterior, e e o que o dono pediu pra tirar ele da raiva.
	/// </summary>
	public const double Ssj3PedeSsj2Pct = 50;

	/// <summary>Fatores dos grades sobre a base do SSJ1. `SSJ_GRADE2_FACTOR` / `_GRADE3_FACTOR`.</summary>
	public const double Grade2Fator = 1.5, Grade3Fator = 2;

	/// <summary>
	/// ============================ OS STATS DOS GRADES -- E O DM NAO OS TEM ============================
	/// Isto E DECLARADO EM VOZ ALTA porque a instrucao era portar numeros do DM e **eles nao existem
	/// la**. Varridos: `supersaiyanbuff.dm` inteiro (855 linhas), `1A Defines.dm`, e o repo do DM
	/// inteiro atras de `speedMod|physoffMod|kioffMod|techniqueMod|Tspeed|Tphysoff|Ttechnique`. O que
	/// o DM da aos grades e SO: a porta de maestria (`SSJ_GRADE2_PCT` 50 / `SSJ_GRADE3_PCT` 70), o
	/// fator de BP (`SSJ_GRADE2_FACTOR` 1,5 / `_GRADE3_FACTOR` 2), o dreno
	/// (`SSJ_GRADE2_DRAIN` 0,040 / `_GRADE3_DRAIN` 0,055) e o corpo musculoso
	/// (`apply_ussj_body()`, `supersaiyanbuff.dm:222`). Zero stat. O `DU-SOURCE-master` original nem
	/// conhece a palavra `ultrassj` -- o USSJ e invencao deste DM e nunca teve penalidade nenhuma.
	///
	/// ============================ ENTAO O DM ENTRA COMO FORMA, E NAO COMO VALOR ============================
	/// O arquetipo existe e e o OOZARU (`Oozaru.dm:127-129`): `Tphysoff += 1.2`, `Tspeed -= 1.5`,
	/// `Ttechnique -= 1.5` -- forte, lento, e desajeitado NA MESMA ORDEM DE GRANDEZA do bonus, com a
	/// penalidade um pouco maior que ele. E a descricao do dono para o Grade 3, palavra por palavra
	/// ("um belo buff physical mas seria lento e teria dificuldade de acertar"). A escala dos fatores
	/// segue a que o proprio DM usa quando mexe em stat por multiplicacao: `tsujin.dm:110-116` vai de
	/// 1,20 a 1,50, o Majin usa 1,30 (`Majin.dm:37`), a criacao de personagem vai de 0,7 a 1,4
	/// (`CharacterCreation.dm:177-188`).
	///
	/// ============================ E O GRADE 2 TEM QUE SER POUCO ============================
	/// *"o grade 2 n teria tanta diferenca, mas teria"*. Ele fica proximo do neutro nos quatro eixos.
	/// O que separa os dois grades e o TAMANHO do desvio, nao o sinal dele.
	///
	/// Com o `StatCap` comprimindo (medido em stat cru 20: fator 1,60 no physoff ~= +19% de
	/// `Ephysoff`), o efeito final e:
	///
	///           physoff   kioff   tecnica   speed   cadencia        | na pratica
	///   Grade 2   1,15     1,10     0,96     0,92     0,92          | +6% de soco, quase nada de custo
	///   Grade 3   1,60     1,20     0,75     0,60     0,60          | +19% de soco, erra ~12% mais,
	///                                                               | e acertado +30%, soca 1,67x mais devagar
	///
	/// SAO NUMEROS PROPOSTOS, e a estrutura e que importa: mexer qualquer um deles e mexer aqui, e
	/// nenhum outro arquivo precisa saber.
	/// ==================================================================================================
	/// </summary>
	public static readonly ModsDeForma Grade2Mods =
		new(Physoff: 1.15, Kioff: 1.10, Tecnica: 0.96, Speed: 0.92, Cadencia: 0.92);

	/// <inheritdoc cref="Grade2Mods"/>
	public static readonly ModsDeForma Grade3Mods =
		new(Physoff: 1.60, Kioff: 1.20, Tecnica: 0.75, Speed: 0.60, Cadencia: 0.60);

	/// <summary>
	/// O NEUTRO: tudo 1. E o que a base e as 38 formas sem <see cref="FormaDef.Mods"/> valem, e existe
	/// pra `AplicarForma` poder AFIRMAR os sete campos sem um `if` -- voltar pra base tem que escrever
	/// 1 em cada um, e nao "deixar como estava".
	/// </summary>
	public static readonly ModsDeForma SemMods = new();

	/// <summary>A base do SSJ1 por raca: 2 normal, 1,35 diluido. `ssj1base`.</summary>
	public const double Ssj1Base = 2, Ssj1BaseDiluido = 1.35;

	/// <summary>Maestria de God Ki que abre cada degrau divino. `1A Defines.dm`.</summary>
	public const double GodkiBluePct = 33, GodkiRoyalePct = 50, GodkiUiUePct = 70;

	/// <summary>
	/// TEMPO DE COMBATE CONTINUO ate a rampa encher, em segundos.
	///
	/// `LSSJ_RAMP_TICKS 600` pro Legendary (`lssjbuff.dm:184`) e `combatTime / 720` pro Primal
	/// (`supersaiyanbuff.dm:572`).
	///
	/// ============================ ISTO ESTAVA ERRADO POR CADENCIA, E NAO POR UNIDADE ============================
	/// Estava escrito `600 / 6.0` (100 s), com o argumento de que o `combatTime` vem do `Stats()`, que
	/// dorme `sleep(2)`. Duas coisas erradas na mesma frase: `sleep(2)` sao 0,2 s e nao 1/6 de segundo
	/// (ver <see cref="TempoDoDm"/>), e -- pior -- **o `combatTime` nao mora no `Stats()`**. Ele e
	/// incrementado em `Stats.dm:52`, que esta dentro do `GlobalStats()`, cujo `sleep(3)` da **0,3 s**
	/// por ciclo. Entao 600 ciclos = **180 s** e 720 = **216 s**.
	///
	/// E o DM crava o numero: `1A Defines.dm:44` comenta o proprio define --
	/// <c>LSSJ_RAMP_TICKS 600 //ciclos de GlobalStats (~0.3s) ... (600 = ~3min)</c>. Tres minutos.
	/// ========================================================================================================
	/// </summary>
	public const double RampaLssjSegundos = 600 * TempoDoDm.TiquesDoLacoGlobalStats / TempoDoDm.TiquesPorSegundo;
	public const double RampaPrimalSegundos = 720 * TempoDoDm.TiquesDoLacoGlobalStats / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// Quanto de maestria se ganha por segundo DENTRO da forma.
	///
	/// ~3 horas em forma pra dominar. Muito de proposito: maestria e o unico eixo do jogo que nao
	/// se compra nem se treina de fora -- so se paga ficando na forma, gastando Ki.
	/// </summary>
	public const double MaestriaPorSegundo = 100.0 / (3 * 3600);

	// ==================================================================================
	// AS ENTRADAS
	// ==================================================================================

	/// <summary>
	/// A FOLHA DESTE CORPO JA DESENHA O RABO?
	///
	/// `SaiyanObjects.dm:134` -- `else if(container.ssj >= 4) alpha = 0`: o overlay do rabo some no
	/// SSJ4 porque o `saiyan4body` ja o traz desenhado, e o macaco tem o dele. Deixar o rabo de
	/// 32 px por cima daria DOIS rabos.
	///
	/// O CORPO MUSCULOSO E O CONTRA-EXEMPLO, e e por ele que isto e uma funcao e nao um
	/// `Corpo != Nenhum`: as folhas musculosas sao o corpo da RACA inchado -- humanas, 32x32, sem
	/// rabo nenhum desenhado (medido: as tres so trazem `walk/attack/kick/blast/flight_mov/kb/ko/
	/// meditate/train`, o mesmo repertorio do corpo base). Um Saiyajin em Grade 2 ou Legendary
	/// continua com o rabo dele a mostra, e a pergunta "tem corpo proprio?" responderia que nao.
	///
	/// Mora aqui, e nao no cliente, porque quem responde e o SIMBOLO: o
	/// <see cref="Catalogo.CorDoRabo"/> faz a mesma pergunta sem ter um `CharacterVisual` por perto.
	/// </summary>
	public static bool FolhaTrazORabo(CorpoDeForma c) =>
		c is CorpoDeForma.Ssj4 or CorpoDeForma.Oozaru or CorpoDeForma.OozaruDourado;

	/// <summary>
	/// ESTA FOLHA DE CORPO DESENHA UMA **PESSOA**? -- ou seja, ha um rosto humanoide onde a camada de
	/// olhos (e o resto do que se veste num rosto) cai.
	///
	/// ============================ O DEFEITO QUE ELA FECHA, E ELE E DO DM TAMBEM ============================
	/// O dono, sobre a larva do bio-androide: *"os bio androides o OLHO FICA VOANDO na forma de BARATA,
	/// faca com q os olhos SUMAM ao ficar nessa forma"*. Fui procurar o mecanismo no original pra copiar
	/// e **nao ha nenhum**: la o olho e `/obj/overlay/eyes/default_eye`
	/// (`OverlayMobHandlers.dm:251-256`), pendurado por `Eyes()` (`CharacterCreation.dm:23`) pra TODA
	/// raca sem crivo nenhum, e recriado em todo login pelo `RefreshEyes()` (`Login.dm:349`). O
	/// `dnl_bio_hatch` chega a tirar o CABELO da larva (`RemoveHair()` + `hair="Bald"`,
	/// `DNALabs.dm:466-467`) e passa direto pelo olho -- o irmao do gate de careca do bio
	/// (`HairObject.dm:168`) nunca foi escrito. No BYOND e ate pior: a `CellLarva.dmi` tem um estado so
	/// (`""`) contra os oito da `Eyes_Black.dmi`, entao em voo ou no nocaute as pupilas ficariam
	/// sozinhas na tela. **Isto e conserto de projeto, nao porte de linha.**
	///
	/// ============================ E POR QUE ELA PERGUNTA A FOLHA, E NAO "E A LARVA?" ============================
	/// Porque a larva nao e o unico caso e nunca foi: o DM troca o corpo INTEIRO em oito sistemas, e
	/// dois deles poem um corpo que nao e gente (a larva, `DNALabs.dm:491`, e o lobisomem quadrupede,
	/// `Werewolves.dm:109`). O Oozaru so escapa por acidente -- ele esvazia o `overlayList`
	/// (`Oozaru.dm:118-123`), que nem e onde o olho mora. Um `if (larva)` daria certo hoje e erraria no
	/// dia em que o lobo (ou qualquer bicho novo) for portado, caladamente, com o mesmo sintoma.
	///
	/// Aqui a pergunta e do SIMBOLO, ao lado da <see cref="FolhaTrazORabo"/> e pela mesma razao: quem
	/// sabe o que a arte desenha e o catalogo, nao o cliente. O corpo de repouso responde pela irma
	/// dela em <see cref="Jandirus.Core.Appearance.VisualCatalog.CorpoTemRosto"/> -- sao os dois eixos
	/// por onde uma folha pode chegar ao boneco, e nao ha um terceiro.
	///
	/// O MACACO ENTRA AQUI POR COMPLETUDE, e nao porque precise: o `CharacterVisual.Escondida` ja apaga
	/// tudo nele por ser CRIATURA (quadro maior que o do corpo). Deixa-lo de fora seria escrever "esta
	/// folha tem rosto" sobre um focinho de dez metros, contando com uma segunda regra pra corrigir --
	/// e a `Escondida` existe justamente pra nao haver regra escrita duas vezes.
	/// =========================================================================================================
	/// </summary>
	public static bool FolhaTemRosto(CorpoDeForma c) =>
		c is not (CorpoDeForma.Oozaru or CorpoDeForma.OozaruDourado);

	/// <summary>A forma base. Existe como entrada pra "voltar ao normal" nao ser um caso especial.</summary>
	public const string IdBase = "base";

	/// <summary>
	/// O WRATHFUL. Ele e o unico degrau do jogo que aparece por ID em uma derivacao
	/// (<see cref="CorDoOlho"/>), e por isso o id tem nome: um `d.Id == "wrathful"` solto no meio de
	/// um `switch` e uma string que ninguem acha quando o id mudar, e o compilador nao ajuda.
	///
	/// A EXCECAO E DO DONO E NAO MINHA -- *"LSSJ: brancos, sem iris; **excecao**: `wrathful`:
	/// AMARELO"*. Ela nao sai de campo nenhum de proposito: ver a <see cref="CorDoOlho"/>.
	/// </summary>
	public const string IdWrathful = "wrathful";

	/// <summary>
	/// O MISTICO. Constante porque ele e o unico id que o SERVIDOR precisa dizer em voz alta: e
	/// pelo nome dele que o ritual do Kaioshin vai chamar `EstadoDeForma.Liberar` -- ver
	/// <see cref="FormaDef.SoPorConcessao"/>.
	/// </summary>
	public const string IdMistico = "mistico";

	/// <summary>
	/// O SUPER SAIYAJIN COMUM. Constante porque ele e o unico degrau com DOIS estados de
	/// apresentacao -- ver <see cref="DominouOSuperSaiyajin"/>, que e quem usa isto.
	/// </summary>
	public const string IdSsj1 = "ssj1";

	/// <summary>
	/// ============================ O SSJ1 A 100% TEM NOME E CABELO PROPRIOS, E NAO E OUTRA FORMA ============================
	/// O dono, literal e com enfase: *"o ssj 1 em 100% de maestria ... (NAO E FORMA SEPARADA)"*. E o
	/// Grade 4 do original -- o Full Power Super Saiyan --, que no DM tambem nao e um valor a mais de
	/// `container.ssj`: e o mesmo `ssj = 1` com o overlay `SSjFP` no lugar do `SSj`.
	///
	/// DUAS COISAS PENDURADAS NO MESMO PREDICADO, e e o achado que fez este metodo existir. O pedido
	/// do dono chegou como dois defeitos separados ("o nome nao muda" e "o cabelo fp nao troca"), mas
	/// os dois acontecem no MESMO instante e pela MESMA razao. Uma funcao so responde as duas
	/// (<see cref="NomeDe"/> e <see cref="SufixoDoCabeloDe"/>) -- escritos como duas condicoes
	/// separadas, um dia uma passaria a valer aos 100% e a outra aos 99 e ninguem veria.
	///
	/// O `dominada` NAO e lido daqui de dentro de proposito: este arquivo nao conhece o
	/// <see cref="Maestrias"/> de ninguem em particular, e quem chama nem sempre TEM o livro --
	/// o cliente recebe este fato como um bit no `S2C.Forma`, porque a maestria dos OUTROS nao viaja
	/// (e nao deve viajar: um numero onde um bit basta e vazamento de progressao alheia).
	/// ==================================================================================================
	/// </summary>
	public static bool DominouOSuperSaiyajin(FormaDef? d, bool dominada) =>
		dominada && d != null && d.Id == IdSsj1;

	/// <summary>
	/// COMO ESTA FORMA SE CHAMA NA TELA -- o funil unico do nome.
	///
	/// ============================ POR QUE DERIVADO, E NAO UM CAMPO A MAIS ============================
	/// O <see cref="FormaDef.Nome"/> continua sendo a identidade escrita da ENTRADA; o que muda com o
	/// estado do jogador e a APRESENTACAO. Um segundo campo ("NomeDominado") seria uma string a mais
	/// pra 33 entradas onde uma sozinha a usa.
	///
	/// E O `Id`/`IdRede` NAO MUDAM. Nome e so texto: o save, a rede e todas as comparacoes continuam
	/// no id. Conferido nesta tarefa -- o unico lugar do projeto que compara NOME e o
	/// `GameServer.Admin.FormaPorTexto`, e ele resolve a ENTRADA a partir do que o admin digitou
	/// (onde maestria nao entra), nao o estado de ninguem.
	/// ==========================================================================================
	/// </summary>
	public static string NomeDe(FormaDef? d, bool dominada) =>
		d == null ? "" : DominouOSuperSaiyajin(d, dominada) ? NomeDoGrade4 : d.Nome;

	/// <summary>
	/// O MESMO NOME, PRA QUEM TEM O LIVRO EM VEZ DO BIT.
	///
	/// ============================ POR QUE DUAS PORTAS PRA UMA REGRA SO ============================
	/// O `bool` existe porque a maestria dos OUTROS nao viaja pela rede (ver <see cref="NomeDe"/>): o
	/// cliente recebe um bit no `S2C.Forma` e e tudo o que ele pode saber sobre o corpo alheio. Mas os
	/// tres lugares que nomeiam a forma de quem ESTA ali -- a aba Formas, as recusas do servidor e a
	/// lista de maestrias do admin -- tem o <see cref="Maestrias"/> na mao, e obriga-los a escrever
	/// `livro.De(id) >= 100` cada um por si seria semear o numero 100 por tres arquivos. No dia em que
	/// o teto da maestria mudasse, o nome mudaria em um lugar e nao nos outros -- que e exatamente o
	/// envelhecimento calado que este funil existe pra impedir.
	///
	/// Os `100` daqui sao o mesmo teto do <see cref="Maestrias.Por"/>, que nao deixa a barra passar
	/// disso; "dominada" e literalmente "a barra encostou no fim".
	/// =========================================================================================
	/// </summary>
	public static string NomeDe(FormaDef? d, Maestrias? livro) =>
		NomeDe(d, d != null && livro != null && livro.De(d.Id) >= 100);

	/// <summary>
	/// O nome do SSJ1 dominado. "Grade 4" e a palavra do dono; o "Super Saiyajin" na frente e a
	/// convencao das duas entradas irmas (`grade2` e `grade3` se chamam "Super Saiyajin Grade N"),
	/// e sem ele a aba mostraria "Grade 4" solto ao lado de "Super Saiyajin Grade 2".
	/// </summary>
	public const string NomeDoGrade4 = "Super Saiyajin Grade 4";

	/// <summary>
	/// O SUFIXO DE CABELO DA FORMA -- o mesmo funil, pro outro lado do pedido.
	///
	/// ============================ ONDE O `fp` MORRIA ============================
	/// A pasta `SSJ Hairs/` tem dez folhas `*SSjFP*` desenhadas e **nenhuma entrada do catalogo pedia
	/// esse sufixo**: os unicos valores escritos em <see cref="FormaDef.SufixoDoCabelo"/> sao `SSj`,
	/// `USSj`, `SSj2`, `SSj3`, `SSJ4`, `LSSj`, `UI` e `UIPerf`. O resolvedor do cliente ate tinha uma
	/// linha de heranca pra `SSjFP`, mas ela era inalcancavel -- ninguem chamava com esse sufixo.
	/// As dez folhas estavam 100% mortas.
	///
	/// ============================ E O DISCO DECIDIU O ALCANCE ============================
	/// O dono perguntou se o `fp` vale so pro SSJ1 ou tambem pros degraus acima. A pasta responde
	/// sozinha: **todo arquivo com "FP" no nome termina em `SSjFP`/`SSJFP`**. Nao existe `SSj2FP`,
	/// `SSj3FP`, `USSjFP`, `SSJ4FP` nem `LSSjFP`. O Full Power e o degrau do SSJ1 e so dele -- que e
	/// tambem o que o canone diz (o Grade 4 e a maestria do Super Saiyajin, nao do SSJ2).
	/// ================================================================================
	/// </summary>
	public static string SufixoDoCabeloDe(FormaDef? d, bool dominada) =>
		d == null ? "" : DominouOSuperSaiyajin(d, dominada) ? SufixoDoSuperSaiyajinPleno : d.SufixoDoCabelo;

	/// <summary>
	/// O sufixo das folhas de Full Power (`Hair_GokuSSjFP`, `Hair_KidGohanSSjFP`...). Constante pelo
	/// mesmo motivo do <see cref="SufixoDoUltraInstinto"/>: ele e o unico sufixo que nao esta escrito
	/// em nenhuma entrada do catalogo -- quem o produz e a derivacao acima, e quem o consome e o
	/// resolvedor de cabelo do cliente.
	/// </summary>
	public const string SufixoDoSuperSaiyajinPleno = "SSjFP";

	// ============================ A ORDEM DESTE BLOCO IMPORTA ============================
	// Estas tabelas TEM que ser declaradas ANTES de `Todas`. Inicializador de campo estatico roda
	// na ordem de declaracao: com elas embaixo, `Todas` as leria ainda NULAS e as formas divinas
	// nasceriam sem gate nenhum -- o Blue liberado pra qualquer linhagem, calado. O compilador
	// avisa (CS8601) e o aviso e verdadeiro; foi assim que apareceu.
	// ==================================================================================
	/// <summary>
	/// A CLASSE DO ROSE. `statsaiyan.dm:156`, e o comentario de la e a ficcao inteira:
	///
	///   "Kaio -- NAO NASCE: so pelo desejo do corpo Saiyajin (Kaioshin Golden Apple -- estilo Goku
	///    Black, WishTable.dm). Dois diferenciais de um Saiyajin comum: Zenkai SEM aposentadoria e
	///    Rose no lugar do Blue (godki_mod > 1)."
	///
	/// Ou seja: ninguem cria um personagem Rose. Alguem toma o corpo de um Saiyajin por um desejo
	/// -- e a linha divina dele sai rosa em vez de azul.
	/// </summary>
	public const string ClasseRose = "Kaio";

	/// <summary>
	/// AS CLASSES E LINHAGENS QUE TEM O KI DIVINO "PADRAO" (SSG e Blue).
	///
	/// ============================ REGRA DO DONO, MAIS APERTADA QUE O DM ============================
	/// No BYOND o `god_form_mult` nao olha linhagem nenhuma -- qualquer um que despertasse o ki
	/// divino subia SSG -> Blue -> Royale. O dono fechou isso: **God e Blue sao do Saiyajin de
	/// linhagem NORMAL (classe Low-Class, Normal ou Elite) e do meio-Saiyajin New Generation**, e
	/// o **Blue Evolution e so do Elite**. Legendary, Primal, Legendary Primal, Future Lineage e
	/// Prodigial nao tem nem o God.
	///
	/// O DM ja concordava com a metade mais restritiva: `godki.dm:347` diz "Royale/Rose 2 56x (50%,
	/// so Elite/Kaio via USSJ)" -- o degrau final sempre foi de Elite.
	///
	/// O Prodigial nao fica sem escada: ele tem a propria (Mistico -> Beast), e e o que o DM manda.
	/// ==========================================================================================
	/// </summary>
	public static readonly string[] OrigemDoKiDivino = ["Saiyan", "New Generation"];

	/// <summary>As classes que alcancam SSG e Blue. "Legendary" fica de fora a pedido do dono.</summary>
	// "New Generation" e a classe do MEIO-SAIYAJIN (`stathalfbreed.dm:71`), e nao uma classe de
	// Saiyajin puro -- ela entra aqui porque a regra do dono inclui o meio-Saiyajin New Generation.
	public static readonly string[] ClassesDoKiDivino =
		["Low-Class", "Normal", "Elite", "New Generation", ClasseRose];

	/// <summary>A classe do Blue Evolution / Rose 2: so o topo. `godki.dm:347`.</summary>
	public const string ClasseElite = "Elite";

	/// <summary>A classe do Beast. `godki.dm:349`.</summary>
	public const string ClasseProdigial = "Prodigial";

	// ==================================================================================
	// AS TRES LINHAS NAO-SAIYAJIN QUE NAO SAO O FROST DEMON
	//
	// Os numeros do BYOND, um por constante, com o arquivo:linha de onde saem. Eles moram aqui e nao
	// soltos nas entradas pelo mesmo motivo das tintas de cabelo logo abaixo: cada um responde por
	// mais de um lugar (o multiplicador do Heran aparece na curva E no `BaseDaClasse`, a porta do
	// Alien aparece na entrada E na conta da segunda forma), e literal repetido e literal que diverge.
	// ==================================================================================

	/// <summary>As racas destas tres linhas, como o `races.json` as escreve.</summary>
	public const string RacaNamekuseijin = "Namekian", RacaHeran = "Heran", RacaAlien = "Alien";

	/// <summary>
	/// O MEIO-SAIYAJIN, como o `races.json` o escreve: <b>"Halfbreed"</b> -- e a grafia e o problema
	/// inteiro (ver <see cref="EhSaiyajin"/>). Na tela ele se chama "Half-Saiyan"
	/// (`CreationScreen.cs:1135`), no genoma ele carrega o proto "Saiyan" (`Birth.cs:98`), e a raca
	/// gravada no personagem e esta.
	/// </summary>
	public const string RacaMeioSaiyajin = "Halfbreed";

	/// <summary>`snamekmult` -- 5x. `Super_Namek.dm:5`.</summary>
	public const double SuperNamekMult = 5;

	/// <summary>`snamekdrain` -- 1,5% do Ki maximo. `Super_Namek.dm:6`.</summary>
	public const double SuperNamekDreno = 0.015;

	/// <summary>
	/// `trueKiMod = 2` -- o Super Namekuseijin DOBRA o tanque de Ki (`Super_Namek.dm:41`).
	///
	/// E o unico numero de <see cref="TetoDeKi"/> que nao sai de um campo do `Fighter`: as escadas
	/// Saiyajin leem `ssjenergymod` e companhia (que skills reescrevem), e o Namekuseijin tem o 2
	/// cravado no proprio buff. Copiar `ssjenergymod` aqui daria 2 por acidente hoje e mentiria no
	/// dia em que uma skill mexesse naquele campo.
	/// </summary>
	public const double SuperNamekKi = 2;

	/// <summary>`ayyform1mult` / `ayyform2mult` -- 2x e 4x. `Alien_Transformations.dm:4-5`.</summary>
	public const double AlienMult1 = 2, AlienMult2 = 4;

	/// <summary>`ayyform1at` -- 1 milhao de BP. `Alien_Transformations.dm:3`.</summary>
	public const double PortaAlien1 = 1_000_000;

	/// <summary>
	/// A porta da 2a forma Alien -- **10 milhoes**, e ela e uma CONTA e nao o `ayyform2at`.
	///
	/// `Alien_Trans()` cobra `BP >= ayyform2at / ayyform1mult` (`:9`), ou seja 20 milhoes divididos
	/// por 2. Escrever 10 milhoes direto perderia a razao de ser do numero: se o multiplicador da 1a
	/// forma mudar, a porta da 2a anda junto no original.
	/// </summary>
	public const double PortaAlien2 = 20_000_000 / AlienMult1;

	/// <summary>`ayyform1drain` / `ayyform2drain` -- 1,0% e 1,5%. `Alien_Transformations.dm:7-8`.</summary>
	public const double AlienDreno1 = 0.010, AlienDreno2 = 0.015;

	// =====================================================================
	// A SUPER PERFEITA -- `CellFormBuff.dm`
	// =====================================================================
	/// <summary>`cell4mult` -- 8x. `CellFormBuff.dm:56`.</summary>
	public const double SuperPerfeitoMult = 8;

	/// <summary>`cell4drain` -- 1,0% do Ki maximo por segundo. `CellFormBuff.dm:57`.</summary>
	public const double SuperPerfeitoDreno = 0.010;

	/// <summary>
	/// A PORTA DA SUPER PERFEITA -- **750 milhoes**, e ela e uma CONTA como a da 2a forma Alien.
	///
	/// `Cell4()` cobra `BP >= cell4at / cell3mult` (`CellFormBuff.dm:74`): `cell4at` e 3 bilhoes e
	/// `cell3mult` e 4 (o proprio multiplicador da forma perfeita). Escrever 750 milhoes direto
	/// perderia a razao de ser do numero -- no original a porta anda junto se o degrau perfeito
	/// mudar de peso, e e ESSE o desenho: a forma perfeita ja quadruplicou o BP base, entao cobrar o
	/// `cell4at` cheio pediria o quadruplo de novo.
	/// </summary>
	public const double PortaSuperPerfeito = 3e9 / Races.BioAndroids.MultDoPerfeito;

	/// <summary>
	/// A CURVA DE MAESTRIA DO HERAN -- os fatores relativos de `heran_form_mult()`
	/// (`HeranBuff.dm:245-249`): 1x no degrau cru, e ate 2,016x com a forma dominada.
	///
	/// Ela e a MESMA nas duas formas (o DM repete a lista trocando so o `ssjmult` pelo `ssj2mult`), e
	/// por isso e uma constante em vez de dois literais: o dia em que o dono quiser a maestria do
	/// Heran valendo mais, ele mexe num lugar e as duas formas andam.
	/// </summary>
	public static readonly double[] CurvaDoHeran = [1, 1.2, 1.68, 2.016];

	/// <summary>
	/// `ssjmult` por classe -- `statheran.dm:26-42`. A chave vazia e o `else` do original (Epsilon).
	///
	/// A INVERSAO E DE PROPOSITO e e o desenho do Heran: o Omega transforma PIOR (1,30x contra 3x do
	/// Low-Class) e acende quase 7x mais tarde (ver `LimiaresPessoais.RolarHeran`), e o que ele
	/// compra com isso sao stats muito melhores. Quem "consertar" o 1,30 quebra a classe rara.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, double> HeranBasePorClasse =
		new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
		{ ["Omega"] = 1.30, ["Low-Class"] = 3, [""] = 2.4 };

	/// <summary>`ssj2mult` por classe -- as mesmas tres linhas do `statheran.dm`.</summary>
	public static readonly IReadOnlyDictionary<string, double> Heran2BasePorClasse =
		new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
		{ ["Omega"] = 2, ["Low-Class"] = 4, [""] = 3 };

	/// <summary>
	/// O dreno do Max Power por degrau de maestria -- `list(0.025, 0.015, 0.008, 0)`
	/// (`HeranBuff.dm:39`). **A forma DOMINADA nao drena nada**, que e a recompensa inteira.
	/// </summary>
	public static readonly double[] HeranDreno1 = [0.025, 0.015, 0.008, 0];

	/// <summary>O dreno do True Max Power -- `list(0.040, 0.025, 0.012, 0)` (`HeranBuff.dm:43`).</summary>
	public static readonly double[] HeranDreno2 = [0.040, 0.025, 0.012, 0];

	/// <summary>
	/// O DIVISOR DA PORTA DO **True Max Power** -- 50, e ele e do Heran e de mais ninguem.
	///
	/// `heran.dm:38` cobra `ssj2at/50 <= BP`, contra o `/6` que o `Transformation Controls.dm:19`
	/// cobra do Super Saiyajin 2. Nao e engano do original: o `ssj2at` do Heran NAO e sorteado (ver
	/// `LimiaresPessoais.RolarHeran` -- ele fica no valor de fabrica, 3,5 bilhoes), e sem o divisor
	/// maior a segunda forma dele seria inalcancavel a vida inteira.
	/// </summary>
	public const double HeranGateMult2 = 50;

	// ==================================================================================
	// AS TINTAS DE CABELO -- os `rgb()` literais do DM, um por linha que pinta
	// ==================================================================================
	// Elas ficam em constantes e nao soltas nas entradas porque cada uma responde por VARIOS degraus
	// (o verde por cinco, o azul por dois), e porque cada uma e a mesma cor que o RABO recebe por
	// derivacao -- ver `Catalogo.CorDoRabo`. Um literal repetido cinco vezes e cinco chances de a
	// linha ficar com dois verdes.

	/// <summary>
	/// `rgb(0,110,0)` -- `SaiyanObjects.dm:83-87`, o `EffectStart` do `/obj/overlay/hairs/ssj/lssjhair`.
	///
	/// ============================ ELE VALE TAMBEM PRA LINHA PRIMAL, E ISSO E UMA CORRECAO ============================
	/// No Legendary comum o verde EXISTE e funciona. No Legendary Primal o DM **tenta** e entrega
	/// dourado: `HairObject.dm:184-186` chama `updateOverlay(..., ssjhair, 0,100,0)` com a intencao
	/// escrita no comentario da propria linha ("LSSJ/LSSJ2/LSSJ3 = cabelo SSJ1/2/3 verde"), mas (a) o
	/// `addOverlay()` (`Overlays.dm:49-55`) descarta o RGB quando recebe icone junto, e (b) o
	/// `ssjN/EffectStart` reescreve o `icon` logo depois. O verde nunca chega na tela.
	///
	/// O dono pediu a INTENCAO, e ela e o unico jeito de a linha fazer sentido: aura verde, contorno
	/// verde e cabelo dourado descrevem duas formas diferentes no mesmo boneco.
	///
	/// E `110` E NAO O `100` DA CHAMADA de proposito: sao dois verdes pra a mesma ideia, e o `0,110,0`
	/// e o que o Legendary comum usa e o unico que o jogo alguma vez desenhou. Um valor so.
	/// ==========================================================================================================
	///
	/// ============================ MAS O `0,110,0` E VERDE PURO, E O LENDARIO E AMARELADO ============================
	/// O `rgb(0,110,0)` nao tem vermelho nenhum, e no matiz isso nao muda com o tom: medido, ele
	/// desenhava `#006e00 -> #008f00 -> #00b400 -> #00c700`, quatro verdes de bandeira, escuros e sem
	/// uma gota de amarelo. O dono: *"o lssj e um verde AMARELADO em ambos os casos (primal e
	/// normal)"*, com o Broly de referencia.
	///
	/// E A ARTE DO PROPRIO JOGO CONCORDA, o que fecha a questao: a unica folha de LSSJ desenhada a mao
	/// que o resolvedor alcanca (`Kale Hair LSSJ.png`) tem os tons `#9fbb00`, `#c5e001`, `#f3ff15`,
	/// `#f3ff9d` -- verde-limao indo pro amarelo. O verde puro do DM descrevia um cabelo que o
	/// desenhista da casa nunca desenhou.
	///
	/// MEDIDO NA GPU sobre `Hair_GokuSSj.png` (que e onde a tinta cai de verdade -- o sufixo `LSSj` so
	/// acha arte propria pra a Kale, todos os outros 58 penteados herdam a folha dourada):
	///
	///     #7ba81f  ->  #a0db28  ->  #c9ff33  ->  #dfff38
	///
	/// O tom de 30% cai em `#a0db28`, o `#a8e02a` da referencia do dono; o piso `#7ba81f` e a sombra
	/// `#6f9e1a` que ele pediu. Quatro degraus distintos, nenhum canal estourado.
	/// ==========================================================================================================
	/// </summary>
	private const string VerdeDoCabeloLendario = "7ba81f";

	/// <summary>
	/// ============================ A CONTA DA TINTA QUE CAI SOBRE ARTE JA PINTADA ============================
	/// As tintas daqui pra baixo NAO sao somadas: elas caem no `tinta_modo = 1` do
	/// `Personagem.gdshader` (o MATIZ), porque o sprite embaixo delas ja e o cabelo DOURADO de Super
	/// Saiyajin. Ver <see cref="ModoDoCabelo.TrocarETingir"/>.
	///
	/// A conta do matiz e `cor_desenhada = tinta * luminancia * 2`. Medidos os quatro tons da folha de
	/// SSJ (`Hair_GokuSSj.png`, e sao os MESMOS quatro em todas as variantes):
	///
	///     sombra #948c08 -> luz*2 = 0,999      claro  #f7de29 -> luz*2 = 1,639
	///     meio   #c6b508 -> luz*2 = 1,305      realce #f7ef94 -> luz*2 = 1,812
	///
	/// Ou seja o desenho MULTIPLICA a tinta por ate 1,81 -- e o canal que passar de `255/1,812 = 141`
	/// satura nos tons claros, achatando os quatro degraus em um so.
	///
	/// ============================ E A RAMPA COMECA EM 1,00x: O HEXA E O PIXEL MAIS ESCURO ============================
	/// Esta metade faltava, e era ela que o dono continuava vendo. `luz*2` do tom mais escuro da folha
	/// da **0,999** -- ou seja o hexa escrito nao e "a cor do cabelo", e o **piso** dela: o tom que
	/// pinta 42% dos pixels sai identico ao que esta escrito aqui, e os outros tres sobem dali ate
	/// 1,81x. Calibrar so pelo TETO (o `x 141/238` abaixo) garante que o realce nao estoura e nao diz
	/// nada sobre o piso -- e com `082b8d` o piso desenhava `#082b8d`, que e azul-marinho, e com
	/// `8d1e4d` desenhava `#8d1e4d`, que e vinho ("o rose ta vermelho praticamente").
	///
	/// **Entao os hexas daqui pra baixo sao escolhidos pelas DUAS pontas**, e cada um traz medida na
	/// GPU (Godot 4.7, `Hair_GokuSSj.png` real, imagem do viewport lida pixel a pixel) do que ele
	/// desenha nos quatro tons. Nenhum deles foi aceito por ser "o hexa certo".
	/// ==========================================================================================================
	///
	/// ============================ E A METADE MAIOR DO "ESCURO" NAO ERA COR NENHUMA ============================
	/// Antes desta passada o `Personagem.gdshader` fechava com `COLOR = c * COLOR`, e o `COLOR` que
	/// chega no fragment do Godot **ja vem multiplicado pela textura**: a folha saia ELEVADA AO
	/// QUADRADO. Medido, o Rose desenhava `#521002 #8f1c03 #e02b14 #f73351` -- vermelho-tijolo, que e
	/// a queixa do dono ao pe da letra --, e no penteado base (preto) o SSG desenhava `#000000` em 40%
	/// dos pixels. O conserto esta no shader; sem ele, mexer em hexa aqui era chutar no escuro.
	/// ======================================================================================================
	/// </summary>
	/// <remarks>
	/// ============================ E POR QUE NAO BASTAVA "UM AZUL MAIS FORTE" ============================
	/// O canal antigo era SOMA (`clamp(c.rgb + tinta, 0, 1)`), e soma NAO ABAIXA canal nenhum. O cabelo
	/// de SSJ tem R &gt;= 148 e G &gt;= 140 nos quatro tons, entao qualquer tinta deixava R e G altos --
	/// e azul de verdade precisa dos dois BAIXOS. Medido com o `0d49ee` do DM sobre os quatro tons:
	///
	///     #948c08 -> #a1d5f6      #f7de29 -> #ffffff
	///     #c6b508 -> #d3fef6      #f7ef94 -> #ffffff
	///
	/// dois brancos chapados e um a um ponto do branco -- que e exatamente o "aplicar azul por cima
	/// esta dando BRANCO" do dono. E o rosa `ee3382` dava `#ffbf8a`, `#ffe88a`, `#ffffab`, `#ffffff`:
	/// PESSEGO e CREME, que e o "o cabelo dele esta LOIRO".
	///
	/// Somar MAIS azul anda pro lado errado -- quanto mais forte a tinta, mais branco. O DM ja sabia
	/// disso e por isso ESCURECE antes (`icon -= rgb(100,100,100)` em `SaiyanObjects.dm:12`), mas
	/// portar aquilo ao pe da letra tambem nao resolve: rodada sobre os mesmos quatro tons, a receita
	/// de la (subtrai 100, soma o azul TRES vezes, cada passo saturando) devolve `#57ffff`, `#89ffff`,
	/// `#baffff` e `#baffff` -- troca o branco por CIANO e ainda funde dois tons no mesmo valor.
	///
	/// O que resolve e trocar a OPERACAO, e ela ja existia no shader pro Beast.
	/// ==================================================================================================
	///
	/// ============================ O `082b8d` NAO MORREU: ELE MUDOU DE DONO ============================
	/// Aquele valor e o `0d49ee` do DM trazido pra a escala do matiz, e ele continua sendo isso -- so
	/// que ele e ESCURO, e o dono disse duas vezes que o Blue nao pode ser. Palavra dele: *"o cabelo
	/// atual do blue e pra ser do evolved/royale q e um azul escuro"*. Entao o hexa do DM desceu um
	/// degrau (ver <see cref="AzulDoCabeloRoyale"/>) e o Blue ficou com a referencia que o dono deu --
	/// o Goku SSGSS, *"um azul mais claro"*, ciano.
	///
	/// MEDIDO NA GPU sobre `Hair_GokuSSj.png` (42% / 30% / 19% / 10% dos pixels):
	///
	///     #3392c7  ->  #42beff  ->  #53efff  ->  #5cffff
	///
	/// As duas pontas passam: o piso `#3392c7` e azul de ceu (o antigo era `#082b8d`, marinho), e o
	/// realce fecha em ciano claro sem virar branco chapado. Subir mais o piso nao cabe nesta rampa --
	/// com `3aa8e8` cravado no piso, os dois tons de cima medem `#5fffff` e `#69ffff`, que e o branco
	/// que o dono ja reclamou uma vez.
	/// ==============================================================================================
	/// </remarks>
	private const string AzulDoCabeloDivino = "3392c7";

	/// <summary>
	/// O AZUL DO BLUE EVOLUTION -- e ele e o `rgb(13,73,238)` do DM (`SaiyanObjects.dm:18`) na escala
	/// do matiz, `(13,73,238) x 141/238`. Era a tinta do Blue ate esta passada.
	///
	/// ============================ POR QUE DEIXOU DE SER UMA FRACAO DO BLUE ============================
	/// Ele nasceu como `0,70 x AzulDoCabeloDivino` (`061e63`), com o argumento de que "os dois sao o
	/// mesmo azul divino e o Evolution e o Blue mais fundo". O dono desfez isso ao dar cores separadas
	/// pras duas formas: *"o cabelo atual do blue e pra ser do evolved/royale"*. O `061e63` ficou sem
	/// dono e foi DELETADO -- fracao de um azul que nao existe mais nao e um valor, e um valor solto
	/// que volta pro lugar errado no primeiro que mexer.
	///
	/// MEDIDO NA GPU sobre `Hair_GokuSSj.png`: `#082b8d -> #0a38b8 -> #0d46e7 -> #0e4eff`. Contra o
	/// `#3392c7 -> #5cffff` do Blue, e a mesma familia meio tom abaixo -- o "azul escuro" pedido.
	/// ============================================================================================
	/// </summary>
	private const string AzulDoCabeloRoyale = "082b8d";

	/// <summary>
	/// `rgb(238,51,130)` -- `SaiyanObjects.dm:14-16`, o ramo `godki_mod > 1`.
	///
	/// ============================ ROSA CHICLETE, E O PISO E QUEM DECIDE ============================
	/// O valor anterior era `8d1e4d`, o hexa do DM trazido pra a escala do matiz por `x 141/238`. Essa
	/// conta cuida do TETO e nao do PISO -- e o piso e 42% do cabelo. `#8d1e4d` e vinho, e o dono
	/// mediu isso a olho: *"o rose ta vermelho praticamente, era pra ser um rosa chiclete"*.
	///
	/// MEDIDO NA GPU sobre `Hair_GokuSSj.png`:
	///
	///     #d15694  ->  #ff70c1  ->  #ff8df2  ->  #ff9cff
	///
	/// O tom de 30% cai em `#ff70c1`, que e o `#ff69b4` chiclete da referencia; o piso `#d15694` e
	/// rosa cheio e nao vinho. Cravar o chiclete no PISO (`ff69b4`) atira os outros tres pra
	/// `#ff89eb`/`#ffacff`/`#ffbeff` -- lilas lavado, com o vermelho saturado em tres dos quatro.
	///
	/// O OLHO E A COLADA DO ROSE FORAM CONFERIDOS NA MESMA PASSADA e NAO mudaram: o olho `e0409a` cai
	/// em soma sobre sprite preto (desenha o proprio hexa, matiz 326 graus -- rosa), e a colada
	/// `ff7ac6` MULTIPLICA o cinza 192 da folha `god - grey` e mede `#b85c95`. Nenhum dos dois le como
	/// vermelho; o que os fazia ler errado era o quadrado do shader, e isso foi consertado la.
	/// ==========================================================================================
	/// </summary>
	private const string RosaDoCabeloDivino = "d15694";

	/// <summary>
	/// `rgb(226,51,28)` -- `HairObject.dm:73-75`, o `gdki_me()` do `/obj/overlay/hairs/hair`.
	///
	/// LARANJA-TIJOLO, e nao o `ff4d6a` rosado que o port usava: aquele valor foi escolhido por mim
	/// quando o SSG ainda era "um Blue vermelho". Este e o do arquivo.
	///
	/// AS DUAS VARIANTES POR CARGO DO DM FICARAM DE FORA -- Anjo (`rgb(245,245,245)`, `:64-66`) e Deus
	/// da Destruicao (`rgb(163,0,136)`, `:69-70`). Elas nao sao da FORMA, sao do CARGO: o mesmo SSG
	/// muda de cor conforme a assinatura de quem o veste, e o catalogo nao conhece cargo. Ficam pra
	/// quando o sigilo de cargo chegar ao visual.
	/// </summary>
	private const string VermelhoDoCabeloDivino = "e2331c";

	/// <summary>
	/// `rgb(190,196,208)` -- `UltraInstinct.dm:263`, o `Blend(..., ICON_ADD)` do `ui_eye_icon()`.
	///
	/// NAO E O `rgb(185,190,200)` DO CABELO PRATEADO (`:291`, o `/hairs/uisilver`), que mora na
	/// entrada `ui_perfected` como `b9becb`. Sao duas pratas diferentes no original, e por cinco
	/// pontos de canal -- copiar uma pra outra passaria despercebido pra sempre.
	///
	/// E E A UNICA COR DE OLHO DO JOGO QUE VEIO DO DM. Todas as outras de <see cref="CorDoOlho"/> sao
	/// desenho novo do dono -- ver o cabecalho de <see cref="VerdeDoOlhoSuperSaiyajin"/>.
	/// </summary>
	private const string PrataDoOlhoDoInstinto = "bec4d0";

	// ==================================================================================
	// AS CORES DE OLHO -- eixo NOVO, e o unico deste arquivo sem fonte no original
	// ==================================================================================

	/// <summary>
	/// O VERDE ESMERALDA DA ESCADA SAIYAJIN -- *"todo SSJ normal: verde esmeralda"*.
	///
	/// ============================ O CRITERIO QUE VALE PRA AS SEIS CORES ABAIXO ============================
	/// A iris tem **dois pixels de largura** (quatro de frente), e eles estao cercados: esclerotica
	/// `#fcfdfd` de um lado e o cilio `#000000` por cima -- medido no corpo, ver
	/// <see cref="CorDoOlho"/>. Cor clara encostada em branco desse tamanho nao le como cor: le como
	/// borrao. **O contraste tem que vir do VALOR**, entao todas as cores daqui pra baixo estao
	/// puxadas pra baixo em relacao ao tom "de catalogo" que a palavra do dono sugere.
	///
	/// Este e o hexa que a industria chama de esmeralda (`50c878`) a **80%**, canal a canal:
	/// (80,200,120) x 0,8 = (64,160,96). Luminancia 135 contra os 252 da esclerotica -- o verde
	/// aparece. Em 100% ele fica a 168 e o olho vira uma mancha verde-clara sobre branco.
	/// ==============================================================================================
	///
	/// COBRE OS CINCO DEGRAUS SEM CORPO PROPRIO (`ssj1`, `grade2`, `grade3`, `ssj2`, `ssj3`) mais o
	/// `future_ssj`. Os tres SSJ4 saem por <see cref="AmareloDoOlhoDoSsj4"/>.
	/// </summary>
	private const string VerdeDoOlhoSuperSaiyajin = "40a060";

	/// <summary>
	/// O AMARELO DOS TRES SSJ4 -- *"amarelado, e nao muito forte"*.
	///
	/// "NAO MUITO FORTE" E UMA CONTA e nao um chute: e o meio do caminho entre a aura do SSJ4
	/// (`ffc93a` = 255,201,58) e o CINZA DE MESMA LUMINANCIA dela (202,202,202) -- ((255+202)/2,
	/// (201+202)/2, (58+202)/2) = (229,202,130). Ou seja a cor da forma com metade da saturacao,
	/// que e o que "amarelado" quer dizer quando o vizinho e "amarelo".
	///
	/// FICA CLARO DE PROPOSITO, contra o criterio do <see cref="VerdeDoOlhoSuperSaiyajin"/>: aqui o
	/// pedido e justamente que ele NAO grite. Quem separa o SSJ4 do resto da escada e a pelagem
	/// vermelha do corpo inteiro, nao dois pixels.
	/// </summary>
	private const string AmareloDoOlhoDoSsj4 = "e5ca82";

	/// <summary>
	/// O AMARELO DO WRATHFUL -- a unica excecao nomeada da tabela do dono, e ela existe porque a linha
	/// dele inteira fica de olhos BRANCOS.
	///
	/// FORTE, ao contrario do <see cref="AmareloDoOlhoDoSsj4"/>: o dono escreveu "AMARELO" liso pra
	/// este e "amarelado, e nao muito forte" pro outro, e a diferenca entre os dois tem que ser
	/// visivel senao a excecao nao existe na tela. `e8bc18` = (232,188,24), saturacao cheia com a
	/// luminancia (183) baixa o bastante pra sobreviver ao branco da esclerotica encostada.
	/// </summary>
	private const string AmareloDoOlhoDoWrathful = "e8bc18";

	/// <summary>
	/// ============================ "OLHOS BRANCOS, SEM IRIS" ============================
	/// **E o hexa da ESCLEROTICA DO CORPO, medido**: `#fcfdfd` nos pixels `x=13` e `x=18` da linha do
	/// olho, iguais em `NewPaleMale`, `BaseBlackMale`, `BaseTanFemale` e `BaseWhiteMale`.
	///
	/// E POR ISSO ELE NAO E `ffffff`. A camada de olhos e a IRIS e so ela (a conta esta no cabecalho
	/// do <see cref="CorDoOlho"/>), entao apagar a iris e pinta-la da cor do branco que esta ENCOSTADO
	/// nela. Tres pontos de canal de diferenca bastariam pra desenhar de volta, em tom palido,
	/// exatamente a forma que se queria sumir -- e ninguem olhando de longe saberia dizer por que o
	/// olho do Legendary "parece ter alguma coisa".
	///
	/// COBRE AS DUAS LINHAS LENDARIAS INTEIRAS, menos o `wrathful`.
	/// ==================================================================================
	/// </summary>
	private const string BrancoSemIris = "fcfdfd";

	/// <summary>
	/// O AZUL DO BLUE E DO BLUE EVOLUTION.
	///
	/// ESCRITO E NAO DERIVADO, e o motivo esta no cabecalho do <see cref="CorDoOlho"/>: as tintas de
	/// cabelo da linha estao calibradas pra o modo MATIZ sobre arte dourada, onde o hexa e o PISO de
	/// uma rampa que sobe ate 1,81x. O olho nao tem rampa nenhuma -- ele e soma sobre sprite preto, e
	/// desenha o hexa cru. O <see cref="AzulDoCabeloRoyale"/> (`082b8d`, que o Blue Evolution veste)
	/// daria (8,43,141) em dois pixels: azul-marinho quase preto, que na tela e "o olho ficou escuro"
	/// e nao "o olho ficou azul". Uma cor propria e o unico jeito de os dois degraus terem o mesmo
	/// olho sem que ele siga o piso de qualquer um deles.
	///
	/// `1f6ae8` = (31,106,232), e ele tambem esta longe do `0099cc` que o CORPO ja desenha na iris
	/// por baixo: se o azul da forma fosse o azul de fabrica, o Blue seria a unica forma do jogo cuja
	/// mudanca de olho e invisivel pra metade dos personagens.
	/// </summary>
	private const string AzulDoOlhoDivino = "1f6ae8";

	/// <summary>
	/// O ROSA DO ROSE E DO ROSE 2 -- o irmao do <see cref="AzulDoOlhoDivino"/>, e pelo mesmo corte.
	///
	/// O DONO NAO NOMEOU O ROSE nesta tabela (ele disse "SSJ Blue (e Evolution): AZUL"). A linha Rose
	/// e a MESMA escada em outra cor -- e a razao de ela ser `LinhaDeForma` propria e mecanica, nao
	/// visual --, entao ela recebe o espelho do azul, como ja recebe no contorno e nas coladas. Um
	/// Rose de olho azul seria a unica peca dele que ficou da cor do irmao.
	///
	/// `e0409a` = (224,64,154), com luminancia 104 contra os 99 do azul: os dois degraus gemeos das
	/// duas escadas leem com o mesmo PESO, que e o que faz um par ler como par.
	/// </summary>
	private const string RosaDoOlhoDivino = "e0409a";

	/// <summary>
	/// O VERMELHO DO OLHO DO BEAST -- *"o olho do beast era pra ser vermelho"*.
	///
	/// ============================ ELE NAO DERIVA DE NADA, E ISSO E MEDIDO ============================
	/// Todas as outras cores desta tabela ou vem do DM (a prata do Instinto) ou sao buscadas na tinta
	/// de cabelo da propria forma (o vermelho do SSG, o roxo do Ego). A Fera nao tem de onde: as
	/// QUATRO cores que ela ja declara sao `b6bac4` (cabelo), `7d5af0` (chama), `3f8cff`/`b163ff`
	/// (o par do contorno) e a faisca lilas -- nenhuma vermelha, e nenhuma perto de vermelho. Um
	/// vermelho derivado aqui seria vermelho inventado com cara de derivacao.
	///
	/// ============================ E NAO E O <see cref="VermelhoDoLimitBreaker"/> ============================
	/// `ff2d2f` esta a um passo daqui e reusa-lo seria de graca. **A regra deste arquivo diz que nao**:
	/// e a mesma razao pela qual <see cref="RoxoDaFera"/> existe separado do <see cref="RoxoDoEgo"/>
	/// e <see cref="RosaDaColada"/> separado do <see cref="RosaDivino"/> -- sao formas de linhas
	/// diferentes que nao se devem nada, e a constante compartilhada faz o dia em que alguem afinar o
	/// Limit Breaker mexer no olho da Fera, com o defeito aparecendo longe da mudanca.
	///
	/// ============================ O VALOR: 90% DO VERMELHO QUE O JOGO JA FALA ============================
	/// (255,45,47) x 0,9 = (229,40,42). A escolha do FAMILIA e deliberada -- o vermelho do jogo e o do
	/// Limit Breaker, e um segundo vermelho de outra familia leria como erro de paleta --, e os 10% de
	/// desconto sao o criterio do <see cref="VerdeDoOlhoSuperSaiyajin"/>: a iris tem dois pixels
	/// cercados por esclerotica `#fcfdfd`, entao o contraste vem do VALOR e a cor tem que descer.
	///
	/// Luminancia 80 (0,2126R + 0,7152G + 0,0722B), entre os ~100 do par divino
	/// (<see cref="AzulDoOlhoDivino"/> 99, <see cref="RosaDoOlhoDivino"/> 104) e os 54 do vermelho
	/// puro. Vermelho e a matiz mais ESCURA da roda -- `ff0000` nem chega perto dos 100 do azul --,
	/// entao a conta aqui e o contrario da do amarelo: nao ha como subir sem lavar pra rosa, e o que
	/// se cuida e nao afundar no `#000000` do cilio desenhado por cima (80 contra 0, folgado).
	/// ==================================================================================================
	/// </summary>
	private const string VermelhoDaFera = "e5282a";

	/// <summary>
	/// A FAISCA DO MISTICO -- e SO dele desde que o dono pediu roxo na Fera (ver
	/// <see cref="RoxoDaFaiscaDaFera"/>). Medida em `Electric_Mystic.dmi` -- a folha que o
	/// `MysticEffect` veste (`Mystic.dm:20-23`) -- que **nao tem matiz nenhuma**: cinco tons neutros,
	/// `c4c4c4`, `bdbdbd`, `ffffff`, `cbcbcb` e `d2d2d2`.
	///
	/// O REALCE E NAO O TOM DOMINANTE, e o porque esta na `CorDosRaios`: esta cor vai pro HALO do
	/// raio, e o `RaioDaForma.gdshader:211` avisa que o empurrao de cor nas bordas existe pra o raio
	/// *"nao virar risco cinza"* -- passar o `c4c4c4` do arquivo seria pedir o risco cinza.
	///
	/// ============================ E ELE PERDEU CONTRASTE NESTA MESMA PASSADA ============================
	/// Ate aqui o branco caia sobre a `FieryGod` (tom dominante `ff411c`, luminancia 102,7): 152 pontos
	/// de diferenca. Com o Mistico acendendo a chama do JOGADOR (ver <see cref="ChamaDoJogador"/>), o
	/// fundo virou o `#9ECCFF` do `Aura.CorDoKiCru` -- luminancia **197,9**, e a distancia caiu pra
	/// **57**. Continua legivel (a faisca desenha SOBRE o corpo e a chama fica ATRAS dele, entao os
	/// dois so se encostam na silhueta), mas e menos do que era e o dono nao pediu isso -- e efeito
	/// colateral do pedido 4. Anotado pra ele olhar; nao mexi porque ele nomeou a Fera.
	/// ==================================================================================================
	///
	/// TEM O MESMO VALOR DO <see cref="BrancoNeutro"/> e sao duas escritas de proposito, como o
	/// <see cref="VermelhoDoLimitBreaker"/> e a `Aura` dele: aquele e "nao ha forma nenhuma", este e
	/// a arte do Mistico. Mudar um nao pode mexer no outro.
	/// </summary>
	private const string BrancoDaFaiscaMistica = "ffffff";

	/// <summary>
	/// A FAISCA DA FERA -- *"no beast os raiozinhos sao roxos"*.
	///
	/// ============================ ELA NASCE NUMA COLISAO, E A COLISAO E MEDIDA ============================
	/// Este e o unico lugar do jogo em que dois pedidos do dono se cruzam. O outro pedido tirou a Fera
	/// da chama do SSG (ver <see cref="Folha"/>), e com isso a chama dela passou a ser o `7d5af0` que a
	/// propria entrada declara: **roxo**. Faisca roxa dentro de chama roxa e a familia do defeito
	/// verde-sobre-verde do `primal_legendary2`.
	///
	/// A MATIZ NAO SALVA, e nao adianta fingir que sim: `7d5af0` esta em H=254 e este esta em H=271 --
	/// **17 graus**, contra os ~180 que separam o <see cref="AzulDaFaisca"/> da aura dourada que ele
	/// atravessa. Quem le a faisca da Fera nao le pela cor: le pelo VALOR.
	///
	/// ENTAO O QUE SE ESCOLHEU FOI O VALOR, e o criterio ja existia -- e literalmente o do
	/// <see cref="AzulDaFaisca"/> (*"claro e lavado pra ele aparecer POR CIMA da aura"*), aplicado do
	/// lado roxo do circulo. Luminancia (0,2126R + 0,7152G + 0,0722B): a chama da 108,3 e esta da
	/// **190,4** -- 1,76x mais clara. E o mesmo truque do branco do Mistico com a cor que o dono pediu
	/// por cima, e nao um roxo escolhido "porque roxo".
	///
	/// ============================ O QUE ISTO **NAO** RESOLVE, e o dono tem que ver ============================
	/// 1,76x de luminancia e o bastante pra a faisca existir sobre a chama, mas ela vai ler como um
	/// LILAS CLARO sobre roxo -- nao como o contraste que o azul tem sobre o dourado. Se ele achar
	/// fraco, o precedente dele mesmo diz onde mexer: no `primal_legendary2` a resposta foi trocar a
	/// COR DO RAIO, nao a da aura. O proximo passo daquele lado e sair da matiz (o par do contorno da
	/// Fera ja oscila pro <see cref="AzulDaFera"/>, que esta a 40 graus daqui) -- mas isso e a palavra
	/// dele, nao minha, porque ele pediu ROXO.
	/// ==================================================================================================
	/// </summary>
	private const string RoxoDaFaiscaDaFera = "d9b0ff";

	/// <summary>
	/// OS DOIS SUFIXOS DO ULTRA INSTINCT -- e sao os unicos do catalogo que NAO nomeiam uma variante
	/// de penteado: nomeiam UM ARQUIVO, `Hair_UltraInstinct` e `Hair_MasteredUltraInstinct`
	/// (`UltraInstinct.dm:279` e `:285`), que sao o cabelo do Goku e de mais ninguem.
	///
	/// Estao em constante porque o resolvedor (`CabelosDeForma.Universal`) precisa reconhece-los pelo
	/// MESMO texto pra saber que ali nao se procura padrao de nome nenhum -- e "os dois lados leem a
	/// mesma string" e o tipo de acordo que envelhece calado quando e escrito duas vezes.
	/// </summary>
	public const string SufixoDoUltraInstinto = "UI";

	/// <inheritdoc cref="SufixoDoUltraInstinto"/>
	public const string SufixoDoUltraInstintoPerfeito = "UIPerf";

	public static readonly FormaDef[] Todas =
	[
		new() { Id = IdBase, IdRede = 0, Linha = LinhaDeForma.Saiyajin, Ordem = 0,
				Nome = "Base", Mult = [1], NumeroDm = 0, Intensidade = 0,
				Aura = "ffffff", Cabelo = "", Desc = "Sua forma natural." },

		// ---------------------------------------------------------------------------
		// LINHA SAIYAJIN -- supersaiyanbuff.dm
		// ---------------------------------------------------------------------------
		new() { Id = "ssj1", IdRede = 10, Linha = LinhaDeForma.Saiyajin, Ordem = 10,
				Nome = "Super Saiyajin", NumeroDm = 1, Intensidade = 1,
				// `stepped_mastery_mult(2, 6)`: 2x ate 99%, 6x no 100%. Nao ha meio-termo.
				Mult = [Ssj1Base, 6], MultDiluido = [Ssj1BaseDiluido, 6],
				Dreno = [0.025, 0.015, 0],
				PortaBp = PortaSsj1, ChaveDoLimiar = "ssjat",
				// SEM TINTA: a arte de Super Saiyajin JA e dourada. Ver `Catalogo.CorDoCabelo`.
				Aura = "ffd24a", SufixoDoCabelo = "SSj",
				Desc = "O cabelo se ergue e doura. 2x ate dominar; 6x aos 100% de maestria." },

		// OS GRADES NAO TEM PISO (PisoSobreAnterior = 0), e isso e do original: o SSJ1 dominado
		// (6x) supera os dois de proposito -- eles ficam obsoletos quando voce cresce.
		//
		// ============================ E OS DOIS INCHAM O CORPO ============================
		// `supersaiyanbuff.dm:222` -- o `if(1.5)` do `switch(container.ssj)` chama
		// `container.apply_ussj_body()`, e o comentario do proprio DM diz o que ela faz: *"USSJ:
		// bulk the body to its muscular skin (by skin tone)"*. Os dois grades sao o mesmo estado
		// `ssj = 1.5` la (ver `NumeroDm` logo abaixo), entao os dois recebem.
		//
		// `remove_ussj_body()` (`:193`, na troca de forma) devolve o icone guardado -- aqui isso e
		// so `Corpo = Nenhum` no degrau seguinte, e quem apaga a camada e o `CharacterVisual`.
		// O DM precisa do `ussj_saved_icon` porque la a troca e destrutiva; aqui e uma camada.
		// =================================================================================
		new() { Id = "grade2", IdRede = 15, Linha = LinhaDeForma.Saiyajin, Ordem = 15,
				Nome = "Super Saiyajin Grade 2", NumeroDm = 1.5, Intensidade = 2,
				Mult = [Ssj1Base * Grade2Fator], MultDiluido = [Ssj1BaseDiluido * Grade2Fator],
				Dreno = [0.035], Mods = Grade2Mods,
				PortaBp = PortaSsj1, ChaveDoLimiar = "ssjat", PedeMaestria = Grade2Pct, ForaDoTronco = true,
				Aura = "ffcf3a", SufixoDoCabelo = "USSj", Corpo = CorpoDeForma.Musculoso,
				Desc = "Musculatura inchada: soca um pouco mais forte e fica um pouco mais lento. "
					 + "Nao se compra: abre com 50% de maestria no SSJ1." },

		new() { Id = "grade3", IdRede = 16, Linha = LinhaDeForma.Saiyajin, Ordem = 16,
				Nome = "Super Saiyajin Grade 3", NumeroDm = 1.5, Intensidade = 2,
				Mult = [Ssj1Base * Grade3Fator], MultDiluido = [Ssj1BaseDiluido * Grade3Fator],
				Dreno = [0.05], Mods = Grade3Mods,
				PortaBp = PortaSsj1, ChaveDoLimiar = "ssjat", PedeMaestria = Grade3Pct, ForaDoTronco = true,
				Aura = "ffc21f", SufixoDoCabelo = "USSj", Corpo = CorpoDeForma.Musculoso,
				Desc = "Poder bruto no limite do corpo: soco muito mais pesado, mas lento, "
					 + "desajeitado e facil de acertar. Abre com 70% de maestria no SSJ1." },

		new() { Id = "ssj2", IdRede = 20, Linha = LinhaDeForma.Saiyajin, Ordem = 20,
				// RAIOS = 1 (leve): o `if(2)` do DM acende UMA folha de eletricidade. O SSJ3 acende
				// tres. Ver `FormaDef.Raios`.
				Nome = "Super Saiyajin 2", NumeroDm = 2, Intensidade = 2, Raios = 1,
				Mult = [4, 6, 8, 10], Dreno = [0.045, 0.03, 0.015], PisoSobreAnterior = 2,
				PortaBp = PortaSsj2, ChaveDoLimiar = "ssj2at",
				// A UNICA ENTRADA DO CATALOGO COM ESTE CAMPO -- ver `FormaDef.NegadaAoBioDeLaboratorio`.
				// O bio-androide de tanque tem `canSSJ`, e sem esta linha ele pegaria o SSJ2 pela
				// RAIVA como qualquer Saiyajin -- pulando a forma perfeita, o SSJ1 dominado e a
				// propria morte, que sao o preco inteiro da unica transformacao que e so dele.
				NegadaAoBioDeLaboratorio = true,
				Aura = "ffe36b", SufixoDoCabelo = "SSj2", Desc = "Faiscas percorrem a aura." },

		// SSJ3 e multiplicador FIXO (`ssj3base = 16`): a maestria dele NAO sobe o poder, so alivia
		// o dreno -- e o dreno dele e o pior da linha.
		//
		// ============================ O SSJ3 NAO VEM DA RAIVA: VEM DO SSJ2 DOMINADO PELA METADE ============================
		// Regra do dono: *"ssj3 NAO libera com raiva. Ele pede 50% de maestria do ssj2 e o poder
		// minimo pessoal"*. E o DM concorda literalmente -- `Transformation Controls.dm:46` cobra
		// `usr.BP >= usr.ssj3at/10` **e** `usr.ssj2mastery >= 50`, e em nenhum ramo daquele arquivo
		// o SSJ3 olha pra `Emotion`. Quem colocou raiva no SSJ3 durante o porte copiou o
		// `mst_form_needs_rage` (`MasterStudent.dm:246-249`), que e a lista do caminho MESTRE-ALUNO
		// (o mestre provocando o despertar do aluno) e nao a do jogador subindo sozinho.
		//
		// OS DOIS CAMPOS ABAIXO SAO A REGRA INTEIRA, e nenhum deles e novo:
		//   * `PortaBp`/`ChaveDoLimiar` -> o poder minimo PESSOAL, ja sorteado no nascimento
		//     (`LimiaresPessoais.Porta` divide o `ssj3at` por `Ssj2GateMult`, porque la ele e BP
		//     EXPRESSO). Isso ja estava certo e nao mudou;
		//   * `PedeMaestriaDe`/`PedeMaestria` -> os 50% de SSJ2, que FALTAVAM.
		//
		// E E ASSIM QUE ELE SAI DA LISTA DE RAIVA -- sem uma linha de excecao. O `NasceDaRaiva`
		// pergunta `PedeMaestria <= 0` (ver o cabecalho dele): a mesma edicao que cobra o SSJ2
		// dominado pela metade tira o SSJ3 do tronco da furia, porque no tronco so ficam os degraus
		// que NAO se treinam. Escrever "menos o ssj3" na derivacao teria dado o mesmo resultado hoje
		// e mentido amanha.
		//
		// `PedeMaestriaDe = "ssj2"` EXPLICITO, e nao herdado do `Anterior`: hoje as duas coisas dao
		// o mesmo (o `Anterior` pula os grades por `ForaDoTronco` e chega no SSJ2), mas o DM nomeia
		// `ssj2mastery`, e um degrau novo inserido entre as ordens 20 e 30 mudaria calado de quem e
		// a maestria cobrada.
		// ================================================================================================================
		new() { Id = "ssj3", IdRede = 30, Linha = LinhaDeForma.Saiyajin, Ordem = 30,
				Nome = "Super Saiyajin 3", NumeroDm = 3, Intensidade = 3, Raios = 2,
				Mult = [16], Dreno = [0.075, 0.05, 0.03], PisoSobreAnterior = 2,
				PortaBp = PortaSsj3, ChaveDoLimiar = "ssj3at",
				PedeMaestriaDe = "ssj2", PedeMaestria = Ssj3PedeSsj2Pct,
				Aura = "fff08a", SufixoDoCabelo = "SSj3",
				Desc = "Cabelo ate a cintura, sobrancelhas somem. Exige o SSJ2 dominado pela "
					 + "metade (50%). Dreno brutal." },

		// ============================ O PRE-REQUISITO QUE ERA CODIGO ============================
		// `ApeshitRevert`: sair do Oozaru Dourado tendo o SSJ4 despertado vira SSJ4. Era um `if`
		// dentro do Oozaru; virou este campo. E `SaiyanLineage != "Primal Saiyan"` (SSj4(), :737).
		//
		// OS DOIS CAMPOS DO OOZARU NAO SE REPETEM -- eles falam com pessoas diferentes.
		// `PedeFormaDespertada` pega quem nunca foi Dourado e diz "assuma essa forma uma vez";
		// `PedeMaestria`+`PedeMaestriaDe` pegam quem ja foi e diz quanto falta DOMAR. E a regra que
		// o dono pediu: "pra ele liberar a possibilidade de virar ssj4 ele precisa passar por essa
		// parte do golden oozaru" -- passar por ela e chegar aos 100%, nao so ve-la. A ordem das
		// checagens no `Avaliar` (forma despertada e #3, maestria e #6) e o que faz cada um sair na
		// hora certa.
		// =======================================================================================
		new() { Id = "ssj4", IdRede = 40, Linha = LinhaDeForma.Saiyajin, Ordem = 40,
				Nome = "Super Saiyajin 4", NumeroDm = 4, Intensidade = 4,
				Como = Curva.Rampa, Mult = [20, 40], Dreno = [0.06], PisoSobreAnterior = 2,
				PortaBp = PortaSsj4, ChaveDoLimiar = "rawssj4at",
				PedeLinhagem = Oozaru.LinhagemPrimal, PedeFormaDespertada = "oozaru_dourado",
				PedeMaestriaDe = "oozaru_dourado", PedeMaestria = 100,
				Aura = "ffc93a", SufixoDoCabelo = "SSJ4", Corpo = CorpoDeForma.Ssj4,
				Desc = "Pelagem vermelha. So sai do Oozaru Dourado. 20x, ate 40x dominado." },

		new() { Id = "ssj4_full_power", IdRede = 41, Linha = LinhaDeForma.Saiyajin, Ordem = 41,
				Nome = "Super Saiyajin 4 Full Power", NumeroDm = 5, Intensidade = 4,
				Como = Curva.Rampa, Mult = [32, 50], Dreno = [0.07], PisoSobreAnterior = 2,
				PortaBp = PortaSsj4, ChaveDoLimiar = "rawssj4at",
				PedeLinhagem = Oozaru.LinhagemPrimal, PedeMaestria = 100,
				Aura = "ffe14d", SufixoDoCabelo = "SSJ4", Corpo = CorpoDeForma.Ssj4,
				Desc = "O SSJ4 levado ao limite. Exige o SSJ4 dominado. 32x, ate 50x." },

		// God Form: multiplicador FIXO, sem maestria. `ssj4fplbmult = 56`.
		new() { Id = "ssj4_limit_breaker", IdRede = 42, Linha = LinhaDeForma.Saiyajin, Ordem = 42,
				// RAIOS = 2 (cheio), e ele e o unico do jogo com faisca VERMELHA -- pedido do dono
				// ("limit breaker tem raios vermelhos"), a cor saindo de `Catalogo.CorDosRaios` pela
				// MESMA guarda de ki divino que ja pinta o contorno dele. No DM a faisca dele e folha
				// propria e sao DUAS (`:254-257`: `ssj4lb_sparks` + `ssj4lb_lightning`), as duas com
				// `temporary = 0`, ou seja crepitar constante enquanto a forma durar.
				Raios = 2,
				Nome = "Super Saiyajin 4 Limit Breaker", NumeroDm = 6, Intensidade = 5,
				Mult = [56], Dreno = [0.085], PisoSobreAnterior = 2,
				PortaBp = PortaSsj4, ChaveDoLimiar = "rawssj4at",
				PedeLinhagem = Oozaru.LinhagemPrimal, PedeMaestria = 100, PedeGodKi = GodkiRoyalePct,
				// VERMELHA, e ela VOLTOU a ser: eu a tinha dourado quando o dono disse que o SSJ4
				// conta como Super Saiyajin, e ele corrigiu -- a regra do dourado e do CONTORNO
				// (`CorDoContorno`), nao da aura. A aura do Limit Breaker e a assinatura dele.
				Aura = "ff2d2f", SufixoDoCabelo = "SSJ4", Corpo = CorpoDeForma.Ssj4,
				Desc = "Forma divina: 56x fixo. Rompe o teto do corpo Saiyajin." },

		// ---------------------------------------------------------------------------
		// LINHA FUTURO -- `FutureLineage`, substitui o SSJ1 (ssj_effective_mult:720)
		// ---------------------------------------------------------------------------
		// `min(2 + round(mastery/10) * 2, 20)`. O `round()` de UM argumento no BYOND e FLOOR, entao
		// o degrau k liga em (k-1)*10% -- e por isso os limiares comecam em 0 e nao em 10.
		new() { Id = "future_ssj", IdRede = 11, Linha = LinhaDeForma.Futuro, Ordem = 10,
				Nome = "Future Super Saiyajin", NumeroDm = 1, Intensidade = 2,
				Mult = [2, 4, 6, 8, 10, 12, 14, 16, 18, 20],
				Limiares = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90],
				Dreno = [0.025, 0.02, 0.015],
				PortaBp = PortaSsj1, ChaveDoLimiar = "ssjat",
				// SEM TINTA: a arte de Super Saiyajin JA e dourada. Ver `Catalogo.CorDoCabelo`.
				Aura = "ffd24a", SufixoDoCabelo = "SSj",
				Desc = "A linhagem do futuro: 10 estagios de 2x a 20x, +2x a cada 10% de maestria." },

		// ---------------------------------------------------------------------------
		// LINHA LEGENDARY -- lssjbuff.dm:162-180
		// ---------------------------------------------------------------------------
		// Todas com CombateSobeAoMaximo: `max(piso pela maestria, rampa do combate)`. A maestria e
		// so um PISO; quem lutar 180 s seguidos chega ao maximo sem maestria nenhuma. E o que faz o
		// Legendary ser a linha da FURIA e nao a do treino.
		new() { Id = IdWrathful, IdRede = 110, Linha = LinhaDeForma.Legendary, Ordem = 10,
				Nome = "Wrathful", NumeroDm = 1, Intensidade = 2,
				Como = Curva.Rampa, Mult = [1.5, 10], CombateSobeAoMaximo = true,
				Dreno = [0.02, 0.015, 0.01],
				PortaBp = LimiaresPessoais.RestSsjatInicial, ChaveDoLimiar = "restssjat",
				// ============================ O WRATHFUL NAO MEXE NO CABELO NEM NO RABO ============================
				// SEM SUFIXO E SEM TINTA -- `ModoDoCabelo.Base`, e ele e o unico degrau transformado do
				// jogo que fica assim. `HairObject.dm:208-210`, o caso `if(1)` do `switch(lssj)`:
				// `updateOverlay(/obj/overlay/hairs/hair)` -- o penteado BASE, na cor do jogador -- mais
				// `updateOverlay(/obj/overlay/effects/menacing_aura)`, que e um overlay de CORPO e nao
				// cabelo. O typepath `rlssjhair` (a tinta azul `rgb(0,0,100)`, `SaiyanObjects.dm:77-81`)
				// existe e NUNCA e adicionado: so removido, em tres lugares. E codigo morto.
				//
				// O RABO CAI SOZINHO com o sufixo vazio (ver `Catalogo.CorDoRabo`), e no DM pelo mesmo
				// motivo: o `else if(container.lssj)` de `SaiyanObjects.dm:114` so tem caso pra `==2` e
				// `>=3` -- o `lssj == 1` nao esta na tabela. Ele saia DOURADO aqui, e o dono viu.
				// O que sobra pra distinguir a forma e a AURA (a `AuraLSSjBig`) e o contorno verde.
				// ================================================================================================
				//
				// ============================ E ELE TAMBEM NAO INCHA O CORPO ============================
				// `Corpo = Nenhum` (o padrao) e a UNICA divergencia do enunciado do dono nesta tarefa --
				// ele disse *"LSSJ (qualquer uma MENOS a lssj4)"*, e o Wrathful e uma LSSJ. Fica de fora
				// porque tres fontes independentes dizem a mesma coisa, e nenhuma delas sou eu:
				//
				//   * o DM -- `lssjbuff.dm:84-89`, o caso `if(1)` do `switch(container.lssj)`, e o UNICO
				//     dos quatro que nao tem linha de `container.icon`. O inchaco comeca no `if(2)`;
				//   * o proprio dono, no acerto dos olhos: o Wrathful e *"a forma que fica com o corpo e
				//     o cabelo da base"* -- ver a `CorDoOlho`, onde ele e a unica excecao por id do
				//     arquivo inteiro;
				//   * a `Desc` desta entrada, que ja dizia "a furia SEM FORMA", contra "o corpo incha"
				//     do C-Type e "o corpo mal cabe em si" do Legendary.
				//
				// Se ele quiser mesmo o Wrathful inchado, e uma palavra: `Corpo = CorpoDeForma.Musculoso`.
				// ======================================================================================
				Aura = "76ff7a",
				Desc = "A furia sem forma. 1,5x, e ate 10x com a luta ou a maestria." },

		new() { Id = "c_type", IdRede = 120, Linha = LinhaDeForma.Legendary, Ordem = 20,
				Nome = "Super Saiyajin C-Type", NumeroDm = 2, Intensidade = 3,
				Como = Curva.Rampa, Mult = [12, 20], CombateSobeAoMaximo = true, PisoSobreAnterior = 2,
				Dreno = [0.04, 0.03, 0.02],
				PortaBp = LimiaresPessoais.UnrestSsjatInicial, ChaveDoLimiar = "unrestssjat",
				// O C-TYPE E DOURADO, E ISSO NAO E DESCUIDO: `HairObject.dm:211-212` da a ele
				// `updateOverlay(/obj/overlay/hairs/ssj/ssj1, ssjhair)` -- a folha de Super Saiyajin
				// normal, SEM tinta -- e o rabo dele e o `rgb(218,218,38)` do `SaiyanObjects.dm:116`.
				// O verde da linha comeca no degrau seguinte; aqui quem e verde e so a aura.
				//
				// AQUI COMECA O CORPO INCHADO. `lssjbuff.dm:97`, o caso `if(2)`:
				// `if(container.icon=='White Male.dmi' && !doexpandicon2) container.icon = 'White Male
				// Muscular 2.dmi'`. E a `Desc` desta entrada ja prometia isso desde o porte.
				Aura = "4dff5a", SufixoDoCabelo = "SSj", Corpo = CorpoDeForma.Musculoso,
				Desc = "O corpo incha e o verde toma a aura. 12x a 20x." },

		// ============================ O FULL POWER NAO ERA FORMA: ERA ESTA AQUI A 100% ============================
		// O dono, literal: *"vc fez 2 transformacoes separadas dnv q na vdd sao a mesma, o full power
		// e o legendary super saiyan so q quando a maestria chega em 100%"*. A entrada `140`
		// (`legendary_full_power`) foi APAGADA e o que ela dava mora aqui:
		//
		//   * O MULTIPLICADOR -- a rampa era `[25, 40]` e o Full Power entregava `50` travado. Agora
		//     a rampa e `[25, 50]`: a mesma reta, so que o fim dela e o numero do Full Power. Aos
		//     100% de maestria sai exatamente 50x, que e o que saia antes.
		//   * A APARENCIA -- nao ha o que migrar. As duas entradas tinham o MESMO `Cabelo`
		//     (`VerdeDoCabeloLendario`), o MESMO `SufixoDoCabelo` (`LSSj`) e o MESMO `Corpo`
		//     (`Musculoso`), porque `lssjbuff.dm:111` repete linha por linha a chamada do `lssj == 3`.
		//     A unica diferenca de tela era a aura (`00ff2a` contra `2bff3a`, dois verdes vizinhos) e
		//     ela morre com a entrada: nao existe -- nem no DM nem aqui -- aura que ande com maestria.
		//   * A PORTA (`PortaBp = PortaSsj4`, `ChaveDoLimiar = "rawssj4at"`) morre junto, e tem que
		//     morrer: nao se cobra segunda porta de BP pra continuar na forma em que ja se esta.
		//
		// ============================ O QUE ISTO MUDA DE VERDADE, E POR QUE ESTA CERTO ============================
		// Esticar a rampa de 40 pra 50 mexe em DOIS numeros alem do topo, e nenhum dos dois e engano:
		//
		//   1. O MEIO DA RAMPA SOBE. Aos 50% de maestria sai 37,5x onde saia 32,5x. E o preco de a
		//      forma ser UMA: uma reta que termina em 50 passa mais alto no meio, e a alternativa
		//      (reta ate 40 e um pulo pra 50 no ultimo ponto) seria o degrau separado outra vez,
		//      escrito com outro nome.
		//   2. A FURIA SOZINHA ALCANCA O TOPO. O `CombateSobeAoMaximo` le `Mult[0]` e `Mult[^1]`,
		//      entao 180 s de luta agora levam a 50x sem maestria nenhuma -- e isso e a REGRA DESTA
		//      LINHA, escrita no cabecalho do bloco Legendary desde o porte: *"a maestria e so um
		//      PISO; quem lutar 180 s seguidos chega ao maximo sem maestria nenhuma. E o que faz o
		//      Legendary ser a linha da FURIA e nao a do treino"*. Quem quebrava a regra era o Full
		//      Power, que ficava fora do alcance da furia por ser outra entrada.
		//
		// E O DRENO FICA O DAQUI (`[0.07, 0.05, 0.035]`, que CAI com a maestria) e nao o `[0.06]`
		// fixo do Full Power. Ele ja dizia a mesma coisa que a fusao diz: dominar a lenda e gastar
		// menos Ki nela. O `0.06` do Full Power era MAIOR que o `0.035` do Legendary dominado -- o
		// jogador pagava mais caro por subir, e nao ha regra no DM que peca isso.
		//
		// PRECEDENTE: o `prodigial_mistico_ascendido` (a entrada `306`) morreu do mesmo jeito e pelo
		// mesmo motivo -- ver `_redeAntiga`, que ja guarda os dois.
		// ====================================================================================================
		// ============================ O NOME DELE E "FULL POWER", E O "LEGENDARY" E DO PRIMAL ============================
		// O dono: *"a forma legendary de um Saiyajin comum nao se chama 'Legendary Super Saiyan' e sim
		// 'Super Saiyan Full Power' ... pra diferencial o lssj do primal pro normal"*.
		//
		// ISTO E UM RENOME DE CAMPO, E NAO UMA DERIVACAO -- e a razao esta no `LinhasAbertas`: as duas
		// escadas lendarias SE EXCLUEM (`primal` pega `LegendaryPrimal`, `else if (p.Legendary)` pega
		// `Legendary`), entao esta entrada so e alcancavel por quem NAO e Primal e a `primal_legendary`
		// (220) so por quem e. A linhagem ja esta na ENTRADA; perguntar de novo pelo perfil na hora de
		// nomear seria escrever um ramo que nunca pode ser falso.
		//
		// O QUE O DONO VIU eram os dois botoes do painel de admin -- que lista o catalogo INTEIRO,
		// ignorando linha -- com o mesmo texto "Legendary Super Saiyajin" em faixas diferentes. Com o
		// renome, o painel distingue os dois sem precisar saber de quem esta olhando.
		//
		// "Saiyajin" e nao "Saiyan" pra casar com as outras 30 entradas do catalogo; a palavra do dono
		// era sobre QUAL nome, nao sobre a grafia.
		// ================================================================================================================
		new() { Id = "legendary", IdRede = 130, Linha = LinhaDeForma.Legendary, Ordem = 30,
				Nome = "Super Saiyajin Full Power", NumeroDm = 3, Intensidade = 4,
				Como = Curva.Rampa, Mult = [25, 50], CombateSobeAoMaximo = true, PisoSobreAnterior = 2,
				Dreno = [0.07, 0.05, 0.035],
				PortaBp = LimiaresPessoais.LssjatInicial, ChaveDoLimiar = "lssjat",
				// ============================ AQUI O VERDE E DO CABELO, E NAO SO DA AURA ============================
				// `HairObject.dm:215`: `updateOverlay(/obj/overlay/hairs/ssj/lssjhair, ussjhair, 0,100,0)`.
				// Sao DUAS coisas na mesma chamada e o port so fazia uma:
				//   * o SPRITE e o `ussjhair` -- o cabelo de USSJ do penteado, nao o de SSJ1. O sufixo
				//     continua `LSSj` porque tres penteados TEM folha propria de Legendary (Broly,
				//     FemBroly e Kale -- `HairChoose.dm:338,340,342`); pra os outros ~59 quem responde e
				//     a heranca `LSSj -> USSj -> SSj` do `CabelosDeForma`, que e o `ussjhair` do DM;
				//   * a TINTA e o `rgb(0,110,0)` que o `EffectStart` do `lssjhair` soma DEPOIS
				//     (`SaiyanObjects.dm:83-87`). Os `(0,100,0)` da chamada sao descartados no caminho
				//     limpo; o verde que chega e o do EffectStart.
				// Era este degrau que "saia o dourado do SSJ", como o dono descreveu.
				// ================================================================================================
				// O CORPO CONTINUA INCHADO -- `lssjbuff.dm:103`, o caso `if(3)`, que troca a folha 2 pela
				// 3 (`'White Male Muscular 2.dmi' -> 'White Male Muscular 3.dmi'`). AQUI E O MESMO
				// SIMBOLO nos dois degraus, e essa e uma divergencia declarada: o dono nomeou TRES
				// arquivos (um por pele), nao dois por degrau. Ver `Client/CorposDeForma.cs`.
				Aura = "2bff3a", Cabelo = VerdeDoCabeloLendario, SufixoDoCabelo = "LSSj",
				Corpo = CorpoDeForma.Musculoso,
				Desc = "A lenda do sangue comum. 25x a 50x -- so aos 100% de maestria ela e "
					 + "mesmo plena, e ate la quem manda nela e a furia." },

		// ---------------------------------------------------------------------------
		// LINHA LEGENDARY PRIMAL -- `Class == "Legendary Primal Saiyan"` (supersaiyanbuff.dm:561)
		// ---------------------------------------------------------------------------
		// Ladder PROPRIO: substitui a Saiyajin inteira, nao se mistura. Todas ganham o bonus de
		// combate de +20% POR CIMA do que a maestria deu -- outra mecanica que a do Legendary comum.
		new() { Id = "primal_c_type", IdRede = 210, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 10,
				Nome = "Super Saiyajin C-Type (Z)", NumeroDm = 1, Intensidade = 2,
				Mult = [3], BonusDeCombate = 0.2, Dreno = [0.025, 0.015, 0],
				PortaBp = PortaSsj1, ChaveDoLimiar = "ssjat",
				// DOURADO PURO, e e o unico degrau da linha Primal que nao tenta ser verde:
				// `HairObject.dm:183` -- `updateOverlay(/obj/overlay/hairs/ssj/ssj1)`, sem argumento
				// nenhum. E o rabo dele cai no `if(container.ssj)` e sai `dada26` por derivacao.
				//
				// ============================ O CORPO INCHADO DA LINHA PRIMAL E DIVERGENCIA ============================
				// **No DM os quatro degraus abaixo do SSJ4 primal NAO incham.** A linha Primal reusa a
				// var `ssj` do original (1 = C-Type, 2 = LSSJ, 3 = LSSJ2, 3.5 = LSSJ3, ver
				// `MasterStudent.dm:197`), e a unica troca de icone do `switch(container.ssj)` esta no
				// `if(1.5)` -- o USSJ. Os casos 1, 2, 3 e 3.5 nao tocam em `container.icon`.
				//
				// Entram por ordem do dono, que nesta tarefa escreveu *"LSSJ (qualquer uma MENOS a
				// lssj4)"* e, no item que exclui, nomeou justamente as PRIMAIS (*"lssj4 (o
				// `primal_legendary4` e derivados) fica de FORA"*) -- ou seja, pra ele a linha Primal
				// esta dentro de "LSSJ". E o resto do arquivo ja trata as duas linhas como uma so
				// (aura, contorno, olho e coladas usam `Legendary or LegendaryPrimal` juntas).
				// ==================================================================================================
				Aura = "9dff7a", SufixoDoCabelo = "SSj", Corpo = CorpoDeForma.Musculoso,
				Desc = "3x fixo. O primeiro degrau da linhagem primal." },

		new() { Id = "primal_legendary", IdRede = 220, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 20,
				Nome = "Legendary Super Saiyajin", NumeroDm = 2, Intensidade = 3,
				Como = Curva.Rampa, Mult = [6, 9], BonusDeCombate = 0.2, PisoSobreAnterior = 2,
				Dreno = [0.045, 0.03, 0.015],
				PortaBp = PortaSsj2, ChaveDoLimiar = "ssj2at",
				// SPRITE DE SSJ1 + VERDE. `HairObject.dm:184` passa o `ssjhair` -- a folha de SSJ1, e nao
				// a de Legendary: a linha Primal reusa a var `ssj` do DM (1 = C-Type, 2 = LSSJ, 3 = LSSJ2)
				// e por isso ela sobe pela arte da escada Saiyajin, pintada. Trocar pra `LSSj` aqui daria
				// a ele o cabelo do OUTRO Legendary. Ver `VerdeDoCabeloLendario` pro porque do verde.
				Aura = "76ff7a", Cabelo = VerdeDoCabeloLendario, SufixoDoCabelo = "SSj",
				Corpo = CorpoDeForma.Musculoso, Desc = "6x a 9x conforme a maestria." },

		new() { Id = "primal_legendary2", IdRede = 230, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 30,
				// RAIOS = 2, e ele foi o primeiro degrau fora da escada Saiyajin a acender faisca (hoje
				// o `primal_legendary3` acompanha). Nao e escolha de estilo: o Legendary Primal reusa a
				// var `ssj` do DM com outra semantica (`MasterStudent.dm:197` -- "ssj 2 = LSSJ,
				// 3 = LSSJ2"), entao este degrau cai no MESMO `if(3)` do `supersaiyanbuff.dm` que o
				// SSJ3. Mesma folha, mesmo volume.
				//
				// A COR DELE MUDOU no acerto do dono: era VERDE (a faisca herdava a aura da linha) e
				// hoje e o mesmo azul do SSJ2/SSJ3 -- ver `Catalogo.CorDosRaios`.
				Nome = "Legendary Super Saiyajin 2", NumeroDm = 3, Intensidade = 3, Raios = 2,
				Como = Curva.Rampa, Mult = [9, 12], BonusDeCombate = 0.2, PisoSobreAnterior = 2,
				Dreno = [0.06, 0.045, 0.03],
				PortaBp = PortaSsj3, ChaveDoLimiar = "ssj3at",
				// `HairObject.dm:185`: `/obj/overlay/hairs/ssj/ssj2` -- o cabelo de SSJ2, verde.
				Aura = "4dff5a", Cabelo = VerdeDoCabeloLendario, SufixoDoCabelo = "SSj2",
				Corpo = CorpoDeForma.Musculoso, Desc = "9x a 12x." },

		// `LSSj3_Primal()`: ssj = 3.5, acima do LSSJ2, com animacao verde propria.
		new() { Id = "primal_legendary3", IdRede = 235, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 35,
				// RAIOS = 2. O `if(3.5)` do DM (`supersaiyanbuff.dm:239-243`) acende UMA folha, o que
				// pela conta literal o poria em 1 -- abaixo do degrau que ele sucede. E o UNICO volume
				// deste catalogo que NAO e o do original, e o porque esta no cabecalho de
				// `FormaDef.Raios`: as duas folhas a mais do `if(3)` sao remendo posterior e nunca
				// chegaram a este galho.
				Raios = 2,
				Nome = "Legendary Super Saiyajin 3", NumeroDm = 3.5, Intensidade = 4,
				Mult = [18], BonusDeCombate = 0.2, PisoSobreAnterior = 2, Dreno = [0.075, 0.05, 0.03],
				PortaBp = PortaSsj3, ChaveDoLimiar = "ssj3at", PedeMaestria = 100,
				// `HairObject.dm:186`: `/obj/overlay/hairs/ssj/ssj3` -- o cabelo LONGO de SSJ3, verde.
				Aura = "2bff3a", Cabelo = VerdeDoCabeloLendario, SufixoDoCabelo = "SSj3",
				Corpo = CorpoDeForma.Musculoso, Desc = "18x fixo." },

		new() { Id = "primal_legendary4", IdRede = 240, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 40,
				Nome = "Legendary Super Saiyajin 4", NumeroDm = 4, Intensidade = 4,
				Como = Curva.Rampa, Mult = [22, 44], BonusDeCombate = 0.2, PisoSobreAnterior = 2,
				Dreno = [0.06],
				PortaBp = PortaSsj4, ChaveDoLimiar = "rawssj4at",
				// MESMA PORTA DO SSJ4 COMUM, e tem que ser: o Legendary Primal chega ao degrau 4
				// pela MESMA fera. Deixar so `PedeFormaDespertada` aqui daria ao Legendary um SSJ4
				// mais barato que o do Saiyajin comum -- e o ladder dele ja e o mais forte.
				PedeLinhagem = Oozaru.LinhagemPrimal, PedeFormaDespertada = "oozaru_dourado",
				PedeMaestriaDe = "oozaru_dourado", PedeMaestria = 100,
				// O VERDE PARA AQUI: `HairObject.dm:187-189` manda os tres degraus 4/5/6 pro
				// `/obj/overlay/hairs/ssj/ssj4` SEM argumento -- o cabelo escuro do SSJ4, sem tinta.
				Aura = "8bff2a", SufixoDoCabelo = "SSJ4", Corpo = CorpoDeForma.Ssj4, Desc = "22x a 44x. Tambem sai do Oozaru Dourado." },

		new() { Id = "primal_legendary4_full_power", IdRede = 250, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 50,
				Nome = "Legendary Super Saiyajin 4 Full Power", NumeroDm = 5, Intensidade = 5,
				Como = Curva.Rampa, Mult = [34, 52], BonusDeCombate = 0.2, PisoSobreAnterior = 2,
				Dreno = [0.07],
				PortaBp = PortaSsj4, ChaveDoLimiar = "rawssj4at",
				PedeLinhagem = Oozaru.LinhagemPrimal, PedeMaestria = 100,
				Aura = "6aff1a", SufixoDoCabelo = "SSJ4", Corpo = CorpoDeForma.Ssj4, Desc = "34x a 52x." },

		new() { Id = "primal_legendary4_limit_breaker", IdRede = 260, Linha = LinhaDeForma.LegendaryPrimal, Ordem = 60,
				Nome = "Legendary Super Saiyajin 4 Limit Breaker", NumeroDm = 6, Intensidade = 5,
				Mult = [60], BonusDeCombate = 0.2, PisoSobreAnterior = 2, Dreno = [0.085],
				PortaBp = PortaSsj4, ChaveDoLimiar = "rawssj4at",
				PedeLinhagem = Oozaru.LinhagemPrimal, PedeMaestria = 100, PedeGodKi = GodkiRoyalePct,
				Aura = "4aff0a", SufixoDoCabelo = "SSJ4", Corpo = CorpoDeForma.Ssj4, Desc = "Forma divina da lenda: 60x fixo." },

		// ---------------------------------------------------------------------------
		// LINHA GOD KI -- godki.dm:344-355. TODAS ABSOLUTAS.
		// ---------------------------------------------------------------------------
		// `Fusion.dm:33` e `base.dm:132` dizem com todas as letras: "God forms vivem em
		// god_form_mult, NAO em ssjBuff". Elas nao empilham com a escada -- substituem o valor.
		//
		// A MAESTRIA DE GOD KI E QUEM ABRE OS DEGRAUS (os "tiers" morreram num rework anterior):
		// 0% = SSG, 33% = Blue/Rose, 50% = Blue Evolution/Rose 2.
		new() { Id = "ssg", IdRede = 300, Linha = LinhaDeForma.GodKi, Ordem = 10,
				Nome = "Super Saiyajin God", NumeroDm = -1, Intensidade = 4,
				Mult = [22], Absoluta = true, Dreno = [0.03],
				PedeGodKi = 0, PedeOrigemUmaDe = OrigemDoKiDivino, PedeClasseUmaDe = ClassesDoKiDivino,
				PedeFormaAtual = [IdBase],   // `if(ssj == 0 && lssj == 0) return 22` -- SSG e o ki divino SEM SSJ
				// ============================ O SSG NAO ERGUE O CABELO: ELE O PINTA ============================
				// SEM SUFIXO E COM TINTA -- `ModoDoCabelo.Tingir`. O port punha `SufixoDoCabelo = "SSj"`,
				// ou seja o cabelo ESPETADO de Super Saiyajin, que o DM nao usa em SSG nenhum: a tinta
				// divina mora no `gdki_me()` do `/obj/overlay/hairs/hair` (`HairObject.dm:62-76`), que e
				// o overlay do penteado NORMAL. O `AddHair()` so chega no `/hairs/hair` quando
				// `!ssj && !lssj` (`:179-180`), e SSG e exatamente isso: ki divino com a escada zerada.
				//
				// E o `ff4d6a` era rosa-avermelhado escolhido por mim. O do arquivo e laranja-tijolo.
				// ==========================================================================================
				Aura = "ff4d6a", Cabelo = VermelhoDoCabeloDivino,
				Desc = "22x. O ki divino sobre a forma BASE -- cabelo e aura vermelhos." },

		new() { Id = "blue", IdRede = 310, Linha = LinhaDeForma.GodKi, Ordem = 20,
				Nome = "Super Saiyajin Blue", NumeroDm = -1, Intensidade = 4,
				Mult = [32], Absoluta = true, Dreno = [0.045],
				PedeGodKi = GodkiBluePct, PedeOrigemUmaDe = OrigemDoKiDivino,
				PedeClasseUmaDe = ClassesDoKiDivino, ProibidoParaClasse = [ClasseRose],
				PedeFormaAtual = ["ssj1"],   // SSJ + ki divino
				// TROCA E TINGE: o cabelo de SSJ1 (`ssjhair`) recebe o azul no `EffectStart` compartilhado
				// do `/obj/overlay/hairs/ssj` (`SaiyanObjects.dm:11-20`). Nao existe folha de cabelo Blue
				// no repo -- no original ele TAMBEM e o SSJ pintado.
				Aura = "3ad2ff", Cabelo = AzulDoCabeloDivino, SufixoDoCabelo = "SSj",
				Desc = "32x. Super Saiyajin com o ki divino aceso. Abre com 33% de maestria divina." },

		// ============================ O NOME MUDOU DE PROPOSITO ============================
		// O DM chama de "Royale" (e `GODKI_ROYALE_PCT` continua com esse nome). O dono pediu
		// "Blue Evolution" nesta versao. **Nao 'conserte' isto de volta**: o define do BYOND fala
		// Royale porque e o codigo de la; o campo Nome fala Evolution porque e o jogo daqui.
		// ==================================================================================
		new() { Id = "blue_evolution", IdRede = 320, Linha = LinhaDeForma.GodKi, Ordem = 30,
				Nome = "Super Saiyajin Blue Evolution", NumeroDm = -1, Intensidade = 5,
				Mult = [56], Absoluta = true, Dreno = [0.06],
				// SO O ELITE, e SO A PARTIR DO GRADE. Regra do dono, e o DM ja dizia o mesmo:
				// "Royale/Rose 2 56x (50%, so Elite/Kaio via USSJ)" -- o USSJ de la e o Grade daqui.
				PedeGodKi = GodkiRoyalePct, PedeOrigemUmaDe = ["Saiyan"], PedeClasseUmaDe = [ClasseElite],
				PedeFormaAtual = ["grade2", "grade3"],
				PedeMaestria = Grade2Pct, PedeMaestriaDe = "ssj1",   // e o que o Grade 2 pede
				// SPRITE DO USSJ (`SaiyanObjects.dm:73-75` -> `icon = ussjhair`, e o azul vem do
				// `EffectStart` do pai), E UM AZUL MAIS FUNDO QUE O DO BLUE.
				//
				// O DM NAO DISTINGUE OS DOIS -- os dois caem no mesmo `rgb(13,73,238)` --, e a diferenca
				// e pedido do dono ("no royale um azul ainda mais escuro"). Ela cabe: o Evolution ja e
				// o degrau de cima em aura (`1c7cff` contra `3ad2ff`), e ate agora o cabelo era o unico
				// desenho em que subir de degrau nao mudava nada.
				//
				// E QUEM FICOU COM O HEXA DO DM FOI ELE, e nao o Blue: o dono mandou o azul fundo pra
				// ca (*"o cabelo atual do blue e pra ser do evolved/royale"*) e deu ao Blue o ciano do
				// Goku SSGSS. Ver `AzulDoCabeloRoyale`.
				Aura = "1c7cff", Cabelo = AzulDoCabeloRoyale, SufixoDoCabelo = "USSj",
				Desc = "56x. Grade 2 com o ki divino aceso. Elite, 50% de maestria divina." },

		// ROSE: a MESMA escada, cor outra. `godki.dm:21` marca `godki_mod` como "this is your Rose
		// variable" e `statsaiyan.dm:157` amarra Rose a uma classe que ganha "Rose no lugar do Blue
		// (godki_mod > 1)". Entao Rose nao e uma forma a mais -- e a variante da classe.
		new() { Id = "rose_ssg", IdRede = 301, Linha = LinhaDeForma.GodKiRose, Ordem = 10,
				Nome = "Super Saiyajin God", NumeroDm = -1, Intensidade = 4,
				Mult = [22], Absoluta = true, Dreno = [0.03],
				PedeGodKi = 0, PedeClasseUmaDe = [ClasseRose], PedeFormaAtual = [IdBase],
				// IDENTICO AO `ssg`, E NO DM ELE NEM EXISTE COMO CASO: `godki_mod` (a variavel do Rose)
				// e lida em dois lugares -- `SaiyanObjects.dm:13` e `:104` -- e os DOIS estao dentro de
				// `if(container.ssj)`. Com `ssj == 0` o Kaio em SSG e um SSG comum: cabelo base vermelho,
				// chama `FieryGod`. O rosa so nasce a partir do SSJ1.
				Aura = "ff4d6a", Cabelo = VermelhoDoCabeloDivino,
				Desc = "22x. O ki divino sobre a forma base. O corpo roubado tambem alcanca o deus." },

		new() { Id = "rose", IdRede = 311, Linha = LinhaDeForma.GodKiRose, Ordem = 20,
				Nome = "Super Saiyajin Rose", NumeroDm = -1, Intensidade = 4,
				Mult = [32], Absoluta = true, Dreno = [0.045],
				PedeGodKi = GodkiBluePct, PedeClasseUmaDe = [ClasseRose], PedeFormaAtual = ["ssj1"],
				// A MESMA RECEITA DO BLUE com o outro hexa -- `SaiyanObjects.dm:13-16`, o ramo
				// `if(container.godki_mod > 1)`. Mesmo sprite, so muda a cor.
				Aura = "ff7ac6", Cabelo = RosaDoCabeloDivino, SufixoDoCabelo = "SSj",
				Desc = "32x. O Blue de quem tem o ki divino corrompido -- rosa em vez de azul." },

		new() { Id = "rose2", IdRede = 321, Linha = LinhaDeForma.GodKiRose, Ordem = 30,
				Nome = "Super Saiyajin Rose 2", NumeroDm = -1, Intensidade = 5,
				Mult = [56], Absoluta = true, Dreno = [0.06],
				PedeGodKi = GodkiRoyalePct, PedeClasseUmaDe = [ClasseRose],
				PedeFormaAtual = ["grade2", "grade3"],
				PedeMaestria = Grade2Pct, PedeMaestriaDe = "ssj1",
				Aura = "ff4db0", Cabelo = RosaDoCabeloDivino, SufixoDoCabelo = "USSj",
				Desc = "56x. O degrau final do ki divino corrompido." },

		// ============================ O MISTICO E UM DOM, NAO UM DEGRAU ============================
		// `godki.dm:349-352` e explicito: "Prodigial NAO tem SSG/Blue: o ki divino flui pelo MISTICO
		// (22x -> 32x aos 33%) e culmina no BEAST (56x, raiva aos 50%)" -- e por isso o
		// SSG/Blue/Evolution carregam `ProibidoParaClasse`.
		//
		// ============================ ERAM DUAS ENTRADAS, E ERAM A MESMA FORMA ============================
		// `prodigial_mistico` (22x) e `prodigial_mistico_ascendido` (32x) eram degraus SEPARADOS da
		// escada -- com nome proprio, cinematica propria e ZERO diferenca visual entre eles. O que
		// mudava era o numero, e ele mudava porque a maestria divina cruzou 33%. Ou seja: nunca
		// foram duas formas, era uma forma com duas fotos.
		//
		// O dono ditou a regra inteira, e ela nao cabia em degrau nenhum:
		//   * o Mistico e concedido pelo RITUAL de um jogador Kaioshin (ver `SoPorConcessao`);
		//   * TODA raca pode receber -- 16x, e por isso nao ha `PedeClasseUmaDe` aqui;
		//   * a linhagem **Prodigial** leva 18x;
		//   * o Prodigial que destrave o KI DIVINO sobe pra 22x -- ele e a unica linhagem que hoje
		//     mistura Mistico com ki divino;
		//   * e dai em diante SOBE GRADUALMENTE ate 32x aos 33% de maestria divina, que e o TETO.
		//
		// A curva mora no campo `EscalaComGodKi` (ver o cabecalho dele pro porque de o `Mult[]` nao
		// dar conta). A entrada `306` sumiu -- a migracao do save esta em `RedeDoSave`.
		//
		// O `PedeGodKi` saiu de `0` pra `-1` (o padrao, "nao pede") e isso e o coracao da mudanca:
		// o Mistico deixou de ser forma divina. As derivacoes de cor, aura e raiva desta linha
		// isolam o Beast por `PedeGodKi >= GodkiRoyalePct` e continuam certas de graca.
		//
		// A primeira versao deste catalogo tinha so o Beast, pendurado depois do Blue Evolution --
		// e o Prodigial nunca chegava nele, porque a porta era uma forma que ele nao pode ter. A
		// bancada pegou subindo a linha inteira.
		// ==========================================================================================
		new() { Id = IdMistico, IdRede = 305, Linha = LinhaDeForma.Mistico, Ordem = 10,
				Nome = "Mistico", NumeroDm = -1, Intensidade = 4,
				Mult = [16], Absoluta = true, Dreno = [0.03],
				SoPorConcessao = true,
				EscalaComGodKi = new(Origens: [ClasseProdigial], SemGodKi: 18,
									 AoDespertar: 22, NoTopo: 32, TopoEm: GodkiBluePct),
				// ============================ O MISTICO NAO TOCA NO CABELO ============================
				// SEM SUFIXO E SEM TINTA. `Mystic.dm:33-36`: `Revert()`, `RemoveHair()` e entao
				// `updateOverlay(/obj/overlay/hairs/hair)` -- o penteado base, na cor do jogador. E o
				// `HairObject.dm:29` e `:41` EXCLUEM a classe Prodigial da tinta divina, com o comentario
				// escrito la ("Prodigial nao tem SSG/Blue: cabelo NATURAL mesmo em God Ki").
				//
				// O port dava a ele o cabelo espetado de SSJ (`SufixoDoCabelo = "SSj"`) pintado de lilas
				// (`c9b8f0`), e nem o sprite nem a cor existem no original. O Mistico se le pela AURA
				// (`FieryGod`) e pela faisca; o corpo continua sendo o do jogador.
				// ====================================================================================
				//
				// ============================ E A FAISCA ESTAVA FALTANDO ============================
				// O comentario acima ja dizia "se le pela aura E PELA FAISCA" e o campo `Raios` estava
				// em ZERO -- o Mistico chegava na tela com aura e mais nada, numa forma que por pedido
				// do dono nao muda cabelo, nem cor, nem corpo. O dono viu: *"Mistico: tudo igual a
				// base, MAS ele TEM os raiozinhos, que estao faltando"*.
				//
				// E O ORIGINAL CONCORDA, com folha propria: `Mystic.dm:37` --
				// `updateOverlay(/obj/overlay/effects/MysticEffect)`, que e `Electric_Mystic.dmi`
				// (`:20-23`). UMA folha, e por isso volume 1 e nao 2 -- a conta esta em
				// `FormaDef.Raios` (o SSJ2 acende uma folha e e 1; o SSJ3 acende tres e e 2).
				//
				// A COR NAO SAI DA AURA aqui, e esta e a unica linha do jogo assim: lilas dentro de
				// chama lilas some. Ver `Catalogo.CorDosRaios`.
				// ==================================================================================
				Aura = "d8c8ff", Raios = 1,
				Desc = "16x. Todo o potencial de uma vez, sem virar deus (18x Prodigial; "
					 + "22x a 32x conforme o ki divino dele amadurece)." },

		// ============================ A PORTA DO BEAST SAO TRES COISAS, E NENHUMA E UM CAMPO NOVO ============================
		// O dono, inteiro: *"o beast pede 50% de maestria de ki divino E EXTREME ANGER"*, e a furia
		// extrema *"so acontece quando um amigo proximo e morto"*. Some a isso o degrau anterior, e
		// a porta e **ser Mistico + 50% de ki divino + furia extrema**. Onde cada terco mora:
		//
		//   1. SER MISTICO -- ja estava, e passou a estar por SUBTRACAO: o degrau anterior desta
		//      linha era o `prodigial_mistico_ascendido` (a entrada 306), e apagar ele fez o
		//      `Catalogo.Anterior(beast)` cair no `mistico` sozinho. O passo 5 do `Avaliar` cobra
		//      `EstaEmOuAcimaDe(mistico)` -- estar NA forma, e nao ter passado por ela. Nao ha
		//      linha de codigo nova pra isso, e a bancada tranca a derivacao com uma checagem.
		//   2. 50% DE KI DIVINO -- o `PedeGodKi` logo abaixo, do jeito que sempre esteve.
		//   3. FURIA EXTREMA -- **derivada**, ver `Catalogo.RaivaExigida` (o braco `Mistico` acima de
		//      50% de ki divino, que hoje e so esta entrada). So no DESPERTAR: depois de liberada a
		//      forma vira toggle, que e o `hasbeast` do `supersaiyan.dm:165`.
		//
		// E A CLASSE CONTINUA SENDO A PRODIGIAL. O Mistico logo acima perdeu o `PedeClasseUmaDe`
		// quando virou dom de ritual pra qualquer raca -- este NAO perdeu, e a diferenca e o pedido
		// do dono: ele reescreveu o gatilho do Beast e nao a dona dele. `godki.dm:349-352` continua
		// dizendo de quem a fera e.
		// ====================================================================================================================
		new() { Id = "beast", IdRede = 330, Linha = LinhaDeForma.Mistico, Ordem = 30,
				Nome = "Beast", NumeroDm = -1, Intensidade = 5,
				Mult = [56], Absoluta = true, Dreno = [0.06],
				PedeGodKi = GodkiRoyalePct, PedeClasseUmaDe = [ClasseProdigial],
				// ============================ AS DUAS CORES DA FERA ESTAVAM TROCADAS ============================
				// No DM o BRANCO e o CABELO e o ROXO-AZUL e a AURA. O catalogo tinha `Aura = "e8e8e8"`
				// (quase branco) e `Cabelo = "d9d9d9"` -- as duas para o mesmo lado, e nenhuma no lugar.
				//
				// CABELO: `Mystic.dm:76-85`. A fonte e o `ssj2hair` do proprio jogador (por isso o sufixo
				// e `SSj2` e nao `SSj`), passado por grayscale e clareado pra branco-gelo. O `b6bac4` e
				// esse branco-gelo azulado, aplicado em MATIZ e nao em soma -- ver
				// `ModoDoCabelo.TrocarERecolorir` pro porque de somar nao bastar.
				//
				// AURA: `Mystic.dm:93-96` -- `rgb(125,90,240)` = `7d5af0`. No DM ela e um overlay de POSE
				// colado no corpo (`god - grey.dmi` tingido) e nao a chama; aqui ela e a chama, que e a
				// camada que o port tem. O contorno oscilante azul/roxo (`AzulDaFera`) ja vinha da mesma
				// ideia e continua onde estava.
				// ==========================================================================================
				//
				// A FAISCA E A MESMA DO MISTICO, E ISSO ESTA ESCRITO NO ORIGINAL: `Mystic.dm:112` --
				// `updateOverlay(/obj/overlay/effects/MysticEffect)` dentro do `Buff()` do Beast, com o
				// comentario na propria linha (*"os raios do Mistico continuam no Beast"*). Nao e um
				// efeito parecido: e o MESMO objeto de overlay. O `DeBuff()` (`:126`) o remove.
				//
				// NAO FOI PEDIDO -- o dono nomeou o Mistico. Entrou porque e a mesma folha do mesmo
				// arquivo, e deixar so o degrau de baixo com faisca faria a linha PERDER efeito ao
				// subir, que na tela le como defeito (o mesmo argumento que ja fixou o volume do
				// `primal_legendary3` -- ver `FormaDef.Raios`).
				Aura = "7d5af0", Cabelo = "b6bac4", SufixoDoCabelo = "SSj2", Raios = 1,
				Desc = "56x. O Prodigial nao vira deus: ele vira FERA. Sai pela raiva." },

		// ---------------------------------------------------------------------------
		// ULTRA INSTINCT -- UltraInstinct.dm:34-35. ABSOLUTAS.
		// ---------------------------------------------------------------------------
		new() { Id = "ui_sign", IdRede = 400, Linha = LinhaDeForma.UltraInstinct, Ordem = 10,
				Nome = "Ultra Instinct -Sign-", NumeroDm = -1, Intensidade = 5,
				Mult = [60], Absoluta = true, Dreno = [0.05],
				PedeGodKi = GodkiUiUePct, PedeProficienciaUi = 1,
				// ============================ O SIGN SO TROCA O CABELO DE QUEM E GOKU ============================
				// `ui_apply_hair()` (`UltraInstinct.dm:296-303`): estilo Goku recebe o
				// `Hair_UltraInstinct.dmi`; **todos os outros ficam com o cabelo BASE, sem tinta nenhuma**
				// (`else updateOverlay(/obj/overlay/hairs/hair)`, com o comentario "Sign: os outros
				// estilos ficam iguais a base"). Por isso ha sufixo e NAO ha tinta: quem tem a arte
				// troca, quem nao tem fica como estava -- que e `ModoDoCabelo.Trocar` sem mais nada.
				// A prata do Sign esta nos OLHOS, e olho e outro canal.
				// ==========================================================================================
				Aura = "c9d8ff", SufixoDoCabelo = SufixoDoUltraInstinto,
				Desc = "60x. O corpo se move sem a mente. Esquiva sozinho." },

		new() { Id = "ui_perfected", IdRede = 410, Linha = LinhaDeForma.UltraInstinct, Ordem = 20,
				Nome = "Perfected Ultra Instinct", NumeroDm = -1, Intensidade = 5,
				Mult = [66], Absoluta = true, Dreno = [0.065], PisoSobreAnterior = 2,
				PedeGodKi = GodkiUiUePct, PedeProficienciaUi = 100,
				// O UNICO `TrocarOuTingir` DO CATALOGO -- ver `ModoDoCabelo.TrocarOuTingir`. Goku ganha o
				// `Hair_MasteredUltraInstinct.dmi` puro; os outros ganham o `/hairs/uisilver`, que e o
				// cabelo base + `rgb(185,190,200)` (`UltraInstinct.dm:288-293`). Um OU o outro: a arte ja
				// e prateada, e somar prata em prata estoura pro branco chapado.
				Aura = "eaf2ff", Cabelo = "b9becb", SufixoDoCabelo = SufixoDoUltraInstintoPerfeito,
				Desc = "66x. O instinto completo: cabelo prateado e nenhum golpe acerta." },

		// ---------------------------------------------------------------------------
		// ULTRA EGO -- UltraEgo.dm. ABSOLUTAS. Caminho EXCLUSIVO com o Ultra Instinct.
		// ---------------------------------------------------------------------------
		new() { Id = "destroyer", IdRede = 500, Linha = LinhaDeForma.UltraEgo, Ordem = 10,
				Nome = "Destroyer Form", NumeroDm = -1, Intensidade = 5,
				Mult = [60], Absoluta = true, Dreno = [0.05],
				PedeGodKi = GodkiUiUePct, PedeEnergiaUe = 20,
				// SEM CABELO PROPRIO E SEM TINTA, e o DM diz o porque na linha: "SO o ULTRA EGO pinta o
				// cabelo de roxo -- a Destroyer Form mantem o cabelo base (a maior diferenca visual entre
				// as duas formas)" (`UltraEgo.dm:395-396`, e o `else` do `ue_apply_hair` em `:400`).
				Aura = "9b4dff",
				Desc = "60x. A aura da Destruicao: quanto mais apanha, mais forte fica." },

		new() { Id = "ultra_ego", IdRede = 510, Linha = LinhaDeForma.UltraEgo, Ordem = 20,
				Nome = "Ultra Ego", NumeroDm = -1, Intensidade = 5,
				Mult = [66], Absoluta = true, Dreno = [0.065], PisoSobreAnterior = 2,
				PedeGodKi = GodkiUiUePct, PedeEnergiaUe = 60,
				// `rgb(140,50,190)` sobre o cabelo BASE -- `/obj/overlay/hairs/uepurple`,
				// `UltraEgo.dm:387-392`. Sem sufixo: o Ultra Ego nao ergue o cabelo, so o pinta.
				Aura = "b96bff", Cabelo = "8c32be",
				Desc = "66x. O ego que se alimenta da propria dor." },

		// ---------------------------------------------------------------------------
		// OOZARU -- linha PARALELA (ver Oozaru.cs). Aqui so pelos dados.
		// ---------------------------------------------------------------------------
		new() { Id = "oozaru", IdRede = 600, Linha = LinhaDeForma.Oozaru, Ordem = 10,
				Nome = "Oozaru", NumeroDm = -1, Intensidade = 3,
				Mult = [1.5], Dreno = [0],
				// SEM TINTA DE CABELO: o macaco nao tem cabelo (o `Apeshit` do DM zera o alfa do overlay,
				// `SaiyanObjects.dm:22-23`). O `5a3a1b` que morava aqui descrevia a PELAGEM, que hoje e do
				// sprite (`Corpo`) -- e com a `Cabelo` valendo TINTA ele passaria a pintar de marrom
				// um cabelo que nem se ve. Era esse mesmo campo que ja tinha deixado o rabo do SSJ marrom.
				Aura = "8a5a2b", Corpo = CorpoDeForma.Oozaru,
				Desc = "1,5x. O macaco gigante. Nao se escolhe: a lua cheia escolhe por voce." },

		// O GATE DO DOURADO MORA NESTES CAMPOS, e quem os le e o `Oozaru.PodeDourado` -- nao o
		// `Avaliar`, que recusa a linha inteira (ver `NaoSeSobePraEla`). Ficam AQUI mesmo assim
		// porque sao dado de forma, e ter a regra em dois lugares e como o Dourado ganharia uma
		// segunda verdade sobre quem pode virar.
		//
		// `PedeFormaDespertada = "ssj1"` virou `PedeMaestria = 100 em "ssj1"` a pedido do dono:
		// "precisa ter pelomenos o ss1 masterizado". Ter DESPERTADO o SSJ1 e um instante; DOMINAR
		// e o unico eixo do jogo que so se paga com tempo dentro da forma -- e e o que faz o
		// Dourado (e o SSJ4 depois dele) ser o fim de uma estrada e nao um atalho da lua cheia.
		//
		// ============================ E FALTAVA O PODER MINIMO ============================
		// O dono pediu o Dourado como "dominar o ssj1 MAIS um poder minimo (tambem requisito
		// pessoal)". So a maestria estava aqui, e sem a porta de BP o Dourado era alcancavel por um
		// Primal de 1,5 milhao de BP que tivesse moido o SSJ1 -- ou seja, no primeiro degrau da
		// escada. O `PortaBp`/`ChaveDoLimiar` sao os mesmos dois campos que o resto do catalogo usa,
		// e o limiar e o `ultrassjat` ja sorteado no nascimento: ver `Catalogo.PortaOozaruDourado`
		// pro porque de ser ele e nao o `rawssj4at`.
		//
		// QUEM LE ESTES DOIS CAMPOS TAMBEM E O `Oozaru.PodeDourado`, e nao o `Avaliar`. Foi preciso
		// passar o BP e os limiares pra la (a assinatura mudou) -- justamente pra o campo nao nascer
		// morto, que e o que este arquivo ja avisa duas vezes acima.
		// ==================================================================================
		new() { Id = "oozaru_dourado", IdRede = 610, Linha = LinhaDeForma.Oozaru, Ordem = 20,
				Nome = "Oozaru Dourado", NumeroDm = -1, Intensidade = 5,
				Mult = [18], Dreno = [0],
				PedeLinhagem = Oozaru.LinhagemPrimal,
				PedeMaestriaDe = "ssj1", PedeMaestria = 100,
				PortaBp = PortaOozaruDourado, ChaveDoLimiar = "ultrassjat",
				Aura = "ffd24a", SufixoDoCabelo = "SSj",
				Corpo = CorpoDeForma.OozaruDourado,
				Desc = "18x (20x pro Legendary Primal). Sair dele e o unico caminho pro SSJ4." },

		// ---------------------------------------------------------------------------
		// FROST DEMON -- `IcerTransform.dm` + `icer.dm` (rework de 2026-07-10 do original)
		//
		// ============================ OS SETE DEGRAUS, E DE ONDE VEM CADA NUMERO ============================
		// Os multiplicadores NAO estao escritos aqui: eles ja moram em `Races.FormasDeFrost.Multiplicador`
		// (o porte do `fd_form_mult`), que a tela de criacao le pra mostrar "multiplica o seu poder por N"
		// no tooltip de cada slot. Duas copias do 10 e do 20 e como a tela passaria a prometer um numero
		// que o combate nao paga -- e a promessa e feita ANTES de o personagem existir, entao o jogador
		// escolheria os corpos por uma escada errada.
		//
		// A `Ordem` E O `fd_form`. Ver o cabecalho de `LinhaDeForma.FrostDemon` pro porque de esta linha
		// nao usar a folga de dez em dez do resto do catalogo.
		//
		// ============================ AS SUPRESSOES SAO `ForaDoTronco`, E ISSO E LITERAL ============================
		// O campo diz "ramo lateral: nao e o degrau anterior de NINGUEM" -- e e exatamente o que as formas
		// 1 a 4 sao. O tronco do Frost Demon e base(5) -> 6 -> 7; as supressoes penduram ABAIXO da base e
		// so o Mutante desce ate elas. Sem esta marca, `Anterior(frost5)` devolveria a 4a Forma e o Frost
		// Demon NORMAL -- que nunca tem supressao nenhuma -- ficaria com a propria forma base recusada por
		// `ForaDeOrdem`, calado. (A bancada `--frostteste` guarda exatamente isso.)
		//
		// ============================ SEM DRENO DE KI, E SEM MEXER NO TANQUE ============================
		// `Dreno` fica no `[0]` de fabrica e nao ha ramo pra esta linha em `TetoDeKi`. As duas ausencias
		// sao do original e estao escritas na cara do arquivo dele: *"Formas NAO mexem mais no pool de Ki
		// (o sistema antigo dava +Ki na supressao e -Ki na final)"* (`IcerTransform.dm:12-13`), e nao ha
		// `*drain` nenhum no `Frost_Demon_Forms` nem no `effector` do `icer.dm`. O custo do Frost Demon
		// nao e folego -- e o CONTROLE (ver o motor do Mutante em `GameServer.Frost.cs`).
		//
		// ============================ A CHAMA CONTINUA SENDO A DELE ============================
		// Nenhuma das sete acende aura propria no DM: `Frost_Demon_Forms` toca `1aura.wav`, troca o icone
		// e acabou. Quem poe overlay e o GOLDEN (`/obj/overlay/icergod`, skill separada) e o DESCONTROLE
		// do Mutante (`fd_menacing_red`). Por isso a linha inteira responde `true` no
		// `Catalogo.ChamaDoJogador` -- ver la. O hexa da `Aura` abaixo sobra pro CONTORNO, que e outro
		// canal (`ParDoContorno`), e por isso ele e frio: um Frost Demon com brilho dourado de Saiyajin
		// e a primeira coisa que o olho estranha.
		// =============================================================================================
		new() { Id = "frost1", IdRede = 700, Linha = LinhaDeForma.FrostDemon, Ordem = 1,
				Nome = Races.FormasDeFrost.Nome(1), NumeroDm = 1, Intensidade = 1,
				Mult = [Races.FormasDeFrost.Multiplicador(1)], ForaDoTronco = true,
				PedeClasseUmaDe = [Races.FormasDeFrost.ClasseMutante],
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "8fd0ff",
				Desc = "0,25x. A casca mais apertada. O Mutante NASCE aqui -- e o BP dele ja e "
					 + "quatro vezes maior por causa disso." },

		new() { Id = "frost2", IdRede = 710, Linha = LinhaDeForma.FrostDemon, Ordem = 2,
				Nome = Races.FormasDeFrost.Nome(2), NumeroDm = 2, Intensidade = 1,
				Mult = [Races.FormasDeFrost.Multiplicador(2)], ForaDoTronco = true,
				PedeClasseUmaDe = [Races.FormasDeFrost.ClasseMutante],
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "8fd0ff",
				Desc = "0,50x. Metade do poder liberado. Estavel pra sempre com 25% de maestria da base." },

		new() { Id = "frost3", IdRede = 720, Linha = LinhaDeForma.FrostDemon, Ordem = 3,
				Nome = Races.FormasDeFrost.Nome(3), NumeroDm = 3, Intensidade = 1,
				Mult = [Races.FormasDeFrost.Multiplicador(3)], ForaDoTronco = true,
				PedeClasseUmaDe = [Races.FormasDeFrost.ClasseMutante],
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "9ad8ff",
				Desc = "0,75x. Estavel pra sempre com 50% de maestria da base." },

		new() { Id = "frost4", IdRede = 730, Linha = LinhaDeForma.FrostDemon, Ordem = 4,
				Nome = Races.FormasDeFrost.Nome(4), NumeroDm = 4, Intensidade = 2,
				Mult = [Races.FormasDeFrost.Multiplicador(4)], ForaDoTronco = true,
				PedeClasseUmaDe = [Races.FormasDeFrost.ClasseMutante],
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "a6e0ff",
				Desc = "0,90x. Quase tudo. Estavel pra sempre com 75% de maestria da base." },

		// ============================ A FORMA BASE E UMA ENTRADA, E NAO O `IdBase` ============================
		// Ver o cabecalho da linha. Ela e o repouso do Frost Demon NORMAL (que nasce nela) e o topo da
		// subida do Mutante -- que so a segura pra sempre com 100% de maestria da base, e e SO nela que
		// essa maestria cresce (`Catalogo.SustentarTreina`).
		//
		// `Intensidade = 2` E NAO 0, e a diferenca nao e enfeite: quem chega aqui pela primeira vez e o
		// Mutante ABRINDO a ultima casca, que e o acontecimento central do personagem dele. Pro normal a
		// cena nunca roda -- ele ja NASCE nesta forma (`Catalogo.PisoDaEscada`), e o repouso nao passa
		// por `Entrar`. Ou seja o degrau tem cena pra quem a merece e e mudo pra quem nao.
		// ==================================================================================================
		new() { Id = "frost5", IdRede = 740, Linha = LinhaDeForma.FrostDemon, Ordem = 5,
				Nome = Races.FormasDeFrost.Nome(5), NumeroDm = 5, Intensidade = 2,
				Mult = [Races.FormasDeFrost.Multiplicador(5)],
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "b6e8ff",
				Desc = "1x. O corpo sem casca nenhuma. Pro Mutante, e aqui que a maestria da base cresce." },

		// ============================ AS DUAS EVOLUCOES NAO SORTEIAM LIMIAR PESSOAL ============================
		// `ChaveDoLimiar` fica VAZIO, e a ausencia e do original: nao ha `RolarIcer` no `statsaiyan.dm`
		// nem em lugar nenhum -- o `FD_FORM6_AT`/`FD_FORM7_AT` sao `#define`, iguais pra todo Frost Demon
		// do servidor. Inventar um sorteio aqui daria a cada um uma porta diferente, o que e bonito e
		// nao e o jogo. Com o campo vazio, `LimiaresPessoais.Porta` devolve 0 e o `Avaliar` cai no
		// `PortaBp` de fabrica, que e o numero certo.
		//
		// E O CORTE DO MESTRE CONTINUA VALENDO, e e assim no DM tambem: `IcerTransform.dm:89` multiplica
		// o proprio `#define` por `MST_HALF`. Ver `Skills.Discipulado.Ensinavel`.
		new() { Id = "frost6", IdRede = 750, Linha = LinhaDeForma.FrostDemon, Ordem = 6,
				Nome = Races.FormasDeFrost.Nome(6), NumeroDm = 6, Intensidade = 4,
				Mult = [Races.FormasDeFrost.Multiplicador(6)],
				PortaBp = Races.FormasDeFrost.BpNecessario(6),
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "c9b0ff",
				Desc = "10x. A primeira evolucao -- o corpo se refaz inteiro." },

		new() { Id = "frost7", IdRede = 760, Linha = LinhaDeForma.FrostDemon, Ordem = 7,
				Nome = Races.FormasDeFrost.Nome(7), NumeroDm = 7, Intensidade = 5,
				Mult = [Races.FormasDeFrost.Multiplicador(7)],
				PortaBp = Races.FormasDeFrost.BpNecessario(7),
				Corpo = CorpoDeForma.FrostEscolhido, Aura = "d8c0ff",
				Desc = "20x. A evolucao final. O DM a chama de Forma Black." },

		// ==================================================================================
		// O SUPER NAMEKUSEIJIN -- `Super_Namek.dm`
		// ==================================================================================
		// ============================ O `Raios` DESTAS CINCO E CONTADO EM **FOLHAS**, COMO O RESTO ============================
		// A regra do campo (ver <see cref="FormaDef.Raios"/>) e literal e a escala vai de 0 a 2: conte
		// quantas folhas de eletricidade o buff do DM veste ENQUANTO a forma esta de pe. Nas cinco a
		// conta e curta:
		//
		//   * `snamek` -> UMA (`overlayList += 'snamek Elec.dmi'`, `Super_Namek.dm:42`);
		//   * `heran1` -> **NENHUMA**. O `obj/buff/MaxPower` nao veste folha de eletricidade alguma; o
		//     que o Heran tem sao os `createLightningmisc` espalhados pela view DURANTE o
		//     `Max_Power()` -- transformacao, e nao aura. Zero aqui e o porte certo, e o preco esta
		//     declarado: como o `Cinematicas.Faisca` deriva deste mesmo campo, a cena dele nasce sem o
		//     raio e fica com o tremor. Subir o campo pra 1 devolveria o raio da cena e daria ao Heran
		//     um crepitar PERMANENTE que o jogo dele nunca teve;
		//   * `heran2` -> UMA (`overlayList += 'Electric_Red.dmi'`, `HeranBuff.dm:212`) -- e e por isso
		//     que a escada dele ganha faisca ao subir, que e o que se ve no original;
		//   * `alien1` e `alien2` -> UMA cada, e e a MESMA (`updateOverlay(.../spc)` nos dois galhos do
		//     switch, `Alien_Transformations.dm:60` e `:66`). O segundo degrau nao acende mais nada.
		//
		// A PRIMEIRA ESCRITA DESTAS ENTRADAS USOU 2/3/5/2/4, tratando o campo como "intensidade
		// dramatica". O `RaiosDaForma` do cliente satura em 2 (`Mathf.Clamp(intensidade, 0, 2)`), entao
		// os tres acima do teto viravam o MESMO valor na tela -- tres formas prometendo volumes
		// diferentes e desenhando o mesmo. Quem pegou foi a `--diagforma` ("3 forca(s) errada(s)"), e o
		// que ela pegou nao foi um numero errado: foi uma ESCALA errada.
		// ==================================================================================================================
		// UMA ENTRADA, E ELA E A LINHA INTEIRA. O `snamek` do DM e booleano e o `Loop` do buff so tem
		// `if(1)`: nao ha segundo degrau pra portar, nao ha maestria (nada escreve maestria de
		// `snamek` no original) e nao ha raiva (o gate e skill + BP, `Super_Namek.dm:10`).
		//
		// O `ChaveDoLimiar` E O PONTO DESTA ENTRADA. `LimiaresPessoais.snamekat` ja era sorteado no
		// nascimento e a chave `"snamekat"` ja estava no `Porta()` desde que aquela classe nasceu --
		// groundwork pago e sem consumidor, anotado por escrito em dois arquivos ("a escada Heran ainda
		// NAO existe no port"). Esta linha e o consumidor.
		//
		// A AURA E VERDE E ELA E MEDIDA, nao escolhida: o buff veste `snamek Elec.dmi`, e o corpo
		// Namekuseijin do jogo e verde. O `blue_effect=1` do buff (`:23`) e o flash de ENTRADA -- o
		// `animate(src, color=rgb(255,255,255))` do `snamek()` --, nao a cor da chama.
		new() { Id = "snamek", IdRede = 800, Linha = LinhaDeForma.Namekuseijin, Ordem = 10,
				Nome = "Super Namekuseijin", NumeroDm = 1, Intensidade = 3, Raios = 1,
				Mult = [SuperNamekMult],
				Dreno = [SuperNamekDreno],
				PortaBp = LimiaresPessoais.SNamekatInicial, ChaveDoLimiar = "snamekat",
				PedeFlag = new FlagDeSkill("snamek"),
				Aura = "6fe36f",
				Desc = "5x, e o tanque de Ki DOBRA. Pede a skill Super Namekuseijin e uns dois "
					 + "milhoes de poder base." },

		// ==================================================================================
		// O HERAN -- `HeranBuff.dm` + `heran.dm`
		// ==================================================================================
		// ============================ DUAS ENTRADAS, E O MULTIPLICADOR VEM DA CLASSE ============================
		// Ver `FormaDef.BaseDaClasse` pro porque de nao serem SEIS (uma por classe por degrau, como o
		// Rose e o Blue). O `Mult` aqui e a CURVA de `heran_form_mult()` e nao o poder: a maestria
		// escolhe o degrau (1x -> 2,016x) e o `BaseDaClasse` diz por quanto ele multiplica.
		//
		// ============================ A MAESTRIA E DEGRAU, E ELA ZERA O DRENO ============================
		// `stepped_mastery_mult` nos dois canais -- multiplicador (`:245-249`) e dreno (`:39` e `:43`)
		// --, e o ultimo degrau do dreno e ZERO. Uma forma Heran dominada nao custa folego nenhum, o
		// que e a recompensa inteira da linha: ela troca poder bruto (1,30x na classe rara) por poder
		// que se sustenta pra sempre.
		//
		// ============================ A RAIVA E DO TRONCO, E ELA E DERIVADA ============================
		// Nenhuma das duas declara raiva: `Catalogo.RaivaExigida` as encontra pelo ARCO DA LINHA. Ver
		// la -- foi esta linha que revelou que a derivacao nao pegava o Heran (o comentario dela
		// PROMETIA que pegaria), e o conserto foi na derivacao e nao numa marca por entrada.
		// ====================================================================================================
		new() { Id = "heran1", IdRede = 810, Linha = LinhaDeForma.Heran, Ordem = 10,
				Nome = "Max Power", NumeroDm = 1, Intensidade = 3, Raios = 0,
				Mult = CurvaDoHeran, BaseDaClasse = HeranBasePorClasse,
				Dreno = HeranDreno1,
				PortaBp = SsjatInicial, ChaveDoLimiar = "ssjat",
				Aura = "ffd24a",
				Desc = "O poder maximo do corpo de Hera. O multiplicador sai da CLASSE (Omega 1,30x, "
					 + "Epsilon 2,4x, Low-Class 3x) e a maestria o leva a 2,016x do proprio." },

		new() { Id = "heran2", IdRede = 820, Linha = LinhaDeForma.Heran, Ordem = 20,
				Nome = "True Max Power", NumeroDm = 2, Intensidade = 4, Raios = 1,
				Mult = CurvaDoHeran, BaseDaClasse = Heran2BasePorClasse,
				Dreno = HeranDreno2,
				PortaBp = Ssj2atInicial / HeranGateMult2, ChaveDoLimiar = "ssj2at_heran",
				// `Electric_Red.dmi` (`HeranBuff.dm:212`) -- a unica forma nao-Legendary do jogo cujas
				// faiscas o DM pinta de VERMELHO. Como `CorDosRaios` cai no `d.Aura` desta linha, a cor
				// da chama E a cor da faisca, e as duas tem que ser esta.
				Aura = "ff4a4a",
				Desc = "O poder VERDADEIRO -- faiscas vermelhas. Base da classe (Omega 2x, Epsilon 3x, "
					 + "Low-Class 4x), e a maestria a leva a 2,016x do proprio." },

		// ==================================================================================
		// O ALIEN -- `Alien_Transformations.dm`
		// ==================================================================================
		// A LINHA MAIS SIMPLES DO CATALOGO, e ela e simples porque o original tambem e: dois numeros,
		// duas portas de BP, uma skill comprada. Sem maestria (nada a escreve), sem raiva (o
		// `Alien_Trans()` nao olha `Emotion`), sem limiar pessoal (nao ha `RolarAlien`) e sem corpo ou
		// cabelo proprio -- o unico visual e a faisca `spc` (`:60`, `:66`).
		//
		// O `PedeFlag` COBRA DOIS VALORES DA MESMA FLAG e e literal do DM: a skill escreve
		// `hasayyform = 2`, a 1a forma testa `if(hasayyform)` e a 2a testa `hasayyform == 2`. Hoje o
		// unico jeito de ter a flag e comprando a skill, entao as duas abrem juntas -- mas o degrau
		// esta no dado, e um `hasayyform = 1` vindo de outro lugar (o DM tem esse costume) ja daria a
		// 1a forma e nao a 2a, sozinho.
		new() { Id = "alien1", IdRede = 830, Linha = LinhaDeForma.Alien, Ordem = 10,
				Nome = "Forma Alien", NumeroDm = 1, Intensidade = 2, Raios = 1,
				Mult = [AlienMult1],
				Dreno = [AlienDreno1],
				PortaBp = PortaAlien1,
				PedeFlag = new FlagDeSkill("hasayyform"),
				Aura = "b48cff",
				Desc = "2x. O pico da propria especie. Pede a skill Alien Transformation e um milhao "
					 + "de poder base." },

		new() { Id = "alien2", IdRede = 840, Linha = LinhaDeForma.Alien, Ordem = 20,
				Nome = "Forma Alien Final", NumeroDm = 2, Intensidade = 3, Raios = 1,
				Mult = [AlienMult2],
				Dreno = [AlienDreno2],
				PortaBp = PortaAlien2,
				PedeFlag = new FlagDeSkill("hasayyform", 2),
				Aura = "c8a0ff",
				Desc = "4x. O segundo e ultimo degrau da especie -- dez milhoes de poder base." },

		// ==================================================================================
		// O BIO-ANDROIDE -- `CellFormBuff.dm`
		// ==================================================================================
		// UMA ENTRADA, E ELA E A LINHA INTEIRA -- ver o cabecalho de `LinhaDeForma.BioAndroide` pro
		// porque de larva/imperfeito/semi/perfeito NAO estarem aqui (sao `bio_stage`, estado
		// permanente que mexe no BP BASE, e nao buff).
		//
		// AS TRES PORTAS SAO AS TRES METADES DO `if` do DM, uma por campo:
		//   `cell3 == 1 && form3cantrevert`  -> `PedeEstagioBio = Perfeito` (o degrau 4 ja e as duas)
		//   `BP >= cell4at / cell3mult`      -> `PortaSuperPerfeito`
		//   `!ssj`                           -> `ProibidoComFormaAtual`, a escada Saiyajin inteira
		//
		// A LISTA DE PROIBIDAS E ESCRITA E NAO DERIVADA DA LINHA, e vale saber por que: o `Avaliar`
		// le este campo nos DOIS sentidos (ver o passo 4b), e derivar "toda a linha Saiyajin"
		// incluiria a base -- ou seja, um bio na Super Perfeita nao poderia DESCER. A base tem saida
		// propria e explicita no topo do `Avaliar` (`alvo == IdBase` sempre pode), mas contar com
		// isso pra sempre seria contar com a ordem de duas guardas em arquivos diferentes.
		//
		// O DRENO E DE UM DEGRAU SO porque o bio nao tem maestria nesta forma: nada no original
		// escreve maestria de `cell4`, entao nao ha o que aliviar -- ela custa 1% do Ki por segundo
		// do primeiro ao ultimo dia. E o unico estado do bio que ACABA sozinho.
		//
		// A AURA E VERDE-BRANCA e ela e medida, nao escolhida: o buff nao troca folha de aura
		// nenhuma; o que ele veste sao cabelo dourado e ELETRICIDADE (`CellFormBuff.dm:31-40`). O
		// tom sai da carapaca do proprio corpo -- os quatro sprites do bio sao verdes --, e os raios
		// caem no azul do `CorDosRaios` por nao serem de nenhuma linha nomeada la.
		new() { Id = "super_perfect", IdRede = 850, Linha = LinhaDeForma.BioAndroide, Ordem = 10,
				Nome = "Super Perfeito", NumeroDm = 1, Intensidade = 5, Raios = 2,
				Mult = [SuperPerfeitoMult],
				Dreno = [SuperPerfeitoDreno],
				PortaBp = PortaSuperPerfeito,
				PedeEstagioBio = Races.BioAndroids.Perfeito,
				ProibidoComFormaAtual =
					["ssj1", "grade2", "grade3", "ssj2", "ssj3", "ssj4",
					 "ssj4_full_power", "ssj4_limit_breaker"],
				Corpo = CorpoDeForma.BioSuperPerfeito,
				Aura = "9fe8a8",
				Desc = "8x, e o corpo se enche de eletricidade. Exige a FORMA PERFEITA e 750 milhoes "
					 + "de poder base -- e nao entra por cima de nenhum Super Saiyajin. Drena 1% do "
					 + "Ki por segundo e cai sozinha quando o folego acaba." },
	];

	// ==================================================================================
	// AS CONSULTAS
	// ==================================================================================

	private static readonly Dictionary<string, FormaDef> _porId =
		Todas.ToDictionary(d => d.Id, StringComparer.Ordinal);

	private static readonly Dictionary<ushort, FormaDef> _porRede =
		Todas.ToDictionary(d => d.IdRede);

	public static FormaDef? Def(string? id) =>
		id != null && _porId.TryGetValue(id, out FormaDef? d) ? d : null;

	public static FormaDef? PorRede(ushort id) =>
		_porRede.TryGetValue(id, out FormaDef? d) ? d : null;

	/// <summary>O numero de rede de um id. 0 (base) quando nao existe.</summary>
	public static ushort Rede(string? id) => Def(id)?.IdRede ?? 0;

	/// <summary>
	/// FORMAS QUE SUMIRAM DO CATALOGO E PRA ONDE FORAM. So o SAVE le isto.
	///
	/// ============================ POR QUE UM NUMERO APAGADO NAO PODE SO SUMIR ============================
	/// O disco guarda forma por <see cref="FormaDef.IdRede"/> em tres lugares -- `FormasDespertadas`,
	/// `FormasEstreadas` e as chaves de `Maestrias` -- e os tres IGNORAM em silencio um numero que
	/// nao casa com entrada nenhuma (`Maestrias.DoSave` diz isso na cara). Silencio e o certo pra
	/// quem carrega um save mais NOVO que o binario; e o errado pra quem carrega um save mais VELHO:
	/// o `306` (Mistico Divino Ascendido) sairia da conta e o jogador perderia, sem aviso, tanto a
	/// forma liberada quanto as horas de maestria que pagou por ela.
	///
	/// E o `Atual` NAO precisa de migracao porque ele nao e salvo -- "a forma nao atravessa o
	/// logout", `GameServer.cs`. Quem sai Mistico volta na base, e a base sempre existiu.
	/// ================================================================================================
	/// </summary>
	private static readonly Dictionary<ushort, ushort> _redeAntiga = new()
	{
		// 306 = `prodigial_mistico_ascendido`. Ele e o MESMO Mistico com o ki divino a 33%, e virou
		// um ponto da curva do 305 em vez de uma entrada -- ver o bloco do Mistico nas entradas.
		[306] = 305,

		// 140 = `legendary_full_power`. Ele e o MESMO `legendary` (o 130) com a maestria a 100%, e
		// virou o FIM da rampa dele em vez de uma entrada -- ver o bloco do Legendary nas entradas.
		//
		// AQUI A MIGRACAO PAGA POR TRES COISAS DE UMA VEZ, e as tres seriam perda silenciosa:
		//   * `FormasDespertadas` -- quem ja tinha o Full Power liberado apenas manteria o
		//     `legendary`, que ele obviamente ja tinha. Sem custo, mas passa pela mesma porta;
		//   * `FormasEstreadas` -- **esta e a que morde**. Sem a linha, o `140` sumiria e o
		//     `legendary` seguiria estreado do jeito certo; COM ela, o `130` fica estreado por dois
		//     caminhos e o `HashSet` engole a repeticao. E o que impede a cinematica do Legendary de
		//     tocar de novo -- o defeito que o cabecalho do `RedesDoSave` descreve;
		//   * `Maestrias` -- e a mais cara. As horas pagas NO Full Power (chave 140) e as pagas no
		//     Legendary (chave 130) chegam como a mesma forma, e o `DoSave` fica com a MAIOR das
		//     duas. Sem isto, quem masterizou o Full Power perderia essas horas por sorteio de ordem
		//     de dicionario -- e a maestria do 130 e agora justamente o que vale os 50x.
		[140] = 130,
	};

	/// <summary>
	/// O numero de HOJE de uma forma que o save guardou ontem. Numero que nunca mudou volta igual.
	///
	/// Quando duas formas viram uma, DUAS chaves do save apontam pra mesma -- e por isso quem
	/// carrega maestria tem que ficar com a MAIOR das duas e nao com a ultima lida.
	/// </summary>
	public static ushort RedeDoSave(ushort rede) => _redeAntiga.GetValueOrDefault(rede, rede);

	/// <summary>
	/// O CONJUNTO INTEIRO TRADUZIDO -- e o que a carga do save chama pras DUAS listas de forma
	/// (`FormasDespertadas` e `FormasEstreadas`, `GameServer.cs`).
	///
	/// ============================ POR QUE ISTO E UMA FUNCAO E NAO DUAS EXPRESSOES ============================
	/// A conta morava escrita duas vezes dentro do `Login`, uma por lista. Duas copias da mesma
	/// migracao e onde a PROXIMA fusao de formas passa a valer so pra metade: o personagem voltaria
	/// com a forma liberada e a **estreia dela devendo de novo** -- a cinematica tocando uma segunda
	/// vez, meses depois, sem nada na tela explicando.
	///
	/// E e tambem o que torna a migracao testavel pelo MESMO caminho do jogo: uma bancada que
	/// refizesse o `Select(...RedeDoSave...)` provaria a conta da bancada.
	/// ====================================================================================================
	/// </summary>
	public static HashSet<int> RedesDoSave(IEnumerable<int>? redes) =>
		// O `HashSet` ja engole a repeticao, e ela E esperada: quando duas entradas viram uma, o save
		// traz os DOIS numeros e os dois chegam aqui como o mesmo.
		redes == null ? [] : [.. redes.Select(n => (int)RedeDoSave((ushort)n))];

	/// <summary>
	/// O DEGRAU ANTERIOR DESTA FORMA -- derivado, nunca escrito.
	///
	/// E o coracao do "acrescentar estagio sem dor": a entrada nova nao precisa dizer de onde vem
	/// nem avisar a de baixo que agora tem alguem acima. A ordem e a resposta.
	/// </summary>
	public static FormaDef? Anterior(FormaDef d)
	{
		FormaDef? melhor = null;
		foreach (FormaDef o in Todas)
		{
			if (o.Linha != d.Linha || o.Ordem >= d.Ordem) continue;
			if (o.ForaDoTronco) continue;   // ramo lateral nao e o anterior de ninguem -- ver ForaDoTronco
			if (melhor == null || o.Ordem > melhor.Ordem) melhor = o;
		}
		return melhor;
	}

	/// <summary>Id do degrau anterior, ou "base" quando esta e a primeira da linha.</summary>
	public static string IdAnterior(FormaDef d) => Anterior(d)?.Id ?? IdBase;

	/// <summary>
	/// ============================ ONDE ESTE CORPO **DESCANSA** ============================
	/// Devolve a entrada em que o personagem fica quando nao esta transformado, ou NULO quando esse
	/// lugar e o <see cref="IdBase"/> -- que e o caso de todo mundo menos o Frost Demon.
	///
	/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
	/// Ate aqui "nao transformado" e "base" eram a mesma coisa, e eram porque toda forma do jogo
	/// valia MAIS que 1x: quem nao esta em nenhuma esta em 1x, e 1x e a base. O Frost Demon quebra
	/// isso pelos dois lados de uma vez -- a forma de repouso dele e um SPRITE PROPRIO (o corpo que
	/// ele escolheu na criacao, e nao o corpo generico da raca), e a do Mutante ainda vale **0,25x**,
	/// porque ele nasce lacrado dentro da primeira supressao com um BP de fabrica quadruplicado.
	///
	/// Cair no `IdBase` daria ao Mutante 1x sobre esse BP quadruplicado -- quatro vezes o poder que
	/// ele deve ter, calado, desde o primeiro segundo do personagem.
	///
	/// ============================ E ELA E DERIVADA, NAO UM CAMPO `EhORepouso` ============================
	/// A pergunta e "qual e o degrau mais FRACO que este personagem alcanca de graca?", e as duas
	/// metades ja estao no dado:
	///
	///   * **de graca** -- sem porta de BP, sem maestria, sem raiva, sem forma despertada, sem
	///     concessao, sem porta divina, e com a linha/classe/linhagem batendo. Ninguem "conquista"
	///     um repouso;
	///   * **mais fraco que a base, ou a propria base** (`Mult[0] <= 1`) -- e o que separa um repouso
	///     de um degrau. Toda transformacao do jogo vale mais que 1x; o que vale menos e casca.
	///
	/// Com isso o Saiyajin cai no proprio `IdBase` (`Mult` 1, `Ordem` 0, sem gate nenhum), o Frost
	/// Demon normal cai na forma 5 e o Mutante na forma 1 -- **sem uma linha citando raca nenhuma**, e
	/// a proxima raca que tiver casca ja nasce certa aqui.
	///
	/// O `null` como resposta de "e a base" nao e desleixo: o `IdBase` E uma entrada e seria devolvido
	/// por esta varredura de qualquer jeito, mas quem chama precisa distinguir "descansa na base"
	/// (nao ha corpo proprio, nao ha multiplicador, `NaBase` verdadeiro) de "descansa numa forma".
	/// Devolver a entrada da base faria o chamador escrever `Atual = "base"` -- que e o mesmo -- e
	/// perder a chance de dizer isso numa comparacao so.
	/// ==================================================================================================
	/// </summary>
	public static FormaDef? PisoDaEscada(PerfilDeFormas p)
	{
		HashSet<LinhaDeForma> abertas = LinhasAbertas(p);
		FormaDef? melhor = null;

		foreach (FormaDef d in Todas)
		{
			if (d.Id == IdBase) continue;                       // a base ja e a resposta padrao
			if (!abertas.Contains(d.Linha)) continue;
			if (!PodeSerRepouso(d)) continue;

			// LINHAGEM, CLASSE E ORIGEM -- as mesmas tres perguntas do passo 2 do `Avaliar`. Sao elas
			// que separam o Mutante (que tem as quatro supressoes) do Frost Demon normal (que nao tem
			// nenhuma e portanto descansa na forma 5).
			if (d.PedeLinhagem.Length > 0
				&& !string.Equals(p.Linhagem, d.PedeLinhagem, StringComparison.OrdinalIgnoreCase)) continue;
			if (d.PedeClasseUmaDe.Length > 0 && !Alguma(d.PedeClasseUmaDe, p.Classe)) continue;
			if (d.PedeOrigemUmaDe.Length > 0
				&& !Alguma(d.PedeOrigemUmaDe, p.Linhagem) && !Alguma(d.PedeOrigemUmaDe, p.Classe)) continue;
			if (d.ProibidoParaClasse.Any(c => string.Equals(p.Classe, c, StringComparison.OrdinalIgnoreCase)))
				continue;

			if (melhor == null || d.Ordem < melhor.Ordem) melhor = d;
		}
		return melhor;
	}

	/// <summary>Id do repouso deste personagem -- <see cref="IdBase"/> quando ele nao tem forma de repouso.</summary>
	public static string IdDoPiso(PerfilDeFormas p) => PisoDaEscada(p)?.Id ?? IdBase;

	/// <summary>
	/// ============================ ESTE DEGRAU E UM **REPOUSO**, E NAO UMA CONQUISTA? ============================
	/// A metade de <see cref="PisoDaEscada"/> que NAO depende de quem esta perguntando: "este degrau
	/// nao custa nada e nao e mais forte que a base".
	///
	/// Ela e publica por um motivo especifico, e o motivo e uma bancada. A `RaivaBench` varre o
	/// catalogo cobrando que **toda entrada declare uma porta** -- e a razao dela e boa e ja pegou um
	/// defeito real neste projeto (a entrada `oozaru` sem porta nenhuma virou o degrau mais forte
	/// disponivel pra quem nem tinha SSJ1). A forma base do Frost Demon cai naquela varredura como um
	/// "almoco de graca", e ela nao e: ela e onde o corpo dele simplesmente esta.
	///
	/// A saida NAO podia ser acrescentar `frost5` na lista de isencoes escritas a mao da bancada --
	/// aquela lista existe pra que uma forma REALMENTE gratuita apareca como falha, e uma isencao por
	/// id nao distingue "e repouso" de "esqueceram a porta". Com a pergunta em forma de dado, a
	/// bancada passa a aceitar exatamente a familia certa: **degrau que vale 1x ou menos e nao cobra
	/// nada**. Nenhuma transformacao do jogo cabe nessa descricao, e a 8a forma de Frost Demon cabe.
	///
	/// A CLASSE/LINHAGEM FICA DE FORA daqui de proposito: elas nao dizem se o degrau e repouso, dizem
	/// de QUEM ele e. Quem cruza as duas coisas e o <see cref="PisoDaEscada"/>.
	/// ========================================================================================================
	/// </summary>
	public static bool PodeSerRepouso(FormaDef d)
	{
		if (d.Id == IdBase) return false;            // a base e a resposta padrao, nao um candidato
		if (NaoSeSobePraEla(d)) return false;        // o Oozaru nao e repouso de ninguem
		if (d.SoPorConcessao) return false;
		if (d.Mult.Length == 0 || d.Mult[0] > 1) return false;   // transformacao, e nao casca

		// CUSTA ALGUMA COISA? Entao nao e repouso. A lista e a mesma do `EstadoDeForma.Avaliar`,
		// campo a campo -- e ela e verbosa de proposito: um gate novo que entre no `Avaliar` e nao
		// entre aqui faria uma forma CARA virar o repouso de alguem, o que e um jeito silencioso de
		// dar poder de graca.
		return d.PortaBp <= 0 && d.PedeMaestria <= 0 && d.PedeGodKi < 0
			&& d.PedeEnergiaUe <= 0 && d.PedeProficienciaUi <= 0
			&& d.PedeFormaDespertada.Length == 0 && d.PedeFormaAtual.Length == 0
			&& d.PedeFlag == null            // forma comprada NAO e repouso -- ver FormaDef.PedeFlag
			&& RaivaExigida(d) == NivelDeRaiva.Nenhuma;
	}

	private static bool Alguma(string[] lista, string valor) =>
		lista.Any(x => string.Equals(x, valor, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// ============================ O DEGRAU IMEDIATAMENTE ABAIXO -- E ELE NAO E O `Anterior` ============================
	/// `Anterior` responde "de onde se SOBE pra ca" e por isso pula os ramos laterais. Este responde
	/// "pra onde se DESCE daqui", e ai o ramo lateral e justamente o destino: a 4a Forma do Frost
	/// Demon e `ForaDoTronco` (ela nao e o anterior de ninguem) e mesmo assim e exatamente onde o
	/// Mutante cai quando recua da forma base.
	///
	/// E o `revertIcer()` do original (`IcerTransform.dm:116-127`), que faz `fd_form--` -- **um degrau
	/// por vez**, e nao um salto ate o chao. Ver `GameServer.Formas.Transformar` pra a regra que
	/// decide quando usar isto e quando ir direto ao <see cref="PisoDaEscada"/>.
	/// ================================================================================================================
	/// </summary>
	public static FormaDef? DegrauAbaixo(FormaDef d)
	{
		FormaDef? melhor = null;
		foreach (FormaDef o in Todas)
		{
			if (o.Linha != d.Linha || o.Ordem >= d.Ordem || o.Id == IdBase) continue;
			if (melhor == null || o.Ordem > melhor.Ordem) melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// O `fd_form` DESTA ENTRADA -- 1 a 7 -- ou ZERO quando ela nao e do Frost Demon.
	///
	/// Uma linha, mas com nome, porque ela e a ponte entre o catalogo e a lista de corpos que o
	/// jogador escolheu na criacao: quem desenha (`Jandirus.Client.CorposDeForma`) precisa do numero
	/// do degrau pra achar o slot, e ler `d.Ordem` na cara seria o cliente sabendo que NESTA linha a
	/// ordem tem esse significado -- que e o tipo de conhecimento que envelhece longe de quem o criou.
	/// </summary>
	public static int DegrauDoFrost(FormaDef? d) =>
		d is { Linha: LinhaDeForma.FrostDemon } ? d.Ordem : 0;

	/// <summary>O id da forma BASE do Frost Demon -- o degrau 5, onde a maestria da raca inteira mora.</summary>
	public static readonly string IdDaBaseDoFrost =
		Todas.First(d => d.Linha == LinhaDeForma.FrostDemon
					  && d.Ordem == Races.FormasDeFrost.Base).Id;

	/// <summary>
	/// ============================ EM QUE CHAVE A MAESTRIA DESTA FORMA E GUARDADA ============================
	/// Pra 31 das 38 entradas a resposta e "no proprio id", e nao ha nada a dizer. A linha do Frost
	/// Demon e a excecao, e ela e do original: la NAO EXISTE maestria por forma -- existe **uma so**,
	/// o `fd_base_mastery` (`IcerTransform.dm:27`), e ela mede uma coisa que nao e "quanto voce domina
	/// a 2a Evolucao": mede quanto do PROPRIO CORPO o Frost Demon ja consegue destrancar. E a mesma
	/// barra que decide ate que casca ele se segura pra sempre (`FormasDeFrost.DegrauEstavel`).
	///
	/// Guardar sete numeros e depois perguntar "qual deles e o `fd_base_mastery`?" seria inventar
	/// seis campos pra jogar fora, e o pior: o degrau em que a barra cresce (5 ou acima) nao e o
	/// degrau em que ela e COBRADA (todos), entao os seis registros divergiriam do que o jogo le.
	///
	/// **E ISTO NAO E UM `if` POR ID** -- e uma propriedade da LINHA, com o degrau saindo do dado. A
	/// oitava forma de Frost Demon que alguem acrescentar ja compartilha a mesma barra sozinha.
	/// ====================================================================================================
	/// </summary>
	public static string ChaveDaMaestria(string? id) =>
		Def(id) is { Linha: LinhaDeForma.FrostDemon } ? IdDaBaseDoFrost : id ?? IdBase;

	/// <summary>
	/// ============================ SUSTENTAR **ESTA** FORMA TREINA ALGUMA COISA? ============================
	/// "Maestria so cresce dentro da forma -- sustentar a transformacao E o treino dela" e a regra do
	/// port desde o primeiro dia, e ela vale pra tudo que e transformacao. As SUPRESSOES do Frost
	/// Demon nao sao: elas sao o corpo se FECHANDO, e o original diz isso numa linha --
	/// `icer.dm:45`, `if(S.fd_form >= 5 && ...)`: a barra so anda da forma base pra cima.
	///
	/// Faz sentido literal no jogo dele: o que o Mutante esta aprendendo e a segurar o proprio poder
	/// solto. Recolher a casca e o contrario disso -- e o descanso, e por isso e tambem onde a
	/// liberacao se recupera e onde a bateria de Ki carrega.
	///
	/// Derivado de (linha, ordem) e nao de um `bool TreinaSozinha` no catalogo: um campo aqui falharia
	/// CALADO -- a forma nova nasceria com o padrao e ninguem notaria por meses.
	/// ==================================================================================================
	/// </summary>
	public static bool SustentarTreina(FormaDef? d) =>
		d != null && d.Id != IdBase
		&& (d.Linha != LinhaDeForma.FrostDemon || d.Ordem >= Races.FormasDeFrost.Base);

	/// <summary>
	/// QUAIS LINHAS ESTE PERSONAGEM PODE PERCORRER.
	///
	/// ============================ AS LINHAS SE EXCLUEM ============================
	/// No DM isso era uma cascata de `if` no topo do `ssj_effective_mult()`: quem tem
	/// `FutureLineage` NAO usa o SSJ1 comum, quem e `Legendary Primal Saiyan` NAO usa a escada
	/// Saiyajin, quem e `legendary` usa a `lssj`. Aqui e uma funcao so, e as duas pontas (cliente
	/// desenhando a aba Formas e servidor decidindo) fazem a MESMA pergunta.
	/// ==============================================================================
	/// </summary>
	public static HashSet<LinhaDeForma> LinhasAbertas(PerfilDeFormas p)
	{
		var abertas = new HashSet<LinhaDeForma>();

		bool primal = string.Equals(p.Classe, "Legendary Primal Saiyan", StringComparison.OrdinalIgnoreCase);
		if (primal) abertas.Add(LinhaDeForma.LegendaryPrimal);
		else if (p.Legendary) abertas.Add(LinhaDeForma.Legendary);
		else if (p.Futuro) { abertas.Add(LinhaDeForma.Futuro); abertas.Add(LinhaDeForma.Saiyajin); }

		// ============================ O FROST DEMON TROCA A ESCADA SAIYAJIN PELA DELE ============================
		// Ele entra na MESMA cascata das outras (Primal, Legendary, Futuro) e nao como um `Add` a parte,
		// e a exclusao e o ponto: sem ela, o Frost Demon teria as duas escadas ao mesmo tempo. Isso nao
		// e teoria -- o `else` de baixo abre a linha Saiyajin pra QUEM QUER QUE SEJA (a recusa
		// `RecusaForma.NaoEhSaiyajin` esta declarada no `EstadoDeForma` e nao tem uma unica referencia),
		// entao um Frost Demon com BP acima do `ssjat` apertaria C e viraria Super Saiyajin, de cabelo
		// dourado, por cima do corpo de Freeza.
		//
		// **O BURACO ERA MAIOR DO QUE ESTE RAMO, E ELE FOI FECHADO NESTA SESSAO** -- ver o bloco do
		// `else` la embaixo.
		// ====================================================================================================
		else if (Races.FormasDeFrost.EhFrost(p.Raca)) abertas.Add(LinhaDeForma.FrostDemon);

		// AS TRES LINHAS RACIAIS NOVAS entram na MESMA cascata, pela mesma razao do Frost Demon: elas
		// SUBSTITUEM a escada Saiyajin, nao se somam a ela. Um Namekuseijin com as duas teria um cabelo
		// dourado brotando de uma cabeca que nao tem cabelo.
		else if (EhDaRaca(p.Raca, RacaNamekuseijin)) abertas.Add(LinhaDeForma.Namekuseijin);
		else if (EhDaRaca(p.Raca, RacaHeran)) abertas.Add(LinhaDeForma.Heran);
		else if (EhDaRaca(p.Raca, RacaAlien)) abertas.Add(LinhaDeForma.Alien);

		// O BIO-ANDROIDE entra na MESMA cascata, e nao por simetria: a Super Perfeita e a escada
		// Saiyajin brigam pela mesma vaga no original (as duas escrevem `mob/var/ssj`). Aqui elas
		// nao brigam -- quem tem DNA Saiyajin ganha a Saiyajin ABAIXO, por `CanSsj`, e o bloqueio
		// mutuo entre as duas formas e dito por dado (`ProibidoComFormaAtual`).
		//
		// `Races.BioAndroids.EhBio` E NAO `EhDaRaca` com um literal: a raca tem duas grafias vivas no
		// projeto (`"BioAndroid"` no `races.json`, `"Bio-Android"` nos dados extraidos do DM), e foi
		// exatamente essa dobra que deixou o ramo do Zenkai escrito e inalcancavel por meses.
		else if (Races.BioAndroids.EhBio(p.Raca)) abertas.Add(LinhaDeForma.BioAndroide);

		// ============================ O `else` DEIXOU DE ENTREGAR A ESCADA SAIYAJIN A QUALQUER UM ============================
		// Ele era `else abertas.Add(Saiyajin)` -- sem olhar raca nenhuma. Quem nao fosse Primal,
		// Legendary, Futuro nem Frost Demon recebia a escada inteira do Super Saiyajin: o Namekuseijin,
		// o Humano, o Majin, o Bio-Androide, o Gray, o Makyo, o Demonio, o Android, o Kai. A recusa que
		// existiria pra isso -- `RecusaForma.NaoEhSaiyajin` -- esta declarada desde o primeiro dia e
		// **nunca teve uma unica referencia**, o que e o retrato do buraco: a regra estava escrita no
		// nome de um enum e em lugar nenhum mais.
		//
		// A sessao do Frost Demon fechou so o proprio ramo e registrou o resto como divida de
		// BALANCEAMENTO. Ele deixa de ser divida agora porque as tres linhas raciais acima o tornariam
		// ABSURDO em vez de generoso: um Heran com o Max Power E o Super Saiyajin escolheria o maior dos
		// dois e a linha racial dele nunca seria usada -- ou seja, portar as formas das outras racas sem
		// fechar isto seria portar codigo morto.
		//
		// **O QUE SE PERDE, DITO EM VOZ ALTA:** as racas que nao tem transformacao no DM ficam sem
		// transformacao nenhuma aqui -- Humano, Majin, Bio-Androide, Gray, Makyo, Demonio, Android, Kai,
		// Tsujin, Meta, Kanassa, Yardrat, Arlian, Shapeshifter, Spirit Doll, Saibaman e Demigod. Isso E
		// o DM (nenhuma delas tem `slot=sFORM` na propria arvore alem do que ficou de fora deste porte),
		// mas ate ontem elas tinham a escada Saiyajin de graca, e quem estava jogando com elas VAI
		// notar. As que tem forma no original e ainda nao a tem aqui estao nomeadas no relatorio desta
		// sessao, uma por uma, com o sistema que falta pra cada.
		//
		// O `EhSaiyajin` cobre puro e meio (`"Half Saiyan"`, `"Halfbreed"`), e e o MESMO predicado que o
		// Oozaru ja usava duas linhas abaixo -- de proposito: duas perguntas diferentes sobre "tem
		// sangue Saiyajin?" e o jeito de um Saiyajin ganhar o macaco e perder o SSJ, ou o contrario.
		// ============================================================================================================
		else if (EhSaiyajin(p.Raca)) abertas.Add(LinhaDeForma.Saiyajin);

		// ============================ O `canSSJ` E O BYPASS, E ELE E LITERAL DO DM ============================
		// `Transformation Controls.dm:2` -- `if(usr.canSSJ)` roda a escada Super Saiyajin inteira pra
		// quem quer que tenha a var ligada, sem olhar raca. Hoje quem a recebe e o bio-androide de
		// laboratorio nascido com DNA Saiyajin (`DNALabs.dm:478`), e e por isso que ele SOMA a linha
		// Saiyajin em vez de troca-la: ele tem a Super Perfeita (do corpo dele) **e** o Super
		// Saiyajin (do DNA que o gerou), e as duas ao mesmo tempo e o desenho da criatura.
		//
		// FORA DA CASCATA, e nao dentro: um `else if` aqui apagaria a linha do proprio bio, que e
		// justamente o que ele tem de seu. O Oozaru logo abaixo ja e paralelo pelo mesmo argumento.
		// ====================================================================================================
		if (p.CanSsj) abertas.Add(LinhaDeForma.Saiyajin);

		// O OOZARU e paralelo: quem tem sangue Saiyajin o tem, independente da escada escolhida.
		if (EhSaiyajin(p.Raca)) abertas.Add(LinhaDeForma.Oozaru);

		// AS DIVINAS NAO SAO DE RACA: o ki divino se aprende. `godki.awakened` e o gate, e ele vem
		// no perfil como maestria >= 0 (negativo = nem despertou).
		if (p.GodKi >= 0)
		{
			// AS TRES ESCADAS DIVINAS SE EXCLUEM PELA CLASSE. O Kaio tem a Rose no lugar da padrao
			// e o Prodigial tem a do Mistico; ninguem tem duas.
			//
			// A LINHA DO MISTICO TAMBEM SE ABRE POR FORA DAQUI, e tem que ser assim: o Mistico e
			// concedido pelo ritual do Kaioshin a QUALQUER raca, inclusive a quem nunca ouviu
			// falar de ki divino. Quem abre a porta nesse caso e a propria concessao -- ver
			// `FormaDef.SoPorConcessao` e o passo 1 do `EstadoDeForma.Avaliar`. O que esta linha
			// faz aqui e outra coisa: dar ao Prodigial com ki divino o acesso ao BEAST, que e o
			// segundo degrau dela e continua sendo so dele.
			bool kaio = string.Equals(p.Classe, ClasseRose, StringComparison.OrdinalIgnoreCase);
			bool prodigial = string.Equals(p.Classe, ClasseProdigial, StringComparison.OrdinalIgnoreCase);
			abertas.Add(kaio ? LinhaDeForma.GodKiRose
					  : prodigial ? LinhaDeForma.Mistico
					  : LinhaDeForma.GodKi);

			// ULTRA INSTINCT e ULTRA EGO sao EXCLUSIVOS ENTRE SI (memoria do projeto: "PATHS
			// EXCLUSIVOS (UI xor UE)"). Quem ja tem energia de Ego escolheu o Ego.
			if (p.GodKi >= GodkiUiUePct)
			{
				if (p.EnergiaUe > 0) abertas.Add(LinhaDeForma.UltraEgo);
				else if (p.ProficienciaUi > 0) abertas.Add(LinhaDeForma.UltraInstinct);
				else { abertas.Add(LinhaDeForma.UltraInstinct); abertas.Add(LinhaDeForma.UltraEgo); }
			}
		}
		return abertas;
	}

	/// <summary>
	/// Sangue Saiyajin -- puro ou meio. O meio-Saiyajin ("Half Saiyan"/"Quarter") tambem vira
	/// Oozaru; o que ele nao faz e passar do SSJ3 (`stathalfbreed.dm:9` poe `ssj4mult = 1.75`).
	///
	/// ============================ O `Contains` SOZINHO PERDIA O MEIO-SAIYAJIN, E ELE NAO AVISAVA ============================
	/// A frase acima -- "puro ou meio" -- estava escrita aqui desde o primeiro dia, e o codigo abaixo
	/// dela **nao a cumpria**: a raca que o `races.json` grava no meio-Saiyajin chama-se `"Halfbreed"`,
	/// e "Halfbreed" nao contem "Saiyan". Enquanto o `else` do <see cref="LinhasAbertas"/> dava a
	/// escada Saiyajin a qualquer raca, isso nao tinha consequencia nenhuma -- ele caia no `else` e
	/// recebia a escada por acidente. No dia em que o `else` passou a perguntar `EhSaiyajin`, o
	/// meio-Saiyajin perdeu **as duas** linhas dele (a Saiyajin e o Oozaru) de uma vez, em silencio.
	///
	/// E o resto do jogo nunca concordou com essa perda. `GameServer.Combat.TemRabo` da rabo a
	/// `"Halfbreed"`; `LimiaresPessoais.Rolar` sorteia o `ssjat` dele; `Fighter.Training` o conta como
	/// meio-Saiyajin; a criacao lhe oferece as tres classes de meio-Saiyajin; o `FormaDef.MultDiluido`
	/// existe **so** pra ele; e o comentario do proprio `TemEscada` guarda a frase que isto era antes:
	/// *"era `pl.Race is "Saiyan" or "Halfbreed"`"*.
	///
	/// A CONSTANTE E NAO O LITERAL, e a comparacao e EXATA nela: `Contains("Halfbreed")` casaria com
	/// qualquer raca futura cujo nome a contivesse, que e o erro que o <see cref="EhDaRaca"/> logo
	/// abaixo existe pra descrever.
	///
	/// Quem vigia isto e a secao 1 da `GameServer.RaciaisTeste`: ela varre o `races.json` inteiro e
	/// cobra que toda raca pra quem o NASCIMENTO sorteia limiar de transformacao tenha alguma escada.
	/// =================================================================================================================
	/// </summary>
	public static bool EhSaiyajin(string raca) =>
		raca.Contains("Saiyan", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(raca, RacaMeioSaiyajin, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// E DESTA RACA? Comparacao exata e sem caixa -- o oposto do <see cref="EhSaiyajin"/>, que e por
	/// SUBSTRING de proposito (ele precisa pegar "Half Saiyan"; "Halfbreed" ele pega pelo nome, ver
	/// la -- confiar na substring pra aquele foi exatamente o que custou a escada do meio-Saiyajin).
	///
	/// Aqui a exatidao e que importa: `Contains("Alien")` casaria com qualquer raca futura cujo nome
	/// contivesse a palavra, e daria a escada Alien pra ela sem ninguem notar. As tres racas destas
	/// linhas tem nome fechado no `races.json` (`Namekian`, `Heran`, `Alien`) e nenhuma tem meio-sangue
	/// no port.
	/// </summary>
	private static bool EhDaRaca(string raca, string alvo) =>
		string.Equals(raca, alvo, StringComparison.OrdinalIgnoreCase);

	/// <summary>Todas as formas de uma linha, em ordem.</summary>
	public static IEnumerable<FormaDef> DaLinha(LinhaDeForma l) =>
		Todas.Where(d => d.Linha == l && d.Id != IdBase).OrderBy(d => d.Ordem);

	// ==================================================================================
	// O MULTIPLICADOR
	// ==================================================================================

	/// <summary>
	/// O DEGRAU BRUTO desta forma pela maestria -- sem piso, sem combate.
	///
	/// `stepped_mastery_mult` (supersaiyanbuff.dm:591): com n degraus e sem limiares explicitos, o
	/// degrau k liga em `k/n*100`, e o ULTIMO so aos 100%. A divisao e em ponto flutuante de
	/// proposito: com 3 degraus o segundo abre em 66,67%.
	/// </summary>
	public static double MultBruto(FormaDef d, double maestria, bool diluido)
	{
		double[] tab = (diluido ? d.MultDiluido : null) ?? d.Mult;
		if (tab.Length == 0) return 1;
		if (tab.Length == 1) return tab[0];

		if (d.Como == Curva.Rampa)
			return tab[0] + (tab[^1] - tab[0]) * Math.Clamp(maestria, 0, 100) / 100.0;

		// PorLimiar
		int idx = 0;
		for (int k = 1; k < tab.Length; k++)
		{
			double limiar = d.Limiares != null && k < d.Limiares.Length
				? d.Limiares[k]
				: (k + 1) * 100.0 / tab.Length;   // o `stepped_mastery_mult`
			if (maestria >= limiar) idx = k;
		}
		return tab[idx];
	}

	/// <summary>
	/// O DEGRAU BRUTO DE QUEM ESCALA COM O KI DIVINO -- a curva do Mistico. Ver
	/// <see cref="CurvaDeGodKi"/> pros quatro numeros e <see cref="FormaDef.EscalaComGodKi"/> pro
	/// porque de ela nao caber no `Mult[]`/`Limiares[]`.
	///
	/// O `GodKi &lt; 0` E "NUNCA DESPERTOU" e nao "0% de maestria" -- a distincao e a mesma que o
	/// <see cref="FormaDef.PedeGodKi"/> ja carrega, e aqui ela vale um degrau inteiro: quem
	/// despertou com 0% ja esta em 22x, quem nunca despertou fica em 18x.
	///
	/// A DILUICAO DE SANGUE NAO ENTRA. O `MultDiluido` nerfa a escada Saiyajin de quem tem meio
	/// sangue (`ssj1base` 2 -> 1,35), e o Mistico nao e sangue: e um dom concedido, igual pra toda
	/// raca. Nerfar o meio-Saiyajin aqui seria punir a raca por uma coisa que a raca nao deu.
	/// </summary>
	public static double MultPorGodKi(FormaDef d, CurvaDeGodKi c, PerfilDeFormas p)
	{
		// A ORIGEM CASA COM LINHAGEM **OU** CLASSE, pelo mesmo motivo do `PedeOrigemUmaDe`: o
		// Prodigial e CLASSE de meio-Saiyajin (`stathalfbreed.dm:71`), e a pergunta "de onde voce
		// vem" mora em campos diferentes conforme a raca.
		bool daOrigem = c.Origens.Any(o => string.Equals(o, p.Classe, StringComparison.OrdinalIgnoreCase)
										|| string.Equals(o, p.Linhagem, StringComparison.OrdinalIgnoreCase));

		if (!daOrigem) return d.Mult.Length > 0 ? d.Mult[0] : 1;   // o valor de todo mundo
		if (p.GodKi < 0) return c.SemGodKi;                        // a origem certa, sem ki divino

		// E DAQUI PRA CIMA E RAMPA. O `Clamp` e o TETO ABSOLUTO da forma: passar de `TopoEm` nao
		// rende mais nada, por mais maestria divina que se acumule.
		double t = c.TopoEm > 0 ? Math.Clamp(p.GodKi / c.TopoEm, 0, 1) : 1;
		return c.AoDespertar + (c.NoTopo - c.AoDespertar) * t;
	}

	/// <summary>
	/// O MULTIPLICADOR EFETIVO: degrau + rampa/bonus de combate + piso sobre a anterior.
	///
	/// A recursao no piso e deliberada e e do original: `ssj3` usa o SSJ2 **EFETIVO** e nao o cru
	/// (`supersaiyanbuff.dm:730` tem o comentario explicando que o cru deixaria o SSJ3 nerfado mais
	/// fraco que o SSJ2). Como cada degrau so olha o de baixo, a recursao tem a profundidade da
	/// linha -- sete no pior caso.
	///
	/// <param name="combateSegundos">Ha quanto tempo em combate CONTINUO. Alimenta as duas
	/// mecanicas de combate; 0 fora de luta.</param>
	///
	/// ============================ POR QUE O PERFIL E OBRIGATORIO ============================
	/// Ele entrou NO LUGAR do antigo `bool diluido` (que ja vivia dentro dele) e nao ao lado, e nao
	/// tem valor padrao. Os dois motivos sao o mesmo defeito visto de dois lados:
	///
	///   * com o perfil OPCIONAL, um chamador esquecido calcularia o Mistico de um Prodigial de
	///     ki divino maduro como 16x -- numero plausivel, silencioso, e errado em 2x;
	///   * com `= default`, o `PerfilDeFormas` (record STRUCT) nasceria com `GodKi = 0` e nao `-1`
	///     -- os defaults do construtor nao valem pro `default` --, ou seja "despertou com 0%"
	///     em vez de "nunca despertou". Seria pior: abriria porta divina pra quem nao tem.
	///
	/// Trocar o TIPO do parametro foi o compilador achando os 46 chamadores por mim.
	/// ====================================================================================
	/// </summary>
	public static double Multiplicador(string id, Maestrias m, PerfilDeFormas perfil,
									   double combateSegundos = 0)
	{
		FormaDef? d = Def(id);
		if (d == null || d.Id == IdBase) return 1;

		// A CURVA DE FORA vem antes de tudo: quando ela existe, a maestria da propria forma nao
		// manda no multiplicador (ver `FormaDef.EscalaComGodKi`). O resto do metodo -- rampa de
		// combate, bonus e piso -- continua valendo por cima, e e de graca: nenhuma forma de curva
		// externa usa esses campos hoje, e a que usar um dia ja acha a conta pronta.
		double v = d.EscalaComGodKi is { } curva
			? MultPorGodKi(d, curva, perfil)
			: MultBruto(d, m.De(d.Id), perfil.Diluido);

		// ============================ A ESCALA DA CLASSE -- ver FormaDef.BaseDaClasse ============================
		// Depois da curva de maestria e ANTES de tudo o mais, porque e assim no DM: o
		// `heran_form_mult()` (`HeranBuff.dm:245-249`) monta a lista de degraus JA multiplicada pelo
		// `ssjmult` da classe -- `list(ssjmult, ssjmult*1.2, ssjmult*1.68, ssjmult*2.016)` -- e so
		// entao pergunta em que degrau a maestria esta. Multiplicar aqui da o mesmo numero e mantem a
		// tabela do catalogo legivel como o que ela e: a CURVA, e nao o poder.
		//
		// A chave vazia e o `else` do original (Epsilon, no Heran). Um mapa sem `""` e sem a classe
		// deste personagem cairia em 1 e a forma valeria a curva crua -- por isso a fabrica e o
		// `else`, e nao um `throw`: o catalogo nunca deve derrubar o servidor por dado incompleto.
		// ======================================================================================================
		if (d.BaseDaClasse is { } porClasse)
		{
			double baseDaClasse = porClasse.TryGetValue(perfil.Classe, out double b) ? b
								: porClasse.GetValueOrDefault("", 1);
			v *= baseDaClasse;
		}

		// LSSJ: a luta sobe do MINIMO ao MAXIMO, e a maestria e so um piso. `max` dos dois.
		if (d.CombateSobeAoMaximo && d.Mult.Length > 1)
		{
			double rampa = d.Mult[0] + (d.Mult[^1] - d.Mult[0])
						 * Math.Min(combateSegundos / RampaLssjSegundos, 1);
			v = Math.Max(v, rampa);
		}

		// LEGENDARY PRIMAL: o combate MULTIPLICA por cima. Outra mecanica.
		if (d.BonusDeCombate > 0)
			v *= 1 + Math.Min(combateSegundos / RampaPrimalSegundos, 1) * d.BonusDeCombate;

		// PISO SOBRE A ANTERIOR -- e a anterior EFETIVA, nao a crua.
		if (d.PisoSobreAnterior > 0 && Anterior(d) is { } ant)
			v = Math.Max(v, Multiplicador(ant.Id, m, perfil, combateSegundos) + d.PisoSobreAnterior);

		// O PISO CONTA OS RAMOS TAMBEM: `max(SSJ1, maior grade DESBLOQUEADO) + 2`
		// (supersaiyanbuff.dm:729). O grade nao e o anterior do SSJ2 -- os dois saem do SSJ1 -- entao
		// ele nao entra pela recursao, e sem esta linha um SSJ2 de quem dominou o SSJ1 ate os 70%
		// ficaria mais fraco que o proprio Grade 3 dele.
		//
		// E "DESBLOQUEADO" e a parte que importa: um ramo que o jogador nunca abriu nao levanta piso
		// nenhum. Era exatamente isso que estava quebrado antes do campo ForaDoTronco existir.
		if (d.PisoSobreAnterior > 0)
		{
			double ramo = MaiorRamoAberto(d, m, perfil.Diluido);
			if (ramo > 0) v = Math.Max(v, ramo + d.PisoSobreAnterior);
		}
		return v;
	}

	/// <summary>
	/// O MULTIPLICADOR DO MAIOR RAMO JA DESTRAVADO que sai do MESMO degrau que esta forma. 0 = nenhum.
	///
	/// Hoje isto so encontra os grades do SSJ1, mas a regra e geral de proposito: quem acrescentar
	/// um ramo lateral em qualquer linha nao precisa vir aqui escrever o nome dele.
	/// </summary>
	public static double MaiorRamoAberto(FormaDef d, Maestrias m, bool diluido)
	{
		FormaDef? tronco = Anterior(d);
		if (tronco == null) return 0;

		double melhor = 0;
		foreach (FormaDef r in Todas)
		{
			if (!r.ForaDoTronco || r.Linha != d.Linha || r.Id == d.Id) continue;
			if (Anterior(r)?.Id != tronco.Id) continue;
			if (r.PedeMaestria > 0 && m.De(tronco.Id) < r.PedeMaestria) continue;   // ramo travado
			melhor = Math.Max(melhor, MultBruto(r, m.De(r.Id), diluido));
		}
		return melhor;
	}

	/// <summary>
	/// FRACAO DO KI MAXIMO DRENADA POR SEGUNDO.
	///
	/// O original cobra por ciclo do BuffLoop (~0,8x/s) e o comentario dele da a conta pronta: SSJ3
	/// nao-masterizado, `ssj3drain = 0,075` -> 0,075*0,4 = 3% por ciclo -> ~2,4%/s, ou seja esvazia
	/// um Ki cheio em ~25 s.
	///
	/// O SSJ1 zera o dreno aos 100% de maestria -- dominar a forma e poder ANDAR nela.
	/// </summary>
	public static double DrenoPorSegundo(string id, Maestrias m)
	{
		FormaDef? d = Def(id);
		if (d == null || d.Id == IdBase || d.Dreno.Length == 0) return 0;

		const double porCiclo = 0.4, ciclosPorSeg = 0.8;
		double maestria = m.De(d.Id);

		int idx = 0;
		for (int k = 1; k < d.Dreno.Length; k++)
			if (maestria >= (k + 1) * 100.0 / d.Dreno.Length) idx = k;

		return d.Dreno[idx] * porCiclo * ciclosPorSeg;
	}
}

/// <summary>
/// Quanto o personagem domina CADA forma, de 0 a 100.
///
/// ============================ O SAVE NAO MUDOU DE FORMATO ============================
/// As chaves gravadas continuam sendo o <see cref="FormaDef.IdRede"/> em texto -- "10" pro SSJ1,
/// "30" pro SSJ3 -- exatamente como antes deste rework. Um save de ontem carrega sem conversao, e
/// uma chave que nao existe mais e simplesmente ignorada em vez de derrubar o load.
/// =====================================================================================
/// </summary>
public sealed class Maestrias
{
	private readonly Dictionary<string, double> _v = new(StringComparer.Ordinal);

	/// <summary>
	/// Quanto se domina esta forma. **A chave passa pelo <see cref="Catalogo.ChaveDaMaestria"/>** --
	/// ver la: a linha do Frost Demon inteira compartilha uma barra so, que e o `fd_base_mastery` do
	/// original.
	///
	/// Ler e escrever pela MESMA canonizacao e o ponto: quem perguntar pela 2a Evolucao recebe a
	/// mesma barra que o motor do Mutante consulta pro `fd_stable_gate`, e nao ha um segundo numero
	/// pra divergir.
	/// </summary>
	public double De(string? id) => id == null ? 0 : _v.GetValueOrDefault(Catalogo.ChaveDaMaestria(id));

	/// <summary>
	/// ============================ FORMA DE DISCIPLINA NAO TEM MAESTRIA PROPRIA ============================
	/// O dono: *"ambas essas formas sao da skill de ultra instinct, entao usar elas aumenta a maestria
	/// na SKILL e nao nelas em si"*. Quem responde "esta forma e de uma disciplina?" e o
	/// <see cref="Disciplinas.DaForma"/> -- ver o cabecalho dele pro porque de a pergunta ser essa.
	///
	/// A RECUSA MORA NO **ESCRITOR** e nao em cada chamador, e isso e a regra e nao economia: hoje sao
	/// tres caminhos que escrevem maestria (o <see cref="Subir"/> do tique da forma, o verb de admin
	/// `Forma.Maestria.Por(atual, 100)` e o <see cref="DoSave"/>), e "ligar a regra num chamador e
	/// esquecer do outro" e, escrito nos comentarios deste port, o erro que mais se repetiu. Barrando
	/// aqui, o livro fica ESTRUTURALMENTE incapaz de guardar maestria pras quatro formas divinas.
	///
	/// O DM concorda: `UltraInstinct.dm` e `UltraEgo.dm` nao tem uma unica var de maestria por forma --
	/// o unico `mastery` dos dois arquivos e o `S.godki.mastery`, que e a porta de aprendizado.
	/// ================================================================================================
	/// </summary>
	public void Por(string id, double v)
	{
		if (Disciplinas.DaForma(id) != null) return;
		_v[Catalogo.ChaveDaMaestria(id)] = Math.Clamp(v, 0, 100);
	}

	/// <summary>
	/// Sobe a maestria e devolve TRUE quando cruzou um marco que muda o jogo.
	///
	/// Nao ha guarda de disciplina aqui de proposito: quem escreve e o <see cref="Por"/>, e la a
	/// recusa vale pros tres caminhos de uma vez. Numa forma de disciplina este metodo le 0, escreve
	/// nada, le 0 de novo e devolve false -- sem marco, sem anuncio.
	/// </summary>
	public bool Subir(string id, double quanto, out string marco)
	{
		marco = "";
		if (id == Catalogo.IdBase || quanto <= 0) return false;

		double antes = De(id);
		if (antes >= 100) return false;
		Por(id, antes + quanto);
		double agora = De(id);

		// OS MARCOS QUE O JOGADOR PRECISA SABER QUE CRUZOU. Os grades sao o unico caso de maestria
		// que DESTRAVA outra forma, e por isso tem aviso proprio: sem ele o jogador so descobriria
		// a existencia do Grade 2 abrindo a aba por acaso.
		if (id == "ssj1")
		{
			if (antes < Catalogo.Grade2Pct && agora >= Catalogo.Grade2Pct) marco = "Grade 2 liberado";
			else if (antes < Catalogo.Grade3Pct && agora >= Catalogo.Grade3Pct) marco = "Grade 3 liberado";
		}
		if (antes < 100 && agora >= 100) marco = "forma DOMINADA";
		return marco.Length > 0;
	}

	public IEnumerable<(string Id, double V)> Todas => _v.Select(kv => (kv.Key, kv.Value));

	/// <summary>Chave = <see cref="FormaDef.IdRede"/> em texto. Ver o cabecalho da classe.</summary>
	public Dictionary<string, double> ParaSave() =>
		_v.Where(kv => Catalogo.Def(kv.Key) != null)
		  .ToDictionary(kv => Catalogo.Rede(kv.Key).ToString(), kv => kv.Value);

	/// <summary>
	/// Le o livro do disco. Devolve os NOMES das formas cujo registro foi DESCARTADO por elas terem
	/// virado formas de disciplina -- vazio no caso normal.
	///
	/// ============================ POR QUE DESCARTA EM VEZ DE MIGRAR ============================
	/// A maestria de forma e a proficiencia da disciplina nao sao a mesma moeda: a primeira se pagava
	/// SUSTENTANDO a forma (em qualquer lugar), a segunda se paga LUTANDO com ela. Somar uma na outra
	/// entregaria de graca as faixas que a disciplina cobra caro -- o Godly Display e a Hakai Infusion
	/// abrem aos 80%, e um personagem que passou horas parado dentro do Sign sairia com as duas.
	///
	/// E o que se perde e medivel e proximo de nada: as quatro formas drenam 5%-6,5% do Ki maximo POR
	/// SEGUNDO (`FormaDef.Dreno`), ou seja um tanque cheio a cada ~20 s. Os 3 h dentro da forma que
	/// valiam 100% de maestria nunca foram alcancaveis; na pratica o disco guarda fracoes de 1%.
	///
	/// **O que nao se pode e descartar calado** -- por isso o retorno existe, e o servidor avisa o
	/// jogador no login (ver `GameServer.cs`, o `DoSave` do login).
	/// ======================================================================================
	/// </summary>
	public List<string> DoSave(Dictionary<string, double>? d)
	{
		var descartadas = new List<string>();
		_v.Clear();
		if (d == null) return descartadas;
		foreach ((string k, double v) in d)
		{
			// Uma chave que nao casa com nenhuma forma e IGNORADA, nao um erro: e o caminho de
			// quem carrega um save mais novo que o binario, e derrubar o load ali custaria a conta.
			//
			// O `RedeDoSave` entra ANTES do `PorRede` porque um save mais VELHO e o caso oposto: a
			// chave existe, a forma e que mudou de numero. Sem ele o `306` cairia neste mesmo
			// silencio e levaria junto as horas de maestria do Mistico -- ver `Catalogo.RedeDoSave`.
			if (!ushort.TryParse(k, out ushort rede)) continue;
			if (Catalogo.PorRede(Catalogo.RedeDoSave(rede)) is not { } def) continue;

			// O REGISTRO ANTIGO DAS FORMAS DE DISCIPLINA CAI AQUI, e cai FALANDO. O `Por` ja recusaria
			// (ver o cabecalho dele), mas em silencio: quem carregasse um save com 12% no Sign veria o
			// numero simplesmente sumir da aba e nao teria como saber se foi regra ou defeito.
			if (Disciplinas.DaForma(def.Id) != null)
			{
				// So o que tinha valor vira aviso. Uma chave zerada nao e perda de ninguem.
				if (v > 0 && !descartadas.Contains(def.Nome)) descartadas.Add(def.Nome);
				continue;
			}

			// A MAIOR VENCE, e nao a ultima lida: quando duas formas viram uma, o save traz as duas
			// chaves e o dicionario nao promete ordem. Ficar com a menor apagaria progresso por
			// sorteio -- o tipo de defeito que so aparece num personagem em cada tantos.
			// A CANONIZACAO ENTRA AQUI TAMBEM (`ChaveDaMaestria`): um save gravado com uma chave por
			// degrau de Frost Demon (ou por um binario futuro que os separe de novo) chega como
			// varias, e as varias sao a MESMA barra. Sem isto, a linha do Frost teria no disco uma
			// verdade que a memoria nao tem -- e a de baixo, "a maior vence", e justamente a regra
			// que resolve o encontro.
			string chave = Catalogo.ChaveDaMaestria(def.Id);
			double agora = Math.Clamp(v, 0, 100);
			_v[chave] = _v.TryGetValue(chave, out double ja) ? Math.Max(ja, agora) : agora;
		}
		return descartadas;
	}
}
