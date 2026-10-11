using Jandirus.Core.Economia;

namespace Jandirus.Core.World;

/// <summary>
/// A CONTA DE QUEM ESTA CONSTRUINDO -- quanto ha no bolso AGORA, ja sem as pecas que foram pedidas ao servidor
/// e ainda nao voltaram. E conta do CLIENTE (o modo de construir, `ModoDeConstruir.Pagamento.cs`): quem cobra
/// e o servidor, e isto so decide se o fantasma fica vermelho e se o arrasto pede a proxima peca.
///
/// ============================ POR QUE EXISTE ============================
/// Arrastar o mouse manda uma duzia de pecas por segundo, e cada cobranca so volta do servidor um instante
/// depois. Conferindo so a carteira que ja CHEGOU, o cliente pediria mais pecas do que o bolso paga, e cada
/// uma voltaria como uma linha de "falta zeni" no chat. Entao cada pedido de erguer entra numa fila com o
/// preco, e cada resposta do servidor (ele manda exatamente uma por pedido, tenha a peca subido ou nao --
/// `S2C.SaldoDeObra`) tira a mais antiga.
/// ========================================================================
///
/// ============================ DE ONDE A CONTA PARTE ============================
/// SEM PECA A CAMINHO, da carteira viva: o que o servidor contou por ultimo, por qualquer pacote.
///
/// COM PECA A CAMINHO, de um saldo FIXO: a carteira no instante em que a primeira saiu, e depois a de cada
/// resposta de obra. NAO da carteira viva -- a cobranca de uma peca chega em DOIS pacotes (a carteira nova,
/// `S2C.Carteira`, e logo atras a resposta da obra), e os dois podem cair em quadros diferentes: no quadro do
/// meio a carteira ja traz a peca cobrada e a fila ainda a tem, e a mesma peca sairia da conta duas vezes. Com
/// o bolso no limite, a ultima parede que ele paga deixaria de ser pedida -- um buraco no meio do arrasto.
/// ===============================================================================
/// </summary>
public sealed class ContaDeObra
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagconstruir`): a conta parte SEMPRE da carteira viva, mesmo com peca a
	/// caminho -- a peca cuja cobranca ja chegou e cuja resposta ainda nao e descontada duas vezes. Falso em jogo.
	/// </summary>
	public static bool PartindoDaCarteiraVivaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagconstruir`): as pecas a caminho nao sao descontadas -- num arrasto o
	/// cliente pede mais do que o bolso paga. Falso em jogo.
	/// </summary>
	public static bool SemDescontarDeTeste;

	private readonly Queue<(Moeda Moeda, double Preco)> _aCaminho = new();
	private double _zeni, _recursos;

	/// <summary>Quantas pecas foram pedidas e ainda nao respondidas.</summary>
	public int ACaminho => _aCaminho.Count;

	/// <summary>Esquece tudo o que estava a caminho (o modo de construir desligou, ou acabou de ligar).</summary>
	public void Limpar() => _aCaminho.Clear();

	/// <summary>Um pedido de erguer acabou de sair, e vai custar isto nesta moeda. A carteira e a de agora.</summary>
	public void Pedir(Moeda moeda, double preco, double zeni, double recursos)
	{
		if (_aCaminho.Count == 0) (_zeni, _recursos) = (zeni, recursos);
		_aCaminho.Enqueue((moeda, preco));
	}

	/// <summary>O servidor respondeu ao pedido mais antigo: a carteira e a de DEPOIS dele (a que veio na resposta).</summary>
	public void Responder(double zeni, double recursos)
	{
		if (_aCaminho.Count > 0) _aCaminho.Dequeue();
		(_zeni, _recursos) = (zeni, recursos);
	}

	/// <summary>O saldo nesta moeda, ja sem as pecas a caminho. `zeni` e `recursos` sao a carteira viva.</summary>
	public double Saldo(Moeda moeda, double zeni, double recursos)
	{
		bool viva = _aCaminho.Count == 0 || PartindoDaCarteiraVivaDeTeste || SemDescontarDeTeste;
		double saldo = moeda == Moeda.Recursos ? (viva ? recursos : _recursos) : (viva ? zeni : _zeni);
		if (SemDescontarDeTeste) return saldo;

		foreach ((Moeda m, double preco) in _aCaminho)
			if (m == moeda) saldo -= preco;
		return saldo;
	}
}
