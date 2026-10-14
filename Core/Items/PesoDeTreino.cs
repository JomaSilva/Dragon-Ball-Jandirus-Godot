namespace Jandirus.Core.Items;

/// <summary>
/// **OS PESOS DE TREINO: O PESO MAXIMO SE COMPRA.**
///
/// ============================ NAO E PORTE: E REGRA DO DONO (2026-10-12) ============================
/// *"peso atualmente vc so paga pra fabricar ele e nao gasta pra dar upgrade no peso maximo dele, oq ta errado, vc
/// deveria comprar ele e ele vir bem leve (50kg) e ai vc tem q gastar pra dar upgrade nele pra aumentar o peso maximo
/// pra vc poder usar, e ai vc pode por vontade propria diminuir esse peso em % em funçao do peso maximo q vc
/// colocou"*.
///
/// NO DM o `Upgrade` dos pesos e DE GRACA (`Tier 1.dm:138-151`): ele leva o teto ate duas vezes o limite do corpo
/// (`weight_cap_hw * WEIGHT_ITEM_CAP_MULT`), e o `Change_Weight` escolhe a fracao. O port ja nascia com esse teto
/// cheio -- fabricou, vestiu o dobro do que o corpo aguenta.
///
/// AQUI:
///   * os pesos nascem com <see cref="KgDeFabrica"/> de peso maximo;
///   * cada melhoria DOBRA o maximo (<see cref="ProximoMaximo"/>) e custa <see cref="ZeniPorNivel"/> a mais que a
///     anterior (<see cref="PrecoEmZeni"/>) -- em zeni, ou o dobro em recursos, como toda melhoria de maquina;
///   * quanto se VESTE continua sendo uma porcentagem do maximo, escolhida na mochila.
/// O limite do CORPO nao mudou de lugar: vestir mais do que ele aguenta continua esmagando (`Esmagamento`). Quem
/// decide ate onde vale a pena melhorar e o proprio corpo.
/// ====================================================================================================
///
/// O MAXIMO MORA NA FICHA (`Fighter.pesoMaximoKg`), e nao no item: a mochila guarda id e quantidade, e o item nao
/// tem estado proprio. Zero quer dizer "nunca melhorou" -- o de fabrica.
/// </summary>
public static class PesoDeTreino
{
	/// <summary>O peso maximo de fabrica, em kg. *"vir bem leve (50kg)"*.</summary>
	public const double KgDeFabrica = 50;

	/// <summary>Cada melhoria multiplica o maximo por isto.</summary>
	public const double VezesPorMelhoria = 2;

	/// <summary>A primeira melhoria custa isto em zeni; cada uma depois, isto a mais.</summary>
	public const double ZeniPorNivel = 500;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): o teto volta a ser duas vezes o limite do corpo, de graca -- os
	/// pesos de antes de 2026-10-12. Lido pelo `GameServer.TetoDePeso`. Falso em jogo, sempre.
	/// </summary>
	public static bool TetoDoCorpoDeGracaDeTeste;

	/// <summary>O maximo que vale: o da ficha, e nunca menos que o de fabrica.</summary>
	public static double Maximo(double daFicha) => Math.Max(daFicha, KgDeFabrica);

	/// <summary>Quantas melhorias este maximo ja levou (0 = de fabrica).</summary>
	public static int Nivel(double daFicha) =>
		(int)Math.Round(Math.Log(Maximo(daFicha) / KgDeFabrica, VezesPorMelhoria));

	public static double ProximoMaximo(double daFicha) => Maximo(daFicha) * VezesPorMelhoria;

	/// <summary>O preco da PROXIMA melhoria, em zeni: 500 a primeira, 1.000 a segunda, 1.500 a terceira...</summary>
	public static double PrecoEmZeni(double daFicha) => ZeniPorNivel * (Nivel(daFicha) + 1);

	// =====================================================================
	// A ARTE DOS PESOS NO CORPO (dono, 2026-10-13)
	// =====================================================================
	// *"permita tb o jogador a mudar o icone do peso (assim como é quando cria uma armadura) e quando ele equipar n
	// esqueça de colocar como overlay no personagem assim como o scouter deveria ser (mesma logica das roupas normais
	// e armadura)"*.
	//
	// NO DM os pesos SAO roupa: `obj/items/Weight` usa `Clothes_ShortSleeveShirt.dmi` (`Tier 1.dm:107`), vestir faz
	// `usr.overlayList += icon` (`:124`), e o `verb/Icon()` (`:167-175`) troca a folha por um arquivo que o jogador
	// envia. Aqui a troca e por uma folha do guarda-roupa (a mesma grade da armadura), e a escolha mora na ficha
	// (`Fighter.pesoArte`), ao lado do peso maximo e pelo mesmo motivo: o item da mochila nao tem estado proprio.

	/// <summary>A acao da mochila que abre a grade de artes. O botao e o `case` do servidor leem daqui.</summary>
	public const string AcaoArte = "arte";

	/// <summary>A folha de fabrica -- `icon='Clothes_ShortSleeveShirt.dmi'` (`Tier 1.dm:107`). E o que o `Default` do `Icon()` devolve.</summary>
	public const string ArtePadrao = "Clothes_ShortSleeveShirt";

	/// <summary>
	/// O caminho da folha que os pesos deste personagem usam no corpo: a escolhida, ou a de fabrica quando ele nunca
	/// escolheu (ou a escolhida saiu do catalogo). Nulo sem catalogo.
	/// </summary>
	public static string? Folha(string arteDaFicha, Appearance.VisualCatalog? cat) =>
		cat == null ? null
		: (arteDaFicha.Length > 0 ? cat.Peca(arteDaFicha) : null) ?? cat.Peca(ArtePadrao);

	/// <summary>As artes que a grade oferece: o guarda-roupa inteiro, na ordem do catalogo -- (nome do arquivo, caminho).</summary>
	public static List<(string Nome, string Caminho)> Escolhiveis(Appearance.VisualCatalog cat)
	{
		var saida = new List<(string, string)>();
		foreach (string caminho in cat.Roupas)
			saida.Add((Appearance.VisualCatalog.NomeDoArquivo(caminho), caminho));
		return saida;
	}
}
