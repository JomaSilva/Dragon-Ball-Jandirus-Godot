namespace Jandirus.Core.Items;

/// <summary>
/// **O QUE SE USA APARECE NO CORPO** -- o scouter ligado e os pesos vestidos, por cima do boneco.
///
/// Dono, 2026-10-13: *"equipar o scouter e o peso n aparece no sprite do personagem como overlay (oq deveria)"*.
///
/// E O QUE O DM FAZ, nos dois: `usr.overlayList += icon` no `Equip()` do scouter (`Tier 1.5.dm:361`) e no dos pesos
/// (`Tier 1.dm:124`), e `overlayList -= icon` ao tirar. O port ligava a MECANICA dos dois (o bit de leitura de poder,
/// o `Weighted`) e nunca o desenho: o boneco de quem usava scouter era igual ao de quem nao usava.
///
/// ============================ SAO FATOS DO CORPO, E NAO DA FICHA DE APARENCIA ============================
/// Nenhum dos dois mora em `Appearance.Roupa`: o scouter ligado e um bit que nao vai pro disco
/// (`ServerPlayer.PoderesConcedidos`) e os pesos vestidos sao o `Fighter.Weighted`. Gravar "estou de scouter" na
/// aparencia seria a segunda resposta pra mesma pergunta. Entao quem responde e o servidor, na hora de montar o
/// `S2C.PeerLook` (`GameServer.PacoteDeAparencia`), e a resposta viaja ao lado da aparencia -- o mesmo desenho do
/// potencial desperto na pele (`Appearance.PotencialNaPele`). O cliente so desenha.
///
/// E POR ISSO ELES NAO OCUPAM VAGA DE ROUPA (`Appearance.MaxRoupa`) nem saem no saque como peca do corpo: o que se
/// rouba e o ITEM, e o item em uso fica com o dono (`Saque.EmUso`).
/// ===========================================================================================================
/// </summary>
public static class EquipamentoNoCorpo
{
	/// <summary>
	/// A folha que o scouter veste no rosto: e o `Scouter.dmi` do item (`Tier 1.5.dm:325`), que ja vem com as poses do
	/// corpo (`walk_south`, `train_east`, `ko`...) porque no original ele sempre foi overlay.
	/// </summary>
	public const string FolhaDoScouter = "res://Assets/Sprites/Misc/Objects/Technology/Scouter.tres";

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v021teste` e `--diagv021`): o que se usa volta a nao aparecer no corpo -- a v0.2.0.
	/// Lido pelas duas perguntas daqui. Falso em jogo, sempre.
	/// </summary>
	public static bool NaoApareceNoCorpoDeTeste;

	/// <summary>O scouter aparece no rosto deste corpo? So ligado.</summary>
	public static bool ScouterNoRosto(bool ligado) => ligado && !NaoApareceNoCorpoDeTeste;

	/// <summary>
	/// A folha dos pesos que este corpo veste AGORA, ou vazio quando ele nao veste nenhum: tem de haver peso no corpo
	/// (`Weighted > 0`) -- os pesos guardados na mochila nao aparecem, como a roupa guardada.
	/// </summary>
	public static string PesosNoCorpo(double weighted, string arteDaFicha, Appearance.VisualCatalog? cat) =>
		weighted > 0 && !NaoApareceNoCorpoDeTeste ? PesoDeTreino.Folha(arteDaFicha, cat) ?? "" : "";

	/// <summary>
	/// As folhas das luvas e das botas de dreno (v0.2.1): as mesmas `Clothes_Gloves.dmi` e `Clothes_Boots.dmi` do item
	/// (`Tier 1.5.dm:253` e `:289`), que no original entram no `overlayList` de quem as veste.
	/// </summary>
	public const string FolhaDasLuvasDeDreno = "res://Assets/Sprites/Clothes/Clothes_Gloves.tres",
						FolhaDasBotasDeDreno = "res://Assets/Sprites/Clothes/Clothes_Boots.tres";

	/// <summary>As luvas e as botas de dreno aparecem neste corpo? So as VESTIDAS (`Tech.DrenoDeEnergia.Vestidas`).</summary>
	public static (bool Luvas, bool Botas) DrenoNoCorpo(Inventario mochila) =>
		NaoApareceNoCorpoDeTeste ? (false, false) : Tech.DrenoDeEnergia.Vestidas(mochila);
}
