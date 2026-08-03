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

	/// <summary>
	/// A FICHA DE UM PLANETA, DEDUZIDA DA SEED.
	///
	/// BIOMA: nao ha sorteio novo -- e o mesmo `(seed >> 16) % 6` que o `Espaco` ja usou pra
	/// batizar o planeta, via <see cref="GeradorDeTerreno.BiomaDaSeed"/>. Precisa ser o mesmo,
	/// senao um mundo chamado "Gelido-77" nasceria com areia.
	///
	/// TAMANHO: sai do RAIO que o planeta tem no mapa do espaco
	/// (`Espaco.PlanetaDaChunk`: <c>110 + (h >> 40) % 90</c>, e la <c>h</c> E a seed). Ou seja, o
	/// disco que o jogador ve antes de pousar diz o tamanho do mundo em que ele vai pisar -- um
	/// planeta grande na tela E grande no chao. Sortear o lado por conta propria daria a
	/// incoerencia silenciosa de uma bolinha com 200 mil tiles dentro.
	///
	/// GRAVIDADE: a escada e a do DM, literal (`ProceduralSpace.dm:448-452`) -- "maioria leve,
	/// cauda pesada", com o comentario do proprio autor explicando o porque ("com o esmagamento,
	/// planeta 40x+ e zona de morte"). O `PR.next(n)` de la devolve 1..n, e e por isso que os
	/// intervalos abaixo comecam em 1 e nao em 0.
	/// </summary>
	public static MundoProcedural DaSeed(ulong seed, string nome)
	{
		// o raio do disco no espaco: 110..199 px
		int raio = 110 + (int)((seed >> 40) % 90);

		// 110..199 -> 192..352 tiles, em degraus de 32.
		//
		// O TETO E MEDIDO, e ele nao e limitado pela pintura (que e fatiada) e sim pela GERACAO,
		// que nao da pra fatiar: a colisao tem que existir inteira antes do primeiro passo. Rodando
		// dentro do Godot, `Gerar` custou 14 ms num mundo de 256 e 43 ms num de 448 -- e no
		// SERVIDOR esse tempo para o tick de TODO MUNDO, nao so de quem pousou. 352 (124 mil
		// celulas, ~27 ms) cabe dentro de um tick de 33 ms com folga; 448 nao cabia.
		//
		// O DM usava 500 fixo pra todo planeta (`PSURF_SIZE`), e o `GeradorDeTerreno` aceita ate
		// 2048. Subir daqui e mexer no divisor abaixo -- sabendo o que se paga por isso.
		int lado = 192 + (raio - 110) / 15 * 32;

		ulong h = Espaco.Misturar(seed ^ SalGravidade, 0, 0);
		int faixa = (int)(h % 100);              // o `PR.next(100)` do DM, em 0..99
		int dentro = (int)((h >> 20) % 100);     // segundo sorteio, faixa de bits independente

		double gravidade =
			faixa < 55 ? 1 + dentro % 5 :        // DM: PR.next(5)        -> 1..5
			faixa < 85 ? 6 + dentro % 10 :       // DM: 5 + PR.next(10)   -> 6..15
			faixa < 97 ? 16 + dentro % 25        // DM: 15 + PR.next(25)  -> 16..40
					   // SEM MODULO SOBRE MODULO. `dentro` ja e `h % 100`; fazer `% 40` em cima disso enviesa
					   // a cauda -- os restos 0..19 saem de tres valores de `dentro` e os 20..39 de dois, entao
					   // gravidade 41..60 sai 50% mais comum que 61..80. Uma faixa de bits INDEPENDENTE do
					   // mesmo hash nao tem esse estreitamento.
					   : 41 + (int)((h >> 20) % 40);       // DM: 40 + PR.next(40)  -> 41..80

		return new MundoProcedural
		{
			Seed = seed,
			Nome = nome ?? "",
			Bioma = GeradorDeTerreno.BiomaDaSeed(seed),
			Lado = lado,
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