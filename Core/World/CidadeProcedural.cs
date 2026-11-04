namespace Jandirus.Core.World;

/// <summary>
/// ============================ A CIDADE DE UM MUNDO COM CIVILIZACAO (o pedido 227, 2026-11-02) ============================
/// O dono: *"com isso vc pode fazer cidades procedurais com casas e estradas ligando essas casas etc"*.
///
/// ELA E DO CHAO, e por isso nasce no `GeradorDeTerreno` (logo depois do pomar e da porta da Sala, e antes do blob de colisao): a casa
/// e parede no mapa que o servidor le e no que o cliente desenha, pela mesma seed, sem um byte no fio -- o mesmo caminho da montanha.
/// A casa e DURA (o plano do que nao se quebra, `ZoneCollision.DefinirDuro`): uma explosao nao abre um buraco no desenho que o cliente
/// continuaria pintando.
///
/// O DESENHO E O DAS CASAS DO JOGO, vistas de cima: o TELHADO em cima e a FACHADA na fileira de baixo, com a porta no meio e as janelas
/// dos lados (os turfs do DM: `Roofs`, `Walls`, `turfs`, `Turfs 5`). A porta e desenho -- a casa nao abre. Um ESTILO por cidade (madeira,
/// palha, pedra, tijolo, moderna, metal, areia, escura), sorteado pela seed.
///
/// AS ESTRADAS saem da PRACA (o meio da cidade) pra porta de cada casa e pro POUSO (onde se chega no planeta): quem cai no mundo acha a
/// cidade seguindo a estrada. O caminho e o mais curto com POUCAS CURVAS (cada curva custa como tres passos), pra sair em trechos retos e
/// nao em escadinha. A estrada passa por cima de mata (a arvore sai), e nunca por agua, montanha, rocha grande, macieira ou casa.
///
/// A CIDADE FICA LONGE DO POUSO (14 a 60 tiles): a clareira da chegada e a macieira dela continuam como eram, e quem ja ergueu alguma
/// coisa perto do pouso de um mundo que agora tem cidade nao a encontra embaixo de uma casa.
/// ======================================================================================================================
/// </summary>
public sealed class CidadeProcedural
{
	/// <summary>Uma casa: o canto de cima (em celulas), o tamanho (a ultima fileira e a fachada) e a coluna da porta.</summary>
	public readonly record struct Casa(int X, int Y, int Largura, int Altura, int PortaX)
	{
		/// <summary>A celula da porta (na fachada).</summary>
		public (int X, int Y) Porta => (PortaX, Y + Altura - 1);

		/// <summary>A SOLEIRA: a celula de chao logo ao sul da porta, onde a estrada chega.</summary>
		public (int X, int Y) Soleira => (PortaX, Y + Altura);

		public bool Contem(int x, int y) => x >= X && x < X + Largura && y >= Y && y < Y + Altura;
	}

	/// <summary>O desenho de uma cidade: o telhado, a parede da fachada, a janela, a porta e a estrada.</summary>
	public readonly record struct Estilo(string Nome, TileVisual Telhado, TileVisual Parede, TileVisual Janela, TileVisual Porta,
										 TileVisual Estrada);

	/// <summary>
	/// OS ESTILOS, todos com turfs que o tileset do jogo ja tem (`Assets/Data/tiles.json`), escolhidos na foto montada com eles: a
	/// janela de madeira (`Walls/Wall1`) cai bem em todo tipo de parede.
	/// </summary>
	public static readonly Estilo[] Estilos =
	[
		new("madeira", new("turfs", "rooftop"), new("turfs", "hsiding"), new("Walls", "Wall1"), new("turfs", "door4house"), new("Turfs 5", "gravel")),
		new("palha", new("turfs", "rooftop3"), new("Turfs 5", "crudewall"), new("Walls", "Wall1"), new("turfs", "door4house"), new("Turfs 5", "gravel")),
		new("pedra", new("Roofs", "Roof8"), new("Walls", "Wall8"), new("Walls", "Wall1"), new("turfs", "door4house"), new("turf5", "Sidewalk")),
		new("tijolo", new("Roofs", "Roof7"), new("Walls", "Wall10"), new("Walls", "Wall1"), new("turfs", "door4house"), new("turf5", "Sidewalk")),
		new("moderna", new("Roofs", "Roof6"), new("Turfs 5", "btile"), new("Walls", "Wall1"), new("Turfs 5", "doorshut"), new("turf5", "Sidewalk")),
		new("metal", new("Roofs", "Roof6B"), new("Walls", "Wall4"), new("Walls", "Wall6"), new("Door1", "Closed"), new("turf5", "Floor")),
		new("areia", new("Roofs", "Roof9"), new("Walls", "Wall2"), new("Walls", "Wall1"), new("turfs", "door4house"), new("turf5", "teracotta")),
		new("escura", new("Roofs", "Roof11"), new("Walls", "Wall9"), new("Walls", "Wall1"), new("turfs", "door4house"), new("Turfs 5", "gravel")),
	];

	// OS NUMEROS DO TRACADO
	/// <summary>A distancia (em tiles) entre o pouso e o meio da cidade: a mais perto que cabe, entre estas duas.</summary>
	public const int DistanciaMinima = 14, DistanciaMaxima = 60;

	/// <summary>O meio-lado da area da cidade (em tiles): 25 de largura por 19 de altura.</summary>
	public const int MeiaLargura = 12, MeiaAltura = 9;

	/// <summary>A fracao da area que tem de ser chao de erguer casa pra cidade nascer ali.</summary>
	public const double AreaBoaMinima = 0.7;

	/// <summary>Quantas casas no minimo (abaixo disso o mundo fica sem cidade) e a faixa sorteada.</summary>
	public const int CasasMinimas = 3, CasasDe = 5, CasasAte = 10;

	/// <summary>Quanto uma curva custa, em passos de estrada.</summary>
	public const int CustoDaCurva = 3;

	public required IReadOnlyList<Casa> Casas { get; init; }
	public required IReadOnlySet<int> Estradas { get; init; }
	public required int CentroX { get; init; }
	public required int CentroY { get; init; }
	public required Estilo Desenho { get; init; }
	public required int Largura { get; init; }

	/// <summary>A estrada chegou ate o pouso?</summary>
	public required bool LigadaAoPouso { get; init; }

	public bool EhEstrada(int x, int y) => Estradas.Contains(y * Largura + x);

	/// <summary>O tile desta celula da cidade, ou nulo (a celula nao e da cidade).</summary>
	public TileVisual? TileEm(int x, int y)
	{
		foreach (Casa c in Casas)
		{
			if (!c.Contem(x, y)) continue;
			if (y < c.Y + c.Altura - 1) return Desenho.Telhado;
			if (x == c.PortaX) return Desenho.Porta;
			// as janelas: duas casas pra cada lado da porta, se couberem dentro da fachada (nunca no canto)
			if ((x == c.PortaX - 2 || x == c.PortaX + 2) && x > c.X && x < c.X + c.Largura - 1) return Desenho.Janela;
			return Desenho.Parede;
		}
		return EhEstrada(x, y) ? Desenho.Estrada : null;
	}

	/// <summary>Todas as celulas desenhadas da cidade (as casas e as estradas), com o tile de cada uma.</summary>
	public IEnumerable<(int X, int Y, TileVisual Tile)> Celulas()
	{
		var vistas = new HashSet<int>();
		foreach (Casa c in Casas)
			for (int y = c.Y; y < c.Y + c.Altura; y++)
				for (int x = c.X; x < c.X + c.Largura; x++)
					if (vistas.Add(y * Largura + x) && TileEm(x, y) is { } t) yield return (x, y, t);
		foreach (int i in Estradas)
			if (vistas.Add(i)) yield return (i % Largura, i / Largura, Desenho.Estrada);
	}

	/// <summary>
	/// ERGUE A CIDADE nas grades do gerador (as mesmas que ele entrega depois no `TerrenoGerado`): escolhe o lugar, poe as casas, traca as
	/// estradas e escreve no mapa -- a casa vira parede dura, a estrada e a volta das casas perdem a mata. Nulo se o mundo nao tem onde
	/// (um planeta de agua e rocha): ele fica sem cidade, e o povo nasce do mesmo jeito em volta do pouso.
	/// </summary>
	public static CidadeProcedural? Erguer(int w, int h, byte[] chao, byte[] cobertura, byte[] bits, byte[] agua, byte[] duro,
										  int sx, int sy, ulong seed, IReadOnlyList<(int X, int Y)> macieiras, PortaNoTerreno? portaDaSala)
	{
		bool Bit(byte[] b, int i) => (b[i >> 3] & (1 << (i & 7))) != 0;
		var reservado = new HashSet<int>();
		foreach ((int mx, int my) in macieiras)
			for (int dy = -1; dy <= 1; dy++)
				for (int dx = -1; dx <= 1; dx++)
					reservado.Add((my + dy) * w + mx + dx);
		if (portaDaSala is { } ps)
			for (int dy = -1; dy <= 2; dy++)
				for (int dx = -1; dx <= 1; dx++)
					reservado.Add((ps.Y + dy) * w + ps.X + dx);

		// CHAO DE ANDAR: terra (praia, planicie, colina) sem agua, sem rocha grande, sem o que nao se quebra, sem macieira nem a porta da Sala.
		// A mata pode (ela sai).
		bool DeAndar(int x, int y)
		{
			if (x < 3 || y < 3 || x >= w - 3 || y >= h - 3) return false;
			int i = y * w + x;
			var classe = (ClasseDeTerreno)chao[i];
			return classe is ClasseDeTerreno.Praia or ClasseDeTerreno.Planicie or ClasseDeTerreno.Colina
				   && !Bit(agua, i) && !Bit(duro, i) && cobertura[i] != (byte)CoberturaDeTerreno.RochaGrande && !reservado.Contains(i);
		}

		// CHAO DE ERGUER CASA: de andar, e longe da clareira do pouso
		bool DeErguer(int x, int y) => DeAndar(x, y) && Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) > 4;

		// ---- 1. O LUGAR: o mais perto do pouso em que 70% da area e chao de erguer ----
		var sorte = new Random((int)(Espaco.Misturar(seed, Espaco.Hash64("cidade"), 0) & 0x7FFFFFFF));
		double giro = sorte.NextDouble() * Math.PI * 2;
		int area = (2 * MeiaLargura + 1) * (2 * MeiaAltura + 1);
		(int X, int Y, int Boa)? melhor = null;
		for (int r = DistanciaMinima; r <= DistanciaMaxima && melhor == null; r += 2)
		{
			for (int k = 0; k < 16; k++)
			{
				double a = giro + k * Math.PI / 8;
				int cx = sx + (int)Math.Round(Math.Cos(a) * r), cy = sy + (int)Math.Round(Math.Sin(a) * r);
				if (cx - MeiaLargura < 3 || cy - MeiaAltura < 3 || cx + MeiaLargura >= w - 3 || cy + MeiaAltura >= h - 3) continue;
				int boa = 0;
				for (int y = cy - MeiaAltura; y <= cy + MeiaAltura; y++)
					for (int x = cx - MeiaLargura; x <= cx + MeiaLargura; x++)
						if (DeErguer(x, y)) boa++;
				if (boa >= AreaBoaMinima * area && (melhor == null || boa > melhor.Value.Boa)) melhor = (cx, cy, boa);
			}
		}
		if (melhor is not { } centro) return null;
		int ax0 = centro.X - MeiaLargura, ax1 = centro.X + MeiaLargura, ay0 = centro.Y - MeiaAltura, ay1 = centro.Y + MeiaAltura;

		// ---- 2. AS CASAS: cada uma com uma volta de chao livre (por onde a estrada passa), longe da praca ----
		var ocupado = new HashSet<int>();
		for (int dy = -2; dy <= 2; dy++)
			for (int dx = -2; dx <= 2; dx++)
				ocupado.Add((centro.Y + dy) * w + centro.X + dx);
		var casas = new List<Casa>();
		int alvo = sorte.Next(CasasDe, CasasAte + 1);
		for (int tentativa = 0; tentativa < 400 && casas.Count < alvo; tentativa++)
		{
			int cw = sorte.Next(4, 8), ch = sorte.Next(3, 6);
			int x0 = sorte.Next(ax0, ax1 - cw + 2), y0 = sorte.Next(ay0, ay1 - ch + 1);
			bool cabe = true;
			for (int y = y0 - 1; y <= y0 + ch && cabe; y++)
				for (int x = x0 - 1; x <= x0 + cw && cabe; x++)
					cabe = DeErguer(x, y) && !ocupado.Contains(y * w + x);
			if (!cabe) continue;
			casas.Add(new Casa(x0, y0, cw, ch, x0 + cw / 2));
			for (int y = y0 - 1; y <= y0 + ch; y++)
				for (int x = x0 - 1; x <= x0 + cw; x++)
					ocupado.Add(y * w + x);
		}
		if (casas.Count < CasasMinimas) return null;

		// ---- 3. AS ESTRADAS: o caminho com poucas curvas da praca a cada soleira e ao pouso, numa caixa em volta da cidade e do pouso ----
		int bx0 = Math.Max(1, Math.Min(ax0, sx) - 6), bx1 = Math.Min(w - 2, Math.Max(ax1, sx) + 6);
		int by0 = Math.Max(1, Math.Min(ay0, sy) - 6), by1 = Math.Min(h - 2, Math.Max(ay1, sy) + 6);
		bool Passa(int x, int y)
		{
			if (x < bx0 || x > bx1 || y < by0 || y > by1) return false;
			foreach (Casa c in casas)
				if (c.Contem(x, y)) return false;
			// a clareira do pouso passa mesmo sem ser chao de erguer (e la que a estrada chega)
			return DeAndar(x, y) || (Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) <= 1 && !Bit(agua, y * w + x));
		}

		int[] ddx = [1, 0, -1, 0], ddy = [0, 1, 0, -1];
		var custo = new Dictionary<long, int>();
		var pai = new Dictionary<long, long>();
		var fila = new PriorityQueue<long, int>();
		long Estado(int x, int y, int d) => ((long)(y * w + x) << 2) | (uint)d;
		for (int d = 0; d < 4; d++)
		{
			long e0 = Estado(centro.X, centro.Y, d);
			custo[e0] = 0;
			fila.Enqueue(e0, 0);
		}
		while (fila.TryDequeue(out long e, out int c0))
		{
			if (custo.GetValueOrDefault(e, int.MaxValue) < c0) continue;
			int i = (int)(e >> 2), d0 = (int)(e & 3), x = i % w, y = i / w;
			for (int d = 0; d < 4; d++)
			{
				int nx = x + ddx[d], ny = y + ddy[d];
				if (!Passa(nx, ny)) continue;
				int c1 = c0 + 1 + (d == d0 ? 0 : CustoDaCurva);
				long ne = Estado(nx, ny, d);
				if (c1 >= custo.GetValueOrDefault(ne, int.MaxValue)) continue;
				custo[ne] = c1;
				pai[ne] = e;
				fila.Enqueue(ne, c1);
			}
		}

		var estradas = new HashSet<int>();
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if (Passa(centro.X + dx, centro.Y + dy)) estradas.Add((centro.Y + dy) * w + centro.X + dx);   // a praca
		bool Tracar(int tx, int ty)
		{
			long? fim = null;
			int melhorCusto = int.MaxValue;
			for (int d = 0; d < 4; d++)
				if (custo.TryGetValue(Estado(tx, ty, d), out int c) && c < melhorCusto) { melhorCusto = c; fim = Estado(tx, ty, d); }
			if (fim is not { } e) return false;
			while (true)
			{
				estradas.Add((int)(e >> 2));
				if (!pai.TryGetValue(e, out long anterior)) break;
				e = anterior;
			}
			return true;
		}
		foreach (Casa c in casas) Tracar(c.Soleira.X, c.Soleira.Y);
		bool aoPouso = false;
		foreach ((int dx, int dy) in new[] { (0, 1), (1, 0), (-1, 0), (0, -1), (0, 0) })
			if (!aoPouso && Tracar(sx + dx, sy + dy)) aoPouso = true;

		// ---- 4. NO MAPA: a casa e parede dura sem mata; a volta dela e a estrada perdem a mata (e o bit que a arvore punha) ----
		void Limpar(int i)
		{
			if (cobertura[i] is (byte)CoberturaDeTerreno.Arvore or (byte)CoberturaDeTerreno.ArvoreAcento or (byte)CoberturaDeTerreno.Planta)
			{
				cobertura[i] = (byte)CoberturaDeTerreno.Nada;
				bits[i >> 3] &= (byte)~(1 << (i & 7));
			}
		}
		foreach (Casa c in casas)
		{
			for (int y = c.Y - 1; y <= c.Y + c.Altura; y++)
				for (int x = c.X - 1; x <= c.X + c.Largura; x++)
					Limpar(y * w + x);
			for (int y = c.Y; y < c.Y + c.Altura; y++)
				for (int x = c.X; x < c.X + c.Largura; x++)
				{
					int i = y * w + x;
					bits[i >> 3] |= (byte)(1 << (i & 7));
					duro[i >> 3] |= (byte)(1 << (i & 7));
				}
		}
		foreach (int i in estradas) Limpar(i);

		return new CidadeProcedural
		{
			Casas = casas,
			Estradas = estradas,
			CentroX = centro.X,
			CentroY = centro.Y,
			Desenho = Estilos[(int)(Espaco.Misturar(seed, Espaco.Hash64("estilo da cidade"), 0) % (ulong)Estilos.Length)],
			Largura = w,
			LigadaAoPouso = aoPouso,
		};
	}
}
