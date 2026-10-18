namespace Jandirus.Core.Races;

/// <summary>
/// O KIT RACIAL DE UM CORPO (v0.4.0, pedido 16). Um conjunto por raca, e os de classe somam por cima: o Legendary tem o do Saiyajin
/// e mais o dele; o Legendary Primal tem o do Primal e mais o dele; o Frost Demon mutante tem o do Frost Demon e mais o dele.
/// </summary>
[Flags]
public enum KitRacial
{
	Nenhum = 0,
	Saiyajin = 1 << 0,
	Primal = 1 << 1,
	Lendario = 1 << 2,
	LendarioPrimal = 1 << 3,
	MeioSaiyajin = 1 << 4,
	Humano = 1 << 5,
	Namekuseijin = 1 << 6,
	FrostDemon = 1 << 7,
	Mutante = 1 << 8,
}

/// <summary>O TIPO DO GOLPE que o Saiyan's Resolve conta: contusao (soco, chute), corte (lamina) e ki (tiro, raio).</summary>
public enum TipoDeGolpe { Contusao, Corte, Ki }

/// <summary>
/// ============================ AS PASSIVAS E AS ATIVAS RACIAIS (v0.4.0) ============================
/// O dono (2026-10-17): *"meu amigo gringo deu sugestoes de passivas pras seguintes raças"* -- a lista inteira esta no
/// `PEDIDO-16.md` -- e, perguntado: elas **vem com a raca** (nao se compram na arvore), e o **Demonic Saiyan e uma skill ativa do
/// Legendary Primal**, e nao uma classe.
///
/// **NADA DISTO E DO DM.** E sistema novo, desenhado pelo amigo do dono; onde o texto dele nao dava numero, o numero e escolha minha
/// e esta marcado ESCOLHA nesta classe (e no relatorio). As leituras de palavra (o "Strength" e o ataque fisico, o "output" e a
/// barra de poder, o "combat tag" e a tag de 90 s, os "efeitos negativos"...) estao no `PEDIDO-16.md`.
///
/// O CORE SO CONTA. Quem sabe a forma, o corpo, a tag e o relogio e o servidor (`GameServer.Passivas.cs`): ele monta a
/// <see cref="EntradaRacial"/>, o <see cref="EstadoRacial"/> devolve o <see cref="EfeitoRacial"/>, e o servidor o escreve nos canais
/// (`Fighter.racial*`, `CombatState.ReducaoRacial` e vizinhos). Os canais sao ESCRITOS a cada tique, nunca acumulados -- a mesma
/// regra do `AplicarForma`.
/// ==================================================================================================
/// </summary>
public static class Passivas
{
	// =====================================================================
	// O KIT
	// =====================================================================
	/// <summary>
	/// QUAL KIT ESTE CORPO TEM. O Saiyajin e a linhagem COMUM (Low-Class, Normal, Elite e Legendary); o Primal tem o conjunto PROPRIO
	/// -- a lista do amigo da o "tudo do Saiyajin normal" so pro Legendary, e o Primal tem passivas que conversam com o rabo e o SSJ4.
	/// O androide (a raca "Android", que e o Humano convertido) nao tem o kit humano: o Sheer Willpower ja dizia *"Only available when
	/// they are not an android"*.
	/// </summary>
	public static KitRacial KitDe(string raca, string classe, string linhagemSaiyajin) => raca switch
	{
		"Saiyan" when classe == ClasseLendariaPrimal => KitRacial.Primal | KitRacial.LendarioPrimal,
		"Saiyan" when linhagemSaiyajin == "Primal Saiyan" || classe == "Normal Primal Saiyan" => KitRacial.Primal,
		"Saiyan" when classe == ClasseLendaria => KitRacial.Saiyajin | KitRacial.Lendario,
		"Saiyan" => KitRacial.Saiyajin,
		MeioSaiyajinRaca => KitRacial.MeioSaiyajin,
		"Human" => KitRacial.Humano,
		"Namekian" => KitRacial.Namekuseijin,
		"Icer" when classe == ClasseMutante => KitRacial.FrostDemon | KitRacial.Mutante,
		"Icer" => KitRacial.FrostDemon,
		_ => KitRacial.Nenhum,
	};

	public const string ClasseLendaria = "Legendary", ClasseLendariaPrimal = "Legendary Primal Saiyan", ClasseMutante = "Mutant Frost Demon";
	private const string MeioSaiyajinRaca = "Halfbreed";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--passivasteste`): nenhum corpo tem kit -- o jogo de antes da v0.4.0. Lido pelo servidor ao montar
	/// a entrada. Falso em jogo.
	/// </summary>
	public static bool SemKitDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--passivasteste` e `--iaraciaisteste`): so PESSOA tem kit -- o portao da v0.4.0, antes do dono pedir o
	/// kit pro povo do mundo e pros chefes de saga (2026-10-17). Lido pelo servidor (`GameServer.TemKitRacial`). Falso em jogo.
	/// </summary>
	public static bool SoPessoaTemKitDeTeste;

	// =====================================================================
	// AS IDS DAS ATIVAS -- o canal de habilidade (`C2S.Habilidade`) e a chave da recarga no save
	// =====================================================================
	public const string Ferocidade = "rc_ferocidade", Transbordar = "rc_transbordar", ForcaImparavel = "rc_imparavel",
		TransbordarKi = "rc_transbordar_ki", SedeDeSangue = "rc_sede", Demonico = "rc_demonico", SegundoFolego = "rc_folego",
		ForcaDeVontade = "rc_vontade", PoderMaximo = "rc_poder_maximo", Disrupcao = "rc_disrupcao", Ira = "rc_ira",
		MenteCruel = "rc_mente_cruel", Sadismo = "rc_sadismo";

	/// <summary>
	/// O KIT QUE DA ESTA ATIVA (Nenhum = o id nao e de ativa racial). Quem pergunta e `SkillsRaciais.TemAtiva` -- o verb
	/// (`GameServer.UsarAtivaRacial`), a IA (`GameServer.AtivasRaciaisNaMao`) e o botao: as ativas dos kits de CLASSE pelo kit, e as dos kits
	/// de raca pelo bit da skill (2026-10-17), que no bio-androide e a escolha dele.
	/// </summary>
	public static KitRacial KitDaAtiva(string id) => id switch
	{
		Ferocidade => KitRacial.Primal,
		Transbordar or ForcaImparavel => KitRacial.Lendario,
		TransbordarKi or SedeDeSangue or Demonico => KitRacial.LendarioPrimal,
		SegundoFolego or ForcaDeVontade or PoderMaximo => KitRacial.Humano,
		Disrupcao => KitRacial.Namekuseijin,
		Ira => KitRacial.FrostDemon,
		MenteCruel or Sadismo => KitRacial.Mutante,
		_ => KitRacial.Nenhum,
	};

	/// <summary>As chaves de recarga das passivas que tem uma (no save, junto das ativas).</summary>
	public const string Limitless = "rc_limitless", Ressurreicao = "rc_ressurreicao", Metamorfose = "rc_metamorfose";

	// =====================================================================
	// SAIYAJIN
	// =====================================================================
	/// <summary>Seasoned Warrior: +10% por forma veterana, ate +60%. O 1o degrau pede 10 s numa forma, o 2o 20 s, o 3o 30 s...</summary>
	public const double BonusPorFormaVeterana = 0.10, TetoDoVeterano = 0.60, SegundosPorDegrauVeterano = 10;

	public static double SegundosPraVeterana(int jaPagas) => SegundosPorDegrauVeterano * (jaPagas + 1);

	public static double BonusVeterano(int pagas) => Math.Min(pagas * BonusPorFormaVeterana, TetoDoVeterano);

	/// <summary>Warrior Race: abaixo de 20% de vida, ataque fisico +20% e defesa fisica +30%.</summary>
	public const double VidaDoGuerreiro = 0.20, AtaqueDoGuerreiro = 1.20, DefesaDoGuerreiro = 1.30;

	/// <summary>Saiyan's Resolve: a partir do 2o golpe do MESMO tipo, 2% de reducao por golpe, ate 20%; dura 10 s, o golpe renova.</summary>
	public const double ResolvePorGolpe = 2, TetoDoResolve = 20, SegundosDoResolve = 10;

	public static double ReducaoDoResolve(int golpesDoMesmoTipo) =>
		golpesDoMesmoTipo >= 2 ? Math.Min(golpesDoMesmoTipo * ResolvePorGolpe, TetoDoResolve) : 0;

	/// <summary>O degrau do Prideful pela barra: 0 (75% ou mais), 1 (abaixo de 75%), 2 (abaixo de 50%), 3 (abaixo de 25%).</summary>
	public static int DegrauDoOrgulho(double barra) => barra < 0.25 ? 3 : barra < 0.50 ? 2 : barra < 0.75 ? 1 : 0;

	/// <summary>Prideful por degrau: (regen de Ki e vigor, ataque/defesa fisicos, Ki Skill) -- os numeros do amigo.</summary>
	public static (double Regen, double Fisico, double KiSkill) Orgulho(int degrau) => degrau switch
	{
		3 => (2.00, 1.30, 0.70),
		2 => (1.50, 1.25, 0.85),
		1 => (1.20, 1.10, 0.95),
		_ => (1, 1, 1),
	};

	/// <summary>False Advantage: abaixo de 50% de Ki, vigor ou vida.</summary>
	public const double RecursoDaFalsaVantagem = 0.50, DrenoDaFalsaVantagem = 0.50;

	/// <summary>False Advantage: a SOMA na forma mortal pela maior forma mortal DOMINADA (0 nenhuma, 1 SSJ1, 2 SSJ2, 3 SSJ3).</summary>
	public static double SomaDaFalsaVantagem(int maiorDominada) => maiorDominada switch
	{
		>= 3 => 4,
		2 => 2,
		1 => 1,
		_ => 0.5,
	};

	// =====================================================================
	// PRIMAL
	// =====================================================================
	/// <summary>Primal Evolution pelo degrau do SSJ4 (0 fora dele, 1 SSJ4, 2 Full Power, 3 Limit Breaker): +dano e -consumo de Ki.</summary>
	public static double EvolucaoPrimal(int degrauSsj4) => degrauSsj4 switch
	{
		>= 3 => 0.20,
		2 => 0.15,
		1 => 0.10,
		_ => 0.05,
	};

	/// <summary>Unmatched Ferocity: 1 min, mais 30/60/90 s com SSJ4/FP/LB; recarga 6 min.</summary>
	public static double SegundosDaFerocidade(int degrauSsj4) => 60 + 30 * Math.Clamp(degrauSsj4, 0, 3);

	public const double RecargaDaFerocidade = 6 * 60;

	/// <summary>Limitless Power: a fracao do ki do tiro bloqueado que fica guardada (5%; 10/20/30% com SSJ4/FP/LB), por 1 min; recarga 2 min.</summary>
	public static double FracaoLimitless(int degrauSsj4) => degrauSsj4 switch
	{
		>= 3 => 0.30,
		2 => 0.20,
		1 => 0.10,
		_ => 0.05,
	};

	public const double SegundosDoLimitless = 60, RecargaDoLimitless = 2 * 60;

	/// <summary>Shattering the Limit: o tiro carregado que custa 10% ou mais do Ki ATUAL sai 2x, e o grito ganha o prefixo.</summary>
	public const double CustoDoLimite = 0.10, MultDoLimite = 2;

	public const string PrefixoDoLimite = "10x ";

	/// <summary>Roaring Evolution: -10% de efeito por degrau, ate imune; no maximo um degrau a cada 10 s por skill.</summary>
	public const double PerdaPorRugido = 0.10, SegundosEntreRugidos = 10;

	public static double SobraDoRugido(int degraus) => Math.Max(0, 1 - degraus * PerdaPorRugido);

	// =====================================================================
	// LEGENDARY
	// =====================================================================
	/// <summary>Overflowing Power: +5% a cada 10 s, ate +30%; o desabafo so depois de 30 s.</summary>
	public const double PassoDoTransbordar = 0.05, TetoDoTransbordar = 0.30, SegundosPorPassoDoTransbordar = 10,
		SegundosPraDesabafar = 30;

	public static double BonusDoTransbordar(double segundos) =>
		Math.Min(Math.Floor(segundos / SegundosPorPassoDoTransbordar) * PassoDoTransbordar, TetoDoTransbordar);

	/// <summary>
	/// O PRECO DO TRANSBORDAR, em % do tronco por segundo (ESCOLHA: o amigo deu so a forma -- "cresce, e depois de 30 s escala
	/// rapido"): sobe ate 0,2%/s nos primeiros <paramref name="virada"/> segundos e, dali em diante, dispara (2,8%/s 30 s depois da
	/// virada, 7,5%/s 60 s depois). Quem nao desabafa cai.
	/// </summary>
	public static double TroncoPorSegundoDoTransbordar(double segundos, double virada)
	{
		if (segundos <= virada) return 0.2 * segundos / virada;
		return 0.2 + 0.5 * Math.Pow((segundos - virada) / 10, 1.5);
	}

	/// <summary>
	/// (2026-10-17) O GRITO DO DESABAFO -- o segundo aperto do Overflowing Power e do Overflowing Ki. O dono: *"n é so kiblasts saindo de
	/// forma organizada de uma vez , seriam 3 segundos do personagem gritando e uma esfera de energia verde parecendo com a do demonic em volta
	/// do personagem e nesses 3 segundos kiblasts de forma nao ordenada /aleatoria saindo pra todo lado sendo ate dificil pros inimigos
	/// desviarem"*. Era a rajada de doze tiros de uma vez, um a cada 30 graus.
	///
	/// Os 3 s e os tiros (ESCOLHA: doze por segundo, o triplo da rajada de antes espalhado no tempo), o raio do berco (ESCOLHA: os tiros nascem
	/// em qualquer ponto ate meio tile do centro, dentro da esfera) e as velocidades sorteadas -- as cinco da bola (`ReceitaDeProjetil.Velocidade`
	/// de -1 a 3: 12, 14, 16, 18 e 20 tiles/s, `Projetil.AtrasoDeBola`), que e o que faz a rajada nao ter ritmo.
	/// </summary>
	public const double SegundosDoGrito = 3, TilesDoBercoDoGrito = 0.5;

	/// <summary>Os tiros do grito inteiro.</summary>
	public const int TirosDoGrito = 36;

	/// <summary>As velocidades de bola que um tiro do grito sorteia (12 a 20 tiles/s).</summary>
	public static readonly double[] VelocidadesDoGrito = [-1, 0, 1, 2, 3];

	/// <summary>
	/// OS INSTANTES DOS TIROS DO GRITO, em segundos desde o comeco: cada um sorteado em qualquer ponto dos 3 s, e em ordem -- dois podem cair
	/// juntos, e um meio segundo pode passar sem nenhum.
	/// </summary>
	public static double[] InstantesDoGrito(Random sorteio)
	{
		var t = new double[TirosDoGrito];
		for (int i = 0; i < t.Length; i++) t[i] = sorteio.NextDouble() * SegundosDoGrito;
		Array.Sort(t);
		return t;
	}

	/// <summary>Unstoppable Force: 30 s sem recuo e -10% de dano; recarga 2 min.</summary>
	public const double SegundosImparavel = 30, ReducaoImparavel = 10, RecargaImparavel = 2 * 60;

	/// <summary>
	/// Escalating Power: transformado e em combate, com tronco, vigor E Ki em 80% ou menos, a forma sobe ate +30% -- mais rapido
	/// quanto mais baixo (ESCOLHA da curva: a media dos tres, `30% x p^1,5`, com p = quanto ela desceu abaixo dos 80%).
	/// </summary>
	public const double LinhaDaEscalada = 0.80, TetoDaEscalada = 0.30;

	public static double BonusDaEscalada(double tronco, double vigor, double ki)
	{
		if (tronco > LinhaDaEscalada || vigor > LinhaDaEscalada || ki > LinhaDaEscalada) return 0;
		double media = (tronco + vigor + ki) / 3;
		double p = Math.Clamp((LinhaDaEscalada - media) / LinhaDaEscalada, 0, 1);
		return TetoDaEscalada * Math.Pow(p, 1.5);
	}

	// =====================================================================
	// LEGENDARY PRIMAL
	// =====================================================================
	/// <summary>Overflowing Ki: Ki maximo +10% a cada 10 s, ate +100%; o desabafo so depois de 60 s.</summary>
	public const double PassoDoKiTransbordando = 0.10, TetoDoKiTransbordando = 1.00, SegundosPraDesabafarKi = 60;

	public static double BonusDoKiTransbordando(double segundos) =>
		Math.Min(Math.Floor(segundos / SegundosPorPassoDoTransbordar) * PassoDoKiTransbordando, TetoDoKiTransbordando);

	/// <summary>Primal Bloodlust: golpes sem recuo (15; 20/25/30 com SSJ4/FP/LB), +5% de dano recebido, +2% de dano por golpe; 2 min; recarga 10 min.</summary>
	public static int GolpesDaSede(int degrauSsj4) => 15 + 5 * Math.Clamp(degrauSsj4, 0, 3);

	public const double DanoRecebidoNaSede = 1.05, DanoPorGolpeDaSede = 0.02, SegundosDaSede = 2 * 60, RecargaDaSede = 10 * 60;

	/// <summary>
	/// Legendary Resurrection: a animacao invulneravel, a recarga de 30% de Ki, vigor e vida, os +10% em todos os stats por 3 min.
	/// A RECARGA de 10 min e ESCOLHA (o amigo nao deu): sem ela o Lendario Primal so morreria pela regra do "cair de novo nos 3 min".
	/// </summary>
	public const double SegundosDaAnimacaoDaRessurreicao = 3, RefilDaRessurreicao = 0.30, StatsDaRessurreicao = 1.10,
		SegundosDaRessurreicao = 3 * 60, RecargaDaRessurreicao = 10 * 60;

	/// <summary>
	/// Demonic Saiyan: 15 s parado carregando; depois, a cada 15 s, 10 s com Ki e vigor em 300% (o amigo: *"refills to 300% every 15
	/// seconds"*), ate o fim do combate; so na tag, depois de 2 min de luta, numa forma Lendaria, com o LSSJ ja aberto; recarga 20 min.
	/// E enquanto dura: consumo de Ki +10% (Mindless Beast) e efeitos de membro +10% (Primitive Rage).
	/// </summary>
	public const double SegundosDaCargaDemonica = 15, CicloDemonico = 15, JanelaDemonica = 10, ReservaDemonica = 3.0,
		LutaPraDemonico = 2 * 60, RecargaDemonica = 20 * 60, ConsumoDemonico = 1.10, MembrosDemonicos = 1.10;

	// =====================================================================
	// MEIO-SAIYAJIN
	// =====================================================================
	/// <summary>Prodigy: forma mortal nova comeca com 50% de maestria; dominada, +0,5x (+2x nas divinas) e regen ativa de Ki +25% (ESCOLHA).</summary>
	public const double MaestriaDoProdigio = 50, SomaDoProdigioMortal = 0.5, SomaDoProdigioDivino = 2.0, RegenDoProdigio = 1.25;

	/// <summary>Immeasurable Potential: o teto da carga de Ki vai a 225% em vez de 200% -- a mesma escala, 12,5% mais alta.</summary>
	public const double TetoDoPotencial = 2.25 / 2.0;

	/// <summary>Hidden Talent: tecnica dominada +10%; a mesma tecnica dominada duas vezes seguidas rende 2x na segunda.</summary>
	public const double TalentoDominado = 1.10, TalentoRepetido = 2.0;

	/// <summary>Blazing Spirit: membro abaixo de 50% -> -20% de dano por 1 min e sem efeitos negativos por 10 s.</summary>
	public const double LinhaDoEspirito = 0.50, ReducaoDoEspirito = 20, SegundosDaReducaoDoEspirito = 60, SegundosSemNegativosDoEspirito = 10;

	// =====================================================================
	// HUMANO
	// =====================================================================
	/// <summary>Persistence: +1x a cada 10 min de treino sem parar, ate 3x (ESCOLHA do ritmo).</summary>
	public const double SegundosPorPersistencia = 10 * 60, TetoDaPersistencia = 3;

	public static double FatorDaPersistencia(double segundosSeguidos) =>
		Math.Min(1 + Math.Max(segundosSeguidos, 0) / SegundosPorPersistencia, TetoDaPersistencia);

	/// <summary>Second Wind: 20 s devolvendo, no total, 50% do Ki e do vigor maximos (ESCOLHA); recarga 2 min.</summary>
	public const double SegundosDoFolego = 20, FracaoDoFolego = 0.50, RecargaDoFolego = 2 * 60;

	/// <summary>Sheer Willpower: 15 s sem efeitos negativos e sem morrer; recarga 10 min.</summary>
	public const double SegundosDaVontade = 15, RecargaDaVontade = 10 * 60;

	/// <summary>
	/// Max Power: ataque fisico, defesa fisica e velocidade 1,5x, somando com qualquer forma. O dreno (ESCOLHA): 1,5% do Ki maximo
	/// por segundo ligado, e 1% a cada soco dado.
	/// </summary>
	public const double MultDoPoderMaximo = 1.5, DrenoDoPoderMaximo = 0.015, CustoDoSocoNoPoderMaximo = 0.01;

	// =====================================================================
	// NAMEKUSEIJIN
	// =====================================================================
	/// <summary>Namekian Resilience e o Extreme Tenacity do Frost: o nocaute na metade do tempo.</summary>
	public const double NocauteResiliente = 0.5;

	/// <summary>
	/// Spirit Disruption: cada acerto limpo tira 10% do Ki MAXIMO do alvo, ate 40%, por 30 s; 4 golpes bloqueados ou errados desligam,
	/// e o 4o acerto (o teto) tambem; recarga 3 min.
	/// </summary>
	public const double DisrupcaoPorGolpe = 0.10, TetoDaDisrupcao = 0.40, SegundosDaDisrupcao = 30, RecargaDaDisrupcao = 3 * 60;

	public const int GolpesDaDisrupcao = 4;

	/// <summary>Warrior's Vitality: efeitos de membro pela metade; com todos inteiros, regen passiva de Ki e vigor +25% (ESCOLHA).</summary>
	public const double MembrosDoNamek = 0.5, RegenDoNamekInteiro = 1.25;

	// =====================================================================
	// FROST DEMON
	// =====================================================================
	/// <summary>Perfect Control: carga de tecnica de ki 2x mais rapida e +15% de dano de ki (ESCOLHA dos dois numeros).</summary>
	public const double CargaDoControle = 2.0, KiDoControle = 1.15;

	/// <summary>Extreme Tenacity: a reducao num MEMBRO pela vida dele -- 5% a 50%, 10% a 40%, 15% a 30%, 20% a 20% e 25% a 10%.</summary>
	public static double ReducaoTenaz(double vidaDoMembro) => vidaDoMembro switch
	{
		<= 0.10 => 25,
		<= 0.20 => 20,
		<= 0.30 => 15,
		<= 0.40 => 10,
		<= 0.50 => 5,
		_ => 0,
	};

	/// <summary>Unrestrained Wrath: BP +20% e Ki maximo -20% (ESCOLHA); o kiai empurra quem esta a 3 tiles; desliga a mao depois de 30 s; recarga 3 min.</summary>
	public const double BpDaIra = 1.20, KiDaIra = 0.80, TilesDoKiai = 3, SegundosMinimosDaIra = 30, RecargaDaIra = 3 * 60;

	/// <summary>Revitalizing Metamorphosis: membros +20%, decepados voltam com 10%, regen ativa e carga de Ki +40% por 1 min; recarga 3 min.</summary>
	public const double CuraDaMetamorfose = 0.20, MembroDaMetamorfose = 0.10, KiDaMetamorfose = 1.40, SegundosDaMetamorfose = 60,
		RecargaDaMetamorfose = 3 * 60;

	// =====================================================================
	// FROST DEMON MUTANTE
	// =====================================================================
	/// <summary>Long-Awaited 100%: subir de 50% ou menos ate 100% -> +10% de dano, defesa e velocidade, +5% de regen de Ki.</summary>
	public const double BarraDaEspera = 0.50, BonusDaEspera = 1.10, RegenDaEspera = 1.05;

	/// <summary>Ruthless Mind: 3 s executando; quem nao e amigo a 50 tiles leva -10% em todos os stats por 2 min; recarga 5 min (ESCOLHA).</summary>
	public const double SegundosDaExecucao = 3, TilesDaMenteCruel = 50, DebuffDaMenteCruel = 0.10, SegundosDaMenteCruel = 2 * 60,
		RecargaDaMenteCruel = 5 * 60;

	/// <summary>Sadistic Nature: 1 min; +5% de dano por golpe sem guarda, ate +100%; recarga 5 min.</summary>
	public const double SegundosDoSadismo = 60, SadismoPorGolpe = 0.05, TetoDoSadismo = 1.0, RecargaDoSadismo = 5 * 60;

	// =====================================================================
	// LEITURAS DE FORMA (ids do catalogo)
	// =====================================================================
	/// <summary>O degrau SSJ4 de uma forma: 1 SSJ4, 2 Full Power, 3 Limit Breaker -- da linha Saiyajin e da do Legendary Primal.</summary>
	public static int DegrauSsj4(string forma) => forma switch
	{
		"ssj4" or "primal_legendary4" => 1,
		"ssj4_full_power" or "primal_legendary4_full_power" => 2,
		"ssj4_limit_breaker" or "primal_legendary4_limit_breaker" => 3,
		_ => 0,
	};

	/// <summary>A forma mortal que conta pro False Advantage: 1 (SSJ1/Wrathful), 2 (SSJ2/C-Type), 3 (SSJ3/Legendary); 0 nas outras.</summary>
	public static int DegrauMortal(string forma) => forma switch
	{
		"ssj1" or "wrathful" => 1,
		"ssj2" or "c_type" => 2,
		"ssj3" or "legendary" => 3,
		_ => 0,
	};
}
