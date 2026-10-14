namespace Jandirus.Core.World;

/// <summary>
/// **O CAMINHO EM VOLTA DA PAREDE** -- A* na grade de celulas, pra um corpo que o servidor dirige.
///
/// O dono, 2026-10-13: *"percebi q ela é meio burra as vezes, n sabe fazer path finding e fica andando contra a
/// parede"*. Ele estava certo, e o atuador dizia isso com todas as letras: *"a IA nao contorna obstaculo (nao ha A*
/// aqui), ela desliza no eixo livre e para"*. O cerebro anda NA DIRECAO do alvo; com uma casa no meio, ele anda na
/// direcao da casa.
///
/// ============================ O QUE ELE E ============================
/// Uma busca A* de oito direcoes sobre as celulas do mapa (`ZoneCollision.Bloqueia`, com o MODO de quem anda: a pe
/// a agua barra, nadando nao, voando alto so o predio), numa JANELA fixa em volta de quem anda
/// (<see cref="Raio"/> celulas pra cada lado) e com TETO de celulas abertas (<see cref="TetoDeExpansoes"/>). Os
/// pontos que ele devolve sao posicoes de CORPO: o centro de cada celula, subido de `MoveRules.FeetOffsetY`, que e
/// onde a caixa dos pes cabe inteira dentro dela.
///
/// A DIAGONAL so vale com as duas celulas do canto livres -- a caixa dos pes encostaria nelas, e o `Advance`
/// barraria um passo que o caminho prometeu.
///
/// QUANDO O DESTINO NAO SE ALCANCA (fora da janela, cercado, dentro do que barra), ele devolve o caminho ate a
/// celula alcancavel MAIS PERTO do destino -- se ela for mais perto do que o corpo ja esta. Um corpo que nao tem
/// como chegar ainda assim se aproxima, em vez de empurrar a parede.
///
/// ============================ O QUE ELE NAO E ============================
///   * Nao conhece CORPOS: gente anda, e um caminho tracado em volta de alguem estaria velho no tique seguinte.
///     Quem contorna gente (e o que mais emperrar o passo) e o atuador -- `GameServer.CaminhoDaIa.cs`.
///   * Nao e chamado todo tique: quem o chama (o mesmo arquivo) pergunta antes se a linha reta serve, guarda a
///     resposta por corpo e so refaz quando ela envelhece.
///   * NAO ALOCA: o rascunho da busca e estatico (o tique do servidor e de uma thread so) e a resposta vai pro
///     vetor de quem chamou.
/// </summary>
public static class Caminho
{
	/// <summary>Quantas celulas pra cada lado de quem anda a busca enxerga.</summary>
	public const int Raio = 28;

	/// <summary>Quantas celulas a busca abre antes de se dar por vencida (e devolver o mais perto que chegou).</summary>
	public const int TetoDeExpansoes = 1600;

	/// <summary>Quantos pontos um corpo guarda de cada caminho. O resto se replaneja no meio do percurso.</summary>
	public const int MaximoDePontos = 40;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): nao ha caminho nenhum -- a IA volta a andar reto pra cima da
	/// parede. A v0.2.0. Falso em jogo, sempre.
	/// </summary>
	public static bool SemCaminhoDeTeste;

	/// <summary>Quantas buscas rodaram neste processo. SO PRA BANCADA.</summary>
	public static int BuscasDeTeste;

	private const int Lado = 2 * Raio + 1;
	private const int Celulas = Lado * Lado;
	private const int CapacidadeDaFila = TetoDeExpansoes * 8 + 16;
	private const float Diagonal = 1.41421356f;

	// O RASCUNHO. O carimbo de geracao evita limpar 3 mil celulas a cada busca: celula com carimbo velho nao foi vista.
	private static readonly int[] _carimbo = new int[Celulas];
	private static readonly float[] _g = new float[Celulas];
	private static readonly int[] _pai = new int[Celulas];
	private static readonly byte[] _estado = new byte[Celulas];   // 0 = so vista, 1 = aberta, 2 = fechada, 3 = barrada
	private static readonly int[] _fila = new int[CapacidadeDaFila];
	private static readonly float[] _filaF = new float[CapacidadeDaFila];
	private static int _naFila, _geracao;

	private static readonly int[] Dx = [1, -1, 0, 0, 1, 1, -1, -1];
	private static readonly int[] Dy = [0, 0, 1, -1, 1, -1, 1, -1];

	/// <summary>Onde um CORPO fica pra ter os pes no meio desta celula.</summary>
	public static Vec2 Centro(int cx, int cy) =>
		new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize * 0.5f,
			cy * ZoneCollision.TileSize + ZoneCollision.TileSize * 0.5f - MoveRules.FeetOffsetY);

	/// <summary>A celula em que estao os PES de um corpo nesta posicao.</summary>
	public static (int X, int Y) Celula(Vec2 corpo) =>
		((int)MathF.Floor(corpo.X / ZoneCollision.TileSize), (int)MathF.Floor((corpo.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize));

	/// <summary>
	/// O CAMINHO de <paramref name="de"/> ate <paramref name="para"/>, escrito em <paramref name="saida"/> do primeiro
	/// passo em diante (a celula de partida nao entra). Devolve quantos pontos escreveu; ZERO = nao ha por onde ir
	/// (ou ja se esta la).
	/// </summary>
	/// <param name="chegou">O ultimo ponto e o destino (ou encosta nele)? Falso = e so o mais perto que da pra chegar.</param>
	public static int Achar(ZoneCollision mapa, Vec2 de, Vec2 para, ModoDeTravessia modo, Span<Vec2> saida, out bool chegou)
	{
		chegou = false;
		if (SemCaminhoDeTeste || saida.Length == 0) return 0;

		(int sx, int sy) = Celula(de);
		(int gx, int gy) = Celula(para);
		if (sx == gx && sy == gy) { chegou = true; return 0; }

		int ox = sx - Raio, oy = sy - Raio;
		// O DESTINO FORA DA JANELA entra na borda dela: a busca vai ate onde enxerga, e o resto se replaneja de la.
		int mx = Math.Clamp(gx, ox, ox + Lado - 1), my = Math.Clamp(gy, oy, oy + Lado - 1);
		bool naJanela = mx == gx && my == gy;
		// O DESTINO QUE BARRA (o alvo dentro d'agua pra quem esta a pe, o ponto dentro de um muro): encostar basta.
		bool destinoLivre = naJanela && !mapa.Bloqueia(gx, gy, modo);
		if (naJanela && !destinoLivre && Math.Max(Math.Abs(sx - gx), Math.Abs(sy - gy)) <= 1) { chegou = true; return 0; }

		BuscasDeTeste++;
		if (++_geracao == int.MaxValue) { Array.Clear(_carimbo); _geracao = 1; }
		_naFila = 0;

		int inicio = (sy - oy) * Lado + (sx - ox);
		_carimbo[inicio] = _geracao;
		_g[inicio] = 0;
		_pai[inicio] = -1;
		_estado[inicio] = 1;   // (a celula de partida vale mesmo que barre: o corpo ESTA nela)
		float hDoInicio = H(sx, sy, mx, my);
		Por(inicio, hDoInicio);

		int melhor = inicio, fim = -1, abertas = 0;
		float melhorH = hDoInicio;

		while (_naFila > 0 && abertas < TetoDeExpansoes)
		{
			int atual = Tirar();
			if (_estado[atual] == 2) continue;
			_estado[atual] = 2;
			abertas++;

			int ax = atual % Lado + ox, ay = atual / Lado + oy;
			float h = H(ax, ay, mx, my);
			if (h < melhorH) { melhorH = h; melhor = atual; }

			bool aqui = destinoLivre ? ax == gx && ay == gy
					  : naJanela ? Math.Max(Math.Abs(ax - gx), Math.Abs(ay - gy)) <= 1
					  : ax == mx && ay == my;
			if (aqui) { fim = atual; break; }

			for (int d = 0; d < 8; d++)
			{
				int nx = ax + Dx[d], ny = ay + Dy[d];
				int lx = nx - ox, ly = ny - oy;
				if (lx < 0 || ly < 0 || lx >= Lado || ly >= Lado) continue;
				int viz = ly * Lado + lx;
				if (!Livre(mapa, viz, nx, ny, modo) || _estado[viz] == 2) continue;
				// A DIAGONAL so com as duas celulas do canto livres (a caixa dos pes encostaria nelas).
				if (d >= 4 && (mapa.Bloqueia(nx, ay, modo) || mapa.Bloqueia(ax, ny, modo))) continue;

				float g = _g[atual] + (d >= 4 ? Diagonal : 1f);
				if (_estado[viz] == 1 && g >= _g[viz]) continue;
				_g[viz] = g;
				_pai[viz] = atual;
				_estado[viz] = 1;
				if (_naFila >= CapacidadeDaFila) break;
				Por(viz, g + H(nx, ny, mx, my));
			}
		}

		if (fim >= 0) chegou = naJanela;
		else
		{
			// NAO CHEGOU: vale o mais perto que deu, se for mais perto do que eu ja estou (meia celula de folga).
			if (melhor == inicio || melhorH > hDoInicio - 0.5f) return 0;
			fim = melhor;
		}

		int n = 0;
		for (int i = fim; i != inicio; i = _pai[i]) n++;
		int escritos = Math.Min(n, saida.Length);
		if (escritos < n) chegou = false;   // (cortado: o fim do vetor nao e o destino)
		int k = n - 1;
		for (int i = fim; i != inicio; i = _pai[i], k--)
			if (k < escritos) saida[k] = Centro(i % Lado + ox, i / Lado + oy);
		return escritos;
	}

	/// <summary>A distancia octil: o menor numero de passos (retos e diagonais) entre duas celulas num mapa vazio.</summary>
	private static float H(int ax, int ay, int bx, int by)
	{
		int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
		return dx + dy + (Diagonal - 2f) * Math.Min(dx, dy);
	}

	/// <summary>Da pra pisar nesta celula? Pergunta ao mapa uma vez por busca e guarda no rascunho.</summary>
	private static bool Livre(ZoneCollision mapa, int indice, int cx, int cy, ModoDeTravessia modo)
	{
		if (_carimbo[indice] != _geracao)
		{
			_carimbo[indice] = _geracao;
			_g[indice] = float.MaxValue;
			_estado[indice] = mapa.Bloqueia(cx, cy, modo) ? (byte)3 : (byte)0;
		}
		return _estado[indice] != 3;
	}

	// ---- a fila de prioridade: um monte binario de (celula, f), com repeticao (a celula ja fechada se pula ao sair) ----
	private static void Por(int celula, float f)
	{
		int i = _naFila++;
		while (i > 0)
		{
			int pai = (i - 1) >> 1;
			if (_filaF[pai] <= f) break;
			_fila[i] = _fila[pai];
			_filaF[i] = _filaF[pai];
			i = pai;
		}
		_fila[i] = celula;
		_filaF[i] = f;
	}

	private static int Tirar()
	{
		int topo = _fila[0];
		int ultima = _fila[--_naFila];
		float f = _filaF[_naFila];
		int i = 0;
		while (true)
		{
			int filho = 2 * i + 1;
			if (filho >= _naFila) break;
			if (filho + 1 < _naFila && _filaF[filho + 1] < _filaF[filho]) filho++;
			if (_filaF[filho] >= f) break;
			_fila[i] = _fila[filho];
			_filaF[i] = _filaF[filho];
			i = filho;
		}
		_fila[i] = ultima;
		_filaF[i] = f;
		return topo;
	}
}
