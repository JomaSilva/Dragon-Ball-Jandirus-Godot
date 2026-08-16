using Jandirus.Core.World;

namespace Jandirus.Core.Magic;

/// <summary>
/// **AS ESFERAS DO DRAGAO** -- os numeros, os prazos e o espalhamento. Porte de
/// `Code/Modules/Magic/Dragonballs.dm`.
///
/// ============================ O QUE ESTE ARQUIVO E, E O QUE ELE NAO E ============================
/// So a REGRA: quantas sao, quanto tempo ficam inertes, onde cada uma cai, e o alcance em que as
/// sete "estao juntas". Nada aqui sabe o que e um `ServerPlayer`, uma zona viva ou um pacote -- e o
/// mesmo recorte da <see cref="Social.Conquista"/> e existe pelo mesmo motivo: a bancada mede a regra
/// sem subir servidor, e o servidor nao tem numero solto no meio do laco.
/// ==============================================================================================
///
/// ============================ O ESPALHAMENTO E FUNCAO PURA DA SEMENTE ============================
/// No DM o `Scatter()` (:245-278) sorteia com `rand()` e `pickTurf()`: o mesmo set, na mesma partida,
/// cai em lugares diferentes a cada reinicio -- e nada no BYOND se importava, porque a posicao ia pro
/// savefile.
///
/// Aqui a regra da casa e outra (§0.2: *"o universo, o terreno e o ceu sao funcoes puras de (seed,
/// coordenada)"*), e ela vale pra isto tambem. O que muda de verdade entre um espalhamento e o
/// seguinte e **o CICLO** -- quantas vezes aquele set ja foi consumido --, e o ciclo e um inteiro
/// pequeno que o servidor guarda. Com ele, <see cref="CelulaDoEspalhamento"/> devolve a mesma celula
/// pra sempre, em qualquer maquina, sem uma linha de posicao no disco.
///
/// A CONSEQUENCIA PRATICA que essa escolha compra: um servidor que perde o arquivo das esferas nao
/// perde ONDE elas estavam -- ele so precisa saber quantos ciclos ja se passaram.
/// ============================================================================================
/// </summary>
public static class Esferas
{
	// =====================================================================
	// QUANTAS, E QUAO PERTO
	// =====================================================================
	/// <summary>`NeededBallCount = 7` (Dragonballs.dm:107). Sempre sete, nunca mais nem menos.</summary>
	public const int Total = 7;

	/// <summary>
	/// O ALCANCE DA INVOCACAO, em pixels. E o `set src in view(1)` + `view(1,usr)` do `D_Wish`
	/// (:186-190): as sete tem que estar no MESMO tile ou num dos oito vizinhos.
	///
	/// Um tile e meio, e nao um: `view(1)` do BYOND e o quadrado 3x3 de TILES em volta, e um corpo
	/// parado no meio de um tile alcanca ate a borda longe do vizinho. Medir 1 tile exato faria as
	/// sete "quase juntas" nunca fecharem, que e a pior forma de esta regra falhar -- silenciosa.
	/// </summary>
	public const float AlcanceDaInvocacao = 1.5f * ZoneCollision.TileSize;

	/// <summary>
	/// O ALCANCE DE PEGAR uma esfera do chao -- o `set src in oview(1)` do `Get()` (:344).
	///
	/// E o MESMO numero da invocacao de proposito: no DM os dois sao `view(1)`, e duas distancias
	/// diferentes pra "esta ao meu lado" seriam duas respostas pra mesma pergunta.
	/// </summary>
	public const float AlcanceDePegar = AlcanceDaInvocacao;

	// =====================================================================
	// O RELOGIO -- o `Year` do DM em segundos deste port
	// =====================================================================
	/// <summary>
	/// QUANTOS DIAS IN-GAME VALEM UM `Year` DO DM.
	///
	/// A conta e literal do original: `Year += 0.1` a cada mes (`WorldClock.dm:27`) e o mes tem 28
	/// dias (`if(Days>=29)`, :19). Logo um `Year` inteiro sao 10 meses = 280 dias.
	/// </summary>
	public const double DiasPorAnoDoDm = 280;

	/// <summary>
	/// UM `Year` DO DM EM SEGUNDOS REAIS. **Derivado, nao cravado.**
	///
	/// O DM tem `DAY_REAL_MINUTES 20` e por isso o ano dele dava ~93 h reais. Este port tem um dia de
	/// <see cref="Ceu.SegundosPorDia"/> (24 min), e e o dia DESTE jogo que manda -- copiar as 93 h
	/// deixaria o prazo das esferas discordando do amanhecer que o jogador ve. Ver o mesmo argumento
	/// em <see cref="Espaco.DistanciaTerraNamek"/>: derivar e o que impede os dois de divergirem.
	///
	/// Com o dia de 24 min: 280 x 1.440 s = 403.200 s = **112 h reais** por `Year`.
	/// </summary>
	public static double SegundosPorAno => DiasPorAnoDoDm * Ceu.SegundosPorDia;

	/// <summary>Converte um prazo escrito em `Year` do DM pra segundos deste mundo.</summary>
	public static double SegundosDe(double anosDoDm) => anosDoDm * SegundosPorAno;

	/// <summary>
	/// `OffTime` padrao do set de jogador (`obj/DB/var/OffTime = 1`, :40): as esferas somem por um
	/// `Year` inteiro depois de gastos TODOS os desejos. ~4,7 dias reais aqui.
	/// </summary>
	public const double OffTimeDeJogador = 1.0;

	/// <summary>`ETERNAL_DB_OFFTIME 0.1` (:371) -- o set de Namek volta em um mes, ~11,2 h reais.</summary>
	public const double OffTimeEterno = 0.1;

	/// <summary>
	/// `if(Year>=ActiveYear) ActiveYear = Year + 0.4` (:108) -- esfera recem-criada nasce INERTE por
	/// quatro meses. E o que impede erguer a estatua e pedir no minuto seguinte.
	/// </summary>
	public const double EsperaDeNascimento = 0.4;

	/// <summary>
	/// O TETO DE ESPERA DO SET ETERNO -- `eternal_maintain` corta qualquer `ActiveYear` acima de
	/// `Year + 0.1` (:414-418). O Porunga de Namek nunca espera mais que um mes, nunca morre de vez.
	/// </summary>
	public const double TetoDeEsperaEterna = OffTimeEterno;

	/// <summary>
	/// QUANTO DA ESPERA SE PAGA quando nem todos os desejos foram gastos --
	/// `ActiveYear = Year + OffTime * (WishCount / Wishs)` (:235). Pedir um de tres custa um terco.
	/// </summary>
	public static double EsperaDepoisDoDesejo(double offTime, int pedidos, int total) =>
		total <= 0 ? offTime : offTime * Math.Clamp(pedidos / (double)total, 0, 1);

	// =====================================================================
	// O SET ETERNO -- os `#define` de :369-372
	// =====================================================================
	/// <summary>`ETERNAL_DB_PLANET "Namek"`. A lore nao admite outro lugar -- ver :421-423.</summary>
	public const string PlanetaEterno = "Namek";

	/// <summary>`ETERNAL_DB_WISHES 3`. E a UNICA diferenca mecanica entre Porunga e Shenron.</summary>
	public const int DesejosDoEterno = 3;

	/// <summary>`ETERNAL_DB_WISHPOWER 2000000`.</summary>
	public const double PoderDoEterno = 2_000_000;

	/// <summary>O id de set reservado ao Porunga de Namek. Ver <see cref="SetDeJogador"/>.</summary>
	public const int IdDoSetEterno = 0;

	/// <summary>
	/// UM SET DE JOGADOR TEM ID DE 1 A 999 -- o `BallID = rand(1,999)` do `New()` (:142).
	///
	/// O zero fica de fora e e do set eterno: no DM o eterno sorteia como qualquer outro e o codigo
	/// tem que reencontra-lo pelo tipo da estatua. Reservar um id faz "este e o set de Namek" ser uma
	/// comparacao de inteiro em vez de uma pergunta sobre a classe do objeto.
	/// </summary>
	public static bool SetDeJogador(int id) => id >= 1 && id <= 999;

	// =====================================================================
	// O ESPALHAMENTO
	// =====================================================================
	/// <summary>Sal desta camada. Ver o porque em <see cref="Sistemas"/> -- cada sorteio no seu espaco.</summary>
	private const ulong SalDoEspalhamento = 0xC2B2AE3D27D4EB4FUL;

	/// <summary>
	/// **ONDE ESTA ESFERA CAI**, em celulas do mapa da zona. Funcao pura de
	/// (seed da zona, set, numero, ciclo).
	///
	/// ============================ ELA NAO PERGUNTA SE A CELULA E LIVRE ============================
	/// De proposito: o Core nao tem mapa. Quem chama (o servidor) passa o resultado pelo
	/// `PontoLivrePerto` da colisao, que **tambem** e funcao pura (o terreno e funcao pura da seed,
	/// §0.2) -- entao a composicao das duas continua sendo deterministica, e o servidor nao precisa
	/// carregar a zona pra saber que ela vai cair no mesmo lugar amanha.
	///
	/// O DM faz o oposto (`pickTurf(area, 2)` sorteia DENTRO da lista de turfs andaveis) e podia:
	/// la o mundo inteiro estava sempre na memoria. Aqui um planeta gerado so tem colisao quando
	/// alguem esta nele, e a esfera precisa ter posicao mesmo com o planeta vazio.
	/// ==========================================================================================
	///
	/// A MARGEM tira as bordas do sorteio: o `pickTurf` do DM ja excluia turf denso, e a moldura de um
	/// mapa e quase toda parede. Sortear la faria o `PontoLivrePerto` empurrar quase toda esfera pro
	/// mesmo anel, e as sete apareceriam encostadas na borda do mundo.
	/// </summary>
	public static (int Cx, int Cy) CelulaDoEspalhamento(
		ulong seedDaZona, int idDoSet, int numero, int ciclo, int largura, int altura)
	{
		const int margem = 8;
		int w = Math.Max(largura - 2 * margem, 1);
		int h = Math.Max(altura - 2 * margem, 1);

		ulong a = ((ulong)(uint)idDoSet << 32) | (uint)numero;
		ulong q = Espaco.Misturar(seedDaZona ^ SalDoEspalhamento, a, (ulong)(uint)ciclo);

		return (margem + (int)(q % (ulong)w), margem + (int)((q >> 32) % (ulong)h));
	}

	// =====================================================================
	// O LEQUE -- `Tick()` :308-322
	// =====================================================================
	/// <summary>
	/// O DESLOCAMENTO EM PIXELS DE CADA ESFERA quando varias caem no mesmo ponto.
	///
	/// Literal do DM: 2 sobe 9, 3 desce 9, 4 vai 9 pra direita, 5 pra esquerda, 6 pro canto
	/// superior direito, 7 pro inferior esquerdo, e a 1 fica centrada. Sem isto, sete esferas no
	/// mesmo tile sao UM sprite -- e o jogador que reuniu as sete nao ve as sete.
	///
	/// O Y INVERTE DE SINAL: no BYOND ele cresce PRA CIMA e no Godot pra baixo. Copiar `pixel_y = 9`
	/// cru poria a segunda esfera embaixo em vez de em cima -- o mesmo cuidado que a `ObraDesenhada`
	/// ja documenta pro `pixel_y` das construcoes.
	/// </summary>
	public static (float X, float Y) LequeDe(int numero) => numero switch
	{
		2 => (0, -9),
		3 => (0, 9),
		4 => (9, 0),
		5 => (-9, 0),
		6 => (9, -9),
		7 => (-9, 9),
		_ => (0, 0),
	};

	// =====================================================================
	// TEXTO
	// =====================================================================
	/// <summary>O estado do sprite -- "1".."7" ativa, "inactive" recarregando. E o `icon_state` (:283).</summary>
	public static string EstadoDoSprite(int numero, bool inerte) =>
		inerte ? "inactive" : numero.ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>
	/// O nome que o jogador le. E o `"[Creator] Dragonballs ([BallNum] - [BallID])"` (:114) traduzido
	/// pro que o jogador de verdade chama: a esfera de N estrelas.
	///
	/// **O NOME DO CRIADOR E O ID DO SET FICARAM DE FORA**, e nao por descuido: no DM eles estao ali
	/// porque `name` e a UNICA forma de distinguir dois sets no mesmo mapa (nao ha painel, nao ha
	/// aba). Aqui quem responde "de quem e este set" e o `db_ver`, com o nome, o poder e o prazo --
	/// e pendurar o id interno no nome de cada esfera seria vazar numeracao de servidor na tela.
	/// </summary>
	public static string NomeDaEsfera(int numero) =>
		$"Esfera de {numero} estrela{(numero > 1 ? "s" : "")}";

	// =====================================================================
	// A DIVIDA
	// =====================================================================
	/// <summary>
	/// O QUE DO `Dragonballs.dm` **NAO** ESTA AQUI. Lista e nao comentario, pelo mesmo motivo do
	/// <see cref="Social.Conquista.OqueFalta"/>: sistema pela metade que nao diz onde acaba e sistema
	/// que ninguem termina.
	/// </summary>
	public static readonly string[] OqueFalta =
	[
		// A TABELA DE DESEJOS ENTROU (Fase 2). O que sobrou dela esta em `Desejos.Todos`, marcado
		// desejo a desejo com o motivo -- e o jogador LE isso no menu, em vez de apertar um botao que
		// mente. Repetir os quatro aqui seria uma segunda lista pra divergir da primeira.
		$"{Desejos.NaoPortados} dos {Desejos.Portados + Desejos.NaoPortados} desejos do Shenron não "
		+ "foram portados -- cada um diz o motivo na própria linha do menu (ver `Desejos.Todos`)",

		"o ramo de APROVAÇÃO DE ADMIN do desejo Super Saiyajin (`WishTable.dm:431-455`) -- o DM abre "
		+ "um input() em cada admin online, e sem modal o resultado é o mesmo do original quando "
		+ "ninguém aprova: falha, e a esfera engorda 5%",

		"o ícone CUSTOM do dragão por upload (`Redo()`, :74-77) -- não há canal de arquivo do "
		+ "cliente pro servidor neste port, e não vai haver por causa disto",

		"\"Gender Change\" (`WishTable.dm:383-408`) -- e a ausência é FIEL: o código existe completo no "
		+ "original e a string nunca entra no menu. É código morto lá",
	];
}
