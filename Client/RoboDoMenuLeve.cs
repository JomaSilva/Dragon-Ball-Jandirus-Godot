using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O MENU P LEVE (`--diagmenuleve`) ============================
/// O pedido do dono (2026-10-19), literal: *"as abas do menu P principalmente a de adm, tech , nav etc estao mt mal
/// otimizadas, quando vc joga com elas abertas o jogo fica extremamente lagado [...] na aba de nav system, quando vc
/// der zoom em um sistema ou abrir o mapa do sistema, poderia usar o proprio sprite do planeta pra mostrar ele assim
/// como ta feito com as estrelas"* -- e logo depois: *"esses lags causam problemas de sync com o server as vezes,
/// fazendo personagem atravessar parede etc"*.
///
/// "Mais leve" nao se afirma: se CONTA. Esta bancada conta o que pesava, no caminho de producao, e cada conserto tem o
/// seu defeito injetado (o jogo de antes) pra mostrar que a regua sabe reprovar:
///
///   F1  andando e virando, a ficha chega com a DIRECAO do corpo mudada -- e a Tech e a Admin nao remontam mais;
///   F2  a carteira mudando (minerando) reescreve a faixa da Tech no lugar, sem remontar os cem cards;
///   F3  o Ki mudando (luta) reescreve a linha da Stats no lugar, sem remontar a aba;
///   F4  quem DESENHA o corpo (a pilula do golpe da Stats e da Equip) continua seguindo ele;
///   F5  a lua da World nao compila o shader de novo a cada remontagem;
///   F6  a carta estelar so redesenha quando o que ela desenha muda -- e redesenha quando muda;
///   F7  a tela do sistema idem, e os mundos dela e da carta saem com a ARTE do planeta (com fotos de antes e depois);
///   F8  a pagina escondida (e o menu fechado) nao processa;
///   F9  um quadro longo anda no maximo o teto, e o servidor nao puxa o corpo de volta;
///   F10 (relatorio, sem regua) o custo de cada aba aberta com o corpo andando, ANTES (todos os defeitos ligados) e AGORA.
///
/// O que ela NAO cobre: a sensacao do dono jogando. Os numeros do relatorio sao a melhor ponte, e o passo a passo pra
/// conferir no jogo vai no relatorio da sessao.
///
/// COMO RODAR -- hospedando (o servidor no mesmo processo poe o Nav System na mochila e mexe no Ki), com janela pras fotos:
///     Godot --path . --host --rede 9701 --diagmenuleve --raca Human --conta bancada_menuleve --nome Leve
///           --position 1920,0 --resolution 1280x720
/// ==========================================================================================
/// </summary>
public partial class RoboDoMenuLeve : Node
{
	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	// =====================================================================
	// PLACAR -- as provas e, a parte, as injecoes
	// =====================================================================
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _vermelhas = [], _passouBatido = [], _fotos = [];

	private static void Nota(string linha) => GD.Print("[menuleve] " + linha);

	private void Afirmar(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _vermelhas.Add(oque); }
	}

	/// <summary>
	/// A INJECAO QUE PASSA BATIDO E UMA FALHA, com a palavra: e a regua que nao sabe reprovar, e quem le o log (o ciclo
	/// da fila conta as linhas `FALHA `) tem de ve-la como vermelha.
	/// </summary>
	private void Injeta(string oque, bool pegou, string detalhe = "")
	{
		Nota((pegou ? "  pegou  " : "  FALHA (injecao PASSOU BATIDO) ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (pegou) _injOk++;
		else { _injFalha++; _passouBatido.Add(oque); }
	}

	private void Placar()
	{
		Nota("==================================================================================");
		Nota($"PLACAR: {_ok + _injOk} OK, {_falha + _injFalha} FALHA");
		Nota($"INJECAO: {_injOk} pegou, {_injFalha} PASSOU BATIDO");
		foreach (string v in _vermelhas) Nota("   FALHA  " + v);
		foreach (string v in _passouBatido) Nota("   PASSOU (injecao) " + v);
		foreach (string f in _fotos) Nota("   foto  " + f);
		int ruins = _falha + _injFalha;
		Nota(ruins == 0 ? "===== TUDO OK =====" : $"===== {ruins} FALHA(S) =====");
	}

	// =====================================================================
	// A ESCUTA DO CORPO -- quantas vezes a ficha chegou com o `Estado` mudado
	// =====================================================================
	private byte _ultimoEstado;
	private int _mudancasDoEstado;
	private bool _ouvindo;

	private void AoFicha(SheetState f)
	{
		if (f.Estado == _ultimoEstado) return;
		_ultimoEstado = f.Estado;
		_mudancasDoEstado++;
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		if (_ouvindo && C is { } cli) cli.SheetUpdated -= AoFicha;
		DefeitosDeAntes(false);
		LocalPlayer.PassoSemTetoNoQuadroLongoDeTeste = false;
		MapaEstelar.EuDeTeste = null;
	}

	/// <summary>
	/// TODOS OS DEFEITOS INJETADOS DO MENU, juntos: e o menu de antes de 2026-10-19 inteiro (o relatorio da F10 mede o
	/// "antes" assim). Desligados no `finally` de cada familia e no `_ExitTree`.
	/// </summary>
	private static void DefeitosDeAntes(bool ligado)
	{
		MenuJogo.ComumComOCorpoDeTeste = ligado;
		MenuJogo.VivasNaAssinaturaDeTeste = ligado;
		MenuJogo.PaginaEscondidaProcessaDeTeste = ligado;
		MenuJogo.CorpoForaDaAssinaturaDeTeste = false;   // este nao e "o antes": e o contra-exemplo da F4, so dela
		MapaEstelar.CartaRedesenhaTodoQuadroDeTeste = ligado;
		MapaEstelar.SeloSemEuDeTeste = false;
		MapaEstelar.PlanetaSemArteDeTeste = ligado;
		LuaNoCeu.LuaCompilaACadaDiscoDeTeste = ligado;
	}

	private async System.Threading.Tasks.Task Rodar()
	{
		try { await Roteiro(); }
		catch (Exception e)
		{
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		finally
		{
			DefeitosDeAntes(false);
			LocalPlayer.PassoSemTetoNoQuadroLongoDeTeste = false;
			MapaEstelar.EuDeTeste = null;
			World.Instancia?.PararDeTeste();
			Placar();
			GetTree()?.Quit();
		}
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Roteiro()
	{
		Nota("================ O MENU P LEVE: as abas abertas sem pesar no jogo (dono, 2026-10-19) ================");
		bool pronto = await Ate(() => C is { LocalId: > 0 } && M != null && S != null && World.Instancia?.PosicaoLocal != null, 90);
		Afirmar("PREMISSA: entrei no mundo hospedando (o servidor mora neste processo)", pronto);
		if (!pronto) return;
		GameClient cli = C!;
		MenuJogo menu = M!;
		Jandirus.Server.GameServer srv = S!;
		await Segundos(1.5);   // a ficha lenta e os atributos do login chegam

		cli.SheetUpdated += AoFicha;
		_ouvindo = true;
		_ultimoEstado = cli.Sheet.Estado;

		bool nav = srv.NavSystemNaMochilaDeTeste(cli.LocalId, true)
				   && await Ate(() => cli.Atributos.Tem(Protocol.Poder.Nav), 8);
		Afirmar("PREMISSA: com o Nav System na mochila, a aba Nav existe", nav && Array.IndexOf(menu.AbasDeTeste, "Nav") >= 0);
		Afirmar("PREMISSA: quem hospeda e admin -- a aba Admin existe", Array.IndexOf(menu.AbasDeTeste, Verbos.Admin) >= 0,
				string.Join(",", menu.AbasDeTeste));

		menu.Abrir();
		menu.IrPara("Tech");
		bool catalogo = await Ate(() => cli.Catalogo.Count > 0, 12);
		Afirmar("PREMISSA: o catalogo da Tech chegou (a aba o pede uma vez)", catalogo, $"{cli.Catalogo.Count} ofertas");

		await F1_OCorpoNaoRemonta(menu);
		await F2_ACarteiraDaTech(menu, cli, srv);
		await F3_OKiDaStats(menu, cli, srv);
		await F4_QuemDesenhaOCorpo(menu, cli);
		await F5_ALuaDaWorld(menu);
		await F6_ACarta(menu, cli);
		await F7_ATelaDoSistemaEAArte(menu, cli);
		await F8_APaginaEscondida(menu);
		await F9_OQuadroLongo(menu, cli, srv);
		// O RELATORIO E SO DE QUEM PEDE: ele nao tem regua (nao reprova nada) e custa um minuto de fila a cada rodada de
		// mutante, que so precisa das familias de cima.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--semrelatorio") < 0) await F10_OCustoDeCadaAba(menu);
	}

	// =====================================================================
	// F1 -- o corpo nao remonta quem nao o desenha
	// =====================================================================
	/// <summary>
	/// Anda pra um lado e pro outro (pelo piloto automatico, o caminho de verdade do passo), e conta quantas fichas
	/// chegaram com o `Estado` mudado e quantas vezes a pagina da aba foi remontada no meio.
	/// </summary>
	private async System.Threading.Tasks.Task<(int Remontagens, int Mudancas)> AndarVirando(MenuJogo menu, string aba, double segundos = 3.0)
	{
		await Assentar(menu, aba);
		int r0 = menu.RemontagensDaAbaDeTeste(aba), m0 = _mudancasDoEstado;
		double fim = Agora + segundos;
		int lado = 1;
		while (Agora < fim)
		{
			World.Instancia?.AndarDeTeste(new Vector2(lado, 0));
			await Segundos(0.45);
			lado = -lado;
		}
		World.Instancia?.PararDeTeste();
		await Segundos(0.4);   // a ultima ficha da virada chega
		return (menu.RemontagensDaAbaDeTeste(aba) - r0, _mudancasDoEstado - m0);
	}

	private async System.Threading.Tasks.Task F1_OCorpoNaoRemonta(MenuJogo menu)
	{
		Nota("--- F1: andando e virando, a ficha traz a DIRECAO do corpo -- e as abas que nao a desenham nao remontam ---");
		foreach (string aba in new[] { "Tech", Verbos.Admin })
		{
			(int r, int m) = await AndarVirando(menu, aba);
			Afirmar($"PREMISSA ({aba}): virando de lado, a ficha chegou com o Estado mudado (os bits da direcao do corpo)",
					m >= 2, $"{m} mudanca(s) do Estado");
			Afirmar($"andando e virando com a aba {aba} aberta, a pagina dela NAO e remontada", r == 0,
					$"{r} remontagem(ns) em {m} mudanca(s) do Estado");
		}

		MenuJogo.ComumComOCorpoDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(2);
			(int r, int m) = await AndarVirando(menu, "Tech");
			Injeta("(defeito injetado: o corpo inteiro de volta no `comum`) a Tech volta a remontar os cem cards a cada virada, e a regua de cima REPROVA",
				   r > 0, $"{r} remontagem(ns) em {m} mudanca(s) do Estado");
		}
		finally
		{
			MenuJogo.ComumComOCorpoDeTeste = false;
			menu.ForcarRedesenho();
			await Quadros(2);
		}
	}

	// =====================================================================
	// F2 -- a carteira da Tech e viva
	// =====================================================================
	private async System.Threading.Tasks.Task<(int Chegou, int Remontou, bool Escreveu)> MexerNaCarteira(
		MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv, int vezes)
	{
		int r0 = menu.RemontagensDaAbaDeTeste("Tech"), chegou = 0;
		bool escreveu = true;
		for (int i = 0; i < vezes; i++)
		{
			double antes = cli.Zeni;
			// POR FORA DO FUNIL, como a missao de cargo e o premio do torneio: quem percebe e a vigia do servidor, que
			// manda o `S2C.Carteira` -- o mesmo pacote que chega a cada segundo de mineracao. UM zeni por vez: a oferta
			// mais barata do catalogo custa 10, e a carteira tem de mudar sem virar a recusa de card nenhum.
			srv.ZeniPorForaDeTeste(cli.LocalId, 1);
			if (await Ate(() => cli.Zeni >= antes + 1 - 1e-6, 4)) chegou++;
			await Quadros(2);
			string? faixa = menu.ValorDesenhado("Tech", "Tecnologia");
			escreveu &= faixa != null && faixa.Contains($"{cli.Zeni:N0} zeni", StringComparison.Ordinal);
		}
		return (chegou, menu.RemontagensDaAbaDeTeste("Tech") - r0, escreveu);
	}

	private async System.Threading.Tasks.Task F2_ACarteiraDaTech(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F2: a carteira muda (como minerando) com a aba Tech aberta: a faixa escreve o saldo NO LUGAR, sem remontar os cards ---");
		double zeni0 = cli.Zeni, recursos0 = cli.Recursos;
		try
		{
			// A CARTEIRA ZERADA, pra nenhuma recusa de card virar no meio da medida: com zero, os seis zeni da familia nao
			// pagam oferta nenhuma (a mais barata custa 10), e uma remontagem aqui so pode ser a da carteira na assinatura.
			// O catalogo que o servidor reenvia com a carteira nova remonta a pagina UMA vez -- o `Assentar` espera ela.
			srv.EconomiaDeTeste(cli.LocalId, 0, 0);
			await Ate(() => cli.Zeni == 0 && cli.Recursos == 0, 4);
			await Assentar(menu, "Tech");

			(int chegou, int remontou, bool escreveu) = await MexerNaCarteira(menu, cli, srv, 4);
			Afirmar("PREMISSA: a carteira chegou 4 vezes com o zeni novo", chegou == 4, $"{chegou}");
			Afirmar("a pagina da Tech NAO foi remontada pela carteira (os cem cards ficaram onde estavam)", remontou == 0,
					$"{remontou} remontagem(ns)");
			Afirmar("...e a faixa escreveu o zeni de agora a cada vez (a legenda e viva)", escreveu,
					menu.ValorDesenhado("Tech", "Tecnologia") ?? "(nula)");

			MenuJogo.VivasNaAssinaturaDeTeste = true;
			try
			{
				menu.ForcarRedesenho();
				await Quadros(2);
				(int c2, int r2, _) = await MexerNaCarteira(menu, cli, srv, 2);
				Injeta("(defeito injetado: a carteira de volta na assinatura) a Tech volta a remontar os cards a cada saldo novo, e a regua de cima REPROVA",
					   r2 > 0, $"{r2} remontagem(ns) em {c2} carteira(s)");
			}
			finally
			{
				MenuJogo.VivasNaAssinaturaDeTeste = false;
				menu.ForcarRedesenho();
				await Quadros(2);
			}
		}
		finally
		{
			srv.EconomiaDeTeste(cli.LocalId, zeni0, recursos0);
			await Ate(() => Math.Abs(cli.Zeni - zeni0) < 1e-6, 4);
		}
	}

	// =====================================================================
	// F3 -- os vitais da Stats sao vivos
	// =====================================================================
	private async System.Threading.Tasks.Task<(int Chegou, int Remontou, bool Escreveu)> MexerNoKi(
		MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv, int vezes)
	{
		int r0 = menu.RemontagensDaAbaDeTeste("Stats"), chegou = 0;
		bool escreveu = true;
		for (int i = 0; i < vezes; i++)
		{
			double razao = 0.25 + 0.15 * i;
			double alvo = cli.Sheet.MaxKi * razao;
			srv.KiDeTeste(cli.LocalId, razao);   // a porta da `--condicaoteste`: poe o Ki na razao do tanque e manda a ficha
			// A REGENERACAO ANDA: o Ki que chega e o do alvo mais o que o tique regenerou -- dai a folga.
			if (await Ate(() => Math.Abs(cli.Sheet.Ki - alvo) <= Math.Max(3, alvo * 0.03), 4)) chegou++;
			await Quadros(2);
			// O TEXTO QUE A MONTAGEM ESCREVERIA COM A FICHA DE AGORA: a viva e a montagem leem a mesma conta, entao a
			// linha tem que ser exatamente ele -- e isso vale depois de qualquer ficha, porque a ficha redesenha na hora.
			SheetState f = cli.Sheet;
			escreveu &= menu.ValorDesenhado("Stats", "Ki") == $"{f.Ki:N0} / {f.MaxKi:N0}   ({f.RazaoDeKi * 100:0}%)";
		}
		return (chegou, menu.RemontagensDaAbaDeTeste("Stats") - r0, escreveu);
	}

	private async System.Threading.Tasks.Task F3_OKiDaStats(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F3: o Ki muda (como em luta) com a aba Stats aberta: a linha e a barra se reescrevem NO LUGAR, sem remontar a aba ---");
		double ki0 = cli.Sheet.MaxKi > 0 ? cli.Sheet.Ki / cli.Sheet.MaxKi : 1;
		try
		{
			await Assentar(menu, "Stats");
			(int chegou, int remontou, bool escreveu) = await MexerNoKi(menu, cli, srv, 4);
			Afirmar("PREMISSA: a ficha chegou 4 vezes com o Ki novo", chegou == 4, $"{chegou}");
			Afirmar("a pagina da Stats NAO foi remontada pelo Ki", remontou == 0, $"{remontou} remontagem(ns)");
			Afirmar("...e a linha 'Ki' escreveu o Ki de cada ficha, igual ao que a montagem escreveria", escreveu,
					menu.ValorDesenhado("Stats", "Ki") ?? "(nula)");
			ProgressBar? barra = BarraDaLinha(menu.PaginaDeTeste("Stats"), "Ki");
			Afirmar("...e a barra do Ki esta na razao da ficha de agora", barra != null && Math.Abs(barra.Value - Math.Min(cli.Sheet.RazaoDeKi, barra.MaxValue)) < 0.02,
					barra == null ? "sem barra" : $"barra {barra.Value:0.000}, ficha {cli.Sheet.RazaoDeKi:0.000}");

			MenuJogo.VivasNaAssinaturaDeTeste = true;
			try
			{
				menu.ForcarRedesenho();
				await Quadros(2);
				(int c2, int r2, _) = await MexerNoKi(menu, cli, srv, 2);
				Injeta("(defeito injetado: os vitais de volta na assinatura) a Stats volta a remontar a cada ficha com o Ki novo, e a regua de cima REPROVA",
					   r2 > 0, $"{r2} remontagem(ns) em {c2} ficha(s)");
			}
			finally
			{
				MenuJogo.VivasNaAssinaturaDeTeste = false;
				menu.ForcarRedesenho();
				await Quadros(2);
			}
		}
		finally { srv.KiDeTeste(cli.LocalId, ki0); }
	}

	// =====================================================================
	// F4 -- quem desenha o corpo continua seguindo ele
	// =====================================================================
	private async System.Threading.Tasks.Task<bool> TrocarOLetal(GameClient cli, bool letal)
	{
		cli.SendLethal(letal);
		return await Ate(() => cli.Sheet.Letal == letal, 4);
	}

	private async System.Threading.Tasks.Task F4_QuemDesenhaOCorpo(MenuJogo menu, GameClient cli)
	{
		Nota("--- F4: quem DESENHA o corpo continua seguindo ele: o golpe letal troca e a pilula da Stats e da Equip troca junto ---");
		bool original = cli.Sheet.Letal;
		try
		{
			foreach (string aba in new[] { "Stats", "Equip" })
			{
				await Assentar(menu, aba);
				bool quer = !cli.Sheet.Letal;
				bool chegou = await TrocarOLetal(cli, quer);
				await Quadros(3);
				string? pilula = PilulaDaLinha(menu.PaginaDeTeste(aba), "Golpe");
				Afirmar($"PREMISSA ({aba}): o servidor trocou o golpe e a ficha chegou com o bit do letal", chegou);
				Afirmar($"a pilula 'Golpe' da {aba} diz o golpe de agora ({(quer ? "LETAL" : "não-letal")})",
						pilula == (quer ? "LETAL" : "não-letal"), pilula ?? "(nula)");
			}

			MenuJogo.CorpoForaDaAssinaturaDeTeste = true;
			try
			{
				menu.IrPara("Stats");
				menu.ForcarRedesenho();
				await Quadros(3);
				bool quer = !cli.Sheet.Letal;
				await TrocarOLetal(cli, quer);
				await Quadros(3);
				string? congelada = PilulaDaLinha(menu.PaginaDeTeste("Stats"), "Golpe");
				Injeta("(defeito injetado: a Stats sem os bits do corpo na assinatura) a pilula 'Golpe' fica com o golpe de antes, e a regua de cima REPROVA",
					   congelada != (quer ? "LETAL" : "não-letal"), congelada ?? "(nula)");
			}
			finally { MenuJogo.CorpoForaDaAssinaturaDeTeste = false; }
		}
		finally
		{
			if (cli.Sheet.Letal != original) await TrocarOLetal(cli, original);
			menu.ForcarRedesenho();
			await Quadros(2);
		}
	}

	// =====================================================================
	// F5 -- a lua da World nao compila de novo
	// =====================================================================
	private static ulong IdDoShader(MenuJogo menu) =>
		menu.PaginaDeTeste("World") is { } pg && Todos(pg).OfType<LuaNoCeu>().FirstOrDefault() is { ShaderDeTeste: { } s }
			? s.GetInstanceId() : 0;

	private async System.Threading.Tasks.Task F5_ALuaDaWorld(MenuJogo menu)
	{
		Nota("--- F5: a lua da aba World nao compila o shader de novo a cada remontagem (e e o mesmo do disco do HUD) ---");
		await Assentar(menu, "World");
		ulong a = IdDoShader(menu);
		menu.ForcarRedesenho();
		await Quadros(3);
		ulong b = IdDoShader(menu);
		ulong hud = Hud.Instancia?.Lua?.ShaderDeTeste?.GetInstanceId() ?? 0;
		Afirmar("PREMISSA: a aba World tem o disco da lua (o `LuaNoCeu`)", a != 0 && b != 0);
		Afirmar("duas montagens da aba World usam o MESMO shader da lua (nenhuma compilacao nova)", a != 0 && a == b, $"{a} e {b}");
		Afirmar("...e e o mesmo shader do disco do HUD", hud == 0 || hud == b, hud == 0 ? "sem disco no HUD" : $"{hud}");

		LuaNoCeu.LuaCompilaACadaDiscoDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			ulong c = IdDoShader(menu);
			Injeta("(defeito injetado: cada disco compila o proprio shader) a remontagem traz um shader NOVO, e a regua de cima REPROVA",
				   c != 0 && c != b, $"{b} -> {c}");
		}
		finally
		{
			LuaNoCeu.LuaCompilaACadaDiscoDeTeste = false;
			menu.ForcarRedesenho();
			await Quadros(2);
		}
	}

	// =====================================================================
	// F6 -- a carta estelar redesenha quando muda, e so entao
	// =====================================================================
	private async System.Threading.Tasks.Task<int> DesenhosEm(Func<int> desenhos, double segundos)
	{
		int d0 = desenhos();
		await Segundos(segundos);
		return desenhos() - d0;
	}

	/// <summary>Faz a mudanca e confere que um desenho saiu nos quadros seguintes.</summary>
	private async System.Threading.Tasks.Task<bool> Redesenha(Func<int> desenhos, Action mudanca, int quadros = 4)
	{
		await Quadros(2);
		int d0 = desenhos();
		mudanca();
		await Quadros(quadros);
		return desenhos() > d0;
	}

	private static Vector2 PosDe(string nome)
	{
		PlanetaNoEspaco p = Espaco.PreFeitos().First(x => x.Nome == nome);
		return new Vector2(p.Pos.X, p.Pos.Y);
	}

	private async System.Threading.Tasks.Task F6_ACarta(MenuJogo menu, GameClient cli)
	{
		Nota("--- F6: a carta estelar so redesenha quando o que ela desenha muda -- e quando muda, redesenha ---");
		World.Instancia?.PararDeTeste();
		menu.IrPara("Nav");
		await Segundos(1.5);   // o primeiro enquadramento e a varredura assentam
		MapaEstelar? mapa = menu.MapaDeTeste;
		Afirmar("PREMISSA: a aba Nav montou a carta, e ela esta na tela", mapa is { Visible: true } && mapa.IsVisibleInTree());
		if (mapa == null) return;
		int Desenhos() => mapa.DesenhosDeTeste;

		int parada = await DesenhosEm(Desenhos, 1.0);
		Afirmar("parada por 1 s (corpo parado, nada clicado), a carta desenha no maximo 2 vezes -- antes era uma por quadro",
				parada <= 2, $"{parada} desenho(s) em 1 s");

		MapaEstelar.CartaRedesenhaTodoQuadroDeTeste = true;
		int comDefeito;
		try { comDefeito = await DesenhosEm(Desenhos, 1.0); }
		finally { MapaEstelar.CartaRedesenhaTodoQuadroDeTeste = false; }
		Injeta("(defeito injetado: um redesenho por quadro) a carta volta a desenhar o tempo todo parada, e a regua de cima REPROVA",
			   comDefeito > 10, $"{comDefeito} desenho(s) em 1 s");

		Vector2 terra = PosDe("Earth");
		Afirmar("mexer na camera (o enquadrar, a mesma porta do arrasto e da roda) redesenha",
				await Redesenha(Desenhos, () => mapa.EnquadrarDeTeste(terra, 48f)));

		SistemaSolar? daTerra = Sistemas.Em(cli.SeedDoUniverso, new Vec2(terra.X, terra.Y));
		Afirmar("PREMISSA: a Terra mora num sistema da carta", daTerra != null);
		if (daTerra is { } st)
			// O CLIQUE NAO CHAMA `QueueRedraw`: quem ve a selecao nova e o selo. E ele que esta sendo medido aqui.
			Afirmar("selecionar um sistema pelo clique redesenha (o anel verde aparece sem ninguem pedir o desenho)",
					await Redesenha(Desenhos, () => mapa.ClicarNoSistema(st)));

		// "EU" ANDANDO: no espaco o ponto segue o corpo. Em terra "eu" e o planeta e nao anda, entao a bancada move o
		// ponto pela porta de teste -- 3.000 px de mundo sao ~2 px nesta escala.
		try
		{
			MapaEstelar.EuDeTeste = terra + new Vector2(6000, 0);
			await Quadros(4);
			Afirmar("'eu' andando na carta redesenha (o ponto segue o corpo)",
					await Redesenha(Desenhos, () => MapaEstelar.EuDeTeste = terra + new Vector2(9000, 0)));

			MapaEstelar.SeloSemEuDeTeste = true;
			try
			{
				await Quadros(3);
				bool redesenhou = await Redesenha(Desenhos, () => MapaEstelar.EuDeTeste = terra + new Vector2(12000, 0), 6);
				Injeta("(defeito injetado: o selo sem o 'eu') o ponto anda e a carta nao redesenha, e a regua de cima REPROVA", !redesenhou);
			}
			finally { MapaEstelar.SeloSemEuDeTeste = false; }
		}
		finally { MapaEstelar.EuDeTeste = null; }

		// A ROTA DO PILOTO (o traco ate o destino) -- e soltar o piloto apaga.
		Afirmar("ligar o piloto automatico redesenha (a rota aparece)",
				await Redesenha(Desenhos, () => World.Instancia?.Pilotar(terra + new Vector2(0, 40_000))));
		Afirmar("...e solta-lo tambem (a rota some)", await Redesenha(Desenhos, () => World.Instancia?.SoltarPiloto()));

		// A VARREDURA QUE ANDA POR QUADRO: num zoom de vizinhanca ela traz alguns blocos por desenho, e tem que terminar
		// sozinha -- a carta pede o desenho seguinte enquanto falta bloco.
		// UM ZOOM DE VIZINHANCA LONGE DE TUDO: ~18 blocos na tela, nenhum varrido ainda -- mais que o orcamento de 6 por
		// desenho. A varredura tem de terminar SOZINHA, com a carta parada (sem arrasto), e depois a carta para de desenhar.
		mapa.EnquadrarDeTeste(terra + new Vector2(300_000, 300_000), 140f);
		// O "ANTES" E LIDO JA, sem esperar quadro: o desenho pedido roda na fila de chamadas adiadas, que esvazia duas
		// vezes por quadro -- na rodada r1 a carta ja tinha varrido tudo quando o robo olhou, e a regua leu zero.
		int comecou = mapa.BlocosSemVarrerDeTeste;
		await Segundos(2.0);
		int paradaDepois = await DesenhosEm(Desenhos, 0.6);
		int faltam = mapa.BlocosSemVarrerDeTeste;   // so le: nao anda a varredura
		Afirmar("num zoom de vizinhanca, a varredura dos mundos termina SOZINHA (nenhum bloco da tela fica sem varrer) e a carta volta a ficar parada",
				comecou > 6 && faltam == 0 && paradaDepois <= 1,
				$"{comecou} bloco(s) por varrer no comeco, {faltam} depois de 2 s; {paradaDepois} desenho(s) em 0,6 s");
	}

	// =====================================================================
	// F7 -- a tela do sistema, e a arte dos planetas
	// =====================================================================
	private async System.Threading.Tasks.Task F7_ATelaDoSistemaEAArte(MenuJogo menu, GameClient cli)
	{
		Nota("--- F7: a tela do sistema so redesenha quando muda, e os mundos dela e da carta saem com a ARTE do planeta ---");
		MapaEstelar? mapa = menu.MapaDeTeste;
		Vector2 terra = PosDe("Earth");
		if (mapa == null || Sistemas.Em(cli.SeedDoUniverso, new Vec2(terra.X, terra.Y)) is not { } s)
		{
			Afirmar("PREMISSA: a carta e o sistema da Terra existem", false);
			return;
		}

		// ---- A TABELA DE ICONES: todo estado que ela escolhe existe na folha ----
		SpriteFrames? folha = FolhasPresas.Carregar("res://Assets/Sprites/Misc/Planets.tres");
		var faltam = new List<string>();
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (folha == null || !folha.HasAnimation(ArteDePlaneta.EstadoDe(p))) faltam.Add($"{p.Nome}->{ArteDePlaneta.EstadoDe(p)}");
		foreach (BiomaDeTerreno b in Enum.GetValues<BiomaDeTerreno>())
			if (folha == null || !folha.HasAnimation(ArteDePlaneta.EstadoDoBioma(b))) faltam.Add($"{b}->{ArteDePlaneta.EstadoDoBioma(b)}");
		Afirmar("a folha `Planets.tres` tem o quadro que a tabela escolhe pra cada pre-feito e cada bioma", faltam.Count == 0,
				faltam.Count == 0 ? "" : string.Join(", ", faltam));

		mapa.EnquadrarDeTeste(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y), 48f);
		await Quadros(3);
		bool abriu = mapa.AbrirSistema(s);
		await Segundos(1.0);
		TelaDoSistema? tela = menu.SistemaDeTeste;
		Afirmar("PREMISSA: o duplo clique abriu a tela do sistema da Terra",
				abriu && tela is { Visible: true } && tela.SistemaDeTeste is { } aberto && aberto.Id.Equals(s.Id));
		if (tela == null) return;
		int Desenhos() => tela.DesenhosDeTeste;

		int parada = await DesenhosEm(Desenhos, 1.0);
		Afirmar("parada por 1 s sem nada selecionado, a tela do sistema desenha no maximo 2 vezes", parada <= 2, $"{parada} desenho(s)");
		MapaEstelar.CartaRedesenhaTodoQuadroDeTeste = true;
		int comDefeito;
		try { comDefeito = await DesenhosEm(Desenhos, 1.0); }
		finally { MapaEstelar.CartaRedesenhaTodoQuadroDeTeste = false; }
		Injeta("(defeito injetado: um redesenho por quadro) a tela do sistema volta a desenhar o tempo todo, e a regua de cima REPROVA",
			   comDefeito > 10, $"{comDefeito} desenho(s) em 1 s");

		int ultima = s.Orbitas - 1;
		Afirmar("selecionar um mundo pelo clique redesenha (o clique nao pede desenho: e o selo que ve)",
				await Redesenha(Desenhos, () => tela.ClicarNaOrbita(ultima)));
		int comPainel = await DesenhosEm(Desenhos, 1.0);
		Afirmar("com um mundo selecionado, o painel (a hora la, a distancia daqui) se reescreve ~2 vezes por segundo, e nao 60",
				comPainel >= 1 && comPainel <= 4, $"{comPainel} desenho(s) em 1 s");

		// ---- A ARTE NA TELA DO SISTEMA ----
		tela.Reenquadrar();
		await Quadros(4);
		Afirmar($"os {s.Orbitas} mundos do sistema da Terra sairam com a ARTE do planeta no ultimo desenho",
				tela.MundosComArteDeTeste == s.Orbitas, $"{tela.MundosComArteDeTeste} de {s.Orbitas}");
		await Guardar("menuleve-01-sistema-da-terra-com-a-arte", await Foto());

		MapaEstelar.PlanetaSemArteDeTeste = true;
		try
		{
			tela.QueueRedraw();
			await Quadros(4);
			Injeta("(defeito injetado: o disco de UI no lugar da arte) nenhum mundo sai com a arte, e a regua de cima REPROVA",
				   tela.MundosComArteDeTeste == 0, $"{tela.MundosComArteDeTeste} com arte");
			await Guardar("menuleve-02-sistema-da-terra-ANTES-o-disco-de-ui", await Foto());
		}
		finally
		{
			MapaEstelar.PlanetaSemArteDeTeste = false;
			tela.QueueRedraw();
			await Quadros(2);
		}

		// ---- A ARTE NA CARTA, DE PERTO ----
		tela.VoltarDeTeste();
		await Quadros(3);
		// 0,06 px de tela por px de mundo: a Terra (raio 220) vira um disco de ~13 px, bem acima do piso da arte.
		mapa.EnquadrarDeTeste(terra, (float)(0.06 * Sistemas.CelulaPx));
		await Segundos(1.2);
		(int mundos, int comArte) = mapa.ArteNoUltimoDesenhoDeTeste;
		Afirmar("de perto, a carta desenha os mundos com a ARTE (todos os que tem raio de tela pra ela)",
				comArte >= 1 && comArte <= mundos, $"{comArte} de {mundos} mundo(s) vivo(s) na tela");
		await Guardar("menuleve-03-carta-de-perto-com-a-arte", await Foto());

		MapaEstelar.PlanetaSemArteDeTeste = true;
		try
		{
			mapa.QueueRedraw();
			await Quadros(4);
			Injeta("(defeito injetado: o disco de UI no lugar da arte) a carta de perto volta a desenhar bolas, e a regua de cima REPROVA",
				   mapa.ArteNoUltimoDesenhoDeTeste.ComArte == 0, $"{mapa.ArteNoUltimoDesenhoDeTeste.ComArte} com arte");
			await Guardar("menuleve-04-carta-de-perto-ANTES-o-disco-de-ui", await Foto());
		}
		finally
		{
			MapaEstelar.PlanetaSemArteDeTeste = false;
			mapa.QueueRedraw();
			await Quadros(2);
		}
		mapa.VerTudo();
		await Quadros(2);
	}

	// =====================================================================
	// F8 -- a pagina escondida nao processa
	// =====================================================================
	private async System.Threading.Tasks.Task<int> ProcessosDaCartaEm(MenuJogo menu, int quadros)
	{
		int p0 = menu.MapaDeTeste?.ProcessosDeTeste ?? 0;
		await Quadros(quadros);
		return (menu.MapaDeTeste?.ProcessosDeTeste ?? 0) - p0;
	}

	private async System.Threading.Tasks.Task F8_APaginaEscondida(MenuJogo menu)
	{
		Nota("--- F8: a pagina escondida (e o menu fechado) nao processa: a carta para de rodar fora da tela ---");
		menu.IrPara("Nav");
		await Quadros(3);
		Afirmar("na tela, a pagina Nav processa (e a carta roda o `_Process`)",
				menu.PaginaProcessaDeTeste("Nav") && await ProcessosDaCartaEm(menu, 10) >= 8);

		menu.IrPara("Stats");
		await Quadros(2);
		int escondida = await ProcessosDaCartaEm(menu, 30);
		Afirmar("com outra aba na tela, a pagina Nav NAO processa", !menu.PaginaProcessaDeTeste("Nav") && escondida == 0,
				$"{escondida} `_Process` da carta em 30 quadros");

		menu.Fechar();
		await Quadros(2);
		int fechado = await ProcessosDaCartaEm(menu, 30);
		Afirmar("com o menu FECHADO, nenhuma pagina processa (nem a Nav, nem a Stats, que era a da vez)",
				!menu.PaginaProcessaDeTeste("Nav") && !menu.PaginaProcessaDeTeste("Stats") && fechado == 0,
				$"{fechado} `_Process` da carta em 30 quadros");

		menu.Abrir();
		await Quadros(2);
		Afirmar("...e reaberto, a pagina da vez volta a processar", menu.PaginaProcessaDeTeste("Stats"));

		MenuJogo.PaginaEscondidaProcessaDeTeste = true;
		try
		{
			menu.IrPara("Nav");
			await Quadros(2);
			menu.IrPara("Stats");
			await Quadros(2);
			int comDefeito = await ProcessosDaCartaEm(menu, 30);
			Injeta("(defeito injetado: a pagina escondida processando) a carta escondida roda o `_Process` todo quadro, e a regua de cima REPROVA",
				   comDefeito >= 20, $"{comDefeito} `_Process` em 30 quadros");
		}
		finally
		{
			MenuJogo.PaginaEscondidaProcessaDeTeste = false;
			menu.IrPara("Nav");
			await Quadros(2);
			menu.IrPara("Stats");
			await Quadros(2);
		}
	}

	// =====================================================================
	// F9 -- o quadro longo
	// =====================================================================
	/// <summary>
	/// O ENGASGO DE VERDADE: o corpo andando, e a thread principal parada 0,4 s dentro de um quadro -- o quadro seguinte
	/// chega com o maior delta que o Godot entrega (~0,14 s com a fisica padrao: 8 passos de 1/60 s) pro jogador local E
	/// pro servidor (que mora neste processo). Conta o maior dt que o passo andou e quantas vezes o servidor puxou o corpo.
	/// </summary>
	private async System.Threading.Tasks.Task<(float Dt, int Puxoes, float Andou)> Engasgar(GameClient cli, Jandirus.Server.GameServer srv, Vector2 rumo)
	{
		// 0,25 s ANDANDO E O ENGASGO: o passo dele (ate ~0,14 s com o defeito) cabe no trecho de 0,8 s que o `RumoLivre`
		// ja mediu livre -- nenhum muro no meio pode calar o passo e fazer o contra-exemplo passar por sorte.
		World.Instancia?.AndarDeTeste(rumo);
		await Segundos(0.25);
		Vector2 antes = World.Instancia?.PosicaoLocal ?? default;
		int c0 = srv.CorrecoesDeTeste(cli.LocalId);
		LocalPlayer.MaiorDtDoPassoDeTeste = 0;
		// O QUADRO DO ENGASGO NUNCA PASSA DE ~0,14 s: medido nas rodadas r1 e r2 (0,137 a 0,143 s com engasgos de 0,3 s e
		// de 1,5 s) -- o Godot amarra o delta aos passos de fisica do quadro (no maximo 8 de 1/60 s). Qualquer engasgo acima
		// disso da o mesmo quadro; 0,4 s e o de uma remontagem pesada do menu de antes.
		OS.DelayMsec(400);
		await Quadros(3);
		World.Instancia?.PararDeTeste();   // logo depois do passo do engasgo: o resto da espera e so pros pacotes chegarem
		await Quadros(9);
		float dt = LocalPlayer.MaiorDtDoPassoDeTeste;
		int puxoes = srv.CorrecoesDeTeste(cli.LocalId) - c0;
		float andou = ((World.Instancia?.PosicaoLocal ?? default) - antes).Length();
		await Segundos(0.4);
		return (dt, puxoes, andou);
	}

	/// <summary>
	/// UM RUMO LIVRE DAQUI: o primeiro dos quatro em que o corpo anda 0,8 s sem parar (sai do lugar o bastante pra nao ter
	/// encostado em nada) -- e o corpo VOLTA pro comeco do trecho, pra o engasgo acontecer dentro dele.
	/// </summary>
	private async System.Threading.Tasks.Task<Vector2> RumoLivre(GameClient cli)
	{
		float esperado = Jandirus.Core.World.MoveRules.SpeedPx(cli.Sheet.SpeedStat) * 0.8f;
		foreach (Vector2 rumo in new[] { Vector2.Right, Vector2.Left, Vector2.Down, Vector2.Up })
		{
			Vector2 de = World.Instancia?.PosicaoLocal ?? default;
			World.Instancia?.AndarDeTeste(rumo);
			await Segundos(0.8);
			World.Instancia?.PararDeTeste();
			await Segundos(0.2);
			float andou = ((World.Instancia?.PosicaoLocal ?? default) - de).Length();
			World.Instancia?.AndarDeTeste(-rumo);
			await Segundos(0.8);
			World.Instancia?.PararDeTeste();
			await Segundos(0.3);
			if (andou > esperado * 0.7f) return rumo;
		}
		return Vector2.Right;
	}

	private async System.Threading.Tasks.Task F9_OQuadroLongo(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F9: um quadro longo (0,4 s de engasgo) anda no maximo o teto do cliente, e o servidor nao puxa o corpo de volta ---");
		menu.Fechar();
		await Quadros(2);
		Vector2 rumo = await RumoLivre(cli);
		Vector2 inicio = World.Instancia?.PosicaoLocal ?? default;
		(float dt, int puxoes, float andou) = await Engasgar(cli, srv, rumo);
		Afirmar("PREMISSA: o corpo estava andando quando o quadro engasgou", andou > 4f, $"andou {andou:0.0} px no trecho");
		Afirmar($"o quadro do engasgo andou no maximo {MoveRules.TetoDoQuadroDoCliente:0.##} s (e nao o quadro inteiro que o Godot entregou)",
				dt > 0.05f && dt <= MoveRules.TetoDoQuadroDoCliente + 1e-4f, $"dt do passo {dt:0.000} s");
		Afirmar("...e o servidor NAO puxou o corpo de volta (nenhuma correcao de movimento nova)", puxoes == 0, $"{puxoes} correcao(oes)");

		// DE VOLTA AO COMECO DO TRECHO LIVRE, pelo piloto -- e o mesmo engasgo, no mesmo rumo, com o defeito.
		World.Instancia?.IrAteDeTeste(new Vec2(inicio.X, inicio.Y));
		await Ate(() => ((World.Instancia?.PosicaoLocal ?? default) - inicio).Length() < 4f, 3);
		World.Instancia?.PararDeTeste();
		await Segundos(0.4);
		LocalPlayer.PassoSemTetoNoQuadroLongoDeTeste = true;
		try
		{
			(float dt2, int puxoes2, _) = await Engasgar(cli, srv, rumo);
			// A REGUA DO TETO, e nao a do puxao: o puxao depende da VELOCIDADE (o quadro de ~0,14 s so passa do orcamento de
			// 0,135 s + 6 px de quem corre a mais de ~1.200 px/s), e o corpo novo da bancada anda a ~100. O puxao fica como
			// leitura -- a prova de que ele some e a do servidor, `--passorapidoteste`.
			Injeta("(defeito injetado: o quadro longo anda o passo inteiro) o passo do engasgo passa do teto do cliente, e a regua de cima REPROVA",
				   dt2 > MoveRules.TetoDoQuadroDoCliente + 1e-4f, $"dt {dt2:0.000} s, {puxoes2} correcao(oes)");
		}
		finally
		{
			LocalPlayer.PassoSemTetoNoQuadroLongoDeTeste = false;
			World.Instancia?.PararDeTeste();
		}
		menu.Abrir();
	}

	// =====================================================================
	// F10 -- o relatorio de custo por aba (sem regua)
	// =====================================================================
	/// <summary>
	/// UMA ABA, 2 s andando e virando: remontagens, os ms delas, e o quadro medio e o pior (o intervalo entre dois
	/// quadros, que inclui o desenho e o arranjo da pagina -- o custo que o jogador sente). `antes` liga o menu de antes.
	/// </summary>
	private async System.Threading.Tasks.Task<(int R, double Ms, double Medio, double Pior)> Medir(MenuJogo menu, string aba, bool antes)
	{
		DefeitosDeAntes(antes);
		try
		{
			menu.IrPara(aba);
			menu.ForcarRedesenho();
			await Quadros(3);
			int r0 = menu.RemontagensDaAbaDeTeste(aba);
			double ms0 = menu.MsRemontandoDaAbaDeTeste(aba);
			double soma = 0, pior = 0;
			int quadros = 0, lado = 1;
			double fim = Agora + 2.0, troca = Agora;
			ulong t = Time.GetTicksUsec();
			while (Agora < fim)
			{
				if (Agora >= troca) { World.Instancia?.AndarDeTeste(new Vector2(lado, 0)); lado = -lado; troca = Agora + 0.45; }
				await Quadros(1);
				ulong agora = Time.GetTicksUsec();
				double q = (agora - t) / 1000.0;
				t = agora;
				soma += q;
				pior = Math.Max(pior, q);
				quadros++;
			}
			World.Instancia?.PararDeTeste();
			return (menu.RemontagensDaAbaDeTeste(aba) - r0, menu.MsRemontandoDaAbaDeTeste(aba) - ms0,
					quadros > 0 ? soma / quadros : 0, pior);
		}
		finally { DefeitosDeAntes(false); }
	}

	private async System.Threading.Tasks.Task F10_OCustoDeCadaAba(MenuJogo menu)
	{
		Nota("--- F10 (relatorio, sem regua): cada aba aberta 2 s com o corpo andando e virando -- ANTES (os defeitos ligados) e AGORA ---");
		menu.Abrir();
		await Quadros(2);
		foreach (string aba in menu.AbasDeTeste)
		{
			(int rA, double msA, double medA, double piorA) = await Medir(menu, aba, antes: true);
			(int rD, double msD, double medD, double piorD) = await Medir(menu, aba, antes: false);
			Nota($"  {aba,-9}  ANTES: {rA,3} remontagem(ns) {msA,7:0.0} ms, quadro medio {medA,5:0.0} ms, pior {piorA,6:0.0} ms"
				 + $"   |   AGORA: {rD,3} remontagem(ns) {msD,7:0.0} ms, quadro medio {medD,5:0.0} ms, pior {piorD,6:0.0} ms");
		}
		menu.IrPara("Stats");
		menu.Fechar();
	}

	// =====================================================================
	// AJUDANTES
	// =====================================================================
	private static double Agora => Time.GetTicksMsec() / 1000.0;

	/// <summary>
	/// ABRE A ABA E ESPERA A PAGINA ASSENTAR: a primeira visita de algumas pede listas ao servidor (a Admin pede as contas e
	/// o modo das racas, a Tech o catalogo), e a RESPOSTA remonta a pagina -- uma remontagem legitima, que cairia no meio
	/// da medida e pareceria a remontagem que a medida procura. Assentada = 0,8 s seguidos sem remontar (no maximo 6 s).
	/// </summary>
	private async System.Threading.Tasks.Task Assentar(MenuJogo menu, string aba)
	{
		menu.IrPara(aba);
		await Quadros(3);
		double fim = Agora + 6.0, quieta = Agora + 0.8;
		int r = menu.RemontagensDaAbaDeTeste(aba);
		while (Agora < fim && Agora < quieta)
		{
			await Quadros(1);
			int agora = menu.RemontagensDaAbaDeTeste(aba);
			if (agora != r) { r = agora; quieta = Agora + 0.8; }
		}
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async System.Threading.Tasks.Task Segundos(double s)
	{
		double fim = Agora + s;
		while (Agora < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora + segundos;
		while (Agora < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private async System.Threading.Tasks.Task<Image?> Foto()
	{
		await Quadros(2);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private async System.Threading.Tasks.Task Guardar(string nome, Image? foto)
	{
		if (foto == null) { Nota($"  --     sem foto pra {nome} (sem janela nao ha o que fotografar)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		foto.SavePng(caminho);
		_fotos.Add(caminho);
		Nota("  foto   " + caminho);
		await System.Threading.Tasks.Task.CompletedTask;
	}

	/// <summary>
	/// A arvore debaixo de um node, em profundidade, SEM os nos que ja vao embora: o `QueueFree` de uma remontagem deixa
	/// a pagina velha na arvore ate o fim do quadro, e a varredura veria as duas (ver as armadilhas das bancadas).
	/// </summary>
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f.IsQueuedForDeletion()) continue;
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	/// <summary>O texto da pilula de uma linha de estado (`LinhaComPilula`, metadado `estado`), ou nulo.</summary>
	private static string? PilulaDaLinha(Control? pagina, string rotulo)
	{
		if (pagina == null) return null;
		foreach (Node n in Todos(pagina))
			if (n is HBoxContainer h && h.HasMeta("estado") && h.GetMeta("estado").AsString() == rotulo)
				foreach (Node p in Todos(h))
					if (p is PanelContainer pc && pc.HasMeta("pilula")) return pc.GetMeta("pilula").AsString();
		return null;
	}

	/// <summary>A barra de uma linha com barra (metadado `barra`), ou nula.</summary>
	private static ProgressBar? BarraDaLinha(Control? pagina, string rotulo) =>
		pagina == null ? null
		: Todos(pagina).OfType<ProgressBar>().FirstOrDefault(b => b.HasMeta("barra") && b.GetMeta("barra").AsString() == rotulo);
}
