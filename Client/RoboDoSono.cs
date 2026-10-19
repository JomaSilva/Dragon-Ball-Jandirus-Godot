using Godot;
using Jandirus.Core.Races;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO SLEEP DO MAJIN (`--diagsono`, 2026-10-18): a skill nova na tela, nos 5 s de relogio de verdade. O dono: *"a skill de regeneraçao
/// mude o nome pra sleep, e o personagem ficaria 5 segundos parados, ao qual ao final ele regeneraria grande parte do seu hp dos membros,
/// mas se for interrompido nesses 5s nada acontece e tem um cooldown de 1 minuto mesmo cancelando"*.
///
/// Com janela e com `--host`, nascendo MAJIN no campo aberto da Terra, com o Sleep no livro (`--skillteste`):
///   0. a LOJA: a busca "Sleep" na aba Learning acha a skill na arvore `Majin Racials` -- foto `sono-0-loja.png` --, e a ficha dela abre --
///      foto `sono-0b-ficha.png`;
///   1. o BOTAO "Sleep" na aba Skills, com a descricao -- foto `sono-1-botao.png`;
///   2. ferido a 30%, aperta o botao: o corpo DEITA NA HORIZONTAL e o "z" sobe; e um Majin ao lado, que olhava pro sul, dorme visto de FORA
///      (o boneco remoto), deitado na horizontal tambem -- foto `sono-2-dormindo.png`;
///   3. os 5 s de relogio de verdade, e cada parte acorda em 90% -- foto `sono-3-refeito.png`;
///   4. ferido de novo, dorme e ANDA no meio: acorda, nada se refaz, e o Sleep espera o minuto -- foto `sono-4-acordou.png`;
///   5. CONTRA-EXEMPLO (`World.SonoIgnoradoDeTeste`): o servidor poe o corpo pra dormir e a tela nao mostra nem o "z" nem o corpo deitado --
///      foto `sono-5-defeito.png`;
///   6. CONTRA-EXEMPLO (`GameServer.SonoNaVerticalDeTeste`, o jogo de antes da foto): o Majin ao lado dorme de cabeca pro norte, deitado na
///      VERTICAL -- em pe na tela -- foto `sono-6-defeito-vertical.png`.
///
///     Godot --path . --host --rede 8463 --campoteste 12 --horateste 0.5 --semfauna --skillteste /datum/skill/majin/Sleep --diagsono
///           --raca Majin --conta bancada_sono --nome Dorminhoco
/// </summary>
public partial class RoboDoSono : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
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

	private LocalPlayer? Eu() => World.Instancia?.CorpoDeTeste(C?.LocalId ?? 0) as LocalPlayer;

	private SonoDesenhado? Zs(int eu) => (World.Instancia?.CorpoDeTeste(eu))?.GetNodeOrNull<SonoDesenhado>(SonoDesenhado.NomeDoNode);

	/// <summary>O desenho (`CharacterVisual`) do corpo deste id na tela -- o meu, ou o boneco remoto de um figurante.</summary>
	private static CharacterVisual? Desenho(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>
	/// DEITADO NA HORIZONTAL: o quadro de nocaute (`ko`, um desenho deitado de cabeca pro leste) a 0 ou a 180 graus. A noventa ele fica em pe
	/// na tela -- o defeito da foto de 2026-10-18.
	/// </summary>
	private static bool NaHorizontal(CharacterVisual? v) =>
		v != null && v.EstadoDeTeste == "ko" && Math.Abs(Mathf.Wrap(v.RotationDegrees, -180f, 180f)) is < 1f or > 179f;

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && M != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu nasceram e o servidor esta neste processo");
		if (!pronto || C is not { } cli || M is not { } menu || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);   // o corpo assenta no campo
		GD.Print("[dsono] ===== O SLEEP DO MAJIN, NA TELA (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		bool temOSono = await Ate(() => cli.SkillsAprendidas.Contains(SonoDoMajin.Path), 10);
		Conferir(temOSono && cli.Atributos.Raca == SonoDoMajin.Raca,
				 $"(premissa) o corpo do robo e um MAJIN com o Sleep no livro (raca {cli.Atributos.Raca})");

		// ---- 0. a loja ----
		menu.Abrir();
		menu.IrPara("Learning");
		await Quadros(3);
		LineEdit busca = menu.BuscaDeTeste;
		busca.Text = "Sleep";
		busca.EmitSignal(LineEdit.SignalName.TextChanged, "Sleep");
		await Segundos(0.5);
		Button? achado = menu.PaginaDeTeste("Learning") is { } pg
			? pg.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text.StartsWith("Sleep", StringComparison.Ordinal)
																																		  && b.Text.Contains("Majin Racials", StringComparison.Ordinal))
			: null;
		Conferir(achado != null && achado.Text.Contains("Majin Racials"),
				 $"0. A LOJA: a busca \"Sleep\" na aba Learning acha a skill na arvore `Majin Racials` (\"{achado?.Text}\")");
		Fotografar("user://sono-0-loja.png");
		if (achado != null)
		{
			achado.EmitSignal(BaseButton.SignalName.Pressed);
			await Segundos(0.6);
			Fotografar("user://sono-0b-ficha.png");
		}
		busca.Text = "";
		busca.EmitSignal(LineEdit.SignalName.TextChanged, "");
		await Quadros(3);

		// ---- 1. o botao ----
		menu.IrPara(Verbos.Skills);
		menu.ForcarRedesenho();
		await Quadros(3);
		Button? botao = BotaoDoSono(menu);
		Conferir(botao != null,
				 "1. O BOTAO \"Sleep\" esta na aba Skills (ele sai sozinho do livro: o verb da skill e tecnica portada)");
		await FotoDaBusca(menu, "Sleep", "user://sono-1-botao.png");

		// ---- 2. dormindo: o meu corpo, e o de um Majin ao lado visto de fora ----
		List<int> vizinhos = srv.PalcoCorpos_Fila(eu, ["Majin"], [(2, 0)]);
		int fig = vizinhos.Count > 0 ? vizinhos[0] : 0;
		await Ate(() => Desenho(fig) != null, 5);
		srv.SonoFerirDeTeste(eu, 0.3);
		await Ate(() => cli.Sheet.HP < 50, 3);
		botao = BotaoDoSono(menu);
		botao?.EmitSignal(BaseButton.SignalName.Pressed);
		ulong t0 = Time.GetTicksMsec();
		menu.Fechar();
		bool deitou = await Ate(() => srv.SonoDeTeste(eu).Dormindo && Eu() is { Dormindo: true } && Zs(eu) is { LetrasDeTeste: > 0 }
																  && NaHorizontal(Desenho(eu)), 3);
		Conferir(deitou, "2. DORMINDO: o botao poe o corpo pra dormir -- o servidor conta, o corpo local DEITA NA HORIZONTAL (o quadro de nocaute a "
								  + $"{Desenho(eu)?.RotationDegrees:0} grau(s)) e o \"z\" sobe sobre ele");
		bool vizinhoDorme = srv.DormirOlhandoDeTeste(fig, Jandirus.Core.World.Facing.South);
		bool deFora = await Ate(() => NaHorizontal(Desenho(fig)) && Zs(fig) is { LetrasDeTeste: > 0 }, 3);
		Conferir(vizinhoDorme && deFora,
							  "...e VISTO DE FORA: o Majin ao lado, que olhava pro sul, dorme deitado na horizontal tambem (o quadro de nocaute a "
							  + $"{Desenho(fig)?.RotationDegrees:0} grau(s)), com o \"z\" -- a cabeca e o bit `Deitado` vao no snapshot");
		await Segundos(0.4);
		Fotografar("user://sono-2-dormindo.png");

		// ---- 3. os 5 s de verdade ----
		bool acordou = await Ate(() => !srv.SonoDeTeste(eu).Dormindo, 9);
		double levou = (Time.GetTicksMsec() - t0) / 1000.0;
		await Segundos(0.6);
		double vida = srv.SonoVidaMinimaDeTeste(eu);
		Conferir(acordou && levou is > 4.5 and < 7 && vida > 0.85 && Eu() is { Dormindo: false } && Zs(eu) == null,
				 $"3. OS 5 S DE RELOGIO DE VERDADE: ele acorda em {levou:0.0} s, com cada parte refeita (a menor em {vida:P0}), de pe e sem o \"z\"");
		Fotografar("user://sono-3-refeito.png");

		// ---- 4. acordado no meio ----
		srv.SonoLivreDeTeste(eu);
		srv.SonoFerirDeTeste(eu, 0.3);
		await Segundos(0.3);
		menu.Abrir();
		menu.IrPara(Verbos.Skills);
		menu.ForcarRedesenho();
		await Quadros(3);
		BotaoDoSono(menu)?.EmitSignal(BaseButton.SignalName.Pressed);
		menu.Fechar();
		bool dormiuDeNovo = await Ate(() => srv.SonoDeTeste(eu).Dormindo, 3);
		await Segundos(1.5);
		Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = true });
		bool perdeu = await Ate(() => !srv.SonoDeTeste(eu).Dormindo, 3);
		Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = false });
		await Segundos(4.5);   // o tempo que o sono levaria pra fechar: nada se refaz
		double vidaDepois = srv.SonoVidaMinimaDeTeste(eu);
		Conferir(dormiuDeNovo && perdeu && vidaDepois < 0.35 && Eu() is { Dormindo: false },
				 $"4. ACORDADO NO MEIO: andar (a tecla D) acorda -- o sono se perde e nada se refaz (a menor parte segue em {vidaDepois:P0}), o \"se for "
				 + "interrompido nesses 5s nada acontece\" do dono");
		Fotografar("user://sono-4-acordou.png");
		menu.Abrir();
		menu.IrPara(Verbos.Skills);
		menu.ForcarRedesenho();
		await Quadros(3);
		BotaoDoSono(menu)?.EmitSignal(BaseButton.SignalName.Pressed);
		menu.Fechar();
		await Segundos(0.6);
		Conferir(!srv.SonoDeTeste(eu).Dormindo, "...e o Sleep apertado de novo ESPERA o minuto -- *\"tem um cooldown de 1 minuto mesmo cancelando\"*");

		// ---- 5. o contra-exemplo ----
		World.SonoIgnoradoDeTeste = true;
		try
		{
			srv.SonoLivreDeTeste(eu);
			bool dorme = srv.DormirDeTeste(eu);
			await Segundos(1.0);
			Conferir(dorme && srv.SonoDeTeste(eu).Dormindo && Zs(eu) == null && Eu() is { Dormindo: false },
					 "CONTRA-EXEMPLO (defeito injetado: a tela ignora o sono): o servidor poe o corpo pra dormir e a tela nao mostra nem o \"z\" nem o "
					 + "corpo deitado -- a regua do passo 2 reprovaria");
			Fotografar("user://sono-5-defeito.png");
		}
		finally { World.SonoIgnoradoDeTeste = false; }

		// ---- 6. o contra-exemplo da cabeca ----
		Jandirus.Server.GameServer.SonoNaVerticalDeTeste = true;
		try
		{
			srv.SonoLivreDeTeste(fig);
			bool dorme = srv.DormirOlhandoDeTeste(fig, Jandirus.Core.World.Facing.South);
			bool deitado = await Ate(() => Desenho(fig) is { EstadoDeTeste: "ko" } v && Math.Abs(Math.Abs(Mathf.Wrap(v.RotationDegrees, -180f, 180f)) - 90f) < 1f, 3);
			await Segundos(0.4);
			Conferir(dorme && deitado && !NaHorizontal(Desenho(fig)),
							  "CONTRA-EXEMPLO (defeito injetado: a cabeca do nocaute de olhar, o jogo de antes da foto): o Majin ao lado dorme de cabeca pro norte, a "
							  + $"{Desenho(fig)?.RotationDegrees:0} graus -- deitado na VERTICAL, em pe na tela -- e a regua do passo 2 reprovaria");
			Fotografar("user://sono-6-defeito-vertical.png");
		}
		finally { Jandirus.Server.GameServer.SonoNaVerticalDeTeste = false; }
		srv.PalcoCorpos_Tirar(vizinhos);
		srv.SonoFerirDeTeste(eu, 1.0);
	}

	/// <summary>O botao do Sleep na aba Skills (o texto e o nome da tecnica portada).</summary>
	private static Button? BotaoDoSono(MenuJogo menu) =>
		menu.PaginaDeTeste(Verbos.Skills) is { } pg
			? pg.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "Sleep")
			: null;

	/// <summary>A foto do painel pela busca do menu (o campo do topo): so o que casa com o termo. A busca volta vazia depois.</summary>
	private async Tarefa FotoDaBusca(MenuJogo menu, string termo, string destino)
	{
		LineEdit busca = menu.BuscaDeTeste;
		busca.Text = termo;
		busca.EmitSignal(LineEdit.SignalName.TextChanged, termo);
		await Segundos(0.4);
		Fotografar(destino);
		busca.Text = "";
		busca.EmitSignal(LineEdit.SignalName.TextChanged, "");
		await Quadros(3);
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
		World.SonoIgnoradoDeTeste = false;
		Jandirus.Server.GameServer.SonoNaVerticalDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dsono] " + l);
		GD.Print(_falhas.Count == 0 ? "[dsono] ===== TUDO OK =====" : $"[dsono] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dsono]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
