using Jandirus.Core.Skills;

namespace Jandirus.Core.Races;

/// <summary>
/// ============================ O SLEEP DO MAJIN (dono, 2026-10-18) ============================
/// *"a skill de regeneraçao mude o nome pra sleep, e o personagem ficaria 5 segundos parados, ao qual ao final ele regeneraria grande parte
/// do seu hp dos membros, mas se for interrompido nesses 5s nada acontece e tem um cooldown de 1 minuto mesmo cancelando, regeneração
/// passiva dos majins mesmo em combate esta mt boa n precisa mexer nela"*.
///
/// SO DO MAJIN, E POR QUE: a `Regenerate` do DM (`/datum/skill/general/regenerate`) pende de SEIS arvores -- Alien, Arlian, Bio-Android,
/// meio-Saiyajin, Heran e Majin -- e entra pelo mesmo verb da regeneracao racial do Namekuseijin (`GameServer.PodeRegenerar`). O pedido e
/// do Majin; trocar a skill pra todo mundo mexeria nas outras cinco racas e no Namekuseijin, que ninguem pediu. Entao o Sleep e uma skill
/// DO PORT, so da arvore `Majin Racials`, no lugar da `Regenerate` -- com o custo, o tier e o gene (`Regeneration=5`) dela, pra o Majin que
/// a compra ganhar na ficha exatamente o que ganhava: a regeneracao passiva continua a mesma, como o dono pediu. As outras cinco arvores
/// continuam com a `Regenerate` de sempre.
///
/// POR QUE EM CODIGO E NAO NO `skills.json`: o JSON e o que o extrator LE do DM, e uma re-extracao o reescreve -- a mesma escolha dos
/// <see cref="AcendedoresDoPort"/> e dos <see cref="VerbosDoPort"/>. Quem chama e a carga do catalogo (`SkillCatalog.Parse`), nas duas pontas.
///
/// QUEM JA TINHA A `Regenerate` COMO MAJIN (comprada na arvore dele) troca no login, sem pagar nem receber marco nenhum -- a compra
/// continua compra (<see cref="SkillBook.Trocar"/>). O corpo que dorme mora no servidor (`GameServer.SonoDoMajin.cs`).
/// ============================================================================================
/// </summary>
public static class SonoDoMajin
{
	/// <summary>O typepath da skill do port. Nao existe no DM -- ver o cabecalho.</summary>
	public const string Path = "/datum/skill/majin/Sleep";

	/// <summary>O verb (e o nome) da skill.</summary>
	public const string Verbo = "Sleep";

	/// <summary>A `Regenerate` do DM, que o Sleep substitui na arvore do Majin.</summary>
	public const string Regenerate = "/datum/skill/general/regenerate";

	/// <summary>A arvore racial do Majin (`Majin Racials`).</summary>
	public const string ArvoreDoMajin = "/datum/skill/tree/majin";

	/// <summary>A raca, como o port a escreve.</summary>
	public const string Raca = "Majin";

	/// <summary>Os segundos parado -- *"o personagem ficaria 5 segundos parados"*.</summary>
	public const double Segundos = 5;

	/// <summary>
	/// A RECARGA: 1 min, contado do FIM -- completo ou interrompido. *"tem um cooldown de 1 minuto mesmo cancelando"*. Do fim e nao do
	/// comeco: quem dormiu inteiro espera o minuto inteiro, e quem foi acordado tambem.
	/// </summary>
	public const double SegundosDeRecarga = 60;

	/// <summary>
	/// QUANTO CADA MEMBRO SE REFAZ NO FIM: 60% da vida maxima dele, sem passar do cheio -- *"grande parte do seu hp dos membros"*. O numero
	/// e DECISAO deste port (o dono disse "grande parte", e nao quanto). O membro DECEPADO nao volta por aqui: quem o refaz e a regeneracao
	/// passiva do Majin, que o dono mandou deixar como esta.
	/// </summary>
	public const double FracaoDaCura = 0.6;

	/// <summary>A descricao na ficha da skill (o catalogo e em ingles, como o do DM).</summary>
	public const string Descricao =
		"Lie down and sleep for 5 seconds. If nothing disturbs you, your body knits itself back together and every limb recovers a large "
		+ "part of its health. Moving, acting or getting hit wakes you up and the sleep is lost. 1 minute cooldown, even if interrupted.";

	/// <summary>
	/// DEFEITO INJETADO (`--sonoteste`): o catalogo carrega sem o Sleep -- a arvore do Majin volta a pendurar a `Regenerate` e o login nao
	/// troca nada. O jogo de antes. Falso em jogo, sempre.
	/// </summary>
	public static bool SemOSonoDeTeste;

	/// <summary>
	/// POE O SLEEP NO CATALOGO: a skill, clonada dos numeros da `Regenerate` (custo, tier, teto de nivel, gene, ligada, esquecivel), e o
	/// galho trocado na arvore do Majin. Sem a `Regenerate` ou sem a arvore no catalogo (uma bancada de mesa), nao faz nada.
	/// </summary>
	public static void Aplicar(SkillCatalog cat)
	{
		if (SemOSonoDeTeste) return;
		if (cat.Get(Regenerate) is not { } regen || cat.Get(ArvoreDoMajin) is not { } arvore) return;

		var sono = new Skill
		{
			Path = Path,
			Nome = Verbo,
			Desc = Descricao,
			Tipo = regen.Tipo,
			Tier = regen.Tier,
			Custo = regen.Custo,
			MaxNivel = regen.MaxNivel,
			Folga = regen.Folga,
			Comum = regen.Comum,
			Ligada = regen.Ligada,
			SoVilao = regen.SoVilao,
			Ensinavel = regen.Ensinavel,
			CustoDeEnsino = regen.CustoDeEnsino,
			CustoFixo = regen.CustoFixo,
			Esquecivel = regen.Esquecivel,
			Verbos = [Verbo],
			Buffs = new(regen.Buffs, StringComparer.Ordinal),
			Mults = new(regen.Mults, StringComparer.Ordinal),
			Genes = new(regen.Genes, StringComparer.Ordinal),
			Flags = new(regen.Flags, StringComparer.Ordinal),
		};
		cat.Acrescentar(sono);
		arvore.Galhos = [.. arvore.Galhos.Select(g => string.Equals(g, Regenerate, StringComparison.OrdinalIgnoreCase) ? Path : g)];
	}

	/// <summary>
	/// O LIVRO DO MAJIN DE ANTES: quem tem a `Regenerate` troca pelo Sleep, guardando como ela entrou (comprada continua comprada). So o
	/// Majin. Devolve se trocou -- quem chama avisa o jogador.
	/// </summary>
	public static bool Migrar(SkillBook livro, string raca) =>
		!SemOSonoDeTeste && string.Equals(raca, Raca, StringComparison.OrdinalIgnoreCase) && livro.Trocar(Regenerate, Path);

	/// <summary>O recado do login pra quem teve a skill trocada.</summary>
	public const string AvisoDaTroca =
		"a sua Regenerate de Majin agora se chama Sleep: deite e durma 5 s sem ser incomodado, e cada membro se refaz em boa parte. "
		+ "Andar, agir ou apanhar acorda (e o sono se perde); a espera é de 1 min, acordado ou não. A regeneração passiva do Majin continua a mesma.";
}
