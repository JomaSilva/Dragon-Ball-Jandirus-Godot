using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA MAESTRIA NA SIMULACAO E DA TRAVA DA CAMARA (`--gravtravateste`).
///
/// Dois pedidos do dono (2026-10-21): o Simulator copia a maestria de gravidade de quem pediu, e a
/// Gravity Chamber ganha a trava de seguranca (1 milhao de zeni ou 2 milhoes de recursos) que desliga
/// o campo se alguem desmaia dentro dele.
///
///     Godot --headless -- --server --port 10110 --gravtravateste
/// </summary>
public sealed partial class GameServer
{
	private int _gtOk, _gtFalhou;

	private void AfirmarGt(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _gtOk++; GD.Print($"[gravtrava]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _gtFalhou++; GD.PrintErr($"[gravtrava]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaGravTrava()
	{
		_gtOk = _gtFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[gravtrava] ================ A MAESTRIA NA SIMULACAO E A TRAVA DA CAMARA (dono, 2026-10-21) ================");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
		int obrasAntes = _noChao.Count;
		CofreDeTech cofreDeAntes = _cofreDeTech;
		_cofreDeTech = new CofreDeTech();
		var sims = new List<int>();

		try
		{
			AfirmarGt("(montagem) a Terra tem mapa e o catalogo de obras carregou", mapa != null && _obras != null);
			if (mapa == null || _obras == null) return;

			(int px, int py) = PracaDaBancada(mapa);
			const int T = ZoneCollision.TileSize;
			Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
			(int mx, int my) = (px + 2, py + 2);
			Vec2 noCampo = No(mx, my);

			ServerPlayer dono = Forjar("gt: o dono", No(mx + 1, my + 1), 1_000);
			dono.Conta = "bancada_gravtrava";
			double chao = Math.Max(dono.Ficha.Planetgrav, 1);
			double maestria = chao * 8;
			double campo = chao * 3;
			dono.Ficha.GravMastered = maestria;
			dono.Ficha.Statify();

			Obra? maq = AssentarDeTeste(dono, "Gravity", mx, my);
			// O estado de gravidade nao nasce no posicionar. A --maquinasteste ja faz isto: o primeiro
			// grav_info cria o GravidadeDaObra. Sem ele, a montagem acha a obra e le nulo.
			if (maq != null) ComandoDeInteracao(dono, "grav_info", "");
			AfirmarGt("(montagem) a maquina de gravidade assentou", maq?.Gravidade != null);
			if (maq?.Gravidade is not { } g) return;

			GtDaCopia(dono, maq, g, noCampo, maestria, campo, sims);
			GtDaTrava(dono, maq, g, noCampo, campo, sims);
			GtDoPrecoEDoRecolher(dono, g, px, py, No);
		}
		finally
		{
			SimulacaoSemAMaestriaDeTeste = TravaQueNaoDesligaDeTeste = false;
			foreach (int id in sims) DesfazerSimulacao(id, null);
			if (_noChao.Count > obrasAntes) _noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
			_cofreDeTech = cofreDeAntes;
			GravarOCofreDeTech();
			GravarMundo();
			LimparTudoDaBancada();
			LerOsCampos();
			GD.Print($"[gravtrava] ================ {_gtOk} OK, {_gtFalhou} FALHA(S) ================");
		}
	}

	private void GtLigar(GravidadeDaObra g, double grav)
	{
		g.Energia = Math.Max(g.Energia, 1);
		g.EnergiaMax = Math.Max(g.EnergiaMax, 1);
		g.Grav = grav;
		g.MudaEm = 0;
		g.Pedida = 0;
		g.Cheias = null;
	}

	/// <summary>
	/// O TIQUE DE FICHAS (o de 5 Hz do servidor) nos corpos da regua, o mesmo pros dois. O `LerOsCampos` escreve o `gravmult`
	/// e roda so o `Statify`; o `gravFelt` so e refeito no `PowerLevel` desse tique. Sem ele cada corpo mostraria o `gravFelt`
	/// da ultima conta dele (o dono forjado com a maestria de fabrica, a simulacao nascida antes do campo).
	/// </summary>
	private void GtTicar(params ServerPlayer[] corpos)
	{
		foreach (ServerPlayer pl in corpos) pl.Ficha.Tick(agoraMs: NowMs());
	}

	/// <summary>O `gravFelt` deste corpo e o da gravidade que ele sente agora, e nao o de uma conta antiga.</summary>
	private static bool GtSenteOCampo(ServerPlayer pl) =>
		Math.Abs(pl.Ficha.gravFelt - StatCurves.GravFelt(pl.Ficha.GravMastered, Esmagamento.Gravidade(pl.Ficha))) < 1e-9;

	private static bool GtAguentaIgual(ServerPlayer dono, ServerPlayer clone) =>
		Math.Abs(clone.Ficha.GravMastered - dono.Ficha.GravMastered) < 1e-6
		&& !Esmagamento.Esmaga(clone.Ficha) && !Esmagamento.Prende(clone.Ficha)
		&& Math.Abs(clone.Ficha.gravFelt - dono.Ficha.gravFelt) < 1e-6
		&& Math.Abs(clone.Ficha.gravmult - dono.Ficha.gravmult) < 1e-6
		&& clone.Ficha.gravmult > 0;

	private static string GtConta(ServerPlayer pl) =>
		$"maestria {pl.Ficha.GravMastered:0.##} grav {Esmagamento.Gravidade(pl.Ficha):0.##} " +
		$"razao {Esmagamento.Razao(pl.Ficha):0.##} felt {pl.Ficha.gravFelt:0.###} mult {pl.Ficha.gravmult:0.##}";

	private ServerPlayer GtSimular(ServerPlayer dono, Obra maq, List<int> sims)
	{
		ServerPlayer sim = CriarSimulacao(dono, maq, Simulador.TetoDeFabrica);
		_simulacoes[sim.Id] = dono.Id;
		sims.Add(sim.Id);
		return sim;
	}

	private void GtDaCopia(ServerPlayer dono, Obra maq, GravidadeDaObra g, Vec2 noCampo, double maestria, double campo, List<int> sims)
	{
		GD.Print("[gravtrava] -- 1) a simulacao nasce com a maestria de gravidade de quem pediu --");
		ServerPlayer sim = GtSimular(dono, maq, sims);
		AfirmarGt("**A SIMULACAO NASCE COM A MAESTRIA DE GRAVIDADE DE QUEM PEDIU** (o berco so sobe, nao desce o copiado)",
				  Math.Abs(sim.Ficha.GravMastered - maestria) < 1e-6 && sim.Ficha.GravMastered + 1e-6 >= dono.Ficha.Planetgrav,
				  $"dono {dono.Ficha.GravMastered:0.##} clone {sim.Ficha.GravMastered:0.##} chao {dono.Ficha.Planetgrav:0.##}");

		dono.Pos = noCampo;
		sim.Pos = noCampo;
		dono.Ficha.gravmult = sim.Ficha.gravmult = 0;
		GtLigar(g, campo);
		LerOsCampos();
		GtTicar(dono, sim);
		bool donoAguenta = !Esmagamento.Esmaga(dono.Ficha) && !Esmagamento.Prende(dono.Ficha);
		AfirmarGt("(montagem) o dono, com a maestria dele, aguenta este campo, e o gravFelt dele ja e o do campo",
				  donoAguenta && dono.Ficha.gravmult > 0 && GtSenteOCampo(dono), GtConta(dono));
		AfirmarGt("**NO CAMPO QUE O DONO AGUENTA, A SIMULACAO AGUENTA IGUAL**: nao esmagada, nao presa, gravFelt igual",
				  donoAguenta && GtAguentaIgual(dono, sim), $"{GtConta(dono)} || {GtConta(sim)}");

		DesfazerSimulacao(sim.Id, null);
		SimulacaoSemAMaestriaDeTeste = true;
		try
		{
			ServerPlayer sem = GtSimular(dono, maq, sims);
			sem.Pos = noCampo;
			dono.Ficha.gravmult = sem.Ficha.gravmult = 0;
			GtLigar(g, campo);
			LerOsCampos();
			GtTicar(dono, sem);
			AfirmarGt("(defeito injetado: a simulacao nasce sem a maestria de gravidade) a mesma regua REPROVA",
					  !GtAguentaIgual(dono, sem), $"{GtConta(dono)} || {GtConta(sem)}");
			DesfazerSimulacao(sem.Id, null);
		}
		finally { SimulacaoSemAMaestriaDeTeste = false; }
	}

	private void GtDaTrava(ServerPlayer dono, Obra maq, GravidadeDaObra g, Vec2 noCampo, double campo, List<int> sims)
	{
		GD.Print("[gravtrava] -- 2) a trava desliga a camara quando alguem desmaia no campo --");
		dono.Pos = noCampo;
		ComandoDeInteracao(dono, "aparafusar", "");
		g.Range = Math.Max(g.Range, 3);
		GtLigar(g, campo);
		LerOsCampos();

		ServerPlayer parceiro = Forjar("gt: o parceiro", noCampo, 1_000);
		parceiro.Pos = noCampo;
		parceiro.Combate.Nocautear(30);
		AfirmarGt("**SEM A TRAVA, O DESMAIO NAO DESLIGA A MAQUINA**",
				  !g.Trava && Math.Abs(g.Grav - campo) < 1e-6 && parceiro.Ficha.KO, $"grav {g.Grav:0.##}");

		dono.Ficha.Zeni = 1_000_000;
		dono.Ficha.Recursos = 0;
		List<string> comprou = Ouvir(() => ComandoDeInteracao(dono, "grav_up", "trava"));
		AfirmarGt("**A TRAVA CUSTA 1 MILHAO DE ZENI** e liga na maquina",
				  g.Trava && dono.Ficha.Zeni == 0 && Disse(comprou, "trava de segurança"),
				  $"zeni {dono.Ficha.Zeni:0} | {Ultimos(comprou)}");
		AfirmarGt("**COMPRADA, A TRAVA SAI DO MENU** (bit proprio, a estabilizacao continua)",
				  (MelhoriasFeitas(maq) & Interacoes.TravaDeSegurancaFeita) != 0
				  && !MelhoriasNoMenu(maq).Contains("upgrades/trava")
				  && MelhoriasNoMenu(maq).Contains("upgrades/estabilidade"),
				  string.Join(",", MelhoriasNoMenu(maq)));
		List<string> info = Ouvir(() => ComandoDeInteracao(dono, "grav_info", ""));
		AfirmarGt("**O INFO E OS PRECOS MOSTRAM A TRAVA**",
				  Disse(info, "trava de segurança: sim") && Disse(info, "trava de segurança: já comprada"),
				  Ultimos(info));

		ServerPlayer aluno = Forjar("gt: o aluno", noCampo, 1_000);
		aluno.Pos = noCampo;
		GtLigar(g, campo);
		LerOsCampos();
		List<string> aoCair = Ouvir(() => aluno.Combate.Nocautear(30));
		AfirmarGt("**COM A TRAVA, O DESMAIO DESLIGA A GRAVIDADE E CANCELA A MUDANCA**",
				  aluno.Ficha.KO && g.Grav == 0 && g.MudaEm == 0 && g.Pedida == 0, $"grav {g.Grav:0.##} muda {g.MudaEm}");
		AfirmarGt("**O AVISO DIZ QUEM DESMAIOU**",
				  Disse(aoCair, "A trava de segurança desligou a gravidade") && Disse(aoCair, aluno.Name),
				  Ultimos(aoCair));

		ServerPlayer acordado = Forjar("gt: o acordado", noCampo, 1_000);
		acordado.Pos = noCampo;
		g.Energia = 1;
		List<string> pedido = Ouvir(() => ComandoDeInteracao(dono, "grav_definir", "8"));
		bool esperando = g.MudaEm != 0 && g.Grav == 0;
		List<string> noMeio = Ouvir(() => acordado.Combate.Nocautear(30));
		AfirmarGt("**DESMAIAR NO MEIO DA MUDANCA CANCELA A GRAVIDADE PEDIDA**",
				  esperando && acordado.Ficha.KO && g.Grav == 0 && g.MudaEm == 0 && Disse(noMeio, "desligou a gravidade"),
				  $"esperando {esperando} | {Ultimos(pedido)} || {Ultimos(noMeio)}");

		ServerPlayer controle = Forjar("gt: o controle", noCampo, 1_000);
		controle.Pos = noCampo;
		GtLigar(g, campo);
		LerOsCampos();
		TravaQueNaoDesligaDeTeste = true;
		try
		{
			controle.Combate.Nocautear(30);
			AfirmarGt("(defeito injetado: a trava nao desliga) a mesma regua REPROVA",
					  controle.Ficha.KO && Math.Abs(g.Grav - campo) < 1e-6 && g.MudaEm == 0, $"grav {g.Grav:0.##}");
		}
		finally { TravaQueNaoDesligaDeTeste = false; }

		ServerPlayer sim = GtSimular(dono, maq, sims);
		sim.Pos = noCampo;
		GtLigar(g, campo);
		LerOsCampos();
		double gravAntes = g.Grav;
		sim.Combate.Nocautear(30);
		bool ficou = Math.Abs(g.Grav - gravAntes) < 1e-6 && g.Grav > 0 && sim.Ficha.KO;
		bool desfez = !GuiarASimulacao(sim, dono.Id, out _, out _) && !_players.ContainsKey(sim.Id);
		AfirmarGt("**A SIMULACAO NAO DESMAIA (ELA SE DESFAZ) E NAO DISPARA A TRAVA**",
				  ficou && desfez, $"grav {g.Grav:0.##} desfez {desfez}");
	}

	private void GtDoPrecoEDoRecolher(ServerPlayer dono, GravidadeDaObra primeira, int px, int py, Func<int, int, Vec2> no)
	{
		GD.Print("[gravtrava] -- 3) o preco em recursos, e a trava que vai junto ao recolher --");
		// DENTRO DA PRACA (10x6 de chao comum, colunas 0..9): fora dela o chao nao e garantido. Longe do campo da primeira, e o
		// mesmo primeiro toque dela -- o estado so nasce no `grav_info`.
		dono.Pos = no(px + 8, py + 3);
		Obra? outra = AssentarDeTeste(dono, "Gravity", px + 8, py + 2);
		if (outra != null) ComandoDeInteracao(dono, "grav_info", "");
		AfirmarGt("(montagem) a segunda maquina assentou", outra?.Gravidade != null,
				  outra == null ? "o posicionar recusou" : "assentou sem estado");
		if (outra?.Gravidade is not { } g2) return;

		dono.Ficha.Zeni = 0;
		dono.Ficha.Recursos = 2_000_000;
		List<string> precos = Ouvir(() => ComandoDeInteracao(dono, "grav_info", ""));
		List<string> pagou = Ouvir(() => ComandoDeInteracao(dono, "grav_up", "trava recursos"));
		AfirmarGt("**A TRAVA CUSTA 2 MILHOES DE RECURSOS** (o dobro do zeni, sozinho) e o preco aparece antes",
				  Disse(precos, $"trava de segurança: {Recurso.DoisPrecos(CustoDaTravaDeSeguranca)}")
				  && g2.Trava && dono.Ficha.Recursos == 0 && dono.Ficha.Zeni == 0 && Disse(pagou, "trava de segurança"),
				  $"recursos {dono.Ficha.Recursos:0} | {Ultimos(pagou)}");
		dono.Ficha.Zeni = 500;
		List<string> deNovo = Ouvir(() => ComandoDeInteracao(dono, "grav_up", "trava"));
		AfirmarGt("comprada uma vez, a segunda cobranca nao sai",
				  dono.Ficha.Zeni == 500 && Disse(deNovo, "já tem a trava"), Ultimos(deNovo));

		List<string> aoPegar = Ouvir(() => ComandoDeInteracao(dono, "pegar", ""));
		string? item = dono.Mochila.Pilhas.Select(p => p.Id)
			.FirstOrDefault(id => string.Equals(MaquinaGuardada.Tipo(id), "Gravity", StringComparison.OrdinalIgnoreCase)
								&& MaquinaGuardada.Gaveta(id) > 0);
		int gaveta = item == null ? 0 : MaquinaGuardada.Gaveta(item);
		bool naGaveta = gaveta > 0 && _cofreDeTech.Maquinas.TryGetValue(gaveta, out MaquinaNaGaveta? guardada)
						&& guardada.Gravidade?.Trava == true;
		AfirmarGt("**RECOLHIDA, A TRAVA VAI JUNTO** na gaveta da maquina",
				  naGaveta && Disse(aoPegar, "vão junto"), $"item {item} | {Ultimos(aoPegar)}");

		Obra? deVolta = null;
		if (item != null)
		{
			const int Tile = ZoneCollision.TileSize;
			dono.Pos = no(px + 8, py + 5);
			int antes = _noChao.Count;
			ComandoDeTech(dono, "posicionar", $"{item}/{((px + 8) * Tile + Tile / 2)}/{((py + 4) * Tile + Tile / 2 - (int)MoveRules.FeetOffsetY)}");
			if (_noChao.Count > antes) deVolta = _noChao[^1];
		}
		AfirmarGt("assentada de novo, a trava continua comprada",
				  deVolta?.Gravidade?.Trava == true && (primeira.Trava), $"trava {deVolta?.Gravidade?.Trava}");
	}
}
