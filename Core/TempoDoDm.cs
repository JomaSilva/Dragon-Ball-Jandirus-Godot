namespace Jandirus.Core;

/// <summary>
/// A UNIDADE DE TEMPO DO ORIGINAL -- e o lugar UNICO onde ela esta explicada.
///
/// ============================ O DECISSEGUNDO, E NAO O QUADRO ============================
/// No BYOND, `sleep()`, `spawn()` e `world.time` sao contados em **decissegundos**:
/// <c>sleep(N) = N/10 s</c>. O `world.fps` NAO e a unidade -- ele governa o `tick_lag` (a 12 fps
/// cada tique do agendador vale 10/12 = 0,833 ds), ou seja o GRAO com que o motor acorda uma proc
/// adormecida, e nao a moeda em que o prazo esta escrito.
///
/// Este projeto converteu o tempo do DM ERRADO por muito tempo: havia comentario espalhado por
/// `Cinematicas`, `Oozaru`, `Voo` e `Formas` afirmando que `world.fps = 12` (`Globals/World.dm:5`)
/// fazia `sleep(N)` valer `N/12 s`. Todos os prazos portados por essa regra sairam **20% curtos**,
/// e o defeito se reproduzia sozinho: quem lia o comentario copiava a divisao por 12.
///
/// ============================ AS PROVAS, E ELAS SAO DO PROPRIO DM ============================
/// O original se auto-documenta em oito lugares, e os oito batem com o decimo -- nenhum bate com
/// o doze:
///
/// <code>
///   PlanetPopulation.dm:471   sleep(3000) // every ~5 min             -> 300 s   (/12 daria 250)
///   Area_Death.dm:129         sleep(3100) // five minutes lol         -> 310 s   (/12 daria 258)
///   RankQuests.dm:632         sleep(600)  // 1 min                    ->  60 s   (/12 daria  50)
///   ShipVessel.dm:187         sleep(100)  // 10 seconds               ->  10 s   (/12 daria 8,3)
///   Login.dm:65               spawn(3000) // autosave every 5 mins    -> 300 s
///   stylemobhandler.dm:51     spawn(1000) // every 100 or so seconds  -> 100 s
///   HtmlUI.dm:735             sleep(4)    // ~0.4s                    -> 0,4 s
///   Stats.dm:38               "world.time is deciseconds"             -- textual
/// </code>
///
/// ============================ CADENCIA NAO E `sleep`, E POR ISSO ESTA AQUI TAMBEM ============================
/// O erro irmao -- e o que a multiplicacao por 1,2 NAO conserta -- e converter um CONTADOR de
/// ciclos como se fosse um `sleep`. Quando o DM escreve `angertick--` ou `combatTime++`, o prazo
/// nao esta no numero: esta na cadencia do laco que decrementa. E o original tem **dois** lacos,
/// com cadencias diferentes, e eles se parecem o bastante pra trocar um pelo outro sem perceber:
///
/// <code>
///   mob/proc/Stats()        ... sleep(sleep_tiem)  com sleep_tiem = 2   (Stats.dm:126, :511)
///   mob/proc/GlobalStats()  ... sleep(3)                                (Stats.dm:25,  :67)
/// </code>
///
/// Quem mora em qual muda o resultado em 50%: o dreno de voo (`Stats.dm:411-427`) e do `Stats()`,
/// e o `combatTime` (`Stats.dm:51-53`) e o `BuffLoop()` -- logo todo `Loop()` de buff -- sao do
/// `GlobalStats()`. O `1A Defines.dm:44` confirma o segundo por escrito:
/// <c>LSSJ_RAMP_TICKS 600 //ciclos de GlobalStats (~0.3s) ... (600 = ~3min)</c>.
///
/// ============================ COMO USAR ============================
/// Escreva o numero do DM e DIVIDA -- <c>3000 / TempoDoDm.TiquesPorSegundo</c> --, nunca o segundo
/// ja convertido e nunca "o antigo vezes 1,2". O tique e o que esta no arquivo; o segundo e
/// derivado, e quem for conferir abre o `.dm` e le o mesmo 3000.
///
/// A DIVISAO tambem e o que mantem os prazos em numeros REDONDOS: tique/10 tem exatamente uma casa
/// decimal, entao 250 -> 25,0 e 145 -> 14,5 sem arredondamento nenhum no meio.
/// ==================================================================
/// </summary>
public static class TempoDoDm
{
	/// <summary>
	/// QUANTOS TIQUES DE `sleep`/`spawn`/`world.time` CABEM NUM SEGUNDO: <b>10</b>. E o divisor
	/// canonico do porte -- ver o sumario da classe pras oito provas no proprio DM.
	/// </summary>
	public const double TiquesPorSegundo = 10.0;

	/// <summary>
	/// A CADENCIA DO `Stats()` -- `sleep(2)` (`Stats.dm:126` declara, `:511` dorme): 0,2 s, 5 Hz.
	///
	/// E o laco do CORPO: dreno de voo, nado, camara do tempo, ranking de BP. Todo custo que o DM
	/// escreve "por tique" dentro dele vira custo por segundo dividindo por isto.
	/// </summary>
	public const double SegundosDoLacoStats = 2 / TiquesPorSegundo;

	/// <summary>
	/// A CADENCIA DO `GlobalStats()` -- `sleep(3)` (`Stats.dm:25` abre, `:67` dorme): 0,3 s.
	///
	/// E o laco do ESTADO: `powerlevel()`, `statify()`, `combatTime`, `BuffLoop()`. Todo contador
	/// que um `Loop()` de buff decrementa anda nesta cadencia, e nao na do <see cref="SegundosDoLacoStats"/>.
	///
	/// (O `if(isNPC) sleep(2)` de `Stats.dm:65-66` poe o NPC em 0,5 s. Quem se porta e o prazo do
	/// JOGADOR -- os prazos deste projeto sao sentidos por quem joga.)
	/// </summary>
	public const double SegundosDoLacoGlobalStats = 3 / TiquesPorSegundo;

	/// <summary>QUANTOS TIQUES DE `sleep` CADA VOLTA DO `GlobalStats()` GASTA. Ver acima.</summary>
	public const int TiquesDoLacoGlobalStats = 3;
}
