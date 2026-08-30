using System.Collections;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A PREVIA FOTOGRAFADA (`--fotoinstalar`) ============================
/// O dono pediu, com estas palavras: *"basicamente uma versao **transparente** dele vai ficar **no
/// mouse** como um preview de como vai ficar quando instalar naquele local"*.
///
/// As duas palavras marcadas sao de OLHO, e a `--diaginstalar` nao alcanca nenhuma das duas: ela le
/// `Modulate.A` (um campo) e `Position` (outro campo). Este projeto ja assinou *"o corpo esta
/// branco"* lendo um uniform e a foto mostrou 0,0% de branco -- **campo escrito nao e pixel
/// desenhado**. Um `Modulate` com alfa 0,55 num sprite que o motor desenha por cima de um shader
/// opaco, ou num node cujo pai esta invisivel, da o mesmo campo e nenhuma transparencia.
/// ============================================================================================
///
/// ============================ COMO A TRANSPARENCIA E MEDIDA SEM MODELO DE COR ============================
/// A tentacao era conferir a mistura na conta: `pixel = a*textura + (1-a)*fundo`. **Nao serve.** Esta
/// tela tem `CanvasModulate` da hora do dia, veu de clima, zoom inteiro da camera e sRGB no meio --
/// e uma prova que precise acertar tudo isso vira uma prova sobre a minha algebra, nao sobre o jogo.
///
/// A pergunta e respondida sem cor nenhuma: **o mesmo desenho, em dois fundos diferentes.**
///
///   * se o fantasma fosse OPACO, os pixels dele seriam identicos nos dois lugares -- o fundo nao
///     atravessa. A diferenca entre as duas fotos, na area dele, seria ZERO;
///   * se ele fosse INVISIVEL, a diferenca seria a diferenca dos fundos, inteira;
///   * sendo translucido, ela e uma FRACAO dela -- e a fracao e `1 - alfa`.
///
/// Entao a bancada mede `razao = diferenca(desenhoA, desenhoB) / diferenca(fundoA, fundoB)` e cobra
/// que ela caia no meio: nem 0 (opaco), nem 1 (nao desenhou nada). O numero medido vai pro relatorio
/// ao lado do alfa que o codigo pediu, e os dois tem que conversar.
///
/// A PRE-CONDICAO E COBRADA ANTES: os dois fundos precisam ser DIFERENTES entre si, muito acima do
/// ruido. Em cima de dois tiles de grama identicos a razao seria 0/0 e a prova ficaria verde sem
/// medir nada -- que e o modo de falha que este arquivo existe pra evitar.
/// =====================================================================================================
///
/// ============================ E O RUIDO E MEDIDO, NAO SUPOSTO ============================
/// O mundo se mexe entre duas fotos: agua, clima, corpos, a hora. Entao a primeira coisa que a
/// bancada faz e tirar DUAS fotos da mesma cena e medir a diferenca entre elas. Esse numero e o
/// chao: toda afirmacao de "mudou" pede varias vezes o ruido, e toda afirmacao de "nao mudou" pede
/// ficar perto dele. Sem essa medida, "3 de diferenca" nao quer dizer nada.
/// ====================================================================================
///
/// ============================ O MOUSE PRECISA ANDAR DE VERDADE ============================
/// `Viewport.PushInput(InputEventMouseMotion)` **nao move o cursor**: pro viewport da janela,
/// `GetMousePosition()` responde do `DisplayServer`, e nao do ultimo evento empurrado. Foi o que
/// derrubou a primeira versao da `--diaginstalar`, e e por isso que la a prova virou "o desenho esta
/// na celula do cursor" em vez de "o desenho acompanhou o cursor".
///
/// Aqui o cursor anda pra valer, com `Input.WarpMouse`. Duas consequencias, e as duas estao tratadas:
/// a janela PRECISA existir (no headless o `GetImage` volta vazio de qualquer jeito), e o cursor do
/// dono e movido -- por isso o `.bat` abre no SEGUNDO monitor e este arquivo devolve o ponteiro pra
/// onde ele estava ao terminar.
/// ======================================================================================
///
/// COMO RODAR (precisa de janela; abra no segundo monitor):
///     Godot --path . --host --rede 7976 --techteste --fotoinstalar --horateste 0.5
///           --position 1920,0 --resolution 1280x720
///           --raca Human --conta bancada_fotoinstalar --nome Fotografo
///
/// As fotos saem em `user://instalar-*.png`. Comece pela tira `instalar-E-tira.png`.
/// </summary>
public partial class RoboDeFotoDoInstalar : Node
{
	private static GameClient? C => GameClient.Instance;
	private static TelaDeConstrucao? Obra => TelaDeConstrucao.Instancia;

	/// <summary>O mesmo cobaia da `--diaginstalar`: barato, de chao e com arte convertida.</summary>
	private const string DoChao = "Punching_Bag";

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];
	private readonly List<string> _linhas = [];

	private void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"  ok    {nome}"); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
	}

	private static void Nota(string l) => GD.Print("[fotoinstalar] " + l);

	// =====================================================================
	// O MOTOR
	// =====================================================================
	private IEnumerator? _roteiro;
	private double _espera = 5.0;
	private bool _acabou;
	private Vector2I _mouseDoDono;

	public override void _Ready()
	{
		// ONDE O PONTEIRO DO DONO ESTAVA. Ele volta pra ca no fim -- esta bancada mexe no cursor de
		// verdade, e devolver o que se pegou emprestado e o minimo.
		_mouseDoDono = DisplayServer.MouseGetPosition();
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext()) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;

		// O PONTEIRO VOLTA. `WarpMouse` fala em coordenada da JANELA, e o que eu guardei e de TELA --
		// a diferenca e a posicao da janela, e ela importa porque este `.bat` abre no monitor 2.
		Vector2I janela = DisplayServer.WindowGetPosition();
		Input.WarpMouse(new Vector2(_mouseDoDono.X - janela.X, _mouseDoDono.Y - janela.Y));

		// O ROTEIRO CHEGOU AO FIM? Se nao, isto e uma VERMELHA -- ver `_chegouAoFim`. Ela e cobrada
		// aqui, e nao dentro do roteiro, justamente porque o caso que ela pega e o roteiro NAO
		// chegar a rodar mais nenhuma linha.
		Checa("Z.19 o roteiro andou ate a ultima linha (nenhuma excecao no meio)", _chegouAoFim,
			  "a corrotina parou antes do fim -- procure a excecao no log ACIMA deste placar; "
			  + "as provas que faltaram nao reprovaram, elas NAO RODARAM");

		GD.Print("\n[fotoinstalar] ---- as medidas ----");
		foreach (string l in _linhas) GD.Print("   " + l);
		GD.Print($"\n[fotoinstalar] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[fotoinstalar] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	// =====================================================================
	// A CAMERA
	// =====================================================================
	/// <summary>
	/// UM QUADRO DE POUSIO ANTES DE CADA FOTO -- e ele nao e generosidade.
	///
	/// `GetViewport().GetTexture().GetImage()` chamado dentro do `_Process` devolve o quadro
	/// **anterior**, o que ja foi pintado. Fotografar no mesmo quadro em que a previa nasceu grava a
	/// tela de antes dela com o relatorio dizendo (com razao) que ela existe. Ver a mesma nota, e o
	/// mesmo bug medido, no `RoboDeFotoDaPose`.
	/// </summary>
	private const double Pousio = 0.25;

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	private void Gravar(Image? img, string arquivo)
	{
		if (img == null) return;
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + arquivo);
			img.SavePng(caminho);
			_linhas.Add($"foto: {caminho}");
		}
		catch (Exception e) { Nota($"sem foto {arquivo}: {e.Message}"); }
	}

	/// <summary>
	/// A DIFERENCA MEDIA entre duas imagens do mesmo tamanho, so nos pixels da MASCARA.
	///
	/// A mascara e o desenho do fantasma: os texels em que a arte e opaca. Sem ela, um sprite pequeno
	/// dentro de um quadro de 32x32 quase todo vazio diluiria o sinal no vazio em volta, e o numero
	/// diria "quase nao mudou" sobre um desenho perfeitamente visivel.
	///
	/// Devolve 0..255 (escala de byte), que e a unidade em que da pra conversar sobre pixel.
	/// </summary>
	private static double Diferenca(Image a, Image b, bool[,]? mascara)
	{
		int w = Math.Min(a.GetWidth(), b.GetWidth());
		int h = Math.Min(a.GetHeight(), b.GetHeight());
		double soma = 0;
		int n = 0;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (mascara != null && (x >= mascara.GetLength(0) || y >= mascara.GetLength(1) || !mascara[x, y]))
					continue;
				Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
				soma += (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B)) / 3.0;
				n++;
			}
		return n == 0 ? 0 : soma / n * 255.0;
	}

	/// <summary>O recorte, empurrado pra dentro da tela -- nunca cortado, ou os tamanhos deixam de bater.</summary>
	private static Rect2I DentroDaTela(Image tela, Rect2I r)
	{
		int w = Math.Min(r.Size.X, tela.GetWidth());
		int h = Math.Min(r.Size.Y, tela.GetHeight());
		int x = Math.Clamp(r.Position.X, 0, tela.GetWidth() - w);
		int y = Math.Clamp(r.Position.Y, 0, tela.GetHeight() - h);
		return new Rect2I(x, y, w, h);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		Nota("comecando -- esta bancada PRECISA de janela.");

		if (DisplayServer.GetName() == "headless")
		{
			Checa("Z.0 ha uma janela pra fotografar", false,
				  "rodando headless: o `GetImage` volta vazio e nada aqui mede pixel. "
				  + "Rode pelo `ver-o-instalar.bat`.");
			yield break;
		}

		// -----------------------------------------------------------------
		// 0 -- UM SACO DE PANCADA NA MOCHILA
		// -----------------------------------------------------------------
		if ((C?.Mochila.Quantos(DoChao) ?? 0) == 0)
		{
			C?.SendTech("construir", DoChao);
			for (int i = 0; i < 40 && (C?.Mochila.Quantos(DoChao) ?? 0) == 0; i++) yield return 0.1;
		}
		Checa("Z.1 tenho o item na mochila pra segurar a previa",
			  (C?.Mochila.Quantos(DoChao) ?? 0) > 0);
		if ((C?.Mochila.Quantos(DoChao) ?? 0) == 0) yield break;

		// -----------------------------------------------------------------
		// 1 -- DOIS LUGARES BONS, E COM FUNDOS DIFERENTES
		// -----------------------------------------------------------------
		// A escolha usa a regra do CLIENTE (o que a previa aprova) e depois cobra a diferenca de
		// FUNDO nas fotos. Se todos os candidatos tiverem o mesmo chao, a prova da transparencia
		// seria 0/0 -- e ela reprova dizendo isso, em vez de ficar verde sem medir.
		var candidatos = new List<(int cx, int cy)>();
		{
			(int mcx, int mcy) = CatalogoDeObras.Celula(Eu().X, Eu().Y);
			for (int dy = -2; dy <= 2; dy++)
				for (int dx = -2; dx <= 2; dx++)
				{
					if (dx == 0 && dy == 0) continue;
					int cx = mcx + dx, cy = mcy + dy;
					if (TelaDeConstrucao.RecusaEm(cx, cy) == RecusaDeAssento.Pode) candidatos.Add((cx, cy));
				}
		}
		Nota($"{candidatos.Count} celula(s) que a previa aprova em volta de mim");
		Checa("Z.2 achei pelo menos dois lugares que a previa aprova", candidatos.Count >= 2,
			  $"{candidatos.Count} -- o berco esta entulhado");
		if (candidatos.Count < 2) yield break;

		// -----------------------------------------------------------------
		// 2 -- O RUIDO: DUAS FOTOS DA MESMA CENA
		// -----------------------------------------------------------------
		Obra?.Largar();
		yield return Pousio;
		Image? p0 = Tela();
		yield return 0.4;
		Image? p0b = Tela();
		Checa("Z.3 a tela virou imagem", p0 != null && p0b != null);
		if (p0 == null || p0b == null) yield break;

		double ruido = Diferenca(p0, p0b, null);
		_linhas.Add($"ruido de fundo (duas fotos da MESMA cena): {ruido:0.00} / 255");
		Gravar(p0, "instalar-A-sem-previa.png");

		// -----------------------------------------------------------------
		// 3 -- A PREVIA NO PRIMEIRO LUGAR
		// -----------------------------------------------------------------
		Obra?.Segurar(DoChao);
		yield return 0.2;
		if (Obra?.FantasmaNaTela is not { } fantasma)
		{ Checa("Z.4 a previa nasceu", false); yield break; }
		Checa("Z.4 a previa nasceu", true);

		float alfaPedido = fantasma.Modulate.A;
		Vector2 tamTex = fantasma.Texture?.GetSize() ?? Vector2.Zero;
		_linhas.Add($"alfa que o codigo pediu: {alfaPedido:0.00}   textura: {tamTex.X}x{tamTex.Y}");

		// A MASCARA sai da propria arte: so os texels opacos entram na conta.
		bool[,]? mascaraA = null;

		// ============================ FORA DA CAIXA DE CHAT -- ISTO CUSTOU UMA VERMELHA ============================
		// As provas daqui pra baixo comparam o MESMO retangulo de tela em fotos tiradas em momentos
		// diferentes, e concluem da diferenca que a previa e translucida. A conta so vale se a UNICA
		// coisa que mudou ali dentro foi a previa.
		//
		// A caixa de chat quebra isso, e quebrou de verdade: o proprio `Segurar` escreve *"clique onde
		// quer instalar. Botao direito ou Esc cancela"*, entao entre a foto do fundo e a foto com a
		// previa nasce uma LINHA DE TEXTO no rodape. Quando o segundo lugar caiu em cima do painel, a
		// medida somou o texto ao desenho: a razao foi a 0,875 e o `Z.17` acusou alfa 0,125 onde o
		// codigo pede 0,55 -- **e a foto mostrava "Esc cancela" atravessado no recorte**. O painel
		// ainda muda de opacidade sozinho quando alguem digita (`Chat._painel.Modulate`), entao nem
		// esperar o texto assentar resolveria.
		//
		// A resposta nao e afrouxar o limiar do Z.17 -- seria calibrar a regua pela medida errada. E
		// medir onde a pergunta faz sentido: no MUNDO. A caixa vem do proprio `Chat` (o unico
		// `PanelContainer` dele) e nao de um numero digitado, entao ela acompanha se o painel mudar
		// de tamanho ou de canto.
		// ========================================================================================================
		Rect2 caixaDoChat = CaixaDoChatNaTela();
		int antesDoFiltro = candidatos.Count;
		candidatos = candidatos
			.Where(c => !caixaDoChat.Intersects(RetanguloDaCelula(fantasma, c.cx, c.cy).Grow(2)))
			.ToList();
		Nota($"{candidatos.Count} de {antesDoFiltro} celula(s) sobraram fora da caixa de chat");
		Checa("Z.4b sobraram dois lugares fora da interface pra medir pixel",
			  candidatos.Count >= 2,
			  $"{candidatos.Count} -- a caixa de chat cobre quase tudo em volta");
		if (candidatos.Count < 2) yield break;

		(int acx, int acy) = candidatos[0];
		foreach (object _ in Apontar(acx, acy)) yield return 0.0;
		yield return Pousio;
		Rect2I retA = RetanguloNaTela(fantasma);
		Image? p1 = Tela();
		Checa("Z.5 a previa esta ancorada na celula pra onde o CURSOR foi", NaCelula(fantasma, acx, acy),
			  $"celula ({acx},{acy}), previa em {fantasma.Position}");

		// -----------------------------------------------------------------
		// 4 -- E NO SEGUNDO
		// -----------------------------------------------------------------
		(int bcx, int bcy) = EscolherSegundo(candidatos, acx, acy, fantasma, p0, retA);
		foreach (object _ in Apontar(bcx, bcy)) yield return 0.0;
		yield return Pousio;
		Rect2I retB = RetanguloNaTela(fantasma);
		Image? p2 = Tela();
		Checa("Z.6 e ela foi PRA CELULA NOVA quando o cursor andou", NaCelula(fantasma, bcx, bcy),
			  $"celula ({bcx},{bcy}), previa em {fantasma.Position}");

		if (p1 == null || p2 == null) { Checa("Z.7 as fotos com previa sairam", false); yield break; }
		Gravar(p1, "instalar-B-previa-lugar-1.png");
		Gravar(p2, "instalar-C-previa-lugar-2.png");

		// -----------------------------------------------------------------
		// 5 -- A MASCARA, E ELA TEM QUE SAIR *ANTES* DO ESC
		// -----------------------------------------------------------------
		// ============================ ISTO AQUI JA FOI UM VERDE MENTIROSO ============================
		// A mascara sai da TEXTURA do fantasma, e o Esc **libera o fantasma** (`Largar()` chama
		// `QueueFree()`). Enquanto este bloco morava depois do Esc, o `Mascara(fantasma, ...)`
		// tocava um `Sprite2D` ja destruido e estourava `ObjectDisposedException` -- a corrotina
		// morria ali, as DEZ provas de pixel (Z.9 a Z.18) nunca rodavam, e o placar imprimia
		// **"7 OK, 0 FALHA(S)"**. Verde, com a metade que importa nao tendo acontecido.
		//
		// E era a metade que importa: Z.10 a Z.18 sao as unicas que olham o PIXEL DESENHADO. As
		// sete que sobravam liam campo (`Modulate`, `Position`) -- exatamente a bancada que mede
		// INTENCAO e nao RESULTADO. Nada mudou nas contas; so a ORDEM.
		// =========================================================================================
		GD.Print("\n--- a leitura dos pixels ---");

		retA = DentroDaTela(p0, retA);
		retB = DentroDaTela(p0, retB);
		int lado = Math.Min(Math.Min(retA.Size.X, retB.Size.X), Math.Min(retA.Size.Y, retB.Size.Y));
		if (lado < 4) { Checa("Z.9 o retangulo da previa tem tamanho util", false, $"{lado} px"); yield break; }
		retA = new Rect2I(retA.Position, new Vector2I(lado, lado));
		retB = new Rect2I(retB.Position, new Vector2I(lado, lado));
		_linhas.Add($"retangulo da previa na tela: A={retA}  B={retB}");

		mascaraA = Mascara(fantasma, lado);

		// -----------------------------------------------------------------
		// 6 -- O ESC, E A TELA DE VOLTA AO QUE ERA
		// -----------------------------------------------------------------
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
		yield return 0.3;
		Image? p3 = Tela();
		Gravar(p3, "instalar-D-depois-do-esc.png");
		Checa("Z.8 o Esc largou a previa", Obra?.NaMao == "", $"na mao '{Obra?.NaMao}'");

		Image fundoA = p0.GetRegion(retA), fundoB = p0.GetRegion(retB);
		Image desenhoA = p1.GetRegion(retA), desenhoB = p2.GetRegion(retB);
		Image semPreviaEmA = p2.GetRegion(retA);     // a previa ja saiu de A nesta foto
		Image depoisDoEsc = p3?.GetRegion(retB) ?? fundoB;

		double dFundos = Diferenca(fundoA, fundoB, mascaraA);
		double dDesenhos = Diferenca(desenhoA, desenhoB, mascaraA);
		double apareceuEmA = Diferenca(desenhoA, fundoA, mascaraA);
		double apareceuEmB = Diferenca(desenhoB, fundoB, mascaraA);
		double sobrouEmA = Diferenca(semPreviaEmA, fundoA, mascaraA);
		double sobrouDepoisDoEsc = Diferenca(depoisDoEsc, fundoB, mascaraA);

		_linhas.Add($"fundo A x fundo B          : {dFundos:0.00}");
		_linhas.Add($"desenho A x desenho B      : {dDesenhos:0.00}");
		_linhas.Add($"previa apareceu em A       : {apareceuEmA:0.00}");
		_linhas.Add($"previa apareceu em B       : {apareceuEmB:0.00}");
		_linhas.Add($"sobrou em A depois de sair : {sobrouEmA:0.00}");
		_linhas.Add($"sobrou depois do Esc       : {sobrouDepoisDoEsc:0.00}");

		// ---- 6a: ELA E DESENHADA (senao "transparente" seria satisfeito por nao desenhar nada) ----
		double limiar = Math.Max(3.0, ruido * 4);
		Checa("Z.10 a previa MUDOU os pixels do lugar onde ela esta", apareceuEmA > limiar,
			  $"{apareceuEmA:0.00} de mudanca, precisava passar de {limiar:0.00} (ruido {ruido:0.00})");
		Checa("Z.11 e no segundo lugar tambem", apareceuEmB > limiar, $"{apareceuEmB:0.00} > {limiar:0.00}");

		// ---- 6b: ELA SEGUE O CURSOR -- no pixel: saiu de onde estava ----
		Checa("Z.12 e quando o cursor andou, ela SUMIU do lugar antigo (no pixel)",
			  sobrouEmA <= limiar,
			  $"sobrou {sobrouEmA:0.00} em A depois que o cursor foi pra B (ruido {ruido:0.00})");
		Checa("Z.13 os dois retangulos sao lugares DIFERENTES da tela", retA.Position != retB.Position,
			  $"{retA.Position} == {retB.Position}");

		// ---- 6c: ELA E TRANSPARENTE -- a razao ----
		// A PRE-CONDICAO PRIMEIRO: sem fundos diferentes, a razao seria 0/0.
		Checa("Z.14 os dois fundos sao mesmo diferentes (senao a prova da transparencia e vazia)",
			  dFundos > limiar, $"{dFundos:0.00} de diferenca entre os fundos, ruido {ruido:0.00}");

		if (dFundos > limiar)
		{
			double razao = dDesenhos / dFundos;
			double esperado = 1.0 - alfaPedido;
			_linhas.Add($"razao medida (1 - alfa efetivo): {razao:0.000}   "
						+ $"alfa efetivo ~ {1 - razao:0.000}   alfa pedido {alfaPedido:0.00}");

			// OPACO SERIA ZERO. Esta e a linha que o pedido do dono cobra: da pra ver o chao por baixo.
			Checa("Z.15 o fundo ATRAVESSA a previa (ela nao e opaca)", razao > 0.10,
				  $"razao {razao:0.000} -- perto de zero e sprite opaco");

			// E NAO SUMIDA: razao 1 seria "nao desenhou nada".
			Checa("Z.16 ...e ela nao e invisivel (nao e so o fundo)", razao < 0.90,
				  $"razao {razao:0.000} -- perto de um e nada desenhado");

			// E O ALFA MEDIDO CONVERSA COM O PEDIDO. Folga larga de proposito: no meio ha
			// `CanvasModulate`, veu de clima e sRGB, e a prova nao e sobre a minha algebra.
			Checa("Z.17 e o alfa medido no pixel conversa com o que o codigo pediu",
				  Math.Abs(razao - esperado) < 0.30,
				  $"medido {1 - razao:0.000}, pedido {alfaPedido:0.00} (razao {razao:0.000} x esperado {esperado:0.000})");
		}

		// ---- 6d: O ESC LIMPOU ----
		Checa("Z.18 depois do Esc a tela voltou a ser a de antes (no pixel)",
			  sobrouDepoisDoEsc <= limiar,
			  $"sobrou {sobrouDepoisDoEsc:0.00} onde a previa estava (ruido {ruido:0.00})");

		// -----------------------------------------------------------------
		// 7 -- A TIRA, que e o formato pra o olho
		// -----------------------------------------------------------------
		Tira("instalar-E-tira.png", [fundoA, desenhoA, fundoB, desenhoB, depoisDoEsc]);
		_linhas.Add("tira: fundo A | previa em A | fundo B | previa em B | depois do Esc");

		// A ULTIMA LINHA DO ROTEIRO -- ver `_chegouAoFim`.
		_chegouAoFim = true;
	}

	/// <summary>
	/// O ROTEIRO ANDOU ATE A ULTIMA LINHA?
	///
	/// ============================ SEM ISTO, MORRER NO MEIO SE LE COMO SUCESSO ============================
	/// O motor desta bancada e uma corrotina puxada pelo `_Process`. Quando uma linha dela estoura, o
	/// Godot registra a excecao no log e o iterador fica exaurido -- e o `MoveNext()` do quadro
	/// seguinte devolve `false`, que e **exatamente o que uma corrotina que terminou bem devolve**.
	/// Dai o `Fim()` imprimia o placar com o que tinha dado tempo de medir e um alegre "0 FALHA(S)".
	///
	/// Foi o que aconteceu de verdade: o `Mascara` tocou o fantasma ja liberado pelo Esc, as dez
	/// provas de pixel nao rodaram, e o placar disse "7 OK, 0 FALHA(S)". Ninguem lendo aquilo teria
	/// como saber que a bancada nao chegou ao fim -- a diferenca entre 17 provas e 7 nao esta escrita
	/// em lugar nenhum quando se ve so o total.
	///
	/// Agora o placar so pode fechar em verde se esta marca estiver de pe. Uma excecao no meio vira
	/// uma linha VERMELHA com o nome do que faltou, que e o que ela sempre deveria ter sido.
	/// ==================================================================================================
	/// </summary>
	private bool _chegouAoFim;

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	private static Vector2 Eu() => World.Instancia?.PosicaoLocal ?? Vector2.Zero;

	/// <summary>
	/// LEVA O CURSOR ATE O MEIO DE UMA CELULA -- de verdade, com `Input.WarpMouse`.
	///
	/// Espera alguns quadros porque o warp so vira evento de mouse no quadro seguinte, e o `_Process`
	/// da `TelaDeConstrucao` so entao recoloca o fantasma.
	/// </summary>
	private IEnumerable Apontar(int cx, int cy)
	{
		if (World.Instancia is not { } mundo) yield break;
		const int t = ZoneCollision.TileSize;
		var noMundo = new Vector2(cx * t + t / 2f, cy * t + t / 2f);
		Vector2 naJanela = mundo.GetCanvasTransform() * noMundo;
		Input.WarpMouse(naJanela);
		for (int i = 0; i < 4; i++) yield return 0.0;
		yield return 0.15;
	}

	private static bool NaCelula(Sprite2D f, int cx, int cy)
	{
		if (f.Texture == null) return false;
		const int t = ZoneCollision.TileSize;
		return Mathf.IsEqualApprox(f.Position.X, cx * t)
			&& Mathf.IsEqualApprox(f.Position.Y, (cy + 1) * t - f.Texture.GetHeight());
	}

	/// <summary>
	/// ONDE, NA TELA, O FANTASMA ESTA DESENHADO.
	///
	/// `GetGlobalTransformWithCanvas` ja carrega o zoom da camera (que neste jogo e INTEIRO e
	/// configuravel), entao o tamanho sai daqui e nao do tamanho da textura: em zoom 3 o sprite de 32
	/// px ocupa 96 px de tela, e recortar 32 pegaria um canto dele.
	/// </summary>
	private static Rect2I RetanguloNaTela(Sprite2D f)
	{
		Transform2D t = f.GetGlobalTransformWithCanvas();
		Vector2 tam = (f.Texture?.GetSize() ?? new Vector2(32, 32)) * t.Scale;
		return new Rect2I((int)t.Origin.X, (int)t.Origin.Y, (int)tam.X, (int)tam.Y);
	}

	/// <summary>
	/// A MASCARA DO DESENHO: quais pixels do recorte sao arte, e nao vazio em volta.
	///
	/// Sai da textura da propria previa, ampliada pelo zoom. Sem ela, um sprite pequeno num quadro
	/// quase vazio diluiria toda medida no nada.
	/// </summary>
	private static bool[,]? Mascara(Sprite2D f, int lado)
	{
		if (f.Texture?.GetImage() is not { } arte) return null;
		var m = new bool[lado, lado];
		int quantos = 0;
		for (int y = 0; y < lado; y++)
			for (int x = 0; x < lado; x++)
			{
				int tx = Math.Clamp(x * arte.GetWidth() / lado, 0, arte.GetWidth() - 1);
				int ty = Math.Clamp(y * arte.GetHeight() / lado, 0, arte.GetHeight() - 1);
				m[x, y] = arte.GetPixel(tx, ty).A > 0.5f;
				if (m[x, y]) quantos++;
			}
		// ARTE TODA VAZIA NAO VIRA MASCARA VAZIA: sem pixel nenhum a conta daria 0 e tudo ficaria
		// verde. Nesse caso e melhor medir o quadro inteiro e dizer o numero mais fraco.
		return quantos < 16 ? null : m;
	}

	/// <summary>
	/// O SEGUNDO LUGAR: o candidato mais LONGE do primeiro, pra os dois retangulos nao se encavalarem
	/// na tela (se encavalassem, "sumiu do lugar antigo" mediria o novo desenho).
	/// </summary>
	/// <summary>
	/// O SEGUNDO LUGAR PRA POR A PREVIA -- e ele e escolhido pelo FUNDO, nao pela distancia.
	///
	/// ============================ POR QUE A DISTANCIA NAO SERVIA ============================
	/// A prova da transparencia (Z.15 a Z.17) e uma RAZAO: quanto do desenho muda quando so o fundo
	/// embaixo dele muda. Ela precisa de dois fundos DIFERENTES -- com dois fundos iguais a conta
	/// vira 0/0, e o `Z.14` (que existe pra isso) fecha a porta e as tres provas nem rodam.
	///
	/// E era o que acontecia: esta funcao pegava o candidato mais LONGE, e longe num descampado de
	/// grama e outro tufo de grama igual. O placar dizia "fundo A x fundo B: 0,00" e a unica prova
	/// de que a previa e mesmo translucida ficava de fora da rodada -- sem reprovar, o que e pior.
	///
	/// Agora ela pergunta o que a prova precisa: **qual candidato tem o fundo mais diferente do de
	/// A**, medido na propria foto que ja foi tirada (`p0`, a tela sem previa). Nao ha dado novo --
	/// so a pergunta certa sobre o dado que ja estava na mao.
	///
	/// A distancia continua no desempate, e ela nao e enfeite: quando o chao e mesmo uniforme
	/// (todos empatam em zero) o comportamento antigo volta inteiro, e o Z.13 -- "os dois
	/// retangulos sao lugares diferentes da tela" -- continua de pe.
	/// ====================================================================================
	/// </summary>
	private static (int, int) EscolherSegundo(List<(int cx, int cy)> cand, int acx, int acy,
											  Sprite2D f, Image? semPrevia, Rect2I retA)
	{
		(int cx, int cy) melhor = cand[0];
		double melhorDif = -1;
		int melhorD = -1;

		Image? fundoA = semPrevia != null ? semPrevia.GetRegion(DentroDaTela(semPrevia, retA)) : null;

		foreach ((int cx, int cy) c in cand)
		{
			int d = Math.Abs(c.cx - acx) + Math.Abs(c.cy - acy);
			if (d == 0) continue;

			double dif = 0;
			if (fundoA != null && semPrevia != null)
			{
				Rect2I r = DentroDaTela(semPrevia, RetanguloDaCelula(f, c.cx, c.cy));
				// SO COMPARA RECORTE DO MESMO TAMANHO -- o `Diferenca` anda pelo menor dos dois, e
				// um recorte cortado na beirada da tela daria uma diferenca que nao e de cor.
				if (r.Size == fundoA.GetSize()) dif = Diferenca(fundoA, semPrevia.GetRegion(r), null);
			}

			if (dif > melhorDif + 0.01 || (Math.Abs(dif - melhorDif) <= 0.01 && d > melhorD))
			{ melhorDif = dif; melhorD = d; melhor = c; }
		}
		return melhor;
	}

	/// <summary>
	/// ONDE A CAIXA DE CHAT ESTA NA TELA -- pra nenhuma prova de pixel medir interface.
	///
	/// Ela sai do proprio `Chat` (o unico <see cref="PanelContainer"/> dentro dele, o `_painel`), e
	/// nao de coordenadas digitadas aqui: no dia em que o painel mudar de canto ou de tamanho, esta
	/// funcao acompanha sozinha. Rect vazio quer dizer "nao achei" -- e ai nada e excluido, que e o
	/// lado certo de errar (a prova fica mais dificil, nunca mais facil).
	/// </summary>
	private static Rect2 CaixaDoChatNaTela()
	{
		if (Chat.Instancia is not { } chat) return new Rect2();
		var fila = new Queue<Node>();
		fila.Enqueue(chat);
		while (fila.Count > 0)
		{
			Node n = fila.Dequeue();
			if (n is PanelContainer p && p.Visible) return p.GetGlobalRect();
			foreach (Node f in n.GetChildren()) fila.Enqueue(f);
		}
		return new Rect2();
	}

	/// <summary>
	/// O RETANGULO NA TELA que a previa ocuparia NESTA celula -- perguntado sem move-la de verdade.
	///
	/// Ela e posta la, medida pelo mesmo <see cref="RetanguloNaTela"/> que o roteiro usa, e devolvida
	/// ao lugar antes de qualquer quadro ser desenhado -- entao o jogador (e a foto) nunca ve o
	/// desvio. Reusar a funcao de medida em vez de refazer a algebra e o que garante que o retangulo
	/// escolhido aqui e o mesmo que sera medido depois.
	/// </summary>
	private static Rect2I RetanguloDaCelula(Sprite2D f, int cx, int cy)
	{
		Vector2 guardado = f.Position;
		const int t = ZoneCollision.TileSize;
		f.Position = new Vector2(cx * t, (cy + 1) * t - (f.Texture?.GetSize().Y ?? t));
		Rect2I r = RetanguloNaTela(f);
		f.Position = guardado;
		return r;
	}

	/// <summary>Os recortes colados lado a lado, 3x, pra o olho julgar o que o numero afirmou.</summary>
	private void Tira(string arquivo, Image[] partes)
	{
		var uteis = partes.Where(p => p != null).ToList();
		if (uteis.Count == 0) return;

		const int Vao = 6, Escala = 3;
		int lado = uteis[0].GetWidth();
		int largura = (lado * uteis.Count + Vao * (uteis.Count - 1)) * Escala;
		Image colagem = Image.CreateEmpty(largura, lado * Escala, false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < uteis.Count; i++)
		{
			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados e CALA quando nao tem.
			Image copia = Image.CreateEmpty(uteis[i].GetWidth(), uteis[i].GetHeight(), false, Image.Format.Rgba8);
			copia.BlitRect(uteis[i], new Rect2I(0, 0, uteis[i].GetWidth(), uteis[i].GetHeight()), Vector2I.Zero);
			copia.Resize(lado * Escala, lado * Escala, Image.Interpolation.Nearest);
			colagem.BlitRect(copia, new Rect2I(0, 0, copia.GetWidth(), copia.GetHeight()),
							 new Vector2I(i * (lado * Escala + Vao * Escala), 0));
		}
		Gravar(colagem, arquivo);
	}
}
