using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Core.Stats;

/// <summary>
/// TUDO que entra na conta de poder de um personagem. E o `mob` do BYOND reduzido ao que
/// as duas contas grandes leem: <see cref="Statify"/> (stat cru -> stat efetivo) e
/// <see cref="PowerLevel"/> (BP base -> BP expresso).
///
/// OS NOMES SAO OS DO DM DE PROPOSITO. `ssjBuff`, `nnetBuff`, `Ephysoff`, `bp_remove` --
/// nenhum foi "arrumado" pra convencao de C#. A razao e pratica: essas duas contas tem
/// dezenas de fatores e a ordem entre eles (multiplica / soma na BASE / aplica no FIM) e
/// justamente o que nao pode sair errado. Com os nomes iguais da pra por o `base.dm` do
/// lado e conferir linha a linha; renomeando, nao da.
///
/// Fica no Core (assembly puro, sem Godot): servidor e cliente rodam a MESMA conta, que e
/// o que "cliente calcula, servidor valida" exige.
/// </summary>
public sealed partial class Fighter
{
	// =====================================================================
	// IDENTIDADE
	// =====================================================================
	public string Name = "";
	public string Race = "";
	public string Class = "None";
	public string SaiyanLineage = "";

	// =====================================================================
	// BP e vida
	// =====================================================================
	public double BP = 1;             // o poder REAL, o que o treino aumenta
	public double BPMod = 1;          // multiplicador racial/de classe (vem do proto)
	public double BPadd = 0;          // soma que nao vira BP real
	public double HP = 100;
	public double Ki = 100;
	public double MaxKi = 100;
	public double stamina = 100;
	public double maxstamina = 100;
	public double staminadeBuff = 100;
	public bool dead, KO, IsInFight, dashing, isconcealed;

	/// <summary>
	/// MORREU DE VELHICE -- `mob/var/aged_out` (`Aging.dm:177`, `Death.dm:144`).
	///
	/// Nao e "esta morto" (isso e o <see cref="dead"/>): e COMO se morreu, e serve a uma regra so,
	/// que o proprio DM escreve em comentario na linha em que liga a marca -- "morte de VELHICE: sem
	/// revive por NENHUM meio, so a reencarnacao do Enma". O `ReviveMe()` do original testa este
	/// campo antes de qualquer coisa (`Death.dm:144`).
	///
	/// PERSISTE porque a ficha inteira vai pro disco (ver `CharacterSave.Ficha`), e tem que
	/// persistir: se ele se perdesse no logout, deslogar e voltar seria o jeito barato de
	/// transformar uma morte definitiva numa morte comum.
	/// </summary>
	public bool aged_out;

	/// <summary>
	/// QUANTAS VEZES ESTA ALMA JA VOLTOU DOS MORTOS -- `mob/var/ResurrectedCount`
	/// (`Ranks/rankSkills/OtherworldRankSkills.dm:269-270`).
	///
	/// ============================ ELE E O PRECO INTEIRO DO `Revive` DE CARGO ============================
	/// O verb de cargo (`OtherworldRankSkills.dm:217-267`) nao cobra Ki nenhum. O unico preco esta em
	/// duas linhas (`:237` e `:241-245`): ele soma 1 aqui e, **se o numero passar de 1**, quem
	/// ressuscitou cai morto no lugar -- *"[usr] trades [usr]'s life for the resurrection!"*.
	///
	/// A contagem e do RESSUSCITADO e nao de quem ressuscita, e essa e a regra: a primeira volta de
	/// uma alma e de graca pra qualquer Kaio; a segunda custa uma vida, seja qual for o Kaio. O DM
	/// tambem nunca zera este campo -- nem no `ReviveMe()`, nem na reencarnacao --, entao ele e a
	/// memoria permanente de quantas vezes o mundo ja abriu excecao pra esta pessoa.
	///
	/// PERSISTE de graca (a ficha inteira vai pro disco, ver `CharacterSave.Ficha`), e tem que
	/// persistir pelo mesmo motivo do <see cref="aged_out"/>: se morresse no logout, deslogar seria o
	/// jeito barato de comprar mais uma ressurreicao gratuita.
	/// ================================================================================================
	/// </summary>
	public int ResurrectedCount;

	// ============================ AS TRES MARCAS QUE AS ESFERAS DEIXAM NA ALMA ============================
	// Elas moram aqui, ao lado do `aged_out`, porque sao a mesma classe de coisa: fatos PERMANENTES de
	// um personagem, que persistem de graca (a ficha inteira vai pro disco) e que **tem que**
	// persistir -- se qualquer uma se perdesse no logout, deslogar seria o jeito barato de burlar a
	// regra que ela existe pra impor. O DM as declara juntas, num bloco `mob/var` so
	// (`WishTable.dm:22-25`), e pelo mesmo motivo.
	// ==================================================================================================

	/// <summary>
	/// **FALA A LINGUA DOS DEUSES** -- `mob/var/godtongue` (`WishTable.dm:23`).
	///
	/// Booleano cru, e nao skill nem no de arvore nem campo de raca: e assim no original, e copiar a
	/// FORMA dele e o que a tarefa pediu. Ver <see cref="Magic.LinguaDosDeuses"/> pros dois eixos que o
	/// ligam (cargo divino e sangue Kai/Demigod).
	///
	/// **ELE NUNCA VOLTA A ZERO.** `if(godtongue) return 1` e a primeira linha do `godtongue_check`
	/// (:38): perder o cargo nao tira a lingua. Nao ha, em lugar nenhum deste port, uma linha que
	/// escreva `false` aqui -- e essa ausencia e a regra, nao um esquecimento.
	/// </summary>
	public bool godtongue;

	/// <summary>
	/// **A DIVIDA DO "MAIS FORTE DO UNIVERSO"** -- `mob/var/sw_doom_year` (`WishTable.dm:25`), em
	/// segundos do relogio do mundo. Zero = sem divida.
	///
	/// ============================ O DESEJO NAO E UM BUFF: E UMA SENTENCA ============================
	/// `sw_strongest_wish` (:102-113) poe o BP no DOBRO do maior do jogo **e** marca o vencimento um ano
	/// depois. Quando ele chega (`Aging.dm:114-122`), o personagem recebe `aged_out = 1` e morre -- a
	/// unica morte que **nem as Super Esferas desfazem** (seis guardas no DM, ver `aged_out`).
	///
	/// Portar o multiplicador sem portar este campo transformaria o desejo mais caro do jogo num buff
	/// gratuito. E o bloco do DM roda **antes** de qualquer guarda de nao-envelhecer (o comentario da
	/// :114 diz isso com todas as letras): imortal, vampiro e Deus da Destruicao morrem igual no
	/// vencimento.
	///
	/// EM SEGUNDOS E NAO EM `Year`: este port nao tem calendario (ver `GameServer.cs`,
	/// `ConferirMorteDeVelhice`), e o unico relogio que anda sozinho e o do ceu. Guardar `Year` exigiria
	/// um calendario so pra ler este campo; guardar o INSTANTE deixa a cobranca ser uma comparacao. A
	/// conversao usa `Esferas.SegundosDe`, que ja deriva o ano do DM do dia deste jogo.
	/// ==========================================================================================
	/// </summary>
	public double sw_doom_year;

	/// <summary>
	/// **JA PEDIU MARCOS AO DRAGAO** -- `mob/var/wishedpoints` (`WishTable.dm:361`).
	///
	/// O desejo "Milestones" da 2 Marcos e so pode ser pedido UMA VEZ por personagem, pra sempre --
	/// `if(originator.wishedpoints)` recusa e ainda cancela a invocacao. Sem este campo o desejo seria
	/// uma torneira de pericia aberta, e Marcos sao a moeda de skill inteira deste jogo.
	/// </summary>
	public int wishedpoints;

	/// <summary>
	/// ESTA PESSOA FICA COM O CORPO QUANDO MORRE -- `mob/var/KeepsBody` (`Death.dm:3`).
	///
	/// ============================ O QUE ELE MUDA, LIDO NAS TRES BOCAS DO DM ============================
	///   1. `Stats.dm:275-292` -- o morto comum e ARRANCADO do mundo dos vivos ("cannot exist outside
	///      of the Afterlife"); com `KeepsBody` ele FICA, e so e chamado de volta quando o Ki dele cai
	///      abaixo de `MaxKi/6`. E a coisa toda: o morto continua andando entre os vivos enquanto
	///      tiver energia pra se sustentar;
	///   2. `Gravity.dm:47` -- `if(r > 1 && (!dead || KeepsBody))`: e a UNICA excecao a regra de que
	///      cadaver nao e esmagado. Quem tem corpo tem peso;
	///   3. `OtherworldRankSkills.dm:195-202` -- o verb `Keep_Body` do cargo, que liga e desliga isto
	///      em outra pessoa. **Nao ha nenhum outro produtor** alem do ritual de manipulacao
	///      (`Rituals_Manipulation.dm:387`).
	///
	/// A AUREOLA CONTINUA ACESA, e o proprio DM faz piada com isso na descricao da skill vizinha:
	/// *"Or, you could, y'know, look at their goddamn Halo."* (`OtherworldRankSkills.dm:45`). E por
	/// isso que `Alem.TemAureola` pergunta QUANDO (ja viajou?) e nao ONDE -- o comentario de la ja
	/// tinha previsto este dia por escrito.
	/// ================================================================================================
	/// </summary>
	public bool KeepsBody;

	/// <summary>
	/// `mob/var/isVillain` -- **designado por ADMIN**, e nao conquistado.
	///
	/// A skill que o le e uma so no catalogo inteiro (Planet Destroy, `vilao: 1` -- medido: 1 de 366
	/// entradas do `skills.json`), e o proprio original diz como o bit e escrito: *"only an
	/// admin-designated Villain can learn it"* (`Planets.dm:382`).
	///
	/// MORA NA FICHA porque a ficha inteira e serializada no save: sem isto, ser vilao morreria no
	/// logout e a arma se destrancaria a cada sessao. Ver `GameServer.EhVilao`.
	/// </summary>
	public bool isVillain;

	/// <summary>
	/// `flightability`: a proficiencia de voo, 1 a 100. DIVIDE o custo de Ki de voar.
	///
	/// MORA NA FICHA e nao no <c>ServerPlayer</c> por um motivo so: a ficha inteira e serializada
	/// no save (ver <c>CharacterSave.Ficha</c>), entao um numero que o jogador CONQUISTA voando
	/// persiste de graca. Deixado no jogador vivo, ele zeraria no logout -- e a familia de defeito
	/// de "buff persistente que morre no relog" ja custou caro neste projeto.
	///
	/// Comeca em 1, que no original nao e "1% de pericia": e a sentinela de quem NUNCA aprendeu
	/// (ver <c>Voo.HabilidadeSemVoo</c>).
	/// </summary>
	public double flightability = 1;

	/// <summary>
	/// `swimmastery`: a maestria de NADO (`mobvars.dm:59`, comeca em 0,01). DIVIDE o custo de Ki de
	/// nadar -- ver <see cref="Jandirus.Core.World.Nado"/>.
	///
	/// MORA NA FICHA pelo mesmo motivo do <see cref="flightability"/> logo acima: a ficha inteira e
	/// serializada no save, entao um numero que o jogador conquista nadando persiste de graca. No
	/// original ela e `mob/var` sem `tmp`, ou seja, tambem vai pro save de la.
	///
	/// SAVE ANTIGO CHEGA COM ZERO (o campo nao existia), e zero e proibido: ele esta no DENOMINADOR
	/// do custo. Quem le trata isso -- ver `Nado.CustoPorTiqueDoDm` e `Nado.SubirMaestria`, que
	/// caem no valor de nascenca quando recebem <= 0.
	/// </summary>
	public double swimmastery = Jandirus.Core.World.Nado.MaestriaInicial;

	// O `Apeshitskill` (dominio sobre o Oozaru, 0 a 10) MORAVA AQUI e foi deletado: o Oozaru passou
	// a usar o livro de maestrias das outras formas, por id de catalogo. Ver o bloco "O
	// `Apeshitskill` FOI DELETADO" em `Core/Forms/Oozaru.cs`.

	// =====================================================================
	// STATS CRUS (vem do genoma) e seus tres canais de modificacao
	//   Mod  = MULTIPLICA (transformacoes, itens permanentes)
	//   Buff = SOMA (efeitos que ligam e desligam)
	//   T*   = SOMA temporaria, morre no logout
	// O DM separa os tres justamente pra um "x2 depois +5 depois /2" nao virar perda liquida.
	// =====================================================================
	public double physoff = 1, physdef = 1, technique = 1;
	public double kioff = 1, kidef = 1, kiskill = 1, speed = 1, magiskill = 1;

	public double physoffMod = 1, physdefMod = 1, techniqueMod = 1;
	public double kioffMod = 1, kidefMod = 1, kiskillMod = 1, speedMod = 1, magiMod = 1;

	public double physoffBuff = 0, physdefBuff = 0, techniqueBuff = 0;
	public double kioffBuff = 0, kidefBuff = 0, kiskillBuff = 0, speedBuff = 0, magiBuff = 0;

	public double Tphysoff = 1, Tphysdef = 1, Ttechnique = 1;
	public double Tkioff = 1, Tkidef = 1, Tkiskill = 1, Tspeed = 1, Tmagi = 1, Tmagimon = 1;

	/// <summary>Estilo de luta ativo: multiplica cada stat (Style.dm, tudo 1 sem estilo).</summary>
	public double physoffStyle = 1, physdefStyle = 1, techniqueStyle = 1;
	public double kioffStyle = 1, kidefStyle = 1, kiskillStyle = 1, speedStyle = 1, magiStyle = 1;
	public double kiregenStyle = 1, staminadrainStyle = 1;

	// =====================================================================
	// OS STATS QUE A **FORMA** MEXE
	// =====================================================================
	/// <summary>
	/// ============================ O CANAL DE STAT DA FORMA ATIVA ============================
	/// Uma forma nao muda so o BP. No DM ela mexe em stat, e mexe em varias:
	///
	///   * Oozaru  -- `Tphysoff += 1.2`, `Tspeed -= 1.5`, `Ttechnique -= 1.5`  (`Oozaru.dm:127-129`)
	///   * Gray Full Power -- `Tphysoff/Tkioff/Tspeed += 1.2`                  (`grays.dm:90-92`)
	///   * Icer Full Power -- `Tphysoff += 1.3`, `Tkioff += 1.2`, `Tspeed += 1.3` (`IcerTransform.dm:292-294`)
	///   * Giant Form -- `Tphysoff/Tphysdef += 1.5`, `Tspeed -= 0.5`  (`Giant Form.dm:72-74`)
	///   * Majin -- `physoffMod *= 1.3`  (`Majin.dm:37`)  -- o DM usa OS DOIS canais
	///
	/// O port so tinha isso pro Oozaru, escrito a mao no servidor (`GameServer.Oozaru.cs:211-213`).
	/// O catalogo de formas -- 40 campos -- nao tinha campo nenhum de stat, entao NENHUMA das outras
	/// formas tinha os stats dela. Estes campos sao a casa que faltava, e servem a QUALQUER entrada
	/// do catalogo (ver `FormaDef.Mods`).
	///
	/// ---- POR QUE MULTIPLICATIVO, E NAO SOMA NO CANAL `T*` COMO O DM ----
	/// Porque o `T*` NAO FUNCIONA PRA VELOCIDADE, nem aqui nem la. Repare na linha do `Rspeed`
	/// (`Fighter.Statify.cs`, portada literal de `master.dm:144`): todos os stats somam
	/// `buff + T<stat>`, mas a velocidade faz `speedBuff * Tspeed` -- e `speedBuff` NASCE EM ZERO e
	/// so skill escreve nele. Zero vezes qualquer coisa e zero: o `Tspeed -= 1.5` do Oozaru nao tem
	/// efeito nenhum num personagem sem as skills de `Body.dm`, e tem efeito ALEATORIO num que as
	/// tenha. O dono pediu "debuff de velocidade" -- pelo `Tspeed` ele nao chegaria.
	///
	/// Multiplicativo tambem e o que faz o buff dizer a mesma coisa a vida inteira: uma SOMA de 1,2
	/// e +20% num stat cru 6 e +2% num stat cru 60, entao a mesma forma seria transformadora no
	/// comeco e decorativa no fim.
	///
	/// ---- ONDE ELES ENTRAM ----
	/// Como fator do R INTEIRO, no mesmo lugar do `*Style` (o canal de estilo de luta do DM), e
	/// ANTES do `StatCap`. Ou seja: sao stat, e nao poder -- **nenhum deles entra no
	/// `powerlevel()`**. Isso e deliberado e e o desenho do DM: `Tphysoff` e `physoffMod` nao
	/// aparecem em lugar nenhum daquela conta. Um debuff de velocidade que descesse o BP nao seria
	/// "lento", seria "mais fraco", que e outra coisa. O preco em BP de uma forma ja tem canal
	/// proprio (`Mult`/`MultDiluido` -> `ssjBuff` -> familia 1, e `Dreno`).
	///
	/// SAO ESCRITOS, NUNCA ACUMULADOS: `AplicarForma` os afirma do catalogo a cada chamada -- a
	/// mesma regra do `ssjBuff` e do `trueKiMod`, que moram no mesmo metodo. Nao existe "desfazer"
	/// pra dar errado, e o tique pode reescrever mil vezes sem deriva.
	/// ========================================================================================
	/// </summary>
	public double formaPhysoff = 1, formaPhysdef = 1, formaKioff = 1, formaKidef = 1;
	public double formaTecnica = 1, formaSpeed = 1;

	/// <summary>
	/// A CADENCIA que a forma impoe -- irma dos seis acima, mas fora do `Statify`: quem a le e
	/// `CombatMath.Cadencia`. Divide como o <see cref="hitspeedMod"/> do DM: 0,60 = soca 1,67x mais
	/// devagar. Fica em campo SEPARADO do `hitspeedMod` de proposito -- aquele e do equipamento, e
	/// como a forma AFIRMA o valor dela a cada `AplicarForma`, escrever no mesmo campo apagaria a
	/// espada do sujeito toda vez que ele transformasse.
	/// </summary>
	public double formaCadencia = 1;

	/// <summary>
	/// `hitspeedMod` -- A CADENCIA DO SOCO, e ela e um canal SEPARADO da velocidade.
	///
	/// O dono pediu que o Grade 3 "soque mais devagar", e isso NAO sai do `Espeed`: medido, mexer o
	/// `speedMod` de 1,00 a 0,50 deixa a cadencia parada em 0,333 s. A causa e que o `Eactspeed`
	/// esta cravado em 20 pra todo mundo -- o denominador dele so sai do piso 1 com o argumento
	/// interno acima de ~2154, e o `StatCap` o segura em ~2,96. O comentario de `CombatMath.Cadencia`
	/// dizia que o `Eactspeed` "cai quando o personagem carrega Ki"; ele nao cai nunca.
	///
	/// O lever de cadencia POR PERSONAGEM do DM sempre foi este campo:
	/// `testactspeed /= 3 * globalmeleeattackspeed * hitspeedMod` (`attack cmn.dm:100/137`), escrito
	/// pelo equipamento (`Equipment.dm:289/307/329/349/387`). Ele nunca tinha sido portado. Divide,
	/// entao **acima de 1 e mais rapido, abaixo de 1 e mais lento** -- a mesma orientacao do DM, de
	/// proposito, pra o dia em que o equipamento chegar e usar o numero do original sem traduzir.
	/// </summary>
	public double hitspeedMod = 1;

	// =====================================================================
	// SAIDA de Statify -- R = cru composto, E = depois da curva de retorno decrescente
	// =====================================================================
	public double Rphysoff = 1, Rphysdef = 1, Rtechnique = 1;
	public double Rkioff = 1, Rkidef = 1, Rkiskill = 1, Rmagiskill = 1, Rspeed = 1;

	public double Ephysoff = 1, Ephysdef = 1, Etechnique = 1;
	public double Ekioff = 1, Ekidef = 1, Ekiskill = 1, Emagiskill = 1, Espeed = 1;
	public double Epspeed = 1, Eactspeed = 20, Esuperkiarmor;
	public double speedDIFF = 1, dashingMod = 1, staminapercent = 1, statstamina = 1;
	public double Ewillpower = 1, Estaminamod = 1, MagicCap = 2000, KIregen;
	public double MaxAnger = 120, kicapacity = 1.25, powerupcap = 1.6, baseKiMax = 100;

	// =====================================================================
	// KI e vontade
	// =====================================================================
	public double baseKi = 100, KiMod = 1, kiAmp = 1, trueKiMod = 1, TMaxKi = 1;

	/// <summary>
	/// ============================ O TETO DE KI QUE CADA FORMA DA ============================
	/// No BYOND a escada inteira levanta o teto de Ki enquanto esta ativa, via `trueKiMod`
	/// (`supersaiyanbuff.dm:69-73`, `lssjbuff.dm:151-153`). O port nao tinha nada disso: as 36
	/// entradas do catalogo valiam 1,0x.
	///
	/// SAO CAMPOS, E NAO CONSTANTES NO `FormaDef`, porque no original eles sao `mob/var` que SKILLS
	/// reescrevem -- `heran.dm:135-136` poe `ssjenergymod = 3` e `ssj2energymod = 4`;
	/// `saiyan legendary.dm:57` faz `lssjenergymod *= 1.5`, levando LSSJ3/4 a 6x. Congelar o numero
	/// no catalogo desligaria essas skills EM SILENCIO, que e o pior modo de falha deste projeto.
	///
	/// Quem escolhe qual deles vale pra cada forma e `Catalogo.TetoDeKi`, derivado de (Linha,
	/// Ordem) -- irmao de `Folha` e `NasceDaRaiva`.
	/// ================================================================================
	/// </summary>
	public double ssjenergymod = 2, ssj2energymod = 3, ssj3energymod = 3.5, ssj4energymod = 4.5;

	/// <summary>Os tres da linha Legendary: Wrathful, C-Type e Legendary pra cima.</summary>
	public double rssjenergymod = 1.3, ussjenergymod = 2, lssjenergymod = 4;
	public double KiUnlockPercent = 0.1, kicapacityMod = 1, kicapacity_remove = 1;
	public double kiregenMod = 1, basekiregen = 1, Tkiregen = 1;
	public double kicirculationskill, kigatheringskill, kicontrolskill;
	public double TwillpowerMod = 1, willpowerMod = 1;

	/// <summary>
	/// AS DUAS CHAVES DA TECLA C, e elas sao DIFERENTES -- e o dono chamou a atencao pra isso.
	///
	///   <c>MeditateGivesKiRegen</c>  Ki Unlocked (`Mind.dm:79`). Sem ela, segurar C nao faz NADA:
	///                                quem nunca sentiu o proprio ki nao tem o que reunir. E a
	///                                unica das duas que nasce ligada -- a criacao a DESLIGA
	///                                (`CharacterCreation.dm:87`), entao todo mundo comeca sem.
	///   <c>canPower</c>              Basic Ki Control NIVEL 5 (`Mind.dm:281`), o "controle de ki
	///                                bom". E ela que libera o power-up de verdade: carga mais
	///                                rapida, `powerMod` de volta a 1 e -- so ela -- passar dos
	///                                100% de Ki, que e de onde sai o buff de BP.
	///
	/// SAO `double` PORQUE O CANAL DE EFEITO E DOUBLE. Todo efeito de skill entra por reflexao
	/// (<c>EfeitosDeSkill</c> / <c>NiveisDeSkill</c>) e escreve `double`; um `bool` aqui seria um
	/// campo que o extrator ENXERGA no DM e nao consegue escrever -- e falharia calado, que e o
	/// pior jeito de falhar. `!= 0` e ligado.
	/// </summary>
	public double MeditateGivesKiRegen, canPower;

	/// <summary>
	/// Carga acumulada da sessao de power-up (`extracharge`, tetado em 50 no DM). Ela nao entra em
	/// formula nenhuma do original -- e um CONTADOR de quanto se carregou, que outras coisas leem.
	/// Guardado porque zerar a cada tick esconderia esforco que o jogo mede.
	/// </summary>
	public double extracharge;

	/// <summary>
	/// Ja passou do teto seguro e ESTA pagando por isso. No DM (`Power Control.dm:118-130`) esta
	/// var desarma o dano enquanto o Ki desce sozinho: sem ela, quem ultrapassa leva dano em todo
	/// tick ate voltar, em vez de vazar energia e se estabilizar.
	/// </summary>
	public bool overcharge;
	public double staminagainMod = 1, satiationMod = 1;

	/// <summary>
	/// O TANQUE DE COMIDA -- `currentNutrition` do DM. E daqui que o vigor volta.
	///
	/// Ver <see cref="Nutricao"/>: o port tinha o vigor caindo e subindo do nada, porque a metade
	/// do sistema que o ALIMENTA nunca foi portada.
	/// </summary>
	public double CurrentNutrition = Nutricao.TanqueInicial;

	/// <summary>
	/// QUAO RAPIDO ESTE CORPO DIGERE. Estica o tanque e acelera as passadas do estomago; Namekuseijin
	/// e Saiyajin Primal tem 2 no original.
	/// </summary>
	public double Metabolism = 1;

	/// <summary>O corpo ja avisou que precisa comer? So pra o aviso nao repetir a cada passada.</summary>
	public bool Hungry;

	/// <summary>
	/// OS DOIS CONTADORES QUE ABREM ARVORE. Cada skill de base do corpo soma 1 em um deles, e e a
	/// SOMA que destrava arvore nova (`Body.dm:24-32`): mais de 2 de `bodyreadiness` abre
	/// Bodybuilding, mais de 2 de `bodyskill` abre Martial Skill (e Weapons Expert com arma), e 2
	/// de cada abre Cultivation.
	///
	/// EU JA DEI ESTES DOIS COMO CODIGO MORTO -- e estava errado. A varredura que fiz filtrava as
	/// linhas com `savant.` pra separar escrita de leitura, e com isso apagou justamente as
	/// leituras (`if(savant.bodyskill>2)`). Sao o gate de QUATRO arvores: sem eles, metade da
	/// progressao fisica do jogo fica inalcancavel e nada na tela diz por que.
	/// </summary>
	public double bodyskill, bodyreadiness;

	/// <summary>O estilo de luta ATIVO, pelo id do catalogo. Vazio = nenhum.</summary>
	public string EstiloAtual = "";

	/// <summary>Maestria por estilo, e o teto pessoal de cada um. Persistem no save.</summary>
	public Dictionary<string, double> MaestriaDeEstilo = new(StringComparer.Ordinal);
	public Dictionary<string, double> TetoDeEstilo = new(StringComparer.Ordinal);

	/// <summary>Sabe usar arma -- com `bodyskill` acima de 2, e o que abre a arvore de armas.</summary>
	public double weaponeq;

	/// <summary>
	/// A ESCOLHA EXCLUSIVA DAS ARVORES RACIAIS -- `mob/var/pitted = 0` (tsujin.dm:26). Cada par de
	/// skills irmas escreve 1 ou 2 no `after_learn` (Stick = 1 / Supa = 2, arlian.dm:88/110;
	/// DollRegen / Play, spirit-doll.dm:34/52; Against / Biggest_Brain, tsujin.dm:40/62; Light /
	/// Telespeed, yardrat.dm), e o `treegrow()` da arvore APAGA a irma: `if(savant.pitted==1)
	/// disableskill(Supa)` (arlian.dm:13-16). Nove skills escreviam isto em `flags` e o campo nao
	/// existia: a flag caia em `Desconhecidos` e as duas irmas ficavam compraveis juntas.
	/// </summary>
	public double pitted;

	/// <summary>
	/// `mob/var/HPregenbuff = 1` (master.dm:60): multiplica a cura passiva do canal 4
	/// (`SpreadHeal(0.001 * Ephysdef * ... * HPregenbuff * THPregen)`, master.dm:185). Quem escreve e a
	/// `Supa` do Arlian (`+= 0.1`, arlian.dm:108) e o virus de regeneracao (Viruses.dm:379, `+= 2`).
	/// Consumido em <see cref="Combat.Regeneracao"/>.
	/// </summary>
	public double HPregenbuff = 1;

	/// <summary>
	/// A PARTE DAS SKILLS no `misc_stats["Regeneration"]` do genoma -- `add_to_stat("Regeneration", N)`
	/// (spirit-doll.dm:33 = 10, alien.dm:65 = 5, Bodybuilding.dm:254 = 1). A parte da RACA continua no
	/// `races.json`; as duas se somam no `GameServer.EixoDeRegen`, que refaz o <see cref="Combat.PerfilDeRegen"/>
	/// do corpo. Separado da raca de proposito: a raca e dado do catalogo, isto e progresso do personagem.
	/// </summary>
	public double RegenerationDeSkill;

	/// <summary>
	/// O QUE AS SKILLS APRENDIDAS ESTAO SOMANDO AGORA (campo -> quanto). Guardado pra que
	/// reaplicar seja idempotente: <see cref="Skills.EfeitosDeSkill.Aplicar"/> desconta isto
	/// antes de somar de novo, senao cada login empilharia os buffs mais uma vez.
	/// </summary>
	public Dictionary<string, double> BuffsDeSkill = new(StringComparer.Ordinal);

	/// <summary>Os fatores MULTIPLICATIVOS aplicados agora. Desfeitos por divisao, nao subtracao.</summary>
	public Dictionary<string, double> MultsDeSkill = new(StringComparer.Ordinal);

	/// <summary>As flags/contadores ESCRITOS agora. Voltam a zero quando a skill sai.</summary>
	public Dictionary<string, double> FlagsDeSkill = new(StringComparer.Ordinal);
	public double concealeddeBuff = 1, concealedBuff = 1;
	public double kiarmor, superkiarmor, superkiarmorMod = 1;
	public double actspeed = 20, mana_cap_mod = 1;
	public double phys_remove = 1, bp_remove = 1;

	// =====================================================================
	// RAIVA
	// =====================================================================
	/// <summary>
	/// A RAIVA COMO NUMERO -- 100 e "calmo" (1,0x), e o teto e o <see cref="MaxAnger"/>. Unica
	/// entrada do `angerBuff` (`Fighter.Power.cs`), e o `Anger` do DM (`base.dm:88`).
	///
	/// **CAMPO DERIVADO, E NAO ESTADO.** Quem o escreve e UM lugar so, o `GameServer.ProjetarRaiva`,
	/// e ele nao acumula: copia pra ca uma funcao pura das duas janelas de raiva do `ServerPlayer`
	/// (`GameServer.RaivaComoNumero`, que porta `Stats.dm:438-443`). Ele mora aqui, e nao la, porque
	/// a conta de poder e do Core e o Core nao tem relogio -- nao porque alguem seja dono dele.
	///
	/// SOMAR OU MULTIPLICAR NISTO E O BUG DO 20x, literalmente: o DM ja pagou por ele e escreveu o
	/// remedio no proprio codigo (`Murder.dm:112`, *"never stack, sum, or multiply"*). Quem quiser
	/// enfurecer alguem chama `GameServer.AmigoAbatido`, que mexe na JANELA. Nao ha caminho legitimo
	/// que escreva este campo direto -- e a bancada `raiva` [8] varre os fontes atras de um.
	///
	/// O 100 do valor inicial e o que sobra depois de todo prazo vencer, e tambem o que um corpo
	/// recem-carregado tem: raiva **nao persiste** (a janela nao vai pro save, e este campo nao esta
	/// no `CharacterStore`).
	/// </summary>
	public double Anger = 100;
	public double baseAnger = 100, angerMod = 1;
	/// <summary>Skill Legendary Anger: +100 aqui = +1x no teto de raiva (2x -> 3x).</summary>
	public double legendaryAngerBonus = 0;

	// =====================================================================
	// BUFFS QUE MULTIPLICAM (o "formBuff" e o "buffBuff" do powerlevel)
	// =====================================================================
	public double ssjBuff = 1;        // escada Super Saiyajin
	public double transBuff = 1;      // Oozaru, formas de Icer, etc
	public double formsBuff = 1;      // slot generico de forma
	public double gateBuff = 1;       // Portao do Inferno
	public double HellstarBuff = 1;   // Estrela Makyo
	/// <summary>
	/// O UNBOUND EGO (`ue_ego_mult`): multiplicador de BP pelos membros FERIDOS.
	///
	/// ============================ ELE ESTAVA DECLARADO E ORFAO ============================
	/// O campo nasceu no porte da ficha e NINGUEM o lia -- nem o `PowerLevel()`, nem o servidor.
	/// Um multiplicador que nunca entra na conta e indistinguivel de multiplicador nenhum: o
	/// Unbound Ego "existia" e valia exatamente 1x. Agora o `PowerLevel()` o soma na base, junto
	/// do Mistico, e quem o escreve e o `TickDoUnboundEgo`.
	/// ==================================================================================
	///
	/// Campo PROPRIO e nao um dos `*Pcnt` porque ele e reescrito por INTEIRO a cada tique (o corpo
	/// muda o tempo todo): somar num campo compartilhado o faria empilhar consigo mesmo, e
	/// colidiria com o Mistico, que e de outra linhagem e pode coexistir.
	///
	/// 1 = sem bonus. Ver `Disciplinas.UnboundEgo`.
	/// </summary>
	public double ue_ego_mult = 1;
	public double expandBuff = 1, giantFormbuff = 1, ArtifactsBuff = 1, eyeBuff = 1;
	public double BPBoost = 1;        // Ascensao
	public double powerMod = 1;       // Controle de Poder (pode ser < 1)
	public double OozaruBuff = 1;

	// =====================================================================
	// BUFFS QUE SOMAM NA BASE (additiveBoost) -- ver o comentario em PowerLevel
	// =====================================================================
	public double FuseDanceMod = 1, FPotaraMod = 1;
	public double MysticPcnt = 1, MajinPcnt = 1, aurasBuff = 1;

	public double KaioPcnt = 1;             // o Kaio-ken VIVO (a maestria e outro numero)

	/// <summary>
	/// `mob/var/KaiokenMastery = 1` (kaioken.dm:3) -- a maestria do Kaio-ken, que PERSISTE no save do
	/// DM e vivia num dicionario de sessao do servidor (a divida anotada em `GameServer.Tecnicas.G2`).
	/// Aprender a skill soma 3 (`savant.KaiokenMastery += 3`, kaioken.dm:93) pelo canal normal dos
	/// buffs, e segurar a aura sobe devagar (`+= 0.001 * amt` por volta ate 20). Mora na ficha
	/// porque a ficha e o que vai pro disco inteira.
	/// </summary>
	public double KaiokenMastery = 1;
	public double ParanormalBPMult = 1;     // Vampiro / Lobisomem

	// =====================================================================
	// DEBUFFS e estado
	// =====================================================================
	public double weight = 1, BPrestriction = 1, splitformdeBuff = 1;
	public double splitformMastery = 0, splitformCount = 0;

	/// <summary>
	/// A IDADE EM ANOS. Mora na ficha porque e a ficha que calcula poder.
	///
	/// Ela ja existia no `ServerPlayer.Idade`, mas so pra ser mostrada e salva -- a conta de BP
	/// nao a alcancava, e por isso <see cref="AgeDiv"/> ficava em 1 pra sempre.
	/// </summary>
	public double Idade = 18;

	/// <summary>
	/// O MULTIPLICADOR DE PODER POR IDADE, recalculado a cada `PowerLevel`.
	///
	/// ============================ ELE ERA UM 1 DECORATIVO ============================
	/// O campo existia, entrava no `netBuff` e NUNCA era escrito: nascia 1 e morria 1. O efeito
	/// pratico e que a idade nao fazia nada -- um Saiyajin de 8 anos e um de 800 batiam igual, e a
	/// caixa de idade da criacao era enfeite. Ver `Envelhecimento.DivisorDeIdade`.
	/// =================================================================================
	/// </summary>
	public double AgeDiv = 1;

	// =====================================================================
	// GRAVIDADE
	// =====================================================================
	public double GravMastered = 1, Planetgrav = 1, gravmult = 0;
	public double gravFelt = 1, gravBuff = 1;

	// =====================================================================
	// SOMAS DIRETAS (zeni cru, nao razao -- nunca vire "Nx" numa UI)
	// =====================================================================
	public double HVBPExpAdd, MagAdd, TMagAdd;
	public double FuseBuff, HVBPAdd, CooldownAmount, majin_absorb_bp, AbsorbBP;
	public bool AbsorbDeterminesBP, HVBPAddEnd;

	/// <summary>
	/// `HasSoul` (`Absorption.dm:6`): nasce com alma; o Soul Absorb (`demon.dm:41-42`) a tira, uma vez
	/// na vida. Vai pro save junto com a ficha. Ver o lote G12.
	/// </summary>
	public bool HasSoul = true;

	// =====================================================================
	// GOD KI
	// =====================================================================
	public GodKiState? godki;
	public bool godki_gt_mode;
	public double godki_boost = 2.5, gt_boost = 3, godki_give_mult = 0;
	public double ssj = 0, lssj = 0;
	public bool beast_form, MysticActive;

	// =====================================================================
	// CASOS ESPECIAIS DE FIM DE CONTA
	// =====================================================================
	/// <summary>Revive por Zeni: BP em 25% ate este instante (em ms de relogio real).</summary>
	public long zeni_revive_debuff_until;

	/// <summary>
	/// ATE QUANDO ESTE CORPO NAO PODE FUNDIR DE NOVO -- o `fusion_cooldown_until` do DM
	/// (`Fusion.dm:28`), em ms de relogio real (a mesma unidade do
	/// <see cref="zeni_revive_debuff_until"/> logo acima).
	///
	/// ============================ ELE MORA AQUI PORQUE PRECISA ATRAVESSAR O LOGOUT ============================
	/// No original a declaracao esta dentro do bloco `mob/var` e **sem `tmp/`** -- as tres linhas
	/// vizinhas (`:26-27` e `:29`) sao `tmp/` e esta nao e. E essa ausencia que faz a recarga de 1 h
	/// viajar no savefile.
	///
	/// O port guardava isto num dicionario do servidor com o ID DE SESSAO como chave, e o apagava no
	/// logout de proposito (id se reusa). O efeito era que **Alt+F4 zerava a espera dos dois**, e a
	/// recarga -- o unico freio do sistema inteiro -- virava opcional: funde, separa, relog, funde de
	/// novo, e o `FuseBuff` de `(A+B)*2` sai de graca.
	///
	/// Aqui ele persiste sem uma linha de encanamento: o `CharacterSave.Ficha` serializa este objeto
	/// INTEIRO (ver `Server/CharacterStore.cs:37-43`), que e a mesma razao pela qual o DM o guarda no
	/// mob e nao num datum a parte.
	///
	/// **ZERO = pode fundir.** Nao ha valor "nao inicializado" a distinguir: save antigo carrega 0 e
	/// 0 e o passado, entao quem ja existia continua podendo fundir -- que e o lado certo do erro.
	/// ======================================================================================================
	/// </summary>
	public long fusion_cooldown_until;

	// =====================================================================
	// O BIO-ANDROIDE DE LABORATORIO -- `Code/Modules/Tech/DNALabs.dm`
	// =====================================================================
	/// <summary>
	/// ESTE CORPO SAIU DE UM TANQUE (`bio_lab_born`). E o que separa o bio de LABORATORIO da raca
	/// nativa: o primeiro tem escada por CONTAGEM DE ABSORCOES e SSJ2 pela morte, a segunda sobe
	/// por limiar de BP. Ver o cabecalho de <see cref="bio_stage"/>.
	/// </summary>
	public bool bio_lab_born;

	/// <summary>
	/// ============================ O DEGRAU DO BIO E ESTADO PERMANENTE, E **NAO** UMA FORMA ============================
	/// 1 larva, 2 imperfeito, 3 semi-perfeito, 4 perfeito. E a decisao de projeto mais importante
	/// deste sistema, entao ela esta escrita aqui e nao no relatorio de uma sessao:
	///
	/// No DM os degraus 2/3/4 fazem **`BP *= 2`** e **`BP *= 4`** (`DNALabs.dm:620,628`) -- eles
	/// mexem no BP BASE, de forma permanente, e trocam o `icon` E o `oicon` (o CORPO, nao um
	/// overlay). Nada disso e um buff: nao ha `Loop`, nao ha dreno, nao ha como reverter.
	///
	/// Poe-los no <see cref="Jandirus.Core.Forms.Catalogo"/> como uma linha de forma teria mentido
	/// em tres contas de uma vez -- o teto de treino (`relBPmax` le o BP base), o teto do Zenkai
	/// (`ZenkaiCeiling`) e o `CapCheck` --, porque todas elas leem o BP e nao o `expressedBP`. Um
	/// bio perfeito ficaria com 4x de poder na tela e o teto de treino de uma larva.
	///
	/// **O QUE E FORMA MESMO E SO A SUPER PERFEITA** (`Cell4()`, `CellFormBuff.dm:73`): 8x
	/// temporario, com dreno, que cai sozinho quando o Ki acaba. Essa sim tem entrada no catalogo
	/// (`super_perfect`), e e a unica.
	/// ================================================================================================================
	/// </summary>
	public int bio_stage;

	/// <summary>
	/// HAVIA DNA SAIYAJIN NA FORNADA (`bio_saiyan_dna`). Abre o <see cref="canSSJ"/> e e requisito
	/// do despertar do SSJ2 pela morte (`DNALabs.dm:650`).
	///
	/// **NAO E O QUE DA ZENKAI.** A mensagem do original promete isso (`:480`) mas o
	/// `has_zenkai()` responde TRUE na primeira linha pra qualquer bio-androide
	/// (`combatgains.dm:14`), com ou sem sangue Saiyajin. Ver <see cref="HasZenkai"/>.
	/// </summary>
	public bool bio_saiyan_dna;

	/// <summary>
	/// HAVIA, NA FORNADA, DNA DE UMA RACA QUE RESPIRA NO VACUO -- e por isso ESTE corpo respira.
	///
	/// ============================ POR QUE E UM CAMPO GRAVADO, E NAO UMA PERGUNTA ============================
	/// Porque **a fornada morre no parto**: `NascerBioAndroide` zera `lab.Fornada` na primeira linha,
	/// poe `Genoma = null` e reescreve a `ParentRace` pra "BioAndroid". Passado o nascimento nao
	/// sobrou ninguem no mundo que saiba quem foram os doadores -- ou este bit e escrito na hora e vai
	/// pro disco, ou a informacao deixa de existir. E exatamente o motivo do
	/// <see cref="bio_saiyan_dna"/>, que e o vizinho de cima justamente por isso.
	///
	/// **QUEM DECIDE O VALOR CONTINUA SENDO O <see cref="Jandirus.Core.World.Vacuo"/>.** O parto
	/// pergunta `Vacuo.RespiraNoVacuo(amostra.Raca, amostra.RacaDoPai)` a cada doador -- nao ha uma
	/// segunda lista de raca aqui, e nao pode haver: foi assim que a regeneracao por raca errou nos
	/// DOIS sentidos (deu habilidade a quem nao devia e tirou de quem devia). Mudar a lista do dono em
	/// `Vacuo.RacasQueRespiram` muda o bio junto, no mesmo dia, sem tocar neste arquivo.
	///
	/// E ele entra no funil pelo `folegoConcedido` (`GameServer.Vacuo.SufocaAgora`), no mesmo `||` do
	/// cargo e do traje: um bio que herdou o folego E veste a roupa nao respira duas vezes, e um que
	/// nao herdou continua salvo pela roupa.
	/// =====================================================================================================
	///
	/// ============================ E ISTO DIVERGE DO DM, DE PROPOSITO ============================
	/// No original o Bio-Androide respira SEMPRE, sem olhar doador nenhum: `statbiodroid.dm:51` poe
	/// `"Space Breath" = 1` no proto da raca, e o parto (`DNALabs.dm:460-465`) semeia o genoma **so**
	/// do proto Bio-Android -- quatro doadores humanos dao um bio que respira igualzinho.
	///
	/// O dono pediu outra coisa, literal: *"bio androides pegam a capacidade de respirar no espaco
	/// caso uma das racas q esta em seu dna consiga"*. Isso e MAIS RESTRITIVO que o original, e esta
	/// escrito aqui pra que ninguem "conserte" o port pro lado do DM sem perceber que estaria
	/// desfazendo um pedido.
	/// ======================================================================================
	/// </summary>
	public bool bio_dna_respira;

	/// <summary>Quantos JOGADORES absorvidos desde a ultima evolucao. NPC vale meio.</summary>
	public double bio_abs_players;

	/// <summary>Quantos ANDROIDES absorvidos desde a ultima evolucao. Um so ja evolui.</summary>
	public double bio_abs_androids;

	/// <summary>O SSJ2 ja foi despertado PELA MORTE. Uma vez na vida (`bio_ssj2_by_death`).</summary>
	public bool bio_ssj2_by_death;

	/// <summary>
	/// QUANDO A CARAPACA LARVAL SE ROMPE, em ms de relogio REAL -- `bio_mature_realtime`.
	///
	/// Relogio real e nao in-game porque o original tambem usa (`world.realtime + DAY_REAL_MINUTES
	/// * 600`): um dia in-game, que na escala deste port sao ~24 minutos de relogio.
	/// </summary>
	public long bio_mature_em;

	/// <summary>
	/// A FORMA PERFEITA E PERMANENTE (`form3cantrevert`). No lab-bio ela vem de graca no degrau 4;
	/// na raca nativa ela vem de SOBREVIVER a propria morte. E requisito da Super Perfeita.
	/// </summary>
	public bool form3cantrevert;
	/// <summary>Vazamento do Frost mutante: abaixo de 1 o poder escapa ate o piso.</summary>
	public double fd_release = 1;

	/// <summary>
	/// O `fd_ki_locked` do original: o Mutante PERDEU o controle do ki.
	///
	/// Mora aqui, ao lado do vazamento que ele causa, porque no DM os dois sao `mob/var` vizinhos --
	/// e porque quem o LE nao e o motor de formas e sim a carga (`CargaDeKi.Passo`, o
	/// `Power Control.dm:141`): com o ki solto, a tecla C morre ate ele recuar pra uma forma que
	/// segure. Quem o ESCREVE e o motor do Mutante (`GameServer.Frost.cs`), e so ele.
	///
	/// **NAO da pra derivar de `fd_release &lt; 1`**, e a ordem do original e o motivo: o controle se
	/// perde PRIMEIRO (`fd_ki_locked = 1`, com aviso no chat) e so entao a liberacao comeca a cair,
	/// 1,2% por segundo. No instante em que ele trava, `fd_release` ainda vale 1 -- e e exatamente
	/// esse instante que precisa matar a tecla C, senao o jogador enche o tanque no unico segundo em
	/// que a regra ainda nao pegou.
	/// </summary>
	public bool fd_ki_locked;

	// =====================================================================
	// GANHOS
	// =====================================================================
	public double UPMod = 1;          // Potencial da raca: define o teto pessoal de treino
	public double relcaprate = 1, HBTCMod = 1, bgains = 1, tgains = 1, tailgain = 1;
	public double hiddenpotential = 1, hdnptltoBP = 1;

	/// <summary>
	/// A RAZAO potencial-escondido/BP que as skills de compra GARANTEM pra sempre (One Hundred, One
	/// Punch, One Training -- as tres unicas fontes de `hiddenpotential` deste port). Somada na compra
	/// (`GanhoNaCompra.Aplicar`), devolvida ao esquecer, gravada com a ficha. Ver `Statify`, onde ela
	/// entra na conta do `hdnptlmod`, e o porque de ela existir.
	/// </summary>
	public double potencialGarantido;
	public double BoostMult = 0;
	public bool isHV, BoostActive;

	/// <summary>Multiplicadores de RITMO que vem do genoma (misc_stats).</summary>
	public double TrainMod = 1, MedMod = 1, SparMod = 1, GravMod = 1;

	/// <summary>Multiplicador de MARCO de poder: so sobe ao romper um patamar da propria raca.</summary>
	public double bp_milestone_mult = 1;
	public HashSet<string> bp_milestones_done = new(StringComparer.Ordinal);

	public double StamBPGainMod = 1;

	/// <summary>Ganho acumulado enquanto o personagem NAO treina, pra quem interpreta nao ficar pra tras.</summary>
	public double BPBuffer = 0;
	public int Gaintimer = 0, Buffertimer = 0;

	/// <summary>
	/// Ritmo da ZONA. Sala do Tempo multiplica por 280 (1 dia la = 1 ano), Dimensao Mental
	/// divide por 4. O servidor escreve isto na troca de zona.
	///
	/// ============================ ELE PASSOU A SER ESCRITO DE VERDADE ============================
	/// Ate a camada 2 da Sala do Tempo (13.4) este campo era lido em quatro caminhos de ganho
	/// (`TrainGain`, `AttackGain`, `BlastGain`, `GravGain`) e **o servidor nunca o escrevia** -- so a
	/// `TrainBench` do pipeline. Ou seja: a constante `GainKnobs.TimeChamberMult` existia, a
	/// multiplicacao existia, e mesmo assim a Sala rendia igual a um campo qualquer da Terra.
	///
	/// Quem escreve agora e <see cref="Jandirus.Core.World.SalaDoTempo.AplicarRitmo"/>, e ele escreve
	/// **os dois** campos de zona de uma vez, nas duas direcoes (entrando 280, saindo 1). Uma casa so
	/// pros dois porque "ligou e esqueceu de desligar" e o modo de falha obvio aqui -- um jogador que
	/// saisse da Sala com 280 no bolso treinaria a 280x na Terra pra sempre.
	/// ==========================================================================================
	/// </summary>
	public double zoneGainMult = 1;

	/// <summary>
	/// Ritmo de MAESTRIA DE FORMA da zona -- campo separado do <see cref="zoneGainMult"/> de
	/// proposito, e o proposito e um numero diferente.
	///
	/// Dentro da Sala do Tempo o BP rende **280x** e a maestria sobe **4x**, e isso NAO e
	/// inconsistencia: o 280 e tempo comprimido (um dia la vale um ano de treino), e maestria e
	/// COSTUME COM A FORMA -- acelerar as duas no mesmo fator entregaria todas as formas dominadas
	/// numa unica sessao de 48 minutos. E regra do dono (13.6d) e esta e a razao escrita, pra ninguem
	/// "consertar" a diferenca depois.
	///
	/// Quem le e o funil de maestria do servidor (`GameServer.SubirMaestriaDaZona`), que e o unico
	/// ponto por onde os dois tiques de forma (a escada e o Oozaru) creditam maestria.
	/// </summary>
	public double zoneMasteryMult = 1;

	// --- estado do laco de treino (o que o BYOND guardava em tmp) ---------
	public bool train, med, minuteshot, train_med_to_hp;
	public int missedtrain, minuteshot_ig_ki;
	public double tmp_activ_gains;
	public Facing lastdir = Facing.South, dir = Facing.South;

	// --- peso ------------------------------------------------------------
	public double Weighted = 0;        // quanto de peso o personagem esta vestindo
	public double weight_ratio = 0;
	public double weight_cap_hw = 0;   // RECORDE do corpo: nunca cai (senao vira espiral)

	// --- Zenkai ----------------------------------------------------------
	public long zenkaiReady;           // relogio real (ms) em que o proximo Zenkai libera
	public double ssj3at = 2e10;       // BP de referencia do SSJ3 -- teto de aposentadoria do Zenkai
	public double ssj3LearnReq = 0;    // requisito pessoal, quando ja sorteado
	public string ParentRace = "";
	public bool canSSJ;

	// =====================================================================
	// LOTE G11 -- os campos que as skills "mudas" do censo escreviam no DM e o port nao tinha.
	// Todos `double` de proposito: o extrator de skills (`EfeitosDeSkill`, canal ATRIBUICAO) e o
	// motor de niveis so escrevem campo `double` por reflexao, e e por eles que `partplant`,
	// `can_stretch_arms`, `psythre`, `cangivepower`, `hellstar_disabled` e `teleskill` chegam aqui
	// ao aprender a skill (`skills.json`: flags). Persistem com a ficha (a JSON inclui campos).
	// =====================================================================
	public double makyosunmastery, makyomoonmastery, makyoaamastery;   // maestria 0-100 do astro (makyo.dm:16-18)
	public double hellstar_disabled = 1;   // makyo.dm:135 (o Above_All escreve 0; a Estrela nao existe no port)
	public double teleskill = 1;           // yardrat.dm:68 -- cresce com a distancia teletransportada
	public double partplant, can_stretch_arms, psythre, cangivepower;   // alien.dm:131, namekian.dm:139, heran.dm:151, givepower.dm:13
	public double stored_time;             // kanassa-jin.dm:50 -- a moeda do tempo guardado (1 por mes)
	public double stuckage;                // kanassa-jin.dm:60 -- a idade em que o Time Store prendeu o corpo (0 = ainda nao)
	public double unlockPotential;         // UnlockPotential.dm:5 -- 1 = ja despertou, UMA vez por vida
	public double expandlevel;             // Body Expansion.dm:9 -- o grau atual da expansao
	public double observingnow;            // observe.dm:7/12 -- projetando a mente em alguem

	// =====================================================================
	// LOTE G13 -- a familia da MENTE (`/datum/skill/tree/Mind`, "Strength of Mind").
	//
	// Os tres sao `double` pelo mesmo motivo dos de cima: o motor de niveis so ESCREVE campo
	// `double` por reflexao, e `buffregen` chega exatamente assim -- e um `savant.buffregen=1` no
	// degrau 30 da Advanced Ki Circulation (`Mind.dm:498`). Enquanto o campo nao existia, aquele
	// degrau escrevia num campo inexistente e o relatorio nem reclamava: ninguem no port jamais
	// tinha chegado ao nivel 30 daquela skill pra a escrita ser TENTADA.
	// =====================================================================

	/// <summary>
	/// `savant.kibuffon` (Ki2.0/KiBuffs.dm:2) -- um buff de Ki de pe: Focus, Efficiency ou Energy
	/// Shield. Quem liga e desliga e o motor de buffs do servidor (`GameServer.LigarBuff`).
	///
	/// Ele e condicao de exp em SEIS skills (as tres de Circulacao x Basic/Advanced/Perfect) e o
	/// portao do <see cref="buffregen"/>.
	/// </summary>
	public double kibuffon;

	/// <summary>
	/// `savant.buffregen` (Mind.dm:498) -- a Advanced Ki Circulation no nivel 30 faz o corpo se
	/// curar enquanto um buff de Ki estiver ligado (`SpreadHeal(0.01)` por tique, `:517`).
	/// </summary>
	public double buffregen;

	/// <summary>
	/// `savant.studying` (KiStatsModule.dm:49) -- o laco do verb `Study_Other` esta de pe. Fonte de
	/// exp das tres skills de Percepcao. `tmp` no DM; aqui ele e zerado ao entrar (o laco nao
	/// sobrevive a sessao).
	/// </summary>
	public double studying;

	/// <summary>O genoma que gerou este lutador. Usado pra saber a fracao de sangue Saiyajin.</summary>
	public Races.Genome? Genoma;

	// =====================================================================
	// RESULTADO
	// =====================================================================
	public double expressedBP;        // o numero que o mundo LE (scouter, dano, ranking)
	public double relBPmax;           // teto pessoal de treino
	public double peakexBP;           // o que seria sem idade/peso/ferimento
	public double Egains = 1;         // multiplicador de ganho de treino
	public double expressedAdd;

	/// <summary>
	/// Razoes de estado do corpo. Comecam em 1 (corpo inteiro) -- no DM sao `tmp` sem valor
	/// inicial, ou seja ZERO ate o primeiro powerlevel rodar, e o Statify le elas um tick
	/// antes de existirem. Zero ali nao e valor de projeto, e variavel nao inicializada.
	/// </summary>
	public double kiratio = 1, hpratio = 1, staminaratio = 1;
	public double buffBuff, formBuff, deBuff, statusBuff, netBuff, nnetBuff = 1;
	public double fusionBuff = 1, angerBuff = 1;

	/// <summary>
	/// A GRAVIDADE COMO DIVISOR DE CONDICAO -- irma de <see cref="deBuff"/>, <see cref="statusBuff"/>
	/// e <see cref="AgeDiv"/>, e ate agora a unica da familia que era VARIAVEL LOCAL do PowerLevel.
	///
	/// Ela subiu pra campo quando a <see cref="Inteireza"/> passou a sair do produto dos fatores de
	/// condicao em vez de sair da razao `expressedBP / peakexBP`: sem isto a % nao teria como
	/// enxergar a gravidade, que e justamente um dos tres fatores que o dono citou. Nasce em 1
	/// (nenhum peso de planeta) e o ramo do poder escondido a deixa assim de proposito.
	/// </summary>
	public double gravDiv = 1;

	/// <summary>
	/// Um tick completo, na ORDEM do GlobalStats do BYOND: primeiro os stats, depois o poder.
	/// A ordem importa -- o Statify recalcula MaxKi, e o PowerLevel divide o Ki por ele.
	/// </summary>
	public void Tick(double gravBalance = 1, long agoraMs = 0)
	{
		Statify();
		ClampAnger();
		PowerLevel(gravBalance, agoraMs);
		WeightTick();   // depende do expressedBP que o PowerLevel acabou de escrever
	}

	/// <summary>
	/// Prende a raiva na faixa valida. No BYOND isto mora no laco Stats(), junto com o
	/// decaimento e os rotulos de humor (que sao do sistema de raiva, outra etapa) -- mas o
	/// CORTE precisa vir junto da conta de poder: sem ele, qualquer coisa que escreva um
	/// Anger alto vira multiplicador de BP direto, que foi exatamente a anomalia do 20x.
	/// Depende do MaxAnger, entao roda depois do Statify.
	/// </summary>
	public void ClampAnger() => Anger = DmMath.Clamp(Anger, 100, MaxAnger);

	// =====================================================================
	// SEMENTE
	// =====================================================================
	/// <summary>
	/// Monta um lutador a partir do genoma ja composto -- a ponte entre a Etapa 3 (racas) e
	/// esta. As chaves sao as do proto do DM; quem nao vier no bloco fica no default.
	/// </summary>
	public static Fighter FromGenome(Genome genoma, StatBlock bloco, double bp, string nome = "")
	{
		var f = new Fighter
		{
			Name = nome,
			Race = genoma.MajorityRace,
			Class = genoma.Class,
			BP = Math.Max(bp, 1),

			physoff = bloco.Stats.GetValueOrDefault("Physical Offense", 1),
			physdef = bloco.Stats.GetValueOrDefault("Physical Defense", 1),
			kioff = bloco.Stats.GetValueOrDefault("Ki Offense", 1),
			kidef = bloco.Stats.GetValueOrDefault("Ki Defense", 1),
			kiskill = bloco.Stats.GetValueOrDefault("Ki Skill", 1),
			technique = bloco.Stats.GetValueOrDefault("Technique", 1),
			speed = bloco.Stats.GetValueOrDefault("Speed", 1),
			magiskill = bloco.Stats.GetValueOrDefault("Esoteric Skill", 1),

			KiMod = bloco.Stats.GetValueOrDefault("Energy Level", 1),
			BPMod = bloco.Stats.GetValueOrDefault("Battle Power", 1),

			basekiregen = bloco.Misc.GetValueOrDefault("Ki Regeneration", 1),
			baseAnger = 100 * bloco.Misc.GetValueOrDefault("Anger", 1),

			// POTENCIAL -> teto pessoal de treino. Vale repetir porque e facil de errar: o
			// relBPmax e `BP * (1 + UPMod) * ...`, entao UPMod ZERO deixa o teto colado no BP
			// atual e o personagem NAO TREINA. O default do DM e 1, e a raca sobrescreve.
			UPMod = bloco.Misc.GetValueOrDefault("Potential", 1),

			TrainMod = bloco.Misc.GetValueOrDefault("Train Mod", 1),
			MedMod = bloco.Misc.GetValueOrDefault("Med Mod", 1),
			SparMod = bloco.Misc.GetValueOrDefault("Spar Mod", 1),
			GravMod = bloco.Misc.GetValueOrDefault("Gravity Mod", 1),

			Genoma = genoma,
		};

		// o DM protege o ascBPmod contra 0; o mesmo vale pros mods que dividem contas
		if (f.BPMod <= 0) f.BPMod = 1;
		if (f.KiMod <= 0) f.KiMod = 1;

		// Nasce DESCANSADO. O Ki maximo depende do "Energy Level" da raca, entao deixar o Ki
		// no 100 fixo faria um Saiyajin (KiMod 1,4) nascer com 71% de energia -- e um Ki
		// parcial derruba o BP expresso na hora (o kiratio entra no pacote de estado).
		f.Tick();
		f.Ki = f.MaxKi;
		f.Tick();
		return f;
	}
}

/// <summary>
/// O pedaco do datum de God Ki que a conta de poder LE. O motor (maestria, energia,
/// despertar) e outra etapa -- aqui so entra o que vira multiplicador.
/// </summary>
public sealed class GodKiState
{
	public bool usage;        // esta usando agora
	public bool awakened;     // ja despertou alguma vez
	public double mastery;    // 0-100%: destrava Blue (33), Royale/Beast (50), UI/Destruicao (80)
	public double godki_mult = 1;
	public double transform_adjust = 1;
	public bool adjust_me;
	public double efficiency = 1, energy = 100;

	public const double BluePct = 33;
	public const double RoyalePct = 50;
}
