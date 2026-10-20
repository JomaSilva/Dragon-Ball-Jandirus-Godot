using System.Runtime.CompilerServices;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ O PEDACO QUE O CORTE DE MEMBRO SOLTA DO CORPO (dono, 2026-10-19) ============================
/// O membro arrancado some no `Personagem.gdshader` por FAIXA -- a do braco na lateral do meio do quadro, a da perna no chao --,
/// e as faixas foram medidas no corpo de 32 px de gente. Nas folhas de outra proporcao elas atravessam o que nao e o membro: o
/// rabo do Frost Demon sobe pela lateral e corre pelo chao, e perder a perna (ou o braco do lado do rabo) deixava o resto do rabo
/// FLUTUANDO. Conta de papel nas 49 folhas do Frost, nos quadros parados: pedaco solto em 21 delas, ate 39 pixels (o rabo inteiro
/// do Kuriza, do Frieza, do Kold e do Koola ao perder a perna esquerda de frente); nas folhas de gente, do musculoso, do SSJ4, do
/// Super Perfeito e do Kid Boo, nenhum ou quase nenhum. O dono escolheu: **o pedaco solto some junto com o membro** (e um dia ele
/// desenha a mascara de membro de cada folha, que separa o rabo do braco -- a faixa nao sabe o que e rabo).
///
/// "SOLTO" E CONTA DE VIZINHANCA, que shader nao faz: aqui, no quadro, com a MESMA faixa do shader. Cada pedaco do desenho (pixels
/// opacos ligados, 8-vizinhanca) que o corte parte fica so com a sua parte MAIOR; as outras partes vao pra mascara. Pedaco que o
/// desenho ja trazia separado (um chifre solto, uma bota de roupa) nao e tocado: so conta o que o corte partiu.
///
/// UMA MASCARA POR QUADRO E POR CORTE, guardada pelo processo inteiro: a folha fica presa (`FolhasPresas`) e o quadro e sempre o
/// mesmo objeto, entao cada par (quadro, membros arrancados) e contado uma vez so. A leitura do desenho (`GetImage`) so acontece
/// pra corpo com membro faltando.
/// =================================================================================================================================
/// </summary>
public static class PedacosSoltos
{
	// A FAIXA DO CORTE: os MESMOS numeros do bloco "O QUE FOI ARRANCADO NAO E DESENHADO" do `Personagem.gdshader`. Mudar la e mudar
	// aqui -- esta mascara acha o que o corte DAQUELE bloco solta.
	private const float BracoDe = 0.46f, BracoAte = 0.80f, ColunaDoBraco = 0.165f, PernaDe = 0.795f;

	/// <summary>Opaco pro shader de ferida: o bloco inteiro so roda com `c.a > 0.01`.</summary>
	private const float Opaco = 0.01f;

	private static readonly ConditionalWeakTable<Texture2D, Dictionary<int, ImageTexture?>> _feitas = new();

	/// <summary>
	/// A mascara do que o corte solta neste quadro (R = 1 no pixel que some junto), ou nula quando nada fica solto. <paramref name="braco"/>
	/// e <paramref name="perna"/> sao os pares que o shader recebe, ja no lado da IMAGEM (x = metade esquerda, y = direita);
	/// <paramref name="deitado"/>, o `ferida_deitado` (o quadro de nocaute e um desenho deitado).
	/// </summary>
	public static ImageTexture? Mascara(Texture2D quadro, Vector2 braco, Vector2 perna, bool deitado)
	{
		int corte = (braco.X > 0.5f ? 1 : 0) | (braco.Y > 0.5f ? 2 : 0) | (perna.X > 0.5f ? 4 : 0) | (perna.Y > 0.5f ? 8 : 0);
		if (corte == 0) return null;
		int chave = corte | (deitado ? 16 : 0);
		Dictionary<int, ImageTexture?> porCorte = _feitas.GetOrCreateValue(quadro);
		if (porCorte.TryGetValue(chave, out ImageTexture? pronta)) return pronta;
		ImageTexture? feita = Fazer(quadro, corte, deitado);
		porCorte[chave] = feita;
		return feita;
	}

	private static ImageTexture? Fazer(Texture2D quadro, int corte, bool deitado)
	{
		if (quadro.GetImage() is not { } img || img.IsEmpty()) return null;
		int w = img.GetWidth(), h = img.GetHeight();

		// O MESMO `quv` DO SHADER: o UV do centro do pixel na folha, sobre a caixa que a `BorraoDirecional.Caixa` manda pra ele (do
		// centro do primeiro pixel do quadro ao do ultimo). Quadro que nao e de atlas tem a folha inteira como caixa.
		(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(quadro);
		Vector2 folha = new(w, h), canto = Vector2.Zero;
		if (quadro is AtlasTexture at && at.Atlas != null)
		{
			folha = new Vector2(Math.Max(at.Atlas.GetWidth(), 1), Math.Max(at.Atlas.GetHeight(), 1));
			canto = at.Region.Position;
		}
		Vector2 caixa = new(Math.Max(max.X - min.X, 1e-6f), Math.Max(max.Y - min.Y, 1e-6f));

		var opaco = new bool[w * h];
		var fica = new bool[w * h];
		var cortado = new bool[w * h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				int i = y * w + x;
				if (img.GetPixel(x, y).A <= Opaco) continue;
				opaco[i] = true;
				Vector2 quv = ((canto + new Vector2(x + 0.5f, y + 0.5f)) / folha - min) / caixa;
				Vector2 fuv = deitado ? new Vector2(quv.Y, 1f - quv.X) : quv;
				bool direita = fuv.X >= 0.5f;
				bool soBraco = fuv.Y >= BracoDe && fuv.Y < BracoAte && Math.Abs(fuv.X - 0.5f) >= ColunaDoBraco;
				bool soPerna = fuv.Y >= PernaDe;
				cortado[i] = (soBraco && (corte & (direita ? 2 : 1)) != 0) || (soPerna && (corte & (direita ? 8 : 4)) != 0);
				fica[i] = !cortado[i];
			}

		// OS PEDACOS DO DESENHO (antes do corte) e o que sobra de cada um (depois): de cada pedaco que o corte partiu, so a parte
		// maior fica.
		int[] doDesenho = Rotular(opaco, w, h, out int pedacos);
		int[] doQueFica = Rotular(fica, w, h, out int partes);
		var partido = new bool[pedacos + 1];
		for (int i = 0; i < cortado.Length; i++)
			if (cortado[i]) partido[doDesenho[i]] = true;
		var tamanho = new int[partes + 1];
		var deQuem = new int[partes + 1];
		for (int i = 0; i < doQueFica.Length; i++)
			if (doQueFica[i] > 0) { tamanho[doQueFica[i]]++; deQuem[doQueFica[i]] = doDesenho[i]; }
		var maior = new int[pedacos + 1];
		for (int k = 1; k <= partes; k++)
			if (maior[deQuem[k]] == 0 || tamanho[k] > tamanho[maior[deQuem[k]]]) maior[deQuem[k]] = k;

		byte[]? solto = null;
		for (int i = 0; i < doQueFica.Length; i++)
		{
			int k = doQueFica[i];
			if (k == 0 || !partido[deQuem[k]] || k == maior[deQuem[k]]) continue;
			solto ??= new byte[w * h];
			solto[i] = 255;
		}
		return solto == null ? null : ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.R8, solto));
	}

	/// <summary>Os pedacos ligados (8-vizinhanca) de <paramref name="dentro"/>: 0 = fora, 1..<paramref name="n"/> = o pedaco.</summary>
	private static int[] Rotular(bool[] dentro, int w, int h, out int n)
	{
		var rotulo = new int[w * h];
		var pilha = new Stack<int>();
		n = 0;
		for (int i = 0; i < rotulo.Length; i++)
		{
			if (!dentro[i] || rotulo[i] != 0) continue;
			rotulo[i] = ++n;
			pilha.Push(i);
			while (pilha.Count > 0)
			{
				int p = pilha.Pop(), px = p % w, py = p / w;
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
					{
						int nx = px + dx, ny = py + dy;
						if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
						int q = ny * w + nx;
						if (!dentro[q] || rotulo[q] != 0) continue;
						rotulo[q] = n;
						pilha.Push(q);
					}
			}
		}
		return rotulo;
	}
}
