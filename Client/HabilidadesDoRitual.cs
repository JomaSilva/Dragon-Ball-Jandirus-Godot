using Jandirus.Core.Ranks;
using Jandirus.Core.Verbos;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-23) O BOTAO DO RITUAL MISTICO DO VELHO KAIOHSHIN (aba Skills) -- o que entrou no kit do Kaioshin no lugar da skill do
/// Mistico (`GameServer.RitualDoKaioshin.cs`). Como todo o `Habilidades`, este arquivo so REGISTRA: quem decide se o botao existe pra
/// este personagem e a linha `hab:ritual_kaioshin` da tabela (`RequisitosDosVerbos`: o trono do Kaioshin, ou estar no meio de um
/// ritual -- o alvo tambem o ve, pra interromper), lida ao vivo. E ele nao e permissao: o servidor confere tudo no aperto.
/// </summary>
public static class HabilidadesDoRitual
{
	public static void Registrar()
	{
		Verbos.Registrar(new Verbo(
			RitualDoKaioshin.Nome,
			Verbos.Skills,
			"O ritual do Velho Kaiohshin: com alguém na sua frente (marque-o e encoste nele), você oferece o ritual -- e, se ele aceitar "
			+ $"no cartão, os dois ficam {RitualDoKaioshin.DuracaoMs / 60_000} minutos parados, frente a frente. No fim o potencial que ele "
			+ "guardou se liberta em dobro e ele ganha a forma Potential Unleashed -- uma vez por personagem.\n\nUm ritual por dia (tempo real); interrompido antes do fim, o dia não "
			+ "é gasto. No meio do ritual, este botão o interrompe -- e quem recebe o ritual também o vê.",
			() => GameClient.Instance?.SendHabilidade(RitualDoKaioshin.Verbo))
			{ Chave = RequisitosDosVerbos.PrefixoDeHabilidade + RitualDoKaioshin.Verbo });

		// (2026-10-29, o pedido 149) O KAIOSHIN RESTAURA UM MUNDO DESTRUIDO -- a linha `hab:kai_restaurar` da tabela (o trono). Ver
		// `GameServer.UsarVerboDeRestaurar`.
		Verbos.Registrar(new Verbo(
			MissoesDeCargo.NomeDeRestaurar,
			Verbos.Skills,
			"Traz de volta um planeta destruído: o da sua tarefa de Reconstruir, se houver, ou o último que foi destruído. É a tarefa "
			+ "de cargo do Kaioshin enquanto houver mundos destruídos.",
			() => GameClient.Instance?.SendHabilidade(MissoesDeCargo.VerboDeRestaurar))
			{ Chave = RequisitosDosVerbos.PrefixoDeHabilidade + MissoesDeCargo.VerboDeRestaurar });
	}
}
