using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO RASTRO DO SENSE (`--diagsenserastro`, 2026-10-19) -- o pedido de jogador que o dono repassou: *"colocar a opcao de pesquisar o
/// nome da pessoa ou dar track clicando no nome da pessoa"*. A regra mora em `RastroDoSense`, a tela em `RastroNaTela` e na aba Sense.
///
/// Com janela e com `--host`, no campo aberto da Terra, com o palco do servidor em volta (`GameServer.PalcoRS_Montar`: o Sense no nivel do
/// planeta, o Alvo a 20 tiles a leste e a Angela a 10 a oeste -- os dois conhecidos --, e um Estranho a 12 ao sul):
///   R1. a BUSCA da aba filtra os cartoes: "ANGELA" acha a Angela com acento, "alv" o Alvo, um pedaco da assinatura o Estranho, "zzz"
///       ninguem (com o cartao que diz isso), e vazia devolve todos -- foto `rastro-1-busca`. CONTRA-EXEMPLO (`BuscaCegaDeTeste`);
///   R2. a busca GLOBAL do menu, digitada com a aba Sense aberta, troca a pagina pelos achados (o defeito de antes: a aba nao a via).
///       CONTRA-EXEMPLO (`BuscaGlobalForaDaAssinaturaDeTeste`);
///   R3. CLICAR NO NOME segue: o selo do HUD diz quem, quantos tiles e o rumo, o cartao acende com "RASTREANDO", o cartao "Rastro" nasce, e
///       a seta aponta pra ele pela posicao exata do snapshot (fora do eixo, pra o rumo de oito pontos nao passar por ela) -- foto
///       `rastro-2-seta`;
///   R4. o rastro ANDA com a pessoa: ela vai pro norte e o selo e a seta vao junto. CONTRA-EXEMPLO (`RastroCongeladoDeTeste`);
///   R5. segue a PESSOA, e nao a linha da lista: ela passa a ser a mais perto (a lista e ordenada pela distancia) e o selo continua nela.
///       CONTRA-EXEMPLO (`RastroPelaLinhaDeTeste`);
///   R6. ela sai do alcance (vai pra Namek): "Rastro perdido" e a seta some -- foto `rastro-3-perdido`; volta, e o rastro volta sozinho;
///   R7. o cartao "Rastro" escreve a frase do selo, e o "parar" do HUD apaga tudo;
///   R8. clicar de novo no nome para;
///   R9. sem a skill Sense o rastro para sozinho (a lista deixaria de chegar).
///
///     Godot --path . --host --rede 9401 --campoteste 12 --horateste 0.5 --semfauna --diagsenserastro --raca Human --conta bancada_rastro --nome Rastreador
/// </summary>
public partial class RoboDoRastro : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
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

	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos()
	{
		RastroDoSense.BuscaCegaDeTeste = false;
		RastroDoSense.RastroPelaLinhaDeTeste = false;
		RastroNaTela.RastroCongeladoDeTeste = false;
		MenuJogo.BuscaGlobalForaDaAssinaturaDeTeste = false;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null
									  && RastroNaTela.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, o menu P e o selo do rastro nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu || RastroNaTela.Instancia is not { } hud) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[diagsenserastro] ===== A BUSCA E O RASTRO DO SENSE (pedido de jogador, 2026-10-19) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		Conferir(srv.PalcoRS_Montar(eu), "(palco) o Sense no nivel do planeta e os tres corpos: Alvo, Angela e o Estranho");
		string sig = srv.PalcoRS_Assinatura("Estranho");
		bool chegaram = await Ate(() => Tem(cli, "Alvo") && Tem(cli, "Ângela") && cli.Sentidos.Any(p => p.Assinatura == sig && p.Nome.Length == 0), 10);
		Conferir(chegaram && sig.Length > 0, $"(montagem) a lista do Sense traz o Alvo e a Angela pelo nome e o Estranho pela assinatura [{Lista(cli)}]");
		if (!chegaram) return;
		try
		{
			await ABusca(cli, menu, sig);
			await ABuscaGlobal(menu);
			await OCliqueSegue(cli, menu, hud);
			await ORastroAnda(cli, srv, hud, eu);
			await APessoaENaoALinha(srv, hud, eu);
			await PerdeEVolta(cli, srv, menu, hud, eu);
			await OCartaoEOParar(menu, hud);
			await ClicarDeNovoPara(menu);
			await SemSenseParaSozinho(srv, menu, eu);
		}
		finally
		{
			DesligarOsDefeitos();
			RastroDoSense.Parar();
			srv.PalcoRS_Limpar(eu);
		}
	}

	// =====================================================================
	// R1) A BUSCA DA ABA
	// =====================================================================
	private async Tarefa ABusca(GameClient cli, MenuJogo menu, string sig)
	{
		await Aba(menu, "Sense");
		int todos = cli.Sentidos.Count;
		Conferir(Cartoes(menu).Count == todos && Campo(menu, MenuJogo.DicaDaBuscaDoSense) != null,
				 $"R1. a aba Sense tem a caixa \"{MenuJogo.DicaDaBuscaDoSense}\" e, vazia, um cartao por presenca [{Cartoes(menu).Count} de {todos}]");

		await Digitar(menu, MenuJogo.DicaDaBuscaDoSense, "ANGELA");
		Conferir(Titulos(menu).SequenceEqual(["Ângela"]),
				 $"R1a. **SEM ACENTO E SEM CAIXA**: \"ANGELA\" deixa so o cartao da Angela [{string.Join(", ", Titulos(menu))}]");
		await Digitar(menu, MenuJogo.DicaDaBuscaDoSense, "alv");
		bool soOAlvo = Titulos(menu).SequenceEqual(["Alvo"]);
		Conferir(soOAlvo, $"R1b. \"alv\" deixa so o cartao do Alvo [{string.Join(", ", Titulos(menu))}]");
		Fotografar("user://rastro-1-busca.png");

		string pedaco = sig.Length >= 4 ? sig[..4].ToLowerInvariant() : sig.ToLowerInvariant();
		await Digitar(menu, MenuJogo.DicaDaBuscaDoSense, pedaco);
		Conferir(Titulos(menu).SequenceEqual([$"??? ({sig})"]),
				 $"R1c. **QUEM VOCE NAO CONHECE SE ACHA PELA ASSINATURA** que o cartao mostra: \"{pedaco}\" deixa so o \"??? ({sig})\" "
				 + $"[{string.Join(", ", Titulos(menu))}]");
		await Digitar(menu, MenuJogo.DicaDaBuscaDoSense, "zzz");
		Conferir(Cartoes(menu).Count == 0 && CartaoTitulado(menu, "Busca") != null,
				 $"R1d. \"zzz\" nao deixa cartao de presenca nenhum, e o cartao \"Busca\" diz que ninguem casa [{Cartoes(menu).Count}]");

		RastroDoSense.BuscaCegaDeTeste = true;
		bool cegaReprova;
		try
		{
			await Digitar(menu, MenuJogo.DicaDaBuscaDoSense, "alv");
			cegaReprova = !Titulos(menu).SequenceEqual(["Alvo"]);
		}
		finally { RastroDoSense.BuscaCegaDeTeste = false; }
		Conferir(cegaReprova, $"CONTRA-EXEMPLO (defeito injetado: a busca que ignora o termo): \"alv\" deixa todos os cartoes -- a regua de R1b "
							 + $"REPROVA [{string.Join(", ", Titulos(menu))}]");

		await Digitar(menu, MenuJogo.DicaDaBuscaDoSense, "");
		Conferir(Cartoes(menu).Count == cli.Sentidos.Count, $"R1e. a busca vazia devolve todos os cartoes [{Cartoes(menu).Count} de {cli.Sentidos.Count}]");
	}

	// =====================================================================
	// R2) A BUSCA GLOBAL COM A ABA SENSE ABERTA
	// =====================================================================
	private async Tarefa ABuscaGlobal(MenuJogo menu)
	{
		await Digitar(menu, "procurar acao...", "Treinar");
		// O CABECALHO DOS ACHADOS SAI EM CAIXA ALTA (`Secao` -> `Tema.Rotulo`): "BUSCA: \"TREINAR\"". A r1 procurava "Busca:" e
		// reprovou a regua -- e o contra-exemplo passava pelo motivo errado (o rotulo nunca casava).
		bool achados = TemRotuloQueComeca(menu, "BUSCA: \"TREINAR");
		await Digitar(menu, "procurar acao...", "");
		Conferir(achados, "R2. **A BUSCA GLOBAL NA ABA SENSE**: digitar \"Treinar\" no \"procurar acao...\" troca a pagina pelos achados na hora");

		MenuJogo.BuscaGlobalForaDaAssinaturaDeTeste = true;
		bool reprova;
		try
		{
			await Digitar(menu, "procurar acao...", "Treinar");
			reprova = !TemRotuloQueComeca(menu, "BUSCA: \"TREINAR");
		}
		finally { MenuJogo.BuscaGlobalForaDaAssinaturaDeTeste = false; }
		await Digitar(menu, "procurar acao...", "");
		menu.ForcarRedesenho();
		await Quadros(4);
		Conferir(reprova, "CONTRA-EXEMPLO (defeito injetado: a busca global fora da assinatura da aba, o de antes): a pagina continua a do Sense -- "
						  + "a regua de R2 REPROVA");
	}

	// =====================================================================
	// R3) O CLIQUE NO NOME SEGUE
	// =====================================================================
	private async Tarefa OCliqueSegue(GameClient cli, MenuJogo menu, RastroNaTela hud)
	{
		// FORA DO EIXO: a 20 tiles a leste e 3 ao sul o rumo de oito pontos e "SE" -- uma seta que saisse dele apontaria 45 graus pra baixo,
		// e a posicao exata, quase reto pra direita. E o que separa as duas fontes.
		S!.PalcoRS_Mover(cli.LocalId, "Alvo", 20, 3);
		await Ate(() => cli.Sentidos.Any(p => p.Nome == "Alvo" && p.Distancia == 20), 5);
		await Aba(menu, "Sense");
		await ClicarNoNome(menu, "Alvo");
		bool seguiu = await Ate(() => RastroDoSense.Nome == "Alvo" && hud.TextoDeTeste == "Rastro: Alvo · 20 tiles · SE", 4);
		Conferir(seguiu, $"R3. **CLICAR NO NOME SEGUE**: o selo diz \"Rastro: Alvo · 20 tiles · SE\" [\"{hud.TextoDeTeste}\"]");
		await Quadros(4);
		Conferir(Pilula(menu, "RASTREANDO") && CartaoTitulado(menu, "Rastro") != null,
				 "R3b. o cartao do Alvo ganha a pilula RASTREANDO e o cartao \"Rastro\" nasce no alto da aba");

		menu.Fechar();
		await Quadros(10);
		Conferir(hud.SetaNaTelaDeTeste && hud.FonteDaSetaDeTeste == "corpo" && hud.DirecaoDaSetaDeTeste.X > 0.95f,
				 "R3c. **A SETA APONTA PRA ELE PELA POSICAO EXATA** (o corpo no snapshot): quase reto pra direita, e nao os 45 graus do \"SE\" "
				 + $"[seta {hud.SetaNaTelaDeTeste}, fonte \"{hud.FonteDaSetaDeTeste}\", direcao {hud.DirecaoDaSetaDeTeste}]");
		Fotografar("user://rastro-2-seta.png");
	}

	// =====================================================================
	// R4) O RASTRO ANDA COM A PESSOA
	// =====================================================================
	private async Tarefa ORastroAnda(GameClient cli, Jandirus.Server.GameServer srv, RastroNaTela hud, int eu)
	{
		srv.PalcoRS_Mover(eu, "Alvo", 0, -15);
		bool foi = await Ate(() => hud.TextoDeTeste == "Rastro: Alvo · 15 tiles · N", 5);
		await Quadros(4);
		Conferir(foi && hud.SetaNaTelaDeTeste && hud.DirecaoDaSetaDeTeste.Y < -0.95f,
				 $"R4. **O RASTRO ANDA COM A PESSOA**: ela vai 15 tiles pro norte e o selo e a seta vao junto [\"{hud.TextoDeTeste}\", {hud.DirecaoDaSetaDeTeste}]");

		RastroNaTela.RastroCongeladoDeTeste = true;
		bool reprova;
		try
		{
			srv.PalcoRS_Mover(eu, "Alvo", 0, 15);
			await Ate(() => cli.Sentidos.Any(p => p.Nome == "Alvo" && p.Rumo == 5), 5);
			await Quadros(6);
			reprova = hud.TextoDeTeste != "Rastro: Alvo · 15 tiles · S";
		}
		finally { RastroNaTela.RastroCongeladoDeTeste = false; }
		Conferir(reprova, $"CONTRA-EXEMPLO (defeito injetado: o selo que so le a lista no clique): ela vai pro sul e o selo fica no norte -- a regua "
						  + $"de R4 REPROVA [\"{hud.TextoDeTeste}\"]");
		srv.PalcoRS_Mover(eu, "Alvo", 0, 16);
		await Ate(() => hud.TextoDeTeste == "Rastro: Alvo · 16 tiles · S", 5);
	}

	// =====================================================================
	// R5) A PESSOA, E NAO A LINHA
	// =====================================================================
	private async Tarefa APessoaENaoALinha(Jandirus.Server.GameServer srv, RastroNaTela hud, int eu)
	{
		// Clicado a 20 tiles, o Alvo era o TERCEIRO da lista (Angela a 10, Estranho a 12). A 3 tiles ele passa a ser o primeiro.
		srv.PalcoRS_Mover(eu, "Alvo", 3, 0);
		bool continua = await Ate(() => hud.TextoDeTeste == "Rastro: Alvo · 3 tiles · E", 5);
		Conferir(continua, $"R5. **SEGUE A PESSOA, E NAO A LINHA**: o Alvo vira o mais perto da lista e o selo continua nele [\"{hud.TextoDeTeste}\"]");

		RastroDoSense.RastroPelaLinhaDeTeste = true;
		bool reprova;
		try
		{
			srv.PalcoRS_Mover(eu, "Alvo", 4, 0);
			await Ate(() => C!.Sentidos.Any(p => p.Nome == "Alvo" && p.Distancia == 4), 5);
			await Quadros(6);
			reprova = hud.TextoDeTeste != "Rastro: Alvo · 4 tiles · E";
		}
		finally { RastroDoSense.RastroPelaLinhaDeTeste = false; }
		Conferir(reprova, "CONTRA-EXEMPLO (defeito injetado: o rastro que segue a linha do clique): o selo passa a ler quem ocupa a terceira linha "
						  + $"-- a regua de R5 REPROVA [\"{hud.TextoDeTeste}\"]");
		srv.PalcoRS_Mover(eu, "Alvo", 5, 0);
		await Ate(() => hud.TextoDeTeste == "Rastro: Alvo · 5 tiles · E", 5);
	}

	// =====================================================================
	// R6) PERDE O RASTRO, E ELE VOLTA SOZINHO
	// =====================================================================
	private async Tarefa PerdeEVolta(GameClient cli, Jandirus.Server.GameServer srv, MenuJogo menu, RastroNaTela hud, int eu)
	{
		srv.PalcoRS_LevarPraLonge("Alvo");
		bool perdeu = await Ate(() => hud.TextoDeTeste.StartsWith("Rastro perdido: Alvo", StringComparison.Ordinal), 5);
		await Quadros(4);
		Conferir(perdeu && !hud.SetaNaTelaDeTeste && RastroDoSense.Ativo,
				 $"R6. **SAIU DO ALCANCE, PERDEU O RASTRO**: o Alvo vai pra Namek (fora do Sense no nivel do planeta), o selo diz \"Rastro perdido\" e "
				 + $"a seta some, com o rastro ainda armado [\"{hud.TextoDeTeste}\", seta {hud.SetaNaTelaDeTeste}]");
		Fotografar("user://rastro-3-perdido.png");

		srv.PalcoRS_Mover(eu, "Alvo", 25, 0);
		bool voltou = await Ate(() => hud.TextoDeTeste == "Rastro: Alvo · 25 tiles · E", 6);
		Conferir(voltou, $"R6b. **VOLTOU PRA LISTA, O RASTRO VOLTA SOZINHO** [\"{hud.TextoDeTeste}\"; {Lista(cli)}]");
	}

	// =====================================================================
	// R7) O CARTAO "RASTRO" E O "PARAR" DO HUD
	// =====================================================================
	private async Tarefa OCartaoEOParar(MenuJogo menu, RastroNaTela hud)
	{
		await Aba(menu, "Sense");
		string frase = CartaoTitulado(menu, "Rastro") is { } c ? string.Join(" | ", Todos(c).OfType<Label>().Select(l => l.Text)) : "";
		Conferir(frase.Contains(hud.TextoDeTeste, StringComparison.Ordinal) && hud.TextoDeTeste.Length > 0,
				 $"R7. o cartao \"Rastro\" da aba escreve a mesma frase do selo [{frase}]");
		Button? parar = Todos(hud).OfType<Button>().FirstOrDefault(b => b.Text == "parar");
		parar?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
		Conferir(parar != null && !RastroDoSense.Ativo && hud.TextoDeTeste.Length == 0 && CartaoTitulado(menu, "Rastro") == null
				 && !Pilula(menu, "RASTREANDO"),
				 "R7b. o \"parar\" do selo apaga o rastro: o selo some, e a aba perde o cartao \"Rastro\" e a pilula RASTREANDO");
	}

	// =====================================================================
	// R8) CLICAR DE NOVO PARA
	// =====================================================================
	private async Tarefa ClicarDeNovoPara(MenuJogo menu)
	{
		await ClicarNoNome(menu, "Ângela");
		bool seguiu = await Ate(() => RastroDoSense.Nome == "Ângela", 3);
		await ClicarNoNome(menu, "Ângela");
		bool parou = await Ate(() => !RastroDoSense.Ativo, 3);
		Conferir(seguiu && parou, "R8. clicar no nome da Angela a segue, e clicar de novo para");
	}

	// =====================================================================
	// R9) SEM SENSE, O RASTRO PARA
	// =====================================================================
	private async Tarefa SemSenseParaSozinho(Jandirus.Server.GameServer srv, MenuJogo menu, int eu)
	{
		await ClicarNoNome(menu, "Alvo");
		bool seguiu = await Ate(() => RastroDoSense.Nome == "Alvo", 3);
		srv.PalcoRS_TirarOSense(eu);
		bool parou = await Ate(() => !RastroDoSense.Ativo, 6);
		Conferir(seguiu && parou, "R9. **SEM O SENSE O RASTRO PARA SOZINHO**: tirada a skill, a lista deixaria de chegar, e o rastro nao fica apontando "
								  + "pra onde a pessoa estava");
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static bool Tem(GameClient cli, string nome) => cli.Sentidos.Any(p => p.Nome == nome);

	private static string Lista(GameClient cli) =>
		string.Join(", ", cli.Sentidos.Select(p => $"{(p.Nome.Length > 0 ? p.Nome : $"??? ({p.Assinatura})")}[d{p.Distancia} {Protocol.NomeDoRumo(p.Rumo)}]"));

	/// <summary>A pagina da aba Sense, ou nula.</summary>
	private static Control? Pagina(MenuJogo menu) => menu.PaginaDeTeste("Sense");

	/// <summary>Os cartoes de PRESENCA da aba (sem titulo -- os de titulo sao o "Rastro" e o "Busca"), sem os da tela velha.</summary>
	private static List<PanelContainer> Cartoes(MenuJogo menu) =>
		Pagina(menu) is { } pg
			? [.. Todos(pg).OfType<PanelContainer>().Where(x => !x.IsQueuedForDeletion() && x.HasMeta("cartao") && x.GetMeta("titulo").AsString().Length == 0)]
			: [];

	/// <summary>O nome escrito em cada cartao de presenca, na ordem da aba (o rotulo que segue, marcado com o metadado `rastro`).</summary>
	private static List<string> Titulos(MenuJogo menu) =>
		[.. Cartoes(menu).Select(c => Todos(c).OfType<Label>().FirstOrDefault(l => l.HasMeta("rastro"))?.Text ?? "?")];

	private static PanelContainer? CartaoTitulado(MenuJogo menu, string titulo) =>
		Pagina(menu) is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(x => !x.IsQueuedForDeletion() && x.HasMeta("cartao") && x.GetMeta("titulo").AsString() == titulo)
			: null;

	private static bool Pilula(MenuJogo menu, string texto) =>
		Pagina(menu) is { } pg && Todos(pg).OfType<PanelContainer>().Any(x => !x.IsQueuedForDeletion() && x.HasMeta("pilula") && x.GetMeta("pilula").AsString() == texto);

	private static bool TemRotuloQueComeca(MenuJogo menu, string comeco) =>
		Pagina(menu) is { } pg && Todos(pg).OfType<Label>().Any(l => !l.IsQueuedForDeletion() && l.Text.StartsWith(comeco, StringComparison.Ordinal));

	/// <summary>O campo de texto do menu com esta dica (a da aba ou o "procurar acao..." do topo).</summary>
	private static LineEdit? Campo(MenuJogo menu, string dica) =>
		Todos(menu).OfType<LineEdit>().FirstOrDefault(l => !l.IsQueuedForDeletion() && l.PlaceholderText == dica);

	/// <summary>Escreve no campo como o teclado escreveria: o texto, e o `TextChanged` que a tela escuta.</summary>
	private async Tarefa Digitar(MenuJogo menu, string dica, string texto)
	{
		if (Campo(menu, dica) is not { } campo) { Conferir(false, $"(montagem) o campo \"{dica}\" existe"); return; }
		campo.Text = texto;
		campo.EmitSignal(LineEdit.SignalName.TextChanged, texto);
		await Quadros(4);
	}

	/// <summary>O clique esquerdo no nome do cartao -- o mesmo `GuiInput` que o mouse entrega.</summary>
	private async Tarefa ClicarNoNome(MenuJogo menu, string nome)
	{
		if (!menu.Visible) await Aba(menu, "Sense");
		Label? rotulo = Cartoes(menu).Select(c => Todos(c).OfType<Label>().FirstOrDefault(l => l.HasMeta("rastro")))
									  .FirstOrDefault(l => l?.Text == nome);
		if (rotulo == null) { Conferir(false, $"(montagem) o nome \"{nome}\" esta num cartao da aba Sense"); return; }
		rotulo.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
		await Quadros(4);
	}

	/// <summary>Abre o menu P na aba pedida (pelo botao dela, o caminho do dedo) e espera ela montar.</summary>
	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
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
		DesligarOsDefeitos();
		foreach (string l in _linhas) GD.Print("[diagsenserastro] " + l);
		GD.Print($"[diagsenserastro] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[diagsenserastro] ===== TUDO OK =====" : $"[diagsenserastro] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagsenserastro]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
