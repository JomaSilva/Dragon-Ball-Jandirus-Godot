using Godot;
using Jandirus.Core.Appearance;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS CORPOS DE RACA E DOS FROST DO TORNEIO (`--diagcorposderaca`, 2026-10-18). Os dois pedidos do dono que sao de CARA:
/// *"a raça saibamen n ta com o icone do saibamen, ta com de humano, (talvez outras raças tb, resolva isso)"* e *"e no torneio os frost
/// demons tao sempre usando o mesmo sprite, coloca mais variaçao, tem varios sprites de frost"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra:
///   1. uma fila de figurantes, um por raca, cada um com o corpo de repouso que o catalogo da (o primeiro da lista: o retrato da raca) -- o
///      Humano no comeco, de referencia -- foto `racas-1-corpos.png`;
///   2. CONTRA-EXEMPLO (`VisualCatalog.SemOsCorposDasRacasNovasDeTeste`): a mesma fila com o catalogo de antes, todos humanos palidos -- foto
///      `racas-2-defeito.png`;
///   3. a CRIACAO do Saibaman, na etapa da aparencia, sem o "None" do DM no texto -- foto `racas-3-criacao-saibaman.png`; e o contra-exemplo
///      (`CreationScreen.NoneNaListaDeTeste`, "Linhagens possíveis: None.") -- foto `racas-3b-criacao-defeito.png`;
///   4. oito Frost Demons vestidos pela porta dos NPCs (`AparenciaDeNpc`, com o molde do torneio) -- foto `frost-1-variedade.png`;
///   5. CONTRA-EXEMPLO (`GameServer.FrostNpcSemVariedadeDeTeste`): os oito iguais, o "Changeling Frieza 2" de antes -- foto `frost-2-defeito.png`.
///
///     Godot --path . --host --rede 8465 --campoteste 12 --horateste 0.5 --semfauna --diagcorposderaca --raca Human --conta bancada_corpos --nome Retratista
/// </summary>
public partial class RoboDosCorposDeRaca : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>As racas da fila, na ordem: o Humano de referencia e as onze que ganharam corpo proprio.</summary>
	private static readonly string[] Racas =
		["Human", "Saibaman", "Makyo", "Android", "Alien", "Arlian", "Kanassa", "Gray", "Heran", "Meta", "Yardrat", "SpiritDoll"];

	/// <summary>
	/// AS CASAS DA FOTO, em tiles a partir do jogador (que a camera poe no centro da tela de 1280x720, 96 px por tile): fora dos paineis -- o
	/// de status (no alto a esquerda), o do boneco (no alto a direita) e o chat (embaixo a esquerda) -- e fora do proprio jogador. A primeira
	/// rodada (job89) fotografou o Humano de referencia atras do painel de status, o Arlian atras do boneco e um Frost em cima do jogador.
	/// </summary>
	private static readonly (float X, float Y)[] CasasDasRacas =
		[(-3, -2), (-1.5f, -2), (0, -2), (1.5f, -2), (3, -2), (-6, 0), (-4.5f, 0), (-3, 0), (-1.5f, 0), (1.5f, 0), (3, 0), (4.5f, 0)];

	/// <summary>As oito casas dos Frost: a fileira do jogador, quatro de cada lado.</summary>
	private static readonly (float X, float Y)[] CasasDosFrost =
		[(-6, 0), (-4.5f, 0), (-3, 0), (-1.5f, 0), (1.5f, 0), (3, 0), (4.5f, 0), (6, 0)];

	/// <summary>O corpo de repouso que cada uma tem que vestir -- o nome da folha (sem pasta nem extensao).</summary>
	private static readonly Dictionary<string, string> Esperado = new()
	{
		["Human"] = "NewPaleMale", ["Saibaman"] = "Saibaman - Form 1", ["Makyo"] = "Makyojin", ["Android"] = "GochekAndroid",
		["Alien"] = "Konatsu", ["Arlian"] = "Arlian", ["Kanassa"] = "Kanassan", ["Gray"] = "Jiren", ["Heran"] = "spacepirate",
		["Meta"] = "MetaM", ["Yardrat"] = "Yardrat", ["SpiritDoll"] = "Spirit Doll",
	};

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

	private static CharacterVisual? Boneco(int id) =>
		(World.Instancia?.FindChild($"Remoto{id}", true, false) as RemotePlayer)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>O nome da folha do corpo que o boneco veste agora (sem pasta nem extensao), ou "" sem boneco.</summary>
	private static string Folha(int id) => Boneco(id)?.FolhaDoCorpoDeTeste is { Length: > 0 } f ? System.IO.Path.GetFileNameWithoutExtension(f) : "";

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dcorpos] ===== OS CORPOS DE RACA E OS FROST DO TORNEIO, NA TELA (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		// ---- 1. a fila das racas ----
		List<int> fila = srv.PalcoCorpos_Fila(eu, Racas, CasasDasRacas);
		bool vestidos = await Ate(() => fila.All(i => Folha(i).Length > 0), 10);
		await Segundos(0.6);
		List<string> erradas = [.. Racas.Select((r, i) => (r, f: Folha(fila[i]))).Where(x => x.f != Esperado[x.r]).Select(x => $"{x.r}={x.f}")];
		Conferir(vestidos && erradas.Count == 0,
				 "1. CADA RACA COM O SEU CORPO (*\"a raça saibamen n ta com o icone do saibamen, ta com de humano\"*): o Saibaman de Saibaman, o Makyo, o "
				 + "Androide, os alienigenas (Alien, Arlian, Kanassa, Gray) e as quatro do corpo proprio que o DM nao alcancava (Heran, Meta, Yardrat, "
				 + $"Spirit Doll) -- e o Humano de referencia, de humano{(erradas.Count > 0 ? $" (erradas: {string.Join(", ", erradas)})" : "")}");
		Fotografar("user://racas-1-corpos.png");
		srv.PalcoCorpos_Tirar(fila);
		await Segundos(0.8);

		// ---- 2. o contra-exemplo ----
		VisualCatalog.SemOsCorposDasRacasNovasDeTeste = true;
		try
		{
			List<int> velha = srv.PalcoCorpos_Fila(eu, Racas, CasasDasRacas);
			await Ate(() => velha.All(i => Folha(i).Length > 0), 10);
			await Segundos(0.6);
			int humanos = velha.Count(i => Folha(i) == "NewPaleMale");
			Conferir(humanos == Racas.Length,
					 $"CONTRA-EXEMPLO (defeito injetado: o catalogo sem as racas novas, o jogo de antes): a mesma fila sai inteira de humano palido ({humanos} de "
					 + $"{Racas.Length}) -- a regua do passo 1 reprovaria");
			Fotografar("user://racas-2-defeito.png");
			srv.PalcoCorpos_Tirar(velha);
		}
		finally { VisualCatalog.SemOsCorposDasRacasNovasDeTeste = false; }
		await Segundos(0.8);

		// ---- 3. a criacao do Saibaman ----
		// No jogo a criacao vem ANTES do mundo: a interface dele (o chat, os paineis) e apagada pra foto, como o robo do vestido faz.
		var tela = new CreationScreen { Name = "CriacaoDoSaibaman" };
		AddChild(tela);
		await Quadros(2);
		tela.Reabrir("", "Saibaman", "");
		await Quadros(2);
		for (int i = 0; i < 5 && tela.TituloDaEtapaDeTeste != "APARÊNCIA"; i++)
		{
			tela.FindChildren("*", nameof(Button), true, false).OfType<Button>()
				.FirstOrDefault(b => b.Text == "Avançar" && b.IsVisibleInTree() && !b.Disabled)?.EmitSignal(BaseButton.SignalName.Pressed);
			await Segundos(0.3);
		}
		List<CanvasLayer> apagadas = ApagarAInterfaceMenos(tela);
		try
		{
			string titulo = tela.TituloDaEtapaDeTeste;
			string corpo = srv.PalcoCorpos_PrimeiroCorpo("Saibaman");
			string texto = tela.DescricaoDeTeste;
			Conferir(titulo == "APARÊNCIA" && corpo.Contains("Saibaman - Form 1") && texto.Contains("Sem variação de linhagem") && !texto.Contains("None"),
						   $"3. A CRIACAO do Saibaman chega a etapa da aparencia ('{titulo}') com o corpo dele na previa ({System.IO.Path.GetFileNameWithoutExtension(corpo)}), "
						   + $"e o texto da raca sem o \"None\" do DM (\"{texto}\")");
			await Segundos(0.5);
			Fotografar("user://racas-3-criacao-saibaman.png");

			CreationScreen.NoneNaListaDeTeste = true;
			try
			{
				tela.Reabrir("", "Saibaman", "");
				await Quadros(2);
				for (int i = 0; i < 5 && tela.TituloDaEtapaDeTeste != "APARÊNCIA"; i++)
				{
					tela.FindChildren("*", nameof(Button), true, false).OfType<Button>()
						.FirstOrDefault(b => b.Text == "Avançar" && b.IsVisibleInTree() && !b.Disabled)?.EmitSignal(BaseButton.SignalName.Pressed);
					await Segundos(0.3);
				}
				string comODefeito = tela.DescricaoDeTeste;
				Conferir(comODefeito.Contains("Linhagens possíveis: None"),
							   $"CONTRA-EXEMPLO (defeito injetado: o \"None\" do DM na lista, o jogo de antes): o texto volta a dizer \"{comODefeito}\" -- a regua do "
							   + "passo 3 reprovaria");
				await Segundos(0.4);
				Fotografar("user://racas-3b-criacao-defeito.png");
			}
			finally { CreationScreen.NoneNaListaDeTeste = false; }
		}
		finally { DevolverAInterface(apagadas); }
		tela.QueueFree();
		await Segundos(0.5);

		// ---- 4. os Frost do torneio ----
		List<int> frost = srv.PalcoCorpos_FrostDoTorneio(eu, CasasDosFrost);
		await Ate(() => frost.All(i => Folha(i).Length > 0), 10);
		await Segundos(0.6);
		string[] bases = [.. frost.Select(Folha)];
		Conferir(bases.Distinct().Count() >= 5,
				 $"4. OS FROST DO TORNEIO VARIAM (*\"tem varios sprites de frost\"*): oito vestidos pela porta dos NPCs com o molde do torneio saem com "
				 + $"{bases.Distinct().Count()} corpos de repouso diferentes ({string.Join(", ", bases)})");
		Fotografar("user://frost-1-variedade.png");
		srv.PalcoCorpos_Tirar(frost);
		await Segundos(0.8);

		// ---- 5. o contra-exemplo ----
		Jandirus.Server.GameServer.FrostNpcSemVariedadeDeTeste = true;
		try
		{
			List<int> iguais = srv.PalcoCorpos_FrostDoTorneio(eu, CasasDosFrost);
			await Ate(() => iguais.All(i => Folha(i).Length > 0), 10);
			await Segundos(0.6);
			string[] basesIguais = [.. iguais.Select(Folha)];
			Conferir(basesIguais.Distinct().Count() == 1,
					 $"CONTRA-EXEMPLO (defeito injetado: a lista padrao do DM, o jogo de antes): os oito saem com o MESMO corpo ({basesIguais.FirstOrDefault()}) "
					 + "-- a regua do passo 4 reprovaria");
			Fotografar("user://frost-2-defeito.png");
			srv.PalcoCorpos_Tirar(iguais);
		}
		finally { Jandirus.Server.GameServer.FrostNpcSemVariedadeDeTeste = false; }
		await Segundos(0.5);
	}

	/// <summary>
	/// Apaga toda a interface do jogo (as `CanvasLayer` visiveis) menos <paramref name="fica"/>, e devolve a lista do que apagou -- o metodo do
	/// robo do vestido (`RoboDeVestido.EsconderAInterface`): o mundo nao e `CanvasLayer`, entao so a interface some. So os que estavam acesos
	/// entram na lista, pra a volta nao acender painel que o jogo tinha fechado.
	/// </summary>
	private List<CanvasLayer> ApagarAInterfaceMenos(CanvasLayer fica)
	{
		var apagadas = new List<CanvasLayer>();
		Varrer(GetTree().Root);
		return apagadas;

		void Varrer(Node no)
		{
			foreach (Node f in no.GetChildren())
			{
				if (f == fica) continue;
				if (f is CanvasLayer cl && cl.Visible) { cl.Visible = false; apagadas.Add(cl); }
				Varrer(f);
			}
		}
	}

	private static void DevolverAInterface(List<CanvasLayer> apagadas)
	{
		foreach (CanvasLayer cl in apagadas)
			if (IsInstanceValid(cl)) cl.Visible = true;
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
		VisualCatalog.SemOsCorposDasRacasNovasDeTeste = false;
		Jandirus.Server.GameServer.FrostNpcSemVariedadeDeTeste = false;
		CreationScreen.NoneNaListaDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dcorpos] " + l);
		GD.Print(_falhas.Count == 0 ? "[dcorpos] ===== TUDO OK =====" : $"[dcorpos] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dcorpos]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
