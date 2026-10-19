namespace Jandirus.Core.World;

/// <summary>
/// ============================ O POUSO NUM LUGAR SORTEADO, EM TERRA FIRME (2026-10-18) ============================
/// O dono: *"faça a pod e a nave quando chegarem no planeta pousarem em algum local aleatorio com terra firme"*. Quem desce do espaco
/// PILOTANDO (o pod, a Capital Ship ao leme, a Capital Ship seguindo sozinha) pousa num ponto sorteado do mapa INTEIRO -- e nao mais no
/// berco do pre-feito nem na chegada que o gerador abriu. Quem desce voando sozinho continua chegando no berco.
///
/// TERRA FIRME SAO QUATRO PERGUNTAS, e as tres primeiras ja existiam no mapa:
///   * a celula SERVE DE CHAO (<see cref="ZoneCollision.ServeDeChao"/>: nem parede, nem obra, nem beirada, nem agua/lava, nem nuvem);
///   * ela fica A CEU ABERTO (<see cref="ZoneCollision.NasceuDentro"/>): um pod nao cai dentro do Banco, e a Capital Ship nao pousa no
///     salao do castelo;
///   * as duas valem EM VOLTA, na folga do veiculo: o casco inteiro sobre chao, e nao meio casco sobre o lago (a praia de um tile e a
///     ilhota nao servem);
///   * e o chao nao e um BOLSAO: andando a pe dali se chega a pelo menos <see cref="RegiaoMinima"/> celulas (busca em largura). Quem desce
///     da nave sem saber voar sai andando, e nao fica num patio cercado de muralha.
/// E LONGE DE GENTE (<see cref="LongeDeGente"/>): nada cai em cima de ninguem.
///
/// O SORTEIO TEM TETO (<see cref="Tentativas"/>). Nenhum ponto servindo -- a estacao espacial e coberta inteira, um mundo que e so
/// oceano -- devolve nulo, e quem chama pousa no ponto de sempre: o defeito aparece como "pousou no berco", nunca como "pousou dentro
/// da pedra".
///
/// FUNCAO PURA, com a sorte de fora: a bancada passa uma semente e confere o ponto; o servidor passa a dele.
///
/// DIVERGENCIA DECLARADA (o pedido do dono): o DM pousa no miolo do mapa, `locate(rand(240,260),rand(240,260),z)`
/// (`Planets.dm:184-215`), e no planeta gerado num dos `land_spots` de planicie sem NPC a 8 tiles (`ProceduralSpace.dm:618-631`).
/// ===============================================================================================================
/// </summary>
public static class PousoSorteado
{
	/// <summary>Quantos pontos o sorteio tenta. Na Terra (44% agua) os primeiros ja servem; o teto e pro mundo que nao tem onde.</summary>
	public const int Tentativas = 400;

	/// <summary>A folga do POD: dois tiles pra cada lado da celula (5x5) -- o miolo da cratera que ele abre.</summary>
	public const int FolgaDoPod = 2;

	/// <summary>A folga da CAPITAL SHIP: tres tiles pra cada lado (7x7) -- o casco tem 128x138 px.</summary>
	public const int FolgaDaNaveGrande = 3;

	/// <summary>Quantas celulas de chao ligadas a pe fazem o lugar nao ser um bolsao cercado.</summary>
	public const int RegiaoMinima = 200;

	/// <summary>A distancia minima de qualquer corpo da zona, em pixels: tres tiles.</summary>
	public const float LongeDeGente = 3 * ZoneCollision.TileSize;

	/// <summary>
	/// UM PONTO SORTEADO EM TERRA FIRME, no centro da celula (a quina de uma celula encostada em parede ja nasceria em violacao pro
	/// `MoveRules` -- a nota do <see cref="ZoneCollision.PontoLivrePerto"/>). Nulo = nenhum dos <see cref="Tentativas"/> serviu.
	/// </summary>
	/// <param name="folga">Tiles de terra firme exigidos pra cada lado: <see cref="FolgaDoPod"/> ou <see cref="FolgaDaNaveGrande"/>.</param>
	/// <param name="corpos">Onde estao os corpos da zona (gente e NPC): o ponto fica a <see cref="LongeDeGente"/> de todos.</param>
	public static Vec2? Sortear(ZoneCollision mapa, Random sorte, int folga, IReadOnlyList<Vec2>? corpos = null)
	{
		// A margem do sorteio ja tira a beirada inteira da folga: o `ServeDeChao` recusaria a celula de qualquer jeito, e assim nenhuma
		// tentativa e gasta nela.
		int margem = ZoneCollision.MargemDaBorda + folga;
		if (mapa.Width <= 2 * margem || mapa.Height <= 2 * margem) return null;

		for (int i = 0; i < Tentativas; i++)
		{
			int cx = sorte.Next(margem, mapa.Width - margem);
			int cy = sorte.Next(margem, mapa.Height - margem);
			if (!TerraFirme(mapa, cx, cy, folga)) continue;
			Vec2 centro = mapa.CentroDaCelula(cx, cy);
			if (corpos != null && PertoDeAlguem(centro, corpos)) continue;
			if (!LigadaAoMundo(mapa, cx, cy)) continue;
			return centro;
		}
		return null;
	}

	/// <summary>ESTA CELULA E CHAO A CEU ABERTO? -- a pergunta de cada celula da folga, e de cada passo da busca em largura.</summary>
	public static bool ChaoAberto(ZoneCollision mapa, int cx, int cy) => mapa.ServeDeChao(cx, cy) && !mapa.NasceuDentro(cx, cy);

	/// <summary>A celula E a folga inteira em volta dela sao chao a ceu aberto?</summary>
	public static bool TerraFirme(ZoneCollision mapa, int cx, int cy, int folga)
	{
		for (int dy = -folga; dy <= folga; dy++)
			for (int dx = -folga; dx <= folga; dx++)
				if (!ChaoAberto(mapa, cx + dx, cy + dy)) return false;
		return true;
	}

	/// <summary>
	/// O CHAO DESTA CELULA SE LIGA A PELO MENOS <see cref="RegiaoMinima"/> CELULAS, A PE? Busca em largura pelos quatro lados, que para assim
	/// que conta o bastante -- o custo e de no maximo `RegiaoMinima` celulas por tentativa, e nao do mapa inteiro.
	/// </summary>
	public static bool LigadaAoMundo(ZoneCollision mapa, int cx, int cy)
	{
		var vistas = new HashSet<int> { cy * mapa.Width + cx };
		var fila = new Queue<(int X, int Y)>();
		fila.Enqueue((cx, cy));
		while (fila.Count > 0)
		{
			(int x, int y) = fila.Dequeue();
			for (int lado = 0; lado < 4; lado++)
			{
				int nx = x + (lado == 0 ? 1 : lado == 1 ? -1 : 0);
				int ny = y + (lado == 2 ? 1 : lado == 3 ? -1 : 0);
				// (fora do mapa o `ChaoAberto` ja e falso -- parede ou beirada --, entao nenhum indice negativo entra no conjunto)
				if (!ChaoAberto(mapa, nx, ny) || !vistas.Add(ny * mapa.Width + nx)) continue;
				if (vistas.Count >= RegiaoMinima) return true;
				fila.Enqueue((nx, ny));
			}
		}
		return false;
	}

	private static bool PertoDeAlguem(Vec2 ponto, IReadOnlyList<Vec2> corpos)
	{
		foreach (Vec2 c in corpos)
			if ((c - ponto).Length < LongeDeGente) return true;
		return false;
	}
}
