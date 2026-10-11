using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO VERB "MINERAR", PELO CLIENTE (`--diagminerar`).
///
/// O dono (2026-10-10): *"adicionando um verb chamado mineirar ao qual vc comeca a pegar resource por segundo
/// [...] caso vc tenha uma hand drill ou shovel vc pega esses recursos mais rapidos [...] porem ao estar
/// mineirando e igual meditacao vc n pode se mover nem fazer nada"*. A regra do servidor (a reserva, o que cada
/// ferramenta rende, o que derruba) esta na `--recursosteste`; aqui se prova o que so existe DESTE lado e PELO
/// FIO:
///
///   1. O verb esta na aba Other pra todo mundo, e o BOTAO dele poe o corpo local na atividade (a pose de
///      treino), acende o "MINERANDO" do HUD e a faixa (logo acima do chat) -- com o que o servidor contou:
///      ferramenta, taxa e reserva.
///   2. Os recursos CHEGAM: a carteira do cliente sobe aos degraus da ferramenta, e a faixa a acompanha -- e
///      ela avisa pelo sino DELA (`CarteiraMudou`), sem tocar o da tecnologia, que e a resposta que o fantasma
///      de assentar maquina espera. Contraprova: a carteira tocando o `TechMudou`, como na primeira versao.
///   3. A ferramenta que entra na mochila muda a taxa na tela (pa, depois furadeira). Contraprova: com a
///      ferramenta sem contar, a tela volta a dizer "com as maos".
///   4. ANDAR derruba (o corpo local larga a atividade na hora, e o servidor para de pagar); e andando o verb
///      nao comeca.
///   5. O verb de novo para; o botao "Parar" da faixa para. E um QUADRO LONGO entre o clique e a leitura nao
///      come o clique (o pedido vale por quadros do corpo). Contraprova: o prazo em milissegundos de antes.
///   6. AGIR derruba -- e aqui quem para e o SERVIDOR, e o cliente tem de ficar sabendo (a atividade local cai).
///      Contraprova: com a escuta desligada o corpo local segue "minerando" de um corpo que o servidor parou.
///   7. AS DUAS PONTAS NAO FICAM CONTANDO HISTORIAS DIFERENTES: um "minerando" que so o servidor tem (os dois
///      lados se cruzaram no fio) e desfeito pelo corpo local. Contraprova: sem a escuta, o servidor segue
///      minerando sozinho.
///   8. A reserva SECA: o servidor para, a faixa some, e o que entrou no bolso e o que o planeta tinha; e num
///      planeta esgotado o verb e recusado sem o corpo local ficar "minerando".
///   9. O "CAVAR" SAIU (dono: *"tire a opcao de cavar da pa e furadeira pq e redundante"*): na mochila, o painel
///      da pa so tem "Jogar fora"; a dica dela (no slot e no card da aba Tech) diz o que ela faz agora; e o
///      comando antigo, mandado a mao, e recusado sem render nada. Contraprova: o botao que volta.
///
/// O servidor mora neste processo (`--host`): a carteira, a ferramenta e a reserva sao postas por ele, pelas
/// portas de bancada do `GameServer.Recursos.cs`. As teclas de andar e de guarda sao as de verdade
/// (`Input.ActionPress`), lidas pelo `LocalPlayer` de producao.
///
///     Godot --headless --path . --host --rede 8112 --diagminerar --raca Human --conta bancada_minerar --nome Mineiro
/// </summary>
public partial class RoboDeMinerar : Node
{
	private int _ok;
	private readonly List<string> _falhas = [];

	/// <summary>O que o servidor disse a este jogador pelo chat, na ordem -- as frases da mineracao passam por aqui.</summary>
	private readonly List<string> _ouvido = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;
	private string Pose => Corpo?.GetNodeOrNull<CharacterVisual>("Visual")?.EstadoDeTeste ?? "(sem corpo)";

	private static void Nota(string linha) => GD.Print("[minerar] " + linha);

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		Nota((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		if (C is not { } cli) return;
		cli.Falou -= AoOuvir;
		cli.CarteiraMudou -= ContarCarteira;
		cli.TechMudou -= ContarSinoDaTech;
	}

	/// <summary>Quantas carteiras chegaram, e quantas vezes o sino da tecnologia (`TechMudou`) tocou -- ver a F2.</summary>
	private int _carteiras, _sinosDaTech;
	private void ContarCarteira() => _carteiras++;
	private void ContarSinoDaTech() => _sinosDaTech++;

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	private bool Ouvi(string trecho) => _ouvido.Any(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// UMA EXCECAO NO ROTEIRO E UM VERMELHO, NAO UM SILENCIO (a licao da `RoboDasAbas`): o roteiro e uma Task
	/// solta, e sem este `try` um passo que lancasse deixaria o processo vivo e sem placar. O placar e a
	/// arrumacao saem no `finally` -- inclusive os defeitos injetados, que sao estaticos e nao podem ficar ligados.
	/// </summary>
	private async System.Threading.Tasks.Task Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Conferir(false, "o roteiro correu ate o fim sem lancar excecao", $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		finally
		{
			Arrumar();
			Nota(_falhas.Count == 0
				? $"===== TUDO OK ({_ok} afirmacoes) ====="
				: $"===== {_falhas.Count} FALHA(S), {_ok} ok =====\n[minerar]   " + string.Join("\n[minerar]   ", _falhas));
		}
	}

	// =====================================================================
	// OS GESTOS
	// =====================================================================
	/// <summary>O verb como a tecla ligada a ele o aciona: a acao do proprio registro.</summary>
	private static void ApertarOVerb() => Verbos.PorChave("minerar")?.Acionar?.Invoke();

	private static void Tecla(string acao, bool embaixo)
	{
		if (embaixo) Godot.Input.ActionPress(Teclas.NomeNoMotor(acao));
		else Godot.Input.ActionRelease(Teclas.NomeNoMotor(acao));
	}

	/// <summary>O "MINERANDO" do canto do HUD esta escrito? (o rotulo de atividade, o mesmo do TREINANDO/MEDITANDO)</summary>
	private static bool HudDizMinerando() =>
		Hud.Instancia is { } hud && Todos(hud).OfType<Label>().Any(l => l.Text == "MINERANDO" && l.IsVisibleInTree());

	/// <summary>As caixas que a faixa nao pode cobrir: os paineis do chat e do HUD que estao na tela agora.</summary>
	private static IEnumerable<(string Nome, Rect2 Caixa)> CaixasVizinhas()
	{
		foreach ((string nome, Node? raiz) in new (string, Node?)[] { ("chat", Chat.Instancia), ("HUD", Hud.Instancia) })
		{
			if (raiz == null) continue;
			foreach (PanelContainer p in Todos(raiz).OfType<PanelContainer>())
				if (p.IsVisibleInTree() && p.GetGlobalRect().HasArea()) yield return ($"{nome}:{p.Name}", p.GetGlobalRect());
		}
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node x in Todos(f)) yield return x;
	}

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	/// <summary>
	/// COM JANELA (a corrida `fminerar`, com `--pasta`), a tela e fotografada pra quem for olhar o DESENHO da
	/// faixa; headless nao ha o que fotografar, e a bancada segue sem ela. A foto nao pontua: o que ela mostra
	/// (onde a faixa cai, se cobre alguma coisa) e pra olho, e o texto ja foi cobrado acima.
	/// </summary>
	private async System.Threading.Tasks.Task Fotografar(string nome)
	{
		if (DisplayServer.GetName() == "headless") return;
		await Quadros(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? foto = GetViewport()?.GetTexture()?.GetImage();
		if (foto == null || foto.IsEmpty()) { Nota($"  --     sem foto pra {nome}"); return; }

		string pasta = Arg("--pasta");
		if (pasta.Length == 0) pasta = ProjectSettings.GlobalizePath("user://");
		if (!pasta.EndsWith('/') && !pasta.EndsWith('\\')) pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(pasta);
		foto.SavePng(pasta + nome + ".png");
		Nota("  foto   " + pasta + nome + ".png");
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

	// =====================================================================
	// A MONTAGEM, E A ARRUMACAO
	// =====================================================================
	private bool _montado;
	private int _eu;
	private double _zeniAntes, _recursosAntes;

	/// <summary>Devolve o mundo como estava: defeitos desligados, teclas soltas, carteira, mochila e reserva de antes.</summary>
	private void Arrumar()
	{
		LocalPlayer.MineracaoNaoCaiDeTeste = false;
		GameClient.CarteiraTocaOSinoDaTechDeTeste = false;
		LocalPlayer.PedidoDeMineracaoVenceNoRelogioDeTeste = false;
		CatalogoDeItens.FerramentaVoltaACavarDeTeste = false;
		(GetTree().Root.FindChild("Inventario", true, false) as TelaDeInventario)?.Fechar();
		Recurso.FerramentaNaoContaDeTeste = false;
		Recurso.ReservaNaoRegeneraDeTeste = false;
		Tecla("move_right", false);
		Tecla("guard", false);
		MenuJogo.Instancia?.Fechar();

		if (!_montado || S is not { } srv) return;
		if (C is { Atividade: Protocol.Activity.Minerando } cli) cli.SendActivity(Protocol.Activity.Parado);
		srv.ReservaDeTeste(_eu, double.PositiveInfinity);
		srv.FerramentaDeTeste(_eu, Ferramenta.Mao);
		srv.EconomiaDeTeste(_eu, _zeniAntes, _recursosAntes);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Percurso()
	{
		Nota("===== BANCADA DO VERB MINERAR, PELO CLIENTE =====");

		bool pronto = await Ate(() => C is { Connected: true, LocalId: not 0 } c && c.Atributos.Raca is { Length: > 0 }
										&& Corpo != null && PainelDeMineracao.Instancia != null
										&& Verbos.PorChave("minerar") != null, 90);
		Conferir(pronto, "(montagem) o mundo chegou: conexao, corpo local, a faixa montada e o verb registrado",
			$"raca={C?.Atributos.Raca} corpo={Corpo != null} faixa={PainelDeMineracao.Instancia != null} verb={Verbos.PorChave("minerar") != null}");
		if (!pronto || C is not { } cli || Corpo is not { } corpo || PainelDeMineracao.Instancia is not { } faixa) return;

		bool hospedando = Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0;
		Conferir(hospedando && S != null, "(montagem) o servidor mora neste processo (--host): e ele que poe a ferramenta e a reserva");
		if (!hospedando || S is not { } srv) return;

		cli.Falou += AoOuvir;
		cli.CarteiraMudou += ContarCarteira;
		cli.TechMudou += ContarSinoDaTech;
		await Segundos(1.0);

		// A MONTAGEM: sem recurso no bolso, sem ferramenta, planeta cheio -- pelo servidor.
		_eu = cli.LocalId;
		_zeniAntes = cli.Zeni;
		_recursosAntes = Math.Max(0, srv.RecursosDeTeste(_eu));
		_montado = true;
		srv.EconomiaDeTeste(_eu, _zeniAntes, 0);
		srv.FerramentaDeTeste(_eu, Ferramenta.Mao);
		double cheia = srv.ReservaDeTeste(_eu, double.PositiveInfinity);
		await Ate(() => cli.Recursos == 0, 5);
		Conferir(Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone) && cheia > 0,
			"(montagem) o personagem nasceu num planeta, e o planeta tem reserva", $"{cli.Zone.Name}: {cheia:N0}");
		Conferir(cli.Recursos == 0, "(montagem) a carteira do cliente chegou zerada pelo fio", $"{cli.Recursos:0}");

		await F1_OVerbPoeOCorpoMinerando(cli, srv, corpo, faixa);
		await F2_OsRecursosChegam(cli, srv, faixa);
		await F3_AFerramentaMudaATaxa(cli, srv, faixa);
		await F4_AndarDerruba(cli, srv, corpo, faixa);
		await F5_OVerbDeNovoEOBotaoParam(cli, srv, corpo, faixa);
		await F6_AgirDerruba(cli, srv, corpo, faixa);
		await F7_AsDuasPontasConcordam(cli, srv, corpo);
		await F8_AReservaSeca(cli, srv, corpo, faixa);
		await F9_APaNaoCavaMais(cli, srv);
	}

	// ---------------------------------------------------------------------- 1. o verb, a pose, o HUD, a faixa
	private async System.Threading.Tasks.Task F1_OVerbPoeOCorpoMinerando(
		GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, PainelDeMineracao faixa)
	{
		Nota("--- 1: o verb, a pose, o HUD e a faixa ---");
		Verbo? verb = Verbos.Da(Verbos.Outros).FirstOrDefault(v => v.Nome == "Minerar");
		// (o `Verbo.Mostrar` virou `Verbo.Requisito` + a tabela `RequisitosDosVerbos`: "sem requisito" e nao ter nenhum dos dois)
		Conferir(verb is { Requisito: null, Disponivel: null, Acionar: not null } && Verbos.RequisitoDe(verb).Livre,
			"o verb \"Minerar\" esta na aba Other, pra todo mundo e sem requisito (sem `Requisito`, sem linha na tabela, sem `Disponivel`)");
		Conferir(verb != null && verb.Descricao.Contains($"+{Recurso.PorSegundoNaMao:0} com as mãos")
				 && verb.Descricao.Contains($"+{Recurso.PorSegundoComPa:0} com uma pá")
				 && verb.Descricao.Contains($"+{Recurso.PorSegundoComFuradeira:0} com uma furadeira"),
			"...e a frase dele diz as tres taxas, lidas das constantes do Core", verb?.Descricao ?? "");
		Conferir(!faixa.NaTela && !HudDizMinerando() && corpo.AtividadeDeTeste == Protocol.Activity.Parado,
			"antes de minerar nao ha faixa nem \"MINERANDO\" na tela");

		// O BOTAO DE VERDADE: abrir o menu, ir na aba e apertar o SINAL do `Button` -- e nao a acao por dentro.
		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara(Verbos.Outros);
		await Segundos(0.3);
		Button? botao = MenuJogo.Instancia?.PaginaDeTeste(Verbos.Outros) is { } pagina
			? Todos(pagina).OfType<Button>().FirstOrDefault(b => b.Text == "Minerar" && b.IsVisibleInTree())
			: null;
		Conferir(botao is { Disabled: false }, "o botao \"Minerar\" esta na pagina da aba Other, aceso");
		if (botao != null) botao.EmitSignal(BaseButton.SignalName.Pressed);
		else ApertarOVerb();
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Minerando, 2);
		MenuJogo.Instancia?.Fechar();

		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Minerando && cli.Atividade == Protocol.Activity.Minerando,
			"**O BOTAO POE O CORPO LOCAL MINERANDO** (a terceira atividade, ao lado de treinar e meditar)",
			$"corpo {corpo.AtividadeDeTeste}, cliente {cli.Atividade}");
		await Ate(() => faixa.NaTela && HudDizMinerando() && Pose == "train", 2);
		Conferir(faixa.NaTela && HudDizMinerando(), "...a faixa da mineracao aparece e o HUD diz \"MINERANDO\"", faixa.TextoDeTeste);
		Conferir(Pose == "train", "...e o corpo veste a pose de treino (a de quem trabalha parado)", Pose);

		await Ate(() => cli.Mineracao.Ativa && srv.MinerandoDeTeste(_eu), 3);
		Conferir(srv.MinerandoDeTeste(_eu) && cli.Mineracao is { Ativa: true, Ferramenta: Ferramenta.Mao }
				 && cli.Mineracao.PorSegundo == (float)Recurso.PorSegundoNaMao,
			"o SERVIDOR confirma pelo fio: minerando com as maos, na taxa da mao", $"{cli.Mineracao}");
		await Quadros(3);
		Conferir(faixa.TextoDeTeste.Contains("com as mãos") && faixa.TextoDeTeste.Contains($"+{Recurso.PorSegundoNaMao:0} recursos/s")
				 && faixa.TextoDeTeste.Contains("reserva do planeta: ") && faixa.TextoDeTeste.EndsWith('%'),
			"...e a faixa escreve a ferramenta, o ganho por segundo e a reserva do planeta, em porcento", faixa.TextoDeTeste);
		await Fotografar("minerar-1-maos");

		// ONDE A FAIXA CAI: dentro da tela, e sem cobrir o chat nem os paineis do HUD (a tela e de 1280 de largura,
		// e a primeira versao -- uma linha no pe, ao centro -- cobria as falas).
		Rect2 caixa = faixa.RetanguloDeTeste, tela = GetViewport().GetVisibleRect();
		List<string> cobertas = CaixasVizinhas().Where(v => v.Caixa.Intersects(caixa)).Select(v => $"{v.Nome} {v.Caixa}").ToList();
		Conferir(caixa.HasArea() && tela.Encloses(caixa) && cobertas.Count == 0,
			"a faixa cabe na tela e NAO cobre o chat nem os paineis do HUD", $"faixa {caixa} | tela {tela.Size} | cobre: {string.Join(", ", cobertas)}");
		var noPeAoCentro = new Rect2(tela.Size.X / 2 - 325, tela.Size.Y - 108, 650, 64);
		Conferir(CaixasVizinhas().Any(v => v.Nome.StartsWith("chat", StringComparison.Ordinal) && v.Caixa.Intersects(noPeAoCentro)),
			"(contraprova) a mesma regua reprova o lugar da primeira versao: uma linha de 650 px no pe da tela, ao centro, cobre o chat",
			$"{noPeAoCentro} | chat {string.Join(", ", CaixasVizinhas().Where(v => v.Nome.StartsWith("chat", StringComparison.Ordinal)).Select(v => v.Caixa))}");
		await Ate(() => Ouvi("você começa a minerar com as mãos"), 2);
		Conferir(Ouvi("você começa a minerar com as mãos"), "...e o chat conta o que comecou (a frase do servidor)",
			_ouvido.LastOrDefault() ?? "(nada)");
	}

	// ---------------------------------------------------------------------- 2. os recursos chegam
	private async System.Threading.Tasks.Task F2_OsRecursosChegam(GameClient cli, Jandirus.Server.GameServer srv, PainelDeMineracao faixa)
	{
		Nota("--- 2: os recursos chegam ---");
		double antes = cli.Recursos;
		double t0 = Time.GetTicksMsec() / 1000.0;
		double tres = 3 * Recurso.PorSegundoNaMao;
		_carteiras = _sinosDaTech = 0;
		bool chegou = await Ate(() => cli.Recursos - antes >= tres, 10);
		double levou = Time.GetTicksMsec() / 1000.0 - t0;
		double ganho = cli.Recursos - antes;

		Conferir(chegou && ganho % Recurso.PorSegundoNaMao == 0,
			$"**OS RECURSOS CHEGAM NA CARTEIRA DO CLIENTE**, aos degraus de {Recurso.PorSegundoNaMao:0} (a taxa da mao)", $"+{ganho:0}");
		Conferir(chegou && levou is > 1.5 and < 6.0,
			$"...tres degraus em ~3 s (\"por segundo\", e nao por quadro nem por minuto)", $"{levou:0.0} s");
		await Quadros(3);
		Conferir(cli.Recursos == srv.RecursosDeTeste(_eu), "...e o numero do cliente e o do servidor",
			$"cliente {cli.Recursos:0}, servidor {srv.RecursosDeTeste(_eu):0}");
		Conferir(faixa.TextoDeTeste.Contains($"você tem {cli.Recursos:N0}"), "...a faixa mostra o saldo novo", faixa.TextoDeTeste);
		Conferir(cli.Mineracao.Reserva is < 1f and > 0.99f && !faixa.TextoDeTeste.Contains("100%"),
			"...e a reserva do planeta ja nao esta em 100%: o que entrou no bolso saiu do chao",
			$"{cli.Mineracao.Reserva:0.000000} | {faixa.TextoDeTeste}");

		// A CARTEIRA TEM O SINO DELA: o `TechMudou` e a resposta que o fantasma de assentar maquina espera
		// (`TelaDeConstrucao`), e uma carteira por segundo tocando nele soltaria a previa de quem assenta minerando.
		Conferir(_carteiras >= 3 && _sinosDaTech == 0,
			"a carteira que chega a cada segundo avisa pelo sino DELA (`CarteiraMudou`) e nao toca o da tecnologia (`TechMudou`)",
			$"{_carteiras} carteiras, {_sinosDaTech} toques no sino da tecnologia");
		GameClient.CarteiraTocaOSinoDaTechDeTeste = true;
		int carteirasAntes = _carteiras;
		await Ate(() => _carteiras > carteirasAntes, 3);
		Conferir(_carteiras > carteirasAntes && _sinosDaTech > 0,
			"(defeito injetado: a carteira toca o sino da tecnologia, como na primeira versao) a contagem acusa -- a regua sabe reprovar",
			$"{_sinosDaTech} toques");
		GameClient.CarteiraTocaOSinoDaTechDeTeste = false;
	}

	// ---------------------------------------------------------------------- 3. a ferramenta
	private async System.Threading.Tasks.Task F3_AFerramentaMudaATaxa(GameClient cli, Jandirus.Server.GameServer srv, PainelDeMineracao faixa)
	{
		Nota("--- 3: a ferramenta ---");
		srv.FerramentaDeTeste(_eu, Ferramenta.Pa);
		await Ate(() => cli.Mineracao.Ferramenta == Ferramenta.Pa, 3);
		await Quadros(3);
		Conferir(cli.Mineracao.Ferramenta == Ferramenta.Pa && cli.Mineracao.PorSegundo == (float)Recurso.PorSegundoComPa
				 && faixa.TextoDeTeste.Contains("com a pá") && faixa.TextoDeTeste.Contains($"+{Recurso.PorSegundoComPa:0} recursos/s"),
			"com a PA na mochila a taxa da tela sobe pra da pa", faixa.TextoDeTeste);

		srv.FerramentaDeTeste(_eu, Ferramenta.Furadeira);
		await Ate(() => cli.Mineracao.Ferramenta == Ferramenta.Furadeira, 3);
		await Quadros(3);
		Conferir(cli.Mineracao.PorSegundo == (float)Recurso.PorSegundoComFuradeira && faixa.TextoDeTeste.Contains("com a furadeira")
				 && faixa.TextoDeTeste.Contains($"+{Recurso.PorSegundoComFuradeira:0} recursos/s")
				 && Recurso.PorSegundoNaMao < Recurso.PorSegundoComPa && Recurso.PorSegundoComPa < Recurso.PorSegundoComFuradeira,
			"...e com a FURADEIRA sobe de novo: mao < pa < furadeira", faixa.TextoDeTeste);
		await Fotografar("minerar-2-furadeira");

		Recurso.FerramentaNaoContaDeTeste = true;
		await Ate(() => cli.Mineracao.Ferramenta == Ferramenta.Mao, 3);
		await Quadros(3);
		Conferir(cli.Mineracao.Ferramenta == Ferramenta.Mao && faixa.TextoDeTeste.Contains("com as mãos"),
			"(defeito injetado: a ferramenta nao conta) com a furadeira na mochila a tela volta a dizer \"com as maos\" -- a regua sabe reprovar",
			faixa.TextoDeTeste);
		Recurso.FerramentaNaoContaDeTeste = false;
		await Ate(() => cli.Mineracao.Ferramenta == Ferramenta.Furadeira, 3);
	}

	// ---------------------------------------------------------------------- 4. andar derruba
	private async System.Threading.Tasks.Task F4_AndarDerruba(
		GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, PainelDeMineracao faixa)
	{
		Nota("--- 4: andar derruba ---");
		Conferir(srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Minerando, "(montagem) ainda minerando");
		Tecla("move_right", true);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado && !faixa.NaTela, 2);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && cli.Atividade == Protocol.Activity.Parado
				 && !faixa.NaTela && !HudDizMinerando(),
			"**ANDAR DERRUBA A MINERACAO**: o corpo local larga a atividade, a faixa e o \"MINERANDO\" somem",
			$"{corpo.AtividadeDeTeste}");

		// ANDANDO, O VERB NAO COMECA (a recusa e desta ponta: o pedido nem vira pacote).
		ApertarOVerb();
		await Segundos(0.6);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && !srv.MinerandoDeTeste(_eu),
			"...e com a tecla de andar embaixo o verb nao comeca", $"{corpo.AtividadeDeTeste}");
		Tecla("move_right", false);

		await Ate(() => !srv.MinerandoDeTeste(_eu), 2);
		Conferir(!srv.MinerandoDeTeste(_eu), "...o servidor parou de minerar por ele");
		await Quadros(3);
		double parado = cli.Recursos;
		await Segundos(2.5);
		Conferir(cli.Recursos == parado, "...e o saldo parou de subir", $"{parado:0} -> {cli.Recursos:0}");
	}

	// ---------------------------------------------------------------------- 5. o verb de novo, e o botao
	private async System.Threading.Tasks.Task F5_OVerbDeNovoEOBotaoParam(
		GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, PainelDeMineracao faixa)
	{
		Nota("--- 5: o verb de novo para; o botao \"Parar\" para ---");
		ApertarOVerb();
		await Ate(() => srv.MinerandoDeTeste(_eu) && cli.Mineracao.Ativa, 3);
		Conferir(srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Minerando, "(montagem) minerando de novo, pelo verb");

		_ouvido.Clear();   // a frase tem de ser a DESTA parada, e nao uma de antes
		ApertarOVerb();
		await Ate(() => !srv.MinerandoDeTeste(_eu) && !faixa.NaTela, 2);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && !srv.MinerandoDeTeste(_eu) && !faixa.NaTela,
			"**O VERB DE NOVO PARA** (o mesmo toc-toc do T e do M)", $"{corpo.AtividadeDeTeste}");
		await Ate(() => Ouvi("você para de minerar"), 2);   // a fala vem pelo fio: chega um quadro depois do estado
		Conferir(Ouvi("você para de minerar"), "...e o chat diz que parou", _ouvido.LastOrDefault() ?? "(nada)");

		ApertarOVerb();
		await Ate(() => srv.MinerandoDeTeste(_eu) && faixa.NaTela && cli.Mineracao.Ativa, 3);
		Conferir(srv.MinerandoDeTeste(_eu) && faixa.NaTela, "(montagem) minerando, com a faixa na tela");
		faixa.PararDeTeste();
		await Ate(() => !srv.MinerandoDeTeste(_eu) && !faixa.NaTela, 2);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && !srv.MinerandoDeTeste(_eu) && !faixa.NaTela,
			"...e o botao \"Parar\" da faixa faz o mesmo", $"{corpo.AtividadeDeTeste}");

		// ---- UM QUADRO LONGO NAO COME O CLIQUE. O pedido do verb espera o corpo le-lo, e o prazo dele era em
		// milissegundos (400): numa rodada com o boot mais pesado o quadro seguinte ao clique demorou mais que isso
		// e o botao nao fez NADA, calado. Aqui o quadro longo e fabricado -- a thread dorme 600 ms entre o clique e
		// a leitura -- e a contraprova e o prazo de antes.
		const int quadroLongoMs = 600;
		LocalPlayer.PedidoDeMineracaoVenceNoRelogioDeTeste = true;
		ApertarOVerb();
		System.Threading.Thread.Sleep(quadroLongoMs);
		await Segundos(1.0);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && !srv.MinerandoDeTeste(_eu),
			$"(defeito injetado: o pedido vence no relogio, 400 ms) um quadro de {quadroLongoMs} ms entre o clique e a leitura COME o clique, calado -- a regua sabe reprovar",
			$"corpo {corpo.AtividadeDeTeste}, servidor {srv.MinerandoDeTeste(_eu)}");
		LocalPlayer.PedidoDeMineracaoVenceNoRelogioDeTeste = false;

		ApertarOVerb();
		System.Threading.Thread.Sleep(quadroLongoMs);
		bool comecou = await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Minerando && srv.MinerandoDeTeste(_eu), 3);
		Conferir(comecou, $"**UM QUADRO LONGO NAO COME O CLIQUE**: com o mesmo quadro de {quadroLongoMs} ms, o pedido (que vale por QUADROS do corpo) e lido e a mineracao comeca",
			$"corpo {corpo.AtividadeDeTeste}, servidor {srv.MinerandoDeTeste(_eu)}");
		ApertarOVerb();
		await Ate(() => !srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
	}

	// ---------------------------------------------------------------------- 6. agir derruba (quem para e o servidor)
	private async System.Threading.Tasks.Task F6_AgirDerruba(
		GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, PainelDeMineracao faixa)
	{
		Nota("--- 6: agir derruba (a guarda) -- quem para e o servidor ---");
		LocalPlayer.MineracaoNaoCaiDeTeste = true;
		ApertarOVerb();
		await Ate(() => srv.MinerandoDeTeste(_eu), 3);
		Tecla("guard", true);
		await Ate(() => !srv.MinerandoDeTeste(_eu), 2);
		await Segundos(0.5);
		Conferir(!srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Minerando && faixa.NaTela,
			"(defeito injetado: o cliente nao escuta o \"parou\" do servidor) o servidor ja parou e o corpo local segue \"minerando\", com a faixa na tela -- a regua sabe reprovar",
			$"servidor {srv.MinerandoDeTeste(_eu)}, local {corpo.AtividadeDeTeste}");
		Tecla("guard", false);
		LocalPlayer.MineracaoNaoCaiDeTeste = false;
		ApertarOVerb();   // o corpo local sai do estado torto: pedir "minerando" PARA
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);

		ApertarOVerb();
		await Ate(() => srv.MinerandoDeTeste(_eu) && cli.Mineracao.Ativa, 3);
		Conferir(srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Minerando, "(montagem) minerando, agora com a escuta ligada");
		Tecla("guard", true);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		await Quadros(3);
		Conferir(!srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Parado
				 && cli.Atividade == Protocol.Activity.Parado && !faixa.NaTela && !HudDizMinerando(),
			"**AGIR DERRUBA** (erguer a guarda): o servidor para, e o corpo local, o HUD e a faixa ficam sabendo",
			$"servidor {srv.MinerandoDeTeste(_eu)}, local {corpo.AtividadeDeTeste}");
		Tecla("guard", false);
		await Segundos(0.3);
	}

	// ---------------------------------------------------------------------- 7. as duas pontas concordam
	private async System.Threading.Tasks.Task F7_AsDuasPontasConcordam(GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo)
	{
		Nota("--- 7: um \"minerando\" que so o servidor tem e desfeito ---");
		// O CRUZAMENTO NO FIO, FABRICADO: o pacote sai sem passar pelo corpo -- o servidor comeca a minerar e o
		// corpo local continua parado. E o estado em que as duas pontas contam historias diferentes.
		LocalPlayer.MineracaoNaoCaiDeTeste = true;
		cli.SendActivity(Protocol.Activity.Minerando);
		await Ate(() => srv.MinerandoDeTeste(_eu), 2);
		await Segundos(1.6);   // pelo menos um "esta de pe" do servidor passa pela escuta desligada
		Conferir(srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Parado,
			"(defeito injetado: a escuta desligada) o servidor segue minerando por um corpo que, deste lado, esta parado -- a regua sabe reprovar",
			$"servidor {srv.MinerandoDeTeste(_eu)}, local {corpo.AtividadeDeTeste}");

		LocalPlayer.MineracaoNaoCaiDeTeste = false;
		bool desfez = await Ate(() => !srv.MinerandoDeTeste(_eu) && cli.Atividade == Protocol.Activity.Parado, 3);
		Conferir(desfez && corpo.AtividadeDeTeste == Protocol.Activity.Parado,
			"**AS DUAS PONTAS VOLTAM A CONCORDAR**: ao ouvir \"esta de pe\" sem estar minerando, o corpo redeclara que esta parado, e o servidor para",
			$"servidor {srv.MinerandoDeTeste(_eu)}, cliente {cli.Atividade}, corpo {corpo.AtividadeDeTeste}");
		await Segundos(0.3);
	}

	// ---------------------------------------------------------------------- 8. a reserva seca
	private async System.Threading.Tasks.Task F8_AReservaSeca(
		GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, PainelDeMineracao faixa)
	{
		Nota("--- 8: a reserva seca ---");
		// 20 no chao e a regeneracao parada (pra conta fechar): a furadeira tira 12, depois os 8 que sobraram.
		const double resto = 20;
		Recurso.ReservaNaoRegeneraDeTeste = true;
		double ficou = srv.ReservaDeTeste(_eu, resto);
		Conferir(ficou == resto, "(montagem) a reserva do planeta foi posta em 20, sem regenerar", $"{ficou:0.###}");
		await Quadros(3);
		double antes = cli.Recursos;
		_ouvido.Clear();

		ApertarOVerb();
		bool comecou = await Ate(() => srv.MinerandoDeTeste(_eu), 3);
		Conferir(comecou, "(montagem) comecou a minerar no planeta quase seco");
		await Ate(() => cli.Mineracao.Ativa, 2);
		await Fotografar("minerar-3-quase-seco");
		await Ate(() => !srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Parado, 8);
		await Quadros(3);
		Conferir(!srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Parado && !faixa.NaTela && !HudDizMinerando(),
			"**A RESERVA SECA PARA A MINERACAO**: o servidor para, e o corpo local, o HUD e a faixa acompanham",
			$"servidor {srv.MinerandoDeTeste(_eu)}, local {corpo.AtividadeDeTeste}");
		Conferir(cli.Recursos - antes == resto && srv.ReservaAtualDeTeste(_eu) == 0,
			"...e o que entrou no bolso e EXATAMENTE o que o planeta tinha (20): nada vem do nada",
			$"+{cli.Recursos - antes:0.###}, reserva {srv.ReservaAtualDeTeste(_eu):0.###}");
		await Ate(() => Ouvi("a reserva deste planeta secou"), 2);
		Conferir(Ouvi("a reserva deste planeta secou"), "...e o chat diz por que parou", _ouvido.LastOrDefault() ?? "(nada)");

		_ouvido.Clear();
		ApertarOVerb();
		await Ate(() => Ouvi("esgotado"), 2);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		await Quadros(3);
		Conferir(Ouvi("esgotado") && !srv.MinerandoDeTeste(_eu) && corpo.AtividadeDeTeste == Protocol.Activity.Parado && !faixa.NaTela,
			"num planeta ESGOTADO o verb e recusado com a frase, e o corpo local nao fica \"minerando\" sozinho",
			$"servidor {srv.MinerandoDeTeste(_eu)}, local {corpo.AtividadeDeTeste}, ouviu: {_ouvido.LastOrDefault() ?? "(nada)"}");
	}

	// ---------------------------------------------------------------------- 9. o cavar saiu
	/// <summary>
	/// A PA NA MOCHILA, COMO O JOGADOR A VE. A tela da mochila desenha um botao por acao que o item declara
	/// (`TelaDeInventario.AbrirAcoes`), entao "nao ha botao de cavar" se mede NA TELA: abre-se a mochila, clica-se
	/// no slot e leem-se os botoes que nasceram.
	/// </summary>
	private async System.Threading.Tasks.Task F9_APaNaoCavaMais(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- 9: a pa e a furadeira nao cavam mais ---");
		srv.FerramentaDeTeste(_eu, Ferramenta.Pa);
		await Ate(() => cli.Mochila.Quantos(CatalogoDeItens.Pa) == 1 && cli.Mochila.Quantos(CatalogoDeItens.Furadeira) == 0, 3);
		string daPa = Recurso.DescricaoDeFerramenta(CatalogoDeItens.Pa) ?? "";

		TelaDeInventario? tela = GetTree().Root.FindChild("Inventario", true, false) as TelaDeInventario;
		Conferir(tela != null && cli.Mochila.Quantos(CatalogoDeItens.Pa) == 1, "(montagem) a tela da mochila existe, e ha uma pa nela");
		if (tela == null) return;

		// ABRE A MOCHILA, CLICA NO SLOT DA PA e devolve a dica do slot e os botoes que o clique fez nascer.
		async System.Threading.Tasks.Task<(string Dica, List<string> Botoes)> PainelDaPa()
		{
			tela.Abrir();
			await Quadros(3);
			Button? slot = Todos(tela).OfType<Button>()
				.FirstOrDefault(x => x.IsVisibleInTree() && x.TooltipText.StartsWith("Pá\n", StringComparison.Ordinal));
			if (slot == null) return ("(sem slot da pa)", []);

			HashSet<ulong> jaEstavam = [.. Todos(tela).OfType<Button>().Select(x => x.GetInstanceId())];
			slot.EmitSignal(BaseButton.SignalName.Pressed);
			await Quadros(3);
			List<string> nasceram = [.. Todos(tela).OfType<Button>()
				.Where(x => x.IsVisibleInTree() && !jaEstavam.Contains(x.GetInstanceId())).Select(x => x.Text)];
			return (slot.TooltipText, nasceram);
		}
		static bool EhDeCavar(string texto) => texto.Contains("cavar", StringComparison.OrdinalIgnoreCase);

		(string dica, List<string> botoes) = await PainelDaPa();
		Conferir(botoes.Count == 1 && botoes[0] == "Jogar fora" && !botoes.Any(EhDeCavar),
			"**NAO HA MAIS BOTAO DE CAVAR**: clicar na pa, na mochila, abre o painel so com \"Jogar fora\" (o de todo item)",
			string.Join(" | ", botoes));
		Conferir(dica == $"Pá\n{daPa}" && daPa.Contains("minerar mais rápido") && !EhDeCavar(dica),
			"...e a dica do slot diz o que ela faz AGORA: na mochila, minera-se mais rapido (com os numeros e o caminho do verb)",
			dica.Replace("\n", " / "));
		tela.Fechar();

		// A MESMA FRASE NO CARD DA ABA TECH (a dica do card e a descricao do item).
		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara("Tech");
		PanelContainer? CardDaPa() => MenuJogo.Instancia?.PaginaDeTeste("Tech") is { } pagina
			? Todos(pagina).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("id") && c.GetMeta("id").AsString() == CatalogoDeItens.Pa)
			: null;
		await Ate(() => CardDaPa() != null, 6);
		Conferir(CardDaPa()?.TooltipText == daPa, "...e o card da Shovel na aba Tech diz a mesma frase", CardDaPa()?.TooltipText ?? "(sem card)");
		MenuJogo.Instancia?.Fechar();

		// O COMANDO ANTIGO, MANDADO A MAO -- o cliente velho, ou o mexido.
		double zeni = cli.Zeni, xp = cli.TechXp;
		_ouvido.Clear();
		cli.SendVerbo("item_cavar", CatalogoDeItens.Pa);
		await Ate(() => Ouvi("não dá pra cavar Pá"), 3);
		await Segundos(0.5);
		Conferir(Ouvi("não dá pra cavar Pá") && cli.Zeni == zeni && cli.TechXp == xp && cli.Mochila.Quantos(CatalogoDeItens.Pa) == 1,
			"um cliente antigo que ainda mande `item_cavar` ouve a recusa -- e nao ganha zeni, nem XP de tecnologia, nem perde a pa",
			$"ouviu: {_ouvido.LastOrDefault() ?? "(nada)"} | zeni {zeni:0} -> {cli.Zeni:0}, xp {xp:0.##} -> {cli.TechXp:0.##}");

		// CONTRAPROVA: a pa volta a declarar a acao, e o botao volta pra tela.
		CatalogoDeItens.FerramentaVoltaACavarDeTeste = true;
		try
		{
			(_, List<string> comODefeito) = await PainelDaPa();
			Conferir(comODefeito.Any(EhDeCavar),
				"(defeito injetado: a pa volta a declarar 'cavar') o botao de cavar REAPARECE no painel da mochila -- a regua sabe reprovar",
				string.Join(" | ", comODefeito));
		}
		finally
		{
			CatalogoDeItens.FerramentaVoltaACavarDeTeste = false;
			tela.Fechar();
		}
	}
}
