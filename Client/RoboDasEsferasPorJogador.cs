using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DAS ESFERAS POR JOGADOR (`--diagesferaslocais`, 2026-10-31, o pedido 195) -- as fotos do botao do admin. Com janela e `--host` (quem
/// hospeda entra como admin), um set de jogador na Terra (o palco da janela de desejos), e o caminho do dedo:
///   E1. o menu P na aba Admin, o cartao "Esferas do Dragão" na tela -- foto `esferas-1-o-cartao-do-admin`;
///   E2. "Por jogador": o servidor liga o modo, o mundo le o anuncio, e o host ganha a copia dele do set -- foto `esferas-2-ligado`;
///   E3. o `db_ver` diz que as sete sao so dele -- foto `esferas-3-as-sete-sao-suas`;
///   E4. "Um set no servidor": o modo desliga, as copias somem e o mundo le -- foto `esferas-4-desligado`.
///
///     Godot --path . --host --rede 8487 --campoteste 8 --horateste 0.5 --semfauna --diagesferaslocais --raca Namekian --conta bancada_esflocais --nome Guardiao
/// </summary>
public partial class RoboDasEsferasPorJogador : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _falas = [];

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

	private void AoFalar(Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);

	private bool Ouviu(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.Ordinal));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(3));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null
									  && C.Atributos.Tem(Protocol.Poder.Admin), 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu nasceram, o servidor esta neste processo -- e quem hospeda e ADMIN");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		GD.Print("[desferaslocais] ===== AS ESFERAS DO DRAGAO POR JOGADOR (dono, 2026-10-31, o pedido 195) =====");
		int eu = cli.LocalId;
		cli.Falou += AoFalar;
		srv.EmbateDeFoto_CeuLimpo(eu);
		srv.PalcoDosDesejosDeTeste(eu);   // um set de jogador na Terra, acordado
		await Segundos(1.5);

		// ------------------------------------------------------------ E1. o cartao na aba Admin
		menu.Abrir();
		await Quadros(3);
		Button? aba = Todos(menu).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "Admin");
		aba?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(8);
		Button? porJogador = Todos(menu).OfType<Button>().FirstOrDefault(b => b.Text == "Por jogador");
		Button? doServidor = Todos(menu).OfType<Button>().FirstOrDefault(b => b.Text == "Um set no servidor");
		if (porJogador != null) await Mostrar(porJogador);
		Conferir(aba != null && porJogador != null && doServidor != null && porJogador.IsVisibleInTree(),
				 "E1. **O CARTAO \"Esferas do Dragão\" NA ABA ADMIN**: os botoes \"Por jogador\" e \"Um set no servidor\"");
		Fotografar("esferas-1-o-cartao-do-admin");

		// ------------------------------------------------------------ E2. ligado
		_falas.Clear();
		porJogador?.EmitSignal(BaseButton.SignalName.Pressed);
		bool ligou = await Ate(() => srv.EsferasPorJogadorDeTeste.Ligado && Ouviu("agora são POR JOGADOR"), 8);
		bool copiou = await Ate(() => srv.EsferasPorJogadorDeTeste.Copias >= 1, 5);
		menu.Fechar();
		await Segundos(1.0);
		Conferir(ligou && copiou && Ouviu("ESFERAS POR JOGADOR: LIGADO"),
				 $"E2. **\"Por jogador\" LIGA O MODO**: o mundo le o anuncio, o admin le o modo, e o host ganha a copia dele [{srv.EsferasPorJogadorDeTeste}; {Ultimas()}]");
		Fotografar("esferas-2-ligado");

		// ------------------------------------------------------------ E3. o db_ver
		_falas.Clear();
		cli.SendVerbo("db_ver");
		bool disse = await Ate(() => Ouviu("estas sete são só suas"), 5);
		await Segundos(0.6);
		Conferir(disse, $"E3. **O `db_ver` DIZ QUE AS SETE SAO SO DELE** [{Ultimas()}]");
		Fotografar("esferas-3-as-sete-sao-suas");

		// ------------------------------------------------------------ E4. desligado
		_falas.Clear();
		menu.Abrir();
		await Quadros(6);
		Todos(menu).OfType<Button>().FirstOrDefault(b => b.Text == "Admin")?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(8);
		Todos(menu).OfType<Button>().FirstOrDefault(b => b.Text == "Um set no servidor")?.EmitSignal(BaseButton.SignalName.Pressed);
		bool desligou = await Ate(() => !srv.EsferasPorJogadorDeTeste.Ligado && srv.EsferasPorJogadorDeTeste.Copias == 0
										&& Ouviu("voltaram a ser UM SET DE CADA"), 8);
		menu.Fechar();
		await Segundos(1.0);
		Conferir(desligou, $"E4. **\"Um set no servidor\" DESLIGA**: as copias somem e o mundo le [{srv.EsferasPorJogadorDeTeste}; {Ultimas()}]");
		Fotografar("esferas-4-desligado");
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	/// <summary>Rola o menu ate o controle aparecer inteiro na foto.</summary>
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
		if (C is { } cli) cli.Falou -= AoFalar;
		foreach (string l in _linhas) GD.Print("[desferaslocais] " + l);
		GD.Print(_falhas.Count == 0 ? "[desferaslocais] ===== TUDO OK =====" : $"[desferaslocais] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[desferaslocais]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
