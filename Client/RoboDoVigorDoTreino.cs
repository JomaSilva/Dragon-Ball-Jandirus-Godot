using Godot;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO VIGOR DO TREINO NA TELA (`--diagvigortreino`) -- a ponta do cliente das decisoes do dono de 2026-10-24 (*"Avisar e mostrar
/// no HUD"*), com FOTO de cada passo:
///
///   1. vigor cheio e comida: o painel nao diz nada do vigor;
///   2. EXAUSTO (vigor a 3%, com comida): a linha no painel; a tecla T e recusada pelo servidor dizendo quanto falta e que a comida
///      recupera, e o corpo local larga a pose;
///   3. EXAUSTO e SEM COMIDA: as duas linhas, e a recusa que diz que sem comida o vigor nao volta;
///   4. SEM COMIDA com o vigor bom: so a linha da comida, e cada tentativa de treinar avisa (duas tentativas, dois avisos);
///   5. SEM VIGOR NENHUM: a % efetivo da tela cai a 0,3x, como no DM (`StaminaDrain.dm:40`);
///   e os contra-exemplos: o painel calado de antes (`Hud.AvisosDoVigorMudosDeTeste`) e o T exausto que passava e caia no primeiro
///   passe (`GameServer.TExaustoPassaDeTeste`).
///
/// A tecla e a de verdade (`Input.ActionPress`, a telinha do T no botao normal); o vigor e o estomago sao posados pelo palco
/// (`GameServer.VigorTreinoPalco.cs`) -- o caminho de jogo ate eles (treinar, lutar, nao comer) e da bancada `--vigortreinoteste`.
///
///     Godot --path . --position 1920,0 --resolution 1280x720 --host --rede 11011 --diagvigortreino --raca Human --conta bancada_vigortreino_r --nome Suado
/// </summary>
public partial class RoboDoVigorDoTreino : Node
{
	private int _ok;
	private readonly List<string> _falhas = [];
	private readonly List<string> _ouvido = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	private static void Nota(string linha) => GD.Print("[dvigortreino] " + linha);

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		Nota((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		Hud.AvisosDoVigorMudosDeTeste = false;
		Jandirus.Server.GameServer.TExaustoPassaDeTeste = false;
		if (C is { } cli) cli.Falou -= AoOuvir;
	}

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	private bool Ouvi(string trecho) => _ouvido.Any(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>Uma excecao no roteiro e um vermelho, nao um silencio; o placar e a arrumacao saem no `finally`.</summary>
	private async System.Threading.Tasks.Task Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Conferir(false, "R0z. o roteiro correu ate o fim sem lancar excecao", $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		finally
		{
			Hud.AvisosDoVigorMudosDeTeste = false;
			Jandirus.Server.GameServer.TExaustoPassaDeTeste = false;
			Tecla("train", false);
			if (C is { LocalId: not 0 } cli && S is { } srv)
			{
				srv.EstaminaDeTeste(cli.LocalId, 100);
				srv.VigorDeTeste(cli.LocalId, 1, 30);
			}
			Nota(_falhas.Count == 0
				? $"===== TUDO OK ({_ok} afirmacoes) ====="
				: $"===== {_falhas.Count} FALHA(S), {_ok} ok =====\n[dvigortreino]   " + string.Join("\n[dvigortreino]   ", _falhas));
		}
	}

	// =====================================================================
	// OS GESTOS
	// =====================================================================
	private static void Tecla(string acao, bool embaixo)
	{
		if (embaixo) Godot.Input.ActionPress(Teclas.NomeNoMotor(acao));
		else Godot.Input.ActionRelease(Teclas.NomeNoMotor(acao));
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

	/// <summary>A tecla T como um dedo -- e a telinha que ela abre, no botao do treino normal (o AFK de sempre), como o `RoboDeAtividade`.</summary>
	private async System.Threading.Tasks.Task ApertarT()
	{
		bool iaTreinar = Corpo?.AtividadeDeTeste == Protocol.Activity.Treinando;
		Tecla("train", true);
		await Quadros(2);
		Tecla("train", false);
		await Quadros(2);
		if (!iaTreinar && TelaDeTreino.Instancia is { NaTela: true } tela)
			tela.ApertarDesenhado(EscolhaDoTreino.Normal);
		await Quadros(1);
	}

	private void Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("  --     sem foto (headless nao renderiza): " + nome); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://vigor-{nome}.png");
		img.SavePng(caminho);
		Nota("  --     foto: " + caminho);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Percurso()
	{
		Nota("===== O VIGOR DO TREINO NA TELA: O HUD, O T EXAUSTO E A COMIDA (dono, 2026-10-24) =====");

		bool pronto = await Ate(() => C is { Connected: true, LocalId: not 0 } c && c.Atributos.Raca is { Length: > 0 } && Corpo != null
									   && Hud.Instancia != null, 90);
		Conferir(pronto, "R0a. (montagem) o mundo chegou: conexao, corpo local e o painel");
		if (!pronto || C is not { } cli || Corpo is not { } corpo || Hud.Instancia is not { } hud) return;

		bool hospedando = Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0;
		Conferir(hospedando && S != null, "R0b. (montagem) o servidor mora neste processo (--host): o que ele diz e lido dele");
		if (!hospedando || S is not { } srv) return;

		cli.Falou += AoOuvir;
		await Segundos(1.5);
		int eu = cli.LocalId;
		bool Treina() => srv.VigorDoCorpoDeTeste(eu).Treina;
		bool Aviso(string a) => hud.AvisosDoCorpoDeTeste.Contains(a);
		string Painel() => string.Join(" / ", hud.AvisosDoCorpoDeTeste);
		string Estado()
		{
			var v = srv.VigorDoCorpoDeTeste(eu);
			return $"vigor {v.Vigor:P1}, nutricao {v.Nutricao:0.#}, treina {v.Treina}, corpo {corpo.AtividadeDeTeste} | painel: {Painel()}";
		}
		async System.Threading.Tasks.Task Posar(double vigor, double nutricao)
		{
			srv.VigorDeTeste(eu, vigor, nutricao);
			await Ate(() => Math.Abs(cli.Sheet.RazaoDeVigor - vigor) < 0.002 && Math.Abs(cli.Sheet.Nutricao - nutricao) < 0.01, 2);
			await Quadros(3);
		}

		// ---------------------------------------------------------------------- 1. o normal
		Nota("--- 1: vigor cheio e comida no estomago ---");
		await Posar(1, 30);
		Conferir(!Aviso(Hud.AvisoDeExausto) && !Aviso(Hud.AvisoDeSemComida),
			"R1. com o vigor cheio e comida no estomago o painel nao diz nada do vigor", Estado());
		Foto("1-normal");

		// ---------------------------------------------------------------------- 2. exausto, com comida
		Nota("--- 2: EXAUSTO (vigor a 3%, com comida) ---");
		await Posar(0.03, 30);
		Conferir(Aviso(Hud.AvisoDeExausto) && !Aviso(Hud.AvisoDeSemComida),
			"R2a. **EXAUSTO NO PAINEL**: o vigor que nao paga um passe de treino acende a linha (a mesma conta que recusa o T)", Estado());
		_ouvido.Clear();
		await ApertarT();
		await Ate(() => Ouvi("faltam uns"), 3);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		Conferir(Ouvi("você está exausto: faltam uns") && Ouvi("comida que recupera o vigor") && !Treina()
				 && corpo.AtividadeDeTeste == Protocol.Activity.Parado,
			"R2b. **A TECLA T EXAUSTA E RECUSADA**: o chat diz quanto falta e que a comida recupera, o servidor nao treina e o corpo local larga a pose",
			$"{Estado()} | chat: {_ouvido.LastOrDefault() ?? "(nada)"}");
		await Quadros(3);
		Foto("2-exausto");

		// ---------------------------------------------------------------------- 3. exausto e sem comida
		Nota("--- 3: EXAUSTO E SEM COMIDA ---");
		await Posar(0.03, 0);
		Conferir(Aviso(Hud.AvisoDeExausto) && Aviso(Hud.AvisoDeSemComida),
			"R3a. **SEM COMIDA NO PAINEL**, junto do EXAUSTO, quando o estomago esvazia", Estado());
		_ouvido.Clear();
		await ApertarT();
		await Ate(() => Ouvi("estômago vazio"), 3);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		Conferir(Ouvi(Cansaco.FraseDoExaustoSemComida) && !Ouvi("faltam uns") && !Treina(),
			"R3b. ...e a tecla T, exausto de estomago vazio, ouve que sem comida o vigor nao volta (sem prometer tempo)",
			$"{Estado()} | chat: {_ouvido.LastOrDefault() ?? "(nada)"}");
		await Quadros(3);
		Foto("3-exausto-sem-comida");

		// ---------------------------------------------------------------------- 4. sem comida, com vigor
		Nota("--- 4: SEM COMIDA com o vigor ainda bom: o treino liga, e cada tentativa avisa ---");
		await Posar(0.8, 0);
		Conferir(Aviso(Hud.AvisoDeSemComida) && !Aviso(Hud.AvisoDeExausto), "R4a. com o vigor bom e o estomago vazio, so a linha da comida", Estado());
		_ouvido.Clear();
		await ApertarT();
		bool ligou1 = await Ate(Treina, 3);
		await Ate(() => Ouvi("estômago está vazio"), 2);
		await ApertarT();   // para
		await Ate(() => !Treina(), 3);
		await ApertarT();   // tenta de novo
		bool ligou2 = await Ate(Treina, 3);
		static bool DoEstomago(string t) => t.Contains("seu estômago está vazio", StringComparison.OrdinalIgnoreCase);
		await Ate(() => _ouvido.Count(DoEstomago) >= 2, 2);
		int avisos = _ouvido.Count(DoEstomago);
		Conferir(ligou1 && ligou2 && avisos == 2,
			"R4b. **CADA TENTATIVA DE TREINAR COM O ESTOMAGO VAZIO AVISA** que sem comida o vigor nao volta: duas tentativas, dois avisos, e o treino liga",
			$"ligou {ligou1}/{ligou2}, avisos {avisos} | {Estado()}");
		await Quadros(3);
		Foto("4-sem-comida");
		await ApertarT();
		await Ate(() => !Treina(), 3);

		// ---------------------------------------------------------------------- 5. sem vigor nenhum
		Nota("--- 5: SEM VIGOR NENHUM: o poder cai como no DM ---");
		await Posar(1, 30);
		await Ate(() => cli.Sheet.Inteireza > 0, 2);
		double cheio = cli.Sheet.Inteireza;
		double razao = srv.EstaminaDeTeste(eu, 25);   // o vigor no fundo da curva, o estomago seguro vazio
		await Ate(() => cli.Sheet.Inteireza < cheio * 0.5, 3);
		await Quadros(3);
		double fundo = cli.Sheet.Inteireza;
		Conferir(Math.Abs(razao - 0.3) < 0.001 && Math.Abs(fundo / cheio - 0.3) < 0.02 && hud.TextoDeEfetivoDeTeste.StartsWith($"{fundo * 100:0}%"),
			"R5. **SEM VIGOR O CORPO FICA NERFADO COMO NO DM**: no fundo do tanque a % efetivo da tela cai a 0,3x (`StaminaDrain.dm:40`, `base.dm:78`)",
			$"efetivo {cheio:P1} -> {fundo:P1} (tela \"{hud.TextoDeEfetivoDeTeste}\"), razao {razao:0.###} | {Estado()}");
		Foto("5-sem-vigor");

		// ---------------------------------------------------------------------- os contra-exemplos
		Nota("--- contraprovas ---");
		Hud.AvisosDoVigorMudosDeTeste = true;
		hud.RedesenharOsAvisosDoCorpoDeTeste(cli.Sheet);
		bool calado = !Aviso(Hud.AvisoDeExausto) && !Aviso(Hud.AvisoDeSemComida);
		Hud.AvisosDoVigorMudosDeTeste = false;
		hud.RedesenharOsAvisosDoCorpoDeTeste(cli.Sheet);
		Conferir(calado && Aviso(Hud.AvisoDeExausto),
			"R6a. (defeito injetado: o painel calado de antes) a mesma regua REPROVA: o corpo sem vigor e sem comida, e o painel sem uma linha",
			Painel());

		srv.EstaminaDeTeste(eu, 100);
		await Posar(0.03, 30);
		_ouvido.Clear();
		Jandirus.Server.GameServer.TExaustoPassaDeTeste = true;
		try
		{
			await ApertarT();
			await Ate(() => Ouvi("o treino para"), 3);
		}
		finally { Jandirus.Server.GameServer.TExaustoPassaDeTeste = false; }
		Conferir(Ouvi("o treino para") && !Ouvi("faltam uns"),
			"R6b. (defeito injetado: o T exausto passa, o jogo de antes) a mesma regua REPROVA: o treino liga e cai no primeiro passe, sem dizer quanto falta",
			$"chat: {string.Join(" | ", _ouvido)}");
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		await Posar(1, 30);
	}
}
