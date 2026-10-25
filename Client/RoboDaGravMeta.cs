using Godot;
using Jandirus.Net;
using Servidor = Jandirus.Server.GameServer;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O PESO, A GRAVIDADE E O GANHO NA TELA (`--diaggravmeta`, 2026-10-23). O dono: *"usar pesos acima de 8x seu limite corporal, vc nem
/// conseguiria treinar assim"*, *"o buff de bp do aclimado deveria ser no maximo 25% quando vc tem 500x gravidade masterizada"* e *"faça com
/// q morto sofra dano de peso e gravidade"*. As regras e os contra-exemplos delas sao da `--gravmetateste`; aqui e o que o jogador ve. Com
/// janela e `--host`, o palco (`GameServer.GravMetaPalco.cs`) poe o corpo local em cada estado pelas portas de producao:
///   G1. o peso vestido a 8,6x o limite do corpo: a aba Stats diz "Ganho de BP 0x" e a pilula "peso demais (acima de 8x o limite): nao se
///       treina" -- foto `gravmeta-1-stats-peso-demais`. CONTRA-EXEMPLO `MenuJogo.PesoDemaisCaladoDeTeste` (a pilula volta a dizer "8x pesos");
///   G2. a aba Equip: "Ganho no treino" diz "0x -- passa de 8x o limite do corpo" -- foto `gravmeta-2-equip-peso-demais`. O mesmo
///       contra-exemplo (a linha volta ao "8x");
///   G3. o pedido de treinar (o pacote do T, `SendActivity`) e recusado: o servidor nao treina e o chat diz por que -- foto
///       `gravmeta-3-recusa-no-chat`;
///   G4. sem peso, dominando 500x na Terra: a aba Forms diz "Aclimatado +25% nesta gravidade" (eram +97%) -- foto `gravmeta-4-aclimatado-500x`;
///       e 100x diz +14%;
///   G5. morto, no portao do Inferno (10x) dominando 1x: o tique de 1 Hz o prende e o fere, e o chat avisa -- foto `gravmeta-5-morto-no-inferno`.
///
///     Godot --janela --resolution 1280x720 --host --rede 10911 --horateste 0.5 --semfauna --diaggravmeta --raca Human --conta bancada_dgravmeta --nome Pesado
/// </summary>
public partial class RoboDaGravMeta : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ouvido = [];
	private int _ok;

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? "   " + detalhe : ""));
		if (passou) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => Desligar();

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	/// <summary>Nenhum botao de defeito e nenhum ouvido sobrevivem ao roteiro.</summary>
	private void Desligar()
	{
		MenuJogo.PesoDemaisCaladoDeTeste = false;
		if (GameClient.Instance is { } c) c.Falou -= AoOuvir;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Checa("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => GameClient.Instance is { Connected: true, LocalId: > 0 } && Servidor.Instance != null
									  && MenuJogo.Instancia != null && World.Instancia != null, 90);
		Checa("(montagem) em 90 s o cliente conectou, o servidor e deste processo e o menu P nasceu", pronto);
		if (!pronto || GameClient.Instance is not { } cli || Servidor.Instance is not { } srv || MenuJogo.Instancia is not { } menu) return;
		bool dePe = await Ate(() => !cli.Sheet.KO && !cli.Sheet.Morto, 25);
		Checa("(montagem) o corpo entrou de pe", dePe);
		if (!dePe) return;
		cli.Falou += AoOuvir;
		int id = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dgravmeta] ===== O PESO, A GRAVIDADE E O GANHO NA TELA (dono, 2026-10-23) =====");

		// ---------------------------------------------------------------- G1. a aba Stats com o peso demais
		bool vestiu = srv.PalcoGm_Peso(id, "43");
		bool chegou = vestiu && await Ate(() => cli.Atributos.PesoKg > 0 && cli.Atributos.GanhoDeTreino <= 0, 6);
		Checa("(montagem) o peso a 8,6x o limite do corpo, pelo verbo de producao: a ficha lenta chega com o ganho do treino em zero", chegou,
			  $"peso {cli.Atributos.PesoKg:N0} kg; ganho {cli.Atributos.GanhoDeTreino:0.##}x; weight {cli.Atributos.PesoMult:0.##}");
		await AbrirNaAba(menu, "Stats");
		string ganho = menu.ValorDesenhado("Stats", "Ganho de BP") ?? "(nulo)";
		Label? pilula = RotuloQueDiz(menu, "Stats", "peso demais (acima de 8x o limite): não se treina");
		Checa("G1. **A ABA STATS DIZ QUE ASSIM NAO SE TREINA**: \"Ganho de BP 0x\" e a pilula \"peso demais (acima de 8x o limite): não se treina\" "
			  + "-- e nao o \"8x pesos\" do peso, que ainda divide o poder",
			  ganho == "0x" && pilula != null && RotuloQueDiz(menu, "Stats", "8x pesos") == null, $"ganho '{ganho}'; pilula {(pilula != null ? "sim" : "nao")}");
		await Fotografar("gravmeta-1-stats-peso-demais", pilula);
		MenuJogo.PesoDemaisCaladoDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(2);
			Checa("G1b. (defeito injetado: a tela ignora o zero, o jogo de antes) a pilula volta a dizer \"8x pesos\" -- a regua G1 REPROVA",
				  RotuloQueDiz(menu, "Stats", "peso demais") == null && RotuloQueDiz(menu, "Stats", "8x pesos") != null);
		}
		finally { MenuJogo.PesoDemaisCaladoDeTeste = false; }
		menu.ForcarRedesenho();
		await Quadros(2);

		// ---------------------------------------------------------------- G2. a aba Equip
		await AbrirNaAba(menu, "Equip");
		string naEquip = menu.ValorDesenhado("Equip", "Ganho no treino") ?? "(nulo)";
		Checa("G2. **A ABA EQUIP DIZ 0x E O PORQUE**: \"Ganho no treino\" diz \"0x -- passa de 8x o limite do corpo\"",
			  naEquip == "0x -- passa de 8x o limite do corpo", $"'{naEquip}'");
		await Fotografar("gravmeta-2-equip-peso-demais", RotuloQueDiz(menu, "Equip", "0x -- passa de 8x"));
		MenuJogo.PesoDemaisCaladoDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(2);
			string deAntes = menu.ValorDesenhado("Equip", "Ganho no treino") ?? "(nulo)";
			Checa("G2b. (defeito injetado: o mesmo) a linha volta ao \"8x\" do peso -- a regua G2 REPROVA", deAntes == "8x", $"'{deAntes}'");
		}
		finally { MenuJogo.PesoDemaisCaladoDeTeste = false; }
		menu.ForcarRedesenho();
		await Quadros(2);
		menu.Fechar();

		// ---------------------------------------------------------------- G3. o pedido de treinar, recusado
		_ouvido.Clear();
		cli.SendActivity(Protocol.Activity.Treinando);
		bool recusou = await Ate(() => _ouvido.Any(s => s.Contains("com esse peso você não consegue treinar")), 4);
		Checa("G3. **O PEDIDO DE TREINAR E RECUSADO, COM O AVISO**: o servidor nao treina e o chat diz que com esse peso nao da (passa de 8x)",
			  recusou && !srv.AtividadeDeTeste(id).Treina && _ouvido.Any(s => s.Contains("8x")), $"disse \"{string.Join(" | ", _ouvido)}\"");
		await Segundos(0.5);
		await Fotografar("gravmeta-3-recusa-no-chat", null);

		// ---------------------------------------------------------------- G4. a aclimatacao com teto, na aba Forms
		bool aclimatou = srv.PalcoGm_Aclimatado(id, 500) && await Ate(() => Math.Abs(cli.Atributos.Aclimatacao - 1.25f) < 1e-3, 6);
		await AbrirNaAba(menu, "Forms");
		string em500 = menu.ValorDesenhado("Forms", "Aclimatado") ?? "(nulo)";
		Checa("G4. **ACLIMATADO NO MAXIMO +25%**: dominando 500x na Terra, a aba Forms diz \"+25% nesta gravidade\" (eram +97%)",
			  aclimatou && em500 == "+25% nesta gravidade", $"no fio {cli.Atributos.Aclimatacao:0.####}; na tela '{em500}'");
		await Fotografar("gravmeta-4-aclimatado-500x", RotuloQueDiz(menu, "Forms", "nesta gravidade"));
		bool em100Chegou = srv.PalcoGm_Aclimatado(id, 100) && await Ate(() => Math.Abs(cli.Atributos.Aclimatacao - 1.137f) < 2e-3, 6);
		menu.ForcarRedesenho();
		await Quadros(2);
		string em100 = menu.ValorDesenhado("Forms", "Aclimatado") ?? "(nulo)";
		Checa("G4b. ...e dominando 100x, \"+14% nesta gravidade\" (a mesma curva, eram +53%)", em100Chegou && em100 == "+14% nesta gravidade",
			  $"no fio {cli.Atributos.Aclimatacao:0.####}; na tela '{em100}'");
		menu.Fechar();

		// ---------------------------------------------------------------- G5. o morto no Inferno
		_ouvido.Clear();
		bool morreu = srv.PalcoGm_MortoNoInferno(id);
		// a zona chega pelo fio e o mapa monta (o tempo do `RoboDoInferno`); nesse meio-tempo o tique de 1 Hz ja esta apertando
		bool noInferno = morreu && await Ate(() => cli.Zone.Equals(Jandirus.Core.World.ZoneKey.Premade(Jandirus.Core.World.Alem.ZonaDoInferno)), 15);
		bool esmagou = noInferno && await Ate(() => _ouvido.Any(s => s.Contains("ESMAGANDO SEU CORPO")) && cli.Sheet.HP < 99, 8);
		Checa("G5. **O MORTO SOFRE A GRAVIDADE**: no portao do Inferno (10x), dominando 1x, o fantasma fica preso, perde vida e le o aviso no chat",
			  esmagou && cli.Sheet.Morto, $"no Inferno {noInferno}; morto {cli.Sheet.Morto}; vida {cli.Sheet.HP:0.#}; disse \"{string.Join(" | ", _ouvido)}\"");
		await Segundos(3);
		await Fotografar("gravmeta-5-morto-no-inferno", null);
	}

	private async Tarefa AbrirNaAba(MenuJogo menu, string aba)
	{
		menu.Abrir();
		menu.IrPara(aba);
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(2);
	}

	/// <summary>O rotulo visivel da pagina que diz isto (comeco ou meio); nenhum = nulo.</summary>
	private static Label? RotuloQueDiz(MenuJogo menu, string aba, string trecho) =>
		menu.PaginaDeTeste(aba) is { } pg
			? Todos(pg).OfType<Label>().FirstOrDefault(l => l.Text.Contains(trecho, StringComparison.Ordinal) && l.IsVisibleInTree())
			: null;

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
		if (GameClient.Instance is { LocalId: > 0 } c) Servidor.Instance?.PalcoGm_Limpar(c.LocalId);
		Desligar();
		foreach (string l in _linhas) GD.Print("[dgravmeta] " + l);
		GD.Print($"[dgravmeta] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dgravmeta] ===== TUDO OK =====" : $"[dgravmeta] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dgravmeta]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
