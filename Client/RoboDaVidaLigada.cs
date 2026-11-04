using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// AS FOTOS DO PEDIDO 216 (`--diagvidaligada`, 2026-11-01) -- o dono: *"N esqueça de tirar foto de tudo pra revisao"*. Com janela e com
/// `--host` (o palco e `GameServer.VidaLigadaPalco.cs`): o jogador senta no trono do Deus da Destruicao, com dois Kaioshins de jogador ao lado.
///   V1. um Kaioshin cai: o CARTAO VERMELHO na tela e a linha vermelha no chat, com quem caiu e quantos restam (*"o player deus da destruição
///       recebe um aviso urgente q alguem ta matando um kaioshin"*) -- foto `vidaligada-1-aviso-urgente`;
///   V2. o ultimo cai: o aviso de "o ultimo", e o Deus cai junto (*"com isso de todos os players kaioshins morrerem o deus da destruição tb
///       morre"*) -- foto `vidaligada-2-o-deus-cai`. No fim o Deus volta a vida e os tronos ao que eram.
///
///     Godot --path . --host --rede 12851 --horateste 0.5 --semfauna --diagvidaligada --raca Human --conta bancada_vidaligada --nome Deus
/// </summary>
public partial class RoboDaVidaLigada : Node
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

	private static int LinhasUrgentes() => Chat.LinhasDeTeste().Count(l => l.Canal == Protocol.Fala.AvisoUrgente);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
										 && AvisoUrgenteNaTela.Instancia != null && Chat.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu, o servidor esta neste processo e o cartao do aviso existe");
		if (!pronto || C is not { } cli || S is not { } srv || AvisoUrgenteNaTela.Instancia is not { } cartao) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dvidaligada] ===== AS FOTOS DA VIDA LIGADA DOS KAIOSHINS E DO DEUS DA DESTRUICAO (dono, 2026-11-01) =====");
		try
		{
			(int a, int b) = srv.PalcoVL_Montar(eu);
			await Segundos(1.5);

			// ---------------------------------------------------------------- V1: um Kaioshin cai
			int antes = LinhasUrgentes();
			srv.PalcoVL_Matar(a);
			bool avisou = await Ate(() => cartao.NaTela && cartao.TextoDesenhado.Contains("acabou de morrer", StringComparison.Ordinal), 6);
			await Segundos(1.0);
			Fotografar("vidaligada-1-aviso-urgente");
			string texto1 = cartao.TextoDesenhado;
			Conferir(a != 0 && b != 0 && avisou && texto1.Contains("resta 1 Kaioshin vivo", StringComparison.Ordinal)
					 && LinhasUrgentes() > antes && !cli.Sheet.Morto,
					 "V1. UM KAIOSHIN CAI: o cartao vermelho na tela e a linha no chat, com quem caiu e quantos restam, e o Deus vive "
					 + $"[Kaioshins {a}/{b}; cartao \"{texto1}\"; linhas urgentes {antes} -> {LinhasUrgentes()}; morto {cli.Sheet.Morto}]");
			Conferir(Chat.FormatarDeTeste(Protocol.Fala.AvisoUrgente, "x").Contains(Chat.CorDoUrgente, StringComparison.Ordinal)
					 && Chat.AbaMostraDeTeste("Sistema", Protocol.Fala.AvisoUrgente),
					 "V1b. a linha urgente sai no vermelho dela e mora na aba Sistema");

			// ---------------------------------------------------------------- V2: o ultimo cai, e o Deus com ele
			srv.PalcoVL_Matar(b);
			bool caiu = await Ate(() => cli.Sheet.Morto, 6);
			await Ate(() => cartao.TextoDesenhado.Contains("você morre também", StringComparison.Ordinal), 3);
			await Segundos(0.6);
			Fotografar("vidaligada-2-o-deus-cai");
			Conferir(caiu && cartao.NaTela && cartao.TextoDesenhado == Jandirus.Server.GameServer.FraseDaMorteDoDeus,
					 $"V2. O ULTIMO KAIOSHIN CAI, E O DEUS DA DESTRUICAO CAI JUNTO [morto {cli.Sheet.Morto}; cartao \"{cartao.TextoDesenhado}\"]");
		}
		finally
		{
			srv.PalcoVL_Desfazer(eu);
			cartao.Esconder();
		}
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
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
		foreach (string l in _linhas) GD.Print("[dvidaligada] " + l);
		GD.Print(_falhas.Count == 0 ? "[dvidaligada] ===== TUDO OK =====" : $"[dvidaligada] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dvidaligada]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
