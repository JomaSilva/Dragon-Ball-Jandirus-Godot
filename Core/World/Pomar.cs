namespace Jandirus.Core.World;

/// <summary>
/// O POMAR DE UM MUNDO GERADO -- em que celulas nascem as macieiras que todo planeta sorteado tem.
///
/// O dono (2026-10-10): *"para facilitar a vida em planetas procedurais, esses planetas sempre devem ter
/// uma arvore frutifera (maça) pro player nunca morrer de fome"*.
///
/// ============================ O QUE SE DECIDE AQUI, E O QUE NAO ============================
/// AQUI: as CELULAS. Uma macieira perto de onde se chega (a primeira da lista) e mais uma por setor do
/// mapa, espalhadas. E funcao pura do terreno -- e portanto da seed do planeta: o mesmo mundo tem o pomar
/// no mesmo lugar em qualquer maquina e depois de qualquer reinicio, sem nada gravado em disco.
///
/// NAO AQUI: a arvore. Ela e a MESMA macieira dos mapas desenhados a mao e a que nasce da semente
/// (`CatalogoDeItens.ArvoreDeMaca`, ver `Core/Items/Plantio.cs`): uma obra de pe, que o servidor poe como
/// mobilia do mapa (`GameServer.Pomar.cs`). Por isso ela ja chega desenhada, barra o corpo, responde a
/// tecla E e da maca pelo verbo de sempre -- o gerador so reserva o chao dela.
///
/// NAO HA NADA DISTO NO DM: la o mundo sorteado tem a flora do bioma e as plantas de colher
/// (`ProceduralSpace.dm:1334-1344`), e macieira so nos mapas desenhados. E regra nova, do dono.
/// ==========================================================================================
///
/// DETERMINISMO, pela mesma disciplina do <see cref="GeradorDeTerreno"/>: zero estado, so inteiros, e
/// todo sorteio sai do <see cref="Espaco.Misturar"/>.
/// </summary>
public static class Pomar
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `procedural` e `--pomarteste`): o mundo nasce SEM pomar -- o jogo de
	/// antes de 2026-10-10. Lido pela propria regra, em <see cref="Plantar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PlanetaSemPomarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `procedural`): o canteiro volta a poder cair DENTRO do desenho de uma rocha
	/// grande -- a macieira com o tronco atras da pedra e a copa por cima dela. Lido pela propria regra, em
	/// <see cref="ServeDeCanteiro"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CanteiroNaRochaDeTeste;

	/// <summary>
	/// A que distancia do ponto de chegada, no MAXIMO, fica a macieira da chegada -- em tiles, contando a
	/// diagonal como um (anel de Chebyshev). Seis tiles e meia tela no zoom do jogo: quem pousa ve a arvore
	/// sem dar um passo. Na pratica ela nasce no anel 2 ou 3; o teto so e alcancado em pouso apertado.
	/// </summary>
	public const int RaioDaChegada = 6;

	/// <summary>
	/// O lado, em tiles, do SETOR que ganha uma macieira espalhada. E o que faz a quantidade acompanhar o
	/// tamanho do planeta: um mundo de lado L tem `teto(L / 128)` setores por lado, e cada setor com chao
	/// firme ganha a sua. Lado 192 (o menor) = 4 espalhadas; 500 = 16; 1000 (o maior) = 64.
	///
	/// Pra comparar: a Terra desenhada a mao tem 6 macieiras em 500x500. O numero nao pode crescer sem
	/// olhar o cliente -- cada macieira e uma obra, e a lista de obras da zona e refeita INTEIRA a cada
	/// pacote (`World.DesenharObras`: "sao dezenas, nao milhares").
	/// </summary>
	public const int LadoDoSetor = 128;

	/// <summary>
	/// Quantas celulas a macieira guarda da beirada do mapa. Uma a mais que a faixa intocavel da borda
	/// (<see cref="ZoneCollision.MargemDaBorda"/>) de cada lado da arvore: a celula dela e as oito em volta
	/// ficam todas dentro do mundo em que se anda.
	/// </summary>
	public const int MargemDaBeirada = 4;

	/// <summary>
	/// Em quantos segundos a macieira do pomar que foi DERRUBADA volta a nascer no lugar dela. A mobilia do
	/// mapa cai no primeiro soco (`Obra.ArmaduraMax`), e sem isto uma briga no ponto de pouso deixaria o
	/// planeta sem fruta ate o servidor reiniciar -- o "sempre" do pedido. Cinco minutos: da pra abrir uma
	/// clareira na marra, nao da pra matar um mundo de fome.
	/// </summary>
	public const int SegundosPraRebrotar = 300;

	/// <summary>De quanto em quanto tempo o servidor confere os pomares dos mundos vivos.</summary>
	public const int SegundosEntreRondas = 30;

	/// <summary>
	/// Quantas celulas cada setor sorteia atras de um canteiro. As primeiras exigem CAMPO ABERTO (ver
	/// <see cref="CampoAberto"/>); as ultimas aceitam qualquer chao firme. Setor que nao acerta nenhuma
	/// fica sem macieira -- e quase todo mar ou quase toda rocha, e arvore em ilhota nao salva ninguem.
	/// </summary>
	private const int Tentativas = 48;
	private const int TentativasEmCampoAberto = 32;

	// SAIS -- um por sorteio, e nenhum deles e a proporcao aurea (ver a nota dos sais do gerador).
	private const ulong SalDaChegada = 0x8A5CD789635D2DFFUL;
	private const ulong SalDoSetor = 0x4CF5AD432745937FUL;

	/// <summary>Quantos setores por lado tem um mundo deste tamanho. Ver <see cref="LadoDoSetor"/>.</summary>
	public static int SetoresPorLado(int ladoEmTiles) => Math.Max(1, (ladoEmTiles + LadoDoSetor - 1) / LadoDoSetor);

	/// <summary>
	/// ESCOLHE AS CELULAS DO POMAR de um mundo que acabou de ser gerado. A da chegada vem primeiro.
	///
	/// Roda DENTRO do <see cref="GeradorDeTerreno.Gerar"/>, depois da clareira do pouso, e por isso recebe
	/// as grades cruas: no ultimo recurso ela ABRE uma celula (ver <see cref="CavarAoLadoDoPouso"/>), e essa
	/// celula tem de sair aberta no terreno das duas pontas.
	///
	/// PUBLICA POR CAUSA DA BANCADA (`procedural`, no AssetPipeline): os dois casos raros -- o pouso num
	/// ilheu de um tile e o rio entre o pouso e o campo -- a seed quase nunca sorteia, e la eles sao
	/// escritos a mao em grades de 32x32 e entregues a ESTA funcao. No jogo o unico chamador e o gerador.
	/// </summary>
	/// <param name="chao">Classe por celula (`ClasseDeTerreno`), em ordem de linha.</param>
	/// <param name="cobertura">O que ha em cima (`CoberturaDeTerreno`), mesma ordem.</param>
	/// <param name="bits">O bitset de parede que vai virar o JCOL.</param>
	/// <param name="agua">O bitset da agua.</param>
	/// <param name="sx">Celula do pouso, ja decidida e ja com a clareira aberta.</param>
	/// <param name="sy">Idem.</param>
	/// <param name="seed">A seed do planeta: e dela que saem os dois sorteios daqui.</param>
	public static (int X, int Y)[] Plantar(int w, int h, byte[] chao, byte[] cobertura, byte[] bits,
										   byte[] agua, int sx, int sy, ulong seed)
	{
		if (PlanetaSemPomarDeTeste) return [];

		var pomar = new List<(int X, int Y)>();

		// ---- 1. A DA CHEGADA ----
		(int X, int Y)? chegada = PertoDoPouso(w, h, chao, cobertura, bits, sx, sy, seed)
								  ?? CavarAoLadoDoPouso(w, h, chao, cobertura, bits, agua, sx, sy);
		if (chegada is { } c) pomar.Add(c);

		// ---- 2. AS ESPALHADAS: uma por setor ----
		int nx = SetoresPorLado(w), ny = SetoresPorLado(h);
		for (int j = 0; j < ny; j++)
		{
			for (int i = 0; i < nx; i++)
			{
				int x0 = i * w / nx, largura = (i + 1) * w / nx - x0;
				int y0 = j * h / ny, altura = (j + 1) * h / ny - y0;
				ulong setor = (ulong)(j * nx + i);

				for (int t = 0; t < Tentativas; t++)
				{
					ulong r = Espaco.Misturar(seed ^ SalDoSetor, setor, (ulong)t);
					int x = x0 + (int)(r % (ulong)largura);
					int y = y0 + (int)((r >> 32) % (ulong)altura);

					// A CHEGADA JA TEM A DELA: outra dentro do mesmo raio so entupiria o ponto de pouso.
					if (Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) <= RaioDaChegada) continue;
					if (!ServeDeCanteiro(w, h, chao, cobertura, bits, x, y, aceitaPraia: false)) continue;
					if (t < TentativasEmCampoAberto && !CampoAberto(w, h, chao, cobertura, bits, x, y)) continue;

					pomar.Add((x, y));
					break;
				}
			}
		}

		return [.. pomar];
	}

	/// <summary>
	/// A CELULA DA MACIEIRA DA CHEGADA, entre as que ja sao chao livre. Nulo = nao ha nenhuma no raio.
	///
	/// QUATRO PREFERENCIAS, da melhor pra pior, e dentro de cada uma o anel mais perto do pouso:
	///   3. fora da clareira, FORA DA LINHA E DAS COLUNAS do pouso, em campo aberto;
	///   2. o mesmo, sem exigir campo aberto;
	///   1. qualquer chao firme do anel 2 ao <see cref="RaioDaChegada"/>;
	///   0. o anel 1 -- dentro da propria clareira do pouso.
	///
	/// POR QUE FORA DA LINHA E DAS COLUNAS: a macieira e DENSA e desenha POR CIMA de quem passa por baixo
	/// (`ObraDesenhada.AcimaDoJogador`), com a copa de 61x93 px subindo tres tiles a partir da base e
	/// sobrando meio tile de cada lado. Na mesma linha ela barra o primeiro passo de quem pousa; a menos de
	/// duas colunas e abaixo dele, a copa cobre o corpo que acabou de chegar.
	///
	/// E DO ANEL 2 PRA FORA SO VALE ONDE SE CHEGA A PE (<see cref="AlcancadasAPe"/>): seis tiles em linha
	/// reta podem ser a outra margem de um rio, e uma macieira que so se alcanca voando nao alimenta quem
	/// acabou de nascer. O anel 1 dispensa a pergunta -- dali a tecla E ja alcanca sem dar um passo.
	///
	/// O DESEMPATE DENTRO DO ANEL E O HASH DA SEED, e nao a ordem da varredura: com a ordem, todo planeta
	/// de planicie aberta teria a arvore exatamente dois tiles a noroeste do pouso.
	/// </summary>
	private static (int X, int Y)? PertoDoPouso(int w, int h, byte[] chao, byte[] cobertura, byte[] bits,
												int sx, int sy, ulong seed)
	{
		bool[] aPe = AlcancadasAPe(w, h, chao, bits, sx, sy);
		const int lado = 2 * RaioDaChegada + 1;

		for (int exigencia = 3; exigencia >= 0; exigencia--)
		{
			(int X, int Y)? melhor = null;
			ulong notaDoMelhor = ulong.MaxValue;

			for (int dy = -RaioDaChegada; dy <= RaioDaChegada; dy++)
			{
				for (int dx = -RaioDaChegada; dx <= RaioDaChegada; dx++)
				{
					int anel = Math.Max(Math.Abs(dx), Math.Abs(dy));
					if (exigencia == 0 ? anel != 1 : anel < 2) continue;
					if (exigencia >= 2 && (Math.Abs(dx) < 2 || dy == 0)) continue;
					if (exigencia >= 1 && !aPe[(dy + RaioDaChegada) * lado + dx + RaioDaChegada]) continue;

					int x = sx + dx, y = sy + dy;
					if (!ServeDeCanteiro(w, h, chao, cobertura, bits, x, y, aceitaPraia: true)) continue;
					if (exigencia == 3 && !CampoAberto(w, h, chao, cobertura, bits, x, y)) continue;

					// o anel manda (os 8 bits de cima); dentro dele, o hash
					ulong nota = ((ulong)anel << 56)
								 | (Espaco.Misturar(seed ^ SalDaChegada, (ulong)(uint)x, (ulong)(uint)y) >> 8);
					if (nota >= notaDoMelhor) continue;
					notaDoMelhor = nota;
					melhor = (x, y);
				}
			}

			if (melhor != null) return melhor;
		}
		return null;
	}

	/// <summary>
	/// AONDE SE CHEGA ANDANDO A PARTIR DO POUSO, dentro do quadrado de <see cref="RaioDaChegada"/> em volta
	/// dele. Devolve a grade do quadrado, linha a linha (`(dy + raio) * lado + dx + raio`).
	///
	/// PASSO EM CRUZ, e nao na diagonal: o corpo e uma caixa de 16x10 px e nao passa entre duas paredes que
	/// so se tocam pelo canto. Anda-se em tudo o que nao e parede nem agua: quem diz o que e parede e o
	/// `bits` que vai virar o JCOL (montanha, arvore e a pegada da rocha grande, ja carimbada quando isto
	/// roda) -- a planta de colher nao esta nele, e nao barra. Caminho que SAI do quadrado e volta nao
	/// conta: e a favor de quem chega, a arvore tem de estar perto pelo caminho tambem.
	/// </summary>
	private static bool[] AlcancadasAPe(int w, int h, byte[] chao, byte[] bits, int sx, int sy)
	{
		const int lado = 2 * RaioDaChegada + 1;
		var alcancada = new bool[lado * lado];
		var fila = new int[lado * lado];
		int cabeca = 0, cauda = 0;

		int meio = RaioDaChegada * lado + RaioDaChegada;
		alcancada[meio] = true;
		fila[cauda++] = meio;

		while (cabeca < cauda)
		{
			int k = fila[cabeca++];
			int dx = k % lado - RaioDaChegada, dy = k / lado - RaioDaChegada;

			foreach ((int px, int py) in PassosEmCruz)
			{
				int nx = dx + px, ny = dy + py;
				if (Math.Abs(nx) > RaioDaChegada || Math.Abs(ny) > RaioDaChegada) continue;

				int kn = (ny + RaioDaChegada) * lado + nx + RaioDaChegada;
				if (alcancada[kn]) continue;

				int x = sx + nx, y = sy + ny;
				if (x < 0 || y < 0 || x >= w || y >= h) continue;
				int i = y * w + x;
				if ((bits[i >> 3] & (1 << (i & 7))) != 0 || chao[i] == (byte)ClasseDeTerreno.Agua) continue;

				alcancada[kn] = true;
				fila[cauda++] = kn;
			}
		}
		return alcancada;
	}

	private static readonly (int Dx, int Dy)[] PassosEmCruz = [(1, 0), (-1, 0), (0, 1), (0, -1)];

	/// <summary>
	/// O ULTIMO RECURSO: o pouso caiu numa celula ISOLADA (um ilheu de um tile, um poco no meio da rocha) --
	/// nao se chega a pe a canteiro nenhum e nenhuma das oito vizinhas e chao firme. Abre uma vizinha: vira
	/// planicie, sem cobertura, sem parede e sem agua, e a macieira nasce nela.
	///
	/// E A MESMA OPERACAO DA CLAREIRA ESCAVADA (`GeradorDeTerreno.LimparEmVolta` com `cavar`), numa celula
	/// so, e mexe nas quatro grades pelo mesmo motivo: o cliente nao pode pintar mar onde o servidor ja pos
	/// uma arvore. Sem ela a promessa do dono teria um buraco -- raro, mas o universo nao acaba, e num
	/// universo que nao acaba o raro acontece.
	///
	/// A ORDEM DAS VIZINHAS segue a preferencia de cima: as diagonais primeiro (as de cima antes: a copa
	/// sobe, e ali nao cobre quem pousou), depois a coluna, e a linha do pouso por ultimo.
	/// </summary>
	private static (int X, int Y)? CavarAoLadoDoPouso(int w, int h, byte[] chao, byte[] cobertura, byte[] bits,
													  byte[] agua, int sx, int sy)
	{
		foreach ((int dx, int dy) in VizinhasPraCavar)
		{
			int x = sx + dx, y = sy + dy;
			const int m = ZoneCollision.MargemDaBorda;   // aqui vale a da BORDA: o pouso pode estar colado nela
			if (x < m || y < m || x >= w - m || y >= h - m) continue;

			int i = y * w + x;
			chao[i] = (byte)ClasseDeTerreno.Planicie;
			cobertura[i] = (byte)CoberturaDeTerreno.Nada;
			bits[i >> 3] &= (byte)~(1 << (i & 7));
			agua[i >> 3] &= (byte)~(1 << (i & 7));
			return (x, y);
		}
		return null;
	}

	private static readonly (int Dx, int Dy)[] VizinhasPraCavar =
		[(1, -1), (-1, -1), (1, 1), (-1, 1), (0, -1), (0, 1), (1, 0), (-1, 0)];

	/// <summary>
	/// DA PRA UMA MACIEIRA NASCER NESTA CELULA? Chao firme (planicie ou colina; praia so pra a da chegada,
	/// que nao escolhe muito), sem nada em cima, longe da beirada -- e fora do desenho de uma rocha grande.
	///
	/// A ROCHA GRANDE E MAIOR QUE A PAREDE DELA: o desenho sobe tres fileiras alem da ancora, e nas duas do
	/// alto o chao e livre (anda-se por tras da pedra -- ver <see cref="RochaGrande"/>). So que a macieira e
	/// uma obra, e desenha por cima de tudo: plantada ali ela sairia de dentro da pedra. Medido em 1500
	/// mundos antes desta linha: 225 das 13.254 macieiras, e em 20 mundos era a da chegada.
	/// </summary>
	private static bool ServeDeCanteiro(int w, int h, byte[] chao, byte[] cobertura, byte[] bits,
										int x, int y, bool aceitaPraia)
	{
		if (x < MargemDaBeirada || y < MargemDaBeirada || x >= w - MargemDaBeirada || y >= h - MargemDaBeirada)
			return false;
		if (!Livre(w, h, chao, cobertura, bits, x, y)) return false;
		if (!CanteiroNaRochaDeTeste && RochaGrande.Desenha(w, h, cobertura, x, y)) return false;
		return aceitaPraia || chao[y * w + x] != (byte)ClasseDeTerreno.Praia;
	}

	/// <summary>
	/// AS OITO VIZINHAS SAO CHAO LIVRE? Uma arvore densa no meio de um campo aberto nunca fecha passagem:
	/// da-se a volta nela. Encostada numa parede ou na agua ela pode ser a rolha de um corredor de um tile.
	/// </summary>
	private static bool CampoAberto(int w, int h, byte[] chao, byte[] cobertura, byte[] bits, int x, int y)
	{
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if ((dx != 0 || dy != 0) && !Livre(w, h, chao, cobertura, bits, x + dx, y + dy)) return false;
		return true;
	}

	/// <summary>Terra em que se anda e sem nada em cima: nem agua, nem montanha, nem arvore, planta ou rocha.</summary>
	private static bool Livre(int w, int h, byte[] chao, byte[] cobertura, byte[] bits, int x, int y)
	{
		if (x < 0 || y < 0 || x >= w || y >= h) return false;

		int i = y * w + x;
		if (cobertura[i] != (byte)CoberturaDeTerreno.Nada) return false;
		if ((bits[i >> 3] & (1 << (i & 7))) != 0) return false;
		return (ClasseDeTerreno)chao[i] is ClasseDeTerreno.Praia or ClasseDeTerreno.Planicie or ClasseDeTerreno.Colina;
	}
}
