using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ESFERA NA TECLA E (`--diagesferae`, 2026-10-18): o dono -- *"n to conseguindo pegar a esfera do dragao"* e *"seria bom aparecer a
/// mensagem de pegar a esfera assim como pega um item dropado"*. Com janela e `--host`, nascendo Humano no campo da Terra:
///   E1. uma Esfera do Dragao a um tile do corpo (o palco a traz pra perto): a dica no pe da tela diz "[E] pegar a Esfera de N estrelas", como a
///       de um item largado -- foto `esfera-1-dica`; CONTRA-EXEMPLO (`MenuDeInteracao.EsferaSoPeloMenuDeTeste`, o jogo de antes): sem dica --
///       foto `esfera-1b-defeito`;
///   E2. a tecla E de verdade: o servidor diz "voce pega a ..." e a esfera sai do chao -- foto `esfera-2-pegou`.
///
///     Godot --path . --host --rede 8471 --campoteste 12 --horateste 0.5 --semfauna --diagesferae --raca Human --conta bancada_esferae --nome Catador
/// </summary>
public partial class RoboDaEsferaNaTeclaE : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

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

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu da tecla E nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[desferae] ===== A ESFERA DO DRAGAO NA TECLA E (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		int numero = srv.PalcoEE_EsferaAoLado(eu);
		bool chegou = numero > 0 && await Ate(() => cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Numero == numero), 10);
		await Segundos(0.8);
		string esperada = $"[E] pegar a {Jandirus.Core.Magic.Esferas.NomeDaEsfera(numero)}";
		Conferir(chegou && menu.DicaNaTela == esperada,
				 "E1. A ESFERA A UM TILE, E A DICA DE PEGAR APARECE (*\"seria bom aparecer a mensagem de pegar a esfera assim como pega um item "
				 + $"dropado\"*) [esfera {numero} na tela {chegou}; dica \"{menu.DicaNaTela}\"]");
		Fotografar("user://esfera-1-dica.png");

		MenuDeInteracao.EsferaSoPeloMenuDeTeste = true;
		string dicaDeAntes;
		try
		{
			await Quadros(4);
			dicaDeAntes = menu.DicaNaTela;
			Fotografar("user://esfera-1b-defeito.png");
		}
		finally { MenuDeInteracao.EsferaSoPeloMenuDeTeste = false; }
		await Quadros(4);
		Conferir(!dicaDeAntes.Contains("Esfera", StringComparison.Ordinal),
				 $"CONTRA-EXEMPLO (defeito injetado: a tecla E nao enxerga a esfera, o jogo de antes): sem dica de pegar -- a regua de E1 reprovaria [\"{dicaDeAntes}\"]");

		_ditas.Clear();
		cli.Falou += Ouvir;
		try
		{
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = false });
			bool pegou = await Ate(() => _ditas.Any(t => t.StartsWith("você pega a", StringComparison.Ordinal))
										 && !cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Numero == numero), 5);
			Conferir(pegou, "E2. A TECLA E PEGA A ESFERA: o servidor diz que pegou, e ela sai do chao "
							+ $"[ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
		}
		finally { cli.Falou -= Ouvir; }
		await Segundos(0.5);
		Fotografar("user://esfera-2-pegou.png");
		srv.PalcoEE_Largar(eu);
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
		MenuDeInteracao.EsferaSoPeloMenuDeTeste = false;
		foreach (string l in _linhas) GD.Print("[desferae] " + l);
		GD.Print(_falhas.Count == 0 ? "[desferae] ===== TUDO OK =====" : $"[desferae] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[desferae]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
