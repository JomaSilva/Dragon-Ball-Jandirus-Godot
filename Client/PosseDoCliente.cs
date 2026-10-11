using Jandirus.Core.Verbos;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// **O QUE ESTE PERSONAGEM TEM, DO JEITO QUE O CLIENTE SABE** -- a ponta de ca do <see cref="IPosse"/>.
///
/// ============================ ELA SO LE O QUE O SERVIDOR MANDOU ============================
/// Nada aqui e deduzido: a raca e a classe vem na ficha lenta (`S2C.Atributos`), as tecnicas e as skills no
/// livro (`S2C.Skills` -- `VerbosAtivos` e a lista que o `SabeTecnica` de la aceita), os itens na mochila
/// (`S2C.Inventario`), os cargos na lista de cargos (empurrada no login) e os <see cref="Fatos"/> no pacote
/// deles. O lugar sai da zona e das coisas de esfera desta zona, que tambem chegam por pacote.
///
/// Por isso a pergunta e feita AO VIVO, a cada desenho, e nao guardada num retrato: um retrato teria que
/// ser refeito em cada um dos seis eventos, e o primeiro que esquecesse deixaria um botao preso na tela.
///
/// E ELA NAO E PERMISSAO. Um cliente mexido responde o que quiser aqui e ganha um botao que o servidor
/// recusa -- que e exatamente o menu de antes.
/// ==========================================================================================
/// </summary>
public sealed class PosseDoCliente : IPosse
{
	private static readonly PosseDoCliente Eu = new();

	/// <summary>
	/// SO PRA BANCADA (`--diagverbs`): troca o personagem que o menu acha que e -- e assim que um processo so
	/// desenha o menu de um humano novato, de um Namekuseijin do Cla do Dragao e de quem tem Split Form.
	/// Nulo em jogo, sempre.
	/// </summary>
	public static IPosse? DeTeste;

	/// <summary>O personagem que o menu desenha agora.</summary>
	public static IPosse Atual => DeTeste ?? Eu;

	private static GameClient? C => GameClient.Instance;

	public string Raca => C?.Atributos.Raca ?? "";
	public string Classe => C?.Atributos.Classe ?? "";

	public bool SabeTecnica(string verbo) => C?.VerbosAtivos.Contains(verbo) == true;

	public bool SabeSkill(string typepath) => C?.SkillsAprendidas.Contains(typepath) == true;

	public bool TemItem(string id) => C is { } c && c.Mochila.Quantos(id) > 0;

	/// <summary>
	/// O dono de um cargo chega pelo NOME na lista de cargos (`CargoInfo.Dono`), que o servidor manda no login
	/// e a cada outorga -- as chaves sao as de `Core/Ranks/Ranks.cs`.
	/// </summary>
	public bool TemCargo(string chave)
	{
		if (C is not { LocalName.Length: > 0 } c) return false;
		foreach (GameClient.CargoInfo k in c.Cargos)
			if (string.Equals(k.Chave, chave, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(k.Dono, c.LocalName, StringComparison.Ordinal))
				return true;
		return false;
	}

	public Fatos Fatos => C?.Fatos ?? Fatos.Nenhum;

	public Lugar Lugar
	{
		get
		{
			if (C is not { } c) return Lugar.Nenhum;
			Lugar l = Lugar.Nenhum;
			// O MESMO CRIVO DO SERVIDOR (`Espaco.EhPlaneta`/`EhEspaco`, no Core): duas nocoes de planeta
			// envelheceriam separadas, e a primeira a errar seria a do cliente.
			if (Jandirus.Core.World.Espaco.EhPlaneta(c.Zone)) l |= Lugar.Planeta;
			if (Jandirus.Core.World.Espaco.EhEspaco(c.Zone)) l |= Lugar.Espaco;
			foreach (GameClient.EsferaInfo e in c.Esferas)
				if (e.Tipo == Protocol.CoisaDeEsfera.Estatua) { l |= Lugar.ComEstatua; break; }
			return l;
		}
	}

	/// <summary>
	/// O QUE A POSSE DE AGORA FAZ APARECER, numa linha -- o pedaco que a aba World poe na assinatura de cache
	/// pra a pagina se refazer quando um botao passa a existir (ou deixa de). Sao as respostas da tabela de
	/// requisitos, e nao os campos crus: um item qualquer que entra na mochila nao remonta nada.
	/// </summary>
	public static string Assinatura()
	{
		IPosse p = Atual;
		return $"{(byte)p.Lugar}/{RequisitosDosVerbos.AssinaturaDe(p)}";
	}
}
