namespace Jandirus.Core.World;

/// <summary>
/// ============================ O VENTO NA VEGETACAO (o pedido 226, 2026-11-02) ============================
/// O dono: *"vegetaçao dos planetas poderiam balançar (baseando-se no clima) pra simular vento, mas seria a parte superior deles pq se
/// balancar a arvore toda o tronco vai balancar e ficar restranho, entao mire mais na parte das folhas mesma ideia q o terraria usa"*.
///
/// Aqui mora a REGRA, pura: quanto venta com cada clima, quanto a copa balanca e onde fica o PE DA COPA de cada quadro de arvore. A tela
/// e do cliente: o `VentoNaVegetacao` poe um material em cada quadro de vegetacao do tileset, e o shader `VentoNaCopa` verga so as
/// fileiras de cima do pe da copa -- o tronco, embaixo dele, nao mexe.
///
/// O VENTO E SO DESENHO: nada no servidor muda, e nenhuma colisao anda. A arvore continua parada onde o mapa diz.
/// =========================================================================================================
/// </summary>
public static class Vento
{
	/// <summary>A BRISA: o ceu limpo (e o planeta sem clima nenhum) ainda mexe as folhas, de leve. E o vento de 0 a 1.</summary>
	public const double Brisa = 0.15;

	/// <summary>
	/// QUANTO VENTA COM CADA CLIMA NO AUGE, de 0 a 1. A tempestade e a destruicao de planeta no maximo; a nevasca e a tempestade de areia
	/// quase; a chuva no meio; a neblina e mais parada que o ceu limpo (e ar parado que deixa a neblina assentar).
	/// </summary>
	public static double DoTipo(TipoDeClima t) => t switch
	{
		TipoDeClima.Limpo => Brisa,
		TipoDeClima.Nublado => 0.30,
		TipoDeClima.Chuva or TipoDeClima.ChuvaDeSangue or TipoDeClima.ChuvaDeNamek => 0.50,
		TipoDeClima.Tempestade => 1.0,
		TipoDeClima.Neve => 0.25,
		TipoDeClima.Nevasca => 0.90,
		TipoDeClima.Neblina => 0.10,
		TipoDeClima.Fumaca => 0.20,
		TipoDeClima.Areia => 0.80,
		TipoDeClima.Destruicao => 1.0,
		_ => Brisa,
	};

	/// <summary>
	/// O VENTO AGORA: o do clima, pesado pela forca dele a partir da brisa -- o clima que entra e sai devagar (`EstadoDoClima.Forca`)
	/// leva o vento junto, sem tranco.
	/// </summary>
	public static double DoClima(EstadoDoClima c) => Brisa + (DoTipo(c.Tipo) - Brisa) * Math.Clamp(c.Forca, 0, 1);

	/// <summary>
	/// O BALANCO NO ALTO DA COPA, em pixels de arte, pra uma copa de <see cref="CopaDeReferenciaPx"/>: ~1 px na brisa, 4 na tempestade.
	/// O shader escala pelo porte de cada arvore (<see cref="Porte"/>).
	/// </summary>
	public static double AmplitudePx(double vento) => 0.8 + 3.2 * Math.Clamp(vento, 0, 1);

	/// <summary>
	/// O EMPURRAO: o vento forte DEITA a copa pro lado dele (a direita, pra onde as nuvens do `Clima.gdshader` andam), em pixels no alto.
	/// Ao quadrado: a brisa quase nao deita nada, a tempestade deita 2,5 px.
	/// </summary>
	public static double EmpurraoPx(double vento)
	{
		double v = Math.Clamp(vento, 0, 1);
		return 2.5 * v * v;
	}

	/// <summary>A copa que balanca a <see cref="AmplitudePx"/> inteira -- a da arvore do Jardim (`Tree Good`, 68 px).</summary>
	public const double CopaDeReferenciaPx = 64;

	/// <summary>
	/// O PORTE: a copa alta balanca mais pixels que a baixa (a palmeira mais que a muda), entre a metade e o dobro da referencia.
	/// </summary>
	public static double Porte(int alturaDaCopaPx) => Math.Clamp(alturaDaCopaPx / CopaDeReferenciaPx, 0.5, 2.0);

	/// <summary>O alfa (0-255) a partir do qual um pixel da arte conta como desenhado.</summary>
	public const int AlfaOpaco = 40;

	/// <summary>A fileira e da COPA quando tem mais que esta fracao da largura da fileira mais larga da arte; abaixo disso e tronco.</summary>
	public const double FracaoDaCopa = 0.45;

	/// <summary>Tronco mais curto que isto (px) nao e tronco: a moita, a plantacao e o pinheiro que vai ate o chao vergam pela raiz.</summary>
	public const int SemTroncoPx = 3;

	/// <summary>
	/// ============================ O PE DA COPA, MEDIDO NA ARTE ============================
	/// Cada quadro de arvore tem o tronco de uma altura (o pinheiro quase nenhum, a arvore de Namek um poste), entao o ponto onde a copa
	/// comeca nao pode ser um numero so. Ele e lido no proprio desenho: de baixo pra cima, a primeira fileira LARGA (mais que
	/// <see cref="FracaoDaCopa"/> da fileira mais larga) e o pe da copa -- o que esta abaixo dela e o tronco, e nao mexe. Sem tronco
	/// (menos que <see cref="SemTroncoPx"/>), o pe e a raiz: a planta inteira verga presa no chao, como o trigo.
	///
	/// Devolve as fileiras (de 0, o alto do quadro) da primeira desenhada (`Topo`), do pe da copa (`Pivo`) e da ultima desenhada
	/// (`Base`); nulo pra um quadro vazio.
	/// ======================================================================================
	/// </summary>
	/// <param name="opaco">O pixel (x, y) do quadro esta desenhado?</param>
	public static (int Topo, int Pivo, int Base)? MedirCopa(Func<int, int, bool> opaco, int largura, int altura)
	{
		var larg = new int[altura];
		int topo = -1, baseDaArte = -1, maior = 0;
		for (int y = 0; y < altura; y++)
		{
			for (int x = 0; x < largura; x++)
				if (opaco(x, y)) larg[y]++;
			if (larg[y] == 0) continue;
			if (topo < 0) topo = y;
			baseDaArte = y;
			maior = Math.Max(maior, larg[y]);
		}
		if (topo < 0) return null;

		int pe = baseDaArte;
		for (int y = baseDaArte; y >= topo; y--)
			if (larg[y] > FracaoDaCopa * maior) { pe = y; break; }
		if (baseDaArte - pe <= SemTroncoPx) pe = baseDaArte;
		return (topo, pe, baseDaArte);
	}
}
