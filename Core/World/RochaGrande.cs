namespace Jandirus.Core.World;

/// <summary>
/// A ROCHA GRANDE DOS PLANETAS GERADOS -- a formacao de pedra cinza de sete tiles de largura que o dono
/// fotografou em 2026-10-10: *"a segunda imagem mostra q esses rochas n tem fisica de colisao oq fica estranho
/// adicione colisao nessas rochas grandes"*.
///
/// ============================ O QUE ELA E, E POR QUE NAO TINHA FISICA ============================
/// Ela nasceu de um engano e ficou. O gerador sorteia veios nas colinas e um em cada quatro e "gema"
/// (`ProceduralSpace.dm:1352`, o `/obj/Raw_Material/Quartz` de `Mining.dm:67-69`, um minerio de 32x32 em
/// `White rock.dmi`). A paleta pedia o atlas `"White rock"`, o <see cref="CatalogoDeTiles"/> acha atlas sem
/// ligar pra caixa das letras, e o `tiles.json` tem DUAS folhas com esse nome: o minerio do jogo
/// (`White rock~Misc.Objects.Materials.Ores`, 32x32) e a `white rock` da arvore `DU/` (250x250, uma pilha de
/// pedras). O nome puro cai na segunda. O conversor de mapas consertou isso pros planetas desenhados a mao
/// (`Tools/AssetPipeline/IconesDoDm.cs`); o gerador continuou pedindo o nome puro.
///
/// E a fisica nunca existiu porque o veio era "coisa de pegar": `GeradorDeTerreno.Bloqueia` so contava
/// montanha e arvore, com a nota *"isto precisa de conferencia quando o sistema de mineracao for portado"*.
/// O desenho de 250 px ficava ancorado numa celula de colina LIVRE, e o servidor e o cliente -- que geram o
/// mesmo mapa da mesma seed -- concordavam que ali nao havia nada.
///
/// O dono viu as pedras, gostou delas e pediu a colisao. Entao elas ficam: a cobertura deixou de se chamar
/// gema, o atlas e pedido pelo nome dele (<see cref="Atlas"/>) e a pegada mora aqui.
/// =================================================================================================
///
/// ============================ A PEGADA E A BASE, E O DESENHO DESCE DOIS TILES ============================
/// O atlas tem 250x250 px e a pedra ocupa so o miolo: de 11 a 238 na horizontal e de 63 a 184 na vertical
/// (medido no alfa do PNG). O tileset o desenha centrado na celula e com o PE DO ATLAS no pe dela
/// (`texture_origin = (0, 109)`, a regra de todo tile maior que 32) -- e como sobram 65 px vazios embaixo, a
/// base da pedra ficava DUAS fileiras acima da celula que a carrega. A ancora ficava de fora do proprio
/// desenho, e quem parasse logo abaixo da pedra era desenhado ATRAS dela (o Y-sort ordena pela celula).
///
/// O desenho desce <see cref="Descida"/> tiles (quem desce e a camada do cliente, ver
/// `PlanetaProcedural.Camada`): a base da pedra passa a encostar no pe da celula da ancora, e a ancora fica
/// debaixo dela. Com isso a pedra cobre quatro fileiras, da ancora pra cima. Em porcentagem de cada celula
/// coberta pelo desenho (colunas de -4 a +4 em volta da ancora; medido no mesmo PNG):
///
///     fileira -3      0   0   3  73  84  74   6   0   0     o topo: anda-se por tras
///     fileira -2      0  15  68 100 100 100  77  37   0     o topo: anda-se por tras
///     fileira -1      1  84  93 100 100 100  96  78   2     PEGADA (sete celulas)
///     fileira  0      0  29  43  68  93  86   6   0   0     PEGADA (as quatro do meio)
///
/// A pegada e a BASE: a fileira de cima da ancora inteira e o miolo da fileira dela. As duas fileiras do
/// alto sao a altura da pedra -- o corpo passa por tras e some atras dela, que e o que se espera de uma
/// pedra mais alta que uma pessoa. E a ORDEM do desenho e a linha de cima da fileira da ancora
/// (<see cref="OrdemPx"/>): quem esta abaixo dela aparece na frente, quem esta acima aparece atras, e na
/// fileira -1 nao cabe ninguem.
/// ========================================================================================================
///
/// ============================ ELA NAO SE QUEBRA ============================
/// Toda celula da pegada entra tambem no plano do que nao se quebra (<see cref="ZoneCollision.Indestrutivel"/>).
/// O desenho e UM tile de onze celulas: derrubar uma delas abriria um buraco numa pedra que continua
/// desenhada inteira, e derrubar a da ancora apagaria o desenho com dez celulas de parede em volta. Decisao
/// deste port (o dono pediu a colisao e nao disse o que acontece com um soco): a pedra aguenta, como a
/// borda do mundo.
/// ===========================================================================
/// </summary>
public static class RochaGrande
{
	/// <summary>
	/// O atlas, pelo nome que o `tiles.json` da a ele (`DU/Map/white rock.png`). Em minusculas de proposito:
	/// e o nome DESTA folha, e nao o do minerio homonimo -- ver o cabecalho.
	/// </summary>
	public const string Atlas = "white rock";

	/// <summary>Quantos tiles o desenho desce em relacao a ancora padrao do tileset. Ver o cabecalho.</summary>
	public const int Descida = 2;

	/// <summary>A descida em pixels: o deslocamento da camada que desenha as rochas grandes.</summary>
	public const int DescidaPx = Descida * ZoneCollision.TileSize;

	/// <summary>
	/// ONDE O DESENHO ENTRA NA ORDEM DE Y, em pixels a somar ao centro da celula em que a camada (ja
	/// descida) o pinta: a linha de cima da fileira da ancora. E o `y_sort_origin` da camada.
	/// </summary>
	public const int OrdemPx = -(DescidaPx + ZoneCollision.TileSize);

	/// <summary>
	/// A PEGADA, em celulas a partir da ancora (x pra direita, y pra baixo). Ver a tabela do cabecalho.
	/// </summary>
	public static readonly (int Dx, int Dy)[] Pegada =
	[
		(-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (3, -1),
		(-2, 0), (-1, 0), (0, 0), (1, 0),
	];

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rochasteste`): a rocha grande volta a ser so desenho -- nenhuma celula
	/// da pegada barra nem endurece. E o mundo de antes do pedido, no mesmo binario. Falso em jogo, sempre.
	/// </summary>
	public static bool SemPegadaDeTeste;

	/// <summary>
	/// ESTA CELULA FICA DEBAIXO DA PEGADA DE ALGUMA ROCHA GRANDE? Pergunta pelas ancoras em volta: e o que
	/// o ponto de pouso usa pra nao escolher um chao que a pedra vai tomar.
	/// </summary>
	public static bool Cobre(int w, int h, byte[] cobertura, int x, int y)
	{
		foreach ((int dx, int dy) in Pegada)
		{
			int ax = x - dx, ay = y - dy;
			if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
			if (cobertura[ay * w + ax] == (byte)CoberturaDeTerreno.RochaGrande) return true;
		}
		return false;
	}

	/// <summary>
	/// O TAMANHO DO DESENHO, em celulas a partir da ancora: a fileira dela e as tres de cima, e quatro colunas
	/// pra cada lado -- a tabela do cabecalho inteira. E maior que a <see cref="Pegada"/>: nas duas fileiras do
	/// alto o chao e livre, e anda-se por tras da pedra.
	/// </summary>
	public const int FileirasDoDesenho = 4;
	public const int ColunasDeCadaLado = 4;

	/// <summary>
	/// ESTA CELULA FICA DENTRO DO DESENHO DE ALGUMA ROCHA GRANDE -- na pegada, ou no topo por onde se passa por
	/// tras? Quem pergunta e o pomar (`Pomar.ServeDeCanteiro`): a macieira e uma obra, que desenha por cima de
	/// tudo, e plantada ali ela nasceria com o tronco atras da pedra e a copa na frente dela.
	/// </summary>
	public static bool Desenha(int w, int h, byte[] cobertura, int x, int y)
	{
		// a ancora de quem desenha em (x, y) esta na fileira dela ou em uma das tres de BAIXO
		for (int dy = 0; dy < FileirasDoDesenho; dy++)
			for (int dx = -ColunasDeCadaLado; dx <= ColunasDeCadaLado; dx++)
			{
				int ax = x + dx, ay = y + dy;
				if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
				if (cobertura[ay * w + ax] == (byte)CoberturaDeTerreno.RochaGrande) return true;
			}
		return false;
	}

	/// <summary>
	/// TIRA DO MAPA AS ROCHAS CUJA PEGADA TOCA ESTA CELULA. E a clareira do pouso: as oito celulas em volta
	/// de quem chega tem de estar livres, e uma pedra ancorada a tres colunas dali as tomaria.
	/// </summary>
	public static void TirarAsQueTocam(int w, int h, byte[] cobertura, int x, int y)
	{
		foreach ((int dx, int dy) in Pegada)
		{
			int ax = x - dx, ay = y - dy;
			if (ax < 0 || ay < 0 || ax >= w || ay >= h) continue;
			int i = ay * w + ax;
			if (cobertura[i] == (byte)CoberturaDeTerreno.RochaGrande) cobertura[i] = (byte)CoberturaDeTerreno.Nada;
		}
	}

	/// <summary>
	/// CARIMBA A PEGADA de todas as rochas grandes nos dois planos: o que barra (<paramref name="bits"/>) e o
	/// que nao se quebra (<paramref name="duro"/>). Celula fora do mapa fica de fora; agua, arvore e
	/// montanha debaixo da pedra viram pedra. Devolve quantas rochas ha no mundo.
	/// </summary>
	public static int Carimbar(int w, int h, byte[] cobertura, byte[] bits, byte[] duro)
	{
		int rochas = 0;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (cobertura[y * w + x] != (byte)CoberturaDeTerreno.RochaGrande) continue;
				rochas++;
				if (SemPegadaDeTeste) continue;

				foreach ((int dx, int dy) in Pegada)
				{
					int px = x + dx, py = y + dy;
					if (px < 0 || py < 0 || px >= w || py >= h) continue;
					int i = py * w + px;
					bits[i >> 3] |= (byte)(1 << (i & 7));
					duro[i >> 3] |= (byte)(1 << (i & 7));
				}
			}
		return rochas;
	}
}
