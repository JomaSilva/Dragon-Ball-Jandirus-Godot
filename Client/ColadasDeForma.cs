using Godot;

namespace Jandirus.Client;

/// <summary>
/// O SIMBOLO DO CORE -> O ARQUIVO, pras camadas coladas no corpo. **UMA traducao, e ela mora aqui.**
///
/// Irma da <see cref="SpriteDeAura.CaminhoDa"/> e pelo mesmo motivo escrito la: quem DECIDE e o Core
/// (<see cref="Jandirus.Core.Forms.Catalogo.Coladas"/>, derivado da linha e da ordem da forma), e o
/// Core nao conhece o Godot. Aqui so se traduz simbolo em `res://`, que e a fronteira exata.
///
/// ============================ POR QUE UM ARQUIVO PROPRIO E NAO UM `switch` NO VISUAL ============================
/// O `CharacterVisual` monta as camadas; ele nao precisa saber onde a arte mora. E o precedente e o
/// do <see cref="CabelosDeForma"/>, que resolve caminho de cabelo do lado de fora pelo mesmo motivo:
/// no dia em que a bancada quiser conferir que a arte existe (e ela vai -- ver <see cref="Existe"/>),
/// ela pergunta a este arquivo em vez de instanciar um boneco.
/// ==========================================================================================================
/// </summary>
public static class ColadasDeForma
{
	private const string Pasta = "res://Assets/Sprites/Auras/";

	/// <summary>
	/// `LSSJpowerz.tres` -- 6 quadros de 32x32, animacao unica. Ver
	/// <see cref="Jandirus.Core.Forms.FolhaColada.PoderLendario"/>: nao vem do DM, vem do dono.
	/// </summary>
	public const string FolhaPoderLendario = Pasta + "LSSJpowerz.tres";

	/// <summary>`god - grey.tres` -- a folha CINZA, a unica que se pinta.</summary>
	public const string FolhaAmeacadora = Pasta + "god - grey.tres";

	/// <summary>`god.tres` -- `gkoverlay` (`AuraObject.dm:14`). Ja laranja.</summary>
	public const string FolhaDeus = Pasta + "god.tres";

	/// <summary>`god blue.tres` -- `sgkoverlay` (`AuraObject.dm:16`). Ja azul.</summary>
	public const string FolhaDeusAzul = Pasta + "god blue.tres";

	/// <inheritdoc cref="ColadasDeForma"/>
	public static string CaminhoDa(Jandirus.Core.Forms.FolhaColada f) => f switch
	{
		Jandirus.Core.Forms.FolhaColada.PoderLendario => FolhaPoderLendario,
		Jandirus.Core.Forms.FolhaColada.Ameacadora => FolhaAmeacadora,
		Jandirus.Core.Forms.FolhaColada.DeusAzul => FolhaDeusAzul,
		_ => FolhaDeus,
	};

	/// <summary>
	/// A ARTE DESTE SIMBOLO ESTA IMPORTADA? Nao "existe na pasta" -- IMPORTADA.
	///
	/// Este projeto ja perdeu tempo duas vezes com arte que estava no disco e que o Godot nunca
	/// importou (os 35 atlas de animacao; depois os sons, cujo efeito era silencio). `ResourceLoader`
	/// pergunta ao banco de recursos importados, que e a unica pergunta que corresponde ao que o jogo
	/// vai conseguir carregar. Pra bancada.
	/// </summary>
	public static bool Existe(Jandirus.Core.Forms.FolhaColada f) => ResourceLoader.Exists(CaminhoDa(f));
}
