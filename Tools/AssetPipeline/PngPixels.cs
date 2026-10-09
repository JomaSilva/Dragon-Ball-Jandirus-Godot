using System.IO.Compression;

namespace Jandirus.Tools;

/// <summary>
/// OS PIXELS DE UMA FOLHA -- pra uma pergunta so: "este quadro e o mesmo desenho naquela outra folha?".
///
/// Existe por causa das folhas de nome repetido (ver <see cref="IconesDoDm"/>): duas `Lab.png` com os
/// mesmos estados, na mesma ordem, e o quadro `ATM` desenhado diferente. Metadado nenhum distingue as
/// duas; so os pixels.
///
/// LE O QUE A ARVORE TEM, E SO: conferido em `Assets/Sprites` (3.628 folhas) -- RGBA de 8 bits (1.326),
/// paleta de 1, 2, 4 e 8 bits (2.297) e RGB de 8 bits (5), nenhuma entrelacada. Qualquer outro formato
/// devolve NULO, e quem chama trata "nao sei comparar" como falta, nunca como "igual".
/// </summary>
internal static class PngPixels
{
	/// <summary>Uma imagem em RGBA de 8 bits, linha a linha.</summary>
	internal sealed record Imagem(int Largura, int Altura, byte[] Rgba);

	private static readonly Dictionary<string, Imagem?> Lidas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>A folha decodificada (com cache por caminho), ou nulo quando o formato nao e um dos tres.</summary>
	internal static Imagem? Ler(string caminho)
	{
		if (Lidas.TryGetValue(caminho, out Imagem? ja)) return ja;
		return Lidas[caminho] = Decodificar(caminho);
	}

	/// <summary>
	/// Os dois quadros tem os MESMOS pixels? Nulo = nao deu pra saber (folha ilegivel, quadro fora da
	/// imagem). Pixel totalmente transparente e igual a outro totalmente transparente, seja qual for a
	/// cor que ficou embaixo -- e o que a tela mostra.
	/// </summary>
	internal static bool? QuadrosIguais(string pngA, int xA, int yA, string pngB, int xB, int yB, int largura, int altura)
	{
		if (Ler(pngA) is not { } a || Ler(pngB) is not { } b) return null;
		if (xA + largura > a.Largura || yA + altura > a.Altura || xB + largura > b.Largura || yB + altura > b.Altura) return null;

		for (int y = 0; y < altura; y++)
			for (int x = 0; x < largura; x++)
			{
				int ia = ((yA + y) * a.Largura + xA + x) * 4, ib = ((yB + y) * b.Largura + xB + x) * 4;
				if (a.Rgba[ia + 3] == 0 && b.Rgba[ib + 3] == 0) continue;
				if (a.Rgba[ia] != b.Rgba[ib] || a.Rgba[ia + 1] != b.Rgba[ib + 1]
					|| a.Rgba[ia + 2] != b.Rgba[ib + 2] || a.Rgba[ia + 3] != b.Rgba[ib + 3]) return false;
			}
		return true;
	}

	private static Imagem? Decodificar(string caminho)
	{
		byte[] b;
		try { b = File.ReadAllBytes(caminho); }
		catch (IOException) { return null; }
		if (b.Length < 33 || b[0] != 0x89 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return null;

		int w = 0, h = 0, bits = 0, tipo = -1, entrelace = 0;
		byte[]? paleta = null, alfaDaPaleta = null;
		using var idat = new MemoryStream();

		for (int p = 8; p + 8 <= b.Length;)
		{
			int tam = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
			int dados = p + 8;
			if (tam < 0 || dados + tam > b.Length) return null;
			switch ((char)b[p + 4], (char)b[p + 5], (char)b[p + 6], (char)b[p + 7])
			{
				case ('I', 'H', 'D', 'R'):
					w = (b[dados] << 24) | (b[dados + 1] << 16) | (b[dados + 2] << 8) | b[dados + 3];
					h = (b[dados + 4] << 24) | (b[dados + 5] << 16) | (b[dados + 6] << 8) | b[dados + 7];
					bits = b[dados + 8]; tipo = b[dados + 9]; entrelace = b[dados + 12];
					break;
				case ('P', 'L', 'T', 'E'): paleta = b[dados..(dados + tam)]; break;
				case ('t', 'R', 'N', 'S'): alfaDaPaleta = b[dados..(dados + tam)]; break;
				case ('I', 'D', 'A', 'T'): idat.Write(b, dados, tam); break;
			}
			p = dados + tam + 4;   // + o CRC
		}

		// canais por pixel de cada tipo que a arvore usa: 2 = RGB, 3 = indice na paleta, 6 = RGBA
		int canais = tipo switch { 2 => 3, 3 => 1, 6 => 4, _ => 0 };
		if (canais == 0 || entrelace != 0 || w <= 0 || h <= 0) return null;
		if (tipo == 3 ? (bits is not (1 or 2 or 4 or 8) || paleta == null) : bits != 8) return null;

		int porLinha = (w * canais * bits + 7) / 8, passo = Math.Max(1, canais * bits / 8);
		var cru = new byte[(porLinha + 1) * h];
		idat.Position = 0;
		using (var z = new ZLibStream(idat, CompressionMode.Decompress))
		{
			int lido = 0;
			while (lido < cru.Length)
			{
				int n = z.Read(cru, lido, cru.Length - lido);
				if (n <= 0) return null;
				lido += n;
			}
		}

		// DESFILTRAGEM: cada linha traz um byte de filtro, e os filtros olham o byte a esquerda (a),
		// o de cima (c) e o da diagonal (d) -- em passos do tamanho de um pixel.
		var linhas = new byte[porLinha * h];
		for (int y = 0; y < h; y++)
		{
			int filtro = cru[y * (porLinha + 1)], de = y * (porLinha + 1) + 1, pra = y * porLinha, acima = pra - porLinha;
			for (int x = 0; x < porLinha; x++)
			{
				int a = x >= passo ? linhas[pra + x - passo] : 0;
				int c = y > 0 ? linhas[acima + x] : 0;
				int d = y > 0 && x >= passo ? linhas[acima + x - passo] : 0;
				int soma = filtro switch
				{
					0 => 0,
					1 => a,
					2 => c,
					3 => (a + c) / 2,
					4 => Paeth(a, c, d),
					_ => -1,
				};
				if (soma < 0) return null;
				linhas[pra + x] = (byte)(cru[de + x] + soma);
			}
		}

		var rgba = new byte[w * h * 4];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				int o = (y * w + x) * 4, l = y * porLinha;
				if (tipo == 6) Array.Copy(linhas, l + x * 4, rgba, o, 4);
				else if (tipo == 2)
				{
					Array.Copy(linhas, l + x * 3, rgba, o, 3);
					rgba[o + 3] = 255;
				}
				else
				{
					int bit = x * bits, indice = (linhas[l + bit / 8] >> (8 - bits - bit % 8)) & ((1 << bits) - 1);
					if (indice * 3 + 2 >= paleta!.Length) return null;
					Array.Copy(paleta, indice * 3, rgba, o, 3);
					rgba[o + 3] = alfaDaPaleta != null && indice < alfaDaPaleta.Length ? alfaDaPaleta[indice] : (byte)255;
				}
			}
		return new Imagem(w, h, rgba);
	}

	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
