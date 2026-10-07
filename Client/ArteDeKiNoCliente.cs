using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Client;

/// <summary>
/// COMO CADA ARTE DE KI SE DESENHA -- a metade de TELA da <see cref="ArteDeProjetil"/>.
///
/// ============================ ELE ERA UM FORMATADOR DE CAMINHO, E VIROU A MESA DE ESTILOS ============================
/// Este arquivo dizia onde morava a folha de cada arte (`res://Assets/Sprites/Beams/Beam3.tres`) e a
/// carregava. O dono mandou trocar as folhas por shader (2026-10-07): *"ao inves de usar sprite pra
/// cada beam como era no byond, fazer eles por efeitos, noise e shaders do proprio godot"*. Nao ha
/// mais folha pra carregar; o que cada arte precisa agora e de um ESTILO -- os numeros que o
/// `FeixeDeKi.gdshader` e o `EsferaDeKi.gdshader` recebem.
///
/// ============================ CADA ESTILO E A LEITURA DE UMA FOLHA, E NAO UM DESENHO NOVO ============================
/// As 42 artes do original nao sao 42 cores do mesmo raio: o Masenko tem a casca em dente de serra,
/// o Static Beam estoura em espinhos e solta descarga, o Makkankosappo e um fio com aneis em volta, o
/// Kamehameha 2 e chapado e sem nucleo, o tiro do Android e um losango. Perder isso seria apagar a
/// unica diferenca visivel entre vinte tecnicas -- e o jogador ESCOLHE uma destas na mesa.
///
/// Entao cada linha abaixo foi escrita olhando a folha de mesmo nome (os quadros `origin`/`tail`/
/// `head` dos raios, o `default` das bolas) e respondendo quatro perguntas: que TAMANHO tem cada
/// bola, a ponta e redonda / em chama / em seta / em coroa, o que ENFEITA o tronco, e quais sao os
/// tres tons do desenho.
///
/// ============================ A COR CONTINUA SENDO `icon += rgb(...)` ============================
/// No BYOND a folha e (quase sempre) CINZA e o jogo SOMA a cor do ki de quem atirou
/// (`beams.dm:130-132`, `blasts.dm:56`). Isso nao mudou: cada estilo guarda os tres tons DA FOLHA --
/// casca, manto e nucleo -- e a <see cref="Rampa.Com"/> soma a tinta em cima, canal a canal, com teto.
/// Por isso o Galick Ho continua roxo na mao de qualquer um (a folha dele ja e roxa) e o Ki Wave e
/// da cor de quem atira (a folha dele e cinza).
///
/// ============================ E AS MEDIDAS QUE SAO REGRA NAO ESTAO AQUI ============================
/// A meia espessura do tronco e a frente da cabeca moram no Core
/// (<see cref="ArteDeProjetil.MeiaEspessuraDoTronco"/>, <see cref="ArteDeProjetil.FrenteDaCabeca"/>),
/// porque o servidor corta e encosta por elas. Este arquivo so diz o que e APARENCIA: quanto do
/// tronco e macico, o tamanho das bolas das pontas, os enfeites e a cor.
/// ==================================================================================================
/// </summary>
public static class ArteDeKiNoCliente
{
	/// <summary>
	/// OS TRES TONS DE UMA ARTE -- casca, manto e nucleo, do mais escuro ao mais claro -- como estavam
	/// na folha do original, ANTES da tinta de quem atira.
	/// </summary>
	public readonly record struct Rampa(Color Borda, Color Manto, Color Nucleo)
	{
		public static Rampa De(string borda, string manto, string nucleo) =>
			new(new Color(borda), new Color(manto), new Color(nucleo));

		/// <summary>
		/// A rampa TINGIDA: o `icon += rgb(blastR, blastG, blastB)` do DM, canal a canal, com teto em 1.
		/// E soma e nao produto pelo motivo que o `Personagem.gdshader` ja pagou: onde o tom e escuro,
		/// multiplicar zera -- e a casca do raio viraria preta em vez de virar a cor do ki.
		/// </summary>
		public (Vector3 Borda, Vector3 Manto, Vector3 Nucleo) Com(Color tinta) =>
			(Somar(Borda, tinta), Somar(Manto, tinta), Somar(Nucleo, tinta));

		private static Vector3 Somar(Color tom, Color tinta) => new(
			Mathf.Min(tom.R + tinta.R, 1f), Mathf.Min(tom.G + tinta.G, 1f), Mathf.Min(tom.B + tinta.B, 1f));
	}

	/// <summary>
	/// O ESTILO DE UM RAIO. Os tamanhos sao em pixel na escala 1; os enfeites vao de 0 a 1 e tem o
	/// mesmo nome do uniform que recebem no `FeixeDeKi.gdshader` (la esta o que cada um desenha).
	/// </summary>
	public sealed record EstiloDeFeixe
	{
		/// <summary>O raio da bola da PONTA. A frente dela cai em `FrenteDaCabeca` -- isso e regra, nao estilo.</summary>
		public float Cabeca { get; init; } = 7f;

		/// <summary>O raio da bola da MAO.</summary>
		public float Boca { get; init; } = 7f;

		/// <summary>
		/// Que fracao da meia espessura da REGRA e tronco macico. 1 na maioria; menos nas artes em que o
		/// que chega ate a beirada e enfeite (descarga, fiapo, anel) e o tronco em si e mais fino.
		/// </summary>
		public float Macico { get; init; } = 1f;

		public float Ponta { get; init; }
		public float Chama { get; init; }
		public float Coroa { get; init; }
		public float Serrilha { get; init; }
		public float Granulado { get; init; }
		public float Fiapos { get; init; }
		public float Aneis { get; init; }
		public float Espiral { get; init; }
		public float Eletrico { get; init; }
		public float Pulsar { get; init; }
		public float Brancura { get; init; } = 0.5f;
		public Rampa Tons { get; init; } = CinzaDeRaio;
	}

	/// <summary>
	/// As formas de fundo de uma bola. **OS NUMEROS SAO OS DO SHADER** (`EsferaDeKi.gdshader`, as
	/// constantes do topo): trocar um aqui sem trocar la desenha a forma vizinha, calado.
	/// </summary>
	public enum Forma
	{
		Orbe = 0,
		Anel = 1,
		Disco = 2,
		Lamina = 3,
		Concha = 4,
		Losango = 5,
		Vortice = 6,
		Faisca = 7,
		Alvo = 8,
	}

	/// <summary>O ESTILO DE UMA BOLA. Mesma convencao do <see cref="EstiloDeFeixe"/>.</summary>
	public sealed record EstiloDeBola
	{
		public Forma Forma { get; init; } = Forma.Orbe;

		/// <summary>O raio, em px na escala 1.</summary>
		public float Raio { get; init; } = 6f;

		/// <summary>O comprimento do rastro, em RAIOS, com o tiro em velocidade cheia. Zero = nao deixa rastro.</summary>
		public float Rastro { get; init; }

		public float Chama { get; init; }
		public float Escuro { get; init; }
		public float Fervura { get; init; }
		public float Calma { get; init; }
		public float Eletrico { get; init; }
		public float Pontilhado { get; init; }
		public float Pontos { get; init; } = 8f;
		public float Aro { get; init; }
		public float Pulsar { get; init; }
		public float Brancura { get; init; } = 0.5f;

		/// <summary>
		/// NAO GIRA COM O RUMO. So o disco: ele e uma serra DEITADA vista de cima, e uma coisa deitada
		/// aparece larga na horizontal da tela pra onde quer que voe (a folha do Kienzan nao tinha
		/// direcao nenhuma, e era por isso).
		/// </summary>
		public bool SemGiro { get; init; }

		public Rampa Tons { get; init; } = CinzaDeBola;
	}

	/// <summary>Os tres cinzas da `Beam3.dmi`, a folha padrao de raio (`beams.dm:4`).</summary>
	private static readonly Rampa CinzaDeRaio = Rampa.De("313131", "808080", "c0c0c0");

	/// <summary>Os tres cinzas da `1.dmi`, a bola padrao (`race.dm:62`).</summary>
	private static readonly Rampa CinzaDeBola = Rampa.De("464646", "696969", "afafaf");

	// =====================================================================
	// OS RAIOS
	// =====================================================================
	private static readonly Dictionary<ArteDeKi, EstiloDeFeixe> Feixes = new()
	{
		// --- Icons/Beams ---
		// `Beam3`: a barra limpa, com as pontas so um pouco mais largas que o tronco.
		[ArteDeKi.Beam3] = new() { Cabeca = 7, Boca = 7, Brancura = 0.62f },

		// `Beam2`: tronco medio e duas bolas em CHAMA rasgada.
		[ArteDeKi.Beam2] = new() { Cabeca = 13, Boca = 13, Chama = 1, Tons = Rampa.De("4d4d4d", "7e7e7e", "d2d2d2") },

		// `Beam4` (Boom Wave): o fio que troca de espessura a cada quadro, com uma bolinha na ponta.
		[ArteDeKi.Beam4] = new() { Cabeca = 5, Boca = 4, Pulsar = 1, Brancura = 0.3f, Tons = Rampa.De("383838", "545454", "777777") },

		// `Beam11` (Raio Colossal): barra branca de contorno, entre duas bolas redondas do dobro da largura.
		[ArteDeKi.Beam11] = new() { Cabeca = 14, Boca = 14.5f, Chama = 0.25f, Brancura = 0.85f, Tons = Rampa.De("000000", "c0c0c0", "ffffff") },

		// `BeamMasenko`: a casca inteira em dente de serra, a mao em ponta de chama.
		[ArteDeKi.BeamMasenko] = new() { Cabeca = 8, Boca = 6, Macico = 0.7f, Serrilha = 1, Chama = 0.5f, Brancura = 0.7f, Tons = Rampa.De("000000", "808080", "f7f7f7") },

		// `BeamStaticBeam`: tronco fino, descargas ate a beirada, a ponta num estouro de espinhos.
		[ArteDeKi.BeamStaticBeam] = new() { Cabeca = 15, Boca = 11, Macico = 0.4f, Coroa = 1, Eletrico = 1, Brancura = 0.8f, Tons = Rampa.De("000000", "c0c0c0", "f8f8f8") },

		// `Beam - Big Fire` (Final Flash): o muro. A folha ja e DOURADA -- e o que o faz amarelo em quem tem ki azul.
		[ArteDeKi.BeamBigFire] = new() { Cabeca = 29, Boca = 27, Chama = 0.55f, Brancura = 0.8f, Tons = Rampa.De("f8b020", "f8d030", "f8f8c0") },

		// `EraserCannon`: a folha quase nao tem tronco -- e uma bola grande com cauda. Verde de fabrica.
		[ArteDeKi.EraserCannon] = new() { Cabeca = 18, Boca = 6, Chama = 0.7f, Brancura = 0.75f, Tons = Rampa.De("00f200", "9dff9d", "d2ffd2") },

		// `Makkankosappo`: um fio com ANEIS correndo em volta e a ponta em broca. Vermelho-laranja de fabrica.
		[ArteDeKi.Makkankosappo] = new() { Cabeca = 5, Boca = 9, Macico = 0.5f, Ponta = 1, Aneis = 1, Chama = 0.6f, Tons = Rampa.De("ff0000", "ff8040", "ffd0a0") },

		// --- Icons/Techniques: os seis do sorteio do Kamehameha (`beams/Kamehameha.dm:15-20`) ---
		[ArteDeKi.Kamehameha1] = new() { Cabeca = 13, Boca = 13, Chama = 1, Brancura = 0.6f, Tons = Rampa.De("12a5ff", "8cf6ff", "ffffff") },
		// o 2 e CHAPADO: duas cores, nenhum branco -- o unico Kamehameha sem nucleo
		[ArteDeKi.Kamehameha2] = new() { Cabeca = 14, Boca = 14, Brancura = 0.12f, Tons = Rampa.De("00ccff", "00ffff", "9affff") },
		[ArteDeKi.Kamehameha3] = new() { Cabeca = 14, Boca = 14, Chama = 0.6f, Granulado = 1, Brancura = 0.3f, Tons = Rampa.De("0099ff", "00ccff", "b0ffff") },
		// o 4 e a CAPSULA: as pontas da largura do tronco, tres faixas lisas
		[ArteDeKi.Kamehameha4] = new() { Cabeca = 13, Boca = 13, Brancura = 0.45f, Tons = Rampa.De("0066ff", "00ccff", "ccffff") },
		[ArteDeKi.Kamehameha5] = new() { Cabeca = 14, Boca = 14, Macico = 0.8f, Fiapos = 1, Brancura = 0.9f, Tons = Rampa.De("2fddff", "2fddff", "ffffff") },
		[ArteDeKi.Kamehameha6] = new() { Cabeca = 14, Boca = 14, Chama = 0.9f, Granulado = 1, Tons = Rampa.De("047ae1", "2ad5ff", "ffffff") },

		// `galacticgun` (Galick Ho): roxo de fabrica, casca pontilhada.
		[ArteDeKi.GalacticGun] = new() { Cabeca = 13, Boca = 13, Chama = 0.8f, Granulado = 0.8f, Serrilha = 0.25f, Brancura = 0.6f, Tons = Rampa.De("9900cc", "cc66ff", "ffe6ff") },

		// `Makkankosappo3` (Death Beam): o fio rosa com uma chama pequena na ponta.
		[ArteDeKi.Makkankosappo3] = new() { Cabeca = 7, Boca = 6, Chama = 0.8f, Tons = Rampa.De("ff66ff", "ff66ff", "ffccff") },

		// `Makkankosappo4`: a linha de contorno escuro, sem nucleo.
		[ArteDeKi.Makkankosappo4] = new() { Cabeca = 5, Boca = 3, Chama = 0.4f, Brancura = 0.1f, Tons = Rampa.De("000000", "545454", "8a8a8a") },

		// `Dodompa`: um tom so, e as duas pontas em SETA.
		[ArteDeKi.Dodompa] = new() { Cabeca = 5.5f, Boca = 4, Ponta = 1, Brancura = 0.1f, Tons = Rampa.De("5a5a5a", "7e7e7e", "a8a8a8") },

		// `Enkumei`: fio branco entre duas chamas em camadas.
		[ArteDeKi.Enkumei] = new() { Cabeca = 14, Boca = 14, Chama = 1, Brancura = 0.55f, Tons = Rampa.De("2a2a2a", "6c6c6c", "ffffff") },
	};

	/// <summary>
	/// O raio sem arte: liso, e MACICO ate a beirada que a regra da pra quem nao tem linha na tabela
	/// (`ArteDeProjetil.MeiaEspessuraDoTronco`, 8 px). Ele ja foi mais fino que a regra (62%), sem enfeite
	/// nenhum cobrindo a diferenca -- o corte e o cruzamento aconteciam 3 px fora do que se via, e a
	/// bancada `--diagartedeki` mediu.
	/// </summary>
	private static readonly EstiloDeFeixe FeixeNeutro = new() { Cabeca = 9, Boca = 8, Brancura = 0.6f };

	// =====================================================================
	// AS BOLAS
	// =====================================================================
	private static readonly Dictionary<ArteDeKi, EstiloDeBola> Bolas = new()
	{
		// --- Icons/Techniques ---
		// `BasenioBlast` (Kill Driver): um ARO oco, amarelo de fabrica.
		[ArteDeKi.BasenioBlast] = new() { Forma = Forma.Anel, Raio = 14, Brancura = 0.6f, Tons = Rampa.De("ffff00", "ffff66", "ffffcc") },

		// `Kikoho`: a concha -- frente redonda, costas em espinho. A folha e escura e meio transparente.
		[ArteDeKi.Kikoho] = new() { Forma = Forma.Concha, Raio = 13, Brancura = 0.35f, Tons = Rampa.De("2a3a3a", "4a6a6a", "9ab0b0") },

		// `Kienzan`: a serra deitada.
		[ArteDeKi.Kienzan] = new() { Forma = Forma.Disco, Raio = 15, SemGiro = true, Tons = Rampa.De("545454", "777777", "a1a1a1") },

		// `KiHead` no estado `paralysis`: um estalo de faiscas e descarga. Amarelado de fabrica.
		[ArteDeKi.KiHead] = new() { Forma = Forma.Faisca, Raio = 9, Eletrico = 1, Brancura = 0.6f, Tons = Rampa.De("707070", "ffff66", "ffff99") },

		// --- Icons/Blasts: as bolas de raca, simples e carregada ---
		[ArteDeKi.Blast1] = new() { Raio = 4.5f, Rastro = 2.2f, Brancura = 0.6f },
		[ArteDeKi.Blast18] = new() { Raio = 6.5f, Rastro = 3.6f, Chama = 0.3f, Brancura = 0.65f, Tons = Rampa.De("464646", "858585", "b6b6b6") },
		[ArteDeKi.Blast5] = new() { Raio = 3.5f, Rastro = 0.8f, Brancura = 0.3f, Tons = Rampa.De("151515", "858585", "858585") },
		[ArteDeKi.Blast6] = new() { Raio = 6, Rastro = 1.4f, Brancura = 0.3f, Tons = Rampa.De("1c1c1c", "5b5b5b", "858585") },
		// o 7 do Yardrat: uma bolinha com outra girando em volta
		[ArteDeKi.Blast7] = new() { Raio = 3.5f, Rastro = 2, Pontilhado = 1, Pontos = 1, Tons = Rampa.De("232323", "4d4d4d", "9a9a9a") },
		[ArteDeKi.Blast8] = new() { Forma = Forma.Losango, Raio = 9, Rastro = 0.8f, Chama = 0.8f, Brancura = 0.35f, Tons = Rampa.De("1c1c1c", "4d4d4d", "858585") },
		// os dois do Android: o losango com o contorno em circuito -- aqui, em descarga
		[ArteDeKi.Blast10] = new() { Forma = Forma.Losango, Raio = 7.5f, Eletrico = 1, Brancura = 0.3f, Tons = Rampa.De("2a2a2a", "4d4d4d", "696969") },
		[ArteDeKi.Blast11] = new() { Forma = Forma.Losango, Raio = 10, Rastro = 0.6f, Eletrico = 1, Brancura = 0.3f, Tons = Rampa.De("2a2a2a", "4d4d4d", "696969") },
		// o 19 do Genie: nao e bola, e um punhado de brilhos
		[ArteDeKi.Blast19] = new() { Forma = Forma.Faisca, Raio = 7, Brancura = 0.7f, Tons = Rampa.De("4d4d4d", "8c8c8c", "afafaf") },
		// o 20 (Tiro Carregado): a bola cheia, de contorno escuro
		[ArteDeKi.Blast20] = new() { Raio = 9, Rastro = 0.9f, Brancura = 0.8f, Tons = Rampa.De("000817", "acabab", "c1cbe3") },
		// o 31 do Ogre: o dardo com um aro em volta
		[ArteDeKi.Blast31] = new() { Raio = 5.5f, Rastro = 2.6f, Aro = 1, Brancura = 0.6f, Tons = Rampa.De("151515", "696969", "afafaf") },
		// o 35: a bola de FOGO -- vermelho, laranja e branco de fabrica
		[ArteDeKi.Blast35] = new() { Raio = 9, Rastro = 1.1f, Chama = 1, Brancura = 0.7f, Tons = Rampa.De("ff0000", "fa9a10", "fdfdfe") },
		// o 28 (Bala Dispersa): a bola que incha e murcha
		[ArteDeKi.Blast28] = new() { Raio = 6.5f, Pulsar = 1, Brancura = 0.25f, Tons = Rampa.De("4d4d4d", "696969", "657e89") },
		// o 30 (Esfera Teleguiada): a bola com um colar de pontos
		[ArteDeKi.Blast30] = new() { Raio = 6, Pontilhado = 1, Pontos = 8, Brancura = 0.6f, Tons = Rampa.De("464646", "777777", "a8a8a8") },
		// o 12 (o padrao da tecnica inventada): o dardo fino
		[ArteDeKi.Blast12] = new() { Raio = 3.5f, Rastro = 3.2f, Brancura = 0.4f, Tons = Rampa.De("151515", "5b5b5b", "8c8c8c") },
		// o 14: o alvo de treino, aneis dentro de aneis -- rosa de fabrica
		[ArteDeKi.Blast14] = new() { Forma = Forma.Alvo, Raio = 14, Brancura = 0.4f, Tons = Rampa.De("ff0000", "ff0066", "ff00ff") },

		// `deathball2017purple2`: a DEATH BALL -- miolo escuro, casca roxa acesa, descargas. A folha tem 98x89.
		[ArteDeKi.DeathBall2017Purple2] = new() { Raio = 36, Escuro = 1, Eletrico = 1, Tons = Rampa.De("700878", "a040d0", "ffffff") },

		// `Blast - Spiraling Ki` (Spirit Gun): miolo branco com bracos em espiral.
		[ArteDeKi.BlastSpiralingKi] = new() { Forma = Forma.Vortice, Raio = 23, Brancura = 0.8f, Tons = Rampa.De("385898", "88d8f8", "f8f8f8") },

		// `Daitoppa`: a meia-lua de ar do Kiai. Palida de fabrica.
		[ArteDeKi.Daitoppa] = new() { Forma = Forma.Lamina, Raio = 14, Brancura = 0.3f, Tons = Rampa.De("8fd0cc", "aee9e7", "d8fffd") },

		// ============================ A GENKIDAMA TEM 96 PX DE RAIO, E NAO OS 150 DA FOLHA ============================
		// `SpiritBomb22017.png` e uma imagem de 450x450 com a bola ocupando uns 300 px de diametro: no
		// BYOND ela e MAIOR que tudo. Aqui a tela mostra 240 px de altura no zoom padrao -- com 150 de
		// raio o jogador nao veria a borda da propria bola. 96 px (tres tiles de raio) ainda e a maior
		// coisa que voa no jogo, e cabe na tela. A escala que o servidor manda (`+0,1` por pulso,
		// `SpiritBomb.dm:119-121`) multiplica isto, como multiplicava a folha.
		// =============================================================================================================
		[ArteDeKi.SpiritBomb] = new() { Raio = 96, Calma = 1, Brancura = 0.8f, Tons = Rampa.De("3a78c8", "9fd8ff", "ffffff") },
	};

	/// <summary>A bola sem arte: lisa, com um rastro curto.</summary>
	private static readonly EstiloDeBola BolaNeutra = new() { Raio = 6, Rastro = 1.5f, Brancura = 0.6f };

	/// <summary>
	/// O ESTILO DE RAIO desta arte.
	///
	/// ARTE DE BOLA NUM RAIO ACONTECE, e e legitimo: a tecnica customizada de RAIO pode escolher
	/// qualquer folha de `Techniques` (`ArteDeProjetil.PermitidasPara`), e la moram o disco e o aro.
	/// O raio sai no feitio neutro, com os TONS da arte escolhida -- quem escolheu o aro amarelo ganha
	/// um raio amarelo.
	/// </summary>
	public static EstiloDeFeixe EstiloDoFeixe(ArteDeKi a)
	{
		if (Feixes.TryGetValue(a, out EstiloDeFeixe? e)) return e;
		return Bolas.TryGetValue(a, out EstiloDeBola? b) ? FeixeNeutro with { Tons = b.Tons } : FeixeNeutro;
	}

	/// <summary>
	/// O ESTILO DE BOLA desta arte.
	///
	/// ARTE DE RAIO NUMA BOLA TAMBEM ACONTECE (o teleguiado customizado escolhe em `Techniques`, onde
	/// moram os Kamehamehas). A bola sai sendo a CABECA daquele raio voando sozinha -- o mesmo tamanho,
	/// a mesma chama, os mesmos tons --, que e literalmente o que a folha desenhava nesse caso.
	/// </summary>
	public static EstiloDeBola EstiloDaBola(ArteDeKi a)
	{
		if (Bolas.TryGetValue(a, out EstiloDeBola? b)) return b;
		if (!Feixes.TryGetValue(a, out EstiloDeFeixe? f)) return BolaNeutra;
		return new EstiloDeBola
		{
			Raio = f.Cabeca, Rastro = 1.2f, Chama = f.Chama, Eletrico = f.Eletrico,
			Brancura = f.Brancura, Tons = f.Tons,
		};
	}

	/// <summary>Esta arte tem estilo PROPRIO (e nao o neutro)? Pra bancada: toda entrada do enum tem que ter.</summary>
	public static bool TemEstilo(ArteDeKi a) => Feixes.ContainsKey(a) || Bolas.ContainsKey(a);

	// =====================================================================
	// A CARGA NA MAO -- os nove desenhos da `BlastCharges.dmi`
	// =====================================================================
	/// <summary>Os tres cinzas da `BlastCharges.dmi`.</summary>
	private static readonly Rampa CinzaDeCarga = Rampa.De("2a2a2a", "545454", "9a9a9a");

	/// <summary>
	/// O BRILHO QUE SE JUNTA NA MAO antes de um raio, pelo `ChargeState` do personagem (1 a 9 -- ver
	/// `ArteDeProjetil.CargaDeRaio`). Sao nove folhas no original e nove estilos aqui, na mesma ordem:
	/// a estrela que pisca, as bolas com colar de pontos, a bolinha, a de quatro pontos, as de
	/// satelite, a grande e o redemoinho.
	/// </summary>
	public static EstiloDeBola EstiloDaCarga(int estado) => estado switch
	{
		1 => new() { Forma = Forma.Faisca, Raio = 6.5f, Brancura = 0.7f, Tons = CinzaDeCarga },
		2 => new() { Raio = 4.5f, Pontilhado = 1, Pontos = 8, Pulsar = 0.6f, Tons = CinzaDeCarga },
		3 => new() { Raio = 4.5f, Pontilhado = 1, Pontos = 6, Pulsar = 0.8f, Brancura = 0.7f, Tons = CinzaDeCarga },
		4 => new() { Raio = 3.5f, Pulsar = 1, Brancura = 0.6f, Tons = CinzaDeCarga },
		5 => new() { Raio = 4, Pontilhado = 1, Pontos = 4, Tons = CinzaDeCarga },
		6 => new() { Raio = 4.5f, Pontilhado = 1, Pontos = 1, Tons = CinzaDeCarga },
		7 => new() { Raio = 5, Pontilhado = 1, Pontos = 2, Brancura = 0.65f, Tons = CinzaDeCarga },
		8 => new() { Raio = 7.5f, Pulsar = 0.5f, Brancura = 0.7f, Tons = CinzaDeCarga },
		_ => new() { Forma = Forma.Vortice, Raio = 6, Brancura = 0.6f, Tons = CinzaDeCarga },
	};

	/// <summary>
	/// O NOME QUE O MENU DA TECNICA CUSTOMIZADA MOSTRA -- o `"[prefix]: [f]"` do `pick_game_icon`
	/// (`customattacks.dm:551`), que e literalmente `"Blasts: 12.dmi"`.
	///
	/// O rotulo e o NOME DO ARQUIVO do original e nao um apelido bonito, e isso e escolha: o catalogo
	/// tem 42 artes e quase nenhuma tem nome de jogo (o `28.dmi` nao se chama nada). Inventar apelidos
	/// seria inventar conteudo, e o original ja resolveu mostrando o arquivo. O desenho mudou; o nome
	/// pelo qual o jogador conhece cada um, nao.
	/// </summary>
	public static string Rotulo(ArteDeKi a)
	{
		(string pasta, string arquivo) = ArteDeProjetil.Folha(a);
		return pasta.Length == 0 ? "(padrao)" : $"{pasta}: {arquivo}{(arquivo.Contains('.') ? "" : ".dmi")}";
	}
}
