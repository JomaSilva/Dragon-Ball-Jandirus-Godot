namespace Jandirus.Core.World;

/// <summary>
/// O CALENDARIO DO MUNDO (dono, 2026-10-17) -- o `WorldClock.dm` do original, que o port nao tinha. O pedido: *"a idade avança quando
/// apssar 1 ano ingame, se nao me engano no dm o ano eram 10 meses de 30 dias ingame algo assim e tinhas os meses e o ano"*.
///
/// ============================ OS NUMEROS E OS NOMES SAO OS DO DM ============================
///   * o MES tem 28 dias -- `if(Days>=29)` (`WorldClock.dm:19`): quatro semanas de sete dias, com os nomes de `DayNames` (`:79`). O
///     dono lembrava de "30 dias algo assim"; o DM conta 28, e o port segue o DM;
///   * o ANO tem 10 meses -- `if(Month>=11)` (`:24`), com os nomes de `MonthNames` (`:84`): 280 dias. O `Year` do DM anda 0,1 por mes
///     (`:27`), e o `round(Year)` e o "Age N" da frase dele (`:112`);
///   * um mundo novo comeca no 1 de Luty do ano 1 (`var/Month = 1`, `var/Year = 1`, `:82-83`).
///
/// ============================ O DIA E O DA TERRA, E VIRA NA MEIA-NOITE DELA ============================
/// No DM havia UM relogio e todo planeta obedecia a ele; aqui cada mundo tem o proprio dia (`RelogioDoPlaneta`, pedido do dono), e o
/// calendario conta os dias da TERRA -- a regua do universo (`Ceu`), a mesma que o Time Store e o mes do Bills ja usavam. A data vira
/// na meia-noite da Terra, como o `Hours >= 24` do DM (`:16-18`), e quem esta em Vegeta ve a mesma data.
///
/// UM DIA DO JOGO SAO 24 MINUTOS REAIS (`Ceu.SegundosPorDia`; no DM, `DAY_REAL_MINUTES = 20`, `1A Defines.dm:4`): o mes dura 11,2 h
/// e o ano 112 h (4 dias e 16 horas) de relogio de parede.
///
/// ============================ A ORIGEM E DO MUNDO ============================
/// O 1 de Luty do ano 1 e o dia da Terra em que este mundo nasceu -- o arquivo `Year` do DM (`Aging.dm:1-40`), que a limpeza apaga
/// (`SettingsDatum.dm:225`). Quem guarda a origem e o servidor (`calendario.json`); a conta daqui e pura, e o cliente a refaz igual
/// com a origem que chega no `S2C.Ceu`.
/// ============================================================================================
/// </summary>
public static class Calendario
{
	public const int DiasPorSemana = 7;
	public const int DiasPorMes = 28;
	public const int MesesPorAno = 10;
	public const int DiasPorAno = DiasPorMes * MesesPorAno;

	/// <summary>Os dias da semana do DM, na ordem (`DayNames`, `WorldClock.dm:79`).</summary>
	public static readonly string[] DiasDaSemana = ["Suntag", "Veindin", "Thordin", "Kaidin", "Dedin", "Fradin", "Sondin"];

	/// <summary>Os meses do DM, na ordem (`MonthNames`, `WorldClock.dm:84`).</summary>
	public static readonly string[] Meses =
		["Luty", "Sakavik", "Frieze", "Saiya", "Augustus", "Plant", "Yule", "Propositus", "Zenkin", "Alacritas"];

	/// <summary>QUE DIA DA TERRA E ESTE INSTANTE: a parte inteira do dia local dela (a fracao e a hora; zero e a meia-noite).</summary>
	public static long DiaDaTerra(RelogioDoPlaneta terra, double segundosDoMundo) =>
		(long)Math.Floor(terra.DiaLocal(segundosDoMundo));

	/// <summary>O MES CORRIDO de um dia do calendario: 0 e Luty do ano 1, 10 e Luty do ano 2. Antes da origem vale o primeiro.</summary>
	public static long MesDe(long dias) => Math.Max(0, dias) / DiasPorMes;

	/// <summary>A DATA de um dia do calendario (0 = o primeiro dia do mundo). Antes da origem vale o primeiro dia.</summary>
	public static DataDoCalendario De(long dias)
	{
		long d = Math.Max(0, dias);
		return new DataDoCalendario(d, (int)(d / DiasPorAno) + 1, (int)(d % DiasPorAno / DiasPorMes) + 1, (int)(d % DiasPorMes) + 1);
	}
}

/// <summary>Uma data do calendario: `Ano` a partir do 1, `Mes` de 1 a 10, `Dia` de 1 a 28 -- e `Dias`, quantos dias o mundo ja correu.</summary>
public readonly record struct DataDoCalendario(long Dias, int Ano, int Mes, int Dia)
{
	/// <summary>O dia da semana -- o `listedDay` do `Calculate_Day` (`WorldClock.dm:37-59`): o dia 8 e de novo o primeiro.</summary>
	public string NomeDoDia => Calendario.DiasDaSemana[(Dia - 1) % Calendario.DiasPorSemana];

	public string NomeDoMes => Calendario.Meses[Mes - 1];

	/// <summary>O mes corrido desde a origem (0 = Luty do ano 1) -- a regua da idade.</summary>
	public long MesCorrido => Calendario.MesDe(Dias);

	/// <summary>"Suntag, 1 de Luty do ano 1" -- o `checkthetimeidiot()` do DM (`WorldClock.dm:111-112`), em portugues.</summary>
	public string Extenso => $"{NomeDoDia}, {Dia} de {NomeDoMes} do ano {Ano}";

	/// <summary>Quantos dias faltam pro mes virar (1 no ultimo dia dele).</summary>
	public int DiasAteOMesVirar => Calendario.DiasPorMes - Dia + 1;

	/// <summary>A data do primeiro dia do proximo mes.</summary>
	public DataDoCalendario ProximoMes => Calendario.De(Dias + DiasAteOMesVirar);
}
