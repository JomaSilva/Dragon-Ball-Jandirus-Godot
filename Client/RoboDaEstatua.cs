using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.Verbos;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO MENU DA ESTATUA (`--diagestatua`, 2026-10-22): o menu de criacao do set de esferas na tela, com foto de cada passo -- o dono:
/// *"e ao criar um set de esferas crie um menu bem intuitivo e bem feito pro jogador q criou o set escolher os desejos, a quantidade, a
/// força etc"*. Com janela e `--host`; o palco (`GameServer.EstatuaPalco.cs`) faz do corpo um Namekuseijin do Cla do Dragao dono de Vegeta,
/// e o resto e o caminho de producao:
///   E0. a regra do Core, sem tela: a forca de cada pedido, o nome por planeta, o nome que nao serve, a ida e volta do texto do verbo;
///   E1. a aba World: "Erguer Estátua do Dragão" esta la, e os dois "Erguer + gravar o SUPREMO" nao (o supremo mora no menu);
///   E2. o botao ABRE o menu, e ainda nao ha estatua -- foto `estatua-1-erguer`;
///   E3. o nome, 2 pedidos e "Matar alguém" desligado: a Riqueza diz 25.000.000 na hora -- fotos `estatua-2-escolhas` e `estatua-2b-desligado`;
///       com 3 pedidos o Matar e o Ressuscitar TODOS apagam; CONTRA-EXEMPLO (`JanelaDaEstatua.ForcaInteiraNaTelaDeTeste`): 50.000.000;
///   E4. o supremo em zeni, e "Erguer a estátua": a estatua do servidor tem o nome, os 2 pedidos, o Matar desligado, o supremo -- e o
///       zeni foi cobrado;
///   E5. o dragao de pe: a janela de desejos com o nome, a Riqueza dividida, sem o Matar, e a linha "Configurar o dragão" -- foto
///       `estatua-3-desejos`;
///   E6. "Configurar o dragão" abre o mesmo menu, com o que o set tem -- foto `estatua-4-refazer` --; outro nome, 1 pedido e o Matar de
///       volta, "Refazer o set": o servidor tem a configuracao nova.
///
///     Godot --path . --host --rede 10421 --horateste 0.5 --semfauna --diagestatua --raca Human --conta bancada_estatua_foto --nome Escultor
/// </summary>
public partial class RoboDaEstatua : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

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
		AsRegras();

		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuJogo.Instancia != null && JanelaDaEstatua.Instancia != null && MenuDeDesejos.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, o menu P, a janela de desejos e o menu da estatua nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu || JanelaDaEstatua.Instancia is not { } j) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[diagestatua] ===== O MENU DA ESTATUA DO DRAGAO (dono, 2026-10-22) =====");

		// ---------------------------------------------------------- E1. o palco e a aba World
		bool armou = srv.PalcoES_Criador(eu);
		bool chegou = armou && await Ate(() => cli.Zone.Name == Jandirus.Server.GameServer.PlanetaDoPalcoES
											   && RequisitosDosVerbos.DoVerbo("db_estatua", "").Atende(PosseDoCliente.Atual), 40);
		Conferir(chegou, $"E1. O PALCO: o corpo e um Namekuseijin do Clã do Dragão que aprendeu a estátua, dono de Vegeta, e está lá [zona {cli.Zone.Name}; "
						 + $"raça {PosseDoCliente.Atual.Raca}, classe {PosseDoCliente.Atual.Classe}]");
		if (!chegou) return;
		await Segundos(2.0);

		menu.Abrir();
		menu.IrPara("World");
		menu.ForcarRedesenho();
		await Quadros(4);
		List<string> daWorld = BotoesDa(menu, "World");
		Conferir(daWorld.Contains("Erguer Estátua do Dragão") && !daWorld.Any(t => t.StartsWith("Erguer + gravar", StringComparison.Ordinal)),
				 $"E1b. NA ABA WORLD HA \"Erguer Estátua do Dragão\" E NENHUM \"Erguer + gravar o SUPREMO\" (o supremo mora no menu) [{string.Join(" | ", daWorld.Where(t => t.StartsWith("Erguer", StringComparison.Ordinal)))}]");

		// ---------------------------------------------------------- E2. o botao abre o menu
		bool apertou = ApertarNaAbaWorld(menu, "Erguer Estátua do Dragão");
		bool abriu = apertou && await Ate(() => j.NaTela, 3);
		await Segundos(0.6);
		Conferir(abriu && srv.PalcoES_SetDaqui(eu) == null,
				 $"E2. O BOTAO ABRE O MENU DA ESTATUA, E AINDA NAO HA ESTATUA (ele nao ergue nada sozinho) [na tela {j.NaTela}; set {srv.PalcoES_SetDaqui(eu) != null}]");
		Conferir(j.ConfigDesenhada == "cfg|nome=|pedidos=1" && j.DescricaoDesenhada("dinheiro").Contains("50.000.000", StringComparison.Ordinal),
				 $"E2b. ...e ele abre com 1 pedido, todos os desejos ligados e a Riqueza inteira [{j.ConfigDesenhada}; {j.DescricaoDesenhada("dinheiro")}]");
		Fotografar("user://estatua-1-erguer.png");

		// ---------------------------------------------------------- E3. as escolhas
		Digitar(j, "Mega Shenron");
		Apertar(j, "Pedidos2");
		Marcar(j, "Desejo_matar", false);
		await Quadros(4);
		string riqueza = j.DescricaoDesenhada("dinheiro");
		Conferir(riqueza.Contains("25.000.000", StringComparison.Ordinal) && j.ConfigDesenhada == "cfg|nome=Mega Shenron|pedidos=2|sem=matar"
				 && j.ResumoDesenhado.StartsWith("MEGA SHENRON", StringComparison.Ordinal),
				 $"E3. O NOME, 2 PEDIDOS E O MATAR DESLIGADO: a Riqueza diz 25.000.000 NA HORA (a força dividida), o resumo diz MEGA SHENRON [{riqueza}; "
				 + $"{j.ConfigDesenhada}; {j.ResumoDesenhado}]");
		Fotografar("user://estatua-2-escolhas.png");
		RolarAte(j, "Linha_matar");
		await Quadros(3);
		Fotografar("user://estatua-2b-desligado.png");

		Apertar(j, "Pedidos3");
		await Quadros(3);
		bool matarApagado = j.FindChild("Desejo_matar", true, false) is CheckBox { Disabled: true }
							&& j.FindChild("Desejo_reviver_todos", true, false) is CheckBox { Disabled: true };
		Conferir(matarApagado && j.DescricaoDesenhada("dinheiro").Contains("16.666.667", StringComparison.Ordinal),
				 $"E3b. COM 3 PEDIDOS o Matar e o Ressuscitar TODOS apagam, e a Riqueza cai pra um terço [{j.DescricaoDesenhada("dinheiro")}]");
		Apertar(j, "Pedidos2");
		await Quadros(2);

		try
		{
			JanelaDaEstatua.ForcaInteiraNaTelaDeTeste = true;
			Apertar(j, "Pedidos2");
			await Quadros(2);
			Conferir(j.DescricaoDesenhada("dinheiro").Contains("50.000.000", StringComparison.Ordinal),
					 $"E3c. (defeito injetado: o menu ignora a força dividida) a Riqueza volta a dizer 50.000.000 com 2 pedidos -- a regua de E3 REPROVA [{j.DescricaoDesenhada("dinheiro")}]");
		}
		finally { JanelaDaEstatua.ForcaInteiraNaTelaDeTeste = false; }
		Apertar(j, "Pedidos2");
		await Quadros(2);

		// ---------------------------------------------------------- E4. o supremo, e erguer
		double zeniAntes = srv.PalcoES_Zeni(eu);
		Marcar(j, "GravarSupremo", true);
		await Quadros(2);
		Conferir(j.ConfigDesenhada.EndsWith("|supremo=zeni", StringComparison.Ordinal) && j.ResumoDesenhado.Contains("supremo em zeni", StringComparison.Ordinal),
				 $"E4. O SUPREMO EM ZENI entra na configuracao e no resumo [{j.ConfigDesenhada}]");
		Apertar(j, "Confirmar");
		bool ergueu = await Ate(() => srv.PalcoES_SetDaqui(eu) is { Esferas: Esferas.Total }, 5);
		var set = srv.PalcoES_SetDaqui(eu);
		Conferir(ergueu && set is { Nome: "MEGA SHENRON", Pedidos: 2, Desligados: "matar", Supremo: true }
				 && Math.Abs(zeniAntes - srv.PalcoES_Zeni(eu) - Desejos.PrecoDoSupremo) < 1 && !j.NaTela,
				 $"E4b. \"Erguer a estátua\": o servidor ergue MEGA SHENRON, 2 pedidos, sem o Matar, com o supremo -- e cobra os 2.000.000 de zeni; o menu fecha "
				 + $"[set {set}; zeni {zeniAntes:N0} -> {srv.PalcoES_Zeni(eu):N0}]");
		menu.Fechar();
		await Quadros(3);

		// ---------------------------------------------------------- E5. a janela de desejos
		bool subiu = srv.PalcoES_Invocar(eu);
		MenuDeDesejos janela = MenuDeDesejos.Instancia!;
		bool abriuADoDragao = subiu && await Ate(() => janela.NaTela && janela.NomeDesenhado.Contains("MEGA SHENRON", StringComparison.Ordinal), 8);
		await Segundos(1.2);
		List<(string Id, string Nome, bool Pode, string Motivo)> linhas = janela.LinhasDesenhadas();
		EntradaDoDragao? daRiqueza = cli.MenuDoDragao?.Entrada("dinheiro");
		Conferir(abriuADoDragao && daRiqueza != null && daRiqueza.Desc.Contains("25.000.000", StringComparison.Ordinal)
				 && !linhas.Any(l => l.Id == "matar") && linhas.Any(l => l.Id == AcoesDoDragao.Configurar),
				 $"E5. O DRAGAO DE PE: a janela de desejos diz MEGA SHENRON, a Riqueza dividida (25.000.000), sem o Matar, e com \"Configurar o dragão\" "
				 + $"[\"{janela.NomeDesenhado}\"; riqueza: {daRiqueza?.Desc}; linhas: {string.Join(",", linhas.Select(l => l.Id))}]");
		Fotografar("user://estatua-3-desejos.png");

		// ---------------------------------------------------------- E6. configurar o dragao
		bool clicou = janela.ApertarLinha(AcoesDoDragao.Configurar);
		bool reabriu = clicou && await Ate(() => j.NaTela, 3);
		await Segundos(0.6);
		Conferir(reabriu && j.ConfigDesenhada == "cfg|nome=MEGA SHENRON|pedidos=2|sem=matar"
				 && j.FindChild("Confirmar", true, false) is Button { Text: "Refazer o set" },
				 $"E6. \"Configurar o dragão\" ABRE O MESMO MENU COM O QUE O SET TEM: o nome, os 2 pedidos e o Matar desligado, e o botao e \"Refazer o set\" "
				 + $"[{j.ConfigDesenhada}]");
		Fotografar("user://estatua-4-refazer.png");

		Digitar(j, "Shenron Dourado");
		Apertar(j, "Pedidos1");
		Marcar(j, "Desejo_matar", true);
		await Quadros(2);
		Apertar(j, "Confirmar");
		bool refez = await Ate(() => srv.PalcoES_SetDaqui(eu) is { Nome: "SHENRON DOURADO", Pedidos: 1, Desligados: "" }, 5);
		Conferir(refez, $"E6b. \"Refazer o set\": o servidor tem SHENRON DOURADO, 1 pedido e o Matar de volta, e o supremo continua gravado "
						+ $"[set {srv.PalcoES_SetDaqui(eu)}]");
		Conferir(srv.PalcoES_SetDaqui(eu) is { Supremo: true, Esferas: Esferas.Total }, $"E6c. ...e as sete esferas se refizeram, e o supremo ficou [set {srv.PalcoES_SetDaqui(eu)}]");

		srv.PalcoES_Encerrar();
	}

	/// <summary>E0. A REGRA DO CORE, sem tela.</summary>
	private void AsRegras()
	{
		Conferir(Math.Abs(ConfigDaEstatua.ForcaDeCadaPedido(1) - 1) < 1e-9 && Math.Abs(ConfigDaEstatua.ForcaDeCadaPedido(2) - 0.5) < 1e-9
				 && Math.Abs(ConfigDaEstatua.ForcaDeCadaPedido(3) - 1.0 / 3) < 1e-9,
				 "E0. a força de cada pedido: 1 pedido = 100%, 2 = 50%, 3 = 33%");
		Conferir(ConfigDaEstatua.NomeNoPlaneta("Earth", "Qualquer") == Esferas.NomeDoShenlong && ConfigDaEstatua.NomeNoPlaneta("Namek", "Qualquer") == "PORUNGA"
				 && ConfigDaEstatua.NomeNoPlaneta("Vegeta", "  mega   shenron ") == "MEGA SHENRON" && ConfigDaEstatua.NomeNoPlaneta("Vegeta", "") == ConfigDaEstatua.NomePadrao,
				 "E0b. o nome por planeta: a Terra sempre o Shenlong, Namek sempre o Porunga, fora deles o escolhido (limpo, em maiusculas) ou o SHENRON");
		Conferir(ConfigDaEstatua.PorQueNaoServeONome("A") != null && ConfigDaEstatua.PorQueNaoServeONome("Shen|ron") != null
				 && ConfigDaEstatua.PorQueNaoServeONome(new string('a', ConfigDaEstatua.NomeMaximo + 1)) != null
				 && ConfigDaEstatua.PorQueNaoServeONome("Ômega d'Água-Viva") == null,
				 "E0c. o nome que nao serve: uma letra so, um simbolo, longo demais -- e o com acento, apostrofo e hifen serve");
		var c = new ConfigDaEstatua { Nome = "Mega Shenron", Pedidos = 2, Supremo = ConfigDaEstatua.SupremoEmRecursos };
		c.Desligados.Add("matar");
		c.Desligados.Add("calcinha");
		ConfigDaEstatua? volta = ConfigDaEstatua.Ler(c.Codificar());
		Conferir(volta != null && volta.Nome == "Mega Shenron" && volta.Pedidos == 2 && volta.Desligados.SetEquals(["matar", "calcinha"])
				 && volta.Supremo == ConfigDaEstatua.SupremoEmRecursos && ConfigDaEstatua.Ler("supremo") == null && ConfigDaEstatua.Ler("2") == null,
				 $"E0d. o texto do verbo vai e volta igual, e os argumentos de antes (\"supremo\", \"2\") nao sao configuracao [{c.Codificar()}]");

		// O TETO DO FIO: de nenhum a todos os desejos desligados, com o nome mais longo e o supremo, o texto cabe no argumento do verbo (acima
		// dele o servidor le VAZIO e ergueria a estatua de sempre) -- e volta com os mesmos desligados (a lista menor: `sem=` ou `com=`)
		List<DesejoDef> ajustaveis = [.. ConfigDaEstatua.Ajustaveis];
		int maior = 0;
		bool todosVoltam = true;
		for (int k = 0; k <= ajustaveis.Count; k++)
		{
			var pior = new ConfigDaEstatua { Nome = new string('Á', ConfigDaEstatua.NomeMaximo), Pedidos = 3, Supremo = ConfigDaEstatua.SupremoEmRecursos, SupremoGravado = true };
			foreach (DesejoDef d in ajustaveis.Take(k)) pior.Desligados.Add(d.Id);
			string texto = pior.Codificar();
			maior = Math.Max(maior, texto.Length);
			todosVoltam &= ConfigDaEstatua.Ler(texto) is { } lido && lido.Desligados.SetEquals(pior.Desligados);
		}
		Conferir(maior <= Jandirus.Net.Protocol.MaxArgDeVerbo && todosVoltam && Jandirus.Net.FioDoMenuDoDragao.MaxPedido >= maior,
				 $"E0e. O TEXTO CABE NO FIO: de 0 a {ajustaveis.Count} desejos desligados, o maior tem {maior} letras (o teto do verbo: "
				 + $"{Jandirus.Net.Protocol.MaxArgDeVerbo}; o da linha da janela de desejos: {Jandirus.Net.FioDoMenuDoDragao.MaxPedido}) -- e todos voltam iguais");
	}

	// =====================================================================
	// MEXER NO MENU -- pelos nodes dele, com os sinais de producao
	// =====================================================================
	private static void Digitar(JanelaDaEstatua j, string texto)
	{
		if (j.FindChild("Nome", true, false) is not LineEdit campo) return;
		campo.Text = texto;
		campo.EmitSignal(LineEdit.SignalName.TextChanged, texto);
	}

	private static void Apertar(JanelaDaEstatua j, string nome)
	{
		if (j.FindChild(nome, true, false) is not Button b || b.Disabled) return;
		if (b.ToggleMode) b.ButtonPressed = true;
		b.EmitSignal(BaseButton.SignalName.Pressed);
	}

	/// <summary>Liga ou desliga uma caixa -- o `ButtonPressed` solta o `toggled` de producao.</summary>
	private static void Marcar(JanelaDaEstatua j, string nome, bool ligado)
	{
		if (j.FindChild(nome, true, false) is CheckBox c && !c.Disabled) c.ButtonPressed = ligado;
	}

	/// <summary>Rola a lista ate a linha de um desejo (a foto do desligado).</summary>
	private static void RolarAte(JanelaDaEstatua j, string nomeDaLinha)
	{
		if (j.FindChild("Rolagem", true, false) is ScrollContainer r && j.FindChild(nomeDaLinha, true, false) is Control linha)
			r.EnsureControlVisible(linha);
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private static List<string> BotoesDa(MenuJogo menu, string aba) =>
		menu.PaginaDeTeste(aba) is { } pg ? [.. Todos(pg).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text)] : [];

	private static bool ApertarNaAbaWorld(MenuJogo menu, string texto)
	{
		if (menu.PaginaDeTeste("World") is not { } pg
			|| Todos(pg).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == texto) is not { } b) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	// =====================================================================
	// O RELOGIO E A FOTO
	// =====================================================================
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

	public override void _ExitTree() => JanelaDaEstatua.ForcaInteiraNaTelaDeTeste = false;

	private void Fechar()
	{
		JanelaDaEstatua.ForcaInteiraNaTelaDeTeste = false;
		foreach (string l in _linhas) GD.Print("[diagestatua] " + l);
		GD.Print(_falhas.Count == 0 ? "[diagestatua] ===== TUDO OK =====" : $"[diagestatua] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagestatua]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
