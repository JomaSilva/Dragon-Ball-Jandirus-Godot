using Jandirus.Core.Items;

namespace Jandirus.Server;

/// <summary>
/// OS ITENS QUE SAIRAM DO JOGO, LADO DO SERVIDOR: so o recado. A lista e o porque moram no Core
/// (<see cref="ItensAposentados"/>); a limpeza roda na carga do save (`AccountStore.ParaJogador`), antes de a
/// mochila ser saneada.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--aposentadosteste`, as duas metades): quantos zeni o item que sai DEVOLVE por
	/// unidade, com a frase prometendo o dinheiro. E o reembolso que este recado fazia ate 2026-10-11 e o dono
	/// recusou (*"ninguem fabricou item nenhum entao n precisa de reembolso"*). Zero em jogo. Lido pela propria
	/// linha do recado.
	/// </summary>
	internal double AposentadoDevolveZeniDeTeste;

	/// <summary>
	/// O RECADO DE QUEM PERDEU UM ITEM PRA UMA DECISAO DO JOGO -- o irmao do <see cref="AvisarDasAposentadas"/>.
	/// Sem ele o jogador abriria a mochila e acharia um espaco a menos, sem ninguem ter dito por que. Uma frase
	/// CURTA por item, com o nome e o motivo -- e SEM dinheiro: o item sai, a carteira fica como estava (decisao
	/// do dono; ver "SEM REEMBOLSO" em <see cref="ItensAposentados"/>). A lista se esvazia ao falar, pra o recado
	/// sair uma vez so.
	/// </summary>
	private void AvisarDosItensAposentados(ServerPlayer pl)
	{
		foreach ((ItensAposentados.Aposentado item, int quantos) in pl.ItensQueSairam)
		{
			string frase = quantos == 1
				? $"o item {item.Nome} saiu do jogo e foi tirado da sua mochila: {item.Porque}."
				: $"os {quantos} {item.Nome} que você carregava saíram do jogo e foram tirados da sua mochila: {item.Porque}.";
			if (AposentadoDevolveZeniDeTeste > 0)
			{
				double volta = AposentadoDevolveZeniDeTeste * quantos;
				pl.Ficha.Zeni += volta;
				frase += $" Os {volta:N0} zeni que custava voltaram pra sua carteira.";
			}
			Avisar(pl, frase);
		}
		pl.ItensQueSairam = [];
	}
}
