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

	// =====================================================================
	// O TETO DO JOGO: 200% (dono, 2026-10-13)
	// =====================================================================
	/// <summary>O teto de quem acabou de ganhar o controle (Basic Ki Control 5): 140% do tanque.</summary>
	public const double TetoDeFabrica = 1.4;

	/// <summary>
	/// **O TETO DO JOGO**: ninguem passa de 200% de Ki, por caminho nenhum -- *"o teto limite do jogo q vc pode ter
	/// de ki seria 200%"*. E onde chega a carga de quem tem as duas pericias no maximo (<see cref="TetoPelaPericia"/>),
	/// e e o mais alto que o corte duro de um corpo pode ser (<see cref="TetoDuro"/>).
	/// </summary>
	public const double TetoDoJogo = 2.0;

	/// <summary>
	/// **O TETO E DURO** (dono, 2026-10-13, na vespera do lancamento): *"ta dando pra passar o ki maximo (deveria ser um
	/// hard cap)"* e *"simplesmente so carrego meu ki com C e ele passa do teto do meu personagem q é 140%"*. O teto de
	/// carga do corpo (<see cref="TetoDeCarga"/>, o numero que a aba Ki mostra) e o MAXIMO de Ki dele, venha o Ki de
	/// onde vier -- a tecla C, o troco da carga, um presente, um tiro engolido --, e nunca alem do
	/// <see cref="TetoDoJogo"/>. E o corte que o servidor aplica em todo tique (`GameServer.TickDaCarga`).
	///
	/// QUEM FURAVA: o TROCO DA CARGA (`extracharge`, `Stats.RegenDeKi.Passo`), e so pra quem SOLTA o C e volta a
	/// segurar. Carregando sem parar o troco fica cheio (50) e nao paga nada -- a parabola dele vale zero na ponta.
	/// Solto o C (ou andando, ou sem folego), ele comeca a voltar como Ki; e quem aperta o C de novo com o Ki ja no teto
	/// cai no ramo "ja no maximo" do <see cref="Passo"/>, que nao escorre nem reenche o troco: o resto dele -- ate 208
	/// vezes o `KIregen` -- entrava ACIMA do teto e ficava la. MEDIDO: com a regeneracao em 0,09% do tanque por chamada,
	/// soltar cinco segundos no teto e segurar de novo levava o Ki de 140% a 158,4% (a tela do dono mostrava 158,7% com
	/// "Teto de carga 140% do tanque"); com o dobro dela, a 170%. (2026-10-16) O TROCO SAIU INTEIRO -- ver `Stats.RegenDeKi` --, e o
	/// teto continua duro pros outros caminhos: um presente, uma tecnica, um tiro engolido.
	/// </summary>
	public static double TetoDuro(Fighter f) => f.MaxKi * Math.Min(Math.Max(f.powerupcap, 1), TetoDoJogo * Math.Max(f.racialTetoDaCarga, 1));

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o teto de carga volta a valer so pra tecla C -- o troco da carga (e
	/// qualquer outro caminho) passa dele, ate os 200% do jogo. Lido pelo `GameServer.TickDaCarga`. Falso em jogo, sempre.
	/// </summary>
	public static bool TetoDeCargaNaoEDuroDeTeste;

	/// <summary>
	/// O nivel das duas pericias (circulacao e controle de ki) em que a carga chega ao teto do jogo: o fim das tres
	/// escadas de cada uma (Basic, Advanced e Perfect ate o nivel 100 -- uns cem pontos de pericia).
	/// </summary>
	public const double PericiaDoTeto = 100;

	/// <summary>Abaixo disto a pericia ainda nao rende teto nenhum -- o `max(pericia/10, 1)` da conta do DM (`master.dm:183`).</summary>
	public const double PericiaQueComecaARender = 10;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o teto da carga volta a sair da conta do DM (`1.4 * log7(...)`,
	/// que passava de 200% com pericia mediana e ia a 500% no talo) e o corte dos 200% deixa de existir -- a v0.2.0.
	/// Lido pelo `Fighter.Statify` e pelo `GameServer.TickDaCarga`. Falso em jogo, sempre.
	/// </summary>
	public static bool TetoPelaFormulaDoDmDeTeste;

	/// <summary>
	/// ATE ONDE A CARGA DESTE CORPO CHEGA, em razao sobre o tanque: de 1,4 (de fabrica) a 2,0 (o teto do jogo), em
	/// reta pela MEDIA GEOMETRICA das duas pericias -- as duas contam, como na conta do DM, e so as duas no maximo
	/// chegam aos 200%.
	/// </summary>
	public static double TetoPelaPericia(double circulacao, double controle)
	{
		double media = Math.Sqrt(Math.Max(circulacao, 0) * Math.Max(controle, 0));
		double progresso = Math.Clamp((media - PericiaQueComecaARender) / (PericiaDoTeto - PericiaQueComecaARender), 0, 1);
		return TetoDeFabrica + (TetoDoJogo - TetoDeFabrica) * progresso;
	}

	/// <summary>
	/// O MESMO TETO, mas EM RAZAO sobre o tanque -- 1,4 de fabrica, 2,0 com as duas pericias de ki no
	/// maximo (ate 2026-10-13 a conta do DM o levava a mais de 3,8).
	///
	/// Existe pra a barra de Ki do HUD: ela desenha razao, e um trilho que acaba em 1,0 e o que
	/// fazia o excedente sumir da tela. O DM ja expunha este numero ao jogador com esta cara
	/// (`Statistics.dm:349`: `Ki Capacity: [(kicapacity/MaxKi)*100]%`), entao mostra-lo nao e
	/// invencao do port.
	/// </summary>
	public static double TetoEmRazao(Fighter f) => Math.Max(f.powerupcap, 1);

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
		// `f.dead` SAIU DAQUI (2026-09-04), como no `Regeneracao.Tique`: quem sabe se um morto e cadaver
		// ou fantasma de pe e o servidor (`EhCadaver`, pela zona). O nocaute continua sendo do corpo.
		if (f.KO) return EstagioDaCarga.Nada;

		// ============================ O FROST MUTANTE COM O KI SOLTO NAO REUNE NADA ============================
		// `Power Control.dm:141`: *"MUTANT FROST com ki descontrolado: nao carrega energia nem sobe
		// power-up (tecla C morta ate recuar pra forma estavel)"*. E a punicao inteira do motor do
		// Mutante -- sem ela, ficar numa forma que ele nao segura custaria so o vazamento de BP, e
		// vazamento de BP se compensa enchendo o tanque, que e o que a tecla C faz.
		//
		// AQUI E NAO NO SERVIDOR: e neste arquivo que o DM poe o `if`, e e daqui que saem os TRES
		// estagios da carga. Escrito la em cima, no `TickDaCarga`, ele valeria pro jogador segurando
		// C e nao pra mais nenhum caminho que chame `Passo` -- e a IA chama.
		// =================================================================================================
		if (f.fd_ki_locked) return EstagioDaCarga.Nada;
		if (f.stamina <= FolegoMinimo) return EstagioDaCarga.SemFolego;

		// PE PLANTADO. Nao ha ganho nenhum em movimento -- ver EstagioDaCarga.Andando.
		if (mexendo) return EstagioDaCarga.Andando;

		// SEM CONTROLE: so o primeiro estagio existe, e o teto e o proprio MaxKi. Nada de aura,
		// nada de ultrapassar. E "recarregar o ki" e mais nada -- exatamente o que o dono disse.
		if (!TemControle(f))
		{
			if (f.Ki >= f.MaxKi) return EstagioDaCarga.Nada;
			f.Ki = Math.Min(f.Ki + f.MaxKi * EncheSemControle * f.racialRegenKiAtiva * dt, f.MaxKi);   // (v0.4.0) a regen ATIVA
			f.stamina -= f.maxstamina * FolegoEnchendoSemControle * dt;
			return EstagioDaCarga.Enchendo;
		}

		// --- 1) o tanque -----------------------------------------------------
		// ============================ O TANQUE NAO PODE SER UMA PAREDE ============================
		// Aqui havia `Ki = min(Ki + ..., MaxKi)` seguido de `return`. Parece inofensivo -- e o DM nem
		// clampa (`Power Control.dm:151` faz `Ki += (MaxKi/90)` seco, e passa do MaxKi de proposito:
		// e ESSE excesso que faz a chamada seguinte cair no `else if` de baixo e a escada andar).
		//
		// COM UM DRENO CONCORRENDO, o clamp vira uma barreira ABSORVENTE em exatamente 100%. Dentro de
		// uma forma o `TickDaForma` (`GameServer.Formas.cs:285`) tira Ki ANTES da carga no mesmo tique;
		// entao todo tique chegava aqui com `Ki < MaxKi`, enchia de volta ate o MaxKi CRAVADO, e
		// voltava. O estagio 3 era inalcancavel -- nao por falta de taxa, por nunca ser consultado.
		// Era o defeito do dono: "nao consigo passar do 100% de ki ao carregar no ssj3" (na base
		// funciona porque la nao ha dreno, e o clamp nunca chega a ser exercido duas vezes seguidas).
		//
		// A CORRECAO E O TEMPO, NAO O TETO: gasta-se do `dt` so o que fez falta pra encher, e o que
		// sobra do tique SEGUE pros estagios de baixo. Copiar o "passa do MaxKi" do DM tambem
		// destravaria, mas o tanto que passa seria o resto de um passo -- ou seja dependeria da
		// CADENCIA do servidor, e o mesmo personagem carregaria diferente a 30 e a 60 Hz. Repartir o
		// `dt` da o mesmo resultado em qualquer cadencia, que e a regra que este port ja segue ao
		// escrever tudo como taxa por segundo.
		// ==========================================================================================
		if (f.Ki < f.MaxKi)
		{
			double taxa = f.MaxKi * EncheComControle * f.racialRegenKiAtiva;   // por segundo -- (v0.4.0) vezes a regen ATIVA racial
			double atePassar = (f.MaxKi - f.Ki) / Math.Max(taxa, 1e-9);
			// CLAMP e nao `Min`: um MaxKi degenerado (zero) faria `atePassar` sair negativo, e um
			// `gasto` negativo devolveria folego e ALONGARIA o tique em vez de encurta-lo.
			double gasto = Math.Clamp(atePassar, 0, dt);

			f.Ki = Math.Min(f.Ki + taxa * gasto, f.MaxKi);
			f.stamina -= f.maxstamina * FolegoEnchendoComControle * gasto;

			// nao encheu dentro deste tique: o tanque e mesmo o assunto do momento
			if (gasto >= dt) return EstagioDaCarga.Enchendo;

			// encheu no meio do tique -- o resto do tempo pertence aos estagios seguintes
			dt -= gasto;
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
		if (f.Ki >= teto)
		{
			// JA NO MAXIMO: segura, nao sobe -- E CANSA (2026-10-13). Ate aqui este ramo saia sem cobrar nada, e
			// desde que o Ki acima de 100% passou a escorrer de quem NAO esta reunindo (`PrecoDoExcesso`), segurar o
			// C no teto seria o jeito de ficar la em cima de graca, pra sempre.
			if (!SegurarNoTetoDeGracaDeTeste) f.stamina -= f.maxstamina * FolegoUltrapassando * dt;
			return EstagioDaCarga.Ultrapassando;
		}

		// O GANHO ENCOLHE conforme se sobe (`/(3*kiratio)`), e e o que impede o power-up de virar
		// uma rampa infinita: quanto mais comprimido o ki, mais caro cada gota.
		double razao = Math.Max(f.Ki / Math.Max(f.MaxKi, 1e-9), 1);
		double baseTaxa = mexendo ? UltrapassaAndando : UltrapassaParado;
		double divisorH = mexendo ? 10 : 10;
		double denominador = mexendo ? 3 : 2.5;

		double ganho = f.MaxKi * baseTaxa * (Habilidade(f, divisorH) / denominador / (3 * razao)) * f.racialRegenKiAtiva * dt;
		f.Ki = Math.Min(f.Ki + ganho, teto);
		f.stamina -= f.maxstamina * FolegoUltrapassando * dt;
		return EstagioDaCarga.Ultrapassando;
	}

	/// <summary>
	/// `Ekiskill + (kicontrolskill + kigatheringskill)/N` -- a habilidade que multiplica os
	/// estagios 2 e 3. O divisor muda de estagio pra estagio no DM (15, 10, 10), entao ele entra
	/// como parametro em vez de virar tres copias da mesma conta.
	/// </summary>
	private static double Habilidade(Fighter f, double divisor)
		=> f.Ekiskill + (f.kicontrolskill + f.kigatheringskill) / Math.Max(divisor, 1e-9);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): segurar o C com o Ki ja no teto volta a nao gastar folego -- a
	/// v0.2.0. Lido pela propria linha, no <see cref="Passo"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SegurarNoTetoDeGracaDeTeste;

	// =====================================================================
	// O KI ACIMA DE 100% ESCORRE (dono, 2026-10-13)
	// =====================================================================
	// *"ki acima de 100% deveria cair mais rapido por ser algo 'nao natural' ultrapassar o maximo natural do seu
	// corpo, atualmente tu consegue ficar MUITO tempo com ki acima de 100%"* e *"quanto maior o ki acima de 100%
	// mais rapido ele deveria cair"*.
	//
	// O vazamento do DM dentro do teto seguro e `Ki * 0.0001` com `prob(25)` a 10 Hz (`Power Control.dm:126-130`):
	// 0,025% por segundo -- uma hora pra perder os 40% de quem carregou. Agora o excesso escorre por uma taxa que
	// CRESCE com ele: meio por cento do tanque por segundo, mais cinco por cento de cada "cem por cento" a mais.
	// De 140% volta-se a 100% em uns 32 s; de 200%, em uns 48 s -- e os primeiros pontos somem muito mais depressa
	// que os ultimos.
	//
	// SO DE QUEM NAO ESTA REUNINDO: com o C segurado (parado, com folego) o excesso se sustenta, e quem paga e o
	// vigor. Soltou a tecla, andou, ficou sem folego -- escorre.
	/// <summary>O vazamento de quem esta um fio acima dos 100%, em fracao do Ki MAXIMO por segundo.</summary>
	public const double VazamentoMinimoPorSegundo = 0.005;

	/// <summary>Quanto o vazamento cresce por cada 100% de excesso, em fracao do Ki MAXIMO por segundo.</summary>
	public const double VazamentoPorExcessoPorSegundo = 0.05;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o Ki acima de 100% volta a escorrer so pela taxa do DM (0,025% por
	/// segundo: fica la em cima por uma hora) -- a v0.2.0. Lido pelo <see cref="PrecoDoExcesso"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ExcessoNaoEscorreDeTeste;

	/// <summary>
	/// Quanto do Ki MAXIMO o excesso perde por segundo, pela razao de agora (1,4 = 140%): cresce com ela.
	/// Em 100% ou menos, zero.
	/// </summary>
	public static double VazamentoPorSegundo(double razao) =>
		razao <= 1 ? 0 : VazamentoMinimoPorSegundo + VazamentoPorExcessoPorSegundo * (razao - 1);

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
	/// <param name="reunindo">
	/// ESTE CORPO ESTA REUNINDO ENERGIA AGORA (o C segurado, parado, com folego)? Entao o excesso nao escorre pela
	/// taxa nova -- ver <see cref="VazamentoPorSegundo"/>. O padrao e falso: quem nao segura a tecla, escorre.
	/// </param>
	public static double PrecoDoExcesso(Fighter f, double dt, bool reunindo = false)
	{
		if (f.Ki <= f.MaxKi)
		{
			f.overcharge = false;
			return 0;
		}

		const double Eventos = 2.5;   // o prob(25) a 10 Hz
		double razao = f.Ki / Math.Max(f.MaxKi, 1e-9);

		// (2026-10-13) O EXCESSO ESCORRE, mais depressa quanto maior -- e nunca abaixo dos 100% por este caminho.
		if (!reunindo && !ExcessoNaoEscorreDeTeste)
		{
			f.Ki = Math.Max(f.MaxKi, f.Ki - f.MaxKi * VazamentoPorSegundo(razao) * dt);
			if (f.Ki <= f.MaxKi) { f.overcharge = false; return 0; }
			razao = f.Ki / Math.Max(f.MaxKi, 1e-9);
		}
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

	// =====================================================================
	// O CHAO TREME -- item 5b do playtest de 2026-10-10
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagvizinho`): o chao nunca treme -- o estado de antes de 2026-10-10, em
	/// que o "e o chao comeca a tremer" do aviso de <see cref="EstagioDaCarga.Ultrapassando"/> era so texto.
	/// Lido pela propria <see cref="TremeOChao"/>.
	/// </summary>
	public static bool ChaoNaoTremeDeTeste;

	/// <summary>
	/// A PARTIR DE QUANTO KI o chao treme, em razao sobre o tanque. O pedido do playtest diz o numero:
	/// *"enquanto o jogador segura C com o ki acima de 120%"*.
	///
	/// DESVIO DECLARADO DO ORIGINAL: no DM o tremor nasce junto com o estagio 3 (qualquer coisa acima de 100%)
	/// e e SORTEADO -- `prob(10)` por chamada do `Energy_Draw`, 3 s de `Quake()` de 8 px de cada vez
	/// (`Power Control.dm:161-164`, `Ascension.dm:7-21`). Aqui ele e um ESTADO: vale enquanto a condicao vale e
	/// para quando ela cai, que e o que o pedido descreve.
	/// </summary>
	public const double LimiarDoTremor = 1.2;

	/// <summary>
	/// ATE ONDE O TREMOR CHEGA, em tiles a partir de quem carrega. No DM e `range(screenx)` -- a tela de quem
	/// carrega, sem queda nenhuma com a distancia (e o planeta INTEIRO acima de 100 mil de Ki). Aqui e um raio,
	/// e a forca cai ate zero na borda dele (<see cref="PesoDoTremor"/>). Dezesseis tiles e mais ou menos ate
	/// onde o zumbido da carga se ouve (`CargaVisual`, 560 px): sente-se o chao ate onde se ouve a energia.
	/// </summary>
	public const float RaioDoTremorEmTiles = 16f;

	/// <summary>O mesmo raio em pixels de mundo -- a unidade em que o cliente mede a distancia.</summary>
	public const float RaioDoTremor = RaioDoTremorEmTiles * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// A FORCA, em pixels de camera, na tela de quem carrega (distancia zero). Baixa de proposito: e um ronco
	/// CONTINUO, que dura enquanto a tecla estiver em baixo. Os 8 px do `Quake()` do DM sao um solavanco de
	/// 3 s; 8 px sem parar nao deixam ler a tela.
	/// </summary>
	public const float ForcaDoTremor = 3f;

	/// <summary>
	/// QUANTA FORCA SOME POR SEGUNDO depois que o estado cai -- e portanto em quanto tempo a tela para quando
	/// ele solta o C (`ForcaDoTremor / QuedaDoTremor`: 0,375 s na tela de quem carrega, menos na de quem esta
	/// longe).
	/// </summary>
	public const float QuedaDoTremor = 8f;

	/// <summary>Segundos que a camera segura cada rumo -- a velocidade do ronco. A mesma da cinematica de transformacao.</summary>
	public const float CadenciaDoTremor = 0.08f;

	/// <summary>
	/// ESTE CORPO ESTA FAZENDO O CHAO TREMER? Quem responde e o SERVIDOR (o Ki alheio nao viaja), e a resposta
	/// vai no snapshot (`EntityState.CargaAlta`) -- ESTADO, e nao evento: quem chega depois ja sente, e o
	/// tremor para pra todo mundo no tique em que a condicao cai.
	///
	/// AS TRES CONDICOES sao a frase do pedido, uma por termo: SEGURANDO o C (<paramref name="segurando"/>),
	/// com a carga RENDENDO (andando ela fica suspensa, e a aura e o ronco apagam juntos -- ver
	/// <see cref="EstagioDaCarga.Andando"/>) e ACIMA de <see cref="LimiarDoTremor"/>.
	/// </summary>
	public static bool TremeOChao(Fighter f, bool segurando, EstagioDaCarga estagio)
		=> !ChaoNaoTremeDeTeste && segurando
		   && (estagio is EstagioDaCarga.Ultrapassando or EstagioDaCarga.Retomando)
		   && f.Ki > f.MaxKi * LimiarDoTremor;

	/// <summary>
	/// QUANTO DO TREMOR CHEGA a quem esta a <paramref name="distancia"/> pixels de quem carrega: inteiro em
	/// cima dele, caindo em linha reta ate zero em <see cref="RaioDoTremor"/>. E o *"tremor mais fraco quanto
	/// mais longe"* do pedido.
	/// </summary>
	public static float PesoDoTremor(float distancia)
		=> distancia >= RaioDoTremor ? 0f : 1f - Math.Max(distancia, 0f) / RaioDoTremor;

	/// <summary>
	/// A AURA ACENDE em `kiratio > 1,1` e apaga em `kiratio <= 1` (`AuraCheck`, Power Control.dm:22-37).
	///
	/// A HISTERESE E DE PROPOSITO -- acender e apagar no MESMO limiar faria a aura piscar em todo
	/// tick pra quem estivesse parado exatamente nele. A faixa morta entre 1,0 e 1,1 e o que
	/// mantem o efeito estavel.
	/// </summary>
	public static bool AuraAcesa(Fighter f, bool acesaAgora)
	{
		// ============================ ACENDE EM 100%, NAO EM 110% ============================
		// Era `> 1.1` pra acender e `<= 1.0` pra apagar -- uma histerese de dez pontos, escrita pra
		// a aura nao piscar em quem parou exatamente no limite.
		//
		// O dono mediu e nao gostou: "a aura constante so ativa quando ki ta em torno de 110/114% e
		// nao apartir de 101% como deveria ser (o contorno deveria começar ai tb)". Ele esta certo
		// -- passar dos 100% E o acontecimento; fazer o jogador comprimir mais dez pontos pra a tela
		// admitir isso desliga o retorno visual justo no comeco, que e onde ele ensina a mecanica.
		//
		// A HISTERESE FICA, so encolhe: acende em 1,01 e apaga em 1,00. Um ponto e o bastante pra
		// nao tremular (a carga sobe bem mais que 1% por tique) e e estreito o bastante pra parecer
		// imediato. Zerar a folga faria a aura piscar em quem para no limite, que era o defeito que
		// a versao antiga evitava -- e nao vale desfazer uma correcao pra fazer outra.
		// ================================================================================
		double razao = f.Ki / Math.Max(f.MaxKi, 1e-9);
		if (razao > 1.01) return true;
		if (razao <= 1.0) return false;
		return acesaAgora;
	}
}
