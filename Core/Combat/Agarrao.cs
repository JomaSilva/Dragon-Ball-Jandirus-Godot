using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// EM QUE PE ESTA O AGARRAO -- os tres estados do `grabMode` do original.
///
/// Os numeros sao os do DM de proposito (`Grabbing.dm`: `grabMode` 0/1/2), pra a comparacao com
/// `if(grabMode==2)` de la ser direta.
/// </summary>
public enum ModoDeAgarrao : byte
{
	/// <summary>`grabMode = 0` -- ninguem esta segurando ninguem.</summary>
	Nenhum = 0,

	/// <summary>
	/// `grabMode = 1` -- **segurando**. O corpo preso fica onde esta, e ANDAR o arremessa
	/// (`mob/OnStep()`, `Throw.dm:1-34`).
	/// </summary>
	Segurando = 1,

	/// <summary>
	/// `grabMode = 2` -- **carregando**. O corpo preso e colado no de quem carrega a cada tique
	/// (`grab()`, `Grabbing.dm:203`) e anda junto pra onde ele for.
	/// </summary>
	Carregando = 2,
}

/// <summary>
/// O AGARRAO -- pegar um corpo, segurar, carregar e arremessar.
///
/// ============================ ELE JA EXISTIA INTEIRO NO ORIGINAL ============================
/// Nada aqui e invencao: `Code/Grabbing.dm` (o verb, o laco e a soltura), `Code/Modules/Movement
/// Improvement/Throw.dm:1-34` (o arremesso) e `Code/Modules/Movement Improvement/movement
/// handler.dm:222-255` (a luta pra escapar). Ate o DUPLO TOQUE pra carregar e literal --
/// `Grabbing.dm:78-95`, o segundo `Grab()` com `grabMode==1` sobe pro 2.
///
/// O que **nao** existe la e a metade nova: no BYOND nao ha altitude (voar e um booleano) e o
/// `grabMode==2` copia so `x,y,z` -- a flag `flying` do carregado nunca e escrita. Quem e carregado
/// herdar a ALTURA e o MODO DE TRAVESSIA de quem carrega e pedido do dono, e mora no servidor
/// (`GameServer.Agarrao.cs`), nao aqui: e encanamento de corpo, nao formula.
/// ==========================================================================================
///
/// ============================ A REGRA E SOBRE **CORPO**, E ISSO E O DESENHO INTEIRO ============================
/// `Grabbing.dm:170` -- a unica pergunta que o original faz sobre o alvo e
/// <c>if(!grabbee &amp;&amp; !M.grabber)</c>: "eu ja seguro alguem? ele ja esta preso por outro?".
/// **Nao ha corte por vivo, por morto, por nocaute, por chefe, por cargo nem por BP.**
///
/// Quem inventou a excecao no BYOND foi o CADAVER, e ele a inventou por ser um `/obj`: o
/// `/obj/mobCorpse` nasce `IsntAItem=1` (`Corpse.dm:2`) e a lista de agarraveis so aceita objeto com
/// `canGrab && !IsntAItem` (`:101`,`:103`). Ou seja -- **o agarrao nunca recusou um corpo morto; ele
/// nunca VIU um, porque la o morto vira mobilia.**
///
/// Aqui o cadaver e o proprio corpo (ver `Core/World/Alem.cs`), entao a regra portada ao pe da letra
/// ja o agarra, carrega e arremessa **sem uma linha de excecao**. Este arquivo nao pergunta
/// `dead` em lugar nenhum, e isso e proposital: perguntar transformaria o pedido do dono
/// (*"o corpo mesmo morto TEM TODAS AS INTERACOES DE UM CORPO VIVO"*) numa segunda implementacao.
/// ============================================================================================================
/// </summary>
public static class Agarrao
{
	/// <summary>
	/// A FORCA DE QUEM SEGURA -- o `grabberSTR`. `Grabbing.dm:176`:
	/// <code>M.grabberSTR = (Ephysoff * expressedBP)</code>
	///
	/// **RECALCULADA A CADA TIQUE** (`:188`, dentro do `grab()`), e nao congelada no instante da
	/// pegada: quem se transforma segurando alguem aperta mais forte no mesmo gesto.
	/// </summary>
	public static double Forca(Fighter quemSegura) =>
		quemSegura.Ephysoff * quemSegura.expressedBP;

	/// <summary>
	/// CARREGAR APERTA 50% MAIS FORTE -- `grabbee.grabberSTR *= 1.5` (`Grabbing.dm:83`).
	///
	/// Ela nao anda sozinha: o carregado escapa DUAS vezes mais facil
	/// (<see cref="EscapeDeQuemECarregado"/>). Os dois numeros sao do original e sao intencionais --
	/// carregar e um aperto melhor e uma prisao pior, porque quem carrega tem as maos ocupadas.
	/// </summary>
	public const double ApertoDeQuemCarrega = 1.5;

	/// <summary>`if(grabber.grabMode == 2) escapechance *= 2` (`movement handler.dm:232`).</summary>
	public const double EscapeDeQuemECarregado = 2;

	/// <summary>`if(Race=="Majin") escapechance *= 5` (`movement handler.dm:236`) -- o corpo elastico.</summary>
	public const double EscapeDeMajin = 5;

	/// <summary>A raca que escorrega da mao. Escrita uma vez, aqui.</summary>
	public const string RacaQueEscorrega = "Majin";

	/// <summary>
	/// O QUANTO SE PRECISA JUNTAR PRA SE SOLTAR: `if(grabCounter >= 20/escapechance)`
	/// (`movement handler.dm:238`).
	/// </summary>
	public const double ContadorParaEscapar = 20;

	/// <summary>
	/// ============================ O PISO DA CHANCE DE ESCAPAR -- ESTE E DO PORT ============================
	/// O original nao tem piso: com um abismo de poder, `escapechance` tende a zero, `20/escapechance`
	/// tende ao infinito e **o preso nunca sai**. No BYOND isso passa despercebido porque o agarrao la
	/// e um gesto de segundos entre jogadores que estao brigando; aqui ele carrega alguem pelo mapa e
	/// por cima do oceano.
	///
	/// A instrucao do dono e explicita -- *"ninguem pode ficar preso -- prefira soltar"* --, e um PISO
	/// e melhor que um cronometro: com cronometro o agarrao acabaria sozinho no meio de um voo, sem
	/// nada explicando por que; com piso quem se debate SEMPRE progride, e quem nao se debate continua
	/// preso porque escolheu nao lutar.
	///
	/// A CONTA: com o piso em 0,05 o alvo do contador vira 400 (o teto de
	/// <see cref="LimiteDoContador"/>), e o ganho MINIMO por tique de luta e ~1,14
	/// (`Ephysoff/10 + Ephysdef/25 + 1 + Etechnique/5` com todos os stats em 1). Sao ~350 tiques de
	/// 0,1 s = **~35 s de luta continua no pior caso do jogo**. Bem acima do agarrao normal (que se
	/// resolve em um a tres segundos entre parelhos) e bem abaixo de "pra sempre".
	/// ====================================================================================================
	/// </summary>
	public const double EscapeMinimo = 0.05;

	/// <summary>O teto que o piso produz: <c>ContadorParaEscapar / EscapeMinimo</c>.</summary>
	public const double LimiteDoContador = ContadorParaEscapar / EscapeMinimo;

	/// <summary>
	/// DOIS TILES DE DISTANCIA SOLTAM NA HORA -- `if(get_dist(src,grabber) >= 2) escapechance = 9999`
	/// (`movement handler.dm:228`, o comentario do autor e "far away man").
	/// </summary>
	public const double EscapeDeQuemEstaLonge = 9999;

	/// <summary>
	/// A CHANCE DE ESCAPAR, literal -- `movement handler.dm:227-236`:
	/// <code>
	/// escapechance = (Etechnique*expressedBP)/grabberSTR
	/// if(get_dist(src,grabber) >= 2) escapechance = 9999
	/// escapechance /= BPModulus(grabberSTR, expressedBP)
	/// escapechance *= stamina/maxstamina
	/// escapechance *= grabber.maxstamina/grabber.stamina
	/// if(grabber.grabMode == 2) escapechance *= 2
	/// if(Race=="Majin") escapechance *= 5
	/// </code>
	///
	/// Repare que ela NAO e uma probabilidade de 0 a 100: e um divisor. Ela entra em
	/// <c>20/escapechance</c> (o alvo do contador) e no <see cref="CustoDeSegurar"/>. Um numero alto
	/// quer dizer "sai rapido", nao "sai com 90% de chance".
	/// </summary>
	/// <param name="forcaDeQuemSegura">O `grabberSTR` guardado no corpo preso.</param>
	/// <param name="longe">`get_dist(src,grabber) >= 2` -- ja medido pelo chamador, que tem as posicoes.</param>
	public static double ChanceDeEscapar(Fighter preso, Fighter quemSegura, double forcaDeQuemSegura,
										 bool longe, ModoDeAgarrao modo)
	{
		double str = Math.Max(forcaDeQuemSegura, 1e-9);

		double c = preso.Etechnique * preso.expressedBP / str;
		if (longe) c = EscapeDeQuemEstaLonge;

		c /= Math.Max(CombatMath.BpModulus(str, preso.expressedBP), 1e-9);
		c *= preso.maxstamina > 0 ? preso.stamina / preso.maxstamina : 1;
		c *= quemSegura.stamina > 0 ? quemSegura.maxstamina / quemSegura.stamina : 1;
		if (modo == ModoDeAgarrao.Carregando) c *= EscapeDeQuemECarregado;
		if (string.Equals(preso.Race, RacaQueEscorrega, StringComparison.OrdinalIgnoreCase))
			c *= EscapeDeMajin;

		return Math.Max(c, EscapeMinimo);
	}

	/// <summary>
	/// QUANTO O CONTADOR SOBE A CADA TIQUE EM QUE O PRESO SE DEBATE -- `movement handler.dm:233-234`:
	/// <code>grabCounter += Ephysoff/10 ; grabCounter += Ephysdef/25</code>
	/// </summary>
	public static double PassoDaLuta(Fighter preso) =>
		preso.Ephysoff / 10 + preso.Ephysdef / 25;

	/// <summary>
	/// E QUANTO ELE SOBE A MAIS QUANDO A TENTATIVA FALHA -- o `else` de `movement handler.dm:253-255`:
	/// <code>grabCounter += 1 ; grabCounter += Etechnique/5</code>
	///
	/// E o que garante progresso: mesmo sem sorte nenhuma, cada tique de luta aproxima a soltura.
	/// </summary>
	public static double PassoDeQuemFalhou(Fighter preso) => 1 + preso.Etechnique / 5;

	/// <summary>
	/// O EMPURRAO DE SORTE -- `if(prob(escapechance * grabCounter)) grabCounter += 2`
	/// (`movement handler.dm:237`). Um `prob()` que se realimenta: quanto mais perto de sair, mais
	/// chance de dar o salto.
	/// </summary>
	public const double SaltoDeSorte = 2;

	/// <summary>
	/// SEGURAR CUSTA FOLEGO A QUEM SEGURA -- `grabber.stamina -= min(0.10*escapechance, 2)`
	/// (`movement handler.dm:235`). Ou seja: **quanto mais o preso se debate, mais caro fica segurar**.
	/// Cobrado por tique de luta, e nao por segundo.
	/// </summary>
	public static double CustoDeSegurar(double chanceDeEscapar) =>
		Math.Min(0.10 * chanceDeEscapar, 2);

	// =====================================================================
	// O ESTRANGULAMENTO
	// =====================================================================
	/// <summary>
	/// `prob(5)` por tique de movimento da vitima (`movement handler.dm:193`).
	/// </summary>
	public const double ChanceDeAfogar = 5;

	/// <summary>
	/// O DANO DO ESTRANGULAMENTO -- `movement handler.dm:194-197`:
	/// <code>
	/// dmg = grabber.NormDamageCalc(src) + grabCounter
	/// dmg /= 20
	/// dmg *= BPModulus(grabber.expressedBP, expressedBP)
	/// </code>
	///
	/// O `NormDamageCalc` do DM e o <see cref="CombatMath.DanoBase"/> deste port (mesma formula:
	/// `(Ephysoff + Etechnique/1.25) / (M.Ephysdef + M.Etechnique/1.25) * global`).
	///
	/// **O `grabCounter` ENTRA NA SOMA**, e isso e a regra mais bonita do sistema: quem se debate se
	/// machuca mais. Debater-se e a saida E o preco.
	/// </summary>
	public static double DanoDeAfogamento(Fighter quemSegura, Fighter preso, double contador) =>
		(CombatMath.DanoBase(quemSegura, preso) + contador) / 20
		* CombatMath.BpModulus(quemSegura.expressedBP, preso.expressedBP);

	// =====================================================================
	// O ARREMESSO -- `Throw.dm:1-34`
	// =====================================================================
	/// <summary>`testback = min(testback,15)` (`Throw.dm:12`).</summary>
	public const int TiquesMaxDoArremesso = 15;

	/// <summary>
	/// A DISTANCIA DO ARREMESSO, em tiques de voo -- `Throw.dm:10-12`:
	/// <code>
	/// testback = ( Ephysoff * ( rand(3, 5*BPModulus(expressedBP, grabbee.expressedBP)) / 1.8 ) )
	///            / max( (grabbee.Ephysdef*grabbee.Etechnique)/2, 0.1 )
	/// testback = min(round(testback,1), 15)
	/// </code>
	///
	/// O TETO DE 15 NAO E O QUE MORDE: o `/effect/knockback` reaplica `min(kbdur,10)`
	/// (`Movement Effects.dm:43`), e o port faz o mesmo no <c>Arremessar</c>. Os dois tetos existem
	/// no original e ficam os dois aqui pra a conta bater numero a numero.
	/// </summary>
	/// <param name="sorteio">
	/// O `rand(3, N)` do original, INTEIRO e inclusivo nas duas pontas. Entra como funcao pra a
	/// formula continuar pura -- quem tem o dado e o servidor.
	/// </param>
	public static int TiquesDoArremesso(Fighter quemJoga, Fighter jogado, Func<int, int, int> sorteio)
	{
		double topo = 5 * CombatMath.BpModulus(quemJoga.expressedBP, jogado.expressedBP);

		// `rand(3, X)` no BYOND com X < 3 devolve um numero ENTRE os dois de qualquer jeito (ele nao
		// exige ordem). Aqui o piso e explicito pra o sorteio nunca receber um intervalo invertido.
		int alto = Math.Max(3, (int)Math.Round(topo));

		double t = quemJoga.Ephysoff * (sorteio(3, alto) / 1.8)
				 / Math.Max(jogado.Ephysdef * jogado.Etechnique / 2, 0.1);

		return (int)Math.Min(Math.Round(t), TiquesMaxDoArremesso);
	}

	/// <summary>
	/// A FORCA DO ARREMESSO -- o `kbpow`, `Throw.dm:15`:
	/// <code>grabbee.kbpow = (expressedBP/2) * Ephysoff * Etechnique</code>
	///
	/// **NAO E O DANO**: e ela que decide se a parede do caminho cai ou se o voo para nela (ver
	/// <see cref="Empurrao"/>). Por isso um lutador forte joga alguem ATRAVES de uma casa.
	/// </summary>
	public static double ForcaDoArremesso(Fighter quemJoga) =>
		quemJoga.expressedBP / 2 * quemJoga.Ephysoff * quemJoga.Etechnique;

	/// <summary>
	/// O DANO DE SER ARREMESSADO -- `Throw.dm:20-27`:
	/// <code>
	/// if(Ephysoff>1||Etechnique>1) phystechcalc = Ephysoff+Etechnique
	/// if(g.Ephysoff>1||g.Etechnique>1) opponentphystechcalc = g.Ephysoff+g.Etechnique
	/// dmg = DamageCalc(phystechcalc, opponentphystechcalc, Ephysoff)
	/// damage_mob(grabbee, dmg * BPModulus(expressedBP, grabbee.expressedBP) / 4)
	/// </code>
	///
	/// ============================ AS DUAS ESQUISITICES SAO DO DM, E FICAM ============================
	/// 1. **Os `if(... > 1)`**: quando os dois stats de alguem estao em 1 ou abaixo, o `phystechcalc`
	///    fica **nulo** no BYOND -- que em conta vale ZERO. Do lado de quem joga isso zera o dano; do
	///    lado de quem apanha, o `DamageCalc` troca o divisor 0 por 1 (`calcs.dm:3-4`), o que faz o
	///    alvo destreinado apanhar MAIS. Nao e engano de leitura: e o comportamento de la, e mudar
	///    aqui faria a conta divergir do original sem que ninguem soubesse.
	/// 2. **O denominador do DamageCalc usa `Ephysoff+Etechnique`** e nao o `Ephysdef` -- diferente do
	///    `NormDamageCalc`, que usa `Ephysdef+Etechnique/1.25`. E o proprio comentario do autor no
	///    `Throw.dm` diz que ele "usou uma equacao PARECIDA com a do KB": parecida, nao igual.
	/// ============================================================================================
	///
	/// **O `/4` fica no fim**, como la: o arremesso machuca menos que o soco que o gerou -- o estrago
	/// de verdade vem do baque no fim do voo, que e o `/effect/knockback`.
	/// </summary>
	public static double DanoDoArremesso(Fighter quemJoga, Fighter jogado)
	{
		double meu = quemJoga.Ephysoff > 1 || quemJoga.Etechnique > 1
			? quemJoga.Ephysoff + quemJoga.Etechnique : 0;
		double dele = jogado.Ephysoff > 1 || jogado.Etechnique > 1
			? jogado.Ephysoff + jogado.Etechnique : 0;

		// `DamageCalc`: `if(!downscalar) downscalar = 1` (`calcs.dm:3-4`).
		double calc = meu / (dele == 0 ? 1 : dele) * quemJoga.Ephysoff;

		return calc * CombatMath.BpModulus(quemJoga.expressedBP, jogado.expressedBP) / 4;
	}
}
