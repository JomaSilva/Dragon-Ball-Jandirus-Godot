using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA MAESTRIA DO INSTANT TRANSMISSION (<c>--diagitmaestria</c>, dono 2026-10-23). Pelo botao de producao da aba Sense, no
/// relogio de verdade (o servidor tica sozinho no mesmo processo; ninguem vence a concentracao na mao):
///   R1. o cartao "Instant Transmission" na aba Sense com a maestria em 0% (foto);
///   R2. a viagem de maestria 0 leva os 3 s de sempre (nivel 3), e o cartao passa a 2%;
///   R3. o veterano (98%) viaja e chega a 100%: o cartao diz 0,2 s no campo de visao e um quarto do Ki, e o chat avisa (foto);
///   R4. a LUTA: o Rival chega e soca (foto); R5. no meio dela o jogador some e aparece do lado do Aliado em ~0,2 s (foto);
///   R6. e volta pra cima do Rival, tambem em ~0,2 s (foto);
///   R7. CONTRA-EXEMPLO: com a maestria desligada no servidor (os dois defeitos injetados) a mesma viagem leva os 3 s de sempre --
///       a regua do R5 reprova.
///   R8/R9. (2026-10-24, o dono: "Fazer igual no IT") QUEM ESTA PERTO VE O CLARAO ENTRE ZONAS: o Viajante do palco vai pra Namek e
///       quem fica o ve PISCAR antes de sumir (foto); ele volta, e quem esta no destino o ve chegar PISCANDO (foto). Cada um com o
///       contra-exemplo dele (o corpo que some no tique do clarao; o clarao da chegada antes do corpo).
/// Desde 2026-10-24 toda viagem leva a concentracao MAIS os 0,2 s piscando antes de sumir.
/// </summary>
public partial class RoboDaMaestriaDaTransmissao : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>
	/// O teto da viagem rapida medida no relogio de parede: 0,2 s da concentracao + 0,2 s piscando antes de sumir + um tique (33 ms) e
	/// os quadros do robo. Sem a pressa do campo de visao a mesma viagem passa de 0,8 s.
	/// </summary>
	private const double TetoDaViagemRapidaS = 0.65;

	/// <summary>Quantos quadros o corpo tem de ser VISTO piscando pra a saida contar como vista (o salto espera 0,2 s pra sumir).</summary>
	private const int QuadrosQueContamComoVisto = 3;

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		// os defeitos do servidor nunca ficam ligados depois do robo, nem se ele cair no meio
		Jandirus.Server.GameServer.MaestriaDoItNaoEncurtaDeTeste = false;
		Jandirus.Server.GameServer.MaestriaDoItSemPressaDePertoDeTeste = false;
		Jandirus.Server.GameServer.TransmissaoSomeNaHoraDeTeste = false;
		Jandirus.Server.GameServer.TransmissaoClaraoNaHoraDeTeste = false;
	}

	private async Tarefa Rodar()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 20);
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu)
		{
			Conferir(false, "(montagem) o host entrou, com mundo, servidor e menu");
			Fechar();
			return;
		}
		try
		{
			Conferir(srv.PalcoMIT_Montar(cli.LocalId), "(montagem) o palco armou o Sense, a skill no nivel 3, a maestria em 0 e os dois conhecidos");
			bool lista = await Ate(() => cli.NivelDaTransmissao >= 3 && cli.Transmissao.TemMaestria
										 && cli.Sentidos.Exists(p => p.Corpo == srv.PalcoMIT_Id("Aliado")), 6);
			Conferir(lista, $"(montagem) a lista do Sense e o cartao do IT chegaram (nivel {cli.NivelDaTransmissao}, {cli.Transmissao})");
			await OCartaoEmZero(menu);
			await AViagemDeSempre(cli, menu, srv);
			await OVeterano(cli, menu, srv);
			await ALuta(cli, menu, srv);
			await OContraExemplo(cli, menu, srv);
			await QuemEstaPertoVe(cli, menu, srv);
		}
		catch (Exception e)
		{
			Conferir(false, $"(montagem) o robo estourou: {e.GetType().Name}: {e.Message}");
		}
		finally
		{
			srv.PalcoMIT_Limpar(cli.LocalId);
		}
		Fechar();
	}

	// ---------------------------------------------------------------- R1
	private async Tarefa OCartaoEmZero(MenuJogo menu)
	{
		await Sense(menu);
		bool cartao = await Ate(() => Valor(menu, MenuJogo.RotuloDaMaestriaDoIt) == "0%", 3);
		string concentracao = Valor(menu, "concentração") ?? "";
		string ki = Valor(menu, "custo de Ki") ?? "";
		Conferir(cartao && Cartao(menu) && concentracao.StartsWith("3", StringComparison.Ordinal) && ki.StartsWith("100%", StringComparison.Ordinal),
			$"R1. a aba Sense tem o cartao 'Instant Transmission' com a maestria em 0%: 3 s de concentracao e o Ki inteiro, como sempre [{concentracao} | {ki}]");
		Fotografar("user://itm-1-cartao-zero.png");
	}

	// ---------------------------------------------------------------- R2
	private async Tarefa AViagemDeSempre(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		double? s = await Viajar(menu, srv, "Aliado", 6, fecharOMenu: false);
		Conferir(s is >= 2.7 and <= 4.0, $"R2. com maestria 0 a viagem leva os 3 s de sempre (nivel 3): {Seg(s)}");
		bool subiu = await Ate(() => Valor(menu, MenuJogo.RotuloDaMaestriaDoIt) == "2%", 4);
		Conferir(subiu && Math.Abs(srv.PalcoMIT_Maestria(cli.LocalId) - 2) < 1e-9,
			$"R2b. ...e chegou: a maestria sobe a 2% no servidor e no cartao [{Valor(menu, MenuJogo.RotuloDaMaestriaDoIt)}]");
	}

	// ---------------------------------------------------------------- R3
	private async Tarefa OVeterano(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		srv.PalcoMIT_PorMaestria(cli.LocalId, 98);
		bool noCartao = await Ate(() => Valor(menu, MenuJogo.RotuloDaMaestriaDoIt) == "98%", 4);
		Conferir(noCartao, $"(montagem) o veterano: a maestria posta em 98% chega ao cartao [{Valor(menu, MenuJogo.RotuloDaMaestriaDoIt)}]");
		double? s = await Viajar(menu, srv, "Rival", 3, fecharOMenu: false);
		bool cem = await Ate(() => Valor(menu, MenuJogo.RotuloDaMaestriaDoIt) == "100%", 4);
		string concentracao = Valor(menu, "concentração") ?? "";
		string ki = Valor(menu, "custo de Ki") ?? "";
		bool avisou = Chat.LinhasDeTeste().Exists(l => l.Texto.Contains("domina o Instant Transmission", StringComparison.Ordinal)
													  || l.Texto.Contains("mastered Instant Transmission", StringComparison.Ordinal));
		Conferir(s != null && cem && avisou && (concentracao.Contains("0,2 s no") || concentracao.Contains("0.2 s")) && ki.StartsWith("25%", StringComparison.Ordinal),
			$"R3. a viagem do veterano ({Seg(s)}) fecha a maestria em 100%: o chat avisa, e o cartao diz 0,2 s no campo de visao e um quarto do Ki [{concentracao} | {ki}]");
		await Quadros(4);
		Fotografar("user://itm-2-cartao-cem.png");
	}

	// ---------------------------------------------------------------- R4-R6
	private async Tarefa ALuta(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		menu.Fechar();
		bool luta = srv.PalcoMIT_Luta(cli.LocalId);
		// (o golpe chega a tela pelo fio: uns quadros pra o soco e o Rival colado aparecerem na foto)
		await Espera(0.25);
		Fotografar("user://itm-3-luta.png");
		// o soco pode tropecar ou empurrar: a concentracao exige o corpo parado, entao espera ele assentar
		await Espera(1.5);
		Conferir(luta && srv.PalcoMIT_EmLuta(cli.LocalId), "R4. a luta: o Rival soca, e os dois ficam com a tag de combate acesa");

		double? fuga = await Viajar(menu, srv, "Aliado", 2, fecharOMenu: true);
		Conferir(fuga is >= 0.35 and <= TetoDaViagemRapidaS && srv.PalcoMIT_EmLuta(cli.LocalId),
			$"R5. NO MEIO DA LUTA, com 100%: o jogador some e aparece do lado do Aliado em {Seg(fuga)} (0,2 s de concentracao no campo de visao + 0,2 s piscando antes de sumir)");
		await Quadros(4);
		Fotografar("user://itm-4-fuga.png");

		await Espera(0.5);
		double? volta = await Viajar(menu, srv, "Rival", 2, fecharOMenu: true);
		Conferir(volta is >= 0.35 and <= TetoDaViagemRapidaS,
			$"R6. ...e volta pra cima do Rival em {Seg(volta)}");
		await Quadros(4);
		Fotografar("user://itm-5-volta.png");
	}

	// ---------------------------------------------------------------- R7
	private async Tarefa OContraExemplo(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		double? ruim;
		Jandirus.Server.GameServer.MaestriaDoItNaoEncurtaDeTeste = true;
		Jandirus.Server.GameServer.MaestriaDoItSemPressaDePertoDeTeste = true;
		try { ruim = await Viajar(menu, srv, "Aliado", 6, fecharOMenu: true); }
		finally
		{
			Jandirus.Server.GameServer.MaestriaDoItNaoEncurtaDeTeste = false;
			Jandirus.Server.GameServer.MaestriaDoItSemPressaDePertoDeTeste = false;
		}
		Conferir(ruim is >= 2.5,
			$"(defeito injetado: a maestria nao encurta, nem perto) a mesma viagem de 100% leva {Seg(ruim)} -- a regua dos {TetoDaViagemRapidaS:0.00} s do R5 REPROVA");
	}

	// ---------------------------------------------------------------- R8-R9: quem esta perto ve o clarao, entre zonas (2026-10-24)
	/// <summary>
	/// O VIAJANTE DO PALCO (IT no nivel 5, maestria cheia) vai pra Namek e volta pro lado do Rival, pelo verbo de producao e no tique de
	/// verdade -- e quem joga, parado, e quem FICA na ida e quem ESTA NO DESTINO na volta. O clarao e desenhado no corpo
	/// (`EsquivaZanzoken`, o mesmo do desvio de golpe): a regua e o corpo dele ter o efeito na tela de quem olha.
	/// </summary>
	private async Tarefa QuemEstaPertoVe(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv)
	{
		menu.Fechar();
		Conferir(srv.PalcoMIT_Viajante(cli.LocalId), "(montagem) o Viajante (IT no nivel 5, maestria cheia) do lado de quem joga, e o Destino dele em Namek");
		int v = srv.PalcoMIT_Id("Viajante");
		Conferir(await Ate(() => Existe(v), 4), "(montagem) o Viajante chegou a tela de quem joga");
		await Quadros(20);

		srv.PalcoMIT_ViajanteVai("Destino");
		int saida = await PiscarEFotografar(v, "user://itm-6-saida-vista.png", 4);
		bool sumiu = await Ate(() => !Existe(v), 3);
		Conferir(sumiu && saida >= QuadrosQueContamComoVisto,
				 $"R8. QUEM FICA ve o Viajante PISCAR antes de sumir pra Namek (o corpo sai 0,2 s depois do clarao) [{saida} quadro(s) piscando]");
		// A VIAGEM SO FECHA NO CLARAO DA CHEGADA (0,1 s depois do salto), e ate la o verbo recusa um segundo pedido ("voce ja esta se
		// concentrando"): a volta espera ela fechar. Sem isto a volta saia no instante em que o corpo sumia e era recusada -- a r2 voltou assim.
		await Ate(() => srv.FaseDaTransmissaoDeTeste(v) == null, 2);

		srv.PalcoMIT_ViajanteVai("Rival");
		int chegada = await PiscarEFotografar(v, "user://itm-7-chegada-vista.png", 5);
		Conferir(chegada >= 1,
				 $"R9. QUEM ESTA NO DESTINO ve o Viajante chegar de Namek PISCANDO (o clarao sai 0,1 s depois de o corpo entrar na zona) [{chegada} quadro(s)]");
		await Ate(() => !Piscando(v), 2);
		await Quadros(15);

		// O CONTRA-EXEMPLO DA SAIDA: o corpo some no tique do clarao, e quem fica nao ve clarao nenhum
		Jandirus.Server.GameServer.TransmissaoSomeNaHoraDeTeste = true;
		try
		{
			int quadros = 0;
			srv.PalcoMIT_ViajanteVai("Destino");
			bool foi = await Ate(() => { if (Piscando(v)) quadros++; return !Existe(v); }, 4);
			Conferir(foi && quadros < QuadrosQueContamComoVisto,
					 $"R8b. (defeito injetado: o corpo some no tique do clarao) a regua do R8 REPROVA -- quem fica nao ve o clarao [{quadros} quadro(s)]");
		}
		finally { Jandirus.Server.GameServer.TransmissaoSomeNaHoraDeTeste = false; }
		await Quadros(15);
		// A VIAGEM SO FECHA NO CLARAO DA CHEGADA (0,1 s depois do salto), e ate la o verbo recusa um segundo pedido ("voce ja esta se
		// concentrando"): a volta espera ela fechar. Sem isto a volta saia no instante em que o corpo sumia e era recusada -- o mesmo da volta de cima.
		await Ate(() => srv.FaseDaTransmissaoDeTeste(v) == null, 2);

		// O CONTRA-EXEMPLO DA CHEGADA: o clarao sai no tique do salto, antes de o corpo existir na tela de quem esta la
		Jandirus.Server.GameServer.TransmissaoClaraoNaHoraDeTeste = true;
		try
		{
			bool viu = false;
			srv.PalcoMIT_ViajanteVai("Rival");
			bool apareceu = await Ate(() => { if (Piscando(v)) viu = true; return Existe(v); }, 5);
			await Ate(() => { if (Piscando(v)) viu = true; return false; }, 0.8);
			Conferir(apareceu && !viu, "R9b. (defeito injetado: o clarao da chegada no tique do salto) a regua do R9 REPROVA -- o corpo chega sem piscar");
		}
		finally { Jandirus.Server.GameServer.TransmissaoClaraoNaHoraDeTeste = false; }
	}

	/// <summary>
	/// ESPERA O CORPO PISCAR e fotografa no SEGUNDO quadro com as listras (a textura da tela e a do quadro ja desenhado; no primeiro as
	/// listras acabaram de nascer). Devolve quantos quadros ele foi visto piscando; para quando o clarao acaba ou o corpo some. Sem
	/// clarao, a foto sai mesmo assim (o relatorio mostra o que havia na tela). O molde e o do `RoboDoKaiKai`.
	/// </summary>
	private async System.Threading.Tasks.Task<int> PiscarEFotografar(int id, string foto, double segundos)
	{
		int quadros = 0;
		bool fotografou = false;
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (Piscando(id))
			{
				quadros++;
				if (quadros == 2) { Fotografar(foto); fotografou = true; }
			}
			else if (quadros > 0) break;
			await Quadros(1);
		}
		if (!fotografou) Fotografar(foto);
		return quadros;
	}

	private static bool Existe(int id) => World.Instancia?.CorpoDeTeste(id) != null;

	private static bool Piscando(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<EsquivaZanzoken>(EsquivaZanzoken.NomeDoNode) != null;

	// ---------------------------------------------------------------- as pecas
	/// <summary>
	/// APERTA o botao "Teletransportar" do corpo pedido, na aba Sense, e mede no relogio de parede ate o corpo chegar do lado dele
	/// (o servidor tica sozinho). Devolve os segundos, ou nulo se o botao nao estava la ou a viagem nao chegou no prazo.
	/// </summary>
	private async System.Threading.Tasks.Task<double?> Viajar(MenuJogo menu, Jandirus.Server.GameServer srv, string chave, double prazo, bool fecharOMenu)
	{
		int id = srv.PalcoMIT_Id(chave);
		await Sense(menu);
		bool achou = await Ate(() => Botoes(menu).Exists(b => Meta(b) == id), 3);
		Button? botao = Botoes(menu).FirstOrDefault(b => Meta(b) == id);
		if (!achou || botao == null) { _linhas.Add($"  --     sem o botao de {chave} na aba Sense"); return null; }
		ulong t0 = Time.GetTicksMsec();
		botao.EmitSignal(BaseButton.SignalName.Pressed);
		if (fecharOMenu) menu.Fechar();
		float tile = Jandirus.Core.World.ZoneCollision.TileSize;
		bool chegou = await Ate(() => { float d = srv.PalcoMIT_Distancia(C!.LocalId, chave); return d >= 0 && d <= 2f * tile; }, prazo);
		double s = (Time.GetTicksMsec() - t0) / 1000.0;
		_linhas.Add($"  --     viagem ate {chave}: {(chegou ? $"{s:0.000} s" : "NAO chegou")}");
		return chegou ? s : null;
	}

	private static string Seg(double? s) => s is { } v ? $"{v:0.00} s" : "nao chegou";

	private async Tarefa Sense(MenuJogo menu)
	{
		if (!menu.Visible) menu.Abrir();
		// A ABA SO EXISTE DEPOIS DO BIT DO SENSE CHEGAR (a ficha lenta): antes dele o `IrPara` cairia em Stats calado
		await Ate(() => menu.AbasDeTeste.Contains("Sense"), 5);
		menu.IrPara("Sense");
		await Ate(() => menu.AbaDeTeste == "Sense", 2);
		await Quadros(4);
	}

	private static string? Valor(MenuJogo menu, string rotulo) => menu.ValorDesenhado("Sense", rotulo);

	private static bool Cartao(MenuJogo menu) =>
		menu.PaginaDeTeste("Sense") is { } pg && Todos(pg).OfType<PanelContainer>()
			.Any(p => !p.IsQueuedForDeletion() && p.HasMeta("titulo") && p.GetMeta("titulo").AsString() == MenuJogo.TituloDoCartaoDaMaestria);

	// O texto LITERAL do botao, como o `RoboDaTransmissao`: o dono pediu "Teletransportar" (2026-10-21).
	private static List<Button> Botoes(MenuJogo menu) =>
		menu.PaginaDeTeste("Sense") is { } pg
			? [.. Todos(pg).OfType<Button>().Where(b => !b.IsQueuedForDeletion() && b.Text == "Teletransportar")]
			: [];

	private static int Meta(Button b) => b.HasMeta("teletransporte") ? b.GetMeta("teletransporte").AsInt32() : 0;

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private void Conferir(bool ok, string oque)
	{
		if (ok) { _ok++; _linhas.Add("  OK    " + oque); return; }
		_falhas.Add(oque);
		_linhas.Add("  FALHA " + oque);
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

	private async Tarefa Espera(double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
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
		foreach (string l in _linhas) GD.Print("[diagitmaestria] " + l);
		GD.Print($"[diagitmaestria] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0
			? "[diagitmaestria] ===== TUDO OK ====="
			: $"[diagitmaestria] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagitmaestria]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
