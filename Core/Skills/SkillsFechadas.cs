namespace Jandirus.Core.Skills;

/// <summary>
/// ============================ AS SKILLS QUE ESTE JOGO AINDA NAO VENDE ============================
/// O dono, 2026-10-13: *"tem mts verbs mortos q poderiam ser tirados, e tem verb q diz q faz algo mas n faz nada"*.
///
/// Uma skill cujo UNICO efeito e conceder um verb que este port ainda nao tem (o sistema inteiro dele falta: parar o
/// tempo, a arvore usada de arma, a imitacao permanente, a camada Golden) era uma compra que custava marcos e
/// entregava um botao cinza escrito "(nao portada)". O botao cinza saiu do menu (`Habilidades.DasSkills`), e sem ele
/// a compra seria um marco jogado fora em silencio -- entao a skill FECHA: a loja a mostra dizendo que ela ainda nao
/// existe neste jogo, a compra e recusada, e quem ja a tinha comprado recebe os marcos de volta no login.
///
/// NAO E <see cref="SkillsAposentadas"/>: aquelas sairam do jogo; estas ESPERAM o sistema delas. No dia em que o verb
/// ganhar corpo a linha sai daqui, e a bancada cobra isso (skill fechada cujo verb ja tem corpo reprova).
///
/// A LISTA E EXPLICITA, e a bancada cobra a outra direcao: nenhuma OUTRA skill que a loja vende pode ser so verb sem
/// corpo -- a proxima que aparecer tem de ganhar corpo ou entrar aqui.
/// ==============================================================================================
/// </summary>
public static class SkillsFechadas
{
	/// <summary>
	/// Uma skill fechada: o typepath, o nome que o jogador ve, quanto custava (o `SkillCatalog.CustoDe` dela -- o TIER,
	/// e nao o `custo` cru do catalogo: e o que a compra debitou, e a bancada confere um contra o outro), e o que
	/// falta pra ela existir.
	/// </summary>
	public readonly record struct Fechada(string Path, string Nome, int Custo, string Falta);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): nada esta fechado -- a loja volta a vender as quatro, e quem compra
	/// leva um verb que nao faz nada. A v0.2.0. Falso em jogo, sempre.
	/// </summary>
	public static bool TudoAbertoDeTeste;

	public static readonly Fechada[] Todas =
	[
		// `Misc/TimeStop.dm:36-105`: o `Stop` para o MUNDO inteiro (`TimeStopped`, global), cega quem nao enxerga o
		// tempo parado e deixa andar so quem para e quem e mais forte que ele. Mexe em todo laco do jogo.
		new("/datum/skill/general/stoptime", "Time Stop", 1, "o sistema de parar o tempo"),
		// `Weapons Expert.dm:167-227`: arrancar uma arvore do cenario (`Pluck_Tree`), empunha-la (`Equip_Tree`) e
		// arremessa-la (`Throw_Tree`). Pede arvore como objeto do mundo e arma empunhada.
		new("/datum/skill/Tree_Mastery", "Tree Mastery", 2, "arrancar uma árvore do cenário e usá-la como arma"),
		// `Imitation.dm`: a `Permanent_Imitation` reescreve o genoma do corpo com o do alvo imitado, pra sempre.
		new("/datum/skill/general/PermanentImitation", "Shapeshifter's Cure", 1, "a imitação permanente (trocar o próprio corpo pelo do imitado)"),
		// `IcerTransform.dm:255-300`: a camada Golden -- ki divino temporario por cima da evolucao do Frost Demon.
		new("/datum/skill/icer/Golden_Form", "Golden Form", 1, "a forma Golden do Frost Demon"),
	];

	/// <summary>Esta skill esta fechada pra compra? Devolve a linha dela, ou nulo.</summary>
	public static Fechada? De(string path)
	{
		if (TudoAbertoDeTeste) return null;
		foreach (Fechada f in Todas)
			if (string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)) return f;
		return null;
	}

	/// <summary>
	/// TIRA DO LIVRO AS FECHADAS QUE FORAM COMPRADAS, com os marcos de volta -- a mesma regra (e a mesma pergunta,
	/// <see cref="SkillBook.FoiComprada"/>) do <see cref="SkillsAposentadas.Limpar"/>. A que chegou por outro caminho
	/// (o kit de um cargo, um admin) fica onde esta: ela nao custou marco, e o que a deu a daria de novo.
	/// </summary>
	public static List<(Fechada Skill, int Devolvidos)> Limpar(SkillBook livro)
	{
		var saiu = new List<(Fechada, int)>();
		if (TudoAbertoDeTeste) return saiu;
		foreach (Fechada f in Todas)
		{
			if (!livro.Sabe(f.Path) || !livro.FoiComprada(f.Path) || livro.FoiEnsinada(f.Path)) continue;
			livro.Esquecer(f.Path);
			livro.EsquecerGanho(f.Path);
			livro.MarcosLivres += f.Custo;
			saiu.Add((f, f.Custo));
		}
		return saiu;
	}
}
