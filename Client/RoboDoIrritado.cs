using Godot;
using Jandirus.Core.Forms;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// <c>--diagirritado</c> -- a foto do dono: a linha IRRITADO embaixo da vida, e o Super Saiyajin estreado por ela.
///
///     Godot --janela --resolution 1280x720 --host --rede 10121 --diagirritado --raca Saiyan --conta bancada_dirritado --nome Irritado
/// </summary>
public partial class RoboDoIrritado : Node
{
	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dirritado] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? "   " + detalhe : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	public override void _Ready()
	{
		ProcessPriority = -100;
		_ = Rodar();
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim", false, e.Message);
			Nota(e.ToString());
		}
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		Nota(_falha == 0 ? "===== TUDO OK =====" : $"===== {_falha} FALHA(S) =====");
		GetTree().Quit(_falha == 0 ? 0 : 1);
	}

	private async Tarefa Percurso()
	{
		Nota("o IRRITADO na tela");
		bool pronto = await Ate(() => GameClient.Instance is { Connected: true, LocalId: > 0 }
									  && GameServerVivo() && Hud.Instancia != null, 90);
		Checa("o mundo chegou e o servidor e deste processo", pronto);
		if (!pronto || GameClient.Instance is not { } cli || Hud.Instancia is not { } hud
			|| Jandirus.Server.GameServer.Instance is not { } srv)
			return;

		await Segundos(0.6);
		(NivelDeRaiva raiva, string forma) = srv.PalcoDoIrritado(cli.LocalId);
		bool linha = await Ate(() => hud.TextoDaRaivaDeTeste == "IRRITADO", 4);
		Checa("o HUD escreve IRRITADO embaixo da vida", linha && raiva == NivelDeRaiva.Irritado,
			  $"tela '{hud.TextoDaRaivaDeTeste}', degrau {raiva}");
		await Quadros(3);
		Fotografar("irritado-hud");

		bool estreou = await Ate(() => srv.PalcoDoIrritadoForma(cli.LocalId) == "ssj1", 4);
		Checa("o irritado estreia o Super Saiyajin sozinho (porta x2,5 paga)", estreou || forma == "ssj1",
			  $"forma {srv.PalcoDoIrritadoForma(cli.LocalId)}");
		await Segundos(1.2);
		Fotografar("irritado-forma");

		Hud.RaivaMudaDeTeste = true;
		try
		{
			hud.RedesenharOPoderEmUsoDeTeste();
			await Quadros(2);
			Checa("(defeito injetado: o painel nao escreve a raiva) a linha IRRITADO REPROVA",
				  hud.TextoDaRaivaDeTeste == "");
		}
		finally { Hud.RaivaMudaDeTeste = false; }
	}

	private static bool GameServerVivo() => Jandirus.Server.GameServer.Instance != null;

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto `{nome}` (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
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
