using Jandirus.Core.Races;

namespace Jandirus.Core.Forms;

/// <summary>
/// ============================ O SUPER MAJIN -- A FORMA DO MAJIN QUE NAO E CORRUPTED (dono, 2026-10-23) ============================
/// O pedido: *"dar a forma super majin melhorada pros majins q nao sao corrupted"*, e a regra, palavra por palavra: *"faça ela ter uma
/// maestria bem lenta ao ponto q ela possa chegar no ssj3, mesma ideia do future super saiyan, mas a maestria dela trava com base no bp
/// atual do majin, se ele ta nivel ssj1 de bp, essa forma vai ater 6x, se ele chegar nivel ssj2 ela vai ate 10x, se ele chegar no bp
/// proximo das formas de ssj3 ele consegue masterizar 100% e virar 16x"*. O visual, escolhido por ele: o corpo de sempre (a folha e a
/// cor que o jogador escolheu) e a aura ROSA com as faiscas do `Electric_Majin`.
///
/// AQUI MORA SO A REGRA E OS NUMEROS -- o Core nao conhece o Godot nem o servidor. A ENTRADA e o "super_majin" do `Catalogo.Todas`, a
/// linha e a <see cref="LinhaDeForma.SuperMajin"/>, a escada se abre no `Catalogo.AbrirLinhas` (pelo <see cref="AbreALinha"/>) e quem
/// trava a maestria em jogo e o funil de maestria do servidor (`GameServer.SubirMaestriaDaZona`, que chama o
/// `GameServer.SubirPeloPoder` de `GameServer.SuperMajin.cs`).
///
/// ============================ O QUE NAO E O DM ============================
/// No DM o "Super Majin" do Majin comum era uma SKILL de buff (`majin.dm:96-147`: +1,2 de tecnica, ki e velocidade; depois da Ascensao,
/// 3x e o Ki em dobro), e ela foi APOSENTADA em 2026-10-18 (`SkillsAposentadas`). Isto e outra coisa e nao a ressuscita: e uma FORMA, no
/// molde do Future Super Saiyajin (`future_ssj`, `Catalogo.Todas`). A unica peca do DM que vem junto e a ARTE das faiscas --
/// `Electric_Majin.dmi`, o `MajinEffect` (`Majin.dm:20-23`) que o buff `Majin` do DM colava no corpo.
/// ======================================================================================================================
/// </summary>
public static class SuperMajin
{
	/// <summary>O id da forma no catalogo -- e a chave da maestria dela.</summary>
	public const string Id = "super_majin";

	/// <summary>O numero de rede e de save. 870 estava livre (860/861 sao a Forma Pura do Corrupted, 900+ a Tensao).</summary>
	public const ushort IdRede = 870;

	/// <summary>
	/// A RACA, e so ela -- comparacao EXATA. O meio-Majin (raca de outro pai com a `ParentRace` "Majin", que o
	/// `Combat.AbsorcaoMajin.EhMajin` aceita) fica DE FORA: o dono disse "majins", e categoria nomeada nao se estende ao parente.
	/// </summary>
	public const string Raca = "Majin";

	// ============================ A CURVA -- A IDEIA DO FUTURE SUPER SAIYAJIN ============================
	// O Future e `min(2 + round(mastery/10) * 2, 20)`: 2x, e +2x a cada degrau de maestria. Aqui sao os mesmos degraus de 2x, so que
	// OITO e nao dez, porque o topo do dono e 16x (*"masterizar 100% e virar 16x"*), e o ultimo degrau so liga aos 100% cravados.
	//
	// OS LIMIARES SAO ESCOLHA MINHA, e o que os decide sao os dois tetos do dono: 6x e 10x tem de cair num limiar, e os tres trechos da
	// barra ficam quase iguais -- 0..30% no nivel do SSJ1, 30..60% no do SSJ2 e 60..100% no do SSJ3. Um degrau a cada 15%, e o ultimo
	// aos 100 (o "100% = 16x" e literal).
	// ======================================================================================================
	/// <summary>Os oito degraus: 2x a 16x, +2x por degrau (o passo do Future).</summary>
	public static readonly double[] Mult = [2, 4, 6, 8, 10, 12, 14, 16];

	/// <summary>Em que maestria cada degrau liga. Ver o bloco de cima.</summary>
	public static readonly double[] Limiares = [0, 15, 30, 45, 60, 75, 90, 100];

	/// <summary>O dreno do Future (`future_ssj`), sem tirar nem por: o dono pediu "a mesma ideia", e o resto do desenho e o dele.</summary>
	public static readonly double[] Dreno = [0.025, 0.02, 0.015];

	/// <summary>
	/// O TETO NO NIVEL DO SUPER SAIYAJIN, em MULTIPLICADOR (o numero do dono: *"nivel ssj1 de bp, essa forma vai ate 6x"*). A maestria
	/// para no limiar em que este degrau liga (30%). Vale tambem abaixo do nivel do SSJ1 -- o unico jeito de estar na forma com menos e um
	/// mestre ter cortado a porta pela metade (ou o BP base ter descido) --, porque ninguem na forma tem teto menor que o primeiro.
	/// </summary>
	public const double TetoNoNivelDoSsj1 = 6;

	/// <summary>O TETO NO NIVEL DO SUPER SAIYAJIN 2: *"se ele chegar nivel ssj2 ela vai ate 10x"* (60%).</summary>
	public const double TetoNoNivelDoSsj2 = 10;

	/// <summary>
	/// ============================ QUANTO MAIS DEVAGAR QUE UMA FORMA COMUM ============================
	/// *"uma maestria bem lenta ao ponto q ela possa chegar no ssj3"*: a forma acompanha o Majin do nivel do SSJ1 ate o do SSJ3, e e isso
	/// que o ritmo tem de cobrir. DEZ VEZES mais lenta que a escada (<see cref="Catalogo.MaestriaPorSegundo"/>, as ~3 h do SSJ1): 30 h
	/// DENTRO da forma de 0 a 100% -- 9 h ate o teto do SSJ1 (30%, 6x), mais 9 h ate o do SSJ2 (60%, 10x) e 12 h ate os 100%. O SSJ1 chega
	/// aos mesmos 6x em 3 h. ESCOLHA MINHA o numero; a Sala do Tempo (4x) e o fator do corpo continuam valendo por cima (o funil de zona).
	/// ==============================================================================================
	/// </summary>
	public const double LentidaoDaMaestria = 10;

	/// <summary>A maestria por segundo dentro da forma -- a da escada dividida pela <see cref="LentidaoDaMaestria"/>.</summary>
	public const double MaestriaPorSegundo = Catalogo.MaestriaPorSegundo / LentidaoDaMaestria;

	/// <summary>
	/// A AURA ROSA (o visual escolhido pelo dono). A chama colorivel (`FolhaDeAura.Base`) desenha no alfa e pinta com esta cor, entao ela
	/// sai exatamente neste rosa. ESCOLHA MINHA o tom: rosa vivo, e NAO o `ff70d0` da Forma Pura do Corrupted -- quem ve de longe nao pode
	/// confundir as duas. O contorno e a cor da faisca de cena saem dela tambem (o `_` do `Catalogo.ParDoContorno` e do `CorDosRaios`).
	/// </summary>
	public const string AuraRosa = "ff6ec7";

	// =====================================================================
	// QUEM TEM A LINHA
	// =====================================================================
	/// <summary>
	/// O MAJIN QUE NAO E CORRUPTED TEM A ESCADA -- a pergunta do `Catalogo.AbrirLinhas`. O Corrupted fica sem ela, e na LINHA (e nao num
	/// `ProibidoParaClasse` da entrada): a escada dele e a SAGA (`Races.SagaDoMajin`), e sem a linha ele continua ouvindo no C e no X o
	/// que sempre ouviu ("sua raca nao tem essa escada"). Com a proibicao so na entrada a linha abriria pra ele e a frase mudaria.
	/// </summary>
	public static bool AbreALinha(string? raca, string? classe) =>
		string.Equals(raca, Raca, StringComparison.OrdinalIgnoreCase)
		&& (CorruptedTambemDeTeste || !string.Equals(classe, SagaDoMajin.Classe, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// DEFEITO INJETADO (bancadas `pformas` e `--supermajinteste`): o Corrupted Majin tambem ganha a linha -- a regra sem o "nao Corrupted".
	/// Lido pela propria <see cref="AbreALinha"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CorruptedTambemDeTeste;

	// =====================================================================
	// A TRAVA PELO PODER
	// =====================================================================
	/// <summary>
	/// ============================ ATE ONDE A MAESTRIA SOBE COM O BP BASE DE AGORA ============================
	/// Devolve a maestria (0-100) em que a barra para: o limiar do degrau de 6x no nivel do SSJ1, o do de 10x no nivel do SSJ2, e 100 no
	/// nivel do SSJ3. O BP e o BASE, como toda porta deste jogo (o cabecalho do <see cref="Catalogo"/>): fusao, absorcao e o emprestado
	/// somam FORA dele e nao contam.
	///
	/// "NIVEL" E A PORTA DE CADA SUPER SAIYAJIN, perguntada ao funil de portas deste corpo (<see cref="EstadoDeForma.PortaDeBp"/>: o
	/// limiar pessoal, ou o de fabrica -- o Majin nao sorteia nenhum, entao hoje sao 583 milhoes pro SSJ2 e 2 bilhoes pro SSJ3). O "bp
	/// proximo das formas de ssj3" do dono e a PORTA do SSJ3 -- ESCOLHA MINHA o ponto exato, a mesma conta do `SagaDoMajin.BpDaFormaPura`
	/// ("minimo de SSJ3"). A entrada da forma e a porta do SSJ1 (a da propria entrada, `ssjat`).
	///
	/// ============================ E SE O BP DESCER? NADA DESCE ============================
	/// ESCOLHA MINHA, e o dono decide se quiser outra: o teto so TRAVA a subida. A maestria ja ganha fica, e o multiplicador continua o da
	/// maestria (ele nao le BP) -- o BP base so desce esquecendo uma skill (o `before_forget` devolve o que a compra somou) ou por admin, e
	/// nenhuma maestria deste jogo desce. O que muda com o BP de volta ao nivel de baixo e so que a barra nao sobe mais ate ele voltar.
	/// ======================================================================================================
	/// </summary>
	public static double TetoDaMaestria(double bpBase, EstadoDeForma est)
	{
		if (Catalogo.Def(Id) is not { } d) return 100;
		if (bpBase >= NivelDe("ssj3", est)) return 100;
		return LimiarDe(d, bpBase >= NivelDe("ssj2", est) ? TetoNoNivelDoSsj2 : TetoNoNivelDoSsj1);
	}

	/// <summary>A porta de BP de uma forma de referencia PRA ESTE CORPO (o limiar pessoal ou o de fabrica). Sem a forma: inalcancavel.</summary>
	private static double NivelDe(string forma, EstadoDeForma est) =>
		Catalogo.Def(forma) is { } f ? est.PortaDeBp(f) : double.MaxValue;

	/// <summary>A maestria em que o degrau de <paramref name="mult"/> liga -- o primeiro que chega nele. Nenhum chega: 100.</summary>
	public static double LimiarDe(FormaDef d, double mult)
	{
		for (int k = 0; k < d.Mult.Length; k++)
			if (d.Mult[k] >= mult)
				return d.Limiares != null && k < d.Limiares.Length ? d.Limiares[k] : (k + 1) * 100.0 / d.Mult.Length;
		return 100;
	}

	/// <summary>
	/// ============================ O GANHO DE UM TIQUE, COM A LENTIDAO E A TRAVA ============================
	/// <paramref name="ganhoComum"/> e o que o funil de maestria daria a uma forma comum neste tique (a taxa, o tempo, a zona, o corpo).
	/// Pra qualquer outra forma ele volta intacto; pra esta ele sai dividido pela <see cref="LentidaoDaMaestria"/> e nao passa do
	/// <paramref name="teto"/> -- e nunca e negativo: o teto so trava a SUBIDA (ver "nada desce" no <see cref="TetoDaMaestria"/>).
	///
	/// <c>NoTeto</c> e verdadeiro so no tique em que a barra ENCOSTA num teto abaixo de 100, e ai quem chama CRAVA a maestria no teto em
	/// vez de somar o ganho: `antes + (teto - antes)` em ponto flutuante pode parar um fio ABAIXO do limiar (29,999...), e o degrau de 6x
	/// nao ligaria. E e o tique em que o servidor avisa o jogador -- uma vez, porque encostada a barra nao sobe mais. Nos 100 nao ha o que
	/// cortar: o `Maestrias.Por` ja crava o fim da barra, e o aviso e o de sempre ("forma DOMINADA").
	/// </summary>
	public static (double Ganho, bool NoTeto) Ganho(string? id, double antes, double ganhoComum, double teto)
	{
		if (id != Id || ganhoComum <= 0) return (ganhoComum, false);
		double lento = NaTaxaComumDeTeste ? ganhoComum : ganhoComum / LentidaoDaMaestria;
		if (SemTravaDeTeste || teto >= 100) return (lento, false);
		if (antes >= teto) return (0, false);
		return lento < teto - antes ? (lento, false) : (teto - antes, true);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancadas `pformas`, `--supermajinteste` e `--diagsupermajin`): a maestria sobe sem o teto do poder -- a regra sem a
	/// trava. Lido pelo proprio <see cref="Ganho"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SemTravaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `pformas` e `--supermajinteste`): a maestria sobe no ritmo de uma forma comum -- a regra sem a lentidao.
	/// Lido pelo proprio <see cref="Ganho"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool NaTaxaComumDeTeste;

	// =====================================================================
	// O QUE O JOGADOR LE
	// =====================================================================
	/// <summary>
	/// O MARCO DO TETO: o que o servidor diz quando a barra encosta no teto do poder (depois do nome da forma, como os outros marcos de
	/// maestria: "Super Majin: ..."). Sem o BP, que e segredo de scouter: so a barra que o jogador ja ve na aba.
	/// </summary>
	public static string MarcoDoTeto(double maestria) =>
		$"a maestria parou em {maestria:0}%, no teto que o seu poder segura agora -- ela volta a subir quando você ficar mais forte";

	/// <summary>
	/// A NOTA DO CARTAO da aba Forms: a regra inteira, sem numero de BP (o jogador ve a barra parar e le por que). Os tres numeros saem
	/// das constantes, e nao do texto, pra nota nao mentir no dia em que alguem mexer nelas.
	/// </summary>
	public static string NotaDaTrava =>
		$"Sobe bem devagar, e o seu poder base trava a maestria: até ×{TetoNoNivelDoSsj1:0} no nível do Super Saiyajin, até "
		+ $"×{TetoNoNivelDoSsj2:0} no do Super Saiyajin 2, e só perto do Super Saiyajin 3 ela chega a 100% (×{Mult[^1]:0}).";
}
