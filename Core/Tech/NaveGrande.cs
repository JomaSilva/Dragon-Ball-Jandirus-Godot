using Jandirus.Core.World;

namespace Jandirus.Core.Tech;

/// <summary>
/// UMA PECA FIXA DO INTERIOR: o computador da ponte e a plataforma de saida.
///
/// Elas NAO sao `Obra` e nao poderiam ser: a `Obra` guarda a zona como NOME puro
/// (`GameServer.Tech.cs:15`), e o nome do interior de toda nave e o mesmo ("Nave") -- as duas
/// pecas de uma nave apareceriam dentro de todas as outras. E o mesmo defeito que fez a `Nave`
/// nascer com tipo+nome+seed em vez de um campo so (ver o cabecalho de `Nave`).
///
/// Aqui elas nem precisam de registro: a posicao delas e FUNCAO DA PLANTA, igual pra toda nave, e
/// quem as manda pro cliente as deriva na hora (`MobiliaDoInterior`). Nao ha lista pra sanear, nao
/// ha id pra colidir, e nao ha como uma nave "perder" a ponte dela.
/// </summary>
/// <param name="Tipo">O id que o menu da tecla E consulta (<see cref="Interacoes.De"/>).</param>
/// <param name="Cel">A celula em que ela esta, no sistema de coordenadas DESTE port.</param>
/// <param name="Arte">res:// do SpriteFrames -- viaja no pacote, como o de qualquer construcao.</param>
/// <param name="Estado">O `icon_state` do original.</param>
/// <param name="Densa">Bloqueia passagem? `density=1` no console, `density=0` na plataforma.</param>
public readonly record struct PecaDoInterior(string Tipo, (int X, int Y) Cel, string Arte,
											 string Estado, bool Densa);

/// <summary>
/// **A CAPITAL SHIP** -- os numeros e a PLANTA do interior. Porte de `obj/PlayerShip`
/// (`Code/Modules/Tech/ShipVessel.dm`).
///
/// ============================ O QUE ELA E, EM UMA FRASE ============================
/// A Spacepod e um objeto que voce veste; a Capital Ship e um LUGAR que voa. Dois milhoes de zeni
/// e tecnologia 55 compram uma sala de 100x100 com uma ponte no canto, que se lanca ao espaco e
/// que outras pessoas podem habitar enquanto ela viaja.
/// ==================================================================================
///
/// ============================ POR QUE A PLANTA MORA NO CORE ============================
/// Porque as DUAS pontas precisam dela e pelo mesmo motivo de sempre neste projeto (regra 0.2): o
/// servidor precisa da COLISAO (senao o casco nao segura ninguem e o `ValidateStep` deixa sair
/// pelo vazio) e o cliente precisa do DESENHO (senao o interior e um chao provisorio invisivel).
/// Se as duas fossem escritas separadas, o dia em que alguem mexesse numa parede so uma das duas
/// mudaria -- e o sintoma seria "atravesso a parede da minha nave", que e exatamente o defeito que
/// o `ZoneCollision` foi escrito pra impedir.
///
/// O DM constroi o interior com um laco de `new /turf/...` sobre um z novo (`build_interior`,
/// :146-176). Aqui nao ha "z novo" -- ha <see cref="ZoneKey.Interior"/>, que ja existe e ja e
/// usado (a dimensao mental do clone). O que muda e so quem produz o chao: la o mapa E o mundo,
/// aqui o mundo e uma funcao.
/// ====================================================================================
///
/// ============================ UMA PLANTA SO, COMPARTILHADA -- E POR QUE ISSO E SEGURO ============================
/// Todas as naves tem o MESMO interior (o DM tambem: `build_interior` nao le nada da nave), entao
/// <see cref="Planta"/> devolve sempre a mesma instancia. O que separa a nave A da nave B e a
/// <see cref="ZoneKey"/> -- `Interior("Nave", id)` --, e o hash dela ja e o corte de interesse.
///
/// Compartilhar um <see cref="ZoneCollision"/> so e seguro porque ele e MUTAVEL em tres lugares e
/// nenhum dos tres alcanca este mapa:
///   * `Bloquear`/`LimparObras` (a camada das construcoes) -- construir DENTRO da nave e recusado
///     pelo `Posicionar`, justamente porque a `Obra` nao sabe distinguir dois interiores;
///   * `Abrir`/`Fechar` (as portas) -- a entrada da ponte e um VAO, e nao uma porta. O DM chegou a
///     por uma `turf/Door/Door2` la (:169), e o comentario dele diz que antes era so um vao. Aqui
///     ela volta a ser vao de proposito: uma porta e ESTADO por zona, e estado por zona num mapa
///     compartilhado seria a porta da nave A abrindo a da nave B;
///   * a densidade do console, que NAO entra pela camada de obras -- ela e assada no bitset da
///     propria planta, la embaixo.
///
/// O dia em que uma nave precisar de interior PROPRIO (mobilia posta pelo dono, tamanho por
/// upgrade), esta funcao passa a receber o id e a devolver instancia por nave -- e as tres linhas
/// acima sao a lista do que conferir.
/// ==========================================================================================================
/// </summary>
public static class NaveGrande
{
	/// <summary>O id do catalogo (`construcoes.json`). `create_type = /obj/PlayerShip` (:57).</summary>
	public const string Tipo = "Capital_Ship";

	/// <summary>Este id do catalogo e a nave grande?</summary>
	public static bool EhNaveGrande(string tipo) => tipo == Tipo;

	/// <summary>
	/// O NOME DA ZONA DO INTERIOR. O id da nave entra como SEED da <see cref="ZoneKey"/>, e nao no
	/// nome: e o hash (que inclui a seed) que corta o interesse, e o nome e o que diz ao cliente
	/// QUE cenario montar -- exatamente como `Interior("Interdimension", id)` ja faz.
	/// </summary>
	public const string NomeDaZonaInterna = "Nave";

	/// <summary>A zona do interior desta nave.</summary>
	public static ZoneKey ZonaDoInterior(int idDaNave) =>
		ZoneKey.Interior(NomeDaZonaInterna, (ulong)idDaNave);

	/// <summary>Esta zona e o interior de alguma nave? Devolve o id dela em <paramref name="id"/>.</summary>
	public static bool EhInterior(ZoneKey z, out int id)
	{
		id = z.Kind == ZoneKey.KindInterior && z.Name == NomeDaZonaInterna ? (int)z.Seed : 0;
		return id != 0;
	}

	// =====================================================================
	// OS NUMEROS
	// =====================================================================
	/// <summary>
	/// A ARMADURA -- `maxarmor = max(usr.intBPcap * 5, 1000)` (`ShipVessel.dm:63`).
	///
	/// `intBPcap` nao existe neste port; quem ocupa o lugar dele desde o `Posicionar` e o
	/// `expressedBP` de quem ergueu (ver `GameServer.Tech.cs:385`, a mesma traducao). O `* 5` e o
	/// que faz a nave ser "tanky" nas palavras do proprio autor do DM: ela aguenta cinco vezes o
	/// que a bancada do mesmo dono aguentaria.
	///
	/// O PISO DE 1000 IMPORTA MAIS DO QUE PARECE por causa da superarmadura
	/// (<see cref="Combat.Armadura"/>): dano abaixo de 75% do maximo NAO ENTRA. Com o piso, a nave
	/// de um novato ainda exige 750 de golpe pra arranhar -- ou seja, ela nao cai pro primeiro
	/// transeunte so porque o dono era fraco no dia em que a construiu.
	/// </summary>
	public static double ArmaduraMaxima(double expressedBpDeQuemErgueu) =>
		Math.Max(expressedBpDeQuemErgueu * 5, 1000);

	/// <summary>
	/// A ESPERA DO LANCAMENTO -- `sleep(100)` e o proprio texto do DM: *"ETA 10 seconds"* (:186-187).
	///
	/// FIXA, e nao `400/Speed` como a da Spacepod: a Capital Ship do original nao tem `var/Speed` --
	/// ela nao tem verb de upgrade nenhum. Ver <see cref="Naves.SegundosDeLancamento"/> pro caso do
	/// pod, e o comentario de `MelhorarNave` pro que foi feito com a velocidade dela aqui.
	/// </summary>
	public const double SegundosDeLancamento = 10.0;

	// =====================================================================
	// A PLANTA
	// =====================================================================
	/// <summary>`#define SHIP_INTERIOR_SIZE 100` (:16). Cem celulas de lado -- 3.200 px.</summary>
	public const int Lado = 100;

	/// <summary>
	/// A CELULA DA PLATAFORMA DE SAIDA -- `SHIP_PAD_X/Y 50` (:17-18), em base zero.
	/// </summary>
	public static readonly (int X, int Y) CelDaPlataforma = (49, 49);

	/// <summary>
	/// ONDE O CORPO APARECE AO EMBARCAR -- `locate(SHIP_PAD_X, SHIP_PAD_Y - 1, iz)` (:120), com o
	/// comentario do DM: *"appear just south of the central pad"*.
	///
	/// UM TILE AO SUL E REQUISITO, nao enfeite: quem chegasse EM CIMA da plataforma dispararia o
	/// `Crossed` dela no mesmo instante -- no DM isso e a caixa de "sair da nave?" abrindo na cara
	/// de quem acabou de entrar. Aqui a saida e por menu e nao por pisar, mas o tile de folga
	/// continua sendo o certo: ele deixa a plataforma VISIVEL na frente de quem chega.
	///
	/// No BYOND o Y cresce PRA CIMA (`SHIP_PAD_Y - 1` e ao sul); aqui ele cresce pra baixo, entao
	/// o sul e `+1`. Copiar o sinal cru poria o corpo ao NORTE da plataforma.
	/// </summary>
	public static readonly (int X, int Y) CelDaChegada = (49, 50);

	/// <summary>
	/// A SALA DE CONTROLE -- `ry = sz - 14` (:164) e as duas paredes internas (:165-171).
	///
	/// O DM a poe no canto de cima ("top-left room"), onde "cima" e Y ALTO. Aqui Y cresce pra
	/// baixo, entao o canto de cima e Y BAIXO e a sala ocupa as 14 primeiras linhas. E a mesma
	/// inversao do <see cref="CelDaChegada"/>, e ela vale pra planta inteira.
	/// </summary>
	public const int AlturaDaSala = 14;

	/// <summary>A coluna da parede DIREITA da sala -- `locate(14, yy, iz)` (:166), base zero.</summary>
	public const int ColunaDaParedeDaSala = 13;

	/// <summary>O vao na parede de baixo da sala -- `if(xx == 8)` (:168), base zero.</summary>
	public const int ColunaDoVao = 7;

	/// <summary>
	/// O COMPUTADOR DA PONTE -- `locate(5, sz - 5, iz)` (:172), base zero e com o Y invertido:
	/// cinco celulas da borda de cima, dentro da sala.
	/// </summary>
	public static readonly (int X, int Y) CelDoConsole = (4, 4);

	// A DISTANCIA PRA ALCANCAR O CONSOLE OU A PLATAFORMA **NAO MORA MAIS AQUI**.
	//
	// Havia um `AlcanceDaPeca = 48f` proprio, com o comentario dizendo que ele era "o mesmo
	// AlcanceDeUso que toda construcao deste port ja usa" -- e nao era: aquele e 64. A diferenca
	// abria uma banda morta de 16 px em que o cliente ABRIA o menu do console e o servidor RECUSAVA
	// o verbo ("você precisa estar no console da ponte"). Duas celulas ortogonais dao exatamente
	// 64 px, entao dava pra cair nela andando em linha reta.
	//
	// Quem responde agora e <see cref="Interacoes.Alcance"/>, que e a casa unica desse numero -- e o
	// comentario de la conta a historia inteira.

	/// <summary>O centro em pixels de uma celula da planta.</summary>
	public static Vec2 PixelDe((int X, int Y) cel) => new(
		(cel.X + 0.5f) * ZoneCollision.TileSize,
		(cel.Y + 0.5f) * ZoneCollision.TileSize);

	/// <summary>
	/// AS DUAS PECAS FIXAS. Derivadas da planta, nunca guardadas -- ver <see cref="PecaDoInterior"/>.
	///
	/// A ARTE E A DO ORIGINAL: `icon = 'Computer.dmi'` com `icon_state = "Computer"` (:288-289) e
	/// `icon = 'Bigteleporter2013.dmi'` (:270). Os dois .dmi ja estao convertidos neste port.
	/// </summary>
	public static readonly PecaDoInterior[] Mobilia =
	[
		new("Ship_Control", CelDoConsole,
			"res://Assets/Sprites/Misc/Objects/Technology/Computer.tres", "Computer", true),
		new("Ship_Pad", CelDaPlataforma,
			"res://Assets/Sprites/Misc/Objects/Technology/Bigteleporter2013.tres", "", false),
	];

	// =====================================================================
	// O CHAO
	// =====================================================================
	/// <summary>
	/// A PALETA DO CASCO -- `turf/ShipFloor` e `turf/ShipWall` (:21-35), os dois de `Space.dmi`
	/// com os estados `"bottom"` (o convés) e `"top"` (o casco).
	///
	/// Ela NAO e um bioma novo: e uma <see cref="PaletaDeBioma"/> montada a mao, que e o que
	/// permite reusar o `TerrenoGerado` inteiro (e com ele o pintor por pedaco do cliente e a
	/// colisao do servidor) sem inventar um sexto lugar que sabe desenhar chao.
	///
	/// AS ENTRADAS QUE NAO EXISTEM AQUI (agua, praia, colina, arvore, minerio...) apontam pro
	/// convés de proposito, e nao pro vazio: o `PlanetaProcedural.MontarCamadas` resolve os DEZ
	/// pinceis da paleta no nascimento, e um `TileVisual` vazio faria dez avisos amarelos no
	/// console toda vez que alguem embarca. Aviso que sempre sai e aviso que ninguem le -- e a
	/// regra 5 da casa (falhar alto) so vale enquanto o alto for raro.
	/// </summary>
	private static readonly TileVisual Conves = new("Space", "bottom");
	private static readonly TileVisual Casco = new("Space", "top");

	private static readonly PaletaDeBioma PaletaDoCasco = new()
	{
		// SO UM ROTULO. A `TerrenoGerado` exige um bioma e nenhum deles e "metal"; `Morto` e o mais
		// perto do que uma sala de aco e, e ele nao entra em conta nenhuma -- so na assinatura.
		Bioma = BiomaDeTerreno.Morto,
		LimiarAgua = 0, LimiarPraia = 0, LimiarColina = 1, LimiarMontanha = 1,
		VegetacaoPct = 0, PlantaPct = 0, MinerioPct = 0,
		Agua = Conves, Praia = Conves, Planicie = Conves, Colina = Conves, Montanha = Casco,
		Arvore = Conves, ArvoreAcento = Conves, Planta = Conves, Minerio = Conves, Gema = Conves,
	};

	private static TerrenoGerado? _planta;

	/// <summary>
	/// A PLANTA PRONTA -- montada na primeira vez que alguem embarca e reusada dali em diante.
	///
	/// ============================ O CUSTO E POR QUE ELE CABE NO TIQUE ============================
	/// Sao 10.000 celulas. O gerador de planeta paga ~0,2 us por celula nos mundos de 200 mil
	/// (`PlanetaProcedural`, tabela medida), e este laco e mais simples que aquele -- nao ha ruido,
	/// nao ha sorteio, nao ha busca de clareira. A bancada `nave` mede e imprime o numero em vez de
	/// estima-lo (regra 0.5), e ele so e pago UMA VEZ no processo inteiro.
	///
	/// Se um dia ele nao couber, o caminho ja existe e esta escrito: `Encomendar`/`TickDasGeracoes`
	/// (`GameServer.Procedural.cs`) -- a mesma thread de fundo que gera planeta.
	/// ========================================================================================
	/// </summary>
	public static TerrenoGerado Planta() => _planta ??= Montar();

	private static TerrenoGerado Montar()
	{
		const int n = Lado;
		var chao = new byte[n * n];
		var cobertura = new byte[n * n];
		var bits = new byte[(n * n + 7) / 8];

		void Parede(int x, int y)
		{
			int i = y * n + x;
			chao[i] = (byte)ClasseDeTerreno.Montanha;
			bits[i >> 3] |= (byte)(1 << (i & 7));
		}

		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				int i = y * n + x;
				cobertura[i] = (byte)CoberturaDeTerreno.Nada;

				// O CASCO E A BORDA -- `if(xx == 1 || yy == 1 || xx == sz || yy == sz)` (:156).
				//
				// No DM ele e indestrutivel de um jeito que uma parede densa comum nao e: o
				// `Enter()` do `turf/ShipWall` (:33-35) devolve 0 pra QUALQUER mob, *"a plain
				// density wall lets flyers pass"*. Aqui isso sai de graca e por outro caminho -- o
				// `ZoneCollision.NaBorda` ja recusa quebrar as duas primeiras celulas de qualquer
				// mapa, e quem voa dentro do interior nao passa por parede porque o
				// `AtravessandoCenario` (GameServer.Voo.cs) so solta a colisao de quem esta VOANDO,
				// e ninguem voa numa sala de teto baixo.
				if (x == 0 || y == 0 || x == n - 1 || y == n - 1) { Parede(x, y); continue; }

				chao[i] = (byte)ClasseDeTerreno.Planicie;
			}

		// A SALA DE CONTROLE: parede direita (:165-166) e parede de baixo com o vao (:167-171).
		for (int y = 0; y < AlturaDaSala; y++) Parede(ColunaDaParedeDaSala, y);
		for (int x = 0; x <= ColunaDaParedeDaSala; x++)
			if (x != ColunaDoVao) Parede(x, AlturaDaSala);

		// O CONSOLE E PAREDE, e ele e assado AQUI e nao pela camada de obras (`ZoneCollision.Bloquear`).
		// `density = 1` no `obj/ShipControl` (:290). Ver o cabecalho: a camada de obras e limpa pelo
		// `AplicarColisaoDasObras` a cada pacote de construcoes, e esta planta e compartilhada por
		// todas as naves -- assar no bitset e o que faz a densidade nao depender de quem mandou o
		// ultimo pacote. A plataforma NAO e parede (`density = 0`, :272): pisa-se nela.
		Parede(CelDoConsole.X, CelDoConsole.Y);

		byte[] jcol = GeradorDeTerreno.MontarJcol(n, n, bits);

		return new TerrenoGerado
		{
			Largura = n,
			Altura = n,
			Bioma = BiomaDeTerreno.Morto,
			Seed = 0,               // a planta e uma so; quem separa as naves e a `ZoneKey`
			Paleta = PaletaDoCasco,
			Chao = chao,
			Cobertura = cobertura,
			BytesDeColisao = jcol,
			// UMA NAVE NAO TEM AGUA. O interior e conves e casco (ver `PaletaDoCasco`, onde os
			// limiares de agua e praia sao 0), entao o plano sai vazio -- e vazio quer dizer
			// `ZoneCollision.TemAgua == false`, sem custo nenhum por celula.
			BytesDeAgua = new byte[(n * n + 7) / 8],
			Colisao = ZoneCollision.Load(jcol)!,
			SpawnCelX = CelDaChegada.X,
			SpawnCelY = CelDaChegada.Y,
			ClareiraEscavada = false,
		};
	}
}
