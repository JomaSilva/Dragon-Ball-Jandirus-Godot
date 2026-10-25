using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO RITUAL MISTICO DO VELHO KAIOHSHIN (`--diagritualkai`, 2026-10-23) -- as fotos do pedido do dono (*"o jogador fica 10
/// minutos na frente do outro fazendo o ritual, nenhum pode se mover, ao terminar o potencial oculto desse jogador e despertado e ele ganha
/// a forma mistica (q deve ter o nome renomeado no jogo todo pra Potential Unleashed)"*). A regra e da `--ritualkaiteste`; aqui e a TELA,
/// com o robo no papel de Kaioshin (o botao) e depois no de quem recebe o ritual, em portugues e em ingles:
///   R1. o robo no trono do Kaioshin, com OUTRO Kaioshin na segunda vaga (dois de cinco): o botao "Ritual Mistico do Velho Kaiohshin"
///       na aba Skills -- foto `ritual-1-botao`; contra-exemplo `PosseDoCliente.DonoInteiroDeTeste` (com dois donos o botao sumia);
///   R2. um Kaioshin forjado ao lado aperta o ritual: o convite chega como cartao (o do Unlock Potential) -- foto `ritual-2-convite`;
///   R3. o "Aceitar" do cartao comeca o ritual: o chat diz, e o corpo local sabe que nao anda (o efeito `ritual_kaioshin` atravessou o fio)
///       -- foto `ritual-3-parados`; contra-exemplo `LocalPlayer.TravaDoRitualSoNoServidorDeTeste`;
///   R4. tres minutos de relogio de parede depois: o chat conta "3 de 10" -- foto `ritual-4-progresso`;
///   R5. no fim, o potencial desperta e a forma e dele: a tecla C o leva ao Potential Unleashed, e a aba Forms o mostra com o nome novo --
///       fotos `ritual-5-cena` e `ritual-6-forma`;
///   R6. o MESMO em ingles: o botao "Elder Kai's Unlock Ability", o cartao, o chat e a forma -- fotos `ritual-7-en-botao`,
///       `ritual-8-en-convite`, `ritual-9-en-parados` e `ritual-10-en-forma`.
///
///     Godot --path . --host --rede 10981 --horateste 0.5 --semfauna --diagritualkai --raca Human --conta bancada_ritualkai --nome Novico
/// </summary>
public partial class RoboDoRitualDoKaioshin : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
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

	public override void _ExitTree()
	{
		LocalPlayer.TravaDoRitualSoNoServidorDeTeste = false;
		PosseDoCliente.DonoInteiroDeTeste = false;
		Idioma.Aplicar(Idioma.Portugues);
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private void Ouvir(Protocol.Fala _, string __, string texto) => _ditas.Add(texto);

	private bool Ouviu(string trecho) => _ditas.Any(t => t.Contains(trecho, StringComparison.Ordinal));

	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu P nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		GD.Print("[dritualkai] ===== O RITUAL MISTICO DO VELHO KAIOHSHIN E O POTENTIAL UNLEASHED (2026-10-23) =====");
		cli.Falou += Ouvir;
		try
		{
			await Segundos(2.0);   // o mundo assenta (a primeira visita das abas pede listas ao servidor)
			// O OUTRO KAIOSHIN JA ESTA NO TRONO (a segunda vaga): quando o robo sobe, sao dois -- o caso do pedido, e o que a rodada
			// em ingles da r1 (2026-10-23) achou: com dois donos o botao do ritual sumia dos dois (`PosseDoCliente.TemCargo`).
			Conferir(srv.PalcoRk_OutroKaioshin(eu) != null, "R0. (montagem) o outro Kaioshin, forjado ao lado do robo, esta na segunda vaga do trono");
			await UmaRodada(cli, srv, menu, eu, ingles: false);

			// ---- R6. O MESMO EM INGLES ----
			srv.PalcoRk_TirarAForma(eu);
			await Segundos(1.0);
			Idioma.Aplicar(Idioma.Ingles);
			await Quadros(6);
			Conferir(Idioma.EmIngles, "R6. (montagem) o jogo esta em ingles");
			await UmaRodada(cli, srv, menu, eu, ingles: true);
		}
		finally
		{
			cli.Falou -= Ouvir;
			LocalPlayer.TravaDoRitualSoNoServidorDeTeste = false;
			PosseDoCliente.DonoInteiroDeTeste = false;
			Idioma.Aplicar(Idioma.Portugues);
			srv.PalcoRk_Limpar(eu);
		}
	}

	/// <summary>UMA RODADA INTEIRA (o botao, o convite, o aceite, o progresso, o fim e a forma) -- em portugues e depois em ingles.</summary>
	private async Tarefa UmaRodada(GameClient cli, Jandirus.Server.GameServer srv, MenuJogo menu, int eu, bool ingles)
	{
		string L(string codigo) => ingles ? $"R6{codigo}" : codigo;
		string foto(int n, string nome) => ingles ? $"user://ritual-{n + 6}-en-{nome}.png" : $"user://ritual-{n}-{nome}.png";
		string nomeDoRitual = ingles ? "Elder Kai's Unlock Ability" : RitualDoKaioshin.Nome;

		// ---- R1. O BOTAO DO KAIOSHIN ----
		srv.PalcoRk_ORoboEKaioshin(eu, sim: true);
		bool chegouOTrono = await Ate(() => cli.Cargos.Any(c => c.Chave == RitualDoKaioshin.Cargo && c.Dono.Contains(cli.LocalName)), 6);
		await Aba(menu, "Skills");
		bool botao = await Ate(() => BotaoDoRitual(menu, ingles) != null, 6);
		if (BotaoDoRitual(menu, ingles) is { } b) Mostrar(b);
		await Segundos(0.8);
		Conferir(chegouOTrono && botao,
				 $"{L("R1")}. o robo no trono do Kaioshin -- com OUTRO Kaioshin na segunda vaga -- ve o botao \"{nomeDoRitual}\" na aba Skills, "
				 + "o que o kit ganhou no lugar do Mistico");
		Fotografar(foto(1, "botao"));
		if (!ingles)
		{
			// O CONTRA-EXEMPLO DO DONO INTEIRO: com ele, o `Dono` "Novico, Kaioshin" nao e o robo, e o cargo (e o botao) some.
			PosseDoCliente.DonoInteiroDeTeste = true;
			bool semCargo;
			try { semCargo = !PosseDoCliente.Atual.TemCargo(RitualDoKaioshin.Cargo); }
			finally { PosseDoCliente.DonoInteiroDeTeste = false; }
			Conferir(semCargo && PosseDoCliente.Atual.TemCargo(RitualDoKaioshin.Cargo),
					 "R1x. (defeito injetado: o dono do cargo comparado inteiro) a regua do R1 REPROVA -- com dois Kaioshins, o robo deixa de ter o cargo");
		}
		menu.Fechar();
		srv.PalcoRk_ORoboEKaioshin(eu, sim: false);
		await Segundos(1.0);

		// ---- R2. O CONVITE ----
		_ditas.Clear();
		bool ofereceu = srv.PalcoRk_OKaioshinOferece(eu);
		bool chegou = await Ate(() => cli.Pedidos.Any(p => p.Fato == Fatos.OfertaDePotencial && p.Texto.Contains(RitualDoKaioshin.Nome)), 6);
		string cartao = cli.Pedidos.FirstOrDefault(p => p.Fato == Fatos.OfertaDePotencial).Texto ?? "";
		await Segundos(1.0);
		Conferir(ofereceu && chegou && Idioma.T(cartao).Contains(nomeDoRitual, StringComparison.Ordinal),
				 $"{L("R2")}. **o convite do ritual chega como cartao** (o do Unlock Potential), com o nome do ritual na lingua da tela [{Idioma.T(cartao)}]");
		Fotografar(foto(2, "convite"));

		// ---- R3. O ACEITE: OS DOIS PARADOS ----
		_ditas.Clear();
		// (pelo texto da FONTE: em ingles a tela mostra "Accept", mas o `Text` do botao continua "Aceitar" -- quem traduz e o Godot)
		Button? aceitar = BotaoNoPainelDePedidos("Aceitar");
		aceitar?.EmitSignal(BaseButton.SignalName.Pressed);
		string comecou = ingles ? "the ritual has begun" : "o ritual começou";
		bool comecouNoChat = await Ate(() => Ouviu(comecou), 5);
		bool travado = await Ate(() => Corpo?.PorQueNaoAnda.Contains("Ritual", StringComparison.Ordinal) == true, 3);
		var estado = srv.PalcoRk_Estado(eu);
		await Segundos(1.0);
		Conferir(aceitar != null && comecouNoChat && travado && estado.EmRitual,
				 $"{L("R3")}. o \"Aceitar\" do cartao comeca o ritual: o chat diz, e o corpo local sabe que nao anda [{Corpo?.PorQueNaoAnda}] "
				 + $"[ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
		Fotografar(foto(3, "parados"));

		if (!ingles)
		{
			// O CONTRA-EXEMPLO DA TRAVA NO CLIENTE: com ele, o corpo local nao sabe que esta preso (preveria o passo e seria puxado de volta).
			LocalPlayer.TravaDoRitualSoNoServidorDeTeste = true;
			bool semTrava;
			try { semTrava = Corpo is { } c && !c.PorQueNaoAnda.Contains("Ritual", StringComparison.Ordinal); }
			finally { LocalPlayer.TravaDoRitualSoNoServidorDeTeste = false; }
			Conferir(semTrava, "R3x. (defeito injetado: a trava do ritual so no servidor) a regua do R3 REPROVA -- o corpo local nao sabe que esta preso");

			// ---- R4. O PROGRESSO ----
			_ditas.Clear();
			srv.PalcoRk_AndarORelogio(3 * RitualDoKaioshin.AvisoACadaMs + 1_000);
			bool tres = await Ate(() => Ouviu("3 de 10 minutos"), 4);
			await Segundos(0.8);
			Conferir(tres && srv.PalcoRk_Estado(eu).EmRitual, "R4. tres minutos depois o chat conta \"3 de 10 minutos\" -- e o ritual continua");
			Fotografar(foto(4, "progresso"));
		}

		// ---- R5. O FIM, E A FORMA ----
		_ditas.Clear();
		srv.PalcoRk_AndarORelogio(RitualDoKaioshin.DuracaoMs + 1_000);
		string suaForma = ingles ? "Potential Unleashed" : "o Potential Unleashed é seu";
		bool fim = await Ate(() => Ouviu(suaForma), 5);
		bool solto = await Ate(() => Corpo?.PorQueNaoAnda.Contains("Ritual", StringComparison.Ordinal) == false, 3);
		var depois = srv.PalcoRk_Estado(eu);
		Conferir(fim && solto && depois.TemAForma && depois.Potencial && !depois.EmRitual,
				 $"{L("R5")}. no fim o potencial desperta e o Potential Unleashed e dele -- o chat diz, e o corpo se solta "
				 + $"[ouvi: {string.Join(" | ", _ditas.TakeLast(3))}]");

		cli.SendTransformar(true);
		bool virou = await Ate(() => cli.Atributos.FormaAtual == Catalogo.Rede(Catalogo.IdMistico), 8);
		await Segundos(2.0);
		if (!ingles) Fotografar(foto(5, "cena"));
		await Segundos(6.0);   // a cena do ritual divino acaba
		await Aba(menu, "Forms");
		bool naAba = await Ate(() => menu.PaginaDeTeste("Forms") is { } pg && TemTexto(pg, "Potential Unleashed"), 5);
		await Segundos(0.8);
		Conferir(virou && naAba,
				 $"{L("R5b")}. a tecla C leva ao Potential Unleashed, e a aba Forms o mostra com o nome novo (e nao \"Mistico\") [forma {cli.Atributos.FormaAtual}]");
		Fotografar(foto(ingles ? 4 : 6, "forma"));
		menu.Fechar();
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	/// <summary>O botao do ritual na aba Skills -- pelo texto da fonte (portugues); em ingles, o que a tela mostra e a traducao dele.</summary>
	private static Button? BotaoDoRitual(MenuJogo menu, bool ingles) =>
		menu.PaginaDeTeste("Skills") is { } pg
			? Todos(pg).OfType<Button>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.IsVisibleInTree()
				&& b.Text == RitualDoKaioshin.Nome && (!ingles || Idioma.T(b.Text) == "Elder Kai's Unlock Ability"))
			: null;

	/// <summary>A foto mostra o botao: ele pode morar abaixo da dobra da aba.</summary>
	private static void Mostrar(Control c)
	{
		for (Node? n = c.GetParent(); n != null; n = n.GetParent())
			if (n is ScrollContainer rolagem) { rolagem.EnsureControlVisible(c); break; }
	}

	private Button? BotaoNoPainelDePedidos(string texto) =>
		GetTree().Root.FindChild("Pedidos", true, false) is { } painel
			? Todos(painel).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase))
			: null;

	private static bool TemTexto(Node raiz, string trecho) =>
		Todos(raiz).OfType<Label>().Any(l => !l.IsQueuedForDeletion() && l.Text.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
		await Segundos(0.9);   // a primeira visita de uma aba pode pedir listas ao servidor e remontar
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
		foreach (string l in _linhas) GD.Print("[dritualkai] " + l);
		GD.Print($"[dritualkai] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dritualkai] ===== TUDO OK =====" : $"[dritualkai] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dritualkai]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
