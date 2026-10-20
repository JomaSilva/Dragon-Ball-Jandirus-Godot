namespace Jandirus.Core.Stats;

/// <summary>
/// O ESTOMAGO -- `Stomach.dm` e `StaminaDrain.dm` do original.
///
/// ============================ O QUE FALTAVA ============================
/// O port ja tinha VIGOR (`stamina`), e ele ja caia ao correr, socar e juntar Ki. O que nao havia
/// era de onde ele VOLTA: a barra subia sozinha, por nada, e comer nao existia. No original o
/// folego nao se recupera do ar -- ele e comprado com COMIDA, e a nutricao e o tanque que paga.
///
/// A cadeia inteira do DM e esta:
///
///     comida  ->  nutricao  ->  vigor (e Ki)  ->  poder de luta
///
/// O ultimo elo ja existia aqui (`staminaratio` entra no `statusBuff`, que multiplica o BP
/// expresso). Os dois primeiros sao o que este arquivo traz.
/// =======================================================================
///
/// ============================ O QUE NAO VEIO, E POR QUE ============================
/// O DM MATA DE FOME -- ou melhor, matava: o bloco de KO e morte por inanicao esta COMENTADO no
/// original, com a anotacao "Trying rebalanced stamina with no death". O que sobrou la e dano
/// continuo com vigor zerado. Portei o que esta LIGADO, e nao o que esta comentado: reviver uma
/// regra que o proprio autor desligou seria portar a intencao dele ao contrario.
/// ===================================================================================
/// </summary>
public static class Nutricao
{
	/// <summary>
	/// O TANQUE CHEIO. `maxNutrition = 50` no DM, e o metabolismo estica: quem digere rapido
	/// carrega mais.
	/// </summary>
	public const double TanqueBase = 50;

	/// <summary>Quanto do tanque um personagem novo ja traz. O DM nasce com ele cheio.</summary>
	public const double TanqueInicial = 50;

	/// <summary>
	/// COM QUE FREQUENCIA O ESTOMAGO TRABALHA, em segundos.
	///
	/// O DM roda `CheckStomach` a cada `30 / Metabolism` ticks, e um tique de BYOND e 0,1 s -- ou
	/// seja tres segundos com metabolismo 1. Manter a cadencia importa: todas as constantes abaixo
	/// sao POR PASSADA, e rodar isto no tique de 30 Hz multiplicaria a digestao por noventa.
	/// </summary>
	public const double SegundosPorPassada = 3;

	/// <summary>
	/// A FOME BATE quando o vigor cai a um quarto -- e o `stamina/maxstamina <= 0.25` do DM, que e
	/// onde o barrigao ronca na tela.
	/// </summary>
	public const double LimiarDeFome = 0.25;

	/// <summary>Acima disto o estomago para de converter: o corpo esta satisfeito.</summary>
	public const double LimiarDeSaciedade = 0.96;

	/// <summary>
	/// O TETO DO TANQUE deste personagem. Metabolismo alto = estomago maior.
	/// `maxNutrition = 50 * Metabolism` (Stomach.dm).
	/// </summary>
	public static double Tanque(double metabolismo) => TanqueBase * Math.Max(metabolismo, 0.1);

	/// <summary>
	/// UMA PASSADA DO ESTOMAGO: queima nutricao e devolve vigor e Ki.
	///
	/// ============================ A ORDEM E A DO ORIGINAL ============================
	/// Primeiro a nutricao DECAI sozinha (digestao custa, mesmo parado), depois vem o pingo de
	/// vigor que nao cobra nada, e so entao a conversao que consome o tanque. Trocar a ordem muda o
	/// saldo: o pingo gratuito antes do desconto e o que faz alguem com o tanque quase vazio ainda
	/// recuperar alguma coisa.
	///
	/// O DECAIMENTO E LOGARITMICO (`log(1.5, nutricao)`), e nao linear: tanque cheio digere mais
	/// rapido que tanque quase vazio. Sem isso, comer muito seria puro lucro -- com isso, o excesso
	/// evapora sozinho e a refeicao grande rende menos por unidade que duas pequenas.
	/// =================================================================================
	/// </summary>
	/// <returns>O quanto de vigor e de Ki esta passada devolveu.</returns>
	public static (double Vigor, double Ki) Digerir(Fighter f, double comidaGlobal = 1)
	{
		// SO DIGERE QUEM PRECISA. O DM exige tanque com algo dentro, vivo, vigor abaixo do maximo e
		// FORA DE COMBATE -- no meio da briga o corpo nao para pra processar comida.
		if (f.dead || f.CurrentNutrition <= 0 || f.stamina >= f.maxstamina || f.IsInFight)
			return (0, 0);

		double metabolismo = Math.Max(f.Metabolism, 0.1);
		double tanque = Tanque(f.Metabolism);

		// 1. a digestao cobra o proprio custo
		f.CurrentNutrition = Math.Max(
			f.CurrentNutrition - Math.Log(Math.Max(f.CurrentNutrition, 1), 1.5) * (tanque / 100) * 0.005, 0);

		// 2. o pingo que nao cobra nada
		double vigor = 0.005 * comidaGlobal * f.maxstamina / metabolismo;

		// 3. a conversao de verdade, que come o tanque
		double custoDeVigor = 0.025 * comidaGlobal * f.maxstamina / 100 / metabolismo;
		if (f.CurrentNutrition >= custoDeVigor) f.CurrentNutrition -= custoDeVigor;
		else
		{
			// O RESTO DO TANQUE DE UMA VEZ. E a saida do DM pra nao deixar migalha presa: com menos
			// que uma dose, gasta-se o que houver e converte-se pelo `satiationMod`.
			vigor += f.CurrentNutrition * f.satiationMod * (f.maxstamina / 100);
			f.CurrentNutrition = 0;
		}

		// 4. e o Ki, que sai do mesmo tanque
		double ki = 0;
		if (f.Ki < f.MaxKi && f.CurrentNutrition > 0)
		{
			double custoDeKi = 0.035 * comidaGlobal / metabolismo;
			if (f.CurrentNutrition >= custoDeKi)
			{
				f.CurrentNutrition -= custoDeKi;
				ki = 0.035 * comidaGlobal * f.MaxKi / 100 / metabolismo;
			}
			else
			{
				ki = f.CurrentNutrition * f.satiationMod * (f.MaxKi / 100);
				f.CurrentNutrition = 0;
			}
		}

		// QUEM SE CONTEM DESCANSA (dono, 2026-10-12): relaxado ou usando so parte do poder, o mesmo tanque de comida
		// devolve mais vigor -- ver `PoderEmUso.FatorDeDescanso`.
		vigor *= PoderEmUso.FatorDeDescanso(f) * f.racialRegenVigor;   // (v0.4.0) e a recuperacao de vigor racial (Prideful, Warrior's Vitality)
		f.stamina = Math.Min(f.stamina + vigor, f.maxstamina);
		// ============================ NAO APARE QUEM JA ESTAVA ACIMA ============================
		// Isto era `Math.Min(f.Ki + ki, f.MaxKi)`, e o `Min` nao limitava so o que a comida
		// ACRESCENTA -- ele derrubava o Ki de quem ja estava acima do teto. Com `ki = 0` a conta
		// virava `Min(140, 100) = 100`, todo tique.
		//
		// Era o que desfazia a tecla C: o dono comprimia o Ki ate 140% e a NUTRICAO devolvia pra
		// 100 no quadro seguinte. O motor da carga estava certo o tempo todo (simulado 25 s, ele
		// segura 140 sem oscilar) -- quem aparava era outro sistema, que nem sabia da sobrecarga.
		//
		// A regra certa: a comida enche ATE o teto, e nunca TIRA. Quem passou dos 100% pela tecla
		// C so desce pelo caminho dela (o dreno, o tempo, o preco do excesso).
		// ================================================================================
		f.Ki = Math.Min(f.Ki + ki, Math.Max(f.MaxKi, f.Ki));
		return (vigor, ki);
	}

	/// <summary>
	/// QUEM TEM PISO DE MEIO TANQUE: ninguem, desde a v0.3.0. O `StaminaDrain.dm:19` fala dos dois, Majin e Bio-Androide -- e
	/// o dono tirou os dois em 2026-10-15: o Majin TRAVA NO CHEIO (<see cref="VigorSempreCheio"/>), e o bio *"sozinho N tem
	/// nada"*: o vigor dele e o das racas doadoras (`Races.SangueDoBio`). O piso so volta como o jogo de antes, nas bancadas.
	/// </summary>
	public static bool TemPisoDeVigor(string raca) =>
		(raca == "Majin" && VigorDoMajinComPisoDeTeste) || (raca == Races.BioAndroids.Raca && Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste);

	// =====================================================================
	// O VIGOR (E O KI) QUE NAO ACABAM -- dono, 2026-10-15
	// =====================================================================
	// *"se majin tem vigor infinito, n precisa fazer o vigor deles cair pra 50% e dps travar, trava logo no 100%, dieferenca
	// deles pros androides de energia infinita e q os androides tem vigor e ki infinitos (ou seja nunca caem pra baixo de
	// 100%)"*. DESVIO DECLARADO DO ORIGINAL: no DM o Majin cai a meio tanque e para la (`StaminaDrain.dm:19-20`), e o
	// androide perpetuo enche 2% do Ki a cada meio segundo (`DNALabs.dm:201-210`).

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): o vigor do Majin volta a cair ate meio tanque e travar la -- o jogo de
	/// antes. Lido por <see cref="TemPisoDeVigor"/> e <see cref="VigorSempreCheio"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool VigorDoMajinComPisoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): o Ki do androide de energia infinita volta a nao ter piso -- o jogo de
	/// antes. Lido por <see cref="KiSempreCheio"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool KiDoAndroideSemPisoDeTeste;

	/// <summary>
	/// O vigor deste corpo nunca sai do cheio: o do Majin (vivo ou fantasma) e o do androide de energia infinita vivo -- e, desde
	/// a v0.3.0, o do bio com o sangue de um deles (`Races.SangueDoBio`: *"o gasto de vigor/ki das racas q foram usadas pra
	/// criar ele"*) e o de TODO MORTO (*"mortos tem vigor infinito"* -- ver <see cref="MortoComMeioTanqueDeTeste"/>).
	/// </summary>
	public static bool VigorSempreCheio(Fighter f) =>
		VigorDeMajin(f) || (EnergiaInfinita(f) && !f.dead) || (f.dead && !MortoComMeioTanqueDeTeste);

	/// <summary>
	/// O VIGOR QUE NUNCA ACABA PELA RACA, e nao pelo estado: o do Majin (vivo ou fantasma), o do androide de energia infinita e o do bio com o
	/// sangue de um deles -- o <see cref="VigorSempreCheio"/> sem o MORTO comum. E a quem a mesa de tecnicas nao paga ponto pelo folego
	/// (`Skills.TecnicaCustomizada.VendeFolego`); o dono, 2026-10-19: *"só as raças com vigor infinito perdem a opção de fôlego"* -- o morto
	/// tem o vigor travado no cheio, mas revive, e a tecnica dele volta a cobrar. O androide conta MESMO MORTO: o motor nao para com a morte
	/// (<see cref="KiSempreCheio"/>), e morto o vigor trava no cheio do mesmo jeito -- vivo ou morto, ele nunca paga folego.
	/// </summary>
	public static bool VigorInfinitoDaRaca(Fighter f) => VigorDeMajin(f) || EnergiaInfinita(f) || (f.dead && MortoComoRacaDeTeste);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--tecnicateste`): o morto volta a contar como raca de vigor infinito -- a primeira versao da regra da mesa
	/// (2026-10-19, antes de o dono escolher so as racas). Lido por <see cref="VigorInfinitoDaRaca"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MortoComoRacaDeTeste;

	/// <summary>
	/// ============================ O MORTO TEM VIGOR INFINITO (dono, 2026-10-15) ============================
	/// *"Mortos atualmente o vigor fica preso em 50% seria melhor deixar preso em 100% logo, mortos tem vigor infinito"*.
	/// DESVIO DECLARADO DO ORIGINAL: no DM o morto fica cravado em meio tanque (`else if(dead) stamina = 0.5*maxstamina`,
	/// `StaminaDrain.dm:35-36`); aqui o cadeado do tique do corpo o segura no cheio (<see cref="VigorSempreCheio"/>), e o
	/// estomago nao o queima (<see cref="QueimarVigor"/>). O Ki do morto continua o de sempre -- menos o do androide de energia
	/// infinita, que segue com o motor (<see cref="KiSempreCheio"/>, 2026-10-20).
	///
	/// DEFEITO INJETADO (bancadas `--v030teste` e `--alemteste`): o morto volta a ficar em meio tanque -- o jogo de antes. Falso
	/// em jogo, sempre.
	/// ====================================================================================================
	/// </summary>
	public static bool MortoComMeioTanqueDeTeste;

	/// <summary>
	/// O Ki deste corpo nunca fica abaixo de 100%: o do androide de energia infinita (e o do bio que tem o motor dele no sangue), vivo
	/// OU MORTO, e, desde a v0.3.0, o do CHEFE DE SAGA (`Fighter.KiDeChefe`: *"faça boss de saga de ki infinito pra deixar mais
	/// desafiador"*). ACIMA de 100% ele carrega, escorre e para no teto de carga como o de qualquer um -- o cadeado so segura o
	/// piso.
	///
	/// O MOTOR NAO PARA COM A MORTE (dono, 2026-10-20: *"eles mortos ficam sem energia infinita"* -- era um bug pra ele). Aqui morava um
	/// `!f.dead` -- o laco do DM (`dnl_start_infinite`, `DNALabs.dm:201-210`) morre com o corpo --, e o morto ficava com o vigor cheio pela
	/// regra de todo morto e o Ki drenando como o de qualquer um. O bio com o motor no sangue vai junto (a mesma escolha do dono).
	/// </summary>
	public static bool KiSempreCheio(Fighter f) =>
		(EnergiaInfinita(f) && !(f.dead && MotorParaNaMorteDeTeste) && !KiDoAndroideSemPisoDeTeste) || (f.KiDeChefe && !KiDoChefeFinitoDeTeste);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): o motor do androide de energia infinita volta a parar com a morte -- o Ki do morto
	/// solto, o jogo de antes de 2026-10-20. Lido pela propria linha do <see cref="KiSempreCheio"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MotorParaNaMorteDeTeste;

	/// <summary>DEFEITO INJETADO (`--v030teste`): o Ki do chefe de saga volta a acabar -- o jogo de antes. Falso em jogo, sempre.</summary>
	public static bool KiDoChefeFinitoDeTeste;

	/// <summary>O vigor do Majin, e o do bio com o sangue dele -- vivo ou fantasma. Ver <see cref="VigorDoMajinComPisoDeTeste"/>.</summary>
	private static bool VigorDeMajin(Fighter f) =>
		(f.Race == "Majin" || Races.SangueDoBio.Carrega(f, "Majin")) && !VigorDoMajinComPisoDeTeste;

	/// <summary>O motor de energia infinita: o androide que o tem, e o bio que o herdou (`Races.SangueDoBio.InfinitoNoSangue`).</summary>
	private static bool EnergiaInfinita(Fighter f) => f.AndroideInfinito || Races.SangueDoBio.InfinitoNoSangue(f);

	/// <summary>
	/// O CADEADO: poe o vigor (e o Ki) de volta onde a regra manda. Chamado POR ULTIMO no tique do corpo
	/// (`GameServer.TickDosRelogiosDoCorpo`), depois de todo relogio que gasta -- o que eles cobraram volta no mesmo tique.
	/// </summary>
	public static void ManterOsInfinitos(Fighter f)
	{
		if (VigorSempreCheio(f)) f.stamina = f.maxstamina;
		if (KiSempreCheio(f) && f.Ki < f.MaxKi) f.Ki = f.MaxKi;
	}

	/// <summary>
	/// O VIGOR QUEIMA COM O TEMPO -- o dreno passivo de `CheckNutrition`.
	///
	/// Existir custa folego, e lutar custa mais. Vontade (`Ewillpower`) e o stat de aguentar: ele
	/// divide o dreno, entao quem tem cabeca-dura cansa devagar.
	/// </summary>
	public static void QueimarVigor(Fighter f, double dreno = 1)
	{
		// (v0.3.0) O MORTO NAO QUEIMA: o vigor dele trava no cheio no tique do corpo -- ver `MortoComMeioTanqueDeTeste`, que
		// devolve o meio tanque do DM.
		if (f.dead) { if (MortoComMeioTanqueDeTeste) f.stamina = 0.5 * f.maxstamina; return; }

		// ============================ O PISO DE MEIO TANQUE DO BIO-ANDROIDE (e do Majin, ate 2026-10-15) ============================
		// `if(stamina<(maxstamina/2)&&(Race=="Majin"||Race=="Bio-Android")) stamina=(maxstamina/2)` -- a segunda
		// linha do `CheckNutrition()` (`Stamina/StaminaDrain.dm:19-20`), e ela faltava aqui.
		//
		// ERA O "VIGOR DO MAJIN QUE AS VEZES NAO REGENERA" do playtest de 2026-10-10. O CUSTO de refazer o
		// corpo foi portado (`Regeneracao.Tique`: 0,67% do tanque por segundo enquanto houver UM membro abaixo
		// do cheio -- quatro vezes o que o estomago repoe), e o piso que o original poe embaixo dele nao. Sem
		// ele o vigor ia a zero e la ficava: com zero a regeneracao para (`TemEnergia`), o corpo nunca fica
		// inteiro, e o estomago -- que nao digere em luta nem de tanque vazio -- nao o tira de la. O cerebro e
		// os orgaos, que so saram pelo canal lento, esticavam isso por dezenas de minutos.
		//
		// A CADA PASSADA DO ESTOMAGO (a cadencia do `CheckNutrition`), e ANTES do dreno, como la.
		// ====================================================================================================
		if (TemPisoDeVigor(f.Race) && f.stamina < f.maxstamina / 2)
			f.stamina = f.maxstamina / 2;

		// QUEM ESCONDE O PODER CANSA MAIS DEVAGAR PARADO: `max(200*Ewillpower*concealedBuff*Estaminamod,0.01)`
		// (`StaminaDrain.dm:30`), com o `concealedBuff` em 1,5 enquanto o sigilo esta ligado (`master.dm:136-141`).
		// O `Statify` ja calculava o campo e NINGUEM O LIA -- o mesmo defeito do `KIregen` e do `splitformdeBuff`.
		// So o primeiro termo o leva, como la: a parcela de luta, logo abaixo, nao.
		double folgaDoSigilo = PoderEmUso.SigiloNaoPoupaVigorDeTeste ? 1 : f.concealedBuff;
		double resistencia = Math.Max(200 * f.Ewillpower * folgaDoSigilo * f.Estaminamod, 0.01);
		double gasto = 1.5 * f.dashingMod * dreno * (Math.Max(f.Metabolism, 0.1) / 2) / resistencia;

		// LUTAR CANSA MAIS, e por um caminho proprio: a conta do DM soma um segundo termo em vez de
		// multiplicar o primeiro, entao o combate pesa igual pra quem tem vontade alta e pra quem
		// nao tem. E o que impede o personagem de vontade alta lutar de graca.
		if (f.IsInFight)
			gasto += 2 * dreno * f.dashingMod / Math.Max(150 * f.Ewillpower * f.Estaminamod, 0.01);

		f.stamina = Math.Max(f.stamina - gasto, 0);
	}

	/// <summary>O que uma refeicao deu: se foi comida, e o aviso que o jogador le (vazio se nao ha o que dizer).</summary>
	public readonly record struct Refeicao(bool Comeu, string Aviso);

	public const string AvisoDeCheio = "você já está cheio: não cabe mais nada.";
	public const string AvisoDeEncheu = "você come até ficar cheio.";

	/// <summary>
	/// COMER. O tanque e o TETO. O dono (2026-09-05): *"caso o jogador tente comer com nutrição em 100%
	/// o jogo vai falar que ele já está cheio, e caso ele coma algo que faria ele passar de 100% (estar
	/// com 90% e comer algo que dá 20%) ele vai atingir o 100% e receber uma mensagem que ele comeu até
	/// ficar cheio"*.
	///
	/// ============================ O QUE MUDOU EM RELACAO AO DM, DECLARADO ============================
	/// O DM deixava o valor passar do teto por um tique e o `CheckStomach()` (`Stomach.dm:5-7`) o cravava
	/// de volta em `maxNutrition` com o aviso de "you ate too much... and you throw up a little". A versao
	/// anterior deste metodo nem cravava (chamava isso de "punicao por gula") -- e o dono viu a nutricao
	/// acima de 100% na tela. Agora: CHEIO NAO COME, e a comida fica (quem chama nao a consome); o que
	/// passaria do teto entra so ate ele, com o aviso de que encheu. Quem chega ja ACIMA do teto (save
	/// antigo) nao e cortado aqui -- o `Digerir` desce sozinho.
	///
	/// `mesmoCheio` e a Semente Senzu: ela e cura antes de ser comida, e estar cheio nao pode impedir a
	/// cura -- so nao enche alem do teto.
	/// ================================================================================================
	/// </summary>
	public static Refeicao Comer(Fighter f, double nutricao, bool mesmoCheio = false)
	{
		double tanque = Tanque(f.Metabolism);
		double antes = f.CurrentNutrition;
		if (antes >= tanque)
		{
			if (!mesmoCheio) return new Refeicao(false, AvisoDeCheio);
			f.Hungry = false;
			return new Refeicao(true, AvisoDeCheio);
		}

		f.CurrentNutrition = Math.Min(antes + nutricao, tanque);
		f.Hungry = false;
		return new Refeicao(true, antes + nutricao > tanque ? AvisoDeEncheu : "");
	}

	/// <summary>
	/// O CORPO PEDE COMIDA? Duas portas, como no DM: vigor no chao, ou tanque vazio com vigor ja
	/// abaixo de 60%. A segunda existe pra o aviso chegar ANTES de o personagem travar.
	/// </summary>
	public static bool TemFome(Fighter f) =>
		f.stamina / Math.Max(f.maxstamina, 1) <= LimiarDeFome
		|| (f.CurrentNutrition <= 0 && f.stamina < f.maxstamina * 0.6);

	/// <summary>
	/// PASSAR FOME MACHUCA. Com o vigor no zero E o tanque vazio, o corpo comeca a se consumir --
	/// e o `SpreadDamage` do DM. Devolve quanto dano espalhar, ou zero.
	///
	/// COM TANQUE, NAO MACHUCA: o DM segura o vigor em 1 enquanto houver comida no estomago. E a
	/// diferenca entre "estou exausto" e "estou morrendo de fome".
	/// </summary>
	public static double DanoDaFome(Fighter f, ref double acumulado)
	{
		if (f.dead || f.stamina >= 1) return 0;

		if (f.CurrentNutrition > 0) { f.stamina = 1; return 0; }

		double dano = Math.Abs(f.stamina) * acumulado;
		acumulado = Math.Min(acumulado + f.stamina, 10);
		f.stamina = 0;
		return dano;
	}
}
