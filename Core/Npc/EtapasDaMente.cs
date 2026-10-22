using System.Globalization;

namespace Jandirus.Core.Npc;

/// <summary>
/// A LEMBRANCA DAS FORMAS DE UM CHEFE (2026-10-21). O dono: ao treinar na mente com um chefe que
/// voce ja enfrentou, se ele tiver transformacoes que voce viu, da pra escolher a etapa.
///
/// O indice guardado e o <see cref="PapelDeNpc.Estagio"/> (0 = a primeira forma). O que o jogador
/// le como "etapa 1" e esse 0. Save antigo, que so tinha o conjunto de chefes, ganha o 0.
/// </summary>
public static class EtapasDaMente
{
	/// <summary>O verbo da lista (S2C.Escolhas). A resposta volta com o item inteiro.</summary>
	public const string Verbo = "mente_etapa";

	/// <summary>
	/// "molde:0,2,4" por chefe, indices em ordem. Lista vazia no disco e save de antes deste campo.
	/// </summary>
	public static List<string> Gravar(Dictionary<string, HashSet<int>> etapas)
	{
		var linhas = new List<string>();
		foreach (string molde in etapas.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
		{
			if (!etapas.TryGetValue(molde, out HashSet<int>? set) || set.Count == 0) continue;
			string nums = string.Join(',', set.Where(n => n >= 0).OrderBy(n => n).Select(n => n.ToString(CultureInfo.InvariantCulture)));
			if (nums.Length == 0) continue;
			linhas.Add(molde + ":" + nums);
		}
		return linhas;
	}

	/// <summary>O inverso de <see cref="Gravar"/>. Linha torta e ignorada: um save ruim nao apaga o resto.</summary>
	public static Dictionary<string, HashSet<int>> Ler(IEnumerable<string>? linhas)
	{
		var d = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
		if (linhas == null) return d;
		foreach (string linha in linhas)
		{
			int corta = linha.IndexOf(':');
			if (corta <= 0) continue;
			string molde = linha[..corta];
			var set = new HashSet<int>();
			foreach (string p in linha[(corta + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				if (int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 0) set.Add(n);
			if (molde.Length > 0 && set.Count > 0) d[molde] = set;
		}
		return d;
	}

	/// <summary>
	/// SAVE ANTIGO: chefe ja visto e etapa nenhuma gravada ganha a etapa 1 (indice 0). Quem ja tem
	/// etapa nao e mexido -- inclusive quem so viu a forma final, que nao deve ganhar a primeira.
	/// </summary>
	public static void CompletarSaveAntigo(IEnumerable<string> chefesVistos, Dictionary<string, HashSet<int>> etapas)
	{
		foreach (string id in chefesVistos)
		{
			if (id.Length == 0) continue;
			if (!etapas.TryGetValue(id, out HashSet<int>? set) || set.Count == 0)
				etapas[id] = [0];
		}
	}

	/// <summary>O nome da etapa na lista. Sem o campo no molde, "Etapa N" (N a partir de 1).</summary>
	public static string NomeDaEtapa(EstagioDeChefe etapa, int indiceZero) =>
		etapa.Nome.Length > 0 ? etapa.Nome : "Etapa " + (indiceZero + 1).ToString(CultureInfo.InvariantCulture);

	/// <summary>O item da lista: o molde, o indice e o nome. O nome e o que o botao mostra.</summary>
	public static string Item(string molde, int indice, string nome) =>
		molde + "|" + indice.ToString(CultureInfo.InvariantCulture) + "|" + nome;

	public static bool LerItem(string item, out string molde, out int indice, out string nome)
	{
		molde = "";
		indice = -1;
		nome = "";
		string[] p = item.Split('|', 3);
		if (p.Length < 2 || p[0].Length == 0) return false;
		if (!int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out indice) || indice < 0) return false;
		molde = p[0];
		nome = p.Length > 2 ? p[2] : "";
		return true;
	}

	public static string NomeDoItem(string item) =>
		LerItem(item, out _, out _, out string nome) && nome.Length > 0 ? nome : item;

	/// <summary>
	/// "cell" convoca sem etapa (o servidor pergunta, se houver mais de uma). "cell:2" ja escolheu.
	/// O id de molde nao tem dois-pontos.
	/// </summary>
	public static (string Molde, int? Etapa) SepararPedido(string pedido)
	{
		int corta = pedido.LastIndexOf(':');
		if (corta > 0 && int.TryParse(pedido[(corta + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 0)
			return (pedido[..corta], n);
		return (pedido, null);
	}
}
