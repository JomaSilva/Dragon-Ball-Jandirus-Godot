using System.Globalization;

namespace Jandirus.Core.Magic;

/// <summary>
/// (2026-10-22) **O QUE O CRIADOR ESCOLHE AO ERGUER (OU REFAZER) UM SET DE ESFERAS** -- o menu de criacao (`Client/JanelaDaEstatua.cs`).
///
/// O dono: *"e ao criar um set de esferas crie um menu bem intuitivo e bem feito pro jogador q criou o set escolher os desejos, a
/// quantidade, a força etc"* -- e, perguntado, as tres regras:
///   * O NOME: na Terra o dragao e sempre o Shenlong, em Namek sempre o Porunga; em qualquer outro mundo o criador o escolhe.
///   * A FORCA DIVIDIDA: a forca do set e o BP de quem cria, e ela se divide pelos pedidos -- 1 pedido = 100% da forca, 2 = 50% cada,
///     3 = 33% cada (<see cref="ForcaDeCadaPedido"/>). E o menu mostra na hora quanto cada desejo da.
///   * LIGA E DESLIGA, SEM TROCA: o criador tira do dragao dele os desejos que nao quer; os que ficam continuam com a forca normal, e a
///     forca ainda decide quais aparecem.
///
/// ============================ DADO PURO, NUMA LINHA DE TEXTO ============================
/// A janela monta, o verbo leva (`db_estatua` ao erguer, `db_refazer` ao refazer) e o servidor le e confere TUDO de novo
/// (`GameServer.Esferas.cs`): uma janela mexida so consegue mandar o que ele recusa. O formato e
/// `cfg|nome=...|pedidos=2|sem=matar,calcinha|supremo=zeni`, e os argumentos de antes (`db_estatua supremo`, `db_refazer 2`) continuam
/// valendo -- eles nao comecam por <see cref="Prefixo"/>.
/// ========================================================================================
/// </summary>
public sealed class ConfigDaEstatua
{
	/// <summary>O comeco de toda configuracao no argumento do verbo.</summary>
	public const string Prefixo = "cfg";

	/// <summary>O nome do dragao como o criador o digitou (vazio = o padrao do planeta). So vale fora da Terra e de Namek.</summary>
	public string Nome = "";

	/// <summary>Pedidos por invocacao, 1 a 3 -- o `Wishs` do DM.</summary>
	public int Pedidos = 1;

	/// <summary>Os desejos que o criador TIROU do dragao dele (ids de <see cref="Desejos.Todos"/>).</summary>
	public HashSet<string> Desligados = new(StringComparer.Ordinal);

	/// <summary>Gravar o desejo supremo ao erguer, e em que moeda: vazio = nao, "zeni" ou "recursos". So ao ERGUER (o DM so o vende ali).</summary>
	public string Supremo = "";

	/// <summary>
	/// O set JA TEM o supremo gravado -- so a ida do servidor pra janela (a linha "Configurar o dragão" da janela de desejos), pra ela dizer
	/// que ele esta la. O servidor nunca o le de volta.
	/// </summary>
	public bool SupremoGravado;

	public const string SupremoEmZeni = "zeni", SupremoEmRecursos = "recursos";

	// =====================================================================
	// A FORCA DIVIDIDA
	// =====================================================================
	/// <summary>A FORCA DE CADA PEDIDO, em fracao da forca do set: 1 pedido = 1, 2 = 1/2, 3 = 1/3.</summary>
	public static double ForcaDeCadaPedido(int pedidos) => 1.0 / Math.Clamp(pedidos, 1, 3);

	/// <summary>A mesma fracao em porcentagem, como a janela a escreve: "100%", "50%", "33%".</summary>
	public static string PorcentagemDaForca(int pedidos) =>
		(ForcaDeCadaPedido(pedidos) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

	// =====================================================================
	// OS DESEJOS QUE O CRIADOR PODE TIRAR
	// =====================================================================
	/// <summary>
	/// OS DESEJOS QUE SE LIGAM E DESLIGAM: os portados da tabela, menos o "Cancelar" (sair sem pedir nao e desejo) e o supremo (ele e
	/// COMPRADO ao erguer, e nao desligado). Os nao portados nem aparecem: um interruptor de algo que nao existe seria um botao que mente.
	/// </summary>
	public static IEnumerable<DesejoDef> Ajustaveis => Desejos.Todos.Where(d => d.Portado && d.Id != "cancelar" && !d.PedeSupremo);

	/// <summary>Este desejo existe num dragao com <paramref name="pedidos"/> pedidos? O "Ressuscitar TODOS" e o "Matar" so ate dois.</summary>
	public static bool CabeNosPedidos(DesejoDef d, int pedidos) => !(d.SoAteDoisPedidos && pedidos > 2);

	// =====================================================================
	// O NOME
	// =====================================================================
	public const int NomeMinimo = 2, NomeMaximo = 20;

	/// <summary>O nome padrao fora da Terra e de Namek -- o do DM.</summary>
	public const string NomePadrao = "SHENRON";

	/// <summary>Os espacos do nome em um so, e nenhum nas pontas.</summary>
	public static string Limpo(string? nome) =>
		string.Join(' ', (nome ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

	/// <summary>O nome do jeito que o dragao o carrega: limpo e em maiusculas, como "SHENRON" e "PORUNGA".</summary>
	public static string NoFormatoDoDragao(string? nome) => Limpo(nome).ToUpper(CultureInfo.GetCultureInfo("pt-BR"));

	/// <summary>Por que este nome NAO serve (nulo = serve): letras, espaco, hifen e apostrofo, de 2 a 20.</summary>
	public static string? PorQueNaoServeONome(string? nome)
	{
		string n = Limpo(nome);
		if (n.Length < NomeMinimo) return $"o nome do dragão precisa de pelo menos {NomeMinimo} letras.";
		if (n.Length > NomeMaximo) return $"o nome do dragão cabe em até {NomeMaximo} letras.";
		foreach (char c in n)
			if (!char.IsLetter(c) && c != ' ' && c != '-' && c != '\'')
				return "o nome do dragão só leva letras, espaço, hífen e apóstrofo.";
		return null;
	}

	/// <summary>Este planeta tem o nome do dragao fixo? A Terra (o Shenlong) e Namek (o Porunga).</summary>
	public static bool NomeFixoEm(string planeta) => NomeFixoDe(planeta) != null;

	private static string? NomeFixoDe(string planeta) =>
		string.Equals(planeta, "Earth", StringComparison.OrdinalIgnoreCase) ? Esferas.NomeDoShenlong
		: string.Equals(planeta, Esferas.PlanetaEterno, StringComparison.OrdinalIgnoreCase) ? "PORUNGA"
		: null;

	/// <summary>
	/// O NOME QUE O DRAGAO TEM NESTE PLANETA: o fixo da Terra e de Namek, ou o escolhido (no formato do dragao), ou o padrao.
	/// <paramref name="escolhido"/> ja passou por <see cref="PorQueNaoServeONome"/> -- quem chama confere antes.
	/// </summary>
	public static string NomeNoPlaneta(string planeta, string? escolhido) =>
		NomeFixoDe(planeta) ?? (Limpo(escolhido).Length > 0 ? NoFormatoDoDragao(escolhido) : NomePadrao);

	// =====================================================================
	// A CONFERENCIA
	// =====================================================================
	/// <summary>
	/// POR QUE ESTA CONFIGURACAO NAO SERVE (nulo = serve), na lingua do jogador. <paramref name="nomeLivre"/> = o planeta deixa escolher o
	/// nome (fora da Terra e de Namek): onde ele e fixo, o nome digitado so e ignorado.
	/// </summary>
	public string? PorQueNaoServe(bool nomeLivre)
	{
		if (Pedidos is < 1 or > 3) return "a estátua concede de 1 a 3 pedidos por invocação.";
		if (nomeLivre && Limpo(Nome).Length > 0 && PorQueNaoServeONome(Nome) is { } semNome) return semNome;
		List<DesejoDef> ajustaveis = [.. Ajustaveis];
		foreach (string id in Desligados)
			if (!ajustaveis.Exists(d => d.Id == id)) return $"não há desejo \"{id}\" para desligar.";
		if (!ajustaveis.Exists(d => CabeNosPedidos(d, Pedidos) && !Desligados.Contains(d.Id)))
			return "deixe pelo menos um desejo ligado no seu dragão.";
		if (Supremo.Length > 0 && Supremo != SupremoEmZeni && Supremo != SupremoEmRecursos)
			return "o desejo supremo se paga em zeni ou em recursos.";
		return null;
	}

	// =====================================================================
	// O TEXTO DO VERBO
	// =====================================================================
	/// <summary>
	/// A configuracao no argumento do verbo. Os desejos em ordem, pra o mesmo set dar sempre o mesmo texto.
	///
	/// O ARGUMENTO TEM TETO (`Protocol.MaxArgDeVerbo`, 256): acima dele o servidor le uma linha VAZIA -- e ergueria a estatua de sempre, sem
	/// nada do que o criador escolheu. Os dezesseis desejos desligados por nome passariam do teto; por isso vai a lista MENOR: os desligados
	/// (`sem=`) ou os que ficam ligados (`com=`). No pior caso (oito de cada lado, o nome mais longo, o supremo) o texto fica abaixo de 210.
	/// </summary>
	public string Codificar()
	{
		var p = new List<string> { Prefixo, "nome=" + Limpo(Nome), "pedidos=" + Pedidos.ToString(CultureInfo.InvariantCulture) };
		if (Desligados.Count > 0)
		{
			List<string> sem = [.. Desligados.OrderBy(x => x, StringComparer.Ordinal)];
			List<string> com = [.. Ajustaveis.Select(d => d.Id).Where(id => !Desligados.Contains(id)).OrderBy(x => x, StringComparer.Ordinal)];
			p.Add(com.Count < sem.Count ? "com=" + string.Join(",", com) : "sem=" + string.Join(",", sem));
		}
		if (Supremo.Length > 0) p.Add("supremo=" + Supremo);
		if (SupremoGravado) p.Add("gravado=1");
		return string.Join("|", p);
	}

	/// <summary>Este argumento e uma configuracao (e nao um dos de antes)?</summary>
	public static bool EhConfig(string? arg)
	{
		string a = (arg ?? "").Trim();
		return a == Prefixo || a.StartsWith(Prefixo + "|", StringComparison.Ordinal);
	}

	/// <summary>A configuracao deste argumento, ou nula se ele nao e uma (<see cref="EhConfig"/>). Pedaco desconhecido e ignorado.</summary>
	public static ConfigDaEstatua? Ler(string? arg)
	{
		if (!EhConfig(arg)) return null;
		var c = new ConfigDaEstatua { Pedidos = 0 };
		foreach (string parte in arg!.Trim().Split('|').Skip(1))
		{
			int i = parte.IndexOf('=');
			if (i <= 0) continue;
			string v = parte[(i + 1)..];
			switch (parte[..i])
			{
				case "nome": c.Nome = v; break;
				case "pedidos": c.Pedidos = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0; break;
				case "sem":
					foreach (string id in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) c.Desligados.Add(id);
					break;
				case "com":   // a lista dos LIGADOS -- ver `Codificar`: desligado e todo ajustavel que nao esta nela
					var ligados = new HashSet<string>(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
					foreach (DesejoDef d in Ajustaveis)
						if (!ligados.Contains(d.Id)) c.Desligados.Add(d.Id);
					break;
				case "supremo": c.Supremo = v.Trim().ToLowerInvariant(); break;
				case "gravado": c.SupremoGravado = v.Trim() == "1"; break;
			}
		}
		return c;
	}
}
