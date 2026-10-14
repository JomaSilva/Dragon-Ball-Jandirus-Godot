using Jandirus.Core.Forms;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// ============================ OS DOIS DEGRAUS DA RAIVA POR AMIGO -- REGRA DO DONO (2026-10-11) ============================
/// As palavras dele, em duas mensagens:
///
///   *"ao ver um amigo morrer ou ser absorvido o personagem ele fica com o mais alto nivel de raiva (...) e alem
///   disso amigo quando e nocauteado vc n fica com o mais alto nivel de raiva, vc fica angry apenas, o very angry e
///   so em morte e absorcao. e outro detalhe caso o usuario ja tenha um certo nivel de ki control etc e ele fique
///   com extreme anger, instantaneamente o ki dele sobe pro maximo q ele pode carregar, entao se ele pode no maximo
///   140% de ki, nao importa onde o ki dele esta, ele vai pra 140% na pura raiva, raiva comum n acontece isso"*
///   -- e logo depois: *"considere very angry = extreme anger"*.
///
/// SAO DOIS DEGRAUS, e o enum deles ja existia (<see cref="NivelDeRaiva"/>, que nasceu pra PORTA das formas):
///
///   * <see cref="NivelDeRaiva.Comum"/> -- a RAIVA COMUM, o "Angry" do DM: um amigo foi NOCAUTEADO;
///   * <see cref="NivelDeRaiva.Extrema"/>  -- o degrau MAIS ALTO, o "Very Angry" do DM: um amigo MORREU ou foi ABSORVIDO.
///
/// Este arquivo guarda o que cada degrau VALE em numero (o `Anger` do DM, que vira `angerBuff` e BP) e a regra que
/// so o de cima tem: o SALTO DE KI. Quem acende os degraus e o servidor (`GameServer.AmigoAbatido`); quem decide
/// quem e amigo e o `Core.Social.Convivio`, pela declaracao do jogador.
///
/// ============================ ONDE ISTO DIVERGE DO DM, E POR QUE ============================
/// No original os dois gatilhos escrevem o MESMO numero: `Do_Anger_Stuff(extreme)` faz `Anger = max(Anger,
/// MaxAnger)` com 0 ou com 1 (`Murder.dm:110-114`), e o `KO.dm:38` ate anuncia *"has become very angry"*. La o
/// `extreme` so decide a cinematica. O dono pediu o contrario, por escrito: nocaute e "angry apenas". A regua de
/// humor do DM tem os dois nomes (`Stats.dm:448-449`: "Angry" a partir de `100 + folga/1,66`, "Very Angry" a partir
/// de `100 + folga/1,25`), entao o que mudou foi so qual gatilho cai em qual degrau dela.
///
/// O QUE NAO MUDOU: a duracao (120 s, `rageExpire`, `Murder.dm:113`), o gotejamento (`Stats.dm:443`) e o teto do
/// buff (`base.dm:88`).
///
/// E O QUE CADA DEGRAU ABRE: as formas de raiva vem com os DOIS, e a comum cobra mais poder por elas -- o
/// `if("Angry")` do `switch(savant.Emotion)` (`supersaiyan.dm:112`), que so faz sentido porque a raiva comum daqui
/// vale justamente o "Angry" de la. Ver <see cref="NivelDeRaiva"/> e `FormaDef.AgioNaRaivaComum`.
/// O *"second wind"* de `KO.dm:19-26` esta COMENTADO no original (codigo morto la) e era de quem CAI, nao de quem
/// assiste: nao ha o que portar.
///
/// ============================ E A SKILL `LEGENDARY ANGER` (pergunta do dono, 2026-10-13) ============================
/// *"lendario saiyajin tem como ter a skill raiva lendaria e com isso o multiplicador de raiva dele em todos os niveis
/// (extreme e normal) é maior"*. E, e este arquivo nao precisa de linha nenhuma pra isso: a skill soma 100 no
/// `MaxAnger` (`saiyan.dm:50` + `master.dm:180`, no `Fighter.Statify`) e sobe o teto do buff de 2x pra 3x
/// (`base.dm:88`, no `Fighter.Power`), e os DOIS degraus daqui saem do `MaxAnger` (<see cref="Topo"/>). O +100 entra
/// inteiro na extrema (+1,0x) e dividido por <see cref="DivisorDaRaivaComum"/> na comum (+0,77x): num corpo no piso
/// de fabrica (`MaxAnger` 120) a extrema vai de 1,20x pra 2,20x e a comum de 1,15x pra 1,92x. Medido no corpo vivo,
/// com a skill aprendida pelo canal do jogo, pela `--formasteste` (`GameServer.ALegendaryAngerNosDoisDegraus`).
/// ==============================================================================================================
/// </summary>
public static class RaivaEmDegraus
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `--raivateste` e `raiva`): o nocaute de um amigo volta a valer o TOPO da raiva
	/// (`MaxAnger`), como a morte -- o que este port fez ate 2026-10-11 e o que o DM faz. Lido pelo <see cref="Topo"/>.
	/// </summary>
	public static bool NocauteNoTopoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--raivateste` e `raiva`): a furia extrema nao mexe no Ki de ninguem -- o estado
	/// de antes de 2026-10-11. Lido pelo <see cref="SaltaOKi"/>.
	/// </summary>
	public static bool SemSaltoDeKiDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--raivateste` e `raiva`): a raiva COMUM tambem leva o Ki ao teto -- o avesso do
	/// *"raiva comum n acontece isso"* do dono. Lido pelo <see cref="SaltaOKi"/>.
	/// </summary>
	public static bool SaltoNaRaivaComumDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--formasteste`, secao `ALegendaryAngerNosDoisDegraus`): o bonus da skill Legendary
	/// Anger fica so no TETO do buff (`Fighter.Power`: `2 + legendaryAngerBonus/100`) e NAO entra no `MaxAnger` -- os
	/// dois degraus deixam de subir com ela, que e como a skill estaria "desatualizada" com a raiva em degraus. Lido
	/// pelo `Fighter.Statify`. Falso em jogo, sempre.
	/// </summary>
	public static bool LegendaryAngerSoNoTetoDeTeste;

	// =====================================================================
	// 1. O NUMERO DE CADA DEGRAU
	// =====================================================================

	/// <summary>
	/// POR QUANTO SE DIVIDE A FOLGA DE RAIVA NO DEGRAU COMUM: `Anger = ((MaxAnger-100)/1.30)+100`.
	///
	/// E o numero que o proprio DM usa pra POR alguem em "Angry": o verb `Anger Me`, opcao "Angry" (`Voting.dm:40`).
	/// Cai no meio da faixa "Angry" da regua (`Stats.dm:448-449`, entre `/1,66` e `/1,25`) e, com o gotejamento
	/// de dois minutos por cima, continua dentro dela ate o prazo vencer (perde `folga*120/1600` = 7,5% da folga).
	/// O limiar seco (`/1,66`, que o `mst_ignite_anger` usa em `MasterStudent.dm:406`) cairia pra "Slightly Angry"
	/// no primeiro tique -- e por isso la o `Emotion` e cravado a mao na linha seguinte.
	///
	/// AJUSTE DO DONO: quanto MAIOR o divisor, mais fraca a raiva comum (1,0 = igual a extrema).
	/// </summary>
	public const double DivisorDaRaivaComum = 1.30;

	/// <summary>
	/// O NUMERO DA RAIVA NO INSTANTE EM QUE O DEGRAU ACENDE (o `Anger` do DM; 100 = calmo = 1,0x de BP).
	///
	///   * extrema: `MaxAnger` inteiro (`Murder.dm:112`);
	///   * comum:   `100 + (MaxAnger - 100) / DivisorDaRaivaComum`;
	///   * nenhuma: 100.
	///
	/// O gotejamento por cima (`Stats.dm:443`) e do relogio, e quem o aplica e o servidor (`RaivaComoNumero`).
	/// </summary>
	public static double Topo(NivelDeRaiva grau, double maxAnger)
	{
		double folga = Math.Max(0, maxAnger - 100);
		return grau switch
		{
			NivelDeRaiva.Extrema => 100 + folga,
			NivelDeRaiva.Comum => NocauteNoTopoDeTeste ? 100 + folga : 100 + folga / DivisorDaRaivaComum,
			_ => 100,
		};
	}

	// =====================================================================
	// 2. O SALTO DE KI -- so do degrau mais alto
	// =====================================================================

	/// <summary>
	/// A QUE FRACAO DO TETO DE CARGA O KI SALTA. Um = o teto inteiro (*"se ele pode no maximo 140% de ki ... ele vai
	/// pra 140%"*). AJUSTE DO DONO.
	/// </summary>
	public const double FracaoDoTetoNoSalto = 1.0;

	/// <summary>
	/// O "CERTO NIVEL DE KI CONTROL" QUE O SALTO PEDE: este corpo CONSEGUE CARREGAR ACIMA DE 100% com a tecla C.
	///
	/// DECISAO DO PORT, DECLARADA: o DM nao tem regra nenhuma de raiva mexendo em Ki (`Do_Anger_Stuff`,
	/// `Murder.dm:110-119`, so escreve `Anger`, `rageExpire` e `StoredAnger`), entao nao ha limiar de la pra copiar.
	/// O criterio e o que o proprio texto do dono descreve -- *"o maximo q ele pode carregar"* --: as duas chaves da
	/// carga (`MeditateGivesKiRegen`, o Ki Unlocked de `Mind.dm:79`, e `canPower`, o Basic Ki Control nivel 5 de
	/// `Mind.dm:281`, sem o qual o `Energy_Draw` de `Power Control.dm:148` nao passa do tanque) e o ki nao estar
	/// travado (`fd_ki_locked`, `Power Control.dm:141`: o Frost mutante descontrolado nao reune nada).
	///
	/// Quem quiser um limiar mais alto (um nivel de skill, um minimo de `powerupcap`) muda SO esta funcao.
	/// </summary>
	public static bool TemOControleDeKiExigido(Fighter f) =>
		CargaDeKi.SabeReunir(f) && CargaDeKi.TemControle(f) && !f.fd_ki_locked;

	/// <summary>
	/// ESTE DEGRAU, NESTE CORPO, LEVA O KI AO TETO? So a furia EXTREMA, e so pra quem tem o controle
	/// (<see cref="TemOControleDeKiExigido"/>). A raiva comum, nunca.
	/// </summary>
	public static bool SaltaOKi(NivelDeRaiva grau, Fighter f) =>
		!SemSaltoDeKiDeTeste
		&& (grau == NivelDeRaiva.Extrema || (SaltoNaRaivaComumDeTeste && grau == NivelDeRaiva.Comum))
		&& TemOControleDeKiExigido(f);

	/// <summary>
	/// PRA ONDE O KI VAI, em unidades absolutas: o MESMO teto da tecla C (<see cref="CargaDeKi.TetoDeCarga"/>, o
	/// `MaxKi * powerupcap` de `Power Control.dm:158`). De fabrica 140% do tanque; com as skills de power-up, mais.
	/// </summary>
	public static double KiDoSalto(Fighter f) => CargaDeKi.TetoDeCarga(f) * FracaoDoTetoNoSalto;

	/// <summary>
	/// O SALTO. Devolve TRUE quando o Ki SUBIU (e so entao ha o que avisar e o que recalcular).
	///
	/// NUNCA DESCE: quem ja esta acima do teto de carga (o Kaio-ken e as tecnicas levam o Ki alem dele) fica onde
	/// esta -- *"nao importa onde o ki dele esta"* fala de quem esta ABAIXO.
	///
	/// E O `overcharge` VAI JUNTO: com as skills de power-up o teto de carga pode passar do teto SEGURO
	/// (`kicapacity`), e ali o excesso machuca (`CargaDeKi.PrecoDoExcesso`, `Power Control.dm:118-125`). O Ki que a
	/// furia DA nao pode nocautear o enfurecido: e a mesma chave que o DM liga quando da Ki de presente
	/// (`MajinSaga.dm:221-222`: `Ki = MaxKi; overcharge = 1`) -- ela segura o dano ate o Ki voltar pra baixo do
	/// teto seguro, e so. O vazamento continua (desde 2026-10-13 pela taxa que cresce com o excesso,
	/// `CargaDeKi.VazamentoPorSegundo`).
	/// </summary>
	public static bool SaltarOKi(NivelDeRaiva grau, Fighter f)
	{
		if (!SaltaOKi(grau, f)) return false;

		double alvo = KiDoSalto(f);
		if (f.Ki >= alvo) return false;

		f.Ki = alvo;
		f.overcharge = true;
		return true;
	}
}
