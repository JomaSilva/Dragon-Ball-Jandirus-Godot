using System.Text.Json;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO POMAR (`--pomarteste`): todo mundo sorteado tem macieira, perto de onde se chega.
///
/// O dono (2026-10-10): *"para facilitar a vida em planetas procedurais, esses planetas sempre devem ter uma
/// arvore frutifera (maça) pro player nunca morrer de fome"*.
///
/// A irma de console dela (`procedural`, no AssetPipeline) mede a REGRA em milhares de mundos: onde as
/// celulas caem, se sao chao firme, se a quantidade acompanha o tamanho. Esta mede o que so existe com o
/// servidor de pe -- o FUNIL: o corpo pousa pelo caminho de producao (`PlanetaSob` -> a geracao fora do
/// tique -> `PousarEmProcedural`), e a arvore tem de estar la como obra, barrando o corpo e dando maca pelo
/// verbo da tecla E.
///
/// No boot, como as outras de servidor: ninguem logado, e um corpo forjado (sem `Peer`) que pousa de verdade.
/// O universo e o DESTE servidor: na pasta de saves vazia da bancada a seed e sorteada, entao cada rodada
/// visita outros planetas (a seed sai na primeira linha, pra repetir a rodada que reprovar).
///
/// AS PERGUNTAS:
///   1. EM CADA MUNDO: as macieiras de pe sao as que a seed manda, ha uma a ate
///      <see cref="Pomar.RaioDaChegada"/> tiles do pouso, todas em chao firme, e a da chegada da maca.
///   2. A PODA: o mundo que sai da memoria leva as macieiras DELE, e so as dele; voltando, elas voltam.
///   3. A CONTRAPROVA: com o defeito injetado (`Pomar.PlanetaSemPomarDeTeste`) a regua de 1 reprova.
///   4. A REBROTA: derrubada, ela volta no prazo -- e espera se houver um corpo em cima.
///   5. O DISCO: o pomar nao vai pro `mundo.json`; a macieira que alguem plantou vai.
///   6. O MUNDO FRIO: quem entra sem pousar (o login num mundo que o servidor ainda nao tem) faz o mundo
///      nascer com o corpo ja dentro -- e o pomar nasce do mesmo jeito.
///
///     Godot --headless -- --server --port 8140 --pomarteste
/// </summary>
public partial class GameServer
{
	private int _pmOk, _pmFalhou;

	private void AfirmarPm(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _pmOk++; GD.Print($"[pomar]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _pmFalhou++; GD.PrintErr($"[pomar]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>Id longe de qualquer jogador de verdade, e de toda outra bancada.</summary>
	private const int IdDaCobaiaDoPomar = 96_400;

	public void RodarBancadaDoPomar()
	{
		_pmOk = _pmFalhou = 0;
		GD.Print($"[pomar] ================ O POMAR DOS MUNDOS SORTEADOS (dono, 2026-10-10) -- universo {SeedDoUniverso} ================");

		var reais = new HashSet<Obra>(_noChao);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		long rondaDeAntes = _proximaRondaDoPomar;
		var visitadas = new List<ZoneKey>();
		ServerPlayer? cobaia = null;

		try
		{
			AfirmarPm("(montagem) o catalogo de obras tem a macieira, e ela e densa",
					  _obras?.Get(CatalogoDeItens.ArvoreDeMaca) is { Densa: true });

			// UM MUNDO A MAIS QUE O CACHE AGUENTA: o ultimo pouso e o que faz a poda de producao rodar.
			List<PlanetaNoEspaco> mundos = MundosDoPomarDeTeste(TetoDeZonasGeradas + 1);
			var biomas = new HashSet<BiomaDeTerreno>(mundos.Select(p => MundoProcedural.DaSeed(p.Seed, p.Nome).Bioma));
			AfirmarPm($"(montagem) {TetoDeZonasGeradas + 1} mundos sorteados VIVOS do universo deste servidor, de biomas e tamanhos variados",
					  mundos.Count == TetoDeZonasGeradas + 1 && biomas.Count >= 3,
					  string.Join(" | ", mundos.Select(p => $"{p.Nome} {MundoProcedural.DaSeed(p.Seed, p.Nome).Lado}")));
			if (mundos.Count < TetoDeZonasGeradas + 1 || _obras?.Get(CatalogoDeItens.ArvoreDeMaca) == null) return;

			cobaia = ForjarCobaiaDoPomar();

			// ---- 1) em cada mundo ----
			GD.Print("[pomar] -- 1) em cada mundo: pousar pelo funil -> o pomar esta de pe, perto, e da maca --");
			Obra? plantada = null;
			for (int i = 0; i < TetoDeZonasGeradas; i++)
			{
				PmDoMundo(cobaia, mundos[i], visitadas);
				if (i == 0) plantada = PmPlantarASemente(cobaia);
			}

			// ---- 2) a poda ----
			PmDaPoda(cobaia, mundos, visitadas, plantada);

			// ---- 3) a contraprova ----
			// NO SEGUNDO MUNDO, e nao no primeiro: la ha uma macieira plantada a dois tiles do pouso, e a
			// tecla E a acharia -- a contraprova mediria a semente, e nao a falta do pomar.
			PmDoDefeito(cobaia, mundos[0], mundos[1]);

			// ---- 4) a rebrota ----
			PmDaRebrota(cobaia, mundos[1]);

			// ---- 5) o disco ----
			PmDoDisco(cobaia, plantada);

			// ---- 6) o mundo frio ----
			// O TERCEIRO MUNDO: a poda da secao 2 o tirou da memoria, e ninguem voltou la.
			PmDoMundoFrio(cobaia, mundos[2]);
		}
		catch (Exception e)
		{
			AfirmarPm($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			Pomar.PlanetaSemPomarDeTeste = false;
			PomarNaoRebrotaDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			_proximaRondaDoPomar = rondaDeAntes;

			// O CORPO SAI PRIMEIRO: ele esta numa lista de zona e no `_players`.
			if (cobaia != null)
			{
				ZoneList(cobaia.Zone.Hash).Remove(cobaia);
				_players.Remove(cobaia.Id);
				_avisadosDeEspera.Remove(cobaia.Id);
				_avisadosDePlanetaMorto.Remove(cobaia.Id);
			}

			// OS MUNDOS DA BANCADA SAEM DA MEMORIA, com o pomar e o estrago deles; e o que ela plantou sai do
			// chao e do disco.
			foreach (ZoneKey z in visitadas)
			{
				_zonasGeradas.Remove(z.Hash);
				ArrancarPomar(z.Hash);
				_cenarioCaido.Remove(ChaveDeEstado(z));
			}
			if (_noChao.RemoveAll(o => !reais.Contains(o)) > 0) GravarMundo();

			AfirmarPm("a bancada devolve o mundo como pegou (nenhum corpo, nenhuma obra e nenhum pomar a mais)",
					  !_players.ContainsKey(IdDaCobaiaDoPomar) && _noChao.Count == reais.Count
					  && visitadas.TrueForAll(z => !_pomares.ContainsKey(z.Hash)),
					  $"obras={_noChao.Count}/{reais.Count} pomares={_pomares.Count}");

			GD.Print($"[pomar] ================ {_pmOk} OK, {_pmFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	/// <summary>
	/// <paramref name="quantos"/> MUNDOS SORTEADOS do universo deste servidor: um de cada bioma, o MAIOR que
	/// aparecer (e o que tem mais setores) e, pra completar, os menores -- gerar custa pela area.
	/// </summary>
	private List<PlanetaNoEspaco> MundosDoPomarDeTeste(int quantos)
	{
		var candidatos = new List<(PlanetaNoEspaco P, MundoProcedural M)>();
		for (int i = 2; i <= 400 && candidatos.Count < 120; i++)
		{
			if (Sistemas.Do(SeedDoUniverso, i * 2, i * 3 + 1) is not { Ancorado: false } sistema) continue;
			foreach (PlanetaNoEspaco p in sistema.Planetas())
				if (!p.Premade && !ZonaMorta(Espaco.ZonaDe(p)))
					candidatos.Add((p, MundoProcedural.DaSeed(p.Seed, p.Nome)));
		}

		var escolhidos = new List<PlanetaNoEspaco>();
		var chaves = new HashSet<ulong>();
		void Escolher(PlanetaNoEspaco p)
		{
			if (escolhidos.Count < quantos && chaves.Add(Espaco.ZonaDe(p).Hash)) escolhidos.Add(p);
		}

		foreach (BiomaDeTerreno b in Enum.GetValues<BiomaDeTerreno>())
		{
			var doBioma = candidatos.Where(c => c.M.Bioma == b).OrderBy(c => c.M.Lado).ToList();
			if (doBioma.Count > 0) Escolher(doBioma[0].P);
		}
		if (candidatos.Count > 0) Escolher(candidatos.MaxBy(c => c.M.Lado).P);
		foreach ((PlanetaNoEspaco p, _) in candidatos.OrderBy(c => c.M.Lado)) Escolher(p);
		return escolhidos;
	}

	private ServerPlayer ForjarCobaiaDoPomar()
	{
		var novo = new ServerPlayer
		{
			Id = IdDaCobaiaDoPomar,
			Peer = null,
			Name = "bancada: o faminto",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDoEspaco,
			Pos = new Vec2(0, 0),
			Conta = "bancada_pomar",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 1000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		return novo;
	}

	private static (int X, int Y) CelulaDoCorpo(Vec2 pos) => CatalogoDeObras.Celula(pos.X, pos.Y);

	private static Vec2 CorpoNaCelulaDe(int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		return new Vec2(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
	}

	/// <summary>As macieiras de pe nesta zona -- as do pomar e as de quem plantou.</summary>
	private List<Obra> MacieirasDaZona(ZoneKey zona) =>
		[.. _noChao.Where(o => o.Tipo == CatalogoDeItens.ArvoreDeMaca && o.Zona.Equals(zona))];

	// =====================================================================
	// A REGUA
	// =====================================================================
	/// <summary>
	/// O POMAR DESTE MUNDO ESTA DE PE E CERTO? A regua das secoes 1, 2 e 3 -- uma so, pra a contraprova
	/// reprovar exatamente o que a prova aprova. <paramref name="esperado"/> e o mundo gerado de novo pela
	/// seed, aqui na bancada: o servidor nao guarda o terreno, so a colisao.
	/// </summary>
	private bool PomarEstaCerto(ZoneKey zona, TerrenoGerado esperado, out string detalhe)
	{
		detalhe = "";
		if (MapaDaZonaOuCatalogo(zona) is not { } mapa) { detalhe = "o mundo nao esta carregado"; return false; }

		List<Obra> dePe = [.. MacieirasDaZona(zona).Where(o => o.DoMapa)];
		var celulas = new HashSet<(int, int)>(dePe.Select(o => CatalogoDeObras.Celula(o.X, o.Y)));
		int setores = Pomar.SetoresPorLado(esperado.Largura) * Pomar.SetoresPorLado(esperado.Altura);

		int longe = dePe.Count == 0 ? -1
			: celulas.Min(c => Math.Max(Math.Abs(c.Item1 - esperado.SpawnCelX), Math.Abs(c.Item2 - esperado.SpawnCelY)));
		detalhe = $"{dePe.Count} de pe ({esperado.Macieiras.Count} na seed, {setores} setor(es)); a mais perto do pouso a {longe} tile(s)";

		if (dePe.Count == 0) { detalhe += " | NENHUMA macieira"; return false; }
		if (!celulas.SetEquals(esperado.Macieiras.Select(c => (c.X, c.Y)))) { detalhe += " | nao sao as celulas da seed"; return false; }
		if (longe < 1 || longe > Pomar.RaioDaChegada) { detalhe += " | nenhuma no raio da chegada"; return false; }
		if (dePe.Count - 1 > setores) { detalhe += " | mais espalhadas que setores"; return false; }

		foreach (Obra o in dePe)
		{
			(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
			if (!o.Aparafusada || o.DonoConta.Length > 0)
			{ detalhe += $" | a de ({cx},{cy}) nao e mobilia do lugar"; return false; }

			// CHAO FIRME pelo mapa que o servidor usa: nao nasceu parede (montanha/arvore), nao e agua, nao e
			// beirada -- e AGORA barra, porque a macieira e densa e a camada de obras ja foi escrita.
			if (mapa.BloqueadaNoArquivo(cx, cy) || mapa.EhAgua(cx, cy) || mapa.NaBorda(cx, cy) || !mapa.BlockedCell(cx, cy))
			{
				detalhe += $" | a de ({cx},{cy}) esta mal assentada (parede {mapa.BloqueadaNoArquivo(cx, cy)}, agua {mapa.EhAgua(cx, cy)}, "
						   + $"beirada {mapa.NaBorda(cx, cy)}, barra {mapa.BlockedCell(cx, cy)})";
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// A CELULA MAIS PERTO DO POUSO DE ONDE A MACIEIRA ESTA AO ALCANCE DA MAO, andando -- passo em cruz, pela
	/// colisao que o corpo sente (parede, obra e agua barram quem anda). Nulo = nao se chega a pe.
	/// </summary>
	private static (int X, int Y)? ChaoAoLadoDe(ZoneCollision mapa, (int X, int Y) daqui, (int X, int Y) arvore)
	{
		const int janela = 2 * Pomar.RaioDaChegada;
		var vistas = new HashSet<(int, int)> { daqui };
		var fila = new Queue<(int X, int Y)>();
		fila.Enqueue(daqui);
		while (fila.Count > 0)
		{
			(int x, int y) = fila.Dequeue();
			if (Math.Max(Math.Abs(x - arvore.X), Math.Abs(y - arvore.Y)) <= 1) return (x, y);

			foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
			{
				int nx = x + dx, ny = y + dy;
				if (Math.Max(Math.Abs(nx - daqui.X), Math.Abs(ny - daqui.Y)) > janela) continue;
				if (mapa.Bloqueia(nx, ny, ModoDeTravessia.APe)) continue;
				if (vistas.Add((nx, ny))) fila.Enqueue((nx, ny));
			}
		}
		return null;
	}

	// =====================================================================
	// 1) EM CADA MUNDO
	// =====================================================================
	private void PmDoMundo(ServerPlayer pl, PlanetaNoEspaco p, List<ZoneKey> visitadas)
	{
		MundoProcedural ficha = MundoProcedural.DaSeed(p.Seed, p.Nome);
		ZoneKey zona = Espaco.ZonaDe(p);
		visitadas.Add(zona);

		bool pousou = PousarDeVerdade(pl, p);
		AfirmarPm($"{p.Nome}: o corpo POUSA pelo funil de producao ({ficha.Descricao()})", pousou && pl.Zone.Equals(zona), pl.Zone.ToString());
		if (!pousou || MapaDaZonaOuCatalogo(zona) is not { } mapa) return;

		TerrenoGerado esperado = GeradorDeTerreno.Gerar(ficha.Parametros());
		(int X, int Y) pouso = CelulaDoCorpo(pl.Pos);

		bool certo = PomarEstaCerto(zona, esperado, out string detalhe);
		AfirmarPm($"{p.Nome}: **O POMAR ESTA DE PE** -- as macieiras da seed, uma delas a ate {Pomar.RaioDaChegada} tiles do pouso, todas em chao firme e barrando",
				  certo && pouso == (esperado.SpawnCelX, esperado.SpawnCelY), detalhe);
		if (!certo) return;

		// ---- A DA CHEGADA DA MACA: anda-se ate ela e colhe-se pelo verbo da tecla E ----
		(int X, int Y) celula = esperado.Macieiras[0];
		Obra? arvore = ObraNaCelula(zona, celula.X, celula.Y);
		(int X, int Y)? aoLado = ChaoAoLadoDe(mapa, pouso, celula);
		if (arvore == null || aoLado is not { } chao)
		{
			AfirmarPm($"{p.Nome}: do pouso se CHEGA A PE ate a macieira da chegada", false,
					  $"pouso ({pouso.X},{pouso.Y}), macieira ({celula.X},{celula.Y}), obra {(arvore == null ? "nenhuma" : "de pe")}");
			return;
		}

		pl.Pos = CorpoNaCelulaDe(chao.X, chao.Y);
		int antes = pl.Mochila.Quantos(CatalogoDeItens.Maca);
		List<string> falas = Ouvir(() => ComandoDeInteracao(pl, "colher", ""));
		AfirmarPm($"{p.Nome}: **ELA DA MACA** -- do pouso se chega a pe ao lado dela, e o verbo da tecla E poe uma maca na mochila (restam 9 de 10)",
				  pl.Mochila.Quantos(CatalogoDeItens.Maca) == antes + 1 && MacasDe(arvore.Id) == 9 && Disse(falas, "você colhe uma maçã"),
				  $"pouso ({pouso.X},{pouso.Y}) -> ao lado ({chao.X},{chao.Y}) da macieira ({celula.X},{celula.Y}) | macas={pl.Mochila.Quantos(CatalogoDeItens.Maca)} no pe={MacasDe(arvore.Id)} | {Ultimos(falas)}");
		pl.Pos = CorpoNaCelulaDe(pouso.X, pouso.Y);
	}

	/// <summary>
	/// PLANTA UMA SEMENTE no mundo em que o corpo esta, pelo funil de producao (`posicionar`): a macieira DE
	/// ALGUEM, que as secoes 2 e 5 usam pra provar que o pomar nao a confunde com as dele.
	/// </summary>
	private Obra? PmPlantarASemente(ServerPlayer pl)
	{
		if (MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa) return null;
		(int X, int Y) aqui = CelulaDoCorpo(pl.Pos);

		// um chao livre a dois tiles do corpo (o alcance de assentar e tres), que nao seja do pomar
		(int X, int Y)? canteiro = null;
		for (int dy = -2; dy <= 2 && canteiro == null; dy++)
			for (int dx = -2; dx <= 2 && canteiro == null; dx++)
				if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == 2 && mapa.ServeDeChao(aqui.X + dx, aqui.Y + dy)
					&& ObraNaCelula(pl.Zone, aqui.X + dx, aqui.Y + dy) == null)
					canteiro = (aqui.X + dx, aqui.Y + dy);
		if (canteiro is not { } c) { AfirmarPm("(montagem) ha chao livre ao lado do pouso pra plantar uma semente", false); return null; }

		pl.Mochila.Guardar(CatalogoDeItens.SementeDeMaca);
		const int T = ZoneCollision.TileSize;
		List<string> falas = Ouvir(() => ComandoDeTech(pl, "posicionar",
			$"{CatalogoDeItens.SementeDeMaca}/{c.X * T + T / 2}/{c.Y * T + T / 2 - (int)MoveRules.FeetOffsetY}"));
		Obra? plantada = ObraNaCelula(pl.Zone, c.X, c.Y);
		AfirmarPm("a SEMENTE continua plantando num mundo sorteado, ao lado do pomar: nasce uma macieira COM DONO, que vai pro disco",
				  plantada is { Tipo: CatalogoDeItens.ArvoreDeMaca, DoMapa: false } && plantada.DonoConta == pl.Conta,
				  $"({c.X},{c.Y}) | {Ultimos(falas)}");
		return plantada;
	}

	// =====================================================================
	// 2) A PODA
	// =====================================================================
	private void PmDaPoda(ServerPlayer pl, List<PlanetaNoEspaco> mundos, List<ZoneKey> visitadas, Obra? plantada)
	{
		GD.Print("[pomar] -- 2) a poda: o mundo que sai da memoria leva as macieiras dele (e so as dele) --");

		List<ZoneKey> velhas = [.. visitadas];
		int noChaoAntes = _noChao.Count, doPomarAntes = _noChao.Count(o => o.DoMapa && velhas.Exists(z => z.Equals(o.Zona)));
		AfirmarPm($"(montagem) o cache esta no teto ({TetoDeZonasGeradas} mundos), cada um com o pomar dele",
				  velhas.TrueForAll(z => _zonasGeradas.ContainsKey(z.Hash) && _pomares.ContainsKey(z.Hash)) && doPomarAntes >= velhas.Count,
				  $"{_zonasGeradas.Count} mundos, {_pomares.Count} pomares, {doPomarAntes} macieiras de pomar");

		// O NONO POUSO: a colheita da geracao chama a poda DE PRODUCAO, e os oito de antes estao vazios.
		PlanetaNoEspaco nono = mundos[^1];
		ZoneKey zonaDoNono = Espaco.ZonaDe(nono);
		visitadas.Add(zonaDoNono);
		bool pousou = PousarDeVerdade(pl, nono);

		int sobraram = _noChao.Count(o => o.DoMapa && velhas.Exists(z => z.Equals(o.Zona)));
		AfirmarPm("**A PODA LEVA O POMAR**: os mundos descarregados nao deixam macieira nenhuma na lista de obras, nem registro de pomar",
				  pousou && velhas.TrueForAll(z => !_zonasGeradas.ContainsKey(z.Hash) && !_pomares.ContainsKey(z.Hash)) && sobraram == 0,
				  $"{doPomarAntes} antes, {sobraram} depois; obras no chao {noChaoAntes} -> {_noChao.Count}");
		AfirmarPm("...e a macieira PLANTADA por alguem fica: ela e do `mundo.json`, nao do pomar",
				  plantada != null && _noChao.Contains(plantada));
		AfirmarPm($"...e o mundo que acabou de nascer ({nono.Nome}) tem o dele",
				  PomarEstaCerto(zonaDoNono, GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(nono.Seed, nono.Nome).Parametros()), out string d9), d9);

		// DE VOLTA AO PRIMEIRO: ele nasce de novo, com o mesmo pomar, e a plantada continua la.
		PlanetaNoEspaco primeiro = mundos[0];
		ZoneKey zona = Espaco.ZonaDe(primeiro);
		pousou = PousarDeVerdade(pl, primeiro);
		TerrenoGerado esperado = GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(primeiro.Seed, primeiro.Nome).Parametros());
		bool certo = PomarEstaCerto(zona, esperado, out string detalhe);
		AfirmarPm($"voltando a {primeiro.Nome}, o mundo renasce com O MESMO pomar (e funcao da seed, nao de arquivo)", pousou && certo, detalhe);
		AfirmarPm("...sem engolir nem duplicar a plantada: as macieiras da zona sao as do pomar mais uma",
				  plantada != null && MacieirasDaZona(zona).Count == esperado.Macieiras.Count + 1,
				  $"{MacieirasDaZona(zona).Count} na zona, {esperado.Macieiras.Count} na seed");
	}

	// =====================================================================
	// 3) A CONTRAPROVA
	// =====================================================================
	private void PmDoDefeito(ServerPlayer pl, PlanetaNoEspaco carregado, PlanetaNoEspaco p)
	{
		GD.Print("[pomar] -- 3) a contraprova: o mundo de antes do pedido --");

		// O MUNDO SAI DA MEMORIA POR FORA DA PODA (e o que uma bancada faz, e o que um wipe faria): o pomar
		// fica orfao, e quem o recolhe e a ronda.
		ZoneKey orfa = Espaco.ZonaDe(carregado);
		bool tinha = _pomares.ContainsKey(orfa.Hash) && MacieirasDaZona(orfa).Exists(o => o.DoMapa);
		MoveToZone(pl.Id, ZonaDoEspaco, carregado.Pos);
		_zonasGeradas.Remove(orfa.Hash);
		_proximaRondaDoPomar = 0;
		TickDoPomar();
		AfirmarPm("a RONDA recolhe o pomar de um mundo que saiu da memoria sem passar pela poda",
				  tinha && !_pomares.ContainsKey(orfa.Hash) && !MacieirasDaZona(orfa).Exists(o => o.DoMapa),
				  $"tinha pomar: {tinha}; {MacieirasDaZona(orfa).Count(o => o.DoMapa)} macieira(s) de pomar sobraram");

		ZoneKey zona = Espaco.ZonaDe(p);
		TerrenoGerado esperado = GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(p.Seed, p.Nome).Parametros());

		Pomar.PlanetaSemPomarDeTeste = true;
		bool pousou, certo;
		string detalhe;
		List<string> falas;
		int macas = pl.Mochila.Quantos(CatalogoDeItens.Maca);
		try
		{
			pousou = PousarDeVerdade(pl, p);
			certo = PomarEstaCerto(zona, esperado, out detalhe);
			falas = Ouvir(() => ComandoDeInteracao(pl, "colher", ""));
		}
		finally { Pomar.PlanetaSemPomarDeTeste = false; }
		AfirmarPm("(defeito injetado: o mundo nasce sem pomar) a MESMA regua reprova o MESMO mundo, e no pouso a tecla E nao acha o que colher",
				  pousou && !certo && pl.Mochila.Quantos(CatalogoDeItens.Maca) == macas && Disse(falas, "não há nada por perto pra colher"),
				  $"{detalhe} | {Ultimos(falas)}");

		// E DE VOLTA: sem o defeito, o mesmo mundo renasce com o pomar.
		MoveToZone(pl.Id, ZonaDoEspaco, p.Pos);
		_zonasGeradas.Remove(zona.Hash);
		_proximaRondaDoPomar = 0;
		TickDoPomar();
		pousou = PousarDeVerdade(pl, p);
		AfirmarPm("...e sem o defeito o mesmo mundo volta a nascer com o pomar", pousou && PomarEstaCerto(zona, esperado, out detalhe), detalhe);
	}

	// =====================================================================
	// 4) A REBROTA
	// =====================================================================
	private void PmDaRebrota(ServerPlayer pl, PlanetaNoEspaco p)
	{
		GD.Print($"[pomar] -- 4) a rebrota: derrubada, a macieira volta em {Pomar.SegundosPraRebrotar} s --");

		ZoneKey zona = Espaco.ZonaDe(p);
		if (MapaDaZonaOuCatalogo(zona) is not { } mapa || !_pomares.TryGetValue(zona.Hash, out PomarDaZona? pomar))
		{ AfirmarPm("(montagem) o mundo e o pomar dele estao vivos", false); return; }

		(int cx, int cy) = pomar.Celulas[0];
		long ronda = Pomar.SegundosEntreRondas * 1000L, prazo = Pomar.SegundosPraRebrotar * 1000L;
		void Rondar(long depoisDe) { AdiantoDoRelogioDeTeste += depoisDe; _proximaRondaDoPomar = 0; TickDoPomar(); }
		bool DePe() => ObraNaCelula(zona, cx, cy) is { Tipo: CatalogoDeItens.ArvoreDeMaca, DoMapa: true };

		// ---- cai no primeiro golpe, como toda mobilia de mapa ----
		if (ObraNaCelula(zona, cx, cy) is not { } arvore) { AfirmarPm("(montagem) a macieira da chegada esta de pe", false); return; }
		Estragar(arvore, 1e12, null);
		AfirmarPm("a macieira do pomar CAI como toda mobilia de mapa: some da lista e a celula deixa de barrar",
				  !DePe() && !mapa.BlockedCell(cx, cy));

		Rondar(0);
		Rondar(prazo - 2 * ronda);
		AfirmarPm($"antes do prazo ela NAO volta (a ronda so marca desde quando a celula esta vazia)", !DePe());

		PomarNaoRebrotaDeTeste = true;
		try { Rondar(3 * ronda); }
		finally { PomarNaoRebrotaDeTeste = false; }
		AfirmarPm("(defeito injetado: a macieira caida nao rebrota) passado o prazo, a celula continua vazia", !DePe());

		Rondar(ronda);
		Obra? nova = ObraNaCelula(zona, cx, cy);
		AfirmarPm("**PASSADO O PRAZO ELA REBROTA** na mesma celula: mobilia do lugar, barrando, e cheia (10 macas)",
				  DePe() && nova != null && nova.Id != arvore.Id && mapa.BlockedCell(cx, cy) && MacasDe(nova.Id) == 10,
				  nova == null ? "nao voltou" : $"obra #{nova.Id} (a que caiu era a #{arvore.Id}), {MacasDe(nova.Id)} macas");
		if (nova == null) return;

		// ---- com um corpo em cima, ela ESPERA ----
		Estragar(nova, 1e12, null);
		Vec2 antes = pl.Pos;
		pl.Pos = CorpoNaCelulaDe(cx, cy);
		Rondar(0);
		Rondar(prazo + ronda);
		AfirmarPm("COM UM CORPO EM CIMA do canteiro a macieira ESPERA (ela e densa: nascer ali prenderia quem esta parado)", !DePe());

		pl.Pos = antes;
		Rondar(ronda);
		AfirmarPm("...e na ronda seguinte a ele sair, ela nasce", DePe());
	}

	// =====================================================================
	// 5) O DISCO
	// =====================================================================
	private void PmDoDisco(ServerPlayer pl, Obra? plantada)
	{
		GD.Print("[pomar] -- 5) o disco: o pomar nao e gravado --");

		string copia = System.IO.Path.Combine(_store?.Pasta ?? ".", "mundo.da-bancada-do-pomar.json");
		try
		{
			GravarMundoEm(copia);
			List<Obra>? gravadas = System.IO.File.Exists(copia)
				? JsonSerializer.Deserialize<List<Obra>>(System.IO.File.ReadAllText(copia), new JsonSerializerOptions { IncludeFields = true })
				: null;
			int doPomar = MacieirasDaZona(pl.Zone).Count(o => o.DoMapa);
			AfirmarPm("o `mundo.json` NAO leva as macieiras do pomar (elas renascem da seed) e leva a que alguem plantou",
					  gravadas != null && doPomar > 0 && !gravadas.Exists(o => o.DoMapa)
					  && plantada != null && gravadas.Exists(o => o.Id == plantada.Id && o.Tipo == CatalogoDeItens.ArvoreDeMaca),
					  $"{gravadas?.Count} obra(s) no arquivo; {doPomar} macieira(s) de pomar de pe em {pl.Zone.Name}");
		}
		finally { if (System.IO.File.Exists(copia)) System.IO.File.Delete(copia); }
	}

	// =====================================================================
	// 6) O MUNDO FRIO
	// =====================================================================
	/// <summary>
	/// QUEM ENTRA SEM POUSAR. O login num mundo sorteado que o servidor ainda nao tem (o personagem que
	/// deslogou la e volta depois de um reinicio) nao passa pelo pouso: o corpo ja esta no chao, e o mundo
	/// nasce em volta dele (`GameServer.MundoFrio.cs`). O pomar tem de nascer do mesmo jeito -- ele e
	/// plantado na COLHEITA da geracao, e nao no pouso -- e so a macieira que cairia em cima do corpo espera.
	/// </summary>
	private void PmDoMundoFrio(ServerPlayer pl, PlanetaNoEspaco p)
	{
		GD.Print("[pomar] -- 6) o mundo frio: quem entra sem pousar (o login num mundo sorteado) --");

		ZoneKey zona = Espaco.ZonaDe(p);
		TerrenoGerado esperado = GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(p.Seed, p.Nome).Parametros());
		if (_zonasGeradas.ContainsKey(zona.Hash) || _gerando.ContainsKey(zona.Hash) || esperado.Macieiras.Count == 0)
		{ AfirmarPm($"(montagem) {p.Nome} esta fora da memoria do servidor, e tem pomar na seed", false); return; }

		// O CORPO ENTRA DIRETO NO CHAO, e em cima do canteiro da chegada: o pior lugar que ele podia escolher.
		(int cx, int cy) = esperado.Macieiras[0];
		MoveToZone(pl.Id, zona, CorpoNaCelulaDe(cx, cy));
		for (int i = 0; i < 400 && !_zonasGeradas.ContainsKey(zona.Hash); i++)
		{
			TickDasGeracoes();
			if (!_zonasGeradas.ContainsKey(zona.Hash)) System.Threading.Thread.Sleep(5);
		}

		int dePe = MacieirasDaZona(zona).Count(o => o.DoMapa);
		AfirmarPm("**O MUNDO QUE NASCE COM GENTE DENTRO TAMBEM GANHA O POMAR**: todas as macieiras da seed, menos a que nasceria em cima do corpo",
				  _zonasGeradas.ContainsKey(zona.Hash) && pl.Zone.Equals(zona)
				  && dePe == esperado.Macieiras.Count - 1 && ObraNaCelula(zona, cx, cy) == null,
				  $"mundo vivo: {_zonasGeradas.ContainsKey(zona.Hash)}; {dePe} de pe, {esperado.Macieiras.Count} na seed; o corpo esta em ({cx},{cy})");

		// ELE SAI DE CIMA: a ronda marca a celula vazia e, passado o prazo, a arvore nasce.
		pl.Pos = CorpoNaCelulaDe(esperado.SpawnCelX, esperado.SpawnCelY);
		_proximaRondaDoPomar = 0;
		TickDoPomar();
		AdiantoDoRelogioDeTeste += (Pomar.SegundosPraRebrotar + Pomar.SegundosEntreRondas) * 1000L;
		_proximaRondaDoPomar = 0;
		TickDoPomar();
		AfirmarPm("...e depois que ele sai de cima do canteiro a ronda completa o pomar", PomarEstaCerto(zona, esperado, out string detalhe), detalhe);
	}
}
