using Godot;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagsalas`: AS SALAS DO TEMPO DE PLANETA NA TELA -- a porta na montanha, a pista da aba World, as regras da
/// porta com a dilatacao, o interior, a expulsao e a recarga. Pedido do dono de 2026-10-11.
///
/// MODO HOST (o servidor e deste processo): quem arruma o palco e o `GameServer.SalasDePlanetaPalco.cs` -- acha um
/// mundo com sala, leva o corpo ate a soleira da porta e gasta a sessao. Tudo o que o robo CONFERE passa pelo fio
/// de verdade: a porta chega como construcao no pacote de obras, as regras e os avisos chegam pelo chat, a zona
/// muda pelo `ZoneChanged`, e a pista da aba World sai da mesma funcao pura que o servidor usa.
///
/// A irma de servidor (`--salasteste`) mede a REGRA (o sorteio, o ganho, a idade, a recarga, as bordas). Esta
/// mede o que o JOGADOR VE -- e, com janela, FOTOGRAFA o que numero nenhum mede: a porta encravada na rocha, o
/// quarto branco la dentro, o aviso da dilatacao.
///
///     Godot --janela -- --host --rede 8352 --horateste 0.5 --diagsalas --raca Human --conta bancada_dsalas --nome Viajante
/// </summary>
public partial class RoboDasSalas : Node
{
	private int _ok, _falha, _injOk, _injFalha, _fotos;
	private readonly List<string> _reprovadas = [];
	private readonly List<string> _falas = [];
	private bool _escutando;

	private static void Nota(string s) => GD.Print("[dsalas] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static bool SemJanela => DisplayServer.GetName() == "headless";

	public override void _Ready() => _ = Rodar();

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void AoFalar(Jandirus.Net.Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);

	private void Encerrar(int codigo)
	{
		SalaDePlaneta.NenhumaSalaDeTeste = false;
		if (_escutando && C is { } cli) cli.Falou -= AoFalar;
		World.Instancia?.PararDeTeste();
		CamadasDeTela(true);

		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" AS SALAS DO TEMPO DE PLANETA NA TELA -- a porta na montanha, a pista, as regras, o interior, a expulsao");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 } && c.SeedDoUniverso != 0
									  && M != null && S != null && World.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, seed do universo, menu de pe) e o servidor e deste processo (--host)", pronto);
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv) { Encerrar(2); return; }

		cli.Falou += AoFalar;
		_escutando = true;
		int eu = cli.LocalId;
		Checa("(montagem) a cobaia existe no servidor e aguenta o chao de qualquer mundo", srv.PalcoDasSalas_Cobaia(eu));

		// ---- 1) em casa: mundo sem sala, aba sem pista ----
		ZoneKey casa = cli.Zone;
		menu.Abrir();
		await Abrir(menu, "World", forcar: true);
		Checa("no mundo em que se nasce NAO ha pista: a aba World nao tem a linha 'Sala do Tempo'",
			  !MenuJogo.MundoComSalaDoTempo(cli) && menu.ValorDesenhado("World", "Sala do Tempo") == null, $"zona {cli.Zone.Name}");

		// ---- 2) a porta na montanha ----
		if (srv.PalcoDasSalas_Mundo() is not { } alvo)
		{
			Checa("(montagem) ha um mundo com Sala do Tempo nas celulas em volta da Terra", false);
			Encerrar(1);
			return;
		}
		ZoneKey mundo = Espaco.ZonaDe(alvo.Corpo);
		string passa = SalaDePlaneta.TextoDaDilatacao(alvo.Dilatacao);
		Nota($"  o mundo do palco: {alvo.Corpo.Nome} [{alvo.Sx}:{alvo.Sy}] -- {MundoProcedural.DaSeed(alvo.Corpo.Seed, alvo.Corpo.Nome).Descricao()}; a sala dilata {passa} por dia");

		menu.Fechar();
		bool chegou = await Ate(() => srv.PalcoDasSalas_IrParaAPorta(eu, alvo.Corpo), 60)
					  && await Ate(() => cli.Zone.Equals(mundo) && World.Instancia?.ZonaMontadaDeTeste == mundo.Hash, 30);
		await Segundos(3.0);   // o terreno pinta por pedaco e a porta chega no pacote de obras
		Checa("o corpo esta no mundo com sala, na soleira da porta (o cliente gerou o mesmo chao da seed)", chegou, $"zona {cli.Zone.Name}");
		if (!chegou) { Encerrar(1); return; }

		Vector2 eu2 = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		GameClient.ObraInfo? porta = cli.Obras.Where(o => o.Tipo == "Time_Chamber_Door")
			.OrderBy(o => o.Pos.DistanceTo(eu2)).Cast<GameClient.ObraInfo?>().FirstOrDefault();
		(int X, int Y)? noServidor = srv.PalcoDasSalas_Porta(mundo);
		const int t = ZoneCollision.TileSize;
		Checa("**A PORTA CHEGOU PELO FIO**: uma construcao 'Porta da Sala do Tempo' na celula que o servidor tem, a um tile do corpo",
			  porta is { } p && noServidor is { } s && (int)MathF.Floor(p.Pos.X / t) == s.X
			  && Math.Abs(p.Pos.X - eu2.X) <= 2 * t && Math.Abs(p.Pos.Y - eu2.Y) <= 2 * t && cli.Obras.Count(o => o.Tipo == "Time_Chamber_Door") == 1,
			  porta is { } q ? $"'{q.Nome}' em ({q.Pos.X:0},{q.Pos.Y:0}); o corpo em ({eu2.X:0},{eu2.Y:0}); no servidor a celula ({noServidor?.X},{noServidor?.Y})" : "nenhuma porta nas obras do cliente");
		await FotoDoMundo(menu, "a-porta-na-montanha", 3);
		await FotoDoMundo(menu, "a-porta-na-montanha-de-longe", 2);
		await Foto("a-porta-com-o-hud");

		// ---- 3) a pista da aba World ----
		menu.Abrir();
		await Abrir(menu, "World", forcar: true);
		bool linha = await Ate(() => menu.ValorDesenhado("World", "Sala do Tempo") == "há uma porta na rocha", 4);
		Checa("**A PISTA**: neste mundo a aba World ganha a linha 'Sala do Tempo: há uma porta na rocha' (a mesma funcao pura do servidor)",
			  linha && MenuJogo.MundoComSalaDoTempo(cli), menu.ValorDesenhado("World", "Sala do Tempo") ?? "(sem linha)");
		await Foto("aba-world-a-pista");

		SalaDePlaneta.NenhumaSalaDeTeste = true;
		try
		{
			await Abrir(menu, "World", forcar: true);
			Injeta("nenhum mundo tem sala: a pista some da aba World", menu.ValorDesenhado("World", "Sala do Tempo") == null);
		}
		finally { SalaDePlaneta.NenhumaSalaDeTeste = false; }
		await Abrir(menu, "World", forcar: true);
		menu.Fechar();

		// ---- 4) as regras da porta: a dilatacao ----
		_falas.Clear();
		cli.SendVerbo("sala_regras");
		bool regras = await Ate(() => Disse($"um dia aqui fora são {passa} lá dentro"), 6);
		Checa($"**AS REGRAS DA PORTA DIZEM A DILATACAO**: \"um dia aqui fora são {passa} lá dentro\", a expulsao e a recarga -- e nenhum numero de BP",
			  regras && Disse("DEVOLVE") && Disse("a da Terra") && !_falas.Exists(f => f.Contains("BP")), Ultimas());
		await Segundos(0.6);
		await Foto("as-regras-da-porta-a-dilatacao");

		// ---- 5) entrar: o interior ----
		_falas.Clear();
		cli.SendVerbo("sala_entrar");
		bool dentro = await Ate(() => SalaDoTempo.EhDePlaneta(cli.Zone) && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 20);
		await Segundos(3.0);
		Checa("**ENTRAR**: o cliente esta no interior da sala deste mundo (o mapa da Sala, outra zona que nao a da Terra)",
			  dentro && cli.Zone.Seed == mundo.Seed && !SalaDoTempo.EhASala(cli.Zone), $"zona {cli.Zone}");
		Checa($"...e a entrada avisou a dilatacao (\"Aqui dentro passam {passa}\"), a sessao e a recarga de todas as salas",
			  Disse($"Aqui dentro passam {passa}") && Disse("devolve você à porta") && Disse("a da Terra"), Ultimas());
		await Foto("o-aviso-da-entrada");
		await FotoDoMundo(menu, "o-interior", 2);

		menu.Abrir();
		await Abrir(menu, "Stats", forcar: true);
		string sessao = menu.ValorDesenhado("Stats", "Sessão na Sala") ?? "";
		Checa("a aba Stats mostra a sessao correndo (\"min de treino restantes\")", sessao.Contains("min de treino restantes"), sessao);
		await Foto("aba-stats-a-sessao");
		menu.Fechar();

		// ---- 6) o fim: a expulsao ----
		_falas.Clear();
		Checa("(palco) a sessao da cobaia e gasta ate um segundo do fim", srv.PalcoDasSalas_AcabarASessao(eu));
		bool fora = await Ate(() => cli.Zone.Equals(mundo) && World.Instancia?.ZonaMontadaDeTeste == mundo.Hash, 20);
		await Segundos(2.5);
		Checa("**A EXPULSAO**: acabada a sessao, o cliente esta de volta ao mundo, na frente da porta -- e o chat diz por que",
			  fora && Disse("devolve à porta"), $"zona {cli.Zone.Name}; {Ultimas()}");
		await Foto("expulso-de-volta-a-porta");

		// ---- 7) a recarga ----
		_falas.Clear();
		cli.SendVerbo("sala_entrar");
		bool recusa = await Ate(() => Disse("TODA Sala do Tempo"), 6);
		Checa("**A RECARGA**: a porta recusa quem acabou de sair, e a frase diz que ela vale pra toda Sala do Tempo",
			  recusa && cli.Zone.Equals(mundo), Ultimas());
		await Segundos(0.6);
		await Foto("a-recarga");

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// OS AUXILIARES (o molde do RoboDoGodKi)
	// =====================================================================
	private bool Disse(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(4).Select(f => f.Length > 110 ? f[..110] + "..." : f));

	/// <summary>A tela inteira em `user://salas-NN-nome.png`. SO COM JANELA: as fotos sao pro olho de quem confere.</summary>
	private async Tarefa Foto(string nome)
	{
		if (SemJanela) return;
		await Quadros(2);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath($"user://salas-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
	}

	/// <summary>FOTO DO MUNDO: o menu, o HUD e o chat saem da frente e a camera vai a este zoom; depois tudo volta.</summary>
	private async Tarefa FotoDoMundo(MenuJogo menu, string nome, int zoom)
	{
		if (SemJanela) return;
		int antes = (int)MathF.Round(World.Instancia?.ZoomDaCameraDeTeste ?? 0f);
		menu.Fechar();
		CamadasDeTela(false);
		World.Instancia?.AplicarZoom(zoom);
		await Segundos(0.4);
		try { await Foto(nome); }
		finally
		{
			if (antes > 0) World.Instancia?.AplicarZoom(antes);
			CamadasDeTela(true);
		}
	}

	private static void CamadasDeTela(bool aVista)
	{
		if (Hud.Instancia is { } hud) hud.Visible = aVista;
		if (Chat.Instancia is { } chat) chat.Visible = aVista;
	}

	private async Tarefa Abrir(MenuJogo menu, string aba, bool forcar)
	{
		menu.IrPara(aba);
		await Quadros(3);
		if (forcar) { menu.ForcarRedesenho(); await Quadros(3); }
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
