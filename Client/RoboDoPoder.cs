using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagpoder`: POWER UP E POWER DOWN NA TELA -- a faixa "Poder em uso" da aba Ki (o estado, o deslizador,
/// relaxar / ficar pronto, esconder / mostrar), a linha do painel de status em cada estado, a linha da aba Stats,
/// e o botao "Controle de Poder" da aba Skills abrindo o controle. Pedido do dono de 2026-10-11.
///
/// MODO HOST (o servidor e deste processo): o palco (`GameServer.PoderPalco.cs`) da o controle de ki a cobaia
/// pelas chamadas do `admin_liberar_ki` e devolve a VERDADE do servidor. Tudo o que o robo CONFERE passa pelo fio
/// de verdade: os botoes mandam o `C2S.Habilidade` de producao, o retrato volta pelo `S2C.PoderEmUso`, e o que se
/// le e o texto que a tela escreveu.
///
/// A irma de servidor (`--poderteste`) mede a REGRA. Esta mede o que o JOGADOR VE e consegue apertar -- e, com
/// janela, FOTOGRAFA cada estado (`user://poder-NN-*.png`: a tela inteira e o recorte do painel de status).
///
///     Godot --janela -- --host --rede 8361 --horateste 0.5 --diagpoder --raca Human --conta bancada_dpoder --nome Contido
/// </summary>
public partial class RoboDoPoder : Node
{
	private int _ok, _falha, _injOk, _injFalha, _fotos;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dpoder] " + s);

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
		Hud.PoderEmUsoMudoDeTeste = false;
		World.Instancia?.PararDeTeste();

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
		Nota(" POWER UP E POWER DOWN NA TELA -- a faixa da aba Ki, o painel de status, a aba Stats, o botao da aba Skills");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 }
									  && M != null && S != null && Hud.Instancia != null && World.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, menu e painel de status de pe) e o servidor e deste processo (--host)", pronto);
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv || Hud.Instancia is not { } hud) { Encerrar(2); return; }

		int eu = cli.LocalId;
		Checa("(montagem) a cobaia existe no servidor, de pe", srv.PalcoDoPoder_Cobaia(eu));
		await Segundos(1.0);   // o primeiro retrato sai pelo tique de 5 Hz

		// ---- 1) sem o controle de ki: so relaxar ----
		menu.Abrir();
		await Abrir(menu, "Ki");
		MenuJogo.PoderNaAba p = menu.PoderNaAbaDeTeste();
		Checa("a aba Ki tem a faixa \"Poder em uso\", e de pe ela le 100% -- \"usando todo o poder\"",
			  p.Visivel && p.Grande == "100%" && p.Legenda == "usando todo o poder", $"{p.Grande} / {p.Legenda}");
		Checa("SEM o controle de ki (o novato): nao ha deslizador nem botao de esconder -- so \"Relaxar (1%)\", que e de todo corpo",
			  !p.TemDeslizador && !p.TemEsconder && p.Prontidao == "Relaxar (1%)", $"deslizador {p.TemDeslizador}, esconder {p.TemEsconder}, botao '{p.Prontidao}'");
		Checa("...e o painel de status nao escreve linha nenhuma com o poder inteiro e a vista", hud.TextoDoPoderEmUsoDeTeste == "", hud.TextoDoPoderEmUsoDeTeste);
		Checa("o verb \"Relaxar / ficar pronto\" existe na aba Skills (da pra ligar uma tecla nele), e o \"Controle de Poder\" ainda nao",
			  Verbos.Da(Verbos.Skills).Any(v => v.Nome == "Relaxar / ficar pronto") && Verbos.PorChave("hab:Power_Control") == null);
		await Foto("aba-ki-sem-controle-de-ki");

		// ---- 2) relaxar ----
		await Clicar(menu.BotaoDaProntidaoDeTeste);
		bool relaxou = await Ate(() => cli.PoderEmUso.Relaxado, 5);
		p = menu.PoderNaAbaDeTeste();
		Checa("**RELAXAR**: o botao manda o verbo, o retrato volta pelo fio e a faixa le 1% -- \"relaxado (1% do poder)\", com o botao virando \"Ficar pronto (100%)\"",
			  relaxou && p.Grande == "1%" && p.Legenda == "relaxado (1% do poder)" && p.Prontidao == "Ficar pronto (100%)", $"{p.Grande} / {p.Legenda} / '{p.Prontidao}'");
		Checa("...o servidor concorda (1% do poder, relaxado)", srv.PalcoDoPoder_Verdade(eu) is { Porcento: 1, Relaxado: true }, $"{srv.PalcoDoPoder_Verdade(eu)}");
		Checa("**O PAINEL DE STATUS DIZ**: \"RELAXADO · 1% DO PODER\"", hud.TextoDoPoderEmUsoDeTeste == "RELAXADO · 1% DO PODER", hud.TextoDoPoderEmUsoDeTeste);
		Hud.PoderEmUsoMudoDeTeste = true;
		try
		{
			hud.RedesenharOPoderEmUsoDeTeste();
			Injeta("o painel mudo (o HUD de antes): a 1% do poder a tela nao diz nada, e a prova de cima reprova", hud.TextoDoPoderEmUsoDeTeste == "");
		}
		finally { Hud.PoderEmUsoMudoDeTeste = false; hud.RedesenharOPoderEmUsoDeTeste(); }
		await Foto("relaxado-aba-ki");
		await Abrir(menu, "Stats");
		Checa("a aba Stats escreve o estado no cartao Estado: \"Poder em uso: relaxado (1% do poder)\"",
			  menu.ValorDesenhado("Stats", "Poder em uso") == "relaxado (1% do poder)", menu.ValorDesenhado("Stats", "Poder em uso") ?? "(sem linha)");
		menu.Fechar();
		await Foto("relaxado-o-painel-de-status");
		await FotoDoPainel("relaxado");

		// ---- 3) ficar pronto ----
		menu.Abrir();
		await Abrir(menu, "Ki");
		await Clicar(menu.BotaoDaProntidaoDeTeste);
		bool voltou = await Ate(() => cli.PoderEmUso.Porcento == 100 && !cli.PoderEmUso.Relaxado, 5);
		Checa("**FICAR PRONTO**: o poder volta a 100% na hora, a faixa volta a \"usando todo o poder\" e a linha do painel some",
			  voltou && menu.PoderNaAbaDeTeste().Grande == "100%" && hud.TextoDoPoderEmUsoDeTeste == "", $"{menu.PoderNaAbaDeTeste().Grande} / '{hud.TextoDoPoderEmUsoDeTeste}'");

		// ---- 4) com o controle de ki: o deslizador e o botao de esconder ----
		Checa("(palco) a cobaia ganha o controle de ki (Ki Unlocked + Basic Ki Control 5) pelas chamadas do `admin_liberar_ki`", srv.PalcoDoPoder_DarAPorta(eu));
		bool chegou = await Ate(() => cli.VerbosAtivos.Contains(MenuJogo.VerboDeBaixar) && cli.VerbosAtivos.Contains(MenuJogo.VerboDeEsconder)
									  && menu.PoderNaAbaDeTeste().TemDeslizador, 15);
		p = menu.PoderNaAbaDeTeste();
		Checa("**COM A PORTA, O CONTROLE APARECE**: os dois verbos chegam pelo fio e a faixa ganha o deslizador e o botao \"Esconder o poder\"",
			  chegou && p.TemDeslizador && p.TemEsconder && p.Esconder == "Esconder o poder" && !p.EsconderApagado, $"deslizador {p.TemDeslizador}, esconder {p.TemEsconder} '{p.Esconder}'");
		await Foto("aba-ki-com-o-controle");

		// ---- 5) baixar pra 40% ----
		menu.ArrastarOPoderDeTeste(40);
		await Quadros(2);
		p = menu.PoderNaAbaDeTeste();
		Checa("arrastar o deslizador ate 40 escreve \"Baixar para 40%\" no botao, aceso (40 esta abaixo de 100)",
			  p.Escolhido == 40 && p.Baixar == "Baixar para 40%" && !p.BaixarApagado, $"'{p.Baixar}' apagado {p.BaixarApagado}");
		await Clicar(menu.BotaoDeBaixarDeTeste);
		bool baixou = await Ate(() => cli.PoderEmUso.Porcento == 40, 5);
		p = menu.PoderNaAbaDeTeste();
		Checa("**BAIXAR**: o botao manda `Power_Control:40`, e a faixa le 40% -- \"usando parte do poder (40%)\"",
			  baixou && p.Grande == "40%" && p.Legenda == "usando parte do poder (40%)", $"{p.Grande} / {p.Legenda}");
		Checa("...o servidor concorda (40%)", srv.PalcoDoPoder_Verdade(eu) is { Porcento: 40, Relaxado: false, Escondido: false }, $"{srv.PalcoDoPoder_Verdade(eu)}");
		Checa("**O PAINEL DE STATUS DIZ**: \"USANDO 40% DO PODER\"", hud.TextoDoPoderEmUsoDeTeste == "USANDO 40% DO PODER", hud.TextoDoPoderEmUsoDeTeste);
		Checa("o mesmo 40 ja nao baixa nada: o botao apaga (isto so BAIXA)", p.BaixarApagado, $"apagado {p.BaixarApagado}");
		menu.ArrastarOPoderDeTeste(80);
		await Quadros(2);
		Checa("...e 80 tambem nao: pra SUBIR e segurando a tecla de carregar, ou ficando pronto", menu.PoderNaAbaDeTeste().BaixarApagado && menu.PoderNaAbaDeTeste().Baixar == "Baixar para 80%");
		Checa("o botao da prontidao agora e \"Ficar pronto (100%)\": de 40% ele sobe, nao relaxa", menu.PoderNaAbaDeTeste().Prontidao == "Ficar pronto (100%)");
		await Foto("contido-em-40-aba-ki");
		menu.Fechar();
		await Foto("contido-em-40-o-painel-de-status");
		await FotoDoPainel("contido-em-40");

		// ---- 6) esconder ----
		menu.Abrir();
		await Abrir(menu, "Ki");
		await Clicar(menu.BotaoDeEsconderDeTeste);
		bool escondeu = await Ate(() => cli.PoderEmUso.Escondido && cli.PoderEmUso.PorVontade, 5);
		p = menu.PoderNaAbaDeTeste();
		Checa("**ESCONDER**: a faixa le \"usando parte do poder (40%) · escondendo o poder\", e o botao vira \"Mostrar o poder\"",
			  escondeu && p.Legenda == "usando parte do poder (40%) · escondendo o poder" && p.Esconder == "Mostrar o poder", $"{p.Legenda} / '{p.Esconder}'");
		Checa("...com o botao APAGADO enquanto os cinco segundos correm (a trava do original)", p.EsconderApagado && cli.PoderEmUso.SigiloEmEspera);
		Checa("...o servidor concorda (escondido, por vontade propria)", srv.PalcoDoPoder_Verdade(eu) is { Escondido: true, PorVontade: true }, $"{srv.PalcoDoPoder_Verdade(eu)}");
		Checa("**O PAINEL DE STATUS DIZ**: \"USANDO 40% DO PODER · ESCONDIDO\"", hud.TextoDoPoderEmUsoDeTeste == "USANDO 40% DO PODER · ESCONDIDO", hud.TextoDoPoderEmUsoDeTeste);
		await Foto("escondido-aba-ki");
		await Abrir(menu, "Forms");
		string mult = menu.ValorDesenhado("Forms", "Multiplicador total") ?? "(sem linha)";
		Checa("ESCONDIDO E SEM SCOUTER, a aba Forms nao entrega o BP: o multiplicador total le \"×0\" (inteiro ele seria 5 / BP)",
			  mult == "×0" && double.IsNaN(cli.Sheet.BP) && cli.Sheet.MultTotal == 0, $"'{mult}', MultTotal no fio {cli.Sheet.MultTotal}");
		await Abrir(menu, "Ki");
		bool reacendeu = await Ate(() => !cli.PoderEmUso.SigiloEmEspera && !menu.PoderNaAbaDeTeste().EsconderApagado, 9);
		Checa("passados os cinco segundos o botao REACENDE sozinho (o servidor manda o retrato de novo)", reacendeu);

		// ---- 7) ficar pronto escondido, e mostrar ----
		await Clicar(menu.BotaoDaProntidaoDeTeste);
		bool cheioEscondido = await Ate(() => cli.PoderEmUso.Porcento == 100 && cli.PoderEmUso.Escondido, 5);
		Checa("ficar pronto NAO mostra o poder: 100% em uso e ainda escondido -- o painel le \"PODER ESCONDIDO\"",
			  cheioEscondido && hud.TextoDoPoderEmUsoDeTeste == "PODER ESCONDIDO", hud.TextoDoPoderEmUsoDeTeste);
		menu.Fechar();
		await Foto("escondido-o-painel-de-status");
		await FotoDoPainel("escondido");
		menu.Abrir();
		await Abrir(menu, "Ki");
		await Clicar(menu.BotaoDeEsconderDeTeste);
		bool mostrou = await Ate(() => !cli.PoderEmUso.Escondido, 5);
		Checa("**MOSTRAR**: o botao desliga o sigilo, e com 100% a vista a linha do painel some de novo",
			  mostrou && hud.TextoDoPoderEmUsoDeTeste == "" && menu.PoderNaAbaDeTeste().Legenda == "usando todo o poder", $"'{hud.TextoDoPoderEmUsoDeTeste}' / {menu.PoderNaAbaDeTeste().Legenda}");

		// ---- 8) o botao "Controle de Poder" da aba Skills abre o controle ----
		await Abrir(menu, "Stats");
		Verbo? controle = Verbos.PorChave("hab:Power_Control");
		PoderEmUsoState antes = cli.PoderEmUso;
		controle?.Acionar?.Invoke();
		await Quadros(4);
		Checa("o botao \"Controle de Poder\" da aba Skills (e a tecla ligada a ele) ABRE a aba Ki com a faixa -- e nao manda o verbo sem numero",
			  controle != null && menu.AbaDeTeste == "Ki" && menu.PoderNaAbaDeTeste().Visivel && cli.PoderEmUso == antes, $"verb {(controle != null)}, aba {menu.AbaDeTeste}");
		menu.Fechar();
		controle?.Acionar?.Invoke();
		await Quadros(4);
		Checa("...com o menu FECHADO ele o abre ja na aba Ki", MenuJogo.Aberto && menu.AbaDeTeste == "Ki" && menu.PoderNaAbaDeTeste().Visivel);

		// ---- 9) a regra principal do BP ----
		p = menu.PoderNaAbaDeTeste();
		string tudo = string.Join(" | ", p.Grande, p.Legenda, p.Baixar, p.Prontidao, p.Esconder, p.Nota);
		Checa("SEM SCOUTER, nenhum texto da faixa fala em BP: so a porcentagem do PROPRIO poder em uso",
			  !tudo.Contains("BP", StringComparison.Ordinal) && double.IsNaN(cli.Sheet.ExpressedBP), tudo);
		await Abrir(menu, "Learning");
		Checa("a faixa e SO da aba Ki: noutra aba ela some", !menu.PoderNaAbaDeTeste().Visivel);
		menu.Fechar();

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// OS AUXILIARES (o molde do RoboDasSalas)
	// =====================================================================

	/// <summary>A tela inteira em `user://poder-NN-nome.png`. SO COM JANELA: as fotos sao pro olho de quem confere.</summary>
	private async Tarefa Foto(string nome)
	{
		if (SemJanela) return;
		await Quadros(3);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath($"user://poder-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
	}

	/// <summary>SO O PAINEL DE STATUS, recortado da tela e ampliado 2x: a linha do poder em uso, legivel.</summary>
	private async Tarefa FotoDoPainel(string nome)
	{
		if (SemJanela || Hud.RetanguloDoStatus is not { } r) return;
		await Quadros(2);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			var corte = new Rect2I((int)Math.Max(r.Position.X - 6, 0), (int)Math.Max(r.Position.Y - 6, 0), (int)r.Size.X + 12, (int)r.Size.Y + 12)
				.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
			Image painel = img.GetRegion(corte);
			painel.Resize(corte.Size.X * 2, corte.Size.Y * 2, Image.Interpolation.Nearest);
			string caminho = ProjectSettings.GlobalizePath($"user://poder-{++_fotos:00}-painel-{nome}.png");
			painel.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto do painel '{nome}': {e.Message})"); }
	}

	private async Tarefa Abrir(MenuJogo menu, string aba)
	{
		menu.IrPara(aba);
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
	}

	private async Tarefa Clicar(Button b)
	{
		b.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(3);
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
}
