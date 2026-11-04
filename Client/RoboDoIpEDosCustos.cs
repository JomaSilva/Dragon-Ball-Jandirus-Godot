using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// AS FOTOS DOS PEDIDOS 212 E 213 (`--diagipecustos`, 2026-11-01) -- o dono: *"N esqueça de tirar foto de tudo pra revisao"*. Com janela e com
/// `--host` (o palco e `GameServer.FotoDoIpEDosCustos.cs`):
///   I1. o pedido 212 (*"N permita personagens de mesmo ip usarem revive , potential unlock ou qualquer coisa no outro"*): um irmao de
///       conexao colado no jogador; a cura, a fusao e o revive nele saem RECUSADOS, com a frase no chat -- foto `ipecustos-1-mesmo-ip-recusado`;
///   C1. o pedido 213 (*"faça custo de reviver, custo de visitar mundo dos vivos morto etc ser uma config de adm do server"*): os custos mudados
///       pelo verb da aba Admin, e o cartao "Custos do Outro Mundo" da aba Admin com os numeros novos -- foto `ipecustos-2-aba-admin-custos`;
///   C2. ...e a mesa do Enma escrevendo o preco novo no botao -- foto `ipecustos-3-mesa-do-enma`. No fim, os custos voltam ao padrao.
///
///     Godot --path . --host --rede 12846 --horateste 0.5 --semfauna --diagipecustos --raca Human --conta bancada_ipecustos --nome Fotografo
/// </summary>
public partial class RoboDoIpEDosCustos : Node
{
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
	private int Ditas(string trecho) => _ditas.Count(t => t.Contains(trecho, StringComparison.Ordinal));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
										 && MenuDeInteracao.Instancia != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } e || MenuJogo.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dipecustos] ===== AS FOTOS DO MESMO IP E DOS CUSTOS DO OUTRO MUNDO (dono, 2026-11-01) =====");
		cli.Falou += Ouvir;
		try
		{
			// ---------------------------------------------------------------- I1: o irmao de conexao (o pedido 212)
			int irmao = srv.PalcoIP_IrmaoDeConexao(eu);
			await Segundos(1.0);
			srv.PalcoIP_TentarTudo(eu);
			bool recusou = await Ate(() => Ditas("um personagem não usa nada no outro") >= 3, 5);
			await Segundos(1.0);
			Fotografar("ipecustos-1-mesmo-ip-recusado");
			Conferir(irmao != 0 && recusou,
					 "I1. O IRMAO DE CONEXAO: a cura, a fusao e o revive nele saem RECUSADOS, com a frase no chat (*\"N permita personagens de mesmo ip "
					 + $"usarem revive , potential unlock ou qualquer coisa no outro\"*) [irmao {irmao}; frases {Ditas("um personagem não usa nada no outro")}]");
			srv.PalcoIP_Desfazer();

			// ---------------------------------------------------------------- C1: o cartao da aba Admin (o pedido 213)
			srv.PalcoIP_MudarCustos(eu, "revive 350000", "passe 50000", "dias 3");
			bool chegou = await Ate(() => CustosDoAlem.PrecoDoRevive == 350_000 && CustosDoAlem.PrecoDoPasse == 50_000 && CustosDoAlem.DiasDoPasse == 3, 5);
			menu.Abrir();
			menu.IrPara("Admin");
			await Quadros(4);
			Control? cartao = Achar<Label>(menu, l => l.Text.Contains("Custos do Outro Mundo", StringComparison.OrdinalIgnoreCase));
			if (cartao != null && Ancestral<ScrollContainer>(cartao) is { } rol)
			{
				// O CARTAO INTEIRO NA FOTO: a rolagem vai ate o ULTIMO controle dele (o Salvar da ultima linha), e nao so ate o titulo -- a r2 do
				// agente botoes fotografou so o titulo, no pe da aba, com os campos dos precos abaixo da borda.
				Control fundo = Ancestral<PanelContainer>(cartao) is { } painel && UltimoControle(painel) is { } ultimo ? ultimo : cartao;
				rol.EnsureControlVisible(fundo);
				await Quadros(4);
			}
			bool campoNovo = Achar<LineEdit>(menu, l => l.Text == "350.000") != null;
			Fotografar("ipecustos-2-aba-admin-custos");
			Conferir(chegou && cartao != null && campoNovo,
					 "C1. O CARTAO \"CUSTOS DO OUTRO MUNDO\" DA ABA ADMIN mostra os numeros que o verb dela gravou [chegou "
					 + $"{chegou}; cartao {cartao != null}; campo da volta com 350.000 {campoNovo}; {CustosDoAlem.Resumo()}]");
			menu.Fechar();
			await Quadros(3);

			// ---------------------------------------------------------------- C2: a mesa do Enma com o preco novo
			bool naMesa = srv.PalcoV30_MortoAoLadoDoEnma(eu, 1_000_000);
			bool morto = naMesa && await Ate(() => cli.Sheet.Morto && Alem.EhOAlem(cli.Zone.Name), 15);
			const string BotaoNovo = "Voltar à vida (350.000 zeni)";
			bool aberto = morto && await AbrirOMenuDoEnma(e, BotaoNovo);
			await Segundos(0.6);
			Fotografar("ipecustos-3-mesa-do-enma");
			Conferir(aberto, $"C2. A MESA DO ENMA ESCREVE O PRECO NOVO no botao [{(aberto ? BotaoNovo : string.Join(" | ", e.BotoesDesenhados()))}]");
			if (e.NaTela) Tecla(Key.Escape);
			srv.PalcoV30_Reviver(eu);
		}
		finally
		{
			cli.Falou -= Ouvir;
			srv.PalcoIP_Desfazer();
			srv.PalcoIP_MudarCustos(eu, "revive 200000", "passe 100000", "dias 7");
		}
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

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private void Tecla(Key k) => GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });

	private static T? Achar<T>(Node raiz, Func<T, bool> serve) where T : Node
	{
		if (raiz is T t && !t.IsQueuedForDeletion() && serve(t)) return t;
		foreach (Node filho in raiz.GetChildren())
			if (Achar(filho, serve) is { } achado) return achado;
		return null;
	}

	/// <summary>O ultimo botao ou campo (na ordem da arvore) dentro deste no.</summary>
	private static Control? UltimoControle(Node raiz)
	{
		Control? ultimo = null;
		foreach (Node filho in raiz.GetChildren())
		{
			if (filho is Button or LineEdit) ultimo = (Control)filho;
			if (UltimoControle(filho) is { } dentro) ultimo = dentro;
		}
		return ultimo;
	}

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent()) if (p is T t) return t;
		return null;
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
		foreach (string l in _linhas) GD.Print("[dipecustos] " + l);
		GD.Print(_falhas.Count == 0 ? "[dipecustos] ===== TUDO OK =====" : $"[dipecustos] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dipecustos]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
