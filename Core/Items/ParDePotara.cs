namespace Jandirus.Core.Items;

/// <summary>
/// (2026-10-23) O PAR DE BRINCOS POTARA QUE O KAIOSHIN DA -- a regra do dono depois do relato dos jogadores (*"deu problema com as
/// potaras"*). O que eles faziam era ROUPA: o "esquerdo" e o "direito" da Materialize sao pecas do guarda-roupa, so aparencia, e nao
/// fundem ninguem; e o par de verdade (<see cref="CatalogoDeItens.BrincosPotara"/>) sumia em 1 s da mochila de quem nao era Kaioshin. A
/// decisao: *"Kaioshin entrega o par"* -- o Kaioshin DA um par a outro jogador, que fica com ele; quem tem o par joga no alvo marcado e os
/// dois se fundem pelo fluxo de sempre (o convite, o aceite em 60 s, ate 20 tiles). Os brincos de roupa sairam do guarda-roupa.
///
/// ============================ O QUE FOI DECIDIDO AQUI (decisoes minhas, anotadas pro dono) ============================
///   * O CARGO ENTREGA UM PAR POR DIA REAL (<see cref="HorasEntreParesDoCargo"/>): o Kaioshin que da o par (ou o larga, guarda num bau,
///     perde num saque) fica sem ate 24 h depois do ultimo que o cargo lhe entregou. Repor na hora seria a fabrica de Potara (dar, ganhar
///     outro no tique seguinte, dar de novo); "um por vez" pediria saber onde o par foi parar (a mochila de quem esta fora do jogo, um bau,
///     o chao) -- e um por dia e a mesma conta do Ritual do Velho Kaiohshin, outro dom do mesmo cargo.
///   * O PAR SE GASTA NA FUSAO DE QUEM NAO E KAIOSHIN (<see cref="SeGastaNaFusao"/>): sem isso cada par dado seria uma Potara pra sempre
///     (uma fusao a cada hora e meia -- a meia hora dela e a recarga de 1 h), e o estoque so cresceria, um por dia por Kaioshin. O par do
///     proprio Kaioshin nao se gasta: e a Potara de hoje dele, e ninguem pediu pra mudar isso.
///   * O DM SUGERE MENOS: *"Kaioshins start with two"* (`Fusion.dm:608`) -- um par na vida, que nunca se repoe (o unico codigo que o
///     criaria esta COMENTADO, `CharacterCreation.dm:81-87`) e nunca se gasta (a fusao so o desequipa, `Fusion.dm:679-684`). La isso nao
///     virava fabrica porque o par nem chegava a existir; aqui o dono quer que o Kaioshin os distribua.
/// ===============================================================================================================
/// </summary>
public static class ParDePotara
{
	/// <summary>De quanto em quanto tempo o cargo de Kaioshin entrega um par novo a quem ficou sem (horas reais). Ver o cabecalho.</summary>
	public const double HorasEntreParesDoCargo = 24;

	public const long MsEntreParesDoCargo = (long)(HorasEntreParesDoCargo * 3600 * 1000);

	/// <summary>
	/// A ACAO DE DAR O PAR a quem esta marcado -- o botao da mochila e o `case` do servidor (`GameServer.DarOsBrincos`) leem daqui. So os
	/// brincos a declaram: dar item a outro jogador nao existe no resto do jogo (o caminho de todo item e o chao), e o dono pediu o gesto
	/// pra eles.
	/// </summary>
	public const string AcaoDar = "dar";

	/// <summary>
	/// ATE ONDE O PAR SE DA: o mesmo alcance de jogar os brincos no alvo marcado (<see cref="Social.Fusao.TilesDaPotara"/>, o `oview(usr,20)`
	/// do DU). Dar e o gesto irmao de jogar, e um alcance diferente faria o jogador medir a distancia duas vezes.
	/// </summary>
	public const int TilesPraDar = Social.Fusao.TilesDaPotara;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--potarateste`): o cargo repoe o par NA HORA -- a fabrica de Potara (dar, ganhar outro no tique seguinte,
	/// dar de novo). Lido pela propria <see cref="CargoEntrega"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CargoRepoeNaHoraDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--potarateste`): o par de quem nao e Kaioshin NAO se gasta na fusao -- cada par dado vira uma Potara pra
	/// sempre. Lido pela propria <see cref="SeGastaNaFusao"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ParNaoSeGastaDeTeste;

	/// <summary>
	/// O CARGO ENTREGA UM PAR AGORA? <paramref name="entregueEm"/> e quando ele entregou o ultimo (ms do relogio de parede, 0 = nunca; e o
	/// `Fighter.ParDePotaraEm`), e e escrito AQUI quando a resposta e sim.
	///
	/// O PAR QUE ELE JA TEM CONTA COMO O DE HOJE: quem ja segurava um par sem carimbo (o Kaioshin de antes desta regra, ou o que recebeu um
	/// de outro antes de subir ao trono) ganha o carimbo na primeira passada -- sem isso o primeiro par que ele desse viria de volta no tique
	/// seguinte, e o primeiro dia teria dois.
	/// </summary>
	public static bool CargoEntrega(ref long entregueEm, bool temPar, long agora)
	{
		if (temPar)
		{
			if (entregueEm <= 0) entregueEm = agora;
			return false;
		}
		if (!CargoRepoeNaHoraDeTeste && entregueEm > 0 && agora - entregueEm < MsEntreParesDoCargo) return false;
		entregueEm = agora;
		return true;
	}

	/// <summary>Quantas horas faltam pro cargo entregar o proximo par (0 = ja). Pra frase de quem acabou de dar o dele.</summary>
	public static double HorasAteOProximo(long entregueEm, long agora) =>
		Math.Max(0, (entregueEm + MsEntreParesDoCargo - agora) / 3_600_000.0);

	/// <summary>O par de quem convidou se gasta nesta fusao? So o de quem nao e Kaioshin -- ver o cabecalho.</summary>
	public static bool SeGastaNaFusao(bool convidouEhKaioshin) => !convidouEhKaioshin && !ParNaoSeGastaDeTeste;

	// =====================================================================
	// OS BRINCOS DE ROUPA, QUE SAIRAM DO JOGO
	// =====================================================================
	/// <summary>
	/// UM DOS QUATRO BRINCOS DE ROUPA (`potara`, `Potara-Equipped`, `potaraleft`, `potararight`), pelo nome do arquivo -- o mesmo pedaco que o
	/// extrator recusa no guarda-roupa (`ForaDoGuardaRoupa`, `Tools/AssetPipeline/DmAppearanceScanner.cs`). Ele so enganava: era o "esquerdo"
	/// e o "direito" que os jogadores faziam com a Materialize achando que era a Potara.
	/// </summary>
	public static bool EhBrincoDeRoupa(string caminho) =>
		Appearance.VisualCatalog.NomeDoArquivo(caminho).Contains("potara", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// QUEM JA TINHA UM BRINCO DE ROUPA O PERDE NO LOGIN (decisao minha, anotada pro dono): o vestido sai do corpo AQUI (`AccountStore.ParaJogador`),
	/// e o da mochila sai no `Inventario.Sanear` da mesma carga, porque o catalogo nao o conhece mais. Ficar "so como roupa" deixaria o
	/// brinco no corpo de quem achava que tinha a Potara -- a mesma confusao do relato -- e uma peca que nao volta pra mochila (o
	/// `TirarRoupa` a guarda como item, e ela nao e mais item). O brinco da FUSAO nao passa por aqui: ele mora na aparencia emprestada
	/// (`LookDeFusao`), que nunca vai pro disco. Devolve quantos tirou.
	/// </summary>
	public static int TirarOsBrincosDeRoupa(Appearance.Appearance visual) =>
		BrincoDeRoupaFicaNoCorpoDeTeste ? 0 : visual.Roupa.RemoveAll(p => EhBrincoDeRoupa(p.Caminho));

	/// <summary>
	/// DEFEITO INJETADO (bancada `--potarateste`): o brinco de roupa vestido FICA no corpo depois do login -- o jogo de antes. Lido pela propria
	/// <see cref="TirarOsBrincosDeRoupa"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool BrincoDeRoupaFicaNoCorpoDeTeste;
}
