using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A TIRA: N FOTOS LADO A LADO, NUMERADAS ============================
/// O arquivo que o dono abre. Uma bancada visual entrega duas coisas -- o placar, que e o veredito, e
/// a TIRA, que e o que deixa alguem DISCORDAR do veredito. Um numero sozinho ("dif 0,214") nao diz se
/// o planeta rachou ou se ele virou um disco de ruido amarelo; foi exatamente assim que a primeira
/// rodada da agonia passou verde com o auge repintado em vez de rachado.
///
/// ============================ POR QUE ISTO E UM ARQUIVO SO, E COMPARTILHADO ============================
/// A `RoboDeBioRetrato` escreveu esta montagem primeiro e pagou por ela: **a tira saiu PRETA com as
/// sete fotos boas no disco e o placar limpo**, porque `BlitRect` copia byte a byte e nao converte
/// formato -- o recorte sai do viewport em `Rgb8` e a folha nasce `Rgba8`. Uma segunda copia deste
/// codigo noutra bancada seria uma segunda chance de reaprender isso, e a casa tem regra contra duas
/// verdades com o mesmo nome.
///
/// Por isso o `Convert` e a GUARDA DE PINTURA moram aqui dentro, e nao em quem chama: quem monta uma
/// tira nova nao precisa saber que este tombo existiu.
/// ==================================================================================================
/// </summary>
public static class TiraDeFotos
{
	/// <summary>Um quadro da tira: a imagem e o numero que vai desenhado embaixo dela.</summary>
	public readonly record struct Quadro(Image Imagem, int Numero);

	private const int Vao = 10;
	private const int AlturaDoRotulo = 28;

	/// <summary>O fundo da tira. Escuro e liso pra a guarda de pintura ter contra o que comparar.</summary>
	public static readonly Color Fundo = new(0.06f, 0.06f, 0.08f);

	/// <summary>
	/// Monta a tira, grava em <paramref name="caminhoAbsoluto"/> e devolve **que fracao dela e imagem
	/// e nao fundo**.
	///
	/// A fracao e o retorno (e nao um `bool` de sucesso) de proposito: quem chama tem que ter um
	/// numero pra por na linha da bancada. Um `SavePng` que devolvesse `void` deixaria a bancada
	/// afirmando "a tira saiu" sobre um arquivo que pode estar vazio -- que e o caso ja acontecido.
	/// </summary>
	public static double Montar(IReadOnlyList<Quadro> quadros, string caminhoAbsoluto)
	{
		if (quadros.Count == 0) return 0;

		int larg = quadros.Sum(q => q.Imagem.GetWidth()) + Vao * (quadros.Count + 1);
		int alt = quadros.Max(q => q.Imagem.GetHeight()) + Vao * 2 + AlturaDoRotulo;

		Image tira = Image.CreateEmpty(larg, alt, false, Image.Format.Rgba8);
		tira.Fill(Fundo);

		int x = Vao;
		foreach (Quadro q in quadros)
		{
			// SEM ESTE `Convert` A TIRA SAI PRETA -- ver o cabecalho. `BlitRect` nao converte formato.
			var c = (Image)q.Imagem.Duplicate();
			c.Convert(Image.Format.Rgba8);
			tira.BlitRect(c, new Rect2I(0, 0, c.GetWidth(), c.GetHeight()), new Vector2I(x, Vao));
			Numerar(tira, q.Numero, x + c.GetWidth() / 2, alt - AlturaDoRotulo + 4);
			x += c.GetWidth() + Vao;
		}

		tira.SavePng(caminhoAbsoluto);

		// A GUARDA DE PINTURA: quanto da tira e imagem e quanto e fundo.
		int pintados = 0, total = 0;
		for (int y = Vao; y < alt - AlturaDoRotulo; y += 3)
			for (int px = 0; px < larg; px += 3)
			{
				total++;
				Color c2 = tira.GetPixel(px, y);
				if (Math.Abs(c2.R - Fundo.R) + Math.Abs(c2.G - Fundo.G) + Math.Abs(c2.B - Fundo.B) > 0.05f)
					pintados++;
			}

		return total == 0 ? 0 : (double)pintados / total;
	}

	/// <summary>
	/// O NUMERO DO QUADRO, desenhado a mao em 3x5. Sem rotulo, a tira e cinco discos parecidos e quem
	/// olha tem que adivinhar de que lado ela comeca -- que e o tipo de prova que, meses depois, nao
	/// prova nada.
	/// </summary>
	private static readonly string[] Digitos =
	[
		"###" + "#.#" + "#.#" + "#.#" + "###",   // 0
		".#." + "##." + ".#." + ".#." + "###",   // 1
		"###" + "..#" + "###" + "#.." + "###",   // 2
		"###" + "..#" + "###" + "..#" + "###",   // 3
		"#.#" + "#.#" + "###" + "..#" + "..#",   // 4
		"###" + "#.." + "###" + "..#" + "###",   // 5
		"###" + "#.." + "###" + "#.#" + "###",   // 6
		"###" + "..#" + "..#" + "..#" + "..#",   // 7
		"###" + "#.#" + "###" + "#.#" + "###",   // 8
		"###" + "#.#" + "###" + "..#" + "###",   // 9
	];

	private static void Numerar(Image tira, int numero, int centroX, int topoY)
	{
		const int Escala = 4;
		string d = Digitos[Math.Clamp(numero, 0, 9)];
		int x0 = centroX - 3 * Escala / 2;
		var branco = new Color(0.85f, 0.9f, 0.85f);

		for (int linha = 0; linha < 5; linha++)
			for (int coluna = 0; coluna < 3; coluna++)
			{
				if (d[linha * 3 + coluna] != '#') continue;
				for (int y = 0; y < Escala; y++)
					for (int x = 0; x < Escala; x++)
					{
						int px = x0 + coluna * Escala + x, py = topoY + linha * Escala + y;
						if (px >= 0 && py >= 0 && px < tira.GetWidth() && py < tira.GetHeight())
							tira.SetPixel(px, py, branco);
					}
			}
	}
}
