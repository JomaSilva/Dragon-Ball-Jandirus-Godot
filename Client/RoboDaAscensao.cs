using Godot;
using Jandirus.Core.Races;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagascensao`: A ASCENSAO NA ABA FORMS (dono, 2026-10-23) -- a linha "Ascensão" do cartao Agora, num corpo de uma raca sem
/// transformacao (o Tsujin), pelo BP base: sem linha abaixo de 1 milhao, x2 na porta do SSJ1, o meio da rampa, x16 na do SSJ3, e de
/// volta sem linha. A regra e o numero sao da bancada de servidor (`--ascensaoteste`); esta mede o que o JOGADOR VE.
///
/// MODO HOST (o servidor e deste processo): o palco (`GameServer.CravarBpDaAscensaoDeTeste`) so CRAVA o BP base. O resto e o jogo -- o
/// tique de fichas poe o `BPBoost`, a ficha lenta (`AtributosState.Ascensao`) o leva pelo fio, e a aba se redesenha pela assinatura dela
/// (sem `ForcarRedesenho` entre um BP e outro). Com janela, FOTOGRAFA cada passo (`user://ascensao-NN-*.png`).
///
///     Godot --janela -- --resolution 1280x720 --host --rede 10931 --horateste 0.5 --diagascensao --raca Tsujin --conta bancada_dascensao --nome Ascendido
/// </summary>
public partial class RoboDaAscensao : Node
{
	private int _ok, _falha, _injOk, _injFalha, _fotos;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dascensao] " + s);

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

	public override void _ExitTree() => MenuJogo.AscensaoForaDaAbaDeTeste = false;

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

	private void Encerrar(int codigo)
	{
		MenuJogo.AscensaoForaDaAbaDeTeste = false;
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
		Nota(" A ASCENSAO NA ABA FORMS -- a linha \"Ascensão\" do cartao Agora, pelo BP base de uma raca sem transformacao");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 }
									  && M != null && S != null && World.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta e menu) e o servidor e deste processo (--host)", pronto);
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv) { Encerrar(2); return; }

		int eu = cli.LocalId;
		var verdade = srv.VerdadeDaAscensaoDeTeste(eu);
		Checa("(montagem) o corpo e de uma raca que ASCENDE pela regra do catalogo (o robo sobe com `--raca Tsujin`)",
			  verdade is { } v0 && Ascensao.Ascende(v0.Raca, v0.Classe), $"{verdade}");

		// ---- 1) abaixo de 1 milhao: sem linha ----
		srv.CravarBpDaAscensaoDeTeste(eu, 900_000);
		await Ate(() => BoostNoServidor(srv, eu) == 1 && cli.Atributos.Ascensao <= 1.005f, 8);
		menu.Abrir();
		await Abrir(menu, "Forms");
		Checa("COM 900 MIL DE BP BASE (abaixo da rampa) a aba Forms NAO tem a linha \"Ascensão\" -- e o Tsujin le x1 no fio",
			  menu.ValorDesenhado("Forms", "Ascensão") == null && cli.Atributos.Ascensao <= 1.005f,
			  $"'{menu.ValorDesenhado("Forms", "Ascensão")}', no fio x{cli.Atributos.Ascensao:0.###}");
		await Foto("abaixo-de-1-milhao");

		// ---- 2..4) a rampa, com a aba ABERTA: ela se redesenha sozinha pela assinatura ----
		await Passo(menu, cli, srv, eu, Jandirus.Core.Forms.Catalogo.PortaSsj1, "na-porta-do-ssj1",
			"NA PORTA DO SSJ1 (1,5 milhao) a linha aparece no cartao Agora com x2 -- o texto do fio, sem redesenho forcado", "×2");
		await Passo(menu, cli, srv, eu, 100_000_000, "100-milhoes",
			"COM 100 MILHOES a linha anda junto (o meio da rampa, x3,4)", null);
		await Passo(menu, cli, srv, eu, Jandirus.Core.Forms.Catalogo.PortaSsj3, "na-porta-do-ssj3",
			"NA PORTA DO SSJ3 (2 bilhoes) a linha le x16, o teto", "×16");

		// ---- 5) o contra-exemplo: a aba sem a linha ----
		MenuJogo.AscensaoForaDaAbaDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			Injeta("a aba sem a linha (o jogo sem a tela da Ascensao): com 16x no fio a tela nao diz nada -- a regua da linha REPROVA",
				menu.ValorDesenhado("Forms", "Ascensão") == null && cli.Atributos.Ascensao > 15,
				$"no fio x{cli.Atributos.Ascensao:0.##}");
		}
		finally { MenuJogo.AscensaoForaDaAbaDeTeste = false; }
		menu.ForcarRedesenho();
		await Quadros(3);
		Checa("...e desfeito o defeito, a linha volta", menu.ValorDesenhado("Forms", "Ascensão") == MenuJogo.TextoDaAscensao(cli.Atributos.Ascensao),
			  $"'{menu.ValorDesenhado("Forms", "Ascensão")}'");

		// ---- 6) de volta ao comeco: a linha some ----
		srv.CravarBpDaAscensaoDeTeste(eu, 1_000);
		bool sumiu = await Ate(() => cli.Atributos.Ascensao <= 1.005f && LinhaNaTela(menu) == null, 8);
		Checa("DE VOLTA A MIL DE BP a linha some sozinha (a assinatura da aba a tira)", sumiu, $"'{LinhaNaTela(menu)}'");
		await Foto("de-volta-sem-linha");
		menu.Fechar();

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	/// <summary>
	/// UM PASSO DA RAMPA: crava o BP, espera o servidor e o fio concordarem, e le a TELA sem forcar redesenho -- a linha tem de estar
	/// DENTRO do cartao Agora, com o texto que o fio manda. <paramref name="texto"/> nulo = so concordar com o fio.
	/// </summary>
	private async Tarefa Passo(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv, int eu, double bp, string foto, string oque, string? texto)
	{
		srv.CravarBpDaAscensaoDeTeste(eu, bp);
		double esperado = Ascensao.Rampa(bp);
		bool chegou = await Ate(() => Math.Abs(BoostNoServidor(srv, eu) - esperado) < 1e-6
									  && Math.Abs(cli.Atributos.Ascensao - esperado) < 1e-3
									  && LinhaNaTela(menu) == MenuJogo.TextoDaAscensao(cli.Atributos.Ascensao), 10);
		await Quadros(2);
		string? naTela = LinhaNaTela(menu);
		Control? pg = menu.PaginaDeTeste("Forms");
		PanelContainer? agora = pg == null ? null : CartaoComTitulo(pg, "Agora");
		HBoxContainer? linha = pg == null ? null : LinhaEscrita(pg, "Ascensão");
		bool noCartao = agora != null && linha != null && agora.IsAncestorOf(linha);
		double noServidor = BoostNoServidor(srv, eu);
		Checa(oque,
			  chegou && naTela != null && naTela == MenuJogo.TextoDaAscensao(cli.Atributos.Ascensao) && (texto == null || naTela == texto) && noCartao,
			  $"tela '{naTela}', fio x{cli.Atributos.Ascensao:0.####}, servidor x{noServidor:0.####}, rampa x{esperado:0.####}, no cartao Agora {noCartao}");
		await Foto(foto);
	}

	/// <summary>O `BPBoost` deste corpo no servidor (NaN se ele nao existe).</summary>
	private static double BoostNoServidor(Jandirus.Server.GameServer srv, int eu) =>
		srv.VerdadeDaAscensaoDeTeste(eu) is { } v ? v.BPBoost : double.NaN;

	/// <summary>O texto da linha "Ascensão" na aba Forms, pulando o que o `QueueFree` ainda nao tirou da arvore.</summary>
	private static string? LinhaNaTela(MenuJogo menu) =>
		menu.PaginaDeTeste("Forms") is { } pg && LinhaEscrita(pg, "Ascensão") is { } h && h.GetChildCount() > 1 && h.GetChild(1) is Label v
			? v.Text : null;

	// =====================================================================
	// OS AUXILIARES (o molde do RoboDoPoder e do RoboDasAbas)
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f.IsQueuedForDeletion()) continue;
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private static PanelContainer? CartaoComTitulo(Node raiz, string titulo) =>
		Todos(raiz).OfType<PanelContainer>().FirstOrDefault(p =>
			p.IsVisibleInTree() && p.HasMeta("cartao") && p.HasMeta("titulo") && p.GetMeta("titulo").AsString() == titulo);

	private static HBoxContainer? LinhaEscrita(Node raiz, string rotulo) =>
		Todos(raiz).OfType<HBoxContainer>().FirstOrDefault(h => h.HasMeta("linha") && h.GetMeta("linha").AsString() == rotulo);

	/// <summary>A tela inteira em `user://ascensao-NN-nome.png`. SO COM JANELA: as fotos sao pro olho de quem confere.</summary>
	private async Tarefa Foto(string nome)
	{
		if (SemJanela) return;
		await Quadros(3);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath($"user://ascensao-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
	}

	private async Tarefa Abrir(MenuJogo menu, string aba)
	{
		menu.IrPara(aba);
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
