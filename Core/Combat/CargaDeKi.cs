using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>Em que ponto da carga o corpo esta AGORA. Decide aura, som e o que dizer no chat.</summary>
public enum EstagioDaCarga
{
	/// <summary>Nao esta carregando -- ou nao tem como (sem Ki Unlocked).</summary>
	Nada,

	/// <summary>Enchendo o tanque ate 100%. E o unico estagio que existe sem `canPower`.</summary>
	Enchendo,

	/// <summary>Retomando o proprio poder: `powerMod` voltando a 1 depois de ter sido baixado.</summary>
	Retomando,

	/// <summary>PASSANDO DOS 100%. E aqui que nasce o buff de BP -- e o risco.</summary>
	Ultrapassando,

	/// <summary>Sem folego: `stamina` no chao. Carregar custa esforco.</summary>
	SemFolego,

	/// <summary>
	/// ANDANDO. Nao rende nada -- pra reunir energia e preciso plantar o pe.
	///
	/// DESVIO DECLARADO DO ORIGINAL: no DM da pra carregar andando, so que mais devagar (o
	/// `powermovetimer` corta a taxa). O dono jogou e cortou: "eu consigo correr e carregar o ki,
	/// eu deveria ter q ficar parado pra n ficar op d+". Ele esta certo em termos de jogo --
	/// correr JA custa Ki, e carregar correndo transformava a fuga numa forma de LUCRAR energia.
	/// </summary>
	Andando,
}

/// <summary>
/// REUNIR ENERGIA -- o `Energy_Draw()` do original (`Modules/Stats/BP/Power Control.dm:141-180`),
/// que e o que a tecla C faz enquanto fica segurada.
///
/// ============================ AS DUAS CHAVES ============================
/// O dono resumiu o sistema em uma frase: "segurar C pra quem tem o ki unlocked permite recarregar
/// o ki, e quem tem um controle de ki bom ao apertar C ativa a aura e pode ultrapassar o 100% de
/// ki pra ganhar um buff no bp". Sao duas permissoes distintas, e o DM as separa mesmo:
///
///   <c>MeditateGivesKiRegen</c>  Ki Unlocked. Sem ela a tecla e MUDA -- nao ha mensagem de erro
///                                no original, simplesmente nada acontece.
///   <c>canPower</c>              Basic Ki Control nivel 5. E o "controle bom": so ela abre os
///                                estagios 2 e 3, e so ela deixa o Ki passar de MaxKi.
/// ========================================================================
///
/// OS TRES ESTAGIOS SAO SEQUENCIAIS, e a ordem e a do `if/else if/else if` do DM -- nao da pra
/// pular. Encher o tanque vem primeiro; so com o tanque cheio o corpo volta a expressar o poder
/// todo (`powerMod`); e so com as duas coisas resolvidas ele comeca a comprimir energia alem da
/// conta. E por isso que o power-up do anime demora: sao tres coisas em fila, nao uma.
///
/// DE ONDE VEM O BUFF DE BP. De lugar nenhum novo -- ele ja estava montado. `Fighter.PowerLevel`
/// faz `kiratio = Ki/MaxKi` e multiplica o BP expresso por `statusBuff`, que carrega o kiratio
/// dentro. Passar de 100% de Ki E o buff. A unica peca que faltava era alguem que empurrasse o Ki
/// pra cima de MaxKi, e e esta classe.
///
/// ============================ 10 Hz, E POR QUE ============================
/// O DM soma por CHAMADA da verb, e a verb repete enquanto a tecla esta em baixo, na cadencia do
/// `world.tick_lag` (0,1 s por padrao) -- ou seja ~10 chamadas por segundo. Aqui o tick e 30 Hz e
/// vem com `dt`, entao cada valor do original foi multiplicado por 10 e virou TAXA POR SEGUNDO.
///
/// Escrever as taxas ja convertidas (e nao "o numero do DM dividido por alguma coisa em runtime")
/// e o que deixa conferir: `MaxKi/9` por segundo enche o tanque em 9 s com `canPower` e em 15 s
/// sem -- da pra cronometrar em jogo e comparar com o original sem abrir codigo.
/// ==========================================================================
/// </summary>
public static class CargaDeKi
{
	/// <summary>Chamadas por segundo do original. E o fator que converte "por chamada" em "por segundo".</summary>
	private const double PorSegundo = 10.0;

	// --- ESTAGIO 1: encher o tanque -----------------------------------------
	/// <summary>`Ki += MaxKi/90` com canPower. Tanque cheio em 9 s.</summary>
	private const double EncheComControle = PorSegundo / 90.0;

	/// <summary>`Ki += MaxKi/150` sem canPower. 15 s -- quem so destravou o ki junta devagar.</summary>
	private const double EncheSemControle = PorSegundo / 150.0;

	private const double FolegoEnchendoComControle = PorSegundo / 610.0;
	private const double FolegoEnchendoSemControle = PorSegundo / 500.0;

	// --- ESTAGIO 2: retomar o proprio poder ---------------------------------
	private const double RetomaParado = PorSegundo * 0.01;
	private const double RetomaAndando = PorSegundo * 0.0025;

	// --- ESTAGIO 3: passar dos 100% -----------------------------------------
	private const double UltrapassaParado = PorSegundo / 100.0;
	private const double UltrapassaAndando = PorSegundo / 200.0;
	private const double FolegoUltrapassando = PorSegundo / 590.0;

	/// <summary>Teto do `extracharge` no DM.</summary>
	private const double TetoDaCarga = 50;

	/// <summary>
	/// Segurar C exige folego. O DM checa `stamina > 1` antes de sequer acender a aura
	/// (`Meditate.dm:180`), e o proprio dreno de folego de cada estagio faz esse piso ser
	/// alcancavel -- carregar sem parar esvazia e para sozinho.
	/// </summary>
	private const double FolegoMinimo = 1;

	/// <summary>Pode sequer TENTAR reunir energia (a chave do Ki Unlocked).</summary>
	public static bool SabeReunir(Fighter f) => f.MeditateGivesKiRegen != 0;

	/// <summary>Tem controle bom o bastante pra power-up de verdade (a chave do Ki Control 5).</summary>
	public static bool TemControle(Fighter f) => f.canPower != 0;

	/// <summary>
	/// Ate onde o Ki pode ir carregando, em unidades absolutas. `powerupcap` e RAZAO sobre o MaxKi
	/// (o Statify o calcula a partir das skills de power-up, minimo 1,4).
	/// </summary>
	public static double TetoDeCarga(Fighter f) => f.MaxKi * Math.Max(f.powerupcap, 1);

	/// <summary>
	/// UM PASSO DE CARGA. Mexe no lutador e devolve em que estagio ele ficou.
	///
	/// <paramref name="mexendo"/> e o `powermovetimer` do DM: carregar andando funciona, mas rende
	/// bem menos (um quarto na retomada, metade na ultrapassagem). E o que obriga a plantar o pe
	/// pra subir de poder, que e a imagem do anime inteiro.
	/// </summary>
	public static EstagioDaCarga Passo(Fighter f, double dt, bool mexendo)
	{
		if (dt <= 0 || !SabeReunir(f)) return EstagioDaCarga.Nada;
		if (f.KO || f.dead) return EstagioDaCarga.Nada;
		if (f.stamina <= FolegoMinimo) return EstagioDaCarga.SemFolego;

		// PE PLANTADO. Nao ha ganho nenhum em movimento -- ver EstagioDaCarga.Andando.
		if (mexendo) return EstagioDaCarga.Andando;

		// SEM CONTROLE: so o primeiro estagio existe, e o teto e o proprio MaxKi. Nada de aura,
		// nada de ultrapassar. E "recarregar o ki" e mais nada -- exatamente o que o dono disse.
		if (!TemControle(f))
		{
			if (f.Ki >= f.MaxKi) return EstagioDaCarga.Nada;
			f.Ki = Math.Min(f.Ki + f.MaxKi * EncheSemControle * dt, f.MaxKi);
			f.stamina -= f.maxstamina * FolegoEnchendoSemControle * dt;
			f.extracharge = Math.Min(f.extracharge + 0.5 * PorSegundo * dt, TetoDaCarga);
			return EstagioDaCarga.Enchendo;
		}

		// --- 1) o tanque -----------------------------------------------------
		if (f.Ki < f.MaxKi)
		{
			f.Ki = Math.Min(f.Ki + f.MaxKi * EncheComControle * dt, f.MaxKi);
			f.stamina -= f.maxstamina * FolegoEnchendoComControle * dt;
			f.extracharge = Math.Min(f.extracharge + PorSegundo * dt, TetoDaCarga);
			return EstagioDaCarga.Enchendo;
		}

		// --- 2) o proprio poder de volta -------------------------------------
		if (f.powerMod < 1)
		{
			double taxa = mexendo ? RetomaAndando : RetomaParado;
			double divisor = mexendo ? 10 : 15;
			f.powerMod = Math.Min(1, f.powerMod + taxa * Habilidade(f, divisor) * dt);
			return EstagioDaCarga.Retomando;
		}

		// --- 3) alem dos 100% ------------------------------------------------
		double teto = TetoDeCarga(f);
		if (f.Ki >= teto) return EstagioDaCarga.Ultrapassando;   // ja no maximo: segura, nao sobe

		// O GANHO ENCOLHE conforme se sobe (`/(3*kiratio)`), e e o que impede o power-up de virar
		// uma rampa infinita: quanto mais comprimido o ki, mais caro cada gota.
		double razao = Math.Max(f.Ki / Math.Max(f.MaxKi, 1e-9), 1);
		double baseTaxa = mexendo ? UltrapassaAndando : UltrapassaParado;
		double divisorH = mexendo ? 10 : 10;
		double denominador = mexendo ? 3 : 2.5;

		double ganho = f.MaxKi * baseTaxa * (Habilidade(f, divisorH) / denominador / (3 * razao)) * dt;
		f.Ki = Math.Min(f.Ki + ganho, teto);
		f.stamina -= f.maxstamina * FolegoUltrapassando * dt;
		f.extracharge = Math.Min(f.extracharge + PorSegundo * dt, TetoDaCarga);
		return EstagioDaCarga.Ultrapassando;
	}

	/// <summary>
	/// `Ekiskill + (kicontrolskill + kigatheringskill)/N` -- a habilidade que multiplica os
	/// estagios 2 e 3. O divisor muda de estagio pra estagio no DM (15, 10, 10), entao ele entra
	/// como parametro em vez de virar tres copias da mesma conta.
	/// </summary>
	private static double Habilidade(Fighter f, double divisor)
		=> f.Ekiskill + (f.kicontrolskill + f.kigatheringskill) / Math.Max(divisor, 1e-9);

	// =====================================================================
	// O PRECO DE FICAR LA EM CIMA
	// =====================================================================
	/// <summary>
	/// O `CheckPowerMod()` do DM (`Power Control.dm:113-134`): passar dos 100% VAZA energia, e
	/// passar do teto seguro (`kicapacity`) machuca.
	///
	/// SEM MOEDA. O original faz `prob(25)` a cada chamada -- um sorteio a 10 Hz que rende 2,5
	/// eventos por segundo. Aqui a mesma coisa entra como taxa continua (o valor vezes 2,5 por
	/// segundo). O resultado medio e identico e o comportamento fica DETERMINISTICO, o que importa
	/// porque isto roda no servidor pra todo mundo em todo tick: com sorteio, dois jogadores no
	/// mesmo estado se machucariam de formas diferentes sem motivo, e nao haveria como reproduzir
	/// um relato de "tomei dano do nada".
	///
	/// Devolve quanto dano espalhar pelos membros (0 = nada).
	/// </summary>
	public static double PrecoDoExcesso(Fighter f, double dt)
	{
		if (f.Ki <= f.MaxKi)
		{
			f.overcharge = false;
			return 0;
		}

		const double Eventos = 2.5;   // o prob(25) a 10 Hz
		double razao = f.Ki / Math.Max(f.MaxKi, 1e-9);
		double pericia = Math.Max(f.Ekiskill, 0.1);

		// ============================ AS DUAS SAO UNIDADES DIFERENTES ============================
		// `kicapacity` e ABSOLUTA e `powerupcap` e RAZAO. Parecem iguais (as duas nascem por volta
		// de 1,3 no `base.dm`) e nao sao: o `Statify` reescreve a primeira como
		// `1,3 * MaxKi * log(...)` (`master.dm:182`) e deixa a segunda em torno de 1,4 puro.
		//
		// EU JA ERREI ISTO AQUI, e escrevi um comentario dizendo que o DM e que estava errado por
		// comparar `Ki > kicapacity`. Nao esta -- a comparacao dele e absoluto com absoluto, e a
		// minha "correcao" pra razao dava um teto de 236 contra uma razao de 1,4: dano NUNCA saia,
		// e a bancada mostrou isso na hora (`dano 0,00` a 150% de Ki).
		//
		// O QUE ESSA FAIXA SIGNIFICA: com os dois valores de fabrica o teto seguro fica em 130% do
		// MaxKi e a carga alcanca 140%. Ou seja os ultimos 10% de power-up DOEM -- e a mordida que
		// faz "passar do limite" ser uma escolha e nao so um numero maior.
		// ======================================================================================
		double teto = Math.Max(f.kicapacity, 1e-9);

		double dano;
		if (f.Ki > teto * 1.1 && !f.overcharge)
		{
			f.Ki -= f.Ki * 0.04 * Eventos * dt / pericia;
			f.stamina -= 0.0025 * (f.maxstamina / 100) * Eventos * dt / pericia;
			dano = 0.05 * razao / pericia;
		}
		else if (f.Ki > teto && !f.overcharge)
		{
			f.Ki -= f.Ki * 0.005 * Eventos * dt / pericia;
			f.stamina -= 0.0012 * (f.maxstamina / 100) * Eventos * dt;
			// `kicapacity/MaxKi` e o teto EM RAZAO -- e o unico ponto em que o DM converte uma na
			// outra, e por isso a divisao esta escrita assim e nao simplificada.
			dano = 0.01 * (razao / (teto / Math.Max(f.MaxKi, 1e-9)));
		}
		else
		{
			// acima de 100% mas dentro do seguro: so vaza, devagar, e nao machuca
			if (f.Ki < teto) f.overcharge = false;
			f.Ki -= f.Ki * 0.0001 * Eventos * dt / pericia;
			if (f.dead) f.Ki -= f.Ki * 0.0001 * Eventos * dt / pericia;
			return 0;
		}

		return dano * Eventos * dt;
	}

	/// <summary>
	/// A AURA ACENDE em `kiratio > 1,1` e apaga em `kiratio <= 1` (`AuraCheck`, Power Control.dm:22-37).
	///
	/// A HISTERESE E DE PROPOSITO -- acender e apagar no MESMO limiar faria a aura piscar em todo
	/// tick pra quem estivesse parado exatamente nele. A faixa morta entre 1,0 e 1,1 e o que
	/// mantem o efeito estavel.
	/// </summary>
	public static bool AuraAcesa(Fighter f, bool acesaAgora)
	{
		double razao = f.Ki / Math.Max(f.MaxKi, 1e-9);
		if (razao > 1.1) return true;
		if (razao <= 1.0) return false;
		return acesaAgora;
	}
}
