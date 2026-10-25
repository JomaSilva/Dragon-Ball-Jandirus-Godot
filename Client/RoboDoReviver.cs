using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using Servidor = Jandirus.Server.GameServer;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// A VOLTA PAGA NA TELA (`--diagreviver`, 2026-10-23). O dono: *"diminua o preco de reviver no ceu pra 200k zeni, e faca com q ao
/// reviver n tenha mais o debuff de bp"*. A conta e a `--alemteste` (familia 8: o preco literal, o prazo que nao se arma, o poder
/// inteiro e o save de antes); aqui so o que o JOGADOR ve, pela tecla E de producao, com janela e `--host`:
///
///   R0. morto na mesa do Enma, "Ouvir o Enma": a fala dele diz o preco novo -- foto `dreviver-1-menu`, com o menu aberto de novo;
///   R1. o menu da mesa diz "Voltar a vida (200.000 zeni)" (LITERAL: e o que o dono pediu, e a regua nao pergunta a tabela);
///   R2. o botao me devolve vivo ao berco;
///   R3. ...e o chat nao fala mais de corpo fragil -- foto `dreviver-2-de-volta`;
///   R4. a aba Forms, cartao "Agora": o multiplicador total de quem voltou e o de antes de morrer, sem o corte a um quarto -- foto
///       `dreviver-3-formas`. CONTRA-EXEMPLO: a regra de antes (`Alem.DebuffDoEnmaComoAntesDeTeste`), a mesma volta paga, e a regua do
///       R4 REPROVA -- foto `dreviver-4-defeito-formas` (o jogo de antes, pra comparar).
///
/// O botao se aperta pelo rotulo da TABELA (`Interacoes`), e nao pelo literal do R1: um rotulo errado reprova so o R1, e o resto do
/// caminho ainda se mede. O BP base vem do `--bpteste` (100.000): com o BP de novato (~8) o arredondamento comeria o corte.
///
///     Godot --janela --resolution 1280x720 --host --rede 10902 --horateste 0.5 --semfauna --bpteste 100000 --diagreviver
///           --raca Human --conta bancada_reviver_dr --nome Defunto
/// </summary>
public partial class RoboDoReviver : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ouvido = [];
	private int _ok;
	private bool _ouvindo;

	/// <summary>O dono, 2026-10-23: o botao da mesa com o preco novo. LITERAL de proposito -- ver o R1 no cabecalho.</summary>
	private const string RotuloDoDono = "Voltar à vida (200.000 zeni)";

	/// <summary>Os 200 mil do dono e mais cinco: o Enma tem de aceitar e deixar os cinco.</summary>
	private const double ZeniNaMesa = 200_005;

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? "   " + detalhe : ""));
		if (passou) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		Alem.DebuffDoEnmaComoAntesDeTeste = false;
		if (_ouvindo && GameClient.Instance is { } cli) cli.Falou -= AoOuvir;
	}

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	private bool Ouvi(string trecho) => _ouvido.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Checa("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => GameClient.Instance is { Connected: true, LocalId: > 0 } && Servidor.Instance != null
									  && MenuDeInteracao.Instancia != null && MenuJogo.Instancia != null && World.Instancia != null, 90);
		Checa("(montagem) em 90 s o cliente conectou, o servidor e deste processo e os dois menus nasceram", pronto);
		if (!pronto || GameClient.Instance is not { } cli || Servidor.Instance is not { } srv
			|| MenuDeInteracao.Instancia is not { } e || MenuJogo.Instancia is not { } menu)
			return;
		GD.Print("[dreviver] ===== A VOLTA PAGA NA TELA: 200 MIL E SEM DEBUFF (2026-10-23) =====");
		cli.Falou += AoOuvir;
		_ouvindo = true;

		// O PODER DE ANTES DE MORRER, de pe e assentado: e a regua do R4 (o Ki de agora pode estar abaixo do cheio -- o piso dele e 0,6 --,
		// e a volta enche o Ki: o "inteiro" depois passa folgado, e o um quarto da regra de antes nao chega perto)
		await Segundos(2);
		double antes = cli.Sheet.MultTotal;
		Checa("(montagem) vivo, com o BP do `--bpteste`, e o multiplicador total de antes de morrer chegou", !cli.Sheet.Morto && antes > 0,
			  $"x{antes:0.###}");
		if (antes <= 0) return;

		try
		{
			// ---- R0/R1: morto na mesa, o menu e a fala do preco
			if (!await MortoNaMesa(cli, srv, "R0")) return;
			string rotuloDaTabela = Interacoes.De(Alem.TipoDoEnma).FirstOrDefault(a => a.Verbo == "enma_reviver").Rotulo ?? "";
			if (!await AbrirOMenuDoEnma(e, "Ouvir o Enma")) { Checa("R0. o menu da tecla E abriu na mesa do Enma", false, Botoes(e)); return; }
			_ouvido.Clear();
			e.ApertarDesenhado("Ouvir o Enma");
			bool falou = await Ate(() => Ouvi("Uma alma equilibrada"), 5);
			string fala = _ouvido.FirstOrDefault(t => t.Contains("Uma alma equilibrada")) ?? "";
			Checa("R0. \"Ouvir o Enma\": a fala dele diz o preco novo (200.000 zeni, no formato do servidor)",
				  falou && (fala.Contains("200.000 zeni") || fala.Contains("200,000 zeni")), fala);
			bool aberto = await AbrirOMenuDoEnma(e, rotuloDaTabela);
			Checa("R1. O MENU DA MESA DIZ O PRECO DO DONO: o botao da volta e \"Voltar a vida (200.000 zeni)\"",
				  aberto && e.BotoesDesenhados().Contains(RotuloDoDono), Botoes(e));
			await Fotografar("dreviver-1-menu", null);

			// ---- R2/R3: o botao, a volta e o chat
			_ouvido.Clear();
			bool apertou = e.ApertarDesenhado(rotuloDaTabela);
			bool voltou = apertou && await Ate(() => !cli.Sheet.Morto && !Alem.EhOAlem(cli.Zone.Name), 10);
			Checa("R2. o botao da volta me devolve VIVO ao berco (o Enma aceitou os 200 mil)", voltou,
				  $"apertou={apertou} morto={cli.Sheet.Morto} zona={cli.Zone.Name}");
			if (!voltou) return;
			bool carimbou = await Ate(() => Ouvi("De volta à terra dos vivos"), 3);
			await Segundos(0.6);   // a linha do chat desenhada antes da foto
			bool fragil = _ouvido.Exists(t => t.Contains("frágil") || t.Contains("poder fica em"));
			Checa("R3. ...e o chat diz a volta e NAO fala mais de corpo fragil nem de 25% (o dono, 2026-10-23)", carimbou && !fragil,
				  string.Join(" | ", _ouvido));
			await Fotografar("dreviver-2-de-volta", null);

			// ---- R4: a aba Forms, o multiplicador total de quem voltou
			(double depois, string naTela) = await MultiplicadorNaAbaForms(cli, menu, () => cli.Sheet.MultTotal >= antes * 0.9);
			Checa("R4. A ABA FORMS: o multiplicador total de quem voltou e o de antes de morrer -- sem o corte a um quarto",
				  depois >= antes * 0.9, $"antes x{antes:0.###}, depois x{depois:0.###} (na tela: {naTela})");
			await Fotografar("dreviver-3-formas", CartaoAgora(menu));
			menu.Fechar();

			// ---- O CONTRA-EXEMPLO: a regra de antes (o prazo armado na volta e o corte de 25%), a mesma volta paga
			Alem.DebuffDoEnmaComoAntesDeTeste = true;
			try
			{
				bool montou = await MortoNaMesa(cli, srv, "contra-exemplo") && await AbrirOMenuDoEnma(e, rotuloDaTabela)
							  && e.ApertarDesenhado(rotuloDaTabela)
							  && await Ate(() => !cli.Sheet.Morto && !Alem.EhOAlem(cli.Zone.Name), 10);
				(double comODefeito, string naTelaComODefeito) = montou
					? await MultiplicadorNaAbaForms(cli, menu, () => cli.Sheet.MultTotal < antes * 0.5)
					: (0.0, "");
				Checa("(defeito injetado: a regra de antes, o debuff do DM) a mesma volta paga deixa o multiplicador a um quarto -- a regua do R4 REPROVA",
					  montou && !(comODefeito >= antes * 0.9) && comODefeito > 0 && comODefeito < antes * 0.5,
					  $"montou={montou}; antes x{antes:0.###}, com o defeito x{comODefeito:0.###} (na tela: {naTelaComODefeito})");
				if (montou) await Fotografar("dreviver-4-defeito-formas", CartaoAgora(menu));
			}
			finally
			{
				Alem.DebuffDoEnmaComoAntesDeTeste = false;
				menu.Fechar();
			}
		}
		finally
		{
			Alem.DebuffDoEnmaComoAntesDeTeste = false;
			srv.PalcoV30_Reviver(cli.LocalId);
		}
	}

	// =====================================================================
	// O PALCO E A TELA
	// =====================================================================
	/// <summary>Morto, de pe e de aureola ao lado do Enma (o palco da `--diagv030`), com 200.005 zeni; espera a minha tela saber.</summary>
	private async System.Threading.Tasks.Task<bool> MortoNaMesa(GameClient cli, Servidor srv, string onde)
	{
		bool naMesa = srv.PalcoV30_MortoAoLadoDoEnma(cli.LocalId, ZeniNaMesa);
		bool morto = naMesa && await Ate(() => cli.Sheet.Morto && Alem.EhOAlem(cli.Zone.Name), 15);
		Checa($"(montagem, {onde}) morto na mesa do Enma, com 200.005 zeni", morto, $"na mesa {naMesa}; zona {cli.Zone.Name}");
		return morto;
	}

	/// <summary>A tecla E com o Enma ao alcance (a dica do pe dela diz que o Outro Mundo ja carregou), ate o menu desenhar este botao.</summary>
	private async System.Threading.Tasks.Task<bool> AbrirOMenuDoEnma(MenuDeInteracao e, string botao)
	{
		for (int tentativa = 0; tentativa < 3; tentativa++)
		{
			if (e.NaTela && e.BotoesDesenhados().Contains(botao)) return true;
			if (e.NaTela) { Tecla(Key.Escape); await Quadros(3); }
			await Ate(() => e.DicaNaTela.Contains("Enma"), 10);
			Tecla(Key.E);
			if (await Ate(() => e.NaTela && e.BotoesDesenhados().Contains(botao), 4)) { await Quadros(2); return true; }
		}
		return false;
	}

	private void Tecla(Key k) => GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });

	private static string Botoes(MenuDeInteracao e) => "botoes: " + string.Join(" | ", e.BotoesDesenhados());

	/// <summary>
	/// A aba Forms aberta, e o multiplicador total quando a condicao vale (ou o prazo acaba): o da ficha e o TEXTO da linha "Multiplicador
	/// total" do cartao "Agora". A ficha da volta chega no tique seguinte da ficha, e a aba remonta com ela.
	/// </summary>
	private async System.Threading.Tasks.Task<(double Mult, string Texto)> MultiplicadorNaAbaForms(GameClient cli, MenuJogo menu, Func<bool> pronto)
	{
		await Ate(pronto, 5);
		menu.Abrir();
		menu.IrPara("Forms");
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
		string texto = "";
		if (CartaoAgora(menu) is { } c)
			foreach (HBoxContainer h in Todos(c).OfType<HBoxContainer>())
				if (h.GetChildCount() == 2 && h.GetChild(0) is Label { Text: "Multiplicador total" } && h.GetChild(1) is Label v) texto = v.Text;
		return (cli.Sheet.MultTotal, texto);
	}

	/// <summary>O cartao "Agora" da aba Forms (o `Cartao` marca o titulo no metadado).</summary>
	private static PanelContainer? CartaoAgora(MenuJogo menu) =>
		menu.PaginaDeTeste("Forms") is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == "Agora"
																	  && c.IsVisibleInTree())
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
	/// <summary>A foto da tela inteira; com um cartao, rolado ate ele ficar a vista.</summary>
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
		Alem.DebuffDoEnmaComoAntesDeTeste = false;
		if (_ouvindo && GameClient.Instance is { } cli) { cli.Falou -= AoOuvir; _ouvindo = false; }
		foreach (string l in _linhas) GD.Print("[dreviver] " + l);
		GD.Print($"[dreviver] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dreviver] ===== TUDO OK =====" : $"[dreviver] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dreviver]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
