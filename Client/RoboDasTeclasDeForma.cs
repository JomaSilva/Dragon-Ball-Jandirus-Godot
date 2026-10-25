using Godot;
using Catalogo = Jandirus.Core.Forms.Catalogo;
using EstadoDeForma = Jandirus.Core.Forms.EstadoDeForma;
using Servidor = Jandirus.Server.GameServer;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO C DO KI DIVINO E DA TECLA QUE VAI DIRETO (`--diagformasteclas`, 2026-10-23), NA TELA. O dono: *"atualmente quem libera god ki
/// nao consegue virar as formas mortais, so as divinas, coloque q as transformacoes divinas podem ser colocadas em teclas assim indo direto
/// pra elas, ou tendo um botao q troca entre o c duas vezes virar as formas mortais e o c duas vezes virar forma divina, outra coisa formas
/// com acima de 50% de maestria q estao colocadas em teclas, podem sim pular etapas anteriores"*. Com janela e `--host`, um Saiyajin; o
/// palco (`GameServer.FormasTeclasPalco.cs`) monta o corpo da bancada de servidor (classe Normal, ki divino a 40%, as formas ja assumidas)
/// e quem transforma e o que o robo APERTA -- o botao da aba Other, o C duplo e a tecla de forma, pelo teclado de verdade -- com o servidor
/// conferido a cada passo e uma FOTO de cada um:
///
///   R1. O BOTAO "Toggle C: Mortal/Divine Forms" so existe pra quem despertou o ki divino -- foto `fteclas-1-botao` (a aba Other).
///   R2. AS DUAS POSICOES: o clique troca pras MORTAIS e o servidor diz no chat (`fteclas-2-mortais`); o segundo, pras DIVINAS
///       (`fteclas-3-divinas`).
///   R3. O C DUPLO, das mortais primeiro, vai da base pro SUPER SAIYAJIN com o ki divino desperto -- `fteclas-4-c-duplo-ssj`. CONTRA-EXEMPLO
///       `EstadoDeForma.CSurdoAFamiliaDeTeste`: o jogo de antes, o mesmo C vai pro SSG -- `fteclas-4b-antes-ssg`.
///   R4. A TECLA DO BLUE sai da BASE direto pro Blue -- `fteclas-5-tecla-blue`. CONTRA-EXEMPLO `EstadoDeForma.DivinaPedeOCaminhoDeTeste`.
///   R5. A TECLA DO SSJ3 com 50% de maestria nele sai da BASE direto pro SSJ3 -- `fteclas-6-tecla-ssj3`; com 49% fica na base, e o chat diz
///       que ele vem depois do 2 e quanto falta -- `fteclas-7-ssj3-49`. CONTRA-EXEMPLO `EstadoDeForma.MaestriaNaoPulaDeTeste`.
///
///     Godot --janela --resolution 1280x720 --host --rede 10881 --campoteste 8 --horateste 0.5 --semfauna --diagformasteclas --raca Saiyan --conta bancada_dfteclas --nome Tecla
/// </summary>
public partial class RoboDasTeclasDeForma : Node
{
	private const string NomeDoBotao = "Toggle C: Mortal/Divine Forms";

	/// <summary>Duas letras livres (H, J, L, O, U, Y, Z sao as que o jogo nao usa), uma por tecla de forma.</summary>
	private const Key TeclaDoBlue = Key.J, TeclaDoSsj3 = Key.L;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? "   " + detalhe : ""));
		if (passou) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos() =>
		EstadoDeForma.CSurdoAFamiliaDeTeste = EstadoDeForma.DivinaPedeOCaminhoDeTeste = EstadoDeForma.MaestriaNaoPulaDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Checa("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => GameClient.Instance is { Connected: true, LocalId: > 0 } && Servidor.Instance != null
									  && MenuJogo.Instancia != null && World.Instancia != null && Chat.Instancia != null, 90);
		Checa("(montagem) em 90 s o cliente conectou, o servidor e deste processo e o menu P e o chat nasceram", pronto);
		if (!pronto || GameClient.Instance is not { } cli || Servidor.Instance is not { } srv || MenuJogo.Instancia is not { } menu) return;
		await Segundos(1.5);
		GD.Print("[dfteclas] ===== O C DO KI DIVINO E A TECLA QUE VAI DIRETO, NA TELA (dono, 2026-10-23) =====");

		int id = cli.LocalId;
		bool montado = srv.PalcoFT_Montar(id)
					   && await Ate(() => cli.KiDivino.Desperto && cli.Atributos.Classe == "Normal" && FormasDespertas.Sei("ssj1")
										 && FormasDespertas.Sei(Catalogo.IdBlue) && NoCliente(cli) == Catalogo.IdBase, 8);
		Checa("(montagem) Saiyajin Normal com o ki divino desperto, que ja assumiu o Super Saiyajin e o Blue, na base: o servidor e o "
			  + "cliente concordam", montado, $"[forma {NoCliente(cli)}, classe {cli.Atributos.Classe}, ki divino {cli.KiDivino.Desperto}]");
		if (!montado) return;
		try
		{
			await R1_OBotao(cli, srv, menu);
			await R2_AsDuasPosicoes(cli, srv, menu);
			await R3_OCDuplo(cli, srv, menu);
			await R4_ATeclaDoBlue(cli, srv);
			await R5_ATeclaDoSsj3(cli, srv);
		}
		finally
		{
			DesligarOsDefeitos();
			menu.Fechar();
			Teclas.Desligar("forma", Catalogo.IdBlue);
			Teclas.Desligar("forma", "ssj3");
			srv.PalcoFT_ABase(id);
		}
	}

	// =====================================================================
	// R1) O BOTAO SO EXISTE PRA QUEM TEM KI DIVINO
	// =====================================================================
	private async Tarefa R1_OBotao(GameClient cli, Servidor srv, MenuJogo menu)
	{
		srv.PalcoFT_GodKi(cli.LocalId, false);
		bool apagou = await Ate(() => !cli.KiDivino.Desperto && !BotaoNaLista(), 8);
		await AbrirOutros(menu);
		Checa("R1a. sem o ki divino o botao do C NAO esta na aba Other (ele so serve a quem tem forma divina a alcancar)",
			  apagou && BotaoDoC(menu) == null, $"[ki divino {cli.KiDivino.Desperto}, na lista {BotaoNaLista()}]");

		srv.PalcoFT_GodKi(cli.LocalId, true);
		bool voltou = await Ate(() => cli.KiDivino.Desperto && BotaoNaLista(), 8);
		await AbrirOutros(menu);
		Button? b = BotaoDoC(menu);
		Checa($"R1b. **com o ki divino desperto o botao \"{NomeDoBotao}\" aparece na aba Other**, aceso, com a frase do que faz embaixo",
			  voltou && b is { Disabled: false } && RotuloQueComeca(menu, "Troca o C duplo") != null,
			  $"[na lista {BotaoNaLista()}, botao {(b == null ? "nao achei" : $"Disabled={b.Disabled}")}]");
		string? cartao = b == null ? null : CartaoDe(b);
		Checa("R1c. ...no cartao \"Combate\" da aba Other, junto dos graus do Super Saiyajin (o irmao dele)", cartao == "Combate",
			  $"[cartao {cartao ?? "nenhum"}]");
		await Fotografar("fteclas-1-botao", b);
	}

	/// <summary>O tema do cartao da aba Other em que este botao mora -- o metadado "grupo" que o `MenuJogo.Outros` poe no cartao.</summary>
	private static string? CartaoDe(Node n)
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p.HasMeta("grupo")) return p.GetMeta("grupo").AsString();
		return null;
	}

	// =====================================================================
	// R2) AS DUAS POSICOES, DITAS PELO SERVIDOR NO CHAT
	// =====================================================================
	private async Tarefa R2_AsDuasPosicoes(GameClient cli, Servidor srv, MenuJogo menu)
	{
		int id = cli.LocalId;
		(bool mortais, string disse) = await Clicar(menu, () => srv.FormaDeTeste(id)?.CPelasDivinas == false, "MORTAIS");
		Checa("R2a. **o clique troca o C duplo pras formas MORTAIS** -- o servidor guarda e diz no chat", mortais, $"[{disse}]");
		await Fotografar("fteclas-2-mortais", BotaoDoC(menu));

		(bool divinas, disse) = await Clicar(menu, () => srv.FormaDeTeste(id)?.CPelasDivinas == true, "DIVINAS");
		Checa("R2b. **o segundo clique volta pras formas DIVINAS**, e diz tambem", divinas, $"[{disse}]");
		await Fotografar("fteclas-3-divinas", BotaoDoC(menu));

		// as mortais de novo: e o lado que o R3 mede
		(bool deNovo, disse) = await Clicar(menu, () => srv.FormaDeTeste(id)?.CPelasDivinas == false, "MORTAIS");
		Checa("(montagem) o terceiro clique deixa as mortais primeiro pro C duplo", deNovo, $"[{disse}]");
		menu.Fechar();
		await Quadros(3);
	}

	// =====================================================================
	// R3) O C DUPLO VAI PRO SUPER SAIYAJIN COM O KI DIVINO DESPERTO
	// =====================================================================
	private async Tarefa R3_OCDuplo(GameClient cli, Servidor srv, MenuJogo menu)
	{
		int id = cli.LocalId;
		await ABase(cli, srv);
		await DuploC();
		bool ssj = await Ate(() => NoServidor(srv, id) == "ssj1" && NoCliente(cli) == "ssj1", 6);
		Checa("R3a. **as mortais primeiro: o C duplo (o teclado de verdade), da base, vai pro Super Saiyajin** -- com o ki divino desperto",
			  ssj, $"[servidor {NoServidor(srv, id)}, cliente {NoCliente(cli)}]");
		await Segundos(2.0);   // o brilho da transformacao instantanea some antes da foto
		await Fotografar("fteclas-4-c-duplo-ssj", null);

		EstadoDeForma.CSurdoAFamiliaDeTeste = true;
		try
		{
			await ABase(cli, srv);
			await DuploC();
			bool ssg = await Ate(() => NoServidor(srv, id) == "ssg" && NoCliente(cli) == "ssg", 6);
			Checa("(defeito injetado: o C surdo a familia -- o jogo de antes) a mesma regua da R3a REPROVA: o C duplo vai pro Super Saiyajin God",
				  ssg, $"[servidor {NoServidor(srv, id)}, cliente {NoCliente(cli)}]");
			await Segundos(2.0);
			await Fotografar("fteclas-4b-antes-ssg", null);
		}
		finally { EstadoDeForma.CSurdoAFamiliaDeTeste = false; }
	}

	// =====================================================================
	// R4) A TECLA DO BLUE SAI DA BASE
	// =====================================================================
	private async Tarefa R4_ATeclaDoBlue(GameClient cli, Servidor srv)
	{
		int id = cli.LocalId;
		bool ligou = Teclas.Ligar(TeclaDoBlue, new Atalho("forma", Catalogo.IdBlue, "Super Saiyajin Blue"));
		await ABase(cli, srv);
		await Toque(TeclaDoBlue);
		bool blue = await Ate(() => NoServidor(srv, id) == Catalogo.IdBlue && NoCliente(cli) == Catalogo.IdBlue, 6);
		Checa("R4a. **a tecla do Blue, da BASE, vai direto pro Blue** (a forma divina ja usada, sem o caminho do Super Saiyajin)",
			  ligou && blue, $"[ligou {ligou}, servidor {NoServidor(srv, id)}, cliente {NoCliente(cli)}]");
		await Segundos(2.0);
		await Fotografar("fteclas-5-tecla-blue", null);

		EstadoDeForma.DivinaPedeOCaminhoDeTeste = true;
		try
		{
			await ABase(cli, srv);
			await Toque(TeclaDoBlue);
			await Segundos(1.2);
			Checa("(defeito injetado: a forma divina pede o caminho -- o jogo de antes) a mesma regua da R4a REPROVA: a tecla do Blue, da "
				  + "base, nao transforma", NoServidor(srv, id) == Catalogo.IdBase, $"[servidor {NoServidor(srv, id)}]");
		}
		finally { EstadoDeForma.DivinaPedeOCaminhoDeTeste = false; }
	}

	// =====================================================================
	// R5) A TECLA DO SSJ3 COM 50% SAI DA BASE; COM 49% NAO
	// =====================================================================
	private async Tarefa R5_ATeclaDoSsj3(GameClient cli, Servidor srv)
	{
		int id = cli.LocalId;
		double pula = EstadoDeForma.MaestriaQuePulaDegrau;
		srv.PalcoFT_Maestria(id, "ssj3", pula);
		bool sabe = await Ate(() => FormasDespertas.Sei("ssj3"), 6);
		bool ligou = Teclas.Ligar(TeclaDoSsj3, new Atalho("forma", "ssj3", "Super Saiyajin 3"));
		Checa($"(montagem) com o SSJ3 a {pula:0}% o cliente o conhece (a tela de teclas o ofereceria) e a tecla foi ligada", sabe && ligou);

		await ABase(cli, srv);
		await Toque(TeclaDoSsj3);
		bool ssj3 = await Ate(() => NoServidor(srv, id) == "ssj3" && NoCliente(cli) == "ssj3", 6);
		Checa($"R5a. **a tecla do SSJ3 com {pula:0}% de maestria nele sai da BASE direto pro SSJ3** (o exemplo do dono)",
			  ssj3, $"[servidor {NoServidor(srv, id)}, cliente {NoCliente(cli)}]");
		await Segundos(2.0);
		await Fotografar("fteclas-6-tecla-ssj3", null);

		srv.PalcoFT_Maestria(id, "ssj3", pula - 1);
		await ABase(cli, srv);
		int marca = TextoDoChat().Length;
		await Toque(TeclaDoSsj3);
		bool disse = await Ate(() => NovoNoChat(marca).Contains("vem depois", StringComparison.OrdinalIgnoreCase), 4);
		string frase = PrimeiraCom(NovoNoChat(marca), "vem depois");
		Checa($"R5b. com {pula - 1:0}% a tecla do SSJ3 NAO sai da base -- e o chat diz que ele vem depois do 2 e quanto falta "
			  + $"({pula:0}%, e o corpo tem {pula - 1:0}%)",
			  disse && NoServidor(srv, id) == Catalogo.IdBase && frase.Contains($"{pula:0}%") && frase.Contains($"{pula - 1:0}%"),
			  $"[servidor {NoServidor(srv, id)} | \"{frase}\"]");
		await Fotografar("fteclas-7-ssj3-49", null);

		srv.PalcoFT_Maestria(id, "ssj3", pula);
		EstadoDeForma.MaestriaNaoPulaDeTeste = true;
		try
		{
			await ABase(cli, srv);
			await Toque(TeclaDoSsj3);
			await Segundos(1.2);
			Checa("(defeito injetado: a maestria nao pula -- o jogo de antes) a mesma regua da R5a REPROVA: com 50%, a tecla do SSJ3 da base "
				  + "nao transforma", NoServidor(srv, id) == Catalogo.IdBase, $"[servidor {NoServidor(srv, id)}]");
		}
		finally { EstadoDeForma.MaestriaNaoPulaDeTeste = false; }
	}

	// =====================================================================
	// OS GESTOS
	// =====================================================================
	/// <summary>O CLIQUE NO BOTAO DE VERDADE (o sinal `pressed` do `Button` do menu), e o que o servidor guardou e disse.</summary>
	private async System.Threading.Tasks.Task<(bool Foi, string Disse)> Clicar(MenuJogo menu, Func<bool> guardou, string termo)
	{
		await AbrirOutros(menu);
		Button? b = BotaoDoC(menu);
		if (b == null) return (false, "nao achei o botao");
		int marca = TextoDoChat().Length;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		bool foi = await Ate(() => guardou() && NovoNoChat(marca).Contains(termo, StringComparison.Ordinal), 5);
		await Quadros(2);
		return (foi, PrimeiraCom(NovoNoChat(marca), termo));
	}

	/// <summary>O C DUPLO pelo teclado: dois toques dentro da janela do duplo (`LocalPlayer.JanelaDoDuplo`, 1 s).</summary>
	private async Tarefa DuploC()
	{
		await Toque(Key.C);
		await Segundos(0.15);
		await Toque(Key.C);
		await Segundos(0.3);
	}

	private async Tarefa ABase(GameClient cli, Servidor srv)
	{
		srv.PalcoFT_ABase(cli.LocalId);
		await Ate(() => NoCliente(cli) == Catalogo.IdBase, 5);
		await Segundos(0.4);
	}

	/// <summary>TECLADO DE MENTIRA, eventos de verdade: as duas metades preenchidas, como na `--diagtecla` (`RoboDeTecla.Tecla`).</summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = apertada });

	/// <summary>
	/// UM TOQUE, com quadros no meio (o molde do `RoboDoMutante`): o `LocalPlayer` le o C por `IsActionJustPressed` no `_Process`, e apertar e
	/// soltar no mesmo quadro poderia nem chegar a ser visto.
	/// </summary>
	private async Tarefa Toque(Key k)
	{
		Tecla(k, true);
		await Quadros(3);
		Tecla(k, false);
		await Quadros(2);
	}

	// =====================================================================
	// LER A TELA E O SERVIDOR
	// =====================================================================
	private static string NoCliente(GameClient cli) => Catalogo.PorRede(cli.Atributos.FormaAtual)?.Id ?? "?";

	private static string NoServidor(Servidor srv, int id) => srv.FormaDeTeste(id)?.Atual ?? "?";

	private static bool BotaoNaLista() => Verbos.Da(Verbos.Outros).Any(v => v.Nome == NomeDoBotao);

	private async Tarefa AbrirOutros(MenuJogo menu)
	{
		menu.Abrir();
		menu.IrPara(Verbos.Outros);
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
	}

	private static Button? BotaoDoC(MenuJogo menu) =>
		menu.PaginaDeTeste(Verbos.Outros) is { } pg
			? Todos(pg).OfType<Button>().FirstOrDefault(b => b.Text == NomeDoBotao && b.IsVisibleInTree())
			: null;

	private static Label? RotuloQueComeca(MenuJogo menu, string comeco) =>
		menu.PaginaDeTeste(Verbos.Outros) is { } pg
			? Todos(pg).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith(comeco, StringComparison.Ordinal) && l.IsVisibleInTree())
			: null;

	private static string TextoDoChat() =>
		Chat.Instancia is { } c && Todos(c).OfType<RichTextLabel>().FirstOrDefault() is { } r ? r.GetParsedText() : "";

	private static string NovoNoChat(int marca)
	{
		string tudo = TextoDoChat();
		return tudo.Length > marca ? tudo[marca..] : "";
	}

	private static string PrimeiraCom(string texto, string termo) =>
		texto.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Contains(termo, StringComparison.OrdinalIgnoreCase)) ?? "(nada no chat)";

	/// <summary>A arvore inteira, SEM os nos que ja vao embora (a pagina velha fica na arvore ate o fim do quadro da remontagem).</summary>
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f.IsQueuedForDeletion()) continue;
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	// =====================================================================
	// A FOTO E O RELOGIO
	// =====================================================================
	/// <summary>A foto da tela inteira; com um controle, rolado ate ele ficar a vista.</summary>
	private async Tarefa Fotografar(string nome, Control? aVista)
	{
		if (aVista != null && Ancestral<ScrollContainer>(aVista) is { } rol) rol.EnsureControlVisible(aVista);
		await Quadros(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto `{nome}` (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
	}

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	private static double Agora => Time.GetTicksMsec() / 1000.0;

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Agora + s;
		while (Agora < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora + segundos;
		while (Agora < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		DesligarOsDefeitos();
		foreach (string l in _linhas) GD.Print("[dfteclas] " + l);
		GD.Print($"[dfteclas] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dfteclas] ===== TUDO OK =====" : $"[dfteclas] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dfteclas]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
