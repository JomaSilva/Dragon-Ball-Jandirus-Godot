using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ CONTAR OS DISCOS **NO PIXEL** ============================
/// O pedido do dono e visual: *"na fusao o `FusionLight.png` n e um em cada personagem, e so UM
/// efeito em cima dos dois personagens, atualmente sao 2 e fica estranho"*. A `--diagcenafusao` e a
/// `--fusaoduplateste` respondem a isso lendo CAMPO (`LuzesDaFusaoDeTeste == 1`) -- e um campo diz
/// quantos NODES a cena criou, nao quantos claroes o dono ve na tela. Este arquivo e a metade que
/// olha a foto.
///
/// ============================ E CONTAR COMPONENTES CONEXOS **NAO BASTA** -- ISTO E UM ACHADO ============================
/// A ideia obvia (limiar de brilho + componentes conexos) **fica verde com o defeito de volta**, e o
/// motivo e geometria medida, nao opiniao:
///
///   * o quadro cheio da folha tem 112 px de lado; na escala 2 do DM (`_Fusion.dm:121`) isso e 224
///     px de MUNDO, ou seja um disco de ~7 tiles de diametro;
///   * o palco da foto (`RoboDeFotoDeFusao.OPalcoDaFoto`) e de 4 tiles -- e ele e MAIOR que a
///     distancia do jogo de proposito: com os dois colados (que e como eles chegam, depois do
///     puxao) duas copias da folha teriam area e caixa quase iguais as de uma, e a contagem
///     perderia o dente contra o defeito que ela existe pra pegar.
///
/// Ou seja: **no auge do estouro os dois discos do defeito SEMPRE se encostam** -- eles saem como UM
/// componente conexo, e uma bancada que so contasse componentes assinaria "ha um disco" com dois na
/// tela. (Nos quadros pequenos da folha eles se separam, e ai a contagem de manchas pega: com o
/// defeito injetado o quadro 0 devolveu **2**.)
///
/// A tentativa seguinte foi contar **NUCLEOS** -- transformada de distancia sobre a mascara e
/// componentes de `{ dist &gt; fracao x dist_maxima }` --, na ideia de que duas copias encostadas fariam
/// um amendoim com dois miolos. **Ela tambem nao separa nesta geometria, e o numero diz por que**:
/// duas copias a 256 px de tela com raio de 224 px se cruzam a 184 px de profundidade, e o miolo de
/// uma copia so vai a 206 px. Vinte e dois pixels de folga nao distinguem nada, e com o defeito
/// injetado a contagem de nucleos ficou VERDE dizendo "1". Ela virou NOTA no log.
///
/// **Quem responde por copia encostada e o TAMANHO**: area medida contra a area de uma copia da folha
/// (1,40 com o defeito) e a caixa (146% de largura). Ver `RoboDeFotoDeFusao.ContarDiscos`.
/// ==============================================================================================
///
/// ============================ A MASCARA E POR **COR**, E ELA E CALIBRADA NA HORA ============================
/// Nao ha limiar de brilho escrito aqui, e nao pode haver. Medido na folha: ela so tem **seis** cores
/// e alfa binario (0 ou 255 nos 147.456 pixels), e as tres que ocupam 99,9% dela sao `f8f8f8`,
/// `eafeff` e `e0f2fe` -- todas a menos de 0,09 uma da outra. Mas na TELA elas chegam multiplicadas
/// pelo `CanvasModulate` da hora (`dcdcd2` ao meio-dia), pelo veu do clima e pelo que mais o cliente
/// pendurar: ao meio-dia o branco `f8f8f8` da folha vira `d6d6cc` na foto. Um `luz &gt; 0,9` escrito
/// aqui reprovaria a cena certa num dia nublado.
///
/// Entao a referencia e AMOSTRADA na foto, no ponto em que a propria cena diz que a luz esta
/// (`Transformacao.PosDaLuzDaFusaoDeTeste`), e a mascara e "tudo o que esta a menos de
/// <paramref name="tolerancia"/> daquela cor". Isso nao e circular pra a pergunta que importa: a
/// amostra diz QUAL E A COR do disco, e a varredura acha TODOS os pixels dela na tela -- se houver um
/// segundo disco, ele entra na mascara igual.
/// ========================================================================================================
/// </summary>
internal static class ContagemDeDiscos
{
	/// <summary>
	/// QUANTO DUAS CORES PODEM DIFERIR E AINDA SEREM "A MESMA COR DE DISCO", em distancia RGB de 0 a 1.
	///
	/// **0,13 e medido, e nao chutado**: as tres cores da folha estao a no maximo 0,087 umas das
	/// outras, e como o `CanvasModulate` MULTIPLICA (nunca soma), a tela so pode aproxima-las. A
	/// sobra existe pro que o cliente pendura por cima (veu do clima, tinta da zona).
	///
	/// E o outro lado tambem esta medido: o branco do HUD (`ffffff`, que nao passa pelo
	/// `CanvasModulate` porque e outra `CanvasLayer`) fica a 0,30 da cor do disco, e a grama a 0,55.
	/// Os dois ficam de fora com folga.
	/// </summary>
	internal const float ToleranciaDeCor = 0.13f;

	/// <summary>
	/// A FRACAO DA DISTANCIA MAXIMA QUE DEFINE UM NUCLEO. Ver o cabecalho.
	///
	/// Com 0,60: o nucleo de UM disco e o miolo de raio `0,40 x R`, e dois discos a distancia `d`
	/// separam quando `d &gt; 0,80 x R`. Com o R de 112 px de mundo do quadro cheio, isso da 90 px --
	/// e os 4 tiles (128 px) do portao do convite passam disso com folga. Abaixo de 4 tiles os dois
	/// nucleos se fundem e a contagem volta a 1: por isso a bancada planta os dois corpos EXATAMENTE
	/// no portao, que e a distancia maxima que o jogo permite e o pior caso da medida.
	/// </summary>
	internal const float FracaoDoNucleo = 0.60f;

	/// <summary>Uma mancha da mascara: quantos pixels, onde, e o centro de massa.</summary>
	internal readonly record struct Mancha(int Area, Rect2I Caixa, Vector2 Centro);

	/// <summary>
	/// A MASCARA DE DISCO dentro de <paramref name="regiao"/>, em coordenadas de TELA.
	///
	/// A regiao existe pra a medida nao ser envenenada pelo HUD: o painel de vida, o boneco de papel
	/// e a caixa de fala moram nos cantos, sao de outra `CanvasLayer` (nao levam `CanvasModulate`) e
	/// tem texto claro. Recortar e mais honesto do que inventar uma regra de "ignore texto".
	/// </summary>
	internal static bool[] Mascara(Image img, Rect2I regiao, Color referencia, float tolerancia)
	{
		var m = new bool[regiao.Size.X * regiao.Size.Y];
		float t2 = tolerancia * tolerancia;
		for (int y = 0; y < regiao.Size.Y; y++)
			for (int x = 0; x < regiao.Size.X; x++)
			{
				Color c = img.GetPixel(regiao.Position.X + x, regiao.Position.Y + y);
				float dr = c.R - referencia.R, dg = c.G - referencia.G, db = c.B - referencia.B;
				m[y * regiao.Size.X + x] = dr * dr + dg * dg + db * db <= t2;
			}
		return m;
	}

	/// <summary>
	/// AS MANCHAS DA MASCARA, em ordem decrescente de area -- vizinhanca de 4, laco iterativo.
	///
	/// Recursao aqui estouraria a pilha: uma unica mancha de disco tem ~150 mil pixels.
	/// </summary>
	internal static List<Mancha> Manchas(bool[] mascara, Vector2I lado, Vector2I origem,
										 int areaMinima)
	{
		int w = lado.X, h = lado.Y;
		var visto = new bool[mascara.Length];
		var pilha = new Stack<int>();
		var achadas = new List<Mancha>();

		for (int i = 0; i < mascara.Length; i++)
		{
			if (!mascara[i] || visto[i]) continue;

			pilha.Push(i);
			visto[i] = true;
			int area = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
			double sx = 0, sy = 0;

			while (pilha.Count > 0)
			{
				int p = pilha.Pop();
				int px = p % w, py = p / w;
				area++;
				sx += px; sy += py;
				if (px < x0) x0 = px;
				if (px > x1) x1 = px;
				if (py < y0) y0 = py;
				if (py > y1) y1 = py;

				if (px > 0 && mascara[p - 1] && !visto[p - 1]) { visto[p - 1] = true; pilha.Push(p - 1); }
				if (px < w - 1 && mascara[p + 1] && !visto[p + 1]) { visto[p + 1] = true; pilha.Push(p + 1); }
				if (py > 0 && mascara[p - w] && !visto[p - w]) { visto[p - w] = true; pilha.Push(p - w); }
				if (py < h - 1 && mascara[p + w] && !visto[p + w]) { visto[p + w] = true; pilha.Push(p + w); }
			}

			if (area < areaMinima) continue;
			achadas.Add(new Mancha(
				area,
				new Rect2I(origem.X + x0, origem.Y + y0, x1 - x0 + 1, y1 - y0 + 1),
				new Vector2(origem.X + (float)(sx / area), origem.Y + (float)(sy / area))));
		}

		achadas.Sort((a, b) => b.Area.CompareTo(a.Area));
		return achadas;
	}

	/// <summary>
	/// **QUANTOS NUCLEOS LUMINOSOS** ha nesta mancha, e onde. Ver o cabecalho pro porque de a
	/// contagem ser esta, e nao a de componentes conexos.
	///
	/// A transformada de distancia e a de chanfro (3,4) em duas passadas -- a aproximacao classica da
	/// euclidiana, com erro abaixo de 2%. Precisao maior nao muda resposta nenhuma aqui: a pergunta e
	/// *"ha um miolo ou dois?"*, e os dois miolos do defeito ficam a 256 px um do outro.
	///
	/// **A mascara e limitada A MANCHA ESCOLHIDA** e nao a tela: sem isso, o texto claro da caixa de
	/// fala entraria na conta e cada letra viraria um nucleo de distancia 1.
	/// </summary>
	internal static (int Quantos, List<Vector2> Centros, int MaiorDistancia)
		Nucleos(bool[] mascara, Vector2I lado, Vector2I origem, Rect2I daMancha, float fracao)
	{
		int w = lado.X, h = lado.Y;

		// A distancia em unidades de chanfro: 0 fora da mascara e "infinito" dentro.
		const int Perto = 3, Diagonal = 4, Infinito = int.MaxValue / 4;
		var d = new int[mascara.Length];
		for (int i = 0; i < mascara.Length; i++) d[i] = mascara[i] ? Infinito : 0;

		// SO O QUE ESTA DENTRO DA MANCHA ESCOLHIDA conta -- o resto vira fundo (distancia 0).
		var caixa = new Rect2I(daMancha.Position - origem, daMancha.Size);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				if (!caixa.HasPoint(new Vector2I(x, y))) d[y * w + x] = 0;

		int Menor(int a, int b) => a < b ? a : b;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				int i = y * w + x;
				if (d[i] == 0) continue;
				if (x > 0) d[i] = Menor(d[i], d[i - 1] + Perto);
				if (y > 0) d[i] = Menor(d[i], d[i - w] + Perto);
				if (x > 0 && y > 0) d[i] = Menor(d[i], d[i - w - 1] + Diagonal);
				if (x < w - 1 && y > 0) d[i] = Menor(d[i], d[i - w + 1] + Diagonal);
			}
		for (int y = h - 1; y >= 0; y--)
			for (int x = w - 1; x >= 0; x--)
			{
				int i = y * w + x;
				if (d[i] == 0) continue;
				if (x < w - 1) d[i] = Menor(d[i], d[i + 1] + Perto);
				if (y < h - 1) d[i] = Menor(d[i], d[i + w] + Perto);
				if (x < w - 1 && y < h - 1) d[i] = Menor(d[i], d[i + w + 1] + Diagonal);
				if (x > 0 && y < h - 1) d[i] = Menor(d[i], d[i + w - 1] + Diagonal);
			}

		int maior = 0;
		foreach (int v in d) if (v > maior && v < Infinito) maior = v;
		if (maior == 0) return (0, [], 0);

		int corte = (int)(maior * fracao);
		var miolo = new bool[d.Length];
		for (int i = 0; i < d.Length; i++) miolo[i] = d[i] >= corte && d[i] < Infinito && d[i] > 0;

		// AREA MINIMA DE NUCLEO: 16 px. Um miolo de verdade tem milhares; o que este piso corta e o
		// pixel solto que a borda serrilhada da folha (alfa de 1 bit, sem anti-alias) deixa pra tras.
		List<Mancha> nucleos = Manchas(miolo, lado, origem, areaMinima: 16);
		return (nucleos.Count, nucleos.ConvertAll(n => n.Centro), maior / Perto);
	}

	/// <summary>
	/// QUANTO DESTA CAIXA A MASCARA COBRE, de 0 a 1 -- e ela e a pergunta *"o disco esta tapando este
	/// corpo?"*.
	/// </summary>
	internal static float CoberturaDe(bool[] mascara, Vector2I lado, Vector2I origem, Rect2I caixa)
	{
		int dentro = 0, total = 0;
		for (int y = caixa.Position.Y; y < caixa.Position.Y + caixa.Size.Y; y++)
			for (int x = caixa.Position.X; x < caixa.Position.X + caixa.Size.X; x++)
			{
				int lx = x - origem.X, ly = y - origem.Y;
				if (lx < 0 || ly < 0 || lx >= lado.X || ly >= lado.Y) continue;
				total++;
				if (mascara[ly * lado.X + lx]) dentro++;
			}
		return total == 0 ? 0 : dentro / (float)total;
	}

	/// <summary>
	/// A COR MEDIANA DE UMA CAIXA -- a regua do chao, tirada da PROPRIA foto.
	///
	/// Mediana por canal, e nao media: um pedregulho levitando no meio da amostra puxa a media e nao
	/// mexe na mediana. E ela e amostrada de 2 em 2 px porque o que se quer e a cor do chao, nao um
	/// censo.
	/// </summary>
	internal static Color CorMediana(Image img, Rect2I caixa)
	{
		List<float> r = [], g = [], b = [];
		for (int y = caixa.Position.Y; y < caixa.Position.Y + caixa.Size.Y; y += 2)
			for (int x = caixa.Position.X; x < caixa.Position.X + caixa.Size.X; x += 2)
			{
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				Color c = img.GetPixel(x, y);
				r.Add(c.R); g.Add(c.G); b.Add(c.B);
			}
		if (r.Count == 0) return Colors.Black;
		r.Sort(); g.Sort(); b.Sort();
		return new Color(r[r.Count / 2], g[g.Count / 2], b[b.Count / 2]);
	}

	/// <summary>
	/// QUANTO DESTA CAIXA **NAO E CHAO**, de 0 a 1 -- e ela e a pergunta *"ha um corpo desenhado
	/// aqui?"*.
	///
	/// A regua e a cor do chao da MESMA foto (ver <see cref="CorMediana"/>), e nao uma cor de grama
	/// escrita aqui: a bancada roda de dia, mas roda em planetas diferentes e com clima, e um verde
	/// cravado seria a mesma armadilha do limiar de brilho.
	/// </summary>
	internal static float ConteudoDe(Image img, Rect2I caixa, Color chao, float tolerancia)
	{
		int fora = 0, total = 0;
		float t2 = tolerancia * tolerancia;
		for (int y = caixa.Position.Y; y < caixa.Position.Y + caixa.Size.Y; y++)
			for (int x = caixa.Position.X; x < caixa.Position.X + caixa.Size.X; x++)
			{
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				total++;
				Color c = img.GetPixel(x, y);
				float dr = c.R - chao.R, dg = c.G - chao.G, db = c.B - chao.B;
				if (dr * dr + dg * dg + db * db > t2) fora++;
			}
		return total == 0 ? 0 : fora / (float)total;
	}

	/// <summary>
	/// QUANTO "NAO E CHAO" TEM UMA MANCHA QUALQUER DESTA CENA -- a mediana de uma grade de caixas do
	/// mesmo tamanho, tirando as que encostam no que se quer medir.
	///
	/// ============================ ELA E O CONTRA-EXEMPLO, E ELE PRECISA SER ROBUSTO ============================
	/// A afirmacao *"ha um corpo desenhado aqui"* so vale contra uma regua, e a regua obvia (UMA caixa
	/// de controle num ponto escolhido) tem um modo de falha bobo: basta uma arvore, um pedregulho
	/// levitando ou um NPC da populacao cair naquele ponto e o controle sobe -- num mundo VIVO, que e
	/// onde esta bancada roda. A MEDIANA de vinte e cinco caixas nao se importa com duas delas.
	/// ====================================================================================================
	/// </summary>
	internal static float ChaoTipico(Image img, Rect2I regiao, int lado, Color chao, float tolerancia,
									 List<Rect2I> fora)
	{
		var amostras = new List<float>();
		int passo = Math.Max(lado, 8);
		for (int y = regiao.Position.Y + lado / 2; y < regiao.Position.Y + regiao.Size.Y - lado / 2; y += passo)
			for (int x = regiao.Position.X + lado / 2; x < regiao.Position.X + regiao.Size.X - lado / 2; x += passo)
			{
				var c = new Rect2I(x - lado / 2, y - lado / 2, lado, lado);
				if (fora.Exists(f => f.Intersects(c))) continue;
				amostras.Add(ConteudoDe(img, Dentro(c, img), chao, tolerancia));
			}
		if (amostras.Count == 0) return 0;
		amostras.Sort();
		return amostras[amostras.Count / 2];
	}

	/// <summary>Uma caixa quadrada de lado <paramref name="lado"/> em volta de um ponto de tela.</summary>
	internal static Rect2I CaixaEmVolta(Vector2 centro, int lado) =>
		new((int)centro.X - lado / 2, (int)centro.Y - lado / 2, lado, lado);

	/// <summary>A caixa apertada pra dentro da imagem -- nunca fora, nunca de lado zero.</summary>
	internal static Rect2I Dentro(Rect2I r, Image img)
	{
		int x0 = Math.Clamp(r.Position.X, 0, Math.Max(0, img.GetWidth() - 1));
		int y0 = Math.Clamp(r.Position.Y, 0, Math.Max(0, img.GetHeight() - 1));
		int x1 = Math.Clamp(r.Position.X + r.Size.X, x0 + 1, img.GetWidth());
		int y1 = Math.Clamp(r.Position.Y + r.Size.Y, y0 + 1, img.GetHeight());
		return new Rect2I(x0, y0, x1 - x0, y1 - y0);
	}
}
