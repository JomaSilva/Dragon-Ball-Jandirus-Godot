using Jandirus.Core.Economia;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O SALDO DE QUEM CONSTROI BASE -- o que chega pelo `S2C.SaldoDeObra`: os dois bolsos e a moeda com que o
/// servidor esta cobrando as pecas deste jogador. Quem escreve e o `GameServer.MandarSaldoDeObra`
/// (`GameServer.CustoDeObra.cs`); quem le e o seletor "Pagar com" do modo de construir
/// (`ModoDeConstruir.Pagamento.cs`).
///
/// OS DOIS BOLSOS SAO OS DA CARTEIRA (<see cref="Zeni"/> e <see cref="Recursos"/>, `GameClient.Economia.cs`), e nao
/// uma copia: o numero que o seletor mostra e o mesmo das abas Stats e Tech, e o que a carteira traz por fora de
/// uma obra (a mineracao, o Drill, uma arvore derrubada) aparece no seletor sem mais nada. Este pacote existe ao
/// lado dela por duas coisas que ela nao diz: a MOEDA em uso, e "este e o saldo de depois DAQUELE pedido de
/// erguer" -- uma resposta por pedido, tenha a peca subido ou nao.
/// </summary>
public partial class GameClient
{
	/// <summary>A moeda com que o SERVIDOR esta cobrando as pecas deste jogador (o que ele respondeu por ultimo).</summary>
	public Moeda MoedaDeObraNoServidor { get; private set; }

	/// <summary>Quantas respostas de saldo de obra ja chegaram. Quem liga o modo de construir espera a proxima pra confiar no saldo.</summary>
	public int SaldosDeObraRecebidos { get; private set; }

	/// <summary>
	/// Quantas dessas respostas foram de UM PEDIDO DE ERGUER. O servidor manda exatamente uma por pedido, tenha
	/// a peca subido ou nao -- e a conta que deixa o modo de construir dar baixa nas pecas a caminho.
	/// </summary>
	public int RespostasDePecaDeObra { get; private set; }

	/// <summary>
	/// A CARTEIRA COMO ELA VEIO NA ULTIMA RESPOSTA DE OBRA. Na hora em que chega e a mesma de <see cref="Zeni"/> e
	/// <see cref="Recursos"/>; deixa de ser quando a carteira viva anda por outro pacote (`S2C.Carteira`). E dela
	/// que a conta das pecas a caminho parte -- ver `ContaDeObra`, que conta por que nao pode ser da viva.
	/// </summary>
	public (double Zeni, double Recursos) CarteiraNaUltimaObra { get; private set; }

	/// <summary>O tratador do `S2C.SaldoDeObra`. Casa byte a byte com o `GameServer.MandarSaldoDeObra`.</summary>
	private void LerSaldoDeObra(NetDataReader reader)
	{
		// SO GUARDA, SEM AVISAR NINGUEM: as telas que mostram a carteira (o seletor, a Stats, a Tech) a conferem
		// por quadro, e o `TechMudou` e tambem o sino do fantasma de assentar maquina (`TelaDeConstrucao`) --
		// toca-lo daqui seria responder por um pacote que nao e este.
		Zeni = reader.GetDouble();
		Recursos = reader.GetDouble();
		CarteiraNaUltimaObra = (Zeni, Recursos);
		MoedaDeObraNoServidor = (Moeda)reader.GetByte();
		bool deUmaPeca = reader.GetBool();

		SaldosDeObraRecebidos++;
		if (deUmaPeca) RespostasDePecaDeObra++;
	}
}
