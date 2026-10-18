using Godot;
using Jandirus.Core.Races;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO ADENDO DO BIO-ANDROIDE (`--diagdnabio`, 2026-10-17): as SKILLS DO DNA no menu, a ESTOCADA e o NUCLEO, na tela. O adendo do dono --
/// *"o bio androide pode escolher dentre as raças de seu dna quais skills (passivas e ativas) ele quer, com limite de 6 skills"* -- e
/// *"n esqueça de checar tudo com print de tudo isso q estamos fazendo"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra. O servidor deste processo vira o corpo do robo um bio de laboratorio
/// PERFEITO com o sangue do Cell (Saiyajin, Namekuseijin, Icer, Humano -- a premissa); o robo aperta os BOTOES do menu P e os verbs pelo fio:
///   0. a aba Skills com os dezesseis botoes da escolha -- foto `dnabio-0-escolha.png`;
///   1. tres escolhas pelo BOTAO (Warrior Race, Second Wind, Unrestrained Wrath): os tres somem, as duas ativas ganham botao e a passiva vira
///      linha -- foto `dnabio-1-tres.png`;
///   2. mais tres (Seasoned Warrior, Namekian Resilience, Perfect Control): a escolha fecha -- foto `dnabio-2-completo.png`;
///   3. CONTRA-EXEMPLO (`SkillsRaciais.BioComOSangueInteiroDeTeste`): o bio ganha o sangue inteiro e o Max Power, que ele nao escolheu, aparece
///      -- foto `dnabio-2a-sangue-inteiro.png`;
///   4. a ATIVA escolhida, pelo botao: Unrestrained Wrath (o kiai e a ira) -- foto `dnabio-3-ira.png`;
///   5. a PASSIVA escolhida: com a vida a 15%, a Warrior Race acende no HUD -- foto `dnabio-4-warrior-race.png`;
///   6. a ESTOCADA: agarra a presa e crava a cauda quatro vezes -- o braco esquerdo quebra e o direito sangra -- foto `dnabio-5-estocada.png`;
///   7. o NUCLEO no braco esquerdo: a morte chega e ele a nega (caido, 5% de tudo) -- foto `dnabio-6-nucleo.png`; a segunda, e ele DETONA e
///      leva o braco -- foto `dnabio-7-detonou.png`.
///
///     Godot --path . --host --rede 8460 --campoteste 12 --horateste 0.5 --semfauna --diagdnabio --raca Human --conta bancada_dnabio --nome Genoma
/// </summary>
public partial class RoboDoDnaDoBio : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
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
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && M != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu nasceram e o servidor esta neste processo");
		if (!pronto || C is not { } cli || M is not { } menu || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);   // o corpo assenta no campo
		GD.Print("[ddnabio] ===== AS SKILLS DO DNA, A ESTOCADA E O NUCLEO DO BIO-ANDROIDE, NA TELA (dono, 2026-10-17) =====");

		// ---- a premissa: um bio perfeito com o sangue do Cell ----
		srv.EmbateDeFoto_CeuLimpo(eu);
		srv.VirarBioDeLaboratorioDeTeste(eu, BioAndroids.Perfeito, 2);
		srv.DarSangueDeTeste(eu, ["Saiyan", "Namekian", "Icer", "Human"]);
		// o NUCLEO ja escolhido (pelo fio do botao): os cinco botoes dele saem da aba, e e no braco esquerdo que ele vai morar no fim
		cli.SendHabilidade("nucleo:Braco esquerdo");
		bool chegou = await Ate(() => cli.Atributos.Raca == BioAndroids.Raca && cli.Atributos.SkillsDoSangue != 0
																		   && srv.DnaDeTeste(eu) is { Nucleo: "Braco esquerdo" }, 10);
		Conferir(chegou && (SkillRacial)cli.Atributos.SkillsDoSangue == srv.DnaDeTeste(eu)?.Sangue && cli.Atributos.SkillsRaciais == 0,
						"(premissa) o corpo do robo e um bio de laboratorio com o sangue do Cell: o cliente sabe as dezesseis skills do sangue (pela ficha "
						+ "lenta) e nenhuma escolhida -- e o nucleo mora no braco esquerdo");

		// ---- 0. o painel da escolha ----
		menu.Abrir();
		List<string> dna = await DnaNaAba(menu);
		Conferir(dna.Count == 16 && dna.Contains("DNA: Warrior Race") && dna.Contains("DNA: Unrestrained Wrath") && !dna.Contains("DNA: Prodigy"),
						$"A ABA SKILLS mostra os DEZESSEIS botoes da escolha -- 5 do Saiyajin, 3 do Namekuseijin, 4 do Frost Demon e 4 do Humano -- e nenhum do "
						+ $"meio-Saiyajin, que o Cell nao carrega ({dna.Count}: {string.Join(", ", dna.Take(4))}...)");
		await FotoDaBusca(menu, "DNA", "user://dnabio-0-escolha.png");

		// ---- 1. tres escolhas, pelo BOTAO ----
		foreach (string nome in new[] { "Warrior Race", "Second Wind", "Unrestrained Wrath" })
			Conferir(await Apertar(menu, $"DNA: {nome}"), $"o botao `DNA: {nome}` esta desenhado e o robo o aperta");
		bool tres = await Ate(() => (SkillRacial)cli.Atributos.SkillsRaciais == (SkillRacial.RacaGuerreira | SkillRacial.SegundoFolego | SkillRacial.Ira), 6);
		dna = await DnaNaAba(menu);
		HashSet<string> botoes = Botoes(menu);
		Conferir(tres && dna.Count == 13 && !dna.Contains("DNA: Warrior Race") && botoes.Contains("Second Wind") && botoes.Contains("Unrestrained Wrath")
						&& !botoes.Contains("Max Power") && botoes.Contains("Warrior Race  (do seu DNA)"),
						"TRES ESCOLHIDAS PELO BOTAO: os tres somem da escolha (sobram 13), as duas ATIVAS ganham o botao delas (Second Wind, Unrestrained "
						+ "Wrath) -- e o Max Power, do mesmo sangue humano, nao -- e a PASSIVA vira uma linha so de leitura (o botao apagado "
						+ "\"Warrior Race (do seu DNA)\")");
		await FotoDaBusca(menu, "DNA", "user://dnabio-1-tres.png");

		// ---- 2. mais tres: a escolha fecha ----
		foreach (string nome in new[] { "Seasoned Warrior", "Namekian Resilience", "Perfect Control" })
			await Apertar(menu, $"DNA: {nome}");
		SkillRacial seis = SkillRacial.RacaGuerreira | SkillRacial.SegundoFolego | SkillRacial.Ira | SkillRacial.GuerreiroVeterano
								   | SkillRacial.Resiliencia | SkillRacial.Controle;
		bool fechou = await Ate(() => (SkillRacial)cli.Atributos.SkillsRaciais == seis, 6);
		dna = await DnaNaAba(menu);
		Conferir(fechou && dna.Count == 0 && srv.DnaDeTeste(eu)?.Valem == seis
						&& (cli.Fatos & Jandirus.Core.Verbos.Fatos.SkillDoDnaPorEscolher) == 0,
						"A SEXTA FECHA: nenhum botao de DNA sobra na aba (e o fato da vaga apagou) -- o corpo vale as seis escolhidas, e so elas");
		await FotoDaBusca(menu, "DNA", "user://dnabio-2-completo.png");

		// ---- 3. o contra-exemplo: o sangue inteiro ----
		SkillsRaciais.BioComOSangueInteiroDeTeste = true;
		try
		{
			bool inteiro = await Ate(() => SkillsRaciais.Quantas((SkillRacial)cli.Atributos.SkillsRaciais) == 16, 6);
			await DnaNaAba(menu);
			HashSet<string> comDefeito = Botoes(menu);
			Conferir(inteiro && comDefeito.Contains("Max Power") && comDefeito.Contains("Sheer Willpower") && comDefeito.Contains("Spirit Disruption"),
							"CONTRA-EXEMPLO (defeito injetado: o bio ganha o sangue INTEIRO -- a uniao automatica): o Max Power, o Sheer Willpower e a Spirit "
							+ "Disruption, que ele NAO escolheu, aparecem na aba -- a regua do passo 1 reprovaria");
			Rolar(menu, "Max Power");
			await Segundos(0.4);
			Fotografar("user://dnabio-2a-sangue-inteiro.png");
		}
		finally { SkillsRaciais.BioComOSangueInteiroDeTeste = false; }
		await Ate(() => (SkillRacial)cli.Atributos.SkillsRaciais == seis, 6);

		// ---- 4. a ativa escolhida, pelo botao ----
		await DnaNaAba(menu);
		Conferir(await Apertar(menu, "Unrestrained Wrath"), "o botao da ATIVA `Unrestrained Wrath` esta desenhado e o robo o aperta");
		menu.Fechar();
		bool irado = await Ate(() => srv.DnaDeTeste(eu) is { Irado: true }, 4);
		await Segundos(0.6);
		Conferir(irado, "UNRESTRAINED WRATH (a ativa do sangue Icer que ele escolheu): o kiai sai e a ira toma conta -- a linha dela no HUD");
		Fotografar("user://dnabio-3-ira.png");

		// ---- 5. a passiva escolhida: a Warrior Race ----
		srv.FerirTodosDeTeste(eu, 0.15);
		await Segundos(1.2);
		Conferir(srv.DnaDeTeste(eu) is { Vida: < 20 } && cli.Sheet.Raciais.Contains("WARRIOR RACE", StringComparison.Ordinal),
						$"WARRIOR RACE (a passiva do sangue Saiyajin que ele escolheu): com a vida a 15% ela acende no HUD -- \"{cli.Sheet.Raciais.Replace('\n', '/')}\"");
		Fotografar("user://dnabio-4-warrior-race.png");
		srv.RestaurarDeTeste(eu);
		await Segundos(0.6);

		// ---- 6. a estocada ----
		int presa = srv.PresaNaFrenteDeTeste(eu, 9_460_001);
		Conferir(presa > 0, "(premissa) uma presa parada nasceu na frente do bio");
		if (presa > 0)
		{
			await Segundos(0.6);
			cli.SendHabilidade("agarrar");
			bool pegou = await Ate(() => srv.AgarrandoDeTeste(eu) == presa, 3);
			Conferir(pegou, "`agarrar` (pelo fio): o bio segura a presa");
			for (int i = 0; i < 4; i++)
			{
				cli.SendHabilidade("absorver");
				await Segundos(1.15);   // uma estocada por segundo
			}
			double[] m = srv.MembrosDaPresaDeTeste(presa);
			// (o golpe e NAO-LETAL -- o modo do robo --, e o nao-letal para o membro no limiar da quebra, 20%: a cauda o da por furado e passa)
			Conferir(m.Length == 5 && m[0] <= 0.2 + 1e-6 && m[1] is > 0.6 and < 0.8 && m[2] > 0.99,
							$"A ESTOCADA (o absorver agarrando, quatro vezes): a cauda fura o BRACO ESQUERDO ate a quebra e passa pro direito -- "
							+ $"[{string.Join(" ", m.Select(x => x.ToString("0.00")))}]");
			Fotografar("user://dnabio-5-estocada.png");
			cli.SendHabilidade("agarrar");   // solta
			await Segundos(0.4);
			srv.TirarDeTeste(presa);
		}
		srv.RestaurarDeTeste(eu);
		await Segundos(0.6);

		// ---- 7. o nucleo ----
		srv.MatarDeTeste(eu);
		bool negou = await Ate(() => srv.DnaDeTeste(eu) is { Caido: true, Morto: false, UsosDoNucleo: 1 }, 3);
		await Segundos(0.8);
		Conferir(negou, "O NUCLEO NEGA A MORTE (no braco esquerdo, inteiro): o corpo volta caido, com 5% de tudo");
		Fotografar("user://dnabio-6-nucleo.png");
		srv.RestaurarDeTeste(eu);
		await Segundos(0.6);
		srv.MatarDeTeste(eu);
		bool detonou = await Ate(() => srv.DnaDeTeste(eu) is { Morto: false, UsosDoNucleo: 2, BracoEsquerdoDecepado: true }, 3);
		await Segundos(0.8);
		Conferir(detonou, "...e na SEGUNDA ele reergue o corpo e DETONA: o braco esquerdo, onde ele morava, vai junto");
		Fotografar("user://dnabio-7-detonou.png");
		srv.RestaurarDeTeste(eu);
	}

	/// <summary>A aba Skills redesenhada, e os botoes de escolha do DNA nela.</summary>
	private async System.Threading.Tasks.Task<List<string>> DnaNaAba(MenuJogo menu)
	{
		if (!MenuJogo.Aberto) menu.Abrir();
		menu.IrPara(Verbos.Skills);
		menu.ForcarRedesenho();
		await Quadros(3);
		return [.. Botoes(menu).Where(t => t.StartsWith("DNA: ", StringComparison.Ordinal))];
	}

	private static HashSet<string> Botoes(MenuJogo menu) =>
		menu.PaginaDeTeste(Verbos.Skills) is { } pg
			? [.. pg.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text)]
			: [];

	/// <summary>
	/// A FOTO DO PAINEL PELA BUSCA DO MENU (o campo do topo, o de verdade): com "DNA" a tela mostra so os botoes da escolha e as passivas
	/// escolhidas, sem as descricoes compridas da aba inteira. A busca volta vazia depois da foto.
	/// </summary>
	private async Tarefa FotoDaBusca(MenuJogo menu, string termo, string destino)
	{
		LineEdit busca = menu.BuscaDeTeste;
		busca.Text = termo;
		busca.EmitSignal(LineEdit.SignalName.TextChanged, termo);
		await Segundos(0.4);
		Fotografar(destino);
		busca.Text = "";
		busca.EmitSignal(LineEdit.SignalName.TextChanged, "");
		await Quadros(3);
	}

	/// <summary>O botao da aba Skills com este texto, apertado como o jogador aperta (o sinal `pressed`). Devolve se ele estava la.</summary>
	private async System.Threading.Tasks.Task<bool> Apertar(MenuJogo menu, string texto)
	{
		await DnaNaAba(menu);
		if (menu.PaginaDeTeste(Verbos.Skills) is not { } pg) return false;
		Button? b = pg.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(x => x.Text == texto && x.IsVisibleInTree());
		if (b == null) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		await Segundos(0.5);   // o servidor responde e a ficha lenta volta
		return true;
	}

	/// <summary>Rola a aba ate o primeiro botao que comeca assim -- pra foto pegar ele.</summary>
	private static void Rolar(MenuJogo menu, string comeco)
	{
		if (menu.PaginaDeTeste(Verbos.Skills) is not { } pg) return;
		Button? b = pg.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(x => x.Text.StartsWith(comeco, StringComparison.Ordinal));
		if (b == null) return;
		for (Node? n = b.GetParent(); n != null; n = n.GetParent())
			if (n is ScrollContainer rolo) { rolo.EnsureControlVisible(b); return; }
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
		SkillsRaciais.BioComOSangueInteiroDeTeste = false;
		foreach (string l in _linhas) GD.Print("[ddnabio] " + l);
		GD.Print(_falhas.Count == 0 ? "[ddnabio] ===== TUDO OK =====" : $"[ddnabio] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[ddnabio]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
