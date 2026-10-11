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
/// E, JA QUE O PASSO 10 VAI ATE UMA SUPER ESFERA, ele mede tambem que ela APARECE (dono, 2026-10-11: *"faça as
/// super esferas aparecerem"*): longe nao ha nada; ao lado ela esta no pixel que o servidor diz, com a arte, as
/// estrelas e o dono; a posse que troca de mao troca a legenda; e as sete que se espalham somem da tela. Tres
/// contra-exemplos: o desenho desligado, a lista que nao acompanha a troca de zona, e a posse contada so a quem tomou.
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
		// (o roteiro desliga os de baixo no proprio passo; aqui e pra uma excecao no meio dele nao os deixar ligados.)
		World.SuperEsferasSemDesenhoDeTeste = SuperEsferaDesenhada.SobOAmbienteDeTeste = false;
		if (S is { } servidor)
			servidor.SuperShenronDeQualquerLugarDeTeste = servidor.SupersNaoAcompanhamAZonaDeTeste = servidor.PosseDaSuperSoPraQuemTomouDeTeste = false;
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

	/// <summary>O que esta na tela agora, sem gravar em disco. Nulo sem janela (headless nao renderiza).</summary>
	private Image? Capturar()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private Image? Fotografar(string nome)
	{
		Image? img = Capturar();
		if (img == null) { Nota($"sem foto pra {nome} (headless nao renderiza): rode com janela"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		_fotos.Add(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	/// <summary>
	/// QUANTO DE UM DISCO DA FOTO E LARANJA -- a cor da Super Esfera -- e onde cai o centro da mancha. E a regua
	/// de "ela esta DESENHADA ali": um node na arvore com a posicao certa nao e pixel na tela (ja se pagou por
	/// essa diferenca neste projeto), e o ceu do espaco, preto com estrelas miudas, nao tem laranja nenhum.
	/// O centro e o raio sao em pixels da foto.
	/// </summary>
	private static (float Fracao, Vector2 Centro) LaranjaNoDisco(Image? foto, Vector2 centro, float raio)
	{
		if (foto == null) return (0, default);

		int x0 = Math.Max(0, (int)(centro.X - raio)), x1 = Math.Min(foto.GetWidth() - 1, (int)(centro.X + raio));
		int y0 = Math.Max(0, (int)(centro.Y - raio)), y1 = Math.Min(foto.GetHeight() - 1, (int)(centro.Y + raio));
		long noDisco = 0, laranjas = 0;
		double somaX = 0, somaY = 0;
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				float dx = x - centro.X, dy = y - centro.Y;
				if (dx * dx + dy * dy > raio * raio) continue;
				noDisco++;

				Color c = foto.GetPixel(x, y);
				if (!(c.R > 0.55f && c.G > 0.18f && c.G < 0.78f && c.B < 0.40f && c.R > c.G + 0.08f)) continue;
				laranjas++;
				somaX += x;
				somaY += y;
			}

		return noDisco == 0 ? (0, default)
			: (laranjas / (float)noDisco, laranjas == 0 ? default : new Vector2((float)(somaX / laranjas), (float)(somaY / laranjas)));
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

		// ------------------------------------------------------------ 10b) as Super Esferas na tela
		// "faça as super esferas aparecerem" (dono, 2026-10-11). O servidor ja mandava as que estao no recorte de
		// chunk do corpo e o cliente nao desenhava nenhuma: o jogador era mandado pra "ao lado" de uma coisa que nao
		// existia na tela. Daqui pra baixo o robo vai ao espaco e mede o que se VE -- e so entao pede o desejo.
		Nota("-- 10b) as SUPER ESFERAS NA TELA: longe nao ha nada; ao lado, ela esta no pixel que o servidor diz --");
		World mundo = M!;
		ciclo = srv.SupersNaMaoDeTeste(eu);   // as sete de novo (o defeito de cima as consumiu), nas posicoes do ciclo novo

		// ATE LA PELO CAMINHO DAS OUTRAS BANCADAS: a roupa espacial entra pela mochila (um humano sufoca em 20 s no
		// vacuo -- a `--diagnav` ja pagou por isso) e o corpo vai pelo `MoveToZone` do servidor, como na
		// `--avessoteste` e na `--esferateste`.
		bool traje = srv.TrajeNaMochilaDeTeste(eu);
		Vec2 esfera = srv.OndeEstaASuper(1);
		var esferaNoMundo = new Vector2(esfera.X, esfera.Y);
		// A UM TILE DELA, e nao em cima: "ao lado" e estar dentro do alcance (2 tiles), e e isso que se mede.
		Vec2 aoLadoDela = esfera + new Vec2(SuperEsferas.AlcanceDeInvocar * 0.5f, 0);
		Vec2 longeDeTodas = srv.LongeDasSupersDeTeste(1);

		// ---- LONGE: fora do que o servidor manda nao se desenha nada
		srv.MoveToZone(eu, srv.ZonaDoEspaco, longeDeTodas);
		for (double t = 0; t < 10 && !Espaco.EhEspaco(cli.Zone); t += 0.1) yield return 0.1;
		yield return 2.5;   // a tela de carregamento sai e o corpo assenta
		Afirmar("(montagem) o corpo esta no ESPACO, de roupa espacial, num ponto FORA do recorte de chunk de todas as sete",
				traje && Espaco.EhEspaco(cli.Zone) && (longeDeTodas - esfera).Length > Espaco.ChunkPx * 2,
				$"traje {traje}, zona '{cli.Zone.Name}', a {(longeDeTodas - esfera).Length:0} px da Super Esfera 1");
		Afirmar("LONGE de todas, a lista do servidor vem VAZIA e nao ha Super Esfera nenhuma desenhada (o cliente nao sabe onde elas estao)",
				cli.Supers.Count == 0 && mundo.SuperEsferasDesenhadasDeTeste().Count == 0,
				$"{cli.Supers.Count} na lista, {mundo.SuperEsferasDesenhadasDeTeste().Count} desenhada(s)");

		// ---- AO LADO, CHEGANDO COMO QUEM DECOLA: a lista acompanha a troca de zona
		// O contra-exemplo vem primeiro, porque so se ve em quem chega com a lista vazia: sem o pacote na troca de
		// zona, o corpo pousa ao lado da esfera com a chunk ja carimbada e fica sem ve-la ate cruzar outra.
		srv.SupersNaoAcompanhamAZonaDeTeste = true;
		srv.ChegarAoEspacoComoQuemDecolaDeTeste(eu, aoLadoDela);
		yield return 2.5;
		Afirmar("(defeito injetado: a lista das Super so sai quando o corpo cruza uma chunk) quem chega como quem decola, AO LADO de uma, "
				+ "nao a recebe nem a ve -- o que o servidor fazia ate 2026-10-11",
				cli.Supers.Count == 0 && mundo.SuperEsferasDesenhadasDeTeste().Count == 0,
				$"{cli.Supers.Count} na lista, {mundo.SuperEsferasDesenhadasDeTeste().Count} desenhada(s)");
		srv.SupersNaoAcompanhamAZonaDeTeste = false;
		srv.ChegarAoEspacoComoQuemDecolaDeTeste(eu, aoLadoDela);
		for (double t = 0; t < 4 && !mundo.SuperEsferasDesenhadasDeTeste().Exists(e => e.Numero == 1); t += 0.1) yield return 0.1;
		yield return 2.5;   // o corpo assenta, e a camera com ele, antes de medir pixel

		float ateAEsfera = mundo.PosicaoLocal is { } corpo ? corpo.DistanceTo(esferaNoMundo) : float.MaxValue;
		Afirmar("(montagem) agora AO LADO: no espaco, a menos de `AlcanceDeInvocar` (2 tiles) da Super Esfera 1, com as sete",
				Espaco.EhEspaco(cli.Zone) && ateAEsfera <= SuperEsferas.AlcanceDeInvocar && cli.MinhasSupers == SuperEsferas.Total,
				$"zona '{cli.Zone.Name}', a {ateAEsfera:0} px da esfera (alcance {SuperEsferas.AlcanceDeInvocar:0}), supers {cli.MinhasSupers}");

		// ---- ELA ESTA NA TELA: o node, a arte, o tamanho, a legenda -- e o PIXEL
		List<(int Numero, Vector2 NoMundo, Vector2 NaTela, float Diametro, int Estrelas, string Legenda)> naTela = mundo.SuperEsferasDesenhadasDeTeste();
		(int Numero, Vector2 NoMundo, Vector2 NaTela, float Diametro, int Estrelas, string Legenda) uma = naTela.Find(e => e.Numero == 1);
		Afirmar("AO LADO de uma Super Esfera, chegando como quem decola, ela esta DESENHADA: uma por linha do pacote, cada uma no lugar que o "
				+ "pacote trouxe -- e a de 1 estrela exatamente onde o servidor a tem",
				naTela.Count > 0 && naTela.Count == cli.Supers.Count
				&& naTela.All(e => cli.Supers.Exists(s => s.Numero == e.Numero && s.Pos.DistanceTo(e.NoMundo) < 0.5f))
				&& uma.Numero == 1 && uma.NoMundo.DistanceTo(esferaNoMundo) < 0.5f,
				$"{naTela.Count} desenhada(s) de {cli.Supers.Count} na lista; a #1 em {uma.NoMundo}, servidor {esferaNoMundo}");
		Afirmar("...com a ARTE do original e as estrelas DELA (o quadro de 1 estrela do `SuperDragonball.dmi`), do tamanho do alcance: "
				+ "128 px de mundo de diametro, 4 tiles",
				uma.Estrelas == 1 && Math.Abs(uma.Diametro - 2 * SuperEsferas.AlcanceDeReivindicar) < 0.5f && Math.Abs(uma.Diametro - 128f) < 0.5f,
				$"quadro de {uma.Estrelas} estrela(s), diametro {uma.Diametro:0.#} px");
		Afirmar("...e a legenda diz as estrelas e o dono: '1 estrela · SUA'",
				uma.Legenda == "1 estrela · SUA" && uma.Legenda == SuperEsferaDesenhada.Legenda(1, cli.LocalName, true), uma.Legenda);

		Image? daEsfera = Fotografar("desejos-07-a-super-esfera");
		float zoom = mundo.ZoomDaCameraDeTeste;
		Vector2 pixelDoServidor = GetViewport().GetCanvasTransform() * esferaNoMundo;   // onde o servidor diz, pela camera
		float raioNaTela = SuperEsferaDesenhada.Raio * zoom;
		(float laranja, Vector2 centroDaMancha) = LaranjaNoDisco(daEsfera, pixelDoServidor, raioNaTela);
		Afirmar("...NA TELA: o node esta no pixel que a posicao do servidor da pela camera, dentro do quadro -- e na FOTO ha um disco laranja ali "
				+ "(mais da metade do disco, com o centro da mancha no centro dele)",
				uma.NaTela.DistanceTo(pixelDoServidor) < 1.5f && GetViewport().GetVisibleRect().HasPoint(pixelDoServidor)
				&& daEsfera != null && laranja > 0.5f && centroDaMancha.DistanceTo(pixelDoServidor) < raioNaTela * 0.2f,
				$"node {uma.NaTela}, servidor {pixelDoServidor}, zoom {zoom}; laranja em {laranja:P0} do disco, centro da mancha {centroDaMancha}");

		// O CONTRA-EXEMPLO DO DESENHO: o cliente de antes. A lista chega igual, e a tela fica vazia.
		World.SuperEsferasSemDesenhoDeTeste = true;
		mundo.RedesenharSupersDeTeste();
		yield return 0.3;
		(float semDesenho, _) = LaranjaNoDisco(Capturar(), pixelDoServidor, raioNaTela);
		Afirmar("(defeito injetado: o desenho desligado -- o cliente ate 2026-10-11) a lista do servidor continua la e NADA aparece: "
				+ "nenhum node, e nenhum laranja naquele disco da tela",
				cli.Supers.Exists(s => s.Numero == 1) && mundo.SuperEsferasDesenhadasDeTeste().Count == 0 && semDesenho < 0.05f,
				$"{cli.Supers.Count} na lista, {mundo.SuperEsferasDesenhadasDeTeste().Count} desenhada(s), laranja em {semDesenho:P0} do disco");
		World.SuperEsferasSemDesenhoDeTeste = false;
		mundo.RedesenharSupersDeTeste();
		yield return 0.2;
		Afirmar("...e desfeito o defeito ela volta pra tela", mundo.SuperEsferasDesenhadasDeTeste().Exists(e => e.Numero == 1));

		// O CONTRA-EXEMPLO DA LUZ PROPRIA: sob o ambiente do espaco (um crepusculo parado) o node esta la, no
		// lugar certo -- e o laranja nao chega a tela. A foto fica, pro dono comparar as duas.
		SuperEsferaDesenhada.SobOAmbienteDeTeste = true;
		mundo.RedesenharSupersDeTeste();
		yield return 0.3;
		(float apagada, _) = LaranjaNoDisco(Fotografar("desejos-07-sem-luz-propria"), pixelDoServidor, raioNaTela);
		Afirmar("(defeito injetado: a esfera sob o ambiente do espaco, como os planetas) o node continua desenhado no lugar e a MESMA regua "
				+ "de pixel reprova: o crepusculo parado leva o laranja pra marrom",
				mundo.SuperEsferasDesenhadasDeTeste().Exists(e => e.Numero == 1 && e.NoMundo.DistanceTo(esferaNoMundo) < 0.5f) && apagada < 0.05f,
				$"laranja em {apagada:P0} do disco (com luz propria: {laranja:P0})");
		SuperEsferaDesenhada.SobOAmbienteDeTeste = false;
		mundo.RedesenharSupersDeTeste();
		yield return 0.2;

		// ---- O CUSTO: a lista se refaz por PACOTE, nunca por quadro
		// (o corpo nao tem o Dragon Radar: no tique de segundo do servidor nao sai pacote nenhum pra ele.)
		ulong[] nodes = mundo.NodesDasSupersDeTeste();
		yield return 1.0;
		bool osMesmos = nodes.Length > 0 && nodes.SequenceEqual(mundo.NodesDasSupersDeTeste());
		mundo.RedesenharSupersDeTeste();
		bool aReguaVeUmRemonte = !nodes.SequenceEqual(mundo.NodesDasSupersDeTeste());
		Afirmar("um segundo parado (dezenas de quadros, nenhum pacote) e os nodes desenhados sao OS MESMOS: nada se remonta por quadro -- "
				+ "e a regua ve um remonte quando ha um (o redesenho forcado troca todos)",
				osMesmos && aReguaVeUmRemonte, $"{nodes.Length} node(s); os mesmos {osMesmos}, a regua ve {aReguaVeUmRemonte}");

		// ---- O RADAR E A TELA DIZEM O MESMO LUGAR
		// O Dragon Radar entra so agora: com ele o tique de segundo do servidor manda a lista de novo sempre que a
		// frase muda, e as cenas de cima (o pacote que acompanha a troca de zona) deixariam de medir a troca de zona.
		bool semAparelhoSemFrase = cli.SinalDourado.Length == 0;
		srv.RadarNaMochilaDeTeste(eu, true);
		for (double t = 0; t < 4 && cli.SinalDourado.Length == 0; t += 0.1) yield return 0.1;
		yield return 0.2;
		string frase = cli.SinalDourado;
		naTela = mundo.SuperEsferasDesenhadasDeTeste();
		// A FRASE FALA NA UNIDADE DA ABA WORLD: o ponto vem como a linha "Coordenadas" o escreveria (tiles), e nao em
		// pixels do mundo -- senao quem segue a frase olhando a propria posicao na aba procura no lugar errado.
		static string NaAba(Vector2 p) => Jandirus.Core.World.MapaDoLugar.Coordenadas(new Jandirus.Core.World.Vec2(p.X, p.Y));
		bool AFraseDaOLugarDaAba(string f) => naTela.Exists(e => f.Contains("perto de " + NaAba(e.NoMundo), StringComparison.Ordinal));
		Afirmar("sem o Dragon Radar a frase do radar dourado nao vem (a bola na tela nao faz o papel do aparelho); COM ele, ao lado da "
				+ "esfera, a frase diz 'NESTA célula' e da o MESMO ponto em que ela esta desenhada, nas coordenadas da aba World",
				semAparelhoSemFrase && frase.Contains("NESTA célula", StringComparison.Ordinal) && AFraseDaOLugarDaAba(frase),
				$"sem aparelho: {(semAparelhoSemFrase ? "sem frase" : "COM frase")}; com ele: '{frase}'; desenhada(s) em "
				+ string.Join(" ", naTela.Select(e => $"#{e.Numero}[{NaAba(e.NoMundo)}]")));
		try
		{
			Jandirus.Core.Magic.SuperEsferas.RadarEmPixelsDeTeste = true;
			for (double t = 0; t < 4 && cli.SinalDourado == frase; t += 0.1) yield return 0.1;
			string emPixels = cli.SinalDourado;
			Afirmar("(defeito injetado: a frase do radar em PIXELS) o ponto da frase deixa de ser o da aba World, e a checagem de cima REPROVA",
					emPixels != frase && emPixels.Contains("NESTA célula", StringComparison.Ordinal) && !AFraseDaOLugarDaAba(emPixels), $"'{emPixels}'");
		}
		finally { Jandirus.Core.Magic.SuperEsferas.RadarEmPixelsDeTeste = false; }
		for (double t = 0; t < 4 && cli.SinalDourado != frase; t += 0.1) yield return 0.1;
		srv.RadarNaMochilaDeTeste(eu, false);
		for (double t = 0; t < 4 && cli.SinalDourado.Length > 0; t += 0.1) yield return 0.1;

		// ---- DE LONGE: fora do anel ela ja esta inteira na tela -- e dali o dragao ainda nao atende
		// O corpo vai pra cima e pra esquerda dela, pra a bola cair embaixo e a direita do meio da tela: e o pedaco
		// sem HUD (a caixa de fala mora embaixo a esquerda, o boneco de vida em cima a direita), e a regua de pixel
		// mede o disco sem nada na frente.
		Vec2 deLonge = esfera + new Vec2(-100, -40);
		srv.MoveToZone(eu, srv.ZonaDoEspaco, deLonge);
		yield return 2.5;
		for (double t = 0; t < 3 && !mundo.SuperEsferasDesenhadasDeTeste().Exists(e => e.Numero == 1); t += 0.1) yield return 0.1;
		yield return 0.5;
		uma = mundo.SuperEsferasDesenhadasDeTeste().Find(e => e.Numero == 1);
		float deQuantoLonge = mundo.PosicaoLocal is { } corpoLa ? corpoLa.DistanceTo(esferaNoMundo) : 0f;
		Image? fotoDeLonge = Fotografar("desejos-08-de-longe");
		Vector2 pixelDeLonge = GetViewport().GetCanvasTransform() * esferaNoMundo;
		(float laranjaDeLonge, Vector2 manchaDeLonge) = LaranjaNoDisco(fotoDeLonge, pixelDeLonge, raioNaTela);
		Afirmar("DE LONGE (a mais de 3 tiles: fora do alcance) ela esta INTEIRA na tela, no pixel que o servidor diz -- e sem nada na frente "
				+ "o disco e laranja em mais de 3/4, com o centro da mancha no centro dele",
				deQuantoLonge > SuperEsferas.AlcanceDeInvocar * 1.5f && uma.Numero == 1 && uma.NaTela.DistanceTo(pixelDeLonge) < 1.5f
				&& GetViewport().GetVisibleRect().Grow(-raioNaTela).HasPoint(pixelDeLonge)
				&& fotoDeLonge != null && laranjaDeLonge > 0.75f && manchaDeLonge.DistanceTo(pixelDeLonge) < raioNaTela * 0.12f,
				$"a {deQuantoLonge:0} px dela; node {uma.NaTela}, servidor {pixelDeLonge}; laranja em {laranjaDeLonge:P0} do disco, "
				+ $"centro da mancha {manchaDeLonge}");

		// DALI O DRAGAO AINDA NAO ATENDE: a bola esta na tela e o corpo esta FORA do anel dela -- o anel e o alcance.
		const string recusaNoEspaco = "ao seu alcance";
		List<string> ouvidoDeLonge = [];
		GameServer.EscutaDeAvisos = ouvidoDeLonge;
		cli.SendVerbo("sdb_invocar", "sdb_riqueza");
		for (double t = 0; t < 3 && !ouvidoDeLonge.Exists(f => f.Contains(recusaNoEspaco, StringComparison.Ordinal)); t += 0.1) yield return 0.1;
		yield return 0.3;
		GameServer.EscutaDeAvisos = null;
		Afirmar("...e de la, FORA do anel, o pedido e recusado (\"não há Super Esfera ao seu alcance\"): as sete continuam com ele e o ciclo "
				+ "nao anda -- DENTRO do anel ele entra (o 10c)",
				ouvidoDeLonge.Exists(f => f.Contains(recusaNoEspaco, StringComparison.Ordinal))
				&& cli.MinhasSupers == SuperEsferas.Total && srv.CicloDasSupersAgoraDeTeste == ciclo,
				$"supers {cli.MinhasSupers}, ciclo {ciclo} -> {srv.CicloDasSupersAgoraDeTeste}, ouviu: {string.Join(" | ", ouvidoDeLonge)}");

		// ...e de volta pro lado dela.
		srv.MoveToZone(eu, srv.ZonaDoEspaco, aoLadoDela);
		yield return 2.5;
		for (double t = 0; t < 3 && !mundo.SuperEsferasDesenhadasDeTeste().Exists(e => e.Numero == 1); t += 0.1) yield return 0.1;

		// ---- DE QUEM ELA E: a posse troca de mao com alguem olhando
		string LegendaDaUm() => mundo.SuperEsferasDesenhadasDeTeste().Find(e => e.Numero == 1).Legenda ?? "";
		srv.PosseDaSuperSoPraQuemTomouDeTeste = true;
		string ladrao = srv.OutroTomaASuperDeTeste(1);
		yield return 1.5;
		Afirmar("(defeito injetado: a troca de dono so e contada a quem tomou) outro toma a esfera na frente do jogador e a tela continua "
				+ "dizendo SUA, com o placar em 7 -- o que o servidor fazia ate 2026-10-11",
				ladrao.Length > 0 && LegendaDaUm() == "1 estrela · SUA" && cli.MinhasSupers == SuperEsferas.Total,
				$"legenda '{LegendaDaUm()}', placar {cli.MinhasSupers}");
		srv.PosseDaSuperSoPraQuemTomouDeTeste = false;
		srv.SupersNaMaoDeTeste(eu);   // as sete de volta (e o pacote que acerta a tela)
		yield return 0.5;
		ladrao = srv.OutroTomaASuperDeTeste(1);
		for (double t = 0; t < 3 && LegendaDaUm() == "1 estrela · SUA"; t += 0.1) yield return 0.1;
		Afirmar("OUTRO toma a esfera com o jogador olhando: a legenda troca pra 'de Freeza' NA HORA e o placar dele cai pra 6, sem o corpo "
				+ "sair do lugar",
				LegendaDaUm() == $"1 estrela · de {ladrao}" && cli.MinhasSupers == SuperEsferas.Total - 1,
				$"legenda '{LegendaDaUm()}', placar {cli.MinhasSupers}");
		ciclo = srv.SupersNaMaoDeTeste(eu);   // ...e as sete de novo com ele, pro pedido
		for (double t = 0; t < 3 && cli.MinhasSupers != SuperEsferas.Total; t += 0.1) yield return 0.1;

		// ---- e, com a esfera na tela, o Super Shenron
		Nota("-- 10c) o Super Shenron, AO LADO da Super Esfera: os desejos acendem e o pedido entra --");
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

		// ---- AS SETE SE ESPALHARAM: a desenhada vai junto
		for (double t = 0; t < 3 && mundo.SuperEsferasDesenhadasDeTeste().Exists(e => e.NoMundo.DistanceTo(esferaNoMundo) < 1f); t += 0.1) yield return 0.1;
		naTela = mundo.SuperEsferasDesenhadasDeTeste();
		Vec2 nova = srv.OndeEstaASuper(1);
		Afirmar("as sete se espalharam com o desejo: a esfera desenhada SOME do lugar antigo junto, e o que fica na tela e so o que o servidor "
				+ "manda agora, nos lugares novos",
				!naTela.Exists(e => e.NoMundo.DistanceTo(esferaNoMundo) < 1f) && (nova - esfera).Length > 1000
				&& naTela.Count == cli.Supers.Count
				&& naTela.All(e => { Vec2 p = srv.OndeEstaASuper(e.Numero); return e.NoMundo.DistanceTo(new Vector2(p.X, p.Y)) < 0.5f; }),
				$"{naTela.Count} desenhada(s) de {cli.Supers.Count} na lista; a #1 foi de {esferaNoMundo} pra ({nova.X:0}, {nova.Y:0})");
	}
}
