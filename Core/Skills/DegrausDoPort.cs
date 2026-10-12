namespace Jandirus.Core.Skills;

/// <summary>
/// ONDE ESTE PORT DESVIA DO `effector()` DO DM NUM DEGRAU DE NIVEL -- e so isso.
///
/// ============================ POR QUE ISTO MORA NO CORE (dono, 2026-10-12) ============================
/// O dono abriu a ficha do Ki Unlocked e leu "nivel 30: concede a skill Flight". No jogo o voo so abre
/// no nivel 50 -- e a regra e DELE ("voar tem que LIBERAR SOZINHO ao chegar em metade da maestria do Ki").
/// As duas coisas estavam certas, cada uma no seu canto: o `niveis.json` e fiel ao DM (`Mind.dm:108-110`),
/// e o desvio morava numa lista PRIVADA do servidor, que a <see cref="FichaDeSkill"/> -- funcao pura do
/// catalogo -- nao tinha como ler. A tela repetia o DM e o servidor obedecia o dono.
///
/// Agora o desvio e UM dado, lido pelas duas bocas: o servidor (o que NAO conceder, e em que nivel o voo
/// abre) e a ficha (o que NAO anunciar, e o que anunciar no lugar). Desvio novo entra AQUI -- e a bancada
/// `--menteskills` (secao 8) compara a ficha com a PRATICA, nivel a nivel.
/// ======================================================================================================
/// </summary>
public static class DegrausDoPort
{
	/// <summary>
	/// A skill de voo do DM (`flying.dm`). Quem a tiver voa pelo caminho antigo, e e ela que da os pisos de
	/// 25/75 de proficiencia; o port so nao a CONCEDE mais por degrau (ver <see cref="NaoConcede"/>).
	/// </summary>
	public const string SkillDoVoo = "/datum/skill/flying";

	/// <summary>A raiz da arvore de Ki. Sobe meditando (2 por tique) e voando (2) -- Mind.dm:81.</summary>
	public const string SkillDoKi = "/datum/skill/mind/Ki_Unlocked";

	/// <summary>
	/// METADE DA MAESTRIA DO KI: onde o voo aparece sozinho.
	///
	/// `Ki_Unlocked` tem `maxlevel = 100`, entao "50% de maestria" e o nivel 50, literal.
	/// </summary>
	public const int MaestriaQueDestravaVoo = 50;

	/// <summary>
	/// DEFEITO INJETADO (bancada --menteskills): a ficha volta a repetir o DM onde o port desvia -- anuncia a
	/// skill de voo no nivel 30 do Ki Unlocked e cala o voo do nivel 50.
	/// </summary>
	public static bool FichaRepeteODmDeTeste;

	/// <summary>
	/// A SKILL QUE UM DEGRAU DO DM ENTREGARIA E ESTE PORT NAO ENTREGA. Hoje e uma so: a `flying` do nivel 30
	/// do Ki Unlocked (Mind.dm:109-110). O dono cortou o voo-por-skill, e concede-la reabriria pelo 30 a
	/// porta que ele fechou no 50. O dado continua no `niveis.json` (e fiel ao DM); o desvio mora aqui.
	/// </summary>
	public static bool NaoConcede(string path) =>
		string.Equals(path, SkillDoVoo, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// O QUE O PORT DA NUM NIVEL E O DM NAO MARCA ALI: (nivel, a frase da ficha). Hoje, o voo -- que no
	/// servidor e a pergunta `PodeVoar` (`GameServer.Voo.cs`), e nao um verb comprado nem uma skill.
	/// </summary>
	public static IEnumerable<(int Nivel, string Frase)> Acrescimos(string path)
	{
		if (string.Equals(path, SkillDoKi, StringComparison.OrdinalIgnoreCase))
			yield return (MaestriaQueDestravaVoo, FraseDoVoo);
	}

	/// <summary>A linha do voo na ficha. Uma so, pra a bancada cobrar pelo texto.</summary>
	public const string FraseDoVoo = "libera o voo (tecla F)";
}
