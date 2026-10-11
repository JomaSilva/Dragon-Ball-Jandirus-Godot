namespace Jandirus.Core.Skills;

/// <summary>
/// ============================ AS SKILLS QUE O DONO TIROU DO JOGO ============================
/// Elas continuam no DM -- que e so a referencia deste port -- e nao existem mais aqui. Esta lista e o
/// unico lugar em que a decisao esta escrita, e as duas pontas a leem:
///
///   * O EXTRATOR (`Tools/AssetPipeline`: `DmSkillScanner.Scan` e `DmEffectorScanner.Scan`) tira a skill
///     do que escreve no `skills.json` e no `niveis.json`, inclusive da lista de galhos da arvore dela.
///     Sem isto a proxima re-extracao a traria de volta calada: o DM ainda a declara.
///   * O SERVIDOR, no login (`GameServer.PrepararSkills`), tira a skill do livro de quem ja a tinha e
///     DEVOLVE OS MARCOS de quem a comprou -- ver <see cref="Limpar"/>.
///
/// O marco congelado do catalogo (`catalogo-marco.json`) NAO e mexido: pra ele a skill e uma PERDA, e
/// ela esta explicada, com o mesmo motivo, em <see cref="MarcoDoCatalogo.PerdasExplicadas"/>.
/// ============================================================================================
///
/// TIRAR UMA SKILL E APAGAR O EFEITO DELA TAMBEM (regra da casa: codigo substituido se deleta). Esta
/// lista nao desliga nada -- quem a le so esquece o NOME. O efeito sai do servidor na mesma mudanca.
/// </summary>
public static class SkillsAposentadas
{
	/// <summary>Uma skill que saiu: o typepath do DM, o nome que o jogador via, quanto custava, e por que saiu.</summary>
	public readonly record struct Aposentada(string Path, string Nome, int Custo, string Porque);

	/// <summary>
	/// DEFEITO INJETADO (bancada): a skill some do livro e o marco que ela custou NAO volta -- o jogador
	/// que a comprou perderia um marco pra sempre, calado. Lido pela propria linha do reembolso, em
	/// <see cref="Limpar"/>.
	/// </summary>
	public static bool NaoReembolsaDeTeste;

	public static readonly Aposentada[] Todas =
	[
		// Dono, 2026-10-10: *"remova essa skill, pq ela n tem mais funçao no jogo, pode deletar ela
		// completamente"*. No DM (`saiyan.dm:176-200`) ela dava `Attack_Gain(level+2)` a quem lutava com
		// alguem de BP base maior -- o zenkai "em combate". O zenkai deste jogo ja paga na hora, na
		// derrota, e a skill ficou sem papel. Custava 1 marco (tier 1 da arvore `Saiyan Racials`).
		new("/datum/skill/saiyan/Saiyan_Power", "Saiyan Power", 1,
			"aposentada pelo dono em 2026-10-10: o zenkai em combate dela ficou sem funcao no jogo"),

		// Dono, 2026-10-11: *"pode tirar"*. Estas duas NAO sao do jogo original: vinham de `Skills/CustomAttacks/beams` e
		// `.../blasts`, arquivos que nenhum `#include` do `.dme` cita -- o sistema VELHO de ataques customizados, que o
		// BYOND nem compila. (O que ele compila e o `customattacks.dm`, a mesa "Inventar tecnicas de ki" deste port, e
		// ela nao pede skill nenhuma.) O extrator deixou de ler arquivo fora do `.dme` (`DmFonte.Arquivos`), e elas
		// sairam do `skills.json` junto com a arvore `Custom_Attacks`, que nunca foi concedida a ninguem.
		//
		// NINGUEM AS COMPRAVA: nao pendiam de arvore que raca, classe ou cargo recebesse. As duas linhas existem pra o
		// save que as tiver por outro caminho (um admin) nao ficar com um path orfao no livro -- e, se as comprou,
		// receber o marco de volta. Custavam 1 cada (`skillcost = 1`).
		new("/datum/skill/CustomAttacks/ki/beam/Beam", "Beam", 1,
			"tirada em 2026-10-11: vinha de um arquivo que o jogo original nao compila (fora do .dme)"),
		new("/datum/skill/CustomAttacks/ki/blast/Blast", "Blast", 1,
			"tirada em 2026-10-11: vinha de um arquivo que o jogo original nao compila (fora do .dme)"),
	];

	/// <summary>Esta skill saiu do jogo?</summary>
	public static bool Saiu(string path)
	{
		foreach (Aposentada a in Todas)
			if (string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>
	/// TIRA DO LIVRO O QUE SAIU DO JOGO, e devolve o que tirou e quantos marcos voltaram com cada uma
	/// (pra quem chama poder avisar o dono).
	///
	/// OS MARCOS VOLTAM PRA QUEM COMPROU -- a mesma regra do <see cref="SkillBook.EsquecerEReembolsar"/>, e
	/// pela mesma pergunta (<see cref="SkillBook.FoiComprada"/>): copia ENSINADA, herdada ou dada por admin
	/// nao custou marco, e devolver marco por ela seria uma impressora. Sem o reembolso, tirar uma skill do
	/// jogo seria tirar um marco de cada personagem que a escolheu. (No save de antes da lista de compradas,
	/// tudo o que ninguem ensinou conta como comprado -- ver <see cref="SkillBook.CarregarCompradas"/>.)
	///
	/// NAO ENCOLHE A ARVORE, de proposito e pela mesma razao que o login nunca encolhe (ver
	/// <see cref="SkillBook.Encolher"/>): o investido dela cai junto com a skill, e quem ja tinha comprado
	/// um tier acima com esse investimento fica com o que comprou -- ninguem perde uma segunda skill por
	/// uma decisao que nao foi dele.
	///
	/// O NIVEL DA SKILL NAO PRECISA DE LINHA AQUI: o `NiveisDeSkill.DoSave` so carrega progresso de quem
	/// tem regra no `niveis.json`, e a aposentada nao tem mais.
	/// </summary>
	public static List<(Aposentada Skill, int Devolvidos)> Limpar(SkillBook livro)
	{
		var saiu = new List<(Aposentada, int)>();
		foreach (Aposentada a in Todas)
		{
			if (!livro.Sabe(a.Path)) continue;
			int devolve = !livro.FoiComprada(a.Path) || livro.FoiEnsinada(a.Path) || NaoReembolsaDeTeste ? 0 : a.Custo;
			livro.Esquecer(a.Path);
			livro.EsquecerGanho(a.Path);
			livro.MarcosLivres += devolve;
			saiu.Add((a, devolve));
		}
		return saiu;
	}
}
