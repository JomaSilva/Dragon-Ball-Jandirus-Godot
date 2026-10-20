using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO GOLPE NA TELA (`--diaggolpe`, 2026-10-19): o rotulo do HUD ("GOLPE LETAL" / "nao-letal") e a tecla dizem o golpe que o
/// SERVIDOR aplica, e nao o ultimo que a tecla pediu. O dono: *"modo nao letal ainda ta matando as vezes"* -- e uma das causas era esta: o
/// servidor troca o golpe por conta propria (o fim do treino com o Whis e do torneio devolvem o de antes, a saida da meditacao profunda
/// tambem, a posse do Neo Tuffle troca o corpo de luta), e a tela continuava dizendo "nao-letal" com ele matando.
///
/// Com `--host` (o servidor no mesmo processo), um corpo no campo aberto:
///   1. A TECLA PEDE, A FICHA RESPONDE: a tecla liga o golpe NO SERVIDOR, e o rotulo diz "GOLPE LETAL" quando a ficha volta.
///   2. O SERVIDOR LIGA SOZINHO (o caso que matava): com a tela em "nao-letal", o servidor religa o golpe -- e o rotulo passa a dizer
///      "GOLPE LETAL". Foto `golpe-2-servidor-ligou.png`.
///   3. A TECLA SEGUINTE DESLIGA DE VERDADE: ela alterna a partir do que o servidor aplica, e o servidor fica nao-letal.
///   4. E NO OUTRO SENTIDO: o servidor desliga sozinho, e o rotulo volta a "nao-letal".
///   5. CONTRA-EXEMPLO (`LetalPeloPalpiteDeTeste`, o jogo de antes): o passo 2 deixa a tela em "nao-letal" com o servidor
///      letal, e a tecla seguinte o DEIXA letal -- as reguas dos passos 2 e 3 reprovam. Foto `golpe-5-defeito.png`.
///
/// A TECLA E A LINHA DELA (`LocalPlayer.LerMira`: `cli.SendLethal(!cli.Letal)`), chamada daqui: o que se mede e o cliente entre o pedido e
/// a ficha, e nao o mapa de teclas.
///
///     Godot --path . --host --rede 8941 --campoteste 12 --semfauna --diaggolpe --raca Human --conta bancada_golpe --nome Golpeador
/// </summary>
public partial class RoboDoGolpe : Node
{
	/// <summary>
	/// DEFEITO INJETADO (este robo): o cliente volta a guardar o PALPITE do que a tecla pediu e a ignorar a ficha -- o jogo de antes de
	/// 2026-10-19. Lido pelas duas linhas do conserto (`GameClient.SendLethal` e `GameClient.SeguirOLetalDaFicha`). Mora aqui, e nao ao
	/// lado delas, pra este arquivo compilar sozinho contra o repo de antes do conserto. Falso em jogo, sempre.
	/// </summary>
	internal static bool LetalPeloPalpiteDeTeste;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>O que a ficha leva pra a tela voltar: o pacote de ficha sai na hora do pedido, e o do tique a 5 Hz.</summary>
	private const double EsperaDaFicha = 2;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	// O BOTAO DE DEFEITO NUNCA FICA LIGADO, nem se o robo sair no meio.
	public override void _ExitTree() => LetalPeloPalpiteDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	/// <summary>O rotulo do golpe no HUD, como o jogador o le.</summary>
	private static string Rotulo() =>
		Hud.Instancia?.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text is "GOLPE LETAL" or "nao-letal")?.Text ?? "(sem rotulo)";

	/// <summary>A tecla do golpe letal -- a linha dela no `LocalPlayer`.</summary>
	private static void Tecla(GameClient cli) => cli.SendLethal(!cli.Letal);

	/// <summary>A tela diz o que o servidor aplica: o rotulo, o `Letal` do cliente e o do servidor, os tres iguais a <paramref name="letal"/>.</summary>
	private static bool Bate(GameClient cli, Jandirus.Server.GameServer srv, int eu, bool letal) =>
		srv.LetalNoServidorDeTeste(eu) == letal && cli.Letal == letal && Rotulo() == (letal ? "GOLPE LETAL" : "nao-letal");

	private static string Como(GameClient cli, Jandirus.Server.GameServer srv, int eu) =>
		$"servidor {(srv.LetalNoServidorDeTeste(eu) ? "LETAL" : "nao-letal")}, cliente {(cli.Letal ? "LETAL" : "nao-letal")}, rotulo \"{Rotulo()}\"";

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && Hud.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o HUD nasceram e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);   // o corpo assenta e a primeira ficha chega
		GD.Print("[dgolpe] ===== O GOLPE NA TELA E O DO SERVIDOR (dono, 2026-10-19) =====");
		Conferir(Bate(cli, srv, eu, false), $"(premissa) o corpo nasce nao-letal, e a tela diz isso ({Como(cli, srv, eu)})");

		// ---- 1. a tecla pede, a ficha responde ----
		Tecla(cli);
		bool ligou = await Ate(() => Bate(cli, srv, eu, true), EsperaDaFicha);
		Conferir(ligou, $"1. A TECLA PEDE, A FICHA RESPONDE: a tecla liga o golpe no SERVIDOR, e o rotulo diz \"GOLPE LETAL\" ({Como(cli, srv, eu)})");
		Tecla(cli);
		await Ate(() => Bate(cli, srv, eu, false), EsperaDaFicha);

		// ---- 2 e 3. o servidor liga sozinho, e a tecla seguinte desliga de verdade ----
		(bool telaLetal, bool teclaDesliga) = await OServidorLigaSozinho(cli, srv, eu, "user://golpe-2-servidor-ligou.png");
		Conferir(telaLetal, "2. O SERVIDOR LIGA SOZINHO (o fim do treino, do torneio, da meditacao profunda): com a tela em \"nao-letal\", o servidor religa "
							+ $"o golpe -- e o rotulo passa a dizer \"GOLPE LETAL\" ({Como(cli, srv, eu)})");
		Conferir(teclaDesliga, $"3. A TECLA SEGUINTE DESLIGA DE VERDADE: ela alterna a partir do golpe do servidor, e ele fica nao-letal ({Como(cli, srv, eu)})");

		// ---- 4. e no outro sentido: a tela em "GOLPE LETAL" (pela tecla, se precisar), e o servidor desliga sozinho ----
		if (!cli.Letal) Tecla(cli);
		await Ate(() => Bate(cli, srv, eu, true), EsperaDaFicha);
		srv.TrocarOLetalNoServidorDeTeste(eu, false);
		bool desligou = await Ate(() => Bate(cli, srv, eu, false), EsperaDaFicha);
		Conferir(desligou, $"4. E NO OUTRO SENTIDO: o servidor desliga sozinho, e o rotulo volta a \"nao-letal\" ({Como(cli, srv, eu)})");

		// ---- 5. o contra-exemplo ----
		LetalPeloPalpiteDeTeste = true;
		try
		{
			(bool telaLetalDeAntes, bool teclaDeAntes) = await OServidorLigaSozinho(cli, srv, eu, "user://golpe-5-defeito.png");
			Conferir(!telaLetalDeAntes && !teclaDeAntes,
					 "CONTRA-EXEMPLO (defeito injetado: o cliente guarda o palpite da tecla, o jogo de antes): o servidor religa o golpe e a tela CONTINUA dizendo "
					 + $"\"nao-letal\", e a tecla seguinte o deixa letal -- as reguas dos passos 2 e 3 reprovam ({Como(cli, srv, eu)})");
		}
		finally { LetalPeloPalpiteDeTeste = false; }

		// DE VOLTA A VERDADE: o servidor desliga, e a ficha que muda leva a tela junto.
		srv.TrocarOLetalNoServidorDeTeste(eu, true);
		await Ate(() => Bate(cli, srv, eu, true), EsperaDaFicha);
		srv.TrocarOLetalNoServidorDeTeste(eu, false);
		bool voltou = await Ate(() => Bate(cli, srv, eu, false), EsperaDaFicha);
		Conferir(voltou, $"...e desfeito o defeito, a tela volta a seguir o servidor ({Como(cli, srv, eu)})");
	}

	/// <summary>
	/// O CASO QUE MATAVA, do comeco: a tecla deixa tudo nao-letal, o servidor religa sozinho, e a tecla e apertada de novo. Devolve se a tela
	/// mostrou o golpe letal depois da troca e se a tecla seguinte deixou o servidor nao-letal.
	/// </summary>
	private async System.Threading.Tasks.Task<(bool TelaLetal, bool TeclaDesliga)> OServidorLigaSozinho(GameClient cli, Jandirus.Server.GameServer srv, int eu,
																										 string foto)
	{
		// a tecla ate a tela dizer "nao-letal", como o jogador a deixaria
		if (cli.Letal) Tecla(cli);
		await Ate(() => !cli.Letal && !srv.LetalNoServidorDeTeste(eu), EsperaDaFicha);

		srv.TrocarOLetalNoServidorDeTeste(eu, true);
		bool telaLetal = await Ate(() => Bate(cli, srv, eu, true), EsperaDaFicha);
		await Segundos(0.3);
		Fotografar(foto);

		Tecla(cli);
		bool desliga = await Ate(() => !srv.LetalNoServidorDeTeste(eu) && Bate(cli, srv, eu, false), EsperaDaFicha);
		return (telaLetal, desliga);
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
		LetalPeloPalpiteDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dgolpe] " + l);
		GD.Print(_falhas.Count == 0 ? "[dgolpe] ===== TUDO OK =====" : $"[dgolpe] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dgolpe]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
