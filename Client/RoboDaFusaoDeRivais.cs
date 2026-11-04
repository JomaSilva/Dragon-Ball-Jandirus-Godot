using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA FUSAO DE RIVAIS (`--diagfusaorival`, 2026-11-01, o pedido 211): a foto que o dono pede de tudo (*"tire print de tudo pra provar
/// e revisar"*). Com janela e com `--host`: um corpo ao lado do jogador (o palco da foto da fusao, `ForjarParaAFotoDeFusao`), os dois se
/// declaram "rival amigo" um do outro pelo funil do `Convivio`, a Potara sai pelo funil da cena (`ComecarACenaNaFotoDeFusao`) e, fundidos,
/// o chat diz que a rivalidade deixou a fusao 15% mais forte -- foto `dfusaorival-1-aviso`. Os numeros (os 15% na Danca, na Potara e na
/// estragada, e a declaracao de um lado so) sao da `--fusaoduplateste` (secao N).
///
///     Godot --path . --host --rede 12806 --campoteste 8 --horateste 0.5 --semfauna --diagfusaorival --raca Saiyan --conta bancada_fusaorival --nome Kakarotto
/// </summary>
public partial class RoboDaFusaoDeRivais : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private const float T = 32f;

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

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);
	private bool Disse(string trecho) => _ditas.Any(t => t.Contains(trecho, StringComparison.Ordinal));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dfusaorival] ===== A FUSAO DE RIVAIS (dono, 2026-11-01) =====");
		srv.SolAPinoNaFotoDeFusao(eu);
		srv.CeuLimpoNaFotoDeFusao(eu);
		cli.Falou += Ouvir;
		try
		{
			int outro = srv.ForjarParaAFotoDeFusao(eu, new Vec2(2 * T, 0), "Ouji", 50_000, "Vegeta");
			Conferir(outro != 0, "(montagem) o outro corpo nasceu ao lado do jogador");
			if (outro == 0) return;

			// ---------------------------------------------------------------- R1: os dois se declaram rivais
			bool rivais = srv.RivaisNaFotoDeFusao(eu, outro);
			Conferir(rivais, "R1. OS DOIS SE DECLARAM RIVAL AMIGO UM DO OUTRO (a aba People, pelo funil do convivio)");
			await Segundos(1.0);

			// ---------------------------------------------------------------- R2: a Potara, e o aviso da rivalidade
			bool cena = srv.ComecarACenaNaFotoDeFusao(eu, outro, TipoDeFusao.Potara);
			bool fundiu = await Ate(() => srv.EstadoNaFotoDeFusao(eu).Fundido, 30);
			bool ouviu = await Ate(() => Disse("rivalidade"), 6);
			await Segundos(2.5);   // a luz da cena se apaga e o chat assenta
			Fotografar("user://dfusaorival-1-aviso.png");
			Conferir(cena && fundiu && ouviu && Disse("15% mais forte"),
					 "R2. RIVAIS FUNDIDOS PELA POTARA: o chat diz que a rivalidade deixa a fusao 15% mais forte (*\"personagens q sao rivais (rival bom) "
					 + "tenham um boost de poder se fizerem fusao\"*) "
					 + $"[cena {cena}; fundiu {fundiu}; ouvi: {string.Join(" | ", _ditas.TakeLast(3))}]");
			srv.SepararNaFotoDeFusao(eu, "fim do robo da fusao de rivais");
		}
		finally
		{
			cli.Falou -= Ouvir;
			srv.LimparAFotoDeFusao();
		}
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
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
		foreach (string l in _linhas) GD.Print("[dfusaorival] " + l);
		GD.Print(_falhas.Count == 0 ? "[dfusaorival] ===== TUDO OK =====" : $"[dfusaorival] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dfusaorival]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
