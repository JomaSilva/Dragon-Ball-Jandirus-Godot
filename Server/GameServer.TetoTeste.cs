using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO TETO NO SERVIDOR (`--tetoteste`).
///
/// O que ela mede e a REGRA de quem esta sob teto (`GameServer.Teto.cs`): dentro do Banco da Terra
/// -- o predio onde todo Human nasce, e uma area `Inside` no DM -- a lua cheia nao transforma, nao se
/// anuncia e o raio da tempestade nao e sorteado. O desenho (a neve que nao cai la dentro) e de
/// outra bancada, com janela: `--diagteto`.
///
/// CADA FAMILIA TEM O CONTRA-EXEMPLO NO MESMO CORPO: o corpo que nao vira dentro do Banco e levado
/// dois passos pra fora da porta e vira. Sem isso a familia ficaria verde num servidor em que a lua
/// cheia nunca transforma ninguem.
///
/// E OS DOIS DEFEITOS DO CORE SAO LIGADOS AQUI (`CelulaInterna.SemPlanoDeTeste` e
/// `CaidaContinuaInternaDeTeste`): com cada um, a pergunta de producao tem que dar a resposta errada.
///
/// Roda no primeiro login, como a `--luasometeste`: precisa dos moldes (o Saiyajin nasce pelo caminho
/// de producao), do ceu e de um jogador de verdade na zona (a familia do aviso e a do NPC). Adianta o
/// relogio do mundo, derruba uma celula do piso do Banco e devolve as duas coisas no `finally`.
///
///     Godot --path . --headless --host --rede 7931 --tetoteste --raca Human --conta bancada_teto --nome MedidorDoTeto
/// </summary>
public sealed partial class GameServer
{
	private bool _tetoDeTeste;

	/// <summary>
	/// O CEU DA TERRA NA HORA PEDIDA -- a manivela do `--horateste`, aberta pra bancada de janela
	/// (`--diagteto`) trocar de hora no meio da rodada sem subir outro processo.
	///
	/// O RELOGIO E REENVIADO NA HORA: o tique so o sincroniza de 15 em 15 s
	/// (<see cref="SegundosEntreSincronias"/>), e a bancada fotografaria o ceu velho ate la.
	/// </summary>
	internal void CeuDaTerraDeTeste(double hora, int fase = 0)
	{
		AjustarCeuDaTerra(hora, fase);
		foreach (ServerPlayer p in _players.Values) MandarCeu(p);
	}

	/// <summary>
	/// A RESPOSTA DO SERVIDOR pra "esta celula esta sob teto?". A bancada de janela a usa como juiz
	/// do pixel de proposito: o cliente desenha com a copia DELE do plano, e medir o desenho contra
	/// essa mesma copia ficaria verde com as duas erradas do mesmo jeito.
	/// </summary>
	internal bool CelulaSobTetoDeTeste(ZoneKey zona, int cx, int cy) => CelulaSobTeto(zona, cx, cy);

	/// <summary>
	/// DERRUBA UMA CELULA pelo funil de producao (`DerrubarCelula`: a guarda do indestrutivel, o
	/// conjunto do estrago, a colisao e o pacote pra zona). A bancada de janela abre um buraco no piso
	/// do Banco com isto, pra medir que a neve passa a cair nele.
	/// </summary>
	internal bool DerrubarCelulaDeTeste(ZoneKey zona, int cx, int cy) => DerrubarCelula(zona, cx, cy);

	/// <summary>Refaz o cenario da zona deste jogador -- o verb `admin_consertar_cenario`, sem pedir o cargo.</summary>
	internal void ConsertarCenarioDeTeste(int playerId)
	{
		if (_players.TryGetValue(playerId, out ServerPlayer? pl)) AdminConsertarCenario(pl);
	}

	/// <summary>
	/// UMA CELULA LIVRE E SOB TETO da zona, com as oito vizinhas livres e sob teto tambem -- o meio de
	/// um salao, e nao um corredor -- e a mais perto do centro do mapa. A bancada de janela a usa pra
	/// fotografar uma CAVERNA sem que ninguem tenha de saber de cor onde fica o chao dela. Nula em zona
	/// sem interior.
	/// </summary>
	internal (int Cx, int Cy)? CelulaLivreSobTetoDeTeste(ZoneKey zona)
	{
		if (MapaDaZonaOuCatalogo(zona) is not { TemDentro: true } mapa) return null;

		(int Cx, int Cy)? melhor = null;
		long menor = long.MaxValue;
		int meioX = mapa.Width / 2, meioY = mapa.Height / 2;
		mapa.ParaCadaCelulaQueNasceuDentro((x, y) =>
		{
			long d = (long)(x - meioX) * (x - meioX) + (long)(y - meioY) * (y - meioY);
			if (d >= menor) return;
			for (int dy = -1; dy <= 1; dy++)
				for (int dx = -1; dx <= 1; dx++)
					if (mapa.BlockedCell(x + dx, y + dy) || !CelulaSobTeto(zona, x + dx, y + dy)) return;
			menor = d;
			melhor = (x, y);
		});
		return melhor;
	}

	// O BANCO DA TERRA, em celula do port (o `.dmm` o pinta em BYOND x 68-79, y 238-243; ver
	// `Tools/AssetPipeline/DentroBench.cs`, que confere o retangulo inteiro).
	private const int TetoBercoCx = 73, TetoBercoCy = 260;   // o `/obj/SpawnPoint`: piso, sob teto
	private const int TetoForaCx = 73, TetoForaCy = 264;     // dois passos ao sul da porta: ar livre
	private const int TetoPisoCx = 70, TetoPisoCy = 259;     // um piso do Banco, pra derrubar

	/// <summary>O centro de um corpo cujos PES estao no meio da celula (ver <see cref="MoveRules.FeetOffsetY"/>).</summary>
	private static Vec2 CorpoNaCelulaDoTeto(int cx, int cy) =>
		new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
			cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY);

	private void RodarBancadaDoTeto(ServerPlayer pl)
	{
		GD.Print("\n[teto] ================ SOB TETO NAO HA CEU (a area `Inside` do DM) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[teto]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[teto]   FALHA {nome}   [{detalhe}]"); }
		}
		List<string> Ouvido()
		{
			List<string> ditos = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;
			return ditos;
		}
		static bool FalaDeLua(string t) => t.Contains("lua", StringComparison.OrdinalIgnoreCase);

		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		int luaVistaGuardada = pl.LuaVista;
		bool luaEstavaGuardada = pl.LuaEstavaNoCeu;
		var forjados = new List<ServerPlayer>();
		var palco = ZoneKey.Premade("Earth");
		bool derrubei = false;

		try
		{
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(palco);
			if (mapa == null || _moldes == null || _racas == null)
			{
				Checa("PRECONDICAO: a Earth tem mapa e os moldes/racas carregaram", false);
				return;
			}

			Vec2 dentro = CorpoNaCelulaDoTeto(TetoBercoCx, TetoBercoCy);
			Vec2 fora = CorpoNaCelulaDoTeto(TetoForaCx, TetoForaCy);

			// =====================================================================
			// 0) O PALCO
			// =====================================================================
			GD.Print("[teto] -- 0) o palco: o Banco da Terra, lua cheia no alto --");
			Checa("PRECONDICAO: a Earth carregou o plano do que nasce sob teto (`.dentro`)", mapa.TemDentro);
			Checa("PRECONDICAO: o berco do Banco e dois passos ao sul da porta sao chao livre",
				  !mapa.BlockedCell(TetoBercoCx, TetoBercoCy) && !mapa.BlockedCell(TetoForaCx, TetoForaCy));
			Checa("o berco do Banco esta SOB TETO", SobTeto(palco, dentro));
			Checa("contra-exemplo: dois passos ao sul da porta e AR LIVRE", !SobTeto(palco, fora));

			// MEIA-NOITE, e nao 0,90: a fala de quem encontra a lua depende de ela estar nascendo ou
			// ja no alto, e a familia 3 mede a segunda.
			AjustarCeuDaTerra(hora: 0.0, fase: Ceu.Cheia);
			EstadoDoCeu ceu = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
			Checa("PRECONDICAO: lua cheia no alto do ceu da Terra", ceu.Cheia && ceu.LuaNoCeu,
				  $"fase {ceu.Fase}, altura {ceu.Altura:0.00}");

			MoveToZone(pl.Id, palco, PontoDeNascimento(palco));
			TickDosCorposSemDono(Protocol.TickSeconds);   // e quem escreve o `_zonasComGente`
			Checa("PRECONDICAO: com o host na Earth, a zona conta como habitada",
				  _zonasComGente.Contains(palco.Hash));

			// =====================================================================
			// 1) O BOTAO VERMELHO: olhar pra lua de dentro de casa
			// =====================================================================
			GD.Print("[teto] -- 1) olhar pra lua (`OlharParaALua`, o botao vermelho) --");
			ServerPlayer? a = ForjarSaiyajin(palco, mapa, forjados);
			ServerPlayer? b = ForjarSaiyajin(palco, mapa, forjados);
			if (a == null || b == null) { Checa("PRECONDICAO: nasceram dois Saiyajins com rabo", false); return; }

			a.Pos = dentro;
			EscutaDeAvisos = [];
			OlharParaALua(a);
			List<string> recusa = Ouvido();
			Checa("DENTRO do Banco, olhar pra lua cheia NAO transforma (`Weather.dm:201`)",
				  a.Oozaru == FormaOozaru.Nao, a.Oozaru.ToString());
			Checa("...e a recusa FALA que e o teto (recusa muda e o mesmo que travar)",
				  recusa.Any(t => t.Contains("dentro", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", recusa));

			a.Pos = fora;
			OlharParaALua(a);
			Checa("contra-exemplo: o MESMO corpo, dois passos pra fora da porta, vira a fera",
				  a.Oozaru != FormaOozaru.Nao, a.Oozaru.ToString());

			b.Pos = dentro;
			CelulaInterna.SemPlanoDeTeste = true;
			try
			{
				OlharParaALua(b);
				Checa("(defeito injetado: o plano nao vale) de dentro do Banco ele VIRA -- a primeira linha reprovaria",
					  b.Oozaru != FormaOozaru.Nao, b.Oozaru.ToString());
			}
			finally { CelulaInterna.SemPlanoDeTeste = false; }

			// =====================================================================
			// 2) O NPC: o Saiyajin de povoamento, lutando e ferido
			// =====================================================================
			GD.Print("[teto] -- 2) o NPC Saiyajin ferido (`ALuaPegaOSaiyajin`) --");
			ServerPlayer? c = ForjarSaiyajin(palco, mapa, forjados);
			ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
			if (c == null || agressor == null) { Checa("PRECONDICAO: nasceram os Saiyajins da briga", false); return; }
			PorEmBrigaFeia(c, agressor);
			Checa("PRECONDICAO: ele esta de pe e com ferimento grave",
				  !c.Ficha.KO && !c.Ficha.dead && Jandirus.Core.Combat.Feridas.Grave(c.EnvFeridas));

			c.Pos = dentro;
			ALuaPegaOSaiyajin(c, TempoDoMundo);
			Checa("lutando e ferido DENTRO do Banco, o NPC nao olha pra lua", c.Oozaru == FormaOozaru.Nao, c.Oozaru.ToString());
			c.Pos = fora;
			ALuaPegaOSaiyajin(c, TempoDoMundo);
			Checa("contra-exemplo: o mesmo NPC, ao ar livre, vira a fera", c.Oozaru != FormaOozaru.Nao, c.Oozaru.ToString());

			// =====================================================================
			// 3) OS AVISOS: a lua nao se anuncia dentro, e se anuncia na saida
			// =====================================================================
			GD.Print("[teto] -- 3) os avisos da lua (`OlharProCeu`, o tique de 1 Hz) --");
			pl.Pos = dentro;
			pl.LuaEstavaNoCeu = false;
			pl.LuaVista = 0;
			EscutaDeAvisos = [];
			OlharProCeu(pl, ceu);
			List<string> ditosDentro = Ouvido();
			Checa("o jogador DENTRO do Banco nao recebe aviso de lua nenhum",
				  !ditosDentro.Any(FalaDeLua), string.Join(" | ", ditosDentro));
			Checa("...e a memoria da lua dele continua zerada (e ela que faz a saida anunciar)",
				  !pl.LuaEstavaNoCeu && pl.LuaVista == 0, $"estava {pl.LuaEstavaNoCeu}, vista {pl.LuaVista}");

			pl.Pos = fora;
			EscutaDeAvisos = [];
			OlharProCeu(pl, ceu);
			List<string> ditosSaindo = Ouvido();
			Checa("ao SAIR pela porta numa noite de cheia, a lua cheia se anuncia",
				  ditosSaindo.Any(t => t.Contains("lua cheia", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", ditosSaindo));
			Checa("...com a fala de quem a encontra JA NO ALTO, e nao nascendo (`Weather.dm:197-199`)",
				  ditosSaindo.Any(FalaDeLua) && !ditosSaindo.Any(t => t.Contains("horizonte", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", ditosSaindo));

			pl.Pos = dentro;
			EscutaDeAvisos = [];
			OlharProCeu(pl, ceu);
			List<string> ditosEntrando = Ouvido();
			Checa("ao ENTRAR de volta, nenhuma fala (a lua nao 'se pos': foi o teto)",
				  !ditosEntrando.Any(FalaDeLua), string.Join(" | ", ditosEntrando));

			// =====================================================================
			// 4) O RAIO: quem esta sob teto nao puxa descarga de tempestade
			// =====================================================================
			GD.Print("[teto] -- 4) o raio (`SortearQuemPuxaORaio`) --");
			ServerPlayer? r1 = ForjarSaiyajin(palco, mapa, forjados);
			ServerPlayer? r2 = ForjarSaiyajin(palco, mapa, forjados);
			if (r1 == null || r2 == null) { Checa("PRECONDICAO: nasceram os dois corpos do raio", false); return; }
			r1.Pos = dentro;
			r2.Pos = fora;
			var soDentro = new List<ServerPlayer> { r1 };
			var umDeCada = new List<ServerPlayer> { r1, r2 };

			Checa("zona so com gente sob teto: ninguem puxa raio de tempestade (`Weather.dm:243-254`)",
				  SortearQuemPuxaORaio(soDentro, TipoDeClima.Tempestade) == null);
			int deFora = 0;
			const int sorteios = 200;
			for (int i = 0; i < sorteios; i++)
				if (SortearQuemPuxaORaio(umDeCada, TipoDeClima.Tempestade) == r2) deFora++;
			Checa($"um dentro e um fora: o raio e sempre de quem esta fora ({sorteios} sorteios)",
				  deFora == sorteios, $"{deFora} de {sorteios}");
			Checa("a DESTRUICAO do planeta nao respeita teto (`Area_Death.dm:97-98`)",
				  SortearQuemPuxaORaio(soDentro, TipoDeClima.Destruicao) == r1);

			// =====================================================================
			// 5) A CELULA QUE CAI: o buraco no teto deixa o ceu entrar
			// =====================================================================
			GD.Print("[teto] -- 5) a celula interna destruida volta pro lado de fora (`NewTurfs.dm:13-17`) --");
			Vec2 noPiso = CorpoNaCelulaDoTeto(TetoPisoCx, TetoPisoCy);
			Checa("PRECONDICAO: o piso escolhido e chao livre e esta sob teto",
				  !mapa.BlockedCell(TetoPisoCx, TetoPisoCy) && SobTeto(palco, noPiso));
			derrubei = DerrubarCelula(palco, TetoPisoCx, TetoPisoCy);
			Checa("PRECONDICAO: o piso do Banco caiu pelo funil de producao (`DerrubarCelula`)", derrubei);
			Checa("a celula derrubada deixa de estar sob teto", !SobTeto(palco, noPiso));
			Checa("contra-exemplo: a celula vizinha, inteira, continua sob teto",
				  SobTeto(palco, CorpoNaCelulaDoTeto(TetoPisoCx + 1, TetoPisoCy)));

			ServerPlayer? g = ForjarSaiyajin(palco, mapa, forjados);
			if (g == null) { Checa("PRECONDICAO: nasceu o Saiyajin do buraco", false); return; }
			g.Pos = noPiso;
			OlharParaALua(g);
			Checa("...e de cima dela a lua cheia transforma, mesmo no meio do Banco",
				  g.Oozaru != FormaOozaru.Nao, g.Oozaru.ToString());

			CelulaInterna.CaidaContinuaInternaDeTeste = true;
			try
			{
				Checa("(defeito injetado: a caida continua interna) o buraco segue 'sob teto' -- a linha acima reprovaria",
					  SobTeto(palco, noPiso));
			}
			finally { CelulaInterna.CaidaContinuaInternaDeTeste = false; }

			Checa("a bancada chegou ao fim", true);
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			CelulaInterna.SemPlanoDeTeste = false;
			CelulaInterna.CaidaContinuaInternaDeTeste = false;
			EscutaDeAvisos = null;

			foreach (ServerPlayer n in forjados)
				if (_players.ContainsKey(n.Id)) RemoverNpc(n);

			// O PISO VOLTA pelo verb de producao (o `Restaurar_Planeta` do admin), que e quem sabe
			// refechar a colisao e mandar o cliente recarregar o cenario. O host ainda esta na Earth.
			if (derrubei && pl.Zone.Hash == palco.Hash) AdminConsertarCenario(pl);

			_adiantoDoCeu = ceuGuardado;
			if (pl.Zone.Hash != zonaGuardada.Hash) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			pl.Pos = posGuardada;
			pl.LuaVista = luaVistaGuardada;
			pl.LuaEstavaNoCeu = luaEstavaGuardada;
		}
		GD.Print($"[teto] ================ {ok} OK, {falhou} FALHA(S) ================");
	}
}
