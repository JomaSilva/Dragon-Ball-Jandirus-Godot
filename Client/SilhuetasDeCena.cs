using Godot;

namespace Jandirus.Client;

/// <summary>
/// O SIMBOLO DO CORE -> O ARQUIVO, pra silhueta de luz que a cinematica acende sobre o corpo.
///
/// Irma da <see cref="ColadasDeForma"/> e da <see cref="SpriteDeAura.CaminhoDa"/>, e pela mesma
/// fronteira: quem DECIDE qual folha e o Core (<see cref="Jandirus.Core.Forms.Cinematica.Silhueta"/>,
/// campo da cena), e o Core nao conhece o Godot. Aqui so se traduz simbolo em `res://`.
///
/// ============================ AS TRES FOLHAS ESTAVAM NO DISCO E ORFAS ============================
/// `bioto2.tres`, `bioto3.tres` e `flashtrans.tres` estao convertidas, importadas e com os atlas
/// gerados desde o pipeline -- e nao tinham UM leitor em `.cs` no repositorio inteiro. E o mesmo
/// modo de falha que este projeto ja pagou duas vezes (as 229 folhas de beam, os 35 atlas mortos):
/// arte pronta que ninguem pede nao da erro nenhum, so nao aparece.
/// ============================================================================================
/// </summary>
public static class SilhuetasDeCena
{
	/// <summary>
	/// `bioto2.dmi` -- a metamorfose PRA forma semi-perfeita (`cinematics/imperfecttrans.dm:5`,
	/// `DNALabs.dm:563`). Silhueta branco-ciano do corpo, 32x32, quatro direcoes de 8 quadros.
	/// </summary>
	public const string BioSemiPerfeito =
		"res://Assets/Sprites/Character Icons/Bioandroids/bioto2.tres";

	/// <summary>`bioto3.dmi` -- a metamorfose PRA forma perfeita (`cinematics/perfecttrans.dm:5`).</summary>
	public const string BioPerfeito =
		"res://Assets/Sprites/Character Icons/Bioandroids/bioto3.tres";

	/// <summary>
	/// `flashtrans.dmi` -- o clarao branco com que a carapaca larval se rompe
	/// (`flick('flashtrans.dmi', src)`, `DNALabs.dm:509`).
	/// </summary>
	public const string Rompimento = "res://Assets/Sprites/Misc/flashtrans.tres";

	/// <inheritdoc cref="SilhuetasDeCena"/>
	public static string? CaminhoDa(Jandirus.Core.Forms.FolhaDeSilhueta f) => f switch
	{
		Jandirus.Core.Forms.FolhaDeSilhueta.Rompimento => Rompimento,
		Jandirus.Core.Forms.FolhaDeSilhueta.BioSemiPerfeito => BioSemiPerfeito,
		Jandirus.Core.Forms.FolhaDeSilhueta.BioPerfeito => BioPerfeito,

		// `Nenhuma` -- e e a resposta das 39 cenas que nao sao do bio. NULO e nao vazio: quem o
		// recebe apaga a camada, que e o gesto certo pra "esta cena nao acende silhueta".
		_ => null,
	};

	/// <summary>
	/// A ARTE DESTE SIMBOLO ESTA IMPORTADA? Nao "existe na pasta" -- IMPORTADA. Mesma pergunta (e
	/// mesma justificativa) da <see cref="ColadasDeForma.Existe"/>. Pra bancada.
	/// </summary>
	public static bool Existe(Jandirus.Core.Forms.FolhaDeSilhueta f) =>
		CaminhoDa(f) is { } c && ResourceLoader.Exists(c);
}
