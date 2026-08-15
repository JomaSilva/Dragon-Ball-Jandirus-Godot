namespace Jandirus.Core.World;

/// <summary>
/// O BIOMA de um planeta.
///
/// Os seis nomes NAO sao invencao deste arquivo: sao o vocabulario que o projeto ja usa em
/// `Tools/AssetPipeline/DmPlanetScanner.cs` (tabela `TipoPorNome`), que classifica os planetas
/// PRE-FEITOS -- Earth/Namek = Jardim, Vegeta = Rochoso, Icer Planet = Gelado, Arlia = Deserto,
/// Hell = Vulcanico, Makyo Star = Morto. Um vocabulario so pro pre-feito e pro procedural: a cena
/// do planeta manda o mesmo texto nos dois casos e nao precisa saber qual e qual.
///
/// O DM tem SEIS biomas proprios (`Modules/Tech/ProceduralSpace.dm:239-245`): Verdejante, Gelido,
/// Deserto, Vulcanico, Alienigena, Sombrio. Cinco casam direto; ver <see cref="GeradorDeTerreno.BiomaDoTipo"/>
/// pro que foi feito com o sexto.
/// </summary>
public enum BiomaDeTerreno : byte
{
	Jardim = 0,
	Deserto = 1,
	Gelado = 2,
	Rochoso = 3,
	Vulcanico = 4,
	Morto = 5,
}

/// <summary>
/// A CLASSE de um tile de chao. Sao as mesmas cinco do original
/// (`ProceduralSpace.dm:1265` -- "1 agua, 2 praia, 3 planicie, 4 colina, 5 montanha"),
/// renumeradas de 0 porque aqui elas sao indice de array e nao de lista do DM.
/// </summary>
public enum ClasseDeTerreno : byte
{
	/// <summary>Lago. No bioma Vulcanico e LAVA (`ProceduralSpace.dm:1307-1311`).</summary>
	Agua = 0,
	/// <summary>Margem: a faixa de terra batida entre o lago e a planicie.</summary>
	Praia = 1,
	/// <summary>"Onde a vida acontece" (`ProceduralSpace.dm:1321`): flora, plantas, pouso.</summary>
	Planicie = 2,
	/// <summary>Terreno alto: mais arvore e os veios de minerio.</summary>
	Colina = 3,
	/// <summary>Parede densa e sobrevoavel -- o `turf/pspace_mountain` (`ProceduralSpace.dm:562-573`).</summary>
	Montanha = 4,
}

/// <summary>
/// O que esta EM CIMA do chao. E uma segunda grade porque um TileMapLayer do Godot guarda um
/// tile por celula -- arvore e grama na mesma celula sao duas camadas, exatamente como o
/// MapConverter ja separa "Chao" de "Objetos" nas cenas dos planetas pre-feitos.
/// </summary>
public enum CoberturaDeTerreno : byte
{
	Nada = 0,
	/// <summary>Especie dominante do planeta (85% das arvores; `ProceduralSpace.dm:1335`).</summary>
	Arvore = 1,
	/// <summary>Especie de acento (os outros 15%): o que impede a floresta de virar carimbo.</summary>
	ArvoreAcento = 2,
	/// <summary>Planta colhivel (`ProceduralSpace.dm:1340-1344`).</summary>
	Planta = 3,
	/// <summary>Veio de minerio comum, so em colina (`ProceduralSpace.dm:1350-1353`).</summary>
	Minerio = 4,
	/// <summary>Gema: 1 em cada 4 veios (`ProceduralSpace.dm:1352`).</summary>
	Gema = 5,
}

/// <summary>
/// Como um tile se DESENHA: o atlas e o estado, do jeito que o pipeline de assets os nomeia.
///
/// `Atlas` e o nome do arquivo SEM extensao -- e a chave que o `MapConverter` usa
/// (`Path.GetFileNameWithoutExtension`), e a mesma coisa que o DM escreve em `icon = 'Grass.dmi'`
/// e que vira `res://Assets/Sprites/Turfs/Grass.png` no `tileset.tres`. `Estado` e o `icon_state`.
///
/// O Core NAO carrega o id numerico da fonte no TileSet de proposito: esse id e atribuido por
/// ORDEM DE DESCOBERTA na conversao (`MapConverter.Garantir`, `proxId++`), entao ele muda quando
/// alguem reconverte os mapas. Gravar o numero aqui seria plantar um defeito com data marcada.
/// Quem traduz nome -> id e a cena do planeta, uma vez, no carregamento.
/// </summary>
/// <param name="Atlas">Nome do .dmi/.png sem extensao ("Grass", "Ground", "Waters", "Walls"...).</param>
/// <param name="Estado">O `icon_state`. Vazio e um estado VALIDO (varios .dmi tem o estado sem nome).</param>
/// <param name="Tinta">Cor "#rrggbb" aplicada por cima, ou nulo. Vem dos tints do DM.</param>
public readonly record struct TileVisual(string Atlas, string Estado, string? Tinta = null);

/// <summary>
/// A "personalidade" de um bioma: quanta agua, quanta rocha, quanta vegetacao, quao aspero.
///
/// DE ONDE VEM CADA NUMERO -- isto importa mais que os numeros:
///   * os quatro LIMIARES de relevo sao NOSSOS e nao copias do DM. O DM tem os dele
///     (`ProceduralSpace.dm:34-37`), iguais pros seis biomas; aplicados a este ruido eles davam
///     0,4% de montanha. Ver a nota de calibracao em `GeradorDeTerreno.Paletas`;
///   * as densidades de arvore/planta/minerio sao do DM onde o DM as declara
///     (`ProceduralSpace.dm:28-30` e a tabela de biomas em `:239-245`). Onde o DM nao tem
///     equivalente -- o bioma Rochoso -- o numero e NOSSO e esta marcado como tal;
///   * <see cref="Aspereza"/> nao existe no DM. E invencao nossa (ver o campo).
/// </summary>
/// <summary>
/// IMUTAVEL DEPOIS DE CONSTRUIDA, e isto e requisito de DETERMINISMO, nao gosto.
///
/// A tabela `Paletas` e `static readonly` -- readonly na REFERENCIA, nao no conteudo -- e
/// `Paleta(b)` e publica e entregava a instancia VIVA. Qualquer codigo podia escrever
/// `Paleta(Jardim).LimiarAgua = 0.9` e mudar o bioma pro processo inteiro: o cliente passaria a
/// gerar um mundo e o servidor outro a partir da MESMA seed, e a falha seria invisivel -- ninguem
/// descobre ate alguem cair num buraco que o outro nao ve.
///
/// `init`-only e o compilador garantindo o que um comentario so pediria.
/// </summary>
public sealed class PaletaDeBioma
{
	public BiomaDeTerreno Bioma { get; init; }

	// ---- relevo: a altitude (0..1) cai numa destas faixas ----
	public double LimiarAgua { get; init; }
	public double LimiarPraia { get; init; }
	public double LimiarColina { get; init; }
	public double LimiarMontanha { get; init; }

	/// <summary>
	/// INVENCAO NOSSA. Nao ha nada equivalente no DM, onde todo planeta usa a mesma mistura de
	/// ruido e os mesmos quatro limiares -- e por isso um mundo vulcanico e um mundo-jardim tinham
	/// o MESMO relevo, so pintado diferente.
	///
	/// E um esticao da altitude em torno de 0,5: `h = 0,5 + (h - 0,5) * aspereza`. 1,00 devolve a
	/// curva do ruido crua; acima de 1 empurra os extremos pras pontas (mais lago E mais
	/// cordilheira, terreno recortado); abaixo de 1 achata tudo em planicie. Um numero so mexe nos
	/// dois extremos ao mesmo tempo, que e como relevo de verdade se comporta.
	///
	/// E MONOTONA, e isso e o que torna a recalibracao dos limiares uma conta e nao um chute: o
	/// limiar que corta em p% e sempre `clamp(0,5 + (quantil(p) - 0,5) * aspereza)`. Mudou a
	/// aspereza, os quatro limiares do bioma tem de ser refeitos por essa formula.
	/// </summary>
	public double Aspereza { get; init; } = 1.0;

	// ---- povoamento (chance em 100 por tile) ----
	public int VegetacaoPct { get; init; }
	public int PlantaPct { get; init; }
	public int MinerioPct { get; init; }

	/// <summary>O lago e LAVA. `ProceduralSpace.dm:1176` (`water_lava = 1` no Vulcanico).</summary>
	public bool LiquidoEhLava { get; init; }

	// ---- visual (ver a nota de conferencia em GeradorDeTerreno) ----
	public TileVisual Agua { get; init; }
	public TileVisual Praia { get; init; }
	public TileVisual Planicie { get; init; }
	public TileVisual Colina { get; init; }
	public TileVisual Montanha { get; init; }
	public TileVisual Arvore { get; init; }
	public TileVisual ArvoreAcento { get; init; }
	public TileVisual Planta { get; init; }
	public TileVisual Minerio { get; init; }
	public TileVisual Gema { get; init; }
}

/// <summary>
/// O PEDIDO de geracao. E o que a cena do planeta preenche: o servidor manda a seed, a cena
/// conhece o nome/gravidade/tipo do planeta, e o Core devolve o terreno.
/// </summary>
public sealed class ParametrosDeTerreno
{
	/// <summary>
	/// A seed do PLANETA -- a mesma que viaja no pacote `Vizinhanca`
	/// (`Server/GameServer.Espaco.cs`, campo `PlanetaNoEspaco.Seed`). Cliente e servidor geram o
	/// mesmo chao sem trocar um byte de mapa; e o motivo de o universo caber num numero.
	/// </summary>
	public ulong Seed;

	/// <summary>Lado do mundo. 500 e o `PSURF_SIZE` do original (`ProceduralSpace.dm:18`).</summary>
	public int Largura = 500;
	public int Altura = 500;

	public BiomaDeTerreno Bioma = BiomaDeTerreno.Jardim;

	/// <summary>
	/// Gravidade do planeta (1 = Terra). Entra na geracao so como um empurrao pequeno na
	/// <see cref="PaletaDeBioma.Aspereza"/> -- ver <see cref="GeradorDeTerreno.FatorDeGravidade"/>.
	/// </summary>
	public double Gravidade = 1.0;

	/// <summary>Nome, so pra rastreabilidade e log. Nao entra na geracao.</summary>
	public string Nome = "";
}

/// <summary>O terreno pronto: as duas grades, a colisao que o servidor le, e onde se pousa.</summary>
public sealed class TerrenoGerado
{
	public required int Largura { get; init; }
	public required int Altura { get; init; }
	public required BiomaDeTerreno Bioma { get; init; }
	public required ulong Seed { get; init; }
	public required PaletaDeBioma Paleta { get; init; }

	/// <summary>Classe de chao por celula, em ordem de LINHA (`i = y * Largura + x`).</summary>
	public required byte[] Chao { get; init; }

	/// <summary>O que esta em cima, mesma indexacao.</summary>
	public required byte[] Cobertura { get; init; }

	/// <summary>
	/// O blob "JCOL" pronto (cabecalho + bitset), no formato exato que o
	/// `Tools/AssetPipeline/MapConverter.EscreverColisao` grava pros planetas pre-feitos. Serve pra
	/// salvar em disco ou mandar pela rede sem reserializar nada.
	/// </summary>
	public required byte[] BytesDeColisao { get; init; }

	/// <summary>
	/// O BITSET DA AGUA deste mundo (1 bit por celula, mesma ordem de linha do `Chao`), ja
	/// pendurado na <see cref="Colisao"/>. Fica exposto pra bancada poder conta-lo sem refazer a
	/// derivacao -- contar de novo aqui seria a segunda copia da regra "agua e `ClasseDeTerreno.Agua`".
	/// </summary>
	public required byte[] BytesDeAgua { get; init; }

	/// <summary>A mesma colisao, ja lida. E o que o servidor consulta em `PathBlocked`/`BlockedAt`.</summary>
	public required ZoneCollision Colisao { get; init; }

	/// <summary>
	/// O MAPA DO QUE **ESCONDE** deste mundo -- so montanha. O irmao gerado do `.vis` dos pre-feitos.
	///
	/// ============================ POR QUE ELE MUDOU DE CASA ============================
	/// Isto morava no `Client/PlanetaProcedural.cs` e era do CLIENTE, porque so o cliente desenhava
	/// sombra. A voz local o trouxe pra ca: quem responde *"ha parede entre estas duas pessoas?"* e o
	/// SERVIDOR (e ele quem decide quem recebe voz de quem), e a resposta dele tem que ser a MESMA que
	/// o olho da -- duas derivacoes do mesmo mapa sao duas oportunidades de divergir, e o sintoma
	/// seria voz abafada sem parede na tela.
	///
	/// **NAO DA PRA REUSAR A <see cref="Colisao"/>**, e essa e a linha inteira: la a arvore tambem e
	/// parede -- e ela PARA o corpo, com razao --, e uma floresta cegando o jogador inteiro seria
	/// outro jogo. Ceg**ar** e bloque**ar** sao duas perguntas, exatamente como o `.col` e o `.vis`
	/// dos mapas desenhados a mao.
	/// =================================================================================
	///
	/// PREGUICOSO E GUARDADO: sao 250 mil a 1 milhao de passadas num mundo grande, e quem pergunta e
	/// o leque da voz (por quadro). Calcular a cada pergunta poria no tique o custo de uma geracao.
	/// </summary>
	public ZoneCollision QueEsconde => _queEsconde ??= MontarOQueEsconde();

	private ZoneCollision? _queEsconde;

	private ZoneCollision MontarOQueEsconde()
	{
		int n = Largura * Altura;
		var bits = new byte[(n + 7) / 8];

		// O PLANO DE IDENTIDADE SAI DE GRACA AQUI: a "identidade do tile" de um planeta gerado e a
		// propria CLASSE DE TERRENO, que ja esta no `Chao`. Sem ele, a sombra num mundo gerado cairia
		// no caminho de degradacao (`SemGrupo`) e voltaria a parar na primeira parede -- que e menos
		// errado, mas nao e o que os planetas pre-feitos fazem.
		var grupo = new byte[n];

		for (int i = 0; i < n; i++)
		{
			if (Chao[i] != (byte)ClasseDeTerreno.Montanha) continue;
			bits[i >> 3] |= (byte)(1 << (i & 7));
			// +1 pra nao colidir com o 0, que e "borda do mundo" nos mapas pre-feitos
			grupo[i] = (byte)(Chao[i] + 1);
		}

		ZoneCollision vista = ZoneCollision.Montar(Largura, Altura, bits, grupo);

		// ============================ O QUE ESCONDE HERDA A BEIRADA (OU A FALTA DELA) ============================
		// Fora do bitset, `BlockedCell` devolve `!SemBorda` -- e num mundo sem beirada (a dimensao
		// mental) esquecer esta linha nao daria erro nenhum: daria **escuridao**. O olho e a voz usam
		// este mapa pelo DDA, e todo raio que saisse da chapa bateria na "borda do mundo" na primeira
		// celula de fora. O jogador andaria dez tiles pro norte e a tela fecharia, com o chao branco
		// desenhado normalmente por baixo -- o defeito mais dificil de ler que este par produz.
		//
		// A `Colisao` e o `QueEsconde` sao dois mapas do MESMO mundo. Bloquear e cegar sao perguntas
		// diferentes (e por isso sao dois objetos), mas "onde este mundo acaba" e a mesma resposta.
		// ====================================================================================================
		vista.SemBorda = Colisao.SemBorda;
		return vista;
	}

	public required int SpawnCelX { get; init; }
	public required int SpawnCelY { get; init; }

	/// <summary>
	/// PRECISOU ESCAVAR? Verdadeiro quando o planeta saiu sem uma clareira natural e o gerador
	/// abriu uma na marra. Nao e erro (planeta 100% agua ou 100% rocha e um resultado legitimo do
	/// ruido); e um sinal pra bancada e pro log.
	/// </summary>
	public required bool ClareiraEscavada { get; init; }

	/// <summary>O ponto de pouso em PIXELS, no centro da celula. E o que vai pro `MoveToZone`.</summary>
	public Vec2 Spawn => new(
		(SpawnCelX + 0.5f) * ZoneCollision.TileSize,
		(SpawnCelY + 0.5f) * ZoneCollision.TileSize);

	public bool Dentro(int x, int y) => x >= 0 && y >= 0 && x < Largura && y < Altura;

	public ClasseDeTerreno ClasseEm(int x, int y) =>
		Dentro(x, y) ? (ClasseDeTerreno)Chao[y * Largura + x] : ClasseDeTerreno.Montanha;

	public CoberturaDeTerreno CoberturaEm(int x, int y) =>
		Dentro(x, y) ? (CoberturaDeTerreno)Cobertura[y * Largura + x] : CoberturaDeTerreno.Nada;

	/// <summary>Como esta celula de CHAO se desenha.</summary>
	public TileVisual VisualDoChao(int x, int y) => ClasseEm(x, y) switch
	{
		ClasseDeTerreno.Agua => Paleta.Agua,
		ClasseDeTerreno.Praia => Paleta.Praia,
		ClasseDeTerreno.Planicie => Paleta.Planicie,
		ClasseDeTerreno.Colina => Paleta.Colina,
		_ => Paleta.Montanha,
	};

	/// <summary>Como a COBERTURA desta celula se desenha. Nulo = nao ha cobertura.</summary>
	public TileVisual? VisualDaCobertura(int x, int y) => CoberturaEm(x, y) switch
	{
		CoberturaDeTerreno.Arvore => Paleta.Arvore,
		CoberturaDeTerreno.ArvoreAcento => Paleta.ArvoreAcento,
		CoberturaDeTerreno.Planta => Paleta.Planta,
		CoberturaDeTerreno.Minerio => Paleta.Minerio,
		CoberturaDeTerreno.Gema => Paleta.Gema,
		_ => null,
	};

	/// <summary>
	/// A ASSINATURA do resultado inteiro. E o que uma bancada de console usa pra provar o
	/// determinismo: gere duas vezes com a mesma seed, compare dois `ulong`.
	///
	/// FNV-1a de 64 bits -- as MESMAS constantes do <see cref="ZoneKey.Hash"/>, pelo mesmo motivo
	/// que ele as escolheu: e estavel entre execucoes e entre maquinas. `GetHashCode()` nao serve
	/// (o .NET o randomiza por processo), e um hash que muda a cada boot nao prova nada.
	/// </summary>
	public ulong Assinatura()
	{
		const ulong offset = 14695981039346656037UL;
		const ulong prime = 1099511628211UL;
		ulong h = offset;

		void Somar(byte b) => h = (h ^ b) * prime;
		void Inteiro(int v) { for (int i = 0; i < 4; i++) Somar((byte)((v >> (i * 8)) & 0xFF)); }
		void Longo(ulong v) { for (int i = 0; i < 8; i++) Somar((byte)((v >> (i * 8)) & 0xFF)); }

		Longo(Seed);
		Inteiro(Largura);
		Inteiro(Altura);
		Somar((byte)Bioma);
		foreach (byte b in Chao) Somar(b);
		foreach (byte b in Cobertura) Somar(b);
		foreach (byte b in BytesDeColisao) Somar(b);
		Inteiro(SpawnCelX);
		Inteiro(SpawnCelY);
		Somar((byte)(ClareiraEscavada ? 1 : 0));
		return h;
	}
}

/// <summary>
/// O MOTOR DE TERRENO PROCEDURAL.
///
/// COMO ELE E CHAMADO: a cena do planeta (um node do Godot) sabe nome, gravidade e tipo do
/// planeta e recebe a SEED do servidor; se o planeta for procedural, ela monta um
/// <see cref="ParametrosDeTerreno"/> e chama <see cref="Gerar"/>. O Core nao conhece o Godot --
/// devolve grades de bytes e um <see cref="ZoneCollision"/>, e a cena pinta os TileMapLayer.
///
/// POR QUE O MOTOR MORA AQUI, E NAO NA CENA: porque o SERVIDOR precisa da mesma resposta. Ele nao
/// pode instanciar uma cena de 250 mil tiles por planeta (a razao de existir do `ZoneCollision`),
/// mas precisa saber onde e parede pra validar movimento. Regra unica, dois consumidores -- e a
/// mesma disciplina do `Espaco`, onde o universo inteiro ja e funcao pura de (seed, chunk).
///
/// DE ONDE VEM O RELEVO: <see cref="RuidoPerlin"/>, duas oitavas. NAO e mais o value noise do
/// `pspace_noise_at` do DM -- a troca foi pedida pelo dono do projeto e obrigou a recalibrar os
/// quatro limiares dos seis biomas, porque Perlin tem outra distribuicao. Os numeros medidos e o
/// antes/depois estao em <see cref="Paletas"/>; ler aquilo antes de mexer em limiar poupa um dia.
///
/// DETERMINISMO -- e a propriedade central, nao um detalhe:
///   * ZERO estado. Nao ha `Random`, nao ha campo estatico mutavel, nao ha `GetHashCode()`
///     (o .NET randomiza esse por processo: cliente e servidor gerariam mundos diferentes);
///   * TODO sorteio sai do <see cref="Espaco.Misturar"/>, que ja existe e ja e o hash que decide
///     onde ha planeta no universo. Um hash so pro mapa e pro chao;
///   * a aritmetica e `double` com +, -, * e / (mais `Math.Floor`, que tambem e exata). Nada de
///     `Math.Pow`/`Sin`/`Sqrt`: essas podem variar de biblioteca pra biblioteca, enquanto as
///     operacoes basicas do IEEE-754 sao exatas e iguais em qualquer maquina. As constantes
///     irracionais de que o Perlin precisa estao cravadas como literais pelo mesmo motivo;
///   * ordem de varredura fixa (y externo, x interno), entao ate o `byte[]` sai igual.
/// Quem quiser CONFERIR chama <see cref="TerrenoGerado.Assinatura"/> duas vezes.
///
/// SOBRE OS NOMES DE TILE: nenhum foi inventado. Cada atlas/estado da tabela de paletas foi lido
/// do DM (`ProceduralSpace.dm` e `Modules/Turfs/Plants.dm`) e depois CONFERIDO contra os metadados
/// do .png convertido em `Assets/Sprites` -- os `icon_state` listados aqui existem de verdade nos
/// arquivos. Onde o DM pedia um atlas que o `tileset.tres` gerado NAO registra, ha uma
/// substituicao marcada com "SUBSTITUICAO" e o porque.
/// </summary>
public static class GeradorDeTerreno
{
	/// <summary>
	/// Lado minimo/maximo. O teto nao e gosto: o cabecalho "JCOL" guarda largura e altura em
	/// uint16 (`ZoneCollision.Load`), entao acima de 65535 o formato nao representa o mapa. 2048
	/// para bem antes disso porque 2048x2048 ja sao 4,2 milhoes de tiles.
	/// </summary>
	public const int LadoMinimo = 16;
	public const int LadoMaximo = 2048;

	/// <summary>
	/// Tamanho das celulas das duas oitavas de ruido, em tiles. Sao os divisores do original
	/// (`ProceduralSpace.dm:1276`: `.../16` com peso 0,7 e `.../6` com peso 0,3).
	///
	/// Aqui eles sao ABSOLUTOS, e no DM eram relativos ao tamanho do mundo (a grade tinha
	/// `size/16+2` celulas). A diferenca aparece quando o mundo muda de tamanho: la, dobrar o
	/// mundo dobrava o tamanho das montanhas; aqui um mundo maior tem MAIS montanhas do mesmo
	/// tamanho, que e o que se espera de um planeta maior.
	/// </summary>
	private const double CelulaGrande = 16.0;
	private const double CelulaPequena = 6.0;
	private const double PesoGrande = 0.7;
	private const double PesoPequena = 0.3;

	// SAIS. Cada uso do hash precisa de um espaco proprio: os dois primeiros dao DUAS tabelas de
	// permutacao diferentes ao Perlin (com o mesmo sal a oitava fina seria a grossa em outra
	// escala, e o recorte cairia sempre nos mesmos lugares dos continentes), e o terceiro afasta a
	// flora do relevo -- senao a arvore nasceria exatamente no pico. Nao pode ser 0x9E3779B9...
	// (a proporcao aurea): o Misturar ja faz `seed ^ 0x9E37...` na primeira linha, e usar a mesma
	// constante como sal desfaria o XOR e devolveria a seed crua.
	private const ulong SalRelevoGrosso = 0xD1B54A32D192ED03UL;
	private const ulong SalRelevoFino = 0xA24BAED4963EE407UL;
	private const ulong SalCobertura = 0x7FEB352D9E374C4DUL;

	// =====================================================================
	// AS PALETAS
	// =====================================================================
	/// <summary>A paleta de um bioma. A tabela e fixa e compartilhada: ninguem a modifica.</summary>
	public static PaletaDeBioma Paleta(BiomaDeTerreno b) => Paletas[(int)b];

	/// <summary>
	/// OS LIMIARES SAO CALIBRADOS, NAO COPIADOS -- e o unico ponto em que me afastei do DM de
	/// proposito, entao vale o porque. E FORAM RECALIBRADOS UMA SEGUNDA VEZ, quando o ruido trocou.
	///
	/// PRIMEIRO PORQUE (por que nao sao os do DM): o original crava quatro numeros pra TODOS os
	/// biomas (`ProceduralSpace.dm:34-37`: agua 0,32, praia 0,38, colina 0,75, montanha 0,86). A
	/// altitude nunca foi uniforme em [0,1) -- interpolar entre nos come a variancia --, entao
	/// aqueles limiares davam 0,4% de montanha: o relevo tinha CINCO classes no papel e TRES na
	/// tela, com as montanhas nascendo como pilares soltos de um tile em vez de cordilheira.
	///
	/// SEGUNDO PORQUE (por que nao sao os da versao anterior daqui): o ruido deixou de ser VALUE
	/// NOISE e passou a ser PERLIN (ver <see cref="RuidoPerlin"/>), a pedido do dono do projeto. As
	/// duas curvas nao se parecem. Medida em 4.096.000 amostras (40 seeds x 320x320) da altitude
	/// composta (0,7 grosso + 0,3 fino), ANTES da aspereza:
	///
	///                       media   desvio    min      max     p1%     p50%    p99%
	///     value (antigo)    0,4993  0,1553   0,0078   0,9864  0,1630  0,4987  0,8379
	///     PERLIN (atual)    0,5001  0,1161   0,0758   0,9262  0,2419  0,5000  0,7586
	///
	/// Perlin e um terco MAIS ESTREITO (desvio 0,116 contra 0,155) e nao chega perto das pontas: o
	/// valor no NO da grade e sempre exatamente zero -- e a marca do gradient noise --, entao os
	/// extremos so aparecem no meio das celulas, e diluidos. Manter os limiares antigos sob Perlin
	/// teria feito, EM SILENCIO:
	///
	///     Jardim     15/10/55/16/4  ->   7,4/10,5/70,0/11,3/0,7   (agua pela metade, montanha 1/6)
	///     Deserto     3/ 6/74/13/4  ->   0,3/ 3,0/86,8/ 9,2/0,7   (oasis praticamente extinto)
	///     Gelado     13/ 9/58/15/5  ->   6,1/ 8,9/71,7/12,2/1,0
	///     Rochoso     5/ 5/35/40/15 ->   1,1/ 2,5/38,6/50,0/7,7   (metade da cordilheira sumindo)
	///     Vulcanico   8/ 6/44/31/11 ->   2,4/ 4,4/53,9/35,0/4,3
	///     Morto       5/ 5/60/24/6  ->   1,1/ 2,4/71,6/23,5/1,5
	///
	/// Os numeros abaixo sao os QUANTIS medidos da curva do Perlin: "Jardim agua = 0,376" quer
	/// dizer "os 15% mais baixos deste planeta". As proporcoes DECLARADAS em cada bioma nao mudaram
	/// -- e essa a intencao de design, e ela foi preservada na troca; o que mudou foram os numeros
	/// que a realizam.
	///
	/// POR QUE QUATRO CASAS DECIMAIS: perto da mediana a curva do Perlin e ingreme -- cada 0,01 de
	/// altitude vale ~3,4% dos tiles. Arredondar pra duas casas, como estava antes, erraria as
	/// proporcoes em varios pontos percentuais.
	///
	/// E por isso que dois biomas com a mesma intencao tem limiares diferentes -- a
	/// <see cref="PaletaDeBioma.Aspereza"/> estica a curva, e o mesmo 0,376 significa coisas
	/// diferentes em cada uma. Mexer na aspereza sem remedir os quatro limiares junto e o jeito de
	/// fazer um bioma sumir sem ninguem perceber. A conta pra remedir e direta: a aspereza e
	/// monotona, entao o limiar novo = clamp(0,5 + (quantil - 0,5) * aspereza).
	/// </summary>
	private static readonly PaletaDeBioma[] Paletas =
	[
		// -------------------------------------------------------------
		// JARDIM -- o "Verdejante" do DM (ProceduralSpace.dm:240), variante dos
		// gramados canonicos da Terra (:1127-1130).
		// -------------------------------------------------------------
		new PaletaDeBioma
		{
			Bioma = BiomaDeTerreno.Jardim,
			// a referencia dos seis: lago grande, muita planicie, cordilheira que existe mas
			// nao domina. 15% agua / 10% praia / 55% planicie / 16% colina / 4% montanha
			// MEDIDO sob Perlin, 24 planetas 500x500 (6M tiles): 15,0 / 10,0 / 54,9 / 16,0 / 4,0
			// os mesmos quatro no value noise antigo eram 0,33 / 0,39 / 0,64 / 0,77
			LimiarAgua = 0.376,
			LimiarPraia = 0.4186,
			LimiarColina = 0.6013,
			LimiarMontanha = 0.7035,
			Aspereza = 1.00,          // NOSSO: 1,00 = a curva do Perlin sem esticao nenhum
			VegetacaoPct = 10,        // DM: PSURF_TREE_PROB 5 (:28) x2 no Verdejante (:1128)
			PlantaPct = 2,            // DM: PSURF_PLANT_PROB (:29)
			MinerioPct = 3,           // DM: PSURF_ORE_PROB 3 (:30) x ore_mult 1 (:240)
			Agua = new TileVisual("Waters", "1"),               // wa_states (:1109)
			Praia = new TileVisual("Ground", "Ground2"),        // beach_states (:1117)
			Planicie = new TileVisual("Grass", "Grass1"),       // gr_verde (:1106)
			Colina = new TileVisual("Ground", "Ground1"),       // gr_terra (:1107)
			// SUBSTITUICAO: o DM usa 'JEFcliff.dmi' (:1302, :563). O .png existe em
			// Assets/Sprites/Turfs mas NAO esta no tileset.tres -- so entram no tileset os
			// atlas que algum .dmm usa, e nenhum mapa pre-feito usa esse. 'Walls.dmi' e a
			// parede dos mapas canonicos (/turf/Wall, Turfs.dm:1477-1484) e tem a MESMA
			// semantica do pspace_mountain: density=1 e passagem liberada em voo.
			Montanha = new TileVisual("Walls", "Wall1", "#9a9a8a"),   // tint = B[7] (:240)
			Arvore = new TileVisual("Tree Good", "0"),          // GoodTree (Plants.dm:141-142)
			ArvoreAcento = new TileVisual("Pine", "1"),         // PineTree (Plants.dm:143-144)
			Planta = new TileVisual("carrot", "stage 1"),       // Orange (Plants.dm:602-604) + :525
			Minerio = new TileVisual("Red Rock", ""),           // SUBSTITUICAO: ver nota do Ore
			Gema = new TileVisual("White rock", ""),            // Quartz (Mining.dm:67-69)
		},

		// -------------------------------------------------------------
		// DESERTO -- o "Deserto" do DM (:242), na variante de terra batida (:1168-1173),
		// que e a unica das tres que NAO depende de 'Dirt.dmi' (fora do tileset).
		// -------------------------------------------------------------
		new PaletaDeBioma
		{
			Bioma = BiomaDeTerreno.Deserto,
			// agua e a EXCECAO: 3% do planeta, e cada mancha dessas e um oasis.
			// 3% agua / 6% praia / 74% planicie / 13% colina / 4% montanha (mesa)
			// MEDIDO sob Perlin, 24 planetas 500x500 (6M tiles): 3,0 / 5,9 / 74,1 / 13,0 / 4,0
			// os mesmos quatro no value noise antigo eram 0,25 / 0,32 / 0,63 / 0,73
			LimiarAgua = 0.3164,
			LimiarPraia = 0.3653,
			LimiarColina = 0.5973,
			LimiarMontanha = 0.673,
			Aspereza = 0.85,          // NOSSO: duna e ondulacao longa, nao recorte
			VegetacaoPct = 1,         // DM: tree_prob = 1, "palmeiras raras (oasis)" (:1153)
			PlantaPct = 2,            // DM (:29) -- Opuntia esta no pool do bioma (:242)
			MinerioPct = 3,           // DM: 3 x ore_mult 1 (:242)
			Agua = new TileVisual("Waters", "1", "#8fd0c8"),         // tint = B[5] (:242)
			Praia = new TileVisual("Ground", "Ground7", "#e0c070"),  // beach_tint (:1173)
			Planicie = new TileVisual("Ground", "Ground2", "#e8cf7a"), // plains_tint (:1171)
			Colina = new TileVisual("Ground", "Ground1", "#c8a858"),  // hill_tint (:1172)
			Montanha = new TileVisual("Walls", "Wall1", "#b89848"),   // tint = B[7] (:242)
			Arvore = new TileVisual("Palm", "3"),               // PalmTree1 (Plants.dm:196-198)
			ArvoreAcento = new TileVisual("Palm", "1"),         // PalmTreeRight (Plants.dm:171-173)
			Planta = new TileVisual("beetroot", "stage 1"),     // Opuntia (Plants.dm:610-612)
			Minerio = new TileVisual("Red Rock", ""),
			Gema = new TileVisual("White rock", ""),
		},

		// -------------------------------------------------------------
		// GELADO -- o "Gelido" do DM (:241), variante de gelo/neve canonicos (:1141-1144).
		// -------------------------------------------------------------
		new PaletaDeBioma
		{
			Bioma = BiomaDeTerreno.Gelado,
			// quase o Jardim, com um pouco menos de agua livre (o resto esta congelado) e um
			// pouco mais de geleira alta. 13% agua / 9% praia / 58% planicie / 15% colina / 5% montanha
			// MEDIDO sob Perlin, 24 planetas 500x500 (6M tiles): 13,0 / 9,0 / 58,0 / 15,0 / 5,0
			// os mesmos quatro no value noise antigo eram 0,31 / 0,37 / 0,64 / 0,77
			LimiarAgua = 0.3588,
			LimiarPraia = 0.4022,
			LimiarColina = 0.6063,
			LimiarMontanha = 0.702,
			Aspereza = 1.05,          // NOSSO: leve exagero -- gelo racha, nao ondula
			VegetacaoPct = 5,         // DM: o Gelido nao sobrescreve tree_prob, fica o base 5 (:28)
			PlantaPct = 0,            // DM: pool de plantas VAZIO no Gelido (:241)
			MinerioPct = 3,           // DM: 3 x ore_mult 1 (:241)
			Agua = new TileVisual("Waters", "1", "#bfe8ff"),          // water_tint (:1137)
			Praia = new TileVisual("Ground", "GroundIce2", "#cfe0ee"),// beach_tint (:1138)
			Planicie = new TileVisual("Ground", "GroundSnow1"),       // gr_gelo (:1108)
			Colina = new TileVisual("Ground", "GroundIce1", "#dfeaf5"),// hill_tint (:1139)
			Montanha = new TileVisual("Walls", "Wall1", "#dfeaf5"),   // tint = B[7] (:241)
			Arvore = new TileVisual("Tree Snow", ""),           // SnowTree (Plants.dm:149-150)
			ArvoreAcento = new TileVisual("Pine", "1"),         // PineTree (Plants.dm:143-144)
			Planta = new TileVisual("carrot", "stage 1"),       // sem uso: PlantaPct = 0
			Minerio = new TileVisual("Red Rock", ""),
			Gema = new TileVisual("White rock", ""),
		},

		// -------------------------------------------------------------
		// ROCHOSO -- NAO EXISTE NO DM. Este bioma inteiro e NOSSO.
		//
		// Ele existe porque o vocabulario do projeto ja o usa: o DmPlanetScanner classifica
		// Vegeta e Hera como "Rochoso". Sem ele, pousar em Vegeta pediria um bioma emprestado.
		// A leitura e a do planeta Vegeta: terra exposta avermelhada, quase sem agua, relevo
		// quebrado e muito minerio. TODOS os numeros abaixo sao nossos.
		// -------------------------------------------------------------
		new PaletaDeBioma
		{
			Bioma = BiomaDeTerreno.Rochoso,
			// o unico dos seis onde a planicie NAO e maioria -- a paisagem e o obstaculo.
			// 5% agua / 5% praia / 35% planicie / 40% colina / 15% montanha
			// MEDIDO sob Perlin, 24 planetas 500x500 (6M tiles): 5,0 / 5,0 / 35,0 / 40,1 / 14,9
			// os mesmos quatro no value noise antigo eram 0,18 / 0,24 / 0,47 / 0,71
			LimiarAgua = 0.2602,
			LimiarPraia = 0.3102,
			LimiarColina = 0.4809,
			LimiarMontanha = 0.6551,
			Aspereza = 1.25,          // o mais recortado dos seis
			VegetacaoPct = 2,
			PlantaPct = 0,
			MinerioPct = 6,
			Agua = new TileVisual("Waters", "1"),
			Praia = new TileVisual("Ground", "Ground8"),
			Planicie = new TileVisual("Ground", "Ground4", "#c0907a"),
			Colina = new TileVisual("Ground", "Ground6", "#a87058"),
			Montanha = new TileVisual("Walls", "Wall1", "#8a6a5a"),
			// Oaktree (Plants.dm:76-78). O SmallTree do DM seria a escolha obvia, mas ele pede
			// 'DecTrees.dmi' estado "1" (Plants.dm:113-114) e esse atlas so tem os estados "" e
			// "apple" -- o estado "1" nao existe, e o pipeline cairia calado no quadro 0.
			Arvore = new TileVisual("Oak", "1"),
			ArvoreAcento = new TileVisual("Pine", "2"),
			Planta = new TileVisual("carrot", "stage 1"),       // sem uso: PlantaPct = 0
			Minerio = new TileVisual("Red Rock", ""),
			Gema = new TileVisual("White rock", ""),
		},

		// -------------------------------------------------------------
		// VULCANICO -- o "Vulcanico" do DM (:243), variante das planicies de cinza (:1178-1184).
		// -------------------------------------------------------------
		new PaletaDeBioma
		{
			Bioma = BiomaDeTerreno.Vulcanico,
			// 8% lava / 6% praia / 44% planicie de cinza / 31% colina / 11% montanha
			// MEDIDO sob Perlin, 24 planetas 500x500 (6M tiles): 8,1 / 6,0 / 43,9 / 31,0 / 11,0
			// os mesmos quatro no value noise antigo eram 0,23 / 0,29 / 0,54 / 0,74
			LimiarAgua = 0.3012,
			LimiarPraia = 0.345,
			LimiarColina = 0.5298,
			LimiarMontanha = 0.6751,
			Aspereza = 1.20,          // NOSSO: rocha quebrada
			VegetacaoPct = 1,         // DM: tree_prob = 1, "quase esteril" (:1175)
			PlantaPct = 0,            // DM: pool VAZIO (:243)
			MinerioPct = 6,           // DM: 3 (:30) x ore_mult 2 (:243)
			LiquidoEhLava = true,     // DM: water_lava = 1 (:1176)
			// o DM troca o lago pelo /turf/Water/Water7 (:1309), que e 'Waters.dmi' estado "7"
			// (Turfs.dm:1618-1619) -- a lava que da dano
			Agua = new TileVisual("Waters", "7"),
			Praia = new TileVisual("Turfs1", "dirt"),           // beach_states (:1184)
			Planicie = new TileVisual("Turfs1", "ash"),         // plains_states (:1180)
			Colina = new TileVisual("Turfs1", "hellground"),    // hill_states (:1182)
			Montanha = new TileVisual("Walls", "Wall1", "#6a4038"),   // tint = B[7] (:243)
			Arvore = new TileVisual("RedTree", ""),             // AshTree (Plants.dm:170-172)
			ArvoreAcento = new TileVisual("Tree Red", ""),      // CrimsonTree (Plants.dm:167-169)
			Planta = new TileVisual("carrot", "stage 1"),       // sem uso: PlantaPct = 0
			Minerio = new TileVisual("Red Rock", ""),
			Gema = new TileVisual("White rock", ""),
		},

		// -------------------------------------------------------------
		// MORTO -- o "Sombrio" do DM (:245), variante da rocha escura morta (:1219-1224).
		// -------------------------------------------------------------
		new PaletaDeBioma
		{
			Bioma = BiomaDeTerreno.Morto,
			// pocas paradas, nao mares. 5% agua / 5% praia / 60% planicie / 24% colina / 6% montanha
			// MEDIDO sob Perlin, 24 planetas 500x500 (6M tiles): 5,0 / 5,0 / 60,1 / 23,9 / 6,0
			// os mesmos quatro no value noise antigo eram 0,22 / 0,27 / 0,59 / 0,77
			LimiarAgua = 0.289,
			LimiarPraia = 0.333,
			LimiarColina = 0.5701,
			LimiarMontanha = 0.7009,
			Aspereza = 1.10,          // NOSSO
			VegetacaoPct = 5,         // DM: o Sombrio nao sobrescreve tree_prob, fica o base 5 (:28)
			PlantaPct = 0,            // DM: pool VAZIO (:245)
			MinerioPct = 6,           // DM: 3 (:30) x ore_mult 2 (:245)
			Agua = new TileVisual("Waters", "1", "#5a6378"),    // water_tint (:1216)
			Praia = new TileVisual("Turfs1", "dirt"),           // beach_states (:1224)
			Planicie = new TileVisual("Turfs1", "hellground"),  // plains_states (:1220)
			Colina = new TileVisual("Turfs1", "ash"),           // hill_states (:1222)
			Montanha = new TileVisual("Walls", "Wall1", "#4a5368"),   // tint = B[7] (:245)
			Arvore = new TileVisual("RedTree", ""),             // AshTree, do pool do Sombrio (:245)
			ArvoreAcento = new TileVisual("Tree Red", ""),      // CrimsonTree, idem
			Planta = new TileVisual("carrot", "stage 1"),       // sem uso: PlantaPct = 0
			Minerio = new TileVisual("Red Rock", ""),
			Gema = new TileVisual("White rock", ""),
		},
	];

	// =====================================================================
	// DE ONDE VEM O BIOMA
	// =====================================================================
	/// <summary>
	/// O tipo que a cena do planeta carrega -> bioma.
	///
	/// Aceita tanto o vocabulario do projeto (o do `DmPlanetScanner`) quanto os nomes que o DM
	/// usa nos planetas procedurais, porque sao esses que aparecem no NOME do planeta gerado
	/// (`Sistemas.NomeDeBioma`: "Verdejante-1042", "Gelido-77"...). Desconhecido cai em Rochoso,
	/// que e o mesmo padrao do `DmPlanetScanner.Scan`.
	///
	/// O "Alienigena" do DM (:244) cai em JARDIM, e isso e escolha nossa: olhando o que o DM faz
	/// com ele (:1197-1214), a diferenca pro Verdejante e TINTA -- grama teal, agua verde -- e nao
	/// estrutura. Ele tem lagos, floresta e planicie iguais. Forcar um sexto comportamento so pra
	/// nao repetir seria inventar terreno que ninguem pediu.
	/// </summary>
	public static BiomaDeTerreno BiomaDoTipo(string? tipo) => (tipo ?? "").Trim().ToLowerInvariant() switch
	{
		"jardim" or "verdejante" or "alienigena" or "alienígena" => BiomaDeTerreno.Jardim,
		"deserto" => BiomaDeTerreno.Deserto,
		"gelado" or "gelido" or "gélido" => BiomaDeTerreno.Gelado,
		"vulcanico" or "vulcânico" => BiomaDeTerreno.Vulcanico,
		"morto" or "sombrio" => BiomaDeTerreno.Morto,
		_ => BiomaDeTerreno.Rochoso,
	};

	/// <summary>
	/// O bioma de um planeta PROCEDURAL, tirado da propria seed dele.
	///
	/// Nao e um sorteio novo: e a MESMA conta que o <see cref="Espaco"/> ja usou pra batizar o
	/// planeta (`Sistemas.NomeDeBioma`, `(seed >> 16) % 6`, sobre a mesma seed que o
	/// `SistemaSolar.Planeta` deu ao corpo). Precisa ser a
	/// mesma, senao um mundo chamado "Gelido-77" nasceria com areia -- o tipo de incoerencia que
	/// ninguem reporta como bug e todo mundo estranha.
	/// </summary>
	public static BiomaDeTerreno BiomaDaSeed(ulong seedDoPlaneta) => ((seedDoPlaneta >> 16) % 6) switch
	{
		0 => BiomaDeTerreno.Jardim,      // "Verdejante"
		1 => BiomaDeTerreno.Gelado,      // "Gelido"
		2 => BiomaDeTerreno.Deserto,     // "Deserto"
		3 => BiomaDeTerreno.Vulcanico,   // "Vulcanico"
		4 => BiomaDeTerreno.Jardim,      // "Alienigena" -- ver BiomaDoTipo
		_ => BiomaDeTerreno.Morto,       // "Sombrio"
	};

	/// <summary>
	/// O EMPURRAO DA GRAVIDADE na aspereza do relevo. INVENCAO NOSSA -- o DM sorteia gravidade
	/// (`ProceduralSpace.dm:448-452`) e ela nao toca no terreno.
	///
	/// A ideia e que um mundo de 40x nao pareca a Terra pintada de outra cor: ele fica mais
	/// quebrado. O efeito e de proposito PEQUENO e saturado (1x = 1,00; 10x = 1,09; 40x = 1,39;
	/// dali pra cima nao cresce mais), porque gravidade ja e o eixo mais forte do jogo pelo
	/// treino, e deixa-la reescrever a geografia junto seria empilhar duas coisas grandes num
	/// numero so.
	/// </summary>
	public static double FatorDeGravidade(double gravidade) =>
		1.0 + Math.Clamp((gravidade - 1.0) / 100.0, 0.0, 0.40);

	// =====================================================================
	// O RUIDO
	// =====================================================================
	/// <summary>
	/// AS DUAS CAMADAS DE RELEVO de um planeta: a grossa (continentes) e a fina (recorte).
	///
	/// Sao dois <see cref="RuidoPerlin"/> porque cada um carrega a propria tabela de permutacao --
	/// montar essa tabela custa 255 embaralhamentos, entao ela e feita UMA vez por planeta e reusada
	/// pelos 250 mil tiles. Criar um `RuidoPerlin` por tile funcionaria e seria absurdamente lento.
	///
	/// A funcao continua PURA: as mesmas (seed) dao as mesmas duas tabelas em qualquer maquina, e o
	/// valor de um tile nao depende de nenhum outro ter sido calculado antes. E o que permite, mais
	/// pra frente, gerar planeta por chunk como o `Espaco` ja faz.
	/// </summary>
	public static (RuidoPerlin Grosso, RuidoPerlin Fino) CamadasDeRelevo(ulong seed) =>
		(new RuidoPerlin(seed, SalRelevoGrosso), new RuidoPerlin(seed, SalRelevoFino));

	/// <summary>
	/// A ALTITUDE de um tile, em [0,1]. Duas oitavas: uma grossa que faz os continentes e uma
	/// fina que faz o recorte -- 0,7 e 0,3, os pesos do original (`ProceduralSpace.dm:1276`).
	///
	/// Esta sobrecarga e a do CAMINHO QUENTE: recebe as camadas ja montadas. Quem tem so a seed na
	/// mao usa a outra, que monta e joga fora.
	/// </summary>
	public static double Altitude(RuidoPerlin grosso, RuidoPerlin fino, int x, int y, double aspereza)
	{
		double h = grosso.Em(x / CelulaGrande, y / CelulaGrande) * PesoGrande
				 + fino.Em(x / CelulaPequena, y / CelulaPequena) * PesoPequena;

		h = 0.5 + (h - 0.5) * aspereza;   // ver PaletaDeBioma.Aspereza
		return h < 0.0 ? 0.0 : h > 1.0 ? 1.0 : h;
	}

	/// <summary>
	/// A altitude de UM tile a partir da seed crua. Publica porque uma bancada consegue sondar o
	/// perfil sem gerar o mapa inteiro.
	///
	/// CUIDADO: ela monta as duas tabelas de permutacao a cada chamada. Pra varrer area, pegue as
	/// camadas uma vez com <see cref="CamadasDeRelevo"/> e chame a outra sobrecarga -- a diferenca
	/// e de duas ordens de grandeza.
	/// </summary>
	public static double Altitude(ulong seed, int x, int y, double aspereza)
	{
		(RuidoPerlin grosso, RuidoPerlin fino) = CamadasDeRelevo(seed);
		return Altitude(grosso, fino, x, y, aspereza);
	}

	// =====================================================================
	// A GERACAO
	// =====================================================================
	/// <summary>
	/// GERA O TERRENO. Funcao pura: os mesmos parametros dao o mesmo `byte[]`, sempre.
	/// </summary>
	public static TerrenoGerado Gerar(ParametrosDeTerreno p)
	{
		ArgumentNullException.ThrowIfNull(p);

		int w = Math.Clamp(p.Largura, LadoMinimo, LadoMaximo);
		int h = Math.Clamp(p.Altura, LadoMinimo, LadoMaximo);
		PaletaDeBioma pal = Paleta(p.Bioma);
		double aspereza = pal.Aspereza * FatorDeGravidade(p.Gravidade);

		var chao = new byte[w * h];
		var cobertura = new byte[w * h];
		var bits = new byte[(w * h + 7) / 8];

		// O PLANO DA AGUA, montado na MESMA passada que o de parede. Sao dois bitsets porque sao
		// duas classes de celula diferentes (ver `ClasseDeAgua`): montanha para todo mundo, agua
		// para so quem esta a pe. Deriva-lo depois, de um segundo laco sobre o `chao`, seria uma
		// segunda derivacao do mesmo dado -- e a clareira mexe nos dois, entao eles precisam
		// envelhecer juntos.
		var agua = new byte[(w * h + 7) / 8];

		// as tabelas de permutacao do Perlin: UMA vez por planeta, nao uma por tile
		(RuidoPerlin grosso, RuidoPerlin fino) = CamadasDeRelevo(p.Seed);

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				int i = y * w + x;

				double alt = Altitude(grosso, fino, x, y, aspereza);
				ClasseDeTerreno classe =
					alt < pal.LimiarAgua ? ClasseDeTerreno.Agua :
					alt < pal.LimiarPraia ? ClasseDeTerreno.Praia :
					alt < pal.LimiarColina ? ClasseDeTerreno.Planicie :
					alt < pal.LimiarMontanha ? ClasseDeTerreno.Colina :
					ClasseDeTerreno.Montanha;

				chao[i] = (byte)classe;

				// UM hash por celula, TRES sorteios em faixas de bits diferentes. Chamar o
				// Misturar tres vezes custaria tres vezes mais pelo mesmo resultado -- as faixas
				// altas e baixas de um splitmix ja sao independentes entre si.
				ulong r = Espaco.Misturar(p.Seed ^ SalCobertura, (ulong)(uint)x, (ulong)(uint)y);
				int d1 = (int)(r % 100);
				int d2 = (int)((r >> 20) % 100);
				int d3 = (int)((r >> 40) % 100);

				CoberturaDeTerreno cob = CoberturaDeTerreno.Nada;
				switch (classe)
				{
					case ClasseDeTerreno.Planicie:
						// ProceduralSpace.dm:1334-1344 -- arvore primeiro, planta so onde nao ha arvore
						if (d1 < pal.VegetacaoPct)
							cob = d2 < 85 ? CoberturaDeTerreno.Arvore : CoberturaDeTerreno.ArvoreAcento;
						else if (d2 < pal.PlantaPct)
							cob = CoberturaDeTerreno.Planta;
						break;

					case ClasseDeTerreno.Colina:
						// ProceduralSpace.dm:1350-1361 -- minerio tem prioridade; arvore o DOBRO da planicie
						if (d1 < pal.MinerioPct)
							cob = d3 < 25 ? CoberturaDeTerreno.Gema : CoberturaDeTerreno.Minerio;
						else if (d2 < pal.VegetacaoPct * 2)
							cob = d3 < 85 ? CoberturaDeTerreno.Arvore : CoberturaDeTerreno.ArvoreAcento;
						break;
				}
				cobertura[i] = (byte)cob;

				if (Bloqueia(classe, cob)) bits[i >> 3] |= (byte)(1 << (i & 7));
				if (classe == ClasseDeTerreno.Agua) agua[i >> 3] |= (byte)(1 << (i & 7));
			}
		}

		(int sx, int sy, bool escavou) = AcharOuAbrirClareira(w, h, chao, cobertura, bits, agua);

		// o blob e montado DEPOIS da clareira -- montar antes congelaria os bits que a escavacao
		// ainda ia apagar, e o servidor acharia parede exatamente no ponto de pouso
		byte[] jcol = MontarJcol(w, h, bits);

		// A COLISAO SAI JA COM A AGUA DENTRO, e e isto que fecha o buraco do lado do SERVIDOR: ele
		// guarda de cada planeta gerado so a `ZonaGerada` (`GameServer.Procedural.cs:193-204`) e
		// JOGA FORA o `TerrenoGerado` -- ou seja, o `Chao`, que e onde a agua mora. O cliente
		// regenera pela semente e sabe onde e agua; a autoridade nao saberia. Pendurar o plano no
		// proprio `ZoneCollision` faz ele viajar pelo caminho que o servidor ja guarda, sem campo
		// novo em lugar nenhum.
		ZoneCollision colisao = ZoneCollision.Load(jcol)!;
		colisao.DefinirAgua(agua);

		return new TerrenoGerado
		{
			Largura = w,
			Altura = h,
			Bioma = p.Bioma,
			Seed = p.Seed,
			Paleta = pal,
			Chao = chao,
			Cobertura = cobertura,
			BytesDeColisao = jcol,
			BytesDeAgua = agua,
			// so falharia com w/h fora do uint16, e o Clamp la em cima ja impede
			Colisao = colisao,
			SpawnCelX = sx,
			SpawnCelY = sy,
			ClareiraEscavada = escavou,
		};
	}

	/// <summary>
	/// QUEM E PAREDE.
	///
	/// Montanha e arvore bloqueiam; agua NAO. Isso nao e simplificacao: no DM o `/turf/Water` nao
	/// tem `density` (`Turfs.dm:1602-1607`) -- quem decide se voce entra e o `testWaters()`, que e
	/// uma regra de personagem (nadar/voar) e nao de geometria. Marcar agua como parede aqui
	/// tornaria o lago intransponivel ate pra quem voa.
	///
	/// Minerio e planta ficam de fora porque sao coisas de PEGAR, e o `/obj/Raw_Material` do DM
	/// nao declara densidade. **Isto precisa de conferencia** quando o sistema de mineracao for
	/// portado: se no jogo final o veio for um obstaculo, e uma linha aqui.
	/// </summary>
	public static bool Bloqueia(ClasseDeTerreno classe, CoberturaDeTerreno cobertura) =>
		classe == ClasseDeTerreno.Montanha
		|| cobertura is CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento;

	/// <summary>
	/// Cabecalho "JCOL" + uint16 largura + uint16 altura + bitset (ver ZoneCollision.Load).
	///
	/// PUBLICA DESDE QUE O INTERIOR DE NAVE PASSOU A PRODUZIR CHAO (<see cref="Tech.NaveGrande"/>):
	/// ele monta o proprio bitset a mao (uma sala, nao um mundo de ruido) mas tem que gravar o
	/// MESMO cabecalho, senao o `ZoneCollision.Load` recusa calado e a nave vira uma zona sem
	/// parede. Uma segunda copia de oito bytes de cabecalho e exatamente o tipo de verdade
	/// duplicada que a regra 4 da casa proibe.
	/// </summary>
	public static byte[] MontarJcol(int w, int h, byte[] bits)
	{
		var blob = new byte[8 + bits.Length];
		blob[0] = (byte)'J'; blob[1] = (byte)'C'; blob[2] = (byte)'O'; blob[3] = (byte)'L';
		blob[4] = (byte)(w & 0xFF); blob[5] = (byte)(w >> 8);
		blob[6] = (byte)(h & 0xFF); blob[7] = (byte)(h >> 8);
		Array.Copy(bits, 0, blob, 8, bits.Length);
		return blob;
	}

	// =====================================================================
	// O PONTO DE POUSO
	// =====================================================================
	/// <summary>
	/// Acha o melhor ponto de pouso e GARANTE que ele e livre.
	///
	/// O original nao garantia: ele guardava o tile de planicie mais perto do centro
	/// (`ProceduralSpace.dm:1326-1330`) e, se o planeta saisse sem planicie nenhuma, pousava no
	/// centro "mesmo" (`:1367-1369`) -- que pode ser o meio de um lago ou de uma cordilheira.
	/// Aqui a busca tem tres degraus, e o terceiro nao pode falhar:
	///
	///   1. planicie limpa, a mais perto do centro (o criterio do DM);
	///   2. qualquer celula que nao seja parede NEM AGUA (planeta quase todo montanha);
	///   3. ESCAVA uma clareira de 3x3 no centro -- vira planicie, sem cobertura, sem colisao.
	///
	/// E em qualquer um dos casos as oito vizinhas perdem a cobertura: nascer cercado por oito
	/// arvores e tecnicamente "livre" e na pratica e nascer preso.
	///
	/// ============================ O DEGRAU 2 ACEITAVA AGUA, E ISSO VIROU DEFEITO ============================
	/// Enquanto agua era chao comum, "nao e parede" bastava. Agora que ela para quem esta a pe, um
	/// planeta oceanico (o Verdejante tem 15% de agua, e um Aquatico existiria) podia largar o corpo
	/// no meio do mar: livre pela colisao, parado pela regra. A escavacao do degrau 3 tambem passa a
	/// secar a agua do 3x3 -- o degrau 3 e o unico que nao pode falhar.
	///
	/// **ISTO MUDA O PONTO DE POUSO DE ALGUMAS SEMENTES ANTIGAS** -- so as que caiam no degrau 2 em
	/// cima de agua. Nao muda o TERRENO de nenhuma: o ruido, os limiares e a cobertura sao os
	/// mesmos, e a mesma semente continua dando o mesmo mapa.
	/// ========================================================================================================
	/// </summary>
	private static (int X, int Y, bool Escavou) AcharOuAbrirClareira(
		int w, int h, byte[] chao, byte[] cobertura, byte[] bits, byte[] agua)
	{
		int cx = w / 2;
		int cy = h / 2;

		int raioMax = Math.Max(w, h);
		for (int exigente = 1; exigente >= 0; exigente--)
		{
			bool Serve(int x, int y)
			{
				// a borda fica de fora: fora do mapa o ZoneCollision ja e parede, e nascer
				// colado nela deixa o corpo com metade da tela no nada
				if (x < 1 || y < 1 || x >= w - 1 || y >= h - 1) return false;

				int i = y * w + x;
				if ((bits[i >> 3] & (1 << (i & 7))) != 0) return false;
				// AGUA NAO E CHAO: ela nao esta no `bits` (nao e parede) e precisa ser recusada
				// aqui, senao o degrau 2 pousa o corpo dentro do lago. Ver `ClasseDeAgua.ServeDeChao`.
				if (chao[i] == (byte)ClasseDeTerreno.Agua) return false;
				if (cobertura[i] != (byte)CoberturaDeTerreno.Nada) return false;
				return exigente == 0 || chao[i] == (byte)ClasseDeTerreno.Planicie;
			}

			// ANEIS de Chebyshev saindo do centro. Percorre so o PERIMETRO de cada anel, nao o
			// quadrado inteiro com um `continue` no miolo: varrer o quadrado torna a busca
			// O(n^1.5), e num planeta 500x500 sem clareira nenhuma isso e mais de cem milhoes de
			// testes -- caro justamente no caso ruim, que e quando a busca importa.
			for (int r = 0; r <= raioMax; r++)
			{
				for (int d = -r; d <= r; d++)
				{
					// as quatro bordas do anel (em r = 0 as quatro sao o proprio centro)
					if (Serve(cx + d, cy - r)) return Aceitar(cx + d, cy - r);
					if (Serve(cx + d, cy + r)) return Aceitar(cx + d, cy + r);
					if (Serve(cx - r, cy + d)) return Aceitar(cx - r, cy + d);
					if (Serve(cx + r, cy + d)) return Aceitar(cx + r, cy + d);
				}
			}
		}

		(int, int, bool) Aceitar(int x, int y)
		{
			LimparEmVolta(w, h, chao, cobertura, bits, agua, x, y, cavar: false);
			return (x, y, false);
		}

		// planeta 100% parede OU 100% agua: abre a clareira na marra
		LimparEmVolta(w, h, chao, cobertura, bits, agua, cx, cy, cavar: true);
		return (cx, cy, true);
	}

	/// <summary>
	/// Tira a cobertura das oito vizinhas (e, quando <paramref name="cavar"/>, tambem derruba a
	/// montanha delas, SECA a agua delas, e faz o mesmo com a do centro). Mantem as QUATRO
	/// estruturas coerentes -- chao, cobertura, parede e agua saem juntos, senao o cliente
	/// desenharia agua onde o servidor ja diz que se anda.
	/// </summary>
	private static void LimparEmVolta(int w, int h, byte[] chao, byte[] cobertura, byte[] bits,
		byte[] agua, int cx, int cy, bool cavar)
	{
		for (int dy = -1; dy <= 1; dy++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				int x = cx + dx;
				int y = cy + dy;
				if (x < 0 || y < 0 || x >= w || y >= h) continue;

				int i = y * w + x;
				cobertura[i] = (byte)CoberturaDeTerreno.Nada;
				if (cavar && (chao[i] == (byte)ClasseDeTerreno.Montanha
							  || chao[i] == (byte)ClasseDeTerreno.Agua))
				{
					chao[i] = (byte)ClasseDeTerreno.Planicie;
					// o desenho E a regra saem juntos: sem esta linha o cliente pintaria mar e o
					// servidor deixaria andar por cima
					agua[i >> 3] &= (byte)~(1 << (i & 7));
				}

				if (!cavar && chao[i] == (byte)ClasseDeTerreno.Montanha) continue;  // montanha fica
				bits[i >> 3] &= (byte)~(1 << (i & 7));
			}
		}
	}
}
