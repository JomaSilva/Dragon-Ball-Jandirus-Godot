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
	/// <summary>
	/// Um quadro da tira: a imagem e a **legenda** que vai desenhada embaixo dela.
	///
	/// ============================ POR QUE ISTO DEIXOU DE SER SO UM NUMERO -- E O DONO QUE PAGOU ============================
	/// A tira da agonia saia com seis discos numerados de 0 a 5, e o quadro 0 era **outro planeta**
	/// (Namek viva, o contra-exemplo). Nada na imagem dizia isso: a explicacao morava so no console.
	/// O dono abriu o arquivo, leu a linha como uma sequencia -- que e como qualquer um le uma tira --
	/// e perguntou: *"o planeta quando esta na animacao de explosao... eles sempre trocam o icone pra
	/// terra? pq nesse print parece q namek virou a terra"*.
	///
	/// **Nao havia troca de icone nenhuma** (provado com foto, em tres planetas, medido no pixel). O que
	/// havia era uma PROVA que provava errado, que e pior que prova nenhuma: ela custou uma investigacao
	/// inteira e por pouco nao custou um "conserto" num sistema que estava certo.
	///
	/// A causa raiz e esta linha: enquanto a tira so soubesse escrever DIGITO, nenhuma tira deste
	/// projeto poderia dizer o que ela esta mostrando -- e ha varias (`agonia-tira-do-chao.png`, as do
	/// bio, as da rolagem, as do marcavel). O `Numerar` virou <see cref="Escrever"/>, com alfabeto.
	/// ==================================================================================================================
	/// </summary>
	public readonly record struct Quadro(Image Imagem, string Legenda)
	{
		/// <summary>
		/// SO O NUMERO -- o jeito antigo, e ele continua valendo pra tira cujo assunto e um so.
		///
		/// Nao foi aposentado porque o defeito nunca foi o numero: foi o numero ser a UNICA coisa que
		/// dava pra escrever. Uma tira de sete poses do mesmo corpo nao precisa repetir o nome dele
		/// sete vezes.
		/// </summary>
		public Quadro(Image imagem, int numero)
			: this(imagem, numero.ToString(System.Globalization.CultureInfo.InvariantCulture)) { }
	}

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
			Escrever(tira, q.Legenda, x + c.GetWidth() / 2, alt - AlturaDoRotulo + 4);
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
	/// DUAS FOTOS, UMA EM CIMA DA OUTRA -- o quadro "antes x depois" de uma tira.
	///
	/// Nasceu privada na `RoboDoMarcavel` e subiu pra ca no dia em que a segunda bancada (a da aba
	/// de skills) precisou do mesmo empilhamento: a licao e a mesma da fileira -- `BlitRect` copia
	/// byte a byte e NAO converte formato, e sem o `Convert` a montagem sai preta com as duas fotos
	/// boas no disco. Uma segunda copia seria uma segunda chance de reaprender isso.
	/// </summary>
	public static Image Empilhar(Image cima, Image baixo)
	{
		var a = (Image)cima.Duplicate(); a.Convert(Image.Format.Rgba8);
		var b = (Image)baixo.Duplicate(); b.Convert(Image.Format.Rgba8);
		const int Entre = 6;
		int larg = Math.Max(a.GetWidth(), b.GetWidth());
		Image fora = Image.CreateEmpty(larg, a.GetHeight() + b.GetHeight() + Entre, false, Image.Format.Rgba8);
		fora.Fill(Fundo);
		fora.BlitRect(a, new Rect2I(0, 0, a.GetWidth(), a.GetHeight()), Vector2I.Zero);
		fora.BlitRect(b, new Rect2I(0, 0, b.GetWidth(), b.GetHeight()), new Vector2I(0, a.GetHeight() + Entre));
		return fora;
	}

	/// <summary>
	/// ============================ QUANTOS QUADROS DA TIRA GRAVADA TEM ROTULO **DESENHADO** ============================
	/// Le o PNG de volta do disco e conta, quadro a quadro, quantos tem tinta na faixa do rotulo.
	///
	/// **Ela existe porque "eu passei uma legenda" nao e "a legenda saiu"** -- e o defeito que esta
	/// classe inteira existe pra consertar foi exatamente esse tipo de distancia entre a intencao e o
	/// pixel. `Escrever` devolve `void`, joga fora todo caractere que nao conhece e ainda encolhe a
	/// escala quando o texto e longo: uma legenda inteira pode virar nada sem uma unica excecao. Numa
	/// tira em que o rotulo E a prova (ele e o que separa a vitima do controle pra quem abre o arquivo
	/// sem contexto), rotulo que nao saiu e a prova inteira.
	///
	/// A faixa lida e a de baixo (`AlturaDoRotulo`), e a coluna de cada quadro sai da largura dele --
	/// ou seja, ela confere que **cada** quadro tem o SEU rotulo, e nao que "a tira tem texto em algum
	/// lugar", que ficaria verde com quatro dos cinco em branco.
	/// ==============================================================================================================
	/// </summary>
	public static int QuadrosComRotulo(string caminhoAbsoluto, IReadOnlyList<Quadro> quadros)
	{
		if (quadros.Count == 0) return 0;

		Image? tira = Image.LoadFromFile(caminhoAbsoluto);
		if (tira == null || tira.IsEmpty()) return 0;

		int alt = tira.GetHeight(), comTinta = 0, x = Vao;

		foreach (Quadro q in quadros)
		{
			int larg = q.Imagem.GetWidth();
			int tinta = 0;

			for (int y = alt - AlturaDoRotulo; y < alt; y++)
				for (int px = x; px < x + larg && px < tira.GetWidth(); px++)
				{
					if (y < 0 || y >= tira.GetHeight()) continue;
					Color c = tira.GetPixel(px, y);
					// A TINTA E CLARA E O FUNDO E ESCURO: `Escrever` pinta 0,85/0,90/0,85 sobre
					// 0,06/0,06/0,08. Meio caminho entre os dois separa os dois sem ambiguidade.
					if (c.Luminance > 0.45f) tinta++;
				}

			// O PISO E 40 PIXELS: uma letra de 4x5 na escala 4 pinta ~200 px, entao 40 nao aceita
			// respingo e nao exige legenda longa.
			if (tinta >= 40) comTinta++;
			x += larg + Vao;
		}

		return comTinta;
	}

	/// <summary>
	/// ============================ A FONTE 4x5, FEITA A MAO -- E POR QUE ELA TEM LETRA ============================
	/// Sem legenda, a tira e uma fileira de discos parecidos e quem olha tem que ADIVINHAR o que ela
	/// afirma. Ate aqui so havia digito, e o preco disso esta escrito no <see cref="Quadro"/>: o dono
	/// leu uma tira de contra-exemplo + rampa como se fosse um filme, e concluiu que um planeta virava
	/// outro no meio da explosao.
	///
	/// **UMA FONTE E NAO UM `Label`**: a tira e uma <see cref="Image"/> montada por `BlitRect` fora da
	/// arvore de cena -- nao ha viewport pra rasterizar texto nela. Desenhar pixel e o unico caminho.
	///
	/// ============================ E ELA NASCEU 3x5 E FOI PROMOVIDA PELA FOTO ============================
	/// Tres colunas e o menor grid em que "cabe um alfabeto", e por isso foi o primeiro escolhido. So
	/// que **nele o `N` nao existe**: a diagonal do meio ocupa a mesma celula da barra do `H`, e a unica
	/// saida e engordar o bloco central -- que e o mesmo desenho da `M`. A tira do controle saiu com
	/// "NAMEK" lendo **"MAMEK"**, e a do rescaldo com "+55S" lendo **"555"** (o `S` e o `5` sao o mesmo
	/// desenho em 3x5). Numa tira, uma legenda ambigua nao e um detalhe: e a repeticao, em miniatura,
	/// do defeito que esta tira acabou de consertar.
	///
	/// Quatro colunas custam 33% de largura e resolvem os dois por DESENHO: o `N` ganha a diagonal
	/// (`##.#`/`#.##`), o `S` ganha a curva contra as tres barras do `5`, e o `0` ganha o corte contra
	/// o `O`. Fora do alfabeto, tudo o que a fonte nao conhece vira ESPACO -- nunca um quadrado preto,
	/// que denunciaria caractere perdido no meio de uma legenda que deveria ser lida sem esforco.
	/// ==================================================================================================
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<char, string> Glifos = new()
	{
		// O ZERO LEVA UM CORTE e o "O" nao: sem isso os dois sao o mesmo desenho, e uma legenda como
		// "AG 0.12" fica a um passo de virar "AG O.12".
		['0'] = ".##." + "#.##" + "##.#" + "#..#" + ".##.",
		['1'] = ".#.." + "##.." + ".#.." + ".#.." + "###.",
		['2'] = "###." + "...#" + ".##." + "#..." + "####",
		['3'] = "###." + "...#" + ".##." + "...#" + "###.",
		['4'] = "#..#" + "#..#" + "####" + "...#" + "...#",
		['5'] = "####" + "#..." + "###." + "...#" + "###.",
		['6'] = ".###" + "#..." + "###." + "#..#" + ".##.",
		['7'] = "####" + "...#" + "..#." + ".#.." + ".#..",
		['8'] = ".##." + "#..#" + ".##." + "#..#" + ".##.",
		['9'] = ".##." + "#..#" + ".###" + "...#" + "###.",

		['A'] = ".##." + "#..#" + "####" + "#..#" + "#..#",
		['B'] = "###." + "#..#" + "###." + "#..#" + "###.",
		['C'] = ".###" + "#..." + "#..." + "#..." + ".###",
		['D'] = "###." + "#..#" + "#..#" + "#..#" + "###.",
		['E'] = "####" + "#..." + "###." + "#..." + "####",
		['F'] = "####" + "#..." + "###." + "#..." + "#...",
		['G'] = ".###" + "#..." + "#.##" + "#..#" + ".###",
		['H'] = "#..#" + "#..#" + "####" + "#..#" + "#..#",
		['I'] = "####" + ".##." + ".##." + ".##." + "####",
		['J'] = "..##" + "...#" + "...#" + "#..#" + ".##.",
		['K'] = "#..#" + "#.#." + "##.." + "#.#." + "#..#",
		['L'] = "#..." + "#..." + "#..." + "#..." + "####",
		// ============================ `M` E `N`: A DIAGONAL SO CABE EM QUATRO COLUNAS ============================
		// A primeira fonte desta tira era 3x5, e nela **nao existe** um `N` legivel: com so tres
		// colunas, a diagonal do meio e a mesma celula da barra do `H`, e o unico jeito de diferenciar
		// as tres letras e engordar o bloco do meio. O resultado foi medido na foto: **"NAMEK" saiu
		// lendo "MAMEK"** -- na escala 4 a diferenca de uma linha e um degrau de 4 px dentro de um bloco
		// de 20, e o olho nao o separa.
		//
		// Quatro colunas resolvem por desenho e nao por truque: o `N` ganha a diagonal de verdade
		// (`##.#` / `#.##`) e o `M` fica com o vale no topo. E a legenda de uma tira **precisa** ser
		// lida sem esforco -- foi uma tira ilegivel que fez o dono concluir que um planeta virava outro.
		// =====================================================================================================
		['M'] = "#..#" + "####" + "####" + "#..#" + "#..#",
		['N'] = "#..#" + "##.#" + "#.##" + "#..#" + "#..#",
		['O'] = ".##." + "#..#" + "#..#" + "#..#" + ".##.",
		['P'] = "###." + "#..#" + "###." + "#..." + "#...",
		['Q'] = ".##." + "#..#" + "#..#" + "#.#." + ".#.#",
		['R'] = "###." + "#..#" + "###." + "#.#." + "#..#",
		// O `S` NAO PODE SER O `5`: no 3x5 os dois eram o mesmo desenho, e a legenda do rescaldo saiu
		// **"EARTH MAIS 555"** onde devia ler "+55S". Numa tira que existe pra dizer QUANDO cada quadro
		// foi tirado, confundir o numero com a unidade e o defeito inteiro de novo, em miniatura.
		['S'] = ".###" + "#..." + ".##." + "...#" + "###.",
		['T'] = "####" + ".##." + ".##." + ".##." + ".##.",
		['U'] = "#..#" + "#..#" + "#..#" + "#..#" + ".##.",
		['V'] = "#..#" + "#..#" + "#..#" + ".##." + ".##.",
		['W'] = "#..#" + "#..#" + "####" + "####" + "#..#",
		['X'] = "#..#" + ".##." + ".##." + ".##." + "#..#",
		['Y'] = "#..#" + "#..#" + ".##." + ".##." + ".##.",
		['Z'] = "####" + "..#." + ".#.." + "#..." + "####",

		['-'] = "...." + "...." + "####" + "...." + "....",
		['+'] = "...." + ".#.." + "###." + ".#.." + "....",
		['='] = "...." + "####" + "...." + "####" + "....",
		['.'] = "...." + "...." + "...." + "...." + ".#..",
		[','] = "...." + "...." + "...." + ".#.." + "#...",
		[':'] = "...." + ".#.." + "...." + ".#.." + "....",
		['/'] = "...#" + "..#." + ".#.." + "#..." + "#...",
		['%'] = "#..#" + "...#" + ".##." + "#..." + "#..#",
		['('] = "..##" + ".#.." + ".#.." + ".#.." + "..##",
		[')'] = "##.." + "..#." + "..#." + "..#." + "##..",
	};

	/// <summary>
	/// ESCREVE A LEGENDA, centrada no quadro. Maiusculiza sozinha (a fonte so tem caixa alta) e joga
	/// fora o que nao conhece.
	///
	/// A ESCALA CAI QUANDO A LEGENDA E LONGA, e isso nao e cosmetico: uma legenda que transborda
	/// invade o quadro do lado, e ai ela nao rotula mais nem o proprio -- que e uma forma nova do
	/// mesmo defeito que esta tira acabou de pagar (rotulo apontando pro quadro errado).
	/// </summary>
	private static void Escrever(Image tira, string texto, int centroX, int topoY)
	{
		if (string.IsNullOrEmpty(texto)) return;

		texto = texto.ToUpperInvariant();

		const int LarguraDoQuadro = 4, VaoDaLetra = 1;
		int escala = 4;
		while (escala > 1 && texto.Length * (LarguraDoQuadro + VaoDaLetra) * escala > tira.GetWidth())
			escala--;

		int passo = (LarguraDoQuadro + VaoDaLetra) * escala;
		int x0 = centroX - texto.Length * passo / 2;
		var branco = new Color(0.85f, 0.9f, 0.85f);

		for (int c = 0; c < texto.Length; c++)
		{
			if (!Glifos.TryGetValue(texto[c], out string? g)) continue;

			for (int linha = 0; linha < 5; linha++)
				for (int coluna = 0; coluna < LarguraDoQuadro; coluna++)
				{
					if (g[linha * LarguraDoQuadro + coluna] != '#') continue;
					for (int y = 0; y < escala; y++)
						for (int x = 0; x < escala; x++)
						{
							int px = x0 + c * passo + coluna * escala + x, py = topoY + linha * escala + y;
							if (px >= 0 && py >= 0 && px < tira.GetWidth() && py < tira.GetHeight())
								tira.SetPixel(px, py, branco);
						}
				}
		}
	}
}
