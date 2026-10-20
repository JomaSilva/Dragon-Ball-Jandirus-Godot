using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS SERVICOS DO OUTRO MUNDO (`--diagservicos`, 2026-10-19) -- as fotos da rede de quests que paga zeni aos mortos (pedido de
/// jogador repassado pelo dono, que aprovou o desenho). A regra e da `--servicosteste`; aqui e a TELA: os doadores desenhados no lugar
/// deles, o menu E com a lista, o cartao da aba World e a entrega.
///
/// Com janela e com `--host`, o palco mata o robo (a alma ja de pe no Outro Mundo) e o leva pra perto de cada doador:
///   S1. ao lado do Oni da recepcao a dica ensina a tecla -- foto `servicos-1-recepcao`;
///   S2. a tecla E abre o menu dele (Pedir servico, Entregar), e o Pedir servico vira a lista dos quatro -- foto `servicos-2-lista`;
///   S3. escolher a entrega ao Sr. Kaioh poe o servico no cartao da aba World -- foto `servicos-3-aba-world`;
///   S4. com a aba World ABERTA e o morto parado, o servico que chega pelo fio remonta o cartao sozinho. CONTRA-EXEMPLO
///       (`OuvidoDoServicoDoAlem.SurdoDeTeste`);
///   S5. a Princesa Serpente no palacio e o Oni do Inferno na sala da passagem -- fotos `servicos-4-princesa`, `servicos-5-inferno`;
///   S6. no Sr. Kaioh o menu tem a conversa E os dois botoes, e Entregar paga -- foto `servicos-6-entregue`;
///   S7. revivido, o cartao some da aba World.
///
///     Godot --path . --host --rede 9412 --horateste 0.5 --semfauna --diagservicos --raca Human --conta bancada_servicos --nome Finado
/// </summary>
public partial class RoboDosServicos : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => OuvidoDoServicoDoAlem.SurdoDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private void Ouvir(Protocol.Fala _, string __, string texto) => _ditas.Add(texto);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, o menu da tecla E e o menu P nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } menuE || MenuJogo.Instancia is not { } menuP) return;
		int eu = cli.LocalId;
		GD.Print("[dservicos] ===== OS SERVICOS DO OUTRO MUNDO (2026-10-19) =====");
		cli.Falou += Ouvir;
		try
		{
			// ---- S1. o Oni da recepcao ----
			Conferir(srv.PalcoSv_Morrer(eu), "(palco) o robo morre, de pe no Outro Mundo, ao lado do Oni da recepcao");
			string dica = "[E] Oni da recepção";
			bool naTela = await Ate(() => menuE.DicaNaTela == dica, 20);
			await Segundos(1.0);
			Conferir(naTela, $"S1. ao lado do Oni da recepcao a dica ensina a tecla [\"{menuE.DicaNaTela}\"]");
			Fotografar("user://servicos-1-recepcao.png");

			// ---- S2. o menu dele, e a lista ----
			Apertar(Key.E);
			bool abriu = await Ate(() => menuE.NaTela, 3);
			await Segundos(0.3);
			List<string> botoes = menuE.BotoesDesenhados();
			Conferir(abriu && menuE.TituloDesenhado == "Oni da recepção"
					 && botoes.Contains(ServicosDoAlem.RotuloPedir) && botoes.Contains(ServicosDoAlem.RotuloEntregar),
					 $"S2. a tecla E abre o menu do Oni com \"{ServicosDoAlem.RotuloPedir}\" e \"{ServicosDoAlem.RotuloEntregar}\" "
					 + $"[\"{menuE.TituloDesenhado}\": {string.Join(" | ", botoes)}]");
			menuE.ApertarDesenhado(ServicosDoAlem.RotuloPedir);
			string[] daRecepcao = [.. ServicosDoAlem.De(DoadorDoAlem.Recepcao).Select(s => s.Nome)];
			bool listou = await Ate(() => daRecepcao.All(n => menuE.BotoesDesenhados().Contains(n)), 5);
			await Segundos(0.3);
			Conferir(listou, $"S2b. \"{ServicosDoAlem.RotuloPedir}\" vira a lista dos quatro servicos dele [{string.Join(" | ", menuE.BotoesDesenhados())}]");
			Fotografar("user://servicos-2-lista.png");

			// ---- S3. a entrega ao Sr. Kaioh, no cartao da aba World ----
			menuE.ApertarDesenhado("Entrega ao Sr. Kaioh");
			bool pegou = await Ate(() => cli.CartaoDoAlem.Titulo == "Entrega ao Sr. Kaioh", 5);
			if (menuE.NaTela) Apertar(Key.Escape);
			await Aba(menuP, "World");
			await Segundos(0.5);
			Conferir(pegou && TemCartaoDoAlem(menuP) && EmCurso(menuP, "Entrega ao Sr. Kaioh"),
					 $"S3. escolher a entrega poe o servico no cartao \"{MenuJogo.TituloDoCartaoDoAlem}\" da aba World [\"{cli.CartaoDoAlem.Titulo}\"]");
			Fotografar("user://servicos-3-aba-world.png");

			// ---- S4. o cartao remonta sozinho, com a aba aberta e o morto parado ----
			cli.SendVerbo(ServicosDoAlem.VerboDesistir);
			await Ate(() => cli.CartaoDoAlem.Titulo.Length == 0, 5);
			await Segundos(0.5);
			srv.PalcoSv_Pegar(eu, "almas_briguentas");
			bool remontou = await Ate(() => EmCurso(menuP, "Almas briguentas"), 4);
			Conferir(remontou, "S4. **O CARTAO REMONTA SOZINHO**: o servico que chega pelo fio aparece na aba World aberta, com o morto parado");
			cli.SendVerbo(ServicosDoAlem.VerboDesistir);
			await Ate(() => cli.CartaoDoAlem.Titulo.Length == 0 && !EmCurso(menuP, "Almas briguentas"), 5);

			OuvidoDoServicoDoAlem.SurdoDeTeste = true;
			bool surdoRemontou;
			try
			{
				srv.PalcoSv_Pegar(eu, "almas_briguentas");
				await Ate(() => cli.CartaoDoAlem.Titulo == "Almas briguentas", 4);
				await Segundos(1.0);
				surdoRemontou = EmCurso(menuP, "Almas briguentas");
			}
			finally { OuvidoDoServicoDoAlem.SurdoDeTeste = false; }
			Conferir(!surdoRemontou, "CONTRA-EXEMPLO (defeito injetado: o ouvido surdo) o cartao novo fica na memoria e nao vira tela -- a regua de S4 REPROVA");
			cli.SendVerbo(ServicosDoAlem.VerboDesistir);
			await Ate(() => cli.CartaoDoAlem.Titulo.Length == 0, 5);
			menuP.Fechar();

			// ---- S5. a Princesa e o Oni do Inferno ----
			srv.PalcoSv_AoLado(eu, DoadorDoAlem.Princesa);
			bool princesa = await Ate(() => menuE.DicaNaTela == "[E] Princesa Serpente", 20);
			await Segundos(1.0);
			Conferir(princesa, $"S5. a Princesa Serpente esta no palacio dela, no Caminho da Serpente [\"{menuE.DicaNaTela}\"]");
			Fotografar("user://servicos-4-princesa.png");
			srv.PalcoSv_AoLado(eu, DoadorDoAlem.OniDoInferno);
			bool inferno = await Ate(() => menuE.DicaNaTela == "[E] Oni do Inferno", 20);
			await Segundos(1.0);
			Conferir(inferno, $"S5b. o Oni do Inferno esta na sala da passagem [\"{menuE.DicaNaTela}\"]");
			Fotografar("user://servicos-5-inferno.png");

			// ---- S6. a entrega no Sr. Kaioh ----
			srv.PalcoSv_AoLado(eu, DoadorDoAlem.Recepcao);
			await Ate(() => menuE.DicaNaTela == dica, 20);
			srv.PalcoSv_Pegar(eu, "entrega_kaioh");
			srv.PalcoSv_AoLado(eu, DoadorDoAlem.SrKaioh);
			bool noKaioh = await Ate(() => menuE.DicaNaTela == "[E] Sr. Kaioh", 20);
			Apertar(Key.E);
			await Ate(() => menuE.NaTela, 3);
			await Segundos(0.3);
			List<string> doKaioh = menuE.BotoesDesenhados();
			Conferir(noKaioh && doKaioh.Contains("Falar com o Sr. Kaioh") && doKaioh.Contains(ServicosDoAlem.RotuloPedir)
					 && doKaioh.Contains(ServicosDoAlem.RotuloEntregar),
					 $"S6. o menu do Sr. Kaioh tem a conversa E os dois botoes do servico [{string.Join(" | ", doKaioh)}]");
			_ditas.Clear();
			menuE.ApertarDesenhado(ServicosDoAlem.RotuloEntregar);
			bool pago = await Ate(() => _ditas.Any(t => t.Contains("+40.000 zeni", StringComparison.Ordinal)), 5)
						&& await Ate(() => cli.CartaoDoAlem.Titulo.Length == 0, 3);
			await Segundos(0.6);
			Conferir(pago, $"S6b. Entregar no Sr. Kaioh paga 40.000 e o servico sai do cartao [ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
			Fotografar("user://servicos-6-entregue.png");
			if (menuE.NaTela) Apertar(Key.Escape);

			// ---- S7. o vivo nao ve o cartao ----
			srv.PalcoSv_Reviver(eu);
			bool sumiu = await Ate(() => !cli.CartaoDoAlem.Mostrar, 5);
			await Aba(menuP, "World");
			await Segundos(0.5);
			Conferir(sumiu && !TemCartaoDoAlem(menuP), "S7. revivido, o cartao dos servicos some da aba World");
			menuP.Fechar();
		}
		finally
		{
			cli.Falou -= Ouvir;
			OuvidoDoServicoDoAlem.SurdoDeTeste = false;
			srv.PalcoSv_Reviver(eu);
		}
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private static Control? PaginaDoMundo(MenuJogo menu) => menu.PaginaDeTeste("World");

	private static bool TemCartaoDoAlem(MenuJogo menu) =>
		PaginaDoMundo(menu) is { } pg && Todos(pg).OfType<PanelContainer>().Any(x => !x.IsQueuedForDeletion() && x.HasMeta("cartao")
																				 && x.GetMeta("titulo").AsString() == MenuJogo.TituloDoCartaoDoAlem);

	/// <summary>A linha do servico na rede do cartao diz "em curso"? (O nome tambem e rotulo da rede: so o VALOR separa o servico em curso.)</summary>
	private static bool EmCurso(MenuJogo menu, string servico) => menu.ValorDesenhado("World", servico) == "em curso";

	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
	}

	private void Apertar(Key k)
	{
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = false });
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
		OuvidoDoServicoDoAlem.SurdoDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dservicos] " + l);
		GD.Print($"[dservicos] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dservicos] ===== TUDO OK =====" : $"[dservicos] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dservicos]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
