using System.Text.Json.Serialization;

namespace Jandirus.Core.Stats;

/// <summary>
/// **A JANELA DO TIRO** -- o `minuteshot` do `Blast_Gain` (`combatgains.dm:163-205`, portado no <see cref="BlastGain"/>): o tiro que
/// acerta fora da janela rende metade e a ABRE; dentro dela cada tiro rende menos que o anterior (`0.18 / log(1.3, contagem)`); e 45 s
/// depois ela fecha (`spawn(450) minuteshot=0`, `combatgains.dm:191`) e o proximo tiro volta a ser o primeiro.
///
/// ============================ ELA NUNCA FECHAVA (dono, 2026-10-24: "Consertar como no DM") ============================
/// O port tinha trazido a conta do `Blast_Gain` e nao o `spawn(450)`: depois do PRIMEIRO tiro da vida a janela ficava aberta pra sempre,
/// e a contagem (`minuteshot_ig_ki`, `tmp` no DM -- `attack_bck.dm:5-8`) ia pro save com a ficha (`IncludeFields`) e so crescia, de
/// sessao em sessao: o 2o tiro pagava x0,043, o 100o x0,0089. Era o "ganho de ki mt baixo" do relato. Agora:
///   * a janela FECHA no tique da ficha (<see cref="FecharAJanelaDoTiro"/>, que o <see cref="Tick"/> chama com o relogio do servidor),
///     45 s depois de aberta -- o prazo comeca no primeiro tique depois do tiro que a abriu (ate 0,2 s de folga, o passo do tique de
///     5 Hz; o `ignoreMinuteshot`, que nenhuma porta do port usa, reabre a contagem sem mexer no prazo, como o `spawn` mais antigo do DM
///     fechando primeiro);
///   * a janela, a contagem e o prazo NAO vao pro save (`JsonIgnore`: o `tmp` do DM) -- o save velho que traz a contagem alta entra com
///     ela zerada, e quem loga entra com a janela fechada.
/// O `BufferTick` le a mesma janela (`Stats.dm:163`: com ela aberta, o acumulador de quem nao ganha enche sem esperar o `Gaintimer`) --
/// presa, ele enchia assim pra sempre; fechando, volta a esperar como no DM.
/// =====================================================================================================================
/// </summary>
public sealed partial class Fighter
{
	/// <summary>Quanto a janela do tiro fica aberta: `spawn(450)` (`combatgains.dm:191`) -- 450 decissegundos, 45 s de relogio.</summary>
	public const long JanelaDoTiroMs = 45_000;

	/// <summary>A janela do tiro aberta (o `minuteshot` do DM, `tmp`): o proximo tiro paga o detrator. FORA DO SAVE.</summary>
	[JsonIgnore] public bool minuteshot;

	/// <summary>A contagem de tiros da janela (o `minuteshot_ig_ki` do DM, `tmp`): 1 no tiro que a abre, +2 por tiro depois. FORA DO SAVE.</summary>
	[JsonIgnore] public int minuteshot_ig_ki;

	/// <summary>Quando a janela fecha, em ms do relogio do servidor; 0 = o prazo ainda nao foi marcado (o tique seguinte ao tiro marca). FORA DO SAVE.</summary>
	[JsonIgnore] public long minuteshotFechaEm;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--vigortreinoteste`): a janela do tiro volta a nunca fechar -- o jogo de antes de 2026-10-24. Lido por
	/// <see cref="FecharAJanelaDoTiro"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool JanelaDoTiroNaoFechaDeTeste;

	/// <summary>
	/// O `spawn(450) minuteshot=0`: marca o prazo no primeiro tique depois do tiro que abriu a janela, e a fecha quando ele vence. Sem
	/// relogio (`agoraMs` 0 -- as contas que tiquam a ficha sem tempo, nas bancadas e nas ferramentas) nao mexe em nada.
	/// </summary>
	public void FecharAJanelaDoTiro(long agoraMs)
	{
		if (agoraMs <= 0 || !minuteshot || JanelaDoTiroNaoFechaDeTeste) return;
		if (minuteshotFechaEm == 0) { minuteshotFechaEm = agoraMs + JanelaDoTiroMs; return; }
		if (agoraMs < minuteshotFechaEm) return;
		minuteshot = false;
		minuteshotFechaEm = 0;
	}
}
