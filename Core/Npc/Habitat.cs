using Jandirus.Core.World;

namespace Jandirus.Core.Npc;

/// <summary>
/// AS MANIVELAS DO ESPALHAMENTO -- a secao `espalhamento` do `rotina.json`.
/// </summary>
public sealed class Espalhamento
{
	/// <summary>
	/// O lado do BLOCO que conta como "uma regiao" na hora de espalhar. 32 tiles num mapa de 500
	/// dao ate 16x16 blocos; os que tem terra viram regioes, e os habitantes se dividem entre elas.
	/// </summary>
	public int LadoDaRegiaoEmTiles = 32;

	/// <summary>
	/// A distancia minima (Chebyshev, em tiles) entre duas VAGAS. E o passo do reticulado: duas
	/// vagas distintas nunca ficam a menos disto uma da outra, entao dois habitantes nunca nascem
	/// empilhados nem colados.
	/// </summary>
	public int DistanciaMinimaEmTiles = 4;

	/// <summary>
	/// Um bloco com MENOS vagas que isto nao vira regiao: e uma beirada de praia, uma ilhota, um
	/// degrau. Nascer ali seria nascer num canto sem onde andar.
	/// </summary>
	public int MinimoDeVagasPorRegiao = 3;

	/// <summary>
	/// Uma componente de terra (ilha, continente, patio) so entra no habitat com pelo menos ESTAS
	/// celulas. A do berco entra sempre. 400 celulas = um quadrado de 20x20: uma ilha de Namek
	/// entra, um patio murado de castelo ou um telhado nao. Ver a regra 1 do <see cref="Habitat"/>.
	/// </summary>
	public int MinimoDeCelulasPorComponente = 400;

	public List<string> Problemas()
	{
		var p = new List<string>();
		if (LadoDaRegiaoEmTiles < 4) p.Add($"espalhamento.ladoDaRegiaoEmTiles = {LadoDaRegiaoEmTiles}: menor que 4 nao e regiao, e celula");
		if (DistanciaMinimaEmTiles < 1) p.Add($"espalhamento.distanciaMinimaEmTiles = {DistanciaMinimaEmTiles}: precisa ser >= 1");
		if (DistanciaMinimaEmTiles > LadoDaRegiaoEmTiles) p.Add("espalhamento: a distancia minima e maior que o lado da regiao -- regiao sem vaga nenhuma");
		if (MinimoDeVagasPorRegiao < 1) p.Add($"espalhamento.minimoDeVagasPorRegiao = {MinimoDeVagasPorRegiao}: precisa ser >= 1");
		if (MinimoDeCelulasPorComponente < 1) p.Add($"espalhamento.minimoDeCelulasPorComponente = {MinimoDeCelulasPorComponente}: precisa ser >= 1");
		return p;
	}
}

/// <summary>
/// ONDE UM HABITANTE PODE NASCER NUMA ZONA -- a terra NAVEGAVEL ligada ao berco, cortada em regioes
/// e reticulada em VAGAS, numa ordem de nascimento fixa.
///
/// ============================ O PEDIDO E O QUE ELE SUBSTITUI ============================
/// O dono pediu: *"spawn de NPCs distribuido pelo planeta inteiro (so em terra, navegavel, sem
/// colisao), distribuicao aproximadamente uniforme (com distancia minima/regioes)"*. O que existia
/// era o `planet_spawn_turf` do original (PlanetPopulation.dm:283), `rand(-10,10)` em volta de um
/// SpawnPoint -- e como o port tem UM ponto de chegada por zona, os quarenta habitantes da Terra
/// nasciam todos num quadrado de 49x49 em volta do (249,250). DIVERGENCIA DECLARADA do original,
/// a pedido: aqui a populacao cobre o planeta.
///
/// ============================ AS QUATRO REGRAS, E DE ONDE VEM CADA UMA ============================
///   1. TERRA NAVEGAVEL = as componentes conexas pelo `ServeDeChao` (4 vizinhos) com pelo menos
///      `MinimoDeCelulasPorComponente` celulas, mais a do berco, sempre. Uma ilha de Namek entra
///      (o planeta inteiro e ilhas, e a populacao e do planeta); um patio murado, um telhado, um
///      degrau nao entram, porque um corpo nascido la viveria num canto sem onde andar. O
///      `ServeDeChao` ja recusa agua, nuvem, borda do mapa, parede, obra e boca lacrada, entao a
///      lista de "onde nao nascer" nao vive aqui.
///   2. VAGAS = o reticulado de passo `DistanciaMinima` sobre essa terra. Duas vagas distintas
///      distam pelo menos o passo: e assim que "distancia minima" e garantida SEM olhar quem ja
///      nasceu -- ver a regra 4.
///   3. REGIOES = blocos de `LadoDaRegiao`; os que tem vagas de sobra contam. A ordem de nascimento
///      visita as regioes pela MENOR razao (tomadas / vagas): as primeiras N vagas caem em N
///      regioes diferentes, e depois disso cada regiao recebe na proporcao da sua area -- uniforme
///      POR AREA, e nao por regiao (uma praia de 3 vagas nao vale uma planicie de 300).
///   4. A ORDEM E FIXA, e a vaga do pedido `lugar` e `sequencia[lugar mod vagas]`. Isto e o que
///      mantem a promessa do `PontoDeHabitante` que o Embaralho escreveu por extenso: **funcao
///      pura de (semente do universo, zona, lugar)** -- reiniciar o servidor poe cada habitante
///      exatamente onde ele nasceu no primeiro boot. Olhar os corpos vivos pra escolher a vaga
///      quebraria isso: a mesma semente daria mundos diferentes conforme quem morreu antes.
/// ====================================================================================================
/// </summary>
public sealed class Habitat
{
	public readonly int Width, Height;

	/// <summary>O lado do bloco (regiao) e o passo do reticulado, em tiles -- copiados da config.</summary>
	public readonly int LadoDaRegiao, DistanciaMinima;

	/// <summary>Quantos blocos cabem numa linha do mapa (`Width / LadoDaRegiao`, arredondado pra cima).</summary>
	public readonly int Colunas;

	/// <summary>Celulas navegaveis: a componente do berco mais as ilhas grandes (regra 1).</summary>
	public readonly int Celulas;

	/// <summary>Blocos que viraram regiao (tem vagas de sobra).</summary>
	public readonly int Regioes;

	private readonly bool[] _chao;
	private readonly (ushort X, ushort Y)[] _sequencia;
	private readonly int[] _blocoDaVaga;

	/// <summary>Quantas vagas existem -- e o periodo da sequencia de nascimento.</summary>
	public int Vagas => _sequencia.Length;

	private Habitat(int w, int h, int lado, int passo, int colunas, bool[] chao, int celulas, int regioes,
					(ushort, ushort)[] sequencia, int[] blocoDaVaga)
	{
		Width = w; Height = h; LadoDaRegiao = lado; DistanciaMinima = passo; Colunas = colunas;
		_chao = chao; Celulas = celulas; Regioes = regioes; _sequencia = sequencia; _blocoDaVaga = blocoDaVaga;
	}

	/// <summary>A celula e terra navegavel (componente do berco ou ilha grande)?</summary>
	public bool Navegavel(int cx, int cy) =>
		cx >= 0 && cy >= 0 && cx < Width && cy < Height && _chao[cy * Width + cx];

	/// <summary>O indice do bloco (regiao) que contem a celula -- so pra comparar duas celulas.</summary>
	public int Bloco(int cx, int cy) => (cy / LadoDaRegiao) * Colunas + cx / LadoDaRegiao;

	/// <summary>
	/// A VAGA DO PEDIDO `lugar`. Pura: o mesmo `lugar` devolve sempre a mesma celula. Chamar com
	/// `Vagas == 0` e erro do chamador (zona sem terra); ele confere antes.
	///
	/// O primeiro pedido e o 1 (o contador do povoamento nasce em 1), e ele cai na PRIMEIRA vaga da
	/// sequencia: e assim que os `Regioes` primeiros pedidos cobrem as `Regioes` regioes, sem pular
	/// a primeira. O pedido 0 da a volta e cai na ultima.
	/// </summary>
	public (int X, int Y) Vaga(ulong lugar)
	{
		(ushort x, ushort y) = _sequencia[Indice(lugar)];
		return (x, y);
	}

	/// <summary>O bloco (regiao) da vaga do pedido `lugar` -- pra bancada medir o espalhamento.</summary>
	public int BlocoDaVaga(ulong lugar) => _blocoDaVaga[Indice(lugar)];

	private int Indice(ulong lugar) => (int)((lugar + (ulong)Vagas - 1) % (ulong)Vagas);

	/// <summary>
	/// LEVANTA O HABITAT de uma zona. Custa um flood-fill do mapa inteiro (250 mil celulas num
	/// mapa de 500) mais a ordenacao das vagas -- alguns milissegundos, UMA vez por zona.
	/// </summary>
	/// <param name="semente">a semente da ZONA (universo + nome): embaralha regioes e vagas de forma reproduzivel</param>
	public static Habitat Levantar(ZoneCollision mapa, int bercoCx, int bercoCy, Espalhamento cfg, ulong semente)
	{
		int w = mapa.Width, h = mapa.Height;
		var chao = new bool[w * h];
		int celulas = 0;

		// 1) O BERCO, ou a terra mais perto dele -- o mesmo gesto do `PontoLivrePerto`, anel por anel.
		(int sx, int sy) = ChaoMaisPerto(mapa, bercoCx, bercoCy);
		int bercoIdx = sx >= 0 ? sy * w + sx : -1;

		// 2) AS COMPONENTES CONEXAS, 4 vizinhos (diagonal nao conta: um corpo a pe nao atravessa o
		//    encontro de duas quinas). Cada uma e preenchida com uma marca provisoria; entra no
		//    habitat se for a do berco ou se tiver celulas de sobra -- ver a regra 1 do cabecalho.
		var visto = new bool[w * h];
		var fila = new Queue<int>();
		var componente = new List<int>();
		int minimo = Math.Max(1, cfg.MinimoDeCelulasPorComponente);
		for (int inicio = 0; inicio < w * h; inicio++)
		{
			if (visto[inicio]) continue;
			int icx = inicio % w, icy = inicio / w;
			if (!mapa.ServeDeChao(icx, icy)) { visto[inicio] = true; continue; }
			componente.Clear();
			visto[inicio] = true;
			fila.Enqueue(inicio);
			while (fila.Count > 0)
			{
				int i = fila.Dequeue();
				componente.Add(i);
				int cx = i % w, cy = i / w;
				Espiar(cx + 1, cy); Espiar(cx - 1, cy); Espiar(cx, cy + 1); Espiar(cx, cy - 1);
			}
			bool doBerco = bercoIdx >= 0 && componente.Contains(bercoIdx);
			if (!doBerco && componente.Count < minimo) continue;
			foreach (int i in componente) chao[i] = true;
			celulas += componente.Count;
		}
		void Espiar(int nx, int ny)
		{
			if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
			int j = ny * w + nx;
			if (visto[j] || !mapa.ServeDeChao(nx, ny)) return;
			visto[j] = true;
			fila.Enqueue(j);
		}

		// 3) AS VAGAS: o reticulado de passo `d`, deslocado de meio passo pra nao colar na borda.
		int d = Math.Max(1, cfg.DistanciaMinimaEmTiles);
		int desloc = d / 2;
		int lado = Math.Max(1, cfg.LadoDaRegiaoEmTiles);
		int colunas = (w + lado - 1) / lado, linhas = (h + lado - 1) / lado;
		var porBloco = new List<(ushort, ushort)>?[colunas * linhas];
		for (int cy = desloc; cy < h; cy += d)
			for (int cx = desloc; cx < w; cx += d)
			{
				if (!chao[cy * w + cx]) continue;
				int b = (cy / lado) * colunas + cx / lado;
				(porBloco[b] ??= []).Add(((ushort)cx, (ushort)cy));
			}

		// 4) AS REGIOES: blocos com vagas de sobra, em ordem embaralhada pela semente.
		Random rng = SorteioDeNpc.Sorteador(semente, "espalhamento");
		var regioes = new List<int>();
		for (int b = 0; b < porBloco.Length; b++)
			if (porBloco[b] is { } v && v.Count >= Math.Max(1, cfg.MinimoDeVagasPorRegiao)) regioes.Add(b);
		Embaralhar(regioes, rng);
		int total = 0;
		foreach (int b in regioes) { Embaralhar(porBloco[b]!, rng); total += porBloco[b]!.Count; }

		// 5) A ORDEM DE NASCIMENTO: sempre a regiao de MENOR razao (tomadas / vagas); empate, a
		//    que veio primeiro no embaralho. Ver a regra 3 do cabecalho.
		var sequencia = new (ushort, ushort)[total];
		var blocoDaVaga = new int[total];
		var tomadas = new int[regioes.Count];
		for (int i = 0; i < total; i++)
		{
			int melhor = -1;
			double menor = double.MaxValue;
			for (int k = 0; k < regioes.Count; k++)
			{
				int n = porBloco[regioes[k]]!.Count;
				if (tomadas[k] >= n) continue;
				double razao = tomadas[k] / (double)n;
				if (razao < menor) { menor = razao; melhor = k; }
			}
			sequencia[i] = porBloco[regioes[melhor]]![tomadas[melhor]];
			blocoDaVaga[i] = regioes[melhor];
			tomadas[melhor]++;
		}

		return new Habitat(w, h, lado, d, colunas, chao, celulas, regioes.Count, sequencia, blocoDaVaga);
	}

	private static (int, int) ChaoMaisPerto(ZoneCollision mapa, int cx, int cy)
	{
		if (mapa.ServeDeChao(cx, cy)) return (cx, cy);
		for (int r = 1; r <= 64; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (mapa.ServeDeChao(cx + dx, cy + dy)) return (cx + dx, cy + dy);
				}
		return (-1, -1);
	}

	/// <summary>Fisher-Yates com o `Random` semeado: a mesma semente, a mesma ordem, em qualquer maquina.</summary>
	private static void Embaralhar<T>(List<T> lista, Random rng)
	{
		for (int i = lista.Count - 1; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(lista[i], lista[j]) = (lista[j], lista[i]);
		}
	}
}
