using Jandirus.Core.Social;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// OS DETALHES DOS CONHECIDOS NO CLIENTE -- o que o servidor ja me contou de cada um (idade, genero, cargo,
/// historia), pedido pessoa a pessoa pela aba People (`MenuJogo.Gente.cs`) e so entregue a quem conviveu o
/// bastante (`Convivio.ConvivioParaDetalhes`; quem confere e o servidor, `GameServer.VerboDetalhes`).
///
/// A LISTA de conhecidos (`Conhecidos`, em `GameClient.cs`) NAO traz nada disto: ela vai inteira a cada mudanca,
/// e mandar a historia de todo mundo junto seria mandar pra quem nao tem direito de ler e esconder na tela.
///
/// O QUE FICA AQUI E UM CACHE, e ele nunca guarda mais do que a lista permite: sai quem saiu da lista ou do
/// convivio (<see cref="PodarDetalhes"/>), e sai tudo quando o personagem muda (<see cref="EsquecerDetalhes"/>).
/// </summary>
public partial class GameClient
{
	private readonly Dictionary<string, DetalhesDeConhecido> _detalhesDeGente = new(StringComparer.Ordinal);

	/// <summary>
	/// Sobe a cada detalhe que chega (ou sai). A aba People a poe na assinatura de cache dela -- sem isto, a
	/// resposta do servidor chegaria e a pagina aberta continuaria dizendo "buscando" -- e a usa pra saber se o
	/// pedido que ela fez ainda vale (ver `MenuJogo._detalhesPedidos`).
	/// </summary>
	public int VersaoDosDetalhes { get; private set; }

	/// <summary>O que o servidor me contou desta pessoa, ou nulo se eu ainda nao pedi (ou nao tenho direito).</summary>
	public DetalhesDeConhecido? DetalhesDe(string assinatura) =>
		_detalhesDeGente.TryGetValue(assinatura, out DetalhesDeConhecido d) ? d : null;

	/// <summary>Pede ao servidor os detalhes de um conhecido. A resposta (se o convivio pagar) chega no `S2C.DetalhesDeConhecido`.</summary>
	public void PedirDetalhes(string assinatura) => SendVerbo("detalhes", assinatura);

	/// <summary>O `S2C.DetalhesDeConhecido` chegou. Avisa pelo mesmo evento da lista: quem desenha os dois e a aba People.</summary>
	private void LerDetalhesDeConhecido(NetDataReader reader)
	{
		DetalhesDeConhecido d = FioDosConhecidos.LerDetalhes(reader);
		if (d.Assinatura.Length == 0) return;
		_detalhesDeGente[d.Assinatura] = d;
		VersaoDosDetalhes++;
		ConhecidosMudaram?.Invoke();
	}

	/// <summary>
	/// A LISTA NOVA CHEGOU: o que eu guardava de quem NAO esta mais nela -- ou de quem o convivio desta lista nao
	/// paga -- sai. O cache nunca fica com o que a lista nao deixa ler (quem foi esquecido, por exemplo).
	/// </summary>
	private void PodarDetalhes()
	{
		if (_detalhesDeGente.Count == 0) return;
		var podem = new HashSet<string>(StringComparer.Ordinal);
		foreach (ConhecidoInfo k in Conhecidos)
			if (k.Familiaridade >= Convivio.ConvivioParaDetalhes) podem.Add(k.Assinatura);

		int antes = _detalhesDeGente.Count;
		foreach (string sig in _detalhesDeGente.Keys.Where(s => !podem.Contains(s)).ToList()) _detalhesDeGente.Remove(sig);
		if (_detalhesDeGente.Count != antes) VersaoDosDetalhes++;
	}

	/// <summary>
	/// ESQUECE TUDO o que o servidor contou. Chamado quando um personagem entra no mundo (`S2C.JoinAccepted`): o
	/// `GameClient` atravessa o logout, e o que estava guardado era a memoria do personagem ANTERIOR -- dois
	/// personagens da mesma conta podem conhecer a mesma pessoa, e cada um tem a propria ficha dela. A aba pede
	/// de novo quando precisar.
	/// </summary>
	public void EsquecerDetalhes()
	{
		if (_detalhesDeGente.Count == 0) return;
		_detalhesDeGente.Clear();
		VersaoDosDetalhes++;
	}
}
