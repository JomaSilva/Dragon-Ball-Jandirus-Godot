namespace Jandirus.Core.Items;

/// <summary>
/// (2026-10-26) A FILEIRA "EQUIPADO" DA MOCHILA -- o dono: *"coloca tb no inventario um wearing so pra coisas como peso, scouter etc, pra n
/// confundir"*; e na pergunta que se seguiu: o peso vestido, o scouter ligado, a roupa espacial, o respirador e os brincos Potara saem da
/// grade enquanto estao em uso, e o Nav System e o Radar continuam nela.
///
/// SO A TELA SE ARRUMA: no servidor eles continuam na mochila (contam nos 30 espacos, e o que esta em uso fica com o dono no saque --
/// `Saque.EmUso`). "Em uso" e o que o jogo ja diz: o peso VESTIDO (`Fighter.pesoVestido`, uma unidade), o scouter LIGADO (o bit), e a roupa
/// espacial, o respirador e os brincos por ESTAREM na mochila -- no port, usar esses e estar com eles (ver o cabecalho de `MenuJogo.Equip`).
/// </summary>
public static class Equipado
{
	/// <summary>As vagas da fileira: sempre estas cinco, cheias ou nao -- a tela nao danca a cada troca, como a fileira "vestindo".</summary>
	public const int Vagas = 5;

	/// <summary>Os que estao em uso por estarem na mochila, na ordem da fileira (depois do peso e do scouter).</summary>
	public static readonly string[] PorEstarNaMochila = [CatalogoDeItens.Traje, CatalogoDeItens.Respirador, CatalogoDeItens.BrincosPotara];

	/// <summary>
	/// SEPARA A MOCHILA: UMA unidade de cada coisa em uso vai pra fileira -- na ordem: o peso vestido, o scouter ligado, a roupa espacial, o
	/// respirador e os brincos --, e a grade fica com o resto, na mesma ordem (a pilha que tinha mais de um fica com um a menos).
	/// </summary>
	public static (List<Pilha> EmUso, List<Pilha> Grade) Separar(IReadOnlyList<Pilha> mochila, bool scouterLigado, string pesoVestido)
	{
		var emUso = new List<Pilha>();
		var tirar = new HashSet<string>(StringComparer.Ordinal);
		void Usar(string id)
		{
			if (id.Length == 0 || tirar.Contains(id) || !mochila.Any(p => p.Id == id && p.Quantidade > 0)) return;
			tirar.Add(id);
			emUso.Add(new Pilha(id, 1));
		}
		Usar(pesoVestido);
		if (scouterLigado) Usar(CatalogoDeItens.Scouter);
		foreach (string id in PorEstarNaMochila) Usar(id);

		var grade = new List<Pilha>();
		foreach (Pilha p in mochila)
		{
			if (p.Quantidade > 0 && tirar.Remove(p.Id))
			{
				if (p.Quantidade > 1) grade.Add(p with { Quantidade = p.Quantidade - 1 });
				continue;
			}
			grade.Add(p);
		}
		return (emUso, grade);
	}
}
