namespace Jandirus.Core.World;

/// <summary>
/// AS SUB-ZONAS DE UM PLANETA -- os mapas do catalogo que SAO aquele planeta pro relogio, pro clima e pro fim do mundo, e o pedaco do
/// Lookout que e de OUTRO planeta. E o lugar UNICO que responde "de que planeta e o ceu deste chao?".
///
/// O dono (2026-10-21): *"sincronizar o horario, clima e efeitos de destruiçao de planetas em sub zonas do planeta, como cavernas do
/// planeta vegeta com o proprio planeta, lookout com a terra . e pode ir pro espaço do proprio lookout pois é considerado terra, as
/// cavernas de vegeta ainda nao dariam pelo fato de estarem em uma caverna e nao ceu a berto, e obviamente n da pra voar pro espaço pela
/// sala do tempo"*. E, perguntado sobre o fim do mundo, escolheu *"Efeitos e morte"*: o ceu, o tremor e a contagem chegam na caverna e
/// no Lookout, e quem estiver la MORRE JUNTO quando o planeta explode. A Sala do Tempo fica de fora de tudo.
///
/// ============================ O QUE O DM DIZ, CELULA A CELULA ============================
/// No DM "de que planeta e este chao" e a AREA da celula (`area/var/Planet`, `Areas.dm:38`): o relogio e o clima correm por area
/// (`Weather.dm`), e o fim do mundo varre toda area do planeta (`for(var/area/A in area_list) if(A.Planet == Planet)`,
/// `Area_Death.dm:19-22` e `:80-82`) -- as sub-zonas inclusive. Contado no `Maps/1to26.dmm`:
///   z12 Lookout ........ `/area/Lookout` (Planet "Earth", `Areas.dm:268-275`) e o resto `Outside` (as nuvens), MENOS o castelo do
///                        Rei Vegeta: `/area/Vegeta` (x 275..433, y 327..458) + `/area/Vegeta/Inside` (x 323..385, y 410..499), Planet
///                        "Vegeta" -- a escada `VCastleEntrance1` de Vegeta (`z03.passagens`) da nele, e as `VCastleExit1/2` voltam;
///   z23 Earth_Cave ..... toda celula andavel e `/area/Earth/Inside`;
///   z22 Vegeta_Cave .... toda celula andavel e `/area/Vegeta/Inside`;
///   z13 Sala do Tempo .. `/area/Hyperbolic_Time_Chamber`, Planet "Hyperbolic Time Dimension" -- OUTRA DIMENSAO, e fica de fora.
/// ==========================================================================================
///
/// ============================ POR QUE NAO E O `InteriorDoPlaneta` ============================
/// Aquele anda de boca em boca de passagem e responde *"indo ate la a pe, eu saio do planeta?"* (as Esferas na mochila): por esse
/// caminho o Lookout e da Terra E de Vegeta ao mesmo tempo, porque o castelo liga os dois. A pergunta daqui e outra -- *"de que planeta
/// e o CEU desta celula?"* -- e a resposta do DM e por celula. As cavernas vem da lista da <see cref="Cavernas"/> (a mesma da reserva de
/// minerio, e nao uma segunda copia); o Lookout e o castelo, desta classe.
/// ==========================================================================================
///
/// QUEM PERGUNTA: o relogio e a lua (<see cref="Ceu.RelogioDaZona"/>), o clima natural (<see cref="Clima.DaZona"/>,
/// <see cref="Clima.SalDaZona"/>), e no servidor o clima forcado, o fim do mundo e a subida pro espaco (`GameServer.SubZonas.cs`); no
/// cliente o ceu de onde o corpo esta e a trilha de combate do planeta em perigo (`World.SubZonas.cs`).
/// </summary>
public static class SubZonas
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste` e robo `--diagsubzonas`): a sub-zona volta a ter o relogio e a lua DELA, sorteados pelo
	/// proprio nome -- o jogo de antes de 2026-10-21, em que o Lookout tinha dia e lua cheia em outra hora que a Terra. Lido pela propria
	/// linha da regra, em <see cref="Ceu.RelogioDaZona"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CeuProprioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste`): a sub-zona volta a ter o clima natural DELA -- o Lookout e as cavernas, que nao estao
	/// no `planetas.json`, sem clima nenhum. Lido pelas duas linhas da regra, em <see cref="Clima.DaZona"/> e <see cref="Clima.SalDaZona"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool ClimaProprioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste` e robo `--diagsubzonas`): o castelo do Rei Vegeta e Lookout como o resto do mapa -- da
	/// Terra. Lido pela propria linha da regra, em <see cref="PlanetaMae(ZoneKey, Vec2)"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CasteloDaTerraDeTeste;

	/// <summary>O Lookout -- o z12, o Templo Sagrado de ceu aberto, com o castelo do Rei Vegeta num canto do mesmo mapa.</summary>
	public const string Templo = "Lookout";

	private static readonly ZoneKey Terra = ZoneKey.Premade("Earth");
	private static readonly ZoneKey Vegeta = ZoneKey.Premade("Vegeta");

	/// <summary>
	/// ============================ A CAIXA DO CASTELO, EM CELULAS DO z12 ============================
	/// O retangulo que cobre as duas areas de Vegeta do Lookout no `.dmm` (`/area/Vegeta` x 275..433 y 327..458 e `/area/Vegeta/Inside`
	/// x 323..385 y 410..499, na conta do conversor: `cx = x - 1`, `cy = 500 - y`). Conferido celula a celula contra o `.dmm` e os planos
	/// do mapa: TODA celula de Vegeta do z12 cai dentro dela, nenhuma fica fora; o que nao e Vegeta la dentro e parede (`Outside`, 3.936)
	/// e uma costura de 132 celulas de NUVEM marcada `/area/Lookout` na coluna x=322 (y 327..409), no meio do ceu do castelo -- aqui ela
	/// segue o castelo, e ninguem pisa nela sem voar. O chao andavel do castelo (1.386 ao ar livre, 1.427 sob teto) e todo de Vegeta.
	/// ===============================================================================================
	/// </summary>
	public const int CasteloX0 = 275, CasteloY0 = 327, CasteloX1 = 433, CasteloY1 = 499;

	/// <summary>Esta posicao do z12 (em pixels) cai no castelo do Rei Vegeta? Ver <see cref="CasteloX0"/>.</summary>
	public static bool NoCastelo(Vec2 pos)
	{
		int cx = (int)MathF.Floor(pos.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(pos.Y / ZoneCollision.TileSize);
		return cx >= CasteloX0 && cx <= CasteloX1 && cy >= CasteloY0 && cy <= CasteloY1;
	}

	/// <summary>
	/// O PLANETA DE QUE ESTA ZONA E SUB-ZONA -- nulo quando ela nao e sub-zona de ninguem (o proprio planeta, o espaco, a Sala do Tempo,
	/// o Outro Mundo, uma nave, a mente). Sem a posicao, o Lookout inteiro responde Terra: quem precisa do castelo pergunta com ela
	/// (<see cref="PlanetaMae(ZoneKey, Vec2)"/>).
	///
	/// SO MAPA DO CATALOGO: um mundo gerado ou um interior com o mesmo nome nao e sub-zona -- o nome sozinho nao e endereco (ver
	/// <see cref="ZoneKey"/>). E a mesma regra da <see cref="Cavernas.PlanetaMae"/>, que responde pelas cavernas.
	/// </summary>
	public static ZoneKey? PlanetaMae(ZoneKey zona)
	{
		if (zona.Kind != ZoneKey.KindPremade) return null;
		if (Cavernas.PlanetaMae(zona) is { } dela) return dela;
		return string.Equals(zona.Name, Templo, StringComparison.Ordinal) ? Terra : null;
	}

	/// <summary>
	/// O PLANETA DESTE CHAO, com a posicao: o castelo do Rei Vegeta e de Vegeta, o resto do Lookout e da Terra. E a pergunta de quem tem
	/// um corpo na mao (o servidor por jogador, o cliente pelo corpo local).
	/// </summary>
	public static ZoneKey? PlanetaMae(ZoneKey zona, Vec2 pos)
	{
		ZoneKey? mae = PlanetaMae(zona);
		if (mae != null && !CasteloDaTerraDeTeste && string.Equals(zona.Name, Templo, StringComparison.Ordinal) && NoCastelo(pos))
			return Vegeta;
		return mae;
	}

	/// <summary>DE QUE ZONA E O CEU DAQUI: o planeta, se esta e uma sub-zona dele; senao a propria zona.</summary>
	public static ZoneKey ZonaDoCeu(ZoneKey zona) => PlanetaMae(zona) ?? zona;

	/// <summary>A mesma pergunta com a posicao (o castelo). Ver <see cref="PlanetaMae(ZoneKey, Vec2)"/>.</summary>
	public static ZoneKey ZonaDoCeu(ZoneKey zona, Vec2 pos) => PlanetaMae(zona, pos) ?? zona;

	/// <summary>Esta zona tem MAIS DE UM CEU conforme o lugar? So o Lookout (a Terra e, no castelo, Vegeta).</summary>
	public static bool TemDoisCeus(ZoneKey zona) =>
		zona.Kind == ZoneKey.KindPremade && string.Equals(zona.Name, Templo, StringComparison.Ordinal);

	/// <summary>Os ceus que cabem nesta zona: os dois do Lookout, ou o unico de qualquer outra (o planeta-mae, ou ela mesma).</summary>
	public static IEnumerable<ZoneKey> CeusDaZona(ZoneKey zona)
	{
		if (TemDoisCeus(zona))
		{
			yield return Terra;
			yield return Vegeta;
			yield break;
		}
		yield return ZonaDoCeu(zona);
	}

	/// <summary>Todas as sub-zonas do catalogo: o Lookout e as cavernas -- pra quem varre (o tique do servidor, as bancadas).</summary>
	public static IEnumerable<ZoneKey> Todas()
	{
		yield return ZoneKey.Premade(Templo);
		foreach ((ZoneKey caverna, ZoneKey _) in Cavernas.Todas()) yield return caverna;
	}

	/// <summary>
	/// AS SUB-ZONAS EM QUE O CEU DESTE PLANETA PODE ESTAR: as cavernas dele e, da Terra e de Vegeta, o Lookout (no castelo, o de Vegeta).
	/// Quem as varre filtra corpo a corpo pela posicao (<see cref="ZonaDoCeu(ZoneKey, Vec2)"/>).
	/// </summary>
	public static IEnumerable<ZoneKey> SubZonasDe(ZoneKey planeta)
	{
		if (planeta.Kind != ZoneKey.KindPremade) yield break;
		foreach ((ZoneKey caverna, ZoneKey dela) in Cavernas.Todas())
			if (dela.Equals(planeta)) yield return caverna;
		if (planeta.Equals(Terra) || planeta.Equals(Vegeta)) yield return ZoneKey.Premade(Templo);
	}

	/// <summary>
	/// POR QUE DAQUI NAO SE SOBE PRO ESPACO -- vazio quando este lugar nao tem razao propria pra recusar (o Lookout sobe pelo planeta-mae;
	/// o resto segue as recusas de sempre). A caverna nao tem ceu aberto (dono: *"as cavernas de vegeta ainda nao dariam pelo fato de
	/// estarem em uma caverna e nao ceu a berto"*), e a Sala do Tempo e outra dimensao (*"obviamente n da pra voar pro espaço pela sala
	/// do tempo"*) -- a da Terra e as dos mundos sorteados, que sao o mesmo quarto.
	/// </summary>
	public static string PorQueNaoSobe(ZoneKey zona)
	{
		if (Cavernas.EhCaverna(zona)) return "você está numa caverna, sem céu aberto: daqui não se sobe pro espaço.";
		if (SalaDoTempo.EhUmaSala(zona)) return "a Sala do Tempo é outra dimensão: não há céu aqui pra romper.";
		return "";
	}
}
