using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// O LUTADOR EM COMBATE: a ficha (<see cref="Fighter"/>) mais tudo que so existe enquanto
/// se troca golpe -- o corpo em partes, a guarda, o cronometro do proximo soco.
///
/// Fica separado da ficha de proposito. O <see cref="Fighter"/> tem 235 campos e vai inteiro
/// pro savefile; nada daqui deve ser gravado, porque nada daqui sobrevive a desconexao. Uma
/// guarda erguida ou um atordoamento nao sao patrimonio do personagem.
/// </summary>
public sealed class CombatState
{
	public Fighter F;
	public Body Corpo;

	// --- guarda ----------------------------------------------------------
	/// <summary>Guarda erguida (a tecla de bloqueio segurada).</summary>
	public bool Bloqueando;

	/// <summary>Ha quantos segundos a guarda subiu. Bloquear NA HORA vira contra-ataque.</summary>
	public double TempoDeGuarda;

	/// <summary>
	/// O contra-ataque desta guarda ainda esta disponivel.
	///
	/// UM POR SUBIDA DE GUARDA, e com recarga. Sem as duas travas bastava martelar a tecla de
	/// bloqueio pra devolver TODO golpe que chegasse -- a janela reabria a cada toque, e
	/// defender virava a jogada dominante.
	/// </summary>
	public bool ContraPronto;

	/// <summary>Quanto falta pra poder contra-atacar de novo.</summary>
	public double RecargaContra;

	// --- esquiva e mira --------------------------------------------------
	/// <summary>Deflexao: derruba a pontaria de quem ataca. O Zanzoken soma +20 aqui.</summary>
	public double Deflexao;

	/// <summary>Precisao: soma na propria pontaria.</summary>
	public double Precisao;

	/// <summary>
	/// Chance de ESQUIVA AUTONOMA, em porcento. Zero pra todo mundo -- e o Ultra Instinct que
	/// enche este numero. No BYOND a esquiva existia mas nunca era consultada contra socos
	/// (ver o defeito 1 no <see cref="MeleeResolver"/>).
	/// </summary>
	public double ChanceEsquiva;

	/// <summary>
	/// REDUCAO DE DANO em porcento, 0-100. Zero pra todo mundo -- e a Aura of Destruction que enche
	/// este numero (`UE_DR_BASE 15` + `UE_DR_PER_EN 0.10` x energia).
	///
	/// ============================ POR QUE UM CAMPO E NAO UM DESCONTO DEPOIS ============================
	/// A tentacao e reduzir o dano DEPOIS do resolvedor, no servidor. Nao funciona: quando o
	/// resolvedor devolve, o membro JA foi ferido, o corpo ja decidiu se quebrou e a morte ja foi
	/// avaliada -- devolver vida depois seria curar, nao reduzir, e um golpe fatal continuaria fatal.
	///
	/// Entao a reducao mora aqui, igual a esquiva: o Core le, o servidor escreve. E o Core continua
	/// sem saber que existe uma disciplina divina.
	/// ============================================================================================
	/// </summary>
	public double ReducaoDeDano;

	// ============================ O CANAL RACIAL (v0.4.0) ============================
	// Escrito a cada tique pelo `GameServer.Passivas` a partir do `Races.EfeitoRacial` do corpo -- e so por ele. Fica SEPARADO da
	// reducao da aura de proposito: as duas sao escritas por donos diferentes a cada tique, e uma por cima da outra apagaria a outra.
	// ===================================================================================

	/// <summary>(v0.4.0) A REDUCAO DE DANO DAS PASSIVAS RACIAIS, em %, combinada com a da aura no <see cref="MeleeResolver"/>.</summary>
	public double ReducaoRacial;

	/// <summary>(v0.4.0) O dano que ESTE corpo DA (soco; o de ki o servidor multiplica no tiro) e o que ele RECEBE antes da reducao.</summary>
	public double DanoDadoRacial = 1, DanoRecebidoRacial = 1;

	/// <summary>(v0.4.0) EXTREME TENACITY: cada membro com pouca vida apanha menos (`Races.Passivas.ReducaoTenaz`).</summary>
	public bool Tenaz;

	/// <summary>(v0.4.0) SEM RECUO: nem arremesso nem atordoamento (Unstoppable Force, Primal Bloodlust).</summary>
	public bool Inabalavel;

	/// <summary>(v0.4.0) SEM EFEITOS NEGATIVOS: debuffs de membro, atordoamento e debuffs de skill inimiga (Unmatched Ferocity, Sheer
	/// Willpower, Blazing Spirit).</summary>
	public bool SemEfeitosNegativos;

	/// <summary>(v0.4.0) INVULNERAVEL -- a animacao da Legendary Resurrection. Entra no <see cref="Intocavel"/>.</summary>
	public bool Invulneravel;

	/// <summary>(v0.4.0) A escala racial dos debuffs de membro (Warrior's Vitality 0,5; o Demonic 1,1) -- `DebuffsDeMembro.Escalar`.</summary>
	public double FatorDosMembros = 1;

	/// <summary>(v0.4.0) O RITMO DO NOCAUTE: 0,5 = os relogios do nocaute andam no dobro (Namekian Resilience, Extreme Tenacity).</summary>
	public double FatorDoNocaute = 1;

	/// <summary>A escala de membro que vale agora: a racial, ou ZERO sem efeitos negativos.</summary>
	public double EscalaDosMembros => SemEfeitosNegativos ? 0 : FatorDosMembros;

	/// <summary>Zona que este lutador esta mirando: "cabeca", "torso", "bracos", "pernas".</summary>
	public string? ZonaMirada;

	/// <summary>
	/// O `murderToggle`. DESLIGADO por padrao: um soco nao-letal nunca leva um membro abaixo
	/// do limiar de nocaute, entao da pra treinar e brigar sem matar sem querer.
	/// </summary>
	public bool Letal;

	// --- cronometros (em segundos) ---------------------------------------
	/// <summary>Quanto falta pra poder golpear de novo.</summary>
	public double Recarga;

	/// <summary>
	/// GOLPES SEGUIDOS que encostaram. Serve pro NIVEL do som de impacto -- o original
	/// escolhe entre baque pequeno, medio e grande com `min(3, tipo + min(1, combo_count))`,
	/// e e isso que faz uma sequencia de socos ESQUENTAR em vez de virar um metronomo.
	/// Zera ao errar, ao ser aparado e sozinho depois de <see cref="JanelaDeCombo"/>.
	/// </summary>
	public int Combo;
	public double ComboAte;

	/// <summary>Quanto tempo um golpe conta como "seguido" do anterior.</summary>
	public const double JanelaDeCombo = 1.2;

	/// <summary>Marca mais um golpe encaixado.</summary>
	public void SomarCombo()
	{
		Combo = ComboAte > 0 ? Combo + 1 : 1;
		ComboAte = JanelaDeCombo;
	}

	public void ZerarCombo() { Combo = 0; ComboAte = 0; }

	/// <summary>Quanto falta de atordoamento. Acima de zero, nao ataca nem anda direito.</summary>
	public double Stun;

	/// <summary>
	/// A TAG DE "EM COMBATE" (90 s no original) -- e ela e so DISPLAY e MUSICA.
	///
	/// Nao confundir com <see cref="LutandoDeVerdade"/>: sao dois relogios com duracoes e
	/// propositos diferentes, e o DM os mantem separados de proposito (ver `CombatKnobs`).
	/// </summary>
	public double EmCombate;

	/// <summary>
	/// O RELOGIO DA MECANICA: 10 s desde o ultimo golpe. E ele que alimenta o `IsInFight` --
	/// velocidade de combate, regeneracao de Ki pela metade, ganho de skill.
	/// </summary>
	public double LutandoDeVerdade;

	/// <summary>
	/// A BRIGA COM ALGUEM: 90 s desde o ultimo golpe trocado com OUTRO corpo, dado ou levado. E a `combatTag` do DM
	/// ao pe da letra -- la o `refresh_combat_tag()` so e chamado em golpe entre mobs (`attack cmn.dm:153-156`,
	/// `objects.dm:304-305`, `BeamClash.dm:204-205`), e o `SpreadDamage` da gravidade (`Gravity.dm:56`, que cai no
	/// `Injuries.dm:63-87`) nao o chama.
	///
	/// A <see cref="EmCombate"/> deste port e MAIS LARGA, e ha regra que conta com isso: o dano SEM ALGOZ (o
	/// esmagamento da gravidade e do peso, o vacuo, o preco de uma tecnica no proprio corpo) tambem a arma, e e por
	/// isso que quem sufoca nao regenera (ver `Vacuo.DanoPorSegundo`). Mas um corpo num chao mais pesado que ele --
	/// ou treinando com peso acima do limite -- fica com a tag acesa o tempo INTEIRO, um dano por segundo, sem ter
	/// trocado um golpe com ninguem. Quem precisa saber se o corpo esta BRIGANDO, e nao so apanhando do mundo, le
	/// <see cref="EmBrigaComAlguem"/>. O primeiro leitor e a lenha (*"durante combate n conta"*, `GameServer.Lenha.cs`).
	/// </summary>
	public double BrigandoComAlguem;

	/// <summary>
	/// O corpo esta em combate COM ALGUEM? A tag esta de pe e foi um golpe trocado com outro corpo que a armou nos
	/// ultimos 90 s. Pede as duas de proposito: quem zera a tag (a morte, a volta do transe) encerra a briga junto,
	/// sem que cada um desses lugares precise conhecer este relogio.
	/// </summary>
	public bool EmBrigaComAlguem => EmCombate > 0 && BrigandoComAlguem > 0;

	/// <summary>
	/// ============================ O NOCAUTE E UM COMA COM TETO, E NAO UM CRONOMETRO ============================
	/// **O DM NAO DA PRAZO AO NOCAUTE DE LUTA.** Ele nasce em `Injuries.dm:283` como `KO(-1)`, e em
	/// `KO.dm:112-117` os dois ramos que agendariam o despertar sao `if(KOtimer>0)` e
	/// `else if(!KOtimer)` -- **`-1` nao e nenhum dos dois**, entao nenhum `spawn` casa e ninguem
	/// agenda o `Un_KO`. Quem acorda o corpo e `Injuries.dm:286-289`: o nucleo ferido subir de volta
	/// acima da linha de 20%. **A CURA E QUE DECIDE A HORA DE LEVANTAR** -- ver
	/// <see cref="NocautePorVital"/>.
	///
	/// Este campo continua existindo como **TETO** (ver `MeleeResolver.TetoDoNocaute`) e como o
	/// relogio dos nocautes que o DM realmente cronometra: o de HP zerado (`KO.dm:116`), o do
	/// `Death.dm:117` (`KO(20)`) e o do reflexo da mente.
	/// =====================================================================================================
	/// </summary>
	public double NocauteRestante;

	/// <summary>
	/// O `vitalKOd` do DM (`Injuries.dm:280-289`). LIGADO: este corpo caiu porque um NUCLEO cedeu, e
	/// entao ele levanta quando o nucleo voltar acima da linha -- e nao quando um relogio vencer.
	/// DESLIGADO: e um nocaute cronometrado (o verb de admin, o reflexo da mente), e so o relogio o
	/// levanta.
	///
	/// **A DISTINCAO E DO ORIGINAL E NAO E DETALHE**: sem ela, o `else if(vitalKOd)` de
	/// `Injuries.dm:287` acordaria de imediato todo mundo que foi nocauteado por outra razao, porque
	/// o nucleo desses corpos nunca esteve abaixo da linha.
	/// </summary>
	public bool NocautePorVital;

	/// <summary>
	/// ============================ O DESMAIO TEM UM PISO (dono, 2026-10-15) ============================
	/// *"majin n ta dando pra ser nocauteado no soco, tem o efeito de KO mas ele n desmaia,ele deveria desmaiar mas
	/// levantar mais rapido q outras raças"*.
	///
	/// O nocaute por NUCLEO acaba quando o nucleo volta acima da linha (<see cref="NocautePorVital"/>), e quem cura EM
	/// COMBATE (o `fastRegen` -- ver `PerfilDeRegen.CuraEmCombate`) a recupera em fracao de segundo: o Majin, com o vigor
	/// travado no cheio, subia os 20% antes de o corpo encostar no chao, e o golpe dizia "KO" pra um corpo de pe. DESVIO
	/// DECLARADO DO DM (`Injuries.dm:286-289` acorda no mesmo ciclo da cura): o desmaio por nucleo dura pelo menos isto.
	/// Pra quem nao cura em luta o piso nunca morde -- a tag de combate segura a cura por muito mais tempo --, e e por isso
	/// que o Majin continua levantando antes de todo mundo.
	/// ===============================================================================================
	/// </summary>
	public const double SegundosMinimosDoDesmaio = 8;

	/// <summary>Quanto ainda falta do <see cref="SegundosMinimosDoDesmaio"/> deste desmaio (zero = a cura ja levanta o corpo).</summary>
	public double DesmaioMinimo;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): o desmaio por nucleo volta a acabar no ciclo em que o nucleo sara -- o
	/// jogo de antes. Lido pelo <see cref="Nocautear"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool DesmaioSemMinimoDeTeste;

	/// <summary>
	/// ============================ O MAJIN DESFEITO EM PEDACOS (dono, 2026-10-15) ============================
	/// *"majin "morto" por soco n morre de verdade ele so fica em um KO mais longo e começa uma animaçao q tinha no DM
	/// representando ele regenerando e voltando a vida, morrer pra ki é oq mata definitivamente"*.
	///
	/// E o ramo `DeathRegen >= 6` do `TestDeathRegen` (`DeathRegen.dm:152-173`): o corpo some, cinco pedacos de
	/// `Majin1.dmi` ficam espalhados em volta, *"You will regenerate in 30 seconds"*, e no fim os pedacos voltam e ele se
	/// levanta INTEIRO (`SpreadHeal(100)` e todo membro decepado refeito, `:58` e `:71-73`). Naquele DM a porta estava
	/// fechada pra todo mundo (`:39`, *"SEM respawn automatico por tempo"*); o dono a reabre so pro Majin, e so fora do ki
	/// -- ver <see cref="QuandoONucleoCede"/>.
	///
	/// DESFEITO = CAIDO + INTOCAVEL: o DM tirava o corpo do mapa (`loc=locate(100,100,25)`, `:69`); aqui ele fica no
	/// lugar, ninguem o acerta (<see cref="Intocavel"/>) e a tela desenha os pedacos (`Ocupacao.Desfeito`). O relogio e o
	/// do nocaute (<see cref="NocauteRestante"/>), e quem o levanta e o <see cref="Levantar"/>, que o devolve inteiro.
	/// ====================================================================================================
	/// </summary>
	public bool Desfeito;

	/// <summary>Os segundos em pedacos -- *"You will regenerate in 30 seconds"* (`DeathRegen.dm:162`).</summary>
	public const double SegundosDesfeito = 30;

	/// <summary>
	/// ESTE CORPO SE DESFAZ EM VEZ DE MORRER? Gancho, e nao campo, pelo motivo do <see cref="MortoDePe"/>: quem sabe a
	/// raca (o `Race=="Majin"||Parent_Race=="Majin"` do DM) e o servidor. Nulo = ninguem se desfaz.
	/// </summary>
	public Func<CombatState, bool>? SeDesfazAoMorrer;

	/// <summary>Quem ouve o corpo se desfazer: as frases (`GameServer.MajinDesfeito.cs`). Chamado depois de tudo escrito.</summary>
	public Action<CombatState>? AoSeDesfazer;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): o Majin volta a ser so mais uma raca que refaz membro -- o golpe que o
	/// mataria o poe em coma, e o ki nao o mata. Lido pelo <see cref="QuandoONucleoCede"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MajinNaoSeDesfazDeTeste;

	/// <summary>
	/// O `limbregenbuffer` (`Injuries.dm:296-300`): pontos juntados rumo a devolver UM membro
	/// perdido. So o Majin, o Bio e as racas de `canheallopped` juntam -- ver
	/// <see cref="Regeneracao.PontosPorSegundo"/>. Nao vai pro savefile: um membro pela metade nao e
	/// patrimonio, e o DM tambem o perde no relog.
	/// </summary>
	public double BufferDeMembro;

	/// <summary>
	/// CARENCIA DE RENASCIMENTO, em segundos. Quem acabou de voltar nao pode ser atingido.
	///
	/// Nao e cortesia: sem isto, morrer ao lado de quem matou e renascer no mesmo ponto vira
	/// laco -- o teste de rede com dois robos mostrou exatamente isso, morte atras de morte
	/// no mesmo lugar. A carencia CAI no instante em que o renascido golpeia: escudo pra
	/// levantar e ir embora, nao pra voltar batendo.
	/// </summary>
	public double Carencia;

	/// <summary>
	/// ============================ ESTE CORPO ESTA PRESO NUMA CINEMATICA AGORA? ============================
	/// O dono: *"durante cinematicas de transformacao, o personagem ficaria IMUNE A DANOS pra ninguem
	/// poder MATAR o player ou NPCs enquanto ele transforma"*.
	///
	/// **E PORTE, NAO DESENHO NOVO.** No DM a flag existe e se chama `attackable`, e ela nao mora na
	/// proc da cena -- mora nas procs que a CHAMAM, envolvendo a chamada: `supersaiyanbuff.dm:314`
	/// zera, `:320` roda o `SSJCinematic()`, `:350` devolve o um; o mesmo par cerca o USSJ (`:387`/
	/// `:410`), o SSJ2 (`:435`/`:467`), o SSJ3 (`:480`/`:530`) e mais quatro degraus, e se repete em
	/// `lssjbuff.dm`, `IcerTransform.dm:102-104`, `HeranBuff.dm:99`, `perfecttrans.dm:3` e
	/// `imperfecttrans.dm:3`. O `move = 0` das procs de cena NAO e a imunidade: em `attack cmn.dm:98`
	/// o `move` e do ATACANTE e o `attackable` e do ALVO -- portar so o `move` seria portar metade.
	///
	/// **GANCHO E NAO CAMPO**, pelo mesmo motivo do <see cref="NegarMorte"/> la embaixo: quem sabe da
	/// cena e o servidor (`ServerPlayer.CenaSegundos`, escrito so pelo `MarcarCena` e abatido so pelo
	/// `TickDaForma`), e o Core nao conhece forma, cliente nem cinematica. **Ele PERGUNTA, ele nao
	/// guarda.** Um segundo bit aqui precisaria ser apagado em todo jeito de a cena acabar -- o prazo
	/// vencendo, o nocaute no meio dela, a morte, a reversao pra base -- e este port ja perdeu duas
	/// vezes exatamente assim (o antigo prazo do alem e a aureola). Derivado, o escudo cai no MESMO gesto em
	/// que a cena cai, e nao ha linha pra ninguem esquecer.
	///
	/// Instalado uma vez por corpo, no `GameServer.PrepararCombate` -- que e por onde passam o jogador
	/// (`Entrar`) e o corpo sem dono (`PorNoMundo`), entao **NPC tem o mesmo escudo que o jogador**,
	/// sem crivo por tipo de corpo.
	/// ==================================================================================================
	/// </summary>
	public Func<bool>? EmCinematica;

	/// <summary>
	/// NAO PODE SER ATINGIDO AGORA -- e as tres razoes (a carencia, a cinematica e o Majin em pedacos, <see cref="Desfeito"/>)
	/// moram na MESMA pergunta de proposito.
	///
	/// Sao dezoito leitores espalhados pelo servidor (a busca do soco, o arranque, a mira, o varredor
	/// do projetil, o cone das tecnicas, o embate de ki, o dano em area). Uma segunda propriedade pra
	/// a imunidade de cinematica significaria visitar os dezoito e acertar todos -- e o que sobrasse
	/// seria o "as vezes" que ninguem consegue reproduzir.
	/// </summary>
	public bool Intocavel => Carencia > 0 || Desfeito || Invulneravel || Enterrado || Possuido || EmCinematica?.Invoke() == true
							 || AoLeme?.Invoke() == true;

	/// <summary>
	/// (2026-10-19) AO LEME DE UMA NAVE: o casco blinda quem pilota -- a setima razao do <see cref="Intocavel"/> (o dono: *"faça com q
	/// nenhum efeito externo afete o personagem enquanto esta pilotando a nave"*; ver `GameServer.Leme.cs`). Por ultimo de proposito: e a
	/// unica razao que custa um laco (as naves do servidor), e quase todo corpo ja saiu nas de cima.
	///
	/// **GANCHO E NAO CAMPO**, pelo motivo do <see cref="EmCinematica"/>: quem sabe quem esta ao leme e o servidor (o `PilotoId` da nave),
	/// e o escudo cai no MESMO gesto em que o leme e largado -- desembarcar, voltar a ponte, a nave explodir --, sem bit pra apagar.
	///
	/// NA NAVE GRANDE O CORPO DA PONTE DIVIDE ESTE ESTADO com quem esta ao leme (`GameServer.CorpoLargado.cs`), entao ele tambem fica fora de
	/// alcance enquanto o dono pilota -- o dono escolheu assim (2026-10-19, "blindar os dois ao leme"): largou o leme, o corpo volta a
	/// poder apanhar, e a te acordar.
	/// </summary>
	public Func<bool>? AoLeme;

	/// <summary>
	/// (2026-10-17) A LARVA ENTERRADA (`GameServer.Larva.cs`): debaixo da terra ninguem a alcanca -- a quinta razao do <see cref="Intocavel"/>.
	/// Quem a escreve e o servidor, ao enterrar e ao sair da terra.
	/// </summary>
	public bool Enterrado;

	/// <summary>
	/// (2026-10-18) O CORPO DE VERDADE DE QUEM FOI POSSUIDO pelo Neo Tuffle (`GameServer.NeoTuffle.cs`): enquanto o Tuffle anda no lugar
	/// dele, este fica escondido com o dono assistindo, e ninguem o alcanca -- a sexta razao do <see cref="Intocavel"/>. Quem a escreve e o
	/// servidor, ao entrar e ao sair.
	/// </summary>
	public bool Possuido;

	/// <summary>
	/// ============================ ESTE CORPO ESTA SENDO ARREMESSADO AGORA? ============================
	/// **PORTE QUE FALTAVA, E ELE E LITERAL.** O efeito de knockback do DM escreve DUAS linhas ao entrar
	/// (`Movement Effects.dm:39-40`):
	///
	///     target.KB += 1
	///     target.canfight -= 1        &lt;-- esta
	///
	/// e as devolve ao sair (`:52-53`). E `canfight` e a **primeira** recusa do `testAttack()`
	/// (`attack_bck.dm:175`: `if(attacking || ... || !canfight || KO ...) return FALSE`), que e o portao
	/// de todo `Attack()` do jogo. Ou seja: **no original, quem esta voando pelo golpe do outro NAO SOCA.**
	///
	/// O port tinha portado o `KB` (o <see cref="ServerPlayer.TiquesDeVoo"/>, que ja deita o corpo e
	/// recusa o passo) e **nao** o `canfight`. O resultado media exatamente o relato do dono: o corpo
	/// arremessado continuava aceitando `Atacar` durante os 0,8-0,9 s de voo, e cada um daqueles socos
	/// saia do ramo do vazio (`AlvoNaFrente` devolve nulo porque o inimigo ficou 512 px pra tras) --
	/// **animacao e assobio de soco no ar, sem dodge, sem mensagem e sem acerto**. *"por uns segundos
	/// (1 ou 2) MEUS SOCOS N ACERTAM ELE"*: um ou dois segundos e a duracao do voo.
	///
	/// **GANCHO E NAO CAMPO**, pelo mesmo motivo do <see cref="EmCinematica"/> logo acima: quem sabe do
	/// voo e o servidor (`ServerPlayer.TiquesDeVoo`, escrito so pelo `Arremessar` e abatido so pelo
	/// `TickDoEmpurrao`), e o Core nao conhece tique de voo nem correcao de posicao. Derivado, a recusa
	/// morre no MESMO instante em que o voo morre -- inclusive quando ele acaba na parede, no outro
	/// corpo ou porque o `Arremessar` foi chamado de novo. Nao ha bit pra ninguem esquecer de apagar.
	///
	/// Instalado uma vez por corpo, no `GameServer.PrepararCombate` -- entao **NPC e clone tem a mesma
	/// recusa que o jogador**, sem crivo por tipo de corpo.
	/// ==================================================================================================
	/// </summary>
	public Func<bool>? SendoArremessado;

	/// <summary>
	/// ============================ COM QUE RITMO A RECARGA DO SOCO ESCORRE ============================
	/// `if(hasTime) attacking = max(0, attacking - (armsLost>=2 ? 0.5 : 1))` (`movement handler.dm:167`): no DM a
	/// espera do soco nao e um prazo, e um contador que DESCE por tique -- e sem os dois bracos ele desce a
	/// metade. Portado como esta: o ritmo multiplica o `dt` do <see cref="Tick"/>, entao perder (ou recuperar) um
	/// braco no meio da espera muda o resto dela na hora, como la.
	///
	/// **GANCHO E NAO CAMPO**, pelo motivo dos dois de cima: a regra e do corpo (`DebuffsDeMembro.RitmoDaRecarga`,
	/// funcao pura dos membros de agora -- nao ha bit pra apagar quando o braco volta), mas QUEM a paga e do
	/// servidor. No DM a linha mora no `unitimer()`, o laco do JOGADOR; o NPC tem relogio proprio e desce
	/// `attacking-1` cru (`NPCAI.dm:892`). Quem sabe quem dirige o corpo e o `GameServer.RitmoDaRecargaDe`.
	/// Nulo = ritmo cheio.
	/// ================================================================================================
	/// </summary>
	public Func<double>? RitmoDaRecarga;

	/// <summary>
	/// Composicao do golpe por tipo, e o que o corpo resiste. Os defaults sao os do jogo:
	/// soco = 2 de fisico + 1 de energia contra resistencias 1, o que da o 1,5x conhecido.
	/// </summary>
	public Dictionary<string, double> TiposDeDano = new() { ["Physical"] = 2, ["Energy"] = 1 };
	public Dictionary<string, double> Resistencias = new() { ["Physical"] = 1, ["Energy"] = 1 };

	public CombatState(Fighter f, bool comRabo = false, PerfilDeRegen? regen = null)
	{
		F = f;
		Corpo = Body.Novo(comRabo);
		// O EIXO DA CURA ENTRA AQUI E SO AQUI. Era um `bool regenera` com uma lista de racas cravada
		// no servidor; agora e o genoma (`misc_stats["Regeneration"]`) -- ver `PerfilDeRegen`.
		Corpo.Regen = regen ?? PerfilDeRegen.Comum;
		SincronizarVida();
	}

	/// <summary>
	/// A vida da ficha e SEMPRE derivada do corpo. Escrever `HP` em qualquer outro lugar
	/// criaria duas verdades, e a que o jogador ve (a barra) e sempre a do corpo.
	/// </summary>
	public void SincronizarVida() => F.HP = Corpo.Vida();

	/// <summary>
	/// ============================ O FUNIL UNICO DO DANO NUM MEMBRO ============================
	/// Todo dano que ENCOSTA neste corpo entra por aqui: o soco (<see cref="MeleeResolver"/>), o raio
	/// de ki, a explosao e o dano em area (`EspalharDanoG3`), o esmagamento por gravidade, o calor da
	/// estrela, a sobrecarga da carga de Ki, o Kaio-ken que cobra o proprio corpo e o recuo da Aura of
	/// Destruction.
	///
	/// **ELE NASCEU MEDIDO, E O NUMERO E O ARGUMENTO**: havia SETE chamadas de `Corpo.Ferir` fora de
	/// bancada e QUATRO delas nao passavam por crivo nenhum -- o calor da estrela (`GameServer.Sol`), o
	/// recuo da aura, a sobrecarga de Ki e o Kaio-ken. Uma imunidade escrita so no caminho do soco e
	/// uma imunidade que o RAIO ignora, e quem morre transformando reporta "as vezes", que e o defeito
	/// que nao se acha. Com o funil, o crivo e um so e uma fonte nova de dano ja nasce coberta.
	///
	/// **DEVOLVE SE O CORPO REALMENTE PERDEU VIDA.** Quem conta nocaute e morte DEPOIS do laco precisa
	/// saber disso: um corpo que ja estava no limiar do nocaute quando a cena comecou cairia por um
	/// dano que nao aconteceu, e o escudo teria derrubado quem ele protegia.
	///
	/// `Corpo.Ferir` continua publico e sem crivo -- as bancadas machucam corpo de proposito, e uma
	/// bancada que nao consegue mais quebrar um membro nao mede nada.
	/// ========================================================================================
	///
	/// ============================ E ELE E O `DamageMe` INTEIRO: FERE **E** ARRANCA ============================
	/// `mobparts_logic.dm:85-97`: `health -= number; ... if(health <= 0 && !nonlethal) LopLimb()`. No
	/// original a amputacao mora DENTRO do dano -- nao ha como ferir um membro por golpe letal ate
	/// zero sem que ele caia, venha o dano de um soco, de uma explosao (`SpreadDamage`,
	/// `Injuries.dm:63-87`) ou de um golpe direto (`DamageLimb`, `:32-48`): os tres chamam o mesmo
	/// `DamageMe`.
	///
	/// **NO PORT ISSO ESTAVA SO NO SOCO.** O `MeleeResolver` arrancava depois de ferir; o dano em area
	/// e o dano direto (`EspalharDanoG3`, `FerirUmMembroG10`) feriam pelo mesmo funil e paravam ai --
	/// um braco zerado por uma tecnica ficava com `Vida = 0`, inteiro no corpo, e nenhuma peca nascia.
	/// Foi assim que o dono viu "o body part nao esta spawnando": ele viu a metade dos funis que nao
	/// tinha a cauda. Aqui a cauda mora no funil, e um quarto caminho de dano nasce com ela.
	///
	/// O QUE CAI SAI PELO <see cref="AoDecepar"/> -- a peca no chao e o Ki descontado sao consequencia
	/// de <see cref="Arrancar"/>, e quem chamou `Ferir` le o resultado no proprio membro
	/// (`membro.Decepado`). Nao ha lista de retorno pra ninguem esquecer de esvaziar.
	/// =========================================================================================================
	/// </summary>
	public bool Ferir(BodyPart membro, double dano, bool letal)
	{
		if (Intocavel || dano <= 0) return false;
		Corpo.Ferir(membro, dano, letal);
		GolpesQueFeriram++;

		// O `if(health <= 0 && !nonlethal) LopLimb()` do DM, com as regras que o port ja tinha no
		// soco: so golpe LETAL arranca, so um membro que ZEROU, so `Vitalidade.Membro` (nucleo zerado
		// e morte, nao amputacao -- cabeca arrancada e coisa de tecnica, nao de troca de golpes), e o
		// aninhado (mao, pe, cerebro, orgaos) nao cai sozinho: ele vai de carona no membro de fora.
		//
		// `SemArrancarDeTeste` e o DEFEITO INJETAVEL desta cauda -- ver o campo. `MembrosFirmes` e a regra do
		// corpo que nao perde membro (o bicho) -- ver o campo.
		if (letal && !SemArrancarDeTeste && !MembrosFirmes && membro.Vida <= 0
			&& membro.Papel == Vitalidade.Membro && !membro.Aninhado)
			Arrancar(membro);

		return true;
	}

	/// <summary>
	/// (2026-10-18) QUANTAS VEZES UM DANO ENCOSTOU NESTE CORPO pelo funil unico (<see cref="Ferir"/>) -- so sobe. Quem quer saber "apanhou
	/// desde entao?" guarda o numero e compara: o Sleep do Majin (`GameServer.SonoDoMajin.cs`). Uma regua pela vida somada das partes acusava o
	/// que nao era golpe; esta so conta o que passou pelo funil.
	/// </summary>
	public long GolpesQueFeriram;

	/// <summary>
	/// ESTE CORPO NAO PERDE MEMBRO. Ligado pelo servidor no corpo de um ANIMAL (`GameServer.Animais.cs`), e so
	/// nele: todo corpo do jogo e o humanoide do <see cref="Body.Novo"/>, e a amputacao e desenhada como tal --
	/// a peca que cai no chao e um braco de gente (`Body.PecaDe`) e a mascara apaga a coluna do braco no sprite
	/// (`CharacterVisual.AplicarAmputacao`). Num lobo isso seria um braco humano no chao e um buraco no meio do
	/// desenho. O membro dele ZERA como o de qualquer um (e o corpo cai e morre pela mesma conta); so nao sai.
	/// </summary>
	public bool MembrosFirmes;

	/// <summary>
	/// ============================ `LopLimb()` -- `mobparts_logic.dm:104-128` ============================
	/// ARRANCA ESTE MEMBRO: a cascata do <see cref="Body.Decepar"/> (o `parentlimb` do original, que
	/// la nunca rodava -- defeito 2 do <see cref="MeleeResolver"/>), o Ki que o corpo perde
	/// (`savant.Ki -= 0.2*savant.MaxKi`, `:112-113`) e o aviso a quem escuta -- que e por onde o
	/// `SpawnLop()` (`:144-146`) acontece no servidor.
	///
	/// SAO DUAS PORTAS PRA ELE, e as duas sao do DM: a do dano (<see cref="Ferir"/>, o `DamageMe`) e a
	/// do rabo pelas costas (`MeleeResolver.ArrancarRabo`, o `CombatMovement.dm:309-314`, que arranca
	/// sem o rabo ter zerado). Uma terceira que aparecer entra por aqui -- e ganha a peca no chao e o
	/// Ki de graca, em vez de copiar as tres linhas e esquecer uma.
	///
	/// Devolve o que caiu (vazio se o membro ja estava fora). O membro ja decepado NAO cai de novo.
	/// ====================================================================================================
	/// </summary>
	public List<BodyPart> Arrancar(BodyPart membro)
	{
		List<BodyPart> caiu = Corpo.Decepar(membro);
		if (caiu.Count == 0) return caiu;

		F.Ki = Math.Max(0, F.Ki - F.MaxKi * Regras.CustoDeceparKi);

		// DEPOIS DE TUDO ESCRITO, como o `AoMorrer`: quem ouve le o corpo pra decidir onde a peca
		// nasce e o que o rabo caido muda no treino, e um gancho chamado no meio veria o membro
		// ainda no lugar.
		AoDecepar?.Invoke(this, caiu);
		return caiu;
	}

	/// <summary>
	/// ============================ ESTE CORPO ACABOU DE PERDER UM MEMBRO -- o `SpawnLop` ============================
	/// O irmao do <see cref="AoMorrer"/>, e nasceu pelo mesmo motivo: a peca no chao era emitida em
	/// UM lugar do servidor (`SoltarPecas`, pendurado no anuncio do soco), lendo uma lista que so o
	/// resolvedor de socos preenchia. Todo outro caminho que decepasse -- e agora todo `Ferir` letal
	/// decepa -- teria que lembrar de emitir, e o dia em que um esquecesse seria "o membro caiu e nada
	/// apareceu no chao", que ninguem liga ao codigo depois.
	///
	/// Com o gancho, quem emite e quem arrancou (<see cref="Arrancar"/>), e ha UM emissor: o servidor
	/// instala `SoltarPecas` aqui no `PrepararCombate`, pro jogador, pro NPC e pro cadaver.
	///
	/// O Core continua sem saber o que e um decalque, uma zona ou um pacote -- ele so diz QUAIS partes
	/// sairam do corpo, na ordem da cascata (o membro de fora primeiro).
	/// ===================================================================================================================
	/// </summary>
	public Action<CombatState, List<BodyPart>>? AoDecepar;

	/// <summary>
	/// ============================ O DEFEITO INJETAVEL DA CAUDA UNICA ============================
	/// **FALSO EM JOGO, SEMPRE** -- so a `--pecateste` o liga, num corpo forjado dela. Ligado, o
	/// <see cref="Ferir"/> volta a ser o que era antes da cauda: fere e para, sem `LopLimb`. E a
	/// reproducao letra por letra do defeito que o dono relatou (um membro zerado por dano em area
	/// que continua no corpo e nao poe peca nenhuma no chao).
	///
	/// MORA NO CORE, e nao na bancada, pela regra da casa que o `_cadaverNoPlayersDeTeste` e o
	/// `_dcGradeCega` ja seguem: o criterio tem que ser o MESMO objeto com e sem o defeito. Uma copia
	/// do `Ferir` escrita na bancada mediria a copia concordando consigo mesma. Custa um `bool` por
	/// golpe que zera um membro -- que e o evento mais raro do combate.
	/// ============================================================================================
	/// </summary>
	public bool SemArrancarDeTeste;

	/// <summary>Passagem de tempo: cronometros, guarda, saida do nocaute.</summary>
	public void Tick(double dt)
	{
		// A RECARGA ESCORRE NO RITMO DO CORPO: sem os dois bracos, a meio ritmo -- ver `RitmoDaRecarga`.
		if (Recarga > 0) Recarga = Math.Max(0, Recarga - dt * (RitmoDaRecarga?.Invoke() ?? 1));
		if (RecargaContra > 0) RecargaContra = Math.Max(0, RecargaContra - dt);
		if (ComboAte > 0 && (ComboAte = Math.Max(0, ComboAte - dt)) == 0) Combo = 0;
		if (Carencia > 0) Carencia = Math.Max(0, Carencia - dt);
		// (v0.4.0) SEM RECUO E SEM NEGATIVOS, o atordoamento cai no tique: e um funil so pros vinte lugares que o escrevem.
		if (Stun > 0) Stun = Inabalavel || SemEfeitosNegativos ? 0 : Math.Max(0, Stun - dt);
		if (EmCombate > 0) EmCombate = Math.Max(0, EmCombate - dt);
		if (LutandoDeVerdade > 0) LutandoDeVerdade = Math.Max(0, LutandoDeVerdade - dt);
		if (BrigandoComAlguem > 0) BrigandoComAlguem = Math.Max(0, BrigandoComAlguem - dt);

		TempoDeGuarda = Bloqueando ? TempoDeGuarda + dt : 0;

		F.IsInFight = LutandoDeVerdade > 0;   // o CURTO, nao a tag -- ver CombatKnobs.LutaDeVerdade

		if (!F.KO) return;

		// O RELOGIO SO ANDA SE ALGUEM O ARMOU. `Nocautear()` e a porta unica que arma; uma bancada
		// que escreve `Ficha.KO = true` na mao continua com o corpo caido ate mandar levanta-lo, que
		// e o que ela quis dizer.
		bool armado = NocauteRestante > 0;
		// (v0.4.0) O RITMO RACIAL DO NOCAUTE: 0,5 = os dois relogios andam no dobro -- ver `FatorDoNocaute`.
		double dtDoNocaute = dt / Math.Max(FatorDoNocaute, 0.05);
		if (armado) NocauteRestante = Math.Max(0, NocauteRestante - dtDoNocaute);
		// O PISO DO DESMAIO anda junto -- ver `SegundosMinimosDoDesmaio`.
		if (DesmaioMinimo > 0) DesmaioMinimo = Math.Max(0, DesmaioMinimo - dtDoNocaute);

		// ============================ QUEM ACORDA O CORPO E A CURA, E DEPOIS O TETO ============================
		// A ordem e a de `Injuries.dm:277-289`, e ela e ESTA e nao a inversa: o coma por nucleo acaba
		// quando o nucleo volta acima da linha, e o relogio e so a rede de seguranca de quem nao
		// consegue subir (ninguem regenera dentro de uma estrela em chamas, por exemplo).
		//
		// `DeveMorrer()` entra junto porque um nucleo DECEPADO nao satisfaz `DeveNocautear()` -- ele
		// nao esta "abaixo do limiar", ele nao esta. E o coma do Majin sem cabeca, e ele nao pode
		// terminar pela primeira pergunta.
		if (NocautePorVital && DesmaioMinimo <= 0 && !Corpo.DeveNocautear() && !Corpo.DeveMorrer()) Levantar();
		else if (armado && NocauteRestante == 0) Levantar();
	}

	/// <summary>
	/// Pode golpear agora? Nocauteado, morto, atordoado, ARREMESSADO, ou ainda em recarga: nao.
	///
	/// A ordem e a do `testAttack()` do DM (`attack_bck.dm:175`), e as quatro primeiras recusas dele
	/// estao todas aqui: `attacking` (a <see cref="Recarga"/>), `!canfight` (o <see cref="Stun"/> **e**
	/// o <see cref="SendoArremessado"/> -- os dois escrevem `canfight -= 1` la, `Movement Effects.dm:40`
	/// e `:93`), `KO` e a morte.
	/// </summary>
	public bool PodeAtacar() =>
		(!F.dead || MortoDePe?.Invoke(this) == true)   // o cadaver nao soca; o fantasma de pe soca (ver `MortoDePe`)
		&& !F.KO && Stun <= 0 && Recarga <= 0 && SendoArremessado?.Invoke() != true;

	/// <summary>
	/// Ergue ou baixa a guarda. Subir REARMA o cronometro (e ele que decide o contra-ataque)
	/// e so libera o contra se a recarga ja tiver passado.
	/// </summary>
	public void Guardar(bool erguida)
	{
		if (erguida && !Bloqueando)
		{
			TempoDeGuarda = 0;
			ContraPronto = RecargaContra <= 0;
		}
		// (2026-10-16) O FANTASMA DE PE TAMBEM ERGUE A GUARDA -- o dono: *"n tem como dar bloqueio morto? deveria"*. A mesma
		// regra do `PodeAtacar`: o cadaver nao, o morto de pe do Outro Mundo sim (ver `MortoDePe`).
		Bloqueando = erguida && !F.KO && (!F.dead || (!MortoNaoBloqueiaDeTeste && MortoDePe?.Invoke(this) == true));
		if (!Bloqueando) ContraPronto = false;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`, familia G): a guarda volta a recusar TODO morto, inclusive o de pe no Outro Mundo
	/// -- o jogo de antes. Lido pelo <see cref="Guardar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MortoNaoBloqueiaDeTeste;

	/// <summary>Marca o inicio (ou a renovacao) da luta pros dois lados.</summary>
	/// <param name="comAlguem">
	/// Falso = o dano veio do MUNDO ou do proprio corpo (autor e vitima sao o mesmo: o esmagamento, o vacuo, o preco
	/// de uma tecnica). A tag e a mecanica armam como sempre; a briga com alguem, nao. Ver <see cref="BrigandoComAlguem"/>.
	/// </param>
	public void EntrarEmCombate(bool comAlguem = true)
	{
		EmCombate = CombatKnobs.TagDeCombate;          // 90 s: a tag que o jogador VE e OUVE
		LutandoDeVerdade = CombatKnobs.LutaDeVerdade;  // 10 s: o que a mecanica sente
		if (comAlguem) BrigandoComAlguem = CombatKnobs.TagDeCombate;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--reverterteste`): o nocaute nao mexe no Ki -- o estado de antes de
	/// 2026-10-10, em que quem caia acima de 100% continuava acima de 100%. Lido pela propria linha do
	/// conserto, em <see cref="Nocautear"/>.
	/// </summary>
	public static bool NocauteGuardaASobrecargaDeTeste;

	/// <summary>
	/// Cai. A guarda cai junto -- quem esta desmaiado nao bloqueia -- e o corpo fica um tempo
	/// desligado antes de responder de novo.
	/// </summary>
	/// <param name="porVital">
	/// Este nocaute veio de um NUCLEO que cedeu? Ver <see cref="NocautePorVital"/>.
	///
	/// **O PADRAO E `false`, e a escolha do lado importa.** Com `true` no padrao, qualquer chamada
	/// sobre um corpo INTEIRO vira no-op: o `Tick` pergunta ao corpo, o corpo responde "estou bem", e
	/// o nocaute se desfaz no quadro seguinte. Foi o que a bancada da mente mostrou -- tres provas
	/// vermelhas, todas em pontos que derrubam um corpo saudavel de proposito (o palco do filme do
	/// Bio, o verb de admin, a segunda queda do reflexo, o velorio). Com `false` no padrao a falha e
	/// pro lado seguro: o corpo fica no chao o tempo pedido, que e sempre o que quem chamou quis.
	///
	/// Passa `true` **so quem acabou de consultar <see cref="Body.DeveNocautear"/>** -- o soco, o
	/// raio, o dano em area, o sol e a sobrecarga de Ki. Esses sao os unicos lugares do port onde
	/// existe um nucleo abaixo da linha pra subir de volta.
	/// </param>
	public void Nocautear(double segundos, bool porVital = false)
	{
		// ============================ O NOCAUTE DISPERSA A SOBRECARGA ============================
		// *"Nocaute reseta o ki: ao ser nocauteado, se o ki estiver acima de 100%, ele volta para 100%. Com
		// 100% ou menos, nada muda."* -- o item 7 do playtest de 2026-10-10.
		//
		// DESVIO DECLARADO DO ORIGINAL: no DM o excesso nunca cai de uma vez, so vaza (`CheckPowerMod`,
		// `Power Control.dm:113-134`), e quem desmaia a 140% continua a 140% ate vazar. Como o `kiratio`
		// entra no BP expresso (`Fighter.PowerLevel`), o corpo ficava deitado com o bonus de poder de quem
		// ainda estava carregado.
		//
		// AQUI E NAO NO SERVIDOR porque este e o funil unico do nocaute: soco, raio, dano em area, a propria
		// sobrecarga, o verb de admin, o reflexo da mente -- todos passam por esta linha.
		// ==========================================================================================
		if (!NocauteGuardaASobrecargaDeTeste && F.Ki > F.MaxKi) F.Ki = F.MaxKi;

		F.KO = true;
		F.train = false;   // `KO.dm:57-58`: o nocaute desliga o treino e a meditacao
		F.med = false;
		Bloqueando = false;
		ContraPronto = false;
		TempoDeGuarda = 0;
		NocauteRestante = segundos;
		NocautePorVital = porVital;
		// (2026-10-15) O PISO DO DESMAIO, so no nocaute por nucleo -- o unico que a cura encerra (ver `SegundosMinimosDoDesmaio`).
		DesmaioMinimo = porVital && !DesmaioSemMinimoDeTeste ? SegundosMinimosDoDesmaio : 0;
	}

	/// <summary>
	/// ============================ LEVANTAR NAO E CURAR -- `Un_KO()` (`KO.dm:126-134`) ============================
	/// O DM da **`SpreadHeal(25, 1, 1)`**: vinte e cinco pontos, **so nos VITAIS**, e so nos que
	/// estao abaixo de 70%. Braco, perna e rabo nao estao nessa lista -- **quem levanta do nocaute
	/// levanta com o braco quebrado do jeito que ele estava**, e e essa a metade do pedido do dono
	/// que o port desfazia sozinho.
	///
	/// Porque o port empurrava **toda** parte pra 1,5x o limiar (30% da vida) de graca ao fim dos
	/// 12 s. Somado ao prazo curto, o nocaute nao era so barato: era o jeito MAIS RAPIDO de curar um
	/// membro quebrado -- 12 s contra os 598 s que o original cobra por um braco inteiro.
	///
	/// O PISO SOBROU, mas so como rede e so nos NUCLEOS: se o TETO do nocaute vencer com um nucleo
	/// ainda abaixo da linha (o corpo dentro de uma estrela, que queima mais rapido do que sara), o
	/// `Tick` o derrubaria no quadro seguinte, pra sempre. Com os 25 pontos do `Un_KO` na frente
	/// isso quase nunca dispara -- um nucleo em 5% sai em 30%.
	/// =========================================================================================================
	/// </summary>
	public void Levantar()
	{
		F.KO = false;
		NocauteRestante = 0;
		NocautePorVital = false;
		DesmaioMinimo = 0;

		// (2026-10-15) O MAJIN QUE SE REFAZ volta INTEIRO: `SpreadHeal(100,1,0)` e todo membro decepado refeito
		// (`DeathRegen.dm:58` e `:71-73`) -- e nao os 25 pontos do `Un_KO` de quem so desmaiou.
		if (Desfeito)
		{
			Desfeito = false;
			Corpo.Restaurar();
			SincronizarVida();
			return;
		}

		Corpo.CurarVitais(CuraDeAcordar);

		// A REDE, e so ela: sem isto o teto vencido devolveria um corpo que cai de novo no proximo
		// tique. Nucleo, e nao "toda parte" -- e o nucleo que decide o nocaute.
		if (Corpo.DeveNocautear())
			foreach (BodyPart p in Corpo.Partes)
			{
				if (p.Decepado || p.Papel != Vitalidade.Nucleo) continue;
				double min = p.VidaMax * Regras.LimiarQuebra * 1.05;
				if (p.Vida < min) p.Vida = min;
			}

		SincronizarVida();
	}

	/// <summary>O `SpreadHeal(25,1,1)` de `Un_KO` (`KO.dm:133`). Vinte e cinco, e so nos vitais.</summary>
	public const double CuraDeAcordar = 25;

	/// <summary>
	/// O NUCLEO CEDEU DE VEZ (<see cref="Body.DeveMorrer"/>) -- e agora? A pergunta UNICA, que o soco, o tiro e o dano
	/// direto fazem do mesmo jeito (`MeleeResolver.AplicarNoMembro` e `GameServer.ConsequenciaDoDano`):
	///   * o corpo comum MORRE;
	///   * a raca que refaz membro (o `canheallopped`) cai em COMA ate o nucleo voltar (`Injuries.dm:276-285`);
	///   * o Majin SE DESFAZ em pedacos (<see cref="Desfeito"/>) -- menos quando foi o KI: *"morrer pra ki é oq mata
	///     definitivamente"* (dono, 2026-10-15).
	/// </summary>
	/// <param name="porKi">O golpe era de ki: o tiro, o raio, a explosao de ki. O soco, a tecnica de corpo, a bomba, nao.</param>
	public FimDoNucleo QuandoONucleoCede(bool porKi)
	{
		if (!Corpo.RegeneraDecepado) return FimDoNucleo.Morre;
		if (MajinNaoSeDesfazDeTeste || SeDesfazAoMorrer?.Invoke(this) != true) return FimDoNucleo.Coma;
		return porKi ? FimDoNucleo.Morre : FimDoNucleo.SeDesfaz;
	}

	/// <summary>
	/// SE DESFAZ: cai pelo RELOGIO do nocaute (<see cref="SegundosDesfeito"/>, e nao pelo nucleo -- o corpo nem esta
	/// inteiro pra "sarar acima da linha"), fica <see cref="Intocavel"/>, e quem o levanta e o <see cref="Levantar"/>, que
	/// o devolve inteiro. Avisa quem ouve (<see cref="AoSeDesfazer"/>) depois de tudo escrito.
	/// </summary>
	public void Desfazer()
	{
		Nocautear(SegundosDesfeito, porVital: false);
		Desfeito = true;
		AoSeDesfazer?.Invoke(this);
	}

	/// <summary>
	/// ALGUEM PODE NEGAR ESTA MORTE. Devolve true quando negou -- e ai o corpo NAO morre.
	///
	/// ============================ POR QUE UM GANCHO E NAO UM `if` ============================
	/// Antes deste gancho havia SETE lugares chamando `Morrer()`: o resolvedor de socos, o dano em
	/// area, a Final Explosion, o Kaio-ken que estoura, a volta no tempo, o verb de admin. Um seguro
	/// contra a morte escrito em UM deles seria um seguro que funciona contra soco e nao contra
	/// Kamehameha -- e o jogador nao teria como saber por que.
	///
	/// Entao a morte passou a ter uma porta so, e quem quiser barra-la se pendura AQUI. Hoje quem
	/// se pendura e a Aura of Destruction (`GameServer.Disciplinas.TentarNegarMorte`), mas o desenho
	/// nao sabe disso: o Core continua sem conhecer disciplina divina nenhuma.
	/// ====================================================================================
	/// </summary>
	public Func<CombatState, bool>? NegarMorte;

	/// <summary>
	/// ESTE MORTO ESTA DE PE (no Outro Mundo)? Gancho e nao campo, pelo mesmo motivo do
	/// <see cref="NegarMorte"/>: quem sabe o LUGAR e o servidor (`ServerPlayer.MortoDePe`, que le a zona),
	/// e este Core nao tem zona. Nulo = nenhum morto esta de pe (o padrao de quem nao pendura nada).
	///
	/// O que ele decide: o fantasma SOCA e APANHA (<see cref="PodeAtacar"/> e os filtros de alvo do
	/// servidor). E o DM: o `Death()` nunca olha `dead` pra recusar golpe, e o Outro Mundo tem os
	/// `Afterlife_Fighter` (`NPClist.dm:324`) pra brigar. O cadaver dos 2 s continua fora de tudo.
	/// </summary>
	public Func<CombatState, bool>? MortoDePe;

	/// <summary>
	/// O FANTASMA "MORREU" DE NOVO -- o `else if(dead)` do `Death()` (`Death.dm:113-118`): `move=1`,
	/// `loc=locate(187,104,6)`, `SpreadHeal(100,1,1)`, `KO(20)`. Nao e uma morte (nao conta, nao paga
	/// Zenkai, nao muda karma): e curado, devolvido a mesa do Enma e posto em coma por 2 s. Quem move e
	/// o servidor (`GameServer.MorrerDeNovoNoAlem`); <see cref="Morrer"/> so avisa e devolve falso.
	/// </summary>
	public Action<CombatState>? AoMorrerDeNovo;

	/// <summary>
	/// ============================ ESTE CORPO ACABOU DE MORRER -- O OUTRO LADO DO `NegarMorte` ============================
	/// O irmao do gancho acima, e ele nasceu pelo mesmo motivo, medido: **`RenasceEm = agora +
	/// MsAteRenascer` estava escrito a mao em OITO lugares** (o resolvedor de socos, a explosao do
	/// planeta, o calor da estrela, a Final Explosion, o Kaio-ken que estoura, a gestacao do
	/// bio-androide, o dano espalhado do G3, o verb de admin) -- e o `PrepararCombate` era o nono,
	/// pra quem loga ja morto.
	///
	/// Quando a morte ganhou uma SEGUNDA consequencia (a viagem pro Outro Mundo e a aureola),
	/// acrescentar a linha nova em sete dos oito lugares e literalmente o defeito que este port
	/// registra como o mais repetido dele. Entao a consequencia virou gancho: `Morrer()` ja era a
	/// porta unica da morte, e agora ela AVISA.
	///
	/// ============================ O GANCHO SO PODE ENFILEIRAR ============================
	/// `Morrer()` e chamado de dentro do `MeleeResolver`, do laco de dano em area e do tique de
	/// combate -- todos percorrendo a lista de uma zona. Um gancho que chamasse `MoveToZone` daqui
	/// mexeria nas listas de DUAS zonas no meio dessa varredura, que e o "Collection was modified"
	/// que o `_npcsPraTirar` e o `TickDeQuemVolta` ja existem pra evitar. Ver
	/// `GameServer.Alem.AMorteAconteceu`: ele escreve um relogio e mais nada.
	/// ==================================================================================
	/// </summary>
	public Action<CombatState>? AoMorrer;

	/// <summary>
	/// MORRER. Devolve **false** quando a morte foi NEGADA -- e ai nada mudou no corpo além do que
	/// quem negou tenha feito.
	///
	/// Todo chamador precisa olhar o retorno antes de fazer o que vem depois (pagar Zenkai,
	/// anunciar). Anunciar a morte de quem nao morreu e pior que nao ter o seguro: o jogador ve
	/// "voce morreu" e continua de pe.
	///
	/// O PRAZO DE RENASCER NAO E MAIS CONTA DO CHAMADOR -- ver <see cref="AoMorrer"/>.
	/// </summary>
	/// <param name="ignorarSeguro">
	/// Pula o <see cref="NegarMorte"/>. E pro verb de admin: uma ferramenta que uma mecanica de
	/// jogador consegue bloquear deixa de ser ferramenta.
	/// </param>
	public bool Morrer(bool ignorarSeguro = false)
	{
		if (F.dead)
		{
			// O MORTO NAO MORRE DE NOVO -- mas o `Death()` do DM tem um ramo pra ele (`:113-118`): o
			// fantasma que leva um golpe letal no Outro Mundo e curado, devolvido a mesa do Enma e
			// nocauteado por 2 s. O cadaver dos 2 s nunca chega aqui (ninguem o acerta: `EhCadaver`).
			if (MortoDePe?.Invoke(this) == true) AoMorrerDeNovo?.Invoke(this);
			return false;
		}
		if (!ignorarSeguro && NegarMorte?.Invoke(this) == true) return false;

		F.dead = true;
		F.KO = false;
		Desfeito = false;   // (2026-10-15) quem morre de vez nao fica em pedacos
		DesmaioMinimo = 0;
		// O TREINO E A MEDITACAO DESLIGAM: o `Death()` do DM comeca por `KO(-1)`, e `KO()` faz
		// `train = 0; med = 0` (`KO.dm:57-58`). Sem isto quem morria com a tecla T ligada seguia
		// ganhando BP no Outro Mundo -- e o cliente nunca manda "parado", porque o morto nem le acoes.
		F.train = false;
		F.med = false;
		Bloqueando = false;
		NocauteRestante = 0;
		EmCombate = 0;
		LutandoDeVerdade = 0;

		// DEPOIS DE TUDO ESCRITO, e nao antes: quem ouve este aviso le `F.dead` pra decidir, e um
		// gancho chamado no meio veria o corpo ainda vivo.
		AoMorrer?.Invoke(this);
		return true;
	}

	public void Reviver(double fracaoDeVida = 1, double carencia = 0)
	{
		F.dead = false;
		F.KO = false;
		Desfeito = false;
		DesmaioMinimo = 0;
		// `hakai_mark = 0` (`Death.dm:148`, no `ReviveMe`): a marca do Hakai morre junto com a volta a vida, por
		// QUALQUER porta -- o Enma, as esferas, a tecnica de reviver. Ver `Fighter.hakai_mark`.
		F.hakai_mark = false;
		Carencia = carencia;
		Corpo.Restaurar();
		if (fracaoDeVida < 1)
			foreach (BodyPart p in Corpo.Partes) p.Vida = p.VidaMax * fracaoDeVida;
		SincronizarVida();
	}

	/// <summary>
	/// CURAR NAO E REVIVER. O corpo inteiro (membros, decepado incluso), de pe se estava nocauteado, com
	/// a carencia -- e o `dead` fica exatamente como esta.
	///
	/// E o `World_Heal` do DM (`Admin.dm:409-413`): `Un_KO()` + `SpreadHeal(100,1,1)` + `Ki = MaxKi`, e
	/// nenhuma linha escreve `dead = 0`. O Heal de admin do port passava pelo <see cref="Reviver"/> e por
	/// isso ressuscitava -- o dono (2026-09-05): *"heal all ou heal em alguem especifico faz ela reviver,
	/// o que nao deveria acontecer"*. Quem revive e o <see cref="Reviver"/>, pelo verb de reviver.
	/// </summary>
	public void Curar(double carencia = 0)
	{
		F.KO = false;
		Desfeito = false;
		DesmaioMinimo = 0;
		Carencia = carencia;
		Corpo.Restaurar();
		SincronizarVida();
	}
}
