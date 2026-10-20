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
///   2. ferido a 30%, aperta o botao: o corpo DORME EM PE (dono, 2026-10-19: *"ele ta deitando pra dormir na skill de regeneraçao dele, ele
///      deveria dormir em pé"*) e o "z" sobe; e um Majin ao lado, que olhava pro sul, dorme visto de FORA (o boneco remoto), em pe tambem --
///      foto `sono-2-dormindo.png`;
///   3. os 5 s de relogio de verdade, e cada parte acorda em 90% -- foto `sono-3-refeito.png`;
///   4. ferido de novo, dorme e ANDA no meio: acorda, nada se refaz, e o Sleep espera o minuto -- foto `sono-4-acordou.png`;
///   5. CONTRA-EXEMPLO (`World.SonoIgnoradoDeTeste`): o servidor poe o corpo pra dormir e a tela nao mostra o "z" -- foto `sono-5-defeito.png`;
///   6. CONTRA-EXEMPLO (`GameServer.SonoDeitadoDeTeste`, o jogo da 0.4.1): o Majin ao lado dorme DEITADO no quadro de nocaute -- foto
///      `sono-6-defeito-deitado.png`;
///   7. (2026-10-19) O KI ACIMA DE 100%: a 120% na barra, o botao o poe pra dormir e a barra cai pra 100% -- fotos `sono-7a-ki-acima.png` e
///      `sono-7b-ki-dormindo.png` (*"ao dormir caso o ki dele esteja acima de 100% ele volta pra 100%"*).
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

	private SonoDesenhado? Zs(int eu) => (World.Instancia?.CorpoDeTeste(eu))?.GetNodeOrNull<SonoDesenhado>(SonoDesenhado.NomeDoNode);

	/// <summary>O desenho (`CharacterVisual`) do corpo deste id na tela -- o meu, ou o boneco remoto de um figurante.</summary>
	private static CharacterVisual? Desenho(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>
	/// (2026-10-19) DE PE: o quadro de quem esta parado (`default`), sem giro -- e nao o de nocaute (`ko`, o desenho deitado que a 0.4.1 vestia
	/// em quem dormia).
	/// </summary>
	private static bool DePe(CharacterVisual? v) =>
		v != null && v.EstadoDeTeste == "default" && Math.Abs(Mathf.Wrap(v.RotationDegrees, -180f, 180f)) < 1f;

	/// <summary>Como o desenho deste corpo esta agora, pro rodape das linhas: o quadro e o giro.</summary>
	private static string Como(CharacterVisual? v) => v == null ? "sem desenho" : $"quadro {v.EstadoDeTeste}, {v.RotationDegrees:0} grau(s)";

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
		bool dormindo = await Ate(() => srv.SonoDeTeste(eu).Dormindo && Zs(eu) is { LetrasDeTeste: > 0 } && DePe(Desenho(eu)), 3);
		Conferir(dormindo, "2. DORMINDO EM PE: o botao poe o corpo pra dormir -- o servidor conta, o corpo local fica DE PE (o quadro de quem esta "
								  + $"parado, sem giro: {Como(Desenho(eu))}) e o \"z\" sobe sobre ele");
		bool vizinhoDorme = srv.DormirOlhandoDeTeste(fig, Jandirus.Core.World.Facing.South);
		bool deFora = await Ate(() => srv.SonoDeTeste(fig).Dormindo && DePe(Desenho(fig)) && Zs(fig) is { LetrasDeTeste: > 0 }, 3);
		Conferir(vizinhoDorme && deFora,
							  "...e VISTO DE FORA: o Majin ao lado, que olhava pro sul, dorme em pe tambem, com o \"z\" -- a pose vai no snapshot "
							  + $"({Como(Desenho(fig))})");
		await Segundos(0.4);
		Fotografar("user://sono-2-dormindo.png");

		// ---- 3. os 5 s de verdade ----
		bool acordou = await Ate(() => !srv.SonoDeTeste(eu).Dormindo, 9);
		double levou = (Time.GetTicksMsec() - t0) / 1000.0;
		await Segundos(0.6);
		double vida = srv.SonoVidaMinimaDeTeste(eu);
		Conferir(acordou && levou is > 4.5 and < 7 && vida > 0.85 && DePe(Desenho(eu)) && Zs(eu) == null,
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
		Conferir(dormiuDeNovo && perdeu && vidaDepois < 0.35 && Zs(eu) == null,
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
			Conferir(dorme && srv.SonoDeTeste(eu).Dormindo && Zs(eu) == null,
					 "CONTRA-EXEMPLO (defeito injetado: a tela ignora o sono): o servidor poe o corpo pra dormir e a tela nao mostra o \"z\" -- a regua "
					 + "do passo 2 reprovaria");
			Fotografar("user://sono-5-defeito.png");
		}
		finally { World.SonoIgnoradoDeTeste = false; }

		// ---- 6. o contra-exemplo do corpo deitado ----
		Jandirus.Server.GameServer.SonoDeitadoDeTeste = true;
		try
		{
			await Ate(() => !srv.SonoDeTeste(fig).Dormindo, 7);   // o sono do passo 2 ja fechou sozinho; a espera e so a rede
			srv.SonoLivreDeTeste(fig);
			bool dorme = srv.DormirOlhandoDeTeste(fig, Jandirus.Core.World.Facing.South);
			bool deitado = await Ate(() => srv.SonoDeTeste(fig).Dormindo && Desenho(fig) is { EstadoDeTeste: "ko" }, 3);
			await Segundos(0.4);
			Conferir(dorme && deitado && !DePe(Desenho(fig)),
							  "CONTRA-EXEMPLO (defeito injetado: quem dorme deita no quadro de nocaute, o jogo da 0.4.1): o Majin ao lado dorme DEITADO "
							  + $"({Como(Desenho(fig))}) -- e a regua do passo 2 reprovaria");
			Fotografar("user://sono-6-defeito-deitado.png");
		}
		finally { Jandirus.Server.GameServer.SonoDeitadoDeTeste = false; }

		// ---- 7. o Ki acima de 100% ----
		// A 120% e nao mais: acima do teto seguro (`kicapacity`, 130% de fabrica) a sobrecarga fere, e um golpe no corpo acordaria quem dorme.
		await Ate(() => !srv.SonoDeTeste(eu).Dormindo, 7);   // o sono do passo 5 fecha sozinho
		srv.SonoLivreDeTeste(eu);
		srv.SonoKiDeTeste(eu, 1.2);
		bool acima = await Ate(() => cli.Sheet.MaxKi > 0 && cli.Sheet.Ki > cli.Sheet.MaxKi * 1.15, 3);
		double kiAntes = cli.Sheet.MaxKi > 0 ? cli.Sheet.Ki / cli.Sheet.MaxKi : 0;
		Fotografar("user://sono-7a-ki-acima.png");
		menu.Abrir();
		menu.IrPara(Verbos.Skills);
		menu.ForcarRedesenho();
		await Quadros(3);
		BotaoDoSono(menu)?.EmitSignal(BaseButton.SignalName.Pressed);
		menu.Fechar();
		bool caiu = await Ate(() => srv.SonoDeTeste(eu).Dormindo && cli.Sheet.MaxKi > 0 && Math.Abs(cli.Sheet.Ki / cli.Sheet.MaxKi - 1) < 0.005, 3);
		await Segundos(0.4);
		Conferir(acima && caiu,
				 $"7. O KI ACIMA DE 100%: a {kiAntes:P0} na barra, o botao o poe pra dormir e a barra cai pra "
				 + $"{(cli.Sheet.MaxKi > 0 ? cli.Sheet.Ki / cli.Sheet.MaxKi : 0):P0} -- *\"ao dormir caso o ki dele esteja acima de 100% ele volta pra 100%\"*");
		Fotografar("user://sono-7b-ki-dormindo.png");
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
		Jandirus.Server.GameServer.SonoDeitadoDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dsono] " + l);
		GD.Print(_falhas.Count == 0 ? "[dsono] ===== TUDO OK =====" : $"[dsono] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dsono]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
