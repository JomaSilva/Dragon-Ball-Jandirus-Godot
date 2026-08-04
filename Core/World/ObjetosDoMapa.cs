namespace Jandirus.Core.World;

/// <summary>Uma máquina que já vem posta no mapa: onde ela está e o que ela é.</summary>
public readonly record struct ObjetoDoMapa(int X, int Y, string Id);

/// <summary>
/// AS MÁQUINAS QUE O MAPA JÁ TRAZ -- banco, bancada de pesquisa, sala de gravidade, os labs.
///
/// ============================ POR QUE ELAS SAÍRAM DO TILEMAP ============================
/// No original todas têm VERBOS (`set src in oview(1)`): são coisas com que se interage. Aqui
/// viravam célula de tilemap -- pintura no chão, sem estado e sem resposta -- e ficavam ao lado
/// de uma cópia construída por jogador, que é um node e funciona. Duas coisas iguais na tela,
/// uma viva e a outra não.
///
/// Agora o conversor as reconhece pelo `create_type` do catálogo de tecnologia e as escreve
/// aqui, ao lado da cena. O servidor as registra como CONSTRUÇÕES do mapa, e com isso elas
/// entram inteiras no sistema que já existe: mesmo desenho, mesma densidade, mesmo alcance de
/// uso, mesmos comandos. Uma bancada que estava no mapa desde sempre passa a servir pra estudar.
/// ========================================================================================
///
/// É O MESMO CAMINHO DA PORTA (`PortasDaZona`), e de propósito: um arquivo ao lado da cena, lido
/// pelas duas pontas, sem nada de novo no protocolo. O que muda é só o que se faz com a lista.
///
/// O ARQUIVO GUARDA O ID, e não a arte: quem sabe desenhar uma bancada é o catálogo, e repetir
/// arte, densidade e deslocamento aqui seria uma segunda verdade sobre a mesma máquina -- a
/// primeira a divergir seria a densidade, que já foi exatamente o defeito entre o item de loja e
/// o `create_type`.
/// </summary>
public static class ObjetosDoMapa
{
	public static List<ObjetoDoMapa> Parse(string json)
	{
		var lista = new List<ObjetoDoMapa>();
		foreach (string bloco in Blocos(json))
		{
			string id = Str(bloco, "id");
			if (id.Length > 0) lista.Add(new ObjetoDoMapa(Int(bloco, "x"), Int(bloco, "y"), id));
		}
		return lista;
	}

	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		int b = bloco.IndexOf('"', a + 1);
		return b < 0 ? "" : bloco[(a + 1)..b];
	}

	private static int Int(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '-' or ' ')) b++;
		return int.TryParse(bloco[a..b].Trim(), out int v) ? v : 0;
	}
}
