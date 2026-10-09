using Godot;
using Jandirus.Core.Combat;
using static Jandirus.Client.ArteDeKiNoCliente;

namespace Jandirus.Client;

/// <summary>
/// COMO SE PINTA KI COM SHADER -- o material de cada estilo e a geometria que o carrega.
///
/// ============================ POR QUE ISTO NAO MORA NO `ProjetilDesenhado` ============================
/// O `ProjetilDesenhado` e um ESPELHO do servidor: ele sabe onde o tiro esta, pra onde vai e quem o
/// alimenta. Como se transforma "um Masenko de escala 2" em uniforms e triangulos e outra pergunta, e
/// ela tem CINCO fregueses: o tiro que voa, a carga na mao (<see cref="CargaDeRaioVisual"/>), a
/// miniatura da mesa de tecnicas (<see cref="AmostraDeKi"/>), a estrela do embate
/// (<see cref="ChoqueDeKi"/>) e o estouro (<see cref="EstouroDeKi"/>). Escrita dentro do tiro, as
/// outras quatro teriam que fingir que sao tiro.
///
/// ============================ A GEOMETRIA LEVA O UV EM PIXEL, E O SHADER NAO SABE DE ANGULO ============================
/// Os quatro shaders (`FeixeDeKi`, `EsferaDeKi`, `ChoqueDeKi`, `EstouroDeKi`) nao recebem direcao nenhuma. O que eles
/// leem e o `UV`, e quem o escreve e este arquivo, em PIXELS:
///
///   * numa FITA (o raio), `UV.x` e quanto ja se andou desde a mao e `UV.y` e a distancia ao eixo;
///   * num QUADRO (a bola, o choque, o estouro), `UV` e a posicao a partir do centro, com `+x` no rumo.
///
/// E por isso que um raio a 17 graus sai igual a um na horizontal: o que gira e a geometria, e o
/// desenho acontece num espaco onde o feixe esta sempre deitado. Era exatamente o contrario do
/// desenho antigo, que carimbava folhas SEM girar ao longo do eixo e escorregava de lado a cada
/// carimbo -- a escada que o dono viu no trailer (*"o beam fica todo torto... aquela coisa picotada"*).
///
/// ============================ E A FITA TEM FOLGA, E A FOLGA E CALCULADA ============================
/// O shader pinta halo ALEM da casca, e labareda alem da bola. Se a fita acabar antes do desenho, o
/// que aparece e a borda reta da geometria -- um retangulo claro em volta do raio. Cada funcao de
/// "meia" abaixo soma tudo o que o estilo pode pintar de lado; o alcance do halo e escrito daqui pro
/// shader (`alcance_do_halo`) justamente pra os dois lados nao terem como discordar.
/// ====================================================================================================
/// </summary>
public static class PintorDeKi
{
	public const string ShaderDoFeixe = "res://Assets/Shaders/FeixeDeKi.gdshader";
	public const string ShaderDaEsfera = "res://Assets/Shaders/EsferaDeKi.gdshader";
	public const string ShaderDoChoque = "res://Assets/Shaders/ChoqueDeKi.gdshader";
	public const string ShaderDoEstouro = "res://Assets/Shaders/EstouroDeKi.gdshader";

	/// <summary>Um pixel de sobra em volta de tudo, pro anti-serrilhado da casca nao encostar na borda.</summary>
	private const float Sobra = 2f;

	// =====================================================================
	// AS CAMADAS -- quem desenha por cima de quem
	// =====================================================================
	/// <summary>
	/// O `ZIndex` DE UM TIRO. Acima dos corpos (0), e UM DEGRAU ACIMA DA POEIRA (1).
	///
	/// ============================ ERA 1, E A POEIRA SUJAVA O FEIXE ============================
	/// Sobre os corpos ele sempre esteve, e a razao esta no `ProjetilDesenhado._Ready` (o `KI_PLANE` do
	/// DM). Mas 1 e tambem a camada da `PoeiraDeEstrago`, e entre iguais quem decide e o Y -- ou seja, o
	/// acaso. Num embate o chao racha DEBAIXO dos feixes a cada segundo, e cada celula que cai solta uma
	/// nuvem da cor dela: na foto do jogo o miolo branco do raio aparecia com manchas cinza-esverdeadas
	/// (grama moida, 34% de alfa) que pareciam buracos no feixe. Foi olhando a foto ampliada que se viu
	/// que eram nuvens, e nao o shader.
	///
	/// Energia brilha ATRAVES de poeira; e a poeira continua acima dos corpos, que e onde o dono a quer.
	/// ==========================================================================================
	/// </summary>
	public const int CamadaDoTiro = 2;

	/// <summary>A estrela do embate TAPA o lugar onde as duas pontas se encontram: um acima dos tiros.</summary>
	public const int CamadaDoChoque = CamadaDoTiro + 1;

	/// <summary>Quando algo estoura, e isso que se ve: acima dos tiros e da estrela do embate.</summary>
	public const int CamadaDoEstouro = CamadaDoChoque + 1;

	/// <summary>
	/// BANCADA: a semente de TODO material feito daqui pra frente (nulo = sorteada, que e o jogo). O ruido
	/// de cada tiro nasce de uma semente sorteada pra dois raios iguais nao pulsarem juntos -- e por isso
	/// duas fotos do MESMO tiro nunca saem iguais. Uma bancada que quer comparar desenhos crava isto (e o
	/// `ProjetilDesenhado.TempoDeTeste`) e passa a comparar so o que ela mudou.
	/// </summary>
	public static float? SementeDeTeste;

	private static float Semente() => SementeDeTeste ?? GD.Randf() * 10f;

	/// <summary>
	/// AS MEDIDAS DE UM RAIO, em pixel de mundo, ja na escala do tiro. As duas primeiras sao a REGRA
	/// (Core) e as outras tres sao o estilo -- juntas aqui porque o desenho precisa das cinco.
	/// </summary>
	/// <param name="Alcance">A meia espessura da regra: ate onde o feixe e desenhado de lado do eixo.</param>
	/// <param name="Frente">Quanto a ponta avanca alem da posicao do tiro.</param>
	/// <param name="Raio">A meia espessura do tronco MACICO.</param>
	public readonly record struct MedidasDoFeixe(float Alcance, float Frente, float Raio, float Cabeca, float Boca)
	{
		/// <summary>O alcance do halo alem da casca. A MESMA conta vai pro shader como `alcance_do_halo`.</summary>
		public float Halo => 7f + Raio * 1.1f;
	}

	public static MedidasDoFeixe Medir(ArteDeKi arte, float escala)
	{
		EstiloDeFeixe e = EstiloDoFeixe(arte);
		float alcance = ArteDeProjetil.MeiaEspessuraDoTronco(arte) * escala;
		float macico = ArteDeProjetil.MeiaEspessuraDoTronco(arte) * e.Macico;   // na escala 1: o bojo e da ARTE
		return new MedidasDoFeixe(
			alcance, Feixe.AlcanceDaCabeca(arte, escala), alcance * e.Macico,
			Mathf.Max(e.Cabeca, macico * Bojo(BojoDaCabeca, macico)) * escala,
			Mathf.Max(e.Boca, macico * Bojo(BojoDaBoca, macico)) * escala);
	}

	/// <summary>
	/// O BOJO DAS PONTAS: quantas vezes o tronco a bola da MAO e a da CABECA tem que ter, no minimo.
	///
	/// ============================ O PEDIDO, E AS ARTES QUE NAO O CUMPRIAM ============================
	/// O dono (2026-10-07): *"ele ser maior quando sai da mao do personagem, ai ele afina ate chega na
	/// cabeca do beam onde cresce dnv"*. A maioria das folhas ja era assim (o Kamehameha 1 tem bolas de
	/// 13 px num tronco de 5). Mas algumas tem a ponta DA LARGURA DO TRONCO ou menor -- a capsula do
	/// Kamehameha 4 (13 e 13), a mao do Masenko (6 num tronco de 5,6), a do Eraser Cannon (6 em 8) -- e
	/// nessas nao ha de onde afinar. O bojo e o piso: se a folha ja da mais, vale a folha.
	///
	/// A da mao e maior que a da cabeca porque o pedido comeca por ela ("maior quando SAI DA MAO").
	///
	/// ============================ E O MURO NAO GANHA BOJO ============================
	/// O piso vale cheio ate 10 px de tronco e some aos 20. O Final Flash (27 px, atirado a 4x: 216 px
	/// de espessura numa tela de 240) ja e a maior coisa em cena -- com uma vez e meia disso na mao ele
	/// teria uma esfera de 324 px, maior que a tela. O que cresce nas pontas dele e o leque.
	/// =================================================================================
	/// </summary>
	private const float BojoDaBoca = 1.5f, BojoDaCabeca = 1.4f;

	private const float TroncoDeBojoCheio = 10f, TroncoSemBojo = 20f;

	private static float Bojo(float cheio, float macicoNaEscala1) =>
		1f + (cheio - 1f) * Mathf.Clamp((TroncoSemBojo - macicoNaEscala1) / (TroncoSemBojo - TroncoDeBojoCheio), 0f, 1f);

	/// <summary>
	/// O LEQUE DESTE RAIO (o uniform `labareda`): o do estilo, ABAFADO pelo tamanho da explosao.
	///
	/// O leque e medido em raios de bola, e proporcional puro nao serve nas pontas da escala: o Final
	/// Flash a 4x tem uma cabeca de 116 px, e um leque de 2,4 raios dela seriam 280 px de fogo pra cada
	/// lado. Entao a explosao de uma bola grande cresce MENOS que a bola: com 13 px (um Kamehameha)
	/// sobram 88% do leque, com 116 sobram 46%.
	/// </summary>
	public static float Labareda(EstiloDeFeixe e, MedidasDoFeixe m) =>
		e.Labareda / (1f + Mathf.Max(m.Cabeca, m.Boca) / BolaQueAbafaOLeque);

	private const float BolaQueAbafaOLeque = 100f;

	/// <summary>
	/// O MATERIAL DE UM RAIO desta arte, nesta escala, com a cor de ki de quem atirou. Um por tiro: os
	/// uniforms do instante (comprimento, tempo, solto) sao de cada um.
	/// </summary>
	public static ShaderMaterial? MaterialDeFeixe(ArteDeKi arte, float escala, Color cor)
	{
		if (ResourceLoader.Load<Shader>(ShaderDoFeixe) is not { } sh) return null;

		EstiloDeFeixe e = EstiloDoFeixe(arte);
		var mat = new ShaderMaterial { Shader = sh };
		EscalarFeixe(mat, e, Medir(arte, escala));
		mat.SetShaderParameter("onda", e.Onda);
		mat.SetShaderParameter("ponta", e.Ponta);
		mat.SetShaderParameter("coroa", e.Coroa);
		mat.SetShaderParameter("serrilha", e.Serrilha);
		mat.SetShaderParameter("granulado", e.Granulado);
		mat.SetShaderParameter("fiapos", e.Fiapos);
		mat.SetShaderParameter("aneis", e.Aneis);
		mat.SetShaderParameter("eletrico", e.Eletrico);
		mat.SetShaderParameter("pulsar", e.Pulsar);
		mat.SetShaderParameter("brancura", e.Brancura);
		mat.SetShaderParameter("semente", Semente());
		TingirFeixe(mat, e.Tons, cor);
		return mat;
	}

	/// <summary>
	/// ESCREVE AS MEDIDAS DE UM RAIO NO MATERIAL DELE -- tudo o que, no shader, depende do TAMANHO. E a parte do
	/// <see cref="MaterialDeFeixe"/> que o raio vivo repete quando engrossa com o poder de quem o segura (dono,
	/// 2026-10-08; `ProjetilDesenhado.Reescalar`): o material, a semente do ruido e as cores continuam os mesmos,
	/// e por isso o raio CRESCE em vez de piscar pra um desenho novo.
	/// </summary>
	public static void EscalarFeixe(ShaderMaterial mat, EstiloDeFeixe e, MedidasDoFeixe m)
	{
		mat.SetShaderParameter("raio", m.Raio);
		mat.SetShaderParameter("raio_cabeca", m.Cabeca);
		mat.SetShaderParameter("raio_boca", m.Boca);
		mat.SetShaderParameter("alcance", m.Alcance);
		mat.SetShaderParameter("alcance_do_halo", m.Halo);
		mat.SetShaderParameter("labareda", Labareda(e, m));
	}

	/// <summary>O MATERIAL DE UMA BOLA deste estilo. `raio` ja vem na escala do tiro.</summary>
	public static ShaderMaterial? MaterialDeBola(EstiloDeBola e, float raio, Color cor)
	{
		if (ResourceLoader.Load<Shader>(ShaderDaEsfera) is not { } sh) return null;

		var mat = new ShaderMaterial { Shader = sh };
		mat.SetShaderParameter("forma", (int)e.Forma);
		mat.SetShaderParameter("raio", raio);
		mat.SetShaderParameter("alcance_do_halo", HaloDaBola(e, raio));
		mat.SetShaderParameter("chama", e.Chama);
		mat.SetShaderParameter("escuro", e.Escuro);
		mat.SetShaderParameter("fervura", e.Fervura);
		mat.SetShaderParameter("calma", e.Calma);
		mat.SetShaderParameter("eletrico", e.Eletrico);
		mat.SetShaderParameter("pontilhado", e.Pontilhado);
		mat.SetShaderParameter("pontos", e.Pontos);
		mat.SetShaderParameter("aro", e.Aro);
		mat.SetShaderParameter("pulsar", e.Pulsar);
		mat.SetShaderParameter("brancura", e.Brancura);
		mat.SetShaderParameter("semente", Semente());
		Tingir(mat, e.Tons, cor);
		return mat;
	}

	/// <summary>
	/// ESCREVE AS TRES CORES -- os tons da arte com a tinta do ki somada (ver <see cref="Rampa.Com"/>).
	/// E a unica escrita que a troca de cor ao vivo da mesa de tecnicas precisa repetir numa BOLA.
	/// </summary>
	public static void Tingir(ShaderMaterial mat, Rampa tons, Color cor)
	{
		(Vector3 borda, Vector3 manto, Vector3 nucleo) = tons.Com(cor);
		mat.SetShaderParameter("cor_borda", borda);
		mat.SetShaderParameter("cor_manto", manto);
		mat.SetShaderParameter("cor_nucleo", nucleo);
	}

	/// <summary>
	/// AS CORES DE UM RAIO: as tres de sempre e a QUARTA, a cor forte do leque e das fitas (ver
	/// <see cref="Rampa.Fundo"/>). So o `FeixeDeKi.gdshader` a declara -- por isso a bola nao passa por aqui.
	/// </summary>
	public static void TingirFeixe(ShaderMaterial mat, Rampa tons, Color cor)
	{
		Tingir(mat, tons, cor);
		mat.SetShaderParameter("cor_fundo", tons.Fundo(cor));
	}

	/// <summary>O alcance do halo de uma bola. A Genkidama (`calma`) brilha mais longe: e quase toda luz.</summary>
	private static float HaloDaBola(EstiloDeBola e, float raio) => (5f + raio * 0.9f) * (1f + 0.5f * e.Calma);

	// =====================================================================
	// AS FOLGAS -- ate onde cada estilo pinta
	// =====================================================================
	/// <summary>
	/// A MEIA LARGURA DA FITA de um raio: tudo o que o shader pode pintar de lado do eixo, mais o halo.
	///
	/// **OS NUMEROS SAO OS TETOS DO `FeixeDeKi.gdshader`, UM POR UM** -- trocar um la sem trocar aqui
	/// corta o desenho numa borda reta, calado:
	///
	///   * o LEQUE de cada explosao sobe ate <see cref="AlturaDoLeque"/> raios de bola; a cabeca respira
	///     4% e a boca nasce 51% inchada (o clarao do disparo);
	///   * as FITAS chegam a `raio + afasta` do eixo, mais a meia largura delas (`fio`), e as faiscas
	///     ficam ate 3,6 raios de tronco;
	///   * a serrilha, e os enfeites de cada arte, como sempre.
	///
	/// Sobrar um pouco custa pixel transparente; faltar custa um retangulo visivel.
	/// </summary>
	public static float MeiaFita(EstiloDeFeixe e, MedidasDoFeixe m)
	{
		float leque = AlturaDoLeque(Labareda(e, m));
		float explosao = Mathf.Max(m.Cabeca * 1.04f, m.Boca * 1.51f) * leque;

		float fitas = 0f;
		if (e.Onda > 0f)
		{
			float fio = Mathf.Min(Mathf.Max(m.Raio, 1.3f), 5f + m.Raio * 0.25f);
			float afasta = Mathf.Clamp(m.Raio * 2f, 5f, 11f + m.Raio * 0.3f);
			fitas = Mathf.Max(Mathf.Max(m.Alcance, m.Raio + afasta) + fio * 1.15f, m.Raio * 3.6f);
		}

		float tronco = m.Raio * (1.2f + 0.7f * e.Serrilha);
		float enfeite = e.Aneis + e.Eletrico + e.Fiapos > 0f ? Mathf.Max(m.Alcance, m.Raio * 2f) * 1.45f : 0f;
		return Mathf.Max(Mathf.Max(explosao, fitas), Mathf.Max(tronco, enfeite)) + m.Halo + Sobra;
	}

	/// <summary>
	/// ATE ONDE O LEQUE DE UMA EXPLOSAO SOBE, em raios de bola, de lado do eixo. Sem leque e a propria bola
	/// (o piso de 0,8 e a massa que cabe dentro dela); com ele cheio sao 2,5 -- o maior `massa + espinho`
	/// do shader (1,35 + 1,45) acontece a uns 75 graus do tronco, onde o espinho ja perdeu 15% do
	/// comprimento (`ESPINHO_DE_PE`) e a altura e 96% da distancia ao centro.
	/// </summary>
	public static float AlturaDoLeque(float labareda) => Mathf.Max(1f, 0.8f + 1.72f * labareda);

	/// <summary>
	/// Quanto a fita passa da PONTA: so o halo. A cabeca acaba exatamente na ponta (e regra do servidor --
	/// ver o espinho fixo da `coroa` no shader), e o leque e cortado no plano dela.
	/// </summary>
	public static float FolgaNaPonta(MedidasDoFeixe m) => m.Halo + Sobra;

	/// <summary>
	/// Quanto a fita recua atras da MAO: so o halo. O shader nao pinta corpo atras da mao, e os dois leques
	/// (o da cabeca tambem, que abre pra tras) sao cortados no plano dela.
	/// </summary>
	public static float FolgaNaMao(MedidasDoFeixe m) => m.Halo + Sobra;

	/// <summary>A meia largura do quadro de uma bola: a casca viva, os enfeites em volta e o halo.</summary>
	public static float MeiaDaBola(EstiloDeBola e, float raio)
	{
		float corpo = raio * (1.35f + 0.55f * e.Chama);
		if (e.Forma is Forma.Vortice or Forma.Losango) corpo = Mathf.Max(corpo, raio * 1.4f);
		if (e.Pontilhado > 0f) corpo = Mathf.Max(corpo, raio * 1.95f);
		if (e.Aro > 0f) corpo = Mathf.Max(corpo, raio * 1.75f);
		if (e.Eletrico > 0f) corpo = Mathf.Max(corpo, raio * 1.45f);
		return corpo + HaloDaBola(e, raio) + Sobra;
	}

	// =====================================================================
	// A GEOMETRIA
	// =====================================================================
	private static readonly Color[] Branco = [Colors.White];
	private static readonly int[] DoisTriangulos = [0, 1, 2, 1, 3, 2];

	/// <summary>
	/// UMA FITA RETA de `de` ate `ate`, com o UV em pixel: `x` = 0 em `de` e o comprimento em `ate`.
	/// E a geometria de quase todo raio do jogo -- quatro vertices.
	/// </summary>
	public static void FitaReta(CanvasItem alvo, Vector2 de, Vector2 ate, float meia, float atras, float aFrente)
	{
		Vector2 eixo = ate - de;
		float comp = eixo.Length();
		eixo = comp > 0.001f ? eixo / comp : Vector2.Right;
		var lado = new Vector2(-eixo.Y, eixo.X);
		Vector2 a = de - eixo * atras, b = ate + eixo * aFrente;

		RenderingServer.CanvasItemAddTriangleArray(alvo.GetCanvasItem(), DoisTriangulos,
			[a + lado * meia, a - lado * meia, b + lado * meia, b - lado * meia], Branco,
			[new(-atras, meia), new(-atras, -meia), new(comp + aFrente, meia), new(comp + aFrente, -meia)]);
	}

	/// <summary>
	/// UMA FITA AO LONGO DE UM CAMINHO -- o raio que FAZ CURVA. O `UV.x` e o comprimento percorrido, e
	/// por isso o shader desenha o mesmo feixe de sempre: pra ele o caminho continua sendo uma reta.
	///
	/// A normal de cada degrau e a media das duas pernas vizinhas. Isso so serve pra curva suave (com
	/// o raio de curvatura bem maior que a meia largura a fita nao se dobra sobre si mesma) -- e as
	/// curvas daqui sao suaves por construcao, ver `ProjetilDesenhado.Curva`.
	/// </summary>
	/// <returns>O comprimento do caminho, que o shader precisa receber como `comprimento`.</returns>
	public static float FitaCurva(CanvasItem alvo, Vector2[] caminho, int n, float meia, float atras, float aFrente)
	{
		if (n < 2) return 0f;

		// as duas pontas se estendem pela tangente: e ali que moram as folgas
		int m = n + 2;
		var pts = new Vector2[m * 2];
		var uvs = new Vector2[m * 2];
		var idx = new int[(m - 1) * 6];

		Vector2 Em(int i) => i == 0 ? caminho[0] - (caminho[1] - caminho[0]).Normalized() * atras
						   : i == m - 1 ? caminho[n - 1] + (caminho[n - 1] - caminho[n - 2]).Normalized() * aFrente
						   : caminho[i - 1];

		float andado = -atras, total = 0f;
		for (int i = 0; i < m; i++)
		{
			Vector2 p = Em(i);
			Vector2 tang = i == 0 ? Em(1) - p
						 : i == m - 1 ? p - Em(i - 1)
						 : (Em(i + 1) - p).Normalized() + (p - Em(i - 1)).Normalized();
			tang = tang.LengthSquared() > 1e-8f ? tang.Normalized() : Vector2.Right;
			if (i > 0) andado += p.DistanceTo(Em(i - 1));
			if (i == m - 2) total = andado;

			var lado = new Vector2(-tang.Y, tang.X);
			pts[i * 2] = p + lado * meia;
			pts[i * 2 + 1] = p - lado * meia;
			uvs[i * 2] = new Vector2(andado, meia);
			uvs[i * 2 + 1] = new Vector2(andado, -meia);

			if (i == m - 1) break;
			int a = i * 2, k = i * 6;
			idx[k] = a; idx[k + 1] = a + 1; idx[k + 2] = a + 2;
			idx[k + 3] = a + 1; idx[k + 4] = a + 3; idx[k + 5] = a + 2;
		}

		RenderingServer.CanvasItemAddTriangleArray(alvo.GetCanvasItem(), idx, pts, Branco, uvs);
		return total;
	}

	/// <summary>
	/// UM QUADRO em volta de um ponto, com o UV em pixel a partir dele e `+x` no `eixo`. `paraTras`
	/// estica o quadro pro lado de onde o tiro veio -- e onde mora o rastro de uma bola.
	/// </summary>
	public static void Quadro(CanvasItem alvo, Vector2 centro, Vector2 eixo, float meia, float paraTras = 0f)
	{
		eixo = eixo.LengthSquared() > 1e-8f ? eixo.Normalized() : Vector2.Right;
		var lado = new Vector2(-eixo.Y, eixo.X);
		float x0 = -meia - paraTras;
		Vector2 a = centro + eixo * x0, b = centro + eixo * meia;

		RenderingServer.CanvasItemAddTriangleArray(alvo.GetCanvasItem(), DoisTriangulos,
			[a + lado * meia, a - lado * meia, b + lado * meia, b - lado * meia], Branco,
			[new(x0, meia), new(x0, -meia), new(meia, meia), new(meia, -meia)]);
	}
}
