using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagbioabsorve`: O BIO-ANDROIDE QUE PASSOU DO QUE ABSORVEU NAO PERDE PODER (dono, 2026-10-31, o pedido 203) -- o "Multiplicador total"
/// do cartao Agora da aba Forms, num Bio-Androide com 10 milhoes de BP base e a media da absorcao ligada, contra a REGUA (o mesmo corpo sem
/// absorver nada -- idade, Ki e chao tambem entram no multiplicador):
///   1. o defeito de antes, injetado (`Fighter.MediaDoAbsorvidoComoAntesDeTeste`): 1 milhao absorvido le 0,55 da regua -- o "debuff eterno"
///      do jogador da VPS -- foto `bioabsorve-01-o-debuff-de-antes`;
///   2. o jogo de agora: o mesmo corpo le a regua inteira -- foto `bioabsorve-02-sem-debuff`;
///   3. 30 milhoes absorvidos (mais do que ele tem): a media ainda o puxa pra cima, perto de 2x -- foto `bioabsorve-03-a-media-ajuda`.
///
/// MODO HOST (o servidor e deste processo): o palco (`GameServer.PalcoBA_Absorvido`) crava o BP base e o total absorvido, como a absorcao
/// de verdade deixa escrito; o resto e o jogo -- a conta de poder, o fio e a aba que se redesenha pela assinatura dela.
///
///     Godot --janela -- --resolution 1280x720 --host --rede 12621 --horateste 0.5 --diagbioabsorve --raca Human --conta bancada_dbioabsorve --nome Absorvedor
/// </summary>
public partial class RoboDoBioAbsorve : Node
{
	private int _ok, _falha, _injOk, _injFalha, _fotos;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dbioabsorve] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static bool SemJanela => DisplayServer.GetName() == "headless";

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => Jandirus.Core.Stats.Fighter.MediaDoAbsorvidoComoAntesDeTeste = false;

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void Encerrar(int codigo)
	{
		Jandirus.Core.Stats.Fighter.MediaDoAbsorvidoComoAntesDeTeste = false;
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" O BIO-ANDROIDE QUE PASSOU DO QUE ABSORVEU NAO PERDE PODER (o pedido 203)");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 }
									  && M != null && S != null && World.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta e menu) e o servidor e deste processo (--host)", pronto);
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv) { Encerrar(2); return; }
		int eu = cli.LocalId;
		// O CORPO VIRA UM BIO-ANDROIDE PERFEITO pelo palco do robo da larva (`VirarBioDeLaboratorioDeTeste`): a criacao recusa a raca
		// ("raca nao pertence a esse planeta" -- o bio so nasce do laboratorio), e a r1 deste robo subiu com `--raca BioAndroid` e nunca
		// entrou no mundo. Perfeito e nao larva: a larva sela o poder (sempre relaxada) e esconderia o multiplicador.
		srv.VirarBioDeLaboratorioDeTeste(eu, Jandirus.Core.Races.BioAndroids.Perfeito, 0);
		bool virou = await Ate(() => cli.Atributos.Raca == Jandirus.Core.Races.BioAndroids.Raca, 10);
		Checa("(montagem) o corpo virou um Bio-Androide Perfeito de laboratorio", virou, cli.Atributos.Raca);
		await Ate(() => false, 2.0);

		// ---- 0) a regua: o mesmo corpo sem absorver nada (idade, Ki e chao entram no multiplicador; as razoes abaixo sao contra ela) ----
		srv.PalcoBA_Absorvido(eu, 10_000_000, 0);
		menu.Abrir();
		await Abrir(menu, "Forms");
		await Ate(() => false, 2.0);
		double regua = cli.Sheet.MultTotal;
		Checa("(montagem) a regua: o multiplicador do corpo com 10 milhoes de BP base e nada absorvido", regua > 0.3, $"x{regua:0.###}");

		// ---- 1) o defeito de antes: a media sempre, e o x0,55 do jogador da VPS ----
		Jandirus.Core.Stats.Fighter.MediaDoAbsorvidoComoAntesDeTeste = true;
		srv.PalcoBA_Absorvido(eu, 10_000_000, 1_000_000);
		bool caiu = await Ate(() => cli.Sheet.MultTotal < regua * 0.7 && Multiplicador(menu) == Texto(cli.Sheet.MultTotal), 10);
		double deAntes = cli.Sheet.MultTotal;
		Injeta("a media de antes do pedido 203 (sempre `(BP + absorvido) / 2`): com 10 milhoes de BP e 1 milhao absorvido, a aba le 0,55 da "
			   + "regua -- o debuff eterno do relato", caiu && Math.Abs(deAntes / regua - 0.55) < 0.02,
			   $"tela '{Multiplicador(menu)}', fio x{deAntes:0.###}, regua x{regua:0.###}");
		await Foto("o-debuff-de-antes");

		// ---- 2) o jogo de agora: a regua inteira ----
		Jandirus.Core.Stats.Fighter.MediaDoAbsorvidoComoAntesDeTeste = false;
		srv.PalcoBA_Absorvido(eu, 10_000_000, 1_000_000);
		bool inteiro = await Ate(() => cli.Sheet.MultTotal > regua * 0.98 && Multiplicador(menu) == Texto(cli.Sheet.MultTotal), 10);
		Checa("O MESMO BIO-ANDROIDE, NO JOGO DE AGORA: 10 milhoes de BP e 1 milhao absorvido le o multiplicador inteiro -- o absorvido mais "
			  + "fraco que ele nao tira nada", inteiro && Math.Abs(cli.Sheet.MultTotal / regua - 1) < 0.01,
			  $"tela '{Multiplicador(menu)}', fio x{cli.Sheet.MultTotal:0.###}, regua x{regua:0.###}");
		await Foto("sem-debuff");

		// ---- 3) quem absorveu mais do que tem: a media ainda puxa pra cima ----
		srv.PalcoBA_Absorvido(eu, 10_000_000, 30_000_000);
		bool subiu = await Ate(() => cli.Sheet.MultTotal > regua * 1.5 && Multiplicador(menu) == Texto(cli.Sheet.MultTotal), 10);
		Checa("COM 30 MILHOES ABSORVIDOS (mais do que os 10 dele) a media ainda o puxa pra cima, perto de 2x a regua -- o absorver pra "
			  + "compensar do DM", subiu, $"tela '{Multiplicador(menu)}', fio x{cli.Sheet.MultTotal:0.###}, regua x{regua:0.###}");
		await Foto("a-media-ajuda");
		menu.Fechar();
		srv.PalcoBA_Absorvido(eu, 10_000_000, 0);

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	/// <summary>O texto da linha "Multiplicador total" do cartao Agora, como a aba o desenhou agora.</summary>
	private static string? Multiplicador(MenuJogo menu) => menu.ValorDesenhado("Forms", "Multiplicador total");

	/// <summary>O texto que a aba escreve pra este multiplicador (o `MultTexto` dela, abaixo de x100).</summary>
	private static string Texto(double m) => m >= 10 ? $"×{m:0.#}" : $"×{m:0.##}";

	// =====================================================================
	// OS AUXILIARES (o molde do RoboDaAscensao)
	// =====================================================================
	/// <summary>A tela inteira em `user://bioabsorve-NN-nome.png`. SO COM JANELA: as fotos sao pro olho de quem confere.</summary>
	private async Tarefa Foto(string nome)
	{
		if (SemJanela) return;
		await Quadros(3);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath($"user://bioabsorve-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
	}

	private async Tarefa Abrir(MenuJogo menu, string aba)
	{
		menu.IrPara(aba);
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
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
