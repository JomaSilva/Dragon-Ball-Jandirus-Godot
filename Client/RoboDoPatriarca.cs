using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO GRANDE PATRIARCA DE NAMEK (`--diagpatriarca`, 2026-10-19): o dono -- *"adicione o patriarca em namek assim como tinha no
/// Finale-master"*. Com janela e `--host`, nascendo Humano na Terra:
///   P1. o Guru que o POVOAMENTO pos em Namek (nada forjado: o robo espera ele nascer), a ate 10 tiles do berco; o jogador e levado a um
///       tile dele, e a tela desenha a folha `NamekOld - Guru Style` com a dica "[E] Guru" no pe -- foto `patriarca-1-dica`;
///   P1b. CONTRA-EXEMPLO (`GameServer.PatriarcaSemMenuDeTeste`, o apontador que nao o enxerga): sem dica -- foto `patriarca-1b-defeito`;
///   P2. a tecla E de verdade abre o menu dele, "Falar com o Grande Patriarca" -- foto `patriarca-2-menu`;
///   P3. o botao: as falas do DM no chat ("põe a mão sobre a cabeça de...", o ganho, "Desperte... AGORA!") -- foto `patriarca-3-despertou`;
///   P4. de novo: "ainda se assenta" (2026-10-26: o Guru solta o banco do potencial de hora em hora; era "Seu potencial já foi
///       despertado", uma vez na vida) -- foto `patriarca-4-de-novo`.
///
///     Godot --path . --host --rede 8881 --horateste 0.5 --semfauna --diagpatriarca --raca Human --conta bancada_patriarca --nome Discipulo
/// </summary>
public partial class RoboDoPatriarca : Node
{
	private const string Botao = "Falar com o Grande Patriarca";

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

	private static CharacterVisual? Boneco(int id) =>
		(World.Instancia?.FindChild($"Remoto{id}", true, false) as RemotePlayer)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>O nome da folha do corpo que o boneco veste agora (sem pasta nem extensao), ou "" sem boneco.</summary>
	private static string Folha(int id) => Boneco(id)?.FolhaDoCorpoDeTeste is { Length: > 0 } f ? System.IO.Path.GetFileNameWithoutExtension(f) : "";

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu da tecla E nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		GD.Print("[dpatriarca] ===== O GRANDE PATRIARCA DE NAMEK (dono, 2026-10-19) =====");

		// ---- P1. o Guru do povoamento, e o jogador ao lado dele ----
		int guru = 0;
		bool nasceu = await Ate(() => (guru = srv.PalcoPt_OGuruDeNamek()) != 0, 60);
		int doBerco = srv.PalcoPt_TilesDoBerco(guru);
		Conferir(nasceu && doBerco is >= 0 and <= 10,
				 $"P1. O POVOAMENTO POS O GURU EM NAMEK, PERTO DO BERCO (o `planet_spawn_turf` do DM: ate 10 tiles) [nasceu {nasceu}; a {doBerco} tile(s) do berco]");
		if (!nasceu) return;
		bool levado = srv.PalcoPt_AoLadoDoGuru(eu, guru);
		await Segundos(1.0);
		srv.EmbateDeFoto_CeuLimpo(eu);
		bool naTela = levado && await Ate(() => Folha(guru).Length > 0 && menu.DicaNaTela == "[E] Guru", 20);
		await Segundos(1.0);
		Conferir(naTela && Folha(guru) == "NamekOld - Guru Style",
				 "P1. A UM TILE DELE A TELA DESENHA O GRANDE PATRIARCA (a folha `NamekOld - Guru Style`, o `M.icon` da fabrica do DM) E A DICA ENSINA "
				 + $"A TECLA [folha \"{Folha(guru)}\"; dica \"{menu.DicaNaTela}\"]");
		Fotografar("user://patriarca-1-dica.png");

		// ---- P1b. o contra-exemplo: o apontador cego ----
		string dicaCega;
		Jandirus.Server.GameServer.PatriarcaSemMenuDeTeste = true;
		try
		{
			await Ate(() => menu.DicaNaTela != "[E] Guru", 3);
			await Segundos(0.3);
			dicaCega = menu.DicaNaTela;
			Fotografar("user://patriarca-1b-defeito.png");
		}
		finally { Jandirus.Server.GameServer.PatriarcaSemMenuDeTeste = false; }
		Conferir(dicaCega != "[E] Guru",
				 $"CONTRA-EXEMPLO (defeito injetado: o apontador nao enxerga o Guru): sem a dica dele -- a regua de P1 reprovaria [\"{dicaCega}\"]");
		bool voltou = await Ate(() => menu.DicaNaTela == "[E] Guru", 5);

		// ---- P2. a tecla E abre o menu dele ----
		Apertar(Key.E);
		bool abriu = voltou && await Ate(() => menu.NaTela, 3);
		await Segundos(0.4);
		List<string> botoes = menu.BotoesDesenhados();
		Conferir(abriu && menu.TituloDesenhado == "Guru" && botoes.SequenceEqual([Botao]),
				 $"P2. A TECLA E ABRE O MENU DELE: \"{Botao}\" [titulo \"{menu.TituloDesenhado}\"; botoes {string.Join(" | ", botoes)}]");
		Fotografar("user://patriarca-2-menu.png");

		// ---- P3. o botao: o despertar ----
		_ditas.Clear();
		cli.Falou += Ouvir;
		try
		{
			bool apertou = menu.ApertarDesenhado(Botao);
			bool despertou = apertou && await Ate(() => _ditas.Any(t => t.Contains("Desperte... AGORA!", StringComparison.Ordinal)), 5);
			await Segundos(0.6);
			Conferir(despertou && _ditas.Any(t => t.Contains("põe a mão sobre a cabeça de", StringComparison.Ordinal))
							   && _ditas.Any(t => t.Contains("o potencial que você guardou se liberta", StringComparison.Ordinal)),
					 $"P3. FALAR COM ELE DESPERTA O POTENCIAL, com as falas do DM no chat [ouvi: {string.Join(" | ", _ditas.TakeLast(4))}]");
			Fotografar("user://patriarca-3-despertou.png");

			// ---- P4. de novo, dentro da hora (2026-10-26: o banco do potencial se solta de hora em hora) ----
			_ditas.Clear();
			Apertar(Key.E);
			bool deNovo = await Ate(() => menu.NaTela, 3) && menu.ApertarDesenhado(Botao)
						  && await Ate(() => _ditas.Any(t => t.Contains("ainda se assenta", StringComparison.Ordinal)), 5);
			await Segundos(0.6);
			Conferir(deNovo, $"P4. DE NOVO DENTRO DA HORA, NAO: o potencial \"ainda se assenta\" [ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
			Fotografar("user://patriarca-4-de-novo.png");
		}
		finally { cli.Falou -= Ouvir; }
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
		Jandirus.Server.GameServer.PatriarcaSemMenuDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dpatriarca] " + l);
		GD.Print(_falhas.Count == 0 ? "[dpatriarca] ===== TUDO OK =====" : $"[dpatriarca] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dpatriarca]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
