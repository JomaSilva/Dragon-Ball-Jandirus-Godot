namespace Jandirus.Core.Stats;

public partial class Fighter
{
	/// <summary>
	/// (2026-10-23) QUANDO O CARGO DE KAIOSHIN ENTREGOU O ULTIMO PAR DE BRINCOS POTARA a este personagem, em ms do relogio de parede (0 =
	/// nunca). O cargo entrega um par por dia real a quem ficou sem -- ver `Items.ParDePotara.CargoEntrega`, que e quem le e escreve.
	///
	/// Campo publico da ficha vai pro save sozinho: sair e voltar do jogo nao zera o dia. Mora na FICHA DO PERSONAGEM, como o dia do
	/// Ritual do Velho Kaiohshin, e nao na conta -- e o trono e da conta: uma conta com dois personagens no trono teria um par por dia por
	/// personagem (anotado pro dono).
	/// </summary>
	public long ParDePotaraEm;
}
