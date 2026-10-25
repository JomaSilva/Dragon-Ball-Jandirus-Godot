namespace Jandirus.Core.Stats;

/// <summary>
/// (2026-10-23) O DIA DO RITUAL MISTICO DO VELHO KAIOHSHIN: quando este Kaioshin TERMINOU o ultimo ritual, em ms do relogio de parede
/// (0 = nunca). Um ritual por dia da vida real, contado no Kaioshin que faz (o dono) -- e so o ritual que chega ao fim gasta o dia: o
/// interrompido nao escreve aqui. Ver `Core/Ranks/RitualDoKaioshin.cs` e `GameServer.RitualDoKaioshin.cs`.
///
/// Campo publico da ficha vai pro save sozinho: sair e voltar do jogo nao zera o dia. Mora na FICHA DO PERSONAGEM, como a recarga da
/// Sala do Tempo (`CharacterSave.SalaUltimaEntrada`), e nao na conta -- e o trono e da conta: uma conta com dois personagens no trono
/// teria um dia por personagem (anotado pro dono).
/// </summary>
public partial class Fighter
{
	/// <summary>O fim do ultimo ritual feito por este Kaioshin (ms do relogio de parede; 0 = nunca fez).</summary>
	public long RitualDoKaioshinEm;
}
