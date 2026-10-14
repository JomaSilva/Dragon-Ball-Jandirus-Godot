namespace Jandirus.Core.Skills;

/// <summary>
/// ============================ O EFEITO QUE NAO ESTA NO CATALOGO: ESTA ESCRITO A MAO ============================
/// O catalogo (`skills.json`, `niveis.json`) so carrega o que o extrator consegue ler como DADO: soma, fator,
/// flag, verbo. O `effector()` de algumas skills e LOGICA -- um contador de luta, a hora do dia, um tiro que
/// passa perto --, e o servidor a escreveu a mao (lotes G11 e G12). Pro catalogo essas skills continuavam "sem
/// efeito": a ficha dizia *"o efeito mecânico desta habilidade ainda não foi portado"* e o aviso de compra dizia
/// *"(sem efeito mecanico ainda)"* de uma skill que funciona.
///
/// O dono, 2026-10-13: *"ta dizendo q ki gathering n tem efeito mecanico, n sei se isso ta desatualizado ou nao,
/// verifique ... NAO ESQUEÇA DE CHECAR SE AS INFORMAÇOES Q O JOGO PASSA AO COMPRAR AS SKILLS ESTAO CERTAS"*.
///
/// Esta tabela e a ponte: o que a skill faz, na lingua do jogador, pra ficha (<see cref="FichaDeSkill"/>), pro
/// aviso de compra do servidor e pro censo. A FRASE DIZ A MECANICA QUE O SERVIDOR RODA, e a bancada cobra que o
/// caminho de cada linha continue citado no codigo de producao -- tabela que sobrevive ao efeito que descreve e a
/// pior mentira que esta tela pode contar.
/// ================================================================================================================
/// </summary>
public static class EfeitosEscritosAMao
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): a tabela responde vazio -- a ficha e o aviso voltam a chamar de
	/// "sem efeito" as skills que o servidor roda. A v0.2.0. Falso em jogo, sempre.
	/// </summary>
	public static bool TabelaVaziaDeTeste;

	private static readonly Dictionary<string, string> Tabela = new(StringComparer.OrdinalIgnoreCase)
	{
		// `heran.dm:66-80` -> `GameServer.ZenkaiEmCombateG11`
		["/datum/skill/heran/Heran_Power"] =
			"em luta, com alguém mais forte à vista, seu poder cresce em surtos (cada surto rende poder de combate e devolve um pouco de Ki); "
			+ "ela sobe de nível lutando assim, e quanto maior o nível mais forte é o surto",
		// `makyo.dm:71-89` -> `GameServer.AstrosDoMakyoG11`
		["/datum/skill/makyo/Sun"] =
			"de dia o seu poder é multiplicado -- de 1,2× ao amanhecer e ao entardecer até 2× ao meio-dia --, mais um bônus de maestria que cresce ao meio-dia",
		// `makyo.dm:115-124` -> `GameServer.AstrosDoMakyoG11`
		["/datum/skill/makyo/Moon"] =
			"à noite o seu poder é multiplicado (1,2×), mais um bônus de maestria que cresce enquanto é noite",
		// `kanassa-jin.dm:38-43` -> `GameServer.TickDaPrecognicaoG12`
		["/datum/skill/kanassajin/precognition"] =
			"quando um tiro de outra pessoa chega a um passo de você, o corpo dá um passo de lado sozinho (não vale enquanto você atira ou não pode se mexer)",
	};

	/// <summary>O que esta skill faz neste port, se o efeito dela e escrito a mao; nulo se nao e.</summary>
	public static string? De(string path) => TabelaVaziaDeTeste ? null : Tabela.GetValueOrDefault(path);

	/// <summary>Os caminhos da tabela -- a bancada varre o codigo de producao atras de cada um.</summary>
	public static IEnumerable<string> Caminhos => Tabela.Keys;
}
