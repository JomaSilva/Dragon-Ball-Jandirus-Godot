using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO CARGO DA PATRULHA (`--diagcargopg`, 2026-10-31, o pedido 198i) -- as fotos dos exemplos do dono. Com janela e `--host` (o palco
/// e `GameServer.CargoDaPatrulhaPalco.cs`):
///   C1. fraco demais, "Entrar na Patrulha" e recusado e diz o requisito -- foto `cargopg-1-o-requisito`;
///   C2. com 50 mil de poder e karma limpo, ele entra: o scouter de graca e a primeira missao -- foto `cargopg-2-entrou`;
///   C3. a aba Nav: o botao "missão da Patrulha" leva a carta ao planeta da missao -- foto `cargopg-3-o-botao-do-nav`;
///   C4. no planeta da missao, o foragido nasce -- foto `cargopg-4-o-foragido`;
///   C5. derrotado, a missao e cumprida (zeni e karma) -- foto `cargopg-5-missao-cumprida`;
///   C6. (o pedido 200) o Freeza fica solto invadindo Icer: so a Patrulha sabe -- vira a missao do membro -- foto `cargopg-6-missao-do-vilao`;
///   C7. o botao "missão da Patrulha" da carta leva a Icer -- foto `cargopg-7-o-nav-leva-ao-vilao`;
///   C8. em Icer, o membro derrota o Freeza: a missao paga 1.000.000 de zeni -- foto `cargopg-8-o-vilao-paga`.
///
///     Godot --path . --host --rede 8490 --campoteste 8 --horateste 0.5 --semfauna --diagcargopg --raca Human --conta bancada_cargopg_r --nome Patrulheiro
/// </summary>
public partial class RoboDoCargoDaPatrulha : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _falas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private void AoFalar(Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);

	private bool Ouviu(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.Ordinal));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(3));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		GD.Print("[dcargopg] ===== O CARGO DA PATRULHA GALACTICA (dono, 2026-10-31, o pedido 198i) =====");
		int eu = cli.LocalId;
		cli.Falou += AoFalar;
		srv.PalcoPG_ForaDaZonaSegura(eu);
		await Segundos(2.0);
		srv.EmbateDeFoto_CeuLimpo(eu);

		// ------------------------------------------------------------ C1. o requisito
		srv.PalcoCP_Fraco(eu);
		_falas.Clear();
		cli.SendVerbo(Jandirus.Server.GameServer.VerboEntrar);
		bool recusou = await Ate(() => Ouviu("só aceita quem tem"), 5);
		await Segundos(0.8);
		Conferir(recusou, $"C1. **FRACO DEMAIS, A PATRULHA RECUSA E DIZ O REQUISITO** [{Ultimas()}]");
		Fotografar("cargopg-1-o-requisito");

		// ------------------------------------------------------------ C2. entrou
		srv.PalcoCP_Candidato(eu);
		await Segundos(0.5);
		_falas.Clear();
		cli.SendVerbo(Jandirus.Server.GameServer.VerboEntrar);
		bool entrou = await Ate(() => Ouviu("bem-vindo à Patrulha") && Ouviu("SCOUTER") && Ouviu("MISSÃO DA PATRULHA"), 5);
		await Segundos(0.8);
		Conferir(entrou && srv.PalcoCP_Missao(eu).Tipo != 0, $"C2. **ELE ENTRA: O SCOUTER DE GRACA E A PRIMEIRA MISSAO** [{Ultimas()}]");
		Fotografar("cargopg-2-entrou");

		// ------------------------------------------------------------ C3. o botao do Nav
		bool chegou = await Ate(() => cli.MissaoDaPatrulha is { Membro: true, TemMissao: true, NaCarta: true }, 5);
		menu.Abrir();
		menu.IrPara("Nav");
		await Quadros(10);
		Button? botao = Todos(menu).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "missão da Patrulha");
		botao?.EmitSignal(BaseButton.SignalName.Pressed);
		await Segundos(1.0);
		MapaEstelar? mapa = Todos(menu).OfType<MapaEstelar>().FirstOrDefault();
		string planeta = srv.PalcoCP_Missao(eu).Planeta;
		Conferir(chegou && botao != null && mapa?.Selecionado is { } sel && string.Equals(sel.Nome, planeta, StringComparison.OrdinalIgnoreCase),
				 $"C3. **O BOTAO \"missão da Patrulha\" DA CARTA LEVA AO PLANETA DA MISSAO** ({planeta}; selecionado: {mapa?.Selecionado?.Nome})");
		Fotografar("cargopg-3-o-botao-do-nav");
		menu.Fechar();
		await Segundos(0.5);

		// ------------------------------------------------------------ C4. o foragido
		_falas.Clear();
		srv.PalcoCP_IrAMissao(eu);
		bool nasceu = await Ate(() => srv.PalcoCP_Missao(eu).Foragido != 0, 8);
		srv.PalcoCP_ForagidoPerto(eu, 4);
		await Segundos(2.0);
		Conferir(nasceu && Ouviu("o foragido"), $"C4. **NO PLANETA DA MISSAO, O FORAGIDO NASCE** [{Ultimas()}]");
		Fotografar("cargopg-4-o-foragido");

		// ------------------------------------------------------------ C5. missao cumprida
		_falas.Clear();
		srv.PalcoCP_DerrotarOForagido(eu);
		bool cumpriu = await Ate(() => Ouviu("MISSÃO CUMPRIDA"), 5);
		await Segundos(1.0);
		Conferir(cumpriu, $"C5. **O FORAGIDO DERROTADO: MISSAO CUMPRIDA** (zeni e karma da Patrulha) [{Ultimas()}]");
		Fotografar("cargopg-5-missao-cumprida");

		// ------------------------------------------------------------ C6. (o pedido 200) o vilao pos-saga vira a missao
		await Segundos(1.5);
		_falas.Clear();
		bool solto = srv.PalcoSV_Vencer(eu, "freeza_namek", 2, "Icer");
		bool avisou = await Ate(() => Ouviu("MISSÃO DA PATRULHA") && Ouviu("invade Icer"), 5);
		bool doVilao = await Ate(() => srv.PalcoCP_Missao(eu) is { Tipo: 3, Planeta: "Icer" }
									   && cli.MissaoDaPatrulha is { Membro: true, TemMissao: true, NaCarta: true }, 5);
		await Segundos(1.0);
		Conferir(solto && avisou && doVilao && !Ouviu("o império dele invade"),
				 $"C6. **O FREEZA INVADE ICER E SO A PATRULHA SABE: VIRA A MISSAO DO MEMBRO** (o pedido 200) [{Ultimas()}]");
		Fotografar("cargopg-6-missao-do-vilao");

		// ------------------------------------------------------------ C7. o botao da carta leva ao vilao
		menu.Abrir();
		menu.IrPara("Nav");
		await Quadros(10);
		Button? doNav = Todos(menu).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "missão da Patrulha");
		doNav?.EmitSignal(BaseButton.SignalName.Pressed);
		await Segundos(1.0);
		MapaEstelar? carta = Todos(menu).OfType<MapaEstelar>().FirstOrDefault();
		Conferir(doNav != null && carta?.Selecionado is { } alvo && string.Equals(alvo.Nome, "Icer", StringComparison.OrdinalIgnoreCase),
				 $"C7. **O BOTAO \"missão da Patrulha\" DA CARTA LEVA AO PLANETA QUE O VILAO INVADE** (selecionado: {carta?.Selecionado?.Nome})");
		Fotografar("cargopg-7-o-nav-leva-ao-vilao");
		menu.Fechar();
		await Segundos(0.5);

		// ------------------------------------------------------------ C8. a missao do vilao paga
		srv.PalcoCP_IrAMissao(eu);
		srv.PalcoSV_Forte(eu);   // o chefe solto luta pra matar: o host tem de sobreviver a foto
		bool la = await Ate(() => srv.PalcoSV_Estado("freeza_namek").Corpo != 0, 8);
		// A DERROTA VEM LOGO, e a espera da cena de chegada depois: os soldados do imperio nascem escalados pelo poder do host (311
		// trilhoes contra o 1e15 dele) e, nos 5 s em que a cena saia da tela, mataram o host da r1 do agente soltosmissao -- a foto
		// saiu com ele de aureola no Outro Mundo. Derrotado o Freeza, o imperio foge do planeta e nao ha mais quem o mate.
		srv.PalcoSV_Perto(eu, "freeza_namek", 4);
		_falas.Clear();
		srv.PalcoSV_Derrotar(eu, "freeza_namek");
		bool pagou = await Ate(() => Ouviu("MISSÃO CUMPRIDA") && Ouviu("1.000.000 zeni"), 5);
		await Segundos(5.0);   // a cena de chegada do Freeza sai da tela
		bool vivo = srv.PalcoCP_Vivo(eu);
		Conferir(la && pagou && vivo, $"C8. **EM ICER, O MEMBRO DERROTA O FREEZA: A MISSAO PAGA 1.000.000 DE ZENI** (o pedido 200; vivo na foto: {vivo}) [{Ultimas()}]");
		Fotografar("cargopg-8-o-vilao-paga");
		srv.PalcoSV_Encerrar();
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
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
		if (C is { } cli) cli.Falou -= AoFalar;
		foreach (string l in _linhas) GD.Print("[dcargopg] " + l);
		GD.Print(_falhas.Count == 0 ? "[dcargopg] ===== TUDO OK =====" : $"[dcargopg] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dcargopg]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
