namespace Jandirus.Core.Combat;

/// <summary>
/// UMA ARTE DE ATAQUE DE KI -- a identidade do `.dmi` que o BYOND punha no objeto que voa.
///
/// ============================ A FOLHA SAIU DA TELA, O NOME FICOU ============================
/// Cada entrada nasceu como uma folha de sprite. O dono mandou trocar (2026-10-07): *"ao inves de
/// usar sprite pra cada beam como era no byond, fazer eles por efeitos, noise e shaders do proprio
/// godot"* -- e os tiros hoje sao desenhados por shader (`Client/ProjetilDesenhado`).
///
/// O ENUM NAO MORREU COM AS FOLHAS, porque ele nunca foi "qual arquivo": e QUAL ATAQUE E ESTE NA
/// TELA. O Masenko continua nao se parecendo com o Kamehameha, a bola do Android continua nao sendo
/// a do Tsujin, e o jogador continua escolhendo uma destas na mesa de tecnicas. Cada entrada virou
/// um ESTILO (`Client/ArteDeKiNoCliente`), desenhado a partir do que a folha de mesmo nome mostrava.
/// ==========================================================================================
///
/// ============================ POR QUE UM SIMBOLO, E NAO O DESENHO ============================
/// O Core nao conhece Godot: quem sabe como um `Beam3` se desenha e o cliente. Aqui so existe a
/// IDENTIDADE, e ela e o que viaja no fio -- dois bytes, uma vez por tiro.
/// ==========================================================================================
///
/// ============================ OS NUMEROS SAO CONTRATO DE REDE ============================
/// Cada entrada tem valor EXPLICITO porque este enum viaja no anuncio de nascimento
/// (`GameServer.AnunciarProjetil`). Reordenar a lista sem os numeros trocaria a arte de todo tiro
/// pra qualquer cliente de versao diferente -- e o defeito apareceria como "o Kamehameha do fulano
/// virou uma bola", nunca como erro. Entrada nova entra NO FIM, com numero novo.
/// ====================================================================================
///
/// ============================ ONDE O BYOND RESOLVE O NOME ============================
/// No DM o icone e escrito pelo nome puro (`'Makkankosappo.dmi'`) e o compilador procura na ORDEM
/// dos `FILE_DIR` do `.dme`: `Icons/Beams` (linha 7) vem antes de `Icons/Blasts` (9), que vem antes
/// de `Icons/Techniques` (51). Isso decide os DOIS casos ambiguos do jogo -- `Makkankosappo.dmi` e
/// `Beam - Big Fire.dmi` existem nas duas pastas, e quem vale e a de `Beams`. Os comentarios abaixo
/// dizem a pasta VENCEDORA, e e ela que o cliente tem que carregar.
/// ==================================================================================
/// </summary>
public enum ArteDeKi : ushort
{
	/// <summary>
	/// NENHUMA -- e ela NAO e "sem arte", e "ainda nao resolvida".
	///
	/// A receita nasce assim e o <see cref="ArteDeProjetil.De"/> preenche no disparo. Um tiro que
	/// chega ao cliente com zero (um verb fora da tabela, um numero de uma versao mais nova) e
	/// desenhado no estilo NEUTRO do tipo dele -- um raio liso, uma bola lisa -- que e a resposta
	/// certa: melhor um tiro sem personalidade do que um tiro invisivel.
	/// </summary>
	Nenhuma = 0,

	// =====================================================================
	// RAIOS -- `Icons/Beams`
	// =====================================================================
	/// <summary>`Beam3.dmi`. O raio generico da casa: `WaveIcon` e `beamicon` padrao (`race.dm:57`, `beams.dm:4`).</summary>
	Beam3 = 1,

	/// <summary>`Beam2.dmi`. So o Demigod da linhagem Demigod nasce com ele (`statdemi.dm:9`).</summary>
	Beam2 = 2,

	/// <summary>`Beam4.dmi`. Boom Wave (`beams.dm:486`).</summary>
	Beam4 = 3,

	/// <summary>`Beam11.dmi`. Raio Colossal / Massive Beam (`beams.dm:426`).</summary>
	Beam11 = 4,

	/// <summary>`BeamMasenko.dmi`. Masenko (`beams.dm:322`).</summary>
	BeamMasenko = 5,

	/// <summary>`BeamStaticBeam.dmi`. O verb `Makkankosappo` de `beams.dm:357` -- ver a nota da tabela.</summary>
	BeamStaticBeam = 6,

	/// <summary>`Beam - Big Fire.dmi`. Final Flash (`beams/FinalFlash.dm:23`).</summary>
	BeamBigFire = 7,

	/// <summary>`EraserCannon.dmi`. Erasure (`beams/Erasure.dm:35`) -- ainda nao portada. Ver a nota do fim.</summary>
	EraserCannon = 8,

	/// <summary>`Makkankosappo.dmi` (pasta `Beams`). O `Makkankoicon` do Yardrat (`statyardrat.dm:11`).</summary>
	Makkankosappo = 9,

	// =====================================================================
	// TECNICAS NOMEADAS -- `Icons/Techniques`
	// =====================================================================
	/// <summary>`Kamehameha1.dmi`. Um dos SEIS do sorteio (`beams/Kamehameha.dm:15`).</summary>
	Kamehameha1 = 10,
	Kamehameha2 = 11,
	Kamehameha3 = 12,
	Kamehameha4 = 13,
	Kamehameha5 = 14,

	/// <summary>`Kamehameha6.dmi`. O ultimo do `rand(1,6)` (`beams/Kamehameha.dm:20`).</summary>
	Kamehameha6 = 15,

	/// <summary>`galacticgun.dmi`. Galick Ho (`beams/GalicGun.dm:22`).</summary>
	GalacticGun = 16,

	/// <summary>`Makkankosappo3.dmi`. Death Beam (`beams/DeathBeam.dm:22`) e o Makkanko do Tsujin (`stattsujin.dm:11`).</summary>
	Makkankosappo3 = 17,

	/// <summary>`Makkankosappo4.dmi`. O `Makkankoicon` PADRAO (`race.dm:66`) e o do Demigod (`statdemi.dm:54`).</summary>
	Makkankosappo4 = 18,

	/// <summary>`Dodompa.dmi`. Dodon Ray (`beams.dm:442`).</summary>
	Dodompa = 19,

	/// <summary>`Enkumei.dmi`. Enkumei (`beams/Enkumei.dm:35`).</summary>
	Enkumei = 20,

	/// <summary>`BasenioBlast.dmi`. Kill Driver (`blasts/KillDriver.dm:23`).</summary>
	BasenioBlast = 21,

	/// <summary>`Kikoho.dmi`. Kikoho (`blasts/Kikoho.dm:56`).</summary>
	Kikoho = 22,

	/// <summary>`Kienzan.dmi`. O disco (`discs.dm:49`).</summary>
	Kienzan = 23,

	/// <summary>`KiHead.dmi`. Paralysis e Stunlock (`Ki2.0/Debuffs.dm:18`, `Race Trees/meta.dm:74`).</summary>
	KiHead = 24,

	// =====================================================================
	// BOLAS -- `Icons/Blasts`
	// =====================================================================
	/// <summary>`1.dmi`. `BLASTICON` padrao (`race.dm:62`) e do Demigod-Demigod (`statdemi.dm:13`).</summary>
	Blast1 = 25,

	/// <summary>`18.dmi`. `CBLASTICON` padrao (`race.dm:64`) e do Demigod-Demigod (`statdemi.dm:15`).</summary>
	Blast18 = 26,

	/// <summary>`5.dmi` / `6.dmi`. Bola e bola carregada do Tsujin (`stattsujin.dm:6-7`).</summary>
	Blast5 = 27,
	Blast6 = 28,

	/// <summary>`7.dmi` / `8.dmi`. Yardrat (`statyardrat.dm:7-8`).</summary>
	Blast7 = 29,
	Blast8 = 30,

	/// <summary>`10.dmi` / `11.dmi`. Android (`statandroid.dm:9-10`).</summary>
	Blast10 = 31,
	Blast11 = 32,

	/// <summary>`19.dmi` / `20.dmi`. Genie (`statdemi.dm:45-46`).</summary>
	Blast19 = 33,

	/// <summary>
	/// `20.dmi`. DUAS VIDAS: o `CBLASTICON` do Genie (`statdemi.dm:46`) e o Tiro Carregado de
	/// TODO MUNDO -- o `Charged_Shot` nao le `CBLASTICON`, ele escreve `'20.dmi'` na unha
	/// (`blasts.dm:103`). Nao ha entrada duplicada: e a mesma folha.
	/// </summary>
	Blast20 = 34,

	/// <summary>`31.dmi` / `35.dmi`. Ogre (`statdemi.dm:29,31`).</summary>
	Blast31 = 35,
	Blast35 = 36,

	/// <summary>`28.dmi`. A Bala Dispersa, literal e igual pra todos (`blasts.dm:574`).</summary>
	Blast28 = 37,

	/// <summary>`30.dmi`. O `GuideBombIcon` da Esfera Teleguiada (`blasts/GuidedBall.dm:23`).</summary>
	Blast30 = 38,

	/// <summary>`deathball2017purple2.dmi`. O `DEATHBALLICON` do Buster Shell (`blasts/BusterShell.dm:46`).</summary>
	DeathBall2017Purple2 = 39,

	/// <summary>`Blast - Spiraling Ki.dmi`. Spirit Gun (`Core Trees/Spirit.dm:355`).</summary>
	BlastSpiralingKi = 40,

	/// <summary>
	/// `12.dmi`. A bola PADRAO da tecnica customizada -- o `else A.icon = '12.dmi'` do
	/// `CustomMakeBlast` (`customattacks.dm:499-501`), pra quando o jogador nao escolheu arte.
	/// </summary>
	Blast12 = 41,

	/// <summary>
	/// `Daitoppa.dmi`, tingido com `rgb(blastR, blastG, blastB)`: a LAMINA DE AR do Kiai
	/// (`Ki2.0/Kiai.dm:34-40`). Ela nasce com `invisibility = 1` -- a folha existe pra quem tem
	/// `see_invisible` (ver `VisaoDoInvisivel`); pros outros a tecnica e so o sopro e o som.
	/// </summary>
	Daitoppa = 42,

	/// <summary>
	/// `SpiritBomb22017.png` -- a GENKIDAMA (`blasts/SpiritBomb.dm:25,45`). Nao e um `.dmi` de `Icons/`:
	/// e uma imagem solta de 450x450 em `Images/attacks/`, e por isso nenhuma tecnica customizada a
	/// alcanca (ver <see cref="ArteDeProjetil.PermitidasPara"/>).
	///
	/// ENTROU JUNTO COM O DESENHO POR SHADER: enquanto os tiros eram folha, a Genkidama voava sem arte
	/// (o verb nao estava na tabela) e saia como um circulo que nem crescia. O dono citou ela pelo nome.
	/// </summary>
	SpiritBomb = 43,

	/// <summary>`14.dmi`. O alvo de treino da meditacao (`Stats/Training/Meditate.dm:26`).</summary>
	Blast14 = 44,
}

/// <summary>
/// QUEM DECIDE A ARTE DE CADA TIRO -- **o unico lugar**, e cada linha cita o `arquivo:linha` do DM.
///
/// ============================ O BYOND ESCOLHE EM TRES CAMADAS ============================
/// Nao e "uma arte por tipo de projetil" (era o que este port desenhava: duas formas pra vinte e
/// quatro tecnicas). Sao tres, e a variedade mora na do meio:
///
///   1. PISO RACIAL -- todo mob nasce com `WaveIcon`, `BLASTICON`, `CBLASTICON` e `Makkankoicon`
///      (`race.dm:57-66`), e quatro racas sobrescrevem os seus (`statandroid`, `stattsujin`,
///      `statyardrat`, `statdemi` -- este ultimo POR LINHAGEM). Ver <see cref="Bola"/> e irmas.
///
///   2. SORTEIO NO APRENDIZADO -- `datum/skill/rank/Kamehameha/after_learn()` roda `rand(1,6)` e
///      grava `savant.Kamehamehaicon` (`beams/Kamehameha.dm:12-21`), que e `mob/var` NAO-`tmp`, ou
///      seja viaja no savefile. **O Kamehameha do seu personagem nao se parece com o do vizinho, e
///      nao muda nunca mais.** Ver <see cref="SorteioDoKamehameha"/>.
///
///   3. LITERAL NO VERB -- `usr.forceicon = 'BeamMasenko.dmi'` (`beams.dm:322`). A maioria.
///
/// E o consumo e um `if` so (`beams.dm:120-132`): `if(bypass) A.icon = forceicon; else A.icon =
/// WaveIcon`, e logo depois `A.icon += rgb(blastR,blastG,blastB)` -- a COR e do jogador e e SOMADA
/// (`ICON_ADD`) por cima da folha. Arte e cor sao ortogonais, e continuam sendo aqui.
/// =====================================================================================
///
/// ============================ O `WaveIcon` NAO DESENHA RAIO NENHUM, E ISSO E MEDIDO ============================
/// A camada 1 tem um buraco que so aparece lendo os verbs: **os DEZ raios portados escrevem
/// `bypass = 1`** (grep de `bypass` em `Skills/Ki/`: 22 escritas, todas `=1`). Como o `else` do
/// `beams.dm:120` e o unico consumidor de `WaveIcon`, a folha racial de RAIO nunca chega a um
/// projetil por esses caminhos -- quem responde pelo Ki Wave e pelo Volley e o `beamicon`
/// (`beams.dm:289,392`), outra `mob/var`, cujo padrao e a MESMA `Beam3.dmi`.
///
/// Por isso a tabela abaixo nao tem fonte "racial de raio": ela seria uma regra que o jogo original
/// nao pratica. O campo racial fica registrado em <see cref="Onda"/> -- ele existe, esta certo, e
/// esta anotado como o que e: um piso que nenhum verb portado alcanca.
/// ==========================================================================================================
///
/// ============================ E O `beamicon` NAO TEM QUEM O ESCREVA, ENTAO NAO HA O QUE SALVAR ============================
/// `beamicon` e `BLASTICON` sao trocaveis em jogo -- pelos verbs `Change_Beam_Icon` e
/// `Change_Blast_Icon` (`Character Customization/changeicon.dm:202-225`). Mas os dois pedem UPLOAD
/// DE ARQUIVO e passam por `if(!usr.can_upload_icon()) return`, que e gate de admin: **nao ha
/// catalogo, nao ha escolha de jogador, e nao ha nada pra persistir**. Inventar uma tela de escolha
/// aqui seria desenho novo, nao porte.
///
/// A UNICA escolha de arte que o jogador FAZ no original e a da tecnica customizada
/// (`pick_game_icon`, `customattacks.dm:543-568`), e ela e por tecnica -- mora em
/// <see cref="Jandirus.Core.Skills.TecnicaCustomizada.Arte"/>, que ja viaja no save junto do resto
/// da tecnica. Ver la.
/// ====================================================================================================================
/// </summary>
public static class ArteDeProjetil
{
	/// <summary>
	/// DE ONDE SAI A ARTE DESTE VERB. Ver o cabecalho da classe -- e a taxonomia das tres camadas
	/// do DM, e nao uma invencao deste port.
	/// </summary>
	public enum Fonte : byte
	{
		/// <summary>O verb escreve o `.dmi` na unha. A maioria.</summary>
		Literal = 0,

		/// <summary>`usr.BLASTICON` -- a bola da RACA.</summary>
		BolaRacial = 1,

		/// <summary>O `rand(1,6)` do Kamehameha, gravado no personagem.</summary>
		SorteioDeKamehameha = 2,

		// ============================ AS OUTRAS TRES FONTES DO DM NAO ESTAO AQUI ============================
		// O original tem mais tres camadas raciais -- `WaveIcon`, `CBLASTICON` e `Makkankoicon` --, e
		// as tres estao LIDAS e CONFERIDAS neste arquivo (<see cref="Onda"/>,
		// <see cref="BolaCarregada"/>, <see cref="Makkanko"/>). Nenhuma tem valor de enum, e isso e
		// deliberado: **nenhum verb portado alcanca nenhuma delas**, e um valor de enum que nenhuma
		// linha da tabela usa e um ramo morto num `switch` -- exatamente a "API 100% orfa" que este
		// projeto ja escreveu uma vez achando que estava aplicando a regra.
		//
		// Quem PRECISAR de uma delas (ao portar o Energy_Shot ou o Special Beam Cannon) acrescenta o
		// valor junto da linha que o usa, e nao antes. A leitura do DM -- que e a parte cara -- ja
		// esta feita e testada pela bancada.
		// ================================================================================================
	}

	/// <summary>Uma linha da tabela: a fonte, e (quando ela e literal) qual folha.</summary>
	public readonly record struct Regra(Fonte De, ArteDeKi Fixa);

	/// <summary>
	/// ============================ A TABELA -- UM LUGAR SO DECIDE ============================
	/// Chave = o id do verb, o MESMO que viaja no `C2S.Habilidade` e o mesmo que o `Canalizar`
	/// recebe. Nenhum outro arquivo escolhe arte: os vinte e quatro pontos de disparo dizem qual
	/// VERB estao atirando e a resposta sai daqui.
	///
	/// Cada entrada carrega o `arquivo:linha` do DM, como a tabela de membro decepado. Sem isso a
	/// tabela vira opiniao, e a primeira duvida ("o Masenko era mesmo BeamMasenko?") custa a
	/// releitura do original inteiro.
	/// ===================================================================================
	/// </summary>
	private static readonly Dictionary<string, Regra> Tudo = new(StringComparer.OrdinalIgnoreCase)
	{
		// ---------------------------------------------------------------
		// OS TRES DE PRODUCAO (`GameServer.Projeteis.cs`)
		// ---------------------------------------------------------------
		// `usr.forceicon = usr.beamicon` (`beams.dm:289`), e `beamicon = 'Beam3.dmi'` (`beams.dm:4`).
		// LITERAL e nao "racial de raio" de proposito -- ver o bloco do `WaveIcon` no cabecalho.
		["Ki_Wave"] = new(Fonte.Literal, ArteDeKi.Beam3),

		// `var/bcolor = usr.BLASTICON` (`blasts.dm:55`). A bola de todo dia E a folha da raca -- e a
		// unica tecnica do jogo em que um Android e um Tsujin atiram coisas visivelmente diferentes.
		["Basic_Blast"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),

		// `var/bcolor = GuideBombIcon` (`blasts/GuidedBall.dm:35`), `GuideBombIcon = '30.dmi'` (`:23`).
		["Guided_Ball"] = new(Fonte.Literal, ArteDeKi.Blast30),

		// ---------------------------------------------------------------
		// LOTE G5 -- os quatro raios de `beams.dm` e as dez de `blasts.dm`/`discs.dm`
		// ---------------------------------------------------------------
		["Masenko"] = new(Fonte.Literal, ArteDeKi.BeamMasenko),          // `beams.dm:322`

		// ATENCAO AO NOME: o verb `Makkankosappo` de `beams.dm:336` NAO usa a folha
		// `Makkankosappo.dmi` -- ele escreve `'BeamStaticBeam.dmi'` (`:357`). Quem usa a folha
		// homonima e o `Makkankoicon` do SpecialBeamCannon, que este lote nao portou. Copiar o nome
		// do verb pra a folha teria posto a arte errada num raio que ja esta em producao.
		["Makkankosappo"] = new(Fonte.Literal, ArteDeKi.BeamStaticBeam), // `beams.dm:357`

		["Massive_Beam"] = new(Fonte.Literal, ArteDeKi.Beam11),          // `beams.dm:426`
		["Final_Flash"] = new(Fonte.Literal, ArteDeKi.BeamBigFire),      // `beams/FinalFlash.dm:23,37`

		// `var/bcolor = '20.dmi'` (`blasts.dm:103`) -- LITERAL, e nao `CBLASTICON`. O tiro carregado
		// e a unica bola do jogo que ignora a raca de quem atira.
		["Charged_Shot"] = new(Fonte.Literal, ArteDeKi.Blast20),

		["KillDriver"] = new(Fonte.Literal, ArteDeKi.BasenioBlast),      // `blasts/KillDriver.dm:23,49`

		// `A.icon = usr.DEATHBALLICON` (`blasts/BusterShell.dm:46`), `= 'deathball2017purple2.dmi'`
		// (`blasts/GuidedBall.dm:94`). Var de mob que ninguem mais escreve: literal na pratica.
		["BusterShell"] = new(Fonte.Literal, ArteDeKi.DeathBall2017Purple2),

		["Scattershot"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),       // `blasts.dm:159`
		["Energy_Barrage"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),    // `blasts.dm:222`
		["Ki_Bomb"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),           // `blasts.dm:428`
		["Hellzone_Grenade"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),  // `blasts.dm:491`
		["Kienzan"] = new(Fonte.Literal, ArteDeKi.Kienzan),              // `discs.dm:49`

		// As duas leem a MESMA `ParalysisIcon = 'KiHead.dmi'` (`blasts/Paralysis.dm:23`), de dois
		// arquivos diferentes: `Ki2.0/Debuffs.dm:18` e `Skill Trees/Race Trees/meta.dm:74`.
		["Paralysis"] = new(Fonte.Literal, ArteDeKi.KiHead),
		["Stunlock"] = new(Fonte.Literal, ArteDeKi.KiHead),

		// ---------------------------------------------------------------
		// LOTE G6 -- os seis raios nomeados e o Kikoho
		// ---------------------------------------------------------------
		// O UNICO SORTEADO DO JOGO. `rand(1,6)` no `after_learn` (`beams/Kamehameha.dm:12-21`).
		["Kamehameha"] = new(Fonte.SorteioDeKamehameha, ArteDeKi.Kamehameha1),

		["GalicGun"] = new(Fonte.Literal, ArteDeKi.GalacticGun),         // `beams/GalicGun.dm:22,34`
		["Death_Beam"] = new(Fonte.Literal, ArteDeKi.Makkankosappo3),    // `beams/DeathBeam.dm:22,34`
		["Dodompa"] = new(Fonte.Literal, ArteDeKi.Dodompa),              // `beams.dm:442`
		["Enkumei"] = new(Fonte.Literal, ArteDeKi.Enkumei),              // `beams/Enkumei.dm:35`
		["Boom_Wave"] = new(Fonte.Literal, ArteDeKi.Beam4),              // `beams.dm:486`
		["Kikoho"] = new(Fonte.Literal, ArteDeKi.Kikoho),                // `blasts/Kikoho.dm:56`

		// ---------------------------------------------------------------
		// LOTE G7
		// ---------------------------------------------------------------
		["Scattering_Bullet"] = new(Fonte.Literal, ArteDeKi.Blast28),    // `blasts.dm:574`
		["Spirit_Gun"] = new(Fonte.Literal, ArteDeKi.BlastSpiralingKi),  // `Core Trees/Spirit.dm:355`

		// ---------------------------------------------------------------
		// O SOPRO -- a lamina de ar do Kiai (`Ki2.0/Kiai.dm:34-40`)
		// ---------------------------------------------------------------
		// ESTA LINHA FICOU DE FORA POR UMA LEITURA ERRADA, e vale registrar qual: a nota que morava
		// aqui dizia que "o Kiai cria o blast e nunca escreve `icon`". Escreve -- `bcolor =
		// 'Daitoppa.dmi'; bcolor += rgb(blastR, blastG, blastB); A.icon = bcolor` (`:34-39`) -- e
		// depois poe `A.invisibility = 1` (`:40`). Sao DUAS decisoes: a folha e o Daitoppa tingido de
		// ki, e ela e invisivel pra quem nao tem `see_invisible`. A arte mora aqui; a invisibilidade
		// e outro dado (`ReceitaDeProjetil.Invisivel`), porque um dia outra tecnica pode nascer
		// invisivel com outra folha. O dono viu a primitiva colorida voando e disse que estava errado
		// (2026-09-07): estava.
		["Kiai"] = new(Fonte.Literal, ArteDeKi.Daitoppa),

		// ---------------------------------------------------------------
		// OS SETE QUE VOAVAM SEM ARTE (lotes G11 e G12)
		// ---------------------------------------------------------------
		// Entraram em producao sem linha aqui e por isso caiam no desenho neutro -- inclusive as duas
		// maiores bolas do jogo. Conferidos um a um no DM em 2026-10-07, quando o desenho virou shader
		// e o dono pediu "esferas como genkidama, super nova, blast":
		["Death_Ball"] = new(Fonte.Literal, ArteDeKi.DeathBall2017Purple2),  // `blasts/DeathBall.dm:53`
		["SpiritBomb"] = new(Fonte.Literal, ArteDeKi.SpiritBomb),            // `blasts/SpiritBomb.dm:25,45`

		// As tres rajadas saem do `Create_Blast()`, que sem icone usa `BLASTICON` (`tools/copypaste.dm:12-13`)
		// -- a bola da raca, como o tiro de todo dia.
		["BusterBarrage"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),              // `blasts/BusterBarrage.dm:43,60`
		["Continuous_Energy_Bullets"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),  // `blasts.dm:278`
		["Spin_Blast"] = new(Fonte.BolaRacial, ArteDeKi.Nenhuma),                 // `blasts.dm:348`

		// `bcolor = usr.ParalysisIcon` + `icon_state = "Paralysis"` (`click.dm:18,26`): o fio e a mesma
		// faisca das duas paralisias.
		["Psycho_Thread"] = new(Fonte.Literal, ArteDeKi.KiHead),

		// `/obj/training_obj/Ki_Target { icon = '14.dmi' }` (`Stats/Training/Meditate.dm:26`).
		["Ki_Targets"] = new(Fonte.Literal, ArteDeKi.Blast14),
	};

	/// <summary>
	/// A ARTE DESTE TIRO. Ponto de entrada unico -- ver o cabecalho.
	///
	/// Verb desconhecido devolve <see cref="ArteDeKi.Nenhuma"/> e o tiro sai no estilo NEUTRO do tipo
	/// dele. **Recusar em silencio e a resposta certa aqui**: a alternativa (chutar a bola racial pra
	/// todo verb novo) poria arte errada numa tecnica futura sem ninguem notar, e este projeto ja tem
	/// o registro de um "campo morto" que ficou anos assim.
	/// </summary>
	/// <param name="semente">
	/// A semente do PERSONAGEM -- `LimiaresPessoais.SementeDe(nome, criadoEm)`. So o Kamehameha a
	/// usa. Ver <see cref="SorteioDoKamehameha"/>.
	/// </param>
	public static ArteDeKi De(string verbo, string raca, string classe, ulong semente)
	{
		if (verbo.Length == 0 || !Tudo.TryGetValue(verbo, out Regra r)) return ArteDeKi.Nenhuma;

		return r.De switch
		{
			Fonte.BolaRacial => Bola(raca, classe),
			Fonte.SorteioDeKamehameha => SorteioDoKamehameha(semente),
			_ => r.Fixa,
		};
	}

	// ============================ NAO HA `Conhece`, `Verbos` NEM `RegraDe` ============================
	// Escrevi os tres ("pra a bancada varrer a tabela") e apaguei os tres, porque a bancada NAO os
	// usa e nem devia: a lista de verbos dela e escrita a mao (ver `RoboDeArteDeKi.VerbosDeProducao`
	// e o comentario de la), justamente pra que varrer a tabela nao vire conferir a tabela com ela
	// mesma. Sobraram tres metodos publicos sem chamador nenhum -- a mesma "API orfa" que o bloco do
	// `Fonte` acima diz que este projeto ja pagou uma vez. Quem precisar, escreve na hora.
	// =============================================================================================

	// =====================================================================
	// A IDENTIDADE DA FOLHA NO ORIGINAL
	// =====================================================================
	/// <summary>
	/// ONDE ESTA FOLHA MORAVA NO BYOND: a PASTA de `Icons/` e o nome do `.dmi`, sem extensao.
	///
	/// ============================ ELA NAO E MAIS UM ARQUIVO QUE SE CARREGA ============================
	/// Ninguem abre estas folhas pra desenhar tiro (ver o cabecalho de <see cref="ArteDeKi"/>). O que
	/// sobrou e o que elas sempre foram no original, e as duas coisas tem leitor:
	///
	///   * o NOME que o jogador ve na mesa de tecnicas -- o `"[prefix]: [f]"` do `pick_game_icon`
	///     (`customattacks.dm:551`), literalmente `"Blasts: 12.dmi"`;
	///   * a PASTA, que e REGRA: e ela que diz quais artes cada tipo de tecnica customizada pode
	///     escolher (<see cref="PermitidasPara"/>, validado no servidor).
	///
	/// A PASTA IMPORTA, e nao e decoracao: `Makkankosappo` e `Beam - Big Fire` existem em DUAS
	/// pastas com conteudo diferente, e quem desempata e a ordem dos `FILE_DIR` do `.dme`. Ver o
	/// cabecalho de <see cref="ArteDeKi"/>.
	/// ========================================================================================
	/// </summary>
	public static (string Pasta, string Arquivo) Folha(ArteDeKi a) => a switch
	{
		// --- Icons/Beams (FILE_DIR linha 7) ---
		ArteDeKi.Beam3 => ("Beams", "Beam3"),
		ArteDeKi.Beam2 => ("Beams", "Beam2"),
		ArteDeKi.Beam4 => ("Beams", "Beam4"),
		ArteDeKi.Beam11 => ("Beams", "Beam11"),
		ArteDeKi.BeamMasenko => ("Beams", "BeamMasenko"),
		ArteDeKi.BeamStaticBeam => ("Beams", "BeamStaticBeam"),
		ArteDeKi.BeamBigFire => ("Beams", "Beam - Big Fire"),
		ArteDeKi.EraserCannon => ("Beams", "EraserCannon"),
		ArteDeKi.Makkankosappo => ("Beams", "Makkankosappo"),

		// --- Icons/Blasts (FILE_DIR linha 9) ---
		ArteDeKi.Blast1 => ("Blasts", "1"),
		ArteDeKi.Blast5 => ("Blasts", "5"),
		ArteDeKi.Blast6 => ("Blasts", "6"),
		ArteDeKi.Blast7 => ("Blasts", "7"),
		ArteDeKi.Blast8 => ("Blasts", "8"),
		ArteDeKi.Blast10 => ("Blasts", "10"),
		ArteDeKi.Blast11 => ("Blasts", "11"),
		ArteDeKi.Blast12 => ("Blasts", "12"),
		ArteDeKi.Blast14 => ("Blasts", "14"),
		ArteDeKi.Blast18 => ("Blasts", "18"),
		ArteDeKi.Blast19 => ("Blasts", "19"),
		ArteDeKi.Blast20 => ("Blasts", "20"),
		ArteDeKi.Blast28 => ("Blasts", "28"),
		ArteDeKi.Daitoppa => ("Blasts", "Daitoppa"),
		ArteDeKi.Blast30 => ("Blasts", "30"),
		ArteDeKi.Blast31 => ("Blasts", "31"),
		ArteDeKi.Blast35 => ("Blasts", "35"),
		ArteDeKi.DeathBall2017Purple2 => ("Blasts", "deathball2017purple2"),
		ArteDeKi.BlastSpiralingKi => ("Blasts", "Blast - Spiraling Ki"),

		// --- Icons/Techniques (FILE_DIR linha 51) ---
		ArteDeKi.Kamehameha1 => ("Techniques", "Kamehameha1"),
		ArteDeKi.Kamehameha2 => ("Techniques", "Kamehameha2"),
		ArteDeKi.Kamehameha3 => ("Techniques", "Kamehameha3"),
		ArteDeKi.Kamehameha4 => ("Techniques", "Kamehameha4"),
		ArteDeKi.Kamehameha5 => ("Techniques", "Kamehameha5"),
		ArteDeKi.Kamehameha6 => ("Techniques", "Kamehameha6"),
		ArteDeKi.GalacticGun => ("Techniques", "galacticgun"),
		ArteDeKi.Makkankosappo3 => ("Techniques", "Makkankosappo3"),
		ArteDeKi.Makkankosappo4 => ("Techniques", "Makkankosappo4"),
		ArteDeKi.Dodompa => ("Techniques", "Dodompa"),
		ArteDeKi.Enkumei => ("Techniques", "Enkumei"),
		ArteDeKi.BasenioBlast => ("Techniques", "BasenioBlast"),
		ArteDeKi.Kikoho => ("Techniques", "Kikoho"),
		ArteDeKi.Kienzan => ("Techniques", "Kienzan"),
		ArteDeKi.KiHead => ("Techniques", "KiHead"),

		// --- fora de `Icons/`: uma imagem solta (e `.png`, nao `.dmi` -- por isso a extensao vem escrita),
		//     que nenhuma tecnica customizada alcanca ---
		ArteDeKi.SpiritBomb => ("Images/attacks", "SpiritBomb22017.png"),

		_ => ("", ""),
	};

	/// <summary>
	/// A ARTE DE UMA TECNICA CUSTOMIZADA QUE NAO ESCOLHEU NENHUMA -- os dois `else` do DM:
	///
	///   * BEAM          -- `usr.forceicon = usr.beamicon` (`customattacks.dm:440`), e `beamicon`
	///                      padrao e `'Beam3.dmi'` (`beams.dm:4`);
	///   * BLAST/GUIDED  -- `A.icon = '12.dmi'` (`customattacks.dm:500`), colorida pelo ki do dono.
	///
	/// A bola padrao do custom NAO e a `BLASTICON` da raca, e vale dizer por que: o `CustomMakeBlast`
	/// escreve `'12.dmi'` na unha e nunca le a var racial. Cair no racial "porque faz sentido"
	/// daria a um Android uma tecnica inventada com a arte de bola de Android -- coisa que o
	/// original nao faz, e que apagaria a unica pista visual de que aquilo e uma tecnica de autor.
	/// </summary>
	public static ArteDeKi PadraoDoCustom(TipoDeProjetil tipo) =>
		tipo == TipoDeProjetil.Beam ? ArteDeKi.Beam3 : ArteDeKi.Blast12;

	// ============================ NAO HA MAIS `EstadoDeBola` ============================
	// Morava aqui o `icon_state` de cada bola (`"paralysis"` pra `KiHead.dmi`, `"default"` pro resto),
	// e o unico leitor era quem procurava a animacao na folha. Sem folha nao ha estado: a paralisia
	// e reconhecida pela ARTE (`KiHead`), e o estilo dela e a faisca -- ver `ArteDeKiNoCliente`.
	// ====================================================================================

	/// <summary>
	/// QUANTO A CABECA DESTA ARTE AVANCA A FRENTE DA POSICAO DO TIRO, em pixel, na escala 1.
	///
	/// ============================ ISTO E REGRA, E O DESENHO OBEDECE ============================
	/// O servidor planta a cabeca de um raio encostada em quem ela empurra, e encontra duas cabecas
	/// numa disputa, por ESTE numero (`Feixe.AlcanceDaCabeca`). E o cliente desenha a ponta do raio
	/// exatamente ai: `ProjetilDesenhado` poe o fim do feixe em `posicao + rumo * frente * escala`.
	/// Uma tabela, dois leitores -- o que se ve e o que encosta.
	///
	/// A DIRECAO DA DEPENDENCIA INVERTEU, e vale registrar. Estes numeros nasceram MEDIDOS: um script
	/// lia o alpha de `head_east` de cada folha (16 em quase todas, porque a arte ia ate a borda da
	/// celula de 32) e a tabela dizia ao servidor o que a folha ja desenhava. Foi assim que se achou o
	/// Final Flash -- celula de 64, escala 4, a cabeca avanca 128 px -- cobrindo quem ele empurrava
	/// (dono, 2026-09-23: *"as cabecas ainda estao se sobrepondo as vezes"*). Desde 2026-10-07 nao ha
	/// folha: o numero e o mesmo, mas agora e ele quem manda, e o shader quem acompanha. A
	/// `--diagartedeki` (familia 6) confere um contra o outro, na FOTO, arte por arte.
	/// ================================================================================================
	/// </summary>
	public static float FrenteDaCabeca(ArteDeKi a) => a switch
	{
		ArteDeKi.BeamBigFire => 32f,    // o Final Flash: a cabeca e do tamanho do tronco, e ele e enorme
		ArteDeKi.EraserCannon => 21f,   // a bola da frente e maior que a de um raio comum
		ArteDeKi.Dodompa => 14f,        // a seta fina para 2 px antes
		ArteDeKi.Makkankosappo => 0f,   // a broca e desenhada toda PRA TRAS da posicao: a ponta E a posicao
		_ => Projetil.RaioDeImpacto,    // meio tile -- todos os outros
	};

	/// <summary>
	/// A MEIA ESPESSURA DO TRONCO DESTA ARTE, em pixel, na escala 1: ate onde, de lado do eixo, o feixe
	/// AINDA E desenhado. Quem encosta no tronco (o corte) e quem bate nele (o cruzamento) encosta na
	/// BEIRADA dele, nao no eixo -- e por isso e regra, e por isso o desenho a obedece
	/// (`ProjetilDesenhado` le daqui o raio do tronco, do mesmo jeito que le a frente da cabeca).
	///
	/// ============================ OITO FIOS ENGROSSARAM, E NADA MUDOU NO JOGO ============================
	/// As folhas do BYOND tinham raios de 4 a 6 px de espessura (meia espessura 2 e 3). Era o que o
	/// dono viu no trailer -- riscos finos entre duas cabecas grandes --, e um feixe de shader desse
	/// calibre e um fio de neon. Os oito mais finos subiram 1 ou 2 px:
	///
	///     Beam3 4->5   Beam4 2->3   Kamehameha1 3->5   Kamehameha3 3->5
	///     Makkankosappo3 2->3   Makkankosappo4 2->3   Dodompa 2->3   Enkumei 3->4
	///
	/// NENHUM RESULTADO DE COMBATE MUDA: a faixa do corte e `max(16, meia + 8)` (`GameServer.Feixe`), e
	/// todos os oito continuam abaixo de 8 -- a faixa era 16 e segue 16. O unico leitor que os sente e
	/// o raio de espera do cruzamento (`frente + meia`), que anda os mesmos 1 ou 2 px.
	/// ============================================================================================
	///
	/// NAS ARTES COM ENFEITE (as descargas do Static Beam, os fiapos do Kamehameha 5, os aneis do
	/// Makkankosappo) este numero e ate onde o ENFEITE chega; o tronco macico e mais fino, e a fracao
	/// esta no estilo do cliente (`EstiloDeFeixe.Macico`).
	/// </summary>
	public static float MeiaEspessuraDoTronco(ArteDeKi a) => a switch
	{
		ArteDeKi.Beam3 => 5f,
		ArteDeKi.Beam2 => 7f,
		ArteDeKi.Beam4 => 3f,
		ArteDeKi.Beam11 => 7f,
		ArteDeKi.BeamMasenko => 8f,
		ArteDeKi.BeamStaticBeam => 13f,
		ArteDeKi.BeamBigFire => 27f,
		ArteDeKi.Makkankosappo => 5f,
		ArteDeKi.Kamehameha1 => 5f,
		ArteDeKi.Kamehameha2 => 5f,
		ArteDeKi.Kamehameha3 => 5f,
		ArteDeKi.Kamehameha4 => 13f,
		ArteDeKi.Kamehameha5 => 13f,
		ArteDeKi.Kamehameha6 => 7f,
		ArteDeKi.GalacticGun => 7f,
		ArteDeKi.Makkankosappo3 => 3f,
		ArteDeKi.Makkankosappo4 => 3f,
		ArteDeKi.Dodompa => 3f,
		ArteDeKi.Enkumei => 4f,
		_ => Projetil.RaioDeImpacto / 2f,   // a EraserCannon, o raio sem arte e as bolas: meio raio
	};

	/// <summary>Toda folha conhecida -- pra a bancada varrer e pro menu da tecnica customizada.</summary>
	public static IEnumerable<ArteDeKi> Todas =>
		Enum.GetValues<ArteDeKi>().Where(a => a != ArteDeKi.Nenhuma);

	/// <summary>
	/// AS FOLHAS QUE A TECNICA CUSTOMIZADA PODE ESCOLHER, por tipo -- o `custom_icon_folders` do DM
	/// (`customattacks.dm:558-562`), literal:
	///
	///     Beam   -> `Icons/Beams` + `Icons/Techniques`
	///     Blast  -> SO `Icons/Blasts`
	///     Guided -> SO `Icons/Techniques`
	///
	/// O RECORTE E DO ORIGINAL E NAO ARBITRARIO: um raio precisa de folha com `origin`/`tail`/`head`
	/// (so `Beams` e `Techniques` tem), e uma bola precisa de um desenho unico. Deixar o jogador
	/// pendurar `Kamehameha3.dmi` numa bola daria uma bola desenhada com a CAUDA de um raio.
	///
	/// A lista do DM e a pasta INTEIRA (`flist`); a daqui e o que este port converteu e conferiu --
	/// ver a nota do fim do arquivo sobre as folhas que existem no disco e ainda nao tem entrada.
	/// </summary>
	public static IEnumerable<ArteDeKi> PermitidasPara(TipoDeProjetil tipo)
	{
		string[] pastas = tipo switch
		{
			TipoDeProjetil.Beam => ["Beams", "Techniques"],
			TipoDeProjetil.Blast => ["Blasts"],
			_ => ["Techniques"],
		};
		return Todas.Where(a => pastas.Contains(Folha(a).Pasta));
	}

	// =====================================================================
	// AS QUATRO FOLHAS RACIAIS (`race.dm:57-66` + os quatro `stat*.dm` que sobrescrevem)
	// =====================================================================
	/// <summary>
	/// `WaveIcon` -- O RAIO DA RACA. `race.dm:57` da `Beam3.dmi` a todo mundo e SO o Demigod da
	/// linhagem Demigod troca (`statdemi.dm:9`, `Beam2.dmi`); Ogre reafirma o padrao (`:24`).
	///
	/// **NENHUM VERB PORTADO CHEGA AQUI** -- ver o bloco do `WaveIcon` no cabecalho da classe. Ela
	/// existe porque o campo existe e esta certo, e porque o dia em que alguem portar um caminho de
	/// `bypass = 0` (o `GenericBeamCode.dm:15` e o `Cyborgs.dm:1123` sao os dois candidatos) a
	/// resposta ja esta escrita e conferida contra o original.
	/// </summary>
	public static ArteDeKi Onda(string raca, string classe) =>
		raca == "Demigod" && classe is not ("Ogre" or "Genie") ? ArteDeKi.Beam2 : ArteDeKi.Beam3;

	/// <summary>
	/// `BLASTICON` -- A BOLA DA RACA, e a camada 1 do DM na pratica: e ela que faz `Basic_Blast`,
	/// `Scattershot`, `Energy_Barrage`, `Ki_Bomb` e `Hellzone_Grenade` sairem diferentes de raca pra
	/// raca. Padrao `1.dmi` (`race.dm:62`).
	/// </summary>
	public static ArteDeKi Bola(string raca, string classe) => raca switch
	{
		"Android" => ArteDeKi.Blast10,     // `statandroid.dm:9`
		"Tsujin" => ArteDeKi.Blast5,       // `stattsujin.dm:6`
		"Yardrat" => ArteDeKi.Blast7,      // `statyardrat.dm:7`
		"Demigod" => classe switch
		{
			"Ogre" => ArteDeKi.Blast31,    // `statdemi.dm:29`
			"Genie" => ArteDeKi.Blast19,   // `statdemi.dm:45`
			_ => ArteDeKi.Blast1,          // `statdemi.dm:13` -- a linhagem Demigod repete o padrao
		},
		_ => ArteDeKi.Blast1,              // `race.dm:62`
	};

	/// <summary>
	/// `CBLASTICON` -- A BOLA CARREGADA DA RACA. Padrao `18.dmi` (`race.dm:64`).
	///
	/// **NENHUM VERB PORTADO A USA**, e o motivo e um so: o unico consumidor no DM e o
	/// `Energy_Shot` (`blasts.dm:630`), que nao esta no port. O `Charged_Shot`, que pelo nome seria
	/// o candidato obvio, escreve `'20.dmi'` na unha (`blasts.dm:103`) -- foi conferido justamente
	/// porque a suposicao contraria era natural.
	/// </summary>
	public static ArteDeKi BolaCarregada(string raca, string classe) => raca switch
	{
		"Android" => ArteDeKi.Blast11,     // `statandroid.dm:10`
		"Tsujin" => ArteDeKi.Blast6,       // `stattsujin.dm:7`
		"Yardrat" => ArteDeKi.Blast8,      // `statyardrat.dm:8`
		"Demigod" => classe switch
		{
			"Ogre" => ArteDeKi.Blast35,    // `statdemi.dm:31`
			"Genie" => ArteDeKi.Blast20,   // `statdemi.dm:46`
			_ => ArteDeKi.Blast18,         // `statdemi.dm:15`
		},
		_ => ArteDeKi.Blast18,             // `race.dm:64`
	};

	/// <summary>
	/// `Makkankoicon` -- a folha do Special Beam Cannon de verdade (`beams/SpecialBeamCannon.dm`),
	/// que **nao esta portado**. Padrao `Makkankosappo4.dmi` (`race.dm:66`).
	///
	/// Repare que o valor declarado no arquivo da skill (`SpecialBeamCannon.dm:23`,
	/// `Makkankosappo.dmi`) NAO e o que vale: `finalize_Race` sobrescreve todo mundo no nascimento.
	/// Ler so a declaracao da skill teria dado a folha errada pra 100% dos personagens.
	/// </summary>
	public static ArteDeKi Makkanko(string raca) => raca switch
	{
		"Tsujin" => ArteDeKi.Makkankosappo3,    // `stattsujin.dm:11`
		"Yardrat" => ArteDeKi.Makkankosappo,    // `statyardrat.dm:11`
		_ => ArteDeKi.Makkankosappo4,           // `race.dm:66` e `statdemi.dm:54`
	};

	// =====================================================================
	// O SORTEIO
	// =====================================================================
	/// <summary>As seis folhas do `rand(1,6)`, na ordem do `beams/Kamehameha.dm:15-20`.</summary>
	private static readonly ArteDeKi[] SeisKamehamehas =
	[
		ArteDeKi.Kamehameha1, ArteDeKi.Kamehameha2, ArteDeKi.Kamehameha3,
		ArteDeKi.Kamehameha4, ArteDeKi.Kamehameha5, ArteDeKi.Kamehameha6,
	];

	/// <summary>Quantas folhas o sorteio tem. A bancada cobra as seis.</summary>
	public static int QuantosKamehamehas => SeisKamehamehas.Length;

	/// <summary>
	/// O KAMEHAMEHA DESTE PERSONAGEM -- `rand(1,6)`, uma vez, pra sempre.
	///
	/// ============================ POR QUE DERIVADO E NAO UM CAMPO NO SAVE ============================
	/// No DM o numero e sorteado no `after_learn` e gravado numa `mob/var` nao-`tmp`
	/// (`Kamehamehaicon`, `beams/Kamehameha.dm:29`), que viaja no savefile pelo `..()` do
	/// `mob/Write`. Duas propriedades importam, e so duas: **e diferente entre personagens** e
	/// **nunca muda depois de escolhido**.
	///
	/// Uma FUNCAO PURA da identidade do personagem entrega as duas, e este repo ja usou exatamente
	/// esta saida uma vez -- e ela esta documentada em <see cref="Appearance.CorDeAura.De"/>: nome +
	/// instante de criacao e a dupla que TODO save tem, inclusive os anteriores a esta
	/// funcionalidade. Sem campo novo: sem ramo de migracao, sem `??=`, e sem o modo de falha que
	/// este projeto ja pagou duas vezes ("campo novo que se lista a mao some do disco, calado" --
	/// ver o bloco dos `Limiares` em `CharacterStore.DoJogador`).
	///
	/// E ha um motivo a mais, que e de ENCANAMENTO: o `after_learn` do DM e UM ponto de entrada, e
	/// aqui aprender skill entra por varios (`Aprender`, `SkillBook.Dar`, `DarComoEnsinada`, o
	/// caminho do ensino, o do cargo, o do admin). Um gancho de sorteio teria que ser plugado em
	/// todos, e o que ficasse de fora daria Kamehameha1 calado pra um caminho inteiro de jogadores.
	/// Aqui a pergunta so e feita no DISPARO, e no disparo a resposta ja existe.
	///
	/// O QUE SE PERDE, e esta anotado: no DM, ESQUECER e reaprender o Kamehameha RE-SORTEIA a folha.
	/// Aqui nao -- a arte e do personagem, nao da instancia da skill. Ninguem pediu o re-sorteio, e
	/// ele seria indistinguivel de um bug pra quem gostou da arte que tinha.
	///
	/// O DIA EM QUE HOUVER ESCOLHA DE JOGADOR, o campo entra em `Appearance` (que ja viaja inteiro
	/// no `CharacterSave.Visual` e por isso nao pode ser esquecido no `DoJogador`), anulavel, com a
	/// mesma semantica do <see cref="Appearance.Appearance.CorAura"/>: nulo = este sorteio.
	/// =========================================================================================
	/// </summary>
	public static ArteDeKi SorteioDoKamehameha(ulong semente)
	{
		// O `Hash64("kamehameha")` no meio e o "um gerador por campo" do `LimiaresPessoais.Sorteador`
		// e do `CorDeAura.DeSemente`: sem ele, dois sistemas que sorteassem da semente crua do mesmo
		// personagem andariam juntos pra sempre (quem tem aura branca teria sempre Kamehameha1).
		ulong s = World.Espaco.Misturar(
			semente, World.Espaco.Hash64("kamehameha"), 0x51ED_2C97_A3B4_D60FUL);
		var r = new Random(unchecked((int)(s ^ (s >> 32))));
		return SeisKamehamehas[r.Next(SeisKamehamehas.Length)];
	}

	// =====================================================================
	// A CARGA DO RAIO -- o `ChargeState` do DM
	// =====================================================================
	/// <summary>
	/// QUAL DOS NOVE DESENHOS DE CARGA ESTE CORPO ACENDE ao reunir energia pra um raio.
	///
	/// ============================ O QUE ELE E NO DM ============================
	/// `ChargeState` e uma `mob/var` de TEXTO (`Ki Attacks.dm:2`, padrao `"1"`) e o seu unico
	/// consumidor e o overlay de carga: `I.icon = 'BlastCharges.dmi'` / `I.icon_state = ChargeState`
	/// (`Skills/Ki/tools/mobhandler.dm:5-7` e a copia `:21-23`). Ele nasce no `finalize_Race` com
	/// `var/chargo = rand(1,9)` (`race.dm:60-61`), e QUATRO ramos raciais o cravam por cima:
	/// Android `"2"` (`statandroid.dm:6`), Yardrat `"5"` (`statyardrat.dm:4`), Tsujin `"6"`
	/// (`stattsujin.dm:9`), e o Demigod POR LINHAGEM -- Demigod `"8"` (`statdemi.dm:11`), Genie
	/// `"9"` (`statdemi.dm:40`) e Ogre que **re-sorteia** o proprio `rand(1,9)` (`statdemi.dm:28`).
	/// E a mesma taxonomia do <see cref="Bola"/> logo acima, com a mesma ordem de precedencia.
	/// ==========================================================================
	///
	/// ============================ POR QUE DERIVADO, E NAO UM CAMPO NO SAVE ============================
	/// Pelas MESMAS duas razoes escritas no <see cref="SorteioDoKamehameha"/>, e nao por economia:
	/// o numero precisa ser **diferente entre personagens** e **nunca mudar**, e uma funcao pura da
	/// identidade (nome + instante de criacao) entrega as duas sem campo novo -- logo sem ramo de
	/// migracao e sem o modo de falha do "campo novo que se lista a mao some do disco, calado".
	///
	/// E aqui ha um ganho que o Kamehameha nao tem: **vale pra quem nao tem save**. NPC, clone da
	/// meditacao e corpo de bancada tem nome e tem `CriadoEm`, entao caem na mesma conta e acendem
	/// a mesma carga em toda sessao. Um campo persistido teria deixado justamente esses tres com o
	/// `"1"` padrao pra sempre -- e a IA atira.
	/// =============================================================================================
	///
	/// O QUE SE PERDE, e esta anotado: o Ogre do DM re-sorteia (`statdemi.dm:28`) com um `rand`
	/// SEPARADO do da raca, ou seja, no original ele pode calhar de repetir o numero do sorteio
	/// geral. Aqui ele cai no mesmo sorteio -- a diferenca e invisivel (os dois sao `rand(1,9)`
	/// sobre o mesmo corpo) e ter um segundo gerador so pra ele seria complexidade sem leitor.
	/// </summary>
	/// <param name="semente">
	/// A semente do PERSONAGEM -- `LimiaresPessoais.SementeDe(nome, criadoEm)`, a mesma que a cor de
	/// aura e o Kamehameha ja consomem.
	/// </param>
	/// <returns>De 1 a 9 -- os nomes dos estados de `BlastCharges` (o `8` e o unico sem direcao).</returns>
	public static int CargaDeRaio(string raca, string classe, ulong semente) => raca switch
	{
		"Android" => 2,                     // `statandroid.dm:6`
		"Yardrat" => 5,                     // `statyardrat.dm:4`
		"Tsujin" => 6,                      // `stattsujin.dm:9`
		"Demigod" => classe switch
		{
			"Genie" => 9,                   // `statdemi.dm:40`
			"Ogre" => SorteioDaCarga(semente),  // `statdemi.dm:28` -- re-sorteia, ver acima
			_ => 8,                         // `statdemi.dm:11`
		},
		_ => SorteioDaCarga(semente),       // `race.dm:60-61`
	};

	/// <summary>
	/// O `rand(1,9)` do `race.dm:60`, por funcao pura.
	///
	/// O `Hash64("chargestate")` no meio e o "um gerador por campo" que o <see cref="SorteioDoKamehameha"/>
	/// explica: sem ele, dois sistemas sorteando da semente CRUA do mesmo personagem andariam juntos
	/// pra sempre -- quem tem Kamehameha1 teria sempre a carga 1.
	/// </summary>
	private static int SorteioDaCarga(ulong semente)
	{
		ulong s = World.Espaco.Misturar(
			semente, World.Espaco.Hash64("chargestate"), 0x9E37_79B9_7F4A_7C15UL);
		var r = new Random(unchecked((int)(s ^ (s >> 32))));
		return r.Next(1, 10);   // `rand(1,9)` do DM inclui o 9
	}
}
