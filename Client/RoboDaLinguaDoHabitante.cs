using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA LINGUA DO HABITANTE (`--diaglinguanpc`, 2026-11-01, o pedido 221) -- o dono: *"faça npcs de cada planeta falar na lingua nativa
/// deles, e tira o [lingua falada] q tem sempre q vc fala uma lingua q nao é a comum galatica"*. Um jogador HUMANO na Terra, e ao lado dele
/// dois habitantes que dizem a mesma frase pela fala do jogo:
///   N1. o habitante SAIYAJIN fala a nativa dele: o humano le a frase EMBARALHADA, sem nenhuma etiqueta de lingua -- e o habitante
///       TERRAQUEO, na lingua do humano, chega inteiro. O chat e os baloes na foto `lingua-npc-1-o-saiyajin-e-o-terraqueo`.
///
///     Godot --path . --host --rede 12940 --campoteste 8 --horateste 0.5 --semfauna --diaglinguanpc --raca Human --conta bancada_linguanpc --nome Ouvinte
/// </summary>
public partial class RoboDaLinguaDoHabitante : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<(string Autor, string Texto)> _falas = [];

	/// <summary>A frase dos dois habitantes: palavras de mais de duas letras, pra o embaralhador ter o que trocar.</summary>
	private const string Frase = "Bem-vindo ao nosso planeta, viajante! Hoje o mercado esta cheio.";

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

	private void AoFalar(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Diz) _falas.Add((autor, texto));
	}

	private string? DisseQuem(string autor) => _falas.FindLast(f => f.Autor == autor) is { Autor.Length: > 0 } f ? f.Texto : null;

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia is { PosicaoLocalDeTeste: not null } && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		GD.Print("[dlinguanpc] ===== A LINGUA DO HABITANTE (dono, 2026-11-01, o pedido 221) =====");
		int eu = cli.LocalId;
		cli.Falou += AoFalar;
		srv.EmbateDeFoto_CeuLimpo(eu);
		await Segundos(1.5);

		// ------------------------------------------------------------ N1. o saiyajin e o terraqueo dizem a mesma frase
		const string Saiyajin = "Habitante Saiyajin", Terraqueo = "Habitante Terraqueo";
		int sai = srv.PalcoLN_Habitante(eu, "Saiyan", Saiyajin, 2.5f);
		int ter = srv.PalcoLN_Habitante(eu, "Human", Terraqueo, -2.5f);
		bool aVista = await Ate(() => World.Instancia?.CorpoDeTeste(sai) != null && World.Instancia?.CorpoDeTeste(ter) != null, 8);
		await Segundos(1.0);
		_falas.Clear();
		srv.PalcoLN_Dizer(sai, Frase);
		srv.PalcoLN_Dizer(ter, Frase);
		bool ouviu = await Ate(() => DisseQuem(Saiyajin) != null && DisseQuem(Terraqueo) != null, 6);
		await Segundos(1.2);
		string doSaiyajin = DisseQuem(Saiyajin) ?? "", doTerraqueo = DisseQuem(Terraqueo) ?? "";
		string[] palavras = [.. Frase.Split(' ', ',', '!', '.', '-').Where(w => w.Length > 2)];
		bool nenhumaInteira = palavras.All(w => !doSaiyajin.Split(' ', ',', '!', '.', '-').Contains(w));
		Fotografar("lingua-npc-1-o-saiyajin-e-o-terraqueo");
		Conferir(sai != 0 && ter != 0 && aVista && ouviu && doSaiyajin.Length > 0 && doSaiyajin != Frase && nenhumaInteira
				 && !doSaiyajin.Contains('[') && !doSaiyajin.Contains("língua") && doTerraqueo == Frase,
				 $"N1. **O HABITANTE SAIYAJIN FALA A NATIVA DELE**: o humano le EMBARALHADO e sem etiqueta -- e o TERRAQUEO chega inteiro [saiyajin "
				 + $"'{doSaiyajin}'; terraqueo '{doTerraqueo}']");
		srv.PalcoLN_Limpar();
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
		foreach (string l in _linhas) GD.Print("[dlinguanpc] " + l);
		GD.Print(_falhas.Count == 0 ? "[dlinguanpc] ===== TUDO OK =====" : $"[dlinguanpc] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dlinguanpc]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
