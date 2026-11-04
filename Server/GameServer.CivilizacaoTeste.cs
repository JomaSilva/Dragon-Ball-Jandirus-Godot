using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS CIVILIZACOES (`--civteste`, o pedido 227, 2026-11-02). O dono: *"15% dos planetas procedurais vao ter civilizaçao, com
/// isso vc pode fazer cidades procedurais com casas e estradas ligando essas casas etc, e os npcs desse planeta andando normalmente [...]
/// a raça vai ter uma aparencia procedural [...] nunca faça eles como uma das raças normais/principais do jogo [...] mantenha um padrao de
/// cor de pele e sprite no planeta [...] eles falam a lingua nativa deles [...] eles ja vem acostumados com a gravidade padrao do planeta
/// natal deles, e quanto mais forte a gravidade maior a media de poder deles"*.
///   C1. 15% DOS MUNDOS (em 20 mil sementes), e o Yardrat nunca;
///   C2. os NOMES DE RACA: todos procedurais, nenhum de raca do jogo, quase sem repeticao -- e nenhuma raca do jogo passa por procedural;
///   C3. a RACA PELO NOME: as duas pontas tiram o mesmo, e os dois corpos (a gente pintada e a folha de alien) aparecem;
///   C4. a CIDADE: casas duras e barrando, fora da agua; a estrada liga a praca a cada porta e ao pouso; e a mesma nas duas geracoes;
///       CONTRA-EXEMPLO (`Civilizacao.NenhumaDeTeste`): o mesmo mundo sem cidade;
///   C5. o PODER PELA GRAVIDADE: o molde de um mundo de 40x tem 40 vezes o poder do de 1x;
///   C6. o POVO, pousando de verdade: nasce com o mundo, na cidade, todo da raca do mundo, acostumado a gravidade, falando a lingua dela;
///       e o mundo que fica na memoria nao ganha um segundo povo;
///   C7. o PADRAO DO POVO: a mesma folha de corpo pra todos (por genero), a mesma cor de pele variando so o tom, e a cor de cabelo comum
///       sendo a mais vista.
///
///     Godot --path . --headless --server --port 12980 --civteste
/// </summary>
public sealed partial class GameServer
{
	private int _cvOk, _cvFalhou;

	private void AfirmarCv(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _cvOk++; GD.Print($"[civ]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _cvFalhou++; GD.PrintErr($"[civ]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>
	/// O MUNDO COM CIVILIZACAO DA BANCADA: o primeiro da seed de fabrica (`SementeLegada`, o mesmo em toda rodada) com cidade, gravidade
	/// ate <paramref name="gravidadeMaxima"/> e lado pequeno -- e, se pedido, de pele livre ou de alien.
	/// </summary>
	internal static (PlanetaNoEspaco Planeta, MundoProcedural Ficha, TerrenoGerado Terreno)? MundoCivilizadoDeTeste(
		double gravidadeMaxima = 2, bool? peleLivre = null, int pular = 0, double gravidadeMinima = 0)
	{
		for (int i = 2; i <= 600; i++)
		{
			if (Sistemas.Do(SementeLegada, i * 2, i * 3 + 1) is not { Ancorado: false } sistema) continue;
			foreach (PlanetaNoEspaco p in sistema.Planetas())
			{
				if (p.Premade || !Civilizacao.Tem(p)) continue;
				MundoProcedural m = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (m.Gravidade > gravidadeMaxima || m.Gravidade < gravidadeMinima || m.Lado > 256) continue;
				if (peleLivre is { } pl && RacaProcedural.Do(RacaProcedural.NomeDoMundo(p.Seed)).DePeleLivre != pl) continue;
				TerrenoGerado t = GeradorDeTerreno.Gerar(m.Parametros());
				if (t.Cidade == null) continue;
				if (pular-- > 0) continue;
				return (p, m, t);
			}
		}
		return null;
	}

	/// <summary>Pousa o corpo num mundo sorteado pela seed, pelo funil de producao (o mundo nasce fora do tique: pede-se ate ele existir).</summary>
	private bool CvPousar(ServerPlayer pl, PlanetaNoEspaco p)
	{
		var zona = ZoneKey.Procedural(p.Nome, p.Seed);
		var falso = new PlanetaNoEspaco { Nome = p.Nome, Seed = p.Seed, Pos = pl.Pos, Raio = 150 };
		for (int i = 0; i < 600 && !pl.Zone.Equals(zona); i++)
		{
			TickDasGeracoes();
			PousarEmProcedural(pl, falso);
			if (!pl.Zone.Equals(zona)) System.Threading.Thread.Sleep(5);
		}
		return pl.Zone.Equals(zona);
	}

	public void RodarBancadaDaCivilizacao()
	{
		_cvOk = _cvFalhou = 0;
		GD.Print("[civ] ================ AS CIVILIZACOES DOS MUNDOS SORTEADOS (dono, 2026-11-02, o pedido 227) ================");
		ServerPlayer? cobaia = null;
		try
		{
			// ---- C1. 15% ----
			int civ = 0, total = 20_000, yardrat = 0;
			for (ulong s = 1; s <= (ulong)total; s++)
			{
				ulong seed = Espaco.Misturar(s, 0xC1C1, 7);
				if (Civilizacao.Tem(seed, "Mundo")) civ++;
				if (Civilizacao.Tem(seed, Yardrat.Nome)) yardrat++;
			}
			double fracao = civ / (double)total;
			AfirmarCv("C1. **15% DOS MUNDOS SORTEADOS TEM CIVILIZACAO** (o numero do dono, em 20 mil sementes) -- e o Yardrat, que ja tem povo, nunca",
					  fracao is >= 0.14 and <= 0.16 && yardrat == 0, $"{civ} de {total} ({fracao:P2}); o Yardrat {yardrat}");

			// ---- C2. os nomes ----
			var nomes = new List<string>();
			for (ulong s = 1; nomes.Count < 3000; s++)
			{
				ulong seed = Espaco.Misturar(s, 0xC2C2, 9);
				if (Civilizacao.Tem(seed, "Mundo")) nomes.Add(RacaProcedural.NomeDoMundo(seed));
			}
			int distintos = nomes.Distinct(StringComparer.OrdinalIgnoreCase).Count();
			List<string> doJogo = [.. nomes.Where(n => RacaProcedural.RacasDoJogo.Contains(n) || _racas?.Get(n) != null)];
			List<string> racasDoArquivo = [.. _racas?.Protos.Keys ?? Enumerable.Empty<string>()];
			List<string> racasQuePassam = [.. racasDoArquivo.Where(r => RacaProcedural.EhProcedural(r))];
			AfirmarCv("C2. os NOMES DE RACA sao todos procedurais e nenhum e de raca do jogo, quase sem repetir (3 mil povos) -- e nenhuma raca do arquivo passa por procedural",
					  nomes.All(RacaProcedural.EhProcedural) && doJogo.Count == 0 && distintos >= nomes.Count * 0.99 && racasDoArquivo.Count >= 20
					  && racasQuePassam.Count == 0,
					  $"{distintos} distintos de {nomes.Count}; exemplos {string.Join(", ", nomes.Take(12))}; do jogo {doJogo.Count}; "
					  + $"racas do arquivo {racasDoArquivo.Count}, que passam {string.Join(",", racasQuePassam)}");

			// ---- C3. a raca pelo nome ----
			List<RacaProcedural> racas = [.. nomes.Take(600).Select(RacaProcedural.Do)];
			int pintadas = racas.Count(r => r.DePeleLivre);
			bool iguais = nomes.Take(50).All(n => RacaProcedural.Do(n) is var a && RacaProcedural.Do(n) is var b
														   && a.Folha == b.Folha && a.Pele.Equals(b.Pele) && a.DePeleLivre == b.DePeleLivre);
			bool folhasDeAlien = racas.Where(r => !r.DePeleLivre).All(r => RacaProcedural.FolhasDeAlien.Contains(r.Folha));
			AfirmarCv("C3. a RACA SAI DO NOME (as duas pontas tiram a mesma) -- e os dois corpos aparecem: a gente pintada e a folha de alien",
					  iguais && folhasDeAlien && pintadas > racas.Count / 4 && pintadas < racas.Count * 3 / 4,
					  $"{pintadas} de {racas.Count} de pele livre; as folhas de alien {string.Join(",", racas.Where(r => !r.DePeleLivre).Select(r => r.Folha).Distinct())}");

			// ---- C4. a cidade ----
			// UM MUNDO MAIS PESADO QUE A TERRA (1,2x a 2,5x): num de 1x o "acostumado a gravidade" passaria sem a linha que acostuma
			if (MundoCivilizadoDeTeste(2.5, gravidadeMinima: 1.2) is not { } achado) { AfirmarCv("(montagem) ha um mundo com cidade (1,2x a 2,5x) na seed de fabrica", false); return; }
			(PlanetaNoEspaco planeta, MundoProcedural ficha, TerrenoGerado t) = achado;
			CidadeProcedural c = t.Cidade!;
			var problemas = new List<string>();
			foreach (CidadeProcedural.Casa casa in c.Casas)
				for (int y = casa.Y; y < casa.Y + casa.Altura; y++)
					for (int x = casa.X; x < casa.X + casa.Largura; x++)
					{
						if (!t.Colisao.BlockedCell(x, y)) problemas.Add($"casa nao barra em ({x},{y})");
						if (!t.Colisao.Indestrutivel(x, y)) problemas.Add($"casa nao e dura em ({x},{y})");
						if (t.ClasseEm(x, y) == ClasseDeTerreno.Agua) problemas.Add($"casa na agua em ({x},{y})");
					}
			foreach (int i in c.Estradas)
				if (t.Colisao.BlockedCell(i % t.Largura, i / t.Largura)) problemas.Add($"estrada barrada em ({i % t.Largura},{i / t.Largura})");
			// a rede: da praca, andando so por estrada, chega-se a toda soleira e ao lado do pouso
			var alcancadas = new HashSet<int>();
			var fila = new Queue<int>();
			int praca = c.CentroY * t.Largura + c.CentroX;
			if (c.Estradas.Contains(praca)) { alcancadas.Add(praca); fila.Enqueue(praca); }
			while (fila.TryDequeue(out int i))
				foreach (int v in new[] { i + 1, i - 1, i + t.Largura, i - t.Largura })
					if (c.Estradas.Contains(v) && alcancadas.Add(v)) fila.Enqueue(v);
			int soleirasLigadas = c.Casas.Count(k => alcancadas.Contains(k.Soleira.Y * t.Largura + k.Soleira.X));
			bool pousoLigado = new[] { (0, 0), (0, 1), (1, 0), (-1, 0), (0, -1) }
				.Any(d => alcancadas.Contains((t.SpawnCelY + d.Item2) * t.Largura + t.SpawnCelX + d.Item1));
			int distancia = Math.Max(Math.Abs(c.CentroX - t.SpawnCelX), Math.Abs(c.CentroY - t.SpawnCelY));
			TerrenoGerado t2 = GeradorDeTerreno.Gerar(ficha.Parametros());
			bool mesma = t2.Cidade is { } c2 && c2.Celulas().Select(k => (k.X, k.Y, k.Tile)).SequenceEqual(c.Celulas().Select(k => (k.X, k.Y, k.Tile)))
						 && t2.Assinatura() == t.Assinatura();
			AfirmarCv($"C4. **A CIDADE** de {planeta.Nome}: {c.Casas.Count} casas duras e barrando, fora da agua; a ESTRADA liga a praca a cada porta e ao pouso; e a mesma nas duas geracoes",
					  c.Casas.Count >= CidadeProcedural.CasasMinimas && problemas.Count == 0 && soleirasLigadas == c.Casas.Count && pousoLigado
					  && c.LigadaAoPouso && distancia is >= CidadeProcedural.DistanciaMinima - 1 and <= CidadeProcedural.DistanciaMaxima + 1 && mesma,
					  $"estilo {c.Desenho.Nome}; {c.Estradas.Count} celulas de estrada; soleiras ligadas {soleirasLigadas}/{c.Casas.Count}; pouso {pousoLigado}; "
					  + $"a {distancia} tiles do pouso; mesma {mesma}; {string.Join("; ", problemas.Take(5))}");
			Civilizacao.NenhumaDeTeste = true;
			TerrenoGerado semCiv;
			try { semCiv = GeradorDeTerreno.Gerar(ficha.Parametros()); }
			finally { Civilizacao.NenhumaDeTeste = false; }
			AfirmarCv("   CONTRA-EXEMPLO (defeito injetado: nenhum mundo tem civilizacao, o jogo de antes): o mesmo mundo nasce sem cidade, e o chao e outro",
					  semCiv.Cidade == null && semCiv.Assinatura() != t.Assinatura(), $"cidade {semCiv.Cidade != null}");

			// ---- C5. o poder pela gravidade ----
			// duas sementes de mentira (nao as do mundo da bancada: o molde dele tem de nascer com a gravidade dele, no C6)
			MoldeDeNpc? leve = MoldeDaCivilizacao(0xF00D_0001UL, 1);
			MoldeDeNpc? pesado = MoldeDaCivilizacao(0xF00D_0002UL, 40);
			AfirmarCv("C5. **QUANTO MAIS PESADO O MUNDO, MAIS FORTE O POVO**: o molde de um mundo de 40x tem 40 vezes o poder do de 1x (o habitante de sempre vezes a gravidade)",
					  leve != null && pesado != null && _moldes?.Get("cidadao") is { } comum
					  && Math.Abs(leve.BpMin - comum.BpMin) < 1e-6 && Math.Abs(pesado.BpMin - comum.BpMin * 40) < 1e-6 && Math.Abs(pesado.BpMax - comum.BpMax * 40) < 1e-6,
					  $"1x: {leve?.BpMin:N0}-{leve?.BpMax:N0}; 40x: {pesado?.BpMin:N0}-{pesado?.BpMax:N0}");

			// ---- C6. o povo, pousando de verdade ----
			cobaia = ForjarComSkills("bancada civilizacao", new Vec2(0, 0), 50_000);
			cobaia.Ficha.GravMastered = 100;
			bool pousou = CvPousar(cobaia, planeta);
			var zona = ZoneKey.Procedural(planeta.Nome, planeta.Seed);
			string raca = RacaProcedural.NomeDoMundo(planeta.Seed);
			List<ServerPlayer> povo = [.. ZoneList(zona.Hash).Where(p => p.Peer == null && p.Papel?.Molde.Id.StartsWith("civ_", StringComparison.Ordinal) == true)];
			int esperados = Civilizacao.Habitantes(c);
			bool todosDaRaca = povo.Count > 0 && povo.All(p => p.Race == raca && p.Ficha.Race == raca);
			bool acostumados = povo.All(p => p.Ficha.GravMastered >= ficha.Gravidade);
			bool falamANativa = povo.All(p => LinguaDoHabitante(p) == raca);
			bool naCidade = povo.All(p =>
			{
				(int px, int py) = ((int)MathF.Floor(p.Pos.X / ZoneCollision.TileSize), (int)MathF.Floor((p.Pos.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize));
				for (int dy = -3; dy <= 3; dy++)
					for (int dx = -3; dx <= 3; dx++)
						if (c.EhEstrada(px + dx, py + dy)) return true;
				return false;
			});
			int poderBaixo = povo.Count(p => p.Ficha.BP < (_moldes?.Get("cidadao")?.BpMin ?? 0) * RacaProcedural.FatorDePoder(ficha.Gravidade) * 0.5);
			PovoarACivilizacao(ficha, GeradorDeTerreno.Gerar(ficha.Parametros()));
			int depoisDeNovo = ZoneList(zona.Hash).Count(p => p.Peer == null && p.Papel?.Molde.Id.StartsWith("civ_", StringComparison.Ordinal) == true);
			AfirmarCv($"C6. **O POVO NASCE COM O MUNDO**, pousando de verdade em {planeta.Nome}: na cidade, todo da raca {raca}, acostumado a gravidade ({ficha.Gravidade:0.#}x), falando a lingua {raca} -- e o mundo na memoria nao ganha um segundo povo",
					  pousou && povo.Count == esperados && todosDaRaca && acostumados && falamANativa && naCidade && poderBaixo == 0 && depoisDeNovo == povo.Count,
					  $"pousou {pousou}; {povo.Count} de {esperados}; da raca {todosDaRaca}; acostumados {acostumados}; lingua {falamANativa} "
					  + $"({LinguaDoHabitante(povo.FirstOrDefault() ?? cobaia)}); na cidade {naCidade}; abaixo do poder {poderBaixo}; de novo {depoisDeNovo}; "
					  + $"nomes {string.Join(", ", povo.Take(8).Select(p => p.Name))}");

			// ---- C7. o padrao do povo ----
			RacaProcedural rp = RacaProcedural.Do(raca);
			string Folha(ServerPlayer p)
			{
				List<string> lista = _visual?.CorposDe(p.Race).Para(p.Genero) ?? [];
				return p.Visual.Corpo >= 0 && p.Visual.Corpo < lista.Count ? System.IO.Path.GetFileNameWithoutExtension(lista[p.Visual.Corpo]) : "?";
			}
			var folhas = povo.GroupBy(p => p.Genero).ToDictionary(g => g.Key, g => g.Select(Folha).Distinct().ToList());
			bool umaFolha = folhas.Values.All(l => l.Count == 1 && l[0] != "?");
			bool pele, cabelo;
			string detalhe;
			if (rp.DePeleLivre)
			{
				(double hBase, double sBase, _) = CvHsv(rp.Pele);
				List<(double H, double S, double V)> tons = [.. povo.Select(p => p.Visual.CorPele is { } cp ? CvHsv(cp) : (-1, -1, -1))];
				pele = tons.All(x => x.H >= 0 && (sBase < 0.1 || CvDistanciaDeMatiz(x.H, hBase) <= 10)) && tons.Select(x => Math.Round(x.V, 2)).Distinct().Count() > 1;
				List<Rgb?> cores = [.. povo.Select(p => p.Visual.CorCabelo)];
				int nComum = cores.Count(x => x is { } k && k.Equals(rp.Cabelos[0]));
				int maisVista = cores.GroupBy(x => x).Max(g => g.Count());
				cabelo = nComum > 0 && nComum == maisVista;
				detalhe = $"pele de base {rp.Pele} (matiz {hBase:0}); tons {string.Join(" ", tons.Select(x => $"{x.H:0}/{x.V:0.00}"))}; cabelo comum {rp.Cabelos[0]} em {nComum} de {cores.Count}";
			}
			else
			{
				pele = povo.All(p => p.Visual.CorPele == null) && povo.Select(p => p.Visual.Tom).Distinct().Count() > 1;
				cabelo = povo.All(p => p.Visual.Cabelo == "Bald");
				detalhe = $"folha de alien {rp.Folha}; tons {string.Join(",", povo.Select(p => p.Visual.Tom))}";
			}
			AfirmarCv($"C7. **O PADRAO DO POVO** ({(rp.DePeleLivre ? "gente pintada" : "folha de alien")}): a mesma folha de corpo pra todos, a mesma cor de pele variando so o tom, e o cabelo comum sendo o mais visto",
					  umaFolha && pele && cabelo, $"folhas {string.Join("; ", folhas.Select(kv => kv.Key + ": " + string.Join(",", kv.Value)))}; {detalhe}");
		}
		catch (Exception e)
		{
			AfirmarCv($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			Civilizacao.NenhumaDeTeste = false;
			if (cobaia != null)
			{
				ZoneList(cobaia.Zone.Hash).Remove(cobaia);
				_players.Remove(cobaia.Id);
			}
			GD.Print($"[civ] ================ {_cvOk} passaram, {_cvFalhou} falharam ================");
		}
	}

	private static (double H, double S, double V) CvHsv(Rgb c)
	{
		double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
		double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
		return (h < 0 ? h + 360 : h, max == 0 ? 0 : d / max, max);
	}

	private static double CvDistanciaDeMatiz(double a, double b)
	{
		double d = Math.Abs(a - b) % 360;
		return d > 180 ? 360 - d : d;
	}
}
