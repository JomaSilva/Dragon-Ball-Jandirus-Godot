using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS SALAS DO TEMPO DE PLANETA (`--salasteste`) -- o pedido do dono de 2026-10-11, do sorteio a expulsao.
///
/// No boot, como as outras de servidor: ninguem logado, corpos forjados (sem `Peer`) que pousam, andam ate a porta
/// e apertam os verbos pelo funil de producao. O universo e o DESTE servidor (a seed sai na primeira linha, pra
/// repetir a rodada que reprovar).
///
/// AS PERGUNTAS:
///   1. O SORTEIO -- a raridade medida numa regiao de verdade (um em 400, mais rara que a atmosfera divina), a
///      tabela "dilatacao -> chance" medida em cem mil seeds, a mesma seed dando a mesma resposta;
///   2. A PORTA -- em mundos de todo bioma e tamanho: numa celula de MONTANHA, com a soleira ao sul em chao firme a
///      que se chega A PE desde o pouso (conferido por uma inundacao da propria bancada), a mesma em toda geracao;
///   3. O MUNDO VIVO -- a porta de pe como obra no mundo que tem sala, que nao cai no soco; a linha das regras;
///   4. A SALA -- entrar; o interior e o da Terra (mapa sem borda, 10x, clima) e NAO e a da Terra (vaga, prisao);
///      o ganho de BP e a idade vezes a dilatacao; a sessao de dois dias; a EXPULSAO pra frente da porta;
///   5. A RECARGA -- 24 h x a dilatacao, e uma so pra todas: outra sala de planeta e a porta do Templo recusam;
///   6. AS BORDAS -- a lotacao, sair pela passagem, sair por fora, entrar de carona, deslogar la dentro com o
///      mundo fora da memoria, o save antigo, o planeta destruido, o teleporte que nao chega, o verb de admin.
///
/// CADA REGRA TEM O DEFEITO DELA, injetado no codigo de PRODUCAO: as linhas "(defeito injetado: ...)" so passam
/// quando a regua REPROVA com o botao ligado. `--salasantes` roda a bancada inteira com "nenhum mundo tem sala"
/// -- o jogo de antes do pedido --, e o placar dela e o VERMELHO do antes.
///
///     Godot --headless -- --server --port 8350 --salasteste
/// </summary>
public sealed partial class GameServer
{
	private int _slOk, _slFalhou;

	private void AfirmarSl(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _slOk++; GD.Print($"[salas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _slFalhou++; GD.PrintErr($"[salas]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>Faixa de ids de bancada -- longe do `_nextId` e das faixas das outras.</summary>
	private const int IdBaseDasSalasDeTeste = 96_700;

	/// <summary>O raio (em celulas de sistema, a partir da Terra) da regiao em que a raridade e medida: 201x201.</summary>
	private const int RaioDaMedidaDeSalas = 100;

	public void RodarBancadaDasSalasDePlaneta(bool antes)
	{
		_slOk = _slFalhou = 0;
		GD.Print($"[salas] ================ AS SALAS DO TEMPO DE PLANETA (dono, 2026-10-11) -- universo {SeedDoUniverso}"
				 + (antes ? " -- O ANTES: nenhum mundo tem sala" : "") + " ================");

		var reais = new HashSet<Obra>(_noChao);
		var salaReal = new Dictionary<string, long>(_naSala, StringComparer.Ordinal);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		var corpos = new List<ServerPlayer>();
		var mundos = new List<ZoneKey>();
		string? chaveMorta = null;
		SalaDePlaneta.NenhumaSalaDeTeste = antes;

		try
		{
			AfirmarSl("(montagem) o catalogo de obras tem a porta da Sala do Tempo", _obras?.Get(TipoDaPorta) != null);
			AfirmarSl("(montagem) o mapa da Sala (z13) esta no manifesto, e sem borda",
					  _catalogo?.Get(ZoneKey.Premade(ZonaDaSala))?.Mapa is { SemBorda: true });

			List<SalaNoUniverso> salas = SlDoSorteio();
			SlDaPorta(salas);

			// DOIS MUNDOS COM SALA, os menores (gerar e mais barato): um que dilate MAIS que a Terra, pra a idade e o
			// ganho terem o que mostrar, e outro qualquer -- de dilatacao diferente, se houver.
			SalaNoUniverso? forte = salas.Where(s => s.Dilatacao > 1).OrderBy(s => MundoProcedural.DaSeed(s.Corpo.Seed, s.Corpo.Nome).Lado)
										 .Cast<SalaNoUniverso?>().FirstOrDefault();
			SalaNoUniverso? outra = salas.Where(s => forte == null || s.Corpo.Seed != forte.Value.Corpo.Seed)
										 .OrderBy(s => forte != null && s.Dilatacao == forte.Value.Dilatacao ? 1 : 0)
										 .ThenBy(s => MundoProcedural.DaSeed(s.Corpo.Seed, s.Corpo.Nome).Lado)
										 .Cast<SalaNoUniverso?>().FirstOrDefault();
			AfirmarSl("(montagem) a regiao medida tem um mundo cuja sala dilata MAIS que a da Terra, e outro mundo com sala",
					  forte != null && outra != null,
					  forte == null ? "nenhum" : $"{forte.Value.Corpo.Nome} ({forte.Value.Dilatacao} a/d) e {outra?.Corpo.Nome} ({outra?.Dilatacao} a/d)");
			if (forte is not { } a || outra is not { } b) return;

			ZoneKey mundoA = Espaco.ZonaDe(a.Corpo), mundoB = Espaco.ZonaDe(b.Corpo);
			mundos.Add(mundoA);
			mundos.Add(mundoB);

			ServerPlayer p1 = SlForjar(1, "bancada: o viajante", corpos);
			ServerPlayer p2 = SlForjar(2, "bancada: o segundo", corpos);
			ServerPlayer p3 = SlForjar(3, "bancada: o terceiro", corpos);

			SlDoMundoVivo(p1, a, mundoA);
			SlDaSala(p1, a, mundoA);
			SlDaRecarga(p1, a, mundoA, b, mundoB);
			SlDasBordas(p1, p2, p3, a, mundoA, ref chaveMorta);
		}
		catch (Exception e)
		{
			AfirmarSl($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			SalaDePlaneta.NenhumaSalaDeTeste = false;
			SalaDePlaneta.DilatacaoFixaDeTeste = false;
			SalaDePlaneta.PortaSemCaminhoDeTeste = false;
			SalaDePlaneta.PortaNoMatoDeTeste = false;
			TranseFechaASessaoDeTeste = false;
			SalaDoTempo.GanhoSemDilatacaoDeTeste = false;
			SalaDoTempo.IdadeSemDilatacaoDeTeste = false;
			PortaDaSalaNaoNasceDeTeste = SalaNaoExpulsaDeTeste = RecargaSoDaTerraDeTeste = RecargaSemDilatacaoDeTeste = false;
			SalaDePlanetaSemLotacaoDeTeste = SairPorForaMantemASessaoDeTeste = false;
			SemSessaoTambemRendeDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			EscutaDeAvisos = null;

			if (chaveMorta != null && ChaveDePlaneta.Ler(chaveMorta, "", out ChaveDePlaneta morta)) _mortos.Tirar(morta);

			foreach (ServerPlayer p in corpos)
			{
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
				EsquecerSessaoDaSala(p.Id);
				EsquecerPassagem(p.Id);
				_avisadosDeEspera.Remove(p.Id);
				_avisadosDePlanetaMorto.Remove(p.Id);
			}
			foreach (ZoneKey z in mundos)
			{
				_zonasGeradas.Remove(z.Hash);
				ArrancarPomar(z.Hash);
				FecharAPortaDaSala(z.Hash);
				_cenarioCaido.Remove(ChaveDeEstado(z));
			}
			_noChao.RemoveAll(o => !reais.Contains(o));
			_naSala.Clear();
			foreach (var kv in salaReal) _naSala[kv.Key] = kv.Value;

			AfirmarSl("a bancada devolve o mundo como pegou (nenhum corpo, nenhuma obra, nenhuma porta e nenhuma vaga a mais)",
					  corpos.TrueForAll(p => !_players.ContainsKey(p.Id)) && _noChao.Count == reais.Count
					  && mundos.TrueForAll(z => !_portasDasSalas.ContainsKey(z.Hash)) && _naSala.Count == salaReal.Count,
					  $"obras={_noChao.Count}/{reais.Count} portas={_portasDasSalas.Count} vagas={_naSala.Count}/{salaReal.Count}");

			GD.Print($"[salas] ================ {_slOk} OK, {_slFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// OS AUXILIARES
	// =====================================================================
	private ServerPlayer SlForjar(int i, string nome, List<ServerPlayer> corpos)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDasSalasDeTeste + i,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDoEspaco,
			Pos = new Vec2(0, 0),
			Conta = $"bancada_salas_{(char)('a' + i)}",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 1000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		corpos.Add(novo);
		return novo;
	}

	/// <summary>Faz o mundo nascer no servidor pelo caminho de producao (a geracao fora do tique + a colheita), e espera.</summary>
	private ZonaGerada? SlAcordar(PlanetaNoEspaco p)
	{
		ZoneKey zona = Espaco.ZonaDe(p);
		for (int i = 0; i < 4000; i++)
		{
			if (MundoDaZona(zona, p) is { } viva) return viva;
			TickDasGeracoes();
			if (!_zonasGeradas.ContainsKey(zona.Hash)) System.Threading.Thread.Sleep(5);
		}
		return null;
	}

	/// <summary>Poe o corpo na SOLEIRA da porta do mundo (o chao em frente a ela), pousado e parado.</summary>
	private bool SlNaPorta(ServerPlayer pl, PlanetaNoEspaco p)
	{
		ZoneKey zona = Espaco.ZonaDe(p);
		if (SlAcordar(p) == null || !_portasDasSalas.TryGetValue(zona.Hash, out PortaNoTerreno porta)) return false;
		(int sx, int sy) = porta.Soleira;
		MoveToZone(pl.Id, zona, CorpoNaCelulaDaSala(sx, sy));
		pl.Voando = false;
		pl.Moving = false;
		return true;
	}

	/// <summary>Quantos passos a pe do pouso ate esta celula, pela regua da BANCADA (em cruz; parede, agua e macieira barram). -1 = nao chega.</summary>
	private static int SlPassosAte(TerrenoGerado t, (int X, int Y) alvo, byte[] chao, byte[] bits)
	{
		int w = t.Largura, h = t.Altura;
		var pomar = new HashSet<int>(t.Macieiras.Select(m => m.Y * w + m.X));
		int[] d = new int[w * h];
		Array.Fill(d, -1);
		var fila = new Queue<int>();
		d[t.SpawnCelY * w + t.SpawnCelX] = 0;
		fila.Enqueue(t.SpawnCelY * w + t.SpawnCelX);
		while (fila.Count > 0)
		{
			int k = fila.Dequeue();
			if (k == alvo.Y * w + alvo.X) return d[k];
			foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
			{
				int x = k % w + dx, y = k / w + dy;
				if (x < 0 || y < 0 || x >= w || y >= h) continue;
				int n = y * w + x;
				if (d[n] >= 0 || (bits[n >> 3] & (1 << (n & 7))) != 0 || chao[n] == (byte)ClasseDeTerreno.Agua || pomar.Contains(n)) continue;
				d[n] = d[k] + 1;
				fila.Enqueue(n);
			}
		}
		return -1;
	}

	/// <summary>
	/// A REGUA DA PORTA num terreno: a porta e rocha que barra, a soleira e chao firme e limpo, e chega-se nela a pe.
	/// Devolve a porta que a regra escolheu (com `umEm: 1`, pra medir em qualquer mundo) e diz o que reprovou.
	/// </summary>
	private static PortaNoTerreno? SlMedirAPorta(ulong seed, string nome, out string defeito, out TerrenoGerado t)
	{
		t = GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(seed, nome).Parametros());
		byte[] chao = (byte[])t.Chao.Clone(), cobertura = (byte[])t.Cobertura.Clone(), agua = (byte[])t.BytesDeAgua.Clone();
		byte[] bits = t.BytesDeColisao[8..];
		PortaNoTerreno? porta = t.PortaDaSala ?? SalaDePlaneta.AbrirAPorta(t.Largura, t.Altura, chao, cobertura, bits, agua,
																		   t.SpawnCelX, t.SpawnCelY, seed, [.. t.Macieiras], umEm: 1);
		defeito = "";
		if (porta is not { } p) { defeito = "nao ha porta"; return null; }

		int w = t.Largura, i = p.Y * w + p.X, s = p.Soleira.Y * w + p.Soleira.X;
		if (chao[i] != (byte)ClasseDeTerreno.Montanha || (bits[i >> 3] & (1 << (i & 7))) == 0) defeito = "a porta nao e rocha que barra";
		else if ((bits[s >> 3] & (1 << (s & 7))) != 0 || chao[s] == (byte)ClasseDeTerreno.Agua) defeito = "a soleira nao e chao";
		else if (SlPassosAte(t, p.Soleira, chao, bits) < 0) defeito = "nao se chega a soleira a pe desde o pouso";
		else
		{
			// O TERREIRO: nenhuma arvore nem planta nas fileiras em frente a porta (a copa a cobriria), e nenhuma macieira.
			for (int y = p.Y + 1; y <= p.Y + SalaDePlaneta.FundoDoTerreiro && defeito.Length == 0; y++)
				for (int x = p.X - SalaDePlaneta.LadoDoTerreiro; x <= p.X + SalaDePlaneta.LadoDoTerreiro; x++)
				{
					if (x < 0 || y < 0 || x >= w || y >= t.Altura) continue;
					var c = (CoberturaDeTerreno)cobertura[y * w + x];
					if (c is CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento or CoberturaDeTerreno.Planta || t.Macieiras.Contains((x, y)))
					{ defeito = $"ha mato no terreiro da porta, em ({x},{y})"; break; }
				}
		}
		return porta;
	}

	// =====================================================================
	// 1) O SORTEIO
	// =====================================================================
	private List<SalaNoUniverso> SlDoSorteio()
	{
		GD.Print("[salas] -- 1) o sorteio: a raridade num universo de verdade, e a tabela da dilatacao --");
		const int r = RaioDaMedidaDeSalas;

		List<SalaNoUniverso> salas = SalaDePlaneta.NaRegiao(SeedDoUniverso, 0, 0, r);
		int mundosSorteados = SalaDePlaneta.MundosNaRegiao(SeedDoUniverso, 0, 0, r);
		int celulas = (2 * r + 1) * (2 * r + 1);
		double esperadas = mundosSorteados / (double)SalaDePlaneta.UmEm;
		double folga = 5 * Math.Sqrt(Math.Max(esperadas, 1));

		AfirmarSl($"A RARIDADE MEDIDA: um mundo sorteado em cada ~{SalaDePlaneta.UmEm} tem sala (regiao de {celulas:N0} celulas em volta da Terra)",
				  salas.Count > 0 && Math.Abs(salas.Count - esperadas) <= folga,
				  $"{salas.Count} salas em {mundosSorteados:N0} mundos = uma em {(salas.Count > 0 ? mundosSorteados / (double)salas.Count : 0):0}; "
				  + $"esperadas {esperadas:0} +/- {folga:0}; uma sala a cada {(salas.Count > 0 ? celulas / (double)salas.Count : 0):0} celulas");
		AfirmarSl($"ela e MAIS RARA que a atmosfera divina (um em {AtmosferaDivina.UmEm})", SalaDePlaneta.UmEm > AtmosferaDivina.UmEm);

		// A QUE DISTANCIA DA TERRA FICA A MAIS PERTO -- informativo: o numero e deste universo.
		Vec2 terra = new(0, 0);
		if (salas.Count > 0)
		{
			SalaNoUniverso perto = salas.MinBy(s => (s.Corpo.Pos - terra).Length);
			double px = (perto.Corpo.Pos - terra).Length;
			double minutosAPe = px / MoveRules.BaseSpeedPx / 60;
			var porD = salas.GroupBy(s => s.Dilatacao).OrderBy(g => g.Key)
							.Select(g => $"{SalaDePlaneta.TextoDaDilatacao(g.Key)}: {g.Count()}");
			GD.Print($"[salas]   (medida) a sala mais perto da Terra: {perto.Corpo.Nome} [{perto.Sx}:{perto.Sy}], "
					 + $"{SalaDePlaneta.TextoDaDilatacao(perto.Dilatacao)} por dia, a {px / Sistemas.CelulaPx:0.0} celulas "
					 + $"({minutosAPe:0} min de voo a pe). Nesta regiao: {string.Join(" | ", porD)}");
		}

		// E NUM UNIVERSO TIPICO? A seed deste servidor e uma so; a mesma conta em 300 universos diz o que esperar
		// de qualquer um -- a que distancia da Terra fica a sala mais perto, e a de 5 anos mais perto.
		const int universos = 300, raioDeBusca = 40;
		var perto1 = new List<double>();
		var perto5 = new List<double>();
		for (int u = 0; u < universos; u++)
		{
			ulong seed = Espaco.Misturar(SeedDoUniverso, (ulong)u, 0x5A1A);
			double c = SalaDePlaneta.MaisPertoDaTerra(seed, raioDeBusca).Celulas;
			if (c >= 0) perto1.Add(c);
			if (u < 60 && SalaDePlaneta.MaisPertoDaTerra(seed, 120, SalaDePlaneta.DilatacaoMaxima).Celulas is >= 0 and var c5) perto5.Add(c5);
		}
		perto1.Sort();
		perto5.Sort();
		double minPorCelula = Sistemas.CelulaPx / MoveRules.BaseSpeedPx / 60;
		AfirmarSl($"EM {universos} UNIVERSOS todo universo tem uma sala a ate {raioDeBusca} celulas da Terra, e a mais perto fica em media a poucas celulas",
				  perto1.Count == universos && perto1.Average() is > 2 and < 9,
				  perto1.Count == 0 ? "nenhuma" : $"media {perto1.Average():0.0} celulas ({perto1.Average() * minPorCelula:0} min de voo a pe); mediana {perto1[perto1.Count / 2]:0.0}; "
				  + $"a mais perto {perto1[0]:0.0}; 9 em 10 universos ate {perto1[perto1.Count * 9 / 10]:0.0}; o pior {perto1[^1]:0.0}"
				  + (perto5.Count == 0 ? "" : $" | A DE 5 ANOS mais perto, em 60 universos: media {perto5.Average():0} celulas "
				  + $"({perto5.Average() * minPorCelula / 60:0.0} h de voo a pe), a mais perto {perto5[0]:0}, o pior {perto5[^1]:0}"));

		// A MESMA SEED, A MESMA RESPOSTA: a regiao varrida de novo da a mesma lista, na mesma ordem.
		List<SalaNoUniverso> deNovo = SalaDePlaneta.NaRegiao(SeedDoUniverso, 0, 0, r);
		AfirmarSl("a mesma semente da as mesmas salas, com a mesma dilatacao (a regiao varrida duas vezes)",
				  deNovo.Count == salas.Count && deNovo.Zip(salas).All(par => par.First.Corpo.Seed == par.Second.Corpo.Seed
																			  && par.First.Dilatacao == par.Second.Dilatacao));
		AfirmarSl("so MUNDO SORTEADO tem sala de planeta: a Terra, o espaco e um interior nao",
				  !SalaDePlaneta.Tem(ZoneKey.Premade("Earth")) && !SalaDePlaneta.Tem(ZonaDoEspaco)
				  && !SalaDePlaneta.Tem(ZoneKey.Interior("Nave", 7)));

		// ---- a tabela "dilatacao -> chance", medida ----
		const int n = 100_000;
		var conta = new Dictionary<double, int>();
		for (int i = 0; i < n; i++)
		{
			double d = SalaDePlaneta.Dilatacao(Espaco.Misturar(SeedDoUniverso, (ulong)i, 0x51A1));
			conta[d] = conta.GetValueOrDefault(d) + 1;
		}
		bool bate = true, desce = true;
		int pesoAntes = int.MaxValue, soma = 0;
		var linhas = new List<string>();
		foreach (FaixaDeDilatacao f in SalaDePlaneta.Tabela)
		{
			double medido = conta.GetValueOrDefault(f.AnosPorDia) * 100.0 / n;
			if (Math.Abs(medido - f.Peso) > 0.6) bate = false;
			if (f.Peso > pesoAntes) desce = false;
			pesoAntes = f.Peso;
			soma += f.Peso;
			linhas.Add($"{SalaDePlaneta.TextoDaDilatacao(f.AnosPorDia)} {f.Peso}% (medido {medido:0.0}%)");
		}
		AfirmarSl($"A TABELA DA DILATACAO, medida em {n:N0} seeds, bate com os pesos escritos", bate, string.Join(" | ", linhas));
		AfirmarSl("as MELHORES sao as mais RARAS: o peso so desce da de 3 meses ate a de 5 anos, e a soma e 100",
				  desce && soma == SalaDePlaneta.PesoTotal && conta.Keys.All(d => d >= SalaDePlaneta.DilatacaoMinima && d <= SalaDePlaneta.DilatacaoMaxima)
				  && SalaDePlaneta.Tabela[0].AnosPorDia == SalaDePlaneta.DilatacaoMinima && SalaDePlaneta.Tabela[^1].AnosPorDia == SalaDePlaneta.DilatacaoMaxima);
		AfirmarSl("a frase da dilatacao: 3 meses, 6 meses, 1 ano, 5 anos",
				  SalaDePlaneta.TextoDaDilatacao(0.25) == "3 meses" && SalaDePlaneta.TextoDaDilatacao(0.5) == "6 meses"
				  && SalaDePlaneta.TextoDaDilatacao(1) == "1 ano" && SalaDePlaneta.TextoDaDilatacao(5) == "5 anos");

		// ---- os defeitos ----
		SalaDePlaneta.DilatacaoFixaDeTeste = true;
		try
		{
			AfirmarSl("(defeito injetado: toda sala dilata como a da Terra) a regua da tabela REPROVA",
					  SalaDePlaneta.Dilatacao(123) == 1 && SalaDePlaneta.Dilatacao(456) == 1 && SalaDePlaneta.Dilatacao(98765) == 1);
		}
		finally { SalaDePlaneta.DilatacaoFixaDeTeste = false; }

		bool antes = SalaDePlaneta.NenhumaSalaDeTeste;
		SalaDePlaneta.NenhumaSalaDeTeste = true;
		try
		{
			AfirmarSl("(defeito injetado: nenhum mundo tem sala) a regua da raridade REPROVA -- a regiao inteira fica sem nenhuma",
					  SalaDePlaneta.NaRegiao(SeedDoUniverso, 0, 0, 30).Count == 0);
		}
		finally { SalaDePlaneta.NenhumaSalaDeTeste = antes; }

		return salas;
	}

	// =====================================================================
	// 2) A PORTA
	// =====================================================================
	private void SlDaPorta(List<SalaNoUniverso> salas)
	{
		GD.Print("[salas] -- 2) a porta: numa parede de montanha, alcancavel a pe, a mesma em toda geracao --");

		// OS MUNDOS DA MEDIDA: os de sala desta regiao (ate 8, os menores) e mais 4 de cada bioma de qualquer seed
		// (a regra da porta medida com `umEm: 1`) -- montanha quase nenhuma no Deserto, quase so montanha no Rochoso.
		var amostra = new List<(ulong Seed, string Nome)>();
		foreach (SalaNoUniverso s in salas.OrderBy(s => MundoProcedural.DaSeed(s.Corpo.Seed, s.Corpo.Nome).Lado).Take(8))
			amostra.Add((s.Corpo.Seed, s.Corpo.Nome));
		int deSala = amostra.Count;
		var porBioma = new Dictionary<BiomaDeTerreno, int>();
		for (int i = 1; i <= 400 && porBioma.Values.Sum() < 24; i++)
		{
			if (Sistemas.Do(SeedDoUniverso, i * 3 + 1, i * 2) is not { Ancorado: false } sistema) continue;
			foreach (PlanetaNoEspaco p in sistema.Planetas())
			{
				MundoProcedural m = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (p.Premade || m.Lado > 420 || porBioma.GetValueOrDefault(m.Bioma) >= 4) continue;
				porBioma[m.Bioma] = porBioma.GetValueOrDefault(m.Bioma) + 1;
				amostra.Add((p.Seed, p.Nome));
			}
		}

		int certas = 0, erguidas = 0, deParede = 0, iguais = 0, maisLonge = 0;
		var defeitos = new List<string>();
		foreach ((ulong seed, string nome) in amostra)
		{
			PortaNoTerreno? porta = SlMedirAPorta(seed, nome, out string defeito, out TerrenoGerado t);
			if (porta is { } p && defeito.Length == 0)
			{
				certas++;
				if (p.Erguida) erguidas++;
				bool M(int x, int y) => t.Dentro(x, y) && (p.Erguida || t.ClasseEm(x, y) == ClasseDeTerreno.Montanha);
				if (M(p.X - 1, p.Y) && M(p.X + 1, p.Y) && M(p.X, p.Y - 1)) deParede++;
				maisLonge = Math.Max(maisLonge, p.Passos);
			}
			else defeitos.Add($"{nome}: {defeito}");

			PortaNoTerreno? outraVez = SlMedirAPorta(seed, nome, out _, out _);
			if (porta == outraVez) iguais++;
		}

		AfirmarSl($"EM {amostra.Count} MUNDOS ({deSala} com sala na seed + {amostra.Count - deSala} de todo bioma): a porta e uma celula de "
				  + "MONTANHA que barra, e a soleira ao sul dela e chao firme a que se chega A PE desde o pouso",
				  amostra.Count >= 12 && certas == amostra.Count,
				  defeitos.Count > 0 ? string.Join("; ", defeitos.Take(4)) : $"{deParede} encravadas em parede de verdade (rocha dos lados e atras), "
				  + $"{erguidas} com rochedo erguido; a mais longe do pouso a {maisLonge} passos");
		AfirmarSl("a mesma semente da a MESMA porta (cada mundo gerado duas vezes)", iguais == amostra.Count, $"{iguais}/{amostra.Count}");

		if (deSala > 0)
		{
			(ulong seed, string nome) = amostra[0];
			TerrenoGerado comSala = GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(seed, nome).Parametros());
			AfirmarSl("o mundo que tem sala na seed sai do gerador COM a porta, e o que nao tem sai sem",
					  comSala.PortaDaSala != null && amostra.Skip(deSala).Take(3).All(m =>
						  SalaDePlaneta.Tem(m.Seed) || GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(m.Seed, m.Nome).Parametros()).PortaDaSala == null));
		}

		// ---- o defeito: a porta sem caminho ----
		SalaDePlaneta.PortaSemCaminhoDeTeste = true;
		int reprovadas = 0;
		try
		{
			foreach ((ulong seed, string nome) in amostra.Skip(deSala))
			{
				SlMedirAPorta(seed, nome, out string defeito, out _);
				if (defeito.Length > 0) reprovadas++;
			}
		}
		finally { SalaDePlaneta.PortaSemCaminhoDeTeste = false; }
		AfirmarSl("(defeito injetado: a porta na primeira montanha da varredura, sem perguntar pelo caminho) a regua REPROVA",
				  reprovadas > 0, $"{reprovadas} de {amostra.Count - deSala} mundos com a porta inalcancavel");

		// ---- o defeito: a porta no meio do mato ----
		SalaDePlaneta.PortaNoMatoDeTeste = true;
		int noMato = 0;
		try
		{
			foreach ((ulong seed, string nome) in amostra.Skip(deSala))
			{
				SlMedirAPorta(seed, nome, out string defeito, out _);
				if (defeito.Contains("mato")) noMato++;
			}
		}
		finally { SalaDePlaneta.PortaNoMatoDeTeste = false; }
		AfirmarSl("(defeito injetado: o terreiro em frente a porta nao e limpo) a regua REPROVA -- ha arvore na frente da porta",
				  noMato > 0, $"{noMato} de {amostra.Count - deSala} mundos com copa na frente da porta");

		SlDoRochedo(erguidas);
	}

	/// <summary>
	/// O ULTIMO RECURSO DA PORTA, em grades escritas a mao (o molde da bancada do pomar): a seed quase nunca sorteia
	/// um mundo sem parede de montanha ao alcance, entao os dois casos sao entregues a regra de producao com `umEm: 1`.
	/// </summary>
	private void SlDoRochedo(int erguidasNaAmostra)
	{
		const int n = 40, sx = 20, sy = 20;
		(byte[] Chao, byte[] Cobertura, byte[] Bits, byte[] Agua) Grade(ClasseDeTerreno classe)
		{
			var chao = new byte[n * n];
			Array.Fill(chao, (byte)classe);
			var agua = new byte[(n * n + 7) / 8];
			if (classe == ClasseDeTerreno.Agua)
			{
				Array.Fill(agua, (byte)0xFF);
				// a clareira do pouso, como o gerador a escava num mundo todo mar: 3x3 de planicie seca
				for (int y = sy - 1; y <= sy + 1; y++)
					for (int x = sx - 1; x <= sx + 1; x++)
					{
						int i = y * n + x;
						chao[i] = (byte)ClasseDeTerreno.Planicie;
						agua[i >> 3] &= (byte)~(1 << (i & 7));
					}
			}
			return (chao, new byte[n * n], new byte[(n * n + 7) / 8], agua);
		}
		bool Rocha((byte[] Chao, byte[] Cobertura, byte[] Bits, byte[] Agua) g, int x, int y) =>
			g.Chao[y * n + x] == (byte)ClasseDeTerreno.Montanha && (g.Bits[(y * n + x) >> 3] & (1 << ((y * n + x) & 7))) != 0;
		bool Chao((byte[] Chao, byte[] Cobertura, byte[] Bits, byte[] Agua) g, int x, int y) =>
			g.Chao[y * n + x] == (byte)ClasseDeTerreno.Planicie && (g.Bits[(y * n + x) >> 3] & (1 << ((y * n + x) & 7))) == 0
			&& (g.Agua[(y * n + x) >> 3] & (1 << ((y * n + x) & 7))) == 0;

		// ---- um mundo todo PLANICIE: nao ha montanha nenhuma ----
		var campo = Grade(ClasseDeTerreno.Planicie);
		PortaNoTerreno? r = SalaDePlaneta.AbrirAPorta(n, n, campo.Chao, campo.Cobertura, campo.Bits, campo.Agua, sx, sy, 4242, [], umEm: 1);
		AfirmarSl("O ULTIMO RECURSO (mundo sem montanha nenhuma): o gerador ERGUE um rochedo de 3x2 pra porta, a 3+ tiles do pouso, com a soleira livre ao sul",
				  r is { Erguida: true } p && Rocha(campo, p.X - 1, p.Y) && Rocha(campo, p.X, p.Y) && Rocha(campo, p.X + 1, p.Y)
				  && Rocha(campo, p.X - 1, p.Y - 1) && Rocha(campo, p.X, p.Y - 1) && Rocha(campo, p.X + 1, p.Y - 1)
				  && Chao(campo, p.Soleira.X, p.Soleira.Y) && Math.Max(Math.Abs(p.X - sx), Math.Abs(p.Y - sy)) >= 3 && p.Passos > 0,
				  r is { } q ? $"porta em ({q.X},{q.Y}), a {q.Passos} passos do pouso ({sx},{sy}); erguida: {q.Erguida}" : "nao ha porta");

		// ---- um mundo todo MAR: so a clareira do pouso e chao ----
		var mar = Grade(ClasseDeTerreno.Agua);
		r = SalaDePlaneta.AbrirAPorta(n, n, mar.Chao, mar.Cobertura, mar.Bits, mar.Agua, sx, sy, 4242, [], umEm: 1);
		AfirmarSl("...e no mundo todo MAR (sem lugar pra ela) o rochedo sobe ao norte do pouso, com a soleira SECA e encostada na clareira",
				  r is { Erguida: true } m && Rocha(mar, m.X, m.Y) && Chao(mar, m.Soleira.X, m.Soleira.Y)
				  && m.Soleira.X == sx && m.Soleira.Y == sy - 2 && Chao(mar, sx, sy - 1),
				  r is { } s ? $"porta em ({s.X},{s.Y}), soleira ({s.Soleira.X},{s.Soleira.Y}); pouso ({sx},{sy})" : "nao ha porta");

		GD.Print($"[salas]   (medida) na amostra de mundos de verdade o ultimo recurso disparou {erguidasNaAmostra} vez(es): parede de montanha ao alcance e a regra, e nao a excecao");
	}

	// =====================================================================
	// 3) O MUNDO VIVO
	// =====================================================================
	private void SlDoMundoVivo(ServerPlayer pl, SalaNoUniverso a, ZoneKey mundo)
	{
		GD.Print($"[salas] -- 3) o mundo vivo: {a.Corpo.Nome} ({MundoProcedural.DaSeed(a.Corpo.Seed, a.Corpo.Nome).Descricao()}), sala de {SalaDePlaneta.TextoDaDilatacao(a.Dilatacao)} por dia --");

		// ---- o defeito PRIMEIRO: o mundo nasce sem a porta ----
		PortaDaSalaNaoNasceDeTeste = true;
		try
		{
			SlAcordar(a.Corpo);
			AfirmarSl("(defeito injetado: a colheita do mundo nao poe a porta) a regua da porta de pe REPROVA",
					  _zonasGeradas.ContainsKey(mundo.Hash) && !_noChao.Exists(o => o.Tipo == TipoDaPorta && o.Zona.Equals(mundo)));
		}
		finally
		{
			PortaDaSalaNaoNasceDeTeste = false;
			_zonasGeradas.Remove(mundo.Hash);
			ArrancarPomar(mundo.Hash);
		}

		TerrenoGerado esperado = GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(a.Corpo.Seed, a.Corpo.Nome).Parametros());
		bool chegou = SlNaPorta(pl, a.Corpo);
		List<Obra> portas = _noChao.FindAll(o => o.Tipo == TipoDaPorta && o.Zona.Equals(mundo));
		AfirmarSl("**O MUNDO QUE TEM SALA NASCE COM A PORTA DE PE**: uma obra so, mobilia do mapa, na celula que a seed manda",
				  chegou && portas.Count == 1 && portas[0].DoMapa && esperado.PortaDaSala is { } e
				  && CatalogoDeObras.Celula(portas[0].X, portas[0].Y) == (e.X, e.Y),
				  portas.Count == 0 ? "nenhuma porta" : $"porta em {CatalogoDeObras.Celula(portas[0].X, portas[0].Y)}, a seed manda ({esperado.PortaDaSala?.X},{esperado.PortaDaSala?.Y})");
		if (!chegou || portas.Count == 0) return;

		AfirmarSl("...a celula dela barra no mapa do servidor, e da soleira a tecla E a alcanca",
				  MapaDaZona(mundo) is { } mapa && esperado.PortaDaSala is { } pt && mapa.BlockedCell(pt.X, pt.Y)
				  && !mapa.BlockedCell(pt.Soleira.X, pt.Soleira.Y) && ObraQueAceita(pl, "sala_entrar") == portas[0]);

		Estragar(portas[0], 1e12, null);
		AfirmarSl("...e ela NAO CAI nem com um golpe de 1e12 (e a mesma obra da porta do Templo)", _noChao.Contains(portas[0]));

		List<string> regras = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_regras", ""));
		string texto = SalaDePlaneta.TextoDaDilatacao(a.Dilatacao);
		AfirmarSl($"AS REGRAS DA PORTA dizem a dilatacao (\"um dia aqui fora são {texto} lá dentro\"), a expulsao, a recarga de todas e que nao ha guardiao nem comida",
				  regras.Exists(f => f.Contains($"um dia aqui fora são {texto} lá dentro")) && regras.Exists(f => f.Contains("DEVOLVE"))
				  && regras.Exists(f => f.Contains("a da Terra")) && regras.Exists(f => f.Contains("não pede chave")) && regras.Exists(f => f.Contains("não há comida")),
				  string.Join(" / ", regras.Take(3)));
		AfirmarSl("...e nao falam do Guardiao da Terra nem de prisao (isso e so da porta do Templo)",
				  regras.Count > 0 && !regras.Exists(f => f.Contains("Guardião") || f.Contains("PRESO")));
	}

	// =====================================================================
	// 4) A SALA
	// =====================================================================
	private void SlDaSala(ServerPlayer pl, SalaNoUniverso a, ZoneKey mundo)
	{
		GD.Print("[salas] -- 4) a sala: entrar, o interior, o ganho e a idade x a dilatacao, a sessao, a expulsao --");
		double d = a.Dilatacao;
		ZoneKey sala = SalaDePlaneta.Interior(mundo), daTerra = ZoneKey.Premade(ZonaDaSala);

		if (!SlNaPorta(pl, a.Corpo)) { AfirmarSl("(montagem) o corpo esta na soleira da porta", false); return; }
		pl.SalaUltimaEntrada = 0;
		pl.Idade = 25;
		pl.Ficha.Idade = 25;
		int vagasDaTerra = _naSala.Count;
		double gravLaFora = pl.Ficha.Planetgrav;

		// (2026-10-27) A SALA FECHADA NO COMECO DO MUNDO vale aqui tambem (o pedido 112): num mundo nascido agora a porta deste mundo recusa
		long nascimentoGuardado = _nascimentoDoMundo;
		string abertaGuardada = _salaAbertaPor;
		List<string> fechada;
		bool ficou;
		SalaFechadaNaBancadaDeTeste = true;
		try
		{
			_nascimentoDoMundo = NowMs();
			_salaAbertaPor = "";
			fechada = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
			ficou = pl.Zone.Equals(mundo);
		}
		finally
		{
			SalaFechadaNaBancadaDeTeste = false;
			_nascimentoDoMundo = nascimentoGuardado;
			_salaAbertaPor = abertaGuardada;
		}
		AfirmarSl("**A SALA FECHADA NO COMECO DO MUNDO** (o pedido 112): num mundo nascido agora a porta DESTE mundo tambem recusa, com a frase",
				  ficou && fechada.Any(s => s.Contains("ainda está fechada neste mundo")), string.Join(" / ", fechada));

		List<string> falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl("**ENTRAR PELA PORTA**: o corpo vai pro interior da sala DESTE mundo (o mapa da Sala, instancia do mundo)",
				  pl.Zone.Equals(sala) && SalaDoTempo.EhDePlaneta(pl.Zone) && pl.SalaPlaneta == mundo.Name && pl.SalaDilatacao == d,
				  $"zona {pl.Zone}; sessao em '{pl.SalaPlaneta}'; {string.Join(" / ", falas.Take(1))}");
		if (!pl.Zone.Equals(sala)) return;

		AfirmarSl("...e ela NAO E a Sala da Terra: nao ocupa vaga la, nao e `NaSala`, e a zona e outra",
				  !NaSala(pl) && !SalaDoTempo.EhASala(pl.Zone) && _naSala.Count == vagasDaTerra && sala.Hash != daTerra.Hash
				  && SalaDoTempo.EhUmaSala(pl.Zone) && SalaDoTempo.EhUmaSala(daTerra) && !SalaDoTempo.EhDePlaneta(daTerra));
		AfirmarSl("o interior e o MAPA da Sala: o mesmo quarto, sem borda (o vazio branco) -- numa COPIA so deste mundo, e nao no mapa da Terra",
				  MapaDaZonaOuCatalogo(sala) is { SemBorda: true } m && _catalogo?.Get(daTerra)?.Mapa is { } daPlanta
				  && m.SaiuDe(daPlanta) && !ReferenceEquals(m, daPlanta));
		AfirmarSl("**GRAVIDADE 10x** la dentro, escrita pelo funil de troca de zona (la fora o chao puxava outra coisa)",
				  Math.Abs(pl.Ficha.Planetgrav - 10) < 1e-9, $"{pl.Ficha.Planetgrav}x dentro; {gravLaFora}x la fora");
		ClimaDoPlaneta clima = ClimaDaZona(sala), climaDaTerra = ClimaDaZona(daTerra);
		AfirmarSl("**O CLIMA CAOTICO** da Sala: os mesmos climas da ficha dela -- e os outros interiores continuam sem ceu",
				  clima.Existe && clima.Permitidos.Length == climaDaTerra.Permitidos.Length && clima.Permitidos.Length >= 8
				  && !ClimaDaZona(ZoneKey.Interior("Nave", 3)).Existe
				  && RelogioDaZona(sala).TemDia == RelogioDaZona(daTerra).TemDia && RelogioDaZona(sala).TemNoite == RelogioDaZona(daTerra).TemNoite,
				  $"{clima.Permitidos.Length} climas; nave: {ClimaDaZona(ZoneKey.Interior("Nave", 3)).Existe}");
		AfirmarSl("la dentro NAO se minera: a sala nao tem reserva de planeta", ReservaDe(sala) == null);

		// ---- o ganho x a dilatacao ----
		AfirmarSl($"**O GANHO DE BP E 280 x A DILATACAO** ({280 * d:0}x aqui), escrito na ficha pelo funil",
				  Math.Abs(pl.Ficha.zoneGainMult - GainKnobs.TimeChamberMult * d) < 1e-6, $"zoneGainMult = {pl.Ficha.zoneGainMult}");
		AfirmarSl("...e a MAESTRIA de forma fica em 4x (decisao: ela e costume, nao tempo)", Math.Abs(pl.Ficha.zoneMasteryMult - SalaDoTempo.MaestriaMult) < 1e-9);
		var naTerra = new Fighter();
		SalaDoTempo.AplicarRitmo(naTerra, daTerra);
		AfirmarSl("a Sala da TERRA continua em 280x (dilatacao 1), e fora de sala o ritmo e 1x",
				  Math.Abs(naTerra.zoneGainMult - GainKnobs.TimeChamberMult) < 1e-9 && SalaDoTempo.Dilatacao(mundo) == 0 && SalaDoTempo.Dilatacao(daTerra) == 1);
		SalaDoTempo.GanhoSemDilatacaoDeTeste = true;
		try
		{
			AplicarGravidade(pl);
			AfirmarSl("(defeito injetado: a sala de planeta rende os 280x da Terra) a regua do ganho REPROVA",
					  Math.Abs(pl.Ficha.zoneGainMult - GainKnobs.TimeChamberMult * d) > 1e-6, $"zoneGainMult = {pl.Ficha.zoneGainMult}");
		}
		finally { SalaDoTempo.GanhoSemDilatacaoDeTeste = false; AplicarGravidade(pl); }

		AfirmarSl("A FRASE DA ENTRADA diz a dilatacao e a recarga -- e NENHUMA frase nova (regras, entrada) diz numero de BP",
				  falas.Exists(f => f.Contains(SalaDePlaneta.TextoDaDilatacao(d))) && falas.Exists(f => f.Contains("a da Terra"))
				  && !falas.Exists(f => f.Contains("BP")), string.Join(" / ", falas.Take(2)));

		// ---- a sessao: um dia in-game, e depois o segundo ----
		var avisos = new List<string>();
		EscutaDeAvisos = avisos;
		int idade0 = pl.Idade;
		for (int i = 0; i < 24; i++) TickDasSalasDePlaneta(60);
		int anosNoPrimeiroDia = pl.Idade - idade0;
		AfirmarSl($"**A IDADE ANDA COM A DILATACAO**: um dia in-game la dentro = {Math.Floor(d)} ano(s) a mais (na da Terra, 1)",
				  pl.Zone.Equals(sala) && anosNoPrimeiroDia == (int)Math.Floor(d) && pl.Ficha.Idade == pl.Idade,
				  $"{idade0} -> {pl.Idade} com {pl.SalaDiasDaSessao:0.###} dia(s) de sessao");
		AfirmarSl("...no meio da sessao a tela mostra a fase 'rendendo' e os minutos que faltam",
				  FaseDaSalaPraTela(pl) == 1 && Math.Abs(MinutosDaSalaPraTela(pl) - 24) < 1.5, $"fase {FaseDaSalaPraTela(pl)}, {MinutosDaSalaPraTela(pl):0.#} min");

		SalaDoTempo.IdadeSemDilatacaoDeTeste = true;
		try
		{
			AfirmarSl("(defeito injetado: a sala de planeta envelhece 1 ano por dia, como a da Terra) a regua da idade REPROVA",
					  SalaDoTempo.AnosFechados(0, 1, d) != (int)Math.Floor(d));
		}
		finally { SalaDoTempo.IdadeSemDilatacaoDeTeste = false; }
		AfirmarSl("a sala de 3 MESES nao fecha um ano na sessao inteira (declarado: a idade do port e inteira); a de 5 anos fecha DEZ",
				  SalaDoTempo.AnosFechados(0, SalaDoTempo.SessaoEmDias, 0.25) == 0 && SalaDoTempo.AnosFechados(0, SalaDoTempo.SessaoEmDias, 5) == 10
				  && SalaDoTempo.AnosFechados(0, SalaDoTempo.SessaoEmDias, 1) == 2);

		// ---- o fim: com o defeito, ela nao expulsa ----
		SalaNaoExpulsaDeTeste = true;
		try
		{
			for (int i = 0; i < 25; i++) TickDasSalasDePlaneta(60);
			AfirmarSl("(defeito injetado: a sala de planeta nao expulsa) a regua da expulsao REPROVA -- o corpo fica la dentro, sem render",
					  pl.Zone.Equals(sala) && !SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao) && Math.Abs(pl.Ficha.zoneGainMult - 1) < 1e-9,
					  $"zona {pl.Zone.Name}, {pl.SalaDiasDaSessao:0.##} dias, ritmo {pl.Ficha.zoneGainMult}x");
		}
		finally { SalaNaoExpulsaDeTeste = false; }

		TickDasSalasDePlaneta(1);
		EscutaDeAvisos = null;
		PortaNoTerreno porta = _portasDasSalas[mundo.Hash];
		(int cx, int cy) = CatalogoDeObras.Celula(pl.Pos.X, pl.Pos.Y);
		AfirmarSl("**NO FIM DOS DOIS DIAS A SALA EXPULSA**: o corpo esta de volta ao mundo, na SOLEIRA da porta, do lado de fora",
				  pl.Zone.Equals(mundo) && Math.Max(Math.Abs(cx - porta.Soleira.X), Math.Abs(cy - porta.Soleira.Y)) <= 1 && pl.SalaPlaneta.Length == 0,
				  $"zona {pl.Zone.Name}, celula ({cx},{cy}); a soleira e ({porta.Soleira.X},{porta.Soleira.Y})");
		AfirmarSl($"...com {SalaDoTempo.AnosFechados(0, 2, d)} ano(s) a mais no total (2 dias x {d}), o ritmo de volta a 1x e o chao de fora de volta",
				  pl.Idade - idade0 == SalaDoTempo.AnosFechados(0, 2, d) && Math.Abs(pl.Ficha.zoneGainMult - 1) < 1e-9
				  && Math.Abs(pl.Ficha.zoneMasteryMult - 1) < 1e-9 && Math.Abs(pl.Ficha.Planetgrav - gravLaFora) < 1e-9,
				  $"idade {idade0} -> {pl.Idade}; ritmo {pl.Ficha.zoneGainMult}x; gravidade {pl.Ficha.Planetgrav}x");
		AfirmarSl("a sessao AVISOU antes de acabar (10, 5 e 1 minuto) e disse a expulsao e o que falta da recarga -- sem BP",
				  avisos.Exists(f => f.Contains("~10 minutos")) && avisos.Exists(f => f.Contains("~1 minuto"))
				  && avisos.Exists(f => f.Contains("devolve à porta")) && avisos.Exists(f => f.Contains("qualquer uma")) && !avisos.Exists(f => f.Contains("BP")),
				  string.Join(" / ", avisos.TakeLast(2)));
		AfirmarSl("ninguem ficou PRESO: a sala de planeta nao tem janela nem tranca", !pl.SalaPreso && pl.SalaJanelaAte == 0);
	}

	// =====================================================================
	// 5) A RECARGA
	// =====================================================================
	private void SlDaRecarga(ServerPlayer pl, SalaNoUniverso a, ZoneKey mundoA, SalaNoUniverso b, ZoneKey mundoB)
	{
		GD.Print("[salas] -- 5) a recarga: 24 h x a dilatacao, e UMA pra todas (outro mundo, e o Templo) --");
		double d = a.Dilatacao;
		long dia = MsDeRecargaDaSala;

		AfirmarSl("A RECARGA E 24 h x A DILATACAO: 6 h na sala de 3 meses, 24 h na de 1 ano, 5 dias na de 5 anos",
				  MsDeRecargaDaSalaQueDilata(0.25) == dia / 4 && MsDeRecargaDaSalaQueDilata(1) == dia && MsDeRecargaDaSalaQueDilata(5) == dia * 5
				  && dia == 24L * 3_600_000);

		// O CORPO ACABOU DE SER EXPULSO da sala A: a recarga dele conta da ENTRADA.
		long fim = FimDaRecargaDasSalas(pl);
		AfirmarSl($"quem entrou na sala de {SalaDePlaneta.TextoDaDilatacao(d)} espera {d * 24:0.#} h desde a ENTRADA (sair antes ou ser expulso nao muda)",
				  fim == pl.SalaUltimaEntrada + (long)(dia * d) && fim > NowMs());

		SlNaPorta(pl, a.Corpo);
		List<string> falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl("com a recarga correndo, a MESMA porta recusa -- e a frase diz que a recarga vale pra toda Sala do Tempo",
				  pl.Zone.Equals(mundoA) && falas.Exists(f => f.Contains("TODA Sala do Tempo")), string.Join(" / ", falas));

		// ---- outra sala, de outro mundo ----
		bool naB = SlNaPorta(pl, b.Corpo);
		falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl($"**A RECARGA E COMPARTILHADA**: a porta de OUTRO mundo ({b.Corpo.Nome}, {SalaDePlaneta.TextoDaDilatacao(b.Dilatacao)} por dia) recusa tambem",
				  naB && pl.Zone.Equals(mundoB) && falas.Exists(f => f.Contains("TODA Sala do Tempo")), string.Join(" / ", falas));
		RecargaSoDaTerraDeTeste = true;
		try
		{
			falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
			AfirmarSl("(defeito injetado: a porta de planeta nao pergunta pela recarga) a regua da recarga compartilhada REPROVA -- ele entra",
					  SalaDoTempo.EhDePlaneta(pl.Zone));
		}
		finally { RecargaSoDaTerraDeTeste = false; }
		// ele entrou pela porta de B com o defeito: volta pra fora, e a recarga volta a ser a da entrada em A
		long entradaEmA = fim - (long)(dia * d);
		MoveToZone(pl.Id, mundoB, pl.Pos);
		TickDasSalasDePlaneta(1);
		pl.SalaUltimaEntrada = entradaEmA;
		pl.SalaDilatacao = d;

		// ---- a porta do TEMPLO ----
		ZoneKey lookout = ZoneKey.Premade("Lookout");
		void NoTemplo()
		{
			MoveToZone(pl.Id, lookout, new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16));
			pl.Voando = false;
			pl.SalaAutorizada = true;
		}
		NoTemplo();
		falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl("**E VALE PRA DA TERRA**: com a chave do Guardiao na mao, a porta do Templo recusa quem esta na recarga de uma sala de planeta",
				  pl.Zone.Equals(lookout) && pl.SalaAutorizada && falas.Exists(f => f.Contains("distorção temporal")), string.Join(" / ", falas));

		// ---- o relogio: um minuto antes do fim, e um minuto depois ----
		AdiantoDoRelogioDeTeste += fim - NowMs() - 60_000;
		falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		bool recusouNoFim = pl.Zone.Equals(lookout);
		RecargaSemDilatacaoDeTeste = true;
		try
		{
			AfirmarSl("(defeito injetado: a recarga e de 24 h pra toda sala) a regua da proporcao REPROVA -- o fim da espera muda",
					  FimDaRecargaDasSalas(pl) != fim, $"{(FimDaRecargaDasSalas(pl) - pl.SalaUltimaEntrada) / 3_600_000.0:0.#} h");
		}
		finally { RecargaSemDilatacaoDeTeste = false; }
		AdiantoDoRelogioDeTeste += 120_000;
		falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl($"um minuto ANTES das {d * 24:0.#} h a porta ainda recusa; um minuto DEPOIS ela abre (aqui, a do Templo: a recarga de uma libera a outra)",
				  recusouNoFim && NaSala(pl) && _naSala.ContainsKey(pl.Assinatura), $"zona {pl.Zone.Name}; {string.Join(" / ", falas.Take(1))}");

		// ---- e o contrario: quem entrou na da Terra espera 24 h pra qualquer porta de planeta ----
		AfirmarSl("entrar na da TERRA arma o mesmo relogio com a dilatacao dela (1): 24 h",
				  pl.SalaDilatacao == 1 && pl.SalaPlaneta == SalaDoTempo.PortaDaTerra && FimDaRecargaDasSalas(pl) == pl.SalaUltimaEntrada + dia
				  && Math.Abs(pl.Ficha.zoneGainMult - GainKnobs.TimeChamberMult) < 1e-9);
		MoveToZone(pl.Id, lookout, pl.Pos);
		TickDaSalaDoTempo();
		SlNaPorta(pl, a.Corpo);
		falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl("...e a porta de planeta recusa quem acabou de sair da Sala da Terra", pl.Zone.Equals(mundoA) && falas.Exists(f => f.Contains("TODA Sala do Tempo")),
				  string.Join(" / ", falas));
		AdiantoDoRelogioDeTeste += dia + 60_000;
		falas = Ouvir(() => ComandoDaSalaDoTempo(pl, "sala_entrar", ""));
		AfirmarSl("...ate as 24 h passarem: ai ela abre", SalaDoTempo.EhDePlaneta(pl.Zone));
	}

	// =====================================================================
	// 6) AS BORDAS
	// =====================================================================
	private void SlDasBordas(ServerPlayer p1, ServerPlayer p2, ServerPlayer p3, SalaNoUniverso a, ZoneKey mundo, ref string? chaveMorta)
	{
		GD.Print("[salas] -- 6) as bordas: lotacao, as saidas, a carona, o logout com o mundo dormindo, o planeta destruido --");
		ZoneKey sala = SalaDePlaneta.Interior(mundo);
		double d = a.Dilatacao;

		// p1 esta DENTRO (entrou no fim da secao 5). ---- a lotacao ----
		SlNaPorta(p2, a.Corpo);
		ComandoDaSalaDoTempo(p2, "sala_entrar", "");
		SlNaPorta(p3, a.Corpo);
		List<string> falas = Ouvir(() => ComandoDaSalaDoTempo(p3, "sala_entrar", ""));
		AfirmarSl("A LOTACAO E A DA TERRA: dois dentro, e o TERCEIRO e recusado na porta -- sem gastar a recarga dele",
				  p1.Zone.Equals(sala) && p2.Zone.Equals(sala) && p3.Zone.Equals(mundo) && p3.SalaUltimaEntrada == 0
				  && falas.Exists(f => f.Contains("máximo")), string.Join(" / ", falas));
		SalaDePlanetaSemLotacaoDeTeste = true;
		try
		{
			ComandoDaSalaDoTempo(p3, "sala_entrar", "");
			AfirmarSl("(defeito injetado: a sala de planeta nao tem lotacao) a regua da lotacao REPROVA -- o terceiro entra", p3.Zone.Equals(sala));
		}
		finally { SalaDePlanetaSemLotacaoDeTeste = false; }

		// ---- sair pela passagem de dentro ----
		p3.Pos = new Vec2(145 * ZoneCollision.TileSize + 16, 339 * ZoneCollision.TileSize + 8);
		p3.Facing = Facing.North;
		p3.Moving = true;
		_acabouDeAtravessar.Remove(p3.Id);
		_naBocaDaPassagem.Remove(p3.Id);
		falas = Ouvir(TickDasPassagens);
		PortaNoTerreno porta = _portasDasSalas[mundo.Hash];
		(int cx, int cy) = CatalogoDeObras.Celula(p3.Pos.X, p3.Pos.Y);
		AfirmarSl("**A SAIDA DE DENTRO leva de volta a PORTA DO MUNDO** (no mapa ela esta cravada pro Templo Sagrado), e fecha a sessao",
				  p3.Zone.Equals(mundo) && Math.Max(Math.Abs(cx - porta.Soleira.X), Math.Abs(cy - porta.Soleira.Y)) <= 1 && p3.SalaPlaneta.Length == 0,
				  $"zona {p3.Zone.Name}, celula ({cx},{cy}); {string.Join(" / ", falas.Take(1))}");
		long recargaDoP3 = FimDaRecargaDasSalas(p3);
		AfirmarSl("...e sair antes NAO encurta a recarga: ela continua contando da entrada", recargaDoP3 == p3.SalaUltimaEntrada + (long)(MsDeRecargaDaSala * d));

		// ---- sair por fora (teleporte, admin) ----
		ZoneKey terra = ZoneKey.Premade("Earth");
		SairPorForaMantemASessaoDeTeste = true;
		try
		{
			MoveToZone(p2.Id, terra, SpawnPos);
			TickDasSalasDePlaneta(1);
			AfirmarSl("(defeito injetado: quem sai por fora leva a sessao aberta) a regua REPROVA -- a sessao segue aberta na Terra",
					  p2.SalaPlaneta.Length > 0);
		}
		finally { SairPorForaMantemASessaoDeTeste = false; }
		TickDasSalasDePlaneta(1);
		AfirmarSl("QUEM SAI POR FORA DA PORTA (teleporte, admin, morte) fecha a sessao onde estiver -- e a recarga fica: nao ha drible",
				  p2.SalaPlaneta.Length == 0 && FimDaRecargaDasSalas(p2) > NowMs() && Math.Abs(p2.Ficha.zoneGainMult - 1) < 1e-9);
		AfirmarSl("...e nao se CHEGA a uma sala de planeta por teleporte (a mesma lista que fecha a da Terra, a mente e o selo)",
				  ZonaFechadaAoTeleporte(sala) && ZonaFechadaAoTeleporte(ZoneKey.Premade(ZonaDaSala)) && !ZonaFechadaAoTeleporte(mundo));

		// ---- entrar de carona: la dentro sem ter atravessado a porta ----
		MoveToZone(p2.Id, sala, p1.Pos);
		AfirmarSl("QUEM ESTA LA DENTRO SEM TER ATRAVESSADO A PORTA (o passageiro de uma fusao, um corpo posto por admin) NAO RENDE: ritmo 1x",
				  p2.Zone.Equals(sala) && p2.SalaPlaneta.Length == 0 && Math.Abs(p2.Ficha.zoneGainMult - 1) < 1e-9 && FaseDaSalaPraTela(p2) == 0);
		SemSessaoTambemRendeDeTeste = true;
		try
		{
			AplicarGravidade(p2);
			AfirmarSl("(defeito injetado: sem sessao tambem rende) a regua da carona REPROVA", p2.Ficha.zoneGainMult > 1);
		}
		finally { SemSessaoTambemRendeDeTeste = false; AplicarGravidade(p2); }
		ASalaDePlanetaTemOutraSaida(p2);
		AfirmarSl("...e a saida dele nao tem porta a que voltar: vai pro berco", !SalaDoTempo.EhUmaSala(p2.Zone) && !p2.Zone.Equals(mundo), p2.Zone.ToString());

		// ---- o transe: mergulhar na propria mente la dentro ----
		Vec2 ondeEstava = p1.Pos;
		ZoneKey mente = DimensaoMental.De(p1.Id);
		// (palco) O CORPO LARGADO: quem medita deixa um boneco deitado no chao, e e por ele que o servidor sabe onde o
		// corpo esta DE VERDADE (`LugarFisicoDe`). Corpo forjado nao larga o corpo (nao tem `Peer`), entao o boneco e
		// posto a mao -- o caminho de producao inteiro (`LargarOCorpo`/`VoltarProCorpo`) e o da `--salapresencateste`.
		p1.BonecoLargado = new ServerPlayer { Id = IdBaseDasSalasDeTeste + 7, Zone = sala, Pos = ondeEstava, Ficha = new Fighter() };
		TranseFechaASessaoDeTeste = true;
		try
		{
			MoveToZone(p1.Id, mente, ondeEstava);
			TickDasSalasDePlaneta(1);
			AfirmarSl("(defeito injetado: o transe fecha a sessao) a regua do transe REPROVA", p1.SalaPlaneta.Length == 0);
		}
		finally { TranseFechaASessaoDeTeste = false; }
		p1.SalaPlaneta = mundo.Name;   // devolve o que o defeito apagou
		double diasAntesDoTranse = p1.SalaDiasDaSessao;
		for (int i = 0; i < 5; i++) TickDasSalasDePlaneta(60);
		bool abertaNaMente = p1.Zone.Equals(mente) && p1.SalaPlaneta.Length > 0 && p1.SalaDiasDaSessao == diasAntesDoTranse;
		p1.BonecoLargado = null;   // antes da volta: com o boneco na mao o `MoveToZone` arrancaria o transe por conta propria
		MoveToZone(p1.Id, sala, ondeEstava);
		AfirmarSl("O TRANSE LA DENTRO NAO FECHA A SESSAO: na mente ela para de contar (nao rende, nao envelhece), e na volta continua",
				  abertaNaMente && TemSessaoNaSalaDePlaneta(p1) && Math.Abs(p1.Ficha.zoneGainMult - GainKnobs.TimeChamberMult * d) < 1e-6,
				  $"aberta na mente: {abertaNaMente}; de volta, ritmo {p1.Ficha.zoneGainMult}x");

		// ---- deslogar la dentro, com o mundo fora da memoria na volta ----
		double diasDoP1 = p1.SalaDiasDaSessao = 0.75;
		CharacterSave save = AccountStore.DeJogador(p1, NowMs());
		var volta = new ServerPlayer
		{
			Id = IdBaseDasSalasDeTeste + 9, Peer = null, Conta = p1.Conta, Slot = 0, Race = "Human", Genero = "Male",
			Ficha = new Fighter { Race = "Human", BP = 1000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		AccountStore.ParaJogador(save, volta);
		volta.Livro ??= new Jandirus.Core.Skills.SkillBook();
		AfirmarSl("**DESLOGAR LA DENTRO**: o save leva a zona da sala, os dias gastos, a dilatacao e o mundo da porta",
				  save.ZonaTipo == ZoneKey.KindInterior && save.Zona == ZonaDaSala && save.ZonaSeed == mundo.Seed
				  && volta.SalaPlaneta == mundo.Name && volta.SalaDilatacao == d && volta.SalaDiasDaSessao == diasDoP1 && volta.SalaUltimaEntrada == p1.SalaUltimaEntrada,
				  $"zona {save.Zona}#{save.ZonaSeed} tipo {save.ZonaTipo}; sessao em '{save.SalaPlaneta}'");

		// o servidor "reiniciou": o mundo e a porta sairam da memoria, e ele volta JA dentro
		ZoneList(p1.Zone.Hash).Remove(p1);
		_players.Remove(p1.Id);
		_zonasGeradas.Remove(mundo.Hash);
		ArrancarPomar(mundo.Hash);
		FecharAPortaDaSala(mundo.Hash);
		int vagas = _naSala.Count;
		volta.Zone = new ZoneKey(save.ZonaTipo, save.Zona, save.ZonaSeed);
		volta.Name = p1.Name;
		PorNoMundo(volta);
		SalaDoTempoNoLogin(volta);
		AplicarGravidade(volta);
		AfirmarSl("quem volta ja dentro CONTINUA a sessao de onde parou (rende, e nao ocupa vaga da Sala da Terra)",
				  TemSessaoNaSalaDePlaneta(volta) && Math.Abs(volta.Ficha.zoneGainMult - GainKnobs.TimeChamberMult * d) < 1e-6 && _naSala.Count == vagas
				  && !_noChao.Exists(o => o.Tipo == TipoDaPorta && o.Zona.Equals(mundo)),
				  $"ritmo {volta.Ficha.zoneGainMult}x; porta de pe: {_noChao.Exists(o => o.Tipo == TipoDaPorta && o.Zona.Equals(mundo))}");

		// a trava de PAREDE: ele dormiu mais que o dobro da sessao -- o primeiro tique fecha a conta e expulsa
		AdiantoDoRelogioDeTeste += (long)SalaDoTempo.TravaDeParedeEmMs + 60_000;
		for (int i = 0; i < 4000 && SalaDoTempo.EhDePlaneta(volta.Zone); i++)
		{
			TickDasSalasDePlaneta(1);
			TickDasGeracoes();
			if (SalaDoTempo.EhDePlaneta(volta.Zone)) System.Threading.Thread.Sleep(5);
		}
		bool portaDeVolta = _portasDasSalas.TryGetValue(mundo.Hash, out porta);
		(cx, cy) = CatalogoDeObras.Celula(volta.Pos.X, volta.Pos.Y);
		AfirmarSl("**...E A EXPULSAO ACORDA O MUNDO**: com ele fora da memoria (o reinicio), a sala espera o mundo nascer e larga o corpo na soleira",
				  volta.Zone.Equals(mundo) && portaDeVolta && Math.Max(Math.Abs(cx - porta.Soleira.X), Math.Abs(cy - porta.Soleira.Y)) <= 1
				  && volta.SalaPlaneta.Length == 0 && _noChao.Exists(o => o.Tipo == TipoDaPorta && o.Zona.Equals(mundo)),
				  $"zona {volta.Zone.Name}, celula ({cx},{cy})");
		ZoneList(volta.Zone.Hash).Remove(volta);
		_players.Remove(volta.Id);
		EsquecerSessaoDaSala(volta.Id);

		// ---- o save antigo ----
		// (o mesmo save de cima, como ele seria num servidor de ontem: sem os dois campos novos)
		var antigo = new ServerPlayer { Id = IdBaseDasSalasDeTeste + 8, Ficha = new Fighter() };
		long agora = NowMs();
		save.SalaDilatacao = 0;
		save.SalaPlaneta = null;
		save.SalaUltimaEntrada = agora - 23L * 3_600_000;
		AccountStore.ParaJogador(save, antigo);
		AfirmarSl("O SAVE ANTIGO CARREGA: sem a dilatacao gravada, a recarga de quem ja esperava e a da Terra (24 h) -- falta 1 h",
				  antigo.SalaDilatacao == 0 && antigo.SalaPlaneta.Length == 0
				  && Math.Abs(FimDaRecargaDasSalas(antigo) - agora - 3_600_000) < 2000, $"faltam {(FimDaRecargaDasSalas(antigo) - agora) / 60_000.0:0} min");

		// ---- o planeta destruido com gente la dentro ----
		p3.SalaUltimaEntrada = 0;
		SlNaPorta(p3, a.Corpo);
		ComandoDaSalaDoTempo(p3, "sala_entrar", "");
		bool dentro = p3.Zone.Equals(sala);
		chaveMorta = ChaveDePlaneta.Da(mundo)!.Value.Texto;
		_mortos.Por(new EstadoDaMorte { Chave = chaveMorta, Nome = mundo.Name, Fase = FaseDaMorte.Destruido });
		TickDasSalasDePlaneta(60);
		AfirmarSl("O PLANETA DESTRUIDO com gente na sala dele: quem esta la dentro NAO morre nem e arrancado -- a sessao continua",
				  dentro && p3.Zone.Equals(sala) && ZonaMorta(mundo) && SalaDoTempo.SessaoValendo(p3.SalaDiasDaSessao));
		falas = Ouvir(() => ASalaDePlanetaTemOutraSaida(p3));
		AfirmarSl("...e a porta abre pro NADA: quem sai (ou e expulso) cai no espaco, onde o mundo ficava",
				  Espaco.EhEspaco(p3.Zone) && p3.SalaPlaneta.Length == 0 && falas.Exists(f => f.Contains("virou pó")), $"zona {p3.Zone.Name}; {string.Join(" / ", falas.Take(1))}");

		// ---- o verb de admin ----
		MoveToZone(p3.Id, terra, SpawnPos);
		falas = Ouvir(() => AdminIrParaASalaDoTempo(p3, ""));
		AfirmarSl("O VERB DE ADMIN, no chao, LISTA as salas por perto (mundo, dilatacao, distancia) e manda decolar -- sem mudar de zona",
				  p3.Zone.Equals(terra) && falas.Exists(f => f.Contains("Sala do Tempo em")) && falas.Exists(f => f.Contains("decole primeiro")),
				  string.Join(" / ", falas.Take(2)));
		MoveToZone(p3.Id, ZonaDoEspaco, new Vec2(0, 0));
		falas = Ouvir(() => AdminIrParaASalaDoTempo(p3, ""));
		SalaNoUniverso? perto = SalaDePlaneta.NaRegiao(SeedDoUniverso, 0, 0, RaiosDaBuscaDeSala[^1])
			.Where(s => !ZonaMorta(Espaco.ZonaDe(s.Corpo))).OrderBy(s => s.Corpo.Pos.Length).Cast<SalaNoUniverso?>().FirstOrDefault();
		AfirmarSl("...e do espaco ele salta pra orbita do mundo com sala mais perto (o destruido fica de fora)",
				  perto is { } alvo && (p3.Pos - Espaco.PontoDeDecolagem(alvo.Corpo)).Length < 4 && Espaco.EhEspaco(p3.Zone),
				  string.Join(" / ", falas.TakeLast(1)));
	}
}
