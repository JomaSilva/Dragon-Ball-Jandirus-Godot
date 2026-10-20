using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO ESPALHAMENTO (`--espalhamentoteste`) -- onde os habitantes nascem.
///
/// O pedido do dono (2026-09-06): *"spawn de NPCs distribuido pelo planeta inteiro (so em terra,
/// navegavel, sem colisao), distribuicao aproximadamente uniforme (com distancia minima/regioes)"*,
/// com o criterio de aceite *"nenhum NPC nasce em agua ou empilhado"*.
///
/// ============================ O QUE SO ELA RESPONDE ============================
/// O `Habitat` e Core puro e daria pra provar numa mesa. O que a mesa nao prova e o que esta
/// bancada mede: que o `PontoDeHabitante` DE PRODUCAO, com os mapas de verdade dos planetas
/// do plano, devolve os `quantos` pontos do `npcs.json` (1) fora da agua, (2) em chao que serve,
/// (3) na terra LIGADA ao berco, (4) nunca a menos da distancia minima um do outro e (5) espalhados
/// pelo mapa -- e que a regra antiga (24 tiles em volta do berco), escrita aqui como o defeito
/// injetado, REPROVA no espalhamento. O mapa sintetico no fim isola cada recusa (lago, ilha atras
/// do muro, borda, obra em cima da vaga) onde o mapa real as mistura.
///
/// (2026-10-19) So as linhas que nascem PELO HABITAT: a de `pertoDoBerco` (o Grande Patriarca de Namek)
/// nasce perto do ponto de nascer e e medida na `--patriarcateste`; e (5) so vale com gente bastante
/// pra nao ser sorte -- o Rei e o Principe de Vegeta sao um so cada.
///
/// Roda no boot, sem jogador: nao forja corpo nenhum -- so pergunta onde eles nasceriam.
/// </summary>
public sealed partial class GameServer
{
	private void RodarBancadaDoEspalhamento()
	{
		GD.Print("\n[espalhamento] ================ ONDE OS HABITANTES NASCEM (pedido do dono, 2026-09-06) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[espalhamento]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[espalhamento]   FALHA {nome}   [{detalhe}]"); }
		}
		static (int X, int Y) Celula(Vec2 p) =>
			((int)MathF.Floor(p.X / ZoneCollision.TileSize), (int)MathF.Floor(p.Y / ZoneCollision.TileSize));
		static int Chebyshev((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
		// O MENOR NUMERO DE HABITANTES em que "a populacao cobre o planeta" deixa de ser sorte (a conta esta no bloco 1): com
		// 10, ~1 rodada vermelha em 10 mil por linha. O plano de antes da v0.3.0 nao tinha linha abaixo de 12.
		const int MinimoPraCobrirOPlaneta = 10;

		// UMA EXCECAO E UMA FALHA, E O PLACAR SAI ASSIM MESMO. Da v0.3.0 ate 2026-10-19 esta bancada estourava no meio
		// do bloco 1 (`pontos[6]` numa linha de 1 habitante) e o placar nunca saia: o lancador esperava o prazo inteiro, o
		// log acabava num erro de motor, e ninguem viu que ela tinha parado de medir.
		try
		{
			if (_moldes == null)
			{
				Checa("PRECONDICAO: os moldes carregaram (npcs.json)", false);
				return;
			}

			// =====================================================================
			// 1) OS PLANETAS DO PLANO, com os mapas de verdade
			// =====================================================================
			GD.Print("[espalhamento] -- 1) os planetas do plano de povoamento --");
			foreach (LinhaDePovoamento linha in _moldes.Plano)
			{
				var zona = ZoneKey.Premade(linha.Planeta);
				// PLANETA E MOLDE no rotulo: desde a v0.3.0 um planeta tem mais de uma linha (o povo, o Rei e o Principe de
				// Vegeta), e o rotulo so com o planeta repetia a mesma afirmacao tres vezes.
				string quem = $"{linha.Planeta}/{linha.Molde}";

				// (2026-10-19) A LINHA PERTO DO BERCO NAO PASSA PELO HABITAT: o Grande Patriarca de Namek (`pertoDoBerco`) nasce
				// pelo `PontoPertoDoBerco`, a ate N tiles do ponto de nascer, e as reguas daqui mediriam uma vaga onde ele nao
				// nasce. Quem mede o lugar dele e a `--patriarcateste` (familia N). Daqui sai so a premissa do pulo -- o funil poe
				// os pedidos dela FORA da vaga --, que e tambem o contra-exemplo da regua do funil logo abaixo.
				if (linha.PertoDoBerco > 0)
				{
					int foraDaVagaNoBerco = 0;
					for (ulong lugar = 1; lugar <= (ulong)linha.Quantos; lugar++)
						if (!PontoDoPedido(linha.Molde, zona, lugar).Equals(PontoDeHabitante(zona, lugar))) foraDaVagaNoBerco++;
					Checa($"{quem}: fora das reguas do habitat (nasce a ate {linha.PertoDoBerco} tiles do berco, quem mede e a --patriarcateste)"
						  + " -- contra-exemplo: a regua do funil REPROVA nela",
						  foraDaVagaNoBerco == linha.Quantos, $"{foraDaVagaNoBerco} de {linha.Quantos} pedido(s) fora da vaga do habitat");
					continue;
				}

				ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
				Habitat? h = mapa == null ? null : HabitatDaZona(zona);
				if (mapa == null || h == null)
				{
					Checa($"{quem}: tem mapa e habitat", false);
					continue;
				}
				int n = linha.Quantos;
				Checa($"{quem}: ha vagas de sobra pros {n} habitantes do plano", h.Vagas >= n * 10,
					  $"{h.Vagas:N0} vagas, {h.Celulas:N0} celulas navegaveis, {h.Regioes} regioes");

				var pontos = new List<Vec2>();
				for (ulong lugar = 1; lugar <= (ulong)n; lugar++) pontos.Add(PontoDeHabitante(zona, lugar));
				int naAgua = 0, foraDoChao = 0, desligados = 0, colados = 0;
				var blocos = new HashSet<int>();
				var celulas = new List<(int X, int Y)>();
				foreach (Vec2 p in pontos)
				{
					(int cx, int cy) = Celula(p);
					celulas.Add((cx, cy));
					if (mapa.EhAgua(cx, cy)) naAgua++;
					if (!mapa.ServeDeChao(cx, cy)) foraDoChao++;
					if (!h.Navegavel(cx, cy)) desligados++;
					blocos.Add(h.Bloco(cx, cy));
				}
				int maisLonge = 0;
				for (int i = 0; i < celulas.Count; i++)
					for (int j = i + 1; j < celulas.Count; j++)
					{
						int d = Chebyshev(celulas[i], celulas[j]);
						if (d < h.DistanciaMinima) colados++;
						if (d > maisLonge) maisLonge = d;
					}
				// A EXTENSAO DO PROPRIO HABITAT e a regua do espalhamento: um planeta pequeno nao pode
				// ser cobrado por 100 tiles que ele nao tem.
				int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
				for (ulong k = 1; k <= (ulong)h.Vagas; k++)
				{
					(int vx, int vy) = h.Vaga(k);
					minX = Math.Min(minX, vx); maxX = Math.Max(maxX, vx);
					minY = Math.Min(minY, vy); maxY = Math.Max(maxY, vy);
				}
				int extensao = Math.Max(maxX - minX, maxY - minY);

				Checa($"{quem}: nenhum dos {n} habitantes nasce na agua", naAgua == 0, $"{naAgua} na agua");
				Checa($"{quem}: todos em chao que serve (sem parede, borda, nuvem)", foraDoChao == 0, $"{foraDoChao} fora");
				Checa($"{quem}: todos em terra navegavel (componente com >= {_rotina.Espalhamento.MinimoDeCelulasPorComponente} celulas, ou a do berco)", desligados == 0, $"{desligados} fora");
				// (2026-10-19) O FUNIL DE PRODUCAO E O MEDIDO AQUI: o povoamento chama o `PontoDoPedido`, que manda a linha de
				// `pertoDoBerco` pra perto do berco e as outras pro `PontoDeHabitante` -- e e este que as reguas desta linha medem.
				int foraDaVaga = 0;
				for (ulong lugar = 1; lugar <= (ulong)n; lugar++)
					if (!PontoDoPedido(linha.Molde, zona, lugar).Equals(pontos[(int)lugar - 1])) foraDaVaga++;
				Checa($"{quem}: o funil do povoamento (`PontoDoPedido`) poe os {n} nas vagas medidas aqui", foraDaVaga == 0, $"{foraDaVaga} fora");
				Checa($"{quem}: nenhum par a menos de {h.DistanciaMinima} tiles um do outro (ninguem empilhado)", colados == 0, $"{colados} pares colados");
				int esperado = Math.Min(n, h.Regioes);
				Checa($"{quem}: os {n} caem em {esperado} regioes DIFERENTES (um por regiao antes de repetir)",
					  blocos.Count >= esperado, $"{blocos.Count} regioes de {h.Regioes}");
				// (2026-10-19) COBRIR O PLANETA E REGUA DE POPULACAO, e com pouca gente ela vira sorte: a ordem das regioes sai da
				// semente do mundo, e a bancada nasce com mundo novo a cada rodada. k pontos jogados ao acaso ficam com os dois
				// eixos abaixo da metade da extensao com chance de ~((k+1)/2^k)^2 -- 3,5% com 5 (os Supremos Kaiohs do Ceu), 56%
				// com 2 -- e com 1 (o Rei e o Principe de Vegeta) nao ha par e ela reprova sempre. Abaixo do minimo, o log diz
				// que pulou e por que. As outras reguas de quantidade valem com qualquer `n`: as regioes ja esperam `min(n, Regioes)`.
				if (n >= MinimoPraCobrirOPlaneta)
					Checa($"{quem}: a populacao cobre o planeta (par mais distante >= metade da extensao do habitat)",
						  maisLonge * 2 >= extensao, $"par mais distante {maisLonge} tiles, habitat com {extensao} de extensao");
				else
					GD.Print($"[espalhamento]      {quem}: 'cobre o planeta' PULADA -- {n} habitante(s), abaixo dos "
						   + $"{MinimoPraCobrirOPlaneta} em que a regua deixa de ser sorte");
				// O PEDIDO 7, OU O ULTIMO DA LINHA: o Rei de Vegeta e um so, e o `pontos[6]` fixo estourava a bancada inteira.
				int pedido = Math.Min(7, n);
				Checa($"{quem}: o mesmo pedido cai sempre no mesmo canto (funcao pura de semente, zona e lugar)",
					  PontoDeHabitante(zona, (ulong)pedido).Equals(pontos[pedido - 1]) && PontoDeHabitante(zona, 1).Equals(pontos[0]));

				// LEVANTAR DE NOVO da a mesma ordem: e a promessa do reinicio ("a mesma semente da o mesmo mundo").
				Vec2 berco = PontoDeNascimento(zona);
				(int bx, int by) = Celula(berco);
				Habitat outraVez = Habitat.Levantar(mapa, bx, by, _rotina.Espalhamento,
													Espaco.Misturar(SeedDoUniverso, Espaco.Hash64(zona.Name), 0));
				bool igual = outraVez.Vagas == h.Vagas;
				for (ulong k = 1; igual && k <= (ulong)n; k++) igual = outraVez.Vaga(k) == h.Vaga(k);
				Checa($"{quem}: levantar o habitat de novo (um reinicio) da a MESMA ordem de vagas", igual);
				GD.Print($"[espalhamento]      {quem}: {h.Celulas:N0} celulas navegaveis, {h.Vagas:N0} vagas, "
					   + $"{h.Regioes} regioes; {n} habitantes em {blocos.Count} regioes, par mais distante {maisLonge} tiles");
			}

			// =====================================================================
			// 2) O DEFEITO INJETADO: a regra antiga, 24 tiles em volta do berco
			// =====================================================================
			GD.Print("[espalhamento] -- 2) contra-exemplo: a regra antiga reprova --");
			{
				var zona = ZoneKey.Premade("Earth");
				ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
				Habitat? h = mapa == null ? null : HabitatDaZona(zona);
				if (mapa != null && h != null)
				{
					const int raioAntigo = 24;
					var antigos = new List<(int X, int Y)>();
					var blocosAntigos = new HashSet<int>();
					for (ulong lugar = 1; lugar <= 40; lugar++)
					{
						Random r = SorteioDeNpc.Sorteador(Espaco.Misturar(SeedDoUniverso, Espaco.Hash64(zona.Name), lugar), "onde");
						var desejado = new Vec2(
							SpawnPos.X + r.Next(-raioAntigo, raioAntigo + 1) * ZoneCollision.TileSize,
							SpawnPos.Y + r.Next(-raioAntigo, raioAntigo + 1) * ZoneCollision.TileSize);
						(int cx, int cy) = Celula(mapa.PontoLivrePerto(desejado));
						antigos.Add((cx, cy));
						blocosAntigos.Add(h.Bloco(cx, cy));
					}
					int maisLonge = 0;
					for (int i = 0; i < antigos.Count; i++)
						for (int j = i + 1; j < antigos.Count; j++) maisLonge = Math.Max(maisLonge, Chebyshev(antigos[i], antigos[j]));
					Checa("[injecao] a regra antiga (24 tiles em volta do berco) poe os 40 da Terra em POUCAS regioes",
						  blocosAntigos.Count < Math.Min(40, h.Regioes) / 2, $"{blocosAntigos.Count} regioes de {h.Regioes}");
					Checa("[injecao] ...e o par mais distante nao passa de um quadrado de 49 tiles", maisLonge <= 2 * raioAntigo + 2,
						  $"{maisLonge} tiles");
				}
			}

			// =====================================================================
			// 3) O MAPA SINTETICO: cada recusa isolada
			// =====================================================================
			GD.Print("[espalhamento] -- 3) o mapa sintetico: lago, ilha atras do muro, borda, obra na vaga --");
			{
				const int w = 48, alt = 48;
				var bits = new byte[(w * alt + 7) / 8];
				var agua = new byte[(w * alt + 7) / 8];
				void Marcar(byte[] plano, int cx, int cy) { int i = cy * w + cx; plano[i >> 3] |= (byte)(1 << (i & 7)); }
				for (int y = 0; y < alt; y++) Marcar(bits, 36, y);                 // o muro que separa a ILHA GRANDE do leste (9x44 = 396 celulas)
				for (int y = 8; y <= 15; y++) for (int x = 8; x <= 15; x++) Marcar(agua, x, y);   // o lago
				for (int y = 30; y <= 32; y++) for (int x = 20; x <= 22; x++) Marcar(bits, x, y);   // um pilar
				for (int i = 20; i <= 27; i++) { Marcar(bits, i, 20); Marcar(bits, i, 27); Marcar(bits, 20, i); Marcar(bits, 27, i); }   // o PATIO MURADO (6x6 = 36 celulas por dentro)
				ZoneCollision mapa = ZoneCollision.Montar(w, alt, bits);
				mapa.DefinirAgua(agua);
				var cfg = new Espalhamento { LadoDaRegiaoEmTiles = 12, DistanciaMinimaEmTiles = 2, MinimoDeVagasPorRegiao = 1, MinimoDeCelulasPorComponente = 300 };
				Habitat h = Habitat.Levantar(mapa, 5, 5, cfg, 12345);

				Checa("mapa sintetico: a terra do berco e navegavel", h.Navegavel(5, 5) && h.Navegavel(18, 18) && h.Navegavel(30, 40));
				Checa("...a ILHA GRANDE atras do muro tambem e (uma ilha de Namek e casa de gente)", h.Navegavel(40, 24) && h.Navegavel(38, 5) && h.Navegavel(44, 44));
				Checa("...o PATIO MURADO nao e (36 celulas: ninguem nasce num canto sem onde andar)", !h.Navegavel(23, 23) && !h.Navegavel(21, 26));
				Checa("...o lago nao e", !h.Navegavel(10, 10) && !h.Navegavel(15, 15));
				Checa("...o pilar nao e", !h.Navegavel(21, 31));
				Checa("...a borda do mapa (2 celulas) nao e", !h.Navegavel(1, 20) && !h.Navegavel(20, 46) && !h.Navegavel(0, 0));
				int vagasNaAgua = 0, vagasNaIlha = 0, vagasNoPatio = 0, vagasForaDoChao = 0;
				for (ulong k = 1; k <= (ulong)h.Vagas; k++)
				{
					(int x, int y) = h.Vaga(k);
					if (mapa.EhAgua(x, y)) vagasNaAgua++;
					if (x > 36) vagasNaIlha++;
					if (x is > 20 and < 27 && y is > 20 and < 27) vagasNoPatio++;
					if (!mapa.ServeDeChao(x, y)) vagasForaDoChao++;
				}
				Checa("...ha vagas de sobra", h.Vagas >= 100, $"{h.Vagas} vagas em {h.Regioes} regioes");
				Checa("...nenhuma vaga na agua", vagasNaAgua == 0, $"{vagasNaAgua}");
				Checa("...ha vagas na ilha grande, e nenhuma no patio", vagasNaIlha > 0 && vagasNoPatio == 0, $"ilha {vagasNaIlha}, patio {vagasNoPatio}");
				Checa("...toda vaga em chao que serve", vagasForaDoChao == 0, $"{vagasForaDoChao}");
				Habitat semIlha = Habitat.Levantar(mapa, 5, 5, new Espalhamento { LadoDaRegiaoEmTiles = 12, DistanciaMinimaEmTiles = 2, MinimoDeVagasPorRegiao = 1, MinimoDeCelulasPorComponente = 500 }, 12345);
				Checa("...contra-exemplo: com o minimo por componente em 500, a ilha de 396 fica de fora", !semIlha.Navegavel(40, 24) && semIlha.Celulas < h.Celulas);
				var primeiras = new HashSet<int>();
				for (ulong k = 1; k <= (ulong)h.Regioes; k++) primeiras.Add(h.BlocoDaVaga(k));
				Checa($"...os pedidos 1..{h.Regioes} caem em {h.Regioes} regioes diferentes (o primeiro pedido e o 1)", primeiras.Count == h.Regioes, $"{primeiras.Count}");
				Checa("...e o pedido 0 da a volta e cai na ultima vaga, sem excecao", h.Vaga(0) == h.Vaga((ulong)h.Vagas));
				int colados = 0;
				int amostra = Math.Min(60, h.Vagas);
				for (ulong i = 1; i <= (ulong)amostra; i++)
					for (ulong j = i + 1; j <= (ulong)amostra; j++)
						if (Chebyshev(h.Vaga(i), h.Vaga(j)) < cfg.DistanciaMinimaEmTiles) colados++;
				Checa("...duas vagas nunca distam menos que o passo", colados == 0, $"{colados} pares");
				Habitat h2 = Habitat.Levantar(mapa, 5, 5, cfg, 12345);
				bool igual = h2.Vagas == h.Vagas;
				for (ulong k = 1; igual && k <= (ulong)Math.Min(80, h.Vagas); k++) igual = h2.Vaga(k) == h.Vaga(k);
				Checa("...a mesma semente da a mesma ordem", igual);
				Habitat h3 = Habitat.Levantar(mapa, 5, 5, cfg, 999);
				bool diferente = false;
				for (ulong k = 1; !diferente && k <= (ulong)Math.Min(80, h.Vagas); k++) diferente = h3.Vaga(k) != h.Vaga(k);
				Checa("...e outra semente da outra ordem (contra-exemplo)", diferente);

				// UMA OBRA EM CIMA DA VAGA, depois do levantamento: o nascimento desvia pra celula livre do lado.
				(int vx, int vy) = h.Vaga(3);
				mapa.Bloquear(vx, vy);
				Vec2 desviado = mapa.PontoLivrePerto(mapa.CentroDaCelula(vx, vy));
				(int dx, int dy) = Celula(desviado);
				Checa("...uma obra levantada em cima da vaga desvia o nascimento pra celula livre do lado",
					  (dx, dy) != (vx, vy) && mapa.ServeDeChao(dx, dy) && Chebyshev((dx, dy), (vx, vy)) <= 2, $"({vx},{vy}) -> ({dx},{dy})");
				mapa.LimparObras();

				Habitat h4 = Habitat.Levantar(mapa, 10, 10, cfg, 1);
				Checa("...berco dentro do lago: o habitat nasce da terra mais perto, e e o mesmo", h4.Celulas == h.Celulas, $"{h4.Celulas} vs {h.Celulas}");

				var tudoAgua = new byte[(16 * 16 + 7) / 8];
				for (int i = 0; i < tudoAgua.Length; i++) tudoAgua[i] = 0xFF;
				ZoneCollision oceano = ZoneCollision.Montar(16, 16, new byte[(16 * 16 + 7) / 8]);
				oceano.DefinirAgua(tudoAgua);
				Habitat h5 = Habitat.Levantar(oceano, 8, 8, cfg, 1);
				Checa("...um mapa so de agua da zero vagas, sem excecao", h5.Vagas == 0 && h5.Celulas == 0);
			}

			// =====================================================================
			// 4) ZONA SEM MAPA: o funil nao quebra, cai no berco
			// =====================================================================
			{
				var semMapa = ZoneKey.Premade("ZonaQueNaoExiste");
				Vec2 p = PontoDeHabitante(semMapa, 1);
				Checa("zona sem mapa: `PontoDeHabitante` devolve o berco em vez de quebrar", p.Equals(PontoDeNascimento(semMapa)));
			}
		}
		catch (Exception e)
		{
			Checa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			GD.Print($"[espalhamento] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
