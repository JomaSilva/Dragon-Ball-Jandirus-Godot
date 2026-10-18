using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DE TELA DA VERSAO 0.2.0 (`--diagv020`) -- o pedido do dono de 2026-10-12, do lado de ca do fio.
///
/// A irma de servidor (`--v020teste`) prova as regras. Esta prova o que so existe na TELA e PELO FIO:
///
///   F1  o CHAT que perde o foco fecha a linha (o corpo travado depois do menu de interacao) e guarda a frase;
///   F2  a caixa do BATISMO atravessa a remontagem da aba World com o teclado, o texto e o cursor;
///   F3  a MARCA DE ALVO desenha acima do piso;
///   F4  a obra SEM PARAFUSO (a macieira plantada) nao pisca de "solta";
///   F5  os AVISOS do painel: guarda levantada (pela tecla de verdade), atordoado (pelo servidor), o agarrao;
///   F6  o PASSO: um toque na tecla mostra um passo, e nao um deslize;
///   F7  AGARRADO, apertar a direcao e se debater: o andamento sobe no HUD e o corpo se solta;
///   F8  a MIRA NO RABO: o gancho do seletor e a tecla 6;
///   F9  AS ROUPAS: o catalogo do cliente tem a pasta inteira, e cada folha nova abre com desenho;
///   F10 A ARTE DA ARMADURA: fabricar abre a tela, a grade tem todas as artes, e a escolhida vira a peca;
///   F11 A ROUPA DO NOCAUTEADO: o menu de revistar oferece "Tirar do corpo", e a peca vem pra mochila;
///   F12 A ESTRELA DE UM LADO SO: o que o node da estrela manda ao shader (a INTENCAO -- o pixel e de foto);
///   F13 O ESC DA MOCHILA: fecha a mochila e nao abre a pausa, uma camada por vez (o teclado numerico, a pausa, o chat).
///
/// Cada familia tem o defeito injetado do jogo de antes. O que ela NAO prova: cor e forma (o gancho do rabo, a
/// marca no chao, os pontinhos do ar divino -- estes sao da `--diaggodki`).
///
///     Godot --headless --path . --host --rede 8262 --diagv020 --raca Saiyan --conta bancada_dvdois --nome Vinte
/// </summary>
public partial class RoboDaV020 : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dv020] " + s);

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
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	public override void _Ready()
	{
		// PENSA ANTES DO CORPO: as teclas de acao sao lidas no quadro em que descem (a licao do `RoboDoTranse`).
		ProcessPriority = -100;
		_ = Rodar();
	}

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
		// O MUNDO VOLTA: nenhum botao de defeito e nenhuma tecla apertada sobrevivem ao roteiro.
		Chat.FocoPerdidoNaoFechaDeTeste = false;
		MenuJogo.FocoMorreNaRemontagemDeTeste = false;
		MarcaDeAlvo.DebaixoDoPisoDeTeste = false;
		Interacoes.TudoPedeParafusoDeTeste = false;
		Hud.AvisosDoCorpoMudosDeTeste = false;
		PassoSegurado.SemSegurarDeTeste = false;
		LocalPlayer.AgarradoNaoSeDebateDeTeste = false;
		TelaDeArmadura.NaoAbreAoFabricarDeTeste = TelaDeArmadura.EscSoDepoisDaPausaDeTeste = false;
		TelaDeInventario.EscSoDepoisDaPausaDeTeste = false;
		ChoqueDeKi.UmLadoSaiInteiroDeTeste = false;
		Saque.RoupaDoCorpoFicaDeTeste = false;
		foreach (string acao in new[] { "guard", "move_right", "move_left" }) Input.ActionRelease(Teclas.NomeNoMotor(acao));

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
		Nota(" A VERSAO 0.2.0 NA TELA -- o pedido de 2026-10-12");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 } && c.LocalId != 0
									  && M != null && S != null && World.Instancia != null && Hud.Instancia != null
									  && Chat.Instancia != null && Corpo != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, menu, HUD, chat, corpo) e o servidor e deste processo (--host)", pronto);
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv || Hud.Instancia is not { } hud
			|| Chat.Instancia is not { } chat || Corpo is not { } corpo) { Encerrar(2); return; }
		await Segundos(1.0);

		await F1_OChatQuePerdeOFoco(chat);
		await F2_OCampoQueAtravessaARemontagem(menu, cli);
		await F3_AMarcaAcimaDoPiso();
		F4_AObraSemParafuso();
		await F5_OsAvisosDoPainel(hud, cli, srv);
		await F6_OPasso(corpo);
		await F7_AgarradoSeDebate(hud, cli, srv, corpo);
		await F8_AMiraNoRabo(hud, cli);
		F9_AsRoupas();
		await F10_AArteDaArmadura(cli, srv);
		await F11_ARoupaDoCaido(cli, srv);
		await F12_AEstrelaDeUmLado();
		await F13_OEscDaMochila(cli, srv);

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// ---------------------------------------------------------------- F1
	private async Tarefa F1_OChatQuePerdeOFoco(Chat chat)
	{
		Nota("--- F1: o chat que perde o foco (o corpo travado depois do menu de interacao) ---");
		var camada = new CanvasLayer { Name = "PalcoDoChat", Layer = 50 };
		var botao = new Button { Text = "opcao de um menu" };
		camada.AddChild(botao);
		AddChild(camada);
		await Quadros(2);

		Toque(Key.Enter);
		await Quadros(3);
		Checa("ENTER abre a linha de fala, e o jogo sabe que ha alguem escrevendo",
			  Chat.Digitando && Foco.Digitando && chat.LinhaAbertaDeTeste);
		chat.EscreverDeTeste("meia frase");

		botao.GrabFocus();   // o que o clique num botao de OUTRA tela faz ao campo do chat
		await Quadros(2);
		Checa("**O CAMPO PERDEU O FOCO E O JOGO VOLTOU**: a linha fecha e nada mais diz que alguem esta escrevendo",
			  !Chat.Digitando && !Foco.Digitando && !chat.LinhaAbertaDeTeste && botao.HasFocus(),
			  $"Chat.Digitando={Chat.Digitando} Foco.Digitando={Foco.Digitando} linha={chat.LinhaAbertaDeTeste}");
		Checa("...e a frase pela metade ficou guardada no campo escondido", chat.RascunhoDeTeste == "meia frase", chat.RascunhoDeTeste);

		botao.ReleaseFocus();
		await Quadros(1);
		Toque(Key.Enter);
		await Quadros(3);
		Checa("...ela volta no proximo ENTER, com o teclado no campo", Chat.Digitando && chat.LinhaAbertaDeTeste && chat.RascunhoDeTeste == "meia frase",
			  chat.RascunhoDeTeste);
		Toque(Key.Escape);
		await Quadros(3);
		Checa("ESC continua desistindo de vez: fecha e apaga", !Chat.Digitando && !chat.LinhaAbertaDeTeste && chat.RascunhoDeTeste.Length == 0);

		Chat.FocoPerdidoNaoFechaDeTeste = true;
		Toque(Key.Enter);
		await Quadros(3);
		botao.GrabFocus();
		await Quadros(2);
		Injeta("(o campo que perde o foco nao fecha) o `Digitando` fica aceso sem campo nenhum com o teclado -- o corpo travado do playtest",
			   Chat.Digitando && Foco.Digitando && botao.HasFocus());
		Chat.FocoPerdidoNaoFechaDeTeste = false;
		botao.ReleaseFocus();
		Toque(Key.Escape);
		await Quadros(3);
		Checa("(limpeza) o chat fechou", !Chat.Digitando);
		camada.QueueFree();
		await Quadros(1);
	}

	// ---------------------------------------------------------------- F2
	private static LineEdit? CaixaDoBatismo(MenuJogo menu) =>
		menu.PaginaDeTeste("World") is { } pg
			? Todos(pg).OfType<LineEdit>().FirstOrDefault(l => l.PlaceholderText.StartsWith("novo nome", StringComparison.Ordinal))
			: null;

	private async Tarefa F2_OCampoQueAtravessaARemontagem(MenuJogo menu, GameClient cli)
	{
		Nota("--- F2: a caixa do batismo atravessa a remontagem da aba World ---");
		menu.Abrir();
		menu.IrPara("World");
		await Quadros(4);
		LineEdit? campo = CaixaDoBatismo(menu);
		Checa("(montagem) a caixa do batismo existe na aba World (o host e admin, e a Terra e um planeta)", campo != null,
			  $"direito={cli.DireitoDeBatismo} zona={cli.Zone.Name}");
		if (campo == null) { menu.Fechar(); return; }

		const string escrito = "Novo Nam";
		campo.GrabFocus();
		campo.Text = escrito;
		campo.EmitSignal(LineEdit.SignalName.TextChanged, escrito);
		campo.CaretColumn = 4;
		await Quadros(1);
		Checa("escrevendo na caixa, o jogo sabe que ha um campo de texto com o teclado (os atalhos ficam mudos)",
			  MenuJogo.Digitando && Foco.Digitando);

		ulong idVelho = campo.GetInstanceId();
		menu.ForcarRedesenho();
		await Quadros(2);
		LineEdit? novo = CaixaDoBatismo(menu);
		Checa("**A PAGINA REMONTOU E O CAMPO CONTINUA COM O TECLADO**: e outro node, com o foco, o texto e o cursor no lugar",
			  novo != null && novo.GetInstanceId() != idVelho && novo.HasFocus() && novo.Text == escrito && novo.CaretColumn == 4,
			  novo == null ? "(sem campo)" : $"foco={novo.HasFocus()} texto='{novo.Text}' cursor={novo.CaretColumn}");
		Checa("...e o jogo nunca deixou de saber que alguem escreve: a letra seguinte nao vira atalho", MenuJogo.Digitando && Foco.Digitando);

		MenuJogo.FocoMorreNaRemontagemDeTeste = true;
		menu.ForcarRedesenho();
		await Quadros(2);
		Injeta("(a pagina remontada nasce sem foco) o campo larga o teclado e o jogo volta a ler as letras como atalho",
			   !MenuJogo.Digitando && CaixaDoBatismo(menu) is { } solto && !solto.HasFocus());
		MenuJogo.FocoMorreNaRemontagemDeTeste = false;

		if (CaixaDoBatismo(menu) is { } limpar)
		{
			limpar.Text = "";
			limpar.EmitSignal(LineEdit.SignalName.TextChanged, "");
			limpar.ReleaseFocus();
		}
		menu.Fechar();
		await Quadros(2);
	}

	// ---------------------------------------------------------------- F3
	private async Tarefa F3_AMarcaAcimaDoPiso()
	{
		Nota("--- F3: a marca de alvo desenha acima do piso ---");
		if (World.Instancia is not { } mundo) { Checa("(montagem) ha um mundo de pe", false); return; }

		// O PISO DE VERDADE: as camadas de tile do mapa que nao ordenam por Y (o `Chao` e a decoracao).
		List<int> pisos = [.. Todos(mundo).OfType<TileMapLayer>().Where(l => l.ZIndex < 0).Select(l => l.ZIndex)];
		Checa("(montagem) o mapa tem camada de piso com plano negativo (o `Chao` do conversor e -2)", pisos.Count > 0 && pisos.Min() <= -2,
			  string.Join(",", pisos.Distinct()));
		if (pisos.Count == 0) return;

		var alvo = new Node2D { Name = "AlvoDePalco" };
		mundo.AddChild(alvo);
		var marca = new MarcaDeAlvo { Name = "Alvo" };
		alvo.AddChild(marca);
		await Quadros(1);
		int plano = alvo.ZIndex + marca.ZIndex;
		Checa("**A MARCA FICA ACIMA DO PISO**: o plano dela (relativo ao corpo marcado) e maior que o do chao e nao fica abaixo da decoracao",
			  plano > pisos.Min() && plano >= pisos.Max() && plano < 0, $"marca {plano}, pisos {string.Join(",", pisos.Distinct())}");
		marca.QueueFree();

		MarcaDeAlvo.DebaixoDoPisoDeTeste = true;
		var antiga = new MarcaDeAlvo { Name = "AlvoAntigo" };
		alvo.AddChild(antiga);
		await Quadros(1);
		Injeta("(a marca no plano -3 de antes) ela nasce ABAIXO do chao e nunca aparece", alvo.ZIndex + antiga.ZIndex < pisos.Min());
		MarcaDeAlvo.DebaixoDoPisoDeTeste = false;
		alvo.QueueFree();
		await Quadros(1);
	}

	// ---------------------------------------------------------------- F4
	private void F4_AObraSemParafuso()
	{
		Nota("--- F4: a obra sem parafuso nao pisca de 'solta' ---");
		var arvore = new ObraDesenhada { Tipo = Jandirus.Core.Items.CatalogoDeItens.ArvoreDeMaca, Aparafusada = false };
		var tanque = new ObraDesenhada { Tipo = Regenerador.Tipo, Aparafusada = false };
		var tanqueFixo = new ObraDesenhada { Tipo = Regenerador.Tipo, Aparafusada = true };
		Checa("**A MACIEIRA PLANTADA NAO PISCA**: ela nasce sem parafuso (nao tem `Bolt`) e o contorno amarelo nao se acende",
			  !arvore.PiscaDeSolta);
		Checa("CONTRA-EXEMPLO: o regenerador solto pisca, e aparafusado para", tanque.PiscaDeSolta && !tanqueFixo.PiscaDeSolta);
		Interacoes.TudoPedeParafusoDeTeste = true;
		Injeta("(toda obra pede parafuso) a macieira volta a piscar de 'solta'", arvore.PiscaDeSolta);
		Interacoes.TudoPedeParafusoDeTeste = false;
		arvore.Free();
		tanque.Free();
		tanqueFixo.Free();
	}

	// ---------------------------------------------------------------- F5
	private async Tarefa F5_OsAvisosDoPainel(Hud hud, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F5: os avisos do painel de status (guarda, atordoado, agarrao) ---");
		SheetState limpa = cli.Sheet;
		limpa.Estado = 0;
		limpa.Estado2 = 0;
		limpa.Escape = 0;

		Checa("de pe e inteiro o painel nao escreve aviso nenhum", Hud.AvisosDoCorpo(limpa).Count == 0);
		Checa("guarda erguida (bit 4 do estado) -> \"GUARDA LEVANTADA\"",
			  Hud.AvisosDoCorpo(limpa with { Estado = 4 }).Select(a => a.Texto).SequenceEqual([Hud.AvisoDeGuarda]));
		Checa("atordoado -> \"ATORDOADO\"", Hud.AvisosDoCorpo(limpa with { Estado2 = 4 }).Select(a => a.Texto).SequenceEqual([Hud.AvisoDeAtordoado]));
		string agarrado = Hud.AvisosDoCorpo(limpa with { Estado2 = 8, Escape = 37 }).Select(a => a.Texto).FirstOrDefault() ?? "";
		Checa("agarrado -> diz que as teclas de andar soltam, com o andamento da luta", agarrado.StartsWith("AGARRADO") && agarrado.Contains("teclas de andar") && agarrado.Contains("37%"), agarrado);
		Checa("...pelo rabo -> \"RABO PRESO\"; com o pescoco apertado -> \"ESTRANGULADO\"",
			  Hud.AvisosDoCorpo(limpa with { Estado2 = 8 | 16 })[0].Texto.StartsWith("RABO PRESO")
			  && Hud.AvisosDoCorpo(limpa with { Estado2 = 8 | 32 })[0].Texto.StartsWith("ESTRANGULADO"));
		Checa("quem segura alguem le o que o soco e o passo fazem", Hud.AvisosDoCorpo(limpa with { Estado2 = 64 })[0].Texto.Contains("estrangula"));
		Checa("caido nao ha aviso: o corpo no chao ja e a leitura", Hud.AvisosDoCorpo(limpa with { Estado = 1 | 4, Estado2 = 4 }).Count == 0);
		// (2026-10-16) O MORTO DE PE TEM AVISO: *"n aparece a informaçao de guarda levantada quando vc defende quando morto"*. De visita (o
		// bit do passe) o morto esta de pe em qualquer zona -- e a mesma pergunta do Outro Mundo (`LocalPlayer.Caido`).
		Checa("MORTO DE PE (de visita) com a guarda erguida -> \"GUARDA LEVANTADA\"",
			  Hud.AvisosDoCorpo(limpa with { Estado = 2 | 4, Estado3 = 1 }).Select(a => a.Texto).SequenceEqual([Hud.AvisoDeGuarda]));
		LocalPlayer.VisitaDeitaDeTeste = true;
		try
		{
			Injeta("(o passe ignorado: o morto de visita conta como cadaver) o mesmo corpo fica sem aviso -- o corte de antes, pro morto de pe",
				   Hud.AvisosDoCorpo(limpa with { Estado = 2 | 4, Estado3 = 1 }).Count == 0);
		}
		finally { LocalPlayer.VisitaDeitaDeTeste = false; }

		// ---- pelo fio: a tecla de guarda de verdade
		Input.ActionPress(Teclas.NomeNoMotor("guard"));
		bool subiu = await Ate(() => hud.AvisosDoCorpoDeTeste.Contains(Hud.AvisoDeGuarda), 3);
		Checa("**GUARDA LEVANTADA, PELA TECLA**: segurar a guarda acende o aviso no painel -- e quem diz e a ficha do servidor",
			  subiu && cli.Sheet.Guarda, string.Join(" | ", hud.AvisosDoCorpoDeTeste));

		Hud.AvisosDoCorpoMudosDeTeste = true;
		hud.RedesenharOsAvisosDoCorpoDeTeste(cli.Sheet);
		Injeta("(o painel sem os avisos) com a guarda erguida no servidor a tela nao diz nada", cli.Sheet.Guarda && hud.AvisosDoCorpoDeTeste.Count == 0);
		Hud.AvisosDoCorpoMudosDeTeste = false;
		hud.RedesenharOsAvisosDoCorpoDeTeste(cli.Sheet);

		Input.ActionRelease(Teclas.NomeNoMotor("guard"));
		bool desceu = await Ate(() => !hud.AvisosDoCorpoDeTeste.Contains(Hud.AvisoDeGuarda), 3);
		Checa("...e soltar a tecla apaga", desceu && !cli.Sheet.Guarda);

		// ---- pelo fio: o atordoamento que so o servidor conhece
		Checa("(montagem) o servidor atordoou o corpo por 1,2 s", srv.AtordoarDeTeste(cli.LocalId, 1.2));
		bool atordoado = await Ate(() => hud.AvisosDoCorpoDeTeste.Contains(Hud.AvisoDeAtordoado), 1.0);
		Checa("**ATORDOADO**: o relogio do servidor aparece no painel de quem esta atordoado", atordoado, string.Join(" | ", hud.AvisosDoCorpoDeTeste));
		bool passou = await Ate(() => !hud.AvisosDoCorpoDeTeste.Contains(Hud.AvisoDeAtordoado), 3);
		Checa("...e some quando o atordoamento acaba", passou);
	}

	// ---------------------------------------------------------------- F6
	private async Tarefa F6_OPasso(LocalPlayer corpo)
	{
		Nota("--- F6: um toque na tecla mostra um passo, e nao um deslize ---");
		// a regra pura
		var passo = new PassoSegurado();
		bool noToque = passo.Segurar(true, 0.05);
		int quadrosSegurados = 0;
		while (passo.Segurar(false, 0.02) && quadrosSegurados < 100) quadrosSegurados++;
		Checa("um toque de 50 ms deixa a pose de andar de pe ate fechar o minimo do passo",
			  noToque && Math.Abs(0.05 + quadrosSegurados * 0.02 - PassoSegurado.MinimoDoPasso) < 0.05, $"{0.05 + quadrosSegurados * 0.02:0.00} s");
		var longo = new PassoSegurado();
		for (int i = 0; i < 60; i++) longo.Segurar(true, 0.02);
		Checa("CONTRA-EXEMPLO: quem andou mais que o minimo para na hora em que para (nao ha marcha no lugar)", !longo.Segurar(false, 0.02));

		if (World.Instancia?.VisualLocalDeTeste is not { } visual) { Checa("(montagem) o corpo local tem desenho", false); return; }
		string direita = Teclas.NomeNoMotor("move_right");

		Vector2 antes = corpo.GlobalPosition;
		Input.ActionPress(direita);
		await Quadros(4);
		Input.ActionRelease(direita);
		await Quadros(2);
		Checa("(montagem) o toque moveu o corpo", corpo.GlobalPosition.DistanceTo(antes) > 0.5f, $"{corpo.GlobalPosition.DistanceTo(antes):0.0} px | {corpo.PorQueNaoAnda}");
		Checa("**O TOQUE MOSTRA O PASSO**: solta a tecla, a pose de andar continua de pe (a perna chega a mexer)", visual.AndandoDeTeste);
		Checa("...no ritmo do corpo: o da velocidade dele", Math.Abs(visual.RitmoDoPasso - MoveRules.RitmoDoPasso(corpo.SpeedStat)) < 1e-4
			  && Math.Abs(visual.RitmoDaPernadaDeTeste - visual.RitmoDoPasso) < 1e-4, $"{visual.RitmoDoPasso:0.00}");
		await Segundos(PassoSegurado.MinimoDoPasso + 0.2);
		Checa("...e cai sozinha depois do minimo", !visual.AndandoDeTeste);

		PassoSegurado.SemSegurarDeTeste = true;
		Input.ActionPress(direita);
		await Quadros(4);
		Input.ActionRelease(direita);
		await Quadros(2);
		Injeta("(a pose que segue o deslocamento quadro a quadro) solta a tecla, a pose ja caiu -- o corpo andou sem mexer a perna", !visual.AndandoDeTeste);
		PassoSegurado.SemSegurarDeTeste = false;
		await Segundos(0.4);
	}

	// ---------------------------------------------------------------- F7
	private async Tarefa F7_AgarradoSeDebate(Hud hud, GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo)
	{
		Nota("--- F7: agarrado, apertar a direcao e se debater ---");
		int quem = srv.AgarrarOJogadorDeTeste(cli.LocalId, 1.0, porTras: false);
		Checa("(montagem) um corpo de bancada de poder igual agarrou o jogador PELA FRENTE, pela porta de producao", quem != 0);
		if (quem == 0) return;
		string esquerda = Teclas.NomeNoMotor("move_left");
		try
		{
			bool soube = await Ate(() => cli.Sheet.Agarrado, 2);
			Checa("a ficha chega dizendo AGARRADO, e o painel escreve como sair",
				  soube && hud.AvisosDoCorpoDeTeste.Any(a => a.StartsWith("AGARRADO") && a.Contains("teclas de andar")),
				  string.Join(" | ", hud.AvisosDoCorpoDeTeste));
			Checa("o corpo local sabe por que nao anda (nao integra a tecla: nao ha correcao por pacote)", corpo.PorQueNaoAnda.StartsWith("agarrado"), corpo.PorQueNaoAnda);

			await Segundos(1.0);
			Checa("sem apertar direcao a luta nao anda: um segundo depois o contador esta em zero",
				  srv.AgarraoDeTeste(cli.LocalId) is { PorQuem: not 0, Contador: 0 }, $"{srv.AgarraoDeTeste(cli.LocalId)}");

			LocalPlayer.AgarradoNaoSeDebateDeTeste = true;
			Input.ActionPress(esquerda);
			await Segundos(1.0);
			Injeta("(o bit de andar so leva o deslocamento) a tecla apertada nao chega ao servidor como luta: o contador continua em zero",
				   srv.AgarraoDeTeste(cli.LocalId) is { PorQuem: not 0, Contador: 0 } && !corpo.DebatendoDeTeste);
			LocalPlayer.AgarradoNaoSeDebateDeTeste = false;

			Checa("com a direcao apertada, o corpo preso manda a INTENCAO de andar", corpo.DebatendoDeTeste);
			bool andou = await Ate(() => cli.Sheet.Escape > 0 || !cli.Sheet.Agarrado, 3);
			Checa("**A LUTA ANDA**: o andamento sobe na ficha (e o painel escreve o %)", andou, $"{cli.Sheet.Escape}%");
			bool soltou = await Ate(() => !cli.Sheet.Agarrado, 25);
			Checa("**APERTAR A DIRECAO SOLTA**: de poder igual, o jogador se livra do aperto", soltou && srv.AgarraoDeTeste(cli.LocalId) is { PorQuem: 0 });
			await Quadros(3);
			Checa("...e o aviso some do painel", !hud.AvisosDoCorpoDeTeste.Any(a => a.Contains("AGARRADO")), string.Join(" | ", hud.AvisosDoCorpoDeTeste));
		}
		finally
		{
			LocalPlayer.AgarradoNaoSeDebateDeTeste = false;
			Input.ActionRelease(esquerda);
			srv.TirarOAgarradorDeTeste(quem);
		}
		await Segundos(0.5);
		await F7b_PeloRabo(hud, cli, srv);
	}

	/// <summary>O Saiyajin da bancada agarrado POR TRAS: e pelo rabo -- o painel diz, o poder cai, e soltar devolve.</summary>
	private async Tarefa F7b_PeloRabo(Hud hud, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F7b: agarrado por tras, o Saiyajin e pego pelo rabo ---");
		if (!cli.Sheet.Rabo) { Checa("(montagem) o personagem da bancada tem rabo (rode com --raca Saiyan)", false); return; }
		double inteiro = cli.Sheet.Inteireza;
		int quem = srv.AgarrarOJogadorDeTeste(cli.LocalId, 1.0, porTras: true);
		Checa("(montagem) o corpo de bancada agarrou o jogador POR TRAS", quem != 0);
		if (quem == 0) return;
		try
		{
			bool soube = await Ate(() => cli.Sheet.RaboPreso, 2);
			Checa("**RABO PRESO**: a ficha diz, e o painel escreve (com a saida: as teclas de andar)",
				  soube && hud.AvisosDoCorpoDeTeste.Any(a => a.StartsWith("RABO PRESO") && a.Contains("teclas de andar")),
				  string.Join(" | ", hud.AvisosDoCorpoDeTeste));
			bool caiu = await Ate(() => cli.Sheet.Inteireza < inteiro * 0.5, 2);
			Checa("...o \"% efetivo\" do painel despenca: o poder some enquanto o rabo esta na mao de alguem", caiu,
				  $"{inteiro * 100:0}% -> {cli.Sheet.Inteireza * 100:0}%");
			Checa("...e o corpo fica atordoado (o painel tambem diz)", await Ate(() => hud.AvisosDoCorpoDeTeste.Contains(Hud.AvisoDeAtordoado), 2));
		}
		finally { srv.TirarOAgarradorDeTeste(quem); }
		bool voltou = await Ate(() => !cli.Sheet.Agarrado && cli.Sheet.Inteireza > inteiro * 0.9, 3);
		Checa("SOLTO, o poder volta e os avisos somem", voltou && !hud.AvisosDoCorpoDeTeste.Any(a => a.Contains("RABO")),
			  $"{cli.Sheet.Inteireza * 100:0}% | {string.Join(" | ", hud.AvisosDoCorpoDeTeste)}");
		await Segundos(0.5);
	}

	// ---------------------------------------------------------------- F8
	private async Tarefa F8_AMiraNoRabo(Hud hud, GameClient cli)
	{
		Nota("--- F8: a mira no rabo (o gancho do seletor e a tecla 6) ---");
		ZonePicker? seletor = Todos(hud).OfType<ZonePicker>().FirstOrDefault();
		Checa("(montagem) o seletor de zona esta no HUD", seletor != null);
		if (seletor == null) return;

		seletor.ClicarDeTeste(28, 12);   // o gancho, a direita da cintura (eixo do BYOND: y de baixo pra cima)
		bool mirou = await Ate(() => cli.ZonaMirada == 6, 2);
		Checa("**CLICAR NO GANCHO MIRA O RABO**: o servidor recebe a zona 6 e o seletor a marca", mirou && seletor.SelecionadoDeTeste == ZonePicker.SelDoRabo,
			  $"zona {cli.ZonaMirada}, seletor '{seletor.SelecionadoDeTeste}'");
		Checa("...e o painel diz \"mira: rabo\"", Todos(hud).OfType<Label>().Any(l => l.Text == "mira: rabo"));

		Toque(Key.Key3);
		bool abdomen = await Ate(() => cli.ZonaMirada == 3, 2);
		Checa("CONTRA-EXEMPLO: a tecla 3 continua mirando o abdomen (e o gancho apaga)", abdomen && seletor.SelecionadoDeTeste == "groin", seletor.SelecionadoDeTeste);
		Toque(Key.Key6);
		bool pelaTecla = await Ate(() => cli.ZonaMirada == 6, 2);
		Checa("**A TECLA 6 MIRA O RABO**, e o seletor acompanha", pelaTecla && seletor.SelecionadoDeTeste == ZonePicker.SelDoRabo);
		Checa("um clique fora do gancho e do boneco nao muda a mira", ClicarEManter(seletor, 2, 2) == ZonePicker.SelDoRabo);
		Toque(Key.Key0);
		await Ate(() => cli.ZonaMirada == 0, 2);
	}

	// ---------------------------------------------------------------- F9
	private void F9_AsRoupas()
	{
		Nota("--- F9: as roupas da criacao (a pasta inteira, menos a do Beerus e o rabo) ---");
		if (VistosDeGente.Catalogo is not { } cat) { Checa("(montagem) o cliente tem o catalogo de aparencia", false); return; }
		const string pasta = "res://Assets/Sprites/Clothes";
		string[] novas = Jandirus.Server.GameServer.RoupasNovasDaV020;
		Checa($"o catalogo que monta a grade da criacao tem as onze folhas que faltavam ({cat.Roupas.Count} roupas ao todo)",
			  novas.All(n => cat.Roupas.Contains($"{pasta}/{n}.tres")), string.Join(", ", novas.Where(n => !cat.Roupas.Contains($"{pasta}/{n}.tres"))));
		Checa("...e nao tem o rabo nem a do Beerus", !cat.Roupas.Exists(c => c.EndsWith("/Tail.tres") || c.Contains("Beerus")));

		// A FOLHA ABRE COM DESENHO -- e a mesma porta que a previa do boneco usa (`FolhasPresas`).
		var semDesenho = new List<string>();
		foreach (string n in novas)
		{
			SpriteFrames? f = FolhasPresas.Carregar($"{pasta}/{n}.tres");
			if (f == null || !f.GetAnimationNames().Any(a => f.GetFrameCount(a) > 0 && f.GetFrameTexture(a, 0) != null)) semDesenho.Add(n);
		}
		Checa("**CADA UMA DAS ONZE ABRE COM DESENHO** (as seis que so tinham o PNG ganharam a folha de animacao)", semDesenho.Count == 0,
			  string.Join(", ", semDesenho));
	}

	// ---------------------------------------------------------------- F10
	private async Tarefa F10_AArteDaArmadura(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F10: fabricar uma armadura abre a tela da arte ---");
		if (TelaDeArmadura.Instancia is not { } tela || VistosDeGente.Catalogo is not { } cat
			|| GetTree().Root.FindChild("Inventario", true, false) is not TelaDeInventario mochila)
		{ Checa("(montagem) a tela da armadura e a mochila estao montadas", false); return; }

		const double zeni = 50_000;
		Checa("(montagem) no bolso: zeni e tecnologia pra fabricar armadura", srv.EconomiaDeTeste(cli.LocalId, zeni, 0, 30));
		await Ate(() => cli.Zeni == zeni, 3);
		int cruas = cli.Mochila.Quantos(ArmaduraDeTech.Id);

		// ---- a contraprova primeiro: sem o gatilho, a armadura chega e ninguem pergunta a arte
		TelaDeArmadura.NaoAbreAoFabricarDeTeste = true;
		cli.SendTech("construir", ArmaduraDeTech.Id);
		tela.EsperarAFabricada();
		bool chegou = await Ate(() => cli.Mochila.Quantos(ArmaduraDeTech.Id) == cruas + 1, 4);
		await Quadros(3);
		Injeta("(fabricar nao abre a tela) a armadura crua chega a mochila e nada pergunta a arte -- o item parado de antes", chegou && !tela.AbertaDeTeste,
			   $"cruas {cli.Mochila.Quantos(ArmaduraDeTech.Id)}");
		TelaDeArmadura.NaoAbreAoFabricarDeTeste = false;

		// ---- o que a aba Tech faz no clique de "Fabricar": manda o pedido e avisa a tela
		cli.SendTech("construir", ArmaduraDeTech.Id);
		tela.EsperarAFabricada();
		bool abriu = await Ate(() => tela.AbertaDeTeste, 4);
		Checa("**FABRICAR UMA ARMADURA ABRE A TELA DA ARTE** -- quando a mochila confirma que ela chegou",
			  abriu && cli.Mochila.Quantos(ArmaduraDeTech.Id) == cruas + 2, $"cruas {cli.Mochila.Quantos(ArmaduraDeTech.Id)}");
		(List<string> artes, int comDesenho) = tela.GradeDeTeste();
		Checa($"a grade oferece todas as artes da pasta Armor ({artes.Count}), cada uma com o seu desenho, e nao a do Beerus",
			  artes.Count == ArmaduraDeTech.Escolhiveis(cat).Count && artes.Count >= 45 && comDesenho == artes.Count
			  && !artes.Exists(a => a.Contains("Beerus")), $"{comDesenho}/{artes.Count} com desenho");

		Checa("(montagem) apertei a arte 'Armor 8'", tela.EscolherDeTeste("Armor 8"));
		const string peca = "Armadura|Armor 8|";
		bool moldou = await Ate(() => cli.Mochila.Quantos(peca) == 1, 4);
		Checa("**ESCOLHIDA, A ARMADURA CRUA VIRA A PECA** na mochila, e a tela fecha",
			  moldou && cli.Mochila.Quantos(ArmaduraDeTech.Id) == cruas + 1 && !tela.AbertaDeTeste);

		// ---- pela mochila: a crua que sobrou tem a acao "Escolher a arte"
		mochila.Abrir();
		await Quadros(3);
		// (o slot se acha pelo NOME que a mochila escreve: o da construcao, o mesmo do card da aba Tech)
		string nomeDaCrua = (CatalogoDeItens.Get(ArmaduraDeTech.Id)?.Nome ?? ArmaduraDeTech.Id) + "\n";
		Button? slot = Todos(mochila).OfType<Button>().FirstOrDefault(b => b.TooltipText.StartsWith(nomeDaCrua, StringComparison.Ordinal));
		slot?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(3);
		Button? acao = Todos(mochila).OfType<Button>().FirstOrDefault(b => b.Text == "Escolher a arte" && b.IsVisibleInTree());
		Checa("na mochila, a armadura crua tem a acao 'Escolher a arte'", slot != null && acao != null);
		acao?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(3);
		Checa("...que abre a MESMA tela", tela.AbertaDeTeste);

		// A CONTRAPROVA DO ESC primeiro, com a grade SOZINHA na tela: ouvido depois da pausa, ele abre a PAUSA por cima e a
		// grade fica aberta atras. (A mochila sai de baixo so pra esta linha: ela tambem ouve o Esc no `_Input`, e com a
		// grade muda quem responderia seria ela, antes da pausa. A contraprova DELA e a F13.)
		mochila.Fechar();
		TelaDeArmadura.EscSoDepoisDaPausaDeTeste = true;
		Toque(Key.Escape);
		await Quadros(3);
		Injeta("(o Esc da grade ouvido depois da pausa) a pausa abre por cima e a grade continua aberta atras",
			   tela.AbertaDeTeste && PauseMenu.Instancia is { Aberto: true });
		TelaDeArmadura.EscSoDepoisDaPausaDeTeste = false;
		if (PauseMenu.Instancia is { Aberto: true } pausa) pausa.Fechar("a bancada fechou");
		mochila.Abrir();   // de volta pra baixo da grade, como estava quando a grade abriu por ela
		await Quadros(2);

		// UMA CAMADA POR VEZ: as duas telas ouvem o Esc no `_Input`, e quem responde e a da frente (o node da grade vem
		// depois do da mochila na arvore). A mochila de baixo so fecha no Esc seguinte.
		Toque(Key.Escape);
		await Quadros(3);
		Checa("**O ESC FECHA A GRADE, E SO ELA**: a mochila de baixo continua aberta, a pausa nao abre, nada foi moldado e a crua continua na mochila",
			  !tela.AbertaDeTeste && MochilaAberta(mochila) && PauseMenu.Instancia is not { Aberto: true }
			  && cli.Mochila.Quantos(ArmaduraDeTech.Id) == cruas + 1,
			  $"grade={tela.AbertaDeTeste} mochila={MochilaAberta(mochila)} pausa={PauseMenu.Instancia?.Aberto} cruas={cli.Mochila.Quantos(ArmaduraDeTech.Id)}");
		mochila.Fechar();
		await Quadros(2);

		// ---- vestida, ela protege
		if (cli.Vestindo.Count >= Jandirus.Core.Appearance.Appearance.MaxRoupa)
		{
			cli.SendVerbo($"item_{RoupaGuardada.AcaoTirar}", RoupaGuardada.De(cli.Vestindo[0]).Id);
			await Ate(() => cli.Vestindo.Count < Jandirus.Core.Appearance.Appearance.MaxRoupa, 3);
		}
		await Segundos(0.5);
		(double defesaAntes, bool tinha) = srv.DefesaDeTeste(cli.LocalId);
		cli.SendVerbo($"item_{RoupaGuardada.AcaoVestir}", peca);
		bool vestiu = await Ate(() => cli.Vestindo.Exists(p => p.Caminho.EndsWith("/Clothes/Armor/Armor 8.tres")), 4);
		bool protege = await Ate(() => srv.DefesaDeTeste(cli.LocalId) is { Armadura: true } d && d.Defesa > defesaAntes, 3);
		Checa("**VESTIDA PELA MOCHILA, A ARMADURA PROTEGE**: a peca aparece na fileira 'vestindo' e a defesa fisica sobe no servidor",
			  !tinha && vestiu && protege, $"defesa {defesaAntes:0.###} -> {srv.DefesaDeTeste(cli.LocalId).Defesa:0.###}");
	}

	// ---------------------------------------------------------------- F11
	private async Tarefa F11_ARoupaDoCaido(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F11: revistar um nocauteado mostra a roupa do corpo ---");
		// (2026-10-13) QUEM DESENHA O SAQUE E A TELA DA MOCHILA, aberta na mochila do caido -- o menu de botoes da tecla E
		// saiu. A tecla E continua sendo o gesto que pergunta.
		if (TelaDeInventario.Instancia is not { } telaI || World.Instancia is not { } mundo)
		{ Checa("(montagem) a tela da mochila esta montada", false); return; }

		int caido = srv.CaidoVestidoDeTeste(cli.LocalId);
		Checa("(montagem) um corpo de bancada nocauteado e vestido (camisa tingida + armadura) apareceu ao lado", caido != 0);
		if (caido == 0) return;
		const string camisa = "Roupa|Clothes_GiTop|2A2A33";
		try
		{
			bool visto = await Ate(() => mundo.PosicaoDesenhadaDe(cli.LocalId) is { } eu
										 && mundo.CaidoPerto(eu, Interacoes.Alcance, cli.CadaverPerto) is { } c && c.Id == caido, 5);
			Checa("o cliente ve o caido ao alcance da tecla E", visto);

			Toque(Key.E);
			bool abriu = await Ate(() => telaI.SaqueDeTeste.Aberto, 4);
			List<string> botoes = telaI.RotulosDoSaqueDeTeste();
			Checa("**REVISTAR UM NOCAUTEADO MOSTRA A ROUPA DO CORPO**: 'Tirar do corpo' pra camisa e pra armadura",
				  abriu && botoes.Contains("Tirar do corpo: Clothes_GiTop") && botoes.Contains("Tirar do corpo: Armor 8"), string.Join(" | ", botoes));

			int minhas = cli.Mochila.Quantos(camisa);
			Checa("(montagem) apertei 'Tirar do corpo: Clothes_GiTop'", telaI.LevarDoSaqueDeTeste("Tirar do corpo: Clothes_GiTop"));
			bool levou = await Ate(() => cli.Mochila.Quantos(camisa) == minhas + 1, 4);
			Checa("**A PECA SAI DO CORPO DELE PRA MINHA MOCHILA**, com a cor que tinha",
				  levou && !srv.RoupaDeTeste(caido).Contains(camisa), $"ele veste: {string.Join(", ", srv.RoupaDeTeste(caido))}");
			bool voltou = await Ate(() => telaI.SaqueDeTeste is { Aberto: true, Itens: 1 }, 4);
			Checa("...e a tela volta com o que sobrou: so a armadura",
				  voltou && telaI.RotulosDoSaqueDeTeste().SequenceEqual(["Tirar do corpo: Armor 8"]), string.Join(" | ", telaI.RotulosDoSaqueDeTeste()));

			Toque(Key.Escape);
			bool fechou = await Ate(() => !telaI.SaqueDeTeste.Aberto, 2);
			Checa("o Esc fecha a tela de revistar", fechou && PauseMenu.Instancia is not { Aberto: true },
				  $"tela={telaI.SaqueDeTeste.Aberto} pausa={PauseMenu.Instancia?.Aberto}");
			Saque.RoupaDoCorpoFicaDeTeste = true;
			Toque(Key.E);
			bool vazio = await Ate(() => telaI.SaqueDeTeste is { Aberto: true, Itens: 0 }, 4);
			Injeta("(a roupa do corpo fica) a tela de revistar volta a nao ter nada no corpo pra levar -- o saque de antes", vazio, telaI.SaqueDeTeste.Titulo);
			Saque.RoupaDoCorpoFicaDeTeste = false;
		}
		finally
		{
			Saque.RoupaDoCorpoFicaDeTeste = false;
			Toque(Key.Escape);
			srv.TirarOCorpoDeTeste(caido);
		}
		await Quadros(3);
	}

	// ---------------------------------------------------------------- F12
	/// <summary>
	/// A ESTRELA DE UM LADO SO, no que este robo consegue ver: o que o node manda ao shader. Isto e INTENCAO, e nao
	/// pixel -- se o corte apaga a metade certa so uma foto diz (`--diagembateki`), e este robo roda sem janela.
	/// </summary>
	private async Tarefa F12_AEstrelaDeUmLado()
	{
		Nota("--- F12: a estrela da colisao de um lado so (o uniform do shader) ---");
		if (World.Instancia is not { } mundo) { Checa("(montagem) ha um mundo de pe", false); return; }
		var estrela = new ChoqueDeKi { Name = "EstrelaDePalco", SemLuz = true };
		estrela.Definir(new Vector2(-4000, -4000), Vector2.Right, 30f, 18f, Colors.Cyan, Colors.Cyan);
		mundo.AddChild(estrela);
		await Quadros(2);

		Checa("a estrela de dois feixes nasce INTEIRA (o shader recebe `so_um_lado` = 0)", Mathf.IsEqualApprox(estrela.UmLadoNoShaderDeTeste, 0f),
			  $"{estrela.UmLadoNoShaderDeTeste}");
		estrela.SoUmLado = true;
		await Quadros(1);
		Checa("**COM UM ATAQUE SO, A ESTRELA E SO DO LADO DELE**: o shader recebe `so_um_lado` = 1", Mathf.IsEqualApprox(estrela.UmLadoNoShaderDeTeste, 1f),
			  $"{estrela.UmLadoNoShaderDeTeste}");
		// o tamanho mudou: o node reescreve os uniforms todos, e o corte tem de continuar la
		estrela.Definir(new Vector2(-4000, -4000), Vector2.Right, 40f, 22f, Colors.Cyan, Colors.Cyan);
		await Quadros(1);
		Checa("...e continua de um lado so quando o raio engrossa (os uniforms sao reescritos juntos)", Mathf.IsEqualApprox(estrela.UmLadoNoShaderDeTeste, 1f));

		ChoqueDeKi.UmLadoSaiInteiroDeTeste = true;
		estrela.SoUmLado = false;
		estrela.SoUmLado = true;
		await Quadros(1);
		Injeta("(a estrela de uma ponta so sai inteira) o shader recebe 0 e pinta as duas metades -- a de antes", Mathf.IsEqualApprox(estrela.UmLadoNoShaderDeTeste, 0f));
		ChoqueDeKi.UmLadoSaiInteiroDeTeste = false;

		estrela.QueueFree();
		await Quadros(1);
	}

	// ---------------------------------------------------------------- F13
	/// <summary>A mochila esta desenhada? Ela nao tem porta de bancada: pergunta-se ao NODE se ha um `Control` na tela.</summary>
	private static bool MochilaAberta(TelaDeInventario mochila) =>
		mochila.GetChildren().OfType<Control>().Any(c => c.IsVisibleInTree());

	/// <summary>O teclado numerico da mochila esta na arvore? Fechado ele SAI dela (`QueueFree`), e e isso que se le.</summary>
	private static bool TecladoDaMochila(TelaDeInventario mochila) => Todos(mochila).OfType<TecladoNumerico>().Any();

	/// <summary>
	/// O ESC COM A MOCHILA ABERTA. Ela o ouvia no `_UnhandledInput`, onde o menu de pausa (o ultimo node da arvore) o
	/// recebe antes: a pausa abria por cima e a mochila continuava aberta atras -- com o botao dela dizendo "Fechar (I
	/// ou Esc)". Cada guarda do conserto (`TelaDeInventario._Input`) tem a sua linha aqui: a pausa so abre sem nada na
	/// frente, a pausa JA aberta e dona do Esc, o teclado numerico fecha antes da mochila, e quem digita fica com a
	/// tecla. A grade da armadura por cima da mochila e da F10.
	/// </summary>
	private async Tarefa F13_OEscDaMochila(GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F13: o Esc com a mochila aberta fecha a mochila, e nao abre a pausa ---");
		if (PauseMenu.Instancia is not { } pausa
			|| GetTree().Root.FindChild("Inventario", true, false) is not TelaDeInventario mochila)
		{ Checa("(montagem) a mochila e a tela de pausa de producao estao na arvore", false); return; }

		string Retrato() => $"mochila={MochilaAberta(mochila)} teclado={TecladoDaMochila(mochila)} pausa={pausa.Aberto} digitando={Foco.Digitando}";
		try
		{
			Checa("(montagem) nada aberto e ninguem escrevendo", !MochilaAberta(mochila) && !pausa.Aberto && !Foco.Digitando, Retrato());

			// ---- o gesto do jogador: a tecla I abre, o Esc fecha
			Toque(Key.I);
			await Quadros(3);
			Checa("a tecla I abre a mochila", MochilaAberta(mochila), Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("**O ESC FECHA A MOCHILA, E A PAUSA NAO ABRE** -- o botao dela diz \"Fechar (I ou Esc)\"",
				  !MochilaAberta(mochila) && !pausa.Aberto, Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("CONTRA-EXEMPLO: o Esc SEGUINTE, sem nada na frente, abre a pausa como sempre", pausa.Aberto && !MochilaAberta(mochila), Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("...que o Esc fecha de volta", !pausa.Aberto, Retrato());

			// ---- A CONTRAPROVA: ouvido depois da pausa, o Esc abre a PAUSA por cima e a mochila fica aberta atras
			Toque(Key.I);
			await Quadros(3);
			TelaDeInventario.EscSoDepoisDaPausaDeTeste = true;
			Toque(Key.Escape);
			await Quadros(3);
			Injeta("(o Esc da mochila ouvido depois da pausa) a pausa abre por cima e a mochila continua aberta atras -- o jogo de antes",
				   MochilaAberta(mochila) && pausa.Aberto, Retrato());
			TelaDeInventario.EscSoDepoisDaPausaDeTeste = false;

			// ---- o defeito deixou a pausa NA FRENTE da mochila: sem ele, o Esc e dela
			Toque(Key.Escape);
			await Quadros(3);
			Checa("**COM A PAUSA NA FRENTE O ESC E DELA**: ela fecha, e a mochila de tras fica", MochilaAberta(mochila) && !pausa.Aberto, Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("...e o Esc seguinte fecha a mochila", !MochilaAberta(mochila) && !pausa.Aberto, Retrato());

			// ---- UMA CAMADA POR VEZ: o teclado numerico (o ajuste dos pesos) fica por cima da mochila
			if (cli.Mochila.Quantos(CatalogoDeItens.Pesos) == 0)
			{
				srv.EconomiaDeTeste(cli.LocalId, 50_000, 0, 30);
				cli.SendTech("construir", CatalogoDeItens.Pesos);
			}
			bool pesos = await Ate(() => cli.Mochila.Quantos(CatalogoDeItens.Pesos) > 0, 4);
			await Quadros(3);   // a mochila que chega redesenha a grade: os botoes de baixo so depois dela
			Toque(Key.I);
			await Quadros(3);
			Button? slot = Todos(mochila).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.TooltipText.StartsWith("Pesos\n", StringComparison.Ordinal));
			slot?.EmitSignal(BaseButton.SignalName.Pressed);
			await Quadros(3);
			Button? ajustar = Todos(mochila).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "Ajustar o peso");
			ajustar?.EmitSignal(BaseButton.SignalName.Pressed);
			await Quadros(3);
			Checa("(montagem) ha pesos na mochila, e a acao 'Ajustar o peso' abriu o teclado numerico por cima dela",
				  pesos && slot != null && ajustar != null && MochilaAberta(mochila) && TecladoDaMochila(mochila), Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("**UMA CAMADA POR VEZ**: com o teclado numerico aberto o Esc fecha o TECLADO -- a mochila fica, e a pausa nao abre",
				  !TecladoDaMochila(mochila) && MochilaAberta(mochila) && !pausa.Aberto, Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("...e o Esc seguinte fecha a mochila", !MochilaAberta(mochila) && !pausa.Aberto, Retrato());

			// ---- QUEM DIGITA E DONO DO ESC: o chat ouve o `_Input` DEPOIS da mochila, e mesmo assim a tecla e dele
			Toque(Key.I);
			await Quadros(3);
			Toque(Key.Enter);
			await Quadros(3);
			Checa("(montagem) com a mochila aberta, ENTER abre a linha de fala", MochilaAberta(mochila) && Chat.Digitando && Foco.Digitando, Retrato());
			Toque(Key.Escape);
			await Quadros(3);
			Checa("**QUEM DIGITA E DONO DO ESC**: a frase desiste, a mochila continua aberta e a pausa nao abre",
				  !Chat.Digitando && MochilaAberta(mochila) && !pausa.Aberto, Retrato());
			Toque(Key.I);
			await Quadros(3);
			Checa("...e a tecla I continua fechando a mochila", !MochilaAberta(mochila) && !pausa.Aberto, Retrato());
		}
		finally
		{
			// A TELA VOLTA: nem o botao de defeito nem nada desta familia fica aberto por cima do que vier depois.
			TelaDeInventario.EscSoDepoisDaPausaDeTeste = false;
			if (Chat.Digitando) Toque(Key.Escape);
			mochila.Fechar();
			if (pausa.Aberto) pausa.Fechar("a bancada fechou");
		}
		await Quadros(2);
	}

	private static string ClicarEManter(ZonePicker seletor, int x, int y)
	{
		seletor.ClicarDeTeste(x, y);
		return seletor.SelecionadoDeTeste;
	}

	// =====================================================================
	// TECLADO DE MENTIRA, LER A TELA, ESPERAR
	// =====================================================================
	/// <summary>As duas metades preenchidas, como um teclado de verdade manda (a nota da `--diagmudez`).</summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = apertada });

	private static void Toque(Key k) { Tecla(k, true); Tecla(k, false); }

	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
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
