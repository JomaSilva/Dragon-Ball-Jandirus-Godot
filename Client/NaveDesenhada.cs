using Godot;

namespace Jandirus.Client;

/// <summary>
/// A NAVE EM CIMA DE QUEM A PILOTA.
///
/// ============================ POR QUE ELA E FILHA DO CORPO ============================
/// No original o pod copia a posicao do piloto cinco vezes por segundo (`loc =
/// locate(pilot.x,pilot.y,pilot.z)` dentro de um `while`, `PlanetTech.dm:140-144`). Aqui ela e
/// FILHA do node do corpo, e a copia deixa de existir: o Godot ja move o filho junto do pai, sem um
/// `_Process` por veiculo e sem um quadro de atraso.
///
/// Isso importa numa nave de 39x: a 6.240 px/s, um unico quadro de atraso poria o pod 104 px atras
/// do piloto -- tres tiles de rastro, que e exatamente a aparencia de "o desenho descolou".
/// ====================================================================================
///
/// ============================ POR CIMA, E NAO POR TRAS ============================
/// `plane = 6` no `obj/Spacepod` (`PlanetTech.dm:44`), acima da camada dos mobs: no original quem
/// entra no pod some dentro dele. Desenhar por tras faria a leitura errada -- um boneco andando com
/// uma capsula colada nas costas, em vez de uma capsula voando.
/// ================================================================================
///
/// QUEM A CRIA E A DESTROI e o <see cref="World.MarcarNaNave"/>, pelo bit `Pilotando` do snapshot.
/// Ela nao guarda estado nenhum -- existir JA E o estado.
/// </summary>
public partial class NaveDesenhada : Node2D
{
	/// <summary>O nome do node. Uma casa so: quem cria e quem destroi procuram por ele.</summary>
	public const string NomeDoNode = "Nave";

	/// <summary>res:// do SpriteFrames. Vazio = nada e desenhado (falha visivel, nao silenciosa).</summary>
	public string Arte = "res://Assets/Sprites/DU/Items/Spacepod.tres";

	/// <summary>
	/// ESTA E A CAPITAL SHIP, e nao um pod. Vem do bit `EntityState.NaveGrande` do snapshot.
	///
	/// ============================ POR QUE O CASCO GRANDE NAO E "O MESMO SPRITE, MAIOR" ============================
	/// Sao 128x138 px contra 32x32, e o DM ancora o casco com `pixel_x = -48` (`ShipVessel.dm:74`)
	/// com o comentario que explica tudo: *"the 4x4-tile sprite anchors bottom-left; shift it left so
	/// the loc/dense tile sits under the DOOR (hitbox = the visible door)"*. Ou seja: o ponto que o
	/// jogo considera "onde a nave esta" e a PORTA dela, e nao o meio do casco.
	///
	/// Aqui a nave esta montada num CORPO e nao numa celula, entao o que se quer e o casco centrado
	/// em cima de quem pilota -- o deslocamento do DM e sobre o canto da celula e nao se aplica. O
	/// que se aplica e a mesma leitura: a pessoa some dentro da nave.
	/// ========================================================================================================
	/// </summary>
	public bool Grande;

	/// <summary>A arte da Capital Ship -- `icon = 'Ship.dmi'` (`ShipVessel.dm:53`).</summary>
	private const string ArteDaNaveGrande = "res://Assets/Sprites/Misc/Ship.tres";

	private AnimatedSprite2D? _sprite;

	public override void _Ready()
	{
		// ACIMA DO BONECO: ver o bloco `plane = 6` no cabecalho.
		ZIndex = 1;

		if (Grande) Arte = ArteDaNaveGrande;

		if (!ResourceLoader.Exists(Arte)) { GD.PushWarning($"[nave] sem arte em {Arte}"); return; }
		if (ResourceLoader.Load<SpriteFrames>(Arte) is not { } folha) return;

		string anim = folha.HasAnimation("default")
			? "default"
			: folha.GetAnimationNames() is { Length: > 0 } nomes ? nomes[0] : "";
		if (anim.Length == 0) return;

		// CENTRADA NO CORPO, e nao ancorada na base como uma construcao: aqui ela nao ocupa celula
		// de chao nenhuma -- ela e um sprite montado num corpo que ja esta posicionado. O 4 pra
		// baixo aproxima o meio do pod dos PES (o centro do corpo fica `MoveRules.FeetOffsetY`
		// acima deles), pra a capsula nao parecer pendurada na cabeca.
		_sprite = new AnimatedSprite2D
		{
			SpriteFrames = folha,
			Animation = anim,
			Centered = true,
			Position = new Vector2(0, 4),
		};
		AddChild(_sprite);
		_sprite.Play();
	}
}
