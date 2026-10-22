using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// ROBO `--diagsubzonas` (2026-10-21). As fotos que o dono pediu, no jogo de verdade: a hora e a lua da Terra no Lookout (a noite de lua
/// cheia, com o disco no canto do HUD, na Terra e no Templo), o Lookout debaixo do temporal da Terra e a caverna sem essa chuva (o teto ja
/// recorta), a recusa de subir pro espaco na caverna, o Lookout enquanto a Terra se desfaz, e a decolagem do Lookout pro espaco da Terra.
///
///     Godot --path . --janela --resolution 1280x720 --host --rede 10171 --luateste 5 --semfauna --diagsubzonas
///           --raca Human --conta bancada_dsubzonas --nome Monge
///
/// Headless nao tira foto; as medidas saem do mesmo jeito. Cada regra da TELA tem o defeito dela e a regua REPROVA.
/// </summary>
public partial class RoboDeSubZonas : Node
{
	private const int T = ZoneCollision.TileSize;

	/// <summary>Chao ao ar livre na Terra, ao sul do Banco. O primeiro que nao estiver sob teto e o da foto.</summary>
	private static readonly (int X, int Y)[] ChaoDaTerra = [(76, 266), (80, 280), (140, 180), (250, 80)];

	private const int LookoutX = 141, LookoutY = 320;
	private const int CasteloX = 354, CasteloY = 398;
	private const int CavernaX = 156, CavernaY = 197;

	private static readonly ZoneKey Terra = ZoneKey.Premade("Earth");
	private static readonly ZoneKey Vegeta = ZoneKey.Premade("Vegeta");
	private static readonly ZoneKey Lookout = ZoneKey.Premade("Lookout");
	private static readonly ZoneKey Caverna = ZoneKey.Premade("Earth_Cave");

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _chat = [];
	private IEnumerator<double>? _roteiro;
	private double _espera = 0.5;
	private bool _acabou, _tirouFoto = true;

	private static GameClient? C => GameClient.Instance;
	private static GameServer? S => GameServer.Instance;
	private static World? M => World.Instancia;

	public override void _Ready()
	{
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0 && S is { Running: false })
		{
			Checa("P0. o robo subiu hospedando de verdade", false, "a porta ja estava tomada");
			Encerrar();
			return;
		}
		if (C is { } cli) cli.Falou += (_, _, texto) => _chat.Add(texto);
		_roteiro = Roteiro().GetEnumerator();
	}

	public override void _ExitTree()
	{
		World.PerigoMudoDeTeste = false;
		World.TagCalaOPerigoDeTeste = false;
		World.CeuSoNaCargaDeTeste = false;
		GameServer.LookoutSemEspacoDeTeste = false;
		if (!_acabou) Encerrar();
	}

	public override void _Process(double delta)
	{
		if (_roteiro == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; Encerrar(); return; }
		_espera = _roteiro.Current;
	}

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  ok    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (!passou) _falhas.Add(oque);
	}

	private void Encerrar()
	{
		if (_acabou) return;
		_acabou = true;
		World.PerigoMudoDeTeste = false;
		World.TagCalaOPerigoDeTeste = false;
		World.CeuSoNaCargaDeTeste = false;
		GameServer.LookoutSemEspacoDeTeste = false;
		GD.Print("\n[dsubzonas] ===== BANCADA DAS SUB-ZONAS =====");
		foreach (string l in _linhas) GD.Print("[dsubzonas] " + l);
		if (!_tirouFoto) GD.Print("[dsubzonas]   (sem foto: headless nao renderiza)");
		GD.Print(_falhas.Count == 0
			? "[dsubzonas] ===== TUDO OK ====="
			: $"[dsubzonas] ===== {_falhas.Count} FALHA =====");
		if (IsInsideTree()) GetTree().Quit();
	}

	private void Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _tirouFoto = false; return; }
		img.SavePng(ProjectSettings.GlobalizePath($"user://subzonas-{nome}.png"));
		_linhas.Add($"  --     foto subzonas-{nome}.png");
	}

	private static void Tela(bool ligada)
	{
		if (Hud.Instancia is { } h) h.Visible = ligada;
		if (Chat.Instancia is { } c) c.Visible = ligada;
	}

	private static Vec2 Meio(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f);

	/// <summary>A mesma ficha de ceu: rotacao, dia e noite, e a lua.</summary>
	private static bool MesmoRelogio(RelogioDoPlaneta x, RelogioDoPlaneta y) =>
		x.Defasagem == y.Defasagem && x.SegundosPorDia == y.SegundosPorDia && x.TemDia == y.TemDia && x.TemNoite == y.TemNoite
		&& x.Lua.Existe == y.Lua.Existe && x.Lua.Defasagem == y.Lua.Defasagem;

	/// <summary>
	/// O CEU DA TELA E O DESTE PLANETA, NO MESMO INSTANTE? A ficha da luz e a do planeta, e a hora e a fase sao as dele no relogio da
	/// propria luz -- lido no mesmo quadro, porque o relogio anda (um segundo e 7e-4 do dia da Terra).
	/// </summary>
	private static bool CeuDaTelaE(World mundo, ZoneKey planeta)
	{
		if (mundo.Ceu is not { } aqui) return false;
		EstadoDoCeu dele = Ceu.De(Planetas.Relogio(planeta), mundo.TempoDoMundo);
		return mundo.CeuNaLuzDeTeste.Equals(planeta) && MesmoRelogio(mundo.RelogioDoLugar, Planetas.Relogio(planeta))
			&& Math.Abs(aqui.Hora - dele.Hora) < 1e-3 && aqui.Fase == dele.Fase && aqui.LuaNoCeu == dele.LuaNoCeu;
	}

	private IEnumerable<double> Ir(ZoneKey zona, int cx, int cy)
	{
		if (S == null || C is not { } cli) yield break;
		S.MoveToZone(cli.LocalId, zona, Meio(cx, cy));
		for (int i = 0; i < 40 && !cli.Zone.Equals(zona); i++) yield return 0.25;
		yield return 2.5;
	}

	/// <summary>Anda dentro da MESMA zona (o teleporte de producao) e espera o corpo local chegar.</summary>
	private IEnumerable<double> Cravar(int cx, int cy)
	{
		if (S == null || C is not { } cli || M is not { } mundo) yield break;
		Vec2 alvo = Meio(cx, cy);
		S.CravarNaFoto(cli.LocalId, alvo);
		for (int i = 0; i < 20 && !(mundo.PosicaoLocal is { } p && (new Vec2(p.X, p.Y) - alvo).Length < T); i++) yield return 0.1;
		yield return 0.5;
	}

	private IEnumerable<double> Roteiro()
	{
		for (int i = 0; i < 60 && (C is not { Connected: true, LocalId: > 0 } || M?.PosicaoLocal == null || S == null); i++)
			yield return 0.25;
		if (C is not { } cli || M is not { } mundo || S is not { } srv)
		{
			Checa("P0. entrou no mundo", false);
			yield break;
		}
		Checa("P0. entrou no mundo", true, cli.Zone.Name);
		mundo.AplicarZoom(3);

		// ============ 1. A HORA E A LUA DA TERRA NO LOOKOUT: a noite de lua cheia do `--luateste 5`, com o HUD na foto ============
		// O disco da lua mora no canto do HUD (`LuaNoCeu`), e so aparece com a lua no ceu e o corpo fora de teto. O ceu limpo forcado tira
		// o temporal sorteado, que esconderia a lua.
		Tela(true);
		srv.ForcarClima(Terra, TipoDeClima.Limpo, 600, 1, "robo subzonas");
		(int tx, int ty) = (76, 266);
		bool aberta = false;
		foreach ((int x, int y) in ChaoDaTerra)
		{
			foreach (double e in Ir(Terra, x, y)) yield return e;
			if (mundo.EuEstouSobTeto) continue;
			tx = x; ty = y; aberta = true;
			break;
		}
		EstadoDoCeu ceuDaTerra = mundo.Ceu ?? default;
		Checa("P0b. a foto da Terra e ao ar livre, de noite, com a lua cheia no ceu e no HUD",
			aberta && cli.Zone.Equals(Terra) && ceuDaTerra is { LuaNoCeu: true, Cheia: true } && Hud.Instancia?.Lua is { Visible: true },
			$"celula {tx},{ty} teto {mundo.EuEstouSobTeto}; hora {ceuDaTerra.Hora:0.000} fase {ceuDaTerra.Fase} lua no ceu {ceuDaTerra.LuaNoCeu}");
		Foto("1-terra-lua");

		foreach (double e in Ir(Lookout, LookoutX, LookoutY)) yield return e;
		EstadoDoCeu ceuDoTemplo = mundo.Ceu ?? default;
		bool temploIgual = cli.Zone.Equals(Lookout) && !mundo.EuEstouSobTeto && CeuDaTelaE(mundo, Terra)
			&& ceuDoTemplo is { LuaNoCeu: true, Cheia: true } && Hud.Instancia?.Lua is { Visible: true };
		Foto("2-lookout-lua");

		// O CASTELO DO REI VEGETA, NO MESMO MAPA: quem anda da plataforma ate o patio dele troca pro ceu de Vegeta no quadro seguinte.
		foreach (double e in Cravar(CasteloX, CasteloY)) yield return e;
		bool castelo = CeuDaTelaE(mundo, Vegeta);
		string noCastelo = $"{mundo.CeuNaLuzDeTeste.Name} {mundo.Ceu?.Hora:0.000}";
		foreach (double e in Cravar(LookoutX, LookoutY)) yield return e;
		Checa("P6. quem anda da plataforma do Templo ate o castelo do Rei Vegeta passa pro ceu de Vegeta, e de volta pro da Terra",
			castelo && CeuDaTelaE(mundo, Terra), $"no castelo {noCastelo}; de volta {mundo.CeuNaLuzDeTeste.Name}");
		World.CeuSoNaCargaDeTeste = true;
		try
		{
			foreach (double e in Cravar(CasteloX, CasteloY)) yield return e;
			Checa("CONTRA-EXEMPLO (defeito injetado: o ceu so na carga da zona) -- a regua P6 REPROVA: no castelo a tela fica sem o ceu de Vegeta",
				!CeuDaTelaE(mundo, Vegeta), mundo.CeuNaLuzDeTeste.Name);
		}
		finally { World.CeuSoNaCargaDeTeste = false; }
		foreach (double e in Cravar(LookoutX, LookoutY)) yield return e;

		// ============ 2. O TEMPORAL DA TERRA: no Lookout cai; na caverna o tempo e o mesmo, e a chuva fica la fora ============
		// O dia seguinte, ao meio-dia: as fotos de clima saem legiveis.
		Tela(false);
		srv.HoraDaTerraNaFoto(0.5);
		for (int i = 0; i < 20 && !(mundo.Ceu is { } c && Math.Abs(c.Hora - 0.5) < 0.02); i++) yield return 0.25;
		srv.ForcarClima(Terra, TipoDeClima.Tempestade, 600, 1, "robo subzonas");
		for (int i = 0; i < 20 && mundo.TempoQueFaz is not { Tipo: TipoDeClima.Tempestade }; i++) yield return 0.25;
		yield return 1.5;
		Checa("P1b. o temporal da Terra chega no Lookout", mundo.TempoQueFaz is { Tipo: TipoDeClima.Tempestade, Forcado: true },
			$"{mundo.TempoQueFaz?.Tipo}; hora {mundo.Ceu?.Hora:0.000}");
		Foto("3-lookout-chuva");

		foreach (double e in Ir(Caverna, CavernaX, CavernaY)) yield return e;
		bool cavernaDoPlaneta = cli.Zone.Equals(Caverna) && CeuDaTelaE(mundo, Terra);
		Checa("P1. o ceu desenhado no Lookout e na caverna e o da Terra, no mesmo instante: a mesma hora e a mesma lua", temploIgual && cavernaDoPlaneta,
			$"templo {ceuDoTemplo.Hora:0.000} fase {ceuDoTemplo.Fase} x terra {ceuDaTerra.Hora:0.000} fase {ceuDaTerra.Fase}; luz da caverna "
			+ mundo.CeuNaLuzDeTeste.Name);
		Checa("P1c. a caverna esta sob teto: o estado do temporal chega, a chuva fica do lado de fora",
			cli.Zone.Equals(Caverna) && mundo.EuEstouSobTeto && mundo.TempoQueFaz is { Tipo: TipoDeClima.Tempestade },
			$"teto {mundo.EuEstouSobTeto} clima {mundo.TempoQueFaz?.Tipo}");
		Foto("4-caverna-sem-chuva");

		// ============ 3. A RECUSA NA CAVERNA, com o chat na foto ============
		Tela(true);
		if (Hud.Instancia is { } hud) hud.Visible = false;
		int chatAntes = _chat.Count;
		srv.DecolarNaFoto(cli.LocalId);
		for (int i = 0; i < 12 && !_chat.Skip(chatAntes).Any(t => t.Contains("caverna", StringComparison.OrdinalIgnoreCase)); i++)
			yield return 0.25;
		yield return 0.5;
		bool ficou = cli.Zone.Equals(Caverna) && _chat.Skip(chatAntes).Any(t => t.Contains("caverna", StringComparison.OrdinalIgnoreCase));
		Checa("P4. da caverna nao se sobe: a frase diz que nao ha ceu aberto", ficou, string.Join(" | ", _chat.Skip(chatAntes)));
		Foto("5-caverna-recusa");
		Tela(false);

		// ============ 4. O FIM DO MUNDO VISTO DO LOOKOUT, e a trilha de combate ============
		srv.ForcarClima(Terra, TipoDeClima.Limpo, 0);
		foreach (double e in Ir(Lookout, LookoutX, LookoutY)) yield return e;
		bool comecou = srv.ComecarDestruicao(Terra, 1, "robo subzonas");
		srv.ForcarClima(Terra, TipoDeClima.Destruicao, 400, 1, "robo subzonas, ceu no auge");
		bool musica = false, ceuDeFim = false;
		for (int i = 0; i < 24; i++)
		{
			yield return 0.25;
			ceuDeFim = mundo.TempoQueFaz is { Tipo: TipoDeClima.Destruicao };
			musica = AudioDirector.Instance is { CamadaDeTeste: AudioDirector.Camada.Combate } a
				&& a.FaixaDeTeste.Contains("battle", StringComparison.OrdinalIgnoreCase);
			if (ceuDeFim && musica) break;
		}
		Checa("P2. o Lookout de uma Terra explodindo toca a trilha de combate",
			comecou && ceuDeFim && musica && mundo.PerigoDoChaoDeTeste == PerigoDoChao.Explodindo,
			$"ceu {mundo.TempoQueFaz?.Tipo} perigo {mundo.PerigoDoChaoDeTeste} faixa {AudioDirector.Instance?.FaixaDeTeste}");
		yield return 1.0;
		Foto("6-lookout-destruicao");

		mundo.TagCurtaDeTeste(0.3);
		yield return 0.7;
		bool segue = AudioDirector.Instance is { CamadaDeTeste: AudioDirector.Camada.Combate };
		Checa("P3. a tag que cai com o chao em perigo nao cala a trilha", segue,
			AudioDirector.Instance?.CamadaDeTeste.ToString() ?? "-");

		World.TagCalaOPerigoDeTeste = true;
		try
		{
			mundo.TagCurtaDeTeste(0.3);
			yield return 0.7;
			Checa("CONTRA-EXEMPLO (defeito injetado: a tag cala o perigo) -- a regua P3 REPROVA: a trilha cala com a tag",
				AudioDirector.Instance is not { CamadaDeTeste: AudioDirector.Camada.Combate },
				AudioDirector.Instance?.CamadaDeTeste.ToString() ?? "-");
		}
		finally { World.TagCalaOPerigoDeTeste = false; }

		World.PerigoMudoDeTeste = true;
		yield return 0.4;
		World.PerigoMudoDeTeste = false;
		yield return 0.6;
		Checa("P2b. acabado o defeito mudo, a trilha do perigo volta",
			AudioDirector.Instance is { CamadaDeTeste: AudioDirector.Camada.Combate });

		// ============ 5. A DECOLAGEM DO LOOKOUT PRO ESPACO DA TERRA ============
		chatAntes = _chat.Count;
		srv.DecolarNaFoto(cli.LocalId);
		for (int i = 0; i < 20 && !Espaco.EhEspaco(cli.Zone); i++) yield return 0.25;
		yield return 2.0;
		bool noEspaco = Espaco.EhEspaco(cli.Zone);
		bool musicaCaiu = AudioDirector.Instance is not { CamadaDeTeste: AudioDirector.Camada.Combate };
		Checa("P5. do Lookout se sobe pro espaco da Terra, e la a trilha de combate cai (orbita nao e o planeta)",
			noEspaco && musicaCaiu, $"zona {cli.Zone.Name} faixa {AudioDirector.Instance?.CamadaDeTeste}");
		Foto("7-decolagem");

		foreach (double e in Ir(Lookout, LookoutX, LookoutY)) yield return e;
		GameServer.LookoutSemEspacoDeTeste = true;
		try
		{
			chatAntes = _chat.Count;
			srv.DecolarNaFoto(cli.LocalId);
			yield return 0.6;
			Checa("CONTRA-EXEMPLO (defeito injetado: o Lookout fora do mapa) -- a regua P5 REPROVA: o corpo fica",
				cli.Zone.Equals(Lookout) && _chat.Skip(chatAntes).Any(t => t.Contains("mapa do universo")),
				string.Join(" | ", _chat.Skip(chatAntes)));
		}
		finally { GameServer.LookoutSemEspacoDeTeste = false; }

		srv.AbortarMorte(Terra, "robo subzonas");
		srv.ForcarClima(Terra, TipoDeClima.Limpo, 0);
		Tela(true);
	}
}
