namespace Jandirus.Core.World;

/// <summary>
/// O toco de UMA arvore, derivado da arte dela: os pixels (RGBA, linha a linha) e onde eles ficam dentro do recorte
/// da arvore. Quem desenha poe o toco na mesma posicao em que esses pixels estavam -- a raiz do toco e a raiz da
/// arvore, pixel por pixel.
/// </summary>
public sealed class TocoDerivado
{
	public required byte[] Rgba { get; init; }
	public required int Largura { get; init; }
	public required int Altura { get; init; }

	/// <summary>O canto de cima-esquerda do toco DENTRO do recorte da arvore, em pixels (pode passar um pixel da borda).</summary>
	public required int X { get; init; }
	public required int Y { get; init; }

	/// <summary>A arvore nao tem tronco a vista (a copa vai ate o chao, como no pinheiro): o toco e de casca padrao.</summary>
	public required bool Sintetico { get; init; }

	/// <summary>Quantas linhas acima da base da arvore passa o corte.</summary>
	public required int AlturaDoCorte { get; init; }

	/// <summary>A largura do tronco na linha do corte -- e a largura do topo cortado.</summary>
	public required int LarguraDoTopo { get; init; }
}

/// <summary>
/// O TOCO SAI DA ARTE DA PROPRIA ARVORE -- o pedido do dono de 2026-10-10: *"sim seria bom um sprite"* de toco no
/// lugar da arvore caida.
///
/// ============================ POR QUE DERIVAR, E NAO USAR UM TOCO PRONTO ============================
/// Ha toco pronto na arte que ja temos, e eu olhei: o `stump` do `turfs.dmi` do DM (um toco oco e queimado, 32x32,
/// ja no tileset) e o `Small_Stump` do `celianna_TileB2_Medium.dmi` do DU. Sao UM desenho cada, marrom, de um
/// tamanho so. As arvores em uso sao 27 tiles, de 32x32 a 180x180: carvalho de tronco de 40 px, palmeira torta,
/// a branca congelada de Icer, a de cristal azul, a bola de Namek num talo de 3 px. Um toco so nao bate com nenhuma
/// das que nao sao marrons e medias -- e o toco tem de bater com a arvore que caiu.
///
/// Entao o toco e a BASE DA PROPRIA ARVORE (raiz, tronco, o tufo de chao que a arte traz) cortada, com o topo do
/// corte desenhado por cima: uma elipse da cor da casca clareada, com borda e, se couber, um anel. A cor, a largura
/// e a posicao saem da arte -- nao ha PNG novo, nem lista de arvores pra manter.
/// ====================================================================================================
///
/// ============================ COMO SE LE UMA ARVORE ============================
/// De baixo pra cima, pelo FIO: a corrida de pixels solidos que sobe encostada na de baixo, a partir da base.
/// W(k) e a largura do fio na linha k acima da base. Medido nas 27 artes em uso (2026-10-10):
///
///   * A COPA e a regiao larga de cima: a que contem a linha mais larga dos dois tercos de cima do fio, descendo
///     enquanto W >= 40% da largura total. Vir DE CIMA e o que separa a copa do tufo de chao das arvores de um
///     tile, que e tao largo quanto ela.
///   * COPA QUE COMECA NO CHAO (o pinheiro): nao ha tronco a vista. O toco e sintetico, de casca padrao.
///   * BOJO NO PE (raiz aberta, tufo de chao) com um estreitamento logo acima: o corte entra no estreito -- e nunca
///     passa de <see cref="TetoDoToco"/> vezes a altura-alvo, senao o carvalho e a palmeira virariam poste.
///   * TRONCO RETO (o talo de Namek, a palmeira): o corte vai na altura-alvo, limitada pela largura do tronco.
///
/// O QUE FICA: os pixels solidos LIGADOS ao tronco da linha do corte pra baixo. A copa que pendia ao lado e a
/// sombra da arvore (que a arte traz semitransparente) saem.
/// ===============================================================================
///
/// Funcao pura sobre bytes: nao conhece textura nem tela. Quem le a arte e desenha e o `Client/TocosNaTela.cs`.
/// </summary>
public static class ArteDoToco
{
	/// <summary>O alfa a partir do qual o pixel e madeira ou folha, e nao sombra (as sombras da arte ficam abaixo de 200).</summary>
	public const byte Solido = 200;

	/// <summary>A copa e onde o fio passa desta fracao da largura total da arvore.</summary>
	public const double FracaoDaCopa = 0.40;

	/// <summary>A altura-alvo do toco, em fracao da altura da arvore, e os limites dela em pixels.</summary>
	public const double FracaoDoAlvo = 0.11;
	public const int AlvoMin = 4, AlvoMax = 16;

	/// <summary>Ha estreitamento acima do bojo se o fio chega a esta fracao da largura do bojo.</summary>
	public const double Estreito = 0.75;

	/// <summary>O toco nunca passa disto vezes a altura-alvo, desde que ali o fio ja tenha afinado.</summary>
	public const double TetoDoToco = 1.6;

	/// <summary>A casca de quem nao tem tronco a vista (o pinheiro): um marrom de madeira.</summary>
	private static readonly (int R, int G, int B) CascaPadrao = (112, 76, 48);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): o topo do corte sai sempre da casca PADRAO, e nao da cor do tronco
	/// da arvore -- o toco de madeira no pe da arvore de gelo, que o pedido proibe (*"arvore de neve em Icer nao vira
	/// toco de carvalho"*). Lido pela propria linha que escolhe a casca, em <see cref="Derivar"/>. Falso em jogo.
	/// </summary>
	public static bool CascaPadraoDeTeste;

	/// <summary>A folga do quadro de trabalho em volta do recorte: o topo cortado pode passar um pixel da borda.</summary>
	private const int Margem = 8;

	/// <summary>
	/// O toco desta arvore, ou nulo se o recorte nao tem um pixel solido.
	/// </summary>
	/// <param name="rgba">O recorte da arvore, 4 bytes por pixel, linha a linha de cima pra baixo.</param>
	public static TocoDerivado? Derivar(byte[] rgba, int w, int h)
	{
		if (w <= 0 || h <= 0 || rgba.Length < w * h * 4) return null;

		bool Sol(int x, int y) => rgba[(y * w + x) * 4 + 3] >= Solido;

		// ---- a base, o alto e a largura total do que e solido ----
		int yb = -1, yt = -1, xmin = w, xmax = -1;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (!Sol(x, y)) continue;
				if (yt < 0) yt = y;
				yb = y;
				if (x < xmin) xmin = x;
				if (x > xmax) xmax = x;
			}
		if (yb < 0) return null;
		int altArvore = yb - yt + 1, largTotal = xmax - xmin + 1;

		// ---- o eixo: a mediana dos solidos das tres linhas de baixo ----
		var daBase = new List<int>();
		for (int y = Math.Max(0, yb - 2); y <= yb; y++)
			for (int x = 0; x < w; x++)
				if (Sol(x, y)) daBase.Add(x);
		daBase.Sort();
		int eixo = daBase[daBase.Count / 2];

		// ---- o fio: sobe da base seguindo a corrida que encosta na de baixo ----
		var fio = new List<(int L, int R)>();
		for (int y = yb; y >= yt; y--)
		{
			(int L, int R)? escolhida = null;
			double melhor = double.MaxValue;
			for (int x = 0; x < w; x++)
			{
				if (!Sol(x, y)) continue;
				int l = x;
				while (x + 1 < w && Sol(x + 1, y)) x++;
				int r = x;

				double nota;
				if (fio.Count == 0)
					nota = l <= eixo && eixo <= r ? 0 : Math.Min(Math.Abs(l - eixo), Math.Abs(r - eixo));
				else
				{
					(int al, int ar) = fio[^1];
					if (l > ar + 1 || r < al - 1) continue;   // nao encosta na de baixo
					nota = Math.Abs((l + r) / 2.0 - (al + ar) / 2.0);
				}
				if (nota < melhor) { melhor = nota; escolhida = (l, r); }
			}
			if (escolhida is not { } corrida) break;
			fio.Add(corrida);
		}

		int n = fio.Count;
		int[] W = new int[n];
		for (int k = 0; k < n; k++) W[k] = fio[k].R - fio[k].L + 1;

		// ---- a copa: a regiao larga que contem a linha mais larga dos dois tercos de cima ----
		double limiar = FracaoDaCopa * largTotal;
		int kmax = n / 3;
		for (int k = kmax + 1; k < n; k++)
			if (W[k] > W[kmax]) kmax = k;
		int copa = n;   // o fio nunca alarga (galho seco): tudo e tronco
		if (W[kmax] >= limiar)
		{
			copa = kmax;
			while (copa - 1 >= 0 && W[copa - 1] >= limiar) copa--;
		}

		int alvo = Math.Clamp((int)Math.Round(FracaoDoAlvo * altArvore), AlvoMin, AlvoMax);
		if (copa <= Math.Max(4, alvo / 2)) return Sintetizar(yb, eixo, largTotal, w, h);

		// ---- o bojo e coisa do PE da arvore: so se procura perto da base ----
		int pe = Math.Min(copa, 2 * alvo + 4);
		int kpico = 0;
		for (int k = 1; k < pe; k++)
			if (W[k] > W[kpico]) kpico = k;

		bool haAcima = kpico + 1 < copa;
		int m2 = W[kpico];
		if (haAcima)
		{
			m2 = int.MaxValue;
			for (int k = kpico + 1; k < copa; k++) m2 = Math.Min(m2, W[k]);
		}
		double teto = m2 * 1.3 + 1;
		int kn = copa;
		for (int k = kpico + 1; k < copa; k++)
			if (W[k] <= teto) { kn = k; break; }

		int kcut;
		if (haAcima && m2 <= Estreito * W[kpico] && kn <= pe)
		{
			// HA BOJO E ESTREITAMENTO LOGO ACIMA: o corte entra no estreito.
			int fim = kn;
			while (fim + 1 < copa && W[fim + 1] <= teto) fim++;
			kcut = Math.Max(kn, Math.Min(alvo, fim));
			if (fim - kn <= 2) kcut = fim;   // estreito curtinho (arvore de um tile): corta no alto dele
			int tetoAlt = (int)Math.Round(TetoDoToco * alvo);
			if (kcut > tetoAlt && W[tetoAlt] <= 0.8 * W[kpico]) kcut = tetoAlt;   // ...e nunca um poste
		}
		else
		{
			// TRONCO RETO: a altura-alvo, sem passar de uma vez e meia a largura dele.
			int wt = W[Math.Min(alvo, copa - 1)];
			kcut = Math.Min(Math.Min(alvo, Math.Max(AlvoMin, (int)Math.Round(1.5 * wt) + 2)), copa - 1);
		}
		kcut = Math.Max(1, Math.Min(kcut, copa - 1));

		int ycut = yb - kcut;
		(int tl, int tr) = fio[kcut];

		// ---- o que fica: os solidos LIGADOS ao tronco, da linha do corte pra baixo ----
		var quadro = new Quadro(w, h);
		var fica = new bool[w * h];
		var pilha = new Stack<(int X, int Y)>();
		for (int x = tl; x <= tr; x++) pilha.Push((x, ycut));
		while (pilha.Count > 0)
		{
			(int x, int y) = pilha.Pop();
			if (x < 0 || x >= w || y < ycut || y >= h || fica[y * w + x] || !Sol(x, y)) continue;
			fica[y * w + x] = true;
			pilha.Push((x + 1, y));
			pilha.Push((x - 1, y));
			pilha.Push((x, y + 1));
			pilha.Push((x, y - 1));
		}

		// ...e a borda suave colada neles (o antialias da arte e o pe da sombra)
		for (int y = ycut; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (!fica[y * w + x]) continue;
				quadro.Por(x, y, rgba[(y * w + x) * 4], rgba[(y * w + x) * 4 + 1], rgba[(y * w + x) * 4 + 2], rgba[(y * w + x) * 4 + 3]);
				for (int dx = -1; dx <= 1; dx++)
					for (int dy = 0; dy <= 1; dy++)
					{
						int qx = x + dx, qy = y + dy;
						if (qx < 0 || qx >= w || qy >= h || fica[qy * w + qx]) continue;
						int i = (qy * w + qx) * 4;
						if (rgba[i + 3] > 0 && rgba[i + 3] < Solido) quadro.Por(qx, qy, rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
					}
			}

		// ---- a cor da casca: a media do fio na linha do corte e nas tres de baixo ----
		long sr = 0, sg = 0, sb = 0;
		int cnt = 0;
		for (int k = Math.Max(0, kcut - 3); k <= kcut; k++)
		{
			int a = Math.Max(fio[k].L, tl), b = Math.Min(fio[k].R, tr);
			for (int x = a; x <= b; x++)
			{
				int i = ((yb - k) * w + x) * 4;
				sr += rgba[i];
				sg += rgba[i + 1];
				sb += rgba[i + 2];
				cnt++;
			}
		}
		cnt = Math.Max(cnt, 1);
		(int R, int G, int B) casca = CascaPadraoDeTeste ? CascaPadrao : ((int)(sr / cnt), (int)(sg / cnt), (int)(sb / cnt));

		PorOTopo(quadro, ycut, tl, tr, casca);
		return quadro.Fechar(sintetico: false, kcut, tr - tl + 1);
	}

	/// <summary>
	/// ARVORE SEM TRONCO A VISTA (a copa vai ate o chao): um toco de casca padrao, no pe da arvore, com a largura
	/// proporcional a saia dela.
	/// </summary>
	private static TocoDerivado? Sintetizar(int yb, int eixo, int largTotal, int w, int h)
	{
		int wt = Math.Clamp((int)Math.Round(largTotal * 0.16), 6, 12);
		int alt = Math.Clamp(wt / 2 + 3, AlvoMin, 9);
		int l = eixo - wt / 2, r = l + wt - 1;
		int ycut = yb - alt + 1;

		(int R, int G, int B) escura = Misturar(CascaPadrao, (20, 12, 8), 0.40), clara = Misturar(CascaPadrao, (255, 240, 210), 0.18);
		var quadro = new Quadro(w, h);
		for (int y = ycut; y <= yb; y++)
		{
			int folga = y >= yb - 1 ? 1 : 0;   // a raiz abre um pixel pra cada lado
			for (int x = l - folga; x <= r + folga; x++)
			{
				(int R, int G, int B) c = x == l - folga || x == r + folga ? escura : Mod(x - l, 3) == 1 ? clara : CascaPadrao;
				quadro.Por(x, y, (byte)c.R, (byte)c.G, (byte)c.B, 255);
			}
		}

		PorOTopo(quadro, ycut, l, r, CascaPadrao);
		return quadro.Fechar(sintetico: true, alt - 1, wt);
	}

	/// <summary>
	/// O TOPO DO CORTE: uma elipse deitada sobre a linha do corte, da largura do tronco ali. Miolo da cor da casca
	/// clareada, borda escura, e um anel a meio raio quando o tronco tem pelo menos 8 pixels.
	/// </summary>
	private static void PorOTopo(Quadro quadro, int ycut, int l, int r, (int R, int G, int B) casca)
	{
		(int R, int G, int B) topo = Misturar(casca, (255, 244, 214), 0.62);
		(int R, int G, int B) anel = Misturar(casca, topo, 0.50);
		(int R, int G, int B) borda = Misturar(casca, (24, 14, 8), 0.38);

		int wt = r - l + 1;
		double cx = (l + r) / 2.0, rx = wt / 2.0;
		int ry = Math.Clamp((int)Math.Round(wt * 0.30), 1, 6);

		bool Dentro(int x, int y)
		{
			double dx = (x - cx) / (rx + 0.01), dy = (y - ycut) / (ry + 0.01);
			return dx * dx + dy * dy <= 1.0;
		}

		for (int y = ycut - ry - 1; y <= ycut + ry + 1; y++)
			for (int x = l - 1; x <= r + 1; x++)
			{
				if (!Dentro(x, y)) continue;
				bool beira = !Dentro(x + 1, y) || !Dentro(x - 1, y) || !Dentro(x, y + 1) || !Dentro(x, y - 1);
				(int R, int G, int B) c = beira ? borda : topo;
				if (!beira && wt >= 8)
				{
					double ax = (x - cx) / (rx * 0.55), ay = (y - ycut) / (ry * 0.55);
					double d = ax * ax + ay * ay;
					if (d >= 0.55 && d <= 1.25) c = anel;
				}
				quadro.Por(x, y, (byte)c.R, (byte)c.G, (byte)c.B, 255);
			}
	}

	private static (int R, int G, int B) Misturar((int R, int G, int B) a, (int R, int G, int B) b, double t) =>
		((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

	private static int Mod(int a, int m) => ((a % m) + m) % m;

	/// <summary>O quadro de trabalho: o recorte da arvore com uma margem em volta, e a caixa do que foi desenhado.</summary>
	private sealed class Quadro(int w, int h)
	{
		private readonly int _lw = w + 2 * Margem, _lh = h + 2 * Margem;
		private readonly byte[] _px = new byte[(w + 2 * Margem) * (h + 2 * Margem) * 4];
		private int _x0 = int.MaxValue, _y0 = int.MaxValue, _x1 = int.MinValue, _y1 = int.MinValue;

		public void Por(int x, int y, byte r, byte g, byte b, byte a)
		{
			int qx = x + Margem, qy = y + Margem;
			if (qx < 0 || qy < 0 || qx >= _lw || qy >= _lh) return;
			int i = (qy * _lw + qx) * 4;
			_px[i] = r;
			_px[i + 1] = g;
			_px[i + 2] = b;
			_px[i + 3] = a;
			if (qx < _x0) _x0 = qx;
			if (qx > _x1) _x1 = qx;
			if (qy < _y0) _y0 = qy;
			if (qy > _y1) _y1 = qy;
		}

		public TocoDerivado? Fechar(bool sintetico, int alturaDoCorte, int larguraDoTopo)
		{
			if (_x1 < _x0) return null;
			int lw = _x1 - _x0 + 1, lh = _y1 - _y0 + 1;
			var saida = new byte[lw * lh * 4];
			for (int y = 0; y < lh; y++)
				Array.Copy(_px, ((_y0 + y) * _lw + _x0) * 4, saida, y * lw * 4, lw * 4);
			return new TocoDerivado
			{
				Rgba = saida, Largura = lw, Altura = lh, X = _x0 - Margem, Y = _y0 - Margem,
				Sintetico = sintetico, AlturaDoCorte = alturaDoCorte, LarguraDoTopo = larguraDoTopo,
			};
		}
	}
}
