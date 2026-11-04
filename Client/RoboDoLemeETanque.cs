using Godot;
using Jandirus.Core.Tech;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO LEME E DO TANQUE (`--diagpodtanque`, 2026-10-31): as fotos de tres pedidos do dono num recado so --
///   *"Ta dando pra apertar shift na pod ou capital ship pra fazer ela voar mais rapido gastando ki (oq n deveria acontecer) e alem disso vc
///   sair do jogo pilotando uma pod ou nave e entrando em outro slot, o jogo acha q vc ta em uma pod mesmo n estando no outro slot. E adicione
///   uma forma de melhoria na pod e capital ship de aumentar o tanque de combustivel"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra:
///   T1. (o pedido 209) um pod meu ao lado: a tecla E mostra "Melhorar tanque" e a gemea "(com recursos)" -- foto `dpodtanque-1-menu`; o
///       botao sobe o tanque de 1.000 pra 1.500 por 5.000 zeni, e o chat diz que o espaco novo vem vazio;
///   T2. "Entrar": a barra do canto diz 1.000 / 1.500 -- foto `dpodtanque-2-barra`; e o "Abastecer" enche ate os 1.500;
///   T3. (o pedido 207) no ar, segurando a seta com e sem o shift: o cliente nao pede a corrida, o servidor nao a concede, e o pod anda o
///       mesmo tanto -- foto `dpodtanque-3-shift-no-ar`;
///   T4. ...no espaco, o mesmo -- e o Ki do piloto nao desce (la fora o shift era a corrida do chao, paga em Ki) -- foto
///       `dpodtanque-4-shift-no-espaco`;
///   T5. (o pedido 208) sai do jogo PILOTANDO (a conexao cai pelo fio, o relog da saga do Majin) e volta com OUTRO personagem da conta, no
///       slot 2: nada de nave -- sem veiculo, sem a barra do tanque, sem o menu de dentro do pod na tecla E, e o corpo anda no chao -- foto
///       `dpodtanque-5-outro-slot`;
///   T6. (o pedido 209, na Capital Ship) o outro personagem ao leme de uma Capital Ship: a tecla E tem "Melhorar tanque", e a barra vai de
///       5.000 / 5.000 a 5.000 / 7.500 -- foto `dpodtanque-6-capital`.
///
/// O contra-exemplo de cada conserto e a MUTACAO do ciclo (`ag/podtanque`): sem a linha do cliente o T3/T4 reprovam (o cliente pede a
/// corrida), sem a do servidor a `--naveteste` reprova (a camada 12), e sem a do `JoinAccepted` o T5 reprova (o outro personagem herda a nave).
///
/// ELE SE MUDA PRA RAIZ DA ARVORE (o `RoboDeQueda`, o `RoboDaSagaMajin`): a queda derruba os filhos do `Boot`, e a volta ao mundo passa outra
/// vez pelo `Boot.MontarMundo`, que so nao cria um segundo robo por causa do <see cref="Vivo"/>.
///
///     Godot --path . --host --rede 12741 --campoteste 8 --horateste 0.5 --semfauna --diagpodtanque --raca Human --conta bancada_podtanque --nome Piloto
/// </summary>
public partial class RoboDoLemeETanque : Node
{
	/// <summary>Ja ha um robo vivo? A volta com o outro personagem passa de novo pelo `MontarMundo`, que o criaria outra vez.</summary>
	public static bool Vivo { get; private set; }

	/// <summary>O nome do personagem do slot 2, o que entra depois da saida pilotando.</summary>
	private const string NomeDoOutro = "Outro";

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
	private Boot? _boot;
	private bool _comecou;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _EnterTree()
	{
		Vivo = true;
		if (GetParent() is Boot boot) _boot = boot;
	}

	public override void _Ready()
	{
		// a mudanca pra raiz passa de novo por aqui (sai de um pai, entra no outro): o roteiro comeca uma vez so
		if (_comecou) return;
		_comecou = true;
		ProcessMode = ProcessModeEnum.Always;
		CallDeferred(nameof(Emancipar));
		_ = Rodar();
	}

	public override void _ExitTree()
	{
		// SO NA SAIDA DE VERDADE: a mudanca pra raiz tambem passa por aqui.
		if (IsQueuedForDeletion()) Vivo = false;
	}

	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;
		Node raiz = arv.Root;   // antes do `RemoveChild`: fora da arvore o `GetTree()` devolve nulo
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu da tecla E nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dpodtanque] ===== O LEME E O TANQUE: O SHIFT, O OUTRO SLOT E O TANQUE MAIOR (dono, 2026-10-31) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		cli.Falou += Ouvir;
		try
		{
			if (await OTanqueDoPod(cli, srv, eu) && await OShiftAoLeme(cli, srv, eu))
			{
				int outro = await OOutroSlot(cli, srv, eu);
				if (outro > 0) await ACapitalShip(cli, srv, outro);
			}
		}
		finally
		{
			cli.Falou -= Ouvir;
			Boot.SlotDoAutoDeTeste = null;
			if (cli.LocalId > 0) srv.PalcoCS_Limpar(cli.LocalId);
		}
	}

	/// <summary>O que o servidor disse a este cliente (os avisos chegam pelo canal de fala).</summary>
	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Any(t => t.Contains(trecho, StringComparison.Ordinal));

	private string UltimasDitas() => string.Join(" | ", _ditas.TakeLast(3));

	private static string Mil(double v) => v.ToString("N0");

	// =====================================================================
	// T1-T2) O TANQUE MAIOR DO POD (o pedido 209)
	// =====================================================================
	private async System.Threading.Tasks.Task<bool> OTanqueDoPod(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		srv.PalcoPT_Bolso(eu, 60, 1_000_000, 100_000);
		int pod = srv.PalcoPT_Pod(eu);
		bool naTela = pod > 0 && await Ate(() => cli.Obras.Any(o => o.Id == -pod), 8);
		await Segundos(1.0);
		Conferir(naTela, "(montagem) um pod meu, parado a um tile, com 60 de tecnologia, zeni e recursos no bolso");
		if (!naTela) return false;

		// ---------------------------------------------------------------- T1: a tecla E mostra o tanque, e o botao o sobe
		ApertarE();
		bool menu = await Ate(() => MenuDeInteracao.Instancia is { NaTela: true }, 3);
		await Quadros(4);
		List<string> botoes = MenuDeInteracao.Instancia?.BotoesDesenhados() ?? [];
		Fotografar("user://dpodtanque-1-menu.png");
		Conferir(menu && botoes.Contains("Melhorar tanque") && botoes.Contains("Melhorar tanque (com recursos)") && botoes.Contains("Melhorar velocidade"),
				 "T1. A TECLA E NO POD TEM O TANQUE MAIOR (*\"adicione uma forma de melhoria na pod ... de aumentar o tanque de combustivel\"*): \"Melhorar "
				 + $"tanque\" e a gemea \"(com recursos)\", ao lado da velocidade [{string.Join(", ", botoes)}]");
		_ditas.Clear();
		bool apertou = menu && MenuDeInteracao.Instancia!.ApertarDesenhado("Melhorar tanque");
		bool subiu = apertou && await Ate(() => srv.PalcoPT_Tanque(pod).Degraus == 1 && Disse("agora leva"), 5);
		(int degraus, double nivel, double cheio) = srv.PalcoPT_Tanque(pod);
		Conferir(subiu && Math.Abs(cheio - 1_500) < 1e-6 && Math.Abs(nivel - 1_000) < 1e-6 && Disse("O espaço novo vem vazio"),
				 "T1b. ...E O BOTAO SOBE O TANQUE: de 1.000 pra 1.500, por 5.000 zeni, e o chat diz que o espaco novo vem vazio (o pod continua com os 1.000 "
				 + $"que tinha) [degrau {degraus}; tanque {nivel:0} de {cheio:0}; ouvi: {UltimasDitas()}]");
		await Segundos(0.5);

		// ---------------------------------------------------------------- T2: dentro, a barra conta o tanque novo; abastecer enche ate ele
		bool dentro = await PeloMenu("Entrar", () => cli.VeiculoMontado.Length > 0 && cli.Tanque is { Modo: 1 } && Math.Abs(cli.Tanque.Capacidade - 1_500) < 1);
		await Segundos(1.0);
		string barra = PainelDeCombustivel.Instancia?.TextoDeTeste ?? "";
		Fotografar("user://dpodtanque-2-barra.png");
		// (a r1 viu "999 / 1.500": ao embarcar o pod vem ate o piloto, e esse passo de um tile gasta 0,05 -- a barra arredonda pra baixo)
		Conferir(dentro && cli.Tanque.Nivel is >= 990f and <= 1_000f && barra.Contains($" / {Mil(1_500)}", StringComparison.Ordinal),
				 $"T2. DENTRO DO POD, A BARRA CONTA O TANQUE NOVO: os {Mil(1_000)} que ele tinha (menos o fio do embarque) de {Mil(1_500)} "
				 + $"[dentro {dentro}; \"{barra}\"]");
		bool encheu = await PeloMenu("Abastecer", () => cli.Tanque.Nivel >= 1_499);
		await Quadros(6);
		Conferir(encheu && (PainelDeCombustivel.Instancia?.TextoDeTeste ?? "").Contains($"{Mil(1_500)} / {Mil(1_500)}", StringComparison.Ordinal),
				 $"T2b. ...e o \"Abastecer\" enche ate os {Mil(1_500)} [\"{PainelDeCombustivel.Instancia?.TextoDeTeste}\"; ouvi: {UltimasDitas()}]");
		return dentro;
	}

	// =====================================================================
	// T3-T4) O SHIFT AO LEME (o pedido 207)
	// =====================================================================
	private async System.Threading.Tasks.Task<bool> OShiftAoLeme(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		bool noAr = srv.PalcoCS_PodNoAr(eu);
		await Segundos(1.5);   // a subida
		(float semShift, _, _) = await Segurar(eu, srv, comShift: false, 1.0);
		(float comShift, bool pediuNoAr, bool deuNoAr) = await Segurar(eu, srv, comShift: true, 1.0, "user://dpodtanque-3-shift-no-ar.png");
		Conferir(noAr && semShift > 20 && comShift < semShift * 1.15f && !pediuNoAr && !deuNoAr,
				 "T3. NO AR, O SHIFT NAO ACELERA O POD (*\"Ta dando pra apertar shift na pod ... pra fazer ela voar mais rapido\"*): o cliente nao pede a corrida, "
				 + $"o servidor nao a concede, e o pod anda o mesmo tanto com e sem ele [no ar {noAr}; sem shift {semShift:0} px, com shift {comShift:0} px em 1 s; "
				 + $"o cliente pediu {pediuNoAr}; o servidor deu {deuNoAr}]");

		bool noEspaco = srv.PalcoCS_PodNoEspaco(eu, "Earth") && await Ate(() => Jandirus.Core.World.Espaco.EhEspaco(cli.Zone)
																				 && TelaDeCarregamento.Instancia?.NoAr != true, 15);
		await Segundos(1.5);
		srv.PalcoPT_KiCheio(eu);
		await Segundos(0.3);
		(_, _, double kiAntes, double kiMax) = srv.PalcoPT_Shift(eu);
		(float noVazio, bool pediuLaFora, bool deuLaFora) = await Segurar(eu, srv, comShift: true, 2.0, "user://dpodtanque-4-shift-no-espaco.png");
		(_, _, double kiDepois, _) = srv.PalcoPT_Shift(eu);
		Conferir(noEspaco && noVazio > 20 && !pediuLaFora && !deuLaFora && kiDepois >= kiAntes - 1e-6,
				 "T4. NO ESPACO, O SHIFT NAO GASTA O KI DO PILOTO (*\"... gastando ki (oq n deveria acontecer)\"*): dois segundos de shift e seta no vazio, e o Ki "
				 + $"fica cheio -- antes, la fora, o shift era a corrida do chao, a 2% do Ki por segundo [no espaco {noEspaco}; andou {noVazio:0} px; "
				 + $"o cliente pediu {pediuLaFora}; o servidor deu {deuLaFora}; Ki {kiAntes:0} -> {kiDepois:0} de {kiMax:0}]");
		return noEspaco;
	}

	/// <summary>
	/// SEGURA A SETA PRA LESTE (e o shift, se pedido) por <paramref name="segundos"/>, como o jogador: devolve o que o corpo andou na tela, se
	/// o cliente pediu a corrida em algum quadro e se o servidor a concedeu (a corrida ou a marcha rapida do voo). A foto sai no meio.
	/// </summary>
	private async System.Threading.Tasks.Task<(float Andou, bool Pediu, bool Deu)> Segurar(int eu, Jandirus.Server.GameServer srv, bool comShift,
																						   double segundos, string? foto = null)
	{
		string direita = Teclas.NomeNoMotor("move_right"), correr = Teclas.NomeNoMotor("run");
		bool pediu = false, deu = false;
		Vector2 antes = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		if (comShift) Godot.Input.ActionPress(correr);
		Godot.Input.ActionPress(direita);
		try
		{
			double fim = Time.GetTicksMsec() / 1000.0 + segundos;
			bool fotografou = foto == null;
			while (Time.GetTicksMsec() / 1000.0 < fim)
			{
				await Quadros(1);
				pediu |= World.Instancia?.CorrendoLocalDeTeste == true;
				(bool correndo, bool rapido, _, _) = srv.PalcoPT_Shift(eu);
				deu |= correndo || rapido;
				if (!fotografou && Time.GetTicksMsec() / 1000.0 > fim - segundos / 2) { Fotografar(foto!); fotografou = true; }
			}
		}
		finally
		{
			Godot.Input.ActionRelease(direita);
			Godot.Input.ActionRelease(correr);
		}
		await Quadros(2);
		return (((World.Instancia?.PosicaoLocal ?? Vector2.Zero) - antes).Length(), pediu, deu);
	}

	// =====================================================================
	// T5) O OUTRO SLOT (o pedido 208)
	// =====================================================================
	private async System.Threading.Tasks.Task<int> OOutroSlot(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		bool pilotando = cli.VeiculoMontado.Length > 0 && cli.Tanque.Modo == 1;
		Boot.SlotDoAutoDeTeste = (1, NomeDoOutro);
		bool caiu = srv.DerrubarDeBancada(eu);
		bool aviso = caiu && await Ate(() => _boot?.AvisoDeQuedaNaTela() != null, 10);
		bool ok = aviso && _boot!.ApertarOkDoAvisoDeQueda();
		await Segundos(1.0);
		_boot?.DiscarDeNovoDeTeste();
		bool voltou = ok && await Ate(() => C is { Connected: true, LocalId: > 0 } c && c.LocalName == NomeDoOutro && _boot is { MundoMontado: true }
											&& World.Instancia is { } w && IsInstanceValid(w) && w.PosicaoLocal != null
											&& TelaDeCarregamento.Instancia?.NoAr != true, 45);
		Boot.SlotDoAutoDeTeste = null;
		Conferir(pilotando && voltou, "(montagem) sai do jogo PILOTANDO o pod no espaco (a conexao cai pelo fio) e volta com OUTRO personagem da conta, "
									  + $"\"{NomeDoOutro}\", no slot 2 [pilotava {pilotando}; caiu {caiu}; aviso {aviso}; voltou {voltou}; sou {cli.LocalName}]");
		if (!voltou) return 0;
		await Segundos(2.5);   // o mundo assenta e os primeiros pacotes chegam -- o servidor nao manda nada de nave a quem nao pilota

		bool semVeiculo = cli.VeiculoMontado.Length == 0 && cli.NomeDoVeiculo.Length == 0 && !cli.Observador;
		bool semBarra = cli.Tanque.Modo == 0 && PainelDeCombustivel.Instancia?.NaTela != true;
		ApertarE();
		await Segundos(0.6);
		List<string> botoes = MenuDeInteracao.Instancia?.BotoesDesenhados() ?? [];
		string titulo = MenuDeInteracao.Instancia?.TituloDesenhado ?? "";
		if (MenuDeInteracao.Instancia is { NaTela: true }) { ApertarE(); await Segundos(0.3); }
		bool semMenuDoPod = !botoes.Contains("Sair da nave") && !botoes.Contains("Abastecer");

		string direita = Teclas.NomeNoMotor("move_right");
		Vector2 antes = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		Godot.Input.ActionPress(direita);
		try { await Segundos(1.0); Fotografar("user://dpodtanque-5-outro-slot.png"); }
		finally { Godot.Input.ActionRelease(direita); }
		float andou = ((World.Instancia?.PosicaoLocal ?? Vector2.Zero) - antes).Length();
		Conferir(semVeiculo && semBarra && semMenuDoPod && andou > 40,
				 "T5. O OUTRO SLOT NAO HERDA A NAVE (*\"vc sair do jogo pilotando uma pod ou nave e entrando em outro slot, o jogo acha q vc ta em uma pod\"*): "
				 + "sem veiculo e sem a barra do tanque, a tecla E nao abre o menu de dentro do pod, e o corpo anda no chao "
				 + $"[veiculo \"{cli.VeiculoMontado}\"; barra {cli.Tanque.Modo}/{PainelDeCombustivel.Instancia?.NaTela}; menu \"{titulo}\": "
				 + $"{string.Join(", ", botoes)}; andou {andou:0} px em 1 s]");
		return cli.LocalId;
	}

	// =====================================================================
	// T6) O TANQUE MAIOR DA CAPITAL SHIP (o pedido 209)
	// =====================================================================
	private async Tarefa ACapitalShip(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		srv.PalcoPT_Bolso(eu, 60, 1_000_000, 100_000);
		int nave = srv.PalcoCS_AoLeme(eu);
		bool aoLeme = nave > 0 && await Ate(() => cli.VeiculoMontado == NaveGrande.Tipo && cli.Tanque is { Modo: 1, Capacidade: > 0 }
												  && TelaDeCarregamento.Instancia?.NoAr != true, 15);
		await Segundos(1.5);
		Conferir(aoLeme, "(montagem) o outro personagem ao leme de uma Capital Ship dele, com o tanque de fabrica (5.000)");
		if (!aoLeme) return;
		_ditas.Clear();
		bool cresceu = await PeloMenu("Melhorar tanque", () => Math.Abs(cli.Tanque.Capacidade - 7_500) < 1);
		await Quadros(8);
		string barra = PainelDeCombustivel.Instancia?.TextoDeTeste ?? "";
		Fotografar("user://dpodtanque-6-capital.png");
		(int degraus, double nivel, double cheio) = srv.PalcoPT_Tanque(nave);
		// (a r1 viu "4.999 / 7.500": o leme poe o piloto em cima do casco, e esse passo de nada ja gasta um fio -- a barra arredonda pra baixo)
		Conferir(cresceu && degraus == 1 && nivel is > 4_990 and <= 5_000 && Math.Abs(cheio - 7_500) < 1e-6
				 && barra.Contains($" / {Mil(7_500)}", StringComparison.Ordinal) && Disse("O espaço novo vem vazio"),
				 "T6. A CAPITAL SHIP TAMBEM (*\"na pod e capital ship\"*): ao leme, a tecla E tem \"Melhorar tanque\", e o tanque vai de 5.000 a 7.500 (por 25.000 "
				 + $"zeni) com o espaco novo vazio -- a barra conta os {Mil(7_500)}, com os ~{Mil(5_000)} que ela tinha [\"{barra}\"; degrau {degraus}; tanque "
				 + $"{nivel:0.##}; ouvi: {UltimasDitas()}]");
	}

	/// <summary>A TECLA E, e o botao do menu que ela abriu -- como o jogador: devolve se o efeito chegou (a condicao) em ate 15 s.</summary>
	private async System.Threading.Tasks.Task<bool> PeloMenu(string botao, Func<bool> chegou)
	{
		if (MenuDeInteracao.Instancia is not { NaTela: true })
		{
			ApertarE();
			await Ate(() => MenuDeInteracao.Instancia is { NaTela: true }, 3);
		}
		bool apertou = MenuDeInteracao.Instancia is { NaTela: true } m && m.ApertarDesenhado(botao);
		if (!apertou) _linhas.Add($"  --     o botao \"{botao}\" nao estava no menu da tecla E [{string.Join(", ", MenuDeInteracao.Instancia?.BotoesDesenhados() ?? [])}]");
		return apertou && await Ate(chegou, 15);
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	/// <summary>A TECLA E de verdade: apertada e solta, empurrada no viewport (o caminho do `_UnhandledInput`, como o robo do embarque).</summary>
	private void ApertarE()
	{
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = false });
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
		Boot.SlotDoAutoDeTeste = null;
		Godot.Input.ActionRelease(Teclas.NomeNoMotor("move_right"));
		Godot.Input.ActionRelease(Teclas.NomeNoMotor("run"));
		foreach (string l in _linhas) GD.Print("[dpodtanque] " + l);
		GD.Print(_falhas.Count == 0 ? "[dpodtanque] ===== TUDO OK =====" : $"[dpodtanque] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dpodtanque]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
