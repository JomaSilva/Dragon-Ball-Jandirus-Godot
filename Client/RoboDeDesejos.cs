using System.Collections;
using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA JANELA DE DESEJOS (`--diagdesejos`) -- a metade do CLIENTE do pedido do dono de 2026-10-10: *"ao
/// invocar o dragao das esferas, deveria aparecer um menu com varios desejos q vc pode escolher e caso tenha um
/// target o jogo vai perguntar se e pra vc ou pra outra pessoa q vc pode escolher e nao ter q abrir um menu e
/// colocar um id de pedido."*
///
/// ELE ANDA A JANELA INTEIRA, pelo caminho do dedo: invoca pelo verbo do jogador, espera a janela abrir SOZINHA,
/// clica nas linhas e nas respostas pelos botoes desenhados, aperta "PEDIR" -- e le o resultado dos DOIS lados:
/// o servidor (zeni, idade, quem voltou a vida, pedidos gastos, o dragao de pe; modo host) e a tela (a etapa, o
/// que esta escrito, o que esta apagado). E FOTOGRAFA as etapas: a lista, o confirmar, o "pra quem?".
///
/// O PALCO e montado pelo servidor do mesmo processo (`GameServer.PalcoDosDesejosDeTeste`): um Shenron de tres
/// pedidos erguido pelo proprio jogador (por isso "Poder" aparece apagado), duas pessoas ao lado (uma delas o
/// alvo MARCADO dele), uma longe, um morto e um morto de velhice.
///
/// O QUE ESTA BANCADA NAO MEDE: as regras dos desejos e dos alvos -- isso e do servidor, e a `--menudodragaoteste`
/// mede com contra-exemplo. Aqui os contra-exemplos sao os da JANELA: ela fecha quando o dragao vai embora, ela
/// mora embaixo do HUD (nao por cima do boneco do corpo), e a do Super Shenron so acende os desejos AO LADO de uma
/// Super Esfera (o passo 10 a mede dos dois lados do portao: de pe na Terra, e no espaco diante da esfera).
///
/// Rode com janela (no segundo monitor) -- as fotos precisam de desenho -- e num campo aberto (o berco da Terra
/// e dentro do Banco, e debaixo de teto o dragao some no breu):
///   --host --rede 8181 --horateste 0.5 --campoteste 8 --diagdesejos --raca Human --conta bancada_ddesejos --nome Goku
/// </summary>
public partial class RoboDeDesejos : Node
{
	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [], _fotos = [];
	private IEnumerator? _roteiro;
	private double _espera = 2.5;
	private bool _acabou;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[ddesejos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[ddesejos]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[ddesejos] " + s);

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		bool segue;
		try { segue = _roteiro.MoveNext(); }
		catch (Exception e)
		{
			// UMA EXCECAO NO ROTEIRO E UM VERMELHO, E NAO UM SILENCIO: sem isto o processo ficava vivo, sem placar.
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			segue = false;
		}
		if (!segue) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;
		MenuDeDesejos.FicaAbertaSemDragaoDeTeste = false;
		MenuDeDesejos.CobreOHudDeTeste = false;
		// (o roteiro desliga os dois de baixo no proprio passo; aqui e pra uma excecao no meio dele nao os deixar ligados.)
		if (S is { } servidor) servidor.SuperShenronDeQualquerLugarDeTeste = false;
		GameServer.EscutaDeAvisos = null;
		S?.DesfazerOPalcoDosDesejosDeTeste();
		foreach (string f in _fotos) Nota("foto  " + f);
		if (_falhou > 0) GD.PrintErr("[ddesejos] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print($"\n[ddesejos] ===== {_ok} OK, {_falhou} FALHA(S) =====");
	}

	// =====================================================================
	// LER E APERTAR A TELA
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	/// <summary>Um botao VISIVEL de uma pagina, pelo texto exato.</summary>
	private static Button? BotaoDe(Node? raiz, string texto) =>
		raiz == null ? null : Todos(raiz).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == texto);

	/// <summary>Aperta um botao da aba World pelo texto, rolando a pagina ate ele (o cartao do pedido e o ultimo).</summary>
	private static bool ApertarNaAbaWorld(string texto)
	{
		if (MenuJogo.Instancia?.PaginaDeTeste("World") is not { } pg || BotaoDe(pg, texto) is not { } b) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	private Image? Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto pra {nome} (headless nao renderiza): rode com janela"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		_fotos.Add(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		if (S is not { } srv) { Afirmar("(montagem) o robo roda no modo host, com o servidor no mesmo processo", false); yield break; }
		if (MenuDeDesejos.Instancia is not { } janela) { Afirmar("(montagem) a janela de desejos existe no mundo", false); yield break; }
		if (MenuJogo.Instancia is not { } menu) { Afirmar("(montagem) o menu do P existe no mundo", false); yield break; }
		int eu = cli.LocalId;

		srv.EmbateDeFoto_CeuLimpo(eu);

		// ------------------------------------------------------------ 0) um mundo SEM estatua
		Nota("-- 0) antes do palco: um mundo sem estatua --");
		bool haviaEstatua = cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Estatua);
		menu.Abrir();
		menu.IrPara("World");
		yield return 0.6;
		Control? antesDoPalco = menu.PaginaDeTeste("World");
		Afirmar("num mundo SEM estatua o cartao do pedido tem 'Ver os desejos' e NAO tem 'Derrubar a estátua' (nao ha o que derrubar)",
				!haviaEstatua && BotaoDe(antesDoPalco, "Ver os desejos") != null && BotaoDe(antesDoPalco, "Derrubar a estátua") == null,
				$"estatua aqui: {haviaEstatua}");
		menu.Fechar();
		yield return 0.2;

		(int marcado, int aoLado, int longe, int morto, int velho) = srv.PalcoDosDesejosDeTeste(eu);
		yield return 1.5;

		(int Pedidos, int Desejos, bool DragaoDePe, bool Existe) set = srv.SetDoPalcoDeTeste(eu);
		Afirmar("(montagem) o palco esta de pe: um set de 3 pedidos, acordado e sem dragao, e as cinco pessoas",
				marcado != 0 && aoLado != 0 && longe != 0 && morto != 0 && velho != 0 && set is { Existe: true, Desejos: 3, Pedidos: 0, DragaoDePe: false },
				$"set {set}");
		Afirmar("(montagem) a janela de desejos nasce FECHADA", !janela.NaTela);

		// ------------------------------------------------------------ 1) a aba World
		Nota("-- 1) a aba World: o cartao do pedido sem caixa de id --");
		menu.Abrir();
		menu.IrPara("World");
		yield return 0.6;
		Control? pg = menu.PaginaDeTeste("World");
		Button? ver = BotaoDe(pg, "Ver os desejos");
		bool caixaDeId = pg != null && Todos(pg).OfType<LineEdit>().Any(l => l.PlaceholderText.Contains("desejo", StringComparison.OrdinalIgnoreCase));
		Afirmar("com a estatua do palco de pe, o cartao do pedido tem 'Ver os desejos' e ganhou 'Derrubar a estátua' -- e NENHUMA caixa pra digitar id de desejo",
				ver != null && BotaoDe(pg, "Derrubar a estátua") != null && !caixaDeId && BotaoDe(pg, "PEDIR") == null);
		if (ver != null)
			for (Node? p = ver.GetParent(); p != null; p = p.GetParent())
				if (p is ScrollContainer rol) { rol.EnsureControlVisible(ver); break; }
		yield return 0.4;
		Fotografar("desejos-00-a-aba-world");

		// ------------------------------------------------------------ 2) o dragao sobe, a janela abre
		Nota("-- 2) invocar: a janela abre SOZINHA, na lista --");
		cli.SendVerbo("db_invocar");
		for (double t = 0; t < 4 && !janela.NaTela; t += 0.1) yield return 0.1;
		Afirmar("o dragao sobe e a janela de desejos ABRE SOZINHA pra quem invocou",
				janela.NaTela && srv.SetDoPalcoDeTeste(eu).DragaoDePe);
		Afirmar("...na lista, e o menu do P sai da frente", janela.EtapaDeTeste == "Lista" && !MenuJogo.Aberto, janela.EtapaDeTeste);
		yield return 1.5;   // o dragao termina de subir, pra foto

		Afirmar("o cabecalho diz QUAL dragao, quantos pedidos restam e quanto ele ainda espera",
				janela.NomeDesenhado == "SHENRON" && janela.ContagemDesenhada.Contains("3 de 3", StringComparison.Ordinal)
				&& janela.ContagemDesenhada.Contains("espera mais", StringComparison.Ordinal)
				&& janela.EstadoDesenhado.Contains("aguarda o seu pedido", StringComparison.Ordinal),
				$"'{janela.NomeDesenhado}' | '{janela.ContagemDesenhada}' | '{janela.EstadoDesenhado}'");

		Rect2 painel = janela.RetanguloDoPainel;
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		Afirmar("ela mora na DIREITA da tela: nao tapa o meio, que e onde o dragao acabou de subir",
				painel.Position.X > tela.X * 0.5f && painel.End.X <= tela.X + 1 && painel.Size.Y > tela.Y * 0.6f, $"painel {painel} numa tela de {tela}");

		// ...E EMBAIXO DO HUD, NAO EM CIMA DELE. Os dois retangulos sao lidos da TELA: o da janela e o dos dois
		// widgets do bloco da direita do HUD (o boneco do corpo e a mira).
		Control? boneco = Hud.Instancia?.FindChild("Corpo", true, false) as Control;
		Control? mira = Hud.Instancia?.FindChild("Mira", true, false) as Control;
		bool CobreOHud() => boneco != null && mira != null
			&& (janela.RetanguloDoPainel.Intersects(boneco.GetGlobalRect()) || janela.RetanguloDoPainel.Intersects(mira.GetGlobalRect()));
		Afirmar("...e EMBAIXO do HUD: nao cobre o boneco do corpo nem a mira (quem escolhe um desejo continua vendo o proprio corpo)",
				boneco != null && mira != null && boneco.IsVisibleInTree() && !CobreOHud(),
				$"janela {janela.RetanguloDoPainel} | boneco {boneco?.GetGlobalRect()} | mira {mira?.GetGlobalRect()}");
		MenuDeDesejos.CobreOHudDeTeste = true;
		yield return 0.3;
		Afirmar("(defeito injetado: a janela vai da margem de cima a de baixo) o MESMO criterio REPROVA -- ela cobre o boneco",
				CobreOHud(), $"janela {janela.RetanguloDoPainel}");
		MenuDeDesejos.CobreOHudDeTeste = false;
		yield return 0.3;
		Afirmar("...e desfeito o defeito ela volta pra baixo do HUD", !CobreOHud(), $"janela {janela.RetanguloDoPainel}");

		List<(string Id, string Nome, bool Pode, string Motivo)> linhas = janela.LinhasDesenhadas();
		// Linha que nao esta na tela volta vazia (e nao nula): as afirmacoes abaixo leem o texto dela.
		(string Id, string Nome, bool Pode, string Motivo) Linha(string id) =>
			linhas.Exists(l => l.Id == id) ? linhas.Find(l => l.Id == id) : ("", "", false, "");
		Afirmar("a lista traz os desejos do servidor com o nome de cada um: Riqueza, Juventude, Ressuscitar alguém...",
				linhas.Count >= 12 && Linha("dinheiro") is { Nome: "Riqueza", Pode: true } && Linha("juventude") is { Nome: "Juventude", Pode: true }
				&& Linha("reviver") is { Nome: "Ressuscitar alguém", Pode: true }, $"{linhas.Count} linhas");
		Afirmar("o que NAO pode ser pedido agora aparece APAGADO com o motivo: 'Poder' (ele ergueu o set) e os nao portados",
				Linha("poder") is { Pode: false } poder && poder.Motivo.Contains("SEU poder", StringComparison.Ordinal)
				&& Linha("imortalidade") is { Pode: false } imortal && imortal.Motivo.Contains("portado", StringComparison.Ordinal),
				$"poder: '{Linha("poder").Motivo}'");
		// A ORDEM NA TELA: os desejos sao as linhas ate o primeiro gesto (dispensar). Primeiro as que da pra pedir
		// -- a lista abre em "Riqueza", e "Nada" fecha o trecho --, depois as apagadas, sob o titulo delas.
		List<(string Id, string Nome, bool Pode, string Motivo)> desejosNaTela = [.. linhas.TakeWhile(l => l.Id != AcoesDoDragao.Dispensar)];
		int primeiraApagada = desejosNaTela.FindIndex(l => !l.Pode);
		List<string> titulos = janela.TitulosDesenhados();
		Afirmar("a lista COMECA pelo que da pra pedir (abre em 'Riqueza', 'Nada' por ultimo) e so depois vem o apagado, sob o titulo 'não dá pra pedir agora'",
				primeiraApagada > 1 && desejosNaTela[0].Id == "dinheiro" && desejosNaTela[primeiraApagada - 1].Id == "nada"
				&& desejosNaTela.Skip(primeiraApagada).All(l => !l.Pode)
				&& titulos.Count == 3 && titulos[0] == "ESCOLHA O SEU DESEJO" && titulos[1] == "NÃO DÁ PRA PEDIR AGORA" && titulos[2] == "OUTROS GESTOS",
				$"linhas: {string.Join(",", desejosNaTela.Select(l => l.Id + (l.Pode ? "" : "(x)")))} | titulos: {string.Join(" / ", titulos)}");
		Afirmar("os gestos em volta do dragao tambem sao linhas: dispensar, pedidos por invocacao, derrubar a estatua",
				Linha(AcoesDoDragao.Dispensar).Pode && Linha(AcoesDoDragao.Pedidos).Pode && Linha(AcoesDoDragao.Derrubar).Pode);
		Afirmar("a linha apagada nao responde ao clique: a janela continua na lista, e nada e pedido",
				!janela.ApertarLinha("poder") && !janela.ApertarLinha("imortalidade") && janela.EtapaDeTeste == "Lista"
				&& srv.SetDoPalcoDeTeste(eu) is { Pedidos: 0, DragaoDePe: true });
		bool voltarNaLista = janela.RetanguloDoVoltar.Size.Y > 0;
		Fotografar("desejos-01-a-lista");

		// ------------------------------------------------------------ 3) um desejo SEM alvo
		Nota("-- 3) um desejo sem alvo: confirma e pede --");
		double zeniAntes = srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? -1;
		bool clicou = janela.ApertarLinha("dinheiro");
		yield return 0.3;
		(string Nome, string Quem, string Custo) conf = janela.ConfirmarDesenhado();
		Afirmar("clicar numa linha SEM alvo vai direto pro CONFIRMAR, que diz o que vai ser pedido e quanto custa",
				clicou && janela.EtapaDeTeste == "Confirmar" && conf.Nome == "Riqueza" && conf.Quem.Length == 0
				&& conf.Custo.Contains("1 dos 3", StringComparison.Ordinal), $"{janela.EtapaDeTeste}: {conf}");
		Afirmar("...e ate ali NADA foi pedido: o zeni e a contagem do servidor nao mudaram",
				Math.Abs((srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? 0) - zeniAntes) < 1 && srv.SetDoPalcoDeTeste(eu).Pedidos == 0);
		Fotografar("desejos-02-confirmar");

		janela.ApertarBotao("Pedir");
		for (double t = 0; t < 3 && srv.SetDoPalcoDeTeste(eu).Pedidos == 0; t += 0.1) yield return 0.1;
		yield return 0.5;
		Afirmar("PEDIR concede: +50.000.000 de zeni no servidor, e UM pedido gasto",
				Math.Abs((srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? 0) - (zeniAntes + Desejos.ZeniDoDesejo)) < 1 && srv.SetDoPalcoDeTeste(eu).Pedidos == 1,
				$"zeni {zeniAntes:N0} -> {srv.CorpoDosDesejosDeTeste(eu)?.Zeni:N0}, pedidos {srv.SetDoPalcoDeTeste(eu).Pedidos}");
		Afirmar("...e a janela continua aberta, de volta na lista, com a contagem nova (restam 2 de 3)",
				janela.NaTela && janela.EtapaDeTeste == "Lista" && janela.ContagemDesenhada.Contains("2 de 3", StringComparison.Ordinal),
				janela.ContagemDesenhada);

		// ------------------------------------------------------------ 4) um desejo COM alvo
		Nota("-- 4) um desejo com alvo: pra mim ou pra outra pessoa? --");
		clicou = janela.ApertarLinha("juventude");
		yield return 0.4;
		List<(string Rotulo, string Nota, bool Pode, bool Marcado)> resp = janela.RespostasDesenhadas();
		Afirmar("clicar num desejo COM alvo abre a segunda etapa, que PERGUNTA: 'Juventude pra quem?'",
				clicou && janela.EtapaDeTeste == "Resposta" && janela.PerguntaDesenhada == "Juventude pra quem?", $"{janela.EtapaDeTeste}: '{janela.PerguntaDesenhada}'");
		Afirmar("...com 'Para mim' em primeiro, e as pessoas ao alcance do dragao pra escolher (sem digitar nome nenhum)",
				resp.Count >= 4 && resp[0] is { Rotulo: "Para mim", Pode: true } && resp.Exists(r => r.Rotulo == "Kuririn" && r.Pode)
				&& resp.Exists(r => r.Rotulo == "Bulma" && r.Pode), string.Join(" | ", resp.Select(r => r.Rotulo)));
		int iKuririn = resp.FindIndex(r => r.Rotulo == "Kuririn"), iBulma = resp.FindIndex(r => r.Rotulo == "Bulma");
		Afirmar("...o alvo MARCADO do jogador vem na frente das outras pessoas, destacado e com a nota",
				iKuririn >= 0 && iKuririn < iBulma && resp[iKuririn].Marcado && !resp[iBulma].Marcado
				&& resp[iKuririn].Nota.Contains("alvo marcado", StringComparison.Ordinal), iKuririn >= 0 ? resp[iKuririn].Nota : "sem Kuririn");
		Afirmar("...e sem quem esta LONGE (Yamcha), sem o morto (Tenshinhan) e sem o morto de velhice (Mestre Kame)",
				!resp.Exists(r => r.Rotulo is "Yamcha" or "Tenshinhan" or "Mestre Kame"));
		// (na primeira foto desta etapa o "Voltar" fechava a lista de respostas e saia cortado pela rolagem.)
		Rect2 voltar = janela.RetanguloDoVoltar;
		Afirmar("...e o 'Voltar' mora no RODAPE, inteiro dentro da janela e ao lado do 'Fechar' -- na lista, onde nao ha pra onde voltar, ele nao existe",
				!voltarNaLista && voltar.Size.Y > 10 && janela.RetanguloDoPainel.Encloses(voltar),
				$"na lista: {voltarNaLista} | voltar {voltar} | janela {janela.RetanguloDoPainel}");
		Fotografar("desejos-03-pra-quem");

		// ------------------------------------------------------------ 5) fecha e reabre
		Nota("-- 5) fechar e reabrir pela aba World: a etapa e a contagem ficam --");
		janela.Fechar();
		yield return 0.4;
		Afirmar("fechar a janela a tira da tela -- e o dragao continua de pe no servidor", !janela.NaTela && srv.SetDoPalcoDeTeste(eu).DragaoDePe);
		menu.Abrir();
		menu.IrPara("World");
		yield return 0.5;
		bool apertou = ApertarNaAbaWorld("Ver os desejos");
		for (double t = 0; t < 3 && !janela.NaTela; t += 0.1) yield return 0.1;
		yield return 0.3;
		Afirmar("'Ver os desejos' da aba World REABRE a janela na MESMA etapa em que ela foi fechada, com a mesma contagem",
				apertou && janela.NaTela && janela.EtapaDeTeste == "Resposta" && janela.PerguntaDesenhada == "Juventude pra quem?"
				&& janela.ContagemDesenhada.Contains("2 de 3", StringComparison.Ordinal),
				$"apertou={apertou} naTela={janela.NaTela} {janela.EtapaDeTeste} '{janela.ContagemDesenhada}'");

		// ------------------------------------------------------------ 6) escolhe a pessoa
		Nota("-- 6) escolher a pessoa na lista, confirmar e pedir --");
		clicou = janela.ApertarResposta("Kuririn");
		yield return 0.3;
		conf = janela.ConfirmarDesenhado();
		Afirmar("clicar na pessoa leva ao CONFIRMAR, que diz o desejo e PRA QUEM",
				clicou && janela.EtapaDeTeste == "Confirmar" && conf.Nome == "Juventude" && conf.Quem.StartsWith("Kuririn", StringComparison.Ordinal), conf.ToString());
		janela.ApertarBotao("Pedir");
		for (double t = 0; t < 3 && srv.SetDoPalcoDeTeste(eu).Pedidos < 2; t += 0.1) yield return 0.1;
		yield return 0.5;
		Afirmar("PEDIR rejuvenesce A PESSOA ESCOLHIDA no servidor (60 -> 25 anos), e nao quem pediu",
				srv.CorpoDosDesejosDeTeste(marcado)?.Idade == Desejos.IdadeDaJuventude && srv.SetDoPalcoDeTeste(eu).Pedidos == 2,
				$"idade do alvo {srv.CorpoDosDesejosDeTeste(marcado)?.Idade}, pedidos {srv.SetDoPalcoDeTeste(eu).Pedidos}");
		Afirmar("...e a contagem da janela cai de novo (resta 1 de 3)", janela.ContagemDesenhada.Contains("1 de 3", StringComparison.Ordinal), janela.ContagemDesenhada);

		// ------------------------------------------------------------ 7) ressuscitar: o ultimo pedido
		Nota("-- 7) ressuscitar alguem: a lista dos mortos, e o ultimo pedido fecha a janela --");
		janela.ApertarLinha("reviver");
		yield return 0.4;
		resp = janela.RespostasDesenhadas();
		Afirmar("'Ressuscitar alguém' pergunta QUEM: o morto esta na lista, quem morreu de velhice NAO, e 'Para mim' vem apagado (\"você está vivo\")",
				janela.EtapaDeTeste == "Resposta" && resp.Exists(r => r.Rotulo == "Tenshinhan" && r.Pode) && !resp.Exists(r => r.Rotulo == "Mestre Kame")
				&& resp.Exists(r => r is { Rotulo: "Para mim", Pode: false, Nota: "você está vivo" }),
				string.Join(" | ", resp.Select(r => $"{r.Rotulo}{(r.Pode ? "" : " (apagado)")}")));
		Fotografar("desejos-04-ressuscitar-quem");
		Afirmar("a resposta apagada nao responde ao clique", !janela.ApertarResposta("Para mim") && janela.EtapaDeTeste == "Resposta");

		janela.ApertarResposta("Tenshinhan");
		yield return 0.3;
		conf = janela.ConfirmarDesenhado();
		Afirmar("o confirmar avisa que e o ULTIMO pedido da invocacao", conf.Custo.Contains("último pedido", StringComparison.Ordinal), conf.Custo);
		janela.ApertarBotao("Pedir");
		for (double t = 0; t < 4 && janela.NaTela; t += 0.1) yield return 0.1;
		yield return 0.3;
		Afirmar("o ultimo pedido e concedido (o morto VOLTA a vida no servidor) e o dragao vai embora",
				srv.CorpoDosDesejosDeTeste(morto) is { Morto: false } && !srv.SetDoPalcoDeTeste(eu).DragaoDePe);
		Afirmar("...e a janela FECHA SOZINHA: o dragao sumiu", !janela.NaTela);

		// ------------------------------------------------------------ 8) o contra-exemplo: a janela que nao fecha
		Nota("-- 8) dispensar o dragao -- e a janela que ignora o dragao indo embora (defeito injetado) --");
		srv.ReporODragaoDeTeste(eu);
		yield return 0.6;
		cli.SendVerbo("db_invocar");
		for (double t = 0; t < 4 && !janela.NaTela; t += 0.1) yield return 0.1;
		yield return 0.3;
		Afirmar("um dragao NOVO abre a janela na LISTA: a etapa em que a do dragao anterior parou nao vaza pra este",
				janela.NaTela && janela.EtapaDeTeste == "Lista" && janela.ContagemDesenhada.Contains("3 de 3", StringComparison.Ordinal),
				$"{janela.EtapaDeTeste} '{janela.ContagemDesenhada}'");

		MenuDeDesejos.FicaAbertaSemDragaoDeTeste = true;
		janela.ApertarLinha(AcoesDoDragao.Dispensar);
		yield return 0.3;
		janela.ApertarBotao("Pedir");
		for (double t = 0; t < 3 && srv.SetDoPalcoDeTeste(eu).DragaoDePe; t += 0.1) yield return 0.1;
		yield return 1.0;
		Afirmar("(defeito injetado: a janela ignora o dragao indo embora) o dragao foi embora no servidor e a janela CONTINUA na tela",
				!srv.SetDoPalcoDeTeste(eu).DragaoDePe && janela.NaTela);
		MenuDeDesejos.FicaAbertaSemDragaoDeTeste = false;
		janela.Fechar();
		yield return 0.3;

		srv.ReporODragaoDeTeste(eu);
		yield return 0.6;
		cli.SendVerbo("db_invocar");
		for (double t = 0; t < 4 && !janela.NaTela; t += 0.1) yield return 0.1;
		yield return 0.3;
		janela.ApertarLinha(AcoesDoDragao.Dispensar);
		yield return 0.3;
		conf = janela.ConfirmarDesenhado();
		janela.ApertarBotao("Pedir");
		for (double t = 0; t < 4 && janela.NaTela; t += 0.1) yield return 0.1;
		Afirmar("DISPENSAR o dragao pela janela (confirmando): ele vai embora sem gastar pedido, e a janela fecha sozinha",
				conf.Nome == "Dispensar o dragão" && !srv.SetDoPalcoDeTeste(eu).DragaoDePe && srv.SetDoPalcoDeTeste(eu).Pedidos == 0 && !janela.NaTela,
				$"confirmar '{conf.Nome}', pedidos {srv.SetDoPalcoDeTeste(eu).Pedidos}, naTela {janela.NaTela}");

		// ------------------------------------------------------------ 9) o botao "Derrubar a estatua" da aba
		Nota("-- 9) 'Derrubar a estátua' da aba World abre a janela ja na confirmacao --");
		menu.Abrir();
		menu.IrPara("World");
		yield return 0.5;
		apertou = ApertarNaAbaWorld("Derrubar a estátua");
		for (double t = 0; t < 3 && !janela.NaTela; t += 0.1) yield return 0.1;
		yield return 0.3;
		conf = janela.ConfirmarDesenhado();
		Afirmar("o botao abre a janela JA NO CONFIRMAR de derrubar -- e a estatua continua de pe (nada foi mandado)",
				apertou && janela.NaTela && janela.EtapaDeTeste == "Confirmar" && conf.Nome == "Derrubar a estátua" && srv.SetDoPalcoDeTeste(eu).Existe,
				$"apertou={apertou} {janela.EtapaDeTeste} '{conf.Nome}'");
		Fotografar("desejos-05-derrubar-a-estatua");
		janela.ApertarBotao("Voltar");
		yield return 0.3;
		linhas = janela.LinhasDesenhadas();
		Afirmar("'Voltar' devolve pra lista -- que, sem dragao de pe, vem toda apagada e diz por que",
				janela.EtapaDeTeste == "Lista" && srv.SetDoPalcoDeTeste(eu).Existe && linhas.Exists(l => l.Id == "dinheiro" && !l.Pode)
				&& !linhas.Exists(l => l.Id == AcoesDoDragao.Dispensar) && janela.EstadoDesenhado.Contains("não está de pé", StringComparison.Ordinal),
				janela.EstadoDesenhado);

		// ------------------------------------------------------------ 10) o Super Shenron
		// O SUPER SHENRON SO E CHAMADO AO LADO DE UMA SUPER ESFERA, NO ESPACO (`set src in oview(2)`,
		// `ProceduralSpace.dm:1580`). O portao e do servidor (`GameServer.DianteDeUmaSuper`) e quem o mede e a
		// `--validacoesteste`; daqui se mede a JANELA dos dois lados dele: longe, ela mostra e nao pede; ao lado,
		// pede. (Ate 2026-10-10 este passo pedia a riqueza de pe na Terra -- o furo que o portao fechou.)
		Nota("-- 10) o Super Shenron, LONGE de uma Super Esfera: a janela mostra e nao pede --");
		int ciclo = srv.SupersNaMaoDeTeste(eu);
		for (double t = 0; t < 3 && !janela.LinhasDesenhadas().Exists(l => l.Id == AcoesDoDragao.Super); t += 0.1) yield return 0.1;
		Afirmar("com as sete Super Esferas na mao, a janela ABERTA ganha sozinha o atalho 'Super Shenron' (o servidor a mantem em dia)",
				janela.LinhasDesenhadas().Exists(l => l.Id == AcoesDoDragao.Super && l.Pode));
		janela.ApertarLinha(AcoesDoDragao.Super);
		for (double t = 0; t < 3 && janela.NomeDesenhado != "SUPER SHENRON"; t += 0.1) yield return 0.1;
		yield return 0.3;

		// O CRITERIO DO LADO DE CA DO PORTAO, lido da TELA: os tres desejos apagados e a frase que diz onde ir.
		string[] desejosDoSuper = ["sdb_riqueza", "sdb_reviver", "sdb_supremo"];
		const string fraseDoPortao = "AO LADO de uma Super Esfera";
		bool LongeMostraENaoPede()
		{
			List<(string Id, string Nome, bool Pode, string Motivo)> l = janela.LinhasDesenhadas();
			return janela.NomeDesenhado == "SUPER SHENRON" && janela.EtapaDeTeste == "Lista"
				&& desejosDoSuper.All(id => l.Exists(x => x.Id == id && !x.Pode))
				&& janela.EstadoDesenhado.Contains(fraseDoPortao, StringComparison.Ordinal);
		}

		linhas = janela.LinhasDesenhadas();
		Afirmar("(a) LONGE de qualquer Super Esfera (de pe na Terra, com as sete e a Lingua dos Deuses) o atalho troca a janela pro SUPER SHENRON "
				+ "na hora -- com os tres desejos APAGADOS, a frase do portao no alto, e o gesto de emprestar as sete",
				LongeMostraENaoPede() && linhas.Exists(l => l.Id == AcoesDoDragao.Emprestar) && !janela.ApertarLinha("sdb_riqueza"),
				$"'{janela.NomeDesenhado}' | '{janela.EstadoDesenhado}' | {string.Join(",", linhas.Select(l => l.Id + (l.Pode ? "" : "(x)")))}");
		Fotografar("desejos-06-super-shenron-longe");

		// O PACOTE QUE A JANELA NAO MANDA (o cliente mexido): o servidor recusa, e diz onde se chama.
		const string recusaDoPortao = "ao lado de uma delas";
		double zeniNaTerra = srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? -1;
		List<string> ouvido = [];
		GameServer.EscutaDeAvisos = ouvido;
		cli.SendVerbo("sdb_invocar", "sdb_riqueza");
		for (double t = 0; t < 3 && !ouvido.Exists(f => f.Contains(recusaDoPortao, StringComparison.Ordinal)); t += 0.1) yield return 0.1;
		yield return 0.4;
		GameServer.EscutaDeAvisos = null;
		Afirmar("...e o pedido MANDADO ASSIM MESMO (o pacote que a janela nao manda) e RECUSADO pelo servidor, que diz onde se chama: "
				+ "nenhum zeni, o ciclo nao anda, as sete continuam com ele e a janela fica",
				ouvido.Exists(f => f.Contains(recusaDoPortao, StringComparison.Ordinal))
				&& Math.Abs((srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? 0) - zeniNaTerra) < 1 && srv.CicloDasSupersAgoraDeTeste == ciclo
				&& cli.MinhasSupers == SuperEsferas.Total && janela.NaTela,
				$"zeni {zeniNaTerra:N0} -> {srv.CorpoDosDesejosDeTeste(eu)?.Zeni:N0}, ciclo {ciclo} -> {srv.CicloDasSupersAgoraDeTeste}, "
				+ $"supers {cli.MinhasSupers}, ouviu: {string.Join(" | ", ouvido)}");

		// O CONTRA-EXEMPLO: o portao desligado pelo botao de defeito DELE. O servidor acerta a janela no tique
		// seguinte (1 Hz), e as duas metades do caso de cima reprovam: os desejos acendem, e o pacote entra.
		srv.SuperShenronDeQualquerLugarDeTeste = true;
		for (double t = 0; t < 3 && !janela.LinhasDesenhadas().Exists(l => l.Id == "sdb_riqueza" && l.Pode); t += 0.1) yield return 0.1;
		Afirmar("(defeito injetado: o portao de lugar desligado, `SuperShenronDeQualquerLugarDeTeste`) o MESMO criterio REPROVA -- "
				+ "de pe na Terra os desejos ACENDEM e a frase do portao some",
				!LongeMostraENaoPede() && janela.LinhasDesenhadas().Exists(l => l.Id == "sdb_riqueza" && l.Pode),
				$"'{janela.EstadoDesenhado}'");
		cli.SendVerbo("sdb_invocar", "sdb_riqueza");
		for (double t = 0; t < 3 && srv.CicloDasSupersAgoraDeTeste == ciclo; t += 0.1) yield return 0.1;
		yield return 0.4;
		srv.SuperShenronDeQualquerLugarDeTeste = false;
		Afirmar("(defeito injetado) ...e o mesmo pacote ENTRA de dentro de um planeta: +5.000.000 e as sete se consomem -- o que o servidor "
				+ "fazia ate 2026-10-10",
				Math.Abs((srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? 0) - (zeniNaTerra + Desejos.ZeniDoSuperDesejo)) < 1
				&& srv.CicloDasSupersAgoraDeTeste == ciclo + 1,
				$"zeni {zeniNaTerra:N0} -> {srv.CorpoDosDesejosDeTeste(eu)?.Zeni:N0}, ciclo {ciclo} -> {srv.CicloDasSupersAgoraDeTeste}");

		// ------------------------------------------------------------ 10b) ao lado de uma Super Esfera
		Nota("-- 10b) o Super Shenron, AO LADO de uma Super Esfera (no espaco): os desejos acendem e o pedido entra --");
		ciclo = srv.SupersNaMaoDeTeste(eu);   // as sete de novo (o defeito de cima as consumiu), nas posicoes do ciclo novo

		// ATE LA PELO CAMINHO DAS OUTRAS BANCADAS: a roupa espacial entra pela mochila (um humano sufoca em 20 s no
		// vacuo -- a `--diagnav` ja pagou por isso) e o corpo vai pelo `MoveToZone` do servidor ate a Super Esfera 1,
		// como na `--avessoteste` e na `--esferateste`. A UM TILE dela, e nao em cima: "ao lado" e estar dentro do
		// alcance (2 tiles), e e isso que se mede. (Na foto so ha espaco em volta do corpo: o cliente recebe a
		// posicao das Super Esferas por perto -- `GameClient.Supers` -- e hoje ninguem as desenha.)
		bool traje = srv.TrajeNaMochilaDeTeste(eu);
		Vec2 esfera = srv.OndeEstaASuper(1);
		srv.MoveToZone(eu, srv.ZonaDoEspaco, esfera + new Vec2(SuperEsferas.AlcanceDeInvocar * 0.5f, 0));
		for (double t = 0; t < 10 && !Espaco.EhEspaco(cli.Zone); t += 0.1) yield return 0.1;
		yield return 2.5;   // a tela de carregamento sai e o corpo assenta
		float ateAEsfera = M?.PosicaoLocal is { } corpo ? corpo.DistanceTo(new Vector2(esfera.X, esfera.Y)) : float.MaxValue;
		Afirmar("(montagem) o corpo esta no ESPACO, de roupa espacial, a menos de `AlcanceDeInvocar` (2 tiles) da Super Esfera 1 -- e continua com as sete",
				traje && Espaco.EhEspaco(cli.Zone) && ateAEsfera <= SuperEsferas.AlcanceDeInvocar && cli.MinhasSupers == SuperEsferas.Total,
				$"traje {traje}, zona '{cli.Zone.Name}', a {ateAEsfera:0} px da esfera (alcance {SuperEsferas.AlcanceDeInvocar:0}), supers {cli.MinhasSupers}");

		menu.Abrir();
		menu.IrPara("World");
		yield return 0.6;
		// (o cartao se acha pelo metadado do titulo, como a `--diagabas` os acha.)
		PanelContainer? cartaoDoSuper = menu.PaginaDeTeste("World") is { } abaNoEspaco
			? Todos(abaNoEspaco).OfType<PanelContainer>().FirstOrDefault(p => p.HasMeta("titulo") && p.GetMeta("titulo").AsString() == "O pedido ao Super Shenron")
			: null;
		Afirmar("no espaco o cartao do pedido e o do SUPER SHENRON: 'Ver os desejos' continua la, e 'Derrubar a estátua' nao (nao ha estatua no espaco)",
				cartaoDoSuper != null && BotaoDe(cartaoDoSuper, "Ver os desejos") != null && BotaoDe(cartaoDoSuper, "Derrubar a estátua") == null);
		apertou = ApertarNaAbaWorld("Ver os desejos");
		for (double t = 0; t < 3 && !(janela.NaTela && janela.NomeDesenhado == "SUPER SHENRON"); t += 0.1) yield return 0.1;
		yield return 0.4;
		linhas = janela.LinhasDesenhadas();
		Afirmar("(b) AO LADO de uma Super Esfera 'Ver os desejos' abre a janela do SUPER SHENRON com os desejos ACESOS e sem a frase do portao: "
				+ "SUPER SHENRON atende UM desejo",
				apertou && janela.NaTela && janela.NomeDesenhado == "SUPER SHENRON" && janela.EtapaDeTeste == "Lista"
				&& linhas.Exists(l => l.Id == "sdb_riqueza" && l.Pode) && linhas.Exists(l => l.Id == "sdb_supremo" && l.Pode)
				&& !janela.EstadoDesenhado.Contains(fraseDoPortao, StringComparison.Ordinal)
				&& janela.EstadoDesenhado.Contains("atende UM desejo", StringComparison.Ordinal),
				$"apertou={apertou} '{janela.NomeDesenhado}' | '{janela.EstadoDesenhado}' | {string.Join(",", linhas.Select(l => l.Id + (l.Pode ? "" : "(x)")))}");
		Fotografar("desejos-06-super-shenron");

		zeniAntes = srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? -1;
		janela.ApertarLinha("sdb_riqueza");
		yield return 0.3;
		janela.ApertarBotao("Pedir");
		for (double t = 0; t < 4 && janela.NaTela; t += 0.1) yield return 0.1;
		yield return 0.3;
		Afirmar("RIQUEZA COLOSSAL pela janela, ao lado da esfera: +5.000.000 no servidor, as sete se consomem (o ciclo anda) e a janela fecha",
				Math.Abs((srv.CorpoDosDesejosDeTeste(eu)?.Zeni ?? 0) - (zeniAntes + Desejos.ZeniDoSuperDesejo)) < 1
				&& srv.CicloDasSupersAgoraDeTeste == ciclo + 1 && !janela.NaTela,
				$"zeni {zeniAntes:N0} -> {srv.CorpoDosDesejosDeTeste(eu)?.Zeni:N0}, ciclo {ciclo} -> {srv.CicloDasSupersAgoraDeTeste}, naTela {janela.NaTela}");
	}
}
