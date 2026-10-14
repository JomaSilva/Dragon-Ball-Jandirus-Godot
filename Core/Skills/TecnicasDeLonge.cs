using Jandirus.Core.Ai;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// QUAIS TECNICAS SAO ATAQUE A DISTANCIA -- e, pra cada uma, as cinco perguntas que a IA precisa
/// fazer antes de decidir atirar.
///
/// ============================ ELA FICOU VAZIA POR TRES CAMADAS, E AGORA TEM AS TRES QUE VOAM ============================
/// O cabecalho antigo dizia, com razao: *"a tabela nao pode ser preenchida por enquanto, so pra
/// testar -- uma linha aqui liga a IA a um efeito que nao existe, ela pararia de socar pra conjurar
/// um nada"*. A condicao pra preenche-la era **existir um ataque que viaja**, e ela foi cumprida:
/// a camada 1 pos no jogo a primeira entidade que sai de um corpo, anda entre tiques e acerta outro
/// (`Core.Combat.Projetil`), com tres verbs de producao -- um por tipo.
///
/// Foram esses tres que entraram primeiro; na v0.2.1 entrou TODO verb que poe tiro no mundo (trinta e
/// duas linhas -- ver o bloco no meio da tabela). As tecnicas que NAO viajam continuam FORA daqui:
/// elas resolvem por raio, no funil do soco, e o `Light_Buster` na mao da IA e um golpe de aproximacao,
/// nao um tiro. A distincao que este arquivo sempre defendeu -- *a tabela e de ATAQUE A DISTANCIA* --
/// continua sendo o criterio; o que mudou e que as outras deixaram de estar fora da IA: moram na
/// `TecnicasDeLuta`, com outra receita no cerebro.
///
/// O QUE ISSO LIGA, sem uma linha de codigo novo na IA: `GameServer.ArsenalDeLonge` para de devolver
/// vazio, `Cerebro.EscolherTiro` passa a ter o que escolher, `Plano.Atirar` deixa de ser inalcancavel
/// e a linha de visao volta a ser tracada (a 1 Hz, e so pra quem tem arsenal). O gesto -- recuar,
/// plantar o pe, conjurar, soltar -- ja estava escrito e nunca tinha rodado.
/// =====================================================================================================================
///
/// ============================ O RAIO E UM CANAL, E O GANCHO NAO PRECISOU MUDAR ============================
/// O `Cerebro.Disparo` solta UM PULSO e o `Ki_Wave` e um estado sustentado -- a camada 1 anotou isso
/// como pergunta em aberto ("ou o beam da IA vira tiro unico, ou o `Comando` ganha um estado de
/// canal"). O DM ja tinha respondido: `npc_beam_loop` (`BeamClash.dm:424`) canaliza e o laco morre
/// por TEMPO (`BCL_NPC_BEAM_TIME`, 12 s), e o prazo NAO corre durante uma disputa de feixes.
///
/// Entao o pulso ABRE o canal e um relogio o FECHA (`GameServer.TickDoPrazoDeRaioDaIa`). Nada mudou
/// no `Comando`, nada mudou no `Cerebro`: o gancho estava certo.
/// ======================================================================================================
///
/// ============================ POR QUE O CUSTO E UMA FUNCAO E NAO UM NUMERO ============================
/// A tentacao obvia e escrever `custoKi: 250` na linha. Isso cria a SEGUNDA COPIA DO PRECO: a
/// tecnica cobra o dela la no efeito (`Tecnicas.SolarCustoKi(f)`, `GameServer.Tecnicas.cs:58`) e a
/// IA acredita neste; no dia em que alguem reequilibrar um dos dois, a IA passa a decidir com um
/// preco que o jogo nao pratica -- e ninguem liga o defeito a esta linha.
///
/// Entao a linha aponta pra a MESMA funcao que o efeito chama. Quem registrar uma tecnica de longe
/// e nao tiver essa funcao esta dizendo, pela forma do dado, que o preco dela ainda mora dentro do
/// efeito e precisa sair de la primeiro.
///
/// E, mesmo assim, a IA trata o custo como PISO e nunca como permissao: quem recusa por falta de Ki
/// e o efeito. Uma divergencia entre os dois pode deixar a IA timida (ela nao atira quando podia);
/// nunca pode deixa-la trapacear (atirar sem pagar). Errar pro lado seguro e uma escolha.
/// ================================================================================================
/// </summary>
public static class TecnicasDeLonge
{
	/// <summary>
	/// UMA LINHA DA TABELA -- a descricao ESTATICA de uma tecnica de longe.
	///
	/// Nao confundir com <c>Jandirus.Core.Ai.Tiro</c>: aquele e o que sobra depois de resolver esta
	/// linha PARA UM CORPO (o custo vira numero, os tiles viram pixels). Esta aqui nao conhece
	/// ninguem, e por isso e dado compartilhado; aquele e resposta do jogo pra um corpo so, e por
	/// isso mora nas `Capacidades`.
	/// </summary>
	public readonly record struct Linha(
		/// <summary>O id do verb do DM -- o MESMO que viaja no `C2S.Habilidade` ("Kamehameha").</summary>
		string Id,
		/// <summary>Perto demais nao vale: quem esta colado leva soco, e nao raio. Em TILES.</summary>
		float AlcanceMinTiles,
		/// <summary>Alem disto o golpe nao chega. Em TILES.</summary>
		float AlcanceMaxTiles,
		/// <summary>Quanto tempo de pe, parado, ate o golpe sair. Segundos.</summary>
		double TempoDeConjuracao,
		/// <summary>O preco, pela MESMA funcao que o efeito cobra. Ver o cabecalho.</summary>
		Func<Fighter, double> CustoDeKi,
		/// <summary>Parede no caminho impede? Um raio sim; uma bomba em arco, nao.</summary>
		bool PrecisaDeLinhaLivre,
		/// <summary>0..1 -- o quanto ela perdoa alvo longe e alvo em movimento. 1 = nunca erra.</summary>
		double Precisao,
		/// <summary>A SEGUNDA MOEDA: o preco em FOLEGO, pela mesma disciplina do de Ki. Nulo = nao cobra folego.</summary>
		Func<Fighter, double>? CustoDeFolego = null,
		/// <summary>
		/// SEGUNDOS ATE O SEGUNDO APERTO. As tecnicas que se SEGURAM (a barragem ligada, a bola que carrega sobre a
		/// cabeca) terminam com outro aperto do mesmo verb, e o cerebro so sabe dar um pulso -- quem da o segundo e
		/// o relogio do servidor (`GameServer.TickDoSegundoApertoDaIa`), o mesmo desenho do prazo do raio. Zero = o
		/// verb acaba sozinho (ou e um raio: o canal tem o relogio dele).
		/// </summary>
		double SegundoAperto = 0,
		/// <summary>O que ela pede da cena -- ver <see cref="Ai.Exige"/>.</summary>
		Exige Exige = Exige.Nada,
		/// <summary>Segundos sem repetir -- ver <see cref="Tiro.Descanso"/>. Zero = o padrao do cerebro.</summary>
		double Descanso = 0);

	/// <summary>
	/// A TABELA. Comeca pelas tres primeiras que VIAJARAM, uma por tipo de projetil -- e os numeros delas contam
	/// como cada coluna se decide; as outras linhas seguem a mesma regra.
	///
	/// ============================ CADA NUMERO SAI DO VERB, E NENHUM E OPINIAO ============================
	/// * ALCANCE MAXIMO: os tres verbs nascem com `maxdistance = 30` tiles, e e esse o teto fisico do
	///   tiro. A IA usa MENOS que isso de proposito -- 18, 14 e 16 --, e o motivo nao e timidez: o
	///   `RiscoDeErrar` cresce ate 1 na ponta da janela, entao registrar 30 faria o cerebro rejeitar
	///   quase todo tiro por risco em vez de nunca tentar. A janela e onde ele ACERTA, nao onde o
	///   tiro chega.
	/// * ALCANCE MINIMO: 3 tiles pras bolas e 4 pro raio. Colado se soca -- e o raio pede um pouco
	///   mais porque ele PLANTA o corpo (`canmove = 0`), e plantar-se a dois tiles de alguem e um
	///   presente.
	/// * CUSTO: a MESMA expressao que o verb cobra, nunca uma copia. `10*BaseDrain` pros dois
	///   primeiros (`beams.dm:281`, `blasts.dm:44`), `600*BaseDrain` pro teleguiado
	///   (`GuidedBall.dm:30` -- o valor COBRADO, ver o defeito consertado na camada 1).
	/// * CONJURACAO: o tempo que o corpo fica de pe antes de o golpe sair. A bola sai na hora (0) e o
	///   raio pede a carga do DM. O `Guided_Ball` pede meio segundo por ser um gesto grande, e e o
	///   unico numero aqui que e escolha -- esta anotado como tal.
	/// * LINHA LIVRE: os tres precisam. Sao tiros retos que morrem no cenario (`BlockedAt`), e um
	///   NPC que atira num muro e o defeito que qualquer jogador nota em dez segundos.
	/// * PRECISAO: o teleguiado e o que mais acerta quem esta marcado -- ele faz curva atras do alvo
	///   (0,95). O numero e de quando a curva nao tinha limite (`walk_towards`); desde 2026-10-09 ela
	///   tem (`Core.Combat.Teleguiado`: de leve, e so ate 45 graus do rumo da saida), e quem corre de lado
	///   escapa -- o peso fica, porque contra quem anda ele continua sendo o tiro que nao se desvia. O raio
	///   e rapido e comprido (0,78); a bola e um PONTO, e nao uma linha -- quem sai de lado a tempo a ve
	///   passar (0,70). Os tres agora saem NO marcado em qualquer angulo (`MiraDeKi`), e nao so em fileira.
	///   (Ela era 0,55 enquanto foi o tiro lento do jogo -- 107 px/s, mais devagar que alguem ANDANDO.
	///   Desde 2026-10-08 ela voa a 512 px/s, mais que uma corrida: ver `Projetil.AtrasoDeBola`. O que
	///   ainda a separa do raio e so a forma.)
	/// ================================================================================================
	/// </summary>
	private static readonly Dictionary<string, Linha> Tudo = new(StringComparer.OrdinalIgnoreCase)
	{
		["Ki_Wave"] = new("Ki_Wave", 4f, 18f, 0.7, CustoDeRaio, true, 0.78),
		["Basic_Blast"] = new("Basic_Blast", 3f, 14f, 0, CustoDeBola, true, 0.70),
		["Guided_Ball"] = new("Guided_Ball", 3f, 16f, 0.5, CustoDeTeleguiado, true, 0.95),

		// ============================ A QUARTA, E ELA E A PRIMEIRA QUE NAO PRECISA DE LINHA LIVRE ============================
		// A BALA DISPERSA (`blasts.dm:530`, lote G7) nasce ESPALHADA em volta de quem atira e converge
		// no alvo marcado de todos os angulos (`walk_towards`, `:601`). E a unica tecnica do jogo em
		// que a parede entre os dois nao decide nada: as bolas contornam porque nunca estiveram na
		// linha reta. Registrar `PrecisaDeLinhaLivre = true` aqui seria copiar o dos outros tres e
		// apagar a unica coisa que distingue esta -- a IA deixaria de usa-la exatamente na situacao
		// pra qual ela serve.
		//
		// Alcance 4..20: o piso e maior que o das bolas porque a nuvem nasce em VOLTA de quem atira
		// (a dois tiles, metade dela nasce em cima do alvo e ele nem precisa desviar); o teto e o do
		// verb, que exige alvo a ate 30 tiles, encurtado pela mesma razao dos outros tres -- a janela
		// e onde ele ACERTA, nao onde o tiro chega. Conjuracao ZERO: o corpo nao e plantado, a espera
		// de 2 s acontece com as bolas ja no ar. Precisao 0,9: elas perseguem como o teleguiado, mas
		// sao muitas e cada uma sai de um lugar, entao a nuvem perdoa mais do que uma bola so.
		// =====================================================================================================================
		["Scattering_Bullet"] = new("Scattering_Bullet", 4f, 20f, 0, CustoDeBalaDispersa, false, 0.90),

		// ============================ E AS OUTRAS VINTE E OITO (v0.2.1, dono 2026-10-13) ============================
		// *"IAs q controlam o personagem (oozaru, formas de lssj descontroladas etc) deveriam usar todas as skills
		// disponiveis do jogador descontrolado, se a IA n sabe usar, melhore ela"*. Ate aqui a tabela tinha quatro
		// linhas e o jogo trinta e dois verbos que atiram: o corpo possuido de um jogador com o livro cheio so
		// soltava Onda de Ki. Cada linha abaixo segue a regra do cabecalho -- a JANELA e julgamento (onde o tiro
		// ACERTA, nunca o `maxdistance` do verb), o PRECO espelha a conta do verb, e a `--catalogoteste` aperta cada
		// uma e confere que a tabela nunca promete mais barato do que o jogo cobra.
		//
		// O QUE ESPELHA E O PRECO, NAO UMA COPIA DELE: os verbs destes lotes escrevem o custo dentro do efeito
		// (`GameServer.Tecnicas.G5/G6/G7/G12/G14`), e tira-lo de la seria reescrever trinta tecnicas em producao por
		// causa da IA. A conta fica dos dois lados e quem a mantem igual e a regua da bancada.
		// ======================================================================================================

		// ---- os quatro raios do lote G5 (`beams.dm`). Sao CANAIS, como o Ki Wave: o pulso abre, o prazo fecha. ----
		// O preco e o ALUGUEL por tiro (`lastbeamcost`), e nao a entrada: o Raio Colossal entra por 15 e custa 150
		// por tique -- quem decide pela entrada abre um raio que nao segura dois segundos.
		["Masenko"] = new("Masenko", 4f, 16f, 0.7, f => 30 * f.BaseDrain(), true, 0.74),
		["Makkankosappo"] = new("Makkankosappo", 6f, 26f, 0.7, f => 30 * f.BaseDrain(), true, 0.80),
		["Massive_Beam"] = new("Massive_Beam", 4f, 20f, 0.7, f => 150 * f.BaseDrain(), true, 0.76),
		["Final_Flash"] = new("Final_Flash", 6f, 30f, 1.0,
			f => 25 * f.BaseDrain() * DegrauDeCusto(f.beamskill, 1, 10, 20, 30), true, 0.80),

		// ---- os seis raios nomeados do lote G6. A escada de `beamskill` multiplica o aluguel (ate 30x). ----
		// O Death Beam chega em um sexto de segundo e so alcanca dez tiles; o Boom Wave e o raio de quem luta colado
		// (cinco tiles), e o unico cujo aluguel CAI com o treino -- o preco dele aqui e a entrada.
		["Kamehameha"] = new("Kamehameha", 4f, 24f, 0.8,
			f => 20 * f.BaseDrain() * DegrauDeCusto(f.beamskill, 1, 10, 20, 30), true, 0.80),
		["GalicGun"] = new("GalicGun", 4f, 24f, 0.8,
			f => 21 * f.BaseDrain() * DegrauDeCusto(f.beamskill, 1.2, 12, 22, 30), true, 0.80),
		["Enkumei"] = new("Enkumei", 4f, 24f, 0.8,
			f => 22 * f.BaseDrain() * DegrauDeCusto(f.beamskill, 1, 10, 20, 30), true, 0.78),
		["Death_Beam"] = new("Death_Beam", 3f, 9f, 0.3, f => 21 * 25 * f.BaseDrain(), true, 0.92),
		["Dodompa"] = new("Dodompa", 4f, 20f, 0.6, f => 21 * 29 * f.BaseDrain(), true, 0.82),
		["Boom_Wave"] = new("Boom_Wave", 1.5f, 4.5f, 0.2, f => 15 * f.BaseDrain(), true, 0.85),

		// ---- as bolas do lote G5 (`blasts.dm` e vizinhos). Saem na hora; a recarga e do verb. ----
		["Charged_Shot"] = new("Charged_Shot", 3f, 14f, 0.3, f => 50 * f.BaseDrain(), true, 0.72),
		["KillDriver"] = new("KillDriver", 3f, 14f, 0, f => 50 * f.BaseDrain(), true, 0.72),
		["BusterShell"] = new("BusterShell", 2f, 12f, 0, f => 4 + f.BaseDrain(), true, 0.82),
		["Scattershot"] = new("Scattershot", 2f, 9f, 0, f => BolasDaBarragem(f, 8, f.volleyskill) * 40 * f.BaseDrain(), true, 0.78),
		["Energy_Barrage"] = new("Energy_Barrage", 3f, 14f, 0, f => BolasDaBarragem(f, 10, f.volleyskill) * 15 * f.BaseDrain(), true, 0.80),
		// OS DOIS CERCOS nascem EM VOLTA do alvo marcado (a ate vinte tiles), e por isso a parede no meio nao decide
		// nada -- como a Bala Dispersa. O Campo Minado FICA onde nasceu: so fere quem anda, e a precisao dele e a
		// mais baixa da tabela; a Hellzone converge, e e das mais altas.
		["Ki_Bomb"] = new("Ki_Bomb", 2f, 18f, 0, f => BolasDaBarragem(f, 7, f.targetedskill) * 15 * f.BaseDrain(), false, 0.55),
		["Hellzone_Grenade"] = new("Hellzone_Grenade", 2f, 18f, 0, f => BolasDaBarragem(f, 7, f.targetedskill) * 50 * f.BaseDrain(), false, 0.92),
		["Kienzan"] = new("Kienzan", 3f, 16f, 0.4, f => 100 * f.BaseDrain(), true, 0.86),
		// AS DUAS QUE PRENDEM: o dano e enfeite (`basedamage = 0.1`) e a recarga do verb e longa -- o descanso
		// daqui so impede o cerebro de bater na porta a cada rodada.
		["Paralysis"] = new("Paralysis", 3f, 14f, 0.3, f => 700 * f.BaseDrain(), true, 0.72, Descanso: 15),
		["Stunlock"] = new("Stunlock", 3f, 14f, 0.3, f => 300 * f.BaseDrain(), true, 0.72, Descanso: 12),

		// ---- o Kikoho (lote G6): cobra SANGUE de quem atira (`SpreadDamage(2*n)`), e a silaba anda KI->KO->HO, ----
		// cada uma mais cara -- o preco daqui e o da terceira, e ele so sai com a vida acima da metade.
		["Kikoho"] = new("Kikoho", 3f, 12f, 0.5, f => 150 * f.BaseDrain(), true, 0.85, Exige: Exige.VidaSobrando),

		// ---- O SPIRIT GUN, que nao cabia: e o unico tiro do jogo pago em FOLEGO (`usr.stamina -= kireq`, ----
		// `Spirit.dm:352`). O campo que faltava e o `CustoDeFolego`, e a porta do cerebro confere as duas moedas.
		["Spirit_Gun"] = new("Spirit_Gun", 3f, 14f, 0.3, _ => 0, true, 0.76,
			CustoDeFolego: f => f.angerBuff * f.Ephysoff * f.SpiritBallCost * 10),

		// ---- o Cuspe Acido (lote G14): a porta do verb e um decimo do tanque, e ele recusa o corpo ferido. ----
		["Acid_Spit"] = new("Acid_Spit", 3f, 14f, 0, f => Math.Max(12 * f.BaseDrain(), f.MaxKi * 0.1), true, 0.70,
			Exige: Exige.VidaSobrando),

		// ============================ AS CINCO QUE SE SEGURAM (lote G12) ============================
		// Nenhuma acaba num aperto so, e o corpo fica PLANTADO enquanto dura (`PresoPeloG12`). O segundo aperto e
		// do relogio do servidor (`SegundoAperto`, em segundos), e o descanso e longo: sao gestos de uma vez por
		// luta, nao de rotacao.
		//   * DEATH BALL: carrega sozinha em quatro estagios de 1,5 s e depois obedece ao olhar -- o aperto dos dez
		//     segundos larga a guia (ou, se ainda carregava, solta com o estagio que tem).
		//   * GENKIDAMA: cobra 90% do tanque, forma em 3 s e cresce; o aperto dos seis segundos a dispara.
		//   * BUSTER BARRAGE e as DUAS RAJADAS: ligadas, cospem ate o Ki acabar; o segundo aperto e o que as para.
		//     O preco e o de varios ciclos (o verb cobra um por vez): quem liga com o tanque no fim cospe duas bolas.
		//   * A barragem e a rajada giratoria atiram EM VOLTA: nao pedem linha nem distancia minima.
		// =========================================================================================
		["Death_Ball"] = new("Death_Ball", 5f, 16f, 0.5, f => 150 * f.BaseDrain(), true, 0.65, SegundoAperto: 10, Descanso: 30),
		["SpiritBomb"] = new("SpiritBomb", 8f, 24f, 1.0, f => f.MaxKi * 0.9, true, 0.60, SegundoAperto: 6, Descanso: 60),
		["BusterBarrage"] = new("BusterBarrage", 0f, 8f, 0, f => 20 * f.BaseDrain(), false, 0.50, SegundoAperto: 5, Descanso: 20),
		["Continuous_Energy_Bullets"] = new("Continuous_Energy_Bullets", 3f, 14f, 0.2, f => 300 * f.BaseDrain(), true, 0.76, SegundoAperto: 4, Descanso: 15),
		["Spin_Blast"] = new("Spin_Blast", 0f, 6f, 0.2, f => 500 * f.BaseDrain(), false, 0.50, SegundoAperto: 4, Descanso: 20),
	};

	/// <summary>
	/// O MULTIPLICADOR DE ALUGUEL POR `beamskill` -- os quatro degraus dos raios que tem escada (`< 30`, `< 70`,
	/// `< 100`, e dali pra cima). Os numeros de cada raio estao na linha dele; a escada do verb mora no
	/// `GameServer.Tecnicas.G5.FinalFlashG5` e no `DegrauDoRaioG6`.
	/// </summary>
	private static double DegrauDeCusto(double beamskill, double a, double b, double c, double d) =>
		beamskill < 30 ? a : beamskill < 70 ? b : beamskill < 100 ? c : d;

	/// <summary>
	/// QUANTAS BOLAS SAEM de uma barragem ou de um cerco: `base + bonusShots + round(ln(blastskill)) +
	/// round(ln(segunda pericia))`, com o piso de 1 dentro dos dois logaritmos (`blasts.dm:137`, `:202` e os dois
	/// cercos). A segunda pericia e a de volei nas barragens e a de mira nos cercos.
	/// </summary>
	private static int BolasDaBarragem(Fighter f, double baseQtd, double segundaPericia) =>
		(int)Math.Max(baseQtd + f.bonusShots
					  + DmMath.Round(Math.Log(Math.Max(f.blastskill, 1)), 1)
					  + DmMath.Round(Math.Log(Math.Max(segundaPericia, 1)), 1), 1);

	/// <summary>`10*BaseDrain` -- o mesmo que o `Ki_Wave` cobra (`beams.dm:281`).</summary>
	private static double CustoDeRaio(Fighter f) => 10 * f.BaseDrain();

	/// <summary>`10*BaseDrain` -- o mesmo que o `Basic_Blast` cobra (`blasts.dm:44`).</summary>
	private static double CustoDeBola(Fighter f) => 10 * f.BaseDrain();

	/// <summary>
	/// `600*BaseDrain` -- o que o `Guided_Ball` COBRA. O DM confere 50 e cobra 600 no mesmo verb; a
	/// camada 1 consertou isso pro valor cobrado, e a IA tem que decidir com o preco de verdade,
	/// senao ela escolheria o golpe caro achando que ele e barato e ouviria "nao" do proprio jogo.
	/// </summary>
	private static double CustoDeTeleguiado(Fighter f) => 600 * f.BaseDrain();

	/// <summary>
	/// `60*BaseDrain` -- o que a Bala Dispersa COBRA (`blasts.dm:534`).
	///
	/// O verb do DM escreve `kireq = 60*BaseDrain` e cobra `kireq*BaseDrain` (BaseDrain ao QUADRADO);
	/// o lote G7 cobra o valor conferido, e a IA decide com o preco de verdade -- senao ela julgaria
	/// o tiro caro e nunca o escolheria.
	/// </summary>
	private static double CustoDeBalaDispersa(Fighter f) => 60 * f.BaseDrain();

	// ============================ O ARSENAL DA IA NAO CRESCE SOZINHO ============================
	// Esta tabela e escrita a mao, e de proposito -- cada linha declara `AlcanceMin`, `AlcanceMax` e
	// `Precisao`, que sao julgamentos sobre a JANELA em que a IA acerta, e nenhum dos tres esta escrito
	// no DM (o verb so diz `maxdistance`). Deriva-los automaticamente do `maxdistance` produziria o
	// defeito que o cabecalho da tabela ja descreve: registrar 30 faz o `RiscoDeErrar` chegar a 1 na
	// ponta e o cerebro rejeitar quase todo tiro.
	//
	// Quem cobra que ela nao fique pra tras e a `--catalogoteste`: ela aperta TODO verb vivo, anota os
	// que puseram tiro no mundo e reprova se algum nao tiver linha aqui (a lista de divida que ela
	// aceitava ate a v0.2.0 ficou vazia na v0.2.1, e saiu).
	// ===========================================================================================


	/// <summary>Quantas tecnicas de longe o jogo conhece.</summary>
	public static int Quantas => Tudo.Count;

	/// <summary>
	/// AS LINHAS, pra quem quer perguntar "quais destas eu sei?" em vez de "quais verbs eu tenho?".
	///
	/// A diferenca e de CUSTO e ela apareceu numa medicao: montar a lista de TODOS os verbs de um
	/// corpo (o `TecnicasDe`) pra depois filtrar tres aloca uma lista proporcional ao livro inteiro --
	/// num personagem com o catalogo todo aprendido sao centenas de strings, uma vez por segundo, por
	/// corpo. Perguntando pelas tres, a varredura continua sendo a mesma mas nao sobra lixo.
	/// </summary>
	/// <remarks>
	/// O TIPO CONCRETO E DE PROPOSITO. Como `IEnumerable&lt;Linha&gt;`, o `foreach` do `ArsenalDeLonge` (a leitura
	/// de 1 Hz de cada corpo da IA) encaixotava o enumerador -- 80 B por leitura, sem nada em troca. O
	/// `ValueCollection` tem enumerador `struct`, continua somente-leitura, e todo consumidor LINQ (`Select`,
	/// `Min`, `Count`) compila igual.
	/// </remarks>
	public static Dictionary<string, Linha>.ValueCollection Todas => Tudo.Values;

	/// <summary>
	/// EXISTE ALGUM ATAQUE DE LONGE NESTE JOGO? A poda mais barata do sistema: enquanto for falso,
	/// nenhum livro de skill e varrido e nenhuma linha de visao e tracada.
	/// </summary>
	public static bool Alguma => Tudo.Count > 0;

	/// <summary>
	/// Esta tecnica e de longe? Nulo = nao -- e e a resposta pra toda tecnica que nao poe tiro no mundo.
	///
	/// O SOLAR FLARE NAO ENTRA NA TABELA, e vale dizer por que: ele TEM alcance
	/// (`Tecnicas.SolarAlcanceTiles`) mas nao e um ataque -- cega quem esta OLHANDO, nao causa dano
	/// e nao viaja. Registra-lo faria a IA "atirar" flashes no meio do soco, comportamento que
	/// ninguem pediu. A tabela e de ATAQUE A DISTANCIA, e a distincao e essa.
	/// </summary>
	public static Linha? Get(string id) =>
		id.Length > 0 && Tudo.TryGetValue(id, out Linha l) ? l : null;
}
