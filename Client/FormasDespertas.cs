using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// QUAIS FORMAS ESTE CORPO SABE FAZER -- a pergunta, num lugar so.
///
/// ============================ POR QUE ELA VIROU UM ARQUIVO ============================
/// Ela ja era feita pela aba Forms do menu ("Formas que voce desperta"), inline. Com a tela de
/// teclas ela passou a ter DOIS leitores, e as duas listas tem que ser a MESMA: uma forma que a aba
/// mostra e a tela de teclas nao oferece e uma forma que o jogador ve, quer e nao consegue ligar --
/// sem nada na tela explicando a diferenca.
///
/// A aba Forms continua com a copia inline dela nesta rodada porque outra sessao esta com o arquivo
/// na mao. Quando aquele trabalho cair, `MenuJogo.AbaFormas` passa a chamar <see cref="Minhas"/> e a
/// segunda escrita da regra morre -- e ate la ela esta escrita aqui, com nome, em vez de existir
/// duas vezes sem que ninguem saiba.
/// ======================================================================================
///
/// ============================ O QUE CONTA COMO "SEI FAZER" ============================
/// Duas provas, e sao diferentes porque as formas sao de dois tipos:
///
///   * **maestria &gt; 0** -- este corpo ja esteve nessa forma alguma vez. E o unico registro
///     honesto que existe pras formas de escada;
///   * **a faixa da disciplina cruzada** -- as quatro formas divinas (Sign, Perfected, Destroyer,
///     Ultra Ego) NAO acumulam maestria nenhuma. O que prova que o corpo as conhece e a
///     proficiencia REAL ter passado do `Degrau.Pct` da faixa que as concede. Sem esta metade elas
///     sumiriam no instante em que o jogador voltasse pra base.
///
/// A FORMA ATUAL ENTRA SEMPRE, mesmo com maestria zerada: estar nela e a prova mais forte que ha.
/// </summary>
public static class FormasDespertas
{
	/// <summary>
	/// As formas que este personagem desperta, na ordem do catalogo.
	///
	/// ============================ O OOZARU FICA DE FORA, E NAO E FILTRO DE TELA ============================
	/// `Catalogo.NaoSeSobePraEla` e a mesma pergunta que o `EstadoDeForma.Avaliar` faz no passo 0: nao
	/// existe gesto que suba pro Oozaru -- existe a LUA. Uma tecla ligada a ele seria uma tecla que so
	/// sabe recusar, e o jogador levaria a recusa como defeito da tecla.
	/// ==================================================================================================
	/// </summary>
	public static IEnumerable<FormaDef> Minhas()
	{
		if (GameClient.Instance is not { } cli) yield break;
		Jandirus.Net.Protocol.AtributosState a = cli.Atributos;
		string atual = Catalogo.PorRede(a.FormaAtual)?.Id ?? Catalogo.IdBase;

		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Catalogo.IdBase || Catalogo.NaoSeSobePraEla(d)) continue;
			if (d.Id == atual || Maestria(d.Id) > 0 || PelaDisciplina(d.Id)) yield return d;
		}
	}

	/// <summary>Este personagem desperta esta forma? E o portao do lado do cliente -- ver `Atalhos.Disparar`.</summary>
	public static bool Sei(string forma) => Minhas().Any(d => d.Id == forma);

	private static double Maestria(string forma)
	{
		if (GameClient.Instance is not { } cli) return 0;
		ushort alvo = Catalogo.Rede(forma);
		foreach ((ushort id, float pct) in cli.Atributos.Maestrias ?? []) if (id == alvo) return pct;
		return 0;
	}

	private static bool PelaDisciplina(string forma)
	{
		if (GameClient.Instance is not { } cli) return false;
		Jandirus.Net.Protocol.AtributosState a = cli.Atributos;
		return Disciplinas.DaForma(forma) is { } par
			&& a.Disciplina == Disciplinas.Rede(par.Def.Tipo)
			&& a.DiscReal >= par.Faixa.Pct;
	}
}
