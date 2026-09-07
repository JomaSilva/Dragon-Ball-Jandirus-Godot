namespace Jandirus.Core.World;

/// <summary>
/// UMA CELULA QUE LEVA A OUTRO MAPA -- a boca da caverna, a escada do Templo, a saída do Inferno.
///
/// ============================ ELA NAO E UMA PORTA ============================
/// A porta abre e continua no mesmo lugar; a passagem TROCA DE MUNDO. No BYOND as duas usam o mesmo
/// gancho (`Enter`), e por isso é fácil confundi-las -- a diferença está numa linha:
/// `M.loc = locate(x, y, OUTRO_Z)`.
///
/// O que ela carrega é o DESTINO, e destino não cabe num tile: por isso ela sai do mapa como lista
/// (`.passagens`), do mesmo jeito que as portas e as máquinas.
/// =============================================================================
/// </summary>
public sealed class Passagem
{
	/// <summary>A celula, em coordenadas de tile da zona de ORIGEM.</summary>
	public int X, Y;

	/// <summary>A zona de destino -- a chave do <see cref="ZoneCatalog"/>.</summary>
	public string Zona = "";

	/// <summary>Onde o corpo aparece na zona de destino, em pixels.</summary>
	public float Dx, Dy;

	/// <summary>O que o jogador lê antes de entrar ("Caverna da Terra").</summary>
	public string Nome = "";

	/// <summary>
	/// O CORPO ESTA DANDO O PASSO QUE ENTRA NESTA CELULA? E o `Enter()` do BYOND, em pixel: a caixa
	/// dos pes deslocada UM TILE no rumo em que o corpo olha toca a celula.
	///
	/// ============================ SO O PASSO QUE ENTRA, E NAO O ENCOSTO ============================
	/// A regra das portas (`PortasDaZona.VaiEntrar`) tambem aceita a caixa PARADA encostando na
	/// celula, e la isso e certo: porta abre pra quem chega perto. Aqui nao: a boca fica LACRADA
	/// (ver `ZoneCollision.Selar`), entao o corpo encosta nela toda vez que anda ate a parede -- e
	/// quem anda rente a beirada da boca, indo pro lado, encostaria sem querer entrar. Contar so o
	/// passo projetado no rumo do olhar e o que faz "andar pra dentro da caverna" atravessar e
	/// "passar na frente dela" nao.
	///
	/// O deslocamento e de um tile inteiro: com a boca lacrada o corpo para na borda dela, e dali a
	/// caixa projetada cai bem no meio da celula. De um tile de distancia ela toca a borda -- e o
	/// BYOND tambem dispara do tile vizinho, na tentativa do passo.
	/// =============================================================================================
	/// </summary>
	public bool NoPasso(Vec2 pos, Facing olhar) => NoPasso(pos, olhar, X, Y);

	public static bool NoPasso(Vec2 pos, Facing olhar, int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		Vec2 passo = olhar switch
		{
			Facing.North => new Vec2(0, -T),
			Facing.South => new Vec2(0, T),
			Facing.East => new Vec2(T, 0),
			_ => new Vec2(-T, 0),
		};
		Vec2 p = pos + passo;
		float px0 = p.X - MoveRules.BodyHalfW, px1 = p.X + MoveRules.BodyHalfW;
		float py0 = p.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH;
		float py1 = p.Y + MoveRules.FeetOffsetY + MoveRules.BodyHalfH;
		float tx0 = cx * T, ty0 = cy * T;
		return px1 > tx0 && px0 < tx0 + T && py1 > ty0 && py0 < ty0 + T;
	}

	/// <summary>Quantos tiles de distancia (Chebyshev) desta boca ate a celula dada.</summary>
	public int Longe(int cx, int cy) => Math.Max(Math.Abs(X - cx), Math.Abs(Y - cy));

	/// <summary>Ate quantos tiles da chegada do DM a boca de VOLTA e procurada. Ver <see cref="Chegada"/>.</summary>
	public const int AlcanceDaVolta = 3;

	/// <summary>
	/// ONDE O CORPO APARECE DO OUTRO LADO: um tile NA FRENTE da boca de volta.
	///
	/// ============================ O DM CRAVA A CHEGADA A MAO, E ERRA UMA ============================
	/// Cada `Enter()` de passagem do original tem um `locate(x, y, z)` escrito a mao
	/// (`Turfs.dm:1431-1475`). Em sete das oito cavernas o mapeador escreveu exatamente a celula na
	/// frente da boca de volta; na saida 1 da caverna da Terra (`Underground_E_Exit`,
	/// `Turfs.dm:1452-1455`) ele escreveu `locate(267,388,1)`, que fica dois tiles ao sul e um a
	/// oeste da boca `ECaveEntrance1` (268,390) -- o corpo saia de lado, e nao na frente.
	///
	/// O dono pediu a regra, nao os numeros: "sair de cavernas colocando o personagem 1 tile na
	/// frente da entrada". Entao a chegada e DERIVADA DO MAPA: entre as bocas de volta ate
	/// <see cref="AlcanceDaVolta"/> tiles do ponto que o DM cravou, o vizinho ortogonal LIVRE mais
	/// perto desse ponto -- que e a propria celula do DM nas sete que ele acertou, e a celula da
	/// frente na que ele errou. DIVERGENCIA DECLARADA nessa uma.
	///
	/// O PAR (boca, vizinho) e escolhido de uma vez, e nao "a boca mais perto, e depois o vizinho
	/// dela": a escada do Templo tem TRES bocas lado a lado, todas a um tile do ponto do DM, e
	/// escolher a primeira delas mandava o corpo pro lado da escada em vez de pra frente dela.
	///
	/// A BEIRADA DO MAPA VALE como chegada (`ServeDeChao(mesmoNaBorda: true)`): a escada do Templo
	/// esta nas duas ultimas fileiras do mapa, e e la que o DM deposita quem sobe.
	///
	/// Quando nao ha boca de volta com frente livre vale o ponto do DM -- inclusive se ele e ceu ou
	/// agua: os pads do Templo Sagrado (`Turfs.dm:179-189`, os `Special/Teleporter` do `.dmm`)
	/// depositam o corpo no ceu de fora do palacio, e la quem nao voa cai, como no original. So
	/// PAREDE e recusada. E se o proprio ponto do DM e parede, a FRENTE DELE: a saida da Sala do
	/// Tempo cai em cima do teleporter `tohbtc` do Templo (`Turfs.dm:157-165`), denso e fora da
	/// lista de passagens, e o pad de dentro do palacio cai num `Wall23` (BYOND (44,401,12)) -- no
	/// BYOND `loc = locate()` nao passa pelo `Enter()` e o corpo fica DENTRO da parede; aqui ele
	/// nasce um tile ao lado dela. So depois disso vale o chao livre mais proximo -- nunca dentro
	/// de parede, que era a outra metade da queixa.
	///
	/// "NA FRENTE" desempata pro SUL: as bocas sao desenhadas na fileira da parede, com a abertura
	/// virada pra baixo, e um corpo que sai dela sai por baixo.
	/// ================================================================================================
	/// </summary>
	/// <param name="mapa">A colisao da zona de DESTINO, ja com as bocas lacradas.</param>
	/// <param name="voltas">As passagens da zona de destino.</param>
	/// <param name="origem">O nome da zona de onde o corpo vem -- so contam as bocas que levam de volta a ela.</param>
	/// <param name="dm">O ponto que o DM cravou, em pixels.</param>
	public static Vec2 Chegada(ZoneCollision mapa, IReadOnlyList<Passagem> voltas, string origem, Vec2 dm)
	{
		const int T = ZoneCollision.TileSize;
		int cx = (int)MathF.Floor(dm.X / T), cy = (int)MathF.Floor(dm.Y / T);

		// sul, oeste, leste, norte: a ordem e o desempate ("na frente" e pra baixo)
		(int dx, int dy)[] rumos = [(0, 1), (-1, 0), (1, 0), (0, -1)];
		(int X, int Y)? escolha = null;
		int perto = int.MaxValue;
		foreach (Passagem v in voltas)
		{
			if (!string.Equals(v.Zona, origem, StringComparison.OrdinalIgnoreCase)) continue;
			if (v.Longe(cx, cy) > AlcanceDaVolta) continue;
			foreach ((int dx, int dy) in rumos)
			{
				int x = v.X + dx, y = v.Y + dy;
				if (!mapa.ServeDeChao(x, y, mesmoNaBorda: true)) continue;
				int d = Math.Abs(x - cx) + Math.Abs(y - cy);
				if (d >= perto) continue;
				perto = d;
				escolha = (x, y);
			}
		}
		if (escolha is { } e) return mapa.CentroDaCelula(e.X, e.Y);

		// O PONTO DO DM, se nao e parede: chao, beirada, ceu ou agua -- a escolha e dele.
		if (!mapa.BlockedCell(cx, cy)) return dm;

		// O PONTO DO DM E PAREDE (o teleporter denso, o pad dentro do muro): a frente dele -- chao
		// primeiro, e so depois o que nao e parede -- na mesma ordem de desempate.
		foreach ((int dx, int dy) in rumos)
			if (mapa.ServeDeChao(cx + dx, cy + dy, mesmoNaBorda: true))
				return mapa.CentroDaCelula(cx + dx, cy + dy);
		foreach ((int dx, int dy) in rumos)
			if (!mapa.BlockedCell(cx + dx, cy + dy))
				return mapa.CentroDaCelula(cx + dx, cy + dy);

		return mapa.PontoLivrePerto(dm, 6);
	}

	/// <summary>
	/// Le a lista `.passagens` de uma zona.
	///
	/// Parser a mao pelo mesmo motivo do resto do Core: nao ha dependencia externa aqui, e o
	/// formato e nosso -- uma lista plana de objetos com seis campos.
	/// </summary>
	public static List<Passagem> Parse(string json)
	{
		var saida = new List<Passagem>();
		int i = 0;
		while (true)
		{
			int a = json.IndexOf('{', i);
			if (a < 0) break;
			int b = json.IndexOf('}', a);
			if (b < 0) break;

			string bloco = json[(a + 1)..b];
			var p = new Passagem
			{
				X = (int)Num(bloco, "x"),
				Y = (int)Num(bloco, "y"),
				Zona = Str(bloco, "zona"),
				Dx = (float)Num(bloco, "dx"),
				Dy = (float)Num(bloco, "dy"),
				Nome = Str(bloco, "nome"),
			};
			if (p.Zona.Length > 0) saida.Add(p);
			i = b + 1;
		}
		return saida;
	}

	private static string Str(string bloco, string campo)
	{
		int i = bloco.IndexOf($"\"{campo}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		int b = bloco.IndexOf('"', a + 1);
		return b < 0 ? "" : bloco[(a + 1)..b];
	}

	private static double Num(string bloco, string campo)
	{
		int i = bloco.IndexOf($"\"{campo}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int dp = bloco.IndexOf(':', i);
		int fim = dp + 1;
		while (fim < bloco.Length && (char.IsDigit(bloco[fim]) || bloco[fim] is ' ' or '-' or '.')) fim++;
		return double.TryParse(bloco[(dp + 1)..fim].Trim(),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
	}
}
