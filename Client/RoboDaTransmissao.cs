using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO INSTANT TRANSMISSION (<c>--diagteleporte</c>). Fotografa o botao no cartao do Sense, a linha do chat,
/// o sumico e a chegada, o grupo que medita junto e o agarrado. O homonimo (dois "Gemeo") e o motivo do botao
/// carregar o id do corpo, e nao o nome.
/// </summary>
public partial class RoboDaTransmissao : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 20);
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu)
		{
			Conferir(false, "(montagem) o host entrou, com mundo, servidor e menu");
			Fechar();
			return;
		}
		Conferir(srv.PalcoIT_Montar(cli.LocalId), "(montagem) o palco armou o Sense, a skill e os corpos");
		bool lista = await Ate(() => cli.NivelDaTransmissao >= 3 && cli.Sentidos.Count >= 3, 6);
		Conferir(lista, $"(montagem) a lista do Sense chegou com o nivel do teleporte ({cli.NivelDaTransmissao}, {cli.Sentidos.Count} presencas)");
		await OBotao(menu, srv);
		await AViagem(cli, menu, srv);
		await OGrupo(cli, menu, srv);
		await OAgarrado(cli, menu, srv);
		srv.PalcoIT_Limpar(cli.LocalId);
		Fechar();
	}

	private async Tarefa OBotao(MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		await Aba(menu, "Sense");
		List<Button> botoes = Botoes(menu);
		int a = srv.PalcoIT_Id("GemeoA");
		int b = srv.PalcoIT_Id("GemeoB");
		int alvo = srv.PalcoIT_Id("Alvo");
		Conferir(botoes.Count >= 3 && Tem(botoes, a) && Tem(botoes, b) && Tem(botoes, alvo),
			$"F1. cada presenca no alcance tem o botao Teletransportar, e os dois Gemeo tem ids diferentes ({botoes.Count} botoes)");
		Fotografar("user://it-1-botao.png");
	}

	private async Tarefa AViagem(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		int alvo = srv.PalcoIT_Id("Alvo");
		Button? botao = Botoes(menu).FirstOrDefault(b => Meta(b) == alvo);
		if (botao == null) { Conferir(false, "(montagem) o botao do Alvo esta na aba"); return; }
		botao.EmitSignal(BaseButton.SignalName.Pressed);
		bool chat = await Ate(() => Chat.LinhasDeTeste().Exists(l => l.Texto.Contains("toca a própria testa")), 4);
		Conferir(chat, "F2. o chat avisa que o personagem toca a própria testa e se concentra");
		Fotografar("user://it-2-testa.png");
		Conferir(srv.PalcoIT_Vencer(cli.LocalId), "F3. a concentracao vence pelo tique");
		// (as listras duram 0,3 s e o pacote chega alguns quadros depois: com a maquina cheia, um numero fixo de quadros
		// passa delas. Olhar a cada quadro.)
		bool listras = await Ate(() => Piscando(cli.LocalId) || Piscando(alvo), 1.5);
		Conferir(listras, "F3b. o corpo pisca com o efeito de desviar um golpe");
		menu.Fechar();
		await Quadros(8);
		float dist = srv.PalcoIT_Distancia(cli.LocalId, "Alvo");
		Conferir(dist >= 0 && dist <= 2f * Jandirus.Core.World.ZoneCollision.TileSize,
			$"F3c. chegou ao lado do Alvo ({dist:0} px)");
		Fotografar("user://it-3-chegada.png");
	}

	private async Tarefa OGrupo(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		srv.PalcoIT_CenaMeditando(cli.LocalId);
		await Ate(() => cli.Sentidos.Exists(p => p.Corpo == srv.PalcoIT_Id("Alvo")), 4);
		await Aba(menu, "Sense");
		Button? botao = Botoes(menu).FirstOrDefault(b => Meta(b) == srv.PalcoIT_Id("Alvo"));
		botao?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(4);
		srv.PalcoIT_Vencer(cli.LocalId);
		await Quadros(8);
		float junto = srv.PalcoIT_Distancia(cli.LocalId, "Junto");
		float dePe = srv.PalcoIT_Distancia(cli.LocalId, "DePe");
		float tile = Jandirus.Core.World.ZoneCollision.TileSize;
		Conferir(junto >= 0 && junto <= 3f * tile && dePe > 4f * tile,
			$"F4. quem meditava a 1 tile veio ({junto:0} px); quem estava de pe ficou ({dePe:0} px)");
		menu.Fechar();
		await Quadros(4);
		Fotografar("user://it-4-meditando.png");
	}

	private async Tarefa OAgarrado(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		srv.PalcoIT_CenaAgarrao(cli.LocalId);
		await Ate(() => cli.Sentidos.Exists(p => p.Corpo == srv.PalcoIT_Id("Alvo")), 4);
		await Aba(menu, "Sense");
		Button? botao = Botoes(menu).FirstOrDefault(b => Meta(b) == srv.PalcoIT_Id("Alvo"));
		botao?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(4);
		srv.PalcoIT_Vencer(cli.LocalId);
		await Quadros(8);
		float junto = srv.PalcoIT_Distancia(cli.LocalId, "Junto");
		float tile = Jandirus.Core.World.ZoneCollision.TileSize;
		Conferir(junto >= 0 && junto <= 3f * tile, $"F5. o agarrado chegou junto ({junto:0} px)");
		menu.Fechar();
		await Quadros(4);
		Fotografar("user://it-5-agarrado.png");
	}

	// O texto LITERAL do botao (o verbo que o dono pediu em 2026-10-21), e nao uma constante do menu: com o nome velho na tela,
	// a busca nao acha botao nenhum e o F1 reprova.
	private static List<Button> Botoes(MenuJogo menu) =>
		menu.PaginaDeTeste("Sense") is { } pg
			? [.. Todos(pg).OfType<Button>().Where(b => !b.IsQueuedForDeletion() && b.Text == "Teletransportar")]
			: [];

	private static bool Tem(List<Button> botoes, int id) => botoes.Exists(b => Meta(b) == id);

	private static bool Piscando(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<EsquivaZanzoken>(EsquivaZanzoken.NomeDoNode) != null;

	private static int Meta(Button b) => b.HasMeta("teletransporte") ? b.GetMeta("teletransporte").AsInt32() : 0;

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(8);
	}

	private void Conferir(bool ok, string oque)
	{
		if (ok) { _ok++; _linhas.Add("  OK    " + oque); return; }
		_falhas.Add(oque);
		_linhas.Add("  FALHA " + oque);
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
		foreach (string l in _linhas) GD.Print("[diagteleporte] " + l);
		GD.Print($"[diagteleporte] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0
			? "[diagteleporte] ===== TUDO OK ====="
			: $"[diagteleporte] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagteleporte]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
