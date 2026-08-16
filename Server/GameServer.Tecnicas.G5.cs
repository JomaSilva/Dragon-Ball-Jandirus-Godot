using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G5 -- O ARSENAL DE KI NOMEADO. Catorze folhas que estavam MUDAS.
///
/// ============================ POR QUE ESTAS CATORZE, E NAO OUTRAS ============================
/// O censo das folhas ligadas (165 delas) achou 60 verbos que uma skill concede e o servidor nao
/// atende: o jogador compra, le "voce pode usar X" no chat, e X nao existe. Elas nao sao todas do
/// mesmo tamanho -- 31 esperam um SISTEMA que este port nao tem (absorcao de corpo, agarrao, parar
/// o tempo, destruicao de planeta), e essas ficam catalogadas.
///
/// Estas catorze sao as que NAO esperam nada: o encanamento de que precisam ja esta de pe desde a
/// camada dos projeteis. Todas entram por UMA das duas portas que ja existiam --
/// <see cref="GameServer.Disparar"/> (bola) ou <see cref="GameServer.Canalizar"/> (raio) -- e
/// nenhuma delas inventa cadeia de dano, entidade nova ou opcode. O que cada uma traz do DM e uma
/// RECEITA: custo, alcance, potencia, recarga.
///
/// SEIS DELAS SAO `datum/skill/rank/*` (Final Flash, Makankosappo, Buster Shell, Kill Driver e as
/// duas de paralisia). Ou seja: metade deste lote e a arvore de CARGO, que era a outra metade do
/// pedido -- fechar as folhas nomeadas e fechar o que os cargos concedem.
/// ============================================================================================
///
/// ============================ O QUE ESTE LOTE NAO CONSERTOU (e e divida) ============================
/// O `mods` do DM e o `basedamage` do DM sao DUAS grandezas (`objects.dm:315`:
/// `DamageCalc(mods*6*globalKiDamage, ..., basedamage)`), e o port funde as duas: o `Disparar`
/// escreve `ModsBase = ModsDoTiro(tipo) * BaseDano` e depois a cadeia usa `BaseDano` OUTRA VEZ como
/// `basedamage`. Alem disso o `ModsDoTiro` de bola devolve `Ekioff*Ekiskill`
/// (`customattacks.dm:509`, a formula da tecnica CUSTOMIZADA), enquanto todos os verbs de bola
/// nomeados do DM usam `Ekioff*Ekiskill*log_10(max(kieffusionskill,2))*log_10(max(blastskill,2))`.
///
/// NAO MEXI NISSO AQUI, de proposito: desfazer a fusao rebalanceia as tres tecnicas ja em producao
/// (Ki Wave, Basic Blast, Guided Ball) e o dano de ki cresce com `Ekioff` AO QUADRADO no port contra
/// linear no original -- e uma passada de balanceamento com bancada propria, nao uma folha. O que
/// este lote garante e que as catorze usem a MESMA convencao das tres que ja rodavam, para que a
/// ordem relativa que o DM escreveu (Massive Beam 4 contra Masenko 1,5; Hellzone 4x contra Barrage
/// 0,8x) chegue intacta. Fica anotado como divida, com o numero: `Ekioff` linear virou quadratico.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// AS QUATRO RECARGAS, E ELAS SAO AS DO DM
	// =====================================================================
	/// <summary>
	/// `barrageCD` -- a recarga COMPARTILHADA das barragens (Scattershot e Energy Barrage).
	///
	/// Uma so pras duas porque o DM as poe no mesmo contador: quem acabou de despejar dez bolas nao
	/// pode emendar oito por cima trocando de verb. Separar em duas recargas dobraria o volume de
	/// fogo de graca, e o contador compartilhado E o balanceamento da familia.
	/// </summary>
	private readonly Dictionary<int, long> _volleyPronto = [];

	/// <summary>`targetedCD` -- Ki Minefield e Hellzone Grenade. Mesma logica do de cima.</summary>
	private readonly Dictionary<int, long> _alvoPronto = [];

	/// <summary>`debuffCD` -- Paralysis e Stunlock.</summary>
	private readonly Dictionary<int, long> _debuffPronto = [];

	/// <summary>
	/// `tick` do BYOND: um decimo de segundo. Toda recarga do DM esta escrita em tiques
	/// (`basicCD += 20`, `reload = Eactspeed*5`), e converter na hora de usar espalharia a
	/// constante por vinte lugares.
	/// </summary>
	private const double MsPorTique = 100;

	/// <summary>
	/// O ALCANCE PADRAO de uma bola solta, em tiles.
	///
	/// Nenhum verb de bola do DM escreve `maxdistance` -- eles confiam no `Burnout()` de 5 s pra
	/// apagar o tiro. O port precisa dos dois (o alcance tambem alimenta o `rangemod` e a forca do
	/// empurrao de perto), entao herda os 30 que o `Basic_Blast` ja usava. Com o passo mais lento
	/// (0,3 s/tile) o prazo vence primeiro, que e exatamente o que acontece la.
	/// </summary>
	private const double AlcanceDeBolaG5 = 30;

	// =====================================================================
	// REGISTRO
	// =====================================================================
	public static void RegistrarTecnicasG5()
	{
		// ---- os quatro raios (beams.dm) ----
		Tecnicas.Registrar("Masenko", "Masenko", Modo.Sustentada,
			"Um raio que sai FORTE e vai MORRENDO no caminho: cada tile viajado tira um pouco da "
			+ "potencia. E a arma de quem luta colado -- de longe ela chega fraca.");

		Tecnicas.Registrar("Makkankosappo", "Makankosappo", Modo.Sustentada,
			"O avesso do Masenko: um raio fino que GANHA forca a cada tile e alcanca o dobro da "
			+ "distancia. Em compensacao demora seis vezes mais pra carregar -- e essa espera, na "
			+ "cara do inimigo, e o preco dele.");

		Tecnicas.Registrar("Massive_Beam", "Raio Colossal", Modo.Sustentada,
			"Um raio LARGO, com o dobro do poder do seu proprio corpo por tras e quatro vezes a "
			+ "potencia de um raio comum. Custa quinze vezes mais Ki por segundo que o raio comum e "
			+ "viaja devagar: quem carrega isso esta apostando tudo num tiro.");

		Tecnicas.Registrar("Final_Flash", "Final Flash", Modo.Sustentada,
			"Uma muralha de energia com QUATRO vezes o seu poder por tras dela, capaz de passar por "
			+ "cima de gente mais forte que voce. Quanto melhor a sua pericia de raio, mais longe e "
			+ "mais forte ela sai -- e mais absurdamente cara fica.");

		// ---- as bolas nomeadas (blasts.dm, discs.dm, Debuffs.dm, meta.dm) ----
		Tecnicas.Registrar("Charged_Shot", "Tiro Carregado", Modo.Instantanea,
			"A Bola de Ki carregada: o dobro da potencia e um poder vinte por cento maior que o seu "
			+ "por tras dela. Em troca custa cinco vezes mais e trava seu proximo tiro por dois "
			+ "segundos.");

		Tecnicas.Registrar("KillDriver", "Kill Driver", Modo.Instantanea,
			"Um disco de energia rapido que NAO DA PRA DEFLETIR e PARALISA quem acerta. O dano e "
			+ "quase nada -- o que ele faz e tirar as pernas do inimigo por ate dez segundos.");

		Tecnicas.Registrar("BusterShell", "Buster Shell", Modo.Instantanea,
			"Quatro esferas soltas de uma vez, abrindo em leque. Nenhuma delas machuca muito "
			+ "sozinha; juntas cobrem a frente inteira e e dificil sair de todas.");

		Tecnicas.Registrar("Scattershot", "Tiro Disperso", Modo.Instantanea,
			"Uma nuvem de bolas que nascem ESPALHADAS em volta de voce e depois convergem pra frente. "
			+ "Quantas saem depende da sua pericia de bola e de volei. Cara, e deixa toda a familia "
			+ "de barragem em espera.");

		Tecnicas.Registrar("Energy_Barrage", "Barragem de Energia", Modo.Instantanea,
			"Mais bolas que o Tiro Disperso e mais baratas, todas retas pra frente. E a barragem do "
			+ "dia a dia: menos dano por bola, muito mais volume.");

		Tecnicas.Registrar("Ki_Bomb", "Campo Minado de Ki", Modo.Instantanea,
			"Semeia bolas PARADAS em volta do alvo marcado e as deixa la por quatro segundos. Elas "
			+ "nao perseguem ninguem -- quem se machuca e quem tentar sair do cerco. Precisa de alvo "
			+ "a ate vinte tiles.");

		Tecnicas.Registrar("Hellzone_Grenade", "Hellzone Grenade", Modo.Instantanea,
			"O cerco que FECHA: as bolas nascem em volta do alvo, ficam paradas UM SEGUNDO e entao "
			+ "convergem todas nele ao mesmo tempo. O dobro do dano do campo minado e mais de tres "
			+ "vezes o preco.");

		Tecnicas.Registrar("Kienzan", "Kienzan", Modo.Instantanea,
			"Um disco de corte com dano fixo e ALTO, que persegue o alvo marcado por dois minutos "
			+ "inteiros. Nao se apaga sozinho como os outros tiros: ou acerta, ou bate em alguma "
			+ "coisa.");

		Tecnicas.Registrar("Paralysis", "Paralisia", Modo.Instantanea,
			"Um tiro que quase nao machuca e que NAO DA PRA DEFLETIR: ele tranca as pernas de quem "
			+ "acerta por cinco a dez segundos. O alvo continua batendo e se defendendo -- so nao "
			+ "consegue mais fugir. Custa uma fortuna em Ki.");

		Tecnicas.Registrar("Stunlock", "Stunlock", Modo.Instantanea,
			"A paralisia dos Metamorianos: mais barata que a Paralysis e com um poder um pouco menor "
			+ "por tras, com a mesma promessa -- nao da pra defletir, e as pernas param.");
	}

	// =====================================================================
	// DESPACHO
	// =====================================================================
	/// <summary>
	/// O DESPACHO DO LOTE. Nenhum destes ids aceita argumento, entao a checagem de "voce sabe esta
	/// tecnica?" NAO e repetida aqui: este lote e chamado de dentro do `switch` do
	/// <see cref="UsarTecnica"/>, ja depois do <see cref="SabeTecnica"/> geral -- o mesmo caminho
	/// dos tres verbos de projetil originais, e pela mesma razao (quem nao comprou ouve o "voce nao
	/// sabe" por uma porta so, em vez de catorze recusas repetidas).
	/// </summary>
	private void UsarTecnicasG5(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "Masenko": MasenkoG5(pl); break;
			case "Makkankosappo": MakankosappoG5(pl); break;
			case "Massive_Beam": RaioColossalG5(pl); break;
			case "Final_Flash": FinalFlashG5(pl); break;

			case "Charged_Shot": TiroCarregadoG5(pl); break;
			case "KillDriver": KillDriverG5(pl); break;
			case "BusterShell": BusterShellG5(pl); break;
			case "Scattershot": BarragemG5(pl, disperso: true); break;
			case "Energy_Barrage": BarragemG5(pl, disperso: false); break;
			case "Ki_Bomb": CercoG5(pl, converge: false); break;
			case "Hellzone_Grenade": CercoG5(pl, converge: true); break;
			case "Kienzan": KienzanG5(pl); break;
			case "Paralysis": ParalisiaG5(pl, forte: true); break;
			case "Stunlock": ParalisiaG5(pl, forte: false); break;
		}
	}

	/// <summary>Os catorze ids deste lote -- lidos pelo `switch` do despacho geral.</summary>
	private static readonly string[] IdsG5 =
	[
		"Masenko", "Makkankosappo", "Massive_Beam", "Final_Flash",
		"Charged_Shot", "KillDriver", "BusterShell", "Scattershot", "Energy_Barrage",
		"Ki_Bomb", "Hellzone_Grenade", "Kienzan", "Paralysis", "Stunlock",
	];

	public static bool EhDoLoteG5(string id) => Array.IndexOf(IdsG5, id) >= 0;

	// =====================================================================
	// OS QUATRO RAIOS -- `Code/Modules/Skills/Ki/beams.dm`
	// =====================================================================
	/// <summary>
	/// MASENKO (`beams.dm:303`). `kireq = lastbeamcost = 30*BaseDrain`, `beamspeed = 1`,
	/// `powmod = 1.5`, `maxdistance = 20`, `rangemod = 0.95`, `chargedelay = 2`.
	///
	/// O `rangemod` ABAIXO DE 1 e a tecnica inteira: o proprio DM escreve na descricao do verb
	/// *"Fire an energy wave that loses power as it travels"*. A cada tile o `mods` e multiplicado
	/// por 0,95, entao aos vinte tiles ele chega com ~36% do que saiu. Ver `Projetil.ModsAgora`.
	/// </summary>
	private void MasenkoG5(ServerPlayer pl)
	{
		if (SoltarCanal(pl, "Masenko")) return;

		double custo = 30 * pl.Ficha.BaseDrain();
		Canalizar(pl, "Masenko", custo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam,
			BaseDano = 1.5,          // `powmod`
			Velocidade = 1,          // `beamspeed`
			AlcanceTiles = 20,       // `maxdistance`
			RangeMod = 0.95,         // MORRE andando
			CargaMinima = 2,         // `chargedelay`
			Nome = "Masenko",
		});
	}

	/// <summary>
	/// MAKANKOSAPPO (`beams.dm:338`). `kireq = 20*BaseDrain` mas `lastbeamcost = 30*BaseDrain`,
	/// `powmod = 1.3`, `maxdistance = 40`, `rangemod = 1.03`, `chargedelay = 6`.
	///
	/// O IRMAO INVERTIDO DO MASENKO -- *"gains power was it travels"*, diz o verb (com o erro de
	/// digitacao do autor). Aos quarenta tiles chega com ~3,3x o que saiu. Os seis de `chargedelay`
	/// sao a conta que fecha: e o raio mais forte de longe e o que mais tempo deixa voce plantado no
	/// lugar carregando.
	/// </summary>
	private void MakankosappoG5(ServerPlayer pl)
	{
		if (SoltarCanal(pl, "Makkankosappo")) return;

		double custo = 20 * pl.Ficha.BaseDrain();
		Canalizar(pl, "Makkankosappo", custo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam,
			BaseDano = 1.3,
			Velocidade = 1,
			AlcanceTiles = 40,
			RangeMod = 1.03,         // ENGROSSA andando
			CargaMinima = 6,
			Nome = "Makankosappo",
		}, custoPorTiro: 30 * pl.Ficha.BaseDrain());
	}

	/// <summary>
	/// MASSIVE BEAM (`beams.dm:407`). `kireq = 15*BaseDrain`, `lastbeamcost = 150*BaseDrain`,
	/// `beamspeed = 0.5`, `powmod = 4`, `wavemult = 2`, `maxdistance = 30`, `chargedelay = 5`.
	///
	/// QUINZE VEZES O CUSTO DE MANUTENCAO do Ki Wave, e essa e a assinatura dele: barato de comecar,
	/// impagavel de segurar. O `wavemult = 2` poe o dobro do seu proprio poder por tras do feixe --
	/// e o que faz ele GANHAR embate de quem tem o mesmo BP que voce. Ver `Projetil.MultDeOnda`.
	/// </summary>
	private void RaioColossalG5(ServerPlayer pl)
	{
		if (SoltarCanal(pl, "Massive_Beam")) return;

		double custo = 15 * pl.Ficha.BaseDrain();
		Canalizar(pl, "Massive_Beam", custo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam,
			BaseDano = 4,
			Velocidade = 0.5,        // metade da velocidade: 0,2 s por tile
			AlcanceTiles = 30,
			MultDeOnda = 2,
			CargaMinima = 5,
			Nome = "Raio Colossal",
		}, custoPorTiro: 150 * pl.Ficha.BaseDrain());
	}

	/// <summary>
	/// FINAL FLASH (`beams/FinalFlash.dm:29`). `kireq = 25*BaseDrain`, `beamspeed = 0.6`,
	/// `wavemult = 4`, e QUATRO DEGRAUS por `beamskill`:
	///
	///     beamskill &lt; 30   -> custo x1    powmod 2.5   alcance 80
	///     beamskill &lt; 70   -> custo x10   powmod 2.8   alcance 90
	///     beamskill &lt; 100  -> custo x20   powmod 3.0   alcance 90
	///     beamskill >= 100 -> custo x30   powmod 3.3   alcance 95
	///
	/// ============================ UM DEFEITO DO ORIGINAL, CONSERTADO ============================
	/// O ultimo degrau do DM e `else if(usr.beamskill==100)` -- IGUALDADE EXATA. Como `beamskill`
	/// e um `float` que cresce em fracoes (`beamcounter += 3` e um contador que vira pericia), ele
	/// passa de 99,x pra 100,y sem NUNCA valer exatamente 100. E quando passa, nenhum dos quatro
	/// ramos casa: o `charging = 1` fica de fora, e o verb inteiro nao faz nada.
	///
	/// Ou seja, no jogo antigo o mestre absoluto de raio PERDIA o Final Flash. Aqui o ultimo degrau
	/// e `>= 100`. O que se perdeu: nada. O que se ganhou: a tecnica continua existindo pra quem
	/// mais treinou pra ter ela.
	/// ============================================================================================
	/// </summary>
	private void FinalFlashG5(ServerPlayer pl)
	{
		if (SoltarCanal(pl, "Final_Flash")) return;

		double kireq = 25 * pl.Ficha.BaseDrain();
		double bs = pl.Ficha.beamskill;

		(double mult, double powmod, double alcance) = bs switch
		{
			< 30 => (1.0, 2.5, 80.0),
			< 70 => (10.0, 2.8, 90.0),
			< 100 => (20.0, 3.0, 90.0),
			_ => (30.0, 3.3, 95.0),   // `== 100` no DM -- ver o bloco acima
		};

		Canalizar(pl, "Final_Flash", kireq, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam,
			BaseDano = powmod,
			Velocidade = 0.6,
			AlcanceTiles = alcance,
			MultDeOnda = 4,          // *"makes the FinalFlash BP 4x greater than your own. Oof."*
			CargaMinima = 1,         // o verb nao mexe no `chargedelay`: fica o padrao
			Nome = "Final Flash",
		}, custoPorTiro: kireq * mult);
	}

	// =====================================================================
	// AS BOLAS -- `Code/Modules/Skills/Ki/blasts.dm` e vizinhos
	// =====================================================================
	/// <summary>
	/// CHARGED SHOT (`blasts.dm:87`). `kireq = 50*BaseDrain`, `basicCD += 20` (2 s),
	/// `BP = expressedBP*1.2`, `basedamage = 2*Ekioff*log_10(max(blastskill,10))*globalKiDamage`.
	///
	/// O `globalKiDamage` APARECE DUAS VEZES no original -- uma aqui no `basedamage` e outra dentro
	/// do `Bump` (`objects.dm:315`: `mods*6*globalKiDamage`). Nao e engano meu: o `Basic_Blast` da
	/// linha 63 do MESMO arquivo nao o multiplica, e por isso o Charged Shot vale mais que "o dobro"
	/// do tiro comum. Deixei os dois, porque tirar um seria escolher um numero que o jogo antigo nao
	/// escolheu.
	/// </summary>
	private void TiroCarregadoG5(ServerPlayer pl)
	{
		long agora = NowMs();
		if (EmEsperaG5(pl, _blastPronto, "sua mao ainda esta juntando energia")) return;

		double custo = 50 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		_blastPronto[pl.Id] = agora + (long)(20 * MsPorTique);
		pl.Ficha.Ki -= custo;

		// `usr.Blast_Gain()` tres vezes + `chargedcounter += 5` -- e o unico canal de treino da
		// pericia de CARGA, que e a que encurta o raio (ver `Projetil.SegundosDeCarga`).
		for (int i = 0; i < 3; i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.blastskill += 0.05;
		pl.Ficha.chargedskill += 0.05;
		// `usr.blastcounter += 5` (`blasts.dm:100`) -- CINCO, e nao um: o tiro carregado conta por
		// cinco no contador de exp, ainda que a pericia daqui suba os mesmos 0,05 do tiro simples.
		// O `chargedcounter += 5` da linha seguinte do DM NAO entra: nenhuma das 30 pericias observa
		// `chargedcounter` (ele so alimenta o somatorio do `Ki_Effusion`, que nao e uma delas).
		CreditarContador(pl, "blastcounter", 5);

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 2 * pl.Ficha.Ekioff * Log10Min(pl.Ficha.blastskill, 10)
					   * DanoDeKi.DanoGlobalDeKi,
			Velocidade = 1,
			AlcanceTiles = AlcanceDeBolaG5,
			MultDeOnda = 1.2,        // `passbp = expressedBP*1.2`
			Nome = "Tiro Carregado",
		}, verbo: "Charged_Shot");
	}

	/// <summary>
	/// KILL DRIVER (`blasts/KillDriver.dm:33`). `Ki -= 50*BaseDrain`, `basedamage = 0.5`,
	/// `deflectable = 0`, `paralysis = 1`, `walk(A, dir, 2)` (0,2 s por tile).
	///
	/// A DESCRICAO DA SKILL MENTE UM POUCO -- *"a stun attack that does damage in addition"* --,
	/// mas o `basedamage = 0.5` contra o `1*Ekioff*log_10(...)` do Basic Blast diz outra coisa: ele
	/// e um tiro de PRENDER, e o dano e enfeite. Mantive o 0,5.
	///
	/// O `shockwave = 1` do original NAO foi portado: ele pertence a familia `kishock`/`kiforceful`/
	/// `kiinterfere` (as propriedades de ki do DM), e nenhuma das tres existe neste port. Fica
	/// catalogado; sem ela o tiro perde o estouro de area no impacto e continua paralisando, que e
	/// o que a skill promete.
	///
	/// UM DEFEITO DO ORIGINAL, CONSERTADO: o verb testa `usr.Ki < 50` (numero cru) e logo abaixo
	/// cobra `50*BaseDrain` (escalado com o Ki maximo). Quem tem muito Ki maximo passava no teste e
	/// saia com Ki negativo. Aqui confere o valor que cobra, como no Guided Ball.
	/// </summary>
	private void KillDriverG5(ServerPlayer pl)
	{
		double custo = 50 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		pl.Ficha.Ki -= custo;
		for (int i = 0; i < 4; i++) pl.Ficha.BlastGain(_rng);

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 0.5,
			Velocidade = 2,          // `walk(A,dir,2)`: 0,2 s por tile
			AlcanceTiles = AlcanceDeBolaG5,
			Deflectivel = false,     // `A.deflectable = 0`
			Paralisia = true,        // `A.paralysis = 1`
			Nome = "Kill Driver",
		}, verbo: "KillDriver");
		Falar(pl, Protocol.Fala.Diz, "Kill Driver!");
	}

	/// <summary>
	/// BUSTER SHELL (`blasts/BusterShell.dm:26`). QUATRO bolas, `Ki -= 4 + BaseDrain`,
	/// `basedamage = 0.5` em cada, `walk(A, dir, 2)`.
	///
	/// AS QUATRO SAEM EM LEQUE, e nao empilhadas. No DM elas nascem no MESMO tile e o
	/// `obj/proc/BusterShell()` as faz oscilar um tile pro lado a cada decimo de segundo enquanto
	/// voam -- um chacoalhado que, visto de longe, e um leque. Aqui o chacoalhado vira o que ele
	/// produzia: um deslocamento PERPENDICULAR fixo por bola (-24, -8, +8, +24 px). O port nao tem
	/// oscilacao por projetil e inventar uma seria um sistema; o resultado em jogo -- quatro linhas
	/// paralelas cobrindo a frente -- e o mesmo, e e o que a descricao da skill promete
	/// (*"a multiball blast"*).
	///
	/// O CUSTO E CRU, e isso e do DM: `4 + BaseDrain`, e nao `4*BaseDrain`. Quatro bolas por quase
	/// nada. E a tecnica mais barata deste lote inteiro, de longe, e o `basedamage = 0.5` e o
	/// contrapeso.
	/// </summary>
	private void BusterShellG5(ServerPlayer pl)
	{
		double custo = 4 + pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		pl.Ficha.Ki -= custo;
		for (int i = 0; i < 4; i++) pl.Ficha.BlastGain(_rng);

		Vec2 frente = MeleeArea.Frente(pl.Facing);
		Vec2 lado = new(-frente.Y, frente.X);
		float[] desvios = [-24, -8, 8, 24];

		foreach (float d in desvios)
			Disparar(pl, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast,
				BaseDano = 0.5,
				Velocidade = 2,
				AlcanceTiles = AlcanceDeBolaG5,
				Nome = "Buster Shell",
			}, rumoDado: frente, deOnde: pl.Pos + lado * d, verbo: "BusterShell");

		Falar(pl, Protocol.Fala.Diz, "Buster Shell!");
	}

	/// <summary>
	/// AS DUAS BARRAGENS: SCATTERSHOT (`blasts.dm:137`) e ENERGY BARRAGE (`blasts.dm:202`).
	///
	/// Sao o MESMO verb com quatro numeros trocados, e por isso um metodo so -- escrever duas copias
	/// e como as duas divergem no dia em que alguem mexe numa:
	///
	///                     base   Ki/bola   recarga         dano por bola   espalha?
	///     Scattershot      8     x40       Eactspeed*5     1,2 x ...        SIM (3 tiles)
	///     Energy Barrage  10     x15       Eactspeed*2     0,8 x ...        nao
	///
	/// `amount = base + bonusShots + round(log(blastskill)) + round(log(volleyskill))` -- LOGARITMO
	/// NATURAL (o `log(x)` de um argumento so do BYOND), e nao o de base 10 do resto do arquivo.
	///
	/// O PISO DE 1 NO LOGARITMO E DO PORT: o verb original chama `log(usr.blastskill)` sem guarda
	/// nenhuma, e `blastskill` nasce ZERO. No BYOND isso e um runtime silencioso; aqui seria `-inf`
	/// virando um numero de bolas negativo. O `Ki_Bomb` do mesmo arquivo JA escreve
	/// `log(max(usr.blastskill,1))` -- ou seja, o autor conhecia a guarda e a esqueceu nestes dois.
	/// </summary>
	private void BarragemG5(ServerPlayer pl, bool disperso)
	{
		if (EmEsperaG5(pl, _volleyPronto, "suas maos ainda estao quentes da ultima barragem")) return;

		double baseQtd = disperso ? 8 : 10;
		double porBola = disperso ? 40 : 15;
		double tiquesDeRecarga = disperso ? pl.Ficha.Eactspeed * 5 : pl.Ficha.Eactspeed * 2;
		double dano = disperso ? 1.2 : 0.8;

		int quantas = (int)Math.Max(
			baseQtd + pl.Ficha.bonusShots
			+ DmMath.Round(Math.Log(Math.Max(pl.Ficha.blastskill, 1)), 1)
			+ DmMath.Round(Math.Log(Math.Max(pl.Ficha.volleyskill, 1)), 1), 1);

		double custo = quantas * porBola * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		// O TETO DE TIROS DA ZONA ENTRA AQUI, ANTES DA COBRANCA. O `Disparar` recusa uma a uma em
		// silencio (devolve o projetil morto), entao sem esta conferencia o jogador pagaria por vinte
		// bolas e veria tres saindo. Um teto que cobra pelo que nao entrega e pior que teto nenhum.
		if (ProjeteisDaZona(pl.Zone.Hash).Count + quantas > MaxProjeteisPorZona)
		{
			Avisar(pl, "o ar aqui ja esta saturado de energia -- nao cabe uma barragem inteira.");
			return;
		}

		_volleyPronto[pl.Id] = NowMs() + (long)(Math.Max(tiquesDeRecarga, 2) * MsPorTique);
		pl.Ficha.Ki -= custo;
		for (int i = 0; i < (disperso ? 4 : 3); i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.blastskill += 0.05 * quantas;
		pl.Ficha.volleyskill += 0.05 * quantas;

		// ============ OS DOIS VERBS DO DM QUE ESTE METODO JUNTOU ============
		// O `disperso` escolhe entre `Scattershot` (`blasts.dm:137`, QUATRO `Blast_Gain()`) e a
		// barragem reta (`blasts.dm:~200`, TRES) -- e o numero de `Blast_Gain()` do laco acima e o
		// que identifica cada um. Os contadores diferem entre eles:
		//
		//   Scattershot   `blastcounter+=amount; homingcounter+=amount; volleycounter+=amount` (:154-156)
		//   barragem reta `blastcounter+=amount; volleycounter+=amount`                        (:218-219)
		//
		// O `homingcounter` SO EXISTE NO GALHO DISPERSO, e ele importa: era tido como um contador
		// sem fonte nenhuma neste port -- as tres Homing Mastery ficariam no nivel 0 pra sempre.
		// Esta linha e a unica fonte dele no jogo (a outra do DM, `Effusion.dm:444`, e o effector de
		// uma skill e nao um golpe).
		CreditarContador(pl, "blastcounter", quantas);
		CreditarContador(pl, "volleycounter", quantas);
		if (disperso) CreditarContador(pl, "homingcounter", quantas);

		Vec2 frente = MeleeArea.Frente(pl.Facing);
		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = dano * pl.Ficha.Ekioff * Log10Min(pl.Ficha.blastskill, 10)
					   * DanoDeKi.DanoGlobalDeKi,
			Velocidade = 1,
			AlcanceTiles = AlcanceDeBolaG5,
			Nome = disperso ? "Tiro Disperso" : "Barragem de Energia",
		};

		for (int i = 0; i < quantas; i++)
		{
			// `step(A,randdir)` TRES VEZES antes do `walk(A,curdir)`: a bola nasce ate tres tiles
			// pro lado e SO ENTAO segue pra frente, todas paralelas. E o que faz o Scattershot ser
			// uma nuvem e a Barragem ser uma linha.
			Vec2 berco = pl.Pos;
			if (disperso)
			{
				Vec2 desvio = DirecaoSorteadaG5();
				berco += desvio * (3 * ZoneCollision.TileSize);
			}
			Disparar(pl, receita, rumoDado: frente, deOnde: berco,
					 verbo: disperso ? "Scattershot" : "Energy_Barrage");
		}

		Avisar(pl, $"voce despeja {quantas} esferas de energia.");
	}

	/// <summary>
	/// OS DOIS CERCOS: KI MINEFIELD (`blasts.dm:404`) e HELLZONE GRENADE (`blasts.dm:466`).
	///
	/// Outro par que e o mesmo verb com numeros trocados -- e a diferenca entre eles e UMA LINHA do
	/// original (`A.homingchance`: 0 contra 100, mais o `spawn(10) A.blasthoming`):
	///
	///                      Ki/bola   recarga           dano por bola   as bolas...
	///     Ki Minefield      x15      Eactspeed*5       2 x ...          FICAM onde nasceram
	///     Hellzone Grenade  x50      Eactspeed*10      4 x ...          esperam 1 s e CONVERGEM
	///
	/// `amount = 7 + bonusShots + round(log(max(blastskill,1))) + round(log(max(targetedskill,1)))`
	/// -- aqui o autor LEMBROU da guarda do logaritmo (ver o comentario da barragem).
	///
	/// AS BOLAS NASCEM EM VOLTA DO ALVO, e nao na sua mao:
	/// `A.loc = locate(pick(target.x+2, +1, -1, -2), pick(target.y+2, +1, -1, -2), z)`. Repare que o
	/// ZERO nao esta na lista dos dois eixos -- nenhuma bola nasce EM CIMA do alvo. E de proposito:
	/// a tecnica e um cerco, e um cerco que ja acertou nao e um cerco.
	/// </summary>
	private void CercoG5(ServerPlayer pl, bool converge)
	{
		if (EmEsperaG5(pl, _alvoPronto, "voce ainda nao consegue armar outro cerco")) return;

		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null || alvo == pl || alvo.Ficha.dead || !alvo.Zone.Equals(pl.Zone))
		{
			Avisar(pl, "isso precisa de um alvo marcado. (Marque alguem com duplo clique.)");
			return;
		}
		// `if(usr.target in oview(20))` -- vinte tiles, e a recusa e do DM: *"Your target is not in range!"*
		if (Vec2.Distance(alvo.Pos, pl.Pos) > 20 * ZoneCollision.TileSize)
		{
			Avisar(pl, "seu alvo esta longe demais pra isso.");
			return;
		}

		int quantas = (int)Math.Max(
			7 + pl.Ficha.bonusShots
			+ DmMath.Round(Math.Log(Math.Max(pl.Ficha.blastskill, 1)), 1)
			+ DmMath.Round(Math.Log(Math.Max(pl.Ficha.targetedskill, 1)), 1), 1);

		double custo = quantas * (converge ? 50 : 15) * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		if (ProjeteisDaZona(pl.Zone.Hash).Count + quantas > MaxProjeteisPorZona)
		{
			Avisar(pl, "o ar aqui ja esta saturado de energia -- nao cabe o cerco inteiro.");
			return;
		}

		double tiques = Math.Max(pl.Ficha.Eactspeed * (converge ? 10 : 5), 20);
		_alvoPronto[pl.Id] = NowMs() + (long)(tiques * MsPorTique);
		pl.Ficha.Ki -= custo;
		for (int i = 0; i < (converge ? 5 : 4); i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.blastskill += 0.05 * quantas;
		pl.Ficha.targetedskill += 0.15 * quantas;   // `targetedcounter += amount*3`
		pl.Ficha.volleyskill += 0.05 * quantas;

		// Mesma historia do metodo da barragem: `converge` escolhe entre os dois verbs mirados do DM
		// (QUATRO `Blast_Gain()` em `blasts.dm:417-424`, CINCO em `:479-487`), e o de cinco acrescenta
		// `homingcounter += amount` aos tres contadores comuns. O `*3` do targeted e literal (`:423`).
		CreditarContador(pl, "blastcounter", quantas);
		CreditarContador(pl, "targetedcounter", quantas * 3);
		CreditarContador(pl, "volleycounter", quantas);
		if (converge) CreditarContador(pl, "homingcounter", quantas);

		var receita = new ReceitaDeProjetil
		{
			Tipo = converge ? TipoDeProjetil.Guided : TipoDeProjetil.Blast,
			BaseDano = (converge ? 4 : 2) * pl.Ficha.Ekioff * Log10Min(pl.Ficha.blastskill, 10)
					   * DanoDeKi.DanoGlobalDeKi,
			Velocidade = 1,
			AlcanceTiles = 200,      // ver abaixo: quem apaga estas bolas e o prazo, nao o alcance
			Nome = converge ? "Hellzone Grenade" : "Campo Minado",
		};

		foreach (Vec2 canto in CantosDoCercoG5(alvo.Pos, quantas))
		{
			// RUMO NULO nos dois casos: a mina fica parada pra sempre, e a bola da Hellzone fica
			// parada ate a espera de caca vencer. Ver `AndarProjetil` passo 5b.
			Projetil p = Disparar(pl, receita, rumoDado: Vec2.Zero, deOnde: canto,
								  verbo: converge ? "Hellzone_Grenade" : "Ki_Bomb");
			if (!p.Vivo) continue;

			// `spawn A.Burnout(40)` -- QUATRO segundos, e nao os cinco de todo mundo. Sao eles que
			// apagam o cerco; o alcance de 200 tiles acima existe so pra o prazo chegar primeiro.
			p.VidaRestante = 4;
			if (!converge) continue;

			p.Alvo = alvo.Id;
			p.EsperaDeCaca = 1.0;    // `spawn(10)`: um segundo parado antes de fechar
		}

		Falar(pl, Protocol.Fala.Diz, converge ? "Hellzone Grenade!" : "Ki Minefield!");
		Avisar(alvo, converge
			? $"{quantas} esferas de {pl.Name} se acendem em volta de voce."
			: $"{quantas} esferas de {pl.Name} ficam paradas no ar em volta de voce.");
	}

	/// <summary>
	/// OS PONTOS DO CERCO: `pick(x+2, x+1, x-1, x-2)` nos dois eixos (`blasts.dm:434`).
	///
	/// SORTEIO COM REPOSICAO, como no DM -- duas bolas podem calhar no mesmo canto, e calham. Nao
	/// "melhorei" pra uma distribuicao uniforme porque a irregularidade E o cerco: um anel perfeito
	/// de sete bolas teria sempre a mesma brecha, e o jogador aprenderia a brecha em vez da tecnica.
	/// </summary>
	private IEnumerable<Vec2> CantosDoCercoG5(Vec2 centro, int quantas)
	{
		float[] passos = [2, 1, -1, -2];
		for (int i = 0; i < quantas; i++)
		{
			float dx = passos[_rng.Next(passos.Length)] * ZoneCollision.TileSize;
			float dy = passos[_rng.Next(passos.Length)] * ZoneCollision.TileSize;
			yield return centro + new Vec2(dx, dy);
		}
	}

	/// <summary>
	/// KIENZAN (`discs.dm:26`). `kireq = 100*BaseDrain`, `basedamage = 25` FIXO,
	/// `spawn A.Burnout(1200)` -- cento e vinte segundos de vida.
	///
	/// O `basedamage` FIXO e o que o disco e: ele nao cresce com `Ekioff` nem com pericia de bola
	/// como as outras bolas deste arquivo. Corta igual pra quem acabou de aprender e pra quem tem
	/// mil anos de treino -- e por isso ele e a arma de quem esta em desvantagem.
	///
	/// DUAS COISAS DO ORIGINAL NAO FORAM PORTADAS, e as duas sao movimento:
	///   * `A.ZigZag()` -- o disco vai serpenteando em vez de reto. E enfeite de trajetoria e o port
	///     nao tem oscilacao por projetil (ver a nota do Buster Shell).
	///   * o laco `while(A && usr.Guiding) A.dir = usr.dir` -- no DM voce PILOTA o disco virando o
	///     corpo. Aqui ele persegue o alvo MARCADO, que e o que o `blasthoming(usr.target)` da linha
	///     seguinte do mesmo verb ja fazia. As duas conduzem o disco ao mesmo lugar; a do port nao
	///     prende o jogador olhando pro disco.
	/// </summary>
	private void KienzanG5(ServerPlayer pl)
	{
		double custo = 100 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		ServerPlayer? alvo = Marcado(pl);
		if (alvo != null && (alvo == pl || alvo.Ficha.dead || !alvo.Zone.Equals(pl.Zone))) alvo = null;

		pl.Ficha.Ki -= custo;
		for (int i = 0; i < 4; i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.guidedskill += 0.1;

		// `usr.guidedcounter += 2` (`discs.dm:93`). NO DM ISSO E POR PASSO DO DISCO, dentro do
		// `while(A&&A.loc&&usr.Guiding)` que o empurra enquanto se guia; aqui o disco e um projetil
		// que anda sozinho e nao ha laco de guia, entao o credito sai UMA vez, no disparo.
		// PERDEU-SE: quem guiava um disco por muito tempo treinava mais. GANHOU-SE: nao ha como
		// segurar um disco parado ao lado do corpo pra farmar `guidedcounter` sem lutar.
		CreditarContador(pl, "guidedcounter", 2);

		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			// SEM ALVO ELE VOA RETO, como qualquer bola -- o `blasthoming` do DM tambem so entra
			// `if(usr.target && usr.target != usr)`.
			Tipo = alvo != null ? TipoDeProjetil.Guided : TipoDeProjetil.Blast,
			BaseDano = 25,
			Velocidade = 1,
			AlcanceTiles = 120,
			Nome = "Kienzan",
		}, verbo: "Kienzan");
		if (p.Vivo)
		{
			p.Alvo = alvo?.Id ?? 0;
			p.VidaRestante = 120;    // `Burnout(1200)`
		}

		Falar(pl, Protocol.Fala.Diz, "Kienzan!");
	}

	/// <summary>
	/// AS DUAS PARALISIAS: PARALYSIS (`Ki2.0/Debuffs.dm:3`) e STUNLOCK
	/// (`Skill Trees/Race Trees/meta.dm:59`). Terceiro par identico deste lote:
	///
	///                  Ki                 poder por tras                recarga
	///     Paralysis    700*BaseDrain      expressedBP * log_10(...)     Eactspeed*6
	///     Stunlock     300*BaseDrain      expressedBP * log_11(...)     Eactspeed*7
	///
	/// SIM, LOG DE BASE ONZE. `log(11, max(kidebuffskill,10))` esta escrito assim no `meta.dm:92`, e
	/// nao e engano de leitura -- e o jeito de o Stunlock ser um pouco mais fraco que a Paralysis
	/// pelo mesmo caminho, sem mudar a formula. Com a pericia zerada os dois valem menos que 1
	/// (log_10(10) = 1, log_11(10) = 0,96), entao a Paralysis nasce neutra e o Stunlock nasce
	/// ligeiramente ABAIXO do seu proprio poder.
	///
	/// O `basedamage = 0.1` das duas nao e engano: elas nao existem pra machucar. O que decide a
	/// paralisia e o BP do tiro, pelo `BPModulus` de `Projetil.SegundosDeParalisia`.
	///
	/// O `kiforceful = 1` do Stunlock nao foi portado -- mesma familia do `shockwave` do Kill Driver
	/// (as propriedades de ki do DM, que este port nao tem). Fica catalogado.
	/// </summary>
	private void ParalisiaG5(ServerPlayer pl, bool forte)
	{
		if (EmEsperaG5(pl, _debuffPronto, "voce ainda nao consegue moldar outro debuff")) return;

		double custo = (forte ? 700 : 300) * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		double tiques = pl.Ficha.Eactspeed * (forte ? 6 : 7);
		_debuffPronto[pl.Id] = NowMs() + (long)(Math.Max(tiques, 2) * MsPorTique);

		pl.Ficha.Ki -= custo;
		for (int i = 0; i < 4; i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.kidebuffskill += forte ? 0.5 : 0.2;   // `kidebuffcounter += 5` / `+= 2`
		CreditarContador(pl, "kidebuffcounter", forte ? 5 : 2);   // `Debuffs.dm:8` / `meta.dm:64`

		// `BP = expressedBP * log_base(max(kidebuffskill,10))` -- base 10 na Paralysis, 11 no Stunlock.
		double baseLog = forte ? 10 : 11;
		double mult = Math.Log(Math.Max(pl.Ficha.kidebuffskill, 10)) / Math.Log(baseLog);

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 0.1,
			Velocidade = 2,          // `walk(A, dir, 2)`
			AlcanceTiles = AlcanceDeBolaG5,
			Deflectivel = false,
			Paralisia = true,

			// ============================ A PARALYSIS NAO EMPURRA, E ISSO E A TECNICA ============================
			// No DM um blast so arremessa se for `WaveAttack` (raio) ou se escrever `kiforceful`
			// (`objects.dm:450-460`). A Paralysis (`Ki2.0/Debuffs.dm:3-40`) **nao e nenhum dos dois** --
			// e a irma Stunlock (`meta.dm:59`) escreve `kiforceful = 1`, o que prova que a ausencia na
			// Paralysis e escolha e nao esquecimento do autor.
			//
			// SEM ESTA LINHA A TECNICA SE DESMENTIA: o arremesso escreve `TiquesDeVoo`, e
			// `CombatState.PodeAtacar()` recusa quem esta sendo arremessado. Ou seja, a tecnica que a
			// propria descricao promete ser *"o alvo continua batendo e se defendendo -- so nao consegue
			// mais fugir"* tirava os bracos junto com as pernas. A `--arsenalteste` reprovava nisso, com
			// a frase exata, e a acusacao dela estava certa.
			//
			// O STUNLOCK CONTINUA EMPURRANDO (padrao `true`), o que e o `kiforceful` dele -- e o unico
			// jeito de as duas paralisias serem diferentes sem mudar formula, que e como o DM as separa.
			// ================================================================================================
			Empurra = !forte,

			MultDeOnda = mult,
			Nome = forte ? "Paralisia" : "Stunlock",
		}, verbo: forte ? "Paralysis" : "Stunlock");

		Falar(pl, Protocol.Fala.Diz, forte ? "Paralysis!" : "Stunlock!");
	}

	// =====================================================================
	// FERRAMENTAS DO LOTE
	// =====================================================================
	/// <summary>
	/// A RECARGA, COM O TEMPO QUE FALTA NA FRASE. Os quatro contadores do DM passam por aqui.
	///
	/// O NUMERO NA MENSAGEM E O PONTO. O original ja escrevia *"This skill was set on cooldown for
	/// [barrageCD/10] seconds"* -- uma recusa muda faz o jogador apertar de novo achando que o
	/// pacote se perdeu, e este e o lote com quatro recargas diferentes de uma vez.
	/// </summary>
	private bool EmEsperaG5(ServerPlayer pl, Dictionary<int, long> mapa, string frase)
	{
		if (!mapa.TryGetValue(pl.Id, out long livre)) return false;
		long agora = NowMs();
		if (agora >= livre) { mapa.Remove(pl.Id); return false; }
		Avisar(pl, $"{frase} (faltam {(livre - agora) / 1000.0:0.#}s).");
		return true;
	}

	/// <summary>
	/// `log_10(max(x, piso))` -- o padrao que aparece em quase todo `basedamage` do `blasts.dm`.
	///
	/// Existe como funcao pra que o piso NAO seja digitado quatorze vezes: e ele que faz um
	/// personagem novo (pericia zero) receber exatamente 1,0 em vez de menos infinito, e um piso
	/// esquecido num dos catorze seria um dano negativo que ninguem consegue relatar.
	/// </summary>
	private static double Log10Min(double x, double piso)
		=> Math.Log(Math.Max(x, piso)) / Math.Log(10);

	/// <summary>
	/// UMA DAS OITO DIRECOES do BYOND, sorteada -- o `pick(NORTH,SOUTH,EAST,WEST,NORTHWEST,...)` do
	/// Scattershot (`blasts.dm:186`).
	///
	/// AS OITO, E NAO UM ANGULO QUALQUER: no DM as diagonais tem o mesmo peso das retas, e e isso
	/// que da a nuvem a cara de xadrez que ela tem. Um angulo continuo faria um disco uniforme --
	/// mais bonito, e outra tecnica.
	/// </summary>
	private Vec2 DirecaoSorteadaG5()
	{
		const float d = 0.70710678f;   // raiz de 2 sobre 2: a diagonal normalizada
		Vec2[] oito =
		[
			new(0, -1), new(0, 1), new(1, 0), new(-1, 0),
			new(-d, -d), new(d, -d), new(-d, d), new(d, d),
		];
		return oito[_rng.Next(oito.Length)];
	}
}
