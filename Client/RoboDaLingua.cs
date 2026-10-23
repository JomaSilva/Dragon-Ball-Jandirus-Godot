using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA LINGUA DO JOGO (`--diaglingua`, 2026-10-22) ============================
/// O PEDIDO DO DONO: *"adicionar uma opçao de mudar a lingua do jogo pra ingles, mudando toda a interface do jogo pra
/// ingles, mensagens q o jogador fala automaticamente tb seriam traduzidas pro ingles, entao mesmo q um personagem q
/// esta com jogo em portugues fale frases automatizadas por raiva ou transformaçao, faça com q o jogo saiba quando é
/// automatizada pra traduzir pro ingles na hora"*.
///
/// NASCE NO LOBBY, como a `--diagopcoes`, e pelo mesmo motivo: a opcao mora na tela de opcoes que o `Boot` acabou de
/// pendurar, e o robo aperta o botao "Opções" e escolhe "English" no seletor como o dedo escolheria. Depois o
/// `--lobbyteste` disca, o mundo chega, e o robo olha o HUD, cada aba do menu P e o chat.
///
/// A REGUA DA TELA E "O QUE ESTA DESENHADO AINDA E PORTUGUES?": o texto de todo controle visivel COMO O GODOT O DESENHA
/// (`Atr`, o mesmo caminho do desenho) passa pelo `Tradutor.ParecePortugues` (acento, palavra de ligacao e o vocabulario que
/// so o lado portugues do dicionario tem -- "madrugada" nao tem acento). O que sobra e listado com o texto --
/// e a lista das faltas do jogo, pra quem for completar o dicionario. A mesma regua, com o jogo ainda em portugues,
/// ENXERGA portugues (senao ela passaria sem olhar), e com a traducao fora do Godot (`Idioma.TelaSemTraducaoDeTeste`)
/// reprova.
///
/// O CHAT E O QUE O DONO PEDIU POR ESCRITO: o grito da furia sai pela boca deste jogador pelo caminho de producao
/// (`GameServer.GritarNaErupcao`) e tem de chegar em ingles; a fala DIGITADA (uma frase que o dicionario conhece) tem de
/// chegar crua. Contra-exemplos: o servidor sem a marca de automatica (`GameServer.FalaSemMarcaDeTeste`) e o cliente
/// que traduz o digitado (`Idioma.TraduzODigitadoDeTeste`).
/// ==========================================================================================================
///
/// COMO RODAR (janela no SEGUNDO monitor):
///     Godot --path . --host --rede 10500 --lobbyteste 30 --diaglingua --raca Human --conta bancada_lingua --nome Lingua
///           --position 1920,0 --resolution 1280x720
/// Fotos `lingua-*.png` no `user://`. Fecha com `===== TUDO OK =====` ou `===== N FALHA(S) =====`.
/// </summary>
public partial class RoboDaLingua : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [], _injPassou = [];
	private int _nFoto;

	private static void Nota(string linha) => GD.Print("[lingua] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	/// <summary>
	/// UM CONTRA-EXEMPLO: o defeito ligado tem de deixar a regua vermelha. O que passa batido sai como linha `FALHA` (e
	/// conta no placar): e por ela que o ciclo da fila le a bancada, e um contra-exemplo mudo e uma regua que nao prova nada.
	/// </summary>
	private void Injeta(string oque, bool pegou, string detalhe = "")
	{
		Nota((pegou ? "  pegou  " : "  FALHA  PASSOU BATIDO") + " (defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (pegou) _injOk++;
		else { _injFalha++; _injPassou.Add(oque); }
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => DesligarDefeitos();

	private static void DesligarDefeitos()
	{
		Idioma.TelaSemTraducaoDeTeste = false;
		Idioma.TraduzODigitadoDeTeste = false;
		Jandirus.Server.GameServer.FalaSemMarcaDeTeste = false;
	}

	private async System.Threading.Tasks.Task Rodar()
	{
		int codigo = 3;
		try
		{
			await Percurso();
			codigo = _falha == 0 && _injFalha == 0 ? 0 : 1;
		}
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		finally
		{
			DesligarDefeitos();
		}
		Placar();
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Percurso()
	{
		Nota("==================================================================================");
		Nota(" A LINGUA DO JOGO -- a opcao, a tela inteira em ingles, e a fala automatica traduzida");
		Nota("==================================================================================");

		// ---------------- 0. o lobby, em portugues de fabrica ----------------
		bool lobby = await Ate(() => Boot.SessaoDeJogador && Botao(GetTree().Root, "Opções") != null, 30);
		Checa("o lobby montou, com o botao Opções", lobby);
		if (!lobby) return;
		await Segundos(1.0);
		Checa("de fabrica o jogo esta em portugues", !Idioma.EmIngles && Boot.Config.Lingua == Idioma.Portugues, Boot.Config.Lingua);
		List<string> emPortugues = Sobras(GetTree().Root);
		Checa("(a regua enxerga) com o jogo em portugues, a varredura acha portugues na tela", emPortugues.Count > 0, $"{emPortugues.Count} textos");
		await Foto("lobby-pt");

		// ---------------- 1. a opcao, pelo caminho do dedo ----------------
		Nota("--- 1. Opções -> Idioma / Language -> English ---");
		await Clicar(Botao(GetTree().Root, "Opções")!);
		Checa("o botao Opções do lobby abre a tela de opcoes", PauseMenu.Instancia is { Aberto: true });
		if (PauseMenu.Instancia is not { } pausa) return;
		OptionButton sel = pausa.SeletorDeLinguaDeTeste;
		Checa("o seletor de lingua esta na tela", sel.IsVisibleInTree());
		Checa("...com as duas linguas, cada uma escrita na propria lingua",
			  sel.ItemCount == 2 && sel.GetItemText(0) == "Português" && sel.GetItemText(1) == "English",
			  $"{sel.GetItemText(0)} / {sel.GetItemText(1)}");
		Checa("...e e a primeira opcao da tela (quem precisa dela nao le a lingua da tela)", PrimeiroSeletor(pausa) == sel);
		Escolher(sel, 1);
		await Quadros(4);
		Checa("escolher English poe o jogo em ingles", Idioma.EmIngles);
		Checa("...e a escolha fica gravada no config.json", Settings.Carregar().Lingua == Idioma.Ingles, Settings.Carregar().Lingua);
		Checa("...e o seletor nao se traduz (continua dizendo Português / English)",
			  sel.Atr(sel.GetItemText(0)) == "Português" || !sel.CanAutoTranslate());
		await Foto("opcoes-en");
		ChecaSemSobras("a tela de opcoes inteira em ingles", pausa);
		await Clicar(Botao(pausa, "Fechar")!);
		Checa("o Fechar fecha a tela de opcoes", !pausa.Aberto);
		await Quadros(3);
		ChecaSemSobras("o lobby inteiro em ingles", GetTree().Root);
		await Foto("lobby-en");

		// contra-exemplo da tela: a traducao fora do Godot -- a MESMA varredura tem de achar portugues
		Idioma.TelaSemTraducaoDeTeste = true;
		Idioma.Aplicar(Idioma.Portugues);
		Idioma.Aplicar(Idioma.Ingles);
		await Quadros(3);
		int semGodot = Sobras(GetTree().Root).Count;
		Idioma.TelaSemTraducaoDeTeste = false;
		Idioma.Aplicar(Idioma.Portugues);
		Idioma.Aplicar(Idioma.Ingles);
		await Quadros(3);
		Injeta("a traducao nao registrada no Godot: o lobby fica em portugues e a varredura reprova", semGodot > 0, $"{semGodot} textos");

		// ---------------- 2. o mundo ----------------
		Nota("--- 2. o mundo: o HUD e cada aba do menu P ---");
		bool mundo = await Ate(() => World.Instancia != null && GameClient.Instance is { Connected: true, LocalId: > 0 }
									 && MenuJogo.Instancia != null, 120);
		Checa("o mundo chegou", mundo);
		if (!mundo || MenuJogo.Instancia is not { } menu || GameClient.Instance is not { } cli) return;
		await Segundos(3.0);
		ChecaSemSobras("o HUD e a tela do mundo em ingles", GetTree().Root);
		await Foto("mundo-en");

		menu.Abrir();
		await Quadros(4);
		var sobrasDasAbas = new List<string>();
		foreach (string aba in menu.AbasDeTeste)
		{
			if (Botao(menu, aba) is not { } b) { Checa($"a aba '{aba}' tem botao", false); continue; }
			await Clicar(b);
			await Segundos(0.9);   // a primeira visita de uma aba pode pedir listas ao servidor e remontar (ver as notas do robo das abas)
			foreach (string s in Sobras(menu)) sobrasDasAbas.Add($"{aba}: {s}");
			await Foto("aba-" + aba.ToLowerInvariant());
		}
		Checa("nenhum texto em portugues nas abas do menu P", sobrasDasAbas.Count == 0, Resumo(sobrasDasAbas));
		menu.Fechar();
		await Quadros(3);

		// ---------------- 3. o chat ----------------
		Nota("--- 3. o chat: o recado, o grito automatico e a fala digitada ---");
		var srv = Jandirus.Server.GameServer.Instance!;
		int eu = cli.LocalId;

		const string recado = "voce esta calado.";
		string recadoEn = Idioma.Tradutor.Traduzir(recado);
		List<(Protocol.Fala Canal, string Texto)> novas = await Ouvir(() => srv.AvisoDeTeste(eu, recado), 1, 5);
		Checa("o recado do servidor chega na lingua da tela", recadoEn != recado && novas.Any(l => l.Texto == recadoEn),
			  string.Join(" | ", novas.Select(l => l.Texto)));

		const string amigo = "Kuririn";
		var gritosPt = RaivaEmDegraus.FormasDoNome(NivelDeRaiva.Extrema, amigo).Concat(RaivaEmDegraus.GritosDaFuria).ToHashSet();
		var gritosEn = gritosPt.Select(Idioma.Tradutor.Traduzir).ToHashSet();
		bool GritosEmIngles(List<(Protocol.Fala Canal, string Texto)> ls)
		{
			var ditos = ls.Where(l => l.Canal == Protocol.Fala.Diz).Select(l => l.Texto).ToList();
			return ditos.Count >= 2 && ditos.All(t => gritosEn.Contains(t) && !gritosPt.Contains(t));
		}
		novas = await Ouvir(() => srv.GritoDaFuriaDeTeste(eu, amigo), 2, 6);
		Checa("o grito da FURIA (o nome com o NAO e a frase logo depois) chega em ingles", GritosEmIngles(novas),
			  string.Join(" | ", novas.Select(l => l.Texto)));
		await Foto("chat-grito-en");

		Jandirus.Server.GameServer.FalaSemMarcaDeTeste = true;
		try
		{
			novas = await Ouvir(() => srv.GritoDaFuriaDeTeste(eu, amigo), 2, 6);
			Injeta("o servidor sem a marca de automatica: o grito chega em portugues e a mesma regua reprova", !GritosEmIngles(novas),
				   string.Join(" | ", novas.Select(l => l.Texto)));
		}
		finally { Jandirus.Server.GameServer.FalaSemMarcaDeTeste = false; }

		// A FALA DIGITADA: uma frase que o dicionario CONHECE (a fala do NPC ocupado), escrita por uma pessoa
		const string digitada = "Agora não, estou ocupado.";
		Checa("(montagem) a frase digitada e uma que o dicionario traduziria", Idioma.Tradutor.Traduzir(digitada) != digitada);
		await Segundos(0.6);   // o teto de uma fala a cada 400 ms (`GameServer.MsEntreFalas`)
		novas = await Ouvir(() => cli.SendChat(Protocol.Fala.Diz, digitada), 1, 5);
		Checa("a fala DIGITADA chega crua, do jeito que a pessoa escreveu", novas.Any(l => l.Canal == Protocol.Fala.Diz && l.Texto == digitada),
			  string.Join(" | ", novas.Select(l => l.Texto)));

		Idioma.TraduzODigitadoDeTeste = true;
		try
		{
			await Segundos(0.6);
			novas = await Ouvir(() => cli.SendChat(Protocol.Fala.Diz, digitada), 1, 5);
			Injeta("o cliente que traduz o digitado: a frase da pessoa chega mudada e a mesma regua reprova",
				   !novas.Any(l => l.Canal == Protocol.Fala.Diz && l.Texto == digitada), string.Join(" | ", novas.Select(l => l.Texto)));
		}
		finally { Idioma.TraduzODigitadoDeTeste = false; }
		await Foto("chat-digitada");

		// ---------------- 4. de volta ao portugues, pelo mesmo seletor ----------------
		Nota("--- 4. de volta ao portugues ---");
		pausa.Abrir();
		await Quadros(3);
		Escolher(pausa.SeletorDeLinguaDeTeste, 0);
		await Quadros(4);
		pausa.Fechar("bancada da lingua");
		await Quadros(3);
		Checa("escolher Português volta o jogo pro portugues", !Idioma.EmIngles && Settings.Carregar().Lingua == Idioma.Portugues);
		int deVolta = Sobras(GetTree().Root).Count;
		Checa("...e a tela volta a dizer portugues", deVolta > 0, $"{deVolta} textos");
		await Foto("volta-pt");

		// ---------------- o que o tradutor viu sem traducao ----------------
		List<string> faltas = [.. Idioma.Tradutor.Faltas.OrderBy(f => f, StringComparer.Ordinal)];
		Nota($"--- o que passou pelo tradutor nesta rodada e ficou em portugues: {faltas.Count} ---");
		foreach (string f in faltas.Take(400)) Nota("  FALTA " + f.Replace("\n", "\\n"));
	}

	// =====================================================================
	// A REGUA DA TELA
	// =====================================================================
	private void ChecaSemSobras(string oque, Node raiz)
	{
		List<string> s = Sobras(raiz);
		Checa(oque, s.Count == 0, Resumo(s));
	}

	private static string Resumo(List<string> s)
	{
		if (s.Count == 0) return "";
		foreach (string x in s) Nota("    sobrou " + x.Replace("\n", "\\n"));
		return $"{s.Count} textos em portugues: " + string.Join(" | ", s.Take(6));
	}

	/// <summary>
	/// O QUE ESTA DESENHADO E AINDA PARECE PORTUGUES: o texto de cada controle visivel como o Godot o desenha (`Atr`).
	/// O chat (`RichTextLabel`) fica de fora -- la mora o que as pessoas escrevem -- e o controle que nao se traduz de
	/// proposito (o nome do personagem, o seletor de lingua) tambem.
	/// </summary>
	private static List<string> Sobras(Node raiz)
	{
		var r = new List<string>();
		foreach (Node n in Todos(raiz))
		{
			if (n is not Control c || n.IsQueuedForDeletion() || !c.IsVisibleInTree() || c is RichTextLabel || !c.CanAutoTranslate()) continue;
			foreach (string t in TextosDe(c))
			{
				if (t.Length == 0) continue;
				string visto = c.Atr(t);
				// a regua com o vocabulario do dicionario: "madrugada" e "(alto)" nao tem acento nem palavra de ligacao
				if (Idioma.Tradutor.ParecePortugues(visto)) r.Add($"{c.GetType().Name} '{visto}'");
			}
		}
		return [.. r.Distinct()];
	}

	private static IEnumerable<string> TextosDe(Control c)
	{
		switch (c)
		{
			case Label l: yield return l.Text; break;
			case OptionButton ob:
				for (int i = 0; i < ob.ItemCount; i++) yield return ob.GetItemText(i);
				break;
			case Button b: yield return b.Text; break;
			case LineEdit le: yield return le.PlaceholderText; break;
		}
		if (c.TooltipText.Length > 0) yield return c.TooltipText;
	}

	// =====================================================================
	// O CHAT
	// =====================================================================
	/// <summary>Faz <paramref name="gesto"/> e devolve as linhas que chegaram ao chat depois dele (espera ate <paramref name="quantas"/>).</summary>
	private async System.Threading.Tasks.Task<List<(Protocol.Fala Canal, string Texto)>> Ouvir(Action gesto, int quantas, double prazo)
	{
		int antes = Chat.LinhasDeTeste().Count;
		gesto();
		await Ate(() => Chat.LinhasDeTeste().Count - antes >= quantas, prazo);
		await Quadros(2);
		return [.. Chat.LinhasDeTeste().Skip(antes)];
	}

	// =====================================================================
	// LER E APERTAR A TELA
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	/// <summary>Um botao visivel pelo TEXTO (o de fonte, em portugues -- o `Text` nao muda com a lingua).</summary>
	private static Button? Botao(Node raiz, string texto) =>
		Todos(raiz).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && !b.IsQueuedForDeletion() && b.Text == texto);

	private static OptionButton? PrimeiroSeletor(Node raiz) => Todos(raiz).OfType<OptionButton>().FirstOrDefault(o => o.IsVisibleInTree());

	/// <summary>Escolhe o item <paramref name="i"/> como o mouse escolheria: o item fica selecionado e o sinal sai.</summary>
	private static void Escolher(OptionButton ob, int i)
	{
		ob.Select(i);
		ob.EmitSignal(OptionButton.SignalName.ItemSelected, (long)i);
	}

	private async System.Threading.Tasks.Task Clicar(Button b)
	{
		b.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(3);
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async System.Threading.Tasks.Task Segundos(double s)
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

	private async System.Threading.Tasks.Task Foto(string nome)
	{
		await Quadros(2);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"  --     sem foto pra {nome} (headless?)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://lingua-{++_nFoto:00}-{nome}.png");
		img.SavePng(caminho);
		Nota("  foto   " + caminho);
	}

	private void Placar()
	{
		Nota("==================================================================");
		int total = _falha + _injFalha;
		Nota($"PLACAR: {_ok + _injOk} OK, {total} FALHA(S)   (contra-exemplos: {_injOk} pegaram, {_injFalha} passaram batido)");
		foreach (string r in _reprovadas) Nota("  reprovou: " + r);
		foreach (string r in _injPassou) Nota("  contra-exemplo que passou batido: " + r);
		Nota(total == 0 ? "===== TUDO OK =====" : $"===== {total} FALHA(S) =====");
	}
}
