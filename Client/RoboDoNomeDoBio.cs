using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagbionome`: O NOME DO BIO-ANDROIDE SE ESCREVE AO GESTAR (dono, 2026-10-31, o pedido 204) -- pelo caminho do jogador: o E sobre o
/// mainframe do Bio-Android Lab (montado pelo palco `GameServer.PalcoBN_LabComDna`, com DNA no tanque), o botao "Gestar bio-androide", a
/// CAIXA DE TEXTO com o nome digitado -- foto `bionome-01-o-nome-na-caixa` --, o "Gravar" que fecha o tanque com o nome, e o parto (o mes do
/// tanque adiantado pelo palco): a criatura desperta com o nome escolhido -- e o painel do canto ja o mostra, sem relogar (2026-10-31, o
/// `S2C.MeuNome`) -- foto `bionome-02-ela-nasce-com-ele`.
///
///     Godot --janela -- --resolution 1280x720 --host --rede 12626 --horateste 0.5 --diagbionome --raca Human --conta bancada_dbionome --nome Cientista
/// </summary>
public partial class RoboDoNomeDoBio : Node
{
	/// <summary>O nome que o robo escreve na caixa -- e com que a criatura tem de despertar.</summary>
	private const string NomeDaCriatura = "Cria Perfeita";

	private int _ok, _falha, _fotos;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dbionome] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static bool SemJanela => DisplayServer.GetName() == "headless";

	public override void _Ready() => _ = Rodar();

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		Encerrar();
	}

	private void Encerrar()
	{
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		Nota(_falha == 0 ? "===== TUDO OK =====" : $"===== {_falha} FALHA(S) =====");
		GetTree().Quit(_falha == 0 ? 0 : 1);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" O NOME DO BIO-ANDROIDE SE ESCREVE AO GESTAR (o pedido 204)");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 90);
		Checa("o mundo chegou (conexao, mundo e menu da tecla E) e o servidor e deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		await Ate(() => false, 2.0);

		// ---- o laboratorio com DNA, um tile a direita ----
		srv.PalcoBN_LabComDna(eu);
		await Ate(() => false, 1.5);

		// ---- o E de verdade, o botao do menu, a caixa ----
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
		bool abriu = await Ate(() => menu.NaTela, 5);
		Checa("o E abriu o menu de interacao sobre o mainframe", abriu, string.Join(" | ", menu.BotoesDesenhados()));
		bool apertou = abriu && menu.ApertarDesenhado("Gestar bio-androide");
		bool caixa = await Ate(() => menu.CaixaDeTextoNaTela, 3);
		Checa("\"Gestar bio-androide\" abre a CAIXA DE TEXTO do nome (`Forma.Texto`) em vez de fechar o tanque direto",
			  apertou && caixa, $"botoes: {string.Join(" | ", menu.BotoesDesenhados())}");
		menu.DigitarNaCaixa(NomeDaCriatura, confirmar: false);
		await Quadros(4);
		await Foto("o-nome-na-caixa");

		// ---- o Gravar: o tanque fecha com o nome ----
		menu.DigitarNaCaixa(NomeDaCriatura, confirmar: true);
		bool naFornada = await Ate(() => srv.PalcoBN_NomeNaFornada(eu) == NomeDaCriatura, 5);
		Checa("O TANQUE FECHA COM O NOME ESCRITO NA CAIXA (a gestacao comecou e a fornada o guarda)", naFornada,
			  $"na fornada: '{srv.PalcoBN_NomeNaFornada(eu)}'");

		// ---- o parto: o mes do tanque passa, e a criatura desperta com o nome ----
		// (2026-10-31) o que o painel do canto desenhava NO INSTANTE em que o nome novo chegou ao cliente: o painel se inscreveu no
		// `MeuNomeMudou` antes deste robo, entao ja se redesenhou quando este ouvinte roda (sem esperar a proxima ficha)
		string noInstante = "";
		void AoMeuNome() => noInstante = Hud.Instancia?.NomeDesenhadoDeTeste ?? "";
		cli.MeuNomeMudou += AoMeuNome;
		srv.PalcoBN_ParirAgora();
		bool nasceu = await Ate(() => srv.PalcoBN_Corpo(eu) is { Nome: NomeDaCriatura, Raca: "BioAndroid" }, 8);
		await Ate(() => false, 4.0);   // o anuncio e o corpo novo (a larva) chegam ao cliente
		(string nome, string raca) = srv.PalcoBN_Corpo(eu);
		Checa("A CRIATURA DESPERTA COM O NOME ESCOLHIDO (e nao \"Bio-Androide de Cientista\")", nasceu, $"{nome} ({raca})");
		cli.MeuNomeMudou -= AoMeuNome;
		// (2026-10-31) E O PAINEL DO CANTO MOSTRA O NOME NOVO, sem relogar: o cliente o recebe pelo `S2C.MeuNome` (antes ficava o do
		// login, e a primeira foto deste robo saiu com "Cientista" no painel)
		bool noPainel = await Ate(() => cli.LocalName == NomeDaCriatura && Hud.Instancia?.NomeDesenhadoDeTeste == NomeDaCriatura, 6);
		Checa("O PAINEL DO CANTO MOSTRA O NOME NOVO, SEM RELOGAR (o cliente recebe o renome na hora)", noPainel,
			  $"LocalName '{cli.LocalName}', painel '{Hud.Instancia?.NomeDesenhadoDeTeste}'");
		Checa("O PAINEL TROCA O NOME NO INSTANTE EM QUE ELE CHEGA (sem esperar a proxima ficha)", noInstante == NomeDaCriatura,
			  $"no instante: '{noInstante}'");
		await Foto("ela-nasce-com-ele");
	}

	// =====================================================================
	// OS AUXILIARES (o molde do RoboDaAscensao)
	// =====================================================================
	/// <summary>A tela inteira em `user://bionome-NN-nome.png`. SO COM JANELA: as fotos sao pro olho de quem confere.</summary>
	private async Tarefa Foto(string nome)
	{
		if (SemJanela) return;
		await Quadros(3);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath($"user://bionome-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
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
}
