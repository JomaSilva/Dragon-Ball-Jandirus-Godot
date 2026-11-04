using Godot;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA CAMERA DO LOCK-ON (`--diagcameratrava`, 2026-10-31, o pedido 194). O dono: *"quando vc tiver com lock in em alguem, a camera
/// vai sempre ficar centralizada em vc e no inimigo, afastando o zoom se os personagens se afastarem etc (mesma coisa de observar torneio) ,
/// e faca isso ser uma opção no menu de pausa (esc) podendo ativar ou nao"*.
///
/// Com janela e `--host`, no campo aberto da bancada e sem fauna; tres corpos de pe a leste do jogador pelo gancho da foto dos corpos -- o
/// Perto (4 tiles), o Longe (12) e o Alem (18). O Perto e travado pela funcao da tecla L (`World.TravarNoMaisPerto`); o Longe e o Alem,
/// que a tecla nao alcanca (ela so pega quem esta a vista), pelo duplo clique em cima deles (`World.DuploCliqueDeTeste`) -- a MESMA marca.
/// O zoom do jogador e o de fabrica (3x); o piso do menu e 2x (`Settings.ZoomMin`).
///   C1. sem alvo: a camera no jogador, no zoom dele -- foto `camera-1-sem-alvo`;
///   C2. travado no Perto: a camera no MEIO dos dois, no zoom do jogador, e o olho do veu continua o do jogador -- foto
///       `camera-2-lock-perto-a-camera-no-meio`; CONTRA-EXEMPLOS (`World.CameraDoLockOnDesligadaDeTeste`: a camera volta pro jogador;
///       `World.OlhoNaCameraDoLockOnDeTeste`: o olho vai pro centro da camera);
///   C3. travado no Longe: o zoom AFASTA um degrau (2x) e os dois cabem na tela -- foto `camera-3-lock-longe-o-zoom-afasta`;
///   C4. travado no Alem (nem no 2x os dois cabem): o zoom fica no piso e a camera se inclina pro alvo sem o jogador sair da tela -- foto
///       `camera-4-alvo-alem-da-tela-o-jogador-fica-na-tela`;
///   C5. o menu de pausa (Esc) com a caixa "câmera do lock-on" ligada -- foto `camera-5-a-opcao-no-menu-de-pausa`; desligada PELA CAIXA, a
///       camera volta pro jogador e o zoom pro dele, com o alvo ainda travado -- foto `camera-6-opcao-desligada-a-camera-volta-pra-voce`.
///
///     Godot --path . --host --rede 8480 --campoteste 8 --horateste 0.5 --semfauna --diagcameratrava --raca Human --conta bancada_camtrava --nome Travador
/// </summary>
public partial class RoboDaCameraDoLockOn : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _obrasAntes = -1;

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
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia is { PosicaoLocalDeTeste: not null } && S != null
									  && PauseMenu.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu de pausa nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || World.Instancia is not { } mundo || PauseMenu.Instancia is not { } pausa) return;
		GD.Print("[dcameratrava] ===== A CAMERA DO LOCK-ON (dono, 2026-10-31, o pedido 194) =====");
		int eu = cli.LocalId;
		Boot.Config.CameraDoLockOn = true;
		srv.EmbateDeFoto_CeuLimpo(eu);
		const float T = ZoneCollision.TileSize;
		_obrasAntes = srv.ObrasAgoraNaFotoDeCorpos();
		int perto = srv.ForjarParaAFotoDeCorpos(eu, new Vec2(4 * T, 0), "Perto", 50_000);
		int longe = srv.ForjarParaAFotoDeCorpos(eu, new Vec2(12 * T, 0), "Longe", 50_000);
		int alem = srv.ForjarParaAFotoDeCorpos(eu, new Vec2(18 * T, 0), "Alem", 50_000);
		bool chegaram = await Ate(() => mundo.CorpoDeTeste(perto) != null && mundo.CorpoDeTeste(longe) != null && mundo.CorpoDeTeste(alem) != null, 10);
		int outros = srv.OutrosPertoNaFotoDeCorpos(eu, 20, perto, longe, alem);
		Conferir(perto != 0 && longe != 0 && alem != 0 && chegaram && outros == 0,
				 $"(palco) tres corpos de pe a leste: o Perto (4 tiles), o Longe (12) e o Alem (18), desenhados na tela -- e mais ninguem a 20 tiles ({outros} de fora)");
		int cheio = Boot.Config.Zoom;
		Conferir(cheio == 3 && Settings.ZoomMin == 2, $"(montagem) o zoom do jogador e o de fabrica, 3x, e o piso do menu e 2x (zoom {cheio}, piso {Settings.ZoomMin})");
		await Segundos(1.5);

		// ------------------------------------------------------------ C1. sem alvo
		Conferir(cli.AlvoId == 0 && Perto(mundo.DeslocamentoDaCameraDeTeste, Vector2.Zero) && mundo.ZoomDaCameraDeTeste == cheio,
				 $"C1. sem alvo, a camera esta no jogador, no zoom dele [desloc {mundo.DeslocamentoDaCameraDeTeste}, zoom {mundo.ZoomDaCameraDeTeste}]");
		Fotografar("camera-1-sem-alvo");

		// ------------------------------------------------------------ C2. o Perto: o meio
		mundo.TravarNoMaisPerto();
		await Segundos(1.5);
		Vector2 meio = Meio(mundo, perto);
		Conferir(cli.AlvoId == perto && Perto(mundo.DeslocamentoDaCameraDeTeste, meio) && mundo.ZoomDaCameraDeTeste == cheio,
				 $"C2. **TRAVADO NO PERTO, A CAMERA FICA NO MEIO DOS DOIS** (4 tiles cabem no zoom do jogador, 3x) [alvo {cli.AlvoId}; desloc "
				 + $"{mundo.DeslocamentoDaCameraDeTeste}, o meio {meio}; zoom {mundo.ZoomDaCameraDeTeste}]");
		Conferir(mundo.OlhoDoVeuDeTeste == null, "C2b. ...e o olho do veu continua o do jogador (quem trava nao sai do proprio corpo, como quem assiste ao torneio)");
		Fotografar("camera-2-lock-perto-a-camera-no-meio");

		World.CameraDoLockOnDesligadaDeTeste = true;
		bool voltou;
		try
		{
			await Segundos(1.5);
			voltou = Perto(mundo.DeslocamentoDaCameraDeTeste, Vector2.Zero);
		}
		finally { World.CameraDoLockOnDesligadaDeTeste = false; }
		Conferir(voltou, $"CONTRA-EXEMPLO (defeito injetado: a camera ignora o lock-on, o jogo de antes): com o alvo travado ela fica no jogador -- a regua de C2 reprovaria [desloc {mundo.DeslocamentoDaCameraDeTeste}]");
		World.OlhoNaCameraDoLockOnDeTeste = true;
		Vector2? olhoComDefeito;
		try
		{
			await Segundos(1.5);
			olhoComDefeito = mundo.OlhoDoVeuDeTeste;
		}
		finally { World.OlhoNaCameraDoLockOnDeTeste = false; }
		Conferir(olhoComDefeito != null, "CONTRA-EXEMPLO (defeito injetado: o olho do veu vai com a camera, como no torneio): o olho sai do corpo -- a regua de C2b reprovaria");

		// ------------------------------------------------------------ C3. o Longe: o zoom afasta
		Clicar(mundo, longe);
		await Segundos(1.5);
		meio = Meio(mundo, longe);
		Conferir(cli.AlvoId == longe && mundo.ZoomDaCameraDeTeste == cheio - 1 && Perto(mundo.DeslocamentoDaCameraDeTeste, meio)
				 && NaTela(mundo, mundo.PosicaoLocalDeTeste) && NaTela(mundo, mundo.CorpoDeTeste(longe)?.GlobalPosition),
				 $"C3. **TRAVADO NO LONGE (12 tiles), O ZOOM AFASTA UM DEGRAU** (3x -> 2x) e os dois cabem na tela, a camera no meio [alvo {cli.AlvoId}; "
				 + $"zoom {mundo.ZoomDaCameraDeTeste}; desloc {mundo.DeslocamentoDaCameraDeTeste}, o meio {meio}]");
		Fotografar("camera-3-lock-longe-o-zoom-afasta");

		// ------------------------------------------------------------ C4. o Alem: o piso, e o jogador na tela
		Clicar(mundo, alem);
		await Segundos(1.5);
		meio = Meio(mundo, alem);
		Conferir(cli.AlvoId == alem && mundo.ZoomDaCameraDeTeste == Settings.ZoomMin && mundo.DeslocamentoDaCameraDeTeste.X > 0
				 && mundo.DeslocamentoDaCameraDeTeste.X < meio.X - 8 && NaTela(mundo, mundo.PosicaoLocalDeTeste),
				 $"C4. **O ALVO ALEM DA TELA (18 tiles)**: o zoom para no piso do jogador (2x -- o lock-on nao ve mais longe que o menu deixa) e a camera "
				 + $"se inclina pro alvo SEM o jogador sair da tela [zoom {mundo.ZoomDaCameraDeTeste}; desloc {mundo.DeslocamentoDaCameraDeTeste}, o meio {meio}]");
		Fotografar("camera-4-alvo-alem-da-tela-o-jogador-fica-na-tela");

		// ------------------------------------------------------------ C5. a opcao no menu de pausa
		mundo.TravarNoMaisPerto();   // (a tecla de novo: o Perto, o mais perto a vista)
		await Segundos(1.5);
		pausa.Abrir();
		await Quadros(8);
		CheckBox? caixa = pausa.CaixaDaCameraDoLockOnDeTeste;
		Conferir(cli.AlvoId == perto && caixa != null && caixa.IsVisibleInTree() && caixa.ButtonPressed && caixa.Text.Contains("lock-on"),
				 $"C5. **A OPCAO NO MENU DE PAUSA (Esc)**: a caixa \"{caixa?.Text}\", ligada de fabrica");
		if (caixa != null) await Mostrar(caixa);
		Fotografar("camera-5-a-opcao-no-menu-de-pausa");
		if (caixa != null) caixa.ButtonPressed = false;   // o clique: o `Toggled` grava a preferencia
		await Quadros(4);
		pausa.Fechar();
		await Segundos(1.5);
		Conferir(!Boot.Config.CameraDoLockOn && cli.AlvoId == perto && Perto(mundo.DeslocamentoDaCameraDeTeste, Vector2.Zero) && mundo.ZoomDaCameraDeTeste == cheio,
				 $"C6. **DESLIGADA PELA CAIXA, A CAMERA VOLTA PRO JOGADOR** -- com o alvo ainda travado [preferencia {Boot.Config.CameraDoLockOn}; alvo {cli.AlvoId}; "
				 + $"desloc {mundo.DeslocamentoDaCameraDeTeste}; zoom {mundo.ZoomDaCameraDeTeste}]");
		Fotografar("camera-6-opcao-desligada-a-camera-volta-pra-voce");

		// a volta: a caixa ligada de novo, o alvo solto
		pausa.Abrir();
		await Quadros(4);
		if (caixa != null) caixa.ButtonPressed = true;
		pausa.Fechar();
		cli.SendAlvo(0);
	}

	/// <summary>O DUPLO CLIQUE em cima do corpo (o gesto do mouse que marca o alvo, sem o mouse).</summary>
	private static void Clicar(World mundo, int id)
	{
		if (mundo.CorpoDeTeste(id) is { } c) mundo.DuploCliqueDeTeste(c.GlobalPosition);
	}

	/// <summary>O meio do jogador e do corpo, como deslocamento da camera (o alvo menos eu, pela metade).</summary>
	private static Vector2 Meio(World mundo, int id) =>
		mundo.CorpoDeTeste(id) is { } c && mundo.PosicaoLocalDeTeste is { } eu ? (c.GlobalPosition - eu) / 2f : new Vector2(float.NaN, float.NaN);

	private static bool Perto(Vector2 a, Vector2 b) => a.DistanceTo(b) <= 6f;

	/// <summary>O ponto esta dentro do que a camera mostra agora (o centro da tela, mais ou menos meia tela no zoom de agora)?</summary>
	private bool NaTela(World mundo, Vector2? p)
	{
		if (p is not { } q || mundo.ZoomDaCameraDeTeste <= 0) return false;
		Vector2 meia = GetViewport().GetVisibleRect().Size / mundo.ZoomDaCameraDeTeste / 2f;
		Vector2 d = q - mundo.CentroDaCameraDeTeste;
		return Math.Abs(d.X) <= meia.X && Math.Abs(d.Y) <= meia.Y;
	}

	/// <summary>Rola o menu de pausa ate a caixa aparecer inteira na foto.</summary>
	private async Tarefa Mostrar(Control c)
	{
		for (Node? n = c.GetParent(); n != null; n = n.GetParent())
			if (n is ScrollContainer s) { s.EnsureControlVisible(c); break; }
		await Quadros(6);
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
		World.CameraDoLockOnDesligadaDeTeste = false;
		World.OlhoNaCameraDoLockOnDeTeste = false;
		Boot.Config.CameraDoLockOn = true;
		if (S is { } srv && _obrasAntes >= 0) srv.LimparAFotoDeCorpos(_obrasAntes);
		foreach (string l in _linhas) GD.Print("[dcameratrava] " + l);
		GD.Print(_falhas.Count == 0 ? "[dcameratrava] ===== TUDO OK =====" : $"[dcameratrava] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dcameratrava]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
