using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS SENTIDOS (`--diagsentidos`, 2026-10-18): as fotos de dois pedidos do dono --
///   * *"coloque um bp maximo no scouter de 100m de bp, acima disso o scouter marca com ???, e pessoas q estao em formas com godki, ou
///     estao em constante uso de ki divino (...) n podem ter o poder lido com scouter ou ter o ki sentido"* -- e *"scouter NUNCA consegue
///     detectar ou sentir ki divino"*;
///   * *"adicione niveis na skill de sense, assim aumentando a area de efeito, começando no campo de visao, dps regiao, dps planeta, dps
///     sistema, e dps o universo"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra, com os corpos do palco em volta (`PalcoSN_Montar`):
///   S1. o SCAN (scouter ligado): o Abaixo (60 milhoes) em numero, o Acima (160 milhoes) em "???", e nem o Dourado (Golden Apple) nem o
///       Supremo Kaioh na lista -- foto `sentidos-1-scan`; CONTRA-EXEMPLO (`GameServer.KiDivinoAparecerDeTeste`): os dois divinos aparecem
///       -- foto `sentidos-1b-defeito-divino`;
///   S2. a aba STATS com o proprio poder acima de 100 milhoes: "???" -- foto `sentidos-2-stats-acima`; CONTRA-EXEMPLO
///       (`Scouter.SemTetoDeTeste`): o numero;
///   S3. o SENSE (sem scouter) nos cinco niveis: a faixa diz o nivel e o alcance, e a cada nivel entra o corpo do alcance novo -- fotos
///       `sentidos-3-nivel-1` a `-5`; o Dourado e o Kaioh nunca (o ki divino, pra quem nao o tem).
///
///     Godot --path . --host --rede 8472 --campoteste 12 --horateste 0.5 --semfauna --diagsentidos --raca Human --conta bancada_sentidos --nome Sensor
/// </summary>
public partial class RoboDosSentidos : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

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

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu P nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dsentidos] ===== O SCOUTER COM TETO E SEM KI DIVINO, E OS CINCO NIVEIS DO SENSE (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		Conferir(srv.PalcoSN_Montar(eu), "(palco) os corpos em volta: Abaixo, Acima, Dourado, Kaioh, Vizinho, NaRegiao, NoPlaneta, NoEspaco e EmNamek");
		try
		{
			await OScan(cli, srv, menu, eu);
			await AFichaAcimaDoTeto(cli, srv, menu, eu);
			await OsNiveisDoSense(cli, srv, menu, eu);
		}
		finally
		{
			Jandirus.Server.GameServer.KiDivinoAparecerDeTeste = false;
			Jandirus.Core.Stats.Scouter.SemTetoDeTeste = false;
			srv.PalcoSN_Limpar(eu);
		}
	}

	private static Protocol.PresencaState? Achar(GameClient cli, string nome) => cli.Sentidos.FirstOrDefault(p => p.Nome == nome) is { Nome.Length: > 0 } p ? p : null;

	private static bool Tem(GameClient cli, string nome) => cli.Sentidos.Any(p => p.Nome == nome);

	private static string Lista(GameClient cli) =>
		string.Join(", ", cli.Sentidos.Select(p => $"{(p.Nome.Length > 0 ? p.Nome : "???")}[a{p.Alcance} {(double.IsNaN(p.Bp) ? "bp ???" : $"bp {p.Bp:N0}")}]"));

	/// <summary>Abre o menu P na aba pedida (pelo botao dela, o caminho do dedo) e espera ela montar.</summary>
	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
	}

	// =====================================================================
	// S1) O SCAN
	// =====================================================================
	private async Tarefa OScan(GameClient cli, Jandirus.Server.GameServer srv, MenuJogo menu, int eu)
	{
		srv.PalcoSN_Scouter(eu, true);
		bool leu = await Ate(() => cli.SentidosSaoDoScouter && Tem(cli, "Abaixo") && Tem(cli, "Acima"), 8);
		await Aba(menu, "Scan");
		await Segundos(0.5);
		Protocol.PresencaState? abaixo = Achar(cli, "Abaixo"), acima = Achar(cli, "Acima");
		bool interrogacao = menu.PaginaDeTeste("Scan") is { } pg && Todos(pg).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text == "???");
		Conferir(leu && abaixo is { } a && !double.IsNaN(a.Bp) && a.Bp <= Jandirus.Core.Stats.Scouter.TetoDeLeitura
				 && acima is { } b && double.IsNaN(b.Bp) && interrogacao,
				 "S1. **O SCOUTER TEM TETO** (*\"acima disso o scouter marca com ???\"*): na aba Scan o Abaixo vem em numero e o Acima (160 milhoes) "
				 + $"em ???, escrito na tela [{Lista(cli)}; ??? desenhado {interrogacao}]");
		Conferir(!Tem(cli, "Dourado") && !Tem(cli, "Kaioh"),
				 "S1b. **E NAO DETECTA KI DIVINO** (*\"scouter NUNCA consegue detectar ou sentir ki divino\"*): nem o Dourado (Golden Apple) nem o "
				 + $"Supremo Kaioh aparecem, com os dois a poucos tiles [{Lista(cli)}]");
		Fotografar("user://sentidos-1-scan.png");

		Jandirus.Server.GameServer.KiDivinoAparecerDeTeste = true;
		bool apareceram;
		try
		{
			apareceram = await Ate(() => Tem(cli, "Dourado") && Tem(cli, "Kaioh"), 5);
			await Quadros(8);
			Fotografar("user://sentidos-1b-defeito-divino.png");
		}
		finally { Jandirus.Server.GameServer.KiDivinoAparecerDeTeste = false; }
		Conferir(apareceram, $"CONTRA-EXEMPLO (defeito injetado: o ki divino visivel, o de antes): os dois divinos aparecem no Scan -- a regua de S1b reprovaria [{Lista(cli)}]");
		await Ate(() => !Tem(cli, "Dourado"), 5);
	}

	// =====================================================================
	// S2) A FICHA ACIMA DO TETO
	// =====================================================================
	private async Tarefa AFichaAcimaDoTeto(GameClient cli, Jandirus.Server.GameServer srv, MenuJogo menu, int eu)
	{
		srv.PalcoSN_MeuPoder(eu, 160_000_000);
		bool estourou = await Ate(() => double.IsNaN(cli.Sheet.ExpressedBP), 5);
		await Aba(menu, "Stats");
		await Segundos(0.5);
		string faixa = menu.ValorDesenhado("Stats", "Battle Power") ?? "";
		Conferir(estourou && faixa.StartsWith("???", StringComparison.Ordinal),
				 $"S2. **A PROPRIA FICHA ACIMA DE 100 MILHOES**: com o scouter ligado, a aba Stats escreve ??? [faixa \"{faixa}\"]");
		Fotografar("user://sentidos-2-stats-acima.png");

		Jandirus.Core.Stats.Scouter.SemTetoDeTeste = true;
		string faixaDeAntes;
		try
		{
			srv.PalcoSN_Ficha(eu);
			await Ate(() => !double.IsNaN(cli.Sheet.ExpressedBP), 5);
			await Quadros(8);
			faixaDeAntes = menu.ValorDesenhado("Stats", "Battle Power") ?? "";
		}
		finally { Jandirus.Core.Stats.Scouter.SemTetoDeTeste = false; }
		Conferir(faixaDeAntes.Length > 0 && char.IsDigit(faixaDeAntes[0]),
				 $"CONTRA-EXEMPLO (defeito injetado: o scouter sem teto, o de antes): a faixa traz o numero -- a regua de S2 reprovaria [\"{faixaDeAntes}\"]");
		srv.PalcoSN_MeuPoder(eu, -1);
		srv.PalcoSN_Scouter(eu, false);
	}

	// =====================================================================
	// S3) OS CINCO NIVEIS DO SENSE
	// =====================================================================
	private async Tarefa OsNiveisDoSense(GameClient cli, Jandirus.Server.GameServer srv, MenuJogo menu, int eu)
	{
		(int Nivel, string Entra)[] degraus = [(1, "Vizinho"), (2, "NaRegiao"), (3, "NoPlaneta"), (4, "NoEspaco"), (5, "EmNamek")];
		foreach ((int nivel, string entra) in degraus)
		{
			srv.PalcoSN_Sense(eu, nivel);
			bool chegou = await Ate(() => !cli.SentidosSaoDoScouter && cli.NivelDoSense == nivel && Tem(cli, entra), 8);
			await Aba(menu, "Sense");
			await Segundos(0.6);
			string faixa = menu.ValorDesenhado("Sense", "Presenças") ?? "";
			bool proximoFora = nivel == SenseEmNiveis.NivelMaximo || !Tem(cli, degraus[nivel].Entra);
			Conferir(chegou && proximoFora && faixa.Contains($"Sense nível {nivel} de {SenseEmNiveis.NivelMaximo}", StringComparison.Ordinal)
					 && faixa.Contains(SenseEmNiveis.NomeDoAlcance(nivel), StringComparison.Ordinal)
					 && !Tem(cli, "Dourado") && !Tem(cli, "Kaioh"),
					 $"S3.{nivel} **SENSE NIVEL {nivel}, {SenseEmNiveis.NomeDoAlcance(nivel).ToUpperInvariant()}**: a faixa diz o nivel e o alcance, o {entra} "
					 + "entra, o do alcance seguinte ainda nao -- e os divinos nunca (o ki divino, pra quem nao o tem) "
					 + $"[faixa \"{faixa}\"; {Lista(cli)}]");
			Fotografar($"user://sentidos-3-nivel-{nivel}.png");
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
		Jandirus.Server.GameServer.KiDivinoAparecerDeTeste = false;
		Jandirus.Core.Stats.Scouter.SemTetoDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dsentidos] " + l);
		GD.Print(_falhas.Count == 0 ? "[dsentidos] ===== TUDO OK =====" : $"[dsentidos] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dsentidos]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
