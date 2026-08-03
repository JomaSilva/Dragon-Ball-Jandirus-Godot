namespace Jandirus.Core.Stats;

/// <summary>
/// O QUE AS TECNICAS ATIVAS PRECISAM DA FICHA: o dreno de Ki e as pericias que o dimensionam.
/// </summary>
public partial class Fighter
{
	// =====================================================================
	// PERICIAS DE KI que dimensionam tecnica (as `*skill` do original)
	// =====================================================================
	/// <summary>Quao forte sao os DEBUFFS que voce aplica (cegueira, paralisia). Cresce usando.</summary>
	public double kidebuffskill;

	/// <summary>
	/// Quanto Ki voce DESPERDICA. Entra dividindo em <see cref="BaseDrain"/> -- e o unico canal
	/// que faz tecnica ficar mais BARATA com o tempo, em vez de so mais forte.
	/// </summary>
	public double kiefficiencyskill;

	/// <summary>Modificadores de dreno: `P` e permanente, o outro e o de agora.</summary>
	public double PDrainMod = 1, DrainMod = 1;

	/// <summary>O botao do servidor pra deixar tudo mais caro ou mais barato de uma vez.</summary>
	public static double GlobalKiDrainMod = 1;

	/// <summary>
	/// O CUSTO-BASE DE QUALQUER TECNICA, 1:1 com o `BaseDrain()` do original:
	///
	///     sqrt(max(MaxKi,1)/140) * PDrainMod * globalKiDrainMod * DrainMod / log(9, max(kiefficiencyskill,5))
	///
	/// ELE ESCALA COM O SEU PROPRIO KI MAXIMO, e essa e a parte contra-intuitiva que precisa
	/// ficar registrada: uma tecnica de "100 de Ki" nao custa 100 -- custa 100 x BaseDrain, e
	/// BaseDrain cresce com a raiz do seu MaxKi. E de proposito (o comentario do original diz:
	/// "depois de certo Ki maximo, drenos pequenos nao fazem nada"), mas ja causou um bug de
	/// verdade no jogo antigo: o Planet Destroy pedia `1000*BaseDrain` e virou IMPAGAVEL pra
	/// quem tinha muito Ki. Custo fixo e custo escalado sao decisoes diferentes -- ao portar
	/// cada tecnica, olhe qual das duas o verb usava.
	/// </summary>
	public double BaseDrain()
	{
		double num = Math.Max(MaxKi, 1);
		double eff = Math.Log(Math.Max(kiefficiencyskill, 5)) / Math.Log(9);
		return Math.Sqrt(num / 140) * PDrainMod * GlobalKiDrainMod * DrainMod / eff;
	}
}
