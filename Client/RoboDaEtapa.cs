using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// AS FOTOS DA ETAPA NA MENTE (`--diagetapa`, 2026-10-21). A lista na aba Learning e o chefe nascido
/// na forma escolhida, dentro da mente: Cell Perfeito e Freeza a 100%.
///
///     Godot --path . --host --rede 10201 --horateste 0.5 --semfauna --diagetapa --raca Human --conta bancada_etapa --nome Etapa
/// </summary>
public partial class RoboDaEtapa : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
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
		catch (Exception e) { Conferir(false, "o roteiro correu ate o fim sem excecao (" + e.GetType().Name + ": " + e.Message + ")"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && MenuJogo.Instancia != null && S != null, 60);
		if (!pronto || C is not { } cli || MenuJogo.Instancia is not { } menu || S is not { } srv)
		{
			Conferir(false, "entrou no mundo com menu e servidor");
			return;
		}

		cli.SendActivity(Jandirus.Net.Protocol.Activity.Meditando);
		cli.SendHabilidade("mente");
		bool naMente = await Ate(() => DimensaoMental.EhAMente(cli.Zone) && DimensaoMental.Anfitriao(cli.Zone) == cli.LocalId, 20);
		Conferir(naMente, "R0. mergulhou na propria mente");
		if (!naMente) return;

		srv.PalcoMcf_Lembrancas(cli.LocalId);
		bool lista = await Ate(() => cli.ChefesVistos.Count >= 3, 8);
		Conferir(lista, "R1. os tres chefes chegaram no menu");
		if (!menu.Visible) menu.Abrir();
		menu.IrPara("Learning");
		await Quadros(8);

		Button? vegeta = Botao(menu, "Enfrentar na mente: Freeza (Planeta Vegeta)");
		Button? namek = Botao(menu, "Enfrentar na mente: Freeza (Namek)");
		Button? cell = Botao(menu, "Enfrentar na mente: Cell");
		Conferir(vegeta != null && namek != null && cell != null,
			"R1b. os dois Freeza e o Cell tem botao, e os Freeza nao se comem");
		if (namek != null) Mostrar(namek);
		await Quadros(4);
		Fotografar("user://etapa-1-botoes.png");

		cell?.EmitSignal(BaseButton.SignalName.Pressed);
		bool cartao = await Ate(() => BotaoDaEtapa(menu, "Perfeito") != null, 8);
		Conferir(cartao, "R2. a lista mostra Imperfeito, Semi-Perfeito e Perfeito, e nao o Super Perfeito");
		Conferir(BotaoDaEtapa(menu, "Imperfeito") != null && BotaoDaEtapa(menu, "Semi-Perfeito") != null
			&& BotaoDaEtapa(menu, "Super Perfeito") == null, "R2b. so as formas que este personagem viu");
		if (BotaoDaEtapa(menu, "Perfeito") is { } perfeito) Mostrar(perfeito);
		await Quadros(4);
		Fotografar("user://etapa-2-lista-cell.png");

		BotaoDaEtapa(menu, "Perfeito")?.EmitSignal(BaseButton.SignalName.Pressed);
		bool nasceu = await Ate(() => srv.PalcoMcf_Estado(cli.LocalId).StartsWith("cell|2|", StringComparison.Ordinal), 8);
		string estado = srv.PalcoMcf_Estado(cli.LocalId);
		Conferir(nasceu && estado.Contains("|bio4|", StringComparison.Ordinal),
			"R3. o Cell nasce Perfeito dentro da mente (" + estado + ")");
		menu.Fechar();
		await Segundos(0.8);
		Fotografar("user://etapa-3-cell-perfeito.png");

		// (2026-10-27, o pedido 124c) A LUTA NAO SE ENCERRA NO MEIO: com o Cell de pe, outra lembranca e recusada. Ele cai (o funil comum do
		// nocaute) -- e a vitoria o desfaz e devolve a lista.
		srv.DerrubarALembrancaDeTeste(cli.LocalId);
		Conferir(await Ate(() => srv.MenteDeTeste(cli.LocalId).Lembranca == 0, 5), "R3b. derrubado, o Cell se desfaz (a vitoria da lista)");

		if (!menu.Visible) menu.Abrir();
		menu.IrPara("Learning");
		await Quadros(6);
		Botao(menu, "Enfrentar na mente: Freeza (Namek)")?.EmitSignal(BaseButton.SignalName.Pressed);
		bool formas = await Ate(() => BotaoDaEtapa(menu, "100%") != null && BotaoDaEtapa(menu, "Forma Final") != null, 8);
		Conferir(formas, "R4. a lista do Freeza de Namek tem Forma Final e 100%");
		if (BotaoDaEtapa(menu, "100%") is { } cem) Mostrar(cem);
		await Quadros(4);
		Fotografar("user://etapa-4-lista-freeza.png");

		BotaoDaEtapa(menu, "100%")?.EmitSignal(BaseButton.SignalName.Pressed);
		bool final = await Ate(() => srv.PalcoMcf_Estado(cli.LocalId).StartsWith("freeza_namek|4|", StringComparison.Ordinal), 8);
		Conferir(final, "R5. o Freeza nasce a 100% (" + srv.PalcoMcf_Estado(cli.LocalId) + ")");
		menu.Fechar();
		await Segundos(0.8);
		Fotografar("user://etapa-5-freeza-100.png");

		// (2026-10-27, o pedido 124c) e o Freeza tambem cai antes do defeito injetado: com ele de pe a convocacao seria recusada antes da
		// pergunta da forma, e a regua do defeito passaria pelo motivo errado
		srv.DerrubarALembrancaDeTeste(cli.LocalId);
		Conferir(await Ate(() => srv.MenteDeTeste(cli.LocalId).Lembranca == 0, 5), "R5b. derrubado, o Freeza se desfaz (a vitoria da lista)");

		MenuJogo.EtapasSemCartaoDeTeste = true;
		try
		{
			if (!menu.Visible) menu.Abrir();
			menu.IrPara("Learning");
			await Quadros(4);
			menu.ForcarRedesenho();
			await Quadros(4);
			Botao(menu, "Enfrentar na mente: Cell")?.EmitSignal(BaseButton.SignalName.Pressed);
			// O pacote da lista chega no mesmo processo, quase na hora. Esperar o bastante pra ele ter
			// redesenhado, e so entao cobrar que o botao nao apareceu -- olhar cedo demais passa no vazio.
			await Segundos(2);
			Conferir(BotaoDaEtapa(menu, "Perfeito") == null,
				"R2c. (defeito injetado: a aba nao desenha a lista) a mesma regua REPROVA");
		}
		finally
		{
			MenuJogo.EtapasSemCartaoDeTeste = false;
			menu.Fechar();
		}
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (!f.IsQueuedForDeletion()) yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private static Button? Botao(MenuJogo menu, string texto) =>
		menu.PaginaDeTeste("Learning") is { } pg
			? Todos(pg).OfType<Button>().FirstOrDefault(b => b.Text == texto)
			: null;

	private static Button? BotaoDaEtapa(MenuJogo menu, string nome) =>
		menu.PaginaDeTeste("Learning") is { } pg
			? Todos(pg).OfType<Button>().FirstOrDefault(b => b.HasMeta("etapa_da_mente") && b.Text == nome)
			: null;

	private static void Mostrar(Control c)
	{
		for (Node? n = c; n != null; n = n.GetParent())
			if (n is ScrollContainer s) { s.EnsureControlVisible(c); break; }
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
		foreach (string l in _linhas) GD.Print("[detapa] " + l);
		GD.Print($"[detapa] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[detapa] ===== TUDO OK =====" : $"[detapa] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[detapa]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
