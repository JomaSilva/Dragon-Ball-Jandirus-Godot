namespace Jandirus.Core.World;

// ===========================================================================================
// ESTE ARQUIVO ESTAVA NO SERVIDOR e o CLIENTE o chamava.
//
// Compilava, porque Client e Server caem no mesmo assembly do Godot. Mas era a unica regra do
// lote que as DUAS pontas precisam calcular IDENTICA -- quem decide o bioma, o tamanho e a
// gravidade de um planeta gerado. Uma resposta diferente de cada lado nao da erro: da um jogador
// andando num deserto enquanto o servidor acha que ali e agua.
//
// Regra do projeto (`Core/Jandirus.Core.csproj`): o que decide resultado de jogo mora no Core,
// que nao conhece o engine. Aqui e onde isto sempre devia ter estado.
// ===========================================================================================

/// <summary>
/// A FICHA DE UM MUNDO GERADO: tudo que a geracao precisa saber, TIRADO SO DA SEED.
///
/// ============================ POR QUE "SO DA SEED" E A REGRA ============================
/// O cliente e o servidor tem que chegar no MESMO chao sem trocar um byte de mapa. O unico dado
/// que ja viaja de um planeta gerado e a <see cref="ZoneKey"/> -- tipo, nome e seed (ver
/// `Protocol.PutZone`). Entao TUDO que entra em `GeradorDeTerreno.Gerar` tem que ser funcao pura
/// desses tres campos, senao as duas pontas divergem.
///
/// Isso nao e teoria: a bancada mediu. Duas geracoes com a mesma seed e gravidade 3 dao a mesma
/// assinatura; a MESMA seed com gravidade 1 da outra assinatura, porque a gravidade entra na
/// aspereza do relevo (`GeradorDeTerreno.FatorDeGravidade`). Ou seja, se o servidor sorteasse a
/// gravidade e o cliente chutasse 1, os dois desenhariam planetas diferentes com o mesmo nome --
/// e o sintoma seria o jogador atravessando montanha que ele ve e batendo em parede invisivel.
/// =======================================================================================
///
/// ONDE ISTO DEVERIA MORAR: em `Core/World/Espaco.cs`, como campos de <see cref="PlanetaNoEspaco"/>
/// -- e la que o universo ja decide nome, posicao e raio de cada planeta a partir da seed. Esta
/// classe esta aqui porque `Espaco.cs` nao e meu nesta rodada; o relatorio traz a assinatura da
/// mudanca. Enquanto ela nao acontece, o CLIENTE chama esta classe (o caminho Client -> Server ja
/// existe: `Boot.cs` e `PauseMenu.cs` falam com `GameServer.Instance`).
/// </summary>
public sealed class MundoProcedural
{
	/// <summary>A seed do planeta -- a mesma que veio no `PlanetaNoEspaco.Seed` e na `ZoneKey`.</summary>
	public required ulong Seed { get; init; }

	/// <summary>O nome da zona ("Verdejante-1042"). So pra log e pra ficha: nao entra na geracao.</summary>
	public required string Nome { get; init; }

	public required BiomaDeTerreno Bioma { get; init; }

	/// <summary>Lado do mundo em tiles. Quadrado, como o `PSURF_SIZE` do original.</summary>
	public required int Lado { get; init; }

	/// <summary>O `Planetgrav`: multiplica ganho de treino e pesa no poder efetivo.</summary>
	public required double Gravidade { get; init; }

	// SAIS. Cada sorteio precisa de um espaco proprio, senao a gravidade sairia correlacionada
	// com o bioma. Nao pode ser 0x9E3779B9...: o `Misturar` ja faz `seed ^ 0x9E37...` na primeira
	// linha, e repetir a constante como sal desfaria o XOR e devolveria a seed crua.
	private const ulong SalGravidade = 0xC2B2AE3D27D4EB4FUL;

	/// <summary>A gravidade mais leve e a mais pesada que a escada do DM consegue sortear.</summary>
	public const double GravidadeMinima = 1;
	public const double GravidadeMaxima = 80;

	/// <summary>
	/// Lado do menor e do maior mundo gerado, em tiles.
	///
	/// ============================ POR QUE O TETO E 1000, E O QUE ELE CUSTA ============================
	/// O limite NAO e a pintura: desde que o cenario e entregue ao tilemap em pedacos de 64x64 (ver
	/// `Client.PintorDePedacos`), desenhar custa o mesmo num mundo de 192 e num de 1000 -- so os
	/// pedacos em volta da camera entram, e o teto de memoria de desenho e fixo.
	///
	/// O limite e a GERACAO, que nao da pra fatiar: a colisao tem que existir inteira antes do
	/// primeiro passo (o servidor valida movimento contra ela) e o ponto de pouso sai de uma busca
	/// que precisa do mapa pronto. E o custo e linear na area:
	///
	///     lado    celulas    gerar
	///      352    124 mil    ~27 ms
	///     1000      1 mi    ~220 ms
	///
	/// NO SERVIDOR esse tempo para o tick de TODO MUNDO, nao so de quem pousou -- e o tick e de
	/// 33 ms. Um mundo de 1000 custa ~7 ticks parados, UMA vez, no instante em que o primeiro
	/// jogador pousa nele (depois fica em cache).
	///
	/// O que torna isso aceitavel e a ESCADA DE GRAVIDADE ser a mesma que decide o tamanho: mundo
	/// grande e mundo de gravidade alta, e gravidade alta e rara de proposito (3% passam de 40).
	/// Ou seja, o caso caro e o caso incomum, e ele vem junto do planeta que o jogo ja trata como
	/// excepcional. Amarrar o tamanho a um sorteio proprio poria 1 milhao de celulas num planeta
	/// qualquer.
	///
	/// O `GeradorDeTerreno` aceita ate 2048 (4,2 milhoes de celulas, ~1 s). Subir daqui e mexer
	/// neste numero -- sabendo o que se paga.
	/// =================================================================================================
	/// </summary>
	public const int LadoMinimo = 192;
	public const int LadoMaximo = 1000;

	/// <summary>
	/// A GRAVIDADE DE UM MUNDO, so da seed.
	///
	/// Publica porque agora ela decide DUAS coisas: o tamanho do mundo (aqui) e o raio do disco no
	/// mapa do espaco (`Espaco.PlanetaDaChunk`). Duas copias da escada divergiriam no dia em que
	/// alguem mexesse numa delas, e o sintoma seria um disco pequeno escondendo um mundo enorme.
	///
	/// A escada e a do DM, literal (`ProceduralSpace.dm:448-452`) -- "maioria leve, cauda pesada",
	/// com o comentario do proprio autor explicando o porque ("com o esmagamento, planeta 40x+ e
	/// zona de morte"). O `PR.next(n)` de la devolve 1..n, e e por isso que os intervalos abaixo
	/// comecam em 1 e nao em 0.
	/// </summary>
	public static double GravidadeDaSeed(ulong seed)
	{
		ulong h = Espaco.Misturar(seed ^ SalGravidade, 0, 0);
		int faixa = (int)(h % 100);              // o `PR.next(100)` do DM, em 0..99
		int dentro = (int)((h >> 20) % 100);     // segundo sorteio, faixa de bits independente

		return
			faixa < 55 ? 1 + dentro % 5 :        // DM: PR.next(5)        -> 1..5
			faixa < 85 ? 6 + dentro % 10 :       // DM: 5 + PR.next(10)   -> 6..15
			faixa < 97 ? 16 + dentro % 25        // DM: 15 + PR.next(25)  -> 16..40
					   // SEM MODULO SOBRE MODULO. `dentro` ja e `h % 100`; fazer `% 40` em cima disso enviesa
					   // a cauda -- os restos 0..19 saem de tres valores de `dentro` e os 20..39 de dois, entao
					   // gravidade 41..60 sai 50% mais comum que 61..80. Uma faixa de bits INDEPENDENTE do
					   // mesmo hash nao tem esse estreitamento.
					   : 41 + (int)((h >> 20) % 40);       // DM: 40 + PR.next(40)  -> 41..80
	}

	/// <summary>
	/// O LADO DO MUNDO, EM TILES, A PARTIR DA GRAVIDADE.
	///
	/// Regra do dono: o planeta de gravidade mais alta que a natureza consegue sortear e tambem o
	/// maior possivel, e o de gravidade mais baixa e o menor. E coerente com o que a gravidade
	/// significa -- mais massa, mundo maior -- e tem a vantagem pratica de por o custo de geracao
	/// exatamente onde a raridade ja esta (ver <see cref="LadoMaximo"/>).
	///
	/// INTERPOLACAO DIRETA, sem degraus: a conta e exata nas duas pontas (gravidade 1 da 192,
	/// gravidade 80 da 1000). Os degraus de 32 que existiam aqui vinham de o lado ser derivado do
	/// raio do disco em seis faixas, e nao havia razao pra manter a quantizacao depois que a fonte
	/// mudou -- um mundo de 233 tiles nao e mais dificil de nada que um de 224.
	/// </summary>
	public static int LadoDaGravidade(double gravidade)
	{
		double t = (gravidade - GravidadeMinima) / (GravidadeMaxima - GravidadeMinima);
		t = Math.Clamp(t, 0, 1);
		return LadoMinimo + (int)Math.Round(t * (LadoMaximo - LadoMinimo));
	}

	/// <summary>
	/// A FICHA DE UM PLANETA, DEDUZIDA DA SEED.
	///
	/// BIOMA: nao ha sorteio novo -- e o mesmo `(seed >> 16) % 6` que o `Espaco` ja usou pra
	/// batizar o planeta, via <see cref="GeradorDeTerreno.BiomaDaSeed"/>. Precisa ser o mesmo,
	/// senao um mundo chamado "Gelido-77" nasceria com areia.
	///
	/// TAMANHO: sai da GRAVIDADE (ver <see cref="LadoDaGravidade"/>). Antes saia do raio do disco
	/// no mapa do espaco, e a coerencia "planeta grande na tela e grande no chao" continua valendo
	/// -- so que agora pelo outro lado: o `Espaco` e que passou a tirar o raio do disco da mesma
	/// gravidade. Um sorteio, tres consequencias, nada pra divergir.
	/// </summary>
	public static MundoProcedural DaSeed(ulong seed, string nome)
	{
		double gravidade = GravidadeDaSeed(seed);

		return new MundoProcedural
		{
			Seed = seed,
			Nome = nome ?? "",
			Bioma = GeradorDeTerreno.BiomaDaSeed(seed),
			Lado = LadoDaGravidade(gravidade),
			Gravidade = gravidade,
		};
	}

	/// <summary>O pedido pronto pro Core. As duas pontas montam ESTE objeto, com estes valores.</summary>
	public ParametrosDeTerreno Parametros() => new()
	{
		Seed = Seed,
		Largura = Lado,
		Altura = Lado,
		Bioma = Bioma,
		Gravidade = Gravidade,
		Nome = Nome,
	};

	/// <summary>A linha que o jogador le ao pousar.</summary>
	public string Descricao() => $"{Bioma}, {Lado}x{Lado} tiles, gravidade {Gravidade:0.##}x";
}

/// <summary>
/// O PLANETA GERADO, LADO DO SERVIDOR.
///
/// O SERVIDOR NAO DESENHA, MAS PRECISA DA MESMA GEOMETRIA. Ele e quem decide onde e parede (o
/// `MoveRules.ValidateStep` roda com o `ZoneCollision` da zona) e onde o corpo aparece ao pousar.
/// Como o motor de terreno mora no Core, ele chama a MESMA `GeradorDeTerreno.Gerar` com a MESMA
/// seed que o cliente -- uma funcao, duas pontas, zero bytes de mapa na rede. E o mesmo desenho do
/// <see cref="Espaco"/>, onde o universo inteiro ja e funcao pura de (seed, chunk).
///
/// POR QUE HA CACHE: gerar o maior mundo de hoje (352x352) custa ~27 ms rodando dentro do Godot.
/// Isso e barato UMA vez e absurdo a cada passo validado -- e o passo e validado a 30 Hz POR
/// JOGADOR. O que fica guardado e so o que o servidor consulta: a colisao (15 KB) e o ponto de
/// chegada. As duas grades de bytes do desenho (mais 248 KB por mundo) sao soltas na hora, porque
/// quem pinta e o cliente e ele gera as dele.
/// </summary>