using System.Globalization;
using System.Text;
using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS BLACK STAR -- um set so, espalhado por planetas (dono, 2026-10-21).
///
/// Mora fora de <c>_sets</c> e de <c>_esferas</c> de proposito: a policia de planeta, o
/// <c>PorQueAsEsferasPrendem</c> e o tique que derruba quem sai do mundo varrem aquelas listas.
/// As comuns continuam presas. Estas nao. O disco, a mochila e o pacote <c>S2C.Esferas</c>
/// ganham uma linha cada nos arquivos que ja escrevem -- o corpo esta aqui.
///
/// (2026-10-23, dono) O DESEJO TEM PRECO E A LISTA TEM UM PEDIDO SO DELAS: o planeta onde elas foram usadas comeca a se partir
/// (<see cref="DepoisDoDesejoNegro"/>), e "Aprender a Danca da Fusao" (<see cref="DesejoDaDancaDaFusao"/>) so existe aqui.
/// </summary>
public sealed partial class GameServer
{
	private readonly List<EsferaNegra> _negras = [];
	private SetDeEsferas _setNegro = CriarSetNegro();
	private ZoneKey _zonaDoDragaoNegro;

	/// <summary>DEFEITO INJETADO (bancada --esferanegrateste): o sal da espalhada e sempre o cravado. Duas espalhadas caem no mesmo lugar. Falso em jogo.</summary>
	internal static bool SalFixoDeTeste;

	/// <summary>DEFEITO INJETADO: a esfera assenta na celula (0,0), fora do habitat (agua, borda ou ilhota). Falso em jogo.</summary>
	internal static bool EsferaForaDoHabitatDeTeste;

	/// <summary>DEFEITO INJETADO: sair do planeta espalha a Black Star, como se ela prendesse. Falso em jogo.</summary>
	internal static bool NegraPrendeNoPlanetaDeTeste;

	/// <summary>DEFEITO INJETADO: o Supremo entra na lista das Black Star. Falso em jogo -- ele exige compra na estatua.</summary>
	internal static bool BlackStarComSupremoDeTeste;

	/// <summary>DEFEITO INJETADO: o efeito numerico vale 1x, nao 5. Lido por <see cref="FatorNegro"/>.</summary>
	internal static bool BlackStarSemMultiplicadorDeTeste;

	/// <summary>DEFEITO INJETADO: o boot reassenta pelo sal ate a esfera largada no chao, e ela pula de celula. Lido por <see cref="CarregarNegras"/>. Falso em jogo.</summary>
	internal static bool ReassentarTudoNoBootDeTeste;

	/// <summary>DEFEITO INJETADO: quem sai do jogo carregando, mesmo num planeta, faz a esfera se espalhar. Lido por <see cref="TickDasNegras"/>. Falso em jogo.</summary>
	internal static bool DeslogarEspalhaDeTeste;

	/// <summary>(2026-10-23) DEFEITO INJETADO (bancada --esferanegrateste, familia X): o desejo NAO parte o planeta onde as Black Star foram usadas -- o jogo de antes. Lido por <see cref="DepoisDoDesejoNegro"/>. Falso em jogo.</summary>
	internal static bool NegraNaoExplodeOPlanetaDeTeste;

	/// <summary>(2026-10-23) DEFEITO INJETADO (bancada --esferanegrateste, familia X): o sorteio so pula planeta MORTO, como antes -- uma Black Star pode cair no planeta que vai explodir. Lido por <see cref="SortearPlanetas"/>. Falso em jogo.</summary>
	internal static bool NegraCaiNoPlanetaCondenadoDeTeste;

	/// <summary>(2026-10-23) DEFEITO INJETADO (bancada --esferanegrateste, familia D): a Danca da Fusao entra na lista de TODO dragao, e nao so na das Black Star. Lido pelo <c>MenuDaqui</c> (`GameServer.Desejos.cs`). Falso em jogo.</summary>
	internal static bool DancaEmTodoDragaoDeTeste;

	/// <summary>(2026-10-23) DEFEITO INJETADO (bancada --esferanegrateste, familia D): o desejo da Danca a poe no livro sem a marca de ensinada -- e o tique dos cargos a tira. Lido por <see cref="DesejoDaDancaDaFusao"/>. Falso em jogo.</summary>
	internal static bool DancaSemSerEnsinadaDeTeste;

	/// <summary>(2026-10-23) DEFEITO INJETADO (bancada --esferanegrateste, familia D): quem ja sabe a Danca pode pedi-la de novo -- a janela a acende e o pedido e gasto. Lido por <see cref="DesejoDaDancaDaFusao"/> e pelo <c>EntradaDoDesejo</c> (`GameServer.MenuDoDragao.cs`). Falso em jogo.</summary>
	internal static bool DancaRepetidaDeTeste;

	private static SetDeEsferas CriarSetNegro() => new()
	{
		Id = EsferasNegras.IdDoSet,
		Desejos = EsferasNegras.Desejos,
		OffTime = EsferasNegras.OffTimeEmAnos,
		Poder = EsferasNegras.PoderDoSet,
		TemSupremo = false,
		NomeDoDragao = EsferasNegras.NomeDoDragao,
		TituloDoDragao = EsferasNegras.Titulo,
		Dragao = EsferasNegras.Dragao,
		Folha = EsferasNegras.Folha,
	};

	/// <summary>
	/// (2026-10-21) SO AS BANCADAS DO SET COMUM (`--esferasteste`, `--radarteste`): o set negro inteiro dorme. O radar comum tambem acende
	/// as Black Star do chao deste mundo, e uma sorteada na Terra virava o oitavo sinal das reguas de "as sete". Estatico de proposito: o
	/// reinicio simulado dessas bancadas (`CarregarEsferas` -> `CarregarNegras`) recria o set negro do disco.
	/// </summary>
	internal static bool NegrasDormemDeTeste;

	private bool SetNegroAtivo() => !NegrasDormemDeTeste && _setNegro != null && !_setNegro.Inerte && TempoDoMundo >= _setNegro.AtivoEm;

	private string CaminhoDasNegras =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "esferas-negras.txt");

	// =====================================================================
	// DISCO -- largura fixa. O retrato da --wipeteste compara o TAMANHO do arquivo:
	// dois sais aleatorios tem de ocupar os mesmos bytes.
	// =====================================================================
	private const int LarguraDoNome = 24;

	private void CarregarNegras()
	{
		_negras.Clear();
		_setNegro = CriarSetNegro();
		_zonaDoDragaoNegro = default;
		bool leu = false;
		try
		{
			if (System.IO.File.Exists(CaminhoDasNegras))
				leu = LerNegras(System.IO.File.ReadAllText(CaminhoDasNegras));
		}
		catch (Exception e) { GD.PushWarning($"[server] esferas-negras.txt ilegivel: {e.Message}"); }

		if (!leu || _negras.Count != Esferas.Total)
		{
			_negras.Clear();
			for (int n = 1; n <= Esferas.Total; n++) _negras.Add(new EsferaNegra { Numero = n });
			_setNegro.AtivoEm = 0;
			_setNegro.Pedidos = 0;
			EspalharAsNegras(anunciar: false);
		}
		else
		{
			// SO A PENDENTE SE ASSENTA NO BOOT. A que esta no chao com celula gravada (largada por alguem, ou
			// deixada onde o dragao foi invocado) fica onde estava: reassentar pelo sal a fazia pular sozinha
			// pra outra celula do planeta a cada reinicio do servidor.
			foreach (EsferaNegra e in _negras)
				if (e.PrecisaAssentar || ReassentarTudoNoBootDeTeste) TentarAssentarNegra(e);
			GD.Print($"[server] black star: ciclo {_setNegro.Ciclo}, sal {_setNegro.SalHex}, "
				   + $"{_negras.Count(e => !e.PrecisaAssentar)}/{Esferas.Total} assentadas");
		}
		RecolherNegrasDePlanetasMortos();
	}

	private void ZerarNegras()
	{
		_negras.Clear();
		_setNegro = CriarSetNegro();
		_zonaDoDragaoNegro = default;
		_invocacoes.Remove(EsferasNegras.IdDoSet);
		for (int n = 1; n <= Esferas.Total; n++) _negras.Add(new EsferaNegra { Numero = n });
		EspalharAsNegras(anunciar: false);
	}

	private void SalvarNegras()
	{
		if (_store == null || _limpezaEmCurso || _negras.Count != Esferas.Total) return;
		try
		{
			var sb = new StringBuilder();
			sb.Append("NEGRA1\n");
			sb.Append("SAL ").Append(_setNegro.SalHex).Append('\n');
			sb.Append("CIC ").Append(_setNegro.Ciclo.ToString("D6", CultureInfo.InvariantCulture)).Append('\n');
			long ativo = (long)Math.Clamp(Math.Floor(_setNegro.AtivoEm), 0, 999_999_999_999d);
			sb.Append("ATV ").Append(ativo.ToString("D12", CultureInfo.InvariantCulture)).Append('\n');
			sb.Append("PED ").Append(_setNegro.Pedidos.ToString("D1", CultureInfo.InvariantCulture)).Append('\n');
			foreach (EsferaNegra e in _negras.OrderBy(x => x.Numero))
			{
				string nome = e.ZonaNome.Length > LarguraDoNome ? e.ZonaNome[..LarguraDoNome] : e.ZonaNome;
				sb.Append("N ").Append(e.Numero.ToString(CultureInfo.InvariantCulture))
				  .Append(" K ").Append(e.ZonaTipo.ToString(CultureInfo.InvariantCulture))
				  .Append(' ').Append(nome.PadRight(LarguraDoNome))
				  .Append(" S ").Append(e.ZonaSeed.ToString("X16", CultureInfo.InvariantCulture))
				  .Append(" X ").Append(Math.Clamp(e.Cx, 0, 9999).ToString("D4", CultureInfo.InvariantCulture))
				  .Append(" Y ").Append(Math.Clamp(e.Cy, 0, 9999).ToString("D4", CultureInfo.InvariantCulture))
				  .Append(" A ").Append(e.PrecisaAssentar ? '1' : '0').Append('\n');
			}
			System.IO.File.WriteAllBytes(CaminhoDasNegras, Encoding.UTF8.GetBytes(sb.ToString()));
		}
		catch (Exception e) { GD.PushWarning($"[server] esferas-negras.txt nao gravou: {e.Message}"); }
	}

	private bool LerNegras(string texto)
	{
		string[] linhas = texto.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
		if (linhas.Length < 5 || linhas[0] != "NEGRA1") return false;
		if (!linhas[1].StartsWith("SAL ", StringComparison.Ordinal) || linhas[1].Length != 4 + 16) return false;
		_setNegro.SalHex = linhas[1][4..];
		if (!int.TryParse(linhas[2].AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ciclo)) return false;
		_setNegro.Ciclo = ciclo;
		if (!double.TryParse(linhas[3].AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out double ativo)) return false;
		_setNegro.AtivoEm = ativo;
		if (!int.TryParse(linhas[4].AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pedidos)) return false;
		_setNegro.Pedidos = pedidos;
		for (int i = 5; i < linhas.Length; i++)
		{
			EsferaNegra? e = LerLinhaNegra(linhas[i]);
			if (e != null) _negras.Add(e);
		}
		return _negras.Count == Esferas.Total && _negras.Select(e => e.Numero).Distinct().Count() == Esferas.Total;
	}

	private static EsferaNegra? LerLinhaNegra(string linha)
	{
		// "N 1 K 0 " + nome com 24 colunas + " S " + seed X16 + " X " + cx D4 + " Y " + cy D4 + " A " + bit
		// O nome ocupa colunas fixas pra o arquivo ter sempre o mesmo tamanho.
		if (linha.Length < 69 || linha[0] != 'N') return null;
		int numero = linha[2] - '0';
		if (numero < 1 || numero > Esferas.Total || linha[6] < '0' || linha[6] > '9') return null;
		if (!ulong.TryParse(linha.AsSpan(35, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong seed)) return null;
		if (!int.TryParse(linha.AsSpan(54, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cx)) return null;
		if (!int.TryParse(linha.AsSpan(61, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cy)) return null;
		return new EsferaNegra
		{
			Numero = numero,
			ZonaTipo = (byte)(linha[6] - '0'),
			ZonaNome = linha.Substring(8, LarguraDoNome).TrimEnd(),
			ZonaSeed = seed,
			Cx = cx,
			Cy = cy,
			PrecisaAssentar = linha[68] == '1',
		};
	}

	// =====================================================================
	// ESPALHAR
	// =====================================================================
	private ulong SortearSalNovo(ulong anterior)
	{
		if (SalFixoDeTeste) return EsferasNegras.SalCravado;
		for (int i = 0; i < 8; i++)
		{
			ulong sal = (ulong)Random.Shared.NextInt64();
			if (sal != 0 && sal != anterior && sal != EsferasNegras.SalCravado) return sal;
		}
		return anterior ^ 0x9E3779B97F4A7C15UL;
	}

	private void EspalharAsNegras(bool anunciar)
	{
		var antes = _negras.Select(e => e.Zona.Hash).ToHashSet();
		_setNegro.Ciclo++;
		_setNegro.GravarSal(SortearSalNovo(_setNegro.LerSal()));
		List<ZoneKey> planetas = SortearPlanetas(_setNegro.LerSal(), Esferas.Total);
		for (int i = 0; i < _negras.Count; i++)
		{
			EsferaNegra e = _negras.First(x => x.Numero == i + 1);
			e.Portador = 0;
			ZoneKey z = planetas[i % planetas.Count];
			e.PorZona(z);
			e.PrecisaAssentar = true;
			e.Cx = 0;
			e.Cy = 0;
			TentarAssentarNegra(e);
			antes.Add(z.Hash);
		}
		SalvarNegras();
		foreach (ulong h in antes) MandarEsferasPorHash(h);
		if (anunciar)
			AnunciarEsferas($"{EsferasNegras.NomeDoDragao} some no céu, e as Esferas de Estrela Negra se espalham pelos planetas.");
		GD.Print($"[esferas] black star espalhadas (ciclo {_setNegro.Ciclo}, sal {_setNegro.SalHex})");
	}

	private void EspalharUmaNegra(EsferaNegra e, string motivo)
	{
		ulong hashAntes = e.Zona.Hash;
		int quem = e.Portador;
		e.Portador = 0;
		List<ZoneKey> planetas = SortearPlanetas(SortearSalNovo(e.ZonaSeed), 1);
		ZoneKey z = planetas[0];
		if (z.Hash == hashAntes && planetas.Count == 1)
		{
			List<ZoneKey> mais = SortearPlanetas(SortearSalNovo(_setNegro.LerSal() ^ (ulong)e.Numero), Esferas.Total);
			z = mais.FirstOrDefault(p => p.Hash != hashAntes);
			if (z.Name.Length == 0) z = mais[0];
		}
		ZoneKey destino = z;
		if (ZonaMorta(z) || !Espaco.EhPlaneta(z))
		{
			// Sem planeta vivo pra ela: fica no espaco, e o tique tenta de novo.
			destino = Espaco.Zona(SeedDoUniverso);
			e.PorZona(destino);
			e.PrecisaAssentar = true;
			e.Cx = 0;
			e.Cy = 0;
		}
		else
		{
			e.PorZona(destino);
			e.PrecisaAssentar = true;
			TentarAssentarNegra(e);
		}
		SalvarNegras();
		MandarEsferasPorHash(hashAntes);
		MandarEsferas(destino);
		if (quem != 0 && _players.TryGetValue(quem, out ServerPlayer? pl))
			Avisar(pl, $"a {EsferasNegras.NomeDaEsfera(e.Numero)} se espalha de novo por um planeta -- {motivo}");
		GD.Print($"[esferas] black star #{e.Numero} reespalhada ({motivo}) -> {z.Name}");
	}

	private List<ZoneKey> SortearPlanetas(ulong sal, int quantos)
	{
		var pool = new List<ZoneKey>();
		void Por(ZoneKey z)
		{
			// (2026-10-23) NEM NO PLANETA CONDENADO, so no morto: o do desejo comeca a se partir ANTES de elas se espalharem
			// (`DepoisDoDesejoNegro`), e uma que caisse la passaria os 310 s num mundo de onde todo mundo foge, pra ser reespalhada no
			// commit (`RecolherNegrasDePlanetasMortos`). `ZonaCondenada` e o morto mais o que esta morrendo (o pavio lento ou os 310 s).
			if (!Espaco.EhPlaneta(z) || (NegraCaiNoPlanetaCondenadoDeTeste ? ZonaMorta(z) : ZonaCondenada(z))) return;
			if (pool.Any(p => p.Hash == z.Hash)) return;
			pool.Add(z);
		}
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos()) Por(Espaco.ZonaDe(p));
		PlanetaNoEspaco terra = Espaco.PreFeitos().First();
		foreach (PlanetaNoEspaco p in Espaco.PorPerto(SeedDoUniverso, terra.Chunk, EsferasNegras.RaioDeBuscaEmChunks))
			Por(Espaco.ZonaDe(p));
		if (pool.Count == 0) pool.Add(ZoneKey.Premade(Esferas.PlanetaEterno));

		var rng = new Random(unchecked((int)sal ^ (int)(sal >> 32)));
		for (int i = pool.Count - 1; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(pool[i], pool[j]) = (pool[j], pool[i]);
		}
		if (pool.Count >= quantos) return pool.Take(quantos).ToList();
		var r = new List<ZoneKey>();
		for (int i = 0; i < quantos; i++) r.Add(pool[i % pool.Count]);
		return r;
	}

	private void TentarAssentarNegra(EsferaNegra e)
	{
		if (EsferaForaDoHabitatDeTeste)
		{
			e.Cx = 0;
			e.Cy = 0;
			e.PrecisaAssentar = false;
			return;
		}
		ZoneCollision? mapa = MapaDaZona(e.Zona);
		Habitat? habitat = mapa != null ? HabitatDaZona(e.Zona) : null;
		if (habitat is not { Vagas: > 0 })
		{
			e.PrecisaAssentar = true;
			return;
		}
		ulong lugar = EsferasNegras.LugaresDoSal(_setNegro.LerSal(), habitat.Vagas)[e.Numero - 1];
		(int x, int y) = habitat.Vaga(lugar);
		e.Cx = x;
		e.Cy = y;
		e.PrecisaAssentar = false;
	}

	/// <summary>
	/// Planeta destruido com Black Star em cima: ela sorteia outro. A destruicao chegar na
	/// caverna e no Lookout e do agente das sub-zonas; aqui so a superficie onde a esfera esta.
	/// </summary>
	private void RecolherNegrasDePlanetasMortos()
	{
		foreach (EsferaNegra e in _negras.ToList())
		{
			if (e.Zona.Name.Length == 0 || !ZonaMorta(e.Zona)) continue;
			EspalharUmaNegra(e, $"{NomeDoPlaneta(e.ZonaNome)} não existe mais");
		}
	}

	private void TickDasNegras()
	{
		foreach (EsferaNegra e in _negras)
		{
			if (e.Portador == 0)
			{
				if (!Espaco.EhPlaneta(e.Zona))
				{
					EspalharUmaNegra(e, "estava fora de um planeta");
					continue;
				}
				if (e.PrecisaAssentar) TentarAssentarNegra(e);
				continue;
			}
			bool temCorpo = _players.TryGetValue(e.Portador, out ServerPlayer? quem);
			bool dePe = temCorpo && quem!.Peer != null && !quem.Ficha.dead && !quem.Ficha.KO;
			if (!dePe)
			{
				// QUEM SAIU DO JOGO NUM PLANETA tambem a deixa cair ali -- a celula e a do ultimo tique em que ele
				// estava de pe (o ramo de baixo a escreve). So fora da superficie de um planeta vivo ela se espalha.
				ZoneKey onde = temCorpo ? quem!.Zone : e.Zona;
				if (Espaco.EhPlaneta(onde) && !ZonaMorta(onde) && (temCorpo || !DeslogarEspalhaDeTeste))
				{
					e.Portador = 0;
					e.PorZona(onde);
					if (temCorpo)
					{
						e.Cx = (int)MathF.Floor(quem!.Pos.X / ZoneCollision.TileSize);
						e.Cy = (int)MathF.Floor(quem.Pos.Y / ZoneCollision.TileSize);
					}
					e.PrecisaAssentar = false;
					SalvarNegras();
					MandarEsferas(onde);
					if (temCorpo) Avisar(quem!, $"a {EsferasNegras.NomeDaEsfera(e.Numero)} cai da sua mão.");
				}
				else EspalharUmaNegra(e, temCorpo ? "você não está na superfície de um planeta" : "quem a carregava saiu");
				continue;
			}

			// De pe: ela viaja com o corpo, inclusive pra fora do planeta. O defeito injetado
			// desfaz isso e espalha -- e a regra das esferas comuns, que estas nao seguem.
			e.PorZona(quem!.Zone);
			e.Cx = (int)MathF.Floor(quem.Pos.X / ZoneCollision.TileSize);
			e.Cy = (int)MathF.Floor(quem.Pos.Y / ZoneCollision.TileSize);
			if (NegraPrendeNoPlanetaDeTeste && !Espaco.EhPlaneta(quem.Zone))
				EspalharUmaNegra(e, "defeito: ela prendeu no planeta");
		}
	}

	// =====================================================================
	// PEGAR, LARGAR, MOCHILA, RADAR, PACOTE
	// =====================================================================
	private bool TentarPegarNegra(ServerPlayer pl)
	{
		if (!SetNegroAtivo() && _setNegro.AtivoEm > TempoDoMundo)
		{
			// Pedra tambem se pega -- o dono carrega a esfera apagada como as outras.
		}
		EsferaNegra? e = _negras
			.Where(x => x.Portador == 0 && !x.PrecisaAssentar && x.Zona.Hash == pl.Zone.Hash
				&& Math.Abs(x.X - pl.Pos.X) <= Esferas.AlcanceDePegar
				&& Math.Abs(x.Y - pl.Pos.Y) <= Esferas.AlcanceDePegar)
			.OrderBy(x => (x.X - pl.Pos.X) * (x.X - pl.Pos.X) + (x.Y - pl.Pos.Y) * (x.Y - pl.Pos.Y))
			.FirstOrDefault();
		if (e == null) return false;
		e.Portador = pl.Id;
		SalvarNegras();
		MandarEsferas(pl.Zone);
		MandarMochila(pl);
		Avisar(pl, $"você pega a {EsferasNegras.NomeDaEsfera(e.Numero)} e a guarda na mochila. "
				 + "Esta sai do planeta com você."
				 + (SetNegroAtivo() ? "" : " Ela está apagada (pedra) até o ano do dragão passar."));
		return true;
	}

	private int LargarNegras(ServerPlayer pl)
	{
		List<EsferaNegra> minhas = [.. _negras.Where(e => e.Portador == pl.Id)];
		foreach (EsferaNegra e in minhas)
		{
			e.Portador = 0;
			e.PorZona(pl.Zone);
			e.Cx = (int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize);
			e.Cy = (int)MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize);
			e.PrecisaAssentar = !Espaco.EhPlaneta(pl.Zone);
			if (e.PrecisaAssentar) EspalharUmaNegra(e, "largada fora de um planeta");
		}
		if (minhas.Count > 0) { SalvarNegras(); MandarEsferas(pl.Zone); MandarMochila(pl); }
		return minhas.Count;
	}

	private void AcrescentarNegrasNaMochila(ServerPlayer pl, List<string> ids)
	{
		foreach (EsferaNegra e in _negras.Where(x => x.Portador == pl.Id).OrderBy(x => x.Numero))
			ids.Add(new EsferaNaMochila(EsferasNegras.IdDoSet, e.Numero, !SetNegroAtivo(), false) { Negra = true }.Id);
	}

	private bool LargarNegraDoSlot(ServerPlayer pl, EsferaNaMochila qual)
	{
		EsferaNegra? e = _negras.Find(x => x.Numero == qual.Numero && x.Portador == pl.Id);
		if (e == null || qual.Set != EsferasNegras.IdDoSet) return false;
		e.Portador = 0;
		e.PorZona(pl.Zone);
		e.Cx = (int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize);
		e.Cy = (int)MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize);
		e.PrecisaAssentar = !Espaco.EhPlaneta(pl.Zone);
		if (e.PrecisaAssentar) EspalharUmaNegra(e, "largada fora de um planeta");
		else { SalvarNegras(); MandarEsferas(pl.Zone); }
		MandarMochila(pl);
		Avisar(pl, $"você larga a {EsferasNegras.NomeDaEsfera(e.Numero)} no chão.");
		return true;
	}

	/// <summary>(2026-10-21) DEFEITO INJETADO (bancada `--radarteste`): o servidor nao marca o sinal da Black Star, e o disco a pintaria de
	/// amarelo como as comuns. Falso em jogo, sempre.</summary>
	internal static bool NegraSemMarcaNoRadarDeTeste;

	private void AcrescentarSinaisNegros(ServerPlayer pl, List<SinalDeEsfera> sinais)
	{
		if (!SetNegroAtivo()) return;
		// (2026-10-21) TODO sinal daqui leva a marca da Black Star: o disco a pinta de vermelho, e as comuns de amarelo (dono).
		bool negra = !NegraSemMarcaNoRadarDeTeste;
		if (Espaco.EhEspaco(pl.Zone))
		{
			var ja = new HashSet<ulong>();
			foreach (EsferaNegra e in _negras)
			{
				if (e.Portador != 0)
				{
					if (_players.TryGetValue(e.Portador, out ServerPlayer? quem) && quem.Zone.Hash == pl.Zone.Hash)
						sinais.Add(new SinalDeEsfera(quem.Pos.X, quem.Pos.Y, true, quem.Id == pl.Id, negra));
					continue;
				}
				if (!ja.Add(e.Zona.Hash)) continue;
				if (PosicaoNoEspaco(e.Zona) is { } pos)
					sinais.Add(new SinalDeEsfera(pos.X, pos.Y, false, false, negra));
			}
			return;
		}
		foreach (EsferaNegra e in _negras)
		{
			if (e.Portador != 0)
			{
				if (_players.TryGetValue(e.Portador, out ServerPlayer? quem) && quem.Zone.Hash == pl.Zone.Hash)
					sinais.Add(new SinalDeEsfera(quem.Pos.X, quem.Pos.Y, true, quem.Id == pl.Id, negra));
				continue;
			}
			if (e.PrecisaAssentar || e.Zona.Hash != pl.Zone.Hash) continue;
			sinais.Add(new SinalDeEsfera(e.X, e.Y, false, false, negra));
		}
	}

	private Vec2? PosicaoNoEspaco(ZoneKey z)
	{
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (Espaco.ZonaDe(p).Hash == z.Hash) return p.Pos;
		PlanetaNoEspaco terra = Espaco.PreFeitos().First();
		foreach (PlanetaNoEspaco p in Espaco.PorPerto(SeedDoUniverso, terra.Chunk, EsferasNegras.RaioDeBuscaEmChunks))
			if (Espaco.ZonaDe(p).Hash == z.Hash) return p.Pos;
		return null;
	}

	private void AvisarNegrasNoRadar(ServerPlayer pl)
	{
		if (pl.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.Radar) <= 0) return;
		if (!SetNegroAtivo()) return;
		if (Espaco.EhEspaco(pl.Zone))
		{
			var nomes = new List<string>();
			foreach (EsferaNegra e in _negras.Where(x => x.Portador == 0))
			{
				if (nomes.Contains(e.ZonaNome)) continue;
				Vec2? pos = PosicaoNoEspaco(e.Zona);
				string rumo = pos is { } p ? Rumo(pl.Pos, p) : "algum lugar";
				nomes.Add(e.ZonaNome);
				Avisar(pl, $"  estrela negra em {NomeDoPlaneta(e.ZonaNome)}, {rumo}.");
			}
			if (nomes.Count > 0)
				Avisar(pl, $"-- radar do espaço: {nomes.Count} planeta(s) com Esfera de Estrela Negra --");
			return;
		}
		int n = _negras.Count(e => e.Portador == 0 && e.Zona.Hash == pl.Zone.Hash && !e.PrecisaAssentar);
		int mao = _negras.Count(e => e.Portador != 0 && _players.TryGetValue(e.Portador, out ServerPlayer? q) && q.Zone.Hash == pl.Zone.Hash);
		if (n + mao > 0)
			Avisar(pl, $"  estrela negra neste mundo: {n} no chão, {mao} na mão de alguém.");
	}

	private int ContarNegrasNoPacote(ZoneKey zona) =>
		_negras.Count(e => e.Portador == 0 && e.Zona.Hash == zona.Hash && !e.PrecisaAssentar)
		+ (_invocacoes.ContainsKey(EsferasNegras.IdDoSet) && _zonaDoDragaoNegro.Hash == zona.Hash ? 1 : 0);

	private void EscreverUmaNegra(LiteNetLib.Utils.NetDataWriter w, EsferaNegra e)
	{
		w.Put(EsferasNegras.IdDoSet * 10 + e.Numero);
		w.Put((byte)Protocol.CoisaDeEsfera.Esfera);
		w.Put((byte)e.Numero);
		w.Put(e.X);
		w.Put(e.Y);
		w.Put(!SetNegroAtivo());
		w.Put(EsferasNegras.Folha);
	}

	private void EscreverODragaoNegro(LiteNetLib.Utils.NetDataWriter w)
	{
		if (!_invocacoes.TryGetValue(EsferasNegras.IdDoSet, out Invocacao? i)) return;
		w.Put(EsferasNegras.IdDoSet * 10 + 8);
		w.Put((byte)Protocol.CoisaDeEsfera.Dragao);
		w.Put((byte)0);
		w.Put(i.Onde.X);
		w.Put(i.Onde.Y);
		w.Put(false);
		w.Put(EsferasNegras.Dragao);
	}

	// =====================================================================
	// INVOCAR E O DEPOIS DO DESEJO
	// =====================================================================
	private int NegrasJuntas(ServerPlayer pl) =>
		_negras.Count(e => e.Portador == pl.Id
			|| (e.Portador == 0 && !e.PrecisaAssentar && e.Zona.Hash == pl.Zone.Hash
				&& Math.Abs(e.X - pl.Pos.X) <= Esferas.AlcanceDaInvocacao
				&& Math.Abs(e.Y - pl.Pos.Y) <= Esferas.AlcanceDaInvocacao));

	private bool InvocarANegraSeCouber(ServerPlayer pl)
	{
		if (!Espaco.EhPlaneta(pl.Zone)) return false;
		if (NegrasJuntas(pl) < Esferas.Total) return false;
		if (!SetNegroAtivo())
		{
			Avisar(pl, _setNegro.AtivoEm > TempoDoMundo
				? $"as Esferas de Estrela Negra ainda são pedra. Falta {(_setNegro.AtivoEm - TempoDoMundo) / 3600.0:0.#} h real(is)."
				: "as Esferas de Estrela Negra não atendem agora.");
			return true;
		}
		if (_invocacoes.ContainsKey(EsferasNegras.IdDoSet))
		{
			Avisar(pl, $"{EsferasNegras.NomeDoDragao} já está de pé.");
			return true;
		}
		foreach (EsferaNegra e in _negras)
		{
			e.Portador = 0;
			e.PorZona(pl.Zone);
			e.Cx = (int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize);
			e.Cy = (int)MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize);
			e.PrecisaAssentar = false;
		}
		_setNegro.PorZona(pl.Zone);
		_setNegro.Pedidos = 0;
		_zonaDoDragaoNegro = pl.Zone;
		var onde = new Vec2(pl.Pos.X, pl.Pos.Y - ZoneCollision.TileSize);
		_invocacoes[EsferasNegras.IdDoSet] = new Invocacao
		{
			Set = EsferasNegras.IdDoSet,
			Invocador = pl.Id,
			Onde = onde,
			ExpiraEm = TempoDoMundo + 120,
		};
		AnunciarEsferas($"{EsferasNegras.NomeDoDragao} diz: 'EU SOU {EsferasNegras.NomeDoDragao}, {EsferasNegras.Titulo}! "
					  + "DIGA O SEU DESEJO! UM SÓ, E ELE VALE MAIS.'");
		MandarEsferas(pl.Zone);
		MandarMochila(pl);
		AbrirOsDesejos(pl, _setNegro);
		return true;
	}

	/// <summary>
	/// O desejo unico acabou: pedra por um ano, O PLANETA DO DESEJO COMECA A SE PARTIR, e elas se espalham de novo. Nao passa pelo
	/// <c>EspalharOSet</c> (aquele prega as sete no planeta do set).
	/// </summary>
	private void DepoisDoDesejoNegro()
	{
		_setNegro.Pedidos = EsferasNegras.Desejos;
		_setNegro.AtivoEm = PrazoEmSegundos(Esferas.SegundosDe(EsferasNegras.OffTimeEmAnos));
		ZoneKey zona = _zonaDoDragaoNegro;
		FecharAInvocacao(EsferasNegras.IdDoSet,
			$"{EsferasNegras.NomeDoDragao} some no céu. As Esferas de Estrela Negra viram pedra e se espalham pelos planetas.");
		// (2026-10-23) O PRECO DAS ESTRELAS NEGRAS (dono: *"ao ser usada explodir o planeta q esta sendo usada alguns minutos apos o uso"*;
		// decidido: "5 min, pelos planetas de hoje"). O planeta onde o dragao subiu -- e nao onde quem pediu esta agora -- comeca a se
		// partir pela porta unica: os 310 s, o tremor, o ceu, o aviso pro mundo e o commit de sempre, a Terra tambem. Vale pra QUALQUER
		// desejo, o "curar um planeta" inclusive: ele cura um mundo que ja morreu, e este so comeca a morrer agora. ANTES do espalhar, de
		// proposito: o sorteio pula planeta condenado (`SortearPlanetas`). Sem autor (id 0): e o preco das esferas e nao um golpe de quem
		// pediu, entao ninguem vira o algoz de quem morrer no commit. O BP e o limiar do proprio planeta -- so vai pro log.
		if (Espaco.EhPlaneta(zona) && !NegraNaoExplodeOPlanetaDeTeste)
			ComecarDestruicao(zona, MortePlanetaria.BpExigido(MedidasDoPlaneta(zona).Gravidade), "o desejo das Black Star");
		EspalharAsNegras(anunciar: false);
		if (zona.Name.Length > 0) MandarEsferas(zona);
	}

	/// <summary>
	/// (2026-10-23) "Aprender a Danca da Fusao" -- o desejo que so as Black Star tem (dono: *"faca ter a opcao de aprender a danca da fusao
	/// como um pedido da black star dragon ball"*; a linha mora em `Desejos.DaBlackStar`). Ensina a skill da Danca a quem pede; as portas
	/// da Danca (os dois sabendo, a mesma raca, poder parecido -- `Fusao.Avaliar`) continuam as de sempre, na hora de fundir.
	/// </summary>
	private bool DesejoDaDancaDaFusao(ServerPlayer pl, SetDeEsferas s)
	{
		// QUEM JA SABE NAO GASTA O PEDIDO (o desejo unico das Black Star) -- a janela ja mostra a linha apagada pelo mesmo motivo.
		if (SabeDancar(pl) && !DancaRepetidaDeTeste)
		{
			Avisar(pl, "você já sabe a Dança da Fusão. Nenhum pedido foi gasto.");
			return false;
		}
		if (_skills?.Get(PathDaDanca) is not { } danca) return false;
		Jandirus.Core.Skills.SkillBook livro = pl.Livro ??= new Jandirus.Core.Skills.SkillBook();
		// O DRAGAO ENSINA COMO UM MESTRE QUE NAO COBRA (`EnsinoDeSkill.Ensinar`): a copia fica marcada como ENSINADA, e e isso que a
		// segura no livro -- a Danca e do kit do Geti Star (o espaco `rank/`), e o tique dos cargos (`ReconciliarDadiva`) tira em 1 s a
		// copia de cargo que ninguem ensinou. Como toda copia ensinada, ela nao se repassa a um terceiro.
		if (DancaSemSerEnsinadaDeTeste) livro.Dar(danca.Path);
		else Jandirus.Core.Skills.EnsinoDeSkill.Ensinar(livro, danca);
		MandarSkills(pl, forcar: true);
		Persistir(pl);
		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} os passos da Dança da Fusão!");
		Avisar(pl, "você aprende a Dança da Fusão. Pra fundir, a outra pessoa também precisa saber a dança, ser da mesma raça "
				 + "e ter um poder parecido com o seu.");
		return true;
	}

	private double FatorNegro(SetDeEsferas s) =>
		s.Id == EsferasNegras.IdDoSet && !BlackStarSemMultiplicadorDeTeste
			? EsferasNegras.MultiplicadorDaBlackStar
			: 1;
}

/// <summary>Uma Black Star. O portador nao vai pro disco, como o da esfera comum.</summary>
public sealed class EsferaNegra
{
	public int Numero;
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;
	public int Cx, Cy;
	public bool PrecisaAssentar = true;
	public int Portador;

	public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);

	public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }

	public float X => Cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f;
	public float Y => Cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f;
}

/// <summary>
/// Coordenada de esfera no <c>esferas.json</c> com largura fixa. O sal novo muda o lugar; o
/// retrato da limpeza compara o tamanho do arquivo, e um float curto contra um comprido o
/// deixaria vermelho sem ninguem ter errado a regra.
/// </summary>
internal sealed class CoordenadaFixa : System.Text.Json.Serialization.JsonConverter<float>
{
	public override float Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert,
		System.Text.Json.JsonSerializerOptions options)
	{
		if (reader.TokenType == System.Text.Json.JsonTokenType.String)
			return float.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
		return reader.GetSingle();
	}

	public override void Write(System.Text.Json.Utf8JsonWriter writer, float value,
		System.Text.Json.JsonSerializerOptions options)
	{
		int px = (int)MathF.Round(value);
		if (px < 0) px = 0;
		if (px > 999999) px = 999999;
		writer.WriteStringValue(px.ToString("D6", CultureInfo.InvariantCulture));
	}
}
