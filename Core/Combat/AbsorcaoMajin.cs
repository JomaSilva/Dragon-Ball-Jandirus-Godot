using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// ============================ A ABSORCAO DO MAJIN -- OS NUMEROS E AS PERGUNTAS ============================
/// Porte de `Code/Modules/Magic/MajinSaga.dm` (a metade "ABSORPTION REWORK", `:6-9` e `:120-393`) e do verb
/// que a chama (`obj/Buu_Absorb`, `Magic/Absorption.dm:76-107`). O resumo do proprio autor:
///
///   *"an absorbed player is NOT killed; they are sent to this Majin's private pocket z-level ALIVE. While
///   absorbed the Majin gains 10% of their BP, all their skill verbs, and their clothes. If the absorbed
///   dies in the pocket OR the Majin is KO'd, that person ESCAPES and the Majin loses those gains."*
///
/// E, do guardiao (`:217`): *"spawn a fightable clone of ME beside them: beat it and you ESCAPE; lose and
/// you stay absorbed. One clone per prisoner."*
///
/// AQUI MORA SO O QUE E CONTA E PERGUNTA PURA. O lugar e o `World.InteriorDoMajin`; quem move corpo, ergue
/// o guardiao e solta gente e o `Server/GameServer.AbsorcaoMajin.cs`.
/// ======================================================================================================
/// </summary>
public static class AbsorcaoMajin
{
	/// <summary>
	/// DEFEITO INJETADO (bancada): vencer o guardiao nao solta ninguem -- a imagem some e o prisioneiro
	/// continua la dentro. E o defeito que o proprio DM registra ter tido (`MajinSaga.dm:337-339`: o
	/// `del(src)` matava o proc antes da linha que soltava). Falso em jogo, sempre.
	/// </summary>
	public static bool GuardiaoVencidoNaoSoltaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o Majin nocauteado continua segurando quem absorveu (sem o
	/// `majin_escape_all()` do `KO()`, `CombatMechanics/KO.dm:13-14`). Falso em jogo, sempre.
	/// </summary>
	public static bool NocauteNaoSoltaDeTeste;

	/// <summary>A skill da arvore racial que concede o verb (`Race Trees/majin.dm:31-40`).</summary>
	public const string Skill = "/datum/skill/general/buuabsorb";

	/// <summary>O verb que ela concede -- o `obj/Buu_Absorb`, como o extrator o chama.</summary>
	public const string Verbo = "Buu_Absorb";

	/// <summary>`usr.Race=="Majin"||usr.Parent_Race=="Majin"` (`Absorption.dm:83`): o meio-sangue tambem.</summary>
	public static bool EhMajin(string? raca, string? racaDoPai) =>
		string.Equals(raca, "Majin", StringComparison.Ordinal) || string.Equals(racaDoPai, "Majin", StringComparison.Ordinal);

	/// <summary>
	/// O QUE O MAJIN GANHA POR ABSORVIDO: `rec.bp_bonus = round(M.BP * 0.1)` (`MajinSaga.dm:157`; o NPC
	/// devorado rende o mesmo, `:137`). Dez por cento do BP BASE da vitima -- e nao do expresso --, e o
	/// `round()` de um argumento do DM e o piso (ver <see cref="DmMath.Round(double)"/>).
	///
	/// ELE ENTRA SOMADO NA BASE do `powerlevel()` (`Stats/BP/base.dm:109`, ja portado em
	/// `Fighter.PowerLevel`: `tempBP = BP + ... + majin_absorb_bp`), e por isso passa pelos multiplicadores
	/// de forma como o proprio BP. Sai inteiro quando o absorvido sai (`majin_release`, `:255`).
	/// </summary>
	public static double BonusDeBp(double bpDaVitima) => DmMath.Round(Math.Max(bpDaVitima, 0) * 0.1);

	/// <summary>
	/// O PODER DO GUARDIAO: `guard_seed_bp = max(round(expressedBP / 2), 1)` (`MajinSaga.dm:367`) -- metade
	/// do poder EXPRESSO do Majin no instante da absorcao (a forma dele inclusa), e PINADO dali em diante:
	/// o `NPCTicker()` do guardiao so reescreve esse numero (`:314-318`) e ele nao tem surto de poder
	/// (`ai_no_powerup = 1`, `:303`, com o motivo: *"o surto chamava NPCAscension -> BPBoost ate 200x se o
	/// prisioneiro transformasse -> guardiao absurdo"*).
	/// </summary>
	public static double BpDoGuardiao(double expressoDoMajin) => Math.Max(DmMath.Round(expressoDoMajin / 2), 1);

	/// <summary>A vida abaixo da qual o guardiao esta vencido, de pe ou nao: o `HP &lt;= 15` de `:333`.</summary>
	public const double VidaDoVencido = 15;

	/// <summary>
	/// O GUARDIAO ESTA VENCIDO? -- `if(KO || dead || HP &lt;= 15)` (`MajinSaga.dm:333`), a condicao do
	/// `guard_watch()`. Nocauteado, morto ou quase: as tres soltam o prisioneiro.
	/// </summary>
	public static bool GuardiaoVencido(bool ko, bool morto, double hp) => ko || morto || hp <= VidaDoVencido;

	/// <summary>De quanto em quanto o `guard_watch()` olha: `sleep(8)` (`MajinSaga.dm:332`), 0,8 s.</summary>
	public const double SegundosEntreOlhadas = 8 / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// O `sleep(20)` do `absorbing` (`Absorption.dm:85-86`): dois segundos entre uma absorcao e a proxima,
	/// tenha ela dado certo ou nao.
	/// </summary>
	public const long RecargaMs = 2_000;

	/// <summary>`SpreadHeal(100, 1, 0)` no Majin que absorveu (`MajinSaga.dm:141` e `:220`).</summary>
	public const double CuraDoMajin = 100;

	/// <summary>
	/// POR QUE ESTE MAJIN NAO ABSORVE AGORA -- a frase pronta, ou "" quando pode. As recusas do verb
	/// (`Absorption.dm:81`: `!usr.absorbing &amp;&amp; M.absorbable &amp;&amp; !usr.KO &amp;&amp; usr.Planet!="Sealed"`) que
	/// sao do PROPRIO Majin; as do alvo sao de quem tem os dois corpos na mao (o servidor).
	///
	/// O `Planet != "Sealed"` e o Majin selado pelo Mafuba: quem esta preso num selo nao absorve ninguem.
	/// </summary>
	public static string PorQueNaoAbsorve(bool sabeAbsorver, bool ko, bool morto, bool selado, bool emRecarga)
	{
		if (!sabeAbsorver) return "voce ainda nao sabe absorver: aprenda Buu Absorb na arvore racial do Majin.";
		if (ko || morto) return "nao da, caido.";
		if (selado) return "selado, voce nao absorve ninguem.";
		if (emRecarga) return "seu corpo ainda esta se fechando sobre a ultima.";
		return "";
	}
}
