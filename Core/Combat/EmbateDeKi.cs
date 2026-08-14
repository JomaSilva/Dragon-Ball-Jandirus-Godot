using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// O EMBATE DE KI -- dois feixes se encontrando no ar, ou um feixe encontrando as MAOS de quem
/// aguenta. A fisica do medidor, pura, sem servidor e sem Godot.
///
/// ============================ ISTO E PORTE, E O ORIGINAL E `BeamClash.dm` ============================
/// O pedido do dono foi *"a COLISAO DE KI (q seguiria a mesma ideia do zanzoclash mas como ki
/// EMPURRANDO o outro ate atingir o inimigo ou vc)"*, e a instrucao desta camada dizia pra escrever
/// "desenho novo" no comentario **se o DM nao tivesse colisao de feixe**. Ele tem:
/// `Code/Modules/CombatMechanics/BeamClash.dm`, 463 linhas, com o gatilho no `Bump` de
/// `objects.dm:509` (frontal) e em `objects.dm:246-254` (por proximidade), a tecla no
/// `Hotkeys.dm:119`, a IA no `NPCAI.dm:374` e ate um pulso de Ultra Ego no `UltraEgo.dm:615`.
///
/// Entao os numeros abaixo NAO sao escolhidos: sao os `#define BCL_*` do original, um por um. O que
/// e desenho novo esta marcado NOVO no lugar em que aparece, e sao tres coisas: o feixe contra QUEM
/// BLOQUEIA (o DM so disputa feixe contra feixe), o ponto de encontro que ANDA durante a disputa (la
/// so o orbe se deslocava em pixel, e o avanco de verdade era depois) e a troca do martelar de
/// ESPACO pelo quick time event de letras -- ver <see cref="ApertosPorLetra"/>, que e onde as duas
/// escalas se encontram.
/// ================================================================================================
///
/// ============================ O MEDIDOR E UM CABO DE GUERRA ============================
/// Um numero so, de 0 a 100, comecando em 50. 100 = o lado A engoliu a disputa, 0 = o lado B. Ele
/// sobe com o que A conquista e desce com o que B conquista, e o mais forte ainda o empurra sozinho
/// (a <see cref="DerivaPorCiclo"/>) mesmo que nao faca nada -- que e o que impede um lutador muito
/// mais fraco de EMPATAR de graca so ficando quieto.
/// =======================================================================================
/// </summary>
public static class EmbateDeKi
{
	// =====================================================================
	// OS NUMEROS DO DM (`BeamClash.dm:18-33`)
	// =====================================================================
	/// <summary>`BCL_TICK 2` -- o passo do laco da disputa, 2 tiques de 0,1 s.</summary>
	public const double SegundosPorCiclo = 0.2;

	/// <summary>`BCL_PUSH_PER_PRESS 2.2` -- quanto um aperto move o medidor, antes da vantagem.</summary>
	public const double EmpurraoPorAperto = 2.2;

	/// <summary>
	/// `BCL_ADV_CAP 6` -- o teto da vantagem por poder.
	///
	/// O comentario do DM promete: *"com o dobro do BP, cada aperto vale 2x, entao ele pode apertar
	/// bem mais devagar e ainda vencer"*. O teto existe pela mesma razao do <c>TetoDaVantagem</c> do
	/// ZanzoClash (que e 8): sem ele, um encontro entre faixas diferentes de poder daria
	/// multiplicador de milhoes e a disputa viraria enfeite pros dois lados.
	/// </summary>
	public const double TetoDaVantagem = 6;

	/// <summary>
	/// `BCL_DRIFT_PER_TICK 0.45` -- a deriva passiva, por ciclo de 0,2 s, a favor do mais forte.
	///
	/// E ela que faz "nao apertar" ser uma derrota lenta em vez de um empate. Repare que ela usa a
	/// DIFERENCA das duas vantagens, e nao a razao: em forcas parelhas as duas valem 1 e a deriva e
	/// exatamente zero.
	/// </summary>
	public const double DerivaPorCiclo = 0.45;

	/// <summary>`BCL_MAX_SECONDS 22` -- alem disto a disputa expira e se decide no medidor.</summary>
	public const double SegundosMaximos = 22;

	/// <summary>`BCL_KI_PER_TICK 0.004` -- a fracao do Ki maximo que CADA lado queima por ciclo.</summary>
	public const double KiPorCiclo = 0.004;

	/// <summary>`BCL_NPC_CPS 3.2` -- "apertos por segundo" base de um NPC na disputa.</summary>
	public const double ApertosPorSegundoBase = 3.2;

	/// <summary>`var/intel = 50` (`BeamClash.dm:145`) -- a inteligencia de quem nao e NPC.</summary>
	public const double InteligenciaPadrao = 50;

	/// <summary>`BCL_NPC_DETECT 7` -- a que distancia, em tiles, o NPC percebe um feixe vindo.</summary>
	public const double TilesDeFaro = 7;

	/// <summary>`BCL_NPC_BEAM_CD 220` tiques -- o respiro entre contra-feixes de um NPC.</summary>
	public const double SegundosEntreContraFeixes = 22;

	/// <summary>
	/// `BCL_NPC_BEAM_TIME 120` tiques -- por quanto tempo um NPC segura o proprio feixe.
	///
	/// ============================ ESTE NUMERO RESPONDE A PERGUNTA QUE FICOU ABERTA ============================
	/// A camada 1 anotou que o gancho da IA (`Cerebro.Disparo`) modela "conjura, solta um pulso,
	/// pausa" e nao um estado SUSTENTADO, e deixou pro dono decidir se o raio da IA seria tiro unico
	/// ou se o `Comando` ganharia um estado de canal.
	///
	/// O DM ja tinha respondido: `npc_beam_loop` canaliza e o laco morre por TEMPO. Ou seja, o raio
	/// da IA e um pulso que ABRE o canal e um prazo que o FECHA -- nada muda no `Comando`, e o gancho
	/// estava certo. A condicao do laco de la (`beamclash || world.time - started &lt; BCL_NPC_BEAM_TIME`)
	/// tem a outra metade: numa DISPUTA o prazo nao corre, *"senao o NPC desistiria no meio do clash"*.
	/// ======================================================================================================
	/// </summary>
	public const double SegundosDeFeixeDeNpc = 12;

	/// <summary>`BCL_NPC_KI_MIN 0.25` -- fracao minima de Ki pro NPC tentar o contra-feixe.</summary>
	public const double FracaoDeKiDoContraFeixe = 0.25;

	/// <summary>`BCL_PUSH_STEP 2` -- segundos por tile do empurrao depois da vitoria (0,2 s).</summary>
	public const double SegundosPorTileDoEmpurrao = 0.2;

	/// <summary>`BCL_PUSH_MAX 30` -- quantos tiles o feixe vencedor empurra antes de voltar ao voo normal.</summary>
	public const double TilesDoEmpurrao = 30;

	/// <summary>O medidor comeca no meio (`meter = 50`) e vai de 0 a 100.</summary>
	public const double MedidorInicial = 50;

	/// <summary>
	/// A FAIXA DO EMPATE no fim do prazo: `if(meter > 55) A; else if(meter < 45) B; else draw()`.
	/// </summary>
	public const double MargemDoEmpate = 5;

	// =====================================================================
	// O PODER DE CADA LADO
	// =====================================================================
	/// <summary>
	/// O PODER DE UM FEIXE -- `BP * mods * basedamage`.
	///
	/// ============================ POR QUE NAO E O `expressedBP` CRU ============================
	/// O `advantage()` do `BeamClash.dm:132` compara `expressedBP` puro. Mas o MESMO arquivo de
	/// origem tem uma segunda comparacao pra a mesma pergunta: a resolucao automatica de feixe contra
	/// feixe (`objects.dm:503`) faz `BPModulus(BP*mods*basedamage, R.BP*R.mods*R.basedamage)` -- ou
	/// seja, quando o DM precisa dizer QUAL FEIXE e mais forte, ele nao pergunta o BP do corpo:
	/// pergunta o poder do tiro.
	///
	/// Portei a segunda, e o motivo e a camada 2: agora o jogador DESENHA a propria tecnica (potencia
	/// de 0,1 a quanto ele pagar, carga, alcance). Com o `expressedBP` cru, largar a tecnica mais
	/// barata do painel disputaria exatamente igual a largar a mais cara -- a loja de pontos inteira
	/// deixaria de valer no unico momento em que dois ataques de ki se medem. O BP continua mandando
	/// (ele e fator do produto); o que muda e que a TECNICA tambem conta.
	///
	/// O que se perdeu: um numero a mais na conta do que o `advantage()` do DM tinha.
	/// ==========================================================================================
	/// </summary>
	public static double PoderDoFeixe(double bp, double mods, double baseDano)
		=> Math.Max(bp, 1) * Math.Max(mods, 1e-6) * Math.Max(baseDano, 1e-6);

	/// <summary>
	/// O PODER DE SEGURAR UM FEIXE COM AS MAOS. **NOVO** -- o DM so disputa feixe contra feixe.
	///
	/// ============================ O NUMERO NAO E INVENTADO ============================
	/// Ele e o NUMERADOR da chance de deflexao do proprio DM (`objects.dm:333`, ja portado em
	/// <see cref="DanoDeKi.ChanceDeDeflexao"/>): `Ekidef * max(expressedBP,1) * max(Ekiskill,
	/// Etechnique) * max(kidefenseskill/10,1)`, e o DENOMINADOR de la e exatamente
	/// `BP * mods * basedamage`, que e o <see cref="PoderDoFeixe"/> acima. Quer dizer: o jogo ja
	/// tinha uma conta que compara um corpo com um tiro de ki -- ela e a razao entre estes dois
	/// numeros --, e o que este metodo faz e usa-la pra outra pergunta.
	///
	/// A DIVISAO POR 100 e a unica coisa a explicar. A razao do DM sai em PORCENTO (`prob(chance)`),
	/// entao o ponto em que o corpo defletiria o tiro SEMPRE (100%) e o ponto em que as duas forcas
	/// se anulam -- e e ali que a disputa tem que comecar empatada. Sem esta normalizacao as maos
	/// nasceriam cem vezes mais fracas que qualquer feixe e o embate de guarda seria decorativo.
	///
	/// A GUARDA DOBRA, como no DM (`if(M.blocking) deflectchance *= 2`, `objects.dm:341`) -- e por
	/// isso quem SOLTA a guarda no meio perde forca na hora, que e o que faz "sair do embate" ter
	/// preco tambem deste lado.
	/// ==================================================================================
	/// </summary>
	public static double PoderDeSegurar(Fighter f, bool bloqueando)
	{
		double cima = f.Ekidef * Math.Max(f.expressedBP, 1) * Math.Max(f.Ekiskill, f.Etechnique)
					* Math.Max(f.kidefenseskill / 10, 1);
		if (bloqueando) cima *= 2;
		return Math.Max(cima / 100, 1e-6);
	}

	/// <summary>
	/// A VANTAGEM DE UM LADO SOBRE O OUTRO: `min(max(a/b, 1/CAP), CAP)` (`BeamClash.dm:132-135`).
	///
	/// Repare que o piso e `1/CAP` e nao 1: o lado mais fraco tem a vantagem INVERSA, e e ela que
	/// entra na deriva com sinal negativo. Escrever `max(a/b, 1)` (que era a tentacao) faria os dois
	/// lados terem vantagem >= 1 e a deriva nunca sair de zero.
	/// </summary>
	public static double Vantagem(double meu, double dele)
	{
		double a = Math.Max(meu, 1e-9), b = Math.Max(dele, 1e-9);
		return Math.Clamp(a / b, 1 / TetoDaVantagem, TetoDaVantagem);
	}

	// =====================================================================
	// O QUE UM ACERTO VALE
	// =====================================================================
	/// <summary>
	/// APERTOS POR SEGUNDO de quem nao tem teclado: `BCL_NPC_CPS * (0.5 + intel/100)`
	/// (`BeamClash.dm:148`).
	///
	/// A mesma funcao serve pra um NPC burro (intel 0 -> 1,6/s) e pra um genio (intel 100 -> 4,8/s).
	/// </summary>
	public static double ApertosPorSegundo(double inteligencia0a100)
		=> ApertosPorSegundoBase * (0.5 + Math.Clamp(inteligencia0a100, 0, 100) / 100);

	/// <summary>
	/// QUANTO VALE UMA LETRA ACERTADA, em "apertos" do DM. **A costura entre as duas escalas.**
	///
	/// ============================ POR QUE O ESPACO VIROU LETRA ============================
	/// No DM a disputa e MARTELAR ESPACO, com um teto anti-macro de 5 apertos por ciclo (25/s). O
	/// ZanzoClash deste port ja atravessou essa discussao e a resposta esta escrita no
	/// `MsPorTecla`: *"um quick time event que premia velocidade de DIGITACAO mede a maquina do
	/// jogador, e a um cliente automatico entrega a vitoria de graca"*. Martelar espaco e literalmente
	/// isso -- e o dono pediu a colisao de ki *"seguindo a mesma ideia do zanzoclash"*.
	///
	/// Entao a ENTRADA e a do ZanzoClash (uma letra sorteada por janela fixa) e a FISICA continua a
	/// do DM. O que costura as duas e esta funcao: uma letra certa vale o que o martelar de um
	/// oponente MEDIANO (a `intel = 50` que o proprio DM usa pra quem tem cliente) renderia durante a
	/// janela daquela letra. Consequencias:
	///   * o ritmo da disputa nao muda -- 50 pontos de medidor continuam levando ~7 s de dominio;
	///   * jogador perfeito e NPC mediano empatam, e a diferenca passa a ser o PODER e o erro;
	///   * nenhum numero novo foi inventado: sao `BCL_PUSH_PER_PRESS`, `BCL_NPC_CPS` e a janela da
	///     letra, que ja existia.
	/// ======================================================================================
	/// </summary>
	public static double ApertosPorLetra(double segundosDaJanela)
		=> ApertosPorSegundo(InteligenciaPadrao) * Math.Max(segundosDaJanela, 0);

	// =====================================================================
	// A FISICA DO MEDIDOR
	// =====================================================================
	/// <summary>
	/// UM PASSO DO CABO DE GUERRA (`BeamClash.dm:176-181`):
	/// <code>
	/// meter += side_presses(A) * BCL_PUSH_PER_PRESS * advA
	/// meter -= side_presses(B) * BCL_PUSH_PER_PRESS * advB
	/// meter += (advA - advB) * BCL_DRIFT_PER_TICK
	/// meter = min(max(meter, 0), 100)
	/// </code>
	///
	/// A DERIVA E POR CICLO DE 0,2 s e aqui o tique e de 1/30 s, entao ela e escalada por
	/// `dt / SegundosPorCiclo`. Copiar o `0.45` cru faria a deriva render seis vezes mais que no
	/// original -- o tipo de erro que nao aparece em teste nenhum, so em "o mais forte ganha sozinho
	/// rapido demais".
	/// </summary>
	public static double Empurrar(double medidor, double apertosA, double apertosB,
								  double vantagemA, double vantagemB, double dt)
	{
		medidor += apertosA * EmpurraoPorAperto * vantagemA;
		medidor -= apertosB * EmpurraoPorAperto * vantagemB;
		medidor += (vantagemA - vantagemB) * DerivaPorCiclo * (dt / SegundosPorCiclo);
		return Math.Clamp(medidor, 0, 100);
	}

	/// <summary>
	/// A DISPUTA ACABOU? `meter >= 100` A engole, `meter <= 0` B engole, e no prazo a margem de 5
	/// decide -- dentro dela os dois feixes explodem em pe de igualdade (`draw()`).
	/// </summary>
	public static FimDeEmbateDeKi Decidir(double medidor, double segundosCorridos)
	{
		if (medidor >= 100) return FimDeEmbateDeKi.VenceuA;
		if (medidor <= 0) return FimDeEmbateDeKi.VenceuB;
		if (segundosCorridos < SegundosMaximos) return FimDeEmbateDeKi.Continua;

		if (medidor > MedidorInicial + MargemDoEmpate) return FimDeEmbateDeKi.VenceuA;
		if (medidor < MedidorInicial - MargemDoEmpate) return FimDeEmbateDeKi.VenceuB;
		return FimDeEmbateDeKi.Empate;
	}

	/// <summary>
	/// ONDE O PONTO DE ENCONTRO ESTA, de -1 (colado no lado A) a +1 (colado no lado B). **NOVO.**
	///
	/// ============================ O DM SO EMPURRAVA O ORBE ============================
	/// La o ponto de colisao e um `turf` fixo e o que se move e o `pixel_x` do orbe:
	/// `push = round((meter - 50) / 50 * 20)` (`BeamClash.dm:215`), 20 pixels no maximo. O avanco de
	/// verdade so acontece DEPOIS, no `push_phase`, quando a disputa ja acabou.
	///
	/// O dono descreveu outra coisa, e com estas palavras: *"como ki EMPURRANDO o outro ate atingir o
	/// inimigo ou vc"*. Entao aqui a MESMA razao do orbe move as CABECAS: enquanto se disputa, o
	/// ponto de encontro caminha pelo campo, e quem esta perdendo ve o feixe do outro chegando. A
	/// formula e a do orbe, sem o `round` e sem o teto de 20 px -- a escala de quanto isso vale em
	/// pixels e do servidor, que e quem sabe onde os dois corpos estao.
	///
	/// O que se ganhou: a tensao fica no MUNDO e nao so na barra da tela, e o empurrao depois da
	/// vitoria vira a continuacao de um movimento que ja estava acontecendo em vez de um corte.
	/// =================================================================================
	/// </summary>
	public static double Deslocamento(double medidor)
		=> Math.Clamp((medidor - MedidorInicial) / MedidorInicial, -1, 1);
}

/// <summary>Como a disputa terminou. Ver <see cref="EmbateDeKi.Decidir"/>.</summary>
public enum FimDeEmbateDeKi : byte
{
	/// <summary>Ainda esta de pe.</summary>
	Continua = 0,

	/// <summary>O lado A engoliu a disputa.</summary>
	VenceuA = 1,

	/// <summary>O lado B engoliu a disputa.</summary>
	VenceuB = 2,

	/// <summary>Prazo vencido em pe de igualdade: os dois estouram no meio (`draw()`).</summary>
	Empate = 3,
}
