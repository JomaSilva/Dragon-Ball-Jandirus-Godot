using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA PROVA DO ESPIRITO DO CARGO (`--diagprovacargo`, 2026-10-19) -- as fotos do pedido do dono (*"faça a prova do espirito"*).
/// A regra e da `--provacargoteste`; aqui e a TELA, pelo botao de verdade:
///   R1. o cartao do Eremita Tartaruga (vago, apto) tem o "Reivindicar" com a frase da prova embaixo -- foto `prova-0-cartao`;
///   R1b. apertar o botao ergue o Espirito do Cargo ao lado, anunciado -- foto `prova-1-espirito` (de aureola, meio transparente);
///   R2. o espirito vem pra cima: em ate 10 s ele acerta quem o invocou -- foto `prova-2-luta`;
///   R3. derrubado o espirito (pelo palco, nas maos do robo), o trono e dele: "VOCE E O NOVO ..." e o cartao com o nome dele --
///       foto `prova-3-vitoria`;
///   R4. a prova do Grou, e o robo cai: o espirito se desfaz em risos e o cargo fica vago -- foto `prova-4-derrota`;
///   R4b. o "Reivindicar" seguinte ouve a zombaria (a vergonha de 30 min).
///
///     Godot --path . --host --rede 9622 --horateste 0.5 --semfauna --diagprovacargo --raca Human --conta bancada_provacargo --nome Provado
/// </summary>
public partial class RoboDaProvaDoCargo : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private void Ouvir(Protocol.Fala _, string __, string texto) => _ditas.Add(texto);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu P nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu || World.Instancia is not { } mundo) return;
		int eu = cli.LocalId;
		GD.Print("[dprovacargo] ===== A PROVA DO ESPIRITO DO CARGO (2026-10-19) =====");
		cli.Falou += Ouvir;
		try
		{
			// ---- R1. o cartao, e o botao de verdade ----
			srv.PalcoPcg_AptoPara(eu, karma: 30);
			await Aba(menu, "Cargos");
			cli.SendCargo();
			bool cartao = await Ate(() => BotaoDoCargo(menu, "Turtle Hermit") != null, 6);
			await Segundos(0.5);
			PanelContainer? doEremita = CartaoDoCargo(menu, "Turtle Hermit");
			Conferir(cartao && doEremita != null && TemTexto(doEremita, "a prova invoca o Espírito do Cargo"),
					 "R1. o cartao do Eremita (vago, apto) tem o \"Reivindicar\" com a frase da prova embaixo");
			Fotografar("user://prova-0-cartao.png");

			_ditas.Clear();
			BotaoDoCargo(menu, "Turtle Hermit")?.EmitSignal(BaseButton.SignalName.Pressed);
			bool nasceu = await Ate(() => mundo.PosicaoDoUnicoComONome(ProvaDoCargo.NomeDoEspirito) != null, 6);
			menu.Fechar();
			await Segundos(1.5);   // a aureola e a passagem chegam pelo tique de 5 Hz
			Conferir(nasceu && _ditas.Any(t => t.Contains("se materializa diante de", StringComparison.Ordinal)),
					 $"R1b. **o Reivindicar ergue o Espirito do Cargo** ao lado, anunciado [ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
			Fotografar("user://prova-1-espirito.png");

			// ---- R2. a luta ----
			double vida0 = srv.PalcoPcg_Estado(eu).Vida;
			bool acertou = await Ate(() => srv.PalcoPcg_Estado(eu) is { Prova: true } s && s.Vida < vida0 - 0.5, 10);
			Conferir(acertou, $"R2. **o espirito vem pra cima**: em ate 10 s ele acerta quem o invocou [vida {vida0:0} -> {srv.PalcoPcg_Estado(eu).Vida:0}]");
			Fotografar("user://prova-2-luta.png");

			// ---- R3. a vitoria ----
			_ditas.Clear();
			bool derrubou = srv.PalcoPcg_DerrubarOEspirito(eu);
			bool coroado = await Ate(() => _ditas.Any(t => t.Contains("VOCE E O NOVO", StringComparison.Ordinal)), 5);
			await Aba(menu, "Cargos");
			cli.SendCargo();
			bool dono = await Ate(() => CartaoDoCargo(menu, "Turtle Hermit") is { } c && TemTexto(c, cli.LocalName) && BotaoDoCargo(menu, "Turtle Hermit") == null, 5);
			await Segundos(0.5);
			Conferir(derrubou && coroado && dono,
					 $"R3. derrubado o espirito, o trono e dele: \"VOCE E O NOVO ...\" e o cartao com o nome dele [ouvi: {string.Join(" | ", _ditas.TakeLast(3))}]");
			Fotografar("user://prova-3-vitoria.png");
			menu.Fechar();

			// ---- R4. a derrota ----
			srv.PalcoPcg_AptoPara(eu, karma: -5);
			await Aba(menu, "Cargos");
			cli.SendCargo();
			await Ate(() => BotaoDoCargo(menu, "Crane Hermit") != null, 6);
			_ditas.Clear();
			BotaoDoCargo(menu, "Crane Hermit")?.EmitSignal(BaseButton.SignalName.Pressed);
			bool nasceu2 = await Ate(() => mundo.PosicaoDoUnicoComONome(ProvaDoCargo.NomeDoEspirito) != null, 6);
			menu.Fechar();
			await Segundos(1.0);
			srv.PalcoPcg_Nocautear(eu);
			bool perdeu = await Ate(() => _ditas.Any(t => t.Contains("se desfaz em risos", StringComparison.Ordinal)), 5);
			await Segundos(1.0);
			Conferir(nasceu2 && perdeu && !srv.PalcoPcg_Estado(eu).Prova,
					 $"R4. **quem cai perde**: nocauteado na prova do Grou, o espirito se desfaz em risos e o cargo fica vago [ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
			Fotografar("user://prova-4-derrota.png");

			// ---- R4b. a vergonha ----
			srv.PalcoPcg_Levantar(eu);
			await Aba(menu, "Cargos");
			cli.SendCargo();
			await Ate(() => BotaoDoCargo(menu, "Crane Hermit") != null, 6);
			_ditas.Clear();
			BotaoDoCargo(menu, "Crane Hermit")?.EmitSignal(BaseButton.SignalName.Pressed);
			bool zombou = await Ate(() => _ditas.Any(t => t.Contains("zomba", StringComparison.Ordinal)), 5);
			Conferir(zombou && !srv.PalcoPcg_Estado(eu).Prova, "R4b. o \"Reivindicar\" seguinte ouve a zombaria (30 min de vergonha) e nenhum espirito nasce");
			menu.Fechar();
		}
		finally
		{
			cli.Falou -= Ouvir;
			srv.PalcoPcg_Limpar(eu);
		}
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private static PanelContainer? CartaoDoCargo(MenuJogo menu, string nome) =>
		menu.PaginaDeTeste("Cargos") is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(x => !x.IsQueuedForDeletion() && x.HasMeta("cartao")
				  && x.GetMeta("cartao").AsString() == "cargo" && x.GetMeta("titulo").AsString() == nome)
			: null;

	private static Button? BotaoDoCargo(MenuJogo menu, string nome) =>
		CartaoDoCargo(menu, nome) is { } c
			? Todos(c).OfType<Button>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.IsVisibleInTree()
				  && string.Equals(b.Text, "Reivindicar", StringComparison.OrdinalIgnoreCase))
			: null;

	private static bool TemTexto(Node raiz, string trecho) =>
		Todos(raiz).OfType<Label>().Any(l => !l.IsQueuedForDeletion() && l.Text.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dprovacargo] " + l);
		GD.Print($"[dprovacargo] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dprovacargo] ===== TUDO OK =====" : $"[dprovacargo] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dprovacargo]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
