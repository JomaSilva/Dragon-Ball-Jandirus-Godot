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

	/// <summary>A pose do icone dos pesos -- a do item de fabrica (`CatalogoDeItens`): a camisa de frente.</summary>
	public const string PoseDoIcone = "walk_south";

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v030teste` e `--diagv030`): a arte escolhida so aparece no CORPO -- na mochila, na aba
	/// Equip e no chao os pesos continuam com o icone de fabrica, o jogo de antes. Lido pelo icone do cliente
	/// (`Miniaturas.DosPesos`) e pelo servidor ao largar (`GameServer.LargarItem`). Falso em jogo, sempre.
	/// </summary>
	public static bool ArteSoNoCorpoDeTeste;

	// =====================================================================
	// ATE O LIMITE DO CORPO DE UMA VEZ (dono, 2026-10-15)
	// =====================================================================
	// *"o peso deveria ter uma opçao q vc compra seu peso maximo do personagem, sem ter q ficar dobrando o peso varias vezes
	// ate chegar no q vc quer, e claro o preço proporcional ao peso q vc precisa, e ai se quiser vc pode dobrar e continuar
	// o upgrade atual"*. O "peso maximo do personagem" e o LIMITE DO CORPO -- o recorde `weight_cap_hw`, o que ele aguenta
	// sem esmagar em gravidade 1x. O botao compra de uma vez as melhorias que faltam pra o maximo dos pesos passar dele, pelo
	// preco de cada uma somado: e o mesmo dinheiro de dobrar uma a uma, num clique so. O maximo continua num degrau
	// (50 kg x 2^n) -- um numero exato do limite entregaria o poder do corpo, que e funcao direta dele.

	/// <summary>A acao da mochila (e o `case` do servidor).</summary>
	public const string AcaoAteOLimite = "limite";

	/// <summary>Nenhuma compra leva mais longe que isto de uma vez (50 kg x 2^40 ja passa de qualquer corpo).</summary>
	public const int MaximoDeMelhoriasDeUmaVez = 40;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): a compra ate o limite nao existe -- o jogo de antes, em que so se dobrava.
	/// Lido por <see cref="MelhoriasAteOLimite"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SoDobrandoDeTeste;

	/// <summary>Quantas melhorias faltam pro maximo dos pesos alcancar o limite do corpo (0 = ja alcancou).</summary>
	public static int MelhoriasAteOLimite(double daFicha, double limiteDoCorpoKg)
	{
		if (SoDobrandoDeTeste || limiteDoCorpoKg <= Maximo(daFicha)) return 0;
		int n = (int)Math.Ceiling(Math.Log(limiteDoCorpoKg / Maximo(daFicha), VezesPorMelhoria) - 1e-9);
		return Math.Clamp(n, 1, MaximoDeMelhoriasDeUmaVez);
	}

	/// <summary>O preco, em zeni, de <paramref name="n"/> melhorias seguidas a partir deste maximo: a soma do preco de cada uma.</summary>
	public static double PrecoDeVariasEmZeni(double daFicha, int n)
	{
		int nivel = Nivel(daFicha);
		double soma = 0;
		for (int k = 1; k <= n; k++) soma += ZeniPorNivel * (nivel + k);
		return soma;
	}

	/// <summary>O maximo depois de <paramref name="n"/> melhorias.</summary>
	public static double MaximoDepoisDe(double daFicha, int n) => Maximo(daFicha) * Math.Pow(VezesPorMelhoria, n);

	// =====================================================================
	// A PORCENTAGEM GUARDADA (dono, 2026-10-15)
	// =====================================================================
	// *"dps de setar a % do peso maximo ele salvaria aquele valor e vc so precisaria clicar na opcao 'equipar' pra por o peso
	// ao inves de colocar a % toda vez"*. A porcentagem mora na ficha (`Fighter.pesoPct`), quem a escreve e o "ajustar" e
	// quem a veste e o "equipar" (`GameServer.VestirOsPesos`).

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v030teste` e `--diagv030`): a porcentagem escolhida nao fica guardada -- o "equipar" nao
	/// tem com o que vestir, e a % volta a ser pedida toda vez (o jogo de antes). Lido pelo ajuste do servidor
	/// (`GameServer.AjustarPeso`). Falso em jogo, sempre.
	/// </summary>
	public static bool PorcentagemEsquecidaDeTeste;
}
