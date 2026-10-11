using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Core.Forms;

/// <summary>Por onde a exposicao ao ki divino entrou. Os dois caminhos do dono enchem o MESMO medidor.</summary>
public enum FonteDaExposicao : byte
{
	/// <summary>O ar de um mundo de atmosfera divina (`AtmosferaDivina`).</summary>
	Atmosfera = 0,
	/// <summary>Um golpe trocado com quem tem ki divino, ou esta numa forma divina.</summary>
	Luta = 1,
	/// <summary>A mao de um admin -- o `Force God Ki` do `Godki_Settings` (`godkiattain.dm:76-77`).</summary>
	Admin = 2,
}

/// <summary>O que o despertar fez nesta ficha, pra quem chamou poder contar ao jogador.</summary>
/// <param name="Despertou">Falso = ja estava desperto, e nada foi escrito.</param>
/// <param name="Marco">O novo multiplicador de ganho (5x), ou 0 se o marco nao subiu nada.</param>
/// <param name="BonusDeBp">O multiplicador permanente de poder (2,5x), ou 0 pra quem nao o recebe.</param>
/// <param name="Maestria">A maestria divina com que ele sai do despertar (33 no sangue divino, 0 nos outros).</param>
public readonly record struct ResultadoDoDespertar(bool Despertou, double Marco, double BonusDeBp, double Maestria, bool SangueDivino);

/// <summary>
/// O TETO POR TEMPO da exposicao que vem de LUTA: uma janela corrida de
/// <see cref="DespertarDivino.SegundosDoTeto"/> em que cabem
/// <see cref="DespertarDivino.TetoDeLutaPorMinuto"/> pontos, e nem um a mais.
/// </summary>
public struct TetoDeLuta
{
	/// <summary>Quando a janela de agora abriu, no relogio de quem chama (segundos).</summary>
	public double Inicio;
	/// <summary>Quanto ja entrou nesta janela.</summary>
	public double Ganho;

	/// <summary>Quanto de <paramref name="pedido"/> cabe agora. A janela se renova sozinha quando vence.</summary>
	public double Conceder(double agora, double pedido)
	{
		if (agora - Inicio >= DespertarDivino.SegundosDoTeto) { Inicio = agora; Ganho = 0; }
		double dado = Math.Min(Math.Max(pedido, 0), Math.Max(0, DespertarDivino.TetoDeLutaPorMinuto - Ganho));
		Ganho += dado;
		return dado;
	}
}

/// <summary>
/// O DESPERTAR DO KI DIVINO -- as regras. Quem as aplica e o servidor (`GameServer.GodKi.cs`).
///
/// ============================ O QUE FALTAVA (achado de 2026-10-10) ============================
/// Nenhum codigo de producao ligava `godki.awakened`: so as bancadas. O perfil de formas, o catalogo, o
/// desafio ao Deus da Destruicao e as duas disciplinas divinas ja LIAM o ki divino -- e ele era
/// inalcancavel no jogo de verdade. E o defeito que mais se repetiu neste port: a regra escrita, e
/// ninguem pra liga-la.
/// ==========================================================================================
///
/// ============================ O QUE O DONO DECIDIU, PALAVRA POR PALAVRA ============================
/// *"o god ki alguns planetas raros vao possuir uma atmosfera divina ao qual quando o jogador tiver um bp
/// base maior q o requisito minimo dele de despertar o god ki, ao ficar um tempo nessa atmosfera o ki
/// divino e liberado, outra forma seria lutando com pessoas q ja tem ki divino ou estao transformadas em
/// formas divinas, mas claro so desperta se tiver acima do bp base minimo do god ki pessoal dele."*
///
/// TRES PECAS, entao: o REQUISITO pessoal de BP base (<see cref="RequisitoDeBpBase"/>), o MEDIDOR que os
/// dois caminhos enchem (`Fighter.ExposicaoDivina`, de 0 a <see cref="ExposicaoCheia"/>) e o DESPERTAR
/// em si (<see cref="Despertar"/>, o `get_godki()` do `godkiattain.dm:32-44`).
/// ==============================================================================================
///
/// ============================ O QUE MUDOU EM RELACAO AO DM, DECLARADO ============================
///   * NO DM QUEM TEM O BP DESPERTA NA HORA: o `gain_godki` (`godkiattain.dm:21-26`) soma a energia e,
///     com `BP >= godki_at`, ENCHE o tanque e chama o `get_godki()` na mesma linha -- qualquer gota
///     basta. A energia so se ACUMULA la pra quem esta ABAIXO do requisito (e ai ela e o ki divino
///     emprestado do ritual, que este port nao tem). Aqui e o contrario, a pedido do dono: abaixo do
///     requisito NADA se acumula, e acima dele o despertar leva TEMPO.
///   * AS FONTES do original (meditar numa fenda divina, `Rifts.dm:101-105`; o ritual dos seis,
///     `GodRitual.dm:87`; a forma Golden do Frost Demon, `IcerTransform.dm:295`) deram lugar as duas do
///     dono. A fenda e a mae da atmosfera; lutar com um deus so TREINAVA a maestria de quem ja era deus
///     (`calcs.dm:147-148`), e agora tambem desperta.
///   * O REQUISITO e pessoal aqui e global la -- ver <see cref="RequisitoDeBpBase"/>.
/// ==============================================================================================
/// </summary>
public static class DespertarDivino
{
	// =====================================================================
	// 1. O REQUISITO -- "o bp base minimo do god ki PESSOAL dele"
	// =====================================================================
	/// <summary>`godki_at` (`godkiattain.dm:2`): 250 milhoes de BP BASE. E o centro de onde sai o de cada um.</summary>
	public const double BpBaseDeFabrica = 2.5e8;

	/// <summary>
	/// A FAIXA PESSOAL, em decimos: de x0,9 a x1,3 -- 225 a 325 milhoes, em cinco valores.
	///
	/// ============================ DIVERGENCIA DECLARADA: NO DM ELE E UM SO ============================
	/// `godki_at` e uma `var` global (`godkiattain.dm:2`, com um botao de admin em `:58-59`): todo mundo
	/// desperta no mesmo numero. O dono escreveu "o requisito minimo DELE" e "do god ki PESSOAL dele", e
	/// a casa ja tem o que isso quer dizer -- e o que ele mesmo pediu pro Super Namekuseijin: *"mantendo a
	/// ideia de cada um ter um requisito pessoal, mas em torno de um valor"* (`LimiaresPessoais`).
	///
	/// A FAIXA E A QUE O PROPRIO DM USA PRAS FORMAS ALTAS: `initial(X) * rand(9,13)/10`, igual pra toda
	/// classe (`statsaiyan.dm:52-56` -- SSJ3, SSJ4, Legendary). E DISCRETA de proposito, como la:
	/// `rand(9,13)/10` da cinco valores, nao um numero continuo.
	///
	/// PRA VOLTAR AO NUMERO UNICO DO ORIGINAL: os dois em 10.
	/// ================================================================================================
	/// </summary>
	public const int FaixaPessoalDe = 9, FaixaPessoalAte = 13;

	/// <summary>O campo do sorteio. Um gerador por campo, como no `LimiaresPessoais.Sorteador`.</summary>
	private const string CampoDoRequisito = "godkiat";

	/// <summary>Sal do requisito ("GODKIAT" em ASCII). Qualquer constante que nao seja a do `Misturar`.</summary>
	private const ulong SalDoRequisito = 0x474F444B49415400UL;

	/// <summary>
	/// O FATOR PESSOAL deste personagem -- funcao pura da SEMENTE dele, a mesma que sorteou os limiares
	/// de Super Saiyajin no nascimento.
	///
	/// NAO HA CAMPO NOVO NO SAVE, e e por isso que isto e uma conta e nao um valor guardado: a semente ja
	/// esta gravada (`LimiaresPessoais.Semente`) e, pra quem nasceu antes de ela existir, se reconstroi do
	/// nome e do instante de criacao (`LimiaresPessoais.SementeDe`). Personagem de ontem tem requisito
	/// hoje, o mesmo em qualquer maquina e em qualquer reinicio -- sem conserto de login.
	/// </summary>
	public static double FatorPessoal(ulong sementeDoPersonagem)
	{
		int valores = FaixaPessoalAte - FaixaPessoalDe + 1;
		ulong h = Espaco.Misturar(sementeDoPersonagem, Espaco.Hash64(CampoDoRequisito), SalDoRequisito);
		return (FaixaPessoalDe + (int)(h % (ulong)valores)) / 10.0;
	}

	/// <summary>
	/// O BP BASE QUE ESTE PERSONAGEM PRECISA TER PRA O KI DIVINO DESPERTAR NELE. O unico lugar do jogo
	/// que responde isso.
	/// </summary>
	public static double RequisitoDeBpBase(ulong sementeDoPersonagem) =>
		BpBaseDeFabrica * FatorPessoal(sementeDoPersonagem);

	/// <summary>
	/// ESTE CORPO JA ALCANCA O KI DIVINO? E o BP BASE (`Fighter.BP`) contra o requisito pessoal: nunca o
	/// expresso, nunca o de forma -- a mesma regra da porta do Super Saiyajin, e pelo mesmo motivo (uma
	/// forma, uma raiva ou um Kaio-ken nao podem fingir o requisito). E o `BP &lt; godki_at` do
	/// `godkiattain.dm:24`, onde `BP` tambem e o base.
	///
	/// O `godki_gt_mode` fecha a porta como no original (`godkiattain.dm:22`).
	/// </summary>
	public static bool AlcancaORequisito(Fighter f, ulong sementeDoPersonagem) =>
		!f.godki_gt_mode && f.BP >= RequisitoDeBpBase(sementeDoPersonagem);

	public static bool JaDespertou(Fighter f) => f.godki is { awakened: true };

	// =====================================================================
	// 2. O MEDIDOR -- os dois caminhos enchem o mesmo
	// =====================================================================
	/// <summary>Com quanto de exposicao o ki divino desperta. `if(godki.energy >= 100)`, `godkiattain.dm:33`.</summary>
	public const double ExposicaoCheia = 100;

	/// <summary>
	/// QUANTO TEMPO DE ATMOSFERA ENCHE O MEDIDOR, do zero: dez minutos. *"Ao ficar um tempo nessa
	/// atmosfera"* -- minutos, e nao horas: o custo de verdade ja e o requisito de BP e a viagem ate um
	/// mundo raro. So conta o tempo de quem ja alcanca o requisito (e nao e um corpo morto no chao).
	/// </summary>
	public const double SegundosNaAtmosfera = 600;

	public static double PorSegundoNaAtmosfera => ExposicaoCheia / SegundosNaAtmosfera;

	/// <summary>
	/// O QUE UM GOLPE VALE, quando ele ENCOSTA e do outro lado ha ki divino. Levar o golpe de um deus
	/// expoe mais do que acerta-lo: e o ki dele entrando no seu corpo.
	///
	/// OS DOIS NUMEROS QUASE NUNCA MANDAM -- quem manda numa luta de verdade e o
	/// <see cref="TetoDeLutaPorMinuto"/>, que duas duzias de golpes por minuto ja alcancam. Eles existem
	/// pra que uma troca rala (um golpe a cada tanto) renda menos que uma luta.
	/// </summary>
	public const double PorGolpeDado = 0.4, PorGolpeRecebido = 0.6;

	/// <summary>
	/// A JANELA DO REVIDE, em segundos: o golpe so conta se o outro lado tambem ACERTOU voce (ou voce
	/// nele) ha menos que isto. E o que separa trocar golpes de bater num deus parado -- ou de ficar
	/// parado apanhando de um. Dez segundos e o `CombatKnobs.LutaDeVerdade` do port.
	/// </summary>
	public const double SegundosDeRevide = 10;

	/// <summary>
	/// O TETO DA LUTA: no maximo dez pontos de exposicao por minuto, por mais golpes que se troquem.
	/// Sem ele um raio segurado em cima de alguem (cinco batidas por segundo) encheria o medidor em um
	/// minuto. Com ele, despertar SO lutando pede dez janelas -- nove minutos e pouco de luta, o mesmo
	/// tamanho da atmosfera. Os dois caminhos SOMAM: lutar com um deus num mundo divino leva a metade.
	/// </summary>
	public const double TetoDeLutaPorMinuto = 10;

	/// <summary>O tamanho da janela do teto. Ver <see cref="TetoDeLuta"/>.</summary>
	public const double SegundosDoTeto = 60;

	/// <summary>As etapas que o jogador ouve no caminho (alem da chegada e do despertar).</summary>
	public static readonly double[] Etapas = [25, 50, 75];

	/// <summary>
	/// Soma no medidor, com o teto. Devolve quanto de fato entrou.
	///
	/// A PORTA NAO E CONFERIDA AQUI: quem decide se este corpo pode acumular (ainda nao despertou, alcanca
	/// o requisito, nao e um cadaver) e o funil do servidor, `GameServer.Expor` -- e so ele chama isto.
	/// </summary>
	public static double Somar(Fighter f, double quanto)
	{
		double antes = Math.Clamp(f.ExposicaoDivina, 0, ExposicaoCheia);
		double depois = Math.Clamp(antes + Math.Max(quanto, 0), 0, ExposicaoCheia);

		// SEISCENTAS SOMAS DE UM SEXTO NAO DAO CEM: dao 99,99999999999994. Quem chegou a um bilionesimo do
		// fim chegou -- senao o despertar custaria um tique a mais por um residuo de ponto flutuante.
		if (ExposicaoCheia - depois < 1e-9) depois = ExposicaoCheia;

		f.ExposicaoDivina = depois;
		return depois - antes;
	}

	/// <summary>A maior das <see cref="Etapas"/> que este passo atravessou, ou 0 se nao atravessou nenhuma.</summary>
	public static double EtapaCruzada(double antes, double depois)
	{
		double cruzada = 0;
		foreach (double etapa in Etapas)
			if (antes < etapa && depois >= etapa) cruzada = etapa;
		return cruzada;
	}

	// =====================================================================
	// 3. QUEM E "DIVINO" PRO SEGUNDO CAMINHO
	// =====================================================================
	/// <summary>
	/// ESTA FORMA E DIVINA? E a que PEDE ki divino (<see cref="FormaDef.PedeGodKi"/> >= 0): Super Saiyajin
	/// God, Blue, Evolution, as tres do Rose, o Beast, os Limit Breakers e as quatro das disciplinas
	/// (Ultra Instinct e Poder da Destruicao). E o mesmo corte que o catalogo ja usa pra cor do contorno
	/// e da chama -- nao ha lista de ids aqui pra envelhecer quando uma forma nova entrar.
	///
	/// O MISTICO FICA DE FORA: ele e um dom de ritual e nao pede ki divino nenhum (`PedeGodKi = -1`).
	/// </summary>
	public static bool EhFormaDivina(FormaDef? d) => d is { PedeGodKi: >= 0 };

	/// <summary>
	/// ESTE CORPO CARREGA KI DIVINO AGORA? Ja despertou, OU esta numa forma divina -- as duas metades da
	/// frase do dono (*"pessoas q ja tem ki divino ou estao transformadas em formas divinas"*). A segunda
	/// existe por causa de quem chega a uma forma divina sem ter despertado: um chefe de saga com a forma
	/// no roteiro, ou um corpo posto nela pela mao de um admin.
	/// </summary>
	public static bool CarregaKiDivino(Fighter f, string formaAtual) =>
		JaDespertou(f) || EhFormaDivina(Catalogo.Def(formaAtual));

	// =====================================================================
	// 4. O DESPERTAR -- o `get_godki()` do DM, e o unico lugar que liga `awakened`
	// =====================================================================
	/// <summary>A etiqueta do marco de ganho. `bp_milestone_reach("godki")`, `godkiattain.dm:35`.</summary>
	public const string MarcoDoDespertar = "godki";

	/// <summary>`GODKI_KAIDEMON_START_PCT 33` (`1A Defines.dm:56`): o sangue divino ja desperta no nivel do Blue.</summary>
	public const double MaestriaDoSangueDivino = Catalogo.GodkiBluePct;

	/// <summary>
	/// SANGUE DIVINO: Kai e Demonio, de raca ou de pai. E o `if` do `godkiattain.dm:39` (a `Father_Race`
	/// de la nao existe neste port; a `Parent_Race` e a <see cref="Fighter.ParentRace"/>).
	/// </summary>
	public static bool SangueDivino(Fighter f) =>
		f.Race is "Kai" or "Demon" || f.ParentRace is "Kai" or "Demon";

	/// <summary>
	/// O KI DIVINO DESPERTA NESTA FICHA. Idempotente: num corpo ja desperto nao escreve nada e devolve
	/// `Despertou = false`.
	///
	/// ============================ O QUE O ORIGINAL FAZ, LINHA A LINHA (`godkiattain.dm:32-44`) ============================
	///   * `godki.awakened = 1`                       -- aqui. O `GodKiState` nasce neste instante: ate o
	///                                                   despertar a ficha nao tem datum de ki divino nenhum.
	///   * `bp_milestone_reach("godki")`              -- aqui: o marco de GANHO (5x, `Milestones.Tabela`).
	///   * `godki_give_mult = godki_boost`            -- aqui: 2,5x de poder PRA SEMPRE, forma ou nao
	///                                                   (`base.dm:134`; o `Fighter.GodKiFactor` ja o aplicava,
	///                                                   so que ninguem escrevia o campo).
	///   * Primal Saiyan: `godki_give_mult = 0`       -- aqui. No Primal o ki divino so abre o Limit Breaker.
	///   * Kai/Demonio: maestria no nivel do Blue     -- aqui (<see cref="SangueDivino"/>).
	///   * `godki.b_efficiency = godki_mod`           -- NAO VEIO: o port nao tem `godki_mod` (ele e sempre 1, e o
	///                                                   Rose daqui e decidido pela classe -- `GameServer.Desejos.cs`).
	///   * `godki.naturalization = TRUE`              -- NAO VEIO: o port nao tem naturalizacao.
	///   * `enable_visibility(/datum/mastery/Godki)`  -- NAO VEIO: e a linha da aba de maestrias do DM.
	///   * `CheckGodki()` (os verbs `God_Ki`/`Focus`) -- NAO VEIO: aqui as formas divinas sao entradas do
	///                                                   catalogo, e quem as oferece e a tecla C.
	/// ====================================================================================================================
	///
	/// O MEDIDOR FICA CHEIO: quem desperta pela mao de um admin tambem sai com 100, e a aba World nao
	/// mostra "37%" pra um deus.
	/// </summary>
	public static ResultadoDoDespertar Despertar(Fighter f)
	{
		if (JaDespertou(f)) return default;

		f.godki ??= new GodKiState();
		f.godki.awakened = true;
		f.ExposicaoDivina = ExposicaoCheia;

		double marco = f.ReachMilestone(MarcoDoDespertar);
		f.godki_give_mult = f.SaiyanLineage == Oozaru.LinhagemPrimal ? 0 : f.godki_boost;

		bool sangue = SangueDivino(f);
		if (sangue) f.godki.mastery = Math.Max(f.godki.mastery, MaestriaDoSangueDivino);

		return new ResultadoDoDespertar(true, marco, f.godki_give_mult, f.godki.mastery, sangue);
	}

	// =====================================================================
	// 5. A MAESTRIA DIVINA -- o `godki_mastery_tick()` do DM
	// =====================================================================
	/// <summary>
	/// QUANTAS HORAS DE USO levam a maestria divina de 0 a 100. `GODKI_MASTERY_TICK` (`1A Defines.dm:51`):
	/// 0,00052 por volta de ~0,3 s do `GlobalStats`, que o proprio comentario do original fecha em
	/// "~16 HORAS de uso pra 100%". Aqui a conta e por SEGUNDO, entao o numero guardado e o das horas.
	/// </summary>
	public const double HorasDeUsoPraMaestriaCheia = 16;

	/// <summary>`GODKI_MASTERY_COMBAT_MULT 2` (`1A Defines.dm:52`): em combate a maestria rende o dobro.</summary>
	public const double MaestriaEmCombate = 2;

	/// <summary>
	/// OS MARCOS DA MAESTRIA (`godki.dm:133`): 33 abre o Blue, 50 o Evolution/Rose 2/Beast, 70 deixa
	/// aprender as disciplinas divinas, 100 e o fim.
	/// </summary>
	public static readonly double[] MarcosDeMaestria =
		[Catalogo.GodkiBluePct, Catalogo.GodkiRoyalePct, Catalogo.GodkiUiUePct, 100];

	/// <summary>
	/// ESTA FORMA USA O KI DIVINO? E o `godki.usage` do DM, traduzido: la o ki divino se LIGA por um verb
	/// (`God_Ki()`, `godki.dm:189`) e as formas divinas sao o que o Super Saiyajin vira com ele ligado.
	/// Neste port nao ha esse interruptor -- cada forma divina e uma entrada do catalogo --, entao "o ki
	/// divino esta ligado" e "o corpo esta numa forma que o usa":
	///
	///   * a que PEDE ki divino (<see cref="EhFormaDivina"/>);
	///   * a que ESCALA com ele (<see cref="FormaDef.EscalaComGodKi"/>) -- o Mistico do Prodigial, que e o
	///     caminho divino daquela linhagem (`godki.dm:349-352`) e so vale pra origem que a curva nomeia.
	/// </summary>
	public static bool UsaOKiDivino(FormaDef? d, string classe, string linhagem)
	{
		if (d == null) return false;
		if (EhFormaDivina(d)) return true;
		if (d.EscalaComGodKi is not { } curva) return false;
		foreach (string origem in curva.Origens)
			if (string.Equals(origem, classe, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(origem, linhagem, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>
	/// SOBE A MAESTRIA DIVINA por <paramref name="dt"/> segundos de uso. Devolve o maior dos
	/// <see cref="MarcosDeMaestria"/> que este passo atravessou, ou 0.
	///
	/// E o `godki_mastery_tick()` (`godki.dm:123-130`): so de quem despertou, so com o ki divino em uso
	/// (quem decide isso e quem chama), dobrado em combate e parado aos 100.
	/// </summary>
	public static double SubirMaestria(Fighter f, double dt, bool emCombate)
	{
		if (f.godki is not { awakened: true } g || g.mastery >= 100 || dt <= 0) return 0;

		double antes = g.mastery;
		double porSegundo = 100 / (HorasDeUsoPraMaestriaCheia * 3600) * (emCombate ? MaestriaEmCombate : 1);
		g.mastery = Math.Min(antes + porSegundo * dt, 100);

		double cruzado = 0;
		foreach (double marco in MarcosDeMaestria)
			if (antes < marco && g.mastery >= marco) cruzado = marco;
		return cruzado;
	}
}
