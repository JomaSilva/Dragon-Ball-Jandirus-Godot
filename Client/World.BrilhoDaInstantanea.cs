using Godot;

namespace Jandirus.Client;

// (2026-10-19) O BRILHO DA TRANSFORMACAO INSTANTANEA -- o pedido do dono e os numeros moram no Core
// (`Jandirus.Core.Forms.BrilhoDaInstantanea`). Arquivo proprio porque o `World.cs` e quente: la entra so a chamada, no fim do caminho
// sem cena do `AoMudarForma`.
public partial class World
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagbrilhoforma`): a instantanea SECA de antes -- a forma entra no corpo sem brilho nenhum. Lido
	/// pela propria linha do conserto, em <see cref="BrilharNaInstantanea"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool InstantaneaSecaDeTeste;

	/// <summary>
	/// O CORPO LAVA DE BRANCO E ESCOA -- o terceiro degrau (`DegrauDeCena.Nenhuma`) chegando. O `AoMudarForma` so chama isto no
	/// caminho SEM cena, so com forma nova (voltar ao normal nao e "se transformar") e so quando houve troca (`de != para`).
	///
	/// PELO `Banhar` E NAO PELO `Impacto`: e o canal do soco sem o gesto do soco (achatar e empurrar) -- ver `CharacterVisual.Banhar`.
	/// E o ultimo a escrever ganha: um soco no meio do brilho corta o brilho, e um beat de banho de uma cena tambem.
	/// </summary>
	private static void BrilharNaInstantanea(Node2D corpo)
	{
		if (InstantaneaSecaDeTeste) return;
		corpo.GetNodeOrNull<CharacterVisual>("Visual")?.Banhar(
			new Color(Jandirus.Core.Forms.BrilhoDaInstantanea.Cor),
			Jandirus.Core.Forms.BrilhoDaInstantanea.Segundos,
			Jandirus.Core.Forms.BrilhoDaInstantanea.Lavagem);
	}
}
